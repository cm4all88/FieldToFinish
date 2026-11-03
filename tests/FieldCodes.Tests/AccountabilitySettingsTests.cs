using FieldCodes.Settings;
using Newtonsoft.Json;

namespace FieldCodes.Tests;

/// <summary>Settings > Point Accountability: defaults, the per-feature continuity rows, validation,
/// and a settings file from before the section existed.</summary>
public sealed class AccountabilitySettingsTests
{
    [Fact]
    public void TheDefaultsAreTheAgreedTestingValues()
    {
        var a = new AccountabilitySettings();
        Assert.True(a.RunAfterProcessing);
        Assert.True(a.RequireIgnoreReasons);
        Assert.Equal(0.01, a.DuplicateToleranceFeet);
        var d = a.Continuity.Defaults;
        Assert.Equal(3.0, d.SpacingFactor);
        Assert.Equal(50.0, d.MaxGapFeet);
        Assert.Equal(25, d.SequenceWindow);
        Assert.Equal(30.0, d.MaxDeflectionDegrees);
        Assert.Contains(a.Continuity.Features, f => f.Name == "Fences" && f.Codes.Contains("FNC"));
        Assert.Contains(a.Continuity.Features, f => f.Name == "Curbs and flowlines" && f.Codes.Contains("CG"));
        Assert.Contains(a.Continuity.Features, f => f.Name == "Walls");
        Assert.Contains(a.Continuity.Features, f => f.Name == "Pavement and surface edges" && f.Codes.Contains("EP"));
    }

    [Fact]
    public void EachFeatureFindsItsRowAndAnyOtherTheDefaults()
    {
        var c = new ContinuitySettings();
        Assert.Equal("Fences", c.For("FNC2").Name);
        Assert.Equal("Buildings", c.For("bld").Name);
        Assert.Equal(95.0, c.For("BLD1").MaxDeflectionDegrees);
        Assert.Same(c.Defaults, c.For("TOE"));
        Assert.False(c.For("LN").Enabled);                         // striping is left alone by default
    }

    [Fact]
    public void ValidationCatchesBadLimitsAndACodeInTwoRows()
    {
        var a = new AccountabilitySettings();
        a.Continuity.Features[0].MaxGapFeet = 0;
        a.Continuity.Features[1].Codes.Add("FNC");
        a.DuplicateToleranceFeet = -1;
        var problems = new List<string>();
        a.Validate(problems);
        Assert.Contains(problems, p => p.Contains("Fences") && p.Contains("longest gap"));
        Assert.Contains(problems, p => p.Contains("FNC is in two rows"));
        Assert.Contains(problems, p => p.Contains("duplicate tolerance"));
    }

    [Fact]
    public void AnOlderSettingsFileGetsTheSection()
    {
        var path = Path.Combine(Path.GetTempPath(), "ftf-settings-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, "{ \"version\": \"1\" }");
        try
        {
            var s = FtfSettings.Load(path);
            Assert.NotNull(s.Accountability);
            Assert.NotNull(s.Accountability.Continuity);
            Assert.Contains(s.Sections, x => x.Title == "Point Accountability");
            Assert.True(s.Review.IsRequired("line.gap"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void TheSectionRoundTrips()
    {
        var s = new FtfSettings();
        s.Accountability.SymbolBlocks["CB"] = new List<string> { "PMX-CB*" };
        s.Accountability.Continuity.For("FNC").MaxGapFeet = 12;
        var back = JsonConvert.DeserializeObject<FtfSettings>(JsonConvert.SerializeObject(s))!;
        back.FillMissingSections();
        Assert.Equal(new[] { "PMX-CB*" }, back.Accountability.SymbolBlocksFor("CB2"));
        Assert.Equal(12, back.Accountability.Continuity.For("FNC").MaxGapFeet);
    }
}
