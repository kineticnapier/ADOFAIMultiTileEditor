using System;
using System.Collections.Generic;

namespace KineticNapier.ADOFAIMultiTileEditor
{
    internal static class TimelineMerger
    {
        internal const double BeatEpsilon = 1.0e-6;

        internal static GenerationPlan Merge(IList<AnalyzedTrack> tracks)
        {
            if (tracks == null || tracks.Count == 0)
                throw new InvalidOperationException("No analyzed tracks were supplied.");

            int virtualSegments = VirtualRepeatPlanExpander.ExpandRegistered(tracks);

            double commonStart = tracks[0].StartBeat;
            double maxEnd = tracks[0].EndBeat;
            double maxEndSeconds = tracks[0].EndSeconds;
            for (int i = 1; i < tracks.Count; i++)
            {
                if (!NearlyEqual(tracks[i].StartBeat, commonStart))
                    throw new InvalidOperationException("Track start times do not match within timeline epsilon.");
                if (tracks[i].EndBeat > maxEnd) maxEnd = tracks[i].EndBeat;
                if (tracks[i].EndSeconds > maxEndSeconds) maxEndSeconds = tracks[i].EndSeconds;
            }

            var allTimes = new List<double>();
            for (int t = 0; t < tracks.Count; t++)
            {
                AnalyzedTrack track = tracks[t];
                if (track.Segments.Count == 0)
                    throw new InvalidOperationException("Track '" + track.Name + "' has no analyzable segments.");

                track.SourceFloors.Clear();
                double expectedStart = track.StartBeat;
                for (int s = 0; s < track.Segments.Count; s++)
                {
                    TrackSegment segment = track.Segments[s];
                    if (!NearlyEqual(segment.StartBeat, expectedStart))
                        throw new InvalidOperationException("Track '" + track.Name + "' contains a timing gap near source floor " + segment.SourceFloor + ".");
                    if (!(segment.EndBeat > segment.StartBeat + BeatEpsilon))
                        throw new InvalidOperationException("Track '" + track.Name + "' contains a zero/negative segment near source floor " + segment.SourceFloor + ".");
                    expectedStart = segment.EndBeat;
                    allTimes.Add(segment.StartBeat);
                    allTimes.Add(segment.EndBeat);
                    track.SourceFloors.Add(new SourceFloorPoint { Floor = segment.SourceFloor, Beat = segment.StartBeat });
                }
                TrackSegment last = track.Segments[track.Segments.Count - 1];
                track.SourceFloors.Add(new SourceFloorPoint { Floor = last.SourceFloor + 1, Beat = last.EndBeat });

                if (!NearlyEqual(expectedStart, track.EndBeat))
                    throw new InvalidOperationException("Track '" + track.Name + "' did not terminate at its analyzed end beat.");
            }

            allTimes.Sort();
            var canonicalTimes = new List<double>();
            for (int i = 0; i < allTimes.Count; i++)
            {
                double time = allTimes[i];
                if (canonicalTimes.Count == 0 || !NearlyEqual(time, canonicalTimes[canonicalTimes.Count - 1]))
                    canonicalTimes.Add(time);
            }

            var plan = new GenerationPlan
            {
                StartBeat = commonStart,
                EndBeat = maxEnd,
                EndSeconds = maxEndSeconds
            };
            for (int i = 0; i < tracks.Count; i++) plan.Tracks.Add(tracks[i]);
            for (int i = 0; i < canonicalTimes.Count; i++)
                plan.Anchors.Add(new MasterAnchor { Beat = canonicalTimes[i] });

            for (int t = 0; t < tracks.Count; t++)
            {
                AnalyzedTrack track = tracks[t];
                for (int s = 0; s < track.Segments.Count; s++)
                {
                    TrackSegment segment = track.Segments[s];
                    int startIndex = FindAnchorIndex(plan.Anchors, segment.StartBeat);
                    int endIndex = FindAnchorIndex(plan.Anchors, segment.EndBeat);
                    if (startIndex < 0 || endIndex < 0 || endIndex <= startIndex)
                        throw new InvalidOperationException("Could not map source segment to the merged timeline.");
                    segment.MasterAnchorIndex = startIndex;
                    plan.Anchors[startIndex].StartingSegments.Add(segment);
                }
            }

            // TrackAnalyzer historically copies track 0's EndSeconds back onto the plan after Merge.
            // Keep that compatibility field at the true merged maximum while beat termination stays independent.
            tracks[0].EndSeconds = maxEndSeconds;

            plan.Diagnostic = "Plan OK: " + plan.Tracks.Count + " track(s), "
                + CountSegments(plan.Tracks) + " segment(s), " + plan.Anchors.Count
                + " master anchor(s), duration " + (plan.EndBeat - plan.StartBeat).ToString("0.######")
                + " beats (tracks may end independently)"
                + (virtualSegments > 0 ? "; added " + virtualSegments + " virtual-repeat segment(s) from one stored source cycle" : "")
                + ".";
            return plan;
        }

        internal static bool NearlyEqual(double a, double b)
        {
            return Math.Abs(a - b) <= BeatEpsilon;
        }

        internal static int FindAnchorIndex(IList<MasterAnchor> anchors, double beat)
        {
            for (int i = 0; i < anchors.Count; i++)
                if (NearlyEqual(anchors[i].Beat, beat)) return i;
            return -1;
        }

        private static int CountSegments(IList<AnalyzedTrack> tracks)
        {
            int total = 0;
            for (int i = 0; i < tracks.Count; i++) total += tracks[i].Segments.Count;
            return total;
        }
    }
}
