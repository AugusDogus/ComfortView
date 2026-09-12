import { afterEach, expect, test } from 'bun:test';
import { existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { Uint8ArrayReader, Uint8ArrayWriter, ZipReader, ZipWriter } from '@zip.js/zip.js';
import { validate } from '../scripts/validate-package.ts';

const roots: string[] = [];
afterEach(() => {
  for (const root of roots.splice(0)) rmSync(root, { recursive: true, force: true });
});

function fixture() {
  const root = mkdtempSync(join(tmpdir(), 'mod-package-'));
  roots.push(root);
  const manifest = {
    name: 'Example', version_number: '1.2.3', description: 'Example mod',
    website_url: 'https://github.com/example/Example',
    dependencies: ['denikson-BepInExPack_Valheim-5.4.2350'],
  };
  const icon = Buffer.alloc(24);
  Buffer.from('89504e470d0a1a0a0000000d49484452', 'hex').copy(icon);
  icon.writeUInt32BE(256, 16);
  icon.writeUInt32BE(256, 20);
  const files = {
    'package/manifest.json': JSON.stringify(manifest),
    'package/icon.png': icon,
    'package/banner.png': 'banner',
    'README.md': '<img src="package/banner.png">\n![Screenshot](package/screenshots/example.webp)\n![External](https://example.com/image.png)\n[Development](docs/DEVELOPMENT.md)\n[Controls](#controls)',
    'src/Example/Example.csproj': '<Project><PropertyGroup><Version>1.2.3</Version><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>',
    'src/Example/Plugin.cs': '[BepInPlugin(PluginId, "Example", PluginVersion)]\npublic const string PluginVersion = "1.2.3";',
    'src/Example/Properties/AssemblyInfo.cs': '[assembly: AssemblyVersion("1.2.3.0")]\n[assembly: AssemblyFileVersion("1.2.3.0")]',
    'src/Example/bin/Release/net8.0/Example.dll': 'compiled plugin',
  };
  for (const [name, data] of Object.entries(files)) {
    mkdirSync(dirname(join(root, name)), { recursive: true });
    writeFileSync(join(root, name), data);
  }
  const dll = join(root, 'src/Example/bin/Release/net8.0/Example.dll');
  const build = (tag = '') => validate(root, { kind: 'build', assembly: dll }, tag);
  const check = () => validate(root, { kind: 'check' }, '');
  return { root, dll, build, check };
}

test('packages only the mod and resolves README links', async () => {
  const { build, check } = fixture();
  const { archive } = await build('v1.2.3');
  await check();
  const reader = new ZipReader(new Uint8ArrayReader(readFileSync(archive)), { useWebWorkers: false });
  try {
    const entries = await reader.getEntries();
    expect(entries.filter(entry => entry.filename.endsWith('.dll')).map(entry => entry.filename)).toEqual(['plugins/Example.dll']);
    const readme = entries.find(entry => entry.filename === 'README.md');
    if (!readme || readme.directory) throw new Error('Package must contain README.md.');
    const text = Buffer.from(await readme.getData(new Uint8ArrayWriter())).toString();
    expect(text).toContain('src="https://raw.githubusercontent.com/example/Example/v1.2.3/package/banner.png"');
    expect(text).toContain('![Screenshot](https://raw.githubusercontent.com/example/Example/v1.2.3/package/screenshots/example.webp)');
    expect(text).toContain('![External](https://example.com/image.png)');
    expect(text).toContain('https://github.com/example/Example/blob/main/docs/DEVELOPMENT.md');
    expect(text).toContain('[Controls](#controls)');
  } finally {
    await reader.close();
  }
});

test('rejects a mismatched tag before creating a package', async () => {
  const { root, build } = fixture();
  await expect(build('v1.2.4')).rejects.toThrow('Release tag');
  expect(existsSync(join(root, 'artifacts'))).toBe(false);
});

test('rejects bundled dependencies', async () => {
  const { root, build } = fixture();
  writeFileSync(join(root, 'package/assembly_valheim.dll'), 'game');
  await expect(build()).rejects.toThrow('Keep DLLs out');
});

test('rejects unexpected archive files', async () => {
  const { build, check } = fixture();
  const { archive } = await build();
  const reader = new ZipReader(new Uint8ArrayReader(readFileSync(archive)), { useWebWorkers: false });
  const writer = new ZipWriter(new Uint8ArrayWriter(), { useWebWorkers: false });
  try {
    for (const entry of await reader.getEntries()) {
      if (entry.directory) throw new Error('Fixture package must contain only files.');
      await writer.add(entry.filename, new Uint8ArrayReader(await entry.getData(new Uint8ArrayWriter())));
    }
  } finally {
    await reader.close();
  }
  await writer.add('plugins/BepInEx.dll', new Uint8ArrayReader(Buffer.from('dependency')));
  writeFileSync(archive, await writer.close());
  await expect(check()).rejects.toThrow('ZIP contents differ');
});

test('rejects a stale plugin', async () => {
  const { dll, build, check } = fixture();
  await build();
  writeFileSync(dll, 'new build');
  await expect(check()).rejects.toThrow('differs from its source');
});

test('rejects inconsistent plugin versions', async () => {
  const { root, build } = fixture();
  const path = join(root, 'src/Example/Plugin.cs');
  writeFileSync(path, readFileSync(path, 'utf8').replace('"1.2.3"', '"1.2.4"'));
  await expect(build()).rejects.toThrow('PluginVersion');
});

test('rejects invalid icons', async () => {
  const { root, build } = fixture();
  writeFileSync(join(root, 'package/icon.png'), 'invalid');
  await expect(build()).rejects.toThrow('256 by 256');
});

test('rejects ZIP checksum corruption even when file bytes are unchanged', async () => {
  const { build, check } = fixture();
  const { archive } = await build();
  const bytes = readFileSync(archive);
  const header = bytes.indexOf(Buffer.from('504b0102', 'hex'));
  expect(header).toBeGreaterThanOrEqual(0);
  bytes.writeUInt32LE((bytes.readUInt32LE(header + 16) + 1) >>> 0, header + 16);
  writeFileSync(archive, bytes);
  await expect(check()).rejects.toThrow();
});
