using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FieldCodes.Linework;
using FieldCodes.Orient;

namespace FieldCodes.Review
{
    /// <summary>One survey point as the review checks see it.</summary>
    public sealed class ReviewPoint
    {
        public string Number { get; set; }
        public string Description { get; set; }
        public string Handle { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        /// <summary>The point as the parser read it (null when not parsed).</summary>
        public ParsedPoint Parsed { get; set; }
    }

    /// <summary>
    /// Turns what the existing checks find into review items. Codes FTF ignores on purpose,
    /// ordinary Civil 3D linework and bare codes are informational and never become items.
    /// </summary>
    public static class ReviewChecks
    {
        public const string CodesSource = "Codes";
        public const string OrientationSource = "Orientation";
        public const string LineLabelsSource = "Line labels";
        public const string PointLabelsSource = "Point labels";

        /// <summary>A point label FTFLABELS could not place clear of the symbols and labels around
        /// it: it was left out rather than laid over them, and a drafter places it.</summary>
        public static ReviewItem LabelNotPlaced(string pointNumber, string handle, double x, double y, string text)
        {
            var item = ReviewKinds.New(ReviewKinds.LabelFailed, pointNumber,
                "no position within the leader length clears the symbols and labels around point " + pointNumber +
                "; the label (" + text + ") was left out rather than laid over them");
            item.Source = PointLabelsSource;
            item.FeatureType = "Point label";
            item.Points = pointNumber;
            if (!string.IsNullOrEmpty(handle)) item.Handles.Add(handle);
            item.X = x;
            item.Y = y;
            item.Issue = "Label has no clear position";
            item.FixCommand = "FTFLABELS";
            item.SettingsPage = "Leader Styles";
            return item;
        }
        public const string FenceHeightsSource = "Fence heights";

        /// <summary>
        /// Unknown codes (one item per code, listing its points), blank or unreadable
        /// descriptions and invalid values (one per point), from the Unknown Codes classifier.
        /// </summary>
        public static IList<ReviewItem> Codes(IEnumerable<ReviewPoint> points, CodeDiagnostics diagnostics)
        {
            var items = new List<ReviewItem>();
            if (diagnostics == null) return items;
            var unknown = new Dictionary<string, List<ReviewPoint>>(StringComparer.OrdinalIgnoreCase);
            var reasons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in points ?? Enumerable.Empty<ReviewPoint>())
            {
                if (p == null || p.Parsed == null) continue;
                var issue = diagnostics.Classify(p.Parsed);
                if (issue.Category == CodeIssueCategory.NeedsConfiguration)
                {
                    List<ReviewPoint> list;
                    if (!unknown.TryGetValue(issue.Code, out list)) { list = new List<ReviewPoint>(); unknown[issue.Code] = list; }
                    list.Add(p);
                    if (!reasons.ContainsKey(issue.Code)) reasons[issue.Code] = issue.Reason;
                    continue;
                }
                if (issue.Category != CodeIssueCategory.Error) continue;
                var kind = issue.Kind == CodeIssueKind.InvalidData ? ReviewKinds.CodeInvalid : ReviewKinds.CodeUnreadable;
                var item = ReviewKinds.New(kind, p.Number, issue.Reason);
                item.Issue = issue.Label;
                item.SuggestedAction = issue.Action;
                At(item, p);
                items.Add(item);
            }

            foreach (var pair in unknown.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            {
                var list = pair.Value;
                var item = ReviewKinds.New(ReviewKinds.CodeUnknown, pair.Key,
                    list.Count.ToString(CultureInfo.InvariantCulture) + " point(s) coded " + pair.Key + ". " + reasons[pair.Key]);
                item.Issue = "Code nothing handles: " + pair.Key;
                item.Points = Points(list.Select(p => p.Number));
                item.Handles = list.Where(p => !string.IsNullOrEmpty(p.Handle)).Select(p => p.Handle).Take(50).ToList();
                item.X = list[0].X;
                item.Y = list[0].Y;
                items.Add(item);
            }
            return items;
        }

        /// <summary>
        /// Words after a linework code that are not a linework command, a number, a side or another
        /// known code: "FNC CL" or "EP XYZ B". Read with the same parser as every linework check
        /// (<see cref="LineworkCoding"/>), configured from the office's command set. One item per
        /// code and word, listing its points; the drafter decides whether it is the crew's note or a
        /// miscoded line. Words before the first figure ("FGP" in "FGP FNC") are the point's own code.
        /// </summary>
        public static IList<ReviewItem> LineworkModifiers(IEnumerable<ReviewPoint> points, ICollection<string> lineworkCodes,
                                                          ICollection<string> otherKnownCodes,
                                                          LineworkCommandSettings commands = null)
        {
            var items = new List<ReviewItem>();
            if (lineworkCodes == null || lineworkCodes.Count == 0) return items;
            var known = new HashSet<string>((otherKnownCodes ?? new List<string>()).Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()),
                                            StringComparer.OrdinalIgnoreCase);
            var found = new Dictionary<string, List<ReviewPoint>>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in points ?? Enumerable.Empty<ReviewPoint>())
            {
                if (p == null || string.IsNullOrWhiteSpace(p.Description)) continue;
                var shot = LineworkCoding.Parse(p.Description, lineworkCodes, commands);
                foreach (var figure in shot.Figures)
                    foreach (var word in figure.Words)
                    {
                        var w = word.ToUpperInvariant();
                        if (known.Contains(LineHeights.CodeOf(w))) continue;
                        var key = figure.Code + " " + w;
                        List<ReviewPoint> list;
                        if (!found.TryGetValue(key, out list)) { list = new List<ReviewPoint>(); found[key] = list; }
                        if (!list.Contains(p)) list.Add(p);
                    }
            }

            foreach (var pair in found.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            {
                var list = pair.Value;
                var parts = pair.Key.Split(' ');
                var item = ReviewKinds.New(ReviewKinds.CodeModifier, pair.Key,
                    "\"" + parts[1] + "\" after " + parts[0] + " is not a linework command (B, E, C...), a number or a code FTF knows (" +
                    list.Count.ToString(CultureInfo.InvariantCulture) + " point(s), e.g. \"" + list[0].Description.Trim() + "\")");
                item.Issue = "Linework word not recognised: " + pair.Key;
                item.Points = Points(list.Select(p => p.Number));
                item.Handles = list.Where(p => !string.IsNullOrEmpty(p.Handle)).Select(p => p.Handle).Take(50).ToList();
                item.X = list[0].X;
                item.Y = list[0].Y;
                items.Add(item);
            }
            return items;
        }

        /// <summary>Symbols FTFORIENT could not turn with confidence: marking directions it could not
        /// decide, rotations needing review, and points it could not change.</summary>
        public static IList<ReviewItem> Orientation(OrientationResult result, IDictionary<OrientationDecision, OrientOutcome> outcomes,
                                                    IDictionary<string, string> handles)
        {
            var items = new List<ReviewItem>();
            if (result == null || outcomes == null) return items;
            foreach (var d in result.Decisions)
            {
                OrientOutcome outcome;
                if (d == null || d.Feature == null || !outcomes.TryGetValue(d, out outcome)) continue;
                string reason;
                string kind;
                var marking = d.Feature.Kind == OrientKind.PavementArrow || d.Feature.Kind == OrientKind.PavementWord;
                switch (outcome)
                {
                    case OrientOutcome.Review:
                        kind = marking ? ReviewKinds.MarkingDirection : ReviewKinds.OrientReview;
                        reason = string.IsNullOrWhiteSpace(d.ReviewReason) ? "FTFORIENT could not decide its direction" : d.ReviewReason;
                        break;
                    case OrientOutcome.BelowThreshold:
                        kind = marking ? ReviewKinds.MarkingDirection : ReviewKinds.OrientReview;
                        reason = "not turned: " + d.Tier + " evidence at confidence " +
                                 d.Confidence.ToString("0.00", CultureInfo.InvariantCulture) + " is below the minimum to apply";
                        break;
                    case OrientOutcome.Failed:
                        kind = ReviewKinds.LabelFailed;
                        reason = "FTFORIENT could not turn the marker (the point may be locked by its point group or style)";
                        break;
                    default:
                        continue;
                }
                var item = ReviewKinds.New(kind, d.Feature.Id, reason);
                item.Source = OrientationSource;
                item.FeatureType = marking ? "Pavement marking" : "Symbol";
                item.Points = d.Feature.Id;
                string handle;
                if (handles != null && handles.TryGetValue(d.Feature.Id, out handle) && !string.IsNullOrEmpty(handle)) item.Handles.Add(handle);
                item.X = d.Feature.Location.X;
                item.Y = d.Feature.Location.Y;
                if (kind == ReviewKinds.LabelFailed)
                {
                    item.Issue = "Symbol could not be turned";
                    item.FixCommand = "FTFORIENT";
                    item.SettingsPage = "Symbols and Rotations";
                }
                else
                {
                    // Fix opens this point's decision: why, the reference used, and Preview,
                    // Apply, Override and Recheck.
                    item.FixCommand = "FTFORIENTPOINT " + d.Feature.Id;
                    if (!string.IsNullOrEmpty(d.Reference)) item.Reason = item.Reason + " (reference: " + d.Reference + ")";
                }
                items.Add(item);
            }

            // Probable underground connections: for review only, never utility linework.
            foreach (var c in result.Connections ?? new List<InferredConnection>())
            {
                if (c == null || c.FeatureIds == null || c.FeatureIds.Count == 0) continue;
                var subject = string.Join(" ", c.FeatureIds.ToArray());
                var item = ReviewKinds.New(ReviewKinds.InferredConnection, subject + " " + (c.Kind ?? "connection"),
                    string.Format(CultureInfo.InvariantCulture, "{0} inferred at confidence {1:0.00} from the positions of points {2}; not verified",
                                  c.Kind ?? "connection", c.Confidence, subject));
                item.Source = OrientationSource;
                item.FeatureType = "Utility (inferred)";
                item.Points = subject;
                foreach (var id in c.FeatureIds)
                {
                    string handle;
                    if (handles != null && handles.TryGetValue(id, out handle) && !string.IsNullOrEmpty(handle)) item.Handles.Add(handle);
                }
                item.X = (c.From.X + c.To.X) / 2;
                item.Y = (c.From.Y + c.To.Y) / 2;
                item.FixCommand = "FTFORIENTPOINT " + c.FeatureIds[0];
                items.Add(item);
            }
            return items;
        }

        /// <summary>Fence heights that could not be read ("FNC B 35").</summary>
        public static ReviewItem FenceHeight(string pointNumber, string handle, double x, double y, string problem)
        {
            var subject = string.IsNullOrWhiteSpace(pointNumber)
                ? x.ToString("0.00", CultureInfo.InvariantCulture) + "," + y.ToString("0.00", CultureInfo.InvariantCulture)
                : pointNumber;
            var item = ReviewKinds.New(ReviewKinds.FenceHeight, subject, problem);
            item.Points = pointNumber;
            if (!string.IsNullOrEmpty(handle)) item.Handles.Add(handle);
            item.X = x;
            item.Y = y;
            return item;
        }

        private static void At(ReviewItem item, ReviewPoint p)
        {
            item.Points = p.Number;
            if (!string.IsNullOrEmpty(p.Handle)) item.Handles.Add(p.Handle);
            item.X = p.X;
            item.Y = p.Y;
        }

        private static string Points(IEnumerable<string> numbers)
        {
            var list = numbers.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().ToList();
            var shown = string.Join(", ", list.Take(12).ToArray());
            return list.Count > 12 ? shown + " (+" + (list.Count - 12).ToString(CultureInfo.InvariantCulture) + " more)" : shown;
        }
    }
}
