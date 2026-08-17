using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using ADOFAI;
using R = System.Reflection;

namespace KineticNapier.ADOFAIMultiTileEditor
{
    internal static class MasterOutputGenerator
    {
        internal static OrbitCommitResult GenerateAndCommit(
            scnEditor editor,
            GenerationPlan plan,
            MasterPathPreview preview,
            IList<TrackSlot> tracks,
            int baseTrackIndex)
        {
            if (editor == null || editor.levelData == null)
                throw new InvalidOperationException("Editor is not ready.");
            if (plan == null || !(plan.MasterBpm > 0.0))
                throw new InvalidOperationException("Analyze a valid master timing plan first.");
            if (preview == null)
                throw new InvalidOperationException("Verify the master path before generating.");

            int chordHelpers = ChordPlanExpander.Expand(plan, preview);

            LevelData original = editor.levelData.Copy();
            int selectedFloor = GameAngleProbe.TryGetSelectedFloorIndex(editor);
            bool success = false;

            try
            {
                LevelData normalized = original.Copy();
                IList events = normalized.levelEvents as IList;
                if (events == null)
                    throw new InvalidOperationException("LevelData.levelEvents is not list-compatible in this game build.");

                int removedSetSpeed = RemoveSetSpeedActions(events, plan.RegionStartFloor);

                if (plan.RegionStartFloor == 0)
                {
                    double baseBpm = Convert.ToDouble(normalized.bpm, CultureInfo.InvariantCulture);
                    if (Math.Abs(baseBpm - plan.MasterBpm) > 1.0e-4)
                    {
                        throw new InvalidOperationException(
                            "The Multi Tile region starts at F0, where ADOFAI cannot host the generated timing normalization. "
                            + "Choose a later region start, or use source timings whose normalized master BPM equals the level base BPM.");
                    }
                }
                else
                {
                    events.Add(CreateSetSpeedEvent(plan.MasterBpm, plan.RegionStartFloor));
                }

                TrackStore.RestoreSnapshot(editor, normalized, true);
                if (plan.RegionStartFloor > 0)
                    VerifyMasterSetSpeed(editor.levelData.levelEvents as IList, plan.MasterBpm, plan.RegionStartFloor);

                OrbitCommitResult result = PACL2AutoGenerator.GenerateAndCommit(
                    editor, plan, preview, tracks, baseTrackIndex);

                result.Diagnostic += " Master timing uses constant "
                    + plan.MasterBpm.ToString("0.######", CultureInfo.InvariantCulture)
                    + " BPM from F" + plan.RegionStartFloor + "; replaced " + removedSetSpeed
                    + " source/base SetSpeed action(s) inside the region while preserving earlier timing events."
                    + (chordHelpers > 0
                        ? " Added " + chordHelpers + " 0.1° helper floor(s) to represent simultaneous source hits."
                        : "");

                success = true;
                return result;
            }
            finally
            {
                if (!success)
                {
                    TrackStore.RestoreSnapshot(editor, original, true);
                    if (selectedFloor >= 0 && selectedFloor < editor.floors.Count)
                        editor.SelectFloor(editor.floors[selectedFloor], true);
                }
            }
        }

        private static LevelEvent CreateSetSpeedEvent(double bpm, int floor)
        {
            LevelEventInfo info = ResolveEventInfo("SetSpeed");
            LevelEvent ev = new LevelEvent(floor, info.type, info);
            SetRequiredData(ev, "speedType", "Bpm");
            SetRequiredData(ev, "beatsPerMinute", bpm);
            SetOptionalData(ev, "bpmMultiplier", 1.0);
            SetOptionalData(ev, "angleOffset", 0.0);
            SetOptionalData(ev, "eventTag", "");
            SetOptionalData(ev, "active", true);
            return ev;
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
            throw new InvalidOperationException("Stock SetSpeed event metadata is unavailable in this ADOFAI build.");
        }

        private static void SetRequiredData(LevelEvent ev, string key, object value)
        {
            if (ev == null || ev.info == null || ev.info.propertiesInfo == null || !ev.info.propertiesInfo.ContainsKey(key))
                throw new InvalidOperationException("SetSpeed metadata has no property '" + key + "'.");
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

        private static object SafeGetData(LevelEvent ev, string key)
        {
            if (ev == null) return null;
            try { return ev[key]; } catch { return null; }
        }

        private static void VerifyMasterSetSpeed(IList events, double expectedBpm, int expectedFloor)
        {
            if (events == null)
                throw new InvalidOperationException("Stock restore lost the level event list while applying master timing.");
            for (int i = 0; i < events.Count; i++)
            {
                LevelEvent ev = events[i] as LevelEvent;
                if (ev == null || !IsSetSpeed(ev)) continue;
                int floor;
                if (!TryReadFloor(ev, out floor) || floor != expectedFloor) continue;
                string speedType = Convert.ToString(SafeGetData(ev, "speedType"), CultureInfo.InvariantCulture) ?? "";
                double bpm;
                try { bpm = Convert.ToDouble(SafeGetData(ev, "beatsPerMinute"), CultureInfo.InvariantCulture); }
                catch { continue; }
                if (string.Equals(speedType, "Bpm", StringComparison.OrdinalIgnoreCase)
                    && Math.Abs(bpm - expectedBpm) <= 1.0e-4) return;
            }
            throw new InvalidOperationException(
                "The generated F" + expectedFloor + " SetSpeed did not survive stock reconstruction at "
                + expectedBpm.ToString("0.######", CultureInfo.InvariantCulture) + " BPM.");
        }

        private static int RemoveSetSpeedActions(IList events, int regionStartFloor)
        {
            if (events == null) return 0;
            int removed = 0;
            for (int i = events.Count - 1; i >= 0; i--)
            {
                LevelEvent ev = events[i] as LevelEvent;
                if (ev == null || !IsSetSpeed(ev)) continue;
                int floor;
                if (!TryReadFloor(ev, out floor) || floor < regionStartFloor) continue;
                events.RemoveAt(i);
                removed++;
            }
            return removed;
        }

        private static bool TryReadFloor(LevelEvent ev, out int floor)
        {
            floor = -1;
            if (ev == null) return false;
            Type type = ev.GetType();
            const R.BindingFlags flags = R.BindingFlags.Instance | R.BindingFlags.Public | R.BindingFlags.NonPublic;
            try
            {
                R.PropertyInfo p = type.GetProperty("floor", flags) ?? type.GetProperty("floorIndex", flags);
                if (p != null && p.GetIndexParameters().Length == 0)
                {
                    floor = Convert.ToInt32(p.GetValue(ev, null), CultureInfo.InvariantCulture);
                    return true;
                }
            }
            catch { }
            try
            {
                R.FieldInfo f = type.GetField("floor", flags) ?? type.GetField("floorIndex", flags);
                if (f != null)
                {
                    floor = Convert.ToInt32(f.GetValue(ev), CultureInfo.InvariantCulture);
                    return true;
                }
            }
            catch { }
            return false;
        }

        private static bool IsSetSpeed(LevelEvent ev)
        {
            string name = ev != null && ev.info != null ? (ev.info.name ?? string.Empty) : string.Empty;
            if (string.Equals(name, "SetSpeed", StringComparison.OrdinalIgnoreCase)) return true;
            string eventType = ev == null ? string.Empty : Convert.ToString(ev.eventType, CultureInfo.InvariantCulture) ?? string.Empty;
            return string.Equals(eventType, "SetSpeed", StringComparison.OrdinalIgnoreCase);
        }
    }

    internal static class ChordPlanExpander
    {
        private const double HelperDegrees = 0.1;
        private const double HelperBeat = HelperDegrees / 180.0;
        private const double MaxSingleTravelDegrees = 360.0;

        private sealed class BoundaryGroup
        {
            internal double Beat;
            internal readonly List<int> Tracks = new List<int>();
        }

        private sealed class BoundaryOccurrence
        {
            internal double Beat;
            internal int Track;
        }

        internal static int Expand(GenerationPlan plan, MasterPathPreview preview)
        {
            if (plan == null || preview == null || plan.Tracks.Count < 2) return 0;

            List<BoundaryGroup> groups = BuildBoundaryGroups(plan);
            int helpers = 0;
            for (int i = 0; i < groups.Count; i++)
                if (groups[i].Tracks.Count > 1) helpers += groups[i].Tracks.Count - 1;
            if (helpers == 0) return 0;

            ValidateRoom(groups);

            for (int t = 0; t < plan.Tracks.Count; t++)
            {
                AnalyzedTrack track = plan.Tracks[t];
                for (int s = 0; s < track.Segments.Count; s++)
                {
                    TrackSegment segment = track.Segments[s];
                    double newStart = ShiftedBeat(groups, segment.StartBeat, segment.TrackIndex);
                    double newEnd = ShiftedBeat(groups, segment.EndBeat, segment.TrackIndex);
                    if (!(newEnd > newStart + TimelineMerger.BeatEpsilon))
                    {
                        throw new InvalidOperationException(
                            "0.1° simultaneous-hit expansion would collapse a segment on track '"
                            + track.Name + "' near source floor " + segment.SourceFloor + ".");
                    }
                    segment.StartBeat = newStart;
                    segment.EndBeat = newEnd;
                    segment.DurationBeats = newEnd - newStart;
                }

                track.SourceFloors.Clear();
                for (int s = 0; s < track.Segments.Count; s++)
                {
                    TrackSegment segment = track.Segments[s];
                    track.SourceFloors.Add(new SourceFloorPoint
                    {
                        Floor = segment.SourceFloor,
                        Beat = segment.StartBeat
                    });
                }
                TrackSegment last = track.Segments[track.Segments.Count - 1];
                track.SourceFloors.Add(new SourceFloorPoint
                {
                    Floor = last.SourceFloor + 1,
                    Beat = last.EndBeat
                });
                track.StartBeat = track.Segments[0].StartBeat;
                track.EndBeat = last.EndBeat;
            }

            RebuildAnchors(plan);
            RebuildPreview(plan, preview);
            plan.StartBeat = plan.Anchors[0].Beat;
            plan.EndBeat = plan.Anchors[plan.Anchors.Count - 1].Beat;
            plan.Diagnostic = (plan.Diagnostic ?? string.Empty).TrimEnd('.')
                + "; expanded simultaneous boundaries with " + helpers + " helper floor(s) at 0.1° spacing.";
            return helpers;
        }

        private static List<BoundaryGroup> BuildBoundaryGroups(GenerationPlan plan)
        {
            var occurrences = new List<BoundaryOccurrence>();
            for (int t = 0; t < plan.Tracks.Count; t++)
            {
                AnalyzedTrack track = plan.Tracks[t];
                for (int s = 0; s < track.Segments.Count; s++)
                {
                    TrackSegment segment = track.Segments[s];
                    occurrences.Add(new BoundaryOccurrence { Beat = segment.StartBeat, Track = segment.TrackIndex });
                    occurrences.Add(new BoundaryOccurrence { Beat = segment.EndBeat, Track = segment.TrackIndex });
                }
            }
            occurrences.Sort(delegate(BoundaryOccurrence a, BoundaryOccurrence b)
            {
                int beat = a.Beat.CompareTo(b.Beat);
                return beat != 0 ? beat : a.Track.CompareTo(b.Track);
            });

            var groups = new List<BoundaryGroup>();
            for (int i = 0; i < occurrences.Count; i++)
            {
                BoundaryOccurrence occurrence = occurrences[i];
                BoundaryGroup group = groups.Count > 0
                    && TimelineMerger.NearlyEqual(groups[groups.Count - 1].Beat, occurrence.Beat)
                    ? groups[groups.Count - 1]
                    : null;
                if (group == null)
                {
                    group = new BoundaryGroup { Beat = occurrence.Beat };
                    groups.Add(group);
                }
                if (!group.Tracks.Contains(occurrence.Track)) group.Tracks.Add(occurrence.Track);
            }
            for (int i = 0; i < groups.Count; i++) groups[i].Tracks.Sort();
            return groups;
        }

        private static void ValidateRoom(IList<BoundaryGroup> groups)
        {
            for (int i = 0; i + 1 < groups.Count; i++)
            {
                double span = Math.Max(0, groups[i].Tracks.Count - 1) * HelperBeat;
                double gap = groups[i + 1].Beat - groups[i].Beat;
                if (span + TimelineMerger.BeatEpsilon >= gap)
                {
                    throw new InvalidOperationException(
                        "Two source hit groups are too close to insert the requested 0.1° simultaneous helper tile(s) without changing their order.");
                }
            }
        }

        private static double ShiftedBeat(IList<BoundaryGroup> groups, double beat, int trackIndex)
        {
            for (int i = 0; i < groups.Count; i++)
            {
                BoundaryGroup group = groups[i];
                if (!TimelineMerger.NearlyEqual(group.Beat, beat)) continue;
                int order = group.Tracks.IndexOf(trackIndex);
                return order < 0 ? beat : group.Beat + order * HelperBeat;
            }
            return beat;
        }

        private static void RebuildAnchors(GenerationPlan plan)
        {
            var times = new List<double>();
            for (int t = 0; t < plan.Tracks.Count; t++)
            {
                AnalyzedTrack track = plan.Tracks[t];
                for (int s = 0; s < track.Segments.Count; s++)
                {
                    times.Add(track.Segments[s].StartBeat);
                    times.Add(track.Segments[s].EndBeat);
                }
            }
            times.Sort();

            plan.Anchors.Clear();
            for (int i = 0; i < times.Count; i++)
            {
                double beat = times[i];
                if (plan.Anchors.Count == 0
                    || !TimelineMerger.NearlyEqual(plan.Anchors[plan.Anchors.Count - 1].Beat, beat))
                    plan.Anchors.Add(new MasterAnchor { Beat = beat });
            }

            for (int t = 0; t < plan.Tracks.Count; t++)
            {
                AnalyzedTrack track = plan.Tracks[t];
                for (int s = 0; s < track.Segments.Count; s++)
                {
                    TrackSegment segment = track.Segments[s];
                    int anchor = TimelineMerger.FindAnchorIndex(plan.Anchors, segment.StartBeat);
                    if (anchor < 0)
                        throw new InvalidOperationException("Expanded simultaneous hit could not be mapped back to the master path.");
                    segment.MasterAnchorIndex = anchor;
                    plan.Anchors[anchor].StartingSegments.Add(segment);
                }
            }
        }

        private static void RebuildPreview(GenerationPlan plan, MasterPathPreview preview)
        {
            preview.AngleData.Clear();
            preview.RuntimeAnchorBeats.Clear();
            preview.RuntimeFloorCount = 0;
            preview.MaxAngleErrorDegrees = 0.0;
            preview.MaxBeatError = 0.0;

            double previousHeading = plan.RegionStartHeading;
            bool inheritedIsCCW = plan.RegionInheritedIsCCW;
            for (int i = 0; i + 1 < plan.Anchors.Count; i++)
            {
                double deltaBeat = plan.Anchors[i + 1].Beat - plan.Anchors[i].Beat;
                if (!(deltaBeat > TimelineMerger.BeatEpsilon))
                    throw new InvalidOperationException("Expanded master timeline contains a zero/negative interval near M" + i + ".");

                double travelDegrees = deltaBeat * 180.0;
                if (travelDegrees > MaxSingleTravelDegrees + 0.02)
                    throw new InvalidOperationException("Expanded master interval exceeds 360° near M" + i + ".");

                double heading = inheritedIsCCW
                    ? NormalizeDegrees(previousHeading - 180.0 + travelDegrees)
                    : NormalizeDegrees(previousHeading + 180.0 - travelDegrees);
                if (heading < 0.0001 || heading > 359.9999) heading = 0.0;
                preview.AngleData.Add((float)heading);
                previousHeading = heading;
            }
        }

        private static double NormalizeDegrees(double value)
        {
            value %= 360.0;
            if (value < 0.0) value += 360.0;
            if (Math.Abs(value - 360.0) < 1.0e-9) value = 0.0;
            return value;
        }
    }
}