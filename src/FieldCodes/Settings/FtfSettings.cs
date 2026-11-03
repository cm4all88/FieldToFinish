using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FieldCodes.Settings
{
    /// <summary>The highest settings level that has a file (SettingsLevel lists the whole stack).</summary>
    public enum SettingsSource
    {
        /// <summary>No file found; shipped defaults are in use.</summary>
        Defaults = 0,
        /// <summary>The project file beside the drawing: the highest level.</summary>
        DrawingFolder = 1,
        /// <summary>The user's own defaults (%APPDATA%). Below profiles and project files.</summary>
        UserProfile = 2,
        /// <summary>A named project or client profile selected for the drawing.</summary>
        Profile = 4,
        /// <summary>The office standard in the plugin folder: the lowest file level.</summary>
        PluginFolder = 3
    }

    /// <summary>
    /// The settings in force for a drawing, and where each value came from. Settings are
    /// layered value by value -- built-in, office, my defaults, profile, project -- so
    /// Source and LoadedFrom name the highest level that has a file, Levels lists every
    /// level, and Provenance says, for each setting, which level supplied it.
    /// </summary>
    public sealed class SettingsResolution
    {
        private IDictionary<string, SettingProvenance> _provenance;

        public FtfSettings Settings { get; set; }

        /// <summary>The highest level that has a file.</summary>
        public SettingsSource Source { get; set; }

        /// <summary>That level's file, or null when defaults were used.</summary>
        public string LoadedFrom { get; set; }

        /// <summary>The drafting profile in force, when one was selected and found.</summary>
        public string ProfileName { get; set; }

        /// <summary>A profile the drawing selected that could not be found.</summary>
        public string MissingProfile { get; set; }

        /// <summary>True when the values came from the legacy keys in rules.json.</summary>
        public bool MigratedFromRules { get; set; }

        /// <summary>Every level, lowest first, indexed by (int)SettingsLevel: whether its file
        /// exists, where it is, and whether it is a complete (older, version 1) file.</summary>
        public IList<SettingsLevelInfo> Levels { get; set; }

        /// <summary>
        /// For every setting (dotted JSON path, e.g. "labels.textHeightPlotted"): the value
        /// in use, the level that supplied it, and the different values other levels hold.
        /// Built on first use; empty for a resolution not built from the levels.
        /// </summary>
        public IDictionary<string, SettingProvenance> Provenance
        {
            get
            {
                if (_provenance == null) _provenance = SettingsLayers.BuildProvenance(this);
                return _provenance;
            }
        }

        /// <summary>One level's details, or null when this resolution has no levels.</summary>
        public SettingsLevelInfo LevelInfo(SettingsLevel level)
        {
            var index = (int)level;
            return Levels != null && index >= 0 && index < Levels.Count ? Levels[index] : null;
        }

        /// <summary>The file a level reads and saves, or null when it has none.</summary>
        public string PathFor(SettingsLevel level)
        {
            var info = LevelInfo(level);
            return info == null ? null : info.Path;
        }

        // What the resolution was built from, for saving and refreshing.
        internal LayerInputs Inputs { get; set; }
        internal JObject BuiltInJson { get; set; }
        internal JObject EffectiveJson { get; set; }

        internal void ResetProvenance()
        {
            _provenance = null;
        }
    }

    /// <summary>
    /// How the Field to Finish commands behave. Deliberately separate from the rules
    /// file: rules.json is the office grammar standard and ships with the plugin, so a
    /// rebuild replaces it. Settings that a user edits must live somewhere a rebuild
    /// cannot reach.
    ///
    /// Nothing here interprets a field code. The grammar -- codes, species, modifiers,
    /// ignore list -- stays in rules.json and is read-only from the setup window.
    ///
    /// Adding a section for a new command is one class implementing ISettingsSection
    /// plus one property here. No other file needs to change.
    /// </summary>
    public sealed class FtfSettings
    {
        public const string FileName = "ftf-settings.json";

        /// <summary>Schema version of this file, not the rules version.</summary>
        [JsonProperty("version")]
        public string Version { get; set; }

        [JsonProperty("general")] public GeneralSettings General { get; set; }
        [JsonProperty("trees")] public TreeSettings Trees { get; set; }
        [JsonProperty("drip")] public DripSettings Drip { get; set; }
        [JsonProperty("labels")] public LabelSettings Labels { get; set; }
        [JsonProperty("lineLabels")] public LineLabelSettings LineLabels { get; set; }
        [JsonProperty("drafting")] public DraftingSettings Drafting { get; set; }
        [JsonProperty("tags")] public TagSettings Tags { get; set; }
        [JsonProperty("drawOrder")] public DrawOrderSettings DrawOrder { get; set; }
        [JsonProperty("spots")] public SpotSettings Spots { get; set; }
        [JsonProperty("contours")] public ContourSettings Contours { get; set; }
        [JsonProperty("sheets")] public SheetSettings Sheets { get; set; }
        [JsonProperty("dips")] public UtilitySettings Dips { get; set; }
        [JsonProperty("easements")] public EasementSettings Easements { get; set; }
        [JsonProperty("rightOfWay")] public RightOfWaySettings RightOfWay { get; set; }
        [JsonProperty("lots")] public LotSettings Lots { get; set; }
        [JsonProperty("exhibits")] public ExhibitSettings Exhibits { get; set; }
        [JsonProperty("recordSurvey")] public RecordSurveySettings RecordSurvey { get; set; }
        [JsonProperty("cleanup")] public CleanupSettings Cleanup { get; set; }
        [JsonProperty("compare")] public CompareSettings Compare { get; set; }
        [JsonProperty("annotation")] public AnnotationSettings Annotation { get; set; }
        [JsonProperty("diagnostics")] public DiagnosticsSettings Diagnostics { get; set; }
        [JsonProperty("styles")] public FieldCodes.Styles.StyleSettings Styles { get; set; }
        [JsonProperty("workflows")] public WorkflowSettings Workflows { get; set; }
        [JsonProperty("layerStandards")] public LayerStandardsSettings LayerStandards { get; set; }
        [JsonProperty("gates")] public GateSettings Gates { get; set; }
        [JsonProperty("review")] public ReviewSettings Review { get; set; }
        [JsonProperty("accountability")] public AccountabilitySettings Accountability { get; set; }

        /// <summary>The resolution this object was resolved into, so a save to one of its
        /// level files writes that level's differences rather than a complete copy.</summary>
        [JsonIgnore]
        internal SettingsResolution Origin { get; set; }

        public FtfSettings()
        {
            Version = "1";
            General = new GeneralSettings();
            Trees = new TreeSettings();
            Drip = new DripSettings();
            Labels = new LabelSettings();
            LineLabels = new LineLabelSettings();
            Drafting = new DraftingSettings();
            Tags = new TagSettings();
            DrawOrder = new DrawOrderSettings();
            Spots = new SpotSettings();
            Contours = new ContourSettings();
            Sheets = new SheetSettings();
            Dips = new UtilitySettings();
            Easements = new EasementSettings();
            RightOfWay = new RightOfWaySettings();
            Lots = new LotSettings();
            Exhibits = new ExhibitSettings();
            RecordSurvey = new RecordSurveySettings();
            Cleanup = new CleanupSettings();
            Compare = new CompareSettings();
            Annotation = new AnnotationSettings();
            Diagnostics = new DiagnosticsSettings();
            Styles = new FieldCodes.Styles.StyleSettings();
            Workflows = new WorkflowSettings();
            LayerStandards = new LayerStandardsSettings();
            Gates = new GateSettings();
            Review = new ReviewSettings();
            Accountability = new AccountabilitySettings();
        }

        /// <summary>Every section, in the order the setup window shows them.</summary>
        [JsonIgnore]
        public IList<ISettingsSection> Sections
        {
            get
            {
                return new List<ISettingsSection>
                {
                    General, Trees, Drip, Labels, LineLabels, Drafting, Tags, DrawOrder,
                    Spots, Contours, Sheets, Dips, Easements, RightOfWay, Lots, Exhibits, RecordSurvey, Cleanup, Compare, Annotation,
                    Styles, Workflows, LayerStandards, Gates, Review, Accountability
                };
            }
        }

        // ------------------------------------------------------------------ loading

        /// <summary>
        /// The settings files, highest priority first: beside the drawing (project), the
        /// user's own defaults, then the plugin folder (office standard). A selected
        /// profile sits between the project and the user's defaults. Every one of them
        /// that exists is merged, value by value (SettingsLayers).
        /// </summary>
        public static IList<string> CandidatePaths(string drawingDirectory, string pluginDirectory)
        {
            var paths = new List<string>();

            if (!string.IsNullOrWhiteSpace(drawingDirectory))
                paths.Add(Path.Combine(drawingDirectory, FileName));

            paths.Add(UserProfilePath());

            if (!string.IsNullOrWhiteSpace(pluginDirectory))
                paths.Add(Path.Combine(pluginDirectory, FileName));

            return paths;
        }

        /// <summary>Test hook: redirects the user-profile location so resolution
        /// tests stay hermetic on a machine where FTF has really been used -- a
        /// genuine %APPDATA% settings file would otherwise satisfy the middle of
        /// the search chain and fail them. Always null in production.</summary>
        internal static string ProfileDirectoryOverride;

        public static string UserProfilePath()
        {
            var dir = ProfileDirectoryOverride ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "FieldToFinish");
            return Path.Combine(dir, FileName);
        }

        // --------------------------------------------------------------- profiles

        /// <summary>
        /// Named drafting profiles -- "Parametrix Standard", "WSDOT", a client or a
        /// single project. Each is a settings file in the profiles folder holding the
        /// values that profile changes; no name is built in or mandatory. A drawing
        /// selects one, and every FTF tool then drafts to it, with the project file
        /// beside the drawing still on top of it.
        /// </summary>
        public static string ProfilesDirectory()
        {
            return Path.Combine(Path.GetDirectoryName(UserProfilePath()), "profiles");
        }

        public static string ProfilePath(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            var safe = new string(name.Trim().Select(ch =>
                Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch).ToArray());
            return Path.Combine(ProfilesDirectory(), safe + ".json");
        }

        public static IList<string> ListProfiles()
        {
            var dir = ProfilesDirectory();
            if (!Directory.Exists(dir)) return new List<string>();
            return Directory.GetFiles(dir, "*.json")
                .Select(Path.GetFileNameWithoutExtension)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static bool ProfileExists(string name)
        {
            var path = ProfilePath(name);
            return path != null && SafeExists(path);
        }

        /// <summary>
        /// Writes a profile. Settings that came from a resolution are saved as the values
        /// that differ from the office standard and the saver's own defaults (an override
        /// file, SettingsLayers.SaveProfile); a settings object built in code is written
        /// whole, as before.
        /// </summary>
        public static void SaveProfile(string name, FtfSettings settings)
        {
            var path = ProfilePath(name);
            if (path == null) throw new ArgumentException("A profile needs a name.");
            if (settings == null) throw new ArgumentNullException("settings");
            Directory.CreateDirectory(ProfilesDirectory());

            if (settings.Origin != null && settings.Origin.Levels != null)
            {
                SettingsLayers.SaveProfile(settings.Origin, name, settings);
                return;
            }
            settings.Save(path);
        }

        /// <summary>
        /// Settings for a drawing: the built-in values, then the office standard (plugin
        /// folder), the user's own defaults, the drawing's selected profile and the project
        /// file beside the drawing, merged value by value -- a level that does not set a
        /// value inherits it (SettingsLayers). A selected profile that does not exist is
        /// skipped and reported in MissingProfile. A file that cannot be read is a
        /// ConfigException, never silently skipped.
        /// </summary>
        public static SettingsResolution Resolve(string drawingDirectory, string pluginDirectory,
                                                 RulesConfig rulesForMigration, string profileName)
        {
            return SettingsLayers.Resolve(drawingDirectory, pluginDirectory, rulesForMigration, profileName);
        }

        /// <summary>
        /// Resolution without a profile. When no settings file exists anywhere, the legacy
        /// keys in <paramref name="rulesForMigration"/> seed the result so an existing
        /// install keeps behaving the way it did before this file existed.
        /// </summary>
        public static SettingsResolution Resolve(string drawingDirectory, string pluginDirectory,
                                                 RulesConfig rulesForMigration)
        {
            return SettingsLayers.Resolve(drawingDirectory, pluginDirectory, rulesForMigration, null);
        }

        private static bool SafeExists(string path)
        {
            try { return File.Exists(path); }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        public static FtfSettings Load(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("Settings file not found.", path);

            FtfSettings settings;
            try
            {
                settings = JsonConvert.DeserializeObject<FtfSettings>(File.ReadAllText(path));
            }
            catch (JsonException ex)
            {
                throw new ConfigException("Settings file is not valid JSON: " + ex.Message, ex);
            }

            if (settings == null)
                throw new ConfigException("Settings file is empty.");

            // A file written by an older build may be missing whole sections.
            settings.FillMissingSections();
            return settings;
        }

        /// <summary>
        /// A settings file from an earlier version may not have every section. Missing
        /// ones get defaults rather than becoming null references at draw time.
        /// </summary>
        public void FillMissingSections()
        {
            if (General == null) General = new GeneralSettings();
            if (Trees == null) Trees = new TreeSettings();
            if (Drip == null) Drip = new DripSettings();
            if (Labels == null) Labels = new LabelSettings();
            if (LineLabels == null) LineLabels = new LineLabelSettings();
            if (Drafting == null) Drafting = new DraftingSettings();
            if (Drafting.LineTypes == null || Drafting.LineTypes.Count == 0)
                Drafting.RestoreDefaults();
            if (Tags == null) Tags = new TagSettings();
            if (DrawOrder == null) DrawOrder = new DrawOrderSettings();
            if (Spots == null) Spots = new SpotSettings();
            if (Contours == null) Contours = new ContourSettings();
            if (Sheets == null) Sheets = new SheetSettings();
            if (Dips == null) Dips = new UtilitySettings();
            if (Easements == null) Easements = new EasementSettings();
            if (RightOfWay == null) RightOfWay = new RightOfWaySettings();
            if (Lots == null) Lots = new LotSettings();
            if (Exhibits == null) Exhibits = new ExhibitSettings();
            if (RecordSurvey == null) RecordSurvey = new RecordSurveySettings();
            if (RecordSurvey.Entities == null || RecordSurvey.Entities.Count == 0 ||
                RecordSurvey.Monuments == null || RecordSurvey.Monuments.Count == 0)
            {
                // A settings file written before this section existed, or one with the lists
                // removed: the standards catalog is what makes the commands usable at all.
                var keep = RecordSurvey;
                var fresh = new RecordSurveySettings();
                if (keep.Entities == null || keep.Entities.Count == 0) keep.Entities = fresh.Entities;
                if (keep.Monuments == null || keep.Monuments.Count == 0) keep.Monuments = fresh.Monuments;
            }
            if (Cleanup == null) Cleanup = new CleanupSettings();
            if (Compare == null) Compare = new CompareSettings();
            if (Annotation == null) Annotation = new AnnotationSettings();
            if (Annotation.Categories == null) Annotation.Categories = new List<AnnotationCategory>(AnnotationSettings.DefaultCategories());
            if (Annotation.NoLabelWords == null) Annotation.NoLabelWords = new List<string>();
            if (Diagnostics == null) Diagnostics = new DiagnosticsSettings();
            if (Gates == null) Gates = new GateSettings();
            if (Gates.PostCodes == null || Gates.PostCodes.Count == 0) Gates.PostCodes = new List<string> { "FGP" };
            if (Review == null) Review = new ReviewSettings();
            if (Accountability == null) Accountability = new AccountabilitySettings();
            if (Accountability.Continuity == null) Accountability.Continuity = new ContinuitySettings();
            if (Review.Required == null) Review.Required = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            if (Styles == null) Styles = new FieldCodes.Styles.StyleSettings();
            Styles.FillMissing();
            if (Workflows == null) Workflows = new WorkflowSettings();
            if (LayerStandards == null) LayerStandards = new LayerStandardsSettings();
            if (LayerStandards.Layers == null) LayerStandards.Layers = new List<LayerStandard>();

            if (DrawOrder.ProtectedLayers == null) DrawOrder.ProtectedLayers = new List<string>();
            if (DrawOrder.MaskableLayers == null) DrawOrder.MaskableLayers = new List<string>();
            if (Tags.TableColumns == null || Tags.TableColumns.Count == 0)
                Tags.RestoreDefaults();
            if (string.IsNullOrEmpty(Version)) Version = "1";

            // Layers an earlier build shipped that are not in the PMX template.
            PmxLayers.Upgrade(this);
        }

        // ------------------------------------------------------------------ saving

        /// <summary>
        /// Validates, then writes. An invalid setting is never written, so a bad value
        /// cannot be saved and then fail to load on the next command.
        ///
        /// When this object came from a resolution and <paramref name="path"/> is one of its
        /// level files (project, profile, my defaults, office), the save goes through
        /// SettingsLayers.SaveChanges: what changed is saved to that level, and the file
        /// keeps only its real differences. Any other path gets a complete copy (an export).
        /// </summary>
        public void Save(string path)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentNullException("path");

            var origin = Origin;
            if (origin != null && origin.Levels != null)
            {
                var level = SettingsLayers.LevelOfPath(origin, path);
                if (level.HasValue)
                {
                    SettingsLayers.SaveChanges(origin, level.Value, this);
                    return;
                }
            }

            var problems = new List<string>();
            Validate(problems);
            if (problems.Count > 0)
                throw new ConfigException("Settings are not valid:" + Environment.NewLine +
                                          string.Join(Environment.NewLine, problems.ToArray()));

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            if (File.Exists(path))
                File.Copy(path, path + ".bak", true);

            File.WriteAllText(path, JsonConvert.SerializeObject(this, Formatting.Indented));
        }

        public void Validate(ICollection<string> problems)
        {
            FillMissingSections();
            foreach (var section in Sections) section.Validate(problems);
        }

        public void RestoreDefaults()
        {
            foreach (var section in Sections) section.RestoreDefaults();
        }

        // --------------------------------------------------------------- migration

        /// <summary>
        /// Seeds from the legacy keys that used to live in rules.json, so upgrading
        /// does not silently change how anyone's drawings come out.
        /// </summary>
        public void MigrateFrom(RulesConfig rules)
        {
            if (rules == null) return;

            if (rules.UnitsPerFoot > 0) General.UnitsPerFoot = rules.UnitsPerFoot;

            Trees.TrunkDecimals = rules.TrunkDecimals;
            Trees.MultiStemAverage = rules.MultiStemAverage;

            if (rules.ProtectedLayers != null && rules.ProtectedLayers.Count > 0)
                DrawOrder.ProtectedLayers = new List<string>(rules.ProtectedLayers);

            if (rules.MaskableLayers != null && rules.MaskableLayers.Count > 0)
                DrawOrder.MaskableLayers = new List<string>(rules.MaskableLayers);

            var lp = rules.LabelPlacement;
            if (lp != null)
            {
                if (lp.TextHeightPlotted > 0) Labels.TextHeightPlotted = lp.TextHeightPlotted;
                if (lp.PaddingPlotted >= 0) Labels.PaddingPlotted = lp.PaddingPlotted;
                if (lp.BaseOffsetPlotted >= 0) Labels.BaseOffsetPlotted = lp.BaseOffsetPlotted;
                if (lp.RingStepPlotted > 0) Labels.RingStepPlotted = lp.RingStepPlotted;
                if (lp.RingCount >= 1) Labels.RingCount = lp.RingCount;
                if (lp.MovedTolerance >= 0) Cleanup.MovedTolerance = lp.MovedTolerance;
                // characterWidthFactor is deliberately not carried over: label boxes are
                // measured from the real text now, so the estimate has no successor.
            }
        }

        /// <summary>
        /// Pushes the values the parser and layer classifier read onto the rules
        /// object. Settings are the authority; the matching keys in rules.json are only
        /// ever a seed for a first-time migration.
        /// </summary>
        public void ApplyTo(RulesConfig rules)
        {
            if (rules == null) return;

            rules.UnitsPerFoot = General.UnitsPerFoot;
            rules.TrunkDecimals = Trees.TrunkDecimals;
            rules.MultiStemAverage = Trees.MultiStemAverage;
            rules.ProtectedLayers = new List<string>(DrawOrder.ProtectedLayers ?? new List<string>());
            rules.MaskableLayers = new List<string>(DrawOrder.MaskableLayers ?? new List<string>());
            rules.ActiveLayerMappings = new List<LayerMapping>(General.LayerMappings ?? new List<LayerMapping>());
            if (Diagnostics != null) Diagnostics.ApplyTo(rules);
        }
    }
}

