using System;
using System.Collections.Generic;
using System.Linq;
using FieldCodes.Easements;

namespace FieldCodes.Orient
{
    /// <summary>One road leg at an intersection: traffic on it arrives travelling <see cref="Inbound"/>.</summary>
    public sealed class Approach
    {
        public Intersection Node { get; set; }
        public OrientLine Axis { get; set; }

        /// <summary>Unit direction from the node out along the leg.</summary>
        public P2 Outward { get; set; }
        public P2 Inbound { get { return Outward * -1; } }

        /// <summary>A one-way leg drawn away from the node carries no inbound traffic.</summary>
        public bool InboundAllowed { get; set; }

        public string Name { get { return Axis.Id + " " + Compass(SymbolRotation.AzimuthOf(Outward)) + " leg"; } }

        internal static string Compass(double az)
        {
            var names = new[] { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
            return names[(int)Math.Round(az / 45.0) % 8];
        }
    }

    /// <summary>Where road centre lines meet or would meet.</summary>
    public sealed class Intersection
    {
        public P2 Center { get; set; }
        public IList<Approach> Approaches { get; set; }
        public Intersection() { Approaches = new List<Approach>(); }
    }

    /// <summary>A stop bar and the travel direction of the traffic it stops, when known.</summary>
    public sealed class StopBar
    {
        public OrientLine Line { get; set; }
        public P2 Mid { get; set; }
        public P2 Along { get; set; }
        public P2? Travel { get; set; }
        public string TravelFrom { get; set; }
        public Approach Approach { get; set; }
    }

    /// <summary>A direction of travel at a point, and how it was known.</summary>
    /// <summary>
    /// A road drawn by its edges only, meeting a road that has a centre line: the edge a point
    /// stands next to runs across that centre line rather than along it. Common on rural
    /// surveys, where only the main road's stripe is shot.
    /// </summary>
    public sealed class SideRoad
    {
        public OrientLine Edge { get; set; }
        /// <summary>The centre line the side road comes up to.</summary>
        public OrientLine Meets { get; set; }
        public P2 Foot { get; set; }
        public P2 Tangent { get; set; }
        /// <summary>Along the side road, toward the road it meets: the way approaching traffic goes.</summary>
        public P2 TowardMeets { get; set; }
        public double Distance { get; set; }
        public double MeetsDistance { get; set; }
    }

    /// <summary>
    /// A road line near a point, as a direction reference: where it passes the point (on the
    /// line, or on its extension past an end), its local direction there as drawn, how far it is,
    /// how far it had to be followed past its end, and how much it is trusted. Read only: no
    /// linework is extended or changed.
    /// </summary>
    public sealed class RoadReference
    {
        public OrientLine Line { get; set; }
        public LineRole Role { get { return Line.Role; } }
        public P2 Foot { get; set; }
        /// <summary>Unit direction of the line where it passes the point, as drawn (start to end).</summary>
        public P2 Tangent { get; set; }
        public double Distance { get; set; }
        public double Station { get; set; }
        /// <summary>How far past the line's end the point is, along the line's last stretch (0: beside it).</summary>
        public double Extension { get; set; }
        /// <summary>Set when the same kind of line resumes on the far side: the point is in a gap.</summary>
        public OrientLine BridgedTo { get; set; }
        public double Reliability { get; set; }
        public double Score { get; set; }

        public string Describe()
        {
            var what = RoadModel.RoleName(Role) + " " + Line.Id;
            if (BridgedTo != null)
                return string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0} ({1:0.0} ft away, in a gap of {2:0} ft or more before {3} resumes)",
                                     what, Distance, Extension, BridgedTo.Id);
            if (Extension > 0)
                return string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0} ({1:0.0} ft away, followed {2:0.0} ft past its end)",
                                     what, Distance, Extension);
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0} ({1:0.0} ft away)", what, Distance);
        }
    }

    public sealed class TravelAt
    {
        public P2 Direction { get; set; }
        public OrientationTier Tier { get; set; }
        public double Confidence { get; set; }
        public string Why { get; set; }
        public OrientLine Line { get; set; }
        public double Distance { get; set; }
        /// <summary>Set when the traffic is on a side road drawn by its edges only.</summary>
        public SideRoad SideRoad { get; set; }
        /// <summary>The road reference the direction came from (null for a side road).</summary>
        public RoadReference Reference { get; set; }
        /// <summary>Set when road lines near the point disagree about the road's direction: the
        /// feature goes to review instead of one line being picked.</summary>
        public string Conflict { get; set; }
        /// <summary>The direction the disagreeing line would give, for the review.</summary>
        public P2? ConflictDirection { get; set; }
    }

    /// <summary>
    /// The road network as surveyed: centre lines (two-way, traffic keeps right), one-way
    /// lines (drawn in the direction of travel), edges, lane lines, stop bars, crosswalks.
    /// Built once per run; only read.
    /// </summary>
    public sealed class RoadModel
    {
        private readonly OrientationStandard _std;
        public IList<OrientLine> Axes { get; private set; }
        public IList<OrientLine> Edges { get; private set; }
        public IList<StopBar> StopBars { get; private set; }
        public IList<OrientLine> Crosswalks { get; private set; }
        /// <summary>Edges of concrete, sidewalks: road context, supporting only.</summary>
        public IList<OrientLine> Context { get; private set; }
        public IList<Intersection> Intersections { get; private set; }

        public RoadModel(IEnumerable<OrientLine> lines, OrientationStandard std)
        {
            _std = std;
            var all = (lines ?? Enumerable.Empty<OrientLine>()).Where(l => l != null && l.Points.Count >= 2).ToList();
            Axes = all.Where(l => l.Role == LineRole.Centerline || l.Role == LineRole.OneWay).ToList();
            Edges = all.Where(l => l.Role == LineRole.Edge || l.Role == LineRole.LaneLine).ToList();
            Crosswalks = all.Where(l => l.Role == LineRole.Crosswalk).ToList();
            Context = all.Where(l => l.Role == LineRole.RoadContext).ToList();
            Intersections = FindIntersections();
            StopBars = all.Where(l => l.Role == LineRole.StopBar).Select(BuildStopBar).ToList();
        }

        // ------------------------------------------------------------------ intersections

        private const double RayReach = 150;     // how far a centre line is followed past its end
        private const double NodeMerge = 40;

        private List<Intersection> FindIntersections()
        {
            var candidates = new List<P2>();
            for (var i = 0; i < Axes.Count; i++)
                for (var j = i + 1; j < Axes.Count; j++)
                {
                    var a = Axes[i];
                    var b = Axes[j];
                    candidates.AddRange(Crossings(a, b));
                    foreach (var ray in EndRays(a))
                        foreach (var hit in RayHits(ray.Item1, ray.Item2, b)) candidates.Add(hit);
                    foreach (var ray in EndRays(b))
                        foreach (var hit in RayHits(ray.Item1, ray.Item2, a)) candidates.Add(hit);
                    foreach (var ra in EndRays(a))
                        foreach (var rb in EndRays(b))
                        {
                            P2 x;
                            if (RayRay(ra.Item1, ra.Item2, rb.Item1, rb.Item2, out x)) candidates.Add(x);
                        }
                }

            var nodes = new List<List<P2>>();
            foreach (var c in candidates)
            {
                var home = nodes.FirstOrDefault(n => Centroid(n).DistanceTo(c) < NodeMerge);
                if (home != null) home.Add(c); else nodes.Add(new List<P2> { c });
            }

            var result = new List<Intersection>();
            foreach (var n in nodes)
            {
                var node = new Intersection { Center = Centroid(n) };
                foreach (var axis in Axes) AddLegs(node, axis);
                // A node is a real intersection only when at least three legs, or two legs of
                // different roads at an angle, meet there.
                var distinctAxes = node.Approaches.Select(a => a.Axis).Distinct().Count();
                if (node.Approaches.Count >= 3 || (distinctAxes >= 2 && node.Approaches.Count >= 2))
                    result.Add(node);
            }
            return result;
        }

        private void AddLegs(Intersection node, OrientLine axis)
        {
            P2 foot, tangent; double d, station;
            if (!axis.Nearest(node.Center, out foot, out tangent, out d, out station)) return;
            var oneWay = axis.Role == LineRole.OneWay;

            // The line passes through the node: two legs.
            if (d < 30 && station > 20 && station < axis.Length - 20)
            {
                node.Approaches.Add(new Approach { Node = node, Axis = axis, Outward = tangent, InboundAllowed = !oneWay });
                node.Approaches.Add(new Approach { Node = node, Axis = axis, Outward = tangent * -1, InboundAllowed = true });
                return;
            }

            // The line ends short of the node, pointing at it: one leg.
            foreach (var end in new[] { Tuple.Create(axis.Start, axis.StartDirection * -1, true),
                                        Tuple.Create(axis.End, axis.EndDirection, false) })
            {
                var toNode = node.Center - end.Item1;
                var dist = toNode.Length;
                if (dist > RayReach || dist < 1e-6) continue;
                var along = P2.Dot(toNode.Normalized(), end.Item2);
                if (along < Math.Cos(Math.PI / 6) && dist > 30) continue;
                // Traffic on a one-way line travels in the drawn direction: it arrives at a node
                // ahead of the line's end, and leaves toward a node behind its start.
                var isStartEnd = end.Item3;
                node.Approaches.Add(new Approach
                {
                    Node = node, Axis = axis, Outward = end.Item2 * -1, InboundAllowed = !oneWay || !isStartEnd
                });
            }
        }

        private static IEnumerable<Tuple<P2, P2>> EndRays(OrientLine l)
        {
            yield return Tuple.Create(l.Start, l.StartDirection * -1);
            yield return Tuple.Create(l.End, l.EndDirection);
        }

        private static IEnumerable<P2> Crossings(OrientLine a, OrientLine b)
        {
            for (var i = 1; i < a.Points.Count; i++)
                for (var j = 1; j < b.Points.Count; j++)
                {
                    P2 x;
                    if (SegSeg(a.Points[i - 1], a.Points[i], b.Points[j - 1], b.Points[j], out x)) yield return x;
                }
        }

        private static IEnumerable<P2> RayHits(P2 o, P2 d, OrientLine l)
        {
            for (var j = 1; j < l.Points.Count; j++)
            {
                P2 x;
                if (SegSeg(o, o + d * RayReach, l.Points[j - 1], l.Points[j], out x)) yield return x;
            }
        }

        private static bool RayRay(P2 o1, P2 d1, P2 o2, P2 d2, out P2 x)
        {
            x = default(P2);
            var den = P2.Cross(d1, d2);
            if (Math.Abs(den) < Math.Sin(Math.PI / 9)) return false;         // under 20 degrees: not a crossing
            var t = P2.Cross(o2 - o1, d2) / den;
            var u = P2.Cross(o2 - o1, d1) / den;
            if (t < 0 || u < 0 || t > RayReach || u > RayReach) return false;
            x = o1 + d1 * t;
            return true;
        }

        private static bool SegSeg(P2 a, P2 b, P2 c, P2 d, out P2 x)
        {
            x = default(P2);
            var r = b - a;
            var s = d - c;
            var den = P2.Cross(r, s);
            if (Math.Abs(den) < 1e-9) return false;
            var t = P2.Cross(c - a, s) / den;
            var u = P2.Cross(c - a, r) / den;
            if (t < 0 || t > 1 || u < 0 || u > 1) return false;
            var angle = Math.Abs(den) / (r.Length * s.Length);
            if (angle < Math.Sin(Math.PI / 9)) return false;
            x = a + r * t;
            return true;
        }

        private static P2 Centroid(IList<P2> ps)
        {
            return new P2(ps.Average(p => p.X), ps.Average(p => p.Y));
        }

        // ------------------------------------------------------------------ stop bars

        private StopBar BuildStopBar(OrientLine line)
        {
            var mid = line.Start + (line.End - line.Start) * 0.5;
            var bar = new StopBar { Line = line, Mid = mid, Along = (line.End - line.Start).Normalized() };

            // Traffic crosses a stop bar heading into the intersection beyond it.
            var node = Intersections.Where(n => n.Center.DistanceTo(mid) < _std.IntersectionSearchFeet)
                                    .OrderBy(n => n.Center.DistanceTo(mid)).FirstOrDefault();
            if (node != null)
            {
                var toNode = (node.Center - mid).Normalized();
                var leg = node.Approaches.Where(a => a.InboundAllowed)
                              .OrderByDescending(a => P2.Dot(a.Inbound, toNode)).FirstOrDefault();
                if (leg != null && Math.Abs(P2.Dot(leg.Inbound, bar.Along)) < 0.71)      // bars skewed up to 45 degrees
                {
                    bar.Travel = leg.Inbound;
                    bar.Approach = leg;
                    bar.TravelFrom = "toward the intersection at " + node.Center;
                    return bar;
                }
            }

            // A crosswalk just past a stop bar is on the far side.
            var cross = Crosswalks.Select(c => { P2 f, t; double d, s; c.Nearest(mid, out f, out t, out d, out s); return Tuple.Create(c, f, d); })
                                  .Where(c => c.Item3 < 40).OrderBy(c => c.Item3).FirstOrDefault();
            var normal = bar.Along.LeftNormal();
            if (cross != null)
            {
                var toward = cross.Item2 - mid;
                var dir = P2.Dot(toward, normal) >= 0 ? normal : normal * -1;
                bar.Travel = dir;
                bar.TravelFrom = "toward the crosswalk beyond it";
                return bar;
            }

            // Otherwise a two-way centre line: the bar spans the lane on the right of its traffic.
            var travel = TravelAtPoint(mid, true);
            if (travel != null && travel.Conflict == null && travel.Tier <= OrientationTier.Alignment && Math.Abs(P2.Dot(travel.Direction, bar.Along)) < 0.71)
            {
                bar.Travel = travel.Direction;
                bar.TravelFrom = travel.Why;
            }
            return bar;
        }

        // ------------------------------------------------------------------ road references

        internal static string RoleName(LineRole role)
        {
            switch (role)
            {
                case LineRole.Centerline: return "centre line";
                case LineRole.OneWay: return "one-way line";
                case LineRole.LaneLine: return "lane line";
                case LineRole.Edge: return "road edge";
                case LineRole.RoadContext: return "concrete edge";
                default: return role.ToString();
            }
        }

        private static bool IsAxis(LineRole role) { return role == LineRole.Centerline || role == LineRole.OneWay; }

        /// <summary>
        /// Every road line within <paramref name="reach"/> of the point as a direction reference,
        /// best first. A line that stops short of the point is followed past its end along its
        /// last stretch, at most maxExtensionFeet; further than that it is no reference at all.
        /// The direction is the line's local tangent over localTangentFeet, so a curve gives the
        /// direction where the point is. Scored by how far the kind of line is trusted, how near it
        /// is, how far it had to be followed, and how long it is (a short piece, such as a
        /// driveway's edge, says less about the road than a long one).
        /// </summary>
        public IList<RoadReference> References(P2 p, double reach, bool includeContext = true)
        {
            var found = new List<RoadReference>();
            var lines = Axes.Concat(Edges);
            if (includeContext) lines = lines.Concat(Context);
            foreach (var line in lines)
            {
                var r = ReferenceOn(line, p, reach);
                if (r != null) found.Add(r);
            }
            Bridge(found);
            return found.OrderByDescending(r => r.Score).ToList();
        }

        internal RoadReference ReferenceOn(OrientLine line, P2 p, double reach)
        {
            P2 foot, segment; double d, s;
            if (!line.Nearest(p, out foot, out segment, out d, out s)) return null;
            var reliability = _std.ReliabilityOf(line.Role);
            if (reliability <= 0) return null;
            var length = line.Length;
            var window = Math.Max(1.0, _std.LocalTangentFeet);
            var extension = 0.0;
            P2 tangent;
            var atStart = s <= 1e-6;
            var atEnd = s >= length - 1e-6;
            if (atStart || atEnd)
            {
                // Past an end: the line's last stretch, followed outward.
                var end = atEnd ? line.End : line.Start;
                var drawn = atEnd ? line.DirectionOver(Math.Max(0, length - window), length) : line.DirectionOver(0, Math.Min(length, window));
                var asDrawn = drawn ?? segment;
                var outward = atEnd ? asDrawn : asDrawn * -1;
                var rel = p - end;
                var along = P2.Dot(rel, outward);
                if (along > 1e-6)
                {
                    extension = along;
                    if (extension > _std.MaxExtensionFeet) return null;
                    foot = end + outward * along;
                    d = Math.Abs(P2.Cross(outward, rel));
                }
                tangent = asDrawn;
            }
            else
            {
                var half = window / 2;
                tangent = line.DirectionOver(Math.Max(0, s - half), Math.Min(length, s + half)) ?? segment;
            }
            if (d > reach) return null;
            var reference = new RoadReference
            {
                Line = line, Foot = foot, Tangent = tangent, Distance = d, Station = s, Extension = extension, Reliability = reliability
            };
            reference.Score = BaseScore(reference) * ExtensionFactor(extension);
            return reference;
        }

        private static double BaseScore(RoadReference r)
        {
            return r.Reliability * (1.0 / (1.0 + r.Distance / 40.0)) * Math.Max(0.4, Math.Min(1.0, r.Line.Length / 50.0));
        }

        /// <summary>1 beside a line, falling to 0.6 at the most a line may be followed past its end.</summary>
        internal double ExtensionFactor(double extension)
        {
            if (extension <= 0) return 1.0;
            if (_std.MaxExtensionFeet <= 0) return 0;
            return 1.0 - 0.4 * Math.Min(1.0, extension / _std.MaxExtensionFeet);
        }

        /// <summary>A line followed past its end whose kind resumes on the far side of the point,
        /// running the same way and about in line: the point is in a gap (a driveway, a missed
        /// shot), and the line is trusted as if it ran through.</summary>
        private void Bridge(IList<RoadReference> found)
        {
            var tolerance = Math.Cos(Math.Max(1, _std.AlignmentToleranceDegrees) * Math.PI / 180);
            foreach (var a in found.Where(x => x.Extension > 0).ToList())
                foreach (var b in found.Where(x => x != a && x.Extension > 0 && IsAxis(x.Role) == IsAxis(a.Role)))
                {
                    if (Math.Abs(P2.Dot(a.Tangent, b.Tangent)) < tolerance) continue;
                    // Each was followed toward the point from its own end: from opposite sides.
                    var fromA = a.Foot - a.Line.PointAt(a.Station);
                    var fromB = b.Foot - b.Line.PointAt(b.Station);
                    if (P2.Dot(fromA, fromB) >= 0) continue;
                    if (Math.Abs(a.Distance - b.Distance) > 3.0) continue;
                    a.BridgedTo = b.Line;
                    a.Score = BaseScore(a);
                }
        }

        /// <summary>Confidence a reference's direction deserves: less the further it was followed
        /// past its end (unless the line resumes beyond), less beyond 30 ft away.</summary>
        internal double ConfidenceFactor(RoadReference r)
        {
            var f = r.BridgedTo != null ? 1.0 : ExtensionFactor(r.Extension);
            if (r.Distance > 30) f *= Math.Max(0.7, 1.0 - 0.3 * (r.Distance - 30) / 50.0);
            return f;
        }

        /// <summary>
        /// A road line near the point that runs a different way from <paramref name="chosen"/> by
        /// more than the alignment tolerance and is trusted nearly as much: the road's direction is
        /// then not established, and the feature should be reviewed. Null when they agree.
        /// Concrete edges and sidewalks never raise a disagreement: they only support.
        /// </summary>
        public RoadReference Disagreeing(RoadReference chosen, IList<RoadReference> all)
        {
            if (chosen == null || all == null) return null;
            var tolerance = Math.Cos(Math.Max(1, _std.AlignmentToleranceDegrees) * Math.PI / 180);
            return all.Where(r => r != chosen && r.Line != chosen.Line && r.Role != LineRole.RoadContext && chosen.Role != LineRole.RoadContext)
                      .Where(r => r.Score >= chosen.Score * 0.6 && r.Distance <= chosen.Distance * 1.5 + 10)
                      .Where(r => Math.Abs(P2.Dot(r.Tangent, chosen.Tangent)) < tolerance)
                      .OrderByDescending(r => r.Score).FirstOrDefault();
        }

        /// <summary>Other road lines that run the same way as the chosen one.</summary>
        public IList<RoadReference> Agreeing(RoadReference chosen, IList<RoadReference> all)
        {
            if (chosen == null || all == null) return new List<RoadReference>();
            var tolerance = Math.Cos(Math.Max(1, _std.AlignmentToleranceDegrees) * Math.PI / 180);
            return all.Where(r => r != chosen && r.Line != chosen.Line && Math.Abs(P2.Dot(r.Tangent, chosen.Tangent)) >= tolerance).ToList();
        }

        /// <summary>The road's axis at a point, as drawn on the best road line there (centre lines
        /// first): for valves and other features that need only the road's direction.</summary>
        public P2? RoadAxisAt(P2 p, double reach)
        {
            var refs = References(p, reach, false);
            var best = refs.FirstOrDefault(r => IsAxis(r.Role)) ?? refs.FirstOrDefault();
            return best == null ? (P2?)null : best.Tangent;
        }

        /// <summary>Where a perpendicular from the point meets the nearest road edge (else centre
        /// line), on a line's extension only within the limit.</summary>
        public P2? RoadFootAt(P2 p, double reach)
        {
            var refs = References(p, reach, false);
            var best = refs.Where(r => !IsAxis(r.Role)).OrderBy(r => r.Distance).FirstOrDefault() ??
                       refs.Where(r => IsAxis(r.Role)).OrderBy(r => r.Distance).FirstOrDefault();
            return best == null ? (P2?)null : best.Foot;
        }

        // ------------------------------------------------------------------ queries

        /// <summary>The nearest road centre line or one-way line within the search distance.</summary>
        public OrientLine NearestAxis(P2 p, double within, out P2 foot, out P2 tangent, out double distance)
        {
            return Nearest(Axes, p, within, out foot, out tangent, out distance);
        }

        public OrientLine NearestEdge(P2 p, double within, out P2 foot, out P2 tangent, out double distance)
        {
            return Nearest(Edges, p, within, out foot, out tangent, out distance);
        }

        private static OrientLine Nearest(IEnumerable<OrientLine> lines, P2 p, double within, out P2 foot, out P2 tangent, out double distance)
        {
            foot = default(P2); tangent = default(P2); distance = double.MaxValue;
            OrientLine best = null;
            foreach (var l in lines)
            {
                P2 f, t; double d, s;
                if (!l.Nearest(p, out f, out t, out d, out s) || d > within || d >= distance) continue;
                best = l; foot = f; tangent = t; distance = d;
            }
            return best;
        }

        /// <summary>A point this close to an edge that runs across the nearest centre line
        /// (and at most half as far from it as from the centre line) is beside a side road.</summary>
        internal const double SideRoadEdgeFeet = 30;

        /// <summary>Edges more than this far off the centre line's direction belong to another road.</summary>
        internal const double SideRoadAngleDegrees = 50;

        /// <summary>
        /// The side road a point stands beside, when that road has edges but no centre line of
        /// its own: the nearest edge is much nearer than the nearest centre line and runs across
        /// it. Null when the point is beside the centre line's own road.
        /// </summary>
        public SideRoad SideRoadAt(P2 p)
        {
            P2 af, at; double ad;
            var axis = NearestAxis(p, _std.RoadSearchFeet, out af, out at, out ad);
            if (axis == null) return null;
            P2 ef, et; double ed;
            var edge = NearestEdge(p, Math.Min(SideRoadEdgeFeet, ad * 0.5), out ef, out et, out ed);
            if (edge == null) return null;

            // The centre line's direction where it passes nearest the edge, not nearest the point:
            // on a bend those differ.
            P2 af2, at2; double ad2, s2;
            if (!axis.Nearest(ef, out af2, out at2, out ad2, out s2)) return null;
            if (Math.Abs(P2.Dot(et, at2)) > Math.Cos(SideRoadAngleDegrees * Math.PI / 180)) return null;

            Func<P2, double> toAxis = q => { P2 f, t; double d, s; axis.Nearest(q, out f, out t, out d, out s); return d; };
            var toward = toAxis(ef + et * 10) <= toAxis(ef - et * 10) ? et : et * -1;
            return new SideRoad { Edge = edge, Meets = axis, Foot = ef, Tangent = et, TowardMeets = toward, Distance = ed, MeetsDistance = ad };
        }

        /// <summary>
        /// The direction traffic travels past a point. A one-way line gives it outright. A
        /// two-way centre line gives it by keeping right: the point is on the right of the
        /// traffic beside it. Edges alone give it the same way from the nearest edge, with less
        /// confidence (a one-way road would break the assumption). Null when no road is near.
        /// </summary>
        /// <summary>Every road line (centre lines, one-way lines, lane lines, edges) within
        /// <paramref name="reach"/> of the point: the line, its direction at the nearest point
        /// (as drawn), and the distance.</summary>
        public IList<Tuple<OrientLine, P2, double>> LinesNear(P2 p, double reach)
        {
            // Through the references: the local direction where the point is, and a line that
            // stops short only within the extension limit.
            return References(p, reach, false).Select(r => Tuple.Create(r.Line, r.Tangent, r.Distance)).ToList();
        }

        public TravelAt TravelAtPoint(P2 p, bool insideRoad = false)
        {
            P2 foot, tangent; double d;

            // Beside a side road with no centre line: its edge says more than a centre line it
            // only runs into.
            var sideRoad = insideRoad ? null : SideRoadAt(p);
            if (sideRoad != null)
            {
                var beside = P2.Cross(sideRoad.Tangent, p - sideRoad.Foot) < 0 ? sideRoad.Tangent : sideRoad.Tangent * -1;
                return new TravelAt
                {
                    Direction = beside, Tier = OrientationTier.Alignment, Confidence = 0.55, Line = sideRoad.Edge, Distance = sideRoad.Distance,
                    SideRoad = sideRoad,
                    Why = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "side road edge {0} ({1:0} ft away; it runs into {2}, {3:0} ft away, and has no centre line of its own), traffic beside it keeps right",
                        sideRoad.Edge.Id, sideRoad.Distance, sideRoad.Meets.Id, sideRoad.MeetsDistance)
                };
            }

            var refs = References(p, _std.RoadSearchFeet);
            // Best by score, not simply nearest: a short driveway edge beside a sign says less
            // about the road than the long road edge a little further off.
            var axis = refs.Where(r => IsAxis(r.Role)).OrderByDescending(r => r.Score).FirstOrDefault();
            if (axis != null)
            {
                foot = axis.Foot; tangent = axis.Tangent; d = axis.Distance;
                TravelAt found;
                if (axis.Role == LineRole.OneWay)
                    found = new TravelAt { Direction = tangent, Tier = OrientationTier.Alignment, Confidence = 0.85, Line = axis.Line, Distance = d,
                                           Why = "one-way road " + axis.Describe() + " drawn in the direction of travel" };
                else
                {
                    var side = P2.Cross(tangent, p - foot);
                    var t = side < 0 ? tangent : tangent * -1;          // keep right: the point is on the right
                    found = new TravelAt { Direction = t, Tier = OrientationTier.Alignment, Confidence = d < 0.5 ? 0.3 : 0.7, Line = axis.Line, Distance = d,
                                           Why = "two-way road " + axis.Describe() + ", traffic keeps right" };
                }
                return Checked(found, axis, refs, p);
            }

            var edgeRef = refs.Where(r => r.Role != LineRole.RoadContext).OrderByDescending(r => r.Score).FirstOrDefault();
            var context = false;
            if (edgeRef == null)
            {
                // Only an edge of concrete or a sidewalk: context, never enough on its own.
                edgeRef = refs.OrderByDescending(r => r.Score).FirstOrDefault();
                context = true;
            }
            if (edgeRef == null) return null;
            var edge = edgeRef.Line;
            foot = edgeRef.Foot; tangent = edgeRef.Tangent; d = edgeRef.Distance;
            if (insideRoad)
            {
                // Between two edges: keep right of the midline.
                var nearFoot = foot;
                var other = refs.Where(e => e.Line != edge && e.Role != LineRole.RoadContext && P2.Dot(e.Foot - p, nearFoot - p) < 0)
                                .OrderBy(e => e.Distance).FirstOrDefault();
                if (other == null) return null;
                var mid = foot + (other.Foot - foot) * 0.5;
                var keepRight = P2.Cross(tangent, p - mid) < 0 ? tangent : tangent * -1;
                return Checked(new TravelAt { Direction = keepRight, Tier = OrientationTier.Inferred, Confidence = 0.5, Line = edge, Distance = d,
                                              Why = "between " + edgeRef.Describe() + " and " + other.Describe() + ", traffic keeps right of the middle" },
                               edgeRef, refs, p);
            }
            var outSide = P2.Cross(tangent, p - foot);
            var dir = outSide < 0 ? tangent : tangent * -1;
            var basis = context ? 0.35 : (edgeRef.Role == LineRole.LaneLine ? 0.6 : 0.55);
            return Checked(new TravelAt
            {
                Direction = dir, Tier = context ? OrientationTier.Inferred : OrientationTier.Alignment, Confidence = basis, Line = edge, Distance = d,
                Why = edgeRef.Describe() + " (no centre line), traffic beside it keeps right" + (context ? "; road context only" : "")
            }, edgeRef, refs, p);
        }

        /// <summary>Lowers the confidence for a line followed past its end or far away, notes the
        /// lines that agree, and marks a disagreement for review.</summary>
        private TravelAt Checked(TravelAt found, RoadReference chosen, IList<RoadReference> all, P2 p)
        {
            found.Reference = chosen;
            found.Confidence = Math.Round(found.Confidence * ConfidenceFactor(chosen), 2);
            var agree = Agreeing(chosen, all);
            if (agree.Count > 0)
                found.Why += "; " + string.Join(", ", agree.Take(3).Select(a => a.Describe()).ToArray()) + (agree.Count == 1 ? " runs" : " run") + " the same way";
            var rival = Disagreeing(chosen, all);
            if (rival != null)
            {
                var angle = Math.Acos(Math.Min(1, Math.Abs(P2.Dot(rival.Tangent, chosen.Tangent)))) * 180 / Math.PI;
                found.Conflict = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "road lines here disagree: {0} and {1} run {2:0} degrees apart; record its direction or check the linework",
                    chosen.Describe(), rival.Describe(), angle);
                var side = P2.Cross(rival.Tangent, p - rival.Foot);
                found.ConflictDirection = side < 0 ? rival.Tangent : rival.Tangent * -1;
            }
            return found;
        }
    }
}
