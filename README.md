<p align="center">
  <img src="banner.png" alt="ComfortView Reforged: comfort ranges for Valheim" width="900">
</p>

See the space your comfort items cover in Valheim. Display colored range rings
or translucent 3D spheres, then pin individual pieces to inspect their overlap.

## Features

- Visualize the 10 m comfort radius around nearby furniture.
- Switch between ground-level circles and 3D spheres.
- Highlight an item's range by looking at it.
- Show all nearby items, use the active-only filter, or choose specific types and pieces.
- Discover comfort items from the running game's data, including newly eligible
  and modded pieces, without maintaining a fixed item list.

## Installation

Requires Valheim and BepInEx 5.

1. Build the project using the instructions below.
2. Close Valheim.
3. Place `ComfortView.dll` in your mod profile's
   `BepInEx/plugins/ComfortViewReforged/` directory.
4. Launch the game with that profile.

Remove any original ComfortView DLL from the profile before installing.
Reforged retains its plugin ID and config file, so the two versions cannot run together.

The current build compiles against the installed Valheim assemblies but has not
been tested in-game. Range displays assume the default 10 m comfort radius.

## Controls

| Key | Action |
| --- | --- |
| **F6** | Show or hide ranges |
| **F7** | Open or close the selection menu |
| **F8** | Pin or unpin the item you're looking at |
| **Up / Down** | Move through menu rows |
| **Right** | Select an item or change the highlighted option |
| **Left / Right** | Switch Items / Nearby tabs when the tab row is selected |
| **Escape** | Close the menu |

**To see spheres:** press F7, highlight **Shape**, and press Right to select
**Spheres**. Press F8 while looking at furniture to isolate its range.
You can pin multiple items to compare their coverage.

F6, F7, and F8 bindings can be changed in
`BepInEx/config/mishka.valheim.comfortview.cfg`.

## Build

Requires .NET SDK 8 or later, the Valheim client, and BepInEx 5.

```sh
dotnet build src/ComfortView.csproj -c Release \
  -p:GameDir="/path/to/Valheim" \
  -p:BepInExDir="/path/to/profile/BepInEx"
```

The paths can be omitted for a standard Linux Steam install with r2modman's
Default Valheim profile. Output: `src/bin/Release/net472/ComfortView.dll`.

---

Based on [ComfortView by greymishka](https://www.nexusmods.com/valheim/mods/3589).
