using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace FieldCodes.Cad.Ui
{
    /// <summary>
    /// A searchable block box: the drawing's blocks, then the approved symbol library's (marked
    /// "(library)"), narrowed to the names containing what is typed. Anything can still be typed --
    /// a wildcard pattern for a symbol rule (SSV-SN*), or a block not brought in yet; the box never
    /// inserts or loads anything. <see cref="BlockName"/> is the name without the library mark.
    ///
    /// UNTESTED as UI.
    /// </summary>
    internal sealed class BlockChooser : ComboBox
    {
        public const string LibraryMark = "  (library)";

        private readonly List<string> _drawing = new List<string>();
        private readonly List<string> _library = new List<string>();

        public BlockChooser()
        {
            DropDownStyle = ComboBoxStyle.DropDown;
            MaxDropDownItems = 20;
            AutoCompleteMode = AutoCompleteMode.None;
        }

        /// <summary>The block names to offer: the drawing's, and the library's that the drawing lacks.</summary>
        public void SetBlocks(IEnumerable<string> drawing, IEnumerable<string> library)
        {
            _drawing.Clear();
            _drawing.AddRange((drawing ?? Enumerable.Empty<string>()).Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase));
            _library.Clear();
            _library.AddRange((library ?? Enumerable.Empty<string>()).Where(n => !string.IsNullOrWhiteSpace(n) && !InDrawing(n))
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase));
            var text = Text;
            Fill(null);
            Text = text;
        }

        /// <summary>A block just brought into the drawing moves from the library part to the drawing's.</summary>
        public void AddDrawingBlock(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || InDrawing(name)) return;
            _library.RemoveAll(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
            _drawing.Add(name);
            _drawing.Sort(StringComparer.OrdinalIgnoreCase);
            var text = Text;
            Fill(null);
            Text = text;
        }

        /// <summary>The name chosen or typed, without the library mark; empty for none.</summary>
        public string BlockName
        {
            get { return Strip(Text); }
            set { Text = value ?? string.Empty; }
        }

        public bool InDrawing(string name)
        {
            return !string.IsNullOrWhiteSpace(name) && _drawing.Contains(name.Trim(), StringComparer.OrdinalIgnoreCase);
        }

        public bool InLibrary(string name)
        {
            return !string.IsNullOrWhiteSpace(name) && _library.Contains(name.Trim(), StringComparer.OrdinalIgnoreCase);
        }

        public IList<string> DrawingBlocks { get { return _drawing; } }

        public static string Strip(string text)
        {
            var t = (text ?? string.Empty).Trim();
            var mark = LibraryMark.Trim();
            if (t.EndsWith(mark, StringComparison.OrdinalIgnoreCase)) t = t.Substring(0, t.Length - mark.Length).Trim();
            return t;
        }

        private void Fill(string filter)
        {
            var f = Strip(filter);
            BeginUpdate();
            try
            {
                Items.Clear();
                foreach (var n in _drawing.Where(n => f.Length == 0 || n.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0))
                    Items.Add(n);
                foreach (var n in _library.Where(n => f.Length == 0 || n.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0))
                    Items.Add(n + LibraryMark);
            }
            finally { EndUpdate(); }
        }

        /// <summary>Typing narrows the list to the names containing the text, kept open.</summary>
        protected override void OnTextUpdate(EventArgs e)
        {
            base.OnTextUpdate(e);
            var text = Text;
            var caret = SelectionStart;
            Fill(text);
            if (Items.Count > 0 && !DroppedDown)
            {
                DroppedDown = true;
                Cursor.Current = Cursors.Default;   // opening the list hides the pointer otherwise
            }
            // Opening the list or refilling it picks an item and overwrites what was typed.
            if (Text != text) Text = text;
            SelectionStart = Math.Min(caret, text.Length);
            SelectionLength = 0;
        }

        protected override void OnDropDownClosed(EventArgs e)
        {
            base.OnDropDownClosed(e);
            if (!IsHandleCreated) return;
            BeginInvoke(new Action(() =>
            {
                if (DroppedDown) return;
                var text = Text;
                Fill(null);
                if (Text != text) Text = text;
            }));
        }
    }
}
