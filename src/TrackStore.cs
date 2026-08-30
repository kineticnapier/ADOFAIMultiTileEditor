using System;
using System.Collections.Generic;
using ADOFAI;
using UnityEngine;

namespace KineticNapier.ADOFAIMultiTileEditor
{
    internal sealed class TrackSlot
    {
        private static readonly Dictionary<int, TrackSlot> Registered = new Dictionary<int, TrackSlot>();

        internal string Name;
        internal LevelData Data;
        internal int CursorFloor;
        internal int RegionStartFloor;
        internal List<AngleSample> Angles = new List<AngleSample>();
        internal List<Vector2> PreviewPositions = new List<Vector2>();
        internal string PlanetATag = "";
        internal string PlanetBTag = "";
        internal bool PivotIsA;

        internal CompactWrapMode WrapMode = CompactWrapMode.Tiles;
        internal int WrapEveryTiles = 32;
        internal double WrapEveryBeats = 16.0;
        internal int RepeatCount = 1;
        internal bool ReuseRepeatPath;

        internal string WrapTilesText = "32";
        internal string WrapBeatsText = "16";
        internal string RepeatCountText = "1";

        internal TrackSlot(string name, LevelData data, int cursorFloor)
        {
            Name = name;
            Data = data;
            CursorFloor = cursorFloor;
            RegionStartFloor = cursorFloor;
            PivotIsA = false;
            ReuseRepeatPath = true;
        }

        internal AngleSample CurrentAngle
        {
            get
            {
                if (CursorFloor < 0 || CursorFloor >= Angles.Count)
                    return AngleSample.Invalid("cursor out of range");
                return Angles[CursorFloor];
            }
        }

        internal bool TagsReady
        {
            get
            {
                return !string.IsNullOrWhiteSpace(PlanetATag)
                    && !string.IsNullOrWhiteSpace(PlanetBTag)
                    && !string.Equals(PlanetATag.Trim(), PlanetBTag.Trim(), StringComparison.Ordinal);
            }
        }

        internal int EffectiveRepeatCount
        {
            get { return ReuseRepeatPath ? Math.Max(1, RepeatCount) : 1; }
        }

        internal static bool TryGetRegistered(int index, out TrackSlot slot)
        {
            return Registered.TryGetValue(index, out slot) && slot != null;
        }

        internal static void ReplaceRegistration(IList<TrackSlot> tracks)
        {
            Registered.Clear();
            if (tracks == null) return;
            for (int i = 0; i < tracks.Count; i++)
                if (tracks[i] != null) Registered[i] = tracks[i];
        }
    }

    internal sealed class TrackStore
    {
        private static TrackStore current;
        private readonly List<TrackSlot> tracks = new List<TrackSlot>();
        private int activeIndex = -1;
        private int nextAutoTagId = 1;

        internal TrackStore()
        {
            current = this;
        }

        internal static TrackStore Current { get { return current; } }
        internal IList<TrackSlot> Tracks { get { return tracks; } }
        internal int ActiveIndex { get { return activeIndex; } }

        internal void Reset()
        {
            tracks.Clear();
            activeIndex = -1;
            nextAutoTagId = 1;
            TrackSlot.ReplaceRegistration(tracks);
            ChartSessionGuard.Reset();
        }

        internal void DetachActive()
        {
            activeIndex = -1;
        }

        internal int StoreCurrent(scnEditor editor, string name)
        {
            if (editor == null || editor.levelData == null) throw new InvalidOperationException("Editor is not ready.");
            if (tracks.Count == 0) ChartSessionGuard.AcceptCurrent(editor);
            else ChartSessionGuard.EnsureCurrent(editor);
            if (string.IsNullOrWhiteSpace(name)) name = "Track " + (tracks.Count + 1);

            int cursor = GameAngleProbe.TryGetCurrentFloorIndex(editor);
            if (cursor < 0) cursor = Math.Max(0, editor.floors.Count - 1);

            int tagId = nextAutoTagId++;
            var slot = new TrackSlot(name.Trim(), editor.levelData.Copy(), cursor)
            {
                PlanetATag = "MTE_P" + tagId + "_A",
                PlanetBTag = "MTE_P" + tagId + "_B"
            };
            if (tracks.Count > 0)
                slot.RegionStartFloor = tracks[0].RegionStartFloor;

            slot.Angles = GameAngleProbe.Capture(editor);
            slot.PreviewPositions = GameAngleProbe.CapturePositions(editor);
            tracks.Add(slot);
            activeIndex = tracks.Count - 1;
            ClampFloors(slot);
            TrackSlot.ReplaceRegistration(tracks);
            ChartSessionGuard.AcceptCurrent(editor);
            return activeIndex;
        }

        internal void SaveActive(scnEditor editor)
        {
            if (activeIndex < 0 || activeIndex >= tracks.Count || editor == null || editor.levelData == null) return;
            ChartSessionGuard.EnsureCurrent(editor);
            TrackSlot track = tracks[activeIndex];
            track.Data = editor.levelData.Copy();
            track.Angles = GameAngleProbe.Capture(editor);
            track.PreviewPositions = GameAngleProbe.CapturePositions(editor);

            int selected = GameAngleProbe.TryGetCurrentFloorIndex(editor);
            if (selected >= 0) track.CursorFloor = selected;
            ClampFloors(track);
            TrackSlot.ReplaceRegistration(tracks);
            ChartSessionGuard.AcceptCurrent(editor);
        }

        internal void SetActiveRegionStartFromSelection(scnEditor editor)
        {
            if (activeIndex < 0 || activeIndex >= tracks.Count)
                throw new InvalidOperationException("Switch to a source track before changing its region start.");
            ChartSessionGuard.EnsureCurrent(editor);
            int selected = GameAngleProbe.TryGetSelectedFloorIndex(editor);
            if (selected < 0)
                throw new InvalidOperationException("Select the floor where Multi Tile should begin first.");

            TrackSlot track = tracks[activeIndex];
            track.RegionStartFloor = selected;
            ClampFloors(track);
        }

        internal void SwitchTo(scnEditor editor, int index)
        {
            if (editor == null) throw new InvalidOperationException("Editor is not ready.");
            if (index < 0 || index >= tracks.Count) throw new ArgumentOutOfRangeException("index");
            ChartSessionGuard.EnsureCurrent(editor);
            if (index == activeIndex) return;

            SaveActive(editor);
            RestoreSnapshot(editor, tracks[index].Data, true);
            activeIndex = index;
            tracks[index].Angles = GameAngleProbe.Capture(editor);
            tracks[index].PreviewPositions = GameAngleProbe.CapturePositions(editor);
            ClampFloors(tracks[index]);
            TrackSlot.ReplaceRegistration(tracks);

            int floor = tracks[index].CursorFloor;
            if (floor >= 0 && floor < editor.floors.Count)
                editor.SelectFloor(editor.floors[floor], true);
        }

        internal void Remove(scnEditor editor, int index)
        {
            if (index < 0 || index >= tracks.Count) return;
            if (tracks.Count > 0) ChartSessionGuard.EnsureCurrent(editor);
            bool removingActive = index == activeIndex;
            tracks.RemoveAt(index);

            if (tracks.Count == 0)
            {
                activeIndex = -1;
                TrackSlot.ReplaceRegistration(tracks);
                ChartSessionGuard.AcceptCurrent(editor);
                return;
            }

            if (index < activeIndex) activeIndex--;
            else if (removingActive)
            {
                activeIndex = Math.Min(index, tracks.Count - 1);
                RestoreSnapshot(editor, tracks[activeIndex].Data, true);
                tracks[activeIndex].Angles = GameAngleProbe.Capture(editor);
                tracks[activeIndex].PreviewPositions = GameAngleProbe.CapturePositions(editor);
                ClampFloors(tracks[activeIndex]);
            }
            TrackSlot.ReplaceRegistration(tracks);
            ChartSessionGuard.AcceptCurrent(editor);
        }

        internal static void RestoreSnapshot(scnEditor editor, LevelData snapshot, bool updateDecorations)
        {
            if (editor == null) throw new InvalidOperationException("Editor is not ready.");
            if (snapshot == null) throw new InvalidOperationException("Track snapshot is empty.");
            ChartSessionGuard.EnsureCurrent(editor);

            editor.customLevel.levelData = snapshot.Copy();
            ChartSessionGuard.AcceptCurrent(editor);
            editor.DeselectFloors(false);
            editor.RemakePath(true, true);
            if (updateDecorations)
            {
                editor.DeselectAllDecorations();
                editor.UpdateDecorationObjects();
            }
            ChartSessionGuard.AcceptCurrent(editor);
        }

        private static void ClampFloors(TrackSlot track)
        {
            int max = track.Angles != null && track.Angles.Count > 0
                ? track.Angles.Count - 1
                : (track.Data != null && track.Data.angleData != null ? Math.Max(0, track.Data.angleData.Count - 1) : 0);
            track.CursorFloor = Math.Max(0, Math.Min(track.CursorFloor, max));
            track.RegionStartFloor = Math.Max(0, Math.Min(track.RegionStartFloor, max));
        }
    }
}
