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
            MteLocalization.Initialize();
            EnsureOverlay();
            logger.Log("ADOFAI Multi Tile Editor v" + ModVersion + " loaded. UI is hosted in ADOFAI Workbench.");
            return true;
        }

        private static bool OnToggle(UnityModManager.ModEntry entry, bool value)
        {
            if (!value && ADOBase.editor != null) store.FlushAutosave(ADOBase.editor);
            enabled = value;
            EnsureOverlay();
            if (overlay != null) overlay.enabled = value;
            return true;
        }

        private static void OnUpdate(UnityModManager.ModEntry entry, float deltaTime)
        {
            if (!enabled) return;

            scnEditor editor = ADOBase.editor;
            bool editorChanged = editor != lastEditor;
            bool chartChangedInPlace = !editorChanged && editor != null && ChartSessionGuard.HasExternalChange(editor);

            if (editorChanged || chartChangedInPlace)
            {
                store.Reset();
                string message = chartChangedInPlace
                    ? "Open chart changed; stale in-memory MTE tracks were detached."
                    : "Editor session changed.";

                lastEditor = editor;
                if (editor != null)
                {
                    ChartSessionGuard.AcceptCurrent(editor);
                    string recovery;
                    if (store.TryRestoreWorkspace(editor, out recovery))
                        message = recovery + " Choose a track to restore its source snapshot.";
                    else if (!string.IsNullOrWhiteSpace(recovery))
                        message = recovery;
                }

                WorkbenchIntegration.ResetGenerationState(message);
                WorkbenchIntegration.PublishNow(true);
            }
            else
            {
                lastEditor = editor;
                if (editor != null) ChartSessionGuard.AcceptCurrent(editor);
            }

            if (editor != null) store.AutosaveTick(editor);
        }

        internal static void FlushWorkspaceAutosave()
        {
            if (ADOBase.editor != null) store.FlushAutosave(ADOBase.editor);
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
