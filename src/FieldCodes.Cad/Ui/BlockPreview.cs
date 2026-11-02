using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using Autodesk.AutoCAD.DatabaseServices;
using FieldCodes.Easements;
using FieldCodes.Geometry;
using FieldCodes.Orient;
using FieldCodes.Symbols;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace FieldCodes.Cad.Ui
{
    /// <summary>
    /// A block definition drawn small, turned the way a marker rotation would turn it: the
    /// insertion point marked (red), the block's native zero as an arrow (blue; two heads for a
    /// symbol that repeats every 180 degrees), and a sample facing (orange, dashed) the symbol is
    /// turned to. Drag the "Test facing" slider to see the rotation FTFPOINTS / FTFORIENT would
    /// give for that azimuth (<see cref="SymbolRotation.MarkerDegrees"/>). With
    /// <see cref="PickReferenceOnClick"/> on, a click sets the native zero to the direction clicked.
    ///
    /// Simplified: lines, polylines (bulges as short chords), arcs, circles, 2D solids and one level
    /// of nested blocks are drawn; text, hatches and the rest are left out and the preview says so.
    ///
    /// Use: <see cref="ShowBlock"/> (a block of a drawing) or <see cref="ShowSketch"/> (one read
    /// elsewhere, such as the symbol library), then <see cref="ReferenceDegrees"/> /
    /// <see cref="Period"/> (or <see cref="ApplySymbolRule"/>), and either <see cref="TestAzimuth"/>
    /// (the rotation follows it) or <see cref="Rotation"/> with <see cref="RotationFollowsAzimuth"/> off.
    ///
    /// UNTESTED as UI; the fit, rotation and arrow arithmetic is unit tested (PreviewFrame, SymbolPreview).
    /// </summary>
    public sealed class BlockPreview : UserControl
    {
        private const float FitMargin = 16f;

        private readonly Canvas _canvas;
        private readonly FlowLayoutPanel _strip;
        private readonly TrackBar _slider;
        private readonly NumericUpDown _azimuthBox;
        private readonly Label _readout;

        private BlockSketch _sketch;
        private string _message = "(no block)";
        private string _caption;
        private double _reference;
        private double _period = 360;
        private double _azimuth = 45;
        private double _rotation;
        private bool _follow = true;
        private bool _syncing;

        /// <summary>Raised when the sample facing changes (slider, box or <see cref="TestAzimuth"/>).</summary>
        public event EventHandler TestAzimuthChanged;

        /// <summary>Raised when a click on the preview set <see cref="ReferenceDegrees"/>.</summary>
        public event EventHandler ReferencePicked;

        public BlockPreview()
        {
            _canvas = new Canvas { Dock = DockStyle.Fill, BackColor = SystemColors.Window };
            _canvas.Paint += (s, e) => PaintPreview(e.Graphics, _canvas.ClientSize);
            _canvas.MouseClick += CanvasClicked;

            _strip = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, WrapContents = true, Padding = new Padding(0, 2, 0, 0) };
            var caption = new Label { Text = "Test facing (azimuth)", AutoSize = true, Margin = new Padding(0, 9, 4, 0) };
            _slider = new TrackBar
            {
                Minimum = 0, Maximum = 359, TickFrequency = 45, SmallChange = 1, LargeChange = 15,
                Width = 150, AutoSize = false, Height = 30, Value = 45, Tag = SettingsPanel.ViewOnly
            };
            _azimuthBox = new NumericUpDown
            {
                Minimum = 0, Maximum = 359.9m, DecimalPlaces = 1, Increment = 15, Width = 64, Value = 45,
                Margin = new Padding(4, 6, 0, 0), Tag = SettingsPanel.ViewOnly
            };
            _readout = new Label { AutoSize = true, MaximumSize = new Size(420, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(0, 2, 0, 2) };
            _slider.ValueChanged += (s, e) => { if (!_syncing) SetAzimuth(_slider.Value, true); };
            _azimuthBox.ValueChanged += (s, e) => { if (!_syncing) SetAzimuth((double)_azimuthBox.Value, true); };
            _strip.Controls.Add(caption);
            _strip.Controls.Add(_slider);
            _strip.Controls.Add(_azimuthBox);
            _strip.SetFlowBreak(_azimuthBox, true);
            _strip.Controls.Add(_readout);

            // Fill first, then the strip: the last control added is docked first.
            Controls.Add(_canvas);
            Controls.Add(_strip);
            Size = new Size(300, 300);
            MinimumSize = new Size(160, 160);
            Recompute();
        }

        // ------------------------------------------------------------------ API

        /// <summary>The block shown (its name), or null.</summary>
        public string BlockName { get { return _sketch != null ? _sketch.Name : null; } }

        /// <summary>What is shown, as read; null when a message is shown instead.</summary>
        public BlockSketch Sketch { get { return _sketch; } }

        /// <summary>The block's native zero: the AutoCAD direction (degrees, 0 east, 90 north) its
        /// meaningful direction points at rotation 0.</summary>
        public double ReferenceDegrees
        {
            get { return _reference; }
            set { _reference = Clamp360(value); Recompute(); }
        }

        /// <summary>How often the symbol repeats: 360 one direction, 180 an axis, 0 never turned.</summary>
        public double Period
        {
            get { return _period; }
            set { _period = value; Recompute(); }
        }

        /// <summary>The sample facing, survey azimuth (clockwise from north). Setting it turns the
        /// symbol (when <see cref="RotationFollowsAzimuth"/>) and moves the slider.</summary>
        public double TestAzimuth
        {
            get { return _azimuth; }
            set { SetAzimuth(value, false); }
        }

        /// <summary>The rotation the block is drawn at, AutoCAD degrees. Worked out from the test
        /// azimuth while <see cref="RotationFollowsAzimuth"/> is on; set it with that off to show a
        /// rotation the host works out itself (an inserted block's rotation method).</summary>
        public double Rotation
        {
            get { return _rotation; }
            set { _rotation = value; Recompute(); }
        }

        /// <summary>On (the default): the rotation is the marker rotation for the test azimuth.</summary>
        public bool RotationFollowsAzimuth
        {
            get { return _follow; }
            set { _follow = value; Recompute(); }
        }

        /// <summary>Shows or hides the test-facing slider and its readout.</summary>
        public bool ShowAzimuthControls
        {
            get { return _strip.Visible; }
            set { _strip.Visible = value; }
        }

        /// <summary>When on, a click on the preview sets the native zero to the direction clicked
        /// (whole degrees; within 3 degrees of a multiple of 45, that multiple).</summary>
        public bool PickReferenceOnClick { get; set; }

        /// <summary>A line drawn under the block name (what the rotation means for this use).</summary>
        public string Caption
        {
            get { return _caption; }
            set { _caption = value; _canvas.Invalidate(); }
        }

        /// <summary>Native zero and period from a symbol rule (none: zero 0, one direction).</summary>
        public void ApplySymbolRule(SymbolRule rule)
        {
            _reference = rule != null ? Clamp360(rule.ReferenceCadDegrees) : 0.0;
            _period = rule != null ? rule.Period : 360.0;
            Recompute();
        }

        /// <summary>Reads and shows a block of <paramref name="db"/> (the active drawing's
        /// database is locked while it is read). A name the drawing does not have shows a note.</summary>
        public void ShowBlock(Database db, string blockName)
        {
            if (db == null || string.IsNullOrWhiteSpace(blockName)) { ShowMessage("(no block)"); return; }
            BlockSketch sketch = null;
            string problem = null;
            try
            {
                var doc = AcadApp.DocumentManager.MdiActiveDocument;
                if (doc != null && doc.Database == db)
                    using (doc.LockDocument()) sketch = BlockSketchReader.Read(db, blockName);
                else
                    sketch = BlockSketchReader.Read(db, blockName);
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex) { problem = "\"" + blockName + "\" could not be read: " + ex.Message; }
            if (sketch == null) { ShowMessage(problem ?? "\"" + blockName.Trim() + "\" is not in this drawing."); return; }
            ShowSketch(sketch);
        }

        /// <summary>Shows a sketch read elsewhere (the symbol library, a test).</summary>
        public void ShowSketch(BlockSketch sketch)
        {
            _sketch = sketch;
            _message = sketch == null ? "(no block)" : null;
            Recompute();
        }

        /// <summary>Shows a note instead of a block.</summary>
        public void ShowMessage(string message)
        {
            _sketch = null;
            _message = message ?? string.Empty;
            Recompute();
        }

        // ------------------------------------------------------------------ state

        private void SetAzimuth(double azimuth, bool fromControls)
        {
            var a = Angles.NormalizeDegrees(azimuth);
            var changed = Math.Abs(a - _azimuth) > 1e-9;
            _azimuth = a;
            if (!fromControls || changed) SyncControls();
            Recompute();
            if (changed && TestAzimuthChanged != null) TestAzimuthChanged(this, EventArgs.Empty);
        }

        private void SyncControls()
        {
            _syncing = true;
            try
            {
                var whole = (int)Math.Round(_azimuth) % 360;
                if (_slider.Value != whole) _slider.Value = whole;
                var boxed = (decimal)Math.Round(_azimuth, 1);
                if (boxed > _azimuthBox.Maximum) boxed = 0;
                if (_azimuthBox.Value != boxed) _azimuthBox.Value = boxed;
            }
            finally { _syncing = false; }
        }

        private void Recompute()
        {
            if (_follow) _rotation = SymbolPreview.MarkerRotation(_reference, _period, _azimuth);
            _readout.Text = _follow
                ? SymbolPreview.Describe(_reference, _period, _azimuth)
                : string.Format(CultureInfo.InvariantCulture, "Facing azimuth {0:0.#}: block rotation {1:0.#}.", _azimuth, Angles.NormalizeDegrees(_rotation));
            _canvas.Invalidate();
        }

        private static double Clamp360(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return 0;
            return v >= 360 ? 360 : (v < 0 ? Angles.NormalizeDegrees(v) : v);
        }

        // ------------------------------------------------------------------ input

        private void CanvasClicked(object sender, MouseEventArgs e)
        {
            if (!PickReferenceOnClick || _sketch == null || e.Button != MouseButtons.Left) return;
            var frame = Frame(_canvas.ClientSize);
            var angle = frame.BlockAngleAt(new P2(e.X, e.Y), _rotation);
            if (!angle.HasValue) return;
            var a = Math.Round(angle.Value);
            var snap = Math.Round(a / 45.0) * 45.0;
            if (Math.Abs(a - snap) <= 3) a = snap;
            _reference = Angles.NormalizeDegrees(a);
            Recompute();
            if (ReferencePicked != null) ReferencePicked(this, EventArgs.Empty);
        }

        // ------------------------------------------------------------------ drawing

        private PreviewFrame Frame(Size size)
        {
            return PreviewFrame.Fit(SymbolPreview.FitRadius(_sketch), size.Width, size.Height, FitMargin);
        }

        private void PaintPreview(Graphics g, Size size)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(_canvas.BackColor);
            using (var border = new Pen(SystemColors.ControlDark))
                g.DrawRectangle(border, 0, 0, size.Width - 1, size.Height - 1);

            var font = Font;
            if (_sketch == null || _sketch.IsEmpty)
            {
                var text = _sketch == null ? _message : "\"" + _sketch.Name + "\" has nothing the preview can draw" +
                           (_sketch.Simplified ? " (" + _sketch.SkippedSummary() + ")." : ".");
                using (var brush = new SolidBrush(SystemColors.GrayText))
                using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    g.DrawString(text ?? string.Empty, font, brush, new RectangleF(8, 8, Math.Max(10, size.Width - 16), Math.Max(10, size.Height - 16)), format);
                return;
            }

            var frame = Frame(size);
            var center = Pt(frame.Insertion);

            // North up, faint axes through the insertion point.
            using (var faint = new Pen(Color.FromArgb(222, 226, 232)))
            {
                g.DrawLine(faint, 0, center.Y, size.Width, center.Y);
                g.DrawLine(faint, center.X, 0, center.X, size.Height);
            }
            using (var grey = new SolidBrush(SystemColors.GrayText))
                g.DrawString("N", font, grey, center.X + 3, 2);

            // The symbol, turned about its insertion point.
            using (var pen = new Pen(Color.FromArgb(35, 35, 35), 1.4f))
            {
                foreach (var path in _sketch.Paths)
                {
                    var points = path.Points.Select(p => frame.ToScreen(p, _rotation))
                                     .Where(p => Finite(p.X) && Finite(p.Y) && Math.Abs(p.X) < 1e6 && Math.Abs(p.Y) < 1e6)
                                     .Select(Pt).ToArray();
                    if (points.Length < 2) continue;
                    if (path.Closed && points.Length > 2) g.DrawPolygon(pen, points);
                    else g.DrawLines(pen, points);
                }
            }

            var length = SymbolPreview.ArrowLength(_sketch.Radius());

            // The sample facing (survey azimuth): where the symbol is asked to point.
            if (_period > 0 || !_follow)
                using (var facing = new Pen(Color.FromArgb(230, 130, 20), 1.2f) { DashStyle = DashStyle.Dash })
                    g.DrawLine(facing, center, Pt(frame.Toward(Angles.AzimuthToCadDegrees(_azimuth), length)));

            // The native zero, turned with the block: on a directional symbol it lands on the facing.
            var zero = SymbolPreview.ArrowCadDegrees(_reference, _rotation);
            using (var arrow = new Pen(_period > 0 ? Color.FromArgb(25, 95, 200) : Color.FromArgb(150, 170, 200), 2f))
            {
                if (_period <= 0) arrow.DashStyle = DashStyle.Dot;
                arrow.CustomEndCap = new AdjustableArrowCap(4, 5);
                if (_period > 0 && _period < 360 - 1e-9)
                {
                    arrow.CustomStartCap = new AdjustableArrowCap(4, 5);
                    g.DrawLine(arrow, Pt(frame.Toward(zero + 180, length)), Pt(frame.Toward(zero, length)));
                }
                else g.DrawLine(arrow, center, Pt(frame.Toward(zero, length)));
            }
            var tip = Pt(frame.Toward(zero, length * 1.06));
            using (var blue = new SolidBrush(Color.FromArgb(25, 95, 200)))
                g.DrawString("zero", font, blue, tip.X - 12, tip.Y - 7);

            // The insertion point.
            using (var red = new Pen(Color.FromArgb(200, 30, 30), 1.6f))
            {
                g.DrawEllipse(red, center.X - 4, center.Y - 4, 8, 8);
                g.DrawLine(red, center.X - 7, center.Y, center.X + 7, center.Y);
                g.DrawLine(red, center.X, center.Y - 7, center.X, center.Y + 7);
            }

            using (var text = new SolidBrush(SystemColors.ControlText))
                g.DrawString(_sketch.Name, font, text, 4, 3);
            var lines = new List<string>();
            if (!string.IsNullOrEmpty(_caption)) lines.Add(_caption);
            if (_sketch.Simplified) lines.Add("Simplified preview: " + _sketch.SkippedSummary() + " not drawn.");
            if (lines.Count > 0)
                using (var grey = new SolidBrush(SystemColors.GrayText))
                    g.DrawString(string.Join(Environment.NewLine, lines.ToArray()), font, grey,
                                 new RectangleF(4, size.Height - 4 - 2 * font.Height - 2, Math.Max(10, size.Width - 8), 2 * font.Height + 2));
        }

        private static bool Finite(double v)
        {
            return !double.IsNaN(v) && !double.IsInfinity(v);
        }

        private static PointF Pt(P2 p)
        {
            return new PointF((float)p.X, (float)p.Y);
        }

        /// <summary>A double-buffered drawing surface.</summary>
        private sealed class Canvas : Panel
        {
            public Canvas()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                         ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            }
        }
    }
}
