namespace FieldCodes.Tests;

/// <summary>
/// FTFLABELBETWEEN on a U-shaped 3D polyline was refused outright: the second pick on the
/// same hedge was "the same line twice", the pick points were thrown away, and the preview
/// asked AutoCAD for the nearest point in 3D to a cursor at Z = 0, which a line hundreds of
/// feet up answers with wherever it is lowest. The CAD side has no test host here, so what
/// fixed it is held to the source; the geometry itself is in LabelBetweenTests.
/// </summary>
public sealed class LabelBetweenSourceTests
{
    private static string Repo([System.Runtime.CompilerServices.CallerFilePath] string file = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "..", ".."));

    private static readonly string Source =
        File.ReadAllText(Path.Combine(Repo(), "src", "FieldCodes.Cad", "LineworkCommands.cs"));

    private static string Between(string from, string to)
    {
        var start = Source.IndexOf(from, StringComparison.Ordinal);
        Assert.True(start >= 0, "not found: " + from);
        var end = Source.IndexOf(to, start + from.Length, StringComparison.Ordinal);
        Assert.True(end > start, "not found after it: " + to);
        return Source.Substring(start, end - start);
    }

    private static string RunBetween => Between("private static void RunBetween(", "private sealed class LinetypeLook");
    private static string PickEdge => Between("private static EdgePick PickEdge(", "private static string BetweenKindOf(");
    private static string PathOf => Between("private static PlanPath BetweenPathOf(", "private static bool PlaceOneBetweenLabel(");
    private static string PlaceOne => Between("private static bool PlaceOneBetweenLabel(", "[CommandMethod(\"FTFLABELSTAIRS\"");
    private static string Jig => Between("private sealed class LineBetweenJig", "private static IList<string> NotesAtVertices(");

    [Fact]
    public void EachPickKeepsWhereItLandedInWorldCoordinates()
    {
        Assert.Contains("picked.PickedPoint.TransformBy(ed.CurrentUserCoordinateSystem)", PickEdge);
        Assert.Contains("first.At.X, first.At.Y", PlaceOne);
        Assert.Contains("second.At.X, second.At.Y", PlaceOne);
    }

    [Fact]
    public void TwoPicksOnTheSameLineAreAllowed()
    {
        Assert.DoesNotContain("same line twice", RunBetween);
        Assert.DoesNotContain("first == second", RunBetween);
        Assert.Contains("var oneLine = first.Id == second.Id;", PlaceOne);
        Assert.Contains("LabelBetween.OneLine(", PlaceOne);
        Assert.Contains("LabelBetween.TwoLines(", PlaceOne);
    }

    [Fact]
    public void ThePreviewWorksInPlanNotAgainstTheCurveIn3D()
    {
        Assert.DoesNotContain("GetClosestPointTo", Jig);
        Assert.DoesNotContain("GetParameterAtPoint", Jig);
        Assert.Contains("LabelBetween.Place(_sides, _cursor.X, _cursor.Y, _alignToLine)", Jig);
        Assert.Contains("LabelBetween.Start(sides, alignToLine)", Jig);
    }

    [Fact]
    public void TheEdgesAreOnlyEverRead()
    {
        foreach (var body in new[] { PathOf, PlaceOne, Jig })
        {
            Assert.DoesNotContain("OpenMode.ForWrite", body);
            Assert.DoesNotContain("UpgradeOpen", body);
            Assert.DoesNotContain("Explode", body);
            Assert.DoesNotContain("ConvertFrom", body);
            Assert.DoesNotContain(".Erase(", body);
        }
        Assert.Contains("OpenMode.ForRead", PlaceOne);
        // The label goes through the one placement every interactive line label uses.
        Assert.Contains("PlaceLineLabel(db, tr, jig.Plan, jig.CurrentWording", PlaceOne);
        Assert.Contains("ManualLineLabelMark", PlaceOne);
    }

    [Fact]
    public void AnUnconfiguredEdgeAsksForTheWordingInsteadOfRefusing()
    {
        Assert.DoesNotContain("Pick the feature's own edge first", PlaceOne);
        Assert.Contains("AskForWording(ed)", PlaceOne);
    }
}
