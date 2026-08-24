using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
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

        internal static void RunOnUnity(Action action)
        {
            Workbench.RunOnUnityThread(delegate
            {
                try
                {
                    if (action != null) action();
                    PublishNow(true);
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogError("[ADOFAIMultiTileEditor/Workbench] " + ex);
                }
            });
        }

        private static void PublishNow(bool force)
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
                        snapshot.ActiveName = active.Name ?? "Track " + (store.ActiveIndex + 1);
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
        private MultiTileSnapshot latest = new MultiTileSnapshot();

        public IEnumerable<IDockablePane> CreatePanes()
        {
            yield return tracks;
            yield return settings;
        }

        internal void Publish(MultiTileSnapshot snapshot)
        {
            latest = snapshot ?? new MultiTileSnapshot();
            Workbench.RunOnUiThread(delegate
            {
                tracks.ApplySnapshot(latest);
                settings.ApplySnapshot(latest);
            });
        }
    }

    internal abstract class MultiTilePaneBase : IDockablePane
    {
        protected readonly FlowLayoutPanel Root = new FlowLayoutPanel();
        protected MultiTileSnapshot Snapshot = new MultiTileSnapshot();
        private bool created;

        protected MultiTilePaneBase()
        {
            Root.Dock = DockStyle.Fill;
            Root.FlowDirection = FlowDirection.TopDown;
            Root.WrapContents = false;
            Root.AutoScroll = true;
            Root.Padding = new Padding(12);
            Root.BackColor = Color.FromArgb(19, 21, 26);
            Root.ForeColor = Color.FromArgb(225, 228, 235);
        }

        public abstract string Id { get; }
        public abstract string Title { get; }
        public virtual bool CanClose { get { return true; } }

        public Control CreateView()
        {
            created = true;
            Draw();
            return Root;
        }

        public void OnOpened() { if (created) Draw(); }
        public void OnClosed() { }

        internal void ApplySnapshot(MultiTileSnapshot snapshot)
        {
            Snapshot = snapshot ?? new MultiTileSnapshot();
            if (created && !Root.IsDisposed) Draw();
        }

        protected abstract void DrawContents();

        protected void Draw()
        {
            Root.SuspendLayout();
            try
            {
                Root.Controls.Clear();
                Root.Controls.Add(Text(Title, 16f, true));
                Root.Controls.Add(Spacer(6));
                DrawContents();
            }
            finally
            {
                Root.ResumeLayout(true);
            }
        }

        protected static Label Text(string value, float size, bool bold)
        {
            return new Label
            {
                Text = value ?? "",
                AutoSize = true,
                MaximumSize = new Size(900, 0),
                ForeColor = Color.FromArgb(225, 228, 235),
                Font = new Font(SystemFonts.MessageBoxFont.FontFamily, size, bold ? FontStyle.Bold : FontStyle.Regular),
                Margin = new Padding(2, 2, 2, 4)
            };
        }

        protected static Control Spacer(int height)
        {
            return new Panel { Width = 1, Height = height, Margin = new Padding(0) };
        }

        protected static Button ActionButton(string text, Action action, bool selected)
        {
            Button button = new Button
            {
                Text = text,
                AutoSize = true,
                MinimumSize = new Size(70, 30),
                Margin = new Padding(2),
                Padding = new Padding(6, 0, 6, 0),
                FlatStyle = FlatStyle.Flat,
                BackColor = selected ? Color.FromArgb(70, 86, 118) : Color.FromArgb(50, 54, 64),
                ForeColor = Color.White
            };
            if (action != null) button.Click += delegate { action(); };
            return button;
        }

        protected static FlowLayoutPanel Row(params Control[] children)
        {
            FlowLayoutPanel panel = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                Margin = new Padding(0, 2, 0, 4),
                BackColor = Color.FromArgb(19, 21, 26)
            };
            for (int i = 0; i < children.Length; i++) panel.Controls.Add(children[i]);
            return panel;
        }
    }

    internal sealed class MultiTileTracksPane : MultiTilePaneBase
    {
        public override string Id { get { return "mte.tracks"; } }
        public override string Title { get { return "MTE Tracks"; } }

        protected override void DrawContents()
        {
            if (!Snapshot.EditorAvailable)
            {
                Root.Controls.Add(Text("Open the ADOFAI level editor first.", 10f, false));
                return;
            }

            Root.Controls.Add(Row(
                ActionButton("+ Store current", delegate
                {
                    WorkbenchIntegration.RunOnUnity(delegate { TrackStore.Current.StoreCurrent(ADOBase.editor, ""); });
                }, false),
                ActionButton("Save active", delegate
                {
                    WorkbenchIntegration.RunOnUnity(delegate { TrackStore.Current.SaveActive(ADOBase.editor); });
                }, false),
                ActionButton("Start = selected", delegate
                {
                    WorkbenchIntegration.RunOnUnity(delegate { TrackStore.Current.SetActiveRegionStartFromSelection(ADOBase.editor); });
                }, false)));

            if (Snapshot.Tracks.Count == 0)
            {
                Root.Controls.Add(Text("No tracks yet. Store the current chart to create one.", 10f, false));
                return;
            }

            for (int i = 0; i < Snapshot.Tracks.Count; i++)
            {
                TrackSnapshot track = Snapshot.Tracks[i];
                bool active = track.Index == Snapshot.ActiveIndex;
                int index = track.Index;
                Root.Controls.Add(Row(
                    ActionButton((active ? "> " : "") + track.Name, delegate
                    {
                        WorkbenchIntegration.RunOnUnity(delegate { TrackStore.Current.SwitchTo(ADOBase.editor, index); });
                    }, active),
                    ActionButton("×", delegate
                    {
                        WorkbenchIntegration.RunOnUnity(delegate { TrackStore.Current.Remove(ADOBase.editor, index); });
                    }, false),
                    Text("F" + track.RegionStartFloor + "   " + track.Layout, 9f, false)));
            }
        }
    }

    internal sealed class MultiTileSettingsPane : MultiTilePaneBase
    {
        public override string Id { get { return "mte.settings"; } }
        public override string Title { get { return "Multi Tile"; } }

        protected override void DrawContents()
        {
            if (!Snapshot.EditorAvailable || Snapshot.ActiveIndex < 0)
            {
                Root.Controls.Add(Text("Choose or store a source track first.", 10f, false));
                return;
            }

            Root.Controls.Add(Text(
                Snapshot.ActiveName + "   start F" + Snapshot.RegionStartFloor + "   cursor F" + Snapshot.CursorFloor,
                10f,
                false));

            Root.Controls.Add(Row(
                Text("Pivot", 10f, false),
                ActionButton(Snapshot.PivotIsA ? "A" : "B", delegate
                {
                    WorkbenchIntegration.RunOnUnity(delegate
                    {
                        TrackSlot track = TrackStore.Current.Tracks[TrackStore.Current.ActiveIndex];
                        track.PivotIsA = !track.PivotIsA;
                    });
                }, true)));

            Root.Controls.Add(Row(
                Text("Wrap", 10f, false),
                WrapButton("Off", CompactWrapMode.Off),
                WrapButton("Tiles", CompactWrapMode.Tiles),
                WrapButton("Beats", CompactWrapMode.Beats)));

            Root.Controls.Add(Row(
                Text("Repeat", 10f, false),
                ActionButton("−", delegate { AdjustRepeat(-1); }, false),
                Text("×" + Snapshot.RepeatCount, 10f, true),
                ActionButton("+", delegate { AdjustRepeat(1); }, false),
                ActionButton(Snapshot.ReuseRepeatPath ? "Reuse path: ON" : "Reuse path: OFF", delegate
                {
                    WorkbenchIntegration.RunOnUnity(delegate
                    {
                        TrackSlot track = TrackStore.Current.Tracks[TrackStore.Current.ActiveIndex];
                        track.ReuseRepeatPath = !track.ReuseRepeatPath;
                    });
                }, Snapshot.ReuseRepeatPath)));

            Root.Controls.Add(Text("Planet A tag: " + (string.IsNullOrWhiteSpace(Snapshot.PlanetATag) ? "<unset>" : Snapshot.PlanetATag), 9f, false));
            Root.Controls.Add(Text("Planet B tag: " + (string.IsNullOrWhiteSpace(Snapshot.PlanetBTag) ? "<unset>" : Snapshot.PlanetBTag), 9f, false));
            Root.Controls.Add(Spacer(8));
            Root.Controls.Add(Text("Generation controls remain in the MTE UMM panel for now.", 9f, false));
        }

        private Button WrapButton(string label, CompactWrapMode mode)
        {
            return ActionButton(label, delegate
            {
                WorkbenchIntegration.RunOnUnity(delegate
                {
                    TrackSlot track = TrackStore.Current.Tracks[TrackStore.Current.ActiveIndex];
                    track.WrapMode = mode;
                });
            }, Snapshot.WrapMode == mode);
        }

        private void AdjustRepeat(int delta)
        {
            WorkbenchIntegration.RunOnUnity(delegate
            {
                TrackSlot track = TrackStore.Current.Tracks[TrackStore.Current.ActiveIndex];
                track.RepeatCount = Math.Max(1, Math.Min(999, track.RepeatCount + delta));
                track.RepeatCountText = track.RepeatCount.ToString();
            });
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
