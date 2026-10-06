using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace CrewUpload
{
    /// <summary>One person on the crews: the initials Crew Upload uses for them, and who they are in the schedule.</summary>
    public sealed class CrewMember
    {
        /// <summary>JBB: what goes in download names and on the daily report.</summary>
        [JsonProperty("initials")] public string Initials { get; set; }
        [JsonProperty("name")] public string Name { get; set; }

        /// <summary>The Survey Schedule's employee id ("jeff_bearson"), or null when not linked.</summary>
        [JsonProperty("scheduleEmployeeId", NullValueHandling = NullValueHandling.Ignore)] public string ScheduleEmployeeId { get; set; }

        /// <summary>Their Windows sign-in (jbearson), so the app can tell who is at the keyboard. Optional.</summary>
        [JsonProperty("user", NullValueHandling = NullValueHandling.Ignore)] public string User { get; set; }

        [JsonProperty("active")] public bool Active { get; set; } = true;

        public override string ToString() => Initials + (string.IsNullOrEmpty(Name) ? string.Empty : " - " + Name);
    }

    /// <summary>The Schedule activities that mean one Crew Upload work type. Set by a PM; empty until then.</summary>
    public sealed class ActivityMapping
    {
        /// <summary>A work type code from job-folders.json (TOPO).</summary>
        [JsonProperty("workType")] public string WorkType { get; set; }

        /// <summary>Schedule activity names / comment words ("Topo", "Topographic").</summary>
        [JsonProperty("activities")] public List<string> Activities { get; set; } = new List<string>();
    }

    /// <summary>
    /// crew-settings.json in the shared Config folder: crew initials and the Schedule-activity to
    /// work-type mapping. Edited by PMs, read by every crew PC. Nothing here is hard-coded.
    /// </summary>
    public sealed class CrewSettings : ISharedDocument
    {
        public const string FileName = "crew-settings.json";

        [JsonProperty("version")] public string Version { get; set; } = "1";
        [JsonProperty("revision")] public int Revision { get; set; }
        [JsonProperty("savedBy")] public string SavedBy { get; set; }
        [JsonProperty("savedOn")] public DateTime? SavedOn { get; set; }
        [JsonProperty("members")] public List<CrewMember> Members { get; set; } = new List<CrewMember>();
        [JsonProperty("activityMap")] public List<ActivityMapping> ActivityMap { get; set; } = new List<ActivityMapping>();

        private static readonly Regex InitialsPattern = new Regex("^[A-Z]{2,4}$");

        public static string NormalizeInitials(string s) => (s ?? string.Empty).Trim().ToUpperInvariant();

        public CrewMember ByInitials(string initials)
        {
            var i = NormalizeInitials(initials);
            return Members.FirstOrDefault(m => m.Active && m.Initials == i);
        }

        public CrewMember ByScheduleId(string id) =>
            string.IsNullOrEmpty(id) ? null : Members.FirstOrDefault(m => m.Active && m.ScheduleEmployeeId == id);

        public CrewMember ByUser(string user)
        {
            if (string.IsNullOrWhiteSpace(user)) return null;
            var bare = user.Contains("\\") ? user.Substring(user.LastIndexOf('\\') + 1) : user;
            return Members.FirstOrDefault(m => m.Active && !string.IsNullOrEmpty(m.User)
                && (string.Equals(m.User, user, StringComparison.OrdinalIgnoreCase) || string.Equals(m.User, bare, StringComparison.OrdinalIgnoreCase)));
        }

        /// <summary>
        /// The work type a Schedule activity maps to, or null. Exact only: the activity text, trimmed and
        /// ignoring case, must be one of the names a PM listed for exactly one work type. Free text
        /// ("Topo, 7:00 start") is never searched for words -- a wrong work type is worse than none, so
        /// anything not clearly mapped is left for the crew to choose.
        /// </summary>
        public string WorkTypeFor(string activity)
        {
            var text = (activity ?? string.Empty).Trim();
            if (text.Length == 0) return null;
            var hits = ActivityMap.Where(m => m != null && m.Activities != null && !string.IsNullOrWhiteSpace(m.WorkType)
                    && m.Activities.Any(a => string.Equals((a ?? string.Empty).Trim(), text, StringComparison.OrdinalIgnoreCase)))
                .Select(m => m.WorkType.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return hits.Count == 1 ? hits[0] : null;
        }

        public string Problems()
        {
            var p = new List<string>();
            if (Members == null || ActivityMap == null) return "Not a crew settings file.";
            foreach (var m in Members)
            {
                if (m == null) { p.Add("An empty crew entry."); continue; }
                if (!InitialsPattern.IsMatch(m.Initials ?? "")) p.Add("'" + m.Initials + "' is not 2-4 capital letters.");
            }
            foreach (var g in Members.Where(m => m != null && m.Active).GroupBy(m => m.Initials).Where(g => g.Count() > 1))
                p.Add(g.Key + " is used by more than one active person.");
            foreach (var g in Members.Where(m => m != null && m.Active && !string.IsNullOrEmpty(m.ScheduleEmployeeId)).GroupBy(m => m.ScheduleEmployeeId).Where(g => g.Count() > 1))
                p.Add("Schedule person " + g.Key + " is given more than one set of initials.");
            foreach (var m in ActivityMap)
                if (m == null || string.IsNullOrWhiteSpace(m.WorkType)) p.Add("An activity mapping has no work type.");
            foreach (var g in ActivityMap.Where(m => m != null).SelectMany(m => (m.Activities ?? new List<string>()).Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => new { a = a.Trim(), m.WorkType }))
                         .GroupBy(x => x.a, StringComparer.OrdinalIgnoreCase).Where(g => g.Select(x => x.WorkType).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1))
                p.Add("Activity '" + g.Key + "' is mapped to more than one work type.");
            return p.Count == 0 ? null : string.Join(" ", p);
        }
    }

    /// <summary>Reads and saves crew-settings.json. Reading never stops Crew Upload: no file means empty settings.</summary>
    public sealed class CrewSettingsStore
    {
        private readonly SharedJsonFile<CrewSettings> _file;

        public CrewSettingsStore(string path) { _file = new SharedJsonFile<CrewSettings>(path); }

        public static CrewSettingsStore For(JobFolderConfig config) => new CrewSettingsStore(config.CrewSettingsPath);

        public string FilePath => _file.FilePath;
        public RegistryLog Log => _file.Log;
        internal SharedJsonFile<CrewSettings> File => _file;

        /// <summary>For editing: throws when the file exists but cannot be read, so a PM never saves over it blind.</summary>
        public CrewSettings Load() => _file.Load();

        /// <summary>For crews: the settings, or empty ones when they cannot be read (with the reason).</summary>
        public CrewSettings TryLoad(out string problem)
        {
            problem = null;
            try { return string.IsNullOrEmpty(_file.FilePath) ? new CrewSettings() : _file.Load(); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is InvalidDataException)
            {
                problem = e.Message;
                return new CrewSettings();
            }
        }

        public CrewSettings Save(CrewSettings settings, string user, int basisRevision) => _file.Save(settings, user, basisRevision);
    }
}
