# ADOFAI Multi Tile Editor — v1 Specification

Status: **Draft / implementation contract**

This document freezes the first useful version of the multi-tile workflow before more code is added. It is based on the hand-built `multitest.adofai` golden sample and on the behavior already verified in the stock ADOFAI editor.

The important architectural decision is:

> Multi Tile Editor is not a second renderer and it is not a per-frame multi-planet runtime. It converts several ordinary two-planet source charts into one ordinary ADOFAI master timeline plus PACL2 `OrbitDecoration` actions.

## 1. Goal

Given any number of independently edited two-planet source tracks, generate a single playable chart where every group follows its own source rhythm at the same time.

The mod should eliminate the repetitive work of manually:

- finding every hit time across every source chart,
- constructing a master path containing the union of those hit times,
- alternating each group's moving/pivot planet,
- calculating each group's Orbit Decoration angle and duration,
- placing all Orbit Decoration actions on the correct master floors.

The stock ADOFAI editor remains the chart editor. PACL2 remains responsible for playing `OrbitDecoration`.

## 2. Explicit non-goals for v1

The following are deliberately out of scope for v1:

- external editor,
- custom multi-planet renderer,
- frame-by-frame planet simulation in this mod,
- arbitrary radius changes (`dstRadiusMultiplier` is fixed to `1`),
- groups containing more than two planets,
- automatic creation or automatic positioning of planet decorations,
- VS Code-style split branch editor,
- DAG/shared-node editor,
- merging arbitrary track-specific gameplay/events,
- track-specific `SetSpeed`, `Pause`, or other timing-map differences,
- reproducing the exact visual shape or exact floor numbers of the golden sample's master path.

These can be added later only after the v1 conversion is stable.

## 3. Terminology

### Source track

A normal ADOFAI chart snapshot edited through the stock editor. One source track represents one independent two-planet group.

### Group

A pair of planet decorations:

- Planet A tag
- Planet B tag
- initial pivot (`A` or `B`)

A group's pivot state is completely independent of every other group.

### Segment

One movement from one source-track hit to the next. A segment has at least:

- source floor/index,
- absolute start time,
- absolute end time,
- signed travel angle,
- Orbit duration,
- moving planet,
- pivot planet.

### Master timeline

The ordered union of hit/start times from every source track.

### Master path

The ordinary ADOFAI `angleData` path synthesized so that its floors occur at the master timeline instants. It exists primarily to provide deterministic event anchors and timing.

### Golden sample

The hand-built `multitest.adofai` used as the first behavioral reference.

## 4. User workflow

The intended v1 workflow is queue-like:

1. Edit one source chart in the stock ADOFAI editor.
2. Store it as a track.
3. Edit the next source chart.
4. Store it as another track.
5. Repeat for any number of tracks.
6. Configure each track's Planet A tag, Planet B tag, and initial pivot.
7. Ensure the output/base chart already contains the two initial `AddObject` planet decorations for each group.
8. Generate the whole multi-tile region in one operation.

The current per-step `Generate Multi Tile Step` model is superseded by this specification. Generation is a **whole-region conversion**, not a cursor-advance loop.

## 5. Source-track constraints

For v1, all stored source tracks MUST:

- belong to the same song/timing context,
- use the same common BPM / timing-affecting event map,
- start the generated region at the same absolute song time,
- finish the generated region at the same absolute song time within the timeline epsilon,
- represent exactly one two-planet group each.

The source tracks MAY have different numbers of floors and different rhythms.

The source tracks MAY have different travel angles at the same musical time.

Track count is arbitrary. The implementation MUST NOT assume exactly two groups.

### Timing-event restriction

v1 does not attempt to merge competing timing maps. If one source track changes BPM or pauses independently of another source track, generation MUST fail preflight with a useful diagnostic rather than guessing.

Common timing events belong to the base/master chart and are preserved once.

## 6. Source-track analysis

Track analysis MUST use the stock game's reconstructed floor data whenever possible.

The converter MUST NOT derive the real movement only from raw `angleData` subtraction.

For every source segment, analysis must obtain:

- absolute start time,
- absolute end time,
- signed travel angle,
- duration in the units required by PACL2 at the output anchor,
- source floor/index for diagnostics.

### Angle

The angle used by `OrbitDecoration.amount` is signed.

The analyzer MUST preserve the real travel direction, including direction changes caused by stock game state such as Twirl. It MUST NOT assume that every Orbit amount is negative merely because the golden sample uses negative values.

`angleLength` is known to be radians in the current game build and may be useful for magnitude, but magnitude alone is not sufficient to determine direction.

### Duration

Duration is per segment, not per generated step and not shared between groups.

The golden sample happens to contain ordinary segments where:

```text
duration ≈ abs(amount) / 180
```

For example:

- 180° -> 1 beat
- 135° -> 0.75 beat
- 120° -> 0.6666666... beat
- 90° -> 0.5 beat

This is an observed property of the sample, not permission to ignore the game's timing calculation. The analyzer should use reconstructed game timing so later support does not depend on a fragile formula.

## 7. Timeline merge

The generator first converts every source track into a list of timed segments.

It then creates the master timeline from the sorted union of segment boundaries/start times.

Conceptually:

```text
Track A: 0 ---- 1 ---- 1.75 ---- 2.5 ---- ...
Track B: 0 ---- 1 ---- 1.666... ---- 2.333... ---- ...

Master : 0 ---- 1 ---- 1.666... -- 1.75 -- 2.333... -- 2.5 -- ...
```

Important rules:

- Merge by **musical/song time**, never by source floor number.
- Multiple groups may start a segment at the same master instant.
- Multiple Orbit Decoration actions may therefore share one master floor.
- Ordering between different groups at the same instant must be deterministic but has no semantic effect.
- No frame-count-based handoff or stability test belongs in this mod.

### Timeline epsilon

Floating-point representations such as repeated `0.6666666` must not create accidental microscopic splits.

Initial v1 comparison tolerance:

```text
1e-6 beat-equivalent, or the corresponding absolute-song-time tolerance
```

The implementation should keep the tolerance in one named constant and test it explicitly.

The exact floor split seen in a manually authored golden chart is not normative if two instants are musically equivalent within tolerance.

## 8. Master path synthesis

After the timeline is merged, the generator creates an ordinary ADOFAI path whose floor timing matches the master timeline.

Requirements:

- The master path MUST reproduce every merged timeline instant within tolerance.
- The exact visual geometry is not important.
- The exact resulting floor numbers are not part of the public contract.
- The builder MAY use `999`/midspin anchors internally when useful.
- Path construction should rely on stock ADOFAI reconstruction/validation after writing the candidate data.
- The builder MUST verify the reconstructed master floor times after synthesis before committing.

The golden sample contains `999` entries in its master `angleData`; those are an implementation detail of the hand-built timing path, not a requirement to copy that exact sequence.

## 9. Group/pivot state

Each group owns its own pivot state.

For a group with tags `A` and `B`:

```text
pivot B -> move A around B -> pivot becomes A
pivot A -> move B around A -> pivot becomes B
```

The pivot swaps after every emitted source segment for that group.

One group's pivot changes MUST NOT affect any other group.

Initial pivot is configurable per track because the golden sample demonstrates different valid starting pivots between groups.

## 10. Orbit Decoration emission

For every analyzed source segment, emit exactly one PACL2 `OrbitDecoration` action at the corresponding master timeline anchor.

The semantic fields are:

```json
{
  "eventType": "OrbitDecoration",
  "duration": "<segment duration>",
  "tag": "<moving planet tag>",
  "centerTag": "<pivot planet tag>",
  "amount": "<signed source travel angle>",
  "lockRotation": false,
  "dstRadiusMultiplier": 1,
  "ease": "Linear",
  "angleOffset": 0,
  "eventTag": ""
}
```

v1 fixes the following values:

- `dstRadiusMultiplier = 1`
- `ease = Linear`
- `lockRotation = false`
- `angleOffset = 0`
- `eventTag = ""`

These can become options later, but they are not v1 variables.

### No stale template values

The current prototype clones a dummy Orbit Decoration template. If template cloning remains necessary for compatibility, every field owned by this specification MUST be explicitly overwritten and no unrelated state may leak from the template.

Longer term, constructing a fresh compatible event is preferable if PACL2/game APIs allow it safely.

## 11. Planet decorations

v1 requires the output chart to already contain exactly two initial `AddObject` planet decorations per configured group.

The user supplies their initial visual positions once.

The generator:

- validates that every configured tag exists,
- validates that A and B tags are distinct,
- validates that tags are unique across groups unless a future spec explicitly allows sharing,
- does not reposition the planets,
- does not regenerate their appearance/color settings.

After initialization, movement is driven only by generated Orbit Decoration actions.

This deliberately avoids turning Multi Tile Editor into another renderer.

## 12. EQOL independence

The golden sample contains helper `AddObject` floor decorations generated with EditorQoL, including tags such as:

```text
T0
T0_0
T1
T1_0
qolMultiTile_T0
qolMultiTileRhythm_...
```

These are reference/visualization data only.

Multi Tile Editor MUST NOT:

- require EditorQoL,
- parse those helper tags as source-of-truth timing,
- depend on those floor decorations being present,
- generate them as part of the core v1 conversion.

All required rhythm information comes from the stored source tracks/game reconstruction.

## 13. Non-Orbit actions

The generator owns only the multi-tile master path and the Orbit Decoration actions for configured groups.

Common/base actions such as the golden sample's shared `SetSpeed` and `SetHitsound` are preserved once from the base chart.

v1 does not merge arbitrary per-track actions. If source tracks contain conflicting/non-common actions inside the generation region, preflight should reject or explicitly ignore them according to a narrow allowlist; it must not silently duplicate everything.

## 14. Atomic generation

Generation is all-or-nothing.

Before touching the active LevelData, preflight MUST validate:

- at least one stored track,
- analyzable floor timings,
- equal region start time,
- equal region end time within tolerance,
- compatible/common timing map,
- valid distinct A/B tags for every group,
- required initial planet decorations,
- PACL2 Orbit Decoration compatibility,
- a synthesizable master timeline.

The generator should build a complete candidate LevelData copy in memory, reconstruct it with stock game code, validate it, and only then replace the active editor state.

Failure MUST leave the chart unchanged.

A successful generation SHOULD be one stock-editor Undo step.

Generation MUST NOT mutate TrackStore cursors or pivot state as a side effect. Track snapshots are inputs; generation should behave as a pure conversion from inputs to candidate output.

## 15. Regeneration / idempotence

Running generation twice with the same tracks and configuration MUST NOT duplicate Orbit Decoration actions.

Before commit, the generator should replace previously generated Orbit actions belonging to the configured group tag pairs in the target region rather than append blindly.

The output of repeated generation with identical inputs should be structurally equivalent.

## 16. Playback and seeking

The generated result is an ordinary ADOFAI chart plus PACL2 Orbit Decoration events.

Therefore:

- Multi Tile Editor itself should have no per-frame playback loop.
- FPS independence belongs to PACL2 Orbit Decoration runtime behavior.
- playback speed independence belongs to PACL2 Orbit Decoration runtime behavior.
- mid-level playback/seek should work because the generated events are anchored to deterministic stock floors/times.

Acceptance must include:

- 60 FPS,
- uncapped FPS,
- reduced playback speed such as 0.65x,
- starting playback from the middle of the generated region.

The previously fixed PACL2 stable-handoff behavior is the runtime layer; this generator should not reimplement it.

## 17. Golden sample observations

The hand-built sample contains two independent groups.

### Group `r` / `b`

Initial pivot: `b`

| # | Master floor in sample | Moving | Center | Amount | Duration |
|---|---:|---|---|---:|---:|
| 0 | 1  | r | b | -180° | 1 |
| 1 | 2  | b | r | -135° | 0.75 |
| 2 | 7  | r | b | -135° | 0.75 |
| 3 | 10 | b | r | -90°  | 0.5 |
| 4 | 11 | r | b | -135° | 0.75 |
| 5 | 16 | b | r | -135° | 0.75 |
| 6 | 19 | r | b | -90°  | 0.5 |
| 7 | 20 | b | r | -180° | 1 |

Nominal total duration: `6` beat-units across the sample timing context.

### Group `c` / `d`

Initial pivot: `c`

| # | Master floor in sample | Moving | Center | Amount | Duration |
|---|---:|---|---|---:|---:|
| 0 | 1  | d | c | -180° | 1 |
| 1 | 2  | c | d | -120° | 0.6666666 |
| 2 | 5  | d | c | -120° | 0.6666666 |
| 3 | 8  | c | d | -120° | 0.6666666 |
| 4 | 11 | d | c | -120° | 0.6666666 |
| 5 | 14 | c | d | -120° | 0.6666666 |
| 6 | 17 | d | c | -120° | 0.6666666 |
| 7 | 21 | c | d | -180° | 1 |

Stored decimal total: about `5.9999996`, which is musically intended to coincide with `6`. This is the motivating example for timeline epsilon handling.

### Other golden-sample facts

- Base BPM starts at `100`.
- A common `SetSpeed` changes BPM to `200` at sample floor `2`.
- Both groups can start actions on the same master floor.
- Later actions occur at different master floors because their rhythms differ.
- The sample contains four initial Planet `AddObject` decorations with tags `r`, `b`, `c`, `d`.
- The EQOL-generated `T0_*` / `T1_*` Floor decorations are not required by runtime behavior.

The exact master floor numbers above are useful for regression comparison, but **timing equivalence is the real requirement**. A generated chart may use a different but stock-valid master path if the reconstructed hit times and Orbit behavior match.

## 18. Acceptance tests for the first real generator

The first implementation is not considered usable until all of these pass:

1. **Golden two-group test** — regenerate behavior equivalent to `multitest.adofai` from its two source rhythms.
2. **Three-group test** — prove there is no hard-coded group-count assumption.
3. **Different rhythm lengths** — e.g. 135° vs 120° segments merge into one master timeline correctly.
4. **Simultaneous anchors** — multiple Orbit actions may share one master floor.
5. **Floating convergence** — `5.9999996` and `6.0`-style endpoints converge under tolerance instead of creating accidental micro-timing.
6. **Pivot isolation** — swapping one group's pivot never modifies another group.
7. **Regenerate twice** — no duplicate Orbit actions.
8. **No EQOL installed** — generation and playback still work.
9. **Middle playback** — starting inside the region shows correct planet positions.
10. **60 FPS / uncapped / 0.65x** — no drift attributable to the generated data.
11. **Preflight failure** — incompatible timing maps or missing tags leave the active chart unchanged.

## 19. Implementation boundaries

Suggested modules after the current prototype is refactored:

```text
TrackStore
  capture / save / switch ordinary source snapshots

TrackAnalyzer
  LevelData + stock reconstructed floors
  -> ordered timed source segments

TimelineMerger
  N segment streams
  -> normalized master timeline

MasterPathBuilder
  master timeline + base timing map
  -> candidate stock ADOFAI path

OrbitEmitter
  source segments + group config + master anchors
  -> OrbitDecoration actions

MultiTileGenerator
  preflight -> build copy -> reconstruct -> validate -> atomic commit
```

There should be **no MultiTile runtime renderer** in this architecture.

## 20. Deferred questions

These are intentionally postponed until v1 works:

- automatic planet creation/placement,
- arbitrary `Ease`, `lockRotation`, or radius animation,
- source-chart midspin support beyond what falls out naturally from stock analysis,
- track-specific speed/pause maps,
- selecting a sub-region with both a start and an end cursor,
- preserving/rejoining an arbitrary common suffix,
- branch/DAG editing UI,
- multi-planet groups larger than two,
- merging decoration/gameplay actions from every source track.

If any of these becomes necessary, update this document first, then change code.
