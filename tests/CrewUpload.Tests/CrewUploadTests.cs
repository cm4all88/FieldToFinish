using CrewUpload;
using Newtonsoft.Json;

namespace CrewUpload.Tests;

public sealed class CrewUploadTests : IDisposable
{
    private readonly string _root;
    private readonly string _clients;
    private readonly string _card;
    private readonly JobFolderConfig _config;

    public CrewUploadTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "crew-" + Path.GetRandomFileName());
        _clients = Path.Combine(_root, "Clients");
        _card = Path.Combine(_root, "FLD_Download");
        Directory.CreateDirectory(_clients);
        Directory.CreateDirectory(_card);
        _config = JobFolderConfig.CreateDefault();
        _config.JobsRoot = _clients;
        _config.BaseDirectory = _root;
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private const string Download = "20260128-JAM-1521-799-TOPO";
    private static readonly FieldVisit Visit = new() { Crew = "jam", Date = new DateTime(2026, 1, 28), WorkType = "topo" };
    private static readonly string[] Downloads = { "02Field", "01FLD_DR_FN_DCfile", "Unprocessed" };

    private string CardFile(string relative, string content = "x")
    {
        var path = Path.Combine(new[] { _card }.Concat(relative.Split('\\')).ToArray());
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>The download from the crew's screenshot.</summary>
    private string CrewDownload(string name = Download)
    {
        CardFile(name + @"\Photos\IMG_0412.JPG", "p1");
        CardFile(name + @"\Photos\IMG_0413.JPG", "p2");
        CardFile(name + @"\" + name + "-ASB.pdf", "asb");
        CardFile(name + @"\" + name + "-FN.pdf", "fn");
        CardFile(name + @"\" + name + ".job", "job");
        CardFile(name + @"\" + name + ".jxl", "jxl");
        return Path.Combine(_card, name);
    }

    /// <summary>An existing project's base Survey folder on the "share", as the office makes them.</summary>
    private string SurveyDir(string client = "1521-CityOfOrting", string project = "554-1521-799 Main St Topo")
    {
        var path = Path.Combine(_clients, client, project, "99Svcs", "Survey");
        Directory.CreateDirectory(path);
        return path;
    }

    // Temp folders are not UNC; the UNC rule itself is tested on its own.
    private string RegistryFile => Path.Combine(_root, "Config", "project-registry.json");

    private string LogFile => Path.Combine(_root, "logs", "registry-errors.log");

    private ProjectRegistry Registry() => new(RegistryFile, requireUnc: false, backups: 5, retries: 5, retryDelayMs: 50)
    {
        Log = new RegistryLog(LogFile),
        RetryDelaysMs = new[] { 1, 1, 1, 1 },
        LockWait = TimeSpan.FromMilliseconds(300),
    };

    private string LockFile => Path.Combine(Path.GetDirectoryName(RegistryFile)!, "project-registry.lock");

    private string Log() => File.Exists(LogFile) ? File.ReadAllText(LogFile) : string.Empty;

    /// <summary>What a Windows share throws for a sharing violation (ERROR_SHARING_VIOLATION, 32).</summary>
    private static IOException SharingViolation() =>
        new("The process cannot access the file because it is being used by another process.", unchecked((int)0x80070020));

    private ProjectStore Store() => new(_config, Registry());

    /// <summary>The PM registers 1521-799 to its Survey folder; the crew then finds it by number.</summary>
    private ProjectFolder Project()
    {
        Registry().Register("1521-799", SurveyDir(), "pm");
        return Store().Find("1521-799")!;
    }

    private string InDownloads(ProjectFolder p, params string[] rest) => Path.Combine(new[] { p.Path }.Concat(Downloads).Concat(rest).ToArray());

    /// <summary>Relative to the registered Survey folder.</summary>
    private static string Rel(ProjectFolder p, string path) => Path.GetRelativePath(p.Path, path).Replace(Path.DirectorySeparatorChar, '\\');

    private (UploadPlanner planner, List<UploadItem> items) Plan(ProjectFolder p, params string[] dropped)
    {
        var planner = new UploadPlanner(_config);
        var items = planner.Collect(dropped);
        planner.Assign(p, items, Visit);
        return (planner, items);
    }

    // ------------------------------------------------------------------ config

    [Fact]
    public void ShippedConfigIsTheDefaultAndValid()
    {
        var shipped = JobFolderConfig.Load(Path.Combine(AppContext.BaseDirectory, JobFolderConfig.DefaultFileName));
        Assert.Equal(JsonConvert.SerializeObject(JobFolderConfig.CreateDefault()), JsonConvert.SerializeObject(shipped));
        var problems = new List<string>();
        shipped.Validate(problems);
        Assert.Empty(problems);
    }

    [Fact]
    public void TheShareIsReachedByItsUncPathNotADriveLetter() =>
        Assert.True(ProjectRegistry.IsUnc(JobFolderConfig.CreateDefault().JobsRoot));

    [Fact]
    public void ValidateCatchesFoldersLeavingTheProject()
    {
        _config.UnprocessedFolder = @"..\Elsewhere";
        _config.Categories[0].Folder = @"..\..\Elsewhere";
        var problems = new List<string>();
        _config.Validate(problems);
        Assert.Contains(problems, p => p.Contains("unprocessedFolder"));
        Assert.Contains(problems, p => p.Contains("inside the download folder"));
    }

    [Fact]
    public void BrandingColoursAreChecked()
    {
        _config.Branding.PrimaryColor = "charcoal";
        var problems = new List<string>();
        _config.Validate(problems);
        Assert.Contains(problems, x => x.Contains("primaryColor"));
    }

    [Theory]
    [InlineData("1800-119", "1800-119")]
    [InlineData(" 1521-799 ", "1521-799")]
    [InlineData("554-1800-119", "1800-119")]
    [InlineData("1800-11", "1800-011")]
    [InlineData("800-7", "0800-007")]
    public void ProjectNumbersAreClientTask(string typed, string number) =>
        Assert.Equal(number, JobFolderConfig.NormalizeProjectNumber(typed));

    [Theory]
    [InlineData("1800-119", "1800-119", null)]
    [InlineData("1800-119-141", "1800-119", "141")]
    [InlineData("554-1800-119", "1800-119", null)]
    [InlineData("554-1800-119-141", "1800-119", "141")]
    [InlineData("1800-11-2", "1800-011", "2")]
    public void PhasesAreReadFromTheNumber(string typed, string clientTask, string? phase)
    {
        Assert.True(JobFolderConfig.ParseProjectNumber(typed, out var ct, out var ph));
        Assert.Equal((clientTask, phase), (ct, ph));
    }

    [Fact]
    public void APhaseFollowsClientTaskInEveryName()
    {
        var p = Project();
        var visit = new FieldVisit { Crew = "jbb", Date = new DateTime(2026, 10, 5), WorkType = "TOPO", Phase = "141" };
        Assert.Equal("20261005-JBB-1521-799-141-TOPO", DownloadNames.Name(_config, "1521-799", visit));

        CardFile("IMG_0412.JPG");
        CardFile("x.job");
        var planner = new UploadPlanner(_config);
        var items = planner.Collect(Directory.GetFiles(_card).OrderBy(f => f));
        planner.Assign(p, items, visit);
        Assert.Equal(new[] { "1521-799-141-20261005-0412.jpg", "20261005-JBB-1521-799-141-TOPO.job" }, items.Select(i => Path.GetFileName(i.Destination)));
        // Same registered project: the phase does not change where it goes.
        Assert.All(items, i => Assert.StartsWith(p.UploadRoot, i.Destination));
    }

    [Fact]
    public void ADownloadNamedWithAPhaseIsReadBack()
    {
        var parsed = DownloadNames.Parse(_config, "20261005-JBB-1800-119-141-TOPO")!;
        Assert.Equal(("1800-119", "141", "TOPO"), (parsed.ProjectNumber, parsed.Visit.Phase, parsed.Visit.WorkType));
        Assert.Null(DownloadNames.Parse(_config, "20261005-JBB-1800-119-TOPO")!.Visit.Phase);
    }

    [Fact]
    public void TheBoxesAreJobFilesFieldNotesAsBuiltNotesPhotos() =>
        Assert.Equal(new[] { "Job files", "Field notes", "As-built notes", "Photos" }, _config.Categories.Select(c => c.Name));

    [Theory]
    [InlineData("1800-119", true)]
    [InlineData("554-1800-119", true)]
    [InlineData("1800", false)]
    [InlineData("", false)]
    [InlineData("SILVER LAKE", false)]
    public void ProjectNumbers(string number, bool ok) => Assert.Equal(ok, _config.IsValidProjectNumber(number));

    // ---------------------------------------------------------- download names

    [Fact]
    public void DownloadNameIsDateCrewProjectWorkType() =>
        Assert.Equal(Download, DownloadNames.Name(_config, "1521-799", Visit));

    [Fact]
    public void ADownloadFolderNameIsReadBack()
    {
        var parsed = DownloadNames.Parse(_config, "20261005-JBB-1800-119-TOPO")!;
        Assert.Equal("1800-119", parsed.ProjectNumber);
        Assert.Equal("JBB", parsed.Visit.Crew);
        Assert.Equal(new DateTime(2026, 10, 5), parsed.Visit.Date);
        Assert.Equal("TOPO", parsed.Visit.WorkType);
    }

    [Theory]
    [InlineData("Photos")]
    [InlineData("20261399-JAM-1521-799-TOPO")] // no 99th day
    [InlineData("20260128-JAM-TOPO")]
    public void OtherFolderNamesAreNotDownloads(string name) => Assert.Null(DownloadNames.Parse(_config, name));

    // ------------------------------------------------------------ registration

    [Fact]
    public void APmRegistersTheProjectsSurveyFolderOnce()
    {
        var survey = SurveyDir("1800-HDR", "554-1800-119 TDLE Phase 3");
        Registry().Register("1800-119", survey, "pm1");

        var p = Store().Find("1800-119")!;
        Assert.Equal(survey, p.Path);
        Assert.Equal(Path.Combine(survey, "02Field", "01FLD_DR_FN_DCfile", "Unprocessed"), p.UploadRoot);
        Assert.Equal("554-1800-119 TDLE Phase 3", p.Info.ProjectName);
        Assert.Equal("1800-HDR", p.Info.Client);
        var reg = Registry().Find("1800-119")!;
        Assert.Equal(("1800", "119", "pm1"), (reg.Client, reg.Task, reg.RegisteredBy));
    }

    [Fact]
    public void AnUnregisteredProjectIsNotFoundEvenIfItsFolderExists()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(RegistryFile)!);
        SurveyDir("1800-HDR", "554-1800-119 TDLE Phase 3");
        Assert.Null(Store().Find("1800-119"));
    }

    [Fact]
    public void ThePmCanMoveARegistrationAndTheOldPathIsKept()
    {
        var first = SurveyDir("1800-HDR", "554-1800-119 TDLE Phase 3");
        var second = SurveyDir("1800-HDR", "554-1800-119 TDLE Phase 3 (moved)");
        Registry().Register("1800-119", first, "pm1");
        Registry().Register("1800-119", second, "pm2");

        var reg = Registry().Find("1800-119")!;
        Assert.Equal(second, reg.SurveyFolder);
        Assert.Equal(new[] { RegistrationChange.Registered, RegistrationChange.Moved }, reg.History.Select(h => h.Change));
        var moved = reg.History[1];
        Assert.Equal((second, first, "pm2"), (moved.SurveyFolder, moved.PreviousFolder, moved.By));
        Assert.Single(Registry().All());
    }

    [Fact]
    public void RegistrationKeepsOtherProjects()
    {
        Registry().Register("1800-119", SurveyDir("1800-HDR", "554-1800-119 TDLE"), "pm");
        Registry().Register("1711-042", SurveyDir("1711-CityOfOrting", "1711-042 Harman Way"), "pm");
        Assert.Equal(new[] { "1711-042", "1800-119" }, Registry().All().Select(r => r.ProjectNumber));
    }

    [Fact]
    public void OnlyAnExistingFolderCanBeRegisteredAndNothingIsCreated()
    {
        var missing = Path.Combine(_clients, "1800-HDR", "nope", "Survey");
        Assert.Throws<DirectoryNotFoundException>(() => Registry().Register("1800-119", missing, "pm"));
        Assert.False(Directory.Exists(Path.Combine(_clients, "1800-HDR")));
    }

    [Fact]
    public void ADriveLetterPathIsRefusedWhenUncIsRequired()
    {
        var registry = new ProjectRegistry(Path.Combine(_root, "r.json"), requireUnc: true);
        var e = Assert.Throws<ArgumentException>(() => registry.Register("1800-119", SurveyDir(), "pm"));
        Assert.Contains("UNC", e.Message);
        Assert.True(ProjectRegistry.IsUnc(@"\\parametrix.com\pmx\PSO\Projects\Clients\1800-HDR\554-1800-119 TDLE Phase 3\99Svcs\Survey"));
        Assert.False(ProjectRegistry.IsUnc(@"U:\PSO\Projects\Clients"));
    }

    [Fact]
    public void ShippedConfigKeepsTheRegistryInTheSharedConfigFolder()
    {
        var c = JobFolderConfig.CreateDefault();
        Assert.True(c.RequireUncPaths);
        c.BaseDirectory = _root;
        Assert.Equal(@"\\parametrix.com\pmx\PSO\Shared\Divisions\00Survey\FLD\CrewUpload\Config\project-registry.json", c.RegistryPath);
        Assert.Equal(5, c.RegistryBackups);
        Assert.Equal(@"02Field\01FLD_DR_FN_DCfile\Unprocessed", c.UnprocessedFolder);
    }

    [Fact]
    public void ARegistryBesideTheAppIsRefused()
    {
        var c = JobFolderConfig.CreateDefault();
        c.RegistryFile = "project-registry.json";
        var problems = new List<string>();
        c.Validate(problems);
        Assert.Contains(problems, p => p.Contains("registryFile must be a permanent UNC path"));
    }

    [Fact]
    public void ThePmListIsOptionalAndCaseInsensitive()
    {
        var c = JobFolderConfig.CreateDefault();
        Assert.True(c.IsProjectManager("anyone"));
        c.ProjectManagers.Add("PMSmith");
        Assert.True(c.IsProjectManager("pmsmith"));
        Assert.True(c.IsProjectManager("x", "PMSmith"));
        Assert.False(c.IsProjectManager("jbb"));
    }

    // ---------------------------------------------------------- registry file

    [Fact]
    public void KeysKeepTheirLeadingZeros()
    {
        Registry().Register("1800-119", SurveyDir("1800-HDR", "554-1800-119 TDLE"), "pm");
        Registry().Register("1800-011", SurveyDir("1800-HDR", "554-1800-011 Other"), "pm");
        Assert.Equal(new[] { "1800-011", "1800-119" }, Registry().All().Select(r => r.Key));
        Assert.EndsWith("554-1800-011 Other" + Path.DirectorySeparatorChar + "99Svcs" + Path.DirectorySeparatorChar + "Survey", Registry().Find("1800-11")!.SurveyFolder);
    }

    [Fact]
    public void EverySaveReplacesTheWholeFileAndKeepsFiveBackups()
    {
        for (var i = 0; i < 7; i++)
            Registry().Register("1800-1" + i.ToString("00"), SurveyDir("1800-HDR", "554-1800-1" + i.ToString("00") + " P"), "pm");

        var registry = Registry();
        var live = registry.Load();
        Assert.Equal(7, live.Revision);
        Assert.Equal(7, live.Projects.Count);
        for (var n = 1; n <= 5; n++) Assert.True(File.Exists(registry.BackupPath(n)), "backup-" + n);
        Assert.False(File.Exists(registry.BackupPath(6)));
        // backup-1 is the version just before the live one.
        Assert.Contains("\"revision\": 6", File.ReadAllText(registry.BackupPath(1)));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(RegistryFile)!, "*.tmp-*"));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(RegistryFile)!, "*.lock"));
    }

    [Fact]
    public void AWriteThatDoesNotReadBackNeverReplacesTheLiveFile()
    {
        Registry().Register("1800-119", SurveyDir("1800-HDR", "554-1800-119 TDLE"), "pm");
        var before = File.ReadAllText(RegistryFile);

        var registry = Registry();
        registry.AfterTempWritten = temp => File.WriteAllText(temp, "{ \"projects\": [ broken");
        Assert.Throws<InvalidDataException>(() => registry.Register("1711-042", SurveyDir("1711-Orting", "1711-042 Harman"), "pm"));

        Assert.Equal(before, File.ReadAllText(RegistryFile));
        Assert.False(File.Exists(registry.BackupPath(1)));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(RegistryFile)!, "*.tmp-*"));
        Assert.Null(Registry().Find("1711-042"));
    }

    [Fact]
    public void AChangeToTheSameProjectByAnotherPmIsNotOverwritten()
    {
        var a = SurveyDir("1800-HDR", "554-1800-119 TDLE");
        var b = SurveyDir("1800-HDR", "554-1800-119 TDLE moved");
        var c = SurveyDir("1800-HDR", "554-1800-119 TDLE elsewhere");
        Registry().Register("1800-119", a, "pm1");

        var myScreen = Registry().Load();               // PM 2 opens setup
        Registry().Register("1800-119", b, "pm3");      // PM 3 moves it meanwhile
        var e = Assert.Throws<RegistryConflictException>(() => Registry().Register("1800-119", c, "pm2", myScreen));
        Assert.Contains("pm3", e.Message);
        Assert.Contains("Refresh", e.Message);
        Assert.Equal(b, Registry().Find("1800-119")!.SurveyFolder);
    }

    [Fact]
    public void ChangesToOtherProjectsMeanwhileAreMergedNotLost()
    {
        Registry().Register("1800-119", SurveyDir("1800-HDR", "554-1800-119 TDLE"), "pm1");
        var myScreen = Registry().Load();
        Registry().Register("1711-042", SurveyDir("1711-Orting", "1711-042 Harman"), "pm3");

        var result = Registry().SetActive("1800-119", false, "pm2", myScreen);
        Assert.True(result.MergedOtherChanges);
        Assert.NotNull(Registry().Find("1711-042"));
        Assert.False(Registry().Find("1800-119")!.Active);
        Assert.Equal(3, Registry().Load().Revision);
    }

    [Fact]
    public void TwoPmsSavingAtOnceBothLand()
    {
        var folders = Enumerable.Range(0, 10).Select(i => SurveyDir("1800-HDR", "554-1800-2" + i.ToString("00") + " P")).ToList();
        Parallel.For(0, 10, i =>
        {
            var registry = Registry();
            registry.LockWait = TimeSpan.FromSeconds(30); // ten PMs queueing for one lock
            registry.Register("1800-2" + i.ToString("00"), folders[i], "pm" + i);
        });
        var live = Registry().Load();
        Assert.Equal(10, live.Projects.Count);
        Assert.Equal(10, live.Revision);
    }

    [Fact]
    public void CrewReadsRideOutABrieflyLockedFile()
    {
        Registry().Register("1800-119", SurveyDir("1800-HDR", "554-1800-119 TDLE"), "pm");
        var held = new FileStream(RegistryFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var release = Task.Run(async () => { await Task.Delay(120); held.Dispose(); });
        Assert.NotNull(Registry().Find("1800-119"));
        release.Wait();
    }

    [Fact]
    public void ADamagedLiveFileFallsBackToTheLastGoodBackupAndBlocksSaves()
    {
        Registry().Register("1800-119", SurveyDir("1800-HDR", "554-1800-119 TDLE"), "pm");
        Registry().Register("1711-042", SurveyDir("1711-Orting", "1711-042 Harman"), "pm");
        File.WriteAllText(RegistryFile, "{ hand edited, oops");

        var snapshot = Registry().Load();
        Assert.Equal(Registry().BackupPath(1), snapshot.ReadFromBackup);
        Assert.NotNull(snapshot.Get("1800-119"));
        var e = Assert.Throws<InvalidDataException>(() => Registry().Register("1700-001", SurveyDir("1700-X", "1700-001 Y"), "pm"));
        Assert.Contains("Restore", e.Message);
        Assert.Equal("{ hand edited, oops", File.ReadAllText(RegistryFile));
    }

    // ------------------------------------------------------- share failure cases

    private void WriteOwner(string user, string machine, DateTime acquiredUtc, bool released = false)
    {
        File.WriteAllText(LockFile + ".owner.json", Newtonsoft.Json.JsonConvert.SerializeObject(new
        {
            user, machine, processId = 4242, acquiredUtc, releasedUtc = released ? acquiredUtc.AddSeconds(1) : (DateTime?)null,
        }));
    }

    [Fact]
    public void ALockHeldByAnotherPmIsNeverRemoved()
    {
        Registry().Register("1800-119", SurveyDir("1800-HDR", "554-1800-119 TDLE"), "pm1");
        var before = File.ReadAllText(RegistryFile);
        using var held = new FileStream(LockFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        WriteOwner(@"PMX\pmother", "PC-0099", DateTime.UtcNow.AddSeconds(-5));

        var e = Assert.Throws<RegistryLockedException>(() => Registry().Register("1711-042", SurveyDir("1711-Orting", "1711-042 Harman"), "pm2"));
        Assert.Contains(@"PMX\pmother on PC-0099", e.Message);
        Assert.Contains("try again", e.Message);
        Assert.True(File.Exists(LockFile));
        held.WriteByte(1); // still ours, still open
        Assert.Equal(before, File.ReadAllText(RegistryFile));
        Assert.Contains("op=\"acquire registry lock\"", Log());
    }

    [Fact]
    public void ALockHeldPastTheTimeoutIsReportedButStillNotBroken()
    {
        using var held = new FileStream(Path.Combine(Directory.CreateDirectory(Path.GetDirectoryName(RegistryFile)!).FullName, "project-registry.lock"),
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        WriteOwner(@"PMX\pmcrashed", "PC-0042", DateTime.UtcNow.AddMinutes(-12));
        var registry = Registry();
        registry.StaleLockAfter = TimeSpan.FromMinutes(2);

        var e = Assert.Throws<RegistryLockedException>(() => registry.Register("1800-119", SurveyDir("1800-HDR", "554-1800-119 TDLE"), "pm2"));
        Assert.Contains("PC-0042", e.Message);
        Assert.Contains("12 min", e.Message);
        Assert.Contains("never breaks a lock that is still held", e.Message);
        Assert.Contains("Open Files", e.Message);
        Assert.True(File.Exists(LockFile));
        Assert.False(File.Exists(RegistryFile));
    }

    [Fact]
    public void ALeftoverLockThatNobodyHoldsIsRecoveredAndLogged()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(RegistryFile)!);
        File.WriteAllText(LockFile, "{\"user\":\"pmcrashed\"}");   // the crash left the file behind
        WriteOwner(@"PMX\pmcrashed", "PC-0042", DateTime.UtcNow.AddHours(-3));

        Registry().Register("1800-119", SurveyDir("1800-HDR", "554-1800-119 TDLE"), "pm2");
        Assert.NotNull(Registry().Find("1800-119"));
        var log = Log();
        Assert.Contains("op=\"recover stale registry lock\"", log);
        Assert.Contains("PC-0042", log);
        Assert.False(File.Exists(LockFile) && new FileInfo(LockFile).Length > 0);
    }

    [Fact]
    public void ACleanReleaseIsNotReportedAsStale()
    {
        Registry().Register("1800-119", SurveyDir("1800-HDR", "554-1800-119 TDLE"), "pm1");
        Registry().Register("1711-042", SurveyDir("1711-Orting", "1711-042 Harman"), "pm2");
        Assert.DoesNotContain("recover stale", Log());
    }

    [Fact]
    public void ALockThePmCannotOpenIsReportedAsPermissionNotBusy()
    {
        // A folder where the lock file should be: opening it is "access denied" on every attempt,
        // like a share where this user lacks Modify.
        Directory.CreateDirectory(LockFile);
        var e = Record.Exception(() => Registry().Register("1800-119", SurveyDir("1800-HDR", "554-1800-119 TDLE"), "pm"));
        Assert.IsType<UnauthorizedAccessException>(e);
        Assert.Contains("check Modify on the folder", Log());
        Assert.False(File.Exists(RegistryFile));
    }

    [Fact]
    public void ASharingViolationOnTheSwapIsRetried()
    {
        Registry().Register("1800-119", SurveyDir("1800-HDR", "554-1800-119 TDLE"), "pm1");
        var registry = Registry();
        registry.BeforeFileStep = (op, attempt) => { if (op == "replace live registry" && attempt <= 2) throw SharingViolation(); };

        registry.Register("1711-042", SurveyDir("1711-Orting", "1711-042 Harman"), "pm2");
        Assert.NotNull(Registry().Find("1711-042"));
        Assert.Equal(2, Registry().Load().Revision);
        var log = Log();
        Assert.Contains("win32=32", log);
        Assert.Contains("succeeded after 3 attempts", log);
    }

    [Theory]
    [InlineData("write temporary registry")]
    [InlineData("read back temporary registry")]
    [InlineData("copy live registry to backup")]
    [InlineData("replace live registry")]
    public void APersistentShareErrorLeavesTheLiveRegistryExactlyAsItWas(string failingStep)
    {
        Registry().Register("1800-119", SurveyDir("1800-HDR", "554-1800-119 TDLE"), "pm1");
        Registry().Register("1800-011", SurveyDir("1800-HDR", "554-1800-011 Early"), "pm1");
        var folder = Path.GetDirectoryName(RegistryFile)!;
        var before = File.ReadAllText(RegistryFile);
        var backupsBefore = Directory.GetFiles(folder, "*.backup-*").OrderBy(f => f).Select(File.ReadAllText).ToList();

        var registry = Registry();
        registry.BeforeFileStep = (op, attempt) => { if (op == failingStep) throw SharingViolation(); };
        Assert.ThrowsAny<IOException>(() => registry.Register("1711-042", SurveyDir("1711-Orting", "1711-042 Harman"), "pm2"));

        Assert.Equal(before, File.ReadAllText(RegistryFile));
        Assert.Equal(backupsBefore, Directory.GetFiles(folder, "*.backup-*").OrderBy(f => f).Select(File.ReadAllText).ToList());
        Assert.Empty(Directory.GetFiles(folder, "*.tmp-*"));
        Assert.Empty(Directory.GetFiles(folder, "*.backup-new-*"));

        var error = Assert.Single(Log().Split('\n'), l => l.Contains("\tERROR\t") && l.Contains("save registry"));
        Assert.Contains(failingStep, error);
        Assert.Contains("win32=32", error);
        Assert.Contains("user=\"" + RegistryLog.UserName + "\"", error);
        Assert.Contains("machine=\"" + Environment.MachineName + "\"", error);
        Assert.Contains("path=\"" + RegistryFile + "\"", error);
    }

    [Fact]
    public void AnAntivirusLockOnTheBackupCopyIsRetried()
    {
        Registry().Register("1800-119", SurveyDir("1800-HDR", "554-1800-119 TDLE"), "pm1");
        var registry = Registry();
        registry.BeforeFileStep = (op, attempt) =>
        {
            if (op == "copy live registry to backup" && attempt == 1) throw new UnauthorizedAccessException("Access to the path is denied.");
        };
        registry.Register("1711-042", SurveyDir("1711-Orting", "1711-042 Harman"), "pm2");
        Assert.Contains("\"revision\": 1", File.ReadAllText(registry.BackupPath(1)));
    }

    [Fact]
    public void ABackupRotationFailureStillSavesAndKeepsThePreviousVersion()
    {
        Registry().Register("1800-119", SurveyDir("1800-HDR", "554-1800-119 TDLE"), "pm1");
        var registry = Registry();
        registry.BeforeFileStep = (op, attempt) => { if (op == "delete oldest backup") throw SharingViolation(); };

        var result = registry.Register("1711-042", SurveyDir("1711-Orting", "1711-042 Harman"), "pm2");
        Assert.NotNull(result.Warning);
        Assert.Equal(2, Registry().Load().Revision);
        var kept = Assert.Single(Directory.GetFiles(Path.GetDirectoryName(RegistryFile)!, "*.backup-new-*"));
        Assert.Contains("\"revision\": 1", File.ReadAllText(kept));
        Assert.Contains("op=\"rotate registry backups\"", Log());
    }

    [Fact]
    public void AReplaceThatFinishedButLostItsReplyIsNotRetriedIntoAFailure()
    {
        Registry().Register("1800-119", SurveyDir("1800-HDR", "554-1800-119 TDLE"), "pm1");
        var registry = Registry();
        var folder = Path.GetDirectoryName(RegistryFile)!;
        registry.BeforeFileStep = (op, attempt) =>
        {
            if (op != "replace live registry" || attempt != 1) return;
            File.Replace(Directory.GetFiles(folder, "*.tmp-*").Single(), RegistryFile, null); // the server did it ...
            throw new IOException("The specified network name is no longer available.", unchecked((int)0x80070040)); // ... the reply was lost
        };
        registry.Register("1711-042", SurveyDir("1711-Orting", "1711-042 Harman"), "pm2");
        Assert.Equal(2, Registry().Load().Revision);
        Assert.Contains("win32=64", Log());
        Assert.DoesNotContain("\tERROR\t", Log());
    }

    [Fact]
    public void TheWindowsErrorCodeIsReadFromTheException()
    {
        Assert.Equal("win32=32 hresult=0x80070020", RegistryLog.WindowsError(SharingViolation()));
        Assert.Equal("win32=5 hresult=0x80070005", RegistryLog.WindowsError(new UnauthorizedAccessException()));
    }

    [Fact]
    public void AnInactiveProjectLeavesCrewSelectionButKeepsItsHistory()
    {
        Registry().Register("1800-119", SurveyDir("1800-HDR", "554-1800-119 TDLE"), "pm");
        Registry().Register("1711-042", SurveyDir("1711-Orting", "1711-042 Harman"), "pm");
        Registry().SetActive("1800-119", false, "pm");

        string problem;
        Assert.Null(Store().Find("1800-119", out problem));
        Assert.Contains("inactive", problem);
        Assert.Equal(new[] { "1711-042" }, Store().ActiveProjects());
        var reg = Registry().Find("1800-119")!;
        Assert.Equal(RegistrationChange.Deactivated, reg.History.Last().Change);

        Registry().SetActive("1800-119", true, "pm");
        Assert.NotNull(Store().Find("1800-119"));
        Assert.Equal(3, Registry().Find("1800-119")!.History.Count);
    }

    [Fact]
    public void AVersionOneRegistryIsStillRead()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(RegistryFile)!);
        var survey = SurveyDir("1800-HDR", "554-1800-119 TDLE").Replace("\\", "\\\\");
        File.WriteAllText(RegistryFile, "{\"version\":\"1\",\"projects\":[{\"client\":\"1800\",\"task\":\"119\",\"surveyFolder\":\"" + survey + "\"}]}");
        var reg = Registry().Find("1800-119")!;
        Assert.Equal("1800-119", reg.Key);
        Assert.True(reg.Active);
    }

    // ------------------------------------------------------------- classifying

    [Theory]
    [InlineData("IMG_0412.JPG", "photos")]
    [InlineData("Field Notes 01-28.pdf", "notes")]
    [InlineData("JAM FN.pdf", "notes")]
    [InlineData("As-Built Notes.pdf", "asbuilt")]
    [InlineData("asbuilt.pdf", "asbuilt")]
    [InlineData("TOPO.job", "data")]
    [InlineData("points.csv", "data")]
    [InlineData("drawing.pdf", "data")]
    public void ClassifiesByNameThenExtension(string file, string key) =>
        Assert.Equal(key, new UploadPlanner(_config).Classify(file).Key);

    [Fact]
    public void TheCrewDownloadSortsItself()
    {
        var items = new UploadPlanner(_config).Collect(new[] { CrewDownload() });
        var types = items.ToDictionary(i => Path.GetFileName(i.SourcePath), i => i.Category!.Key);
        Assert.Equal("asbuilt", types[Download + "-ASB.pdf"]);
        Assert.Equal("notes", types[Download + "-FN.pdf"]);
        Assert.Equal("data", types[Download + ".job"]);
        Assert.Equal("data", types[Download + ".jxl"]);
        Assert.Equal("photos", types["IMG_0412.JPG"]);
    }

    [Fact]
    public void CrewInitialsInTheDownloadNameNeverDecideTheType()
    {
        // A crew whose initials are "FN" must not turn every file into field notes.
        const string dl = "20260128-FN-1521-799-TOPO";
        CardFile(dl + @"\" + dl + ".job");
        CardFile(dl + @"\" + dl + "-ASB.pdf");
        var items = new UploadPlanner(_config).Collect(new[] { Path.Combine(_card, dl) });
        var types = items.ToDictionary(i => Path.GetFileName(i.SourcePath), i => i.Category!.Key);
        Assert.Equal("data", types[dl + ".job"]);
        Assert.Equal("asbuilt", types[dl + "-ASB.pdf"]);
    }

    [Fact]
    public void DroppingOnATypesBoxDecidesTheTypeForEverything()
    {
        CardFile(@"Day 3\IMG_1.jpg");
        CardFile(@"Day 3\SVLK.job");
        var items = new UploadPlanner(_config).Collect(new[] { Path.Combine(_card, "Day 3") }, _config.Category("notes"));
        Assert.Equal(2, items.Count);
        Assert.All(items, i => Assert.Equal("notes", i.Category!.Key));
    }

    [Fact]
    public void AnEarlierManifestInTheDownloadIsNotUploaded()
    {
        CrewDownload();
        CardFile(Download + @"\upload-manifest.csv");
        var items = new UploadPlanner(_config).Collect(new[] { Path.Combine(_card, Download) });
        Assert.DoesNotContain(items, i => i.SourcePath.EndsWith("upload-manifest.csv"));
    }

    // ------------------------------------------------------------------ naming

    [Fact]
    public void TheDownloadKeepsItsOwnFolderUnderTheDownloadsFolder()
    {
        var p = Project();
        var (_, items) = Plan(p, CrewDownload());

        var to = items.ToDictionary(i => Path.GetFileName(i.SourcePath), i => Rel(p, i.Destination!));
        var dl = @"02Field\01FLD_DR_FN_DCfile\Unprocessed\" + Download + @"\";
        Assert.Equal(dl + Download + "-ASB.pdf", to[Download + "-ASB.pdf"]);
        Assert.Equal(dl + Download + "-FN.pdf", to[Download + "-FN.pdf"]);
        Assert.Equal(dl + Download + ".job", to[Download + ".job"]);
        Assert.Equal(dl + Download + ".jxl", to[Download + ".jxl"]);
        // Client-task-date-camera number.
        Assert.Equal(dl + @"Photos\1521-799-20260128-0412.jpg", to["IMG_0412.JPG"]);
        Assert.Equal(dl + @"Photos\1521-799-20260128-0413.jpg", to["IMG_0413.JPG"]);
    }

    [Fact]
    public void TheOriginalFolderNameAndSubfoldersArePreserved()
    {
        var p = Project();
        const string odd = "20260128-jam-1521-799-topo";
        CardFile(odd + @"\Sketches\north fence.pdf");
        var (_, items) = Plan(p, Path.Combine(_card, odd));
        Assert.StartsWith(InDownloads(p, odd, "Sketches") + Path.DirectorySeparatorChar, Assert.Single(items).Destination);
    }

    [Fact]
    public void TwoDownloadsAreNeverFlattenedTogether()
    {
        var p = Project();
        var (_, items) = Plan(p, CrewDownload(), CrewDownload("20260129-JAM-1521-799-TOPO"));
        var folders = items.Select(i => Rel(p, i.Destination!).Split('\\')[3]).Distinct().ToList();
        Assert.Equal(new[] { Download, "20260129-JAM-1521-799-TOPO" }, folders);
    }

    [Fact]
    public void LooseFilesAreNamedForTheVisit()
    {
        var p = Project();
        CardFile("notes.pdf");
        CardFile("Job001.job");
        CardFile("asbuilt.pdf");
        var planner = new UploadPlanner(_config);
        var items = planner.Collect(Directory.GetFiles(_card).OrderBy(f => f));
        planner.Assign(p, items, new FieldVisit { Crew = "cmm", Date = new DateTime(2026, 10, 6), WorkType = "LINEOUT" });

        Assert.All(items, i => Assert.Equal(InDownloads(p, "20261006-CMM-1521-799-LINEOUT"), Path.GetDirectoryName(i.Destination)));
        Assert.Equal(new[]
        {
            "20261006-CMM-1521-799-LINEOUT-ASB.pdf",
            "20261006-CMM-1521-799-LINEOUT.job",
            "20261006-CMM-1521-799-LINEOUT-FN.pdf",
        }, items.Select(i => Path.GetFileName(i.Destination)));
    }

    [Fact]
    public void TwoFilesInOneBatchWithTheSameNameAreNumbered()
    {
        var p = Project();
        CardFile("notes page 1.pdf", "1");
        CardFile("notes page 2.pdf", "2");
        var planner = new UploadPlanner(_config);
        var items = planner.Collect(Directory.GetFiles(_card).OrderBy(f => f));
        planner.Assign(p, items, Visit);
        Assert.Equal(new[] { Download + "-FN.pdf", Download + "-FN-2.pdf" }, items.Select(i => Path.GetFileName(i.Destination)));
    }

    [Fact]
    public void ChangingTheTypeRenames()
    {
        var p = Project();
        CardFile("scan.pdf");
        var planner = new UploadPlanner(_config);
        var items = planner.Collect(new[] { Path.Combine(_card, "scan.pdf") });
        planner.Assign(p, items, Visit);
        Assert.Equal(Download + ".pdf", Path.GetFileName(items[0].Destination));

        items[0].Category = _config.Category("asbuilt");
        planner.Assign(p, items, Visit);
        Assert.Equal(Download + "-ASB.pdf", Path.GetFileName(items[0].Destination));
    }

    [Theory]
    [InlineData("IMG_0412", "0412")]
    [InlineData("DSC01234", "01234")]
    [InlineData("PXL_20260128_193045123", "193045123")]
    [InlineData("sketch", "sketch")]
    public void PhotosTakeTheCamerasNumber(string original, string number) =>
        Assert.Equal(number, UploadPlanner.CameraNumber(original));

    [Theory]
    [InlineData("SV-<a>:b", "SV-a-b")]
    [InlineData("SV--FN-", "SV-FN")]
    [InlineData("name. ", "name")]
    [InlineData("a   b", "a b")]
    public void CleanMakesLegalWindowsNames(string raw, string clean) => Assert.Equal(clean, Naming.Clean(raw));

    // ---------------------------------------------------------- already there

    [Fact]
    public void SameNameAndSizeIsAlreadyUploaded()
    {
        var p = Project();
        var (_, first) = Plan(p, CrewDownload());
        Assert.True(new UploadRunner(_config).Run(p, first, "JAM").Complete);

        // The crew adds a photo and drops the folder again.
        CardFile(Download + @"\Photos\IMG_0414.JPG", "p3");
        var (_, again) = Plan(p, Path.Combine(_card, Download));
        Assert.Equal(6, again.Count(i => i.State == UploadState.AlreadyUploaded));
        var result = new UploadRunner(_config).Run(p, again, "JAM");
        Assert.True(result.Complete);
        Assert.Equal(1, result.Copied);
        Assert.Equal(6, result.AlreadyUploaded);
        Assert.Equal(3, Directory.GetFiles(InDownloads(p, Download, "Photos")).Length);
    }

    [Fact]
    public void SameNameDifferentSizeIsAConflictAndIsNotCopied()
    {
        var p = Project();
        var existing = InDownloads(p, Download, Download + "-FN.pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(existing)!);
        File.WriteAllText(existing, "the office's longer copy");

        var (_, items) = Plan(p, CrewDownload());
        var fn = items.Single(i => i.SourcePath.EndsWith("-FN.pdf"));
        Assert.Equal(UploadState.Conflict, fn.State);
        Assert.Equal(existing, fn.ConflictWith);

        var result = new UploadRunner(_config).Run(p, items, "JAM");
        Assert.False(result.Complete);
        Assert.Equal(1, result.Undecided);
        Assert.Equal("the office's longer copy", File.ReadAllText(existing));
        Assert.Contains(File.ReadAllLines(InDownloads(p, Download, "upload-manifest.csv")), l => l.Contains("CONFLICT"));
    }

    [Fact]
    public void AConflictKeptBothIsUploadedNextToTheOther()
    {
        var p = Project();
        var existing = InDownloads(p, Download, Download + "-FN.pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(existing)!);
        File.WriteAllText(existing, "the office's longer copy");

        var (planner, items) = Plan(p, CrewDownload());
        var fn = items.Single(i => i.SourcePath.EndsWith("-FN.pdf"));
        fn.Choice = ConflictChoice.KeepBoth;
        planner.Assign(p, items, Visit);
        Assert.Equal(InDownloads(p, Download, Download + "-FN-2.pdf"), fn.Destination);
        Assert.True(new UploadRunner(_config).Run(p, items, "JAM").Complete);
        Assert.Equal("the office's longer copy", File.ReadAllText(existing));
        Assert.Equal("fn", File.ReadAllText(fn.Destination!));
    }

    [Fact]
    public void ResolvingAConflictFindsTheFileAlreadyThereUnderAnotherName()
    {
        var p = Project();
        var folder = InDownloads(p, Download);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, Download + "-FN.pdf"), "different and longer");
        File.WriteAllText(Path.Combine(folder, "renamed by the office.pdf"), "fn");

        var (planner, items) = Plan(p, CrewDownload());
        var fn = items.Single(i => i.SourcePath.EndsWith("-FN.pdf"));
        fn.Choice = ConflictChoice.KeepBoth;
        planner.Assign(p, items, Visit);
        Assert.Equal(UploadState.AlreadyUploaded, fn.State);
        Assert.Equal(Path.Combine(folder, "renamed by the office.pdf"), fn.AlreadyUploadedAs);
    }

    [Fact]
    public void AConflictSkippedIsLeftOut()
    {
        var p = Project();
        var existing = InDownloads(p, Download, Download + "-FN.pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(existing)!);
        File.WriteAllText(existing, "the office's longer copy");

        var (planner, items) = Plan(p, CrewDownload());
        items.Single(i => i.SourcePath.EndsWith("-FN.pdf")).Choice = ConflictChoice.Skip;
        planner.Assign(p, items, Visit);
        var result = new UploadRunner(_config).Run(p, items, "JAM");
        Assert.True(result.Complete);
        Assert.Equal(1, result.Skipped);
        Assert.Single(Directory.GetFiles(InDownloads(p, Download), "*FN*"));
    }

    // ---------------------------------------------------------------- uploading

    [Fact]
    public void NothingIsWrittenIfTheRegisteredSurveyFolderIsGone()
    {
        var p = Project();
        var (_, items) = Plan(p, CrewDownload());
        Directory.Delete(p.Path, true);

        var result = new UploadRunner(_config).Run(p, items, "JAM");
        Assert.False(result.Complete);
        Assert.Equal(0, result.Copied);
        Assert.False(Directory.Exists(p.Path));
        Assert.Contains("Ask the PM", items[0].Error);
    }

    [Fact]
    public void TheRunnerRefusesToWriteOutsideUnprocessed()
    {
        var p = Project();
        var (_, items) = Plan(p, CrewDownload());
        var outside = Path.Combine(p.Path, "02Field", "processed.job");
        items[0].Destination = outside;

        new UploadRunner(_config).Run(p, items, "JAM");
        Assert.Contains("outside", items[0].Error);
        Assert.False(File.Exists(outside));
    }

    [Fact]
    public void UploadCopiesVerifiesAndLeavesTheOriginals()
    {
        var p = Project();
        Assert.False(Directory.Exists(InDownloads(p))); // Unprocessed is made on the first upload, inside the registered Survey folder
        var src = CrewDownload();
        var (_, items) = Plan(p, src);

        var result = new UploadRunner(_config).Run(p, items, "JAM");
        Assert.True(result.Complete);
        Assert.Equal(6, result.Copied);
        Assert.All(items, i => Assert.Equal(UploadState.Done, i.State));
        Assert.Equal("fn", File.ReadAllText(InDownloads(p, Download, Download + "-FN.pdf")));
        Assert.Equal(6, Directory.GetFiles(src, "*", SearchOption.AllDirectories).Length);
        Assert.Empty(Directory.GetFiles(p.Path, "*.partial", SearchOption.AllDirectories));
    }

    [Fact]
    public void TheManifestMapsOriginalNamesToFinalNames()
    {
        var p = Project();
        var (_, items) = Plan(p, CrewDownload());
        new UploadRunner(_config).Run(p, items, "JAM");

        var lines = File.ReadAllLines(InDownloads(p, Download, "upload-manifest.csv"));
        Assert.Equal(UploadRunner.ManifestHeader, lines[0]);
        Assert.Equal(7, lines.Length);
        var photo = Assert.Single(lines, l => l.Contains("IMG_0412.JPG"));
        Assert.Contains(",1521-799-20260128-0412.jpg,", photo);
        Assert.EndsWith("uploaded and verified", photo);
    }

    [Fact]
    public void VerificationCatchesACopyOfTheWrongSize()
    {
        var p = Project();
        var (_, items) = Plan(p, CrewDownload());
        var target = items[0].Destination!;
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, "truncated");
        items[0].AlreadyUploadedAs = target;
        items[0].Destination = null;

        var result = new UploadResult();
        UploadRunner.Verify(new[] { items[0] }, result);
        Assert.False(result.Complete);
        Assert.Contains("bytes", items[0].Error);
    }

    [Fact]
    public void ACollisionAtCopyTimeFailsThatFileOnlyAndOverwritesNothing()
    {
        var p = Project();
        CardFile("a.pdf", "1");
        CardFile("b.job", "2");
        var (_, items) = Plan(p, Path.Combine(_card, "a.pdf"), Path.Combine(_card, "b.job"));
        Directory.CreateDirectory(Path.GetDirectoryName(items[0].Destination!)!);
        File.WriteAllText(items[0].Destination!, "someone else");

        var result = new UploadRunner(_config).Run(p, items, "JAM");
        Assert.False(result.Complete);
        Assert.Equal(1, result.Copied);
        Assert.NotNull(items[0].Error);
        Assert.Equal("someone else", File.ReadAllText(items[0].Destination!));
        Assert.Equal(UploadState.Done, items[1].State);
    }

    // ---------------------------------------------------------------- documents

    [Fact]
    public void NewAsBuiltNotesFromTemplateAreNamedForTheVisit()
    {
        var p = Project();
        Directory.CreateDirectory(Path.Combine(_root, "templates"));
        File.WriteAllText(Path.Combine(_root, "templates", "As-Built Notes.docx"), "template");
        var doc = _config.Documents.First(d => d.Category == "asbuilt");

        var path = new DocumentMaker(_config).Create(p, doc, Visit);
        Assert.Equal(InDownloads(p, Download, Download + "-ASB.docx"), path);
        Assert.Equal("template", File.ReadAllText(path));
    }

    [Fact]
    public void TypedFieldNotesLandInTheDownload()
    {
        var p = Project();
        var path = new DocumentMaker(_config).CreateNotes(p, _config.Category("notes")!, Visit, "Found 1/2\" rebar at NE corner.");
        Assert.Equal(Download + "-FN.txt", Path.GetFileName(path));
        var text = File.ReadAllText(path);
        Assert.Contains("554-1521-799 Main St Topo", text);
        Assert.Contains("TOPO", text);
        Assert.Contains("rebar", text);
    }
}
