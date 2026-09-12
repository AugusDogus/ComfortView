import assert from 'node:assert/strict';
import { appendFileSync, existsSync, mkdirSync, readFileSync, renameSync, rmSync, writeFileSync } from 'node:fs';
import { basename, dirname, join, relative, resolve } from 'node:path';
import { parseArgs } from 'node:util';
import { Glob, XML } from 'bun';
import { Uint8ArrayReader, Uint8ArrayWriter, ZipReader, ZipWriter } from '@zip.js/zip.js';

type PackageMode = { kind: 'check' } | { kind: 'build'; assembly: string };

function readManifest(root: string) {
  const value: unknown = JSON.parse(readFileSync(join(root, 'package/manifest.json'), 'utf8'));
  assert(typeof value === 'object' && value !== null, 'The package manifest must be an object.');
  assert('name' in value && typeof value.name === 'string' && /^[A-Za-z0-9_]+$/.test(value.name),
    'The package name must contain only letters, numbers, and underscores.');
  assert('version_number' in value && typeof value.version_number === 'string'
    && /^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$/.test(value.version_number)
    && value.version_number.split('.').every(part => Number(part) <= 65534),
  'The package version must use MAJOR.MINOR.PATCH with components between 0 and 65534.');
  assert('description' in value && typeof value.description === 'string' && value.description.length <= 250,
    'The package description must be at most 250 characters.');
  assert('website_url' in value && typeof value.website_url === 'string'
    && /^https:\/\/github\.com\/[\w.-]+\/[\w.-]+$/.test(value.website_url),
  "Set website_url to this mod's GitHub repository URL.");
  assert('dependencies' in value && Array.isArray(value.dependencies)
    && value.dependencies.every((item: unknown): item is string =>
      typeof item === 'string' && /^\w+-\w+-\d+\.\d+\.\d+$/.test(item)),
  'Dependencies must use OWNER-PACKAGE-MAJOR.MINOR.PATCH.');
  assert.equal(value.dependencies.filter(item => item.startsWith('denikson-BepInExPack_Valheim-')).length, 1,
    'Declare exactly one BepInExPack_Valheim dependency.');
  return { name: value.name, version: value.version_number, website: value.website_url };
}

function projectProperty(project: XML.Node, name: string): string | undefined {
  const values: string[] = [];
  for (const group of project.children) {
    if (typeof group === 'string' || !('name' in group) || group.name !== 'PropertyGroup') continue;
    for (const property of group.children) {
      if (typeof property === 'string' || !('name' in property) || property.name !== name) continue;
      assert(property.children.every(child => typeof child === 'string'), `Project ${name} must contain text.`);
      values.push(property.children.join('').trim());
    }
  }
  assert(values.length <= 1, `Expected at most one project ${name} property.`);
  return values[0];
}

function metadata(root: string, tag: string) {
  const manifest = readManifest(root);
  const projects = [...new Glob('src/*/*.csproj').scanSync({ cwd: root, absolute: true })];
  const [project] = projects;
  assert(project && projects.length === 1, 'Expected one plugin project at src/<Mod>/<Mod>.csproj.');
  const tree = XML.parse(readFileSync(project), { compact: false });
  assert.equal(projectProperty(tree, 'Version'), manifest.version,
    'The project Version must match package/manifest.json. Run bumpp before tagging.');
  const plugin = readFileSync(join(dirname(project), 'Plugin.cs'), 'utf8');
  assert.deepEqual([...plugin.matchAll(/public const string PluginVersion\s*=\s*"([^"]+)";/g)].map(match => match[1]),
    [manifest.version], 'PluginVersion must match package/manifest.json.');
  assert(/\[BepInPlugin\([^\n]*, PluginVersion\)\]/.test(plugin), 'BepInPlugin must use PluginVersion.');
  const assembly = readFileSync(join(dirname(project), 'Properties/AssemblyInfo.cs'), 'utf8');
  for (const attribute of ['AssemblyVersion', 'AssemblyFileVersion']) {
    const pattern = new RegExp(`^\\[assembly: ${attribute}\\("([^"]+)"\\)\\]`, 'gm');
    assert.deepEqual([...assembly.matchAll(pattern)].map(match => match[1]), [manifest.version + '.0'],
      `${attribute} must match the package version with a .0 revision.`);
  }
  assert(!tag || tag === `v${manifest.version}`,
    `Release tag ${JSON.stringify(tag)} must be v${manifest.version}. No package was published.`);
  const icon = readFileSync(join(root, 'package/icon.png'));
  assert(icon.length >= 24 && icon.subarray(0, 8).equals(Buffer.from('89504e470d0a1a0a', 'hex'))
    && icon.readUInt32BE(16) === 256 && icon.readUInt32BE(20) === 256,
  'package/icon.png must be a 256 by 256 PNG.');
  const name = projectProperty(tree, 'AssemblyName') ?? basename(project, '.csproj');
  const framework = projectProperty(tree, 'TargetFramework');
  assert(framework, 'The plugin project must declare TargetFramework.');
  return {
    ...manifest,
    dll: join(dirname(project), 'bin/Release', framework, name + '.dll'),
    archive: join(root, 'artifacts', `${manifest.name}-${manifest.version}.zip`),
  };
}

function packageFiles(root: string, info: ReturnType<typeof metadata>) {
  const files = new Map<string, Uint8Array>();
  const packageDir = join(root, 'package');
  for (const name of [...new Glob('**/*').scanSync({ cwd: packageDir, dot: true, onlyFiles: true })].sort()) {
    assert(!name.toLowerCase().endsWith('.dll'), 'Keep DLLs out of package/. Only the built plugin may be distributed.');
    files.set(name.replaceAll('\\', '/'), readFileSync(join(packageDir, name)));
  }
  function link(_match: string, prefix: string, target: string, suffix: string) {
    if (target.startsWith('package/')) {
      const repository = info.website.slice('https://github.com/'.length);
      target = `https://raw.githubusercontent.com/${repository}/v${info.version}/${target}`;
    }
    else if (!/^(?:[a-z]+:|#|\/)/.test(target)) target = `${info.website}/blob/main/${target}`;
    return prefix + target + suffix;
  }
  // Thunderstore does not serve bundled images; use the release tag's public files.
  const readme = readFileSync(join(root, 'README.md'), 'utf8')
    .replace(/(\]\()([^\s)]+)(\))/g, link).replace(/(src=")([^"]+)(")/g, link);
  files.set('README.md', Buffer.from(readme));
  for (const name of ['CHANGELOG.md', 'LICENSE.md', 'assets/artwork/FONT-LICENSE.txt']) {
    if (existsSync(join(root, name))) files.set(basename(name), readFileSync(join(root, name)));
  }
  files.set('plugins/' + basename(info.dll), readFileSync(info.dll));
  return files;
}

export async function validate(root: string, mode: PackageMode = { kind: 'check' }, tag = process.env.RELEASE_TAG ?? '') {
  const info = metadata(root, tag);
  if (mode.kind === 'build') {
    assert.equal(resolve(mode.assembly), resolve(info.dll),
      'Package requires the Release build. Run dotnet build -c Release -t:Package.');
  }
  const expected = packageFiles(root, info);
  if (mode.kind === 'build') {
    const writer = new ZipWriter(new Uint8ArrayWriter(), { useWebWorkers: false });
    for (const [name, bytes] of expected) await writer.add(name, new Uint8ArrayReader(bytes));
    const bytes = await writer.close();
    mkdirSync(dirname(info.archive), { recursive: true });
    const temporary = info.archive + '.tmp';
    try {
      writeFileSync(temporary, bytes);
      renameSync(temporary, info.archive);
    } finally {
      rmSync(temporary, { force: true });
    }
  }
  const reader = new ZipReader(new Uint8ArrayReader(readFileSync(info.archive)), { useWebWorkers: false });
  try {
    const entries = await reader.getEntries();
    assert.deepEqual(entries.map(entry => entry.filename).sort(), [...expected.keys()].sort(),
      'ZIP contents differ from the expected plugin and metadata. Rebuild the package.');
    for (const entry of entries) {
      assert(!entry.directory, 'Unexpected directory entry. Rebuild the package.');
      const bytes = await entry.getData(new Uint8ArrayWriter(), { checkSignature: true });
      const source = expected.get(entry.filename);
      assert(source && Buffer.from(bytes).equals(source),
        `Packaged ${entry.filename} differs from its source. Rebuild the package.`);
    }
  } finally {
    await reader.close();
  }
  return info;
}

if (import.meta.main) {
  try {
    const { values } = parseArgs({ options: { assembly: { type: 'string' } } });
    const root = resolve(import.meta.dir, '..');
    const info = await validate(root, values.assembly ? { kind: 'build', assembly: values.assembly } : { kind: 'check' });
    const archive = relative(root, info.archive).replaceAll('\\', '/');
    if (process.env.GITHUB_OUTPUT) {
      appendFileSync(process.env.GITHUB_OUTPUT, `version=${info.version}\nname=${info.name}\narchive=${archive}\n`);
    }
    console.log(`Validated ${archive}.`);
  } catch (error) {
    console.error(`Package validation failed: ${error instanceof Error ? error.message : String(error)}`);
    process.exitCode = 1;
  }
}
