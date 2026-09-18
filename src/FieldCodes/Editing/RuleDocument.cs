using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FieldCodes.Editing
{
    /// <summary>
    /// The surveyor-facing view of one feature rule: the concepts the editor exposes,
    /// nothing more. Everything absent from this class is preserved untouched in the
    /// underlying document.
    /// </summary>
    public sealed class EditableRule
    {
        public string Id { get; set; }
        public bool Enabled { get; set; }
        public string Description { get; set; }

        /// <summary>The match pattern. Advanced; validated before it can be saved.</summary>
        public string Match { get; set; }

        public string LabelFormat { get; set; }
        public string LabelLayer { get; set; }
        public string Leader { get; set; }
        public string TagPrefix { get; set; }

        /// <summary>Editable only when the rule already has a dripline.</summary>
        public bool HasDripline { get; set; }
        public string DripLayer { get; set; }

        // Read-only context the editor displays but never writes.
        public string RotationSummary { get; set; }
        public string SymbolSummary { get; set; }
        public bool InTable { get { return !string.IsNullOrEmpty(TagPrefix); } }
    }

    /// <summary>
    /// The rules file as an editable document.
    ///
    /// All edits happen on the JSON DOM, never by deserialising and re-serialising:
    /// the file carries documentation comments ("//..." keys), and future versions
    /// may add properties this build has never heard of. A round trip through the
    /// typed objects would silently destroy both. This class writes exactly the
    /// properties the editor owns and leaves every other byte of structure alone --
    /// an older UI can never delete configuration added later.
    ///
    /// Saving is atomic: the new content is fully serialised and validated first,
    /// written to a temp file, and swapped in with a backup. The original file as it
    /// was before the very first edit is kept permanently alongside.
    /// </summary>
    public sealed class RuleDocument
    {
        /// <summary>Kept forever: the file as it was before the editor's first write.</summary>
        public const string OriginalBackupSuffix = ".original.bak";

        private readonly JObject _root;

        private RuleDocument(JObject root)
        {
            _root = root;
        }

        public static RuleDocument Load(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("Rules file not found.", path);
            return FromText(File.ReadAllText(path));
        }

        public static RuleDocument FromText(string json)
        {
            try
            {
                return new RuleDocument(JObject.Parse(json));
            }
            catch (JsonException ex)
            {
                throw new ConfigException("Rules file is not valid JSON: " + ex.Message, ex);
            }
        }

        // ------------------------------------------------------------------ reading

        public IList<string> RuleIds()
        {
            return Rules().Select(r => (string)r["id"]).Where(id => id != null).ToList();
        }

        public EditableRule GetEditable(string id)
        {
            var rule = FindRule(id);
            if (rule == null) return null;

            var label = rule["label"] as JObject;
            var drip = rule["dripLine"] as JObject;
            var rotation = rule["rotation"] as JObject;

            var editable = new EditableRule
            {
                Id = id,
                Enabled = rule["enabled"] == null || rule["enabled"].Value<bool>(),
                Description = (string)rule["description"] ?? string.Empty,
                Match = (string)rule["match"] ?? string.Empty,
                LabelFormat = label != null ? (string)label["format"] ?? string.Empty : string.Empty,
                LabelLayer = label != null ? (string)label["layer"] ?? string.Empty : string.Empty,
                Leader = label != null ? (string)label["leader"] ?? "Auto" : "Auto",
                TagPrefix = (string)rule["tagPrefix"] ?? string.Empty,
                HasDripline = drip != null,
                DripLayer = drip != null ? (string)drip["layer"] ?? string.Empty : string.Empty
            };

            editable.RotationSummary = rotation == null
                ? "None"
                : ((string)rotation["mode"] ?? "Azimuth") +
                  " from field '" + ((string)rotation["from"] ?? "?") + "'";

            var insert = rule["insertBlock"] != null && rule["insertBlock"].Value<bool>();
            editable.SymbolSummary = insert
                ? "FTF inserts block " + ((string)rule["block"] ?? "?") + " (opt-in rule)"
                : "Civil 3D (description keys) - FTF inserts nothing";

            return editable;
        }

        // ------------------------------------------------------------------ editing

        /// <summary>
        /// Writes the editable fields back onto the document. Only the properties the
        /// editor owns are touched; sibling properties (clusterFormat, validate,
        /// species, unknown future keys, comments) are untouched by construction.
        /// </summary>
        public void ApplyEdit(EditableRule edit)
        {
            if (edit == null) throw new ArgumentNullException("edit");

            var rule = FindRule(edit.Id);
            if (rule == null)
                throw new ConfigException("No rule with id '" + edit.Id + "'.");

            // enabled: absent means true, so the property only exists when disabling.
            if (edit.Enabled) rule.Remove("enabled");
            else rule["enabled"] = false;

            SetOrRemove(rule, "description", edit.Description);
            if (!string.IsNullOrWhiteSpace(edit.Match)) rule["match"] = edit.Match;
            SetOrRemove(rule, "tagPrefix", edit.TagPrefix);

            // Label: created when text is set on a rule that had none; the object is
            // only removed when it would carry nothing at all, so a clusterFormat or
            // any future property keeps it alive.
            var label = rule["label"] as JObject;
            var wantsLabel = !string.IsNullOrWhiteSpace(edit.LabelFormat);

            if (label == null && wantsLabel)
            {
                label = new JObject();
                rule["label"] = label;
            }

            if (label != null)
            {
                SetOrRemove(label, "format", edit.LabelFormat);
                SetOrRemove(label, "layer", edit.LabelLayer);

                if (!string.IsNullOrWhiteSpace(edit.Leader) && edit.Leader != "Auto")
                    label["leader"] = edit.Leader;
                else
                    label.Remove("leader");

                if (!label.Properties().Any())
                    rule.Remove("label");
            }

            // Dripline: the editor can retarget its layer, never create or delete the
            // dripline itself -- that is a feature-design decision, not a setting.
            var drip = rule["dripLine"] as JObject;
            if (drip != null && !string.IsNullOrWhiteSpace(edit.DripLayer))
                drip["layer"] = edit.DripLayer;
        }

        private static void SetOrRemove(JObject owner, string name, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) owner.Remove(name);
            else owner[name] = value;
        }

        // --------------------------------------------------------------- validation

        /// <summary>Empty list means the document is safe to save.</summary>
        public IList<string> Validate()
        {
            var problems = new List<string>();

            RulesConfig cfg;
            try
            {
                cfg = JsonConvert.DeserializeObject<RulesConfig>(Render());
                if (cfg == null) { problems.Add("Rules document is empty."); return problems; }
                cfg.Compile();
            }
            catch (ConfigException ex)
            {
                problems.Add(ex.Message);
                return problems;
            }
            catch (JsonException ex)
            {
                problems.Add("Not valid JSON: " + ex.Message);
                return problems;
            }

            EditorChecks(cfg, problems);
            return problems;
        }

        /// <summary>
        /// The checks Compile() does not make: they only matter once rules are being
        /// edited by hand in a UI rather than authored deliberately in the file.
        /// </summary>
        private static void EditorChecks(RulesConfig cfg, List<string> problems)
        {
            var derivedFields = new[] { "trunk.stems", "trunk.count", "trunk.exact" };
            var modifierGroups = cfg.Modifiers
                .Where(m => m.Regex != null)
                .SelectMany(m => m.Regex.GetGroupNames())
                .Where(NotNumeric)
                .ToList();

            foreach (var rule in cfg.Codes)
            {
                if (rule.Regex == null) continue;   // already reported by Compile
                var where = "Rule '" + rule.Id + "': ";

                var groups = rule.Regex.GetGroupNames().Where(NotNumeric).ToList();

                if (rule.Label != null && !string.IsNullOrWhiteSpace(rule.Label.Format) &&
                    string.IsNullOrWhiteSpace(rule.Label.Layer))
                    problems.Add(where + "a label needs a layer.");

                CheckLayerName(where + "label layer",
                    rule.Label != null ? rule.Label.Layer : null, problems);
                CheckLayerName(where + "dripline layer",
                    rule.DripLine != null ? rule.DripLine.Layer : null, problems);

                if (!string.IsNullOrEmpty(rule.TagPrefix) &&
                    !Regex.IsMatch(rule.TagPrefix, "^[A-Za-z_-]+$"))
                    problems.Add(where + "tag prefix '" + rule.TagPrefix +
                                 "' may only contain letters, '-' or '_' -- a digit would " +
                                 "merge with the tag number.");

                if (rule.Rotation != null &&
                    (string.IsNullOrWhiteSpace(rule.Rotation.From) ||
                     !groups.Contains(rule.Rotation.From)))
                    problems.Add(where + "rotation reads field '" +
                                 (rule.Rotation.From ?? "(none)") +
                                 "', which is not a named group in the pattern.");

                var valid = new HashSet<string>(groups, StringComparer.OrdinalIgnoreCase);
                foreach (var f in derivedFields) valid.Add(f);
                foreach (var g in modifierGroups) valid.Add(g);

                if (rule.Label != null)
                {
                    CheckPlaceholders(where + "label", rule.Label.Format, valid, problems);
                    CheckPlaceholders(where + "cluster label", rule.Label.ClusterFormat, valid, problems);
                }
            }

            CheckConflicts(cfg, problems);
        }

        private static void CheckLayerName(string where, string layer, List<string> problems)
        {
            if (string.IsNullOrWhiteSpace(layer)) return;
            if (layer.IndexOfAny(new[]
                { '<', '>', '/', '\\', '"', ':', ';', '?', '*', '|', ',', '=', '`' }) >= 0)
                problems.Add(where + " '" + layer + "' contains characters AutoCAD does " +
                             "not allow in a layer name.");
        }

        private static readonly Regex Placeholder =
            new Regex(@"\{(?<name>[A-Za-z_][A-Za-z0-9_.]*)(?::[^}]+)?\}",
                      RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static void CheckPlaceholders(string where, string template,
                                              HashSet<string> valid, List<string> problems)
        {
            if (string.IsNullOrEmpty(template)) return;

            foreach (Match m in Placeholder.Matches(template))
            {
                var name = m.Groups["name"].Value;
                if (!valid.Contains(name))
                    problems.Add(where + " uses {" + name + "}, which is neither a named " +
                                 "group in the pattern nor a derived field.");

                // The format specifier after the colon is fed to double.ToString at
                // parse time. An invalid one used to pass validation here and then
                // throw on EVERY point, killing whole runs. Prove it formats now.
                var colon = m.Value.IndexOf(':');
                if (colon >= 0)
                {
                    var fmt = m.Value.Substring(colon + 1,
                        m.Value.Length - colon - 2);   // strip the closing brace
                    try
                    {
                        (0.0).ToString(fmt, System.Globalization.CultureInfo.InvariantCulture);
                    }
                    catch (FormatException)
                    {
                        problems.Add(where + " uses the number format '" + fmt +
                                     "' in {" + name + "}, which is not a valid " +
                                     ".NET numeric format.");
                    }
                }
            }
        }

        /// <summary>
        /// Two enabled rules claiming the same code would make the winner depend on
        /// file order. Detection is by probing each rule's literal codes against every
        /// other rule's real regex, so it can miss exotic patterns but never cries wolf.
        /// </summary>
        private static void CheckConflicts(RulesConfig cfg, List<string> problems)
        {
            var enabled = cfg.Codes.Where(c => c.Enabled && c.Regex != null).ToList();
            var reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var rule in enabled)
            {
                foreach (var token in LiteralCodes(rule))
                {
                    if (reported.Contains(token)) continue;

                    var probe = token + " 1";
                    var claimants = enabled
                        .Where(c => c.Regex.IsMatch(probe))
                        .Select(c => c.Id)
                        .ToList();

                    if (claimants.Count > 1)
                    {
                        reported.Add(token);
                        problems.Add("Code '" + token + "' is claimed by more than one " +
                                     "enabled rule: " + string.Join(", ", claimants.ToArray()) +
                                     ". Whichever loads first would win.");
                    }
                }
            }
        }

        /// <summary>Best-effort literal codes out of a rule pattern. Public because the
        /// editor UI shows them as the feature code column.</summary>
        public static IList<string> LiteralCodes(CodeRule rule)
        {
            if (rule.Match != null &&
                rule.Match.IndexOf("{SPECIES}", StringComparison.OrdinalIgnoreCase) >= 0)
                return rule.Species.Keys.ToList();

            var match = rule.Match ?? string.Empty;
            var start = match.IndexOf("(?<code>", StringComparison.Ordinal);
            if (start < 0) return new List<string>();

            start += 8;
            var depth = 1;
            var end = start;
            while (end < match.Length && depth > 0)
            {
                if (match[end] == '(') depth++;
                else if (match[end] == ')') depth--;
                if (depth > 0) end++;
            }

            var tokens = new List<string>();
            foreach (var alt in match.Substring(start, end - start).Split('|'))
            {
                var cleaned = alt.Replace("(?:", "").Replace(")", "").Replace("?", "").Trim();

                // A token still carrying regex machinery cannot be probed literally.
                if (cleaned.Length == 0 ||
                    cleaned.IndexOfAny(new[] { '[', ']', '\\', '(', '+', '*', '.', '^', '$', '{' }) >= 0)
                    continue;

                tokens.Add(cleaned);
            }

            return tokens;
        }

        // ------------------------------------------------------------------ output

        public string Render()
        {
            return _root.ToString(Formatting.Indented);
        }

        /// <summary>The document compiled as the parser would see it, for previews.</summary>
        public RulesConfig CompileCandidate()
        {
            var cfg = JsonConvert.DeserializeObject<RulesConfig>(Render());
            if (cfg == null) throw new ConfigException("Rules document is empty.");
            cfg.Compile();
            return cfg;
        }

        /// <summary>
        /// Atomic save. Order matters: serialise and validate completely before any
        /// file is touched, write to a temp file, then swap. A crash at any point
        /// leaves either the old file or the new file -- never a partial one.
        /// </summary>
        public void Save(string path)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentNullException("path");

            var problems = Validate();
            if (problems.Count > 0)
                throw new ConfigException("Refusing to save invalid rules:" +
                    Environment.NewLine + string.Join(Environment.NewLine, problems.ToArray()));

            var text = Render();

            // First save into the office level creates its directory.
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            // The file as it was before the editor ever touched it, kept permanently.
            var original = path + OriginalBackupSuffix;
            if (File.Exists(path) && !File.Exists(original))
                File.Copy(path, original);

            var tmp = path + ".tmp";
            File.WriteAllText(tmp, text);

            if (File.Exists(path))
                File.Replace(tmp, path, path + ".bak");
            else
                File.Move(tmp, path);
        }

        // ------------------------------------------------------------------ helpers

        private JArray Rules()
        {
            return _root["codes"] as JArray ?? new JArray();
        }

        private JObject FindRule(string id)
        {
            return Rules().OfType<JObject>().FirstOrDefault(
                r => string.Equals((string)r["id"], id, StringComparison.OrdinalIgnoreCase));
        }

        private static bool NotNumeric(string name)
        {
            int ignored;
            return !int.TryParse(name, out ignored);
        }
    }
}
