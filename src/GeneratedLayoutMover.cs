using System;
using System.Collections;
using System.Globalization;
using ADOFAI;
using UnityEngine;

namespace KineticNapier.ADOFAIMultiTileEditor
{
    internal static class GeneratedLayoutMover
    {
        private const string OwnerTag = "adofaiMTEGenerated";
        private const float Epsilon = 0.00001f;

        internal static int Apply(scnEditor editor, TrackSlot track, int trackIndex)
        {
            if (editor == null || editor.levelData == null)
                throw new InvalidOperationException("Editor is not ready.");
            if (track == null) throw new InvalidOperationException("Track is unavailable.");

            // Treat X/Y as a relative nudge. This avoids stale absolute-position state
            // after regeneration: every Apply simply moves the currently generated group
            // by the requested amount, then resets the inputs to zero.
            Vector2 delta = track.LayoutOffset;
            if (delta.sqrMagnitude <= Epsilon * Epsilon) return 0;

            LevelData original = editor.levelData.Copy();
            LevelData candidate = original.Copy();
            int moved = 0;
            moved += MoveMatching(candidate.decorations as IList, track, trackIndex, delta);
            moved += MoveMatching(candidate.levelEvents as IList, track, trackIndex, delta);

            if (moved == 0)
            {
                throw new InvalidOperationException(
                    "No generated decorations for track '" + track.Name + "' were found in the current output. Generate Multi Tile first.");
            }

            int selectedFloor = GameAngleProbe.TryGetSelectedFloorIndex(editor);
            try
            {
                try { editor.SaveState(true, true); } catch { }
                TrackStore.RestoreSnapshot(editor, candidate, true);
                editor.ApplyEventsToFloors();
                editor.UpdateDecorationObjects();
            }
            catch
            {
                TrackStore.RestoreSnapshot(editor, original, true);
                throw;
            }

            if (selectedFloor >= 0 && selectedFloor < editor.floors.Count)
            {
                try { editor.SelectFloor(editor.floors[selectedFloor], true); } catch { }
            }

            track.AppliedLayoutOffsetX += track.LayoutOffsetX;
            track.AppliedLayoutOffsetY += track.LayoutOffsetY;
            track.LayoutOffsetX = 0.0;
            track.LayoutOffsetY = 0.0;
            track.LayoutOffsetXText = "0";
            track.LayoutOffsetYText = "0";
            return moved;
        }

        private static int MoveMatching(IList list, TrackSlot track, int trackIndex, Vector2 delta)
        {
            if (list == null) return 0;
            int moved = 0;
            string floorGroupTag = "T" + trackIndex;

            for (int i = 0; i < list.Count; i++)
            {
                LevelEvent ev = list[i] as LevelEvent;
                if (ev == null || !IsEventNamed(ev, "AddObject")) continue;

                string objectType = Convert.ToString(SafeGetData(ev, "objectType"), CultureInfo.InvariantCulture) ?? string.Empty;
                string tag = Convert.ToString(SafeGetData(ev, "tag"), CultureInfo.InvariantCulture) ?? string.Empty;
                bool isPlanet = string.Equals(objectType, "Planet", StringComparison.OrdinalIgnoreCase)
                    && (HasExactTag(tag, track.PlanetATag) || HasExactTag(tag, track.PlanetBTag));
                bool isGeneratedFloor = string.Equals(objectType, "Floor", StringComparison.OrdinalIgnoreCase)
                    && HasExactTag(tag, OwnerTag)
                    && HasExactTag(tag, floorGroupTag);
                if (!isPlanet && !isGeneratedFloor) continue;

                Vector2 position;
                if (!TryReadVector2(ev, "position", out position)) continue;
                SetTypedData(ev, "position", position + delta);
                moved++;
            }
            return moved;
        }

        private static bool HasExactTag(string tags, string requested)
        {
            if (string.IsNullOrWhiteSpace(tags) || string.IsNullOrWhiteSpace(requested)) return false;
            string[] split = tags.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < split.Length; i++)
                if (string.Equals(split[i], requested.Trim(), StringComparison.Ordinal)) return true;
            return false;
        }

        private static bool TryReadVector2(LevelEvent ev, string key, out Vector2 value)
        {
            object raw = SafeGetData(ev, key);
            if (raw is Vector2) { value = (Vector2)raw; return true; }
            if (raw is Vector3)
            {
                Vector3 v = (Vector3)raw;
                value = new Vector2(v.x, v.y);
                return true;
            }
            value = Vector2.zero;
            return false;
        }

        private static void SetTypedData(LevelEvent ev, string key, object value)
        {
            object current = SafeGetData(ev, key);
            if (current is Vector3 && value is Vector2)
            {
                Vector2 v = (Vector2)value;
                ev[key] = new Vector3(v.x, v.y, ((Vector3)current).z);
            }
            else ev[key] = value;
            if (ev.disabled != null && ev.disabled.ContainsKey(key)) ev.disabled[key] = false;
        }

        private static object SafeGetData(LevelEvent ev, string key)
        {
            if (ev == null) return null;
            try { return ev[key]; } catch { return null; }
        }

        private static bool IsEventNamed(LevelEvent ev, string requestedName)
        {
            if (ev == null) return false;
            string infoName = ev.info != null ? ev.info.name : string.Empty;
            if (string.Equals(infoName, requestedName, StringComparison.OrdinalIgnoreCase)) return true;
            if (!string.IsNullOrEmpty(infoName) && infoName.EndsWith(requestedName, StringComparison.OrdinalIgnoreCase)) return true;
            return string.Equals(ev.eventType.ToString(), requestedName, StringComparison.OrdinalIgnoreCase);
        }
    }
}
