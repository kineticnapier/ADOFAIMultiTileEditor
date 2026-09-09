using System;
using System.Collections.Generic;
using UnityEngine;

namespace KineticNapier.ADOFAIMultiTileEditor
{
    internal static class WorkspacePreviewRenderer
    {
        private const int MaxDrawnPoints = 240;

        internal static void Draw(TrackSlot track, Rect rect, bool active)
        {
            GUI.BeginGroup(rect);
            Rect local = new Rect(0f, 0f, rect.width, rect.height);
            GUI.Box(local, "");

            if (track == null || track.PreviewPositions == null || track.PreviewPositions.Count == 0)
            {
                GUI.Label(new Rect(8f, 8f, Math.Max(0f, rect.width - 16f), 22f), "No source preview assigned.");
                GUI.EndGroup();
                return;
            }

            int count = track.PreviewPositions.Count;
            int start = Math.Max(0, Math.Min(track.RegionStartFloor, count - 1));
            int first = Math.Max(0, start - 1);
            int last = count - 1;
            int spanCount = Math.Max(1, last - first + 1);
            int stride = Math.Max(1, (int)Math.Ceiling(spanCount / (double)MaxDrawnPoints));

            float minX = float.PositiveInfinity;
            float minY = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            float maxY = float.NegativeInfinity;
            bool any = false;

            for (int i = first; i <= last; i += stride)
            {
                Vector2 p = track.PreviewPositions[i];
                if (!Finite(p)) continue;
                any = true;
                minX = Math.Min(minX, p.x);
                minY = Math.Min(minY, p.y);
                maxX = Math.Max(maxX, p.x);
                maxY = Math.Max(maxY, p.y);
            }

            Vector2 lastPoint = track.PreviewPositions[last];
            if (Finite(lastPoint))
            {
                any = true;
                minX = Math.Min(minX, lastPoint.x);
                minY = Math.Min(minY, lastPoint.y);
                maxX = Math.Max(maxX, lastPoint.x);
                maxY = Math.Max(maxY, lastPoint.y);
            }

            if (!any)
            {
                GUI.Label(new Rect(8f, 8f, Math.Max(0f, rect.width - 16f), 22f), "Preview geometry unavailable.");
                GUI.EndGroup();
                return;
            }

            const float pad = 16f;
            float usableW = Math.Max(1f, rect.width - pad * 2f);
            float usableH = Math.Max(1f, rect.height - pad * 2f - 20f);
            float spanX = Math.Max(0.001f, maxX - minX);
            float spanY = Math.Max(0.001f, maxY - minY);
            float scale = Math.Min(usableW / spanX, usableH / spanY);
            if (float.IsInfinity(scale) || float.IsNaN(scale)) scale = 1f;

            float drawnW = spanX * scale;
            float drawnH = spanY * scale;
            float originX = pad + (usableW - drawnW) * 0.5f;
            float originY = pad + (usableH - drawnH) * 0.5f;

            Color oldColor = GUI.color;
            GUI.color = active ? new Color(0.42f, 0.78f, 1f, 0.95f) : new Color(0.72f, 0.72f, 0.72f, 0.8f);

            bool havePrev = false;
            Vector2 prev = Vector2.zero;
            for (int i = first; i <= last; i += stride)
            {
                Vector2 source = track.PreviewPositions[i];
                if (!Finite(source))
                {
                    havePrev = false;
                    continue;
                }

                Vector2 p = ToLocal(source, minX, minY, maxY, scale, originX, originY);
                if (havePrev) DrawLine(prev, p, active ? 2f : 1f);
                GUI.DrawTexture(new Rect(p.x - 2f, p.y - 2f, 4f, 4f), Texture2D.whiteTexture);
                prev = p;
                havePrev = true;
            }

            if ((last - first) % stride != 0 && Finite(lastPoint))
            {
                Vector2 p = ToLocal(lastPoint, minX, minY, maxY, scale, originX, originY);
                if (havePrev) DrawLine(prev, p, active ? 2f : 1f);
                GUI.DrawTexture(new Rect(p.x - 2f, p.y - 2f, 4f, 4f), Texture2D.whiteTexture);
            }

            DrawMarker(track, start, minX, minY, maxY, scale, originX, originY, new Color(1f, 0.82f, 0.2f, 1f), 7f);
            DrawMarker(track, track.CursorFloor, minX, minY, maxY, scale, originX, originY, new Color(0.35f, 1f, 0.48f, 1f), 5f);

            GUI.color = oldColor;
            GUI.Label(new Rect(8f, rect.height - 21f, Math.Max(0f, rect.width - 16f), 18f),
                "F" + start + " -> F" + last + "   " + count + " floors");
            GUI.EndGroup();
        }

        private static void DrawMarker(
            TrackSlot track,
            int floor,
            float minX,
            float minY,
            float maxY,
            float scale,
            float originX,
            float originY,
            Color color,
            float size)
        {
            if (track == null || track.PreviewPositions == null || floor < 0 || floor >= track.PreviewPositions.Count) return;
            Vector2 source = track.PreviewPositions[floor];
            if (!Finite(source)) return;

            Vector2 p = ToLocal(source, minX, minY, maxY, scale, originX, originY);
            Color old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(p.x - size * 0.5f, p.y - size * 0.5f, size, size), Texture2D.whiteTexture);
            GUI.color = old;
        }

        private static Vector2 ToLocal(
            Vector2 source,
            float minX,
            float minY,
            float maxY,
            float scale,
            float originX,
            float originY)
        {
            return new Vector2(
                originX + (source.x - minX) * scale,
                originY + (maxY - source.y) * scale);
        }

        private static bool Finite(Vector2 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x)
                && !float.IsNaN(value.y) && !float.IsInfinity(value.y);
        }

        private static void DrawLine(Vector2 from, Vector2 to, float width)
        {
            Vector2 delta = to - from;
            float length = delta.magnitude;
            if (length <= 0.001f) return;

            Matrix4x4 old = GUI.matrix;
            float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
            GUIUtility.RotateAroundPivot(angle, from);
            GUI.DrawTexture(new Rect(from.x, from.y - width * 0.5f, length, width), Texture2D.whiteTexture);
            GUI.matrix = old;
        }
    }
}
