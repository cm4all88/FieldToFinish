using FieldCodes.Linework;

namespace FieldCodes.Tests;

/// <summary>
/// Step 1: one identity for the shots linework was drawn through -- point number, handle, raw
/// description as stored and XYZ -- found by coincidence with a vertex (within 0.01 units, plan),
/// including across the rounding boundary the old 0.01 bucket missed.
/// </summary>
public sealed class SourcePointIndexTests
{
    private static SourcePoint P(string number, double x, double y, string raw = "FNC", double z = 100)
        => new() { Number = number, Handle = "H" + number, RawDescription = raw, X = x, Y = y, Z = z };

    [Fact]
    public void AVertexFindsTheShotThatMadeIt()
    {
        var index = new SourcePointIndex(new[] { P("101", 10, 20, "FNC B 3.5", 215.25), P("102", 30, 20) });
        var at = Assert.Single(index.At(10, 20));
        Assert.Equal("101", at.Number);
        Assert.Equal("H101", at.Handle);
        Assert.Equal("FNC B 3.5", at.RawDescription);
        Assert.Equal(215.25, at.Z);
        Assert.Empty(index.At(15, 20));
    }

    [Fact]
    public void ARoundingBoundaryNoLongerHidesTheShot()
    {
        // 0.000002 apart. The old string bucket rounded these to 100.00 and 100.01 and never
        // matched them; the index measures the distance and looks in the neighbouring cells.
        var index = new SourcePointIndex(new[] { P("1", 100.004999, 50.004999) });
        Assert.Single(index.At(100.005001, 50.005001));
    }

    [Fact]
    public void CoincidenceNotProximity()
    {
        var index = new SourcePointIndex(new[] { P("1", 0, 0) });
        Assert.Single(index.At(0.0099, 0));
        Assert.Single(index.At(0.007, 0.007));    // 0.0099 apart diagonally
        Assert.Empty(index.At(0.0102, 0));
        Assert.Empty(index.At(0.5, 0));
    }

    [Fact]
    public void StackedShotsAreAllReturnedNearestFirstThenByNumber()
    {
        var index = new SourcePointIndex(new[] { P("10002", 5.004, 5), P("9999", 5, 5), P("10001", 5, 5), P("7", 9, 9) });
        Assert.Equal(new[] { "9999", "10001", "10002" }, index.At(5, 5).Select(p => p.Number));
    }

    [Fact]
    public void BlankDescriptionsAreKeptForIdentityButNotAsNotes()
    {
        var index = new SourcePointIndex(new[] { P("1", 0, 0, ""), P("2", 0, 0, "EP B") });
        Assert.Equal(2, index.At(0, 0).Count);
        Assert.Equal(new[] { "EP B" }, index.DescriptionsAt(0, 0));
    }

    [Fact]
    public void TheRawDescriptionIsKeptExactlyAsStored()
    {
        var index = new SourcePointIndex(new[] { P("1", 0, 0, "  FNC1  B   6' WOOD ") });
        Assert.Equal("  FNC1  B   6' WOOD ", index.At(0, 0)[0].RawDescription);
    }

    [Fact]
    public void StatePlaneCoordinatesKeepTheirPrecision()
    {
        var index = new SourcePointIndex(new[] { P("1", 1237323.841, 384752.147), P("2", 1237323.861, 384752.147) });
        Assert.Equal("1", Assert.Single(index.At(1237323.8415, 384752.1465)).Number);
        Assert.Equal("2", Assert.Single(index.At(1237323.861, 384752.147)).Number);
    }

    [Fact]
    public void PointsAreFoundByNumberAndKeptInTheGivenOrder()
    {
        var index = new SourcePointIndex(new[] { P("3", 0, 0), P("1", 1, 1), P("2", 2, 2) });
        Assert.Equal(new[] { "3", "1", "2" }, index.All.Select(p => p.Number));
        Assert.Equal(1, index.ByNumber("1")!.X);
        Assert.Null(index.ByNumber("4"));
        Assert.Equal(3, index.Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => new SourcePointIndex(new SourcePoint[0], 0));
    }

    [Fact]
    public void VerticesListTheirShotsInOrder()
    {
        var index = new SourcePointIndex(new[] { P("101", 0, 0), P("102", 10, 0), P("103", 20, 0) });
        var shots = index.AtVertices(new[] { Kv(0, 0), Kv(5, 0), Kv(10, 0), Kv(20, 0) });
        Assert.Equal(new[] { 1, 0, 1, 1 }, shots.Select(s => s.Count));
    }

    private static KeyValuePair<double, double> Kv(double x, double y) => new(x, y);
}

/// <summary>The inventory (FTFLINES) identifies a line by its shots, as the label passes do, and
/// lists the line's handle and shot numbers.</summary>
public sealed class InventoryShotIdentityTests : IClassFixture<RulesFixture>
{
    private readonly RulesFixture _fx;
    public InventoryShotIdentityTests(RulesFixture fx) => _fx = fx;

    private LineworkCatalog Catalog() => new(_fx.Config.LineFeatures, _fx.Config.IgnoreLineNames);

    [Fact]
    public void TheShotsNameALineOnASharedLayer()
    {
        var catalog = Catalog();
        // FNC and FHW share the fence layer: the layer alone is ambiguous, the shots are not.
        var byLayer = catalog.Identify("Polyline", null, "V-SURF-FENC-E", 50, null, null);
        Assert.Equal(LineIdentitySource.Layer, byLayer.Source);
        Assert.Contains("/", byLayer.Codes);

        var notes = PointNotes.Consensus(new[] { "FNC B", "FNC", "FNC E" }, catalog);
        var byShots = catalog.Identify("Polyline", null, "V-SURF-FENC-E", 50, null, notes);
        Assert.Equal(LineIdentitySource.PointNotes, byShots.Source);
        Assert.Equal("FNC", byShots.Codes);
        Assert.Equal("point notes", byShots.SourceText);
    }

    [Fact]
    public void AFigureNameStillWinsOverTheShots()
    {
        var catalog = Catalog();
        var notes = PointNotes.Consensus(new[] { "FHW B", "FHW E" }, catalog);
        var row = catalog.Identify("Survey Figure", "FNC1", "V-SURF-FENC-E", 50, null, notes);
        Assert.Equal(LineIdentitySource.FigureName, row.Source);
        Assert.Equal("FNC", row.Codes);
    }

    [Fact]
    public void TheCsvCarriesHandleVerticesAndShots()
    {
        var row = Catalog().Identify("Polyline", null, "V-SURF-FENC-E", 50, null, null);
        row.Handle = "2A3F";
        row.VertexCount = 3;
        row.Shots = new List<string> { "101", "102", "103" };
        var csv = LineworkInventoryCsv.Render(new[] { row });
        Assert.EndsWith("Handle,Vertices,Shots", csv.Split('\n')[0].TrimEnd());
        Assert.Contains(",2A3F,3,101 102 103", csv);
        Assert.Contains("Lines with no shot at any vertex: 0", LineworkInventoryCsv.Summary(new[] { row }, null));
    }

    [Fact]
    public void TheCadSideUsesTheSharedIndexEverywhere()
    {
        string Read(params string[] parts) => File.ReadAllText(Path.Combine(new[] { Root() }.Concat(parts).ToArray()));
        var linework = Read("src", "FieldCodes.Cad", "LineworkCommands.cs");
        Assert.DoesNotContain("Bucket(", linework);                              // the old string bucket is gone
        Assert.DoesNotContain("Dictionary<string, List<string>> noteIndex", linework);
        Assert.Contains("return SourcePointReader.Read(db, tr, notes);", linework);   // with the crew's notes, for fence heights
        Assert.Contains("HeightReviewItems(noteIndex, heightIssues)", linework);
        Assert.DoesNotContain("Dictionary<string, List<string>> noteIndex", Read("src", "FieldCodes.Cad", "LineworkCommands.Gates.cs"));
        var inventory = Read("src", "FieldCodes.Cad", "FtfLineworkService.cs");
        Assert.Contains("var sources = SourcePointReader.Read(db, tr);", inventory);
        Assert.Contains("catalog.Identify(entityType, figureName, entity.Layer,", inventory);
        Assert.Contains("trimbleName, notes);", inventory);
        var reader = Read("src", "FieldCodes.Cad", "SourcePointReader.cs");
        Assert.DoesNotContain("OpenMode.ForWrite", reader);
    }

    private static string Root()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "FieldToFinish.sln"))) dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("repo root not found");
    }
}

/// <summary>Fence heights the crew wrote in the field notes ("6.0 WOOD") are read like ones in the
/// description; line identification still uses the raw description alone.</summary>
public sealed class FieldNoteHeightTests
{
    [Fact]
    public void ANoteIsReadAfterTheDescriptionForHeights()
    {
        var index = new SourcePointIndex(new[]
        {
            new SourcePoint { Number = "1", RawDescription = "FNC B", Note = "6.0 WOOD", X = 0, Y = 0 },
            new SourcePoint { Number = "2", RawDescription = "FNC 4.5", Note = "4.5", X = 10, Y = 0 },
            new SourcePoint { Number = "3", RawDescription = "FNC E", X = 20, Y = 0 },
        });
        Assert.Equal(new[] { "FNC B 6.0 WOOD" }, index.DescriptionsWithNotesAt(0, 0));
        Assert.Equal(new[] { "FNC 4.5" }, index.DescriptionsWithNotesAt(10, 0));    // already in the description: not doubled
        Assert.Equal(new[] { "FNC E" }, index.DescriptionsWithNotesAt(20, 0));
        Assert.Equal(new[] { "FNC B" }, index.DescriptionsAt(0, 0));                  // identification unchanged

        string written, problem;
        var feet = LineHeights.Read(index.DescriptionsWithNotesAt(0, 0)[0], new HashSet<string> { "FNC" }, new[] { "FGP" }, 15, out written, out problem);
        Assert.Equal(6.0, feet);
        Assert.Null(problem);
    }

    [Fact]
    public void TheHeightReaderUsesTheNotes()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "FieldToFinish.sln"))) dir = Path.GetDirectoryName(dir);
        var linework = File.ReadAllText(Path.Combine(dir!, "src", "FieldCodes.Cad", "LineworkCommands.cs"));
        Assert.Contains("foreach (var description in index.DescriptionsWithNotesAt(vertex.X, vertex.Y))", linework);
        Assert.Contains("CogoNotes.For(db, tr, FtfSession.SettingsFor(db, FtfSession.Rules(db)))", linework);
    }
}
