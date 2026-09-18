using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FieldCodes.Easements
{
    /// <summary>The result of joining selected objects end to end: the courses in order, or why they could not be joined.</summary>
    public sealed class ChainResult
    {
        public List<Course> Courses { get; set; }

        /// <summary>For each course, the index of the selected object it came from.</summary>
        public List<int> PieceOfCourse { get; set; }

        /// <summary>Selected line length set aside because it lies outside the boundary (beyond a corner); zero when every object is used whole.</summary>
        public double IgnoredLength { get; set; }

        /// <summary>Null when the objects join unambiguously.</summary>
        public string Problem { get; set; }

        public bool Ok { get { return Problem == null; } }

        internal static ChainResult Fail(string problem) { return new ChainResult { Problem = problem }; }
    }

    /// <summary>
    /// Joins separately drawn lines and arcs -- lot lines, a right-of-way margin drawn as a line and a
    /// curve -- into one working boundary or path, without touching the objects themselves. It never
    /// guesses: every end must meet exactly one other end, and anything else (a gap, a branch, a line
    /// running past a corner, separate loops) is reported for the drafter to resolve.
    /// </summary>
    public static class CurveChain
    {
        /// <summary>A single closed boundary through every selected object.</summary>
        public static ChainResult Closed(IList<IList<Course>> pieces, double tolerance)
        {
            var graph = Graph.Build(pieces, tolerance);
            if (graph.Problem != null) return ChainResult.Fail(graph.Problem);
            if (graph.Edges.Count == 1)
            {
                var only = graph.Edges[0];
                if (only.Start != only.End) return ChainResult.Fail("One open object was selected; select every line and curve around the lot.");
            }
            foreach (var node in graph.Nodes)
            {
                var degree = graph.Degree(node.Index);
                if (degree == 1) return ChainResult.Fail("The boundary is open near " + node.Point + Nearest(graph, node) + ".");
                if (degree > 2) return ChainResult.Fail(degree + " selected objects meet near " + node.Point + ", so the boundary there is ambiguous; select only the lines of this lot.");
            }
            var walk = Walk(graph, graph.Edges[0].Start, 0);
            if (walk.Courses.Count == 0 || walk.Used.Count < graph.Edges.Count)
            {
                var loops = CountComponents(graph);
                return ChainResult.Fail("The selected objects form " + loops + " separate closed shapes, not one boundary.");
            }
            var courses = walk.Courses;
            if (courses[courses.Count - 1].End.DistanceTo(courses[0].Start) > tolerance)
                return ChainResult.Fail("The boundary does not close.");
            courses[courses.Count - 1] = courses[courses.Count - 1].WithEnds(courses[courses.Count - 1].Start, courses[0].Start);
            var crossings = Loops.SelfIntersections(courses, tolerance);
            if (crossings.Count > 0) return ChainResult.Fail("The selected objects cross each other near " + crossings[0] + ", so they do not make one boundary.");
            if (Math.Abs(Loops.SignedArea(courses)) <= tolerance) return ChainResult.Fail("The selected objects enclose no area.");
            return new ChainResult { Courses = courses, PieceOfCourse = walk.PieceOfCourse };
        }

        /// <summary>
        /// The one area the selected objects enclose, when lot lines run past the lot's corners (a street line drawn
        /// across several lots, say): the objects are split where they meet or cross, loose ends beyond the corners
        /// are set aside, and the result is used only when exactly one closed area remains. Two or more areas, or none,
        /// is reported -- never picked between. <see cref="ChainResult.IgnoredLength"/> is how much selected line
        /// lies outside the boundary, so the drafter can be told.
        /// </summary>
        public static ChainResult Enclosed(IList<IList<Course>> pieces, double tolerance)
        {
            var courses = new List<Course>();
            foreach (var piece in pieces ?? new List<IList<Course>>())
                foreach (var c in piece ?? new List<Course>())
                    if (c != null && c.Length > tolerance) courses.Add(c);
            if (courses.Count == 0) return ChainResult.Fail("Nothing was selected.");
            var total = courses.Sum(c => c.Length);

            // Split every course where another meets or crosses it.
            var edges = new List<Course>();
            for (var i = 0; i < courses.Count; i++)
            {
                var c = courses[i];
                var cuts = new List<double>();
                for (var j = 0; j < courses.Count; j++)
                {
                    if (i == j) continue;
                    var hits = new List<P2>(Intersections.Bounded(c, courses[j], tolerance));
                    foreach (var end in new[] { courses[j].Start, courses[j].End })
                        if (Intersections.OnCourse(c, end, tolerance)) hits.Add(end);
                    foreach (var h in hits)
                    {
                        double along;
                        c.Closest(h, out along);
                        if (along > tolerance && along < c.Length - tolerance) cuts.Add(along);
                    }
                }
                var stations = new[] { 0.0 }.Concat(cuts.OrderBy(a => a)).Concat(new[] { c.Length }).ToList();
                for (var k = 1; k < stations.Count; k++)
                    if (stations[k] - stations[k - 1] > tolerance) edges.Add(c.Sub(stations[k - 1], stations[k]));
            }

            // Nodes, and loose ends pruned until only closed rings remain.
            var nodes = new List<P2>();
            Func<P2, int> nodeAt = p =>
            {
                for (var n = 0; n < nodes.Count; n++) if (nodes[n].DistanceTo(p) <= tolerance) return n;
                nodes.Add(p);
                return nodes.Count - 1;
            };
            var all = edges.Select(e => new { Course = e, A = nodeAt(e.Start), B = nodeAt(e.End) }).Where(e => e.A != e.B || e.Course.Length > tolerance * 10).ToList();
            // A line drawn twice (common in real drawings) is one edge: coincident edges would confuse the face order.
            var graph = all.Take(0).ToList();
            foreach (var e in all)
                if (!graph.Any(g => ((g.A == e.A && g.B == e.B) || (g.A == e.B && g.B == e.A)) &&
                                    g.Course.PointAt(g.Course.Length / 2).DistanceTo(e.Course.PointAt(e.Course.Length / 2)) <= tolerance * 10))
                    graph.Add(e);
            bool pruned;
            do
            {
                pruned = false;
                var degree = new int[nodes.Count];
                foreach (var e in graph) { degree[e.A]++; degree[e.B]++; }
                var keep = graph.Where(e => degree[e.A] > 1 && degree[e.B] > 1).ToList();
                if (keep.Count < graph.Count) { graph = keep; pruned = true; }
            } while (pruned);
            if (graph.Count == 0) return ChainResult.Fail("The selected objects do not enclose an area" + NearMiss(courses, tolerance) + ".");

            // Faces: around each node the edges in angle order; each face keeps its area on the left.
            var half = new List<Tuple<int, int, Course>>();     // from, to, course in that direction
            foreach (var e in graph)
            {
                half.Add(Tuple.Create(e.A, e.B, e.Course));
                half.Add(Tuple.Create(e.B, e.A, e.Course.Reversed()));
            }
            Func<Course, double> leaving = c => Math.Atan2(c.StartDirection.Y, c.StartDirection.X);
            var around = Enumerable.Range(0, nodes.Count).ToDictionary(n => n, n => half.Select((h, i) => new { h, i }).Where(x => x.h.Item1 == n).OrderBy(x => leaving(x.h.Item3)).Select(x => x.i).ToList());
            var used = new bool[half.Count];
            var faces = new List<List<Course>>();
            for (var start = 0; start < half.Count; start++)
            {
                if (used[start]) continue;
                var face = new List<Course>();
                var h = start;
                var guard = 0;
                while (!used[h] && guard++ < half.Count + 1)
                {
                    used[h] = true;
                    face.Add(half[h].Item3);
                    var at = half[h].Item2;
                    var list = around[at];
                    // The twin (back along this edge), then the edge just clockwise of it: the sharpest left turn.
                    var twin = h ^ 1;
                    var index = list.IndexOf(twin);
                    h = list[(index - 1 + list.Count) % list.Count];
                }
                faces.Add(face);
            }
            var bounded = faces.Where(f => Loops.SignedArea(f) > tolerance).ToList();
            if (bounded.Count == 0) return ChainResult.Fail("The selected objects do not enclose an area" + NearMiss(courses, tolerance) + ".");
            if (bounded.Count > 1)
                return ChainResult.Fail("The selected objects enclose " + bounded.Count + " separate areas (" +
                                        string.Join(", ", bounded.Select(f => Loops.SignedArea(f).ToString("N0", CultureInfo.InvariantCulture)).ToArray()) +
                                        " sq units), so which one is the lot is ambiguous; select only the lines around this lot.");
            var ring = bounded[0];
            for (var i = 1; i < ring.Count; i++) ring[i] = ring[i].WithEnds(ring[i - 1].End, ring[i].End);
            ring[ring.Count - 1] = ring[ring.Count - 1].WithEnds(ring[ring.Count - 1].Start, ring[0].Start);
            var loop = Merge(ring, tolerance);
            var ignored = total - loop.Sum(c => c.Length);
            return new ChainResult { Courses = loop, PieceOfCourse = null, IgnoredLength = Math.Max(0, ignored) };
        }

        /// <summary>Where selected objects come close without meeting -- the likely gap -- for the drafter to look at.</summary>
        private static string NearMiss(IList<Course> courses, double tolerance)
        {
            var best = double.MaxValue;
            P2 at = new P2(0, 0);
            for (var i = 0; i < courses.Count; i++)
                foreach (var end in new[] { courses[i].Start, courses[i].End })
                    for (var j = 0; j < courses.Count; j++)
                    {
                        if (i == j) continue;
                        double along;
                        var d = courses[j].Closest(end, out along).DistanceTo(end);
                        if (d > tolerance && d < best) { best = d; at = end; }
                    }
            return best < 5 ? "; the nearest miss is " + best.ToString("0.000", CultureInfo.InvariantCulture) + "' near " + at + ", where two selected objects come close but do not meet" : string.Empty;
        }

        /// <summary>Joins pieces of one straight line or one curve that the splitting left in a row, so each side is one course.</summary>
        private static List<Course> Merge(List<Course> loop, double tolerance)
        {
            var merged = new List<Course>();
            foreach (var c in loop)
            {
                if (merged.Count > 0)
                {
                    var last = merged[merged.Count - 1];
                    if (Continues(last, c, tolerance)) { merged[merged.Count - 1] = last.WithEnds(last.Start, c.End); continue; }
                }
                merged.Add(c);
            }
            while (merged.Count > 2 && Continues(merged[merged.Count - 1], merged[0], tolerance))
            {
                merged[0] = merged[merged.Count - 1].WithEnds(merged[merged.Count - 1].Start, merged[0].End);
                merged.RemoveAt(merged.Count - 1);
            }
            return merged;
        }

        private static bool Continues(Course a, Course b, double tolerance)
        {
            if (a.Kind != b.Kind) return false;
            if (a.Kind == CourseKind.Line)
            {
                var d = a.EndDirection;
                var e = b.StartDirection;
                if (P2.Dot(d, e) <= 0) return false;
                // Still on a's line at b's far end (not just parallel).
                var toEnd = b.End - a.Start;
                return Math.Abs(P2.Cross(d, toEnd)) <= tolerance;
            }
            return a.Center.DistanceTo(b.Center) <= tolerance && Math.Abs(a.Radius - b.Radius) <= tolerance && a.CounterClockwise == b.CounterClockwise;
        }

        /// <summary>A single open path through every selected object, starting from the end nearer <paramref name="from"/>.</summary>
        public static ChainResult Open(IList<IList<Course>> pieces, P2 from, double tolerance)
        {
            var graph = Graph.Build(pieces, tolerance);
            if (graph.Problem != null) return ChainResult.Fail(graph.Problem);
            var ends = new List<Node>();
            foreach (var node in graph.Nodes)
            {
                var degree = graph.Degree(node.Index);
                if (degree == 1) ends.Add(node);
                else if (degree > 2) return ChainResult.Fail(degree + " selected objects meet near " + node.Point + ", so the path there is ambiguous; select only the objects the tie follows.");
            }
            if (ends.Count == 0) return ChainResult.Fail("The selected objects close on themselves; a tie follows an open line.");
            if (ends.Count > 2) return ChainResult.Fail("The selected objects do not join end to end (" + (ends.Count / 2) + " separate pieces); there is a gap near " + ends[1].Point + Nearest(graph, ends[1]) + ".");
            var startNode = ends.OrderBy(n => n.Point.DistanceTo(from)).First();
            var walk = Walk(graph, startNode.Index, -1);
            if (walk.Used.Count < graph.Edges.Count) return ChainResult.Fail("The selected objects are not all connected.");
            return new ChainResult { Courses = walk.Courses, PieceOfCourse = walk.PieceOfCourse };
        }

        // ------------------------------------------------------------------ graph

        private sealed class Node
        {
            public int Index;
            public P2 Point;
        }

        private sealed class Edge
        {
            public int Piece;
            public List<Course> Courses;
            public int Start;
            public int End;
        }

        private sealed class Graph
        {
            public readonly List<Node> Nodes = new List<Node>();
            public readonly List<Edge> Edges = new List<Edge>();
            public string Problem;
            public double Tolerance;

            public static Graph Build(IList<IList<Course>> pieces, double tolerance)
            {
                var g = new Graph { Tolerance = tolerance };
                if (pieces == null || pieces.Count == 0) { g.Problem = "Nothing was selected."; return g; }
                for (var i = 0; i < pieces.Count; i++)
                {
                    var courses = (pieces[i] ?? new List<Course>()).Where(c => c != null && c.Length > tolerance).ToList();
                    if (courses.Count == 0) continue;
                    // A polyline's own courses already run end to end; a gap inside one object is not joined.
                    for (var k = 1; k < courses.Count; k++)
                        if (courses[k].Start.DistanceTo(courses[k - 1].End) > tolerance)
                        {
                            g.Problem = "Selected object " + (i + 1) + " has a gap of its own near " + courses[k].Start + ".";
                            return g;
                        }
                    g.Edges.Add(new Edge { Piece = i, Courses = courses, Start = g.NodeAt(courses[0].Start), End = g.NodeAt(courses[courses.Count - 1].End) });
                }
                if (g.Edges.Count == 0) g.Problem = "The selected objects have no length.";
                return g;
            }

            private int NodeAt(P2 p)
            {
                foreach (var n in Nodes) if (n.Point.DistanceTo(p) <= Tolerance) return n.Index;
                Nodes.Add(new Node { Index = Nodes.Count, Point = p });
                return Nodes.Count - 1;
            }

            public int Degree(int node)
            {
                return Edges.Sum(e => (e.Start == node ? 1 : 0) + (e.End == node ? 1 : 0));
            }
        }

        private sealed class WalkResult
        {
            public readonly List<Course> Courses = new List<Course>();
            public readonly List<int> PieceOfCourse = new List<int>();
            public readonly HashSet<int> Used = new HashSet<int>();
        }

        /// <summary>Follows edges from a node, each edge once. <paramref name="firstEdge"/> is the edge to leave by, or -1 for any.</summary>
        private static WalkResult Walk(Graph g, int startNode, int firstEdge)
        {
            var result = new WalkResult();
            var at = startNode;
            var next = firstEdge;
            while (true)
            {
                if (next < 0)
                    for (var i = 0; i < g.Edges.Count; i++)
                        if (!result.Used.Contains(i) && (g.Edges[i].Start == at || g.Edges[i].End == at)) { next = i; break; }
                if (next < 0) break;
                var e = g.Edges[next];
                result.Used.Add(next);
                var forward = e.Start == at;
                var courses = forward ? e.Courses : EasementBuilder.Reverse(e.Courses);
                foreach (var c in courses) { result.Courses.Add(c); result.PieceOfCourse.Add(e.Piece); }
                at = forward ? e.End : e.Start;
                next = -1;
                if (at == startNode && firstEdge >= 0) break;
            }
            // Snap each joint so the working boundary has no slivers from drafting tolerance.
            for (var i = 1; i < result.Courses.Count; i++)
            {
                var previous = result.Courses[i - 1];
                if (previous.End.DistanceTo(result.Courses[i].Start) > 0)
                    result.Courses[i] = result.Courses[i].WithEnds(previous.End, result.Courses[i].End);
            }
            return result;
        }

        private static int CountComponents(Graph g)
        {
            var seen = new HashSet<int>();
            var count = 0;
            foreach (var n in g.Nodes)
            {
                if (seen.Contains(n.Index)) continue;
                count++;
                var stack = new Stack<int>();
                stack.Push(n.Index);
                while (stack.Count > 0)
                {
                    var k = stack.Pop();
                    if (!seen.Add(k)) continue;
                    foreach (var e in g.Edges.Where(e => e.Start == k || e.End == k)) { stack.Push(e.Start); stack.Push(e.End); }
                }
            }
            return count;
        }

        /// <summary>A hint for an open end: the nearest other end, or the object it touches part way along.</summary>
        private static string Nearest(Graph g, Node node)
        {
            var others = g.Nodes.Where(n => n.Index != node.Index).ToList();
            foreach (var e in g.Edges)
                foreach (var c in e.Courses)
                {
                    double along;
                    var foot = c.Closest(node.Point, out along);
                    if (foot.DistanceTo(node.Point) <= g.Tolerance && along > g.Tolerance && along < c.Length - g.Tolerance && e.Start != node.Index && e.End != node.Index)
                        return " -- it meets another selected object part way along, not at its end (a line runs past the corner)";
                }
            if (others.Count == 0) return string.Empty;
            var nearest = others.OrderBy(n => n.Point.DistanceTo(node.Point)).First();
            return " (the nearest other end is " + node.Point.DistanceTo(nearest.Point).ToString("0.00", CultureInfo.InvariantCulture) + " away)";
        }
    }
}
