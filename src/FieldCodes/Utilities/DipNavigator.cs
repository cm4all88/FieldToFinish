using System;
using System.Collections.Generic;
using System.Linq;

namespace FieldCodes.Utilities
{
    /// <summary>
    /// Where the drafter has been this session, so Back returns to the structure
    /// they were working on instead of sending them to "select structure point"
    /// again.
    ///
    /// This is SESSION STATE, deliberately. It is never written to the drawing and
    /// never serialised with the project: which structures somebody looked at, and
    /// in what order, is not survey information and does not belong in the project
    /// record. Closing the window loses it, which is correct.
    /// </summary>
    public sealed class DipNavigator
    {
        private readonly List<string> _back = new List<string>();
        private readonly List<string> _forward = new List<string>();
        private readonly List<string> _recent = new List<string>();
        private string _current;

        /// <summary>How many recently visited structures to remember.</summary>
        public int RecentLimit { get; set; }

        public DipNavigator() { RecentLimit = 10; }

        /// <summary>The structure being worked on, or null before the first one.</summary>
        public string Current { get { return _current; } }

        public bool CanGoBack { get { return _back.Count > 0; } }
        public bool CanGoForward { get { return _forward.Count > 0; } }

        /// <summary>Most recently visited first, excluding the current structure.</summary>
        public IList<string> Recent
        {
            get { return _recent.Where(id => id != _current).ToList(); }
        }

        /// <summary>Everything visited, most recent first, including the current one.</summary>
        public IList<string> Visited { get { return _recent.ToList(); } }

        /// <summary>
        /// Opens a structure. The one being left becomes the Back target. Re-opening
        /// the structure already open does nothing, so clicking the current structure
        /// in the recents list cannot fill the history with repeats.
        /// </summary>
        public void Open(string structureId)
        {
            if (string.IsNullOrEmpty(structureId)) return;
            if (structureId == _current) { Touch(structureId); return; }

            if (_current != null) _back.Add(_current);
            _forward.Clear();
            _current = structureId;
            Touch(structureId);
        }

        /// <summary>Returns to the previous structure, or null when there is none.</summary>
        public string Back()
        {
            if (_back.Count == 0) return null;
            var target = _back[_back.Count - 1];
            _back.RemoveAt(_back.Count - 1);
            if (_current != null) _forward.Add(_current);
            _current = target;
            Touch(target);
            return target;
        }

        /// <summary>Undoes a Back, or null when there is nothing to go forward to.</summary>
        public string Forward()
        {
            if (_forward.Count == 0) return null;
            var target = _forward[_forward.Count - 1];
            _forward.RemoveAt(_forward.Count - 1);
            if (_current != null) _back.Add(_current);
            _current = target;
            Touch(target);
            return target;
        }

        /// <summary>Drops a structure that no longer exists from the history, so Back
        /// cannot land on a deleted record.</summary>
        public void Forget(string structureId)
        {
            if (string.IsNullOrEmpty(structureId)) return;
            _back.RemoveAll(id => id == structureId);
            _forward.RemoveAll(id => id == structureId);
            _recent.RemoveAll(id => id == structureId);
            if (_current == structureId) _current = null;
        }

        /// <summary>Drops everything the project no longer contains.</summary>
        public void Prune(UtilityProject project)
        {
            if (project == null) return;
            var known = new HashSet<string>(project.Structures.Select(s => s.Id));
            foreach (var id in _recent.Concat(_back).Concat(_forward).Distinct().ToList())
                if (!known.Contains(id)) Forget(id);
        }

        public void Clear()
        {
            _back.Clear();
            _forward.Clear();
            _recent.Clear();
            _current = null;
        }

        private void Touch(string structureId)
        {
            _recent.RemoveAll(id => id == structureId);
            _recent.Insert(0, structureId);
            while (_recent.Count > Math.Max(1, RecentLimit)) _recent.RemoveAt(_recent.Count - 1);
        }
    }
}
