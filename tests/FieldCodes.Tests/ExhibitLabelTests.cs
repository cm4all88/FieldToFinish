using FieldCodes.Drafting;
using FieldCodes.Easements;
using FieldCodes.Settings;

namespace FieldCodes.Tests;

/// <summary>
/// Exhibit labels and area wording, checked against the office's Exhibit B for
/// 28052700104300: its line table holds L1 13.19' N87°55'53"W (the commencement tie),
/// L2 13.62' N44°26'09"E and L3 65.10' N87°55'53"W (the terminus tie); the long courses
/// are labelled on the line.
/// </summary>
public sealed class ExhibitLabelTests
{
    private static readonly EasementSettings Settings = new();

    private static P2 Along(P2 from, double azimuthDegrees, double distance)
    {
        var a = azimuthDegrees * Math.PI / 180;
        return new P2(from.X + Math.Sin(a) * distance, from.Y + Math.Cos(a) * distance);
    }

    private static double Dms(int d, int m, int s) => d + m / 60.0 + s / 3600.0;

    [Fact]
    public void ExhibitBearingsHaveNoSpaces()
    {
        Assert.Equal("N01°22'31\"E", SurveyDirection.FormatBearing(Dms(1, 22, 31), 0, "°", false));
        Assert.Equal("N 01°22'31\" E", SurveyDirection.FormatBearing(Dms(1, 22, 31), 0, "°", true));
        Assert.Equal("N01°22'31\"E", EasementAnnotation.Bearing(Dms(1, 22, 31), Settings, "°"));
    }

    [Fact]
    public void AreaReadsLikeTheExhibitAndTheLegal()
    {
        Assert.Equal("APPROX. SEWER EASEMENT AREA = 2,133 SF", EasementAnnotation.AreaLine(2133.2, Settings, "sewer"));
        Assert.Equal(new[] { "APPROX. SEWER EASEMENT AREA = 2,133 SF" }, EasementAnnotation.AreaLines(2133.2, Settings, "SEWER"));
        Assert.Equal("SAID TEMPORARY CONSTRUCTION EASEMENT CONTAINING 4,181 SQUARE FEET, MORE OR LESS.",
                     EasementAnnotation.LegalAreaLine(4180.6, Settings, "TEMPORARY CONSTRUCTION"));
    }

    [Fact]
    public void AcresAreShownOnlyWhenTheProfileAsksForThem()
    {
        var s = new EasementSettings { AcresFormat = "{acres} ACRES" };
        Assert.Equal(2, EasementAnnotation.AreaLines(43560, s, "SEWER").Count);
        Assert.Equal("1.000 ACRES", EasementAnnotation.AreaLines(43560, s, "SEWER")[1]);
    }

    [Fact]
    public void ShortCoursesAndTiesGoToTheTableLikeExhibitB()
    {
        // 1" = 60', text 0.08" plotted: 4.8' in the drawing.
        var poc = new P2(5000, 5000);
        var pob = Along(poc, 360 - Dms(87, 55, 53), 13.19);
        var a1 = Along(pob, Dms(44, 26, 9), 13.62);
        var a2 = Along(a1, Dms(1, 22, 31), 244.44);
        var terminus = Along(a2, Dms(52, 36, 35), 83.48);
        var corner = Along(terminus, 360 - Dms(87, 55, 53), 65.10);

        var plan = EasementAnnotation.PlanCenterlineLabels(
            EasementAnnotation.Describe(Course.Line(poc, pob)),
            new[] { Course.Line(pob, a1), Course.Line(a1, a2), Course.Line(a2, terminus) },
            EasementAnnotation.Describe(Course.Line(terminus, corner)),
            Settings, 4.8, "°");

        Assert.Equal(5, plan.Count);
        Assert.Equal(new[] { true, true, false, false, true }, plan.Select(p => p.InTable));
        Assert.Equal(new[] { "L1", "L2", "L3" }, plan.Where(p => p.InTable).Select(p => p.Data.Id));
        Assert.Equal("N87°55'53\"W 13.19'", EasementAnnotation.LineText(plan[0].Data, Settings, "°"));
        Assert.Equal("N44°26'09\"E", EasementAnnotation.Bearing(plan[1].Data.AzimuthDegrees, Settings, "°"));
        Assert.Equal("N01°22'31\"E 244.44'", plan[2].Lines[0]);
        Assert.Equal("65.10'", EasementAnnotation.Distance(plan[4].Data.Length, Settings));
        Assert.True(plan[0].IsTie && plan[4].IsTie && !plan[2].IsTie);
    }

    [Fact]
    public void TableAndDirectModesOverrideTheFit()
    {
        var route = new[] { Course.Line(new P2(0, 0), new P2(0, 10)), Course.Line(new P2(0, 10), new P2(0, 500)) };
        var table = EasementAnnotation.PlanCenterlineLabels(null, route, null, new EasementSettings { LabelMode = EasementLabelMode.Table }, 4.8, "°");
        Assert.All(table, p => Assert.True(p.InTable));
        var direct = EasementAnnotation.PlanCenterlineLabels(null, route, null, new EasementSettings { LabelMode = EasementLabelMode.Direct }, 4.8, "°");
        Assert.All(direct, p => Assert.False(p.InTable));
        Assert.Empty(EasementAnnotation.PlanCenterlineLabels(null, route, null, new EasementSettings { LabelMode = EasementLabelMode.None }, 4.8, "°"));
    }
}
