using FieldCodes;
using FieldCodes.Linework;

namespace FieldCodes.Tests;

/// <summary>
/// Span markers: survey points along a line whose notes carry a marker token
/// (GATE) define spans, labelled centred between each marked pair. Token-exact,
/// strictly paired, odd ones out reported -- never guessed into a span.
/// </summary>
public sealed class SpanMarkerTests
{
    // -------------------------------------------------------------- the token

    [Theory]
    [InlineData("GATE", true)]              // gate shot on its own
    [InlineData("FCK B GATE", true)]        // fence begin, noted gate
    [InlineData("FCK GATE B", true)]        // order does not matter
    [InlineData("gate", true)]              // case does not matter
    [InlineData("GATEPOST", false)]         // not the token
    [InlineData("FCK B", false)]
    [InlineData("", false)]
    public void TheMarkerMatchesAsItsOwnTokenOnly(string note, bool expected)
    {
        Assert.Equal(expected, SpanFinder.HasToken(note, "GATE"));
    }

    // ------------------------------------------------------------- the pairing

    [Fact]
    public void TwoMarkedPointsMakeOneSpanCentredBetweenThem()
    {
        bool leftover;
        var spans = SpanFinder.Pair(new[] { 40.0, 52.0 }, out leftover);

        var span = Assert.Single(spans);
        Assert.Equal(40.0, span.Start, 9);
        Assert.Equal(52.0, span.End, 9);
        Assert.Equal(46.0, span.Middle, 9);
        Assert.False(leftover);
    }

    [Fact]
    public void SeveralGatesPairUpInOrderAlongTheLine()
    {
        // A long fence with two gates: four marked shots, two spans -- regardless
        // of the order the points were shot in.
        bool leftover;
        var spans = SpanFinder.Pair(new[] { 120.0, 40.0, 52.0, 132.0 }, out leftover);

        Assert.Equal(2, spans.Count);
        Assert.Equal(46.0, spans[0].Middle, 9);
        Assert.Equal(126.0, spans[1].Middle, 9);
        Assert.False(leftover);
    }

    [Fact]
    public void AnOddMarkerOutIsReported_NeverGuessedIntoASpan()
    {
        bool leftover;
        var spans = SpanFinder.Pair(new[] { 40.0, 52.0, 90.0 }, out leftover);

        Assert.Single(spans);           // the clean pair still labels
        Assert.True(leftover);          // the lone marker is surfaced, not paired
    }

    [Fact]
    public void DoubledShotsAtOneSpotCountOnce()
    {
        // Two crews shooting the same gate post must not fake a zero-width span.
        bool leftover;
        var spans = SpanFinder.Pair(new[] { 40.0, 40.0, 52.0 }, out leftover);

        Assert.Equal(46.0, Assert.Single(spans).Middle, 9);
        Assert.False(leftover);
    }

    // -------------------------------------------------------- stair direction

    [Fact]
    public void StairTreadsShareOneDirection_RegardlessOfDigitizing()
    {
        // Twelve parallel treads, half drawn east and half west: the label
        // direction is east, not the cancelled-out garbage of naive averaging.
        var directions = new List<double>();
        for (var i = 0; i < 6; i++) { directions.Add(0.0); directions.Add(Math.PI); }

        Assert.Equal(0.0, LineLabelPlanner.AverageDirection(directions), 9);
    }

    [Fact]
    public void SlightlyFannedTreadsAverageBetween()
    {
        var direction = LineLabelPlanner.AverageDirection(
            new List<double> { 0.1, -0.1, 0.1, -0.1 });
        Assert.Equal(0.0, direction, 9);
    }

    [Fact]
    public void TheStairsFormatShipsWithCount()
    {
        var cfg = RulesConfig.Load(Path.Combine(AppContext.BaseDirectory, "rules.json"));
        Assert.Equal("12 STEPS",
            cfg.StairsLabelFormat.Replace("{count}", "12"));
    }

    // ------------------------------------------------------------- the config

    [Fact]
    public void TheShippedRulesCarryTheGateMarker()
    {
        var cfg = RulesConfig.Load(Path.Combine(AppContext.BaseDirectory, "rules.json"));

        var gate = Assert.Single(cfg.SpanMarkers);
        Assert.Equal("FGP", gate.Token);   // GATE POST on the office sheet
        Assert.Equal("GATE", gate.Label);
        Assert.True(gate.Enabled);
    }
}
