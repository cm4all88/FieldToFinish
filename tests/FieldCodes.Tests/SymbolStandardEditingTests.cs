using FieldCodes;
using FieldCodes.Editing;
using FieldCodes.Orient;

namespace FieldCodes.Tests;

/// <summary>
/// Settings > Symbols and Rotations edits the orientation standard's symbol rules on the rules
/// file's JSON DOM: the built-in PMX list is copied in before the first edit, comments and unknown
/// properties survive, invalid rows are refused by number, first-match-wins is respected (a code's
/// own rule goes above its family's), and a save merges onto the file as it is on disk.
/// </summary>
public sealed class SymbolStandardEditingTests
{
    private static readonly string RulesPath = Path.Combine(AppContext.BaseDirectory, "rules.json");

    private static RuleDocument Shipped() => RuleDocument.Load(RulesPath);

    /// <summary>A minimal rules file with an orientation section of the caller's.</summary>
    private static RuleDocument With(string orientation) => RuleDocument.FromText(
        "{ \"//top\": \"kept\", \"codes\": [ { \"id\": \"x\", \"match\": \"^(?<code>X)\\\\s+(?<number>[0-9]+)$\" } ]" +
        (orientation.Length > 0 ? ", \"orientation\": " + orientation : string.Empty) + " }");

    private static string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "ftf-symbols-" + Path.GetRandomFileName());
        Directory.CreateDirectory(d);
        return d;
    }

    // ------------------------------------------------------------------ the built-in list

    [Fact]
    public void TheShippedFileUsesTheBuiltInPmxList()
    {
        var doc = Shipped();
        Assert.True(doc.SymbolsAreBuiltIn());
        var symbols = doc.GetSymbols();
        Assert.Equal(OrientationStandard.Pmx().Symbols.Count, symbols.Count);
        Assert.All(symbols, s => Assert.Equal(-1, s.SourceIndex));
        Assert.Equal("SNST", symbols[1].CodesText);
        Assert.Equal(90, symbols[1].ReferenceCadDegrees);
        Assert.True(symbols[1].TwoSided);
    }

    [Fact]
    public void MaterializingCopiesThePmxListIntoTheFileAndChangesNothingElse()
    {
        var doc = Shipped();
        Assert.True(doc.MaterializeSymbols());
        Assert.False(doc.SymbolsAreBuiltIn());
        Assert.False(doc.MaterializeSymbols());          // already the file's own

        var rendered = doc.Render();
        Assert.Contains("\"//symbols\"", rendered);
        Assert.Contains("//orientation", rendered);       // the file's own comments stay
        Assert.Contains("//point-features", rendered);
        Assert.Contains("\"qcArrowPlotted\"", rendered);
        Assert.Empty(doc.Validate());

        // The same rules as the built-in list, in the same order.
        var pmx = OrientationStandard.Pmx().Symbols;
        var compiled = doc.CompileCandidate().Orientation.Symbols;
        Assert.Equal(pmx.Count, compiled.Count);
        for (var i = 0; i < pmx.Count; i++)
        {
            Assert.Equal(pmx[i].Codes, compiled[i].Codes);
            Assert.Equal(pmx[i].Kind, compiled[i].Kind);
            Assert.Equal(pmx[i].Block, compiled[i].Block);
            Assert.Equal(pmx[i].ReferenceCadDegrees, compiled[i].ReferenceCadDegrees);
            Assert.Equal(pmx[i].Period, compiled[i].Period);
            Assert.Equal(pmx[i].TwoSided, compiled[i].TwoSided);
            Assert.Equal(pmx[i].RotateLabel, compiled[i].RotateLabel);
        }
        Assert.All(doc.GetSymbols(), s => Assert.True(s.SourceIndex >= 0));
    }

    [Fact]
    public void AFileWithNoOrientationSectionGetsOneOnMaterializing()
    {
        var doc = With(string.Empty);
        Assert.True(doc.SymbolsAreBuiltIn());
        doc.MaterializeSymbols();
        Assert.Contains("\"orientation\"", doc.Render());
        Assert.Equal(OrientationStandard.Pmx().Symbols.Count, doc.CompileCandidate().Orientation.Symbols.Count);
        Assert.Contains("\"//top\": \"kept\"", doc.Render());
    }

    [Fact]
    public void UsingTheBuiltInListAgainRemovesTheFilesListAndItsNote()
    {
        var doc = Shipped();
        doc.MaterializeSymbols();
        doc.UseBuiltInSymbols();
        Assert.True(doc.SymbolsAreBuiltIn());
        Assert.DoesNotContain("\"//symbols\"", doc.Render());
        Assert.Contains("\"applyMinimumConfidence\"", doc.Render());   // the rest of the section stays
    }

    // ------------------------------------------------------------------ round trips

    private const string OwnList =
        "{ \"applyMinimumConfidence\": 0.6, \"//mine\": \"office note\", \"symbols\": [" +
        "  { \"//why\": \"our sign block\", \"codes\": [ \"SN*\" ], \"kind\": \"Sign\", \"block\": \"OFC-SIGN\", \"referenceCadDegrees\": 90, \"period\": 360, \"futureKey\": 7 }," +
        "  { \"codes\": [ \"WV\" ], \"kind\": \"WaterValve\", \"referenceCadDegrees\": 0, \"period\": 180 }" +
        "] }";

    [Fact]
    public void EditingARowKeepsItsCommentsAndPropertiesThisBuildDoesNotKnow()
    {
        var doc = With(OwnList);
        var symbols = doc.GetSymbols();
        Assert.Equal(2, symbols.Count);
        Assert.Equal(0, symbols[0].SourceIndex);

        symbols[0].ReferenceCadDegrees = 45;
        symbols[0].Meaning = "face";
        doc.SetSymbols(symbols);

        var rendered = doc.Render();
        Assert.Contains("\"//why\": \"our sign block\"", rendered);
        Assert.Contains("\"futureKey\": 7", rendered);
        Assert.Contains("\"//mine\": \"office note\"", rendered);
        Assert.Contains("\"//top\": \"kept\"", rendered);
        Assert.Empty(doc.Validate());

        var again = RuleDocument.FromText(rendered).GetSymbols();
        Assert.Equal(45, again[0].ReferenceCadDegrees);
        Assert.Equal("face", again[0].Meaning);
        Assert.Equal(180, again[1].Period);
    }

    [Fact]
    public void ReorderingCarriesEachRowsOwnUnknownPropertiesWithIt()
    {
        var doc = With(OwnList);
        var symbols = doc.GetSymbols().ToList();
        symbols.Reverse();
        doc.SetSymbols(symbols);
        Assert.Equal(new[] { 0, 1 }, symbols.Select(s => s.SourceIndex));   // updated to their new places

        var compiled = doc.CompileCandidate().Orientation.Symbols;
        Assert.Equal("WV", compiled[0].Codes.Single());
        var rendered = doc.Render();
        Assert.True(rendered.IndexOf("\"WV\"", StringComparison.Ordinal) < rendered.IndexOf("futureKey", StringComparison.Ordinal));
    }

    [Fact]
    public void FalseFlagsAndBlankTextAreLeftOutAsTheShippedFileDoes()
    {
        var doc = With(OwnList);
        var symbols = doc.GetSymbols();
        symbols[1].TwoSided = false;
        symbols[1].RotateLabel = false;
        symbols[1].Meaning = "  ";
        doc.SetSymbols(symbols);
        var rendered = doc.Render();
        Assert.DoesNotContain("twoSided", rendered);
        Assert.DoesNotContain("rotateLabel", rendered);
        Assert.DoesNotContain("\"meaning\"", rendered);
    }

    [Fact]
    public void SavedAndReloadedTheEditedStandardIsWhatFtforientUses()
    {
        var dir = TempDir();
        try
        {
            var path = Path.Combine(dir, "rules.json");
            File.Copy(RulesPath, path);
            var doc = RuleDocument.Load(path);
            doc.MaterializeSymbols();
            var symbols = doc.GetSymbols();
            var hydrant = symbols.Single(s => s.Codes.Contains("WFH"));
            hydrant.ReferenceCadDegrees = 270;
            doc.SetSymbols(symbols);
            doc.Save(path);

            var cfg = RulesConfig.Load(path);
            Assert.Equal(270, cfg.Orientation.SymbolFor("WFH")!.ReferenceCadDegrees);
            Assert.Equal(90, cfg.Orientation.SymbolFor("SN")!.ReferenceCadDegrees);
            var text = File.ReadAllText(path);
            Assert.Contains("//orientation", text);
            Assert.Contains("//sn-sign", text);
            Assert.True(File.Exists(path + RuleDocument.OriginalBackupSuffix));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void AnEmptyListIsRefusedTheBuiltInOneIsChosenOnPurpose()
    {
        var doc = With(OwnList);
        Assert.Throws<ConfigException>(() => doc.SetSymbols(new List<EditableSymbol>()));
    }

    // ------------------------------------------------------------------ validation

    private static IList<string> ProblemsWith(string entry) =>
        With("{ \"symbols\": [ " + entry + " ] }").Validate();

    [Theory]
    [InlineData("{ \"codes\": [ \"SN\" ], \"kind\": \"Sign\", \"referenceCadDegrees\": 90, \"period\": 90 }", "repeats every")]
    [InlineData("{ \"codes\": [ \"SN\" ], \"kind\": \"Sign\", \"referenceCadDegrees\": 400, \"period\": 360 }", "native zero")]
    [InlineData("{ \"codes\": [ \"SN\" ], \"kind\": \"Sign\", \"referenceCadDegrees\": -5, \"period\": 360 }", "native zero")]
    [InlineData("{ \"codes\": [ ], \"kind\": \"Sign\" }", "at least one code")]
    [InlineData("{ \"codes\": [ \"  \" ], \"kind\": \"Sign\" }", "at least one code")]
    [InlineData("{ \"codes\": [ \"SN ST\" ], \"kind\": \"Sign\" }", "has a space")]
    [InlineData("{ \"codes\": [ \"SN\" ], \"kind\": \"Lamp\" }", "kind 'Lamp'")]
    public void AnInvalidSymbolRowIsNamedByItsNumber(string entry, string expected)
    {
        var problems = ProblemsWith(entry);
        var problem = Assert.Single(problems);
        Assert.Contains("symbol rule 1", problem);
        Assert.Contains(expected, problem);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(180)]
    [InlineData(360)]
    public void ThePeriodsAllowedAre0_180And360(double period)
        => Assert.Empty(ProblemsWith("{ \"codes\": [ \"SN\" ], \"kind\": \"sign\", \"referenceCadDegrees\": 360, \"period\": " + period + " }"));

    [Fact]
    public void ThresholdsOutOfRangeAreRefused()
    {
        Assert.Contains(With("{ \"applyMinimumConfidence\": 1.5 }").Validate(), p => p.Contains("between 0 and 1"));
        Assert.Contains(With("{ \"applyWeakestTier\": \"Review\" }").Validate(), p => p.Contains("Measured, Relationship, Alignment or Inferred"));
        Assert.Contains(With("{ \"applyWeakestTier\": \"Guess\" }").Validate(), p => p.Contains("weakest evidence"));
        Assert.Empty(With("{ \"applyMinimumConfidence\": 0.75, \"applyWeakestTier\": \"alignment\" }").Validate());
    }

    [Fact]
    public void ACodeBelowAFamilyThatAlreadyMatchesItIsNeverUsed()
    {
        var shadowed = ProblemsWith(
            "{ \"codes\": [ \"SN*\" ], \"kind\": \"Sign\", \"referenceCadDegrees\": 90 }," +
            "{ \"codes\": [ \"SNST\" ], \"kind\": \"Sign\", \"referenceCadDegrees\": 0 }");
        var problem = Assert.Single(shadowed);
        Assert.Contains("symbol rule 2 lists SNST", problem);
        Assert.Contains("never used", problem);

        // The right way round: the code's own rule above its family's.
        Assert.Empty(ProblemsWith(
            "{ \"codes\": [ \"SNST\" ], \"kind\": \"Sign\", \"referenceCadDegrees\": 0 }," +
            "{ \"codes\": [ \"SN*\" ], \"kind\": \"Sign\", \"referenceCadDegrees\": 90 }"));
    }

    [Fact]
    public void TheMaterializedPmxListHasNoShadowedCodes()
    {
        var doc = Shipped();
        doc.MaterializeSymbols();
        Assert.Empty(doc.Validate());
    }

    // ------------------------------------------------------------------ feature-specific overrides

    [Fact]
    public void AnOverrideGoesAboveTheFamilyAndTakesTheCodeOutOfIt()
    {
        var symbols = OrientationStandard.Pmx().Symbols.Select(r => EditableSymbol.From(r, -1)).ToList();
        var family = symbols.FindIndex(s => s.Codes.Contains("FH"));

        var index = RuleDocument.OverrideSymbolFor(symbols, "fh2");     // figure digits ignored, any case
        Assert.Equal(family, index);
        Assert.Equal(new[] { "FH" }, symbols[index].Codes);
        Assert.Equal(OrientKind.Hydrant.ToString(), symbols[index].Kind);
        Assert.Equal(new[] { "WFH", "FHY" }, symbols[index + 1].Codes);

        symbols[index].ReferenceCadDegrees = 180;
        var std = new OrientationStandard { Symbols = symbols.Select(s => s.ToRule()).ToList() };
        Assert.Equal(180, std.SymbolFor("FH")!.ReferenceCadDegrees);
        Assert.Equal(0, std.SymbolFor("WFH")!.ReferenceCadDegrees);

        var doc = Shipped();
        doc.MaterializeSymbols();
        doc.SetSymbols(symbols);
        Assert.Empty(doc.Validate());
    }

    [Fact]
    public void AnOverrideForACodeAWildcardCoversLeavesTheWildcardAlone()
    {
        var symbols = OrientationStandard.Pmx().Symbols.Select(r => EditableSymbol.From(r, -1)).ToList();
        var family = symbols.FindIndex(s => s.Codes.Contains("SN*"));
        var index = RuleDocument.OverrideSymbolFor(symbols, "SNS");
        Assert.Equal(family, index);
        Assert.Equal(new[] { "SN*" }, symbols[index + 1].Codes);
        var std = new OrientationStandard { Symbols = symbols.Select(s => s.ToRule()).ToList() };
        Assert.Equal(index, std.SymbolIndexFor("SNS"));
        Assert.Equal(index + 1, std.SymbolIndexFor("SNX"));
    }

    [Fact]
    public void AnOverrideIsRefusedWhereItMeansNothing()
    {
        var symbols = OrientationStandard.Pmx().Symbols.Select(r => EditableSymbol.From(r, -1)).ToList();
        Assert.Throws<ConfigException>(() => RuleDocument.OverrideSymbolFor(symbols, "ZZZ"));    // nothing covers it
        Assert.Throws<ConfigException>(() => RuleDocument.OverrideSymbolFor(symbols, "SNST"));   // already its own
        Assert.Throws<ConfigException>(() => RuleDocument.OverrideSymbolFor(symbols, " "));
        Assert.Equal(OrientationStandard.Pmx().Symbols.Count, symbols.Count);
    }

    [Fact]
    public void CodesTypedInOneBoxAreSplitAndUppercased()
        => Assert.Equal(new[] { "SN*", "SNST", "WV" }, EditableSymbol.ParseCodes(" SN*, snst  wv;wv "));

    // ------------------------------------------------------------------ thresholds and library

    [Fact]
    public void TheThresholdsAndLibraryReadAndWrite()
    {
        var doc = Shipped();
        var apply = doc.GetOrientationApply();
        Assert.Equal(0.6, apply.MinimumConfidence, 6);
        Assert.Equal("Inferred", apply.WeakestTier);
        Assert.Equal(string.Empty, apply.SymbolLibrary);

        apply.MinimumConfidence = 0.8;
        apply.WeakestTier = "Alignment";
        apply.SymbolLibrary = @"S:\CAD\Symbols.dwg";
        doc.ApplyOrientationApply(apply);
        Assert.Empty(doc.Validate());
        var cfg = doc.CompileCandidate();
        Assert.Equal(0.8, cfg.Orientation.ApplyMinimumConfidence, 6);
        Assert.Equal(OrientationTier.Alignment, cfg.Orientation.ApplyWeakestTier);
        Assert.Equal(@"S:\CAD\Symbols.dwg", cfg.Orientation.SymbolLibrary);

        apply.SymbolLibrary = string.Empty;
        doc.ApplyOrientationApply(apply);
        Assert.DoesNotContain("symbolLibrary", doc.Render());
    }

    [Fact]
    public void UnchangedThresholdsAreNotWrittenIntoAFileThatLeftThemOut()
    {
        var doc = With(string.Empty);
        doc.ApplyOrientationApply(doc.GetOrientationApply());
        Assert.DoesNotContain("orientation", doc.Render());
    }

    // ------------------------------------------------------------------ merging on save

    [Fact]
    public void SavingMergesOntoTheFileAsItIsNowSoAnotherPagesSaveIsKept()
    {
        var staged = Shipped();
        staged.MaterializeSymbols();
        var symbols = staged.GetSymbols();
        symbols[0].Meaning = "edited on the symbols page";
        staged.SetSymbols(symbols);
        var apply = staged.GetOrientationApply();
        apply.MinimumConfidence = 0.9;
        staged.ApplyOrientationApply(apply);

        // Meanwhile Point Features saved a label change.
        var onDisk = Shipped();
        var pole = onDisk.GetEditable("pole");
        pole.LabelFormat = "UTILITY POLE {number}";
        onDisk.ApplyEdit(pole);

        staged.CopySymbolsTo(onDisk);
        staged.CopyOrientationApplyTo(onDisk);
        Assert.Empty(onDisk.Validate());
        var cfg = onDisk.CompileCandidate();
        Assert.Equal("edited on the symbols page", cfg.Orientation.Symbols[0].Meaning);
        Assert.Equal(0.9, cfg.Orientation.ApplyMinimumConfidence, 6);
        Assert.Equal("UTILITY POLE {number}", onDisk.GetEditable("pole").LabelFormat);
    }

    [Fact]
    public void MergingTheBuiltInListTakesTheFilesListAway()
    {
        var onDisk = Shipped();
        onDisk.MaterializeSymbols();
        Shipped().CopySymbolsTo(onDisk);
        Assert.True(onDisk.SymbolsAreBuiltIn());
    }
}
