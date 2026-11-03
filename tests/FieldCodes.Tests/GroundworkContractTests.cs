using System.Text.RegularExpressions;

namespace FieldCodes.Tests;

/// <summary>
/// Step 0 of the point accountability work, held by reading the source (the Civil 3D project
/// cannot run here): one missing-block rule everywhere, every FtfSession command countable by
/// the Civil 3D harnesses, and no live harness counting ownership kinds by number.
/// </summary>
public sealed class GroundworkContractTests
{
    private static string Read(params string[] parts)
        => File.ReadAllText(Path.Combine(new[] { RepoRoot() }.Concat(parts).ToArray()));

    [Fact]
    public void AMissingBlockIsCountedOnceAsAnErrorEverywhere()
    {
        var tree = Read("src", "FieldCodes.Cad", "TreeCommands.cs");
        var check = tree.IndexOf("if (MissingBlock(db, tr, parsed))", StringComparison.Ordinal);
        var recognized = tree.IndexOf("recognized++;", StringComparison.Ordinal);
        Assert.True(check > 0 && recognized > check, "FTFPOINTS must rule out a missing block before counting a point recognized");
        Assert.Single(Regex.Matches(tree, @"recognized\+\+;"));
        Assert.Contains("internal static bool MissingBlock(Database db, Transaction tr, ParsedPoint parsed)", tree);

        // The Overview, the dry-run review and the review list classify the same way.
        Assert.Contains("TreeCommands.MissingBlock(db, tr, parsed);", Read("src", "FieldCodes.Cad", "FtfStatusService.cs"));
        Assert.Contains("TreeCommands.MissingBlock(db, tr, parsed);", Read("src", "FieldCodes.Cad", "FtfReviewService.cs"));
        Assert.Contains("TreeCommands.MissingBlock(db, tr, parsed);", Read("src", "FieldCodes.Cad", "ReviewCommands.cs"));
    }

    [Fact]
    public void EverySessionCommandIsTracedForTheCivil3dHarnesses()
    {
        var session = Read("src", "FieldCodes.Cad", "FtfSession.cs");
        var start = session.IndexOf("FtfCommandTrace.Start(commandName);", StringComparison.Ordinal);
        var run = session.IndexOf("RunInTransaction(commandName, db, ed, body);", StringComparison.Ordinal);
        var finish = session.IndexOf("if (LastRunError == null) FtfCommandTrace.Finish(commandName);", StringComparison.Ordinal);
        Assert.True(start > 0 && run > start && finish > run, "Start before the body, Finish only after it succeeded");
    }

    [Fact]
    public void NoLiveHarnessComparesAnOwnershipKindWithANumber()
    {
        var dir = Path.Combine(RepoRoot(), "tests", "LiveSmoke");
        var offenders = new List<string>();
        foreach (var file in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            foreach (Match m in Regex.Matches(text, @"\bkind\s*==\s*\d+", RegexOptions.IgnoreCase))
                offenders.Add(Path.GetFileName(file) + ": " + m.Value);
        }
        Assert.True(offenders.Count == 0, "Read kinds from FieldCodes.Cad.FtfEntityKind: " + string.Join(", ", offenders));
        Assert.Contains("KindNumber(\"CompareSplit\")", File.ReadAllText(Path.Combine(dir, "CompareTestHarness.cs")));
        Assert.Contains("KindNumber(\"TrimCloud\")", File.ReadAllText(Path.Combine(dir, "CompareTestHarness.cs")));
    }

    [Fact]
    public void RealDrawingCasesStayOutsideThePublicRepository()
    {
        // The cases name real projects, parcels and clients; they live in C:\dev\FTF-RealWorld\cases.
        var cases = Path.Combine(RepoRoot(), "tests", "RealWorld", "cases");
        var tracked = Directory.GetFiles(cases).Select(Path.GetFileName).OrderBy(n => n).ToArray();
        Assert.All(tracked, n => Assert.True(n == "README.md" || n!.StartsWith("example-", StringComparison.Ordinal), n + " is not synthetic"));
        Assert.Contains("tests/RealWorld/cases/*", File.ReadAllText(Path.Combine(RepoRoot(), ".gitignore")));

        var runner = Read("tests", "RealWorld", "run-all-cases.ps1");
        Assert.Contains("cases\\cases.psd1", runner);
        Assert.DoesNotMatch(@"refs\\\w+\\[^']*\.dwg", runner);   // no drawing names in the runner
    }

    [Fact]
    public void NoTestScriptCarriesAPersonalWindowsPath()
    {
        var offenders = new List<string>();
        foreach (var file in Directory.GetFiles(Path.Combine(RepoRoot(), "tests"), "*.*", SearchOption.AllDirectories))
        {
            if (file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar) ||
                file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)) continue;
            var ext = Path.GetExtension(file).ToLowerInvariant();
            if (ext != ".scr" && ext != ".ps1" && ext != ".cs" && ext != ".txt" && ext != ".md") continue;
            // Generated on every run by run-ui.ps1 and ignored by git: it holds the runner's own path.
            if (Path.GetFileName(file) == "run-ui.scr") continue;
            // A made-up user name in a test (C:\Users\x) is fine; a real one is not.
            if (Regex.IsMatch(File.ReadAllText(file), @"C:\\Users\\(?!Public\b|x\b|example\b)[A-Za-z]{2,}")) offenders.Add(Path.GetFileName(file));
        }
        Assert.True(offenders.Count == 0, "Use %APPDATA% or a generated path: " + string.Join(", ", offenders));
    }

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "FieldToFinish.sln"))) dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("repo root not found");
    }
}
