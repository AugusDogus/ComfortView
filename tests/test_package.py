"""Exercise release rejection paths without game files or publishing credentials."""

import importlib.util
import json
import os
from pathlib import Path
import struct
import tempfile
import unittest
from unittest.mock import patch
import zipfile

spec = importlib.util.spec_from_file_location(
    "package", Path(__file__).resolve().parents[1] / "scripts/validate-package.py")
package = importlib.util.module_from_spec(spec)
spec.loader.exec_module(package)


class PackageTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        project = self.root / "src/Example"
        (project / "Properties").mkdir(parents=True)
        (self.root / "package").mkdir()
        self.manifest = {
            "name": "Example", "version_number": "1.2.3", "description": "Example mod",
            "website_url": "https://github.com/example/Example",
            "dependencies": ["denikson-BepInExPack_Valheim-5.4.2350"],
        }
        (self.root / "package/manifest.json").write_text(json.dumps(self.manifest))
        (self.root / "package/icon.png").write_bytes(
            b"\x89PNG\r\n\x1a\n" + b"\x00\x00\x00\rIHDR" + struct.pack(">II", 256, 256))
        (self.root / "package/banner.png").write_bytes(b"banner")
        (self.root / "README.md").write_text(
            '<img src="package/banner.png">\n[Development](docs/DEVELOPMENT.md)\n[Controls](#controls)')
        (project / "Example.csproj").write_text(
            '<Project><PropertyGroup><Version>1.2.3</Version><TargetFramework>net8.0</TargetFramework>'
            '</PropertyGroup></Project>')
        (project / "Plugin.cs").write_text(
            '[BepInPlugin(PluginId, "Example", PluginVersion)]\npublic const string PluginVersion = "1.2.3";')
        (project / "Properties/AssemblyInfo.cs").write_text(
            '[assembly: AssemblyVersion("1.2.3.0")]\n[assembly: AssemblyFileVersion("1.2.3.0")]')
        self.dll = project / "bin/Release/net8.0/Example.dll"
        self.dll.parent.mkdir(parents=True)
        self.dll.write_bytes(b"compiled plugin")
        self.environment = patch.dict(os.environ, {"RELEASE_TAG": ""})
        self.environment.start()
        self.addCleanup(self.environment.stop)

    def test_package_contains_only_mod_and_resolves_readme_links(self):
        with patch.dict(os.environ, {"RELEASE_TAG": "v1.2.3"}):
            _, archive = package.validate(self.root, self.dll)
        package.validate(self.root)
        with zipfile.ZipFile(archive) as result:
            self.assertEqual([n for n in result.namelist() if n.endswith(".dll")], ["plugins/Example.dll"])
            readme = result.read("README.md").decode()
            self.assertIn('src="banner.png"', readme)
            self.assertIn('https://github.com/example/Example/blob/main/docs/DEVELOPMENT.md', readme)
            self.assertIn('[Controls](#controls)', readme)

    def test_rejects_mismatched_tag_before_creating_package(self):
        with patch.dict(os.environ, {"RELEASE_TAG": "v1.2.4"}):
            with self.assertRaisesRegex(ValueError, "Release tag"):
                package.validate(self.root, self.dll)
        self.assertFalse((self.root / "artifacts").exists())

    def test_rejects_bundled_dependency(self):
        (self.root / "package/assembly_valheim.dll").write_bytes(b"game")
        with self.assertRaisesRegex(ValueError, "Keep DLLs out"):
            package.validate(self.root, self.dll)

    def test_rejects_unexpected_archive_files(self):
        _, archive = package.validate(self.root, self.dll)
        with zipfile.ZipFile(archive, "a") as result:
            result.writestr("plugins/BepInEx.dll", b"dependency")
        with self.assertRaisesRegex(ValueError, "ZIP contents differ"):
            package.validate(self.root)

    def test_rejects_stale_plugin(self):
        package.validate(self.root, self.dll)
        self.dll.write_bytes(b"new build")
        with self.assertRaisesRegex(ValueError, "differs from its source"):
            package.validate(self.root)

    def test_rejects_inconsistent_version(self):
        path = self.root / "src/Example/Plugin.cs"
        path.write_text(path.read_text().replace('"1.2.3"', '"1.2.4"'))
        with self.assertRaisesRegex(ValueError, "PluginVersion"):
            package.validate(self.root, self.dll)

    def test_rejects_invalid_icon(self):
        (self.root / "package/icon.png").write_bytes(b"invalid")
        with self.assertRaisesRegex(ValueError, "256 by 256"):
            package.validate(self.root, self.dll)


if __name__ == "__main__":
    unittest.main()
