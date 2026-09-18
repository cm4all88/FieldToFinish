using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FieldCodes.Easements
{
    /// <summary>One area of the strip after it is cut by the trim lines.</summary>
    public sealed class TrimPiece
    {
        /// <summary>1 is the largest piece, 2 the next, and so on.</summary>
        public int Number { get; internal set; }

        /// <summary>The piece's closed outline, counter-clockwise.</summary>
        public List<Course> Loop { get; internal set; }

        public double Area { get; internal set; }

        /// <summary>A point well inside the piece: where its number is shown, and what
        /// is stored so a rebuild keeps the same piece.</summary>
        public P2 InsidePoint { get; internal set; }

        internal List<int> HalfEdges { get; set; }
    }

    /// <summary>The strip split by its trim lines.</summary>
    public sealed class TrimResult
    {
        public List<TrimPiece> Pieces { get; private set; }

        /// <summary>For each trim line, whether it actually cuts the strip.</summary>
        public List<bool> TrimCuts { get; private set; }

        public List<string> Errors { get; private set; }
        public List<string> Warnings { get; private set; }

        public bool Ok { get { return Errors.Count == 0; } }

        internal StripTrim.Graph Graph { get; set; }
        internal List<Course> Strip { get; set; }

        public TrimResult()
        {
            Pieces = new List<TrimPiece>();
            TrimCuts = new List<bool>();
            Errors = new List<string>();
            Warnings = new List<string>();
        }

        /// <summary>The piece containing a point, or null.</summary>
        public TrimPiece PieceAt(P2 p)
        {
            return Pieces.FirstOrDefault(piece => StripTrim.Inside(piece.Loop, p));
        }
    }

    /// <summary>
    /// Cuts an easement strip with any number of trim lines -- property lines,
    /// right-of-way lines -- the way a drafter trims to them: every place a trim line
    /// crosses the strip divides it, whether across the end or lengthwise along a
    /// side, and the drafter keeps the pieces that belong to the easement. Exact:
    /// arcs stay arcs and nothing is chorded, so areas and courses stay true.
    /// </summary>
    public static class StripTrim
    {
        // ================================================================ ends

        /// <summary>
        /// When the route starts or ends exactly on a trim line, the square end of the
        /// strip would leave one corner short of that line on a skew. Such an end is
        /// run on past the line just far enough that both sides cross it, so the trim
        /// cuts the strip exactly along the line. Ends that are not on a trim line are
        /// left alone.
        /// </summary>
        public static List<Course> ExtendEndsToTrims(IList<Course> route, WidthSpec width,
                                                     IList<IList<Course>> trims, double tolerance)
        {
            var result = route.ToList();
            if (result.Count == 0 || trims == null || trims.Count == 0) return result;

            var last = result.Count - 1;
            var endExtension = Extension(result[last], width.Left, width.Right, trims, tolerance);
            if (endExtension > 0) result[last] = Extend(result[last], endExtension);

            var reversedFirst = result[0].Reversed();
            var startExtension = Extension(reversedFirst, width.Right, width.Left, trims, tolerance);
            if (startExtension > 0) result[0] = Extend(reversedFirst, startExtension).Reversed();
            return result;
        }

        private static double Extension(Course end, double left, double right, IList<IList<Course>> trims, double tolerance)
        {
            var tip = end.End;
            var onTrim = trims.SelectMany(t => t).Where(t => Distance(t, tip) <= tolerance).ToList();
            if (onTrim.Count == 0) return 0;

            var needed = 0.0;
            foreach (var offset in new[] { left, -right })
            {
                string failure;
                var side = end.Offset(offset, out failure);
                if (side == null || side.Length <= tolerance) continue;

                // How far past the end this side meets the line; zero when it already has.
                var reach = double.MaxValue;
                var alreadyCrossed = false;
                foreach (var t in onTrim)
                foreach (var p in Intersections.Carriers(side, t))
                {
                    if (!Intersections.OnCourse(t, p, tolerance)) continue;
                    var along = side.DistanceOf(p);
                    if (along < side.Length - tolerance) { alreadyCrossed = true; continue; }
                    if (end.Kind == CourseKind.Arc && along - side.Length > side.Radius * Math.PI / 2) continue;
                    reach = Math.Min(reach, along - side.Length);
                }
                if (reach == double.MaxValue) { if (!alreadyCrossed) continue; reach = 0; }
                needed = Math.Max(needed, reach * (end.Length / side.Length));
            }
            if (needed <= 0) return 0;
            // A little past the line, so the cut is clean rather than a touch.
            return needed + Math.Max(tolerance * 20, (left + right) * 0.01);
        }

        private static Course Extend(Course c, double by)
        {
            return c.WithEnds(c.Start, c.PointAt(c.Length + by));
        }

        private static double Distance(Course c, P2 p)
        {
            double along;
            return c.Closest(p, out along).DistanceTo(p);
        }

        // =============================================================== split

        internal sealed class Edge
        {
            public Course Course;
            public int Parent;
            public int Trim = -1;
            public int From;
            public int To;
            public bool Removed;
        }

        internal sealed class Graph
        {
            public readonly List<P2> Vertices = new List<P2>();
            public readonly List<Edge> Edges = new List<Edge>();

            // Half edge h: edge h / 2, forward when h is even.
            public Course CourseOf(int h) { var e = Edges[h / 2]; return h % 2 == 0 ? e.Course : e.Course.Reversed(); }
            public int FromOf(int h) { var e = Edges[h / 2]; return h % 2 == 0 ? e.From : e.To; }
            public int ToOf(int h) { var e = Edges[h / 2]; return h % 2 == 0 ? e.To : e.From; }
            public static int Twin(int h) { return h ^ 1; }

            public Dictionary<int, List<int>> Outgoing;
            public int[] FaceOf;
        }

        /// <summary>
        /// Splits the closed strip outline by the trim lines. Parts of a trim line
        /// outside the strip, or lying along its edge, do nothing; a trim line that stops
        /// inside the strip without reaching across is reported and its loose end ignored.
        /// </summary>
        public static TrimResult Split(IList<Course> strip, IList<IList<Course>> trims, double tolerance)
        {
            var result = new TrimResult { Strip = strip.ToList() };
            trims = trims ?? new List<IList<Course>>();
            for (var t = 0; t < trims.Count; t++) result.TrimCuts.Add(false);

            if (strip == null || strip.Count == 0) { result.Errors.Add("There is no easement outline to trim."); return result; }

            // Everything that can bound a piece: the outline, then each trim line.
            var sources = new List<Edge>();
            for (var i = 0; i < strip.Count; i++)
                if (strip[i].Length > tolerance) sources.Add(new Edge { Course = strip[i], Parent = i });
            var parent = strip.Count;
            for (var t = 0; t < trims.Count; t++)
                foreach (var c in trims[t])
                    if (c.Length > tolerance) sources.Add(new Edge { Course = c, Parent = parent++, Trim = t });

            var cuts = sources.Select(s => new List<double> { 0, s.Course.Length }).ToList();
            for (var i = 0; i < sources.Count; i++)
            for (var j = i + 1; j < sources.Count; j++)
            {
                if (sources[i].Trim < 0 && sources[j].Trim < 0) continue;     // the outline meets itself only at corners
                foreach (var p in Intersections.Bounded(sources[i].Course, sources[j].Course, tolerance))
                {
                    cuts[i].Add(Clamp(sources[i].Course.DistanceOf(p), sources[i].Course.Length));
                    cuts[j].Add(Clamp(sources[j].Course.DistanceOf(p), sources[j].Course.Length));
                }
            }

            var graph = new Graph();
            for (var i = 0; i < sources.Count; i++)
            {
                var s = sources[i];
                var stations = cuts[i].OrderBy(x => x).ToList();
                var distinct = new List<double>();
                foreach (var x in stations)
                    if (distinct.Count == 0 || x - distinct[distinct.Count - 1] > tolerance) distinct.Add(x);
                if (distinct[distinct.Count - 1] < s.Course.Length) distinct[distinct.Count - 1] = s.Course.Length;

                for (var k = 1; k < distinct.Count; k++)
                {
                    var piece = s.Course.Sub(distinct[k - 1], distinct[k]);
                    if (piece.Length <= tolerance) continue;
                    if (s.Trim >= 0)
                    {
                        var mid = piece.PointAt(piece.Length / 2);
                        if (OnOutline(strip, mid, tolerance) || !Inside(strip, mid)) continue;
                    }
                    AddEdge(graph, piece, s.Parent, s.Trim, tolerance);
                }
            }

            // A trim line that ends inside the strip leaves a loose end: prune it.
            var loose = new HashSet<int>();
            while (true)
            {
                var degree = new int[graph.Vertices.Count];
                foreach (var e in graph.Edges.Where(e => !e.Removed)) { degree[e.From]++; degree[e.To]++; }
                var dangling = graph.Edges.Where(e => !e.Removed && (degree[e.From] < 2 || degree[e.To] < 2)).ToList();
                if (dangling.Count == 0) break;
                foreach (var e in dangling) { e.Removed = true; if (e.Trim >= 0) loose.Add(e.Trim); }
            }
            foreach (var t in loose.OrderBy(x => x))
                result.Warnings.Add(string.Format(CultureInfo.InvariantCulture,
                    "Trim line {0} stops inside the easement without reaching across it; that loose end does not cut anything.", t + 1));
            foreach (var e in graph.Edges.Where(e => !e.Removed && e.Trim >= 0)) result.TrimCuts[e.Trim] = true;
            for (var t = 0; t < trims.Count; t++)
                if (!result.TrimCuts[t] && !loose.Contains(t))
                    result.Warnings.Add(string.Format(CultureInfo.InvariantCulture,
                        "Trim line {0} does not cross the easement, so it trims nothing.", t + 1));

            // Around each corner, the lines leaving it in counter-clockwise order.
            graph.Outgoing = new Dictionary<int, List<int>>();
            for (var h = 0; h < graph.Edges.Count * 2; h++)
            {
                if (graph.Edges[h / 2].Removed) continue;
                List<int> list;
                if (!graph.Outgoing.TryGetValue(graph.FromOf(h), out list)) graph.Outgoing[graph.FromOf(h)] = list = new List<int>();
                list.Add(h);
            }
            foreach (var key in graph.Outgoing.Keys.ToList())
                graph.Outgoing[key] = graph.Outgoing[key].OrderBy(h => LeavingAngle(graph, h, tolerance)).ToList();

            // Trace faces keeping each face on the left: counter-clockwise faces are pieces.
            graph.FaceOf = Enumerable.Repeat(-1, graph.Edges.Count * 2).ToArray();
            var outerFaces = 0;
            var faceIndex = 0;
            var pieces = new List<TrimPiece>();
            for (var start = 0; start < graph.Edges.Count * 2; start++)
            {
                if (graph.Edges[start / 2].Removed || graph.FaceOf[start] >= 0) continue;
                var cycle = new List<int>();
                var h = start;
                var guard = graph.Edges.Count * 2 + 1;
                while (graph.FaceOf[h] < 0 && guard-- > 0)
                {
                    graph.FaceOf[h] = faceIndex;
                    cycle.Add(h);
                    h = Next(graph, h);
                }
                if (h != start)
                {
                    result.Errors.Add("The trim lines could not be resolved into clean pieces here -- check for overlapping or doubled lines.");
                    return result;
                }

                var loop = cycle.Select(graph.CourseOf).ToList();
                var area = Loops.SignedArea(loop);
                if (area > tolerance * tolerance)
                    pieces.Add(new TrimPiece { Loop = loop, Area = area, HalfEdges = cycle });
                else if (area < -tolerance * tolerance)
                    outerFaces++;
                faceIndex++;
            }

            if (outerFaces > 1)
            {
                result.Errors.Add("A trim line makes a closed shape inside the easement without touching its edge; that is not supported -- pick lines that run across the strip.");
                return result;
            }

            // Renumber faces so FaceOf points at pieces (-1 = outside the strip).
            var faceToPiece = new Dictionary<int, TrimPiece>();
            foreach (var piece in pieces) faceToPiece[graph.FaceOf[piece.HalfEdges[0]]] = piece;
            var ordered = pieces.OrderByDescending(p => p.Area).ToList();
            for (var i = 0; i < ordered.Count; i++) ordered[i].Number = i + 1;
            for (var h = 0; h < graph.FaceOf.Length; h++)
            {
                if (graph.FaceOf[h] < 0) continue;
                TrimPiece piece;
                graph.FaceOf[h] = faceToPiece.TryGetValue(graph.FaceOf[h], out piece) ? piece.Number : 0;
            }
            foreach (var piece in ordered) piece.InsidePoint = PointInside(piece.Loop, tolerance);

            result.Pieces.AddRange(ordered);
            result.Graph = graph;
            if (ordered.Count == 0) result.Errors.Add("Nothing of the easement is left after trimming.");
            return result;
        }

        private static double Clamp(double v, double max) { return Math.Max(0, Math.Min(max, v)); }

        private static void AddEdge(Graph graph, Course course, int parent, int trim, double tolerance)
        {
            var from = Vertex(graph, course.Start, tolerance);
            var to = Vertex(graph, course.End, tolerance);
            if (from == to) return;
            var mid = course.PointAt(course.Length / 2);
            foreach (var e in graph.Edges)
                if (((e.From == from && e.To == to) || (e.From == to && e.To == from)) &&
                    e.Course.PointAt(e.Course.Length / 2).DistanceTo(mid) <= tolerance * 2)
                    return;     // the same line twice
            graph.Edges.Add(new Edge
            {
                Course = course.WithEnds(graph.Vertices[from], graph.Vertices[to]),
                Parent = parent, Trim = trim, From = from, To = to
            });
        }

        private static int Vertex(Graph graph, P2 p, double tolerance)
        {
            for (var i = 0; i < graph.Vertices.Count; i++)
                if (graph.Vertices[i].DistanceTo(p) <= tolerance * 2) return i;
            graph.Vertices.Add(p);
            return graph.Vertices.Count - 1;
        }

        /// <summary>The direction a half edge leaves its corner, looked at a short way
        /// along it so a curve tangent to a line still sorts to its own side.</summary>
        private static double LeavingAngle(Graph graph, int h, double tolerance)
        {
            var c = graph.CourseOf(h);
            var look = Math.Min(c.Length * 0.25, Math.Max(tolerance * 50, c.Length * 0.01));
            var d = c.PointAt(look) - c.Start;
            return Math.Atan2(d.Y, d.X);
        }

        private static int Next(Graph graph, int h)
        {
            var at = graph.ToOf(h);
            var around = graph.Outgoing[at];
            var twin = around.IndexOf(Graph.Twin(h));
            return around[(twin - 1 + around.Count) % around.Count];
        }

        /// <summary>A point comfortably inside a closed loop, away from its edges.</summary>
        public static P2 PointInside(IList<Course> loop, double tolerance)
        {
            var size = Math.Sqrt(Math.Abs(Loops.SignedArea(loop)));
            foreach (var c in loop.OrderByDescending(c => c.Length))
            foreach (var step in new[] { size * 0.25, size * 0.1, size * 0.02, tolerance * 20 })
            {
                var mid = c.PointAt(c.Length / 2);
                var p = mid + c.DirectionAt(c.Length / 2).LeftNormal() * step;
                if (Inside(loop, p) && !OnOutline(loop, p, tolerance)) return p;
            }
            return loop[0].PointAt(loop[0].Length / 2);
        }

        // =============================================================== merge

        /// <summary>
        /// Joins the kept pieces into one outline. Lines split only because a trim crossed
        /// them come back together as one course, so the courses read like the original
        /// strip. The outline runs the same way as the strip did and starts at its first
        /// corner when that corner is still there.
        /// </summary>
        public static List<Course> Merge(TrimResult split, IEnumerable<int> keep, double tolerance, out string failure)
        {
            failure = null;
            var kept = new HashSet<int>(keep ?? new int[0]);
            if (split == null || !split.Ok || split.Graph == null) { failure = "The easement could not be trimmed."; return null; }
            if (kept.Count == 0) { failure = "No piece is kept -- click at least one piece to keep."; return null; }

            var graph = split.Graph;
            var boundary = new List<int>();
            for (var h = 0; h < graph.FaceOf.Length; h++)
            {
                if (graph.Edges[h / 2].Removed) continue;
                if (kept.Contains(graph.FaceOf[h]) && !kept.Contains(graph.FaceOf[Graph.Twin(h)])) boundary.Add(h);
            }

            var unused = new HashSet<int>(boundary);
            var loops = new List<List<int>>();
            while (unused.Count > 0)
            {
                var first = unused.OrderBy(x => x).First();
                var loop = new List<int>();
                var h = first;
                while (true)
                {
                    loop.Add(h);
                    unused.Remove(h);
                    var at = graph.ToOf(h);
                    // Continue with the next boundary edge clockwise from where we came in.
                    var around = graph.Outgoing[at];
                    var index = around.IndexOf(Graph.Twin(h));
                    var next = -1;
                    for (var k = 1; k <= around.Count; k++)
                    {
                        var candidate = around[(index - k + around.Count * 2) % around.Count];
                        if (candidate == first && loop.Count > 1) { next = first; break; }
                        if (unused.Contains(candidate)) { next = candidate; break; }
                    }
                    if (next < 0 || next == first) break;
                    h = next;
                }
                loops.Add(loop);
            }

            if (loops.Count != 1)
            {
                failure = "The kept pieces do not touch each other. Keep pieces that form one area, or draw the other part as its own easement.";
                return null;
            }

            var courses = loops[0].Select(graph.CourseOf).ToList();
            var parents = loops[0].Select(x => graph.Edges[x / 2].Parent).ToList();
            JoinSplitCourses(courses, parents, tolerance);

            if (Loops.SignedArea(courses) * Loops.SignedArea(split.Strip) < 0) courses = EasementBuilder.Reverse(courses);
            var origin = split.Strip[0].Start;
            var at0 = courses.FindIndex(c => c.Start.DistanceTo(origin) <= tolerance * 2);
            if (at0 > 0) courses = courses.Skip(at0).Concat(courses.Take(at0)).ToList();
            return courses;
        }

        private static void JoinSplitCourses(List<Course> courses, List<int> parents, double tolerance)
        {
            // Rotate so the loop does not start in the middle of a split course.
            for (var turn = 0; turn < courses.Count && courses.Count > 1 && parents[0] == parents[parents.Count - 1]; turn++)
            {
                courses.Insert(0, courses[courses.Count - 1]); courses.RemoveAt(courses.Count - 1);
                parents.Insert(0, parents[parents.Count - 1]); parents.RemoveAt(parents.Count - 1);
            }
            for (var i = courses.Count - 1; i > 0; i--)
            {
                if (parents[i] != parents[i - 1] || courses[i].Kind != courses[i - 1].Kind) continue;
                if (courses[i - 1].End.DistanceTo(courses[i].Start) > tolerance * 2) continue;
                courses[i - 1] = courses[i - 1].WithEnds(courses[i - 1].Start, courses[i].End);
                courses.RemoveAt(i);
                parents.RemoveAt(i);
            }
        }

        // ============================================================ queries

        /// <summary>True when p is inside the closed loop. Exact for arcs: the winding
        /// number is summed course by course, so nothing is approximated.</summary>
        public static bool Inside(IList<Course> loop, P2 p)
        {
            var total = 0.0;
            foreach (var c in loop)
            {
                total += Subtended(c.Start, c.End, p);
                if (c.Kind != CourseKind.Arc) continue;

                // The arc differs from its chord by one turn when p lies in the segment
                // between them.
                if (p.DistanceTo(c.Center) >= c.Radius) continue;
                var chord = c.End - c.Start;
                var middle = c.PointAt(c.Length / 2);
                var sideP = P2.Cross(chord, p - c.Start);
                var sideArc = P2.Cross(chord, middle - c.Start);
                if (sideP * sideArc > 0) total += c.CounterClockwise ? 2 * Math.PI : -2 * Math.PI;
            }
            return Math.Abs(Math.Round(total / (2 * Math.PI))) >= 1;
        }

        private static double Subtended(P2 a, P2 b, P2 p)
        {
            var u = a - p;
            var v = b - p;
            return Math.Atan2(P2.Cross(u, v), P2.Dot(u, v));
        }

        public static bool OnOutline(IList<Course> loop, P2 p, double tolerance)
        {
            return loop.Any(c => Distance(c, p) <= tolerance);
        }

        /// <summary>The parts of an open path that lie inside (or along) a closed loop --
        /// the centerline that belongs to a trimmed easement.</summary>
        public static List<List<Course>> PartsInside(IList<Course> path, IList<Course> loop, double tolerance)
        {
            var parts = new List<List<Course>>();
            List<Course> current = null;
            foreach (var c in path)
            {
                var stations = new List<double> { 0, c.Length };
                foreach (var edge in loop)
                foreach (var p in Intersections.Bounded(c, edge, tolerance))
                    stations.Add(Clamp(c.DistanceOf(p), c.Length));
                stations = stations.OrderBy(x => x).ToList();

                for (var k = 1; k < stations.Count; k++)
                {
                    if (stations[k] - stations[k - 1] <= tolerance) continue;
                    var piece = c.Sub(stations[k - 1], stations[k]);
                    var mid = piece.PointAt(piece.Length / 2);
                    var inside = OnOutline(loop, mid, tolerance) || Inside(loop, mid);
                    if (!inside) { current = null; continue; }
                    if (current == null || current[current.Count - 1].End.DistanceTo(piece.Start) > tolerance)
                    {
                        current = new List<Course>();
                        parts.Add(current);
                    }
                    if (current.Count > 0 && SameCarrier(current[current.Count - 1], piece, tolerance))
                        current[current.Count - 1] = current[current.Count - 1].WithEnds(current[current.Count - 1].Start, piece.End);
                    else
                        current.Add(piece);
                }
            }
            return parts;
        }

        private static bool SameCarrier(Course a, Course b, double tolerance)
        {
            if (a.Kind != b.Kind) return false;
            if (a.Kind == CourseKind.Arc)
                return a.Center.DistanceTo(b.Center) <= tolerance && Math.Abs(a.Radius - b.Radius) <= tolerance &&
                       a.CounterClockwise == b.CounterClockwise;
            var da = a.StartDirection;
            var db = b.StartDirection;
            return P2.Dot(da, db) > 1 - 1e-12 && Math.Abs(P2.Cross(da, b.Start - a.Start)) <= tolerance;
        }
    }
}
