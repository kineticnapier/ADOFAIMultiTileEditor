using System;
using System.Collections.Generic;
using KineticNapier.ADOFAIWorkbench;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using UnityEngine;

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
            if (!registered || Time.frameCount < nextPublishFrame) return;
            nextPublishFrame = Time.frameCount + 10;
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
                    Debug.LogError("[ADOFAIMultiTileEditor/Workbench] " + ex);
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
        protected readonly StackPanel Root = new StackPanel { Margin = new Thickness(12) };
        protected MultiTileSnapshot Snapshot = new MultiTileSnapshot();
        private ScrollViewer view;

        public abstract string Id { get; }
        public abstract string Title { get; }
        public virtual bool CanClose { get { return true; } }

        public FrameworkElement CreateView()
        {
            view = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = new SolidColorBrush(Color.FromRgb(19, 21, 26)),
                Content = Root
            };
            Draw();
            return view;
        }

        public void OnOpened() { Draw(); }
        public void OnClosed() { }

        internal void ApplySnapshot(MultiTileSnapshot snapshot)
        {
            Snapshot = snapshot ?? new MultiTileSnapshot();
            if (view != null) Draw();
        }

        protected abstract void DrawContents();

        protected void Draw()
        {
            Root.Children.Clear();
            Root.Children.Add(Text(Title, 22, true));
            Root.Children.Add(Spacer(8));
            DrawContents();
        }

        protected static TextBlock Text(string value, double size, bool bold)
        {
            return new TextBlock
            {
                Text = value ?? "",
                FontSize = size,
                FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = new SolidColorBrush(Color.FromRgb(225, 228, 235)),
                Margin = new Thickness(2, 2, 2, 4),
                TextWrapping = TextWrapping.Wrap
            };
        }

        protected static FrameworkElement Spacer(double height)
        {
            return new Border { Height = height };
        }

        protected static Button ActionButton(string text, Action action, bool selected)
        {
            Button button = new Button
            {
                Content = text,
                Margin = new Thickness(2),
                Padding = new Thickness(9, 5, 9, 5),
                MinWidth = 70,
                Foreground = Brushes.White,
                Background = new SolidColorBrush(selected ? Color.FromRgb(70, 86, 118) : Color.FromRgb(50, 54, 64)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(78, 82, 94))
            };
            if (action != null) button.Click += delegate { action(); };
            return button;
        }

        protected static WrapPanel Row(params UIElement[] children)
        {
            WrapPanel panel = new WrapPanel { Margin = new Thickness(0, 2, 0, 4) };
            for (int i = 0; i < children.Length; i++) panel.Children.Add(children[i]);
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
                Root.Children.Add(Text("Open the ADOFAI level editor first.", 14, false));
                return;
            }

            Root.Children.Add(Row(
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
                Root.Children.Add(Text("No tracks yet. Store the current chart to create one.", 14, false));
                return;
            }

            for (int i = 0; i < Snapshot.Tracks.Count; i++)
            {
                TrackSnapshot track = Snapshot.Tracks[i];
                bool active = track.Index == Snapshot.ActiveIndex;
                int index = track.Index;
                Root.Children.Add(Row(
                    ActionButton((active ? "> " : "") + track.Name, delegate
                    {
                        WorkbenchIntegration.RunOnUnity(delegate { TrackStore.Current.SwitchTo(ADOBase.editor, index); });
                    }, active),
                    ActionButton("×", delegate
                    {
                        WorkbenchIntegration.RunOnUnity(delegate { TrackStore.Current.Remove(ADOBase.editor, index); });
                    }, false),
                    Text("F" + track.RegionStartFloor + "   " + track.Layout, 13, false)));
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
                Root.Children.Add(Text("Choose or store a source track first.", 14, false));
                return;
            }

            Root.Children.Add(Text(
                Snapshot.ActiveName + "   start F" + Snapshot.RegionStartFloor + "   cursor F" + Snapshot.CursorFloor,
                15,
                false));

            Root.Children.Add(Row(
                Text("Pivot", 14, false),
                ActionButton(Snapshot.PivotIsA ? "A" : "B", delegate
                {
                    WorkbenchIntegration.RunOnUnity(delegate
                    {
                        TrackSlot track = TrackStore.Current.Tracks[TrackStore.Current.ActiveIndex];
                        track.PivotIsA = !track.PivotIsA;
                    });
                }, true)));

            Root.Children.Add(Row(
                Text("Wrap", 14, false),
                WrapButton("Off", CompactWrapMode.Off),
                WrapButton("Tiles", CompactWrapMode.Tiles),
                WrapButton("Beats", CompactWrapMode.Beats)));

            Root.Children.Add(Row(
                Text("Repeat", 14, false),
                ActionButton("−", delegate { AdjustRepeat(-1); }, false),
                Text("×" + Snapshot.RepeatCount, 15, true),
                ActionButton("+", delegate { AdjustRepeat(1); }, false),
                ActionButton(Snapshot.ReuseRepeatPath ? "Reuse path: ON" : "Reuse path: OFF", delegate
                {
                    WorkbenchIntegration.RunOnUnity(delegate
                    {
                        TrackSlot track = TrackStore.Current.Tracks[TrackStore.Current.ActiveIndex];
                        track.ReuseRepeatPath = !track.ReuseRepeatPath;
                    });
                }, Snapshot.ReuseRepeatPath)));

            Root.Children.Add(Text("Planet A tag: " + (string.IsNullOrWhiteSpace(Snapshot.PlanetATag) ? "<unset>" : Snapshot.PlanetATag), 13, false));
            Root.Children.Add(Text("Planet B tag: " + (string.IsNullOrWhiteSpace(Snapshot.PlanetBTag) ? "<unset>" : Snapshot.PlanetBTag), 13, false));
            Root.Children.Add(Spacer(8));
            Root.Children.Add(Text("Generation controls remain in the MTE UMM panel for now.", 13, false));
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
