using System;
using UnityEngine;
using UnityModManagerNet;

namespace KineticNapier.ADOFAIMultiTileEditor
{
    public static class Main
    {
        private static UnityModManager.ModEntry.ModLogger logger;
        private static readonly TrackStore store = new TrackStore();
        private static bool enabled;
        private static string newTrackName = "";
        private static Vector2 scroll;
        private static scnEditor lastEditor;
        private static string status = "Open a level in the editor, then store the current chart.";
        private static string durationText = "1";
        private static string easeText = "Linear";
        private static bool lockRotation = true;

        public static bool Load(UnityModManager.ModEntry entry)
        {
            logger = entry.Logger;
            entry.OnToggle = OnToggle;
            entry.OnGUI = OnGUI;
            entry.OnUpdate = OnUpdate;
            logger.Log("ADOFAI Multi Tile Editor Prototype v0.3.0 loaded.");
            return true;
        }

        private static bool OnToggle(UnityModManager.ModEntry entry, bool value)
        {
            enabled = value;
            return true;
        }

        private static void OnUpdate(UnityModManager.ModEntry entry, float deltaTime)
        {
            if (!enabled) return;
            scnEditor editor = ADOBase.editor;
            if (editor == lastEditor) return;

            if (lastEditor != null && editor != lastEditor)
            {
                store.Reset();
                status = "Editor instance changed; track queue was cleared.";
            }
            lastEditor = editor;
        }

        private static void OnGUI(UnityModManager.ModEntry entry)
        {
            GUILayout.Label("Multi Tile Editor prototype v0.3.0 - angle fix + Orbit generator");
            scnEditor editor = ADOBase.editor;
            if (editor == null)
            {
                GUILayout.Label("Level editor is not active.");
                return;
            }

            GUILayout.Label("v0.3 uses the stock floor angle, converts radians to degrees, and clones an existing PACL2 Orbit Decoration as a template.");
            GUILayout.Space(6f);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Name", GUILayout.Width(50f));
            newTrackName = GUILayout.TextField(newTrackName, GUILayout.Width(180f));
            if (GUILayout.Button("Store current as track", GUILayout.Width(170f)))
            {
                Try(delegate
                {
                    int index = store.StoreCurrent(editor, newTrackName);
                    newTrackName = "";
                    status = "Stored current chart as track #" + (index + 1) + ".";
                });
            }
            if (GUILayout.Button("Save active", GUILayout.Width(100f)))
            {
                Try(delegate
                {
                    store.SaveActive(editor);
                    status = "Saved active snapshot and refreshed floor angles.";
                });
            }
            if (GUILayout.Button("Refresh angle probe", GUILayout.Width(135f)))
            {
                Try(delegate
                {
                    store.RefreshActiveAngles(editor);
                    status = "Refreshed active track angle probe.";
                });
            }
            GUILayout.EndHorizontal();

            int sharedAngles = store.GetSharedPrefixAngleCount();
            int firstBranchFloor = sharedAngles + 1;
            if (store.Tracks.Count > 0)
            {
                GUILayout.Label("Tracks: " + store.Tracks.Count + "    Shared angles: " + sharedAngles + "    First branch floor: " + firstBranchFloor);

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Set all cursors to first branch", GUILayout.Width(210f)))
                {
                    store.SetAllCursors(firstBranchFloor);
                    status = "All track cursors set to floor " + firstBranchFloor + ".";
                }
                if (GUILayout.Button("All -1 floor", GUILayout.Width(90f))) store.AdvanceAll(-1);
                if (GUILayout.Button("All +1 floor", GUILayout.Width(90f))) store.AdvanceAll(1);
                GUILayout.EndHorizontal();
            }

            scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(Math.Min(410f, 62f + store.Tracks.Count * 64f)));
            for (int i = 0; i < store.Tracks.Count; i++)
            {
                TrackSlot track = store.Tracks[i];
                GUILayout.BeginHorizontal();
                GUILayout.Label(i == store.ActiveIndex ? "▶" : " ", GUILayout.Width(18f));
                track.Name = GUILayout.TextField(track.Name ?? ("Track " + (i + 1)), GUILayout.Width(125f));
                GUILayout.Label(track.Data != null ? (track.Data.angleData.Count + " angles") : "empty", GUILayout.Width(78f));

                if (GUILayout.Button("-", GUILayout.Width(26f))) store.SetCursor(i, track.CursorFloor - 1);
                GUILayout.Label("F" + track.CursorFloor, GUILayout.Width(44f));
                if (GUILayout.Button("+", GUILayout.Width(26f))) store.SetCursor(i, track.CursorFloor + 1);

                AngleSample angle = track.CurrentAngle;
                string angleText = angle.Valid ? (angle.Degrees.ToString("0.###") + "°") : "?";
                GUILayout.Label(angleText, GUILayout.Width(72f));
                GUILayout.Label(angle.Source ?? "", GUILayout.Width(190f));

                GUI.enabled = i != store.ActiveIndex;
                if (GUILayout.Button("Switch", GUILayout.Width(62f)))
                {
                    int target = i;
                    Try(delegate
                    {
                        store.SwitchTo(editor, target);
                        status = "Switched to " + store.Tracks[target].Name + ".";
                    });
                }
                GUI.enabled = true;

                if (GUILayout.Button("X", GUILayout.Width(28f)))
                {
                    int target = i;
                    Try(delegate
                    {
                        store.Remove(editor, target);
                        status = "Removed track.";
                    });
                    GUILayout.EndHorizontal();
                    break;
                }
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Space(18f);
                GUILayout.Label("A tag", GUILayout.Width(40f));
                track.PlanetATag = GUILayout.TextField(track.PlanetATag ?? "", GUILayout.Width(90f));
                GUILayout.Label("B tag", GUILayout.Width(40f));
                track.PlanetBTag = GUILayout.TextField(track.PlanetBTag ?? "", GUILayout.Width(90f));
                GUILayout.Label("pivot: " + (track.PivotIsA ? "A" : "B"), GUILayout.Width(58f));
                if (GUILayout.Button("swap pivot", GUILayout.Width(82f))) track.PivotIsA = !track.PivotIsA;
                GUILayout.Label("move=" + (track.MovingTag ?? "") + " center=" + (track.CenterTag ?? ""), GUILayout.Width(200f));
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();

            GUILayout.Space(6f);
            GUILayout.Label("Orbit template: " + OrbitTemplateGenerator.ProbeTemplate(editor));
            GUILayout.BeginHorizontal();
            GUILayout.Label("Duration", GUILayout.Width(58f));
            durationText = GUILayout.TextField(durationText, GUILayout.Width(60f));
            GUILayout.Label("beats", GUILayout.Width(40f));
            GUILayout.Label("Ease", GUILayout.Width(34f));
            easeText = GUILayout.TextField(easeText, GUILayout.Width(90f));
            lockRotation = GUILayout.Toggle(lockRotation, "Lock rotation", GUILayout.Width(110f));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Generate Multi Tile Step at selected floor", GUILayout.Width(285f)))
            {
                Try(delegate
                {
                    float duration;
                    if (!float.TryParse(durationText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out duration) || duration <= 0f)
                        throw new InvalidOperationException("Duration must be a positive number (use '.' as decimal separator).");
                    int targetFloor = GameAngleProbe.TryGetCurrentFloorIndex(editor);
                    if (targetFloor < 0) throw new InvalidOperationException("Select a target floor in the active editor first.");

                    OrbitGenerationResult result = OrbitTemplateGenerator.Generate(editor, store.Tracks, targetFloor, duration, easeText, lockRotation);
                    store.CommitGeneratedStep();
                    status = result.Diagnostic + " Track cursors advanced and pivots swapped.";
                });
            }
            if (GUILayout.Button("Probe only", GUILayout.Width(90f)))
            {
                status = OrbitTemplateGenerator.ProbeTemplate(editor);
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);
            GUILayout.Label(status);
            GUILayout.Label("Prototype safety rule: generation is all-or-nothing, but v0.3 still expects one dummy Orbit Decoration in the active chart as a template.");
        }

        private static void Try(Action action)
        {
            try { action(); }
            catch (Exception ex)
            {
                status = ex.GetType().Name + ": " + ex.Message;
                if (logger != null) logger.Error(ex.ToString());
            }
        }
    }
}
