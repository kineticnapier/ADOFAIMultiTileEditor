using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;

namespace KineticNapier.ADOFAIMultiTileEditor
{
    internal sealed class AngleSample
    {
        internal bool Valid;
        internal float Degrees;
        internal string Source;

        internal static AngleSample Invalid(string source)
        {
            return new AngleSample { Valid = false, Degrees = 0f, Source = source ?? "unavailable" };
        }
    }

    internal static class GameAngleProbe
    {
        private static readonly string[] PreferredMethods =
        {
            "GetAngleMoved",
            "GetAngleMovedNoMod",
            "GetFloorAngle"
        };

        private static readonly string[] PreferredMembers =
        {
            "angleLength",
            "floorAngle",
            "angle"
        };

        internal static List<AngleSample> Capture(scnEditor editor)
        {
            var result = new List<AngleSample>();
            if (editor == null || editor.floors == null) return result;

            for (int i = 0; i < editor.floors.Count; i++)
                result.Add(ReadFloorAngle(editor.floors[i]));

            return result;
        }

        internal static int TryGetSelectedFloorIndex(scnEditor editor)
        {
            if (editor == null || editor.floors == null || editor.floors.Count == 0) return -1;

            // The stock editor keeps its current tile selection in selectedFloors.
            // For a multi-selection, generate on the first selected floor.
            try
            {
                if (editor.selectedFloors != null && editor.selectedFloors.Count > 0)
                {
                    object selected = editor.selectedFloors[0];
                    for (int i = 0; i < editor.floors.Count; i++)
                        if (ReferenceEquals(editor.floors[i], selected)) return i;
                }
            }
            catch
            {
            }

            // Compatibility fallback for older editor builds/modded editor states.
            object floor = ReadMember(editor, "currFloor") ?? ReadMember(editor, "selectedFirstFloor");
            if (floor != null)
            {
                for (int i = 0; i < editor.floors.Count; i++)
                    if (ReferenceEquals(editor.floors[i], floor)) return i;
            }

            return -1;
        }

        internal static int TryGetCurrentFloorIndex(scnEditor editor)
        {
            return TryGetSelectedFloorIndex(editor);
        }

        private static AngleSample ReadFloorAngle(object floor)
        {
            if (floor == null) return AngleSample.Invalid("null floor");
            Type type = floor.GetType();

            for (int i = 0; i < PreferredMethods.Length; i++)
            {
                MethodInfo method = type.GetMethod(
                    PreferredMethods[i],
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    Type.EmptyTypes,
                    null);

                if (method == null) continue;
                try
                {
                    object value = method.Invoke(floor, null);
                    float numeric;
                    if (TryFloat(value, out numeric))
                    {
                        bool radians = Math.Abs(numeric) <= (Math.PI * 2.0 + 0.001);
                        float degrees = radians ? numeric * Mathf.Rad2Deg : numeric;
                        return new AngleSample
                        {
                            Valid = true,
                            Degrees = degrees,
                            Source = type.Name + "." + method.Name + "()" + (radians ? " [rad->deg]" : " [deg]")
                        };
                    }
                }
                catch
                {
                }
            }

            for (int i = 0; i < PreferredMembers.Length; i++)
            {
                string member = PreferredMembers[i];
                object value = ReadMember(floor, member);
                float numeric;
                if (!TryFloat(value, out numeric)) continue;

                bool radians = string.Equals(member, "angleLength", StringComparison.OrdinalIgnoreCase)
                    || Math.Abs(numeric) <= (Math.PI * 2.0 + 0.001);
                float degrees = radians ? numeric * Mathf.Rad2Deg : numeric;
                return new AngleSample
                {
                    Valid = true,
                    Degrees = degrees,
                    Source = type.Name + "." + member + (radians ? " [rad->deg]" : " [deg]")
                };
            }

            return AngleSample.Invalid(type.Name + ": no known angle member");
        }

        private static object ReadMember(object target, string name)
        {
            if (target == null) return null;
            Type type = target.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            try
            {
                PropertyInfo property = type.GetProperty(name, flags);
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

        private static bool TryFloat(object value, out float result)
        {
            if (value is float) { result = (float)value; return true; }
            if (value is double) { result = (float)(double)value; return true; }
            if (value is int) { result = (int)value; return true; }
            if (value is long) { result = (long)value; return true; }
            return float.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out result);
        }
    }
}
