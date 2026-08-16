using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using ADOFAI;
using UnityEngine;
using R = System.Reflection;

namespace KineticNapier.ADOFAIMultiTileEditor
{
    internal static class FixedTileDecorationGenerator
    {
        private const string OwnerTag = "adofaiMTEGenerated";

        // Match PACL2AutoGenerator's automatic planet layout exactly so the source
        // preview starts on the same logical floor as the pivot planet.
        private const int LayoutColumns = 4;
        private const float PreviewOriginX = 0.5f;
        private const float PreviewOriginY = 2.5f;
        private const float PreviewSpacingX = 3.0f;
        private const float PreviewSpacingY = 2.5f;
        private const double SpeedEpsilon = 1.0e-5;

        internal static TileDecorationResult GenerateAndCommit(scnEditor editor, IList<TrackSlot> tracks)
        {
            if (editor == null || editor.levelData == null)
                throw new InvalidOperationException("Editor is not ready.");
            if (tracks == null || tracks.Count == 0)
                throw new InvalidOperationException("No source tracks are available for tile decoration generation.");

            LevelData output = editor.levelData.Copy();
            int selectedFloor = GameAngleProbe.TryGetSelectedFloorIndex(editor);
            LevelData candidate = output.Copy();
            TileDecorationResult result = PopulateCandidate(editor, candidate, tracks);

            bool committed = false;
            try
            {
                TrackStore.RestoreSnapshot(editor, candidate, true);
                editor.UpdateDecorationObjects();
                committed = true;
                return result;
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

        private static TileDecorationResult PopulateCandidate(
            scnEditor editor,
            LevelData candidate,
            IList<TrackSlot> tracks)
        {
            LevelData active = editor.levelData.Copy();
            int selectedFloor = GameAngleProbe.TryGetSelectedFloorIndex(editor);
            List<VisualTrack> visuals = CaptureVisualTracks(editor, tracks, active, selectedFloor);

            IList decorations = candidate.decorations as IList;
            if (decorations == null)
                throw new InvalidOperationException("LevelData.decorations is not list-compatible in this game build.");

            int replaced = RemoveOwnedPreviews(decorations);
            int created = 0;
            int skippedTracks = 0;

            for (int t = 0; t < visuals.Count; t++)
            {
                VisualTrack visual = visuals[t];
                if (HasManualCompatiblePreview(decorations, visual.TrackIndex))
                {
                    skippedTracks++;
                    continue;
                }

                Vector2 origin = GetPreviewOrigin(visual.TrackIndex);
                for (int i = 0; i < visual.Tiles.Count; i++)
                {
                    decorations.Add(CreateFloorAddObject(visual, visual.Tiles[i], i, origin));
                    created++;
                }
            }

            return new TileDecorationResult
            {
                Created = created,
                Replaced = replaced,
                SkippedTracks = skippedTracks,
                Diagnostic = "Generated " + created + " geometry-correct source Floor decoration(s); replaced " + replaced
                    + " previous MTE tile decoration(s)"
                    + (skippedTracks > 0 ? "; preserved manual/EQOL preview(s) on " + skippedTracks + " track(s)." : ".")
            };
        }

        private static List<VisualTrack> CaptureVisualTracks(
            scnEditor editor,
            IList<TrackSlot> tracks,
            LevelData active,
            int selectedFloor)
        {
            var result = new List<VisualTrack>();
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
                TrackStore.RestoreSnapshot(editor, active, true);
                if (selectedFloor >= 0 && selectedFloor < editor.floors.Count)
                    editor.SelectFloor(editor.floors[selectedFloor], true);
            }
            return result;
        }

        private static VisualTrack CaptureCurrentTrack(scnEditor editor, TrackSlot slot, int trackIndex)
        {
            var floors = new List<scrFloor>();
            for (int i = 0; i < editor.floors.Count; i++)
            {
                scrFloor floor = editor.floors[i];
                if (floor == null || floor.midSpin) continue;
                floors.Add(floor);
            }

            var visual = new VisualTrack
            {
                TrackIndex = trackIndex,
                Name = slot.Name ?? ("Track " + (trackIndex + 1)),
                BaseBpm = editor.levelData.bpm
            };

            // floor 0 is represented by the initial planet pair; the final runtime
            // floor is synthetic. Preview only the real interior source floors.
            if (floors.Count < 3) return visual;

            float tileSize = ResolveTileSize();
            Vector2 startPosition = ToLevelPosition(floors[0], tileSize);

            for (int i = 1; i + 1 < floors.Count; i++)
            {
                scrFloor floor = floors[i];
                scrFloor previous = floors[i - 1];

                float visualAngle = GetVisualTrackAngle(floor);
                float rhythmAngle = GetRhythmAngle(floor);
                float rotation = GetTrackRotation(floor, visualAngle);
                double speed = ReadDouble(floor, "speed", 1.0);
                double previousSpeed = ReadDouble(previous, "speed", 1.0);

                visual.Tiles.Add(new VisualTile
                {
                    LocalPosition = ToLevelPosition(floor, tileSize) - startPosition,
                    RotationDegrees = rotation,
                    TrackAngleDegrees = visualAngle,
                    RhythmAngleDegrees = rhythmAngle,
                    Speed = speed,
                    PreviousSpeed = previousSpeed
                });
            }

            return visual;
        }

        private static LevelEvent CreateFloorAddObject(
            VisualTrack visual,
            VisualTile tile,
            int tileIndex,
            Vector2 origin)
        {
            LevelEvent ev = CreateCustomEvent("AddObject", 0);
            int rhythmKey = (int)Math.Round(tile.RhythmAngleDegrees * 10000.0, MidpointRounding.AwayFromZero);
            string baseTag = "T" + visual.TrackIndex;
            string tag = baseTag + " " + baseTag + "_" + tileIndex
                + " qolMultiTile_" + baseTag
                + " qolMultiTileRhythm_" + rhythmKey.ToString(CultureInfo.InvariantCulture)
                + " " + OwnerTag;

            SetRequiredData(ev, "objectType", "Floor");
            SetRequiredData(ev, "tag", tag);
            SetRequiredData(ev, "position", origin + tile.LocalPosition);
            SetRequiredData(ev, "relativeTo", "Global");
            SetRequiredData(ev, "rotation", tile.RotationDegrees);
            SetRequiredData(ev, "trackAngle", tile.TrackAngleDegrees);

            SetOptionalData(ev, "depth", tileIndex);
            SetOptionalData(ev, "scale", new Vector2(100f, 100f));
            SetOptionalData(ev, "lockRotation", false);
            SetOptionalData(ev, "lockScale", false);
            SetOptionalData(ev, "pivotOffset", Vector2.zero);
            SetOptionalData(ev, "parallax", Vector2.zero);
            SetOptionalData(ev, "parallaxOffset", Vector2.zero);
            SetOptionalData(ev, "syncFloorDepth", false);
            SetOptionalData(ev, "trackType", "Normal");
            SetOptionalData(ev, "trackStyle", "Standard");
            SetOptionalData(ev, "trackColorType", "Single");
            SetOptionalData(ev, "trackColor", "debb7b");
            SetOptionalData(ev, "secondaryTrackColor", "ffffff");
            SetOptionalData(ev, "trackGlowEnabled", false);
            SetOptionalData(ev, "trackGlowColor", "ffffff");
            SetOptionalData(ev, "trackOpacity", 100f);
            SetOptionalData(ev, "trackIcon", SpeedIcon(tile.PreviousSpeed, tile.Speed));
            SetOptionalData(ev, "trackIconAngle", 0f);
            SetOptionalData(ev, "trackIconFlipped", false);
            SetOptionalData(ev, "trackIconOutlines", false);
            SetOptionalData(ev, "trackGraySetSpeedIcon", false);
            SetOptionalData(ev, "trackSetSpeedIconBpm", visual.BaseBpm * tile.Speed);
            return ev;
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

        // These three helpers intentionally mirror EditorQoL's tested MultiTile geometry.
        // visual track angle is the smaller geometric arc and therefore does not flip when
        // Twirl changes gameplay direction; rhythm angle remains direction-aware for tags.
        private static float GetVisualTrackAngle(scrFloor floor)
        {
            float entryDirection = NormalizeDegrees(90f - (float)(floor.entryangle * Mathf.Rad2Deg));
            float exitDirection = NormalizeDegrees(90f - (float)(floor.exitangle * Mathf.Rad2Deg));
            float difference = Mathf.Abs(exitDirection - entryDirection);
            return difference > 180f ? 360f - difference : difference;
        }

        private static float GetRhythmAngle(scrFloor floor)
        {
            double moved = scrMisc.GetAngleMoved(floor.entryangle, floor.exitangle, !floor.isCCW);
            if (Math.Abs(moved) <= 0.000001d) return 360f;
            return Mathf.Rad2Deg * (float)moved;
        }

        private static float GetTrackRotation(scrFloor floor, float visualAngle)
        {
            float entryDirection = NormalizeDegrees(90f - (float)(floor.entryangle * Mathf.Rad2Deg));
            float exitDirection = NormalizeDegrees(90f - (float)(floor.exitangle * Mathf.Rad2Deg));
            float rotation;
            if (entryDirection > exitDirection)
            {
                rotation = entryDirection - exitDirection > 180f
                    ? entryDirection
                    : exitDirection;
            }
            else
            {
                rotation = exitDirection - entryDirection > 180f
                    ? exitDirection
                    : entryDirection;
            }
            return NormalizeSignedDegrees(rotation + visualAngle + 180f);
        }

        private static float NormalizeDegrees(float value)
        {
            float normalized = Mathf.Repeat(value, 360f);
            return Mathf.Abs(normalized) < 0.000001f ? 0f : normalized;
        }

        private static float NormalizeSignedDegrees(float value)
        {
            float normalized = Mathf.Repeat(value + 180f, 360f) - 180f;
            return Mathf.Abs(normalized) < 0.000001f ? 0f : normalized;
        }

        private static Vector2 GetPreviewOrigin(int trackIndex)
        {
            int col = trackIndex % LayoutColumns;
            int row = trackIndex / LayoutColumns;
            return new Vector2(
                PreviewOriginX + col * PreviewSpacingX,
                PreviewOriginY - row * PreviewSpacingY);
        }

        private static string SpeedIcon(double previousSpeed, double speed)
        {
            if (previousSpeed <= SpeedEpsilon || speed <= SpeedEpsilon) return "None";
            double ratio = speed / previousSpeed;
            if (Math.Abs(ratio - 2.0) <= SpeedEpsilon) return "DoubleRabbit";
            if (Math.Abs(ratio - 0.5) <= SpeedEpsilon) return "DoubleSnail";
            return "None";
        }

        private static int RemoveOwnedPreviews(IList decorations)
        {
            int removed = 0;
            for (int i = decorations.Count - 1; i >= 0; i--)
            {
                LevelEvent ev = decorations[i] as LevelEvent;
                if (ev == null || !IsEventNamed(ev, "AddObject")) continue;
                string objectType = Convert.ToString(SafeGetData(ev, "objectType"), CultureInfo.InvariantCulture) ?? "";
                if (!string.Equals(objectType, "Floor", StringComparison.OrdinalIgnoreCase)) continue;
                string tag = Convert.ToString(SafeGetData(ev, "tag"), CultureInfo.InvariantCulture) ?? "";
                if (tag.IndexOf(OwnerTag, StringComparison.Ordinal) < 0) continue;
                decorations.RemoveAt(i);
                removed++;
            }
            return removed;
        }

        private static bool HasManualCompatiblePreview(IList decorations, int trackIndex)
        {
            string marker = "qolMultiTile_T" + trackIndex;
            for (int i = 0; i < decorations.Count; i++)
            {
                LevelEvent ev = decorations[i] as LevelEvent;
                if (ev == null || !IsEventNamed(ev, "AddObject")) continue;
                string objectType = Convert.ToString(SafeGetData(ev, "objectType"), CultureInfo.InvariantCulture) ?? "";
                if (!string.Equals(objectType, "Floor", StringComparison.OrdinalIgnoreCase)) continue;
                string tag = Convert.ToString(SafeGetData(ev, "tag"), CultureInfo.InvariantCulture) ?? "";
                if (tag.IndexOf(OwnerTag, StringComparison.Ordinal) >= 0) continue;
                if (tag.IndexOf(marker, StringComparison.Ordinal) >= 0) return true;
            }
            return false;
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
            if (GCS.levelEventsInfo.TryGetValue(requestedName, out direct) && direct != null)
                return direct;

            string target = NormalizeEventName(requestedName);
            LevelEventInfo suffixMatch = null;
            foreach (KeyValuePair<string, LevelEventInfo> pair in GCS.levelEventsInfo)
            {
                LevelEventInfo info = pair.Value;
                if (info == null) continue;

                string keyNormalized = NormalizeEventName(pair.Key);
                string infoNormalized = NormalizeEventName(info.name);
                if (keyNormalized == target || infoNormalized == target)
                    return info;
                if (keyNormalized.EndsWith(target, StringComparison.Ordinal)
                    || infoNormalized.EndsWith(target, StringComparison.Ordinal))
                    suffixMatch = info;
            }

            if (suffixMatch != null) return suffixMatch;
            throw new InvalidOperationException(
                "PACL2 event metadata '" + requestedName + "' is unavailable. Make sure PACL2 is loaded before generating.");
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

        private static void SetRequiredData(LevelEvent ev, string key, object value)
        {
            if (ev == null || ev.info == null || ev.info.propertiesInfo == null
                || !ev.info.propertiesInfo.ContainsKey(key))
                throw new InvalidOperationException("Event '" + EventDisplayName(ev) + "' has no property '" + key + "'.");
            SetTypedData(ev, key, value);
        }

        private static void SetOptionalData(LevelEvent ev, string key, object value)
        {
            if (ev == null || ev.info == null || ev.info.propertiesInfo == null
                || !ev.info.propertiesInfo.ContainsKey(key)) return;
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

        private static object SafeGetData(LevelEvent ev, string key)
        {
            if (ev == null) return null;
            try { return ev[key]; }
            catch { return null; }
        }

        private static string EventDisplayName(LevelEvent ev)
        {
            if (ev == null) return "<null>";
            if (ev.info != null && !string.IsNullOrEmpty(ev.info.name)) return ev.info.name;
            return ev.eventType.ToString();
        }

        private static object ConvertFor(object value, Type targetType)
        {
            if (value == null || targetType == null) return value;
            Type actualTarget = Nullable.GetUnderlyingType(targetType) ?? targetType;
            if (actualTarget.IsInstanceOfType(value)) return value;
            if (actualTarget.IsEnum)
            {
                string text = Convert.ToString(value, CultureInfo.InvariantCulture);
                return Enum.Parse(actualTarget, text, true);
            }
            if (actualTarget == typeof(string))
                return Convert.ToString(value, CultureInfo.InvariantCulture);
            if (actualTarget == typeof(Vector2) && value is Vector2)
                return value;
            return Convert.ChangeType(value, actualTarget, CultureInfo.InvariantCulture);
        }

        private static object ReadMember(object target, string name)
        {
            if (target == null) return null;
            Type type = target.GetType();
            const R.BindingFlags flags = R.BindingFlags.Instance | R.BindingFlags.Public | R.BindingFlags.NonPublic;
            try
            {
                R.PropertyInfo p = type.GetProperty(name, flags);
                if (p != null && p.GetIndexParameters().Length == 0) return p.GetValue(target, null);
            }
            catch { }
            try
            {
                R.FieldInfo f = type.GetField(name, flags);
                if (f != null) return f.GetValue(target);
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

        private sealed class VisualTrack
        {
            internal int TrackIndex;
            internal string Name;
            internal double BaseBpm;
            internal readonly List<VisualTile> Tiles = new List<VisualTile>();
        }

        private sealed class VisualTile
        {
            internal Vector2 LocalPosition;
            internal double RotationDegrees;
            internal double TrackAngleDegrees;
            internal double RhythmAngleDegrees;
            internal double Speed;
            internal double PreviousSpeed;
        }
    }
}
