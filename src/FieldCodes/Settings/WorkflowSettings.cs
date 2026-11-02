using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FieldCodes.Annotation;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace FieldCodes.Settings
{
    /// <summary>
    /// One saved processing run: its name, the finishing stages it runs, the feature families
    /// the label and tag stages cover, and the commands queued once it finishes. It runs exactly
    /// like a built-in preset (<see cref="ProcessPresets.Build(WorkflowDefinition, AnnotationSettings)"/>).
    /// </summary>
    public sealed class WorkflowDefinition
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        /// <summary>Stage command names (<see cref="ProcessStage.InOrder"/>). Run in pipeline order
        /// whatever order they are listed in.</summary>
        [JsonProperty("stages", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<string> Stages { get; set; }

        /// <summary>Families the label and tag stages cover. Empty means the families set on
        /// Settings > Label All, so the workflow follows that setting.</summary>
        [JsonProperty("families", ObjectCreationHandling = ObjectCreationHandling.Replace, ItemConverterType = typeof(StringEnumConverter))]
        public List<AnnotationCategory> Families { get; set; }

        /// <summary>Commands queued after the stages, from <see cref="SettingsRegistry.WorkflowFollowUps"/>.</summary>
        [JsonProperty("thenRun", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<string> ThenRun { get; set; }

        public WorkflowDefinition()
        {
            Name = string.Empty;
            Description = string.Empty;
            Stages = new List<string>();
            Families = new List<AnnotationCategory>();
            ThenRun = new List<string>();
        }

        /// <summary>What FTFRUN's prompt offers this workflow as.</summary>
        [JsonIgnore]
        public string Keyword { get { return WorkflowSettings.Keyword(Name); } }

        public WorkflowDefinition Clone()
        {
            return new WorkflowDefinition
            {
                Name = Name,
                Description = Description,
                Stages = new List<string>(Stages ?? new List<string>()),
                Families = new List<AnnotationCategory>(Families ?? new List<AnnotationCategory>()),
                ThenRun = new List<string>(ThenRun ?? new List<string>())
            };
        }

        public override string ToString() { return Name; }
    }

    /// <summary>
    /// Settings > Workflows: processing runs the office or a user has named, beside the built-in
    /// presets. Each appears in the Process page's preset list and as a choice at FTFRUN's prompt.
    /// Saved with the rest of the settings, so a profile carries its workflows with it.
    /// </summary>
    public sealed class WorkflowSettings : ISettingsSection
    {
        public string Title { get { return "Workflows"; } }
        public string AffectedCommands { get { return "FTFRUN and the FTF window's Process page"; } }

        [JsonProperty("items", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<WorkflowDefinition> Items { get; set; }

        public WorkflowSettings() { RestoreDefaults(); }

        /// <summary>None: the built-in presets cover the shipped behaviour.</summary>
        public void RestoreDefaults()
        {
            Items = new List<WorkflowDefinition>();
        }

        /// <summary>FTFRUN's own choices. A workflow cannot take one of these as its keyword.</summary>
        public static readonly IList<string> BuiltInKeywords =
            new List<string> { "Standard", "Trees", "Annotations", "All" }.AsReadOnly();

        /// <summary>Longest keyword offered at the prompt.</summary>
        public const int MaxKeywordLength = 31;

        /// <summary>
        /// The keyword FTFRUN offers for a workflow name: its words run together, each starting with
        /// a capital ("Trees only (no tags)" is TreesOnlyNoTags); letters and digits only, since
        /// a command-line keyword cannot hold spaces or punctuation. A name starting with a digit
        /// gets "Run" in front. Empty when the name has no letter or digit to offer.
        /// </summary>
        public static string Keyword(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;
            var sb = new StringBuilder();
            var startWord = true;
            foreach (var c in name.Trim())
            {
                var ascii = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9');
                if (!ascii) { startWord = true; continue; }
                sb.Append(startWord ? char.ToUpperInvariant(c) : c);
                startWord = false;
            }
            if (sb.Length == 0) return string.Empty;
            if (sb[0] >= '0' && sb[0] <= '9') sb.Insert(0, "Run");
            return sb.Length > MaxKeywordLength ? sb.ToString(0, MaxKeywordLength) : sb.ToString();
        }

        /// <summary>
        /// The workflows FTFRUN can offer, each with its keyword, in their saved order. One whose
        /// keyword is empty, is a built-in choice, or repeats an earlier one is left out (Validate
        /// reports it).
        /// </summary>
        public static IList<KeyValuePair<string, WorkflowDefinition>> Choices(IEnumerable<WorkflowDefinition> workflows)
        {
            var taken = new HashSet<string>(BuiltInKeywords, StringComparer.OrdinalIgnoreCase);
            var choices = new List<KeyValuePair<string, WorkflowDefinition>>();
            foreach (var w in workflows ?? Enumerable.Empty<WorkflowDefinition>())
            {
                if (w == null) continue;
                var keyword = Keyword(w.Name);
                if (keyword.Length == 0 || !taken.Add(keyword)) continue;
                choices.Add(new KeyValuePair<string, WorkflowDefinition>(keyword, w));
            }
            return choices;
        }

        /// <summary>The workflow with this name or keyword, or null.</summary>
        public WorkflowDefinition Find(string nameOrKeyword)
        {
            if (string.IsNullOrWhiteSpace(nameOrKeyword) || Items == null) return null;
            var wanted = nameOrKeyword.Trim();
            return Items.FirstOrDefault(w => w != null && string.Equals((w.Name ?? string.Empty).Trim(), wanted, StringComparison.OrdinalIgnoreCase))
                ?? Choices(Items).Where(c => string.Equals(c.Key, wanted, StringComparison.OrdinalIgnoreCase))
                                 .Select(c => c.Value).FirstOrDefault();
        }

        /// <summary>
        /// Puts every workflow's stages in a runnable form (<see cref="ProcessPresets.Normalize"/>:
        /// known stages, once each, in pipeline order, draw order last when anything it bands
        /// runs) and drops repeated families and follow-up commands. Returns what changed, in words.
        /// </summary>
        public IList<string> Normalize()
        {
            var notes = new List<string>();
            Tidy();
            foreach (var w in Items)
            {
                var adjustments = new List<string>();
                w.Stages = ProcessPresets.Normalize(w.Stages, adjustments).ToList();
                w.Families = w.Families.Distinct().ToList();
                w.ThenRun = w.ThenRun.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim().ToUpperInvariant())
                                     .Distinct().ToList();
                foreach (var a in adjustments) notes.Add((w.Name ?? string.Empty).Trim() + ": " + a);
            }
            return notes;
        }

        public void Validate(ICollection<string> problems)
        {
            Tidy();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var keywords = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var builtInTitles = new[] { ProcessPreset.StandardTopographic, ProcessPreset.TreesAndVegetation, ProcessPreset.AnnotationsOnly, ProcessPreset.Custom }
                .Select(p => ProcessPresets.Title(p)).ToList();

            foreach (var w in Items)
            {
                var name = (w.Name ?? string.Empty).Trim();
                if (name.Length == 0)
                {
                    problems.Add("Workflows: every workflow needs a name.");
                    continue;
                }
                var quoted = "\"" + name + "\"";

                if (!names.Add(name)) problems.Add("Workflows: two workflows are called " + quoted + ".");
                else if (builtInTitles.Contains(name, StringComparer.OrdinalIgnoreCase))
                    problems.Add("Workflows: " + quoted + " is the name of a built-in preset; give the workflow another name.");
                else
                {
                    var keyword = Keyword(name);
                    string other;
                    if (keyword.Length == 0)
                        problems.Add("Workflows: " + quoted + " needs a letter or digit in its name so FTFRUN can offer it.");
                    else if (BuiltInKeywords.Contains(keyword, StringComparer.OrdinalIgnoreCase))
                        problems.Add("Workflows: " + keyword + " is already one of FTFRUN's choices; give " + quoted + " another name.");
                    else if (keywords.TryGetValue(keyword, out other))
                        problems.Add("Workflows: " + quoted + " and \"" + other + "\" would both be typed as " + keyword + " at FTFRUN; rename one.");
                    else
                        keywords[keyword] = name;
                }

                var stages = w.Stages.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim().ToUpperInvariant()).ToList();
                foreach (var unknown in stages.Where(s => !ProcessStage.InOrder.Contains(s)).Distinct())
                    problems.Add("Workflows: " + quoted + " names " + unknown + ", which is not a processing stage.");
                if (!stages.Any(s => ProcessStage.InOrder.Contains(s)))
                    problems.Add("Workflows: " + quoted + " has no stages to run.");

                foreach (var command in w.ThenRun.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim().ToUpperInvariant()).Distinct())
                    if (!SettingsRegistry.WorkflowFollowUps.Contains(command))
                        problems.Add("Workflows: " + quoted + " cannot run " + command + " afterwards (only " +
                                     string.Join(", ", SettingsRegistry.WorkflowFollowUps.ToArray()) + " can).");
            }
        }

        /// <summary>No null workflows or lists, whatever a hand-edited file held.</summary>
        private void Tidy()
        {
            if (Items == null) Items = new List<WorkflowDefinition>();
            Items = Items.Where(w => w != null).ToList();
            foreach (var w in Items)
            {
                if (w.Stages == null) w.Stages = new List<string>();
                if (w.Families == null) w.Families = new List<AnnotationCategory>();
                if (w.ThenRun == null) w.ThenRun = new List<string>();
                if (w.Name == null) w.Name = string.Empty;
                if (w.Description == null) w.Description = string.Empty;
            }
        }
    }
}
