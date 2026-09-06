using System;
using System.Collections.Generic;
using System.Globalization;
using KineticNapier.ADOFAIWorkbench;

namespace KineticNapier.ADOFAIMultiTileEditor
{
    internal static class GeneratedLayoutPaneRegistration
    {
        private static readonly GeneratedLayoutPaneProvider Provider = new GeneratedLayoutPaneProvider();
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
            if (!registered || UnityEngine.Time.frameCount < nextPublishFrame) return;
            nextPublishFrame = UnityEngine.Time.frameCount + 10;
            Provider.Publish();
        }
    }

    internal sealed class GeneratedLayoutPaneProvider : IDockablePaneProvider
    {
        private readonly GeneratedLayoutPane pane = new GeneratedLayoutPane();

        public IEnumerable<IDockablePane> CreatePanes()
        {
            yield return pane;
        }

        internal void Publish()
        {
            Workbench.PublishPane(pane.Id);
        }
    }

    internal sealed class GeneratedLayoutPane : IDockablePane
    {
        private string status = "Set an absolute X/Y offset for a generated group, then Apply.";

        public string Id { get { return "mte.layout"; } }
        public string Title { get { return MteLocalization.T("layoutPane.title", "MTE Layout"); } }
        public bool CanClose { get { return true; } }

        public WorkbenchPaneView BuildView()
        {
            var view = new WorkbenchPaneView()
                .Text(MteLocalization.T("layoutPane.heading", "Generated layout"), 16f, true)
                .Text(MteLocalization.T("layoutPane.help", "Move a whole generated group without selecting every Floor decoration. Planet A/B and all MTE-generated tiles move together."), 9f, false)
                .Spacer(6);

            TrackStore store = TrackStore.Current;
            if (store == null || store.Tracks.Count == 0)
                return view.Text(MteLocalization.T("layoutPane.empty", "No MTE tracks are stored."), 10f, false);

            for (int i = 0; i < store.Tracks.Count; i++)
            {
                TrackSlot track = store.Tracks[i];
                if (track == null) continue;
                string index = i.ToString(CultureInfo.InvariantCulture);
                view.Text(string.IsNullOrWhiteSpace(track.Name) ? "Track " + (i + 1) : track.Name, 11f, true)
                    .BeginRow()
                    .Text("X", 10f, false)
                    .Input(track.LayoutOffsetXText ?? "0", "layout-x:" + index)
                    .Text("Y", 10f, false)
                    .Input(track.LayoutOffsetYText ?? "0", "layout-y:" + index)
                    .Button(MteLocalization.T("layoutPane.apply", "Apply to generated"), "layout-apply:" + index, "", false, true)
                    .EndRow();
            }

            return view.Spacer(6).Text(status, 9f, false);
        }

        public void HandleAction(string actionId, string argument)
        {
            TrackStore store = TrackStore.Current;
            scnEditor editor = ADOBase.editor;
            if (store == null || string.IsNullOrEmpty(actionId)) return;

            int colon = actionId.LastIndexOf(':');
            int index;
            if (colon <= 0 || !int.TryParse(actionId.Substring(colon + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out index)) return;
            if (index < 0 || index >= store.Tracks.Count) return;
            TrackSlot track = store.Tracks[index];
            string command = actionId.Substring(0, colon);

            try
            {
                double value;
                if (command == "layout-x")
                {
                    track.LayoutOffsetXText = argument ?? string.Empty;
                    if (double.TryParse(track.LayoutOffsetXText, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                        && !double.IsNaN(value) && !double.IsInfinity(value)) track.LayoutOffsetX = value;
                    store.PersistMetadata(editor);
                }
                else if (command == "layout-y")
                {
                    track.LayoutOffsetYText = argument ?? string.Empty;
                    if (double.TryParse(track.LayoutOffsetYText, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                        && !double.IsNaN(value) && !double.IsInfinity(value)) track.LayoutOffsetY = value;
                    store.PersistMetadata(editor);
                }
                else if (command == "layout-apply")
                {
                    int moved = GeneratedLayoutMover.Apply(editor, track, index);
                    store.PersistMetadata(editor);
                    status = moved == 0
                        ? "Already at that offset."
                        : "Moved " + moved + " generated object(s) for " + track.Name + ".";
                }
            }
            catch (Exception ex)
            {
                status = ex.GetType().Name + ": " + ex.Message;
            }

            Workbench.PublishPane(Id);
        }
    }
}
