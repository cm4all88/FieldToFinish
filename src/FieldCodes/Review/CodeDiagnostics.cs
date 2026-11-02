using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using FieldCodes.Settings;

namespace FieldCodes.Review
{
    /// <summary>
    /// The three kinds of thing the Unknown Codes page shows, kept apart because
    /// they ask different things of the person reading: an Error is a point FTF
    /// could not finish; Needs Configuration is a setup decision nobody has made;
    /// Informational is a normal condition that needs nothing at all.
    /// </summary>
    public enum CodeIssueCategory
    {
        /// <summary>Matched a rule and finishes (warnings included). Not an issue.</summary>
        None = 0,
        /// <summary>A point FTF could not finish: blank, unreadable or invalid.</summary>
        Error = 1,
        /// <summary>Carries data no rule is configured for.</summary>
        NeedsConfiguration = 2,
        /// <summary>Normal: linework, never-draw, ignored by you, bare codes.</summary>
        Informational = 3
    }

    /// <summary>Why a point landed in its category.</summary>
    public enum CodeIssueKind
    {
        /// <summary>Matched a rule; finishing is planned.</summary>
        Processed = 0,
        /// <summary>The description is empty.</summary>
        BlankDescription = 1,
        /// <summary>A rule matched, then part of the description could not be read
        /// (an unrecognised modifier, a trunk or rotation that is not a number).</summary>
        ParseFailure = 2,
        /// <summary>A rule matched and read a value the rule cannot use: missing
        /// dripline radius, unknown species, out-of-range value.</summary>
        InvalidData = 3,
        /// <summary>Carries data; no rule is configured for the code.</summary>
        NoRule = 4,
        /// <summary>A linework code: Civil 3D builds the figure, FTF labels the line.</summary>
        Linework = 5,
        /// <summary>On the rules file's never-draw list.</summary>
        IgnoredByRules = 6,
        /// <summary>Marked ignored in settings (diagnostics.ignoredCodes).</summary>
        IgnoredByUser = 7,
        /// <summary>Only the code: the Civil 3D symbol stands as-is.</summary>
        BareCode = 8
    }

    /// <summary>The verdict for one point.</summary>
    public sealed class CodeIssue
    {
        public CodeIssueCategory Category { get; set; }
        public CodeIssueKind Kind { get; set; }

        /// <summary>The code the point is grouped under: the leading token with any
        /// sequence digits removed (XYZ2 groups with XYZ); for a code ignored by you,
        /// the entry that ignores it; "(blank)" for an empty description.</summary>
        public string Code { get; set; }

        /// <summary>The rule a failed point matched, when it matched one.</summary>
        public string RuleId { get; set; }

        /// <summary>A few words for a table cell: "Blank description".</summary>
        public string Label { get; set; }

        /// <summary>The sentence that explains it, with the parser's own message for
        /// errors.</summary>
        public string Reason { get; set; }

        /// <summary>What to do about it, if anything.</summary>
        public string Action { get; set; }

        /// <summary>Processed, with warnings worth reading.</summary>
        public bool HasWarnings { get; set; }
    }

    /// <summary>Same-code points of one kind, for the Unknown Codes list.</summary>
    public sealed class CodeIssueGroup
    {
        public CodeIssueCategory Category { get; set; }
        public CodeIssueKind Kind { get; set; }
        public string Code { get; set; }
        public string RuleId { get; set; }
        public string Label { get; set; }
        public string Reason { get; set; }
        public string Action { get; set; }
        public int Count { get; set; }

        /// <summary>The first few point numbers, in the order met.</summary>
        public IList<string> ExamplePoints { get; private set; }

        /// <summary>Distinct real descriptions, which is what a rule is written against.</summary>
        public IList<string> Samples { get; private set; }

        public string Example { get { return Samples.Count > 0 ? Samples[0] : string.Empty; } }

        public CodeIssueGroup()
        {
            ExamplePoints = new List<string>();
            Samples = new List<string>();
        }
    }

    /// <summary>One point worth reading about on the Reports page.</summary>
    public sealed class PointIssue
    {
        public string PointNumber { get; set; }
        public string Code { get; set; }
        public string Description { get; set; }

        /// <summary>"Error", "Warning" or "Needs configuration".</summary>
        public string Severity { get; set; }

        /// <summary>Short kind: "Could not parse", "Invalid value"...</summary>
        public string Kind { get; set; }
        public string Reason { get; set; }
        public string RuleId { get; set; }
    }

    /// <summary>
    /// Counts for the Overview and Reports. The categories partition the points:
    /// Total = Eligible + NeedsConfiguration + SkippedIntentionally, and
    /// Eligible = Processed + Errors.
    /// </summary>
    public sealed class PointTally
    {
        private readonly HashSet<string> _unknownCodes =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public int Total { get; set; }

        /// <summary>Matched a rule and finish (labels, tags, driplines...).</summary>
        public int Processed { get; set; }

        /// <summary>Of <see cref="Processed"/>, those carrying warnings.</summary>
        public int ProcessedWithWarnings { get; set; }

        public int BlankDescriptions { get; set; }
        public int ParseFailures { get; set; }
        public int InvalidData { get; set; }

        /// <summary>Points carrying data no rule handles.</summary>
        public int NeedsConfiguration { get; set; }

        public int Linework { get; set; }
        public int IgnoredByRules { get; set; }
        public int IgnoredByUser { get; set; }
        public int BareCodes { get; set; }

        public int Errors { get { return BlankDescriptions + ParseFailures + InvalidData; } }

        /// <summary>Points FTF acts on: everything that matched a rule, plus the ones
        /// that should have and failed.</summary>
        public int Eligible { get { return Processed + Errors; } }

        /// <summary>Linework, never-draw, ignored by you and bare codes -- normal.</summary>
        public int SkippedIntentionally
        {
            get { return Linework + IgnoredByRules + IgnoredByUser + BareCodes; }
        }

        /// <summary>Distinct codes behind <see cref="NeedsConfiguration"/>.</summary>
        public int UnknownCodes { get { return _unknownCodes.Count; } }

        public void Add(CodeIssue issue)
        {
            if (issue == null) return;
            Total++;

            switch (issue.Kind)
            {
                case CodeIssueKind.Processed:
                    Processed++;
                    if (issue.HasWarnings) ProcessedWithWarnings++;
                    break;
                case CodeIssueKind.BlankDescription: BlankDescriptions++; break;
                case CodeIssueKind.ParseFailure: ParseFailures++; break;
                case CodeIssueKind.InvalidData: InvalidData++; break;
                case CodeIssueKind.NoRule:
                    NeedsConfiguration++;
                    _unknownCodes.Add(issue.Code ?? string.Empty);
                    break;
                case CodeIssueKind.Linework: Linework++; break;
                case CodeIssueKind.IgnoredByRules: IgnoredByRules++; break;
                case CodeIssueKind.IgnoredByUser: IgnoredByUser++; break;
                default: BareCodes++; break;
            }
        }
    }

    /// <summary>
    /// Sorts every parsed point into Error, Needs Configuration or Informational --
    /// the split the Unknown Codes page, the Overview and the Reports page share.
    ///
    /// A pure function of the parse result plus the rules and the codes marked
    /// ignored in settings: nothing here touches a drawing. Normal conditions are
    /// never reported as problems: Civil 3D linework codes, the never-draw list,
    /// codes you ignored, and bare codes whose symbol stands as-is are all
    /// Informational. A code you ignored is never counted as unknown.
    /// </summary>
    public sealed class CodeDiagnostics
    {
        public const string BlankCode = "(blank)";

        private readonly RulesConfig _cfg;
        private readonly List<string> _userIgnored;
        private readonly HashSet<string> _lineFeatureCodes;

        public CodeDiagnostics(RulesConfig cfg, IEnumerable<string> userIgnoredCodes)
        {
            if (cfg == null) throw new ArgumentNullException("cfg");
            _cfg = cfg;

            _userIgnored = (userIgnoredCodes ?? Enumerable.Empty<string>())
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(c => c.Trim().ToUpperInvariant())
                .Distinct()
                .ToList();

            _lineFeatureCodes = new HashSet<string>(
                (cfg.LineFeatures ?? new List<LineFeatureRule>())
                    .Where(f => f != null && f.Enabled && !string.IsNullOrWhiteSpace(f.Code))
                    .Select(f => f.Code.Trim()),
                StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>The codes marked ignored, normalised.</summary>
        public IList<string> UserIgnoredCodes { get { return _userIgnored.AsReadOnly(); } }

        // ------------------------------------------------------------ one point

        public CodeIssue Classify(ParsedPoint p)
        {
            if (p == null) throw new ArgumentNullException("p");

            var token = Token(p.RawDescription);
            var code = GroupCode(token);

            if (string.IsNullOrWhiteSpace(p.RawDescription))
                return Issue(CodeIssueCategory.Error, CodeIssueKind.BlankDescription, BlankCode,
                    "Blank description",
                    "The point has no description, so FTF cannot tell what it is.",
                    "Give the point its field code in Civil 3D (or the source file) and scan again.");

            if (p.Ignored)
            {
                if (DiagnosticsSettings.Matches(_cfg.IgnoreCodes, token))
                    return IgnoredByRules(code);

                if (DiagnosticsSettings.Matches(_cfg.LineworkCodes, token))
                    return LineworkIssue(code);

                var entry = UserEntry(token);
                if (entry != null) return IgnoredByUser(entry);

                return IgnoredByRules(code);
            }

            if (p.NoContent)
            {
                var entry = UserEntry(token);
                if (entry != null) return IgnoredByUser(entry);
                if (IsLineFeature(code)) return LineworkIssue(code);

                return Issue(CodeIssueCategory.Informational, CodeIssueKind.BareCode, code,
                    "Bare code",
                    "Only the code was shot: the Civil 3D symbol stands as-is and there is " +
                    "nothing to finish.",
                    "Nothing to do.");
            }

            if (p.Unhandled)
            {
                var entry = UserEntry(token);
                if (entry != null) return IgnoredByUser(entry);
                if (IsLineFeature(code)) return LineworkIssue(code);

                return Issue(CodeIssueCategory.NeedsConfiguration, CodeIssueKind.NoRule, code,
                    "No rule for code",
                    "Carries data (\"" + Normalize(p.RawDescription) + "\") but no rule is " +
                    "configured for " + code + ".",
                    "Configure a rule for " + code + " in Settings > Point Features, or mark " +
                    "the code ignored if it never needs finishing.");
            }

            if (p.HasErrors)
            {
                var errors = p.Diagnostics.Where(d => d.Severity == Severity.Error).ToList();
                var first = errors[0];
                var reason = first.Message +
                             (errors.Count > 1 ? " (+" + (errors.Count - 1) + " more)" : string.Empty);

                CodeIssue issue;
                if (first.Code == "EMPTY")
                    issue = Issue(CodeIssueCategory.Error, CodeIssueKind.BlankDescription, BlankCode,
                        "Blank description", reason,
                        "Give the point its field code and scan again.");
                else if (IsInvalidDataCode(first.Code))
                    issue = Issue(CodeIssueCategory.Error, CodeIssueKind.InvalidData, code,
                        "Invalid value", reason,
                        "The rule " + (p.RuleId ?? "?") + " matched but needs a valid value " +
                        "here: correct the description (or the rule's limits).");
                else
                    issue = Issue(CodeIssueCategory.Error, CodeIssueKind.ParseFailure, code,
                        "Could not parse", reason,
                        "The rule " + (p.RuleId ?? "?") + " matched but part of the description " +
                        "could not be read: correct the description, or extend the rule.");

                issue.RuleId = p.RuleId;
                return issue;
            }

            var processed = Issue(CodeIssueCategory.None, CodeIssueKind.Processed, code,
                p.HasWarnings ? "Processed, with warnings" : "Processed",
                p.HasWarnings
                    ? string.Join("; ", p.Diagnostics.Where(d => d.Severity == Severity.Warning)
                                         .Select(d => d.Message).ToArray())
                    : "Matched " + (p.RuleId ?? "a rule") + "; finishing is planned.",
                p.HasWarnings ? "Check the point; it is still finished." : "Nothing to do.");
            processed.RuleId = p.RuleId;
            processed.HasWarnings = p.HasWarnings;
            return processed;
        }

        // ----------------------------------------------------------- aggregates

        public PointTally Tally(IEnumerable<ParsedPoint> points)
        {
            var tally = new PointTally();
            foreach (var p in points ?? Enumerable.Empty<ParsedPoint>())
                if (p != null) tally.Add(Classify(p));
            return tally;
        }

        /// <summary>
        /// Every point that is not simply processed, grouped by kind and code: errors
        /// first, then codes needing configuration, then the informational ones; most
        /// frequent first within each.
        /// </summary>
        public IList<CodeIssueGroup> Group(IEnumerable<ParsedPoint> points)
        {
            var groups = new Dictionary<string, CodeIssueGroup>(StringComparer.OrdinalIgnoreCase);

            foreach (var p in points ?? Enumerable.Empty<ParsedPoint>())
            {
                if (p == null) continue;
                var issue = Classify(p);
                if (issue.Category == CodeIssueCategory.None) continue;

                var key = issue.Kind + "|" + issue.Code + "|" + issue.RuleId;
                CodeIssueGroup group;
                if (!groups.TryGetValue(key, out group))
                {
                    group = new CodeIssueGroup
                    {
                        Category = issue.Category,
                        Kind = issue.Kind,
                        Code = issue.Code,
                        RuleId = issue.RuleId,
                        Label = issue.Label,
                        Reason = issue.Reason,
                        Action = issue.Action
                    };
                    groups[key] = group;
                }

                group.Count++;
                if (group.ExamplePoints.Count < 5) group.ExamplePoints.Add(p.PointNumber);

                var sample = Normalize(p.RawDescription);
                if (group.Samples.Count < 5 && sample.Length > 0 &&
                    !group.Samples.Contains(sample, StringComparer.OrdinalIgnoreCase))
                    group.Samples.Add(sample);
            }

            return groups.Values
                .OrderBy(g => (int)g.Category)
                .ThenByDescending(g => g.Count)
                .ThenBy(g => g.Code, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// The points the Reports page lists: every error, every warning, and every
        /// point needing configuration -- with point number, code and reason. In
        /// point order.
        /// </summary>
        public IList<PointIssue> Issues(IEnumerable<ParsedPoint> points)
        {
            var issues = new List<PointIssue>();

            foreach (var p in points ?? Enumerable.Empty<ParsedPoint>())
            {
                if (p == null) continue;
                var issue = Classify(p);

                string severity;
                if (issue.Category == CodeIssueCategory.Error) severity = ErrorSeverity;
                else if (issue.Category == CodeIssueCategory.NeedsConfiguration) severity = ConfigurationSeverity;
                else if (issue.Kind == CodeIssueKind.Processed && issue.HasWarnings) severity = WarningSeverity;
                else continue;

                issues.Add(new PointIssue
                {
                    PointNumber = p.PointNumber,
                    Code = issue.Code,
                    Description = p.RawDescription ?? string.Empty,
                    Severity = severity,
                    Kind = issue.Kind == CodeIssueKind.Processed ? "Warning" : issue.Label,
                    Reason = issue.Reason,
                    RuleId = issue.RuleId
                });
            }

            return issues;
        }

        public const string ErrorSeverity = "Error";
        public const string WarningSeverity = "Warning";
        public const string ConfigurationSeverity = "Needs configuration";

        public static string CategoryText(CodeIssueCategory category)
        {
            switch (category)
            {
                case CodeIssueCategory.Error: return "Error";
                case CodeIssueCategory.NeedsConfiguration: return "Needs configuration";
                case CodeIssueCategory.Informational: return "Informational";
                default: return "Processed";
            }
        }

        // -------------------------------------------------------------- helpers

        private CodeIssue IgnoredByRules(string code)
        {
            return Issue(CodeIssueCategory.Informational, CodeIssueKind.IgnoredByRules, code,
                "Never-draw code",
                "On the rules file's ignore list (ignoreCodes): never drawn and never reported.",
                "Nothing to do. The list lives in the rules file.");
        }

        private static CodeIssue LineworkIssue(string code)
        {
            return Issue(CodeIssueCategory.Informational, CodeIssueKind.Linework, code,
                "Civil 3D linework",
                "Linework code: Civil 3D builds the figure from these points; FTF labels the " +
                "finished line, not the points.",
                "Nothing to do here. Label the line on the Lines page (FTFLABELLINE).");
        }

        private static CodeIssue IgnoredByUser(string entry)
        {
            return Issue(CodeIssueCategory.Informational, CodeIssueKind.IgnoredByUser, entry,
                "Ignored by you",
                "Marked ignored by you: skipped and not reported as unknown. A rule added for " +
                entry + " later still takes effect.",
                "Nothing to do. Stop Ignoring puts it back under Needs Configuration.");
        }

        private static CodeIssue Issue(CodeIssueCategory category, CodeIssueKind kind, string code,
                                       string label, string reason, string action)
        {
            return new CodeIssue
            {
                Category = category,
                Kind = kind,
                Code = code,
                Label = label,
                Reason = reason,
                Action = action
            };
        }

        private string UserEntry(string token)
        {
            foreach (var entry in _userIgnored)
                if (DiagnosticsSettings.Matches(new[] { entry }, token)) return entry;
            return null;
        }

        private bool IsLineFeature(string code)
        {
            return !string.IsNullOrEmpty(code) && _lineFeatureCodes.Contains(code);
        }

        /// <summary>Codes whose error means a value was read but cannot be used, as
        /// opposed to text that could not be read at all.</summary>
        private static bool IsInvalidDataCode(string diagnosticCode)
        {
            switch (diagnosticCode)
            {
                case "RANGE":
                case "SPECIES":
                case "DRIP":
                case "BLOCK":
                    return true;
                default:
                    return false;
            }
        }

        internal static string Normalize(string description)
        {
            if (string.IsNullOrWhiteSpace(description)) return string.Empty;
            return Regex.Replace(description.Trim(), @"\s+", " ");
        }

        /// <summary>The leading token of the description, as the parser sees it.</summary>
        public static string Token(string description)
        {
            var normalized = Normalize(description);
            return normalized.Length == 0 ? string.Empty : FieldCodeParser.FirstToken(normalized);
        }

        /// <summary>The token with sequence digits removed (XYZ2 -> XYZ), the way the
        /// rules file's code lists treat them. A token that is all digits, or blank,
        /// is kept as it is.</summary>
        public static string GroupCode(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return BlankCode;
            var t = token.Trim().ToUpperInvariant();
            var end = t.Length;
            while (end > 0 && char.IsDigit(t[end - 1])) end--;
            return end == 0 ? t : t.Substring(0, end);
        }
    }
}
