using System.Text.RegularExpressions;

namespace FieldCodes.Tests;

/// <summary>
/// TRACE installs and runs beside FieldToFinish. These checks prove the two share no command
/// name and no stored-data name, and that the internal edition still registers exactly the
/// commands it always did.
/// </summary>
public sealed class RuntimeIndependenceTests
{
    // The commands the internal edition registered at the baseline commit (d8db9db).
    private static readonly string[] BaselineCommands =
    {
        "CONSTRUCTIONAREA",
        "FTF",
        "FTFAREA",
        "FTFB",
        "FTFBLOCKS",
        "FTFCLEAN",
        "FTFCONFIG",
        "FTFCONTROL",
        "FTFDIP",
        "FTFDIPACT",
        "FTFDIPCHECK",
        "FTFDIPCONFIRM",
        "FTFDIPCONNECT",
        "FTFDIPDRAW",
        "FTFDIPINSPECT",
        "FTFDIPLABEL",
        "FTFDIPNOTES",
        "FTFDIPREVISIT",
        "FTFDRAFTCLEAN",
        "FTFDRAWLINE",
        "FTFDRIP",
        "FTFEASEMENT",
        "FTFEASEMENTCHECK",
        "FTFEASEMENTCOMPONENT",
        "FTFEASEMENTEXCLUDE",
        "FTFEASEMENTEXPORT",
        "FTFEASEMENTGROUP",
        "FTFEASEMENTINSPECT",
        "FTFEASEMENTLEGAL",
        "FTFEXHIBIT",
        "FTFEXHIBITINSPECT",
        "FTFEXHIBITPREVIEW",
        "FTFEXHIBITQA",
        "FTFEXHIBITREBUILD",
        "FTFEXHIBITSTAMP",
        "FTFKEYMAP",
        "FTFL",
        "FTFLABELBETWEEN",
        "FTFLABELLINE",
        "FTFLABELS",
        "FTFLABELSTAIRS",
        "FTFLEGEND",
        "FTFLINELABELS",
        "FTFLINEMETA",
        "FTFLINES",
        "FTFOPTIONS",
        "FTFORDER",
        "FTFPOINTS",
        "FTFPORTION",
        "FTFPROFILE",
        "FTFRUN",
        "FTFS",
        "FTFSETTINGS",
        "FTFSETUP",
        "FTFSHEETMAKE",
        "FTFSHEETPLAN",
        "FTFSHEETS",
        "FTFSPOT",
        "FTFTABLE",
        "FTFTAGS",
        "FTFTREES",
        "FTFWHERE",
        "PORTIONEASEMENT",
        "STRIPEASEMENT"
    };

    private static string Repo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "FieldToFinish.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repository root not found");
    }

    private static string CommandNamesSource()
        => File.ReadAllText(Path.Combine(Repo(), "src", "FieldCodes.Cad", "CommandNames.cs"));

    private static List<string> Values(string section)
        => Regex.Matches(section, "public const string \\w+ = \"([^\"]+)\";").Select(m => m.Groups[1].Value).ToList();

    private static (string Trace, string Internal) Branches()
    {
        var text = CommandNamesSource();
        var ifAt = text.IndexOf("#if TRACE_EDITION", StringComparison.Ordinal);
        var elseAt = text.IndexOf("#else", StringComparison.Ordinal);
        var endAt = text.IndexOf("#endif", StringComparison.Ordinal);
        return (text.Substring(ifAt, elseAt - ifAt), text.Substring(elseAt, endAt - elseAt));
    }

    internal static List<string> InternalBranch() => Values(Branches().Internal);

    internal static List<string> TraceBranch() => Values(Branches().Trace).Concat(new[] { "TRACE", "TRACERUN" }).ToList();

    [Fact]
    public void TheInternalEditionRegistersExactlyTheOriginalCommands()
        => Assert.Equal(BaselineCommands.OrderBy(x => x), Values(Branches().Internal).OrderBy(x => x));

    [Fact]
    public void EveryCommandAttributeUsesTheNameTable()
    {
        var literal = new Regex("\\[CommandMethod\\(\"");
        foreach (var f in Directory.GetFiles(Path.Combine(Repo(), "src", "FieldCodes.Cad"), "*.cs", SearchOption.AllDirectories))
        {
            if (f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)) continue;
            if (f.Contains(Path.DirectorySeparatorChar + "Trace" + Path.DirectorySeparatorChar)) continue;   // TRACE-only commands
            Assert.False(literal.IsMatch(File.ReadAllText(f)), Path.GetFileName(f) + " registers a command by literal name");
        }
    }

    [Fact]
    public void TraceRegistersNoCommandNameTheInternalEditionUses()
    {
        var trace = TraceBranch();
        Assert.Equal(trace.Count, trace.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(trace, n => Assert.StartsWith("TRACE", n));
        Assert.All(trace, n => Assert.DoesNotContain("FTF", n));
        Assert.Empty(trace.Intersect(BaselineCommands, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void CommandsThatLaunchOtherCommandsUseTheNameTable()
    {
        var literal = new Regex("SendStringToExecute\\(\"_?FTF");
        foreach (var f in Directory.GetFiles(Path.Combine(Repo(), "src", "FieldCodes.Cad"), "*.cs", SearchOption.AllDirectories))
        {
            if (f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)) continue;
            if (Path.GetFileName(f) == "FtfRibbon.cs") continue;       // the internal ribbon is never built in the TRACE edition
            Assert.False(literal.IsMatch(File.ReadAllText(f)), Path.GetFileName(f) + " launches a command by literal FTF name");
        }
    }

    [Fact]
    public void StoredDataNamesDifferAndTheInternalOneIsUnchanged()
    {
        var text = File.ReadAllText(Path.Combine(Repo(), "src", "FieldCodes.Cad", "DataNames.cs"));
        Assert.Contains("public const string Root = \"TRACE_OBJECTS\";", text);
        Assert.Contains("public const string Root = \"FTF_FIELDTOFINISH\";", text);
    }

    [Fact]
    public void FileNamesFollowTheEditionAndTheInternalOnesAreUnchanged()
    {
        // Unit tests run unstamped, which is the internal edition.
        Assert.Equal("ftf", Branding.FilePrefix);
        Assert.Equal("ftf-settings.json", FieldCodes.Settings.FtfSettings.FileName);
        Assert.Equal(".ftf-exceptions.csv", FieldCodes.Reporting.ExceptionReport.Suffix);
        Assert.Equal(".ftf-unhandled.csv", FieldCodes.Reporting.UnhandledSummary.Suffix);
        Assert.Equal(".ftf-linework.csv", FieldCodes.Linework.LineworkInventoryCsv.Suffix);
    }

    [Fact]
    public void TheOneStepInstallerBuildsAndInstallsOnlyTheTraceBundle()
    {
        var script = File.ReadAllText(Path.Combine(Repo(), "deploy", "Install-Trace.ps1"));
        Assert.Contains("-p:TraceEdition=Default", script);
        Assert.Contains("install-dev.ps1", script);
        Assert.Contains("'-Edition', 'Default'", script);
        Assert.DoesNotContain("FieldToFinish.bundle", script.Replace("FieldToFinish.bundle is not touched", ""));
        Assert.DoesNotContain("Rename-Item", script);
        Assert.DoesNotContain("Remove-Item", script);
    }
}
