# CyberCAT-SimpleGUI

A simplified offshoot of SirBitesalot's CyberCAT.

This is a fork of [DeweySalt's CyberCAT-SimpleGUI](https://github.com/Deweh/CyberCAT-SimpleGUI)
v0.27d, updated to read and write Cyberpunk 2077 **patch 2.31** saves: the
WolvenKit submodule now tracks `main`, the editor targets .NET 10, and the
call sites that moved in WolvenKit's API were brought forward.

## Download

Grab the latest **`CP2077SaveEditor-2.31-single.zip`** from
[Releases](https://github.com/exexexexe/CyberCAT-SimpleGUI-2.31/releases/latest),
extract it, and run `CP2077SaveEditor.exe`. Self-contained -- no .NET runtime
to install. Keep the extracted folder together; the editor reads `previews\`,
`licenses\` and `config.json` from its own directory.

Back up your saves first. They live in
`%USERPROFILE%\Saved Games\CD Projekt Red\Cyberpunk 2077\`.

## Features

**Feature**                                                                     | **Stability**
------------------------------------------------------------------------------- | -----------
Save and load presets for your character's appearance.                          | Stable
Edit the quantity, flags, & mod tree of items in your inventory.                | Stable
Edit quest facts.                                                               | Stable
Quick actions - dedicated controls for changing money & making items legendary. | Semi-Stable

## Pictures

Selecting a vehicle, or an iconic weapon in your inventory, shows a picture of it
beside the list.

The id-to-picture map is generated offline by `tools/build-image-map.py` from the
Cyberpunk wiki and ships inside the exe, so the editor never scrapes anything at
runtime. It fetches the bytes of a picture you actually look at, once, then keeps
it in `%LOCALAPPDATA%\CP2077SaveEditor\images`. No pictures are redistributed with
the editor. Set `"EnableImages": 0` in `config.json` to switch the network access
off entirely; everything else works the same, you just get no pictures.

Coverage is all 75 vehicles and 142 iconic weapon ids. Ordinary guns are not
covered -- the wiki documents named weapons, not every preset -- so most inventory
rows show nothing, by design. Vehicles are matched on their name rather than an
id, and variants fall back to their base model's picture; where the match was not
exact the pane says "closest match" instead of pretending.

## Game Version Support

**Game Version**    | **Support** | **CyberCAT-SimpleGUI Version**
--------------------| ------------|-----------------------------
Patch 2.31          | Full        | >=v0.27d-2.31
Patch 1.5X          | Full        | >=v0.20a
Patch 1.3           | Partial     | >=v0.10a_r1
Patch 1.23          | Full        | >=v0.10a

Patch 2.31 support requires the updated `WolvenKit` submodule (`main`); the
2024-era `develop` branch cannot read saveVersion 269.

## Building

Requires the **.NET 10 SDK** (the tree pins 10.0.302 via `WolvenKit/global.json`).

```
git clone --recurse-submodules <this repo>
cd CyberCAT-SimpleGUI-2.31
pwsh ./build.ps1
```

`build.ps1` produces one self-contained `CP2077SaveEditor.exe` (no installed
runtime needed) in `../CP2077SaveEditor-2.31-single`, alongside the data files
the app reads from its working directory. Pass `-Layout Folder` for an
unbundled build instead.

Two notes:

- If `WolvenKit/` is empty, run `git submodule update --init --recursive`. The
  submodule must track `main` -- the 2024-era `develop` branch is gone upstream
  and could not read saveVersion 269 anyway.
- `previews/` and `items.bin` are **not** in this repo: ~137 MB of
  game-derived data that is not ours to redistribute. The build works without
  them (nothing reads `items.bin`; item and fact data is compiled in as
  embedded resources), but the appearance tab shows blank thumbnails until you
  drop `previews/` from the upstream 0.27d release zip into the repo root.

## Usage

1. Run **CP2077SaveEditor.exe**
2. Click **"Load Save"**
3. Make changes to your save.
    - Double-click items in your inventory to edit them.
    - Double-click nodes in an item's mod tree to edit them
4. Click **"Save Changes"**

## Todo

- Refactor, refactor, refactor.
- Eat some of the spaghetti.
- Improve the functionality of the appearance tab.
- Move contribution section from readme to github wiki

## Credits

[CyberCAT by SirBitesalot and other contributors](https://github.com/WolvenKit/CyberCAT)
[WolvenKit by the WolvenKit team](https://github.com/WolvenKit/WolvenKit)

## Contribution

### VSCodium

**Task** | **Info**
-------- | ----------------------------------
Requires | .NET 5.0 SDK
Setup    | None
Build    | Terminal > Run Build Task...
Debug    | Unsupported

### Visual Studio 2019

**Task** | **Info**
-------- | ----------------------------------
Requires | .NET 5.0 SDK, C# Desktop workspace
Setup    | Solution > Restore NuGet Packages
Build    | Build > Rebuild Solution
Debug    | Debug > Start Debugging
