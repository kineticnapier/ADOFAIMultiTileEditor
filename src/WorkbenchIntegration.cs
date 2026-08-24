using System;
using System.Collections.Generic;
using ADOFAI.EditorToolkit.Game;
using KineticNapier.ADOFAIWorkbench;
using UnityEngine;

namespace KineticNapier.ADOFAIMultiTileEditor
{
    internal static class WorkbenchIntegration
    {
        private static readonly MultiTilePaneProvider Provider = new MultiTilePaneProvider();
        private static bool registered;

        internal static void EnsureRegistered()
        {
            if (registered) return;
            Workbench.RegisterPaneProvider(Provider);
            registered = true;
            Workbench.OpenPane("mte.tracks");
            Workbench.OpenPane("mte.settings");
        }

        internal static void Unregister()
        {
            if (!registered) return;
            Workbench.UnregisterPaneProvider(Provider);
            registered = false;
        }
    }

    internal sealed class MultiTilePaneProvider : IDockablePaneProvider
    {
        private readonly IDockablePane[] panes =
        {
            new MultiTileTracksPane(),
            new MultiTileSettingsPane()
        };

        public IEnumerable<IDockablePane> CreatePanes()
        {
            for (int i = 0; i < panes.Length; i++) yield return panes[i];
        }
    }

    internal abstract class MultiTilePaneBase : IDockablePane
    {
        protected GameObject root;
        protected RectTransform rootRect;

        public abstract string Id { get; }
        public abstract string Title { get; }
        public virtual bool CanClose { get { return true; } }

        public void Mount(RectTransform parent)
        {
            Unmount();
            root = new GameObject(Title + " Pane", typeof(RectTransform));
            rootRect = (RectTransform)root.transform;
            rootRect.SetParent(parent, false);
            Stretch(rootRect);
            Draw();
        }

        public void Unmount()
        {
            if (root != null) UnityEngine.Object.Destroy(root);
            root = null;
            rootRect = null;
        }

        protected abstract void Draw();

        protected void Refresh()
        {
            if (rootRect == null) return;
            for (int i = rootRect.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(rootRect.GetChild(i).gameObject);
            Draw();
        }

        protected static scnEditor Editor { get { return ADOBase.editor; } }
        protected static TrackStore Store { get { return TrackStore.Current; } }

        protected void Run(Action action)
        {
            try
            {
                action();
                Refresh();
            }
            catch (Exception ex)
            {
                Debug.LogError("[ADOFAIMultiTileEditor/Workbench] " + ex);
            }
        }

        protected RectTransform Label(string text, float x, float y, float width, float height, float size)
        {
            GameObject label = ADOFAIEditorUiHost.CloneStockObject("findFloorPanelTitle", rootRect, "Label");
            RectTransform rect = label.transform as RectTransform;
            SetTopLeft(rect, x, y, width, height);
            SetText(label, text);
            SetFontSize(label, size);
            SetRaycast(label, false);
            return rect;
        }

        protected RectTransform Button(string text, float x, float y, float width, float height, Action action, bool selected)
        {
            RectTransform rect = CreateRect(rootRect, "Button");
            SetTopLeft(rect, x, y, width, height);

            Type imageType = FindLoadedType("UnityEngine.UI.Image");
            Type buttonType = FindLoadedType("UnityEngine.UI.Button");
            if (imageType == null || buttonType == null) throw new InvalidOperationException("Unity UI types are unavailable.");

            Component image = rect.gameObject.AddComponent(imageType);
            SetProperty(image, "color", selected ? new Color(0.28f, 0.34f, 0.46f, 0.98f) : new Color(0.18f, 0.19f, 0.23f, 0.98f));
            SetProperty(image, "raycastTarget", true);
            Component button = rect.gameObject.AddComponent(buttonType);
            SetProperty(button, "targetGraphic", image);
            BindButton(button, action);

            GameObject label = ADOFAIEditorUiHost.CloneStockObject("findFloorPanelTitle", rect, "Text");
            RectTransform labelRect = label.transform as RectTransform;
            SetTopLeft(labelRect, 6f, 0f, Math.Max(1f, width - 12f), height);
            SetText(label, text);
            SetFontSize(label, 14f);
            SetRaycast(label, false);
            return rect;
        }

        private static void BindButton(Component button, Action callback)
        {
            object onClick = button.GetType().GetProperty("onClick").GetValue(button, null);
            System.Reflection.MethodInfo remove = onClick.GetType().GetMethod("RemoveAllListeners", Type.EmptyTypes);
            if (remove != null) remove.Invoke(onClick, null);
            System.Reflection.MethodInfo[] methods = onClick.GetType().GetMethods();
            for (int i = 0; i < methods.Length; i++)
            {
                if (!string.Equals(methods[i].Name, "AddListener", StringComparison.Ordinal)) continue;
                if (methods[i].GetParameters().Length != 1) continue;
                Type delegateType = methods[i].GetParameters()[0].ParameterType;
                Delegate listener = Delegate.CreateDelegate(delegateType, callback.Target, callback.Method);
                methods[i].Invoke(onClick, new object[] { listener });
                return;
            }
            throw new InvalidOperationException("Could not bind Unity button callback.");
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
            System.Reflection.PropertyInfo property = component.GetType().GetProperty(name);
            if (property != null && property.CanWrite) property.SetValue(component, value, null);
        }

        private static void SetText(GameObject go, string value)
        {
            Component[] components = go.GetComponentsInChildren<Component>(true);
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null) continue;
                System.Reflection.PropertyInfo property = component.GetType().GetProperty("text");
                if (property != null && property.CanWrite && property.PropertyType == typeof(string))
                    property.SetValue(component, value, null);
            }
        }

        private static void SetFontSize(GameObject go, float value)
        {
            Component[] components = go.GetComponentsInChildren<Component>(true);
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null) continue;
                System.Reflection.PropertyInfo property = component.GetType().GetProperty("fontSize");
                if (property != null && property.CanWrite && property.PropertyType == typeof(float))
                    property.SetValue(component, value, null);
            }
        }

        private static void SetRaycast(GameObject go, bool value)
        {
            Component[] components = go.GetComponentsInChildren<Component>(true);
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null) continue;
                System.Reflection.PropertyInfo property = component.GetType().GetProperty("raycastTarget");
                if (property != null && property.CanWrite && property.PropertyType == typeof(bool))
                    property.SetValue(component, value, null);
            }
        }

        private static RectTransform CreateRect(Transform parent, string name)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            RectTransform rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void SetTopLeft(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }
    }

    internal sealed class MultiTileTracksPane : MultiTilePaneBase
    {
        public override string Id { get { return "mte.tracks"; } }
        public override string Title { get { return "MTE Tracks"; } }

        protected override void Draw()
        {
            Label("Multi Tile tracks", 12f, 10f, 360f, 28f, 19f);
            TrackStore store = Store;
            scnEditor editor = Editor;
            if (store == null || editor == null)
            {
                Label("Open a level editor first.", 12f, 46f, 420f, 24f, 14f);
                return;
            }

            Button("+ Store current", 12f, 48f, 150f, 30f, delegate { Run(delegate { store.StoreCurrent(editor, ""); }); }, false);
            Button("Save active", 170f, 48f, 120f, 30f, delegate { Run(delegate { store.SaveActive(editor); }); }, false);
            Button("Start = selected", 298f, 48f, 150f, 30f, delegate { Run(delegate { store.SetActiveRegionStartFromSelection(editor); }); }, false);

            float y = 90f;
            for (int i = 0; i < store.Tracks.Count; i++)
            {
                int index = i;
                TrackSlot track = store.Tracks[i];
                bool active = i == store.ActiveIndex;
                string title = (active ? "> " : "") + (track != null ? track.Name : "Track " + (i + 1));
                Button(title, 12f, y, 250f, 30f, delegate { Run(delegate { store.SwitchTo(editor, index); }); }, active);
                Button("x", 268f, y, 34f, 30f, delegate { Run(delegate { store.Remove(editor, index); }); }, false);
                if (track != null)
                    Label("F" + track.RegionStartFloor + "  " + CompactLayoutPostProcessor.Describe(track), 312f, y + 2f, 500f, 26f, 13f);
                y += 36f;
            }

            if (store.Tracks.Count == 0)
                Label("No tracks yet. Store the current chart to create one.", 12f, 94f, 600f, 26f, 14f);
        }
    }

    internal sealed class MultiTileSettingsPane : MultiTilePaneBase
    {
        public override string Id { get { return "mte.settings"; } }
        public override string Title { get { return "Multi Tile"; } }

        protected override void Draw()
        {
            Label("Multi Tile settings", 12f, 10f, 360f, 28f, 19f);
            TrackStore store = Store;
            if (store == null || store.ActiveIndex < 0 || store.ActiveIndex >= store.Tracks.Count)
            {
                Label("Choose or store a source track first.", 12f, 48f, 520f, 26f, 14f);
                return;
            }

            TrackSlot track = store.Tracks[store.ActiveIndex];
            Label(track.Name + "   start F" + track.RegionStartFloor + "   cursor F" + track.CursorFloor, 12f, 48f, 620f, 26f, 15f);

            Label("Pivot", 12f, 88f, 80f, 26f, 14f);
            Button(track.PivotIsA ? "A" : "B", 90f, 84f, 70f, 30f, delegate { Run(delegate { track.PivotIsA = !track.PivotIsA; }); }, true);

            Label("Wrap", 12f, 128f, 80f, 26f, 14f);
            Button("Off", 90f, 124f, 70f, 30f, delegate { Run(delegate { track.WrapMode = CompactWrapMode.Off; }); }, track.WrapMode == CompactWrapMode.Off);
            Button("Tiles", 166f, 124f, 70f, 30f, delegate { Run(delegate { track.WrapMode = CompactWrapMode.Tiles; }); }, track.WrapMode == CompactWrapMode.Tiles);
            Button("Beats", 242f, 124f, 70f, 30f, delegate { Run(delegate { track.WrapMode = CompactWrapMode.Beats; }); }, track.WrapMode == CompactWrapMode.Beats);

            Label("Repeat", 12f, 168f, 80f, 26f, 14f);
            Button("-", 90f, 164f, 38f, 30f, delegate { Run(delegate { track.RepeatCount = Math.Max(1, track.RepeatCount - 1); track.RepeatCountText = track.RepeatCount.ToString(); }); }, false);
            Label("x" + track.RepeatCount, 136f, 166f, 70f, 26f, 15f);
            Button("+", 206f, 164f, 38f, 30f, delegate { Run(delegate { track.RepeatCount = Math.Min(999, track.RepeatCount + 1); track.RepeatCountText = track.RepeatCount.ToString(); }); }, false);
            Button(track.ReuseRepeatPath ? "Reuse path: ON" : "Reuse path: OFF", 260f, 164f, 150f, 30f, delegate { Run(delegate { track.ReuseRepeatPath = !track.ReuseRepeatPath; }); }, track.ReuseRepeatPath);

            Label("Planet A tag: " + (string.IsNullOrWhiteSpace(track.PlanetATag) ? "<unset>" : track.PlanetATag), 12f, 212f, 500f, 24f, 13f);
            Label("Planet B tag: " + (string.IsNullOrWhiteSpace(track.PlanetBTag) ? "<unset>" : track.PlanetBTag), 12f, 238f, 500f, 24f, 13f);
            Label("Generation controls remain in the MTE UMM panel for this migration step.", 12f, 278f, 680f, 24f, 13f);
        }
    }
}
