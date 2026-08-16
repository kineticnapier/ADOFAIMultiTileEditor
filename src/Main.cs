using System;
using UnityEngine;
using UnityModManagerNet;

namespace KineticNapier.ADOFAIMultiTileEditor
{
    public static class Main
    {
        private static UnityModManager.ModEntry.ModLogger logger;
        private static readonly TrackStore store = new TrackStore();
        private static bool enabled;
        private static string newTrackName = "";
        private static Vector2 trackScroll;
        private static Vector2 planScroll;
        private static scnEditor lastEditor;
        private static string status = "Open a level in the editor, then store each source chart as a track.";
        private static GenerationPlan lastPlan;

        public static bool Load(UnityModManager.ModEntry entry)
        {
            logger = entry.Logger;
            entry.OnToggle = OnToggle;
            entry.OnGUI = OnGUI;
            entry.OnUpdate = OnUpdate;
            logger.Log("ADOFAI Multi Tile Editor Prototype v0.4.0 loaded.");
            return true;
        }

        private static bool OnToggle(UnityModManager.ModEntry entry, bool value)
        {
            enabled = value;
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
                lastPlan = null;
                status = "Editor instance changed; track queue was cleared.";
            }
            lastEditor = editor;
        }

        private static void OnGUI(UnityModManager.ModEntry entry)
        {
            GUILayout.Label("Multi Tile Editor prototype v0.4.0 - whole-region planner");
            scnEditor editor = ADOBase.editor;
            if (editor == null)
            {
                GUILayout.Label("Level editor is not active.");
                return;
            }

            GUILayout.Label("v0.4 removes the obsolete per-step generator. It reconstructs every stored source track, analyzes stock entryBeat/angleLength/isCCW data, and merges a master timeline without modifying the chart.");
            GUILayout.Space(6f);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Name", GUILayout.Width(50f));
            newTrackName = GUILayout.TextField(newTrackName, GUILayout.Width(180f));
            if (GUILayout.Button("Store current as track", GUILayout.Width(170f)))
            {
                Try(delegate
                {
                    int index = store.StoreCurrent(editor, newTrackName);
                    newTrackName = "";
                    lastPlan = null;
                    status = "Stored current chart as track #" + (index + 1) + ".";
                });
            }
            if (GUILayout.Button("Save active", GUILayout.Width(100f)))
            {
                Try(delegate
                {
                    store.SaveActive(editor);
                    lastPlan = null;
                    status = "Saved active source-track snapshot.";
                });
            }
            GUILayout.EndHorizontal();

            GUILayout.Label("Tracks: " + store.Tracks.Count + "    (cursor/angle below are editor diagnostics only; generation no longer advances cursors)");
            trackScroll = GUILayout.BeginScrollView(trackScroll, GUILayout.Height(Math.Min(360f, 52f + store.Tracks.Count * 64f)));
            for (int i = 0; i < store.Tracks.Count; i++)
            {
                TrackSlot track = store.Tracks[i];
                GUILayout.BeginHorizontal();
                GUILayout.Label(i == store.ActiveIndex ? "▶" : " ", GUILayout.Width(18f));
                track.Name = GUILayout.TextField(track.Name ?? ("Track " + (i + 1)), GUILayout.Width(125f));
                GUILayout.Label(track.Data != null && track.Data.angleData != null ? (track.Data.angleData.Count + " angles") : "empty", GUILayout.Width(82f));
                GUILayout.Label("F" + track.CursorFloor, GUILayout.Width(44f));
                AngleSample angle = track.CurrentAngle;
                GUILayout.Label(angle.Valid ? (angle.Degrees.ToString("0.###") + "°") : "?", GUILayout.Width(72f));

                GUI.enabled = i != store.ActiveIndex;
                if (GUILayout.Button("Switch", GUILayout.Width(62f)))
                {
                    int target = i;
                    Try(delegate
                    {
                        store.SwitchTo(editor, target);
                        lastPlan = null;
                        status = "Switched to " + store.Tracks[target].Name + ".";
                    });
                }
                GUI.enabled = true;

                if (GUILayout.Button("X", GUILayout.Width(28f)))
                {
                    int target = i;
                    Try(delegate
                    {
                        store.Remove(editor, target);
                        lastPlan = null;
                        status = "Removed track.";
                    });
                    GUILayout.EndHorizontal();
                    break;
                }
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Space(18f);
                GUILayout.Label("A tag", GUILayout.Width(40f));
                track.PlanetATag = GUILayout.TextField(track.PlanetATag ?? "", GUILayout.Width(90f));
                GUILayout.Label("B tag", GUILayout.Width(40f));
                track.PlanetBTag = GUILayout.TextField(track.PlanetBTag ?? "", GUILayout.Width(90f));
                GUILayout.Label("initial pivot: " + (track.PivotIsA ? "A" : "B"), GUILayout.Width(92f));
                if (GUILayout.Button("swap", GUILayout.Width(50f)))
                {
                    track.PivotIsA = !track.PivotIsA;
                    lastPlan = null;
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();

            GUILayout.Space(6f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Analyze whole-region plan", GUILayout.Width(210f)))
            {
                Try(delegate
                {
                    store.SaveActive(editor);
                    lastPlan = TrackAnalyzer.BuildPlan(editor, store.Tracks);
                    status = lastPlan.Diagnostic;
                });
            }
            GUI.enabled = lastPlan != null;
            if (GUILayout.Button("Clear plan", GUILayout.Width(90f))) lastPlan = null;
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            if (lastPlan != null) DrawPlan(lastPlan);

            GUILayout.Space(4f);
            GUILayout.Label(status);
            GUILayout.Label("v0.4 is intentionally read-only after planning. MasterPathBuilder + OrbitEmitter come next, after the analyzed rhythms/timeline match the golden sample.");
        }

        private static void DrawPlan(GenerationPlan plan)
        {
            GUILayout.Space(6f);
            GUILayout.Label("Plan summary: " + plan.Tracks.Count + " tracks, " + plan.Anchors.Count + " anchors, "
                + (plan.EndBeat - plan.StartBeat).ToString("0.######") + " beats. Timeline epsilon=" + TimelineMerger.BeatEpsilon.ToString("0.0e+0"));

            planScroll = GUILayout.BeginScrollView(planScroll, GUILayout.Height(300f));
            for (int t = 0; t < plan.Tracks.Count; t++)
            {
                AnalyzedTrack track = plan.Tracks[t];
                GUILayout.Label(track.Name + ": " + track.Segments.Count + " segments, "
                    + track.StartBeat.ToString("0.######") + " -> " + track.EndBeat.ToString("0.######") + " beats");
                int shown = Math.Min(track.Segments.Count, 16);
                for (int s = 0; s < shown; s++)
                {
                    TrackSegment seg = track.Segments[s];
                    GUILayout.Label("  #" + s + " F" + seg.SourceFloor + "  "
                        + (seg.StartBeat - plan.StartBeat).ToString("0.######") + " -> "
                        + (seg.EndBeat - plan.StartBeat).ToString("0.######") + "  dur="
                        + seg.DurationBeats.ToString("0.######") + "  " + seg.MovingTag + " around " + seg.CenterTag
                        + "  amount=" + seg.AmountDegrees.ToString("0.###") + "°  @M" + seg.MasterAnchorIndex);
                }
                if (track.Segments.Count > shown) GUILayout.Label("  ... " + (track.Segments.Count - shown) + " more segment(s)");
            }

            GUILayout.Label("Master anchors (relative beats):");
            int anchorShown = Math.Min(plan.Anchors.Count, 40);
            for (int i = 0; i < anchorShown; i++)
            {
                MasterAnchor anchor = plan.Anchors[i];
                GUILayout.Label("  M" + i + " = " + (anchor.Beat - plan.StartBeat).ToString("0.######")
                    + "    starts " + anchor.StartingSegments.Count + " orbit(s)");
            }
            if (plan.Anchors.Count > anchorShown) GUILayout.Label("  ... " + (plan.Anchors.Count - anchorShown) + " more anchor(s)");
            GUILayout.EndScrollView();
        }

        private static void Try(Action action)
        {
            try { action(); }
            catch (Exception ex)
            {
                status = ex.GetType().Name + ": " + ex.Message;
                if (logger != null) logger.Error(ex.ToString());
            }
        }
    }
}
