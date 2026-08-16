using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using ADOFAI;

namespace KineticNapier.ADOFAIMultiTileEditor
{
    // Source tracks may have completely different SetSpeed maps. TrackAnalyzer has
    // already converted them into one constant-BPM master timeline. LevelData.bpm is
    // read-only in current ADOFAI builds, so the output keeps the chart's base BPM and
    // installs one absolute SetSpeed at floor 0 to establish the selected master BPM.
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
                IList events = normalized.levelEvents as IList;
                if (events == null)
                    throw new InvalidOperationException("LevelData.levelEvents is not list-compatible in this game build.");

                int removedSetSpeed = RemoveSetSpeedActions(events);
                LevelEvent masterSpeed = CreateSetSpeedEvent(plan.MasterBpm);
                events.Add(masterSpeed);

                TrackStore.RestoreSnapshot(editor, normalized, true);
                VerifyMasterSetSpeed(editor.levelData.levelEvents as IList, plan.MasterBpm);

                OrbitCommitResult result = PACL2AutoGenerator.GenerateAndCommit(
                    editor, plan, preview, tracks, baseTrackIndex);

                result.Diagnostic += " Master timing uses constant "
                    + plan.MasterBpm.ToString("0.######", CultureInfo.InvariantCulture)
                    + " BPM via a floor-0 SetSpeed; replaced " + removedSetSpeed
                    + " source/base SetSpeed action(s) because their timing is already baked into the merged real-time timeline.";

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

        private static LevelEvent CreateSetSpeedEvent(double bpm)
        {
            LevelEventInfo info = ResolveEventInfo("SetSpeed");
            LevelEvent ev = new LevelEvent(0, info.type, info);

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
            if (GCS.levelEventsInfo.TryGetValue(requestedName, out direct) && direct != null)
                return direct;

            foreach (KeyValuePair<string, LevelEventInfo> pair in GCS.levelEventsInfo)
            {
                if (pair.Value == null) continue;
                if (string.Equals(pair.Key, requestedName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(pair.Value.name, requestedName, StringComparison.OrdinalIgnoreCase))
                    return pair.Value;
            }

            throw new InvalidOperationException("Stock SetSpeed event metadata is unavailable in this ADOFAI build.");
        }

        private static void SetRequiredData(LevelEvent ev, string key, object value)
        {
            if (ev == null || ev.info == null || ev.info.propertiesInfo == null
                || !ev.info.propertiesInfo.ContainsKey(key))
                throw new InvalidOperationException("SetSpeed metadata has no property '" + key + "'.");
            SetTypedData(ev, key, value);
        }

        private static void SetOptionalData(LevelEvent ev, string key, object value)
        {
            if (ev == null || ev.info == null || ev.info.propertiesInfo == null
                || !ev.info.propertiesInfo.ContainsKey(key)) return;
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
            if (actual == typeof(string))
                return Convert.ToString(value, CultureInfo.InvariantCulture);
            return Convert.ChangeType(value, actual, CultureInfo.InvariantCulture);
        }

        private static object SafeGetData(LevelEvent ev, string key)
        {
            if (ev == null) return null;
            try { return ev[key]; }
            catch { return null; }
        }

        private static void VerifyMasterSetSpeed(IList events, double expectedBpm)
        {
            if (events == null)
                throw new InvalidOperationException("Stock restore lost the level event list while applying master timing.");

            for (int i = 0; i < events.Count; i++)
            {
                LevelEvent ev = events[i] as LevelEvent;
                if (ev == null || !IsSetSpeed(ev) || ev.floor != 0) continue;

                string speedType = Convert.ToString(SafeGetData(ev, "speedType"), CultureInfo.InvariantCulture) ?? "";
                double bpm;
                try { bpm = Convert.ToDouble(SafeGetData(ev, "beatsPerMinute"), CultureInfo.InvariantCulture); }
                catch { continue; }

                if (string.Equals(speedType, "Bpm", StringComparison.OrdinalIgnoreCase)
                    && Math.Abs(bpm - expectedBpm) <= 1.0e-4)
                    return;
            }

            throw new InvalidOperationException(
                "The generated floor-0 SetSpeed did not survive stock reconstruction at "
                + expectedBpm.ToString("0.######", CultureInfo.InvariantCulture) + " BPM.");
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
