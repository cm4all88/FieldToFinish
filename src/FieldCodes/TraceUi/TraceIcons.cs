using System;
using System.Collections.Generic;
using System.Globalization;

namespace FieldCodes.TraceUi
{
    /// <summary>What a shape is.</summary>
    public enum TraceOpKind
    {
        Line = 0,
        Poly = 1,
        Rect = 2,
        Circle = 3,
        Curve = 4
    }

    /// <summary>Which colour of the current theme a shape uses.</summary>
    public enum TraceRole
    {
        /// <summary>The icon's main stroke: navy on light, white on dark.</summary>
        Main = 0,
        /// <summary>The accent: survey blue on light, green on dark.</summary>
        Accent = 1,
        /// <summary>Always white (used inside the logo tile).</summary>
        White = 2,
        /// <summary>Always TRACE Navy (the logo tile).</summary>
        Navy = 3,
        /// <summary>Always accent green (the logo swoosh).</summary>
        Green = 4
    }

    /// <summary>
    /// One shape on an icon grid. P holds coordinates: Line x1 y1 x2 y2; Poly x y pairs;
    /// Rect x y w h; Circle cx cy r; Curve x0 y0 c1x c1y c2x c2y x1 y1 (cubic).
    /// </summary>
    public sealed class TraceOp
    {
        public TraceOpKind Kind { get; set; }
        public double[] P { get; set; }
        public TraceRole Role { get; set; }
        public bool Fill { get; set; }
        public bool Closed { get; set; }
        public bool Dashed { get; set; }
        public double Width { get; set; }
    }

    /// <summary>An icon: shapes drawn on a square grid (24 units, 48 for the logo).</summary>
    public sealed class TraceIcon
    {
        public string Id { get; set; }
        public int Grid { get; set; }
        public IList<TraceOp> Ops { get; set; }
    }

    /// <summary>
    /// The TRACE icon set as plain data, so the ribbon (WPF) and the palette (GDI+) draw the
    /// same pictures and nothing ships as an image file that could go missing.
    /// </summary>
    public static class TraceIcons
    {
        private static readonly Dictionary<string, TraceIcon> Set = Build();

        /// <summary>All icon ids.</summary>
        public static ICollection<string> Ids { get { return Set.Keys; } }

        public static bool Has(string id)
        {
            return id != null && Set.ContainsKey(id);
        }

        public static TraceIcon Get(string id)
        {
            TraceIcon icon;
            if (id == null || !Set.TryGetValue(id, out icon))
                throw new ArgumentException("Unknown TRACE icon '" + id + "'.");
            return icon;
        }

        // ------------------------------------------------------------ builders

        private static double[] A(params double[] v) { return v; }

        private static TraceOp Line(TraceRole r, double x1, double y1, double x2, double y2)
        { return new TraceOp { Kind = TraceOpKind.Line, P = A(x1, y1, x2, y2), Role = r }; }

        private static TraceOp Poly(TraceRole r, bool closed, bool fill, params double[] xy)
        { return new TraceOp { Kind = TraceOpKind.Poly, P = xy, Role = r, Closed = closed, Fill = fill }; }

        private static TraceOp Rect(TraceRole r, double x, double y, double w, double h, bool fill)
        { return new TraceOp { Kind = TraceOpKind.Rect, P = A(x, y, w, h), Role = r, Fill = fill }; }

        private static TraceOp Circle(TraceRole r, double cx, double cy, double rad, bool fill, bool dashed)
        { return new TraceOp { Kind = TraceOpKind.Circle, P = A(cx, cy, rad), Role = r, Fill = fill, Dashed = dashed }; }

        private static TraceOp Curve(TraceRole r, double w, params double[] p)
        { return new TraceOp { Kind = TraceOpKind.Curve, P = p, Role = r, Width = w }; }

        private static void Add(Dictionary<string, TraceIcon> d, string id, int grid, params TraceOp[] ops)
        { d[id] = new TraceIcon { Id = id, Grid = grid, Ops = new List<TraceOp>(ops) }; }

        private static Dictionary<string, TraceIcon> Build()
        {
            var d = new Dictionary<string, TraceIcon>(StringComparer.Ordinal);
            const TraceRole M = TraceRole.Main;
            const TraceRole C = TraceRole.Accent;

            // The logo tile: navy square, white ridge, green contour swoosh, survey target.
            Add(d, "trace", 48,
                Rect(TraceRole.Navy, 0, 0, 48, 48, true),
                Curve(TraceRole.Green, 3, 5, 36, 16, 31, 30, 32, 44, 21),
                Poly(TraceRole.White, false, false, 6, 30, 17, 17, 24, 24, 33, 11, 43, 28),
                Circle(TraceRole.White, 14, 10, 3.4, false, false),
                Line(TraceRole.White, 14, 5.2, 14, 14.8),
                Line(TraceRole.White, 9.2, 10, 18.8, 10));

            Add(d, "run", 24,
                Circle(M, 12, 12, 9, false, false),
                Poly(C, true, true, 10, 8.2, 16, 12, 10, 15.8));
            Add(d, "window", 24,
                Rect(M, 3, 4, 18, 16, false),
                Line(C, 14.5, 4, 14.5, 20));
            Add(d, "clean", 24,
                Poly(M, true, false, 5, 15, 13, 7, 19, 13, 13, 19, 8, 19),
                Line(C, 9, 11, 15, 17));
            Add(d, "order", 24,
                Poly(M, true, false, 12, 4, 21, 9, 12, 14, 3, 9),
                Poly(C, false, false, 3, 14, 12, 19, 21, 14));
            Add(d, "labels", 24,
                Rect(M, 3, 6, 18, 12, false),
                Line(C, 7, 10, 17, 10),
                Line(C, 7, 14, 13, 14));
            Add(d, "labelline", 24,
                Line(M, 3, 19, 21, 11),
                Poly(C, true, false, 7.34, 8.26, 14.71, 5.14, 16.66, 9.74, 9.29, 12.86));
            Add(d, "between", 24,
                Line(M, 3, 6, 21, 6),
                Line(M, 3, 18, 21, 18),
                Line(C, 12, 9, 12, 15),
                Poly(C, false, false, 9.5, 11.5, 12, 9, 14.5, 11.5));
            Add(d, "stairs", 24,
                Poly(M, false, false, 3, 19, 8, 19, 8, 14, 13, 14, 13, 9, 18, 9, 18, 5),
                Line(C, 18, 5, 21, 5));
            Add(d, "spot", 24,
                Line(M, 6, 6, 18, 18),
                Line(M, 18, 6, 6, 18),
                Circle(C, 12, 12, 2.2, false, false));
            Add(d, "tags", 24,
                Poly(M, true, false, 4, 4, 12, 4, 20, 12, 12, 20, 4, 12),
                Circle(C, 8.5, 8.5, 1.4, false, false));
            Add(d, "trees", 24,
                Circle(M, 12, 12, 8, false, false),
                Circle(C, 12, 12, 2.6, true, false),
                Line(C, 12, 4, 12, 8),
                Line(C, 12, 16, 12, 20),
                Line(C, 4, 12, 8, 12),
                Line(C, 16, 12, 20, 12));
            Add(d, "drip", 24,
                Circle(C, 12, 12, 8.5, false, true),
                Circle(M, 12, 12, 2.4, false, false));
            Add(d, "drawline", 24,
                Circle(M, 5, 19, 2, false, false),
                Circle(C, 19, 6, 2, false, false),
                Line(M, 6.6, 17.6, 17.4, 7.4));
            Add(d, "table", 24,
                Rect(M, 3, 4, 18, 16, false),
                Line(C, 3, 9.5, 21, 9.5),
                Line(C, 9, 9.5, 9, 20),
                Line(M, 3, 14.8, 21, 14.8));
            Add(d, "control", 24,
                Poly(M, true, false, 12, 4, 20, 19, 4, 19),
                Circle(C, 12, 14, 1.8, true, false));
            Add(d, "legend", 24,
                Rect(C, 3, 5, 4, 4, false),
                Rect(M, 3, 15, 4, 4, false),
                Line(M, 11, 7, 21, 7),
                Line(M, 11, 17, 21, 17));
            Add(d, "review", 24,
                Circle(M, 10.5, 10.5, 6.5, false, false),
                Line(M, 15.5, 15.5, 21, 21),
                Poly(C, false, false, 7.8, 10.6, 9.9, 12.7, 13.5, 8.5));
            Add(d, "setup", 24,
                Line(M, 4, 7, 20, 7),
                Line(M, 4, 12, 20, 12),
                Line(M, 4, 17, 20, 17),
                Circle(C, 9, 7, 2, false, false),
                Circle(C, 15, 12, 2, false, false),
                Circle(C, 8, 17, 2, false, false));
            Add(d, "profiles", 24,
                Rect(M, 3, 7, 14, 12, false),
                Poly(C, false, false, 7, 4, 19, 4, 21, 6, 21, 16));
            Add(d, "diag", 24,
                Poly(C, false, false, 3, 12, 7, 12, 9.2, 6, 13.2, 18, 15.4, 12, 21, 12));

            return d;
        }

        /// <summary>#RRGGBB for a role on a theme: light = light palette, otherwise dark.</summary>
        public static string Hex(TraceRole role, bool lightTheme)
        {
            switch (role)
            {
                case TraceRole.Navy: return "#0B2E4F";
                case TraceRole.Green: return "#7CC84A";
                case TraceRole.White: return "#FFFFFF";
                case TraceRole.Accent: return lightTheme ? "#1E6FB8" : "#7CC84A";
                default: return lightTheme ? "#0B2E4F" : "#FFFFFF";
            }
        }

        /// <summary>Test helper: every coordinate sits inside the icon's grid.</summary>
        public static bool FitsGrid(TraceIcon icon)
        {
            foreach (var op in icon.Ops)
            {
                var n = op.P.Length;
                for (var i = 0; i < n; i++)
                {
                    var v = op.P[i];
                    // Rect and circle store sizes, not positions; check their extents below.
                    if (v < -0.001 || v > icon.Grid + 0.001) return false;
                }
                if (op.Kind == TraceOpKind.Rect && (op.P[0] + op.P[2] > icon.Grid + 0.001 || op.P[1] + op.P[3] > icon.Grid + 0.001)) return false;
                if (op.Kind == TraceOpKind.Circle &&
                    (op.P[0] - op.P[2] < -0.001 || op.P[1] - op.P[2] < -0.001 ||
                     op.P[0] + op.P[2] > icon.Grid + 0.001 || op.P[1] + op.P[2] > icon.Grid + 0.001)) return false;
            }
            return true;
        }

        /// <summary>Invariant text for a coordinate (used by tests and diagnostics).</summary>
        public static string Num(double v)
        {
            return v.ToString("0.##", CultureInfo.InvariantCulture);
        }
    }
}
