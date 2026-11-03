using System.Text.RegularExpressions;
using FieldCodes.Linework;
using FieldCodes.Review;
using FieldCodes.Settings;

namespace FieldCodes.Tests;

/// <summary>
/// The Civil 3D accountability check (tests/LiveSmoke/AccountabilityTestHarness.cs) seeds 34 synthetic
/// points, 7 polylines and a TIN, then expects particular review items and ledger dispositions after
/// FTFRUN. These tests run the same seed through the same library code (continuity, then the
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
        ("701", 0, 400, 100, "XYZQ 12"), ("702", 30, 400, 100, "SSCO"),
        ("801", 0, 500, 100, "EP B"), ("802", 10, 500, 100, "EP"), ("803", 20, 500, 100, "EP"), ("804", 30, 500, 100, "EP"), ("805", 40, 500, 100, "EP E"),
        ("901", 0, 600, 100, "GS"), ("902", 10, 600, 101, "GS"), ("903", 0, 610, 102, "GS"), ("904", 30, 600, 100, "ZK"), ("905", 100, 650, 100, "GS"),
        ("1001", 0, 700, 100, "FIP 5/8"), ("1101", 0, 800, 100, "WFH 12"),
        ("1201", 0, 900, 100, "FNC B"), ("1202", 10, 903, 100, "FNC"), ("1203", 20, 900, 100, "FNC E"),
    };

    private static readonly string[][] Lines =
    {
        new[] { "101", "102", "103" }, new[] { "105", "106", "107" }, new[] { "201", "202" }, new[] { "203", "204" },
        new[] { "401", "402" }, new[] { "403", "404" }, new[] { "1201", "1203" },
    };

    private (ContinuityResult continuity, ReviewRegister register, PointLedger ledger) Run(bool hydrantLabelled)
    {
        var shots = Seed.Select(s => new ContinuityShot { Number = s.N, Handle = "H" + s.N, RawDescription = s.D, X = s.X, Y = s.Y, Z = s.Z, Coding = LineworkCoding.Parse(s.D, _fx.Config) }).ToList();
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
        var settings = new FtfSettings();
        var gates = new HashSet<string>(Seed.Where(s => s.D.Split(' ').Any(w => w == "FGP")).Select(s => s.N));
        var continuity = LineContinuity.Analyze(new ContinuityInput { Shots = shots, Lines = lines, GatePosts = gates, Settings = settings.Accountability.Continuity, UnitsPerFoot = 1.0 });
        var register = new ReviewRegister();
        var now = DateTime.Now;
        register.Report(ReviewKinds.ContinuitySource, continuity.ReviewItems(shots.ToDictionary(s => s.Number)), now);

        var facts = Seed.Select(s =>
        {
            var p = new PointFacts
            {
                Number = s.N, Handle = "H" + s.N, RawDescription = s.D, X = s.X, Y = s.Y, Z = s.Z,
                Parsed = _fx.Parse(s.D, s.N), Coding = LineworkCoding.Parse(s.D, _fx.Config),
                Marker = new MarkerFact { Known = true, Visible = true, MarkerType = "UseCustomMarker", StyleName = "Basic" }
            };
            if (s.N is "901" or "902" or "903") p.Surfaces.Add(new SurfaceFact { Surface = "FTF-SMOKE-GS", InDefinition = false, IsVertex = true });
            if (s.N == "1101" && hydrantLabelled) p.Outputs.Add(new OutputFact { Kind = "Label", Handle = "LB" });
            return p;
        }).ToList();
        var ctx = new AccountingContext
        {
            Rules = _fx.Config, Settings = settings, Diagnostics = new CodeDiagnostics(_fx.Config, null), Continuity = continuity,
            FamilyOf = c => c, UnitsPerFoot = 1.0, GatePostsLabelled = new HashSet<string>()
        };
        var accounting = PointAccounting.Build(null, facts, ctx, now);
        register.Report(ReviewKinds.PointsSource, accounting.Items, now);
        // The code check's items, as ReviewChecks.Codes raises them.
        register.Report(ReviewChecks.CodesSource, ReviewChecks.Codes(Seed.Select(s => new ReviewPoint { Number = s.N, Description = s.D, Parsed = _fx.Parse(s.D, s.N) }), new CodeDiagnostics(_fx.Config, null)), now);
        PointAccounting.Finish(accounting.Ledger, register, now, "test");
        return (continuity, register, accounting.Ledger);
    }

    [Fact]
    public void TheItemsTheHarnessExpectsAfterFtfRun()
    {
        var (_, register, _) = Run(true);
        ReviewItem? Find(string id) => register.Find(id);
        var gap = Find("line.gap:FNC:103-105")!;
        Assert.Equal("probable", gap.Confidence);
        Assert.True(gap.Required);
        Assert.Equal(2, gap.Geometry!.Count);
        Assert.DoesNotContain(register.Items, i => i.Id.StartsWith("line.gap:FNC:20"));
        Assert.DoesNotContain(register.Items, i => i.Id.Contains("FNC1") || i.Id.Contains("FNC2"));
        foreach (var id in new[] { "point.duplicate:601", "point.duplicate:602", "code.unknown:XYZQ", "point.nosymbol:702",
                                   "line.missingsegment:EP:801-805", "line.missingvertex:FNC:1201-1202-1203",
                                   "point.excludednoreason:904", "point.missingoutput:905" })
            Assert.True(Find(id) != null, id);
        var withGeometry = register.Items.Count(i => i.IsOpen && i.Source == ReviewKinds.ContinuitySource && i.Geometry != null && i.Geometry.Count >= 2);
        Assert.True(withGeometry >= 3);
    }

    [Fact]
    public void TheDispositionsTheHarnessExpects()
    {
        var (_, register, ledger) = Run(true);
        Assert.Equal(34, ledger.Points.Count);
        Assert.Equal(PointDisposition.Surface, ledger.Find("901")!.Disposition);
        Assert.Equal(PointDisposition.Control, ledger.Find("1001")!.Disposition);
        Assert.Equal(PointDisposition.Linework, ledger.Find("103")!.Disposition);
        Assert.Equal(PointDisposition.Linework, ledger.Find("105")!.Disposition);
        Assert.Equal(PointDisposition.Review, ledger.Find("803")!.Disposition);
        Assert.Contains("line.missingsegment:EP:801-805", ledger.Find("803")!.Issues);
        Assert.Equal(PointDisposition.Drawn, ledger.Find("1101")!.Disposition);
        var run = ledger.LastRun!;
        Assert.Equal(34, run.Points);
        Assert.True(run.Review > 0 && run.Accounted > 0);

        var (_, unlabelled, _) = Run(false);
        Assert.NotNull(unlabelled.Find("point.missingoutput:1101"));
        Assert.False(register.MarkQcComplete(new ReviewSettings(), "x", DateTime.Now, out _));
    }

    [Fact]
    public void TheHarnessSeedsWhatThisTestRuns()
    {
        var source = File.ReadAllText(Path.Combine(Root(), "tests", "LiveSmoke", "AccountabilityTestHarness.cs"));
        foreach (var s in Seed)
            Assert.Contains("\"" + s.D + "\"", source);
        Assert.Contains("CountPoints(db) == 34,", source);
        Assert.Contains("Count(P(ledger, \"Points\")) == 34", source);
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
