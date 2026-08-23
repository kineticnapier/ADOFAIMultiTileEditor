using System;
using System.Globalization;
using UnityEngine;

namespace KineticNapier.ADOFAIMultiTileEditor
{
    internal static class WorkspaceGui
    {
        private static readonly MultiTileWorkspace workspace = new MultiTileWorkspace();
        private static string newTrackName = "";
        private static string status = "Store source charts, then arrange them into editor groups.";
        private static bool lastActionFailed;
        private static scnEditor lastEditor;
        private static GenerationPlan lastPlan;
        private static MasterPathPreview lastPathPreview;

        internal static void DrawWindow(scnEditor editor, float windowWidth, float windowHeight)
        {
            if (editor == null)
            {
                GUILayout.Label("Level editor is not active.");
                return;
            }

            TrackStore store = TrackStore.Current;
            if (store == null)
            {
                GUILayout.Label("Track store is not initialized yet.");
                return;
            }

            if (!ReferenceEquals(lastEditor, editor))
            {
                lastEditor = editor;
                Invalidate();
                status = "Workspace attached to the active ADOFAI editor.";
                lastActionFailed = false;
            }

            Draw(editor, store, workspace, windowWidth, windowHeight, Invalidate, Report, Run);
            GUILayout.Space(5f);
            DrawGenerationControls(editor, store);
            GUILayout.Space(3f);
            DrawStatus();
        }

        private static void Draw(
            scnEditor editor,
            TrackStore store,
            MultiTileWorkspace workspaceState,
            float windowWidth,
            float windowHeight,
            Action invalidate,
            Action<string> report,
            Action<Action> run)
        {
            if (editor == null || store == null || workspaceState == null) return;

            workspaceState.EnsureAssignments(store.Tracks);
            DrawToolbar(editor, store, workspaceState, invalidate, report, run);
            GUILayout.Space(4f);

            float available = Mathf.Max(220f, windowHeight - 185f);
            switch (workspaceState.LayoutMode)
            {
                case WorkspaceLayoutMode.Single:
                    DrawPane(editor, store, workspaceState, 0, available, invalidate, report, run);
                    break;

                case WorkspaceLayoutMode.TwoColumns:
                    GUILayout.BeginHorizontal();
                    DrawPane(editor, store, workspaceState, 0, available, invalidate, report, run);
                    DrawPane(editor, store, workspaceState, 1, available, invalidate, report, run);
                    GUILayout.EndHorizontal();
                    break;

                case WorkspaceLayoutMode.TwoRows:
                {
                    float paneHeight = Mathf.Max(110f, (available - 5f) * 0.5f);
                    DrawPane(editor, store, workspaceState, 0, paneHeight, invalidate, report, run);
                    DrawPane(editor, store, workspaceState, 1, paneHeight, invalidate, report, run);
                    break;
                }

                default:
                {
                    float paneHeight = Mathf.Max(110f, (available - 5f) * 0.5f);
                    GUILayout.BeginHorizontal();
                    DrawPane(editor, store, workspaceState, 0, paneHeight, invalidate, report, run);
                    DrawPane(editor, store, workspaceState, 1, paneHeight, invalidate, report, run);
                    GUILayout.EndHorizontal();
                    GUILayout.BeginHorizontal();
                    DrawPane(editor, store, workspaceState, 2, paneHeight, invalidate, report, run);
                    DrawPane(editor, store, workspaceState, 3, paneHeight, invalidate, report, run);
                    GUILayout.EndHorizontal();
                    break;
                }
            }
        }

        private static void DrawToolbar(
            scnEditor editor,
            TrackStore store,
            MultiTileWorkspace workspaceState,
            Action invalidate,
            Action<string> report,
            Action<Action> run)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Editor groups", GUILayout.Width(82f));

            DrawLayoutButton(workspaceState, WorkspaceLayoutMode.Single, "1", 34f, store);
            DrawLayoutButton(workspaceState, WorkspaceLayoutMode.TwoColumns, "1|2", 46f, store);
            DrawLayoutButton(workspaceState, WorkspaceLayoutMode.TwoRows, "1/2", 46f, store);
            DrawLayoutButton(workspaceState, WorkspaceLayoutMode.Grid2x2, "2x2", 48f, store);

            GUILayout.Space(8f);
            GUILayout.Label("New track", GUILayout.Width(62f));
            newTrackName = GUILayout.TextField(newTrackName ?? "", GUILayout.Width(150f));
            if (GUILayout.Button("+ Store current", GUILayout.Width(125f)))
            {
                run(delegate
                {
                    int index = store.StoreCurrent(editor, newTrackName);
                    newTrackName = "";
                    workspaceState.AssignToActivePane(store.Tracks[index]);
                    workspaceState.EnsureAssignments(store.Tracks);
                    invalidate();
                    report("Stored " + store.Tracks[index].Name + " in editor group " + (workspaceState.ActivePaneIndex + 1) + ".");
                });
            }

            GUILayout.FlexibleSpace();
            GUILayout.Label(store.Tracks.Count + " track(s)", GUILayout.Width(72f));
            GUILayout.EndHorizontal();
        }

        private static void DrawLayoutButton(
            MultiTileWorkspace workspaceState,
            WorkspaceLayoutMode mode,
            string label,
            float width,
            TrackStore store)
        {
            string text = workspaceState.LayoutMode == mode ? "> " + label : label;
            if (GUILayout.Button(text, GUILayout.Width(width)))
                workspaceState.SetLayout(mode, store.Tracks);
        }

        private static void DrawPane(
            scnEditor editor,
            TrackStore store,
            MultiTileWorkspace workspaceState,
            int paneIndex,
            float paneHeight,
            Action invalidate,
            Action<string> report,
            Action<Action> run)
        {
            WorkspacePaneState pane = workspaceState.GetPane(paneIndex);
            TrackSlot track = pane.Track;
            int trackIndex = workspaceState.GetTrackIndex(store.Tracks, paneIndex);
            bool active = trackIndex >= 0 && trackIndex == store.ActiveIndex && workspaceState.ActivePaneIndex == paneIndex;

            GUILayout.BeginVertical("box", GUILayout.Height(paneHeight), GUILayout.ExpandWidth(true));

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(active ? "ACTIVE" : "Edit", GUILayout.Width(55f)))
                ActivatePane(editor, store, workspaceState, paneIndex, invalidate, report, run);

            GUI.enabled = store.Tracks.Count > 1;
            if (GUILayout.Button("<", GUILayout.Width(24f)))
            {
                workspaceState.CycleTrack(store.Tracks, paneIndex, -1);
                track = workspaceState.GetPane(paneIndex).Track;
                trackIndex = workspaceState.GetTrackIndex(store.Tracks, paneIndex);
                if (workspaceState.ActivePaneIndex == paneIndex)
                    ActivatePane(editor, store, workspaceState, paneIndex, invalidate, report, run);
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
                workspaceState.CycleTrack(store.Tracks, paneIndex, 1);
                track = workspaceState.GetPane(paneIndex).Track;
                trackIndex = workspaceState.GetTrackIndex(store.Tracks, paneIndex);
                if (workspaceState.ActivePaneIndex == paneIndex)
                    ActivatePane(editor, store, workspaceState, paneIndex, invalidate, report, run);
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

            active = trackIndex == store.ActiveIndex && workspaceState.ActivePaneIndex == paneIndex;
            float previewHeight = Mathf.Max(62f, paneHeight - 142f);
            Rect preview = GUILayoutUtility.GetRect(80f, previewHeight, GUILayout.ExpandWidth(true));
            WorkspacePreviewRenderer.Draw(track, preview, active);

            Event ev = Event.current;
            if (ev != null && ev.type == EventType.MouseDown && ev.button == 0 && preview.Contains(ev.mousePosition))
            {
                ActivatePane(editor, store, workspaceState, paneIndex, invalidate, report, run);
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
                    workspaceState.EnsureAssignments(store.Tracks);
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
            MultiTileWorkspace workspaceState,
            int paneIndex,
            Action invalidate,
            Action<string> report,
            Action<Action> run)
        {
            int target = workspaceState.GetTrackIndex(store.Tracks, paneIndex);
            if (target < 0) return;

            workspaceState.ActivePaneIndex = paneIndex;
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

        private static void DrawGenerationControls(scnEditor editor, TrackStore store)
        {
            string state;
            if (lastPlan == null) state = "Not analyzed";
            else if (lastPathPreview == null) state = "Analyzed";
            else state = "Ready";

            GUILayout.BeginHorizontal();
            GUILayout.Label("Generation", GUILayout.Width(72f));
            GUILayout.Label(state, GUILayout.Width(92f));

            GUI.enabled = store.ActiveIndex >= 0 && store.Tracks.Count > 0;
            if (GUILayout.Button("Analyze + Verify", GUILayout.Width(140f)))
            {
                Run(delegate
                {
                    store.SaveActive(editor);
                    lastPlan = TrackAnalyzer.BuildPlan(editor, store.Tracks);
                    lastPathPreview = MasterPathBuilder.BuildAndVerify(editor, lastPlan);
                    Report("Ready: " + lastPlan.Tracks.Count + " tracks, "
                        + lastPlan.Anchors.Count + " master anchors, "
                        + (lastPlan.EndSeconds - lastPlan.StartSeconds).ToString("0.###") + " sec.");
                });
            }

            GUI.enabled = lastPlan != null && lastPathPreview != null;
            if (GUILayout.Button("Generate Multi Tile", GUILayout.Width(150f)))
            {
                Run(delegate
                {
                    int baseTrackIndex = store.ActiveIndex;
                    OrbitCommitResult orbitResult = MasterOutputGenerator.GenerateAndCommit(
                        editor, lastPlan, lastPathPreview, store.Tracks, baseTrackIndex);
                    TileDecorationResult tileResult = FixedTileDecorationGenerator.GenerateAndCommit(
                        editor, store.Tracks, lastPlan);
                    string previewFinish = TilePreviewPostProcessor.ApplyAndCommit(
                        editor, store.Tracks, lastPlan);
                    string compactFinish = CompactLayoutPostProcessor.ApplyAndCommit(
                        editor, store.Tracks, lastPlan);

                    store.DetachActive();
                    Report("Generated: " + orbitResult.Emitted + " Orbit action(s), "
                        + tileResult.Created + " Floor decoration(s). "
                        + previewFinish + " " + compactFinish);
                });
            }

            GUI.enabled = lastPlan != null || lastPathPreview != null;
            if (GUILayout.Button("Clear", GUILayout.Width(60f)))
            {
                Invalidate();
                Report("Cleared analyzed plan.");
                lastActionFailed = false;
            }
            GUI.enabled = true;

            GUILayout.FlexibleSpace();
            if (lastPlan != null)
            {
                GUILayout.Label(
                    "F" + lastPlan.RegionStartFloor
                    + " / " + lastPlan.Tracks.Count + " tracks"
                    + " / " + lastPlan.MasterBpm.ToString("0.###") + " BPM");
            }
            GUILayout.EndHorizontal();
        }

        private static void DrawStatus()
        {
            GUILayout.Label((lastActionFailed ? "ERROR: " : "Status: ") + status);
        }

        private static void Invalidate()
        {
            lastPlan = null;
            lastPathPreview = null;
        }

        private static void Report(string value)
        {
            status = value ?? "";
        }

        private static void Run(Action action)
        {
            try
            {
                lastActionFailed = false;
                action();
            }
            catch (Exception ex)
            {
                lastActionFailed = true;
                status = ex.GetType().Name + ": " + ex.Message;
                Debug.LogError("ADOFAI Multi Tile Workspace: " + ex);
            }
        }
    }
}
