using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using FieldCodes.Easements;

namespace FieldCodes.Cad.Ui
{
    /// <summary>
    /// The strip after trimming, before anything is drawn. Each piece the trim lines
    /// made can be clicked to keep or remove it; the title, area and any problems
    /// update as the drafter goes. Nothing reaches the drawing until "Draw easement".
    /// </summary>
    internal sealed class EasementPreviewForm : Form
    {
        private readonly EasementCommands.TrimPreview _preview;
        private readonly HashSet<int> _keep;
        private readonly TrimCanvas _canvas;
        private readonly TextBox _purpose;
        private readonly Label _title;
        private readonly Label _area;
        private readonly Label _pieces;
        private readonly Label _temporaryCaption;
        private readonly Label _temporary;
        private readonly Label _tiesCaption;
        private readonly Label _ties;
        private readonly Label _problem;
        private readonly Label _notes;
        private readonly Button _draw;

        public EasementPreviewForm(EasementCommands.TrimPreview preview)
        {
            _preview = preview;
            _keep = new HashSet<int>(preview.Keep ?? new List<int> { 1 });

            DipBuilderForm.UseTheme(ThemePreference.LoadDark());
            Text = "Strip Easement Preview";
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = false;
            MinimizeBox = false;
            BackColor = DipBuilderForm.Ground;
            ForeColor = DipBuilderForm.Ink;
            Font = DipBuilderForm.F(10.5f, false);
            var area = Screen.FromPoint(Cursor.Position).WorkingArea;
            ClientSize = new Size(Math.Min(1180, area.Width - 60), Math.Min(760, area.Height - 60));
            MinimumSize = new Size(Math.Min(820, area.Width), Math.Min(520, area.Height));

            // Header.
            var header = new Panel { Dock = DockStyle.Top, BackColor = DipBuilderForm.Surface, Padding = new Padding(20, 12, 20, 10) };
            var heading = new Label { AutoSize = true, Text = "Keep the pieces that make the easement", Font = DipBuilderForm.F(15f, true), ForeColor = DipBuilderForm.Ink };
            var guide = new Label
            {
                AutoSize = true, ForeColor = DipBuilderForm.Muted, Margin = new Padding(1, 4, 0, 0),
                Text = "Click a piece to keep or remove it. Scroll to zoom, drag to pan, double-click to fit."
            };
            var headerText = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Dock = DockStyle.Fill };
            headerText.Controls.Add(heading);
            headerText.Controls.Add(guide);
            header.Controls.Add(headerText);
            header.Height = heading.PreferredHeight + guide.PreferredHeight + header.Padding.Vertical + 8;
            header.Paint += (s, e) => { using (var pen = new Pen(DipBuilderForm.Rule)) e.Graphics.DrawLine(pen, 0, header.Height - 1, header.Width, header.Height - 1); };

            // Side panel: title, area, problems, buttons.
            var side = new Panel { Dock = DockStyle.Right, Width = 340, BackColor = DipBuilderForm.Surface, Padding = new Padding(18, 16, 18, 16) };
            side.Paint += (s, e) => { using (var pen = new Pen(DipBuilderForm.Rule)) e.Graphics.DrawLine(pen, 0, 0, 0, side.Height); };

            var stack = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
            var width = side.Width - side.Padding.Horizontal - 8;
            stack.Controls.Add(Caption("Purpose"));
            _purpose = new TextBox
            {
                Width = width, Text = preview.Purpose, CharacterCasing = CharacterCasing.Upper, BorderStyle = BorderStyle.FixedSingle,
                BackColor = DipBuilderForm.Dark ? DipBuilderForm.Calculated : Color.White, ForeColor = DipBuilderForm.Ink,
                Font = DipBuilderForm.F(11f, false), Margin = new Padding(0, 2, 0, 14)
            };
            _purpose.TextChanged += (s, e) => Recalculate(true);
            stack.Controls.Add(_purpose);

            stack.Controls.Add(Caption("Title"));
            _title = Value(width, DipBuilderForm.F(12f, true));
            stack.Controls.Add(_title);
            stack.Controls.Add(Caption("Kept area"));
            _area = Value(width, DipBuilderForm.F(12f, true));
            stack.Controls.Add(_area);
            _temporaryCaption = Caption("Temporary construction easement");
            stack.Controls.Add(_temporaryCaption);
            _temporary = Value(width, DipBuilderForm.F(11f, false));
            stack.Controls.Add(_temporary);
            _tiesCaption = Caption("Ties");
            stack.Controls.Add(_tiesCaption);
            _ties = Value(width, DipBuilderForm.F(10f, false));
            stack.Controls.Add(_ties);
            stack.Controls.Add(Caption("Pieces"));
            _pieces = Value(width, DipBuilderForm.F(10.5f, false));
            stack.Controls.Add(_pieces);

            _problem = Value(width, DipBuilderForm.F(10.5f, false));
            _problem.ForeColor = DipBuilderForm.Bad;
            stack.Controls.Add(_problem);

            _notes = Value(width, DipBuilderForm.F(9.5f, false));
            _notes.ForeColor = DipBuilderForm.Muted;
            stack.Controls.Add(_notes);

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 10, 0, 0) };
            _draw = Btn("Draw easement", true);
            _draw.Click += (s, e) => Accept();
            var cancel = Btn("Cancel", false);
            cancel.DialogResult = DialogResult.Cancel;
            buttons.Controls.Add(_draw);
            buttons.Controls.Add(cancel);
            AcceptButton = _draw;
            CancelButton = cancel;

            side.Controls.Add(stack);
            side.Controls.Add(buttons);

            _canvas = new TrimCanvas(preview, _keep) { Dock = DockStyle.Fill };
            _canvas.PieceClicked += number =>
            {
                if (!_keep.Remove(number)) _keep.Add(number);
                Recalculate(true);
            };

            Controls.Add(_canvas);
            Controls.Add(side);
            Controls.Add(header);
            Recalculate(true);
        }

        private static Label Caption(string text)
        {
            return new Label
            {
                AutoSize = true, Text = text.ToUpperInvariant(), ForeColor = DipBuilderForm.Muted,
                Font = DipBuilderForm.F(8.5f, false), Margin = new Padding(0, 0, 0, 2)
            };
        }

        private static Label Value(int width, Font font)
        {
            return new Label { AutoSize = true, MaximumSize = new Size(width, 0), Font = font, ForeColor = DipBuilderForm.Ink, Margin = new Padding(0, 0, 0, 14) };
        }

        private static Button Btn(string text, bool primary)
        {
            var b = new Button
            {
                Text = text, AutoSize = true, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand,
                BackColor = primary ? DipBuilderForm.Accent : DipBuilderForm.Surface, ForeColor = primary ? Color.White : DipBuilderForm.Ink,
                Font = DipBuilderForm.F(10f, false), Padding = new Padding(10, 3, 10, 3), Margin = new Padding(8, 0, 0, 0)
            };
            b.FlatAppearance.BorderColor = primary ? DipBuilderForm.Accent : DipBuilderForm.ButtonBorder;
            b.FlatAppearance.MouseOverBackColor = primary ? DipBuilderForm.AccentHover : DipBuilderForm.AccentSoft;
            return b;
        }

        private void Recalculate(bool geometry)
        {
            var purpose = string.IsNullOrWhiteSpace(_purpose.Text) ? _preview.Settings.DefaultPurpose : _purpose.Text.Trim();
            _title.Text = EasementAnnotation.Title(_preview.Width, purpose, _preview.Settings);
            if (!geometry) return;

            string failure;
            var merged = StripTrim.Merge(_preview.Split, _keep, _preview.Tolerance, out failure);
            var upf = _preview.UnitsPerFoot;
            var s = _preview.Settings;
            const string deg = "°";
            List<List<Course>> parts = null;
            if (merged != null)
            {
                var sqft = Math.Abs(Loops.SignedArea(merged)) / (upf * upf);
                _area.Text = string.Join("\n", EasementAnnotation.AreaLines(sqft, s, _purpose.Text).ToArray());
                parts = StripTrim.PartsInside(_preview.Route, merged, _preview.Tolerance);
                if (parts.Count == 0)
                    failure = "The easement line does not run through the kept pieces.";
            }
            else
                _area.Text = "-";

            // The temporary construction easement keeps what surrounds the kept pieces.
            _canvas.TemporaryKeep = new HashSet<int>();
            _temporaryCaption.Visible = _temporary.Visible = _preview.TemporarySplit != null;
            if (_preview.TemporarySplit != null)
            {
                P2? missing;
                var keepPoints = _preview.Split.Pieces.Where(p => _keep.Contains(p.Number)).Select(p => p.InsidePoint);
                _canvas.TemporaryKeep = new HashSet<int>(EasementCommands.PiecesAt(_preview.TemporarySplit, keepPoints, out missing));
                string temporaryFailure;
                var temporary = _canvas.TemporaryKeep.Count == 0 ? null
                    : StripTrim.Merge(_preview.TemporarySplit, _canvas.TemporaryKeep, _preview.Tolerance, out temporaryFailure);
                var title = EasementAnnotation.Title(_preview.TemporaryWidth, s.TemporaryPurpose, s);
                if (temporary != null)
                {
                    var sqft = Math.Abs(Loops.SignedArea(temporary)) / (upf * upf);
                    _temporary.Text = title + "\n" + string.Join("\n", EasementAnnotation.AreaLines(sqft, s, s.TemporaryPurpose).ToArray());
                }
                else
                {
                    _temporary.Text = title + "\n-";
                    if (failure == null && merged != null) failure = "The temporary construction easement could not be matched to the kept pieces.";
                }
            }

            // Ties from the Point of Commencement and to the terminus corner, as they will be described.
            var ties = new List<string>();
            _canvas.Beginning = _canvas.Terminus = null;
            if (parts != null && parts.Count > 0)
            {
                var beginning = parts[0][0].Start;
                var last = parts[parts.Count - 1];
                var terminus = last[last.Count - 1].End;
                _canvas.Beginning = beginning;
                _canvas.Terminus = terminus;
                if (_preview.Commencement.HasValue)
                    ties.Add("Commencement to beginning:\n  " + EasementAnnotation.LineText(EasementAnnotation.Describe(Course.Line(_preview.Commencement.Value, beginning)), s, deg));
                if (_preview.TerminusCorner.HasValue)
                    ties.Add("Terminus to corner:\n  " + EasementAnnotation.LineText(EasementAnnotation.Describe(Course.Line(terminus, _preview.TerminusCorner.Value)), s, deg));
            }
            _ties.Text = string.Join("\n", ties.ToArray());
            _tiesCaption.Visible = _ties.Visible = ties.Count > 0;

            var total = _preview.Split.Pieces.Count;
            _pieces.Text = total == 1
                ? "One piece -- the trim lines do not divide the strip."
                : string.Format("{0} of {1} kept. Blue pieces are kept; outlined pieces are left out.", _keep.Count, total);
            _problem.Text = failure ?? string.Empty;
            _problem.Visible = failure != null;
            _notes.Text = _preview.Notes.Count == 0 ? string.Empty : "Notes:\n" + string.Join("\n", _preview.Notes.Select(n => "- " + n).ToArray());
            _draw.Enabled = failure == null;
            _canvas.Invalidate();
        }

        private void Accept()
        {
            _preview.Keep = _keep.OrderBy(k => k).ToList();
            _preview.Purpose = string.IsNullOrWhiteSpace(_purpose.Text) ? _preview.Settings.DefaultPurpose : _purpose.Text.Trim().ToUpperInvariant();
            DialogResult = DialogResult.OK;
            Close();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                var on = DipBuilderForm.Dark ? 1 : 0;
                DwmSetWindowAttribute(Handle, 20, ref on, sizeof(int));
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    }

    /// <summary>The trimmed strip drawn to scale: pieces, trim lines, the easement line
    /// and its angle points. North is up.</summary>
    internal sealed class TrimCanvas : Control
    {
        private readonly EasementCommands.TrimPreview _preview;
        private readonly HashSet<int> _keep;
        private double _scale = 1;
        private P2 _center;
        private int _hover;
        private Point _dragFrom;
        private bool _dragging;
        private bool _dragged;

        public event Action<int> PieceClicked;

        /// <summary>Pieces of the temporary construction easement that are kept.</summary>
        public HashSet<int> TemporaryKeep { get; set; }
        public P2? Beginning { get; set; }
        public P2? Terminus { get; set; }

        // Markup colours: the easement line red and angle points green, as on a sketch.
        private static Color RouteColor { get { return DipBuilderForm.Dark ? Color.FromArgb(240, 96, 88) : Color.FromArgb(200, 40, 32); } }
        private static Color AngleColor { get { return DipBuilderForm.Dark ? Color.FromArgb(96, 200, 120) : Color.FromArgb(24, 130, 60); } }
        private static Color TrimColor { get { return DipBuilderForm.Dark ? Color.FromArgb(176, 140, 240) : Color.FromArgb(104, 64, 170); } }

        public TrimCanvas(EasementCommands.TrimPreview preview, HashSet<int> keep)
        {
            _preview = preview;
            _keep = keep;
            DoubleBuffered = true;
            ResizeRedraw = true;
            BackColor = DipBuilderForm.Surface;
            Font = DipBuilderForm.F(9.5f, false);
            Cursor = Cursors.Hand;
            Fit();
        }

        private IEnumerable<P2> ExtentPoints()
        {
            foreach (var piece in _preview.Split.Pieces)
                foreach (var c in piece.Loop) { yield return c.Start; yield return c.PointAt(c.Length / 2); }
            foreach (var c in _preview.Route) { yield return c.Start; yield return c.End; }
            if (_preview.TemporarySplit != null)
                foreach (var piece in _preview.TemporarySplit.Pieces)
                    foreach (var c in piece.Loop) yield return c.Start;
            if (_preview.Commencement.HasValue) yield return _preview.Commencement.Value;
            if (_preview.TerminusCorner.HasValue) yield return _preview.TerminusCorner.Value;
        }

        /// <summary>The band along the bottom kept for the legend.</summary>
        private int LegendBand { get { return Font.Height + 22; } }

        private Rectangle View { get { return new Rectangle(0, 0, Width, Math.Max(1, Height - LegendBand)); } }

        /// <summary>Fits the strip in the view with a margin. Until the drafter zooms or
        /// pans, the view refits whenever the window is resized.</summary>
        private void Fit()
        {
            _userMoved = false;
            var points = ExtentPoints().ToList();
            if (points.Count == 0) return;
            double minX = points.Min(p => p.X), maxX = points.Max(p => p.X), minY = points.Min(p => p.Y), maxY = points.Max(p => p.Y);
            _center = new P2((minX + maxX) / 2, (minY + maxY) / 2);
            const int margin = 48;
            var w = Math.Max(1, View.Width - 2 * margin);
            var h = Math.Max(1, View.Height - 2 * margin);
            _scale = Math.Min(w / Math.Max(1e-6, maxX - minX), h / Math.Max(1e-6, maxY - minY));
            Invalidate();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (Width > 0 && Height > 0 && !_userMoved) Fit();
        }

        private bool _userMoved;

        private PointF ToScreen(P2 p)
        {
            return new PointF((float)(View.Width / 2.0 + (p.X - _center.X) * _scale), (float)(View.Height / 2.0 - (p.Y - _center.Y) * _scale));
        }

        private P2 World(Point p)
        {
            return new P2(_center.X + (p.X - View.Width / 2.0) / _scale, _center.Y - (p.Y - View.Height / 2.0) / _scale);
        }

        private PointF[] Path(IEnumerable<Course> courses)
        {
            var points = new List<PointF>();
            foreach (var c in courses)
            {
                var steps = c.Kind == CourseKind.Arc ? Math.Max(2, (int)Math.Ceiling(c.Sweep / (Math.PI / 90))) : 1;
                if (points.Count == 0) points.Add(ToScreen(c.Start));
                for (var i = 1; i <= steps; i++) points.Add(ToScreen(c.PointAt(c.Length * i / steps)));
            }
            return points.ToArray();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);
            g.SetClip(View);

            // Trim lines first, running past the strip so their direction reads.
            using (var pen = new Pen(TrimColor, 2f))
                foreach (var trim in _preview.Trims)
                {
                    var path = Path(trim);
                    if (path.Length > 1) g.DrawLines(pen, path);
                }

            foreach (var piece in _preview.Split.Pieces)
            {
                var path = Path(piece.Loop);
                if (path.Length < 3) continue;
                var kept = _keep.Contains(piece.Number);
                var hover = piece.Number == _hover;
                using (var fill = new SolidBrush(Color.FromArgb(kept ? (hover ? 150 : 110) : (hover ? 60 : 0), DipBuilderForm.Accent)))
                    g.FillPolygon(fill, path);
                using (var pen = new Pen(kept ? DipBuilderForm.Accent : DipBuilderForm.Muted, kept ? 2.2f : 1.4f))
                {
                    if (!kept) pen.DashStyle = DashStyle.Dash;
                    g.DrawPolygon(pen, path);
                }
            }

            // The temporary construction easement: a dashed outline around the kept pieces.
            if (_preview.TemporarySplit != null && TemporaryKeep != null)
                foreach (var piece in _preview.TemporarySplit.Pieces.Where(p => TemporaryKeep.Contains(p.Number)))
                {
                    var path = Path(piece.Loop);
                    if (path.Length < 3) continue;
                    using (var fill = new SolidBrush(Color.FromArgb(35, DipBuilderForm.Accent))) g.FillPolygon(fill, path);
                    using (var pen = new Pen(DipBuilderForm.Accent, 1.4f) { DashStyle = DashStyle.Dash }) g.DrawPolygon(pen, path);
                }

            // Ties: dashed lines to the Point of Commencement and the terminus corner.
            using (var tie = new Pen(DipBuilderForm.Ink, 1.2f) { DashStyle = DashStyle.Dot })
            using (var mark = new SolidBrush(DipBuilderForm.Ink))
            {
                if (_preview.Commencement.HasValue)
                {
                    var c = ToScreen(_preview.Commencement.Value);
                    if (Beginning.HasValue) g.DrawLine(tie, c, ToScreen(Beginning.Value));
                    g.FillRectangle(mark, c.X - 5, c.Y - 5, 10, 10);
                    TextRenderer.DrawText(g, "POC", Font, new Point((int)c.X + 8, (int)c.Y - Font.Height), DipBuilderForm.Ink);
                }
                if (_preview.TerminusCorner.HasValue)
                {
                    var c = ToScreen(_preview.TerminusCorner.Value);
                    if (Terminus.HasValue) g.DrawLine(tie, c, ToScreen(Terminus.Value));
                    g.FillRectangle(mark, c.X - 5, c.Y - 5, 10, 10);
                }
                if (Beginning.HasValue)
                {
                    var b = ToScreen(Beginning.Value);
                    TextRenderer.DrawText(g, "POB", Font, new Point((int)b.X + 8, (int)b.Y + 2), DipBuilderForm.Ink);
                }
                if (Terminus.HasValue)
                {
                    var end = ToScreen(Terminus.Value);
                    TextRenderer.DrawText(g, "TERMINUS", Font, new Point((int)end.X + 8, (int)end.Y + 2), DipBuilderForm.Ink);
                }
            }

            using (var pen = new Pen(RouteColor, 1.8f) { DashStyle = DashStyle.DashDot })
            {
                var route = Path(_preview.Route);
                if (route.Length > 1) g.DrawLines(pen, route);
            }
            using (var dot = new SolidBrush(AngleColor))
                foreach (var p in _preview.AnglePoints)
                {
                    var s = ToScreen(p);
                    g.FillEllipse(dot, s.X - 5, s.Y - 5, 10, 10);
                }

            // Piece numbers, only when there is a choice to make.
            if (_preview.Split.Pieces.Count > 1)
                foreach (var piece in _preview.Split.Pieces)
                {
                    // Slivers too small to see at this zoom get no badge; zoom in to pick them.
                    if (piece.Area * _scale * _scale < 900 && !_keep.Contains(piece.Number)) continue;
                    var s = ToScreen(piece.InsidePoint);
                    var kept = _keep.Contains(piece.Number);
                    var text = piece.Number.ToString();
                    var size = TextRenderer.MeasureText(text, Font);
                    var d = Math.Max(size.Width, size.Height) + 6;
                    var r = new Rectangle((int)(s.X - d / 2f), (int)(s.Y - d / 2f), d, d);
                    using (var back = new SolidBrush(kept ? DipBuilderForm.Accent : DipBuilderForm.Surface)) g.FillEllipse(back, r);
                    using (var pen = new Pen(kept ? DipBuilderForm.Accent : DipBuilderForm.Muted, 1.2f)) g.DrawEllipse(pen, r);
                    TextRenderer.DrawText(g, text, Font, r, kept ? Color.White : DipBuilderForm.Ink,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }

            DrawNorth(g);
            g.ResetClip();
            using (var band = new SolidBrush(DipBuilderForm.Surface)) g.FillRectangle(band, 0, View.Height, Width, LegendBand);
            using (var rule = new Pen(DipBuilderForm.Rule)) g.DrawLine(rule, 0, View.Height, Width, View.Height);
            DrawLegend(g);
        }

        private void DrawNorth(Graphics g)
        {
            var cx = Width - 26f;
            const float top = 14f;
            using (var ink = new SolidBrush(DipBuilderForm.Ink))
            using (var surface = new SolidBrush(DipBuilderForm.Surface))
            using (var pen = new Pen(DipBuilderForm.Ink, 1.2f))
            {
                var tip = new PointF(cx, top);
                var notch = new PointF(cx, top + 13);
                g.FillPolygon(ink, new[] { tip, new PointF(cx - 7, top + 18), notch });
                g.FillPolygon(surface, new[] { tip, new PointF(cx + 7, top + 18), notch });
                g.DrawPolygon(pen, new[] { tip, new PointF(cx + 7, top + 18), notch, new PointF(cx - 7, top + 18) });
            }
            var size = TextRenderer.MeasureText("N", Font);
            TextRenderer.DrawText(g, "N", Font, new Point((int)cx - size.Width / 2, (int)top + 20), DipBuilderForm.Ink);
        }

        private void DrawLegend(Graphics g)
        {
            var y = View.Height + LegendBand / 2;
            var x = 16;
            Action<Action<int>, string> item = (mark, text) =>
            {
                mark(x);
                x += 28;
                TextRenderer.DrawText(g, text, Font, new Point(x, y - Font.Height / 2), DipBuilderForm.Muted);
                x += TextRenderer.MeasureText(text, Font).Width + 18;
            };
            item(at => { using (var p = new Pen(RouteColor, 1.8f) { DashStyle = DashStyle.DashDot }) g.DrawLine(p, at, y, at + 22, y); }, "easement line");
            item(at => { using (var b = new SolidBrush(AngleColor)) g.FillEllipse(b, at + 6, y - 5, 10, 10); }, "angle points");
            item(at => { using (var p = new Pen(TrimColor, 2f)) g.DrawLine(p, at, y, at + 22, y); }, "trim lines");
            item(at => { using (var b = new SolidBrush(Color.FromArgb(110, DipBuilderForm.Accent))) g.FillRectangle(b, at + 2, y - 7, 18, 14); }, "kept");
            item(at => { using (var p = new Pen(DipBuilderForm.Muted, 1.4f) { DashStyle = DashStyle.Dash }) g.DrawRectangle(p, at + 2, y - 7, 18, 14); }, "left out");
            if (_preview.TemporarySplit != null)
                item(at => { using (var p = new Pen(DipBuilderForm.Accent, 1.4f) { DashStyle = DashStyle.Dash }) g.DrawRectangle(p, at + 2, y - 7, 18, 14); }, "temporary");
        }

        private int PieceAt(Point p)
        {
            if (p.Y >= View.Height) return 0;
            var piece = _preview.Split.PieceAt(World(p));
            return piece == null ? 0 : piece.Number;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            _dragFrom = e.Location;
            _dragging = true;
            _dragged = false;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragging && e.Button != MouseButtons.None)
            {
                var dx = e.X - _dragFrom.X;
                var dy = e.Y - _dragFrom.Y;
                if (_dragged || Math.Abs(dx) + Math.Abs(dy) > 4)
                {
                    _dragged = true;
                    _userMoved = true;
                    _center = new P2(_center.X - dx / _scale, _center.Y + dy / _scale);
                    _dragFrom = e.Location;
                    Cursor = Cursors.SizeAll;
                    Invalidate();
                }
                return;
            }
            var hover = PieceAt(e.Location);
            if (hover != _hover) { _hover = hover; Invalidate(); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _dragging = false;
            Cursor = Cursors.Hand;
            if (_dragged || e.Button != MouseButtons.Left) return;
            var number = PieceAt(e.Location);
            if (number > 0 && _preview.Split.Pieces.Count > 1 && PieceClicked != null) PieceClicked(number);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hover != 0) { _hover = 0; Invalidate(); }
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            Fit();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            _userMoved = true;
            var before = World(e.Location);
            _scale *= e.Delta > 0 ? 1.25 : 0.8;
            var after = World(e.Location);
            _center = new P2(_center.X + before.X - after.X, _center.Y + before.Y - after.Y);
            Invalidate();
        }
    }
}
