# ComfortView local fork

Editable reconstruction of greymishka's ComfortView 1.0.0, decompiled with
ILSpy 9.1.0.7988 from the user-downloaded Nexus DLL.

Source: https://www.nexusmods.com/valheim/mods/3589

Original DLL SHA-256:
`f5c3e4de4fc992c61f4453f5c04d4fd1d303d49958f76c317ad2228960161df5`

The source retains the original plugin ID, `mishka.valheim.comfortview`.
This build replaces the original plugin when installed. Do not load both copies.
Original authorship and distribution terms remain applicable; decompilation
does not grant this reconstruction a new license.

## Build

Requires .NET SDK 8 or later, Valheim client assemblies, and BepInEx 5.
The project defaults to the local Linux Steam install and r2modman's Default
Valheim profile. It references those DLLs without copying them into the output.

```sh
dotnet build src/ComfortView.csproj -c Release
```

On this workstation the SDK executable is
`/home/augie/.local/share/dotnet-sdk/dotnet`.

Override paths for other installations:

```sh
dotnet build src/ComfortView.csproj -c Release \
  -p:GameDir="/path/to/Valheim" \
  -p:BepInExDir="/path/to/profile/BepInEx"
```

Output: `src/bin/Release/net472/ComfortView.dll`.
Building does not install or launch the mod.

## Controls

- F6: show or hide ranges.
- F7: open the selection menu. Up/Down selects a row; Right activates it.
- F7, Shape row, Right: switch between Circles and Spheres.
- F8: pin or unpin the looked-at comfort item and switch to picker mode.
- Escape: close the menu.

F8 does not enable spheres automatically. Select Spheres in the menu first.

## Recovery changes and validation

The project targets the original .NET Framework 4.7.2. Build recovery replaces
temporary ILSpy reference paths, removes a decompiled compiler metadata
attribute, and uses Unity's equivalent `Mathf.PI` constant because the standard
.NET Framework reference assemblies do not expose `MathF`.
The informational version is `1.0.0-local` instead of claiming the upstream
commit hash. The plugin version remains 1.0.0.

Release compilation is checked against the installed Valheim assemblies.
In-game loading, rendering, and Valheim 1.0 behavior have not been verified.

Comfort items are discovered from the game's registered pieces using their
positive base comfort value, matching Valheim's comfort-piece registration.
The menu, nearby ranges, and pin selection no longer filter by a fixed list of
item names, so newly eligible and modded pieces can appear automatically.
Temporarily inactive comfort pieces remain discoverable.

Inherited behavior still includes a fixed 10 m comfort radius and the original
active-only calculation. This change updates item discovery only.
