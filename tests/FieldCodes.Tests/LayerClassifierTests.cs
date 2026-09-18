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
    [InlineData("V-SIGN")]
    [InlineData("V-SIGN-DEAD")]
    [InlineData("V-SIGN-RMV-PROT")]
    public void ModifiedSignLayerIsStillASymbol(string layer)
    {
        Assert.Equal(LayerRole.Symbol, _c.Classify(layer));
        Assert.Equal(ObstacleClass.Hard, _c.ObstacleForLayer(layer));
    }

    [Theory]
    [InlineData("V-TREE-DRIP")]
    [InlineData("V-TREE-DRIP-DEAD")]
    [InlineData("V-TREE-DRIP-DEAD-RMV-PROT")]
    public void ModifiedDripLayerIsStillMaskableLinework(string layer)
    {
        Assert.Equal(LayerRole.MaskableLinework, _c.Classify(layer));
        Assert.Equal(ObstacleClass.Free, _c.ObstacleForLayer(layer));
        Assert.Equal(DrawOrderBand.MaskableLinework, LayerClassifier.BandOf(_c.Classify(layer)));
    }

    [Theory]
    [InlineData("V-TREE-TEXT")]
    [InlineData("V-TREE-TEXT-DEAD")]
    [InlineData("V-SIGN-TEXT")]        // was in neither config list; derived from the rule
    [InlineData("V-SIGN-TEXT-DEAD")]
    public void LabelLayersAreDerivedFromCodeRules_AndAreHardObstacles(string layer)
    {
        Assert.Equal(LayerRole.Label, _c.Classify(layer));
        Assert.Equal(ObstacleClass.Hard, _c.ObstacleForLayer(layer));
        Assert.Equal(DrawOrderBand.Label, LayerClassifier.BandOf(_c.Classify(layer)));
    }

    [Theory]
    [InlineData("V-SIGN")]             // derived from the sign rule's blockLayer
    [InlineData("V-SIGN-DEAD")]
    public void BlockLayersNamedByACodeRuleAreSymbols(string layer)
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
    [InlineData("V-UTIL-WATR")]
    [InlineData("V-STRC-FNDN")]
    [InlineData("C-STRM-STRC")]
    public void ProtectedGlobsMatch(string layer)
        => Assert.Equal(LayerRole.Symbol, _c.Classify(layer));

    [Theory]
    [InlineData("C-TOPO-MAJR")]
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
        Assert.DoesNotContain("V-TREE-TEXT", new LayerClassifierConfigProbe().MaskableLayers);
    }

    [Fact]
    public void ClassificationIsCaseInsensitive()
    {
        Assert.Equal(LayerRole.Symbol, _c.Classify("v-sign-dead"));
        Assert.Equal(LayerRole.MaskableLinework, _c.Classify("v-tree-drip"));
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
