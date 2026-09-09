using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using ADOFAI;
using UnityEngine;

namespace KineticNapier.ADOFAIMultiTileEditor
{
    internal static class DynamicPagedLayoutPostProcessor
    {
        private const string OwnerTag = "adofaiMTEGenerated";
        private const string StarterTag = "adofaiMTEStarter";
        private const string EndTag = "adofaiMTEEnd";
        private const string PageAnchorTag = "adofaiMTEPageAnchor";
        private const string PageMoveEventTag = "adofaiMTEPageMove";
        private const string PagePlanetEventTag = "adofaiMTEPagePlanet";
        private const float PositionEpsilon = 0.0005f;
        private static readonly Vector2 HiddenOffset = new Vector2(0f, -10000f);

        private sealed class PagedTrackLayout
        {
            internal int TrackIndex;
            internal int PreviewSourceOffset;
            internal int SourceCycleSegments;
            internal int EffectiveRepeatCount;
            internal int PageLength;
            internal int PageCount;
            internal string PlanetATag;
            internal string PlanetBTag;
            internal readonly List<Vector2> ActualLocal = new List<Vector2>();
            internal readonly List<Vector2> PageLocal = new List<Vector2>();
            internal readonly List<int> PageByFloor = new List<int>();
            internal readonly List<Vector2> PageOffset = new List<Vector2>();
            internal readonly List<PageMove> Moves = new List<PageMove>();
        }

        private sealed class PageMove
        {
            internal double Beat;
            internal string Tag;
            internal Vector2 Delta;
            internal string EventTag;
        }

        internal static bool AnyEnabled(IList<TrackSlot> tracks)
        {
            if (tracks == null) return false;
            for (int i = 0; i < tracks.Count; i++)
                if (tracks[i] != null && tracks[i].DynamicPagingEnabled) return true;
            return false;
        }

        internal static string ApplyAndCommit(scnEditor editor, IList<TrackSlot> tracks, GenerationPlan plan)
        {
            if (editor == null || editor.levelData == null)
                throw new InvalidOperationException("Editor is not ready.");
            if (tracks == null || plan == null || tracks.Count != plan.Tracks.Count)
                throw new InvalidOperationException("Dynamic paging inputs no longer match the analyzed plan.");
            if (!AnyEnabled(tracks)) return "Dynamic paging: off.";

            LevelData output = editor.levelData.Copy();
            int selectedFloor = GameAngleProbe.TryGetSelectedFloorIndex(editor);
            List<PagedTrackLayout> layouts = CaptureLayouts(editor, tracks, plan, output, selectedFloor);
            LevelData candidate = output.Copy();

            IList decorations = candidate.decorations as IList;
            IList levelEvents = candidate.levelEvents as IList;
            if (decorations == null || levelEvents == null)
                throw new InvalidOperationException("LevelData lists are not compatible with dynamic paging.");

            int removedEvents = RemoveOwnedPageEvents(levelEvents);
            int removedAnchors = RemoveOwnedPageAnchors(decorations);
            int movedTiles = 0;
            int duplicatedAnchors = 0;
            int emittedMoves = 0;
            int totalPages = 0;

            for (int i = 0; i < layouts.Count; i++)
            {
                PagedTrackLayout layout = layouts[i];
                totalPages += layout.PageCount;
                ApplyFloorPages(decorations, layout, out int moved, out int anchors);
                movedTiles += moved;
                duplicatedAnchors += anchors;
                BuildMovePlan(layout, plan.Tracks[layout.TrackIndex]);
                emittedMoves += EmitMoves(levelEvents, plan, layout);
            }

            bool committed = false;
            try
            {
                try { editor.SaveState(true, true); } catch { }
                TrackStore.RestoreSnapshot(editor, candidate, true);
                editor.ApplyEventsToFloors();
                editor.UpdateDecorationObjects();
                committed = true;
                return "Dynamic paging: " + layouts.Count + " track(s), " + totalPages + " page(s), "
                    + movedTiles + " Floor placement(s), " + duplicatedAnchors + " boundary copy/copies, "
                    + emittedMoves + " zero-duration MoveDecorations action(s)"
                    + (removedEvents > 0 || removedAnchors > 0
                        ? "; replaced " + removedEvents + " old page move(s) and " + removedAnchors + " old boundary copy/copies."
                        : ".");
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
        }

        private static List<PagedTrackLayout> CaptureLayouts(
            scnEditor editor,
            IList<TrackSlot> tracks,
            GenerationPlan plan,
            LevelData output,
            int selectedFloor)
        {
            var result = new List<PagedTrackLayout>();
            try
            {
                for (int t = 0; t < tracks.Count; t++)
                {
                    TrackSlot slot = tracks[t];
                    if (slot == null || !slot.DynamicPagingEnabled) continue;
                    if (slot.Data == null)
                        throw new InvalidOperationException("Track #" + (t + 1) + " has no source snapshot.");
                    if (slot.PageTiles < 2)
                        throw new InvalidOperationException("Track '" + slot.Name + "' page size must be at least 2 tiles.");

                    TrackStore.RestoreSnapshot(editor, slot.Data, false);
                    result.Add(CaptureCurrentTrack(editor, slot, plan.Tracks[t], t));
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

        private static PagedTrackLayout CaptureCurrentTrack(
            scnEditor editor,
            TrackSlot slot,
            AnalyzedTrack analyzed,
            int trackIndex)
        {
            if (slot.RegionStartFloor < 0 || slot.RegionStartFloor >= editor.floors.Count)
                throw new InvalidOperationException("Track '" + slot.Name + "' paging start is outside its source path.");

            scrFloor requestedStart = editor.floors[slot.RegionStartFloor];
            if (requestedStart == null || requestedStart.midSpin)
                throw new InvalidOperationException("Track '" + slot.Name + "' paging start must be landable.");

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
                throw new InvalidOperationException("Track '" + slot.Name + "' paging region could not be reconstructed.");

            int segmentCount = floors.Count - regionIndex - 1;
            int repeats = slot.EffectiveRepeatCount;
            if (analyzed.Segments.Count != segmentCount * repeats)
            {
                throw new InvalidOperationException(
                    "Track '" + slot.Name + "' paging plan is stale: expected " + (segmentCount * repeats)
                    + " segment(s), got " + analyzed.Segments.Count + ". Re-analyze before generating.");
            }

            int pageLength = Math.Max(2, slot.PageTiles);
            int pageCount = Math.Max(1, (segmentCount + pageLength - 1) / pageLength);
            float tileSize = ResolveTileSize();
            Vector2 start = ToLevelPosition(floors[regionIndex], tileSize);

            var layout = new PagedTrackLayout
            {
                TrackIndex = trackIndex,
                PreviewSourceOffset = regionIndex > 0 ? 0 : 1,
                SourceCycleSegments = segmentCount,
                EffectiveRepeatCount = repeats,
                PageLength = pageLength,
                PageCount = pageCount,
                PlanetATag = analyzed.PlanetATag,
                PlanetBTag = analyzed.PlanetBTag
            };

            for (int i = regionIndex; i < floors.Count; i++)
                layout.ActualLocal.Add(ToLevelPosition(floors[i], tileSize) - start);

            for (int page = 0; page < pageCount; page++)
            {
                int sourceStart = page * pageLength;
                Vector2 offset = page == 0
                    ? Vector2.zero
                    : layout.ActualLocal[0] - layout.ActualLocal[Math.Min(sourceStart, layout.ActualLocal.Count - 1)];
                layout.PageOffset.Add(offset);
            }

            for (int floor = 0; floor < layout.ActualLocal.Count; floor++)
            {
                int page = FloorPage(floor, pageLength, pageCount);
                int pageStart = page * pageLength;
                Vector2 local = page == 0
                    ? layout.ActualLocal[floor]
                    : layout.ActualLocal[floor] - layout.ActualLocal[pageStart] + layout.ActualLocal[0];
                layout.PageByFloor.Add(page);
                layout.PageLocal.Add(local);
            }

            return layout;
        }

        private static int FloorPage(int floorIndex, int pageLength, int pageCount)
        {
            if (floorIndex <= 0 || pageCount <= 1) return 0;
            int page = (floorIndex - 1) / Math.Max(1, pageLength);
            return Math.Max(0, Math.Min(page, pageCount - 1));
        }

        private static void ApplyFloorPages(IList decorations, PagedTrackLayout layout, out int moved, out int anchors)
        {
            moved = 0;
            anchors = 0;
            LevelEvent first = FindOwnedPreview(decorations, layout.TrackIndex, 0);
            if (first == null || layout.ActualLocal.Count == 0) return;

            int firstSource = layout.PreviewSourceOffset;
            if (firstSource < 0 || firstSource >= layout.ActualLocal.Count) return;

            Vector2 originalFirst = ReadVector2Data(first, "position", Vector2.zero);
            Vector2 origin = originalFirst - layout.ActualLocal[firstSource];

            for (int i = 0; ; i++)
            {
                LevelEvent preview = FindOwnedPreview(decorations, layout.TrackIndex, i);
                if (preview == null) break;
                int sourceIndex = layout.PreviewSourceOffset + i;
                if (sourceIndex < 0 || sourceIndex >= layout.PageLocal.Count) break;

                int page = layout.PageByFloor[sourceIndex];
                Vector2 target = origin + layout.PageLocal[sourceIndex] + (page == 0 ? Vector2.zero : HiddenOffset);
                Vector2 current = ReadVector2Data(preview, "position", target);
                if ((current - target).sqrMagnitude > PositionEpsilon * PositionEpsilon)
                {
                    SetTypedData(preview, "position", target);
                    moved++;
                }
                SetPageTag(preview, layout.TrackIndex, page);
            }

            LevelEvent starter = FindOwnedSpecialTile(decorations, layout.TrackIndex, StarterTag);
            if (starter != null) SetPageTag(starter, layout.TrackIndex, 0);

            LevelEvent end = FindOwnedSpecialTile(decorations, layout.TrackIndex, EndTag);
            if (end != null)
            {
                int last = layout.PageLocal.Count - 1;
                int page = layout.PageByFloor[last];
                Vector2 target = origin + layout.PageLocal[last] + (page == 0 ? Vector2.zero : HiddenOffset);
                SetTypedData(end, "position", target);
                SetPageTag(end, layout.TrackIndex, page);
                moved++;
            }

            for (int page = 1; page < layout.PageCount; page++)
            {
                int boundarySource = page * layout.PageLength;
                int tileIndex = boundarySource - layout.PreviewSourceOffset;
                if (tileIndex < 0) continue;
                LevelEvent template = FindOwnedPreview(decorations, layout.TrackIndex, tileIndex);
                if (template == null) continue;

                LevelEvent copy = template.Copy();
                if (copy == null) continue;
                string tag = Convert.ToString(SafeGetData(copy, "tag"), CultureInfo.InvariantCulture) ?? string.Empty;
                tag = StripPageTags(tag, layout.TrackIndex);
                tag = AddTag(tag, PageTag(layout.TrackIndex, page));
                tag = AddTag(tag, PageAnchorTag);
                tag = AddTag(tag, "T" + layout.TrackIndex + "_page" + page + "_anchor");
                SetTypedData(copy, "tag", tag);
                SetTypedData(copy, "position", origin + layout.ActualLocal[0] + HiddenOffset);
                decorations.Add(copy);
                anchors++;
            }
        }

        private static void BuildMovePlan(PagedTrackLayout layout, AnalyzedTrack analyzed)
        {
            layout.Moves.Clear();
            if (layout.PageCount <= 1) return;

            int n = layout.SourceCycleSegments;
            for (int cycle = 0; cycle < layout.EffectiveRepeatCount; cycle++)
            {
                int cycleStart = cycle * n;
                for (int page = 1; page < layout.PageCount; page++)
                {
                    int boundaryFloor = page * layout.PageLength;
                    int segmentIndex = boundaryFloor - 1;
                    if (segmentIndex < 0 || segmentIndex >= n) continue;
                    TrackSegment segment = analyzed.Segments[cycleStart + segmentIndex];

                    AddMove(layout, segment.EndBeat, PageTag(layout.TrackIndex, page - 1), HiddenOffset, PageMoveEventTag);
                    AddMove(layout, segment.EndBeat, PageTag(layout.TrackIndex, page), -HiddenOffset, PageMoveEventTag);
                    Vector2 planetDelta = layout.PageOffset[page] - layout.PageOffset[page - 1];
                    AddMove(layout, segment.EndBeat, layout.PlanetATag + " " + layout.PlanetBTag, planetDelta, PagePlanetEventTag);
                }

                if (cycle + 1 >= layout.EffectiveRepeatCount) continue;
                TrackSegment boundary = analyzed.Segments[cycleStart + n - 1];
                int lastPage = layout.PageCount - 1;
                AddMove(layout, boundary.EndBeat, PageTag(layout.TrackIndex, lastPage), HiddenOffset, PageMoveEventTag);
                AddMove(layout, boundary.EndBeat, PageTag(layout.TrackIndex, 0), -HiddenOffset, PageMoveEventTag);
                Vector2 returnDelta = layout.PageOffset[0] - layout.PageOffset[lastPage];
                AddMove(layout, boundary.EndBeat, layout.PlanetATag + " " + layout.PlanetBTag, returnDelta, PagePlanetEventTag);
            }
        }

        private static void AddMove(PagedTrackLayout layout, double beat, string tag, Vector2 delta, string eventTag)
        {
            if (delta.sqrMagnitude <= PositionEpsilon * PositionEpsilon) return;
            layout.Moves.Add(new PageMove { Beat = beat, Tag = tag, Delta = delta, EventTag = eventTag });
        }

        private static int EmitMoves(IList levelEvents, GenerationPlan plan, PagedTrackLayout layout)
        {
            int emitted = 0;
            for (int i = 0; i < layout.Moves.Count; i++)
            {
                PageMove point = layout.Moves[i];
                int anchor = TimelineMerger.FindAnchorIndex(plan.Anchors, point.Beat);
                if (anchor < 0)
                    throw new InvalidOperationException("Dynamic page transition could not be mapped to the master timeline for track #" + (layout.TrackIndex + 1) + ".");

                int outputFloor = plan.RegionStartFloor + anchor;
                LevelEvent move = CreateCustomEvent("MoveDecorations", outputFloor);
                SetRequiredData(move, "duration", 0f);
                SetRequiredData(move, "tag", point.Tag);
                SetRequiredData(move, "relativeTo", "LastPosition");
                SetRequiredData(move, "positionOffset", point.Delta);
                SetOptionalData(move, "angleOffset", 0f);
                SetOptionalData(move, "ease", "Linear");
                SetOptionalData(move, "eventTag", point.EventTag);
                SetOptionalData(move, "active", true);
                InsertBeforeOrbitAtFloor(levelEvents, move, outputFloor);
                emitted++;
            }
            return emitted;
        }

        private static int RemoveOwnedPageEvents(IList levelEvents)
        {
            int removed = 0;
            for (int i = levelEvents.Count - 1; i >= 0; i--)
            {
                LevelEvent ev = levelEvents[i] as LevelEvent;
                if (ev == null) continue;
                string eventTag = Convert.ToString(SafeGetData(ev, "eventTag"), CultureInfo.InvariantCulture) ?? string.Empty;
                if (!ContainsTagToken(eventTag, PageMoveEventTag) && !ContainsTagToken(eventTag, PagePlanetEventTag)) continue;
                levelEvents.RemoveAt(i);
                removed++;
            }
            return removed;
        }

        private static int RemoveOwnedPageAnchors(IList decorations)
        {
            int removed = 0;
            for (int i = decorations.Count - 1; i >= 0; i--)
            {
                LevelEvent ev = decorations[i] as LevelEvent;
                if (ev == null || !IsEventNamed(ev, "AddObject")) continue;
                string tag = Convert.ToString(SafeGetData(ev, "tag"), CultureInfo.InvariantCulture) ?? string.Empty;
                if (!ContainsTagToken(tag, OwnerTag) || !ContainsTagToken(tag, PageAnchorTag)) continue;
                decorations.RemoveAt(i);
                removed++;
            }
            return removed;
        }

        private static void SetPageTag(LevelEvent ev, int trackIndex, int page)
        {
            string tag = Convert.ToString(SafeGetData(ev, "tag"), CultureInfo.InvariantCulture) ?? string.Empty;
            tag = StripPageTags(tag, trackIndex);
            tag = AddTag(tag, PageTag(trackIndex, page));
            SetTypedData(ev, "tag", tag);
        }

        private static string PageTag(int trackIndex, int page)
        {
            return "adofaiMTEPage_T" + trackIndex + "_" + page;
        }

        private static string StripPageTags(string tags, int trackIndex)
        {
            if (string.IsNullOrWhiteSpace(tags)) return string.Empty;
            string prefix = "adofaiMTEPage_T" + trackIndex + "_";
            string[] split = tags.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var kept = new List<string>();
            for (int i = 0; i < split.Length; i++)
                if (!split[i].StartsWith(prefix, StringComparison.Ordinal)) kept.Add(split[i]);
            return string.Join(" ", kept.ToArray());
        }

        private static string AddTag(string tags, string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return tags ?? string.Empty;
            if (ContainsTagToken(tags, token)) return tags ?? string.Empty;
            return string.IsNullOrWhiteSpace(tags) ? token : tags.Trim() + " " + token;
        }

        private static bool ContainsTagToken(string tags, string token)
        {
            if (string.IsNullOrWhiteSpace(tags) || string.IsNullOrWhiteSpace(token)) return false;
            string[] split = tags.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < split.Length; i++)
                if (string.Equals(split[i], token, StringComparison.Ordinal)) return true;
            return false;
        }

        private static void InsertBeforeOrbitAtFloor(IList events, LevelEvent move, int floor)
        {
            int insert = events.Count;
            for (int i = 0; i < events.Count; i++)
            {
                LevelEvent ev = events[i] as LevelEvent;
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
            events.Insert(insert, move);
        }

        private static LevelEvent FindOwnedPreview(IList decorations, int trackIndex, int tileIndex)
        {
            string token = "T" + trackIndex + "_" + tileIndex;
            for (int i = 0; i < decorations.Count; i++)
            {
                LevelEvent ev = decorations[i] as LevelEvent;
                if (ev == null || !IsEventNamed(ev, "AddObject")) continue;
                string objectType = Convert.ToString(SafeGetData(ev, "objectType"), CultureInfo.InvariantCulture) ?? string.Empty;
                if (!string.Equals(objectType, "Floor", StringComparison.OrdinalIgnoreCase)) continue;
                string tag = Convert.ToString(SafeGetData(ev, "tag"), CultureInfo.InvariantCulture) ?? string.Empty;
                if (ContainsTagToken(tag, OwnerTag) && ContainsTagToken(tag, token) && !ContainsTagToken(tag, PageAnchorTag)) return ev;
            }
            return null;
        }

        private static LevelEvent FindOwnedSpecialTile(IList decorations, int trackIndex, string specialTag)
        {
            string trackToken = "T" + trackIndex;
            for (int i = 0; i < decorations.Count; i++)
            {
                LevelEvent ev = decorations[i] as LevelEvent;
                if (ev == null || !IsEventNamed(ev, "AddObject")) continue;
                string tag = Convert.ToString(SafeGetData(ev, "tag"), CultureInfo.InvariantCulture) ?? string.Empty;
                if (ContainsTagToken(tag, OwnerTag) && ContainsTagToken(tag, trackToken) && ContainsTagToken(tag, specialTag)) return ev;
            }
            return null;
        }

        private static LevelEvent CreateCustomEvent(string requestedName, int floor)
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
            LevelEventInfo suffixMatch = null;
            foreach (KeyValuePair<string, LevelEventInfo> pair in GCS.levelEventsInfo)
            {
                LevelEventInfo info = pair.Value;
                if (info == null) continue;
                string keyNormalized = NormalizeEventName(pair.Key);
                string infoNormalized = NormalizeEventName(info.name);
                if (keyNormalized == target || infoNormalized == target) return info;
                if (keyNormalized.EndsWith(target, StringComparison.Ordinal)
                    || infoNormalized.EndsWith(target, StringComparison.Ordinal)) suffixMatch = info;
            }
            if (suffixMatch != null) return suffixMatch;
            throw new InvalidOperationException("Event metadata '" + requestedName + "' is unavailable.");
        }

        private static string NormalizeEventName(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
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
            string infoName = ev.info != null ? NormalizeEventName(ev.info.name) : string.Empty;
            if (infoName == target || infoName.EndsWith(target, StringComparison.Ordinal)) return true;
            return NormalizeEventName(ev.eventType.ToString()) == target;
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
            {
                string text = Convert.ToString(value, CultureInfo.InvariantCulture);
                return Enum.Parse(actual, text, true);
            }
            if (actual == typeof(string)) return Convert.ToString(value, CultureInfo.InvariantCulture);
            if (actual == typeof(Vector2) && value is Vector2) return value;
            return Convert.ChangeType(value, actual, CultureInfo.InvariantCulture);
        }

        private static object SafeGetData(LevelEvent ev, string key)
        {
            if (ev == null) return null;
            try { return ev[key]; } catch { return null; }
        }

        private static Vector2 ReadVector2Data(LevelEvent ev, string key, Vector2 fallback)
        {
            object value = SafeGetData(ev, key);
            if (value is Vector2) return (Vector2)value;
            if (value is Vector3)
            {
                Vector3 v = (Vector3)value;
                return new Vector2(v.x, v.y);
            }
            return fallback;
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
