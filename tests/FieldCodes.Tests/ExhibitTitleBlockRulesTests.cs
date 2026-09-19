using FieldCodes.Exhibits;
using FieldCodes.Settings;
using Newtonsoft.Json;

namespace FieldCodes.Tests;

/// <summary>
/// Title block attributes: FTF fills only what the drafter entered for the exhibit, and never writes approval,
/// certification, licence, seal or signature fields -- whatever a profile says.
/// </summary>
public sealed class ExhibitTitleBlockRulesTests
{
    [Theory]
    [InlineData("ApprovedBy")]
    [InlineData("APPROVED")]
    [InlineData("AcceptedBy")]
    [InlineData("SEAL")]
    [InlineData("PLS")]
    [InlineData("PLS_NO")]
    [InlineData("LicenseNo")]
    [InlineData("LICENCE")]
    [InlineData("CertifiedBy")]
    [InlineData("SIGNATURE")]
    [InlineData("SurveyorStamp")]
    [InlineData("STAMP")]
    public void ApprovalAndSealFieldsAreTheSurveyors(string tag)
    {
        Assert.True(ExhibitSettings.IsProfessionalTag(tag));
        Assert.NotNull(ExhibitSettings.AttributeRefusal(tag, "{checkedBy}"));
        Assert.NotNull(ExhibitSettings.AttributeRefusal(tag, "JRD"));
    }

    [Theory]
    [InlineData("JobNo")]
    [InlineData("SubmitDate")]
    [InlineData("SheetNo")]
    [InlineData("DesignedBy")]       // "SIGN" inside DESIGNED is not a signature
    [InlineData("DrawnBy")]
    [InlineData("CheckedBy")]
    [InlineData("TIMESTAMP")]        // a plot/date stamp is not a surveyor's stamp
    [InlineData("PLOTSTAMP")]
    [InlineData("TITLE")]
    public void OrdinaryFieldsAreNotProfessional(string tag)
    {
        Assert.False(ExhibitSettings.IsProfessionalTag(tag));
    }

    [Fact]
    public void PersonFieldsComeOnlyFromWhatTheDrafterTyped()
    {
        Assert.True(ExhibitSettings.IsPersonTag("CheckedBy"));
        Assert.True(ExhibitSettings.IsPersonTag("DRAWN_BY"));
        Assert.False(ExhibitSettings.IsPersonTag("JobNo"));

        Assert.Null(ExhibitSettings.AttributeRefusal("CheckedBy", "{checkedBy}"));
        Assert.Null(ExhibitSettings.AttributeRefusal("DrawnBy", "{preparedBy}"));
        // A fixed name in the profile would put the same person on every exhibit.
        Assert.NotNull(ExhibitSettings.AttributeRefusal("CheckedBy", "JRD"));
        Assert.NotNull(ExhibitSettings.AttributeRefusal("DrawnBy", "CMM"));
        // Ordinary fields may carry a fixed value.
        Assert.Null(ExhibitSettings.AttributeRefusal("JobNo", "2169171001"));
    }

    [Fact]
    public void AProfileMappingAnApprovalFieldCannotBeSaved()
    {
        var xs = new ExhibitSettings { TitleBlockAttributes = "JobNo={projectNumber}; ApprovedBy={checkedBy}; CheckedBy=JRD; DrawnBy={preparedBy}" };
        var problems = new List<string>();
        xs.Validate(problems);
        Assert.Contains(problems, p => p.Contains("ApprovedBy") && p.Contains("surveyor"));
        Assert.Contains(problems, p => p.Contains("CheckedBy") && p.Contains("fixed name"));
        Assert.DoesNotContain(problems, p => p.Contains("DrawnBy") || p.Contains("JobNo"));
    }

    [Fact]
    public void TheShippedProfilesPassTheRules()
    {
        foreach (var path in Directory.GetFiles(Path.Combine(RepoRoot(), "config", "profiles"), "*.json"))
        {
            var settings = JsonConvert.DeserializeObject<FtfSettings>(File.ReadAllText(path))!;
            var problems = new List<string>();
            settings.Exhibits.Validate(problems);
            Assert.DoesNotContain(problems, p => p.Contains("title block attribute"));
        }
    }

    [Fact]
    public void RebuildMemoryIsSavedAndOldExhibitsReadWithout()
    {
        var item = new ExhibitItem { Key = "BORDER", Kind = "TITLEBLOCK", Attributes = new Dictionary<string, string> { { "JobNo", "2169171001" } }, HandPosition = true };
        var back = JsonConvert.DeserializeObject<ExhibitItem>(JsonConvert.SerializeObject(item))!;
        Assert.Equal("2169171001", back.Attributes!["JobNo"]);
        Assert.True(back.HandPosition);

        var old = JsonConvert.DeserializeObject<ExhibitItem>("{\"key\":\"LABEL:x:1\",\"kind\":\"LABEL\",\"text\":\"N 10.00'\"}")!;
        Assert.Null(old.Attributes);
        Assert.False(old.HandPosition);
        var json = JsonConvert.SerializeObject(old);
        Assert.DoesNotContain("attributes", json);
        Assert.DoesNotContain("handPosition", json);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "FieldToFinish.sln"))) dir = dir.Parent;
        return dir!.FullName;
    }
}
