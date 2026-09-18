using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace FieldCodes.Settings
{
    /// <summary>Which of the searched locations a settings file came from.</summary>
    public enum SettingsSource
    {
        /// <summary>No file found; shipped defaults are in use.</summary>
        Defaults = 0,
        /// <summary>Next to the drawing. Overrides everything else.</summary>
        DrawingFolder = 1,
        /// <summary>The user's roaming profile. Applies to every drawing.</summary>
        UserProfile = 2,
        /// <summary>A named drafting profile selected for the drawing.</summary>
        Profile = 4,
        /// <summary>Shipped with the plugin. Read-only in practice: a rebuild replaces it.</summary>
        PluginFolder = 3
    }

    /// <summary>Where the settings came from and where a save would go.</summary>
    public sealed class SettingsResolution
    {
        public FtfSettings Settings { get; set; }
        public SettingsSource Source { get; set; }

        /// <summary>File the settings were read from, or null when defaults were used.</summary>
        public string LoadedFrom { get; set; }

        /// <summary>The drafting profile in force, when one was selected and found.</summary>
        public string ProfileName { get; set; }

        /// <summary>A profile the drawing selected that could not be found.</summary>
        public string MissingProfile { get; set; }

        /// <summary>True when the values came from the legacy keys in rules.json.</summary>
        public bool MigratedFromRules { get; set; }
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
        [JsonProperty("sheets")] public SheetSettings Sheets { get; set; }
        [JsonProperty("dips")] public UtilitySettings Dips { get; set; }
        [JsonProperty("easements")] public EasementSettings Easements { get; set; }
        [JsonProperty("exhibits")] public ExhibitSettings Exhibits { get; set; }
        [JsonProperty("cleanup")] public CleanupSettings Cleanup { get; set; }

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
            Sheets = new SheetSettings();
            Dips = new UtilitySettings();
            Easements = new EasementSettings();
            Exhibits = new ExhibitSettings();
            Cleanup = new CleanupSettings();
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
                    Spots, Sheets, Dips, Easements, Exhibits, Cleanup
                };
            }
        }

        // ------------------------------------------------------------------ loading

        /// <summary>
        /// Locations searched, highest priority first: beside the drawing, then the
        /// user's profile, then the plugin folder.
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
        /// single project. Each is a complete settings file in the profiles folder;
        /// no name is built in or mandatory. A drawing selects one, and every FTF
        /// tool then drafts to it.
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

        public static void SaveProfile(string name, FtfSettings settings)
        {
            var path = ProfilePath(name);
            if (path == null) throw new ArgumentException("A profile needs a name.");
            Directory.CreateDirectory(ProfilesDirectory());
            settings.Save(path);
        }

        /// <summary>
        /// Resolution with a drawing's selected profile: settings beside the drawing
        /// (a project override) still win; then the selected profile; then the
        /// user's own settings, the plugin copy and defaults as before. A selected
        /// profile that no longer exists is skipped, and the result says so.
        /// </summary>
        public static SettingsResolution Resolve(string drawingDirectory, string pluginDirectory,
                                                 RulesConfig rulesForMigration, string profileName)
        {
            if (!string.IsNullOrWhiteSpace(profileName))
            {
                var besideDrawing = string.IsNullOrWhiteSpace(drawingDirectory)
                    ? null : Path.Combine(drawingDirectory, FileName);
                if (besideDrawing == null || !SafeExists(besideDrawing))
                {
                    var profilePath = ProfilePath(profileName);
                    if (profilePath != null && SafeExists(profilePath))
                        return new SettingsResolution
                        {
                            Settings = Load(profilePath),
                            Source = SettingsSource.Profile,
                            LoadedFrom = profilePath,
                            ProfileName = profileName
                        };
                }
            }

            var resolved = Resolve(drawingDirectory, pluginDirectory, rulesForMigration);
            if (!string.IsNullOrWhiteSpace(profileName) && resolved.Source != SettingsSource.DrawingFolder)
                resolved.MissingProfile = profileName;
            return resolved;
        }

        /// <summary>
        /// Finds and loads settings. When no file exists anywhere, the legacy keys in
        /// <paramref name="rulesForMigration"/> seed the result so an existing install
        /// keeps behaving the way it did before this file existed.
        /// </summary>
        public static SettingsResolution Resolve(string drawingDirectory, string pluginDirectory,
                                                 RulesConfig rulesForMigration)
        {
            var candidates = CandidatePaths(drawingDirectory, pluginDirectory);

            for (var i = 0; i < candidates.Count; i++)
            {
                var path = candidates[i];
                if (!SafeExists(path)) continue;

                return new SettingsResolution
                {
                    Settings = Load(path),
                    Source = SourceOf(i, drawingDirectory),
                    LoadedFrom = path
                };
            }

            var seeded = new FtfSettings();
            var migrated = false;
            if (rulesForMigration != null)
            {
                seeded.MigrateFrom(rulesForMigration);
                migrated = true;
            }

            return new SettingsResolution
            {
                Settings = seeded,
                Source = SettingsSource.Defaults,
                LoadedFrom = null,
                MigratedFromRules = migrated
            };
        }

        private static SettingsSource SourceOf(int index, string drawingDirectory)
        {
            var haveDrawing = !string.IsNullOrWhiteSpace(drawingDirectory);
            if (haveDrawing && index == 0) return SettingsSource.DrawingFolder;
            return (haveDrawing ? index : index + 1) == 1
                ? SettingsSource.UserProfile
                : SettingsSource.PluginFolder;
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
            if (Sheets == null) Sheets = new SheetSettings();
            if (Dips == null) Dips = new UtilitySettings();
            if (Easements == null) Easements = new EasementSettings();
            if (Exhibits == null) Exhibits = new ExhibitSettings();
            if (Cleanup == null) Cleanup = new CleanupSettings();

            if (DrawOrder.ProtectedLayers == null) DrawOrder.ProtectedLayers = new List<string>();
            if (DrawOrder.MaskableLayers == null) DrawOrder.MaskableLayers = new List<string>();
            if (Tags.TableColumns == null || Tags.TableColumns.Count == 0)
                Tags.RestoreDefaults();
            if (string.IsNullOrEmpty(Version)) Version = "1";
        }

        // ------------------------------------------------------------------ saving

        /// <summary>
        /// Validates, then writes. An invalid setting is never written, so a bad value
        /// cannot be saved and then fail to load on the next command.
        /// </summary>
        public void Save(string path)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentNullException("path");

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
        }
    }
}

