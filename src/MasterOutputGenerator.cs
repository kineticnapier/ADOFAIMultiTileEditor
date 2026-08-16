using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
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
        private const double BpmTolerance = 1.0e-4;

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
                SetLevelBpm(normalized, plan.MasterBpm);
                int removedSetSpeed = RemoveSetSpeedActions(normalized.levelEvents as IList);

                TrackStore.RestoreSnapshot(editor, normalized, true);

                // Verify the value survived the normal stock restore path before emitting
                // any output. LevelData.bpm is read-only in this game build and is backed
                // by the mutable level-settings object, so writing that backing setting is
                // the supported path used here instead of assigning the property itself.
                double activeBpm = Convert.ToDouble(editor.levelData.bpm, CultureInfo.InvariantCulture);
                if (Math.Abs(activeBpm - plan.MasterBpm) > BpmTolerance)
                {
                    throw new InvalidOperationException(
                        "Could not apply constant master BPM. Requested "
                        + plan.MasterBpm.ToString("0.######", CultureInfo.InvariantCulture)
                        + ", stock LevelData reports "
                        + activeBpm.ToString("0.######", CultureInfo.InvariantCulture) + ".");
                }

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

        private static void SetLevelBpm(LevelData levelData, double bpm)
        {
            if (levelData == null) throw new ArgumentNullException("levelData");
            float value = (float)bpm;

            // Some ADOFAI builds expose a non-public setter/backing field directly.
            if (TrySetNumericMember(levelData, "bpm", value)) return;

            // Current builds expose LevelData.bpm as read-only and keep the mutable
            // value in the level settings object. Keep a couple of known/likely names
            // so this remains tolerant of minor game-version layout changes.
            string[] settingsNames = { "settings", "levelSettings", "levelDataSettings" };
            for (int i = 0; i < settingsNames.Length; i++)
            {
                object settings = ReadMember(levelData, settingsNames[i]);
                if (settings != null && TrySetNumericMember(settings, "bpm", value)) return;
            }

            throw new InvalidOperationException(
                "This ADOFAI build exposes LevelData.bpm as read-only and its mutable BPM backing setting could not be found.");
        }

        private static bool TrySetNumericMember(object target, string name, float value)
        {
            if (target == null) return false;
            Type type = target.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            try
            {
                System.Reflection.PropertyInfo property = type.GetProperty(name, flags);
                if (property != null && property.GetIndexParameters().Length == 0)
                {
                    MethodInfo setter = property.GetSetMethod(true);
                    if (setter != null)
                    {
                        setter.Invoke(target, new[] { ConvertNumeric(value, property.PropertyType) });
                        return true;
                    }
                }
            }
            catch { }

            try
            {
                FieldInfo field = type.GetField(name, flags);
                if (field != null)
                {
                    field.SetValue(target, ConvertNumeric(value, field.FieldType));
                    return true;
                }
            }
            catch { }

            // Auto-property backing field fallback.
            try
            {
                FieldInfo backing = type.GetField("<" + name + ">k__BackingField", flags);
                if (backing != null)
                {
                    backing.SetValue(target, ConvertNumeric(value, backing.FieldType));
                    return true;
                }
            }
            catch { }

            return false;
        }

        private static object ConvertNumeric(float value, Type targetType)
        {
            Type actual = Nullable.GetUnderlyingType(targetType) ?? targetType;
            if (actual == typeof(float)) return value;
            if (actual == typeof(double)) return (double)value;
            if (actual == typeof(decimal)) return (decimal)value;
            return Convert.ChangeType(value, actual, CultureInfo.InvariantCulture);
        }

        private static object ReadMember(object target, string name)
        {
            if (target == null) return null;
            Type type = target.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            try
            {
                System.Reflection.PropertyInfo property = type.GetProperty(name, flags);
                if (property != null && property.GetIndexParameters().Length == 0)
                    return property.GetValue(target, null);
            }
            catch { }

            try
            {
                FieldInfo field = type.GetField(name, flags);
                if (field != null) return field.GetValue(target);
            }
            catch { }

            return null;
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
