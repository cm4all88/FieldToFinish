using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace FieldCodes.Cad.Ui
{
    /// <summary>
    /// A box with a short list of the usual values and a "More..." entry that opens the rest. Anything can still be
    /// typed: the lists are suggestions, never a standard the field value has to match.
    /// </summary>
    internal class PickCombo : ComboBox
    {
        public const string MoreEntry = "More...";

        private IList<string> _common = new string[0];
        private IList<string> _more = new string[0];
        private bool _expanded;
        private string _beforeMore = string.Empty;

        /// <summary>Raised when a value is chosen from the list (not while typing).</summary>
        public event EventHandler Picked;

        public PickCombo()
        {
            DropDownStyle = ComboBoxStyle.DropDown;
            MaxDropDownItems = 16;
        }

        public void SetChoices(IEnumerable<string> common, IEnumerable<string> more)
        {
            _common = (common ?? Enumerable.Empty<string>()).Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
            _more = (more ?? Enumerable.Empty<string>()).Where(v => !string.IsNullOrWhiteSpace(v) && !_common.Contains(v)).ToList();
            Fill(false);
        }

        public bool HasChoices { get { return _common.Count + _more.Count > 0; } }

        private void Fill(bool expanded)
        {
            var text = Text;
            _expanded = expanded;
            BeginUpdate();
            Items.Clear();
            foreach (var v in _common) Items.Add(v);
            if (expanded) foreach (var v in _more) Items.Add(v);
            else if (_more.Count > 0) Items.Add(MoreEntry);
            EndUpdate();
            Text = text;
        }

        protected override void OnDropDown(EventArgs e)
        {
            _beforeMore = Text;
            base.OnDropDown(e);
        }

        protected override void OnDropDownClosed(EventArgs e)
        {
            base.OnDropDownClosed(e);
            // The short list again next time; the long one was only for this pick.
            if (_expanded && IsHandleCreated) BeginInvoke(new Action(() => { if (!DroppedDown) Fill(false); }));
        }

        protected override void OnSelectionChangeCommitted(EventArgs e)
        {
            if (Convert.ToString(SelectedItem) == MoreEntry)
            {
                var keep = _beforeMore;
                BeginInvoke(new Action(() =>
                {
                    Fill(true);
                    Text = keep;
                    DroppedDown = true;
                }));
                return;
            }
            base.OnSelectionChangeCommitted(e);
            if (Picked != null) BeginInvoke(new Action(() => Picked(this, EventArgs.Empty)));
        }

        protected override void OnTextChanged(EventArgs e)
        {
            // The "More..." entry is a button, never a value.
            if (Text == MoreEntry) return;
            base.OnTextChanged(e);
        }
    }
}
