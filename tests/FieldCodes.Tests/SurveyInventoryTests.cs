using FieldCodes;

namespace FieldCodes.Tests;

/// <summary>
/// The feature catalog's ground truth, pinned as tests: every point-feature code
/// actually observed in the 553-2750-051-SV-BASE survey (765 points), classified
/// exactly as the engine handles it today.
///
/// The decisive empirical fact this file preserves: in the real survey, EVERY
/// point-feature code is bare -- the description carries nothing beyond the code --
/// except the control cross-references (XMAG/XHT/XNL AKA n) and one check shot
/// (ZK 1015). Meanings are pinned to the office code sheet, PMX Field Code rev
/// 2025-01-13; the comments name each code the way that sheet does.
/// </summary>
public sealed class SurveyInventoryTests : IClassFixture<RulesFixture>
{
    private readonly RulesFixture _fx;

    public SurveyInventoryTests(RulesFixture fx) => _fx = fx;

    // ------------------------------------------------------------ point features

    [Theory]
    // Utility symbols -- labelled with the code itself ("like the code list").
    [InlineData("PP", "V-UTIL-POWR-TEXT")]      // power pole (2)
    [InlineData("PPU", "V-UTIL-POWR-TEXT")]     // pole w/UG drop line (1)
    [InlineData("PPX", "V-UTIL-POWR-TEXT")]     // pole w/transformer (1)
    [InlineData("LT", "V-UTIL-POWR-TEXT")]      // light standard (1)
    [InlineData("PJB", "V-UTIL-POWR-TEXT")]     // power junction box (1)
    [InlineData("PVT", "V-UTIL-POWR-TEXT")]     // power vault (1)
    [InlineData("WFH", "V-UTIL-WATR-TEXT")]     // fire hydrant (2)
    [InlineData("WVL", "V-UTIL-WATR-TEXT")]     // water valve (5)
    [InlineData("GVL", "V-UTIL-NGAS-TEXT")]     // gas valve (3)
    [InlineData("CB", "V-UTIL-STRM-TEXT")]      // catch basin type 1 (13)
    [InlineData("CBS", "V-UTIL-STRM-TEXT")]     // catch basin solid lid (6)
    [InlineData("SDAD", "V-UTIL-STRM-TEXT")]    // area/yard drain (5)
    [InlineData("SSMH", "V-UTIL-STRC-TEXT")]    // sanitary sewer manhole (3)
    [InlineData("SDMH", "V-UTIL-STRC-TEXT")]    // storm drain manhole (1)
    // Transportation / site symbols
    [InlineData("SN", "V-SIGN-TEXT")]           // misc sign (1)
    [InlineData("SNNP", "V-SIGN-TEXT")]         // no parking sign (1)
    [InlineData("POST", "V-SITE-TEXT")]         // post (3)
    [InlineData("MB", "V-SITE-TEXT")]           // mailbox (2)
    public void ConfirmedSymbolCodesLabelThemselvesOnTheirFamilyTextLayer(
        string code, string layer)
    {
        var p = _fx.Parse(code);

        Assert.True(p.Recognized);
        Assert.Equal(code, p.LabelText);      // the code, verbatim as shot
        Assert.Equal(layer, p.LabelLayer);
        Assert.False(p.HasErrors);
        Assert.Empty(p.Diagnostics);
    }

    [Theory]
    // Confirmed codes with no family text layer yet -- not in symbolLabels.
    [InlineData("SSCO")]    // sewer cleanout (1)
    [InlineData("UCO")]     // generic/unknown cleanout (1)
    [InlineData("MW")]      // monitor well (1)
    // Control -- tabled by FTFCONTROL, not labelled
    [InlineData("FMON")]    // found surface monument (6)
    [InlineData("FMIC")]    // found monument in case (2)
    [InlineData("FMAG")]    // found magnail/PK nail (1)
    [InlineData("FIP")]     // found iron pipe (3)
    // Spot elevations -- elevations, not symbols; never labelled with the code
    [InlineData("CONC")]    // shot on concrete (12)
    [InlineData("BLFF")]    // finished floor (4)
    public void UnconfirmedAndControlBareCodesStillDoNothing(string code)
    {
        // As shot in the field: just the code, and no code-label standard -- the
        // Civil 3D symbol stands as-is, nothing in any report.
        var p = _fx.Parse(code);

        Assert.True(p.NoContent, code + " should be 'nothing to draw'");
        Assert.False(p.HasErrors);
        Assert.False(p.Unhandled);
        Assert.Empty(p.Diagnostics);
    }

    // ----------------------------------------------------------------- linework

    [Theory]
    [InlineData("RWC B")]              // conc wall (32) - Civil 3D figure
    [InlineData("RWB B")]              // wall bottom (24)
    [InlineData("RWT B")]              // wall top (17)
    [InlineData("RWRK B")]             // rockery (10)
    [InlineData("FCK B")]              // chain link fence (9)
    [InlineData("BLD B EC B BLD1 B")]  // building + conc edge chained (11)
    [InlineData("LNDY B")]             // CL stripe double yellow (8)
    [InlineData("LN B ASPH")]          // lane skip (2)
    [InlineData("FOG B")]              // fog line (2)
    [InlineData("HDR B")]              // handrail (2)
    [InlineData("EGG B")]              // edge grass (4)
    [InlineData("IDWPP")]              // in figure prefix database
    // ASPH: the office sheet calls it ASPHALT SPOT EL (not a line). It is still
    // handled as linework here pending the office's decision -- see the report.
    [InlineData("ASPH")]
    [InlineData("ASPH LEFT")]
    public void FencesWallsStripingAndBuildingsAreCivil3dLinework(string desc)
    {
        // Civil 3D builds these into figures via the Figure Prefix Database. FTF
        // never re-creates them; finishing them means labelling the EXISTING
        // polylines, which is a capability FTF does not have yet.
        var p = _fx.Parse(desc);

        Assert.True(p.Ignored, desc + " should be handled as linework");
        Assert.Empty(p.Diagnostics);
    }

    // ------------------------------------------------------- data-carrying codes

    [Fact]
    public void TheOnlyDataCarryingDescriptionsAreControlAndOneCheckShot()
    {
        // Control cross-references: recognized, labelled with the alias.
        Assert.Equal("control", _fx.Parse("XMAG AKA 1013").RuleId);
        Assert.Equal("control", _fx.Parse("XHT AKA 1010 & 1014").RuleId);
        Assert.Equal("control", _fx.Parse("XNL AKA 2004").RuleId);

        // Bare control shots do nothing, like every other bare code.
        Assert.True(_fx.Parse("XMAG").NoContent);
        Assert.True(_fx.Parse("XHT").NoContent);

        // ZK 1015 (point 10450) was the last unknown until the office sheet named
        // it: ZK is a CHECK SHOT on point 1015. A check shot is verification, not
        // a feature, so it is intentionally never drawn -- and the unknown-codes
        // report for this survey is now empty.
        var zk = _fx.Parse("ZK 1015");
        Assert.True(zk.Ignored);
        Assert.False(zk.Unhandled);
    }

    // -------------------------------------------------- inert assumed-grammar rules

    [Theory]
    [InlineData("UVLT")]    // generic vault - code never observed
    [InlineData("GYA")]     // guy anchor    - code never observed
    public void RulesForUnobservedCodesAreInertOnBareInput(string code)
    {
        // These rules use codes from the office sheet that this survey never shot.
        // Bare input does nothing, exactly like every other unlisted bare code.
        var p = _fx.Parse(code);
        Assert.True(p.NoContent);
        Assert.Empty(p.Diagnostics);
    }
}
