using System;
using System.Globalization;
using UnityEngine;

namespace KineticNapier.ADOFAIMultiTileEditor
{
    internal sealed class MultiTileOverlay : MonoBehaviour
    {
        private const int WindowId = 0x4D5445;
        private static readonly Rect DefaultRect = new Rect(24f, 80f, 720f, 485f);

        private Rect windowRect = DefaultRect;
        private string wrapTilesText = "32";
        private string wrapBeatsText = "16";
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
            DrawCompactLayoutControls();
            GUILayout.Space(3f);
            Main.DrawOverlayContents();
            GUI.DragWindow(new Rect(0f, 0f, windowRect.width - 8f, 24f));
        }

        private void DrawCompactLayoutControls()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Auto Wrap", GUILayout.Width(72f));

            if (GUILayout.Button(
                CompactLayoutPostProcessor.WrapMode == CompactWrapMode.Off ? "> Off" : "Off",
                GUILayout.Width(58f)))
            {
                CompactLayoutPostProcessor.WrapMode = CompactWrapMode.Off;
            }

            if (GUILayout.Button(
                CompactLayoutPostProcessor.WrapMode == CompactWrapMode.Tiles ? "> Tiles" : "Tiles",
                GUILayout.Width(65f)))
            {
                CompactLayoutPostProcessor.WrapMode = CompactWrapMode.Tiles;
            }

            if (GUILayout.Button(
                CompactLayoutPostProcessor.WrapMode == CompactWrapMode.Beats ? "> Beats" : "Beats",
                GUILayout.Width(68f)))
            {
                CompactLayoutPostProcessor.WrapMode = CompactWrapMode.Beats;
            }

            if (CompactLayoutPostProcessor.WrapMode == CompactWrapMode.Tiles)
            {
                GUILayout.Label("Length", GUILayout.Width(48f));
                wrapTilesText = GUILayout.TextField(wrapTilesText, GUILayout.Width(58f));
                int value;
                if (int.TryParse(wrapTilesText, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
                    && value > 0)
                    CompactLayoutPostProcessor.WrapEveryTiles = value;
                GUILayout.Label("tiles", GUILayout.Width(35f));
            }
            else if (CompactLayoutPostProcessor.WrapMode == CompactWrapMode.Beats)
            {
                GUILayout.Label("Length", GUILayout.Width(48f));
                wrapBeatsText = GUILayout.TextField(wrapBeatsText, GUILayout.Width(58f));
                double value;
                if (double.TryParse(wrapBeatsText, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                    && value > 0.0 && !double.IsNaN(value) && !double.IsInfinity(value))
                    CompactLayoutPostProcessor.WrapEveryBeats = value;
                GUILayout.Label("beats", GUILayout.Width(40f));
            }

            GUILayout.FlexibleSpace();
            GUILayout.Label("Current: " + CompactLayoutPostProcessor.WrapSummary, GUILayout.Width(130f));
            GUILayout.EndHorizontal();
        }
    }
}
