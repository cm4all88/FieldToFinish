using FieldCodes;
using FieldCodes.Geometry;

namespace FieldCodes.Tests;

/// <summary>
/// Civil 3D owns the survey symbol; FTF polishes what it produced.
///
/// The danger these tests guard is quiet: naming a block in an ordinary rule used to
/// be enough to make FTF insert one, which would put a second symbol on top of every
/// point that rule matched. Duplicated symbols look almost right, which is how they
/// reach a plan set.
/// </summary>
public sealed class BlockInsertionSafeguardTests : IClassFixture<RulesFixture>
{
    private readonly RulesFixture _fx;

    public BlockInsertionSafeguardTests(RulesFixture fx) => _fx = fx;

    private static RulesConfig Fresh()
        => RulesConfig.Load(Path.Combine(AppContext.BaseDirectory, "rules.json"));

    // ------------------------------------------------------------- the safeguard

    [Fact]
    public void NamingABlockIsNotEnoughToInsertOne()
    {
        // The exact accident: someone adds a block to a working rule.
        var cfg = Fresh();
        var tree = cfg.Codes.Single(c => c.Id == "tree");
        tree.Block = "TREE-{species}";
        tree.BlockLayer = "V-TREE";

        var p = new FieldCodeParser(cfg).Parse("1", "CON 18 . 25");

        Assert.Equal("TREE-CONIFER", p.BlockName);   // the rule names one...
        Assert.False(p.InsertBlock);                 // ...and FTF still inserts nothing
    }

    [Fact]
    public void InsertingRequiresAnExplicitOptIn()
    {
        var cfg = Fresh();
        var tree = cfg.Codes.Single(c => c.Id == "tree");
        tree.Block = "TREE-{species}";
        tree.InsertBlock = true;                     // deliberate, not incidental

        var p = new FieldCodeParser(cfg).Parse("1", "CON 18 . 25");

        Assert.True(p.InsertBlock);
        Assert.Equal("TREE-CONIFER", p.BlockName);
    }

    [Fact]
    public void OptInDefaultsToFalseOnAFreshRule()
        => Assert.False(new CodeRule().InsertBlock);

    [Fact]
    public void NoShippedRuleInsertsABlock()
    {
        // The production guarantee: nothing in rules.json duplicates a Civil 3D symbol.
        foreach (var code in _fx.Config.Codes)
            Assert.False(code.InsertBlock,
                "rule '" + code.Id + "' inserts a block; Civil 3D should own that symbol");
    }

    [Theory]
    [InlineData("CON 18 . 25")]
    [InlineData("PP 1234")]
    [InlineData("SIGN STOP 135")]
    public void NoShippedFeatureAsksForABlock(string desc)
        => Assert.False(_fx.Parse(desc).InsertBlock);

    [Fact]
    public void AModifierCannotSmuggleInABlockEither()
    {
        // Modifiers can substitute a block name. That must not bypass the opt-in.
        var cfg = Fresh();
        var cluster = cfg.Modifiers.Single(m => m.Id == "cluster");
        cluster.Block = "TREE-CLUSTER";

        var p = new FieldCodeParser(cfg).Parse("1", "CON 18 . 25 CL4");

        Assert.Equal("TREE-CLUSTER", p.BlockName);
        Assert.False(p.InsertBlock);
    }

    // ------------------------------------------------- the sign rotates, not draws

    [Fact]
    public void TheSignRuleRotatesTheExistingMarkerRatherThanDrawingOne()
    {
        var p = _fx.Parse("SIGN STOP 135");

        Assert.False(p.HasErrors);
        Assert.Null(p.BlockName);            // Civil 3D placed the symbol
        Assert.False(p.InsertBlock);
        Assert.Equal(315.0, p.RotationDegrees);   // FTF supplies the rotation
        Assert.Equal("STOP SIGN", p.LabelText);
        Assert.Equal("S", p.TagPrefix);
    }

    // -------------------------------------------- one place for angle conversion

    [Theory]
    [InlineData(0.0, 90.0)]
    [InlineData(90.0, 0.0)]
    [InlineData(135.0, 315.0)]
    [InlineData(180.0, 270.0)]
    [InlineData(270.0, 180.0)]
    [InlineData(360.0, 90.0)]
    public void AzimuthBecomesCadRotation(double azimuth, double expected)
        => Assert.Equal(expected, Angles.AzimuthToCadDegrees(azimuth), 9);

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(90.0, Math.PI / 2)]
    [InlineData(180.0, Math.PI)]
    [InlineData(315.0, 7 * Math.PI / 4)]
    public void CadDegreesBecomeApiRadians(double degrees, double radians)
        => Assert.Equal(radians, Angles.CadDegreesToApiRadians(degrees), 9);

    [Theory]
    [InlineData(-45.0, 315.0)]
    [InlineData(720.0, 0.0)]
    [InlineData(-360.0, 0.0)]
    public void DegreesAlwaysFoldIntoZeroToThreeSixty(double input, double expected)
        => Assert.Equal(expected, Angles.NormalizeDegrees(input), 9);

    [Fact]
    public void RadiansAlwaysFoldIntoZeroToTwoPi()
    {
        Assert.InRange(Angles.CadDegreesToApiRadians(-45), 0, Math.PI * 2);
        Assert.InRange(Angles.CadDegreesToApiRadians(1080), 0, Math.PI * 2);
    }

    [Fact]
    public void TheParserAndTheCadLayerAgreeOnTheSameConversion()
    {
        // The parser produces CAD degrees; the CAD layer converts them to radians.
        // Both go through Angles, so a sign cannot be rotated by one convention and
        // measured by another.
        var p = _fx.Parse("SIGN STOP 135");
        Assert.Equal(Angles.CadDegreesToApiRadians(315.0),
                     Angles.CadDegreesToApiRadians(p.RotationDegrees!.Value), 12);
    }
}
