using System;
using System.Globalization;
using UnityEngine;

namespace KineticNapier.ADOFAIMultiTileEditor
{
    internal static class WorkspaceGui
    {
        private static string newTrackName = "";

        internal static void Draw(
            scnEditor editor,
            TrackStore store,
            MultiTileWorkspace workspace,
            float windowWidth,
            float windowHeight,
            Action invalidate,
            Action<string> report,
            Action<Action> run)
        {
            if (editor == null || store == null || workspace == null) return;

            workspace.EnsureAssignments(store.Tracks);
            DrawToolbar(editor, store, workspace, invalidate, report, run);
            GUILayout.Space(4f);

            float available = Mathf.Max(220f, windowHeight - 180f);
            switch (workspace.LayoutMode)
            {
                case WorkspaceLayoutMode.Single:
                    DrawPane(editor, store, workspace, 0, available, invalidate, report, run);
                    break;

                case WorkspaceLayoutMode.TwoColumns:
                    GUILayout.BeginHorizontal();
                    DrawPane(editor, store, workspace, 0, available, invalidate, report, run);
                    DrawPane(editor, store, workspace, 1, available, invalidate, report, run);
                    GUILayout.EndHorizontal();
                    break;

                case WorkspaceLayoutMode.TwoRows:
                {
                    float paneHeight = Mathf.Max(110f, (available - 5f) * 0.5f);
                    DrawPane(editor, store, workspace, 0, paneHeight, invalidate, report, run);
                    DrawPane(editor, store, workspace, 1, paneHeight, invalidate, report, run);
                    break;
                }

                default:
                {
                    float paneHeight = Mathf.Max(110f, (available - 5f) * 0.5f);
                    GUILayout.BeginHorizontal();
                    DrawPane(editor, store, workspace, 0, paneHeight, invalidate, report, run);
                    DrawPane(editor, store, workspace, 1, paneHeight, invalidate, report, run);
                    GUILayout.EndHorizontal();
                    GUILayout.BeginHorizontal();
                    DrawPane(editor, store, workspace, 2, paneHeight, invalidate, report, run);
                    DrawPane(editor, store, workspace, 3, paneHeight, invalidate, report, run);
                    GUILayout.EndHorizontal();
                    break;
                }
            }
        }

        private static void DrawToolbar(
            scnEditor editor,
            TrackStore store,
            MultiTileWorkspace workspace,
            Action invalidate,
            Action<string> report,
            Action<Action> run)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Editor groups", GUILayout.Width(82f));

            DrawLayoutButton(workspace, WorkspaceLayoutMode.Single, "1", 34f, store);
            DrawLayoutButton(workspace, WorkspaceLayoutMode.TwoColumns, "1|2", 46f, store);
            DrawLayoutButton(workspace, WorkspaceLayoutMode.TwoRows, "1/2", 46f, store);
            DrawLayoutButton(workspace, WorkspaceLayoutMode.Grid2x2, "2x2", 48f, store);

            GUILayout.Space(8f);
            GUILayout.Label("New track", GUILayout.Width(62f));
            newTrackName = GUILayout.TextField(newTrackName ?? "", GUILayout.Width(150f));
            if (GUILayout.Button("+ Store current", GUILayout.Width(125f)))
            {
                run(delegate
                {
                    int index = store.StoreCurrent(editor, newTrackName);
                    newTrackName = "";
                    workspace.AssignToActivePane(store.Tracks[index]);
                    workspace.EnsureAssignments(store.Tracks);
                    invalidate();
                    report("Stored " + store.Tracks[index].Name + " in editor group " + (workspace.ActivePaneIndex + 1) + ".");
                });
            }

            GUILayout.FlexibleSpace();
            GUILayout.Label(store.Tracks.Count + " track(s)", GUILayout.Width(72f));
            GUILayout.EndHorizontal();
        }

        private static void DrawLayoutButton(
            MultiTileWorkspace workspace,
            WorkspaceLayoutMode mode,
            string label,
            float width,
            TrackStore store)
        {
            string text = workspace.LayoutMode == mode ? "> " + label : label;
            if (GUILayout.Button(text, GUILayout.Width(width)))
                workspace.SetLayout(mode, store.Tracks);
        }

        private static void DrawPane(
            scnEditor editor,
            TrackStore store,
            MultiTileWorkspace workspace,
            int paneIndex,
            float paneHeight,
            Action invalidate,
            Action<string> report,
            Action<Action> run)
        {
            WorkspacePaneState pane = workspace.GetPane(paneIndex);
            TrackSlot track = pane.Track;
            int trackIndex = workspace.GetTrackIndex(store.Tracks, paneIndex);
            bool active = trackIndex >= 0 && trackIndex == store.ActiveIndex && workspace.ActivePaneIndex == paneIndex;

            GUILayout.BeginVertical("box", GUILayout.Height(paneHeight), GUILayout.ExpandWidth(true));

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(active ? "ACTIVE" : "Edit", GUILayout.Width(55f)))
                ActivatePane(editor, store, workspace, paneIndex, invalidate, report, run);

            GUI.enabled = store.Tracks.Count > 1;
            if (GUILayout.Button("<", GUILayout.Width(24f)))
            {
                workspace.CycleTrack(store.Tracks, paneIndex, -1);
                if (workspace.ActivePaneIndex == paneIndex)
                    ActivatePane(editor, store, workspace, paneIndex, invalidate, report, run);
            }
            GUI.enabled = true;

            if (track != null)
            {
                string oldName = track.Name ?? ("Track " + (trackIndex + 1));
                string edited = GUILayout.TextField(oldName, GUILayout.MinWidth(80f));
                if (!string.Equals(oldName, edited, StringComparison.Ordinal)) track.Name = edited;
            }
            else
            {
                GUILayout.Label("Empty editor group");
            }

            GUI.enabled = store.Tracks.Count > 1;
            if (GUILayout.Button(">", GUILayout.Width(24f)))
            {
                workspace.CycleTrack(store.Tracks, paneIndex, 1);
                if (workspace.ActivePaneIndex == paneIndex)
                    ActivatePane(editor, store, workspace, paneIndex, invalidate, report, run);
            }
            GUI.enabled = true;

            GUILayout.Label("G" + (paneIndex + 1), GUILayout.Width(26f));
            GUILayout.EndHorizontal();

            if (track == null || trackIndex < 0)
            {
                GUILayout.FlexibleSpace();
                GUILayout.Label("Store a source chart or change the editor-group layout.");
                GUILayout.FlexibleSpace();
                GUILayout.EndVertical();
                return;
            }

            float previewHeight = Mathf.Max(62f, paneHeight - 142f);
            Rect preview = GUILayoutUtility.GetRect(80f, previewHeight, GUILayout.ExpandWidth(true));
            WorkspacePreviewRenderer.Draw(track, preview, active);

            Event ev = Event.current;
            if (ev != null && ev.type == EventType.MouseDown && ev.button == 0 && preview.Contains(ev.mousePosition))
            {
                ActivatePane(editor, store, workspace, paneIndex, invalidate, report, run);
                ev.Use();
            }

            DrawTrackSummary(track);
            DrawPlanetSettings(track, invalidate);
            DrawCompactLayoutSettings(track, invalidate);

            GUILayout.BeginHorizontal();
            GUI.enabled = trackIndex == store.ActiveIndex;
            if (GUILayout.Button("Save snapshot", GUILayout.Width(100f)))
            {
                run(delegate
                {
                    store.SaveActive(editor);
                    invalidate();
                    report("Saved " + track.Name + " from the stock ADOFAI editor.");
                });
            }
            if (GUILayout.Button("Start <- selection", GUILayout.Width(125f)))
            {
                run(delegate
                {
                    store.SetActiveRegionStartFromSelection(editor);
                    invalidate();
                    report("Set " + track.Name + " Multi Tile start to F" + track.RegionStartFloor + ".");
                });
            }
            GUI.enabled = true;

            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Delete", GUILayout.Width(58f)))
            {
                int target = trackIndex;
                run(delegate
                {
                    store.Remove(editor, target);
                    workspace.EnsureAssignments(store.Tracks);
                    invalidate();
                    report("Removed source track.");
                });
            }
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
        }

        private static void ActivatePane(
            scnEditor editor,
            TrackStore store,
            MultiTileWorkspace workspace,
            int paneIndex,
            Action invalidate,
            Action<string> report,
            Action<Action> run)
        {
            int target = workspace.GetTrackIndex(store.Tracks, paneIndex);
            if (target < 0) return;

            workspace.ActivePaneIndex = paneIndex;
            run(delegate
            {
                store.SwitchTo(editor, target);
                invalidate();
                report("Editing " + store.Tracks[target].Name + " in stock ADOFAI (group " + (paneIndex + 1) + ").");
            });
        }

        private static void DrawTrackSummary(TrackSlot track)
        {
            int floors = track.PreviewPositions != null && track.PreviewPositions.Count > 0
                ? track.PreviewPositions.Count
                : (track.Angles != null ? track.Angles.Count : 0);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Start F" + track.RegionStartFloor, GUILayout.Width(70f));
            GUILayout.Label("Cursor F" + track.CursorFloor, GUILayout.Width(76f));
            GUILayout.Label(floors + " floors", GUILayout.Width(70f));
            GUILayout.Label("Repeat " + track.EffectiveRepeatCount + "x", GUILayout.Width(74f));
            GUILayout.FlexibleSpace();
            GUILayout.Label(CompactLayoutPostProcessor.Describe(track));
            GUILayout.EndHorizontal();
        }

        private static void DrawPlanetSettings(TrackSlot track, Action invalidate)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("A", GUILayout.Width(14f));
            string oldA = track.PlanetATag ?? "";
            string newA = GUILayout.TextField(oldA, GUILayout.Width(92f));
            if (!string.Equals(oldA, newA, StringComparison.Ordinal))
            {
                track.PlanetATag = newA;
                invalidate();
            }

            GUILayout.Label("B", GUILayout.Width(14f));
            string oldB = track.PlanetBTag ?? "";
            string newB = GUILayout.TextField(oldB, GUILayout.Width(92f));
            if (!string.Equals(oldB, newB, StringComparison.Ordinal))
            {
                track.PlanetBTag = newB;
                invalidate();
            }

            GUILayout.Label("Pivot " + (track.PivotIsA ? "A" : "B"), GUILayout.Width(56f));
            if (GUILayout.Button("Swap", GUILayout.Width(48f)))
            {
                track.PivotIsA = !track.PivotIsA;
                invalidate();
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private static void DrawCompactLayoutSettings(TrackSlot track, Action invalidate)
        {
            bool changed = false;

            GUILayout.BeginHorizontal();
            GUILayout.Label("Layout", GUILayout.Width(42f));
            if (GUILayout.Button(track.WrapMode == CompactWrapMode.Off ? ">Off" : "Off", GUILayout.Width(42f)))
            {
                if (track.WrapMode != CompactWrapMode.Off) changed = true;
                track.WrapMode = CompactWrapMode.Off;
            }
            if (GUILayout.Button(track.WrapMode == CompactWrapMode.Tiles ? ">Tiles" : "Tiles", GUILayout.Width(50f)))
            {
                if (track.WrapMode != CompactWrapMode.Tiles) changed = true;
                track.WrapMode = CompactWrapMode.Tiles;
            }
            if (GUILayout.Button(track.WrapMode == CompactWrapMode.Beats ? ">Beats" : "Beats", GUILayout.Width(52f)))
            {
                if (track.WrapMode != CompactWrapMode.Beats) changed = true;
                track.WrapMode = CompactWrapMode.Beats;
            }

            if (track.WrapMode == CompactWrapMode.Tiles)
            {
                track.WrapTilesText = GUILayout.TextField(track.WrapTilesText ?? "", GUILayout.Width(42f));
                int value;
                if (int.TryParse(track.WrapTilesText, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value > 0 && value != track.WrapEveryTiles)
                {
                    track.WrapEveryTiles = value;
                    changed = true;
                }
                GUILayout.Label("t", GUILayout.Width(12f));
            }
            else if (track.WrapMode == CompactWrapMode.Beats)
            {
                track.WrapBeatsText = GUILayout.TextField(track.WrapBeatsText ?? "", GUILayout.Width(42f));
                double value;
                if (double.TryParse(track.WrapBeatsText, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                    && value > 0.0 && !double.IsNaN(value) && !double.IsInfinity(value)
                    && Math.Abs(value - track.WrapEveryBeats) > 1e-9)
                {
                    track.WrapEveryBeats = value;
                    changed = true;
                }
                GUILayout.Label("b", GUILayout.Width(12f));
            }

            GUILayout.Space(4f);
            GUILayout.Label("Repeat", GUILayout.Width(43f));
            track.RepeatCountText = GUILayout.TextField(track.RepeatCountText ?? "", GUILayout.Width(38f));
            int repeats;
            if (int.TryParse(track.RepeatCountText, NumberStyles.Integer, CultureInfo.InvariantCulture, out repeats)
                && repeats > 0 && repeats != track.RepeatCount)
            {
                track.RepeatCount = repeats;
                changed = true;
            }
            GUILayout.Label("x", GUILayout.Width(12f));

            bool reuse = GUILayout.Toggle(track.ReuseRepeatPath, "reuse", GUILayout.Width(58f));
            if (reuse != track.ReuseRepeatPath)
            {
                track.ReuseRepeatPath = reuse;
                changed = true;
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            if (changed) invalidate();
        }
    }
}
