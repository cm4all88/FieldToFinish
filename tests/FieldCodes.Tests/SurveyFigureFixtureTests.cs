using System.Globalization;
using System.Text.RegularExpressions;
using FieldCodes.Linework;

namespace FieldCodes.Tests;

/// <summary>
/// The survey figure fixture (tests/LiveSmoke/fixtures): its points are synthetic and say what the
/// README says they cover, as FTF's parser reads them, and the runner can never report a pass
/// without the fixture, or change it. Civil 3D's side is checked by run-figures.ps1, not here.
/// </summary>
public sealed class SurveyFigureFixtureTests : IClassFixture<RulesFixture>
{
    private readonly RulesFixture _fx;
    public SurveyFigureFixtureTests(RulesFixture fx) => _fx = fx;

    private sealed record Row(string Number, double N, double E, double Z, string Description);

    private static List<Row> Rows() => File.ReadAllLines(Path.Combine(Root(), "tests", "LiveSmoke", "fixtures", "figures-synthetic.csv"))
        .Where(l => l.Trim().Length > 0)
        .Select(l => l.Split(','))
        .Select(p => new Row(p[0], double.Parse(p[1], CultureInfo.InvariantCulture), double.Parse(p[2], CultureInfo.InvariantCulture),
                             double.Parse(p[3], CultureInfo.InvariantCulture), p[4]))
        .ToList();

    [Fact]
    public void ThePointsAreSyntheticPnezd()
    {
        var rows = Rows();
        Assert.Equal(30, rows.Count);
        Assert.Equal(rows.Count, rows.Select(r => r.Number).Distinct().Count());
        Assert.All(rows, r =>
        {
            Assert.Matches(@"^\d+$", r.Number);
            Assert.InRange(r.N, 4990, 5700);           // invented local coordinates, nowhere near a state plane grid
            Assert.InRange(r.E, 4990, 5700);
            Assert.InRange(r.Z, 90, 110);
        });
        Assert.Contains("30 synthetic points", File.ReadAllText(Path.Combine(Root(), "tests", "LiveSmoke", "fixtures", "README.md")));
    }

    [Fact]
    public void FtfReadsTheCodingTheReadmeDescribes()
    {
        var rows = Rows().ToDictionary(r => r.Number);
        LineworkShot Shot(string n) => LineworkCoding.Parse(rows[n].Description, _fx.Config);
        IEnumerable<string> On(string figureId) => rows.Keys.Where(n => Shot(n).Figure(figureId) != null);

        Assert.Equal(new[] { "1001", "1002", "1003", "1004", "1005", "1006" }, On("FNC1"));   // begun again at 1005
        Assert.True(Shot("1005").Figure("FNC1")!.Begins);
        Assert.True(Shot("1004").Figure("FNC1")!.Ends);
        Assert.Equal(new[] { "1007", "1008" }, On("FNC"));                                    // FNC is not FNC1
        Assert.Equal(new[] { "1010", "1012", "1014" }, On("EP1"));                            // interleaved
        Assert.Equal(new[] { "1011", "1013", "1015" }, On("EP2"));
        Assert.True(Shot("1023").Figure("BLD1")!.Closes);
        Assert.Contains(LineworkCommandKind.CurveBegin, Shot("1031").Figure("EP3")!.Commands.Select(c => c.Kind));
        Assert.Contains(LineworkCommandKind.CurveEnd, Shot("1033").Figure("EP3")!.Commands.Select(c => c.Kind));
        Assert.Equal(new[] { "TOP1", "TOE1" }, Shot("1041").Figures.Select(f => f.FigureId));  // one shot, two figures
        Assert.True(Shot("1041").Figure("TOP1")!.Ends);
        Assert.True(Shot("1041").Figure("TOE1")!.Begins);
        Assert.Equal(new[] { "1050", "1051" }, On("EC1"));                                    // EC is a figure code
        Assert.False(Shot("1060").IsLinework);
        Assert.False(Shot("1061").IsLinework);
        Assert.All(rows.Keys, n => Assert.All(Shot(n).Figures, f => Assert.Empty(f.Words)));  // nothing for the review list
    }

    [Fact]
    public void TheCurveShotIsOnTheArcThroughItsEnds()
    {
        var rows = Rows().ToDictionary(r => r.Number);
        // PC 1031, mid 1032, PT 1033: all 20 from the centre, which lies on the tangent's normal at each end.
        double Distance(Row r, double n, double e) => Math.Sqrt((r.N - n) * (r.N - n) + (r.E - e) * (r.E - e));
        const double cn = 5020, ce = 5320;
        foreach (var n in new[] { "1031", "1032", "1033" })
            Assert.InRange(Distance(rows[n], cn, ce), 19.9995, 20.0005);
    }

    [Fact]
    public void TheRunnerNeverPassesWithoutTheFixtureAndNeverChangesIt()
    {
        var runner = Code(Read("tests", "LiveSmoke", "run-figures.ps1"));
        var missing = runner.IndexOf("if (-not (Test-Path -LiteralPath $Fixture))", StringComparison.Ordinal);
        Assert.True(missing >= 0);
        var branch = runner.Substring(missing, runner.IndexOf('}', missing) - missing);
        Assert.Contains("exit 2", branch);
        Assert.Contains("NOT verified", branch);
        Assert.DoesNotContain("exit 0", branch);

        Assert.Contains("Get-FileHash -LiteralPath $Fixture", runner);
        Assert.Contains("if ($after -ne $before) { Fail", runner);
        Assert.Contains("Copy-Item -LiteralPath $Fixture -Destination $drawing", runner);
        Assert.Contains("tests\\LiveSmoke\\FigureFixtureHarness.cs", runner);
        Assert.Contains("FTF_FIGURE_RESULT", runner);
        Assert.Contains("'COMPLETE'", runner);
        foreach (var stage in new[] { "SNAP", "LINES", "AGAIN" })
            Assert.Contains("'" + stage + "'", runner);
        Assert.Matches(@"(?m)^exit 0\s*$", runner);
        Assert.Single(Regex.Matches(runner, @"\bexit 0\b"));                         // one way to pass, at the end
    }

    [Fact]
    public void TheHarnessProvesFtfLinesRanAndReadsTheRealApi()
    {
        var harness = Read("tests", "LiveSmoke", "FigureFixtureHarness.cs");
        var verify = harness.Substring(harness.IndexOf("public void Verify()", StringComparison.Ordinal));
        var require = verify.IndexOf("RequireRanOnce();", StringComparison.Ordinal);
        Assert.True(require >= 0 && require < verify.IndexOf("VerifyLines(", StringComparison.Ordinal));
        Assert.Contains("as CivDb.SurveyFigure", harness);
        Assert.Contains("GetPoints(Civil.FeatureLinePointType.AllPoints)", harness);
        Assert.Contains("the drawing has no survey figures", harness);
        Assert.DoesNotContain("OpenMode.ForWrite", harness);                                 // reads, never writes
        Assert.DoesNotContain("CivilApplication", harness);
    }

    [Fact]
    public void TheFixtureDrawingStaysOutOfTheRepository()
    {
        // A copy built here by mistake is ignored rather than committed.
        Assert.Contains("tests/LiveSmoke/fixtures/*.dwg", Read(".gitignore"));
        Assert.Contains(@"C:\dev\FTF-fixtures\figures-synthetic.dwg", Read("tests", "LiveSmoke", "run-figures.ps1"));
    }

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine(new[] { Root() }.Concat(parts).ToArray()));

    private static string Code(string script) =>
        Regex.Replace(Regex.Replace(script, "<#.*?#>", string.Empty, RegexOptions.Singleline), @"(?m)^\s*#.*$", string.Empty);

    private static string Root()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "FieldToFinish.sln"))) dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("repo root not found");
    }
}
