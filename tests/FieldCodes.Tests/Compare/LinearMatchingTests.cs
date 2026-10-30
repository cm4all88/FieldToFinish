using FieldCodes.Compare;

namespace FieldCodes.Tests;

/// <summary>Linework is compared as physical features, by coverage along our line.</summary>
public sealed class LinearMatchingTests
{
    private const string Curb = "V-SURF-CURB-E";

    [Fact]
    public void IdenticalLinesMatchAndGoNonplot()
    {
        var s = CF.Survey(); s.Add(CF.Line("S1", Curb, 0, 0, 100, 0));
        var c = CF.Consultant(); c.Add(CF.Line("C1", "E-CURB", 0, 0, 100, 0));
        var result = CF.Run(s, c);
        var r = CF.R(result, "S1");
        Assert.Equal(MatchStatus.Matched, r.Status);
        Assert.Equal(PlannedAction.Nonplot, r.Action);
        Assert.Equal("PMX-COMP-NP-LINE", r.NonplotLayer);
        Assert.Contains("C1", r.MatchedIds);
        Assert.True(r.Confidence >= 0.75, "confident enough to accept automatically");
        Assert.Equal(100, r.CoveredLength, 1);
    }

    [Fact]
    public void ReversedDirectionStillMatches()
    {
        var s = CF.Survey(); s.Add(CF.Line("S1", Curb, 0, 0, 100, 0));
        var c = CF.Consultant(); c.Add(CF.Line("C1", "E-CURB", 100, 0, 0, 0));
        Assert.Equal(MatchStatus.Matched, CF.R(CF.Run(s, c), "S1").Status);
    }

    [Fact]
    public void OneLongConsultantPolylineCoversSixShortSurveyLines()
    {
        // Example A: their 400 ft curb; our six lines covering 300 ft of it.
        var s = CF.Survey();
        for (var i = 0; i < 6; i++) s.Add(CF.Line("S" + i, Curb, i * 50, 0, (i + 1) * 50, 0));
        var c = CF.Consultant(); c.Add(CF.Line("C1", "E-CURB", 0, 0, 400, 0));
        var result = CF.Run(s, c);
        for (var i = 0; i < 6; i++)
        {
            var r = CF.R(result, "S" + i);
            Assert.Equal(MatchStatus.Matched, r.Status);
            Assert.Equal(PlannedAction.Nonplot, r.Action);
        }
        Assert.Equal(6, result.Summary().Matched);
    }

    [Fact]
    public void ManyConsultantSegmentsCoverOneSurveyPolyline()
    {
        var s = CF.Survey(); s.Add(CF.Line("S1", Curb, 0, 0, 300, 0));
        var c = CF.Consultant();
        c.Add(CF.Line("C1", "E-CURB", 0, 0, 100, 0));
        c.Add(CF.Line("C2", "E-CURB", 100, 0, 200, 0));
        c.Add(CF.Line("C3", "E-CURB", 200, 0, 300, 0));
        var r = CF.R(CF.Run(s, c), "S1");
        Assert.Equal(MatchStatus.Matched, r.Status);
        Assert.Equal(3, r.MatchedIds.Count);
    }

    [Fact]
    public void PartialOverlapKeepsTheUncoveredPortionPlotting()
    {
        // Example B: their 200 ft of edge of pavement; our 350 ft.
        var s = CF.Survey(); s.Add(CF.Line("S1", "V-SURF-ASPH-E", 0, 0, 350, 0));
        var c = CF.Consultant(); c.Add(CF.Line("C1", "E-EOP", 0, 0, 200, 0));
        var result = CF.Run(s, c);
        var r = CF.R(result, "S1");
        Assert.Equal(MatchStatus.Partial, r.Status);
        Assert.Equal(PlannedAction.SplitNonplot, r.Action);
        Assert.InRange(r.CoveredLength, 199, 202);
        Assert.InRange(r.UncoveredLength, 148, 151);
        var uncovered = Assert.Single(r.Intervals.Where(i => i.Kind == IntervalKind.Uncovered));
        Assert.InRange(uncovered.Start, 199, 202);
        Assert.Equal(350, uncovered.End, 1);

        var plan = ChangePlanner.Build(result, CF.Profile());
        var change = Assert.Single(plan.Changes);
        Assert.Equal(PlannedAction.SplitNonplot, change.Action);
        var keep = Assert.Single(change.KeepIntervals);
        Assert.InRange(keep.Start, 199, 202);
        Assert.Equal(350, keep.End, 1);
    }

    [Fact]
    public void FragmentedSurveyWithSmallGapsMatchesAContinuousFence()
    {
        // Example C: one continuous fence; our segments with 2 ft gaps between shots.
        var s = CF.Survey();
        for (var i = 0; i < 5; i++) s.Add(CF.Line("S" + i, "V-SURF-FENC-E", i * 100, 0, i * 100 + 98, 0));
        var c = CF.Consultant(); c.Add(CF.Line("C1", "E-FENCE", 0, 0, 500, 0));
        var result = CF.Run(s, c);
        Assert.All(result.Results, r => Assert.Equal(MatchStatus.Matched, r.Status));
    }

    [Fact]
    public void SmallGapInConsultantDraftingIsBridgedButALargeOneIsNot()
    {
        var s = CF.Survey(); s.Add(CF.Line("S1", Curb, 0, 0, 100, 0));
        var c = CF.Consultant();
        c.Add(CF.Line("C1", "E-CURB", 0, 0, 48, 0));
        c.Add(CF.Line("C2", "E-CURB", 52, 0, 100, 0));
        Assert.Equal(MatchStatus.Matched, CF.R(CF.Run(s, c), "S1").Status);

        var c2 = CF.Consultant();
        c2.Add(CF.Line("C1", "E-CURB", 0, 0, 40, 0));
        c2.Add(CF.Line("C2", "E-CURB", 60, 0, 100, 0));
        var r = CF.R(CF.Run(s, c2), "S1");
        Assert.Equal(MatchStatus.Partial, r.Status);
        Assert.InRange(r.UncoveredLength, 17, 20);   // a sample within tolerance of a line's end counts as covered
    }

    [Fact]
    public void NoSliverIsCreatedForATinyUncoveredStub()
    {
        var s = CF.Survey(); s.Add(CF.Line("S1", Curb, 0, 0, 102, 0));
        var c = CF.Consultant(); c.Add(CF.Line("C1", "E-CURB", 0, 0, 100, 0));
        var r = CF.R(CF.Run(s, c), "S1");
        Assert.Equal(MatchStatus.Matched, r.Status);
        Assert.Equal(PlannedAction.Nonplot, r.Action);
        Assert.DoesNotContain(r.Intervals, i => i.Kind == IntervalKind.Uncovered);
    }

    [Fact]
    public void ParallelButDifferentFeaturesAreNotMatched()
    {
        // Example D: our curb beside their gutter.
        var s = CF.Survey(); s.Add(CF.Line("S1", Curb, 0, 0, 100, 0));
        var c = CF.Consultant(); c.Add(CF.Line("C1", "E-GUTTER", 0, 0.5, 100, 0.5));
        var r = CF.R(CF.Run(s, c), "S1");
        Assert.Equal(MatchStatus.Missing, r.Status);
        Assert.Empty(r.MatchedIds);
    }

    [Fact]
    public void SameCategoryBeyondOffsetToleranceIsNotMatched()
    {
        var s = CF.Survey(); s.Add(CF.Line("S1", Curb, 0, 0, 100, 0));
        var c = CF.Consultant(); c.Add(CF.Line("C1", "E-CURB", 0, 3, 100, 3));
        Assert.Equal(MatchStatus.Missing, CF.R(CF.Run(s, c), "S1").Status);
    }

    [Fact]
    public void OffsetCloseToToleranceIsSentToReviewNotAccepted()
    {
        var s = CF.Survey(); s.Add(CF.Line("S1", Curb, 0, 0, 100, 0));
        var c = CF.Consultant(); c.Add(CF.Line("C1", "E-CURB", 0, 0.8, 100, 0.8));
        var r = CF.R(CF.Run(s, c), "S1");
        Assert.Equal(MatchStatus.Matched, r.Status);
        Assert.True(r.Confidence < 0.75);
        Assert.Equal(MatchStatus.NeedsReview, r.EffectiveStatus);
        Assert.Equal(PlannedAction.None, r.Action);
        Assert.Contains(r.Notes, n => n.Contains("parallel"));
    }

    [Fact]
    public void CrossingLinesAtAnAngleDoNotCount()
    {
        var s = CF.Survey(); s.Add(CF.Line("S1", Curb, 0, 0, 100, 0));
        var c = CF.Consultant(); c.Add(CF.Line("C1", "E-CURB", 50, -50, 50, 50));
        Assert.Equal(MatchStatus.Missing, CF.R(CF.Run(s, c), "S1").Status);
    }

    [Fact]
    public void CurvedLineworkMatchesAcrossDifferentChordings()
    {
        var fine = Geometry2.ChordBulge(new CPoint(0, 0), new CPoint(100, 0), 0.3, 0.02);
        var coarse = Geometry2.ChordBulge(new CPoint(0, 0), new CPoint(100, 0), 0.3, 0.2);
        Assert.True(fine.Count > coarse.Count);
        var s = CF.Survey();
        var sf = new CompareFeature { Handle = "S1", Kind = FeatureKind.Line, EntityType = "LWPOLYLINE", Layer = Curb, Vertices = fine, HasCurves = true };
        s.Add(sf);
        var c = CF.Consultant();
        c.Add(new CompareFeature { Handle = "C1", Kind = FeatureKind.Line, EntityType = "LWPOLYLINE", Layer = "E-CURB", Vertices = coarse, HasCurves = true });
        var r = CF.R(CF.Run(s, c), "S1");
        Assert.Equal(MatchStatus.Matched, r.Status);

        // The straight chord of the same arc is 15 ft off at mid-span: not the same curb.
        var c2 = CF.Consultant(); c2.Add(CF.Line("C1", "E-CURB", 0, 0, 100, 0));
        sf.InvalidateCache();
        Assert.NotEqual(MatchStatus.Matched, CF.R(CF.Run(s, c2), "S1").Status);
    }

    [Fact]
    public void BranchesAndIntersectionsMatchOnlyTheirOwnSegment()
    {
        // Example F: our T-shaped utility: the stem is theirs, the branch is not.
        var s = CF.Survey();
        s.Add(CF.Line("STEM", "V-UTIL-STRM-E", 0, 0, 100, 0));
        s.Add(CF.Line("BRANCH", "V-UTIL-STRM-E", 50, 0, 50, 80));
        var c = CF.Consultant(); c.Add(CF.Line("C1", "E-STRM", 0, 0, 100, 0));
        var result = CF.Run(s, c);
        Assert.Equal(MatchStatus.Matched, CF.R(result, "STEM").Status);
        var branch = CF.R(result, "BRANCH");
        Assert.Equal(MatchStatus.Missing, branch.Status);
        Assert.Equal("PMX-COMP-NP-UTILITY", ChangePlanner.NonplotLayerFor(CF.R(result, "STEM"), CF.Profile().Category("STORM-PIPE"), CF.Profile()));
    }

    [Fact]
    public void SurveyContinuingBeyondTheLimitsIsNotCalledMissing()
    {
        // Example E: their drawing stops at a matchline; ours continues.
        var profile = CF.Profile();
        profile.Boundary.SetPolygon(new[] { new CPoint(-10, -10), new CPoint(200.5, -10), new CPoint(200.5, 10), new CPoint(-10, 10) });
        var s = CF.Survey(); s.Add(CF.Line("S1", Curb, 0, 0, 300, 0));
        var c = CF.Consultant(); c.Add(CF.Line("C1", "E-CURB", 0, 0, 200, 0));
        var result = CF.Run(s, c, profile);
        var r = CF.R(result, "S1");
        Assert.Equal(MatchStatus.Matched, r.Status);
        Assert.Equal(0, r.UncoveredLength, 0);
        Assert.InRange(r.OutsideLength, 98, 101);
        Assert.Equal(MatchStatus.NeedsReview, r.EffectiveStatus);     // the default: a person decides
        Assert.Equal(PlannedAction.None, r.Action);

        profile.Boundary.OutsideLimitsAction = OutsideLimitsAction.KeepPlotting;
        ChangePlanner.Build(result, profile);
        Assert.Equal(PlannedAction.SplitNonplot, r.Action);
        Assert.Equal(MatchStatus.Matched, r.EffectiveStatus);

        profile.Boundary.OutsideLimitsAction = OutsideLimitsAction.Nonplot;
        ChangePlanner.Build(result, profile);
        Assert.Equal(PlannedAction.Nonplot, r.Action);
    }

    [Fact]
    public void ConsultantExtentActsAsLimitsWhenNoBoundaryIsSet()
    {
        var profile = CompareProfile.CreateDefault();   // extent rule on, 10 ft margin
        var s = CF.Survey(); s.Add(CF.Line("S1", Curb, 0, 0, 300, 0));
        var c = CF.Consultant(); c.Add(CF.Line("C1", "E-CURB", 0, 0, 200, 0));
        var r = CF.R(CF.Run(s, c, profile), "S1");
        // The 10 ft inside the margin is theirs to have had; the 90 ft beyond is not judged.
        Assert.Equal(MatchStatus.Partial, r.Status);
        Assert.InRange(r.UncoveredLength, 8, 12);
        Assert.InRange(r.OutsideLength, 88, 92);
        Assert.True(r.HasOutsideLimits);
        Assert.Equal(MatchStatus.NeedsReview, r.EffectiveStatus);
    }

    [Fact]
    public void FeatureEntirelyOutsideTheLimitsIsExcluded()
    {
        var profile = CF.Profile();
        profile.Boundary.SetPolygon(new[] { new CPoint(0, 0), new CPoint(100, 0), new CPoint(100, 100), new CPoint(0, 100) });
        var s = CF.Survey(); s.Add(CF.Line("S1", Curb, 500, 500, 600, 500));
        var c = CF.Consultant(); c.Add(CF.Line("C1", "E-CURB", 0, 50, 100, 50));
        Assert.Equal(MatchStatus.Excluded, CF.R(CF.Run(s, c, profile), "S1").Status);
    }

    [Fact]
    public void UnmappedConsultantLineworkAlongOursGoesToReviewInsteadOfMissing()
    {
        var s = CF.Survey(); s.Add(CF.Line("S1", Curb, 0, 0, 100, 0));
        var c = CF.Consultant(); c.Add(CF.Line("C1", "XX-SOMETHING", 0, 0.2, 100, 0.2));
        var r = CF.R(CF.Run(s, c), "S1");
        Assert.Equal(MatchStatus.NeedsReview, r.Status);
        Assert.Contains(r.Notes, n => n.Contains("XX-SOMETHING"));
    }

    [Fact]
    public void ThreeDimensionalLineworkWithDifferentElevationsIsADiscrepancy()
    {
        var s = CF.Survey(); s.Add(CF.Line3("S1", Curb, 0, 0, 100.0, 100, 0, 100.0));
        var c = CF.Consultant(); c.Add(CF.Line3("C1", "E-CURB", 0, 0, 101.0, 100, 0, 101.0));
        var r = CF.R(CF.Run(s, c), "S1");
        Assert.Equal(MatchStatus.Discrepancy, r.Status);
        Assert.Equal(-1.0, r.VerticalDifference!.Value, 2);

        // A consultant line at Z=0 carries no elevation: no discrepancy.
        var c2 = CF.Consultant(); c2.Add(CF.Line("C1", "E-CURB", 0, 0, 100, 0));
        Assert.Equal(MatchStatus.Matched, CF.R(CF.Run(s, c2), "S1").Status);
    }

    [Fact]
    public void SurveyFigureIsIdentifiedByItsFigureName()
    {
        var s = CF.Survey(); s.Add(CF.Figure("S1", "CU3", "SOMEOTHERLAYER", 0, 0, 100, 0));
        var c = CF.Consultant(); c.Add(CF.Line("C1", "E-CURB", 0, 0, 100, 0));
        var r = CF.R(CF.Run(s, c), "S1");
        Assert.Equal("CURB", r.Category);
        Assert.Equal(MatchStatus.Matched, r.Status);
    }
}
