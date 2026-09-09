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
                if (visible && enabled)
                {
                    WorkbenchIntegration.EnsureRegistered();
                    GeneratedLayoutPaneRegistration.EnsureRegistered();
                }
                else
                {
                    WorkbenchIntegration.Unregister();
                    GeneratedLayoutPaneRegistration.Unregister();
                    DynamicPagingPaneRegistration.Unregister();
                }
            }
        }

        internal void ResetPosition()
        {
            // External Workbench window owns its own OS-level position.
        }

        private void OnEnable()
        {
            if (!visible) return;
            WorkbenchIntegration.EnsureRegistered();
            GeneratedLayoutPaneRegistration.EnsureRegistered();

            // 0.17.1 safety rollback: keep the paging implementation and persisted
            // settings for later recovery/testing, but do not expose or execute it until
            // its output mutation path has been validated against real charts.
            DynamicPagingPaneRegistration.Unregister();
        }

        private void OnDisable()
        {
            Main.FlushWorkspaceAutosave();
            WorkbenchIntegration.Unregister();
            GeneratedLayoutPaneRegistration.Unregister();
            DynamicPagingPaneRegistration.Unregister();
            editorWasAvailable = false;
        }

        private void OnApplicationQuit()
        {
            Main.FlushWorkspaceAutosave();
        }

        private void Update()
        {
            if (!visible)
            {
                WorkbenchIntegration.Unregister();
                GeneratedLayoutPaneRegistration.Unregister();
                DynamicPagingPaneRegistration.Unregister();
                editorWasAvailable = false;
                return;
            }

            WorkbenchIntegration.EnsureRegistered();
            GeneratedLayoutPaneRegistration.EnsureRegistered();
            DynamicPagingPaneRegistration.Unregister();
            WorkbenchIntegration.Tick();
            GeneratedLayoutPaneRegistration.Tick();

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
