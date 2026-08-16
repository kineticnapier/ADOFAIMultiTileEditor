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
        private const double SpeedEpsilon = 1.0e-6;

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

            ValidateCommonSpeedMap(analyzed);
            return TimelineMerger.Merge(analyzed);
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

            var result = new AnalyzedTrack
            {
                TrackIndex = trackIndex,
                Name = slot.Name ?? ("Track " + (trackIndex + 1)),
                PlanetATag = slot.PlanetATag.Trim(),
                PlanetBTag = slot.PlanetBTag.Trim(),
                InitialPivotIsA = slot.PivotIsA,
                StartBeat = ReadRequiredDouble(floors[0], "entryBeat", slot.Name),
                EndBeat = ReadRequiredDouble(floors[floors.Count - 1], "entryBeat", slot.Name)
            };

            ValidateUnsupportedFloorState(floors, slot.Name);
            CaptureSpeedMap(floors, result);

            bool pivotIsA = slot.PivotIsA;
            for (int i = 0; i + 1 < floors.Count; i++)
            {
                object startFloor = floors[i];
                object endFloor = floors[i + 1];
                double startBeat = ReadRequiredDouble(startFloor, "entryBeat", slot.Name);
                double endBeat = ReadRequiredDouble(endFloor, "entryBeat", slot.Name);
                double duration = endBeat - startBeat;
                if (!(duration > TimelineMerger.BeatEpsilon))
                    throw new InvalidOperationException("Track '" + slot.Name + "' has a non-positive beat interval near runtime floor " + ReadFloorId(endFloor, i + 1) + ".");

                double expectedMagnitude = duration * 180.0;
                AngleCandidate current = ReadAngleCandidate(startFloor, i, expectedMagnitude);
                AngleCandidate next = ReadAngleCandidate(endFloor, i + 1, expectedMagnitude);
                AngleCandidate chosen = ChooseCandidate(current, next);
                if (!chosen.Valid)
                    throw new InvalidOperationException("Track '" + slot.Name + "' has no readable angleLength near beat " + startBeat.ToString("0.######", CultureInfo.InvariantCulture) + ".");

                double tolerance = Math.Max(AngleConsistencyAbsoluteDegrees, expectedMagnitude * AngleConsistencyRelative);
                if (chosen.ErrorDegrees > tolerance)
                {
                    throw new InvalidOperationException(
                        "Track '" + slot.Name + "' reconstructed timing/angle mismatch near source floor " + chosen.FloorId
                        + ": duration=" + duration.ToString("0.######", CultureInfo.InvariantCulture)
                        + " beats expects about " + expectedMagnitude.ToString("0.###", CultureInfo.InvariantCulture)
                        + "°, but angleLength gives " + chosen.MagnitudeDegrees.ToString("0.###", CultureInfo.InvariantCulture) + "°."
                        + " v0.4 intentionally rejects Pause/MultiPlanet/other unsupported timing cases instead of guessing.");
                }

                string moving = pivotIsA ? result.PlanetBTag : result.PlanetATag;
                string center = pivotIsA ? result.PlanetATag : result.PlanetBTag;
                double amount = chosen.IsCCW ? chosen.MagnitudeDegrees : -chosen.MagnitudeDegrees;
                result.Segments.Add(new TrackSegment
                {
                    TrackIndex = trackIndex,
                    TrackName = result.Name,
                    SourceFloor = chosen.FloorId,
                    StartBeat = startBeat,
                    EndBeat = endBeat,
                    DurationBeats = duration,
                    AmountDegrees = amount,
                    AngleSource = chosen.Source,
                    MovingTag = moving,
                    CenterTag = center
                });
                pivotIsA = !pivotIsA;
            }

            return result;
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

        private static void CaptureSpeedMap(IList<object> floors, AnalyzedTrack track)
        {
            double lastSpeed = double.NaN;
            for (int i = 0; i < floors.Count; i++)
            {
                object floor = floors[i];
                double beat = ReadRequiredDouble(floor, "entryBeat", track.Name);
                double speed = ReadDouble(floor, "speed", 1.0);
                if (double.IsNaN(lastSpeed) || Math.Abs(speed - lastSpeed) > SpeedEpsilon)
                {
                    track.SpeedMap.Add(new SpeedPoint { Beat = beat, Speed = speed });
                    lastSpeed = speed;
                }
            }
        }

        private static void ValidateCommonSpeedMap(IList<AnalyzedTrack> tracks)
        {
            if (tracks.Count < 2) return;
            AnalyzedTrack expected = tracks[0];
            for (int t = 1; t < tracks.Count; t++)
            {
                AnalyzedTrack actual = tracks[t];
                if (actual.SpeedMap.Count != expected.SpeedMap.Count)
                    throw new InvalidOperationException("Timing map differs between '" + expected.Name + "' and '" + actual.Name + "' (different speed-change count).");
                for (int i = 0; i < expected.SpeedMap.Count; i++)
                {
                    if (!TimelineMerger.NearlyEqual(expected.SpeedMap[i].Beat, actual.SpeedMap[i].Beat)
                        || Math.Abs(expected.SpeedMap[i].Speed - actual.SpeedMap[i].Speed) > SpeedEpsilon)
                    {
                        throw new InvalidOperationException("Timing map differs between '" + expected.Name + "' and '" + actual.Name + "' near speed change #" + (i + 1) + ".");
                    }
                }
            }
        }

        private static AngleCandidate ReadAngleCandidate(object floor, int fallbackIndex, double expectedMagnitude)
        {
            double radians;
            if (!TryReadDouble(floor, "angleLength", out radians)) return AngleCandidate.Invalid();
            double degrees = Math.Abs(radians * Mathf.Rad2Deg);
            return new AngleCandidate
            {
                Valid = true,
                MagnitudeDegrees = degrees,
                ErrorDegrees = Math.Abs(degrees - expectedMagnitude),
                IsCCW = ReadBool(floor, "isCCW", false),
                FloorId = ReadFloorId(floor, fallbackIndex),
                Source = "scrFloor.angleLength [rad->deg]"
            };
        }

        private static AngleCandidate ChooseCandidate(AngleCandidate current, AngleCandidate next)
        {
            if (!current.Valid) return next;
            if (!next.Valid) return current;
            return next.ErrorDegrees <= current.ErrorDegrees ? next : current;
        }

        private static int ReadFloorId(object floor, int fallback)
        {
            return ReadInt(floor, "seqID", fallback);
        }

        private static double ReadRequiredDouble(object target, string name, string trackName)
        {
            double value;
            if (!TryReadDouble(target, name, out value))
                throw new InvalidOperationException("Track '" + trackName + "' runtime floor has no readable " + name + ".");
            return value;
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
            internal double ErrorDegrees;
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
