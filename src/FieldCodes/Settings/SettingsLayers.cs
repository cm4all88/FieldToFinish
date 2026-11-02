using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace FieldCodes.Settings
{
    /// <summary>
    /// The configuration levels, lowest priority first. A value set at a higher level wins
    /// over the same value at every lower level; a value a level does not set is inherited.
    /// </summary>
    public enum SettingsLevel
    {
        /// <summary>The values compiled into FTF.</summary>
        BuiltIn = 0,
        /// <summary>The office drafting standard: ftf-settings.json in the plugin folder.</summary>
        Office = 1,
        /// <summary>The person's own defaults: %APPDATA%\FieldToFinish\ftf-settings.json. Below
        /// profiles and projects, so a personal default never overrides a project standard.</summary>
        User = 2,
        /// <summary>The project or client profile the drawing selects (profiles\NAME.json).</summary>
        Profile = 3,
        /// <summary>ftf-settings.json beside the drawing: this survey project's overrides.</summary>
        Project = 4
    }

    /// <summary>One level of the stack: where its file is and what state it is in.</summary>
    public sealed class SettingsLevelInfo
    {
        public SettingsLevel Level { get; internal set; }

        /// <summary>The file for this level; null for Built-in, for Profile when the drawing
        /// selects none, and for Project when the drawing has never been saved.</summary>
        public string Path { get; internal set; }

        public bool Exists { get; internal set; }

        /// <summary>The profile name, on the Profile level.</summary>
        public string ProfileName { get; internal set; }

        /// <summary>
        /// A file written before settings were layered ("version": "1"): it is a complete
        /// copy of every setting, so the levels below it are not used. Saving to its level
        /// once turns it into an override file that keeps only its real differences.
        /// </summary>
        public bool Complete { get; internal set; }

        /// <summary>A complete file at a higher level hides this one.</summary>
        public bool Shadowed { get; internal set; }

        /// <summary>How many settings the file holds.</summary>
        public int ValueCount { get; internal set; }

        /// <summary>On the Office level: the office file asks for a PIN before it is changed.</summary>
        public bool PinProtected { get { return OfficeAuthorization.HasPin(Admin); } }

        /// <summary>"Office", "My defaults", "Profile "WSDOT"", "Project".</summary>
        public string Name { get { return SettingsLayers.LevelName(Level, ProfileName); } }

        /// <summary>The settings in the file, without its version and admin sections.</summary>
        internal JObject Values { get; set; }

        internal JObject Admin { get; set; }
    }

    /// <summary>A value one level holds for a setting.</summary>
    public sealed class LevelValue
    {
        public SettingsLevel Level { get; internal set; }
        public string LevelName { get; internal set; }
        public JToken Value { get; internal set; }

        /// <summary>The level is hidden by a complete file above it, so its value is not used.</summary>
        public bool Shadowed { get; internal set; }

        public string ValueText { get { return SettingsLayers.Describe(Value); } }

        public override string ToString()
        {
            return LevelName + ": " + ValueText + (Shadowed ? " (not used)" : string.Empty);
        }
    }

    /// <summary>Where one setting's value comes from, and what the other levels say.</summary>
    public sealed class SettingProvenance
    {
        /// <summary>The JSON path, e.g. "labels.textHeightPlotted".</summary>
        public string Path { get; internal set; }

        /// <summary>The readable name, e.g. "Point Labels > Text height (plotted)".</summary>
        public string DisplayName { get; internal set; }

        /// <summary>The value FTF uses.</summary>
        public JToken Value { get; internal set; }

        public string ValueText { get { return SettingsLayers.Describe(Value); } }

        /// <summary>The level that supplies the value.</summary>
        public SettingsLevel From { get; internal set; }

        public string FromName { get; internal set; }

        /// <summary>Other levels that hold a different value, highest level first.</summary>
        public IList<LevelValue> Overridden { get; internal set; }

        /// <summary>The level the value would come from if <see cref="From"/> stopped setting it.</summary>
        public SettingsLevel InheritsFrom { get; internal set; }

        public string InheritsFromName { get; internal set; }

        /// <summary>The value it would then have.</summary>
        public JToken InheritedValue { get; internal set; }
    }

    /// <summary>What a save wrote.</summary>
    public sealed class SettingsSaveResult
    {
        public SettingsLevel Level { get; internal set; }
        public string LevelName { get; internal set; }
        public string Path { get; internal set; }

        /// <summary>Settings that differed from what was loaded.</summary>
        public int Changed { get; internal set; }

        /// <summary>Settings the level's file now holds.</summary>
        public int Stored { get; internal set; }

        /// <summary>Settings dropped from the file because they now equal what the level inherits.</summary>
        public int Removed { get; internal set; }

        /// <summary>True when this save set the office PIN.</summary>
        public bool PinSet { get; internal set; }

        /// <summary>Changes that were saved but are not in force because a higher level sets
        /// the same setting: "Point Labels > Ring count (Project sets 6)".</summary>
        public IList<string> Hidden { get; internal set; }

        public string Summary()
        {
            var text = string.Format(CultureInfo.InvariantCulture,
                "Saved to {0} ({1}): {2} change{3}; the file holds {4} setting{5}",
                LevelName, Path, Changed, Changed == 1 ? string.Empty : "s",
                Stored, Stored == 1 ? string.Empty : "s");
            if (Level != SettingsLevel.Office) text += " that differ from what it inherits";
            if (Removed > 0)
                text += string.Format(CultureInfo.InvariantCulture, "; {0} now inherited instead", Removed);
            if (PinSet) text += "; office PIN set";
            return text + ".";
        }
    }

    /// <summary>What a resolution was built from, kept so it can be rebuilt after a save.</summary>
    internal sealed class LayerInputs
    {
        public string DrawingDirectory;
        public string PluginDirectory;
        public string ProfileName;
        public RulesConfig Rules;

        public LayerInputs WithProfile(string profileName)
        {
            return new LayerInputs
            {
                DrawingDirectory = DrawingDirectory,
                PluginDirectory = PluginDirectory,
                ProfileName = profileName,
                Rules = Rules
            };
        }
    }

    /// <summary>
    /// One configuration system, five levels, merged value by value:
    ///
    ///     Built-in  &lt;  Office standard  &lt;  My defaults  &lt;  Profile  &lt;  Project
    ///
    /// Each file holds only the values its level changes. Objects merge key by key;
    /// lists and single values replace. The merged JSON is read into FtfSettings exactly
    /// as a single file used to be, so every command sees one complete settings object.
    ///
    /// Saving writes only the values that differ from what the level inherits, so a
    /// project file holds just the project's overrides and a later change to the office
    /// standard still reaches every project that does not override it. The office level
    /// is the exception: it is written whole, so the office standard reads as one
    /// complete document and does not move when a new build changes a built-in default.
    ///
    /// A file written before this existed ("version": "1") is a complete copy of every
    /// setting and keeps its old meaning: it hides the levels below it. Saving to its
    /// level once rewrites it as an override file ("version": "2").
    /// </summary>
    public static class SettingsLayers
    {
        public const string VersionKey = "version";

        /// <summary>The version an override file is written with.</summary>
        public const string LayeredVersion = "2";

        // ------------------------------------------------------------------ names

        public static string LevelName(SettingsLevel level, string profileName)
        {
            switch (level)
            {
                case SettingsLevel.Office: return "Office";
                case SettingsLevel.User: return "My defaults";
                case SettingsLevel.Profile:
                    return string.IsNullOrWhiteSpace(profileName) ? "Profile" : "Profile \"" + profileName + "\"";
                case SettingsLevel.Project: return "Project";
                default: return "Built-in";
            }
        }

        /// <summary>The level a settings file belongs to, or null when the path is not one of
        /// the resolution's levels.</summary>
        public static SettingsLevel? LevelOfPath(SettingsResolution resolution, string path)
        {
            if (resolution == null || resolution.Levels == null || string.IsNullOrWhiteSpace(path)) return null;
            var wanted = FullPath(path);
            foreach (var level in resolution.Levels)
                if (level.Level != SettingsLevel.BuiltIn && level.Path != null &&
                    string.Equals(FullPath(level.Path), wanted, StringComparison.OrdinalIgnoreCase))
                    return level.Level;
            return null;
        }

        // ------------------------------------------------------------- resolving

        /// <summary>Reads every level and merges them. See the class summary.</summary>
        public static SettingsResolution Resolve(string drawingDirectory, string pluginDirectory,
                                                 RulesConfig rulesForMigration, string profileName)
        {
            var resolution = new SettingsResolution();
            Populate(resolution, new LayerInputs
            {
                DrawingDirectory = drawingDirectory,
                PluginDirectory = pluginDirectory,
                ProfileName = string.IsNullOrWhiteSpace(profileName) ? null : profileName,
                Rules = rulesForMigration
            });
            return resolution;
        }

        /// <summary>
        /// Reads the files again and updates the resolution in place -- including its
        /// Settings object, whose sections are replaced with the values now in force, so
        /// anything holding the object sees them. Anything holding a section object
        /// should reload from the settings afterwards.
        /// </summary>
        public static void Refresh(SettingsResolution resolution)
        {
            if (resolution == null) throw new ArgumentNullException("resolution");
            if (resolution.Inputs == null) throw new InvalidOperationException("This resolution was not built from the settings levels.");

            var kept = resolution.Settings;
            Populate(resolution, resolution.Inputs);
            if (kept != null && !ReferenceEquals(kept, resolution.Settings))
            {
                CopySections(resolution.Settings, kept);
                resolution.Settings = kept;
                kept.Origin = resolution;
            }
        }

        /// <summary>Last-write times of every level file, so a cache can tell a file changed.</summary>
        public static string FileStamp(string drawingDirectory, string pluginDirectory, string profileName)
        {
            var sb = new StringBuilder();
            foreach (var path in new[]
            {
                string.IsNullOrWhiteSpace(pluginDirectory) ? null : Path.Combine(pluginDirectory, FtfSettings.FileName),
                FtfSettings.UserProfilePath(),
                string.IsNullOrWhiteSpace(profileName) ? null : FtfSettings.ProfilePath(profileName),
                string.IsNullOrWhiteSpace(drawingDirectory) ? null : Path.Combine(drawingDirectory, FtfSettings.FileName)
            })
            {
                long ticks = 0;
                if (path != null)
                {
                    try { if (File.Exists(path)) ticks = File.GetLastWriteTimeUtc(path).Ticks; }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
                sb.Append(ticks.ToString(CultureInfo.InvariantCulture)).Append('|');
            }
            return sb.ToString();
        }

        internal static void Populate(SettingsResolution r, LayerInputs inputs)
        {
            var levels = ReadLevels(inputs);
            var anyFile = levels.Any(l => l.Level != SettingsLevel.BuiltIn && l.Exists);

            // With no settings file anywhere, the legacy keys in rules.json seed the built-in
            // values, exactly as before levels existed. The first save writes them to a file.
            var migrated = !anyFile && inputs.Rules != null;
            var builtIn = migrated ? MigratedBuiltIn(inputs.Rules) : PureBuiltIn();

            var settings = Materialize(MergeLevels(levels, builtIn, SettingsLevel.Project, false), levels, builtIn);
            var top = levels.LastOrDefault(l => l.Level != SettingsLevel.BuiltIn && l.Exists);

            r.Settings = settings;
            settings.Origin = r;
            r.Source = top == null ? SettingsSource.Defaults : SourceOf(top.Level);
            r.LoadedFrom = top == null ? null : top.Path;
            var profile = levels[(int)SettingsLevel.Profile];
            r.ProfileName = profile.Exists ? inputs.ProfileName : null;
            r.MissingProfile = !string.IsNullOrWhiteSpace(inputs.ProfileName) && !profile.Exists ? inputs.ProfileName : null;
            r.MigratedFromRules = migrated;
            r.Levels = levels.AsReadOnly();
            r.Inputs = inputs;
            r.BuiltInJson = builtIn;
            r.EffectiveJson = ToJson(settings);
            r.ResetProvenance();
        }

        private static SettingsSource SourceOf(SettingsLevel level)
        {
            switch (level)
            {
                case SettingsLevel.Project: return SettingsSource.DrawingFolder;
                case SettingsLevel.Profile: return SettingsSource.Profile;
                case SettingsLevel.User: return SettingsSource.UserProfile;
                case SettingsLevel.Office: return SettingsSource.PluginFolder;
                default: return SettingsSource.Defaults;
            }
        }

        /// <summary>Always five entries, indexed by (int)SettingsLevel.</summary>
        internal static List<SettingsLevelInfo> ReadLevels(LayerInputs inputs)
        {
            var levels = new List<SettingsLevelInfo>
            {
                new SettingsLevelInfo { Level = SettingsLevel.BuiltIn, Exists = true },
                FileLevel(SettingsLevel.Office, string.IsNullOrWhiteSpace(inputs.PluginDirectory)
                    ? null : Path.Combine(inputs.PluginDirectory, FtfSettings.FileName), null),
                FileLevel(SettingsLevel.User, FtfSettings.UserProfilePath(), null),
                FileLevel(SettingsLevel.Profile, string.IsNullOrWhiteSpace(inputs.ProfileName)
                    ? null : FtfSettings.ProfilePath(inputs.ProfileName), inputs.ProfileName),
                FileLevel(SettingsLevel.Project, string.IsNullOrWhiteSpace(inputs.DrawingDirectory)
                    ? null : Path.Combine(inputs.DrawingDirectory, FtfSettings.FileName), null)
            };

            var completeAbove = false;
            for (var i = levels.Count - 1; i >= 1; i--)
            {
                levels[i].Shadowed = completeAbove;
                if (levels[i].Exists && levels[i].Complete) completeAbove = true;
            }
            return levels;
        }

        private static SettingsLevelInfo FileLevel(SettingsLevel level, string path, string profileName)
        {
            var info = new SettingsLevelInfo { Level = level, Path = path, ProfileName = profileName };
            if (path == null || !SafeExists(path)) return info;

            JObject raw = ReadFile(path, LevelName(level, profileName) + " settings file");
            info.Exists = true;
            info.Complete = IsComplete(raw.GetValue(VersionKey, StringComparison.OrdinalIgnoreCase));
            info.Admin = raw.GetValue(OfficeAuthorization.AdminKey, StringComparison.OrdinalIgnoreCase) as JObject;
            info.Values = StripMetadata(raw);
            // Settings only: an older complete file also carries captions (Title, AffectedCommands).
            info.ValueCount = Leaves(info.Values).Count(l => IsSetting(l.Key));
            return info;
        }

        /// <summary>A settings file as JSON. Unreadable content is reported, never skipped:
        /// silently falling back to another level would draft to the wrong standard.</summary>
        private static JObject ReadFile(string path, string what)
        {
            var text = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(text))
                throw new ConfigException("The " + what + " " + path + " is empty. Fix it or delete it.");

            JToken token;
            try { token = JToken.Parse(text); }
            catch (JsonException ex)
            {
                throw new ConfigException("The " + what + " " + path + " is not valid JSON: " + ex.Message +
                                          " Fix it or delete it.", ex);
            }

            var obj = token as JObject;
            if (obj == null)
                throw new ConfigException("The " + what + " " + path + " must hold a JSON object ({ ... }).");
            return obj;
        }

        /// <summary>True when the path names a setting FTF has (not a caption or an unknown key).</summary>
        private static bool IsSetting(string path)
        {
            var known = Lookup(PureBuiltInJson.Value, path);
            var obj = known as JObject;
            return known != null && (obj == null || !obj.HasValues);
        }

        private static bool IsComplete(JToken version)
        {
            if (version == null || version.Type == JTokenType.Null) return false;
            if (version.Type == JTokenType.Integer) return (long)version == 1;
            if (version.Type == JTokenType.Float) return Math.Abs((double)version - 1.0) < 1e-9;
            var text = ((string)version ?? string.Empty).Trim();
            return text == "1" || text == "1.0";
        }

        private static JObject StripMetadata(JObject raw)
        {
            var copy = (JObject)raw.DeepClone();
            foreach (var key in new[] { VersionKey, OfficeAuthorization.AdminKey })
            {
                var prop = copy.Property(key, StringComparison.OrdinalIgnoreCase);
                if (prop != null) prop.Remove();
            }
            return copy;
        }

        /// <summary>Built-in values, merged with every existing level up to and including
        /// <paramref name="upTo"/>. A complete file restarts the merge from the built-in values.
        /// <paramref name="skipUser"/> leaves out My defaults: the shared standard alone.</summary>
        internal static JObject MergeLevels(IList<SettingsLevelInfo> levels, JObject builtIn,
                                            SettingsLevel upTo, bool skipUser)
        {
            var top = (int)upTo;
            var start = 1;
            for (var i = top; i >= 1; i--)
            {
                var l = levels[i];
                if (skipUser && l.Level == SettingsLevel.User) continue;
                if (l.Exists && l.Complete) { start = i; break; }
            }

            var merged = (JObject)builtIn.DeepClone();
            for (var i = start; i <= top; i++)
            {
                var l = levels[i];
                if (!l.Exists || (skipUser && l.Level == SettingsLevel.User)) continue;
                MergeInto(merged, l.Values);
            }
            return merged;
        }

        /// <summary>
        /// Reads merged JSON into settings. When it cannot be read, each level is tried on
        /// its own so the message names the file with the bad value.
        /// </summary>
        private static FtfSettings Materialize(JObject merged, IList<SettingsLevelInfo> levels, JObject builtIn)
        {
            try { return FromJson(merged); }
            catch (ConfigException ex)
            {
                if (levels != null)
                    foreach (var l in levels.Where(x => x.Level != SettingsLevel.BuiltIn && x.Exists))
                    {
                        try { FromJson(Merge(builtIn, l.Values)); }
                        catch (ConfigException)
                        {
                            throw new ConfigException("The " + l.Name + " settings file " + l.Path +
                                                      " has a value FTF cannot read: " + ex.Message, ex);
                        }
                    }
                throw;
            }
        }

        // ---------------------------------------------------------------- JSON model

        private sealed class SettingsOnlyResolver : DefaultContractResolver
        {
            protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
            {
                var property = base.CreateProperty(member, memberSerialization);
                // Computed captions (a section's Title, AffectedCommands) are not settings:
                // they cannot be set, so no level can hold them.
                if (!property.Writable && property.PropertyType != null &&
                    (property.PropertyType == typeof(string) || property.PropertyType.IsValueType))
                    property.ShouldSerialize = o => false;
                return property;
            }
        }

        private static readonly JsonSerializer Serializer = JsonSerializer.Create(new JsonSerializerSettings
        {
            ContractResolver = new SettingsOnlyResolver(),
            // The merged JSON is always complete, so nothing is gained by reusing the lists a
            // constructor fills, and appending to them would double them.
            ObjectCreationHandling = ObjectCreationHandling.Replace
        });

        private static readonly Lazy<JObject> PureBuiltInJson = new Lazy<JObject>(() =>
        {
            var s = new FtfSettings();
            s.FillMissingSections();
            return ToJson(s);
        });

        /// <summary>The built-in values as JSON (a fresh copy).</summary>
        public static JObject PureBuiltIn()
        {
            return (JObject)PureBuiltInJson.Value.DeepClone();
        }

        private static JObject MigratedBuiltIn(RulesConfig rules)
        {
            var s = new FtfSettings();
            s.MigrateFrom(rules);
            s.FillMissingSections();
            return ToJson(s);
        }

        /// <summary>Settings as JSON, settings only: no version, no computed captions.</summary>
        public static JObject ToJson(FtfSettings settings)
        {
            if (settings == null) throw new ArgumentNullException("settings");
            var json = JObject.FromObject(settings, Serializer);
            var version = json.Property(VersionKey, StringComparison.OrdinalIgnoreCase);
            if (version != null) version.Remove();
            return json;
        }

        /// <summary>JSON into settings, missing sections filled the way a file load fills them.</summary>
        public static FtfSettings FromJson(JObject json)
        {
            FtfSettings settings;
            try { settings = json == null ? null : json.ToObject<FtfSettings>(Serializer); }
            catch (JsonException ex) { throw new ConfigException(ex.Message, ex); }
            catch (ArgumentException ex) { throw new ConfigException(ex.Message, ex); }
            catch (FormatException ex) { throw new ConfigException(ex.Message, ex); }
            catch (InvalidCastException ex) { throw new ConfigException(ex.Message, ex); }
            catch (OverflowException ex) { throw new ConfigException(ex.Message, ex); }
            if (settings == null) settings = new FtfSettings();
            settings.FillMissingSections();
            return settings;
        }

        /// <summary>A new object: <paramref name="lower"/> with <paramref name="higher"/> on top.</summary>
        public static JObject Merge(JObject lower, JObject higher)
        {
            var merged = lower == null ? new JObject() : (JObject)lower.DeepClone();
            if (higher != null) MergeInto(merged, higher);
            return merged;
        }

        /// <summary>Objects merge key by key (names matched without regard to case, the way the
        /// settings are read); lists and single values replace. A null never wipes out a
        /// whole section or list.</summary>
        private static void MergeInto(JObject target, JObject overlay)
        {
            foreach (var prop in overlay.Properties().ToList())
            {
                var existing = target.Property(prop.Name, StringComparison.OrdinalIgnoreCase);
                if (existing == null)
                {
                    target.Add(prop.Name, prop.Value.DeepClone());
                    continue;
                }

                if (prop.Value.Type == JTokenType.Null &&
                    (existing.Value.Type == JTokenType.Object || existing.Value.Type == JTokenType.Array))
                    continue;

                var into = existing.Value as JObject;
                var from = prop.Value as JObject;
                if (into != null && from != null) MergeInto(into, from);
                else existing.Value = prop.Value.DeepClone();
            }
        }

        /// <summary>Every setting in <paramref name="desired"/> whose value differs from
        /// <paramref name="below"/>, as a nested object.</summary>
        public static JObject Diff(JObject desired, JObject below)
        {
            var result = new JObject();
            if (desired == null) return result;
            foreach (var prop in desired.Properties())
            {
                var other = below == null ? null : below.GetValue(prop.Name, StringComparison.OrdinalIgnoreCase);
                var mine = prop.Value as JObject;
                var theirs = other as JObject;
                if (mine != null && mine.HasValues && theirs != null)
                {
                    var sub = Diff(mine, theirs);
                    if (sub.HasValues) result.Add(prop.Name, sub);
                }
                else if (other == null || !Same(prop.Value, other))
                {
                    result.Add(prop.Name, prop.Value.DeepClone());
                }
            }
            return result;
        }

        /// <summary>Each setting's dotted path and value. Lists are single settings.</summary>
        public static IEnumerable<KeyValuePair<string, JToken>> Leaves(JObject root)
        {
            return Leaves(root, null);
        }

        private static IEnumerable<KeyValuePair<string, JToken>> Leaves(JObject root, string prefix)
        {
            if (root == null) yield break;
            foreach (var prop in root.Properties())
            {
                var path = prefix == null ? prop.Name : prefix + "." + prop.Name;
                var child = prop.Value as JObject;
                if (child != null && child.HasValues)
                {
                    foreach (var leaf in Leaves(child, path)) yield return leaf;
                }
                else
                {
                    yield return new KeyValuePair<string, JToken>(path, prop.Value);
                }
            }
        }

        /// <summary>The value at a dotted path, or null.</summary>
        public static JToken Lookup(JObject root, string path)
        {
            if (root == null || string.IsNullOrEmpty(path)) return null;
            JToken current = root;
            foreach (var segment in path.Split('.'))
            {
                var obj = current as JObject;
                if (obj == null) return null;
                current = obj.GetValue(segment, StringComparison.OrdinalIgnoreCase);
                if (current == null) return null;
            }
            return current;
        }

        private static void SetPath(JObject root, string path, JToken value)
        {
            var segments = path.Split('.');
            var current = root;
            for (var i = 0; i < segments.Length - 1; i++)
            {
                var prop = current.Property(segments[i], StringComparison.OrdinalIgnoreCase);
                var next = prop == null ? null : prop.Value as JObject;
                if (next == null)
                {
                    next = new JObject();
                    if (prop == null) current.Add(segments[i], next);
                    else prop.Value = next;
                }
                current = next;
            }

            var last = current.Property(segments[segments.Length - 1], StringComparison.OrdinalIgnoreCase);
            if (last == null) current.Add(segments[segments.Length - 1], value.DeepClone());
            else last.Value = value.DeepClone();
        }

        /// <summary>Removes a dotted path, and any object left empty by it.</summary>
        private static bool RemovePath(JObject root, string path)
        {
            var segments = path.Split('.');
            var chain = new List<JObject> { root };
            var current = root;
            for (var i = 0; i < segments.Length - 1; i++)
            {
                current = current.GetValue(segments[i], StringComparison.OrdinalIgnoreCase) as JObject;
                if (current == null) return false;
                chain.Add(current);
            }

            var prop = current.Property(segments[segments.Length - 1], StringComparison.OrdinalIgnoreCase);
            if (prop == null) return false;
            prop.Remove();

            for (var i = chain.Count - 1; i >= 1; i--)
            {
                if (chain[i].HasValues) break;
                var parent = chain[i].Parent as JProperty;
                if (parent != null) parent.Remove();
            }
            return true;
        }

        private static bool Same(JToken a, JToken b)
        {
            if (a == null || b == null) return a == null && b == null;
            return JToken.DeepEquals(a, b);
        }

        // ---------------------------------------------------------------- provenance

        internal static IDictionary<string, SettingProvenance> BuildProvenance(SettingsResolution r)
        {
            var result = new Dictionary<string, SettingProvenance>(StringComparer.Ordinal);
            if (r == null || r.Levels == null || r.EffectiveJson == null) return result;

            var levels = r.Levels;
            var builtIn = r.BuiltInJson ?? PureBuiltIn();

            // Each file read through the settings model on its own, so a value shows the way
            // FTF reads it (4 and 4.0 are the same height) rather than as typed.
            var views = new JObject[levels.Count];
            for (var i = 1; i < levels.Count; i++)
            {
                if (!levels[i].Exists) continue;
                try { views[i] = ToJson(FromJson(Merge(builtIn, levels[i].Values))); }
                catch (ConfigException) { views[i] = null; }
            }

            foreach (var leaf in Leaves(r.EffectiveJson))
            {
                var path = leaf.Key;
                var from = SettingsLevel.BuiltIn;
                var held = new List<LevelValue>();

                for (var i = 1; i < levels.Count; i++)
                {
                    var l = levels[i];
                    if (!l.Exists) continue;
                    var raw = Lookup(l.Values, path);
                    if (raw == null) continue;
                    var shown = (views[i] != null ? Lookup(views[i], path) : null) ?? raw;
                    held.Add(new LevelValue { Level = l.Level, LevelName = l.Name, Value = shown, Shadowed = l.Shadowed });
                    if (!l.Shadowed) from = l.Level;
                }

                var builtInValue = Lookup(builtIn, path);
                var overridden = held
                    .Where(v => v.Level != from && !Same(v.Value, leaf.Value))
                    .OrderByDescending(v => (int)v.Level)
                    .ToList();
                if (from != SettingsLevel.BuiltIn && builtInValue != null && !Same(builtInValue, leaf.Value))
                    overridden.Add(new LevelValue
                    {
                        Level = SettingsLevel.BuiltIn,
                        LevelName = LevelName(SettingsLevel.BuiltIn, null),
                        Value = builtInValue
                    });

                // Where the value would come from if the supplying level stopped setting it.
                var inheritsFrom = SettingsLevel.BuiltIn;
                var inherited = builtInValue;
                if (from == SettingsLevel.BuiltIn) inherited = leaf.Value;
                for (var i = (int)from - 1; i >= 1; i--)
                {
                    var l = levels[i];
                    if (!l.Exists) continue;
                    var raw = Lookup(l.Values, path);
                    if (raw != null)
                    {
                        inheritsFrom = l.Level;
                        inherited = (views[i] != null ? Lookup(views[i], path) : null) ?? raw;
                        break;
                    }
                    if (l.Complete) break;
                }

                result[path] = new SettingProvenance
                {
                    Path = path,
                    DisplayName = Humanize(path),
                    Value = leaf.Value,
                    From = from,
                    FromName = levels[(int)from].Name,
                    Overridden = overridden,
                    InheritsFrom = inheritsFrom,
                    InheritsFromName = levels[(int)inheritsFrom].Name,
                    InheritedValue = inherited
                };
            }
            return result;
        }

        // ------------------------------------------------------------------ saving

        /// <summary>
        /// Saves what was changed in <paramref name="edited"/> (compared with the values the
        /// resolution loaded) to one level, then refreshes the resolution in place.
        ///
        /// Project, profile and personal files keep only the settings that differ from what
        /// the level inherits: the values changed now plus the ones the file already held.
        /// A value that has become equal to the inherited one is dropped -- except that a
        /// project or profile value equal to the inherited one only because of the saver's
        /// own defaults is kept, so the project standard never depends on whose computer
        /// saved it. The office file is written whole.
        ///
        /// Lower-level saves cannot leak higher-level values: only the changes go down, so
        /// saving to the office standard from a drawing with a project file does not copy the
        /// project's overrides into the office standard.
        ///
        /// The office level needs <paramref name="officePin"/> when the office file has a
        /// PIN (<see cref="OfficeAuthorizationException"/> otherwise);
        /// <paramref name="newOfficePin"/> sets one when it has none.
        /// </summary>
        public static SettingsSaveResult SaveChanges(SettingsResolution resolution, SettingsLevel level,
                                                     FtfSettings edited, string officePin = null,
                                                     string newOfficePin = null)
        {
            if (resolution == null) throw new ArgumentNullException("resolution");
            if (edited == null) throw new ArgumentNullException("edited");
            if (resolution.Inputs == null) throw new InvalidOperationException("This resolution was not built from the settings levels.");
            if (level == SettingsLevel.BuiltIn) throw new ArgumentException("Built-in values cannot be saved.", "level");

            Validate(edited);
            var levels = ReadLevels(resolution.Inputs);
            var target = levels[(int)level];
            if (target.Path == null) throw new ConfigException(NoFileMessage(level));

            var editedJson = ToJson(edited);
            var anyFile = levels.Any(l => l.Level != SettingsLevel.BuiltIn && l.Exists);
            // Nothing on disk yet: everything that differs from the built-in values is new,
            // including values seeded from the rules file's legacy keys.
            var changes = anyFile
                ? Diff(editedJson, resolution.EffectiveJson ?? PureBuiltIn())
                : Diff(editedJson, PureBuiltIn());

            var result = WriteLevel(levels, target, changes, true, officePin, newOfficePin);
            result.Changed = Leaves(changes).Count();

            Refresh(resolution);

            var hidden = new List<string>();
            foreach (var change in Leaves(changes))
            {
                var now = Lookup(resolution.EffectiveJson, change.Key);
                if (now != null && Same(now, change.Value)) continue;
                SettingProvenance where;
                var by = resolution.Provenance.TryGetValue(change.Key, out where)
                    ? where.FromName + " sets " + Describe(where.Value)
                    : "a higher level sets it";
                hidden.Add(Humanize(change.Key) + " (" + by + ")");
            }
            result.Hidden = hidden;
            return result;
        }

        /// <summary>
        /// Writes <paramref name="settings"/> as profile <paramref name="profileName"/>: every
        /// value that differs from the office standard and the saver's defaults, replacing
        /// what the profile held. "Save as profile" from the settings in use.
        /// </summary>
        public static SettingsSaveResult SaveProfile(SettingsResolution resolution, string profileName,
                                                     FtfSettings settings)
        {
            if (resolution == null) throw new ArgumentNullException("resolution");
            if (settings == null) throw new ArgumentNullException("settings");
            if (string.IsNullOrWhiteSpace(profileName)) throw new ArgumentException("A profile needs a name.");
            if (resolution.Inputs == null) throw new InvalidOperationException("This resolution was not built from the settings levels.");

            Validate(settings);
            var inputs = resolution.Inputs.WithProfile(profileName);
            var levels = ReadLevels(inputs);
            var below = ToJson(FromJson(MergeLevels(levels, PureBuiltIn(), SettingsLevel.User, false)));
            var changes = Diff(ToJson(settings), below);

            var result = WriteLevel(levels, levels[(int)SettingsLevel.Profile], changes, false, null, null);
            result.Changed = Leaves(changes).Count();
            result.Hidden = new List<string>();

            if (string.Equals(resolution.Inputs.ProfileName, profileName, StringComparison.OrdinalIgnoreCase))
                Refresh(resolution);
            return result;
        }

        /// <summary>
        /// Stops a level setting one value, so it inherits from the levels below. A complete
        /// (version 1) file is first reduced to its real differences, so the levels below show
        /// through. False when the level did not hold the value.
        /// </summary>
        public static bool Inherit(SettingsResolution resolution, SettingsLevel level, string settingPath,
                                   string officePin = null)
        {
            if (resolution == null) throw new ArgumentNullException("resolution");
            if (resolution.Inputs == null) throw new InvalidOperationException("This resolution was not built from the settings levels.");
            if (level == SettingsLevel.BuiltIn || string.IsNullOrWhiteSpace(settingPath)) return false;
            if (IsMetadata(settingPath)) return false;

            var levels = ReadLevels(resolution.Inputs);
            var target = levels[(int)level];
            if (!target.Exists) return false;
            if (level == SettingsLevel.Office) Authorize(target.Admin, officePin);
            if (Lookup(target.Values, settingPath) == null) return false;

            var reduced = false;
            if (target.Complete)
            {
                WriteLevel(levels, target, new JObject(), true, officePin, null);
                levels = ReadLevels(resolution.Inputs);
                target = levels[(int)level];
                reduced = true;
            }

            // Reducing a complete file may already have dropped the value (it equalled the
            // inherited one); either way the level no longer sets it.
            var raw = ReadFile(target.Path, target.Name + " settings file");
            var removed = RemovePath(raw, settingPath);
            if (removed) WriteFile(target.Path, raw);

            Refresh(resolution);
            return removed || reduced;
        }

        /// <summary>Removes every value a level sets, keeping a .bak copy. The office PIN
        /// (admin section) and a profile's file survive, empty, so the profile is still found.</summary>
        public static bool ClearLevel(SettingsResolution resolution, SettingsLevel level, string officePin = null)
        {
            if (resolution == null) throw new ArgumentNullException("resolution");
            if (resolution.Inputs == null) throw new InvalidOperationException("This resolution was not built from the settings levels.");
            if (level == SettingsLevel.BuiltIn) return false;

            var levels = ReadLevels(resolution.Inputs);
            var target = levels[(int)level];
            if (!target.Exists) return false;
            if (level == SettingsLevel.Office) Authorize(target.Admin, officePin);

            if (level == SettingsLevel.Profile || target.Admin != null)
            {
                var empty = new JObject { { VersionKey, LayeredVersion } };
                if (target.Admin != null) empty.Add(OfficeAuthorization.AdminKey, target.Admin.DeepClone());
                WriteFile(target.Path, empty);
            }
            else
            {
                File.Copy(target.Path, target.Path + ".bak", true);
                File.Delete(target.Path);
            }

            Refresh(resolution);
            return true;
        }

        /// <summary>
        /// The settings a file would put in the window when imported: a complete file
        /// replaces everything (as before); an override file changes only the values it holds.
        /// </summary>
        public static FtfSettings ReadForImport(string path, FtfSettings current)
        {
            var raw = ReadFile(path, "settings file");
            if (IsComplete(raw.GetValue(VersionKey, StringComparison.OrdinalIgnoreCase)) || current == null)
                return FtfSettings.Load(path);
            try { return FromJson(Merge(ToJson(current), StripMetadata(raw))); }
            catch (ConfigException ex)
            {
                throw new ConfigException("The settings file " + path + " has a value FTF cannot read: " + ex.Message, ex);
            }
        }

        private static SettingsSaveResult WriteLevel(List<SettingsLevelInfo> levels, SettingsLevelInfo target,
                                                     JObject changes, bool keepExisting,
                                                     string officePin, string newOfficePin)
        {
            var level = target.Level;
            var pure = PureBuiltIn();
            var upTo = ToJson(FromJson(MergeLevels(levels, pure, level, false)));
            var result = new SettingsSaveResult { Level = level, LevelName = target.Name, Path = target.Path };

            if (level == SettingsLevel.Office)
            {
                Authorize(target.Admin, officePin);

                var whole = ToJson(FromJson(Merge(upTo, changes)));
                var content = new JObject { { VersionKey, LayeredVersion } };
                foreach (var prop in whole.Properties()) content.Add(prop.Name, prop.Value);

                var admin = target.Admin;
                if (!OfficeAuthorization.HasPin(admin) && !string.IsNullOrEmpty(newOfficePin))
                {
                    admin = OfficeAuthorization.CreateAdmin(newOfficePin);
                    result.PinSet = true;
                }
                if (admin != null) content.Add(OfficeAuthorization.AdminKey, admin.DeepClone());

                WriteFile(target.Path, content);
                result.Stored = Leaves(whole).Count();
                return result;
            }

            var fullBelow = ToJson(FromJson(MergeLevels(levels, pure, level - 1, false)));
            var sharedBelow = ToJson(FromJson(MergeLevels(levels, pure, level - 1, true)));

            // What the level should say for each setting it will consider holding.
            var wanted = new Dictionary<string, JToken>(StringComparer.OrdinalIgnoreCase);
            var held = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (keepExisting && target.Exists)
            {
                foreach (var leaf in Leaves(target.Values))
                {
                    var value = Lookup(upTo, leaf.Key);
                    var asObject = value as JObject;
                    if (value == null || (asObject != null && asObject.HasValues)) continue;   // not a setting
                    wanted[leaf.Key] = value;
                    held.Add(leaf.Key);
                }
            }
            foreach (var change in Leaves(changes)) wanted[change.Key] = change.Value;

            var levelJson = new JObject { { VersionKey, LayeredVersion } };
            var stored = 0;
            var removed = 0;
            foreach (var leaf in Leaves(Merge(upTo, changes)))
            {
                JToken value;
                if (!wanted.TryGetValue(leaf.Key, out value)) continue;

                var full = Lookup(fullBelow, leaf.Key);
                var shared = Lookup(sharedBelow, leaf.Key);
                if (full == null || shared == null || !Same(value, full) || !Same(value, shared))
                {
                    SetPath(levelJson, leaf.Key, value);
                    stored++;
                }
                else if (held.Contains(leaf.Key))
                {
                    removed++;
                }
            }
            if (target.Admin != null) levelJson.Add(OfficeAuthorization.AdminKey, target.Admin.DeepClone());

            WriteFile(target.Path, levelJson);
            result.Stored = stored;
            result.Removed = removed;
            return result;
        }

        private static void Authorize(JObject admin, string pin)
        {
            if (!OfficeAuthorization.HasPin(admin)) return;
            if (pin == null)
                throw new OfficeAuthorizationException("The office standard is protected by a PIN. Enter it to change the office standard.");
            if (!OfficeAuthorization.Verify(admin, pin))
                throw new OfficeAuthorizationException("The office PIN is not right. The office standard was not changed.");
        }

        private static void Validate(FtfSettings settings)
        {
            var problems = new List<string>();
            settings.Validate(problems);
            if (problems.Count > 0)
                throw new ConfigException("Settings are not valid:" + Environment.NewLine +
                                          string.Join(Environment.NewLine, problems.ToArray()));
        }

        private static string NoFileMessage(SettingsLevel level)
        {
            switch (level)
            {
                case SettingsLevel.Project:
                    return "This drawing has never been saved, so there is no project folder to save settings in. Save the drawing first, or save to My defaults.";
                case SettingsLevel.Profile:
                    return "This drawing does not use a profile. Select one on the Profiles page first.";
                case SettingsLevel.Office:
                    return "FTF cannot tell which folder it was loaded from, so there is no office standard file to save.";
                default:
                    return "There is no file for " + LevelName(level, null) + ".";
            }
        }

        private static void WriteFile(string path, JObject content)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            if (File.Exists(path)) File.Copy(path, path + ".bak", true);
            File.WriteAllText(path, content.ToString(Formatting.Indented));
        }

        private static bool IsMetadata(string path)
        {
            var first = path.Split('.')[0];
            return string.Equals(first, VersionKey, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(first, OfficeAuthorization.AdminKey, StringComparison.OrdinalIgnoreCase);
        }

        private static void CopySections(FtfSettings from, FtfSettings to)
        {
            foreach (var prop in typeof(FtfSettings).GetProperties())
                if (prop.CanRead && prop.CanWrite &&
                    prop.GetCustomAttributes(typeof(JsonPropertyAttribute), true).Length > 0)
                    prop.SetValue(to, prop.GetValue(from, null), null);
        }

        private static string FullPath(string path)
        {
            try { return Path.GetFullPath(path); }
            catch (ArgumentException) { return path; }
            catch (NotSupportedException) { return path; }
            catch (PathTooLongException) { return path; }
        }

        private static bool SafeExists(string path)
        {
            try { return File.Exists(path); }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        // ------------------------------------------------------------------ display

        /// <summary>A value as the settings window would say it.</summary>
        public static string Describe(JToken value)
        {
            if (value == null) return "(not set)";
            switch (value.Type)
            {
                case JTokenType.Null:
                case JTokenType.Undefined:
                    return "(none)";
                case JTokenType.Boolean:
                    return (bool)value ? "Yes" : "No";
                case JTokenType.String:
                    var text = (string)value;
                    return string.IsNullOrEmpty(text) ? "(empty)" : text;
                case JTokenType.Float:
                    return ((double)value).ToString("0.######", CultureInfo.InvariantCulture);
                case JTokenType.Integer:
                    return ((long)value).ToString(CultureInfo.InvariantCulture);
                case JTokenType.Array:
                    return DescribeList((JArray)value);
                case JTokenType.Object:
                    var count = ((JObject)value).Count;
                    return count == 0 ? "(none)" : count + " values";
                default:
                    return value.ToString(Formatting.None);
            }
        }

        private static string DescribeList(JArray list)
        {
            if (list.Count == 0) return "(empty list)";
            var names = new List<string>();
            foreach (var item in list)
            {
                var obj = item as JObject;
                if (obj != null)
                {
                    var name = obj.GetValue("name", StringComparison.OrdinalIgnoreCase) ??
                               obj.GetValue("code", StringComparison.OrdinalIgnoreCase) ??
                               obj.GetValue("id", StringComparison.OrdinalIgnoreCase);
                    names.Add(name != null && name.Type == JTokenType.String ? (string)name : "{...}");
                }
                else names.Add(Describe(item));
            }

            const int shown = 6;
            var head = string.Join(", ", names.Take(shown).ToArray());
            return list.Count <= shown
                ? head
                : head + ", ... (" + list.Count.ToString(CultureInfo.InvariantCulture) + " items)";
        }

        /// <summary>Settings-window names for the sections, keyed by their JSON name.</summary>
        private static readonly Dictionary<string, string> SectionNames =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "general", "General" },
                { "trees", "Trees" },
                { "drip", "Driplines" },
                { "labels", "Point Labels" },
                { "lineLabels", "Line Labels" },
                { "drafting", "Drafting Lines" },
                { "tags", "Tags" },
                { "drawOrder", "Drawing Order" },
                { "spots", "Spot Elevations" },
                { "contours", "Contours" },
                { "sheets", "Sheets" },
                { "dips", "Utility Dips" },
                { "easements", "Strip Easements" },
                { "rightOfWay", "Right of Way" },
                { "lots", "Platted Blocks" },
                { "exhibits", "Easement Exhibits" },
                { "recordSurvey", "Recorded Surveys" },
                { "cleanup", "Advanced Settings" },
                { "compare", "Survey Comparison" },
                { "annotation", "Label All" },
                { "diagnostics", "Diagnostics" }
            };

        private static readonly HashSet<string> Acronyms = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "csv", "pdf", "dwg", "id", "pmx", "qc", "ftf", "xref", "tin", "ui", "url"
        };

        /// <summary>"labels.textHeightPlotted" as "Point Labels > Text height (plotted)".</summary>
        public static string Humanize(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            var segments = path.Split('.');
            var parts = new List<string>();
            string section;
            parts.Add(SectionNames.TryGetValue(segments[0], out section) ? section : Words(segments[0]));
            for (var i = 1; i < segments.Length; i++) parts.Add(Words(segments[i]));
            return string.Join(" > ", parts.ToArray());
        }

        private static string Words(string key)
        {
            if (string.IsNullOrEmpty(key)) return key;

            var words = new List<string>();
            var current = new StringBuilder();
            for (var i = 0; i < key.Length; i++)
            {
                var c = key[i];
                if (c == '_' || c == '-' || c == ' ')
                {
                    if (current.Length > 0) { words.Add(current.ToString()); current.Clear(); }
                    continue;
                }

                var prev = i > 0 ? key[i - 1] : '\0';
                var next = i + 1 < key.Length ? key[i + 1] : '\0';
                var boundary = current.Length > 0 &&
                    ((char.IsUpper(c) && (char.IsLower(prev) || char.IsDigit(prev))) ||
                     (char.IsUpper(c) && char.IsUpper(prev) && char.IsLower(next)) ||
                     (char.IsDigit(c) && char.IsLetter(prev)) ||
                     (char.IsLetter(c) && char.IsDigit(prev)));
                if (boundary) { words.Add(current.ToString()); current.Clear(); }
                current.Append(c);
            }
            if (current.Length > 0) words.Add(current.ToString());

            var unit = string.Empty;
            if (words.Count > 1)
            {
                var last = words[words.Count - 1].ToLowerInvariant();
                switch (last)
                {
                    case "plotted": unit = " (plotted)"; break;
                    case "in": unit = " (in)"; break;
                    case "ft": unit = " (ft)"; break;
                    case "percent":
                    case "pct": unit = " (%)"; break;
                }
                if (unit.Length > 0) words.RemoveAt(words.Count - 1);
            }

            for (var i = 0; i < words.Count; i++)
            {
                var w = words[i];
                if (Acronyms.Contains(w)) words[i] = w.ToUpperInvariant();
                else if (i == 0) words[i] = char.ToUpperInvariant(w[0]) + w.Substring(1).ToLowerInvariant();
                else words[i] = w.ToLowerInvariant();
            }
            return string.Join(" ", words.ToArray()) + unit;
        }
    }
}
