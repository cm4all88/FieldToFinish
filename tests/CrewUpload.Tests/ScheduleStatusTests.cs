using CrewUpload;
using CrewUpload.Integration;
using CrewUpload.Reports;

namespace CrewUpload.Tests;

public sealed class ScheduleStatusTests
{
    private sealed class Writer : IScheduleSource, IScheduleStatusWriter
    {
        public List<(List<string> ids, string id, string? activity)> Calls { get; } = new();
        public bool Fail { get; set; }
        public bool Available => true;
        public string? Message => null;
        public bool Refresh() => true;
        public IReadOnlyList<ScheduledEmployee> Employees() => Array.Empty<ScheduledEmployee>();
        public IReadOnlyList<ScheduledProject> Projects() => Array.Empty<ScheduledProject>();
        public IReadOnlyList<string> Activities() => new[] { "Topo", "Staking" };
        public IReadOnlyList<ScheduledWork> WorkFor(string employeeId, DateTime date) => Array.Empty<ScheduledWork>();
        public IReadOnlyList<ScheduledCrew> CrewsOn(DateTime date) => Array.Empty<ScheduledCrew>();

        public bool MarkReported(IEnumerable<string> assignmentIds, string reportId, string activity, out string message)
        {
            Calls.Add((assignmentIds.ToList(), reportId, activity));
            message = Fail ? "share down" : null!;
            return !Fail;
        }
    }

    private static readonly CrewSettings Crew = new() { ActivityMap = { new ActivityMapping { WorkType = "TOPO", Activities = { "Topographic", "Topo" } } } };

    private static DailyReport Report() => new() { ReportId = "DR-1", WorkType = "TOPO", ScheduleAssignmentIds = { "a1", "a2" } };

    [Fact]
    public void OffByDefault()
    {
        var config = JobFolderConfig.CreateDefault();
        Assert.False(config.Features.ScheduleReportStatus);
        var w = new Writer();
        Assert.Null(ScheduleStatus.Report(config, w, Report(), Crew));
        Assert.Empty(w.Calls);
    }

    [Fact]
    public void WhenOnMarksTheScheduleEntriesWithTheScheduleActivity()
    {
        var config = JobFolderConfig.CreateDefault();
        config.Features.ScheduleReportStatus = true;
        var w = new Writer();
        var result = ScheduleStatus.Report(config, w, Report(), Crew)!;
        Assert.True(result.Ok);
        var call = Assert.Single(w.Calls);
        Assert.Equal(new[] { "a1", "a2" }, call.ids);
        Assert.Equal("Topo", call.activity); // "Topographic" is not a schedule activity
    }

    [Fact]
    public void SkipsManualReportsAndSurvivesFailures()
    {
        var config = JobFolderConfig.CreateDefault();
        config.Features.ScheduleReportStatus = true;
        var w = new Writer();
        Assert.Null(ScheduleStatus.Report(config, w, new DailyReport { ReportId = "DR-2" }, Crew));
        Assert.Empty(w.Calls);

        w.Fail = true;
        var result = ScheduleStatus.Report(config, w, Report(), Crew)!;
        Assert.False(result.Ok);
        Assert.Equal("share down", result.Error);

        config.Features.ScheduleIntegration = false;
        Assert.Null(ScheduleStatus.Report(config, w, Report(), Crew));
    }
}
