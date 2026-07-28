using System.Collections.Generic;
using UnityEngine;

namespace IronMeridian.Units
{
    /// <summary>
    /// Undo stack for map-editor edits. Each entry pairs a human-readable label
    /// with the delegate that reverses it, so callers describe *how* to undo
    /// their own change rather than this class having to model every edit type.
    ///
    /// Undo is deliberately one-directional: there is no redo, and the stack is
    /// cleared whenever the map is reloaded, since the actors an undo closure
    /// captured no longer exist after that.
    /// </summary>
    public static class EditHistory
    {
        const int MaxDepth = 64;

        struct Entry
        {
            public string label;
            public System.Action undo;
        }

        static readonly List<Entry> _stack = new List<Entry>();

        public static int Depth => _stack.Count;

        public static void Push(string label, System.Action undo)
        {
            if (undo == null) return;
            _stack.Add(new Entry { label = label, undo = undo });
            if (_stack.Count > MaxDepth) _stack.RemoveAt(0);
        }

        /// <summary>Reverses the most recent edit. Returns false when nothing is left.</summary>
        public static bool Undo(out string label)
        {
            label = null;
            if (_stack.Count == 0) return false;

            var entry = _stack[_stack.Count - 1];
            _stack.RemoveAt(_stack.Count - 1);
            label = entry.label;

            try
            {
                entry.undo();
            }
            catch (System.Exception e)
            {
                // A stale closure (unit already destroyed by other means)
                // shouldn't wedge the whole history.
                Debug.LogWarning($"[EditHistory] Undo of '{entry.label}' failed: {e.Message}");
                return false;
            }
            return true;
        }

        public static void Clear() => _stack.Clear();
    }
}
