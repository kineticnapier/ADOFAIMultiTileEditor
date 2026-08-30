using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace KineticNapier.ADOFAIMultiTileEditor
{
    internal static class StockEditorChromeOverride
    {
        private sealed class SavedState
        {
            internal GameObject Object;
            internal bool ActiveSelf;
        }

        private static readonly string[] PublicMembers =
        {
            "settingsPanel",
            "levelEventsPanel",
            "inspectorTabs",
            "inspectorPanels",
            "levelStringPanel",
            "findFloorPanel"
        };

        private static readonly List<SavedState> saved = new List<SavedState>();
        private static scnEditor appliedEditor;

        internal static void Apply(scnEditor editor)
        {
            if (editor == null) return;
            if (ReferenceEquals(appliedEditor, editor)) return;

            Restore();
            appliedEditor = editor;

            for (int i = 0; i < PublicMembers.Length; i++)
            {
                GameObject go = ResolvePublicObject(editor, PublicMembers[i]);
                Hide(go);
            }

            // ADOFAI 3.3.x stock root children which are not consistently exposed through
            // public scnEditor fields. Keep popup/shortcut-style overlays alone for now.
            RectTransform root = editor.transform as RectTransform;
            if (root != null)
            {
                HideDirectChild(root, "bottomPanel");
                HideDirectChild(root, "fileActions");
                HideDirectChild(root, "filePanel");
                HideDirectChild(root, "eventTabs");
            }
        }

        internal static void Restore()
        {
            for (int i = saved.Count - 1; i >= 0; i--)
            {
                SavedState state = saved[i];
                if (state != null && state.Object != null)
                    state.Object.SetActive(state.ActiveSelf);
            }
            saved.Clear();
            appliedEditor = null;
        }

        private static void HideDirectChild(Transform parent, string name)
        {
            if (parent == null || string.IsNullOrEmpty(name)) return;
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (child != null && string.Equals(child.name, name, StringComparison.OrdinalIgnoreCase))
                    Hide(child.gameObject);
            }
        }

        private static void Hide(GameObject go)
        {
            if (go == null) return;
            for (int i = 0; i < saved.Count; i++)
                if (saved[i] != null && ReferenceEquals(saved[i].Object, go)) return;

            saved.Add(new SavedState { Object = go, ActiveSelf = go.activeSelf });
            go.SetActive(false);
        }

        private static GameObject ResolvePublicObject(scnEditor editor, string name)
        {
            if (editor == null || string.IsNullOrEmpty(name)) return null;
            Type type = editor.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;

            FieldInfo field = type.GetField(name, flags);
            if (field != null) return ToGameObject(field.GetValue(editor));

            System.Reflection.PropertyInfo property = type.GetProperty(name, flags);
            if (property != null && property.CanRead) return ToGameObject(property.GetValue(editor, null));

            return null;
        }

        private static GameObject ToGameObject(object value)
        {
            GameObject go = value as GameObject;
            if (go != null) return go;
            Component component = value as Component;
            return component != null ? component.gameObject : null;
        }
    }
}
