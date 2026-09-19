using FieldCodes.RecordSurvey;
using FieldCodes.Settings;
using Newtonsoft.Json;

namespace FieldCodes.Tests;

/// <summary>
/// Mapping survey entities onto the drawing's own resources: found is used, mapped is used and
/// said, missing is reported and withheld -- never substituted. And the settings section itself.
/// </summary>
public sealed class RecordSurveyStandardsTests
{
    private static DrawingInventory PmxLikeDrawing()
    {
        return new DrawingInventory
        {
            Layers = { "0", "V-PROP-BNDY-E", "V-PROP-BNDY-TEXT-E", "V-PROP-LOTL-E", "V-PROP-RWAY-E", "V-ALGN-CNTR-E", "V-ALGN-TEXT", "V-ESMT-E", "V-ESMT-TEXT-E", "V-CTRL-MONU-E", "V-PROP-TABL-E" },
            TextStyles = { "Standard", "Survey" },
            Linetypes = { "ByLayer", "Continuous", "HIDDEN2" },
            Blocks = { "MON-FOUND", "MON-SET" },
            LineLabelStyles = { "PMX Bearing over Distance" },
            CurveLabelStyles = { "PMX Curve Data" },
            Scale = 50.0,
            CurrentTextStyle = "Standard"
        };
    }

    private static RecordSurveySettings PmxLikeSettings()
    {
        var s = new RecordSurveySettings { TextStyle = "Survey" };
        foreach (var e in s.Entities) { e.LineLabelStyle = "PMX Bearing over Distance"; e.CurveLabelStyle = "PMX Curve Data"; }
        s.FindMonument("Found")!.Block = "MON-FOUND";
        s.FindMonument("Set")!.Block = "MON-SET";
        return s;
    }

    [Fact]
    public void EveryResourceTheDrawingHasIsUsedAsIs()
    {
        var r = StandardsResolver.Resolve(PmxLikeSettings(), PmxLikeDrawing(), null, new[] { "Boundary", "Lot Line", "Centerline" });
        var boundary = r.For("Boundary")!;
        Assert.True(boundary.CanDraw);
        Assert.Equal("V-PROP-BNDY-E", boundary.Layer);
        Assert.Equal("V-PROP-BNDY-TEXT-E", boundary.LabelLayer);        // derived from the layer family, as line labels do
        Assert.Equal("Survey", boundary.TextStyle);
        Assert.Equal("PMX Bearing over Distance", boundary.LineLabelStyle);
        Assert.True(boundary.CanLabelLinesCivil);
        Assert.True(boundary.CanLabelCurvesCivil);
        Assert.True(boundary.CanLabelText);
        Assert.Equal(0.08 * 50.0, boundary.TextHeight, 9);
        Assert.Empty(boundary.Issues.Where(i => i.Severity == "Missing"));
        Assert.Equal("V-ALGN-TEXT", r.For("Centerline")!.LabelLayer);
        Assert.True(r.Monuments["Found"].CanDraw);
        Assert.Equal("MON-FOUND", r.Monuments["Found"].Block);
        Assert.True(r.CanDrawTable);
    }

    [Fact]
    public void AMissingLayerIsReportedWithCandidatesAndTheCoursesAreWithheld()
    {
        var drawing = PmxLikeDrawing();
        drawing.Layers.Remove("V-PROP-LOTL-E");
        drawing.Layers.Add("V-PROP-LOTL");                                  // a near-duplicate exists
        var r = StandardsResolver.Resolve(PmxLikeSettings(), drawing, null, new[] { "Lot Line" });
        var lot = r.For("Lot Line")!;
        Assert.False(lot.CanDraw);
        Assert.Null(lot.Layer);
        var issue = Assert.Single(lot.Issues.Where(i => i.Resource == "layer"));
        Assert.Equal("Missing", issue.Severity);
        Assert.Equal("V-PROP-LOTL-E", issue.Name);
        Assert.Contains("V-PROP-LOTL", issue.Candidates);
        Assert.False(r.AllMissingResolved);
        Assert.Contains("not drawn", issue.Effect);
    }

    [Fact]
    public void AProjectLayerMappingWinsAndIsSaid()
    {
        var drawing = PmxLikeDrawing();
        drawing.Layers.Remove("V-PROP-BNDY-E");
        drawing.Layers.Add("AP-BOUNDARY");
        var mappings = new[] { new LayerMapping { From = "V-PROP-BNDY-E", To = "AP-BOUNDARY" } };
        var r = StandardsResolver.Resolve(PmxLikeSettings(), drawing, mappings, new[] { "Boundary" });
        var b = r.For("Boundary")!;
        Assert.True(b.CanDraw);
        Assert.Equal("AP-BOUNDARY", b.Layer);
        Assert.Equal("Mapped", b.LayerDecision);
        Assert.Contains(b.Issues, i => i.Severity == "Fallback" && i.Effect.Contains("mapping"));
    }

    [Fact]
    public void CreateMissingLayersIsAnExplicitChoiceAndStillReported()
    {
        var drawing = PmxLikeDrawing();
        drawing.Layers.Remove("V-PROP-LOTL-E");
        var s = PmxLikeSettings();
        s.CreateMissingLayers = true;
        var r = StandardsResolver.Resolve(s, drawing, null, new[] { "Lot Line" });
        Assert.True(r.For("Lot Line")!.CanDraw);
        Assert.Equal("Create", r.For("Lot Line")!.LayerDecision);
        Assert.Contains(r.Issues, i => i.Effect.Contains("CREATED"));
    }

    [Fact]
    public void AMissingCivil3DLabelStyleWithholdsTheLabelsByDefault()
    {
        var drawing = PmxLikeDrawing();
        drawing.LineLabelStyles.Clear();
        var r = StandardsResolver.Resolve(PmxLikeSettings(), drawing, null, new[] { "Boundary" });
        var b = r.For("Boundary")!;
        Assert.True(b.CanDraw);                                              // geometry is not held hostage
        Assert.False(b.CanLabelLinesCivil);
        Assert.False(b.CanLabelText);                                        // no silent fallback to plain text either
        var issue = Assert.Single(b.Issues.Where(i => i.Resource == "line label style"));
        Assert.Equal("Missing", issue.Severity);
        Assert.Contains("withheld", issue.Effect);
    }

    [Fact]
    public void PlainTextFallbackForAMissingLabelStyleIsOptInAndStillReported()
    {
        var drawing = PmxLikeDrawing();
        drawing.LineLabelStyles.Clear();
        var s = PmxLikeSettings();
        s.WhenLabelStyleMissing = RecordSurveySettings.MissingStylePlainText;
        var r = StandardsResolver.Resolve(s, drawing, null, new[] { "Boundary" });
        var b = r.For("Boundary")!;
        Assert.False(b.CanLabelLinesCivil);
        Assert.True(b.CanLabelText);
        Assert.Contains(b.Issues, i => i.Resource == "line label style" && i.Severity == "Fallback");
    }

    [Fact]
    public void AMissingTextStyleWithholdsPlainTextLabels()
    {
        var drawing = PmxLikeDrawing();
        drawing.TextStyles.Remove("Survey");
        var s = PmxLikeSettings();
        s.UseCivil3DLabels = false;
        var r = StandardsResolver.Resolve(s, drawing, null, new[] { "Boundary" });
        Assert.False(r.For("Boundary")!.CanLabelText);
        Assert.Contains(r.Issues, i => i.Resource == "text style" && i.Name == "Survey" && i.Severity == "Missing");
    }

    [Fact]
    public void NoConfiguredTextStyleUsesTheCurrentOneVisibly()
    {
        var s = PmxLikeSettings();
        s.TextStyle = string.Empty;
        s.UseCivil3DLabels = false;
        var r = StandardsResolver.Resolve(s, PmxLikeDrawing(), null, new[] { "Boundary" });
        Assert.Equal("Standard", r.For("Boundary")!.TextStyle);
        Assert.Contains(r.Issues, i => i.Resource == "text style" && i.Severity == "Fallback");
    }

    [Fact]
    public void AMissingMonumentBlockKeepsTheMonumentOutOfTheDrawing()
    {
        var drawing = PmxLikeDrawing();
        drawing.Blocks.Remove("MON-SET");
        var r = StandardsResolver.Resolve(PmxLikeSettings(), drawing, null, null);
        Assert.False(r.Monuments["Set"].CanDraw);
        Assert.True(r.Monuments["Found"].CanDraw);
        Assert.Contains(r.Issues, i => i.Resource == "block" && i.Name == "MON-SET" && i.Severity == "Missing");
        // Shipped defaults name no block at all: reported, never guessed.
        var shipped = StandardsResolver.Resolve(new RecordSurveySettings(), drawing, null, null);
        Assert.All(shipped.Monuments.Values, m => Assert.False(m.CanDraw));
        Assert.Contains(shipped.Issues, i => i.Resource == "block" && i.Name == "(none configured)");
    }

    [Fact]
    public void AMissingLinetypeFallsBackToByLayerAndSaysSo()
    {
        var s = PmxLikeSettings();
        s.FindEntity("Easement")!.Linetype = "DASHED2";
        var r = StandardsResolver.Resolve(s, PmxLikeDrawing(), null, new[] { "Easement" });
        Assert.True(r.For("Easement")!.CanDraw);
        Assert.Null(r.For("Easement")!.Linetype);
        Assert.Contains(r.Issues, i => i.Resource == "linetype" && i.Severity == "Fallback");
    }

    [Fact]
    public void OnlyTheEntitiesTheDocumentNeedsAreChecked()
    {
        var drawing = new DrawingInventory { Layers = { "V-PROP-BNDY-E" }, TextStyles = { "Standard" } };
        var s = new RecordSurveySettings { UseCivil3DLabels = false };
        var r = StandardsResolver.Resolve(s, drawing, null, new[] { "Boundary" });
        Assert.True(r.For("Boundary")!.CanDraw);
        Assert.DoesNotContain(r.Issues, i => i.Entity == "Lot Line");
    }

    // ---------------------------------------------------------------- settings

    [Fact]
    public void ShippedDefaultsAreValidAndNameNoGuessedStyles()
    {
        var s = new RecordSurveySettings();
        var problems = new List<string>();
        s.Validate(problems);
        Assert.Empty(problems);
        Assert.All(s.Entities, e => Assert.Equal(string.Empty, e.LineLabelStyle));
        Assert.All(s.Entities, e => Assert.Equal(string.Empty, e.TextStyle));
        Assert.All(s.Monuments, m => Assert.Equal(string.Empty, m.Block));
        Assert.Equal(string.Empty, s.TextStyle);
        Assert.False(s.CreateMissingLayers);
        Assert.Equal(0.85, s.ReviewThreshold);
        Assert.Equal(new double[] { 0, 90, 270, 180 }, s.RotationList());
    }

    [Theory]
    [InlineData("reviewThreshold", 1.5)]
    [InlineData("closureToleranceFt", 0)]
    [InlineData("bearingSecondsDecimals", 7)]
    public void BadNumbersAreReported(string field, double value)
    {
        var s = new RecordSurveySettings();
        if (field == "reviewThreshold") s.ReviewThreshold = value;
        if (field == "closureToleranceFt") s.ClosureToleranceFt = value;
        if (field == "bearingSecondsDecimals") s.BearingSecondsDecimals = (int)value;
        var problems = new List<string>();
        s.Validate(problems);
        Assert.NotEmpty(problems);
    }

    [Fact]
    public void BadWordsAreReported()
    {
        var s = new RecordSurveySettings { BuildFrom = "Guess", LabelMode = "Sometimes", WhenLabelStyleMissing = "Substitute", OcrEngine = "Tesseract" };
        var problems = new List<string>();
        s.Validate(problems);
        Assert.Equal(4, problems.Count);
    }

    [Fact]
    public void ADuplicateEntityNameIsReported()
    {
        var s = new RecordSurveySettings();
        s.Entities.Add(new RecordEntityStandard { Name = "boundary" });
        var problems = new List<string>();
        s.Validate(problems);
        Assert.Contains(problems, p => p.Contains("more than once"));
    }

    [Fact]
    public void TheSectionRoundTripsThroughTheSettingsFileWithoutDoublingLists()
    {
        var s = new FtfSettings();
        s.RecordSurvey.FindEntity("Boundary")!.LineLabelStyle = "PMX Bearing over Distance";
        var json = JsonConvert.SerializeObject(s);
        var back = JsonConvert.DeserializeObject<FtfSettings>(json)!;
        back.FillMissingSections();
        Assert.Equal(9, back.RecordSurvey.Entities.Count);
        Assert.Equal("PMX Bearing over Distance", back.RecordSurvey.FindEntity("Boundary")!.LineLabelStyle);
        Assert.Equal(4, back.RecordSurvey.Monuments.Count);
    }

    [Fact]
    public void ASettingsFileFromBeforeTheSectionExistedGetsTheDefaults()
    {
        var back = JsonConvert.DeserializeObject<FtfSettings>("{\"version\":\"1\",\"general\":{\"unitsPerFoot\":1}}")!;
        back.FillMissingSections();
        Assert.NotNull(back.RecordSurvey);
        Assert.Equal(9, back.RecordSurvey.Entities.Count);
        Assert.Equal("Recorded Surveys", back.RecordSurvey.Title);
        Assert.Contains("FTFRECORDCHECK", back.RecordSurvey.AffectedCommands);
    }

    [Fact]
    public void ThePmxProfilesCarryTheSectionWithOnlyEvidencedResources()
    {
        var repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        foreach (var name in new[] { "PMX SURVEY EXHIBIT.json", "PMX SURVEY EXHIBIT TABLES.json" })
        {
            var path = Path.Combine(repo, "config", "profiles", name);
            if (!File.Exists(path)) continue;
            var s = FtfSettings.Load(path);
            Assert.Equal("Survey", s.RecordSurvey.TextStyle);                       // measured from the office template
            Assert.All(s.RecordSurvey.Entities, e => Assert.Equal(string.Empty, e.LineLabelStyle));   // no guessed PMX label style names
            var problems = new List<string>();
            s.Validate(problems);
            Assert.Empty(problems);
        }
    }
}
