using FieldCodes.Settings;
using FieldCodes.Utilities;

namespace FieldCodes.Tests;

public sealed class OutsideLimitsTests
{
    [Fact]
    public void APipeRunningOutsideTheSurveyLimitsIsSettled_NotAWarningOrARevisitItem()
    {
        var field = new DipNoteParser(new UtilitySettings()).Parse("PT 1 SDMH\n24 RCP E 6.00").Structures[0];
        var s = new StructureRecord { Field = field, StructureType = "SDMH", EnteredInsideWidthIn = 48,
            Cad = new CadStructureSnapshot { PointNumber = "1", Rim = 100, Northing = 0, Easting = 0 } };
        var project = new UtilityProject();
        project.Structures.Add(s);

        ConnectionFinder.MarkOutsideLimits(project, s, s.Field.Pipes[0]);

        var findings = UtilityQc.Evaluate(project, new UtilitySettings());
        Assert.DoesNotContain(findings, f => f.Code == QcCode.UnresolvedConnection);
        Assert.Equal("", UtilityQc.FieldRevisitText(project, findings));
        Assert.Equal(0, UtilityQc.Summarize(project, findings, 0).UnresolvedConnections);
        Assert.Equal(ConnectionStatus.OutsideSurveyLimits, project.ConnectionFor(s.Id, s.Field.Pipes[0].Id)!.Status);
    }
}
