using System;
using System.Collections.Generic;

namespace CrewUpload.Integration
{
    // The whole contract between Crew Upload and any schedule. Crew Upload only ever talks to these
    // types; the Survey Schedule reader lives in its own assembly (CrewUpload.Schedule.dll) and is
    // loaded at run time by ScheduleConnector. Without that assembly, or with the feature switched
    // off, none of this is used and Crew Upload runs exactly as it does on its own.

    /// <summary>A person in the schedule's roster.</summary>
    public sealed class ScheduledEmployee
    {
        /// <summary>The schedule's permanent id: "jeff_bearson".</summary>
        public string Id { get; set; }
        public string Name { get; set; }
        public bool Active { get; set; }
    }

    /// <summary>A project as the schedule knows it.</summary>
    public sealed class ScheduledProject
    {
        /// <summary>The schedule's permanent project id. Survives renames; what a link stores.</summary>
        public string Id { get; set; }
        public string Name { get; set; }

        /// <summary>The job number the schedule shows for it (its jobNum, or one found in the name), or null.</summary>
        public string JobNumber { get; set; }

        /// <summary>The schedule's task number for the project, when it has one.</summary>
        public string TaskNumber { get; set; }

        public string PmId { get; set; }
    }

    /// <summary>One project one person is scheduled on for one day, with who else is on it.</summary>
    public sealed class ScheduledWork
    {
        public DateTime Date { get; set; }
        public string EmployeeId { get; set; }
        public string ScheduleProjectId { get; set; }
        public string ProjectName { get; set; }
        public string JobNumber { get; set; }

        /// <summary>The assignment's task, or one in the project name ("3.1.3"), or null.</summary>
        public string Task { get; set; }

        /// <summary>What the schedule says is being done ("Topo"); free text, may be empty.</summary>
        public string Activity { get; set; }

        /// <summary>The schedule's day type: FIELD, OFFICE, OOT_FIELD ...</summary>
        public string DayType { get; set; }

        /// <summary>Everyone scheduled on the same project that day, this person included.</summary>
        public List<string> CrewEmployeeIds { get; set; } = new List<string>();

        /// <summary>The schedule entries this came from (one per crew member).</summary>
        public List<string> AssignmentIds { get; set; } = new List<string>();
    }

    /// <summary>The people scheduled on one project on one day: one crew, as the schedule implies it.</summary>
    public sealed class ScheduledCrew
    {
        public DateTime Date { get; set; }
        public string ScheduleProjectId { get; set; }
        public string ProjectName { get; set; }
        public string JobNumber { get; set; }
        public string Activity { get; set; }
        public List<string> EmployeeIds { get; set; } = new List<string>();
        public List<string> AssignmentIds { get; set; } = new List<string>();
    }

    /// <summary>
    /// Read-only schedule information. Every member must return quickly and never throw: when the
    /// schedule cannot be read, <see cref="Available"/> is false, <see cref="Message"/> says so in
    /// one calm sentence, and the lists are empty.
    /// </summary>
    public interface IScheduleSource
    {
        bool Available { get; }

        /// <summary>"Schedule unavailable. Report can still be entered manually." and the like; null when fine.</summary>
        string Message { get; }

        /// <summary>Reads the schedule again if it changed. False (with <see cref="Message"/>) when it cannot be read.</summary>
        bool Refresh();

        IReadOnlyList<ScheduledEmployee> Employees();
        IReadOnlyList<ScheduledProject> Projects();

        /// <summary>The schedule's activity names (field and office), for mapping to work type codes.</summary>
        IReadOnlyList<string> Activities();

        /// <summary>What one person is scheduled on that day: none, one, or several projects.</summary>
        IReadOnlyList<ScheduledWork> WorkFor(string employeeId, DateTime date);

        /// <summary>Every field crew scheduled that day (field day types only), for the admin comparison.</summary>
        IReadOnlyList<ScheduledCrew> CrewsOn(DateTime date);
    }

    /// <summary>Optional: lets the schedule show that a scheduled day was reported. Never required.</summary>
    public interface IScheduleStatusWriter
    {
        /// <summary>Marks the schedule entries as reported. False (with a message) when it could not; never throws.</summary>
        bool MarkReported(IEnumerable<string> assignmentIds, string reportId, string activity, out string message);
    }
}
