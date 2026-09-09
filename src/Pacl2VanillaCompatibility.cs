using System;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace KineticNapier.ADOFAIMultiTileEditor
{
    // Stock PACL2 2.5.400 can capture OrbitDecoration's center/radius before the
    // decoration transforms have settled. Kinetic's local PACL2 build carried a
    // small StartEffect update-loop guard for this. Keep MTE compatible with the
    // untouched PACL2 DLL by reproducing that guard at runtime, but only for MTE's
    // own MTE_P* planet tags.
    internal static class Pacl2VanillaCompatibility
    {
        private const string HarmonyId = "kineticnapier.adofai.multitileeditor.pacl2compat";
        private const int Stock2400OrbitUpdateIlLength = 209;
        private const int MaxSettleCallbacks = 12;
        private const float StableAngleTolerance = 0.0005f;
        private const float RadiusEpsilon = 0.00001f;

        private static readonly BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly BindingFlags StaticFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly ConditionalWeakTable<object, SettleState> States = new ConditionalWeakTable<object, SettleState>();

        private static Harmony harmony;
        private static bool enabled;
        private static bool installed;
        private static bool terminalDecision;
        private static string status = "PACL2 compatibility: waiting for PACL2.";

        private sealed class SettleState
        {
            internal bool HasAngle;
            internal float LastAngle;
            internal int StablePhase;
            internal int Attempts;
            internal bool Applied;
        }

        internal static string Status { get { return status; } }

        internal static void SetEnabled(bool value)
        {
            enabled = value;
            if (value) Tick();
        }

        internal static void Tick()
        {
            if (!enabled || installed || terminalDecision) return;

            Assembly pacl2 = FindPacl2Assembly();
            if (pacl2 == null)
            {
                status = "PACL2 compatibility: waiting for PACL2.";
                return;
            }

            MethodInfo target = FindOrbitUpdateMethod(pacl2);
            if (target == null)
            {
                status = "PACL2 compatibility: OrbitDecoration update method was not found; no runtime patch applied.";
                terminalDecision = true;
                return;
            }

            int ilLength = GetIlLength(target);
            if (ilLength >= 400)
            {
                // The known locally patched 2.5.400 method is 487 bytes and already
                // contains the same stabilization concept. Do not stack another guard.
                status = "PACL2 compatibility: Orbit stabilization already present; runtime patch skipped.";
                terminalDecision = true;
                return;
            }

            if (ilLength != Stock2400OrbitUpdateIlLength)
            {
                status = "PACL2 compatibility: unknown OrbitDecoration build (IL " + ilLength + "); runtime patch skipped.";
                terminalDecision = true;
                return;
            }

            try
            {
                harmony = new Harmony(HarmonyId);
                MethodInfo prefix = typeof(Pacl2VanillaCompatibility).GetMethod("OrbitUpdatePrefix", StaticFlags);
                harmony.Patch(target, prefix: new HarmonyMethod(prefix));
                installed = true;
                status = "PACL2 2.5.400 stock detected: MTE Orbit stabilization active.";
            }
            catch (Exception ex)
            {
                status = "PACL2 compatibility patch failed: " + ex.GetType().Name + ": " + ex.Message;
                terminalDecision = true;
                Debug.LogWarning("[ADOFAIMultiTileEditor] " + status);
            }
        }

        // Harmony prefix for PACL2.CustomFFX.Advanced.OrbitDecoration.ffxOrbitDecoration
        // compiler-generated StartEffect update lambda. Returning false holds PACL2's
        // own update for a few callbacks until its transforms stop moving; once stable,
        // the closure's captured orbit/radius state is refreshed and the original code
        // resumes at the current tween progress.
        private static bool OrbitUpdatePrefix(object __instance)
        {
            if (!enabled || __instance == null) return true;

            try
            {
                object owner = ReadMember(__instance, "<>4__this");
                if (owner == null || !IsMteOrbit(owner)) return true;

                SettleState state = States.GetOrCreateValue(__instance);
                if (state.Applied) return true;
                state.Attempts++;
                if (state.Attempts > MaxSettleCallbacks) return true;

                object movingDecoration = ReadMember(__instance, "dec") ?? FindDecorationLikeField(__instance);
                if (movingDecoration == null) return true;

                Vector2 movingPosition;
                Vector2 centerPosition;
                float movingRotation;
                float amount;
                if (!TryReadVector2(movingDecoration, "pivotPosVec", out movingPosition)
                    || !TryAverageCenter(owner, out centerPosition)
                    || !TryReadFloat(movingDecoration, "rotAngle", out movingRotation)
                    || !TryReadFloat(owner, "_amount", out amount))
                    return true;

                Vector2 relative = movingPosition - centerPosition;
                float radius = relative.magnitude;
                if (!(radius > RadiusEpsilon) || float.IsNaN(radius) || float.IsInfinity(radius))
                    return true;

                float angle = Mathf.Atan2(relative.y, relative.x) * Mathf.Rad2Deg;
                if (float.IsNaN(angle) || float.IsInfinity(angle)) return true;

                if (!state.HasAngle)
                {
                    state.HasAngle = true;
                    state.LastAngle = angle;
                    state.StablePhase = 1;
                    return false;
                }

                if (Mathf.Abs(Mathf.DeltaAngle(state.LastAngle, angle)) > StableAngleTolerance)
                {
                    state.LastAngle = angle;
                    state.StablePhase = 1;
                    return false;
                }

                // Match the old local PACL2 fix: after the first stable reading, wait
                // two more update callbacks before trusting the final transform state.
                if (state.StablePhase < 3)
                {
                    state.StablePhase++;
                    return false;
                }

                if (!TryWriteFloat(__instance, "norm", radius)
                    || !TryWriteFloat(__instance, "srcOrbitDirection", angle)
                    || !TryWriteFloat(__instance, "dstOrbitDirection", angle + amount)
                    || !TryWriteFloat(__instance, "srcDecRotation", movingRotation)
                    || !TryWriteFloat(__instance, "dstDecRotation", movingRotation + amount))
                    return true;

                state.Applied = true;
                return true;
            }
            catch
            {
                // Compatibility must never make a level unplayable merely because a
                // PACL2 internal field changed. Unknown shapes fall back to stock code.
                return true;
            }
        }

        private static Assembly FindPacl2Assembly()
        {
            Assembly[] loaded = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < loaded.Length; i++)
            {
                Assembly assembly = loaded[i];
                try
                {
                    if (string.Equals(assembly.GetName().Name, "PACL2", StringComparison.OrdinalIgnoreCase))
                        return assembly;
                }
                catch { }
            }
            return null;
        }

        private static MethodInfo FindOrbitUpdateMethod(Assembly pacl2)
        {
            Type orbitType = pacl2.GetType("PACL2.CustomFFX.Advanced.OrbitDecoration.ffxOrbitDecoration", false);
            if (orbitType == null) return null;

            Type[] nested = orbitType.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic);
            for (int i = 0; i < nested.Length; i++)
            {
                Type type = nested[i];
                MethodInfo method = type.GetMethod("<StartEffect>b__1", InstanceFlags);
                if (method == null) continue;
                if (type.GetField("norm", InstanceFlags) != null
                    && type.GetField("srcOrbitDirection", InstanceFlags) != null
                    && type.GetField("dstOrbitDirection", InstanceFlags) != null)
                    return method;
            }
            return null;
        }

        private static int GetIlLength(MethodInfo method)
        {
            try
            {
                MethodBody body = method.GetMethodBody();
                byte[] il = body == null ? null : body.GetILAsByteArray();
                return il == null ? -1 : il.Length;
            }
            catch { return -1; }
        }

        private static bool IsMteOrbit(object owner)
        {
            return ContainsMteTag(ReadMember(owner, "_tag"))
                || ContainsMteTag(ReadMember(owner, "_centerTag"));
        }

        private static bool ContainsMteTag(object value)
        {
            if (value == null) return false;
            string text = value as string;
            if (text != null)
                return text.IndexOf("MTE_P", StringComparison.Ordinal) >= 0;

            IEnumerable enumerable = value as IEnumerable;
            if (enumerable != null)
            {
                int count = 0;
                foreach (object item in enumerable)
                {
                    if (ContainsMteTag(item)) return true;
                    if (++count >= 32) break;
                }
                return false;
            }

            try
            {
                return value.ToString().IndexOf("MTE_P", StringComparison.Ordinal) >= 0;
            }
            catch { return false; }
        }

        private static object FindDecorationLikeField(object closure)
        {
            FieldInfo[] fields = closure.GetType().GetFields(InstanceFlags);
            for (int i = 0; i < fields.Length; i++)
            {
                object value;
                try { value = fields[i].GetValue(closure); } catch { continue; }
                if (value == null) continue;
                if (HasMember(value.GetType(), "pivotPosVec") && HasMember(value.GetType(), "rotAngle"))
                    return value;
            }
            return null;
        }

        private static bool TryAverageCenter(object owner, out Vector2 center)
        {
            center = Vector2.zero;
            object value = ReadMember(owner, "_centerDecorations");
            IEnumerable enumerable = value as IEnumerable;
            if (enumerable == null) return false;

            int count = 0;
            Vector2 sum = Vector2.zero;
            foreach (object item in enumerable)
            {
                Vector2 position;
                if (item == null || !TryReadVector2(item, "pivotPosVec", out position)) continue;
                sum += position;
                count++;
            }
            if (count <= 0) return false;
            center = sum / count;
            return true;
        }

        private static bool HasMember(Type type, string name)
        {
            return type.GetField(name, InstanceFlags) != null || type.GetProperty(name, InstanceFlags) != null;
        }

        private static object ReadMember(object target, string name)
        {
            if (target == null) return null;
            Type type = target.GetType();
            try
            {
                FieldInfo field = type.GetField(name, InstanceFlags);
                if (field != null) return field.GetValue(target);
            }
            catch { }
            try
            {
                PropertyInfo property = type.GetProperty(name, InstanceFlags);
                if (property != null && property.GetIndexParameters().Length == 0) return property.GetValue(target, null);
            }
            catch { }
            return null;
        }

        private static bool TryReadVector2(object target, string name, out Vector2 value)
        {
            object raw = ReadMember(target, name);
            if (raw is Vector2)
            {
                value = (Vector2)raw;
                return true;
            }
            if (raw is Vector3)
            {
                Vector3 v = (Vector3)raw;
                value = new Vector2(v.x, v.y);
                return true;
            }
            value = Vector2.zero;
            return false;
        }

        private static bool TryReadFloat(object target, string name, out float value)
        {
            object raw = ReadMember(target, name);
            try
            {
                if (raw == null) { value = 0f; return false; }
                value = Convert.ToSingle(raw, System.Globalization.CultureInfo.InvariantCulture);
                return !float.IsNaN(value) && !float.IsInfinity(value);
            }
            catch
            {
                value = 0f;
                return false;
            }
        }

        private static bool TryWriteFloat(object target, string name, float value)
        {
            if (target == null) return false;
            try
            {
                FieldInfo field = target.GetType().GetField(name, InstanceFlags);
                if (field == null) return false;
                object converted = field.FieldType == typeof(float)
                    ? (object)value
                    : Convert.ChangeType(value, field.FieldType, System.Globalization.CultureInfo.InvariantCulture);
                field.SetValue(target, converted);
                return true;
            }
            catch { return false; }
        }
    }
}
