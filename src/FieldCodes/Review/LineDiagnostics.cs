using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FieldCodes.Linework;
using FieldCodes.Settings;

namespace FieldCodes.Review
{
    /// <summary>Where one existing line stands with FTF's labelling.</summary>
    public enum LineStatusKind
    {
        /// <summary>Identified, label text configured, long enough for the automatic pass.</summary>
        Ready = 0,
        /// <summary>Identified, but the line feature has no label text yet.</summary>
        NoLabelText = 1,
        /// <summary>A shared layer whose candidate features label differently.</summary>
        Ambiguous = 2,
        /// <summary>A candidate line feature is switched off in the rules.</summary>
        Disabled = 3,
        /// <summary>Shorter than the automatic pass's minimum length.</summary>
        TooShort = 4,
        /// <summary>No configured line feature matches it.</summary>
        NotConfigured = 5,
        /// <summary>Its TBC name is on the rules' ignore list.</summary>
        IgnoredName = 6
    }

    /// <summary>The verdict for one line: a short category and the reason.</summary>
    public sealed class LineStatus
    {
        public LineStatusKind Kind { get; set; }

        /// <summary>A few words for the Status column: "No label text".</summary>
        public string Short { get; set; }

        /// <summary>Why, specifically, for this line.</summary>
        public string Reason { get; set; }

        /// <summary>What to do about it.</summary>
        public string Action { get; set; }

        /// <summary>Needs a decision from someone (no label text, ambiguous, disabled).</summary>
        public bool NeedsAttention
        {
            get
            {
                return Kind == LineStatusKind.NoLabelText || Kind == LineStatusKind.Ambiguous ||
                       Kind == LineStatusKind.Disabled;
            }
        }
    }

    /// <summary>One setting that governs a line's label, with its current value.</summary>
    public sealed class LineSetting
    {
        public string Name { get; set; }
        public string Value { get; set; }

        /// <summary>Where it is set: "Settings > Line Labels", "Rules: lineFeatures FNC",
        /// "Built-in".</summary>
        public string Where { get; set; }

        public LineSetting(string name, string value, string where)
        {
            Name = name;
            Value = value;
            Where = where;
        }
    }

    /// <summary>
    /// Turns the linework inventory's identification into something a person can act
    /// on: a short status, the reason, and every setting that decides the label --
    /// with its current value and where to change it.
    ///
    /// Describes; never decides placement. FTFLABELLINE stays the way a line is
    /// labelled by hand; the minimum length and repeat interval shape only the
    /// optional automatic pass, and the text says so.
    ///
    /// Pure: no drawing is read here.
    /// </summary>
    public static class LineDiagnostics
    {
        public const string LineLabelsSection = "Settings > Line Labels";
        public const string BuiltIn = "Built-in";

        public static LineStatus Classify(LineworkRow row, IList<LineFeatureRule> candidates,
                                          LineLabelSettings ls, double unitsPerFoot)
        {
            if (row == null) throw new ArgumentNullException("row");
            if (ls == null) ls = new LineLabelSettings();
            if (unitsPerFoot <= 0 || double.IsNaN(unitsPerFoot)) unitsPerFoot = 1.0;
            candidates = candidates ?? new List<LineFeatureRule>();

            if (row.Source == LineIdentitySource.None || candidates.Count == 0)
            {
                if (row.NameIgnored)
                    return Status(LineStatusKind.IgnoredName, "TBC name ignored",
                        "Its TBC name \"" + row.TrimbleName + "\" is on the rules' ignore list " +
                        "(ignoreLineNames): known, deliberately not a line feature.",
                        "Nothing to do.");

                return Status(LineStatusKind.NotConfigured, "Not a line feature",
                    "No configured line feature matches its " +
                    (string.IsNullOrEmpty(row.FigureName) ? string.Empty : "figure name " + row.FigureName + ", ") +
                    (string.IsNullOrEmpty(row.TrimbleName) ? string.Empty : "TBC name \"" + row.TrimbleName + "\", ") +
                    "or layer " + (string.IsNullOrEmpty(row.Layer) ? "(none)" : row.Layer) + ".",
                    "Normal for linework FTF does not label. To label it, add a line feature " +
                    "for this code or layer (Settings > Line Labels).");
            }

            var names = Describe(candidates);

            var disabled = candidates.Where(c => !c.Enabled).ToList();
            if (disabled.Count > 0)
                return Status(LineStatusKind.Disabled, "Feature disabled",
                    "Identified as " + names + ", but " +
                    string.Join(", ", disabled.Select(c => c.Code).ToArray()) +
                    " is switched off in the rules, so it is not labelled.",
                    "Enable the line feature to label it.");

            var unlabelled = candidates.Where(c => string.IsNullOrWhiteSpace(c.Label)).ToList();
            if (unlabelled.Count > 0)
                return Status(LineStatusKind.NoLabelText, "No label text",
                    "Identified as " + names + ", but " +
                    string.Join(", ", unlabelled.Select(c => c.Code).ToArray()) +
                    " has no label text configured, so there is nothing to place.",
                    "Give the line feature its label text (Settings > Line Labels).");

            var label = LineworkCatalog.UnanimousLabel(candidates);
            if (label == null)
                return Status(LineStatusKind.Ambiguous, "Ambiguous",
                    "Layer " + row.Layer + " is shared by " + names + ", which label differently (" +
                    string.Join(" / ", candidates.Select(c => c.Label).Distinct().ToArray()) + ").",
                    "FTFLABELLINE asks which one when you label it; the automatic pass skips it. " +
                    "A survey figure name (FNC2) identifies it exactly.");

            var minFeet = ls.MinLengthFeet;
            if (row.Length.HasValue && row.Length.Value / unitsPerFoot < minFeet)
                return Status(LineStatusKind.TooShort, "Below minimum length",
                    string.Format(CultureInfo.InvariantCulture,
                        "\"{0}\" - {1:0.#} ft is under the automatic pass's {2:0.#} ft minimum, " +
                        "so FTFLINELABELS leaves it unlabelled.",
                        label, row.Length.Value / unitsPerFoot, minFeet),
                    "FTFLABELLINE still labels it where you click. To change the minimum, " +
                    "Settings > Line Labels.");

            var side = LineSideParser.Resolve(row.SourceSide, candidates,
                LineSideParser.ParsePlacement(ls.DefaultPlacement));
            var preview = row.Length.HasValue
                ? LineLabelPlanner.Describe(label, row.Length.Value / unitsPerFoot,
                    ls.MinLengthFeet, ls.RepeatIntervalFeet, ls.EndClearanceFeet,
                    side, ls.SideOffsetFeet)
                : "Label existing line \"" + label + "\"";

            return Status(LineStatusKind.Ready, "Ready to label",
                preview + ".",
                ls.Enabled
                    ? "FTFLABELLINE places it where you click; the automatic pass (on) labels it as described."
                    : "FTFLABELLINE places it where you click. The automatic pass is off; the description is what it would do.");
        }

        /// <summary>
        /// Every setting that governs this line's label, with its current value and
        /// where to change it. Empty for a line that is not a configured line feature,
        /// apart from the settings that say how one would be labelled.
        /// </summary>
        public static IList<LineSetting> GoverningSettings(LineworkRow row,
            IList<LineFeatureRule> candidates, LineLabelSettings ls, string labelLayerText)
        {
            if (ls == null) ls = new LineLabelSettings();
            candidates = candidates ?? new List<LineFeatureRule>();
            var list = new List<LineSetting>();
            var where = candidates.Count == 0
                ? "Rules: lineFeatures"
                : "Rules: lineFeatures " + string.Join("/", candidates.Select(c => c.Code).ToArray());

            // label text / format
            if (candidates.Count > 0)
            {
                var labels = candidates.Select(c => string.IsNullOrWhiteSpace(c.Label) ? "(none)" : "\"" + c.Label + "\"")
                                       .Distinct().ToArray();
                var alternates = candidates.Where(c => c.AltLabels != null)
                    .SelectMany(c => c.AltLabels)
                    .Where(a => !string.IsNullOrWhiteSpace(a))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                list.Add(new LineSetting("Label text",
                    string.Join(" / ", labels) +
                    (alternates.Length > 0 ? "; alternates " + string.Join(", ", alternates) : string.Empty),
                    where));
            }
            else
            {
                list.Add(new LineSetting("Label text", "(not a line feature)", where));
            }

            // layer
            var ruleLayers = candidates.Where(c => !string.IsNullOrWhiteSpace(c.LabelLayer))
                                       .Select(c => c.LabelLayer).Distinct().ToArray();
            list.Add(new LineSetting("Label layer",
                (string.IsNullOrWhiteSpace(labelLayerText) ? "(resolved when labelled)" : labelLayerText) +
                (ruleLayers.Length > 0
                    ? "; rule layer " + string.Join(", ", ruleLayers)
                    : "; else the matching -TEXT layer, else fallback " + ls.DefaultLabelLayer),
                ruleLayers.Length > 0 ? where : LineLabelsSection + " (fallback layer)"));

            list.Add(new LineSetting("Text style / height",
                (string.IsNullOrWhiteSpace(ls.TextStyle) ? "(drawing's current style)" : ls.TextStyle) +
                ", " + Num(ls.TextHeightPlotted) + " plotted",
                LineLabelsSection));

            list.Add(new LineSetting("Minimum length",
                Num(ls.MinLengthFeet) + " ft - automatic pass only; FTFLABELLINE labels any length",
                LineLabelsSection));

            list.Add(new LineSetting("Repeat interval",
                Num(ls.RepeatIntervalFeet) + " ft, " + Num(ls.EndClearanceFeet) +
                " ft end clearance - automatic pass only",
                LineLabelsSection));

            list.Add(new LineSetting("Alignment",
                ls.AlignToLine ? "Along the line, turned to read upright" : "Horizontal",
                LineLabelsSection));

            var rulePlacement = candidates.Select(c => c.Placement)
                .FirstOrDefault(p => LineSideParser.ParsePlacement(p) != LineLabelSide.Auto);
            var placement = row != null && row.SourceSide.HasValue && row.SourceSide.Value != LineLabelSide.Auto
                ? "Source coding says " + row.SourceSide.Value + " (wins)"
                : rulePlacement != null
                    ? "Rule placement " + rulePlacement
                    : "Default " + (string.IsNullOrWhiteSpace(ls.DefaultPlacement) ? "OnLine" : ls.DefaultPlacement);
            list.Add(new LineSetting("Placement / offset",
                placement + "; side offset " + Num(ls.SideOffsetFeet) +
                " ft. In FTFLABELLINE the cursor picks the side.",
                rulePlacement != null ? where : LineLabelsSection));

            list.Add(new LineSetting("Masking",
                ls.DrawMask ? "Wipeout under each label, on the label's own layer" : "Off",
                LineLabelsSection));

            list.Add(new LineSetting("Duplicate prevention",
                "The automatic pass replaces only its own earlier labels and skips a line that " +
                "already carries an FTFLABELLINE label; hand-placed labels are never replaced. " +
                "Not a setting.",
                BuiltIn));

            var always = candidates.Count > 0 && LineworkCatalog.LabelsOverMarkedLinetype(candidates);
            list.Add(new LineSetting("Lettered linetypes",
                ls.LeaveMarkedLinetypesToLegend
                    ? (always ? "Left to the legend, but this feature is alwaysLabel: labelled anyway"
                              : "A linetype that draws its own letters is left to the legend (automatic pass)")
                    : "Labelled like any other line",
                LineLabelsSection));

            list.Add(new LineSetting("Automatic pass",
                ls.Enabled ? "On: FTFRUN labels every identified line"
                           : "Off: FTFLABELLINE (and FTFLABELALL when pressed) only",
                LineLabelsSection));

            return list;
        }

        /// <summary>Count per status kind, for the summary line.</summary>
        public static IDictionary<LineStatusKind, int> Count(IEnumerable<LineStatus> statuses)
        {
            var counts = new Dictionary<LineStatusKind, int>();
            foreach (var s in statuses ?? Enumerable.Empty<LineStatus>())
            {
                if (s == null) continue;
                int n;
                counts.TryGetValue(s.Kind, out n);
                counts[s.Kind] = n + 1;
            }
            return counts;
        }

        /// <summary>"12 ready, 3 no label text, 40 not line features" -- non-zero kinds
        /// in status order.</summary>
        public static string Summary(IDictionary<LineStatusKind, int> counts)
        {
            if (counts == null || counts.Count == 0) return "No line objects.";
            var parts = new List<string>();
            foreach (LineStatusKind kind in Enum.GetValues(typeof(LineStatusKind)))
            {
                int n;
                if (!counts.TryGetValue(kind, out n) || n == 0) continue;
                parts.Add(n.ToString(CultureInfo.InvariantCulture) + " " + ShortPlural(kind));
            }
            return string.Join(", ", parts.ToArray()) + ".";
        }

        public static string ShortText(LineStatusKind kind)
        {
            switch (kind)
            {
                case LineStatusKind.Ready: return "Ready to label";
                case LineStatusKind.NoLabelText: return "No label text";
                case LineStatusKind.Ambiguous: return "Ambiguous";
                case LineStatusKind.Disabled: return "Feature disabled";
                case LineStatusKind.TooShort: return "Below minimum length";
                case LineStatusKind.IgnoredName: return "TBC name ignored";
                default: return "Not a line feature";
            }
        }

        private static string ShortPlural(LineStatusKind kind)
        {
            switch (kind)
            {
                case LineStatusKind.Ready: return "ready to label";
                case LineStatusKind.NoLabelText: return "with no label text";
                case LineStatusKind.Ambiguous: return "ambiguous";
                case LineStatusKind.Disabled: return "disabled";
                case LineStatusKind.TooShort: return "below minimum length";
                case LineStatusKind.IgnoredName: return "ignored by TBC name";
                default: return "not line features";
            }
        }

        private static string Describe(IList<LineFeatureRule> candidates)
        {
            return string.Join(" / ", candidates
                .Select(c => string.IsNullOrWhiteSpace(c.Name) ? c.Code : c.Code + " (" + c.Name + ")")
                .Distinct()
                .ToArray());
        }

        private static LineStatus Status(LineStatusKind kind, string shortText, string reason, string action)
        {
            return new LineStatus { Kind = kind, Short = shortText, Reason = reason, Action = action };
        }

        private static string Num(double v)
        {
            return v.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }
}
