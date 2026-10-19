using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FieldCodes.Compare
{
    /// <summary>
    /// Linework: the physical feature is compared, not the CAD entity. Our line is sampled
    /// along its length; each sample is covered when a consultant line of the same category
    /// runs within the offset tolerance and in the same direction. Runs of covered and
    /// uncovered samples become station intervals, small interior gaps in the consultant's
    /// drafting are bridged, slivers are absorbed, and the line is then matched, missing or
    /// partial. One of ours against many of theirs and many of ours against one of theirs
    /// fall out of the same arithmetic.
    /// </summary>
    public static class LinearMatcher
    {
        internal sealed class Sample
        {
            public double Station;
            public CPoint Point;
            public IntervalKind Kind;
            public string CoveredBy;
            public double Offset;
            public double? Dz;
            public bool NearUnknown;
            public string UnknownLayer;
        }

        public static FeatureResult Match(CompareFeature s, MatchContext ctx)
        {
            var r = FeatureResult.From(s);
            var profile = ctx.Profile;
            var category = profile.Category(s.Class.Category);
            var offTol = ctx.Feet(profile.LineOffsetTolerance(category));
            var angTol = profile.AngleTolerance(category);
            var elevTol = ctx.Feet(profile.ElevationTolerance(category));
            var step = ctx.Feet(profile.Tolerances.SampleStepFeet);
            var gap = ctx.Feet(profile.Tolerances.GapFeet);
            var minUncovered = ctx.Feet(profile.Tolerances.MinUncoveredFeet);

            if (s.Vertices == null || s.Vertices.Count < 2 || s.Length <= 1e-9)
            {
                r.Status = MatchStatus.NotEvaluated;
                r.Note("zero-length linework");
                return r;
            }

            var candidates = ctx.ConsultantLines.QueryFeatures(s.Bounds.Expand(offTol))
                .Where(c => c.Vertices != null && c.Vertices.Count >= 2)
                .ToList();
            var same = candidates.Where(c => !c.Class.IsUnknown && string.Equals(c.Class.Category, s.Class.Category, StringComparison.OrdinalIgnoreCase)).ToList();
            var unknown = candidates.Where(c => c.Class.IsUnknown).ToList();

            // ---- sample ------------------------------------------------------------
            var samples = new List<Sample>();
            foreach (var st in Geometry2.SampleStations(s.Stations, step))
            {
                var p = Geometry2.PointAtStation(s.Vertices, s.Stations, st);
                double dx, dy;
                Geometry2.DirectionAtStation(s.Vertices, s.Stations, st, out dx, out dy);
                var sample = new Sample { Station = st, Point = p, Kind = IntervalKind.Uncovered };
                if (ctx.IsOutsideLimits(p))
                {
                    sample.Kind = IntervalKind.OutsideLimits;
                    samples.Add(sample);
                    continue;
                }
                var best = double.MaxValue;
                foreach (var c in same)
                {
                    if (!c.Bounds.Expand(offTol).Contains(p)) continue;
                    double cst; int seg;
                    var d = Geometry2.ClosestOnPolyline(p, c.Vertices, c.Stations, out cst, out seg);
                    if (d > offTol || d >= best) continue;
                    var a = c.Vertices[seg];
                    var b = c.Vertices[Math.Min(seg + 1, c.Vertices.Count - 1)];
                    if (Geometry2.AngleBetweenUndirected(dx, dy, b.X - a.X, b.Y - a.Y) > angTol) continue;
                    best = d;
                    sample.Kind = IntervalKind.Covered;
                    sample.CoveredBy = c.Id;
                    sample.Offset = d;
                    sample.Dz = Dz(p, c, cst);
                }
                if (sample.Kind != IntervalKind.Covered)
                {
                    foreach (var u in unknown)
                    {
                        if (!u.Bounds.Expand(offTol).Contains(p)) continue;
                        double cst; int seg;
                        if (Geometry2.ClosestOnPolyline(p, u.Vertices, u.Stations, out cst, out seg) <= offTol)
                        {
                            sample.NearUnknown = true;
                            sample.UnknownLayer = u.Layer;
                            break;
                        }
                    }
                }
                samples.Add(sample);
            }

            // ---- intervals ---------------------------------------------------------
            var intervals = Intervals(samples, s.Length);
            Absorb(intervals, gap, minUncovered);
            r.Intervals = intervals;
            r.CoveredLength = intervals.Where(i => i.Kind == IntervalKind.Covered).Sum(i => i.Length);
            r.UncoveredLength = intervals.Where(i => i.Kind == IntervalKind.Uncovered).Sum(i => i.Length);
            r.OutsideLength = intervals.Where(i => i.Kind == IntervalKind.OutsideLimits).Sum(i => i.Length);
            var inBounds = r.CoveredLength + r.UncoveredLength;

            var covering = samples.Where(x => x.Kind == IntervalKind.Covered && x.CoveredBy != null)
                .GroupBy(x => x.CoveredBy).OrderByDescending(g => g.Count()).Select(g => g.Key).ToList();
            foreach (var id in covering)
            {
                r.MatchedIds.Add(id);
                CompareFeature cf;
                if (ctx.ConsultantById.TryGetValue(id, out cf)) r.MatchedHandles.Add(PointMatcher.HandleOf(cf));
            }
            var coveredSamples = samples.Where(x => x.Kind == IntervalKind.Covered).ToList();
            if (coveredSamples.Count > 0) r.HorizontalOffset = coveredSamples.Average(x => x.Offset);

            // ---- status ------------------------------------------------------------
            if (inBounds <= 1e-9)
            {
                r.Status = MatchStatus.Excluded;
                r.Confidence = 1.0;
                r.Note("entirely outside the comparison limits");
                return r;
            }

            var fraction = r.CoveredLength / inBounds;
            var uncoveredNearUnknown = samples.Count(x => x.Kind != IntervalKind.OutsideLimits && x.NearUnknown);
            var inBoundSamples = samples.Count(x => x.Kind != IntervalKind.OutsideLimits);

            if (fraction >= profile.Tolerances.FullCoverageFraction)
            {
                r.Status = MatchStatus.Matched;
                r.Confidence = Confidence(s, ctx, coveredSamples, covering, offTol, r, profile);
            }
            else if (r.CoveredLength < minUncovered || fraction < 0.02)
            {
                if (uncoveredNearUnknown > inBoundSamples * 0.5)
                {
                    r.Status = MatchStatus.NeedsReview;
                    r.Confidence = 0.3;
                    r.Note("unmapped consultant linework runs along it (layer " + samples.First(x => x.NearUnknown).UnknownLayer + "); map that layer in the profile");
                }
                else if (ctx.ConsultantIncomplete)
                {
                    r.Status = MatchStatus.NotEvaluated;
                    r.Confidence = 0;
                    r.Note("consultant data is incomplete (an external reference did not load), so absence proves nothing");
                }
                else
                {
                    r.Status = MatchStatus.Missing;
                    r.Confidence = Math.Max(0.6, s.Class.Confidence);
                    if (r.CoveredLength > 0) r.Note(string.Format(CultureInfo.InvariantCulture, "only {0:0.0} ft coincides with consultant linework; treated as missing", r.CoveredLength / ctx.UnitsPerFoot));
                }
            }
            else
            {
                r.Status = MatchStatus.Partial;
                r.Confidence = Confidence(s, ctx, coveredSamples, covering, offTol, r, profile);
                r.Note(string.Format(CultureInfo.InvariantCulture, "{0:0.0} of {1:0.0} ft represented; {2:0.0} ft not",
                    r.CoveredLength / ctx.UnitsPerFoot, inBounds / ctx.UnitsPerFoot, r.UncoveredLength / ctx.UnitsPerFoot));
                if (uncoveredNearUnknown > 0)
                    r.Note("part of the uncovered length runs along unmapped consultant linework (layer " + samples.First(x => x.NearUnknown).UnknownLayer + ")");
                if (uncoveredNearUnknown > inBoundSamples * 0.25) { r.Status = MatchStatus.NeedsReview; r.Confidence = Math.Min(r.Confidence, 0.4); }
            }

            if (r.OutsideLength > 0)
                r.Note(string.Format(CultureInfo.InvariantCulture, "{0:0.0} ft lies outside the comparison limits", r.OutsideLength / ctx.UnitsPerFoot));

            // Elevation disagreement on 3D linework with real Z on both sides.
            var dzs = coveredSamples.Where(x => x.Dz.HasValue).Select(x => x.Dz.Value).ToList();
            if (dzs.Count > 0 && (r.Status == MatchStatus.Matched || r.Status == MatchStatus.Partial))
            {
                var meanDz = dzs.Average();
                r.VerticalDifference = meanDz;
                if (Math.Abs(meanDz) > elevTol)
                {
                    r.Status = MatchStatus.Discrepancy;
                    r.Note(string.Format(CultureInfo.InvariantCulture, "elevations differ by {0:0.00} ft on average along the matched length", meanDz));
                }
            }
            return r;
        }

        private static double? Dz(CPoint p, CompareFeature c, double station)
        {
            if (!p.HasZ || Math.Abs(p.Z) < 0.01) return null;
            var q = Geometry2.PointAtStation(c.Vertices, c.Stations, station);
            if (!q.HasZ || Math.Abs(q.Z) < 0.01) return null;
            return p.Z - q.Z;
        }

        private static double Confidence(CompareFeature s, MatchContext ctx, List<Sample> covered, List<string> covering, double offTol, FeatureResult r, CompareProfile profile)
        {
            var confidence = s.Class.Confidence;
            foreach (var id in covering)
            {
                CompareFeature c;
                if (!ctx.ConsultantById.TryGetValue(id, out c)) continue;
                confidence = Math.Min(confidence, c.Class.Confidence);
                if (c.Class.Conflict) r.Note("their classification is uncertain: " + c.Class.Evidence);
            }
            if (s.Class.Conflict) { confidence = Math.Min(confidence, 0.5); r.Note("our classification is uncertain: " + s.Class.Evidence); }
            if (covered.Count > 0)
            {
                var ratio = covered.Average(x => x.Offset) / offTol;
                if (ratio > profile.Tolerances.ReviewOffsetFraction)
                {
                    confidence = Math.Min(confidence, 0.6);
                    r.Note(string.Format(CultureInfo.InvariantCulture, "typical offset {0:0.00} ft is close to the {1:0.00} ft tolerance: possibly a parallel feature",
                        covered.Average(x => x.Offset) / ctx.UnitsPerFoot, offTol / ctx.UnitsPerFoot));
                }
            }
            return confidence;
        }

        /// <summary>Runs of equal-kind samples become intervals; the boundary between two
        /// runs sits halfway between the last sample of one and the first of the next.</summary>
        internal static List<CoverageInterval> Intervals(List<Sample> samples, double length)
        {
            var intervals = new List<CoverageInterval>();
            if (samples.Count == 0) return intervals;
            CoverageInterval current = null;
            for (var i = 0; i < samples.Count; i++)
            {
                var x = samples[i];
                if (current == null || current.Kind != x.Kind)
                {
                    var start = current == null ? 0.0 : (samples[i - 1].Station + x.Station) / 2.0;
                    if (current != null) current.End = start;
                    current = new CoverageInterval { Start = start, End = start, Kind = x.Kind };
                    intervals.Add(current);
                }
                if (x.Kind == IntervalKind.Covered && x.CoveredBy != null && !current.CoveredBy.Contains(x.CoveredBy)) current.CoveredBy.Add(x.CoveredBy);
            }
            intervals[intervals.Count - 1].End = length;
            foreach (var iv in intervals)
            {
                var inside = samples.Where(x => x.Kind == IntervalKind.Covered && x.Station >= iv.Start - 1e-9 && x.Station <= iv.End + 1e-9).ToList();
                iv.MeanOffset = inside.Count > 0 ? inside.Average(x => x.Offset) : 0;
            }
            return intervals;
        }

        /// <summary>
        /// Bridges consultant drafting gaps and removes slivers, repeating until stable:
        ///   - an uncovered run no longer than the gap tolerance BETWEEN covered runs is covered;
        ///   - an uncovered run shorter than the minimum beside a covered run is covered
        ///     (no 2 ft plotting stub at the end of a matched curb);
        ///   - a covered run shorter than the minimum beside uncovered runs is uncovered
        ///     (no 2 ft nonplot hole in a line we plot).
        /// Outside-limits intervals never change.
        /// </summary>
        internal static void Absorb(List<CoverageInterval> intervals, double gap, double minUncovered)
        {
            for (var pass = 0; pass < 10; pass++)
            {
                var changed = false;
                for (var i = 0; i < intervals.Count; i++)
                {
                    var iv = intervals[i];
                    if (iv.Kind == IntervalKind.OutsideLimits) continue;
                    var prev = i > 0 ? intervals[i - 1] : null;
                    var next = i < intervals.Count - 1 ? intervals[i + 1] : null;
                    var prevCovered = prev != null && prev.Kind == IntervalKind.Covered;
                    var nextCovered = next != null && next.Kind == IntervalKind.Covered;
                    var prevUncovered = prev != null && prev.Kind == IntervalKind.Uncovered;
                    var nextUncovered = next != null && next.Kind == IntervalKind.Uncovered;

                    if (iv.Kind == IntervalKind.Uncovered)
                    {
                        var bridge = prevCovered && nextCovered && iv.Length <= gap;
                        var sliver = iv.Length < minUncovered && (prevCovered || nextCovered);
                        if (bridge || sliver)
                        {
                            iv.Kind = IntervalKind.Covered;
                            changed = true;
                        }
                    }
                    else if (iv.Kind == IntervalKind.Covered)
                    {
                        if (iv.Length < minUncovered && (prevUncovered || nextUncovered) && !(prevCovered || nextCovered))
                        {
                            iv.Kind = IntervalKind.Uncovered;
                            iv.CoveredBy.Clear();
                            changed = true;
                        }
                    }
                }
                Merge(intervals);
                if (!changed) break;
            }
        }

        private static void Merge(List<CoverageInterval> intervals)
        {
            for (var i = intervals.Count - 1; i > 0; i--)
            {
                var a = intervals[i - 1];
                var b = intervals[i];
                if (a.Kind != b.Kind) continue;
                a.End = b.End;
                foreach (var id in b.CoveredBy) if (!a.CoveredBy.Contains(id)) a.CoveredBy.Add(id);
                a.MeanOffset = (a.MeanOffset + b.MeanOffset) / 2.0;
                intervals.RemoveAt(i);
            }
        }
    }
}
