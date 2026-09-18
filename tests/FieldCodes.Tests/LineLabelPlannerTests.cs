using FieldCodes;
using FieldCodes.Linework;

namespace FieldCodes.Tests;

/// <summary>
/// The line label placement maths: one centred label on short features, evenly
/// repeated labels on long ones, ends respected, and text that can never read
/// upside down. Pure geometry, no drawing anywhere near it.
/// </summary>
public sealed class LineLabelPlannerTests : IClassFixture<RulesFixture>
{
    private readonly RulesFixture _fx;

    public LineLabelPlannerTests(RulesFixture fx) => _fx = fx;

    /// <summary>A straight line from the origin at a fixed bearing.</summary>
    private sealed class StraightPath : ILinePath
    {
        private readonly double _direction;
        public StraightPath(double length, double directionRadians = 0)
        {
            Length = length;
            _direction = directionRadians;
        }

        public double Length { get; }

        public void At(double distance, out double x, out double y, out double direction)
        {
            x = distance * Math.Cos(_direction);
            y = distance * Math.Sin(_direction);
            direction = _direction;
        }
    }

    /// <summary>An L-shaped path: east for half its length, then north.</summary>
    private sealed class CornerPath : ILinePath
    {
        public CornerPath(double length) { Length = length; }
        public double Length { get; }

        public void At(double distance, out double x, out double y, out double direction)
        {
            var half = Length / 2.0;
            if (distance <= half) { x = distance; y = 0; direction = 0; }
            else { x = half; y = distance - half; direction = Math.PI / 2.0; }
        }
    }

    private static LineLabelOptions Options(double min = 10, double interval = 200,
                                            double clearance = 5, bool align = true)
        => new LineLabelOptions
        {
            MinLength = min,
            RepeatInterval = interval,
            EndClearance = clearance,
            AlignToLine = align
        };

    // ------------------------------------------------------------------ placement

    [Fact]
    public void AFeatureBelowTheMinimumGetsNoLabel()
    {
        Assert.Empty(LineLabelPlanner.Plan(new StraightPath(8), Options(min: 10)));
    }

    [Fact]
    public void AShortFeatureGetsOneLabelAtItsMidpoint()
    {
        // The 148.8 ft chain link fence from the Gig Harbor inventory.
        var labels = LineLabelPlanner.Plan(new StraightPath(148.8), Options());

        var label = Assert.Single(labels);
        Assert.Equal(74.4, label.Distance, 6);
        Assert.Equal(74.4, label.X, 6);
    }

    [Fact]
    public void ALongFeatureGetsEvenlySpacedLabels()
    {
        // 1000 ft at a 200 ft interval with 5 ft clearance: usable 990 -> 5 labels.
        var labels = LineLabelPlanner.Plan(new StraightPath(1000), Options());

        Assert.Equal(5, labels.Count);

        // Evenly spaced, symmetric about the middle, inside the clearance.
        Assert.Equal(104.0, labels[0].Distance, 6);       // 5 + 198*0.5
        Assert.Equal(500.0, labels[2].Distance, 6);       // dead centre
        Assert.Equal(896.0, labels[4].Distance, 6);
        Assert.All(labels, l => Assert.InRange(l.Distance, 5.0, 995.0));

        var spacing = labels[1].Distance - labels[0].Distance;
        for (var i = 2; i < labels.Count; i++)
            Assert.Equal(spacing, labels[i].Distance - labels[i - 1].Distance, 6);
    }

    [Fact]
    public void JustOverOneIntervalStillGetsOneLabel()
    {
        // 210 usable / 200 interval rounds to 1: a second label would crowd it.
        var labels = LineLabelPlanner.Plan(new StraightPath(220), Options());
        Assert.Single(labels);
    }

    [Fact]
    public void ClearanceLargerThanTheFeatureStillYieldsAMidpointLabel()
    {
        // 12 ft long, 10 ft clearance each end: nothing usable, but the feature is
        // past the minimum, so it earns its one midpoint label.
        var labels = LineLabelPlanner.Plan(new StraightPath(12), Options(min: 10, clearance: 10));

        var label = Assert.Single(labels);
        Assert.Equal(6.0, label.Distance, 6);
    }

    [Fact]
    public void LabelsFollowTheDirectionAtTheirOwnPositionOnACornerPath()
    {
        // 800 ft L-path: labels on the east leg read east, on the north leg north
        // (flipped to readable).
        var labels = LineLabelPlanner.Plan(new CornerPath(800), Options());

        Assert.True(labels.Count >= 2);
        var eastLeg = labels.Where(l => l.Distance < 400).ToList();
        var northLeg = labels.Where(l => l.Distance > 400).ToList();

        Assert.All(eastLeg, l => Assert.Equal(0.0, l.RotationRadians, 9));
        Assert.All(northLeg, l => Assert.Equal(Math.PI / 2.0, l.RotationRadians, 9));
    }

    // -------------------------------------------------------------- readability

    [Theory]
    [InlineData(0.0, 0.0)]                        // east reads east
    [InlineData(Math.PI / 4, Math.PI / 4)]        // NE stays NE
    [InlineData(Math.PI, 0.0)]                    // west flips to east
    [InlineData(-Math.PI / 4, -Math.PI / 4)]      // SE stays SE
    [InlineData(Math.PI * 0.75, -Math.PI / 4)]    // NW flips to SE
    [InlineData(Math.PI * 1.25, Math.PI / 4)]     // SW flips to NE
    [InlineData(Math.PI / 2, Math.PI / 2)]        // straight north stays (edge case)
    public void TextNeverReadsUpsideDown(double lineDirection, double expected)
    {
        Assert.Equal(expected, LineLabelPlanner.NormalizeReadable(lineDirection), 9);
    }

    [Fact]
    public void AWestboundLineLabelsTheSameAsAnEastboundOne()
    {
        var east = LineLabelPlanner.Plan(new StraightPath(100, 0), Options());
        var west = LineLabelPlanner.Plan(new StraightPath(100, Math.PI), Options());

        Assert.Equal(east.Single().RotationRadians, west.Single().RotationRadians, 9);
    }

    [Fact]
    public void HorizontalModeIgnoresTheLineDirection()
    {
        var labels = LineLabelPlanner.Plan(
            new StraightPath(100, Math.PI / 3), Options(align: false));

        Assert.Equal(0.0, labels.Single().RotationRadians, 9);
    }

    // ------------------------------------------------------------- label resolve

    [Fact]
    public void SharedCurbLayerResolvesToOneLabel_BecauseBothCodesAgree()
    {
        var catalog = new LineworkCatalog(_fx.Config.LineFeatures);
        var candidates = catalog.Candidates(null, "V-SURF-CURB-E");

        Assert.Equal(2, candidates.Count);                     // CU and CX
        Assert.Equal("CURB", LineworkCatalog.UnanimousLabel(candidates));
    }

    [Theory]
    [InlineData("V-SURF-WALL-E", "WALL")]
    [InlineData("V-SURF-WALL-ROCK-E", "ROCK WALL")]
    [InlineData("V-SURF-FENC-CHNL-E", "CHAIN LINK FENCE")]
    [InlineData("V-SURF-ASPH-E", "EDGE OF PAVEMENT")]
    [InlineData("V-SURF-CONC-E", "EDGE OF CONCRETE")]
    [InlineData("V-SURF-BLDG-E", "BUILDING")]
    [InlineData("V-UTIL-STRM-E", "STORM")]
    public void TheConfirmedGigHarborFamiliesAllResolveToTheirStandard(string layer, string label)
    {
        var catalog = new LineworkCatalog(_fx.Config.LineFeatures);
        Assert.Equal(label, LineworkCatalog.UnanimousLabel(catalog.Candidates(null, layer)));
    }

    [Fact]
    public void AnUnconfiguredFeatureResolvesToNull_NotAGuess()
    {
        var catalog = new LineworkCatalog(_fx.Config.LineFeatures);

        // Guardrail is identified but has no labelling standard configured yet.
        Assert.Null(LineworkCatalog.UnanimousLabel(catalog.Candidates("GRL1", null)));

        // FNC and FHW share a layer and neither has a label: null, never a guess.
        Assert.Null(LineworkCatalog.UnanimousLabel(catalog.Candidates(null, "V-SURF-FENC-E")));
    }

    [Fact]
    public void ADisabledFeatureResolvesToNull()
    {
        var features = new List<LineFeatureRule>
        {
            new LineFeatureRule { Code = "FCK", Layer = "L1", Label = "CHAIN LINK FENCE",
                                  Enabled = false }
        };
        var catalog = new LineworkCatalog(features);
        Assert.Null(LineworkCatalog.UnanimousLabel(catalog.Candidates(null, "L1")));
    }

    // ------------------------------------------------------------------- preview

    [Fact]
    public void DescribeMatchesThePlannerForTheRealInventoryLengths()
    {
        // The 148.8 ft fence: one label. The 2,113 ft of curb as one hypothetical
        // run: repeated labels.
        Assert.Equal("Label existing line \"CHAIN LINK FENCE\" - 1 label at midpoint, on line",
            LineLabelPlanner.Describe("CHAIN LINK FENCE", 148.8, 10, 200, 5));

        var longRun = LineLabelPlanner.Describe("CURB", 2113, 10, 200, 5);
        Assert.Contains("11 labels", longRun);
        Assert.Contains("ft apart", longRun);
    }

    [Fact]
    public void DescribeIsHonestAboutTheQuietCases()
    {
        Assert.Contains("standard not configured",
            LineLabelPlanner.Describe(null, 100, 10, 200, 5));
        Assert.Contains("shorter than the 10 ft minimum",
            LineLabelPlanner.Describe("WALL", 6, 10, 200, 5));
    }
}
