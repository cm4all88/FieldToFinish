using FieldCodes.Linework;
using FieldCodes.Review;
using FieldCodes.Settings;
using Newtonsoft.Json;

namespace FieldCodes.Tests;

/// <summary>
/// The point ledger: every point accounted for from evidence in the drawing (never from its code
/// alone), the audit's scenarios A6-A11, the baseline inventory and deleted points, source changes,
/// and dispositions that come only from closed review items.
/// </summary>
public sealed class PointAccountingTests : IClassFixture<RulesFixture>
{
    private readonly RulesFixture _fx;
    public PointAccountingTests(RulesFixture fx) => _fx = fx;

    /// <summary>A point; without a position it gets its own (10 ft per point number), so points
    /// are never duplicates by accident.</summary>
    private PointFacts P(string n, string d, double x = double.NaN, double y = 0, double z = 100)
    {
        var parsed = _fx.Parse(d, n);
        if (double.IsNaN(x)) x = double.Parse(n) * 10;
        return new PointFacts
        {
            Number = n, Handle = "H" + n, RawDescription = d, X = x, Y = y, Z = z,
            Parsed = parsed, Coding = LineworkCoding.Parse(d, _fx.Config),
            Marker = new MarkerFact { Known = true, Visible = true, MarkerType = "UseCustomMarker", StyleName = "Basic" }
        };
    }

    private static MarkerFact Symbol(string block) => new() { Known = true, Visible = true, MarkerType = "UseSymbolForMarker", SymbolName = block, StyleName = "PMX " + block };

    private AccountingContext Ctx(FtfSettings? settings = null, ContinuityResult? continuity = null) => new()
    {
        Rules = _fx.Config,
        Settings = settings ?? new FtfSettings(),
        Diagnostics = new CodeDiagnostics(_fx.Config, null),
        Continuity = continuity ?? new ContinuityResult(),
        FamilyOf = c => c,
        UnitsPerFoot = 1.0
    };

    private static (PointLedger ledger, List<ReviewItem> items, ReviewRegister register, LedgerRun run) Account(
        PointLedger? previous, IList<PointFacts> points, AccountingContext ctx, ReviewRegister? register = null)
    {
        var now = new DateTime(2026, 11, 3, 12, 0, 0);
        var r = PointAccounting.Build(previous, points, ctx, now);
        register ??= new ReviewRegister();
        register.Report(ReviewKinds.PointsSource, r.Items, now, true, null);
        var run = PointAccounting.Finish(r.Ledger, register, now, "test");
        return (r.Ledger, r.Items, register, run);
    }

    [Fact]
    public void ARecognizedCodeNeedsWhatItsRulePlans()
    {
        var labelled = P("10", "WFH 12");
        labelled.Outputs.Add(new OutputFact { Kind = "Label", Handle = "L10" });
        var bare = P("11", "WFH 13");                       // its label is not in the drawing
        var a = Account(null, new[] { labelled, bare }, Ctx());

        var ok = a.ledger.Find("10")!;
        Assert.Equal(PointDisposition.Drawn, ok.Disposition);
        Assert.Contains("label", ok.Expected);
        Assert.True(ok.DraftingComplete);
        Assert.True(ok.Represented);

        var missing = a.ledger.Find("11")!;
        Assert.Equal(PointDisposition.Review, missing.Disposition);
        Assert.Contains("label", missing.Missing);
        Assert.True(missing.Represented);                   // its marker shows it ...
        Assert.False(missing.DraftingComplete);             // ... but the drafting is not done
        var item = Assert.Single(a.items, i => i.Kind == ReviewKinds.PointMissingOutput);
        Assert.Equal("point.missingoutput:11", item.Id);
        Assert.Contains("HYDRANT 13", item.Reason);
    }

    [Fact]
    public void A11_ALabelTheSettingsTurnOffIsNotExpected()
    {
        var settings = new FtfSettings();
        settings.Annotation.Categories = new List<AnnotationCategory>();
        var hydrant = P("12", "WFH 14");
        hydrant.Marker = Symbol("PMX-WFH");
        var a = Account(null, new[] { hydrant }, Ctx(settings));
        var e = a.ledger.Find("12")!;
        Assert.DoesNotContain("label", e.Expected);
        Assert.Contains(e.Evidence, x => x.StartsWith("no label expected"));
        Assert.Equal(PointDisposition.Symbol, e.Disposition);   // the rule planned only a label: its symbol shows it
    }

    [Fact]
    public void A7_ABareCodeNeedsItsOwnSymbolNotAGenericMarker()
    {
        var generic = P("20", "SSCO");
        var own = P("21", "SSCO");
        own.Marker = Symbol("PMX-SSCO");
        var other = P("22", "SSCO");
        other.Marker = Symbol("PMX-CB");
        var none = P("23", "SSCO");
        none.Marker = new MarkerFact { Known = true, Visible = false, MarkerType = "UseSymbolForMarker", SymbolName = "PMX-SSCO", StyleName = "Hidden" };
        Assert.True(generic.Parsed.NoContent);
        var a = Account(null, new[] { generic, own, other, none }, Ctx());

        Assert.Equal(PointDisposition.Review, a.ledger.Find("20")!.Disposition);
        Assert.Contains("generic custom marker", a.items.Single(i => i.Id == "point.nosymbol:20").Reason);
        Assert.Equal(PointDisposition.Symbol, a.ledger.Find("21")!.Disposition);
        Assert.Contains("block PMX-CB", a.items.Single(i => i.Id == "point.nosymbol:22").Reason);
        Assert.Contains("draws no marker", a.items.Single(i => i.Id == "point.nosymbol:23").Reason);
        Assert.False(a.ledger.Find("23")!.Represented);
    }

    [Fact]
    public void ASymbolNamedInSettingsCounts()
    {
        var settings = new FtfSettings();
        settings.Accountability.SymbolBlocks["SSCO"] = new List<string> { "CLEANOUT*" };
        var p = P("24", "SSCO");
        p.Marker = Symbol("CLEANOUT-6IN");
        Assert.Equal(PointDisposition.Symbol, Account(null, new[] { p }, Ctx(settings)).ledger.Find("24")!.Disposition);
    }

    [Fact]
    public void A7_AnUnknownCodeIsHeldByItsCodeItem()
    {
        var p = P("30", "XYZQ 12");
        var a = Account(null, new[] { p }, Ctx());
        var e = a.ledger.Find("30")!;
        Assert.Equal("code.unknown:XYZQ", Assert.Single(e.Issues));
        Assert.Equal(PointDisposition.Review, e.Disposition);   // not accounted for without a rule or a decision
        Assert.Empty(a.items);                                  // the code check raises it, not the ledger

        // A documented decision on the code item accounts for its points.
        var register = new ReviewRegister();
        register.Report(ReviewChecks.CodesSource, new[] { ReviewKinds.New(ReviewKinds.CodeUnknown, "XYZQ", "1 point") }, DateTime.Now);
        register.MarkNotApplicable("code.unknown:XYZQ", "crew test shot, nothing to draw", "JD", DateTime.Now, out _);
        var b = Account(null, new[] { P("30", "XYZQ 12") }, Ctx(), register);
        Assert.Equal(PointDisposition.Excluded, b.ledger.Find("30")!.Disposition);
        Assert.Contains("crew test shot", b.ledger.Find("30")!.Decision);
    }

    [Fact]
    public void A6_DuplicatePositionsAreListedWithTheirPartners()
    {
        var a = Account(null, new[] { P("40", "SSCO", 5, 5), P("41", "WFH 1", 5.004, 5), P("42", "SSCO", 9, 9) }, Ctx());
        var d40 = a.items.Single(i => i.Id == "point.duplicate:40");
        Assert.Equal("40, 41", d40.Points);
        Assert.Contains("WFH 1", d40.Reason);
        Assert.Contains(a.items, i => i.Id == "point.duplicate:41");
        Assert.DoesNotContain(a.items, i => i.Id == "point.duplicate:42");
    }

    [Fact]
    public void A8_ALineworkShotNeedsALineOfItsFeature()
    {
        var shots = new[] { P("301", "EP B", 0, 0), P("302", "EP", 10, 0), P("303", "EP E", 20, 0) };
        var input = new ContinuityInput { Shots = shots.Select(s => new ContinuityShot { Number = s.Number, X = s.X, Y = s.Y, RawDescription = s.RawDescription, Coding = s.Coding }).ToList() };
        var continuity = LineContinuity.Analyze(input);           // no lines: one missing-segment finding
        var a = Account(null, shots, Ctx(continuity: continuity));
        var e = a.ledger.Find("302")!;
        Assert.Equal(PointDisposition.Review, e.Disposition);
        Assert.Contains("line.missingsegment:EP:301-303", e.Issues);
        Assert.DoesNotContain(a.items, i => i.Kind == ReviewKinds.PointMissingLinework);   // the finding covers it

        // Drawn: the shots are on a line of EP.
        var line = new ContinuityLine { Handle = "L1" };
        foreach (var s in shots) { line.Vertices.Add(new[] { s.X, s.Y, 0.0 }); line.ShotsAtVertex.Add(new List<string> { s.Number }); }
        input.Lines = new List<ContinuityLine> { line };
        var b = Account(null, shots, Ctx(continuity: LineContinuity.Analyze(input)));
        Assert.All(b.ledger.Points, p => Assert.Equal(PointDisposition.Linework, p.Disposition));
        Assert.Contains("on EP line L1", b.ledger.Find("302")!.Evidence);
    }

    [Fact]
    public void A9_IgnoredCodesNeedAReasonAndGroundShotsASurface()
    {
        Assert.Equal("surface", _fx.Config.IgnoreNoteFor("GS")!.Expect);
        var inDefinition = P("50", "GS");
        inDefinition.Surfaces.Add(new SurfaceFact { Surface = "EG", InDefinition = true, PointGroup = "TOPO", IsVertex = true });
        var coincident = P("51", "GS2");
        coincident.Surfaces.Add(new SurfaceFact { Surface = "EG", InDefinition = null, IsVertex = true });
        var leftOut = P("52", "GS");
        leftOut.Surfaces.Add(new SurfaceFact { Surface = "EG", InDefinition = true, PointGroup = "TOPO", IsVertex = false });
        var nowhere = P("53", "GS");
        var noReason = P("54", "ZK");
        var noted = P("55", "NOTE");

        var a = Account(null, new[] { inDefinition, coincident, leftOut, nowhere, noReason, noted }, Ctx());
        Assert.Equal(PointDisposition.Surface, a.ledger.Find("50")!.Disposition);
        Assert.Contains(a.ledger.Find("50")!.Evidence, e => e.Contains("in its definition") && e.Contains("TOPO"));
        Assert.Contains(a.ledger.Find("51")!.Evidence, e => e.Contains("coordinates only"));
        Assert.Contains("triangulation leaves it out", a.items.Single(i => i.Id == "point.missingoutput:52").Reason);
        Assert.Contains("no surface", a.items.Single(i => i.Id == "point.missingoutput:53").Reason);
        Assert.Contains(a.items, i => i.Id == "point.excludednoreason:54");
        Assert.Equal(PointDisposition.Excluded, a.ledger.Find("55")!.Disposition);
        Assert.Contains(a.ledger.Find("55")!.Evidence, e => e.Contains("Field note shot"));

        var lenient = new FtfSettings();
        lenient.Accountability.RequireIgnoreReasons = false;
        Assert.DoesNotContain(Account(null, new[] { P("54", "ZK") }, Ctx(lenient)).items, i => i.Kind == ReviewKinds.PointExcludedNoReason);
    }

    [Fact]
    public void A10_ControlPointsAndTheControlTable()
    {
        var a = Account(null, new[] { P("60", "FIP 5/8") }, Ctx());
        Assert.Equal(PointDisposition.Control, a.ledger.Find("60")!.Disposition);

        var ctx = Ctx();
        ctx.ControlTableExists = true;
        ctx.ControlTablePoints = new HashSet<string> { "61" };
        var b = Account(null, new[] { P("61", "FIP 5/8"), P("62", "FIP 1/2") }, ctx);
        Assert.Equal(PointDisposition.Control, b.ledger.Find("61")!.Disposition);
        Assert.Contains("not in the drawing's control table", b.items.Single(i => i.Id == "point.missingoutput:62").Reason);
    }

    [Fact]
    public void TheBaselineRemembersEveryPointAndReportsOnesThatDisappear()
    {
        var first = Account(null, new[] { P("70", "SSCO"), P("71", "WFH 3") }, Ctx());
        Assert.Equal("baseline", first.ledger.Find("70")!.History[0].Action);
        var stored = PointLedger.FromJson(first.ledger.ToJson());

        var second = Account(stored, new[] { P("70", "SSCO") }, Ctx());
        var gone = second.ledger.Find("71")!;
        Assert.True(gone.Deleted);
        Assert.Equal("WFH 3", gone.RawDescription);
        Assert.Contains(gone.History, h => h.Action == "deleted");
        var item = second.items.Single(i => i.Id == "point.deleted:71");
        Assert.True(item.Required || ReviewKinds.Get(item.Kind)!.Required);
        Assert.Equal(1, second.run.Deleted);
        Assert.Equal(1, second.run.Points);

        // Back again: restored, its history kept.
        var third = Account(PointLedger.FromJson(second.ledger.ToJson()), new[] { P("70", "SSCO"), P("71", "WFH 3") }, Ctx());
        var back = third.ledger.Find("71")!;
        Assert.False(back.Deleted);
        Assert.Contains(back.History, h => h.Action == "restored");
        Assert.DoesNotContain(third.items, i => i.Kind == ReviewKinds.PointDeleted);
    }

    [Fact]
    public void AChangedPointIsRecordedAndItsDecisionsReopen()
    {
        var register = new ReviewRegister();
        var first = Account(null, new[] { P("80", "SSCO") }, Ctx(), register);
        Assert.True(register.MarkNotApplicable("point.nosymbol:80", "cleanout shown by the utility plan", "JD", DateTime.Now, out _));
        var again = Account(PointLedger.FromJson(first.ledger.ToJson()), new[] { P("80", "SSCO") }, Ctx(), register);
        Assert.Equal(ReviewItemStatus.NotApplicable, register.Find("point.nosymbol:80")!.Status);
        Assert.Equal(PointDisposition.Excluded, again.ledger.Find("80")!.Disposition);   // documented by a person

        var moved = P("80", "SSCO", 800.5, 0);
        var third = Account(PointLedger.FromJson(again.ledger.ToJson()), new[] { moved }, Ctx(), register);
        Assert.Contains(third.items, i => i.Id == "point.sourcechanged:80" && i.Reason.Contains("moved 0.5"));
        var reopened = register.Find("point.nosymbol:80")!;
        Assert.Equal(ReviewItemStatus.Open, reopened.Status);
        Assert.Contains("source changed", reopened.Note);
        Assert.Contains(reopened.History!, h => h.Action == "source changed");
        Assert.Equal(PointDisposition.Review, third.ledger.Find("80")!.Disposition);
        Assert.False(register.Find("point.sourcechanged:80")!.Required);
    }

    [Fact]
    public void TheRunCountsDispositionsAndTheReportListsEveryPoint()
    {
        var hydrant = P("90", "WFH 7");
        hydrant.Outputs.Add(new OutputFact { Kind = "Label", Handle = "LB" });
        var a = Account(null, new[] { hydrant, P("91", "SSCO"), P("92", "NOTE") }, Ctx());
        Assert.Equal(3, a.run.Points);
        Assert.Equal(2, a.run.Accounted);
        Assert.Equal(1, a.run.Review);
        Assert.Equal(1, a.run.ByDisposition["Drawn"]);
        Assert.Contains("3 points: 2 accounted for", a.run.Describe());

        var csv = FieldCodes.Reporting.AccountabilityReport.RenderPoints(a.ledger);
        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.StartsWith("Point,Handle,RawDescription", lines[0]);
        Assert.Contains(lines, l => l.StartsWith("91,H91,SSCO,") && l.Contains(",Review,"));
        Assert.Contains(lines, l => l.Contains("Summary: 3 points"));
        Assert.Equal(FieldCodes.Reporting.AccountabilityReport.PointsHeader.Split(',').Length, lines[1].Split(',').Length);
    }

    [Fact]
    public void TheLedgerRoundTripsThroughJson()
    {
        var a = Account(null, new[] { P("1", "SSCO"), P("2", "WFH 1") }, Ctx());
        var json = a.ledger.ToJson();
        var back = PointLedger.FromJson(json);
        Assert.Equal(a.ledger.Points.Count, back.Points.Count);
        Assert.Equal(a.ledger.Find("1")!.Disposition, back.Find("1")!.Disposition);
        Assert.Equal(json, back.ToJson());
        Assert.Empty(PointLedger.FromJson("not json").Points);
    }
}

/// <summary>Decisions on review items: fingerprints, history, corrections, notes, bulk.</summary>
public sealed class ReviewDecisionTests
{
    private static ReviewItem Gap(string fp, params double[][] geometry) => new()
    {
        Id = "line.gap:FNC:103-105", Kind = ReviewKinds.LineGap, Source = ReviewKinds.ContinuitySource, Fingerprint = fp,
        Geometry = geometry.Length == 0 ? new List<double[]> { new[] { 0.0, 0, 0 }, new[] { 8.0, 0, 0 } } : geometry.ToList(), Code = "FNC"
    };

    [Fact]
    public void ADecisionHoldsWhileItsSourceIsUnchangedAndReopensWhenItChanges()
    {
        var r = new ReviewRegister();
        var t = new DateTime(2026, 11, 3, 9, 0, 0);
        r.Report(ReviewKinds.ContinuitySource, new[] { Gap("A") }, t);
        Assert.True(r.MarkNotApplicable("line.gap:FNC:103-105", "driveway", "JD", t, out _));
        r.Report(ReviewKinds.ContinuitySource, new[] { Gap("A") }, t.AddHours(1));
        Assert.Equal(ReviewItemStatus.NotApplicable, r.Items[0].Status);
        r.Report(ReviewKinds.ContinuitySource, new[] { Gap("B") }, t.AddHours(2));
        var item = r.Items[0];
        Assert.Equal(ReviewItemStatus.Open, item.Status);
        Assert.Contains("source changed since it was marked not applicable by JD", item.Note);
        Assert.Contains("(driveway)", item.Note);
        Assert.Equal(new[] { "opened", "not applicable", "source changed" }, item.History!.Select(h => h.Action));
    }

    [Fact]
    public void ACorrectionIsRecordedErasingItReopensAndALostFindingReopensIt()
    {
        var r = new ReviewRegister();
        var t = DateTime.Now;
        r.Report(ReviewKinds.ContinuitySource, new[] { Gap("A") }, t);
        Assert.True(r.MarkCorrected("line.gap:FNC:103-105", "2F1", "approved", "JD", t, out _));
        Assert.True(r.Items[0].IsDecided);
        r.Report(ReviewKinds.ContinuitySource, new[] { Gap("A") }, t);         // still found, same source: stays corrected
        Assert.Equal(ReviewItemStatus.Corrected, r.Items[0].Status);
        Assert.True(r.CorrectionErased("line.gap:FNC:103-105", t));
        Assert.Equal(ReviewItemStatus.Open, r.Items[0].Status);
        Assert.Null(r.Items[0].CorrectionHandle);

        Assert.True(r.MarkCorrected("line.gap:FNC:103-105", "2F2", null, "JD", t, out _));
        r.Report(ReviewKinds.ContinuitySource, new ReviewItem[0], t);           // the gap is no longer found
        Assert.Equal(ReviewItemStatus.Open, r.Items[0].Status);
        Assert.Contains("the correction 2F2 was kept", r.Items[0].Note);
    }

    [Fact]
    public void PointAndLineworkItemsNeedANoteToBeAccepted()
    {
        var r = new ReviewRegister();
        r.Report(ReviewKinds.ContinuitySource, new[] { Gap("A") }, DateTime.Now);
        Assert.False(r.Accept("line.gap:FNC:103-105", null, "JD", DateTime.Now, out var problem));
        Assert.Contains("documented disposition", problem);
        Assert.True(r.Accept("line.gap:FNC:103-105", "fence stops at the hedge", "JD", DateTime.Now, out _));
        Assert.Equal("fence stops at the hedge", r.Items[0].Note);

        // Older kinds keep the one-click accept.
        r.Report("Gates", new[] { ReviewKinds.New(ReviewKinds.GateUnpaired, "1", "x") }, DateTime.Now);
        Assert.True(r.Accept("gate.unpaired:1", null, "JD", DateTime.Now));
    }

    [Fact]
    public void ManyItemsCanBeMarkedNotApplicableWithOneReason()
    {
        var r = new ReviewRegister();
        r.Report(ReviewKinds.PointsSource, Enumerable.Range(1, 5).Select(i => ReviewKinds.New(ReviewKinds.PointNoSymbol, i.ToString(), "x")), DateTime.Now);
        Assert.Equal(0, r.MarkNotApplicable(r.Items.Select(i => i.Id), " ", "JD", DateTime.Now, out var problem));
        Assert.NotNull(problem);
        Assert.Equal(5, r.MarkNotApplicable(r.Items.Select(i => i.Id), "symbols from the utility base", "JD", DateTime.Now, out _));
        Assert.All(r.Items, i => Assert.Equal(ReviewItemStatus.NotApplicable, i.Status));
    }

    [Fact]
    public void QcCompleteWaitsForRequiredPointAndLineworkItems()
    {
        var r = new ReviewRegister();
        r.Report(ReviewKinds.PointsSource, new[] { ReviewKinds.New(ReviewKinds.PointMissingOutput, "11", "label missing") }, DateTime.Now, true, new ReviewSettings());
        r.Report(ReviewKinds.ContinuitySource, new[] { Gap("A") }, DateTime.Now, true, new ReviewSettings());
        Assert.False(r.MarkQcComplete(new ReviewSettings(), "JD", DateTime.Now, out var problem));
        Assert.Contains("2 required review item(s)", problem);
        r.MarkNotApplicable("point.missingoutput:11", "labelled on the sheet", "JD", DateTime.Now, out _);
        r.MarkCorrected("line.gap:FNC:103-105", "2F1", null, "JD", DateTime.Now, out _);
        Assert.True(r.MarkQcComplete(new ReviewSettings(), "JD", DateTime.Now, out _));
    }

    [Fact]
    public void TheNewKindsAreRequiredByDefaultExceptSourceChanged()
    {
        foreach (var k in new[] { ReviewKinds.PointMissingOutput, ReviewKinds.PointMissingLinework, ReviewKinds.PointNoSymbol, ReviewKinds.PointDuplicate,
                                  ReviewKinds.PointExcludedNoReason, ReviewKinds.PointDeleted, ReviewKinds.LineGap, ReviewKinds.LineMissingVertex, ReviewKinds.LineMissingSegment })
        {
            Assert.True(ReviewKinds.Get(k)!.Required, k);
            Assert.True(ReviewKinds.Get(k)!.NeedsNote, k);
        }
        Assert.False(ReviewKinds.Get(ReviewKinds.PointSourceChanged)!.Required);
    }

    [Fact]
    public void AnOlderRegisterWithoutTheNewFieldsStillLoads()
    {
        var json = "{\"version\":1,\"items\":[{\"id\":\"gate.unpaired:1\",\"kind\":\"gate.unpaired\",\"status\":\"NotApplicable\",\"note\":\"lone post\"}]}";
        var r = ReviewRegister.FromJson(json);
        Assert.Equal(ReviewItemStatus.NotApplicable, r.Items[0].Status);
        r.Report("Gates", new[] { ReviewKinds.New(ReviewKinds.GateUnpaired, "1", "x") }, DateTime.Now);
        Assert.Equal(ReviewItemStatus.NotApplicable, r.Items[0].Status);    // no fingerprint recorded: the decision stands
    }
}

/// <summary>Ignore lists that document why: entries as codes or as objects with a reason.</summary>
public sealed class IgnoreReasonTests : IClassFixture<RulesFixture>
{
    private readonly RulesFixture _fx;
    public IgnoreReasonTests(RulesFixture fx) => _fx = fx;

    [Fact]
    public void TheShippedRulesDocumentTheirExclusions()
    {
        Assert.Contains("GS", _fx.Config.IgnoreCodes);
        Assert.Equal("surface", _fx.Config.IgnoreNoteFor("GS12")!.Expect);
        Assert.True(_fx.Config.IgnoreNoteFor("NOTE")!.HasReason);
        Assert.False(_fx.Config.IgnoreNoteFor("ZK")!.HasReason);           // left for the office to document
        Assert.Null(_fx.Config.IgnoreNoteFor("FNC"));
        Assert.True(_fx.Parse("GS", "1").Ignored);                            // still never drawn
    }

    [Fact]
    public void ListsReadAndWriteBothForms()
    {
        var json = "{\"ignoredCodes\":[\"XYZ\",{\"code\":\"TEMP\",\"reason\":\"temporary marks\",\"by\":\"JD\"}]}";
        var d = JsonConvert.DeserializeObject<DiagnosticsSettings>(json)!;
        Assert.Equal(new[] { "XYZ", "TEMP" }, d.Codes());
        Assert.Equal("temporary marks", d.NoteFor("TEMP2")!.Reason);
        Assert.False(d.NoteFor("XYZ")!.HasReason);
        var written = JsonConvert.SerializeObject(d);
        Assert.Contains("\"XYZ\"", written);
        Assert.Contains("\"reason\":\"temporary marks\"", written);
        var back = JsonConvert.DeserializeObject<DiagnosticsSettings>(written)!;
        Assert.Equal("JD", back.NoteFor("TEMP")!.By);
    }

    [Fact]
    public void IgnoringFromTheWindowRecordsWhyWhoAndWhen()
    {
        var d = new DiagnosticsSettings();
        Assert.True(d.Ignore("qq1", "crew's practice shots", "JD", new DateTime(2026, 11, 3)));
        var note = d.NoteFor("QQ1")!;
        Assert.Equal("crew's practice shots", note.Reason);
        Assert.Equal("JD", note.By);
        Assert.Contains("\"code\":\"QQ1\"", JsonConvert.SerializeObject(d));
    }
}
