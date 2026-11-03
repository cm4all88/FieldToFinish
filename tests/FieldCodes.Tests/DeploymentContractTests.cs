using System.Text.RegularExpressions;

namespace FieldCodes.Tests;

/// <summary>
/// The promises the update process makes, held to the scripts themselves: UPDATEFTF never touches
/// the developer's working copy, never installs before validating, and puts the previous FTF back
/// when the smoke test fails; the smoke test fails at once on an unregistered command and proves
/// FTFCONTOUR really ran before trusting what it drew. These are text checks on the scripts (no
/// Windows here); the behaviour itself is exercised by running them.
/// </summary>
public sealed class DeploymentContractTests
{
    private static string Repo([System.Runtime.CompilerServices.CallerFilePath] string file = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "..", ".."));

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine(new[] { Repo() }.Concat(parts).ToArray()));

    /// <summary>PowerShell with comments removed, so a sentence cannot satisfy or trip a check.</summary>
    private static string Code(string script) =>
        Regex.Replace(Regex.Replace(script, "<#.*?#>", string.Empty, RegexOptions.Singleline), @"(?m)^\s*#.*$", string.Empty);

    // ----------------------------------------------------------------- UPDATEFTF

    [Theory]
    [InlineData(@"'stash'")]
    [InlineData(@"'reset'")]
    [InlineData(@"'clean'")]
    [InlineData(@"'--hard'")]
    [InlineData(@"'switch'")]
    [InlineData(@"'pull'")]
    [InlineData(@"'merge'")]
    [InlineData(@"'rebase'")]
    [InlineData(@"'push'")]
    public void UpdateFtfNeverRunsAGitCommandThatCouldChangeAWorkingCopy(string verb)
    {
        Assert.DoesNotContain(verb, Code(Read("UPDATEFTF.ps1")));
    }

    [Fact]
    public void UpdateFtfOnlyChecksOutInsideItsOwnBuildCheckout()
    {
        var code = Code(Read("UPDATEFTF.ps1"));
        var checkouts = Regex.Matches(code, @"Run-Git @\(([^)]*)'checkout'");
        Assert.NotEmpty(checkouts);
        Assert.All(checkouts.Cast<Match>(), m => Assert.Contains("'-C', $BuildRoot", m.Groups[1].Value));
        Assert.DoesNotMatch(@"'-C', \$repoRoot, 'checkout'", code);
        Assert.Contains("'worktree', 'add', '--detach', $BuildRoot", code);
        // Hand edits in the build checkout stop the run instead of being discarded.
        Assert.Contains("UPDATEFTF will not discard them", code);
        Assert.DoesNotContain("checkout', '--force'", code);
    }

    [Fact]
    public void UpdateFtfValidatesBeforeTouchingTheInstallAndRestoresOnAFailedSmokeTest()
    {
        var code = Code(Read("UPDATEFTF.ps1"));
        // Each step must come after the one before it.
        var at = 0;
        void Then(string marker)
        {
            var i = code.IndexOf(marker, at, StringComparison.Ordinal);
            Assert.True(i >= 0, "missing, or out of order: " + marker);
            at = i + marker.Length;
        }

        Then("'test', (Join-Path $BuildRoot 'tests\\FieldCodes.Tests\\FieldCodes.Tests.csproj')");
        Then("deploy\\make-release.ps1') @('-Configuration', 'Release', '-NoBuild'");
        Then("deploy\\test-release.ps1') @('-Configuration', 'Release', '-NoBuild')");
        Then("VALIDATED, NOT YET INSTALLED");
        Then("Copy-Item -LiteralPath $installedDir -Destination $script:backup -Recurse -Force");
        Then("(Join-Path $setup 'install.ps1') @('-Source', $setup, '-NoOfficeCopy')");
        Then("$problem = Verify-Installed $target");
        Then("deploy\\smoke-test.ps1') @('-PluginDll', $installedDll, '-Commit', $target");

        Assert.Matches(@"if \(\$smoke\.Code -ne 0\) \{\s*Restore-Backup", code);
        Assert.Contains("channel = 'approved'", code);
        Assert.Matches(@"if \(\$Ref\) \{ Fail '-Ref builds a development ref and needs -Dev", code);
    }

    [Fact]
    public void OnlyAnUnmodifiedBuildOfTheCommitIsAccepted()
    {
        var update = Code(Read("UPDATEFTF.ps1"));
        Assert.Contains(@"-notmatch ('\+' + $short + '\.' + $channel + '$')", update);   // no .dirty, no other commit
        var release = Code(Read("deploy", "make-release.ps1"));
        Assert.Contains("is not a build of $short", release);
        Assert.Contains("cannot be packaged as approved", release);
        Assert.Contains("build-info.json", release);
    }

    [Fact]
    public void ThePackageCarriesEverythingTheMergedFeaturesNeedAndNoAutodeskAssembly()
    {
        var release = Code(Read("deploy", "make-release.ps1"));
        Assert.Contains(@"config\rules.json", release);
        Assert.Contains(@"config\compare-profiles\*.json", release);       // FTFCOMPARE's factory profiles
        Assert.Contains("'^(Ac|Aec)'", release);                               // AecBaseMgd too, not just Aecc
        Assert.Contains("'^(Ac|Aec)'", Code(Read("deploy", "install-dev.ps1")));
        Assert.Contains("'build-info.json'", Code(Read("deploy", "install-dev.ps1")));   // a dev install never claims a stamp
    }

    // ----------------------------------------------------------------- the smoke test

    [Fact]
    public void AnUnknownCommandFailsTheSmokeTestAtOnce()
    {
        var runner = Code(Read("deploy", "smoke-test.ps1"));
        Assert.Contains("Unknown command", runner);
        Assert.Matches(@"while \(-not \$process\.HasExited\)", runner);    // watched while it runs
        Assert.Contains("taskkill.exe /PID $process.Id /T /F", runner);    // and stopped, not waited out
        Assert.Contains("'COMPLETE'", runner);                             // a run that stops early fails
        Assert.Contains("FATAL", runner);
        foreach (var stage in new[] { "UNDONE", "FIRST", "AGAIN", "BOUNDARY" })
            Assert.Contains("'" + stage + "'", runner);
    }

    [Fact]
    public void TheHarnessProvesTheRightBuildIsLoadedAndThatFtfContourRan()
    {
        var harness = Read("tests", "LiveSmoke", "ContourTestHarness.cs");
        Assert.Contains("FTF_SMOKE_EXPECT_DLL", harness);
        Assert.Contains("FTF_SMOKE_EXPECT_COMMIT", harness);
        Assert.Contains("FieldCodes.Cad is loaded \" + loaded.Count + \" times", harness);
        Assert.Contains("Environment.Exit(3)", harness);

        // Every CONTOURVERIFY first requires that FTFCONTOUR started and finished exactly once.
        var verify = harness.Substring(harness.IndexOf("public void Verify()", StringComparison.Ordinal));
        var require = verify.IndexOf("RequireRanOnce(\"FTFCONTOUR\")", StringComparison.Ordinal);
        var firstCheck = verify.IndexOf("Check(ed,", StringComparison.Ordinal);
        Assert.True(require >= 0 && require < firstCheck, "CONTOURVERIFY must prove FTFCONTOUR ran before checking anything");
        Assert.Contains("never ran (Unknown command", harness);
    }

    [Fact]
    public void TheHarnessReadsKindsAndTracesFromThePluginItselfAndThePluginHasThem()
    {
        var harness = Read("tests", "LiveSmoke", "ContourTestHarness.cs");
        Assert.DoesNotMatch(@"const short \w*Kind = \d+", harness);         // kinds are never hard-coded
        foreach (var name in new[] { "\"Contour\"", "\"ContourLabel\"", "\"ContourMask\"" })
            Assert.Contains("Enum.Parse(kinds, " + name + ")", harness);

        var ownership = Read("src", "FieldCodes.Cad", "Ownership.cs");
        Assert.Matches(@"\bContour = \d+,", ownership);
        Assert.Matches(@"\bContourLabel = \d+,", ownership);
        Assert.Matches(@"\bContourMask = \d+", ownership);

        var trace = Read("src", "FieldCodes.Cad", "FtfBuildInfo.cs");
        Assert.Contains("public static class FtfCommandTrace", trace);
        Assert.Contains("public static int StartedCount(string command)", trace);
        Assert.Contains("public static int FinishedCount(string command)", trace);

        var contour = Read("src", "FieldCodes.Cad", "ContourCommands.cs");
        Assert.Contains("FtfCommandTrace.Start(\"FTFCONTOUR\");", contour);
        Assert.Contains("FtfCommandTrace.Finish(\"FTFCONTOUR\");", contour);
    }

    [Fact]
    public void TheIdentityCheckIsRequiredNotReported()
    {
        var runner = Code(Read("deploy", "smoke-test.ps1"));
        var start = runner.IndexOf("function Invoke-IdentityCheck", StringComparison.Ordinal);
        var end = runner.IndexOf("try { Invoke-IdentityCheck }", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "smoke-test.ps1 must define and run Invoke-IdentityCheck");
        var body = runner.Substring(start, end - start);
        Assert.Contains("FTF_IDENTITY_RESULT", body);
        Assert.Contains("tests\\LiveSmoke\\IdentityTestHarness.cs", body);
        Assert.Contains("'COMPLETE'", body);
        Assert.Contains("FATAL", body);
        Assert.Contains("Unknown command", body);
        Assert.Contains("taskkill.exe /PID $p.Id /T /F", body);
        foreach (var stage in new[] { "READER", "LINES", "AGAIN", "REVIEW" })
            Assert.Contains("'" + stage + "'", body);
        Assert.DoesNotContain("not blocking", body);
        Assert.True(Regex.Matches(body, @"\bFail \(").Count >= 5, "every failed outcome of the identity check must call Fail");

        // A failure that throws is a failure too, and the check runs before the reported-only style
        // check, which must not be able to swallow it.
        Assert.Matches(@"try \{ Invoke-IdentityCheck \}\s*catch \{ Fail \(", runner);
        Assert.True(end < runner.IndexOf("try { Invoke-StyleCheck }", StringComparison.Ordinal));
    }

    [Fact]
    public void TheIdentityHarnessProvesEachCommandRanBeforeCheckingIt()
    {
        var harness = Read("tests", "LiveSmoke", "IdentityTestHarness.cs");
        Assert.Contains("FTF_IDENTITY_RESULT", harness);
        Assert.Contains("Environment.Exit(3)", harness);
        Assert.Contains("FieldCodes.Cad is loaded \" + loaded.Count + \" times", harness);
        Assert.Contains("else if (stage == \"LINES\") { RequireRanOnce(\"FTFLINES\");", harness);
        Assert.Contains("else if (stage == \"AGAIN\") { RequireRanOnce(\"FTFLINES\");", harness);
        Assert.Contains("else if (stage == \"REVIEW\") { RequireRanOnce(\"FTFREVIEWCHECK\");", harness);
        Assert.Contains("never ran (Unknown command", harness);

        // FTFLINES does not go through FtfSession.Run, so it is traced itself; FTFREVIEWCHECK is
        // traced by FtfSession.Run.
        var lines = Read("src", "FieldCodes.Cad", "LineworkCommands.cs");
        Assert.Matches(@"FtfCommandTrace\.Start\(""FTFLINES""\);\s*Lines\(\);\s*FtfCommandTrace\.Finish\(""FTFLINES""\);", lines);
        Assert.Contains("FtfSession.Run(\"FTFREVIEWCHECK\"", Read("src", "FieldCodes.Cad", "ReviewCommands.cs"));
    }

    [Fact]
    public void EveryHarnessTheSmokeTestCompilesIsCompileCheckedAndAvoidsAeccMgd()
    {
        // smoke-test.ps1 and run-figures.ps1 compile with csc against accoremgd, acdbmgd, acmgd,
        // AeccDbMgd and AecBaseMgd only. CivilApplication lives in AeccMgd: a harness using it fails every deploy.
        var runners = new[] { Code(Read("deploy", "smoke-test.ps1")), Code(Read("tests", "LiveSmoke", "run-figures.ps1")) };
        var compiled = runners.SelectMany(r => Regex.Matches(r, @"tests\\LiveSmoke\\(\w+)\.cs").Select(m => m.Groups[1].Value)).Distinct().ToList();
        Assert.Contains("IdentityTestHarness", compiled);
        Assert.Contains("FigureFixtureHarness", compiled);
        Assert.All(runners, r => Assert.DoesNotContain("AeccMgd.dll", r));
        var check = Read("tests", "SmokeHarnessCompileCheck", "SmokeHarnessCompileCheck.csproj");
        Assert.Contains("<LangVersion>5</LangVersion>", check);
        foreach (var name in compiled)
        {
            Assert.Contains(@"..\LiveSmoke\" + name + ".cs", check);
            Assert.DoesNotContain("CivilApplication", Read("tests", "LiveSmoke", name + ".cs"));
        }
    }

    [Fact]
    public void TheSmokeScriptIsGeneratedNotHardCoded()
    {
        Assert.False(File.Exists(Path.Combine(Repo(), "tests", "LiveSmoke", "runcontour.scr")),
                     "a hard-coded script would NETLOAD a fixed bin\\Debug path, not the build under test");
        Assert.Contains("deploy\\smoke-test.ps1", Read("tests", "LiveSmoke", "run-contour.ps1"));
    }

    // ----------------------------------------------------------------- saving local work

    [Fact]
    public void SaveLocalWorkLeavesTheWorkingCopyAlone()
    {
        var code = Code(Read("deploy", "Save-LocalWork.ps1"));
        Assert.Contains("$env:GIT_INDEX_FILE = $tempIndex", code);           // a temporary index
        foreach (var verb in new[] { "'stash'", "'reset'", "'checkout'", "'switch'", "'clean'", "'commit', " })
            Assert.DoesNotContain(verb, code);
        Assert.Contains("'commit-tree'", code);
    }
}
