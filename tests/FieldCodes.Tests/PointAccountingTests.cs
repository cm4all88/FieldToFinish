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
        // A post: its standard (the default) takes the label its rule writes.
        var labelled = P("10", "POST 12");
        labelled.Outputs.Add(new OutputFact { Kind = "Label", Handle = "L10" });
        var bare = P("11", "POST 13");                      // its label is not in the drawing
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
        Assert.Contains("POST 13", item.Reason);
        Assert.True(ReviewKinds.Get(item.Kind)!.Required);
    }

    [Fact]
    public void A11_ALabelTheSettingsTurnOffIsNotExpected()
    {
        var settings = new FtfSettings();
        settings.Annotation.Categories = new List<AnnotationCategory>();
        var pole = P("12", "PP 1236");
        var a = Account(null, new[] { pole }, Ctx(settings));
        var e = a.ledger.Find("12")!;
        Assert.DoesNotContain("label", e.Expected);
        Assert.Contains(e.Evidence, x => x.StartsWith("no label expected"));
        Assert.Equal(PointDisposition.PointStyle, e.Disposition);   // its point style shows it
        Assert.Empty(a.items);
    }

    [Fact]
    public void A7_ABareCodeIsShownByItsPointStyleUnlessItsStandardRequiresItsOwnSymbol()
    {
        // SSCO: no standard of its own (Storm and sewer family): any visible point style marker.
        var generic = P("20", "SSCO");
        var hidden = P("19", "SSCO");
        hidden.Marker = new MarkerFact { Known = true, Visible = false, MarkerType = "UseCustomMarker", StyleName = "Hidden" };
        // WFH: the hydrant standard requires the hydrant's own symbol.
        var hydrantGeneric = P("25", "WFH");
        var own = P("21", "WFH");
        own.Marker = Symbol("PMX-WFH");
        var other = P("22", "WFH");
        other.Marker = Symbol("PMX-CB");
        var none = P("23", "WFH");
        none.Marker = new MarkerFact { Known = true, Visible = false, MarkerType = "UseSymbolForMarker", SymbolName = "PMX-WFH", StyleName = "Hidden" };
        Assert.True(generic.Parsed.NoContent);
        var a = Account(null, new[] { generic, hidden, hydrantGeneric, own, other, none }, Ctx());

        Assert.Equal(PointDisposition.PointStyle, a.ledger.Find("20")!.Disposition);
        Assert.DoesNotContain(a.items, i => i.Id == "point.nosymbol:20");
        Assert.Contains("draws no marker", a.items.Single(i => i.Id == "point.nosymbol:19").Reason);
        Assert.Contains("generic custom marker", a.items.Single(i => i.Id == "point.nosymbol:25").Reason);
        Assert.Contains("Fire hydrant", a.items.Single(i => i.Id == "point.nosymbol:25").Reason);
        Assert.Equal(PointDisposition.Symbol, a.ledger.Find("21")!.Disposition);
        Assert.Contains("block PMX-CB", a.items.Single(i => i.Id == "point.nosymbol:22").Reason);
        Assert.Contains("draws no marker", a.items.Single(i => i.Id == "point.nosymbol:23").Reason);
        Assert.False(a.ledger.Find("23")!.Represented);
        Assert.All(a.items.Where(i => i.Kind == ReviewKinds.PointNoSymbol), i => Assert.True(a.register.Find(i.Id)!.Required));
    }

    [Fact]
    public void ASymbolNamedInSettingsCounts()
    {
        var settings = new FtfSettings();
        settings.Accountability.SymbolBlocks["WFH"] = new List<string> { "HYDRANT*" };
        var p = P("24", "WFH");
        p.Marker = Symbol("HYDRANT-STD");
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

    private (PointFacts inDefinition, PointFacts coincident, PointFacts leftOut, PointFacts nowhere) GroundShots()
    {
        var inDefinition = P("50", "GS");
        inDefinition.Surfaces.Add(new SurfaceFact { Surface = "EG", InDefinition = true, PointGroup = "TOPO", IsVertex = true });
        var coincident = P("51", "GS2");
        coincident.Surfaces.Add(new SurfaceFact { Surface = "EG", InDefinition = null, IsVertex = true });
        var leftOut = P("52", "GS");
        leftOut.Surfaces.Add(new SurfaceFact { Surface = "EG", InDefinition = true, PointGroup = "TOPO", IsVertex = false });
        var nowhere = P("53", "GS");
        return (inDefinition, coincident, leftOut, nowhere);
    }

    [Fact]
    public void A9_GroundShotsAreSurfaceDataWithOrWithoutASurface()
    {
        // Surface membership is not checked by default: every GS is designated surface data, with
        // or without a surface in the drawing, and never a review item.
        var (inDefinition, coincident, leftOut, nowhere) = GroundShots();
        var a = Account(null, new[] { inDefinition, coincident, leftOut, nowhere }, Ctx());
        foreach (var n in new[] { "50", "51", "52", "53" })
        {
            var e = a.ledger.Find(n)!;
            Assert.Equal(PointDisposition.Surface, e.Disposition);
            Assert.Equal("designated", e.SurfaceStatus);
            Assert.Equal("Surface only", e.Classification);
            Assert.True(e.DraftingComplete);
            Assert.Equal("GS", e.RawDescription.Split(' ')[0].TrimEnd('2'));       // the description as shot
        }
        Assert.Empty(a.items);
        Assert.DoesNotContain(a.ledger.Find("53")!.Expected, x => x == "symbol" || x == "label" || x == "surface");
    }

    [Fact]
    public void A9_SurfaceMembershipIsAnOptionalCheckAndNeverADraftingError()
    {
        var settings = new FtfSettings();
        settings.Accountability.VerifySurfaceMembership = true;
        var (inDefinition, coincident, leftOut, nowhere) = GroundShots();
        var a = Account(null, new[] { inDefinition, coincident, leftOut, nowhere }, Ctx(settings));

        Assert.Equal("verified", a.ledger.Find("50")!.SurfaceStatus);
        Assert.Contains(a.ledger.Find("50")!.Evidence, e => e.Contains("in its definition") && e.Contains("TOPO"));
        Assert.Equal("verified", a.ledger.Find("51")!.SurfaceStatus);
        Assert.Contains(a.ledger.Find("51")!.Evidence, e => e.Contains("coordinates only"));
        Assert.Equal("excluded", a.ledger.Find("52")!.SurfaceStatus);
        var excluded = a.register.Find("point.surfaceexcluded:52")!;
        Assert.Contains("triangulation leaves it out", excluded.Reason);
        Assert.Equal(ReviewSeverity.Warning, excluded.Severity);
        Assert.False(excluded.Required);
        Assert.Equal("not verifiable", a.ledger.Find("53")!.SurfaceStatus);
        var unverified = a.register.Find("point.surfaceunverified:GS")!;
        Assert.Equal("53", unverified.Points);
        Assert.Equal(ReviewSeverity.Info, unverified.Severity);
        Assert.False(unverified.Required);
        // Accounted for as surface data whatever the check says; nothing for the required queue.
        Assert.All(new[] { "50", "51", "52", "53" }, n => Assert.Equal(PointDisposition.Surface, a.ledger.Find(n)!.Disposition));
        Assert.Empty(a.register.RequiredOpen);
    }

    [Fact]
    public void A9_ZkIsAControlCheckShotAndNotesAreExcludedWithTheirReason()
    {
        var control = P("60", "FIP 5/8", 0, 700, 100);
        var check = P("61", "ZK", 0.03, 700.04, 100.02);
        var far = P("62", "ZK", 50, 900, 100);
        var noted = P("55", "NOTE");
        var a = Account(null, new[] { control, check, far, noted }, Ctx());
        var zk = a.ledger.Find("61")!;
        Assert.Equal(PointDisposition.Control, zk.Disposition);
        Assert.Equal("Control/Check", zk.Classification);
        Assert.Contains("control check", zk.Found);
        Assert.DoesNotContain("label", zk.Expected);
        Assert.DoesNotContain("symbol", zk.Expected);
        Assert.DoesNotContain(a.items, i => i.Points == "61" || i.Points == "62");
        Assert.Equal(PointDisposition.Excluded, a.ledger.Find("55")!.Disposition);
        Assert.Contains(a.ledger.Find("55")!.Evidence, e => e.Contains("Field note shot"));

        var csv = FieldCodes.Reporting.AccountabilityReport.RenderControlChecks(a.ledger, 2.0, 1.0).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(FieldCodes.Reporting.AccountabilityReport.ControlChecksHeader, csv[0]);
        Assert.Equal(3, csv.Length);
        var paired = csv.Single(l => l.StartsWith("61,"));
        Assert.Contains(",60,FIP 5/8,0.040,0.030,0.050,0.020,", paired);
        Assert.Contains("no control point within 2 ft", csv.Single(l => l.StartsWith("62,")));
        Assert.All(csv, l => Assert.Equal(12, l.Split(',').Length));
    }

    [Fact]
    public void AnIgnoredCodeWithoutAReasonIsAWarningNotARequiredAction()
    {
        // CONS: on the shipped ignore list without a reason, and no standard of its own.
        var a = Account(null, new[] { P("54", "CONS") }, Ctx());
        var item = a.register.Find("point.excludednoreason:54")!;
        Assert.False(item.Required);
        Assert.Equal(ReviewSeverity.Warning, item.Severity);
        Assert.NotEqual(PointDisposition.Review, a.ledger.Find("54")!.Disposition);
        var lenient = new FtfSettings();
        lenient.Accountability.RequireIgnoreReasons = false;
        Assert.DoesNotContain(Account(null, new[] { P("54", "CONS") }, Ctx(lenient)).items, i => i.Kind == ReviewKinds.PointExcludedNoReason);
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
        var first = Account(null, new[] { P("80", "WFH") }, Ctx(), register);
        Assert.True(register.MarkNotApplicable("point.nosymbol:80", "hydrant shown by the utility plan", "JD", DateTime.Now, out _));
        var again = Account(PointLedger.FromJson(first.ledger.ToJson()), new[] { P("80", "WFH") }, Ctx(), register);
        Assert.Equal(ReviewItemStatus.NotApplicable, register.Find("point.nosymbol:80")!.Status);
        Assert.Equal(PointDisposition.Excluded, again.ledger.Find("80")!.Disposition);   // documented by a person

        var moved = P("80", "WFH", 800.5, 0);
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
        var post = P("90", "POST 7");
        post.Outputs.Add(new OutputFact { Kind = "Label", Handle = "LB" });
        var a = Account(null, new[] { post, P("91", "WFH"), P("92", "NOTE") }, Ctx());
        Assert.Equal(3, a.run.Points);
        Assert.Equal(2, a.run.Accounted);
        Assert.Equal(1, a.run.Review);
        Assert.Equal(1, a.run.ByDisposition["Drawn"]);
        Assert.Contains("3 points: 2 accounted for", a.run.Describe());

        var csv = FieldCodes.Reporting.AccountabilityReport.RenderPoints(a.ledger);
        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.StartsWith("Point,Handle,RawDescription", lines[0]);
        Assert.Contains(lines, l => l.StartsWith("91,H91,WFH,") && l.Contains(",Review,") && l.Contains("Fire hydrant"));
        Assert.Contains(lines, l => l.Contains("Summary: 3 points"));
        Assert.All(lines.Skip(1).Where(l => !l.Contains("Summary:")), l => Assert.Equal(FieldCodes.Reporting.AccountabilityReport.PointsHeader.Split(',').Length, CsvFields(l).Count));
        var header = FieldCodes.Reporting.AccountabilityReport.PointsHeader.Split(',').ToList();
        var hydrant = CsvFields(lines.Single(l => l.StartsWith("91,")));
        Assert.Equal("Utility", hydrant[header.IndexOf("Class")]);
        Assert.StartsWith("Fire hydrant (WFH, FH)", hydrant[header.IndexOf("Standard")]);
        Assert.Contains("its own symbol", hydrant[header.IndexOf("Needs")]);
    }

    /// <summary>One CSV line into fields, honouring quotes.</summary>
    internal static List<string> CsvFields(string line)
    {
        var fields = new List<string>();
        var sb = new System.Text.StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (quoted)
            {
                if (ch == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                else if (ch == '"') quoted = false;
                else sb.Append(ch);
            }
            else if (ch == '"') quoted = true;
            else if (ch == ',') { fields.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(ch);
        }
        fields.Add(sb.ToString());
        return fields;
    }

    [Fact]
    public void CodesTheOfficeIgnoresAreExcludedWithTheirReason()
    {
        var settings = new FtfSettings();
        settings.Diagnostics.Ignore("XYZQ", "crew's practice shots", "JD", null);
        settings.Diagnostics.Ignore("QQQ");
        // The parser here has not had the settings applied: the ledger must not depend on it.
        var a = Account(null, new[] { P("1", "XYZQ 12"), P("2", "XYZQ"), P("3", "QQQ 4") }, Ctx(settings));
        Assert.Equal(PointDisposition.Excluded, a.ledger.Find("1")!.Disposition);
        Assert.Equal(PointDisposition.Excluded, a.ledger.Find("2")!.Disposition);
        Assert.Contains(a.ledger.Find("1")!.Evidence, e => e.Contains("crew's practice shots"));
        Assert.Contains(a.items, i => i.Id == "point.excludednoreason:3");
        Assert.DoesNotContain(a.items, i => i.Id == "point.nosymbol:2");
    }

    [Fact]
    public void APointNothingAccountsForAlwaysHasAnItem()
    {
        var hidden = P("1", "SSCO");
        hidden.Marker = new MarkerFact { Known = false, Problem = "style unreadable" };
        var a = Account(null, new[] { hidden, P("2", "WFH"), P("3", "XYZQ 12") }, Ctx());
        var e = a.ledger.Find("1")!;
        // A marker FTF could not read is not reported missing, and not passed either: it is
        // unverified, on a required item a person can close.
        Assert.Equal(PointDisposition.Unverified, e.Disposition);
        Assert.False(e.Accounted);
        var item = a.register.Find("point.symbolunverified:SSCO")!;
        Assert.True(item.Required);
        Assert.Contains("style unreadable", item.Reason);
        Assert.Contains("point.symbolunverified:SSCO", e.Issues);         // something a person can close
        Assert.DoesNotContain(a.items, i => i.Id == "point.nosymbol:1");
        Assert.All(a.ledger.Points.Where(p => p.Disposition == PointDisposition.Review), p => Assert.NotEmpty(p.Issues));
    }

    [Fact]
    public void AShotOnTwoFiguresNeedsALineOfEach()
    {
        var shots = new[] { P("1", "EP B FNC B", 0, 0), P("2", "EP E FNC E", 10, 0) };
        var input = new ContinuityInput { Shots = shots.Select(s => new ContinuityShot { Number = s.Number, X = s.X, Y = s.Y, RawDescription = s.RawDescription, Coding = s.Coding }).ToList() };
        var line = new ContinuityLine { Handle = "EP1" };
        foreach (var s in shots) { line.Vertices.Add(new[] { s.X, s.Y, 0.0 }); line.ShotsAtVertex.Add(new List<string> { s.Number }); }
        line.FeatureCode = "EP";
        // The line's family comes from its shots: both EP and FNC are coded there, so make it EP's by name.
        line.FigureName = "EP";
        input.Lines = new List<ContinuityLine> { line };
        var continuity = LineContinuity.Analyze(input);
        var a = Account(null, shots, Ctx(continuity: continuity));
        var e = a.ledger.Find("1")!;
        Assert.Contains("linework EP", e.Found);
        Assert.Contains("linework FNC", e.Missing);
        Assert.Contains(e.Issues, id => id.StartsWith("line.missingsegment:FNC:"));
        Assert.DoesNotContain(e.Issues, id => id.StartsWith("line.") && id.Contains(":EP:"));
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
    public void AReopenedCorrectionWaitsForAPersonAndIsNeverResolvedByARerun()
    {
        var r = new ReviewRegister();
        var t = DateTime.Now;
        r.Report(ReviewKinds.ContinuitySource, new[] { Gap("A") }, t);
        r.MarkCorrected("line.gap:FNC:103-105", "2F1", null, "JD", t, out _);
        r.Report(ReviewKinds.ContinuitySource, new ReviewItem[0], t);        // the drafter joined the fence by hand
        Assert.Equal(ReviewItemStatus.Open, r.Items[0].Status);
        r.Report(ReviewKinds.ContinuitySource, new ReviewItem[0], t);        // FTFQC reruns the checks
        Assert.Equal(ReviewItemStatus.Open, r.Items[0].Status);              // still the person's to decide
        Assert.False(r.MarkQcComplete(new ReviewSettings(), "JD", t, out _));
        Assert.True(r.Accept("line.gap:FNC:103-105", "joined by hand; correction 2F1 erased", "JD", t, out _));
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
    public void ItemKindsAreRequiredOnlyForMissingOrWrongDeliverables()
    {
        // Required action: something the plan must have is missing or wrong.
        foreach (var k in new[] { ReviewKinds.PointMissingOutput, ReviewKinds.PointMissingLinework, ReviewKinds.PointNoSymbol, ReviewKinds.PointDeleted,
                                  ReviewKinds.PointReview, ReviewKinds.LineGap, ReviewKinds.LineMissingVertex, ReviewKinds.LineMissingSegment })
        {
            Assert.True(ReviewKinds.Get(k)!.Required, k);
            Assert.True(ReviewKinds.Get(k)!.NeedsNote, k);
        }
        // Warning: a possible deficiency to look at, not in the required queue.
        foreach (var k in new[] { ReviewKinds.PointDuplicate, ReviewKinds.PointExcludedNoReason, ReviewKinds.PointSurfaceExcluded })
        {
            Assert.False(ReviewKinds.Get(k)!.Required, k);
            Assert.Equal(ReviewSeverity.Warning, ReviewKinds.Get(k)!.Severity);
        }
        // Information: accounted for, nothing to draft.
        foreach (var k in new[] { ReviewKinds.PointSourceChanged, ReviewKinds.PointSurfaceUnverified })
        {
            Assert.False(ReviewKinds.Get(k)!.Required, k);
            Assert.Equal(ReviewSeverity.Info, ReviewKinds.Get(k)!.Severity);
        }
        Assert.Equal(ReviewSeverity.Error, ReviewKinds.Get(ReviewKinds.PointMissingOutput)!.Severity);
        Assert.Equal(ReviewSeverity.Error, ReviewKinds.Get(ReviewKinds.PointNoSymbol)!.Severity);
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
        Assert.Contains("Control check shot", _fx.Config.IgnoreNoteFor("ZK")!.Reason);   // what the drafting standard says it is
        Assert.False(_fx.Config.IgnoreNoteFor("CONS")!.HasReason);         // left for the office to document
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
