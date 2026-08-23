using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace KineticNapier.ADOFAIMultiTileEditor
{
    /// <summary>
    /// Temporary research helper for locating the stock ADOFAI editor's native UI tree.
    /// It deliberately avoids compile-time UnityEngine.UI/TMPro dependencies and instead
    /// identifies UI components by runtime type name.
    /// </summary>
    internal static class EditorUiProbe
    {
        private const int MaxLines = 2200;
        private const int MaxDepth = 24;

        internal static string Capture(scnEditor editor)
        {
            var output = new StringBuilder(64 * 1024);
            int lines = 0;

            Action<string> add = delegate(string value)
            {
                if (lines >= MaxLines) return;
                output.AppendLine(value ?? "");
                lines++;
            };

            add("=== ADOFAI Parallel Editor UI probe ===");
            add("Unity: " + Application.unityVersion);
            add("Game: " + Application.version);
            add("Resolution: " + Screen.width + "x" + Screen.height);
            add("");

            if (editor == null)
            {
                add("ADOBase.editor is null.");
                return output.ToString();
            }

            add("scnEditor object: " + SafePath(editor.transform));
            add("scnEditor components: " + ComponentSummary(editor.gameObject));
            add("scnEditor ancestry:");
            Transform ancestor = editor.transform;
            int ancestorDepth = 0;
            while (ancestor != null && ancestorDepth < MaxDepth)
            {
                add("  " + ancestorDepth + ": " + SafePath(ancestor) + "  [" + ComponentSummary(ancestor.gameObject) + "]");
                ancestor = ancestor.parent;
                ancestorDepth++;
            }

            GameObject[] all = Resources.FindObjectsOfTypeAll<GameObject>();
            var canvases = new List<GameObject>();
            var eventSystems = new List<GameObject>();

            for (int i = 0; i < all.Length; i++)
            {
                GameObject go = all[i];
                if (go == null) continue;

                Component[] components = SafeComponents(go);
                if (HasComponent(components, "Canvas")) canvases.Add(go);
                if (HasComponent(components, "EventSystem")) eventSystems.Add(go);
            }

            canvases.Sort(CompareObjects);
            eventSystems.Sort(CompareObjects);

            add("");
            add("EventSystem candidates (" + eventSystems.Count + "):");
            for (int i = 0; i < eventSystems.Count; i++)
                add("  " + DescribeObject(eventSystems[i]));

            add("");
            add("Canvas candidates (" + canvases.Count + "):");
            for (int i = 0; i < canvases.Count; i++)
                add("  C" + i + " " + DescribeObject(canvases[i]));

            add("");
            add("Canvas hierarchies:");
            for (int i = 0; i < canvases.Count && lines < MaxLines; i++)
            {
                GameObject canvas = canvases[i];
                add("");
                add("--- C" + i + " " + DescribeObject(canvas) + " ---");
                DumpTree(canvas.transform, 0, add, ref lines);
            }

            if (lines >= MaxLines)
                output.AppendLine("... output truncated at " + MaxLines + " lines ...");

            return output.ToString();
        }

        private static void DumpTree(Transform transform, int depth, Action<string> add, ref int lines)
        {
            if (transform == null || depth > MaxDepth || lines >= MaxLines) return;

            var indent = new string(' ', depth * 2);
            add(indent + "- " + DescribeObject(transform.gameObject));

            for (int i = 0; i < transform.childCount && lines < MaxLines; i++)
                DumpTree(transform.GetChild(i), depth + 1, add, ref lines);
        }

        private static string DescribeObject(GameObject go)
        {
            if (go == null) return "<null>";

            var sb = new StringBuilder();
            sb.Append(SafePath(go.transform));
            sb.Append("  active=").Append(go.activeSelf ? "self" : "off");
            sb.Append('/').Append(go.activeInHierarchy ? "hier" : "hidden");

            try
            {
                if (go.scene.IsValid()) sb.Append(" scene=").Append(go.scene.name);
                else sb.Append(" scene=<asset>");
            }
            catch
            {
                sb.Append(" scene=?");
            }

            RectTransform rect = go.transform as RectTransform;
            if (rect != null)
            {
                sb.Append(" rect=(");
                sb.Append(rect.rect.width.ToString("0.#")).Append('x').Append(rect.rect.height.ToString("0.#"));
                sb.Append(") anchor=").Append(Vector(rect.anchorMin)).Append("..").Append(Vector(rect.anchorMax));
                sb.Append(" pivot=").Append(Vector(rect.pivot));
            }

            sb.Append(" [").Append(ComponentSummary(go)).Append(']');
            return sb.ToString();
        }

        private static string ComponentSummary(GameObject go)
        {
            Component[] components = SafeComponents(go);
            if (components.Length == 0) return "no-components";

            var names = new List<string>();
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null)
                {
                    names.Add("<missing>");
                    continue;
                }

                Type type = component.GetType();
                string name = type.FullName ?? type.Name;
                string text = TryReadText(component);
                if (!string.IsNullOrEmpty(text)) name += " text=\"" + text + "\"";
                names.Add(name);
            }
            return string.Join(", ", names.ToArray());
        }

        private static string TryReadText(Component component)
        {
            if (component == null) return null;
            string shortName = component.GetType().Name;
            if (shortName.IndexOf("Text", StringComparison.OrdinalIgnoreCase) < 0) return null;

            try
            {
                PropertyInfo property = component.GetType().GetProperty(
                    "text",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property == null || property.PropertyType != typeof(string) || !property.CanRead) return null;
                string value = property.GetValue(component, null) as string;
                if (string.IsNullOrEmpty(value)) return null;
                value = value.Replace("\r", "\\r").Replace("\n", "\\n");
                return value.Length <= 80 ? value : value.Substring(0, 77) + "...";
            }
            catch
            {
                return null;
            }
        }

        private static Component[] SafeComponents(GameObject go)
        {
            if (go == null) return new Component[0];
            try
            {
                return go.GetComponents<Component>() ?? new Component[0];
            }
            catch
            {
                return new Component[0];
            }
        }

        private static bool HasComponent(Component[] components, string shortName)
        {
            if (components == null) return false;
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component != null && string.Equals(component.GetType().Name, shortName, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private static int CompareObjects(GameObject left, GameObject right)
        {
            bool leftActive = left != null && left.activeInHierarchy;
            bool rightActive = right != null && right.activeInHierarchy;
            if (leftActive != rightActive) return leftActive ? -1 : 1;
            return string.Compare(SafePath(left != null ? left.transform : null), SafePath(right != null ? right.transform : null), StringComparison.OrdinalIgnoreCase);
        }

        private static string SafePath(Transform transform)
        {
            if (transform == null) return "<null>";
            var parts = new List<string>();
            Transform current = transform;
            int guard = 0;
            while (current != null && guard++ < 64)
            {
                parts.Add(current.name ?? "<unnamed>");
                current = current.parent;
            }
            parts.Reverse();
            return "/" + string.Join("/", parts.ToArray());
        }

        private static string Vector(Vector2 value)
        {
            return "(" + value.x.ToString("0.###") + "," + value.y.ToString("0.###") + ")";
        }
    }
}
