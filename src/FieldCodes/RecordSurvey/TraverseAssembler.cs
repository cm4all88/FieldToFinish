using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FieldCodes.RecordSurvey
{
    public sealed class AssemblyOptions
    {
        /// <summary>Drawing scale, feet per inch, from the document's scale note or the reviewer.</summary>
        public double ScaleFeetPerInch { get; set; }
        /// <summary>Pixels per inch of the page images.</summary>
        public double Dpi { get; set; }
        /// <summary>How far north is turned from straight up on the page, degrees clockwise. Zero for north up.</summary>
        public double NorthRotationDegrees { get; set; }
        /// <summary>How far a label may sit from the line it annotates, page pixels. Zero picks a third of an inch.</summary>
        public double LabelOffsetTolerancePx { get; set; }
        /// <summary>A loop closes when its last end comes within this of its start, page pixels. Zero picks a tenth of an inch.</summary>
        public double ClosureTolerancePx { get; set; }
        /// <summary>The most courses a figure may have; bounds the search.</summary>
        public int MaxCoursesPerFigure { get; set; }

        public AssemblyOptions() { Dpi = 300; MaxCoursesPerFigure = 24; }
    }

    /// <summary>A figure the assembler proposes: an ordered chain of calls that meets end to end on the page.</summary>
    public sealed class ProposedFigure
    {
        public string Name { get; set; }
        public bool Closed { get; set; }
        public List<string> CallIds { get; private set; }
        public List<bool> Reversed { get; private set; }
        /// <summary>Worst distance between a label and the course it was chained onto, page pixels.</summary>
        public double WorstGapPx { get; set; }
        public double Confidence { get; set; }
        /// <summary>The lot label found inside the loop, when one was.</summary>
        public string LabelInside { get; set; }
        public List<string> Notes { get; private set; }

        public ProposedFigure() { CallIds = new List<string>(); Reversed = new List<bool>(); Notes = new List<string>(); }
    }

    public sealed class AssemblyResult
    {
        public List<ProposedFigure> Figures { get; private set; }
        public List<string> Unplaced { get; private set; }
        public List<string> Problems { get; private set; }
        public AssemblyResult() { Figures = new List<ProposedFigure>(); Unplaced = new List<string>(); Problems = new List<string>(); }
    }

    /// <summary>
    /// Proposes which calls belong together and in what order, from where their labels sit on
    /// the page. A label lies beside its line, somewhere along it; the written bearing and
    /// distance give the line's direction and length on the page. Starting from any course,
    /// the next course is one whose line passes close to the current end and whose label lies
    /// within a course length of it; the chain continues, in page-space traverse, until it
    /// returns to its start (a lot) or nothing meets it (reported, never closed by invention).
    /// A lot line labelled once on the plat may serve two lots.
    /// The picture assists the ordering only: the geometry later built comes from the calls.
    /// </summary>
    public static class TraverseAssembler
    {
        /// <summary>Diagnostic hook: receives one line per trace attempt. Null in production.</summary>
        internal static Action<string> Trace;

        private sealed class Placed
        {
            public SurveyCall Call;
            public double Cx, Cy;        // label centre
            public double Dx, Dy;        // unit direction of the written course, page space (y down)
            public double Length;        // course length, page pixels
            public int Uses;
        }

        private sealed class Step
        {
            public Placed Course;
            public bool Reversed;
            public double StartX, StartY, EndX, EndY;
            public double Gap;
        }

        public static AssemblyResult Assemble(RecordSurveyProject project, AssemblyOptions options)
        {
            var result = new AssemblyResult();
            if (project == null) return result;
            options = options ?? new AssemblyOptions();
            if (options.ScaleFeetPerInch <= 0)
            {
                result.Problems.Add("The drawing scale is unknown, so course positions on the page cannot be estimated. Enter the scale (feet per inch) in the review and re-run the ordering.");
                return result;
            }
            var pixelsPerFoot = options.Dpi / options.ScaleFeetPerInch;
            var offsetTol = options.LabelOffsetTolerancePx > 0 ? options.LabelOffsetTolerancePx : options.Dpi * 0.35;
            var closeTol = options.ClosureTolerancePx > 0 ? options.ClosureTolerancePx : options.Dpi * 0.10;
            var north = options.NorthRotationDegrees * Math.PI / 180.0;

            var placed = new List<Placed>();
            foreach (var call in project.Calls.Where(c => c.Status != CallStatus.Rejected))
            {
                var p = Estimate(call, pixelsPerFoot, north);
                if (p == null) { result.Unplaced.Add(call.Id); continue; }
                placed.Add(p);
            }

            var n = 0;
            foreach (var seed in placed.OrderByDescending(p => p.Length))
            {
                if (seed.Uses > 0) continue;

                // Trace the face to the right of the seed, seed taken both ways, always turning as far
                // right as the page allows: that walks around one lot and never wanders off along a
                // neighbour's line. The smaller of the two closed faces is the lot; the larger, when
                // both close, is the plat boundary seen from the other side of the seed.
                List<Step> found = null;
                var bestArea = double.MaxValue;
                foreach (var seedReversed in new[] { false, true })
                {
                    var chain = new List<Step> { SeedStep(seed, seedReversed) };
                    var ok = TraceRightmost(chain, placed, offsetTol, closeTol, options.MaxCoursesPerFigure);
                    if (Trace != null) Trace(seed.Call.Id + (seedReversed ? "r" : "") + " -> " + string.Join(",", chain.Select(c => c.Course.Call.Id + (c.Reversed ? "r" : "")).ToArray()) + (ok ? " closed area " + Math.Abs(Area(chain)).ToString("0") : " open"));
                    if (!ok) continue;
                    var area = Math.Abs(Area(chain));
                    if (area < bestArea) { bestArea = area; found = chain; }
                }
                List<Step> closed = found;
                if (found == null)
                {
                    // Nothing closes by the rule: search for any closure, else keep the longest open chain.
                    var chain = new List<Step> { SeedStep(seed, false) };
                    var budget = 4000;
                    var longest = new List<Step>(chain);
                    Search(chain, placed, offsetTol, closeTol, options.MaxCoursesPerFigure, ref budget, ref closed, longest);
                    found = closed ?? longest;
                }
                foreach (var s in found) s.Course.Uses++;

                n++;
                var figure = new ProposedFigure { Closed = closed != null, WorstGapPx = found.Count > 1 ? found.Skip(1).Max(s => s.Gap) : 0 };
                figure.CallIds.AddRange(found.Select(s => s.Course.Call.Id));
                figure.Reversed.AddRange(found.Select(s => s.Reversed));
                figure.Confidence = found.Count == 1 ? 0.3 : Math.Max(0.0, 1.0 - figure.WorstGapPx / (2.0 * offsetTol));
                if (figure.Closed)
                {
                    var label = LabelInside(project, found);
                    figure.LabelInside = label;
                    figure.Name = label ?? "Figure " + n.ToString(CultureInfo.InvariantCulture);
                    if (label == null) figure.Notes.Add("No lot or tract label was found inside this loop; name it in the review.");
                }
                else
                {
                    figure.Name = "Chain " + n.ToString(CultureInfo.InvariantCulture);
                    var last = found[found.Count - 1];
                    var gap = Dist(last.EndX, last.EndY, found[0].StartX, found[0].StartY);
                    figure.Notes.Add(found.Count == 1
                        ? "This course meets no other course on the page; assign it to a figure in the review."
                        : "Open chain: the ends are " + gap.ToString("0", CultureInfo.InvariantCulture) + " px apart on the page. A course may be unread; nothing is added to close it.");
                }
                result.Figures.Add(figure);
            }
            return result;
        }

        private static Step SeedStep(Placed seed, bool reversed)
        {
            var dx = reversed ? -seed.Dx : seed.Dx;
            var dy = reversed ? -seed.Dy : seed.Dy;
            var step = new Step { Course = seed, Reversed = reversed, StartX = seed.Cx - dx * seed.Length / 2, StartY = seed.Cy - dy * seed.Length / 2 };
            step.EndX = step.StartX + dx * seed.Length;
            step.EndY = step.StartY + dy * seed.Length;
            return step;
        }

        /// <summary>Greedy face trace: from each end take the candidate that turns furthest right. True when the chain closed.</summary>
        private static bool TraceRightmost(List<Step> chain, List<Placed> placed, double offsetTol, double closeTol, int maxCourses)
        {
            while (chain.Count < maxCourses)
            {
                var tail = chain[chain.Count - 1];
                var head = chain[0];
                if (chain.Count >= 3 && Closes(tail, head, closeTol, offsetTol))
                {
                    double cx, cy;
                    if (Intersect(tail, head, out cx, out cy))
                    {
                        var d = Dir(head);
                        head.StartX = cx; head.StartY = cy;
                        head.EndX = cx + d.Item1 * head.Course.Length; head.EndY = cy + d.Item2 * head.Course.Length;
                    }
                    return true;
                }
                var dt = Dir(tail);
                Step best = null;
                var bestTurn = double.MinValue;
                foreach (var candidate in Candidates(placed, chain, tail.EndX, tail.EndY, offsetTol))
                {
                    var dc = Dir(candidate);
                    // Page coordinates run y-down, so a positive cross product is a turn to the right of travel.
                    var turn = Math.Atan2(dt.Item1 * dc.Item2 - dt.Item2 * dc.Item1, dt.Item1 * dc.Item1 + dt.Item2 * dc.Item2);
                    if (turn > bestTurn) { bestTurn = turn; best = candidate; }
                }
                if (best == null) return false;
                chain.Add(best);
            }
            return false;
        }

        /// <summary>Signed area of the chain's corner polygon, page pixels squared.</summary>
        private static double Area(List<Step> chain)
        {
            var a = 0.0;
            for (var i = 0; i < chain.Count; i++)
            {
                var p = chain[i]; var q = chain[(i + 1) % chain.Count];
                a += p.StartX * q.StartY - q.StartX * p.StartY;
            }
            return a / 2.0;
        }

        /// <summary>Depth-first search for a chain that returns to its start; keeps the longest open chain otherwise.</summary>
        private static void Search(List<Step> chain, List<Placed> placed, double offsetTol, double closeTol, int maxCourses,
                                   ref int budget, ref List<Step> closed, List<Step> longest)
        {
            if (closed != null || budget <= 0) return;
            budget--;
            var tail = chain[chain.Count - 1];
            var head = chain[0];
            if (chain.Count >= 3 && Closes(tail, head, closeTol, offsetTol))
            {
                closed = new List<Step>(chain);
                // The seed's start was assumed at its label's middle; the loop says where it really is.
                double cx, cy;
                if (Intersect(tail, head, out cx, out cy)) { head.StartX = cx; head.StartY = cy; head.EndX = cx + Dir(head).Item1 * head.Course.Length; head.EndY = cy + Dir(head).Item2 * head.Course.Length; }
                return;
            }
            if (chain.Count > longest.Count) { longest.Clear(); longest.AddRange(chain); }
            if (chain.Count >= maxCourses) return;

            foreach (var step in Candidates(placed, chain, tail.EndX, tail.EndY, offsetTol))
            {
                chain.Add(step);
                Search(chain, placed, offsetTol, closeTol, maxCourses, ref budget, ref closed, longest);
                if (closed != null) return;
                chain.RemoveAt(chain.Count - 1);
            }
        }

        /// <summary>
        /// Courses that can start (or, reversed, end) at the point: their line passes within the
        /// offset tolerance of it and their label lies within a course length of it along the
        /// line. Best first: nearest line, then label nearest the course's middle. Unused courses
        /// before ones already in another figure; never one already in this chain.
        /// </summary>
        private static IEnumerable<Step> Candidates(List<Placed> placed, List<Step> chain, double x, double y, double offsetTol)
        {
            var inChain = new HashSet<Placed>(chain.Select(s => s.Course));
            var tail = chain[chain.Count - 1];
            var dt = Dir(tail);
            var list = new List<KeyValuePair<double, Step>>();
            foreach (var p in placed)
            {
                if (inChain.Contains(p) || p.Uses >= 2) continue;
                var rx = x - p.Cx;
                var ry = y - p.Cy;
                var perp = Math.Abs(p.Dx * ry - p.Dy * rx);
                if (perp > offsetTol) continue;
                foreach (var reversed in new[] { false, true })
                {
                    var dx = reversed ? -p.Dx : p.Dx;
                    var dy = reversed ? -p.Dy : p.Dy;
                    // Doubling back along the line just travelled is never the next course of a figure.
                    if (dt.Item1 * dx + dt.Item2 * dy < Math.Cos(165.0 * Math.PI / 180.0)) continue;
                    // The corner is where the two courses' lines cross, as a drafter would find it;
                    // a course continuing nearly straight on has no useful crossing and starts at the end itself.
                    double sx, sy;
                    if (!Intersect(tail.StartX, tail.StartY, tail.EndX - tail.StartX, tail.EndY - tail.StartY, p.Cx, p.Cy, dx, dy, out sx, out sy)) { sx = x; sy = y; }
                    // The label must lie along the course from that corner (a little slack either way).
                    var t = dx * (p.Cx - sx) + dy * (p.Cy - sy);
                    var slack = p.Length * 0.15 + offsetTol;
                    if (t < -slack || t > p.Length + slack) continue;
                    // And the corner must be close to where the previous course ended.
                    var gap = Dist(sx, sy, x, y);
                    if (gap > offsetTol * 1.5) continue;
                    var step = new Step { Course = p, Reversed = reversed, StartX = sx, StartY = sy, EndX = sx + dx * p.Length, EndY = sy + dy * p.Length, Gap = Math.Max(perp, gap) };
                    list.Add(new KeyValuePair<double, Step>(Score(perp, offsetTol, t, p.Length / 2, p.Length, p.Uses) + gap / offsetTol, step));
                }
            }
            return list.OrderBy(kv => kv.Key).Select(kv => kv.Value);
        }

        private static bool Closes(Step tail, Step head, double closeTol, double offsetTol)
        {
            var d = Dir(head);
            var rx = tail.EndX - head.Course.Cx;
            var ry = tail.EndY - head.Course.Cy;
            var perp = Math.Abs(d.Item1 * ry - d.Item2 * rx);
            if (perp > offsetTol) return false;
            // The last course ends on the seed's line, before the seed's label: that is the seed's start.
            var t = d.Item1 * (head.Course.Cx - tail.EndX) + d.Item2 * (head.Course.Cy - tail.EndY);
            var slack = head.Course.Length * 0.15 + offsetTol;
            if (t < -slack || t > head.Course.Length + slack) return false;
            return Dist(tail.EndX, tail.EndY, head.StartX, head.StartY) <= Math.Max(closeTol, offsetTol * 1.5 + head.Course.Length * 0.15);
        }

        private static Tuple<double, double> Dir(Step s)
        {
            return Tuple.Create(s.Reversed ? -s.Course.Dx : s.Course.Dx, s.Reversed ? -s.Course.Dy : s.Course.Dy);
        }

        private static bool Intersect(Step a, Step b, out double x, out double y)
        {
            var db = Dir(b);
            return Intersect(a.StartX, a.StartY, a.EndX - a.StartX, a.EndY - a.StartY, b.Course.Cx, b.Course.Cy, db.Item1, db.Item2, out x, out y);
        }

        /// <summary>Where line (p, d) crosses line (q, e); false when they are within 5° of parallel.</summary>
        private static bool Intersect(double px, double py, double dx, double dy, double qx, double qy, double ex, double ey, out double x, out double y)
        {
            x = y = 0;
            var denom = dx * ey - dy * ex;
            var la = Math.Sqrt(dx * dx + dy * dy);
            var lb = Math.Sqrt(ex * ex + ey * ey);
            if (la < 1e-9 || lb < 1e-9 || Math.Abs(denom) < la * lb * Math.Sin(5.0 * Math.PI / 180.0)) return false;
            var t = ((qx - px) * ey - (qy - py) * ex) / denom;
            x = px + dx * t;
            y = py + dy * t;
            return true;
        }

        private static double Score(double perp, double offsetTol, double t, double expectedT, double length, int uses)
        {
            return perp / offsetTol + Math.Abs(t - expectedT) / Math.Max(1.0, length) + uses * 0.5;
        }

        /// <summary>Writes a proposal onto the project: figures, order and reversal flags. Existing hand ordering is kept unless overwrite is set.</summary>
        public static void Apply(RecordSurveyProject project, AssemblyResult assembly, bool overwrite)
        {
            foreach (var f in assembly.Figures)
            {
                if (f.CallIds.Count == 0) continue;
                var existing = project.Figures.FirstOrDefault(x => string.Equals(x.Name, f.Name, StringComparison.OrdinalIgnoreCase));
                if (existing == null)
                {
                    existing = new SurveyFigure { Name = f.Name, Kind = f.Name.StartsWith("Lot", StringComparison.OrdinalIgnoreCase) ? "Lot" : f.Name.StartsWith("Tract", StringComparison.OrdinalIgnoreCase) ? "Tract" : "Figure" };
                    project.Figures.Add(existing);
                }
                existing.Closed = f.Closed;
                for (var i = 0; i < f.CallIds.Count; i++)
                {
                    var call = project.FindCall(f.CallIds[i]);
                    if (call == null) continue;
                    if (!string.IsNullOrEmpty(call.Figure) && call.Order > 0 && !string.Equals(call.Figure, existing.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        // Already a course of another figure: this figure gets a copy that points back at
                        // it, so the shared line is built once and both lots know the same call.
                        var already = project.Calls.FirstOrDefault(c => c.SharedWith == call.Id && string.Equals(c.Figure, existing.Name, StringComparison.OrdinalIgnoreCase));
                        if (already != null)
                        {
                            if (overwrite) { already.Order = i + 1; already.Reversed = f.Reversed[i]; }
                            f.CallIds[i] = already.Id;
                            continue;
                        }
                        var copy = call.Clone();
                        copy.Id = project.NextCallId();
                        copy.SharedWith = call.Id;
                        copy.Figure = existing.Name;
                        copy.Order = i + 1;
                        copy.Reversed = f.Reversed[i];
                        copy.Notes.Add("Shared course: the document labels this line once; it also bounds " + call.Figure + " as " + call.Id + ".");
                        project.Calls.Add(copy);
                        f.CallIds[i] = copy.Id;
                        continue;
                    }
                    if (!overwrite && !string.IsNullOrEmpty(call.Figure) && call.Order > 0) continue;
                    call.Figure = existing.Name;
                    call.Order = i + 1;
                    call.Reversed = f.Reversed[i];
                }
            }
        }

        private static Placed Estimate(SurveyCall call, double pixelsPerFoot, double north)
        {
            if (call.PageHint == null) return null;
            double azimuth, length;
            if (call.Kind == CallKind.Line)
            {
                ValueBasis b;
                var v = call.GeometryValue(true, out b);
                if (v == null) return null;
                azimuth = v.AzimuthDegrees.Value;
                length = v.DistanceFeet.Value;
            }
            else
            {
                if (call.Curve == null || !call.Curve.ChordAzimuthDegrees.HasValue) return null;
                var s = CurveSolver.Solve(call.Curve, 0.02, 10.0);
                if (!s.Ok) return null;
                azimuth = call.Curve.ChordAzimuthDegrees.Value;
                length = s.ChordLength;
            }
            // Page pixels: x right, y down; north points up the page when the north rotation is zero.
            var a = azimuth * Math.PI / 180.0 + north;
            return new Placed { Call = call, Cx = call.PageHint.CenterX, Cy = call.PageHint.CenterY, Dx = Math.Sin(a), Dy = -Math.Cos(a), Length = length * pixelsPerFoot };
        }

        private static double Dist(double x0, double y0, double x1, double y1)
        {
            var dx = x1 - x0; var dy = y1 - y0;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>The lot or tract label whose box centre lies inside the loop's page polygon.</summary>
        private static string LabelInside(RecordSurveyProject project, List<Step> chain)
        {
            var poly = chain.Select(s => new[] { s.StartX, s.StartY }).ToList();
            foreach (var f in project.Figures.Where(f => f.PageHint != null))
                if (Inside(poly, f.PageHint.CenterX, f.PageHint.CenterY)) return f.Name;
            // Older plats number lots with a bare figure in the middle of the lot.
            var numbers = project.Annotations.Where(a => a.Kind == SurveyEntityKind.Number && a.Source != null && a.Source.Box != null &&
                                                         Inside(poly, a.Source.Box.CenterX, a.Source.Box.CenterY)).ToList();
            if (numbers.Count == 1) return "Lot " + numbers[0].Text;
            return null;
        }

        internal static bool Inside(List<double[]> poly, double x, double y)
        {
            var inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                var xi = poly[i][0]; var yi = poly[i][1];
                var xj = poly[j][0]; var yj = poly[j][1];
                if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi) inside = !inside;
            }
            return inside;
        }
    }
}
