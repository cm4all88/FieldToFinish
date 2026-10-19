using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FieldCodes.Compare
{
    /// <summary>
    /// Point features: the nearest consultant feature of the SAME category within the
    /// category's tolerance is the same object. A nearby feature of another type in the
    /// same system (catch basin where we shot a manhole) is a question for review, never
    /// a match; a different system (sanitary against storm) is not a candidate at all.
    /// </summary>
    public static class PointMatcher
    {
        public static FeatureResult Match(CompareFeature s, MatchContext ctx)
        {
            var r = FeatureResult.From(s);
            var profile = ctx.Profile;
            var category = profile.Category(s.Class.Category);
            var tol = ctx.Feet(profile.PointTolerance(category));
            var elevTol = ctx.Feet(profile.ElevationTolerance(category));

            if (ctx.IsOutsideLimits(s.Position))
            {
                r.Status = MatchStatus.Excluded;
                r.Confidence = 1.0;
                r.Note("outside the comparison limits");
                return r;
            }

            var nearby = ctx.ConsultantPoints.QueryFeatures(Box2.Of(s.Position).Expand(tol))
                .Select(c => new { Feature = c, Distance = c.Position.DistanceTo(s.Position) })
                .Where(x => x.Distance <= tol)
                .OrderBy(x => x.Distance)
                .ToList();

            var same = nearby.Where(x => !x.Feature.Class.IsUnknown && string.Equals(x.Feature.Class.Category, s.Class.Category, StringComparison.OrdinalIgnoreCase)).ToList();
            var sameSystem = nearby.Where(x => !x.Feature.Class.IsUnknown && !string.Equals(x.Feature.Class.Category, s.Class.Category, StringComparison.OrdinalIgnoreCase)
                                                && string.Equals(x.Feature.Class.System, s.Class.System, StringComparison.OrdinalIgnoreCase)).ToList();
            var unknown = nearby.Where(x => x.Feature.Class.IsUnknown).ToList();
            var other = nearby.Where(x => !x.Feature.Class.IsUnknown && !string.Equals(x.Feature.Class.System, s.Class.System, StringComparison.OrdinalIgnoreCase)).ToList();

            if (same.Count > 0)
            {
                var best = same[0];
                r.Status = MatchStatus.Matched;
                r.MatchedIds.Add(best.Feature.Id);
                r.MatchedHandles.Add(HandleOf(best.Feature));
                r.HorizontalOffset = best.Distance;

                var confidence = Math.Min(s.Class.Confidence, best.Feature.Class.Confidence);
                if (best.Distance > tol * 0.5) { confidence = Math.Min(confidence, 0.8); }
                if (best.Distance > tol * profile.Tolerances.ReviewOffsetFraction)
                {
                    confidence = Math.Min(confidence, 0.6);
                    r.Note(string.Format(CultureInfo.InvariantCulture, "{0:0.00} ft apart, close to the {1:0.0} ft tolerance", best.Distance / ctx.UnitsPerFoot, tol / ctx.UnitsPerFoot));
                }
                if (s.Class.Conflict) { confidence = Math.Min(confidence, 0.5); r.Note("our classification is uncertain: " + s.Class.Evidence); }
                if (best.Feature.Class.Conflict) { confidence = Math.Min(confidence, 0.5); r.Note("their classification is uncertain: " + best.Feature.Class.Evidence); }
                if (same.Count > 1)
                    r.Note(string.Format(CultureInfo.InvariantCulture, "{0} consultant {1} within tolerance (nearest used)", same.Count, category != null ? category.Name : s.Class.Category));

                // Elevations compare only when both are real: a block inserted at Z=0
                // carries no elevation.
                var dz = ElevationDifference(s, best.Feature);
                if (dz.HasValue)
                {
                    r.VerticalDifference = dz.Value;
                    if (Math.Abs(dz.Value) > elevTol)
                    {
                        r.Status = MatchStatus.Discrepancy;
                        r.Note(string.Format(CultureInfo.InvariantCulture, "elevation differs by {0:0.00}: ours {1:0.00}, theirs {2:0.00}",
                            dz.Value, s.Elevation.Value, best.Feature.Elevation.Value));
                    }
                }

                // Facts carried by our block's attributes (a rim in an attribute) that the
                // consultant does not show anywhere at this feature are information that
                // would vanish with the block: a person decides.
                var missingFacts = MissingValueFacts(s, best.Feature, ctx);
                if (missingFacts.Count > 0 && r.Status == MatchStatus.Matched)
                {
                    r.Status = MatchStatus.NeedsReview;
                    r.Note("our attributes carry " + string.Join(", ", missingFacts.ToArray()) + " that the consultant does not show");
                }

                r.Confidence = confidence;
                if (r.Status == MatchStatus.Matched && confidence < profile.Tolerances.AutoAcceptConfidence)
                    r.Note("below the automatic acceptance confidence");
                return r;
            }

            if (sameSystem.Count > 0)
            {
                var near = sameSystem[0];
                r.Status = MatchStatus.NeedsReview;
                r.Confidence = 0.4;
                r.MatchedIds.Add(near.Feature.Id);
                r.MatchedHandles.Add(HandleOf(near.Feature));
                r.HorizontalOffset = near.Distance;
                r.Note(string.Format(CultureInfo.InvariantCulture, "consultant shows a {0} {1:0.00} ft away: same system, different type",
                    NameOf(ctx, near.Feature), near.Distance / ctx.UnitsPerFoot));
                return r;
            }

            if (unknown.Count > 0)
            {
                var near = unknown[0];
                r.Status = MatchStatus.NeedsReview;
                r.Confidence = 0.3;
                r.MatchedIds.Add(near.Feature.Id);
                r.MatchedHandles.Add(HandleOf(near.Feature));
                r.HorizontalOffset = near.Distance;
                r.Note(string.Format(CultureInfo.InvariantCulture, "unmapped consultant object {0:0.00} ft away ({1}); map its layer or block in the profile",
                    near.Distance / ctx.UnitsPerFoot, near.Feature.Describe()));
                return r;
            }

            if (ctx.ConsultantIncomplete)
            {
                r.Status = MatchStatus.NotEvaluated;
                r.Confidence = 0;
                r.Note("consultant data is incomplete (an external reference did not load), so absence proves nothing");
                return r;
            }

            r.Status = MatchStatus.Missing;
            r.Confidence = Math.Max(0.6, s.Class.Confidence);
            if (other.Count > 0)
                r.Note(string.Format(CultureInfo.InvariantCulture, "a {0} is {1:0.00} ft away but belongs to a different system; not treated as the same object",
                    NameOf(ctx, other[0].Feature), other[0].Distance / ctx.UnitsPerFoot));
            return r;
        }

        internal static string HandleOf(CompareFeature f)
        {
            return string.IsNullOrEmpty(f.OwnerPath) ? f.Handle : f.OwnerPath + "|" + f.Handle;
        }

        internal static string NameOf(MatchContext ctx, CompareFeature f)
        {
            var c = ctx.Profile.Category(f.Class.Category);
            return c != null ? c.Name : f.Class.Category;
        }

        /// <summary>Signed difference (ours minus theirs) when both carry a real elevation.</summary>
        internal static double? ElevationDifference(CompareFeature s, CompareFeature c)
        {
            if (!s.Elevation.HasValue || !c.Elevation.HasValue) return null;
            if (Math.Abs(s.Elevation.Value) < 0.01 || Math.Abs(c.Elevation.Value) < 0.01) return null;
            if (double.IsNaN(s.Elevation.Value) || double.IsNaN(c.Elevation.Value)) return null;
            return s.Elevation.Value - c.Elevation.Value;
        }

        private static List<string> MissingValueFacts(CompareFeature s, CompareFeature c, MatchContext ctx)
        {
            var missing = new List<string>();
            if (s.Facts == null) return missing;
            var theirs = new List<AnnotationFact>(c.Facts ?? new List<AnnotationFact>());
            List<CompareFeature> labels;
            if (ctx.ConsultantAnnotationByFeature.TryGetValue(c.Id, out labels))
                foreach (var l in labels) if (l.Facts != null) theirs.AddRange(l.Facts);
            foreach (var fact in s.Facts.Where(AnnotationParser.IsValueFact))
            {
                if (fact.Key == "ID") continue;   // identifiers differ between offices by design
                if (!theirs.Any(t => t.Key == fact.Key)) missing.Add(fact.ToString());
            }
            return missing;
        }
    }
}
