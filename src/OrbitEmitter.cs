using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using ADOFAI;
using R = System.Reflection;

namespace KineticNapier.ADOFAIMultiTileEditor
{
    internal sealed class OrbitCommitResult
    {
        internal int Emitted;
        internal int Replaced;
        internal int RemappedBaseEvents;
        internal string Diagnostic;
    }

    internal static class OrbitEmitter
    {
        private static readonly string[] EventTypeNames = { "eventType", "type" };
        private static readonly string[] FloorNames = { "floor", "floorIndex" };
        private static readonly string[] DataBagNames = { "data", "values", "properties", "eventData" };
        private const double AngleDataTolerance = 0.001;

        internal static OrbitCommitResult GenerateAndCommit(
            scnEditor editor,
            GenerationPlan plan,
            MasterPathPreview preview,
            IList<TrackSlot> tracks,
            int baseTrackIndex)
        {
            if (editor == null || editor.levelData == null) throw new InvalidOperationException("Editor is not ready.");
            if (plan == null || preview == null) throw new InvalidOperationException("Analyze and verify the master region first.");
            if (preview.AngleData.Count + 1 != plan.Anchors.Count)
                throw new InvalidOperationException("Verified master region no longer matches the analyzed plan.");

            ValidateBasePath(editor, plan, preview, tracks, baseTrackIndex);

            LevelData original = editor.levelData.Copy();
            int selectedFloor = GameAngleProbe.TryGetSelectedFloorIndex(editor);
            LevelData candidate = original.Copy();

            OrbitCommitResult result = BuildCandidate(candidate, plan, preview, baseTrackIndex);

            try
            {
                TrackStore.RestoreSnapshot(editor, candidate, false);
                var verification = new MasterPathPreview();
                for (int i = 0; i < preview.AngleData.Count; i++) verification.AngleData.Add(preview.AngleData[i]);
                MasterPathBuilder.VerifyCurrentRuntime(editor, plan, verification);
            }
            finally
            {
                TrackStore.RestoreSnapshot(editor, original, true);
                if (selectedFloor >= 0 && selectedFloor < editor.floors.Count)
                    editor.SelectFloor(editor.floors[selectedFloor], true);
            }

            try
            {
                try { editor.SaveState(true, true); } catch { }
                TrackStore.RestoreSnapshot(editor, candidate, true);
            }
            catch
            {
                TrackStore.RestoreSnapshot(editor, original, true);
                throw;
            }

            result.Diagnostic = "Generated master region from F" + plan.RegionStartFloor + " + " + result.Emitted
                + " OrbitDecoration action(s). Replaced " + result.Replaced
                + " previous configured Orbit action(s); remapped " + result.RemappedBaseEvents
                + " base action(s) inside the region; prefix was preserved. Source snapshots were left unchanged.";
            return result;
        }

        private static OrbitCommitResult BuildCandidate(LevelData candidate, GenerationPlan plan, MasterPathPreview preview, int baseTrackIndex)
        {
            candidate.isOldLevel = false;
            var prefix = new List<float>();
            if (candidate.angleData == null || candidate.angleData.Count < plan.RegionStartFloor)
                throw new InvalidOperationException("Active output no longer contains the prefix through F" + plan.RegionStartFloor + ".");
            for (int i = 0; i < plan.RegionStartFloor; i++) prefix.Add(candidate.angleData[i]);
            candidate.angleData.Clear();
            for (int i = 0; i < prefix.Count; i++) candidate.angleData.Add(prefix[i]);
            for (int i = 0; i < preview.AngleData.Count; i++) candidate.angleData.Add(NormalizeAngleData(preview.AngleData[i]));

            IList events = candidate.levelEvents as IList;
            if (events == null) throw new InvalidOperationException("LevelData.levelEvents is not list-compatible in this game build.");

            ValidatePlanetDecorations(candidate, plan);
            object template = FindConfiguredOrbitTemplate(events, plan);
            if (template == null)
                throw new InvalidOperationException("No configured PACL2 OrbitDecoration template is available after automatic setup.");

            int removed = 0;
            int remapped = 0;
            int outputEndExclusive = plan.RegionStartFloor + plan.Anchors.Count;

            for (int i = events.Count - 1; i >= 0; i--)
            {
                object ev = events[i];
                if (ev == null) continue;

                int floor;
                if (!TryReadInt(ev, FloorNames, out floor) || floor < 0) continue;
                if (floor < plan.RegionStartFloor) continue; // prefix is immutable

                string typeName = GetEventTypeName(ev);
                if (IsOrbitDecoration(typeName) && IsConfiguredOrbitPair(ev, plan))
                {
                    events.RemoveAt(i);
                    removed++;
                    continue;
                }

                if (IsSourceGeometryAction(typeName))
                {
                    events.RemoveAt(i);
                    continue;
                }

                if (baseTrackIndex >= 0)
                {
                    if (baseTrackIndex >= plan.Tracks.Count)
                        throw new InvalidOperationException("Active base track index is outside the analyzed plan.");
                    int outputFloor = MapSourceFloorToOutputFloor(plan.Tracks[baseTrackIndex], plan, floor);
                    SetRequiredValue(ev, FloorNames, outputFloor);
                    remapped++;
                }
                else if (floor >= outputEndExclusive)
                {
                    throw new InvalidOperationException("Detached output contains a base action on floor " + floor + ", outside the verified master region.");
                }
            }

            int emitted = 0;
            for (int a = 0; a < plan.Anchors.Count; a++)
            {
                var ordered = new List<TrackSegment>(plan.Anchors[a].StartingSegments);
                ordered.Sort(delegate(TrackSegment x, TrackSegment y) { return x.TrackIndex.CompareTo(y.TrackIndex); });
                int outputFloor = plan.RegionStartFloor + a;

                for (int s = 0; s < ordered.Count; s++)
                {
                    TrackSegment segment = ordered[s];
                    object clone = CloneEvent(template);
                    if (clone == null) throw new InvalidOperationException("Could not clone the PACL2 OrbitDecoration template.");

                    SetRequiredValue(clone, FloorNames, outputFloor);
                    SetRequiredValue(clone, new[] { "duration" }, segment.DurationBeats);
                    SetRequiredValue(clone, new[] { "tag", "targetTag" }, segment.MovingTag);
                    SetRequiredValue(clone, new[] { "centerTag" }, segment.CenterTag);
                    SetRequiredValue(clone, new[] { "amount" }, segment.AmountDegrees);
                    SetRequiredValue(clone, new[] { "lockRotation" }, false);
                    SetRequiredValue(clone, new[] { "dstRadiusMultiplier" }, 1.0);
                    SetRequiredValue(clone, new[] { "ease" }, "Linear");
                    SetRequiredValue(clone, new[] { "angleOffset" }, 0.0);
                    SetRequiredValue(clone, new[] { "eventTag" }, "");
                    TrySetValue(clone, new[] { "active" }, true);

                    VerifyOrbitSemantic(clone, segment, outputFloor);
                    events.Add(clone);
                    emitted++;
                }
            }

            int expected = 0;
            for (int i = 0; i < plan.Tracks.Count; i++) expected += plan.Tracks[i].Segments.Count;
            if (emitted != expected)
                throw new InvalidOperationException("Orbit emission count mismatch: expected " + expected + ", built " + emitted + ".");

            return new OrbitCommitResult { Emitted = emitted, Replaced = removed, RemappedBaseEvents = remapped };
        }

        private static void ValidateBasePath(scnEditor editor, GenerationPlan plan, MasterPathPreview preview, IList<TrackSlot> tracks, int baseTrackIndex)
        {
            if (baseTrackIndex >= 0)
            {
                if (tracks == null || baseTrackIndex >= tracks.Count || tracks[baseTrackIndex].Data == null)
                    throw new InvalidOperationException("The active source track is no longer available.");
                if (!AngleDataEquivalent(editor.levelData.angleData, tracks[baseTrackIndex].Data.angleData))
                    throw new InvalidOperationException("The active base path changed after analysis. Re-run Analyze region plan and Verify master path before generating.");
            }
            else if (!GeneratedPathEquivalent(editor.levelData.angleData, plan, preview))
            {
                throw new InvalidOperationException("The detached output no longer matches the verified prefix + master region. Re-analyze before regenerating.");
            }
        }

        private static bool GeneratedPathEquivalent(IList<float> actual, GenerationPlan plan, MasterPathPreview preview)
        {
            if (actual == null || actual.Count != plan.RegionStartFloor + preview.AngleData.Count) return false;
            for (int i = 0; i < preview.AngleData.Count; i++)
            {
                double a = NormalizeComparableAngle(actual[plan.RegionStartFloor + i]);
                double b = NormalizeComparableAngle(preview.AngleData[i]);
                double d = Math.Abs(a - b);
                d = Math.Min(d, 360.0 - d);
                if (d > AngleDataTolerance) return false;
            }
            return true;
        }

        private static bool AngleDataEquivalent(IList<float> a, IList<float> b)
        {
            if (a == null || b == null || a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                double aa = NormalizeComparableAngle(a[i]);
                double bb = NormalizeComparableAngle(b[i]);
                double d = Math.Abs(aa - bb);
                d = Math.Min(d, 360.0 - d);
                if (d > AngleDataTolerance) return false;
            }
            return true;
        }

        private static double NormalizeComparableAngle(double value)
        {
            if (Math.Abs(value - 999.0) < AngleDataTolerance) return 999.0;
            value %= 360.0;
            if (value < 0.0) value += 360.0;
            if (Math.Abs(value - 360.0) < AngleDataTolerance || Math.Abs(value) < AngleDataTolerance) return 0.0;
            return value;
        }

        private static float NormalizeAngleData(float value) { return (float)NormalizeComparableAngle(value); }

        private static int MapSourceFloorToOutputFloor(AnalyzedTrack track, GenerationPlan plan, int sourceFloor)
        {
            for (int i = 0; i < track.SourceFloors.Count; i++)
            {
                SourceFloorPoint point = track.SourceFloors[i];
                if (point.Floor != sourceFloor) continue;
                int anchor = TimelineMerger.FindAnchorIndex(plan.Anchors, point.Beat);
                if (anchor >= 0) return plan.RegionStartFloor + anchor;
                break;
            }
            throw new InvalidOperationException("Base action on source floor " + sourceFloor + " cannot be mapped inside the master region for track '" + track.Name + "'.");
        }

        private static void ValidatePlanetDecorations(LevelData levelData, GenerationPlan plan)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int t = 0; t < plan.Tracks.Count; t++)
            {
                counts[plan.Tracks[t].PlanetATag] = 0;
                counts[plan.Tracks[t].PlanetBTag] = 0;
            }
            CountAddObjects(levelData.levelEvents as IList, counts);
            CountAddObjects(levelData.decorations as IList, counts);
            foreach (KeyValuePair<string, int> pair in counts)
            {
                if (pair.Value == 0) throw new InvalidOperationException("Missing PACL2 AddObject planet decoration for tag '" + pair.Key + "'.");
                if (pair.Value > 1) throw new InvalidOperationException("Expected exactly one PACL2 AddObject for tag '" + pair.Key + "', but found " + pair.Value + ".");
            }
        }

        private static void CountAddObjects(IList list, IDictionary<string, int> counts)
        {
            if (list == null) return;
            for (int i = 0; i < list.Count; i++)
            {
                object ev = list[i];
                if (ev == null) continue;
                string typeName = GetEventTypeName(ev);
                if (typeName.IndexOf("AddObject", StringComparison.OrdinalIgnoreCase) < 0) continue;
                string tag = Convert.ToString(ReadValue(ev, new[] { "tag", "objectTag" }), CultureInfo.InvariantCulture) ?? "";
                tag = tag.Trim();
                if (counts.ContainsKey(tag)) counts[tag] = counts[tag] + 1;
            }
        }

        private static object FindConfiguredOrbitTemplate(IList events, GenerationPlan plan)
        {
            for (int i = events.Count - 1; i >= 0; i--)
            {
                object ev = events[i];
                if (ev != null && IsOrbitDecoration(GetEventTypeName(ev)) && IsConfiguredOrbitPair(ev, plan)) return ev;
            }
            return null;
        }

        private static bool IsConfiguredOrbitPair(object ev, GenerationPlan plan)
        {
            string moving = Convert.ToString(ReadValue(ev, new[] { "tag", "targetTag" }), CultureInfo.InvariantCulture) ?? "";
            string center = Convert.ToString(ReadValue(ev, new[] { "centerTag" }), CultureInfo.InvariantCulture) ?? "";
            moving = moving.Trim();
            center = center.Trim();
            for (int i = 0; i < plan.Tracks.Count; i++)
            {
                AnalyzedTrack track = plan.Tracks[i];
                if ((moving == track.PlanetATag && center == track.PlanetBTag)
                    || (moving == track.PlanetBTag && center == track.PlanetATag)) return true;
            }
            return false;
        }

        private static bool IsOrbitDecoration(string typeName)
        {
            return typeName.IndexOf("Orbit", StringComparison.OrdinalIgnoreCase) >= 0
                && typeName.IndexOf("Decoration", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsSourceGeometryAction(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return false;
            return string.Equals(typeName, "Twirl", StringComparison.OrdinalIgnoreCase)
                || string.Equals(typeName, "MultiPlanet", StringComparison.OrdinalIgnoreCase)
                || string.Equals(typeName, "Pause", StringComparison.OrdinalIgnoreCase)
                || string.Equals(typeName, "Hold", StringComparison.OrdinalIgnoreCase)
                || typeName.StartsWith("FreeRoam", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetEventTypeName(object ev)
        {
            object info = ReadMember(ev, "info");
            string infoName = Convert.ToString(ReadMember(info, "name"), CultureInfo.InvariantCulture) ?? "";
            if (!string.IsNullOrWhiteSpace(infoName) && !string.Equals(infoName, "None", StringComparison.OrdinalIgnoreCase)) return infoName;
            return Convert.ToString(ReadValue(ev, EventTypeNames), CultureInfo.InvariantCulture) ?? "";
        }

        private static void VerifyOrbitSemantic(object ev, TrackSegment segment, int floor)
        {
            int actualFloor;
            if (!TryReadInt(ev, FloorNames, out actualFloor) || actualFloor != floor)
                throw new InvalidOperationException("Orbit template clone did not retain the requested output floor.");
            string moving = Convert.ToString(ReadValue(ev, new[] { "tag", "targetTag" }), CultureInfo.InvariantCulture) ?? "";
            string center = Convert.ToString(ReadValue(ev, new[] { "centerTag" }), CultureInfo.InvariantCulture) ?? "";
            if (moving.Trim() != segment.MovingTag || center.Trim() != segment.CenterTag)
                throw new InvalidOperationException("Orbit template clone did not retain the requested moving/center tags.");
            double amount;
            double duration;
            if (!TryReadDouble(ev, new[] { "amount" }, out amount) || Math.Abs(amount - segment.AmountDegrees) > 0.001)
                throw new InvalidOperationException("Orbit template clone did not retain the requested amount.");
            if (!TryReadDouble(ev, new[] { "duration" }, out duration) || Math.Abs(duration - segment.DurationBeats) > 1.0e-6)
                throw new InvalidOperationException("Orbit template clone did not retain the requested duration.");
        }

        private static object CloneEvent(object source)
        {
            if (source == null) return null;
            Type type = source.GetType();
            const R.BindingFlags flags = R.BindingFlags.Instance | R.BindingFlags.Public | R.BindingFlags.NonPublic;
            string[] methods = { "Copy", "Clone", "DeepCopy" };
            for (int i = 0; i < methods.Length; i++)
            {
                R.MethodInfo m = type.GetMethod(methods[i], flags, null, Type.EmptyTypes, null);
                if (m == null) continue;
                try { object result = m.Invoke(source, null); if (result != null) return result; } catch { }
            }
            R.MethodInfo memberwise = typeof(object).GetMethod("MemberwiseClone", R.BindingFlags.Instance | R.BindingFlags.NonPublic);
            object clone = memberwise.Invoke(source, null);
            CloneDictionaryMember(source, clone, "data");
            CloneDictionaryMember(source, clone, "values");
            CloneDictionaryMember(source, clone, "properties");
            CloneDictionaryMember(source, clone, "eventData");
            CloneDictionaryMember(source, clone, "disabled");
            return clone;
        }

        private static void CloneDictionaryMember(object source, object clone, string name)
        {
            IDictionary original = ReadMember(source, name) as IDictionary;
            if (original == null) return;
            try
            {
                IDictionary copy = Activator.CreateInstance(original.GetType()) as IDictionary;
                if (copy == null) return;
                foreach (DictionaryEntry entry in original) copy[entry.Key] = entry.Value;
                SetMember(clone, name, copy);
            }
            catch { }
        }

        private static object ReadValue(object target, string[] names)
        {
            for (int i = 0; i < names.Length; i++)
            {
                object value = ReadMember(target, names[i]);
                if (value != null) return value;
                value = ReadPropertyBag(target, names[i]);
                if (value != null) return value;
                value = ReadIndexer(target, names[i]);
                if (value != null) return value;
            }
            return null;
        }

        private static void SetRequiredValue(object target, string[] names, object value)
        {
            for (int i = 0; i < names.Length; i++)
            {
                string name = names[i];
                if (SetMember(target, name, value) || SetPropertyBag(target, name, value) || SetIndexer(target, name, value))
                {
                    MarkPropertyEnabled(target, name);
                    return;
                }
            }
            throw new InvalidOperationException("LevelEvent has no writable property for: " + string.Join(" / ", names));
        }

        private static bool TrySetValue(object target, string[] names, object value)
        {
            for (int i = 0; i < names.Length; i++)
            {
                string name = names[i];
                if (SetMember(target, name, value) || SetPropertyBag(target, name, value) || SetIndexer(target, name, value))
                {
                    MarkPropertyEnabled(target, name);
                    return true;
                }
            }
            return false;
        }

        private static void MarkPropertyEnabled(object target, string name)
        {
            IDictionary disabled = ReadMember(target, "disabled") as IDictionary;
            if (disabled == null) return;
            try { disabled[name] = false; } catch { }
        }

        private static object ReadMember(object target, string name)
        {
            if (target == null) return null;
            Type type = target.GetType();
            const R.BindingFlags flags = R.BindingFlags.Instance | R.BindingFlags.Public | R.BindingFlags.NonPublic;
            try { R.PropertyInfo p = type.GetProperty(name, flags); if (p != null && p.GetIndexParameters().Length == 0) return p.GetValue(target, null); } catch { }
            try { R.FieldInfo f = type.GetField(name, flags); if (f != null) return f.GetValue(target); } catch { }
            return null;
        }

        private static bool SetMember(object target, string name, object value)
        {
            if (target == null) return false;
            Type type = target.GetType();
            const R.BindingFlags flags = R.BindingFlags.Instance | R.BindingFlags.Public | R.BindingFlags.NonPublic;
            try
            {
                R.PropertyInfo p = type.GetProperty(name, flags);
                if (p != null && p.CanWrite && p.GetIndexParameters().Length == 0) { p.SetValue(target, ConvertFor(value, p.PropertyType), null); return true; }
            }
            catch { }
            try { R.FieldInfo f = type.GetField(name, flags); if (f != null) { f.SetValue(target, ConvertFor(value, f.FieldType)); return true; } } catch { }
            return false;
        }

        private static object ReadIndexer(object target, string key)
        {
            if (target == null) return null;
            Type type = target.GetType();
            const R.BindingFlags flags = R.BindingFlags.Instance | R.BindingFlags.Public | R.BindingFlags.NonPublic;
            foreach (R.PropertyInfo p in type.GetProperties(flags))
            {
                R.ParameterInfo[] pars = p.GetIndexParameters();
                if (pars.Length != 1 || pars[0].ParameterType != typeof(string) || !p.CanRead) continue;
                try { return p.GetValue(target, new object[] { key }); } catch { }
            }
            return null;
        }

        private static bool SetIndexer(object target, string key, object value)
        {
            if (target == null) return false;
            Type type = target.GetType();
            const R.BindingFlags flags = R.BindingFlags.Instance | R.BindingFlags.Public | R.BindingFlags.NonPublic;
            foreach (R.PropertyInfo p in type.GetProperties(flags))
            {
                R.ParameterInfo[] pars = p.GetIndexParameters();
                if (pars.Length != 1 || pars[0].ParameterType != typeof(string) || !p.CanWrite) continue;
                try { p.SetValue(target, ConvertFor(value, p.PropertyType), new object[] { key }); return true; } catch { }
            }
            return false;
        }

        private static object ReadPropertyBag(object target, string key)
        {
            for (int i = 0; i < DataBagNames.Length; i++)
            {
                IDictionary dict = ReadMember(target, DataBagNames[i]) as IDictionary;
                if (dict != null && dict.Contains(key)) return dict[key];
            }
            return null;
        }

        private static bool SetPropertyBag(object target, string key, object value)
        {
            for (int i = 0; i < DataBagNames.Length; i++)
            {
                IDictionary dict = ReadMember(target, DataBagNames[i]) as IDictionary;
                if (dict == null) continue;
                try
                {
                    object old = dict.Contains(key) ? dict[key] : null;
                    dict[key] = old == null ? value : ConvertFor(value, old.GetType());
                    return true;
                }
                catch { }
            }
            return false;
        }

        private static bool TryReadInt(object target, string[] names, out int result)
        {
            object value = ReadValue(target, names);
            try { if (value == null) { result = 0; return false; } result = Convert.ToInt32(value, CultureInfo.InvariantCulture); return true; }
            catch { result = 0; return false; }
        }

        private static bool TryReadDouble(object target, string[] names, out double result)
        {
            object value = ReadValue(target, names);
            if (value is double) { result = (double)value; return true; }
            if (value is float) { result = (float)value; return true; }
            if (value is int) { result = (int)value; return true; }
            if (value is long) { result = (long)value; return true; }
            return double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out result);
        }

        private static object ConvertFor(object value, Type targetType)
        {
            if (value == null) return null;
            Type actualTarget = Nullable.GetUnderlyingType(targetType) ?? targetType;
            if (actualTarget.IsInstanceOfType(value)) return value;
            if (actualTarget.IsEnum)
            {
                string s = Convert.ToString(value, CultureInfo.InvariantCulture);
                try { return Enum.Parse(actualTarget, s, true); } catch { return Activator.CreateInstance(actualTarget); }
            }
            if (actualTarget == typeof(string)) return Convert.ToString(value, CultureInfo.InvariantCulture);
            return Convert.ChangeType(value, actualTarget, CultureInfo.InvariantCulture);
        }
    }
}
