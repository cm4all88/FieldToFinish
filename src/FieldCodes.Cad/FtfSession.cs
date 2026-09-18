using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;

namespace FieldCodes.Cad
{
    /// <summary>
    /// Loads and caches the rules file for a drawing session.
    ///
    /// The rules file is looked for beside the drawing first, then beside this
    /// assembly. A missing or invalid rules file is fatal: the parser's whole point
    /// is that it does not guess, so running with default rules would be worse than
    /// not running at all.
    ///
    /// UNTESTED: never run against a real drawing.
    /// </summary>
    internal static class FtfSession
    {
        public const string RulesFileName = "rules.json";

        private static RulesConfig _cached;
        private static string _cachedFrom;
        private static DateTime _cachedStamp;

        private static FieldCodes.Settings.SettingsResolution _settings;
        private static string _settingsKey;

        /// <summary>
        /// Behaviour settings for this drawing, resolved from the drawing's folder, then
        /// the user's profile, then the plugin folder. Falls back to values carried over
        /// from the rules file so an install that predates the settings file keeps
        /// behaving the way it did.
        /// </summary>
        public static FieldCodes.Settings.SettingsResolution ResolveSettings(
            Database db, RulesConfig rules)
        {
            var drawingDir = db != null ? SafeDirectory(db.Filename) : null;
            var pluginDir = AssemblyDirectory();
            var profile = DrawingStore.ReadProfileName(db);
            var key = (drawingDir ?? "-") + "|" + (pluginDir ?? "-") + "|" + (profile ?? "-");

            if (_settings != null && _settingsKey == key) return _settings;

            // The drawing's selected drafting profile, when it has one, drives every
            // FTF tool; a settings file beside the drawing still overrides it.
            _settings = FieldCodes.Settings.FtfSettings.Resolve(drawingDir, pluginDir, rules, profile);
            _settingsKey = key;
            return _settings;
        }

        /// <summary>
        /// Convenience for commands: the resolved settings, already applied onto the
        /// rules object so the parser and layer classifier see the current values.
        /// </summary>
        public static FieldCodes.Settings.FtfSettings SettingsFor(Database db, RulesConfig rules)
        {
            var resolution = ResolveSettings(db, rules);
            resolution.Settings.ApplyTo(rules);
            return resolution.Settings;
        }

        /// <summary>Drops the cache so the next command re-reads the settings file.</summary>
        public static void InvalidateSettings()
        {
            _settings = null;
            _settingsKey = null;
        }

        /// <summary>
        /// Rules for the current drawing, reloaded when the file changes on disk so an
        /// edit to rules.json takes effect without restarting AutoCAD.
        /// </summary>
        public static RulesConfig Rules(Database db)
        {
            var path = Locate(db);
            if (path == null)
                throw new ConfigException(
                    "No " + RulesFileName + " found. Looked in:" + Environment.NewLine +
                    "  " + string.Join(Environment.NewLine + "  ",
                                       CandidatePaths(db).ToArray()));

            var stamp = File.GetLastWriteTimeUtc(path);
            if (_cached != null &&
                string.Equals(_cachedFrom, path, StringComparison.OrdinalIgnoreCase) &&
                _cachedStamp == stamp)
                return _cached;

            _cached = RulesConfig.Load(path);
            _cachedFrom = path;
            _cachedStamp = stamp;
            return _cached;
        }

        /// <summary>
        /// Every place the rules file is looked for, in precedence order: beside the
        /// drawing (a per-job override), then the office configuration in %APPDATA%,
        /// then the factory defaults shipped in the bundle.
        /// </summary>
        public static IList<string> CandidatePaths(Database db)
        {
            var drawingDir = db != null ? SafeDirectory(db.Filename) : null;
            return FieldCodes.Editing.RulesStore
                .Candidates(drawingDir, AssemblyDirectory())
                .Select(pair => pair.Key)
                .ToList();
        }

        /// <summary>
        /// The active rules level and where an edit would be written. The factory
        /// file is replaced wholesale by every deploy, which is exactly why edits
        /// never target it -- they go to the office level, or a drawing override.
        /// </summary>
        public static FieldCodes.Editing.RulesResolution ResolveRules(Database db)
        {
            var drawingDir = db != null ? SafeDirectory(db.Filename) : null;
            return FieldCodes.Editing.RulesStore.Resolve(drawingDir, AssemblyDirectory());
        }

        public static string Locate(Database db)
        {
            return ResolveRules(db).ActivePath;
        }

        private static string SafeDirectory(string filePath)
        {
            try { return Path.GetDirectoryName(filePath); }
            catch (ArgumentException) { return null; }
        }

        /// <summary>
        /// Folder this assembly was loaded from. Empty when the host shadow-copies or
        /// loads from a byte array, which is why a copy beside the drawing is the more
        /// reliable of the two locations.
        /// </summary>
        private static string AssemblyDirectory()
        {
            try
            {
                var location = typeof(FtfSession).Assembly.Location;
                return string.IsNullOrEmpty(location) ? null : Path.GetDirectoryName(location);
            }
            catch (NotSupportedException) { return null; }
        }

        /// <summary>
        /// Runs a command body inside a transaction, turning any failure into a message
        /// on the command line rather than a modal exception dialog. The transaction is
        /// committed only when the body returns without throwing, so a failed command
        /// leaves the drawing untouched.
        /// </summary>
        /// <summary>
        /// What the most recent Run reported. Commands swallow their own exceptions
        /// (an unhandled exception inside AutoCAD is worse than a message), which
        /// used to leave the pipeline blind: every step looked like a success, so a
        /// step whose transaction rolled back still let the rest of the run pile on
        /// top of it. The pipeline reads this after each step instead.
        /// </summary>
        public static string LastRunError { get; private set; }

        /// <summary>The last failure with its stack trace, for diagnosing a run.</summary>
        public static string LastRunErrorDetail { get; private set; }

        public static void Run(string commandName, Action<Database, Transaction, Editor> body)
        {
            LastRunError = null;
            LastRunErrorDetail = null;

            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) { LastRunError = "no active drawing"; return; }

            var ed = doc.Editor;
            var db = doc.Database;

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                try
                {
                    body(db, tr, ed);
                    tr.Commit();
                }
                catch (ConfigException ex)
                {
                    LastRunError = ex.Message;
                    ed.WriteMessage("\n{0}: rules file problem.\n{1}\n", commandName, ex.Message);
                }
                catch (Autodesk.AutoCAD.Runtime.Exception ex)
                {
                    LastRunError = ex.Message;
                    LastRunErrorDetail = commandName + ": " + ex.ErrorStatus + " " + ex.Message + "\n" + ex.StackTrace;
                    ed.WriteMessage("\n{0}: AutoCAD error: {1}\n", commandName, ex.Message);
                }
                catch (Exception ex)
                {
                    LastRunError = ex.Message;
                    LastRunErrorDetail = commandName + ": " + ex + "\n" + ex.StackTrace;
                    ed.WriteMessage("\n{0} failed: {1}\n{2}\n",
                        commandName, ex.Message, ex.StackTrace);
                }
            }
        }
    }
}
