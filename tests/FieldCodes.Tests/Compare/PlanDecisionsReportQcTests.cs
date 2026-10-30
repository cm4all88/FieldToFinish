using FieldCodes;
using FieldCodes.Compare;

namespace FieldCodes.Tests;

/// <summary>The plan, persistent decisions, re-runs, the report, QC and batch.</summary>
public sealed class PlanDecisionsReportQcTests
{
    private static (CompareDataset s, CompareDataset c) Site()
    {
        var s = CF.Survey();
        s.Add(CF.Line("MATCHED", "V-SURF-CURB-E", 0, 0, 100, 0));
        s.Add(CF.Line("PARTIAL", "V-SURF-ASPH-E", 0, 10, 350, 10));
        s.Add(CF.Line("MISSING", "V-SURF-FENC-E", 0, 20, 100, 20));
        s.Add(CF.Cogo("MH", "SDMH 1", 50, 30, 100.0));
        s.Add(CF.Cogo("MHDIFF", "SDMH 2", 150, 30, 100.0));
        s.Add(CF.Line("UNKNOWN", "XX-NOT-MAPPED", 0, 40, 100, 40));
        var c = CF.Consultant();
        c.Add(CF.Line("C1", "E-CURB", 0, 0, 100, 0));
        c.Add(CF.Line("C2", "E-EOP", 0, 10, 200, 10));
        c.Add(CF.Block("C3", "E-STRM-MH", "STMH", 50.2, 30.1));
        c.Add(CF.Block("C4", "E-STRM-MH", "STMH", 150.2, 30.1, 101.5));
        return (s, c);
    }

    [Fact]
    public void PlanOnlyMovesConfidentMatchesAndExplicitDecisions()
    {
        var (s, c) = Site();
        var result = CF.Run(s, c);
        var plan = ChangePlanner.Build(result, CF.Profile());
        Assert.Equal(PlannedAction.Nonplot, CF.R(result, "MATCHED").Action);
        Assert.Equal(PlannedAction.SplitNonplot, CF.R(result, "PARTIAL").Action);
        Assert.Equal(PlannedAction.None, CF.R(result, "MISSING").Action);
        Assert.Equal(PlannedAction.Nonplot, CF.R(result, "MH").Action);
        Assert.Equal(PlannedAction.None, CF.R(result, "MHDIFF").Action);
        Assert.Equal(MatchStatus.Discrepancy, CF.R(result, "MHDIFF").EffectiveStatus);
        Assert.Equal(PlannedAction.None, CF.R(result, "UNKNOWN").Action);
        Assert.Equal(3, plan.Changes.Count);
        Assert.Equal(2, plan.NonplotCount);
        Assert.Equal(1, plan.SplitCount);
        Assert.Equal(2, plan.AwaitingReview);    // the discrepancy and the unmapped line
        var summary = result.Summary();
        Assert.Equal(2, summary.Matched);
        Assert.Equal(1, summary.Missing);
        Assert.Equal(1, summary.Partial);
        Assert.Equal(1, summary.Discrepancies);
        Assert.Equal(1, summary.NotEvaluated);
    }

    [Fact]
    public void DecisionsOverrideTheAutomaticResult()
    {
        var (s, c) = Site();
        var profile = CF.Profile();
        var result = CF.Run(s, c, profile);
        var decisions = new ReviewDecisionSet();
        decisions.Set(CF.R(result, "MATCHED"), ReviewDecision.KeepPlotting, "office wants our curb");
        decisions.Set(CF.R(result, "MISSING"), ReviewDecision.MakeNonplot, "shown on their sheet 3");
        decisions.Set(CF.R(result, "MHDIFF"), ReviewDecision.AcceptMatch, "their rim is a typo");
        decisions.Set(CF.R(result, "UNKNOWN"), ReviewDecision.Exclude, null);
        decisions.Set(CF.R(result, "PARTIAL"), ReviewDecision.AcceptPartial, null);
        var plan = ChangePlanner.Build(result, profile);

        Assert.Equal(PlannedAction.None, CF.R(result, "MATCHED").Action);
        Assert.Equal(MatchStatus.Missing, CF.R(result, "MATCHED").EffectiveStatus);   // kept plotting: counted as ours
        Assert.Equal(PlannedAction.Nonplot, CF.R(result, "MISSING").Action);
        Assert.Equal(PlannedAction.Nonplot, CF.R(result, "MHDIFF").Action);
        Assert.Equal(MatchStatus.Matched, CF.R(result, "MHDIFF").EffectiveStatus);
        Assert.Equal(MatchStatus.Excluded, CF.R(result, "UNKNOWN").EffectiveStatus);
        Assert.Equal(PlannedAction.SplitNonplot, CF.R(result, "PARTIAL").Action);
        Assert.Equal(0, plan.AwaitingReview);
        Assert.Equal(5, result.Summary().Decided);
    }

    [Fact]
    public void DecisionsPersistAndSurviveARerunOnlyWhileTheFeatureIsUnchanged()
    {
        var (s, c) = Site();
        var first = CF.Run(s, c);
        var decisions = new ReviewDecisionSet();
        decisions.Set(CF.R(first, "MISSING"), ReviewDecision.MakeNonplot, "sheet 3");
        decisions.Set(CF.R(first, "MHDIFF"), ReviewDecision.AcceptMatch, "typo");
        var json = decisions.ToJson();

        // Re-run against a revised consultant drawing: our MHDIFF point moved since.
        var (s2, c2) = Site();
        s2.Features.Single(f => f.Handle == "MHDIFF").Position = new CPoint(150.5, 30, 100);
        var second = CF.Run(s2, c2);
        var reloaded = ReviewDecisionSet.FromJson(json);
        var stale = new List<string>();
        var applied = reloaded.ApplyTo(second, stale);
        Assert.Equal(1, applied);
        Assert.Equal(new[] { "MHDIFF" }, stale);
        Assert.Equal(ReviewDecision.MakeNonplot, CF.R(second, "MISSING").Decision);
        Assert.Equal(ReviewDecision.None, CF.R(second, "MHDIFF").Decision);
        Assert.Contains(CF.R(second, "MHDIFF").Notes, n => n.Contains("changed since"));
    }

    [Fact]
    public void RepeatedRunsAreDeterministic()
    {
        var (s, c) = Site();
        var a = CF.Run(s, c);
        var b = CF.Run(s, c);
        Assert.Equal(a.Results.Count, b.Results.Count);
        foreach (var ra in a.Results)
        {
            var rb = CF.R(b, ra.Handle);
            Assert.Equal(ra.Status, rb.Status);
            Assert.Equal(ra.Action, rb.Action);
            Assert.Equal(ra.Fingerprint, rb.Fingerprint);
            Assert.Equal(ra.Intervals.Count, rb.Intervals.Count);
        }
    }

    [Fact]
    public void AppliedChangesRoundTripWithEverythingNeededToRestore()
    {
        var set = new AppliedChangeSet { RunId = "abc", Profile = "Default", DrawingPath = "x.dwg", AppliedUtc = DateTime.UtcNow };
        set.Changes.Add(new AppliedChange
        {
            Handle = "1A", Kind = FeatureKind.Line, OriginalLayer = "V-SURF-CURB-E", OriginalColor = "ByLayer", OriginalLinetype = "ByLayer",
            OriginalLineweight = "ByLayer", OriginalPlotStyle = "ByLayer", NonplotLayer = "PMX-COMP-NP-LINE", Action = PlannedAction.SplitNonplot,
            SplitHandles = new List<string> { "2B", "2C" }
        });
        var back = AppliedChangeSet.FromJson(set.ToJson())!;
        var change = back.Find("1a")!;
        Assert.Equal("V-SURF-CURB-E", change.OriginalLayer);
        Assert.Equal(2, change.SplitHandles.Count);
        Assert.Equal(PlannedAction.SplitNonplot, change.Action);
    }

    [Fact]
    public void ReportHasTheRequiredColumnsAndOneRowPerFeature()
    {
        var (s, c) = Site();
        var result = CF.Run(s, c);
        var csv = CompareReport.Render(result, "SITE-1");
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(result.Results.Count + 1, lines.Length);
        foreach (var column in new[] { "Site", "FeatureType", "SourceLayer", "SurveyHandle", "ConsultantHandle", "Status", "HorizontalOffsetFt", "VerticalDifferenceFt", "OriginalPlottingLayer", "FinalPlottingLayer", "ReviewDecision", "Notes" })
            Assert.Contains(column, lines[0]);
        var matched = lines.Single(l => l.Contains(",MATCHED,"));
        Assert.Contains("PMX-COMP-NP-LINE", matched);
        Assert.Contains("SITE-1", matched);
        var missing = lines.Single(l => l.Contains(",MISSING,"));
        Assert.Contains("V-SURF-FENC-E,None", missing);

        var text = CompareReport.SummaryText(result, ChangePlanner.Build(result, CF.Profile()));
        Assert.Contains("2 matched, 1 missing, 1 partial, 1 discrepancies", text);
        Assert.Contains("XX-NOT-MAPPED", text);
        Assert.Equal(".ftf-compare.csv", CompareReport.Suffix);
    }

    [Fact]
    public void FinalQcFailsUntilEverythingIsAppliedAndResolved()
    {
        var (s, c) = Site();
        var profile = CF.Profile();
        var result = CF.Run(s, c, profile);
        var plan = ChangePlanner.Build(result, profile);
        var input = new QcInput { Profile = profile, Result = result, Plan = plan, DrawingPath = "/tmp/site.dwg" };
        foreach (var name in profile.NonplotLayers.All) input.Layers[name] = new LayerState { Name = name, Exists = true, Plottable = false };
        foreach (var r in result.Results) input.Entities[r.Handle] = new EntityState { Handle = r.Handle, Exists = true, Layer = r.SourceLayer };

        var checks = CompareQc.Evaluate(input);
        Assert.False(CompareQc.Passes(checks));
        Assert.False(checks.Single(x => x.Code == "APPLIED").Passed);
        Assert.False(checks.Single(x => x.Code == "DISCREPANCIES").Passed);
        Assert.False(checks.Single(x => x.Code == "NOT-EVALUATED").Passed);

        // Resolve the review items, apply the plan, and the drawing passes.
        var decisions = new ReviewDecisionSet();
        decisions.Set(CF.R(result, "MHDIFF"), ReviewDecision.KeepPlotting, "field verified");
        decisions.Set(CF.R(result, "UNKNOWN"), ReviewDecision.Exclude, null);
        plan = ChangePlanner.Build(result, profile);
        input.Plan = plan;
        input.Applied = new AppliedChangeSet { RunId = result.RunId };
        foreach (var change in plan.Changes)
        {
            input.Applied.Changes.Add(new AppliedChange { Handle = change.Handle, OriginalLayer = change.FromLayer, NonplotLayer = change.ToLayer, Action = change.Action });
            input.Entities[change.Handle] = new EntityState { Handle = change.Handle, Exists = true, Layer = change.ToLayer };
        }
        checks = CompareQc.Evaluate(input);
        Assert.True(CompareQc.Passes(checks), CompareQc.Render(checks));

        // A plottable nonplot layer, or a leftover split from an earlier run, fails it again.
        input.Layers["PMX-COMP-NP-LINE"].Plottable = true;
        Assert.False(CompareQc.Evaluate(input).Single(x => x.Code == "NONPLOT-LAYERS").Passed);
        input.Layers["PMX-COMP-NP-LINE"].Plottable = false;
        input.StaleSplitHandles.Add("DEAD1");
        Assert.False(CompareQc.Evaluate(input).Single(x => x.Code == "PREVIOUS-RUNS").Passed);
        input.StaleSplitHandles.Clear();
        input.Xrefs.Add(new XrefState { Name = "BASE", Path = "BASE.dwg", Resolved = false });
        Assert.False(CompareQc.Evaluate(input).Single(x => x.Code == "XREFS").Passed);

        // A matched duplicate put back on its plotting layer by hand is caught.
        input.Xrefs.Clear();
        input.Entities["MATCHED"].Layer = "V-SURF-CURB-E";
        Assert.False(CompareQc.Evaluate(input).Single(x => x.Code == "DUPLICATES-NONPLOT").Passed);
    }

    [Fact]
    public void QcRefusesToPassWithIncompleteSources()
    {
        var (s, c) = Site();
        c.AddIssue(Severity.Error, "XREF-UNRESOLVED", "UTIL.dwg not found", true);
        var profile = CF.Profile();
        var result = CF.Run(s, c, profile);
        var input = new QcInput { Profile = profile, Result = result, Plan = ChangePlanner.Build(result, profile), DrawingPath = "/tmp/x.dwg" };
        var checks = CompareQc.Evaluate(input);
        Assert.False(checks.Single(x => x.Code == "SOURCES").Passed);
        Assert.False(CompareQc.Passes(checks));
        Assert.StartsWith("Final QC: FAILED", CompareQc.Render(checks));
    }

    [Fact]
    public void BatchManifestValidatesAndSummarises()
    {
        var m = BatchManifest.FromJson("{ \"defaultOutputFolder\": \"out\", \"sites\": [ { \"name\": \"A\", \"surveyDrawing\": \"a.dwg\" }, { \"name\": \"A\", \"surveyDrawing\": \"b.dwg\" }, { \"surveyDrawing\": \"\" } ] }");
        var problems = new List<string>();
        m.Validate(problems);
        Assert.Contains(problems, p => p.Contains("used twice"));
        Assert.Contains(problems, p => p.Contains("no name"));
        Assert.Equal("Default", m.ProfileFor(m.Sites[0]));
        Assert.Equal("out", m.OutputFolderFor(m.Sites[0]));

        var outcomes = new List<BatchSiteOutcome>
        {
            new BatchSiteOutcome { Name = "A", Succeeded = true, NeedsReview = false, OutputPath = "out/approved/A-ACC.dwg", Summary = new ComparisonSummary { Matched = 5 } },
            new BatchSiteOutcome { Name = "B", Succeeded = true, NeedsReview = true, OutputPath = "out/needs-review/B-ACC.dwg", Summary = new ComparisonSummary { NeedsReview = 2 } },
            new BatchSiteOutcome { Name = "C", Succeeded = false, Error = "file not found" }
        };
        var text = BatchSummary.RenderText(outcomes);
        Assert.Contains("3 site(s), 1 approved, 1 need review, 1 failed", text);
        var csv = BatchSummary.RenderCsv(outcomes);
        Assert.Equal(4, csv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }
}
