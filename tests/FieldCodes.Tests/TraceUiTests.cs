using System.Text.RegularExpressions;
using FieldCodes.TraceUi;

namespace FieldCodes.Tests;

/// <summary>
/// The ribbon and palette are built from TraceCatalog. These tests keep that list honest:
/// every button runs a command that exists, nothing is a placeholder, no old internal
/// command name leaks into user-visible text, and the remembered selections behave.
/// </summary>
public sealed class TraceUiTests
{
    private static string Repo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "FieldToFinish.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repository root not found");
    }

    private static IEnumerable<string> CadSources()
        => Directory.GetFiles(Path.Combine(Repo(), "src", "FieldCodes.Cad"), "*.cs", SearchOption.AllDirectories)
                    .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                             && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar));

    private static HashSet<string> CommandNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rx = new Regex("\\[CommandMethod\\(\"([A-Za-z0-9_]+)\"");
        foreach (var f in CadSources())
            foreach (Match m in rx.Matches(File.ReadAllText(f)))
                names.Add(m.Groups[1].Value);
        return names;
    }

    private static readonly string[] ExcludedModulePrefixes =
    {
        "FTFDIP", "FTFEASEMENT", "FTFEXHIBIT", "FTFSHEET", "FTFKEYMAP", "STRIPEASEMENT", "PORTIONEASEMENT", "CONSTRUCTIONAREA", "FTFAREA", "FTFPORTION"
    };

    [Fact]
    public void EveryButtonRunsAnExistingCommand()
    {
        var existing = CommandNames();
        foreach (var b in TraceCatalog.AllButtons)
            Assert.True(existing.Contains(b.Command), "'" + b.Text + "' runs " + b.Command + ", which is not a command in the plugin");
    }

    [Fact]
    public void TraceCommandsTheCatalogRelyOnAreDefined()
    {
        var existing = CommandNames();
        Assert.Contains(TraceCatalog.OpenCommand, existing);
        Assert.Contains(TraceCatalog.RunCommand, existing);
    }

    [Fact]
    public void NoExcludedModuleToolIsOffered()
    {
        foreach (var b in TraceCatalog.AllButtons)
            foreach (var prefix in ExcludedModulePrefixes)
                Assert.False(b.Command.StartsWith(prefix, StringComparison.OrdinalIgnoreCase), b.Text + " belongs to an excluded module");
    }

    [Fact]
    public void NoUserVisibleTextShowsAnInternalCommandName()
    {
        foreach (var g in TraceCatalog.Groups)
            Assert.DoesNotContain("FTF", g.Title, StringComparison.OrdinalIgnoreCase);
        foreach (var b in TraceCatalog.AllButtons)
        {
            Assert.DoesNotContain("FTF", b.Text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("FTF", b.Tip, StringComparison.OrdinalIgnoreCase);
            Assert.False(string.IsNullOrWhiteSpace(b.Tip));
        }
        foreach (var title in TraceCatalog.StepTitles.Values)
            Assert.DoesNotContain("FTF", title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheSixRequestedAreasArePresentAndEachHasALargeButton()
    {
        var titles = TraceCatalog.Groups.Select(g => g.Title).ToList();
        foreach (var wanted in new[] { "Cleanup", "Labels", "Trees", "Linework", "Drafting", "Setup" })
            Assert.Contains(wanted, titles);
        foreach (var g in TraceCatalog.Groups)
            Assert.Contains(g.Buttons, b => b.Size == TraceButtonSize.Large);
    }

    [Fact]
    public void EveryButtonHasADrawnIconThatFitsItsGrid()
    {
        foreach (var b in TraceCatalog.AllButtons)
            Assert.True(TraceIcons.Has(b.Icon), b.Text + " uses unknown icon " + b.Icon);
        foreach (var id in TraceIcons.Ids)
            Assert.True(TraceIcons.FitsGrid(TraceIcons.Get(id)), "icon " + id + " draws outside its grid");
        Assert.True(TraceIcons.Has("trace"));
        Assert.True(TraceIcons.Has("run"));
    }

    [Fact]
    public void RunStepTitlesMatchThePipelineStepsExactly()
    {
        // The step names the pipeline really has, read from its source.
        var source = File.ReadAllText(Path.Combine(Repo(), "src", "FieldCodes.Cad", "FtfPipeline.cs"));
        var steps = Regex.Matches(source, "Name = \"(FTF[A-Z]+)\"").Select(m => m.Groups[1].Value).ToList();
        Assert.NotEmpty(steps);
        Assert.Equal(steps.OrderBy(s => s), TraceCatalog.StepTitles.Keys.OrderBy(s => s));
    }

    [Fact]
    public void ColoursAreTheApprovedPalette()
    {
        Assert.Equal("#0B2E4F", TraceIcons.Hex(TraceRole.Navy, true));
        Assert.Equal("#7CC84A", TraceIcons.Hex(TraceRole.Green, true));
        Assert.Equal("#1E6FB8", TraceIcons.Hex(TraceRole.Accent, true));
        Assert.Equal("#7CC84A", TraceIcons.Hex(TraceRole.Accent, false));
        Assert.Equal("#FFFFFF", TraceIcons.Hex(TraceRole.Main, false));
    }

    // ------------------------------------------------------------ remembered selections

    private static readonly string[] Steps = { "FTFPOINTS", "FTFDRIP", "FTFLINELABELS", "FTFLABELS", "FTFTAGS", "FTFTABLE", "FTFORDER" };

    [Fact]
    public void FirstRunTicksEverythingExceptTheOptionalBulkLinePass()
    {
        var selected = new TraceUiState().SelectedSteps(Steps);
        Assert.DoesNotContain("FTFLINELABELS", selected);
        Assert.Equal(6, selected.Count);
        Assert.Equal("FTFORDER", selected.Last());
    }

    [Fact]
    public void SelectionsAreRememberedAndPipelineOrderAlwaysWins()
    {
        var path = Path.Combine(Path.GetTempPath(), "trace-ui-" + Guid.NewGuid().ToString("N"), TraceUiState.FileName);
        try
        {
            var state = new TraceUiState { PaletteVisible = false };
            state.SetSelectedSteps(new[] { "FTFORDER", "FTFLABELS" });   // saved out of order
            Assert.True(state.Save(path));

            var loaded = TraceUiState.Load(path);
            Assert.Equal(new[] { "FTFLABELS", "FTFORDER" }, loaded.SelectedSteps(Steps));
            Assert.False(loaded.PaletteVisible);
        }
        finally { try { Directory.Delete(Path.GetDirectoryName(path)!, true); } catch (IOException) { } }
    }

    [Fact]
    public void UnticktingEverythingMeansNothingRunsAndUnknownNamesAreIgnored()
    {
        var state = new TraceUiState();
        state.SetSelectedSteps(Array.Empty<string>());
        Assert.Empty(state.SelectedSteps(Steps));

        state.SetSelectedSteps(new[] { "FTFNOSUCHSTEP", "FTFTAGS" });
        Assert.Equal(new[] { "FTFTAGS" }, state.SelectedSteps(Steps));
    }

    [Fact]
    public void AMissingOrDamagedFileFallsBackToDefaultsWithoutFailing()
    {
        var dir = Path.Combine(Path.GetTempPath(), "trace-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            Assert.Null(TraceUiState.Load(Path.Combine(dir, "nope.json")).PaletteVisible);
            var bad = Path.Combine(dir, "bad.json");
            File.WriteAllText(bad, "{ this is not json");
            var state = TraceUiState.Load(bad);
            Assert.Null(state.RunSteps);
            Assert.Null(state.PaletteVisible);
        }
        finally { Directory.Delete(dir, true); }
    }
}

/// <summary>
/// The commercial edition shows TRACE wording wherever the existing tools print or label
/// something. The tools keep their own text; TraceWording is applied where it is shown.
/// </summary>
public sealed class TraceWordingTests
{
    [Fact]
    public void TheMessagesFromTheFieldAreRewrittenWithoutLosingMeaning()
    {
        Assert.Equal(
            "Points: 765 point(s) read -- 19 recognized for TRACE finishing, 198 not configured yet, 0 with errors.",
            TraceWording.Brand("FTFPOINTS: 765 point(s) read -- 19 recognized for FTF finishing, 198 not configured yet, 0 with errors."));
        Assert.Equal(
            "Run Driplines, Labels and Tags for the rest of the finishing.",
            TraceWording.Brand("Run FTFDRIP, FTFLABELS and FTFTAGS for the rest of the finishing."));
        Assert.Equal(
            "Driplines: 0 whole circle(s), 16 arc(s), 7 canopy(ies) fully covered.",
            TraceWording.Brand("FTFDRIP: 0 whole circle(s), 16 arc(s), 7 canopy(ies) fully covered."));
    }

    [Theory]
    [InlineData("FTFLABELLINE", "Label line")]
    [InlineData("FTFCLEAN", "Clean up")]
    [InlineData("FTFRUN", "Run TRACE")]
    [InlineData("FTFSOMETHINGNEW", "TRACE")]
    [InlineData("FTF-owned items", "TRACE-made items")]
    [InlineData("Field to Finish", "TRACE")]
    public void NamesMapToTraceWording(string input, string expected)
        => Assert.Equal(expected, TraceWording.Brand(input));

    [Fact]
    public void TextWithoutInternalNamesAndStoredDataNamesAreLeftAlone()
    {
        Assert.Equal("12 labels placed.", TraceWording.Brand("12 labels placed."));
        Assert.Equal("FTF_FIELDTOFINISH", TraceWording.Brand("FTF_FIELDTOFINISH"));
        Assert.Null(TraceWording.Brand(null));
        Assert.Equal("", TraceWording.Brand(""));
    }

    private static string Repo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "FieldToFinish.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repository root not found");
    }

    [Fact]
    public void EveryCommandNameInThePluginComesOutWithoutAnFtfName()
    {
        var rx = new Regex("\\[CommandMethod\\(\"(FTF[A-Za-z0-9_]*)\"");
        var names = Directory.GetFiles(Path.Combine(Repo(), "src", "FieldCodes.Cad"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
            .SelectMany(f => rx.Matches(File.ReadAllText(f)).Cast<Match>().Select(m => m.Groups[1].Value))
            .Distinct().ToList();
        Assert.NotEmpty(names);
        foreach (var n in names)
            Assert.DoesNotMatch(new Regex(@"\bFTF"), TraceWording.Brand("Run " + n + " again."));
    }

    [Fact]
    public void TheSettingsPagesCaptionsComeOutWithoutAnFtfName()
    {
        var rx = new Regex("AffectedCommands\\s*(?:=>|\\{\\s*get\\s*\\{\\s*return)\\s*\"([^\"]*)\"");
        var captions = Directory.GetFiles(Path.Combine(Repo(), "src", "FieldCodes.Cad", "Setup"), "*.cs")
            .SelectMany(f => rx.Matches(File.ReadAllText(f)).Cast<Match>().Select(m => m.Groups[1].Value))
            .ToList();
        Assert.NotEmpty(captions);
        foreach (var c in captions)
            Assert.DoesNotMatch(new Regex(@"\bFTF"), TraceWording.Brand("Affects: " + c));
    }

    [Fact]
    public void EveryEditorMessageGoesThroughTheWordingFilter()
    {
        // No direct Editor.WriteMessage left outside the TRACE-only files and the filter itself:
        // a direct call would print internal names in the commercial edition.
        var offenders = Directory.GetFiles(Path.Combine(Repo(), "src", "FieldCodes.Cad"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                     && !f.Contains(Path.DirectorySeparatorChar + "Trace" + Path.DirectorySeparatorChar)
                     && Path.GetFileName(f) != "FtfText.cs")
            .Where(f => File.ReadAllText(f).Contains(".WriteMessage("))
            .Select(Path.GetFileName).ToList();
        Assert.Empty(offenders);
    }
}
