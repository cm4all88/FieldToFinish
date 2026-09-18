using System;
using System.Collections.Generic;
using System.Linq;

namespace FieldCodes.Geometry
{
    /// <summary>A drip circle in drawing units.</summary>
    public struct Circle2d
    {
        public double X { get; private set; }
        public double Y { get; private set; }
        public double R { get; private set; }

        public Circle2d(double x, double y, double r)
            : this()
        {
            X = x;
            Y = y;
            R = r;
        }
    }

    /// <summary>
    /// A surviving piece of a drip circle. Angles are radians, counter-clockwise from
    /// east -- the same convention AutoCAD's Arc constructor takes.
    /// </summary>
    public struct ArcSpan
    {
        /// <summary>Normalized to [0, 2*pi).</summary>
        public double StartAngle { get; private set; }

        /// <summary>Always positive.</summary>
        public double Sweep { get; private set; }

        public ArcSpan(double startAngle, double sweep)
            : this()
        {
            StartAngle = startAngle;
            Sweep = sweep;
        }

        /// <summary>May exceed 2*pi; AutoCAD normalizes it.</summary>
        public double EndAngle { get { return StartAngle + Sweep; } }
    }

    /// <summary>What survives of one drip circle after its neighbours cut into it.</summary>
    public sealed class DripTrimResult
    {
        public int Index { get; set; }

        /// <summary>Nothing overlapped it -- draw a Circle, not arcs.</summary>
        public bool FullCircle { get; set; }

        /// <summary>Entirely swallowed by a larger circle -- draw nothing.</summary>
        public bool FullyHidden { get; set; }

        public IList<ArcSpan> Arcs { get; set; }

        public DripTrimResult()
        {
            Arcs = new List<ArcSpan>();
        }
    }

    /// <summary>
    /// Trims overlapping drip circles into their outer envelope.
    ///
    /// For each circle: find the angles at which overlapping neighbours cross it, sort
    /// them, and keep only those arcs whose midpoint lies outside every other circle.
    /// Neighbour lookup goes through a uniform grid rather than comparing every pair.
    ///
    /// Two tolerances matter. Containment is tested with a margin so a point sitting
    /// exactly on a neighbour's boundary counts as outside; without it, tangent circles
    /// produce zero-length slivers. Arcs shorter than the minimum sweep are then
    /// dropped outright, which catches the degenerate span at a tangency point.
    /// </summary>
    public sealed class DripLineTrimmer
    {
        private const double TwoPi = Math.PI * 2.0;

        /// <summary>Distance margin on the containment test, in drawing units.</summary>
        public double Tolerance { get; set; }

        /// <summary>Arcs sweeping less than this are discarded, in radians.</summary>
        public double MinimumSweep { get; set; }

        /// <summary>
        /// Grid-based neighbour lookup. On by default. Turning it off compares every
        /// pair, which is only useful for confirming the grid did not change the answer.
        /// </summary>
        public bool UseSpatialIndex { get; set; }

        public DripLineTrimmer()
        {
            Tolerance = 1e-6;
            MinimumSweep = 1e-4;
            UseSpatialIndex = true;
        }

        /// <summary>
        /// Trims every circle against every overlapping neighbour. Results come back in
        /// input order; identical input always produces identical output.
        /// </summary>
        public IList<DripTrimResult> TrimAll(IList<Circle2d> circles)
        {
            if (circles == null) throw new ArgumentNullException("circles");

            var results = new List<DripTrimResult>(circles.Count);
            var grid = UseSpatialIndex ? new CircleGrid(circles) : null;

            for (var i = 0; i < circles.Count; i++)
            {
                var neighbours = grid != null ? grid.NeighboursOf(i) : AllExcept(i, circles.Count);
                results.Add(Trim(i, circles, neighbours));
            }

            return results;
        }

        private static IList<int> AllExcept(int index, int count)
        {
            var all = new List<int>(count);
            for (var i = 0; i < count; i++)
                if (i != index) all.Add(i);
            return all;
        }

        private DripTrimResult Trim(int i, IList<Circle2d> circles, IList<int> candidates)
        {
            var result = new DripTrimResult { Index = i };
            var self = circles[i];

            if (self.R <= Tolerance)
            {
                result.FullyHidden = true;
                return result;
            }

            // Neighbours that actually cut the boundary, and any circle that swallows
            // this one whole.
            var cutters = new List<int>();
            var angles = new List<double>();

            for (var k = 0; k < candidates.Count; k++)
            {
                var j = candidates[k];
                if (j == i) continue;

                var other = circles[j];

                if (IsContainedIn(i, j, circles))
                {
                    result.FullyHidden = true;
                    return result;
                }

                double a1, a2;
                if (TryIntersectionAngles(self, other, out a1, out a2))
                {
                    angles.Add(a1);
                    angles.Add(a2);
                }

                // A neighbour that does not cross the boundary can still cover an arc
                // midpoint, so keep every overlapping circle for the containment test.
                if (Distance(self, other) < self.R + other.R + Tolerance)
                    cutters.Add(j);
            }

            if (angles.Count == 0)
            {
                // No boundary crossings. Either nothing touches it, or it is ringed by
                // circles that do not reach it. A single sample settles which.
                result.FullCircle = !IsPointCoveredByAny(self, 0.0, circles, cutters, i);
                result.FullyHidden = !result.FullCircle;
                return result;
            }

            angles.Sort();

            for (var k = 0; k < angles.Count; k++)
            {
                var start = angles[k];
                var end = (k + 1 < angles.Count) ? angles[k + 1] : angles[0] + TwoPi;

                var sweep = end - start;
                if (sweep <= MinimumSweep) continue;      // sliver at a tangency

                var mid = start + sweep / 2.0;
                if (IsPointCoveredByAny(self, mid, circles, cutters, i)) continue;

                result.Arcs.Add(new ArcSpan(Normalize(start), sweep));
            }

            result.FullyHidden = result.Arcs.Count == 0;
            return result;
        }

        // ------------------------------------------------------------------ geometry

        /// <summary>
        /// True when the point at <paramref name="angle"/> on <paramref name="self"/>
        /// lies inside any other circle. The tolerance shrinks each neighbour slightly,
        /// so a point exactly on a boundary reads as outside.
        /// </summary>
        private bool IsPointCoveredByAny(Circle2d self, double angle, IList<Circle2d> circles,
                                         IList<int> candidates, int selfIndex)
        {
            var px = self.X + self.R * Math.Cos(angle);
            var py = self.Y + self.R * Math.Sin(angle);

            for (var k = 0; k < candidates.Count; k++)
            {
                var j = candidates[k];
                if (j == selfIndex) continue;

                var c = circles[j];
                var dx = px - c.X;
                var dy = py - c.Y;
                var effective = c.R - Tolerance;
                if (effective <= 0) continue;

                if (dx * dx + dy * dy < effective * effective) return true;
            }

            return false;
        }

        /// <summary>
        /// The two angles, measured on <paramref name="a"/>, where the circles cross.
        /// False when they miss, nest, or merely touch.
        /// </summary>
        internal bool TryIntersectionAngles(Circle2d a, Circle2d b, out double a1, out double a2)
        {
            a1 = a2 = 0.0;

            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            var d = Math.Sqrt(dx * dx + dy * dy);

            if (d <= Tolerance) return false;                    // concentric
            if (d >= a.R + b.R - Tolerance) return false;        // apart or tangent
            if (d <= Math.Abs(a.R - b.R) + Tolerance) return false; // nested or tangent

            var x = (d * d + a.R * a.R - b.R * b.R) / (2.0 * d);
            var ratio = x / a.R;
            if (ratio > 1.0) ratio = 1.0;
            else if (ratio < -1.0) ratio = -1.0;

            var half = Math.Acos(ratio);
            var toward = Math.Atan2(dy, dx);

            a1 = Normalize(toward - half);
            a2 = Normalize(toward + half);
            return true;
        }

        /// <summary>
        /// True when circle i sits entirely inside circle j. Identical circles would
        /// otherwise each swallow the other and both would vanish, so ties resolve by
        /// index and the lowest one survives.
        /// </summary>
        private bool IsContainedIn(int i, int j, IList<Circle2d> circles)
        {
            var a = circles[i];
            var b = circles[j];
            var d = Distance(a, b);

            if (d + a.R > b.R + Tolerance) return false;

            var sameRadius = Math.Abs(a.R - b.R) <= Tolerance;
            if (sameRadius && d <= Tolerance) return j < i;

            return true;
        }

        private static double Distance(Circle2d a, Circle2d b)
        {
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        internal static double Normalize(double angle)
        {
            angle %= TwoPi;
            return angle < 0 ? angle + TwoPi : angle;
        }
    }

    /// <summary>
    /// Uniform grid over circle bounding boxes. Cells are sized to the largest circle,
    /// so any two overlapping circles are guaranteed to share at least one queried cell
    /// and neighbour lookup stays linear instead of comparing every pair.
    /// </summary>
    internal sealed class CircleGrid
    {
        private readonly IList<Circle2d> _circles;
        private readonly Dictionary<long, List<int>> _cells;
        private readonly double _cell;
        private readonly bool _degenerate;

        public CircleGrid(IList<Circle2d> circles)
        {
            _circles = circles;
            _cells = new Dictionary<long, List<int>>();

            if (circles.Count == 0)
            {
                _cell = 1.0;
                _degenerate = true;
                return;
            }

            var maxR = circles.Max(c => c.R);
            _cell = maxR > 0 ? maxR * 2.0 : 1.0;

            // Guard against coordinates so large that cell indices overflow the key.
            _degenerate = double.IsNaN(_cell) || double.IsInfinity(_cell) || _cell <= 0;
            if (_degenerate) return;

            for (var i = 0; i < circles.Count; i++)
                foreach (var key in KeysFor(circles[i]))
                {
                    List<int> bucket;
                    if (!_cells.TryGetValue(key, out bucket))
                    {
                        bucket = new List<int>();
                        _cells[key] = bucket;
                    }
                    bucket.Add(i);
                }
        }

        public IList<int> NeighboursOf(int index)
        {
            if (_degenerate) return AllExcept(index);

            var seen = new HashSet<int>();
            var result = new List<int>();

            foreach (var key in KeysFor(_circles[index]))
            {
                List<int> bucket;
                if (!_cells.TryGetValue(key, out bucket)) continue;

                for (var k = 0; k < bucket.Count; k++)
                {
                    var j = bucket[k];
                    if (j == index) continue;
                    if (seen.Add(j)) result.Add(j);
                }
            }

            // Deterministic order so the trimmed output never depends on hashing.
            result.Sort();
            return result;
        }

        private IList<int> AllExcept(int index)
        {
            var all = new List<int>(_circles.Count);
            for (var i = 0; i < _circles.Count; i++)
                if (i != index) all.Add(i);
            return all;
        }

        private IEnumerable<long> KeysFor(Circle2d c)
        {
            var minX = (int)Math.Floor((c.X - c.R) / _cell);
            var maxX = (int)Math.Floor((c.X + c.R) / _cell);
            var minY = (int)Math.Floor((c.Y - c.R) / _cell);
            var maxY = (int)Math.Floor((c.Y + c.R) / _cell);

            for (var gx = minX; gx <= maxX; gx++)
                for (var gy = minY; gy <= maxY; gy++)
                    yield return ((long)gx << 32) ^ (uint)gy;
        }
    }
}
