using System.Collections.Generic;

namespace KineticNapier.ADOFAIMultiTileEditor
{
    internal sealed class SpeedPoint
    {
        internal double Beat;
        internal double Speed;
    }

    internal sealed class TrackSegment
    {
        internal int TrackIndex;
        internal string TrackName;
        internal int SourceFloor;
        internal double StartBeat;
        internal double EndBeat;
        internal double DurationBeats;
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
        internal double StartBeat;
        internal double EndBeat;
        internal readonly List<TrackSegment> Segments = new List<TrackSegment>();
        internal readonly List<SpeedPoint> SpeedMap = new List<SpeedPoint>();
    }

    internal sealed class MasterAnchor
    {
        internal double Beat;
        internal readonly List<TrackSegment> StartingSegments = new List<TrackSegment>();
    }

    internal sealed class GenerationPlan
    {
        internal double StartBeat;
        internal double EndBeat;
        internal readonly List<AnalyzedTrack> Tracks = new List<AnalyzedTrack>();
        internal readonly List<MasterAnchor> Anchors = new List<MasterAnchor>();
        internal string Diagnostic;
    }
}
