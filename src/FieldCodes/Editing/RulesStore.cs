using System;
using System.Collections.Generic;
using System.IO;

namespace FieldCodes.Editing
{
    /// <summary>Which level of the configuration hierarchy is active.</summary>
    public enum RulesSource
    {
        /// <summary>Shipped with the plugin. Replaced by every deploy; never edited.</summary>
        Factory = 0,
        /// <summary>The office standard, outside the install. Survives every update.</summary>
        Office = 1,
        /// <summary>A rules.json beside the drawing. Highest precedence.</summary>
        Drawing = 2
    }

    /// <summary>Where the rules came from, and where an edit would go.</summary>
    public sealed class RulesResolution
    {
        public string ActivePath { get; set; }
        public RulesSource Source { get; set; }

        /// <summary>Where the editor writes. Never the factory file.</summary>
        public string SaveTarget { get; set; }

        public string FactoryPath { get; set; }
        public string OfficePath { get; set; }

        // The inputs, kept so the resolution can be recomputed after a save
        // changes which level exists.
        public string DrawingDirectory { get; set; }
        public string PluginDirectory { get; set; }
    }

    /// <summary>
    /// The configuration ownership model:
    ///
    ///     Factory defaults  ->  Office configuration  ->  Drawing override
    ///     (in the bundle)       (%APPDATA%\FieldToFinish)  (beside the drawing)
    ///
    /// Highest existing level wins when loading. The factory file belongs to the
    /// installer and is replaced wholesale by every deploy, which is exactly why the
    /// editor never writes it: edits go to the office level (created on first save)
    /// or to a drawing override when one is already active. Updating or redeploying
    /// FTF therefore cannot overwrite user configuration, by construction -- the
    /// installer only ever writes inside the bundle.
    ///
    /// Restoring factory defaults is a separate, explicit operation that copies the
    /// shipped file over the office configuration -- it is never a side effect of
    /// loading, reloading or updating.
    /// </summary>
    public static class RulesStore
    {
        public const string RulesFileName = "rules.json";

        /// <summary>%APPDATA%\FieldToFinish -- shared with ftf-settings.json.</summary>
        public static string OfficeDirectory()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "FieldToFinish");
        }

        public static string OfficeRulesPath()
        {
            return Path.Combine(OfficeDirectory(), RulesFileName);
        }

        /// <summary>
        /// Finds the active rules file. <paramref name="officeDirOverride"/> exists for
        /// tests; production callers pass null and get the real office directory.
        /// </summary>
        public static RulesResolution Resolve(string drawingDirectory, string pluginDirectory,
                                              string officeDirOverride = null)
        {
            var officeDir = officeDirOverride ?? OfficeDirectory();
            var officePath = Path.Combine(officeDir, RulesFileName);

            var resolution = new RulesResolution
            {
                DrawingDirectory = drawingDirectory,
                PluginDirectory = pluginDirectory,
                OfficePath = officePath,
                FactoryPath = FirstExisting(CandidatesIn(pluginDirectory))
            };

            var drawingCandidate = FirstExisting(CandidatesIn(drawingDirectory));
            if (drawingCandidate != null)
            {
                resolution.ActivePath = drawingCandidate;
                resolution.Source = RulesSource.Drawing;
                // A drawing override is edited in place: it exists precisely because
                // that job diverges from the office standard.
                resolution.SaveTarget = drawingCandidate;
                return resolution;
            }

            if (File.Exists(officePath))
            {
                resolution.ActivePath = officePath;
                resolution.Source = RulesSource.Office;
                resolution.SaveTarget = officePath;
                return resolution;
            }

            resolution.ActivePath = resolution.FactoryPath;
            resolution.Source = RulesSource.Factory;
            // Never the factory file: the first save creates the office configuration.
            resolution.SaveTarget = officePath;
            return resolution;
        }

        /// <summary>Every location that would be considered, in precedence order.</summary>
        public static IList<KeyValuePair<string, RulesSource>> Candidates(
            string drawingDirectory, string pluginDirectory, string officeDirOverride = null)
        {
            var officeDir = officeDirOverride ?? OfficeDirectory();
            var list = new List<KeyValuePair<string, RulesSource>>();

            foreach (var path in CandidatesIn(drawingDirectory))
                list.Add(new KeyValuePair<string, RulesSource>(path, RulesSource.Drawing));

            list.Add(new KeyValuePair<string, RulesSource>(
                Path.Combine(officeDir, RulesFileName), RulesSource.Office));

            foreach (var path in CandidatesIn(pluginDirectory))
                list.Add(new KeyValuePair<string, RulesSource>(path, RulesSource.Factory));

            return list;
        }

        /// <summary>
        /// Explicitly replaces the office configuration with the shipped defaults.
        ///
        /// The factory content is fully validated before anything is touched: a
        /// damaged factory file must never destroy a working office configuration.
        /// The previous office file survives as .bak. This is the only code path
        /// that ever writes factory content over user configuration, and it only
        /// runs when a person asked for it.
        /// </summary>
        public static void RestoreFactoryDefaults(string factoryPath, string officePath)
        {
            if (string.IsNullOrEmpty(factoryPath) || !File.Exists(factoryPath))
                throw new ConfigException("No factory rules file to restore from.");
            if (string.IsNullOrEmpty(officePath))
                throw new ArgumentNullException("officePath");

            var text = File.ReadAllText(factoryPath);

            var problems = RuleDocument.FromText(text).Validate();
            if (problems.Count > 0)
                throw new ConfigException(
                    "The shipped factory rules failed validation; the office " +
                    "configuration was not touched:" + Environment.NewLine +
                    string.Join(Environment.NewLine, ToArray(problems)));

            var dir = Path.GetDirectoryName(officePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var tmp = officePath + ".tmp";
            File.WriteAllText(tmp, text);

            if (File.Exists(officePath))
                File.Replace(tmp, officePath, officePath + ".bak");
            else
                File.Move(tmp, officePath);
        }

        // ------------------------------------------------------------------ helpers

        private static IEnumerable<string> CandidatesIn(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory)) yield break;
            yield return Path.Combine(directory, RulesFileName);
            yield return Path.Combine(Path.Combine(directory, "config"), RulesFileName);
        }

        private static string FirstExisting(IEnumerable<string> paths)
        {
            foreach (var path in paths)
                if (SafeExists(path)) return path;
            return null;
        }

        private static bool SafeExists(string path)
        {
            try { return File.Exists(path); }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        private static string[] ToArray(IList<string> list)
        {
            var array = new string[list.Count];
            list.CopyTo(array, 0);
            return array;
        }
    }
}
