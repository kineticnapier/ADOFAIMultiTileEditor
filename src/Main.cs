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
        private static MasterPathPreview lastPathPreview;

        public static bool Load(UnityModManager.ModEntry entry)
        {
            logger = entry.Logger;
            entry.OnToggle = OnToggle;
            entry.OnGUI = OnGUI;
            entry.OnUpdate = OnUpdate;
            logger.Log("ADOFAI Multi Tile Editor Prototype v0.5.0 loaded.");
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
                InvalidatePlan();
                status = "Editor instance changed; track queue was cleared.";
            }
            lastEditor = editor;
        }

        private static void OnGUI(UnityModManager.ModEntry entry)
        {
            GUILayout.Label("Multi Tile Editor prototype v0.5.0 - master path verifier");
            scnEditor editor = ADOBase.editor;
            if (editor == null)
            {
                GUILayout.Label("Level editor is not active.");
                return;
            }

            GUILayout.Label("v0.5 keeps whole-region planning and adds a read-only MasterPathBuilder. It synthesizes angleData from the merged anchors, temporarily reconstructs it through the stock editor, verifies angleLength/entryBeat, then restores the active chart unchanged.");
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
                    InvalidatePlan();
                    status = "Stored current chart as track #" + (index + 1) + ".";
                });
            }
            if (GUILayout.Button("Save active", GUILayout.Width(100f)))
            {
                Try(delegate
                {
                    store.SaveActive(editor);
                    InvalidatePlan();
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
                        InvalidatePlan();
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
                        InvalidatePlan();
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
                    InvalidatePlan();
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
                    lastPathPreview = null;
                    status = lastPlan.Diagnostic;
                });
            }

            GUI.enabled = lastPlan != null;
            if (GUILayout.Button("Verify master path", GUILayout.Width(145f)))
            {
                Try(delegate
                {
                    lastPathPreview = MasterPathBuilder.BuildAndVerify(editor, lastPlan);
                    status = lastPathPreview.Diagnostic;
                });
            }
            if (GUILayout.Button("Clear plan", GUILayout.Width(90f)))
            {
                lastPlan = null;
                lastPathPreview = null;
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            if (lastPlan != null) DrawPlan(lastPlan);
            if (lastPathPreview != null) DrawMasterPathPreview(lastPlan, lastPathPreview);

            GUILayout.Space(4f);
            GUILayout.Label(status);
            GUILayout.Label("v0.5 is still read-only after planning/path verification. OrbitEmitter and final atomic commit are intentionally not implemented yet.");
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

        private static void DrawMasterPathPreview(GenerationPlan plan, MasterPathPreview preview)
        {
            GUILayout.Space(6f);
            GUILayout.Label("Master path verification: " + preview.AngleData.Count + " angleData value(s), "
                + preview.RuntimeFloorCount + " runtime anchor floor(s), max angle error="
                + preview.MaxAngleErrorDegrees.ToString("0.######") + "°, max beat error=" + preview.MaxBeatError.ToString("0.0e+0"));

            int shown = Math.Min(preview.AngleData.Count, 32);
            for (int i = 0; i < shown; i++)
            {
                double travel = (plan.Anchors[i + 1].Beat - plan.Anchors[i].Beat) * 180.0;
                GUILayout.Label("  A" + i + " heading=" + preview.AngleData[i].ToString("0.######")
                    + "°    travel=" + travel.ToString("0.######") + "°    M" + i + " -> M" + (i + 1));
            }
            if (preview.AngleData.Count > shown)
                GUILayout.Label("  ... " + (preview.AngleData.Count - shown) + " more angleData value(s)");
        }

        private static void InvalidatePlan()
        {
            lastPlan = null;
            lastPathPreview = null;
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
