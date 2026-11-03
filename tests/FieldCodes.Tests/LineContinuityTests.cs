using FieldCodes.Linework;
using FieldCodes.Review;
using FieldCodes.Settings;

namespace FieldCodes.Tests;

/// <summary>
/// Linework continuity on synthetic shots and plain polylines (the TBC-export reality): the
/// audit's scenarios A1-A5, gaps inside a coded string, skipped shots, undrawn strings, curves,
/// and that nothing is ever inferred from consecutive point numbers alone.
/// </summary>
public sealed class LineContinuityTests : IClassFixture<RulesFixture>
{
    private readonly RulesFixture _fx;
    public LineContinuityTests(RulesFixture fx) => _fx = fx;

    private sealed class Scene
    {
        public readonly List<ContinuityShot> Shots = new();
        public readonly List<ContinuityLine> Lines = new();
        public readonly HashSet<string> Gates = new(StringComparer.OrdinalIgnoreCase);
    }

    private Scene S() => new();

    private void Shot(Scene s, string n, double x, double y, string d, double z = 100)
        => s.Shots.Add(new ContinuityShot { Number = n, Handle = "P" + n, RawDescription = d, X = x, Y = y, Z = z, Coding = LineworkCoding.Parse(d, _fx.Config) });

    private static ContinuityLine Line(Scene s, string handle, params string[] through)
    {
        var line = new ContinuityLine { Handle = handle, Layer = "V-SURF-FENC-E", EntityType = "Polyline" };
        foreach (var n in through)
        {
            var p = s.Shots.First(x => x.Number == n);
            line.Vertices.Add(new[] { p.X, p.Y, p.Z });
            line.ShotsAtVertex.Add(new List<string> { n });
        }
        s.Lines.Add(line);
        return line;
    }

    private static ContinuityLine Plain(Scene s, string handle, string layer, params double[] xy)
    {
        var line = new ContinuityLine { Handle = handle, Layer = layer, EntityType = "Polyline", FeatureCode = "EP" };
        for (var i = 0; i < xy.Length; i += 2) { line.Vertices.Add(new[] { xy[i], xy[i + 1], 0.0 }); line.ShotsAtVertex.Add(new List<string>()); }
        s.Lines.Add(line);
        return line;
    }

    private ContinuityResult Run(Scene s, ContinuitySettings? settings = null) => LineContinuity.Analyze(new ContinuityInput
    {
        Shots = s.Shots, Lines = s.Lines, GatePosts = s.Gates, Settings = settings ?? new ContinuitySettings(), UnitsPerFoot = 1.0
    });

    [Fact]
    public void A1_AnInterruptedFenceIsAProbableMissingConnection()
    {
        var s = S();
        Shot(s, "101", 0, 0, "FNC B"); Shot(s, "102", 20, 0, "FNC"); Shot(s, "103", 40, 0, "FNC");
        Shot(s, "104", 45, 30, "PP");
        Shot(s, "105", 48, 0, "FNC B"); Shot(s, "106", 68, 0, "FNC"); Shot(s, "107", 88, 0, "FNC E");
        Line(s, "L1", "101", "102", "103");
        Line(s, "L2", "105", "106", "107");

        var gap = Assert.Single(Run(s).Findings);
        Assert.Equal(ReviewKinds.LineGap, gap.Kind);
        Assert.Equal(GapConfidence.Probable, gap.Confidence);
        Assert.Equal("line.gap:FNC:103-105", gap.Id);
        Assert.Equal(new[] { "103", "105" }, gap.Shots);
        Assert.Equal(new[] { "L1", "L2" }, gap.Lines);
        Assert.Equal(2, gap.Geometry.Count);
        Assert.Equal(8.0, gap.LengthFeet, 3);
        Assert.Contains(gap.Evidence, e => e.Contains("not coded E") && e.Contains("begun again at 105"));
        Assert.Contains(gap.Evidence, e => e.Contains("1 shot between") && e.Contains("PP"));
        Assert.Contains(gap.Evidence, e => e.StartsWith("lines up"));
        Assert.Equal("V-SURF-FENC-E", gap.Layer);
    }

    [Fact]
    public void A2_AGateOpeningIsNeverAMissingConnection()
    {
        var s = S();
        Shot(s, "201", 0, 0, "FNC B"); Shot(s, "202", 20, 0, "FGP FNC");
        Shot(s, "203", 26, 0, "FGP FNC B"); Shot(s, "204", 46, 0, "FNC E");
        s.Gates.Add("202"); s.Gates.Add("203");
        Line(s, "L1", "201", "202");
        Line(s, "L2", "203", "204");

        var result = Run(s);
        Assert.All(result.Findings, f => Assert.Equal(GapConfidence.Intentional, f.Confidence));
        Assert.Empty(result.ReviewItems(null));
    }

    [Fact]
    public void A3_ACodedBreakAcrossADrivewayIsIntentional()
    {
        var s = S();
        Shot(s, "301", 0, 0, "FNC B"); Shot(s, "302", 20, 0, "FNC E");
        Shot(s, "303", 32, 0, "FNC B"); Shot(s, "304", 52, 0, "FNC E");
        Line(s, "L1", "301", "302");
        Line(s, "L2", "303", "304");
        Plain(s, "D1", "V-SURF-ASPH-E", 24, -30, 24, 30);   // the driveway's edge crosses the gap

        var result = Run(s);
        Assert.Empty(result.ReviewItems(null));
        var f = Assert.Single(result.Findings);
        Assert.Equal(GapConfidence.Intentional, f.Confidence);
        Assert.Contains(f.Evidence, e => e.Contains("coded E"));
        Assert.Contains(f.Evidence, e => e.StartsWith("crossed by"));
    }

    [Fact]
    public void A4_ParallelStringsWithDifferentNumbersAreNeverJoined()
    {
        var s = S();
        Shot(s, "401", 0, 0, "FNC1 B"); Shot(s, "402", 20, 0, "FNC1 E");
        Shot(s, "403", 23, 0, "FNC2 B"); Shot(s, "404", 43, 0, "FNC2 E");
        Line(s, "L1", "401", "402");
        Line(s, "L2", "403", "404");
        Assert.Empty(Run(s).Findings);
    }

    [Fact]
    public void A5_ConsecutiveNumbersAloneProposeNothing()
    {
        var s = S();
        Shot(s, "201", 0, 0, "FNC B"); Shot(s, "202", 10, 0, "FNC"); Shot(s, "203", 20, 0, "FNC E");
        Shot(s, "204", 40, 40, "FNC B"); Shot(s, "205", 40, 50, "FNC"); Shot(s, "206", 40, 60, "FNC E");
        Line(s, "L1", "201", "202", "203");
        Line(s, "L2", "204", "205", "206");
        Assert.Empty(Run(s).Findings);
    }

    [Fact]
    public void ACodedContinuousStringDrawnInTwoPiecesHasAGap()
    {
        var s = S();
        Shot(s, "501", 0, 0, "EP B"); Shot(s, "502", 20, 0, "EP"); Shot(s, "503", 30, 0, "EP"); Shot(s, "504", 50, 0, "EP E");
        Line(s, "L1", "501", "502");
        Line(s, "L2", "503", "504");
        var gap = Assert.Single(Run(s).Findings);
        Assert.Equal("line.gap:EP:502-503", gap.Id);
        Assert.Equal(GapConfidence.Probable, gap.Confidence);
        Assert.Contains(gap.Evidence, e => e.Contains("coded continuous"));
    }

    [Fact]
    public void ALineThatSkipsACodedShotIsAMissingVertex()
    {
        var s = S();
        Shot(s, "601", 0, 0, "FNC B"); Shot(s, "602", 10, 3, "FNC"); Shot(s, "603", 20, 0, "FNC E");
        Line(s, "L1", "601", "603");
        var f = Assert.Single(Run(s).Findings);
        Assert.Equal(ReviewKinds.LineMissingVertex, f.Kind);
        Assert.Equal("line.missingvertex:FNC:601-602-603", f.Id);
        Assert.Equal(3, f.Geometry.Count);
        Assert.Contains("602", f.Reason);
    }

    [Fact]
    public void AShotOnTheLineButNotAVertexIsDrawn()
    {
        var s = S();
        Shot(s, "611", 0, 0, "FNC B"); Shot(s, "612", 10, 0.01, "FNC"); Shot(s, "613", 20, 0, "FNC E");
        Line(s, "L1", "611", "613");
        Assert.Empty(Run(s).Findings);
    }

    [Fact]
    public void AStringWithNoLineIsAMissingSegmentAndTheShotsHaveNoCoverage()
    {
        var s = S();
        Shot(s, "301", 0, 0, "EP B"); Shot(s, "302", 10, 0, "EP"); Shot(s, "303", 20, 0, "EP"); Shot(s, "304", 30, 0, "EP"); Shot(s, "305", 40, 0, "EP E");
        var result = Run(s);
        var f = Assert.Single(result.Findings);
        Assert.Equal(ReviewKinds.LineMissingSegment, f.Kind);
        Assert.Equal("line.missingsegment:EP:301-305", f.Id);
        Assert.StartsWith("Coded string not drawn", f.Issue);
        Assert.Equal(5, f.Geometry.Count);
        Assert.Empty(result.LinesOf("303", "EP"));
        Assert.Contains(f.Id, result.FindingsFor("303", "EP"));
    }

    [Fact]
    public void APartlyDrawnStringReportsOnlyTheUndrawnPart()
    {
        var s = S();
        Shot(s, "701", 0, 0, "EP B"); Shot(s, "702", 10, 0, "EP"); Shot(s, "703", 20, 0, "EP"); Shot(s, "704", 30, 0, "EP E");
        Line(s, "L1", "701", "702");
        var f = Assert.Single(Run(s).Findings);
        Assert.Equal("line.missingsegment:EP:702-704", f.Id);
        Assert.StartsWith("Coded segment not drawn", f.Issue);
    }

    [Fact]
    public void InterleavedStringsAreReadApart()
    {
        var s = S();
        Shot(s, "1", 0, 0, "EP1 B"); Shot(s, "2", 0, 30, "EP2 B"); Shot(s, "3", 30, 0, "EP1"); Shot(s, "4", 30, 30, "EP2");
        Shot(s, "5", 60, 0, "EP1 E"); Shot(s, "6", 60, 30, "EP2 E");
        Line(s, "L1", "1", "3", "5");
        Line(s, "L2", "2", "4", "6");
        var result = Run(s);
        Assert.Empty(result.Findings);
        Assert.Equal(new[] { "1", "3", "5" }, result.Strings.Single(x => x.FigureId == "EP1").Shots);
        Assert.Equal(new[] { "2", "4", "6" }, result.Strings.Single(x => x.FigureId == "EP2").Shots);
    }

    [Fact]
    public void ACurveShotNeedNotBeAVertex()
    {
        var s = S();
        Shot(s, "1", 0, 0, "EP B"); Shot(s, "2", 0, 20, "EP P"); Shot(s, "3", 5.858, 34.142, "EP"); Shot(s, "4", 20, 40, "EP T"); Shot(s, "5", 40, 40, "EP E");
        // The arc tessellated with its own vertices (no shot at them), through the curve shot.
        var line = Line(s, "L1", "1", "2");
        foreach (var a in new[] { 170.0, 157.5, 135.0, 112.5, 100.0 })
        {
            var r = a * Math.PI / 180;
            line.Vertices.Add(new[] { 20 + 20 * Math.Cos(r), 20 + 20 * Math.Sin(r), 0.0 });
            line.ShotsAtVertex.Add(new List<string>());
        }
        foreach (var n in new[] { "4", "5" })
        {
            var p = s.Shots.First(x => x.Number == n);
            line.Vertices.Add(new[] { p.X, p.Y, p.Z });
            line.ShotsAtVertex.Add(new List<string> { n });
        }
        var result = Run(s);
        Assert.Contains("3", result.Strings.Single().CurveShots);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void AClosedFigureIsCheckedRoundToItsStart()
    {
        var s = S();
        Shot(s, "1", 0, 0, "BLD B"); Shot(s, "2", 40, 0, "BLD"); Shot(s, "3", 40, 30, "BLD"); Shot(s, "4", 0, 30, "BLD C");
        var open = Line(s, "L1", "1", "2", "3", "4");
        Assert.Equal(StringEnd.Closed, Run(s).Strings.Single().End);
        var f = Assert.Single(Run(s).Findings);        // drawn open: the closing side is missing
        Assert.Equal(ReviewKinds.LineGap, f.Kind);
        open.Closed = true;
        Assert.Empty(Run(s).Findings);
    }

    [Fact]
    public void AnApprovedCorrectionCoversTheShotsButNeverHidesTheFinding()
    {
        var s = S();
        Shot(s, "1", 0, 0, "EP B"); Shot(s, "2", 10, 0, "EP E");
        var fix = Line(s, "FIX", "1", "2");
        fix.IsCorrection = true;
        var result = Run(s);
        Assert.Single(result.Findings);                     // the survey linework is still missing it
        Assert.Equal(new[] { "FIX" }, result.LinesOf("1", "EP"));   // but the ledger sees the shots drawn
    }

    [Fact]
    public void AnExplicitBreakThatLinesUpIsPossibleNotProbable()
    {
        var s = S();
        Shot(s, "1", 0, 0, "FNC B"); Shot(s, "2", 20, 0, "FNC"); Shot(s, "3", 40, 0, "FNC E");
        Shot(s, "4", 46, 0, "FNC B"); Shot(s, "5", 66, 0, "FNC"); Shot(s, "6", 86, 0, "FNC E");
        Line(s, "L1", "1", "2", "3");
        Line(s, "L2", "4", "5", "6");
        var f = Assert.Single(Run(s).Findings);
        Assert.Equal(GapConfidence.Possible, f.Confidence);
        Assert.Single(Run(s).ReviewItems(null));             // possible gaps need review too
    }

    [Fact]
    public void ThresholdsAreSettingsPerFeature()
    {
        var s = S();
        Shot(s, "101", 0, 0, "FNC B"); Shot(s, "102", 20, 0, "FNC"); Shot(s, "103", 40, 0, "FNC");
        Shot(s, "105", 48, 0, "FNC B"); Shot(s, "106", 68, 0, "FNC"); Shot(s, "107", 88, 0, "FNC E");
        Line(s, "L1", "101", "102", "103");
        Line(s, "L2", "105", "106", "107");
        var tight = new ContinuitySettings();
        tight.For("FNC").MaxGapFeet = 5;                      // the fence row, not the defaults
        Assert.Empty(Run(s, tight).Findings);
        var off = new ContinuitySettings();
        off.For("FNC").Enabled = false;
        Assert.Empty(Run(s, off).Findings);
        Assert.Single(Run(s).Findings);
    }

    [Fact]
    public void FindingsAreStableAcrossRunsAndChangeWhenTheSourceDoes()
    {
        Scene Build(double y105)
        {
            var s = S();
            Shot(s, "101", 0, 0, "FNC B"); Shot(s, "102", 20, 0, "FNC"); Shot(s, "103", 40, 0, "FNC");
            Shot(s, "105", 48, y105, "FNC B"); Shot(s, "106", 68, 0, "FNC"); Shot(s, "107", 88, 0, "FNC E");
            Line(s, "L1", "101", "102", "103");
            Line(s, "L2", "105", "106", "107");
            return s;
        }
        var a = Run(Build(0)).Findings.Single();
        var b = Run(Build(0)).Findings.Single();
        var moved = Run(Build(0.5)).Findings.Single();
        Assert.Equal(a.Id, b.Id);
        Assert.Equal(a.Fingerprint, b.Fingerprint);
        Assert.Equal(a.Id, moved.Id);
        Assert.NotEqual(a.Fingerprint, moved.Fingerprint);
    }

    [Fact]
    public void AClosedPolylineThatRepeatsItsFirstVertexIsNotAGap()
    {
        // Read by parameter, a closed polyline comes back to its first vertex: the first shot is
        // at two vertices. The closing side is drawn.
        var s = S();
        Shot(s, "1", 0, 0, "BLD B"); Shot(s, "2", 40, 0, "BLD"); Shot(s, "3", 40, 30, "BLD"); Shot(s, "4", 0, 30, "BLD C");
        var line = Line(s, "L1", "1", "2", "3", "4", "1");
        line.Closed = true;
        Assert.Empty(Run(s).Findings);
        line.Closed = false;                                  // drawn round but left open: still joined
        Assert.Empty(Run(s).Findings);
    }

    [Fact]
    public void ALineNothingIdentifiesCrossingTheGapDoesNotMakeItIntentional()
    {
        var s = S();
        Shot(s, "101", 0, 0, "FNC B"); Shot(s, "102", 20, 0, "FNC"); Shot(s, "103", 40, 0, "FNC");
        Shot(s, "104", 45, 30, "PP");
        Shot(s, "105", 48, 0, "FNC B"); Shot(s, "106", 68, 0, "FNC"); Shot(s, "107", 88, 0, "FNC E");
        Line(s, "L1", "101", "102", "103");
        Line(s, "L2", "105", "106", "107");
        var contour = Plain(s, "C1", "V-TOPO-CONT-E", 44, -30, 44, 30);
        contour.FeatureCode = null;                           // a contour: no feature, no shots
        var f = Assert.Single(Run(s).Findings);
        Assert.Equal(GapConfidence.Possible, f.Confidence);    // weakened, still for review
        Assert.Contains(f.Evidence, e => e.Contains("not an identified feature"));
    }

    [Fact]
    public void ALineExtendedTowardTheGapChangesItsFingerprint()
    {
        Scene Build(double end)
        {
            var s = S();
            Shot(s, "101", 0, 0, "FNC B"); Shot(s, "102", 20, 0, "FNC"); Shot(s, "103", 40, 0, "FNC");
            Shot(s, "105", 48, 0, "FNC B"); Shot(s, "106", 68, 0, "FNC"); Shot(s, "107", 88, 0, "FNC E");
            var l1 = Line(s, "L1", "101", "102", "103");
            if (end > 40) { l1.Vertices.Add(new[] { end, 0.0, 100.0 }); l1.ShotsAtVertex.Add(new List<string>()); }
            Line(s, "L2", "105", "106", "107");
            return s;
        }
        var before = Run(Build(40)).Findings.Single();
        var extended = Run(Build(44)).Findings.Single();
        Assert.Equal(before.Id, extended.Id);
        Assert.NotEqual(before.Fingerprint, extended.Fingerprint);
    }

    [Fact]
    public void FindingsAreKeptPerFigure()
    {
        var s = S();
        Shot(s, "1", 0, 0, "EP B LN B"); Shot(s, "2", 10, 0, "EP LN"); Shot(s, "3", 20, 0, "EP E LN E");
        var result = Run(s);                                    // nothing drawn; striping is not checked by default
        Assert.NotEmpty(result.FindingsFor("2", "EP"));
        Assert.Empty(result.FindingsFor("2", "LN"));
    }
}
