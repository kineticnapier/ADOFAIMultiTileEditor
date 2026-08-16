using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using ADOFAI;

namespace KineticNapier.ADOFAIMultiTileEditor
{
    // Wrap the already-tested PACL2 generator with one timing-normalization step.
    // Source tracks may have completely different SetSpeed maps; TrackAnalyzer has
    // already converted all source timing into a common constant-BPM master timeline.
    // The output chart therefore must use that master BPM and must not retain the base
    // track's SetSpeed actions, otherwise those timings would be applied a second time.
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

            LevelData original = editor.levelData.Copy();
            int selectedFloor = GameAngleProbe.TryGetSelectedFloorIndex(editor);
            bool success = false;

            try
            {
                LevelData normalized = original.Copy();
                normalized.bpm = (float)plan.MasterBpm;
                int removedSetSpeed = RemoveSetSpeedActions(normalized.levelEvents as IList);

                TrackStore.RestoreSnapshot(editor, normalized, true);
                OrbitCommitResult result = PACL2AutoGenerator.GenerateAndCommit(
                    editor, plan, preview, tracks, baseTrackIndex);

                result.Diagnostic += " Master timing uses constant "
                    + plan.MasterBpm.ToString("0.######", CultureInfo.InvariantCulture)
                    + " BPM; removed " + removedSetSpeed
                    + " base SetSpeed action(s) because source speed maps are already baked into the merged real-time timeline.";

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

        private static int RemoveSetSpeedActions(IList events)
        {
            if (events == null) return 0;
            int removed = 0;
            for (int i = events.Count - 1; i >= 0; i--)
            {
                LevelEvent ev = events[i] as LevelEvent;
                if (ev == null || !IsSetSpeed(ev)) continue;
                events.RemoveAt(i);
                removed++;
            }
            return removed;
        }

        private static bool IsSetSpeed(LevelEvent ev)
        {
            string name = ev != null && ev.info != null
                ? (ev.info.name ?? string.Empty)
                : string.Empty;
            if (string.Equals(name, "SetSpeed", StringComparison.OrdinalIgnoreCase))
                return true;

            string eventType = ev == null
                ? string.Empty
                : Convert.ToString(ev.eventType, CultureInfo.InvariantCulture) ?? string.Empty;
            return string.Equals(eventType, "SetSpeed", StringComparison.OrdinalIgnoreCase);
        }
    }
}
