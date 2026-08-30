using System;
using ADOFAI;

namespace KineticNapier.ADOFAIMultiTileEditor
{
    // Track snapshots are full LevelData copies. Applying one to a different chart
    // would replace the currently open chart, so every destructive snapshot action
    // is guarded by the editor/custom-level/LevelData session that owns the queue.
    internal static class ChartSessionGuard
    {
        private static scnEditor boundEditor;
        private static object boundCustomLevel;
        private static LevelData boundLevelData;
        private static bool bound;

        internal static void Reset()
        {
            bound = false;
            boundEditor = null;
            boundCustomLevel = null;
            boundLevelData = null;
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
            return !Matches(editor);
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

            if (!Matches(editor))
            {
                throw new InvalidOperationException(
                    "The open chart changed after these MTE tracks were captured. "
                    + "The stale track queue will not be applied to the new chart.");
            }
        }

        private static bool Matches(scnEditor editor)
        {
            return editor != null
                && ReferenceEquals(boundEditor, editor)
                && ReferenceEquals(boundCustomLevel, editor.customLevel)
                && ReferenceEquals(boundLevelData, editor.levelData);
        }
    }
}
