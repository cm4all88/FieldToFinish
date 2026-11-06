#if TRACE_EDITION
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FieldCodes.TraceUi;

namespace FieldCodes.Cad
{
    /// <summary>Draws the TRACE icon set with WPF (used by the ribbon).</summary>
    internal static class TraceIconWpf
    {
        public static ImageSource Render(string id, int px, bool lightTheme)
        {
            var icon = TraceIcons.Get(id);
            var k = px / (double)icon.Grid;
            var stroke = System.Math.Max(1.2, (icon.Grid == 24 ? 1.75 : 3.0) * k);

            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                foreach (var op in icon.Ops)
                {
                    var color = (Color)ColorConverter.ConvertFromString(TraceIcons.Hex(op.Role, lightTheme));
                    var brush = new SolidColorBrush(color);
                    brush.Freeze();
                    var pen = new Pen(brush, op.Width > 0 ? op.Width * k : stroke);
                    pen.StartLineCap = PenLineCap.Round;
                    pen.EndLineCap = PenLineCap.Round;
                    pen.LineJoin = PenLineJoin.Round;
                    if (op.Dashed) pen.DashStyle = new DashStyle(new double[] { 1.8, 1.8 }, 0);
                    pen.Freeze();

                    var p = op.P;
                    switch (op.Kind)
                    {
                        case TraceOpKind.Line:
                            dc.DrawLine(pen, new Point(p[0] * k, p[1] * k), new Point(p[2] * k, p[3] * k));
                            break;

                        case TraceOpKind.Rect:
                            {
                                var rect = new Rect(p[0] * k, p[1] * k, p[2] * k, p[3] * k);
                                var radius = op.Fill ? 0.2 * System.Math.Min(rect.Width, rect.Height) : 0.0;
                                dc.DrawRoundedRectangle(op.Fill ? brush : null, op.Fill ? null : pen, rect, radius, radius);
                                break;
                            }

                        case TraceOpKind.Circle:
                            dc.DrawEllipse(op.Fill ? brush : null, op.Fill ? null : pen, new Point(p[0] * k, p[1] * k), p[2] * k, p[2] * k);
                            break;

                        case TraceOpKind.Poly:
                            {
                                var geometry = new StreamGeometry();
                                using (var g = geometry.Open())
                                {
                                    g.BeginFigure(new Point(p[0] * k, p[1] * k), op.Fill && op.Closed, op.Closed);
                                    for (var i = 1; i < p.Length / 2; i++)
                                        g.LineTo(new Point(p[2 * i] * k, p[2 * i + 1] * k), true, true);
                                }
                                geometry.Freeze();
                                dc.DrawGeometry(op.Fill && op.Closed ? brush : null, pen, geometry);
                                break;
                            }

                        case TraceOpKind.Curve:
                            {
                                var geometry = new StreamGeometry();
                                using (var g = geometry.Open())
                                {
                                    g.BeginFigure(new Point(p[0] * k, p[1] * k), false, false);
                                    g.BezierTo(new Point(p[2] * k, p[3] * k), new Point(p[4] * k, p[5] * k), new Point(p[6] * k, p[7] * k), true, true);
                                }
                                geometry.Freeze();
                                dc.DrawGeometry(null, pen, geometry);
                                break;
                            }
                    }
                }
            }

            var bitmap = new RenderTargetBitmap(px, px, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }
    }
}
#endif
