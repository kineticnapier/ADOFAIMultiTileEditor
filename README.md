# ADOFAI Multi Tile Editor

In-game prototype for editing and synchronizing multiple ADOFAI chart tracks while reusing the stock editor.

Current direction:

- Keep any number of ordinary source-chart snapshots while using the stock ADOFAI editor.
- Analyze each two-planet source track with stock reconstructed floor data.
- Merge the independent source rhythms into one master timeline/path.
- Synthesize and verify a stock-valid master `angleData` path from that merged timeline.
- Emit PACL2 Orbit Decoration actions for every group on that master timeline.
- Keep playback/runtime behavior in PACL2 instead of implementing another renderer.

See [SPEC.md](SPEC.md) for the frozen v1 behavior and the observations taken from the hand-built golden sample.

## Current status

Prototype v0.5.0 implements the first three conversion stages while remaining read-only:

1. `TrackAnalyzer` temporarily reconstructs every stored snapshot through the stock editor and reads runtime `entryBeat`, `angleLength`, `isCCW`, and speed state.
2. `TimelineMerger` validates common start/end timing and common speed maps, then merges all segment boundaries with a `1e-6` beat epsilon.
3. `MasterPathBuilder` converts adjacent master-anchor gaps into ordinary two-planet `angleData`, temporarily reconstructs that candidate through the stock editor, and verifies stock `angleLength` plus `entryBeat`/cumulative timing before restoring the active chart unchanged.

The v0.5 path verifier deliberately removes actions only from its temporary candidate so source-track events cannot interfere with geometry validation. It does not modify stored tracks or the active chart.

`OrbitEmitter`, event-floor remapping, decoration validation, and the final atomic commit are intentionally still absent. They come only after the master path is verified against the golden sample.

Current v0.5 path synthesis supports a maximum of `360°` (2 beat-units) between adjacent master anchors. Longer gaps will be rejected until helper/midspin floor insertion is implemented.

## Build

Requires Visual Studio Build Tools with .NET Framework 4.8 SDK/Targeting Pack.

```powershell
.\build.ps1
```

`build.ps1` auto-detects MSBuild through `vswhere` when it is not on `PATH`.
