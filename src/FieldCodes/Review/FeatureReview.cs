using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FieldCodes.Review
{
    /// <summary>Broad grouping the review page filters by.</summary>
    public enum ReviewCategory
    {
        Tree = 0,
        Utility = 1,
        Sign = 2,
        Control = 3,
        OtherFeature = 4,
        /// <summary>Carries data, no rule configured.</summary>
        NotConfigured = 5,
        /// <summary>Bare code -- nothing to finish.</summary>
        NoData = 6,
        /// <summary>Linework or never-draw: not FTF's job.</summary>
        OutsideScope = 7
    }

    public enum ReviewStatus
    {
        Ok = 0,
        Warning = 1,
        Error = 2,
        NotConfigured = 3,
        NothingToDo = 4,
        OutsideScope = 5
    }

    /// <summary>One survey point's dry-run result: what FTF would do, doing nothing.</summary>
    public sealed class ReviewRow
    {
        public string PointNumber { get; set; }
        public string RawDescription { get; set; }
        public string RuleId { get; set; }
        public ReviewCategory Category { get; set; }
        public ReviewStatus Status { get; set; }

        /// <summary>Planned finishing, human-readable, in application order.</summary>
        public IList<string> Actions { get; set; }

        /// <summary>Why this point gets no finishing, when it does not.</summary>
        public string Reason { get; set; }

        public ParsedPoint Parsed { get; set; }

        public ReviewRow() { Actions = new List<string>(); }

        public string ActionsSummary
        {
            get
            {
                return Actions.Count == 0
                    ? "No finishing required"
                    : string.Join(", ", Actions.ToArray());
            }
        }

        public string StatusText
        {
            get
            {
                switch (Status)
                {
                    case ReviewStatus.Ok: return "OK";
                    case ReviewStatus.Warning: return "Warning";
                    case ReviewStatus.Error: return "ERROR";
                    case ReviewStatus.NotConfigured: return "Not configured";
                    case ReviewStatus.NothingToDo: return "Nothing to do";
                    default: return "Outside scope";
                }
            }
        }
    }

    /// <summary>
    /// Builds the dry-run view of what FTF intends to do with each point.
    ///
    /// This is a pure function of the parse result: nothing here touches a drawing,
    /// which is what makes the Feature Review page safe to open and refresh at any
    /// time. The parser already resolved every finishing decision -- this class only
    /// puts words to it.
    ///
    /// The wording is deliberate about the product boundary: FTF rotates, labels and
    /// tags what Civil 3D created; it never claims to create the survey geometry.
    /// </summary>
    public sealed class FeatureReviewBuilder
    {
        private static readonly Dictionary<string, ReviewCategory> CategoryByRule =
            new Dictionary<string, ReviewCategory>(StringComparer.OrdinalIgnoreCase)
            {
                { "tree", ReviewCategory.Tree },
                { "sign", ReviewCategory.Sign },
                { "sn-sign", ReviewCategory.Sign },
                { "control", ReviewCategory.Control },
                { "pole", ReviewCategory.Utility },
                { "light-pole", ReviewCategory.Utility },
                { "junction-box", ReviewCategory.Utility },
                { "guy-anchor", ReviewCategory.Utility },
                { "hydrant", ReviewCategory.Utility },
                { "water-valve", ReviewCategory.Utility },
                { "gas-valve", ReviewCategory.Utility },
                { "manhole", ReviewCategory.Utility },
                { "catch-basin", ReviewCategory.Utility },
                { "vault", ReviewCategory.Utility }
            };

        private readonly RulesConfig _cfg;

        public FeatureReviewBuilder(RulesConfig cfg)
        {
            if (cfg == null) throw new ArgumentNullException("cfg");
            _cfg = cfg;
        }

        public ReviewRow Build(ParsedPoint p)
        {
            if (p == null) throw new ArgumentNullException("p");

            var row = new ReviewRow
            {
                PointNumber = p.PointNumber,
                RawDescription = p.RawDescription,
                RuleId = p.RuleId,
                Parsed = p
            };

            if (p.Ignored)
            {
                row.Category = ReviewCategory.OutsideScope;
                row.Status = ReviewStatus.OutsideScope;
                row.Reason = IsLinework(p.RawDescription)
                    ? "Linework - Civil 3D builds the figures; FTF does not touch the points"
                    : "Ignored intentionally (never-draw list)";
                return row;
            }

            if (p.NoContent)
            {
                row.Category = ReviewCategory.NoData;
                row.Status = ReviewStatus.NothingToDo;
                row.Reason = "Bare code - the description carries nothing to finish; " +
                             "the Civil 3D symbol stands as-is";
                return row;
            }

            if (p.Unhandled)
            {
                row.Category = ReviewCategory.NotConfigured;
                row.Status = ReviewStatus.NotConfigured;
                row.Reason = "Carries data, but no rule is configured for this code";
                return row;
            }

            row.Category = CategoryOf(p.RuleId);

            if (p.HasErrors)
            {
                row.Status = ReviewStatus.Error;
                var first = p.Diagnostics.First(d => d.Severity == Severity.Error);
                row.Reason = first.Message;
                return row;
            }

            row.Status = p.HasWarnings ? ReviewStatus.Warning : ReviewStatus.Ok;
            row.Actions = PlannedActions(p);
            if (row.Actions.Count == 0)
                row.Reason = "Recognized; this rule defines no finishing outputs";

            return row;
        }

        public static ReviewCategory CategoryOf(string ruleId)
        {
            if (string.IsNullOrEmpty(ruleId)) return ReviewCategory.OtherFeature;
            ReviewCategory category;
            return CategoryByRule.TryGetValue(ruleId, out category)
                ? category
                : ReviewCategory.OtherFeature;
        }

        /// <summary>
        /// What FTF would do to this point, in the order the pipeline does it. Never
        /// includes creating the point or its symbol -- Civil 3D owns those.
        /// </summary>
        public static IList<string> PlannedActions(ParsedPoint p)
        {
            var actions = new List<string>();

            if (p.InsertBlock && !string.IsNullOrEmpty(p.BlockName))
                actions.Add("Insert block " + p.BlockName + " (opt-in rule)");

            if (p.RotationDegrees.HasValue)
                actions.Add("Rotate existing marker to " +
                            p.RotationDegrees.Value.ToString("0.#", CultureInfo.InvariantCulture) +
                            " deg");

            if (p.HasDripLine && p.DripRadius.HasValue)
                actions.Add("Dripline r=" +
                            p.DripRadius.Value.ToString("0.#", CultureInfo.InvariantCulture) +
                            " on " + p.DripLayer);

            if (!string.IsNullOrEmpty(p.LabelText))
            {
                actions.Add("Label \"" + p.LabelText + "\"");
                if (p.Leader == LeaderMode.Always) actions.Add("Leader");
                else if (p.Leader == LeaderMode.Auto) actions.Add("Leader if needed");
            }

            if (!string.IsNullOrEmpty(p.TagPrefix))
            {
                actions.Add("Tag " + p.TagPrefix + "#");
                actions.Add("Include in table");
            }

            return actions;
        }

        /// <summary>Full detail for the selected row -- everything the parser resolved.</summary>
        public static string DetailText(ReviewRow row)
        {
            if (row == null || row.Parsed == null) return string.Empty;
            var p = row.Parsed;
            var lines = new List<string>();

            lines.Add("Point " + p.PointNumber + ": " + p.RawDescription);

            if (!string.IsNullOrEmpty(row.Reason))
                lines.Add(row.Reason);

            if (!string.IsNullOrEmpty(p.RuleId))
            {
                lines.Add("Rule: " + p.RuleId);

                if (p.Fields.Count > 0)
                    lines.Add("Parsed fields: " + string.Join(", ",
                        p.Fields.Select(kv => kv.Key + "=" + kv.Value).ToArray()));

                if (p.Modifiers.Count > 0)
                    lines.Add("Modifiers (priority order): " + string.Join(", ",
                        p.Modifiers.Select(m => m.Id).ToArray()));

                if (!string.IsNullOrEmpty(p.LabelText))
                    lines.Add("Label: \"" + p.LabelText + "\" on " + p.LabelLayer +
                              "  (leader " + p.Leader + ")");

                if (p.RotationDegrees.HasValue)
                    lines.Add("Marker rotation: " +
                              p.RotationDegrees.Value.ToString("0.##", CultureInfo.InvariantCulture) +
                              " deg (rotates the Civil 3D marker; no block is inserted)");

                if (p.HasDripLine && p.DripRadius.HasValue)
                    lines.Add("Dripline: radius " +
                              p.DripRadius.Value.ToString("0.##", CultureInfo.InvariantCulture) +
                              " on " + p.DripLayer +
                              (p.DripUnify ? ", trimmed against neighbours" : ", drawn whole"));

                if (!string.IsNullOrEmpty(p.TagPrefix))
                    lines.Add("Tag: next free " + p.TagPrefix +
                              "-number; existing numbers are never reissued");
            }

            foreach (var d in p.Diagnostics)
                lines.Add(d.ToString());

            return string.Join(Environment.NewLine, lines.ToArray());
        }

        private bool IsLinework(string description)
        {
            if (string.IsNullOrWhiteSpace(description)) return false;
            foreach (var pattern in _cfg.LineworkPatterns)
                if (pattern.IsMatch(description.Trim())) return true;
            return false;
        }
    }

    /// <summary>A group of same-code points for the Unknown Codes page.</summary>
    public sealed class CodeGroup
    {
        public string Code { get; set; }
        public string Example { get; set; }
        public int Count { get; set; }
        public IList<string> ExamplePoints { get; set; }
        public string Reason { get; set; }
        public ReviewStatus Status { get; set; }

        public CodeGroup() { ExamplePoints = new List<string>(); }
    }

    public static class CodeGrouping
    {
        /// <summary>
        /// Groups review rows by leading code for the Unknown Codes page: everything
        /// that is not straightforwardly finished, with the reason it is not. A code
        /// with no rule is NOT an error -- the reasons say exactly what each group is.
        /// </summary>
        public static IList<CodeGroup> BuildUnknownView(IEnumerable<ReviewRow> rows)
        {
            var groups = new Dictionary<string, CodeGroup>(StringComparer.OrdinalIgnoreCase);

            foreach (var row in rows)
            {
                if (row.Status == ReviewStatus.Ok || row.Status == ReviewStatus.Warning)
                    continue;

                var code = FirstToken(row.RawDescription);
                var key = row.Status + "|" + code;

                CodeGroup group;
                if (!groups.TryGetValue(key, out group))
                {
                    group = new CodeGroup
                    {
                        Code = code,
                        Example = row.RawDescription,
                        Reason = row.Reason,
                        Status = row.Status
                    };
                    groups[key] = group;
                }

                group.Count++;
                if (group.ExamplePoints.Count < 5)
                    group.ExamplePoints.Add(row.PointNumber);
            }

            // Most actionable first: not configured, then errors, then the quiet ones.
            return groups.Values
                .OrderBy(g => Rank(g.Status))
                .ThenByDescending(g => g.Count)
                .ThenBy(g => g.Code, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static int Rank(ReviewStatus status)
        {
            switch (status)
            {
                case ReviewStatus.Error: return 0;
                case ReviewStatus.NotConfigured: return 1;
                case ReviewStatus.NothingToDo: return 2;
                default: return 3;
            }
        }

        private static string FirstToken(string description)
        {
            if (string.IsNullOrWhiteSpace(description)) return "(blank)";
            var trimmed = description.Trim();
            var space = trimmed.IndexOf(' ');
            return space < 0 ? trimmed : trimmed.Substring(0, space);
        }
    }
}
