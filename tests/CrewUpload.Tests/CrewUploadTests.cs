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
    private static readonly string[] Downloads = { "99Svcs", "Survey", "02Field", "01FLD_DR_FN_DCfile" };

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

    /// <summary>An existing project folder on the "share", as the office makes them.</summary>
    private string ProjectDir(string client = "1521-CityOfOrting", string project = "554-1521-799 Main St Topo")
    {
        var path = Path.Combine(_clients, client, project);
        Directory.CreateDirectory(path);
        return path;
    }

    private ProjectFolder Project()
    {
        ProjectDir();
        return new ProjectStore(_config).Resolve("1521-799").Single();
    }

    private string InDownloads(ProjectFolder p, params string[] rest) => Path.Combine(new[] { p.Path }.Concat(Downloads).Concat(rest).ToArray());

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
        Assert.Equal(@"\\parametrix.com\pmx\PSO\Projects\Clients", JobFolderConfig.CreateDefault().JobsRoot);

    [Fact]
    public void ValidateCatchesFoldersLeavingTheProject()
    {
        _config.DownloadsFolder = @"..\Elsewhere";
        _config.Categories[0].Folder = @"..\..\Elsewhere";
        var problems = new List<string>();
        _config.Validate(problems);
        Assert.Contains(problems, p => p.Contains("downloadsFolder"));
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
    public void ProjectNumbersAreClientTask(string typed, string number) =>
        Assert.Equal(number, JobFolderConfig.NormalizeProjectNumber(typed));

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

    // ---------------------------------------------------------------- projects

    [Fact]
    public void FindsTheProjectUnderItsClient()
    {
        ProjectDir("1800-SoundTransit", "554-1800-119 TDLE Phase 3");
        var found = new ProjectStore(_config).Resolve("1800-119");
        var p = Assert.Single(found);
        Assert.Equal(Path.Combine(_clients, "1800-SoundTransit", "554-1800-119 TDLE Phase 3"), p.Path);
        Assert.Equal("1800-119", p.Info.ProjectNumber);
        Assert.Equal("TDLE Phase 3", p.Info.ProjectName);
        Assert.Equal("1800-SoundTransit", p.Info.Client);
    }

    [Fact]
    public void AProjectFolderWithoutAPrefixIsFoundToo()
    {
        ProjectDir("1711-CityOfOrting", "1711-042 Harman Way");
        Assert.Single(new ProjectStore(_config).Resolve("1711-042"));
    }

    [Theory]
    [InlineData("1800-SoundTransit", "554-1800-1190 Other task")]   // task 1190, not 119
    [InlineData("18000-Someone", "554-1800-119 Wrong client folder")] // client 18000, not 1800
    [InlineData("1711-CityOfOrting", "554-1711-119 Same task, other client")]
    public void LookalikeFoldersAreNotTheProject(string client, string project)
    {
        ProjectDir(client, project);
        Assert.Empty(new ProjectStore(_config).Resolve("1800-119"));
    }

    [Fact]
    public void TwoMatchingFoldersAreReturnedForTheUserToChoose()
    {
        ProjectDir("1800-SoundTransit", "554-1800-119 TDLE Phase 3");
        ProjectDir("1800-SoundTransit", "1800-119 TDLE Phase 3 OLD");
        Assert.Equal(2, new ProjectStore(_config).Resolve("1800-119").Count);
    }

    [Fact]
    public void FindingNothingCreatesNothing()
    {
        Assert.Empty(new ProjectStore(_config).Resolve("1800-119"));
        Assert.Empty(Directory.GetFileSystemEntries(_clients));
    }

    [Fact]
    public void AFolderTheUserPickedIsOpenedButNeverCreated()
    {
        var picked = ProjectDir("1800-SoundTransit", "Archive 554-1800-119");
        var p = new ProjectStore(_config).Open(picked, "1800-119");
        Assert.Equal(picked, p.Path);
        Assert.False(ProjectStore.LooksLike(p.FolderName, "1800-119"));
        Assert.Throws<DirectoryNotFoundException>(() => new ProjectStore(_config).Open(Path.Combine(_clients, "nope"), "1800-119"));
        Assert.False(Directory.Exists(Path.Combine(_clients, "nope")));
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
        var dl = @"99Svcs\Survey\02Field\01FLD_DR_FN_DCfile\" + Download + @"\";
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
        var folders = items.Select(i => Rel(p, i.Destination!).Split('\\')[4]).Distinct().ToList();
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
    public void UploadCopiesVerifiesAndLeavesTheOriginals()
    {
        var p = Project();
        Assert.False(Directory.Exists(InDownloads(p))); // made on the first upload, inside the existing project
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
