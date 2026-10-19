using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FieldCodes.Compare
{
    /// <summary>
    /// Annotation is compared through the feature it belongs to and the facts it carries.
    /// Our rim label at a matched manhole is a duplicate only when the consultant shows the
    /// same rim at the same manhole; an invert they do not show keeps plotting; a different
    /// value is a discrepancy for a person. Annotation on a missing feature is missing.
    /// </summary>
    public static class AnnotationMatcher
    {
        /// <summary>Ties each annotation feature to its owner: the reader's association when
        /// it gave one (an attribute's block, a label's anchor), else the nearest point feature
        /// within the search radius, else the nearest line within it.</summary>
        public static void Associate(CompareDataset dataset, SpatialGrid points, SpatialGrid lines, MatchContext ctx, CompareProfile profile)
        {
            var radius = ctx.Feet(profile.Tolerances.AnnotationSearchFeet);
            var byId = dataset.Source == CompareSource.Survey ? ctx.SurveyById : ctx.ConsultantById;
            foreach (var a in dataset.Features)
            {
                if (a.Kind != FeatureKind.Annotation) continue;
                if (!string.IsNullOrEmpty(a.AssociatedFeatureId) && byId.ContainsKey(a.AssociatedFeatureId)) continue;
                a.AssociatedFeatureId = null;

                var box = Box2.Of(a.Position).Expand(radius);
                CompareFeature best = null;
                var bestDistance = double.MaxValue;
                foreach (var p in points.QueryFeatures(box))
                {
                    if (profile.IsIgnored(p)) continue;
                    var d = p.Position.DistanceTo(a.Position);
                    if (d <= radius && d < bestDistance) { best = p; bestDistance = d; }
                }
                if (best == null)
                {
                    foreach (var l in lines.QueryFeatures(box))
                    {
                        if (profile.IsIgnored(l) || l.Vertices == null || l.Vertices.Count < 2) continue;
                        double st; int seg;
                        var d = Geometry2.ClosestOnPolyline(a.Position, l.Vertices, l.Stations, out st, out seg);
                        if (d <= radius && d < bestDistance) { best = l; bestDistance = d; }
                    }
                }
                if (best != null) a.AssociatedFeatureId = best.Id;
            }
        }

        public static FeatureResult Match(CompareFeature a, MatchContext ctx)
        {
            var r = FeatureResult.From(a);
            var profile = ctx.Profile;
            var rules = profile.Annotation;
            var radius = ctx.Feet(profile.Tolerances.AnnotationSearchFeet);
            var elevTol = ctx.Feet(profile.Tolerances.ElevationFeet);

            if (ctx.IsOutsideLimits(a.Position))
            {
                r.Status = MatchStatus.Excluded;
                r.Confidence = 1.0;
                r.Note("outside the comparison limits");
                return r;
            }

            CompareFeature owner = null;
            FeatureResult ownerResult = null;
            if (!string.IsNullOrEmpty(a.AssociatedFeatureId))
            {
                ctx.SurveyById.TryGetValue(a.AssociatedFeatureId, out owner);
                ctx.ResultByFeatureId.TryGetValue(a.AssociatedFeatureId, out ownerResult);
            }
            r.AssociatedFeatureId = owner != null ? owner.Id : null;

            // The consultant annotation that could be the equivalent of ours: anything at
            // the matched feature, plus anything within the search radius of our text.
            var theirs = new List<CompareFeature>();
            if (ownerResult != null)
                foreach (var id in ownerResult.MatchedIds)
                {
                    List<CompareFeature> list;
                    if (ctx.ConsultantAnnotationByFeature.TryGetValue(id, out list)) theirs.AddRange(list);
                }
            foreach (var c in ctx.ConsultantAnnotation.QueryFeatures(Box2.Of(a.Position).Expand(radius)))
                if (c.Position.DistanceTo(a.Position) <= radius && !theirs.Contains(c)) theirs.Add(c);

            if (owner != null && ownerResult != null)
            {
                r.Category = ownerResult.Category;
                r.System = ownerResult.System;
                switch (ownerResult.Status)
                {
                    case MatchStatus.Missing:
                        r.Status = MatchStatus.Missing;
                        r.Confidence = ownerResult.Confidence;
                        r.Note("its feature is missing from the consultant drawing");
                        return r;
                    case MatchStatus.NotEvaluated:
                        r.Status = MatchStatus.NotEvaluated;
                        r.Note("its feature could not be evaluated");
                        return r;
                    case MatchStatus.Excluded:
                        r.Status = MatchStatus.Excluded;
                        r.Confidence = 1.0;
                        r.Note("its feature is excluded");
                        return r;
                    case MatchStatus.NeedsReview:
                    case MatchStatus.Discrepancy:
                        r.Status = MatchStatus.NeedsReview;
                        r.Confidence = Math.Min(0.5, ownerResult.Confidence);
                        r.Note("its feature needs review (" + ownerResult.Status + ")");
                        return r;
                }

                // Matched or partial. On a partial line, text over an uncovered stretch is ours to plot.
                if (ownerResult.Status == MatchStatus.Partial && owner.Kind == FeatureKind.Line)
                {
                    double st; int seg;
                    Geometry2.ClosestOnPolyline(a.Position, owner.Vertices, owner.Stations, out st, out seg);
                    var interval = ownerResult.Intervals.FirstOrDefault(i => st >= i.Start - 1e-9 && st <= i.End + 1e-9);
                    if (interval != null && interval.Kind != IntervalKind.Covered)
                    {
                        r.Status = MatchStatus.Missing;
                        r.Confidence = ownerResult.Confidence;
                        r.Note("labels a stretch the consultant does not show");
                        return r;
                    }
                }

                // Facts the matched consultant feature itself carries (attributes) count too.
                var theirFacts = new List<AnnotationFact>();
                foreach (var id in ownerResult.MatchedIds)
                {
                    CompareFeature cf;
                    if (ctx.ConsultantById.TryGetValue(id, out cf) && cf.Facts != null) theirFacts.AddRange(cf.Facts);
                }
                foreach (var t in theirs) if (t.Facts != null) theirFacts.AddRange(t.Facts);

                return CompareFacts(r, a, theirs, theirFacts, ownerResult.Confidence, elevTol, rules);
            }

            // No feature of ours to hang it on: compare the wording to what they show nearby.
            var theirFactsLoose = theirs.Where(t => t.Facts != null).SelectMany(t => t.Facts).ToList();
            return CompareFacts(r, a, theirs, theirFactsLoose, 0.7, elevTol, rules);
        }

        private static FeatureResult CompareFacts(FeatureResult r, CompareFeature a, List<CompareFeature> theirs, List<AnnotationFact> theirFacts,
                                                  double baseConfidence, double elevTol, AnnotationRules rules)
        {
            var ours = (a.Facts ?? new List<AnnotationFact>()).ToList();
            var valueFacts = ours.Where(AnnotationParser.IsValueFact).ToList();

            if (valueFacts.Count == 0)
            {
                if (rules.CompareTextWhenNoFacts && theirs.Any(t => AnnotationParser.SameText(t.Description, a.Description, rules)))
                {
                    r.Status = MatchStatus.Matched;
                    r.Confidence = Math.Max(0.75, baseConfidence);
                    var twin = theirs.First(t => AnnotationParser.SameText(t.Description, a.Description, rules));
                    r.MatchedIds.Add(twin.Id);
                    r.MatchedHandles.Add(PointMatcher.HandleOf(twin));
                    r.HorizontalOffset = twin.Position.DistanceTo(a.Position);
                    r.Note("same wording in the consultant drawing");
                    return r;
                }
                if (theirs.Count > 0)
                {
                    r.Status = MatchStatus.NeedsReview;
                    r.Confidence = 0.4;
                    r.Note("consultant text nearby reads differently: " + string.Join(" | ", theirs.Take(3).Select(t => Shorten(t.Description)).ToArray()));
                    return r;
                }
                r.Status = MatchStatus.Missing;
                r.Confidence = Math.Max(0.6, baseConfidence);
                r.Note("no consultant annotation at this feature");
                return r;
            }

            var matched = new List<string>();
            var missing = new List<string>();
            var conflicts = new List<string>();
            foreach (var fact in valueFacts)
            {
                if (fact.Key == "ID") continue;   // identifiers are office-specific
                var sameKey = theirFacts.Where(t => t.Key == fact.Key).ToList();
                if (sameKey.Count == 0)
                {
                    // An invert with a direction we gave but they did not: compare by value.
                    if (fact.Key.StartsWith("IE-", StringComparison.Ordinal))
                        sameKey = theirFacts.Where(t => t.Key == "IE" || t.Key.StartsWith("IE-", StringComparison.Ordinal)).ToList();
                    if (sameKey.Count == 0) { missing.Add(fact.ToString()); continue; }
                }
                if (sameKey.Any(t => Equal(fact, t, elevTol))) matched.Add(fact.ToString());
                else conflicts.Add(fact.ToString() + " vs " + string.Join("/", sameKey.Select(t => t.ToString()).Distinct().ToArray()));
            }

            if (valueFacts.All(f => f.Key == "ID"))
            {
                // Only an identifier: present when any consultant annotation sits at the feature.
                if (theirs.Count > 0) { r.Status = MatchStatus.Matched; r.Confidence = Math.Max(0.75, baseConfidence); r.Note("structure identified in both drawings"); }
                else { r.Status = MatchStatus.Missing; r.Confidence = baseConfidence; r.Note("no consultant annotation at this feature"); }
                return r;
            }

            foreach (var t in theirs)
            {
                if (r.MatchedIds.Contains(t.Id)) continue;
                r.MatchedIds.Add(t.Id);
                r.MatchedHandles.Add(PointMatcher.HandleOf(t));
            }

            if (conflicts.Count > 0)
            {
                r.Status = MatchStatus.Discrepancy;
                r.Confidence = baseConfidence;
                r.Note("value conflict: " + string.Join(", ", conflicts.ToArray()));
                var dz = conflicts.Count == 1 ? NumericGap(valueFacts, theirFacts) : null;
                if (dz.HasValue) r.VerticalDifference = dz;
                return r;
            }
            if (missing.Count > 0)
            {
                r.Status = MatchStatus.Missing;
                r.Confidence = Math.Max(0.6, baseConfidence);
                r.Note("consultant does not show " + string.Join(", ", missing.ToArray()) + (matched.Count > 0 ? " (they do show " + string.Join(", ", matched.ToArray()) + ")" : string.Empty));
                return r;
            }
            r.Status = MatchStatus.Matched;
            r.Confidence = baseConfidence;
            r.Note("consultant shows the same " + string.Join(", ", matched.Select(m => m.Split('=')[0]).Distinct().ToArray()));
            return r;
        }

        private static bool Equal(AnnotationFact ours, AnnotationFact theirs, double elevTol)
        {
            if (ours.Number.HasValue && theirs.Number.HasValue)
            {
                var tol = ours.Key == "SIZE" ? 0.01 : elevTol;
                return Math.Abs(ours.Number.Value - theirs.Number.Value) <= tol + 1e-9;
            }
            return string.Equals((ours.Text ?? string.Empty).Trim(), (theirs.Text ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static double? NumericGap(List<AnnotationFact> ours, List<AnnotationFact> theirs)
        {
            foreach (var o in ours)
            {
                if (!o.Number.HasValue) continue;
                var t = theirs.FirstOrDefault(x => x.Key == o.Key && x.Number.HasValue);
                if (t != null) return o.Number.Value - t.Number.Value;
            }
            return null;
        }

        private static string Shorten(string text)
        {
            var t = AnnotationParser.Clean(text);
            return t.Length > 30 ? t.Substring(0, 30) + "..." : t;
        }
    }
}
