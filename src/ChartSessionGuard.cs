using System;
using System.IO;
using ADOFAI;

namespace KineticNapier.ADOFAIMultiTileEditor
{
    // Track snapshots are full LevelData copies. Applying one to a genuinely different
    // chart would replace the currently open chart, so destructive snapshot actions are
    // guarded by chart identity. Do NOT use LevelData object identity as chart identity:
    // ADOFAI can replace LevelData while keeping the same open chart.
    internal static class ChartSessionGuard
    {
        private static scnEditor boundEditor;
        private static object boundCustomLevel;
        private static LevelData boundLevelData;
        private static string boundChartPath = string.Empty;
        private static bool bound;

        internal static string BoundChartPath { get { return boundChartPath ?? string.Empty; } }

        internal static void Reset()
        {
            bound = false;
            boundEditor = null;
            boundCustomLevel = null;
            boundLevelData = null;
            boundChartPath = string.Empty;
        }

        internal static void AcceptCurrent(scnEditor editor)
        {
            if (editor == null || editor.levelData == null)
            {
                Reset();
                return;
            }

            boundEditor = editor;
            boundCustomLevel = editor.customLevel;
            boundLevelData = editor.levelData;
            boundChartPath = GetNormalizedChartPath(editor);
            bound = true;
        }

        internal static bool HasExternalChange(scnEditor editor)
        {
            if (editor == null || editor.levelData == null) return bound;
            if (!bound)
            {
                AcceptCurrent(editor);
                return false;
            }

            if (!SameChartIdentity(editor)) return true;

            // Same chart, but ADOFAI may have swapped the LevelData/custom-level object
            // as part of an editor operation. Rebind rather than discarding TrackStore.
            if (!ReferenceEquals(boundLevelData, editor.levelData)
                || !ReferenceEquals(boundCustomLevel, editor.customLevel))
                AcceptCurrent(editor);
            return false;
        }

        internal static void EnsureCurrent(scnEditor editor)
        {
            if (editor == null || editor.levelData == null)
                throw new InvalidOperationException("Editor is not ready.");

            if (!bound)
            {
                AcceptCurrent(editor);
                return;
            }

            if (!SameChartIdentity(editor))
            {
                throw new InvalidOperationException(
                    "The open chart changed after these MTE tracks were captured. "
                    + "The stale track queue will not be applied to the new chart.");
            }

            // Keep the guard attached to the current runtime objects for the same chart.
            if (!ReferenceEquals(boundLevelData, editor.levelData)
                || !ReferenceEquals(boundCustomLevel, editor.customLevel))
                AcceptCurrent(editor);
        }

        private static bool SameChartIdentity(scnEditor editor)
        {
            if (editor == null) return false;

            string currentPath = GetNormalizedChartPath(editor);
            if (!string.IsNullOrEmpty(boundChartPath) && !string.IsNullOrEmpty(currentPath))
            {
                return ReferenceEquals(boundEditor, editor)
                    && string.Equals(boundChartPath, currentPath, StringComparison.OrdinalIgnoreCase);
            }

            // Unsaved/untitled charts do not have a stable filesystem identity, so fall
            // back to the editor/custom-level objects. LevelData itself is intentionally
            // excluded because it is replaceable within one chart session.
            return ReferenceEquals(boundEditor, editor)
                && ReferenceEquals(boundCustomLevel, editor.customLevel);
        }

        private static string GetNormalizedChartPath(scnEditor editor)
        {
            string path = ADOBase.levelPath;
            if (string.IsNullOrWhiteSpace(path))
            {
                try
                {
                    if (editor != null && editor.customLevel != null)
                        path = editor.customLevel.levelPath;
                }
                catch { path = string.Empty; }
            }

            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            try { return Path.GetFullPath(path).Trim().ToLowerInvariant(); }
            catch { return path.Trim().ToLowerInvariant(); }
        }
    }
}
