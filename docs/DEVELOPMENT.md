# Development

## Build

Install .NET SDK 8 and Bun 1.4.1 or newer. Run `bun install --frozen-lockfile`
from the repository root to install the release tooling. Use your Valheim
installation and BepInEx 5 profile:

```sh
dotnet build src/ComfortView/ComfortView.csproj -c Release \
  -p:GameDir="/path/to/Valheim" \
  -p:BepInExDir="/path/to/profile/BepInEx"
```

Output: `src/ComfortView/bin/Release/net472/ComfortView.dll`.

`GameDir` defaults to a standard Steam installation on Linux or Windows.
`BepInExDir` defaults to `GameDir/BepInEx`; set it explicitly for r2modman.
Override `ManagedDir` for a dedicated server or another game data directory.
`Environment.props` is ignored by Git and may store local MSBuild properties:

```xml
<Project>
  <PropertyGroup>
    <GameDir>/path/to/Valheim</GameDir>
    <BepInExDir>/path/to/profile/BepInEx</BepInExDir>
  </PropertyGroup>
</Project>
```

## Check

After building, run the mod's checks with the same references:

```sh
MANAGED_DIR="/path/to/Valheim/valheim_Data/Managed" \
BEPINEX_DIR="/path/to/profile/BepInEx" bash scripts/check.sh
bun run typecheck
bun test tests/
```

The TypeScript package checks and JavaScript version checks use Bun's test runner.

## Package

Add `-t:Package` to the Release build command. The TypeScript script creates and validates
`artifacts/<PackageName>-<Version>.zip`. Import this ZIP with r2modman's
**Import local mod**. Build and package commands do not install or publish anything.

`package/manifest.json` defines the Thunderstore identity and dependencies.
Only the plugin DLL, package assets, README, changelog, and available license
notices enter the ZIP. Game, BepInEx, and NuGet dependency DLLs are excluded.
README links are adjusted during packaging so local images still resolve.

## GitHub Actions

The identical **Build and publish** workflow in each mod builds on pushes to
`main`, pull requests, manual runs, and `vMAJOR.MINOR.PATCH` tags. It downloads
current public Valheim dedicated-server assemblies using anonymous SteamCMD
(app 896660), and the BepInEx version from `package/manifest.json`.
No Steam credentials or local game files are needed.

The workflow runs `scripts/check.sh`, package tests, and version tests, then
uploads the validated ZIP as `thunderstore-package`. Tag pushes additionally
create a GitHub release and publish that same ZIP with [Thunderstore CLI](https://github.com/thunderstore-io/thunderstore-cli).
The publishing token is passed only to the publish step. Branch pushes, pull requests,
and manual runs only build and check.

Repository Actions settings:

- Variable `THUNDERSTORE_NAMESPACE`: `AugusDogus`.
- Secret `TCLI_AUTH_TOKEN`: a service-account access token for that Thunderstore team.

## Release

Keep the existing version until a release is ready. Update `CHANGELOG.md` and
commit your changes first. From a clean checkout, use Node.js 22.18+ or 24.11+:

```sh
npx bumpp@12.3.0 --release patch
# Inspect the generated commit and substitute its tag below.
git push --atomic origin HEAD:main vMAJOR.MINOR.PATCH
```

Use `--release 1.0.0` to tag an unreleased `1.0.0` without incrementing it, or
supply another explicit version. The shared bump config updates the manifest,
project version, plugin constant, and assembly versions together. Existing tags
are never overwritten. Check `git remote -v` before pushing from an old checkout
that also has an upstream remote.

Tags must match the source versions exactly. Prerelease tags are not published.
Creating a release manually on GitHub does not trigger publishing.
If Thunderstore publishing fails, the ZIP remains on the GitHub release.
Correct credentials or settings and rerun the failed job. If the version is
already published on Thunderstore, release a new version.

## In-game checks

Verify F6/F7/F8, rings and spheres, furniture selection, and range overlap. ComfortView has no game-independent behavior tests yet. Compilation and package checks do not establish in-game compatibility.
