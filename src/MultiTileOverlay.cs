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
            // Layout is owned by ADOFAIWorkbench now.
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
            if (!Main.OverlayCanDraw || !visible)
            {
                WorkbenchIntegration.Unregister();
                return;
            }

            WorkbenchIntegration.EnsureRegistered();
        }
    }
}
