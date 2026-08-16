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

Prototype v0.6.0 implements the first end-to-end generator:

1. `TrackAnalyzer` reconstructs every stored source snapshot and extracts each two-planet segment.
2. `TimelineMerger` unions all segment boundaries with a `1e-6` beat epsilon.
3. `MasterPathBuilder` synthesizes an ordinary master `angleData` path and verifies it through stock reconstruction.
4. `OrbitEmitter` builds a complete candidate chart, validates the initial PACL2 planet objects, remaps the active base chart's non-geometry actions to master anchors, replaces prior configured Orbit actions, and emits one `OrbitDecoration` per source segment.
5. The complete candidate is reconstructed and verified before it replaces the active editor chart. Source-track snapshots are not mutated by generation.

After a successful generation the editor is detached from the source-track queue so switching tracks cannot accidentally overwrite a stored source snapshot with the generated master chart.

### First-generation preflight

The active output/base chart must already contain:

- exactly one PACL2 `AddObject` planet for every configured A/B tag, and
- one dummy PACL2 `OrbitDecoration` whose moving/center tags are one configured A/B pair.

The dummy event is used only as a compatibility template. v0.6 explicitly overwrites every field owned by the v1 specification (`floor`, `duration`, `tag`, `centerTag`, `amount`, `lockRotation`, `dstRadiusMultiplier`, `ease`, `angleOffset`, and `eventTag`). On regeneration, an already-generated Orbit action can serve as the template.

Source geometry actions (`Twirl`, `MultiPlanet`, `Pause`, `Hold`, and `FreeRoam*`) are not copied onto the synthesized master path. Other actions from the active base chart are remapped from their source floor's musical time to the corresponding master anchor. Common timing maps are still required by the planner.

Current path synthesis supports a maximum of `360°` (2 beat-units) between adjacent master anchors. Longer gaps are rejected until helper/midspin floor insertion is implemented.

## Build

Requires Visual Studio Build Tools with .NET Framework 4.8 SDK/Targeting Pack.

```powershell
.\build.ps1
```

`build.ps1` auto-detects MSBuild through `vswhere` when it is not on `PATH`.
