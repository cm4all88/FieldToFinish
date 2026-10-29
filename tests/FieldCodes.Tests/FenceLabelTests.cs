using FieldCodes;
using FieldCodes.Linework;

namespace FieldCodes.Tests;

/// <summary>
/// Fences: every fence code labels, and a layer two fence codes share is resolved by the
/// shots the line was drawn through, or by asking -- never by guessing.
/// </summary>
public sealed class FenceLabelTests : IClassFixture<RulesFixture>
{
    private readonly LineworkCatalog _catalog;

    public FenceLabelTests(RulesFixture fx) => _catalog = new LineworkCatalog(fx.Config.LineFeatures, fx.Config.IgnoreLineNames);

    [Theory]
    [InlineData("FNC1", "FENCE")]
    [InlineData("FHW2", "HOG WIRE FENCE")]
    [InlineData("FBD", "WOOD FENCE")]
    [InlineData("FRL3", "RAIL FENCE")]
    [InlineData("FBW1", "BARBED WIRE FENCE")]
    [InlineData("FCK2", "CHAIN LINK FENCE")]
    public void EveryFenceFigureLabels(string figureName, string label)
    {
        Assert.Equal(label, LineworkCatalog.UnanimousLabel(_catalog.Candidates(figureName, null)));
    }

    [Theory]
    [InlineData("V-SURF-FENC-BARB-E", "BARBED WIRE FENCE")]
    [InlineData("V-SURF-FENC-CHNL-E", "CHAIN LINK FENCE")]
    public void AFenceOnItsOwnLayerLabelsFromTheLayerAlone(string layer, string label)
    {
        Assert.Equal(label, LineworkCatalog.UnanimousLabel(_catalog.Candidates(null, layer)));
    }

    [Theory]
    [InlineData("V-SURF-FENC-E", "FNC", "FHW")]
    [InlineData("V-SURF-FENC-WOOD-E", "FBD", "FRL")]
    public void ASharedFenceLayerIsAChoiceToAskNotAGuess(string layer, string a, string b)
    {
        var candidates = _catalog.Candidates(null, layer);
        Assert.Null(LineworkCatalog.UnanimousLabel(candidates));
        var choices = LineworkCatalog.DisagreeingChoices(candidates);
        Assert.Equal(new[] { a, b }, choices.Select(c => c.Code).ToArray());
    }

    [Fact]
    public void TheShotsAlongALineSettleASharedLayer()
    {
        // A plain polyline on V-SURF-FENC-E drawn through shots coded FHW is hog wire.
        var notes = PointNotes.Consensus(new[] { "FHW B", "FHW", "FHW E" }, _catalog);
        Assert.Equal("HOG WIRE FENCE", LineworkCatalog.UnanimousLabel(notes.Features));
    }

    [Fact]
    public void NothingIsAskedWhenThereIsNothingToChoose()
    {
        // One candidate, a unanimous label (CU and CX are both CURB), or a missing label.
        Assert.Empty(LineworkCatalog.DisagreeingChoices(_catalog.Candidates("FNC1", null)));
        Assert.Empty(LineworkCatalog.DisagreeingChoices(_catalog.Candidates(null, "V-SURF-CURB-E")));
        Assert.Empty(LineworkCatalog.DisagreeingChoices(new List<LineFeatureRule>
        {
            new LineFeatureRule { Code = "A", Layer = "L", Label = "A LINE", Enabled = true },
            new LineFeatureRule { Code = "B", Layer = "L", Enabled = true }
        }));
    }

    // ----------------------------------------------------------------- the bulk pass report

    [Fact]
    public void TheReportSaysWhatWasNotLabelledAndWhyGroupedByFeature()
    {
        var report = new LineLabelPassReport { LabelsPlaced = 9, FeaturesLabelled = 7, GateLabels = 2, TooShort = 3, AlreadyByHand = 5 };
        for (var i = 0; i < 4; i++) report.NotLabelled(_catalog.Candidates("GRL1", null), "V-SURF-GRAL-E");
        report.NotLabelled(_catalog.Candidates(null, "V-SURF-FENC-E"), "V-SURF-FENC-E");
        report.NotLabelled(_catalog.Candidates(null, "V-SURF-FENC-E"), "V-SURF-FENC-E");

        Assert.Equal(6, report.Unlabelled);
        var lines = report.Lines(10.0);
        Assert.Equal("9 label(s) on 7 line(s), 2 gate/span label(s).", lines[0]);
        Assert.Contains(lines, l => l.StartsWith("3 line(s) shorter than 10 ft"));
        Assert.Contains("5 line(s) already labelled by hand (FTFLABELLINE) were left as they are.", lines);
        Assert.Contains("Not labelled: GRL Guardrail: no label configured (4 line(s)).", lines);
        Assert.Contains(lines, l => l.StartsWith("Not labelled: V-SURF-FENC-E: could be FNC (FENCE) or FHW (HOG WIRE FENCE)") && l.EndsWith("(2 line(s))."));
    }

    [Fact]
    public void ACleanPassReportsOnlyWhatItPlaced()
    {
        var report = new LineLabelPassReport { LabelsPlaced = 3, FeaturesLabelled = 3 };
        Assert.Equal(new[] { "3 label(s) on 3 line(s)." }, report.Lines(10.0));
        Assert.Equal(0, report.Unlabelled);
    }

    // ----------------------------------------------------------------- lettered linetypes

    [Theory]
    [InlineData("WATER", new[] { "W", "W" }, "WATER (W)")]
    [InlineData("UTIL", new[] { "G", "W" }, "UTIL (G/W)")]
    [InlineData("FENCELINE2", new[] { "symbol" }, "FENCELINE2 (symbol)")]
    [InlineData("FENCE", new[] { "" }, "FENCE (symbol)")]
    public void ALinetypeIsDescribedByWhatItDraws(string name, string[] marks, string described)
    {
        Assert.True(LinetypeMarks.IsMarked(marks));
        Assert.Equal(described, LinetypeMarks.Describe(name, marks));
    }

    [Fact]
    public void APlainDashPatternHasNoMarks()
    {
        Assert.False(LinetypeMarks.IsMarked(new string[0]));
        Assert.False(LinetypeMarks.IsMarked(null));
    }

    [Fact]
    public void LinesLeftToTheLegendAreReportedByLinetypeAndAreNotCountedAsFailures()
    {
        var report = new LineLabelPassReport { LabelsPlaced = 2, FeaturesLabelled = 2 };
        for (var i = 0; i < 3; i++) report.ToLegend("WATER", new[] { "W" });
        report.ToLegend("POWER", new[] { "P" });

        Assert.Equal(4, report.LeftToLegend);
        Assert.Equal(0, report.Unlabelled);
        Assert.Contains("Left to the legend: 4 line(s) whose linetype draws its own letter or symbol -- WATER (W) 3, POWER (P) 1.", report.Lines(10.0));
    }

    [Fact]
    public void LeavingLetteredLinetypesToTheLegendIsOnByDefault()
    {
        Assert.True(new FieldCodes.Settings.LineLabelSettings().LeaveMarkedLinetypesToLegend);
        var old = Newtonsoft.Json.JsonConvert.DeserializeObject<FieldCodes.Settings.LineLabelSettings>(@"{ ""drawMask"": false }")!;
        Assert.True(old.LeaveMarkedLinetypesToLegend);
    }
}
