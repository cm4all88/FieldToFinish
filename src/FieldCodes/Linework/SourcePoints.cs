using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FieldCodes.Linework
{
    /// <summary>
    /// One COGO point as it is in the drawing: its number, its handle, its raw description exactly as
    /// stored (never the parser's combined text), and where it is. The identity every check that ties
    /// linework, labels or review items back to the field shots shares.
    /// </summary>
    public sealed class SourcePoint
    {
        public string Number { get; set; }
        public string Handle { get; set; }
        /// <summary>The raw description as stored on the point. May be empty.</summary>
        public string RawDescription { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }

        public bool HasDescription { get { return !string.IsNullOrWhiteSpace(RawDescription); } }

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "{0} \"{1}\" ({2:0.###}, {3:0.###}, {4:0.###})",
                                 Number, RawDescription, X, Y, Z);
        }
    }

    /// <summary>
    /// Every source point, found by plan position. Linework is drawn THROUGH the shots, so a vertex
    /// and the shot that made it coincide; a point within <see cref="Tolerance"/> (plan distance) of a
    /// position is that position's shot. Coincidence, not proximity: the tolerance is the rounding of
    /// stored coordinates, not a search radius.
    ///
    /// Replaces the old 0.01-unit string bucket, which (a) kept only descriptions -- no point number,
    /// handle or elevation -- and (b) missed a shot and a vertex a hair apart on either side of a
    /// rounding boundary, because it never looked in the neighbouring bucket. This index searches the
    /// neighbouring cells and returns the points themselves.
    /// </summary>
    public sealed class SourcePointIndex
    {
        /// <summary>Drawing units. The old bucket's rounding (0.01) as a true distance.</summary>
        public const double DefaultTolerance = 0.01;

        private readonly Dictionary<long, List<SourcePoint>> _cells = new Dictionary<long, List<SourcePoint>>();
        private readonly Dictionary<string, SourcePoint> _byNumber = new Dictionary<string, SourcePoint>(StringComparer.Ordinal);
        private readonly List<SourcePoint> _all;
        private readonly double _cell;

        public SourcePointIndex(IEnumerable<SourcePoint> points, double tolerance = DefaultTolerance)
        {
            if (!(tolerance > 0)) throw new ArgumentOutOfRangeException("tolerance");
            Tolerance = tolerance;
            _cell = tolerance;
            _all = (points ?? Enumerable.Empty<SourcePoint>()).Where(p => p != null).ToList();
            foreach (var p in _all)
            {
                List<SourcePoint> list;
                var key = Key(Cell(p.X), Cell(p.Y));
                if (!_cells.TryGetValue(key, out list)) { list = new List<SourcePoint>(); _cells[key] = list; }
                list.Add(p);
                if (!string.IsNullOrEmpty(p.Number) && !_byNumber.ContainsKey(p.Number)) _byNumber[p.Number] = p;
            }
        }

        public double Tolerance { get; private set; }

        public int Count { get { return _all.Count; } }

        /// <summary>Every point, in the order given (the readers give point-number order).</summary>
        public IList<SourcePoint> All { get { return _all.AsReadOnly(); } }

        public SourcePoint ByNumber(string number)
        {
            SourcePoint p;
            return number != null && _byNumber.TryGetValue(number, out p) ? p : null;
        }

        /// <summary>
        /// The points at this plan position (within <see cref="Tolerance"/>), nearest first, then by
        /// point number. Empty when no shot is there. More than one means stacked or duplicate shots.
        /// </summary>
        public IList<SourcePoint> At(double x, double y)
        {
            var found = new List<KeyValuePair<double, SourcePoint>>();
            var cx = Cell(x);
            var cy = Cell(y);
            for (var i = cx - 1; i <= cx + 1; i++)
                for (var j = cy - 1; j <= cy + 1; j++)
                {
                    List<SourcePoint> list;
                    if (!_cells.TryGetValue(Key(i, j), out list)) continue;
                    foreach (var p in list)
                    {
                        var d = Math.Sqrt((p.X - x) * (p.X - x) + (p.Y - y) * (p.Y - y));
                        if (d <= Tolerance + 1e-9) found.Add(new KeyValuePair<double, SourcePoint>(d, p));
                    }
                }
            return found.OrderBy(f => f.Key)
                        .ThenBy(f => NumberKey(f.Value.Number))
                        .ThenBy(f => f.Value.Number, StringComparer.Ordinal)
                        .Select(f => f.Value)
                        .ToList();
        }

        /// <summary>The raw descriptions of the points at a position (blank ones left out), in the
        /// same order as <see cref="At"/>.</summary>
        public IList<string> DescriptionsAt(double x, double y)
        {
            return At(x, y).Where(p => p.HasDescription).Select(p => p.RawDescription).ToList();
        }

        /// <summary>The shots a polyline was drawn through, one entry per vertex: the points at that
        /// vertex (possibly none, possibly several).</summary>
        public IList<IList<SourcePoint>> AtVertices(IEnumerable<KeyValuePair<double, double>> vertices)
        {
            return (vertices ?? Enumerable.Empty<KeyValuePair<double, double>>())
                .Select(v => At(v.Key, v.Value)).ToList();
        }

        /// <summary>Numeric order of point numbers where they are numbers (1002 before 10001).</summary>
        public static double NumberKey(string number)
        {
            double n;
            return double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out n) ? n : double.MaxValue;
        }

        private long Cell(double v)
        {
            return (long)Math.Floor(v / _cell);
        }

        private static long Key(long i, long j)
        {
            unchecked { return (i * 73856093L) ^ (j * 19349663L); }
        }
    }
}
