using CrewUpload;

namespace CrewUpload.Tests;

public sealed class CrewSettingsTests : TestShare
{
    private CrewSettingsStore Store()
    {
        var store = CrewSettingsStore.For(Config);
        store.File.RetryDelaysMs = new[] { 1, 1 };
        store.File.LockWait = TimeSpan.FromMilliseconds(300);
        store.File.Log = new RegistryLog(Path.Combine(Root, "logs", "registry-errors.log"));
        return store;
    }

    private static CrewSettings Sample() => new()
    {
        Members =
        {
            new CrewMember { Initials = "JBB", Name = "Jeff Bearson", ScheduleEmployeeId = "jeff_bearson", User = "jbearson" },
            new CrewMember { Initials = "JAM", Name = "Jim Martin", ScheduleEmployeeId = "jim_martin" },
            new CrewMember { Initials = "OLD", Name = "Old Hand", ScheduleEmployeeId = "old_hand", Active = false },
        },
        ActivityMap =
        {
            new ActivityMapping { WorkType = "TOPO", Activities = { "Topo", "Topographic" } },
            new ActivityMapping { WorkType = "STAKE", Activities = { "Staking" } },
            new ActivityMapping { WorkType = "CTRL", Activities = { "Control" } },
        },
    };

    [Fact]
    public void LivesBesideTheRegistry() =>
        Assert.Equal(Path.Combine(Root, "Config", "crew-settings.json"), Config.CrewSettingsPath);

    [Fact]
    public void LooksPeopleUpEveryWay()
    {
        var s = Sample();
        Assert.Equal("jeff_bearson", s.ByInitials(" jbb ")!.ScheduleEmployeeId);
        Assert.Equal("JAM", s.ByScheduleId("jim_martin")!.Initials);
        Assert.Equal("JBB", s.ByUser("PMX\\JBearson")!.Initials);
        Assert.Null(s.ByScheduleId("old_hand")); // inactive
        Assert.Null(s.ByInitials("XYZ"));
    }

    [Theory]
    [InlineData("Topo", "TOPO")]
    [InlineData("topo", "TOPO")]
    [InlineData("  Topographic ", "TOPO")]
    [InlineData("Topo, 7:00 start", null)]          // comments are never searched for words
    [InlineData("Set control, 7:00 start", null)]
    [InlineData("Control", "CTRL")]
    [InlineData("Topo and staking", null)]
    [InlineData("Topographical", null)]
    [InlineData("Processing", null)]          // not mapped
    [InlineData("", null)]
    public void MapsActivitiesOnlyAsThePmSaid(string activity, string expected) =>
        Assert.Equal(expected, Sample().WorkTypeFor(activity));

    [Fact]
    public void NothingMapsByDefault() => Assert.Null(new CrewSettings().WorkTypeFor("Topo"));

    [Fact]
    public void WorksWithAnyCodesInTheListNotJustThePlaceholders()
    {
        var s = new CrewSettings { ActivityMap = { new ActivityMapping { WorkType = "STK", Activities = { "Staking" } }, new ActivityMapping { WorkType = "ASBUILT", Activities = { "As-builts" } } } };
        Assert.Equal("STK", s.WorkTypeFor("staking"));
        Assert.Equal("ASBUILT", s.WorkTypeFor("As-builts"));
    }

    [Fact]
    public void CatchesBadSettings()
    {
        var s = Sample();
        s.Members.Add(new CrewMember { Initials = "JBB", Name = "Someone else" });
        Assert.Contains("JBB is used by more than one", s.Problems());

        s = Sample();
        s.Members.Add(new CrewMember { Initials = "J1" });
        Assert.Contains("not 2-4 capital letters", s.Problems());

        s = Sample();
        s.Members.Add(new CrewMember { Initials = "JBX", ScheduleEmployeeId = "jeff_bearson" });
        Assert.Contains("more than one set of initials", s.Problems());

        s = Sample();
        s.ActivityMap.Add(new ActivityMapping { WorkType = "ASBLT", Activities = { "topo" } });
        Assert.Contains("mapped to more than one work type", s.Problems());

        s = Sample();
        s.Members.Add(new CrewMember { Initials = "OLD", Name = "New person, same initials" }); // the other OLD is inactive
        Assert.Null(s.Problems());
    }

    [Fact]
    public void SavesAndReadsBack()
    {
        var store = Store();
        Assert.Empty(store.Load().Members); // no file yet: empty, not an error
        var saved = store.Save(Sample(), "pm1", 0);
        Assert.Equal(1, saved.Revision);
        var back = Store().Load();
        Assert.Equal(3, back.Members.Count);
        Assert.Equal("pm1", back.SavedBy);
        Assert.Equal("TOPO", back.WorkTypeFor("Topo"));

        var second = Sample();
        second.Members.RemoveAt(1);
        Store().Save(second, "pm2", 1);
        Assert.True(File.Exists(Store().File.BackupPath(1)));
        Assert.Equal(2, Store().Load().Members.Count);
        Assert.Empty(Directory.GetFiles(Path.Combine(Root, "Config"), "*.tmp-*"));
    }

    [Fact]
    public void AStaleEditorCannotOverwrite()
    {
        Store().Save(Sample(), "pm1", 0);
        Store().Save(Sample(), "pm2", 1);
        var e = Assert.Throws<RegistryConflictException>(() => Store().Save(Sample(), "pm3", 1));
        Assert.Contains("pm2", e.Message);
        Assert.Equal(2, Store().Load().Revision);
    }

    [Fact]
    public void InvalidSettingsAreNeverWritten()
    {
        Store().Save(Sample(), "pm1", 0);
        var bad = Sample();
        bad.Members.Add(new CrewMember { Initials = "JAM" });
        Assert.Throws<InvalidDataException>(() => Store().Save(bad, "pm1", 1));
        Assert.Equal(1, Store().Load().Revision);
    }

    [Fact]
    public void DamagedFileFallsBackToTheBackupAndCrewsCarryOn()
    {
        Store().Save(Sample(), "pm1", 0);
        Store().Save(Sample(), "pm1", 1);
        File.WriteAllText(Config.CrewSettingsPath, "{ broken");
        Assert.Equal(1, Store().Load().Revision); // backup-1 is revision 1

        foreach (var b in Directory.GetFiles(Path.Combine(Root, "Config"), "crew-settings.backup-*")) File.Delete(b);
        Assert.Throws<InvalidDataException>(() => Store().Load());
        var crew = Store().TryLoad(out var problem);
        Assert.Empty(crew.Members);
        Assert.NotNull(problem);
    }
}
