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

        // Retained for workspace compatibility while paging is safety-disabled.
        internal bool DynamicPagingEnabled;
        internal int PageTiles = 64;
        internal string PageTilesText = "64";

        internal double LayoutOffsetX;
        internal double LayoutOffsetY;
        internal double AppliedLayoutOffsetX;
        internal double AppliedLayoutOffsetY;
        internal string LayoutOffsetXText = "0";
        internal string LayoutOffsetYText = "0";

        internal TrackSlot(string name, LevelData data, int cursorFloor)
        {
            Name = name;
            Data = data;
            CursorFloor = cursorFloor;
            RegionStartFloor = cursorFloor;
            PivotIsA = false;
            ReuseRepeatPath = true;
        }

        internal Vector2 LayoutOffset
        {
            get { return new Vector2((float)LayoutOffsetX, (float)LayoutOffsetY); }
        }

        internal Vector2 AppliedLayoutOffset
        {
            get { return new Vector2((float)AppliedLayoutOffsetX, (float)AppliedLayoutOffsetY); }
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

    // 0.18 model:
    // - TrackSlot.Data is an immutable saved snapshot in normal operation.
    // - ActiveIndex means only "selected in MTE UI", never "whatever is currently
    //   open in ADOFAI belongs to this track".
    // - Loading a track expands a COPY into the editor and never writes back.
    // - Existing Data changes only through ReplaceWithCurrent, an explicit action.
    internal sealed class TrackStore
    {
        private static TrackStore current;
        private readonly List<TrackSlot> tracks = new List<TrackSlot>();
        private int activeIndex = -1;
        private int nextAutoTagId = 1;
        private float nextAutosaveTime;

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
            nextAutosaveTime = 0f;
            TrackSlot.ReplaceRegistration(tracks);
            ChartSessionGuard.Reset();
        }

        internal void DetachActive()
        {
            activeIndex = -1;
        }

        internal bool TryRestoreWorkspace(scnEditor editor, out string message)
        {
            message = string.Empty;
            List<TrackSlot> restored;
            int restoredNextTag;
            if (!WorkspacePersistence.TryLoad(editor, out restored, out restoredNextTag, out message)) return false;

            tracks.Clear();
            for (int i = 0; i < restored.Count; i++)
            {
                TrackSlot track = restored[i];
                ClampFloors(track);
                tracks.Add(track);
            }
            nextAutoTagId = Math.Max(1, restoredNextTag);

            // Restoring a workspace never applies or selects a snapshot.
            activeIndex = -1;
            TrackSlot.ReplaceRegistration(tracks);
            ChartSessionGuard.AcceptCurrent(editor);
            return tracks.Count > 0;
        }

        internal void AutosaveTick(scnEditor editor)
        {
            if (tracks.Count == 0 || editor == null || editor.levelData == null) return;
            if (Time.realtimeSinceStartup < nextAutosaveTime) return;
            nextAutosaveTime = Time.realtimeSinceStartup + 5f;
            FlushAutosave(editor);
        }

        internal void FlushAutosave(scnEditor editor)
        {
            if (tracks.Count == 0 || editor == null || editor.levelData == null) return;
            try
            {
                // Critical invariant: autosave persists already stored snapshots only.
                // It NEVER captures editor.levelData into a TrackSlot.
                WorkspacePersistence.Save(editor, tracks, nextAutoTagId, false);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ADOFAIMultiTileEditor] Workspace autosave failed: " + ex.Message);
            }
        }

        internal void PersistMetadata(scnEditor editor)
        {
            if (tracks.Count == 0 || editor == null || editor.levelData == null) return;
            try { WorkspacePersistence.Save(editor, tracks, nextAutoTagId, false); }
            catch (Exception ex) { Debug.LogWarning("[ADOFAIMultiTileEditor] Workspace metadata save failed: " + ex.Message); }
        }

        private void PersistSnapshotMutation(scnEditor editor)
        {
            if (tracks.Count == 0 || editor == null || editor.levelData == null) return;
            WorkspacePersistence.Save(editor, tracks, nextAutoTagId, true);
        }

        internal void MarkGeneratedOffsetsApplied(scnEditor editor)
        {
            for (int i = 0; i < tracks.Count; i++)
            {
                TrackSlot track = tracks[i];
                track.AppliedLayoutOffsetX = track.LayoutOffsetX;
                track.AppliedLayoutOffsetY = track.LayoutOffsetY;
            }
            PersistMetadata(editor);
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
            int createdIndex = tracks.Count - 1;
            ClampFloors(slot);
            TrackSlot.ReplaceRegistration(tracks);
            ChartSessionGuard.AcceptCurrent(editor);
            PersistSnapshotMutation(editor);

            // Adding a snapshot does not mean the live editor is now "editing" it.
            activeIndex = -1;
            return createdIndex;
        }

        internal void Select(int index)
        {
            if (index < 0 || index >= tracks.Count) throw new ArgumentOutOfRangeException("index");
            activeIndex = index;
            TrackSlot.ReplaceRegistration(tracks);
        }

        internal void LoadCopy(scnEditor editor, int index)
        {
            if (editor == null) throw new InvalidOperationException("Editor is not ready.");
            if (index < 0 || index >= tracks.Count) throw new ArgumentOutOfRangeException("index");
            ChartSessionGuard.EnsureCurrent(editor);

            try { editor.SaveState(true, true); } catch { }
            RestoreSnapshot(editor, tracks[index].Data, true);

            int floor = tracks[index].CursorFloor;
            if (floor >= 0 && floor < editor.floors.Count)
                editor.SelectFloor(editor.floors[floor], true);
            TrackSlot.ReplaceRegistration(tracks);
            ChartSessionGuard.AcceptCurrent(editor);

            // Loading and selecting are deliberately orthogonal operations.
        }

        // Compatibility entry point for older pane code. It now means load-copy only;
        // it never saves the editor back into the previously selected snapshot.
        internal void SwitchTo(scnEditor editor, int index)
        {
            LoadCopy(editor, index);
        }

        // Compatibility entry point. Automatic callers may still invoke SaveActive in
        // old paths; it is deliberately metadata-only so it can never destroy a snapshot.
        internal void SaveActive(scnEditor editor)
        {
            PersistMetadata(editor);
        }

        internal void ReplaceActiveWithCurrent(scnEditor editor)
        {
            if (activeIndex < 0 || activeIndex >= tracks.Count)
                throw new InvalidOperationException("Select the snapshot to replace first.");
            ReplaceWithCurrent(editor, activeIndex);
        }

        internal void ReplaceWithCurrent(scnEditor editor, int index)
        {
            if (editor == null || editor.levelData == null) throw new InvalidOperationException("Editor is not ready.");
            if (index < 0 || index >= tracks.Count) throw new ArgumentOutOfRangeException("index");
            ChartSessionGuard.EnsureCurrent(editor);

            // Install the old collection as primary first. The history-rotating save
            // after mutation then preserves it as a recovery generation.
            WorkspacePersistence.Save(editor, tracks, nextAutoTagId, false);

            TrackSlot track = tracks[index];
            track.Data = editor.levelData.Copy();
            track.Angles = GameAngleProbe.Capture(editor);
            track.PreviewPositions = GameAngleProbe.CapturePositions(editor);
            int cursor = GameAngleProbe.TryGetCurrentFloorIndex(editor);
            if (cursor >= 0) track.CursorFloor = cursor;
            ClampFloors(track);
            TrackSlot.ReplaceRegistration(tracks);
            ChartSessionGuard.AcceptCurrent(editor);

            PersistSnapshotMutation(editor);
            // Replacement also does not bind the live editor to this snapshot.
        }

        internal void SetActiveRegionStartFromSelection(scnEditor editor)
        {
            if (activeIndex < 0 || activeIndex >= tracks.Count)
                throw new InvalidOperationException("Select a saved snapshot before changing its region start.");
            ChartSessionGuard.EnsureCurrent(editor);
            int selected = GameAngleProbe.TryGetSelectedFloorIndex(editor);
            if (selected < 0)
                throw new InvalidOperationException("Select the floor where Multi Tile should begin first.");

            TrackSlot track = tracks[activeIndex];
            track.RegionStartFloor = selected;
            ClampFloors(track);
            PersistMetadata(editor);
        }

        internal void Remove(scnEditor editor, int index)
        {
            if (index < 0 || index >= tracks.Count) return;
            if (tracks.Count > 0) ChartSessionGuard.EnsureCurrent(editor);

            WorkspacePersistence.Save(editor, tracks, nextAutoTagId, false);
            tracks.RemoveAt(index);

            if (tracks.Count == 0)
            {
                activeIndex = -1;
                TrackSlot.ReplaceRegistration(tracks);
                WorkspacePersistence.DeletePrimaryWithHistory(editor);
                ChartSessionGuard.AcceptCurrent(editor);
                return;
            }

            if (index < activeIndex) activeIndex--;
            else if (index == activeIndex) activeIndex = -1;
            TrackSlot.ReplaceRegistration(tracks);
            ChartSessionGuard.AcceptCurrent(editor);
            PersistSnapshotMutation(editor);
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
