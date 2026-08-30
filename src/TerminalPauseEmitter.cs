using System;
using System.Collections;
using System.Globalization;
using ADOFAI;
using ADOFAI.EditorToolkit;

namespace KineticNapier.ADOFAIMultiTileEditor
{
    internal static class TerminalPauseEmitter
    {
        private const string OwnerEventTag = "adofaiMTETerminalPause";

        internal static string Apply(LevelData levelData, GenerationPlan plan)
        {
            if (levelData == null || plan == null || plan.Anchors.Count == 0) return string.Empty;

            // Chord helper expansion rewrites segment boundaries. Reconstruct each
            // track's logical end from its final hit plus the terminal stationary wait.
            double maxEndBeat = plan.Anchors[plan.Anchors.Count - 1].Beat;
            for (int i = 0; i < plan.Tracks.Count; i++)
            {
                AnalyzedTrack track = plan.Tracks[i];
                if (track == null || track.Segments.Count == 0) continue;
                TrackSegment last = track.Segments[track.Segments.Count - 1];
                track.EndBeat = last.EndBeat + Math.Max(0.0, track.TerminalPauseBeats);
                if (track.EndBeat > maxEndBeat) maxEndBeat = track.EndBeat;
            }

            plan.EndBeat = maxEndBeat;
            if (plan.MasterBpm > TimelineMerger.BeatEpsilon)
                plan.EndSeconds = plan.StartSeconds + (plan.EndBeat - plan.StartBeat) * 60.0 / plan.MasterBpm;

            IList actions = levelData.levelEvents as IList;
            int removed = RemoveOwned(actions);

            double finalAnchorBeat = plan.Anchors[plan.Anchors.Count - 1].Beat;
            double remainingBeats = Math.Max(0.0, maxEndBeat - finalAnchorBeat);
            if (remainingBeats <= TimelineMerger.BeatEpsilon)
            {
                return removed > 0
                    ? "Terminal Pause: removed " + removed + " stale generated action(s); no final remainder required."
                    : "Terminal Pause: no final global remainder required.";
            }

            int outputFloor = plan.RegionStartFloor + plan.Anchors.Count - 1;
            EventHandle ev = EditorToolkitBridge.EventsFor(levelData)
                .Create("Pause", outputFloor, EventCollection.Actions)
                .Set("duration", remainingBeats);

            SetOptional(ev, "countdownTicks", 0);
            SetOptional(ev, "angleCorrectionDir", "None");
            SetOptional(ev, "eventTag", OwnerEventTag);

            return "Terminal Pause: emitted " + remainingBeats.ToString("0.######", CultureInfo.InvariantCulture)
                + " master beat(s) at output F" + outputFloor
                + (removed > 0 ? "; replaced " + removed + " stale generated action(s)." : ".");
        }

        private static int RemoveOwned(IList actions)
        {
            if (actions == null) return 0;
            int removed = 0;
            for (int i = actions.Count - 1; i >= 0; i--)
            {
                LevelEvent ev = actions[i] as LevelEvent;
                if (ev == null || !IsEventNamed(ev, "Pause")) continue;
                string tags = Convert.ToString(SafeGetData(ev, "eventTag"), CultureInfo.InvariantCulture) ?? string.Empty;
                if (!HasTag(tags, OwnerEventTag)) continue;
                actions.RemoveAt(i);
                removed++;
            }
            return removed;
        }

        private static bool HasTag(string tags, string requested)
        {
            string[] split = (tags ?? string.Empty).Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < split.Length; i++)
                if (string.Equals(split[i], requested, StringComparison.Ordinal)) return true;
            return false;
        }

        private static void SetOptional(EventHandle ev, string key, object value)
        {
            Exception ignored;
            ev.TrySet(key, value, out ignored);
        }

        private static bool IsEventNamed(LevelEvent ev, string requestedName)
        {
            if (ev == null) return false;
            string infoName = ev.info != null ? ev.info.name : string.Empty;
            if (string.Equals(infoName, requestedName, StringComparison.OrdinalIgnoreCase)) return true;
            return string.Equals(ev.eventType.ToString(), requestedName, StringComparison.OrdinalIgnoreCase);
        }

        private static object SafeGetData(LevelEvent ev, string key)
        {
            if (ev == null) return null;
            try { return ev[key]; } catch { return null; }
        }
    }
}
