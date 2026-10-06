using System.Text;
using CrewUpload;
using CrewUpload.Reports;

namespace CrewUpload.Tests;

public sealed class DailyReportTests : TestShare
{
    public DailyReportTests()
    {
        Config.DailyReport.AdminFolder = Path.Combine(Root, "Admin", "DailyReports");
        Config.DailyReport.LocalFolder = Path.Combine(Root, "Local");
    }

    private DailyReportFiler Filer() => new(Config) { Records = new ReportRecords(Config.DailyReport.AdminFolder, Path.Combine(Root, "PC")) };

    /// <summary>Jeff's report from the sample form (5-7-26).</summary>
    internal static DailyReport Sample() => new()
    {
        Date = new DateTime(2026, 5, 7),
        ProjectNumber = "554-1800-119",
        ProjectName = "TDLE Ph3",
        TaskNumber = "3.5",
        Crew =
        {
            new ReportCrewMember { Initials = "JBB", Name = "Jeff Bearson" },
            new ReportCrewMember { Initials = "CP", Name = "Colin Priest" },
            new ReportCrewMember { Initials = "RM", Name = "Ryan Meldrum" },
        },
        Hours = 10,
        DataFileName = "20260507-JBB-1800-119-STK",
        WorkType = "STK",
        Equipment = { "PSO Survey Equipment" },
        Vehicle = "AUT 103",
        Mileage = { Start = 87874, Finish = 87910 },
        Notes = "layout curb lines (#2) for the bus test at Tacoma Dome parking lot.",
        SafetyObservations = { "Equipment/Machinery", "Traffic", "Uneven Ground" },
        SafetyPrecautions = { "Appropriate Clothing for Cold/Heat", "Aware of Surroundings/Be Alert", "Flashers/Beacons", "Hydration", "Safety Vest", "Tall Leather Boots", "Use Cones/Flags/Signs", "Work in Pairs" },
    };

    private ProjectFolder Project()
    {
        var survey = SurveyDir();
        return new ProjectFolder
        {
            Path = survey,
            UploadRoot = Path.Combine(survey, "02Field", "01FLD_DR_FN_DCfile", "Unprocessed"),
            Info = new ProjectInfo { ProjectNumber = "1800-119", FullNumber = "554-1800-119", ProjectName = "TDLE Phase 3" },
        };
    }

    [Fact]
    public void FormListsComeFromTheShippedForm()
    {
        var f = new DailyReportSettings();
        Assert.Equal(15, f.Vehicles.Count);
        Assert.Equal("246676 / 2702", f.Vehicles.Single(v => v.Id == "AUT 103").Assets);
        Assert.Equal(6, f.Equipment.Count);
        Assert.Equal(14, f.SafetyObservations.Count);
        Assert.Equal(20, f.SafetyPrecautions.Count);

        // a config that lists its own vehicles replaces the defaults instead of adding to them
        var cfg = Newtonsoft.Json.JsonConvert.DeserializeObject<DailyReportSettings>("{\"vehicles\":[{\"id\":\"AUT 1\"}]}")!;
        Assert.Equal("AUT 1", Assert.Single(cfg.Vehicles).Id);
        Assert.Equal(6, cfg.Equipment.Count);
    }

    [Fact]
    public void ChecksWhatTheFormNeeds()
    {
        Assert.Empty(Sample().Problems());
        var r = Sample();
        r.Hours = null;
        r.Mileage.Finish = 80000;
        r.Equipment.Clear();
        var p = r.Problems();
        Assert.Contains(p, x => x.Contains("hours"));
        Assert.Contains(p, x => x.Contains("finish mileage"));
        Assert.Contains(p, x => x.Contains("equipment"));
        Assert.Equal(36, Sample().Mileage.Total);
    }

    [Fact]
    public void RendersAReadablePdf()
    {
        var bytes = Filer().Render(Sample());
        var text = Encoding.Latin1.GetString(bytes);
        Assert.Contains("/Count 1 ", text); // the usual day fits on one page, like the paper form
        Assert.StartsWith("%PDF-1.4", text);
        Assert.EndsWith("%%EOF\n", text);
        Assert.Contains("(TDLE Ph3) Tj", text);
        Assert.Contains("(20260507-JBB-1800-119-STK) Tj", text);
        Assert.Contains("(Colin Priest / Ryan Meldrum) Tj", text);
        Assert.Contains("(layout curb lines \\(#2\\) for the bus test at Tacoma Dome parking lot.) Tj", text);
        Assert.Contains("DAILY FIELD SURVEYOR\u0092S REPORT", text); // the curly apostrophe in WinAnsi
        // the xref offsets point at the objects
        var xref = int.Parse(text.Substring(text.LastIndexOf("startxref\n") + 10).Split('\n')[0]);
        Assert.StartsWith("xref", text.Substring(xref));
        var first = text.Substring(xref).Split('\n')[3];
        Assert.StartsWith("1 0 obj", text.Substring(int.Parse(first.Substring(0, 10))));
    }

    [Fact]
    public void LongCommentsCarryOnToAnotherPage()
    {
        var r = Sample();
        r.Notes = string.Join("\n", Enumerable.Range(1, 80).Select(i => "Line " + i + " of a very long day of notes about the work."));
        var text = Encoding.Latin1.GetString(Filer().Render(r));
        Assert.DoesNotContain("/Count 1 ", text);
        Assert.Contains("(Line 80 of a very long day of notes about the work.) Tj", text);
    }

    [Fact]
    public void WrapsAndMeasures()
    {
        Assert.Equal(5.56f, PdfDocument.Measure("0", 10), 2);
        var lines = PdfDocument.Wrap("one two three four five six seven eight nine ten", 60, 10);
        Assert.True(lines.Count > 1);
        Assert.All(lines, l => Assert.True(PdfDocument.Measure(l, 10) <= 60));
    }

    [Fact]
    public void FilesThreeCopiesAndNeverOverwrites()
    {
        var project = Project();
        var filer = Filer();
        var result = filer.Submit(Sample(), project);
        Assert.True(result.Copies.All(c => c.Ok), string.Join("; ", result.Copies.Select(c => c.Error)));
        var r = result.Report;
        Assert.Matches("^DR-20260507-JBB-[0-9a-f]{8}$", r.ReportId);
        Assert.NotNull(r.SubmittedTime);
        Assert.False(string.IsNullOrEmpty(r.SubmittedBy));

        var projectPdf = Path.Combine(project.UploadRoot, "20260507-JBB-1800-119-STK", "20260507-JBB-1800-119-STK-DR.pdf");
        Assert.Equal(projectPdf, r.PdfPath);
        Assert.True(File.Exists(projectPdf));
        Assert.Equal(Path.Combine(Root, "Admin", "DailyReports", "2026", "2026-05", "20260507-JBB-1800-119-STK-DR.pdf"), r.AdminPdfPath);
        Assert.True(File.Exists(Path.Combine(Root, "Local", "20260507-JBB-1800-119-STK-DR.pdf")));

        var second = filer.Submit(Sample(), project);
        Assert.EndsWith("-DR-2.pdf", second.Report.PdfPath);
        Assert.NotEqual(r.ReportId, second.Report.ReportId);
        Assert.True(File.Exists(projectPdf));
    }

    [Fact]
    public void AnUnregisteredProjectStillFilesTheOtherCopies()
    {
        var r = Sample();
        r.DataFileName = null;
        var result = Filer().Submit(r, null);
        Assert.False(result.Copy(FiledCopy.Project)!.Ok);
        Assert.True(result.Copy(FiledCopy.Admin)!.Ok);
        Assert.Equal(result.Report.AdminPdfPath, result.Report.PdfPath);
        Assert.EndsWith("20260507-JBB-1800-119-DR.pdf", result.Report.PdfPath);
    }

    [Fact]
    public void AnUnreachableAdminFolderDoesNotLoseTheReport()
    {
        File.WriteAllText(Path.Combine(Root, "Admin"), "a file where the folder should be");
        var result = Filer().Submit(Sample(), Project());
        Assert.False(result.Copy(FiledCopy.Admin)!.Ok);
        Assert.True(result.Copy(FiledCopy.Project)!.Ok);
        Assert.True(result.Copy(FiledCopy.Local)!.Ok);
        Assert.Null(result.Report.AdminPdfPath);
    }

    [Fact]
    public void RefusesAReportWithProblems()
    {
        var r = Sample();
        r.Crew.Clear();
        Assert.Throws<InvalidOperationException>(() => Filer().Submit(r, Project()));
        Assert.False(Directory.Exists(Path.Combine(Root, "Admin")));
    }
}
