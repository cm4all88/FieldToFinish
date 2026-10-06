using CrewUpload;
using CrewUpload.Integration;

namespace CrewUpload.Tests;

public sealed class ScheduleLinkTests : TestShare
{
    private static readonly ScheduledProject[] Schedule =
    {
        new() { Id = "2l8vhmyoodt", Name = "TDLE 554-1800-119 3.1.3", JobNumber = "554-1800-119" },
        new() { Id = "other", Name = "TDLE 553-1800-119", JobNumber = "553-1800-119" }, // same client-task, other prefix
        new() { Id = "fd", Name = "Field Day" },
        new() { Id = "short", Name = "Odd 554-180-11", JobNumber = "554-180-11" },
    };

    [Fact]
    public void SuggestsOnlyTheExactFullNumber()
    {
        var s = ScheduleLinks.Suggest("554-1800-119", Schedule);
        Assert.Equal("2l8vhmyoodt", Assert.Single(s).Id);
        Assert.Empty(ScheduleLinks.Suggest("1800-119", Schedule)); // client-task alone is not unique
        Assert.Empty(ScheduleLinks.Suggest("554-1711-042", Schedule));
        Assert.Empty(ScheduleLinks.Suggest(null, Schedule));
    }

    [Fact]
    public void RegisteringNeverLinks()
    {
        var r = Registry().Register("554-1800-119", SurveyDir(), "pm").Entry;
        Assert.Null(r.ScheduleProjectId);
    }

    [Fact]
    public void LinkStoresThePermanentIdWithHistory()
    {
        Registry().Register("554-1800-119", SurveyDir(), "pm1");
        var linked = Registry().LinkSchedule("554-1800-119", "2l8vhmyoodt", "TDLE 554-1800-119 3.1.3", "pm2").Entry;
        Assert.Equal("2l8vhmyoodt", linked.ScheduleProjectId);

        var reread = Registry().Find("554-1800-119");
        Assert.Equal("2l8vhmyoodt", reread.ScheduleProjectId);
        Assert.Equal("TDLE 554-1800-119 3.1.3", reread.ScheduleProjectName);
        var h = reread.History.Last();
        Assert.Equal(RegistrationChange.ScheduleLinked, h.Change);
        Assert.Equal("pm2", h.By);
        Assert.Null(h.PreviousScheduleProjectId);
        Assert.Equal("pm1", reread.RegisteredBy); // linking is not re-registering

        Assert.True(Registry().LinkSchedule("554-1800-119", "2l8vhmyoodt", "TDLE 554-1800-119 3.1.3", "pm2").Unchanged);

        var snap = Registry().Load();
        Assert.Equal("554-1800-119", Assert.Single(ScheduleLinks.LinkedTo(snap, "2l8vhmyoodt")).Key);
    }

    [Fact]
    public void UnlinkKeepsTheRegistration()
    {
        Registry().Register("554-1800-119", SurveyDir(), "pm1");
        Registry().LinkSchedule("554-1800-119", "2l8vhmyoodt", "TDLE", "pm1");
        var r = Registry().LinkSchedule("554-1800-119", null, null, "pm1").Entry;
        Assert.Null(r.ScheduleProjectId);
        Assert.Null(r.ScheduleProjectName);
        Assert.Equal(RegistrationChange.ScheduleUnlinked, r.History.Last().Change);
        Assert.Equal("2l8vhmyoodt", r.History.Last().PreviousScheduleProjectId);
        Assert.True(r.Active);
        Assert.NotNull(r.SurveyFolder);
    }

    [Fact]
    public void CannotLinkAnUnregisteredProject()
    {
        Registry().Register("554-1711-042", SurveyDir("1711-Orting", "554-1711-042 Harman"), "pm");
        Assert.Throws<InvalidOperationException>(() => Registry().LinkSchedule("554-1800-119", "2l8vhmyoodt", "TDLE", "pm"));
    }

    [Fact]
    public void LinkClashWithAnotherPmIsCaught()
    {
        Registry().Register("554-1800-119", SurveyDir(), "pm1");
        var myScreen = Registry().Load();
        Registry().LinkSchedule("554-1800-119", "2l8vhmyoodt", "TDLE", "pm3");
        Assert.Throws<RegistryConflictException>(() => Registry().LinkSchedule("554-1800-119", "other", "Other", "pm2", myScreen));
    }

    [Fact]
    public void OldRegistryFilesWithoutLinksStillRead()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Config.RegistryFile)!);
        var folder = SurveyDir();
        File.WriteAllText(Config.RegistryFile, "{\"version\":\"2\",\"revision\":3,\"projects\":[{\"key\":\"554-1800-119\",\"surveyFolder\":"
            + Newtonsoft.Json.JsonConvert.ToString(folder) + ",\"active\":true,\"registeredBy\":\"pm\",\"registeredOn\":\"2026-01-01T00:00:00\",\"history\":[]}]}");
        Assert.Null(Registry().Find("554-1800-119").ScheduleProjectId);
        Registry().SetActive("554-1800-119", false, "pm");
        Assert.DoesNotContain("scheduleProjectId", File.ReadAllText(Config.RegistryFile)); // nothing written for unlinked projects
    }
}
