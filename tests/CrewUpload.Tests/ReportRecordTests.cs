using CrewUpload;
using CrewUpload.Reports;
using Newtonsoft.Json.Linq;

namespace CrewUpload.Tests;

public sealed class ReportRecordTests : TestShare
{
    private string Admin => Path.Combine(Root, "Admin", "DailyReports");
    private string Pc => Path.Combine(Root, "PC");

    public ReportRecordTests()
    {
        Config.DailyReport.AdminFolder = Admin;
        Config.DailyReport.LocalFolder = Path.Combine(Root, "Local");
    }

    private DailyReportFiler Filer() => new(Config) { Records = new ReportRecords(Admin, Pc) };

    private ProjectFolder Project()
    {
        var survey = SurveyDir();
        return new ProjectFolder
        {
            Path = survey,
            UploadRoot = Path.Combine(survey, "02Field", "01FLD_DR_FN_DCfile", "Unprocessed"),
            Info = new ProjectInfo { ProjectNumber = "1800-119", FullNumber = "554-1800-119", ProjectName = "554-1800-119 TDLE Phase 3" },
        };
    }

    [Fact]
    public void EverySubmitWritesOneRecordWithTheRequiredFields()
    {
        var r = DailyReportTests.Sample();
        r.ScheduleProjectId = "2l8vhmyoodt";
        r.Source = "schedule";
        r.WorkOrder = "101199329";
        var result = Filer().Submit(r, Project());
        var rec = result.Copy(FiledCopy.Record)!;
        Assert.True(rec.Ok, rec.Error);
        Assert.Equal(Path.Combine(Admin, "Records", "2026", "2026-05", result.Report.ReportId + ".json"), rec.Path);

        var j = JObject.Parse(File.ReadAllText(rec.Path!));
        foreach (var field in new[] { "ReportID", "Date", "ProjectNumber", "ProjectName", "TaskNumber", "ScheduleProjectID", "Crew", "WorkType",
                     "Hours", "Vehicle", "Equipment", "Mileage", "WorkOrder", "Notes", "PDFPath", "SubmittedBy", "SubmittedTime" })
            Assert.True(j.ContainsKey(field), field + " missing");
        Assert.Equal(result.Report.ReportId, (string?)j["ReportID"]);
        Assert.Contains("\"Date\": \"2026-05-07\"", File.ReadAllText(rec.Path!));
        Assert.Equal("2l8vhmyoodt", (string?)j["ScheduleProjectID"]);
        Assert.Equal(36, (int)j["Mileage"]!["Total"]!);
        Assert.Equal("JBB", (string?)j["Crew"]![0]!["Initials"]);
        Assert.Equal(result.Report.PdfPath, (string?)j["PDFPath"]);
        Assert.True(File.Exists((string)j["PDFPath"]!));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(rec.Path!)!, "*.tmp"));
    }

    [Fact]
    public void ReadsRecordsByDateNotByFileName()
    {
        var filer = Filer();
        filer.Submit(DailyReportTests.Sample(), Project());
        var other = DailyReportTests.Sample();
        other.Date = new DateTime(2026, 6, 2);
        other.DataFileName = "a-name-that-says-nothing";
        filer.Submit(other, Project());

        var problems = new List<string>();
        var records = new ReportRecords(Admin, Pc);
        Assert.Single(records.Read(new DateTime(2026, 5, 7), new DateTime(2026, 5, 7), problems));
        var both = records.Read(new DateTime(2026, 5, 1), new DateTime(2026, 6, 30), problems);
        Assert.Equal(2, both.Count);
        Assert.Contains(both, x => x.Date == new DateTime(2026, 6, 2) && x.ProjectNumber == "554-1800-119");
        Assert.Empty(problems);
    }

    [Fact]
    public void ABadRecordIsReportedNotFatal()
    {
        Filer().Submit(DailyReportTests.Sample(), Project());
        File.WriteAllText(Path.Combine(Admin, "Records", "2026", "2026-05", "junk.json"), "{ not json");
        var problems = new List<string>();
        Assert.Single(new ReportRecords(Admin, Pc).Read(new DateTime(2026, 5, 1), new DateTime(2026, 5, 31), problems));
        Assert.Contains(problems, p => p.StartsWith("junk.json"));
    }

    [Fact]
    public void AnUnreachableAdminFolderKeepsTheRecordUntilItCanBeSent()
    {
        Directory.CreateDirectory(Path.Combine(Root, "Admin"));
        File.WriteAllText(Admin, "a file where the folder should be");
        var result = Filer().Submit(DailyReportTests.Sample(), Project());
        Assert.True(result.Copy(FiledCopy.Project)!.Ok);
        var rec = result.Copy(FiledCopy.Record)!;
        Assert.False(rec.Ok);
        Assert.Contains("will be sent next time", rec.Error);

        var records = new ReportRecords(Admin, Pc);
        Assert.Equal(1, records.Pending);
        var history = records.History();
        Assert.False(Assert.Single(history).Value); // not at admin yet

        File.Delete(Admin);
        Assert.Equal(1, records.SendPending(out var problem));
        Assert.Null(problem);
        Assert.Equal(0, records.Pending);
        Assert.True(records.History().Single().Value);
        Assert.Single(records.Read(new DateTime(2026, 5, 1), new DateTime(2026, 5, 31), new List<string>()));
    }

    [Fact]
    public void HistoryIsNewestFirst()
    {
        var filer = Filer();
        var first = filer.Submit(DailyReportTests.Sample(), Project()).Report.ReportId;
        Thread.Sleep(20);
        var second = filer.Submit(DailyReportTests.Sample(), Project()).Report.ReportId;
        var h = new ReportRecords(Admin, Pc).History();
        Assert.Equal(new[] { second, first }, h.Select(x => x.Key.ReportId));
    }

    [Fact]
    public void RecordsAreNeverReplaced()
    {
        var path = Path.Combine(Root, "x", "r.json");
        ReportRecords.WriteOnce(path, "{}");
        Assert.Throws<IOException>(() => ReportRecords.WriteOnce(path, "{\"changed\":1}"));
        Assert.Equal("{}", File.ReadAllText(path));
    }
}
