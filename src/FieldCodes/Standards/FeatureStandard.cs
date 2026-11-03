using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using FieldCodes.Settings;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace FieldCodes.Standards
{
    /// <summary>What a surveyed point is, for accountability and the reports.</summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum PointClass
    {
        /// <summary>A topographic feature drafted on the plan (a mailbox, a post).</summary>
        Feature = 0,
        /// <summary>Surface data: ground shots for the TIN.</summary>
        Surface = 1,
        /// <summary>Survey control, a found monument, or a check shot on control.</summary>
        Control = 2,
        Sign = 3,
        /// <summary>A utility feature (hydrant, valve, pole, meter).</summary>
        Utility = 4,
        /// <summary>A storm or sewer structure.</summary>
        Structure = 5,
        Vegetation = 6,
        /// <summary>A field note shot: recorded for the drafter, nothing drafted.</summary>
        Note = 7
    }

    /// <summary>How the standard intends the point to end up on the plan.</summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum IntendedDisposition
    {
        /// <summary>What its code's rule draws (label, tag, block).</summary>
        Drafted = 0,
        /// <summary>Surface data only: no symbol, no label.</summary>
        SurfaceOnly = 1,
        /// <summary>A control check shot: accounted for and listed in the control check report.</summary>
        ControlCheck = 2,
        /// <summary>Its own symbol.</summary>
        Symbol = 3,
        /// <summary>Shown by its Civil 3D point style.</summary>
        PointStyle = 4,
        /// <summary>A text label.</summary>
        Label = 5,
        SymbolAndLabel = 6,
        /// <summary>Intentionally only the survey point.</summary>
        PointOnly = 7,
        /// <summary>A person looks at every one.</summary>
        Review = 8
    }

    /// <summary>What has to show the point as a symbol.</summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum SymbolRequirement
    {
        /// <summary>No symbol is required.</summary>
        None = 0,
        /// <summary>Its Civil 3D point style shows it: any visible marker (or one of the styles
        /// named in symbolStyle).</summary>
        PointStyle = 1,
        /// <summary>The feature's own symbol: a symbol block FTF placed, or a point style whose
        /// marker is the code's symbol block (or one named in symbolStyle). A generic marker
        /// is not enough.</summary>
        Specific = 2
    }

    /// <summary>What has to label the point.</summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum LabelRequirement
    {
        /// <summary>The label its code's rule writes, where Settings > Label All labels its family.</summary>
        AsPlanned = 0,
        /// <summary>No label is required, and FTF draws none.</summary>
        None = 1,
        /// <summary>A label FTF drew, or one typed by hand.</summary>
        Required = 2,
        /// <summary>The point's own Civil 3D label (its label style shows the description), or
        /// a label FTF drew, or one typed by hand.</summary>
        PointLabel = 3
    }

    /// <summary>What kind of sign a sign is, from its code and the words in its description.</summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum SignCategory
    {
        /// <summary>Not a sign, or a sign whose description says nothing about it.</summary>
        None = 0,
        Regulatory = 1,
        Warning = 2,
        StreetName = 3,
        /// <summary>An address or house number sign.</summary>
        Address = 4,
        /// <summary>A private or business sign.</summary>
        Private = 5,
        /// <summary>A sign described in words that name none of the other categories.</summary>
        Other = 6
    }

    /// <summary>The fields a requirement and its exceptions set. Null: inherited (from the
    /// family entry, then the default entry).</summary>
    public class RequirementFields
    {
        [JsonProperty("class", NullValueHandling = NullValueHandling.Ignore)] public PointClass? Classification { get; set; }
        [JsonProperty("disposition", NullValueHandling = NullValueHandling.Ignore)] public IntendedDisposition? Disposition { get; set; }
        [JsonProperty("symbol", NullValueHandling = NullValueHandling.Ignore)] public SymbolRequirement? Symbol { get; set; }
        [JsonProperty("label", NullValueHandling = NullValueHandling.Ignore)] public LabelRequirement? Label { get; set; }
        /// <summary>The point is surface data.</summary>
        [JsonProperty("surface", NullValueHandling = NullValueHandling.Ignore)] public bool? Surface { get; set; }
        /// <summary>Its symbol must be turned to face the way the feature does (FTFORIENT). When
        /// false, it is turned only by a direction the crew recorded, never by an assumption.</summary>
        [JsonProperty("rotation", NullValueHandling = NullValueHandling.Ignore)] public bool? Rotation { get; set; }
        /// <summary>Every such point is for a person to review.</summary>
        [JsonProperty("review", NullValueHandling = NullValueHandling.Ignore)] public bool? Review { get; set; }
        /// <summary>The layer the feature is drafted on (reported beside what the drawing has).</summary>
        [JsonProperty("layer", NullValueHandling = NullValueHandling.Ignore)] public string Layer { get; set; }
        /// <summary>Point styles or symbol blocks that show the feature (comma separated, * and ? allowed).</summary>
        [JsonProperty("symbolStyle", NullValueHandling = NullValueHandling.Ignore)] public string SymbolStyle { get; set; }
        /// <summary>Point label styles that label the feature (comma separated, * and ? allowed).</summary>
        [JsonProperty("labelStyle", NullValueHandling = NullValueHandling.Ignore)] public string LabelStyle { get; set; }
        /// <summary>Why: shown in the evidence and the Review window.</summary>
        [JsonProperty("note", NullValueHandling = NullValueHandling.Ignore)] public string Note { get; set; }

        internal void ApplyTo(EffectiveRequirement e)
        {
            if (Classification.HasValue) e.Classification = Classification.Value;
            if (Disposition.HasValue) e.Disposition = Disposition.Value;
            if (Symbol.HasValue) e.Symbol = Symbol.Value;
            if (Label.HasValue) e.Label = Label.Value;
            if (Surface.HasValue) e.Surface = Surface.Value;
            if (Rotation.HasValue) { e.Rotation = Rotation.Value; e.RotationDeclined = !Rotation.Value; }
            if (Review.HasValue) e.Review = Review.Value;
            if (!string.IsNullOrWhiteSpace(Layer)) e.Layer = Layer.Trim();
            if (!string.IsNullOrWhiteSpace(SymbolStyle)) e.SymbolStyle = SymbolStyle.Trim();
            if (!string.IsNullOrWhiteSpace(LabelStyle)) e.LabelStyle = LabelStyle.Trim();
            if (!string.IsNullOrWhiteSpace(Note)) e.Note = Note.Trim();
        }

        protected void CopyFieldsTo(RequirementFields to)
        {
            to.Classification = Classification; to.Disposition = Disposition; to.Symbol = Symbol; to.Label = Label;
            to.Surface = Surface; to.Rotation = Rotation; to.Review = Review; to.Layer = Layer;
            to.SymbolStyle = SymbolStyle; to.LabelStyle = LabelStyle; to.Note = Note;
        }
    }

    /// <summary>A special condition inside a requirement: a sign category, or words in the
    /// description, that change what the feature needs.</summary>
    public sealed class FeatureException : RequirementFields
    {
        [JsonProperty("name", NullValueHandling = NullValueHandling.Ignore)] public string Name { get; set; }
        /// <summary>Applies to a sign of this category.</summary>
        [JsonProperty("signCategory", NullValueHandling = NullValueHandling.Ignore)] public SignCategory? SignCategory { get; set; }
        /// <summary>Applies when the description (or the crew's note) matches this regular expression.</summary>
        [JsonProperty("when", NullValueHandling = NullValueHandling.Ignore)] public string When { get; set; }

        public FeatureException Copy()
        {
            var c = new FeatureException { Name = Name, SignCategory = SignCategory, When = When };
            CopyFieldsTo(c);
            return c;
        }
    }

    /// <summary>One feature's drafting requirement: matched by field code, by the words of a
    /// description shot without its code, or by Label All family ("*": the default).</summary>
    public sealed class FeatureRequirement : RequirementFields
    {
        public const string DefaultFamily = "*";

        [JsonProperty("name", NullValueHandling = NullValueHandling.Ignore)] public string Name { get; set; }
        /// <summary>Field codes (trailing figure digits ignored; * and ? allowed).</summary>
        [JsonProperty("codes", ObjectCreationHandling = ObjectCreationHandling.Replace, NullValueHandling = NullValueHandling.Ignore)] public List<string> Codes { get; set; }
        /// <summary>A Label All family (Utilities, Signs, ...), or "*" for every point no other entry matches.</summary>
        [JsonProperty("family", NullValueHandling = NullValueHandling.Ignore)] public string Family { get; set; }
        /// <summary>A regular expression on the description, for a feature shot without a code FTF
        /// knows ("HOUSE NUMBER 4735 SIGN"). Never applies to a point whose code FTF knows.</summary>
        [JsonProperty("match", NullValueHandling = NullValueHandling.Ignore)] public string Match { get; set; }
        [JsonProperty("enabled")] public bool Enabled { get; set; }
        [JsonProperty("exceptions", ObjectCreationHandling = ObjectCreationHandling.Replace, NullValueHandling = NullValueHandling.Ignore)] public List<FeatureException> Exceptions { get; set; }

        public FeatureRequirement() { Enabled = true; }

        [JsonIgnore] public bool IsDefault { get { return string.Equals((Family ?? string.Empty).Trim(), DefaultFamily, StringComparison.Ordinal) && (Codes == null || Codes.Count == 0) && string.IsNullOrWhiteSpace(Match); } }

        /// <summary>"WFH, FH", "Signs (family)", "description ~ \bSIGN\b", "default".</summary>
        [JsonIgnore] public string Key
        {
            get
            {
                if (IsDefault) return "default";
                var parts = new List<string>();
                if (Codes != null && Codes.Count > 0) parts.Add(string.Join(", ", Codes.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim().ToUpperInvariant()).ToArray()));
                if (!string.IsNullOrWhiteSpace(Family)) parts.Add(Family.Trim() + " (family)");
                if (!string.IsNullOrWhiteSpace(Match)) parts.Add("description ~ " + Match.Trim());
                return string.Join("; ", parts.ToArray());
            }
        }

        public FeatureRequirement Copy()
        {
            var c = new FeatureRequirement
            {
                Name = Name, Family = Family, Match = Match, Enabled = Enabled,
                Codes = Codes == null ? null : new List<string>(Codes),
                Exceptions = Exceptions == null ? null : Exceptions.Select(x => x.Copy()).ToList()
            };
            CopyFieldsTo(c);
            return c;
        }

        internal bool MatchesCode(string baseCode)
        {
            if (Codes == null || string.IsNullOrEmpty(baseCode)) return false;
            return Codes.Any(c => !string.IsNullOrWhiteSpace(c) && FeatureStandard.Glob(c, baseCode));
        }
    }

    /// <summary>One sign category: the codes that are always it, and the words that make a
    /// description it.</summary>
    public sealed class SignCategoryRule
    {
        [JsonProperty("category")] public SignCategory Category { get; set; }
        [JsonProperty("codes", ObjectCreationHandling = ObjectCreationHandling.Replace)] public List<string> Codes { get; set; }
        [JsonProperty("words", ObjectCreationHandling = ObjectCreationHandling.Replace)] public List<string> Words { get; set; }

        public SignCategoryRule() { Codes = new List<string>(); Words = new List<string>(); }

        public SignCategoryRule Copy() { return new SignCategoryRule { Category = Category, Codes = new List<string>(Codes ?? new List<string>()), Words = new List<string>(Words ?? new List<string>()) }; }
    }

    /// <summary>The point a requirement is looked up for.</summary>
    public sealed class FeatureSubject
    {
        /// <summary>The field code (the description's first word).</summary>
        public string Code { get; set; }
        public AnnotationCategory Family { get; set; }
        /// <summary>The raw description as Civil 3D stores it.</summary>
        public string Description { get; set; }
        /// <summary>The crew's note, when one was read.</summary>
        public string Note { get; set; }
        /// <summary>FTF knows the code (a rule, a symbol label, an ignore entry, a linework or
        /// control code, a code family): a description-match entry never applies to it.</summary>
        public bool KnownCode { get; set; }
    }

    /// <summary>A point's requirement, with every field decided.</summary>
    public sealed class EffectiveRequirement
    {
        public PointClass Classification { get; set; }
        public IntendedDisposition Disposition { get; set; }
        public SymbolRequirement Symbol { get; set; }
        public LabelRequirement Label { get; set; }
        public bool Surface { get; set; }
        /// <summary>Turning its symbol is part of the deliverable (FTFORIENT's review items hold the point).</summary>
        public bool Rotation { get; set; }
        /// <summary>The standard says outright that it is not to be turned by inference (an address or
        /// private sign): only a direction the crew recorded turns it. False when no entry says either
        /// way, which leaves FTFORIENT's own rules to decide.</summary>
        public bool RotationDeclined { get; set; }
        public bool Review { get; set; }
        public string Layer { get; set; }
        public string SymbolStyle { get; set; }
        public string LabelStyle { get; set; }
        public string Note { get; set; }
        public SignCategory SignCategory { get; set; }
        /// <summary>The entry that decided it (null: the default only).</summary>
        public FeatureRequirement Entry { get; set; }
        /// <summary>Matched by its code, or by its description: the standard names this feature.</summary>
        public bool Specific { get; set; }
        /// <summary>Exceptions that applied, in order.</summary>
        public List<string> Exceptions { get; private set; }
        /// <summary>"Fire hydrant (WFH, FH)", "Signs (Signs (family)), exception: Address / house number sign".</summary>
        public string Source { get; set; }

        public EffectiveRequirement()
        {
            Classification = PointClass.Feature;
            Disposition = IntendedDisposition.Drafted;
            Symbol = SymbolRequirement.PointStyle;
            Label = LabelRequirement.AsPlanned;
            Exceptions = new List<string>();
        }

        public IList<string> SymbolStyles { get { return FeatureStandard.Split(SymbolStyle); } }
        public IList<string> LabelStyles { get { return FeatureStandard.Split(LabelStyle); } }

        /// <summary>"Sign (address): symbol by point style, label by the point's label, not rotated".</summary>
        public string Describe()
        {
            var parts = new List<string>();
            switch (Symbol)
            {
                case SymbolRequirement.None: parts.Add("no symbol"); break;
                case SymbolRequirement.PointStyle: parts.Add("shown by its point style"); break;
                default: parts.Add("its own symbol"); break;
            }
            switch (Label)
            {
                case LabelRequirement.None: parts.Add("no label"); break;
                case LabelRequirement.Required: parts.Add("a label"); break;
                case LabelRequirement.PointLabel: parts.Add("labelled by its point label"); break;
                default: parts.Add("the label its rule writes"); break;
            }
            if (Surface) parts.Add("surface data");
            if (Rotation) parts.Add("rotated");
            else if (RotationDeclined) parts.Add("turned only by a recorded direction");
            if (Review) parts.Add("reviewed by a person");
            var cls = ClassName(Classification) + (SignCategory != SignCategory.None ? " (" + SignName(SignCategory) + ")" : string.Empty);
            return cls + ": " + string.Join(", ", parts.ToArray());
        }

        public static string ClassName(PointClass c)
        {
            switch (c)
            {
                case PointClass.Surface: return "Surface only";
                case PointClass.Control: return "Control/Check";
                default: return c.ToString();
            }
        }

        public static string SignName(SignCategory c)
        {
            switch (c)
            {
                case SignCategory.StreetName: return "street name";
                case SignCategory.Address: return "address / house number";
                default: return c.ToString().ToLowerInvariant();
            }
        }
    }

    /// <summary>
    /// The office's drafting standard per feature: for each field code or feature family,
    /// what the point is and what the plan must show for it -- a symbol (its own, or any point
    /// style marker), a label (FTF's, or the point's own Civil 3D label), surface use,
    /// rotation, review -- with exceptions (a house number sign is not a traffic sign).
    /// Point accountability, Label All, FTFORIENT and Drafting QC all read it, so they agree.
    ///
    /// Built in (<see cref="Pmx"/>) unless the rules file lists its own; Settings > Point
    /// Features edits it. A rules file that lists requirements but no default gets the
    /// built-in default, so every point has an answer.
    /// </summary>
    public sealed class FeatureStandard
    {
        [JsonProperty("requirements", ObjectCreationHandling = ObjectCreationHandling.Replace)] public List<FeatureRequirement> Requirements { get; set; }
        [JsonProperty("signCategories", ObjectCreationHandling = ObjectCreationHandling.Replace)] public List<SignCategoryRule> SignCategories { get; set; }

        public FeatureStandard()
        {
            Requirements = new List<FeatureRequirement>();
            SignCategories = new List<SignCategoryRule>();
        }

        public FeatureStandard Copy()
        {
            return new FeatureStandard
            {
                Requirements = (Requirements ?? new List<FeatureRequirement>()).Select(r => r.Copy()).ToList(),
                SignCategories = (SignCategories ?? new List<SignCategoryRule>()).Select(s => s.Copy()).ToList()
            };
        }

        /// <summary>
        /// The built-in standard fills in what a rules file does not say: an entry in the file
        /// replaces the built-in entry with the same key (the same codes, family or pattern), and
        /// every built-in entry it does not mention still applies, after the file's own. So an
        /// office file naming one code changes that code only; a built-in entry is switched off with
        /// an entry of the same key and "enabled": false. Sign categories the same, by category.
        /// </summary>
        public void FillMissing()
        {
            var pmx = Pmx();
            if (Requirements == null) Requirements = new List<FeatureRequirement>();
            Requirements.RemoveAll(r => r == null);
            var keys = new HashSet<string>(Requirements.Select(r => r.Key), StringComparer.OrdinalIgnoreCase);
            foreach (var builtIn in pmx.Requirements)
                if (!keys.Contains(builtIn.Key)) Requirements.Add(builtIn);
            if (SignCategories == null) SignCategories = new List<SignCategoryRule>();
            SignCategories.RemoveAll(c => c == null);
            foreach (var builtIn in pmx.SignCategories)
                if (!SignCategories.Any(c => c.Category == builtIn.Category)) SignCategories.Add(builtIn);
        }

        /// <summary>Problems that make the standard unusable: a pattern that is not a regular
        /// expression, a family that does not exist, an entry that matches nothing.</summary>
        public IList<string> Validate()
        {
            var problems = new List<string>();
            var i = 0;
            foreach (var r in Requirements ?? new List<FeatureRequirement>())
            {
                i++;
                if (r == null) continue;
                var name = string.IsNullOrWhiteSpace(r.Name) ? "requirement " + i : r.Name.Trim();
                var hasCodes = r.Codes != null && r.Codes.Any(c => !string.IsNullOrWhiteSpace(c));
                var hasFamily = !string.IsNullOrWhiteSpace(r.Family);
                if (!hasCodes && !hasFamily && string.IsNullOrWhiteSpace(r.Match))
                    problems.Add("featureStandard: " + name + " names no code, family or description pattern.");
                AnnotationCategory family;
                if (hasFamily && r.Family.Trim() != FeatureRequirement.DefaultFamily && !Enum.TryParse(r.Family.Trim(), true, out family))
                    problems.Add("featureStandard: " + name + ": '" + r.Family + "' is not a feature family (" +
                                 string.Join(", ", Enum.GetNames(typeof(AnnotationCategory))) + ", or * for the default).");
                if (!string.IsNullOrWhiteSpace(r.Match) && !IsRegex(r.Match))
                    problems.Add("featureStandard: " + name + ": the description pattern '" + r.Match + "' is not a valid regular expression.");
                foreach (var x in r.Exceptions ?? new List<FeatureException>())
                {
                    if (x == null) continue;
                    var xn = string.IsNullOrWhiteSpace(x.Name) ? "an exception" : "exception " + x.Name.Trim();
                    if (!x.SignCategory.HasValue && string.IsNullOrWhiteSpace(x.When))
                        problems.Add("featureStandard: " + name + ": " + xn + " has no condition (a sign category or a description pattern).");
                    if (!string.IsNullOrWhiteSpace(x.When) && !IsRegex(x.When))
                        problems.Add("featureStandard: " + name + ": " + xn + ": '" + x.When + "' is not a valid regular expression.");
                }
            }
            if ((Requirements ?? new List<FeatureRequirement>()).Count(r => r != null && r.Enabled && r.IsDefault) > 1)
                problems.Add("featureStandard: more than one default entry (family *).");
            return problems;
        }

        /// <summary>
        /// The requirement for one point. Entries cascade, most specific last: the default, then
        /// its family's entry, then an entry that matches its description (only when FTF does not
        /// know its code), then its code's entry; each sets the fields it names. Then the
        /// exceptions of those entries, in the same order, whose condition holds.
        /// </summary>
        public EffectiveRequirement Resolve(FeatureSubject subject)
        {
            var e = new EffectiveRequirement();
            var reqs = (Requirements ?? new List<FeatureRequirement>()).Where(r => r != null && r.Enabled).ToList();
            var code = BaseCode(subject == null ? null : subject.Code);
            var description = subject == null ? string.Empty : (subject.Description ?? string.Empty);
            var note = subject == null ? null : subject.Note;
            var familyName = subject == null ? null : subject.Family.ToString();

            var chain = new List<FeatureRequirement>();
            var def = reqs.FirstOrDefault(r => r.IsDefault);
            if (def != null) chain.Add(def);
            var family = reqs.FirstOrDefault(r => !r.IsDefault &&
                                                  string.Equals((r.Family ?? string.Empty).Trim(), familyName, StringComparison.OrdinalIgnoreCase));
            if (family != null) chain.Add(family);
            FeatureRequirement byMatch = null;
            if (subject != null && !subject.KnownCode)
                byMatch = reqs.FirstOrDefault(r => !string.IsNullOrWhiteSpace(r.Match) && IsRegex(r.Match) &&
                                                   (Regex.IsMatch(description, r.Match, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
                                                    (!string.IsNullOrWhiteSpace(note) && Regex.IsMatch(note, r.Match, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))));
            if (byMatch != null && !chain.Contains(byMatch)) chain.Add(byMatch);
            var byCode = reqs.FirstOrDefault(r => r.MatchesCode(code));
            if (byCode != null && !chain.Contains(byCode)) chain.Add(byCode);

            foreach (var r in chain) r.ApplyTo(e);
            e.Entry = chain.Count > 0 ? chain[chain.Count - 1] : null;
            e.Specific = byCode != null || byMatch != null;
            if (e.Classification == PointClass.Sign) e.SignCategory = SignCategoryOf(code, description, note);

            var text = description + (string.IsNullOrWhiteSpace(note) ? string.Empty : " " + note);
            foreach (var r in chain)
                foreach (var x in r.Exceptions ?? new List<FeatureException>())
                {
                    if (x == null) continue;
                    var holds = (x.SignCategory.HasValue || !string.IsNullOrWhiteSpace(x.When)) &&
                                (!x.SignCategory.HasValue || x.SignCategory.Value == e.SignCategory) &&
                                (string.IsNullOrWhiteSpace(x.When) || (IsRegex(x.When) && Regex.IsMatch(text, x.When, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)));
                    if (!holds) continue;
                    x.ApplyTo(e);
                    e.Exceptions.Add(string.IsNullOrWhiteSpace(x.Name) ? (x.SignCategory.HasValue ? EffectiveRequirement.SignName(x.SignCategory.Value) + " sign" : x.When) : x.Name.Trim());
                }

            if (e.Disposition == IntendedDisposition.Review) e.Review = true;
            if (e.Disposition == IntendedDisposition.SurfaceOnly) e.Surface = true;
            var entry = e.Entry;
            e.Source = entry == null ? "default"
                     : (string.IsNullOrWhiteSpace(entry.Name) ? entry.Key : entry.Name.Trim() + " (" + entry.Key + ")") +
                       (e.Exceptions.Count == 0 ? string.Empty : ", exception: " + string.Join(", ", e.Exceptions.ToArray()));
            return e;
        }

        /// <summary>A sign's category: words in its description first (they say what the sign is),
        /// then its code. A sign whose description has words naming no category is Other; one with
        /// no words at all (SN, SN 135) is None: a traffic sign, as before.</summary>
        public SignCategory SignCategoryOf(string code, string description, string note)
        {
            var text = " " + Normalize((description ?? string.Empty) + " " + (note ?? string.Empty)) + " ";
            foreach (var rule in SignCategories ?? new List<SignCategoryRule>())
                foreach (var word in rule.Words ?? new List<string>())
                {
                    var w = Normalize(word);
                    if (w.Length > 0 && text.IndexOf(" " + w + " ", StringComparison.Ordinal) >= 0) return rule.Category;
                }
            var baseCode = BaseCode(code);
            foreach (var rule in SignCategories ?? new List<SignCategoryRule>())
                if ((rule.Codes ?? new List<string>()).Any(c => Glob(c, baseCode))) return rule.Category;
            return DescriptiveWords(code, description, note).Count > 0 ? SignCategory.Other : SignCategory.None;
        }

        private static readonly HashSet<string> NotDescriptive = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SIGN", "SIGNS", "N", "S", "E", "W", "NE", "NW", "SE", "SW", "NB", "SB", "EB", "WB", "NORTH", "SOUTH", "EAST", "WEST",
            "FACES", "FACE", "FACING", "AZ", "AZIMUTH", "TO", "AT", "ON", "OF", "THE", "A", "W/", "WITH", "POST", "POLE"
        };

        /// <summary>The words of a description that say what a sign is: not the code, numbers,
        /// directions or the word SIGN.</summary>
        public static IList<string> DescriptiveWords(string code, string description, string note)
        {
            var words = Normalize((description ?? string.Empty) + " " + (note ?? string.Empty)).Split(' ').Where(w => w.Length > 0).ToList();
            var c = (code ?? string.Empty).Trim().ToUpperInvariant();
            if (words.Count > 0 && c.Length > 0 && string.Equals(words[0], c, StringComparison.OrdinalIgnoreCase)) words.RemoveAt(0);
            return words.Where(w => !NotDescriptive.Contains(w) && !Regex.IsMatch(w, @"^[0-9.]+$")).ToList();
        }

        private static string Normalize(string s)
        {
            return Regex.Replace((s ?? string.Empty).ToUpperInvariant(), @"[^A-Z0-9#/.]+", " ").Trim();
        }

        // ------------------------------------------------------------------ the built-in standard

        /// <summary>PMX's standard for a topographic survey.</summary>
        public static FeatureStandard Pmx()
        {
            var s = new FeatureStandard();
            s.Requirements.Add(new FeatureRequirement
            {
                Name = "Default", Family = FeatureRequirement.DefaultFamily, Classification = PointClass.Feature,
                Disposition = IntendedDisposition.Drafted, Symbol = SymbolRequirement.PointStyle, Label = LabelRequirement.AsPlanned,
                Surface = false, Review = false,
                Note = "Every point no other entry names: shown by its Civil 3D point style, labelled only where its code's rule writes a label."
            });
            s.Requirements.Add(new FeatureRequirement
            {
                Name = "Ground shot", Codes = new List<string> { "GS" }, Classification = PointClass.Surface,
                Disposition = IntendedDisposition.SurfaceOnly, Symbol = SymbolRequirement.None, Label = LabelRequirement.None, Surface = true,
                Note = "Topographic surface data: no symbol or label. Surface membership is checked only when Point Accountability > Verify surface membership is on."
            });
            s.Requirements.Add(new FeatureRequirement
            {
                Name = "Control check shot", Codes = new List<string> { "ZK" }, Classification = PointClass.Control,
                Disposition = IntendedDisposition.ControlCheck, Symbol = SymbolRequirement.None, Label = LabelRequirement.None,
                Note = "A check shot on survey control: no symbol or label; listed in the control check report."
            });
            s.Requirements.Add(new FeatureRequirement
            {
                Name = "Fire hydrant", Codes = new List<string> { "WFH", "FH" }, Classification = PointClass.Utility,
                Disposition = IntendedDisposition.Symbol, Symbol = SymbolRequirement.Specific, Label = LabelRequirement.None, Rotation = true,
                Note = "The hydrant symbol, turned by FTFORIENT; no text label."
            });
            s.Requirements.Add(new FeatureRequirement
            {
                Name = "Signs", Family = "Signs", Codes = new List<string> { "SN*", "SIGN" }, Match = @"\bSIGNS?\b",
                Classification = PointClass.Sign, Disposition = IntendedDisposition.Symbol, Symbol = SymbolRequirement.Specific,
                Label = LabelRequirement.PointLabel, Rotation = true,
                Note = "The sign symbol, turned to face its traffic; its description shown by the point's label (or an FTF label).",
                Exceptions = new List<FeatureException>
                {
                    new FeatureException { Name = "Address / house number sign", SignCategory = SignCategory.Address, Symbol = SymbolRequirement.PointStyle, Rotation = false,
                                           Note = "A homeowner's address sign, not a traffic sign: its own description is kept, and it is turned only by a recorded direction." },
                    new FeatureException { Name = "Private sign", SignCategory = SignCategory.Private, Symbol = SymbolRequirement.PointStyle, Rotation = false,
                                           Note = "A private sign: turned only by a recorded direction, never assumed to face a road." },
                    new FeatureException { Name = "Other descriptive sign", SignCategory = SignCategory.Other, Symbol = SymbolRequirement.PointStyle, Rotation = false,
                                           Note = "A sign its description names but no category covers: turned only by a recorded direction." }
                }
            });
            s.Requirements.Add(new FeatureRequirement
            {
                Name = "Utilities", Family = "Utilities", Classification = PointClass.Utility, Symbol = SymbolRequirement.PointStyle,
                Note = "Poles, valves, meters, guy anchors, junction boxes: shown by their point style; labelled where their rule writes a label."
            });
            s.Requirements.Add(new FeatureRequirement
            {
                Name = "Storm and sewer", Family = "StormSewer", Classification = PointClass.Structure, Symbol = SymbolRequirement.PointStyle,
                Note = "Structures: shown by their point style; their dips go to the Dip Builder."
            });
            s.Requirements.Add(new FeatureRequirement
            {
                Name = "Trees", Family = "Trees", Classification = PointClass.Vegetation, Symbol = SymbolRequirement.PointStyle,
                Note = "Trees: their rule's block, label or tag."
            });
            s.Requirements.Add(new FeatureRequirement
            {
                Name = "Control", Family = "Control", Classification = PointClass.Control, Symbol = SymbolRequirement.PointStyle,
                Note = "Control and monuments: in the control table when the drawing has one."
            });

            s.SignCategories.Add(new SignCategoryRule { Category = SignCategory.Address, Words = new List<string> { "HOUSE NUMBER", "HOUSE NO", "HOUSE #", "HOUSE NUM", "ADDRESS", "ADDR" } });
            s.SignCategories.Add(new SignCategoryRule { Category = SignCategory.Private, Words = new List<string> { "PRIVATE", "NO TRESPASSING", "BEWARE", "FOR SALE", "FOR RENT", "REAL ESTATE", "BUSINESS", "CHURCH", "ADVERTISING", "BILLBOARD", "NEIGHBORHOOD WATCH", "SECURITY" } });
            s.SignCategories.Add(new SignCategoryRule { Category = SignCategory.StreetName, Codes = new List<string> { "SNST" }, Words = new List<string> { "STREET NAME", "ST NAME", "STREET SIGN" } });
            s.SignCategories.Add(new SignCategoryRule { Category = SignCategory.Regulatory, Codes = new List<string> { "SNS", "SNY", "SNR*", "SNMPH" },
                                                       Words = new List<string> { "STOP", "YIELD", "SPEED LIMIT", "SPEED", "MPH", "NO PARKING", "PARKING", "ONE WAY", "DO NOT ENTER", "NO TURN", "NO U TURN", "KEEP RIGHT", "KEEP LEFT", "WRONG WAY", "ALL WAY", "ALLWAY", "HANDICAP", "ADA", "TOW AWAY", "NO TRUCKS", "NO OUTLET" } });
            s.SignCategories.Add(new SignCategoryRule { Category = SignCategory.Warning, Codes = new List<string> { "SNW*" },
                                                       Words = new List<string> { "WARNING", "CAUTION", "CURVE", "SCHOOL", "CROSSING", "XING", "PED", "PEDESTRIAN", "CHILDREN", "SLOW", "DEAD END", "BUMP", "HILL", "MERGE", "SIGNAL AHEAD", "AHEAD" } });
            return s;
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>The first word without trailing figure digits: SN2 -> SN.</summary>
        public static string BaseCode(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return string.Empty;
            var first = code.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)[0].ToUpperInvariant();
            var end = first.Length;
            while (end > 1 && char.IsDigit(first[end - 1])) end--;
            return first.Substring(0, end);
        }

        internal static bool Glob(string pattern, string value)
        {
            if (string.IsNullOrWhiteSpace(pattern) || string.IsNullOrEmpty(value)) return false;
            var rx = "^" + Regex.Escape(pattern.Trim()).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
            return Regex.IsMatch(value, rx, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        /// <summary>A name matches one of comma-separated globs.</summary>
        public static bool MatchesAny(string commaGlobs, string name)
        {
            return !string.IsNullOrWhiteSpace(name) && Split(commaGlobs).Any(g => Glob(g, name.Trim()));
        }

        public static IList<string> Split(string commaList)
        {
            return (commaList ?? string.Empty).Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                                              .Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
        }

        private static bool IsRegex(string pattern)
        {
            try { new Regex(pattern); return true; }
            catch (ArgumentException) { return false; }
        }

        public static string Fmt(double v) { return v.ToString("0.##", CultureInfo.InvariantCulture); }
    }
}
