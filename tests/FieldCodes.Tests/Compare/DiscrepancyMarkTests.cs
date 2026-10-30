using FieldCodes.Compare;

namespace FieldCodes.Tests;

/// <summary>A counterpart within the search radius but beyond the discrepancy tolerance
/// (0.5 ft horizontally or vertically by default) is a discrepancy: ours keeps plotting,
/// the drawing gets a cloud and a note (CAD side), and the report lists it.</summary>
public sealed class DiscrepancyMarkTests
{
    [Fact]
    public void TheDefaultsAreHalfAFootAndCloudsOn()
    {
        var p = CompareProfile.CreateDefault();
        Assert.Equal(0.5, p.Tolerances.PointDiscrepancyFeet);
        Assert.Equal(0.5, p.Tolerances.ElevationFeet);
        Assert.True(p.Review.DrawClouds);
        Assert.Equal("PMX-COMP-REVIEW", p.Review.Layer);
        var problems = new List<string>();
        p.Tolerances.PointDiscrepancyFeet = 3.0;   // beyond the 2 ft search radius
        p.Validate(problems);
        Assert.Contains(problems, x => x.Contains("pointDiscrepancyFeet"));
    }

    [Fact]
    public void ASymbolBeyondHalfAFootIsADiscrepancyThatKeepsPlotting()
    {
        var s = CF.Survey(); s.Add(CF.Cogo("HH", "HH 2", 1120, 1260, 0));
        var c = CF.Consultant(); c.Add(CF.Block("C1", "E-COMM", "HH", 1120.9, 1260.3));
        var result = CF.Run(s, c);
        var r = CF.R(result, "HH");
        Assert.Equal("HANDHOLE", r.Category);
        Assert.Equal(MatchStatus.Discrepancy, r.Status);
        Assert.Equal(PlannedAction.None, r.Action);
        Assert.Contains(r.Notes, n => n.StartsWith("0.95 ft from the consultant"));
        Assert.Contains("C1", r.MatchedHandles);
        var csv = CompareReport.Render(result, "site");
        Assert.Contains("Discrepancy", csv);
        Assert.Contains("0.95 ft from the consultant", csv);

        // Just inside stays a match.
        var s2 = CF.Survey(); s2.Add(CF.Cogo("HH", "HH 2", 1120, 1260, 0));
        var c2 = CF.Consultant(); c2.Add(CF.Block("C1", "E-COMM", "HH", 1120.3, 1260.3));
        Assert.Equal(MatchStatus.Matched, CF.R(CF.Run(s2, c2), "HH").Status);
    }

    [Fact]
    public void ACategoryCanLoosenTheDiscrepancyToleranceButNeverBeyondItsSearchRadius()
    {
        var profile = CF.Profile();
        profile.Category("TREE")!.PointToleranceFeet = 5.0;
        var s = CF.Survey(); s.Add(CF.Cogo("T", "CON 18", 0, 0, 0));
        var c = CF.Consultant(); c.Add(CF.Block("CT", "E-TREE", "TREE", 3.5, 0));
        // Found within 5 ft but beyond the profile's 0.5 ft: a discrepancy...
        Assert.Equal(MatchStatus.Discrepancy, CF.R(CF.Run(s, c, profile), "T").Status);
        // ...unless the category says trees within 5 ft are the same tree.
        profile.Category("TREE")!.PointDiscrepancyFeet = 5.0;
        Assert.Equal(MatchStatus.Matched, CF.R(CF.Run(s, c, profile), "T").Status);
        profile.Category("TREE")!.PointDiscrepancyFeet = 9.0;
        Assert.Equal(5.0, profile.PointDiscrepancyTolerance(profile.Category("TREE")));
        Assert.Equal(0.5, profile.PointDiscrepancyTolerance(profile.Category("UTILITY-POLE")));
    }

    [Fact]
    public void TheirRimInAnAttributeOrLabelIsTheirElevation()
    {
        // Our manhole has a real elevation; their symbol sits at Z=0 with a rim attribute.
        var s = CF.Survey(); s.Add(CF.Cogo("MH", "SDMH 1", 1050, 1200, 120.5));
        var c = CF.Consultant();
        c.Add(CF.Block("C1", "E-STRM-MH", "STMH", 1050.3, 1200.1, null, new Dictionary<string, string> { { "RIM", "121.20" } }));
        var r = CF.R(CF.Run(s, c), "MH");
        Assert.Equal(MatchStatus.Discrepancy, r.Status);
        Assert.Equal(-0.7, r.VerticalDifference!.Value, 2);
        Assert.Contains(r.Notes, n => n.Contains("elevation differs"));

        // Within 0.5 ft vertically: a match.
        var c2 = CF.Consultant();
        c2.Add(CF.Block("C1", "E-STRM-MH", "STMH", 1050.3, 1200.1, null, new Dictionary<string, string> { { "RIM", "120.80" } }));
        Assert.Equal(MatchStatus.Matched, CF.R(CF.Run(s, c2), "MH").Status);

        // The rim in a label beside their symbol counts the same way.
        var c3 = CF.Consultant();
        c3.Add(CF.Block("C1", "E-STRM-MH", "STMH", 1050.3, 1200.1));
        c3.Add(CF.Text("T1", "E-TEXT", "RIM 121.20", 1052, 1203));
        var r3 = CF.R(CF.Run(s, c3), "MH");
        Assert.Equal(MatchStatus.Discrepancy, r3.Status);
        Assert.Equal(-0.7, r3.VerticalDifference!.Value, 2);
    }
}
