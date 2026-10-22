using FieldCodes.Compare;

namespace FieldCodes.Tests;

/// <summary>Point features, blocks and utility structures.</summary>
public sealed class PointMatchingTests
{
    [Fact]
    public void UtilityStructureBlocksMatchAcrossNamingConventions()
    {
        var s = CF.Survey(); s.Add(CF.Cogo("S1", "SDMH 101", 50, 50, 120.5));
        var c = CF.Consultant(); c.Add(CF.Block("C1", "E-STRM-MH", "STMH", 50.3, 50.2));
        var result = CF.Run(s, c);
        var r = CF.R(result, "S1");
        Assert.Equal("STORM-MH", r.Category);
        Assert.Equal(MatchStatus.Matched, r.Status);
        Assert.Equal(0.36, r.HorizontalOffset!.Value, 2);
        Assert.Null(r.VerticalDifference);          // their block sits at Z=0: no elevation to compare
        Assert.Equal(PlannedAction.Nonplot, r.Action);
        Assert.Equal("PMX-COMP-NP-UTILITY", r.NonplotLayer);
    }

    [Theory]
    [InlineData("UP 12", "E-POWER", "POLE", "UTILITY-POLE")]
    [InlineData("PP 12", "E-UTIL-OH", "UPOLE", "UTILITY-POLE")]
    [InlineData("HH 3", "E-TEL", "HANDHOLE", "HANDHOLE")]
    [InlineData("WV 7", "E-WATER", "WATER-VALVE", "WATER-VALVE")]
    [InlineData("FH 1", "E-WATER", "HYDRANT", "HYDRANT")]
    [InlineData("CB 44", "E-STRM-CB", "CATCH BASIN", "CATCH-BASIN")]
    [InlineData("CO 2", "E-SS", "SEWER-CO", "CLEANOUT")]
    public void CommonDescriptionsAndConsultantNamesMeetInOneCategory(string ourCode, string theirLayer, string theirBlock, string category)
    {
        var s = CF.Survey(); s.Add(CF.Cogo("S1", ourCode, 10, 10, 0));
        var c = CF.Consultant(); c.Add(CF.Block("C1", theirLayer, theirBlock, 10.4, 10.1));
        var r = CF.R(CF.Run(s, c), "S1");
        Assert.Equal(category, r.Category);
        Assert.Equal(MatchStatus.Matched, r.Status);
    }

    [Fact]
    public void DifferentUtilitySystemsAtTheSameLocationAreNotTheSameObject()
    {
        var s = CF.Survey(); s.Add(CF.Cogo("S1", "SSMH 5", 10, 10, 0));
        var c = CF.Consultant(); c.Add(CF.Block("C1", "E-STRM-MH", "SDMH", 10.3, 10, 0));
        var r = CF.R(CF.Run(s, c), "S1");
        Assert.Equal(MatchStatus.Missing, r.Status);
        Assert.Empty(r.MatchedIds);
        Assert.Contains(r.Notes, n => n.Contains("different system"));
    }

    [Fact]
    public void SameSystemDifferentTypeAtTheSameLocationIsAReviewQuestion()
    {
        var s = CF.Survey(); s.Add(CF.Cogo("S1", "SDMH 5", 10, 10, 0));
        var c = CF.Consultant(); c.Add(CF.Block("C1", "E-STRM-CB", "CB", 10.3, 10));
        var r = CF.R(CF.Run(s, c), "S1");
        Assert.Equal(MatchStatus.NeedsReview, r.Status);
        Assert.Contains("C1", r.MatchedIds);
        Assert.Equal(PlannedAction.None, r.Action);
    }

    [Fact]
    public void MissingHandholesStayPlotting()
    {
        var s = CF.Survey();
        s.Add(CF.Cogo("S1", "HH 12", 200, 200, 0));
        s.Add(CF.Cogo("S2", "HH 13", 300, 300, 0));
        var c = CF.Consultant(); c.Add(CF.Block("C1", "E-TEL", "HH", 305, 300));   // 5 ft away: beyond tolerance
        var result = CF.Run(s, c);
        Assert.Equal(MatchStatus.Missing, CF.R(result, "S1").Status);
        Assert.Equal(MatchStatus.Missing, CF.R(result, "S2").Status);
        Assert.All(result.Results, r => Assert.Equal(PlannedAction.None, r.Action));
        Assert.Equal(2, result.Summary().Missing);
    }

    [Fact]
    public void MissingUtilityPolesAreReportedWhileExistingOnesAreMatched()
    {
        var s = CF.Survey();
        s.Add(CF.Cogo("S1", "PP 1234", 300, 300, 0));
        s.Add(CF.Cogo("S2", "PP 1235", 400, 300, 0));
        var c = CF.Consultant(); c.Add(CF.Block("C1", "E-PWR", "POLE", 300.3, 300.2));   // 0.36 ft: within the 0.5 ft discrepancy tolerance
        var result = CF.Run(s, c);
        Assert.Equal(MatchStatus.Matched, CF.R(result, "S1").Status);
        Assert.Equal(MatchStatus.Missing, CF.R(result, "S2").Status);
    }

    [Fact]
    public void ConflictingElevationsAreADiscrepancyNotAMatch()
    {
        var s = CF.Survey(); s.Add(CF.Cogo("S1", "SDMH 101", 50, 50, 120.5));
        var c = CF.Consultant(); c.Add(CF.Block("C1", "E-STRM-MH", "STMH", 50.2, 50.1, 121.2));
        var r = CF.R(CF.Run(s, c), "S1");
        Assert.Equal(MatchStatus.Discrepancy, r.Status);
        Assert.Equal(-0.7, r.VerticalDifference!.Value, 2);
        Assert.Equal(PlannedAction.None, r.Action);

        var c2 = CF.Consultant(); c2.Add(CF.Block("C1", "E-STRM-MH", "STMH", 50.2, 50.1, 120.55));
        Assert.Equal(MatchStatus.Matched, CF.R(CF.Run(s, c2), "S1").Status);
    }

    [Fact]
    public void CategoryTolerancesAreNotUniversal()
    {
        var profile = CF.Profile();
        profile.Category("TREE")!.PointToleranceFeet = 5.0;
        profile.Category("TREE")!.PointDiscrepancyFeet = 5.0;   // trees are located loosely: anything found within 5 ft is the tree
        profile.Category("MONUMENT")!.PointToleranceFeet = 0.5;
        var s = CF.Survey();
        s.Add(CF.Cogo("T", "CON 18 . 25", 0, 0, 0));
        s.Add(CF.Cogo("M", "FMON", 100, 100, 0));
        var c = CF.Consultant();
        c.Add(CF.Block("CT", "E-TREE", "TREE", 3.5, 0));
        c.Add(CF.Block("CM", "E-MON", "MON", 101, 100));
        var result = CF.Run(s, c, profile);
        Assert.Equal(MatchStatus.Matched, CF.R(result, "T").Status);
        Assert.Equal(MatchStatus.Missing, CF.R(result, "M").Status);
    }

    [Fact]
    public void UnmappedConsultantObjectAtTheLocationGoesToReview()
    {
        var s = CF.Survey(); s.Add(CF.Cogo("S1", "HH 1", 400, 400, 0));
        var c = CF.Consultant(); c.Add(CF.Block("C1", "E-MISC", "XYZ123", 400.5, 400));
        var r = CF.R(CF.Run(s, c), "S1");
        Assert.Equal(MatchStatus.NeedsReview, r.Status);
        Assert.Contains(r.Notes, n => n.Contains("unmapped"));
    }

    [Fact]
    public void AttributeFactsTheConsultantLacksHoldTheBlockForReview()
    {
        var s = CF.Survey();
        s.Add(CF.Block("S1", "V-UTIL-STRM", "SDMH", 50, 50, null, new Dictionary<string, string> { { "RIM", "120.50" } }));
        var c = CF.Consultant(); c.Add(CF.Block("C1", "E-STRM-MH", "STMH", 50.2, 50.1));
        var r = CF.R(CF.Run(s, c), "S1");
        Assert.Equal(MatchStatus.NeedsReview, r.Status);
        Assert.Contains(r.Notes, n => n.Contains("RIM"));

        var c2 = CF.Consultant();
        c2.Add(CF.Block("C1", "E-STRM-MH", "STMH", 50.2, 50.1, null, new Dictionary<string, string> { { "RIM_EL", "120.50" } }));
        Assert.Equal(MatchStatus.Matched, CF.R(CF.Run(s, c2), "S1").Status);
    }

    [Fact]
    public void ClassificationConflictLowersConfidenceBelowAutomaticAcceptance()
    {
        // Code says storm, layer says sanitary: the code wins but nobody auto-accepts it.
        var s = CF.Survey(); s.Add(CF.Cogo("S1", "SDMH 1", 10, 10, 0, "V-UTIL-SSWR-MH"));
        var c = CF.Consultant(); c.Add(CF.Block("C1", "E-STRM-MH", "SDMH", 10.2, 10));
        var r = CF.R(CF.Run(s, c), "S1");
        Assert.Equal("STORM-MH", r.Category);
        Assert.Equal(MatchStatus.Matched, r.Status);
        Assert.Equal(MatchStatus.NeedsReview, r.EffectiveStatus);
        Assert.Contains(r.Notes, n => n.Contains("uncertain"));
    }

    [Fact]
    public void NonplotLayerDependsOnWhatTheObjectIs()
    {
        var profile = CF.Profile();
        var s = CF.Survey();
        s.Add(CF.Cogo("COGO", "PP 1", 0, 0, 0));
        s.Add(CF.Block("BLK", "V-SIGN", "SIGN", 10, 0));
        s.Add(CF.Line("LINE", "V-SURF-CURB-E", 20, 0, 30, 0));
        s.Add(CF.Text("TXT", "V-SIGN-TEXT", "STOP", 10, 1));
        var c = CF.Consultant();
        c.Add(CF.Block("C1", "E-PWR", "POLE", 0.2, 0));
        c.Add(CF.Block("C2", "E-SIGN", "SIGN", 10.2, 0));
        c.Add(CF.Line("C3", "E-CURB", 20, 0, 30, 0));
        c.Add(CF.Text("C4", "E-SIGN-TXT", "STOP", 10.5, 1.5));
        var result = CF.Run(s, c, profile);
        Assert.Equal("PMX-COMP-NP-UTILITY", CF.R(result, "COGO").NonplotLayer);
        Assert.Equal("PMX-COMP-NP-BLOCK", CF.R(result, "BLK").NonplotLayer);
        Assert.Equal("PMX-COMP-NP-LINE", CF.R(result, "LINE").NonplotLayer);
        Assert.Equal("PMX-COMP-NP-TEXT", CF.R(result, "TXT").NonplotLayer);
    }
}
