using UnityEngine;

namespace KineticNapier.ADOFAIMultiTileEditor
{
    internal sealed class MultiTileOverlay : MonoBehaviour
    {
        private bool visible = true;

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
        }

        private void Update()
        {
            if (!visible)
            {
                WorkbenchIntegration.Unregister();
                return;
            }

            // Keep the provider registered while moving between menu/editor scenes.
            // The pane snapshot itself reports whether an editor is available. This
            // avoids unregister -> register -> automatic OPEN traffic exactly while
            // ADOFAI is constructing scnEditor and changing scenes.
            WorkbenchIntegration.EnsureRegistered();
            WorkbenchIntegration.Tick();
        }
    }
}
