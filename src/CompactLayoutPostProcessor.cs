using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using ADOFAI;
using UnityEngine;

namespace KineticNapier.ADOFAIMultiTileEditor
{
    internal enum CompactWrapMode
    {
        Off,
        Tiles,
        Beats
    }

    internal static class CompactLayoutPostProcessor
    {
        private const string OwnerTag = "adofaiMTEGenerated";
        private const string EndTag = "adofaiMTEEnd";
        private const string TeleportEventTag = "adofaiMTETeleport";
        private const float PositionEpsilon = 0.0005f;
        private const double BeatEpsilon = 1.0e-9;

        internal static CompactWrapMode WrapMode = CompactWrapMode.Tiles;
        internal static int WrapEveryTiles = 32;
        internal static double WrapEveryBeats = 16.0;
        internal const float WrapRowSpacing = 3.0f;

        internal static string WrapSummary
        {
            get
            {
                if (WrapMode == CompactWrapMode.Off) return "Off";
                if (WrapMode == CompactWrapMode.Beats)
                    return WrapEveryBeats.ToString("0.###", CultureInfo.InvariantCulture) + " beats";
                return WrapEveryTiles + " tiles";
            }
        }

        private sealed class TeleportPoint
        {
            internal double Beat;
            internal Vector2 Delta;
        }

        private sealed class SourceLayout
        {
            internal int TrackIndex;
            internal int PreviewSourceOffset;
            internal int ExtraRows;
            internal string PlanetATag;
            internal string PlanetBTag;
            internal readonly List<Vector2> ActualLocal = new List<Vector2>();
            internal readonly List<Vector2> WrappedLocal = new List<Vector2>();
            internal readonly List<Vector2> BaseLocal = new List<Vector2>();
            internal readonly List<TeleportPoint> Teleports = new List<TeleportPoint>();
        }

        internal static string ApplyAndCommit(
            scnEditor editor,
            IList<TrackSlot> tracks,
            GenerationPlan plan)
        {
            if (editor == null || editor.levelData == null)
                throw new InvalidOperationException("Editor is not ready.");
            if (tracks == null || plan == null || tracks.Count != plan.Tracks.Count)
                throw new InvalidOperationException("Compact layout inputs no longer match the analyzed plan.");

            ValidateSettings();

            LevelData output = editor.levelData.Copy();
            int selectedFloor = GameAngleProbe.TryGetSelectedFloorIndex(editor);
            List<SourceLayout> layouts = CaptureLayouts(editor, tracks, plan, output, selectedFloor);
            LevelData candidate = output.Copy();

            IList decorations = candidate.decorations as IList;
            IList levelEvents = candidate.levelEvents as IList;
            if (decorations == null || levelEvents == null)
                throw new InvalidOperationException("LevelData lists are not compatible with compact layout generation.");

            int removedTeleports = RemoveOwnedTeleportEvents(levelEvents);
            int movedTiles = 0;
            int wrappedRows = 0;
            int emittedTeleports = 0;

            for (int i = 0; i < layouts.Count; i++)
            {
                SourceLayout layout = layouts[i];
                movedTiles += ApplyPreviewLayout(decorations, layout);
                wrappedRows += layout.ExtraRows;
                emittedTeleports += EmitTeleports(levelEvents, plan, layout);
            }

            bool committed = false;
            try
            {
                TrackStore.RestoreSnapshot(editor, candidate, true);
                editor.ApplyEventsToFloors();
                editor.UpdateDecorationObjects();
                committed = true;

                string wrapText;
                if (WrapMode == CompactWrapMode.Off)
                {
                    wrapText = "Compact layout: wrapping off; kept source preview geometry";
                }
                else
                {
                    wrapText = "Compact layout: auto-wrapped source previews every " + WrapSummary
                        + " (" + wrappedRows + " extra row(s), " + movedTiles + " preview tile move(s))";
                }

                return wrapText + "; emitted " + emittedTeleports
                    + " instant planet teleport(s) for Position Track"
                    + (WrapMode == CompactWrapMode.Off ? "" : " / row-wrap transitions")
                    + (removedTeleports > 0 ? "; replaced " + removedTeleports + " previous teleport event(s)." : ".");
            }
            finally
            {
                if (!committed)
                {
                    TrackStore.RestoreSnapshot(editor, output, true);
                    if (selectedFloor >= 0 && selectedFloor < editor.floors.Count)
                        editor.SelectFloor(editor.floors[selectedFloor], true);
                }
            }
        }

        private static void ValidateSettings()
        {
            if (WrapMode == CompactWrapMode.Tiles && WrapEveryTiles <= 0)
                throw new InvalidOperationException("Auto Wrap tile length must be at least 1, or set wrap mode to Off.");
            if (WrapMode == CompactWrapMode.Beats
                && (!(WrapEveryBeats > BeatEpsilon) || double.IsNaN(WrapEveryBeats) || double.IsInfinity(WrapEveryBeats)))
                throw new InvalidOperationException("Auto Wrap beat length must be positive, or set wrap mode to Off.");
        }

        private static List<SourceLayout> CaptureLayouts(
            scnEditor editor,
            IList<TrackSlot> tracks,
            GenerationPlan plan,
            LevelData output,
            int selectedFloor)
        {
            var result = new List<SourceLayout>();
            try
            {
                for (int t = 0; t < tracks.Count; t++)
                {
                    TrackSlot slot = tracks[t];
                    if (slot == null || slot.Data == null)
                        throw new InvalidOperationException("Track #" + (t + 1) + " has no source snapshot.");

                    TrackStore.RestoreSnapshot(editor, slot.Data, false);
                    result.Add(CaptureCurrentTrack(editor, slot, plan.Tracks[t], t));
                }
            }
            finally
            {
                TrackStore.RestoreSnapshot(editor, output, true);
                if (selectedFloor >= 0 && selectedFloor < editor.floors.Count)
                    editor.SelectFloor(editor.floors[selectedFloor], true);
            }
            return result;
        }

        private static SourceLayout CaptureCurrentTrack(
            scnEditor editor,
            TrackSlot slot,
            AnalyzedTrack analyzed,
            int trackIndex)
        {
            if (slot.RegionStartFloor < 0 || slot.RegionStartFloor >= editor.floors.Count)
                throw new InvalidOperationException("Track '" + slot.Name + "' compact-layout start is outside its source path.");

            scrFloor requestedStart = editor.floors[slot.RegionStartFloor];
            if (requestedStart == null || requestedStart.midSpin)
                throw new InvalidOperationException("Track '" + slot.Name + "' compact-layout start must be landable.");

            var floors = new List<scrFloor>();
            int regionIndex = -1;
            for (int i = 0; i < editor.floors.Count; i++)
            {
                scrFloor floor = editor.floors[i];
                if (floor == null || floor.midSpin) continue;
                if (ReferenceEquals(floor, requestedStart)) regionIndex = floors.Count;
                floors.Add(floor);
            }

            if (regionIndex < 0 || regionIndex + 1 >= floors.Count)
                throw new InvalidOperationException("Track '" + slot.Name + "' compact-layout region could not be reconstructed.");

            int availableSegments = floors.Count - regionIndex - 1;
            if (availableSegments != analyzed.Segments.Count)
                throw new InvalidOperationException("Track '" + slot.Name + "' source floor count changed after analysis.");

            float tileSize = ResolveTileSize();
            Vector2 start = ToLevelPosition(floors[regionIndex], tileSize);
            var layout = new SourceLayout
            {
                TrackIndex = trackIndex,
                PreviewSourceOffset = regionIndex > 0 ? 0 : 1,
                PlanetATag = analyzed.PlanetATag,
                PlanetBTag = analyzed.PlanetBTag
            };

            for (int i = regionIndex; i < floors.Count; i++)
                layout.ActualLocal.Add(ToLevelPosition(floors[i], tileSize) - start);

            BuildWrappedPositions(layout, analyzed);
            BuildNaturalBasePositions(floors, regionIndex, layout.BaseLocal);

            for (int s = 0; s < analyzed.Segments.Count; s++)
            {
                Vector2 currentOffset = layout.WrappedLocal[s] - layout.BaseLocal[s];
                Vector2 nextOffset = layout.WrappedLocal[s + 1] - layout.BaseLocal[s + 1];
                Vector2 delta = nextOffset - currentOffset;
                if (delta.sqrMagnitude <= PositionEpsilon * PositionEpsilon) continue;

                layout.Teleports.Add(new TeleportPoint
                {
                    Beat = analyzed.Segments[s].EndBeat,
                    Delta = delta
                });
            }

            return layout;
        }

        private static void BuildNaturalBasePositions(
            IList<scrFloor> floors,
            int regionIndex,
            IList<Vector2> destination)
        {
            destination.Clear();
            destination.Add(Vector2.zero);

            for (int i = regionIndex; i + 1 < floors.Count; i++)
            {
                scrFloor floor = floors[i];
                float heading = 90f - (float)(floor.exitangle * Mathf.Rad2Deg);
                float radians = heading * Mathf.Deg2Rad;
                Vector2 step = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
                if (step.sqrMagnitude <= 1.0e-8f) step = Vector2.right;
                else step.Normalize();
                destination.Add(destination[destination.Count - 1] + step);
            }
        }

        private static void BuildWrappedPositions(SourceLayout layout, AnalyzedTrack analyzed)
        {
            layout.WrappedLocal.Clear();
            layout.ExtraRows = 0;
            IList<Vector2> source = layout.ActualLocal;
            if (source == null || source.Count == 0) return;

            if (WrapMode == CompactWrapMode.Off)
            {
                for (int i = 0; i < source.Count; i++) layout.WrappedLocal.Add(source[i]);
                return;
            }

            var blockByIndex = new List<int>(source.Count);
            int maxBlock = 0;

            if (WrapMode == CompactWrapMode.Tiles)
            {
                int length = Math.Max(1, WrapEveryTiles);
                for (int i = 0; i < source.Count; i++)
                {
                    int block = i / length;
                    blockByIndex.Add(block);
                    if (block > maxBlock) maxBlock = block;
                }
            }
            else
            {
                double cumulativeSourceBeats = 0.0;
                blockByIndex.Add(0);
                for (int i = 1; i < source.Count; i++)
                {
                    int segmentIndex = i - 1;
                    if (segmentIndex < analyzed.Segments.Count)
                        cumulativeSourceBeats += Math.Max(0.0, analyzed.Segments[segmentIndex].SourceDurationBeats);
                    int block = (int)Math.Floor((cumulativeSourceBeats + BeatEpsilon) / WrapEveryBeats);
                    if (block < 0) block = 0;
                    blockByIndex.Add(block);
                    if (block > maxBlock) maxBlock = block;
                }
            }

            var firstIndexByBlock = new Dictionary<int, int>();
            for (int i = 0; i < blockByIndex.Count; i++)
            {
                int block = blockByIndex[i];
                if (!firstIndexByBlock.ContainsKey(block)) firstIndexByBlock[block] = i;
            }

            for (int i = 0; i < source.Count; i++)
            {
                int block = blockByIndex[i];
                int blockStart = firstIndexByBlock[block];
                Vector2 rowOrigin = source[0] + new Vector2(0f, -block * WrapRowSpacing);
                layout.WrappedLocal.Add(rowOrigin + (source[i] - source[blockStart]));
            }

            layout.ExtraRows = maxBlock;
        }

        private static int ApplyPreviewLayout(IList decorations, SourceLayout layout)
        {
            LevelEvent first = FindOwnedPreview(decorations, layout.TrackIndex, 0);
            if (first == null || layout.ActualLocal.Count == 0) return 0;

            int firstSource = layout.PreviewSourceOffset;
            if (firstSource < 0 || firstSource >= layout.ActualLocal.Count) return 0;

            Vector2 originalFirst = ReadVector2Data(first, "position", Vector2.zero);
            Vector2 origin = originalFirst - layout.ActualLocal[firstSource];
            int moved = 0;

            for (int i = 0; ; i++)
            {
                LevelEvent preview = FindOwnedPreview(decorations, layout.TrackIndex, i);
                if (preview == null) break;
                int sourceIndex = layout.PreviewSourceOffset + i;
                if (sourceIndex < 0 || sourceIndex >= layout.WrappedLocal.Count) break;

                Vector2 target = origin + layout.WrappedLocal[sourceIndex];
                Vector2 current = ReadVector2Data(preview, "position", target);
                if ((current - target).sqrMagnitude > PositionEpsilon * PositionEpsilon)
                {
                    SetTypedData(preview, "position", target);
                    moved++;
                }
            }

            LevelEvent end = FindOwnedEndTile(decorations, layout.TrackIndex);
            if (end != null && layout.WrappedLocal.Count > 0)
            {
                Vector2 target = origin + layout.WrappedLocal[layout.WrappedLocal.Count - 1];
                Vector2 current = ReadVector2Data(end, "position", target);
                if ((current - target).sqrMagnitude > PositionEpsilon * PositionEpsilon)
                {
                    SetTypedData(end, "position", target);
                    moved++;
                }
            }

            return moved;
        }

        private static int EmitTeleports(IList levelEvents, GenerationPlan plan, SourceLayout layout)
        {
            int emitted = 0;
            for (int i = 0; i < layout.Teleports.Count; i++)
            {
                TeleportPoint point = layout.Teleports[i];
                int anchor = TimelineMerger.FindAnchorIndex(plan.Anchors, point.Beat);
                if (anchor < 0)
                    throw new InvalidOperationException("Position/wrap teleport could not be mapped to the master timeline for track #" + (layout.TrackIndex + 1) + ".");

                int outputFloor = plan.RegionStartFloor + anchor;
                LevelEvent move = CreateCustomEvent("MoveDecorations", outputFloor);
                SetRequiredData(move, "duration", 0f);
                SetRequiredData(move, "tag", layout.PlanetATag + " " + layout.PlanetBTag);
                SetRequiredData(move, "relativeTo", "LastPosition");
                SetRequiredData(move, "positionOffset", point.Delta);
                SetOptionalData(move, "angleOffset", 0f);
                SetOptionalData(move, "ease", "Linear");
                SetOptionalData(move, "eventTag", TeleportEventTag);
                SetOptionalData(move, "active", true);
                InsertBeforeOrbitAtFloor(levelEvents, move, outputFloor);
                emitted++;
            }
            return emitted;
        }

        private static int RemoveOwnedTeleportEvents(IList levelEvents)
        {
            int removed = 0;
            for (int i = levelEvents.Count - 1; i >= 0; i--)
            {
                LevelEvent ev = levelEvents[i] as LevelEvent;
                if (ev == null) continue;
                string eventTag = Convert.ToString(SafeGetData(ev, "eventTag"), CultureInfo.InvariantCulture) ?? "";
                if (!ContainsTagToken(eventTag, TeleportEventTag)) continue;
                levelEvents.RemoveAt(i);
                removed++;
            }
            return removed;
        }

        private static void InsertBeforeOrbitAtFloor(IList events, LevelEvent move, int floor)
        {
            int insert = events.Count;
            for (int i = 0; i < events.Count; i++)
            {
                LevelEvent ev = events[i] as LevelEvent;
                if (ev == null) continue;
                if (ev.floor > floor)
                {
                    insert = i;
                    break;
                }
                if (ev.floor == floor && IsEventNamed(ev, "OrbitDecoration"))
                {
                    insert = i;
                    break;
                }
            }
            events.Insert(insert, move);
        }

        private static LevelEvent FindOwnedPreview(IList decorations, int trackIndex, int tileIndex)
        {
            string token = "T" + trackIndex + "_" + tileIndex;
            for (int i = 0; i < decorations.Count; i++)
            {
                LevelEvent ev = decorations[i] as LevelEvent;
                if (ev == null || !IsEventNamed(ev, "AddObject")) continue;
                string objectType = Convert.ToString(SafeGetData(ev, "objectType"), CultureInfo.InvariantCulture) ?? "";
                if (!string.Equals(objectType, "Floor", StringComparison.OrdinalIgnoreCase)) continue;
                string tag = Convert.ToString(SafeGetData(ev, "tag"), CultureInfo.InvariantCulture) ?? "";
                if (ContainsTagToken(tag, OwnerTag) && ContainsTagToken(tag, token)) return ev;
            }
            return null;
        }

        private static LevelEvent FindOwnedEndTile(IList decorations, int trackIndex)
        {
            string token = "T" + trackIndex + "_end";
            for (int i = 0; i < decorations.Count; i++)
            {
                LevelEvent ev = decorations[i] as LevelEvent;
                if (ev == null || !IsEventNamed(ev, "AddObject")) continue;
                string tag = Convert.ToString(SafeGetData(ev, "tag"), CultureInfo.InvariantCulture) ?? "";
                if (!ContainsTagToken(tag, OwnerTag) || !ContainsTagToken(tag, EndTag)) continue;
                if (ContainsTagToken(tag, token)) return ev;
            }
            return null;
        }

        private static bool ContainsTagToken(string tags, string token)
        {
            if (string.IsNullOrEmpty(tags) || string.IsNullOrEmpty(token)) return false;
            string[] split = tags.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < split.Length; i++)
                if (string.Equals(split[i], token, StringComparison.Ordinal)) return true;
            return false;
        }

        private static LevelEvent CreateCustomEvent(string requestedName, int floor)
        {
            LevelEventInfo info = ResolveEventInfo(requestedName);
            return new LevelEvent(floor, info.type, info);
        }

        private static LevelEventInfo ResolveEventInfo(string requestedName)
        {
            if (GCS.levelEventsInfo == null)
                throw new InvalidOperationException("ADOFAI level-event metadata is not initialized.");

            LevelEventInfo direct;
            if (GCS.levelEventsInfo.TryGetValue(requestedName, out direct) && direct != null) return direct;

            string target = NormalizeEventName(requestedName);
            LevelEventInfo suffixMatch = null;
            foreach (KeyValuePair<string, LevelEventInfo> pair in GCS.levelEventsInfo)
            {
                LevelEventInfo info = pair.Value;
                if (info == null) continue;
                string keyNormalized = NormalizeEventName(pair.Key);
                string infoNormalized = NormalizeEventName(info.name);
                if (keyNormalized == target || infoNormalized == target) return info;
                if (keyNormalized.EndsWith(target, StringComparison.Ordinal)
                    || infoNormalized.EndsWith(target, StringComparison.Ordinal)) suffixMatch = info;
            }
            if (suffixMatch != null) return suffixMatch;
            throw new InvalidOperationException("Event metadata '" + requestedName + "' is unavailable.");
        }

        private static string NormalizeEventName(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            var sb = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
            }
            return sb.ToString();
        }

        private static bool IsEventNamed(LevelEvent ev, string requestedName)
        {
            if (ev == null) return false;
            string target = NormalizeEventName(requestedName);
            string infoName = ev.info != null ? NormalizeEventName(ev.info.name) : "";
            if (infoName == target || infoName.EndsWith(target, StringComparison.Ordinal)) return true;
            return NormalizeEventName(ev.eventType.ToString()) == target;
        }

        private static void SetRequiredData(LevelEvent ev, string key, object value)
        {
            if (ev == null || ev.info == null || ev.info.propertiesInfo == null || !ev.info.propertiesInfo.ContainsKey(key))
                throw new InvalidOperationException("Event '" + EventDisplayName(ev) + "' has no property '" + key + "'.");
            SetTypedData(ev, key, value);
        }

        private static void SetOptionalData(LevelEvent ev, string key, object value)
        {
            if (ev == null || ev.info == null || ev.info.propertiesInfo == null || !ev.info.propertiesInfo.ContainsKey(key)) return;
            SetTypedData(ev, key, value);
        }

        private static void SetTypedData(LevelEvent ev, string key, object value)
        {
            object current = SafeGetData(ev, key);
            Type targetType = current != null ? current.GetType() : null;
            if (targetType == null && ev.info != null && ev.info.propertiesInfo != null)
            {
                ADOFAI.PropertyInfo propertyInfo;
                if (ev.info.propertiesInfo.TryGetValue(key, out propertyInfo)
                    && propertyInfo != null && propertyInfo.value_default != null)
                    targetType = propertyInfo.value_default.GetType();
            }

            ev[key] = targetType == null ? value : ConvertFor(value, targetType);
            if (ev.disabled != null && ev.disabled.ContainsKey(key)) ev.disabled[key] = false;
        }

        private static object ConvertFor(object value, Type targetType)
        {
            if (value == null || targetType == null) return value;
            Type actual = Nullable.GetUnderlyingType(targetType) ?? targetType;
            if (actual.IsInstanceOfType(value)) return value;
            if (actual.IsEnum)
            {
                string text = Convert.ToString(value, CultureInfo.InvariantCulture);
                return Enum.Parse(actual, text, true);
            }
            if (actual == typeof(string)) return Convert.ToString(value, CultureInfo.InvariantCulture);
            if (actual == typeof(Vector2) && value is Vector2) return value;
            return Convert.ChangeType(value, actual, CultureInfo.InvariantCulture);
        }

        private static object SafeGetData(LevelEvent ev, string key)
        {
            if (ev == null) return null;
            try { return ev[key]; } catch { return null; }
        }

        private static Vector2 ReadVector2Data(LevelEvent ev, string key, Vector2 fallback)
        {
            object value = SafeGetData(ev, key);
            if (value is Vector2) return (Vector2)value;
            if (value is Vector3)
            {
                Vector3 v = (Vector3)value;
                return new Vector2(v.x, v.y);
            }
            return fallback;
        }

        private static string EventDisplayName(LevelEvent ev)
        {
            if (ev == null) return "<null>";
            if (ev.info != null && !string.IsNullOrEmpty(ev.info.name)) return ev.info.name;
            return ev.eventType.ToString();
        }

        private static float ResolveTileSize()
        {
            float tileSize = ADOBase.controller == null ? 1f : ADOBase.controller.tileSize;
            return Mathf.Abs(tileSize) < 0.000001f ? 1f : tileSize;
        }

        private static Vector2 ToLevelPosition(scrFloor floor, float tileSize)
        {
            Vector3 p = floor.transform.position;
            return new Vector2(p.x / tileSize, p.y / tileSize);
        }
    }
}
