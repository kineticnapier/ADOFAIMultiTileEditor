using System;
using System.Collections.Generic;

namespace KineticNapier.ADOFAIMultiTileEditor
{
    internal static class VirtualRepeatPlanExpander
    {
        internal static int ExpandRegistered(IList<AnalyzedTrack> tracks)
        {
            if (tracks == null) return 0;

            int added = 0;
            for (int t = 0; t < tracks.Count; t++)
            {
                AnalyzedTrack track = tracks[t];
                if (track == null || track.Segments.Count == 0) continue;

                TrackSlot slot;
                if (!TrackSlot.TryGetRegistered(track.TrackIndex, out slot) || slot == null)
                    continue;

                int repeats = slot.EffectiveRepeatCount;
                if (repeats <= 1) continue;

                int cycleCount = track.Segments.Count;
                double cycleSeconds = track.EndSeconds - track.StartSeconds;
                double cycleBeats = track.EndBeat - track.StartBeat;
                if (!(cycleSeconds > 0.0) || !(cycleBeats > TimelineMerger.BeatEpsilon))
                    throw new InvalidOperationException("Track '" + track.Name + "' has an invalid source cycle for virtual repetition.");

                var source = new List<TrackSegment>(cycleCount);
                for (int i = 0; i < cycleCount; i++) source.Add(Clone(track.Segments[i]));

                for (int cycle = 1; cycle < repeats; cycle++)
                {
                    double secondShift = cycleSeconds * cycle;
                    double beatShift = cycleBeats * cycle;
                    bool swapRoles = ((cycle * cycleCount) & 1) != 0;

                    for (int i = 0; i < source.Count; i++)
                    {
                        TrackSegment clone = Clone(source[i]);
                        clone.StartSeconds += secondShift;
                        clone.EndSeconds += secondShift;
                        clone.StartBeat += beatShift;
                        clone.EndBeat += beatShift;
                        clone.MasterAnchorIndex = -1;
                        clone.AngleSource = (clone.AngleSource ?? string.Empty)
                            + " + virtual repeat " + (cycle + 1) + "/" + repeats;

                        if (swapRoles)
                        {
                            string moving = clone.MovingTag;
                            clone.MovingTag = clone.CenterTag;
                            clone.CenterTag = moving;
                        }

                        track.Segments.Add(clone);
                        added++;
                    }
                }

                track.EndSeconds = track.StartSeconds + cycleSeconds * repeats;
                track.EndBeat = track.StartBeat + cycleBeats * repeats;
            }
            return added;
        }

        private static TrackSegment Clone(TrackSegment source)
        {
            return new TrackSegment
            {
                TrackIndex = source.TrackIndex,
                TrackName = source.TrackName,
                SourceFloor = source.SourceFloor,
                StartBeat = source.StartBeat,
                EndBeat = source.EndBeat,
                DurationBeats = source.DurationBeats,
                StartSeconds = source.StartSeconds,
                EndSeconds = source.EndSeconds,
                DurationSeconds = source.DurationSeconds,
                SourceDurationBeats = source.SourceDurationBeats,
                EffectiveBpm = source.EffectiveBpm,
                AmountDegrees = source.AmountDegrees,
                SourceAmountDegrees = source.SourceAmountDegrees,
                DestinationRadiusMultiplier = source.DestinationRadiusMultiplier,
                PositionGeometryInitialized = source.PositionGeometryInitialized,
                PositionGeometryApplied = source.PositionGeometryApplied,
                AngleSource = source.AngleSource,
                MovingTag = source.MovingTag,
                CenterTag = source.CenterTag,
                MasterAnchorIndex = -1
            };
        }
    }
}
