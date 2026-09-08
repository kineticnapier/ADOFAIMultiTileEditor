using System;
using System.Collections.Generic;
using System.Globalization;
using KineticNapier.ADOFAIWorkbench;

namespace KineticNapier.ADOFAIMultiTileEditor
{
    internal static class DynamicPagingRuntime
    {
        private static bool pending;
        private static string status = "Dynamic paging is idle.";

        internal static string Status { get { return status; } }

        internal static void RequestApply(string message)
        {
            pending = true;
            if (!string.IsNullOrWhiteSpace(message)) status = message;
        }

        internal static bool EnforceExclusiveLayout(TrackStore store, scnEditor editor)
        {
            if (store == null) return false;
            bool changed = false;
            for (int i = 0; i < store.Tracks.Count; i++)
            {
                TrackSlot track = store.Tracks[i];
                if (track == null || !track.DynamicPagingEnabled) continue;
                if (track.WrapMode == CompactWrapMode.Off) continue;
                track.WrapMode = CompactWrapMode.Off;
                changed = true;
            }
            if (changed)
            {
                store.PersistMetadata(editor);
                WorkbenchIntegration.PublishNow(true);
            }
            return changed;
        }

        internal static void Tick()
        {
            TrackStore store = TrackStore.Current;
            scnEditor editor = ADOBase.editor;
            EnforceExclusiveLayout(store, editor);

            if (!pending || store == null || editor == null || editor.levelData == null) return;
            pending = false;

            if (store.ActiveIndex >= 0)
            {
                status = MteLocalization.T(
                    "paging.savedForGenerate",
                    "Paging settings saved. Generate Multi Tile to apply them to the output.");
                DynamicPagingPaneRegistration.PublishNow();
                return;
            }

            if (!DynamicPagedLayoutPostProcessor.AnyEnabled(store.Tracks))
            {
                status = MteLocalization.T(
                    "paging.disabledOutput",
                    "Paging is disabled. Regenerate Multi Tile to restore a normal output layout.");
                DynamicPagingPaneRegistration.PublishNow();
                return;
            }

            try
            {
                GenerationPlan plan = TrackAnalyzer.BuildPlan(editor, store.Tracks);
                status = DynamicPagedLayoutPostProcessor.ApplyAndCommit(editor, store.Tracks, plan);
                store.PersistMetadata(editor);
            }
            catch (Exception ex)
            {
                status = ex.GetType().Name + ": " + ex.Message;
            }
            DynamicPagingPaneRegistration.PublishNow();
        }
    }

    internal static class DynamicPagingPaneRegistration
    {
        private static readonly DynamicPagingPaneProvider Provider = new DynamicPagingPaneProvider();
        private static bool registered;
        private static int nextPublishFrame;

        internal static void EnsureRegistered()
        {
            if (registered) return;
            Workbench.RegisterPaneProvider(Provider);
            registered = true;
            Provider.Publish();
        }

        internal static void Unregister()
        {
            if (!registered) return;
            Workbench.UnregisterPaneProvider(Provider);
            registered = false;
        }

        internal static void Tick()
        {
            DynamicPagingRuntime.Tick();
            if (!registered || UnityEngine.Time.frameCount < nextPublishFrame) return;
            nextPublishFrame = UnityEngine.Time.frameCount + 10;
            Provider.Publish();
        }

        internal static void PublishNow()
        {
            if (registered) Provider.Publish();
        }
    }

    internal sealed class DynamicPagingPaneProvider : IDockablePaneProvider
    {
        private readonly DynamicPagingPane pane = new DynamicPagingPane();

        public IEnumerable<IDockablePane> CreatePanes()
        {
            yield return pane;
        }

        internal void Publish()
        {
            Workbench.PublishPane(pane.Id);
        }
    }

    internal sealed class DynamicPagingPane : IDockablePane
    {
        public string Id { get { return "mte.paging"; } }
        public string Title { get { return MteLocalization.T("paging.title", "MTE Paging"); } }
        public bool CanClose { get { return true; } }

        public WorkbenchPaneView BuildView()
        {
            var view = new WorkbenchPaneView()
                .Text(MteLocalization.T("paging.heading", "Dynamic paging"), 16f, true)
                .Text(MteLocalization.T(
                    "paging.help",
                    "For dense Hz charts, split each generated Floor group into reusable pages. Inactive pages stay off-screen and zero-duration MoveDecorations swaps pages at page boundaries."), 9f, false)
                .Spacer(6);

            TrackStore store = TrackStore.Current;
            if (store == null || store.Tracks.Count == 0)
                return view.Text(MteLocalization.T("paging.empty", "No MTE tracks are stored."), 10f, false);

            for (int i = 0; i < store.Tracks.Count; i++)
            {
                TrackSlot track = store.Tracks[i];
                if (track == null) continue;
                string index = i.ToString(CultureInfo.InvariantCulture);
                view.Text(string.IsNullOrWhiteSpace(track.Name) ? "Track " + (i + 1) : track.Name, 11f, true)
                    .BeginRow()
                    .Toggle(MteLocalization.T("paging.enabled", "Paged"), "paging-enable:" + index, track.DynamicPagingEnabled)
                    .Text(MteLocalization.T("paging.pageSize", "Page size"), 10f, false)
                    .Input(track.PageTilesText ?? "64", "paging-size:" + index)
                    .Text(MteLocalization.T("paging.tilesPerPage", "tiles / page"), 10f, false)
                    .EndRow();
            }

            view.Spacer(6)
                .Text(MteLocalization.T(
                    "paging.note",
                    "Paged mode forces the normal Off/Tiles/Beats layout to Off. Page-boundary tiles are duplicated so the landing tile remains visible during the swap."), 9f, false)
                .Spacer(6)
                .Button(MteLocalization.T("paging.apply", "Apply paging to generated output"), "paging-apply", "", false, true)
                .Spacer(4)
                .Text(DynamicPagingRuntime.Status, 9f, false);
            return view;
        }

        public void HandleAction(string actionId, string argument)
        {
            TrackStore store = TrackStore.Current;
            scnEditor editor = ADOBase.editor;
            if (store == null || string.IsNullOrEmpty(actionId)) return;

            if (string.Equals(actionId, "paging-apply", StringComparison.Ordinal))
            {
                DynamicPagingRuntime.RequestApply(MteLocalization.T("paging.queued", "Paging update queued."));
                DynamicPagingPaneRegistration.PublishNow();
                return;
            }

            int colon = actionId.LastIndexOf(':');
            int index;
            if (colon <= 0 || !int.TryParse(actionId.Substring(colon + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out index)) return;
            if (index < 0 || index >= store.Tracks.Count) return;
            TrackSlot track = store.Tracks[index];
            string command = actionId.Substring(0, colon);

            if (command == "paging-enable")
            {
                track.DynamicPagingEnabled = argument == "1";
                if (track.DynamicPagingEnabled) track.WrapMode = CompactWrapMode.Off;
                store.PersistMetadata(editor);
                DynamicPagingRuntime.RequestApply(track.DynamicPagingEnabled
                    ? MteLocalization.T("paging.enabledQueued", "Paged mode enabled; it will apply after generation or to the detached output.")
                    : MteLocalization.T("paging.disabledQueued", "Paged mode disabled; regenerate to remove paging from an existing output."));
                WorkbenchIntegration.PublishNow(true);
            }
            else if (command == "paging-size")
            {
                track.PageTilesText = argument ?? string.Empty;
                int value;
                if (int.TryParse(track.PageTilesText, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value >= 2)
                    track.PageTiles = value;
                store.PersistMetadata(editor);
                if (track.DynamicPagingEnabled)
                    DynamicPagingRuntime.RequestApply(MteLocalization.T("paging.sizeQueued", "Page size updated."));
            }

            DynamicPagingPaneRegistration.PublishNow();
        }
    }
}
