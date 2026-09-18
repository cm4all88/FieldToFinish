using FieldCodes.Easements;
using FieldCodes.Settings;

namespace FieldCodes.Tests;

/// <summary>
/// Strip easement geometry. Areas are checked against closed-form values: a strip
/// of constant width W along a centerline of length L has area W x L exactly when
/// both sides are offset by the same amount -- for lines, mitred angle points and
/// arcs alike -- which makes a strong, independent check on the offsetting.
/// </summary>
public sealed class EasementBuilderTests
{
    private const double Tol = EasementBuilder.DefaultTolerance;
    private static readonly EasementSettings Settings = new EasementSettings();

    private static P2 P(double x, double y) => new P2(x, y);
    private static Course L(double x0, double y0, double x1, double y1) => Course.Line(P(x0, y0), P(x1, y1));

    private static List<Course> Route(params Course[] courses) => courses.ToList();

    private static EasementBuildResult Build(List<Course> route, WidthSpec width,
                                             TerminationSpec? begin = null, TerminationSpec? end = null)
        => EasementBuilder.Build(route, width, begin, end, Tol);

    // ========================================================== straight

    [Fact]
    public void AStraightCenteredStripIsWidthTimesLength()
    {
        var r = Build(Route(L(0, 0, 100, 0)), WidthSpec.Centered(20));

        Assert.True(r.Ok, string.Join("; ", r.Errors));
        Assert.Equal(2000.0, r.Area, 6);
        Assert.Equal(4, r.Boundary.Count);
        Assert.Equal(P(0, 10).X, r.LeftSideline[0].Start.X, 6);
        Assert.Equal(10.0, r.LeftSideline[0].Start.Y, 6);
        Assert.Equal(-10.0, r.RightSideline[0].Start.Y, 6);
    }

    [Fact]
    public void AOneSidedStripHasItsLeftSidelineOnTheRoute()
    {
        var r = Build(Route(L(0, 0, 100, 0)), WidthSpec.Sides(0, 20));
        Assert.True(r.Ok);
        Assert.Equal(2000.0, r.Area, 6);
        Assert.Equal(0.0, r.LeftSideline[0].Start.Y, 9);
        Assert.Equal(-20.0, r.RightSideline[0].Start.Y, 9);
    }

    [Fact]
    public void AnAsymmetricStripOffsetsEachSideByItsOwnWidth()
    {
        var r = Build(Route(L(0, 0, 100, 0)), WidthSpec.Sides(15, 5));
        Assert.True(r.Ok);
        Assert.Equal(2000.0, r.Area, 6);
        Assert.Equal(15.0, r.LeftSideline[0].Start.Y, 9);
        Assert.Equal(-5.0, r.RightSideline[0].Start.Y, 9);
    }

    // ===================================================== angle points

    [Fact]
    public void AngleSidelinesMitreAndTheAreaStaysWidthTimesLength()
    {
        // An L: east 100, then north 100. The inside loses a 10x10 corner and the
        // outside gains one, so the area is still 20 x 200.
        var r = Build(Route(L(0, 0, 100, 0), L(100, 0, 100, 100)), WidthSpec.Centered(20));

        Assert.True(r.Ok, string.Join("; ", r.Errors));
        Assert.Equal(4000.0, r.Area, 6);
        Assert.Equal(90.0, r.LeftSideline[0].End.X, 6);      // inside corner, trimmed
        Assert.Equal(110.0, r.RightSideline[0].End.X, 6);    // outside corner, extended
        Assert.Equal(-10.0, r.RightSideline[0].End.Y, 6);
    }

    [Fact]
    public void SeveralAnglePointsAllMitre()
    {
        var route = Route(L(0, 0, 100, 0), L(100, 0, 150, 50), L(150, 50, 250, 50), L(250, 50, 250, 150));
        var r = Build(route, WidthSpec.Centered(10));

        Assert.True(r.Ok, string.Join("; ", r.Errors));
        Assert.Equal(10 * EasementBuilder.RouteLength(route), r.Area, 4);
        Assert.Empty(Loops.SelfIntersections(r.Boundary, Tol));
    }

    // ============================================================ curves

    private static Course TangentArc(P2 start, double radius, double startHeadingDeg, double sweepDeg, bool left)
    {
        var heading = startHeadingDeg * Math.PI / 180;
        var dir = P(Math.Cos(heading), Math.Sin(heading));
        var normal = left ? dir.LeftNormal() : dir.LeftNormal() * -1;
        var center = start + normal * radius;
        var a0 = Math.Atan2(start.Y - center.Y, start.X - center.X);
        var a1 = a0 + (left ? 1 : -1) * sweepDeg * Math.PI / 180;
        var end = P(center.X + radius * Math.Cos(a1), center.Y + radius * Math.Sin(a1));
        return Course.Arc(start, end, center, left);
    }

    [Fact]
    public void ATangentCurveStripIsWidthTimesLength()
    {
        var line1 = L(0, 0, 100, 0);
        var arc = TangentArc(P(100, 0), 100, 0, 90, left: true);
        var line2 = Course.Line(arc.End, arc.End + P(0, 100));
        var route = Route(line1, arc, line2);

        var r = Build(route, WidthSpec.Centered(20));

        Assert.True(r.Ok, string.Join("; ", r.Errors));
        Assert.Equal(20 * (200 + Math.PI * 50), r.Area, 4);
        Assert.Equal(90.0, r.LeftSideline[1].Radius, 6);      // inside of a left turn
        Assert.Equal(110.0, r.RightSideline[1].Radius, 6);
    }

    [Fact]
    public void CompoundAndReverseCurvesOffsetExactly()
    {
        var a1 = TangentArc(P(0, 0), 200, 0, 30, left: true);
        var a2 = TangentArc(a1.End, 120, 30, 40, left: true);                  // compound
        var a3 = TangentArc(a2.End, 150, 70, 50, left: false);                  // reverse
        var route = Route(a1, a2, a3);

        var r = Build(route, WidthSpec.Centered(16));

        Assert.True(r.Ok, string.Join("; ", r.Errors));
        Assert.Equal(16 * EasementBuilder.RouteLength(route), r.Area, 3);
        Assert.True(Loops.LargestGap(r.Boundary) < 1e-6);
    }

    [Fact]
    public void AnAsymmetricStripOnACurveMatchesTheAnnulusFormula()
    {
        var arc = TangentArc(P(0, 0), 100, 0, 60, left: true);
        var r = Build(Route(arc), WidthSpec.Sides(15, 5));

        Assert.True(r.Ok);
        var sweep = Math.PI / 3;
        var expected = sweep / 2 * (105.0 * 105.0 - 85.0 * 85.0);
        Assert.Equal(expected, r.Area, 4);
    }

    [Fact]
    public void ACurveTighterThanTheWidthIsRejected()
    {
        var arc = TangentArc(P(0, 0), 8, 0, 90, left: true);
        var r = Build(Route(arc), WidthSpec.Sides(20, 0));

        Assert.False(r.Ok);
        Assert.Contains(r.Errors, e => e.Contains("too tight"));
    }

    [Fact]
    public void AFoldedOffsetIsRejected_NotDrawn()
    {
        // A hairpin 6' wide with a 20' strip: the sidelines fold through each other.
        var r = Build(Route(L(0, 0, 100, 0), L(100, 0, 100, 6), L(100, 6, 0, 6)), WidthSpec.Centered(20));
        Assert.False(r.Ok);
    }

    // ====================================================== terminations

    [Fact]
    public void SidelinesTrimToASlantedPropertyLine()
    {
        var boundary = new List<Course> { L(90, -50, 110, 50) };
        var end = new TerminationSpec { Method = TerminationMethod.Boundary, BoundaryKind = "PROPERTY LINE", Boundary = boundary };

        var r = Build(Route(L(0, 0, 150, 0)), WidthSpec.Centered(20), end: end);

        Assert.True(r.Ok, string.Join("; ", r.Errors));
        Assert.Equal(102.0, r.LeftSideline[0].End.X, 6);
        Assert.Equal(98.0, r.RightSideline[0].End.X, 6);
        Assert.Equal(2000.0, r.Area, 6);
        Assert.Single(r.EndEdge);
    }

    [Fact]
    public void SidelinesExtendToARightOfWayBeyondTheRoute()
    {
        var row = new List<Course> { L(150, -40, 150, 40) };
        var end = new TerminationSpec { Method = TerminationMethod.Boundary, BoundaryKind = "ROW", Boundary = row };

        var r = Build(Route(L(0, 0, 100, 0)), WidthSpec.Centered(20), end: end);

        Assert.True(r.Ok, string.Join("; ", r.Errors));
        Assert.Equal(150.0, r.LeftSideline[0].End.X, 6);
        Assert.Equal(3000.0, r.Area, 6);
    }

    [Fact]
    public void TheBeginningExtendsBackToABoundary()
    {
        var row = new List<Course> { L(-30, -40, -30, 40) };
        var begin = new TerminationSpec { Method = TerminationMethod.Boundary, Boundary = row };

        var r = Build(Route(L(0, 0, 100, 0)), WidthSpec.Centered(20), begin: begin);

        Assert.True(r.Ok, string.Join("; ", r.Errors));
        Assert.Equal(-30.0, r.LeftSideline[0].Start.X, 6);
        Assert.Equal(2600.0, r.Area, 6);
    }

    [Fact]
    public void ABoundaryTheSidelineNeverReachesIsAnError()
    {
        var parallel = new List<Course> { L(0, 50, 100, 50) };
        var end = new TerminationSpec { Method = TerminationMethod.Boundary, Boundary = parallel };

        var r = Build(Route(L(0, 0, 100, 0)), WidthSpec.Centered(20), end: end);
        Assert.False(r.Ok);
        Assert.Contains(r.Errors, e => e.Contains("never reaches"));
    }

    [Fact]
    public void AnEndEdgeFollowsBoundaryVertices()
    {
        // A property line with a corner between the two sidelines.
        var boundary = new List<Course> { L(100, -30, 105, 0), L(105, 0, 100, 30) };
        var end = new TerminationSpec { Method = TerminationMethod.Boundary, Boundary = boundary };

        var r = Build(Route(L(0, 0, 150, 0)), WidthSpec.Centered(20), end: end);

        Assert.True(r.Ok, string.Join("; ", r.Errors));
        Assert.Equal(2, r.EndEdge.Count);
        Assert.Equal(105.0, r.EndEdge[0].End.X, 6);
    }

    [Fact]
    public void StationTerminationsShortenTheStrip()
    {
        var r = Build(Route(L(0, 0, 100, 0)), WidthSpec.Centered(20),
                      new TerminationSpec { Method = TerminationMethod.Station, Station = 20 },
                      new TerminationSpec { Method = TerminationMethod.Station, Station = 80 });

        Assert.True(r.Ok);
        Assert.Equal(1200.0, r.Area, 6);
        Assert.Equal(20.0, r.LeftSideline[0].Start.X, 6);
    }

    [Fact]
    public void APointTerminationIsSquareThroughItsProjection()
    {
        var end = new TerminationSpec
        {
            Method = TerminationMethod.Point,
            Point = new SelectedLocation { X = 64, Y = 33, Source = LocationSource.CogoPoint, PointNumber = "2041" }
        };
        var r = Build(Route(L(0, 0, 100, 0)), WidthSpec.Centered(20), end: end);
        Assert.True(r.Ok);
        Assert.Equal(1280.0, r.Area, 6);
    }

    [Fact]
    public void ReversedTerminationsAreAnError()
    {
        var r = Build(Route(L(0, 0, 100, 0)), WidthSpec.Centered(20),
                      new TerminationSpec { Method = TerminationMethod.Station, Station = 80 },
                      new TerminationSpec { Method = TerminationMethod.Station, Station = 20 });
        Assert.False(r.Ok);
    }

    [Fact]
    public void ATaperedWidthIsRejectedUntilSupported()
    {
        var width = WidthSpec.Sides(10, 10);
        width.LeftEnd = 5;
        var r = Build(Route(L(0, 0, 100, 0)), width);
        Assert.False(r.Ok);
        Assert.Contains(r.Errors, e => e.Contains("tapered"));
    }

    // ============================================================ route

    [Fact]
    public void RoutePiecesAreChainedAndFlippedFromTheTpob()
    {
        var pieces = new List<IList<Course>>
        {
            new List<Course> { L(100, 0, 0, 0) },            // drawn backwards
            new List<Course> { L(100, 100, 100, 0) }          // also backwards
        };
        var route = EasementBuilder.OrderRoute(pieces, P(0, 0), Tol);

        Assert.True(route.Ok, string.Join("; ", route.Errors));
        Assert.Equal(0.0, route.Courses[0].Start.X, 9);
        Assert.Equal(100.0, route.Courses[1].End.Y, 9);
    }

    [Fact]
    public void AGapBetweenRoutePiecesIsNeverBridged()
    {
        var pieces = new List<IList<Course>>
        {
            new List<Course> { L(0, 0, 100, 0) },
            new List<Course> { L(100.5, 0, 200, 0) }
        };
        var route = EasementBuilder.OrderRoute(pieces, P(0, 0), Tol);
        Assert.False(route.Ok);
        Assert.Contains(route.Errors, e => e.Contains("gap"));
    }

    [Fact]
    public void ATpobOffTheRouteIsAnError_AndOnItTheRouteStartsThere()
    {
        var pieces = new List<IList<Course>> { new List<Course> { L(0, 0, 100, 0) } };

        Assert.False(EasementBuilder.OrderRoute(pieces, P(20, 3), Tol).Ok);

        var onIt = EasementBuilder.OrderRoute(pieces, P(20, 0), Tol);
        Assert.True(onIt.Ok);
        Assert.Equal(20.0, onIt.Courses[0].Start.X, 9);
        Assert.NotEmpty(onIt.Warnings);
    }

    [Fact]
    public void PolylineBulgesBecomeExactArcs()
    {
        // A semicircle bulge of 1 from (0,0) to (20,0): radius 10, turning left.
        var arc = Course.FromBulge(P(0, 0), P(20, 0), 1.0);
        Assert.Equal(CourseKind.Arc, arc.Kind);
        Assert.Equal(10.0, arc.Radius, 9);
        Assert.Equal(Math.PI * 10, arc.Length, 9);
        Assert.True(arc.CounterClockwise);
        Assert.Equal(-10.0, arc.PointAt(arc.Length / 2).Y, 6);
    }

    // ======================================================= annotation

    [Fact]
    public void LinesAndCurvesAreNumberedForTheTable()
    {
        var arc = TangentArc(P(100, 0), 100, 0, 90, left: true);
        var numbered = EasementAnnotation.Number(new[] { L(0, 0, 100, 0), arc, Course.Line(arc.End, arc.End + P(0, 50)) }, Settings);

        Assert.Equal(new[] { "L1", "C1", "L2" }, numbered.Select(n => n.Id));
        var c1 = numbered[1];
        Assert.Equal(90.0, c1.DeltaDegrees!.Value, 6);
        Assert.Equal(100.0, c1.Radius!.Value, 9);
        Assert.Equal(Math.PI * 50, c1.Length, 6);
        Assert.Equal(100.0, c1.Tangent!.Value, 6);
        Assert.Equal(Math.Sqrt(2) * 100, c1.ChordLength!.Value, 6);
        Assert.Equal(45.0, c1.ChordAzimuthDegrees!.Value, 6);
        Assert.Equal("LEFT", c1.TurnDirection);
    }

    [Fact]
    public void DirectLabelsUseTheProfileBearingAndDistanceFormats()
    {
        var data = EasementAnnotation.Describe(L(0, 0, 70.7107, 70.7107));
        Assert.Equal("N45°00'00\"E 100.00'", EasementAnnotation.LineText(data, Settings, "°"));
        Assert.Equal("N 45°00'00\" E 100.00'", EasementAnnotation.LineText(data, new EasementSettings { BearingSpaces = true }, "°"));

        var curve = EasementAnnotation.Describe(TangentArc(P(0, 0), 250, 0, 12.5, left: false));
        var lines = EasementAnnotation.CurveLines(curve, Settings, "°");
        Assert.Equal("R=250.00'", lines[0]);
        Assert.Equal("Δ=12°30'00\"", lines[2]);
        Assert.StartsWith("CH=", lines[3]);
    }

    [Fact]
    public void AreaTitleAndAcresFollowTheExample()
    {
        Assert.Equal("APPROX. UTILITY EASEMENT AREA = 8,426 SF", EasementAnnotation.AreaLine(8426.2, Settings));
        Assert.Equal(string.Empty, EasementAnnotation.AcresLine(8426.2, Settings));
        var older = new EasementSettings { AreaFormat = "AREA = {sqft} SQ. FT.", AcresFormat = "{acres} ACRES" };
        Assert.Equal("AREA = 8,426 SQ. FT.", EasementAnnotation.AreaLine(8426.2, older));
        Assert.Equal("0.193 ACRES", EasementAnnotation.AcresLine(8426.2, older));
        Assert.Equal("20.00' WIDE UTILITY EASEMENT", EasementAnnotation.Title(WidthSpec.Centered(20), null, Settings));
        Assert.Equal("15.00' WIDE STORM DRAINAGE EASEMENT",
                     EasementAnnotation.Title(WidthSpec.Sides(10, 5), "storm drainage", Settings));
    }

    [Fact]
    public void AChangedControllingGeometryChangesTheFingerprint()
    {
        var before = EasementAnnotation.Fingerprint(new[] { L(0, 0, 100, 0), L(100, 0, 100, 50) });
        var same = EasementAnnotation.Fingerprint(new[] { L(0, 0, 100, 0), L(100, 0, 100, 50) });
        var moved = EasementAnnotation.Fingerprint(new[] { L(0, 0, 100, 0), L(100, 0, 100.01, 50) });

        Assert.Equal(before, same);
        Assert.NotEqual(before, moved);
    }

    [Fact]
    public void TheRecordKeepsTheOrderedGeometryForALaterLegalDescription()
    {
        var r = Build(Route(L(0, 0, 100, 0)), WidthSpec.Centered(20));
        var record = new EasementRecord
        {
            Purpose = "UTILITY",
            PointOfCommencement = new SelectedLocation { X = -50, Y = -50, Source = LocationSource.CogoPoint, PointNumber = "1001" },
            TruePointOfBeginning = new SelectedLocation { X = 0, Y = 0, Source = LocationSource.GeometryEndpoint, Handle = "2A3" },
            CommencementTie = EasementAnnotation.Describe(L(-50, -50, 0, 0)),
            Width = WidthSpec.Centered(20),
            Begin = TerminationSpec.Perpendicular(),
            End = TerminationSpec.Perpendicular(),
            AreaSquareFeet = r.Area,
            Acres = r.Area / EasementAnnotation.SquareFeetPerAcre
        };
        record.RouteCourses.AddRange(EasementAnnotation.Number(r.Centerline, Settings));
        record.BoundaryCourses.AddRange(EasementAnnotation.Number(r.Boundary, Settings));

        var back = EasementRecord.FromJson(record.ToJson());

        Assert.Equal("1001", back.PointOfCommencement.PointNumber);
        Assert.Equal(LocationSource.GeometryEndpoint, back.TruePointOfBeginning.Source);
        Assert.Equal(45.0, back.CommencementTie.AzimuthDegrees, 6);
        Assert.Equal(4, back.BoundaryCourses.Count);
        Assert.Equal(100.0, back.RouteCourses[0].Course.End.X, 9);
        Assert.Contains("surveyor review", back.LegalStatus);
    }
}
