using FieldCodes.Settings;
using FieldCodes.Utilities;

namespace FieldCodes.Tests;

public sealed class StructureSizeTests
{
    private static StructureRecord Structure(string notes)
    {
        var field = new DipNoteParser(new UtilitySettings()).Parse(notes).Structures[0];
        return new StructureRecord
        {
            Field = field, StructureType = field.FieldCode,
            Cad = new CadStructureSnapshot { PointNumber = field.PointNumber, Rim = 100, Northing = 0, Easting = 0 }
        };
    }

    [Fact]
    public void ManholesAreRoundCatchBasinsRectangularCleanoutsUnsized()
    {
        var settings = new UtilitySettings();
        Assert.Equal(StructureShape.Round, settings.FindCode("SDMH")!.Shape);
        Assert.Equal(StructureShape.Rectangular, settings.FindCode("CB")!.Shape);
        Assert.Equal(StructureShape.NoSize, settings.FindCode("CO")!.Shape);
    }

    [Fact]
    public void TheDiameterShowsOnTheLabelHeader()
    {
        var settings = new UtilitySettings();
        var s = Structure("PT 1045 SDMH\n12 RCP N 6.41");
        var project = new UtilityProject();
        project.Structures.Add(s);
        Assert.Equal("SDMH 1045", UtilityLabelFormatter.StructureLabel(project, s, settings)[0]);   // no size, nothing added

        s.EnteredInsideWidthIn = 48;
        Assert.Equal("SDMH 1045 48\"", UtilityLabelFormatter.StructureLabel(project, s, settings)[0]);
    }

    [Fact]
    public void ARectangularStructureShowsWidthByLength()
    {
        var s = Structure("PT 7 CB\n12 RCP N 4.00");
        s.EnteredInsideWidthIn = 24;
        s.EnteredInsideLengthIn = 36;
        Assert.Equal("24\"x36\"", StructureDimensions.SizeText(s, new UtilitySettings()));
    }

    [Fact]
    public void AMissingDiameterIsAGentleNoteOnlyForRoundStructures()
    {
        var settings = new UtilitySettings();
        var project = new UtilityProject();
        project.Structures.Add(Structure("PT 1 SDMH\n12 RCP N 4.00"));
        project.Structures.Add(Structure("PT 2 CO\n6 PVC N 2.00"));
        var findings = UtilityQc.Evaluate(project, settings).Where(f => f.Code == QcCode.StructureSizeMissing).ToList();
        var only = Assert.Single(findings);
        Assert.Equal(Severity.Info, only.Severity);
        Assert.Contains("SDMH 1", only.Message);
    }

    [Fact]
    public void MdWrittenInTheNotesIsUnderstood()
    {
        var s = Structure("PT 1 SDMH\nBOT MD 7.82\n12 RCP N MD 6.41");
        Assert.Equal(7.82, s.Field.BottomDip);
        Assert.Equal(6.41, s.Field.Pipes[0].MeasuredDip);
        Assert.Null(s.Field.Pipes[0].Notes);
    }
}
