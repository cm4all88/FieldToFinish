using FieldCodes.RecordSurvey;

namespace FieldCodes.Tests;

/// <summary>
/// Reading survey calls out of OCR text: the bearing spellings recorded plats use, distances in
/// every unit a Washington record can carry, degree-minute-second angles, and the refusals -- an
/// impossible value is never folded into a plausible one.
/// </summary>
public sealed class RecordSurveyParsingTests
{
    private static double Dms(int d, int m, double s) => d + m / 60.0 + s / 3600.0;

    // ---------------------------------------------------------------- quadrant bearings

    [Theory]
    [InlineData("N 89°42'18\" E", 89, 42, 18, 'N', 'E')]
    [InlineData("N89°42'18\"E", 89, 42, 18, 'N', 'E')]
    [InlineData("N 89 42 18 E", 89, 42, 18, 'N', 'E')]
    [InlineData("N 89-42-18 E", 89, 42, 18, 'N', 'E')]
    [InlineData("N89d42'18\"E", 89, 42, 18, 'N', 'E')]
    [InlineData("S 00°17'42\" E", 0, 17, 42, 'S', 'E')]
    [InlineData("S 89°13'28\" W", 89, 13, 28, 'S', 'W')]
    [InlineData("N 30°30' W", 30, 30, 0, 'N', 'W')]
    [InlineData("N 45° E", 45, 0, 0, 'N', 'E')]
    [InlineData("NORTH 89°42'18\" EAST", 89, 42, 18, 'N', 'E')]
    [InlineData("n 89º42’18” e", 89, 42, 18, 'N', 'E')]        // ordinal-o degree, curly quotes
    [InlineData("N 89*42'18\" E", 89, 42, 18, 'N', 'E')]        // asterisk for the degree sign
    [InlineData("S 12°34'56.5\" W", 12, 34, 56.5, 'S', 'W')]    // decimal seconds
    public void QuadrantBearingsInEveryRecordedSpellingParse(string text, int d, int m, double s, char ns, char ew)
    {
        var read = SurveyCallParser.ParseBearing(text);
        Assert.True(read.Ok, read.Error);
        var angle = Dms(d, m, s);
        var expected = ns == 'N' ? (ew == 'E' ? angle : 360 - angle) : (ew == 'E' ? 180 - angle : 180 + angle);
        Assert.Equal(expected % 360, read.Value, 9);
        Assert.True(read.Confidence > 0.99);
    }

    [Fact]
    public void ParsedBearingsAgreeWithTheDraftingParser()
    {
        // The two parsers must never disagree on a clean bearing: FTFDRAWLINE and FTFRECORD share one convention.
        var record = SurveyCallParser.ParseBearing("S 45°30'00\" W");
        var drafting = FieldCodes.Drafting.SurveyDirection.ParseBearing("S 45 30 00 W");
        Assert.Equal(drafting.Value, record.Value, 9);
        Assert.Equal("S 45°30'00\" W", record.Normalized);
    }

    [Theory]
    [InlineData("DUE NORTH", 0.0)]
    [InlineData("DUE EAST", 90.0)]
    [InlineData("due south", 180.0)]
    [InlineData("DUE WEST", 270.0)]
    public void DueDirectionsParse(string text, double azimuth)
    {
        var read = SurveyCallParser.ParseBearing(text);
        Assert.True(read.Ok);
        Assert.Equal(azimuth, read.Value, 9);
    }

    [Fact]
    public void LookAlikeCharactersAreRepairedAndTheConfidenceSaysSo()
    {
        // OCR reads zeros as the letter O and ones as l; a bearing slot can only hold digits.
        var read = SurveyCallParser.ParseBearing("N 45°OO'l2\" W");
        Assert.True(read.Ok, read.Error);
        Assert.Equal(360 - Dms(45, 0, 12), read.Value, 9);
        Assert.True(read.Confidence < 1.0);
        Assert.Contains(read.Notes, n => n.Contains("repaired"));
    }

    [Theory]
    [InlineData("N 95°00'00\" E")]      // over 90: a misread digit, not a bearing
    [InlineData("N 89°72'18\" E")]      // 72 minutes
    [InlineData("N 89°42'78\" E")]      // 78 seconds
    [InlineData("N 189°42'18\" E")]     // three digits
    [InlineData("N 90°10'00\" E")]      // 90 with minutes
    [InlineData("89°42'18\"")]          // no quadrant letters: an angle, not a bearing
    [InlineData("")]
    [InlineData("LOT 7")]
    public void ImpossibleBearingsAreRefusedNotFolded(string text)
    {
        var read = SurveyCallParser.ParseBearing(text);
        Assert.False(read.Ok);
        Assert.False(string.IsNullOrWhiteSpace(read.Error));
    }

    // ---------------------------------------------------------------- angles

    [Theory]
    [InlineData("45°12'10\"", 45, 12, 10)]
    [InlineData("Δ=45°12'10\"", 45, 12, 10)]
    [InlineData("22°55'06\"", 22, 55, 6)]
    [InlineData("45 12 10", 45, 12, 10)]
    [InlineData("180°", 180, 0, 0)]
    public void DegreeMinuteSecondAnglesParse(string text, int d, int m, int s)
    {
        var read = SurveyCallParser.ParseAngle(text);
        Assert.True(read.Ok, read.Error);
        Assert.Equal(Dms(d, m, s), read.Value, 9);
    }

    [Theory]
    [InlineData("45°60'00\"")]
    [InlineData("45°12'60\"")]
    [InlineData("361°00'00\"")]
    [InlineData("R=250.00'")]
    public void MalformedAnglesAreRefused(string text)
    {
        Assert.False(SurveyCallParser.ParseAngle(text).Ok);
    }

    // ---------------------------------------------------------------- distances

    [Theory]
    [InlineData("1320.45'", 1320.45, "ft")]
    [InlineData("1,320.45'", 1320.45, "ft")]
    [InlineData("1320.45 FT", 1320.45, "ft")]
    [InlineData("1320.45 FEET", 1320.45, "ft")]
    [InlineData("148.52", 148.52, "")]
    [InlineData("20.00 CH", 1320.0, "ch")]
    [InlineData("4 RODS", 66.0, "rd")]
    public void DistancesParseInFeetChainsAndRods(string text, double feet, string unit)
    {
        var read = SurveyCallParser.ParseDistance(text);
        Assert.True(read.Ok, read.Error);
        Assert.Equal(feet, read.Value, 6);
        Assert.Equal(unit, read.Unit);
    }

    [Fact]
    public void MetresConvertAtTheUsSurveyFootAndSaySo()
    {
        var read = SurveyCallParser.ParseDistance("402.44 M");
        Assert.True(read.Ok);
        Assert.Equal(402.44 * 3937.0 / 1200.0, read.Value, 6);
        Assert.Equal("m", read.Unit);
        Assert.Contains(read.Notes, n => n.Contains("metres"));
    }

    [Fact]
    public void ABareNumberReadsAsFeetAtLowerConfidence()
    {
        var bare = SurveyCallParser.ParseDistance("148.52");
        var marked = SurveyCallParser.ParseDistance("148.52'");
        Assert.True(bare.Ok && marked.Ok);
        Assert.True(bare.Confidence < marked.Confidence);
        Assert.Contains(bare.Notes, n => n.Contains("No unit"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("0'")]
    [InlineData("LOT")]
    public void MalformedDistancesAreRefused(string text)
    {
        Assert.False(SurveyCallParser.ParseDistance(text).Ok);
    }

    // ---------------------------------------------------------------- tokens

    [Fact]
    public void ABearingAndDistanceLineTokenizesIntoBothWithNoLeftovers()
    {
        var tokens = SurveyCallParser.Tokenize("N 89°42'18\" E 1320.45'");
        Assert.Equal(2, tokens.Count);
        Assert.Equal(SurveyTokenKind.Bearing, tokens[0].Kind);
        Assert.Equal(SurveyTokenKind.Distance, tokens[1].Kind);
        Assert.Equal(1320.45, tokens[1].Read!.Value, 6);
    }

    [Fact]
    public void RecordAndMeasuredTagsTokenize()
    {
        var tokens = SurveyCallParser.Tokenize("N 89°42'18\" E 1320.45' (R1)  N 89°42'21\" E 1320.38' (M)");
        var tags = tokens.Where(t => t.Kind == SurveyTokenKind.Tag).Select(t => t.Name).ToList();
        Assert.Equal(new[] { "R1", "M" }, tags);
        Assert.Equal(2, tokens.Count(t => t.Kind == SurveyTokenKind.Bearing));
        Assert.Equal(2, tokens.Count(t => t.Kind == SurveyTokenKind.Distance));
    }

    [Theory]
    [InlineData("(R&M)", "R&M")]
    [InlineData("(R1 & M)", "R1&M")]
    [InlineData("(MEAS)", "M")]
    [InlineData("(REC)", "R")]
    [InlineData("(CALC)", "C")]
    [InlineData("(P)", "P")]
    [InlineData("R2:", "R2")]
    public void TagSpellingsNormalise(string text, string expected)
    {
        var tag = SurveyCallParser.Tokenize(text + " 10.00'").First(t => t.Kind == SurveyTokenKind.Tag);
        Assert.Equal(expected, tag.Name);
    }

    [Fact]
    public void CurveKeysTokenizeWithTheirValues()
    {
        var tokens = SurveyCallParser.Tokenize("R=250.00' L=100.00' Δ=22°55'06\" CB=S 78°50'09\" E CH=99.33' T=50.66'");
        var keys = tokens.Where(t => t.Kind == SurveyTokenKind.CurveKey).Select(t => t.Name).ToList();
        Assert.Equal(new[] { "R", "L", "DELTA", "CB", "CH", "T" }, keys);
        Assert.Equal(4, tokens.Count(t => t.Kind == SurveyTokenKind.Distance));
        Assert.Single(tokens.Where(t => t.Kind == SurveyTokenKind.Angle));
        Assert.Single(tokens.Where(t => t.Kind == SurveyTokenKind.Bearing));
    }

    [Fact]
    public void CurveAndLineTagsTokenize()
    {
        var tokens = SurveyCallParser.Tokenize("C1 250.00' 22°55'06\" 100.00'");
        Assert.Equal("C1", tokens.First(t => t.Kind == SurveyTokenKind.CurveTag).Name);
        var line = SurveyCallParser.Tokenize("L12 N 45°00'00\" E 25.00'");
        Assert.Equal("L12", line.First(t => t.Kind == SurveyTokenKind.LineTag).Name);
    }

    [Fact]
    public void WordsThatAreNotCallsStayWords()
    {
        var tokens = SurveyCallParser.Tokenize("FOUND 1/2\" REBAR & CAP LS 12345");
        Assert.DoesNotContain(tokens, t => t.Kind == SurveyTokenKind.Bearing);
        Assert.Contains(tokens, t => t.Kind == SurveyTokenKind.Word && t.Text == "REBAR");
    }

    // ---------------------------------------------------------------- alternatives

    [Fact]
    public void DigitAlternativesOfferTheClassicConfusionsDecimalsFirst()
    {
        var alts = SurveyCallParser.DigitAlternatives("148.52", 0.7, 2);
        Assert.Equal(2, alts.Count);
        Assert.Equal("148.82", alts[0].Text);           // 5 in the tenths place, 5/6? no: 5->3 or 5->6 -- first pair listed with 5 is 5/6
        Assert.All(alts, a => Assert.True(a.Confidence < 0.7));
        Assert.All(alts, a => Assert.Contains("digit confusion", a.Reason));
    }

    [Fact]
    public void DigitAlternativesNeverExceedTheRequestedCount()
    {
        Assert.Empty(SurveyCallParser.DigitAlternatives("148.52", 0.7, 0));
        Assert.True(SurveyCallParser.DigitAlternatives("1358.06", 0.5, 3).Count <= 3);
    }
}

/// <summary>
/// What tesseract actually produced from 1950s and 60s King County plats (typed and hand-lettered),
/// and what the reader must do with it: read by position when the marks are wrong, refuse when the
/// digits are, and never turn noise into a call.
/// </summary>
public sealed class RecordSurveyOcrNoiseTests
{
    [Theory]
    [InlineData("S 89° 43° 14° EF", 180 - (89 + 43 / 60.0 + 14 / 3600.0))]     // marks all read as degree signs, E read as EF
    [InlineData("S$ 88° 26 42\" F", 180 - (88 + 26 / 60.0 + 42 / 3600.0))]     // stray $, no minute mark, F for E
    [InlineData("N O° 47\"W", 360 - 47 / 60.0)]                                 // O for 0, second mark on the minutes
    [InlineData("589° 33 Ww", 180 + 89 + 33 / 60.0)]                            // 5 for S, doubled W
    [InlineData("N 89°43'/4\"W", 360 - (89 + 43 / 60.0 + 14 / 3600.0))]        // slash for a 1 in the seconds
    [InlineData("S 1° 22'58\" pW", 180 + 1 + 22 / 60.0 + 58 / 3600.0)]          // stray p between the seconds mark and W
    [InlineData("N 88° 37'02\"\"pw", 360 - (88 + 37 / 60.0 + 2 / 3600.0))]      // doubled mark and a stray p
    [InlineData("N 4°§3'45\"E", 4 + 53 / 60.0 + 45 / 3600.0)]                   // section sign for a 5
    [InlineData("NB7°OS W", 360 - (87 + 5 / 60.0))]                             // B for 8, OS for 05
    [InlineData("$ 68°24 29\"E", 180 - (68 + 24 / 60.0 + 29 / 3600.0))]
    public void OcrDamagedBearingsAreReadByPositionAtLowerConfidence(string text, double azimuth)
    {
        var read = SurveyCallParser.ParseBearing(text);
        Assert.True(read.Ok, read.Error);
        Assert.Equal(azimuth, read.Value, 9);
        Assert.True(read.Confidence < 0.95, "confidence " + read.Confidence);
        Assert.NotEmpty(read.Notes);
    }

    [Theory]
    [InlineData("N 1° 38 BIE")]         // BI -> 81 seconds: refused, not folded
    [InlineData("NEBS°OZ W")]           // no digits at all
    [InlineData("N@s*327w")]
    [InlineData("NE. 1/4 of NE. 1/4")]
    public void NoiseIsRefusedNotRead(string text)
    {
        Assert.False(SurveyCallParser.ParseBearing(text).Ok);
    }

    [Fact]
    public void ADroppedMinuteMarkDoesNotTruncateTheAngle()
    {
        var read = SurveyCallParser.ParseAngle("50°23");
        Assert.True(read.Ok);
        Assert.Equal(50 + 23 / 60.0, read.Value, 9);
        var withMark = SurveyCallParser.ParseAngle("50°23'");
        Assert.Equal(50 + 23 / 60.0, withMark.Value, 9);
        Assert.Equal(1.0, withMark.Confidence);
        // Marks in the wrong places do cost confidence.
        var wrong = SurveyCallParser.ParseAngle("50\"23'");
        Assert.False(wrong.Ok);                                     // no degree sign: not an angle at all
        var swapped = SurveyCallParser.ParseBearing("N 50° 23° 10° E");
        Assert.True(swapped.Ok);
        Assert.True(swapped.Confidence < 1.0);
    }

    [Theory]
    [InlineData("4=26°58 06", "DELTA", 26 + 58 / 60.0 + 6 / 3600.0)]     // tesseract's Δ
    [InlineData("A 50°23", "A", 50 + 23 / 60.0)]
    [InlineData("Δ 41°53'", "DELTA", 41 + 53 / 60.0)]
    public void SpacedAndMisreadCurveKeysTokenize(string text, string key, double angle)
    {
        var tokens = SurveyCallParser.Tokenize(text);
        Assert.Equal(key, tokens.First(t => t.Kind == SurveyTokenKind.CurveKey).Name);
        Assert.Equal(angle, tokens.First(t => t.Kind == SurveyTokenKind.Angle).Read!.Value, 9);
    }

    [Theory]
    [InlineData("R 573.69'", "R", 573.69)]
    [InlineData("T 326.67'", "T", 326.67)]
    [InlineData("L 705.05'", "L", 705.05)]
    [InlineData("470' RAD.", "R", 470.0)]
    public void OlderPlatsWriteCurveElementsWithoutAnEqualsSign(string text, string key, double value)
    {
        var tokens = SurveyCallParser.Tokenize(text);
        Assert.Equal(key, tokens.First(t => t.Kind == SurveyTokenKind.CurveKey).Name);
        Assert.Equal(value, tokens.First(t => t.Kind == SurveyTokenKind.Distance).Read!.Value, 6);
        Assert.Equal(value, SurveyCallParser.ParseDistance(text).Value, 6);
    }

    [Fact]
    public void ALetterAloneIsNotADistance()
    {
        Assert.False(SurveyCallParser.ParseDistance("L").Ok);
        Assert.False(SurveyCallParser.ParseDistance("LOT").Ok);
        // "LOT 4" is a lot number: the classifier's lot rule runs before any distance is considered.
        Assert.Equal(SurveyEntityKind.LotNumber, EntityClassifier.Classify(new DocumentLine("LOT 4", new PageBox(0, 0, 60, 20), 0.9), 1).Kind);
    }

    [Theory]
    [InlineData("SCALE: 1\" = 50'", "50")]
    [InlineData("SCALE 1 INCH = 100 FEET", "100")]
    [InlineData("Scale: 1\"=200'", "200")]
    [InlineData("1\" = 60'", "60")]
    public void ScaleNotesInEverySpellingClassify(string text, string feet)
    {
        var c = EntityClassifier.Classify(new DocumentLine(text, new PageBox(0, 0, 100, 20), 0.9), 1);
        Assert.Equal(SurveyEntityKind.Scale, c.Kind);
        Assert.Equal(feet, c.Key);
    }

    [Fact]
    public void ABareNumberIsNeitherADistanceNorALotUntilTheAssemblyDecides()
    {
        var c = EntityClassifier.Classify(new DocumentLine("164", new PageBox(0, 0, 40, 20), 0.9), 1);
        Assert.Equal(SurveyEntityKind.Number, c.Kind);
        Assert.Equal("164", c.Key);
        var junk = EntityClassifier.Classify(new DocumentLine("6 a'°cR", new PageBox(0, 0, 40, 20), 0.9), 1);
        Assert.NotEqual(SurveyEntityKind.Distance, junk.Kind);
    }

    [Fact]
    public void ABareNumberInsideAClosedLoopNamesTheLot()
    {
        // A 100' square at 1"=50' (6 px/ft), corners (600,900) (600,300) (1200,300) (1200,900), lot number "7" in the middle.
        DocumentLine L(string t, double x, double y, double rot = 0) => new DocumentLine(t, new PageBox(x, y, t.Length * 14, 28, rot), 0.95);
        var d = new DocumentText();
        var page = new DocumentPage { Number = 1, WidthPx = 2550, HeightPx = 3300, Dpi = 300 };
        page.Lines.AddRange(new[]
        {
            L("N 00°00'00\" E 100.00'", 600 - 154 - 40, 600 - 14, 90), L("N 90°00'00\" E 100.00'", 900 - 154, 300 - 14 - 40),
            L("S 00°00'00\" E 100.00'", 1200 - 154 + 40, 600 - 14, -90), L("S 90°00'00\" W 100.00'", 900 - 154, 900 - 14 + 40),
            L("7", 900 - 7, 600 - 14)
        });
        d.Pages.Add(page);
        var p = CallExtractor.Extract(d, new ExtractionOptions());
        Assert.Contains(p.Annotations, a => a.Kind == SurveyEntityKind.Number && a.Text == "7");
        var a = TraverseAssembler.Assemble(p, new AssemblyOptions { ScaleFeetPerInch = 50, Dpi = 300 });
        var loop = Assert.Single(a.Figures);
        Assert.True(loop.Closed);
        Assert.Equal("Lot 7", loop.Name);
    }

    [Fact]
    public void OlderPlatTitlesAndDedicationsGiveTheSurveyType()
    {
        var lines = new[]
        {
            EntityClassifier.Classify(new DocumentLine("This plat of \"WALDHEIM ACRES\" Addition to King County", new PageBox(0, 0, 500, 20), 0.9), 1),
            EntityClassifier.Classify(new DocumentLine("DEDICATION", new PageBox(0, 0, 100, 40), 0.9), 1)
        };
        Assert.Equal("Subdivision Plat", EntityClassifier.SurveyTypeFrom(lines));
    }

    [Theory]
    [InlineData("/4", "14", 1)]
    [InlineData("§3", "53", 1)]
    [InlineData("OO", "00", 2)]
    [InlineData("1320.45", "1320.45", 0)]
    [InlineData("LOT", "LOT", 0)]
    public void DigitRepairCoversEveryLookAlikeAndLeavesWordsAlone(string token, string expected, int repairs)
    {
        int n;
        Assert.Equal(expected, SurveyCallParser.RepairDigits(token, out n));
        Assert.Equal(repairs, n);
    }

    [Fact]
    public void ABearingWithImpossibleSecondsIsRefusedWithTheReason()
    {
        var r = SurveyCallParser.ParseBearing("S 1°38'8I\" E");
        Assert.False(r.Ok);
        Assert.Contains("Seconds must be under 60", r.Error);
        Assert.Contains("81", r.Error);
    }

    [Fact]
    public void ASlashInTheDecimalsIsAOneButAFractionIsNotADistance()
    {
        var d = SurveyCallParser.ParseDistance("634.3/'");
        Assert.True(d.Ok);
        Assert.Equal(634.31, d.Value, 6);
        Assert.Equal("ft", d.Unit);

        Assert.DoesNotContain(SurveyCallParser.Tokenize("NE 1/4 of the NE 1/4"), t => t.Kind == SurveyTokenKind.Distance);
        Assert.DoesNotContain(SurveyCallParser.Tokenize("FOUND 1/2\" REBAR"), t => t.Kind == SurveyTokenKind.Distance);
        Assert.Contains(SurveyCallParser.Tokenize("N 45°00'00\" E 1320.45'"), t => t.Kind == SurveyTokenKind.Distance && t.Read.Ok && Math.Abs(t.Read.Value - 1320.45) < 1e-9);
    }
}
