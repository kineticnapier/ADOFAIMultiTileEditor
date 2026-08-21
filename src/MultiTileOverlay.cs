using UnityEngine;

namespace KineticNapier.ADOFAIMultiTileEditor
{
    internal sealed class MultiTileOverlay : MonoBehaviour
    {
        private const int WindowId = 0x4D5445;
        private static readonly Rect DefaultRect = new Rect(24f, 80f, 760f, 540f);

        private Rect windowRect = DefaultRect;
        internal bool Visible = true;

        internal void ResetPosition()
        {
            windowRect = DefaultRect;
        }

        private void OnGUI()
        {
            if (!Visible || !Main.OverlayCanDraw) return;

            windowRect = GUI.Window(
                WindowId,
                windowRect,
                DrawWindow,
                "ADOFAI Multi Tile Editor v" + Main.ModVersion);

            float maxX = Mathf.Max(0f, Screen.width - windowRect.width);
            float maxY = Mathf.Max(0f, Screen.height - 28f);
            windowRect.x = Mathf.Clamp(windowRect.x, 0f, maxX);
            windowRect.y = Mathf.Clamp(windowRect.y, 0f, maxY);
        }

        private void DrawWindow(int id)
        {
            Main.DrawOverlayContents();
            GUI.DragWindow(new Rect(0f, 0f, windowRect.width - 8f, 24f));
        }
    }
}
