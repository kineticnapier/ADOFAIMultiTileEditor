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

Prototype v0.4.0 replaces the obsolete per-step Orbit generator with the first half of the v1 pipeline:

1. `TrackAnalyzer` temporarily reconstructs every stored snapshot through the stock editor and reads runtime `entryBeat`, `angleLength`, `isCCW`, and speed state.
2. `TimelineMerger` validates common start/end timing and common speed maps, then merges all segment boundaries with a `1e-6` beat epsilon.
3. The UMM GUI shows the resulting per-track segments and master anchors for comparison against the golden sample.

v0.4.0 is deliberately read-only after planning. It does **not** synthesize the master path or emit Orbit Decoration actions yet. Those are the next two stages (`MasterPathBuilder` and `OrbitEmitter`) after the analyzer output is verified.

The planner rejects unsupported v1 cases such as source `Pause`, `MultiPlanet`, `Hold`, `FreeRoam`, conflicting tag pairs, timing-map mismatches, and reconstructed angle/timing inconsistencies instead of silently guessing.

## Build

Requires Visual Studio Build Tools with .NET Framework 4.8 SDK/Targeting Pack.

```powershell
.\build.ps1
```

`build.ps1` auto-detects MSBuild through `vswhere` when it is not on `PATH`.
