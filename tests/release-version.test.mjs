import { afterEach, test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { readVersion, updateVersions } from '../scripts/release-version.mjs';

const roots = [];
afterEach(() => {
  for (const root of roots.splice(0)) rmSync(root, { recursive: true, force: true });
});

function fixture() {
  const root = mkdtempSync(join(tmpdir(), 'mod-version-'));
  roots.push(root);
  const files = {
    'package/manifest.json': '{"version_number":"1.0.1","description":"Valheim 1.0.12","dependencies":["Other-1.0.1"]}\n',
    'src/ExampleMod/ExampleMod.csproj': '<Project><Version>1.0.1</Version><Reference Version="1.0.1" /></Project>\n',
    'src/ExampleMod/Plugin.cs': '\uFEFFpublic const string PluginVersion = "1.0.1";\r\n',
    'src/ExampleMod/Properties/AssemblyInfo.cs': '// [assembly: AssemblyVersion("1.0.*")]\n[assembly: AssemblyVersion("1.0.1.0")]\n[assembly: AssemblyFileVersion("1.0.1.0")]\n// Valheim 1.0.12\n',
  };
  for (const [name, text] of Object.entries(files)) {
    mkdirSync(dirname(join(root, name)), { recursive: true });
    writeFileSync(join(root, name), text);
  }
  return { root, files };
}

test('updates all mod versions while preserving dependencies, game versions and formatting', () => {
  const { root } = fixture();
  assert.equal(updateVersions('2.3.4', root).length, 4);
  assert.equal(readVersion(root), '2.3.4');
  assert.equal(readFileSync(join(root, 'package/manifest.json'), 'utf8'), '{"version_number":"2.3.4","description":"Valheim 1.0.12","dependencies":["Other-1.0.1"]}\n');
  assert.equal(readFileSync(join(root, 'src/ExampleMod/ExampleMod.csproj'), 'utf8'), '<Project><Version>2.3.4</Version><Reference Version="1.0.1" /></Project>\n');
  assert.equal(readFileSync(join(root, 'src/ExampleMod/Plugin.cs'), 'utf8'), '\uFEFFpublic const string PluginVersion = "2.3.4";\r\n');
  assert.equal(readFileSync(join(root, 'src/ExampleMod/Properties/AssemblyInfo.cs'), 'utf8'), '// [assembly: AssemblyVersion("1.0.*")]\n[assembly: AssemblyVersion("2.3.4.0")]\n[assembly: AssemblyFileVersion("2.3.4.0")]\n// Valheim 1.0.12\n');
});

test('unchanged version supports tagging the first release without a version bump', () => {
  const { root } = fixture();
  assert.deepEqual(updateVersions('1.0.1', root), []);
});

for (const version of ['1.0.1-rc.1', '01.2.3', '1.2', '65535.0.0', 'invalid']) test(`rejects unsupported version ${version} without edits`, () => {
  const { root, files } = fixture();
  assert.throws(() => updateVersions(version, root));
  for (const [name, text] of Object.entries(files)) assert.equal(readFileSync(join(root, name), 'utf8'), text);
});

for (const problem of ['inconsistent', 'duplicate']) test(`rejects ${problem} metadata before any writes`, () => {
  const { root, files } = fixture();
  const name = 'src/ExampleMod/Properties/AssemblyInfo.cs';
  files[name] = problem === 'inconsistent'
    ? files[name].replace('AssemblyFileVersion("1.0.1.0")', 'AssemblyFileVersion("1.0.0.0")')
    : files[name] + '[assembly: AssemblyFileVersion("1.0.1.0")]\n';
  writeFileSync(join(root, name), files[name]);
  assert.throws(() => updateVersions('1.0.2', root));
  for (const [name, text] of Object.entries(files)) assert.equal(readFileSync(join(root, name), 'utf8'), text);
});
