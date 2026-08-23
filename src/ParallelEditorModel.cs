using System;
using System.Collections.Generic;

namespace KineticNapier.ADOFAIParallelEditor
{
    public interface IMultiEditorDocument
    {
        string Id { get; }
        string Title { get; }
        bool IsDirty { get; }
    }

    public interface IMultiEditorDocumentProvider
    {
        IList<IMultiEditorDocument> Documents { get; }
        IMultiEditorDocument ActiveDocument { get; }
        void Activate(IMultiEditorDocument document);
        void Save(IMultiEditorDocument document);
    }

    public enum SplitDirection
    {
        Columns,
        Rows
    }

    public abstract class WorkspaceNode
    {
    }

    public sealed class EditorGroupNode : WorkspaceNode
    {
        private readonly List<string> tabs = new List<string>();

        public EditorGroupNode(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Group id is required.", "id");
            Id = id;
        }

        public string Id { get; private set; }
        public IList<string> Tabs { get { return tabs; } }
        public string ActiveDocumentId { get; set; }

        public void EnsureTab(string documentId)
        {
            if (string.IsNullOrEmpty(documentId)) return;
            if (!tabs.Contains(documentId)) tabs.Add(documentId);
            if (string.IsNullOrEmpty(ActiveDocumentId)) ActiveDocumentId = documentId;
        }

        public void RemoveMissing(ICollection<string> validIds)
        {
            for (int i = tabs.Count - 1; i >= 0; i--)
                if (validIds == null || !validIds.Contains(tabs[i])) tabs.RemoveAt(i);

            if (!string.IsNullOrEmpty(ActiveDocumentId)
                && (validIds == null || !validIds.Contains(ActiveDocumentId)))
                ActiveDocumentId = tabs.Count > 0 ? tabs[0] : null;
        }
    }

    public sealed class SplitNode : WorkspaceNode
    {
        public SplitNode(SplitDirection direction, WorkspaceNode first, WorkspaceNode second, float ratio)
        {
            Direction = direction;
            First = first;
            Second = second;
            Ratio = ratio;
        }

        public SplitDirection Direction { get; set; }
        public WorkspaceNode First { get; set; }
        public WorkspaceNode Second { get; set; }
        public float Ratio { get; set; }
    }

    public sealed class ParallelWorkspaceModel
    {
        private readonly EditorGroupNode firstGroup = new EditorGroupNode("group-1");
        private readonly EditorGroupNode secondGroup = new EditorGroupNode("group-2");

        public ParallelWorkspaceModel()
        {
            Root = new SplitNode(SplitDirection.Columns, firstGroup, secondGroup, 0.5f);
            FocusedGroup = firstGroup;
        }

        public WorkspaceNode Root { get; set; }
        public EditorGroupNode FocusedGroup { get; set; }
        public EditorGroupNode FirstGroup { get { return firstGroup; } }
        public EditorGroupNode SecondGroup { get { return secondGroup; } }
        public bool IsSplit { get { return Root is SplitNode; } }

        public void SetSingleGroup()
        {
            Root = FocusedGroup ?? firstGroup;
        }

        public void SetTwoColumns()
        {
            Root = new SplitNode(SplitDirection.Columns, firstGroup, secondGroup, 0.5f);
        }

        public void Focus(EditorGroupNode group)
        {
            if (group != null) FocusedGroup = group;
        }

        public void SyncDocuments(IList<IMultiEditorDocument> documents)
        {
            var valid = new HashSet<string>();
            if (documents != null)
            {
                for (int i = 0; i < documents.Count; i++)
                {
                    IMultiEditorDocument document = documents[i];
                    if (document != null && !string.IsNullOrEmpty(document.Id)) valid.Add(document.Id);
                }
            }

            firstGroup.RemoveMissing(valid);
            secondGroup.RemoveMissing(valid);

            if (documents == null || documents.Count == 0) return;

            if (string.IsNullOrEmpty(firstGroup.ActiveDocumentId))
            {
                firstGroup.EnsureTab(documents[0].Id);
                firstGroup.ActiveDocumentId = documents[0].Id;
            }

            if (documents.Count > 1 && string.IsNullOrEmpty(secondGroup.ActiveDocumentId))
            {
                secondGroup.EnsureTab(documents[1].Id);
                secondGroup.ActiveDocumentId = documents[1].Id;
            }
            else if (string.IsNullOrEmpty(secondGroup.ActiveDocumentId))
            {
                secondGroup.EnsureTab(documents[0].Id);
                secondGroup.ActiveDocumentId = documents[0].Id;
            }
        }

        public void OpenInGroup(EditorGroupNode group, string documentId)
        {
            if (group == null || string.IsNullOrEmpty(documentId)) return;

            EditorGroupNode other = ReferenceEquals(group, firstGroup) ? secondGroup : firstGroup;
            string previous = group.ActiveDocumentId;

            group.EnsureTab(documentId);
            group.ActiveDocumentId = documentId;
            FocusedGroup = group;

            if (IsSplit && string.Equals(other.ActiveDocumentId, documentId, StringComparison.Ordinal))
            {
                if (!string.IsNullOrEmpty(previous) && !string.Equals(previous, documentId, StringComparison.Ordinal))
                {
                    other.EnsureTab(previous);
                    other.ActiveDocumentId = previous;
                }
                else
                {
                    other.ActiveDocumentId = null;
                }
            }
        }
    }
}
