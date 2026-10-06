using CrewUpload;
using CrewUpload.Integration;

namespace CrewUpload.Tests;

/// <summary>A schedule that answers from lists; what Crew Upload sees through the contract.</summary>
internal sealed class FakeSchedule : IScheduleSource
{
    public bool Available { get; set; } = true;
    public string? Message { get; set; }
    public List<ScheduledWork> Work { get; } = new();
    public List<ScheduledEmployee> People { get; } = new();
    public bool Refresh() => Available;
    public IReadOnlyList<ScheduledEmployee> Employees() => People;
    public IReadOnlyList<ScheduledProject> Projects() => Array.Empty<ScheduledProject>();
    public IReadOnlyList<string> Activities() => new[] { "Topo", "Staking" };
    public IReadOnlyList<ScheduledWork> WorkFor(string employeeId, DateTime date) =>
        Available ? Work.Where(w => w.EmployeeId == employeeId && w.Date == date.Date).ToList() : new List<ScheduledWork>();
    public IReadOnlyList<ScheduledCrew> CrewsOn(DateTime date) => Array.Empty<ScheduledCrew>();
}

public sealed class SchedulePrefillTests : TestShare
{
    private static readonly DateTime Day = new(2026, 7, 13);

    private readonly CrewSettings _crew = new()
    {
        Members =
        {
            new CrewMember { Initials = "JAM", Name = "Jim Martin", ScheduleEmployeeId = "jim_martin" },
            new CrewMember { Initials = "JBB", Name = "Jeff Bearson", ScheduleEmployeeId = "jeff_bearson", User = "jbearson" },
            new CrewMember { Initials = "CB", Name = "Colston Bravo", ScheduleEmployeeId = "colston_bravo" },
            new CrewMember { Initials = "XX", Name = "Not scheduled" },
        },
        ActivityMap = { new ActivityMapping { WorkType = "TOPO", Activities = { "Topo" } } },
    };

    private FakeSchedule Schedule()
    {
        var s = new FakeSchedule();
        s.People.AddRange(new[]
        {
            new ScheduledEmployee { Id = "jim_martin", Name = "Jim Martin", Active = true },
            new ScheduledEmployee { Id = "jeff_bearson", Name = "Jeff Bearson", Active = true },
            new ScheduledEmployee { Id = "colston_bravo", Name = "Colston Bravo", Active = true },
            new ScheduledEmployee { Id = "ryan_meldrum", Name = "Ryan Meldrum", Active = true },
        });
        s.Work.Add(new ScheduledWork
        {
            Date = Day, EmployeeId = "jeff_bearson", ScheduleProjectId = "2l8vhmyoodt", ProjectName = "TDLE 554-1800-119 3.1.3",
            JobNumber = "554-1800-119", Task = "3.1.3", Activity = "Topo", DayType = "FIELD",
            CrewEmployeeIds = { "jeff_bearson", "jim_martin", "colston_bravo", "ryan_meldrum" }, AssignmentIds = { "a1", "a2", "a3", "a4" },
        });
        s.Work.Add(new ScheduledWork
        {
            Date = Day, EmployeeId = "jeff_bearson", ScheduleProjectId = "ann1", ProjectName = "Mercer Pump Station",
            Task = "PMX 126 north segment control", Activity = "Set control, 7:00 start", DayType = "FIELD",
            CrewEmployeeIds = { "jeff_bearson" }, AssignmentIds = { "b1" },
        });
        return s;
    }

    private ProjectStore Linked()
    {
        var registry = Registry();
        registry.Register("554-1800-119", SurveyDir(), "pm");
        registry.LinkSchedule("554-1800-119", "2l8vhmyoodt", "TDLE 554-1800-119 3.1.3", "pm");
        return new ProjectStore(Config, Registry());
    }

    [Fact]
    public void IdentifiesThePersonByInitialsThenSignIn()
    {
        Assert.Equal("JAM", SchedulePrefill.Identify(_crew, "jam", "someone")!.Initials);
        Assert.Equal("JBB", SchedulePrefill.Identify(_crew, "", "PMX\\jbearson")!.Initials);
        Assert.Null(SchedulePrefill.Identify(_crew, "ZZZ", "nobody"));
    }

    [Fact]
    public void PrefillsFromTheScheduleTheLinkAndTheMappings()
    {
        var r = SchedulePrefill.For(Schedule(), Config, _crew, Linked(), _crew.ByInitials("JBB"), Day);
        Assert.Null(r.Message);
        Assert.Equal(2, r.Choices.Count);

        var tdle = r.Choices[0];
        Assert.NotNull(tdle.Project);
        var d = tdle.Draft;
        Assert.Equal("schedule", d.Source);
        Assert.Equal("554-1800-119", d.ProjectNumber);
        Assert.Equal("TDLE", d.ProjectName);
        Assert.Equal("3.1.3", d.TaskNumber);
        Assert.Equal("2l8vhmyoodt", d.ScheduleProjectId);
        Assert.Equal(new[] { "a1", "a2", "a3", "a4" }, d.ScheduleAssignmentIds);
        Assert.Equal(new[] { "JBB", "JAM", "CB" }, d.Crew.Select(c => c.Initials));
        Assert.Equal(new[] { "Ryan Meldrum" }, tdle.UnknownCrew); // scheduled, but no initials in the crew list
        Assert.Equal("TOPO", d.WorkType);
        Assert.Equal("20260713-JBB-1800-119-TOPO", d.DataFileName);
        Assert.Null(d.Hours);   // the schedule does not know these
        Assert.Null(d.Vehicle);
        Assert.Contains("with Jim Martin, Colston Bravo, Ryan Meldrum", tdle.Label);
    }

    [Fact]
    public void UnlinkedOrUnmappedLeavesFieldsForTheCrew()
    {
        var r = SchedulePrefill.For(Schedule(), Config, _crew, Linked(), _crew.ByInitials("JBB"), Day);
        var mercer = r.Choices[1];
        Assert.Null(mercer.Project);
        Assert.Null(mercer.Draft.ProjectNumber);       // no number in the name, no link: never guessed
        Assert.Null(mercer.Draft.WorkType);            // "Set control" is not mapped
        Assert.Null(mercer.Draft.TaskNumber);
        Assert.Equal("PMX 126 north segment control", mercer.Draft.Subtask);
        Assert.Null(mercer.Draft.DataFileName);
        Assert.Contains("solo", mercer.Label);
    }

    [Fact]
    public void ScheduleNumberIsOfferedButTheProjectIsOnlyFiledWhenLinked()
    {
        var store = new ProjectStore(Config, Registry());
        Registry().Register("554-1800-119", SurveyDir(), "pm"); // registered, not linked
        var r = SchedulePrefill.For(Schedule(), Config, _crew, store, _crew.ByInitials("JBB"), Day);
        Assert.Null(r.Choices[0].Project);
        Assert.Equal("554-1800-119", r.Choices[0].Draft.ProjectNumber); // the crew sees it and can keep or change it
        Assert.Null(r.Choices[0].Draft.DataFileName);
    }

    [Fact]
    public void SaysWhyThereIsNothing()
    {
        Assert.Equal(SchedulePrefill.Unavailable, SchedulePrefill.For(null, Config, _crew, null, _crew.ByInitials("JBB"), Day).Message);
        Assert.Equal(SchedulePrefill.NotInCrewList, SchedulePrefill.For(Schedule(), Config, _crew, null, _crew.ByInitials("XX"), Day).Message);
        Assert.Equal(SchedulePrefill.NotInCrewList, SchedulePrefill.For(Schedule(), Config, _crew, null, null, Day).Message);
        Assert.Equal(SchedulePrefill.NotScheduled, SchedulePrefill.For(Schedule(), Config, _crew, null, _crew.ByInitials("JBB"), Day.AddDays(1)).Message);

        var down = Schedule();
        down.Available = false;
        down.Message = "Schedule unavailable. Report can still be entered manually.";
        var r = SchedulePrefill.For(down, Config, _crew, null, _crew.ByInitials("JBB"), Day);
        Assert.Empty(r.Choices);
        Assert.Equal("Schedule unavailable. Report can still be entered manually.", r.Message);
    }

    [Fact]
    public void AnUnreadableRegistryStillPrefills()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Config.RegistryFile)!);
        File.WriteAllText(Config.RegistryFile, "{ broken");
        var r = SchedulePrefill.For(Schedule(), Config, _crew, new ProjectStore(Config, Registry()), _crew.ByInitials("JBB"), Day);
        Assert.Equal(2, r.Choices.Count);
        Assert.Null(r.Choices[0].Project);
    }

    [Fact]
    public void ABrokenScheduleNeverThrows()
    {
        var r = SchedulePrefill.For(new ThrowingSchedule(), Config, _crew, null, _crew.ByInitials("JBB"), Day);
        Assert.Empty(r.Choices);
        Assert.Equal(SchedulePrefill.Unavailable, r.Message);
    }

    private sealed class ThrowingSchedule : IScheduleSource
    {
        public bool Available => true;
        public string? Message => null;
        public bool Refresh() => true;
        public IReadOnlyList<ScheduledEmployee> Employees() => throw new InvalidOperationException("bad");
        public IReadOnlyList<ScheduledProject> Projects() => throw new InvalidOperationException("bad");
        public IReadOnlyList<string> Activities() => throw new InvalidOperationException("bad");
        public IReadOnlyList<ScheduledWork> WorkFor(string employeeId, DateTime date) => throw new InvalidOperationException("bad");
        public IReadOnlyList<ScheduledCrew> CrewsOn(DateTime date) => throw new InvalidOperationException("bad");
    }
}
