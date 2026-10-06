using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CrewUpload
{
    /// <summary>One crew's day on one project doing one kind of work: what a download folder holds.</summary>
    public sealed class FieldVisit
    {
        public string Crew { get; set; }
        public DateTime Date { get; set; }

        /// <summary>Work type code: TOPO, LINEOUT ...</summary>
        public string WorkType { get; set; }

        /// <summary>The project's phase when it has one (141); it follows client-task in every name.</summary>
        public string Phase { get; set; }

        public string CrewCode => (Crew ?? string.Empty).Trim().ToUpperInvariant();
        public string WorkCode => (WorkType ?? string.Empty).Trim().ToUpperInvariant();
    }

    /// <summary>What a download folder's name says, read back.</summary>
    public sealed class ParsedDownload
    {
        public string FolderName { get; set; }
        public string ProjectNumber { get; set; }
        public FieldVisit Visit { get; set; }
    }

    /// <summary>
    /// Builds a download folder name (20260128-JAM-1521-799-TOPO) from the visit, and reads one
    /// back. Both use the config's downloadName pattern, so changing the pattern changes both.
    /// </summary>
    public static class DownloadNames
    {
        public static string Name(JobFolderConfig config, string projectNumber, FieldVisit visit) =>
            Naming.Clean(Naming.Fill(config.DownloadName, Values(config, projectNumber, visit)));

        internal static Dictionary<string, string> Values(JobFolderConfig config, string projectNumber, FieldVisit visit)
        {
            return new Dictionary<string, string>
            {
                // 1800-119, or 1800-119-141 when the visit is for a phase.
                { "projectNumber", JobFolderConfig.NormalizeProjectNumber(projectNumber) + (string.IsNullOrEmpty(visit.Phase) ? string.Empty : "-" + visit.Phase) },
                { "clientTask", JobFolderConfig.NormalizeProjectNumber(projectNumber) },
                { "phase", visit.Phase ?? string.Empty },
                { "date", visit.Date.ToString(config.DateFormat, CultureInfo.InvariantCulture) },
                { "year", visit.Date.Year.ToString(CultureInfo.InvariantCulture) },
                { "crew", visit.CrewCode },
                { "workType", visit.WorkCode },
            };
        }

        /// <summary>
        /// The project, crew, date and work type in a folder the crew already named, or null when
        /// the name does not follow the pattern or holds an impossible date or project number.
        /// </summary>
        public static ParsedDownload Parse(JobFolderConfig config, string folderName)
        {
            if (string.IsNullOrWhiteSpace(folderName)) return null;
            var m = Pattern(config).Match(folderName.Trim());
            if (!m.Success) return null;

            var visit = new FieldVisit();
            if (m.Groups["date"].Success)
            {
                DateTime date;
                if (!DateTime.TryParseExact(m.Groups["date"].Value, config.DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out date)) return null;
                visit.Date = date;
            }
            visit.Crew = m.Groups["crew"].Success ? m.Groups["crew"].Value.ToUpperInvariant() : null;
            visit.WorkType = m.Groups["workType"].Success ? m.Groups["workType"].Value.ToUpperInvariant() : null;
            string number, phase;
            if (!JobFolderConfig.ParseProjectNumber(m.Groups["projectNumber"].Value, out number, out phase) || !config.IsValidProjectNumber(number)) return null;
            visit.Phase = phase;
            return new ParsedDownload { FolderName = folderName.Trim(), ProjectNumber = number, Visit = visit };
        }

        private static Regex Pattern(JobFolderConfig config)
        {
            var sb = new StringBuilder("^");
            var pattern = config.DownloadName ?? string.Empty;
            var last = 0;
            foreach (Match t in Regex.Matches(pattern, @"\{([A-Za-z]+)\}"))
            {
                sb.Append(Regex.Escape(pattern.Substring(last, t.Index - last)));
                switch (t.Groups[1].Value)
                {
                    case "date": sb.Append(@"(?<date>[0-9A-Za-z]+?)"); break;
                    case "crew": sb.Append(@"(?<crew>[A-Za-z]{1,6})"); break;
                    case "projectNumber": sb.Append(@"(?<projectNumber>.+)"); break;
                    case "workType": sb.Append(@"(?<workType>[A-Za-z0-9]+)"); break;
                    default: sb.Append(@".*?"); break;
                }
                last = t.Index + t.Length;
            }
            sb.Append(Regex.Escape(pattern.Substring(last)));
            sb.Append("$");
            return new Regex(sb.ToString(), RegexOptions.IgnoreCase);
        }
    }
}
