using CrewUpload;
using Newtonsoft.Json;

namespace CrewUpload.Tests;

public sealed class CrewUploadTests : IDisposable
{
    private readonly string _root;
    private readonly string _jobs;
    private readonly string _card;
    private readonly JobFolderConfig _config;

    public CrewUploadTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "crew-" + Path.GetRandomFileName());
        _jobs = Path.Combine(_root, "Jobs");
        _card = Path.Combine(_root, "Card");
        Directory.CreateDirectory(_jobs);
        Directory.CreateDirectory(_card);
        _config = JobFolderConfig.CreateDefault();
        _config.JobsRoot = _jobs;
        _config.BaseDirectory = _root;
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private const string Download = "20260128-JAM-1521-799-TOPO";
    private static readonly FieldVisit Visit = new() { Crew = "jam", Date = new DateTime(2026, 1, 28), WorkType = "topo" };

    private string CardFile(string relative, string content = "x")
    {
        var path = Path.Combine(new[] { _card }.Concat(relative.Split('\\')).ToArray());
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>The download from the crew's screenshot.</summary>
    private string CrewDownload()
    {
        CardFile(Download + @"\Photos\IMG_0412.JPG", "p1");
        CardFile(Download + @"\Photos\IMG_0413.JPG", "p2");
        CardFile(Download + @"\" + Download + "-ASB.pdf", "asb");
        CardFile(Download + @"\" + Download + "-FN.pdf", "fn");
        CardFile(Download + @"\" + Download + ".job", "job");
        CardFile(Download + @"\" + Download + ".jxl", "jxl");
        return Path.Combine(_card, Download);
    }

    private ProjectFolder NewProject(string number = "1521-799", string name = "Main St") =>
        new ProjectStore(_config).Create(new ProjectInfo { ProjectNumber = number, ProjectName = name, Client = "City", Created = Visit.Date });

    private static string Rel(ProjectFolder p, string path) => Path.GetRelativePath(p.Path, path).Replace(Path.DirectorySeparatorChar, '\\');

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
    [InlineData("1521-799", true)]
    [InlineData("2169171001", true)]
    [InlineData(" 554-3744-009 ", true)]
    [InlineData("", false)]
    [InlineData("SILVER LAKE", false)]
    [InlineData("2169/171", false)]
    public void ProjectNumbers(string number, bool ok) => Assert.Equal(ok, _config.IsValidProjectNumber(number));

    // ---------------------------------------------------------- download names

    [Fact]
    public void DownloadNameIsDateCrewProjectWorkType() =>
        Assert.Equal(Download, DownloadNames.Name(_config, "1521-799", Visit));

    [Fact]
    public void ADownloadFolderNameIsReadBack()
    {
        var parsed = DownloadNames.Parse(_config, Download)!;
        Assert.Equal("1521-799", parsed.ProjectNumber);
        Assert.Equal("JAM", parsed.Visit.Crew);
        Assert.Equal(new DateTime(2026, 1, 28), parsed.Visit.Date);
        Assert.Equal("TOPO", parsed.Visit.WorkType);
    }

    [Theory]
    [InlineData("Photos")]
    [InlineData("20261399-JAM-1521-799-TOPO")] // no 99th day
    [InlineData("20260128-JAM-TOPO")]
    public void OtherFolderNamesAreNotDownloads(string name) => Assert.Null(DownloadNames.Parse(_config, name));

    // ---------------------------------------------------------------- projects

    [Fact]
    public void PmCreatesTheProjectSkeleton()
    {
        var p = NewProject();
        Assert.Equal(Path.Combine(_jobs, "1521-799 Main St"), p.Path);
        Assert.True(File.Exists(Path.Combine(p.Path, ProjectInfo.FileName)));
        Assert.True(Directory.Exists(Path.Combine(p.Path, "99Svcs", "Survey", "02Field", "01FLD_DR_FN_DCfile")));
    }

    [Fact]
    public void CrewFindsTheProjectByNumber()
    {
        NewProject();
        var found = new ProjectStore(_config).Find(" 1521-799");
        Assert.NotNull(found);
        Assert.Equal("Main St", found!.Info.ProjectName);
        Assert.True(found.HasInfoFile);
    }

    [Fact]
    public void FindsOlderProjectsFiledByYearWithoutProjectJson()
    {
        Directory.CreateDirectory(Path.Combine(_jobs, "2025", "554-3744-009_Kenmore TCE"));
        var found = new ProjectStore(_config).Find("554-3744-009");
        Assert.NotNull(found);
        Assert.Equal("Kenmore TCE", found!.Info.ProjectName);
        Assert.False(found.HasInfoFile);
    }

    [Fact]
    public void ANumberThatIsOnlyAPrefixIsNotAMatch()
    {
        NewProject("1521-799");
        Assert.Null(new ProjectStore(_config).Find("1521-79"));
    }

    [Fact]
    public void TwoFoldersForOneNumberIsAnErrorNotAGuess()
    {
        Directory.CreateDirectory(Path.Combine(_jobs, "1521-799 Main St"));
        Directory.CreateDirectory(Path.Combine(_jobs, "2026", "1521-799 Main St OLD"));
        Assert.Throws<InvalidOperationException>(() => new ProjectStore(_config).Find("1521-799"));
    }

    [Fact]
    public void CannotCreateAProjectTwice()
    {
        NewProject();
        Assert.Throws<InvalidOperationException>(() => NewProject());
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
    public void FindDownloadReadsTheDroppedFolder()
    {
        var parsed = new UploadPlanner(_config).FindDownload(new[] { CrewDownload() });
        Assert.NotNull(parsed);
        Assert.Equal("1521-799", parsed!.ProjectNumber);
    }

    // ------------------------------------------------------------------ naming

    [Fact]
    public void TheCrewDownloadLandsInTheJobUnderTheSameNames()
    {
        var p = NewProject();
        var planner = new UploadPlanner(_config);
        var items = planner.Collect(new[] { CrewDownload() });
        planner.Assign(p, items, Visit);

        var to = items.ToDictionary(i => Path.GetFileName(i.SourcePath), i => Rel(p, i.Destination!));
        var dl = @"99Svcs\Survey\02Field\01FLD_DR_FN_DCfile\" + Download + @"\";
        Assert.Equal(dl + Download + "-ASB.pdf", to[Download + "-ASB.pdf"]);
        Assert.Equal(dl + Download + "-FN.pdf", to[Download + "-FN.pdf"]);
        Assert.Equal(dl + Download + ".job", to[Download + ".job"]);
        Assert.Equal(dl + Download + ".jxl", to[Download + ".jxl"]);
        Assert.Equal(dl + @"Photos\1521-799-0412.jpg", to["IMG_0412.JPG"]);
        Assert.Equal(dl + @"Photos\1521-799-0413.jpg", to["IMG_0413.JPG"]);
    }

    [Fact]
    public void LooseFilesAreNamedForTheVisit()
    {
        var p = NewProject();
        CardFile("notes.pdf");
        CardFile("Job001.job");
        CardFile("asbuilt.pdf");
        var planner = new UploadPlanner(_config);
        var items = planner.Collect(Directory.GetFiles(_card).OrderBy(f => f));
        planner.Assign(p, items, new FieldVisit { Crew = "cmm", Date = new DateTime(2026, 10, 6), WorkType = "LINEOUT" });

        Assert.Equal(new[]
        {
            "20261006-CMM-1521-799-LINEOUT-ASB.pdf",
            "20261006-CMM-1521-799-LINEOUT.job",
            "20261006-CMM-1521-799-LINEOUT-FN.pdf",
        }, items.Select(i => Path.GetFileName(i.Destination)));
    }

    [Fact]
    public void ASecondFileOfTheSameKindIsNumbered()
    {
        var p = NewProject();
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
        var p = NewProject();
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

    // ---------------------------------------------------------------- uploading

    [Fact]
    public void UploadCopiesLogsAndLeavesTheOriginals()
    {
        var p = NewProject();
        var src = CrewDownload();
        var planner = new UploadPlanner(_config);
        var items = planner.Collect(new[] { src });
        planner.Assign(p, items, Visit);

        Assert.Equal(6, new UploadRunner(_config).Run(p, items, "JAM"));
        Assert.All(items, i => Assert.True(i.Done));
        Assert.Equal("fn", File.ReadAllText(Path.Combine(p.Path, "99Svcs", "Survey", "02Field", "01FLD_DR_FN_DCfile", Download, Download + "-FN.pdf")));
        Assert.True(Directory.Exists(src));
        Assert.Empty(Directory.GetFiles(p.Path, "*.partial", SearchOption.AllDirectories));

        var log = File.ReadAllLines(Path.Combine(p.Path, "99Svcs", "Survey", "02Field", "01FLD_DR_FN_DCfile", "upload-log.csv"));
        Assert.Equal(UploadRunner.LogHeader, log[0]);
        Assert.Equal(7, log.Length);
        Assert.EndsWith("uploaded", log[1]);
    }

    [Fact]
    public void TheSameDownloadDroppedTwiceIsNotCopiedTwice()
    {
        var p = NewProject();
        var src = CrewDownload();
        var planner = new UploadPlanner(_config);
        var first = planner.Collect(new[] { src });
        planner.Assign(p, first, Visit);
        new UploadRunner(_config).Run(p, first, "JAM");

        // The crew adds two more photos and drops the folder again.
        CardFile(Download + @"\Photos\IMG_0414.JPG", "p3");
        var again = planner.Collect(new[] { src });
        planner.Assign(p, again, Visit);
        Assert.Equal(6, again.Count(i => i.Skip));
        Assert.Equal(1, new UploadRunner(_config).Run(p, again, "JAM"));
        Assert.Equal(3, Directory.GetFiles(Path.Combine(p.Path, "99Svcs", "Survey", "02Field", "01FLD_DR_FN_DCfile", Download, "Photos")).Length);
    }

    [Fact]
    public void ACollisionAtCopyTimeFailsThatFileOnly()
    {
        var p = NewProject();
        CardFile("a.pdf", "1");
        CardFile("b.job", "2");
        var planner = new UploadPlanner(_config);
        var items = planner.Collect(new[] { Path.Combine(_card, "a.pdf"), Path.Combine(_card, "b.job") });
        planner.Assign(p, items, Visit);
        Directory.CreateDirectory(Path.GetDirectoryName(items[0].Destination!)!);
        File.WriteAllText(items[0].Destination!, "someone else");

        Assert.Equal(1, new UploadRunner(_config).Run(p, items, "JAM"));
        Assert.NotNull(items[0].Error);
        Assert.Equal("someone else", File.ReadAllText(items[0].Destination!));
        Assert.True(items[1].Done);
    }

    // ---------------------------------------------------------------- documents

    [Fact]
    public void NewAsBuiltNotesFromTemplateAreNamedForTheVisit()
    {
        var p = NewProject();
        Directory.CreateDirectory(Path.Combine(_root, "templates"));
        File.WriteAllText(Path.Combine(_root, "templates", "As-Built Notes.docx"), "template");
        var doc = _config.Documents.First(d => d.Category == "asbuilt");

        var path = new DocumentMaker(_config).Create(p, doc, Visit);
        Assert.Equal(@"99Svcs\Survey\02Field\01FLD_DR_FN_DCfile\" + Download + @"\" + Download + "-ASB.docx", Rel(p, path));
        Assert.Equal("template", File.ReadAllText(path));
    }

    [Fact]
    public void TypedFieldNotesLandInTheDownload()
    {
        var p = NewProject();
        var path = new DocumentMaker(_config).CreateNotes(p, _config.Category("notes")!, Visit, "Found 1/2\" rebar at NE corner.");
        Assert.Equal(Download + "-FN.txt", Path.GetFileName(path));
        var text = File.ReadAllText(path);
        Assert.Contains("1521-799 - Main St", text);
        Assert.Contains("TOPO", text);
        Assert.Contains("rebar", text);
    }
}
