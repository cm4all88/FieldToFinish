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
        public IList<Intersection> Intersections { get; private set; }

        public RoadModel(IEnumerable<OrientLine> lines, OrientationStandard std)
        {
            _std = std;
            var all = (lines ?? Enumerable.Empty<OrientLine>()).Where(l => l != null && l.Points.Count >= 2).ToList();
            Axes = all.Where(l => l.Role == LineRole.Centerline || l.Role == LineRole.OneWay).ToList();
            Edges = all.Where(l => l.Role == LineRole.Edge || l.Role == LineRole.LaneLine).ToList();
            Crosswalks = all.Where(l => l.Role == LineRole.Crosswalk).ToList();
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
            if (travel != null && travel.Tier <= OrientationTier.Alignment && Math.Abs(P2.Dot(travel.Direction, bar.Along)) < 0.71)
            {
                bar.Travel = travel.Direction;
                bar.TravelFrom = travel.Why;
            }
            return bar;
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

            var axis = NearestAxis(p, _std.RoadSearchFeet, out foot, out tangent, out d);
            if (axis != null)
            {
                if (axis.Role == LineRole.OneWay)
                    return new TravelAt { Direction = tangent, Tier = OrientationTier.Alignment, Confidence = 0.85, Line = axis, Distance = d,
                                          Why = "one-way road " + axis.Id + " drawn in the direction of travel" };
                var side = P2.Cross(tangent, p - foot);
                var t = side < 0 ? tangent : tangent * -1;          // keep right: the point is on the right
                return new TravelAt { Direction = t, Tier = OrientationTier.Alignment, Confidence = d < 0.5 ? 0.3 : 0.7, Line = axis, Distance = d,
                                      Why = "two-way road " + axis.Id + ", traffic keeps right" };
            }

            var edge = NearestEdge(p, _std.RoadSearchFeet, out foot, out tangent, out d);
            if (edge == null) return null;
            if (insideRoad)
            {
                // Between two edges: keep right of the midline.
                var nearFoot = foot;
                var other = Edges.Where(e => e != edge).Select(e => { P2 f, tg; double dd, s; e.Nearest(p, out f, out tg, out dd, out s); return Tuple.Create(e, f, dd); })
                                 .Where(e => e.Item3 < _std.RoadSearchFeet && P2.Dot(e.Item2 - p, nearFoot - p) < 0)
                                 .OrderBy(e => e.Item3).FirstOrDefault();
                if (other == null) return null;
                var mid = foot + (other.Item2 - foot) * 0.5;
                var keepRight = P2.Cross(tangent, p - mid) < 0 ? tangent : tangent * -1;
                return new TravelAt { Direction = keepRight, Tier = OrientationTier.Inferred, Confidence = 0.5, Line = edge, Distance = d,
                                      Why = "between edges " + edge.Id + " and " + other.Item1.Id + ", traffic keeps right of the middle" };
            }
            var outSide = P2.Cross(tangent, p - foot);
            var dir = outSide < 0 ? tangent : tangent * -1;
            return new TravelAt { Direction = dir, Tier = OrientationTier.Alignment, Confidence = 0.55, Line = edge, Distance = d,
                                  Why = "road edge " + edge.Id + " (no centre line), traffic beside it keeps right" };
        }
    }
}
