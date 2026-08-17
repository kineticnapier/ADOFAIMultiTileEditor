using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using ADOFAI;
using UnityEngine;

namespace KineticNapier.ADOFAIMultiTileEditor
{
    internal sealed class MasterPathPreview
    {
        // Region-only angleData appended after the preserved prefix.
        internal readonly List<float> AngleData = new List<float>();
        internal readonly List<double> RuntimeAnchorBeats = new List<double>();
        internal int RuntimeFloorCount;
        internal double MaxAngleErrorDegrees;
        internal double MaxBeatError;
        internal string Diagnostic;
    }

    internal static class MasterPathBuilder
    {
        private const double AngleToleranceDegrees = 0.02;
        private const double BeatVerificationTolerance = 2.0e-6;
        private const double MaxSingleTravelDegrees = 360.0;

        internal static MasterPathPreview BuildAndVerify(scnEditor editor, GenerationPlan plan)
        {
            if (editor == null || editor.levelData == null)
                throw new InvalidOperationException("Editor is not ready.");
            if (plan == null || plan.Anchors.Count < 2)
                throw new InvalidOperationException("Analyze a region plan before building the master path.");
            if (editor.levelData.angleData == null || editor.levelData.angleData.Count < plan.RegionStartFloor)
                throw new InvalidOperationException("The active chart no longer contains the shared prefix through F" + plan.RegionStartFloor + ".");

            var preview = new MasterPathPreview();
            SynthesizeAngleData(plan, preview.AngleData);

            LevelData original = editor.levelData.Copy();
            int selectedFloor = GameAngleProbe.TryGetSelectedFloorIndex(editor);

            try
            {
                LevelData candidate = original.Copy();
                candidate.isOldLevel = false;
                ReplaceRegionAngleData(candidate.angleData, original.angleData, plan.RegionStartFloor, preview.AngleData);
                RemoveEventsAtOrAfter(candidate.levelEvents as IList, plan.RegionStartFloor);

                TrackStore.RestoreSnapshot(editor, candidate, false);
                VerifyRuntime(editor, plan, preview);
            }
            finally
            {
                TrackStore.RestoreSnapshot(editor, original, true);
                if (selectedFloor >= 0 && selectedFloor < editor.floors.Count)
                    editor.SelectFloor(editor.floors[selectedFloor], true);
            }

            preview.Diagnostic = "Master region OK: preserved prefix through F" + plan.RegionStartFloor + "; "
                + preview.AngleData.Count + " synthesized angleData value(s) -> "
                + preview.RuntimeFloorCount + " region anchor floor(s), max angle error "
                + preview.MaxAngleErrorDegrees.ToString("0.######", CultureInfo.InvariantCulture) + "°, max beat error "
                + preview.MaxBeatError.ToString("0.0e+0", CultureInfo.InvariantCulture)
                + ". Active chart restored unchanged.";
            return preview;
        }

        internal static void VerifyCurrentRuntime(scnEditor editor, GenerationPlan plan, MasterPathPreview preview)
        {
            if (editor == null || plan == null || preview == null)
                throw new InvalidOperationException("Master path verification inputs are incomplete.");
            VerifyRuntime(editor, plan, preview);
        }

        internal static void ReplaceRegionAngleData(IList<float> target, IList<float> sourcePrefix, int regionStartFloor, IList<float> regionAngles)
        {
            if (target == null || sourcePrefix == null || regionAngles == null)
                throw new InvalidOperationException("Master path angleData inputs are unavailable.");
            if (regionStartFloor < 0 || sourcePrefix.Count < regionStartFloor)
                throw new InvalidOperationException("The source prefix is shorter than the Multi Tile start floor.");

            target.Clear();
            for (int i = 0; i < regionStartFloor; i++) target.Add(sourcePrefix[i]);
            for (int i = 0; i < regionAngles.Count; i++) target.Add(regionAngles[i]);
        }

        private static void SynthesizeAngleData(GenerationPlan plan, IList<float> output)
        {
            output.Clear();
            double previousHeading = plan.RegionStartHeading;
            bool inheritedIsCCW = plan.RegionInheritedIsCCW;

            for (int i = 0; i + 1 < plan.Anchors.Count; i++)
            {
                double deltaBeat = plan.Anchors[i + 1].Beat - plan.Anchors[i].Beat;
                if (!(deltaBeat > TimelineMerger.BeatEpsilon))
                    throw new InvalidOperationException("Master timeline contains a zero/negative interval near M" + i + ".");

                double travelDegrees = deltaBeat * 180.0;
                if (travelDegrees > MaxSingleTravelDegrees + AngleToleranceDegrees)
                {
                    throw new InvalidOperationException(
                        "Master interval M" + i + " -> M" + (i + 1) + " requires "
                        + travelDegrees.ToString("0.###", CultureInfo.InvariantCulture)
                        + "°. Current path synthesis supports at most 360° per anchor interval; helper/midspin insertion is not implemented yet.");
                }

                double heading = inheritedIsCCW
                    ? NormalizeDegrees(previousHeading - 180.0 + travelDegrees)
                    : NormalizeDegrees(previousHeading + 180.0 - travelDegrees);
                if (heading < 0.0001 || heading > 359.9999) heading = 0.0;
                output.Add((float)heading);
                previousHeading = heading;
            }
        }

        private static void VerifyRuntime(scnEditor editor, GenerationPlan plan, MasterPathPreview preview)
        {
            var allFloors = new List<object>();
            for (int i = 0; i < editor.floors.Count; i++)
            {
                object floor = editor.floors[i];
                if (floor == null || ReadBool(floor, "midSpin", false)) continue;
                allFloors.Add(floor);
            }

            int regionIndex = FindRegionFloorIndex(editor, allFloors, plan.RegionStartFloor);
            if (regionIndex < 0)
                throw new InvalidOperationException("Synthesized path lost the Multi Tile start floor F" + plan.RegionStartFloor + ".");

            int regionFloorCount = allFloors.Count - regionIndex;
            preview.RuntimeFloorCount = regionFloorCount;
            if (regionFloorCount != plan.Anchors.Count)
            {
                throw new InvalidOperationException(
                    "Synthesized region produced " + regionFloorCount + " non-midspin runtime floors from F" + plan.RegionStartFloor
                    + ", but the master timeline has " + plan.Anchors.Count + " anchors. Expected region angleData count="
                    + (plan.Anchors.Count - 1) + ".");
            }

            preview.RuntimeAnchorBeats.Clear();
            preview.RuntimeAnchorBeats.Add(0.0);
            double cumulativeBeat = 0.0;
            double maxAngleError = 0.0;
            double maxBeatError = 0.0;

            object firstFloor = allFloors[regionIndex];
            double firstEntryBeat;
            bool haveFirstEntryBeat = TryReadDouble(firstFloor, "entryBeat", out firstEntryBeat);

            for (int i = 0; i + 1 < regionFloorCount; i++)
            {
                object floor = allFloors[regionIndex + i];
                double radians;
                if (!TryReadDouble(floor, "angleLength", out radians))
                    throw new InvalidOperationException("Synthesized runtime floor F" + ReadFloorId(floor, plan.RegionStartFloor + i) + " has no readable angleLength.");

                double actualDegrees = Math.Abs(radians * Mathf.Rad2Deg);
                double targetDegrees = (plan.Anchors[i + 1].Beat - plan.Anchors[i].Beat) * 180.0;
                double angleError = Math.Abs(actualDegrees - targetDegrees);
                if (angleError > maxAngleError) maxAngleError = angleError;
                if (angleError > AngleToleranceDegrees)
                {
                    throw new InvalidOperationException(
                        "Master region verification failed at M" + i + ": target travel "
                        + targetDegrees.ToString("0.######", CultureInfo.InvariantCulture) + "°, stock angleLength gives "
                        + actualDegrees.ToString("0.######", CultureInfo.InvariantCulture) + "°."
                        + " Synthesized heading=" + preview.AngleData[i].ToString("0.######", CultureInfo.InvariantCulture) + "°.");
                }

                cumulativeBeat += actualDegrees / 180.0;
                preview.RuntimeAnchorBeats.Add(cumulativeBeat);
                double targetCumulativeBeat = plan.Anchors[i + 1].Beat - plan.StartBeat;
                double cumulativeError = Math.Abs(cumulativeBeat - targetCumulativeBeat);
                if (cumulativeError > maxBeatError) maxBeatError = cumulativeError;
                if (cumulativeError > BeatVerificationTolerance)
                {
                    throw new InvalidOperationException(
                        "Master region cumulative timing drift at M" + (i + 1) + ": target "
                        + targetCumulativeBeat.ToString("0.######", CultureInfo.InvariantCulture) + " beats, stock path gives "
                        + cumulativeBeat.ToString("0.######", CultureInfo.InvariantCulture) + " beats.");
                }

                if (haveFirstEntryBeat)
                {
                    double entryBeat;
                    if (TryReadDouble(floor, "entryBeat", out entryBeat))
                    {
                        double relativeEntry = entryBeat - firstEntryBeat;
                        double targetEntry = plan.Anchors[i].Beat - plan.StartBeat;
                        double entryError = Math.Abs(relativeEntry - targetEntry);
                        if (entryError > maxBeatError) maxBeatError = entryError;
                        if (entryError > BeatVerificationTolerance)
                        {
                            throw new InvalidOperationException(
                                "Master region entryBeat mismatch at M" + i + ": target "
                                + targetEntry.ToString("0.######", CultureInfo.InvariantCulture) + ", stock entryBeat gives "
                                + relativeEntry.ToString("0.######", CultureInfo.InvariantCulture) + ".");
                        }
                    }
                }
            }

            preview.MaxAngleErrorDegrees = maxAngleError;
            preview.MaxBeatError = maxBeatError;
        }

        private static int FindRegionFloorIndex(scnEditor editor, IList<object> filteredFloors, int regionStartFloor)
        {
            if (editor.floors != null && regionStartFloor >= 0 && regionStartFloor < editor.floors.Count)
            {
                object raw = editor.floors[regionStartFloor];
                for (int i = 0; i < filteredFloors.Count; i++)
                    if (ReferenceEquals(filteredFloors[i], raw)) return i;
            }
            for (int i = 0; i < filteredFloors.Count; i++)
                if (ReadFloorId(filteredFloors[i], -1) == regionStartFloor) return i;
            return -1;
        }

        private static void RemoveEventsAtOrAfter(IList events, int regionStartFloor)
        {
            if (events == null) return;
            for (int i = events.Count - 1; i >= 0; i--)
            {
                int floor;
                if (!TryReadInt(events[i], "floor", out floor)) continue;
                if (floor >= regionStartFloor) events.RemoveAt(i);
            }
        }

        private static bool TryReadInt(object target, string name, out int result)
        {
            object value = ReadMember(target, name);
            try
            {
                if (value != null) { result = Convert.ToInt32(value, CultureInfo.InvariantCulture); return true; }
            }
            catch { }
            result = -1;
            return false;
        }

        private static double NormalizeDegrees(double degrees)
        {
            degrees %= 360.0;
            if (degrees < 0.0) degrees += 360.0;
            if (Math.Abs(degrees - 360.0) < 1.0e-9) degrees = 0.0;
            return degrees;
        }

        private static int ReadFloorId(object floor, int fallback)
        {
            object value = ReadMember(floor, "seqID");
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
                System.Reflection.PropertyInfo p = type.GetProperty(name, flags);
                if (p != null && p.GetIndexParameters().Length == 0) return p.GetValue(target, null);
            }
            catch { }
            try
            {
                System.Reflection.FieldInfo f = type.GetField(name, flags);
                if (f != null) return f.GetValue(target);
            }
            catch { }
            return null;
        }
    }
}
