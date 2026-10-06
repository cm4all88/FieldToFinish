using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrewUpload.Integration;
using Newtonsoft.Json;

namespace CrewUpload.Schedule
{
    /// <summary>
    /// Reads the Survey Schedule's shared folder (pso-master.json, pm-*.json, pso-requests.json,
    /// pso-overrides.json) and answers Crew Upload's questions about it. Read-only: files are opened
    /// for reading with sharing for writers, so the schedule app is never blocked and nothing it owns
    /// is ever written -- except, only when features.scheduleReportStatus is on, the shared
    /// pso-progress.json through <see cref="MarkReported"/>. Never throws; when the folder cannot be read, Available is false and Message
    /// says so in one sentence.
    /// </summary>
    public sealed class ScheduleFolderSource : IScheduleSource, IScheduleStatusWriter
    {
        public const string UnavailableMessage = "Schedule unavailable. Report can still be entered manually.";

        internal const string MasterFile = "pso-master.json";
        internal const string RequestsFile = "pso-requests.json";
        internal const string OverridesFile = "pso-overrides.json";

        private static readonly HashSet<string> FieldTypes = new HashSet<string>(StringComparer.Ordinal) { "FIELD", "OOT_FIELD", "FIELD_OFFICE" };

        private readonly string _folder;
        private readonly object _gate = new object();
        private string _signature;
        private AssembledSchedule _schedule;
        private DateTime? _failedAtUtc;
        private Dictionary<string, ProjectRecord> _projectsById = new Dictionary<string, ProjectRecord>(StringComparer.Ordinal);

        /// <summary>How long one read of the folder may take before it counts as unavailable (a dead share hangs).</summary>
        internal TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(8);

        /// <summary>After a failed read, how long the lists stay empty before a read is tried again (Refresh always tries).</summary>
        internal TimeSpan RetryAfter { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>Pauses between attempts when a file is mid-write and does not parse.</summary>
        internal int[] RetryDelaysMs { get; set; } = { 150, 400, 1000 };

        public ScheduleFolderSource(string folder)
        {
            _folder = folder;
        }

        public bool Available { get { lock (_gate) { EnsureLoaded(); return _schedule != null; } } }

        public string Message { get; private set; }

        public bool Refresh()
        {
            lock (_gate)
            {
                try
                {
                    var sig = Run(Signature);
                    if (sig != null && sig == _signature && _schedule != null) return true;
                    var s = Run(Read);
                    if (s == null) return Fail();
                    _schedule = s;
                    _projectsById = new Dictionary<string, ProjectRecord>(StringComparer.Ordinal);
                    foreach (var p in s.Projects.Where(p => p.Id != null)) _projectsById[p.Id] = p;
                    _signature = sig;
                    _failedAtUtc = null;
                    Message = null;
                    return true;
                }
                catch (Exception e) when (!(e is OutOfMemoryException))
                {
                    return Fail();
                }
            }
        }

        public IReadOnlyList<ScheduledEmployee> Employees()
        {
            lock (_gate)
            {
                if (!EnsureLoaded()) return new ScheduledEmployee[0];
                return (_schedule.Master.Employees ?? new List<EmployeeRecord>())
                    .Where(e => e?.Id != null)
                    .Select(e => new ScheduledEmployee { Id = e.Id, Name = e.Name ?? e.Id, Active = e.Active != false })
                    .ToList();
            }
        }

        public IReadOnlyList<ScheduledProject> Projects()
        {
            lock (_gate)
            {
                if (!EnsureLoaded()) return new ScheduledProject[0];
                return _schedule.Projects.Where(p => p.Id != null).Select(ToProject).ToList();
            }
        }

        public IReadOnlyList<string> Activities()
        {
            lock (_gate)
            {
                if (!EnsureLoaded()) return new string[0];
                var a = _schedule.Master.Activities;
                if (a == null) return new string[0];
                return (a.Field ?? new List<string>()).Concat(a.Office ?? new List<string>())
                    .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            }
        }

        public IReadOnlyList<ScheduledWork> WorkFor(string employeeId, DateTime date)
        {
            lock (_gate)
            {
                if (string.IsNullOrEmpty(employeeId) || !EnsureLoaded()) return new ScheduledWork[0];
                var day = date.Date;
                var mine = OnDay(day).Where(a => a.EmployeeId == employeeId && a.Type != "OFF" && a.ProjectId != null).ToList();
                var result = new List<ScheduledWork>();
                foreach (var a in mine)
                {
                    if (result.Any(r => r.ScheduleProjectId == a.ProjectId)) continue; // one choice per project
                    var crew = CrewOf(day, a);
                    ProjectRecord p;
                    _projectsById.TryGetValue(a.ProjectId, out p);
                    result.Add(new ScheduledWork
                    {
                        Date = day,
                        EmployeeId = employeeId,
                        ScheduleProjectId = a.ProjectId,
                        ProjectName = p?.Name ?? a.Label ?? "",
                        JobNumber = JobNumbers.JobNumOf(p),
                        Task = !string.IsNullOrWhiteSpace(a.Task) ? a.Task.Trim() : JobNumbers.TaskOf(p),
                        Activity = (a.Comments ?? "").Trim(),
                        DayType = a.Type,
                        CrewEmployeeIds = OrderedCrew(employeeId, crew),
                        AssignmentIds = crew.Select(c => c.Id).Where(id => id != null).Distinct().ToList(),
                    });
                }
                return result;
            }
        }

        public IReadOnlyList<ScheduledCrew> CrewsOn(DateTime date)
        {
            lock (_gate)
            {
                if (!EnsureLoaded()) return new ScheduledCrew[0];
                var day = date.Date;
                return OnDay(day)
                    .Where(a => a.ProjectId != null && FieldTypes.Contains(a.Type ?? ""))
                    .GroupBy(a => a.ProjectId, StringComparer.Ordinal)
                    .Select(g =>
                    {
                        ProjectRecord p;
                        _projectsById.TryGetValue(g.Key, out p);
                        var list = g.ToList();
                        return new ScheduledCrew
                        {
                            Date = day,
                            ScheduleProjectId = g.Key,
                            ProjectName = p?.Name ?? "",
                            JobNumber = JobNumbers.JobNumOf(p),
                            Activity = list.Select(a => (a.Comments ?? "").Trim()).FirstOrDefault(c => c.Length > 0) ?? "",
                            EmployeeIds = list.Select(a => a.EmployeeId).Where(e => e != null).Distinct().ToList(),
                            AssignmentIds = list.Select(a => a.Id).Where(i => i != null).Distinct().ToList(),
                        };
                    })
                    .ToList();
            }
        }

        /// <summary>
        /// Optional, behind features.scheduleReportStatus: marks the entries as reported in
        /// pso-progress.json. The only file this assembly ever writes; never a PM file.
        /// </summary>
        public bool MarkReported(IEnumerable<string> assignmentIds, string reportId, string activity, out string message)
        {
            try { return ProgressWriter.MarkReported(_folder, assignmentIds, reportId, activity, out message); }
            catch (Exception e) when (!(e is OutOfMemoryException))
            {
                message = "The schedule could not be marked: " + e.Message;
                return false;
            }
        }

        // ---- the crew, as the hand-off says to reconstruct it: same project, same day ----

        /// <summary>
        /// Everyone on the same project that day doing the same kind of day (a field crew is the field
        /// people; an office day on the project stands alone). Departed employees are left out.
        /// </summary>
        private List<AssignmentRecord> CrewOf(DateTime day, AssignmentRecord a)
        {
            var field = FieldTypes.Contains(a.Type ?? "");
            if (!field) return new List<AssignmentRecord> { a };
            var inactive = new HashSet<string>((_schedule.Master.Employees ?? new List<EmployeeRecord>())
                .Where(e => e?.Id != null && e.Active == false).Select(e => e.Id), StringComparer.Ordinal);
            return OnDay(day)
                .Where(x => x.ProjectId == a.ProjectId && FieldTypes.Contains(x.Type ?? "")
                            && (x.EmployeeId == a.EmployeeId || !inactive.Contains(x.EmployeeId ?? "")))
                .ToList();
        }

        private static List<string> OrderedCrew(string me, IEnumerable<AssignmentRecord> crew)
        {
            var ids = new List<string> { me };
            ids.AddRange(crew.Select(c => c.EmployeeId).Where(e => e != null && e != me).Distinct());
            return ids;
        }

        /// <summary>The live entries covering a day. Pending requests are not on the schedule yet.</summary>
        private IEnumerable<AssignmentRecord> OnDay(DateTime day)
        {
            return _schedule.Assignments.Where(a => a.Approval != "pending" && Covers(a, day));
        }

        /// <summary>
        /// start..end inclusive, every day -- weekends too, as the board draws them
        /// (boardHTML: ds&gt;=a.start&amp;&amp;ds&lt;=a.end for each visible day).
        /// </summary>
        internal static bool Covers(AssignmentRecord a, DateTime day)
        {
            DateTime s, e;
            if (!TryDate(a.Start, out s)) return false;
            if (!TryDate(a.End, out e)) e = s;
            return day >= s && day <= e;
        }

        private static bool TryDate(string s, out DateTime d) =>
            DateTime.TryParseExact((s ?? "").Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d);

        private static ScheduledProject ToProject(ProjectRecord p) => new ScheduledProject
        {
            Id = p.Id,
            Name = p.Name ?? "",
            JobNumber = JobNumbers.JobNumOf(p),
            TaskNumber = JobNumbers.TaskOf(p),
            PmId = p.PmId,
        };

        // ---- reading ----

        private bool EnsureLoaded()
        {
            if (_schedule != null) return true;
            // after a failed read, wait a little before trying again: a dead share costs a timeout each time
            if (_failedAtUtc.HasValue && DateTime.UtcNow - _failedAtUtc.Value < RetryAfter) return false;
            return Refresh();
        }

        private bool Fail()
        {
            _failedAtUtc = DateTime.UtcNow;
            Message = UnavailableMessage;
            return _schedule != null; // keep answering from the last good read, if there was one
        }

        /// <summary>Runs one folder operation with a time limit, so a dead share cannot hang the app.</summary>
        private T Run<T>(Func<T> work) where T : class
        {
            var t = Task.Run(work);
            if (!t.Wait(Timeout)) return null;
            return t.Result;
        }

        /// <summary>The files' names, sizes and times: if this has not changed, the schedule has not.</summary>
        private string Signature()
        {
            if (string.IsNullOrWhiteSpace(_folder) || !Directory.Exists(_folder)) return null;
            var names = new[] { MasterFile, RequestsFile, OverridesFile }
                .Concat(Directory.GetFiles(_folder, "pm-*.json").Select(Path.GetFileName))
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase);
            return string.Join("|", names.Select(n =>
            {
                var f = new FileInfo(Path.Combine(_folder, n));
                return f.Exists ? n + ":" + f.Length + ":" + f.LastWriteTimeUtc.Ticks : n + ":-";
            }));
        }

        /// <summary>readFolderState() then assembleState(). Null when there is no usable master file.</summary>
        private AssembledSchedule Read()
        {
            if (string.IsNullOrWhiteSpace(_folder) || !Directory.Exists(_folder)) return null;
            var master = ReadJson<MasterFile>(MasterFile);
            if (master?.Pms == null) return null; // if(!master||!Array.isArray(master.pms))return null;
            var feeds = new Dictionary<string, PmFeed>(StringComparer.Ordinal);
            foreach (var pm in master.Pms.Where(p => p?.Id != null))
                feeds[pm.Id] = ReadJson<PmFeed>("pm-" + pm.Id + ".json") ?? new PmFeed();
            var requests = ReadJson<List<AssignmentRecord>>(RequestsFile) ?? new List<AssignmentRecord>();
            var overrides = ReadJson<List<OverrideRecord>>(OverridesFile) ?? new List<OverrideRecord>();
            return ScheduleAssembler.Assemble(master, feeds, requests, overrides);
        }

        /// <summary>
        /// Timestamps stay the exact text the app wrote: it compares them as strings ("a &gt; b"), so
        /// they must not be turned into dates and back, which would lose their fractions of a second.
        /// </summary>
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings { DateParseHandling = DateParseHandling.None };

        /// <summary>
        /// readJson(): the file's contents, or null when it is missing or will not parse. A file caught
        /// mid-write is tried again a few times before it counts as unreadable.
        /// </summary>
        private T ReadJson<T>(string name) where T : class
        {
            var path = Path.Combine(_folder, name);
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    if (!File.Exists(path)) return null;
                    string text;
                    using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var r = new StreamReader(fs))
                        text = r.ReadToEnd();
                    return JsonConvert.DeserializeObject<T>(text, Settings);
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is JsonException)
                {
                    if (attempt >= RetryDelaysMs.Length) return null;
                    Thread.Sleep(RetryDelaysMs[attempt]);
                }
            }
        }
    }
}
