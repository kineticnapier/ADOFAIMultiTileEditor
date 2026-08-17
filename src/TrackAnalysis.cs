using System.Collections.Generic;

namespace KineticNapier.ADOFAIMultiTileEditor
{
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

        internal double StartBeat;
        internal double EndBeat;
        internal double DurationBeats;

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

        internal int RegionStartFloor;
        internal double RegionStartHeading;
        internal bool RegionInheritedIsCCW;

        internal double StartBeat;
        internal double EndBeat;
        internal double StartSeconds;
        internal double EndSeconds;

        internal readonly List<TrackSegment> Segments = new List<TrackSegment>();
        internal readonly List<SourceFloorPoint> SourceFloors = new List<SourceFloorPoint>();
    }

    internal sealed class MasterAnchor
    {
        internal double Beat;
        internal readonly List<TrackSegment> StartingSegments = new List<TrackSegment>();
    }

    internal sealed class GenerationPlan
    {
        internal double MasterBpm;
        internal int RegionStartFloor;
        internal double RegionStartHeading;
        internal bool RegionInheritedIsCCW;

        internal double StartBeat;
        internal double EndBeat;
        internal double StartSeconds;
        internal double EndSeconds;

        internal readonly List<AnalyzedTrack> Tracks = new List<AnalyzedTrack>();
        internal readonly List<MasterAnchor> Anchors = new List<MasterAnchor>();
        internal string Diagnostic;
    }
}
