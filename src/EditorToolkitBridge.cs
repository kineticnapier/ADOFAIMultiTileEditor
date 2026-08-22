using System;
using ADOFAI;
using ADOFAI.EditorToolkit;
using ADOFAI.EditorToolkit.Game;
using ToolkitEditor = ADOFAI.EditorToolkit.Editor;

namespace KineticNapier.ADOFAIMultiTileEditor
{
    internal static class EditorToolkitBridge
    {
        internal static EventService EventsFor(LevelData level)
        {
            if (level == null) throw new ArgumentNullException(nameof(level));
            EnsureConfigured();
            return ToolkitEditor.Events.ForLevel(level);
        }

        internal static void EnsureConfigured()
        {
            if (!ToolkitEditor.IsConfigured)
                ADOFAIEditorBackend.ConfigureToolkit();
        }
    }
}
