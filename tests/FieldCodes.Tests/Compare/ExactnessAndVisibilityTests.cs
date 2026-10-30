using FieldCodes;
using FieldCodes.Compare;

namespace FieldCodes.Tests;

/// <summary>Sampling exactness, small differences, clipping, hidden and unreadable
/// consultant objects, and QC on real plot state.</summary>
public sealed class ExactnessAndVisibilityTests
{
    private const string Curb = "V-SURF-CURB-E";

    [Fact]
    public void CoverageBoundariesAreRefinedToATenthOfAFoot()
    {
        var s = CF.Survey(); s.Add(CF.Line("S1", Curb, 0, 0, 350, 0));
        var c = CF.Consultant(); c.Add(CF.Line("C1", "E-CURB", 0, 0, 200.37, 0));
        var r = CF.R(CF.Run(s, c), "S1");
        var uncovered = Assert.Single(r.Intervals.Where(i => i.Kind == IntervalKind.Uncovered));
        // Within one offset tolerance of the consultant's end (a sample within tolerance of
        // the end point counts as covered), and refined far finer than the 1 ft step.
        Assert.InRange(uncovered.Start, 200.37, 201.5);
        Assert.Equal(uncovered.Start, uncovered.StartPoint.X, 6);
        Assert.Equal(350, uncovered.EndPoint.X, 6);
        Assert.Equal(0, uncovered.StartPoint.Y, 6);
    }

    [Fact]
    public void AShortJogAwayFromTheirLineKeepsPlottingHoweverShort()
    {
        // Our curb matches theirs except for a 2 ft return that swings 4 ft away.
        var s = CF.Survey(); s.Add(CF.Line("S1", Curb, 0, 0, 50, 0, 50, 4, 52, 4, 52, 0, 100, 0));
        var c = CF.Consultant(); c.Add(CF.Line("C1", "E-CURB", 0, 0, 100, 0));
        var r = CF.R(CF.Run(s, c), "S1");
        Assert.Equal(MatchStatus.Partial, r.Status);
        var kept = r.Intervals.Where(i => i.Kind == IntervalKind.Uncovered).ToList();
        Assert.NotEmpty(kept);
        Assert.True(kept.Sum(i => i.Length) >= 6, "the return (4 + 2 + 4 ft) keeps plotting");
        var plan = ChangePlanner.Build(CF.Run(s, c), CF.Profile());
        Assert.Equal(PlannedAction.SplitNonplot, plan.Changes.Single().Action);
    }

    [Fact]
    public void AShortEndStubThatTurnsAwayKeepsPlottingANearOneIsAbsorbed()
    {
        // A 2.5 ft stub turning away at the end of a matched curb is a real difference
        // (farther than two tolerances): shorter than the sliver minimum, still kept.
        var s = CF.Survey(); s.Add(CF.Line("S1", Curb, 0, 0, 100, 0, 100, 2.5));
        var c = CF.Consultant(); c.Add(CF.Line("C1", "E-CURB", 0, 0, 100, 0));
        var r = CF.R(CF.Run(s, c), "S1");
        Assert.Equal(MatchStatus.Partial, r.Status);
        Assert.InRange(r.UncoveredLength, 1.0, 2.6);
        Assert.Contains(r.Notes, n => n.Contains("short stretch"));

        // A 1.5 ft stub stays within the nearness distance: noise, absorbed.
        var s2 = CF.Survey(); s2.Add(CF.Line("S1", Curb, 0, 0, 100, 0, 100, 1.5));
        Assert.Equal(MatchStatus.Matched, CF.R(CF.Run(s2, c), "S1").Status);
    }

    [Fact]
    public void ANearMissSliverIsStillAbsorbed()
    {
        // The same curb wobbles 1.3 ft off theirs over a couple of feet: noise, not a feature.
        var s = CF.Survey(); s.Add(CF.Line("S1", Curb, 0, 0, 50, 0, 50.7, 1.3, 51.4, 0, 100, 0));
        var c = CF.Consultant(); c.Add(CF.Line("C1", "E-CURB", 0, 0, 100, 0));
        var r = CF.R(CF.Run(s, c), "S1");
        Assert.Equal(MatchStatus.Matched, r.Status);
        Assert.Equal(PlannedAction.Nonplot, r.Action);

        // The same wobble spread over four feet is no longer a sliver: it keeps plotting.
        var s2 = CF.Survey(); s2.Add(CF.Line("S1", Curb, 0, 0, 50, 0, 52, 1.3, 54, 0, 100, 0));
        Assert.Equal(MatchStatus.Partial, CF.R(CF.Run(s2, c), "S1").Status);
    }

    [Fact]
    public void ADraftingGapIsBridgedOnlyWhereOurLineRunsStraightAcrossIt()
    {
        var s = CF.Survey(); s.Add(CF.Line("S1", Curb, 0, 0, 100, 0));
        var c = CF.Consultant();
        c.Add(CF.Line("C1", "E-CURB", 0, 0, 48, 0));
        c.Add(CF.Line("C2", "E-CURB", 52, 0, 100, 0));
        Assert.Equal(MatchStatus.Matched, CF.R(CF.Run(s, c), "S1").Status);

        // Same gap in their drafting, but our line steps 3 ft across it: a real difference.
        var s2 = CF.Survey(); s2.Add(CF.Line("S1", Curb, 0, 0, 48, 0, 48, 3, 52, 3, 52, 0, 100, 0));
        var c2 = CF.Consultant();
        c2.Add(CF.Line("C1", "E-CURB", 0, 0, 48, 0));
        c2.Add(CF.Line("C2", "E-CURB", 52, 0, 100, 0));
        var r = CF.R(CF.Run(s2, c2), "S1");
        Assert.Equal(MatchStatus.Partial, r.Status);
        Assert.True(r.UncoveredLength >= 6);
    }

    [Fact]
    public void VeryShortLinesAreSampledAndDecided()
    {
        var s = CF.Survey();
        s.Add(CF.Line("SHORT", Curb, 10, 0, 11.5, 0));          // 1.5 ft, on their line
        s.Add(CF.Line("STUB", Curb, 10, 5, 11.5, 5));           // 1.5 ft, 5 ft away
        var c = CF.Consultant(); c.Add(CF.Line("C1", "E-CURB", 0, 0, 100, 0));
        var result = CF.Run(s, c);
        Assert.Equal(MatchStatus.Matched, CF.R(result, "SHORT").Status);
        Assert.Equal(MatchStatus.Missing, CF.R(result, "STUB").Status);
    }

    [Fact]
    public void TightReturnsMatchAcrossIndependentChordings()
    {
        // A 3 ft radius curb return, chorded by two different tolerances, still matches:
        // the chord angle is capped so directions agree.
        var fine = Geometry2.ChordBulge(new CPoint(0, 0), new CPoint(6, 0), 1.0, 0.01);
        var coarse = Geometry2.ChordBulge(new CPoint(0, 0), new CPoint(6, 0), 1.0, 0.2);
        Assert.True(coarse.Count >= 19, "cap of 10 degrees per chord on a half circle: " + coarse.Count);
        var s = CF.Survey();
        s.Add(new CompareFeature { Handle = "S1", Kind = FeatureKind.Line, EntityType = "LWPOLYLINE", Layer = Curb, Vertices = fine, HasCurves = true });
        var c = CF.Consultant();
        c.Add(new CompareFeature { Handle = "C1", Kind = FeatureKind.Line, EntityType = "LWPOLYLINE", Layer = "E-CURB", Vertices = coarse, HasCurves = true });
        Assert.Equal(MatchStatus.Matched, CF.R(CF.Run(s, c), "S1").Status);
    }

    [Fact]
    public void ACrossingLineOfTheSameCategoryDoesNotCoverTheCrossing()
    {
        var s = CF.Survey(); s.Add(CF.Line("S1", Curb, 0, 0, 100, 0));
        var c = CF.Consultant();
        c.Add(CF.Line("C1", "E-CURB", 0, 0, 40, 0));
        c.Add(CF.Line("C2", "E-CURB", 50, -50, 50, 50));          // crosses at 50
        var r = CF.R(CF.Run(s, c), "S1");
        Assert.Equal(MatchStatus.Partial, r.Status);
        Assert.InRange(r.UncoveredLength, 58, 60.5);
        Assert.DoesNotContain("C2", r.MatchedIds);
    }

    // ----------------------------------------------------------------- clipping

    [Fact]
    public void ClipPolylineKeepsOnlyTheInsidePieces()
    {
        var square = new[] { new CPoint(0, 0), new CPoint(10, 0), new CPoint(10, 10), new CPoint(0, 10) };
        var line = new[] { new CPoint(-5, 5), new CPoint(15, 5) };
        var inside = Geometry2.ClipPolylineToPolygon(line, square, false);
        var piece = Assert.Single(inside);
        Assert.Equal(0, piece[0].X, 9);
        Assert.Equal(10, piece[^1].X, 9);

        var outside = Geometry2.ClipPolylineToPolygon(line, square, true);
        Assert.Equal(2, outside.Count);
        Assert.Equal(-5, outside[0][0].X, 9);
        Assert.Equal(0, outside[0][^1].X, 9);
        Assert.Equal(10, outside[1][0].X, 9);

        // A line crossing twice gives two inside pieces.
        var zig = new[] { new CPoint(-5, 2), new CPoint(5, 2), new CPoint(5, 12), new CPoint(5, 8), new CPoint(15, 8) };
        var pieces = Geometry2.ClipPolylineToPolygon(zig, square, false);
        Assert.Equal(2, pieces.Count);
        Assert.Empty(Geometry2.ClipPolylineToPolygon(new[] { new CPoint(20, 20), new CPoint(30, 30) }, square, false));
    }

    [Fact]
    public void ClippedConsultantGeometryCannotCoverOurSurvey()
    {
        // The reader would have clipped the consultant curb to x in [0,50]; the half it
        // hides must not make our 100 ft curb "matched".
        var clip = new[] { new CPoint(-10, -10), new CPoint(50, -10), new CPoint(50, 10), new CPoint(-10, 10) };
        var theirs = Geometry2.ClipPolylineToPolygon(new[] { new CPoint(0, 0), new CPoint(100, 0) }, clip, false);
        var c = CF.Consultant();
        c.Add(new CompareFeature { Handle = "C1", Kind = FeatureKind.Line, EntityType = "LINE", Layer = "E-CURB", Vertices = theirs.Single() });
        c.CountHidden("outside xref clip");
        var s = CF.Survey(); s.Add(CF.Line("S1", Curb, 0, 0, 100, 0));
        var result = CF.Run(s, c);
        var r = CF.R(result, "S1");
        Assert.Equal(MatchStatus.Partial, r.Status);
        Assert.InRange(r.UncoveredLength, 48, 50.5);
        Assert.Contains(result.Issues, i => i.Code == "HIDDEN" && i.Message.Contains("outside xref clip"));
    }

    // ---------------------------------------------------------- opaque labels

    [Fact]
    public void AnUnreadableConsultantLabelSendsOurAnnotationToReview()
    {
        var s = CF.Survey(); s.Add(CF.Cogo("MH", "SDMH 101", 50, 50, 0));
        s.Add(CF.Text("RIM", "V-UTIL-STRM-TEXT", "RIM 120.50", 52, 52));
        var c = CF.Consultant(); c.Add(CF.Block("CMH", "E-STRM-MH", "STMH", 50.3, 50.1));
        var label = CF.Text("CL", "E-STRM-LABEL", string.Empty, 53, 53);
        label.EntityType = "STRUCTURELABEL";
        label.Opaque = true;
        c.Add(label);
        var result = CF.Run(s, c);
        Assert.Equal(MatchStatus.Matched, CF.R(result, "MH").Status);
        var rim = CF.R(result, "RIM");
        Assert.Equal(MatchStatus.NeedsReview, rim.Status);
        Assert.Equal(PlannedAction.None, rim.Action);
        Assert.Contains(rim.Notes, n => n.Contains("could not be read"));
    }

    [Fact]
    public void AnUnreadableSurveyLabelIsNotEvaluatedAndKeepsPlotting()
    {
        var s = CF.Survey();
        var label = CF.Text("L1", "V-UTIL-STRM-TEXT", string.Empty, 10, 10);
        label.EntityType = "PIPELABEL";
        label.ReadProblem = "Civil 3D label text is style-driven and could not be read";
        s.Add(label);
        var c = CF.Consultant();
        var r = CF.R(CF.Run(s, c), "L1");
        Assert.Equal(MatchStatus.NotEvaluated, r.Status);
        Assert.Equal(PlannedAction.None, r.Action);
    }

    // ------------------------------------------------------- QC on plot state

    [Fact]
    public void QcUsesTheRealPlotStateNotJustTheLayer()
    {
        var s = CF.Survey();
        s.Add(CF.Cogo("MH", "SDMH 1", 50, 30, 100.0));
        s.Add(CF.Block("BLK", "V-UTIL-POWR", "POLE", 0, 0));
        s.Add(CF.Line("MISSING", Curb, 0, 20, 100, 20));
        var c = CF.Consultant();
        c.Add(CF.Block("C3", "E-STRM-MH", "STMH", 50.2, 30.1));
        c.Add(CF.Block("C4", "E-PWR", "POLE", 0.2, 0.1));
        var profile = CF.Profile();
        var result = CF.Run(s, c, profile);
        var plan = ChangePlanner.Build(result, profile);
        Assert.Equal(2, plan.Changes.Count);

        var input = new QcInput { Profile = profile, Result = result, Plan = plan, DrawingPath = "/tmp/site.dwg", Applied = new AppliedChangeSet() };
        foreach (var name in profile.NonplotLayers.All) input.Layers[name] = new LayerState { Name = name, Exists = true, Plottable = false };
        foreach (var change in plan.Changes)
            input.Applied.Changes.Add(new AppliedChange { Handle = change.Handle, OriginalLayer = change.FromLayer, NonplotLayer = change.ToLayer, Action = change.Action, Verified = true });

        // Both duplicates sit on the nonplot layer, but the resolver found the point's
        // marker still plots through its style and the block's definition draws on its own
        // layer: the layer alone would have passed them; the real state fails them.
        input.Entities["MH"] = new EntityState { Handle = "MH", Exists = true, Layer = "PMX-COMP-NP-UTILITY", Plots = true, PlotNote = "marker on layer V-NODE-MARK (point style)" };
        input.Entities["BLK"] = new EntityState { Handle = "BLK", Exists = true, Layer = "PMX-COMP-NP-BLOCK", Plots = true, PlotNote = "block POLE draws on V-UTIL-POWR" };
        input.Entities["MISSING"] = new EntityState { Handle = "MISSING", Exists = true, Layer = Curb, Plots = false, PlotNote = "invisible" };
        var checks = CompareQc.Evaluate(input);
        var duplicates = checks.Single(x => x.Code == "DUPLICATES-NONPLOT");
        Assert.False(duplicates.Passed);
        Assert.Contains("point style", duplicates.Message);
        Assert.Contains("draws on", duplicates.Message);
        var missing = checks.Single(x => x.Code == "MISSING-PLOT");
        Assert.False(missing.Passed);
        Assert.Contains("invisible", missing.Message);

        // Fixed states pass; an undeterminable state blocks on its own check.
        input.Entities["MH"].Plots = false; input.Entities["BLK"].Plots = false; input.Entities["MISSING"].Plots = true;
        checks = CompareQc.Evaluate(input);
        Assert.True(checks.Single(x => x.Code == "DUPLICATES-NONPLOT").Passed);
        Assert.True(checks.Single(x => x.Code == "MISSING-PLOT").Passed);
        Assert.True(checks.Single(x => x.Code == "PLOT-VERIFIED").Passed);
        input.Entities["MH"].Plots = null; input.Entities["MH"].PlotNote = "point group style override could not be determined";
        checks = CompareQc.Evaluate(input);
        Assert.False(checks.Single(x => x.Code == "PLOT-VERIFIED").Passed);
        Assert.False(CompareQc.Passes(checks));
    }

    [Fact]
    public void SplitCopiesMustPlotForTheQcToPass()
    {
        var s = CF.Survey(); s.Add(CF.Line("PARTIAL", "V-SURF-ASPH-E", 0, 10, 350, 10));
        var c = CF.Consultant(); c.Add(CF.Line("C2", "E-EOP", 0, 10, 200, 10));
        var profile = CF.Profile();
        var result = CF.Run(s, c, profile);
        var plan = ChangePlanner.Build(result, profile);
        var change = Assert.Single(plan.Changes);
        var input = new QcInput { Profile = profile, Result = result, Plan = plan, DrawingPath = "/tmp/site.dwg", Applied = new AppliedChangeSet() };
        foreach (var name in profile.NonplotLayers.All) input.Layers[name] = new LayerState { Name = name, Exists = true, Plottable = false };
        input.Applied.Changes.Add(new AppliedChange { Handle = change.Handle, OriginalLayer = change.FromLayer, NonplotLayer = change.ToLayer, Action = change.Action, SplitHandles = new List<string> { "COPY1" } });
        input.Entities["PARTIAL"] = new EntityState { Handle = "PARTIAL", Exists = true, Layer = change.ToLayer, Plots = false };
        input.Entities["COPY1"] = new EntityState { Handle = "COPY1", Exists = true, Layer = "V-SURF-ASPH-E", Plots = false, PlotNote = "layer V-SURF-ASPH-E frozen" };
        var checks = CompareQc.Evaluate(input);
        Assert.False(checks.Single(x => x.Code == "MISSING-PLOT").Passed);
        input.Entities["COPY1"].Plots = true;
        Assert.True(CompareQc.Evaluate(input).Single(x => x.Code == "MISSING-PLOT").Passed);
    }

    [Fact]
    public void AppliedRecordKeepsEverythingNeededToReverseEveryMethod()
    {
        var set = new AppliedChangeSet();
        set.Changes.Add(new AppliedChange { Handle = "P1", Method = "LayerAndPointStyle", OriginalLayer = "V-NODE", OriginalPointStyle = "2F", OriginalLabelVisible = true, Verified = true });
        set.Changes.Add(new AppliedChange { Handle = "B1", Method = "BlockTwin", OriginalLayer = "V-UTIL-POWR", OriginalBlockDefinition = "A1", TwinBlockDefinition = "A2", Verified = true });
        set.Changes.Add(new AppliedChange { Handle = "B2", Method = "Invisible", OriginalLayer = "V-SIGN", OriginalVisible = true, Verified = true });
        var back = AppliedChangeSet.FromJson(set.ToJson())!;
        Assert.Equal("2F", back.Find("P1")!.OriginalPointStyle);
        Assert.True(back.Find("P1")!.OriginalLabelVisible);
        Assert.Equal("A1", back.Find("B1")!.OriginalBlockDefinition);
        Assert.Equal("A2", back.Find("B1")!.TwinBlockDefinition);
        Assert.True(back.Find("B2")!.OriginalVisible);
        Assert.All(back.Changes, ch => Assert.True(ch.Verified));
    }

    [Fact]
    public void KeepIntervalsCarryTheirEndPointsIntoThePlan()
    {
        var s = CF.Survey(); s.Add(CF.Line("S1", Curb, 0, 0, 100, 0, 100, 100));
        var c = CF.Consultant(); c.Add(CF.Line("C1", "E-CURB", 0, 0, 100, 0));
        var profile = CF.Profile();
        var result = CF.Run(s, c, profile);
        var plan = ChangePlanner.Build(result, profile);
        var keep = Assert.Single(Assert.Single(plan.Changes).KeepIntervals);
        Assert.Equal(100, keep.StartPoint.X, 1);
        Assert.InRange(keep.StartPoint.Y, 0, 1.2);
        Assert.Equal(100, keep.EndPoint.X, 6);
        Assert.Equal(100, keep.EndPoint.Y, 6);
    }
}

public sealed class RepeatabilityTests
{
    [Fact]
    public void RepeatedRunsPlanTheSameChangesWithTheSameSplitKeys()
    {
        var s = CF.Survey();
        s.Add(CF.Line("A", "V-SURF-CURB-E", 0, 0, 100, 0));
        s.Add(CF.Line("B", "V-SURF-ASPH-E", 0, 10, 350, 10));
        s.Add(CF.Cogo("P", "SDMH 1", 50, 30, 100.0));
        var c = CF.Consultant();
        c.Add(CF.Line("C1", "E-CURB", 0, 0, 100, 0));
        c.Add(CF.Line("C2", "E-EOP", 0, 10, 200, 10));
        c.Add(CF.Block("C3", "E-STRM-MH", "STMH", 50.2, 30.1));
        var profile = CF.Profile();
        var first = ChangePlanner.Build(CF.Run(s, c, profile), profile);
        var second = ChangePlanner.Build(CF.Run(s, c, profile), profile);
        Assert.Equal(first.Changes.Count, second.Changes.Count);
        foreach (var change in first.Changes)
        {
            var again = second.Changes.Single(x => x.Handle == change.Handle);
            Assert.Equal(change.Action, again.Action);
            Assert.Equal(change.ToLayer, again.ToLayer);
            // The reconcile step compares this key: identical keys mean an already applied
            // change is left alone, never re-split into a duplicate copy.
            Assert.Equal(change.IntervalsKey, again.IntervalsKey);
        }
    }
}
