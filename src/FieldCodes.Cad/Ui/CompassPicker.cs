using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using FieldCodes.Utilities;

namespace FieldCodes.Cad.Ui
{
    /// <summary>
    /// A compass, north up: click in the direction the field notes give and it takes the nearest of the 16 survey
    /// directions (N, N/NE, NE ...). The centre is "?" for a pipe whose direction was not recorded. Arrow keys step
    /// round the compass when it has focus.
    /// </summary>
    internal sealed class CompassPicker : Control
    {
        public const string Unknown = "?";

        private string _selected;
        private string _hover;

        /// <summary>Raised with the direction name (or "?") when one is clicked.</summary>
        public event Action<string> DirectionPicked;

        public CompassPicker()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.UserPaint | ControlStyles.Selectable | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Size = new Size(300, 300);
            Cursor = Cursors.Hand;
            TabStop = true;
            Font = DipBuilderForm.F(9f, false);
        }

        /// <summary>True when the chosen direction was copied from the connected pipe, not clicked here: it is shown
        /// lighter, outlined, until the drafter clicks a direction.</summary>
        public bool SelectedIsPrefilled
        {
            get { return _prefilled; }
            set { _prefilled = value; Invalidate(); }
        }

        private bool _prefilled;

        /// <summary>The direction shown as chosen: one of the 16, "?", or null (none, or one observed another way).</summary>
        public string Selected
        {
            get { return _selected; }
            set { _selected = value; Invalidate(); }
        }

        private PointF Center { get { return new PointF(Width / 2f, Height / 2f); } }
        private float Radius { get { return Math.Min(Width, Height) / 2f - 4f; } }
        private float CenterRadius { get { return Radius * 0.2f; } }

        /// <summary>What a click at this point means: one of the 16, "?" in the centre, null outside the compass.</summary>
        public string DirectionAt(Point p)
        {
            var dx = p.X - Center.X;
            var dy = p.Y - Center.Y;
            var r = Math.Sqrt(dx * dx + dy * dy);
            if (r > Radius) return null;
            if (r <= CenterRadius) return Unknown;
            // Screen y runs down; azimuth is clockwise from north.
            var az = Math.Atan2(dx, -dy) * 180.0 / Math.PI;
            return DirectionShortcuts.Nearest(az);
        }

        /// <summary>A point inside the wedge for a direction (the tests click here, as a drafter would).</summary>
        public Point PointFor(string name)
        {
            if (name == Unknown) return Point.Round(Center);
            var d = DirectionShortcuts.For(name);
            var az = d.AzimuthDegrees.Value * Math.PI / 180.0;
            var r = Radius * 0.6;
            return Point.Round(new PointF((float)(Center.X + Math.Sin(az) * r), (float)(Center.Y - Math.Cos(az) * r)));
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var h = DirectionAt(e.Location);
            if (h != _hover) { _hover = h; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = null;
            Invalidate();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left) return;
            Focus();
            Pick(DirectionAt(e.Location));
        }

        private void Pick(string name)
        {
            if (name == null) return;
            Selected = name;
            if (DirectionPicked != null) DirectionPicked(name);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            var k = keyData & Keys.KeyCode;
            return k == Keys.Left || k == Keys.Right || k == Keys.Up || k == Keys.Down || base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            var step = e.KeyCode == Keys.Right || e.KeyCode == Keys.Down ? 1 : e.KeyCode == Keys.Left || e.KeyCode == Keys.Up ? -1 : 0;
            if (step == 0) return;
            var current = DirectionShortcuts.For(_selected);
            var az = current != null ? current.AzimuthDegrees.Value : 0.0;
            Pick(current == null ? "N" : DirectionShortcuts.Nearest(az + step * 22.5));
            e.Handled = true;
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var c = Center;
            var r = Radius;
            var outer = new RectangleF(c.X - r, c.Y - r, 2 * r, 2 * r);

            using (var face = new SolidBrush(DipBuilderForm.Surface)) g.FillEllipse(face, outer);

            // The hovered and the chosen wedge.
            foreach (var pair in new[] { Tuple.Create(_hover, DipBuilderForm.AccentSoft), Tuple.Create(_selected, _prefilled ? DipBuilderForm.AccentSoft : DipBuilderForm.Accent) })
            {
                var d = pair.Item1 != null && pair.Item1 != Unknown ? DirectionShortcuts.For(pair.Item1) : null;
                if (d == null) continue;
                var start = (float)(d.AzimuthDegrees.Value - 90 - 11.25);
                using (var wedge = new SolidBrush(pair.Item2))
                    g.FillPie(wedge, outer.X, outer.Y, outer.Width, outer.Height, start, 22.5f);
                if (_prefilled && pair.Item1 == _selected)
                    using (var edge = new Pen(DipBuilderForm.Accent, 2f) { DashStyle = DashStyle.Dash })
                        g.DrawPie(edge, outer.X, outer.Y, outer.Width, outer.Height, start, 22.5f);
            }

            // Spokes between the 16 wedges; the 8 main ones reach the edge more strongly.
            using (var faint = new Pen(DipBuilderForm.Rule, 1f))
                for (var i = 0; i < 16; i++)
                {
                    var a = (i * 22.5 + 11.25) * Math.PI / 180.0;
                    g.DrawLine(faint, c.X + (float)(Math.Sin(a) * CenterRadius), c.Y - (float)(Math.Cos(a) * CenterRadius),
                               c.X + (float)(Math.Sin(a) * r), c.Y - (float)(Math.Cos(a) * r));
                }
            using (var rim = new Pen(Focused ? DipBuilderForm.Accent : DipBuilderForm.ButtonBorder, Focused ? 2f : 1.2f)) g.DrawEllipse(rim, outer);

            // Names: the 8 main points near the rim, the 8 between them a little further in.
            // The in-between names are bold too, a size under the main ones, so both read and hit easily.
            using (var major = DipBuilderForm.F(12f, true))
            using (var minor = new Font(DipBuilderForm.F(10f, false).FontFamily, DipBuilderForm.F(10f, false).Size, FontStyle.Bold))
                foreach (var p in DirectionShortcuts.All)
                {
                    var main = p.Key.IndexOf('/') < 0;
                    var a = p.Value * Math.PI / 180.0;
                    var at = main ? r * 0.82 : r * 0.56;
                    var font = main ? major : minor;
                    var size = TextRenderer.MeasureText(p.Key, font);
                    var pt = new Point((int)(c.X + Math.Sin(a) * at - size.Width / 2.0), (int)(c.Y - Math.Cos(a) * at - size.Height / 2.0));
                    var chosen = p.Key == _selected;
                    TextRenderer.DrawText(g, p.Key, font, pt, chosen && !_prefilled ? Color.White : DipBuilderForm.Ink);
                }

            // "?" in the middle: direction not recorded.
            var hub = new RectangleF(c.X - CenterRadius, c.Y - CenterRadius, 2 * CenterRadius, 2 * CenterRadius);
            var unknownChosen = _selected == Unknown;
            using (var fill = new SolidBrush(unknownChosen ? DipBuilderForm.Accent : _hover == Unknown ? DipBuilderForm.AccentSoft : DipBuilderForm.Calculated))
                g.FillEllipse(fill, hub);
            using (var edge = new Pen(DipBuilderForm.ButtonBorder)) g.DrawEllipse(edge, hub);
            using (var qf = DipBuilderForm.F(11f, true))
                TextRenderer.DrawText(g, Unknown, qf, Rectangle.Round(hub), unknownChosen ? Color.White : DipBuilderForm.Ink,
                                      TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
}
