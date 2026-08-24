using System;
using UnityEngine;
using UnityModManagerNet;

namespace KineticNapier.ADOFAIMultiTileEditor
{
    public static class Main
    {
        internal static string ModVersion { get { return typeof(Main).Assembly.GetName().Version.ToString(3); } }

        private static UnityModManager.ModEntry.ModLogger logger;
        private static readonly TrackStore store = new TrackStore();
        private static bool enabled;
        private static scnEditor lastEditor;
        private static MultiTileOverlay overlay;

        internal static bool OverlayCanDraw
        {
            get { return enabled && ADOBase.editor != null; }
        }

        public static bool Load(UnityModManager.ModEntry entry)
        {
            logger = entry.Logger;
            entry.OnToggle = OnToggle;
            entry.OnUpdate = OnUpdate;
            EnsureOverlay();
            logger.Log("ADOFAI Multi Tile Editor v" + ModVersion + " loaded. UI is hosted in ADOFAI Workbench.");
            return true;
        }

        private static bool OnToggle(UnityModManager.ModEntry entry, bool value)
        {
            enabled = value;
            EnsureOverlay();
            if (overlay != null) overlay.enabled = value;
            return true;
        }

        private static void OnUpdate(UnityModManager.ModEntry entry, float deltaTime)
        {
            if (!enabled) return;

            scnEditor editor = ADOBase.editor;
            if (editor == lastEditor) return;

            if (lastEditor != null && editor != lastEditor)
            {
                store.Reset();
                WorkbenchIntegration.ResetGenerationState("Editor instance changed; track queue was cleared.");
                WorkbenchIntegration.PublishNow(true);
            }

            lastEditor = editor;
        }

        private static void EnsureOverlay()
        {
            if (overlay != null) return;
            try
            {
                GameObject host = new GameObject("ADOFAIMultiTileEditorWorkbenchIntegration");
                UnityEngine.Object.DontDestroyOnLoad(host);
                overlay = host.AddComponent<MultiTileOverlay>();
                overlay.enabled = enabled;
            }
            catch (Exception ex)
            {
                if (logger != null) logger.Error("Could not initialize Multi Tile Workbench integration: " + ex);
            }
        }
    }
}
