using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CrewUpload.Schedule
{
    /// <summary>One file in the schedule folder at one moment: to prove a check changed nothing.</summary>
    public sealed class FileFingerprint
    {
        public string Name { get; set; }
        public long Length { get; set; }
        public DateTime LastWriteUtc { get; set; }
        public string Sha256 { get; set; }
        public override string ToString() => Name + "  " + Length + " bytes  " + LastWriteUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "Z  " + Sha256;
    }

    /// <summary>
    /// Read-only checks of a schedule folder, for comparing Crew Upload's reading with the Survey
    /// Schedule itself (ScheduleCheck.exe and the compatibility test use these). Same reader and
    /// assembler as the integration; nothing is written to the folder.
    /// </summary>
    public static class ScheduleDiagnostics
    {
        /// <summary>Test hook: pauses between reads of a file that will not parse (normally the reader's own).</summary>
        internal static int[] ReadRetryDelaysMs { get; set; }

        private static ScheduleFolderSource Source(string folder)
        {
            var s = new ScheduleFolderSource(folder);
            if (ReadRetryDelaysMs != null) s.RetryDelaysMs = ReadRetryDelaysMs;
            return s;
        }

        public static readonly string[] ScheduleFiles = { ScheduleFolderSource.MasterFile, ScheduleFolderSource.RequestsFile, ScheduleFolderSource.OverridesFile, ProgressWriter.FileName };

        /// <summary>Every schedule file in the folder: name, size, time and SHA-256, read with sharing so nothing is blocked.</summary>
        public static List<FileFingerprint> Fingerprint(string folder)
        {
            var names = ScheduleFiles.Concat(Directory.Exists(folder) ? Directory.GetFiles(folder, "pm-*.json").Select(Path.GetFileName) : new string[0])
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase);
            var list = new List<FileFingerprint>();
            foreach (var n in names)
            {
                var path = Path.Combine(folder, n);
                if (!File.Exists(path)) continue;
                var info = new FileInfo(path);
                string hash;
                using (var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var sha = SHA256.Create())
                    hash = BitConverter.ToString(sha.ComputeHash(s)).Replace("-", string.Empty);
                list.Add(new FileFingerprint { Name = n, Length = info.Length, LastWriteUtc = info.LastWriteTimeUtc, Sha256 = hash });
            }
            return list;
        }

        /// <summary>
        /// The assembled schedule as JSON: {"projects":[...],"assignments":[...]} with every field the
        /// files carry. This is what the compatibility test compares with the app's own assembleState().
        /// Null when the folder has no usable master file.
        /// </summary>
        public static string AssembledJson(string folder)
        {
            var s = Source(folder).ReadAssembled();
            if (s == null) return null;
            var ser = JsonSerializer.Create(new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore, DateParseHandling = DateParseHandling.None });
            var o = new JObject
            {
                ["projects"] = JArray.FromObject(s.Projects, ser),
                ["assignments"] = JArray.FromObject(s.Assignments, ser),
            };
            return o.ToString(Formatting.Indented);
        }

        /// <summary>What Crew Upload sees on each date, in words, for checking against the board.</summary>
        public static string Report(string folder, IEnumerable<DateTime> dates, string person = null)
        {
            var sb = new StringBuilder();
            var src = Source(folder);
            var s = src.ReadAssembled();
            if (s == null) return "Schedule unavailable: no readable " + ScheduleFolderSource.MasterFile + " in " + folder + Environment.NewLine;
            var emp = (s.Master.Employees ?? new List<EmployeeRecord>()).Where(e => e?.Id != null).GroupBy(e => e.Id).ToDictionary(g => g.Key, g => g.First());
            var proj = s.Projects.Where(p => p.Id != null).GroupBy(p => p.Id).ToDictionary(g => g.Key, g => g.Last());
            Func<string, string> name = id => id == null ? "?" : emp.ContainsKey(id) ? (emp[id].Name ?? id) + (emp[id].Active == false ? " (inactive)" : string.Empty) : id + " (not in roster)";
            Func<AssignmentRecord, string> what = a =>
            {
                ProjectRecord p = null;
                if (a.ProjectId != null) proj.TryGetValue(a.ProjectId, out p);
                var title = p != null ? p.Name : a.Label ?? "";
                return a.Type + "  " + title + (JobNumbers.JobNumOf(p) != null ? "  [#" + JobNumbers.JobNumOf(p) + "]" : string.Empty)
                    + (string.IsNullOrEmpty(a.Comments) ? string.Empty : "  \"" + a.Comments + "\"")
                    + (string.IsNullOrEmpty(a.Task) ? string.Empty : "  task: " + a.Task)
                    + "  " + a.Start + (a.End != a.Start ? ".." + a.End : string.Empty)
                    + (a.Approval == "pending" ? "  PENDING (not used by Crew Upload)" : string.Empty)
                    + (s.AppliedOverrides.Contains(a.Id) ? "  OVERRIDDEN" : string.Empty)
                    + "  {" + a.Id + "}";
            };

            sb.AppendLine("Folder: " + folder);
            sb.AppendLine("People " + emp.Count + " (" + emp.Values.Count(e => e.Active != false) + " active), projects " + s.Projects.Count
                + ", entries " + s.Assignments.Count + " (" + s.Assignments.Count(a => a.Approval == "pending") + " pending), overrides applied " + s.AppliedOverrides.Count);
            foreach (var day in dates.Select(d => d.Date).Distinct().OrderBy(d => d))
            {
                var key = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                sb.AppendLine();
                sb.AppendLine("=== " + day.ToString("dddd yyyy-MM-dd", CultureInfo.InvariantCulture) + " ===");
                var onDay = s.Assignments.Where(a => ScheduleFolderSource.Covers(a, day) && (person == null || a.EmployeeId == person)).ToList();
                foreach (var g in onDay.GroupBy(a => a.EmployeeId).OrderBy(g => name(g.Key), StringComparer.OrdinalIgnoreCase))
                {
                    var live = g.Where(a => a.Approval != "pending").ToList();
                    sb.AppendLine(name(g.Key) + (live.Count(a => a.Type != "OFF") > 1 ? "   <-- more than one entry this day" : string.Empty));
                    foreach (var a in g) sb.AppendLine("    " + what(a));
                }
                sb.AppendLine("  Crews Crew Upload sees (field entries, same project, same day):");
                foreach (var c in src.CrewsOn(day).Where(c => person == null || c.EmployeeIds.Contains(person)))
                {
                    sb.AppendLine("    " + c.ProjectName + (c.JobNumber != null ? " [#" + c.JobNumber + "]" : string.Empty) + ": " + string.Join(", ", c.EmployeeIds.Select(name)));
                    // the app's own links: each entry's withIds should name the others
                    foreach (var a in s.Assignments.Where(a => c.AssignmentIds.Contains(a.Id)))
                    {
                        var others = c.EmployeeIds.Where(e => e != a.EmployeeId).OrderBy(e => e).ToList();
                        var with = (a.WithIds ?? new List<string>()).OrderBy(e => e).ToList();
                        if (with.Count > 0 && !others.SequenceEqual(with))
                            sb.AppendLine("      note: " + name(a.EmployeeId) + "'s entry links " + string.Join(", ", with.Select(name)) + " (withIds differs from the grouping)");
                    }
                }
            }
            sb.AppendLine();
            sb.AppendLine("Not modelled by Crew Upload: generated days off from master.patterns (9-80 Fridays, 4-10s) and holidays; they never create work.");
            return sb.ToString();
        }
    }
}
