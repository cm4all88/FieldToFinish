using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using FieldCodes.Settings;

namespace FieldCodes.Annotation
{
    /// <summary>Whether a point gets an FTF label or tag, and why not when it does not.</summary>
    public sealed class AnnotationDecision
    {
        public bool Annotate { get; set; }
        public AnnotationCategory Category { get; set; }
        /// <summary>Why it is left alone; null when annotated.</summary>
        public string Reason { get; set; }

        internal static AnnotationDecision Yes(AnnotationCategory c) { return new AnnotationDecision { Annotate = true, Category = c }; }
        internal static AnnotationDecision No(AnnotationCategory c, string why) { return new AnnotationDecision { Annotate = false, Category = c, Reason = why }; }
    }

    /// <summary>
    /// The one place that decides what Label All, FTFLABELS, FTFTAGS and FTFTABLE annotate:
    /// which family a point belongs to, whether that family is switched on, how trees are
    /// annotated (tag or description or nothing -- never both), points marked "no label",
    /// and dip measurements kept out of label text. The CAD commands only ask.
    /// </summary>
    public static class AnnotationPolicy
    {
        private static readonly Dictionary<string, AnnotationCategory> ByRule =
            new Dictionary<string, AnnotationCategory>(StringComparer.OrdinalIgnoreCase)
            {
                { "tree", AnnotationCategory.Trees },
                { "sign", AnnotationCategory.Signs },
                { "sn-sign", AnnotationCategory.Signs },
                { "control", AnnotationCategory.Control },
                { "pole", AnnotationCategory.Utilities },
                { "light-pole", AnnotationCategory.Utilities },
                { "junction-box", AnnotationCategory.Utilities },
                { "guy-anchor", AnnotationCategory.Utilities },
                { "hydrant", AnnotationCategory.Utilities },
                { "water-valve", AnnotationCategory.Utilities },
                { "gas-valve", AnnotationCategory.Utilities },
                { "vault", AnnotationCategory.Utilities },
                { "manhole", AnnotationCategory.StormSewer },
                { "catch-basin", AnnotationCategory.StormSewer },
                { "mailbox", AnnotationCategory.OtherPoints },
                { "bollard", AnnotationCategory.OtherPoints }
            };

        /// <summary>Codes by family, for points labelled from their bare code or note
        /// (symbol labels) and rules the table above does not name.</summary>
        private static readonly Dictionary<string, AnnotationCategory> ByCode =
            new Dictionary<string, AnnotationCategory>(StringComparer.OrdinalIgnoreCase)
            {
                { "SSMH", AnnotationCategory.StormSewer }, { "SDMH", AnnotationCategory.StormSewer },
                { "CB", AnnotationCategory.StormSewer }, { "CBS", AnnotationCategory.StormSewer },
                { "SDAD", AnnotationCategory.StormSewer }, { "SDCB", AnnotationCategory.StormSewer },
                { "SSCO", AnnotationCategory.StormSewer }, { "SDCO", AnnotationCategory.StormSewer },
                { "MH", AnnotationCategory.StormSewer }, { "INL", AnnotationCategory.StormSewer },
                { "PP", AnnotationCategory.Utilities }, { "PPU", AnnotationCategory.Utilities },
                { "PPX", AnnotationCategory.Utilities }, { "PPUX", AnnotationCategory.Utilities },
                { "PVT", AnnotationCategory.Utilities }, { "PJB", AnnotationCategory.Utilities },
                { "LT", AnnotationCategory.Utilities }, { "LP", AnnotationCategory.Utilities },
                { "WFH", AnnotationCategory.Utilities }, { "FH", AnnotationCategory.Utilities },
                { "WVL", AnnotationCategory.Utilities }, { "WV", AnnotationCategory.Utilities },
                { "GVL", AnnotationCategory.Utilities }, { "GV", AnnotationCategory.Utilities },
                { "WMT", AnnotationCategory.Utilities }, { "GYA", AnnotationCategory.Utilities },
                { "UVLT", AnnotationCategory.Utilities }, { "TJB", AnnotationCategory.Utilities },
                { "SN", AnnotationCategory.Signs }, { "SNNP", AnnotationCategory.Signs },
                { "SNS", AnnotationCategory.Signs }, { "SNST", AnnotationCategory.Signs },
                { "SNMPH", AnnotationCategory.Signs }, { "SNY", AnnotationCategory.Signs },
                { "ASTO", AnnotationCategory.Pavement }, { "ALTO", AnnotationCategory.Pavement },
                { "ARTO", AnnotationCategory.Pavement }, { "ASLT", AnnotationCategory.Pavement },
                { "ASRT", AnnotationCategory.Pavement }, { "ASLR", AnnotationCategory.Pavement },
                { "BART", AnnotationCategory.Pavement },
                { "XMAG", AnnotationCategory.Control }, { "XHT", AnnotationCategory.Control },
                { "XNL", AnnotationCategory.Control }, { "FMAG", AnnotationCategory.Control },
                { "XRC", AnnotationCategory.Control }, { "FMON", AnnotationCategory.Control },
                { "FMIC", AnnotationCategory.Control },
                { "MB", AnnotationCategory.OtherPoints }, { "POST", AnnotationCategory.OtherPoints },
                { "BOL", AnnotationCategory.OtherPoints }
            };

        /// <summary>The family a point belongs to: the rule's own category when the rules file
        /// sets one, then the built-in rule and code tables, else Other point features.</summary>
        public static AnnotationCategory CategoryOf(ParsedPoint p, RulesConfig rules = null)
        {
            if (p == null) return AnnotationCategory.OtherPoints;
            if (rules != null && !string.IsNullOrEmpty(p.RuleId) && rules.Codes != null)
            {
                var rule = rules.Codes.FirstOrDefault(r => string.Equals(r.Id, p.RuleId, StringComparison.OrdinalIgnoreCase));
                AnnotationCategory own;
                if (rule != null && !string.IsNullOrWhiteSpace(rule.Category) && Enum.TryParse(rule.Category.Trim(), true, out own))
                    return own;
            }
            AnnotationCategory c;
            if (!string.IsNullOrEmpty(p.RuleId) && ByRule.TryGetValue(p.RuleId, out c)) return c;
            var code = BareCode(p.Code ?? FieldCodeParser.FirstToken(p.RawDescription ?? string.Empty));
            if (code.Length > 0 && ByCode.TryGetValue(code, out c)) return c;
            if (p.Species != null || p.TrunkInches.HasValue) return AnnotationCategory.Trees;
            return AnnotationCategory.OtherPoints;
        }

        private static string BareCode(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return string.Empty;
            var t = code.Trim().ToUpperInvariant();
            var end = t.Length;
            while (end > 1 && char.IsDigit(t[end - 1])) end--;
            return t.Substring(0, end);
        }

        // ------------------------------------------------------------ dip / depth notes

        private static readonly Regex DepthWord = new Regex(
            @"\b(MD|INV|IE|BOT|BOTTOM|WL|DEPTH|DPTH|DIP|SUMP)\b\.?\s*[=:]?\s*-?\d+(\.\d+)?",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex PipeDip = new Regex(
            @"^\s*\d+(\.\d+)?\s*(""|IN)?\s*(X\s*\d+(\.\d+)?\s*(""|IN)?)?\s+(RCP|PVC|CMP|DI|DIP|CI|CONC|HDPE|ADS|CPEP|CPP|VCP|AC|ACP|STL|STEEL|PE|CP|BOX|CLAY|PP|N12)\b.*\b\d+\.\d+\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>True for a note that records utility depth measurements -- a dip ("12 RCP
        /// N 6.41", "MD 6.25", "BOT 7.82", "WL 6.9", "INV 119.18") -- rather than describing the
        /// feature. Those numbers belong to the Dip Builder's structure labels.</summary>
        public static bool IsDepthNote(string note)
        {
            if (string.IsNullOrWhiteSpace(note)) return false;
            foreach (var line in note.Split(new[] { '\n', '\r', ';' }, StringSplitOptions.RemoveEmptyEntries))
                if (DepthWord.IsMatch(line) || PipeDip.IsMatch(line)) return true;
            return false;
        }

        /// <summary>Whether a crew note may be used as label text under these settings.</summary>
        public static bool MayUseNote(string note, AnnotationSettings settings)
        {
            if (string.IsNullOrWhiteSpace(note)) return true;
            return settings == null || !settings.ExcludeDepthNotes || !IsDepthNote(note);
        }

        // ------------------------------------------------------------ no-label marks

        /// <summary>True when a word after the code, or in the note, says not to label it.</summary>
        public static bool MarkedNoLabel(ParsedPoint p, AnnotationSettings settings)
        {
            if (p == null || settings == null || settings.NoLabelWords == null || settings.NoLabelWords.Count == 0) return false;
            var words = new HashSet<string>(settings.NoLabelWords.Where(w => !string.IsNullOrWhiteSpace(w)).Select(w => w.Trim()),
                                            StringComparer.OrdinalIgnoreCase);
            var raw = (p.RawDescription ?? string.Empty).Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (raw.Skip(1).Any(words.Contains)) return true;
            var note = (p.Note ?? string.Empty).Split(new[] { ' ', '\t', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            return note.Any(words.Contains);
        }

        // ------------------------------------------------------------ decisions

        /// <summary>Whether this point gets a descriptive label. <paramref name="scope"/> is
        /// the families this run covers (Label All's selection, or a Process preset's).</summary>
        public static AnnotationDecision Label(ParsedPoint p, AnnotationSettings settings, ICollection<AnnotationCategory> scope,
                                               RulesConfig rules = null)
        {
            var s = settings ?? new AnnotationSettings();
            var category = CategoryOf(p, rules);
            if (p == null || string.IsNullOrEmpty(p.LabelText)) return AnnotationDecision.No(category, "no label text");
            if (scope != null && !scope.Contains(category))
                return AnnotationDecision.No(category, Name(category) + " not selected");
            if (category == AnnotationCategory.Trees)
            {
                if (s.TreeAnnotation == TreeAnnotation.TagOnly) return AnnotationDecision.No(category, "trees are tagged, not labelled");
                if (s.TreeAnnotation == TreeAnnotation.None) return AnnotationDecision.No(category, "tree annotation is off");
            }
            if (MarkedNoLabel(p, s)) return AnnotationDecision.No(category, "marked no label");
            return AnnotationDecision.Yes(category);
        }

        /// <summary>Whether this point gets a numbered tag. Only rules with a tag prefix tag;
        /// trees only when the tree annotation is Tag only.</summary>
        public static AnnotationDecision Tag(ParsedPoint p, AnnotationSettings settings, ICollection<AnnotationCategory> scope,
                                             RulesConfig rules = null)
        {
            var s = settings ?? new AnnotationSettings();
            var category = CategoryOf(p, rules);
            if (p == null || string.IsNullOrEmpty(p.TagPrefix)) return AnnotationDecision.No(category, "the rule has no tag");
            if (p.HasErrors) return AnnotationDecision.No(category, "the description has errors");
            if (scope != null && !scope.Contains(category))
                return AnnotationDecision.No(category, Name(category) + " not selected");
            if (category == AnnotationCategory.Trees && s.TreeAnnotation != TreeAnnotation.TagOnly)
                return AnnotationDecision.No(category, s.TreeAnnotation == TreeAnnotation.None
                    ? "tree annotation is off" : "trees are labelled with their description, not tagged");
            if (MarkedNoLabel(p, s)) return AnnotationDecision.No(category, "marked no label");
            return AnnotationDecision.Yes(category);
        }

        /// <summary>Whether a tree goes in the schedule, and keyed how.</summary>
        public static bool ScheduleIncludesTrees(AnnotationSettings settings)
        {
            return settings != null && settings.ScheduleTrees;
        }

        public static string Name(AnnotationCategory c)
        {
            switch (c)
            {
                case AnnotationCategory.Trees: return "Trees";
                case AnnotationCategory.Utilities: return "Utilities";
                case AnnotationCategory.StormSewer: return "Storm and sewer structures (dips)";
                case AnnotationCategory.Signs: return "Signs";
                case AnnotationCategory.Control: return "Survey control";
                case AnnotationCategory.Pavement: return "Pavement markings";
                case AnnotationCategory.Lines: return "Line features";
                default: return "Other point features";
            }
        }

        /// <summary>Every family, in the order the settings list them.</summary>
        public static IList<AnnotationCategory> All()
        {
            return new List<AnnotationCategory>
            {
                AnnotationCategory.Trees, AnnotationCategory.Utilities, AnnotationCategory.StormSewer,
                AnnotationCategory.Signs, AnnotationCategory.Control, AnnotationCategory.Pavement,
                AnnotationCategory.OtherPoints, AnnotationCategory.Lines
            };
        }
    }
}
