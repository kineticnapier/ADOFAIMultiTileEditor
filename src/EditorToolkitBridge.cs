using System;
using ADOFAI;
using ADOFAI.EditorToolkit;
using ADOFAI.EditorToolkit.Game;

namespace KineticNapier.ADOFAIMultiTileEditor
{
    internal static class EditorToolkitBridge
    {
        internal static EventService EventsFor(LevelData level)
        {
            if (level == null) throw new ArgumentNullException(nameof(level));
            EnsureConfigured();
            return Editor.Events.ForLevel(level);
        }

        internal static void EnsureConfigured()
        {
            if (!Editor.IsConfigured)
                ADOFAIEditorBackend.ConfigureToolkit();
        }
    }
}
