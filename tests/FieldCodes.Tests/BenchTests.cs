using FieldCodes;
using FieldCodes.Drafting;
using FieldCodes.Easements;

namespace FieldCodes.Tests;

/// <summary>
/// FTFBENCH: a bench shot as two points at the middles of its ends, depth (and height) in
/// the notes. The outline is the shot-to-shot line offset half the depth each side, ends
/// closed; the label says its size. Nothing is guessed: an unmarked number is reported.
/// </summary>
public sealed class BenchTests
{
    // ----------------------------------------------------------------- reading the notes

    [Theory]
    [InlineData("BENCH D1.5", 1.5)]
    [InlineData("BENCH D 1.5", 1.5)]
    [InlineData("BENCH D=1.5", 1.5)]
    [InlineData("BENCH DEPTH 1.5", 1.5)]
    [InlineData("BENCH 1.5D", 1.5)]
    [InlineData("BENCH 1.5' DEEP", 1.5)]
    [InlineData("BENCH 18IN D", 1.5)]
    [InlineData("BENCH D18\"", 1.5)]
    [InlineData("bench d1.5", 1.5)]
    public void TheDepthIsReadHoweverTheCrewWroteIt(string description, double depth)
    {
        var r = BenchNotes.Read(description);
        Assert.Empty(r.Problems);
        Assert.Equal(depth, r.DepthFeet!.Value, 9);
        Assert.Null(r.HeightFeet);
    }

    [Theory]
    [InlineData("BENCH D1.5 H1.5")]
    [InlineData("BENCH H1.5 D1.5")]
    [InlineData("BENCH 1.5D 1.5H")]
    [InlineData("BENCH D1.5H1.5")]
    [InlineData("BENCH DEPTH 1.5 HEIGHT 18IN")]
    [InlineData("BENCH 1.5 D 1.5 H")]
    public void DepthAndHeightAreToldApartByTheirLetters(string description)
    {
        var r = BenchNotes.Read(description);
        Assert.Empty(r.Problems);
        Assert.Empty(r.UnreadNumbers);
        Assert.Equal(1.5, r.DepthFeet!.Value, 9);
        Assert.Equal(1.5, r.HeightFeet!.Value, 9);
    }

    [Fact]
    public void BareNumbersAreReportedNotAssigned()
    {
        var r = BenchNotes.Read("BENCH 1.5 2");
        Assert.Null(r.DepthFeet);
        Assert.Null(r.HeightFeet);
        Assert.Equal(new[] { "1.5", "2" }, r.UnreadNumbers);
    }

    [Fact]
    public void TheCodeItselfIsNotData()
    {
        var r = BenchNotes.Read("BENCHD");
        Assert.Null(r.DepthFeet);
        Assert.Empty(r.UnreadNumbers);
        Assert.Null(BenchNotes.Read("BENCH2").DepthFeet);
    }

    [Fact]
    public void TwoEndsAgreeingOrOneEndCarryingTheNoteIsFine()
    {
        var both = BenchNotes.Combine(BenchNotes.Read("BENCH D1.5"), BenchNotes.Read("BENCH 1.5D H1.5"));
        Assert.Empty(both.Problems);
        Assert.Equal(1.5, both.DepthFeet!.Value, 9);
        Assert.Equal(1.5, both.HeightFeet!.Value, 9);

        var one = BenchNotes.Combine(BenchNotes.Read("BENCH"), BenchNotes.Read("BENCH D2"));
        Assert.Equal(2.0, one.DepthFeet!.Value, 9);
    }

    [Fact]
    public void TwoEndsDisagreeingIsAProblemForThePersonToSettle()
    {
        var r = BenchNotes.Combine(BenchNotes.Read("BENCH D1.5"), BenchNotes.Read("BENCH D2"));
        Assert.Single(r.Problems);
        Assert.Contains("Two different depths", r.Problems[0]);
    }

    [Theory]
    [InlineData("BENCH D1.5", true)]
    [InlineData("BNCH", true)]
    [InlineData("BENCHL2", true)]
    [InlineData("CB 12", false)]
    public void BenchCodesAreRecognisedWithFigureDigits(string description, bool bench)
    {
        Assert.Equal(bench, BenchPlanner.IsBenchCode(description, new BenchRule().Codes));
    }

    // ----------------------------------------------------------------- the outline and label

    [Fact]
    public void TheOutlineIsOffsetHalfTheDepthEachSideWithTheEndsClosed()
    {
        var plan = BenchPlanner.Plan(new P2(100, 200), new P2(103, 200), 1.5, null, new BenchRule().LabelFormat, 1.0);

        Assert.True(plan.Ok);
        Assert.Empty(plan.Warnings);
        Assert.Equal(4, plan.Corners.Length);
        AssertPoint(100, 200.75, plan.Corners[0]);
        AssertPoint(103, 200.75, plan.Corners[1]);
        AssertPoint(103, 199.25, plan.Corners[2]);
        AssertPoint(100, 199.25, plan.Corners[3]);
        AssertPoint(101.5, 200, plan.Centre);
        Assert.Equal(3.0, plan.LengthFeet, 9);
        Assert.Equal("BENCH 3'x1.5'", plan.LabelText);
    }

    [Fact]
    public void ATurnedBenchKeepsItsShapeAndItsLabelReadsUpright()
    {
        // Ends 6 ft apart running north-west; depth 2 ft.
        var a = new P2(0, 0);
        var b = new P2(-6 / Math.Sqrt(2), 6 / Math.Sqrt(2));
        var plan = BenchPlanner.Plan(a, b, 2.0, null, "BENCH {length:0.#}'x{depth:0.#}'", 1.0);

        Assert.Equal("BENCH 6'x2'", plan.LabelText);
        for (var i = 0; i < 4; i++)
        {
            var side = (plan.Corners[(i + 1) % 4] - plan.Corners[i]).Length;
            Assert.Equal(i % 2 == 0 ? 6.0 : 2.0, side, 9);
        }
        var deg = plan.LabelRotationRadians * 180.0 / Math.PI;
        Assert.InRange(deg, -90.0001, 90.0001);
        Assert.Equal(-45.0, deg, 6);
    }

    [Fact]
    public void LengthIsWhatWasShotRoundedOnlyInTheLabel()
    {
        var plan = BenchPlanner.Plan(new P2(0, 0), new P2(6.04, 0), 1.5, 1.5, "BENCH {length:0.#}'x{depth:0.#}'\\PH={height:0.#}'", 1.0);
        Assert.Equal(6.04, plan.LengthFeet, 9);
        AssertPoint(6.04, 0.75, plan.Corners[1]);
        Assert.Equal(new[] { "BENCH 6'x1.5'", "H=1.5'" }, plan.LabelLines);
    }

    [Fact]
    public void AHeightLineIsDroppedWhenNoHeightWasGiven()
    {
        var plan = BenchPlanner.Plan(new P2(0, 0), new P2(4, 0), 1.5, null, "BENCH {length:0.#}'x{depth:0.#}'\\P[H={height:0.#}']", 1.0);
        Assert.Equal(new[] { "BENCH 4'x1.5'" }, plan.LabelLines);
        Assert.True(BenchPlanner.UsesHeight("X {height:0.#}"));
        Assert.False(BenchPlanner.UsesHeight(new BenchRule().LabelFormat));
    }

    [Fact]
    public void DrawingUnitsPerFootScaleTheOutlineNotTheLabel()
    {
        var plan = BenchPlanner.Plan(new P2(0, 0), new P2(36, 0), 1.5, null, new BenchRule().LabelFormat, 12.0);
        Assert.Equal("BENCH 3'x1.5'", plan.LabelText);
        AssertPoint(0, 9, plan.Corners[0]);
    }

    [Fact]
    public void ImpossibleBenchesAreRefusedAndOddOnesWarned()
    {
        Assert.False(BenchPlanner.Plan(new P2(0, 0), new P2(0, 0), 1.5, null, "B", 1.0).Ok);
        Assert.False(BenchPlanner.Plan(new P2(0, 0), new P2(4, 0), 0, null, "B", 1.0).Ok);
        Assert.False(BenchPlanner.Plan(new P2(0, 0), new P2(4, 0), 1.5, -1, "B", 1.0).Ok);

        var across = BenchPlanner.Plan(new P2(0, 0), new P2(1.2, 0), 6, null, "B", 1.0);
        Assert.True(across.Ok);
        Assert.Contains(across.Warnings, w => w.Contains("front and back"));
        Assert.Contains(BenchPlanner.Plan(new P2(0, 0), new P2(60, 0), 1.5, null, "B", 1.0).Warnings, w => w.Contains("longer than any bench"));
    }

    [Fact]
    public void TheShippedRulesCarryABenchStandard()
    {
        var cfg = RulesConfig.Load(Path.Combine(AppContext.BaseDirectory, "rules.json"));
        Assert.Equal("V-SITE-TEXT", cfg.Bench.LabelLayer);
        Assert.Contains("BENCH", cfg.Bench.Codes);
        Assert.Equal("BENCH {length:0.#}'x{depth:0.#}'", cfg.Bench.LabelFormat);
    }

    [Fact]
    public void ARulesFileWithoutABenchSectionGetsTheDefaults()
    {
        var cfg = Newtonsoft.Json.JsonConvert.DeserializeObject<RulesConfig>(@"{ ""codes"": [ { ""id"": ""x"", ""match"": ""^X$"" } ] }")!;
        cfg.Compile();
        Assert.Equal("BENCH {length:0.#}'x{depth:0.#}'", cfg.Bench.LabelFormat);
        Assert.False(string.IsNullOrEmpty(cfg.Bench.Layer));
    }

    private static void AssertPoint(double x, double y, P2 p)
    {
        Assert.Equal(x, p.X, 9);
        Assert.Equal(y, p.Y, 9);
    }
}
