using CrewUpload;
using CrewUpload.Integration;
using CrewUpload.Reports;

namespace CrewUpload.Tests;

public sealed class ReportComparisonTests
{
    private static readonly DateTime Mon = new(2026, 7, 13), Tue = new(2026, 7, 14);

    private static FakeSchedule Schedule()
    {
        var s = new FakeSchedule();
        s.People.Add(new ScheduledEmployee { Id = "jim_martin", Name = "Jim Martin", Active = true });
        s.People.Add(new ScheduledEmployee { Id = "jeff_bearson", Name = "Jeff Bearson", Active = true });
        foreach (var d in new[] { Mon, Tue })
        {
            s.Crews.Add(new ScheduledCrew { Date = d, ScheduleProjectId = "tdle", ProjectName = "TDLE 554-1800-119", JobNumber = "554-1800-119", Activity = "Topo",
                EmployeeIds = { "jim_martin", "jeff_bearson" }, AssignmentIds = { "a1-" + d.Day, "a2-" + d.Day } });
            s.Crews.Add(new ScheduledCrew { Date = d, ScheduleProjectId = "mercer", ProjectName = "Mercer", Activity = "Staking", EmployeeIds = { "tim_do" }, AssignmentIds = { "b-" + d.Day } });
        }
        return s;
    }

    private static DailyReport Report(DateTime d, string id, Action<DailyReport> set)
    {
        var r = new DailyReport { ReportId = id, Date = d, ProjectNumber = "554-1800-119", ProjectName = "TDLE", WorkType = "TOPO", Crew = { new ReportCrewMember { Initials = "JBB" } } };
        set(r);
        return r;
    }

    private static readonly RegistrySnapshot Registry = new()
    {
        Projects = { new ProjectRegistration { Key = "554-1800-119", SurveyFolder = "x", ScheduleProjectId = "tdle" } },
    };

    [Fact]
    public void ScheduledReportedMissingAndUnscheduled()
    {
        var reports = new List<DailyReport>
        {
            Report(Mon, "R1", r => r.ScheduleAssignmentIds.Add("a2-13")),                    // from the schedule, by entry
            Report(Tue, "R2", r => { }),                                                      // typed by hand; the PM's link ties it to the schedule
            Report(Tue, "R3", r => { r.ProjectNumber = "214-0001-001"; r.ProjectName = "Emergency call-out"; }), // not on the schedule
        };
        var rows = ReportComparison.Compare(Mon, Tue, Schedule(), reports, Registry, today: new DateTime(2026, 7, 20));

        Assert.Equal(ComparisonStatus.Reported, rows.Single(x => x.Date == Mon && x.Scheduled?.ScheduleProjectId == "tdle").Status);
        Assert.Equal("R1", rows.Single(x => x.Date == Mon && x.Scheduled?.ScheduleProjectId == "tdle").Reports.Single().ReportId);
        Assert.Equal(ComparisonStatus.Reported, rows.Single(x => x.Date == Tue && x.Scheduled?.ScheduleProjectId == "tdle").Status);
        Assert.Equal(2, rows.Count(x => x.Status == ComparisonStatus.Missing)); // Mercer, both days
        var extra = rows.Single(x => x.Status == ComparisonStatus.NotScheduled);
        Assert.Equal("R3", extra.Reports.Single().ReportId);
        Assert.Equal("Jim Martin, Jeff Bearson", rows.First(x => x.Scheduled?.ScheduleProjectId == "tdle").Crew);
    }

    [Fact]
    public void ADifferentScheduleProjectIsNotCoveredByNumberAlone()
    {
        // the crew picked "Mercer" from the schedule: it does not count for TDLE even with TDLE's number typed
        var r = Report(Mon, "R1", x => x.ScheduleProjectId = "mercer");
        var rows = ReportComparison.Compare(Mon, Mon, Schedule(), new[] { r }, Registry, new DateTime(2026, 7, 20));
        Assert.Equal(ComparisonStatus.Missing, rows.Single(x => x.Scheduled?.ScheduleProjectId == "tdle").Status);
        Assert.Equal(ComparisonStatus.Reported, rows.Single(x => x.Scheduled?.ScheduleProjectId == "mercer").Status);
    }

    [Fact]
    public void TodayIsNotMissingYetAndTheFutureIsNotListed()
    {
        var rows = ReportComparison.Compare(Mon, Tue, Schedule(), new List<DailyReport>(), Registry, today: Mon);
        Assert.All(rows, x => Assert.Equal(Mon, x.Date));
        Assert.All(rows, x => Assert.Equal(ComparisonStatus.NotYet, x.Status));
    }

    [Fact]
    public void WithoutTheScheduleReportsAreStillListed()
    {
        var reports = new[] { Report(Mon, "R1", r => { }) };
        Assert.Equal(ComparisonStatus.ReportOnly, ReportComparison.Compare(Mon, Tue, null, reports, Registry, Tue).Single().Status);
        var down = Schedule();
        down.Available = false;
        Assert.Equal(ComparisonStatus.ReportOnly, ReportComparison.Compare(Mon, Tue, down, reports, Registry, Tue).Single().Status);
    }

    [Fact]
    public void ExportsCsv()
    {
        var rows = ReportComparison.Compare(Mon, Mon, Schedule(), new[] { Report(Mon, "R1", r => { r.ProjectName = "TDLE, Phase 3"; r.ScheduleProjectId = "nope"; }) }, Registry, Tue);
        var csv = ReportComparison.Csv(rows);
        Assert.StartsWith("Date,Status,Project,ProjectNumber,Crew,Work,ReportIDs", csv);
        Assert.Contains("\"TDLE, Phase 3\"", csv);
        Assert.Contains("Missing report", csv);
    }
}
