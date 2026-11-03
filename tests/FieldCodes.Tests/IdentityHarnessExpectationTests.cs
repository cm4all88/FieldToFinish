using System.Text.RegularExpressions;
using FieldCodes.Linework;
using FieldCodes.Review;

namespace FieldCodes.Tests;

/// <summary>
/// The Civil 3D identity check (tests/LiveSmoke/IdentityTestHarness.cs) seeds 15 synthetic points
/// and lines A-G and expects particular FTFLINES rows and review items. These tests run the same
/// seed data through the same library code FTFLINES and FTFREVIEWCHECK use, so a wrong expectation
/// fails here rather than in Civil 3D. They do not replace the Civil 3D run.
/// </summary>
public sealed class IdentityHarnessExpectationTests : IClassFixture<RulesFixture>
{
    private readonly RulesFixture _fx;
    public IdentityHarnessExpectationTests(RulesFixture fx) => _fx = fx;

    // As IDSEED: number, x, y, z, raw description.
    private static readonly (string N, double X, double Y, double Z, string D)[] Seed =
    {
        ("101", 0, 0, 100.0, "FNC B 3.5"), ("102", 10, 0, 100.5, "FNC"), ("103", 20, 0, 101.0, "FNC E"),
        ("201", 100.004999, 50.004999, 100, "FHW B"), ("202", 110, 50, 100, "FHW E"),
        ("301", 0, 100, 200, "EP B"), ("302", 10, 100, 201, "EP"), ("303", 20, 100, 199, "EP E"),
        ("401", 50, 0, 100, "FNC B"), ("402", 50, 0, 100, "FNC B"), ("403", 60, 0, 100, "FNC E"),
        ("501", 0, 300, 100, "FNC B"), ("502", 10, 300, 100, "FNC E"),
        ("601", 0, 400, 100, "FNC1 B 6' WOOD"), ("602", 10, 400, 100, "FNC1 E"),
    };

    // As IDSEED: entity type FTFLINES reports, layer, plan vertices.
    private static readonly Dictionary<string, (string Type, string Layer, double[] Xy)> Lines = new()
    {
        ["A"] = ("Polyline", "V-SURF-FENC-E", new[] { 0.0, 0, 10, 0, 20, 0 }),
        ["B"] = ("Polyline", "V-SURF-FENC-E", new[] { 100.005001, 50.005001, 110, 50 }),
        ["C"] = ("Polyline", "FTF-SMOKE-LINES", new[] { 0.0, 100, 10, 100, 20, 100 }),
        ["D"] = ("Polyline", "V-SURF-FENC-E", new[] { 50.0, 0, 60, 0 }),
        ["E"] = ("Line", "FTF-SMOKE-LINES", new[] { 0.0, 200, 10, 200 }),
        ["F"] = ("Polyline", "V-SURF-FENC-E", new[] { 0.05, 300, 10, 300 }),
        ["G"] = ("Polyline", "V-SURF-FENC-E", new[] { 0.0, 400, 10, 400 }),
    };

    private SourcePointIndex Index() => new(Seed.Select(s => new SourcePoint
        { Number = s.N, Handle = "H" + s.N, RawDescription = s.D, X = s.X, Y = s.Y, Z = s.Z }));

    /// <summary>The steps FtfLineworkService.Scan takes for one curve.</summary>
    private LineworkRow Scan(string key)
    {
        var line = Lines[key];
        var catalog = new LineworkCatalog(_fx.Config.LineFeatures, _fx.Config.IgnoreLineNames);
        var index = Index();
        var shots = new List<string>();
        var notes = new List<string>();
        for (var i = 0; i < line.Xy.Length; i += 2)
            foreach (var p in index.At(line.Xy[i], line.Xy[i + 1]))
            {
                if (shots.Count == 0 || shots[^1] != p.Number) shots.Add(p.Number);
                if (p.HasDescription) notes.Add(p.RawDescription);
            }
        var row = catalog.Identify(line.Type, null, line.Layer, 20, null, PointNotes.Consensus(notes, catalog));
        row.VertexCount = line.Xy.Length / 2;
        row.Shots = shots;
        return row;
    }

    [Theory]
    [InlineData("A", "point notes", "FNC", 3, "101 102 103")]
    [InlineData("B", "point notes", "FHW", 2, "201 202")]
    [InlineData("C", "point notes", "EP", 3, "301 302 303")]
    [InlineData("D", "point notes", "FNC", 2, "401 402 403")]
    [InlineData("E", "-", null, 2, "")]
    [InlineData("F", "point notes", "FNC", 2, "502")]
    [InlineData("G", "point notes", "FNC", 2, "601 602")]
    public void FtfLinesRowsAreWhatTheHarnessExpects(string key, string source, string? codes, int vertices, string shots)
    {
        var row = Scan(key);
        Assert.Equal(source, row.SourceText);
        if (codes != null) Assert.Equal(codes, row.Codes);
        Assert.Equal(vertices, row.VertexCount);
        Assert.Equal(shots, string.Join(" ", row.Shots));
    }

    [Fact]
    public void TheReviewCheckFlagsWoodAndNothingElseOnTheseLines()
    {
        // The known codes ReviewStore.CheckCodes builds (no office settings: default gate posts).
        var cfg = _fx.Config;
        var known = new List<string>();
        known.AddRange(cfg.IgnoreCodes ?? new List<string>());
        known.AddRange(cfg.ControlCodes ?? new List<string>());
        known.Add("FGP");
        foreach (var f in cfg.LineFeatures) { known.Add(f.Code); if (f.Aliases != null) known.AddRange(f.Aliases); }
        foreach (var s in cfg.SymbolLabels ?? new List<SymbolLabelRule>()) known.Add(s.Code);
        foreach (var m in cfg.SpanMarkers ?? new List<SpanMarkerRule>()) known.Add(m.Token);

        var points = Seed.Select(s => new ReviewPoint { Number = s.N, Description = s.D, Handle = "H" + s.N, X = s.X, Y = s.Y });
        var items = ReviewChecks.LineworkModifiers(points, cfg.LineworkCodes, known, cfg.LineworkCommands);
        var wood = Assert.Single(items);
        Assert.Equal("code.modifier:FNC WOOD", wood.Id);
        Assert.Equal("601", wood.Points);
    }

    [Fact]
    public void TheHarnessCountsThePointsItSeeds()
    {
        var source = File.ReadAllText(Path.Combine(Root(), "tests", "LiveSmoke", "IdentityTestHarness.cs"));
        var seeded = Regex.Matches(source, @"\bPoint\(tr, cdoc, (\d+),").Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(Seed.Select(s => s.N), seeded);
        Assert.Contains("CountPoints(db) == " + Seed.Length + ",", source);
        Assert.Contains("all.Count == " + Seed.Length + ",", source);
        foreach (var d in Seed.Select(s => s.D).Distinct())
            Assert.Contains("\"" + d + "\")", source);
    }

    private static string Root()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "FieldToFinish.sln"))) dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("repo root not found");
    }
}
