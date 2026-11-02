using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FieldCodes.Settings;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace FieldCodes.Styles
{
    /// <summary>What sits behind label text so linework does not run through it.</summary>
    public enum MaskMode
    {
        /// <summary>A separate wipeout under the text, kept with it by FTF (the long-standing
        /// FTF behaviour; Settings > Point Labels > Mask).</summary>
        Wipeout = 0,
        /// <summary>The text's own background mask (MTEXT/MLEADER "Background mask"): no
        /// extra object, follows the text when it is moved.</summary>
        Background = 1,
        None = 2
    }

    /// <summary>How label text is turned.</summary>
    public enum TextRotation
    {
        /// <summary>Level, reading left to right.</summary>
        Horizontal = 0,
        /// <summary>A fixed angle (degrees counter-clockwise from east, AutoCAD's way).</summary>
        Fixed = 1,
        /// <summary>The symbol's own rotation (FTFORIENT or the rule's rotation field), turned
        /// so it never reads upside down.</summary>
        FollowSymbol = 2
    }

    /// <summary>Which edge of the text lines up. Auto: the side the leader comes from, so the
    /// lines of a callout line up against the landing (the long-standing FTF behaviour).</summary>
    public enum TextJustification
    {
        Auto = 0,
        Left = 1,
        Center = 2,
        Right = 3
    }

    /// <summary>AutoCAD's leader line kinds, by the names its Properties palette uses.</summary>
    public enum LeaderLineKind
    {
        Straight = 0,
        Spline = 1
    }

    /// <summary>Where a leader's landing meets multi-line text: AutoCAD's own choices
    /// (MLEADERSTYLE > Content > Leader connection), named as it names them.</summary>
    public enum LeaderAttachment
    {
        MiddleOfText = 0,
        TopOfTopLine = 1,
        MiddleOfTopLine = 2,
        BottomOfTopLine = 3,
        UnderlineTopLine = 4,
        MiddleOfBottomLine = 5,
        BottomOfBottomLine = 6,
        UnderlineBottomLine = 7,
        UnderlineAllText = 8
    }

    /// <summary>What a style assignment styles.</summary>
    public enum StyledKind
    {
        /// <summary>Point labels (FTFLABELS, Label All, Process Drawing).</summary>
        Label = 0,
        /// <summary>Numbered tags (FTFTAGS).</summary>
        Tag = 1,
        /// <summary>Linework labels (FTFLINELABELS): text only, they have no leaders.</summary>
        LineLabel = 2
    }

    /// <summary>
    /// A named text look for FTF annotation. Every field is optional: one left empty takes
    /// the value from where it has always come from (Settings > Point Labels for labels,
    /// Settings > Tags for tags, Settings > Line Labels for line labels). So a style only
    /// says what makes it different, and an office that never defines one keeps today's
    /// output exactly.
    /// </summary>
    public sealed class TextStyleDef
    {
        [JsonProperty("name")] public string Name { get; set; }

        /// <summary>A text style in the drawing (STYLE). Empty: inherited.</summary>
        [JsonProperty("textStyle", NullValueHandling = NullValueHandling.Ignore)] public string TextStyle { get; set; }

        /// <summary>Text height as plotted (inches on the sheet).</summary>
        [JsonProperty("heightPlotted", NullValueHandling = NullValueHandling.Ignore)] public double? HeightPlotted { get; set; }

        /// <summary>1.0 is the text style's own width; 0.8 is narrower.</summary>
        [JsonProperty("widthFactor", NullValueHandling = NullValueHandling.Ignore)] public double? WidthFactor { get; set; }

        [JsonProperty("obliqueDegrees", NullValueHandling = NullValueHandling.Ignore)] public double? ObliqueDegrees { get; set; }

        [JsonProperty("rotation", NullValueHandling = NullValueHandling.Ignore)]
        [JsonConverter(typeof(StringEnumConverter))]
        public TextRotation? Rotation { get; set; }

        /// <summary>The angle for <see cref="TextRotation.Fixed"/>.</summary>
        [JsonProperty("rotationDegrees", NullValueHandling = NullValueHandling.Ignore)] public double? RotationDegrees { get; set; }

        [JsonProperty("justification", NullValueHandling = NullValueHandling.Ignore)]
        [JsonConverter(typeof(StringEnumConverter))]
        public TextJustification? Justification { get; set; }

        /// <summary>ACI number or colour name ("red", "7"); "ByLayer" for ByLayer.</summary>
        [JsonProperty("color", NullValueHandling = NullValueHandling.Ignore)] public string Color { get; set; }

        [JsonProperty("mask", NullValueHandling = NullValueHandling.Ignore)]
        [JsonConverter(typeof(StringEnumConverter))]
        public MaskMode? Mask { get; set; }

        /// <summary>Layer for a wipeout mask. Empty: the label's own layer.</summary>
        [JsonProperty("maskLayer", NullValueHandling = NullValueHandling.Ignore)] public string MaskLayer { get; set; }

        /// <summary>Put in front of the label text (MTEXT codes allowed: \P starts a line).</summary>
        [JsonProperty("prefix", NullValueHandling = NullValueHandling.Ignore)] public string Prefix { get; set; }

        [JsonProperty("suffix", NullValueHandling = NullValueHandling.Ignore)] public string Suffix { get; set; }

        /// <summary>Rounds numbers written with a decimal point (6.418 to 6.42). Whole numbers --
        /// pole, sign and tag numbers -- are never changed. Empty: as written.</summary>
        [JsonProperty("decimals", NullValueHandling = NullValueHandling.Ignore)] public int? Decimals { get; set; }

        /// <summary>Upper-case the label text.</summary>
        [JsonProperty("upperCase", NullValueHandling = NullValueHandling.Ignore)] public bool? UpperCase { get; set; }

        /// <summary>Annotative text: AutoCAD sizes it per annotation scale and the height is the
        /// plotted height. Off: sized once for the drawing's scale.</summary>
        [JsonProperty("annotative", NullValueHandling = NullValueHandling.Ignore)] public bool? Annotative { get; set; }

        public TextStyleDef Clone() { return (TextStyleDef)MemberwiseClone(); }
    }

    /// <summary>
    /// A named leader look. Every field is optional: empty means the drawing's multileader
    /// style decides, which is how FTF leaders have always looked. FTF leaders are AutoCAD
    /// multileaders, so each field is a real MLEADER property.
    /// </summary>
    public sealed class LeaderStyleDef
    {
        [JsonProperty("name")] public string Name { get; set; }

        /// <summary>A multileader style in the drawing (MLEADERSTYLE). Empty: the current one.</summary>
        [JsonProperty("mleaderStyle", NullValueHandling = NullValueHandling.Ignore)] public string MLeaderStyle { get; set; }

        /// <summary>Arrowhead block: an AutoCAD arrow name ("_Dot", "_DotSmall", "_Open30",
        /// "_None", "_ArchTick") or a block in the drawing. Empty: the style's arrow.</summary>
        [JsonProperty("arrowBlock", NullValueHandling = NullValueHandling.Ignore)] public string ArrowBlock { get; set; }

        /// <summary>Arrow size as plotted. Empty: Settings > Point Labels > Arrow size.</summary>
        [JsonProperty("arrowSizePlotted", NullValueHandling = NullValueHandling.Ignore)] public double? ArrowSizePlotted { get; set; }

        [JsonProperty("lineType", NullValueHandling = NullValueHandling.Ignore)]
        [JsonConverter(typeof(StringEnumConverter))]
        public LeaderLineKind? LineType { get; set; }

        /// <summary>Leader line colour (ACI number or name, "ByLayer", "ByBlock").</summary>
        [JsonProperty("color", NullValueHandling = NullValueHandling.Ignore)] public string Color { get; set; }

        /// <summary>Leader linetype loaded in the drawing, or ByLayer / ByBlock.</summary>
        [JsonProperty("linetype", NullValueHandling = NullValueHandling.Ignore)] public string Linetype { get; set; }

        /// <summary>Leader lineweight in millimetres ("0.25"), or ByLayer / ByBlock.</summary>
        [JsonProperty("lineweight", NullValueHandling = NullValueHandling.Ignore)] public string Lineweight { get; set; }

        /// <summary>Draw the horizontal landing (dogleg) between leader and text.</summary>
        [JsonProperty("landing", NullValueHandling = NullValueHandling.Ignore)] public bool? Landing { get; set; }

        [JsonProperty("doglegPlotted", NullValueHandling = NullValueHandling.Ignore)] public double? DoglegPlotted { get; set; }

        /// <summary>Gap between the landing and the text, as plotted.</summary>
        [JsonProperty("landingGapPlotted", NullValueHandling = NullValueHandling.Ignore)] public double? LandingGapPlotted { get; set; }

        [JsonProperty("attachment", NullValueHandling = NullValueHandling.Ignore)]
        [JsonConverter(typeof(StringEnumConverter))]
        public LeaderAttachment? Attachment { get; set; }

        /// <summary>The farthest a label may sit from its point, as plotted. Positions farther
        /// out are not tried. Empty or 0: Settings > Point Labels' search rings decide.</summary>
        [JsonProperty("maxLengthPlotted", NullValueHandling = NullValueHandling.Ignore)] public double? MaxLengthPlotted { get; set; }

        /// <summary>Annotative leader: sized per annotation scale.</summary>
        [JsonProperty("annotative", NullValueHandling = NullValueHandling.Ignore)] public bool? Annotative { get; set; }

        public LeaderStyleDef Clone() { return (LeaderStyleDef)MemberwiseClone(); }
    }

    /// <summary>
    /// Which styles a kind of annotation uses: for every feature (no family, no code), for a
    /// family (Trees, Utilities...), for one rule, or for one field code. The most specific
    /// match wins field by field: a code's assignment can change only the leader and keep
    /// its family's text style.
    /// </summary>
    public sealed class StyleAssignment
    {
        [JsonProperty("kind")]
        [JsonConverter(typeof(StringEnumConverter))]
        public StyledKind Kind { get; set; }

        /// <summary>A family name (Trees, Utilities, StormSewer, Signs, Control, Pavement,
        /// OtherPoints, Lines). Empty: any.</summary>
        [JsonProperty("category", NullValueHandling = NullValueHandling.Ignore)] public string Category { get; set; }

        /// <summary>A code rule's id (Point Features). Empty: any.</summary>
        [JsonProperty("rule", NullValueHandling = NullValueHandling.Ignore)] public string Rule { get; set; }

        /// <summary>One field code as the crew types it (FH). Empty: any.</summary>
        [JsonProperty("code", NullValueHandling = NullValueHandling.Ignore)] public string Code { get; set; }

        [JsonProperty("textStyle", NullValueHandling = NullValueHandling.Ignore)] public string TextStyle { get; set; }
        [JsonProperty("leaderStyle", NullValueHandling = NullValueHandling.Ignore)] public string LeaderStyle { get; set; }

        /// <summary>Layer for the label (before Settings > Layers maps it). Empty: the rule's.</summary>
        [JsonProperty("layer", NullValueHandling = NullValueHandling.Ignore)] public string Layer { get; set; }

        /// <summary>Distance from the point to the nearest label position, as plotted.</summary>
        [JsonProperty("offsetPlotted", NullValueHandling = NullValueHandling.Ignore)] public double? OffsetPlotted { get; set; }

        /// <summary>Leader use: Auto (only when the label had to move away), Always, Never.</summary>
        [JsonProperty("leader", NullValueHandling = NullValueHandling.Ignore)]
        [JsonConverter(typeof(StringEnumConverter))]
        public LeaderMode? Leader { get; set; }

        /// <summary>False: this feature is not labelled (or tagged) at all.</summary>
        [JsonProperty("enabled", NullValueHandling = NullValueHandling.Ignore)] public bool? Enabled { get; set; }

        /// <summary>0 any, 1 family, 2 rule, 3 code: how specific this assignment is.</summary>
        [JsonIgnore]
        public int Level
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(Code)) return 3;
                if (!string.IsNullOrWhiteSpace(Rule)) return 2;
                if (!string.IsNullOrWhiteSpace(Category)) return 1;
                return 0;
            }
        }

        /// <summary>"all features", "family Trees", "rule fire-hydrant", "code FH".</summary>
        [JsonIgnore]
        public string Scope
        {
            get
            {
                switch (Level)
                {
                    case 3: return "code " + Code.Trim();
                    case 2: return "rule " + Rule.Trim();
                    case 1: return "family " + Category.Trim();
                    default: return "all features";
                }
            }
        }

        public bool IsEmpty
        {
            get
            {
                return string.IsNullOrWhiteSpace(TextStyle) && string.IsNullOrWhiteSpace(LeaderStyle) &&
                       string.IsNullOrWhiteSpace(Layer) && !OffsetPlotted.HasValue && !Leader.HasValue && !Enabled.HasValue;
            }
        }

        public StyleAssignment Clone() { return (StyleAssignment)MemberwiseClone(); }
    }

    /// <summary>
    /// Settings > Text Styles, Leader Styles and Style Assignments: the office's named looks
    /// for FTF annotation and which features use them. Read by FTFLABELS, FTFTAGS,
    /// FTFLINELABELS and FTFRESTYLE through <see cref="StyleResolver"/>.
    /// </summary>
    public sealed class StyleSettings : ISettingsSection
    {
        public string Title { get { return "Annotation Styles"; } }
        public string AffectedCommands { get { return "FTFLABELS, FTFLABELALL, FTFTAGS, FTFLINELABELS, FTFRESTYLE, FTFRUN"; } }

        [JsonProperty("text", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<TextStyleDef> Text { get; set; }

        [JsonProperty("leaders", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<LeaderStyleDef> Leaders { get; set; }

        [JsonProperty("assignments", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<StyleAssignment> Assignments { get; set; }

        public StyleSettings() { RestoreDefaults(); }

        public void RestoreDefaults()
        {
            Text = new List<TextStyleDef>();
            Leaders = new List<LeaderStyleDef>();
            Assignments = new List<StyleAssignment>();
        }

        public void FillMissing()
        {
            if (Text == null) Text = new List<TextStyleDef>();
            if (Leaders == null) Leaders = new List<LeaderStyleDef>();
            if (Assignments == null) Assignments = new List<StyleAssignment>();
            Text.RemoveAll(t => t == null);
            Leaders.RemoveAll(l => l == null);
            Assignments.RemoveAll(a => a == null);
        }

        public TextStyleDef FindText(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || Text == null) return null;
            return Text.FirstOrDefault(t => t != null && string.Equals((t.Name ?? string.Empty).Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        public LeaderStyleDef FindLeader(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || Leaders == null) return null;
            return Leaders.FirstOrDefault(l => l != null && string.Equals((l.Name ?? string.Empty).Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>The assignment for exactly this kind and scope, or null.</summary>
        public StyleAssignment FindAssignment(StyledKind kind, string category, string rule, string code)
        {
            if (Assignments == null) return null;
            return Assignments.FirstOrDefault(a => a != null && a.Kind == kind &&
                                                   Same(a.Category, category) && Same(a.Rule, rule) && Same(a.Code, code));
        }

        /// <summary>The assignment for exactly this kind and scope, added if there is none.</summary>
        public StyleAssignment AssignmentFor(StyledKind kind, string category, string rule, string code)
        {
            FillMissing();
            var found = FindAssignment(kind, category, rule, code);
            if (found != null) return found;
            found = new StyleAssignment
            {
                Kind = kind,
                Category = Blank(category),
                Rule = Blank(rule),
                Code = Blank(code)
            };
            Assignments.Add(found);
            return found;
        }

        /// <summary>Drops assignments that say nothing (every field empty).</summary>
        public void Prune()
        {
            FillMissing();
            Assignments.RemoveAll(a => a.IsEmpty);
        }

        public void Validate(ICollection<string> problems)
        {
            FillMissing();
            Duplicates(Text.Select(t => t.Name), "text style", problems);
            Duplicates(Leaders.Select(l => l.Name), "leader style", problems);

            foreach (var t in Text)
            {
                var who = "Text style \"" + (t.Name ?? string.Empty) + "\"";
                if (string.IsNullOrWhiteSpace(t.Name)) problems.Add("A text style has no name.");
                if (t.HeightPlotted.HasValue && !(t.HeightPlotted.Value > 0)) problems.Add(who + ": height must be greater than zero.");
                if (t.WidthFactor.HasValue && !(t.WidthFactor.Value > 0.01 && t.WidthFactor.Value <= 100)) problems.Add(who + ": width factor must be between 0.01 and 100.");
                if (t.ObliqueDegrees.HasValue && Math.Abs(t.ObliqueDegrees.Value) > 85) problems.Add(who + ": oblique angle must be within 85 degrees of upright.");
                if (t.Decimals.HasValue && (t.Decimals.Value < 0 || t.Decimals.Value > 8)) problems.Add(who + ": decimal places must be 0 to 8.");
                if (!string.IsNullOrWhiteSpace(t.Color) && !StyleColor.IsValid(t.Color)) problems.Add(who + ": colour \"" + t.Color + "\" is not an ACI number, colour name, ByLayer or ByBlock.");
                if (t.Rotation == TextRotation.Fixed && !t.RotationDegrees.HasValue) problems.Add(who + ": a fixed rotation needs an angle.");
            }

            foreach (var l in Leaders)
            {
                var who = "Leader style \"" + (l.Name ?? string.Empty) + "\"";
                if (string.IsNullOrWhiteSpace(l.Name)) problems.Add("A leader style has no name.");
                if (l.ArrowSizePlotted.HasValue && l.ArrowSizePlotted.Value < 0) problems.Add(who + ": arrow size cannot be negative.");
                if (l.DoglegPlotted.HasValue && l.DoglegPlotted.Value < 0) problems.Add(who + ": landing length cannot be negative.");
                if (l.LandingGapPlotted.HasValue && l.LandingGapPlotted.Value < 0) problems.Add(who + ": landing gap cannot be negative.");
                if (l.MaxLengthPlotted.HasValue && l.MaxLengthPlotted.Value < 0) problems.Add(who + ": maximum leader length cannot be negative.");
                if (!string.IsNullOrWhiteSpace(l.Color) && !StyleColor.IsValid(l.Color)) problems.Add(who + ": colour \"" + l.Color + "\" is not an ACI number, colour name, ByLayer or ByBlock.");
                if (!string.IsNullOrWhiteSpace(l.Lineweight) && !LayerAppearance.LineWeightHundredths(l.Lineweight).HasValue) problems.Add(who + ": lineweight \"" + l.Lineweight + "\" is not a lineweight in millimetres, ByLayer or ByBlock.");
            }

            foreach (var a in Assignments)
            {
                var who = "Style assignment (" + a.Kind + ", " + a.Scope + ")";
                if (!string.IsNullOrWhiteSpace(a.Category) && !StyleResolver.TryCategory(a.Category).HasValue)
                    problems.Add(who + ": \"" + a.Category + "\" is not a feature family.");
                if (!string.IsNullOrWhiteSpace(a.TextStyle) && FindText(a.TextStyle) == null)
                    problems.Add(who + ": text style \"" + a.TextStyle + "\" is not defined.");
                if (!string.IsNullOrWhiteSpace(a.LeaderStyle) && FindLeader(a.LeaderStyle) == null)
                    problems.Add(who + ": leader style \"" + a.LeaderStyle + "\" is not defined.");
                if (a.Kind == StyledKind.LineLabel && !string.IsNullOrWhiteSpace(a.LeaderStyle))
                    problems.Add(who + ": line labels have no leaders; the leader style is not used.");
                if (a.OffsetPlotted.HasValue && a.OffsetPlotted.Value < 0)
                    problems.Add(who + ": offset cannot be negative.");
                string layerProblem;
                if (!string.IsNullOrWhiteSpace(a.Layer) && !LayerMap.IsValidName(a.Layer.Trim(), out layerProblem))
                    problems.Add(who + ": \"" + a.Layer + "\": " + layerProblem + ".");
            }
        }

        private static void Duplicates(IEnumerable<string> names, string what, ICollection<string> problems)
        {
            foreach (var g in names.Where(n => !string.IsNullOrWhiteSpace(n))
                                   .GroupBy(n => n.Trim(), StringComparer.OrdinalIgnoreCase)
                                   .Where(g => g.Count() > 1))
                problems.Add("Two " + what + "s are named \"" + g.Key + "\".");
        }

        private static bool Same(string a, string b)
        {
            return string.Equals((a ?? string.Empty).Trim(), (b ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static string Blank(string s) { return string.IsNullOrWhiteSpace(s) ? null : s.Trim(); }
    }

    /// <summary>Colour settings as a drafter writes them.</summary>
    public static class StyleColor
    {
        public static bool IsByLayer(string s) { return string.Equals((s ?? string.Empty).Trim(), "ByLayer", StringComparison.OrdinalIgnoreCase); }
        public static bool IsByBlock(string s) { return string.Equals((s ?? string.Empty).Trim(), "ByBlock", StringComparison.OrdinalIgnoreCase); }

        public static bool IsValid(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return true;
            if (IsByLayer(s) || IsByBlock(s)) return true;
            var index = LayerAppearance.ColorIndex(s);
            return index.HasValue && index.Value >= 1 && index.Value <= 255;
        }

        /// <summary>The ACI index: 256 ByLayer, 0 ByBlock, 1..255 a colour; null when unset.</summary>
        public static short? Index(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            if (IsByLayer(s)) return 256;
            if (IsByBlock(s)) return 0;
            var index = LayerAppearance.ColorIndex(s);
            return index.HasValue && index.Value >= 1 && index.Value <= 255 ? index : null;
        }

        public static string Describe(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "ByLayer";
            return s.Trim();
        }
    }

    internal static class StyleFormat
    {
        public static string Number(double v) { return v.ToString("0.###", CultureInfo.InvariantCulture); }
    }
}
