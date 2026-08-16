using System;
using System.Collections.Generic;
using ADOFAI;

namespace KineticNapier.ADOFAIMultiTileEditor
{
    internal sealed class TrackSlot
    {
        internal string Name;
        internal LevelData Data;
        internal int CursorFloor;
        internal List<AngleSample> Angles = new List<AngleSample>();
        internal string PlanetATag = "";
        internal string PlanetBTag = "";
        internal bool PivotIsA;

        internal TrackSlot(string name, LevelData data, int cursorFloor)
        {
            Name = name;
            Data = data;
            CursorFloor = cursorFloor;
            PivotIsA = false;
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

        internal string MovingTag { get { return PivotIsA ? PlanetBTag : PlanetATag; } }
        internal string CenterTag { get { return PivotIsA ? PlanetATag : PlanetBTag; } }

        internal bool TagsReady
        {
            get
            {
                return !string.IsNullOrWhiteSpace(PlanetATag)
                    && !string.IsNullOrWhiteSpace(PlanetBTag)
                    && !string.Equals(PlanetATag.Trim(), PlanetBTag.Trim(), StringComparison.Ordinal);
            }
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
            ClampCursor(slot);
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
            ClampCursor(track);
        }

        internal void RefreshActiveAngles(scnEditor editor)
        {
            if (activeIndex < 0 || activeIndex >= tracks.Count || editor == null) return;
            TrackSlot track = tracks[activeIndex];
            track.Angles = GameAngleProbe.Capture(editor);
            ClampCursor(track);
        }

        internal void SwitchTo(scnEditor editor, int index)
        {
            if (editor == null) throw new InvalidOperationException("Editor is not ready.");
            if (index < 0 || index >= tracks.Count) throw new ArgumentOutOfRangeException("index");
            if (index == activeIndex) return;

            SaveActive(editor);
            Restore(editor, tracks[index].Data);
            activeIndex = index;
            tracks[index].Angles = GameAngleProbe.Capture(editor);
            ClampCursor(tracks[index]);

            int floor = tracks[index].CursorFloor;
            if (floor >= 0 && floor < editor.floors.Count)
                editor.SelectFloor(editor.floors[floor], true);
        }

        internal void SetCursor(int index, int floor)
        {
            if (index < 0 || index >= tracks.Count) return;
            tracks[index].CursorFloor = floor;
            ClampCursor(tracks[index]);
        }

        internal void AdvanceAll(int delta)
        {
            for (int i = 0; i < tracks.Count; i++)
            {
                tracks[i].CursorFloor += delta;
                ClampCursor(tracks[i]);
            }
        }

        internal void SetAllCursors(int floor)
        {
            for (int i = 0; i < tracks.Count; i++)
            {
                tracks[i].CursorFloor = floor;
                ClampCursor(tracks[i]);
            }
        }

        internal void CommitGeneratedStep()
        {
            for (int i = 0; i < tracks.Count; i++)
            {
                tracks[i].PivotIsA = !tracks[i].PivotIsA;
                tracks[i].CursorFloor++;
                ClampCursor(tracks[i]);
            }
        }

        internal void Remove(scnEditor editor, int index)
        {
            if (index < 0 || index >= tracks.Count) return;
            bool removingActive = index == activeIndex;
            tracks.RemoveAt(index);

            if (tracks.Count == 0)
            {
                activeIndex = -1;
                return;
            }

            if (index < activeIndex) activeIndex--;
            else if (removingActive)
            {
                activeIndex = Math.Min(index, tracks.Count - 1);
                Restore(editor, tracks[activeIndex].Data);
                tracks[activeIndex].Angles = GameAngleProbe.Capture(editor);
                ClampCursor(tracks[activeIndex]);
            }
        }

        internal int GetSharedPrefixAngleCount()
        {
            if (tracks.Count < 2) return tracks.Count == 1 && tracks[0].Data != null ? tracks[0].Data.angleData.Count : 0;
            int min = int.MaxValue;
            for (int i = 0; i < tracks.Count; i++)
            {
                if (tracks[i].Data == null || tracks[i].Data.angleData == null) return 0;
                min = Math.Min(min, tracks[i].Data.angleData.Count);
            }

            int equal = 0;
            for (int i = 0; i < min; i++)
            {
                float expected = tracks[0].Data.angleData[i];
                bool same = true;
                for (int t = 1; t < tracks.Count; t++)
                {
                    if (Math.Abs(tracks[t].Data.angleData[i] - expected) > 0.0001f)
                    {
                        same = false;
                        break;
                    }
                }
                if (!same) break;
                equal++;
            }
            return equal;
        }

        private static void ClampCursor(TrackSlot track)
        {
            int max = track.Angles != null && track.Angles.Count > 0
                ? track.Angles.Count - 1
                : (track.Data != null && track.Data.angleData != null ? Math.Max(0, track.Data.angleData.Count - 1) : 0);
            track.CursorFloor = Math.Max(0, Math.Min(track.CursorFloor, max));
        }

        private static void Restore(scnEditor editor, LevelData snapshot)
        {
            if (snapshot == null) throw new InvalidOperationException("Track snapshot is empty.");

            editor.customLevel.levelData = snapshot.Copy();
            editor.DeselectFloors(false);
            editor.RemakePath(true, true);
            editor.DeselectAllDecorations();
            editor.UpdateDecorationObjects();
        }
    }
}
