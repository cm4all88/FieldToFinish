using FieldCodes.Easements;
using FieldCodes.Settings;

namespace FieldCodes.Tests;

/// <summary>
/// The draft legal description, checked line by line against the office's stamped
/// Exhibit A for 28052700104300 (with its typos corrected): same courses, ties, widths,
/// areas and wording.
/// </summary>
public sealed class LegalDescriptionTests
{
    private static readonly EasementSettings Settings = new();

    private static P2 Along(P2 from, double azimuthDegrees, double distance)
    {
        var a = azimuthDegrees * Math.PI / 180;
        return new P2(from.X + Math.Sin(a) * distance, from.Y + Math.Cos(a) * distance);
    }

    private static double Dms(int d, int m, int s) => d + m / 60.0 + s / 3600.0;

    private static (EasementRecord Permanent, EasementRecord Temporary) Parcel4()
    {
        var poc = new P2(5000, 5000);
        var pob = Along(poc, 360 - Dms(87, 55, 53), 13.19);
        var a1 = Along(pob, Dms(44, 26, 9), 13.62);
        var a2 = Along(a1, Dms(1, 22, 31), 244.44);
        var terminus = Along(a2, Dms(52, 36, 35), 83.48);
        var corner = Along(terminus, 360 - Dms(87, 55, 53), 65.10);
        var south = new GeometrySource { Handle = "1A", Role = "TRIM LINE 1", EntityType = "Line" };
        var east = new GeometrySource { Handle = "1B", Role = "TRIM LINE 2", EntityType = "Line" };

        EasementRecord Make(WidthSpec width, string purpose, double area, string role) => new()
        {
            Purpose = purpose, Width = width, AreaSquareFeet = area, Role = role,
            PointOfCommencement = new SelectedLocation { X = poc.X, Y = poc.Y, Source = LocationSource.CogoPoint },
            CommencementTie = EasementAnnotation.Describe(Course.Line(poc, pob)),
            CommencementAlong = south, BeginsOn = south, EndsOn = east,
            RouteCourses = EasementAnnotation.Number(new[] { Course.Line(pob, a1), Course.Line(a1, a2), Course.Line(a2, terminus) }, Settings),
            TerminusTiePoint = new SelectedLocation { X = corner.X, Y = corner.Y, Source = LocationSource.GeometryEndpoint },
            TerminusTie = EasementAnnotation.Describe(Course.Line(terminus, corner)),
            TrimLines = new List<GeometrySource> { south, east },
        };
        return (Make(WidthSpec.Centered(15), "SEWER", 2133.2, EasementRecord.PermanentRole),
                Make(WidthSpec.Centered(25), "TEMPORARY CONSTRUCTION", 4180.8, EasementRecord.TemporaryRole));
    }

    private static readonly LegalInputs Names = new()
    {
        ParcelDescription = "PARCEL 4, SNOHOMISH COUNTY SHORT PLAT NO. SP-330 (7-79), RECORDED UNDER AUDITOR'S FILE NUMBER 8005300213, RECORDS OF SNOHOMISH COUNTY, WASHINGTON, BEING A PORTION OF THE SOUTHEAST QUARTER OF THE NORTHEAST QUARTER OF SECTION 27, TOWNSHIP 28 NORTH, RANGE 5 EAST, W.M., IN SNOHOMISH COUNTY, WASHINGTON",
        CommencementCorner = "THE SOUTHEAST CORNER OF THE SOUTHWEST QUARTER OF THE NORTHEAST QUARTER OF SAID SECTION 27",
        BeginningLine = "THE SOUTH LINE OF SAID SOUTHWEST QUARTER OF THE NORTHEAST QUARTER",
        TerminusLine = "THE EAST LINE OF THE NORTHWEST QUARTER OF THE NORTHEAST QUARTER OF SAID SECTION 27",
        TerminusCorner = "THE NORTHWEST CORNER OF SAID PARCEL 4",
        County = "Snohomish",
    };

    [Fact]
    public void TheDraftReadsLikeTheStampedExhibitA()
    {
        var (permanent, temporary) = Parcel4();
        var draft = LegalDescriptionWriter.Write(permanent, temporary, Names, Settings, null);
        var lines = draft.Text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToList();

        Assert.Equal(new[]
        {
            LegalDescriptionWriter.DraftBanner,
            "EXHIBIT A",
            "SEWER EASEMENT AND TEMPORARY CONSTRUCTION EASEMENT",
            "LEGAL DESCRIPTION",
            "THAT PORTION OF " + Names.ParcelDescription + ", DESCRIBED AS FOLLOWS:",
            "A 15.00 FOOT WIDE PERMANENT SEWER EASEMENT, LYING 7.50 FEET ON EACH SIDE OF THE FOLLOWING DESCRIBED CENTERLINE, TOGETHER WITH A 25.00 FOOT WIDE TEMPORARY CONSTRUCTION EASEMENT, LYING 12.50 FEET ON EACH SIDE OF SAID CENTERLINE, SAID TEMPORARY CONSTRUCTION EASEMENT TO TERMINATE UPON COMPLETION OF CONSTRUCTION:",
            "COMMENCING AT THE SOUTHEAST CORNER OF THE SOUTHWEST QUARTER OF THE NORTHEAST QUARTER OF SAID SECTION 27;",
            "THENCE ALONG THE SOUTH LINE OF SAID SOUTHWEST QUARTER OF THE NORTHEAST QUARTER, NORTH 87°55'53\" WEST 13.19 FEET TO THE POINT OF BEGINNING;",
            "THENCE DEPARTING SAID SOUTH LINE, NORTH 44°26'09\" EAST 13.62 FEET;",
            "THENCE NORTH 01°22'31\" EAST 244.44 FEET;",
            "THENCE NORTH 52°36'35\" EAST 83.48 FEET TO THE EAST LINE OF THE NORTHWEST QUARTER OF THE NORTHEAST QUARTER OF SAID SECTION 27 AND THE TERMINUS OF THIS CENTERLINE DESCRIPTION, TO WHICH THE NORTHWEST CORNER OF SAID PARCEL 4 BEARS NORTH 87°55'53\" WEST 65.10 FEET.",
            "ALL SIDELINES OF THIS EASEMENT SHALL BE SHORTENED OR LENGTHENED TO TERMINATE AT ALL ANGLE POINTS.",
            "SAID TEMPORARY CONSTRUCTION EASEMENT CONTAINING 4,181 SQUARE FEET, MORE OR LESS.",
            "SAID PERMANENT SEWER EASEMENT CONTAINING 2,133 SQUARE FEET, MORE OR LESS.",
            "SITUATE IN THE COUNTY OF SNOHOMISH, STATE OF WASHINGTON.",
        }, lines);
        Assert.Empty(draft.Checks);
    }

    [Fact]
    public void MissingNamesAreLeftAsBracketedBlanksAndListed()
    {
        var (permanent, _) = Parcel4();
        var draft = LegalDescriptionWriter.Write(permanent, null, new LegalInputs(), Settings, null);
        Assert.Contains("THAT PORTION OF [PARCEL DESCRIPTION], DESCRIBED AS FOLLOWS:", draft.Text);
        Assert.Contains("COMMENCING AT [COMMENCEMENT CORNER];", draft.Text);
        Assert.Contains("TO [TERMINUS LINE] AND THE TERMINUS", draft.Text);
        Assert.Contains("A 15.00 FOOT WIDE SEWER EASEMENT, LYING 7.50 FEET", draft.Text);
        Assert.Contains("SAID SEWER EASEMENT CONTAINING 2,133 SQUARE FEET", draft.Text);
        Assert.Contains(draft.Checks, c => c.Contains("parcel description"));
        Assert.Contains(draft.Checks, c => c.Contains("county"));
    }

    [Fact]
    public void AChangedSurveyIsTheFirstCheck()
    {
        var (permanent, _) = Parcel4();
        var draft = LegalDescriptionWriter.Write(permanent, null, Names, Settings, new[] { "TRIM LINE 1 (Line) has moved or changed shape." });
        Assert.StartsWith("The survey has changed", draft.Checks[0]);
    }

    [Fact]
    public void WithoutACommencementTheDescriptionBegins()
    {
        var (permanent, _) = Parcel4();
        permanent.PointOfCommencement = null;
        permanent.CommencementTie = null;
        var draft = LegalDescriptionWriter.Write(permanent, null, Names, Settings, null);
        Assert.Contains("BEGINNING AT [DESCRIBE THE POINT OF BEGINNING ON THE SOUTH LINE OF SAID SOUTHWEST QUARTER OF THE NORTHEAST QUARTER];", draft.Text);
        Assert.Contains("THENCE DEPARTING SAID SOUTH LINE, NORTH 44°26'09\" EAST 13.62 FEET;", draft.Text);
    }

    [Fact]
    public void AOneSidedEasementIsDescribedFromItsLine()
    {
        var (permanent, _) = Parcel4();
        permanent.Width = WidthSpec.Sides(0, 10);
        var draft = LegalDescriptionWriter.Write(permanent, null, Names, Settings, null);
        // First course runs N44°E: its right side faces S46°E (azimuth 134°), which reads as EASTERLY.
        Assert.Contains("A 10.00 FOOT WIDE SEWER EASEMENT, LYING 10.00 FEET EASTERLY OF AND ADJACENT TO THE FOLLOWING DESCRIBED LINE:", draft.Text);
        Assert.Contains("THE TERMINUS OF THIS LINE DESCRIPTION", draft.Text);
        Assert.Contains(draft.Checks, c => c.Contains("side words"));
    }

    [Fact]
    public void CurvesAreDescribedTangentOrNot()
    {
        var (permanent, _) = Parcel4();
        var start = new P2(0, 0);
        permanent.RouteCourses = EasementAnnotation.Number(new[]
        {
            Course.Line(start, new P2(100, 0)),
            Course.Arc(new P2(100, 0), new P2(200, 100), new P2(100, 100), true),
        }, Settings);
        var draft = LegalDescriptionWriter.Write(permanent, null, Names, Settings, null);
        Assert.Contains("THENCE ALONG A CURVE TO THE LEFT, HAVING A RADIUS OF 100.00 FEET, THROUGH A CENTRAL ANGLE OF 90°00'00\", AN ARC DISTANCE OF 157.08 FEET", draft.Text);

        permanent.RouteCourses = EasementAnnotation.Number(new[] { Course.Arc(new P2(100, 0), new P2(200, 100), new P2(100, 100), true) }, Settings);
        draft = LegalDescriptionWriter.Write(permanent, null, Names, Settings, null);
        Assert.Contains("NON-TANGENT CURVE TO THE LEFT, THE RADIUS POINT OF WHICH BEARS NORTH 00°00'00\" EAST 100.00 FEET", draft.Text);
    }

    [Fact]
    public void ShortLineNamesComeFromTheFullName()
    {
        Assert.Equal("SOUTH LINE", LegalDescriptionWriter.ShortName("THE SOUTH LINE OF SAID PARCEL 2"));
        Assert.Equal("RIGHT-OF-WAY MARGIN", LegalDescriptionWriter.ShortName("the right-of-way margin"));
    }
}
