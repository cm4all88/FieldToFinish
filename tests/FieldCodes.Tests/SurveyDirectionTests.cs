using FieldCodes.Drafting;

namespace FieldCodes.Tests;

/// <summary>
/// Bearing/azimuth parsing and formatting for FTFDRAWLINE. The parser is strict --
/// anything ambiguous is refused with a message, never guessed -- and the formatter
/// reports what the geometry actually is, with rounding that can never print 60 in
/// a minutes or seconds slot.
/// </summary>
public sealed class SurveyDirectionTests
{
    // ------------------------------------------------------------ bearing parsing

    [Theory]
    [InlineData("N 42 18 36 E", 42.31)]
    [InlineData("N42d18'36\"E", 42.31)]
    [InlineData("n 42°18'36\" e", 42.31)]
    [InlineData("N 42-18-36 E", 42.31)]
    [InlineData("S 10 00 00 E", 170.0)]
    [InlineData("S 45 W", 225.0)]
    [InlineData("N 30 30 W", 329.5)]
    [InlineData("N 90 E", 90.0)]
    [InlineData("N 0 E", 0.0)]
    public void QuadrantBearingsParseToAzimuths(string text, double azimuth)
    {
        var parsed = SurveyDirection.ParseBearing(text);
        Assert.True(parsed.Ok, parsed.Error);
        Assert.Equal(azimuth, parsed.Value, 9);
    }

    // The dot form is packed D.MMSS -- the Civil 3D entry convention -- never
    // decimal degrees. Digits after the dot are positional: minutes, then
    // seconds, then a decimal fraction of the seconds; short entries pad with
    // zeros on the right.

    [Theory]
    [InlineData("N45.2536E", 45, 25, 36.0)]     // 45°25'36"
    [InlineData("n 45.2536 e", 45, 25, 36.0)]
    [InlineData("N45.30E", 45, 30, 0.0)]        // 45°30'00"
    [InlineData("N45.5E", 45, 50, 0.0)]         // .5 pads to 50 minutes
    [InlineData("N45.253612E", 45, 25, 36.12)]  // decimal seconds
    [InlineData("N45.E", 45, 0, 0.0)]           // bare dot: whole degrees
    public void DotFormBearingsArePackedDmss(string text, int deg, int min, double sec)
    {
        var parsed = SurveyDirection.ParseBearing(text);
        Assert.True(parsed.Ok, parsed.Error);
        Assert.Equal(deg + min / 60.0 + sec / 3600.0, parsed.Value, 9);
    }

    [Theory]
    [InlineData("N42.75E")]         // reads as 75 minutes, not a decimal
    [InlineData("N45.2575E")]       // 75 seconds
    [InlineData("N45.2a36E")]       // not digits
    public void MalformedPackedBearingsAreRefused(string text)
    {
        var parsed = SurveyDirection.ParseBearing(text);
        Assert.False(parsed.Ok);
        Assert.False(string.IsNullOrWhiteSpace(parsed.Error));
    }

    [Theory]
    [InlineData("")]                    // nothing
    [InlineData("42 18 36")]            // no quadrant letters
    [InlineData("N 95 E")]              // over 90
    [InlineData("N 42 75 E")]           // minutes over 60
    [InlineData("N 42 18 76 E")]        // seconds over 60
    [InlineData("E 42 N")]              // letters swapped
    [InlineData("N 42.5 18 E")]         // decimals not on the last component
    [InlineData("N E")]                 // quadrants with no angle
    [InlineData("N 1 2 3 4 E")]         // too many components
    public void MalformedBearingsAreRefusedWithAMessage(string text)
    {
        var parsed = SurveyDirection.ParseBearing(text);
        Assert.False(parsed.Ok);
        Assert.False(string.IsNullOrWhiteSpace(parsed.Error));
    }

    // ------------------------------------------------------------ azimuth parsing

    [Theory]
    [InlineData("215.3000", 215.5)]     // packed D.MMSS: 215°30'00"
    [InlineData("215.5", 215.8333333333333333)]  // .5 pads to 50 minutes
    [InlineData("215 30 00", 215.5)]
    [InlineData("215°30'00\"", 215.5)]
    [InlineData("0", 0.0)]
    [InlineData("360", 0.0)]            // folded to zero
    public void AzimuthsParse(string text, double azimuth)
    {
        var parsed = SurveyDirection.ParseAzimuth(text);
        Assert.True(parsed.Ok, parsed.Error);
        Assert.Equal(azimuth, parsed.Value, 9);
    }

    [Theory]
    [InlineData("")]
    [InlineData("361")]
    [InlineData("-5")]
    [InlineData("abc")]
    [InlineData("215.60")]              // packed: 60 minutes is refused
    [InlineData("-5.5")]                // packed forms cannot be negative
    public void MalformedAzimuthsAreRefused(string text)
    {
        var parsed = SurveyDirection.ParseAzimuth(text);
        Assert.False(parsed.Ok);
        Assert.False(string.IsNullOrWhiteSpace(parsed.Error));
    }

    // ----------------------------------------------------------- distance parsing

    [Theory]
    [InlineData("184.27", 184.27)]
    [InlineData("184.27'", 184.27)]     // foot symbol tolerated
    [InlineData("184.27 ft", 184.27)]
    public void DistancesParse(string text, double feet)
    {
        var parsed = SurveyDirection.ParseDistance(text);
        Assert.True(parsed.Ok, parsed.Error);
        Assert.Equal(feet, parsed.Value, 9);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]                   // a zero-length course is refused
    [InlineData("-5")]
    [InlineData("abc")]
    public void MalformedDistancesAreRefused(string text)
    {
        var parsed = SurveyDirection.ParseDistance(text);
        Assert.False(parsed.Ok);
    }

    // -------------------------------------------------------------- from geometry

    [Theory]
    [InlineData(1.0, 0.0, 90.0)]        // due east
    [InlineData(0.0, 1.0, 0.0)]         // due north
    [InlineData(-1.0, 0.0, 270.0)]      // due west
    [InlineData(0.0, -1.0, 180.0)]      // due south
    [InlineData(1.0, 1.0, 45.0)]
    public void AzimuthFromVectorIsClockwiseFromNorth(double dx, double dy, double azimuth)
    {
        Assert.Equal(azimuth, SurveyDirection.AzimuthFromVector(dx, dy), 9);
    }

    [Fact]
    public void ATypedCourseRoundTripsThroughGeometry()
    {
        // What FTFDRAWLINE does: parse the entry, build the vector, then annotate
        // from the vector. The reported bearing must be the entry.
        var parsed = SurveyDirection.ParseBearing("N 42 18 36 E");
        Assert.True(parsed.Ok);

        var radians = FieldCodes.Geometry.Angles.ToRadians(parsed.Value);
        var dx = 184.27 * Math.Sin(radians);
        var dy = 184.27 * Math.Cos(radians);

        var reported = SurveyDirection.AzimuthFromVector(dx, dy);
        Assert.Equal("N 42°18'36\" E", SurveyDirection.FormatBearing(reported, 0, "°"));
    }

    // ---------------------------------------------------------------- formatting

    [Theory]
    [InlineData(42.31, "N 42°18'36\" E")]
    [InlineData(170.0, "S 10°00'00\" E")]
    [InlineData(225.0, "S 45°00'00\" W")]
    [InlineData(329.5, "N 30°30'00\" W")]
    [InlineData(0.0, "N 00°00'00\" E")]
    [InlineData(90.0, "N 90°00'00\" E")]
    [InlineData(180.0, "S 00°00'00\" E")]
    [InlineData(270.0, "N 90°00'00\" W")]
    [InlineData(269.99999, "N 90°00'00\" W")]
    [InlineData(5.5, "N 05°30'00\" E")]
    public void BearingsFormatAsQuadrants(double azimuth, string expected)
    {
        Assert.Equal(expected, SurveyDirection.FormatBearing(azimuth, 0, "°"));
    }

    [Fact]
    public void RoundingCarriesInsteadOfPrintingSixty()
    {
        // 41°59'59.7" at zero decimals: N 42°00'00" E, never N 41°59'60" E.
        var azimuth = 41.0 + 59.0 / 60.0 + 59.7 / 3600.0;
        Assert.Equal("N 42°00'00\" E", SurveyDirection.FormatBearing(azimuth, 0, "°"));
    }

    [Fact]
    public void SecondsDecimalsAreConfigurable()
    {
        var azimuth = 42.0 + 18.0 / 60.0 + 36.55 / 3600.0;
        Assert.Equal("N 42°18'36.6\" E", SurveyDirection.FormatBearing(azimuth, 1, "°"));
    }

    [Theory]
    [InlineData(215.5, "215°30'00\"")]
    [InlineData(0.0, "0°00'00\"")]
    [InlineData(42.31, "42°18'36\"")]
    public void AzimuthsFormatWholeCircle(double azimuth, string expected)
    {
        Assert.Equal(expected, SurveyDirection.FormatAzimuth(azimuth, 0, "°"));
    }

    [Fact]
    public void AzimuthRoundingNeverReportsThreeSixty()
    {
        // 359°59'59.7" at zero decimals carries to a full circle: report 0°.
        var azimuth = 359.0 + 59.0 / 60.0 + 59.7 / 3600.0;
        Assert.Equal("0°00'00\"", SurveyDirection.FormatAzimuth(azimuth, 0, "°"));
    }

    [Fact]
    public void TheDegreeSymbolIsCallerSupplied()
    {
        // DBText renders %%d as the degree symbol; the formatter does not care.
        Assert.Equal("N 45%%d00'00\" E", SurveyDirection.FormatBearing(45.0, 0, "%%d"));
    }

    [Theory]
    [InlineData(184.266, 2, true, "184.27'")]
    [InlineData(184.266, 2, false, "184.27")]
    [InlineData(184.266, 0, true, "184'")]
    [InlineData(184.2, 3, true, "184.200'")]
    public void DistancesFormatPerTheStandard(double feet, int decimals, bool foot,
                                              string expected)
    {
        Assert.Equal(expected, SurveyDirection.FormatDistance(feet, decimals, foot));
    }
}
