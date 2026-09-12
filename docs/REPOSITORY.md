# Repository convention

Start new mods from [ValheimModTemplate](https://github.com/AugusDogus/ValheimModTemplate/blob/main/docs/TEMPLATE.md).
Its `dotnet new valheim-mod -n MyMod` command generates this layout with the
project and plugin identities renamed.

Each mod is an independent repository. Share this layout and build plumbing;
keep runtime code, dependencies, tests, artwork, and licensing specific to the mod.

```text
.github/workflows/release.yml  Build, check, and publish version tags
src/<Mod>/                    Plugin.cs, project, runtime code and resources
tests/                        Mod checks and release-tool regression tests
package/                      Thunderstore manifest, icon, banner, screenshots
assets/                       Editable artwork and optional Unity projects
build/Package.targets         Shared dotnet build -t:Package entry point
scripts/                      Package validation, version updates, mod checks
docs/                         Development and repository documentation
<Mod>.sln                     Solution entry point
Directory.Build.props         Shared local game-reference paths
global.json                   .NET 8 SDK selection
package.json / bun.lock       Pinned Bun release-tool dependencies
tsconfig.json                 Strict TypeScript checks for release tooling
bump.config.mjs               Reviewed version commit and tag creation
thunderstore.toml             Thunderstore publication settings
README.md                     Player-facing overview, setup, controls, build link
CHANGELOG.md                  Release history
LICENSE.md                    Existing project license, when one is declared
```

`artifacts/`, `bin/`, `obj/`, downloaded dependencies, and `Environment.props`
are local outputs. Never commit game assemblies or tokens. `legacy/` is optional
for inherited code and tools excluded from the current build.

Use one plugin project under `src/<Mod>/` and keep its entry point in `Plugin.cs`.
The public `PluginVersion` constant supplies `BepInPlugin`; the version updater
keeps it synchronized with the project, manifest, and assembly attributes.
Package names, assembly names, and plugin IDs may differ for compatibility.

Keep `release.yml`, `Package.targets`, the release scripts and their tests,
`package.json`, `bun.lock`, `tsconfig.json`, `global.json`, and `bump.config.mjs` identical across repositories. Extend
`scripts/check.sh` for the mod's actual tests. No shared private workflow access
or runtime dependency on another repository is required.

The root README is the source for both GitHub and the package page. Keep artwork
under `package/` and link developer details under `docs/`; packaging adjusts
those links. Retain existing attribution and licenses when starting from a fork.
