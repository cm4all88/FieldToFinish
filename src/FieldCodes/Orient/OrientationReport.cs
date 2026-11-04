using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using FieldCodes.Geometry;

namespace FieldCodes.Orient
{
    /// <summary>What FTFORIENT did with one decision.</summary>
    public enum OrientOutcome
    {
        Rotated,
        AlreadyRight,
        /// <summary>Preview: nothing was changed.</summary>
        Preview,
        /// <summary>The marker had been turned by hand since FTF last set it: kept.</summary>
        KeptByHand,
        BelowThreshold,
        Review,
        NotDirectional,
        /// <summary>No symbol standard for the code: nothing to rotate against.</summary>
        NoSymbol,
        Failed,
        /// <summary>Not required to be turned, and nothing recorded its direction: left as surveyed.</summary>
        LeftAsSurveyed,
        /// <summary>A drafter set this marker's rotation (FTFORIENTPOINT Override): never changed by FTFORIENT.</summary>
        ManualOverride
    }

    /// <summary>The CSV written beside the drawing, and the command-line summary.</summary>
    public static class OrientationReport
    {
        public const string Suffix = ".ftf-orientation.csv";

        public const string Header =
            "Point,Code,Kind,Tier,Confidence,Azimuth,Alternative,RotationBefore,RotationAfter,Outcome,Association,Evidence,Review," +
            "Reference,ReferenceDistance,Source,ProposedRotation,Applied,NeedsReview";

        public static string Csv(OrientationResult result, IDictionary<OrientationDecision, OrientOutcome> outcomes)
        {
            var sb = new StringBuilder();
            sb.AppendLine(Header);
            foreach (var d in result.Decisions.OrderBy(x => x.Tier).ThenBy(x => x.Feature.Id, StringComparer.OrdinalIgnoreCase))
            {
                OrientOutcome o;
                outcomes.TryGetValue(d, out o);
                var before = Angles.ToDegrees(d.Feature.CurrentRotation);
                var applied = o == OrientOutcome.Rotated || o == OrientOutcome.AlreadyRight;
                var after = applied
                    ? (d.MarkerRotation.HasValue ? Angles.ToDegrees(d.MarkerRotation.Value) : before)
                    : before;
                // What the marker would be turned to (also for a review item's suggestion).
                var proposedRadians = d.MarkerRotation ??
                    (d.Azimuth.HasValue && d.Symbol != null && d.Symbol.Directional ? SymbolRotation.MarkerRadians(d.Symbol, d.Azimuth.Value) : null);
                var proposed = proposedRadians.HasValue ? Angles.ToDegrees(proposedRadians.Value) : (double?)null;
                var needsReview = o == OrientOutcome.Review || o == OrientOutcome.BelowThreshold || o == OrientOutcome.Failed;
                sb.AppendLine(string.Join(",", new[]
                {
                    Q(d.Feature.Id), Q(d.Feature.Code), d.Feature.Kind.ToString(), d.Tier.ToString(),
                    d.Confidence.ToString("0.00", CultureInfo.InvariantCulture), N(d.Azimuth), N(d.AlternativeAzimuth),
                    before.ToString("0.0", CultureInfo.InvariantCulture), after.ToString("0.0", CultureInfo.InvariantCulture),
                    o.ToString(), Q(d.Association), Q(string.Join(" | ", d.Evidence.Select(e => e.Text).ToArray())), Q(d.ReviewReason),
                    Q(d.Reference), N(d.ReferenceDistance), Q(d.Source), N(proposed), applied ? "yes" : "no", needsReview ? "yes" : "no"
                }));
            }
            foreach (var c in result.Connections)
                sb.AppendLine(string.Join(",", new[]
                {
                    Q(string.Join(" ", c.FeatureIds.ToArray())), "", "InferredConnection", "Inferred",
                    c.Confidence.ToString("0.00", CultureInfo.InvariantCulture), "", "", "", "", "NotDrawnAsUtility",
                    Q(c.Kind), Q(string.Format(CultureInfo.InvariantCulture, "{0:0.00},{1:0.00} to {2:0.00},{3:0.00}", c.From.X, c.From.Y, c.To.X, c.To.Y)),
                    "inferred, not verified: never a deliverable", "", "", "inference", "", "no", "yes"
                }));
            return sb.ToString();
        }

        public static IList<string> Summary(OrientationResult result, IDictionary<OrientationDecision, OrientOutcome> outcomes)
        {
            var lines = new List<string>();
            var byTier = result.Decisions.GroupBy(d => d.Tier).OrderBy(g => g.Key)
                               .Select(g => g.Count() + " " + g.Key.ToString().ToLowerInvariant()).ToArray();
            lines.Add(result.Decisions.Count + " symbol(s): " + string.Join(", ", byTier) + ".");
            var byOutcome = outcomes.GroupBy(kv => kv.Value).OrderBy(g => g.Key)
                                    .Select(g => g.Count() + " " + Words(g.Key)).ToArray();
            if (byOutcome.Length > 0) lines.Add(string.Join(", ", byOutcome) + ".");
            if (result.Intersections.Count > 0)
                lines.Add(result.Intersections.Count + " intersection(s) found, " +
                          result.Intersections.Sum(i => i.Approaches.Count) + " approaches.");
            if (result.Connections.Count > 0)
                lines.Add(result.Connections.Count + " probable underground connection(s) inferred from valves and hydrants " +
                          "(on the review list and in the report; never drawn as utility lines).");
            var review = result.Decisions.Where(d => d.Tier == OrientationTier.Review).ToList();
            foreach (var d in review.Take(15))
                lines.Add("  REVIEW " + d.Feature.Code + " " + d.Feature.Id + ": " + d.ReviewReason);
            if (review.Count > 15) lines.Add("  ... and " + (review.Count - 15) + " more in the report.");
            return lines;
        }

        private static string Words(OrientOutcome o)
        {
            switch (o)
            {
                case OrientOutcome.Rotated: return "rotated";
                case OrientOutcome.AlreadyRight: return "already right";
                case OrientOutcome.Preview: return "previewed (not changed)";
                case OrientOutcome.KeptByHand: return "kept as turned by hand";
                case OrientOutcome.BelowThreshold: return "left (not sure enough)";
                case OrientOutcome.Review: return "for review";
                case OrientOutcome.NotDirectional: return "symmetric (not rotated)";
                case OrientOutcome.NoSymbol: return "no symbol standard";
                case OrientOutcome.LeftAsSurveyed: return "left as surveyed (no recorded direction; its standard turns it only by one)";
                case OrientOutcome.ManualOverride: return "kept as set by a drafter (manual override)";
                default: return "failed";
            }
        }

        private static string N(double? v)
        {
            return v.HasValue ? v.Value.ToString("0.0", CultureInfo.InvariantCulture) : "";
        }

        private static string Q(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.IndexOfAny(new[] { ',', '"', '\n' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        }
    }
}
