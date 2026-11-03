namespace FieldCodes.Tests;

/// <summary>
/// The Civil 3D side of gates, held to its promises by reading the plugin source (the CAD project
/// cannot run here): a rerun replaces only FTF's own unchanged gate labels, keeps the ones moved or
/// reworded by hand, never labels a gate twice, and leaves posts and fences unmodified.
/// </summary>
public sealed class GateSourceTests
{
    private static string Read(params string[] parts)
        => File.ReadAllText(Path.Combine(new[] { RepoRoot(), "src", "FieldCodes.Cad" }.Concat(parts).ToArray()));

    [Fact]
    public void TheBulkPassLeavesGateLabelsToTheGatePass()
    {
        var src = Read("LineworkCommands.cs");
        Assert.Contains("if (IsGateTag(s.TagText)) return false;", src);
        Assert.Contains("var spanMarkers = SpanMarkersWithoutGates(cfg.SpanMarkers, settings.Gates);", src);
        Assert.DoesNotContain("PlaceSpanLabels(db, tr, null, curve, cfg.SpanMarkers", src);
        Assert.Contains("RunGatePass(db, tr, ed, settings, cfg, styler, noteIndex, ObjectId.Null)", src);
        Assert.Contains("SpanMarkersWithoutGates(cfg.SpanMarkers, settings.Gates)))", src);   // FTFLABELLINE
    }

    [Fact]
    public void ARerunKeepsHandWorkAndNeverDuplicates()
    {
        var src = Read("LineworkCommands.Gates.cs");
        Assert.Contains("if (mine.Any(e => e.ByHand))", src);
        Assert.Contains("var moved = stamp.WasMovedByHand(text.Location, 0.01);", src);
        Assert.Contains("Ownership.ContentsHashOf(text.Contents) != stamp.ContentsHash", src);
        Assert.Contains("outcome.Unchanged++;", src);
        Assert.Contains("foreach (var old in mine) { EraseGateLabel(db, tr, old); outcome.Replaced++; }", src);
        // A pair the drafter confirmed is never touched, and its posts are left out of pairing.
        Assert.Contains("existing.Where(e => !e.Confirmed && e.Key == pair.Key)", src);
        Assert.Contains("GateFinder.Find(posts, fences.Select(f => f.Fence).ToList(), gs, confirmed, FenceCodes(cfg))", src);
    }

    [Fact]
    public void PostsAndFencesAreOnlyRead()
    {
        var src = Read("LineworkCommands.Gates.cs");
        Assert.DoesNotContain("OpenMode.ForWrite, false, true) as Autodesk.Civil.DatabaseServices.CogoPoint", src);
        Assert.DoesNotContain("curve.UpgradeOpen", src);
        Assert.DoesNotContain(".Explode(", src);
        // Only FTF's own gate labels and masks are ever opened for write.
        Assert.Equal(2, CountOf(src, "OpenMode.ForWrite"));
    }

    [Fact]
    public void UnsurePairsGoToTheReviewList()
    {
        var src = Read("LineworkCommands.Gates.cs");
        Assert.Contains("register.Report(GateReviewSource, result.Review, DateTime.Now, true, settings.Review);", src);
        Assert.Contains("[CommandMethod(\"FTFGATES\"", src);
        Assert.Contains("[CommandMethod(\"FTFGATE\"", src);
    }

    private static int CountOf(string text, string what)
    {
        var n = 0;
        for (var i = text.IndexOf(what, StringComparison.Ordinal); i >= 0; i = text.IndexOf(what, i + what.Length, StringComparison.Ordinal)) n++;
        return n;
    }

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "FieldToFinish.sln"))) dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("repo root not found");
    }
}
