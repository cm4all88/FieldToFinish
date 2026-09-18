using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FieldCodes.Linework
{
    /// <summary>How a piece of linework was identified.</summary>
    public enum LineIdentitySource
    {
        /// <summary>Not a configured line feature.</summary>
        None = 0,
        /// <summary>By the entity's layer -- may be ambiguous when codes share one.</summary>
        Layer = 1,
        /// <summary>By the survey figure's own name (RWC3 -> RWC). Exact.</summary>
        FigureName = 2,
        /// <summary>By TrimbleName XData the TBC export left on the entity -- either
        /// raw source coding ("ASPH L") or the TBC feature name ("Edge of Pavement"),
        /// matched exactly against a configured code, name or label. Exact.</summary>
        TrimbleName = 3
    }

    /// <summary>One existing Civil 3D line object, identified for the inventory.</summary>
    public sealed class LineworkRow
    {
        public string EntityType { get; set; }
        public string FigureName { get; set; }

        /// <summary>TrimbleName XData surviving from the TBC export, or null. Kept on
        /// the row even when it identifies nothing: an unidentified line that at least
        /// says "Top of Slope" is reviewable in a way a bare polyline is not.</summary>
        public string TrimbleName { get; set; }

        public string Layer { get; set; }

        /// <summary>The matched code(s). More than one when a shared layer is all
        /// there is to go on.</summary>
        public string Codes { get; set; }

        public string FeatureName { get; set; }
        public LineIdentitySource Source { get; set; }
        public double? Length { get; set; }
        public string ProposedAction { get; set; }

        /// <summary>Directional modifier read from the source coding ("ASPH LEFT"),
        /// or null when the coding does not specify one. Never guessed.</summary>
        public LineLabelSide? SourceSide { get; set; }

        /// <summary>True when the TrimbleName is on the configured ignore list:
        /// unidentified on purpose, so the review stops flagging it as unknown.</summary>
        public bool NameIgnored { get; set; }

        /// <summary>Where this feature's label would go and why, display-ready --
        /// e.g. "V-CHAN-STRP-TEXT-E (matches source layer)". Set from the label
        /// layer resolver for identified rows; empty otherwise.</summary>
        public string LabelLayerText { get; set; }

        public string LengthText
        {
            get
            {
                return Length.HasValue
                    ? Length.Value.ToString("0.#", CultureInfo.InvariantCulture)
                    : "-";
            }
        }

        public string SourceText
        {
            get
            {
                switch (Source)
                {
                    case LineIdentitySource.FigureName: return "figure name";
                    case LineIdentitySource.TrimbleName: return "Trimble name";
                    case LineIdentitySource.Layer: return "layer";
                    default: return "-";
                }
            }
        }
    }

    /// <summary>
    /// Identifies existing Civil 3D linework against the configured line features.
    ///
    /// Three identification paths, best first:
    ///   1. The survey figure's name -- "RWC3" is the third RWC string, so stripping
    ///      the trailing digits gives the source code exactly.
    ///   2. TrimbleName XData the TBC export left on the entity -- raw source coding
    ///      or the TBC feature name, matched exactly, never fuzzily.
    ///   3. The entity's layer, for plain polylines that are not survey figures. The
    ///      office Figure Prefix Database maps each code to a layer, so the layer
    ///      names the feature -- ambiguously when several codes share one, in which
    ///      case every candidate is reported rather than one being guessed.
    ///
    /// Pure identification: nothing here reads or writes a drawing, and the proposed
    /// action for an identified feature is labelling the EXISTING line. FTF never
    /// recreates survey geometry.
    /// </summary>
    public sealed class LineworkCatalog
    {
        private readonly List<LineFeatureRule> _features;
        private readonly Dictionary<string, LineFeatureRule> _byCode;
        private readonly Dictionary<string, List<LineFeatureRule>> _byLayer;
        private readonly HashSet<string> _ignoredNames;

        public LineworkCatalog(IEnumerable<LineFeatureRule> features,
                               IEnumerable<string> ignoreLineNames = null)
        {
            _ignoredNames = new HashSet<string>(
                (ignoreLineNames ?? Enumerable.Empty<string>())
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Select(n => n.Trim()),
                StringComparer.OrdinalIgnoreCase);

            _features = (features ?? Enumerable.Empty<LineFeatureRule>())
                .Where(f => f != null && !string.IsNullOrWhiteSpace(f.Code))
                .ToList();

            _byCode = new Dictionary<string, LineFeatureRule>(StringComparer.OrdinalIgnoreCase);
            _byLayer = new Dictionary<string, List<LineFeatureRule>>(StringComparer.OrdinalIgnoreCase);

            foreach (var feature in _features)
            {
                if (!_byCode.ContainsKey(feature.Code))
                    _byCode[feature.Code] = feature;

                if (string.IsNullOrWhiteSpace(feature.Layer)) continue;

                List<LineFeatureRule> list;
                if (!_byLayer.TryGetValue(feature.Layer, out list))
                {
                    list = new List<LineFeatureRule>();
                    _byLayer[feature.Layer] = list;
                }
                list.Add(feature);
            }
        }

        public int Count { get { return _features.Count; } }
        public IList<LineFeatureRule> Features { get { return _features.AsReadOnly(); } }

        /// <summary>"RWC3" -> the RWC feature; null when the code is not configured.
        /// Directional modifiers in the coding ("ASPH LEFT") are stripped first.</summary>
        public LineFeatureRule FindByFigureName(string figureName)
        {
            string codePart;
            LineLabelSide? ignored;
            LineSideParser.Split(figureName, out codePart, out ignored);

            var code = StripTrailingDigits(codePart);
            if (string.IsNullOrEmpty(code)) return null;

            LineFeatureRule feature;
            return _byCode.TryGetValue(code, out feature) ? feature : null;
        }

        /// <summary>
        /// Features matching a TrimbleName XData value. Three exact forms, never
        /// fuzzy: the raw source coding ("ASPH L", "RWC1"), resolved like a figure
        /// name so a surviving directional modifier fires; a confirmed alias from the
        /// rule's own configuration ("Edge of Conc" listed on EC); or the TBC feature
        /// name matched case-insensitively against each rule's name or label. An
        /// unlisted abbreviation matches nothing -- mappings are configured, never
        /// guessed.
        /// </summary>
        public IList<LineFeatureRule> FindByTrimbleName(string trimbleName)
        {
            if (string.IsNullOrWhiteSpace(trimbleName)) return new List<LineFeatureRule>();

            var byCode = FindByFigureName(trimbleName);
            if (byCode != null) return new List<LineFeatureRule> { byCode };

            var name = trimbleName.Trim();

            var byAlias = _features
                .Where(f => f.Aliases != null && f.Aliases.Any(a =>
                    !string.IsNullOrWhiteSpace(a) &&
                    string.Equals(name, a.Trim(), StringComparison.OrdinalIgnoreCase)))
                .ToList();
            if (byAlias.Count > 0) return byAlias;

            return _features
                .Where(f => string.Equals(name, f.Name, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(name, f.Label, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        /// <summary>
        /// Every configured line feature whose code appears as a token in a raw
        /// survey point description, digits stripped -- "BLD B EC B BLD1 B" names
        /// both the building and the concrete edge. Token-exact, never substring:
        /// control codes like B and unknown words match nothing.
        /// </summary>
        public IList<LineFeatureRule> FindInDescription(string description)
        {
            var found = new List<LineFeatureRule>();
            if (string.IsNullOrWhiteSpace(description)) return found;

            foreach (var token in description.Split(
                new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var code = StripTrailingDigits(token);
                if (code == null) continue;

                LineFeatureRule feature;
                if (_byCode.TryGetValue(code, out feature) && !found.Contains(feature))
                    found.Add(feature);
            }

            return found;
        }

        /// <summary>True when a TBC name is on the configured ignore list: known,
        /// deliberately mapped to nothing.</summary>
        public bool IsIgnoredName(string trimbleName)
        {
            return !string.IsNullOrWhiteSpace(trimbleName) &&
                   _ignoredNames.Contains(trimbleName.Trim());
        }

        /// <summary>All features assigned to a layer. Several codes can share one.</summary>
        public IList<LineFeatureRule> FindByLayer(string layer)
        {
            if (string.IsNullOrWhiteSpace(layer)) return new List<LineFeatureRule>();

            List<LineFeatureRule> list;
            return _byLayer.TryGetValue(layer.Trim(), out list)
                ? (IList<LineFeatureRule>)list.AsReadOnly()
                : new List<LineFeatureRule>();
        }

        /// <summary>
        /// Builds the inventory row for one existing line object. Figure name wins
        /// over TrimbleName XData, which wins over the layer; a shared layer reports
        /// every candidate rather than guessing.
        /// </summary>
        public LineworkRow Identify(string entityType, string figureName, string layer,
                                    double? length, string trimbleName = null)
        {
            string codePart;
            LineLabelSide? sourceSide;
            LineSideParser.Split(figureName, out codePart, out sourceSide);

            if (sourceSide == null)
                LineSideParser.Split(trimbleName, out codePart, out sourceSide);

            var row = new LineworkRow
            {
                EntityType = entityType,
                FigureName = figureName ?? string.Empty,
                TrimbleName = trimbleName ?? string.Empty,
                Layer = layer ?? string.Empty,
                Length = length,
                SourceSide = sourceSide
            };

            var byName = FindByFigureName(figureName);
            if (byName != null)
            {
                row.Source = LineIdentitySource.FigureName;
                row.Codes = byName.Code;
                row.FeatureName = string.IsNullOrEmpty(byName.Name) ? byName.Code : byName.Name;
                row.ProposedAction = ProposedActionFor(row.FeatureName);
                return row;
            }

            var byTrimble = FindByTrimbleName(trimbleName);
            if (byTrimble.Count > 0)
            {
                row.Source = LineIdentitySource.TrimbleName;
                row.Codes = string.Join("/", byTrimble.Select(f => f.Code).ToArray());
                row.FeatureName = string.Join(" / ",
                    byTrimble.Select(f => string.IsNullOrEmpty(f.Name) ? f.Code : f.Name)
                             .Distinct()
                             .ToArray());
                row.ProposedAction = ProposedActionFor(row.FeatureName);
                return row;
            }

            var byLayer = FindByLayer(layer);
            if (byLayer.Count > 0)
            {
                row.Source = LineIdentitySource.Layer;
                row.Codes = string.Join("/", byLayer.Select(f => f.Code).ToArray());
                row.FeatureName = string.Join(" / ",
                    byLayer.Select(f => string.IsNullOrEmpty(f.Name) ? f.Code : f.Name)
                           .Distinct()
                           .ToArray());
                row.ProposedAction = ProposedActionFor(row.FeatureName);
                return row;
            }

            row.Source = LineIdentitySource.None;
            row.Codes = "-";
            row.FeatureName = "-";
            row.NameIgnored = IsIgnoredName(trimbleName);
            row.ProposedAction = row.NameIgnored
                ? "None - TBC name intentionally ignored"
                : "None - not a configured line feature";
            return row;
        }

        /// <summary>
        /// The finishing FTF would eventually apply. Deliberately provisional: the
        /// labelling standard is not configured yet, and the wording never suggests
        /// touching the Civil 3D geometry itself.
        /// </summary>
        private static string ProposedActionFor(string featureName)
        {
            return "Label existing line as " + featureName + " (standard not configured yet)";
        }

        /// <summary>
        /// The label text for a set of candidate features. Ambiguity is tolerable
        /// exactly when it does not matter: CU and CX both label "CURB", so a curb
        /// polyline can be labelled without knowing which code shot it. Candidates
        /// that disagree - or any without a configured label - resolve to null.
        /// </summary>
        public static string UnanimousLabel(IList<LineFeatureRule> candidates)
        {
            if (candidates == null || candidates.Count == 0) return null;

            string label = null;
            foreach (var candidate in candidates)
            {
                if (!candidate.Enabled) return null;
                if (string.IsNullOrWhiteSpace(candidate.Label)) return null;

                if (label == null) label = candidate.Label;
                else if (!string.Equals(label, candidate.Label, StringComparison.Ordinal))
                    return null;
            }

            return label;
        }

        /// <summary>
        /// Every wording the surveyor may place for these candidates: the agreed
        /// label first, then the configured alternates ("EOP"), deduplicated. Empty
        /// when no unanimous label exists -- alternates never rescue a disagreement.
        /// </summary>
        public static IList<string> LabelChoices(IList<LineFeatureRule> candidates)
        {
            var choices = new List<string>();

            var primary = UnanimousLabel(candidates);
            if (primary == null) return choices;
            choices.Add(primary);

            foreach (var candidate in candidates)
            {
                if (candidate.AltLabels == null) continue;
                foreach (var alt in candidate.AltLabels)
                {
                    if (string.IsNullOrWhiteSpace(alt)) continue;
                    var trimmed = alt.Trim();
                    if (!choices.Any(c =>
                            string.Equals(c, trimmed, StringComparison.OrdinalIgnoreCase)))
                        choices.Add(trimmed);
                }
            }

            return choices;
        }

        /// <summary>
        /// The wordings for labelling the SURFACE between two of these edges --
        /// "ASPHALT" / "ASPH" between asphalt edges, never "EDGE OF PAVEMENT".
        /// Falls back to the edge wordings when no between-label is configured, so
        /// features like curbs still label between their lines somehow.
        /// </summary>
        public static IList<string> BetweenChoices(IList<LineFeatureRule> candidates)
        {
            var choices = new List<string>();

            foreach (var candidate in candidates ?? new List<LineFeatureRule>())
            {
                if (!candidate.Enabled || candidate.BetweenLabels == null) continue;
                foreach (var wording in candidate.BetweenLabels)
                {
                    if (string.IsNullOrWhiteSpace(wording)) continue;
                    var trimmed = wording.Trim();
                    if (!choices.Any(c =>
                            string.Equals(c, trimmed, StringComparison.OrdinalIgnoreCase)))
                        choices.Add(trimmed);
                }
            }

            return choices.Count > 0 ? choices : LabelChoices(candidates);
        }

        /// <summary>The candidates one entity resolves to: exactly one for a figure
        /// name, possibly several for a shared layer.</summary>
        public IList<LineFeatureRule> Candidates(string figureName, string layer)
        {
            return Candidates(figureName, null, layer);
        }

        /// <summary>As above, with surviving TrimbleName XData between the figure name
        /// and the layer in precedence.</summary>
        public IList<LineFeatureRule> Candidates(string figureName, string trimbleName,
                                                 string layer)
        {
            var byName = FindByFigureName(figureName);
            if (byName != null) return new List<LineFeatureRule> { byName };

            var byTrimble = FindByTrimbleName(trimbleName);
            if (byTrimble.Count > 0) return byTrimble;

            return FindByLayer(layer);
        }

        internal static string StripTrailingDigits(string figureName)
        {
            if (string.IsNullOrWhiteSpace(figureName)) return null;

            var trimmed = figureName.Trim();
            var end = trimmed.Length;
            while (end > 0 && char.IsDigit(trimmed[end - 1])) end--;

            return end == 0 ? null : trimmed.Substring(0, end);
        }
    }
}
