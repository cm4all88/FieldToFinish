using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using FieldCodes.Styles;

namespace FieldCodes.Cad.Setup
{
    /// <summary>
    /// A sketch of one label as FTF will draw it: the point, a line running past it (so the
    /// mask shows), the leader with its arrowhead, landing and line type, and the text with
    /// its height, width, slant, angle, colour, justification, prefix, suffix and rounding.
    /// Drawn at plot scale on a model-space-dark background. The font is the text style's
    /// TrueType face when Windows has it, otherwise a stand-in -- the note under it says so.
    /// Nothing in the drawing is touched.
    /// </summary>
    internal sealed class StylePreview : Control
    {
        private ResolvedAnnotationStyle _style;
        private string _sample = "18\" FIR";
        private bool _withLeader = true;
        private Dictionary<string, string> _fonts;
        private double? _symbolDegrees;

        /// <summary>Screen pixels per plotted inch.</summary>
        private const float PixelsPerInch = 150f;

        private static readonly Color Background = Color.FromArgb(33, 40, 48);
        private static readonly Color LineworkColor = Color.FromArgb(120, 130, 140);

        public StylePreview()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = Background;
            MinimumSize = new Size(260, 150);
        }

        public void ShowStyle(ResolvedAnnotationStyle style, string sample, bool withLeader,
                         Dictionary<string, string> fonts, double? symbolDegrees = null)
        {
            _style = style;
            _sample = string.IsNullOrEmpty(sample) ? "18\" FIR" : sample;
            _withLeader = withLeader;
            _fonts = fonts;
            _symbolDegrees = symbolDegrees;
            Invalidate();
        }

        /// <summary>What the preview could not show exactly, for the caption under it.</summary>
        public string Caveat
        {
            get
            {
                if (_style == null) return string.Empty;
                var face = FaceFor(_style.Text.DrawingTextStyle);
                return face == null
                    ? "Font shown approximately (" + (string.IsNullOrEmpty(_style.Text.DrawingTextStyle) ? "current text style" : _style.Text.DrawingTextStyle) + " is an SHX or unknown font)."
                    : "Font: " + face + ".";
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Background);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            if (_style == null) return;

            var t = _style.Text;
            var l = _style.Leader;
            var anchor = new PointF(Width * 0.28f, Height * 0.68f);

            // A line through the label area: a mask hides it, no mask shows it through.
            using (var pen = new Pen(LineworkColor, 1f) { DashStyle = DashStyle.Dash })
                g.DrawLine(pen, 0, Height * 0.36f, Width, Height * 0.44f);

            // The point: a small cross in a circle, like a survey symbol.
            using (var pen = new Pen(Color.FromArgb(230, 230, 230), 1.2f))
            {
                g.DrawEllipse(pen, anchor.X - 5, anchor.Y - 5, 10, 10);
                g.DrawLine(pen, anchor.X - 8, anchor.Y, anchor.X + 8, anchor.Y);
                g.DrawLine(pen, anchor.X, anchor.Y - 8, anchor.X, anchor.Y + 8);
            }

            var contents = StyleText.Compose(_sample, t);
            var lines = StyleText.PlainLines(contents);
            var heightPx = Math.Max(4f, (float)t.HeightPlotted * PixelsPerInch);
            var face = FaceFor(t.DrawingTextStyle) ?? "Arial";
            using (var font = MakeFont(face, heightPx))
            {
                var lineHeight = heightPx * 1.6f;
                var widths = new List<float>();
                foreach (var line in lines) widths.Add(g.MeasureString(line, font, PointF.Empty, StringFormat.GenericTypographic).Width * (float)Math.Max(0.05, t.WidthFactor));
                var boxW = 0f;
                foreach (var w in widths) boxW = Math.Max(boxW, w);
                var boxH = lineHeight * lines.Count - (lineHeight - heightPx);

                // Where the text sits: up and to the right, like an Auto placement on ring 1.
                var landingLen = _withLeader && l.Landing != false ? (float)(l.DoglegPlotted ?? 0.09) * PixelsPerInch : 0f;
                var gap = _withLeader ? (float)(l.LandingGapPlotted ?? 0.03) * PixelsPerInch : 0f;
                var textLeft = anchor.X + (_withLeader ? 70f + landingLen + gap : (float)_style.OffsetPlotted * PixelsPerInch + 8f);
                var midY = anchor.Y - (_withLeader ? 55f : 20f);
                var attachY = AttachY(l.Attachment, midY, boxH, lineHeight, heightPx, lines.Count);
                var textTop = midY - boxH / 2f;
                var box = new RectangleF(textLeft, textTop, boxW, boxH);

                // Mask first, so the text draws over it.
                if (t.Mask != MaskMode.None)
                {
                    var pad = heightPx * 0.25f;
                    using (var fill = new SolidBrush(Background))
                        g.FillRectangle(fill, box.X - pad, box.Y - pad, box.Width + pad * 2, box.Height + pad * 2);
                    if (t.Mask == MaskMode.Wipeout)
                        using (var pen = new Pen(Color.FromArgb(70, 80, 90)) { DashStyle = DashStyle.Dot })
                            g.DrawRectangle(pen, box.X - pad, box.Y - pad, box.Width + pad * 2, box.Height + pad * 2);
                }

                if (_withLeader) DrawLeader(g, l, anchor, new PointF(textLeft - gap, attachY), landingLen, heightPx, box);

                var color = AciColor(t.Color, Color.FromArgb(240, 240, 240));
                var angle = -(float)StyleText.AngleDegrees(t, _symbolDegrees);
                var state = g.Save();
                g.TranslateTransform(box.X, box.Y);
                g.RotateTransform(angle);
                using (var brush = new SolidBrush(color))
                {
                    for (var i = 0; i < lines.Count; i++)
                    {
                        var x = 0f;
                        if (t.Justification == TextJustification.Center) x = (boxW - widths[i]) / 2f;
                        else if (t.Justification == TextJustification.Right) x = boxW - widths[i];
                        var lineState = g.Save();
                        g.TranslateTransform(x, i * lineHeight);
                        var shear = (float)Math.Tan(-t.ObliqueDegrees * Math.PI / 180.0);
                        using (var m = new Matrix((float)Math.Max(0.05, t.WidthFactor), 0, shear, 1, 0, 0))
                            g.MultiplyTransform(m);
                        g.DrawString(lines[i], font, brush, 0, -heightPx * 0.25f, StringFormat.GenericTypographic);
                        g.Restore(lineState);
                    }
                }
                g.Restore(state);
            }

            using (var small = new Font(SystemFonts.DefaultFont.FontFamily, 7.5f))
            using (var brush = new SolidBrush(Color.FromArgb(150, 160, 170)))
                g.DrawString("Plot scale: " + t.HeightPlotted.ToString("0.###") + "\" text" +
                             (t.Annotative ? ", annotative" : string.Empty), small, brush, 6, Height - 16);
        }

        private static float AttachY(LeaderAttachment a, float midY, float boxH, float lineHeight, float textH, int lines)
        {
            var top = midY - boxH / 2f;
            var bottom = midY + boxH / 2f;
            switch (a)
            {
                case LeaderAttachment.TopOfTopLine: return top;
                case LeaderAttachment.MiddleOfTopLine: return top + textH / 2f;
                case LeaderAttachment.BottomOfTopLine: return top + textH;
                case LeaderAttachment.UnderlineTopLine: return top + textH + textH * 0.25f;
                case LeaderAttachment.MiddleOfBottomLine: return bottom - textH / 2f;
                case LeaderAttachment.BottomOfBottomLine: return bottom;
                case LeaderAttachment.UnderlineBottomLine:
                case LeaderAttachment.UnderlineAllText: return bottom + textH * 0.25f;
                default: return midY;
            }
        }

        private static void DrawLeader(Graphics g, ResolvedLeaderStyle l, PointF anchor, PointF landingEnd, float landingLen,
                                       float textH, RectangleF box)
        {
            var color = AciColor(l.Color, Color.FromArgb(240, 240, 240));
            var weight = FieldCodes.Settings.LayerAppearance.LineWeightHundredths(l.Lineweight);
            var width = weight.HasValue && weight.Value > 0 ? Math.Max(1f, weight.Value / 100f * 4f) : 1.2f;
            using (var pen = new Pen(color, width))
            {
                if (!string.IsNullOrWhiteSpace(l.Linetype) && !StyleCheck.IsSpecialLinetype(l.Linetype))
                    pen.DashStyle = DashStyle.Dash;
                var landingStart = new PointF(landingEnd.X - landingLen, landingEnd.Y);
                if (l.LineType == LeaderLineKind.Spline)
                {
                    var mid = new PointF((anchor.X + landingStart.X) / 2f - 10f, (anchor.Y + landingStart.Y) / 2f - 14f);
                    g.DrawCurve(pen, new[] { anchor, mid, landingStart });
                }
                else
                {
                    g.DrawLine(pen, anchor, landingStart);
                }
                if (landingLen > 0) g.DrawLine(pen, landingStart, landingEnd);
                if (l.Attachment == LeaderAttachment.UnderlineAllText)
                    g.DrawLine(pen, box.Left, landingEnd.Y, box.Right, landingEnd.Y);
                else if (l.Attachment == LeaderAttachment.UnderlineBottomLine || l.Attachment == LeaderAttachment.UnderlineTopLine)
                    g.DrawLine(pen, landingEnd, new PointF(box.Right, landingEnd.Y));

                var size = l.SizeArrow && l.ArrowSizePlotted > 0 ? (float)l.ArrowSizePlotted * PixelsPerInch : textH * 0.9f;
                DrawArrow(g, pen, color, l.ArrowBlock, anchor, landingStart, Math.Max(4f, size));
            }
        }

        private static void DrawArrow(Graphics g, Pen pen, Color color, string arrow, PointF tip, PointF from, float size)
        {
            var name = (arrow ?? string.Empty).Trim().TrimStart('_').ToUpperInvariant();
            var dx = from.X - tip.X;
            var dy = from.Y - tip.Y;
            var len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-3) return;
            var ux = dx / len;
            var uy = dy / len;
            var px = -uy;
            var py = ux;
            using (var brush = new SolidBrush(color))
            {
                switch (name)
                {
                    case "NONE":
                        return;
                    case "DOT":
                        g.FillEllipse(brush, tip.X - size / 2, tip.Y - size / 2, size, size);
                        return;
                    case "DOTSMALL":
                        g.FillEllipse(brush, tip.X - size / 4, tip.Y - size / 4, size / 2, size / 2);
                        return;
                    case "DOTBLANK":
                        g.DrawEllipse(pen, tip.X - size / 2, tip.Y - size / 2, size, size);
                        return;
                    case "ARCHTICK":
                    case "OBLIQUE":
                        g.DrawLine(pen, tip.X - (ux + px) * size / 2, tip.Y - (uy + py) * size / 2,
                                        tip.X + (ux + px) * size / 2, tip.Y + (uy + py) * size / 2);
                        return;
                    case "OPEN":
                    case "OPEN30":
                    case "OPEN90":
                    {
                        var spread = name == "OPEN30" ? 0.27f : name == "OPEN90" ? 1f : 0.35f;
                        g.DrawLine(pen, tip, new PointF(tip.X + (ux + px * spread) * size, tip.Y + (uy + py * spread) * size));
                        g.DrawLine(pen, tip, new PointF(tip.X + (ux - px * spread) * size, tip.Y + (uy - py * spread) * size));
                        return;
                    }
                    case "BOXFILLED":
                    case "BOXBLANK":
                    {
                        var r = new RectangleF(tip.X - size / 2, tip.Y - size / 2, size, size);
                        if (name == "BOXFILLED") g.FillRectangle(brush, r); else g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
                        return;
                    }
                    default:
                    {
                        // Closed filled -- AutoCAD's default -- and anything FTF cannot sketch.
                        var pts = new[]
                        {
                            tip,
                            new PointF(tip.X + (ux + px * 0.18f) * size, tip.Y + (uy + py * 0.18f) * size),
                            new PointF(tip.X + (ux - px * 0.18f) * size, tip.Y + (uy - py * 0.18f) * size)
                        };
                        if (name == "CLOSEDBLANK" || name == "CLOSED") g.DrawPolygon(pen, pts); else g.FillPolygon(brush, pts);
                        return;
                    }
                }
            }
        }

        private string FaceFor(string textStyle)
        {
            if (_fonts == null) return null;
            string face;
            if (!_fonts.TryGetValue(textStyle ?? string.Empty, out face) || string.IsNullOrEmpty(face)) return null;
            if (face.EndsWith(".shx", StringComparison.OrdinalIgnoreCase)) return null;
            return face;
        }

        private static Font MakeFont(string face, float heightPx)
        {
            try
            {
                // Cap height of a typical face is about 0.7 of the em, and AutoCAD text height
                // is the cap height.
                return new Font(face, Math.Max(3f, heightPx / 0.72f), FontStyle.Regular, GraphicsUnit.Pixel);
            }
            catch (ArgumentException)
            {
                return new Font("Arial", Math.Max(3f, heightPx / 0.72f), FontStyle.Regular, GraphicsUnit.Pixel);
            }
        }

        /// <summary>An AutoCAD colour index as the screen shows it on a dark background.</summary>
        internal static Color AciColor(string setting, Color byLayer)
        {
            var index = StyleColor.Index(setting);
            if (!index.HasValue || index.Value == 256 || index.Value == 0) return byLayer;
            return Aci(index.Value);
        }

        internal static Color Aci(short i)
        {
            switch (i)
            {
                case 1: return Color.Red;
                case 2: return Color.Yellow;
                case 3: return Color.Lime;
                case 4: return Color.Cyan;
                case 5: return Color.Blue;
                case 6: return Color.Magenta;
                case 7: return Color.White;
                case 8: return Color.FromArgb(128, 128, 128);
                case 9: return Color.FromArgb(192, 192, 192);
            }
            if (i >= 250)
            {
                var v = 51 + (i - 250) * 40;
                return Color.FromArgb(v, v, v);
            }
            var hue = ((i - 10) / 10) * 15.0;
            var shade = (i - 10) % 10;
            var value = new[] { 1.0, 1.0, 0.8, 0.8, 0.6, 0.6, 0.5, 0.5, 0.3, 0.3 }[shade];
            var saturation = shade % 2 == 0 ? 1.0 : 0.5;
            return FromHsv(hue, saturation, value);
        }

        private static Color FromHsv(double h, double s, double v)
        {
            var c = v * s;
            var x = c * (1 - Math.Abs(h / 60.0 % 2 - 1));
            var m = v - c;
            double r, g, b;
            if (h < 60) { r = c; g = x; b = 0; }
            else if (h < 120) { r = x; g = c; b = 0; }
            else if (h < 180) { r = 0; g = c; b = x; }
            else if (h < 240) { r = 0; g = x; b = c; }
            else if (h < 300) { r = x; g = 0; b = c; }
            else { r = c; g = 0; b = x; }
            return Color.FromArgb((int)((r + m) * 255), (int)((g + m) * 255), (int)((b + m) * 255));
        }
    }
}
