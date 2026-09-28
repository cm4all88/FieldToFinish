using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace FieldCodes.Cad.Ui
{
    /// <summary>
    /// One pipe at the open structure, readable at a glance -- "N/NW 12" RCP IE 6.41" -- with where it goes, whether
    /// it is drawn, its slope and any open question underneath, and the next things to do with it as buttons.
    /// </summary>
    internal sealed class PipeCard : Panel
    {
        public string PipeId { get; private set; }
        public readonly Label Headline;
        public readonly Label Detail;
        public readonly FlowLayoutPanel Actions;
        private readonly FlowLayoutPanel _more;
        private bool _selected;

        /// <summary>Raised when the card itself (not one of its buttons) is clicked.</summary>
        public event EventHandler Picked;

        public PipeCard(string pipeId)
        {
            PipeId = pipeId;
            Tag = pipeId;
            BackColor = DipBuilderForm.Surface;
            Padding = new Padding(12, 6, 12, 4);
            Margin = Padding.Empty;
            Cursor = Cursors.Hand;
            Headline = new Label { Dock = DockStyle.Top, AutoSize = false, Font = new Font("Consolas", 12f, FontStyle.Bold), ForeColor = DipBuilderForm.Ink, UseMnemonic = false };
            Detail = new Label { Dock = DockStyle.Top, AutoSize = false, Font = DipBuilderForm.F(9.75f, false), ForeColor = DipBuilderForm.Muted, UseMnemonic = false };
            Actions = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = false, WrapContents = true, BackColor = Color.Transparent, Padding = new Padding(0, 4, 0, 0) };
            _more = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = false, WrapContents = true, BackColor = Color.Transparent, Visible = false, Padding = new Padding(0, 0, 0, 2) };
            Controls.Add(_more);
            Controls.Add(Actions);
            Controls.Add(Detail);
            Controls.Add(Headline);
            foreach (var c in new Control[] { this, Headline, Detail, Actions })
                c.Click += (s, e) => { if (Picked != null) Picked(this, EventArgs.Empty); };
            Paint += (s, e) =>
            {
                using (var pen = new Pen(_selected ? DipBuilderForm.Accent : DipBuilderForm.Rule, _selected ? 2f : 1f))
                    e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
                if (_selected)
                    using (var bar = new SolidBrush(DipBuilderForm.Accent)) e.Graphics.FillRectangle(bar, 0, 0, 4, Height);
            };
        }

        public bool Selected
        {
            get { return _selected; }
            set { _selected = value; BackColor = value ? DipBuilderForm.AccentSoft : DipBuilderForm.Surface; Invalidate(); }
        }

        /// <summary>A small flat action button on the card.</summary>
        public Button Action(string text, EventHandler click, bool enabled = true, bool primary = false)
        {
            var b = new Button
            {
                Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlatStyle = FlatStyle.Flat,
                Font = DipBuilderForm.F(9f, true), Margin = new Padding(0, 0, 5, 3), Padding = new Padding(2, 0, 2, 0),
                BackColor = primary ? DipBuilderForm.Accent : DipBuilderForm.Surface, ForeColor = primary ? Color.White : DipBuilderForm.Ink,
                Cursor = Cursors.Hand, Enabled = enabled, UseMnemonic = false
            };
            b.FlatAppearance.BorderColor = primary ? DipBuilderForm.Accent : DipBuilderForm.ButtonBorder;
            b.FlatAppearance.MouseOverBackColor = primary ? DipBuilderForm.AccentHover : DipBuilderForm.AccentSoft;
            if (click != null) b.Click += click;
            Actions.Controls.Add(b);
            return b;
        }

        /// <summary>
        /// The actions a normal structure never needs, behind one button: picking a different
        /// structure, leaving a pipe unresolved, marking it outside the survey, redrawing,
        /// deleting. They open in the card itself rather than a popup, so they can be read and
        /// reached with the keyboard -- one click away, never in the way.
        /// </summary>
        public Button More(string text, IEnumerable<KeyValuePair<string, EventHandler>> items)
        {
            var list = items.Where(i => i.Value != null).ToList();
            if (list.Count == 0) return null;

            foreach (var item in list)
            {
                var click = item.Value;
                var extra = new Button
                {
                    Text = item.Key, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlatStyle = FlatStyle.Flat,
                    Font = DipBuilderForm.F(9f, false), Margin = new Padding(0, 0, 5, 3), Padding = new Padding(2, 0, 2, 0),
                    BackColor = DipBuilderForm.Surface, ForeColor = DipBuilderForm.Ink, Cursor = Cursors.Hand, UseMnemonic = false
                };
                extra.FlatAppearance.BorderColor = DipBuilderForm.ButtonBorder;
                extra.FlatAppearance.MouseOverBackColor = DipBuilderForm.AccentSoft;
                extra.Click += (s, e) => click(this, EventArgs.Empty);
                _more.Controls.Add(extra);
            }

            var b = Action(text, null);
            b.Click += (s, e) =>
            {
                MoreOpen = !MoreOpen;
                b.Text = MoreOpen ? text + " \u25b4" : text;
                var list2 = Parent as IFitsWidth;
                if (list2 != null) list2.FitWidth(Width);
                Parent?.PerformLayout();
            };
            return b;
        }

        /// <summary>Whether the extra actions are showing.</summary>
        public bool MoreOpen
        {
            get { return _more.Visible; }
            set { _more.Visible = value; }
        }

        /// <summary>The extra actions, for a test to reach without opening them.</summary>
        public IEnumerable<Button> MoreActions { get { return _more.Controls.OfType<Button>(); } }

        public int FitWidth(int width)
        {
            Width = width;
            var inner = Math.Max(100, width - Padding.Horizontal);
            Headline.Height = TextRenderer.MeasureText(Headline.Text.Length == 0 ? "X" : Headline.Text, Headline.Font, new Size(inner, 0), TextFormatFlags.WordBreak).Height + 2;
            Detail.Height = Detail.Text.Length == 0 ? 0 : TextRenderer.MeasureText(Detail.Text, Detail.Font, new Size(inner, 0), TextFormatFlags.WordBreak).Height + 2;
            Actions.Height = Actions.GetPreferredSize(new Size(inner, 0)).Height;
            _more.Height = _more.Visible ? _more.GetPreferredSize(new Size(inner, 0)).Height : 0;
            Height = Padding.Vertical + Headline.Height + Detail.Height + Actions.Height + _more.Height;
            return Height;
        }
    }

    /// <summary>The open structure's pipe cards, stacked, with a line for each pipe known to run in from elsewhere.</summary>
    internal sealed class PipeCardList : Panel, IFitsWidth
    {
        private readonly List<Control> _rows = new List<Control>();

        public PipeCardList()
        {
            BackColor = DipBuilderForm.Surface;
            Margin = Padding.Empty;
        }

        public IEnumerable<PipeCard> Cards { get { return _rows.OfType<PipeCard>(); } }

        public void SetRows(IEnumerable<Control> rows)
        {
            SuspendLayout();
            foreach (var c in _rows) { Controls.Remove(c); c.Dispose(); }
            _rows.Clear();
            _rows.AddRange(rows);
            foreach (var c in _rows) Controls.Add(c);
            ResumeLayout();
            FitWidth(Width);
        }

        public void FitWidth(int width)
        {
            if (width <= 0) return;
            var y = 0;
            foreach (var c in _rows)
            {
                c.Location = new Point(0, y);
                var card = c as PipeCard;
                int h;
                if (card != null) h = card.FitWidth(width);
                else
                {
                    c.Width = width;
                    var flow = c as FlowLayoutPanel;
                    h = flow != null ? flow.GetPreferredSize(new Size(width, 0)).Height : c.Height;
                    c.Height = h;
                }
                y += h + 6;
            }
            var height = Math.Max(0, y);
            if (Height != height) Height = height;
        }
    }
}
