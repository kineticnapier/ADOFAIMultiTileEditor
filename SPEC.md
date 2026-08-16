# ADOFAI Multi Tile Editor — v1 Specification

Status: **Draft / implementation contract**

This document defines the first useful multi-tile workflow. It is based on the hand-built `multitest.adofai` golden sample and on behavior verified through the stock ADOFAI editor.

The core architectural rule is:

> Multi Tile Editor is a converter, not another renderer. It converts several ordinary two-planet source charts into one ordinary ADOFAI master timeline plus PACL2 `AddObject` / `OrbitDecoration` data.

## 1. Goal

Given any number of independently edited two-planet source tracks, generate a single playable chart where every group follows its own source rhythm at the same time.

The mod should automate:

- source-track storage and switching,
- extracting each track's actual reconstructed movement/timing,
- merging all hit times into one master timeline,
- synthesizing a stock-valid master path,
- alternating each group's moving/pivot planet,
- creating missing initial PACL2 planet decorations,
- calculating and placing every PACL2 `OrbitDecoration`,
- committing only after the complete candidate validates.

The stock ADOFAI editor remains the chart editor. PACL2 remains responsible for runtime Orbit playback.

## 2. Explicit non-goals for v1

The following remain out of scope:

- external editor,
- custom multi-planet renderer,
- per-frame planet simulation in this mod,
- arbitrary radius animation (`dstRadiusMultiplier` stays `1`),
- groups larger than two planets,
- VS Code-style split branch UI,
- DAG/shared-node editing,
- arbitrary per-track gameplay/decor event merging,
- track-specific `SetSpeed`, `Pause`, or incompatible timing maps,
- reproducing the exact visual geometry or exact floor numbers of the golden sample,
- automatically recreating the EQOL helper-floor visualization.

## 3. Terminology

### Source track

A normal ADOFAI chart snapshot edited through the stock editor. One source track represents one independent two-planet group.

### Group

A pair of PACL2 Planet `AddObject` decorations with:

- Planet A tag,
- Planet B tag,
- initial pivot (`A` or `B`).

A group's pivot state is completely independent of every other group.

### Segment

One movement from one source-track hit to the next. A segment contains at least:

- source floor/index,
- start/end musical time,
- signed travel angle,
- Orbit duration,
- moving tag,
- center/pivot tag.

### Master timeline

The ordered union of segment start/boundary times from every source track.

### Master path

The ordinary ADOFAI `angleData` path synthesized so its floors occur at the master timeline instants.

### Golden sample

The hand-built `multitest.adofai` reference chart.

## 4. User workflow

1. Edit one source chart in the stock editor.
2. Store it as a track.
3. Edit/store the next source chart.
4. Repeat for any number of tracks.
5. Configure each track's Planet A tag, Planet B tag, and initial pivot.
6. Run whole-region analysis.
7. Verify the synthesized master path.
8. Generate the output in one operation.

The old per-step `Generate Multi Tile Step` model is superseded. Generation is a **whole-region conversion**.

Initial planet decorations do not need to be manually created when both are absent: the generator creates them automatically. Existing complete A/B pairs are preserved.

## 5. Source-track constraints

All stored source tracks MUST:

- share the same song/timing context,
- use the same common BPM/timing-affecting map,
- start the generated region at the same musical time,
- finish at the same musical time within timeline epsilon,
- represent exactly one two-planet group each.

Tracks MAY have different floor counts, rhythms, and travel angles.

Track count is arbitrary. The implementation MUST NOT assume exactly two groups.

### Timing-event restriction

v1 does not merge competing timing maps. Incompatible track-specific speed/pause behavior MUST fail preflight instead of being guessed.

## 6. Source-track analysis

Analysis MUST use stock reconstructed floor data whenever possible.

For each source segment the analyzer obtains:

- start beat,
- end beat,
- signed travel angle,
- PACL2 duration,
- source floor for diagnostics.

`scrFloor.angleLength` is radians in the current build. It belongs to the movement **leaving** that floor.

The analyzer currently uses reconstructed `angleLength`, `entryBeat`, `isCCW`, and speed state. It MUST NOT infer the real movement from raw `angleData` subtraction alone.

### Duration

Duration is per segment, not shared between groups.

The golden sample contains ordinary cases where:

```text
duration ≈ abs(amount) / 180
```

Examples:

- 180° -> 1
- 135° -> 0.75
- 120° -> 0.6666666...
- 90° -> 0.5

Game reconstruction remains the source of truth.

## 7. Timeline merge

The generator merges source segment boundaries by musical time, never by source floor number.

Conceptually:

```text
Track A: 0 ---- 1 ---- 1.75 ---- 2.5 ---- ...
Track B: 0 ---- 1 ---- 1.666... ---- 2.333... ---- ...

Master : 0 ---- 1 ---- 1.666... -- 1.75 -- 2.333... -- 2.5 -- ...
```

Rules:

- simultaneous source starts share one master anchor,
- several Orbit actions may share one master floor,
- event ordering between groups is deterministic,
- no frame-count/stability logic belongs in this generator.

### Timeline epsilon

Initial v1 tolerance:

```text
1e-6 beat
```

This intentionally merges decimal representations such as `5.9999996` and `6.0` when they are musically intended to coincide.

## 8. Master path synthesis

The builder converts adjacent master-anchor gaps into ordinary ADOFAI `angleData` and verifies the result through stock `RemakePath` reconstruction.

Requirements:

- every master instant must reconstruct within tolerance,
- visual geometry is secondary,
- exact output floor numbers are not part of the public contract,
- stock `angleLength` and `entryBeat`/cumulative timing are checked before commit.

Current synthesis supports at most `360°` / `2 beats` between adjacent master anchors. Longer gaps are deferred until helper/midspin insertion is implemented.

## 9. Group / pivot state

For a group with tags `A` and `B`:

```text
pivot B -> move A around B -> pivot becomes A
pivot A -> move B around A -> pivot becomes B
```

The pivot swaps once per emitted source segment for that group only.

Initial pivot is configurable per track.

## 10. Orbit Decoration emission

For every analyzed source segment, emit exactly one PACL2 `OrbitDecoration` at the corresponding master anchor.

Semantic fields:

```json
{
  "eventType": "OrbitDecoration",
  "duration": "<segment duration>",
  "tag": "<moving tag>",
  "centerTag": "<pivot tag>",
  "amount": "<signed travel angle>",
  "lockRotation": false,
  "dstRadiusMultiplier": 1,
  "ease": "Linear",
  "angleOffset": 0,
  "eventTag": ""
}
```

v1 fixes:

- `dstRadiusMultiplier = 1`
- `ease = Linear`
- `lockRotation = false`
- `angleOffset = 0`
- `eventTag = ""`

### Typed custom-event data

Generated PACL2 data MUST retain the types declared by ADOFAI/PACL2 event metadata.

In particular, generation MUST NOT leave values such as `duration`/`amount` as `Double` or `ease` as raw `String` when PACL2 expects `Single`/enum values. The generated chart must work immediately without requiring a save/reload Decode cycle.

### Template handling

If an existing configured Orbit event exists, it may be used as a compatibility template.

If none exists, the generator SHOULD create a typed temporary Orbit event from registered PACL2 metadata. The user should not need to author a dummy Orbit manually.

Every v1-owned field is explicitly overwritten before emission.

## 11. Planet decoration generation

For every configured group, the output must end with exactly one Planet `AddObject` for A and one for B.

Rules:

- if both A and B already exist, preserve them unchanged;
- if neither exists, create both automatically;
- if only one exists, fail rather than guessing the missing half's relation to a manually positioned planet;
- duplicate configured tags are an error;
- A/B tags must be distinct and unique across groups.

### Automatic layout

Auto-created groups use a deterministic grid.

Within each group:

- the configured initial pivot is the center planet,
- the moving planet starts one tile to the left of the center,
- Planet A defaults to `DefaultRed`,
- Planet B defaults to `DefaultBlue`,
- the objects are Tile-relative and use ordinary PACL2 Planet defaults for non-owned visual fields.

The user can restyle/reposition complete pairs later through normal PACL2 editing; regeneration preserves existing complete pairs.

This is still data generation, not a runtime renderer.

## 12. EQOL independence

The golden sample contains EQOL-generated Floor `AddObject` helpers with tags such as:

```text
T0
T0_0
T1
T1_0
qolMultiTile_T0
qolMultiTileRhythm_...
```

They are reference/visualization data only.

Multi Tile Editor MUST NOT require EQOL, parse those helpers as timing truth, or recreate them as part of core generation.

## 13. Non-Orbit actions

The generator owns the master path, configured planet setup, and configured Orbit actions.

Common/base actions are preserved once from the active base track and remapped from source musical time to the corresponding master anchor.

Source geometry actions such as `Twirl`, `MultiPlanet`, `Pause`, `Hold`, and `FreeRoam*` are not copied onto the synthesized master path.

v1 does not merge arbitrary actions from every source track.

## 14. Atomic generation

Generation is all-or-nothing.

Preflight validates at least:

- stored/analyzable tracks,
- equal region start/end timing,
- compatible timing maps,
- valid unique A/B tags,
- PACL2 `AddObject` and `OrbitDecoration` metadata availability,
- a synthesizable master path,
- no duplicate/half-existing configured planet pairs.

The generator builds a candidate copy, reconstructs and verifies it, then replaces the active editor state.

Failure MUST restore the original chart.

Generation MUST NOT mutate stored source snapshots, TrackStore cursors, or stored pivot configuration.

A successful generation SHOULD behave as one editor operation and then detach the editor from the source-track binding so the generated output cannot overwrite a source snapshot accidentally.

## 15. Regeneration / idempotence

Running generation twice with identical inputs MUST NOT duplicate configured Orbit actions or auto-created planet pairs.

Previously generated configured Orbit actions are replaced.

Existing complete configured planet pairs are preserved.

## 16. Playback and seeking

The output is an ordinary ADOFAI chart plus PACL2 data.

Therefore Multi Tile Editor itself has no per-frame playback loop.

Runtime acceptance includes:

- 60 FPS,
- uncapped FPS,
- reduced playback speed such as 0.65x,
- starting playback from the middle of the generated region.

FPS/playback-speed stability belongs to the patched PACL2 Orbit runtime, not to this generator.

## 17. Golden sample observations

The hand-built sample contains two independent groups.

### Group `r` / `b`

Initial pivot: `b`

| # | Sample floor | Moving | Center | Amount | Duration |
|---|---:|---|---|---:|---:|
| 0 | 1  | r | b | -180° | 1 |
| 1 | 2  | b | r | -135° | 0.75 |
| 2 | 7  | r | b | -135° | 0.75 |
| 3 | 10 | b | r | -90° | 0.5 |
| 4 | 11 | r | b | -135° | 0.75 |
| 5 | 16 | b | r | -135° | 0.75 |
| 6 | 19 | r | b | -90° | 0.5 |
| 7 | 20 | b | r | -180° | 1 |

### Group `c` / `d`

Initial pivot: `c`

| # | Sample floor | Moving | Center | Amount | Duration |
|---|---:|---|---|---:|---:|
| 0 | 1  | d | c | -180° | 1 |
| 1 | 2  | c | d | -120° | 0.6666666 |
| 2 | 5  | d | c | -120° | 0.6666666 |
| 3 | 8  | c | d | -120° | 0.6666666 |
| 4 | 11 | d | c | -120° | 0.6666666 |
| 5 | 14 | c | d | -120° | 0.6666666 |
| 6 | 17 | d | c | -120° | 0.6666666 |
| 7 | 21 | c | d | -180° | 1 |

Other facts:

- nominal total duration is 6 beats,
- repeated `0.6666666` stores about `5.9999996`, motivating timeline epsilon,
- base BPM starts at 100,
- a common SetSpeed changes BPM to 200,
- the sample has four Planet `AddObject`s (`r`, `b`, `c`, `d`),
- EQOL `T0_*` / `T1_*` Floor objects are not runtime requirements.

The exact sample floor numbers are diagnostic only; timing/Orbit equivalence is the real contract.

## 18. Acceptance tests

1. **Golden two-group** — behavior equivalent to `multitest.adofai`.
2. **Zero-manual-decoration** — start with no configured Planet AddObjects and no dummy Orbit; generation creates everything required.
3. **Immediate playback** — generated Orbit works before save/reload.
4. **Three groups** — no hard-coded group-count assumption.
5. **Different rhythms** — e.g. 135° and 120° streams merge correctly.
6. **Simultaneous anchors** — multiple Orbit actions share a master floor safely.
7. **Floating convergence** — near-equal endpoints merge under epsilon.
8. **Pivot isolation** — one group's pivot never alters another's state.
9. **Regenerate twice** — no duplicates.
10. **Existing complete planet pair** — preserve its manual appearance/position.
11. **Half-existing pair** — reject without modifying the chart.
12. **No EQOL** — generation/playback still work.
13. **Middle playback** — correct state when starting inside the region.
14. **60 FPS / uncapped / 0.65x** — no generated-data drift.
15. **Preflight failure** — incompatible inputs leave the chart unchanged.

## 19. Implementation boundaries

```text
TrackStore
  capture / save / switch source snapshots

TrackAnalyzer
  stock reconstructed floors -> timed source segments

TimelineMerger
  N segment streams -> normalized master timeline

MasterPathBuilder
  master timeline -> verified stock ADOFAI angleData

PACL2AutoGenerator
  create/preserve configured planet pairs
  create typed Orbit template when necessary
  normalize PACL2 event data for immediate playback

OrbitEmitter
  source segments + master anchors -> OrbitDecoration actions

Generation
  preflight -> candidate -> reconstruct -> validate -> atomic commit
```

There is **no MultiTile runtime renderer** in this architecture.

## 20. Deferred questions

Deferred until the current generator is stable:

- customizable auto-layout spacing/origin,
- per-group default colors/skins during auto creation,
- arbitrary `Ease`, `lockRotation`, or radius animation,
- source-chart midspin edge cases beyond stock analysis,
- track-specific speed/pause maps,
- selecting arbitrary sub-regions,
- preserving/rejoining arbitrary common suffixes,
- branch/DAG editing UI,
- multi-planet groups larger than two,
- merging arbitrary decoration/gameplay actions from every source track.

If one of these becomes necessary, update this document before changing implementation.
