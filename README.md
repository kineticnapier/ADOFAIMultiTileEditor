# ADOFAI Multi Tile Editor

In-game prototype for editing and synchronizing multiple ADOFAI chart tracks while reusing the stock editor.

Current direction:

- Keep any number of ordinary source-chart snapshots while using the stock ADOFAI editor.
- Analyze each two-planet source track with stock reconstructed floor data.
- Merge the independent source rhythms into one master timeline/path.
- Emit PACL2 Orbit Decoration actions for every group on that master timeline.
- Keep playback/runtime behavior in PACL2 instead of implementing another renderer.

See [SPEC.md](SPEC.md) for the frozen v1 behavior and the observations taken from the hand-built golden sample.

## Current status

Prototype v0.3.1 proved track storage/switching, angle probing, and basic Orbit Decoration insertion. The previous per-step generator design is now considered experimental/obsolete; the next implementation should follow the whole-region conversion described in `SPEC.md`.

## Build

Requires Visual Studio Build Tools with .NET Framework 4.8 SDK/Targeting Pack.

```powershell
.\build.ps1
```

`build.ps1` auto-detects MSBuild through `vswhere` when it is not on `PATH`.
