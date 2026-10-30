using System.Diagnostics;
using System.Reflection;
using FieldCodes.Deploy;

namespace FieldCodes.Tests;

/// <summary>
/// Version information: every FTF assembly carries the commit, branch, date and channel it was
/// built from (build\FtfBuildInfo.targets); FTFVERSION compares the running build with the
/// installed one and the latest approved one and says plainly what, if anything, to do.
/// </summary>
public sealed class BuildStampTests
{
    private const string CommitA = "5c3c0218830d94574671bd690a5df4ce559afdd6";
    private const string CommitB = "a37d4cd9b0f1c2d3e4f5a6b7c8d9e0f1a2b3c4d5";

    private static BuildStamp Stamp(string commit, string channel = BuildStamp.Approved, bool dirty = false) =>
        new() { Commit = commit, Branch = "integration", Channel = channel, Dirty = dirty,
                BuiltUtc = new DateTime(2026, 10, 30, 14, 5, 0, DateTimeKind.Utc), Version = "1.0.0" };

    // ----------------------------------------------------------------- the stamp itself

    [Fact]
    public void TheLibraryThisTestRunsAgainstIsStamped()
    {
        // Built from this repository by the same targets file the plugin imports.
        var stamp = BuildStamp.FromAssembly(typeof(RulesConfig).Assembly);
        Assert.True(stamp.HasCommit, "FieldCodes.dll carries no FtfCommit -- build\\FtfBuildInfo.targets did not run");
        Assert.Matches("^[0-9a-f]{40}$", stamp.Commit);
        Assert.NotNull(stamp.BuiltUtc);
        Assert.True(stamp.Channel == BuildStamp.Dev || stamp.Channel == BuildStamp.Approved);
        Assert.NotNull(stamp.Dirty);

        var product = FileVersionInfo.GetVersionInfo(typeof(RulesConfig).Assembly.Location).ProductVersion;
        Assert.Contains("+" + stamp.ShortCommit + "." + stamp.Channel, product);
    }

    [Fact]
    public void MetadataIsReadExactlyAndUnknownsStayUnknown()
    {
        var stamp = BuildStamp.FromMetadata(new Dictionary<string, string>
        {
            ["FtfCommit"] = CommitA.ToUpperInvariant(), ["FtfBranch"] = "integration", ["FtfChannel"] = "Approved",
            ["FtfBuildDate"] = "2026-10-30T14:05:12Z", ["FtfDirty"] = "false"
        }, "1.0.0+5c3c021.approved", null);

        Assert.Equal(CommitA, stamp.Commit);
        Assert.Equal("5c3c021", stamp.ShortCommit);
        Assert.Equal("approved", stamp.Channel);
        Assert.Equal(new DateTime(2026, 10, 30, 14, 5, 12, DateTimeKind.Utc), stamp.BuiltUtc);
        Assert.Equal(DateTimeKind.Utc, stamp.BuiltUtc!.Value.Kind);
        Assert.False(stamp.Dirty);
        Assert.Equal("1.0.0", stamp.Version);
        Assert.True(stamp.IsApproved);

        var unknown = BuildStamp.FromMetadata(new Dictionary<string, string>
        {
            ["FtfCommit"] = "unknown", ["FtfBranch"] = "unknown", ["FtfDirty"] = "unknown", ["FtfBuildDate"] = "soon"
        }, null, "1.0.0");
        Assert.False(unknown.HasCommit);
        Assert.Null(unknown.Branch);
        Assert.Null(unknown.Dirty);
        Assert.Null(unknown.BuiltUtc);
        Assert.False(unknown.IsApproved);
    }

    [Theory]
    [InlineData("approved", false, true)]
    [InlineData("approved", true, false)]   // uncommitted changes are never an approved release
    [InlineData("dev", false, false)]
    [InlineData(null, false, false)]
    public void OnlyACleanApprovedBuildIsApproved(string? channel, bool dirty, bool approved)
    {
        Assert.Equal(approved, Stamp(CommitA, channel!, dirty).IsApproved);
    }

    [Fact]
    public void TheBundleFileRoundTrips()
    {
        var stamp = Stamp(CommitA);
        stamp.Configuration = "Release";
        stamp.Files = new Dictionary<string, string> { ["Contents/2024/FieldCodes.Cad.dll"] = "ABC123" };
        var back = BuildStamp.FromJson(stamp.ToJson())!;
        Assert.True(back.SameBuildAs(stamp));
        Assert.Equal(stamp.BuiltUtc, back.BuiltUtc);
        Assert.Equal("Release", back.Configuration);
        Assert.Equal("ABC123", back.Files!["Contents/2024/FieldCodes.Cad.dll"]);
        Assert.Contains("\"builtUtc\": \"2026-10-30T14:05:00Z\"", stamp.ToJson());

        Assert.Null(BuildStamp.FromJson(""));
        Assert.Null(BuildStamp.FromJson("{ not json"));
        Assert.False(BuildStamp.FromJson("{ \"commit\": \"abc\" }")!.HasCommit);
    }

    [Fact]
    public void SameBuildMeansSameCommitChannelAndCleanliness()
    {
        Assert.True(Stamp(CommitA).SameBuildAs(Stamp(CommitA)));
        Assert.False(Stamp(CommitA).SameBuildAs(Stamp(CommitB)));
        Assert.False(Stamp(CommitA).SameBuildAs(Stamp(CommitA, BuildStamp.Dev)));
        Assert.False(Stamp(CommitA).SameBuildAs(Stamp(CommitA, BuildStamp.Approved, dirty: true)));
        Assert.False(new BuildStamp().SameBuildAs(new BuildStamp()));
    }

    [Fact]
    public void DescribeSaysWhatKindOfBuildItIs()
    {
        Assert.Equal("approved build 5c3c021 (integration), built 2026-10-30 14:05 UTC", Stamp(CommitA).Describe());
        Assert.StartsWith("DEV build 5c3c021", Stamp(CommitA, BuildStamp.Dev).Describe());
        Assert.StartsWith("DEV build with uncommitted changes", Stamp(CommitA, BuildStamp.Approved, dirty: true).Describe());
    }

    // ----------------------------------------------------------------- what FTFVERSION says

    private static ApprovedHead Latest(string commit) =>
        new() { Commit = commit, Branch = "integration", Subject = "Add FTFCONTOUR",
                CheckedUtc = new DateTime(2026, 10, 30, 15, 0, 0, DateTimeKind.Utc) };

    [Fact]
    public void TheLatestApprovedBuildRunningFromTheBundleIsSaidToBeCurrent()
    {
        var verdicts = VersionReport.Verdicts(Stamp(CommitA), true, Stamp(CommitA), Latest(CommitA));
        Assert.Equal(new[] { "STATUS: running the latest approved build." }, verdicts);
    }

    [Fact]
    public void ADevelopmentBuildLoadedByHandIsFlagged()
    {
        var verdicts = string.Join("\n", VersionReport.Verdicts(Stamp(CommitB, BuildStamp.Dev), false, Stamp(CommitA), Latest(CommitA)));
        Assert.Contains("WARNING: this session loaded FTF from outside the installed bundle", verdicts);
        Assert.Contains("RESTART: the installed FTF is 5c3c021 but this session is running a37d4cd", verdicts);
        Assert.Contains("NOTE: a development build", verdicts);
        Assert.DoesNotContain("UPDATE:", verdicts);   // the latest is already installed; a restart is the fix
    }

    [Fact]
    public void ANewerApprovedBuildIsPointedOut()
    {
        var verdicts = string.Join("\n", VersionReport.Verdicts(Stamp(CommitA), true, Stamp(CommitA), Latest(CommitB)));
        Assert.Contains("UPDATE: a newer approved build (a37d4cd) is on the integration branch. Run UPDATEFTF.ps1", verdicts);
    }

    [Fact]
    public void AnInstalledButNotYetLoadedUpdateAsksForARestart()
    {
        var verdicts = string.Join("\n", VersionReport.Verdicts(Stamp(CommitA), true, Stamp(CommitB), Latest(CommitB)));
        Assert.Contains("RESTART:", verdicts);
        Assert.DoesNotContain("UPDATE:", verdicts);
    }

    [Fact]
    public void ABuildFromBeforeStampingSaysSo()
    {
        var verdicts = VersionReport.Verdicts(new BuildStamp(), true, null, Latest(CommitA));
        Assert.Single(verdicts);
        Assert.StartsWith("STATUS: this FTF carries no build information", verdicts[0]);
    }

    [Fact]
    public void WithoutUpdateFtfHistoryItStillSaysWhatIsRunning()
    {
        var verdicts = VersionReport.Verdicts(Stamp(CommitA), true, Stamp(CommitA), null);
        Assert.Single(verdicts);
        Assert.StartsWith("STATUS: running the installed approved build.", verdicts[0]);
    }

    [Fact]
    public void TheFullReportListsEveryFact()
    {
        var text = string.Join("\n", VersionReport.Lines(Stamp(CommitA), @"C:\Users\x\AppData\Roaming\Autodesk\ApplicationPlugins\FieldToFinish.bundle\Contents\2024\FieldCodes.Cad.dll",
                                                         true, Stamp(CommitA), @"C:\Users\x\AppData\Roaming\Autodesk\ApplicationPlugins\FieldToFinish.bundle", Latest(CommitA)));
        Assert.Contains("Field to Finish -- approved build 5c3c021 (integration), built 2026-10-30 14:05 UTC", text);
        Assert.Contains("commit:    " + CommitA, text);
        Assert.Contains(@"loaded:    C:\Users\x\AppData\Roaming\Autodesk\ApplicationPlugins\FieldToFinish.bundle\Contents\2024\FieldCodes.Cad.dll", text);
        Assert.Contains("installed: approved build 5c3c021", text);
        Assert.Contains("latest approved: 5c3c021 \"Add FTFCONTOUR\" (as of UPDATEFTF 2026-10-30 15:00 UTC)", text);
        Assert.Contains("STATUS: running the latest approved build.", text);
    }

    [Fact]
    public void TheApprovedHeadFileIsValidated()
    {
        Assert.NotNull(ApprovedHead.FromJson("{ \"commit\": \"" + CommitA + "\", \"branch\": \"integration\" }"));
        Assert.Null(ApprovedHead.FromJson("{ \"commit\": \"main\" }"));
        Assert.Null(ApprovedHead.FromJson("garbage"));
    }
}
