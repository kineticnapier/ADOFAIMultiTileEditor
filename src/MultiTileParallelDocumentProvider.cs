using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using KineticNapier.ADOFAIParallelEditor;

namespace KineticNapier.ADOFAIMultiTileEditor
{
    internal sealed class MultiTileParallelDocument : IMultiEditorDocument
    {
        internal MultiTileParallelDocument(TrackSlot track)
        {
            Track = track;
            Id = "track-" + RuntimeHelpers.GetHashCode(track).ToString("x8");
        }

        internal TrackSlot Track { get; private set; }
        public string Id { get; private set; }
        public string Title { get { return Track != null && !string.IsNullOrWhiteSpace(Track.Name) ? Track.Name : "Track"; } }
        public bool IsDirty { get { return false; } }
    }

    internal sealed class MultiTileParallelDocumentProvider : IMultiEditorDocumentProvider
    {
        private readonly Dictionary<TrackSlot, MultiTileParallelDocument> documentsByTrack
            = new Dictionary<TrackSlot, MultiTileParallelDocument>();
        private readonly List<IMultiEditorDocument> documents = new List<IMultiEditorDocument>();

        private TrackStore store;
        private scnEditor editor;

        internal void Bind(scnEditor activeEditor, TrackStore activeStore)
        {
            editor = activeEditor;
            store = activeStore;
            Sync();
        }

        internal MultiTileParallelDocument StoreCurrent(string name)
        {
            if (editor == null || store == null) throw new InvalidOperationException("Editor/provider is not ready.");
            int index = store.StoreCurrent(editor, name);
            Sync();
            return index >= 0 && index < store.Tracks.Count ? GetDocument(store.Tracks[index]) : null;
        }

        internal void Sync()
        {
            documents.Clear();
            if (store == null) return;

            var alive = new HashSet<TrackSlot>();
            for (int i = 0; i < store.Tracks.Count; i++)
            {
                TrackSlot track = store.Tracks[i];
                if (track == null) continue;
                alive.Add(track);

                MultiTileParallelDocument document = GetDocument(track);
                documents.Add(document);
            }

            var stale = new List<TrackSlot>();
            foreach (KeyValuePair<TrackSlot, MultiTileParallelDocument> pair in documentsByTrack)
                if (!alive.Contains(pair.Key)) stale.Add(pair.Key);
            for (int i = 0; i < stale.Count; i++) documentsByTrack.Remove(stale[i]);
        }

        public IList<IMultiEditorDocument> Documents { get { return documents; } }

        public IMultiEditorDocument ActiveDocument
        {
            get
            {
                if (store == null || store.ActiveIndex < 0 || store.ActiveIndex >= store.Tracks.Count) return null;
                return GetDocument(store.Tracks[store.ActiveIndex]);
            }
        }

        public void Activate(IMultiEditorDocument document)
        {
            MultiTileParallelDocument target = document as MultiTileParallelDocument;
            if (target == null || target.Track == null || store == null || editor == null) return;

            for (int i = 0; i < store.Tracks.Count; i++)
            {
                if (!ReferenceEquals(store.Tracks[i], target.Track)) continue;
                store.SwitchTo(editor, i);
                Sync();
                return;
            }
        }

        public void Save(IMultiEditorDocument document)
        {
            MultiTileParallelDocument target = document as MultiTileParallelDocument;
            if (target == null || store == null || editor == null) return;

            if (store.ActiveIndex >= 0 && store.ActiveIndex < store.Tracks.Count
                && ReferenceEquals(store.Tracks[store.ActiveIndex], target.Track))
            {
                store.SaveActive(editor);
                Sync();
            }
        }

        internal MultiTileParallelDocument FindById(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < documents.Count; i++)
            {
                MultiTileParallelDocument document = documents[i] as MultiTileParallelDocument;
                if (document != null && string.Equals(document.Id, id, StringComparison.Ordinal)) return document;
            }
            return null;
        }

        private MultiTileParallelDocument GetDocument(TrackSlot track)
        {
            if (track == null) return null;
            MultiTileParallelDocument document;
            if (!documentsByTrack.TryGetValue(track, out document))
            {
                document = new MultiTileParallelDocument(track);
                documentsByTrack.Add(track, document);
            }
            return document;
        }
    }
}
