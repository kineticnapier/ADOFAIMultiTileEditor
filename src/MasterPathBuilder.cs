using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using ADOFAI;
using UnityEngine;

namespace KineticNapier.ADOFAIMultiTileEditor
{
    internal sealed class MasterPathPreview
    {
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
                throw new InvalidOperationException("Analyze a whole-region plan before building the master path.");

            var preview = new MasterPathPreview();
            SynthesizeAngleData(plan, preview.AngleData);

            LevelData original = editor.levelData.Copy();
            int selectedFloor = GameAngleProbe.TryGetSelectedFloorIndex(editor);

            try
            {
                LevelData candidate = original.Copy();
                candidate.isOldLevel = false;
                candidate.angleData.Clear();
                for (int i = 0; i < preview.AngleData.Count; i++)
                    candidate.angleData.Add(preview.AngleData[i]);

                // This stage verifies path geometry/timing only. Existing actions are not
                // remapped yet, so remove them from the temporary candidate to prevent a
                // source-track Twirl/SetSpeed/etc. floor index from changing the test.
                candidate.levelEvents.Clear();

                TrackStore.RestoreSnapshot(editor, candidate, false);
                VerifyRuntime(editor, plan, preview);
            }
            finally
            {
                TrackStore.RestoreSnapshot(editor, original, true);
                if (selectedFloor >= 0 && selectedFloor < editor.floors.Count)
                    editor.SelectFloor(editor.floors[selectedFloor], true);
            }

            preview.Diagnostic = "Master path OK: " + preview.AngleData.Count + " angleData value(s) -> "
                + preview.RuntimeFloorCount + " runtime anchor floor(s), max angle error "
                + preview.MaxAngleErrorDegrees.ToString("0.######", CultureInfo.InvariantCulture) + "°, max beat error "
                + preview.MaxBeatError.ToString("0.0e+0", CultureInfo.InvariantCulture)
                + ". Active chart restored unchanged.";
            return preview;
        }

        private static void SynthesizeAngleData(GenerationPlan plan, IList<float> output)
        {
            output.Clear();
            double previousHeading = 0.0;

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
                        + "°. v0.5 path synthesis currently supports at most 360° per anchor interval; helper/midspin floor insertion is not implemented yet.");
                }

                // For a normal clockwise two-planet path, stock ADOFAI travel is
                //   travel = 180° - (nextHeading - previousHeading)  (mod 360°).
                // The first tile uses the same relation against the virtual 0° start heading.
                double heading = NormalizeDegrees(previousHeading + 180.0 - travelDegrees);
                output.Add((float)heading);
                previousHeading = heading;
            }
        }

        private static void VerifyRuntime(scnEditor editor, GenerationPlan plan, MasterPathPreview preview)
        {
            var floors = new List<object>();
            for (int i = 0; i < editor.floors.Count; i++)
            {
                object floor = editor.floors[i];
                if (floor == null) continue;
                if (ReadBool(floor, "midSpin", false)) continue;
                floors.Add(floor);
            }

            preview.RuntimeFloorCount = floors.Count;
            if (floors.Count != plan.Anchors.Count)
            {
                throw new InvalidOperationException(
                    "Synthesized path produced " + floors.Count + " non-midspin runtime floors, but the master timeline has "
                    + plan.Anchors.Count + " anchors. Expected angleData count=" + (plan.Anchors.Count - 1) + ".");
            }

            preview.RuntimeAnchorBeats.Clear();
            preview.RuntimeAnchorBeats.Add(0.0);
            double cumulativeBeat = 0.0;
            double maxAngleError = 0.0;
            double maxBeatError = 0.0;

            double firstEntryBeat;
            bool haveFirstEntryBeat = TryReadDouble(floors[0], "entryBeat", out firstEntryBeat);

            for (int i = 0; i + 1 < floors.Count; i++)
            {
                double radians;
                if (!TryReadDouble(floors[i], "angleLength", out radians))
                    throw new InvalidOperationException("Synthesized runtime floor F" + ReadFloorId(floors[i], i) + " has no readable angleLength.");

                double actualDegrees = Math.Abs(radians * Mathf.Rad2Deg);
                double targetDegrees = (plan.Anchors[i + 1].Beat - plan.Anchors[i].Beat) * 180.0;
                double angleError = Math.Abs(actualDegrees - targetDegrees);
                if (angleError > maxAngleError) maxAngleError = angleError;
                if (angleError > AngleToleranceDegrees)
                {
                    throw new InvalidOperationException(
                        "Master path verification failed at M" + i + ": target travel "
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
                        "Master path cumulative timing drift at M" + (i + 1) + ": target "
                        + targetCumulativeBeat.ToString("0.######", CultureInfo.InvariantCulture) + " beats, stock path gives "
                        + cumulativeBeat.ToString("0.######", CultureInfo.InvariantCulture) + " beats.");
                }

                // Also verify stock entryBeat wherever it is readable. The final synthetic
                // portal floor has historically been unreliable, so this checks each real
                // outgoing floor and leaves the endpoint to the angleLength accumulation above.
                if (haveFirstEntryBeat)
                {
                    double entryBeat;
                    if (TryReadDouble(floors[i], "entryBeat", out entryBeat))
                    {
                        double relativeEntry = entryBeat - firstEntryBeat;
                        double targetEntry = plan.Anchors[i].Beat - plan.StartBeat;
                        double entryError = Math.Abs(relativeEntry - targetEntry);
                        if (entryError > maxBeatError) maxBeatError = entryError;
                        if (entryError > BeatVerificationTolerance)
                        {
                            throw new InvalidOperationException(
                                "Master path entryBeat mismatch at M" + i + ": target "
                                + targetEntry.ToString("0.######", CultureInfo.InvariantCulture) + ", stock entryBeat gives "
                                + relativeEntry.ToString("0.######", CultureInfo.InvariantCulture) + ".");
                        }
                    }
                }
            }

            preview.MaxAngleErrorDegrees = maxAngleError;
            preview.MaxBeatError = maxBeatError;
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
    }
}
