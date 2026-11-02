using FieldCodes;

namespace FieldCodes.Tests;

public sealed class LayerClassifierTests : IClassFixture<RulesFixture>
{
    private readonly LayerClassifier _c;

    public LayerClassifierTests(RulesFixture fx) => _c = new LayerClassifier(fx.Config);

    [Theory]
    [InlineData("V-TREE-DEAD", "V-TREE")]
    [InlineData("V-TREE-DRIP-DEAD", "V-TREE-DRIP")]
    [InlineData("V-TREE-TEXT-DEAD", "V-TREE-TEXT")]
    [InlineData("V-SIGN-DEAD", "V-SIGN")]
    [InlineData("V-TREE-DRIP-DEAD-RMV-PROT", "V-TREE-DRIP")]
    [InlineData("V-TREE-RMV-PROT", "V-TREE")]
    [InlineData("V-TREE", "V-TREE")]              // nothing to strip
    [InlineData("C-TOPO-MAJR", "C-TOPO-MAJR")]    // unrelated layer untouched
    public void StripsModifierSuffixesDownToBaseLayer(string input, string expected)
        => Assert.Equal(expected, _c.StripModifierSuffixes(input));

    // The bug this class exists to fix: a modified sign must stay protected.
    [Theory]
    [InlineData("V-SURF-SIGN-SYMB-E")]
    [InlineData("V-SURF-SIGN-SYMB-E-DEAD")]
    [InlineData("V-SURF-SIGN-SYMB-E-RMV-PROT")]
    public void ModifiedSignLayerIsStillASymbol(string layer)
    {
        Assert.Equal(LayerRole.Symbol, _c.Classify(layer));
        Assert.Equal(ObstacleClass.Hard, _c.ObstacleForLayer(layer));
    }

    [Theory]
    [InlineData("V-SURF-VEGE-E")]
    [InlineData("V-SURF-VEGE-E-DEAD")]
    [InlineData("V-SURF-VEGE-E-DEAD-RMV-PROT")]
    public void ModifiedDripLayerIsStillMaskableLinework(string layer)
    {
        Assert.Equal(LayerRole.MaskableLinework, _c.Classify(layer));
        Assert.Equal(ObstacleClass.Free, _c.ObstacleForLayer(layer));
        Assert.Equal(DrawOrderBand.MaskableLinework, LayerClassifier.BandOf(_c.Classify(layer)));
    }

    [Theory]
    [InlineData("V-SURF-VEGE-TEXT-E")]
    [InlineData("V-SURF-VEGE-TEXT-E-DEAD")]
    [InlineData("V-SURF-SIGN-TEXT-E")]      // also matches V-SURF-SIGN-*: the rule's label layer wins
    [InlineData("V-SURF-SIGN-TEXT-E-DEAD")]
    public void LabelLayersAreDerivedFromCodeRules_AndAreHardObstacles(string layer)
    {
        Assert.Equal(LayerRole.Label, _c.Classify(layer));
        Assert.Equal(ObstacleClass.Hard, _c.ObstacleForLayer(layer));
        Assert.Equal(DrawOrderBand.Label, LayerClassifier.BandOf(_c.Classify(layer)));
    }

    [Theory]
    [InlineData("V-SURF-POST-SYMB-E")]       // any PMX surface-feature symbol layer
    [InlineData("V-SURF-MBOX-SYMB-E-DEAD")]
    public void PmxSymbolLayersAreSymbols(string layer)
    {
        Assert.Equal(LayerRole.Symbol, _c.Classify(layer));
        Assert.Equal(DrawOrderBand.Symbol, LayerClassifier.BandOf(_c.Classify(layer)));
    }

    [Fact]
    public void TreeSymbolLayerIsNoLongerDerived_BecauseThePointStyleOwnsTheSymbol()
    {
        // The tree rule places no block, so V-TREE is not a layer this tool writes to.
        // The actual tree symbols belong to CogoPoints, which the CAD layer classifies
        // by entity type rather than by layer -- a point group can sit on any layer.
        Assert.Equal(LayerRole.Unclassified, _c.Classify("V-TREE"));

        // Unclassified is Soft, never Free: an unrecognised thing is not safe to mask.
        Assert.Equal(ObstacleClass.Soft, _c.ObstacleForLayer("V-TREE"));
    }

    [Theory]
    [InlineData("V-UTIL-WATR-SYMB-E")]
    [InlineData("V-SIGL-EQPM-SYMB-E")]
    [InlineData("V-CTRL-PMX_-SYMB-E")]
    [InlineData("V-PROP-BNDY-SYMB")]
    public void ProtectedGlobsMatch(string layer)
        => Assert.Equal(LayerRole.Symbol, _c.Classify(layer));

    [Theory]
    [InlineData("V-TOPO-CONT-E")]
    [InlineData("V-TOPO-CONT-MAJR-E")]
    [InlineData("V-TOPO-MINR-E")]
    [InlineData("V-TINN-BRKL-E")]
    [InlineData("V-ROAD-CNTR")]
    public void MaskableGlobsMatch(string layer)
        => Assert.Equal(LayerRole.MaskableLinework, _c.Classify(layer));

    [Theory]
    [InlineData("V-MISC")]
    [InlineData("0")]
    [InlineData("SOMEONES-XREF|TEXT")]
    public void UnknownLayersAreSoft_NotFree(string layer)
    {
        Assert.Equal(LayerRole.Unclassified, _c.Classify(layer));
        // Masking something we failed to recognise is worse than nudging a label.
        Assert.Equal(ObstacleClass.Soft, _c.ObstacleForLayer(layer));
    }

    [Fact]
    public void LabelLayerIsNoLongerListedAsMaskable()
    {
        // The config contradiction: step 7 makes labels hard obstacles.
        Assert.DoesNotContain("V-SURF-VEGE-TEXT-E", new LayerClassifierConfigProbe().MaskableLayers);
    }

    [Fact]
    public void ClassificationIsCaseInsensitive()
    {
        Assert.Equal(LayerRole.Symbol, _c.Classify("v-surf-sign-symb-e-dead"));
        Assert.Equal(LayerRole.MaskableLinework, _c.Classify("v-surf-vege-e"));
    }

    [Fact]
    public void BandOrderIsBottomToTop()
    {
        Assert.True((int)DrawOrderBand.MaskableLinework < (int)DrawOrderBand.Mask);
        Assert.True((int)DrawOrderBand.Mask < (int)DrawOrderBand.Symbol);
        Assert.True((int)DrawOrderBand.Symbol < (int)DrawOrderBand.Label);
    }

    private sealed class LayerClassifierConfigProbe
    {
        public List<string> MaskableLayers { get; }
        public LayerClassifierConfigProbe()
            => MaskableLayers = new RulesFixture().Config.MaskableLayers;
    }
}
