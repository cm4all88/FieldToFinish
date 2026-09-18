using FieldCodes.Easements;
using FieldCodes.Exhibits;
using FieldCodes.Settings;

namespace FieldCodes.Tests;

/// <summary>
/// The production cleanup pass, from issues the real drawings exposed: a commencement tie that follows a
/// right-of-way margin drawn as a line and a curve (Kenmore 710), a lot drawn as separate lines, honest width
/// dimensions, the north arrow of a turned view, exhibit hatch scale, viewport content choices, office wording
/// options and the grouped QA summary.
/// </summary>
public sealed class ProductionCleanupTests
{
    private const double Tol = EasementBuilder.DefaultTolerance;
    private static readonly EasementSettings Settings = new();
    private static P2 P(double x, double y) => new(x, y);
    private static Course L(double x0, double y0, double x1, double y1) => Course.Line(P(x0, y0), P(x1, y1));

    // ------------------------------------------------------------------ Kenmore 710 (C4)

    private static readonly P2 Center = P(1294138.9871, 283167.8494);
    private static P2 OnArc(double degrees) => Center + P(Math.Cos(degrees * Math.PI / 180), Math.Sin(degrees * Math.PI / 180)) * 210.5;

    /// <summary>The south margin of NE 192nd St as drawn: line 130F8 east from the curve, and curve 130F1 (R 210.50).</summary>
    private static readonly Course MarginLine = L(1294176.5646, 283374.9681, 1294590.8782, 283299.7994);
    private static readonly Course MarginArc = Course.Arc(OnArc(79.7167), OnArc(121.1667), Center, true);
    private static readonly P2 Poc = P(1294185.2053, 283373.4005);
    private static readonly P2 Pob = P(1294165.5448, 283376.6673);

    [Fact]
    public void TheC4TieFollowsTheMarginLineAndCurveInsteadOfTheChord()
    {
        // Picked in any order, the line backwards: joined end to end without touching the objects.
        var chain = CurveChain.Open(new List<IList<Course>> { new List<Course> { MarginArc }, new List<Course> { MarginLine.Reversed() } }, Poc, Tol);
        Assert.True(chain.Ok, chain.Problem);
        var path = AreaPath.Between(chain.Courses, Poc, Pob, Tol, out var problem);
        Assert.NotNull(path);
        Assert.Null(problem);

        Ties.Set(path, out var tie, out var courses);
        Assert.NotNull(courses);
        Assert.Equal(2, courses!.Count);
        Assert.Equal(CourseKind.Line, courses[0].Course.Kind);
        Assert.Equal(8.78, courses[0].Length, 2);
        Assert.Equal(CourseKind.Arc, courses[1].Course.Kind);
        Assert.Equal(210.5, courses[1].Radius!.Value, 3);
        Assert.Equal(82.752 - 79.7167, courses[1].DeltaDegrees!.Value, 2);
        Assert.Same(courses[0], tie);
        // The margin bends only slightly here, so the lengths are close -- but the stated tie is the line and the curve, not the chord.
        Assert.Equal(19.93, Poc.DistanceTo(Pob), 2);
        Assert.True(courses.Sum(c => c.Length) >= Poc.DistanceTo(Pob));
        Assert.Equal(Pob.X, courses[1].Course.End.X, 6);
        Assert.Equal(Pob.Y, courses[1].Course.End.Y, 6);
        Assert.True(Ties.Follows(courses));
    }

    private static EasementRecord C4Area()
    {
        var chain = CurveChain.Open(new List<IList<Course>> { new List<Course> { MarginLine }, new List<Course> { MarginArc } }, Poc, Tol);
        var path = AreaPath.Between(chain.Courses, Poc, Pob, Tol, out _)!;
        Ties.Set(path, out var tie, out var tieCourses);
        // The construction area: along the curve from the POB, then the clicked corners.
        var side = AreaPath.Between(new List<Course> { MarginArc }, Pob, P(1294140.0772, 283378.3465), Tol, out _)!;
        var corners = new[] { P(1294140.0772, 283378.3465), P(1294139.8786, 283334.4833), P(1294185.0282, 283334.2789), P(1294185.1672, 283365.0025), P(1294164.5373, 283368.7454), Pob };
        var loop = side.ToList();
        for (var i = 1; i < corners.Length; i++) loop.Add(Course.Line(corners[i - 1], corners[i]));
        return new EasementRecord
        {
            Kind = EasementRecord.AreaKind, Purpose = "TEMPORARY CONSTRUCTION",
            PointOfCommencement = new SelectedLocation { X = Poc.X, Y = Poc.Y },
            CommencementTie = tie, CommencementTieCourses = tieCourses,
            RouteCourses = EasementAnnotation.Number(loop, Settings),
            BoundaryCourses = EasementAnnotation.Number(loop, Settings),
            AreaSquareFeet = Math.Abs(Loops.SignedArea(loop)),
            AnglePoints = new[] { Pob }.Concat(corners.Take(corners.Length - 1)).Select(p => new SelectedLocation { X = p.X, Y = p.Y }).ToList(),
            AreaSides = new List<AreaSide> { new() { FollowHandle = "130F1" } }.Concat(Enumerable.Range(0, 5).Select(_ => new AreaSide())).ToList()
        };
    }

    [Fact]
    public void TheC4LegalDraftStatesTheTieAlongTheMarginWithItsCurveAndClosureChecksIt()
    {
        var area = C4Area();
        Assert.Equal(1739, area.AreaSquareFeet, 0);     // the office legal's area
        var draft = LegalDescriptionWriter.WriteArea(area, new LegalInputs { TieLine = "THE SOUTH MARGIN OF NE 192ND STREET" }, Settings, null);
        var lines = draft.Text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        var commencing = lines.FindIndex(l => l.StartsWith("COMMENCING AT", StringComparison.Ordinal));
        Assert.StartsWith("THENCE ALONG THE SOUTH MARGIN OF NE 192ND STREET, ", lines[commencing + 1]);
        Assert.Contains("8.78 FEET;", lines[commencing + 1]);
        Assert.StartsWith("THENCE CONTINUING ALONG SAID SOUTH MARGIN, ALONG A CURVE TO THE LEFT, HAVING A RADIUS OF 210.50 FEET", lines[commencing + 2]);
        Assert.EndsWith("TO THE POINT OF BEGINNING;", lines[commencing + 2]);
        Assert.DoesNotContain("19.93", draft.Text);
        Assert.Contains(draft.Checks, c => c.Contains("commencement tie follows the line as drawn (2 courses, with a curve)"));

        var closure = Closure.ForRecord(area, Settings);
        var tieItem = closure.Items.Single(i => i.Name == "Commencement tie");
        Assert.Empty(tieItem.Problems);

        // Without a name for the line, it is a blank for the surveyor -- not guessed.
        var blank = LegalDescriptionWriter.WriteArea(area, new LegalInputs(), Settings, null);
        Assert.Contains("THENCE ALONG [LINE THE TIE RUNS ALONG], ", blank.Text);
    }

    [Fact]
    public void AreaAtStatePlaneCoordinatesIsNotThrownOffByATinyGapAtACurve()
    {
        // The C4 corner coordinates carry four decimals, so the curve's end is 0.00002' off the circle.
        var area = C4Area();
        var loop = area.BoundaryCourses.Select(d => d.Course).ToList();
        var shift = P(-1294000, -283000);
        var nearOrigin = loop.Select(c => c.Kind == CourseKind.Line ? Course.Line(c.Start + shift, c.End + shift) : Course.Arc(c.Start + shift, c.End + shift, c.Center + shift, c.CounterClockwise)).ToList();
        Assert.Equal(Loops.SignedArea(nearOrigin), Loops.SignedArea(loop), 3);
        Assert.InRange(Loops.SignedArea(loop), 1738.8, 1738.95);
    }

    [Fact]
    public void AStraightTieIsStillOneCourseAndReadsAsBefore()
    {
        Ties.Set(new List<Course> { Course.Line(Poc, Pob) }, out var tie, out var courses);
        Assert.Null(courses);
        var record = new EasementRecord { CommencementTie = tie };
        Assert.Single(record.TieCourses());
        Assert.False(Ties.Follows(record.TieCourses()));
    }

    [Fact]
    public void TieCoursesChangeTheExhibitSourceFingerprintButAStraightTieDoesNot()
    {
        var area = C4Area();
        var withCurve = ExhibitPlanner.SourceFingerprint(area);
        var chordOnly = new EasementRecord
        {
            Kind = area.Kind, Title = area.Title, AreaSquareFeet = area.AreaSquareFeet, RouteCourses = area.RouteCourses,
            CommencementTie = EasementAnnotation.Describe(Course.Line(Poc, Pob))
        };
        Assert.NotEqual(withCurve, ExhibitPlanner.SourceFingerprint(chordOnly));
        var sameStraight = new EasementRecord { Kind = chordOnly.Kind, Title = chordOnly.Title, AreaSquareFeet = chordOnly.AreaSquareFeet, RouteCourses = chordOnly.RouteCourses, CommencementTie = chordOnly.CommencementTie, CommencementTieCourses = new List<CourseData>() };
        Assert.Equal(ExhibitPlanner.SourceFingerprint(chordOnly), ExhibitPlanner.SourceFingerprint(sameStraight));
    }

    // ------------------------------------------------------------------ lot from separate lines

    private static List<IList<Course>> Pieces(params Course[] courses) => courses.Select(c => (IList<Course>)new List<Course> { c }).ToList();

    [Fact]
    public void SeparateLotLinesInAnyOrderAndDirectionMakeOneClosedLot()
    {
        var chain = CurveChain.Closed(Pieces(L(100, 200, 0, 200), L(0, 0, 100, 0), L(0, 0, 0, 200), L(100, 0, 100, 200)), Tol);
        Assert.True(chain.Ok, chain.Problem);
        Assert.Equal(4, chain.Courses.Count);
        Assert.Equal(20000, Math.Abs(Loops.SignedArea(chain.Courses)), 6);
        Assert.Equal(0, Loops.LargestGap(chain.Courses), 9);
        Assert.Equal(4, chain.PieceOfCourse.Distinct().Count());
    }

    [Fact]
    public void ALotWithACurvedFrontageAndAPolylineSideCloses()
    {
        var front = Course.Arc(P(0, 0), P(100, 0), P(50, 100), true);                                   // bows out of the lot
        var sides = new List<Course> { L(100, 0, 100, 200), L(100, 200, 0, 200), L(0, 200, 0, 0) };     // one polyline, three courses
        var chain = CurveChain.Closed(new List<IList<Course>> { new List<Course> { front }, sides }, Tol);
        Assert.True(chain.Ok, chain.Problem);
        Assert.Contains(chain.Courses, c => c.Kind == CourseKind.Arc);
        Assert.True(Math.Abs(Loops.SignedArea(chain.Courses)) > 20000);
    }

    [Theory]
    [InlineData("gap", "open near")]
    [InlineData("branch", "meet near")]
    [InlineData("pastCorner", "part way along")]
    [InlineData("twoLots", "separate closed shapes")]
    [InlineData("duplicate", "meet near")]
    [InlineData("oneOpenLine", "One open object")]
    [InlineData("crossing", "cross each other")]
    public void AnythingButOneUnambiguousClosedLotStopsWithTheReason(string layout, string reason)
    {
        List<IList<Course>> pieces = layout switch
        {
            "gap" => Pieces(L(0, 0, 100, 0), L(100, 0, 100, 200), L(100, 200, 0, 200), L(0, 200, 0, 0.5)),
            "branch" => Pieces(L(0, 0, 100, 0), L(100, 0, 100, 200), L(100, 200, 0, 200), L(0, 200, 0, 0), L(100, 200, 180, 200)),
            "pastCorner" => Pieces(L(0, 0, 100, 0), L(100, 0, 100, 200), L(100, 200, 0, 200), L(0, 250, 0, 0)),
            "twoLots" => Pieces(L(0, 0, 100, 0), L(100, 0, 100, 200), L(100, 200, 0, 200), L(0, 200, 0, 0),
                                L(300, 0, 400, 0), L(400, 0, 400, 200), L(400, 200, 300, 200), L(300, 200, 300, 0)),
            "duplicate" => Pieces(L(0, 0, 100, 0), L(100, 0, 100, 200), L(100, 200, 0, 200), L(0, 200, 0, 0), L(100, 0, 0, 0)),
            "oneOpenLine" => Pieces(L(0, 0, 100, 0)),
            "crossing" => Pieces(L(0, 0, 100, 200), L(100, 200, 100, 0), L(100, 0, 0, 200), L(0, 200, 0, 0)),
            _ => throw new ArgumentException(layout)
        };
        var chain = CurveChain.Closed(pieces, Tol);
        Assert.False(chain.Ok);
        Assert.Null(chain.Courses);
        Assert.Contains(reason, chain.Problem);
    }

    [Fact]
    public void LotLinesThatRunPastTheCornersEncloseOneLotAndSayWhatWasSetAside()
    {
        // Silver Lake Parcel 3: the east line runs on past both corners; the north side is two polylines.
        var chain = CurveChain.Enclosed(new List<IList<Course>>
        {
            new List<Course> { L(1317997.8764, 325831.6925, 1318345.1364, 325819.1493) },
            new List<Course> { L(1318343.1229, 325735.2734, 1318369.5223, 326834.9566) },
            new List<Course> { L(1318005.5254, 326147.5991, 1318117.3955, 326143.5583) },
            new List<Course> { L(1318117.3955, 326143.5583, 1318352.7203, 326135.0582) },
            new List<Course> { L(1318005.5254, 326147.5991, 1317997.8764, 325831.6925) }
        }, Tol);
        Assert.True(chain.Ok, chain.Problem);
        Assert.Equal(4, chain.Courses.Count);          // south, east between the corners, north (its two polylines run on one line), west
        Assert.InRange(Math.Abs(Loops.SignedArea(chain.Courses)), 109_000, 111_000);
        Assert.True(chain.IgnoredLength > 700, "the east line's length beyond the corners is set aside and reported (" + chain.IgnoredLength + ")");
        Assert.True(Loops.LargestGap(chain.Courses) < 1e-6);

        // The strict join refuses the same selection: the east line does not end at the corners.
        Assert.False(CurveChain.Closed(new List<IList<Course>> { new List<Course> { L(1317997.8764, 325831.6925, 1318345.1364, 325819.1493) }, new List<Course> { L(1318343.1229, 325735.2734, 1318369.5223, 326834.9566) } }, Tol).Ok);
    }

    [Theory]
    [InlineData("splitByInteriorLine", "2 separate areas")]
    [InlineData("twoLots", "2 separate areas")]
    [InlineData("open", "do not enclose")]
    public void AnEnclosureThatIsNotExactlyOneAreaStops(string layout, string reason)
    {
        List<IList<Course>> pieces = layout switch
        {
            "splitByInteriorLine" => Pieces(L(-10, 0, 110, 0), L(100, -10, 100, 210), L(110, 200, -10, 200), L(0, 210, 0, -10), L(50, -5, 50, 205)),
            "twoLots" => Pieces(L(0, 0, 100, 0), L(100, 0, 100, 200), L(100, 200, 0, 200), L(0, 200, 0, 0),
                                L(300, 0, 400, 0), L(400, 0, 400, 200), L(400, 200, 300, 200), L(300, 200, 300, 0)),
            "open" => Pieces(L(0, 0, 100, 0), L(100, 0, 100, 200), L(100, 200, 0, 200)),
            _ => throw new ArgumentException(layout)
        };
        var chain = CurveChain.Enclosed(pieces, Tol);
        Assert.False(chain.Ok);
        Assert.Contains(reason, chain.Problem);
    }

    [Fact]
    public void AStrayLineAndADuplicateLineDoNotChangeTheOneEnclosedLot()
    {
        var chain = CurveChain.Enclosed(Pieces(L(0, 0, 100, 0), L(100, 0, 100, 200), L(100, 200, 0, 200), L(0, 200, 0, 0), L(100, 200, 180, 230), L(0, 0, 100, 0)), Tol);
        Assert.True(chain.Ok, chain.Problem);
        Assert.Equal(20000, Math.Abs(Loops.SignedArea(chain.Courses)), 6);
        Assert.True(chain.IgnoredLength > 180);
        // A curved frontage crossing past its corners works the same way.
        var curved = CurveChain.Enclosed(new List<IList<Course>> { new List<Course> { Course.Arc(P(-20, 12.9), P(120, 12.9), P(50, 100), true) }, new List<Course> { L(0, -20, 0, 200) }, new List<Course> { L(100, -20, 100, 200) }, new List<Course> { L(-20, 200, 120, 200) } }, Tol);
        Assert.True(curved.Ok, curved.Problem);
        Assert.Contains(curved.Courses, c => c.Kind == CourseKind.Arc);
    }

    [Fact]
    public void AnOpenTiePathMustBeOnePieceEndToEnd()
    {
        Assert.Contains("gap", CurveChain.Open(Pieces(L(0, 0, 50, 0), L(60, 0, 100, 0)), P(0, 0), Tol).Problem);
        Assert.Contains("ambiguous", CurveChain.Open(Pieces(L(0, 0, 50, 0), L(50, 0, 100, 0), L(50, 0, 50, 40)), P(0, 0), Tol).Problem);
        Assert.Contains("close on themselves", CurveChain.Open(Pieces(L(0, 0, 50, 0), L(50, 0, 50, 50), L(50, 50, 0, 0)), P(0, 0), Tol).Problem);
    }

    // ------------------------------------------------------------------ width dimensions

    private static readonly double[] Fractions = { 0.25, 0.5, 0.75, 0.125, 0.375 };

    [Fact]
    public void AStraightStripIsDimensionedWhereItsSidelinesRunParallel()
    {
        var centerline = new List<Course> { L(0, 0, 200, 0) };
        var boundary = new List<Course> { L(0, -10, 200, -10), L(200, -10, 200, 10), L(200, 10, 0, 10), L(0, 10, 0, -10) };
        var spots = WidthDimensions.Spots(centerline, boundary, 10, 10, Tol, Fractions);
        Assert.Equal(5, spots.Count);
        Assert.All(spots, s => Assert.Equal(20, s.Left.DistanceTo(s.Right), 9));
    }

    [Fact]
    public void NoDimensionWhereTheStripIsTaperedOrClippedByATrimLine()
    {
        // The end of the strip is cut by an oblique trim line from x=150 (south side) to x=190 (north side).
        var centerline = new List<Course> { L(0, 0, 200, 0) };
        var boundary = new List<Course> { L(0, -10, 150, -10), L(150, -10, 190, 10), L(190, 10, 0, 10), L(0, 10, 0, -10) };
        var spots = WidthDimensions.Spots(centerline, boundary, 10, 10, Tol, new[] { 0.72, 0.8, 0.9 });
        Assert.Empty(spots);
        // A tapered strip: the north sideline is not parallel to the centerline anywhere.
        var tapered = new List<Course> { L(0, -10, 200, -10), L(200, -10, 200, 14), L(200, 14, 0, 10), L(0, 10, 0, -10) };
        Assert.Empty(WidthDimensions.Spots(centerline, tapered, 10, 10, Tol, Fractions));
    }

    [Fact]
    public void NoDimensionAtABendInTheCenterline()
    {
        var centerline = new List<Course> { L(0, 0, 100, 0), L(100, 0, 100, 100) };
        var boundary = new List<Course> { L(0, -10, 110, -10), L(110, -10, 110, 100), L(110, 100, 90, 100), L(90, 100, 90, 10), L(90, 10, 0, 10), L(0, 10, 0, -10) };
        var spots = WidthDimensions.Spots(centerline, boundary, 10, 10, Tol, new[] { 0.5, 0.25, 0.75 });
        Assert.DoesNotContain(spots, s => Math.Abs(s.Station - 100) < 10);
        Assert.NotEmpty(spots);
    }

    [Fact]
    public void TheC2PermanentEasementIsDimensionedOnItsFullWidthCourseOnly()
    {
        // Silver Lake 104300 after the lengthwise trim along the section line (baseline geometry report).
        var route = new List<Course> { L(1317996.603, 325778.648, 1318062.932, 325829.343) };
        var boundary = new List<Course>
        {
            L(1318051.138, 325829.769, 1318074.725, 325828.917), L(1318074.725, 325828.917, 1318000.047, 325771.841),
            L(1318000.047, 325771.841, 1317994.195, 325528.109), L(1317994.195, 325528.109, 1317990.498, 325524.337),
            L(1317990.498, 325524.337, 1317996.834, 325788.265), L(1317996.834, 325788.265, 1318051.138, 325829.769)
        };
        var spots = WidthDimensions.Spots(route, boundary, 7.5, 7.5, Tol, new[] { 0.25, 0.5, 0.75, 0.125, 0.375, 0.9 });
        Assert.NotEmpty(spots);
        Assert.All(spots, s => Assert.Equal(15, s.Left.DistanceTo(s.Right), 6));
        Assert.DoesNotContain(spots, s => Math.Abs(s.Station - route[0].Length * 0.9) < 1e-6);     // too near the trimmed end
    }

    // ------------------------------------------------------------------ north arrow

    [Fact]
    public void TheNorthArrowTurnIsMeasuredFromTheBlockGeometry()
    {
        var origin = P(0, 0.3296);
        var arrow = new List<P2> { P(-0.011, 0.599), P(-0.138, 0.205), P(0.167, 0.205), P(0, 0.33) };
        var bar = new List<P2> { P(-0.5, -0.08), P(0.5, -0.08), P(0.5, 0.04) };
        List<P2> Turn(double deg) => arrow.Select(p =>
        {
            var v = p - origin; var a = deg * Math.PI / 180;
            return origin + P(v.X * Math.Cos(a) - v.Y * Math.Sin(a), v.X * Math.Sin(a) + v.Y * Math.Cos(a));
        }).Concat(bar).ToList();
        var before = arrow.Concat(bar).ToList();

        var measured = NorthArrowCheck.MeasuredTurn(before, Turn(-35.5), origin);
        Assert.NotNull(measured);
        Assert.Equal(-35.5, measured!.Value, 6);
        Assert.True(NorthArrowCheck.Matches(measured, -35.5));
        Assert.False(NorthArrowCheck.Matches(measured, 35.5));
        Assert.True(NorthArrowCheck.Matches(NorthArrowCheck.MeasuredTurn(before, Turn(270), origin), -90));

        Assert.Equal(-35.5, NorthArrowCheck.MeasuredTurn(before, Turn(-35.5))!.Value, 6);                  // without knowing the origin
        Assert.Equal(120, NorthArrowCheck.MeasuredTurn(before, Turn(120))!.Value, 6);
        Assert.Null(NorthArrowCheck.MeasuredTurn(before, before));
        Assert.Null(NorthArrowCheck.MeasuredTurn(before, arrow.Select(p => p * 1.5).Concat(bar).ToList()));   // scaled, not turned
        Assert.Null(NorthArrowCheck.MeasuredTurn(before, before, origin));                               // nothing turned
        Assert.Null(NorthArrowCheck.MeasuredTurn(before, before.Select(p => p + P(0.1, 0)).ToList(), origin));   // moved, not turned
    }

    // ------------------------------------------------------------------ hatch scale

    [Fact]
    public void TheHatchScalePrintsTheProfileSpacingAtEachExhibitScale()
    {
        // ANSI31 at pattern scale 15 has lines 1.875' apart: 1/32" at 1" = 60'.
        Assert.Equal(15, HatchScaling.PatternScaleFor(15, 1.875, 0.03125, 60), 9);
        Assert.Equal(5, HatchScaling.PatternScaleFor(15, 1.875, 0.03125, 20), 9);
        Assert.Equal(25, HatchScaling.PatternScaleFor(15, 1.875, 0.03125, 100), 9);

        var x = new ExhibitSettings { HatchSpacingIn = 0.05, HatchSpacings = "ANSI31=0.03125; ANSI3?=0.0977" };
        Assert.Equal(0.03125, x.HatchSpacingFor("ANSI31"));
        Assert.Equal(0.0977, x.HatchSpacingFor("ansi37"));
        Assert.Equal(0.05, x.HatchSpacingFor("AR-CONC"));
        Assert.Equal(0, new ExhibitSettings().HatchSpacingFor("ANSI31"));        // generic: hatches left as drafted
    }

    [Fact]
    public void AHatchScaleSetByHandIsRecordedAndKept()
    {
        Assert.Equal(HatchAction.Set, HatchScaling.Decide(current: 15, lastSetByFtf: 15, byHand: null, target: 5));
        Assert.Equal(HatchAction.Set, HatchScaling.Decide(current: 15, lastSetByFtf: null, byHand: null, target: 5));     // older easements
        Assert.Equal(HatchAction.Leave, HatchScaling.Decide(current: 5, lastSetByFtf: 5, byHand: null, target: 5));
        Assert.Equal(HatchAction.RecordHand, HatchScaling.Decide(current: 8, lastSetByFtf: 15, byHand: null, target: 5));
        Assert.Equal(HatchAction.Leave, HatchScaling.Decide(current: 8, lastSetByFtf: 15, byHand: 8, target: 5));
        Assert.Equal(HatchAction.KeepHand, HatchScaling.Decide(current: 15, lastSetByFtf: 15, byHand: 8, target: 5));   // redrawn: the hand scale goes back
    }

    // ------------------------------------------------------------------ viewport content, stamp, wording

    [Fact]
    public void OverheadPowerAndOtherHatchesFollowTheProfileOrTheExhibitsOwnChoice()
    {
        var generic = new ExhibitSettings();
        Assert.Null(ExhibitSettings.ActionFor(generic.LayerRules(), "V-UTIL-POWR-OVHD-E"));      // neither is hidden by default
        Assert.Equal(ExhibitSettings.User, generic.OverheadPower);
        Assert.Equal(ExhibitSettings.User, generic.OtherHatches);

        var office = new ExhibitSettings { OverheadPower = "Hide", OtherHatches = "Relevant", OtherHatchLayers = "C-PROP-RWAY-PATT*; C-BNDY-LIMT-PATT*", ViewportLayerRules = "*-E=Show" };
        var rules = office.LayerRules();
        Assert.Equal(ExhibitSettings.Hide, ExhibitSettings.ActionFor(rules, "V-UTIL-POWR-OVHD-E"));
        Assert.Equal(ExhibitSettings.Relevant, ExhibitSettings.ActionFor(rules, "C-PROP-RWAY-PATT-1-CULVERT"));
        Assert.Equal(ExhibitSettings.Show, ExhibitSettings.ActionFor(rules, "V-PROP-BNDY-E"));

        var shownHere = office.LayerRules("Show", "User");
        Assert.Equal(ExhibitSettings.Show, ExhibitSettings.ActionFor(shownHere, "V-UTIL-POWR-OVHD-E"));
        Assert.Null(ExhibitSettings.ActionFor(shownHere, "C-PROP-RWAY-PATT-1-CULVERT"));
    }

    [Fact]
    public void OfficeWordingVariationsAreChoicesNotAStandard()
    {
        Assert.Equal(new[] { "LINE NO.", "DISTANCE", "BEARING" }, new ExhibitSettings { LineTableHeadings = "DISTANCE/BEARING" }.LineColumns().Select(c => c.Key));
        Assert.Equal(new[] { "LINE NO.", "LENGTH", "DIRECTION" }, new ExhibitSettings { LineTableHeadings = "LENGTH/DIRECTION" }.LineColumns().Select(c => c.Key));
        Assert.Equal(new[] { "NO.", "BRG" }, new ExhibitSettings { LineTableHeadings = "Custom", LineTableColumns = "NO.={id}|BRG={bearing}" }.LineColumns().Select(c => c.Key));
        Assert.Equal(new[] { "LINE NO.", "DISTANCE", "BEARING" }, new ExhibitSettings().LineColumns().Select(c => c.Key));    // unchanged default

        var x = new ExhibitSettings();
        Assert.Null(x.AreaLine("SEWER", "T", 8447.6));
        x.AreaLabelFormat = ExhibitSettings.AreaWordings[0];
        Assert.Equal("APPROX SEWER EASEMENT AREA = 8,448 SF", x.AreaLine("sewer", "T", 8447.6));
        x.AreaLabelFormat = ExhibitSettings.AreaWordings[1];
        Assert.Equal("APPROX. EASEMENT AREA= 4,181 SF", x.AreaLine("", "T", 4181.03));
        x.AreaLabelFormat = ExhibitSettings.AreaWordings[2];
        Assert.Equal("TEMPORARY CONSTRUCTION EASEMENT (2,103 SQ. FT.)", x.AreaLine("TEMPORARY CONSTRUCTION", "T", 2102.86));
    }

    [Fact]
    public void BadChoicesAreRefusedWhenTheProfileIsSaved()
    {
        var x = new ExhibitSettings { OverheadPower = "Sometimes", OtherHatches = "Maybe", StampMode = "Auto", LineTableHeadings = "FEET", HatchSpacingIn = -1 };
        var problems = new List<string>();
        x.Validate(problems);
        Assert.Equal(5, problems.Count);
        Assert.Equal(new[] { 6.1, 3.2 }, new ExhibitSettings { TableSpots = "6.1,3.2; nonsense; 1,2" }.TableSpotList().Take(1).SelectMany(s => new[] { s.Key, s.Value }));
        Assert.Equal(ExhibitSettings.StampNone, new ExhibitSettings().StampMode);
    }

    // ------------------------------------------------------------------ QA summary

    [Fact]
    public void TheQaSummarySeparatesSurveyContentFromDraftingAndNeverApproves()
    {
        var lines = new List<QaLine>
        {
            new() { Severity = "Error", Category = ExhibitQaReport.LegalReproduction, Item = "FEE ACQUISITION AREA", Value = "the stated courses close at 1:1,712" },
            new() { Severity = "Warning", Category = ExhibitQaReport.Drafting, Item = "LABEL 1", Value = "overlaps the POC leader" },
            new() { Severity = "Warning", Category = ExhibitQaReport.Drafting, Item = "LABEL 2", Value = "overlaps the POB leader" },
            new() { Severity = "", Category = ExhibitQaReport.ManualReview, Item = "Stamp", Value = "placeholder only; the surveyor places the stamp" },
            new() { Severity = "", Category = ExhibitQaReport.SourceData, Item = "TCE", Value = "current with the survey" }
        };
        var text = ExhibitQaReport.Format("EXHIBIT QA -- EXHIBIT 1 OF 1", lines);
        Assert.Contains("NOT READY FOR SURVEYOR REVIEW", text);
        Assert.Contains("Survey content (geometry, source data, legal reproduction): 1 errors, 0 review", text);
        Assert.Contains("Drafting, sheet and manual review: 0 errors, 2 review", text);
        var order = ExhibitQaReport.Categories.Select(c => text.IndexOf("\n" + c + " -- ", StringComparison.Ordinal)).ToList();
        Assert.All(order, i => Assert.True(i > 0));
        Assert.Equal(order.OrderBy(i => i), order);
        Assert.DoesNotContain("APPROVED", text.ToUpperInvariant().Replace("DOES NOT APPROVE", string.Empty));
        Assert.Equal("1 Errors, 2 Review Items, 2 Informational Items", ExhibitQaReport.Counts(lines));
        Assert.Equal("READY FOR SURVEYOR REVIEW", ExhibitQaReport.Verdict(lines.Skip(1)));
    }
}
