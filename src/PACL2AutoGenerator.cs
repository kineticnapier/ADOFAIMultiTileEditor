using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using ADOFAI;
using ADOFAI.EditorToolkit;
using UnityEngine;

namespace KineticNapier.ADOFAIMultiTileEditor
{
    internal static class PACL2AutoGenerator
    {
        private const int LayoutColumns = 4;
        private const float GroupSpacingX = 3.0f;
        private const float GroupSpacingY = 2.5f;

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
                throw new InvalidOperationException("Analyze and verify the master region first.");

            LevelData original = editor.levelData.Copy();
            int selectedFloor = GameAngleProbe.TryGetSelectedFloorIndex(editor);
            bool success = false;

            try
            {
                LevelData prepared = original.Copy();
                int createdPlanets = EnsurePlanetDecorations(prepared, plan);
                bool createdOrbitTemplate = EnsureOrbitTemplate(prepared, plan);

                TrackStore.RestoreSnapshot(editor, prepared, true);
                OrbitCommitResult result = OrbitEmitter.GenerateAndCommit(
                    editor, plan, preview, tracks, baseTrackIndex);

                editor.ApplyEventsToFloors();
                editor.UpdateDecorationObjects();

                result.Diagnostic += " Auto setup at F" + plan.RegionStartFloor + " created " + createdPlanets
                    + " planet decoration(s)"
                    + (createdOrbitTemplate ? " and an internal Orbit template" : "")
                    + "; generated event properties were typed by ADOFAI.EditorToolkit metadata conversion.";

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
            EventService events = EditorToolkitBridge.EventsFor(levelData);

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

                if (countA > 1) throw new InvalidOperationException("Planet tag '" + track.PlanetATag + "' already has multiple AddObject decorations.");
                if (countB > 1) throw new InvalidOperationException("Planet tag '" + track.PlanetBTag + "' already has multiple AddObject decorations.");
                if (countA == 1 && countB == 1) continue;

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

                CreatePlanetAddObject(events, track.PlanetATag, aPosition, true, plan.RegionStartFloor);
                CreatePlanetAddObject(events, track.PlanetBTag, bPosition, false, plan.RegionStartFloor);
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

        private static void CreatePlanetAddObject(EventService events, string tag, Vector2 position, bool isA, int floor)
        {
            EventHandle ev = events.Create("AddObject", floor, EventCollection.Decorations)
                .Set("objectType", "Planet")
                .Set("tag", tag)
                .Set("planetColorType", isA ? "DefaultRed" : "DefaultBlue")
                .Set("position", position)
                .Set("relativeTo", "Tile");

            SetOptional(ev, "pivotOffset", Vector2.zero);
            SetOptional(ev, "rotation", 0f);
            SetOptional(ev, "lockRotation", false);
            SetOptional(ev, "scale", new Vector2(100f, 100f));
            SetOptional(ev, "lockScale", false);
            SetOptional(ev, "depth", -1);
            SetOptional(ev, "syncFloorDepth", false);
            SetOptional(ev, "parallax", Vector2.zero);
            SetOptional(ev, "parallaxOffset", Vector2.zero);
        }

        private static bool EnsureOrbitTemplate(LevelData levelData, GenerationPlan plan)
        {
            IList actions = levelData.levelEvents as IList;
            if (actions == null)
                throw new InvalidOperationException("LevelData.levelEvents is not list-compatible in this game build.");

            for (int i = 0; i < actions.Count; i++)
            {
                LevelEvent ev = actions[i] as LevelEvent;
                if (ev != null && IsEventNamed(ev, "OrbitDecoration") && IsConfiguredOrbitPair(ev, plan))
                    return false;
            }

            if (plan.Tracks.Count == 0 || plan.Tracks[0].Segments.Count == 0)
                throw new InvalidOperationException("The analyzed plan has no segment available for an Orbit template.");

            TrackSegment segment = plan.Tracks[0].Segments[0];
            EditorToolkitBridge.EventsFor(levelData)
                .Create("OrbitDecoration", plan.RegionStartFloor, EventCollection.Actions)
                .Set("duration", segment.DurationBeats)
                .Set("tag", segment.MovingTag)
                .Set("centerTag", segment.CenterTag)
                .Set("amount", segment.AmountDegrees)
                .Set("lockRotation", false)
                .Set("dstRadiusMultiplier", 1.0)
                .Set("ease", "Linear")
                .Set("angleOffset", 0.0)
                .Set("eventTag", "");
            return true;
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

        private static void SetOptional(EventHandle ev, string key, object value)
        {
            Exception ignored;
            ev.TrySet(key, value, out ignored);
        }

        private static void GetAutoLayout(int trackIndex, out Vector2 center, out Vector2 moving)
        {
            int col = trackIndex % LayoutColumns;
            int row = trackIndex / LayoutColumns;
            center = new Vector2(0.5f + col * GroupSpacingX, 2.5f - row * GroupSpacingY);
            moving = center + Vector2.left;
        }

        private static bool IsEventNamed(LevelEvent ev, string requestedName)
        {
            if (ev == null) return false;
            string infoName = ev.info != null ? ev.info.name : "";
            if (string.Equals(infoName, requestedName, StringComparison.OrdinalIgnoreCase)) return true;
            if (!string.IsNullOrEmpty(infoName) && infoName.EndsWith(requestedName, StringComparison.OrdinalIgnoreCase)) return true;
            return string.Equals(ev.eventType.ToString(), requestedName, StringComparison.OrdinalIgnoreCase);
        }

        private static object SafeGetData(LevelEvent ev, string key)
        {
            if (ev == null) return null;
            try { return ev[key]; } catch { return null; }
        }
    }
}
