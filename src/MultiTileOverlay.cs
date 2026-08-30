using KineticNapier.ADOFAIWorkbench;
using UnityEngine;

namespace KineticNapier.ADOFAIMultiTileEditor
{
    internal sealed class MultiTileOverlay : MonoBehaviour
    {
        private bool visible = true;
        private bool editorWasAvailable;

        internal bool Visible
        {
            get { return visible; }
            set
            {
                visible = value;
                if (visible && enabled) WorkbenchIntegration.EnsureRegistered();
                else WorkbenchIntegration.Unregister();
            }
        }

        internal void ResetPosition()
        {
            // External Workbench window owns its own OS-level position.
        }

        private void OnEnable()
        {
            if (visible) WorkbenchIntegration.EnsureRegistered();
        }

        private void OnDisable()
        {
            WorkbenchIntegration.Unregister();
            editorWasAvailable = false;
        }

        private void Update()
        {
            if (!visible)
            {
                WorkbenchIntegration.Unregister();
                editorWasAvailable = false;
                return;
            }

            // Keep the provider registered while moving between menu/editor scenes.
            WorkbenchIntegration.EnsureRegistered();
            WorkbenchIntegration.Tick();

            bool editorAvailable = ADOBase.editor != null;
            if (editorAvailable && !editorWasAvailable)
            {
                TrackStore store = TrackStore.Current;
                if (store == null || store.Tracks.Count == 0)
                    Workbench.OpenPane("mte.tracks");
            }
            editorWasAvailable = editorAvailable;
        }
    }
}
