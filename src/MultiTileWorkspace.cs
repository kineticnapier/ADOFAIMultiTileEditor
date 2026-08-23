using System;
using System.Collections.Generic;

namespace KineticNapier.ADOFAIMultiTileEditor
{
    internal enum WorkspaceLayoutMode
    {
        Single,
        TwoColumns,
        TwoRows,
        Grid2x2
    }

    internal sealed class WorkspacePaneState
    {
        internal TrackSlot Track;
    }

    internal sealed class MultiTileWorkspace
    {
        private readonly WorkspacePaneState[] panes =
        {
            new WorkspacePaneState(),
            new WorkspacePaneState(),
            new WorkspacePaneState(),
            new WorkspacePaneState()
        };

        internal WorkspaceLayoutMode LayoutMode = WorkspaceLayoutMode.TwoColumns;
        internal int ActivePaneIndex;

        internal int VisiblePaneCount
        {
            get
            {
                switch (LayoutMode)
                {
                    case WorkspaceLayoutMode.Single: return 1;
                    case WorkspaceLayoutMode.TwoColumns:
                    case WorkspaceLayoutMode.TwoRows: return 2;
                    default: return 4;
                }
            }
        }

        internal WorkspacePaneState GetPane(int index)
        {
            if (index < 0 || index >= panes.Length) throw new ArgumentOutOfRangeException("index");
            return panes[index];
        }

        internal void SetLayout(WorkspaceLayoutMode mode, IList<TrackSlot> tracks)
        {
            LayoutMode = mode;
            if (ActivePaneIndex >= VisiblePaneCount) ActivePaneIndex = VisiblePaneCount - 1;
            EnsureAssignments(tracks);
        }

        internal void EnsureAssignments(IList<TrackSlot> tracks)
        {
            if (tracks == null || tracks.Count == 0)
            {
                for (int i = 0; i < panes.Length; i++) panes[i].Track = null;
                ActivePaneIndex = 0;
                return;
            }

            int visible = VisiblePaneCount;
            var used = new List<TrackSlot>();

            for (int i = 0; i < visible; i++)
            {
                TrackSlot assigned = panes[i].Track;
                if (!ContainsReference(tracks, assigned) || ContainsReference(used, assigned))
                    panes[i].Track = null;
                else
                    used.Add(assigned);
            }

            for (int i = 0; i < visible; i++)
            {
                if (panes[i].Track != null) continue;
                TrackSlot next = FindFirstUnused(tracks, used);
                panes[i].Track = next;
                if (next != null) used.Add(next);
            }

            if (ActivePaneIndex < 0) ActivePaneIndex = 0;
            if (ActivePaneIndex >= visible) ActivePaneIndex = visible - 1;
        }

        internal void AssignToActivePane(IList<TrackSlot> tracks, TrackSlot track)
        {
            int pane = Math.Max(0, Math.Min(ActivePaneIndex, panes.Length - 1));
            SelectTrack(tracks, pane, track);
        }

        internal void SelectTrack(IList<TrackSlot> tracks, int paneIndex, TrackSlot track)
        {
            if (tracks == null || track == null) return;
            if (paneIndex < 0 || paneIndex >= panes.Length) return;
            if (!ContainsReference(tracks, track)) return;

            TrackSlot previous = panes[paneIndex].Track;
            if (ReferenceEquals(previous, track)) return;

            // A source track is shown in at most one visible group. Selecting a tab that is
            // already visible elsewhere swaps the two group assignments instead of creating
            // two competing views of the same mutable stock-editor snapshot.
            int otherPane = FindVisiblePane(track, paneIndex);
            panes[paneIndex].Track = track;
            if (otherPane >= 0)
                panes[otherPane].Track = previous;
        }

        internal int GetTrackIndex(IList<TrackSlot> tracks, int paneIndex)
        {
            if (tracks == null || paneIndex < 0 || paneIndex >= panes.Length) return -1;
            TrackSlot track = panes[paneIndex].Track;
            for (int i = 0; i < tracks.Count; i++)
                if (ReferenceEquals(tracks[i], track)) return i;
            return -1;
        }

        internal void CycleTrack(IList<TrackSlot> tracks, int paneIndex, int delta)
        {
            if (tracks == null || tracks.Count == 0 || paneIndex < 0 || paneIndex >= panes.Length) return;
            int current = GetTrackIndex(tracks, paneIndex);
            if (current < 0) current = 0;
            int next = (current + delta) % tracks.Count;
            if (next < 0) next += tracks.Count;
            SelectTrack(tracks, paneIndex, tracks[next]);
        }

        private int FindVisiblePane(TrackSlot track, int exceptPane)
        {
            int visible = VisiblePaneCount;
            for (int i = 0; i < visible; i++)
            {
                if (i == exceptPane) continue;
                if (ReferenceEquals(panes[i].Track, track)) return i;
            }
            return -1;
        }

        private static TrackSlot FindFirstUnused(IList<TrackSlot> tracks, IList<TrackSlot> used)
        {
            for (int i = 0; i < tracks.Count; i++)
                if (!ContainsReference(used, tracks[i])) return tracks[i];
            return null;
        }

        private static bool ContainsReference(IList<TrackSlot> tracks, TrackSlot target)
        {
            if (tracks == null || target == null) return false;
            for (int i = 0; i < tracks.Count; i++)
                if (ReferenceEquals(tracks[i], target)) return true;
            return false;
        }
    }
}
