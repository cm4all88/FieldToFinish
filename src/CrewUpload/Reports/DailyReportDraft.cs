using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace CrewUpload.Reports
{
    /// <summary>Starting points for a crew's report. Everything here is a suggestion the crew can change.</summary>
    public static class DailyReportDraft
    {
        /// <summary>
        /// From what is on the upload screen: the project, crew, date, phase and work type, and the
        /// download folder's name. Missing pieces are left blank.
        /// </summary>
        public static DailyReport FromUpload(JobFolderConfig config, ProjectFolder project, string crewText, DateTime date, string phase,
            string workType, CrewSettings crew)
        {
            var r = new DailyReport { Date = date.Date, WorkType = workType, TaskNumber = phase, Source = "manual" };
            r.Crew.AddRange(ParseCrew(crewText, crew));
            if (project != null)
            {
                r.ProjectNumber = project.Info.FullNumber ?? project.Info.ProjectNumber;
                r.ProjectName = JobName(project.Info.ProjectName, project.Info.FullNumber, project.Info.ProjectNumber);
                if (r.Crew.Count > 0 && !string.IsNullOrEmpty(workType))
                    r.DataFileName = DownloadNames.Name(config, project.Info.ProjectNumber,
                        new FieldVisit { Crew = r.Crew[0].Initials, Date = r.Date, WorkType = workType, Phase = JobFolderConfig.NormalizePhase(phase) });
            }
            return r;
        }

        /// <summary>"JBB CP, RM" -> the people, names from the crew list. Unknown initials are kept as typed.</summary>
        public static List<ReportCrewMember> ParseCrew(string text, CrewSettings crew)
        {
            var list = new List<ReportCrewMember>();
            foreach (var part in Regex.Split(text ?? string.Empty, @"[\s,;/+&]+").Select(CrewSettings.NormalizeInitials).Where(p => p.Length > 0))
            {
                if (list.Any(m => m.Initials == part)) continue;
                var m2 = crew?.ByInitials(part);
                list.Add(new ReportCrewMember { Initials = part, Name = m2?.Name, ScheduleEmployeeId = m2?.ScheduleEmployeeId });
            }
            return list;
        }

        /// <summary>"554-1800-119 TDLE Phase 3" -> "TDLE Phase 3": the job's name without its number.</summary>
        public static string JobName(string folderName, params string[] numbers)
        {
            var name = (folderName ?? string.Empty).Trim();
            foreach (var n in numbers.Where(n => !string.IsNullOrEmpty(n)).OrderByDescending(n => n.Length))
                name = Regex.Replace(name, @"(^|\s)" + Regex.Escape(n) + @"(?=\s|$)", " ");
            name = Regex.Replace(name, @"\s+", " ").Trim(' ', '-', '_');
            return name.Length == 0 ? null : name;
        }
    }
}
