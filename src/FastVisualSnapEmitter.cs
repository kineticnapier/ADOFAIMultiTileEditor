using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using ADOFAI;
using UnityEngine;

namespace KineticNapier.ADOFAIMultiTileEditor
{
    // PACL2 OrbitDecoration is excellent for ordinary visual motion, but very short
    // tweens can be interrupted before DOTween/PACL2 writes their exact endpoint.
    // Once that happens the next orbit starts from the stale transform and the angular
    // error accumulates. For segments selected by TrackAnalyzer's high-speed guard,
    // OrbitEmitter omits the unstable tween and this pass places the moving planet at
    // the mathematically exact endpoint at the hit boundary.
    //
    // The positionOffset is absolute relative to the AddObject planet's original
    // position (the same model used by the older merge_adofai_visual_v6 prototype).
    // CompactLayoutPostProcessor runs after this pass, so PositionTrack/layout/repeat
    // teleports are still applied on top of the natural endpoint in the correct order.
    internal static class FastVisualSnapEmitter
    {
        private const string EventTag = "adofaiMTEFastVisualSnap";
        private const float PositionEpsilon = 0.00001f;

        private sealed class SourceGeometry
        {
            internal int TrackIndex;
            internal int SourceCycleSegments;
            internal Vector2 InitialA;
            internal Vector2 InitialB;
            internal readonly List<Vector2> Natural = new List<Vector2>();
        }

        internal static string ApplyAndCommit(
            scnEditor editor,
            IList<TrackSlot> tracks,
            GenerationPlan plan)
        {
            if (editor == null || editor.levelData == null)
                throw new InvalidOperationException("Editor is not ready.");
            if (tracks == null || plan == null || tracks.Count != plan.Tracks.Count)
                throw new InvalidOperationException("Fast-visual snap inputs no longer match the analyzed plan.");

            int requested = CountRequested(plan);
            LevelData output = editor.levelData.Copy();
            int selectedFloor = GameAngleProbe.TryGetSelectedFloorIndex(editor);
            List<SourceGeometry> geometries = CaptureGeometries(editor, tracks, plan, output, selectedFloor);
            LevelData candidate = output.Copy();
            IList actions = candidate.levelEvents as IList;
            if (actions == null)
                throw new InvalidOperationException("LevelData.levelEvents is not list-compatible in this game build.");

            int removed = RemoveOwned(actions);
            int emitted = 0;

            for (int t = 0; t < plan.Tracks.Count; t++)
            {
                AnalyzedTrack track = plan.Tracks[t];
                SourceGeometry geometry = geometries[t];
                int n = geometry.SourceCycleSegments;
                if (n <= 0) continue;

                for (int s = 0; s < track.Segments.Count; s++)
                {
                    TrackSegment segment = track.Segments[s];
                    if (!segment.UseInstantVisualSnap) continue;

                    int sourceSegment = s % n;
                    int targetIndex = sourceSegment + 1;
                    if (targetIndex < 0 || targetIndex >= geometry.Natural.Count)
                        throw new InvalidOperationException("Ultra-fast visual endpoint is outside source geometry for track '" + track.Name + "'.");

                    Vector2 basePosition;
                    if (string.Equals(segment.MovingTag, track.PlanetATag, StringComparison.Ordinal))
                        basePosition = geometry.InitialA;
                    else if (string.Equals(segment.MovingTag, track.PlanetBTag, StringComparison.Ordinal))
                        basePosition = geometry.InitialB;
                    else
                        throw new InvalidOperationException("Ultra-fast visual segment has an unknown moving planet tag on track '" + track.Name + "'.");

                    Vector2 offset = geometry.Natural[targetIndex] - basePosition;
                    int anchor = TimelineMerger.FindAnchorIndex(plan.Anchors, segment.EndBeat);
                    if (anchor < 0)
                        throw new InvalidOperationException("Ultra-fast visual endpoint could not be mapped to the master timeline for track '" + track.Name + "'.");

                    LevelEvent move = CreateEvent("MoveDecorations", plan.RegionStartFloor + anchor);
                    SetRequiredData(move, "duration", 0f);
                    SetRequiredData(move, "tag", segment.MovingTag);
                    SetRequiredData(move, "positionOffset", offset);
                    SetOptionalData(move, "angleOffset", 0f);
                    SetOptionalData(move, "ease", "Linear");
                    SetOptionalData(move, "eventTag", EventTag);
                    SetOptionalData(move, "active", true);
                    InsertBeforeOrbitAtFloor(actions, move, plan.RegionStartFloor + anchor);
                    emitted++;
                }
            }

            if (emitted != requested)
                throw new InvalidOperationException("Fast-visual snap emission mismatch: expected " + requested + ", built " + emitted + ".");

            bool committed = false;
            try
            {
                TrackStore.RestoreSnapshot(editor, candidate, true);
                editor.ApplyEventsToFloors();
                editor.UpdateDecorationObjects();
                committed = true;
            }
            finally
            {
                if (!committed)
                {
                    TrackStore.RestoreSnapshot(editor, output, true);
                    if (selectedFloor >= 0 && selectedFloor < editor.floors.Count)
                        editor.SelectFloor(editor.floors[selectedFloor], true);
                }
            }

            return "Fast visual guard: " + emitted + " exact endpoint snap(s)"
                + (removed > 0 ? "; replaced " + removed + " previous snap event(s)." : ".");
        }

        private static int CountRequested(GenerationPlan plan)
        {
            int count = 0;
            for (int t = 0; t < plan.Tracks.Count; t++)
                for (int s = 0; s < plan.Tracks[t].Segments.Count; s++)
                    if (plan.Tracks[t].Segments[s].UseInstantVisualSnap) count++;
            return count;
        }

        private static List<SourceGeometry> CaptureGeometries(
            scnEditor editor,
            IList<TrackSlot> tracks,
            GenerationPlan plan,
            LevelData output,
            int selectedFloor)
        {
            var result = new List<SourceGeometry>();
            try
            {
                for (int t = 0; t < tracks.Count; t++)
                {
                    TrackSlot slot = tracks[t];
                    if (slot == null || slot.Data == null)
                        throw new InvalidOperationException("Track #" + (t + 1) + " has no source snapshot.");
                    TrackStore.RestoreSnapshot(editor, slot.Data, false);
                    result.Add(CaptureCurrent(editor, slot, plan.Tracks[t], t));
                }
            }
            finally
            {
                TrackStore.RestoreSnapshot(editor, output, true);
                if (selectedFloor >= 0 && selectedFloor < editor.floors.Count)
                    editor.SelectFloor(editor.floors[selectedFloor], true);
            }
            return result;
        }

        private static SourceGeometry CaptureCurrent(
            scnEditor editor,
            TrackSlot slot,
            AnalyzedTrack analyzed,
            int trackIndex)
        {
            if (slot.RegionStartFloor < 0 || slot.RegionStartFloor >= editor.floors.Count)
                throw new InvalidOperationException("Track '" + slot.Name + "' fast-visual start is outside its source path.");

            scrFloor requestedStart = editor.floors[slot.RegionStartFloor];
            if (requestedStart == null || requestedStart.midSpin)
                throw new InvalidOperationException("Track '" + slot.Name + "' fast-visual start must be landable.");

            var floors = new List<scrFloor>();
            int regionIndex = -1;
            for (int i = 0; i < editor.floors.Count; i++)
            {
                scrFloor floor = editor.floors[i];
                if (floor == null || floor.midSpin) continue;
                if (ReferenceEquals(floor, requestedStart)) regionIndex = floors.Count;
                floors.Add(floor);
            }
            if (regionIndex < 0 || regionIndex + 1 >= floors.Count)
                throw new InvalidOperationException("Track '" + slot.Name + "' fast-visual geometry could not be reconstructed.");

            int n = floors.Count - regionIndex - 1;
            if (analyzed.Segments.Count != n * slot.EffectiveRepeatCount)
                throw new InvalidOperationException("Track '" + slot.Name + "' fast-visual geometry no longer matches its analyzed repeat plan.");

            float tileSize = ResolveTileSize();
            Vector2 start = ToLevelPosition(floors[regionIndex], tileSize);
            Vector2 incoming;
            if (regionIndex > 0)
                incoming = ToLevelPosition(floors[regionIndex - 1], tileSize) - start;
            else
                incoming = ToLevelPosition(floors[regionIndex + 1], tileSize) - start;

            var geometry = new SourceGeometry
            {
                TrackIndex = trackIndex,
                SourceCycleSegments = n,
                InitialA = analyzed.InitialPivotIsA ? Vector2.zero : incoming,
                InitialB = analyzed.InitialPivotIsA ? incoming : Vector2.zero
            };

            BuildNaturalPositions(floors, regionIndex, geometry.Natural);
            return geometry;
        }

        private static void BuildNaturalPositions(
            IList<scrFloor> floors,
            int regionIndex,
            IList<Vector2> destination)
        {
            destination.Clear();
            destination.Add(Vector2.zero);
            for (int i = regionIndex; i + 1 < floors.Count; i++)
            {
                scrFloor floor = floors[i];
                float heading = 90f - (float)(floor.exitangle * Mathf.Rad2Deg);
                float radians = heading * Mathf.Deg2Rad;
                Vector2 step = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
                if (step.sqrMagnitude <= PositionEpsilon * PositionEpsilon) step = Vector2.right;
                else step.Normalize();
                destination.Add(destination[destination.Count - 1] + step);
            }
        }

        private static int RemoveOwned(IList actions)
        {
            int removed = 0;
            for (int i = actions.Count - 1; i >= 0; i--)
            {
                LevelEvent ev = actions[i] as LevelEvent;
                if (ev == null || !IsEventNamed(ev, "MoveDecorations")) continue;
                string tag = Convert.ToString(SafeGetData(ev, "eventTag"), CultureInfo.InvariantCulture) ?? "";
                if (!ContainsTagToken(tag, EventTag)) continue;
                actions.RemoveAt(i);
                removed++;
            }
            return removed;
        }

        private static void InsertBeforeOrbitAtFloor(IList actions, LevelEvent move, int floor)
        {
            int insert = actions.Count;
            for (int i = 0; i < actions.Count; i++)
            {
                LevelEvent ev = actions[i] as LevelEvent;
                if (ev == null) continue;
                if (ev.floor > floor)
                {
                    insert = i;
                    break;
                }
                if (ev.floor == floor && IsEventNamed(ev, "OrbitDecoration"))
                {
                    insert = i;
                    break;
                }
            }
            actions.Insert(insert, move);
        }

        private static LevelEvent CreateEvent(string requestedName, int floor)
        {
            LevelEventInfo info = ResolveEventInfo(requestedName);
            return new LevelEvent(floor, info.type, info);
        }

        private static LevelEventInfo ResolveEventInfo(string requestedName)
        {
            if (GCS.levelEventsInfo == null)
                throw new InvalidOperationException("ADOFAI level-event metadata is not initialized.");
            LevelEventInfo direct;
            if (GCS.levelEventsInfo.TryGetValue(requestedName, out direct) && direct != null) return direct;

            string target = NormalizeEventName(requestedName);
            LevelEventInfo suffix = null;
            foreach (KeyValuePair<string, LevelEventInfo> pair in GCS.levelEventsInfo)
            {
                LevelEventInfo info = pair.Value;
                if (info == null) continue;
                string key = NormalizeEventName(pair.Key);
                string name = NormalizeEventName(info.name);
                if (key == target || name == target) return info;
                if (key.EndsWith(target, StringComparison.Ordinal) || name.EndsWith(target, StringComparison.Ordinal)) suffix = info;
            }
            if (suffix != null) return suffix;
            throw new InvalidOperationException("Event metadata '" + requestedName + "' is unavailable.");
        }

        private static string NormalizeEventName(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            var sb = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
            }
            return sb.ToString();
        }

        private static bool IsEventNamed(LevelEvent ev, string requestedName)
        {
            if (ev == null) return false;
            string target = NormalizeEventName(requestedName);
            string infoName = ev.info != null ? NormalizeEventName(ev.info.name) : "";
            if (infoName == target || infoName.EndsWith(target, StringComparison.Ordinal)) return true;
            return NormalizeEventName(ev.eventType.ToString()) == target;
        }

        private static bool ContainsTagToken(string tags, string token)
        {
            if (string.IsNullOrEmpty(tags) || string.IsNullOrEmpty(token)) return false;
            string[] split = tags.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < split.Length; i++)
                if (string.Equals(split[i], token, StringComparison.Ordinal)) return true;
            return false;
        }

        private static void SetRequiredData(LevelEvent ev, string key, object value)
        {
            if (ev == null || ev.info == null || ev.info.propertiesInfo == null || !ev.info.propertiesInfo.ContainsKey(key))
                throw new InvalidOperationException("Event '" + EventDisplayName(ev) + "' has no property '" + key + "'.");
            SetTypedData(ev, key, value);
        }

        private static void SetOptionalData(LevelEvent ev, string key, object value)
        {
            if (ev == null || ev.info == null || ev.info.propertiesInfo == null || !ev.info.propertiesInfo.ContainsKey(key)) return;
            SetTypedData(ev, key, value);
        }

        private static void SetTypedData(LevelEvent ev, string key, object value)
        {
            object current = SafeGetData(ev, key);
            Type targetType = current != null ? current.GetType() : null;
            if (targetType == null && ev.info != null && ev.info.propertiesInfo != null)
            {
                ADOFAI.PropertyInfo propertyInfo;
                if (ev.info.propertiesInfo.TryGetValue(key, out propertyInfo)
                    && propertyInfo != null && propertyInfo.value_default != null)
                    targetType = propertyInfo.value_default.GetType();
            }
            ev[key] = targetType == null ? value : ConvertFor(value, targetType);
            if (ev.disabled != null && ev.disabled.ContainsKey(key)) ev.disabled[key] = false;
        }

        private static object ConvertFor(object value, Type targetType)
        {
            if (value == null || targetType == null) return value;
            Type actual = Nullable.GetUnderlyingType(targetType) ?? targetType;
            if (actual.IsInstanceOfType(value)) return value;
            if (actual.IsEnum)
                return Enum.Parse(actual, Convert.ToString(value, CultureInfo.InvariantCulture), true);
            if (actual == typeof(string)) return Convert.ToString(value, CultureInfo.InvariantCulture);
            if (actual == typeof(Vector2) && value is Vector2) return value;
            return Convert.ChangeType(value, actual, CultureInfo.InvariantCulture);
        }

        private static object SafeGetData(LevelEvent ev, string key)
        {
            if (ev == null) return null;
            try { return ev[key]; } catch { return null; }
        }

        private static string EventDisplayName(LevelEvent ev)
        {
            if (ev == null) return "<null>";
            if (ev.info != null && !string.IsNullOrEmpty(ev.info.name)) return ev.info.name;
            return ev.eventType.ToString();
        }

        private static float ResolveTileSize()
        {
            float tileSize = ADOBase.controller == null ? 1f : ADOBase.controller.tileSize;
            return Mathf.Abs(tileSize) < 0.000001f ? 1f : tileSize;
        }

        private static Vector2 ToLevelPosition(scrFloor floor, float tileSize)
        {
            Vector3 p = floor.transform.position;
            return new Vector2(p.x / tileSize, p.y / tileSize);
        }
    }
}
