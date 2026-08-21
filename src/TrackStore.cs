using System;
using System.Collections.Generic;
using ADOFAI;

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
        internal string PlanetATag = "";
        internal string PlanetBTag = "";
        internal bool PivotIsA;

        // Visual-layout / virtual-repeat settings belong to each planet group.
        internal CompactWrapMode WrapMode = CompactWrapMode.Tiles;
        internal int WrapEveryTiles = 32;
        internal double WrapEveryBeats = 16.0;
        internal int RepeatCount = 1;
        internal bool ReuseRepeatPath;

        // Keep in-progress text per group so switching tabs does not destroy edits.
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
            // Virtual repetition is harmless at x1 and makes entering xN enough to use it.
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
        private readonly List<TrackSlot> tracks = new List<TrackSlot>();
        private int activeIndex = -1;

        internal IList<TrackSlot> Tracks { get { return tracks; } }
        internal int ActiveIndex { get { return activeIndex; } }

        internal void Reset()
        {
            tracks.Clear();
            activeIndex = -1;
            TrackSlot.ReplaceRegistration(tracks);
        }

        internal void DetachActive()
        {
            activeIndex = -1;
        }

        internal int StoreCurrent(scnEditor editor, string name)
        {
            if (editor == null || editor.levelData == null) throw new InvalidOperationException("Editor is not ready.");
            if (string.IsNullOrWhiteSpace(name)) name = "Track " + (tracks.Count + 1);

            int cursor = GameAngleProbe.TryGetCurrentFloorIndex(editor);
            if (cursor < 0) cursor = Math.Max(0, editor.floors.Count - 1);

            var slot = new TrackSlot(name.Trim(), editor.levelData.Copy(), cursor);
            slot.Angles = GameAngleProbe.Capture(editor);
            tracks.Add(slot);
            activeIndex = tracks.Count - 1;
            ClampFloors(slot);
            TrackSlot.ReplaceRegistration(tracks);
            return activeIndex;
        }

        internal void SaveActive(scnEditor editor)
        {
            if (activeIndex < 0 || activeIndex >= tracks.Count || editor == null || editor.levelData == null) return;
            TrackSlot track = tracks[activeIndex];
            track.Data = editor.levelData.Copy();
            track.Angles = GameAngleProbe.Capture(editor);

            int selected = GameAngleProbe.TryGetCurrentFloorIndex(editor);
            if (selected >= 0) track.CursorFloor = selected;
            ClampFloors(track);
            TrackSlot.ReplaceRegistration(tracks);
        }

        internal void SetActiveRegionStartFromSelection(scnEditor editor)
        {
            if (activeIndex < 0 || activeIndex >= tracks.Count)
                throw new InvalidOperationException("Switch to a source track before changing its region start.");
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
            if (index == activeIndex) return;

            SaveActive(editor);
            RestoreSnapshot(editor, tracks[index].Data, true);
            activeIndex = index;
            tracks[index].Angles = GameAngleProbe.Capture(editor);
            ClampFloors(tracks[index]);
            TrackSlot.ReplaceRegistration(tracks);

            int floor = tracks[index].CursorFloor;
            if (floor >= 0 && floor < editor.floors.Count)
                editor.SelectFloor(editor.floors[floor], true);
        }

        internal void Remove(scnEditor editor, int index)
        {
            if (index < 0 || index >= tracks.Count) return;
            bool removingActive = index == activeIndex;
            tracks.RemoveAt(index);

            if (tracks.Count == 0)
            {
                activeIndex = -1;
                TrackSlot.ReplaceRegistration(tracks);
                return;
            }

            if (index < activeIndex) activeIndex--;
            else if (removingActive)
            {
                activeIndex = Math.Min(index, tracks.Count - 1);
                RestoreSnapshot(editor, tracks[activeIndex].Data, true);
                tracks[activeIndex].Angles = GameAngleProbe.Capture(editor);
                ClampFloors(tracks[activeIndex]);
            }
            TrackSlot.ReplaceRegistration(tracks);
        }

        internal static void RestoreSnapshot(scnEditor editor, LevelData snapshot, bool updateDecorations)
        {
            if (editor == null) throw new InvalidOperationException("Editor is not ready.");
            if (snapshot == null) throw new InvalidOperationException("Track snapshot is empty.");

            editor.customLevel.levelData = snapshot.Copy();
            editor.DeselectFloors(false);
            editor.RemakePath(true, true);
            if (updateDecorations)
            {
                editor.DeselectAllDecorations();
                editor.UpdateDecorationObjects();
            }
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
