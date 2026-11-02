using System;
using System.Collections.Generic;
using System.Linq;
using FieldCodes.Annotation;
using FieldCodes.Settings;

namespace FieldCodes.Styles
{
    /// <summary>The text look one piece of annotation gets, every value settled.</summary>
    public sealed class ResolvedTextStyle
    {
        /// <summary>The named style, or the settings page the values come from.</summary>
        public string Name { get; set; }
        /// <summary>Drawing text style; empty means the drawing's current one.</summary>
        public string DrawingTextStyle { get; set; }
        public double HeightPlotted { get; set; }
        public double WidthFactor { get; set; }
        public double ObliqueDegrees { get; set; }
        public TextRotation Rotation { get; set; }
        public double RotationDegrees { get; set; }
        public TextJustification Justification { get; set; }
        /// <summary>Empty: ByLayer.</summary>
        public string Color { get; set; }
        public MaskMode Mask { get; set; }
        public string MaskLayer { get; set; }
        public string Prefix { get; set; }
        public string Suffix { get; set; }
        public int? Decimals { get; set; }
        public bool UpperCase { get; set; }
        public bool Annotative { get; set; }

        public ResolvedTextStyle()
        {
            Name = string.Empty;
            DrawingTextStyle = string.Empty;
            WidthFactor = 1.0;
            Color = string.Empty;
            MaskLayer = string.Empty;
            Prefix = string.Empty;
            Suffix = string.Empty;
        }
    }

    /// <summary>The leader look one label gets. Null values: the multileader style decides.</summary>
    public sealed class ResolvedLeaderStyle
    {
        public string Name { get; set; }
        /// <summary>Empty: the drawing's current multileader style.</summary>
        public string MLeaderStyle { get; set; }
        /// <summary>Empty: the multileader style's arrow.</summary>
        public string ArrowBlock { get; set; }
        /// <summary>False: the multileader style sizes the arrow.</summary>
        public bool SizeArrow { get; set; }
        public double ArrowSizePlotted { get; set; }
        public LeaderLineKind? LineType { get; set; }
        public string Color { get; set; }
        public string Linetype { get; set; }
        public string Lineweight { get; set; }
        public bool? Landing { get; set; }
        public double? DoglegPlotted { get; set; }
        public double? LandingGapPlotted { get; set; }
        public LeaderAttachment Attachment { get; set; }
        /// <summary>0: no limit beyond the label search rings.</summary>
        public double MaxLengthPlotted { get; set; }
        public bool Annotative { get; set; }

        public ResolvedLeaderStyle()
        {
            Name = string.Empty;
            MLeaderStyle = string.Empty;
            ArrowBlock = string.Empty;
            Attachment = LeaderAttachment.MiddleOfText;
        }
    }

    /// <summary>
    /// Everything that decides how one label, tag or line label is drawn, with where each
    /// value came from so the settings window can show it ("Text height 0.10 -- text style
    /// Trees, assigned to family Trees").
    /// </summary>
    public sealed class ResolvedAnnotationStyle
    {
        public StyledKind Kind { get; set; }
        public ResolvedTextStyle Text { get; set; }
        public ResolvedLeaderStyle Leader { get; set; }
        /// <summary>The layer before Settings > Layers maps it.</summary>
        public string Layer { get; set; }
        public double OffsetPlotted { get; set; }
        public LeaderMode LeaderMode { get; set; }
        public bool Enabled { get; set; }
        /// <summary>Setting name to where its value came from, in the order they were set.</summary>
        public IList<KeyValuePair<string, string>> Origins { get; private set; }
        /// <summary>Named styles an assignment asks for that do not exist. Reported, never
        /// quietly replaced.</summary>
        public IList<string> Problems { get; private set; }

        public ResolvedAnnotationStyle()
        {
            Text = new ResolvedTextStyle();
            Leader = new ResolvedLeaderStyle();
            Layer = string.Empty;
            Enabled = true;
            Origins = new List<KeyValuePair<string, string>>();
            Problems = new List<string>();
        }

        public string OriginOf(string setting)
        {
            for (var i = Origins.Count - 1; i >= 0; i--)
                if (string.Equals(Origins[i].Key, setting, StringComparison.OrdinalIgnoreCase)) return Origins[i].Value;
            return null;
        }

        internal void From(string setting, string origin)
        {
            for (var i = Origins.Count - 1; i >= 0; i--)
                if (string.Equals(Origins[i].Key, setting, StringComparison.OrdinalIgnoreCase)) Origins.RemoveAt(i);
            Origins.Add(new KeyValuePair<string, string>(setting, origin));
        }
    }

    /// <summary>What a style is being resolved for.</summary>
    public sealed class StyleSubject
    {
        public StyledKind Kind { get; set; }
        public AnnotationCategory Category { get; set; }
        public string RuleId { get; set; }
        public string Code { get; set; }
        /// <summary>The layer the rule (or the tag / line label settings) puts it on.</summary>
        public string BaseLayer { get; set; }
        /// <summary>The rule's leader mode.</summary>
        public LeaderMode BaseLeader { get; set; }

        public static StyleSubject For(StyledKind kind, ParsedPoint p, RulesConfig rules)
        {
            return new StyleSubject
            {
                Kind = kind,
                Category = kind == StyledKind.LineLabel ? AnnotationCategory.Lines : AnnotationPolicy.CategoryOf(p, rules),
                RuleId = p != null ? p.RuleId : null,
                Code = p != null ? (p.Code ?? FieldCodeParser.FirstToken(p.RawDescription ?? string.Empty)) : null,
                BaseLayer = p != null ? p.LabelLayer : null,
                BaseLeader = p != null ? p.Leader : LeaderMode.Auto
            };
        }
    }

    /// <summary>
    /// Settles the look of a label, tag or line label from one place. Start from the values
    /// the existing settings pages have always held (Point Labels, Tags, Line Labels), then
    /// apply the style assignments from the least specific (every feature) to the most
    /// specific (one field code). A named style only changes what it sets.
    ///
    /// With no styles and no assignments defined the result is exactly what FTF drew before
    /// styles existed, so an office's drawings do not change until it chooses.
    /// </summary>
    public static class StyleResolver
    {
        public const string PointLabelsPage = "Point Labels";
        public const string TagsPage = "Tags";
        public const string LineLabelsPage = "Line Labels";
        public const string RuleOrigin = "the feature's rule";
        public const string MultileaderOrigin = "the multileader style";
        public const string FtfDefault = "FTF default";

        public static ResolvedAnnotationStyle Resolve(FtfSettings settings, StyleSubject subject)
        {
            var s = settings ?? new FtfSettings();
            var styles = s.Styles ?? new StyleSettings();
            styles.FillMissing();
            var r = new ResolvedAnnotationStyle { Kind = subject.Kind };

            Defaults(s, subject, r);

            var matches = Matching(styles, subject).ToList();
            string textName = null, leaderName = null, textFrom = null, leaderFrom = null;
            foreach (var a in matches)
            {
                var where = "assignment for " + a.Scope;
                if (!string.IsNullOrWhiteSpace(a.TextStyle)) { textName = a.TextStyle.Trim(); textFrom = where; }
                if (!string.IsNullOrWhiteSpace(a.LeaderStyle)) { leaderName = a.LeaderStyle.Trim(); leaderFrom = where; }
                if (!string.IsNullOrWhiteSpace(a.Layer)) { r.Layer = a.Layer.Trim(); r.From("Layer", where); }
                if (a.OffsetPlotted.HasValue) { r.OffsetPlotted = a.OffsetPlotted.Value; r.From("Offset", where); }
                if (a.Leader.HasValue && subject.Kind != StyledKind.LineLabel) { r.LeaderMode = a.Leader.Value; r.From("Leader", where); }
                if (a.Enabled.HasValue) { r.Enabled = a.Enabled.Value; r.From("Enabled", where); }
            }

            if (textName != null)
            {
                var def = styles.FindText(textName);
                if (def == null)
                    r.Problems.Add("The " + textFrom + " names text style \"" + textName + "\", which is not defined " +
                                   "(Settings > Text Styles). The " + DefaultPage(subject.Kind) + " values were used.");
                else
                    ApplyText(def, r, "text style \"" + def.Name.Trim() + "\" (" + textFrom + ")");
            }

            if (leaderName != null && subject.Kind != StyledKind.LineLabel)
            {
                var def = styles.FindLeader(leaderName);
                if (def == null)
                    r.Problems.Add("The " + leaderFrom + " names leader style \"" + leaderName + "\", which is not defined " +
                                   "(Settings > Leader Styles). The drawing's multileader style was used.");
                else
                    ApplyLeader(def, r, "leader style \"" + def.Name.Trim() + "\" (" + leaderFrom + ")");
            }
            return r;
        }

        /// <summary>The assignments that apply, least specific first.</summary>
        public static IEnumerable<StyleAssignment> Matching(StyleSettings styles, StyleSubject subject)
        {
            if (styles == null || styles.Assignments == null) return Enumerable.Empty<StyleAssignment>();
            return styles.Assignments
                .Where(a => a != null && a.Kind == subject.Kind && Applies(a, subject))
                .Select((a, i) => new { a, i })
                .OrderBy(x => x.a.Level).ThenBy(x => x.i)
                .Select(x => x.a);
        }

        private static bool Applies(StyleAssignment a, StyleSubject subject)
        {
            if (!string.IsNullOrWhiteSpace(a.Category))
            {
                var c = TryCategory(a.Category);
                if (!c.HasValue || c.Value != subject.Category) return false;
            }
            if (!string.IsNullOrWhiteSpace(a.Rule) &&
                !string.Equals(a.Rule.Trim(), subject.RuleId ?? string.Empty, StringComparison.OrdinalIgnoreCase)) return false;
            if (!string.IsNullOrWhiteSpace(a.Code) &&
                !string.Equals(a.Code.Trim(), (subject.Code ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        /// <summary>A family from its name as stored or as shown ("Storm and sewer structures").</summary>
        public static AnnotationCategory? TryCategory(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            AnnotationCategory c;
            if (Enum.TryParse(name.Trim(), true, out c) && Enum.IsDefined(typeof(AnnotationCategory), c)) return c;
            var squeezed = new string(name.Where(char.IsLetter).ToArray());
            if (Enum.TryParse(squeezed, true, out c) && Enum.IsDefined(typeof(AnnotationCategory), c)) return c;
            foreach (AnnotationCategory each in Enum.GetValues(typeof(AnnotationCategory)))
                if (string.Equals(AnnotationPolicy.Name(each), name.Trim(), StringComparison.OrdinalIgnoreCase)) return each;
            return null;
        }

        public static string DefaultPage(StyledKind kind)
        {
            return kind == StyledKind.Tag ? TagsPage : kind == StyledKind.LineLabel ? LineLabelsPage : PointLabelsPage;
        }

        private static void Defaults(FtfSettings s, StyleSubject subject, ResolvedAnnotationStyle r)
        {
            var page = DefaultPage(subject.Kind);
            var lp = s.Labels ?? new LabelSettings();
            var t = r.Text;
            t.Name = page + " (no style)";
            switch (subject.Kind)
            {
                case StyledKind.Tag:
                    var ts = s.Tags ?? new TagSettings();
                    t.DrawingTextStyle = ts.TagTextStyle ?? string.Empty;
                    t.HeightPlotted = ts.TagTextHeightPlotted;
                    t.Mask = MaskMode.None;
                    r.OffsetPlotted = ts.TagOffsetPlotted;
                    r.Layer = string.IsNullOrWhiteSpace(ts.TagLayer) ? (subject.BaseLayer ?? string.Empty) : ts.TagLayer;
                    r.From("Layer", string.IsNullOrWhiteSpace(ts.TagLayer) ? RuleOrigin : page);
                    r.LeaderMode = ts.TagLeader ? LeaderMode.Auto : LeaderMode.Never;
                    r.From("Leader", page);
                    break;
                case StyledKind.LineLabel:
                    var ll = s.LineLabels ?? new LineLabelSettings();
                    t.DrawingTextStyle = ll.TextStyle ?? string.Empty;
                    t.HeightPlotted = ll.TextHeightPlotted;
                    t.Mask = ll.DrawMask ? MaskMode.Wipeout : MaskMode.None;
                    r.OffsetPlotted = 0;
                    r.Layer = subject.BaseLayer ?? string.Empty;
                    r.From("Layer", RuleOrigin);
                    r.LeaderMode = LeaderMode.Never;
                    break;
                default:
                    t.DrawingTextStyle = lp.TextStyle ?? string.Empty;
                    t.HeightPlotted = lp.TextHeightPlotted;
                    t.Mask = lp.DrawMask ? MaskMode.Wipeout : MaskMode.None;
                    t.MaskLayer = lp.MaskLayer ?? string.Empty;
                    r.OffsetPlotted = lp.BaseOffsetPlotted;
                    r.Layer = subject.BaseLayer ?? string.Empty;
                    r.From("Layer", RuleOrigin);
                    r.LeaderMode = subject.BaseLeader;
                    r.From("Leader", RuleOrigin);
                    break;
            }
            foreach (var name in TextSettingNames) r.From(name, FtfDefault);
            r.From("Text style", page);
            r.From("Text height", page);
            if (subject.Kind != StyledKind.Tag) r.From("Mask", page);
            r.From("Offset", subject.Kind == StyledKind.LineLabel ? FtfDefault : page);
            r.From("Enabled", subject.Kind == StyledKind.Tag ? "Settings > Tags / Label All" : "Settings > Label All");

            var leader = r.Leader;
            leader.Name = "Multileader style (no style)";
            leader.SizeArrow = lp.SizeLeaderArrows;
            leader.ArrowSizePlotted = lp.LeaderArrowSizePlotted;
            foreach (var name in LeaderSettingNames) r.From(name, MultileaderOrigin);
            if (lp.SizeLeaderArrows) r.From("Arrow size", PointLabelsPage);
            r.From("Attachment", FtfDefault + " (middle of text)");
        }

        public static readonly string[] TextSettingNames =
        {
            "Text style", "Text height", "Width factor", "Oblique", "Rotation", "Justification", "Text colour",
            "Mask", "Prefix", "Suffix", "Decimals", "Upper case", "Annotative text"
        };

        public static readonly string[] LeaderSettingNames =
        {
            "Multileader style", "Arrowhead", "Arrow size", "Leader line", "Leader colour", "Leader linetype",
            "Leader lineweight", "Landing", "Landing length", "Landing gap", "Attachment", "Maximum length", "Annotative leader"
        };

        private static void ApplyText(TextStyleDef d, ResolvedAnnotationStyle r, string origin)
        {
            var t = r.Text;
            t.Name = d.Name.Trim();
            if (!string.IsNullOrWhiteSpace(d.TextStyle)) { t.DrawingTextStyle = d.TextStyle.Trim(); r.From("Text style", origin); }
            if (d.HeightPlotted.HasValue && d.HeightPlotted.Value > 0) { t.HeightPlotted = d.HeightPlotted.Value; r.From("Text height", origin); }
            if (d.WidthFactor.HasValue && d.WidthFactor.Value > 0) { t.WidthFactor = d.WidthFactor.Value; r.From("Width factor", origin); }
            if (d.ObliqueDegrees.HasValue) { t.ObliqueDegrees = d.ObliqueDegrees.Value; r.From("Oblique", origin); }
            if (d.Rotation.HasValue)
            {
                t.Rotation = d.Rotation.Value;
                t.RotationDegrees = d.RotationDegrees ?? 0;
                r.From("Rotation", origin);
            }
            if (d.Justification.HasValue) { t.Justification = d.Justification.Value; r.From("Justification", origin); }
            if (!string.IsNullOrWhiteSpace(d.Color)) { t.Color = d.Color.Trim(); r.From("Text colour", origin); }
            if (d.Mask.HasValue) { t.Mask = d.Mask.Value; r.From("Mask", origin); }
            if (!string.IsNullOrWhiteSpace(d.MaskLayer)) t.MaskLayer = d.MaskLayer.Trim();
            if (!string.IsNullOrEmpty(d.Prefix)) { t.Prefix = d.Prefix; r.From("Prefix", origin); }
            if (!string.IsNullOrEmpty(d.Suffix)) { t.Suffix = d.Suffix; r.From("Suffix", origin); }
            if (d.Decimals.HasValue) { t.Decimals = Math.Max(0, Math.Min(8, d.Decimals.Value)); r.From("Decimals", origin); }
            if (d.UpperCase.HasValue) { t.UpperCase = d.UpperCase.Value; r.From("Upper case", origin); }
            if (d.Annotative.HasValue) { t.Annotative = d.Annotative.Value; r.From("Annotative text", origin); }
        }

        private static void ApplyLeader(LeaderStyleDef d, ResolvedAnnotationStyle r, string origin)
        {
            var l = r.Leader;
            l.Name = d.Name.Trim();
            if (!string.IsNullOrWhiteSpace(d.MLeaderStyle)) { l.MLeaderStyle = d.MLeaderStyle.Trim(); r.From("Multileader style", origin); }
            if (!string.IsNullOrWhiteSpace(d.ArrowBlock)) { l.ArrowBlock = d.ArrowBlock.Trim(); r.From("Arrowhead", origin); }
            if (d.ArrowSizePlotted.HasValue)
            {
                // 0 hands the arrow back to the multileader style.
                l.SizeArrow = d.ArrowSizePlotted.Value > 0;
                l.ArrowSizePlotted = d.ArrowSizePlotted.Value;
                r.From("Arrow size", d.ArrowSizePlotted.Value > 0 ? origin : MultileaderOrigin);
            }
            if (d.LineType.HasValue) { l.LineType = d.LineType.Value; r.From("Leader line", origin); }
            if (!string.IsNullOrWhiteSpace(d.Color)) { l.Color = d.Color.Trim(); r.From("Leader colour", origin); }
            if (!string.IsNullOrWhiteSpace(d.Linetype)) { l.Linetype = d.Linetype.Trim(); r.From("Leader linetype", origin); }
            if (!string.IsNullOrWhiteSpace(d.Lineweight)) { l.Lineweight = d.Lineweight.Trim(); r.From("Leader lineweight", origin); }
            if (d.Landing.HasValue) { l.Landing = d.Landing.Value; r.From("Landing", origin); }
            if (d.DoglegPlotted.HasValue) { l.DoglegPlotted = d.DoglegPlotted.Value; r.From("Landing length", origin); }
            if (d.LandingGapPlotted.HasValue) { l.LandingGapPlotted = d.LandingGapPlotted.Value; r.From("Landing gap", origin); }
            if (d.Attachment.HasValue) { l.Attachment = d.Attachment.Value; r.From("Attachment", origin); }
            if (d.MaxLengthPlotted.HasValue && d.MaxLengthPlotted.Value > 0) { l.MaxLengthPlotted = d.MaxLengthPlotted.Value; r.From("Maximum length", origin); }
            if (d.Annotative.HasValue) { l.Annotative = d.Annotative.Value; r.From("Annotative leader", origin); }
        }

        /// <summary>
        /// The number of label search rings that keep a label within the leader style's
        /// maximum length: ring 0 is the offset, each later ring one step farther out. At least
        /// one ring is always tried.
        /// </summary>
        public static int RingsWithin(double maxLengthPlotted, double offsetPlotted, double ringStepPlotted, int ringCount)
        {
            var rings = Math.Max(1, ringCount);
            if (!(maxLengthPlotted > 0) || !(ringStepPlotted > 0)) return rings;
            var fit = (int)Math.Floor((maxLengthPlotted - offsetPlotted) / ringStepPlotted + 1e-9) + 1;
            return Math.Max(1, Math.Min(rings, fit));
        }
    }
}
