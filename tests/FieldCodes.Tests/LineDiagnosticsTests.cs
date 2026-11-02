using FieldCodes;
using FieldCodes.Linework;
using FieldCodes.Review;
using FieldCodes.Settings;

namespace FieldCodes.Tests;

/// <summary>
/// The Lines page turns the inventory's identification into an actionable status
/// with a reason, and names every setting that decides a line's label. It describes
/// what FTF would do; it never claims the minimum length or repeat interval apply
/// to FTFLABELLINE, where the surveyor places each label by hand.
/// </summary>
public sealed class LineDiagnosticsTests : IClassFixture<RulesFixture>
{
    private readonly RulesFixture _fx;
    private readonly LineworkCatalog _catalog;

    public LineDiagnosticsTests(RulesFixture fx)
    {
        _fx = fx;
        _catalog = new LineworkCatalog(fx.Config.LineFeatures, fx.Config.IgnoreLineNames);
    }

    private (LineworkRow Row, IList<LineFeatureRule> Candidates) Line(
        string? figure, string layer, double? length, string? trimble = null)
    {
        var row = _catalog.Identify("Polyline", figure, layer, length, trimble);
        var candidates = row.Source == LineIdentitySource.None
            ? new List<LineFeatureRule>()
            : _catalog.Candidates(figure, trimble, layer);
        return (row, candidates);
    }

    private static LineStatus Classify((LineworkRow Row, IList<LineFeatureRule> Candidates) line,
                                       LineLabelSettings? ls = null)
        => LineDiagnostics.Classify(line.Row, line.Candidates, ls ?? new LineLabelSettings(), 1.0);

    [Fact]
    public void ALabelledFeatureOfUsefulLengthIsReady()
    {
        var status = Classify(Line("FNC2", "V-SURF-FENC-E", 152.3));

        Assert.Equal(LineStatusKind.Ready, status.Kind);
        Assert.Equal("Ready to label", status.Short);
        Assert.Contains("\"FENCE\"", status.Reason);
        Assert.Contains("FTFLABELLINE", status.Action);
        Assert.False(status.NeedsAttention);
    }

    [Fact]
    public void AFeatureWithoutLabelTextSaysSo()
    {
        // RWC is identified but its rule has no label text.
        var status = Classify(Line("RWC1", "V-SURF-WALL-CONC-E", 80));

        Assert.Equal(LineStatusKind.NoLabelText, status.Kind);
        Assert.Contains("RWC", status.Reason);
        Assert.True(status.NeedsAttention);
    }

    [Fact]
    public void ASharedLayerWithDifferentLabelsIsAmbiguous()
    {
        // FNC (FENCE) and FHW (HOG WIRE FENCE) share V-SURF-FENC-E.
        var status = Classify(Line(null, "V-SURF-FENC-E", 120));

        Assert.Equal(LineStatusKind.Ambiguous, status.Kind);
        Assert.Contains("FENCE", status.Reason);
        Assert.Contains("HOG WIRE FENCE", status.Reason);
        Assert.Contains("asks which", status.Action);
    }

    [Fact]
    public void ASharedLayerWithTheSameLabelIsReady()
    {
        // CU and CX both label CURB: the label is known without knowing which code.
        Assert.Equal(LineStatusKind.Ready, Classify(Line(null, "V-SURF-CURB-E", 120)).Kind);
    }

    [Fact]
    public void AShortLineIsBelowTheAutomaticMinimum_ButFtfLabelLineStillWorks()
    {
        var ls = new LineLabelSettings { MinLengthFeet = 10 };
        var status = Classify(Line("FNC2", "V-SURF-FENC-E", 6.2), ls);

        Assert.Equal(LineStatusKind.TooShort, status.Kind);
        Assert.Contains("6.2 ft", status.Reason);
        Assert.Contains("10 ft minimum", status.Reason);
        Assert.Contains("FTFLABELLINE still labels it", status.Action);
    }

    [Fact]
    public void UnitsPerFootAreHonoured()
    {
        // 4 m is 13.1 ft: above a 10 ft minimum in a metric drawing.
        var line = Line("FNC2", "V-SURF-FENC-E", 4.0);
        var status = LineDiagnostics.Classify(line.Row, line.Candidates, new LineLabelSettings(), 0.3048);
        Assert.Equal(LineStatusKind.Ready, status.Kind);
    }

    [Fact]
    public void AnUnconfiguredLineIsNotAFeature_AndSaysWhatWasChecked()
    {
        var status = Classify(Line(null, "C-TOPO-MAJR", 400, "Top of Slope"));

        Assert.Equal(LineStatusKind.NotConfigured, status.Kind);
        Assert.Contains("C-TOPO-MAJR", status.Reason);
        Assert.Contains("Top of Slope", status.Reason);
        Assert.False(status.NeedsAttention);
    }

    [Fact]
    public void ADisabledFeatureIsCalledOut()
    {
        var features = _fx.Config.LineFeatures.Select(f => new LineFeatureRule
        {
            Code = f.Code, Name = f.Name, Layer = f.Layer, Label = f.Label,
            Enabled = f.Code != "FNC"
        }).ToList();
        var catalog = new LineworkCatalog(features);
        var row = catalog.Identify("Survey Figure", "FNC1", "V-SURF-FENC-E", 50);

        var status = LineDiagnostics.Classify(row, catalog.Candidates("FNC1", null, "V-SURF-FENC-E"),
                                              new LineLabelSettings(), 1.0);
        Assert.Equal(LineStatusKind.Disabled, status.Kind);
    }

    [Fact]
    public void GoverningSettingsCoverEveryLever_WithCurrentValues()
    {
        var ls = new LineLabelSettings
        {
            MinLengthFeet = 12, RepeatIntervalFeet = 150, EndClearanceFeet = 4,
            SideOffsetFeet = 1.5, DrawMask = false, AlignToLine = true, DefaultPlacement = "Left"
        };
        var line = Line("FNC2", "V-SURF-FENC-E", 152.3);

        var settings = LineDiagnostics.GoverningSettings(line.Row, line.Candidates, ls,
            "V-SURF-FENC-TEXT-E (matches source layer)");
        var names = settings.Select(s => s.Name).ToList();

        foreach (var expected in new[]
                 {
                     "Label text", "Label layer", "Text style / height", "Minimum length",
                     "Repeat interval", "Alignment", "Placement / offset", "Masking",
                     "Duplicate prevention", "Automatic pass"
                 })
            Assert.Contains(expected, names);

        string Value(string name) => settings.Single(s => s.Name == name).Value;
        Assert.Contains("\"FENCE\"", Value("Label text"));
        Assert.Contains("V-SURF-FENC-TEXT-E", Value("Label layer"));
        Assert.Contains("12 ft", Value("Minimum length"));
        Assert.Contains("automatic pass only", Value("Minimum length"));
        Assert.Contains("150 ft", Value("Repeat interval"));
        Assert.Contains("1.5 ft", Value("Placement / offset"));
        Assert.Contains("Default Left", Value("Placement / offset"));
        Assert.Equal("Off", Value("Masking"));
        Assert.Equal(LineDiagnostics.BuiltIn, settings.Single(s => s.Name == "Duplicate prevention").Where);
        Assert.Equal("Rules: lineFeatures FNC", settings.Single(s => s.Name == "Label text").Where);
    }

    [Fact]
    public void TheSummaryCountsEachStatus()
    {
        var statuses = new[]
        {
            Classify(Line("FNC2", "V-SURF-FENC-E", 152.3)),
            Classify(Line("FNC3", "V-SURF-FENC-E", 90)),
            Classify(Line(null, "C-TOPO-MAJR", 400)),
            Classify(Line("RWC1", "V-SURF-WALL-CONC-E", 80))
        };

        var counts = LineDiagnostics.Count(statuses);
        Assert.Equal(2, counts[LineStatusKind.Ready]);
        Assert.Equal("2 ready to label, 1 with no label text, 1 not line features.",
            LineDiagnostics.Summary(counts));
    }
}
