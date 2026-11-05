using FieldCodes.Editing;
using Newtonsoft.Json.Linq;

namespace FieldCodes.Tests;

/// <summary>
/// Settings > Wetland Buffers: FTFWETLANDBUFFER's section of the rules file, read with the built-in
/// values for anything it leaves out, written only where it changed (comments and the rest of the
/// file kept), checked field by field, and reaching the command's rule.
/// </summary>
public sealed class WetlandBufferSettingsEditingTests
{
    private static readonly string RulesPath = Path.Combine(AppContext.BaseDirectory, "rules.json");

    /// <summary>The shipped rules without their wetlandBuffer section (and its comment).</summary>
    private static RuleDocument WithoutSection()
    {
        var root = JObject.Parse(File.ReadAllText(RulesPath));
        root.Remove("wetlandBuffer");
        root.Remove("//wetlandBuffer");
        return RuleDocument.FromText(root.ToString());
    }

    private static JObject Section(RuleDocument doc) => (JObject)JObject.Parse(doc.Render())["wetlandBuffer"]!;

    [Fact]
    public void TheShippedRulesHoldTheBuiltInValues()
    {
        var w = RuleDocument.Load(RulesPath).GetWetlandBuffer();
        Assert.True(w.InFile);
        Assert.Equal("V-WATR-WTLD-BUFR-E", w.Layer);
        Assert.Equal(50, w.Distance);
        Assert.Equal(0, w.Smoothing);
        Assert.True(w.TrimAtHardSurfaces);
        Assert.Equal(WetlandBufferRule.DefaultHardSurfaceLayers(), w.HardSurfaceLayers);
    }

    [Fact]
    public void AFileWithoutTheSectionShowsTheBuiltInValuesAndTheFirstSaveWritesThemIn()
    {
        var doc = WithoutSection();
        var w = doc.GetWetlandBuffer();
        Assert.False(w.InFile);
        Assert.Equal(EditableWetlandBuffer.Defaults().HardSurfaceLayers, w.HardSurfaceLayers);
        Assert.DoesNotContain("wetlandBuffer", doc.Render());

        doc.ApplyWetlandBuffer(w);
        var s = Section(doc);
        Assert.Equal("V-WATR-WTLD-BUFR-E", (string)s["layer"]!);
        Assert.Equal(50, (double)s["distance"]!);
        Assert.Equal(11, ((JArray)s["hardSurfaceLayers"]!).Count);
        Assert.Empty(doc.Validate());
        Assert.True(doc.GetWetlandBuffer().InFile);
    }

    [Fact]
    public void AnEditChangesOnlyWhatChangedAndKeepsEverythingElse()
    {
        var root = JObject.Parse(File.ReadAllText(RulesPath));
        ((JObject)root["wetlandBuffer"]!)["note"] = "kept: a property the editor does not know";
        var doc = RuleDocument.FromText(root.ToString());
        var before = JObject.Parse(doc.Render());

        var w = doc.GetWetlandBuffer();
        w.Distance = 100;
        w.Smoothing = 10;
        w.TrimAtHardSurfaces = false;
        w.HardSurfaceLayers = new List<string> { " V-SURF-ASPH* ", "", "v-surf-asph*", "V-SURF-BLDG-E" };
        doc.ApplyWetlandBuffer(w);

        var after = JObject.Parse(doc.Render());
        var s = (JObject)after["wetlandBuffer"]!;
        Assert.Equal(100, (double)s["distance"]!);
        Assert.Equal(10, (double)s["smoothing"]!);
        Assert.False((bool)s["trimAtHardSurfaces"]!);
        Assert.Equal(new[] { "V-SURF-ASPH*", "V-SURF-BLDG-E" }, ((JArray)s["hardSurfaceLayers"]!).Select(t => (string)t!));
        Assert.Equal("kept: a property the editor does not know", (string)s["note"]!);
        Assert.Equal("V-WATR-WTLD-BUFR-E", (string)s["layer"]!);
        Assert.Equal((string)before["//wetlandBuffer"]!, (string)after["//wetlandBuffer"]!);
        foreach (var p in before.Properties().Where(p => p.Name != "wetlandBuffer"))
            Assert.True(JToken.DeepEquals(p.Value, after[p.Name]), p.Name + " changed");
        Assert.Empty(doc.Validate());

        // And it is what the command reads.
        var rule = doc.CompileCandidate().WetlandBuffer;
        Assert.Equal(100, rule.Distance);
        Assert.Equal(10, rule.Smoothing);
        Assert.False(rule.TrimAtHardSurfaces);
        Assert.True(rule.IsHardSurface("V-SURF-ASPH-OVRL-E"));
        Assert.False(rule.IsHardSurface("V-SURF-CONC-E"));
    }

    [Fact]
    public void NothingChangedWritesNothing()
    {
        var doc = RuleDocument.Load(RulesPath);
        var text = doc.Render();
        doc.ApplyWetlandBuffer(doc.GetWetlandBuffer());
        Assert.Equal(text, doc.Render());
    }

    [Theory]
    [InlineData("distance", "0", "the distance must be greater than zero")]
    [InlineData("distance", "\"fifty\"", "the distance must be a number")]
    [InlineData("smoothing", "-2", "the smoothing radius cannot be negative")]
    [InlineData("layer", "\"V-WATR-WTLD-*\"", "one layer, not a pattern")]
    [InlineData("layer", "\"V-WATR:BUFR\"", "cannot be used")]
    [InlineData("layer", "\"\"", "cannot be used")]
    [InlineData("trimAtHardSurfaces", "\"yes\"", "must be true or false")]
    [InlineData("hardSurfaceLayers", "\"V-SURF-ASPH-E\"", "must be a list")]
    [InlineData("hardSurfaceLayers", "[\"V-SURF-ASPH-E\", \"\"]", "a hard-surface layer is blank")]
    [InlineData("hardSurfaceLayers", "[\"V-SURF<ASPH\"]", "hard-surface layer \"V-SURF<ASPH\" cannot be used")]
    [InlineData("hardSurfaceLayers", "[\"V-SURF-ASPH-E\", \"v-surf-asph-e\"]", "listed twice")]
    public void EachMistakeIsNamedNotSaved(string key, string json, string expected)
    {
        var root = JObject.Parse(File.ReadAllText(RulesPath));
        ((JObject)root["wetlandBuffer"]!)[key] = JToken.Parse(json);
        var problems = RuleDocument.FromText(root.ToString()).Validate();
        Assert.Contains(problems, p => p.StartsWith("Wetland Buffers: ") && p.Contains(expected));
    }

    [Fact]
    public void WildcardPatternsAreAccepted()
    {
        var doc = RuleDocument.Load(RulesPath);
        var w = doc.GetWetlandBuffer();
        w.HardSurfaceLayers = new List<string> { "V-SURF-ASPH*", "V-SURF-CURB-?-E", "*-BLDG-E" };
        doc.ApplyWetlandBuffer(w);
        Assert.Empty(doc.Validate());
    }

    [Fact]
    public void SavingPutsThisPagesSettingsOnTheFileAsItIsNow()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ftf-wlb-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "rules.json");
            File.Copy(RulesPath, path);

            // This page reads the file and stages an edit.
            var page = RuleDocument.Load(path);
            var w = page.GetWetlandBuffer();
            w.Distance = 75;
            w.Layer = "V-WATR-WTLD-BUFR-75";
            page.ApplyWetlandBuffer(w);

            // Meanwhile another page saves its own change.
            var other = RuleDocument.Load(path);
            var o = other.GetOrientationApply();
            o.RoadSearchFeet = 120;
            other.ApplyOrientationApply(o);
            other.Save(path);

            // Save: the file as it is now, with this page's settings put on it.
            var fresh = RuleDocument.Load(path);
            page.CopyWetlandBufferTo(fresh);
            Assert.Empty(fresh.Validate());
            fresh.Save(path);

            var cfg = RulesConfig.Load(path);
            Assert.Equal(75, cfg.WetlandBuffer.Distance);
            Assert.Equal("V-WATR-WTLD-BUFR-75", cfg.WetlandBuffer.Layer);
            Assert.Equal(120, cfg.Orientation.RoadSearchFeet);
            Assert.True(File.Exists(path + RuleDocument.OriginalBackupSuffix));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }
}
