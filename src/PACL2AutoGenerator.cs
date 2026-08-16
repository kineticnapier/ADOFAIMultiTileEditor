using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using ADOFAI;
using UnityEngine;

namespace KineticNapier.ADOFAIMultiTileEditor
{
    internal static class PACL2AutoGenerator
    {
        private const int LayoutColumns = 4;
        private const float GroupSpacingX = 3.0f;
        private const float GroupSpacingY = 2.5f;
        private static readonly string[] OrbitOwnedKeys =
        {
            "duration", "tag", "centerTag", "amount", "lockRotation",
            "dstRadiusMultiplier", "ease", "angleOffset", "eventTag"
        };

        internal static OrbitCommitResult GenerateAndCommit(
            scnEditor editor,
            GenerationPlan plan,
            MasterPathPreview preview,
            IList<TrackSlot> tracks,
            int baseTrackIndex)
        {
            if (editor == null || editor.levelData == null)
                throw new InvalidOperationException("Editor is not ready.");
            if (plan == null || preview == null)
                throw new InvalidOperationException("Analyze and verify the master path first.");

            LevelData original = editor.levelData.Copy();
            int selectedFloor = GameAngleProbe.TryGetSelectedFloorIndex(editor);
            bool success = false;

            try
            {
                LevelData prepared = original.Copy();
                int createdPlanets = EnsurePlanetDecorations(prepared, plan);
                bool createdOrbitTemplate = EnsureOrbitTemplate(prepared, plan);

                // OrbitEmitter validates and atomically commits from the currently active
                // output/base chart. Only decorations/actions are added here; angleData is
                // intentionally unchanged, so the verified source/base-path guard still applies.
                TrackStore.RestoreSnapshot(editor, prepared, true);
                OrbitCommitResult result = OrbitEmitter.GenerateAndCommit(
                    editor, plan, preview, tracks, baseTrackIndex);

                // v0.6 wrote custom-event data through LevelEvent's indexer before its
                // typed data bag. That can turn Single/Ease values into Double/String until
                // a save/reload Decode normalizes them. Repair them in memory and re-apply
                // events so PACL2 works immediately after generation.
                int normalized = NormalizeConfiguredOrbitTypes(editor.levelData, plan);
                if (normalized > 0)
                    editor.ApplyEventsToFloors();
                editor.UpdateDecorationObjects();

                result.Diagnostic += " Auto setup created " + createdPlanets
                    + " planet decoration(s)"
                    + (createdOrbitTemplate ? " and an internal Orbit template" : "")
                    + "; normalized " + normalized + " Orbit action(s) for immediate playback.";

                success = true;
                return result;
            }
            finally
            {
                if (!success)
                {
                    TrackStore.RestoreSnapshot(editor, original, true);
                    if (selectedFloor >= 0 && selectedFloor < editor.floors.Count)
                        editor.SelectFloor(editor.floors[selectedFloor], true);
                }
            }
        }

        private static int EnsurePlanetDecorations(LevelData levelData, GenerationPlan plan)
        {
            IList decorations = levelData.decorations as IList;
            if (decorations == null)
                throw new InvalidOperationException("LevelData.decorations is not list-compatible in this game build.");

            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < plan.Tracks.Count; i++)
            {
                counts[plan.Tracks[i].PlanetATag] = 0;
                counts[plan.Tracks[i].PlanetBTag] = 0;
            }

            CountPlanetAddObjects(levelData.levelEvents as IList, counts);
            CountPlanetAddObjects(levelData.decorations as IList, counts);

            int created = 0;
            for (int i = 0; i < plan.Tracks.Count; i++)
            {
                AnalyzedTrack track = plan.Tracks[i];
                int countA = counts[track.PlanetATag];
                int countB = counts[track.PlanetBTag];

                if (countA > 1)
                    throw new InvalidOperationException("Planet tag '" + track.PlanetATag + "' already has multiple AddObject decorations.");
                if (countB > 1)
                    throw new InvalidOperationException("Planet tag '" + track.PlanetBTag + "' already has multiple AddObject decorations.");

                if (countA == 1 && countB == 1)
                    continue;

                if (countA != 0 || countB != 0)
                {
                    throw new InvalidOperationException(
                        "Track '" + track.Name + "' has only one of its two initial planet decorations. "
                        + "For deterministic auto-layout, either keep both existing A/B planets or remove both and generate again.");
                }

                Vector2 center;
                Vector2 moving;
                GetAutoLayout(i, out center, out moving);

                Vector2 aPosition = track.InitialPivotIsA ? center : moving;
                Vector2 bPosition = track.InitialPivotIsA ? moving : center;

                decorations.Add(CreatePlanetAddObject(track.PlanetATag, aPosition, true));
                decorations.Add(CreatePlanetAddObject(track.PlanetBTag, bPosition, false));
                created += 2;
            }

            return created;
        }

        private static void CountPlanetAddObjects(IList list, IDictionary<string, int> counts)
        {
            if (list == null) return;
            for (int i = 0; i < list.Count; i++)
            {
                LevelEvent ev = list[i] as LevelEvent;
                if (ev == null || !IsEventNamed(ev, "AddObject")) continue;

                string objectType = Convert.ToString(SafeGetData(ev, "objectType"), CultureInfo.InvariantCulture) ?? "";
                if (!string.Equals(objectType, "Planet", StringComparison.OrdinalIgnoreCase)) continue;

                string tag = Convert.ToString(SafeGetData(ev, "tag"), CultureInfo.InvariantCulture) ?? "";
                tag = tag.Trim();
                if (counts.ContainsKey(tag)) counts[tag] = counts[tag] + 1;
            }
        }

        private static LevelEvent CreatePlanetAddObject(string tag, Vector2 position, bool isA)
        {
            LevelEvent ev = CreateCustomEvent("AddObject", 0);
            SetRequiredData(ev, "objectType", "Planet");
            SetRequiredData(ev, "tag", tag);
            SetRequiredData(ev, "planetColorType", isA ? "DefaultRed" : "DefaultBlue");
            SetRequiredData(ev, "position", position);
            SetRequiredData(ev, "relativeTo", "Tile");

            SetOptionalData(ev, "pivotOffset", Vector2.zero);
            SetOptionalData(ev, "rotation", 0f);
            SetOptionalData(ev, "lockRotation", false);
            SetOptionalData(ev, "scale", new Vector2(100f, 100f));
            SetOptionalData(ev, "lockScale", false);
            SetOptionalData(ev, "depth", -1);
            SetOptionalData(ev, "syncFloorDepth", false);
            SetOptionalData(ev, "parallax", Vector2.zero);
            SetOptionalData(ev, "parallaxOffset", Vector2.zero);
            return ev;
        }

        private static void GetAutoLayout(int trackIndex, out Vector2 center, out Vector2 moving)
        {
            int col = trackIndex % LayoutColumns;
            int row = trackIndex / LayoutColumns;

            // Keep the first pair close to the hand-built golden sample: the moving
            // planet starts one tile to the left of its center. More groups are laid
            // out on a deterministic grid so arbitrary track counts do not overlap.
            center = new Vector2(0.5f + col * GroupSpacingX, 2.5f - row * GroupSpacingY);
            moving = center + Vector2.left;
        }

        private static bool EnsureOrbitTemplate(LevelData levelData, GenerationPlan plan)
        {
            IList events = levelData.levelEvents as IList;
            if (events == null)
                throw new InvalidOperationException("LevelData.levelEvents is not list-compatible in this game build.");

            for (int i = 0; i < events.Count; i++)
            {
                LevelEvent ev = events[i] as LevelEvent;
                if (ev != null && IsEventNamed(ev, "OrbitDecoration") && IsConfiguredOrbitPair(ev, plan))
                    return false;
            }

            if (plan.Tracks.Count == 0 || plan.Tracks[0].Segments.Count == 0)
                throw new InvalidOperationException("The analyzed plan has no segment available for an Orbit template.");

            TrackSegment segment = plan.Tracks[0].Segments[0];
            LevelEvent template = CreateCustomEvent("OrbitDecoration", 0);
            SetRequiredData(template, "duration", segment.DurationBeats);
            SetRequiredData(template, "tag", segment.MovingTag);
            SetRequiredData(template, "centerTag", segment.CenterTag);
            SetRequiredData(template, "amount", segment.AmountDegrees);
            SetRequiredData(template, "lockRotation", false);
            SetRequiredData(template, "dstRadiusMultiplier", 1.0);
            SetRequiredData(template, "ease", "Linear");
            SetRequiredData(template, "angleOffset", 0.0);
            SetRequiredData(template, "eventTag", "");
            events.Add(template);
            return true;
        }

        private static int NormalizeConfiguredOrbitTypes(LevelData levelData, GenerationPlan plan)
        {
            IList events = levelData.levelEvents as IList;
            if (events == null) return 0;

            int normalizedEvents = 0;
            for (int i = 0; i < events.Count; i++)
            {
                LevelEvent ev = events[i] as LevelEvent;
                if (ev == null || !IsEventNamed(ev, "OrbitDecoration") || !IsConfiguredOrbitPair(ev, plan))
                    continue;

                bool changed = false;
                for (int k = 0; k < OrbitOwnedKeys.Length; k++)
                    changed |= NormalizeDataType(ev, OrbitOwnedKeys[k]);
                if (changed) normalizedEvents++;
            }
            return normalizedEvents;
        }

        private static bool NormalizeDataType(LevelEvent ev, string key)
        {
            if (ev == null || ev.info == null || ev.info.propertiesInfo == null) return false;

            ADOFAI.PropertyInfo propertyInfo;
            if (!ev.info.propertiesInfo.TryGetValue(key, out propertyInfo) || propertyInfo == null)
                return false;

            object current = SafeGetData(ev, key);
            object defaultValue = propertyInfo.value_default;
            if (current == null || defaultValue == null) return false;

            Type targetType = defaultValue.GetType();
            if (targetType.IsInstanceOfType(current)) return false;

            ev[key] = ConvertFor(current, targetType);
            if (ev.disabled != null && ev.disabled.ContainsKey(key)) ev.disabled[key] = false;
            return true;
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

        private static bool IsConfiguredOrbitPair(LevelEvent ev, GenerationPlan plan)
        {
            string moving = Convert.ToString(SafeGetData(ev, "tag"), CultureInfo.InvariantCulture) ?? "";
            string center = Convert.ToString(SafeGetData(ev, "centerTag"), CultureInfo.InvariantCulture) ?? "";
            moving = moving.Trim();
            center = center.Trim();

            for (int i = 0; i < plan.Tracks.Count; i++)
            {
                AnalyzedTrack track = plan.Tracks[i];
                if ((moving == track.PlanetATag && center == track.PlanetBTag)
                    || (moving == track.PlanetBTag && center == track.PlanetATag))
                    return true;
            }
            return false;
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
            try { return ev.GetData(key); }
            catch
            {
                try { return ev[key]; }
                catch { return null; }
            }
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
    }
}
