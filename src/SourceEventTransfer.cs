using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using R = System.Reflection;
using ADOFAI;

namespace KineticNapier.ADOFAIMultiTileEditor
{
    internal sealed class SourceEventTransferResult
    {
        internal int CatalogTypes;
        internal int MoveTrackEvents;
        internal int EmittedMoveDecorations;
        internal int SkippedMoveTrackEvents;
        internal int ReplacedGeneratedEvents;
        internal string Diagnostic;
    }

    internal static class SourceEventTransfer
    {
        private const string OwnerEventTag = "adofaiMTEMoveTrack";

        internal static SourceEventTransferResult ApplyAndCommit(
            scnEditor editor,
            IList<TrackSlot> tracks,
            GenerationPlan plan)
        {
            if (editor == null || editor.levelData == null)
                throw new InvalidOperationException("Editor is not ready.");
            if (tracks == null || plan == null || tracks.Count != plan.Tracks.Count)
                throw new InvalidOperationException("Source-event inputs no longer match the analyzed plan.");

            LevelData original = editor.levelData.Copy();
            LevelData candidate = original.Copy();
            IList outputEvents = candidate.levelEvents as IList;
            if (outputEvents == null)
                throw new InvalidOperationException("LevelData.levelEvents is not list-compatible in this game build.");

            var result = new SourceEventTransferResult();
            result.CatalogTypes = CountRuntimeEventCatalog();
            result.ReplacedGeneratedEvents = RemovePreviouslyGenerated(outputEvents);

            for (int trackIndex = 0; trackIndex < tracks.Count; trackIndex++)
                TranslateTrack(outputEvents, tracks[trackIndex], plan.Tracks[trackIndex], plan, trackIndex, result);

            bool committed = false;
            int selectedFloor = GameAngleProbe.TryGetSelectedFloorIndex(editor);
            try
            {
                TrackStore.RestoreSnapshot(editor, candidate, true);
                editor.ApplyEventsToFloors();
                editor.UpdateDecorationObjects();
                committed = true;
            }
            finally
            {
                if (!committed)
                {
                    TrackStore.RestoreSnapshot(editor, original, true);
                    if (selectedFloor >= 0 && selectedFloor < editor.floors.Count)
                        editor.SelectFloor(editor.floors[selectedFloor], true);
                }
            }

            result.Diagnostic = "Source events: ADOFAI runtime catalog=" + result.CatalogTypes
                + " type(s); translated " + result.MoveTrackEvents + " MoveTrack source event(s) into "
                + result.EmittedMoveDecorations + " generated-tile MoveDecorations action(s)"
                + (result.SkippedMoveTrackEvents > 0 ? "; skipped " + result.SkippedMoveTrackEvents + " unsupported MoveTrack range(s)" : "")
                + (result.ReplacedGeneratedEvents > 0 ? "; replaced " + result.ReplacedGeneratedEvents + " previous generated action(s)." : ".");
            return result;
        }

        internal static string GetCompatibilitySummary()
        {
            int catalog = CountRuntimeEventCatalog();
            return "Event compatibility: runtime catalog " + catalog
                + " type(s). Supported now: SetSpeed (baked timing), Twirl (baked orbit direction), Pause (stationary timing), "
                + "PositionTrack position (rigid planet/layout teleport), MoveTrack ThisTile ranges (generated tile movement). "
                + "Hold / FreeRoam / MultiPlanet still require dedicated converters.";
        }

        private static void TranslateTrack(
            IList outputEvents,
            TrackSlot slot,
            AnalyzedTrack analyzed,
            GenerationPlan plan,
            int trackIndex,
            SourceEventTransferResult result)
        {
            if (slot == null || slot.Data == null || analyzed == null) return;
            IList sourceEvents = slot.Data.levelEvents as IList;
            if (sourceEvents == null) return;

            for (int i = 0; i < sourceEvents.Count; i++)
            {
                LevelEvent source = sourceEvents[i] as LevelEvent;
                if (source == null || !IsEventNamed(source, "MoveTrack")) continue;
                if (source.floor < slot.RegionStartFloor) continue;

                result.MoveTrackEvents++;
                int gapLength = ReadIntData(source, "gapLength", 0);
                if (gapLength != 0)
                {
                    result.SkippedMoveTrackEvents++;
                    continue;
                }

                int startOffset;
                int endOffset;
                if (!TryReadThisTileReference(SafeGetData(source, "startTile"), out startOffset)
                    || !TryReadThisTileReference(SafeGetData(source, "endTile"), out endOffset))
                {
                    result.SkippedMoveTrackEvents++;
                    continue;
                }

                int startFloor = source.floor + startOffset;
                int endFloor = source.floor + endOffset;
                if (endFloor < startFloor)
                {
                    int swap = startFloor;
                    startFloor = endFloor;
                    endFloor = swap;
                }

                List<string> targetTags = ResolvePreviewTags(slot, trackIndex, startFloor, endFloor);
                if (targetTags.Count == 0)
                {
                    result.SkippedMoveTrackEvents++;
                    continue;
                }

                List<TrackSegment> occurrences = FindSourceFloorOccurrences(analyzed, source.floor);
                if (occurrences.Count == 0)
                {
                    result.SkippedMoveTrackEvents++;
                    continue;
                }

                double sourceAngleOffset = ReadDoubleData(source, "angleOffset", 0.0);
                double sourceDuration = ReadDoubleData(source, "duration", 0.0);
                object positionOffset = SafeGetData(source, "positionOffset");
                object ease = SafeGetData(source, "ease");
                string originalEventTag = Convert.ToString(SafeGetData(source, "eventTag"), CultureInfo.InvariantCulture) ?? string.Empty;

                for (int occurrenceIndex = 0; occurrenceIndex < occurrences.Count; occurrenceIndex++)
                {
                    TrackSegment segment = occurrences[occurrenceIndex];
                    if (!(segment.EffectiveBpm > 0.0)) continue;

                    double offsetMasterBeats = (sourceAngleOffset / 180.0) * plan.MasterBpm / segment.EffectiveBpm;
                    double eventBeat = segment.StartBeat + offsetMasterBeats;
                    int anchorIndex = FindAnchorAtOrBefore(plan, eventBeat);
                    if (anchorIndex < 0) continue;
                    int outputFloor = plan.RegionStartFloor + anchorIndex;
                    double outputAngleOffset = Math.Max(0.0, eventBeat - plan.Anchors[anchorIndex].Beat) * 180.0;
                    double outputDuration = sourceDuration * plan.MasterBpm / segment.EffectiveBpm;

                    for (int targetIndex = 0; targetIndex < targetTags.Count; targetIndex++)
                    {
                        LevelEvent translated = CreateEvent("MoveDecorations", outputFloor);
                        SetRequiredData(translated, "duration", outputDuration);
                        SetRequiredData(translated, "tag", targetTags[targetIndex]);
                        if (positionOffset != null) SetRequiredData(translated, "positionOffset", positionOffset);
                        SetOptionalData(translated, "angleOffset", outputAngleOffset);
                        if (ease != null) SetOptionalData(translated, "ease", ease);
                        SetOptionalData(translated, "eventTag", AddOwnerEventTag(originalEventTag));
                        outputEvents.Add(translated);
                        result.EmittedMoveDecorations++;
                    }
                }
            }
        }

        private static List<string> ResolvePreviewTags(TrackSlot slot, int trackIndex, int startFloor, int endFloor)
        {
            var result = new List<string>();
            int previewStartFloor = slot.RegionStartFloor > 0 ? slot.RegionStartFloor : 1;
            int sourceTransitionCount = slot.Data != null && slot.Data.angleData != null ? slot.Data.angleData.Count : 0;
            int previewCount = Math.Max(0, sourceTransitionCount - previewStartFloor);

            for (int floor = startFloor; floor <= endFloor; floor++)
            {
                int tileIndex = floor - previewStartFloor;
                if (tileIndex < 0 || tileIndex >= previewCount) continue;
                result.Add("T" + trackIndex.ToString(CultureInfo.InvariantCulture) + "_" + tileIndex.ToString(CultureInfo.InvariantCulture));
            }
            return result;
        }

        private static List<TrackSegment> FindSourceFloorOccurrences(AnalyzedTrack track, int sourceFloor)
        {
            var result = new List<TrackSegment>();
            for (int i = 0; i < track.Segments.Count; i++)
                if (track.Segments[i].SourceFloor == sourceFloor) result.Add(track.Segments[i]);
            return result;
        }

        private static int FindAnchorAtOrBefore(GenerationPlan plan, double beat)
        {
            int best = -1;
            for (int i = 0; i < plan.Anchors.Count; i++)
            {
                if (plan.Anchors[i].Beat > beat + TimelineMerger.BeatEpsilon) break;
                best = i;
            }
            return best;
        }

        private static bool TryReadThisTileReference(object value, out int offset)
        {
            offset = 0;
            if (value == null) return false;

            try
            {
                Type type = value.GetType();
                const R.BindingFlags flags = R.BindingFlags.Instance | R.BindingFlags.Public | R.BindingFlags.NonPublic;
                R.PropertyInfo first = type.GetProperty("Item1", flags);
                R.PropertyInfo second = type.GetProperty("Item2", flags);
                if (first != null && second != null)
                {
                    offset = Convert.ToInt32(first.GetValue(value, null), CultureInfo.InvariantCulture);
                    string relative = Convert.ToString(second.GetValue(value, null), CultureInfo.InvariantCulture) ?? string.Empty;
                    return string.Equals(relative, "ThisTile", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch { }

            string text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            if (text.IndexOf("ThisTile", StringComparison.OrdinalIgnoreCase) < 0) return false;
            int comma = text.IndexOf(',');
            int left = text.IndexOf('(');
            if (comma > left)
            {
                string number = text.Substring(left >= 0 ? left + 1 : 0, comma - (left >= 0 ? left + 1 : 0)).Trim();
                return int.TryParse(number, NumberStyles.Integer, CultureInfo.InvariantCulture, out offset);
            }
            return false;
        }

        private static int RemovePreviouslyGenerated(IList events)
        {
            int removed = 0;
            for (int i = events.Count - 1; i >= 0; i--)
            {
                LevelEvent ev = events[i] as LevelEvent;
                if (ev == null || !IsEventNamed(ev, "MoveDecorations")) continue;
                string eventTag = Convert.ToString(SafeGetData(ev, "eventTag"), CultureInfo.InvariantCulture) ?? string.Empty;
                if (!HasTag(eventTag, OwnerEventTag)) continue;
                events.RemoveAt(i);
                removed++;
            }
            return removed;
        }

        private static string AddOwnerEventTag(string original)
        {
            original = (original ?? string.Empty).Trim();
            if (HasTag(original, OwnerEventTag)) return original;
            return original.Length == 0 ? OwnerEventTag : original + " " + OwnerEventTag;
        }

        private static bool HasTag(string tags, string requested)
        {
            string[] split = (tags ?? string.Empty).Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < split.Length; i++)
                if (string.Equals(split[i], requested, StringComparison.Ordinal)) return true;
            return false;
        }

        private static int CountRuntimeEventCatalog()
        {
            try
            {
                if (GCS.levelEventsInfo == null) return 0;
                int count = 0;
                foreach (KeyValuePair<string, LevelEventInfo> pair in GCS.levelEventsInfo)
                    if (pair.Value != null && pair.Value.isActive) count++;
                return count;
            }
            catch { return 0; }
        }

        private static LevelEvent CreateEvent(string name, int floor)
        {
            LevelEventInfo info = ResolveEventInfo(name);
            return new LevelEvent(floor, info.type, info);
        }

        private static LevelEventInfo ResolveEventInfo(string requestedName)
        {
            if (GCS.levelEventsInfo == null)
                throw new InvalidOperationException("ADOFAI level-event metadata is not initialized.");
            LevelEventInfo direct;
            if (GCS.levelEventsInfo.TryGetValue(requestedName, out direct) && direct != null) return direct;
            foreach (KeyValuePair<string, LevelEventInfo> pair in GCS.levelEventsInfo)
            {
                if (pair.Value == null) continue;
                if (string.Equals(pair.Key, requestedName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(pair.Value.name, requestedName, StringComparison.OrdinalIgnoreCase)) return pair.Value;
            }
            throw new InvalidOperationException("ADOFAI event metadata is unavailable for '" + requestedName + "'.");
        }

        private static bool IsEventNamed(LevelEvent ev, string requestedName)
        {
            if (ev == null) return false;
            string infoName = ev.info != null ? ev.info.name : string.Empty;
            if (string.Equals(infoName, requestedName, StringComparison.OrdinalIgnoreCase)) return true;
            return string.Equals(ev.eventType.ToString(), requestedName, StringComparison.OrdinalIgnoreCase);
        }

        private static object SafeGetData(LevelEvent ev, string key)
        {
            if (ev == null) return null;
            try { return ev[key]; } catch { return null; }
        }

        private static int ReadIntData(LevelEvent ev, string key, int fallback)
        {
            object value = SafeGetData(ev, key);
            try { return value == null ? fallback : Convert.ToInt32(value, CultureInfo.InvariantCulture); }
            catch { return fallback; }
        }

        private static double ReadDoubleData(LevelEvent ev, string key, double fallback)
        {
            object value = SafeGetData(ev, key);
            try { return value == null ? fallback : Convert.ToDouble(value, CultureInfo.InvariantCulture); }
            catch { return fallback; }
        }

        private static void SetRequiredData(LevelEvent ev, string key, object value)
        {
            if (ev == null || ev.info == null || ev.info.propertiesInfo == null || !ev.info.propertiesInfo.ContainsKey(key))
                throw new InvalidOperationException((ev != null && ev.info != null ? ev.info.name : "Event") + " metadata has no property '" + key + "'.");
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
            return Convert.ChangeType(value, actual, CultureInfo.InvariantCulture);
        }
    }
}
