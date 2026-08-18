using System;
using System.Collections.Generic;
using UnityEngine;

namespace KineticNapier.ADOFAIMultiTileEditor
{
    internal static class PositionGeometryPlanner
    {
        private const double RadiusEpsilon = 1.0e-6;
        private const double GeometryEpsilon = 1.0e-4;

        internal static int Apply(
            scnEditor editor,
            IList<TrackSlot> tracks,
            GenerationPlan plan)
        {
            if (editor == null || editor.levelData == null || tracks == null || plan == null)
                return 0;
            if (tracks.Count != plan.Tracks.Count)
                throw new InvalidOperationException("Source track count changed after analysis.");

            ADOFAI.LevelData original = editor.levelData.Copy();
            int selectedFloor = GameAngleProbe.TryGetSelectedFloorIndex(editor);
            int adjusted = 0;

            try
            {
                for (int t = 0; t < tracks.Count; t++)
                {
                    TrackSlot slot = tracks[t];
                    AnalyzedTrack analyzed = plan.Tracks[t];
                    if (slot == null || slot.Data == null || analyzed == null)
                        throw new InvalidOperationException("Position geometry source track is unavailable.");

                    TrackStore.RestoreSnapshot(editor, slot.Data, false);
                    adjusted += ApplyCurrentTrack(editor, slot, analyzed);
                }
            }
            finally
            {
                TrackStore.RestoreSnapshot(editor, original, true);
                if (selectedFloor >= 0 && selectedFloor < editor.floors.Count)
                    editor.SelectFloor(editor.floors[selectedFloor], true);
            }

            return adjusted;
        }

        private static int ApplyCurrentTrack(scnEditor editor, TrackSlot slot, AnalyzedTrack analyzed)
        {
            if (slot.RegionStartFloor < 0 || slot.RegionStartFloor >= editor.floors.Count)
                throw new InvalidOperationException("Track '" + slot.Name + "' position geometry start is outside its source path.");

            scrFloor requestedStart = editor.floors[slot.RegionStartFloor];
            if (requestedStart == null || requestedStart.midSpin)
                throw new InvalidOperationException("Track '" + slot.Name + "' position geometry start must be landable.");

            var floors = new List<scrFloor>();
            int regionIndex = -1;
            for (int i = 0; i < editor.floors.Count; i++)
            {
                scrFloor floor = editor.floors[i];
                if (floor == null || floor.midSpin) continue;
                if (ReferenceEquals(floor, requestedStart)) regionIndex = floors.Count;
                floors.Add(floor);
            }

            if (regionIndex < 0)
                throw new InvalidOperationException("Track '" + slot.Name + "' position geometry start could not be reconstructed.");
            if (regionIndex + analyzed.Segments.Count >= floors.Count)
                throw new InvalidOperationException("Track '" + slot.Name + "' position geometry no longer matches its analyzed segment count.");

            float tileSize = ResolveTileSize();
            int adjusted = 0;

            for (int s = 0; s < analyzed.Segments.Count; s++)
            {
                TrackSegment segment = analyzed.Segments[s];
                if (!segment.PositionGeometryInitialized)
                {
                    segment.SourceAmountDegrees = segment.AmountDegrees;
                    segment.PositionGeometryInitialized = true;
                }

                segment.AmountDegrees = segment.SourceAmountDegrees;
                segment.DestinationRadiusMultiplier = 1.0;
                segment.PositionGeometryApplied = false;

                int currentIndex = regionIndex + s;
                if (currentIndex <= 0 || currentIndex + 1 >= floors.Count)
                    continue;

                Vector2 previous = ToLevelPosition(floors[currentIndex - 1], tileSize);
                Vector2 current = ToLevelPosition(floors[currentIndex], tileSize);
                Vector2 next = ToLevelPosition(floors[currentIndex + 1], tileSize);

                Vector2 startVector = previous - current;
                Vector2 endVector = next - current;
                double startRadius = startVector.magnitude;
                double endRadius = endVector.magnitude;
                if (!(startRadius > RadiusEpsilon) || !(endRadius > RadiusEpsilon))
                    continue;

                double signedVisual = Math.Atan2(
                    startVector.x * endVector.y - startVector.y * endVector.x,
                    Vector2.Dot(startVector, endVector)) * Mathf.Rad2Deg;

                double sourceAmount = segment.SourceAmountDegrees;
                double direct = NearestEquivalent(signedVisual, sourceAmount);
                double mirrored = NearestEquivalent(-signedVisual, sourceAmount);
                double visualAmount = Math.Abs(direct - sourceAmount) <= Math.Abs(mirrored - sourceAmount)
                    ? direct
                    : mirrored;

                double radiusMultiplier = endRadius / startRadius;
                if (!(radiusMultiplier > RadiusEpsilon)
                    || double.IsNaN(radiusMultiplier)
                    || double.IsInfinity(radiusMultiplier))
                    continue;

                segment.AmountDegrees = visualAmount;
                segment.DestinationRadiusMultiplier = radiusMultiplier;
                segment.PositionGeometryApplied =
                    Math.Abs(visualAmount - sourceAmount) > GeometryEpsilon
                    || Math.Abs(radiusMultiplier - 1.0) > GeometryEpsilon;

                if (segment.PositionGeometryApplied) adjusted++;
            }

            return adjusted;
        }

        private static double NearestEquivalent(double angle, double target)
        {
            if (double.IsNaN(angle) || double.IsInfinity(angle)) return target;
            double turns = Math.Round((target - angle) / 360.0);
            double candidate = angle + turns * 360.0;

            double plus = candidate + 360.0;
            if (Math.Abs(plus - target) < Math.Abs(candidate - target)) candidate = plus;
            double minus = candidate - 360.0;
            if (Math.Abs(minus - target) < Math.Abs(candidate - target)) candidate = minus;

            return candidate;
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
