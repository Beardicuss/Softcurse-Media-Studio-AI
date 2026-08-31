using System;
using System.Collections.Generic;

namespace SoftcurseMediaLabAI
{
    /// <summary>Keeps undo/redo state within both an item count and a byte budget.</summary>
    public sealed class BoundedMemoryHistory<T> where T : class
    {
        private readonly LinkedList<(T Item, long Bytes)> _undo = new();
        private readonly LinkedList<(T Item, long Bytes)> _redo = new();
        private readonly Func<T, long> _sizeOf;

        public int MaxEntries { get; }
        public long MaxBytes { get; }
        public int UndoCount => _undo.Count;
        public int RedoCount => _redo.Count;
        public long RetainedBytes { get; private set; }

        public BoundedMemoryHistory(int maxEntries, long maxBytes, Func<T, long> sizeOf)
        {
            if (maxEntries <= 0) throw new ArgumentOutOfRangeException(nameof(maxEntries));
            if (maxBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxBytes));
            _sizeOf = sizeOf ?? throw new ArgumentNullException(nameof(sizeOf));
            MaxEntries = maxEntries;
            MaxBytes = maxBytes;
        }

        public void Record(T current)
        {
            ClearList(_redo);
            AddNewest(_undo, current);
            Trim();
        }

        public bool TryUndo(T current, out T? previous)
        {
            if (_undo.Last is null) { previous = null; return false; }
            AddNewest(_redo, current);
            previous = RemoveNewest(_undo);
            Trim();
            return true;
        }

        public bool TryRedo(T current, out T? next)
        {
            if (_redo.Last is null) { next = null; return false; }
            AddNewest(_undo, current);
            next = RemoveNewest(_redo);
            Trim();
            return true;
        }

        public void Clear()
        {
            _undo.Clear();
            _redo.Clear();
            RetainedBytes = 0;
        }

        private void AddNewest(LinkedList<(T Item, long Bytes)> list, T item)
        {
            long bytes = Math.Max(0, _sizeOf(item));
            list.AddLast((item, bytes));
            RetainedBytes += bytes;
        }

        private T RemoveNewest(LinkedList<(T Item, long Bytes)> list)
        {
            var value = list.Last!.Value;
            list.RemoveLast();
            RetainedBytes -= value.Bytes;
            return value.Item;
        }

        private void ClearList(LinkedList<(T Item, long Bytes)> list)
        {
            foreach (var value in list) RetainedBytes -= value.Bytes;
            list.Clear();
        }

        private void Trim()
        {
            while (_undo.Count + _redo.Count > MaxEntries || RetainedBytes > MaxBytes)
            {
                LinkedList<(T Item, long Bytes)> target = _undo.First is not null ? _undo : _redo;
                if (target.First is null) break;
                RetainedBytes -= target.First.Value.Bytes;
                target.RemoveFirst();
            }
        }
    }
}
