using System;
using System.Collections.Generic;

namespace KineticNapier.ADOFAIMultiTileEditor
{
    internal static class PositionGeometryPlanner
    {
        // Position Track does not continuously bend or resize an orbit. In ADOFAI the
        // planets finish the ordinary orbit, then both jump together when the positioned
        // tile is landed on. CompactLayoutPostProcessor now emits those zero-duration
        // rigid teleports. Keep this compatibility pass only to undo geometry data left
        // on a reused GenerationPlan from the older position-aware orbit implementation.
        internal static int Apply(
            scnEditor editor,
            IList<TrackSlot> tracks,
            GenerationPlan plan)
        {
            if (plan == null) return 0;
            if (tracks != null && tracks.Count != plan.Tracks.Count)
                throw new InvalidOperationException("Source track count changed after analysis.");

            for (int t = 0; t < plan.Tracks.Count; t++)
            {
                AnalyzedTrack track = plan.Tracks[t];
                for (int s = 0; s < track.Segments.Count; s++)
                {
                    TrackSegment segment = track.Segments[s];
                    if (!segment.PositionGeometryInitialized)
                    {
                        segment.SourceAmountDegrees = segment.AmountDegrees;
                        segment.PositionGeometryInitialized = true;
                    }

                    segment.AmountDegrees = segment.SourceAmountDegrees;
                    segment.DestinationRadiusMultiplier = 1.0;
                    segment.PositionGeometryApplied = false;
                }
            }

            return 0;
        }
    }
}
