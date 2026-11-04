using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FieldCodes.Easements;

namespace FieldCodes.Wetlands
{
    /// <summary>A line of hard surface (edge of asphalt, concrete, gravel, curb, building) the
    /// buffer stops at. Read only: it is never changed.</summary>
    public sealed class HardSurfaceLine
    {
        public HardSurfaceLine(string id, string layer, IEnumerable<Course> courses)
        {
            Id = id;
            Layer = layer;
            Courses = courses.ToList();
        }

        /// <summary>The object's handle, for messages.</summary>
        public string Id { get; private set; }
        public string Layer { get; private set; }
        public List<Course> Courses { get; private set; }
    }

    /// <summary>A part of the buffer removed because a hard surface lies between it and the wetland.</summary>
    public sealed class TrimmedPiece
    {
        public List<Course> Courses { get; internal set; }
        public double Length { get { return Courses.Sum(c => c.Length); } }
        /// <summary>The hard-surface line that cuts it off from the wetland.</summary>
        public HardSurfaceLine BlockedBy { get; internal set; }
        public P2 Middle { get; internal set; }
    }

    /// <summary>Something the drafter should look at, and where.</summary>
    public sealed class BufferNote
    {
        public string Text { get; internal set; }
        public P2 Where { get; internal set; }

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "{0} (near {1:0.00}, {2:0.00})", Text, Where.X, Where.Y);
        }
    }

    public sealed class WetlandBufferRequest
    {
        public WetlandBufferRequest()
        {
            Tolerance = RoundOffset.DefaultTolerance;
            Side = 1;
        }

        /// <summary>The wetland edge, as drawn.</summary>
        public IList<Course> Wetland { get; set; }

        /// <summary>A closed wetland is buffered outward; an open edge to one side.</summary>
        public bool Closed { get; set; }

        /// <summary>For an open edge: +1 buffers to the left of its direction, -1 to the right.</summary>
        public int Side { get; set; }

        /// <summary>The buffer width, in drawing units.</summary>
        public double Distance { get; set; }

        /// <summary>Rounds the inside corners too, with this radius (0 = none). Done as an offset
        /// out by distance + radius and back in by radius, so the buffer is never closer than
        /// the distance.</summary>
        public double Smoothing { get; set; }

        /// <summary>Hard-surface lines to stop the buffer at; null or empty for none.</summary>
        public IList<HardSurfaceLine> HardSurfaces { get; set; }

        public double Tolerance { get; set; }
    }

    public sealed class WetlandBufferResult
    {
        public WetlandBufferResult()
        {
            Paths = new List<BufferPath>();
            Trimmed = new List<TrimmedPiece>();
            Notes = new List<BufferNote>();
            Problems = new List<string>();
        }

        /// <summary>The buffer lines to draw.</summary>
        public List<BufferPath> Paths { get; private set; }

        /// <summary>What hard surfaces cut away.</summary>
        public List<TrimmedPiece> Trimmed { get; private set; }

        public List<BufferNote> Notes { get; private set; }

        /// <summary>Why nothing should be drawn.</summary>
        public List<string> Problems { get; private set; }

        public bool Ok { get { return Problems.Count == 0 && Paths.Count > 0; } }

        /// <summary>The closest the buffer comes to the wetland: the distance itself, unless
        /// something went wrong (then it is also a problem).</summary>
        public double ClosestApproach { get; internal set; }
    }

    /// <summary>
    /// FTFWETLANDBUFFER's geometry: the wetland buffer line, offset with round tangent corners
    /// at the true buffer distance, optionally with its inside corners rounded, and cut back
    /// where a hard surface lies between it and the wetland.
    ///
    /// Hard surfaces: the buffer is cut wherever a hard-surface line crosses it. A piece between
    /// two cuts is removed when the straight way from every part of it to the nearest point of
    /// the wetland crosses a hard-surface line -- the buffer stops at the road edge rather than
    /// running on across the road. A piece only partly behind a hard surface (a building inside
    /// the buffer, the end of a driveway edge) is kept and noted for the drafter to check. The
    /// wetland and the hard-surface lines are never changed.
    /// </summary>
    public static class WetlandBuffer
    {
        /// <summary>The fewest and most samples along a piece for the hard-surface test; between
        /// them, one every quarter of the buffer distance.</summary>
        private const int FewestSamples = 9;
        private const int MostSamples = 2000;

        public static WetlandBufferResult Build(WetlandBufferRequest request)
        {
            var result = new WetlandBufferResult();
            if (request == null || request.Wetland == null || request.Wetland.Count == 0)
            {
                result.Problems.Add("No wetland line to buffer.");
                return result;
            }
            if (!(request.Distance > 0)) { result.Problems.Add("The buffer distance must be greater than zero."); return result; }
            if (request.Smoothing < 0) { result.Problems.Add("The smoothing radius cannot be negative."); return result; }
            var tol = request.Tolerance > 0 ? request.Tolerance : RoundOffset.DefaultTolerance;

            var frame = Frame.Of(request.Wetland);
            var wetland = frame.In(request.Wetland);
            if (request.Closed && Loops.SignedArea(wetland) < 0) wetland = Reverse(wetland);
            var hard = (request.HardSurfaces ?? new List<HardSurfaceLine>())
                .Select(h => new HardSurfaceLine(h.Id, h.Layer, frame.In(h.Courses))).ToList();

            List<BufferPath> raw;
            try
            {
                raw = Offset(wetland, request.Closed, request.Side, request.Distance, request.Smoothing, tol);
            }
            catch (ArgumentException ex)
            {
                result.Problems.Add(ex.Message);
                return result;
            }
            if (raw.Count == 0)
            {
                result.Problems.Add("The offset came to nothing: the distance is too large for this line, or the line doubles back on itself.");
                return result;
            }

            // Never closer than the buffer distance: checked on what is about to be drawn.
            var closest = raw.SelectMany(p => p.Courses).SelectMany(c => new[] { c.Start, c.PointAt(c.Length / 2), c.End })
                             .Select(p => RoundOffset.DistanceTo(wetland, p)).DefaultIfEmpty(request.Distance).Min();
            result.ClosestApproach = closest;
            if (closest < request.Distance - Math.Max(1e-4, request.Distance * 1e-6))
            {
                result.Problems.Add(string.Format(CultureInfo.InvariantCulture,
                    "The buffer came {0:0.###} from the wetland, closer than {1:0.###}; nothing was drawn. Check the wetland line for overlaps or duplicate vertices.",
                    closest, request.Distance));
                return result;
            }

            var kept = raw;
            if (hard.Count > 0)
            {
                var trim = CutBack(raw, wetland, request.Closed, hard, request.Distance / 4, tol);
                kept = trim.Kept;
                foreach (var piece in trim.Removed)
                    result.Trimmed.Add(new TrimmedPiece
                    {
                        Courses = piece.Courses.Select(frame.Out).ToList(),
                        BlockedBy = request.HardSurfaces.First(h => h.Id == piece.BlockedBy.Id && h.Layer == piece.BlockedBy.Layer),
                        Middle = frame.Out(piece.Middle)
                    });
                foreach (var note in trim.Notes)
                    result.Notes.Add(new BufferNote { Text = note.Text, Where = frame.Out(note.Where) });
                if (kept.Count == 0)
                    result.Notes.Add(new BufferNote
                    {
                        Text = "Hard surfaces cut off the whole buffer: nothing is drawn for this wetland",
                        Where = frame.Out(wetland[0].Start)
                    });
            }

            result.Paths.AddRange(frame.Out(kept));
            return result;
        }

        /// <summary>Which side of an open edge a picked point is on, by the part of the edge
        /// nearest it: +1 left of its direction, -1 right.</summary>
        public static int SideOf(IList<Course> edge, P2 picked)
        {
            if (edge == null || edge.Count == 0) throw new ArgumentException("No edge.");
            Course best = null;
            var bestAlong = 0.0;
            var bestPoint = picked;
            var bestDistance = double.MaxValue;
            foreach (var c in edge)
            {
                double along;
                var q = c.Closest(picked, out along);
                var d = q.DistanceTo(picked);
                if (d < bestDistance) { bestDistance = d; best = c; bestAlong = along; bestPoint = q; }
            }
            var cross = P2.Cross(best.DirectionAt(bestAlong), picked - bestPoint);
            return cross >= 0 ? 1 : -1;
        }

        private static List<BufferPath> Offset(List<Course> wetland, bool closed, int side, double d, double r, double tol)
        {
            if (closed)
            {
                if (r <= 0) return RoundOffset.Outside(wetland, d, tol);
                var wide = RoundOffset.Outside(wetland, d + r, tol).Where(p => p.Closed).Select(p => (IList<Course>)p.Courses).ToList();
                return wide.Count == 0 ? new List<BufferPath>() : RoundOffset.Inside(wide, r, tol);
            }
            if (r <= 0) return RoundOffset.Side(wetland, side, d, tol);
            // Out by d + r, then back toward the edge by r: the inside corners come out round.
            var result = new List<BufferPath>();
            foreach (var wide in RoundOffset.Side(wetland, side, d + r, tol))
                result.AddRange(RoundOffset.Side(wide.Courses, -side, r, tol));
            return result;
        }

        // ================================================================= hard surfaces

        internal sealed class TrimOutcome
        {
            public List<BufferPath> Kept = new List<BufferPath>();
            public List<TrimmedPiece> Removed = new List<TrimmedPiece>();
            public List<BufferNote> Notes = new List<BufferNote>();
        }

        private sealed class Run
        {
            public List<Course> Courses = new List<Course>();
            public bool Removed;
        }

        internal static TrimOutcome CutBack(IList<BufferPath> buffer, IList<Course> wetland, bool wetlandClosed,
                                         IList<HardSurfaceLine> hard, double spacing, double tol)
        {
            var outcome = new TrimOutcome();
            var hardCourses = hard.SelectMany(h => h.Courses.Select(c => new KeyValuePair<HardSurfaceLine, Course>(h, c))).ToList();
            var hardBoxes = hardCourses.Select(h => Box.Of(h.Value, tol * 10)).ToList();

            foreach (var path in buffer)
            {
                // Cut where a hard-surface line crosses.
                var pieces = new List<KeyValuePair<Course, bool>>();     // piece, cut after it
                foreach (var c in path.Courses)
                {
                    var box = Box.Of(c, tol * 10);
                    var stations = new List<double>();
                    for (var k = 0; k < hardCourses.Count; k++)
                    {
                        if (!box.Overlaps(hardBoxes[k])) continue;
                        foreach (var p in Intersections.Bounded(c, hardCourses[k].Value, tol))
                            stations.Add(Math.Max(0, Math.Min(c.Length, c.DistanceOf(p))));
                    }
                    var length = c.Length;
                    var shortest = tol * 10;
                    var sorted = stations.OrderBy(s => s).ToList();
                    var from = 0.0;
                    var cutAtStart = sorted.Any(s => s <= shortest);
                    if (cutAtStart && pieces.Count > 0)
                        pieces[pieces.Count - 1] = new KeyValuePair<Course, bool>(pieces[pieces.Count - 1].Key, true);
                    foreach (var s in sorted.Where(s => s > shortest && s < length - shortest))
                    {
                        if (s - from <= shortest) continue;
                        pieces.Add(new KeyValuePair<Course, bool>(c.Sub(from, s), true));
                        from = s;
                    }
                    pieces.Add(new KeyValuePair<Course, bool>(from > 0 ? c.Sub(from, length) : c, sorted.Any(s => s >= length - shortest)));
                }
                if (pieces.Count == 0) continue;

                var cuts = pieces.Count(p => p.Value);
                if (cuts == 0)
                {
                    // Nothing crosses it: the whole line is kept, unless all of it is behind a
                    // hard surface (a ring road nearer than the buffer distance).
                    var whole = new Run { Courses = path.Courses.ToList() };
                    Decide(whole, wetland, wetlandClosed, hardCourses, hardBoxes, spacing, tol, outcome);
                    if (!whole.Removed) outcome.Kept.Add(path);
                    continue;
                }

                // A closed line starts its first run just after a cut.
                if (path.Closed)
                {
                    var firstCut = pieces.FindIndex(p => p.Value);
                    pieces = pieces.Skip(firstCut + 1).Concat(pieces.Take(firstCut + 1)).ToList();
                }
                var runs = new List<Run>();
                var current = new Run();
                foreach (var p in pieces)
                {
                    current.Courses.Add(p.Key);
                    if (p.Value) { runs.Add(current); current = new Run(); }
                }
                if (current.Courses.Count > 0) runs.Add(current);

                foreach (var run in runs) Decide(run, wetland, wetlandClosed, hardCourses, hardBoxes, spacing, tol, outcome);

                // Kept runs next to each other stay one line (a crossing that removed nothing
                // does not break the polyline).
                var groups = new List<List<Course>>();
                List<Course> group = null;
                foreach (var run in runs)
                {
                    if (run.Removed) { group = null; continue; }
                    if (group == null) { group = new List<Course>(); groups.Add(group); }
                    group.AddRange(run.Courses);
                }
                if (runs.All(x => !x.Removed) && path.Closed)
                {
                    outcome.Kept.Add(new BufferPath(RoundOffset.Merge(groups[0], true), true));
                    continue;
                }
                if (path.Closed && groups.Count > 1 && !runs[0].Removed && !runs[runs.Count - 1].Removed)
                {
                    // The last group runs on into the first around the closing point.
                    groups[groups.Count - 1].AddRange(groups[0]);
                    groups.RemoveAt(0);
                }
                foreach (var g in groups) outcome.Kept.Add(new BufferPath(RoundOffset.Merge(g, false), false));
            }
            return outcome;
        }

        /// <summary>Removes a run when every sample of it is cut off from the wetland by a hard surface.</summary>
        private static void Decide(Run run, IList<Course> wetland, bool wetlandClosed,
                                   List<KeyValuePair<HardSurfaceLine, Course>> hard, List<Box> hardBoxes, double spacing, double tol,
                                   TrimOutcome outcome)
        {
            var length = run.Courses.Sum(c => c.Length);
            if (length <= 0) return;
            var samples = spacing > 0 ? (int)Math.Min(MostSamples, Math.Max(FewestSamples, Math.Ceiling(length / spacing))) : FewestSamples;
            var blocked = 0;
            HardSurfaceLine first = null;
            P2 firstWhere = new P2(0, 0);
            for (var i = 0; i < samples; i++)
            {
                var s = PointAlong(run.Courses, length * (i + 0.5) / samples);
                var by = Blocker(s, wetland, wetlandClosed, hard, hardBoxes, tol);
                if (by == null) continue;
                blocked++;
                if (first == null) { first = by; firstWhere = s; }
            }
            var middle = PointAlong(run.Courses, length / 2);
            if (blocked == samples)
            {
                run.Removed = true;
                outcome.Removed.Add(new TrimmedPiece { Courses = run.Courses.ToList(), BlockedBy = first, Middle = middle });
            }
            else if (blocked > 0)
            {
                outcome.Notes.Add(new BufferNote
                {
                    Text = string.Format(CultureInfo.InvariantCulture,
                        "Part of the buffer is behind {0} (handle {1}), but that line does not cross the buffer there; the buffer was kept: check it",
                        first.Layer, first.Id),
                    Where = firstWhere
                });
            }
        }

        /// <summary>The hard-surface line between a point and the nearest point of the wetland, or null.</summary>
        private static HardSurfaceLine Blocker(P2 s, IList<Course> wetland, bool wetlandClosed,
                                               List<KeyValuePair<HardSurfaceLine, Course>> hard, List<Box> hardBoxes, double tol)
        {
            if (wetlandClosed && StripTrim.Inside(wetland, s)) return null;
            var best = double.MaxValue;
            var w = s;
            foreach (var c in wetland)
            {
                double along;
                var q = c.Closest(s, out along);
                var d = q.DistanceTo(s);
                if (d < best) { best = d; w = q; }
            }
            if (best <= tol) return null;
            var way = Course.Line(s, w);
            var box = Box.Of(way, tol * 10);
            var near = tol * 100;
            for (var k = 0; k < hard.Count; k++)
            {
                if (!box.Overlaps(hardBoxes[k])) continue;
                foreach (var p in Intersections.Bounded(way, hard[k].Value, tol))
                    if (p.DistanceTo(s) > near && p.DistanceTo(w) > near) return hard[k].Key;
            }
            return null;
        }

        private static P2 PointAlong(IList<Course> courses, double distance)
        {
            foreach (var c in courses)
            {
                if (distance <= c.Length) return c.PointAt(distance);
                distance -= c.Length;
            }
            var last = courses[courses.Count - 1];
            return last.End;
        }

        private static List<Course> Reverse(IList<Course> loop)
        {
            var list = loop.Select(c => c.Reversed()).ToList();
            list.Reverse();
            return list;
        }
    }
}
