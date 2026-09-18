using FieldCodes;
using FieldCodes.Linework;

namespace FieldCodes.Tests;

/// <summary>
/// Label-layer resolution, pinned against the REAL layer table dumped from
/// 553-2750-051-SV-BASE (177 layers; the relevant ones below verbatim). The
/// office standard the data shows: linework V-GROUP-FEAT-E pairs with text
/// V-GROUP-FEAT-TEXT-E, and less specific features fall back to the group text
/// layer (V-SURF-FENC-CHNL-E pairs with V-SURF-FENC-TEXT-E). The resolver only
/// ever returns a layer that exists or one explicitly configured -- it cannot
/// invent a name that then gets created.
/// </summary>
public sealed class LabelLayerResolverTests
{
    private const string Default = "V-LINE-TEXT";

    /// <summary>Verbatim from the drawing's layer table dump.</summary>
    private static readonly HashSet<string> GigHarbor = LabelLayerResolver.BuildCatalog(new[]
    {
        "V-ALGN", "V-ALGN-CNTR-E", "V-ALGN-TEXT",
        "V-CHAN-MRKG-TEXT-E", "V-CHAN-STRP-E", "V-CHAN-STRP-TEXT-E",
        "V-PROP-BNDY-E", "V-PROP-RWAY-E", "V-PROP-SIXT-TEXT-E", "V-PROP-TEXT",
        "V-SURF-ASPH-E", "V-SURF-ASPH-TEXT-E",
        "V-SURF-CONC-E", "V-SURF-CONC-TEXT-E",
        "V-SURF-CURB-E", "V-SURF-CURB-TEXT-E",
        "V-SURF-FENC-CHNL-E", "V-SURF-FENC-TEXT-E",
        "V-SURF-HDRL-E",
        "V-SURF-WALL-E", "V-SURF-WALL-ROCK-E", "V-SURF-WALL-TEXT-E",
        "V-SURF-ROCK-TEXT-E", "V-SURF-BLDG-E", "V-SURF-BLDG-TEXT-E",
        "V-SURF-STAR-E", "V-SURF-GRAS-TEXT-E", "V-SURF-VEGE-TEXT-E",
        "V-TINN-BRKL-E", "V-TINN-BRKL-E-FEATURES",
        "V-TOPO-CONT-MAJR-E", "V-TOPO-CONT-TEXT-E", "V-TOPO-CONT-TEXT",
        "V-UTIL-POWR-STRC-E", "V-UTIL-POWR-TEXT-E",
        "V-UTIL-STRM-E", "V-UTIL-STRM-TEXT-E", "V-UTIL-TEXT-E"
    });

    private static LabelLayerResolution Resolve(string sourceLayer,
                                                string? explicitLayer = null)
    {
        var candidates = explicitLayer == null
            ? new List<LineFeatureRule>()
            : new List<LineFeatureRule>
              {
                  new LineFeatureRule { Code = "X", LabelLayer = explicitLayer }
              };
        return LabelLayerResolver.Resolve(sourceLayer, candidates, GigHarbor, Default);
    }

    // -------------------------------------------------------------- priorities

    [Fact]
    public void AnExplicitRuleLayerBeatsEverything()
    {
        var r = Resolve("V-CHAN-STRP-E", explicitLayer: "V-SPECIAL-TEXT");
        Assert.Equal("V-SPECIAL-TEXT", r.Layer);
        Assert.Equal(LabelLayerSource.ExplicitRule, r.Source);
    }

    // ----------------------------------------------- derived from source layer

    [Theory]
    [InlineData("V-CHAN-STRP-E", "V-CHAN-STRP-TEXT-E")]   // the driving example
    [InlineData("V-SURF-ASPH-E", "V-SURF-ASPH-TEXT-E")]
    [InlineData("V-SURF-CURB-E", "V-SURF-CURB-TEXT-E")]
    [InlineData("V-SURF-BLDG-E", "V-SURF-BLDG-TEXT-E")]
    [InlineData("V-UTIL-STRM-E", "V-UTIL-STRM-TEXT-E")]
    public void TheOfficeStandardTextLayerIsDerivedAndMustExist(string source, string expected)
    {
        var r = Resolve(source);
        Assert.Equal(expected, r.Layer);
        Assert.Equal(LabelLayerSource.DerivedText, r.Source);
    }

    [Theory]
    [InlineData("V-SURF-FENC-CHNL-E", "V-SURF-FENC-TEXT-E")]  // no CHNL text layer
    [InlineData("V-SURF-WALL-ROCK-E", "V-SURF-WALL-TEXT-E")]  // wall family text
    [InlineData("V-TOPO-CONT-MAJR-E", "V-TOPO-CONT-TEXT-E")]  // -E form preferred
    [InlineData("V-UTIL-POWR-STRC-E", "V-UTIL-POWR-TEXT-E")]
    [InlineData("V-ALGN-CNTR-E", "V-ALGN-TEXT")]              // non-E form exists
    public void LessSpecificFeaturesFallBackToTheirGroupTextLayer(string source, string expected)
    {
        var r = Resolve(source);
        Assert.Equal(expected, r.Layer);
        Assert.Equal(LabelLayerSource.DerivedText, r.Source);
    }

    [Fact]
    public void ADerivedNameThatDoesNotExistIsNeverReturned()
    {
        // Same source, but a catalog WITHOUT the office text layers: the blind
        // "-E to -TEXT-E" swap must not surface. Default instead, visibly.
        var bare = LabelLayerResolver.BuildCatalog(new[] { "V-CHAN-STRP-E" });
        var r = LabelLayerResolver.Resolve("V-CHAN-STRP-E",
            new List<LineFeatureRule>(), bare, Default);

        Assert.Equal(Default, r.Layer);
        Assert.Equal(LabelLayerSource.Default, r.Source);
        Assert.Contains("no family text layer", r.Describe());
    }

    // ------------------------------------------------------------ family logic

    [Fact]
    public void ASingleFamilyTextLayerIsUsedWhenNoDerivationMatches()
    {
        var catalog = LabelLayerResolver.BuildCatalog(new[]
        {
            "V-RAIL-TRAK-E", "V-RAIL-TEXT2-E", "V-RAIL-SYMB-E"
        });
        // No V-RAIL-TRAK-TEXT*, no V-RAIL-TEXT* derivation hit; exactly one layer
        // in the family carries a TEXT token? "TEXT2" is not the TEXT token -- so
        // nothing matches and the default reports honestly.
        var none = LabelLayerResolver.Resolve("V-RAIL-TRAK-E",
            new List<LineFeatureRule>(), catalog, Default);
        Assert.Equal(LabelLayerSource.Default, none.Source);

        catalog = LabelLayerResolver.BuildCatalog(new[]
        {
            "V-RAIL-TRAK-E", "V-RAIL-SGNL-TEXT-E"
        });
        var single = LabelLayerResolver.Resolve("V-RAIL-TRAK-E",
            new List<LineFeatureRule>(), catalog, Default);
        Assert.Equal("V-RAIL-SGNL-TEXT-E", single.Layer);
        Assert.Equal(LabelLayerSource.FamilyText, single.Source);
    }

    [Fact]
    public void SeveralPlausibleFamilyLayersAreReported_NeverChosenFrom()
    {
        // Real ambiguity from the drawing: the handrail V-SURF-HDRL-E has no
        // derivable text layer (no V-SURF-HDRL-TEXT*, no V-SURF-TEXT*), and the
        // surface family holds many text layers. FTF refuses to pick and says so.
        var r = Resolve("V-SURF-HDRL-E");

        Assert.Equal(Default, r.Layer);
        Assert.Equal(LabelLayerSource.Default, r.Source);
        Assert.True(r.FamilyCandidates.Count > 1);
        Assert.Contains("V-SURF-WALL-TEXT-E", r.FamilyCandidates);
        Assert.Contains("V-SURF-CURB-TEXT-E", r.FamilyCandidates);
        Assert.Contains("ambiguous", r.Describe());
    }

    [Theory]
    [InlineData("V-PROP-BNDY-E")]
    [InlineData("V-PROP-RWAY-E")]
    public void PropertyLinesDeriveThePropertyTextLayerExactly(string source)
    {
        // No V-PROP-BNDY-TEXT* or V-PROP-RWAY-TEXT* exists, but the group
        // derivation V-PROP-TEXT exists verbatim -- an exact office-standard
        // match, preferred over reporting the family as ambiguous.
        var r = Resolve(source);
        Assert.Equal("V-PROP-TEXT", r.Layer);
        Assert.Equal(LabelLayerSource.DerivedText, r.Source);
    }

    // ------------------------------------------------------------ honest edges

    [Theory]
    [InlineData("V-TINN-BRKL-E")]            // no TINN text layer at all
    [InlineData("V-TINN-BRKL-E-FEATURES")]
    [InlineData("0")]                        // not a structured layer name
    [InlineData(null)]
    public void NoSuitableTextLayerFallsBackToTheDefault_Visibly(string? source)
    {
        var r = Resolve(source!);
        Assert.Equal(Default, r.Layer);
        Assert.Equal(LabelLayerSource.Default, r.Source);
        Assert.StartsWith(Default, r.Describe());
    }

    [Fact]
    public void MatchingIsCaseInsensitive_LikeAutoCadLayerNames()
    {
        var r = Resolve("v-surf-curb-e");
        Assert.Equal(LabelLayerSource.DerivedText, r.Source);
        Assert.Equal("V-SURF-CURB-TEXT-E", r.Layer, ignoreCase: true);
    }
}
