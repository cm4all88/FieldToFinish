using System;
using System.Collections.Generic;
using System.Linq;
using FieldCodes.Standards;
using Newtonsoft.Json.Linq;

namespace FieldCodes.Editing
{
    /// <summary>
    /// One exception of a drafting standard entry as Settings > Point Features > Drafting Standard
    /// shows it, a row of the exceptions grid: every field as the grid's text. Blank or (inherit)
    /// leaves a field to the entry; the flags are Yes or No. A row that is not right yet is kept as
    /// typed, and <see cref="ToExceptions"/> names what is wrong with it.
    /// </summary>
    public sealed class EditableFeatureException
    {
        public const string Inherit = "(inherit)";

        public string Name { get; set; }

        /// <summary>A <see cref="FieldCodes.Standards.SignCategory"/> name, or blank: the exception is
        /// for a sign of this category.</summary>
        public string SignCategory { get; set; }

        /// <summary>A regular expression on the description (or the crew's note), or blank.</summary>
        public string When { get; set; }

        public string Classification { get; set; }
        public string Disposition { get; set; }
        public string Symbol { get; set; }
        public string Label { get; set; }

        /// <summary>Yes, No, or blank / (inherit).</summary>
        public string Surface { get; set; }
        public string Rotation { get; set; }
        public string Review { get; set; }

        public string Layer { get; set; }
        public string SymbolStyle { get; set; }
        public string LabelStyle { get; set; }
        public string Note { get; set; }

        /// <summary>The sign categories an exception can be for (blank first: not by category).</summary>
        public static string[] SignCategories()
        {
            return new[] { string.Empty }.Concat(Enum.GetNames(typeof(SignCategory)).Where(n => n != FieldCodes.Standards.SignCategory.None.ToString()))
                                        .Concat(new[] { FieldCodes.Standards.SignCategory.None.ToString() }).ToArray();
        }

        public static EditableFeatureException From(FeatureException x)
        {
            return new EditableFeatureException
            {
                Name = x.Name ?? string.Empty,
                SignCategory = x.SignCategory.HasValue ? x.SignCategory.Value.ToString() : string.Empty,
                When = x.When ?? string.Empty,
                Classification = Show(x.Classification),
                Disposition = Show(x.Disposition),
                Symbol = Show(x.Symbol),
                Label = Show(x.Label),
                Surface = Show(x.Surface),
                Rotation = Show(x.Rotation),
                Review = Show(x.Review),
                Layer = x.Layer ?? string.Empty,
                SymbolStyle = x.SymbolStyle ?? string.Empty,
                LabelStyle = x.LabelStyle ?? string.Empty,
                Note = x.Note ?? string.Empty
            };
        }

        public static List<EditableFeatureException> FromList(IEnumerable<FeatureException> exceptions)
        {
            return (exceptions ?? Enumerable.Empty<FeatureException>()).Where(x => x != null).Select(From).ToList();
        }

        /// <summary>True when nothing has been typed in the row: it is left out, not reported.</summary>
        public bool IsBlank
        {
            get
            {
                return new[] { Name, SignCategory, When, Layer, SymbolStyle, LabelStyle, Note }.All(string.IsNullOrWhiteSpace) &&
                       new[] { Classification, Disposition, Symbol, Label, Surface, Rotation, Review }.All(IsInherit);
            }
        }

        /// <summary>"Signs; Address / house number sign": what the requirement grid's Exceptions column shows.</summary>
        public static string Summary(IEnumerable<EditableFeatureException> rows)
        {
            return string.Join("; ", (rows ?? Enumerable.Empty<EditableFeatureException>()).Where(x => x != null && !x.IsBlank)
                .Select(x => !string.IsNullOrWhiteSpace(x.Name) ? x.Name.Trim()
                           : !string.IsNullOrWhiteSpace(x.SignCategory) ? x.SignCategory.Trim()
                           : "when " + (x.When ?? string.Empty).Trim()).ToArray());
        }

        /// <summary>
        /// The rows as the standard reads them, in order; null when there are none. Blank rows are left
        /// out. A row that is wrong is named in <paramref name="problems"/> ("Row 3 (Signs), exception 2
        /// (House number): ...") and left out.
        /// </summary>
        public static List<FeatureException> ToExceptions(IEnumerable<EditableFeatureException> rows, string owner, ICollection<string> problems)
        {
            var result = new List<FeatureException>();
            var n = 0;
            foreach (var row in rows ?? Enumerable.Empty<EditableFeatureException>())
            {
                n++;
                if (row == null || row.IsBlank) continue;
                var x = row.ToException(owner + ", exception " + n + (string.IsNullOrWhiteSpace(row.Name) ? string.Empty : " (" + row.Name.Trim() + ")") + ": ", problems);
                if (x != null) result.Add(x);
            }
            return result.Count == 0 ? null : result;
        }

        /// <summary>The exception, or null with what is wrong added to <paramref name="problems"/> (each prefixed by <paramref name="where"/>).</summary>
        public FeatureException ToException(string where, ICollection<string> problems)
        {
            var before = problems.Count;
            var x = new FeatureException { Name = Blank(Name), When = Blank(When) };

            if (!IsInherit(SignCategory))
            {
                SignCategory category;
                if (Enum.TryParse(SignCategory.Trim(), true, out category) && Enum.IsDefined(typeof(SignCategory), category)) x.SignCategory = category;
                else problems.Add(where + "sign category '" + SignCategory.Trim() + "' is not one of " +
                                  string.Join(", ", Enum.GetNames(typeof(SignCategory))) + ".");
            }
            if (!x.SignCategory.HasValue && x.When == null && problems.Count == before)
                problems.Add(where + "needs a sign category or a description pattern (when), to say which points it is for.");
            if (x.When != null)
            {
                try { new System.Text.RegularExpressions.Regex(x.When); }
                catch (ArgumentException ex)
                {
                    problems.Add(where + "the description pattern '" + x.When + "' is not a valid regular expression (" + ex.Message + ").");
                }
            }

            x.Classification = Read<PointClass>(Classification, "class", where, problems);
            x.Disposition = Read<IntendedDisposition>(Disposition, "disposition", where, problems);
            x.Symbol = Read<SymbolRequirement>(Symbol, "symbol", where, problems);
            x.Label = Read<LabelRequirement>(Label, "label", where, problems);
            x.Surface = ReadFlag(Surface, "surface", where, problems);
            x.Rotation = ReadFlag(Rotation, "rotate", where, problems);
            x.Review = ReadFlag(Review, "review", where, problems);
            x.Layer = Blank(Layer);
            x.SymbolStyle = Blank(SymbolStyle);
            x.LabelStyle = Blank(LabelStyle);
            x.Note = Blank(Note);
            return problems.Count == before ? x : null;
        }

        private static bool IsInherit(string text)
        {
            return string.IsNullOrWhiteSpace(text) || string.Equals(text.Trim(), Inherit, StringComparison.OrdinalIgnoreCase);
        }

        private static T? Read<T>(string text, string field, string where, ICollection<string> problems) where T : struct
        {
            if (IsInherit(text)) return null;
            T value;
            if (Enum.TryParse(text.Trim(), true, out value) && Enum.IsDefined(typeof(T), value)) return value;
            problems.Add(where + field + " '" + text.Trim() + "' is not one of " + string.Join(", ", Enum.GetNames(typeof(T))) + " (or " + Inherit + ").");
            return null;
        }

        private static bool? ReadFlag(string text, string field, string where, ICollection<string> problems)
        {
            if (IsInherit(text)) return null;
            var t = text.Trim();
            if (string.Equals(t, "Yes", StringComparison.OrdinalIgnoreCase) || string.Equals(t, "true", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(t, "No", StringComparison.OrdinalIgnoreCase) || string.Equals(t, "false", StringComparison.OrdinalIgnoreCase)) return false;
            problems.Add(where + field + " must be Yes, No or " + Inherit + " (not '" + t + "').");
            return null;
        }

        private static string Show<T>(T? value) where T : struct { return value.HasValue ? value.Value.ToString() : Inherit; }

        private static string Show(bool? value) { return value.HasValue ? (value.Value ? "Yes" : "No") : Inherit; }

        private static string Blank(string s) { return string.IsNullOrWhiteSpace(s) ? null : s.Trim(); }
    }

    /// <summary>
    /// The drafting standard per feature ("featureStandard"), as Settings > Point Features edits it.
    /// The file lists only what the office changes from the built-in PMX standard
    /// (<see cref="FeatureStandard.Pmx"/>): an entry replaces the built-in entry with the same key
    /// (the same codes, family or description pattern), and "enabled": false switches a built-in
    /// entry off. So the editor shows every entry in force, and writes back only the ones that
    /// differ from the built-in standard -- an office that changes nothing keeps no section at all,
    /// and later corrections to the built-in standard still reach it.
    /// </summary>
    public sealed partial class RuleDocument
    {
        /// <summary>Written above the section the editor creates.</summary>
        public const string FeatureStandardComment =
            "Drafting standard per feature, edited in Settings > Point Features > Drafting Standard. Only what the office changes " +
            "from the built-in PMX standard is listed: an entry replaces the built-in entry with the same codes, family or description " +
            "match, and \"enabled\": false switches one off. Each says what the feature needs on the plan: class, disposition, symbol " +
            "(None, PointStyle, Specific), label (AsPlanned, None, Required, PointLabel), surface, rotation, review, layer, symbolStyle, " +
            "labelStyle, exceptions (by signCategory or a \"when\" pattern). Remove this section to go back to the built-in standard.";

        /// <summary>True when the file has no drafting standard of its own: the built-in one is in force.</summary>
        public bool FeatureStandardIsBuiltIn()
        {
            var file = FileFeatureStandard();
            return file == null || (file.Requirements.Count == 0 && file.SignCategories.Count == 0);
        }

        /// <summary>
        /// Every requirement in force, for the editor: the file's own entries that are not built in,
        /// in the file's order (they are looked at first), then every built-in entry in PMX order --
        /// the file's version of it where the file has one with the same key.
        /// </summary>
        public IList<FeatureRequirement> GetFeatureRequirements()
        {
            var file = FileFeatureStandard() ?? new FeatureStandard();
            var builtIn = FeatureStandard.Pmx().Requirements;
            var builtInKeys = new HashSet<string>(builtIn.Select(b => b.Key), StringComparer.OrdinalIgnoreCase);
            var result = file.Requirements.Where(r => r != null && !builtInKeys.Contains(r.Key)).Select(r => r.Copy()).ToList();
            foreach (var b in builtIn)
            {
                var own = file.Requirements.FirstOrDefault(r => r != null && string.Equals(r.Key, b.Key, StringComparison.OrdinalIgnoreCase));
                result.Add((own ?? b).Copy());
            }
            return result;
        }

        /// <summary>Every sign category in force: the file's version where it has one, else the built-in.</summary>
        public IList<SignCategoryRule> GetSignCategories()
        {
            var file = FileFeatureStandard() ?? new FeatureStandard();
            var result = new List<SignCategoryRule>();
            foreach (var b in FeatureStandard.Pmx().SignCategories)
            {
                var own = file.SignCategories.FirstOrDefault(c => c != null && c.Category == b.Category);
                result.Add((own ?? b).Copy());
            }
            foreach (var own in file.SignCategories.Where(c => c != null && !result.Any(r => r.Category == c.Category)))
                result.Add(own.Copy());
            return result;
        }

        /// <summary>The built-in entry with this key, or null: the office's own entry.</summary>
        public static FeatureRequirement BuiltInRequirement(string key)
        {
            return FeatureStandard.Pmx().Requirements.FirstOrDefault(b => string.Equals(b.Key, key, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>True when the entry says exactly what the built-in entry with its key says.</summary>
        public static bool SameAsBuiltIn(FeatureRequirement r)
        {
            if (r == null) return false;
            var b = BuiltInRequirement(r.Key);
            return b != null && JToken.DeepEquals(JObject.FromObject(Normalized(r)), JObject.FromObject(Normalized(b)));
        }

        private static bool SameAsBuiltIn(SignCategoryRule c)
        {
            var b = FeatureStandard.Pmx().SignCategories.FirstOrDefault(x => x.Category == c.Category);
            return b != null && JToken.DeepEquals(JObject.FromObject(Normalized(c)), JObject.FromObject(Normalized(b)));
        }

        /// <summary>
        /// Writes the standard the editor shows: only the entries and sign categories that differ
        /// from the built-in ones (a built-in entry switched off is written with "enabled": false).
        /// Comments and unknown properties of the section itself are kept. With nothing different the
        /// section is removed, and the built-in standard is in force again. Refused (nothing written)
        /// when the result would not load: the problems are in the exception.
        /// </summary>
        public void SetFeatureStandard(IEnumerable<FeatureRequirement> requirements, IEnumerable<SignCategoryRule> signCategories)
        {
            var rows = (requirements ?? Enumerable.Empty<FeatureRequirement>()).Where(r => r != null).Select(Normalized).ToList();
            var signs = (signCategories ?? Enumerable.Empty<SignCategoryRule>()).Where(c => c != null).Select(Normalized).ToList();

            var check = new FeatureStandard { Requirements = rows.Select(r => r.Copy()).ToList(), SignCategories = signs.Select(s => s.Copy()).ToList() };
            check.FillMissing();
            var problems = check.Validate();
            var keys = rows.GroupBy(r => r.Key, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            foreach (var k in keys) problems.Add("Drafting standard: two entries for " + k + "; merge them into one.");
            if (problems.Count > 0)
                throw new ConfigException("The drafting standard was not changed:" + Environment.NewLine + string.Join(Environment.NewLine, problems.ToArray()));

            var changed = rows.Where(r => !SameAsBuiltIn(r)).ToList();
            var changedSigns = signs.Where(c => !SameAsBuiltIn(c)).ToList();
            if (changed.Count == 0 && changedSigns.Count == 0) { UseBuiltInFeatureStandard(); return; }

            var section = _root["featureStandard"] as JObject;
            if (section == null)
            {
                section = new JObject();
                if (_root["//featureStandard"] == null) _root["//featureStandard"] = FeatureStandardComment;
                _root["featureStandard"] = section;
            }
            section["requirements"] = new JArray(changed.Select(r => (JToken)JObject.FromObject(r)));
            if (changedSigns.Count > 0) section["signCategories"] = new JArray(changedSigns.Select(c => (JToken)JObject.FromObject(c)));
            else section.Remove("signCategories");
        }

        /// <summary>Removes the file's drafting standard (and the editor's note): the built-in one is in force.</summary>
        public void UseBuiltInFeatureStandard()
        {
            _root.Remove("featureStandard");
            if (string.Equals((string)_root["//featureStandard"], FeatureStandardComment, StringComparison.Ordinal))
                _root.Remove("//featureStandard");
        }

        private FeatureStandard FileFeatureStandard()
        {
            var section = _root["featureStandard"] as JObject;
            if (section == null) return null;
            FeatureStandard standard;
            try { standard = section.ToObject<FeatureStandard>(); }
            catch (Newtonsoft.Json.JsonException ex) { throw new ConfigException("featureStandard: " + ex.Message); }
            if (standard == null) return null;
            if (standard.Requirements == null) standard.Requirements = new List<FeatureRequirement>();
            if (standard.SignCategories == null) standard.SignCategories = new List<SignCategoryRule>();
            return standard;
        }

        /// <summary>Trimmed text, empty lists and text as absent, so an untouched row compares
        /// equal to its built-in entry.</summary>
        private static FeatureRequirement Normalized(FeatureRequirement r)
        {
            var n = r.Copy();
            n.Name = Blank(n.Name);
            n.Family = Blank(n.Family);
            n.Match = Blank(n.Match);
            n.Codes = n.Codes == null ? null : n.Codes.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim().ToUpperInvariant()).ToList();
            if (n.Codes != null && n.Codes.Count == 0) n.Codes = null;
            NormalizeFields(n);
            if (n.Exceptions != null)
            {
                foreach (var x in n.Exceptions) { x.Name = Blank(x.Name); x.When = Blank(x.When); NormalizeFields(x); }
                if (n.Exceptions.Count == 0) n.Exceptions = null;
            }
            return n;
        }

        private static void NormalizeFields(RequirementFields f)
        {
            f.Layer = Blank(f.Layer);
            f.SymbolStyle = Blank(f.SymbolStyle);
            f.LabelStyle = Blank(f.LabelStyle);
            f.Note = Blank(f.Note);
        }

        private static SignCategoryRule Normalized(SignCategoryRule c)
        {
            return new SignCategoryRule
            {
                Category = c.Category,
                Codes = (c.Codes ?? new List<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim().ToUpperInvariant()).ToList(),
                Words = (c.Words ?? new List<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim().ToUpperInvariant()).ToList()
            };
        }

        private static string Blank(string s)
        {
            return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        }
    }
}
