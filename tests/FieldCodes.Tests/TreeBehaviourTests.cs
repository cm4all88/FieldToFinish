using FieldCodes;
using FieldCodes.Tagging;

namespace FieldCodes.Tests;

/// <summary>
/// Characterisation tests for the tree feature family.
///
/// Trees are the first complete feature and the reference implementation for
/// everything that follows. This file exists to make "trees still work" a mechanical
/// fact rather than a promise: it pins every output a tree produces, so generalising
/// the engine cannot quietly change what the demonstration draws.
///
/// If a change here fails, the question is whether the tree behaviour was meant to
/// change -- not whether the test needs updating.
/// </summary>
public sealed class TreeBehaviourTests : IClassFixture<RulesFixture>
{
    private readonly RulesFixture _fx;

    public TreeBehaviourTests(RulesFixture fx) => _fx = fx;

    // ------------------------------------------------------- code interpretation

    [Theory]
    [InlineData("CON 18 . 25", "CON", "CONIFER")]
    [InlineData("DEC 12 . 18", "DEC", "DECIDUOUS")]
    [InlineData("MAP 4 6 . 20", "MAP", "MAPLE")]
    public void TreeCodesResolveToSpecies(string desc, string code, string species)
    {
        var p = _fx.Parse(desc);
        Assert.Equal("tree", p.RuleId);
        Assert.Equal(code, p.Code);
        Assert.Equal(species, p.Species);
        Assert.False(p.HasErrors);
    }

    [Fact]
    public void SpeciesOutsideTheMapIsNotTreatedAsATree()
    {
        var p = _fx.Parse("SPR 18 . 25");
        Assert.True(p.Unhandled);
        Assert.NotEqual("tree", p.RuleId);
    }

    // ------------------------------------------------------------ trunk sizing

    [Fact]
    public void SingleTrunkIsReportedAsMeasured()
    {
        var p = _fx.Parse("CON 18 . 25");
        Assert.Equal(18.0, p.TrunkInches);
        Assert.Equal(1, p.StemCount);
        Assert.Equal(new[] { 18.0 }, p.Stems);
    }

    [Fact]
    public void MultiStemAveragesAndKeepsTheRawStems()
    {
        // Real point 10142. The raw stems must survive: an agency using a different
        // formula cannot recompute the average from the average.
        var p = _fx.Parse("CON 6 8 8 12 18 18 . 30");

        Assert.Equal(70.0 / 6.0, p.TrunkInches!.Value, 9);
        Assert.Equal(6, p.StemCount);
        Assert.Equal(new[] { 6.0, 8.0, 8.0, 12.0, 18.0, 18.0 }, p.Stems);
        Assert.Equal("6,8,8,12,18,18", p.Fields["trunk.stems"]);
        Assert.Equal("6", p.Fields["trunk.count"]);
    }

    [Fact]
    public void RoundedTrunkIsForDisplay_ExactValueDrivesGeometry()
    {
        // Real point 10145: averages 10.5, labels as 10, must never scale as 10.
        var p = _fx.Parse("CON 8 8 10 16 . 28");
        Assert.Equal(10.5, p.TrunkInches!.Value, 9);
        Assert.Equal("10.5", p.Fields["trunk.exact"]);
        Assert.Equal("10", p.Fields["trunk"]);
    }

    [Theory]
    [InlineData("CON 18 . 25", 1)]
    [InlineData("DEC 12 12 . 14", 2)]
    [InlineData("MAP 4 6 8 . 30", 3)]
    [InlineData("CON 8 10 12 18 18 18 24 . 45", 7)]
    public void StemCountComesFromTheNumberOfDiameters(string desc, int expected)
        => Assert.Equal(expected, _fx.Parse(desc).StemCount);

    // -------------------------------------------------------------- drip lines

    [Theory]
    [InlineData("CON 18 . 25", 25.0)]
    [InlineData("CON 8 10 12 18 18 18 24 . 45", 45.0)]
    [InlineData("DEC 12 10 . 35", 35.0)]
    public void DripRadiusIsReadAsARadius(string desc, double radius)
    {
        var p = _fx.Parse(desc);
        Assert.Equal(radius, p.DripRadius);
        Assert.True(p.HasDripLine);
        Assert.Equal("V-TREE-DRIP", p.DripLayer);
        Assert.True(p.DripUnify);
    }

    [Fact]
    public void MissingDripRadiusIsAnError_WithNoFallback()
    {
        var p = _fx.Parse("CON 18");
        Assert.True(p.HasErrors);
        Assert.Contains(p.Diagnostics, d => d.Code == "DRIP");
        Assert.False(p.DripRadius.HasValue);
        Assert.False(p.HasDripLine);
    }

    // ------------------------------------------------------------ tree labels

    [Fact]
    public void SingleStemLabel()
    {
        var p = _fx.Parse("CON 18 . 25");
        Assert.Equal("18\" CONIFER", p.LabelText);
        Assert.Equal("V-TREE-TEXT", p.LabelLayer);
        Assert.Equal(LeaderMode.Auto, p.Leader);
    }

    [Fact]
    public void ClusterLabelAnnouncesItselfWithoutAModifier()
    {
        var p = _fx.Parse("CON 6 8 8 12 18 18 . 30");
        Assert.Equal("CLUSTER OF 6 STEMS, AVG 12\" CONIFER", p.LabelText);
    }

    [Theory]
    [InlineData("CON 18 . 25 DEAD", " (DEAD)", "V-TREE-TEXT-DEAD", "V-TREE-DRIP-DEAD")]
    [InlineData("CON 18 . 25 REM", " (REMOVE)", "V-TREE-TEXT-RMV", "V-TREE-DRIP-RMV")]
    [InlineData("CON 18 . 25 PROT", " (PROTECTED)", "V-TREE-TEXT-PROT", "V-TREE-DRIP-PROT")]
    public void ModifiersSuffixTheLabelAndEveryLayer(
        string desc, string suffix, string labelLayer, string dripLayer)
    {
        var p = _fx.Parse(desc);
        Assert.False(p.HasErrors);
        Assert.EndsWith(suffix, p.LabelText);
        Assert.Equal(labelLayer, p.LabelLayer);
        Assert.Equal(dripLayer, p.DripLayer);
    }

    [Fact]
    public void ModifierOrderDoesNotChangeTheResult()
    {
        Assert.Equal(Canon.Of(_fx.Parse("CON 18 . 25 CL4 DEAD")),
                     Canon.Of(_fx.Parse("CON 18 . 25 DEAD CL4")));
    }

    [Fact]
    public void UnknownModifierIsAnError_NeverIgnored()
    {
        var p = _fx.Parse("CON 18 . 25 DEAD-PROTECTED");
        Assert.True(p.HasErrors);
        Assert.Contains(p.Diagnostics, d => d.Code == "MODIFIER");
    }

    // ------------------------------------------------------------------- tags

    [Fact]
    public void TreesCarryTheTPrefix()
        => Assert.Equal("T", _fx.Parse("CON 18 . 25").TagPrefix);

    [Fact]
    public void TagNumbersSurviveARerun()
    {
        var points = new[] { "CON 18 . 25", "DEC 12 . 18", "MAP 4 . 20" }
            .Select((d, i) => _fx.Parse(d, (10 + i).ToString())).ToList();

        var assigner = new TagAssigner();
        var first = assigner.Assign(points, null);
        var again = assigner.Assign(points,
            first.ToDictionary(t => t.PointNumber, t => t.Text));

        Assert.Equal(new[] { "T1", "T2", "T3" }, first.Select(t => t.Text));
        Assert.Equal(first.Select(t => t.Text), again.Select(t => t.Text));
        Assert.All(again, t => Assert.False(t.IsNew));
    }

    // ---------------------------------------------------------------- schedule

    [Fact]
    public void ScheduleCarriesTheReviewedValues()
    {
        var points = new[] { _fx.Parse("CON 8 8 10 16 . 28", "10145") };
        var tags = new TagAssigner().Assign(points, null);

        var table = TagTable.Build(tags, points, TagTable.DefaultColumns(), "TREE SCHEDULE", 0);
        var row = table.Rows.Single();

        Assert.Equal("T1", row[0]);
        Assert.Equal("10145", row[1]);
        Assert.Equal("CONIFER", row[2]);
        Assert.Equal("10", row[3]);      // trunkDecimals = 0
        Assert.Equal("4", row[4]);
        Assert.Equal("28", row[5]);
    }

    // ------------------------------------------- the whole family, end to end

    [Fact]
    public void EveryTreeInTheDemoDrawingStillParsesCleanly()
    {
        // The 19 real descriptions from 553-2750-051-SV-BASE. If the engine ever stops
        // handling these, the demonstration is broken.
        var real = new[]
        {
            "CON 18 . 25", "CON 22 . 25", "CON 12 . 18", "CON 8 . 10", "CON 18 . 25",
            "CON 18 . 20", "CON 6 8 8 12 18 18 . 30", "CON 22 . 25", "CON 8 8 10 16 . 28",
            "CON 18 22 . 28", "CON 8 10 12 18 18 18 24 . 45", "DEC 6 6 8 8 10 . 25",
            "DEC 12 12 . 14", "CON 6 . 8", "CON 12 . 25", "DEC 12 10 . 35",
            "MAP 4 6 . 20", "MAP 4 6 8 . 30", "DEC 18 22 . 30"
        };

        foreach (var desc in real)
        {
            var p = _fx.Parse(desc);
            Assert.False(p.HasErrors, desc + " should parse cleanly");
            Assert.Equal("tree", p.RuleId);
            Assert.True(p.HasDripLine, desc + " should have a drip line");
            Assert.False(string.IsNullOrEmpty(p.LabelText), desc + " should have a label");
            Assert.Equal("T", p.TagPrefix);
        }

        Assert.Equal(19, real.Length);
    }
}
