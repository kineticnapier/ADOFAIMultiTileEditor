# ADOFAI Multi Tile Editor

In-game prototype for editing and synchronizing multiple ADOFAI chart tracks while reusing the stock editor.

Current direction:

- Keep any number of ordinary source-chart snapshots while using the stock ADOFAI editor.
- Analyze each two-planet source track with stock reconstructed floor data.
- Merge the independent source rhythms into one master timeline/path.
- Synthesize and verify a stock-valid master `angleData` path from that merged timeline.
- Automatically create missing PACL2 planet `AddObject` decorations for complete groups.
- Emit PACL2 Orbit Decoration actions for every group on that master timeline.
- Recreate each source chart as PACL2 Floor `AddObject` preview decorations.
- Keep playback/runtime behavior in PACL2 instead of implementing another renderer.

See [SPEC.md](SPEC.md) for the v1 behavior and the observations taken from the hand-built golden sample.

## Current status

Prototype v0.8.0 implements an end-to-end generator with automatic PACL2 setup and source-tile previews:

1. `TrackAnalyzer` reconstructs every stored source snapshot and extracts each two-planet segment.
2. `TimelineMerger` unions all segment boundaries with a `1e-6` beat epsilon.
3. `MasterPathBuilder` synthesizes an ordinary master `angleData` path and verifies it through stock reconstruction.
4. `PACL2AutoGenerator` creates missing A/B Planet `AddObject` decorations on a deterministic grid and creates an internal `OrbitDecoration` template when none exists.
5. `OrbitEmitter` builds the candidate chart, remaps the active base chart's non-geometry actions to master anchors, replaces prior configured Orbit actions, and emits one `OrbitDecoration` per source segment.
6. Generated custom-event values are normalized back to PACL2/ADOFAI metadata types in memory, then floor effects are reapplied so playback works immediately without a save/reload cycle.
7. `TileDecorationGenerator` reconstructs every stored source chart, reads its runtime floor positions and outgoing `angleLength`, then creates PACL2 Floor `AddObject` previews matching the source shape.
8. Source-track snapshots are not mutated by generation.

After a successful generation the editor is detached from the source-track queue so switching tracks cannot accidentally overwrite a stored source snapshot with the generated master chart.

### Automatic planet setup

For each configured track:

- if neither A nor B Planet `AddObject` exists, the generator creates both automatically;
- if both already exist, they are preserved unchanged;
- if only one exists, generation aborts rather than guessing how to position the missing half;
- duplicate planet tags remain an error.

Auto-created groups use a deterministic grid. Within each group the moving planet starts one tile to the left of the configured initial pivot. Planet A uses `DefaultRed`, Planet B uses `DefaultBlue`.

A manually authored dummy Orbit event is no longer required when PACL2 has registered its `OrbitDecoration` metadata. The generator creates a temporary typed template internally and removes/replaces it during normal Orbit emission.

### Automatic source-tile previews

For each stored source track, v0.8 recreates the interior source floors as PACL2 `AddObject` objects with `objectType = Floor`.

- Runtime `scrFloor.transform.position` is used for the source shape.
- Runtime `angleLength` supplies each preview tile's `trackAngle`.
- The first source floor is represented by the planet pair and the synthetic terminal floor is omitted, matching the hand-built golden sample.
- Generated tags remain EQOL-compatible (`T0`, `T0_0`, `qolMultiTile_T0`, `qolMultiTileRhythm_*`) and also include `adofaiMTEGenerated` so regeneration only removes MTE-owned previews.
- Existing manually-authored/EQOL preview decorations are preserved instead of duplicated.
- Exact 2x / 0.5x runtime speed changes receive `DoubleRabbit` / `DoubleSnail` preview icons.

Source geometry actions (`Twirl`, `MultiPlanet`, `Pause`, `Hold`, and `FreeRoam*`) are not copied onto the synthesized master path. Other actions from the active base chart are remapped from their source floor's musical time to the corresponding master anchor. Common timing maps are still required by the planner.

Current path synthesis supports a maximum of `360°` (2 beat-units) between adjacent master anchors. Longer gaps are rejected until helper/midspin floor insertion is implemented.

## Build

Requires Visual Studio Build Tools with .NET Framework 4.8 SDK/Targeting Pack.

```powershell
.\build.ps1
```

`build.ps1` auto-detects MSBuild through `vswhere` when it is not on `PATH`.
