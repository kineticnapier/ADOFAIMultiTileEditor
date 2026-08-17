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
            IList decorations = candidate.decorations as IList;
            if (decorations == null)
                throw new InvalidOperationException("LevelData.decorations is not list-compatible in this game build.");

            for (int t = 0; t < sourceTracks.Count; t++)
            {
                SourcePreviewTrack source = sourceTracks[t];
                for (int i = 0; i < source.Tiles.Count; i++)
                {
                    LevelEvent preview = FindOwnedPreview(decorations, source.TrackIndex, i);
                    if (preview == null) continue; // manual/EQOL preview was intentionally preserved

                    SourcePreviewTile tile = source.Tiles[i];
                    if (tile.ForceStarter180)
                    {
                        SetTypedData(preview, "trackAngle", 180f);
                        SetTypedData(preview, "rotation", tile.StarterRotationDegrees);
                        starterTiles++;
                    }

                    if (TrySetTypedData(preview, "trackIcon", tile.TrackIcon))
                    {
                        SetOptionalTypedData(preview, "trackIconAngle", tile.TrackIconAngle);
                        SetOptionalTypedData(preview, "trackIconFlipped", tile.TrackIconFlipped);
                        SetOptionalTypedData(preview, "trackIconOutlines", tile.TrackIconOutlines);
                        SetOptionalTypedData(preview, "trackGraySetSpeedIcon", tile.TrackGraySetSpeedIcon);
                        SetOptionalTypedData(preview, "trackSetSpeedIconBpm", tile.TrackSetSpeedIconBpm);
                        if (!string.Equals(tile.TrackIcon, "None", StringComparison.OrdinalIgnoreCase)) iconTiles++;
                    }
                }
            }

            bool committed = false;
            try
            {
                TrackStore.RestoreSnapshot(editor, candidate, true);
                editor.UpdateDecorationObjects();
                committed = true;
                return "Preview finish: forced " + starterTiles + " mid-region starter tile(s) to 180° and reflected "
                    + iconTiles + " source tile icon(s).";
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
            int previewStart = regionIndex > 0 ? regionIndex : regionIndex + 1;
            for (int i = previewStart; i + 1 < floors.Count; i++)
            {
                scrFloor floor = floors[i];
                scrFloor previous = i > 0 ? floors[i - 1] : floor;
                double speed = ReadDouble(floor, "speed", 1.0);
                double previousSpeed = ReadDouble(previous, "speed", 1.0);

                string runtimeIcon = ReadIconName(floor);
                string icon = !string.IsNullOrEmpty(runtimeIcon)
                    ? runtimeIcon
                    : SpeedIcon(previousSpeed, speed);

                bool speedIcon = IsSpeedIcon(icon);
                var tile = new SourcePreviewTile
                {
                    TrackIcon = icon,
                    TrackIconAngle = (float)ReadDouble(floor, "trackIconAngle", 0.0),
                    TrackIconFlipped = ReadBool(floor, "trackIconFlipped", false),
                    TrackIconOutlines = ReadBool(floor, "trackIconOutlines", false),
                    TrackGraySetSpeedIcon = ReadBool(floor, "trackGraySetSpeedIcon", speedIcon),
                    TrackSetSpeedIconBpm = ReadDouble(floor, "trackSetSpeedIconBpm", editor.levelData.bpm * speed),
                    ForceStarter180 = regionIndex > 0 && i == regionIndex,
                    StarterRotationDegrees = 0f
                };

                if (tile.ForceStarter180)
                {
                    Vector2 previousPosition = ToLevelPosition(previous, tileSize);
                    Vector2 startPosition = ToLevelPosition(floor, tileSize);
                    Vector2 incoming = startPosition - previousPosition;
                    if (incoming.sqrMagnitude > 1.0e-8f)
                        tile.StarterRotationDegrees = Mathf.Atan2(incoming.y, incoming.x) * Mathf.Rad2Deg;
                }

                result.Tiles.Add(tile);
            }

            return result;
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

        private static string ReadIconName(object floor)
        {
            string[] names = { "trackIcon", "floorIcon", "speedIcon" };
            for (int i = 0; i < names.Length; i++)
            {
                object value = ReadMember(floor, names[i]);
                if (value == null) continue;
                Type type = value.GetType();
                if (value is string || type.IsEnum)
                {
                    string text = Convert.ToString(value, CultureInfo.InvariantCulture);
                    if (!string.IsNullOrWhiteSpace(text)) return text.Trim();
                }
            }
            return null;
        }

        private static string SpeedIcon(double previousSpeed, double speed)
        {
            if (previousSpeed <= SpeedEpsilon || speed <= SpeedEpsilon) return "None";
            double ratio = speed / previousSpeed;
            if (Math.Abs(ratio - 2.0) <= SpeedEpsilon) return "DoubleRabbit";
            if (Math.Abs(ratio - 0.5) <= SpeedEpsilon) return "DoubleSnail";
            if (ratio > 1.0 + SpeedEpsilon) return "Rabbit";
            if (ratio < 1.0 - SpeedEpsilon) return "Snail";
            return "None";
        }

        private static bool IsSpeedIcon(string icon)
        {
            return string.Equals(icon, "Rabbit", StringComparison.OrdinalIgnoreCase)
                || string.Equals(icon, "Snail", StringComparison.OrdinalIgnoreCase)
                || string.Equals(icon, "DoubleRabbit", StringComparison.OrdinalIgnoreCase)
                || string.Equals(icon, "DoubleSnail", StringComparison.OrdinalIgnoreCase);
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

        private static bool ReadBool(object target, string name, bool fallback)
        {
            object value = ReadMember(target, name);
            if (value == null) return fallback;
            if (value is bool) return (bool)value;
            bool parsed;
            return bool.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out parsed) ? parsed : fallback;
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
            internal readonly List<SourcePreviewTile> Tiles = new List<SourcePreviewTile>();
        }

        private sealed class SourcePreviewTile
        {
            internal string TrackIcon;
            internal float TrackIconAngle;
            internal bool TrackIconFlipped;
            internal bool TrackIconOutlines;
            internal bool TrackGraySetSpeedIcon;
            internal double TrackSetSpeedIconBpm;
            internal bool ForceStarter180;
            internal float StarterRotationDegrees;
        }
    }
}
