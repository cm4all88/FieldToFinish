using FieldCodes;
using FieldCodes.Linework;

namespace FieldCodes.Tests;

/// <summary>
/// Directional label placement: LEFT/L and RIGHT/R in the source coding's modifier
/// position choose which side of the existing line the label offsets to. Left and
/// right are relative to the line's own direction, never the screen -- and flipping
/// text to keep it readable can never move a LEFT label to the right side.
/// </summary>
public sealed class LineSideTests : IClassFixture<RulesFixture>
{
    private readonly LineworkCatalog _catalog;

    public LineSideTests(RulesFixture fx)
        => _catalog = new LineworkCatalog(fx.Config.LineFeatures);

    // -------------------------------------------------------------- the grammar

    [Theory]
    [InlineData("ASPH LEFT", "ASPH", LineLabelSide.Left)]
    [InlineData("ASPH L", "ASPH", LineLabelSide.Left)]
    [InlineData("ASPH RIGHT", "ASPH", LineLabelSide.Right)]
    [InlineData("ASPH R", "ASPH", LineLabelSide.Right)]
    [InlineData("FCK2 LEFT", "FCK2", LineLabelSide.Left)]      // non-asphalt feature
    [InlineData("RWC1 R", "RWC1", LineLabelSide.Right)]
    public void DirectionalModifiersParseInTheModifierPosition(
        string coding, string code, LineLabelSide side)
    {
        string codePart;
        LineLabelSide? parsed;
        LineSideParser.Split(coding, out codePart, out parsed);

        Assert.Equal(code, codePart);
        Assert.Equal(side, parsed);
    }

    [Theory]
    [InlineData("ASPH")]          // no modifier at all
    [InlineData("L")]             // the code position is never read as a direction
    [InlineData("R")]
    [InlineData("EC RL")]         // RL is not a direction
    [InlineData("WALL LEFTY")]    // LEFTY is not LEFT
    public void NoSideIsEverGuessed(string coding)
    {
        string codePart;
        LineLabelSide? parsed;
        LineSideParser.Split(coding, out codePart, out parsed);

        Assert.Null(parsed);
    }

    [Fact]
    public void TheSideStrippedCodeStillIdentifiesTheFeature()
    {
        // "ASPH LEFT" identifies the asphalt feature; the modifier is placement, not
        // identity. Same for the digit-suffixed fence.
        Assert.Equal("Asphalt", _catalog.Identify("Polyline", "ASPH LEFT", null, 50.0).FeatureName);
        Assert.Equal("Chain Link Fence",
            _catalog.Identify("Polyline", "FCK2 R", null, 50.0).FeatureName);

        var row = _catalog.Identify("Polyline", "ASPH LEFT", null, 50.0);
        Assert.Equal(LineLabelSide.Left, row.SourceSide);
    }

    // ------------------------------------------------------------ the precedence

    [Fact]
    public void SourceCodingBeatsConfigBeatsDefault()
    {
        var candidates = new List<LineFeatureRule>
        {
            new LineFeatureRule { Code = "X", Label = "X", Placement = "Right" }
        };

        // Source side wins over the rule's configured placement.
        Assert.Equal(LineLabelSide.Left,
            LineSideParser.Resolve(LineLabelSide.Left, candidates, LineLabelSide.OnLine));

        // No source side: the rule's placement.
        Assert.Equal(LineLabelSide.Right,
            LineSideParser.Resolve(null, candidates, LineLabelSide.OnLine));

        // Neither: the configured default.
        Assert.Equal(LineLabelSide.OnLine,
            LineSideParser.Resolve(null, new List<LineFeatureRule>(), LineLabelSide.OnLine));
    }

    [Fact]
    public void AutoAlwaysFallsThrough_AndTheFinalFallbackIsOnLine()
    {
        var auto = new List<LineFeatureRule> { new LineFeatureRule { Code = "X" } };

        Assert.Equal(LineLabelSide.OnLine,
            LineSideParser.Resolve(null, auto, LineLabelSide.Auto));
        Assert.Equal(LineLabelSide.Left,
            LineSideParser.Resolve(null, auto, LineLabelSide.Left));
    }

    // ------------------------------------------------------------- the geometry

    private sealed class StraightPath : ILinePath
    {
        private readonly double _direction;
        public StraightPath(double length, double direction)
        {
            Length = length;
            _direction = direction;
        }
        public double Length { get; }
        public void At(double d, out double x, out double y, out double dir)
        {
            x = d * Math.Cos(_direction);
            y = d * Math.Sin(_direction);
            dir = _direction;
        }
    }

    private static LineLabelOptions Options(LineLabelSide side)
        => new LineLabelOptions
        {
            MinLength = 10,
            RepeatInterval = 200,
            EndClearance = 5,
            Side = side,
            SideOffset = 1.0
        };

    [Fact]
    public void LeftOfAnEastboundLineIsNorth()
    {
        var label = LineLabelPlanner.Plan(new StraightPath(100, 0),
            Options(LineLabelSide.Left)).Single();

        Assert.Equal(1.0, label.Y, 9);      // offset north of the line
        Assert.Equal(50.0, label.X, 9);     // still at the midpoint along it
    }

    [Fact]
    public void RightOfAnEastboundLineIsSouth()
    {
        var label = LineLabelPlanner.Plan(new StraightPath(100, 0),
            Options(LineLabelSide.Right)).Single();

        Assert.Equal(-1.0, label.Y, 9);
    }

    [Fact]
    public void SidesFollowTheLineDirection_NotTheScreen()
    {
        // Left of a WESTBOUND line is SOUTH: the side belongs to the line's own
        // direction, exactly like driving it.
        var label = LineLabelPlanner.Plan(new StraightPath(100, Math.PI),
            Options(LineLabelSide.Left)).Single();

        Assert.Equal(-1.0, label.Y, 9);
    }

    [Fact]
    public void ReadabilityFlippingNeverMovesTheLabelToTheOtherSide()
    {
        // The westbound line's text is flipped to read east -- but the LEFT offset
        // still uses the raw westbound direction. If the flip leaked into the offset,
        // Y would be +1 instead of -1.
        var label = LineLabelPlanner.Plan(new StraightPath(100, Math.PI),
            Options(LineLabelSide.Left)).Single();

        Assert.Equal(0.0, label.RotationRadians, 9);    // reads like an eastbound label
        Assert.Equal(-1.0, label.Y, 9);                 // but sits on the true left
    }

    [Fact]
    public void OnLinePlacementDoesNotOffsetAtAll()
    {
        var label = LineLabelPlanner.Plan(new StraightPath(100, Math.PI / 4),
            Options(LineLabelSide.OnLine)).Single();

        Assert.Equal(label.X, label.Y, 9);   // still on the 45-degree line
    }

    // -------------------------------------------------------------- the preview

    [Fact]
    public void ThePreviewShowsTheResolvedPlacement()
    {
        var left = LineLabelPlanner.Describe("ASPHALT", 100, 10, 200, 5,
                                             LineLabelSide.Left, 1.0);
        Assert.Contains("Label existing line \"ASPHALT\"", left);
        Assert.Contains("left side, 1 ft offset", left);

        var onLine = LineLabelPlanner.Describe("CURB", 100, 10, 200, 5);
        Assert.Contains("on line", onLine);
    }

    [Fact]
    public void AsphaltResolvesItsConfiguredStandard()
    {
        var candidates = _catalog.Candidates("ASPH LEFT", null);
        Assert.Equal("ASPHALT", LineworkCatalog.UnanimousLabel(candidates));
    }

    // --------------------------------------------------------- wording choices

    [Fact]
    public void EdgeOfPavementOffersItsAbbreviation()
    {
        // FTFLABELLINE's Wording option: the agreed label first, then the
        // configured alternates, so a click places whichever is on the cursor.
        var choices = LineworkCatalog.LabelChoices(_catalog.Candidates(null, "V-SURF-ASPH-E"));

        Assert.Equal(new[] { "EDGE OF PAVEMENT", "EOP" }, choices);
    }

    [Fact]
    public void AFeatureWithoutAlternatesOffersJustItsLabel()
    {
        var choices = LineworkCatalog.LabelChoices(
            _catalog.Candidates(null, "V-SURF-FENC-CHNL-E"));

        Assert.Equal(new[] { "CHAIN LINK FENCE" }, choices);
    }

    [Fact]
    public void BetweenTwoEdgesTheLabelNamesTheSurface()
    {
        // Between two asphalt edges the area is ASPHALT (alt ASPH), never
        // "EDGE OF PAVEMENT" -- and between concrete edges it is a walk.
        Assert.Equal(new[] { "ASPHALT", "ASPH" },
            LineworkCatalog.BetweenChoices(_catalog.Candidates(null, "V-SURF-ASPH-E")));
        Assert.Equal(new[] { "CONC WALK", "CW" },
            LineworkCatalog.BetweenChoices(_catalog.Candidates(null, "V-SURF-CONC-E")));
    }

    [Fact]
    public void FeaturesWithoutBetweenWordingsFallBackToTheEdgeLabel()
    {
        Assert.Equal(new[] { "CHAIN LINK FENCE" },
            LineworkCatalog.BetweenChoices(_catalog.Candidates(null, "V-SURF-FENC-CHNL-E")));
    }

    [Fact]
    public void AlternatesNeverRescueADisagreement()
    {
        // The shared stripe layer has no unanimous label; alternates must not
        // sneak a choice in through the back door.
        var choices = LineworkCatalog.LabelChoices(
            _catalog.Candidates(null, "V-CHAN-STRP-E"));

        Assert.Empty(choices);
    }
}
