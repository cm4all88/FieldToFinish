using System;
using System.Collections.Generic;
using System.Linq;
using FieldCodes.Standards;
using Newtonsoft.Json.Linq;

namespace FieldCodes.Editing
{
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
