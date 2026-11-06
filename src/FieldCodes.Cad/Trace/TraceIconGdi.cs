#if TRACE_EDITION
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using FieldCodes.TraceUi;

namespace FieldCodes.Cad
{
    /// <summary>Draws the TRACE icon set with GDI+ (used by the palette).</summary>
    internal static class TraceIconGdi
    {
        public static Bitmap Render(string id, int px, bool lightTheme)
        {
            var bitmap = new Bitmap(px, px, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                Draw(g, TraceIcons.Get(id), 0f, 0f, px, lightTheme);
            }
            return bitmap;
        }

        public static void Draw(Graphics g, TraceIcon icon, float x, float y, float size, bool lightTheme)
        {
            var k = size / icon.Grid;
            var stroke = System.Math.Max(1.2f, (icon.Grid == 24 ? 1.75f : 3f) * k);

            foreach (var op in icon.Ops)
            {
                var color = ColorTranslator.FromHtml(TraceIcons.Hex(op.Role, lightTheme));
                var width = op.Width > 0 ? (float)op.Width * k : stroke;
                using (var pen = new Pen(color, width))
                using (var brush = new SolidBrush(color))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    pen.LineJoin = LineJoin.Round;
                    if (op.Dashed) pen.DashPattern = new[] { 1.8f, 1.8f };

                    var p = op.P;
                    switch (op.Kind)
                    {
                        case TraceOpKind.Line:
                            g.DrawLine(pen, x + (float)p[0] * k, y + (float)p[1] * k, x + (float)p[2] * k, y + (float)p[3] * k);
                            break;

                        case TraceOpKind.Rect:
                            {
                                var rect = new RectangleF(x + (float)p[0] * k, y + (float)p[1] * k, (float)p[2] * k, (float)p[3] * k);
                                if (op.Fill)
                                {
                                    using (var path = Rounded(rect, 0.2f * System.Math.Min(rect.Width, rect.Height)))
                                        g.FillPath(brush, path);
                                }
                                else
                                {
                                    g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
                                }
                                break;
                            }

                        case TraceOpKind.Circle:
                            {
                                var r = (float)p[2] * k;
                                var box = new RectangleF(x + (float)p[0] * k - r, y + (float)p[1] * k - r, 2 * r, 2 * r);
                                if (op.Fill) g.FillEllipse(brush, box); else g.DrawEllipse(pen, box);
                                break;
                            }

                        case TraceOpKind.Poly:
                            {
                                var pts = new PointF[p.Length / 2];
                                for (var i = 0; i < pts.Length; i++)
                                    pts[i] = new PointF(x + (float)p[2 * i] * k, y + (float)p[2 * i + 1] * k);
                                if (op.Closed)
                                {
                                    if (op.Fill) g.FillPolygon(brush, pts);
                                    g.DrawPolygon(pen, pts);
                                }
                                else
                                {
                                    g.DrawLines(pen, pts);
                                }
                                break;
                            }

                        case TraceOpKind.Curve:
                            g.DrawBezier(pen,
                                new PointF(x + (float)p[0] * k, y + (float)p[1] * k),
                                new PointF(x + (float)p[2] * k, y + (float)p[3] * k),
                                new PointF(x + (float)p[4] * k, y + (float)p[5] * k),
                                new PointF(x + (float)p[6] * k, y + (float)p[7] * k));
                            break;
                    }
                }
            }
        }

        private static GraphicsPath Rounded(RectangleF r, float radius)
        {
            var d = radius * 2f;
            var path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
#endif
