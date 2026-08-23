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
            var sink = new LineSink(MaxLines);

            sink.Add("=== ADOFAI Parallel Editor UI probe ===");
            sink.Add("Unity: " + Application.unityVersion);
            sink.Add("Game: " + Application.version);
            sink.Add("Resolution: " + Screen.width + "x" + Screen.height);
            sink.Add("");

            if (editor == null)
            {
                sink.Add("ADOBase.editor is null.");
                return sink.ToString();
            }

            sink.Add("scnEditor object: " + SafePath(editor.transform));
            sink.Add("scnEditor components: " + ComponentSummary(editor.gameObject));
            sink.Add("scnEditor ancestry:");
            Transform ancestor = editor.transform;
            int ancestorDepth = 0;
            while (ancestor != null && ancestorDepth < MaxDepth)
            {
                sink.Add("  " + ancestorDepth + ": " + SafePath(ancestor) + "  [" + ComponentSummary(ancestor.gameObject) + "]");
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

            sink.Add("");
            sink.Add("EventSystem candidates (" + eventSystems.Count + "):");
            for (int i = 0; i < eventSystems.Count; i++)
                sink.Add("  " + DescribeObject(eventSystems[i]));

            sink.Add("");
            sink.Add("Canvas candidates (" + canvases.Count + "):");
            for (int i = 0; i < canvases.Count; i++)
                sink.Add("  C" + i + " " + DescribeObject(canvases[i]));

            sink.Add("");
            sink.Add("Canvas hierarchies:");
            for (int i = 0; i < canvases.Count && !sink.Full; i++)
            {
                GameObject canvas = canvases[i];
                sink.Add("");
                sink.Add("--- C" + i + " " + DescribeObject(canvas) + " ---");
                DumpTree(canvas.transform, 0, sink);
            }

            if (sink.Full) sink.AppendRaw("... output truncated at " + MaxLines + " lines ...\n");
            return sink.ToString();
        }

        private static void DumpTree(Transform transform, int depth, LineSink sink)
        {
            if (transform == null || depth > MaxDepth || sink.Full) return;

            string indent = new string(' ', depth * 2);
            sink.Add(indent + "- " + DescribeObject(transform.gameObject));

            for (int i = 0; i < transform.childCount && !sink.Full; i++)
                DumpTree(transform.GetChild(i), depth + 1, sink);
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
            return string.Compare(
                SafePath(left != null ? left.transform : null),
                SafePath(right != null ? right.transform : null),
                StringComparison.OrdinalIgnoreCase);
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

        private sealed class LineSink
        {
            private readonly int maxLines;
            private readonly StringBuilder builder = new StringBuilder(64 * 1024);
            private int lines;

            internal LineSink(int maxLines)
            {
                this.maxLines = Math.Max(1, maxLines);
            }

            internal bool Full { get { return lines >= maxLines; } }

            internal void Add(string value)
            {
                if (Full) return;
                builder.AppendLine(value ?? "");
                lines++;
            }

            internal void AppendRaw(string value)
            {
                builder.Append(value ?? "");
            }

            public override string ToString()
            {
                return builder.ToString();
            }
        }
    }
}
