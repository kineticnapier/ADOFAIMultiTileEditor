using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using KineticNapier.ADOFAIWorkbench;
using UnityEngine;

namespace KineticNapier.ADOFAIMultiTileEditor
{
    internal static class MeasureWorkbenchIntegration
    {
        private static readonly MeasurePaneProvider Provider = new MeasurePaneProvider();
        private static bool registered;

        internal static void Register()
        {
            if (registered) return;
            Workbench.RegisterPaneProvider(Provider);
            registered = true;
        }

        internal static void Refresh()
        {
            if (registered) Provider.Refresh();
        }

        internal static void Unregister()
        {
            if (!registered) return;
            Workbench.UnregisterPaneProvider(Provider);
            registered = false;
        }
    }

    internal sealed class MeasurePaneProvider : IDockablePaneProvider
    {
        private readonly MeasurePane pane = new MeasurePane();

        public IEnumerable<IDockablePane> CreatePanes()
        {
            yield return pane;
        }

        internal void Refresh()
        {
            pane.Refresh();
        }
    }

    internal sealed class MeasurePane : IDockablePane
    {
        private MeasureState state = MeasureState.Empty("Select a range of at least two tiles.");

        public string Id { get { return "multi-tile.measure"; } }
        public string Title { get { return "Measure"; } }
        public bool CanClose { get { return true; } }

        public WorkbenchPaneView BuildView()
        {
            WorkbenchPaneView view = new WorkbenchPaneView()
                .Spacer(14)
                .Text("Tile Measure", 20f, true)
                .Spacer(8);

            if (!state.HasMeasurement)
                return view.Text(state.Message, 11f, false);

            return view
                .Text("Range: Tile " + state.FromTile.ToString(CultureInfo.InvariantCulture)
                    + " -> " + state.ToTile.ToString(CultureInfo.InvariantCulture), 11f, false)
                .Spacer(10)
                .Text("Delta X: " + FormatSigned(state.DeltaX) + " tiles", 13f, true)
                .Text("Delta Y: " + FormatSigned(state.DeltaY) + " tiles", 13f, true)
                .Text("Distance: " + state.Distance.ToString("0.000", CultureInfo.InvariantCulture) + " tiles", 13f, true);
        }

        public void HandleAction(string actionId, string argument)
        {
        }

        internal void Refresh()
        {
            MeasureState next = ReadState();
            if (state.Equals(next)) return;

            state = next;
            Workbench.PublishPane(Id);
        }

        private static MeasureState ReadState()
        {
            try
            {
                scnEditor editor = ADOBase.editor;
                if (editor == null || editor.floors == null || editor.floors.Count == 0)
                    return MeasureState.Empty("Open the ADOFAI level editor to measure tiles.");

                if (editor.selectedFloors == null || editor.selectedFloors.Count < 2)
                    return MeasureState.Empty("Select a range of at least two tiles.");

                int fromId = editor.selectedFloors.Min(floor => floor.seqID);
                int toId = editor.selectedFloors.Max(floor => floor.seqID);
                if (fromId == toId)
                    return MeasureState.Empty("Select a range of at least two tiles.");

                if (fromId < 0 || toId >= editor.floors.Count)
                    return MeasureState.Empty("Could not read the selected tile range.");

                scrFloor fromFloor = editor.floors[fromId];
                scrFloor toFloor = editor.floors[toId];
                if (fromFloor == null || toFloor == null)
                    return MeasureState.Empty("Could not read the selected tile range.");

                Transform fromTransform = fromFloor.thisTransform != null ? fromFloor.thisTransform : fromFloor.transform;
                Transform toTransform = toFloor.thisTransform != null ? toFloor.thisTransform : toFloor.transform;
                if (fromTransform == null || toTransform == null)
                    return MeasureState.Empty("Could not read tile center positions.");

                scrController controller = ADOBase.controller;
                if (controller == null || controller.tileSize <= 0.000001f)
                    return MeasureState.Empty("Could not read ADOFAI tile size.");

                Vector3 deltaWorld = toTransform.position - fromTransform.position;
                float tileSize = controller.tileSize;
                double deltaX = deltaWorld.x / tileSize;
                double deltaY = deltaWorld.y / tileSize;

                return MeasureState.Value(fromId, toId, deltaX, deltaY);
            }
            catch (Exception ex)
            {
                Debug.LogError("[ADOFAIMultiTileEditor] Measure selection read failed: " + ex);
                return MeasureState.Empty("Measure is unavailable for the current editor state.");
            }
        }

        private static string FormatSigned(double value)
        {
            if (Math.Abs(value) < 0.0005d) return "0.000";
            return value.ToString("+0.000;-0.000;0.000", CultureInfo.InvariantCulture);
        }
    }

    internal sealed class MeasureState : IEquatable<MeasureState>
    {
        private const double Epsilon = 0.0000001d;

        internal bool HasMeasurement;
        internal string Message;
        internal int FromTile;
        internal int ToTile;
        internal double DeltaX;
        internal double DeltaY;
        internal double Distance;

        internal static MeasureState Empty(string message)
        {
            return new MeasureState { Message = message ?? string.Empty };
        }

        internal static MeasureState Value(int fromTile, int toTile, double deltaX, double deltaY)
        {
            return new MeasureState
            {
                HasMeasurement = true,
                FromTile = fromTile,
                ToTile = toTile,
                DeltaX = deltaX,
                DeltaY = deltaY,
                Distance = Math.Sqrt(deltaX * deltaX + deltaY * deltaY)
            };
        }

        public bool Equals(MeasureState other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (HasMeasurement != other.HasMeasurement) return false;
            if (!HasMeasurement)
                return string.Equals(Message, other.Message, StringComparison.Ordinal);

            return FromTile == other.FromTile
                && ToTile == other.ToTile
                && Math.Abs(DeltaX - other.DeltaX) < Epsilon
                && Math.Abs(DeltaY - other.DeltaY) < Epsilon;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as MeasureState);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = HasMeasurement ? 1 : 0;
                hash = (hash * 397) ^ FromTile;
                hash = (hash * 397) ^ ToTile;
                return hash;
            }
        }
    }
}
