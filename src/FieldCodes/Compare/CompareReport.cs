using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace FieldCodes.Compare
{
    /// <summary>
    /// The QC report: one CSV row per survey feature, and the plain-text summary a
    /// person reads on the command line or in the review window. Pure formatting.
    /// </summary>
    public static class CompareReport
    {
        public const string Suffix = ".ftf-compare.csv";
        public const string Header =
            "Site,FeatureType,Category,System,EntityType,SourceLayer,SurveyHandle,ConsultantHandle,ConsultantSource," +
            "Status,EffectiveStatus,Confidence,HorizontalOffsetFt,VerticalDifferenceFt,LengthFt,CoveredFt,UncoveredFt,OutsideLimitsFt," +
            "OriginalPlottingLayer,FinalPlottingLayer,Action,ReviewDecision,Description,Notes";

        public static string PathFor(string drawingPath)
        {
            if (string.IsNullOrWhiteSpace(drawingPath)) return null;
            try
            {
                var dir = Path.GetDirectoryName(drawingPath);
                var name = Path.GetFileNameWithoutExtension(drawingPath);
                if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(name)) return null;
                return Path.Combine(dir, name + Suffix);
            }
            catch (ArgumentException) { return null; }
        }

        public static string Render(ComparisonResult result, string siteName)
        {
            var sb = new StringBuilder();
            sb.AppendLine(Header);
            if (result == null) return sb.ToString();
            var upf = result.UnitsPerFoot <= 0 ? 1.0 : result.UnitsPerFoot;

            foreach (var r in result.Results.OrderBy(x => Order(x.EffectiveStatus)).ThenBy(x => x.Category).ThenBy(x => x.Handle, StringComparer.OrdinalIgnoreCase))
            {
                var finalLayer = FinalLayer(r);
                var fields = new[]
                {
                    siteName ?? result.SurveyName ?? string.Empty,
                    r.Kind.ToString(),
                    r.Category ?? string.Empty,
                    r.System ?? string.Empty,
                    r.EntityType ?? string.Empty,
                    r.SourceLayer ?? string.Empty,
                    r.Handle ?? string.Empty,
                    string.Join(" ", r.MatchedHandles.Select(h => h.Contains("|") ? h.Substring(h.LastIndexOf('|') + 1) : h).ToArray()),
                    string.Join(" ", r.MatchedHandles.Where(h => h.Contains("|")).Select(h => h.Substring(0, h.LastIndexOf('|'))).Distinct().ToArray()),
                    r.Status.ToString(),
                    r.EffectiveStatus.ToString(),
                    r.Confidence.ToString("0.00", CultureInfo.InvariantCulture),
                    Ft(r.HorizontalOffset, upf),
                    Ft(r.VerticalDifference, upf),
                    r.Kind == FeatureKind.Line ? Ft(r.Length, upf) : string.Empty,
                    r.Kind == FeatureKind.Line ? Ft(r.CoveredLength, upf) : string.Empty,
                    r.Kind == FeatureKind.Line ? Ft(r.UncoveredLength, upf) : string.Empty,
                    r.Kind == FeatureKind.Line ? Ft(r.OutsideLength, upf) : string.Empty,
                    r.SourceLayer ?? string.Empty,
                    finalLayer,
                    r.Action.ToString(),
                    r.Decision.ToString() + (string.IsNullOrEmpty(r.DecisionNote) ? string.Empty : " - " + r.DecisionNote),
                    r.Description ?? string.Empty,
                    r.NotesText
                };
                sb.AppendLine(string.Join(",", fields.Select(Csv).ToArray()));
            }
            return sb.ToString();
        }

        /// <summary>Where the object plots after the plan: its nonplot layer, or its own layer
        /// (a split keeps the uncovered portions on the original layer).</summary>
        public static string FinalLayer(FeatureResult r)
        {
            switch (r.Action)
            {
                case PlannedAction.Nonplot: return r.NonplotLayer ?? string.Empty;
                case PlannedAction.SplitNonplot: return (r.NonplotLayer ?? string.Empty) + " (uncovered portions stay on " + r.SourceLayer + ")";
                default: return r.SourceLayer ?? string.Empty;
            }
        }

        private static int Order(MatchStatus s)
        {
            switch (s)
            {
                case MatchStatus.Discrepancy: return 0;
                case MatchStatus.NeedsReview: return 1;
                case MatchStatus.NotEvaluated: return 2;
                case MatchStatus.Partial: return 3;
                case MatchStatus.Missing: return 4;
                case MatchStatus.Matched: return 5;
                default: return 6;
            }
        }

        private static string Ft(double? v, double upf)
        {
            return v.HasValue ? (v.Value / upf).ToString("0.00", CultureInfo.InvariantCulture) : string.Empty;
        }

        private static string Ft(double v, double upf)
        {
            return (v / upf).ToString("0.0", CultureInfo.InvariantCulture);
        }

        /// <summary>The summary: counts, what limited the run, unknown layers, the plan.</summary>
        public static string SummaryText(ComparisonResult result, ChangePlan plan)
        {
            if (result == null) return "No comparison has been run.";
            var lines = new List<string>();
            var summary = result.Summary();
            lines.Add(string.Format(CultureInfo.InvariantCulture, "FTFCOMPARE {0} vs {1} (profile {2}, {3:0.0} s)",
                result.SurveyName, result.ConsultantName, result.ProfileName, result.ElapsedSeconds));
            lines.Add("  " + summary);
            lines.Add(string.Format(CultureInfo.InvariantCulture, "  consultant features read: {0}; review decisions in force: {1}", result.ConsultantFeatures, summary.Decided));

            if (result.ConsultantIncomplete)
                lines.Add("  CONSULTANT DATA INCOMPLETE: a reference did not load; nothing was declared missing.");

            foreach (var issue in result.Issues.Where(i => i.Severity != Severity.Info))
                lines.Add("  " + issue.Severity.ToString().ToUpperInvariant() + " " + issue.Code + ": " + issue.Message);

            if (result.UnknownSurveyLayers.Count > 0)
            {
                lines.Add("  Survey layers the profile does not map (not evaluated, still plotting):");
                foreach (var kv in result.UnknownSurveyLayers.OrderByDescending(k => k.Value).Take(20))
                    lines.Add(string.Format(CultureInfo.InvariantCulture, "    {0,5}  {1}", kv.Value, kv.Key));
                if (result.UnknownSurveyLayers.Count > 20) lines.Add("    ... more in the CSV");
            }
            if (result.UnknownConsultantLayers.Count > 0)
            {
                lines.Add("  Consultant layers the profile does not map (not compared):");
                foreach (var kv in result.UnknownConsultantLayers.OrderByDescending(k => k.Value).Take(20))
                    lines.Add(string.Format(CultureInfo.InvariantCulture, "    {0,5}  {1}", kv.Value, kv.Key));
                if (result.UnknownConsultantLayers.Count > 20) lines.Add("    ... more");
            }

            if (plan != null)
                lines.Add(string.Format(CultureInfo.InvariantCulture,
                    "  Plan: {0} object(s) to nonplot, {1} line(s) split (covered portion nonplot), {2} keep plotting, {3} awaiting review",
                    plan.NonplotCount, plan.SplitCount, plan.KeptPlotting, plan.AwaitingReview));

            var byCategory = result.Results.Where(r => r.Kind != FeatureKind.Annotation)
                .GroupBy(r => string.IsNullOrEmpty(r.Category) ? "(unmapped)" : r.Category)
                .OrderByDescending(g => g.Count());
            lines.Add("  By category: matched / missing / partial / review+discrepancy / not evaluated / excluded");
            foreach (var g in byCategory.Take(40))
                lines.Add(string.Format(CultureInfo.InvariantCulture, "    {0,-20} {1,5} {2,5} {3,5} {4,5} {5,5} {6,5}", g.Key,
                    g.Count(r => r.EffectiveStatus == MatchStatus.Matched),
                    g.Count(r => r.EffectiveStatus == MatchStatus.Missing),
                    g.Count(r => r.EffectiveStatus == MatchStatus.Partial),
                    g.Count(r => r.EffectiveStatus == MatchStatus.NeedsReview || r.EffectiveStatus == MatchStatus.Discrepancy),
                    g.Count(r => r.EffectiveStatus == MatchStatus.NotEvaluated),
                    g.Count(r => r.EffectiveStatus == MatchStatus.Excluded)));
            return string.Join(Environment.NewLine, lines.ToArray());
        }

        /// <summary>CSV escaping, with formula-injection guarding like the other FTF reports.</summary>
        public static string Csv(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            var s = value;
            if (s[0] == '=' || s[0] == '+' || s[0] == '-' || s[0] == '@') s = "'" + s;
            return s.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        }
    }
}
