using System.Collections.Generic;

namespace KineticNapier.ADOFAIMultiTileEditor
{
    internal sealed class SpeedPoint
    {
        internal double Beat;
        internal double Speed;
    }

    internal sealed class SourceFloorPoint
    {
        internal int Floor;
        internal double Beat;
    }

    internal sealed class TrackSegment
    {
        internal int TrackIndex;
        internal string TrackName;
        internal int SourceFloor;

        // Master-timeline coordinates. These are filled after every source track has
        // been reconstructed and a common constant master BPM has been selected.
        internal double StartBeat;
        internal double EndBeat;
        internal double DurationBeats;

        // Source timing retained separately so tracks with different SetSpeed maps can
        // be merged by real time rather than by their local beat counts.
        internal double StartSeconds;
        internal double EndSeconds;
        internal double DurationSeconds;
        internal double SourceDurationBeats;
        internal double EffectiveBpm;

        internal double AmountDegrees;
        internal string AngleSource;
        internal string MovingTag;
        internal string CenterTag;
        internal int MasterAnchorIndex = -1;
    }

    internal sealed class AnalyzedTrack
    {
        internal int TrackIndex;
        internal string Name;
        internal string PlanetATag;
        internal string PlanetBTag;
        internal bool InitialPivotIsA;
        internal double BaseBpm;

        internal double StartBeat;
        internal double EndBeat;
        internal double StartSeconds;
        internal double EndSeconds;

        internal readonly List<TrackSegment> Segments = new List<TrackSegment>();
        internal readonly List<SpeedPoint> SpeedMap = new List<SpeedPoint>();
        internal readonly List<SourceFloorPoint> SourceFloors = new List<SourceFloorPoint>();
    }

    internal sealed class MasterAnchor
    {
        internal double Beat;
        internal readonly List<TrackSegment> StartingSegments = new List<TrackSegment>();
    }

    internal sealed class GenerationPlan
    {
        // The generated output deliberately uses one constant BPM. Source SetSpeed maps
        // are already baked into each segment's real-time position/duration.
        internal double MasterBpm;
        internal double StartBeat;
        internal double EndBeat;
        internal double StartSeconds;
        internal double EndSeconds;

        internal readonly List<AnalyzedTrack> Tracks = new List<AnalyzedTrack>();
        internal readonly List<MasterAnchor> Anchors = new List<MasterAnchor>();
        internal string Diagnostic;
    }
}
