using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using ADOFAI;
using UnityEngine;
using R = System.Reflection;

namespace KineticNapier.ADOFAIMultiTileEditor
{
    internal static class TilePreviewPostProcessor
    {
        private const string OwnerTag = "adofaiMTEGenerated";
        private const string StarterTag = "adofaiMTEStarter";
        private const string EndTag = "adofaiMTEEnd";
        private const string PreRollTag = "adofaiMTEPreroll";
        private const double SpeedEpsilon = 1.0e-5;

        internal static string ApplyAndCommit(scnEditor editor, IList<TrackSlot> tracks, GenerationPlan plan)
        {
            if (editor == null || editor.levelData == null)
                throw new InvalidOperationException("Editor is not ready.");
            if (tracks == null || tracks.Count == 0 || plan == null)
                throw new InvalidOperationException("Tile preview post-processing inputs are incomplete.");

            LevelData output = editor.levelData.Copy();
            int selectedFloor = GameAngleProbe.TryGetSelectedFloorIndex(editor);
            List<SourcePreviewTrack> sourceTracks = CaptureSourceTracks(editor, tracks, output, selectedFloor);
            LevelData candidate = output.Copy();

            int starterTiles = 0;
            int endTiles = 0;
            int iconTiles = 0;
            int redTwirlIcons = 0;
            int blueTwirlIcons = 0;
            IList decorations = candidate.decorations as IList;
            IList levelEvents = candidate.levelEvents as IList;
            if (decorations == null)
                throw new InvalidOperationException("LevelData.decorations is not list-compatible in this game build.");
            if (levelEvents == null)
                throw new InvalidOperationException("LevelData.levelEvents is not list-compatible in this game build.");

            RemoveOwnedExtraTiles(decorations);
            RemoveOwnedPreRollOrbits(levelEvents);

            for (int t = 0; t < sourceTracks.Count; t++)
            {
                SourcePreviewTrack source = sourceTracks[t];
                LevelEvent firstOwnedPreview = FindOwnedPreview(decorations, source.TrackIndex, 0);

                if (source.HasStarterTile && firstOwnedPreview != null)
                {
                    decorations.Add(CreateStarterTile(firstOwnedPreview, source));
                    starterTiles++;
                }

                for (int i = 0; i < source.Tiles.Count; i++)
                {
                    LevelEvent preview = FindOwnedPreview(decorations, source.TrackIndex, i);
                    if (preview == null) continue;

                    SourcePreviewTile tile = source.Tiles[i];
                    if (!TrySetTypedData(preview, "trackIcon", tile.TrackIcon))
                        continue;

                    float iconAngle = tile.UsePreviewRotationForIcon
                        ? ReadFloatData(preview, "rotation", 0f)
                        : tile.TrackIconAngle;

                    SetOptionalTypedData(preview, "trackIconAngle", iconAngle);
                    SetOptionalTypedData(preview, "trackIconFlipped", tile.TrackIconFlipped);
                    SetOptionalTypedData(preview, "trackIconOutlines", tile.TrackIconOutlines);
                    SetOptionalTypedData(preview, "trackRedSwirl", tile.TrackRedSwirl);
                    SetOptionalTypedData(preview, "trackGraySetSpeedIcon", tile.TrackGraySetSpeedIcon);
                    SetOptionalTypedData(preview, "trackSetSpeedIconBpm", tile.TrackSetSpeedIconBpm);

                    if (!string.Equals(tile.TrackIcon, "None", StringComparison.OrdinalIgnoreCase))
                        iconTiles++;
                    if (string.Equals(tile.TrackIcon, "Swirl", StringComparison.OrdinalIgnoreCase))
                    {
                        if (tile.TrackRedSwirl) redTwirlIcons++;
                        else blueTwirlIcons++;
                    }
                }

                if (source.HasEndTile && firstOwnedPreview != null && source.Tiles.Count > 0)
                {
                    LevelEvent lastOwnedPreview = FindOwnedPreview(
                        decorations, source.TrackIndex, source.Tiles.Count - 1) ?? firstOwnedPreview;
                    decorations.Add(CreateEndTile(firstOwnedPreview, lastOwnedPreview, source));
                    endTiles++;
                }
            }

            int preRollGroups = ApplyPlanetPreRoll(editor, candidate, plan);

            bool committed = false;
            try
            {
                TrackStore.RestoreSnapshot(editor, candidate, true);
                editor.ApplyEventsToFloors();
                editor.UpdateDecorationObjects();
                committed = true;
                return "Preview finish: added " + starterTiles + " separate 180° starter tile(s), "
                    + endTiles + " 180° Portal end tile(s), reflected " + iconTiles
                    + " source tile icon(s), Twirl colors red/blue=" + redTwirlIcons + "/" + blueTwirlIcons
                    + ", pre-rolled " + preRollGroups + " planet group(s) at region-start angular speed.";
            }
            finally
            {
                if (!committed)
                {
                    TrackStore.RestoreSnapshot(editor, output, true);
                    if (selectedFloor >= 0 && selectedFloor < editor.floors.Count)
                        editor.SelectFloor(editor.floors[selectedFloor], true);
                }
            }
        }

        private static List<SourcePreviewTrack> CaptureSourceTracks(
            scnEditor editor,
            IList<TrackSlot> tracks,
            LevelData output,
            int selectedFloor)
        {
            var result = new List<SourcePreviewTrack>();
            try
            {
                for (int t = 0; t < tracks.Count; t++)
                {
                    TrackSlot slot = tracks[t];
                    if (slot == null || slot.Data == null)
                        throw new InvalidOperationException("Track #" + (t + 1) + " has no source snapshot.");

                    TrackStore.RestoreSnapshot(editor, slot.Data, false);
                    result.Add(CaptureCurrentTrack(editor, slot, t));
                }
            }
            finally
            {
                TrackStore.RestoreSnapshot(editor, output, true);
                if (selectedFloor >= 0 && selectedFloor < editor.floors.Count)
                    editor.SelectFloor(editor.floors[selectedFloor], true);
            }
            return result;
        }

        private static SourcePreviewTrack CaptureCurrentTrack(scnEditor editor, TrackSlot slot, int trackIndex)
        {
            if (slot.RegionStartFloor < 0 || slot.RegionStartFloor >= editor.floors.Count)
                throw new InvalidOperationException("Track '" + slot.Name + "' preview start is outside its source path.");

            scrFloor requestedStart = editor.floors[slot.RegionStartFloor];
            if (requestedStart == null || requestedStart.midSpin)
                throw new InvalidOperationException("Track '" + slot.Name + "' preview start must be a landable floor.");

            var floors = new List<scrFloor>();
            int regionIndex = -1;
            for (int i = 0; i < editor.floors.Count; i++)
            {
                scrFloor floor = editor.floors[i];
                if (floor == null || floor.midSpin) continue;
                if (ReferenceEquals(floor, requestedStart)) regionIndex = floors.Count;
                floors.Add(floor);
            }

            var result = new SourcePreviewTrack { TrackIndex = trackIndex };
            if (regionIndex < 0 || regionIndex + 1 >= floors.Count) return result;

            float tileSize = ResolveTileSize();
            Vector2 startPosition = ToLevelPosition(floors[regionIndex], tileSize);

            if (regionIndex > 0)
            {
                Vector2 previousPosition = ToLevelPosition(floors[regionIndex - 1], tileSize);
                Vector2 incomingOffset = previousPosition - startPosition;
                Vector2 incomingDirection = startPosition - previousPosition;

                result.HasStarterTile = incomingOffset.sqrMagnitude > 1.0e-8f;
                result.StarterOffset = incomingOffset;
                result.StarterRotationDegrees = incomingDirection.sqrMagnitude <= 1.0e-8f
                    ? 0f
                    : Mathf.Atan2(incomingDirection.y, incomingDirection.x) * Mathf.Rad2Deg;
            }

            int previewStart = regionIndex > 0 ? regionIndex : regionIndex + 1;
            for (int i = previewStart; i + 1 < floors.Count; i++)
            {
                scrFloor floor = floors[i];
                scrFloor previous = i > 0 ? floors[i - 1] : floor;
                int floorNumber = FindRawFloorIndex(editor, floor);

                var tile = new SourcePreviewTile
                {
                    LocalOffset = ToLevelPosition(floor, tileSize) - startPosition,
                    TrackIcon = "None",
                    TrackIconAngle = 0f,
                    UsePreviewRotationForIcon = true,
                    TrackIconFlipped = false,
                    TrackIconOutlines = false,
                    TrackRedSwirl = false,
                    TrackGraySetSpeedIcon = false,
                    TrackSetSpeedIconBpm = editor.levelData.bpm * ReadDouble(floor, "speed", 1.0)
                };

                ResolveSourceIcon(editor, floorNumber, floor, previous, tile);
                result.Tiles.Add(tile);
            }

            if (floors.Count >= 2 && floors.Count - 1 > regionIndex)
            {
                scrFloor last = floors[floors.Count - 1];
                scrFloor previous = floors[floors.Count - 2];
                Vector2 lastPosition = ToLevelPosition(last, tileSize);
                Vector2 previousPosition = ToLevelPosition(previous, tileSize);
                Vector2 incoming = lastPosition - previousPosition;
                if (incoming.sqrMagnitude > 1.0e-8f)
                {
                    result.HasEndTile = true;
                    result.EndOffset = lastPosition - startPosition;
                    result.EndRotationDegrees = Mathf.Atan2(incoming.y, incoming.x) * Mathf.Rad2Deg;
                }
            }

            return result;
        }

        private static void ResolveSourceIcon(
            scnEditor editor,
            int floorNumber,
            scrFloor floor,
            scrFloor previous,
            SourcePreviewTile tile)
        {
            if (floorNumber < 0) return;

            LevelEvent speedEvent = FindLastEvent(editor, floorNumber, LevelEventType.SetSpeed);
            LevelEvent twirlEvent = FindLastEvent(editor, floorNumber, LevelEventType.Twirl);
            LevelEvent checkpointEvent = FindLastEvent(editor, floorNumber, LevelEventType.Checkpoint);
            LevelEvent multiPlanetEvent = FindLastEvent(editor, floorNumber, LevelEventType.MultiPlanet);

            double speed = ReadDouble(floor, "speed", 1.0);
            double previousSpeed = ReadDouble(previous, "speed", 1.0);
            double ratio = previousSpeed > SpeedEpsilon ? speed / previousSpeed : 1.0;

            if (speedEvent != null && ratio >= 1.9999)
            {
                tile.TrackIcon = "DoubleRabbit";
                ApplySpeedIconData(editor, floor, tile);
            }
            else if (speedEvent != null && ratio <= 0.2501)
            {
                tile.TrackIcon = "DoubleSnail";
                ApplySpeedIconData(editor, floor, tile);
            }
            else if (speedEvent != null && ratio >= 1.0499)
            {
                tile.TrackIcon = "Rabbit";
                ApplySpeedIconData(editor, floor, tile);
            }
            else if (speedEvent != null && ratio <= 0.9501)
            {
                tile.TrackIcon = "Snail";
                ApplySpeedIconData(editor, floor, tile);
            }
            else if (twirlEvent != null)
            {
                tile.TrackIcon = "Swirl";
                float localRelativeAngle = GetRelativeOrbitDegrees(floor);
                tile.TrackRedSwirl = localRelativeAngle < 180f;
                tile.TrackIconFlipped = floor.isCCW;
                tile.TrackIconAngle = tile.TrackIconFlipped
                    ? 180f - (180f - localRelativeAngle) / 2f
                    : (180f - localRelativeAngle) / 2f;
                tile.UsePreviewRotationForIcon = false;
            }
            else if (checkpointEvent != null)
            {
                tile.TrackIcon = "Checkpoint";
            }
            else if (multiPlanetEvent != null)
            {
                string planets = Convert.ToString(SafeGetData(multiPlanetEvent, "planets"), CultureInfo.InvariantCulture) ?? "";
                tile.TrackIcon = string.Equals(planets, "TwoPlanets", StringComparison.OrdinalIgnoreCase)
                    ? "MultiPlanetTwo"
                    : "MultiPlanetThreeMore";
            }
        }

        private static void ApplySpeedIconData(scnEditor editor, scrFloor floor, SourcePreviewTile tile)
        {
            tile.TrackGraySetSpeedIcon = false;
            tile.TrackSetSpeedIconBpm = editor.levelData.bpm * ReadDouble(floor, "speed", 1.0);
        }

        private static float GetRelativeOrbitDegrees(scrFloor floor)
        {
            double moved = scrMisc.GetAngleMoved(floor.entryangle, floor.exitangle, !floor.isCCW);
            float degrees = Mathf.Abs((float)moved * Mathf.Rad2Deg);
            if (degrees <= 0.000001f) return 360f;
            return degrees;
        }

        private static LevelEvent FindLastEvent(scnEditor editor, int floorNumber, LevelEventType type)
        {
            if (editor == null || editor.events == null) return null;
            for (int i = editor.events.Count - 1; i >= 0; i--)
            {
                LevelEvent ev = editor.events[i];
                if (ev != null && ev.floor == floorNumber && ev.eventType == type)
                    return ev;
            }
            return null;
        }

        private static int FindRawFloorIndex(scnEditor editor, scrFloor target)
        {
            if (editor == null || editor.floors == null || target == null) return -1;
            for (int i = 0; i < editor.floors.Count; i++)
                if (ReferenceEquals(editor.floors[i], target)) return i;
            return -1;
        }

        private static LevelEvent CreateStarterTile(LevelEvent firstPreview, SourcePreviewTrack source)
        {
            LevelEvent starter = firstPreview.Copy();
            if (starter == null)
                throw new InvalidOperationException("Could not clone the generated first Floor preview for the 180° starter tile.");

            string baseTag = "T" + source.TrackIndex;
            SetTypedData(starter, "tag",
                baseTag + " " + baseTag + "_starter qolMultiTile_" + baseTag + " "
                + OwnerTag + " " + StarterTag);

            Vector2 firstPosition = ReadVector2Data(firstPreview, "position", Vector2.zero);
            SetTypedData(starter, "position", firstPosition + source.StarterOffset);
            SetTypedData(starter, "trackAngle", 180f);
            SetTypedData(starter, "rotation", source.StarterRotationDegrees);
            SetOptionalTypedData(starter, "trackIcon", "None");
            SetOptionalTypedData(starter, "trackIconAngle", 0f);
            SetOptionalTypedData(starter, "trackIconFlipped", false);
            SetOptionalTypedData(starter, "trackIconOutlines", false);
            SetOptionalTypedData(starter, "trackRedSwirl", false);
            SetOptionalTypedData(starter, "trackGraySetSpeedIcon", false);
            return starter;
        }

        private static LevelEvent CreateEndTile(
            LevelEvent firstPreview,
            LevelEvent lastPreview,
            SourcePreviewTrack source)
        {
            LevelEvent end = lastPreview.Copy();
            if (end == null)
                throw new InvalidOperationException("Could not clone the generated Floor preview for the 180° end tile.");

            string baseTag = "T" + source.TrackIndex;
            SetTypedData(end, "tag",
                baseTag + " " + baseTag + "_end qolMultiTile_" + baseTag + " "
                + OwnerTag + " " + EndTag);

            Vector2 firstPosition = ReadVector2Data(firstPreview, "position", Vector2.zero);
            Vector2 firstLocal = source.Tiles.Count > 0 ? source.Tiles[0].LocalOffset : Vector2.zero;
            Vector2 origin = firstPosition - firstLocal;
            SetTypedData(end, "position", origin + source.EndOffset);
            SetTypedData(end, "trackAngle", 180f);
            SetTypedData(end, "rotation", source.EndRotationDegrees);
            SetOptionalTypedData(end, "depth", source.Tiles.Count + 1);
            SetTypedData(end, "trackIcon", "Portal");
            SetOptionalTypedData(end, "trackIconAngle", source.EndRotationDegrees);
            SetOptionalTypedData(end, "trackIconFlipped", false);
            SetOptionalTypedData(end, "trackIconOutlines", false);
            SetOptionalTypedData(end, "trackRedSwirl", false);
            SetOptionalTypedData(end, "trackGraySetSpeedIcon", false);
            return end;
        }

        private static int ApplyPlanetPreRoll(scnEditor editor, LevelData candidate, GenerationPlan plan)
        {
            if (editor == null || candidate == null || plan == null || plan.RegionStartFloor <= 0)
                return 0;
            if (editor.floors == null || plan.RegionStartFloor >= editor.floors.Count)
                return 0;

            IList decorations = candidate.decorations as IList;
            IList levelEvents = candidate.levelEvents as IList;
            if (decorations == null || levelEvents == null) return 0;

            LevelEvent orbitTemplate = FindOrbitTemplate(levelEvents);
            if (orbitTemplate == null) return 0;

            int applied = 0;
            for (int i = 0; i < plan.Tracks.Count; i++)
            {
                AnalyzedTrack track = plan.Tracks[i];
                int preRollFloorIndex;
                double durationBeats;
                double angleOffsetDegrees;
                double amount;
                if (!TryResolvePreRollWindow(
                    editor,
                    plan.RegionStartFloor,
                    track,
                    out preRollFloorIndex,
                    out durationBeats,
                    out angleOffsetDegrees,
                    out amount))
                    continue;

                LevelEvent planetA = FindPlanet(candidate, track.PlanetATag);
                LevelEvent planetB = FindPlanet(candidate, track.PlanetBTag);
                if (!CanPreRollPlanet(planetA, plan.RegionStartFloor, preRollFloorIndex)
                    || !CanPreRollPlanet(planetB, plan.RegionStartFloor, preRollFloorIndex))
                    continue;

                ShiftPlanetToFloor(editor, planetA, preRollFloorIndex);
                ShiftPlanetToFloor(editor, planetB, preRollFloorIndex);

                string movingTag = track.InitialPivotIsA ? track.PlanetBTag : track.PlanetATag;
                string centerTag = track.InitialPivotIsA ? track.PlanetATag : track.PlanetBTag;
                LevelEvent orbit = orbitTemplate.Copy();
                if (orbit == null) continue;

                SetEventFloor(orbit, preRollFloorIndex);
                SetTypedData(orbit, "duration", durationBeats);
                SetTypedData(orbit, "tag", movingTag);
                SetTypedData(orbit, "centerTag", centerTag);
                SetTypedData(orbit, "amount", amount);
                SetTypedData(orbit, "lockRotation", false);
                SetTypedData(orbit, "dstRadiusMultiplier", 1.0);
                SetTypedData(orbit, "ease", "Linear");
                SetTypedData(orbit, "angleOffset", angleOffsetDegrees);
                SetTypedData(orbit, "eventTag", PreRollTag);
                SetOptionalTypedData(orbit, "active", true);
                levelEvents.Add(orbit);
                applied++;
            }
            return applied;
        }

        private static bool TryResolvePreRollWindow(
            scnEditor editor,
            int regionStartFloor,
            AnalyzedTrack track,
            out int preRollFloorIndex,
            out double durationBeats,
            out double angleOffsetDegrees,
            out double amount)
        {
            preRollFloorIndex = -1;
            durationBeats = 0.0;
            angleOffsetDegrees = 0.0;
            amount = 0.0;

            if (editor == null || editor.levelData == null || editor.floors == null
                || track == null || track.Segments.Count == 0)
                return false;

            TrackSegment first = track.Segments[0];
            double firstMagnitude = Math.Abs(first.AmountDegrees);
            if (!(firstMagnitude > 0.000001) || !(first.DurationSeconds > 0.000001))
                return false;

            int previousFloorIndex = FindPreviousLandableFloorIndex(editor, regionStartFloor - 1);
            if (previousFloorIndex < 0) return false;
            scrFloor previousFloor = editor.floors[previousFloorIndex];
            if (previousFloor == null) return false;

            double baseBpm;
            try { baseBpm = Convert.ToDouble(editor.levelData.bpm, CultureInfo.InvariantCulture); }
            catch { return false; }
            double prefixSpeed = ReadDouble(previousFloor, "speed", 1.0);
            double prefixBpm = baseBpm * prefixSpeed;
            if (!(prefixBpm > 0.000001) || double.IsNaN(prefixBpm) || double.IsInfinity(prefixBpm))
                return false;

            // Match the first generated Orbit's physical angular velocity. A full 360°
            // therefore starts early enough to finish exactly at the Multi Tile boundary.
            double fullTurnSeconds = 360.0 * first.DurationSeconds / firstMagnitude;
            durationBeats = fullTurnSeconds * prefixBpm / 60.0;
            if (!(durationBeats > 0.000001) || double.IsNaN(durationBeats) || double.IsInfinity(durationBeats))
                return false;

            double cumulativeBeats = 0.0;
            int cursor = previousFloorIndex;
            while (cursor >= 0)
            {
                scrFloor floor = editor.floors[cursor];
                if (floor == null || floor.midSpin)
                {
                    cursor = FindPreviousLandableFloorIndex(editor, cursor - 1);
                    continue;
                }

                double speed = ReadDouble(floor, "speed", 1.0);
                if (Math.Abs(speed - prefixSpeed) > SpeedEpsilon)
                    break; // Keep the pre-roll in one constant-speed prefix section.

                double travelDegrees = Math.Abs(ReadDouble(floor, "angleLength", 0.0) * Mathf.Rad2Deg);
                double floorBeats = travelDegrees / 180.0;
                if (floorBeats > 0.000001)
                {
                    cumulativeBeats += floorBeats;
                    if (cumulativeBeats + 1.0e-7 >= durationBeats)
                    {
                        preRollFloorIndex = cursor;
                        double delayBeats = Math.Max(0.0, cumulativeBeats - durationBeats);
                        angleOffsetDegrees = delayBeats * 180.0;
                        amount = first.AmountDegrees >= 0.0 ? 360.0 : -360.0;
                        return true;
                    }
                }

                cursor = FindPreviousLandableFloorIndex(editor, cursor - 1);
            }
            return false;
        }

        private static int FindPreviousLandableFloorIndex(scnEditor editor, int fromIndex)
        {
            if (editor == null || editor.floors == null) return -1;
            int i = Math.Min(fromIndex, editor.floors.Count - 1);
            for (; i >= 0; i--)
            {
                scrFloor floor = editor.floors[i];
                if (floor != null && !floor.midSpin) return i;
            }
            return -1;
        }

        private static bool CanPreRollPlanet(LevelEvent planet, int startFloor, int preRollFloor)
        {
            if (planet == null) return false;
            string relativeTo = Convert.ToString(SafeGetData(planet, "relativeTo"), CultureInfo.InvariantCulture) ?? "";
            if (!string.Equals(relativeTo, "Tile", StringComparison.OrdinalIgnoreCase)) return false;
            int floor;
            if (!TryReadEventFloor(planet, out floor)) return false;
            if (floor < preRollFloor || floor > startFloor) return false;
            object position = SafeGetData(planet, "position");
            return position is Vector2 || position is Vector3;
        }

        private static void ShiftPlanetToFloor(scnEditor editor, LevelEvent planet, int targetFloor)
        {
            if (editor == null || editor.floors == null || planet == null) return;
            int currentFloor;
            if (!TryReadEventFloor(planet, out currentFloor) || currentFloor == targetFloor) return;
            if (currentFloor < 0 || currentFloor >= editor.floors.Count
                || targetFloor < 0 || targetFloor >= editor.floors.Count)
                return;

            scrFloor currentAnchor = editor.floors[currentFloor];
            scrFloor targetAnchor = editor.floors[targetFloor];
            if (currentAnchor == null || targetAnchor == null) return;

            float tileSize = ResolveTileSize();
            Vector2 anchorDelta = ToLevelPosition(currentAnchor, tileSize)
                - ToLevelPosition(targetAnchor, tileSize);
            Vector2 position = ReadVector2Data(planet, "position", Vector2.zero);
            SetTypedData(planet, "position", position + anchorDelta);
            SetEventFloor(planet, targetFloor);
        }

        private static LevelEvent FindPlanet(LevelData levelData, string requestedTag)
        {
            LevelEvent found = FindPlanetInList(levelData.decorations as IList, requestedTag);
            if (found != null) return found;
            return FindPlanetInList(levelData.levelEvents as IList, requestedTag);
        }

        private static LevelEvent FindPlanetInList(IList list, string requestedTag)
        {
            if (list == null || string.IsNullOrWhiteSpace(requestedTag)) return null;
            for (int i = 0; i < list.Count; i++)
            {
                LevelEvent ev = list[i] as LevelEvent;
                if (ev == null || !IsEventNamed(ev, "AddObject")) continue;
                string objectType = Convert.ToString(SafeGetData(ev, "objectType"), CultureInfo.InvariantCulture) ?? "";
                if (!string.Equals(objectType, "Planet", StringComparison.OrdinalIgnoreCase)) continue;
                string tag = Convert.ToString(SafeGetData(ev, "tag"), CultureInfo.InvariantCulture) ?? "";
                if (string.Equals(tag.Trim(), requestedTag.Trim(), StringComparison.Ordinal)) return ev;
            }
            return null;
        }

        private static LevelEvent FindOrbitTemplate(IList levelEvents)
        {
            if (levelEvents == null) return null;
            for (int i = levelEvents.Count - 1; i >= 0; i--)
            {
                LevelEvent ev = levelEvents[i] as LevelEvent;
                if (ev != null && IsEventNamed(ev, "OrbitDecoration")) return ev;
            }
            return null;
        }

        private static void RemoveOwnedExtraTiles(IList decorations)
        {
            if (decorations == null) return;
            for (int i = decorations.Count - 1; i >= 0; i--)
            {
                LevelEvent ev = decorations[i] as LevelEvent;
                if (ev == null || !IsEventNamed(ev, "AddObject")) continue;
                string tag = Convert.ToString(SafeGetData(ev, "tag"), CultureInfo.InvariantCulture) ?? "";
                if (!ContainsTagToken(tag, OwnerTag)) continue;
                if (ContainsTagToken(tag, StarterTag) || ContainsTagToken(tag, EndTag))
                    decorations.RemoveAt(i);
            }
        }

        private static void RemoveOwnedPreRollOrbits(IList levelEvents)
        {
            if (levelEvents == null) return;
            for (int i = levelEvents.Count - 1; i >= 0; i--)
            {
                LevelEvent ev = levelEvents[i] as LevelEvent;
                if (ev == null || !IsEventNamed(ev, "OrbitDecoration")) continue;
                string eventTag = Convert.ToString(SafeGetData(ev, "eventTag"), CultureInfo.InvariantCulture) ?? "";
                if (string.Equals(eventTag.Trim(), PreRollTag, StringComparison.Ordinal))
                    levelEvents.RemoveAt(i);
            }
        }

        private static LevelEvent FindOwnedPreview(IList decorations, int trackIndex, int tileIndex)
        {
            string exactTileTag = "T" + trackIndex + "_" + tileIndex;
            for (int i = 0; i < decorations.Count; i++)
            {
                LevelEvent ev = decorations[i] as LevelEvent;
                if (ev == null || !IsEventNamed(ev, "AddObject")) continue;
                string objectType = Convert.ToString(SafeGetData(ev, "objectType"), CultureInfo.InvariantCulture) ?? "";
                if (!string.Equals(objectType, "Floor", StringComparison.OrdinalIgnoreCase)) continue;
                string tag = Convert.ToString(SafeGetData(ev, "tag"), CultureInfo.InvariantCulture) ?? "";
                if (!ContainsTagToken(tag, OwnerTag) || !ContainsTagToken(tag, exactTileTag)) continue;
                return ev;
            }
            return null;
        }

        private static bool ContainsTagToken(string tags, string token)
        {
            if (string.IsNullOrEmpty(tags) || string.IsNullOrEmpty(token)) return false;
            string[] split = tags.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < split.Length; i++)
                if (string.Equals(split[i], token, StringComparison.Ordinal)) return true;
            return false;
        }

        private static bool IsEventNamed(LevelEvent ev, string requestedName)
        {
            if (ev == null) return false;
            string target = NormalizeEventName(requestedName);
            string infoName = ev.info != null ? NormalizeEventName(ev.info.name) : "";
            if (infoName == target || infoName.EndsWith(target, StringComparison.Ordinal)) return true;
            return NormalizeEventName(ev.eventType.ToString()) == target;
        }

        private static string NormalizeEventName(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            var chars = new List<char>(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (char.IsLetterOrDigit(c)) chars.Add(char.ToLowerInvariant(c));
            }
            return new string(chars.ToArray());
        }

        private static void SetTypedData(LevelEvent ev, string key, object value)
        {
            if (!TrySetTypedData(ev, key, value))
                throw new InvalidOperationException("Generated event cannot accept property '" + key + "' value '" + value + "'.");
        }

        private static bool TrySetTypedData(LevelEvent ev, string key, object value)
        {
            if (ev == null || ev.info == null || ev.info.propertiesInfo == null || !ev.info.propertiesInfo.ContainsKey(key))
                return false;

            object current = SafeGetData(ev, key);
            Type targetType = current != null ? current.GetType() : null;
            if (targetType == null)
            {
                ADOFAI.PropertyInfo propertyInfo;
                if (ev.info.propertiesInfo.TryGetValue(key, out propertyInfo)
                    && propertyInfo != null && propertyInfo.value_default != null)
                    targetType = propertyInfo.value_default.GetType();
            }

            try
            {
                ev[key] = targetType == null ? value : ConvertFor(value, targetType);
                if (ev.disabled != null && ev.disabled.ContainsKey(key)) ev.disabled[key] = false;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void SetOptionalTypedData(LevelEvent ev, string key, object value)
        {
            TrySetTypedData(ev, key, value);
        }

        private static object ConvertFor(object value, Type targetType)
        {
            if (value == null || targetType == null) return value;
            Type actual = Nullable.GetUnderlyingType(targetType) ?? targetType;
            if (actual.IsInstanceOfType(value)) return value;
            if (actual.IsEnum)
            {
                string text = Convert.ToString(value, CultureInfo.InvariantCulture);
                if (!Enum.IsDefined(actual, text))
                    throw new InvalidOperationException("Enum value '" + text + "' is unavailable for " + actual.Name + ".");
                return Enum.Parse(actual, text, true);
            }
            if (actual == typeof(string)) return Convert.ToString(value, CultureInfo.InvariantCulture);
            return Convert.ChangeType(value, actual, CultureInfo.InvariantCulture);
        }

        private static object SafeGetData(LevelEvent ev, string key)
        {
            if (ev == null) return null;
            try { return ev[key]; } catch { return null; }
        }

        private static Vector2 ReadVector2Data(LevelEvent ev, string key, Vector2 fallback)
        {
            object value = SafeGetData(ev, key);
            if (value is Vector2) return (Vector2)value;
            if (value is Vector3)
            {
                Vector3 v = (Vector3)value;
                return new Vector2(v.x, v.y);
            }
            return fallback;
        }

        private static float ReadFloatData(LevelEvent ev, string key, float fallback)
        {
            object value = SafeGetData(ev, key);
            if (value == null) return fallback;
            try { return Convert.ToSingle(value, CultureInfo.InvariantCulture); }
            catch { return fallback; }
        }

        private static bool TryReadEventFloor(LevelEvent ev, out int floor)
        {
            floor = -1;
            object value = ReadMember(ev, "floor") ?? ReadMember(ev, "floorIndex");
            if (value == null) return false;
            try
            {
                floor = Convert.ToInt32(value, CultureInfo.InvariantCulture);
                return true;
            }
            catch { return false; }
        }

        private static void SetEventFloor(LevelEvent ev, int floor)
        {
            if (ev == null) throw new InvalidOperationException("Cannot set the floor of a null event.");
            Type type = ev.GetType();
            const R.BindingFlags flags = R.BindingFlags.Instance | R.BindingFlags.Public | R.BindingFlags.NonPublic;
            string[] names = { "floor", "floorIndex" };
            for (int i = 0; i < names.Length; i++)
            {
                try
                {
                    R.PropertyInfo property = type.GetProperty(names[i], flags);
                    if (property != null && property.CanWrite && property.GetIndexParameters().Length == 0)
                    {
                        property.SetValue(ev, ConvertFor(floor, property.PropertyType), null);
                        return;
                    }
                }
                catch { }
                try
                {
                    R.FieldInfo field = type.GetField(names[i], flags);
                    if (field != null)
                    {
                        field.SetValue(ev, ConvertFor(floor, field.FieldType));
                        return;
                    }
                }
                catch { }
            }
            throw new InvalidOperationException("This ADOFAI build does not expose a writable LevelEvent floor member.");
        }

        private static object ReadMember(object target, string name)
        {
            if (target == null) return null;
            Type type = target.GetType();
            const R.BindingFlags flags = R.BindingFlags.Instance | R.BindingFlags.Public | R.BindingFlags.NonPublic;
            try
            {
                R.PropertyInfo property = type.GetProperty(name, flags);
                if (property != null && property.GetIndexParameters().Length == 0) return property.GetValue(target, null);
            }
            catch { }
            try
            {
                R.FieldInfo field = type.GetField(name, flags);
                if (field != null) return field.GetValue(target);
            }
            catch { }
            return null;
        }

        private static double ReadDouble(object target, string name, double fallback)
        {
            object value = ReadMember(target, name);
            if (value == null) return fallback;
            try { return Convert.ToDouble(value, CultureInfo.InvariantCulture); }
            catch { return fallback; }
        }

        private static bool ReadBool(object target, string name, bool fallback)
        {
            object value = ReadMember(target, name);
            if (value == null) return fallback;
            if (value is bool) return (bool)value;
            bool parsed;
            return bool.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out parsed) ? parsed : fallback;
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

        private sealed class SourcePreviewTrack
        {
            internal int TrackIndex;
            internal bool HasStarterTile;
            internal Vector2 StarterOffset;
            internal float StarterRotationDegrees;
            internal bool HasEndTile;
            internal Vector2 EndOffset;
            internal float EndRotationDegrees;
            internal readonly List<SourcePreviewTile> Tiles = new List<SourcePreviewTile>();
        }

        private sealed class SourcePreviewTile
        {
            internal Vector2 LocalOffset;
            internal string TrackIcon;
            internal float TrackIconAngle;
            internal bool UsePreviewRotationForIcon;
            internal bool TrackIconFlipped;
            internal bool TrackIconOutlines;
            internal bool TrackRedSwirl;
            internal bool TrackGraySetSpeedIcon;
            internal double TrackSetSpeedIconBpm;
        }
    }
}