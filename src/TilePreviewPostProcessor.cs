using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using ADOFAI;
using UnityEngine;
using R = System.Reflection;

namespace KineticNapier.ADOFAIMultiTileEditor
{
    internal static class TilePreviewPostProcessor
    {
        private const string OwnerTag = "adofaiMTEGenerated";
        private const string StarterTag = "adofaiMTEStarter";
        private const double SpeedEpsilon = 1.0e-5;

        internal static string ApplyAndCommit(scnEditor editor, IList<TrackSlot> tracks, GenerationPlan plan)
        {
            if (editor == null || editor.levelData == null)
                throw new InvalidOperationException("Editor is not ready.");
            if (tracks == null || tracks.Count == 0 || plan == null)
                throw new InvalidOperationException("Tile preview post-processing inputs are incomplete.");

            LevelData output = editor.levelData.Copy();
            int selectedFloor = GameAngleProbe.TryGetSelectedFloorIndex(editor);
            List<SourcePreviewTrack> sourceTracks = CaptureSourceTracks(editor, tracks, output, selectedFloor);
            LevelData candidate = output.Copy();

            int starterTiles = 0;
            int iconTiles = 0;
            int redTwirlIcons = 0;
            int blueTwirlIcons = 0;
            IList decorations = candidate.decorations as IList;
            if (decorations == null)
                throw new InvalidOperationException("LevelData.decorations is not list-compatible in this game build.");

            RemoveOwnedStarterTiles(decorations);

            for (int t = 0; t < sourceTracks.Count; t++)
            {
                SourcePreviewTrack source = sourceTracks[t];
                LevelEvent firstOwnedPreview = FindOwnedPreview(decorations, source.TrackIndex, 0);

                // A mid-chart Multi Tile start needs one extra straight tile on the
                // incoming side. Do not mutate the real start tile: it keeps its source
                // corner/straight shape and source icon.
                if (source.HasStarterTile && firstOwnedPreview != null)
                {
                    LevelEvent starter = CreateStarterTile(firstOwnedPreview, source);
                    decorations.Add(starter);
                    starterTiles++;
                }

                for (int i = 0; i < source.Tiles.Count; i++)
                {
                    LevelEvent preview = FindOwnedPreview(decorations, source.TrackIndex, i);
                    if (preview == null) continue; // manual/EQOL preview was intentionally preserved

                    SourcePreviewTile tile = source.Tiles[i];
                    if (!TrySetTypedData(preview, "trackIcon", tile.TrackIcon))
                        continue;

                    float iconAngle = tile.UsePreviewRotationForIcon
                        ? ReadFloatData(preview, "rotation", 0f)
                        : tile.TrackIconAngle;

                    SetOptionalTypedData(preview, "trackIconAngle", iconAngle);
                    SetOptionalTypedData(preview, "trackIconFlipped", tile.TrackIconFlipped);
                    SetOptionalTypedData(preview, "trackIconOutlines", tile.TrackIconOutlines);
                    SetOptionalTypedData(preview, "trackRedSwirl", tile.TrackRedSwirl);
                    SetOptionalTypedData(preview, "trackGraySetSpeedIcon", tile.TrackGraySetSpeedIcon);
                    SetOptionalTypedData(preview, "trackSetSpeedIconBpm", tile.TrackSetSpeedIconBpm);

                    if (!string.Equals(tile.TrackIcon, "None", StringComparison.OrdinalIgnoreCase))
                        iconTiles++;
                    if (string.Equals(tile.TrackIcon, "Swirl", StringComparison.OrdinalIgnoreCase))
                    {
                        if (tile.TrackRedSwirl) redTwirlIcons++;
                        else blueTwirlIcons++;
                    }
                }
            }

            bool committed = false;
            try
            {
                TrackStore.RestoreSnapshot(editor, candidate, true);
                editor.UpdateDecorationObjects();
                committed = true;
                return "Preview finish: added " + starterTiles + " separate 180° starter tile(s), reflected "
                    + iconTiles + " source tile icon(s), Twirl colors red/blue="
                    + redTwirlIcons + "/" + blueTwirlIcons + ".";
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

        private static List<SourcePreviewTrack> CaptureSourceTracks(
            scnEditor editor,
            IList<TrackSlot> tracks,
            LevelData output,
            int selectedFloor)
        {
            var result = new List<SourcePreviewTrack>();
            try
            {
                for (int t = 0; t < tracks.Count; t++)
                {
                    TrackSlot slot = tracks[t];
                    if (slot == null || slot.Data == null)
                        throw new InvalidOperationException("Track #" + (t + 1) + " has no source snapshot.");

                    TrackStore.RestoreSnapshot(editor, slot.Data, false);
                    result.Add(CaptureCurrentTrack(editor, slot, t));
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

        private static SourcePreviewTrack CaptureCurrentTrack(scnEditor editor, TrackSlot slot, int trackIndex)
        {
            if (slot.RegionStartFloor < 0 || slot.RegionStartFloor >= editor.floors.Count)
                throw new InvalidOperationException("Track '" + slot.Name + "' preview start is outside its source path.");

            scrFloor requestedStart = editor.floors[slot.RegionStartFloor];
            if (requestedStart == null || requestedStart.midSpin)
                throw new InvalidOperationException("Track '" + slot.Name + "' preview start must be a landable floor.");

            var floors = new List<scrFloor>();
            int regionIndex = -1;
            for (int i = 0; i < editor.floors.Count; i++)
            {
                scrFloor floor = editor.floors[i];
                if (floor == null || floor.midSpin) continue;
                if (ReferenceEquals(floor, requestedStart)) regionIndex = floors.Count;
                floors.Add(floor);
            }

            var result = new SourcePreviewTrack { TrackIndex = trackIndex };
            if (regionIndex < 0 || regionIndex + 1 >= floors.Count) return result;

            float tileSize = ResolveTileSize();
            Vector2 startPosition = ToLevelPosition(floors[regionIndex], tileSize);

            if (regionIndex > 0)
            {
                Vector2 previousPosition = ToLevelPosition(floors[regionIndex - 1], tileSize);
                Vector2 incomingOffset = previousPosition - startPosition;
                Vector2 incomingDirection = startPosition - previousPosition;

                result.HasStarterTile = incomingOffset.sqrMagnitude > 1.0e-8f;
                result.StarterOffset = incomingOffset;
                result.StarterRotationDegrees = incomingDirection.sqrMagnitude <= 1.0e-8f
                    ? 0f
                    : Mathf.Atan2(incomingDirection.y, incomingDirection.x) * Mathf.Rad2Deg;
            }

            int previewStart = regionIndex > 0 ? regionIndex : regionIndex + 1;
            for (int i = previewStart; i + 1 < floors.Count; i++)
            {
                scrFloor floor = floors[i];
                scrFloor previous = i > 0 ? floors[i - 1] : floor;
                int floorNumber = FindRawFloorIndex(editor, floor);

                var tile = new SourcePreviewTile
                {
                    TrackIcon = "None",
                    TrackIconAngle = 0f,
                    UsePreviewRotationForIcon = true,
                    TrackIconFlipped = false,
                    TrackIconOutlines = false,
                    TrackRedSwirl = false,
                    TrackGraySetSpeedIcon = false,
                    TrackSetSpeedIconBpm = editor.levelData.bpm * ReadDouble(floor, "speed", 1.0)
                };

                ResolveSourceIcon(editor, floorNumber, floor, previous, tile);
                result.Tiles.Add(tile);
            }

            return result;
        }

        private static void ResolveSourceIcon(
            scnEditor editor,
            int floorNumber,
            scrFloor floor,
            scrFloor previous,
            SourcePreviewTile tile)
        {
            if (floorNumber < 0) return;

            LevelEvent speedEvent = FindLastEvent(editor, floorNumber, LevelEventType.SetSpeed);
            LevelEvent twirlEvent = FindLastEvent(editor, floorNumber, LevelEventType.Twirl);
            LevelEvent checkpointEvent = FindLastEvent(editor, floorNumber, LevelEventType.Checkpoint);
            LevelEvent multiPlanetEvent = FindLastEvent(editor, floorNumber, LevelEventType.MultiPlanet);

            double speed = ReadDouble(floor, "speed", 1.0);
            double previousSpeed = ReadDouble(previous, "speed", 1.0);
            double ratio = previousSpeed > SpeedEpsilon ? speed / previousSpeed : 1.0;

            if (speedEvent != null && ratio >= 1.9999)
            {
                tile.TrackIcon = "DoubleRabbit";
                ApplySpeedIconData(editor, floor, tile);
            }
            else if (speedEvent != null && ratio <= 0.2501)
            {
                tile.TrackIcon = "DoubleSnail";
                ApplySpeedIconData(editor, floor, tile);
            }
            else if (speedEvent != null && ratio >= 1.0499)
            {
                tile.TrackIcon = "Rabbit";
                ApplySpeedIconData(editor, floor, tile);
            }
            else if (speedEvent != null && ratio <= 0.9501)
            {
                tile.TrackIcon = "Snail";
                ApplySpeedIconData(editor, floor, tile);
            }
            else if (twirlEvent != null)
            {
                tile.TrackIcon = "Swirl";
                float localRelativeAngle = GetRelativeOrbitDegrees(floor);

                // ADOFAI's Twirl icon is red only when the relative angle after
                // applying Twirl is strictly below 180°. 180° and above is blue.
                tile.TrackRedSwirl = localRelativeAngle < 180f;
                tile.TrackIconFlipped = floor.isCCW;
                tile.TrackIconAngle = tile.TrackIconFlipped
                    ? 180f - (180f - localRelativeAngle) / 2f
                    : (180f - localRelativeAngle) / 2f;
                tile.UsePreviewRotationForIcon = false;
            }
            else if (checkpointEvent != null)
            {
                tile.TrackIcon = "Checkpoint";
            }
            else if (multiPlanetEvent != null)
            {
                string planets = Convert.ToString(SafeGetData(multiPlanetEvent, "planets"), CultureInfo.InvariantCulture) ?? "";
                tile.TrackIcon = string.Equals(planets, "TwoPlanets", StringComparison.OrdinalIgnoreCase)
                    ? "MultiPlanetTwo"
                    : "MultiPlanetThreeMore";
            }
        }

        private static void ApplySpeedIconData(scnEditor editor, scrFloor floor, SourcePreviewTile tile)
        {
            tile.TrackGraySetSpeedIcon = false;
            tile.TrackSetSpeedIconBpm = editor.levelData.bpm * ReadDouble(floor, "speed", 1.0);
        }

        private static float GetRelativeOrbitDegrees(scrFloor floor)
        {
            double moved = scrMisc.GetAngleMoved(floor.entryangle, floor.exitangle, !floor.isCCW);
            float degrees = Mathf.Abs((float)moved * Mathf.Rad2Deg);
            if (degrees <= 0.000001f) return 360f;
            return degrees;
        }

        private static LevelEvent FindLastEvent(scnEditor editor, int floorNumber, LevelEventType type)
        {
            if (editor == null || editor.events == null) return null;
            for (int i = editor.events.Count - 1; i >= 0; i--)
            {
                LevelEvent ev = editor.events[i];
                if (ev != null && ev.floor == floorNumber && ev.eventType == type)
                    return ev;
            }
            return null;
        }

        private static int FindRawFloorIndex(scnEditor editor, scrFloor target)
        {
            if (editor == null || editor.floors == null || target == null) return -1;
            for (int i = 0; i < editor.floors.Count; i++)
                if (ReferenceEquals(editor.floors[i], target)) return i;
            return -1;
        }

        private static LevelEvent CreateStarterTile(LevelEvent firstPreview, SourcePreviewTrack source)
        {
            LevelEvent starter = firstPreview.Copy();
            if (starter == null)
                throw new InvalidOperationException("Could not clone the generated first Floor preview for the 180° starter tile.");

            string baseTag = "T" + source.TrackIndex;
            SetTypedData(starter, "tag",
                baseTag + " " + baseTag + "_starter qolMultiTile_" + baseTag + " "
                + OwnerTag + " " + StarterTag);

            Vector2 firstPosition = ReadVector2Data(firstPreview, "position", Vector2.zero);
            SetTypedData(starter, "position", firstPosition + source.StarterOffset);
            SetTypedData(starter, "trackAngle", 180f);
            SetTypedData(starter, "rotation", source.StarterRotationDegrees);

            SetOptionalTypedData(starter, "trackIcon", "None");
            SetOptionalTypedData(starter, "trackIconAngle", 0f);
            SetOptionalTypedData(starter, "trackIconFlipped", false);
            SetOptionalTypedData(starter, "trackIconOutlines", false);
            SetOptionalTypedData(starter, "trackRedSwirl", false);
            SetOptionalTypedData(starter, "trackGraySetSpeedIcon", false);
            return starter;
        }

        private static void RemoveOwnedStarterTiles(IList decorations)
        {
            if (decorations == null) return;
            for (int i = decorations.Count - 1; i >= 0; i--)
            {
                LevelEvent ev = decorations[i] as LevelEvent;
                if (ev == null || !IsEventNamed(ev, "AddObject")) continue;
                string tag = Convert.ToString(SafeGetData(ev, "tag"), CultureInfo.InvariantCulture) ?? "";
                if (ContainsTagToken(tag, OwnerTag) && ContainsTagToken(tag, StarterTag))
                    decorations.RemoveAt(i);
            }
        }

        private static LevelEvent FindOwnedPreview(IList decorations, int trackIndex, int tileIndex)
        {
            string exactTileTag = "T" + trackIndex + "_" + tileIndex;
            for (int i = 0; i < decorations.Count; i++)
            {
                LevelEvent ev = decorations[i] as LevelEvent;
                if (ev == null || !IsEventNamed(ev, "AddObject")) continue;

                string objectType = Convert.ToString(SafeGetData(ev, "objectType"), CultureInfo.InvariantCulture) ?? "";
                if (!string.Equals(objectType, "Floor", StringComparison.OrdinalIgnoreCase)) continue;

                string tag = Convert.ToString(SafeGetData(ev, "tag"), CultureInfo.InvariantCulture) ?? "";
                if (!ContainsTagToken(tag, OwnerTag) || !ContainsTagToken(tag, exactTileTag)) continue;
                return ev;
            }
            return null;
        }

        private static bool ContainsTagToken(string tags, string token)
        {
            if (string.IsNullOrEmpty(tags) || string.IsNullOrEmpty(token)) return false;
            string[] split = tags.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < split.Length; i++)
                if (string.Equals(split[i], token, StringComparison.Ordinal)) return true;
            return false;
        }

        private static bool IsEventNamed(LevelEvent ev, string requestedName)
        {
            if (ev == null) return false;
            string target = NormalizeEventName(requestedName);
            string infoName = ev.info != null ? NormalizeEventName(ev.info.name) : "";
            if (infoName == target || infoName.EndsWith(target, StringComparison.Ordinal)) return true;
            return NormalizeEventName(ev.eventType.ToString()) == target;
        }

        private static string NormalizeEventName(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            var chars = new List<char>(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (char.IsLetterOrDigit(c)) chars.Add(char.ToLowerInvariant(c));
            }
            return new string(chars.ToArray());
        }

        private static void SetTypedData(LevelEvent ev, string key, object value)
        {
            if (!TrySetTypedData(ev, key, value))
                throw new InvalidOperationException("Generated Floor AddObject cannot accept property '" + key + "' value '" + value + "'.");
        }

        private static bool TrySetTypedData(LevelEvent ev, string key, object value)
        {
            if (ev == null || ev.info == null || ev.info.propertiesInfo == null || !ev.info.propertiesInfo.ContainsKey(key))
                return false;

            object current = SafeGetData(ev, key);
            Type targetType = current != null ? current.GetType() : null;
            if (targetType == null)
            {
                ADOFAI.PropertyInfo propertyInfo;
                if (ev.info.propertiesInfo.TryGetValue(key, out propertyInfo)
                    && propertyInfo != null && propertyInfo.value_default != null)
                    targetType = propertyInfo.value_default.GetType();
            }

            try
            {
                ev[key] = targetType == null ? value : ConvertFor(value, targetType);
                if (ev.disabled != null && ev.disabled.ContainsKey(key)) ev.disabled[key] = false;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void SetOptionalTypedData(LevelEvent ev, string key, object value)
        {
            TrySetTypedData(ev, key, value);
        }

        private static object ConvertFor(object value, Type targetType)
        {
            if (value == null || targetType == null) return value;
            Type actual = Nullable.GetUnderlyingType(targetType) ?? targetType;
            if (actual.IsInstanceOfType(value)) return value;
            if (actual.IsEnum)
            {
                string text = Convert.ToString(value, CultureInfo.InvariantCulture);
                if (!Enum.IsDefined(actual, text))
                    throw new InvalidOperationException("Enum value '" + text + "' is unavailable for " + actual.Name + ".");
                return Enum.Parse(actual, text, true);
            }
            if (actual == typeof(string)) return Convert.ToString(value, CultureInfo.InvariantCulture);
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

        private static float ReadFloatData(LevelEvent ev, string key, float fallback)
        {
            object value = SafeGetData(ev, key);
            if (value == null) return fallback;
            try { return Convert.ToSingle(value, CultureInfo.InvariantCulture); }
            catch { return fallback; }
        }

        private static object ReadMember(object target, string name)
        {
            if (target == null) return null;
            Type type = target.GetType();
            const R.BindingFlags flags = R.BindingFlags.Instance | R.BindingFlags.Public | R.BindingFlags.NonPublic;
            try
            {
                R.PropertyInfo property = type.GetProperty(name, flags);
                if (property != null && property.GetIndexParameters().Length == 0) return property.GetValue(target, null);
            }
            catch { }
            try
            {
                R.FieldInfo field = type.GetField(name, flags);
                if (field != null) return field.GetValue(target);
            }
            catch { }
            return null;
        }

        private static double ReadDouble(object target, string name, double fallback)
        {
            object value = ReadMember(target, name);
            if (value == null) return fallback;
            try { return Convert.ToDouble(value, CultureInfo.InvariantCulture); }
            catch { return fallback; }
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

        private sealed class SourcePreviewTrack
        {
            internal int TrackIndex;
            internal bool HasStarterTile;
            internal Vector2 StarterOffset;
            internal float StarterRotationDegrees;
            internal readonly List<SourcePreviewTile> Tiles = new List<SourcePreviewTile>();
        }

        private sealed class SourcePreviewTile
        {
            internal string TrackIcon;
            internal float TrackIconAngle;
            internal bool UsePreviewRotationForIcon;
            internal bool TrackIconFlipped;
            internal bool TrackIconOutlines;
            internal bool TrackRedSwirl;
            internal bool TrackGraySetSpeedIcon;
            internal double TrackSetSpeedIconBpm;
        }
    }
}
