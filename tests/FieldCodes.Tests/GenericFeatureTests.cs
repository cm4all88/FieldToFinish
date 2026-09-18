using FieldCodes;

namespace FieldCodes.Tests;

/// <summary>
/// Proof that the engine is a general feature engine rather than a tree tool.
///
/// The pole rule in rules.json was added with no C# change whatsoever. Every parse
/// stage is guarded by what the rule declares, so a feature with no species, trunk,
/// drip or rotation simply skips them. These tests exist so that stays true.
/// </summary>
public sealed class GenericFeatureTests : IClassFixture<RulesFixture>
{
    private readonly RulesFixture _fx;

    public GenericFeatureTests(RulesFixture fx) => _fx = fx;

    // ----------------------------------------------------------------- the pole

    [Fact]
    public void APoleWithANumberProducesALabel()
    {
        var p = _fx.Parse("PP 1234");

        Assert.False(p.HasErrors);
        Assert.Equal("pole", p.RuleId);
        Assert.Equal("PP", p.Code);
        Assert.Equal("POLE 1234", p.LabelText);
        Assert.Equal("V-UTIL-POWR-TEXT", p.LabelLayer);
        Assert.Equal("P", p.TagPrefix);
    }

    [Fact]
    public void APoleHasNoTreeAnatomy()
    {
        // The stages a pole rule does not declare must stay silent rather than
        // inventing a species or demanding a drip radius.
        var p = _fx.Parse("PP 1234");

        Assert.Null(p.Species);
        Assert.False(p.TrunkInches.HasValue);
        Assert.Empty(p.Stems);
        Assert.False(p.DripRadius.HasValue);
        Assert.False(p.HasDripLine);
        Assert.False(p.RotationDegrees.HasValue);
        Assert.Null(p.BlockName);        // the point style draws the symbol
    }

    [Fact]
    public void ModifiersComposeOntoANonTreeFeature()
    {
        // Nothing about the modifier machinery is tree-specific.
        var p = _fx.Parse("PP 1234 DEAD");

        Assert.False(p.HasErrors);
        Assert.Equal("POLE 1234 (DEAD)", p.LabelText);
        Assert.Equal("V-UTIL-POWR-TEXT-DEAD", p.LabelLayer);
    }

    [Fact]
    public void ABarePoleLabelsItselfNow_TheSymbolLabelStandard()
    {
        // Since symbolLabels was confirmed, a bare pole labels "PP" on the power
        // text layer. The rule engine is untouched: no rule fired, no diagnostics.
        var p = _fx.Parse("PP");
        Assert.True(p.Recognized);
        Assert.Equal("PP", p.LabelText);
        Assert.False(p.HasErrors);
        Assert.Empty(p.Diagnostics);
    }

    [Fact]
    public void APoleGetsItsOwnTagSequence()
    {
        var points = new[]
        {
            _fx.Parse("CON 18 . 25", "10"),
            _fx.Parse("PP 1234", "20"),
            _fx.Parse("DEC 12 . 18", "30"),
            _fx.Parse("PP 1235", "40")
        };

        var tags = new Tagging.TagAssigner().Assign(points, null);

        Assert.Equal(new[] { "T1", "P1", "T2", "P2" }, tags.Select(t => t.Text));
    }

    // ----------------------------------------------------------------- the sign

    [Fact]
    public void ASignRotatesTheCivil3dSymbolAndAddsTheAnnotation()
    {
        // The division of labour for every point feature: Civil 3D's description keys
        // placed the symbol, FTF supplies the rotation, label, leader and tag.
        var p = _fx.Parse("SIGN STOP 135");

        Assert.False(p.HasErrors);
        Assert.Null(p.BlockName);                 // no second symbol
        Assert.False(p.InsertBlock);
        Assert.Equal(315.0, p.RotationDegrees);   // 135 azimuth -> 315 CAD
        Assert.Equal("STOP SIGN", p.LabelText);
        Assert.Equal(LeaderMode.Always, p.Leader);
        Assert.Equal("S", p.TagPrefix);
    }

    // ------------------------------------------- the wider point-feature set

    [Theory]
    [InlineData("WFH 6", "hydrant", "HYDRANT 6", "V-UTIL-WATR-TEXT")]
    [InlineData("WVL 8", "water-valve", "WATER VALVE 8", "V-UTIL-WATR-TEXT")]
    [InlineData("GVL 2", "gas-valve", "GAS VALVE 2", "V-UTIL-NGAS-TEXT")]
    [InlineData("SSMH 1042", "manhole", "SSMH 1042", "V-UTIL-STRC-TEXT")]
    [InlineData("SDMH 17", "manhole", "SDMH 17", "V-UTIL-STRC-TEXT")]
    [InlineData("CB 4", "catch-basin", "CB 4", "V-UTIL-STRM-TEXT")]
    [InlineData("CBS 9", "catch-basin", "CBS 9", "V-UTIL-STRM-TEXT")]
    [InlineData("UVLT 3", "vault", "VAULT 3", "V-UTIL-STRC-TEXT")]
    [InlineData("PJB 12", "junction-box", "JUNCTION BOX 12", "V-UTIL-POWR-TEXT")]
    [InlineData("MB 1408", "mailbox", "MAILBOX 1408", "V-SITE-TEXT")]
    [InlineData("POST 2", "bollard", "POST 2", "V-SITE-TEXT")]
    [InlineData("LT 7", "light-pole", "LIGHT POLE 7", "V-UTIL-POWR-TEXT")]
    [InlineData("GYA 1", "guy-anchor", "GUY ANCHOR 1", "V-UTIL-POWR-TEXT")]
    public void PointFeatures_AreConfigurationOnly(string desc, string rule,
                                                   string label, string layer)
    {
        // Every one of these was added to rules.json with no C# change. None inserts
        // a block -- Civil 3D's description keys own the symbols.
        var p = _fx.Parse(desc);

        Assert.False(p.HasErrors, desc);
        Assert.Equal(rule, p.RuleId);
        Assert.Equal(label, p.LabelText);
        Assert.Equal(layer, p.LabelLayer);
        Assert.Null(p.BlockName);
        Assert.False(p.InsertBlock);
    }

    [Fact]
    public void SnSign_RotatesTheExistingMarkerFromTheFieldAzimuth()
    {
        // The office sign code. The number is the azimuth, not an id.
        var p = _fx.Parse("SN 135");

        Assert.False(p.HasErrors);
        Assert.Equal("sn-sign", p.RuleId);
        Assert.Equal(315.0, p.RotationDegrees);
        Assert.Equal("SIGN", p.LabelText);
        Assert.Equal("S", p.TagPrefix);
        Assert.Null(p.BlockName);
    }

    [Fact]
    public void ControlPoints_LabelTheirCrossReference()
    {
        // The XMAG/XHT/XNL descriptions were the last codes in the unknown report.
        Assert.Equal("AKA 1013", _fx.Parse("XMAG AKA 1013").LabelText);
        Assert.Equal("AKA 1010 & 1014", _fx.Parse("XHT AKA 1010 & 1014").LabelText);
        Assert.Equal("AKA 1011 & 1015", _fx.Parse("XNL AKA 1011 & 1015").LabelText);
        Assert.Equal("control", _fx.Parse("XMAG AKA 1013").RuleId);
    }

    [Fact]
    public void ModifiersComposeOntoTheNewFeaturesToo()
    {
        var p = _fx.Parse("CB 4 DEAD");
        Assert.False(p.HasErrors);
        Assert.Equal("CB 4 (DEAD)", p.LabelText);
        Assert.Equal("V-UTIL-STRM-TEXT-DEAD", p.LabelLayer);
    }

    [Fact]
    public void BareFeatureCodesLabelThemselves_ButOnlyTheListedOnes()
    {
        // The confirmed symbol codes label with the code itself; rules still fire
        // only when the description carries data after the code.
        foreach (var bare in new[] { "CB", "SSMH", "MB", "LT", "POST", "WVL", "PJB", "SN" })
        {
            var p = _fx.Parse(bare);
            Assert.Equal(bare, p.LabelText);
            Assert.False(p.HasErrors, bare);
        }

        // Codes NOT on the list keep the strict do-nothing behaviour.
        foreach (var bare in new[] { "UVLT", "GYA", "SSCO", "MW" })
            Assert.True(_fx.Parse(bare).NoContent, bare + " should stay 'nothing to draw'");
    }

    [Fact]
    public void LightPolesGetTheirOwnTagSequence()
    {
        var points = new[]
        {
            _fx.Parse("LT 1", "10"),
            _fx.Parse("CON 18 . 25", "20"),
            _fx.Parse("LT 2", "30")
        };

        var tags = new Tagging.TagAssigner().Assign(points, null);
        Assert.Equal(new[] { "L1", "T1", "L2" }, tags.Select(t => t.Text));
    }

    // ------------------------------------------------- rules stay independent

    [Fact]
    public void AddingFeaturesDoesNotDisturbTrees()
    {
        // The pole rule sits ahead of the tree rule in the file. Neither may capture
        // the other's descriptions.
        Assert.Equal("tree", _fx.Parse("CON 18 . 25").RuleId);
        Assert.Equal("pole", _fx.Parse("PP 1234").RuleId);
        Assert.Equal("sign", _fx.Parse("SIGN STOP 135").RuleId);
    }

    [Fact]
    public void ThreeFeatureFamiliesAreConfigured()
    {
        var ids = _fx.Config.Codes.Select(c => c.Id).ToList();
        Assert.Contains("tree", ids);
        Assert.Contains("pole", ids);
        Assert.Contains("sign", ids);
    }

    [Fact]
    public void EveryRuleDeclaresOnlyWhatItNeeds()
    {
        // The shape of the proof: a rule is a declaration, not a code path. A feature
        // that needs no species map or drip line simply does not have one.
        var pole = _fx.Config.Codes.Single(c => c.Id == "pole");

        Assert.Empty(pole.Species);
        Assert.Null(pole.DripLine);
        Assert.Null(pole.Rotation);
        Assert.Null(pole.Block);
        Assert.NotNull(pole.Label);
    }
}
