using FieldCodes;
using FieldCodes.Linework;

namespace FieldCodes.Tests;

/// <summary>
/// TrimbleName XData: the one piece of source identity the TBC export was found to
/// preserve (FTFLINEMETA on 553-2750-051-SV-BASE, 49 of 398 line entities). It
/// carries the TBC feature name -- "Edge of Pavement", "Top of Slope" -- not the raw
/// field coding, so it is matched exactly against a configured code, name or label
/// and NEVER fuzzily. Precedence: figure name, then TrimbleName, then layer.
///
/// Every value in these tests is real: it appeared verbatim in the Gig Harbor
/// metadata inspection -- except "ASPH L", the hypothetical future export that
/// preserves raw coding, which must fire the directional path automatically.
/// </summary>
public sealed class TrimbleNameTests : IClassFixture<RulesFixture>
{
    private readonly LineworkCatalog _catalog;

    public TrimbleNameTests(RulesFixture fx)
        => _catalog = new LineworkCatalog(fx.Config.LineFeatures);

    // ------------------------------------------------------------ exact matches

    [Fact]
    public void ATbcFeatureNameMatchingAConfiguredLabelIdentifiesTheFeature()
    {
        // "Edge of Pavement" on a V-SURF-ASPH-E Line, exactly as found in the
        // drawing. The label match is case-insensitive but otherwise exact.
        var row = _catalog.Identify("Line", null, "V-SURF-ASPH-E", 120.0,
                                    "Edge of Pavement");

        Assert.Equal(LineIdentitySource.TrimbleName, row.Source);
        Assert.Equal("Trimble name", row.SourceText);
        Assert.Equal("Edge of Pavement", row.TrimbleName);
        Assert.Equal("EDGE OF PAVEMENT", LineworkCatalog.UnanimousLabel(
            _catalog.Candidates(null, "Edge of Pavement", "V-SURF-ASPH-E")));
    }

    [Fact]
    public void BuildingMatchesBothBuildingCodes_WhichAgreeOnOneLabel()
    {
        // "Building" (as found on a V-SURF-BLDG-E Line) matches BLD by name and
        // BLD/BLC by label; both label "BUILDING", so the ambiguity is harmless.
        var row = _catalog.Identify("Line", null, "V-SURF-BLDG-E", 80.0, "Building");

        Assert.Equal(LineIdentitySource.TrimbleName, row.Source);
        Assert.Equal("BUILDING", LineworkCatalog.UnanimousLabel(
            _catalog.Candidates(null, "Building", "V-SURF-BLDG-E")));
    }

    // ----------------------------------------------------------- honest misses

    [Theory]
    [InlineData("Edge of Conc")]      // abbreviation of "Concrete Edge" - no match
    [InlineData("FL ASPH CURB")]      // TBC flowline name - not configured
    [InlineData("Lane Skip")]         // TBC name differs from "Skip Stripe"
    public void AnUnrecognizedNameFallsThroughToTheLayer_NeverAFuzzyMatch(string name)
    {
        Assert.Empty(_catalog.FindByTrimbleName(name));

        // With a configured layer underneath, the layer still identifies it.
        var row = _catalog.Identify("Line", null, "V-SURF-CONC-E", 50.0, name);
        Assert.Equal(LineIdentitySource.Layer, row.Source);
    }

    [Fact]
    public void AnUnidentifiedLineStillCarriesItsSurvivingNameForReview()
    {
        // "Top of Slope" on a TIN breakline: no feature, no configured layer -- but
        // the review can now say what it is instead of showing a bare polyline.
        var row = _catalog.Identify("Polyline", null, "V-TINN-BRKL-E", 200.0,
                                    "Top of Slope");

        Assert.Equal(LineIdentitySource.None, row.Source);
        Assert.Equal("Top of Slope", row.TrimbleName);
    }

    // ------------------------------------------------- the directional recovery

    [Fact]
    public void RawCodingSurvivingInTrimbleNameFiresTheDirectionalPathAutomatically()
    {
        // The upstream fix this is built for: an export that preserves "ASPH L"
        // needs no FTF change at all -- code identity AND the side both recover.
        var row = _catalog.Identify("Polyline", null, null, 100.0, "ASPH L");

        Assert.Equal(LineIdentitySource.TrimbleName, row.Source);
        Assert.Equal("ASPH", row.Codes);
        Assert.Equal(LineLabelSide.Left, row.SourceSide);
    }

    [Fact]
    public void ATbcFeatureNameNeverYieldsASide()
    {
        // "Toe of Slope" must not read its own words as directions; and even a
        // matched name like "Edge of Pavement" carries no side.
        Assert.Null(_catalog.Identify("Line", null, null, 10.0, "Toe of Slope").SourceSide);
        Assert.Null(_catalog.Identify("Line", null, "V-SURF-ASPH-E", 10.0,
                                      "Edge of Pavement").SourceSide);
    }

    // -------------------------------------------------------------- precedence

    [Fact]
    public void AFigureNameBeatsTrimbleNameBeatsLayer()
    {
        // Figure name wins outright.
        var figure = _catalog.Identify("Survey Figure", "RWC1", "V-SURF-ASPH-E", 10.0,
                                       "Edge of Pavement");
        Assert.Equal(LineIdentitySource.FigureName, figure.Source);
        Assert.Equal("RWC", figure.Codes);

        // TrimbleName wins over a layer that says something else.
        var trimble = _catalog.Identify("Line", null, "V-SURF-CONC-E", 10.0,
                                        "Edge of Pavement");
        Assert.Equal(LineIdentitySource.TrimbleName, trimble.Source);
        Assert.Equal("EP", trimble.Codes);
    }

    [Fact]
    public void AFigureNameSideBeatsATrimbleNameSide()
    {
        var row = _catalog.Identify("Survey Figure", "ASPH RIGHT", null, 10.0, "ASPH L");
        Assert.Equal(LineLabelSide.Right, row.SourceSide);
    }
}
