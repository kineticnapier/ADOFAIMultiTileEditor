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

        internal static GenerationPlan BuildPlan(scnEditor editor, IList<TrackSlot> tracks)
        {
            if (editor == null || editor.levelData == null)
                throw new InvalidOperationException("Editor is not ready.");
            if (tracks == null || tracks.Count == 0)
                throw new InvalidOperationException("Store at least one source track first.");

            ValidateTags(tracks);

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

            double masterBpm = NormalizeToMasterTimeline(analyzed);
            GenerationPlan plan = TimelineMerger.Merge(analyzed);
            plan.MasterBpm = masterBpm;
            plan.StartSeconds = analyzed[0].StartSeconds;
            plan.EndSeconds = analyzed[0].EndSeconds;
            plan.Diagnostic = plan.Diagnostic.TrimEnd('.')
                + "; constant master BPM " + masterBpm.ToString("0.######", CultureInfo.InvariantCulture)
                + " (source SetSpeed maps baked into real-time segment durations).";
            return plan;
        }

        private static AnalyzedTrack AnalyzeCurrentRuntime(scnEditor editor, TrackSlot slot, int trackIndex)
        {
            if (editor.floors == null || editor.floors.Count < 2)
                throw new InvalidOperationException("Track '" + slot.Name + "' has fewer than two runtime floors.");

            var floors = new List<object>();
            for (int i = 0; i < editor.floors.Count; i++)
            {
                object floor = editor.floors[i];
                if (floor == null) continue;
                if (ReadBool(floor, "midSpin", false)) continue;
                floors.Add(floor);
            }
            if (floors.Count < 2)
                throw new InvalidOperationException("Track '" + slot.Name + "' has fewer than two landable floors after midspin filtering.");

            double baseBpm = Convert.ToDouble(editor.levelData.bpm, CultureInfo.InvariantCulture);
            if (!(baseBpm > MinBpm) || double.IsNaN(baseBpm) || double.IsInfinity(baseBpm))
                throw new InvalidOperationException("Track '" + slot.Name + "' has an invalid base BPM.");

            var result = new AnalyzedTrack
            {
                TrackIndex = trackIndex,
                Name = slot.Name ?? ("Track " + (trackIndex + 1)),
                PlanetATag = slot.PlanetATag.Trim(),
                PlanetBTag = slot.PlanetBTag.Trim(),
                InitialPivotIsA = slot.PivotIsA,
                BaseBpm = baseBpm,
                StartBeat = 0.0,
                EndBeat = 0.0,
                StartSeconds = 0.0,
                EndSeconds = 0.0
            };

            ValidateUnsupportedFloorState(floors, slot.Name);

            bool pivotIsA = slot.PivotIsA;
            double cursorSeconds = 0.0;
            for (int i = 0; i + 1 < floors.Count; i++)
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

                double durationSeconds = sourceDurationBeats * 60.0 / effectiveBpm;
                string source = chosen.Source + " + BPM*speed timing";

                // entryTime is the stock runtime's real-time truth. Use it when it is
                // available and sane; if it differs materially from the simple BPM*speed
                // calculation, prefer the reconstructed runtime value rather than guessing.
                double runtimeStartTime;
                double runtimeEndTime;
                if (TryReadDouble(startFloor, "entryTime", out runtimeStartTime)
                    && TryReadDouble(endFloor, "entryTime", out runtimeEndTime)
                    && runtimeEndTime - runtimeStartTime > 1.0e-9)
                {
                    double runtimeSeconds = runtimeEndTime - runtimeStartTime;
                    double tolerance = Math.Max(TimeConsistencyAbsoluteSeconds, durationSeconds * TimeConsistencyRelative);
                    if (Math.Abs(runtimeSeconds - durationSeconds) > tolerance)
                    {
                        durationSeconds = runtimeSeconds;
                        effectiveBpm = sourceDurationBeats * 60.0 / durationSeconds;
                        source += " + entryTime override";
                    }
                    else
                    {
                        // Keep the exact BPM/speed-derived duration to avoid accumulating
                        // float noise across long charts, but record that stock timing agreed.
                        source += " + entryTime check";
                    }
                }
                else
                {
                    source += " + terminal-safe fallback";
                }

                // entryBeat is independent of SetSpeed and remains useful as an angle
                // consistency check. Synthetic terminal floors may have it unset.
                double runtimeStartBeat;
                double runtimeEndBeat;
                if (TryReadDouble(startFloor, "entryBeat", out runtimeStartBeat)
                    && TryReadDouble(endFloor, "entryBeat", out runtimeEndBeat)
                    && runtimeEndBeat - runtimeStartBeat > TimelineMerger.BeatEpsilon)
                {
                    double runtimeMagnitude = (runtimeEndBeat - runtimeStartBeat) * 180.0;
                    double tolerance = Math.Max(AngleConsistencyAbsoluteDegrees, chosen.MagnitudeDegrees * AngleConsistencyRelative);
                    if (Math.Abs(runtimeMagnitude - chosen.MagnitudeDegrees) > tolerance)
                    {
                        throw new InvalidOperationException(
                            "Track '" + slot.Name + "' reconstructed timing/angle mismatch near source floor " + chosen.FloorId
                            + ": entryBeat interval implies " + runtimeMagnitude.ToString("0.###", CultureInfo.InvariantCulture)
                            + "°, but angleLength gives " + chosen.MagnitudeDegrees.ToString("0.###", CultureInfo.InvariantCulture) + "°."
                            + " Unsupported timing geometry is rejected instead of guessed.");
                    }
                }

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

            result.EndSeconds = cursorSeconds;
            return result;
        }

        private static double NormalizeToMasterTimeline(IList<AnalyzedTrack> tracks)
        {
            if (tracks == null || tracks.Count == 0)
                throw new InvalidOperationException("No analyzed tracks were supplied for timing normalization.");

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

                for (int s = 0; s < track.Segments.Count; s++)
                {
                    TrackSegment segment = track.Segments[s];
                    segment.StartBeat = segment.StartSeconds * masterBpm / 60.0;
                    segment.EndBeat = segment.EndSeconds * masterBpm / 60.0;
                    segment.DurationBeats = segment.DurationSeconds * masterBpm / 60.0;
                }
            }

            return masterBpm;
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

        private static void ValidateUnsupportedFloorState(IList<object> floors, string trackName)
        {
            for (int i = 0; i < floors.Count; i++)
            {
                object floor = floors[i];
                int floorId = ReadFloorId(floor, i);
                int numPlanets = ReadInt(floor, "numPlanets", 2);
                if (numPlanets != 2)
                    throw new InvalidOperationException("Track '" + trackName + "' uses MultiPlanet at/near floor " + floorId + "; v1 source groups must stay two-planet.");
                if (Math.Abs(ReadDouble(floor, "extraBeats", 0.0)) > TimelineMerger.BeatEpsilon)
                    throw new InvalidOperationException("Track '" + trackName + "' uses Pause/extraBeats at/near floor " + floorId + "; v1 does not merge Pause.");
                if (ReadBool(floor, "freeroam", false))
                    throw new InvalidOperationException("Track '" + trackName + "' uses FreeRoam at/near floor " + floorId + "; v1 does not support it.");
                if (ReadInt(floor, "holdLength", -1) >= 0)
                    throw new InvalidOperationException("Track '" + trackName + "' uses Hold at/near floor " + floorId + "; v1 does not support it.");
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

        private static int ReadFloorId(object floor, int fallback)
        {
            return ReadInt(floor, "seqID", fallback);
        }

        private static double ReadDouble(object target, string name, double fallback)
        {
            double value;
            return TryReadDouble(target, name, out value) ? value : fallback;
        }

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

            internal static AngleCandidate Invalid()
            {
                return new AngleCandidate { Valid = false };
            }
        }
    }
}
