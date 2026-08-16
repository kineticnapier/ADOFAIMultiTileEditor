using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

namespace KineticNapier.ADOFAIMultiTileEditor
{
    internal sealed class OrbitGenerationResult
    {
        internal int Created;
        internal string Diagnostic;
    }

    internal static class OrbitTemplateGenerator
    {
        private static readonly string[] EventListNames = { "actions", "events", "levelEvents" };
        private static readonly string[] EventTypeNames = { "eventType", "type" };
        private static readonly string[] FloorNames = { "floor", "floorIndex" };

        internal static string ProbeTemplate(scnEditor editor)
        {
            object template;
            IList list;
            string listName;
            if (!TryFindOrbitTemplate(editor, out template, out list, out listName))
                return "No Orbit Decoration template found in active chart.";

            return "Template: " + template.GetType().Name + " in levelData." + listName;
        }

        internal static OrbitGenerationResult Generate(scnEditor editor, IList<TrackSlot> tracks, int targetFloor, float duration, string ease, bool lockRotation)
        {
            if (editor == null || editor.levelData == null) throw new InvalidOperationException("Editor is not ready.");
            if (tracks == null || tracks.Count == 0) throw new InvalidOperationException("No tracks are stored.");
            if (targetFloor < 0) throw new ArgumentOutOfRangeException("targetFloor");

            object template;
            IList eventList;
            string listName;
            if (!TryFindOrbitTemplate(editor, out template, out eventList, out listName))
                throw new InvalidOperationException("No Orbit Decoration event exists in the active chart. Add one dummy Orbit Decoration first; v0.3 clones it as a PACL2-compatible template.");

            int created = 0;
            var pending = new List<object>();
            for (int i = 0; i < tracks.Count; i++)
            {
                TrackSlot track = tracks[i];
                if (!track.TagsReady)
                    throw new InvalidOperationException("Track '" + track.Name + "' needs distinct Planet A / Planet B tags.");
                AngleSample sample = track.CurrentAngle;
                if (!sample.Valid)
                    throw new InvalidOperationException("Track '" + track.Name + "' has no valid angle at floor " + track.CursorFloor + ".");

                object clone = CloneEvent(template);
                if (clone == null) throw new InvalidOperationException("Could not clone Orbit Decoration template.");

                SetValue(clone, FloorNames, targetFloor);
                SetValue(clone, EventTypeNames, ReadValue(template, EventTypeNames) ?? "OrbitDecoration");
                SetValue(clone, new[] { "duration" }, duration);
                SetValue(clone, new[] { "tag", "targetTag" }, track.MovingTag.Trim());
                SetValue(clone, new[] { "centerTag" }, track.CenterTag.Trim());
                SetValue(clone, new[] { "amount" }, sample.Degrees);
                SetValue(clone, new[] { "ease" }, ease);
                SetValue(clone, new[] { "lockRotation" }, lockRotation);
                pending.Add(clone);
            }

            for (int i = 0; i < pending.Count; i++)
            {
                eventList.Add(pending[i]);
                created++;
            }

            editor.RemakePath(true, true);
            editor.UpdateDecorationObjects();

            return new OrbitGenerationResult
            {
                Created = created,
                Diagnostic = "Created " + created + " Orbit Decoration event(s) at floor " + targetFloor + " via levelData." + listName + "."
            };
        }

        private static bool TryFindOrbitTemplate(scnEditor editor, out object template, out IList eventList, out string listName)
        {
            template = null;
            eventList = null;
            listName = null;
            if (editor == null || editor.levelData == null) return false;

            object levelData = editor.levelData;
            Type type = levelData.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            for (int n = 0; n < EventListNames.Length; n++)
            {
                object value = ReadMember(levelData, EventListNames[n]);
                IList list = value as IList;
                if (list == null) continue;
                object found = FindOrbitInList(list);
                if (found != null)
                {
                    template = found;
                    eventList = list;
                    listName = EventListNames[n];
                    return true;
                }
            }

            foreach (FieldInfo field in type.GetFields(flags))
            {
                IList list = SafeGet(delegate { return field.GetValue(levelData) as IList; });
                if (list == null) continue;
                object found = FindOrbitInList(list);
                if (found != null)
                {
                    template = found;
                    eventList = list;
                    listName = field.Name;
                    return true;
                }
            }
            foreach (PropertyInfo property in type.GetProperties(flags))
            {
                if (property.GetIndexParameters().Length != 0) continue;
                IList list = SafeGet(delegate { return property.GetValue(levelData, null) as IList; });
                if (list == null) continue;
                object found = FindOrbitInList(list);
                if (found != null)
                {
                    template = found;
                    eventList = list;
                    listName = property.Name;
                    return true;
                }
            }

            return false;
        }

        private static object FindOrbitInList(IList list)
        {
            for (int i = list.Count - 1; i >= 0; i--)
            {
                object item = list[i];
                if (item == null) continue;
                object eventType = ReadValue(item, EventTypeNames);
                string text = Convert.ToString(eventType, CultureInfo.InvariantCulture) ?? "";
                if (text.IndexOf("Orbit", StringComparison.OrdinalIgnoreCase) >= 0
                    && text.IndexOf("Decoration", StringComparison.OrdinalIgnoreCase) >= 0)
                    return item;
            }
            return null;
        }

        private static object CloneEvent(object source)
        {
            if (source == null) return null;
            Type type = source.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            string[] methods = { "Copy", "Clone", "DeepCopy" };
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo m = type.GetMethod(methods[i], flags, null, Type.EmptyTypes, null);
                if (m == null) continue;
                try
                {
                    object result = m.Invoke(source, null);
                    if (result != null) return result;
                }
                catch { }
            }

            MethodInfo memberwise = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);
            object clone = memberwise.Invoke(source, null);
            CloneKnownPropertyBags(source, clone);
            return clone;
        }

        private static void CloneKnownPropertyBags(object source, object clone)
        {
            string[] bags = { "data", "values", "properties", "eventData" };
            for (int i = 0; i < bags.Length; i++)
            {
                object original = ReadMember(source, bags[i]);
                IDictionary dict = original as IDictionary;
                if (dict == null) continue;
                object copy = null;
                try
                {
                    copy = Activator.CreateInstance(original.GetType());
                    IDictionary copyDict = copy as IDictionary;
                    if (copyDict != null)
                    {
                        foreach (DictionaryEntry entry in dict) copyDict[entry.Key] = entry.Value;
                        SetMember(clone, bags[i], copy);
                    }
                }
                catch { }
            }
        }

        private static object ReadValue(object target, string[] names)
        {
            for (int i = 0; i < names.Length; i++)
            {
                object direct = ReadMember(target, names[i]);
                if (direct != null) return direct;
                object indexed = ReadIndexer(target, names[i]);
                if (indexed != null) return indexed;
                object bag = ReadPropertyBag(target, names[i]);
                if (bag != null) return bag;
            }
            return null;
        }

        private static void SetValue(object target, string[] names, object value)
        {
            for (int i = 0; i < names.Length; i++)
            {
                if (SetMember(target, names[i], value)) return;
                if (SetIndexer(target, names[i], value)) return;
                if (SetPropertyBag(target, names[i], value)) return;
            }
            throw new InvalidOperationException("LevelEvent has no writable property for: " + string.Join(" / ", names));
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

        private static bool SetMember(object target, string name, object value)
        {
            if (target == null) return false;
            Type type = target.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            try
            {
                PropertyInfo p = type.GetProperty(name, flags);
                if (p != null && p.CanWrite && p.GetIndexParameters().Length == 0)
                {
                    p.SetValue(target, ConvertFor(value, p.PropertyType), null);
                    return true;
                }
            }
            catch { }
            try
            {
                FieldInfo f = type.GetField(name, flags);
                if (f != null)
                {
                    f.SetValue(target, ConvertFor(value, f.FieldType));
                    return true;
                }
            }
            catch { }
            return false;
        }

        private static object ReadIndexer(object target, string key)
        {
            if (target == null) return null;
            Type type = target.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (PropertyInfo p in type.GetProperties(flags))
            {
                ParameterInfo[] pars = p.GetIndexParameters();
                if (pars.Length != 1 || pars[0].ParameterType != typeof(string) || !p.CanRead) continue;
                try { return p.GetValue(target, new object[] { key }); } catch { }
            }
            return null;
        }

        private static bool SetIndexer(object target, string key, object value)
        {
            if (target == null) return false;
            Type type = target.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (PropertyInfo p in type.GetProperties(flags))
            {
                ParameterInfo[] pars = p.GetIndexParameters();
                if (pars.Length != 1 || pars[0].ParameterType != typeof(string) || !p.CanWrite) continue;
                try
                {
                    p.SetValue(target, ConvertFor(value, p.PropertyType), new object[] { key });
                    return true;
                }
                catch { }
            }
            return false;
        }

        private static object ReadPropertyBag(object target, string key)
        {
            string[] bags = { "data", "values", "properties", "eventData" };
            for (int i = 0; i < bags.Length; i++)
            {
                IDictionary dict = ReadMember(target, bags[i]) as IDictionary;
                if (dict == null || !dict.Contains(key)) continue;
                return dict[key];
            }
            return null;
        }

        private static bool SetPropertyBag(object target, string key, object value)
        {
            string[] bags = { "data", "values", "properties", "eventData" };
            for (int i = 0; i < bags.Length; i++)
            {
                IDictionary dict = ReadMember(target, bags[i]) as IDictionary;
                if (dict == null) continue;
                try
                {
                    object old = dict.Contains(key) ? dict[key] : null;
                    dict[key] = old == null ? value : ConvertFor(value, old.GetType());
                    return true;
                }
                catch { }
            }
            return false;
        }

        private static object ConvertFor(object value, Type targetType)
        {
            if (value == null) return null;
            Type actualTarget = Nullable.GetUnderlyingType(targetType) ?? targetType;
            if (actualTarget.IsInstanceOfType(value)) return value;
            if (actualTarget.IsEnum)
            {
                string s = Convert.ToString(value, CultureInfo.InvariantCulture);
                try { return Enum.Parse(actualTarget, s, true); } catch { return Activator.CreateInstance(actualTarget); }
            }
            if (actualTarget == typeof(string)) return Convert.ToString(value, CultureInfo.InvariantCulture);
            return Convert.ChangeType(value, actualTarget, CultureInfo.InvariantCulture);
        }

        private static T SafeGet<T>(Func<T> getter) where T : class
        {
            try { return getter(); } catch { return null; }
        }
    }
}
