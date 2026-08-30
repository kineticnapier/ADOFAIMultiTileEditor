using System;
using System.Collections.Generic;
using System.Globalization;
using KineticNapier.ADOFAIWorkbench;

namespace KineticNapier.ADOFAIMultiTileEditor
{
    internal static class WorkbenchIntegration
    {
        private static readonly MultiTilePaneProvider Provider = new MultiTilePaneProvider();
        private static bool registered;
        private static int nextPublishFrame;
        private static string lastSignature;

        private static string newTrackName = "";
        private static GenerationPlan lastPlan;
        private static MasterPathPreview lastPathPreview;
        private static string status = "";
        private static bool lastActionFailed;
        private static bool showAdvanced;

        internal static void EnsureRegistered()
        {
            if (registered) return;
            Workbench.RegisterPaneProvider(Provider);
            registered = true;
            PublishNow(true);
            Workbench.OpenPane("mte.tracks");
            Workbench.OpenPane("mte.settings");
        }

        internal static void Unregister()
        {
            if (!registered) return;
            Workbench.UnregisterPaneProvider(Provider);
            registered = false;
            lastSignature = null;
        }

        internal static void Tick()
        {
            if (!registered || UnityEngine.Time.frameCount < nextPublishFrame) return;
            nextPublishFrame = UnityEngine.Time.frameCount + 10;
            PublishNow(false);
        }

        internal static void RefreshLanguage()
        {
            lastSignature = null;
            if (registered) PublishNow(true);
        }

        internal static void ResetGenerationState(string message)
        {
            lastPlan = null;
            lastPathPreview = null;
            lastActionFailed = false;
            if (!string.IsNullOrWhiteSpace(message)) status = message;
            lastSignature = null;
        }

        internal static void PublishNow(bool force)
        {
            MultiTileSnapshot snapshot = CaptureSnapshot();
            if (!force && string.Equals(snapshot.Signature, lastSignature, StringComparison.Ordinal)) return;
            lastSignature = snapshot.Signature;
            Provider.Publish(snapshot);
        }

        private static MultiTileSnapshot CaptureSnapshot()
        {
            var snapshot = new MultiTileSnapshot();
            TrackStore store = TrackStore.Current;
            scnEditor editor = ADOBase.editor;
            snapshot.EditorAvailable = editor != null;
            snapshot.ActiveIndex = store != null ? store.ActiveIndex : -1;
            snapshot.NewTrackName = newTrackName ?? "";
            snapshot.Status = string.IsNullOrWhiteSpace(status)
                ? MteLocalization.T("initialStatus", "Select the floor where Multi Tile should begin, then store each source chart as a track.")
                : status;
            snapshot.LastActionFailed = lastActionFailed;
            snapshot.ShowAdvanced = showAdvanced;

            if (store == null || store.Tracks.Count == 0)
            {
                lastPlan = null;
                lastPathPreview = null;
            }

            if (store != null)
            {
                for (int i = 0; i < store.Tracks.Count; i++)
                {
                    TrackSlot track = store.Tracks[i];
                    if (track == null) continue;
                    snapshot.Tracks.Add(new TrackSnapshot
                    {
                        Index = i,
                        Name = string.IsNullOrWhiteSpace(track.Name) ? "Track " + (i + 1) : track.Name,
                        RegionStartFloor = track.RegionStartFloor,
                        CursorFloor = track.CursorFloor,
                        Layout = CompactLayoutPostProcessor.Describe(track)
                    });
                }

                if (store.ActiveIndex >= 0 && store.ActiveIndex < store.Tracks.Count)
                {
                    TrackSlot active = store.Tracks[store.ActiveIndex];
                    if (active != null)
                    {
                        snapshot.ActiveName = string.IsNullOrWhiteSpace(active.Name) ? "Track " + (store.ActiveIndex + 1) : active.Name;
                        snapshot.RegionStartFloor = active.RegionStartFloor;
                        snapshot.CursorFloor = active.CursorFloor;
                        snapshot.PivotIsA = active.PivotIsA;
                        snapshot.WrapMode = active.WrapMode;
                        snapshot.WrapTilesText = active.WrapTilesText ?? active.WrapEveryTiles.ToString(CultureInfo.InvariantCulture);
                        snapshot.WrapBeatsText = active.WrapBeatsText ?? active.WrapEveryBeats.ToString(CultureInfo.InvariantCulture);
                        snapshot.RepeatCountText = active.RepeatCountText ?? active.RepeatCount.ToString(CultureInfo.InvariantCulture);
                        snapshot.RepeatCount = active.RepeatCount;
                        snapshot.ReuseRepeatPath = active.ReuseRepeatPath;
                        snapshot.PlanetATag = active.PlanetATag ?? "";
                        snapshot.PlanetBTag = active.PlanetBTag ?? "";
                        AngleSample angle = active.CurrentAngle;
                        snapshot.AngleText = angle.Valid
                            ? angle.Degrees.ToString("0.###", CultureInfo.InvariantCulture) + "°"
                            : MteLocalization.T("angleUnknown", "angle ?");
                        snapshot.AngleCountText = active.Data != null && active.Data.angleData != null
                            ? MteLocalization.F("angles", "{0} angles", active.Data.angleData.Count)
                            : MteLocalization.T("empty", "empty");
                    }
                }
            }

            if (lastPlan == null) snapshot.GenerationState = MteLocalization.T("state.notAnalyzed", "Not analyzed");
            else if (lastPathPreview == null) snapshot.GenerationState = MteLocalization.T("state.analyzed", "Analyzed");
            else snapshot.GenerationState = MteLocalization.T("state.ready", "Ready");

            snapshot.CanAnalyze = snapshot.EditorAvailable && snapshot.ActiveIndex >= 0 && snapshot.Tracks.Count > 0;
            snapshot.CanGenerate = lastPlan != null && lastPathPreview != null;
            snapshot.CanClear = lastPlan != null || lastPathPreview != null;

            if (lastPlan != null)
            {
                snapshot.PlanSummary = MteLocalization.F(
                    "planSummary",
                    "Start F{0}   Tracks {1}   Duration {2} sec   Master {3} BPM   Layout/repeat per group",
                    lastPlan.RegionStartFloor,
                    lastPlan.Tracks.Count,
                    (lastPlan.EndSeconds - lastPlan.StartSeconds).ToString("0.###", CultureInfo.InvariantCulture),
                    lastPlan.MasterBpm.ToString("0.###", CultureInfo.InvariantCulture));
            }

            if (showAdvanced) BuildAdvancedLines(snapshot);
            snapshot.BuildSignature();
            return snapshot;
        }

        private static void BuildAdvancedLines(MultiTileSnapshot snapshot)
        {
            snapshot.AdvancedLines.Add("Editor binding: " + (snapshot.ActiveIndex >= 0 ? "source track #" + (snapshot.ActiveIndex + 1) : "detached output/base"));
            snapshot.AdvancedLines.Add("Each planet group has its own Off/Tiles/Beats layout length and virtual-repeat settings. PositionTrack position and layout jumps use instant rigid planet teleports.");
            snapshot.AdvancedLines.Add("Virtual repeat expands timing/orbits from one stored source cycle; between cycles the group returns to that cycle's first tile and reuses the same Floor preview.");
            snapshot.AdvancedLines.Add(SourceEventTransfer.GetCompatibilitySummary());

            if (lastPlan != null)
            {
                snapshot.AdvancedLines.Add("Plan: start F" + lastPlan.RegionStartFloor
                    + ", " + lastPlan.Tracks.Count + " tracks"
                    + ", " + lastPlan.Anchors.Count + " anchors"
                    + ", " + (lastPlan.EndBeat - lastPlan.StartBeat).ToString("0.######", CultureInfo.InvariantCulture) + " master beats"
                    + " / " + (lastPlan.EndSeconds - lastPlan.StartSeconds).ToString("0.######", CultureInfo.InvariantCulture) + " sec"
                    + " @ " + lastPlan.MasterBpm.ToString("0.######", CultureInfo.InvariantCulture) + " BPM");

                for (int t = 0; t < lastPlan.Tracks.Count; t++)
                {
                    AnalyzedTrack track = lastPlan.Tracks[t];
                    snapshot.AdvancedLines.Add(track.Name + ": start F" + track.RegionStartFloor
                        + ", " + track.Segments.Count + " segments"
                        + ", " + track.StartBeat.ToString("0.######", CultureInfo.InvariantCulture)
                        + " -> " + track.EndBeat.ToString("0.######", CultureInfo.InvariantCulture) + " master beats"
                        + "; base BPM=" + track.BaseBpm.ToString("0.######", CultureInfo.InvariantCulture));
                    int shown = Math.Min(track.Segments.Count, 16);
                    for (int s = 0; s < shown; s++)
                    {
                        TrackSegment seg = track.Segments[s];
                        string pause = seg.PauseDurationBeats > TimelineMerger.BeatEpsilon
                            ? "  pause=" + seg.PauseDurationBeats.ToString("0.######", CultureInfo.InvariantCulture)
                                + " + motion=" + seg.MotionDurationBeats.ToString("0.######", CultureInfo.InvariantCulture)
                            : "";
                        snapshot.AdvancedLines.Add("  #" + s + " F" + seg.SourceFloor
                            + "  " + (seg.StartBeat - lastPlan.StartBeat).ToString("0.######", CultureInfo.InvariantCulture)
                            + " -> " + (seg.EndBeat - lastPlan.StartBeat).ToString("0.######", CultureInfo.InvariantCulture)
                            + "  dur=" + seg.DurationBeats.ToString("0.######", CultureInfo.InvariantCulture) + " beat / "
                            + seg.DurationSeconds.ToString("0.######", CultureInfo.InvariantCulture) + " sec"
                            + pause + "  " + seg.MovingTag + " around " + seg.CenterTag
                            + "  amount=" + seg.AmountDegrees.ToString("0.###", CultureInfo.InvariantCulture) + "°"
                            + "  @M" + seg.MasterAnchorIndex);
                    }
                    if (track.Segments.Count > shown) snapshot.AdvancedLines.Add("  ... " + (track.Segments.Count - shown) + " more segment(s)");
                }

                snapshot.AdvancedLines.Add("Master anchors:");
                int anchorShown = Math.Min(lastPlan.Anchors.Count, 40);
                for (int i = 0; i < anchorShown; i++)
                {
                    MasterAnchor anchor = lastPlan.Anchors[i];
                    snapshot.AdvancedLines.Add("  M" + i + " -> output F" + (lastPlan.RegionStartFloor + i)
                        + " = " + (anchor.Beat - lastPlan.StartBeat).ToString("0.######", CultureInfo.InvariantCulture)
                        + "    starts " + anchor.StartingSegments.Count + " orbit(s)");
                }
                if (lastPlan.Anchors.Count > anchorShown) snapshot.AdvancedLines.Add("  ... " + (lastPlan.Anchors.Count - anchorShown) + " more anchor(s)");
            }

            if (lastPlan != null && lastPathPreview != null)
            {
                snapshot.AdvancedLines.Add("Verification: " + lastPathPreview.AngleData.Count + " angleData value(s), "
                    + lastPathPreview.RuntimeFloorCount + " anchor floor(s), max angle error="
                    + lastPathPreview.MaxAngleErrorDegrees.ToString("0.######", CultureInfo.InvariantCulture) + "°, max beat error="
                    + lastPathPreview.MaxBeatError.ToString("0.0e+0", CultureInfo.InvariantCulture));
                int shown = Math.Min(lastPathPreview.AngleData.Count, 32);
                for (int i = 0; i < shown; i++)
                {
                    double travel = (lastPlan.Anchors[i + 1].Beat - lastPlan.Anchors[i].Beat) * 180.0;
                    snapshot.AdvancedLines.Add("  A" + i + " heading=" + lastPathPreview.AngleData[i].ToString("0.######", CultureInfo.InvariantCulture)
                        + "°  travel=" + travel.ToString("0.######", CultureInfo.InvariantCulture) + "°"
                        + "  F" + (lastPlan.RegionStartFloor + i) + " -> F" + (lastPlan.RegionStartFloor + i + 1));
                }
                if (lastPathPreview.AngleData.Count > shown) snapshot.AdvancedLines.Add("  ... " + (lastPathPreview.AngleData.Count - shown) + " more angleData value(s)");
            }
        }

        private static void InvalidatePlan()
        {
            lastPlan = null;
            lastPathPreview = null;
        }

        private static void TryAction(Action action)
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
            }
        }

        private static void AnalyzeAndVerify()
        {
            scnEditor editor = ADOBase.editor;
            TrackStore store = TrackStore.Current;
            if (editor == null || store == null || store.ActiveIndex < 0) return;
            TryAction(delegate
            {
                store.SaveActive(editor);
                lastPlan = TrackAnalyzer.BuildPlan(editor, store.Tracks);
                lastPathPreview = MasterPathBuilder.BuildAndVerify(editor, lastPlan);
                status = "Ready: " + lastPlan.Tracks.Count + " tracks, " + lastPlan.Anchors.Count + " master anchors, "
                    + (lastPlan.EndSeconds - lastPlan.StartSeconds).ToString("0.###", CultureInfo.InvariantCulture) + " sec.";
            });
        }

        private static void AnalyzeOnly()
        {
            scnEditor editor = ADOBase.editor;
            TrackStore store = TrackStore.Current;
            if (editor == null || store == null || store.ActiveIndex < 0) return;
            TryAction(delegate
            {
                store.SaveActive(editor);
                lastPlan = TrackAnalyzer.BuildPlan(editor, store.Tracks);
                lastPathPreview = null;
                status = lastPlan.Diagnostic;
            });
        }

        private static void VerifyOnly()
        {
            scnEditor editor = ADOBase.editor;
            if (editor == null || lastPlan == null) return;
            TryAction(delegate
            {
                lastPathPreview = MasterPathBuilder.BuildAndVerify(editor, lastPlan);
                status = lastPathPreview.Diagnostic;
            });
        }

        private static void Generate()
        {
            scnEditor editor = ADOBase.editor;
            TrackStore store = TrackStore.Current;
            if (editor == null || store == null || lastPlan == null || lastPathPreview == null) return;
            TryAction(delegate
            {
                int baseTrackIndex = store.ActiveIndex;
                OrbitCommitResult orbitResult = MasterOutputGenerator.GenerateAndCommit(editor, lastPlan, lastPathPreview, store.Tracks, baseTrackIndex);
                TileDecorationResult tileResult = FixedTileDecorationGenerator.GenerateAndCommit(editor, store.Tracks, lastPlan);
                string previewFinish = TilePreviewPostProcessor.ApplyAndCommit(editor, store.Tracks, lastPlan);
                string compactFinish = CompactLayoutPostProcessor.ApplyAndCommit(editor, store.Tracks, lastPlan);
                store.DetachActive();
                status = "Generated successfully. " + orbitResult.Emitted + " Orbit action(s), " + tileResult.Created
                    + " Floor decoration(s). " + previewFinish + " " + compactFinish
                    + " Output is detached; choose a track to resume source editing.";
            });
        }

        private static void HandleGlobalAction(string actionId, string argument)
        {
            TrackStore store = TrackStore.Current;
            scnEditor editor = ADOBase.editor;
            TrackSlot track = store != null && store.ActiveIndex >= 0 && store.ActiveIndex < store.Tracks.Count ? store.Tracks[store.ActiveIndex] : null;
            int value;
            double doubleValue;

            switch (actionId)
            {
                case "new-name":
                    newTrackName = argument ?? "";
                    break;
                case "store":
                    if (editor == null || store == null) return;
                    TryAction(delegate
                    {
                        int index = store.StoreCurrent(editor, newTrackName);
                        newTrackName = "";
                        InvalidatePlan();
                        status = "Stored " + store.Tracks[index].Name + " with Multi Tile start F" + store.Tracks[index].RegionStartFloor + ".";
                    });
                    break;
                case "save":
                    if (editor == null || store == null) return;
                    TryAction(delegate { store.SaveActive(editor); InvalidatePlan(); status = "Saved active track."; });
                    break;
                case "start":
                    if (editor == null || store == null) return;
                    TryAction(delegate { store.SetActiveRegionStartFromSelection(editor); InvalidatePlan(); status = "Updated Multi Tile start floor."; });
                    break;
                case "rename":
                    if (track == null) return;
                    track.Name = string.IsNullOrWhiteSpace(argument) ? track.Name : argument.Trim();
                    InvalidatePlan();
                    break;
                case "planet-a":
                    if (track == null) return;
                    track.PlanetATag = argument ?? "";
                    InvalidatePlan();
                    break;
                case "planet-b":
                    if (track == null) return;
                    track.PlanetBTag = argument ?? "";
                    InvalidatePlan();
                    break;
                case "pivot":
                    if (track == null) return;
                    track.PivotIsA = !track.PivotIsA;
                    InvalidatePlan();
                    break;
                case "wrap":
                    if (track == null || !int.TryParse(argument, out value)) return;
                    track.WrapMode = (CompactWrapMode)value;
                    InvalidatePlan();
                    break;
                case "wrap-tiles":
                    if (track == null) return;
                    track.WrapTilesText = argument ?? "";
                    if (int.TryParse(track.WrapTilesText, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value > 0) track.WrapEveryTiles = value;
                    InvalidatePlan();
                    break;
                case "wrap-beats":
                    if (track == null) return;
                    track.WrapBeatsText = argument ?? "";
                    if (double.TryParse(track.WrapBeatsText, NumberStyles.Float, CultureInfo.InvariantCulture, out doubleValue)
                        && doubleValue > 0.0 && !double.IsNaN(doubleValue) && !double.IsInfinity(doubleValue)) track.WrapEveryBeats = doubleValue;
                    InvalidatePlan();
                    break;
                case "repeat-text":
                    if (track == null) return;
                    track.RepeatCountText = argument ?? "";
                    if (int.TryParse(track.RepeatCountText, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value > 0) track.RepeatCount = value;
                    InvalidatePlan();
                    break;
                case "reuse":
                    if (track == null) return;
                    track.ReuseRepeatPath = argument == "1";
                    InvalidatePlan();
                    break;
                case "analyze-verify":
                    AnalyzeAndVerify();
                    break;
                case "generate":
                    Generate();
                    break;
                case "clear-plan":
                    InvalidatePlan();
                    lastActionFailed = false;
                    status = "Cleared analyzed plan.";
                    break;
                case "toggle-advanced":
                    showAdvanced = !showAdvanced;
                    break;
                case "analyze-only":
                    AnalyzeOnly();
                    break;
                case "verify-only":
                    VerifyOnly();
                    break;
                default:
                    return;
            }
            PublishNow(true);
        }

        internal static void HandlePaneAction(string actionId, string argument)
        {
            HandleGlobalAction(actionId, argument);
        }
    }

    internal sealed class MultiTilePaneProvider : IDockablePaneProvider
    {
        private readonly MultiTileTracksPane tracks = new MultiTileTracksPane();
        private readonly MultiTileSettingsPane settings = new MultiTileSettingsPane();

        public IEnumerable<IDockablePane> CreatePanes()
        {
            yield return tracks;
            yield return settings;
        }

        internal void Publish(MultiTileSnapshot snapshot)
        {
            MultiTileSnapshot value = snapshot ?? new MultiTileSnapshot();
            tracks.ApplySnapshot(value);
            settings.ApplySnapshot(value);
            Workbench.PublishPane(tracks.Id);
            Workbench.PublishPane(settings.Id);
        }
    }

    internal abstract class MultiTilePaneBase : IDockablePane
    {
        protected MultiTileSnapshot Snapshot = new MultiTileSnapshot();
        public abstract string Id { get; }
        public abstract string Title { get; }
        public virtual bool CanClose { get { return true; } }
        public abstract WorkbenchPaneView BuildView();
        public abstract void HandleAction(string actionId, string argument);
        internal void ApplySnapshot(MultiTileSnapshot snapshot) { Snapshot = snapshot ?? new MultiTileSnapshot(); }
        protected void FinishAction() { WorkbenchIntegration.PublishNow(true); }
    }

    internal sealed class MultiTileTracksPane : MultiTilePaneBase
    {
        public override string Id { get { return "mte.tracks"; } }
        public override string Title { get { return MteLocalization.T("tracks.title", "MTE Tracks"); } }

        public override WorkbenchPaneView BuildView()
        {
            var view = new WorkbenchPaneView().Text(MteLocalization.T("tracks.heading", "MTE Tracks"), 16f, true).Spacer(6);
            if (!Snapshot.EditorAvailable)
                return view.Text(MteLocalization.T("editor.open", "Open the ADOFAI level editor first."), 10f, false);

            view.BeginRow()
                .Text(MteLocalization.T("tracks.new", "New track"), 10f, false)
                .Input(Snapshot.NewTrackName, "new-name")
                .Button(MteLocalization.T("tracks.store", "+ Store current"), "store", "", false, true)
                .EndRow()
                .Spacer(5);

            if (Snapshot.Tracks.Count == 0)
                return view.Text(MteLocalization.T("tracks.empty", "No tracks yet. Select the Multi Tile start floor, then store the current chart."), 10f, false);

            for (int i = 0; i < Snapshot.Tracks.Count; i++)
            {
                TrackSnapshot track = Snapshot.Tracks[i];
                bool active = track.Index == Snapshot.ActiveIndex;
                string index = track.Index.ToString(CultureInfo.InvariantCulture);
                view.BeginRow()
                    .Button((active ? "> " : "") + track.Name, "switch", index, active, !active)
                    .Button(MteLocalization.T("tracks.delete", "Delete"), "remove", index, false, true)
                    .Text(MteLocalization.F("tracks.meta", "Start F{0}   Cursor F{1}   {2}", track.RegionStartFloor, track.CursorFloor, track.Layout), 9f, false)
                    .EndRow();
            }
            return view;
        }

        public override void HandleAction(string actionId, string argument)
        {
            scnEditor editor = ADOBase.editor;
            TrackStore store = TrackStore.Current;
            int index;
            if (actionId == "switch" && editor != null && store != null && int.TryParse(argument, out index))
            {
                try { store.SwitchTo(editor, index); WorkbenchIntegration.ResetGenerationState("Switched source track."); }
                catch { }
                FinishAction();
                return;
            }
            if (actionId == "remove" && editor != null && store != null && int.TryParse(argument, out index))
            {
                try { store.Remove(editor, index); WorkbenchIntegration.ResetGenerationState("Removed track."); }
                catch { }
                FinishAction();
                return;
            }
            WorkbenchIntegration.HandlePaneAction(actionId, argument);
        }
    }

    internal sealed class MultiTileSettingsPane : MultiTilePaneBase
    {
        public override string Id { get { return "mte.settings"; } }
        public override string Title { get { return MteLocalization.T("settings.title", "Multi Tile"); } }

        public override WorkbenchPaneView BuildView()
        {
            var view = new WorkbenchPaneView()
                .Text(MteLocalization.F("settings.heading", "Multi Tile Editor v{0}", Main.ModVersion), 16f, true)
                .Spacer(6);
            if (!Snapshot.EditorAvailable)
                return view.Text(MteLocalization.T("editor.inactive", "Level editor is not active."), 10f, false);

            if (Snapshot.ActiveIndex >= 0)
            {
                view.Text(MteLocalization.T("activeSource", "Active source"), 11f, true)
                    .BeginRow()
                    .Text(MteLocalization.T("track", "Track"), 10f, false)
                    .Input(Snapshot.ActiveName, "rename")
                    .Text(MteLocalization.F("source.meta", "Start F{0}   Cursor F{1}   {2}   {3}",
                        Snapshot.RegionStartFloor, Snapshot.CursorFloor, Snapshot.AngleText, Snapshot.AngleCountText), 9f, false)
                    .EndRow()
                    .BeginRow()
                    .Text(MteLocalization.T("planetA", "Planet A"), 10f, false).Input(Snapshot.PlanetATag, "planet-a")
                    .Text(MteLocalization.T("planetB", "Planet B"), 10f, false).Input(Snapshot.PlanetBTag, "planet-b")
                    .Button(MteLocalization.F("initialPivot", "Initial pivot: {0}", Snapshot.PivotIsA ? "A" : "B"), "pivot", "", true, true)
                    .EndRow()
                    .BeginRow()
                    .Button(MteLocalization.T("saveTrack", "Save track"), "save", "", false, true)
                    .Button(MteLocalization.T("setStart", "Set start from selection"), "start", "", false, true)
                    .EndRow()
                    .Spacer(8)
                    .Text(MteLocalization.T("layout", "Layout"), 11f, true)
                    .BeginRow()
                    .Button(MteLocalization.T("off", "Off"), "wrap", ((int)CompactWrapMode.Off).ToString(), Snapshot.WrapMode == CompactWrapMode.Off, true)
                    .Button(MteLocalization.T("tiles", "Tiles"), "wrap", ((int)CompactWrapMode.Tiles).ToString(), Snapshot.WrapMode == CompactWrapMode.Tiles, true)
                    .Button(MteLocalization.T("beats", "Beats"), "wrap", ((int)CompactWrapMode.Beats).ToString(), Snapshot.WrapMode == CompactWrapMode.Beats, true);

                if (Snapshot.WrapMode == CompactWrapMode.Tiles)
                    view.Text(MteLocalization.T("length", "Length"), 10f, false)
                        .Input(Snapshot.WrapTilesText, "wrap-tiles")
                        .Text(MteLocalization.T("tiles.unit", "tiles"), 10f, false);
                else if (Snapshot.WrapMode == CompactWrapMode.Beats)
                    view.Text(MteLocalization.T("length", "Length"), 10f, false)
                        .Input(Snapshot.WrapBeatsText, "wrap-beats")
                        .Text(MteLocalization.T("beats.unit", "beats"), 10f, false);

                view.EndRow()
                    .BeginRow()
                    .Text(MteLocalization.T("virtualRepeat", "Virtual repeat"), 10f, false)
                    .Input(Snapshot.RepeatCountText, "repeat-text")
                    .Text("x", 10f, false)
                    .Toggle(MteLocalization.T("reuse", "Return to first tile / reuse one source cycle"), "reuse", Snapshot.ReuseRepeatPath)
                    .EndRow()
                    .Text(Snapshot.WrapMode == CompactWrapMode.Off
                        ? MteLocalization.T("layout.offDescription", "Layout folding is off; Position Track and virtual repeat returns still use instant planet teleports.")
                        : Snapshot.ActiveLayout, 9f, false)
                    .Spacer(10);
            }
            else if (Snapshot.Tracks.Count > 0)
            {
                view.Text(MteLocalization.T("detached", "Generated output is detached. Choose a track in MTE Tracks to continue editing a source track."), 10f, false).Spacer(8);
            }
            else
            {
                view.Text(MteLocalization.T("chooseStart", "Choose the Multi Tile start floor, then store the current chart in MTE Tracks."), 10f, false).Spacer(8);
            }

            view.Text(MteLocalization.T("generation", "Generation"), 11f, true)
                .BeginRow()
                .Text(Snapshot.GenerationState, 10f, true)
                .Button(MteLocalization.T("analyzeVerify", "Analyze + Verify"), "analyze-verify", "", false, Snapshot.CanAnalyze)
                .Button(MteLocalization.T("generate", "Generate Multi Tile"), "generate", "", false, Snapshot.CanGenerate)
                .Button(MteLocalization.T("clear", "Clear"), "clear-plan", "", false, Snapshot.CanClear)
                .EndRow();

            if (!string.IsNullOrWhiteSpace(Snapshot.PlanSummary)) view.Text(Snapshot.PlanSummary, 9f, false);

            view.Spacer(8)
                .Text((Snapshot.LastActionFailed
                    ? MteLocalization.T("error", "ERROR: ")
                    : MteLocalization.T("status", "Status: ")) + Snapshot.Status, 10f, Snapshot.LastActionFailed)
                .Spacer(8)
                .Button(Snapshot.ShowAdvanced
                    ? MteLocalization.T("hideAdvanced", "Hide advanced / diagnostics")
                    : MteLocalization.T("showAdvanced", "Show advanced / diagnostics"),
                    "toggle-advanced", "", Snapshot.ShowAdvanced, true);

            if (Snapshot.ShowAdvanced)
            {
                view.Spacer(6)
                    .BeginRow()
                    .Button(MteLocalization.T("analyzeOnly", "Analyze only"), "analyze-only", "", false, Snapshot.CanAnalyze)
                    .Button(MteLocalization.T("verifyOnly", "Verify only"), "verify-only", "", false, Snapshot.CanClear)
                    .EndRow();
                for (int i = 0; i < Snapshot.AdvancedLines.Count; i++) view.Text(Snapshot.AdvancedLines[i], 9f, false);
                view.Spacer(4)
                    .Text(MteLocalization.T("fullDiagnostic", "Full diagnostic:"), 10f, true)
                    .Text(Snapshot.Status, 9f, false);
            }
            return view;
        }

        public override void HandleAction(string actionId, string argument)
        {
            WorkbenchIntegration.HandlePaneAction(actionId, argument);
        }
    }

    internal sealed class MultiTileSnapshot
    {
        internal bool EditorAvailable;
        internal int ActiveIndex = -1;
        internal readonly List<TrackSnapshot> Tracks = new List<TrackSnapshot>();
        internal string NewTrackName = "";
        internal string ActiveName = "";
        internal int RegionStartFloor;
        internal int CursorFloor;
        internal bool PivotIsA;
        internal CompactWrapMode WrapMode;
        internal string WrapTilesText = "32";
        internal string WrapBeatsText = "16";
        internal string RepeatCountText = "1";
        internal int RepeatCount = 1;
        internal bool ReuseRepeatPath;
        internal string PlanetATag = "";
        internal string PlanetBTag = "";
        internal string AngleText = "";
        internal string AngleCountText = "";
        internal string ActiveLayout = "";
        internal string GenerationState = "";
        internal bool CanAnalyze;
        internal bool CanGenerate;
        internal bool CanClear;
        internal string PlanSummary = "";
        internal string Status = "";
        internal bool LastActionFailed;
        internal bool ShowAdvanced;
        internal readonly List<string> AdvancedLines = new List<string>();
        internal string Signature = "";

        internal void BuildSignature()
        {
            var parts = new List<string>
            {
                EditorAvailable ? "1" : "0", ActiveIndex.ToString(), NewTrackName ?? "", ActiveName ?? "",
                RegionStartFloor.ToString(), CursorFloor.ToString(), PivotIsA ? "A" : "B", ((int)WrapMode).ToString(),
                WrapTilesText ?? "", WrapBeatsText ?? "", RepeatCountText ?? "", RepeatCount.ToString(),
                ReuseRepeatPath ? "1" : "0", PlanetATag ?? "", PlanetBTag ?? "", AngleText ?? "", AngleCountText ?? "",
                ActiveLayout ?? "", GenerationState ?? "", CanAnalyze ? "1" : "0", CanGenerate ? "1" : "0", CanClear ? "1" : "0",
                PlanSummary ?? "", Status ?? "", LastActionFailed ? "1" : "0", ShowAdvanced ? "1" : "0"
            };
            for (int i = 0; i < Tracks.Count; i++)
            {
                TrackSnapshot t = Tracks[i];
                parts.Add(t.Index + ":" + t.Name + ":" + t.RegionStartFloor + ":" + t.CursorFloor + ":" + t.Layout);
            }
            for (int i = 0; i < AdvancedLines.Count; i++) parts.Add(AdvancedLines[i] ?? "");
            Signature = string.Join("|", parts.ToArray());
        }
    }

    internal sealed class TrackSnapshot
    {
        internal int Index;
        internal string Name;
        internal int RegionStartFloor;
        internal int CursorFloor;
        internal string Layout;
    }
}
