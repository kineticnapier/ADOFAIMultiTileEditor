using System;
using System.Collections.Generic;
using ADOFAI.EditorToolkit.Game;
using KineticNapier.ADOFAIParallelEditor;
using UnityEngine;

namespace KineticNapier.ADOFAIMultiTileEditor
{
    internal static class NativeParallelWorkspace
    {
        private const string HostName = "ADOFAI Parallel Editor Root";
        private const float ToolbarHeight = 38f;
        private const float StatusHeight = 22f;
        private const float TabHeight = 38f;
        private const float Gap = 4f;

        private static readonly ParallelWorkspaceModel model = new ParallelWorkspaceModel();
        private static readonly MultiTileParallelDocumentProvider provider = new MultiTileParallelDocumentProvider();
        private static readonly Dictionary<Camera, Rect> originalCameraRects = new Dictionary<Camera, Rect>();

        private static scnEditor mountedEditor;
        private static RectTransform host;
        private static RectTransform frame;
        private static RectTransform focusedContent;
        private static string lastSignature;
        private static string status = "Native workspace attached.";
        private static bool visible = true;

        internal static void Update(scnEditor editor)
        {
            if (editor == null)
            {
                SetVisible(false);
                mountedEditor = null;
                host = null;
                frame = null;
                focusedContent = null;
                return;
            }

            if (!ReferenceEquals(mountedEditor, editor))
            {
                RestoreCameraRects();
                mountedEditor = editor;
                host = null;
                frame = null;
                focusedContent = null;
                lastSignature = null;
            }

            TrackStore store = TrackStore.Current;
            if (store == null) return;

            visible = true;
            provider.Bind(editor, store);
            model.SyncDocuments(provider.Documents);

            IMultiEditorDocument stockActive = provider.ActiveDocument;
            if (stockActive != null && model.FocusedGroup != null
                && !string.Equals(model.FocusedGroup.ActiveDocumentId, stockActive.Id, StringComparison.Ordinal))
            {
                model.OpenInGroup(model.FocusedGroup, stockActive.Id);
            }

            EnsureHost();
            host.gameObject.SetActive(true);

            string signature = BuildSignature();
            if (!string.Equals(lastSignature, signature, StringComparison.Ordinal))
            {
                Rebuild();
                lastSignature = BuildSignature();
            }

            ApplyCameraViewport();
        }

        internal static void SetVisible(bool value)
        {
            visible = value;
            if (host != null) host.gameObject.SetActive(value);
            if (!value) RestoreCameraRects();
        }

        private static void EnsureHost()
        {
            if (host != null) return;
            host = ADOFAIEditorUiHost.GetOrCreateViewportRoot(HostName);
        }

        private static void Rebuild()
        {
            EnsureHost();
            if (frame != null)
            {
                frame.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(frame.gameObject);
                frame = null;
            }

            frame = CreateRect(host, "WorkspaceFrame");
            Stretch(frame, Vector2.zero, Vector2.zero);

            RectTransform toolbar = CreatePanel(frame, "WorkspaceToolbar", new Color(0.12f, 0.13f, 0.16f, 0.96f), false, null);
            AnchorTop(toolbar, ToolbarHeight);

            CreateButton(toolbar, "SingleLayout", "1", 6f, 4f, 42f, 30f, delegate
            {
                model.SetSingleGroup();
                status = "Single editor group.";
                Invalidate();
            }, !model.IsSplit);

            CreateButton(toolbar, "SplitColumns", "1 | 2", 52f, 4f, 62f, 30f, delegate
            {
                model.SetTwoColumns();
                status = "Two editor groups.";
                Invalidate();
            }, model.IsSplit);

            CreateButton(toolbar, "StoreCurrent", "+ Store current", 124f, 4f, 132f, 30f, delegate
            {
                Run(delegate
                {
                    MultiTileParallelDocument document = provider.StoreCurrent("");
                    if (document != null)
                    {
                        model.SyncDocuments(provider.Documents);
                        model.OpenInGroup(model.FocusedGroup, document.Id);
                        status = "Stored " + document.Title + ".";
                    }
                });
            }, false);

            CreateButton(toolbar, "SaveCurrent", "Save snapshot", 262f, 4f, 116f, 30f, delegate
            {
                Run(delegate
                {
                    IMultiEditorDocument document = provider.ActiveDocument;
                    if (document != null)
                    {
                        provider.Save(document);
                        status = "Saved " + document.Title + ".";
                    }
                });
            }, false);

            CreateLabel(toolbar, "Title", "ADOFAIParallelEditor", 392f, 7f, 250f, 26f, 17f);

            RectTransform body = CreateRect(frame, "SplitRoot");
            Stretch(body, new Vector2(0f, StatusHeight), new Vector2(0f, -ToolbarHeight));

            focusedContent = null;
            if (model.Root is SplitNode)
            {
                SplitNode split = (SplitNode)model.Root;
                float ratio = Mathf.Clamp(split.Ratio, 0.2f, 0.8f);
                RectTransform first = CreateRect(body, "Group1Host");
                first.anchorMin = Vector2.zero;
                first.anchorMax = new Vector2(ratio, 1f);
                first.offsetMin = Vector2.zero;
                first.offsetMax = new Vector2(-Gap * 0.5f, 0f);

                RectTransform second = CreateRect(body, "Group2Host");
                second.anchorMin = new Vector2(ratio, 0f);
                second.anchorMax = Vector2.one;
                second.offsetMin = new Vector2(Gap * 0.5f, 0f);
                second.offsetMax = Vector2.zero;

                BuildGroup(first, model.FirstGroup, 1);
                BuildGroup(second, model.SecondGroup, 2);
            }
            else
            {
                RectTransform only = CreateRect(body, "SingleGroupHost");
                Stretch(only, Vector2.zero, Vector2.zero);
                BuildGroup(only, model.FocusedGroup ?? model.FirstGroup, 1);
            }

            RectTransform statusBar = CreatePanel(frame, "StatusBar", new Color(0.10f, 0.11f, 0.14f, 0.94f), false, null);
            AnchorBottom(statusBar, StatusHeight);
            CreateLabel(statusBar, "StatusText", status, 8f, 1f, 760f, 20f, 13f);
        }

        private static void BuildGroup(RectTransform parent, EditorGroupNode group, int number)
        {
            bool focused = ReferenceEquals(model.FocusedGroup, group);
            Color tabColor = focused
                ? new Color(0.18f, 0.21f, 0.27f, 0.98f)
                : new Color(0.12f, 0.13f, 0.16f, 0.95f);

            RectTransform tabBar = CreatePanel(parent, "TabStrip", tabColor, false, null);
            AnchorTop(tabBar, TabHeight);

            CreateButton(tabBar, "GroupFocus", focused ? "> G" + number : "G" + number, 4f, 4f, 48f, 30f, delegate
            {
                FocusGroup(group);
            }, focused);

            float x = 56f;
            int rendered = 0;
            for (int i = 0; i < provider.Documents.Count; i++)
            {
                IMultiEditorDocument document = provider.Documents[i];
                if (document == null) continue;

                bool selected = string.Equals(group.ActiveDocumentId, document.Id, StringComparison.Ordinal);
                string label = ShortTitle(document.Title);
                float width = Mathf.Clamp(52f + label.Length * 6f, 78f, 126f);
                if (x + width > parent.rect.width - 4f && rendered > 0)
                {
                    CreateLabel(tabBar, "MoreTabs", "...", x, 7f, 28f, 24f, 15f);
                    break;
                }

                IMultiEditorDocument captured = document;
                CreateButton(tabBar, "Tab_" + document.Id, label, x, 4f, width, 30f, delegate
                {
                    ActivateInGroup(group, captured);
                }, selected && focused);
                x += width + 3f;
                rendered++;
            }

            RectTransform content = CreateRect(parent, "DocumentView");
            Stretch(content, Vector2.zero, new Vector2(0f, -TabHeight));

            MultiTileParallelDocument active = provider.FindById(group.ActiveDocumentId);
            if (focused)
            {
                focusedContent = content;
                string title = active != null ? active.Title : "No document";
                CreateLabel(content, "LiveBadge", "LIVE  " + title, 8f, 6f, 280f, 24f, 14f);
            }
            else
            {
                RectTransform shade = CreatePanel(content, "SnapshotPlaceholder", new Color(0.08f, 0.09f, 0.12f, 0.96f), false, null);
                Stretch(shade, new Vector2(2f, 2f), new Vector2(-2f, -2f));
                string title = active != null ? active.Title : "No document";
                CreateLabel(shade, "SnapshotTitle", "SNAPSHOT  " + title, 12f, 12f, 330f, 26f, 16f);
                if (active != null)
                {
                    int floors = active.Track.PreviewPositions != null && active.Track.PreviewPositions.Count > 0
                        ? active.Track.PreviewPositions.Count
                        : (active.Track.Angles != null ? active.Track.Angles.Count : 0);
                    CreateLabel(shade, "SnapshotInfo",
                        floors + " floors   Start F" + active.Track.RegionStartFloor + "   Cursor F" + active.Track.CursorFloor,
                        12f, 42f, 390f, 24f, 13f);
                }
            }
        }

        private static void FocusGroup(EditorGroupNode group)
        {
            if (group == null) return;
            model.Focus(group);
            MultiTileParallelDocument document = provider.FindById(group.ActiveDocumentId);
            if (document != null)
            {
                Run(delegate { provider.Activate(document); });
                status = "Editing " + document.Title + ".";
            }
            else
            {
                status = "Focused " + group.Id + ".";
            }
            Invalidate();
        }

        private static void ActivateInGroup(EditorGroupNode group, IMultiEditorDocument document)
        {
            if (group == null || document == null) return;
            Run(delegate
            {
                model.OpenInGroup(group, document.Id);
                provider.Activate(document);
                status = "Editing " + document.Title + " in " + group.Id + ".";
            });
        }

        private static void ApplyCameraViewport()
        {
            if (!visible || focusedContent == null) return;

            Rect normalized = ToCanvasNormalizedRect(focusedContent);
            normalized.x = Mathf.Clamp01(normalized.x);
            normalized.y = Mathf.Clamp01(normalized.y);
            normalized.width = Mathf.Clamp(normalized.width, 0.01f, 1f - normalized.x);
            normalized.height = Mathf.Clamp(normalized.height, 0.01f, 1f - normalized.y);

            Camera[] cameras = Resources.FindObjectsOfTypeAll<Camera>();
            for (int i = 0; i < cameras.Length; i++)
            {
                Camera camera = cameras[i];
                if (!IsViewportCamera(camera)) continue;
                if (!originalCameraRects.ContainsKey(camera)) originalCameraRects.Add(camera, camera.rect);
                camera.rect = normalized;
            }
        }

        private static bool IsViewportCamera(Camera camera)
        {
            if (camera == null || camera.targetTexture != null) return false;
            string path = PathOf(camera.transform);
            return string.Equals(path, "/CamParent/Camera", StringComparison.Ordinal)
                || string.Equals(path, "/CamParent/Camera/OverlayCam", StringComparison.Ordinal);
        }

        private static Rect ToCanvasNormalizedRect(RectTransform rect)
        {
            RectTransform root = ADOFAIEditorUiHost.Root;
            Bounds bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(root, rect);
            Rect rootRect = root.rect;
            if (rootRect.width <= 0f || rootRect.height <= 0f) return new Rect(0f, 0f, 1f, 1f);

            return new Rect(
                (bounds.min.x - rootRect.xMin) / rootRect.width,
                (bounds.min.y - rootRect.yMin) / rootRect.height,
                bounds.size.x / rootRect.width,
                bounds.size.y / rootRect.height);
        }

        private static void RestoreCameraRects()
        {
            foreach (KeyValuePair<Camera, Rect> pair in originalCameraRects)
                if (pair.Key != null) pair.Key.rect = pair.Value;
            originalCameraRects.Clear();
        }

        private static string BuildSignature()
        {
            var value = new System.Text.StringBuilder();
            value.Append(model.IsSplit ? "split" : "single");
            value.Append('|').Append(model.FocusedGroup != null ? model.FocusedGroup.Id : "none");
            value.Append('|').Append(model.FirstGroup.ActiveDocumentId ?? "-");
            value.Append('|').Append(model.SecondGroup.ActiveDocumentId ?? "-");
            value.Append('|').Append(status ?? "");
            value.Append('|').Append(provider.Documents.Count);
            for (int i = 0; i < provider.Documents.Count; i++)
            {
                IMultiEditorDocument document = provider.Documents[i];
                if (document != null) value.Append('|').Append(document.Id).Append(':').Append(document.Title);
            }
            IMultiEditorDocument active = provider.ActiveDocument;
            value.Append("|active=").Append(active != null ? active.Id : "-");
            return value.ToString();
        }

        private static void Run(Action action)
        {
            try
            {
                action();
                provider.Sync();
                model.SyncDocuments(provider.Documents);
            }
            catch (Exception ex)
            {
                status = "ERROR: " + ex.Message;
                Debug.LogError("[ADOFAIParallelEditor] " + ex);
            }
            Invalidate();
        }

        private static void Invalidate()
        {
            lastSignature = null;
        }

        private static string ShortTitle(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "Untitled";
            value = value.Trim();
            return value.Length <= 15 ? value : value.Substring(0, 14) + "...";
        }

        private static RectTransform CreateRect(Transform parent, string name)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            RectTransform rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        private static RectTransform CreatePanel(Transform parent, string name, Color color, bool interactive, Action callback)
        {
            RectTransform rect = CreateRect(parent, name);
            GameObject go = rect.gameObject;

            Type imageType = FindLoadedType("UnityEngine.UI.Image");
            if (imageType == null) throw new InvalidOperationException("UnityEngine.UI.Image is not available.");
            Component image = go.AddComponent(imageType);
            SetProperty(image, "color", color);
            SetProperty(image, "raycastTarget", interactive);

            if (interactive && callback != null)
            {
                Type buttonType = FindLoadedType("UnityEngine.UI.Button");
                if (buttonType == null) throw new InvalidOperationException("UnityEngine.UI.Button is not available.");
                Component button = go.AddComponent(buttonType);
                SetProperty(button, "targetGraphic", image);
                BindButton(go, callback);
            }

            return rect;
        }

        private static RectTransform CreateButton(
            Transform parent,
            string name,
            string text,
            float x,
            float y,
            float width,
            float height,
            Action callback,
            bool selected)
        {
            Color color = selected
                ? new Color(0.28f, 0.34f, 0.46f, 0.98f)
                : new Color(0.19f, 0.20f, 0.24f, 0.98f);

            RectTransform rect = CreatePanel(parent, name, color, true, callback);
            SetTopLeft(rect, x, y, width, height);
            CreateLabel(rect, "Text", text, 0f, 0f, width, height, 14f);
            return rect;
        }

        private static RectTransform CreateLabel(
            Transform parent,
            string name,
            string text,
            float x,
            float y,
            float width,
            float height,
            float fontSize)
        {
            GameObject label = ADOFAIEditorUiHost.CloneStockObject("findFloorPanelTitle", parent, name);
            RectTransform rect = label.transform as RectTransform;
            if (rect == null) throw new InvalidOperationException("Stock title template is not a RectTransform.");

            RemoveComponentByName(label, "UnityEngine.UI.ContentSizeFitter");
            RemoveComponentByName(label, "scrShortcutText");
            RemoveComponentByName(label, "scrTextChanger");
            SetTopLeft(rect, x, y, width, height);
            SetText(label, text);
            SetFontSize(label, fontSize);
            SetRaycast(label, false);
            return rect;
        }

        private static void BindButton(GameObject root, Action callback)
        {
            Component button = FindComponent(root, "UnityEngine.UI.Button");
            if (button == null) throw new InvalidOperationException("Button object has no UnityEngine.UI.Button component.");

            System.Reflection.PropertyInfo onClickProperty = button.GetType().GetProperty("onClick");
            object onClick = onClickProperty != null ? onClickProperty.GetValue(button, null) : null;
            if (onClick == null) throw new InvalidOperationException("Could not access Button.onClick.");

            System.Reflection.MethodInfo removeAll = onClick.GetType().GetMethod("RemoveAllListeners", Type.EmptyTypes);
            if (removeAll != null) removeAll.Invoke(onClick, null);

            System.Reflection.MethodInfo add = null;
            System.Reflection.MethodInfo[] methods = onClick.GetType().GetMethods();
            for (int i = 0; i < methods.Length; i++)
            {
                if (!string.Equals(methods[i].Name, "AddListener", StringComparison.Ordinal)) continue;
                System.Reflection.ParameterInfo[] parameters = methods[i].GetParameters();
                if (parameters.Length == 1)
                {
                    add = methods[i];
                    break;
                }
            }
            if (add == null) throw new InvalidOperationException("Could not resolve Button.onClick.AddListener.");

            Type delegateType = add.GetParameters()[0].ParameterType;
            Delegate listener = Delegate.CreateDelegate(delegateType, callback.Target, callback.Method);
            add.Invoke(onClick, new object[] { listener });
        }

        private static Component FindComponent(GameObject root, string fullName)
        {
            Component[] components = root.GetComponentsInChildren<Component>(true);
            for (int i = 0; i < components.Length; i++)
                if (components[i] != null && string.Equals(components[i].GetType().FullName, fullName, StringComparison.Ordinal))
                    return components[i];
            return null;
        }

        private static Type FindLoadedType(string fullName)
        {
            System.Reflection.Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type type = assemblies[i].GetType(fullName, false);
                if (type != null) return type;
            }
            return null;
        }

        private static void SetProperty(Component component, string name, object value)
        {
            if (component == null) return;
            try
            {
                System.Reflection.PropertyInfo property = component.GetType().GetProperty(name);
                if (property != null && property.CanWrite) property.SetValue(component, value, null);
            }
            catch
            {
                // Best-effort styling only.
            }
        }

        private static void RemoveComponentByName(GameObject root, string fullName)
        {
            Component[] components = root.GetComponentsInChildren<Component>(true);
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component != null && string.Equals(component.GetType().FullName, fullName, StringComparison.Ordinal))
                    UnityEngine.Object.Destroy(component);
            }
        }

        private static void SetText(GameObject root, string value)
        {
            Component[] components = root.GetComponentsInChildren<Component>(true);
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null) continue;
                System.Reflection.PropertyInfo property = component.GetType().GetProperty("text");
                if (property == null || !property.CanWrite || property.PropertyType != typeof(string)) continue;
                try { property.SetValue(component, value, null); } catch { }
            }
        }

        private static void SetFontSize(GameObject root, float value)
        {
            Component[] components = root.GetComponentsInChildren<Component>(true);
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null) continue;
                System.Reflection.PropertyInfo property = component.GetType().GetProperty("fontSize");
                if (property == null || !property.CanWrite || property.PropertyType != typeof(float)) continue;
                try { property.SetValue(component, value, null); } catch { }
            }
        }

        private static void SetRaycast(GameObject root, bool value)
        {
            Component[] components = root.GetComponentsInChildren<Component>(true);
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null) continue;
                System.Reflection.PropertyInfo property = component.GetType().GetProperty("raycastTarget");
                if (property == null || !property.CanWrite || property.PropertyType != typeof(bool)) continue;
                try { property.SetValue(component, value, null); } catch { }
            }
        }

        private static void AnchorTop(RectTransform rect, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(0f, height);
            rect.localScale = Vector3.one;
        }

        private static void AnchorBottom(RectTransform rect, float height)
        {
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(0f, height);
            rect.localScale = Vector3.one;
        }

        private static void SetTopLeft(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
            rect.localScale = Vector3.one;
        }

        private static void Stretch(RectTransform rect, Vector2 offsetMin, Vector2 offsetMax)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            rect.localScale = Vector3.one;
        }

        private static string PathOf(Transform transform)
        {
            if (transform == null) return "<null>";
            var names = new List<string>();
            Transform current = transform;
            while (current != null)
            {
                names.Add(current.name);
                current = current.parent;
            }
            names.Reverse();
            return "/" + string.Join("/", names.ToArray());
        }
    }
}
