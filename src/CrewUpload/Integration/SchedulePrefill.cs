using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using CrewUpload.Reports;

namespace CrewUpload.Integration
{
    /// <summary>One thing the schedule says this person is doing that day, ready to start a report or an upload from.</summary>
    public sealed class PrefillChoice
    {
        public ScheduledWork Work { get; set; }

        /// <summary>The registered project linked to the schedule project, or null when none is linked.</summary>
        public ProjectFolder Project { get; set; }

        /// <summary>The report as the schedule would start it. Only a starting point.</summary>
        public DailyReport Draft { get; set; }

        /// <summary>Scheduled people who are not in the crew list (no initials yet).</summary>
        public List<string> UnknownCrew { get; set; } = new List<string>();

        /// <summary>"TDLE 554-1800-119 3.1.3 - Topo - with Jim Martin, Ryan Meldrum"</summary>
        public string Label { get; set; }
    }

    /// <summary>What the schedule says about one person's day, or why it says nothing.</summary>
    public sealed class PrefillResult
    {
        public CrewMember Person { get; set; }
        public List<PrefillChoice> Choices { get; set; } = new List<PrefillChoice>();

        /// <summary>One calm sentence when there are no choices; null otherwise.</summary>
        public string Message { get; set; }
    }

    /// <summary>
    /// Turns the schedule's view of a day into report and upload starting points: who the person is
    /// (crew list), what they are scheduled on (schedule), which registered project that is (the PM's
    /// link), who they are with (crew list initials), and what work type the activity means (the PM's
    /// mapping). Each step that has no answer leaves its field blank for the crew. Never throws.
    /// </summary>
    public static class SchedulePrefill
    {
        public const string Unavailable = "Schedule unavailable. Report can still be entered manually.";
        public const string NotScheduled = "Nothing on the schedule for you that day. Enter the report manually.";
        public const string NotInCrewList = "You are not linked to the schedule in the crew list. Enter the report manually.";

        /// <summary>The person at the keyboard: by the initials on screen, else by their Windows sign-in.</summary>
        public static CrewMember Identify(CrewSettings crew, string initials, string user)
        {
            if (crew == null) return null;
            var first = DailyReportDraft.ParseCrew(initials, crew).FirstOrDefault();
            return (first == null ? null : crew.ByInitials(first.Initials)) ?? crew.ByUser(user);
        }

        public static PrefillResult For(IScheduleSource schedule, JobFolderConfig config, CrewSettings crew, ProjectStore projects,
            CrewMember person, DateTime date)
        {
            var result = new PrefillResult { Person = person };
            try
            {
                if (schedule == null) { result.Message = Unavailable; return result; }
                if (person == null || string.IsNullOrEmpty(person.ScheduleEmployeeId)) { result.Message = NotInCrewList; return result; }
                if (!schedule.Available) { result.Message = schedule.Message ?? Unavailable; return result; }

                var work = schedule.WorkFor(person.ScheduleEmployeeId, date);
                if (work.Count == 0)
                {
                    result.Message = schedule.Available ? NotScheduled : schedule.Message ?? Unavailable;
                    return result;
                }
                var names = schedule.Employees().ToDictionary(e => e.Id, e => e.Name, StringComparer.Ordinal);
                RegistrySnapshot registry = null;
                try { registry = projects?.Registry.Load(); }
                catch (Exception e) when (e is System.IO.IOException || e is UnauthorizedAccessException || e is System.IO.InvalidDataException) { }

                foreach (var w in work) result.Choices.Add(Choice(w, config, crew, projects, registry, names));
            }
            catch (Exception e) when (!(e is OutOfMemoryException))
            {
                result.Choices.Clear();
                result.Message = Unavailable;
            }
            return result;
        }

        internal static PrefillChoice Choice(ScheduledWork w, JobFolderConfig config, CrewSettings crew, ProjectStore projects,
            RegistrySnapshot registry, IDictionary<string, string> names)
        {
            var linked = ScheduleLinks.LinkedTo(registry, w.ScheduleProjectId);
            var project = linked.Count == 1 && projects != null ? projects.FromRegistration(linked[0]) : null;

            var d = new DailyReport
            {
                Date = w.Date,
                ScheduleProjectId = w.ScheduleProjectId,
                ScheduleAssignmentIds = w.AssignmentIds.ToList(),
                Source = "schedule",
            };
            if (project != null) d.ProjectNumber = project.Info.FullNumber;
            else if (ProjectRegistry.KeyFor(w.JobNumber ?? "") != null) d.ProjectNumber = ProjectRegistry.KeyFor(w.JobNumber);

            var task = (w.Task ?? string.Empty).Trim();
            if (Regex.IsMatch(task, @"^\d{1,4}(\.\d{1,4})*$")) d.TaskNumber = task;
            else if (task.Length > 0) d.Subtask = task;
            d.ProjectName = DailyReportDraft.JobName(w.ProjectName, w.JobNumber, JobFolderConfig.NormalizeProjectNumber(w.JobNumber ?? string.Empty), d.TaskNumber)
                ?? w.ProjectName;

            var choice = new PrefillChoice { Work = w, Project = project, Draft = d };
            foreach (var id in w.CrewEmployeeIds)
            {
                var m = crew?.ByScheduleId(id);
                string name;
                names.TryGetValue(id, out name);
                if (m != null) d.Crew.Add(new ReportCrewMember { Initials = m.Initials, Name = m.Name ?? name, ScheduleEmployeeId = id });
                else choice.UnknownCrew.Add(name ?? id);
            }

            var code = crew?.WorkTypeFor(w.Activity);
            if (code != null && config.WorkTypes.Any(t => string.Equals(t.Code, code, StringComparison.OrdinalIgnoreCase)))
                d.WorkType = code.ToUpperInvariant();

            if (project != null && d.Crew.Count > 0 && d.WorkType != null)
                d.DataFileName = DownloadNames.Name(config, project.Info.ProjectNumber, new FieldVisit { Crew = d.Crew[0].Initials, Date = d.Date, WorkType = d.WorkType });

            var others = w.CrewEmployeeIds.Skip(1).Select(id => { string n; return names.TryGetValue(id, out n) ? n : id; }).ToList();
            choice.Label = (string.IsNullOrWhiteSpace(w.ProjectName) ? "(unnamed project)" : w.ProjectName)
                + (string.IsNullOrWhiteSpace(w.Activity) ? string.Empty : " – " + w.Activity)
                + (others.Count == 0 ? " – solo" : " – with " + string.Join(", ", others));
            return choice;
        }
    }
}
