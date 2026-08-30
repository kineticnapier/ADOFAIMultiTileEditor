using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;

namespace KineticNapier.ADOFAIMultiTileEditor
{
    internal static class TrackAnalyzer
    {
        private const double AngleConsistencyAbsoluteDegrees = 0.02;
        private const double AngleConsistencyRelative = 1.0e-4;
        private const double TimeConsistencyAbsoluteSeconds = 0.002;
        private const double TimeConsistencyRelative = 2.0e-4;
        private const double MinBpm = 1.0e-5;
        private const double PrefixAngleTolerance = 0.001;

        internal static GenerationPlan BuildPlan(scnEditor editor, IList<TrackSlot> tracks)
        {
            if (editor == null || editor.levelData == null)
                throw new InvalidOperationException("Editor is not ready.");
            if (tracks == null || tracks.Count == 0)
                throw new InvalidOperationException("Store at least one source track first.");

            ValidateTags(tracks);
            ValidateCommonRegionStartAndPrefix(tracks);

            ADOFAI.LevelData original = editor.levelData.Copy();
            int selectedFloor = GameAngleProbe.TryGetSelectedFloorIndex(editor);
            var analyzed = new List<AnalyzedTrack>();

            try
            {
                for (int i = 0; i < tracks.Count; i++)
                {
                    TrackSlot slot = tracks[i];
                    if (slot.Data == null) throw new InvalidOperationException("Track '" + slot.Name + "' has no snapshot.");
                    TrackStore.RestoreSnapshot(editor, slot.Data, false);
                    analyzed.Add(AnalyzeCurrentRuntime(editor, slot, i));
                }
            }
            finally
            {
                TrackStore.RestoreSnapshot(editor, original, true);
                if (selectedFloor >= 0 && selectedFloor < editor.floors.Count)
                    editor.SelectFloor(editor.floors[selectedFloor], true);
            }

            ValidateCommonRegionRuntime(analyzed);
            double masterBpm = NormalizeToMasterTimeline(analyzed);
            GenerationPlan plan = TimelineMerger.Merge(analyzed);
            plan.MasterBpm = masterBpm;
            plan.RegionStartFloor = analyzed[0].RegionStartFloor;
            plan.RegionStartHeading = analyzed[0].RegionStartHeading;
            plan.RegionInheritedIsCCW = analyzed[0].RegionInheritedIsCCW;
            plan.StartSeconds = analyzed[0].StartSeconds;
            plan.EndSeconds = analyzed[0].EndSeconds;
            plan.Diagnostic = plan.Diagnostic.TrimEnd('.')
                + "; region starts at F" + plan.RegionStartFloor
                + "; constant master BPM " + masterBpm.ToString("0.######", CultureInfo.InvariantCulture)
                + " (source SetSpeed maps baked into region-relative real time; Pause is retained as stationary delay before each orbit, including terminal-floor wait time).";
            return plan;
        }

        private static AnalyzedTrack AnalyzeCurrentRuntime(scnEditor editor, TrackSlot slot, int trackIndex)
        {
            if (editor.floors == null || editor.floors.Count < 2)
                throw new InvalidOperationException("Track '" + slot.Name + "' has fewer than two runtime floors.");
            if (slot.RegionStartFloor < 0 || slot.RegionStartFloor >= editor.floors.Count)
                throw new InvalidOperationException("Track '" + slot.Name + "' region start is outside the reconstructed path.");

            object requestedStart = editor.floors[slot.RegionStartFloor];
            if (requestedStart == null)
                throw new InvalidOperationException("Track '" + slot.Name + "' region start floor is unavailable.");
            if (ReadBool(requestedStart, "midSpin", false))
                throw new InvalidOperationException("Track '" + slot.Name + "' region start is a midspin floor; choose a landable floor.");

            var floors = new List<object>();
            int regionListIndex = -1;
            for (int i = 0; i < editor.floors.Count; i++)
            {
                object floor = editor.floors[i];
                if (floor == null || ReadBool(floor, "midSpin", false)) continue;
                if (ReferenceEquals(floor, requestedStart)) regionListIndex = floors.Count;
                floors.Add(floor);
            }

            if (regionListIndex < 0)
                throw new InvalidOperationException("Track '" + slot.Name + "' region start could not be mapped after midspin filtering.");
            if (regionListIndex + 1 >= floors.Count)
                throw new InvalidOperationException("Track '" + slot.Name + "' has no segment after its region start.");

            double baseBpm = Convert.ToDouble(editor.levelData.bpm, CultureInfo.InvariantCulture);
            if (!(baseBpm > MinBpm) || double.IsNaN(baseBpm) || double.IsInfinity(baseBpm))
                throw new InvalidOperationException("Track '" + slot.Name + "' has an invalid base BPM.");

            int actualStartFloor = ReadFloorId(requestedStart, slot.RegionStartFloor);
            double startHeading = ReadRegionStartHeading(slot, slot.RegionStartFloor);
            bool inheritedIsCCW = regionListIndex > 0 && ReadBool(floors[regionListIndex - 1], "isCCW", false);

            var result = new AnalyzedTrack
            {
                TrackIndex = trackIndex,
                Name = slot.Name ?? ("Track " + (trackIndex + 1)),
                PlanetATag = slot.PlanetATag.Trim(),
                PlanetBTag = slot.PlanetBTag.Trim(),
                InitialPivotIsA = slot.PivotIsA,
                BaseBpm = baseBpm,
                RegionStartFloor = actualStartFloor,
                RegionStartHeading = startHeading,
                RegionInheritedIsCCW = inheritedIsCCW,
                StartBeat = 0.0,
                EndBeat = 0.0,
                StartSeconds = 0.0,
                EndSeconds = 0.0
            };

            ValidateUnsupportedFloorState(floors, regionListIndex, slot.Name);

            bool pivotIsA = slot.PivotIsA;
            double cursorSeconds = 0.0;
            for (int i = regionListIndex; i + 1 < floors.Count; i++)
            {
                object startFloor = floors[i];
                object endFloor = floors[i + 1];

                AngleCandidate chosen = ReadAngleCandidate(startFloor, i);
                if (!chosen.Valid || chosen.MagnitudeDegrees <= TimelineMerger.BeatEpsilon * 180.0)
                {
                    AngleCandidate fallback = ReadAngleCandidate(endFloor, i + 1);
                    if (fallback.Valid && fallback.MagnitudeDegrees > TimelineMerger.BeatEpsilon * 180.0)
                        chosen = fallback;
                }
                if (!chosen.Valid || chosen.MagnitudeDegrees <= TimelineMerger.BeatEpsilon * 180.0)
                    throw new InvalidOperationException("Track '" + slot.Name + "' has no positive readable angleLength near runtime floor " + ReadFloorId(startFloor, i) + ".");

                double sourceDurationBeats = chosen.MagnitudeDegrees / 180.0;
                double speed = ReadDouble(startFloor, "speed", 1.0);
                double effectiveBpm = baseBpm * speed;
                if (!(effectiveBpm > MinBpm) || double.IsNaN(effectiveBpm) || double.IsInfinity(effectiveBpm))
                    throw new InvalidOperationException("Track '" + slot.Name + "' has an invalid effective BPM near source floor " + chosen.FloorId + ".");

                double motionDurationSeconds = sourceDurationBeats * 60.0 / effectiveBpm;
                double pauseSourceBeats = Math.Max(0.0, ReadDouble(startFloor, "extraBeats", 0.0));
                double pauseSeconds = pauseSourceBeats * 60.0 / effectiveBpm;
                string source = chosen.Source + " + BPM*speed timing";
                if (pauseSourceBeats > TimelineMerger.BeatEpsilon)
                    source += " + Pause/extraBeats=" + pauseSourceBeats.ToString("0.######", CultureInfo.InvariantCulture);

                double runtimeStartTime;
                double runtimeEndTime;
                if (TryReadDouble(startFloor, "entryTime", out runtimeStartTime)
                    && TryReadDouble(endFloor, "entryTime", out runtimeEndTime)
                    && runtimeEndTime - runtimeStartTime > 1.0e-9)
                {
                    double runtimeSeconds = runtimeEndTime - runtimeStartTime;
                    double expectedSeconds = motionDurationSeconds + pauseSeconds;
                    double tolerance = Math.Max(TimeConsistencyAbsoluteSeconds, Math.Max(runtimeSeconds, expectedSeconds) * TimeConsistencyRelative);

                    if (pauseSourceBeats > TimelineMerger.BeatEpsilon)
                    {
                        if (Math.Abs(runtimeSeconds - expectedSeconds) <= tolerance)
                        {
                            source += " + entryTime pause check";
                        }
                        else if (runtimeSeconds + tolerance >= motionDurationSeconds)
                        {
                            // Keep the actual orbital speed. Any additional runtime time is
                            // stationary time at the landed tile, rather than slowing the orbit.
                            pauseSeconds = Math.Max(0.0, runtimeSeconds - motionDurationSeconds);
                            source += " + entryTime stationary-delay override";
                        }
                        else
                        {
                            // An inconsistent runtime shorter than the physical motion cannot
                            // contain the requested pause. Fall back to the historical timing
                            // normalization rather than creating negative wait time.
                            pauseSeconds = 0.0;
                            motionDurationSeconds = runtimeSeconds;
                            effectiveBpm = sourceDurationBeats * 60.0 / motionDurationSeconds;
                            source += " + entryTime motion override";
                        }
                    }
                    else if (Math.Abs(runtimeSeconds - motionDurationSeconds) > tolerance)
                    {
                        motionDurationSeconds = runtimeSeconds;
                        effectiveBpm = sourceDurationBeats * 60.0 / motionDurationSeconds;
                        source += " + entryTime override";
                    }
                    else source += " + entryTime check";
                }
                else source += " + terminal-safe fallback";

                double runtimeStartBeat;
                double runtimeEndBeat;
                if (TryReadDouble(startFloor, "entryBeat", out runtimeStartBeat)
                    && TryReadDouble(endFloor, "entryBeat", out runtimeEndBeat)
                    && runtimeEndBeat - runtimeStartBeat > TimelineMerger.BeatEpsilon)
                {
                    double runtimeMagnitude = (runtimeEndBeat - runtimeStartBeat) * 180.0;
                    double expectedMagnitude = chosen.MagnitudeDegrees + pauseSourceBeats * 180.0;
                    double tolerance = Math.Max(AngleConsistencyAbsoluteDegrees, expectedMagnitude * AngleConsistencyRelative);
                    if (Math.Abs(runtimeMagnitude - expectedMagnitude) > tolerance && pauseSourceBeats <= TimelineMerger.BeatEpsilon)
                    {
                        throw new InvalidOperationException(
                            "Track '" + slot.Name + "' reconstructed timing/angle mismatch near source floor " + chosen.FloorId
                            + ": entryBeat interval implies " + runtimeMagnitude.ToString("0.###", CultureInfo.InvariantCulture)
                            + "°, but angleLength gives " + chosen.MagnitudeDegrees.ToString("0.###", CultureInfo.InvariantCulture) + "°."
                            + " Unsupported timing geometry is rejected instead of guessed.");
                    }
                }

                double durationSeconds = pauseSeconds + motionDurationSeconds;
                double startSeconds = cursorSeconds;
                double endSeconds = startSeconds + durationSeconds;
                string moving = pivotIsA ? result.PlanetBTag : result.PlanetATag;
                string center = pivotIsA ? result.PlanetATag : result.PlanetBTag;
                double amount = chosen.IsCCW ? chosen.MagnitudeDegrees : -chosen.MagnitudeDegrees;

                result.Segments.Add(new TrackSegment
                {
                    TrackIndex = trackIndex,
                    TrackName = result.Name,
                    SourceFloor = chosen.FloorId,
                    StartSeconds = startSeconds,
                    EndSeconds = endSeconds,
                    DurationSeconds = durationSeconds,
                    PauseSeconds = pauseSeconds,
                    MotionDurationSeconds = motionDurationSeconds,
                    SourceDurationBeats = sourceDurationBeats,
                    EffectiveBpm = effectiveBpm,
                    AmountDegrees = amount,
                    AngleSource = source,
                    MovingTag = moving,
                    CenterTag = center
                });

                cursorSeconds = endSeconds;
                pivotIsA = !pivotIsA;
            }

            // Pause belongs to the landed floor. Every non-terminal floor is handled
            // above as the stationary prefix of its following segment. The final
            // landable floor has no following segment, so older builds never read its
            // extraBeats at all. Preserve it as a terminal stationary interval.
            object terminalFloor = floors[floors.Count - 1];
            double terminalPauseSourceBeats = Math.Max(0.0, ReadDouble(terminalFloor, "extraBeats", 0.0));
            result.TerminalFloor = ReadFloorId(terminalFloor, floors.Count - 1);
            if (terminalPauseSourceBeats > TimelineMerger.BeatEpsilon)
            {
                double terminalSpeed = ReadDouble(terminalFloor, "speed", 1.0);
                double terminalBpm = baseBpm * terminalSpeed;
                if (!(terminalBpm > MinBpm) || double.IsNaN(terminalBpm) || double.IsInfinity(terminalBpm))
                    throw new InvalidOperationException("Track '" + slot.Name + "' has an invalid effective BPM on terminal floor " + result.TerminalFloor + ".");
                result.TerminalPauseSeconds = terminalPauseSourceBeats * 60.0 / terminalBpm;
            }

            result.EndSeconds = cursorSeconds + result.TerminalPauseSeconds;
            return result;
        }

        private static double NormalizeToMasterTimeline(IList<AnalyzedTrack> tracks)
        {
            double masterBpm = double.PositiveInfinity;
            for (int t = 0; t < tracks.Count; t++)
            {
                AnalyzedTrack track = tracks[t];
                for (int s = 0; s < track.Segments.Count; s++)
                {
                    double bpm = track.Segments[s].EffectiveBpm;
                    if (bpm > MinBpm && !double.IsNaN(bpm) && !double.IsInfinity(bpm) && bpm < masterBpm)
                        masterBpm = bpm;
                }
            }
            if (double.IsPositiveInfinity(masterBpm))
                throw new InvalidOperationException("Could not choose a valid master BPM from the source tracks.");

            for (int t = 0; t < tracks.Count; t++)
            {
                AnalyzedTrack track = tracks[t];
                track.StartBeat = track.StartSeconds * masterBpm / 60.0;
                track.EndBeat = track.EndSeconds * masterBpm / 60.0;
                track.TerminalPauseBeats = track.TerminalPauseSeconds * masterBpm / 60.0;
                for (int s = 0; s < track.Segments.Count; s++)
                {
                    TrackSegment segment = track.Segments[s];
                    segment.StartBeat = segment.StartSeconds * masterBpm / 60.0;
                    segment.EndBeat = segment.EndSeconds * masterBpm / 60.0;
                    segment.DurationBeats = segment.DurationSeconds * masterBpm / 60.0;
                    segment.PauseDurationBeats = segment.PauseSeconds * masterBpm / 60.0;
                    segment.MotionDurationBeats = segment.MotionDurationSeconds * masterBpm / 60.0;
                }
            }
            return masterBpm;
        }

        private static void ValidateCommonRegionStartAndPrefix(IList<TrackSlot> tracks)
        {
            int start = tracks[0].RegionStartFloor;
            if (start < 0) throw new InvalidOperationException("The Multi Tile region start is invalid.");

            for (int t = 0; t < tracks.Count; t++)
            {
                TrackSlot track = tracks[t];
                if (track.RegionStartFloor != start)
                    throw new InvalidOperationException("All source tracks must use the same Multi Tile start floor; '" + track.Name + "' uses F" + track.RegionStartFloor + " instead of F" + start + ".");
                if (track.Data == null || track.Data.angleData == null || track.Data.angleData.Count < start)
                    throw new InvalidOperationException("Track '" + track.Name + "' does not contain the full shared prefix through F" + start + ".");
            }

            for (int i = 0; i < start; i++)
            {
                double expected = tracks[0].Data.angleData[i];
                for (int t = 1; t < tracks.Count; t++)
                {
                    double actual = tracks[t].Data.angleData[i];
                    if (!EquivalentPrefixAngle(expected, actual))
                        throw new InvalidOperationException("Source-track prefixes differ before the Multi Tile region near angleData[" + i + "]. Store/fork tracks from the same prefix.");
                }
            }
        }

        private static void ValidateCommonRegionRuntime(IList<AnalyzedTrack> tracks)
        {
            if (tracks.Count == 0) return;
            AnalyzedTrack expected = tracks[0];
            for (int i = 1; i < tracks.Count; i++)
            {
                AnalyzedTrack actual = tracks[i];
                if (actual.RegionStartFloor != expected.RegionStartFloor)
                    throw new InvalidOperationException("Reconstructed region start floors do not match between source tracks.");
                if (AngularDistance(actual.RegionStartHeading, expected.RegionStartHeading) > PrefixAngleTolerance)
                    throw new InvalidOperationException("Source-track prefix headings differ at the Multi Tile start.");
                if (actual.RegionInheritedIsCCW != expected.RegionInheritedIsCCW)
                    throw new InvalidOperationException("Source-track prefix Twirl state differs at the Multi Tile start.");
            }
        }

        private static double ReadRegionStartHeading(TrackSlot slot, int rawFloor)
        {
            if (rawFloor <= 0) return 0.0;
            if (slot.Data == null || slot.Data.angleData == null || rawFloor - 1 >= slot.Data.angleData.Count)
                throw new InvalidOperationException("Track '" + slot.Name + "' has no prefix heading for F" + rawFloor + ".");
            double value = slot.Data.angleData[rawFloor - 1];
            if (Math.Abs(value - 999.0) < 0.001)
                throw new InvalidOperationException("Track '" + slot.Name + "' starts immediately after a raw midspin marker that v1 cannot use as the master heading.");
            return NormalizeDegrees(value);
        }

        private static bool EquivalentPrefixAngle(double a, double b)
        {
            bool aMid = Math.Abs(a - 999.0) < 0.001;
            bool bMid = Math.Abs(b - 999.0) < 0.001;
            if (aMid || bMid) return aMid && bMid;
            return AngularDistance(NormalizeDegrees(a), NormalizeDegrees(b)) <= PrefixAngleTolerance;
        }

        private static double AngularDistance(double a, double b)
        {
            double d = Math.Abs(NormalizeDegrees(a) - NormalizeDegrees(b));
            return Math.Min(d, 360.0 - d);
        }

        private static double NormalizeDegrees(double value)
        {
            value %= 360.0;
            if (value < 0.0) value += 360.0;
            return value;
        }

        private static void ValidateTags(IList<TrackSlot> tracks)
        {
            var used = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < tracks.Count; i++)
            {
                TrackSlot track = tracks[i];
                if (!track.TagsReady)
                    throw new InvalidOperationException("Track '" + track.Name + "' needs distinct Planet A / Planet B tags before planning.");
                string a = track.PlanetATag.Trim();
                string b = track.PlanetBTag.Trim();
                if (!used.Add(a)) throw new InvalidOperationException("Planet tag '" + a + "' is shared by multiple groups.");
                if (!used.Add(b)) throw new InvalidOperationException("Planet tag '" + b + "' is shared by multiple groups.");
            }
        }

        private static void ValidateUnsupportedFloorState(IList<object> floors, int startIndex, string trackName)
        {
            for (int i = startIndex; i < floors.Count; i++)
            {
                object floor = floors[i];
                int floorId = ReadFloorId(floor, i);
                int numPlanets = ReadInt(floor, "numPlanets", 2);
                if (numPlanets != 2)
                    throw new InvalidOperationException("Track '" + trackName + "' uses MultiPlanet at/near floor " + floorId + "; Multi Tile groups currently require two planets.");
                if (ReadBool(floor, "freeroam", false))
                    throw new InvalidOperationException("Track '" + trackName + "' uses FreeRoam at/near floor " + floorId + "; FreeRoam is not supported yet.");
                if (ReadInt(floor, "holdLength", -1) >= 0)
                    throw new InvalidOperationException("Track '" + trackName + "' uses Hold at/near floor " + floorId + "; Hold is not supported yet.");
            }
        }

        private static AngleCandidate ReadAngleCandidate(object floor, int fallbackIndex)
        {
            double radians;
            if (!TryReadDouble(floor, "angleLength", out radians)) return AngleCandidate.Invalid();
            double degrees = Math.Abs(radians * Mathf.Rad2Deg);
            return new AngleCandidate
            {
                Valid = !double.IsNaN(degrees) && !double.IsInfinity(degrees),
                MagnitudeDegrees = degrees,
                IsCCW = ReadBool(floor, "isCCW", false),
                FloorId = ReadFloorId(floor, fallbackIndex),
                Source = "scrFloor.angleLength [rad->deg]"
            };
        }

        private static int ReadFloorId(object floor, int fallback) { return ReadInt(floor, "seqID", fallback); }
        private static double ReadDouble(object target, string name, double fallback) { double value; return TryReadDouble(target, name, out value) ? value : fallback; }

        private static int ReadInt(object target, string name, int fallback)
        {
            object value = ReadMember(target, name);
            if (value == null) return fallback;
            try { return Convert.ToInt32(value, CultureInfo.InvariantCulture); }
            catch { return fallback; }
        }

        private static bool ReadBool(object target, string name, bool fallback)
        {
            object value = ReadMember(target, name);
            if (value == null) return fallback;
            if (value is bool) return (bool)value;
            bool parsed;
            return bool.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out parsed) ? parsed : fallback;
        }

        private static bool TryReadDouble(object target, string name, out double result)
        {
            object value = ReadMember(target, name);
            if (value is double) { result = (double)value; return true; }
            if (value is float) { result = (float)value; return true; }
            if (value is int) { result = (int)value; return true; }
            if (value is long) { result = (long)value; return true; }
            return double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out result);
        }

        private static object ReadMember(object target, string name)
        {
            if (target == null) return null;
            Type type = target.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            try
            {
                PropertyInfo p = type.GetProperty(name, flags);
                if (p != null && p.GetIndexParameters().Length == 0) return p.GetValue(target, null);
            }
            catch { }
            try
            {
                FieldInfo f = type.GetField(name, flags);
                if (f != null) return f.GetValue(target);
            }
            catch { }
            return null;
        }

        private struct AngleCandidate
        {
            internal bool Valid;
            internal double MagnitudeDegrees;
            internal bool IsCCW;
            internal int FloorId;
            internal string Source;
            internal static AngleCandidate Invalid() { return new AngleCandidate { Valid = false }; }
        }
    }
}
