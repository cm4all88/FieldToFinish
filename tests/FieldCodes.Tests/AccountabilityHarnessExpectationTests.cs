using System.Text.RegularExpressions;
using FieldCodes.Linework;
using FieldCodes.Review;
using FieldCodes.Settings;

namespace FieldCodes.Tests;

/// <summary>
/// The Civil 3D accountability check (tests/LiveSmoke/AccountabilityTestHarness.cs) seeds 38 synthetic
/// points, 7 polylines and a TIN, then expects particular review items and ledger dispositions after
/// FTFRUN, after a drafting standard is added and taken away, and with surface verification on. These tests run the same seed through the same library code (continuity, then the
/// ledger), so a wrong expectation fails here rather than in Civil 3D. They do not replace it.
/// </summary>
public sealed class AccountabilityHarnessExpectationTests : IClassFixture<RulesFixture>
{
    private readonly RulesFixture _fx;
    public AccountabilityHarnessExpectationTests(RulesFixture fx) => _fx = fx;

    // As ACCSEED.
    private static readonly (string N, double X, double Y, double Z, string D)[] Seed =
    {
        ("101", 0, 0, 100, "FNC B"), ("102", 20, 0, 100, "FNC"), ("103", 40, 0, 100, "FNC"), ("104", 45, 30, 100, "PP"),
        ("105", 48, 0, 100, "FNC B"), ("106", 68, 0, 100, "FNC"), ("107", 88, 0, 100, "FNC E"),
        ("201", 0, 100, 100, "FNC B"), ("202", 20, 100, 100, "FGP FNC"), ("203", 26, 100, 100, "FGP FNC B"), ("204", 46, 100, 100, "FNC E"),
        ("401", 0, 200, 100, "FNC1 B"), ("402", 20, 200, 100, "FNC1 E"), ("403", 23, 200, 100, "FNC2 B"), ("404", 43, 200, 100, "FNC2 E"),
        ("601", 0, 300, 100, "SSCO"), ("602", 0.004, 300, 100, "SSCO"),
        ("701", 0, 400, 100, "XYZQ 12"), ("702", 30, 400, 100, "WFH"),
        ("801", 0, 500, 100, "EP B"), ("802", 10, 500, 100, "EP"), ("803", 20, 500, 100, "EP"), ("804", 30, 500, 100, "EP"), ("805", 40, 500, 100, "EP E"),
        ("901", 0, 600, 100, "GS"), ("902", 10, 600, 101, "GS"), ("903", 0, 610, 102, "GS"), ("904", 30, 600, 100, "ZK"), ("905", 100, 650, 100, "GS"),
        ("1001", 0, 700, 100, "FIP 5/8"), ("1002", 0.03, 700.04, 100.02, "ZK"), ("1101", 0, 800, 100, "WFH 12"),
        ("1201", 0, 900, 100, "FNC B"), ("1202", 10, 903, 100, "FNC"), ("1203", 20, 900, 100, "FNC E"),
        ("1301", 0, 1000, 100, "HOUSE NUMBER 4735 SIGN"), ("1302", 0, 1010, 100, "HOUSE NUMBER 4737 SIGN"), ("1401", 0, 1100, 100, "WFH 14"),
    };

    // 1401's point style draws a hydrant symbol block (FTF-SMOKE-WFH); 1302's label is turned off;
    // every other point has the drawing's generic marker and its default point label shown -- read
    // in full, as Accountability.ReadStyle and CogoPointPlotting.ReadLabel now read them.
    private static MarkerFact MarkerOf(string number) => number switch
    {
        "1401" => new MarkerFact { Known = true, Visible = true, MarkerType = "UseSymbolForMarker", SymbolName = "FTF-SMOKE-WFH", StyleName = "FTF-SMOKE-WFH", LabelShown = true, LabelStyleName = "Standard" },
        "1302" => new MarkerFact { Known = true, Visible = true, MarkerType = "UseCustomMarker", SymbolName = string.Empty, StyleName = "Standard", LabelShown = false, LabelStyleName = null, LabelProblem = "the point's label is turned off" },
        _ => new MarkerFact { Known = true, Visible = true, MarkerType = "UseCustomMarker", SymbolName = string.Empty, StyleName = "Standard", LabelShown = true, LabelStyleName = "Standard" }
    };

    private static readonly string[][] Lines =
    {
        new[] { "101", "102", "103" }, new[] { "105", "106", "107" }, new[] { "201", "202" }, new[] { "203", "204" },
        new[] { "401", "402" }, new[] { "403", "404" }, new[] { "1201", "1203" },
    };

    /// <summary>ACCSTANDARD Label: the drafting standard the harness adds to the rules beside the drawing.</summary>
    private const string CleanoutStandard = @"{ ""requirements"": [ { ""name"": ""Cleanout"", ""codes"": [""SSCO""], ""label"": ""Required"" } ] }";

    /// <summary>ACCSURFACE On: the ftf-settings.json the harness writes beside the drawing.</summary>
    private const string SurfaceSettings = @"{ ""version"": ""1"", ""accountability"": { ""verifySurfaceMembership"": true } }";

    private static readonly string[] GroundAndCheckShots = { "901", "902", "903", "904", "905", "1002" };

    /// <summary>As FTFRUN or FTFREVIEWCHECK on the harness drawing: continuity, the ledger, then the
    /// code check, reported into one register (as Accountability.Run).</summary>
    private (ReviewRegister register, PointLedger ledger) Check(ReviewRegister? register = null, PointLedger? previous = null,
                                                             RulesConfig? rules = null, FtfSettings? settings = null, DateTime? at = null)
    {
        rules ??= _fx.Config;
        settings ??= new FtfSettings();
        var now = at ?? new DateTime(2026, 11, 3, 12, 0, 0);
        var parser = new FieldCodeParser(rules);
        var shots = Seed.Select(s => new ContinuityShot { Number = s.N, Handle = "H" + s.N, RawDescription = s.D, X = s.X, Y = s.Y, Z = s.Z, Coding = LineworkCoding.Parse(s.D, rules) }).ToList();
        var lines = Lines.Select((through, i) =>
        {
            var l = new ContinuityLine { Handle = "L" + i, Layer = "V-SURF-FENC-E", EntityType = "Polyline" };
            foreach (var n in through)
            {
                var s = Seed.First(x => x.N == n);
                l.Vertices.Add(new[] { s.X, s.Y, 0.0 });
                l.ShotsAtVertex.Add(Seed.Where(x => Math.Abs(x.X - s.X) <= 0.01 && Math.Abs(x.Y - s.Y) <= 0.01).Select(x => x.N).ToList());
            }
            return l;
        }).ToList();
        var gates = new HashSet<string>(Seed.Where(s => s.D.Split(' ').Any(w => w == "FGP")).Select(s => s.N));
        var continuity = LineContinuity.Analyze(new ContinuityInput { Shots = shots, Lines = lines, GatePosts = gates, Settings = settings.Accountability.Continuity, UnitsPerFoot = 1.0 });
        register ??= new ReviewRegister();
        register.Report(ReviewKinds.ContinuitySource, continuity.ReviewItems(shots.ToDictionary(s => s.Number)), now, true, settings.Review);

        var facts = Seed.Select(s =>
        {
            var p = new PointFacts
            {
                Number = s.N, Handle = "H" + s.N, RawDescription = s.D, X = s.X, Y = s.Y, Z = s.Z,
                Parsed = parser.Parse(s.N, s.D), Coding = LineworkCoding.Parse(s.D, rules), Marker = MarkerOf(s.N)
            };
            // The TIN is built from vertices, not point groups: coincidence only.
            if (s.N is "901" or "902" or "903") p.Surfaces.Add(new SurfaceFact { Surface = "FTF-SMOKE-GS", InDefinition = false, IsVertex = true });
            return p;
        }).ToList();
        var ctx = new AccountingContext
        {
            Rules = rules, Settings = settings, Diagnostics = new CodeDiagnostics(rules, null), Continuity = continuity,
            FamilyOf = c => c, UnitsPerFoot = 1.0, GatePostsLabelled = new HashSet<string>()
        };
        var accounting = PointAccounting.Build(previous, facts, ctx, now);
        register.Report(ReviewKinds.PointsSource, accounting.Items, now, true, settings.Review);
        // The code check's items, as ReviewChecks.Codes raises them.
        register.Report(ReviewChecks.CodesSource, ReviewChecks.Codes(Seed.Select(s => new ReviewPoint { Number = s.N, Description = s.D, Parsed = parser.Parse(s.N, s.D) }), new CodeDiagnostics(rules, null)), now);
        PointAccounting.Finish(accounting.Ledger, register, now, "test");
        return (register, accounting.Ledger);
    }

    private static bool Names(ReviewItem i, string number) =>
        i.Id.EndsWith(":" + number, StringComparison.Ordinal) ||
        (i.Points ?? string.Empty).Split(',').Select(x => x.Trim()).Contains(number);

    [Fact]
    public void TheItemsTheHarnessExpectsAfterFtfRun()
    {
        var (register, _) = Check();
        ReviewItem? Find(string id) => register.Find(id);
        var gap = Find("line.gap:FNC:103-105")!;
        Assert.Equal("probable", gap.Confidence);
        Assert.True(gap.Required);
        Assert.Equal(2, gap.Geometry!.Count);
        Assert.DoesNotContain(register.Items, i => i.Id.StartsWith("line.gap:FNC:20"));
        Assert.DoesNotContain(register.Items, i => i.Id.Contains("FNC1") || i.Id.Contains("FNC2"));
        foreach (var id in new[] { "point.duplicate:601", "point.duplicate:602", "code.unknown:XYZQ", "point.nosymbol:702", "point.nosymbol:1101",
                                   "line.missingsegment:EP:801-805", "line.missingvertex:FNC:1201-1202-1203" })
            Assert.True(Find(id) != null, id);
        // Duplicates are a warning to look at; a hydrant without its symbol is a required action.
        Assert.False(Find("point.duplicate:601")!.Required);
        Assert.True(Find("point.nosymbol:702")!.Required);
        Assert.True(Find("point.nosymbol:1101")!.Required);
        // The hydrant needs no text label: none expected, none drawn by FTFRUN.
        Assert.Null(Find("point.missingoutput:1101"));
        Assert.False(FieldCodes.Annotation.AnnotationPolicy.Label(_fx.Parse("WFH 12"), new FieldCodes.Settings.AnnotationSettings(), null, _fx.Config).Annotate);
        // Ground shots and control check shots are never a required action; 905 (off the TIN) is
        // not on the list at all while surface verification is off.
        Assert.DoesNotContain(register.Items, i => i.Required && GroundAndCheckShots.Any(n => Names(i, n)));
        Assert.DoesNotContain(register.Items, i => Names(i, "905") || Names(i, "904") || Names(i, "1002"));
        // The house number sign is not a code nothing handles, and the hydrant with its symbol has no item.
        Assert.Null(Find("code.unknown:HOUSE"));
        Assert.DoesNotContain(register.Items, i => Names(i, "1301") || Names(i, "1401"));
        Assert.NotNull(Find("point.missingoutput:1302"));
        var withGeometry = register.Items.Count(i => i.IsOpen && i.Source == ReviewKinds.ContinuitySource && i.Geometry != null && i.Geometry.Count >= 2);
        Assert.True(withGeometry >= 3);
    }

    [Fact]
    public void TheDispositionsTheHarnessExpects()
    {
        var (register, ledger) = Check();
        Assert.Equal(38, ledger.Points.Count);
        foreach (var n in new[] { "901", "902", "903", "905" })
        {
            Assert.Equal(PointDisposition.Surface, ledger.Find(n)!.Disposition);
            Assert.Equal("designated", ledger.Find(n)!.SurfaceStatus);
        }
        Assert.Equal(PointDisposition.Control, ledger.Find("1001")!.Disposition);
        foreach (var n in new[] { "904", "1002" })
        {
            Assert.Equal(PointDisposition.Control, ledger.Find(n)!.Disposition);
            Assert.Equal("control check ZK", ledger.Find(n)!.Reading);
            Assert.Equal("Control/Check", ledger.Find(n)!.Classification);
        }
        Assert.Equal(PointDisposition.PointStyle, ledger.Find("601")!.Disposition);
        Assert.Equal(PointDisposition.Linework, ledger.Find("103")!.Disposition);
        Assert.Equal(PointDisposition.Linework, ledger.Find("105")!.Disposition);
        Assert.Equal(PointDisposition.Review, ledger.Find("803")!.Disposition);
        Assert.Contains("line.missingsegment:EP:801-805", ledger.Find("803")!.Issues);
        Assert.Equal(PointDisposition.Review, ledger.Find("1101")!.Disposition);
        Assert.Equal(PointDisposition.Symbol, ledger.Find("1401")!.Disposition);
        var sign = ledger.Find("1301")!;
        Assert.Equal("HOUSE NUMBER 4735 SIGN", sign.RawDescription);
        Assert.Equal("Sign (address / house number)", sign.Classification);
        Assert.Equal(PointDisposition.PointStyle, sign.Disposition);
        Assert.Contains("label (point label)", sign.Found);
        var hidden = register.Find("point.missingoutput:1302")!;
        Assert.True(hidden.Required);
        Assert.Contains("label is hidden", hidden.Reason);
        Assert.Equal("HOUSE NUMBER 4737 SIGN", ledger.Find("1302")!.RawDescription);
        Assert.Equal(PointDisposition.Review, ledger.Find("1302")!.Disposition);
        // Every style and label read in full: nothing unverified, no marker evidence "not read".
        Assert.DoesNotContain(register.Items, i => ReviewKinds.IsUnverified(i.Kind));
        Assert.DoesNotContain(ledger.Points, p => p.Evidence.Any(x => x.StartsWith("marker:") && x.Contains("not read")));
        Assert.Contains(ledger.Find("702")!.Evidence, x => x.StartsWith("marker: Standard ("));
        var run = ledger.LastRun!;
        Assert.Equal(38, run.Points);
        Assert.True(run.Review > 0 && run.Accounted > 0);
        Assert.False(register.MarkQcComplete(new ReviewSettings(), "x", DateTime.Now, out _));

        // The control check report pairs 1002 with 1001 and says 904 has no control point near it.
        var csv = FieldCodes.Reporting.AccountabilityReport.RenderControlChecks(ledger, 2.0, 1.0);
        var rows = csv.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        Assert.Contains(rows, r => r.StartsWith("1002,ZK,") && r.Contains(",1001,FIP 5/8,"));
        Assert.Contains(rows, r => r.StartsWith("904,ZK,") && r.Contains("no control point within"));
    }

    [Fact]
    public void TheStandardStageRequiresTheCleanoutLabelAndRestoringItResolvesIt()
    {
        var (register, ledger) = Check(at: new DateTime(2026, 11, 3, 12, 0, 0));
        var before = register.Items.Where(i => i.IsOpen && i.Source == ReviewKinds.PointsSource).Select(i => i.Id).OrderBy(x => x, StringComparer.Ordinal).ToList();

        // ACCSTANDARD Label, FTFREVIEWCHECK, ACCVERIFY STANDARD.
        var withStandard = RulesFixture.WithFeatureStandard(CleanoutStandard);
        var (_, standard) = Check(register, PointLedger.FromJson(ledger.ToJson()), withStandard, at: new DateTime(2026, 11, 3, 12, 5, 0));
        foreach (var n in new[] { "601", "602" })
        {
            var item = register.Find("point.missingoutput:" + n)!;
            Assert.Equal(ReviewItemStatus.Open, item.Status);
            Assert.True(item.Required);
            Assert.Contains("the standard requires a label", item.Reason);
            Assert.Contains("Cleanout (SSCO)", item.Reason);
            Assert.Equal(PointDisposition.Review, standard.Find(n)!.Disposition);
        }

        // ACCSTANDARD Restore, FTFREVIEWCHECK, ACCVERIFY RESTORED.
        var (_, restored) = Check(register, PointLedger.FromJson(standard.ToJson()), at: new DateTime(2026, 11, 3, 12, 10, 0));
        foreach (var n in new[] { "601", "602" })
        {
            var item = register.Find("point.missingoutput:" + n)!;
            Assert.Equal(ReviewItemStatus.Resolved, item.Status);
            Assert.Equal(new[] { "opened", "resolved" }, item.History!.Select(h => h.Action).ToArray());
            Assert.Equal(PointDisposition.PointStyle, restored.Find(n)!.Disposition);
        }
        var after = register.Items.Where(i => i.IsOpen && i.Source == ReviewKinds.PointsSource).Select(i => i.Id).OrderBy(x => x, StringComparer.Ordinal).ToList();
        Assert.Equal(before, after);
        Assert.Equal(register.Items.Count, register.Items.Select(i => i.Id).Distinct().Count());
    }

    [Fact]
    public void TheSurfaceStageVerifiesTheTinVerticesAndListsTheRestAsInformation()
    {
        var (register, ledger) = Check();
        // The settings file ACCSURFACE writes, read the way the plugin reads one beside a drawing.
        var folder = Path.Combine(Path.GetTempPath(), "ftf-acc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        FtfSettings on;
        try
        {
            File.WriteAllText(Path.Combine(folder, FtfSettings.FileName), SurfaceSettings);
            on = FtfSettings.Resolve(folder, null, _fx.Config).Settings;
        }
        finally { Directory.Delete(folder, true); }
        Assert.True(on.Accountability.VerifySurfaceMembership);

        // ACCSURFACE On, FTFREVIEWCHECK, ACCVERIFY SURFACE.
        var (_, verified) = Check(register, PointLedger.FromJson(ledger.ToJson()), settings: on, at: new DateTime(2026, 11, 3, 12, 5, 0));
        foreach (var n in new[] { "901", "902", "903" })
        {
            Assert.Equal("verified", verified.Find(n)!.SurfaceStatus);
            Assert.Equal(PointDisposition.Surface, verified.Find(n)!.Disposition);
        }
        Assert.Equal("not verifiable", verified.Find("905")!.SurfaceStatus);
        Assert.Equal(PointDisposition.Surface, verified.Find("905")!.Disposition);
        var unverified = register.Find("point.surfaceunverified:GS")!;
        Assert.Equal(ReviewItemStatus.Open, unverified.Status);
        Assert.False(unverified.Required);
        Assert.Equal(ReviewSeverity.Info, unverified.Severity);
        Assert.Equal("905", unverified.Points);
        Assert.DoesNotContain(register.Items, i => i.IsOpen && i.Required && GroundAndCheckShots.Any(n => Names(i, n)));

        // ACCSURFACE Off, FTFREVIEWCHECK, ACCVERIFY SURFACEOFF.
        var (_, off) = Check(register, PointLedger.FromJson(verified.ToJson()), at: new DateTime(2026, 11, 3, 12, 10, 0));
        Assert.Equal(ReviewItemStatus.Resolved, register.Find("point.surfaceunverified:GS")!.Status);
        Assert.Equal("designated", off.Find("905")!.SurfaceStatus);
    }

    [Fact]
    public void TheHarnessSeedsWhatThisTestRuns()
    {
        var source = File.ReadAllText(Path.Combine(Root(), "tests", "LiveSmoke", "AccountabilityTestHarness.cs"));
        foreach (var s in Seed)
            Assert.Contains("\"" + s.D + "\"", source);
        Assert.Contains("CountPoints(db) == 38,", source);
        Assert.Contains("Count(P(ledger, \"Points\")) == 38", source);
        // The harness compares the ledger with what Civil 3D itself says, and cannot pass HY or SG
        // on a reader that read nothing.
        Assert.Contains("\"HY setup: 1401's point style is \"", source);
        Assert.Contains("\"every point style was read in full (\"", source);
        Assert.Contains("\"nothing left unverified: FTF read every marker and label it needed (\"", source);
        Assert.Contains("var label1301 = LabelState(db, 1301, out shown1301);", source);
        // The stages that change the standard and the settings use exactly what these tests use.
        Assert.Contains(CleanoutStandard.Replace("\"", "\"\""), source);
        Assert.Contains(SurfaceSettings.Replace("\"", "\"\""), source);
        Assert.Contains("GapId = \"line.gap:FNC:103-105\"", source);
        Assert.Equal(7, Regex.Matches(source, @"LineHandles\.Add\(Lw\(").Count);
        Assert.DoesNotContain("CivilApplication", source);
    }

    private static string Root()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "FieldToFinish.sln"))) dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("repo root not found");
    }
}
