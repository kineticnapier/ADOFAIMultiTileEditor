using UnityEngine;

namespace KineticNapier.ADOFAIMultiTileEditor
{
    internal sealed class MultiTileOverlay : MonoBehaviour
    {
        private const int WindowId = 0x4D5445;
        private const float MinWidth = 720f;
        private const float MinHeight = 460f;
        private static readonly Rect DefaultRect = new Rect(20f, 56f, 1040f, 680f);

        private Rect windowRect = DefaultRect;
        private bool resizing;
        private Vector2 resizeMouseStart;
        private Rect resizeRectStart;
        private string probeStatus = "";
        private scnEditor nativeWorkspaceFailureEditor;
        internal bool Visible = true;

        internal void ResetPosition()
        {
            windowRect = DefaultRect;
            FitToScreen();
        }

        private void OnEnable()
        {
            nativeWorkspaceFailureEditor = null;
        }

        private void OnDisable()
        {
            NativeParallelWorkspace.SetVisible(false);
        }

        private void Update()
        {
            if (!Main.OverlayCanDraw)
            {
                NativeParallelWorkspace.SetVisible(false);
                return;
            }

            scnEditor editor = ADOBase.editor;
            if (editor == null || editor == nativeWorkspaceFailureEditor) return;

            try
            {
                NativeParallelWorkspace.Update(editor);
            }
            catch (System.Exception ex)
            {
                nativeWorkspaceFailureEditor = editor;
                NativeParallelWorkspace.SetVisible(false);
                Debug.LogError("[ADOFAIMultiTileEditor] Native ParallelEditor workspace failed: " + ex);
            }
        }

        private void OnGUI()
        {
            if (!Visible || !Main.OverlayCanDraw) return;

            FitToScreen();
            windowRect = GUI.Window(
                WindowId,
                windowRect,
                DrawWindow,
                "ADOFAI Multi Tile Workspace v" + Main.ModVersion);

            FitToScreen();
        }

        private void DrawWindow(int id)
        {
            scnEditor editor = ADOBase.editor;

            GUILayout.BeginHorizontal();
            GUILayout.Label("ParallelEditor research", GUILayout.Width(135f));
            if (GUILayout.Button("Probe ADOFAI UI", GUILayout.Width(120f)))
            {
                string report = EditorUiProbe.Capture(editor);
                GUIUtility.systemCopyBuffer = report;
                Debug.Log(report);
                probeStatus = "Copied " + report.Split('\n').Length + " lines to clipboard + Unity log.";
            }
            if (!string.IsNullOrEmpty(probeStatus)) GUILayout.Label(probeStatus);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            WorkspaceGui.DrawWindow(editor, windowRect.width, Mathf.Max(1f, windowRect.height - 26f));

            Rect grip = new Rect(windowRect.width - 18f, windowRect.height - 18f, 16f, 16f);
            GUI.Box(grip, "//");

            Event ev = Event.current;
            if (ev != null)
            {
                if (ev.type == EventType.MouseDown && ev.button == 0 && grip.Contains(ev.mousePosition))
                {
                    resizing = true;
                    resizeMouseStart = ev.mousePosition;
                    resizeRectStart = windowRect;
                    ev.Use();
                }
                else if (resizing && ev.type == EventType.MouseDrag)
                {
                    Vector2 delta = ev.mousePosition - resizeMouseStart;
                    float maxWidth = Mathf.Max(MinWidth, Screen.width - windowRect.x);
                    float maxHeight = Mathf.Max(MinHeight, Screen.height - windowRect.y);
                    windowRect.width = Mathf.Clamp(resizeRectStart.width + delta.x, MinWidth, maxWidth);
                    windowRect.height = Mathf.Clamp(resizeRectStart.height + delta.y, MinHeight, maxHeight);
                    ev.Use();
                }
                else if (resizing && (ev.rawType == EventType.MouseUp || ev.type == EventType.MouseUp))
                {
                    resizing = false;
                }
            }

            if (!resizing)
                GUI.DragWindow(new Rect(0f, 0f, windowRect.width - 24f, 24f));
        }

        private void FitToScreen()
        {
            float screenWidth = Mathf.Max(320f, Screen.width);
            float screenHeight = Mathf.Max(240f, Screen.height);
            float minWidth = Mathf.Min(MinWidth, screenWidth);
            float minHeight = Mathf.Min(MinHeight, screenHeight);

            windowRect.width = Mathf.Clamp(windowRect.width, minWidth, screenWidth);
            windowRect.height = Mathf.Clamp(windowRect.height, minHeight, screenHeight);

            float maxX = Mathf.Max(0f, screenWidth - windowRect.width);
            float maxY = Mathf.Max(0f, screenHeight - 28f);
            windowRect.x = Mathf.Clamp(windowRect.x, 0f, maxX);
            windowRect.y = Mathf.Clamp(windowRect.y, 0f, maxY);
        }
    }
}
