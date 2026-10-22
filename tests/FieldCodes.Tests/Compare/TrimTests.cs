using FieldCodes.Compare;

namespace FieldCodes.Tests;

/// <summary>Trimming the aerial base inside our limits of survey, and the seam checks at
/// every cut. Pure geometry; the drawing side applies the plan with the comparison's own
/// apply and restore record.</summary>
public sealed class TrimTests
{
    private static Footprint Box(string id, double minX, double minY, double maxX, double maxY) => new Footprint
    {
        Id = id, Source = "test", Layer = "V-SURV-LIMITS",
        Polygon = new List<CPoint> { new(minX, minY), new(maxX, minY), new(maxX, maxY), new(minX, maxY) }
    };

    private static CompareProfile Profile()
    {
        var p = CF.Profile();
        p.NonplotLayers.Mode = NonplotLayerNames.ModeSuffix;
        return p;
    }

    [Fact]
    public void TheDefaultsReadSensibly()
    {
        var p = CompareProfile.CreateDefault();
        Assert.Equal(2.0, p.Trim.SeamSearchFeet);
        Assert.Equal(0.5, p.Trim.SeamOffsetFeet);
        Assert.Equal(0.5, p.Trim.SeamElevationFeet);
        Assert.Contains("V-SURV-LIMIT*", p.Trim.LimitsLayers);
        var problems = new List<string>();
        p.Trim.SeamOffsetFeet = 5;
        p.Validate(problems);
        Assert.Contains(problems, x => x.Contains("seamOffsetFeet"));
    }

    [Fact]
    public void ALineCrossingTheFootprintIsCutExactlyAtTheEdge()
    {
        var fp = new[] { Box("A", 950, 950, 1150, 1150) };
        var line = CF.Line("L", "E-CURB", 900, 1000, 1200, 1000);
        var intervals = FootprintClipper.Intervals(line.Vertices, line.Stations, fp);
        Assert.Equal(3, intervals.Count);
        Assert.Equal(IntervalKind.Uncovered, intervals[0].Kind);
        Assert.Equal(IntervalKind.Covered, intervals[1].Kind);
        Assert.Equal(IntervalKind.Uncovered, intervals[2].Kind);
        Assert.Equal(50, intervals[0].End, 6);
        Assert.Equal(250, intervals[1].End, 6);
        Assert.Equal(950, intervals[1].StartPoint.X, 6);
        Assert.Equal(1150, intervals[1].EndPoint.X, 6);
        Assert.Equal(300, intervals[2].End, 6);

        Assert.Single(FootprintClipper.Intervals(CF.Line("I", "E-CURB", 1000, 1000, 1100, 1000).Vertices, CF.Line("I", "E-CURB", 1000, 1000, 1100, 1000).Stations, fp));
        Assert.Equal(IntervalKind.Covered, FootprintClipper.Intervals(CF.Line("I", "E-CURB", 1000, 1000, 1100, 1000).Vertices, CF.Line("I", "E-CURB", 1000, 1000, 1100, 1000).Stations, fp)[0].Kind);
        Assert.Equal(IntervalKind.Uncovered, FootprintClipper.Intervals(CF.Line("O", "E-CURB", 1000, 1300, 1100, 1300).Vertices, CF.Line("O", "E-CURB", 1000, 1300, 1100, 1300).Stations, fp)[0].Kind);

        // Two footprints: in, out, in, out.
        var two = new[] { Box("A", 950, 950, 1000, 1150), Box("B", 1100, 950, 1150, 1150) };
        var kinds = FootprintClipper.Intervals(line.Vertices, line.Stations, two).Select(i => i.Kind).ToList();
        Assert.Equal(new[] { IntervalKind.Uncovered, IntervalKind.Covered, IntervalKind.Uncovered, IntervalKind.Covered, IntervalKind.Uncovered }, kinds);
    }

    private static (CompareDataset aerial, CompareDataset survey) Site()
    {
        var aerial = CF.Consultant("aerial.dwg");
        aerial.Add(CF.Line("CROSS", "E-CURB", 900, 1000, 1200, 1000));          // crosses: split, 50 ft outside each end
        aerial.Add(CF.Line("INSIDE", "E-CURB", 1000, 1050, 1100, 1050));        // entirely inside
        aerial.Add(CF.Line("OUT", "E-CURB", 1000, 1300, 1100, 1300));           // entirely outside
        aerial.Add(CF.Line("STUB", "E-CURB", 949.7, 1100, 1100, 1100));         // 0.3 ft outside remnant: not worth a piece
        aerial.Add(CF.Block("POLEIN", "E-PWR", "POLE", 1000, 1100));            // block inside
        aerial.Add(CF.Block("POLEOUT", "E-PWR", "POLE", 1300, 1200));           // block outside
        aerial.Add(CF.Text("TXT", "E-TEXT", "RIM 121.00", 1050, 1120));         // text inside
        aerial.Add(CF.Line("BORDER", "E-BORDER", 900, 900, 1200, 900));         // ignored layer
        var hatch = new CompareFeature { Handle = "HATCH", Kind = FeatureKind.Point, EntityType = "HATCH", Layer = "E-HATCH", ReadProblem = "HATCH objects are not compared", Position = new CPoint(1000, 1000), OwnerPath = "" };
        aerial.Add(hatch);

        var survey = CF.Survey("site.dwg");
        survey.Add(CF.Line("OURS", "V-SURF-CURB-E", 950, 1000.2, 1150, 1000.2));   // continues the crossing curb, 0.2 ft off
        return (aerial, survey);
    }

    [Fact]
    public void InsideGoesNonplotCrossingIsSplitOutsideIsLeftAlone()
    {
        var (aerial, survey) = Site();
        var profile = Profile();
        ChangePlan plan;
        var result = TrimPlanner.Plan(aerial, survey, new[] { Box("A", 950, 950, 1150, 1150) }, profile, out plan);

        var cross = result.Entries.Single(e => e.Handle == "CROSS");
        Assert.Equal(TrimDisposition.Split, cross.Disposition);
        Assert.Equal(200, cross.InsideLength, 6);
        Assert.Equal(100, cross.OutsideLength, 6);
        Assert.Equal("E-CURB-NP", cross.NonplotLayer);
        var change = plan.Changes.Single(c => c.Handle == "CROSS");
        Assert.Equal(PlannedAction.SplitNonplot, change.Action);
        Assert.Equal(2, change.KeepIntervals.Count);
        Assert.All(change.KeepIntervals, k => Assert.Equal(IntervalKind.Uncovered, k.Kind));
        Assert.Equal(0, change.KeepIntervals[0].Start, 6);
        Assert.Equal(50, change.KeepIntervals[0].End, 6);
        Assert.Equal(950, change.KeepIntervals[0].EndPoint.X, 6);

        Assert.Equal(TrimDisposition.Inside, result.Entries.Single(e => e.Handle == "INSIDE").Disposition);
        Assert.Equal(PlannedAction.Nonplot, plan.Changes.Single(c => c.Handle == "INSIDE").Action);
        Assert.Equal(TrimDisposition.Outside, result.Entries.Single(e => e.Handle == "OUT").Disposition);
        Assert.DoesNotContain(plan.Changes, c => c.Handle == "OUT");

        var stub = result.Entries.Single(e => e.Handle == "STUB");
        Assert.Equal(TrimDisposition.Inside, stub.Disposition);
        Assert.Contains("remnant", stub.Note);
        Assert.Equal(PlannedAction.Nonplot, plan.Changes.Single(c => c.Handle == "STUB").Action);

        Assert.Equal(TrimDisposition.Inside, result.Entries.Single(e => e.Handle == "POLEIN").Disposition);
        Assert.Equal("E-PWR-NP", result.Entries.Single(e => e.Handle == "POLEIN").NonplotLayer);
        Assert.Equal(TrimDisposition.Outside, result.Entries.Single(e => e.Handle == "POLEOUT").Disposition);
        Assert.Equal(TrimDisposition.Inside, result.Entries.Single(e => e.Handle == "TXT").Disposition);
        Assert.Equal(TrimDisposition.Outside, result.Entries.Single(e => e.Handle == "BORDER").Disposition);
        Assert.Contains("ignore", result.Entries.Single(e => e.Handle == "BORDER").Note);
        Assert.Equal(TrimDisposition.Straddles, result.Entries.Single(e => e.Handle == "HATCH").Disposition);
        Assert.DoesNotContain(plan.Changes, c => c.Handle == "HATCH");

        // The apply machinery reads the plan through these results.
        Assert.NotNull(result.Features.Find("CROSS"));
        Assert.Equal(3, result.Features.Find("CROSS")!.Intervals.Count);
        Assert.Equal(300, result.Features.Find("CROSS")!.Length, 6);

        var summary = result.Summary();
        Assert.Equal(4, summary.Inside);
        Assert.Equal(1, summary.Split);
        Assert.Equal(1, summary.Straddles);
        Assert.Equal(3, summary.Outside);
    }

    [Fact]
    public void SeamsAreAlignedDiscrepantMissingOrForReview()
    {
        var fp = new[] { Box("A", 950, 950, 1150, 1150) };
        var profile = Profile();
        var aerial = CF.Consultant("aerial.dwg");
        aerial.Add(CF.Line("ALIGNED", "E-CURB", 900, 1000, 1200, 1000));
        aerial.Add(CF.Line("OFF", "E-CURB", 900, 1050, 1200, 1050));
        aerial.Add(CF.Line("NONE", "E-CURB", 900, 1100, 1200, 1100));
        aerial.Add(CF.Line("TURNS", "E-FENCE", 900, 1120, 1200, 1120));
        aerial.Add(CF.Line("MYSTERY", "E-ZZZ", 900, 1130, 1200, 1130));
        aerial.Add(CF.Line3("HIGH", "E-CURB", 900, 1140, 100.8, 1200, 1140, 100.8));
        var survey = CF.Survey("site.dwg");
        survey.Add(CF.Line("S1", "V-SURF-CURB-E", 950, 1000.2, 1150, 1000.2));      // 0.2 ft: aligned
        survey.Add(CF.Line("S2", "V-SURF-CURB-E", 950, 1050.9, 1150, 1050.9));      // 0.9 ft: discrepancy
        survey.Add(CF.Line("S4", "V-SURF-FENC-E", 950, 1110, 950, 1130));           // perpendicular at the cut: review
        survey.Add(CF.Line("S5", "V-SURF-CURB-E", 950, 1130.1, 1150, 1130.1));      // their layer is unmapped: review
        survey.Add(CF.Line3("S6", "V-SURF-CURB-E", 950, 1140, 100.0, 1150, 1140, 100.0));   // 0.8 ft lower: discrepancy

        ChangePlan plan;
        var result = TrimPlanner.Plan(aerial, survey, fp, profile, out plan);
        Assert.Equal(12, result.Seams.Count);   // six lines, two cuts each

        SeamFinding At(string handle, double x) => result.Seams.Single(s => s.AerialHandle == handle && Math.Abs(s.Cut.X - x) < 1e-6);
        Assert.Equal(SeamStatus.Aligned, At("ALIGNED", 950).Status);
        Assert.Equal("S1", At("ALIGNED", 950).SurveyHandle);
        Assert.Equal(0.2, At("ALIGNED", 950).Offset!.Value, 6);
        Assert.Equal(SeamStatus.Discrepancy, At("OFF", 1150).Status);
        Assert.Contains("0.90 ft from our line", At("OFF", 1150).Note);
        Assert.Equal(SeamStatus.NoContinuation, At("NONE", 950).Status);
        Assert.Equal(SeamStatus.NeedsReview, At("TURNS", 950).Status);
        Assert.Contains("directions differ", At("TURNS", 950).Note);
        Assert.Equal(SeamStatus.NeedsReview, At("MYSTERY", 950).Status);
        Assert.Contains("unknown", At("MYSTERY", 950).Note);
        Assert.Equal(SeamStatus.Discrepancy, At("HIGH", 950).Status);
        Assert.Equal(0.8, At("HIGH", 950).ElevationDifference!.Value, 6);
        Assert.Contains("elevation differs by 0.80 ft", At("HIGH", 950).Note);

        var csv = TrimReport.RenderSeams(result);
        Assert.Contains("Discrepancy", csv);
        Assert.Contains("NoContinuation", csv);
        Assert.StartsWith(TrimReport.SeamHeader, csv);
        var entries = TrimReport.Render(result);
        Assert.Contains("Split", entries);
        var text = TrimReport.SummaryText(result);
        Assert.Contains("6 split at the edge", text);
        Assert.Contains("2 aligned", text);
        Assert.Contains("4 discrepancies", text);          // OFF and HIGH, both cuts each
        Assert.Contains("3 with no continuation", text);   // NONE both cuts, TURNS at x = 1150
        Assert.Contains("3 for review", text);             // TURNS at x = 950, MYSTERY both cuts

        var round = TrimResult.FromJson(result.ToJson());
        Assert.Equal(result.Seams.Count, round!.Seams.Count);
        Assert.Equal(result.Entries.Count, round.Entries.Count);
        Assert.NotNull(round.Features.Find("OFF"));
    }

    [Fact]
    public void WithoutAFootprintNothingIsTrimmed()
    {
        var (aerial, survey) = Site();
        ChangePlan plan;
        var result = TrimPlanner.Plan(aerial, survey, new List<Footprint>(), Profile(), out plan);
        Assert.Empty(plan.Changes);
        Assert.Contains(result.Issues, i => i.Code == "NO-FOOTPRINT");
        Assert.All(result.Entries, e => Assert.Equal(TrimDisposition.Outside, e.Disposition));
    }
}
