using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace FieldCodes.Compare
{
    /// <summary>
    /// Decides what physical feature a drawing object represents, from every source of
    /// evidence the profile knows how to read: the description code, the block name,
    /// key words in descriptions / attributes / labels, and the layer. Each agreeing
    /// source raises confidence; disagreeing sources (layer says storm, code says
    /// sanitary) are a CONFLICT -- the strongest wins but the feature is marked so any
    /// match it takes part in goes to review rather than being accepted silently.
    ///
    /// No evidence at all is UNKNOWN. An unknown feature is never matched and never
    /// declared missing: it is reported as not evaluated, with the layer or block the
    /// profile would need to map.
    /// </summary>
    public sealed class FeatureClassifier
    {
        private const double CodeWeight = 1.0;
        private const double BlockWeight = 0.9;
        private const double KeywordWeight = 0.8;
        private const double LayerWeight = 0.6;

        private readonly CompareProfile _profile;
        private readonly List<CategoryRule> _points;
        private readonly List<CategoryRule> _lines;
        private static readonly Dictionary<string, Regex> PhraseCache = new Dictionary<string, Regex>(StringComparer.Ordinal);

        public FeatureClassifier(CompareProfile profile)
        {
            if (profile == null) throw new ArgumentNullException("profile");
            _profile = profile;
            _points = profile.Categories.Where(c => c.Enabled && c.Kind == FeatureKind.Point).ToList();
            _lines = profile.Categories.Where(c => c.Enabled && c.Kind == FeatureKind.Line).ToList();
        }

        private sealed class Score
        {
            public CategoryRule Category;
            public double Total;
            public bool Code, Block, Keyword, Layer;
            public int LayerSpecificity, BlockSpecificity;
            public int Types { get { return (Code ? 1 : 0) + (Block ? 1 : 0) + (Keyword ? 1 : 0) + (Layer ? 1 : 0); } }
            public bool Specific { get { return Code || Block || Keyword; } }
            public string Evidence
            {
                get
                {
                    var parts = new List<string>();
                    if (Code) parts.Add("code");
                    if (Block) parts.Add("block");
                    if (Keyword) parts.Add("keyword");
                    if (Layer) parts.Add("layer");
                    return string.Join("+", parts.ToArray());
                }
            }
        }

        /// <summary>Classifies and stores the result on the feature. Annotation is not
        /// classified here -- it inherits from the feature it belongs to.</summary>
        public FeatureClass Classify(CompareFeature f)
        {
            if (f == null) throw new ArgumentNullException("f");
            if (f.Kind == FeatureKind.Annotation)
            {
                f.Class = new FeatureClass { Category = string.Empty, System = "ANNOTATION", Confidence = 1.0, Evidence = "annotation" };
                return f.Class;
            }

            var rules = f.Kind == FeatureKind.Point ? _points : _lines;
            var code = CodeOf(f.Description);
            var text = SearchText(f);
            var scores = new List<Score>();

            foreach (var rule in rules)
            {
                var map = rule.For(f.Source);
                var s = new Score { Category = rule };
                if (code != null && map.Codes.Any(c => string.Equals(StripDigits(c), code, StringComparison.OrdinalIgnoreCase))) { s.Code = true; s.Total += CodeWeight; }
                if (!string.IsNullOrEmpty(f.BlockName) && Wildcard.MatchesAny(map.Blocks, f.BlockName)) { s.Block = true; s.Total += BlockWeight; s.BlockSpecificity = Specificity(map.Blocks, f.BlockName); }
                if (text.Length > 0 && map.Keywords.Any(k => ContainsPhrase(text, k))) { s.Keyword = true; s.Total += KeywordWeight; }
                if (Wildcard.MatchesAny(map.Layers, f.Layer)) { s.Layer = true; s.Total += LayerWeight; s.LayerSpecificity = Specificity(map.Layers, f.Layer); }
                if (s.Total > 0) scores.Add(s);
            }

            if (scores.Count == 0)
            {
                f.Class = FeatureClass.Unknown(UnknownReason(f, code));
                return f.Class;
            }

            // Highest score wins; ties broken by the more specific evidence, then by
            // profile order so the result is deterministic.
            scores.Sort((a, b) =>
            {
                var byTotal = b.Total.CompareTo(a.Total);
                if (byTotal != 0) return byTotal;
                var bySpecific = b.Specific.CompareTo(a.Specific);
                if (bySpecific != 0) return bySpecific;
                var byPattern = (b.LayerSpecificity + b.BlockSpecificity).CompareTo(a.LayerSpecificity + a.BlockSpecificity);
                if (byPattern != 0) return byPattern;
                return rules.IndexOf(a.Category).CompareTo(rules.IndexOf(b.Category));
            });

            var best = scores[0];
            var conflict = false;
            string conflictNote = null;
            if (scores.Count > 1)
            {
                var second = scores[1];
                var secondIsOnlyLayer = second.Layer && !second.Specific;
                var bestIsOnlyLayer = best.Layer && !best.Specific;
                // Specific evidence beats a generic layer wildcard that another category
                // of the SAME system also claims (a CB block on the storm structures
                // layer); between two layer wildcards the clearly more specific pattern
                // wins (*CURB*BACK* over *CURB*). A code that says storm on a layer that
                // says sanitary is a real disagreement and stays one.
                var sameSystem = string.Equals(best.Category.System, second.Category.System, StringComparison.OrdinalIgnoreCase);
                var patternDecides = bestIsOnlyLayer && secondIsOnlyLayer && best.LayerSpecificity >= second.LayerSpecificity + 2;
                var blockDecides = best.Block && second.Block && !best.Code && !second.Code && best.BlockSpecificity >= second.BlockSpecificity + 2 && best.Total >= second.Total;
                if (!(best.Specific && secondIsOnlyLayer && sameSystem) && !patternDecides && !blockDecides)
                {
                    conflict = true;
                    conflictNote = "also matches " + second.Category.Key + " by " + second.Evidence;
                }
            }

            var confidence = best.Types >= 3 ? 0.95 : best.Types == 2 ? 0.9 : 0.8;
            if (conflict) confidence = Math.Min(confidence, 0.5);

            f.Class = new FeatureClass
            {
                Category = best.Category.Key,
                System = best.Category.System,
                Confidence = confidence,
                Conflict = conflict,
                Evidence = best.Evidence + (conflictNote == null ? string.Empty : "; " + conflictNote)
            };
            return f.Class;
        }

        public void ClassifyAll(CompareDataset dataset)
        {
            if (dataset == null) return;
            foreach (var f in dataset.Features) Classify(f);
        }

        /// <summary>Literal (non-wildcard) characters in the most specific matching pattern.</summary>
        private static int Specificity(IEnumerable<string> patterns, string text)
        {
            var best = 0;
            foreach (var p in patterns)
            {
                if (!Wildcard.Matches(p, text)) continue;
                var literal = p.Replace("*", string.Empty).Replace("?", string.Empty).Trim().Length;
                if (literal > best) best = literal;
            }
            return best;
        }

        private static string UnknownReason(CompareFeature f, string code)
        {
            var sb = new StringBuilder();
            sb.Append("not mapped in profile: layer '").Append(f.Layer ?? string.Empty).Append('\'');
            if (!string.IsNullOrEmpty(f.BlockName)) sb.Append(", block '").Append(f.BlockName).Append('\'');
            if (!string.IsNullOrEmpty(code)) sb.Append(", code '").Append(code).Append('\'');
            return sb.ToString();
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>The leading code of a description with trailing digits removed:
        /// "SDMH1 1234" → SDMH, "PP 1234 DEAD" → PP, "EC2" → EC. Null when empty.</summary>
        public static string CodeOf(string description)
        {
            if (string.IsNullOrWhiteSpace(description)) return null;
            var first = description.Trim().Split(new[] { ' ', '\t', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)[0];
            return StripDigits(first);
        }

        internal static string StripDigits(string token)
        {
            if (string.IsNullOrEmpty(token)) return null;
            var end = token.Length;
            while (end > 0 && char.IsDigit(token[end - 1])) end--;
            return end == 0 ? token : token.Substring(0, end);
        }

        private static string SearchText(CompareFeature f)
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(f.Description)) sb.Append(f.Description).Append(' ');
            if (!string.IsNullOrEmpty(f.BlockName)) sb.Append(f.BlockName.Replace('_', ' ').Replace('-', ' ')).Append(' ');
            if (f.Attributes != null)
                foreach (var kv in f.Attributes) sb.Append(kv.Key).Append(' ').Append(kv.Value).Append(' ');
            return sb.ToString().ToUpperInvariant();
        }

        /// <summary>Whole-word phrase match, case-insensitive: "STORM MH" is found in
        /// "EX STORM MH RIM 123" but "CB" is not found in "CURB".</summary>
        public static bool ContainsPhrase(string text, string phrase)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(phrase)) return false;
            Regex regex;
            lock (PhraseCache)
            {
                if (!PhraseCache.TryGetValue(phrase, out regex))
                {
                    var words = phrase.Trim().ToUpperInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(Regex.Escape).ToArray();
                    regex = new Regex("(^|[^A-Z0-9])" + string.Join("[\\s_\\-]+", words) + "([^A-Z0-9]|$)", RegexOptions.CultureInvariant);
                    PhraseCache[phrase] = regex;
                }
            }
            return regex.IsMatch(text.ToUpperInvariant());
        }
    }
}
