using System;
using System.Reflection;
using ADOFAI.EditorToolkit.Game;
using UnityEngine;

namespace KineticNapier.ADOFAIMultiTileEditor
{
    /// <summary>
    /// Temporary smoke test for ADOFAIParallelEditor-style native editor mounting.
    /// The test deliberately reuses a stock ADOFAI button visual, strips its interaction,
    /// and mounts it inside the central editor viewport through ADOFAIEditorUiHost.
    /// </summary>
    internal static class NativeMountSmokeTest
    {
        private const string HostName = "ADOFAI Parallel Editor Native Host Test";
        private const string PanelName = "NativeMountSmokePanel";

        private static scnEditor mountedEditor;
        private static RectTransform host;

        internal static void Mount(scnEditor editor)
        {
            if (editor == null) return;

            if (mountedEditor == editor && host != null)
            {
                host.gameObject.SetActive(true);
                return;
            }

            mountedEditor = editor;
            host = ADOFAIEditorUiHost.GetOrCreateViewportRoot(HostName);
            host.gameObject.SetActive(true);

            ClearChildren(host);

            GameObject panel = ADOFAIEditorUiHost.CloneStockObject("buttonSave", host, PanelName);
            RectTransform rect = panel.transform as RectTransform;
            if (rect == null)
                throw new InvalidOperationException("Cloned stock button is not a RectTransform.");

            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -16f);
            rect.sizeDelta = new Vector2(560f, 64f);
            rect.localScale = Vector3.one;

            StripInteraction(panel);
            SetText(panel, "ADOFAIParallelEditor native host test");

            EditorUiInsets insets = ADOFAIEditorUiHost.MeasureViewportInsets();
            Debug.Log(
                "[ADOFAIMultiTileEditor] Native mount smoke test attached to "
                + ADOFAIEditorUiHost.Root.name
                + "; viewport=" + host.rect.width.ToString("0.#") + "x" + host.rect.height.ToString("0.#")
                + "; insets=" + insets + ".");
        }

        internal static void SetVisible(bool visible)
        {
            if (host != null) host.gameObject.SetActive(visible);
        }

        private static void ClearChildren(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                Transform child = parent.GetChild(i);
                if (child != null) UnityEngine.Object.Destroy(child.gameObject);
            }
        }

        private static void StripInteraction(GameObject root)
        {
            Component[] components = root.GetComponentsInChildren<Component>(true);
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null) continue;

                Type type = component.GetType();
                if (string.Equals(type.FullName, "UnityEngine.UI.Button", StringComparison.Ordinal)
                    || string.Equals(type.FullName, "UnityEngine.EventSystems.EventTrigger", StringComparison.Ordinal))
                {
                    UnityEngine.Object.Destroy(component);
                    continue;
                }

                TrySetBoolProperty(component, "raycastTarget", false);
                TrySetBoolProperty(component, "interactable", false);
            }
        }

        private static void SetText(GameObject root, string value)
        {
            Component[] components = root.GetComponentsInChildren<Component>(true);
            bool changed = false;

            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null) continue;

                Type type = component.GetType();
                PropertyInfo property = type.GetProperty(
                    "text",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                if (property == null || !property.CanWrite || property.PropertyType != typeof(string)) continue;

                try
                {
                    property.SetValue(component, value, null);
                    changed = true;
                }
                catch
                {
                    // Ignore unrelated components that expose an unusable text property.
                }
            }

            if (!changed)
                throw new InvalidOperationException("Could not locate a writable text component in the stock button clone.");
        }

        private static void TrySetBoolProperty(Component component, string name, bool value)
        {
            try
            {
                PropertyInfo property = component.GetType().GetProperty(
                    name,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property != null && property.CanWrite && property.PropertyType == typeof(bool))
                    property.SetValue(component, value, null);
            }
            catch
            {
                // Best-effort only; the smoke-test visual must remain non-fatal.
            }
        }
    }
}
