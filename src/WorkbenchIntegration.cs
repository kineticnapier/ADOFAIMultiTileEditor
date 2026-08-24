using System;
using System.Collections.Generic;
using KineticNapier.ADOFAIWorkbench;

namespace KineticNapier.ADOFAIMultiTileEditor
{
    internal static class WorkbenchIntegration
    {
        private static readonly MultiTilePaneProvider Provider = new MultiTilePaneProvider();
        private static bool registered;
        private static int nextPublishFrame;
        private static string lastSignature;

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
                        snapshot.RepeatCount = active.RepeatCount;
                        snapshot.ReuseRepeatPath = active.ReuseRepeatPath;
                        snapshot.PlanetATag = active.PlanetATag ?? "";
                        snapshot.PlanetBTag = active.PlanetBTag ?? "";
                    }
                }
            }

            snapshot.BuildSignature();
            return snapshot;
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

        internal void ApplySnapshot(MultiTileSnapshot snapshot)
        {
            Snapshot = snapshot ?? new MultiTileSnapshot();
        }

        protected void FinishAction()
        {
            WorkbenchIntegration.PublishNow(true);
        }

        protected TrackSlot ActiveTrack()
        {
            TrackStore store = TrackStore.Current;
            if (store == null || store.ActiveIndex < 0 || store.ActiveIndex >= store.Tracks.Count) return null;
            return store.Tracks[store.ActiveIndex];
        }
    }

    internal sealed class MultiTileTracksPane : MultiTilePaneBase
    {
        public override string Id { get { return "mte.tracks"; } }
        public override string Title { get { return "MTE Tracks"; } }

        public override WorkbenchPaneView BuildView()
        {
            var view = new WorkbenchPaneView()
                .Text(Title, 16f, true)
                .Spacer(6);

            if (!Snapshot.EditorAvailable)
                return view.Text("Open the ADOFAI level editor first.", 10f, false);

            view.BeginRow()
                .Button("+ Store current", "store", "", false)
                .Button("Save active", "save", "", false)
                .Button("Start = selected", "start", "", false)
                .EndRow();

            if (Snapshot.Tracks.Count == 0)
                return view.Text("No tracks yet. Store the current chart to create one.", 10f, false);

            for (int i = 0; i < Snapshot.Tracks.Count; i++)
            {
                TrackSnapshot track = Snapshot.Tracks[i];
                bool active = track.Index == Snapshot.ActiveIndex;
                string index = track.Index.ToString();
                view.BeginRow()
                    .Button((active ? "> " : "") + track.Name, "switch", index, active)
                    .Button("x", "remove", index, false)
                    .Text("F" + track.RegionStartFloor + "   " + track.Layout, 9f, false)
                    .EndRow();
            }
            return view;
        }

        public override void HandleAction(string actionId, string argument)
        {
            scnEditor editor = ADOBase.editor;
            TrackStore store = TrackStore.Current;
            if (editor == null || store == null) return;

            int index;
            switch (actionId)
            {
                case "store":
                    store.StoreCurrent(editor, "");
                    break;
                case "save":
                    store.SaveActive(editor);
                    break;
                case "start":
                    store.SetActiveRegionStartFromSelection(editor);
                    break;
                case "switch":
                    if (int.TryParse(argument, out index)) store.SwitchTo(editor, index);
                    break;
                case "remove":
                    if (int.TryParse(argument, out index)) store.Remove(editor, index);
                    break;
                default:
                    return;
            }
            FinishAction();
        }
    }

    internal sealed class MultiTileSettingsPane : MultiTilePaneBase
    {
        public override string Id { get { return "mte.settings"; } }
        public override string Title { get { return "Multi Tile"; } }

        public override WorkbenchPaneView BuildView()
        {
            var view = new WorkbenchPaneView()
                .Text(Title, 16f, true)
                .Spacer(6);

            if (!Snapshot.EditorAvailable || Snapshot.ActiveIndex < 0)
                return view.Text("Choose or store a source track first.", 10f, false);

            view.Text(Snapshot.ActiveName + "   start F" + Snapshot.RegionStartFloor + "   cursor F" + Snapshot.CursorFloor, 10f, false)
                .BeginRow()
                .Text("Pivot", 10f, false)
                .Button(Snapshot.PivotIsA ? "A" : "B", "pivot", "", true)
                .EndRow()
                .BeginRow()
                .Text("Wrap", 10f, false)
                .Button("Off", "wrap", ((int)CompactWrapMode.Off).ToString(), Snapshot.WrapMode == CompactWrapMode.Off)
                .Button("Tiles", "wrap", ((int)CompactWrapMode.Tiles).ToString(), Snapshot.WrapMode == CompactWrapMode.Tiles)
                .Button("Beats", "wrap", ((int)CompactWrapMode.Beats).ToString(), Snapshot.WrapMode == CompactWrapMode.Beats)
                .EndRow()
                .BeginRow()
                .Text("Repeat", 10f, false)
                .Button("-", "repeat", "-1", false)
                .Text("x" + Snapshot.RepeatCount, 10f, true)
                .Button("+", "repeat", "1", false)
                .Button(Snapshot.ReuseRepeatPath ? "Reuse path: ON" : "Reuse path: OFF", "reuse", "", Snapshot.ReuseRepeatPath)
                .EndRow()
                .Text("Planet A tag: " + (string.IsNullOrWhiteSpace(Snapshot.PlanetATag) ? "<unset>" : Snapshot.PlanetATag), 9f, false)
                .Text("Planet B tag: " + (string.IsNullOrWhiteSpace(Snapshot.PlanetBTag) ? "<unset>" : Snapshot.PlanetBTag), 9f, false)
                .Spacer(8)
                .Text("Generation controls remain in the MTE UMM panel for now.", 9f, false);
            return view;
        }

        public override void HandleAction(string actionId, string argument)
        {
            TrackSlot track = ActiveTrack();
            if (track == null) return;

            int value;
            switch (actionId)
            {
                case "pivot":
                    track.PivotIsA = !track.PivotIsA;
                    break;
                case "wrap":
                    if (!int.TryParse(argument, out value)) return;
                    track.WrapMode = (CompactWrapMode)value;
                    break;
                case "repeat":
                    if (!int.TryParse(argument, out value)) return;
                    track.RepeatCount = Math.Max(1, Math.Min(999, track.RepeatCount + value));
                    track.RepeatCountText = track.RepeatCount.ToString();
                    break;
                case "reuse":
                    track.ReuseRepeatPath = !track.ReuseRepeatPath;
                    break;
                default:
                    return;
            }
            FinishAction();
        }
    }

    internal sealed class MultiTileSnapshot
    {
        internal bool EditorAvailable;
        internal int ActiveIndex = -1;
        internal readonly List<TrackSnapshot> Tracks = new List<TrackSnapshot>();
        internal string ActiveName = "";
        internal int RegionStartFloor;
        internal int CursorFloor;
        internal bool PivotIsA;
        internal CompactWrapMode WrapMode;
        internal int RepeatCount = 1;
        internal bool ReuseRepeatPath;
        internal string PlanetATag = "";
        internal string PlanetBTag = "";
        internal string Signature = "";

        internal void BuildSignature()
        {
            var parts = new List<string>
            {
                EditorAvailable ? "1" : "0",
                ActiveIndex.ToString(), ActiveName ?? "", RegionStartFloor.ToString(), CursorFloor.ToString(),
                PivotIsA ? "A" : "B", ((int)WrapMode).ToString(), RepeatCount.ToString(),
                ReuseRepeatPath ? "1" : "0", PlanetATag ?? "", PlanetBTag ?? ""
            };
            for (int i = 0; i < Tracks.Count; i++)
            {
                TrackSnapshot t = Tracks[i];
                parts.Add(t.Index + ":" + t.Name + ":" + t.RegionStartFloor + ":" + t.CursorFloor + ":" + t.Layout);
            }
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
