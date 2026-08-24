using System;
using System.Globalization;
using UnityEngine;
using UnityModManagerNet;

namespace KineticNapier.ADOFAIMultiTileEditor
{
    public static class Main
    {
        internal const string ModVersion = "0.10.10";

        private static UnityModManager.ModEntry.ModLogger logger;
        private static readonly TrackStore store = new TrackStore();

        private static bool enabled;
        private static string newTrackName = "";
        private static Vector2 planScroll;
        private static scnEditor lastEditor;
        private static string status = "Select the floor where Multi Tile should begin, then store each source chart as a track.";
        private static bool lastActionFailed;
        private static bool showAdvanced;
        private static MultiTileOverlay overlay;

        private static GenerationPlan lastPlan;
        private static MasterPathPreview lastPathPreview;

        internal static bool OverlayCanDraw
        {
            get { return enabled && ADOBase.editor != null; }
        }

        public static bool Load(UnityModManager.ModEntry entry)
        {
            logger = entry.Logger;
            entry.OnToggle = OnToggle;
            entry.OnGUI = OnGUI;
            entry.OnUpdate = OnUpdate;
            EnsureOverlay();
            logger.Log("ADOFAI Multi Tile Editor v" + ModVersion + " loaded.");
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
                InvalidatePlan();
                status = "Editor instance changed; track queue was cleared.";
                lastActionFailed = false;
            }

            lastEditor = editor;
        }

        private static void OnGUI(UnityModManager.ModEntry entry)
        {
            GUILayout.Label("Multi Tile Editor v" + ModVersion);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Workbench panes", GUILayout.Width(100f));
            if (overlay != null)
            {
                if (GUILayout.Button(overlay.Visible ? "Hide" : "Show", GUILayout.Width(65f)))
                    overlay.Visible = !overlay.Visible;
            }
            else
            {
                GUILayout.Label("unavailable");
            }
            GUILayout.EndHorizontal();

            scnEditor editor = ADOBase.editor;
            if (editor == null)
            {
                GUILayout.Label("Level editor is not active.");
                return;
            }

            DrawTrackTabs(editor);
            GUILayout.Space(5f);

            if (store.ActiveIndex >= 0 && store.ActiveIndex < store.Tracks.Count)
                DrawActiveTrack(editor, store.Tracks[store.ActiveIndex], store.ActiveIndex);
            else if (store.Tracks.Count > 0)
                GUILayout.Label("Generated output is detached. Choose a track tab to continue editing a source track.");
            else
                GUILayout.Label("Choose the Multi Tile start floor, then store the current chart as the first source track.");

            GUILayout.Space(8f);
            DrawGenerationControls(editor);

            GUILayout.Space(6f);
            DrawStatus();

            GUILayout.Space(4f);
            if (GUILayout.Button(showAdvanced ? "Hide advanced / diagnostics" : "Show advanced / diagnostics", GUILayout.Width(210f)))
                showAdvanced = !showAdvanced;

            if (showAdvanced)
                DrawAdvanced(editor);
        }

        internal static void DrawOverlayContents()
        {
            scnEditor editor = ADOBase.editor;
            if (editor == null)
            {
                GUILayout.Label("Level editor is not active.");
                return;
            }

            DrawTrackTabs(editor);
            GUILayout.Space(4f);

            if (store.ActiveIndex >= 0 && store.ActiveIndex < store.Tracks.Count)
                DrawActiveTrack(editor, store.Tracks[store.ActiveIndex], store.ActiveIndex);
            else if (store.Tracks.Count > 0)
                GUILayout.Label("Output detached. Select a track tab to resume source editing.");
            else
                GUILayout.Label("Select the Multi Tile start floor, then store the first source track.");

            GUILayout.Space(5f);
            DrawGenerationControls(editor);
            GUILayout.Space(4f);
            DrawStatus();
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

        private static void DrawTrackTabs(scnEditor editor)
        {
            const int tabsPerRow = 5;
            for (int rowStart = 0; rowStart < store.Tracks.Count; rowStart += tabsPerRow)
            {
                GUILayout.BeginHorizontal();
                int rowEnd = Math.Min(store.Tracks.Count, rowStart + tabsPerRow);
                for (int i = rowStart; i < rowEnd; i++)
                {
                    TrackSlot track = store.Tracks[i];
                    string label = (i == store.ActiveIndex ? "> " : "") + ShortTrackName(track, i);

                    GUI.enabled = i != store.ActiveIndex;
                    if (GUILayout.Button(label, GUILayout.Width(112f)))
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
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("New track", GUILayout.Width(70f));
            newTrackName = GUILayout.TextField(newTrackName, GUILayout.Width(150f));
            if (GUILayout.Button("+ Store current", GUILayout.Width(125f)))
            {
                Try(delegate
                {
                    int index = store.StoreCurrent(editor, newTrackName);
                    newTrackName = "";
                    InvalidatePlan();
                    status = "Stored " + store.Tracks[index].Name + " with Multi Tile start F"
                        + store.Tracks[index].RegionStartFloor + ".";
                });
            }
            GUILayout.EndHorizontal();
        }

        private static void DrawActiveTrack(scnEditor editor, TrackSlot track, int index)
        {
            GUILayout.BeginVertical("box");

            GUILayout.BeginHorizontal();
            GUILayout.Label("Track", GUILayout.Width(42f));

            string oldName = track.Name ?? ("Track " + (index + 1));
            string newName = GUILayout.TextField(oldName, GUILayout.Width(170f));
            if (!string.Equals(oldName, newName, StringComparison.Ordinal))
                track.Name = newName;

            GUILayout.Label("Start F" + track.RegionStartFloor, GUILayout.Width(72f));
            GUILayout.Label("Cursor F" + track.CursorFloor, GUILayout.Width(78f));

            AngleSample angle = track.CurrentAngle;
            GUILayout.Label(angle.Valid ? angle.Degrees.ToString("0.###") + "°" : "angle ?", GUILayout.Width(78f));

            string count = track.Data != null && track.Data.angleData != null
                ? track.Data.angleData.Count + " angles"
                : "empty";
            GUILayout.Label(count);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Planet A", GUILayout.Width(58f));
            string oldA = track.PlanetATag ?? "";
            string newA = GUILayout.TextField(oldA, GUILayout.Width(105f));
            if (!string.Equals(oldA, newA, StringComparison.Ordinal))
            {
                track.PlanetATag = newA;
                InvalidatePlan();
            }

            GUILayout.Label("Planet B", GUILayout.Width(58f));
            string oldB = track.PlanetBTag ?? "";
            string newB = GUILayout.TextField(oldB, GUILayout.Width(105f));
            if (!string.Equals(oldB, newB, StringComparison.Ordinal))
            {
                track.PlanetBTag = newB;
                InvalidatePlan();
            }

            GUILayout.Label("Initial pivot: " + (track.PivotIsA ? "A" : "B"), GUILayout.Width(92f));
            if (GUILayout.Button("Swap", GUILayout.Width(55f)))
            {
                track.PivotIsA = !track.PivotIsA;
                InvalidatePlan();
            }
            GUILayout.EndHorizontal();

            DrawLayoutSettings(track);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Save track", GUILayout.Width(110f)))
            {
                Try(delegate
                {
                    store.SaveActive(editor);
                    InvalidatePlan();
                    status = "Saved " + track.Name + ". Region start F" + track.RegionStartFloor + " was preserved.";
                });
            }

            if (GUILayout.Button("Set start from selection", GUILayout.Width(165f)))
            {
                Try(delegate
                {
                    store.SetActiveRegionStartFromSelection(editor);
                    InvalidatePlan();
                    status = "Set " + track.Name + " Multi Tile start to F" + track.RegionStartFloor + ".";
                });
            }

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Delete track", GUILayout.Width(100f)))
            {
                int target = index;
                Try(delegate
                {
                    store.Remove(editor, target);
                    InvalidatePlan();
                    status = "Removed track.";
                });
            }
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
        }

        private static void DrawLayoutSettings(TrackSlot track)
        {
            GUILayout.Space(3f);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Layout", GUILayout.Width(58f));

            if (GUILayout.Button(track.WrapMode == CompactWrapMode.Off ? "> Off" : "Off", GUILayout.Width(58f)))
                track.WrapMode = CompactWrapMode.Off;
            if (GUILayout.Button(track.WrapMode == CompactWrapMode.Tiles ? "> Tiles" : "Tiles", GUILayout.Width(66f)))
                track.WrapMode = CompactWrapMode.Tiles;
            if (GUILayout.Button(track.WrapMode == CompactWrapMode.Beats ? "> Beats" : "Beats", GUILayout.Width(68f)))
                track.WrapMode = CompactWrapMode.Beats;

            if (track.WrapMode == CompactWrapMode.Tiles)
            {
                GUILayout.Label("Length", GUILayout.Width(45f));
                track.WrapTilesText = GUILayout.TextField(track.WrapTilesText ?? "", GUILayout.Width(58f));
                int value;
                if (int.TryParse(track.WrapTilesText, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value > 0)
                    track.WrapEveryTiles = value;
                GUILayout.Label("tiles", GUILayout.Width(35f));
            }
            else if (track.WrapMode == CompactWrapMode.Beats)
            {
                GUILayout.Label("Length", GUILayout.Width(45f));
                track.WrapBeatsText = GUILayout.TextField(track.WrapBeatsText ?? "", GUILayout.Width(58f));
                double value;
                if (double.TryParse(track.WrapBeatsText, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                    && value > 0.0 && !double.IsNaN(value) && !double.IsInfinity(value))
                    track.WrapEveryBeats = value;
                GUILayout.Label("beats", GUILayout.Width(40f));
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Virtual repeat", GUILayout.Width(88f));
            track.RepeatCountText = GUILayout.TextField(track.RepeatCountText ?? "", GUILayout.Width(58f));
            int repeats;
            if (int.TryParse(track.RepeatCountText, NumberStyles.Integer, CultureInfo.InvariantCulture, out repeats) && repeats > 0)
                track.RepeatCount = repeats;
            GUILayout.Label("x", GUILayout.Width(16f));

            bool reuse = GUILayout.Toggle(track.ReuseRepeatPath, "Return to first tile / reuse one source cycle", GUILayout.Width(280f));
            track.ReuseRepeatPath = reuse;
            GUILayout.FlexibleSpace();
            GUILayout.Label(CompactLayoutPostProcessor.Describe(track));
            GUILayout.EndHorizontal();

            if (track.WrapMode == CompactWrapMode.Off)
                GUILayout.Label("Layout folding is off; Position Track and virtual repeat returns still use instant planet teleports.");
        }

        private static void DrawGenerationControls(scnEditor editor)
        {
            string state;
            if (lastPlan == null) state = "Not analyzed";
            else if (lastPathPreview == null) state = "Analyzed";
            else state = "Ready";

            GUILayout.BeginHorizontal();
            GUILayout.Label("Generation", GUILayout.Width(72f));
            GUILayout.Label(state, GUILayout.Width(95f));

            GUI.enabled = store.ActiveIndex >= 0 && store.Tracks.Count > 0;
            if (GUILayout.Button("Analyze + Verify", GUILayout.Width(145f)))
            {
                Try(delegate
                {
                    store.SaveActive(editor);
                    lastPlan = TrackAnalyzer.BuildPlan(editor, store.Tracks);
                    lastPathPreview = MasterPathBuilder.BuildAndVerify(editor, lastPlan);
                    status = "Ready: " + lastPlan.Tracks.Count + " tracks, "
                        + lastPlan.Anchors.Count + " master anchors, "
                        + (lastPlan.EndSeconds - lastPlan.StartSeconds).ToString("0.###") + " sec.";
                });
            }

            GUI.enabled = lastPlan != null && lastPathPreview != null;
            if (GUILayout.Button("Generate Multi Tile", GUILayout.Width(155f)))
            {
                Try(delegate
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
                    status = "Generated successfully. "
                        + orbitResult.Emitted + " Orbit action(s), "
                        + tileResult.Created + " Floor decoration(s). "
                        + previewFinish + " " + compactFinish
                        + " Output is detached; choose a track tab to resume source editing.";
                });
            }

            GUI.enabled = lastPlan != null || lastPathPreview != null;
            if (GUILayout.Button("Clear", GUILayout.Width(65f)))
            {
                InvalidatePlan();
                status = "Cleared analyzed plan.";
                lastActionFailed = false;
            }

            GUI.enabled = true;
            GUILayout.EndHorizontal();

            if (lastPlan != null)
            {
                GUILayout.Label(
                    "Start F" + lastPlan.RegionStartFloor
                    + "   Tracks " + lastPlan.Tracks.Count
                    + "   Duration " + (lastPlan.EndSeconds - lastPlan.StartSeconds).ToString("0.###") + " sec"
                    + "   Master " + lastPlan.MasterBpm.ToString("0.###") + " BPM"
                    + "   Layout/repeat per group");
            }
        }

        private static void DrawStatus()
        {
            string prefix = lastActionFailed ? "ERROR: " : "Status: ";
            GUILayout.Label(prefix + status);
        }

        private static void DrawAdvanced(scnEditor editor)
        {
            GUILayout.Space(6f);
            GUILayout.BeginVertical("box");

            string editorBinding = store.ActiveIndex >= 0
                ? "source track #" + (store.ActiveIndex + 1)
                : "detached output/base";
            GUILayout.Label("Editor binding: " + editorBinding);
            GUILayout.Label("Each planet group has its own Off/Tiles/Beats layout length and virtual-repeat settings. Position Track and layout jumps use instant rigid planet teleports.");
            GUILayout.Label("Virtual repeat expands timing/orbits from one stored source cycle; between cycles the group returns to that cycle's first tile and reuses the same Floor preview.");
            GUILayout.Label("Pause / Hold / FreeRoam / MultiPlanet remain unsupported.");

            GUILayout.BeginHorizontal();
            GUI.enabled = store.ActiveIndex >= 0;
            if (GUILayout.Button("Analyze only", GUILayout.Width(105f)))
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
            if (GUILayout.Button("Verify only", GUILayout.Width(105f)))
            {
                Try(delegate
                {
                    lastPathPreview = MasterPathBuilder.BuildAndVerify(editor, lastPlan);
                    status = lastPathPreview.Diagnostic;
                });
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            if (lastPlan != null) DrawPlan(lastPlan);
            if (lastPathPreview != null) DrawMasterPathPreview(lastPlan, lastPathPreview);

            GUILayout.Space(4f);
            GUILayout.Label("Full diagnostic:");
            GUILayout.Label(status);

            GUILayout.EndVertical();
        }

        private static string ShortTrackName(TrackSlot track, int index)
        {
            string name = track != null ? track.Name : null;
            if (string.IsNullOrWhiteSpace(name)) return "Track " + (index + 1);
            name = name.Trim();
            return name.Length <= 11 ? name : name.Substring(0, 10) + "...";
        }

        private static void DrawPlan(GenerationPlan plan)
        {
            GUILayout.Space(6f);
            GUILayout.Label(
                "Plan: start F" + plan.RegionStartFloor
                + ", " + plan.Tracks.Count + " tracks"
                + ", " + plan.Anchors.Count + " anchors"
                + ", " + (plan.EndBeat - plan.StartBeat).ToString("0.######") + " master beats"
                + " / " + (plan.EndSeconds - plan.StartSeconds).ToString("0.######") + " sec"
                + " @ " + plan.MasterBpm.ToString("0.######") + " BPM");

            planScroll = GUILayout.BeginScrollView(planScroll, GUILayout.Height(280f));

            for (int t = 0; t < plan.Tracks.Count; t++)
            {
                AnalyzedTrack track = plan.Tracks[t];
                GUILayout.Label(
                    track.Name + ": start F" + track.RegionStartFloor
                    + ", " + track.Segments.Count + " segments"
                    + ", " + track.StartBeat.ToString("0.######")
                    + " -> " + track.EndBeat.ToString("0.######") + " master beats"
                    + "; base BPM=" + track.BaseBpm.ToString("0.######"));

                int shown = Math.Min(track.Segments.Count, 16);
                for (int s = 0; s < shown; s++)
                {
                    TrackSegment seg = track.Segments[s];
                    GUILayout.Label(
                        "  #" + s + " F" + seg.SourceFloor
                        + "  " + (seg.StartBeat - plan.StartBeat).ToString("0.######")
                        + " -> " + (seg.EndBeat - plan.StartBeat).ToString("0.######")
                        + "  dur=" + seg.DurationBeats.ToString("0.######") + " beat / "
                        + seg.DurationSeconds.ToString("0.######") + " sec  "
                        + seg.MovingTag + " around " + seg.CenterTag
                        + "  amount=" + seg.AmountDegrees.ToString("0.###") + "°"
                        + "  @M" + seg.MasterAnchorIndex);
                }

                if (track.Segments.Count > shown)
                    GUILayout.Label("  ... " + (track.Segments.Count - shown) + " more segment(s)");
            }

            GUILayout.Label("Master anchors:");
            int anchorShown = Math.Min(plan.Anchors.Count, 40);
            for (int i = 0; i < anchorShown; i++)
            {
                MasterAnchor anchor = plan.Anchors[i];
                GUILayout.Label(
                    "  M" + i + " -> output F" + (plan.RegionStartFloor + i)
                    + " = " + (anchor.Beat - plan.StartBeat).ToString("0.######")
                    + "    starts " + anchor.StartingSegments.Count + " orbit(s)");
            }

            if (plan.Anchors.Count > anchorShown)
                GUILayout.Label("  ... " + (plan.Anchors.Count - anchorShown) + " more anchor(s)");

            GUILayout.EndScrollView();
        }

        private static void DrawMasterPathPreview(GenerationPlan plan, MasterPathPreview preview)
        {
            GUILayout.Space(6f);
            GUILayout.Label(
                "Verification: " + preview.AngleData.Count + " angleData value(s), "
                + preview.RuntimeFloorCount + " anchor floor(s), max angle error="
                + preview.MaxAngleErrorDegrees.ToString("0.######") + "°, max beat error="
                + preview.MaxBeatError.ToString("0.0e+0"));

            int shown = Math.Min(preview.AngleData.Count, 32);
            for (int i = 0; i < shown; i++)
            {
                double travel = (plan.Anchors[i + 1].Beat - plan.Anchors[i].Beat) * 180.0;
                GUILayout.Label(
                    "  A" + i + " heading=" + preview.AngleData[i].ToString("0.######")
                    + "°  travel=" + travel.ToString("0.######") + "°"
                    + "  F" + (plan.RegionStartFloor + i)
                    + " -> F" + (plan.RegionStartFloor + i + 1));
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
            try
            {
                lastActionFailed = false;
                action();
            }
            catch (Exception ex)
            {
                lastActionFailed = true;
                status = ex.GetType().Name + ": " + ex.Message;
                if (logger != null) logger.Error(ex.ToString());
            }
        }
    }
}
