using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace KineticNapier.ADOFAIMultiTileEditor
{
    /// <summary>
    /// Temporary research helper for locating the stock ADOFAI editor UI and viewport.
    /// The probe intentionally avoids compile-time UnityEngine.UI/TMPro dependencies.
    /// </summary>
    internal static class EditorUiProbe
    {
        private const int MaxLines = 1200;
        private const int ShallowDepth = 2;

        internal static string Capture(scnEditor editor)
        {
            var sink = new LineSink(MaxLines);

            sink.Add("=== ADOFAI Parallel Editor layout probe v2 ===");
            sink.Add("Unity: " + Application.unityVersion);
            sink.Add("Game: " + Application.version);
            sink.Add("Resolution: " + Screen.width + "x" + Screen.height);
            sink.Add("");

            if (editor == null)
            {
                sink.Add("ADOBase.editor is null.");
                return sink.ToString();
            }

            sink.Add("scnEditor root:");
            sink.Add("  " + DescribeObject(editor.gameObject));

            sink.Add("");
            sink.Add("Editor root direct children (" + editor.transform.childCount + "):");
            for (int i = 0; i < editor.transform.childCount; i++)
                sink.Add("  #" + i + " " + DescribeObject(editor.transform.GetChild(i).gameObject));

            sink.Add("");
            sink.Add("Editor root shallow hierarchy (depth <= " + ShallowDepth + "):");
            DumpTree(editor.transform, 0, ShallowDepth, sink);

            sink.Add("");
            sink.Add("scnEditor public UI/object references:");
            DumpEditorReferences(editor, sink);

            sink.Add("");
            sink.Add("Active Canvas objects in scnEditor scene:");
            DumpEditorCanvases(sink);

            sink.Add("");
            sink.Add("Camera candidates:");
            DumpCameras(sink);

            sink.Add("");
            sink.Add("EventSystem candidates:");
            DumpEventSystems(sink);

            if (sink.Full) sink.AppendRaw("... output truncated at " + MaxLines + " lines ...\n");
            return sink.ToString();
        }

        private static void DumpTree(Transform transform, int depth, int maxDepth, LineSink sink)
        {
            if (transform == null || depth > maxDepth || sink.Full) return;

            string indent = new string(' ', depth * 2);
            sink.Add(indent + "- " + DescribeObject(transform.gameObject));
            if (depth >= maxDepth) return;

            for (int i = 0; i < transform.childCount && !sink.Full; i++)
                DumpTree(transform.GetChild(i), depth + 1, maxDepth, sink);
        }

        private static void DumpEditorReferences(scnEditor editor, LineSink sink)
        {
            FieldInfo[] fields = editor.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public);
            Array.Sort(fields, delegate(FieldInfo left, FieldInfo right)
            {
                return string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
            });

            int emitted = 0;
            for (int i = 0; i < fields.Length && !sink.Full; i++)
            {
                FieldInfo field = fields[i];
                if (!LooksLikeUiReference(field.Name)) continue;

                object value;
                try
                {
                    value = field.GetValue(editor);
                }
                catch
                {
                    continue;
                }

                GameObject go = value as GameObject;
                Component component = value as Component;
                if (go == null && component != null) go = component.gameObject;
                if (go == null) continue;

                string runtimeType = value != null ? value.GetType().FullName : field.FieldType.FullName;
                sink.Add("  " + field.Name + " : " + runtimeType + " -> " + DescribeObject(go));
                emitted++;
            }

            sink.Add("  emitted=" + emitted + " public reference(s)");
        }

        private static bool LooksLikeUiReference(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string lower = name.ToLowerInvariant();
            return lower.Contains("panel")
                || lower.Contains("canvas")
                || lower.Contains("button")
                || lower.Contains("bar")
                || lower.Contains("container")
                || lower.Contains("inspector")
                || lower.Contains("toolbar")
                || lower.Contains("picker")
                || lower.Contains("find")
                || lower.Contains("file")
                || lower.Contains("levelstring")
                || lower.Contains("notification")
                || lower.Contains("events");
        }

        private static void DumpEditorCanvases(LineSink sink)
        {
            Component[] components = Resources.FindObjectsOfTypeAll<Component>();
            var canvases = new List<Component>();

            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null || component.GetType().Name != "Canvas") continue;

                GameObject go = component.gameObject;
                if (go == null || !go.activeInHierarchy) continue;
                try
                {
                    if (!go.scene.IsValid() || go.scene.name != "scnEditor") continue;
                }
                catch
                {
                    continue;
                }
                canvases.Add(component);
            }

            canvases.Sort(delegate(Component left, Component right)
            {
                return string.Compare(SafePath(left.transform), SafePath(right.transform), StringComparison.OrdinalIgnoreCase);
            });

            for (int i = 0; i < canvases.Count && !sink.Full; i++)
                sink.Add("  C" + i + " " + DescribeObject(canvases[i].gameObject));
        }

        private static void DumpCameras(LineSink sink)
        {
            Camera[] cameras = Resources.FindObjectsOfTypeAll<Camera>();
            Array.Sort(cameras, delegate(Camera left, Camera right)
            {
                return string.Compare(SafePath(left != null ? left.transform : null), SafePath(right != null ? right.transform : null), StringComparison.OrdinalIgnoreCase);
            });

            for (int i = 0; i < cameras.Length && !sink.Full; i++)
            {
                Camera camera = cameras[i];
                if (camera == null || camera.gameObject == null) continue;

                bool sceneValid = false;
                string sceneName = "?";
                try
                {
                    sceneValid = camera.gameObject.scene.IsValid();
                    sceneName = sceneValid ? camera.gameObject.scene.name : "<asset>";
                }
                catch
                {
                }

                // Assets and disabled prefab cameras are noise. Keep live cameras and anything in scnEditor/scnGame.
                if (!camera.gameObject.activeInHierarchy && sceneName != "scnEditor" && sceneName != "scnGame") continue;

                Rect r = camera.rect;
                Rect p = camera.pixelRect;
                string target = camera.targetTexture == null
                    ? "screen"
                    : camera.targetTexture.name + "(" + camera.targetTexture.width + "x" + camera.targetTexture.height + ")";

                sink.Add(
                    "  " + SafePath(camera.transform)
                    + " scene=" + sceneName
                    + " active=" + camera.gameObject.activeInHierarchy
                    + " enabled=" + camera.enabled
                    + " depth=" + camera.depth.ToString("0.###")
                    + " rect=(" + r.x.ToString("0.###") + "," + r.y.ToString("0.###") + "," + r.width.ToString("0.###") + "," + r.height.ToString("0.###") + ")"
                    + " pixel=(" + p.x.ToString("0.#") + "," + p.y.ToString("0.#") + "," + p.width.ToString("0.#") + "x" + p.height.ToString("0.#") + ")"
                    + " ortho=" + camera.orthographic
                    + " size=" + camera.orthographicSize.ToString("0.###")
                    + " target=" + target
                    + " mask=0x" + camera.cullingMask.ToString("X8"));
            }
        }

        private static void DumpEventSystems(LineSink sink)
        {
            GameObject[] all = Resources.FindObjectsOfTypeAll<GameObject>();
            for (int i = 0; i < all.Length && !sink.Full; i++)
            {
                GameObject go = all[i];
                if (go == null || !go.activeInHierarchy) continue;
                Component[] components = SafeComponents(go);
                if (HasComponent(components, "EventSystem")) sink.Add("  " + DescribeObject(go));
            }
        }

        private static string DescribeObject(GameObject go)
        {
            if (go == null) return "<null>";

            var sb = new StringBuilder();
            sb.Append(SafePath(go.transform));
            sb.Append(" active=").Append(go.activeSelf ? "self" : "off");
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
                sb.Append(" anchored=").Append(Vector(rect.anchoredPosition));
                sb.Append(" sizeDelta=").Append(Vector(rect.sizeDelta));
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
