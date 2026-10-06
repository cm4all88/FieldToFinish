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

    private static readonly DateTime FieldDay = new(2026, 10, 6);

    private string CardFile(string relative, string content = "x", DateTime? modified = null)
    {
        var path = Path.Combine(new[] { _card }.Concat(relative.Split('\\')).ToArray());
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        if (modified != null) File.SetLastWriteTime(path, modified.Value);
        return path;
    }

    private ProjectFolder NewProject(string number = "2169171001", string name = "Silver Lake") =>
        new ProjectStore(_config).Create(new ProjectInfo { ProjectNumber = number, ProjectName = name, Client = "City", Created = FieldDay });

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
    public void ValidateCatchesCategoryFoldersLeavingTheProject()
    {
        _config.Categories[0].Folder = @"..\..\Elsewhere";
        var problems = new List<string>();
        _config.Validate(problems);
        Assert.Contains(problems, p => p.Contains("inside the project folder"));
    }

    [Theory]
    [InlineData("2169171001", true)]
    [InlineData(" 554-3744-009 ", true)]
    [InlineData("", false)]
    [InlineData("SILVER LAKE", false)]
    [InlineData("2169/171", false)]
    public void ProjectNumbers(string number, bool ok) => Assert.Equal(ok, _config.IsValidProjectNumber(number));

    // ---------------------------------------------------------------- projects

    [Fact]
    public void PmCreatesTheProjectSkeleton()
    {
        var p = NewProject();
        Assert.Equal(Path.Combine(_jobs, "2169171001 Silver Lake"), p.Path);
        Assert.True(File.Exists(Path.Combine(p.Path, ProjectInfo.FileName)));
        Assert.True(Directory.Exists(Path.Combine(p.Path, "Survey", "CAD")));
        Assert.True(Directory.Exists(Path.Combine(p.Path, "Survey", "Field", "Lineouts")));
        // Dated levels are made when something is uploaded for that day, not up front.
        Assert.True(Directory.Exists(Path.Combine(p.Path, "Survey", "Field", "Photos")));
        Assert.Empty(Directory.GetDirectories(Path.Combine(p.Path, "Survey", "Field", "Photos")));
    }

    [Fact]
    public void CrewFindsTheProjectByNumber()
    {
        NewProject();
        var found = new ProjectStore(_config).Find(" 2169171001");
        Assert.NotNull(found);
        Assert.Equal("Silver Lake", found!.Info.ProjectName);
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
        NewProject("2169171001");
        Assert.Null(new ProjectStore(_config).Find("216917100"));
    }

    [Fact]
    public void TwoFoldersForOneNumberIsAnErrorNotAGuess()
    {
        Directory.CreateDirectory(Path.Combine(_jobs, "2169171001 Silver Lake"));
        Directory.CreateDirectory(Path.Combine(_jobs, "2026", "2169171001 Silver Lake OLD"));
        Assert.Throws<InvalidOperationException>(() => new ProjectStore(_config).Find("2169171001"));
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
    [InlineData("Field Notes 10-06.pdf", "notes")]
    [InlineData("fieldnotes.pdf", "notes")]
    [InlineData("SVLK lineout.pdf", "lineout")]
    [InlineData("lineout sketch.jpg", "lineout")]
    [InlineData("Cut Sheet north.xlsx", "stakeout")]
    [InlineData("SILVERLAKE1006.job", "rawdata")]
    [InlineData("drawing.pdf", "other")]
    [InlineData("rawdrawing.pdf", "other")]
    public void ClassifiesByNameThenExtension(string file, string key) =>
        Assert.Equal(key, new UploadPlanner(_config).Classify(file).Key);

    [Fact]
    public void FolderNamesAreHintsNearestFirst()
    {
        var planner = new UploadPlanner(_config);
        Assert.Equal("notes", planner.Classify("scan001.jpg", new[] { "Day 3", "Field_Notes" }).Key);
        Assert.Equal("lineout", planner.Classify("scan001.pdf", new[] { "Field Notes", "Lineouts" }).Key);
        // A keyword in the file's own name beats its folder.
        Assert.Equal("lineout", planner.Classify("lineout.pdf", new[] { "Field Notes" }).Key);
    }

    [Fact]
    public void ADroppedFolderBringsEverythingButJunk()
    {
        CardFile(@"Day 3\Photos\IMG_1.jpg");
        CardFile(@"Day 3\Photos\Thumbs.db");
        CardFile(@"Day 3\Field Notes\page1.pdf");
        CardFile(@"Day 3\~$lineout.xlsx");
        CardFile(@"Day 3\SVLK.job");
        var items = new UploadPlanner(_config).Collect(new[] { Path.Combine(_card, "Day 3") });
        Assert.Equal(new[] { "SVLK.job", "page1.pdf", "IMG_1.jpg" }, items.Select(i => Path.GetFileName(i.SourcePath)));
        Assert.Equal(new[] { "rawdata", "notes", "photos" }, items.Select(i => i.Category!.Key));
        Assert.Equal(@"Day 3\Photos\IMG_1.jpg", items[2].DroppedAs);
    }

    [Fact]
    public void DroppingOnATypesBoxDecidesTheTypeForEverything()
    {
        CardFile(@"Day 3\IMG_1.jpg");
        CardFile(@"Day 3\Field Notes\page1.pdf");
        CardFile(@"Day 3\SVLK.job");
        var items = new UploadPlanner(_config).Collect(new[] { Path.Combine(_card, "Day 3") }, _config.Category("lineout"));
        Assert.Equal(3, items.Count);
        Assert.All(items, i => Assert.Equal("lineout", i.Category!.Key));

        var p = NewProject();
        new UploadPlanner(_config).Assign(p, items, "cmm", FieldDay);
        Assert.All(items, i => Assert.StartsWith(Path.Combine(p.Path, "Survey", "Field", "Lineouts"), i.Destination));
    }

    [Fact]
    public void BrandingColoursAreChecked()
    {
        _config.Branding.PrimaryColor = "charcoal";
        var problems = new List<string>();
        _config.Validate(problems);
        Assert.Contains(problems, x => x.Contains("primaryColor"));
    }

    // ------------------------------------------------------------------ naming

    [Fact]
    public void NamesFollowTheProjectAndNumberOn()
    {
        var p = NewProject();
        var photoDay = new DateTime(2026, 10, 2);
        CardFile("IMG_1.JPG", "a", photoDay);
        CardFile("IMG_2.JPG", "b", photoDay);
        CardFile("notes.pdf", "c");
        CardFile("SVLK.job", "d");

        var planner = new UploadPlanner(_config);
        var items = planner.Collect(Directory.GetFiles(_card).OrderBy(f => f));
        planner.Assign(p, items, "cmm", FieldDay);

        var names = items.ToDictionary(i => Path.GetFileName(i.SourcePath), i => Rel(p, i.Destination!));
        // Photos are filed by the day they were taken, extension lower-cased.
        Assert.Equal(@"Survey\Field\Photos\20261002\SV-2169171001-PHOTO-20261002-01.jpg", names["IMG_1.JPG"]);
        Assert.Equal(@"Survey\Field\Photos\20261002\SV-2169171001-PHOTO-20261002-02.jpg", names["IMG_2.JPG"]);
        Assert.Equal(@"Survey\Field\Field Notes\SV-2169171001-FN-20261006-01.pdf", names["notes.pdf"]);
        // Data collector files keep their job name.
        Assert.Equal(@"Survey\Field\Raw Data\20261006\SV-2169171001-RAW-20261006-SVLK.job", names["SVLK.job"]);
    }

    [Fact]
    public void NumberingContinuesFromWhatIsAlreadyInTheJob()
    {
        var p = NewProject();
        var notes = Path.Combine(p.Path, "Survey", "Field", "Field Notes");
        File.WriteAllText(Path.Combine(notes, "SV-2169171001-FN-20261006-01.pdf"), "earlier");
        CardFile("notes.pdf", "new");
        var planner = new UploadPlanner(_config);
        var items = planner.Collect(new[] { Path.Combine(_card, "notes.pdf") });
        planner.Assign(p, items, "cmm", FieldDay);
        Assert.Equal("SV-2169171001-FN-20261006-02.pdf", Path.GetFileName(items[0].Destination));
    }

    [Fact]
    public void ChangingTheTypeRenamesAndRefiles()
    {
        var p = NewProject();
        CardFile("scan.pdf");
        var planner = new UploadPlanner(_config);
        var items = planner.Collect(new[] { Path.Combine(_card, "scan.pdf") });
        planner.Assign(p, items, "cmm", FieldDay);
        Assert.Equal("other", items[0].Category!.Key);

        items[0].Category = _config.Category("lineout");
        planner.Assign(p, items, "cmm", FieldDay);
        Assert.Equal(@"Survey\Field\Lineouts\SV-2169171001-LINEOUT-20261006-01.pdf", Rel(p, items[0].Destination!));
    }

    [Theory]
    [InlineData("SV-<a>:b", "SV-a-b")]
    [InlineData("SV--FN-", "SV-FN")]
    [InlineData("name. ", "name")]
    [InlineData("a   b", "a b")]
    public void CleanMakesLegalWindowsNames(string raw, string clean) => Assert.Equal(clean, Naming.Clean(raw));

    [Fact]
    public void ABlankTokenDoesNotLeaveDoubleDashes()
    {
        var p = NewProject();
        _config.FileName = "SV-{projectNumber}-{crew}-{code}-{seq}";
        CardFile("notes.pdf");
        var planner = new UploadPlanner(_config);
        var items = planner.Collect(new[] { Path.Combine(_card, "notes.pdf") });
        planner.Assign(p, items, "", FieldDay);
        Assert.Equal("SV-2169171001-FN-01.pdf", Path.GetFileName(items[0].Destination));
    }

    // ---------------------------------------------------------------- uploading

    [Fact]
    public void UploadCopiesLogsAndLeavesTheOriginals()
    {
        var p = NewProject();
        var src = CardFile("IMG_1.jpg", "photo", FieldDay);
        var planner = new UploadPlanner(_config);
        var items = planner.Collect(new[] { src });
        planner.Assign(p, items, "cmm", FieldDay);

        Assert.Equal(1, new UploadRunner(_config).Run(p, items, "cmm"));
        Assert.True(items[0].Done);
        Assert.Equal("photo", File.ReadAllText(items[0].Destination!));
        Assert.True(File.Exists(src));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(items[0].Destination!)!, "*.partial"));

        var log = File.ReadAllLines(Path.Combine(p.Path, "Survey", "Field", "upload-log.csv"));
        Assert.Equal(UploadRunner.LogHeader, log[0]);
        Assert.Contains("CMM", log[1]);
        Assert.EndsWith("uploaded", log[1]);
    }

    [Fact]
    public void TheSameFileDroppedTwiceIsNotCopiedTwice()
    {
        var p = NewProject();
        var src = CardFile("IMG_1.jpg", "photo", FieldDay);
        var planner = new UploadPlanner(_config);
        var first = planner.Collect(new[] { src });
        planner.Assign(p, first, "cmm", FieldDay);
        new UploadRunner(_config).Run(p, first, "cmm");

        var again = planner.Collect(new[] { src });
        planner.Assign(p, again, "cmm", FieldDay);
        Assert.True(again[0].Skip);
        Assert.Equal(first[0].Destination, again[0].AlreadyUploadedAs);
        Assert.Equal(0, new UploadRunner(_config).Run(p, again, "cmm"));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(first[0].Destination!)!));
    }

    [Fact]
    public void ACollisionAtCopyTimeFailsThatFileOnly()
    {
        var p = NewProject();
        CardFile("a.pdf", "1");
        CardFile("b.pdf", "2");
        var planner = new UploadPlanner(_config);
        var items = planner.Collect(new[] { Path.Combine(_card, "a.pdf"), Path.Combine(_card, "b.pdf") });
        planner.Assign(p, items, "cmm", FieldDay);
        Directory.CreateDirectory(Path.GetDirectoryName(items[0].Destination!)!);
        File.WriteAllText(items[0].Destination!, "someone else");

        Assert.Equal(1, new UploadRunner(_config).Run(p, items, "cmm"));
        Assert.NotNull(items[0].Error);
        Assert.Equal("someone else", File.ReadAllText(items[0].Destination!));
        Assert.True(items[1].Done);
    }

    // ---------------------------------------------------------------- documents

    [Fact]
    public void NewLineoutFromTemplateIsNamedAndFiled()
    {
        var p = NewProject();
        Directory.CreateDirectory(Path.Combine(_root, "templates"));
        File.WriteAllText(Path.Combine(_root, "templates", "Field Lineout.xlsx"), "template");
        var doc = _config.Documents.First(d => d.Category == "lineout");

        var maker = new DocumentMaker(_config);
        var first = maker.Create(p, doc, "cmm", FieldDay);
        var second = maker.Create(p, doc, "cmm", FieldDay);
        Assert.Equal(@"Survey\Field\Lineouts\SV-2169171001-LINEOUT-20261006-01.xlsx", Rel(p, first));
        Assert.Equal("SV-2169171001-LINEOUT-20261006-02.xlsx", Path.GetFileName(second));
        Assert.Equal("template", File.ReadAllText(first));
    }

    [Fact]
    public void WithoutATemplateATextSheetIsWritten()
    {
        var p = NewProject();
        var doc = _config.Documents.First(d => d.Category == "lineout");
        var path = new DocumentMaker(_config).Create(p, doc, "cmm", FieldDay, "Set 4 lath on the north line.");
        Assert.EndsWith(".txt", path);
        var text = File.ReadAllText(path);
        Assert.Contains("2169171001 - Silver Lake", text);
        Assert.Contains("CMM", text);
        Assert.Contains("Set 4 lath", text);
    }

    [Fact]
    public void TypedFieldNotesLandWithTheOtherNotes()
    {
        var p = NewProject();
        var path = new DocumentMaker(_config).CreateNotes(p, _config.Category("notes")!, "cmm", FieldDay, "Found 1/2\" rebar at NE corner.");
        Assert.Equal(@"Survey\Field\Field Notes\SV-2169171001-FN-20261006-01.txt", Rel(p, path));
    }
}
