using FieldCodes;

namespace FieldCodes.Tests;

/// <summary>
/// A crew that shoots a pole as "PP" and writes "1234 PSE XFMR" in the data collector's note
/// field gets a point whose raw description is just "PP"; the note lands in a point property or
/// the full description. FTFLABELALL labelled those poles "PP". The note is now read as if it had
/// been typed after the code -- only for codes that take notes, so a tree is never changed by a
/// stray remark.
/// </summary>
public sealed class SeparateNoteTests : IClassFixture<RulesFixture>
{
    private readonly RulesFixture _fx;

    public SeparateNoteTests(RulesFixture fx) => _fx = fx;

    private ParsedPoint Parse(string raw, string? note) => _fx.Parser.Parse("1", raw, note);

    // ----------------------------------------------------------------- the parser

    [Fact]
    public void ABarePoleWithItsNoteElsewhereGetsThePoleLabelNotItsCode()
    {
        Assert.Equal("PP", _fx.Parse("PP").LabelText);    // what FTFLABELALL drew before

        var p = Parse("PP", "1234 PSE XFMR TEL");
        Assert.False(p.HasErrors);
        Assert.Equal("pole", p.RuleId);
        Assert.Equal("POLE 1234 PSE\\PW/ XFMR, TEL", p.LabelText);
        Assert.Equal("1234 PSE XFMR TEL", p.Note);
        Assert.Equal("PP 1234 PSE XFMR TEL", p.RawDescription);
    }

    [Fact]
    public void ANoteThatRepeatsTheCodeIsReadOnce()
    {
        Assert.Equal("POLE 1234 PSE", Parse("PP", "PP 1234 PSE").LabelText);
        Assert.Equal("POLE 1234 PSE", Parse("pp", "pp 1234 pse").LabelText);
    }

    [Theory]
    [InlineData("STOP", "STOP SIGN")]
    [InlineData("NP WP", "NO PARKING SIGN (WOOD POST)")]
    [InlineData("135 STOP", "STOP SIGN")]
    public void ASignWithItsWordsInTheNoteIsLabelledWithThem(string note, string label)
    {
        var p = Parse("SN", note);
        Assert.Equal("sn-sign", p.RuleId);
        Assert.Equal(label, p.LabelText);
    }

    [Fact]
    public void ASignShotWithoutAnAzimuthIsLabelledAndLeftUnrotated()
    {
        var p = _fx.Parse("SN STOP");
        Assert.False(p.HasErrors);
        Assert.Equal("STOP SIGN", p.LabelText);
        Assert.Null(p.RotationDegrees);

        Assert.NotNull(_fx.Parse("SN 135 STOP").RotationDegrees);   // an azimuth still rotates
        Assert.Equal("SN", _fx.Parse("SN").LabelText);               // a bare SN is unchanged
    }

    [Fact]
    public void ACodeLabelledWithItsCodeIsLabelledWithTheNoteWhenNoRuleReadsIt()
    {
        var p = Parse("CB", "TYPE 1 RIM 245.3");
        Assert.Equal("symbol-label", p.RuleId);
        Assert.Equal("TYPE 1 RIM 245.3", p.LabelText);
        Assert.Equal("V-UTIL-STRM-TEXT", p.LabelLayer);
        Assert.Equal("TYPE 1 RIM 245.3", p.Note);
        Assert.Equal("CB TYPE 1 RIM 245.3", p.RawDescription);
        Assert.Equal(new[] { "TYPE 1 RIM 245.3" }, p.LabelLines);
    }

    [Fact]
    public void APoleWithItsNumberTypedAndTheRestInTheNoteReadsBoth()
    {
        Assert.Equal("POLE 1234 PSE\\PW/ XFMR", Parse("PP 1234", "PSE XFMR").LabelText);
    }

    [Fact]
    public void ATreeIgnoresTheNote()
    {
        var plain = _fx.Parse("CON 18 . 25");
        var noted = Parse("CON 18 . 25", "LEANING");
        Assert.False(plain.HasErrors);
        Assert.Equal(plain.LabelText, noted.LabelText);
        Assert.False(noted.HasErrors);
        Assert.Null(noted.Note);
    }

    [Fact]
    public void NoNoteOrAnEmptyOneChangesNothing()
    {
        foreach (var note in new[] { null, "", "   ", "PP" })
        {
            var p = Parse("PP", note);
            Assert.Equal("PP", p.LabelText);
            Assert.Null(p.Note);
        }
    }

    [Fact]
    public void ABareCodeNobodyConfiguredStaysSilent()
    {
        var p = Parse("ZZQ", "SOMETHING");
        Assert.True(p.NoContent);
        Assert.False(p.Unhandled);
        Assert.Null(p.LabelText);
    }

    [Fact]
    public void LineworkAndIgnoredCodesAreNotTouched()
    {
        var plain = _fx.Parse("ASPH");
        var noted = Parse("ASPH", "EDGE");
        Assert.Equal(plain.Ignored, noted.Ignored);
        Assert.Equal(plain.NoContent, noted.NoContent);
        Assert.Null(noted.LabelText);
    }

    // ----------------------------------------------------------------- where the note is found

    private static PointRecord Point(string raw, string? full = null, string? format = null,
                                     params (string Name, string Value)[] properties)
    {
        var p = new PointRecord { Number = "1", RawDescription = raw, FullDescription = full ?? raw, DescriptionFormat = format ?? "$*" };
        foreach (var (name, value) in properties) p.Properties.Add(new KeyValuePair<string, string>(name, value));
        return p;
    }

    private static string? NoteOf(FieldNoteReader reader, PointRecord p) => reader.NoteFor(p, out _);

    [Theory]
    [InlineData("Note")]
    [InlineData("NOTES")]
    [InlineData("Field Note")]
    [InlineData("Comment")]
    [InlineData("Remarks")]
    [InlineData("Description 1")]
    [InlineData("DESC")]
    public void ANoteLikePointPropertyIsRead(string name)
    {
        var p = Point("PP", properties: (name, "1234 PSE"));
        var reader = new FieldNoteReader(FieldNoteReader.Automatic, new[] { p });
        Assert.Equal("1234 PSE", reader.NoteFor(p, out var from));
        Assert.Equal("property " + name, from);
    }

    [Fact]
    public void PropertiesThatAreNotNotesAreNotRead()
    {
        var p = Point("PP", properties: ("Elevation Source", "GPS"));
        Assert.Null(NoteOf(new FieldNoteReader("", new[] { p }), p));
    }

    [Fact]
    public void SeveralNotePropertiesAreReadInOrder()
    {
        var p = Point("PP", properties: new[] { ("Description 1", "1234"), ("Description 2", "PSE XFMR") });
        Assert.Equal("1234 PSE XFMR", NoteOf(new FieldNoteReader("", new[] { p }), p));
    }

    [Fact]
    public void AFullDescriptionThatAddsToTheCodeIsTheNote()
    {
        var p = Point("PP", full: "PP 1234 PSE", format: "PP 1234 PSE");
        var reader = new FieldNoteReader("", new[] { p });
        Assert.Equal("1234 PSE", reader.NoteFor(p, out var from));
        Assert.Equal("Full Description", from);
    }

    [Fact]
    public void AFullDescriptionThatVariesPointToPointIsTheNote()
    {
        var a = Point("SN", full: "STOP", format: "STOP");
        var b = Point("SN", full: "YIELD", format: "YIELD");
        var reader = new FieldNoteReader("", new[] { a, b });
        Assert.Equal("STOP", NoteOf(reader, a));
        Assert.Equal("YIELD", NoteOf(reader, b));
    }

    [Fact]
    public void ADescriptionKeysFixedWordingIsNotANote()
    {
        // Every PP reads POWER POLE: that is a description key's wording, not a crew note.
        var a = Point("PP", full: "POWER POLE", format: "POWER POLE");
        var b = Point("PP", full: "POWER POLE", format: "POWER POLE");
        var reader = new FieldNoteReader("", new[] { a, b });
        Assert.Null(NoteOf(reader, a));

        // A format built from the code's own parameters never holds a note either.
        var c = Point("CB", full: "CATCH BASIN CB", format: "CATCH BASIN $0");
        Assert.Null(NoteOf(new FieldNoteReader("", new[] { c }), c));
    }

    [Fact]
    public void AFullDescriptionIsNotReadWhenTheNoteIsAlreadyTypedAfterTheCode()
    {
        var p = Point("PP 1234", full: "POLE", format: "POLE");
        Assert.Null(NoteOf(new FieldNoteReader("", new[] { p }), p));
    }

    [Fact]
    public void ANamedSourceIsTheOnlyPlaceLooked()
    {
        var p = Point("PP", full: "PP 99", format: "PP 99", properties: new[] { ("Note", "1234"), ("Pole Tag", "5678 PSE") });
        Assert.Equal("5678 PSE", NoteOf(new FieldNoteReader("pole tag", new[] { p }), p));
        Assert.Equal("99", NoteOf(new FieldNoteReader("Full Description", new[] { p }), p));
        Assert.Null(NoteOf(new FieldNoteReader("None", new[] { p }), p));
        Assert.Equal("1234", NoteOf(new FieldNoteReader("", new[] { p }), p));   // automatic: the Note property first
    }

    [Fact]
    public void TheSummarySaysWhereNotesCameFrom()
    {
        var a = Point("PP", properties: ("Note", "1234"));
        var b = Point("PP", properties: ("Note", "1235"));
        var c = Point("SN", full: "SN STOP", format: "SN STOP");
        var reader = new FieldNoteReader("", new[] { a, b, c });
        foreach (var p in new[] { a, b, c }) NoteOf(reader, p);
        Assert.Equal(3, reader.NotesFound);
        Assert.Equal(new[] { "2 from property Note", "1 from Full Description" }, reader.Summary());
    }

    [Fact]
    public void FromTheDrawingToTheLabel()
    {
        var pole = Point("PP", properties: ("Note", "PSE 1234 XFMR"));
        var reader = new FieldNoteReader("", new[] { pole });
        var parsed = _fx.Parser.Parse("1", pole.RawDescription, reader.NoteFor(pole, out _));
        Assert.Equal("POLE 1234 PSE\\PW/ XFMR", parsed.LabelText);
    }
}
