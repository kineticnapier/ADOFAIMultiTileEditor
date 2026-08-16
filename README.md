# ADOFAI Multi Tile Editor

In-game prototype for editing and synchronizing multiple ADOFAI chart tracks while reusing the stock editor.

Current prototype goals:

- Keep any number of chart tracks in RAM while using the stock ADOFAI editor.
- Reuse the stock editor path reconstruction instead of implementing an external editor.
- Read the stock floor travel angle and normalize radians to degrees.
- Generate one PACL2 Orbit Decoration per stored track for a synchronized multi-tile step.

## Current status

Prototype v0.3.0. Track storage/switching works; multi-tile Orbit Decoration generation is experimental and currently being debugged.

## Build

Requires Visual Studio Build Tools with .NET Framework 4.8 SDK/Targeting Pack.

```powershell
.\build.ps1
```

`build.ps1` auto-detects MSBuild through `vswhere` when it is not on `PATH`.
