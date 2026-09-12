"""Build and verify the exact Thunderstore ZIP used locally and in CI."""

import argparse
import json
import os
from pathlib import Path
import re
import struct
import sys
import xml.etree.ElementTree as ET
import zipfile


def metadata(root: Path):
    manifest = json.loads((root / "package/manifest.json").read_text())
    version = manifest["version_number"]
    if not re.fullmatch(r"(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)", version):
        raise ValueError("package/manifest.json must contain a three-part numeric version.")
    if any(int(part) > 65534 for part in version.split(".")):
        raise ValueError("Version components must be at most 65534 for .NET assembly versions.")
    if not re.fullmatch(r"[A-Za-z0-9_]+", manifest["name"]):
        raise ValueError("The package name must contain only letters, numbers, and underscores.")
    if not isinstance(manifest["description"], str) or len(manifest["description"]) > 250:
        raise ValueError("The package description must be at most 250 characters.")
    if not re.fullmatch(r"https://github\.com/[\w.-]+/[\w.-]+", manifest["website_url"]):
        raise ValueError("Set website_url to this mod's GitHub repository URL.")
    dependencies = manifest["dependencies"]
    if not isinstance(dependencies, list) or not all(
        isinstance(item, str) and re.fullmatch(r"\w+-\w+-\d+\.\d+\.\d+", item)
        for item in dependencies
    ):
        raise ValueError("Dependencies must use OWNER-PACKAGE-MAJOR.MINOR.PATCH.")
    if sum(item.startswith("denikson-BepInExPack_Valheim-") for item in dependencies) != 1:
        raise ValueError("Declare exactly one BepInExPack_Valheim dependency.")

    projects = list((root / "src").glob("*/*.csproj"))
    if len(projects) != 1:
        raise ValueError("Expected one plugin project at src/<Mod>/<Mod>.csproj.")
    project = projects[0]
    tree = ET.parse(project)
    if tree.findtext(".//Version") != version:
        raise ValueError("The project Version must match package/manifest.json. Run bumpp before tagging.")
    plugin = (project.parent / "Plugin.cs").read_text(encoding="utf-8-sig")
    versions = re.findall(r'public const string PluginVersion\s*=\s*"([^"]+)";', plugin)
    if versions != [version] or not re.search(r"\[BepInPlugin\([^\n]*, PluginVersion\)\]", plugin):
        raise ValueError("BepInPlugin must use PluginVersion, matching package/manifest.json.")
    assembly = (project.parent / "Properties/AssemblyInfo.cs").read_text(encoding="utf-8-sig")
    for attribute in ("AssemblyVersion", "AssemblyFileVersion"):
        values = re.findall(rf'^\[assembly: {attribute}\("([^"]+)"\)\]', assembly, re.MULTILINE)
        if values != [f"{version}.0"]:
            raise ValueError(f"{attribute} must match the package version with a .0 revision.")
    tag = os.environ.get("RELEASE_TAG", "")
    if tag and tag != f"v{version}":
        raise ValueError(f"Release tag {tag!r} must be 'v{version}'. No package was published.")

    icon = (root / "package/icon.png").read_bytes()
    if (icon[:8] != b"\x89PNG\r\n\x1a\n" or len(icon) < 24
            or struct.unpack(">II", icon[16:24]) != (256, 256)):
        raise ValueError("package/icon.png must be a 256 by 256 PNG.")
    name = tree.findtext(".//AssemblyName") or project.stem
    framework = tree.findtext(".//TargetFramework")
    dll = project.parent / f"bin/Release/{framework}/{name}.dll"
    archive = root / f"artifacts/{manifest['name']}-{version}.zip"
    return manifest, dll, archive


def package_files(root: Path, manifest, dll: Path):
    files = {str(path.relative_to(root / "package")): path.read_bytes()
             for path in sorted((root / "package").rglob("*")) if path.is_file()}
    if any(name.lower().endswith(".dll") for name in files):
        raise ValueError("Keep DLLs out of package/. Only the built plugin may be distributed.")
    readme = (root / "README.md").read_text()

    def link(match):
        prefix, target, suffix = match.groups()
        if target.startswith("package/"):
            target = target.removeprefix("package/")
        elif not re.match(r"(?:[a-z]+:|#|/)", target):
            target = f"{manifest['website_url']}/blob/main/{target}"
        return prefix + target + suffix

    # Keep one README source while making package assets and repository links resolve.
    readme = re.sub(r'(\]\()([^\s)]+)(\))', link, readme)
    readme = re.sub(r'(src=")([^"]+)(")', link, readme)
    files["README.md"] = readme.encode()
    for name in ("CHANGELOG.md", "LICENSE.md"):
        path = root / name
        if path.exists():
            files[name] = path.read_bytes()
    font_license = root / "assets/artwork/FONT-LICENSE.txt"
    if font_license.exists():
        files["FONT-LICENSE.txt"] = font_license.read_bytes()
    files[f"plugins/{dll.name}"] = dll.read_bytes()
    return files


def validate(root: Path, assembly: Path | None = None):
    manifest, dll, archive = metadata(root)
    if assembly is not None and assembly.resolve() != dll.resolve():
        raise ValueError("Package requires the Release build. Run dotnet build -c Release -t:Package.")
    expected = package_files(root, manifest, dll)
    if assembly is not None:
        archive.parent.mkdir(parents=True, exist_ok=True)
        temporary = archive.with_suffix(".zip.tmp")
        try:
            with zipfile.ZipFile(temporary, "w", zipfile.ZIP_DEFLATED) as package:
                for name, content in expected.items():
                    package.writestr(name, content)
            temporary.replace(archive)
        finally:
            temporary.unlink(missing_ok=True)
    with zipfile.ZipFile(archive) as package:
        if package.testzip() is not None:
            raise ValueError("ZIP integrity check failed. Rebuild the package.")
        if sorted(package.namelist()) != sorted(expected):
            raise ValueError("ZIP contents differ from the expected plugin and metadata. Rebuild the package.")
        for name, content in expected.items():
            if package.read(name) != content:
                raise ValueError(f"Packaged {name} differs from its source. Rebuild the package.")
    return manifest, archive


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--assembly", type=Path, help="Build a ZIP from this Release assembly before validating.")
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    try:
        manifest, archive = validate(root, args.assembly)
    except (ValueError, KeyError, TypeError, OSError, ET.ParseError, zipfile.BadZipFile) as error:
        print(f"Package validation failed: {error}", file=sys.stderr)
        sys.exit(1)
    if output := os.environ.get("GITHUB_OUTPUT"):
        with open(output, "a") as stream:
            stream.write(f"version={manifest['version_number']}\n")
            stream.write(f"name={manifest['name']}\n")
            stream.write(f"archive={archive.relative_to(root)}\n")
    print(f"Validated {archive.relative_to(root)}.")
