using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using CrewUpload.Integration;

namespace CrewUpload.Reports
{
    public enum ComparisonStatus
    {
        /// <summary>Scheduled, and a report covers it.</summary>
        Reported,

        /// <summary>Scheduled on a past day, and no report covers it.</summary>
        Missing,

        /// <summary>Scheduled today, no report yet (the day is not over).</summary>
        NotYet,

        /// <summary>A report that matches nothing on the schedule.</summary>
        NotScheduled,

        /// <summary>A report, with no schedule to compare it to.</summary>
        ReportOnly,
    }

    /// <summary>One line of the admin view.</summary>
    public sealed class ComparisonRow
    {
        public DateTime Date { get; set; }
        public ComparisonStatus Status { get; set; }
        public ScheduledCrew Scheduled { get; set; }
        public List<DailyReport> Reports { get; set; } = new List<DailyReport>();
        public string Project { get; set; }
        public string ProjectNumber { get; set; }
        public string Crew { get; set; }
        public string Work { get; set; }
    }

    /// <summary>
    /// Scheduled versus reported, from the structured records and the schedule -- never from file
    /// names. A report covers a scheduled crew when it names one of the crew's schedule entries, or the
    /// same schedule project that day, or a registered project the PM linked to it that day.
    /// </summary>
    public static class ReportComparison
    {
        public static List<ComparisonRow> Compare(DateTime from, DateTime to, IScheduleSource schedule, IReadOnlyList<DailyReport> reports,
            RegistrySnapshot registry, DateTime today)
        {
            var rows = new List<ComparisonRow>();
            var used = new HashSet<DailyReport>();
            var names = new Dictionary<string, string>(StringComparer.Ordinal);
            var compare = schedule != null && schedule.Available;
            if (compare) foreach (var e in schedule.Employees()) names[e.Id] = e.Name;

            // the schedule project each registered project is linked to
            var links = (registry?.Projects ?? new List<ProjectRegistration>())
                .Where(p => !string.IsNullOrEmpty(p.ScheduleProjectId))
                .GroupBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().ScheduleProjectId, StringComparer.OrdinalIgnoreCase);

            for (var day = from.Date; compare && day <= to.Date && day <= today.Date; day = day.AddDays(1))
            {
                var dayReports = reports.Where(r => r.Date.Date == day).ToList();
                foreach (var crew in schedule.CrewsOn(day))
                {
                    var covering = dayReports.Where(r => Covers(r, crew, links)).ToList();
                    foreach (var r in covering) used.Add(r);
                    rows.Add(new ComparisonRow
                    {
                        Date = day,
                        Scheduled = crew,
                        Reports = covering,
                        Status = covering.Count > 0 ? ComparisonStatus.Reported : day == today.Date ? ComparisonStatus.NotYet : ComparisonStatus.Missing,
                        Project = crew.ProjectName,
                        ProjectNumber = covering.Select(r => r.ProjectNumber).FirstOrDefault(n => !string.IsNullOrEmpty(n)) ?? crew.JobNumber,
                        Crew = string.Join(", ", crew.EmployeeIds.Select(id => { string n; return names.TryGetValue(id, out n) ? n : id; })),
                        Work = covering.Select(r => r.WorkType).FirstOrDefault(w => !string.IsNullOrEmpty(w)) ?? crew.Activity,
                    });
                }
            }

            foreach (var r in reports.Where(r => !used.Contains(r) && r.Date.Date >= from.Date && r.Date.Date <= to.Date))
            {
                rows.Add(new ComparisonRow
                {
                    Date = r.Date.Date,
                    Status = compare ? ComparisonStatus.NotScheduled : ComparisonStatus.ReportOnly,
                    Reports = { r },
                    Project = r.ProjectName,
                    ProjectNumber = r.ProjectNumber,
                    Crew = string.Join(", ", r.Crew.Select(c => c.Name ?? c.Initials)),
                    Work = r.WorkType,
                });
            }
            return rows.OrderBy(x => x.Date).ThenBy(x => x.Status).ThenBy(x => x.Project, StringComparer.OrdinalIgnoreCase).ToList();
        }

        internal static bool Covers(DailyReport r, ScheduledCrew crew, IDictionary<string, string> links)
        {
            if (r.Date.Date != crew.Date.Date) return false;
            if (r.ScheduleAssignmentIds.Any(id => crew.AssignmentIds.Contains(id))) return true;
            if (!string.IsNullOrEmpty(r.ScheduleProjectId)) return r.ScheduleProjectId == crew.ScheduleProjectId;
            string linked;
            return !string.IsNullOrEmpty(r.ProjectNumber) && links.TryGetValue(r.ProjectNumber, out linked) && linked == crew.ScheduleProjectId;
        }

        public static string Describe(ComparisonStatus s)
        {
            switch (s)
            {
                case ComparisonStatus.Reported: return "Reported";
                case ComparisonStatus.Missing: return "Missing report";
                case ComparisonStatus.NotYet: return "Not reported yet";
                case ComparisonStatus.NotScheduled: return "Reported, not scheduled";
                default: return "Reported";
            }
        }

        /// <summary>The rows as CSV for a spreadsheet.</summary>
        public static string Csv(IEnumerable<ComparisonRow> rows)
        {
            var sb = new StringBuilder("Date,Status,Project,ProjectNumber,Crew,Work,ReportIDs,Hours,SubmittedBy,PDFPath\r\n");
            foreach (var x in rows)
            {
                sb.Append(string.Join(",", new[]
                {
                    x.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Describe(x.Status), x.Project, x.ProjectNumber, x.Crew, x.Work,
                    string.Join(" ", x.Reports.Select(r => r.ReportId)),
                    string.Join(" ", x.Reports.Select(r => r.Hours?.ToString(CultureInfo.InvariantCulture))),
                    string.Join(" ", x.Reports.Select(r => r.SubmittedBy)),
                    string.Join(" ", x.Reports.Select(r => r.PdfPath)),
                }.Select(Quote))).Append("\r\n");
            }
            return sb.ToString();
        }

        private static string Quote(string s)
        {
            s = s ?? string.Empty;
            return s.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        }
    }
}
