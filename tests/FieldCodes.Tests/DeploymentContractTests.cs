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
        Then("deploy\\test-installer.ps1') @()");
        Then("VALIDATED, NOT YET INSTALLED");
        Then("Copy-Item -LiteralPath $installedDir -Destination $script:backup -Recurse -Force");
        Then("$installArgs = @('-Source', $setup, '-NoOfficeCopy'");
        Then("(Join-Path $setup 'install.ps1') $installArgs");
        Then("$problem = Verify-Installed $target");
        Then("deploy\\smoke-test.ps1') @('-PluginDll', $installedDll, '-Commit', $target");

        Assert.Matches(@"if \(\$smoke\.Code -ne 0\) \{\s*Restore-Backup", code);
        // A development build installs on this workstation only because UPDATEFTF says so.
        Assert.Contains("if ($channel -ne 'approved') { $installArgs += '-AllowDevelopmentBuild' }", code);
        Assert.Contains("channel = 'approved'", code);
        Assert.Matches(@"if \(\$Ref\) \{ Fail '-Ref builds a development ref and needs -Dev", code);
    }

    [Fact]
    public void TheRestoreAfterAFailedSmokeTestIsInTheLog()
    {
        // At 6aca731 the screen said "Restored: ..." but the log, written with Write-Host only, did
        // not: the log showed only reg.exe's output and a blank stderr line logged as
        // "System.Management.Automation.RemoteException".
        var code = Code(Read("UPDATEFTF.ps1"));
        Assert.Matches(@"function Say\(\[string\]\$text, \[string\]\$color\) \{ Write-Host \$text -ForegroundColor \$color; Log \$text \}", code);
        var restore = Regex.Match(code, @"function Restore-Backup[\s\S]*?\r?\n\}\r?\n").Value;
        Assert.Contains("Say \"   Putting the previous FTF back ($why)...\"", restore);
        Assert.Contains("Say (\"   Restored: \" + (Describe-Build (Read-BuildInfo $installedDir)))", restore);
        Assert.Contains("Say \"   The failed build is kept for diagnosis in $failedDir\"", restore);
        // Every message of the restore goes to the log, not only to the screen.
        Assert.DoesNotMatch(@"Write-Host [\(""]", restore);
        // A startup entry that does not import is said, with how to import it by hand.
        Assert.Contains("if ($imported.Code -ne 0) { Say", restore);
        // A stderr line is logged as its text.
        var native = Regex.Match(code, @"function Invoke-Native[\s\S]*?\r?\n\}\r?\n").Value;
        Assert.Contains("if ($_ -is [System.Management.Automation.ErrorRecord]) { $_.Exception.Message } else { \"$_\" }", native);
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
    public void TheRequiredChecksAreRequiredNotReported()
    {
        var runner = Code(Read("deploy", "smoke-test.ps1"));
        var start = runner.IndexOf("function Invoke-RequiredCheck", StringComparison.Ordinal);
        var end = runner.IndexOf("function Invoke-IdentityCheck", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "smoke-test.ps1 must define Invoke-RequiredCheck before the checks that use it");
        var body = runner.Substring(start, end - start);
        Assert.Contains("'COMPLETE'", body);
        Assert.Contains("FATAL", body);
        Assert.Contains("Unknown command", body);
        Assert.Contains("taskkill.exe /PID $p.Id /T /F", body);
        Assert.Contains("rules.json", body);                                    // the build's own rules, not the office's
        Assert.Contains("RESULT PASS", body);
        Assert.Contains("STAGE $_ PASS", body);
        Assert.DoesNotContain("not blocking", body);
        Assert.True(Regex.Matches(body, @"\bFail \(").Count >= 7, "every failed outcome of a required check must call Fail");

        var identity = Between(runner, "function Invoke-IdentityCheck", "function Invoke-AccountabilityCheck");
        Assert.Contains("Invoke-RequiredCheck 'identity' 'IdentityTestHarness' 'FTF_IDENTITY_RESULT'", identity);
        foreach (var stage in new[] { "READER", "LINES", "AGAIN", "REVIEW" })
            Assert.Contains("'" + stage + "'", identity);

        var accountability = Between(runner, "function Invoke-AccountabilityCheck", "try { Invoke-IdentityCheck }");
        Assert.Contains("Invoke-RequiredCheck 'accountability' 'AccountabilityTestHarness' 'FTF_ACCOUNT_RESULT'", accountability);
        foreach (var stage in new[] { "RUN", "AGAIN", "STANDARD", "RESTORED", "SURFACE", "SURFACEOFF", "APPROVED", "KEPT", "CHANGED", "REFUSED", "QC", "DELETED" })
            Assert.Contains("'ACCVERIFY', '" + stage + "'", accountability);
        // The drafting standard and the surface check are changed by the harness, then put back.
        Assert.Contains("'ACCSTANDARD', 'Label',\n        'FTFREVIEWCHECK',\n        'ACCVERIFY', 'STANDARD'", accountability.Replace("\r\n", "\n"));
        Assert.Contains("'ACCSTANDARD', 'Restore',\n        'FTFREVIEWCHECK',\n        'ACCVERIFY', 'RESTORED'", accountability.Replace("\r\n", "\n"));
        Assert.Contains("'ACCSURFACE', 'On',\n        'FTFREVIEWCHECK',\n        'ACCVERIFY', 'SURFACE'", accountability.Replace("\r\n", "\n"));
        Assert.Contains("'ACCSURFACE', 'Off',\n        'FTFREVIEWCHECK',\n        'ACCVERIFY', 'SURFACEOFF'", accountability.Replace("\r\n", "\n"));
        Assert.Contains("'FTFRUN', 'Standard'", accountability);
        Assert.Contains("'-FTFREVIEW', 'Approve', 'line.gap:FNC:103-105'", accountability);
        Assert.Contains("'FTFQC', 'Complete'", accountability);

        // A failure that throws is a failure too.
        Assert.Matches(@"try \{ Invoke-IdentityCheck \}\s*catch \{ Fail \(", runner);
        Assert.Matches(@"try \{ Invoke-AccountabilityCheck \}\s*catch \{ Fail \(", runner);
        Assert.Matches(@"try \{ Invoke-StyleCheck \}\s*catch \{ Fail \(", runner);
        var style = runner.IndexOf("try { Invoke-StyleCheck }", StringComparison.Ordinal);
        Assert.True(runner.IndexOf("try { Invoke-AccountabilityCheck }", StringComparison.Ordinal) < style);

        // The style check is required too: the same runner, its stages proven, no "not blocking".
        var styles = Between(runner, "function Invoke-StyleCheck", "try { Invoke-StyleCheck }");
        Assert.Contains("Invoke-RequiredCheck 'style' 'StyleTestHarness' 'FTF_STYLE_RESULT'", styles);
        Assert.Contains("@('STYLED', 'PREVIEW', 'RESTYLED')", styles);
        Assert.Contains("'FTFRESTYLE', 'Preview',", styles);
        Assert.Contains("'FTFRESTYLE', 'Apply',", styles);
        Assert.DoesNotContain("not blocking", styles);
        Assert.DoesNotContain("STYLE CHECK FAILED", runner);
        // The harness proves each command ran before it judges the stage.
        var harness = Read("tests", "LiveSmoke", "StyleTestHarness.cs");
        Assert.Contains("if (stage == \"STYLED\") RequireRanOnce(\"FTFLABELS\");", harness);
        Assert.Contains("else if (stage == \"PREVIEW\" || stage == \"RESTYLED\") RequireRanOnce(\"FTFRESTYLE\");", harness);
    }

    private static string Between(string text, string from, string to)
    {
        var a = text.IndexOf(from, StringComparison.Ordinal);
        var b = text.IndexOf(to, a < 0 ? 0 : a, StringComparison.Ordinal);
        Assert.True(a >= 0 && b > a, "expected \"" + from + "\" before \"" + to + "\"");
        return text.Substring(a, b - a);
    }

    [Fact]
    public void TheAccountabilityHarnessProvesEachCommandRanBeforeCheckingIt()
    {
        var harness = Read("tests", "LiveSmoke", "AccountabilityTestHarness.cs");
        Assert.Contains("FTF_ACCOUNT_RESULT", harness);
        Assert.Contains("Environment.Exit(3)", harness);
        Assert.Contains("FieldCodes.Cad is loaded \" + loaded.Count + \" times", harness);
        Assert.Contains("if (stage == \"RUN\") { RequireRanOnce(\"FTFRUN\"); RequireRanOnce(\"FTFREVIEWCHECK\");", harness);
        Assert.Contains("else if (stage == \"APPROVED\") { RequireRanOnce(\"-FTFREVIEW\");", harness);
        foreach (var stage in new[] { "STANDARD", "RESTORED", "SURFACE", "SURFACEOFF" })
            Assert.Contains("else if (stage == \"" + stage + "\") { RequireRanOnce(\"FTFREVIEWCHECK\");", harness);
        Assert.Contains("else if (stage == \"QC\") { RequireRanOnce(\"-FTFREVIEW\"); RequireRanOnce(\"FTFQC\");", harness);
        Assert.Contains("Enum.Parse(kinds, \"LineCorrection\")", harness);
        Assert.Contains("Enum.Parse(kinds, \"LineGapProposal\")", harness);

        // What the stages rely on: FTFRUN runs the checks through FtfSession as FTFREVIEWCHECK, and
        // the command-line review is traced.
        var checks = Read("src", "FieldCodes.Cad", "Accountability.cs");
        Assert.Contains("FtfSession.Run(\"FTFREVIEWCHECK\"", checks);
        Assert.Contains("PostRunChecks.AfterProcessing(\"FTFRUN \"", Read("src", "FieldCodes.Cad", "FtfCommand.cs"));
        Assert.Contains("PostRunChecks.AfterProcessing(\"FTFLABELALL\")", Read("src", "FieldCodes.Cad", "LabelAllCommand.cs"));
        var review = Read("src", "FieldCodes.Cad", "ReviewCommands.cs");
        Assert.Contains("[CommandMethod(\"-FTFREVIEW\", CommandFlags.Modal)]", review);
        Assert.Contains("FtfSession.Run(\"-FTFREVIEW\"", review);
        Assert.Contains("var checks = PostRunChecks.Run(db, tr, cfg, settings, \"FTFQC\");", review);
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

        // FTFLINES does not go through FtfSession.Run, so it is traced itself (an exception is
        // recorded in the trace and still thrown, and then it never counts as finished);
        // FTFREVIEWCHECK is traced by FtfSession.Run.
        var lines = Read("src", "FieldCodes.Cad", "LineworkCommands.cs");
        Assert.Matches(@"FtfCommandTrace\.Start\(""FTFLINES""\);\s*try \{ Lines\(\); \}\s*catch \(System\.Exception ex\)\s*\{[^{}]*FtfCommandTrace\.Failed\(""FTFLINES"", ex\);\s*throw;\s*\}\s*FtfCommandTrace\.Finish\(""FTFLINES""\);", lines);
        Assert.Contains("FtfSession.Run(\"FTFREVIEWCHECK\"", Read("src", "FieldCodes.Cad", "ReviewCommands.cs"));
    }

    [Fact]
    public void EveryHarnessTheSmokeTestCompilesIsCompileCheckedAndAvoidsAeccMgd()
    {
        // smoke-test.ps1 and run-figures.ps1 compile with csc against accoremgd, acdbmgd, acmgd,
        // AeccDbMgd and AecBaseMgd only. CivilApplication lives in AeccMgd: a harness using it fails every deploy.
        var runners = new[] { Code(Read("deploy", "smoke-test.ps1")), Code(Read("tests", "LiveSmoke", "run-figures.ps1")) };
        var compiled = runners.SelectMany(r => Regex.Matches(r, @"tests\\LiveSmoke\\(\w+)\.cs").Select(m => m.Groups[1].Value)
                                                 .Concat(Regex.Matches(r, @"Invoke-RequiredCheck '\w+' '(\w+)'").Select(m => m.Groups[1].Value))).Distinct().ToList();
        Assert.Contains("IdentityTestHarness", compiled);
        Assert.Contains("AccountabilityTestHarness", compiled);
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

    // ----------------------------------------------------------------- the approved release folder

    [Fact]
    public void OnlyAnApprovedBuildProvenInCivil3DInTheSameRunIsPublished()
    {
        var code = Code(Read("UPDATEFTF.ps1"));
        // Refused before anything is built.
        Assert.Contains("if ($PublishTo -and $channel -ne 'approved') { Fail", code);
        Assert.Contains("if ($PublishTo -and $SkipSmoke) { Fail", code);
        // An install that is already current is installed and smoke-tested again before publishing.
        Assert.Contains("if (-not $Force -and -not $PublishTo -and $installed", code);
        // Publishing comes after the smoke test passed, and refuses again on its own.
        var smoke = code.IndexOf("Ok 'smoke test passed'", StringComparison.Ordinal);
        var publish = code.IndexOf("(Join-Path $BuildRoot 'deploy\\publish-release.ps1') $pubArgs", StringComparison.Ordinal);
        // The only other call puts a previous release back; it publishes nothing new.
        Assert.Contains("(Join-Path $here 'deploy\\publish-release.ps1') @('-Restore', '-ReleaseFolder', $folderToRestore)", code);
        Assert.True(smoke > 0 && publish > smoke, "publishing must come after a passing smoke test");
        var block = code.Substring(code.IndexOf("if ($PublishTo) {", smoke, StringComparison.Ordinal));
        Assert.Matches(@"if \(\$channel -ne 'approved'\) \{\s*\$notPublished", block);
        Assert.Matches(@"elseif \(\$SkipSmoke\) \{\s*\$notPublished", block);
        // A run that was asked to publish and did not says so in its exit code.
        Assert.Contains("exit 3", code);
        // -Publish takes the office's folder from deploy\release-folder.txt; it is checked before
        // the build, and publishing goes to the \\server\share path the check found.
        Assert.Contains("$PublishTo = Get-OfficeReleaseFolder", code);
        var check = code.IndexOf("$checked = Test-ReleaseFolder $PublishTo", StringComparison.Ordinal);
        var build = code.IndexOf("Step \"Build $short ($channel, Release)\"", StringComparison.Ordinal);
        Assert.True(check > 0 && build > check, "the release folder is checked before the build");
        Assert.Contains("$PublishTo = \"$($checked.Result.folder)\"", code);
        // The office's folder is configured with the scripts by its drive path only: the server
        // behind it is resolved on the publishing workstation and never written into the repository.
        var configured = File.ReadAllLines(Path.Combine(Repo(), "deploy", "release-folder.txt"))
            .Where(l => l.Trim().Length > 0 && !l.TrimStart().StartsWith("#", StringComparison.Ordinal)).ToList();
        Assert.Single(configured);
        Assert.Matches(@"^[A-Za-z]:\\[^\r\n]+\\FieldToFinish-Setup$", configured[0]);
        // -CheckReleaseFolder builds and publishes nothing.
        var only = Regex.Match(code, @"if \(\$CheckReleaseFolder\) \{[\s\S]*?\r?\n\}\r?\n").Value;
        Assert.Contains("exit 0", only);
        Assert.DoesNotContain("publish-release.ps1", only);

        var pub = Code(Read("deploy", "publish-release.ps1"));
        Assert.Contains("Test-FtfBundle $bundle $info -Strict", pub);          // every file, nothing extra
        Assert.Contains("if (-not (Test-FtfApproved $info)) {", pub);            // never a development build
        // Copied into the folder's _publishing, verified there, on the server, and only then
        // switched in; verified again in place, and taken back out if it does not verify.
        var verified = pub.IndexOf("$onServer = @(Test-Release $staging $top)", StringComparison.Ordinal);
        var switched = pub.IndexOf("$failed = Switch-Release $staging $keepAs", StringComparison.Ordinal);
        var inPlace = pub.IndexOf("$live = @(Test-Release $folder $top)", switched > 0 ? switched : 0, StringComparison.Ordinal);
        Assert.True(verified > 0 && switched > verified && inPlace > switched, "verify on the server, switch, verify in place");
        Assert.Matches(@"if \(\$live\.Count -gt 0\) \{\s*\$left = Undo-Switch \$staging \$keepAs", pub);
        // The folder itself never moves (its permissions stay); what it replaced is kept inside it.
        Assert.DoesNotContain("Rename-Item -LiteralPath $folder", pub);
        Assert.Contains("$keepAs = Join-Path $previousRoot (Previous-Name $before)", pub);
        // Installed copies record the folder's \\server\share path.
        Assert.Contains("$folder = Resolve-FtfUncPath $given $DriveMap", pub);
        Assert.Contains("Set-Content -Path (Join-Path $staging 'updates-from.txt') -Value $folder", pub);

        var common = Code(Read("deploy", "setup", "ftf-common.ps1"));
        Assert.Contains("(\"$($info.channel)\" -eq 'approved') -and (-not $info.dirty)", common);
    }

    [Fact]
    public void TheStaffInstallerVerifiesBacksUpAndPutsThePreviousFtfBack()
    {
        var code = Code(Read("deploy", "setup", "install.ps1"));
        var at = 0;
        void Then(string marker)
        {
            var i = code.IndexOf(marker, at, StringComparison.Ordinal);
            Assert.True(i >= 0, "missing, or out of order: " + marker);
            at = i + marker.Length;
        }

        // The release is checked whole before anything is touched...
        // A developer install (-NoOfficeCopy) is the only other way a development build goes in.
        Assert.Contains("if ($NoOfficeCopy) { $AllowDevelopmentBuild = $true; $LeaveOtherLoaders = $true }", code);
        // The release folder is recorded by its \\server\share path.
        Assert.Contains("$network = Resolve-FtfUncPath $office", code);
        Then("$releaseProblems = @(Test-FtfBundle $from $release -Strict)");
        Then("if (-not (Test-FtfApproved $release) -and -not $AllowDevelopmentBuild) {");
        // ...the installed FTF is backed up...
        Then("$backup = Save-Backup (");
        // ...and the copy beside the installed bundle is verified before the swap and again after it.
        var install = Regex.Match(code, @"function Install-Bundle[\s\S]*?\r?\n\}\r?\n").Value;
        Assert.NotEmpty(install);
        var staged = install.IndexOf("$copied = @(Test-FtfBundle $staging $info -Strict:(-not $FromBackup))", StringComparison.Ordinal);
        var placed = install.IndexOf("$placed = @(Test-FtfBundle $target $info)", StringComparison.Ordinal);
        var putBack = install.IndexOf("The previous FTF was put back", StringComparison.Ordinal);
        Assert.True(staged > 0 && placed > staged && putBack > placed, "verify the copy, swap, verify in place, put back on failure");

        // It writes only the bundle, Civil 3D's per-user record and its own state; office and
        // personal settings in %APPDATA%\FieldToFinish are only ever named, never removed.
        foreach (Match m in Regex.Matches(code, @"Remove-Item[^\r\n]*"))
            Assert.DoesNotContain("APPDATA", m.Value);
        // Civil 3D's startup record is written per user; the machine-wide key is only read, for the
        // product name.
        Assert.Contains("[string]$RegistryRoot = 'HKCU:\\SOFTWARE\\Autodesk\\AutoCAD'", code);
        var machine = Regex.Matches(code, @"[^\r\n]*\$ProductRoot[^\r\n]*").Cast<Match>().Select(m => m.Value.Trim()).ToList();
        Assert.Equal(2, machine.Count);
        Assert.Contains(machine, line => line.StartsWith("$where = Join-Path $ProductRoot", StringComparison.Ordinal));
        Assert.Contains("$name = (Get-ItemProperty -Path $where -ErrorAction SilentlyContinue).ProductName", code);
        Assert.Equal(2, Regex.Matches(code, @"\$where\b").Count);      // made, read once, never written
    }

    [Fact]
    public void TheBatchFileStagesTheReleaseLocallyAndStopsWhileCivil3DIsOpen()
    {
        var bat = Read("deploy", "setup", "Install or Update FieldToFinish.bat");
        Assert.Contains("\r\n", bat);                                                       // cmd.exe needs CRLF
        var civil = bat.IndexOf("find /I \"acad.exe\"", StringComparison.Ordinal);
        // The release only: not the releases it replaced, nor one being published.
        var copy = bat.IndexOf("robocopy \"%~dp0.\" \"%STAGE%\" /E /XD \"Previous releases\" \"_publishing\"", StringComparison.Ordinal);
        var run = bat.IndexOf("-File \"%STAGE%\\install.ps1\" -Source \"%~dp0.\"", StringComparison.Ordinal);
        Assert.True(civil > 0 && copy > civil && run > copy, "check Civil 3D, copy locally, then run the local copy");
        Assert.Contains("if errorlevel 8 goto copyfailed", bat);
        // It works when opened by its \\server\share path, where cmd.exe has no current folder:
        // every path is built from the batch file's own location (%~dp0) or %TEMP%, never "cd".
        foreach (var line in bat.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("rem", StringComparison.OrdinalIgnoreCase)))
        {
            Assert.False(Regex.IsMatch(line, @"^(cd|chdir|pushd|popd)\b", RegexOptions.IgnoreCase), "changes folder: " + line);
            Assert.DoesNotMatch(@"(?i)\b[A-Z]:\\", line);                                     // no drive letter, U: included
        }
        Assert.DoesNotContain("NETLOAD", bat.ToUpperInvariant().Replace("NO NETLOAD", string.Empty));
        // Staff never install a development build: neither the batch file nor the automatic
        // update passes what allows one.
        foreach (var dev in new[] { "-NoOfficeCopy", "-AllowDevelopmentBuild" })
        {
            Assert.DoesNotContain(dev, bat);
            Assert.DoesNotContain(dev, Read("src", "FieldCodes.Cad", "OfficeUpdate.cs"));
        }
        Assert.Contains("call \"%~dp0Install or Update FieldToFinish.bat\"", Read("deploy", "setup", "Install FTF.bat"));
    }

    [Fact]
    public void TheReleaseFolderCheckChangesNothingButItsOwnWriteTest()
    {
        var code = Code(Read("deploy", "check-release-folder.ps1"));
        foreach (var verb in new[] { "Set-Acl", "Move-Item", "Copy-Item", "Rename-Item -LiteralPath $folder" })
            Assert.DoesNotContain(verb, code);
        // Only the write test's own folder is ever removed, and it is removed whatever happens.
        var removes = Regex.Matches(code, @"Remove-Item[^\r\n]*").Cast<Match>().ToList();
        Assert.NotEmpty(removes);
        Assert.All(removes, m => Assert.Matches(@"-LiteralPath \$(moved|left) ", m.Value));
        Assert.Matches(@"\} finally \{\s*foreach \(\$left in @\(\$probe, \$moved\)\)", code);
        // What it writes besides: the result, for UPDATEFTF.
        Assert.Single(Regex.Matches(code, @"Set-Content "));
        Assert.Contains("Set-Content -Path $ResultFile", code);
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
