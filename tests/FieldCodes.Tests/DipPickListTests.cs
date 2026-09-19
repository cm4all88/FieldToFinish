using FieldCodes.Settings;
using FieldCodes.Utilities;
using Newtonsoft.Json;

namespace FieldCodes.Tests;

/// <summary>The Add pipe panel's choices and what it records: speed tools that never change what was observed.</summary>
public sealed class DipPickListTests
{
    // ------------------------------------------------------------ structure lists

    [Fact]
    public void StructureTypesKeepOfficeCodesCommonFirst()
    {
        new UtilitySettings().StructureCodeChoices(out var common, out var more);
        Assert.Equal(new[] { "CB", "CBR", "SDMH", "SSMH", "MH" }, common);
        Assert.Contains("SDCO", more);
        Assert.DoesNotContain("SDMH", more);
        Assert.DoesNotContain("CB1", more);
        Assert.Equal(new UtilitySettings().StructureCodes.Count, common.Count + more.Count);
    }

    [Fact]
    public void CommonStructureCodesNotDefinedAreDropped()
    {
        var d = new UtilitySettings { CommonStructureCodes = new List<string> { "sdmh", "CSMH" } };
        d.StructureCodeChoices(out var codes, out _);
        Assert.Equal(new[] { "SDMH" }, codes);
    }

    [Fact]
    public void StructureDiametersStaySeparateFromPipeSizes()
    {
        var d = new UtilitySettings();
        Assert.Equal(new double[] { 48, 54, 60, 72, 96 }, d.StructureDiametersCommon);
        Assert.DoesNotContain(48.0, d.PipeChoicesFor("CB", UtilitySystem.Storm).CommonSizes);
    }

    // ------------------------------------------------------- choices by structure

    [Fact]
    public void SizeAndMaterialButtonsFollowTheStructure()
    {
        var d = new UtilitySettings();
        var cb = d.PipeChoicesFor("CB", UtilitySystem.Storm);
        var sdmh = d.PipeChoicesFor("SDMH", UtilitySystem.Storm);
        var ssmh = d.PipeChoicesFor("SSMH", UtilitySystem.Sanitary);
        var culvert = d.PipeChoicesFor("CULV", UtilitySystem.Culvert);
        var other = d.PipeChoicesFor("MH", UtilitySystem.Other);

        Assert.Equal("Catch basins and inlets", cb.RuleName);
        Assert.Equal("Storm", sdmh.RuleName);
        Assert.Equal("Sanitary", ssmh.RuleName);
        Assert.Equal("Culverts", culvert.RuleName);
        Assert.Equal("Any structure", other.RuleName);

        Assert.Equal(new double[] { 6, 8, 10, 12, 15, 18, 24 }, cb.CommonSizes);
        Assert.NotEqual(cb.CommonSizes, sdmh.CommonSizes);
        Assert.NotEqual(sdmh.CommonSizes, ssmh.CommonSizes);
        Assert.Equal("VCP", ssmh.CommonMaterials[0]);
        Assert.Equal("RCP", sdmh.CommonMaterials[0]);
        Assert.Equal("CMP", culvert.CommonMaterials[0]);
        Assert.Contains("UNK", cb.CommonMaterials);
    }

    [Fact]
    public void StructureTypeWinsOverSystem()
    {
        // A CB is a catch basin whatever system it was put in.
        Assert.Equal("Catch basins and inlets", new UtilitySettings().PipeChoicesFor("cb", UtilitySystem.Sanitary).RuleName);
        // An unlisted type falls back to its system, then to any structure.
        Assert.Equal("Sanitary", new UtilitySettings().PipeChoicesFor("SSWET", UtilitySystem.Sanitary).RuleName);
        Assert.Equal("Any structure", new UtilitySettings().PipeChoicesFor(null, UtilitySystem.Water).RuleName);
    }

    [Fact]
    public void LargerHoldsTheRestNeverTheUsualOnes()
    {
        var cb = new UtilitySettings().PipeChoicesFor("CB", UtilitySystem.Storm);
        // Usual sizes are shortcuts, not a range: a CB still reaches 30" to 60" through Larger.
        Assert.Equal(new double[] { 4, 21, 27, 30, 36, 42, 48, 54, 60 }, cb.LargerSizes);
        Assert.Empty(cb.LargerSizes.Intersect(cb.CommonSizes));

        // A rule with no larger list borrows the any-structure sizes, minus its own.
        var d = new UtilitySettings();
        d.PipeChoices[0].LargerSizes.Clear();
        var borrowed = d.PipeChoicesFor("CB", UtilitySystem.Storm);
        Assert.Contains(144.0, borrowed.LargerSizes);
        Assert.DoesNotContain(12.0, borrowed.LargerSizes);
    }

    [Fact]
    public void MoreMaterialsAreEveryOtherOfficeMaterialUnlessListed()
    {
        var d = new UtilitySettings();
        var cb = d.PipeChoicesFor("CB", UtilitySystem.Storm);
        Assert.Contains("VCP", cb.MoreMaterials);
        Assert.Contains("DIP", cb.MoreMaterials);
        Assert.Empty(cb.MoreMaterials.Intersect(cb.CommonMaterials));

        d.PipeChoices[0].MoreMaterials = new List<string> { "abs", "RCP" };
        var listed = d.PipeChoicesFor("CB", UtilitySystem.Storm);
        Assert.Equal(new[] { "ABS" }, listed.MoreMaterials);   // RCP is already a usual one
    }

    [Fact]
    public void OlderSettingsGetTheDefaultChoices()
    {
        // Saved before pipe choices existed: the defaults, and nothing else changes.
        var old = JsonConvert.DeserializeObject<UtilitySettings>("{\"materials\":[\"RCP\",\"PVC\"],\"textStyle\":\"Standard\"}")!;
        old.Validate(new List<string>());
        Assert.Equal("Standard", old.TextStyle);
        Assert.Equal(new[] { "RCP", "PVC" }, old.Materials);
        Assert.Equal(UtilitySettings.DefaultPipeChoices().Count, old.PipeChoices.Count);
        Assert.Equal("Catch basins and inlets", old.PipeChoicesFor("CB", UtilitySystem.Storm).RuleName);

        // A null list is repaired; a deliberately emptied one is respected and still gives a usable (empty) set.
        var nulled = JsonConvert.DeserializeObject<UtilitySettings>("{\"pipeChoices\":null}")!;
        nulled.Validate(new List<string>());
        Assert.NotEmpty(nulled.PipeChoices);
        var emptied = JsonConvert.DeserializeObject<UtilitySettings>("{\"pipeChoices\":[]}")!;
        Assert.Empty(emptied.PipeChoices);
        Assert.Empty(emptied.PipeChoicesFor("CB", UtilitySystem.Storm).CommonSizes);
    }

    [Fact]
    public void SavedRulesRoundTripAndBadSizesAreReported()
    {
        var d = new UtilitySettings();
        d.PipeChoices[1].CommonSizes = new List<double> { 8, 17.5 };
        var back = JsonConvert.DeserializeObject<UtilitySettings>(JsonConvert.SerializeObject(d))!;
        Assert.Equal(new double[] { 8, 17.5 }, back.PipeChoices[1].CommonSizes);
        Assert.Equal(UtilitySystem.Storm, back.PipeChoices[1].System);
        Assert.Null(back.PipeChoices[0].System);

        back.PipeChoices[1].LargerSizes = new List<double> { -1 };
        var problems = new List<string>();
        back.Validate(problems);
        Assert.Contains(problems, p => p.Contains("Storm") && p.Contains("greater than zero"));
    }

    // ------------------------------------------------------------------ directions

    [Fact]
    public void SixteenDirectionShortcuts()
    {
        var names = DirectionShortcuts.Names.ToList();
        Assert.Equal(new[] { "N", "N/NE", "NE", "E/NE", "E", "E/SE", "SE", "S/SE", "S", "S/SW", "SW", "W/SW", "W", "W/NW", "NW", "N/NW" }, names);
        for (var i = 0; i < 16; i++)
        {
            var d = DirectionShortcuts.For(names[i])!;
            Assert.Equal(names[i], d.Text);
            Assert.Equal(i * 22.5, d.AzimuthDegrees);
            Assert.Equal(names[i], DirectionShortcuts.ButtonFor(d));
        }
        Assert.Equal(337.5, DirectionShortcuts.For("n/nw")!.AzimuthDegrees);
    }

    [Theory]
    [InlineData(0, "N")]
    [InlineData(11.2, "N")]
    [InlineData(11.3, "N/NE")]
    [InlineData(348.8, "N")]
    [InlineData(348.7, "N/NW")]
    [InlineData(-10, "N")]
    [InlineData(370, "N")]
    [InlineData(95, "E")]
    [InlineData(200, "S/SW")]
    [InlineData(292.5, "W/NW")]
    public void ACompassClickSnapsToTheNearestOfTheSixteen(double azimuth, string expected)
    {
        Assert.Equal(expected, DirectionShortcuts.Nearest(azimuth));
    }

    [Fact]
    public void EachShortcutIsItsOwnNearest()
    {
        foreach (var p in DirectionShortcuts.All) Assert.Equal(p.Key, DirectionShortcuts.Nearest(p.Value));
    }

    [Fact]
    public void ParserDirectionsAreUnchanged()
    {
        // The field-note parser still reads exactly what it read before; the new names are window shortcuts only.
        Assert.Null(DipNoteParser.ParseDirection("N/NE"));
        Assert.Equal(45.0, DipNoteParser.ParseDirection("NE")!.AzimuthDegrees);

        // In the window a bearing or azimuth is kept exactly as observed, with no button for it.
        var bearing = DirectionShortcuts.Parse("N45E")!;
        Assert.Equal("N45E", bearing.Text);
        Assert.Null(DirectionShortcuts.ButtonFor(bearing));
        Assert.Equal(215.0, DirectionShortcuts.Parse("AZ215")!.AzimuthDegrees);
        Assert.False(DirectionShortcuts.Parse("?")!.IsKnown);
        Assert.Null(DirectionShortcuts.Parse("NORTHISH"));
    }

    [Fact]
    public void ANoteWithAShortcutNameIsNotReadAsADirection()
    {
        var parsed = new DipNoteParser(new UtilitySettings()).Parse("PT 1 SDMH\n12 RCP N/NE 6.41");
        var pipe = parsed.Structures[0].Pipes[0];
        Assert.False(pipe.Direction.IsKnown);   // unchanged parser behaviour
    }

    // ------------------------------------------------------------- what is recorded

    [Fact]
    public void QuickEntryKeepsValuesExactly()
    {
        var pipe = new QuickPipeEntry
        {
            Direction = DirectionShortcuts.For("N/NW"), SizeIn = 17.5, Material = "ribbed pvc", MeasuredDip = 6.415,
            Reference = MeasurementReference.Invert
        }.Create();
        Assert.Equal(17.5, pipe.WidthIn);
        Assert.Equal(17.5, pipe.HeightIn);
        Assert.Equal("RIBBED PVC", pipe.Material);   // any material, kept (in capitals, as the table does)
        Assert.Equal(6.415, pipe.MeasuredDip);
        Assert.Equal("N/NW", pipe.Direction.Text);
        Assert.Equal(337.5, pipe.Direction.AzimuthDegrees);
        Assert.Equal(ObservationSource.UserEntry, pipe.Source);
        Assert.Equal(ReferenceBasis.EnteredByDrafter, pipe.ReferenceBasis);
        Assert.Equal("N/NW 17.5\" RIBBED PVC IE 6.415", QuickPipeEntry.Summary(pipe, new UtilitySettings()));
    }

    [Theory]
    [InlineData(17.5)]
    [InlineData(6.41)]
    [InlineData(6.415)]
    [InlineData(1.125)]
    [InlineData(8)]
    [InlineData(0.1 + 0.2)]
    [InlineData(123.456789012)]
    public void ShownValuesReadBackExactly(double value)
    {
        var text = QuickPipeEntry.Exact(value);
        Assert.DoesNotContain("E", text);
        Assert.Equal(value, double.Parse(text, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void AnUnmarkedDipStaysUnspecifiedEvenWithTheOfficeConvention()
    {
        var settings = new UtilitySettings();
        Assert.True(settings.UnmarkedDipsAreInvertsByConvention);   // the office default is on...
        var pipe = new QuickPipeEntry
        {
            Direction = DirectionShortcuts.For("N/NW"), SizeIn = 12, Material = "RCP", MeasuredDip = 6.41
        }.Create();
        // ...and still the panel never turns an unmarked MD into an invert.
        Assert.Equal(MeasurementReference.Unspecified, pipe.Reference);
        Assert.Equal(ReferenceBasis.NotStated, pipe.ReferenceBasis);
        Assert.True(pipe.ReferenceUnconfirmed);
        Assert.Equal("N/NW 12\" RCP 6.41 Unspecified", QuickPipeEntry.Summary(pipe, settings));
    }

    [Fact]
    public void TheNoteConventionItselfIsUnchanged()
    {
        var pipe = new DipNoteParser(new UtilitySettings()).Parse("PT 1 SDMH\n12 RCP N 6.41").Structures[0].Pipes[0];
        Assert.Equal(MeasurementReference.Invert, pipe.Reference);
        Assert.Equal(ReferenceBasis.FieldNoteConvention, pipe.ReferenceBasis);
    }

    [Fact]
    public void CalculationsUseQuickEntriesLikeAnyOtherObservation()
    {
        var a = new StructureRecord { Cad = new CadStructureSnapshot { PointNumber = "1", Rim = 100, Northing = 0, Easting = 0 } };
        var b = new StructureRecord { Cad = new CadStructureSnapshot { PointNumber = "2", Rim = 100, Northing = 100, Easting = 0 } };
        var down = new QuickPipeEntry { Direction = DirectionShortcuts.For("N"), SizeIn = 12, MeasuredDip = 5, Reference = MeasurementReference.Invert }.Create();
        var up = new QuickPipeEntry { Direction = DirectionShortcuts.For("S"), SizeIn = 12, MeasuredDip = 6, Reference = MeasurementReference.Invert }.Create();
        a.Field.Pipes.Add(down);
        b.Field.Pipes.Add(up);
        var project = new UtilityProject();
        project.Structures.Add(a);
        project.Structures.Add(b);
        var c = new PipeConnection { FromStructureId = a.Id, FromPipeId = down.Id, ToStructureId = b.Id, ToPipeId = up.Id, Status = ConnectionStatus.ManualOverride };
        project.Connections.Add(c);

        Assert.Equal(95.0, DipElevations.Pipe(a, down)!.Value, 6);
        Assert.Equal(1.0, SlopeCalculator.Compute(project, c).SlopePercent!.Value, 6);

        // An unspecified end gives no slope -- exactly as for a field-note dip.
        up.Reference = MeasurementReference.Unspecified;
        up.ReferenceBasis = ReferenceBasis.NotStated;
        var none = SlopeCalculator.Compute(project, c);
        Assert.Null(none.SlopePercent);
        Assert.Equal(SlopeBasis.IncomparableReferences, none.Basis);

        // The search runs along a 16-point direction's azimuth just as along N.
        var ne = new StructureRecord { Cad = new CadStructureSnapshot { PointNumber = "3", Rim = 100, Northing = 92.388, Easting = 38.268 } };
        project.Structures.Add(ne);
        var tilted = new QuickPipeEntry { Direction = DirectionShortcuts.For("N/NE"), SizeIn = 12 }.Create();
        a.Field.Pipes.Add(tilted);
        var found = ConnectionFinder.Find(project, a, tilted, new UtilitySettings());
        Assert.Equal(ne.Id, found[0].Structure.Id);
        Assert.True(found[0].DeviationDegrees < 0.01);
    }
}
