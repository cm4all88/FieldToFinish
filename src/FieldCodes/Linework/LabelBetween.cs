using System;
using System.Collections.Generic;

namespace FieldCodes.Linework
{
    /// <summary>
    /// One vertex of a source line as the drawing holds it. Z rides along so a 3D polyline's
    /// vertices are handed over exactly as read, but nothing in <see cref="PlanPath"/> or
    /// <see cref="LabelBetween"/> ever reads it: every distance, direction and position is
    /// measured in plan, so a line's elevation can neither move a label nor be changed by one.
    /// </summary>
    public struct PlanPoint
    {
        public PlanPoint(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public PlanPoint(double x, double y) : this(x, y, 0.0) { }

        public double X { get; }
        public double Y { get; }
        public double Z { get; }
    }

    /// <summary>The point of a <see cref="PlanPath"/> nearest some other point, in plan.</summary>
    public sealed class PlanFoot
    {
        public double X { get; set; }
        public double Y { get; set; }

        /// <summary>Distance along the path, in plan, from its first vertex.</summary>
        public double Station { get; set; }

        /// <summary>The segment it lies on: 0 runs from the first vertex to the second.</summary>
        public int Segment { get; set; }

        /// <summary>The path's direction there (radians, CCW from east), in the order the
        /// line was drawn.</summary>
        public double DirectionRadians { get; set; }

        /// <summary>How far, in plan, the point asked about is from the path.</summary>
        public double Distance { get; set; }
    }

    /// <summary>
    /// A source line's trace in plan: its vertices in the order it was drawn, arcs already
    /// broken into short chords by whoever read the line. Consecutive vertices that coincide
    /// in plan are dropped -- a 3D polyline's vertical step has no length on the plan and no
    /// direction to follow. Closed when the last vertex returns to the first.
    ///
    /// Pure geometry: built from numbers, never from an entity, so nothing done with it can
    /// touch the line it came from.
    /// </summary>
    public sealed class PlanPath
    {
        /// <summary>Vertices nearer than this in plan are the same vertex.</summary>
        private const double Coincident = 1e-8;

        /// <summary>A path whose ends are this close is closed.</summary>
        private const double ClosureTolerance = 1e-6;

        private readonly double[] _x;
        private readonly double[] _y;
        private readonly double[] _station;

        public PlanPath(IEnumerable<PlanPoint> points)
        {
            if (points == null) throw new ArgumentNullException("points");

            var xs = new List<double>();
            var ys = new List<double>();
            foreach (var p in points)
            {
                if (!Finite(p.X) || !Finite(p.Y)) continue;
                var n = xs.Count;
                if (n > 0 && Hypot(p.X - xs[n - 1], p.Y - ys[n - 1]) <= Coincident) continue;
                xs.Add(p.X);
                ys.Add(p.Y);
            }

            var count = xs.Count;
            if (count >= 4 && Hypot(xs[0] - xs[count - 1], ys[0] - ys[count - 1]) <= ClosureTolerance)
            {
                xs[count - 1] = xs[0];
                ys[count - 1] = ys[0];
                Closed = true;
            }

            _x = xs.ToArray();
            _y = ys.ToArray();
            _station = new double[count];
            for (var i = 1; i < count; i++)
                _station[i] = _station[i - 1] + Hypot(_x[i] - _x[i - 1], _y[i] - _y[i - 1]);
            Length = count > 0 ? _station[count - 1] : 0.0;
        }

        /// <summary>Vertices after coincident ones were dropped. A closed path repeats its
        /// first vertex at the end.</summary>
        public int Count { get { return _x.Length; } }

        public bool Closed { get; private set; }

        /// <summary>Length in plan.</summary>
        public double Length { get; private set; }

        public double XAt(int vertex) { return _x[vertex]; }
        public double YAt(int vertex) { return _y[vertex]; }
        public double StationAt(int vertex) { return _station[vertex]; }

        /// <summary>Direction of one segment, radians CCW from east.</summary>
        public double SegmentDirection(int segment)
        {
            return Math.Atan2(_y[segment + 1] - _y[segment], _x[segment + 1] - _x[segment]);
        }

        /// <summary>The nearest point of the path to (x, y), measured in plan. Null for a path
        /// with no vertices.</summary>
        public PlanFoot Closest(double x, double y)
        {
            var count = Count;
            if (count == 0) return null;
            if (count == 1)
                return new PlanFoot { X = _x[0], Y = _y[0], Distance = Hypot(x - _x[0], y - _y[0]) };

            PlanFoot best = null;
            for (var i = 0; i < count - 1; i++)
            {
                var dx = _x[i + 1] - _x[i];
                var dy = _y[i + 1] - _y[i];
                var lengthSquared = dx * dx + dy * dy;
                var t = ((x - _x[i]) * dx + (y - _y[i]) * dy) / lengthSquared;
                if (t < 0) t = 0;
                else if (t > 1) t = 1;

                var fx = _x[i] + t * dx;
                var fy = _y[i] + t * dy;
                var distance = Hypot(x - fx, y - fy);
                if (best != null && distance >= best.Distance - 1e-12) continue;

                best = new PlanFoot
                {
                    X = fx,
                    Y = fy,
                    Station = _station[i] + t * Math.Sqrt(lengthSquared),
                    Segment = i,
                    DirectionRadians = Math.Atan2(dy, dx),
                    Distance = distance
                };
            }
            return best;
        }

        /// <summary>The point at a station. A closed path wraps; an open one stops at its ends.</summary>
        public void PointAt(double station, out double x, out double y)
        {
            var count = Count;
            if (count == 0) { x = double.NaN; y = double.NaN; return; }
            if (count == 1) { x = _x[0]; y = _y[0]; return; }

            station = Closed ? Wrap(station) : Clamp(station, 0.0, Length);

            var segment = count - 2;
            for (var i = 0; i < count - 1; i++)
            {
                if (station <= _station[i + 1]) { segment = i; break; }
            }

            var span = _station[segment + 1] - _station[segment];
            var t = span <= 0 ? 0.0 : Clamp((station - _station[segment]) / span, 0.0, 1.0);
            x = _x[segment] + t * (_x[segment + 1] - _x[segment]);
            y = _y[segment] + t * (_y[segment + 1] - _y[segment]);
        }

        /// <summary>
        /// The stretch of the path from one station forward to another, as a path of its own.
        /// On a closed path a <paramref name="to"/> below <paramref name="from"/> runs on through
        /// the first vertex; on an open path the two are taken in order.
        /// </summary>
        public PlanPath Portion(double from, double to)
        {
            var points = new List<PlanPoint>();
            if (Count < 2) return new PlanPath(points);

            double start, end;
            if (Closed)
            {
                start = Wrap(from);
                end = Wrap(to);
                if (end <= start) end += Length;
            }
            else
            {
                start = Clamp(Math.Min(from, to), 0.0, Length);
                end = Clamp(Math.Max(from, to), 0.0, Length);
            }

            double x, y;
            PointAt(start, out x, out y);
            points.Add(new PlanPoint(x, y));

            var laps = Closed ? 2 : 1;
            for (var lap = 0; lap < laps; lap++)
            {
                for (var i = 0; i < Count; i++)
                {
                    var u = _station[i] + lap * Length;
                    if (u > start + Coincident && u < end - Coincident)
                        points.Add(new PlanPoint(_x[i], _y[i]));
                }
            }

            PointAt(end, out x, out y);
            points.Add(new PlanPoint(x, y));
            return new PlanPath(points);
        }

        /// <summary>
        /// The segments walked going forward from one station to another, as unrolled
        /// stations: on a closed path <paramref name="to"/> may run past <see cref="Length"/>
        /// into a second lap, so a walk through the first vertex stays in order.
        /// </summary>
        internal IList<PathPiece> Walk(double from, double to)
        {
            var pieces = new List<PathPiece>();
            var segments = Count - 1;
            if (segments < 1 || to <= from) return pieces;

            var laps = Closed ? 2 : 1;
            for (var lap = 0; lap < laps; lap++)
            {
                for (var i = 0; i < segments; i++)
                {
                    var lo = Math.Max(_station[i] + lap * Length, from);
                    var hi = Math.Min(_station[i + 1] + lap * Length, to);
                    if (hi - lo > Coincident) pieces.Add(new PathPiece(i, lo, hi));
                }
            }
            return pieces;
        }

        /// <summary>The signed change of direction from one segment to the next, (-180, 180]
        /// degrees in radians: positive turns left.</summary>
        internal double Turn(int fromSegment, int toSegment)
        {
            return Normalize(SegmentDirection(toSegment) - SegmentDirection(fromSegment));
        }

        internal double Wrap(double station)
        {
            if (Length <= 0) return 0.0;
            var wrapped = station % Length;
            if (wrapped < 0) wrapped += Length;
            return wrapped;
        }

        internal static double Normalize(double radians)
        {
            var angle = radians % (2.0 * Math.PI);
            if (angle <= -Math.PI) angle += 2.0 * Math.PI;
            else if (angle > Math.PI) angle -= 2.0 * Math.PI;
            return angle;
        }

        internal static double Hypot(double dx, double dy)
        {
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static double Clamp(double value, double lo, double hi)
        {
            return value < lo ? lo : value > hi ? hi : value;
        }

        private static bool Finite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }

    /// <summary>Part of one segment, between two unrolled stations.</summary>
    internal struct PathPiece
    {
        public PathPiece(int segment, double start, double end)
        {
            Segment = segment;
            Start = start;
            End = end;
        }

        public int Segment { get; }
        public double Start { get; }
        public double End { get; }
    }

    /// <summary>
    /// The two sides a between-label sits between, worked out from the two picks: each side is
    /// the run of linework around one pick. <see cref="Problem"/> says, in words for the
    /// drafter, why the picks cannot be labelled between; the sides are null then.
    /// </summary>
    public sealed class BetweenSides
    {
        /// <summary>The side the first pick named.</summary>
        public PlanPath SideA { get; internal set; }

        /// <summary>The side the second pick named.</summary>
        public PlanPath SideB { get; internal set; }

        /// <summary>Where the first pick landed on its line, in plan.</summary>
        public PlanFoot PickA { get; internal set; }

        /// <summary>Where the second pick landed on its line, in plan.</summary>
        public PlanFoot PickB { get; internal set; }

        /// <summary>Both picks were on one line: its two sides were split apart.</summary>
        public bool OneLine { get; internal set; }

        /// <summary>Null when the label can be placed.</summary>
        public string Problem { get; internal set; }
    }

    /// <summary>
    /// FTFLABELBETWEEN's geometry: a label centred between two picked parts of the linework --
    /// a driveway between its two edges, or a hedge drawn as ONE U-shaped 3D polyline between
    /// its two long sides.
    ///
    /// The picks are what matter, not the lines' overall shape. Each side is the linework
    /// around a pick; as the cursor moves the label sits at the midpoint of the nearest point
    /// on each side, reading along their averaged direction (one side flipped when the two
    /// were drawn in opposite directions, as the legs of a U always are) and never upside down
    /// -- the same <see cref="LineLabelPlanner.PlaceBetween"/> every between-label uses.
    ///
    /// Two picks on one line are split into two sides at the bend between them -- the bottom
    /// of a U, the crown of a horseshoe -- found by how far the line turns, not by distance.
    /// The bend belongs to neither side, so a U's label rides between its legs and is never
    /// drawn to the bottom of the U, where the middle of the whole polyline would put it.
    ///
    /// Everything is measured in plan (X and Y); Z is never read.
    /// </summary>
    public static class LabelBetween
    {
        /// <summary>Two picks on one line with less turning than this between them are on
        /// one straight run: there is nothing between them to label.</summary>
        public static readonly double SameRunTurnRadians = 15.0 * Math.PI / 180.0;

        /// <summary>A line turning less than this between the picks (an S or a jog) is split
        /// half way along, by distance.</summary>
        private static readonly double SplitByTurnRadians = 30.0 * Math.PI / 180.0;

        /// <summary>Segments within this of the half-way direction are the middle of the bend.</summary>
        private static readonly double MiddleBandRadians = 30.0 * Math.PI / 180.0;

        private const double SameSpot = 1e-6;

        /// <summary>Two separate lines: each whole line is one side.</summary>
        public static BetweenSides TwoLines(PlanPath first, double firstX, double firstY,
                                            PlanPath second, double secondX, double secondY)
        {
            if (first == null) throw new ArgumentNullException("first");
            if (second == null) throw new ArgumentNullException("second");

            if (first.Count < 2 || first.Length <= SameSpot)
                return Refused("The first edge has no length in plan (a vertical line?).");
            if (second.Count < 2 || second.Length <= SameSpot)
                return Refused("The second edge has no length in plan (a vertical line?).");

            var pickA = first.Closest(firstX, firstY);
            var pickB = second.Closest(secondX, secondY);
            if (PlanPath.Hypot(pickA.X - pickB.X, pickA.Y - pickB.Y) <= SameSpot)
                return Refused("The two edges meet where they were picked. Pick each edge " +
                               "away from where they touch.");

            return new BetweenSides { SideA = first, SideB = second, PickA = pickA, PickB = pickB };
        }

        /// <summary>
        /// Two picks on ONE line -- the two legs of a U, two sides of a closed outline. The line
        /// is split into the run around each pick; picks on one straight run are refused.
        /// </summary>
        public static BetweenSides OneLine(PlanPath line, double firstX, double firstY,
                                           double secondX, double secondY)
        {
            if (line == null) throw new ArgumentNullException("line");
            if (line.Count < 2 || line.Length <= SameSpot)
                return Refused("That line has no length in plan (a vertical line?).");

            var pickA = line.Closest(firstX, firstY);
            var pickB = line.Closest(secondX, secondY);
            var sameRun = "Both picks are on the same straight run of that line. Pick the " +
                          "other side of the feature -- another part of the same line is fine.";

            double firstEnds, secondStarts;
            if (!line.Closed)
            {
                var low = Math.Min(pickA.Station, pickB.Station);
                var high = Math.Max(pickA.Station, pickB.Station);
                if (high - low <= SameSpot || AbsoluteTurn(line, low, high) < SameRunTurnRadians)
                    return Refused(sameRun);

                // The earlier pick's side runs from the start of the line to the bend; the
                // later pick's from the bend to the end.
                Bend(line, low, high, out firstEnds, out secondStarts);
                var before = line.Portion(0.0, firstEnds);
                var after = line.Portion(secondStarts, line.Length);
                var aFirst = pickA.Station <= pickB.Station;

                return new BetweenSides
                {
                    SideA = aFirst ? before : after,
                    SideB = aFirst ? after : before,
                    PickA = pickA,
                    PickB = pickB,
                    OneLine = true
                };
            }

            // Closed: the picks cut the loop into two connecting runs, A on to B and B on
            // round to A, each with its own bend; each side runs between the two bends.
            var a = line.Wrap(pickA.Station);
            var b = line.Wrap(pickB.Station);
            var forward = b >= a ? b - a : b + line.Length - a;
            if (forward <= SameSpot || forward >= line.Length - SameSpot)
                return Refused(sameRun);

            var aToB = a + forward;                 // unrolled
            var bToA = aToB + (line.Length - forward);
            if (Math.Min(AbsoluteTurn(line, a, aToB), AbsoluteTurn(line, aToB, bToA)) < SameRunTurnRadians)
                return Refused(sameRun);

            double aEnds, bStarts, bEnds, aStarts;
            Bend(line, a, aToB, out aEnds, out bStarts);
            Bend(line, aToB, bToA, out bEnds, out aStarts);

            return new BetweenSides
            {
                SideA = line.Portion(aStarts, aEnds),
                SideB = line.Portion(bStarts, bEnds),
                PickA = pickA,
                PickB = pickB,
                OneLine = true
            };
        }

        /// <summary>
        /// The label for a cursor position: centred between the nearest point of each side,
        /// along their averaged direction, readable. What the preview shows is what a click
        /// places.
        /// </summary>
        public static PlannedLineLabel Place(BetweenSides sides, double cursorX, double cursorY,
                                             bool alignToLine)
        {
            if (sides == null) throw new ArgumentNullException("sides");
            if (sides.Problem != null) throw new InvalidOperationException(sides.Problem);

            var a = sides.SideA.Closest(cursorX, cursorY);
            var b = sides.SideB.Closest(cursorX, cursorY);
            return LineLabelPlanner.PlaceBetween(a.X, a.Y, a.DirectionRadians,
                                                 b.X, b.Y, b.DirectionRadians, alignToLine);
        }

        /// <summary>Where the label starts before the cursor moves: centred between the picked
        /// places -- between the legs of a U at the height they were picked, never at the
        /// middle of the whole line.</summary>
        public static PlannedLineLabel Start(BetweenSides sides, bool alignToLine)
        {
            if (sides == null) throw new ArgumentNullException("sides");
            if (sides.Problem != null) throw new InvalidOperationException(sides.Problem);

            return Place(sides, (sides.PickA.X + sides.PickB.X) / 2.0,
                         (sides.PickA.Y + sides.PickB.Y) / 2.0, alignToLine);
        }

        /// <summary>
        /// The bend between two picks on one line (unrolled stations, from the earlier pick
        /// to the later): where the earlier pick's side stops and the later pick's begins.
        /// A side runs on as long as it still heads within a quarter of the total turn of its
        /// own pick's way; what lies between is the bend -- the bottom of a U, the crown of a
        /// horseshoe -- and belongs to neither side, so the label rides between the legs right
        /// up to it instead of being pulled onto it. Two straight sides meeting at a corner
        /// (a V) have no bend: both sides end at the corner. A line that hardly turns overall
        /// (an S, a jog) is split half way along.
        /// </summary>
        internal static void Bend(PlanPath line, double from, double to,
                                  out double firstEnds, out double secondStarts)
        {
            firstEnds = secondStarts = (from + to) / 2.0;

            var pieces = line.Walk(from, to);
            if (pieces.Count == 0) return;

            var turned = Turned(line, pieces);
            var total = turned[pieces.Count - 1];
            if (Math.Abs(total) < SplitByTurnRadians) return;

            var quarter = Math.Abs(total) / 4.0;
            int lastOfFirst = -1, firstOfSecond = -1;
            for (var k = 0; k < pieces.Count; k++)
            {
                if (Math.Abs(turned[k]) <= quarter) lastOfFirst = k;
                if (firstOfSecond < 0 && Math.Abs(turned[k] - total) <= quarter) firstOfSecond = k;
            }

            if (lastOfFirst >= 0 && firstOfSecond >= 0 && lastOfFirst < firstOfSecond)
            {
                firstEnds = pieces[lastOfFirst].End;
                secondStarts = pieces[firstOfSecond].Start;
                return;
            }

            // A line that wanders back and forth: one split, where it has turned half way.
            firstEnds = secondStarts = SplitStation(line, from, to);
        }

        /// <summary>
        /// Where a line is split between two picks (unrolled stations, from before to): the
        /// middle of the stretch whose direction is half way between the two picks' -- the
        /// bottom of a U, the crown of a horseshoe -- or the corner where the line swings past
        /// half way. A line that hardly turns overall (an S, a jog) is split half way along.
        /// </summary>
        internal static double SplitStation(PlanPath line, double from, double to)
        {
            var middle = (from + to) / 2.0;
            var pieces = line.Walk(from, to);
            if (pieces.Count == 0) return middle;

            var turned = Turned(line, pieces);
            var total = turned[pieces.Count - 1];
            if (Math.Abs(total) < SplitByTurnRadians) return middle;

            var half = total / 2.0;
            var band = Math.Min(MiddleBandRadians, Math.Abs(total) / 4.0);

            int firstMiddle = -1, lastMiddle = -1;
            for (var k = 0; k < pieces.Count; k++)
            {
                if (Math.Abs(turned[k] - half) > band) continue;
                if (firstMiddle < 0) firstMiddle = k;
                lastMiddle = k;
            }
            if (firstMiddle >= 0)
                return (pieces[firstMiddle].Start + pieces[lastMiddle].End) / 2.0;

            for (var k = 1; k < pieces.Count; k++)
            {
                if ((turned[k - 1] - half) * (turned[k] - half) <= 0)
                    return pieces[k].Start;
            }
            return middle;
        }

        /// <summary>How far the line has turned (signed) by each piece of a walk.</summary>
        private static double[] Turned(PlanPath line, IList<PathPiece> pieces)
        {
            var turned = new double[pieces.Count];
            for (var k = 1; k < pieces.Count; k++)
                turned[k] = turned[k - 1] + line.Turn(pieces[k - 1].Segment, pieces[k].Segment);
            return turned;
        }

        /// <summary>How much the line turns, left and right alike, going from one station to
        /// another (unrolled).</summary>
        internal static double AbsoluteTurn(PlanPath line, double from, double to)
        {
            var pieces = line.Walk(from, to);
            var sum = 0.0;
            for (var k = 1; k < pieces.Count; k++)
                sum += Math.Abs(line.Turn(pieces[k - 1].Segment, pieces[k].Segment));
            return sum;
        }

        private static BetweenSides Refused(string problem)
        {
            return new BetweenSides { Problem = problem };
        }
    }
}
