using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using FieldCodes;
using FieldCodes.Settings;
using FieldCodes.Utilities;
using FieldCodes.Utilities.FieldNotes;

namespace FieldCodes.Tests;

/// <summary>Made-up dip observations for the field-notes tests. No client data.</summary>
internal static class DipNotesSamples
{
    public static readonly UtilitySettings Settings = new UtilitySettings();

    public static StructureRecord Structure(string notes, double rim, double northing, double easting,
                                            UtilitySystem system, UtilitySettings? settings = null)
    {
        var parsed = new DipNoteParser(settings ?? Settings).Parse(notes);
        Assert.Single(parsed.Structures);
        var field = parsed.Structures[0];
        return new StructureRecord
        {
            Field = field,
            StructureType = field.FieldCode,
            System = system,
            Cad = new CadStructureSnapshot
            {
                PointNumber = field.PointNumber, Rim = rim, Northing = northing, Easting = easting,
                Description = field.FieldCode + " " + field.PointNumber
            }
        };
    }

    public static UtilityProject Project(params StructureRecord[] structures)
    {
        var project = new UtilityProject();
        project.Structures.AddRange(structures);
        return project;
    }

    public static FieldNotesHeader Header()
    {
        return new FieldNotesHeader
        {
            ProjectName = "Sample Avenue Sewer", ProjectNumber = "99-001", Crew = "A. FIELD / B. BOOK",
            ObservationDate = new DateTime(2026, 10, 28)
        };
    }

    /// <summary>The example from the request: SSMH 1025, rim 125.43, 8" PVC in from the NE and out to the SW.</summary>
    public static StructureRecord Ssmh1025()
    {
        return Structure("PT 1025 SSMH\nBOT 6.80\nWL 6.10\n8 PVC NE 6.25 IN\n8 PVC SW 6.42 OUT", 125.43, 5000, 5000, UtilitySystem.Sanitary);
    }

    public static StructureRecord Sdmh1047()
    {
        return Structure("PT 1047 SDMH\nBOT 8.12\n18 RCP N 6.41 IN SILTED\n24 RCP S 7.02 OUT\n12 CPEP E 4.88 TOP\n6 PVC W UTD",
                         131.86, 5200, 5100, UtilitySystem.Storm);
    }

    public static StructureRecord Cb1048()
    {
        var cb = Structure("PT 1048 CB\nBOT 4.40\nDRY\n12 CPEP S 3.10 OUT", 133.02, 5320, 5100, UtilitySystem.Storm);
        cb.EnteredInsideWidthIn = 24;
        cb.EnteredInsideLengthIn = 36;
        return cb;
    }

    public static FieldNotesReport Report(UtilityProject project, ICollection<string>? selected = null,
                                          ICollection<string>? stale = null, UtilitySettings? settings = null)
    {
        return FieldNotesReportBuilder.Build(project, settings ?? Settings, Header(), "sample.dwg", selected, stale);
    }

    public static FieldNotesIssue Issue()
    {
        return new FieldNotesIssue { Issued = new DateTimeOffset(2026, 11, 2, 9, 15, 0, TimeSpan.FromHours(-8)) };
    }

    public static LayoutDocument Layout(FieldNotesReport report, bool sketches = true)
    {
        return FieldNotesLayout.Build(report, Issue(), sketches);
    }

    public static string F2(double value) { return value.ToString("F2", CultureInfo.InvariantCulture); }

    /// <summary>Every page content stream of a PDF this writer made, decompressed when it is Flate-encoded.</summary>
    public static List<string> ContentStreams(byte[] pdf)
    {
        var text = Latin1(pdf);
        var streams = new List<string>();
        foreach (Match m in Regex.Matches(text, @"<< /Length (\d+)( /Filter /FlateDecode)? >>\nstream\n"))
        {
            var length = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            var start = m.Index + m.Length;
            var data = pdf.Skip(start).Take(length).ToArray();
            Assert.Equal("\nendstream", text.Substring(start + length, 10));
            streams.Add(m.Groups[2].Success ? Encoding.ASCII.GetString(Inflate(data)) : Encoding.ASCII.GetString(data));
        }
        return streams;
    }

    public static byte[] Inflate(byte[] zlib)
    {
        Assert.Equal(0x78, zlib[0]);
        Assert.Equal(0, ((zlib[0] << 8) | zlib[1]) % 31);              // a valid zlib header check
        using var input = new MemoryStream(zlib, 2, zlib.Length - 6);
        using var deflate = new DeflateStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        deflate.CopyTo(output);
        var data = output.ToArray();
        uint a = 1, b = 0;
        foreach (var d in data) { a = (a + d) % 65521; b = (b + a) % 65521; }
        var adler = (b << 16) | a;
        var stored = (uint)(zlib[^4] << 24 | zlib[^3] << 16 | zlib[^2] << 8 | zlib[^1]);
        Assert.Equal(adler, stored);
        return data;
    }

    public static string Latin1(byte[] bytes)
    {
        return new string(bytes.Select(b => (char)b).ToArray());
    }
}

/// <summary>
/// What the dip field notes say: completed observations only, every elevation from the library's own
/// calculations, measured depths black and calculated elevations red, nothing invented.
/// </summary>
public sealed class DipFieldNotesTests
{
    private static TextOp TextOn(LayoutDocument doc, string text)
    {
        return Assert.Single(doc.Texts, t => t.Text == text);
    }

    // ================================================================ single and multiple pipes

    [Fact]
    public void ASinglePipeStructureShowsItsMeasuredDepthBlackAndTheLibrarysInvertRed()
    {
        var s = DipNotesSamples.Structure("PT 1025 SSMH\n8 PVC NE 6.25 IN", 125.43, 5000, 5000, UtilitySystem.Sanitary);
        var report = DipNotesSamples.Report(DipNotesSamples.Project(s));

        var notes = Assert.Single(report.Structures);
        var pipe = Assert.Single(notes.Pipes);
        var library = DipElevations.Pipe(s, s.Field.Pipes[0])!;
        Assert.Equal(ElevationKind.Invert, pipe.ElevationKind);
        Assert.Equal("INV", pipe.ElevationLabel);
        Assert.Equal(DipNotesSamples.F2(library.Value), pipe.ElevationText);
        Assert.Equal("119.18", pipe.ElevationText);                       // 125.43 - 6.25
        Assert.Equal("6.25", pipe.MeasuredDipText);
        Assert.Equal("NE", pipe.Direction);
        Assert.Equal("8\"", pipe.Size);
        Assert.Equal("PVC", pipe.Material);
        Assert.Equal(FlowRole.In, pipe.Role);

        var doc = DipNotesSamples.Layout(report);
        var inv = doc.Texts.Where(t => t.Text == "INV 119.18").ToList();
        Assert.NotEmpty(inv);                                              // the table and the sketch
        Assert.All(inv, t => Assert.Equal(PageColor.Red, t.Color));
        Assert.All(inv, t => Assert.Equal(TextRole.CalculatedInvert, t.Role));
        Assert.Contains(inv, t => t.Font == PageFont.HelveticaBold);
        var md = TextOn(doc, "6.25");
        Assert.Equal(PageColor.Black, md.Color);
        Assert.Equal(TextRole.MeasuredDepth, md.Role);
    }

    [Fact]
    public void EveryPipeIsShownOnItsOwnWithItsOwnDepthAndInvert()
    {
        var s = DipNotesSamples.Ssmh1025();
        var report = DipNotesSamples.Report(DipNotesSamples.Project(s));
        var pipes = Assert.Single(report.Structures).Pipes;

        Assert.Equal(2, pipes.Count);
        Assert.Equal(new[] { "P1", "P2" }, pipes.Select(p => p.Key));
        Assert.Equal(FlowRole.In, pipes[0].Role);
        Assert.Equal(FlowRole.Out, pipes[1].Role);
        Assert.Equal("NE", pipes[0].Direction);
        Assert.Equal("SW", pipes[1].Direction);
        Assert.Equal("6.25", pipes[0].MeasuredDipText);
        Assert.Equal("6.42", pipes[1].MeasuredDipText);
        Assert.Equal("119.18", pipes[0].ElevationText);
        Assert.Equal("119.01", pipes[1].ElevationText);

        var doc = DipNotesSamples.Layout(report);
        Assert.Contains(doc.Texts, t => t.Text == "INV 119.01" && t.Color.Equals(PageColor.Red));
        Assert.Contains(doc.Texts, t => t.Text == "6.42" && t.Role == TextRole.MeasuredDepth && t.Color.Equals(PageColor.Black));
        Assert.Contains(doc.Texts, t => t.Text == "IN");
        Assert.Contains(doc.Texts, t => t.Text == "OUT");
        Assert.Contains(doc.Texts, t => t.Text == "125.43");              // the rim, black
        Assert.Equal(PageColor.Black, TextOn(doc, "125.43").Color);
    }

    // ================================================================ structure kinds

    [Fact]
    public void StormAndSanitaryManholesAreNamedAndDrawnRound()
    {
        var storm = DipNotesSamples.Sdmh1047();
        var sanitary = DipNotesSamples.Ssmh1025();
        var report = DipNotesSamples.Report(DipNotesSamples.Project(storm, sanitary));

        var sd = report.Structures.Single(s => s.Code == "SDMH");
        var ss = report.Structures.Single(s => s.Code == "SSMH");
        Assert.Equal(UtilitySystem.Storm, sd.System);
        Assert.Equal("STORM MANHOLE", sd.TypeName);
        Assert.Equal(UtilitySystem.Sanitary, ss.System);
        Assert.Equal("SANITARY MANHOLE", ss.TypeName);
        Assert.Equal(StructureShape.Round, sd.Sketch.Shape);
        Assert.Equal(StructureShape.Round, ss.Sketch.Shape);

        var doc = DipNotesSamples.Layout(report);
        Assert.Equal(2, doc.Pages.SelectMany(p => p.Ops).OfType<CircleOp>().Count(c => Math.Abs(c.Radius - 12) < 0.01));
        Assert.Contains(doc.Texts, t => t.Text.StartsWith("STORM MANHOLE", StringComparison.Ordinal));
        Assert.Contains(doc.Texts, t => t.Text.StartsWith("SANITARY MANHOLE", StringComparison.Ordinal));
    }

    [Fact]
    public void ACatchBasinIsDrawnSquareWithItsMeasuredInsideSize()
    {
        var cb = DipNotesSamples.Cb1048();
        var report = DipNotesSamples.Report(DipNotesSamples.Project(cb));
        var notes = Assert.Single(report.Structures);

        Assert.Equal("CATCH BASIN", notes.TypeName);
        Assert.Equal(StructureShape.Rectangular, notes.Sketch.Shape);
        Assert.Equal("24\"x36\"", notes.InsideSize);
        Assert.Equal("entered by drafter", notes.InsideSizeSource);
        Assert.Equal("129.92", notes.Pipes[0].ElevationText);              // 133.02 - 3.10

        var doc = DipNotesSamples.Layout(report);
        var squares = doc.Pages.SelectMany(p => p.Ops).OfType<RectOp>().Where(r => Math.Abs(r.W - 22) < 0.01 && Math.Abs(r.H - 22) < 0.01).ToList();
        Assert.Single(squares);
        Assert.DoesNotContain(doc.Pages.SelectMany(p => p.Ops).OfType<CircleOp>(), c => Math.Abs(c.Radius - 12) < 0.01);
        Assert.Contains(doc.Texts, t => t.Text == "DRY (no water noted)");
    }

    // ================================================================ water, bottom, references

    [Fact]
    public void TheWaterLevelIsItsOwnCalculatedElevationAndNeverAnInvert()
    {
        var s = DipNotesSamples.Ssmh1025();
        var report = DipNotesSamples.Report(DipNotesSamples.Project(s));
        var notes = Assert.Single(report.Structures);

        Assert.Equal("6.10", notes.WaterDipText);
        Assert.Equal(DipNotesSamples.F2(DipElevations.Water(s)!.Value), notes.WaterText);
        Assert.Equal("119.33", notes.WaterText);
        Assert.DoesNotContain(notes.Pipes, p => p.ElevationText == notes.WaterText);

        var doc = DipNotesSamples.Layout(report);
        var wl = doc.Texts.Single(t => t.Text.Trim() == "WL 119.33");
        Assert.Equal(PageColor.Red, wl.Color);
        Assert.Equal(TextRole.CalculatedElevation, wl.Role);
        Assert.DoesNotContain(doc.Texts, t => t.Text.Contains("INV 119.33"));
        Assert.Contains(doc.Texts, t => t.Text == "MD 6.10" && t.Color.Equals(PageColor.Black) && t.Role == TextRole.MeasuredDepth);
    }

    [Fact]
    public void TheBottomIsItsOwnCalculatedElevationAndNeverAnInvert()
    {
        var s = DipNotesSamples.Ssmh1025();
        var report = DipNotesSamples.Report(DipNotesSamples.Project(s));
        var notes = Assert.Single(report.Structures);

        Assert.Equal("6.80", notes.BottomDipText);                         // recorded 6.80, stored 6.8
        Assert.Equal(DipNotesSamples.F2(DipElevations.Bottom(s)!.Value), notes.BottomText);
        Assert.Equal("118.63", notes.BottomText);

        var doc = DipNotesSamples.Layout(report);
        var bot = doc.Texts.Single(t => t.Text.Trim() == "BOT 118.63");
        Assert.Equal(TextRole.CalculatedElevation, bot.Role);
        Assert.Equal(PageColor.Red, bot.Color);
        Assert.DoesNotContain(doc.Texts, t => t.Text.Contains("INV 118.63"));
        Assert.Contains(doc.Texts, t => t.Text == "MD 6.80" && t.Role == TextRole.MeasuredDepth);
    }

    [Fact]
    public void ATopOfPipeDipGivesTheTopElevationAndNoInvert()
    {
        var s = DipNotesSamples.Sdmh1047();
        var report = DipNotesSamples.Report(DipNotesSamples.Project(s));
        var top = Assert.Single(report.Structures).Pipes.Single(p => p.Direction == "E");

        Assert.Equal(ElevationKind.TopOfPipe, top.ElevationKind);
        Assert.Equal("TOP", top.ElevationLabel);
        Assert.Equal(DipNotesSamples.F2(DipElevations.Pipe(s, s.Field.Pipes[2])!.Value), top.ElevationText);
        Assert.NotNull(top.InvertNote);
        Assert.Contains("top of the pipe", top.InvertNote);

        var doc = DipNotesSamples.Layout(report);
        Assert.Contains(doc.Texts, t => t.Text == "TOP " + top.ElevationText && t.Role == TextRole.CalculatedElevation);
        Assert.DoesNotContain(doc.Texts, t => t.Text == "INV " + top.ElevationText);
    }

    [Fact]
    public void APipeThatCouldNotBeDippedIsKeptWithItsReasonAndNoElevation()
    {
        var s = DipNotesSamples.Sdmh1047();
        var report = DipNotesSamples.Report(DipNotesSamples.Project(s));
        var utd = Assert.Single(report.Structures).Pipes.Single(p => p.Direction == "W");

        Assert.Null(utd.MeasuredDipText);
        Assert.Null(utd.ElevationText);
        Assert.Contains("UNABLE TO DIP", utd.Conditions);
        var doc = DipNotesSamples.Layout(report);
        Assert.Contains(doc.Texts, t => t.Text == "not dipped");
        Assert.Contains(doc.Texts, t => t.Text.Contains("Not dipped: UNABLE TO DIP."));
    }

    [Fact]
    public void ReferenceBasisIsStatedForEveryDip()
    {
        var s = DipNotesSamples.Structure("PT 1060 SDMH\n12 RCP N 5.00 INV\n12 RCP S 5.10", 120.00, 0, 0, UtilitySystem.Storm);
        var report = DipNotesSamples.Report(DipNotesSamples.Project(s));
        var pipes = Assert.Single(report.Structures).Pipes;
        Assert.Equal("stated in the field note", pipes[0].ReferenceBasisWords);
        Assert.StartsWith("office field-note convention", pipes[1].ReferenceBasisWords);
        Assert.NotNull(report.ConventionNote);
    }

    // ================================================================ completeness

    [Fact]
    public void AnIncompleteObservationIsLeftOutAndListedWithWhatItIsMissing()
    {
        var done = DipNotesSamples.Ssmh1025();
        var missing = DipNotesSamples.Structure("PT 1049 CB\n12 CPEP N", 132.50, 5080, 5110, UtilitySystem.Storm);
        var project = DipNotesSamples.Project(done, missing);

        // Even asked for by name, it is not included.
        var report = DipNotesSamples.Report(project, new[] { done.Id, missing.Id });
        Assert.Equal(new[] { "SSMH 1025" }, report.Structures.Select(s => s.Label));
        var excluded = Assert.Single(report.Incomplete);
        Assert.Equal("CB 1049", excluded.Label);
        Assert.Contains(excluded.Reasons, r => r.Contains("no measure down"));

        var doc = DipNotesSamples.Layout(report);
        Assert.Contains(doc.Texts, t => t.Text.StartsWith("NOT INCLUDED", StringComparison.Ordinal));
        Assert.Contains(doc.Texts, t => t.Text.StartsWith("CB 1049:", StringComparison.Ordinal));
        Assert.DoesNotContain(doc.Texts, t => t.Text == "CB 1049" && t.Font == PageFont.HelveticaBold);

        // Nothing about the observation was changed by being checked.
        Assert.Null(missing.Field.Pipes[0].MeasuredDip);
    }

    [Fact]
    public void CompletenessNeedsARimPipesDipsAndStatedReferences()
    {
        var noRim = DipNotesSamples.Ssmh1025();
        noRim.Cad = null;
        var noPipes = DipNotesSamples.Structure("PT 1070 SDMH\nBOT 5.00", 120, 0, 0, UtilitySystem.Storm);
        var unstated = DipNotesSamples.Structure("PT 1071 SDMH\n12 RCP N 5.00", 120, 0, 10, UtilitySystem.Storm,
                                                 new UtilitySettings { UnmarkedDipsAreInvertsByConvention = false });
        var stale = DipNotesSamples.Sdmh1047();
        var project = DipNotesSamples.Project(noRim, noPipes, unstated, stale);

        Assert.Contains(DipObservationStatus.Check(project, noRim, null).Missing, m => m.Contains("no surveyed point"));
        Assert.Contains(DipObservationStatus.Check(project, noPipes, null).Missing, m => m.Contains("no pipes"));
        Assert.Contains(DipObservationStatus.Check(project, unstated, null).Missing, m => m.Contains("confirm the reference"));
        Assert.Contains(DipObservationStatus.Check(project, stale, new[] { stale.Id }).Missing, m => m.Contains("changed"));
        Assert.True(DipObservationStatus.Check(project, stale, null).IsComplete);

        var report = DipNotesSamples.Report(project, null, new[] { stale.Id });
        Assert.Empty(report.Structures);
        Assert.Equal(4, report.Incomplete.Count);
    }

    [Fact]
    public void ConfirmingAnUnstatedReferenceMakesTheObservationComplete()
    {
        var unstated = DipNotesSamples.Structure("PT 1071 SDMH\n12 RCP N 5.00", 120, 0, 10, UtilitySystem.Storm,
                                                 new UtilitySettings { UnmarkedDipsAreInvertsByConvention = false });
        var project = DipNotesSamples.Project(unstated);
        Assert.False(DipObservationStatus.Check(project, unstated, null).IsComplete);

        ObservationReview.ConfirmReference(project, unstated, unstated.Field.Pipes[0], MeasurementReference.Invert);
        Assert.True(DipObservationStatus.Check(project, unstated, null).IsComplete);
        var pipe = DipNotesSamples.Report(project).Structures.Single().Pipes.Single();
        Assert.Equal("115.00", pipe.ElevationText);
        Assert.Equal("unmarked in the note; confirmed by the drafter", pipe.ReferenceBasisWords);
    }

    // ================================================================ several structures, selection

    [Fact]
    public void SeveralStructuresGoInOneRecordAndTheSelectionIsRespected()
    {
        var a = DipNotesSamples.Ssmh1025();
        var b = DipNotesSamples.Sdmh1047();
        var c = DipNotesSamples.Cb1048();
        var project = DipNotesSamples.Project(a, b, c);

        var all = DipNotesSamples.Report(project);
        Assert.Equal(new[] { "SSMH 1025", "SDMH 1047", "CB 1048" }, all.Structures.Select(s => s.Label));

        var two = DipNotesSamples.Report(project, new[] { a.Id, c.Id });
        Assert.Equal(new[] { "SSMH 1025", "CB 1048" }, two.Structures.Select(s => s.Label));
        Assert.Empty(two.Incomplete);

        var doc = DipNotesSamples.Layout(all);
        foreach (var label in new[] { "SSMH 1025", "SDMH 1047", "CB 1048" })
            Assert.Contains(doc.Texts, t => t.Text == label && t.Font == PageFont.HelveticaBold && t.Size >= 13);
    }

    [Fact]
    public void ALongRecordRunsOverSeveralPagesEachNumberedAndIdentified()
    {
        var project = new UtilityProject();
        for (var i = 0; i < 9; i++)
            project.Structures.Add(DipNotesSamples.Structure(
                "PT " + (2000 + i) + " SSMH\nBOT 7.00\n8 PVC N 6.10 IN\n8 PVC S 6.30 OUT\n6 PVC E 5.00 IN",
                120 + i, 1000 + i * 100, 1000, UtilitySystem.Sanitary));
        var doc = DipNotesSamples.Layout(DipNotesSamples.Report(project));

        Assert.True(doc.Pages.Count > 2, "nine structures need several pages");
        for (var i = 0; i < doc.Pages.Count; i++)
        {
            var texts = doc.Pages[i].Texts.ToList();
            Assert.Contains(texts, t => t.Text == "Page " + (i + 1) + " of " + doc.Pages.Count);
            Assert.Contains(texts, t => t.Text == "PROJECT NO. 99-001");
            Assert.Contains(texts, t => t.Text.StartsWith("99-001", StringComparison.Ordinal));     // the foot
            Assert.Contains(texts, t => t.Text == "UTILITY DIP FIELD NOTES");
            Assert.All(doc.Pages[i].Ops.OfType<TextOp>(), t => Assert.InRange(t.Y, 0, LayoutDocument.LetterHeight));
        }
        // No structure is split: each heading appears once, and every structure's pipes are on its page.
        foreach (var s in project.Structures)
            Assert.Single(doc.Texts, t => t.Text == s.Label && t.Size >= 13);
    }

    [Fact]
    public void AStructureTallerThanAPageContinuesWithItsHeadingRepeated()
    {
        var lines = new StringBuilder("PT 3000 SDMH\n");
        for (var i = 0; i < 40; i++) lines.Append("12 RCP N ").Append((4 + i * 0.01).ToString("0.00", CultureInfo.InvariantCulture)).Append('\n');
        var s = DipNotesSamples.Structure(lines.ToString(), 150, 0, 0, UtilitySystem.Storm);
        var doc = DipNotesSamples.Layout(DipNotesSamples.Report(DipNotesSamples.Project(s)));

        Assert.True(doc.Pages.Count >= 2);
        Assert.Contains(doc.Texts, t => t.Text == "(continued)");
        Assert.Equal(40, doc.Texts.Count(t => t.Role == TextRole.MeasuredDepth && t.Font == PageFont.Helvetica && Regex.IsMatch(t.Text, @"^\d+\.\d\d$")));
        Assert.True(doc.Texts.Count(t => t.Text == "MD (FT)") >= 2, "the table head repeats on the next page");
    }

    // ================================================================ agreement with the library

    [Fact]
    public void EveryInvertAgreesWithDipElevations()
    {
        var a = DipNotesSamples.Ssmh1025();
        var b = DipNotesSamples.Sdmh1047();
        var c = DipNotesSamples.Cb1048();
        var project = DipNotesSamples.Project(a, b, c);
        var doc = DipNotesSamples.Layout(DipNotesSamples.Report(project));

        foreach (var s in project.Structures)
        foreach (var p in s.Field.Pipes.Where(p => p.MeasuredDip.HasValue && p.Reference == MeasurementReference.Invert))
        {
            double? invert, crown;
            DipElevations.Envelope(s, p, out invert, out crown);
            var expected = "INV " + DipNotesSamples.F2(DipElevations.Pipe(s, p)!.Value);
            Assert.Equal(DipNotesSamples.F2(invert!.Value), expected.Substring(4));
            Assert.Contains(doc.Texts, t => t.Text == expected && t.Role == TextRole.CalculatedInvert);
        }
        Assert.All(doc.Texts.Where(t => t.Role == TextRole.CalculatedInvert), t => Assert.StartsWith("INV ", t.Text));
    }

    [Fact]
    public void ElevationsFollowTheOfficeDecimals()
    {
        var settings = new UtilitySettings { ElevationDecimals = 3 };
        var s = DipNotesSamples.Structure("PT 1025 SSMH\n8 PVC NE 6.255 IN", 125.43, 0, 0, UtilitySystem.Sanitary);
        var pipe = DipNotesSamples.Report(DipNotesSamples.Project(s), settings: settings).Structures[0].Pipes[0];
        Assert.Equal("119.175", pipe.ElevationText);
        Assert.Equal("6.255", pipe.MeasuredDipText);
    }

    [Fact]
    public void MeasuredDepthsKeepEveryRecordedDigit()
    {
        Assert.Equal("6.80", FieldNotesReportBuilder.Depth(6.8));
        Assert.Equal("6.00", FieldNotesReportBuilder.Depth(6));
        Assert.Equal("6.25", FieldNotesReportBuilder.Depth(6.25));
        Assert.Equal("6.125", FieldNotesReportBuilder.Depth(6.125));
    }

    // ================================================================ nothing invented

    [Fact]
    public void WhatNobodyRecordedPrintsAsNotRecorded()
    {
        var s = DipNotesSamples.Structure("PT 1080 SDMH\n12 N 5.00", 120, 0, 0, UtilitySystem.Storm);
        s.Cad!.Description = null;
        var project = DipNotesSamples.Project(s);
        var report = FieldNotesReportBuilder.Build(project, DipNotesSamples.Settings, new FieldNotesHeader(), null, null, null);
        var pipe = report.Structures.Single().Pipes.Single();
        Assert.Null(pipe.Material);
        Assert.Null(report.Structures[0].WaterDipText);
        Assert.Null(report.Structures[0].Description);

        var doc = DipNotesSamples.Layout(report);
        Assert.True(doc.Texts.Count(t => t.Text == FieldNotesLayout.NotRecorded) >= 5);
        Assert.Contains(doc.Texts, t => t.Text == "Project number " + FieldNotesLayout.NotRecorded);
        Assert.Contains(doc.Texts, t => t.Text == "Project name " + FieldNotesLayout.NotRecorded);
        Assert.DoesNotContain(doc.Texts, t => t.Text.Contains(DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) && t.Role != TextRole.Volatile);
    }

    [Fact]
    public void QcWarningsTravelWithTheirStructure()
    {
        // 8" pipe 1.9' below a sanitary rim: under the 3.00' cover warning.
        var s = DipNotesSamples.Structure("PT 1090 SSMH\n8 PVC N 1.90 IN", 100, 0, 0, UtilitySystem.Sanitary);
        var report = DipNotesSamples.Report(DipNotesSamples.Project(s));
        var warnings = report.Structures.Single().Warnings;
        Assert.Contains(warnings, w => w.Text.StartsWith("P1 8\" PVC N", StringComparison.Ordinal) && w.Text.Contains("cover"));
        Assert.DoesNotContain(warnings, w => w.Text.StartsWith("SSMH 1090", StringComparison.Ordinal));

        var doc = DipNotesSamples.Layout(report);
        Assert.Contains(doc.Texts, t => t.Role == TextRole.Warning && t.Text.Contains("cover"));
    }

    [Fact]
    public void ConnectionsAndCalculatedSlopesAreShown()
    {
        var up = DipNotesSamples.Structure("PT 1025 SSMH\n8 PVC SW 6.42 OUT", 125.43, 5000, 5000, UtilitySystem.Sanitary);
        var down = DipNotesSamples.Structure("PT 1026 SSMH\n8 PVC NE 7.05 IN", 124.10, 4920, 4920, UtilitySystem.Sanitary);
        var project = DipNotesSamples.Project(up, down);
        var candidate = ConnectionFinder.ManualCandidate(project, up, up.Field.Pipes[0], down, DipNotesSamples.Settings);
        var connection = ConnectionFinder.Accept(project, up, up.Field.Pipes[0], candidate, false, null);
        var slope = SlopeCalculator.Compute(project, connection);

        var report = DipNotesSamples.Report(project);
        var p = report.Structures[0].Pipes[0];
        Assert.Equal("SSMH 1026 (confirmed)", p.ConnectsTo);
        Assert.Contains("Slope " + slope.SlopePercent!.Value.ToString("F2", CultureInfo.InvariantCulture) + "%", p.Slope);
        Assert.Contains("low end at SSMH 1026", p.Slope);
        Assert.Equal("SSMH 1025 (confirmed)", report.Structures[1].Pipes[0].ConnectsTo);
    }

    // ================================================================ sketch

    [Fact]
    public void TheSketchDrawsObservedDirectionsSolidAndNeverPassesAnInferredOneOffAsMeasured()
    {
        var cb = DipNotesSamples.Cb1048();
        var carried = new PipeObservation
        {
            WidthIn = 12, HeightIn = 12, Material = "CPEP", MeasuredDip = 2.95, Reference = MeasurementReference.Invert,
            ReferenceBasis = ReferenceBasis.EnteredByDrafter, Source = ObservationSource.UserEntry, Role = FlowRole.In,
            Direction = DirectionShortcuts.For("E")!, PrefilledFrom = "SDMH 1050"
        };
        carried.Prefilled.Add("direction");
        cb.Field.Pipes.Add(carried);
        var report = DipNotesSamples.Report(DipNotesSamples.Project(cb));
        var sketch = report.Structures.Single().Sketch;

        Assert.Equal(SketchDirectionBasis.Observed, sketch.Pipes[0].Basis);
        Assert.Equal(SketchDirectionBasis.CarriedOver, sketch.Pipes[1].Basis);
        Assert.True(sketch.Pipes[1].Inferred);
        Assert.True(sketch.HasNorthArrow);

        var ops = DipNotesSamples.Layout(report).Pages.SelectMany(p => p.Ops).ToList();
        var pipeLines = ops.OfType<LineOp>().Where(l => Math.Abs(l.Width - 1.3) < 0.01).ToList();
        Assert.Equal(2, pipeLines.Count);
        Assert.Single(pipeLines, l => l.Dash == null);
        Assert.Single(pipeLines, l => l.Dash != null);
        Assert.Contains(ops.OfType<TextOp>(), t => t.Text == "(inferred)");
        Assert.Contains(ops.OfType<TextOp>(), t => t.Text == "N" && t.Font == PageFont.HelveticaBold);
        Assert.Contains(ops.OfType<TextOp>(), t => t.Text == "E\u2020");    // carried over, marked in the table too
    }

    [Fact]
    public void APipeWithNoDirectionIsDrawnOnlyWhenAConnectionGivesOneAndThenAsInferred()
    {
        var a = DipNotesSamples.Structure("PT 1100 SSMH\n8 PVC N 5.00 IN", 120, 1000, 1000, UtilitySystem.Sanitary);
        var b = DipNotesSamples.Structure("PT 1101 SSMH\n8 PVC S 5.50 OUT", 121, 1100, 1000, UtilitySystem.Sanitary);
        var loose = new PipeObservation { WidthIn = 6, HeightIn = 6, MeasuredDip = 4, Reference = MeasurementReference.Invert, ReferenceBasis = ReferenceBasis.EnteredByDrafter };
        var linked = new PipeObservation { WidthIn = 6, HeightIn = 6, MeasuredDip = 4.2, Reference = MeasurementReference.Invert, ReferenceBasis = ReferenceBasis.EnteredByDrafter };
        a.Field.Pipes.Add(loose);
        a.Field.Pipes.Add(linked);
        var project = DipNotesSamples.Project(a, b);
        ConnectionFinder.Accept(project, a, linked, ConnectionFinder.ManualCandidate(project, a, linked, b, DipNotesSamples.Settings), true, null);

        var sketch = DipNotesSamples.Report(project).Structures[0].Sketch;
        Assert.Equal(new[] { "P2" }, sketch.NotDrawn);
        var drawn = sketch.Pipes.Single(p => p.Key == "P3");
        Assert.Equal(SketchDirectionBasis.FromConnection, drawn.Basis);
        Assert.Equal(0, drawn.AzimuthDegrees, 6);                           // b is due north of a
        Assert.Null(DipNotesSamples.Report(project).Structures[0].Pipes[2].Direction);   // still not recorded in the table

        var doc = DipNotesSamples.Layout(DipNotesSamples.Report(project));
        Assert.Contains(doc.Texts, t => t.Text.Contains("Not drawn, no direction recorded: P2"));
    }

    [Fact]
    public void NoNorthArrowWhenNoDirectionIsKnown()
    {
        var s = DipNotesSamples.Structure("PT 1110 SDMH\n12 RCP 5.00", 120, 0, 0, UtilitySystem.Storm);
        var report = DipNotesSamples.Report(DipNotesSamples.Project(s));
        var sketch = report.Structures.Single().Sketch;
        Assert.Empty(sketch.Pipes);
        Assert.False(sketch.HasNorthArrow);
        Assert.DoesNotContain(DipNotesSamples.Layout(report).Texts, t => t.Text == "N" && t.Font == PageFont.HelveticaBold);
    }

    [Fact]
    public void SketchesCanBeLeftOff()
    {
        var doc = DipNotesSamples.Layout(DipNotesSamples.Report(DipNotesSamples.Project(DipNotesSamples.Ssmh1025())), sketches: false);
        Assert.DoesNotContain(doc.Texts, t => t.Text == "PLAN SKETCH");
        Assert.Contains(doc.Texts, t => t.Text == "INV 119.18");
    }

    // ================================================================ header

    [Fact]
    public void TheHeaderRoundTripsAndReadsItsDate()
    {
        var header = DipNotesSamples.Header();
        var back = FieldNotesHeader.FromJson(header.ToJson());
        Assert.Equal("2026-10-28", back.ObservationDateText);
        Assert.Equal(new DateTime(2026, 10, 28), back.ObservationDate);
        Assert.Equal("99-001", back.ProjectIdentifier);
        Assert.Equal("A. FIELD / B. BOOK", back.Crew);
        Assert.Null(new FieldNotesHeader { ObservationDateText = "10/28/2026" }.ObservationDate);
        Assert.Null(FieldNotesHeader.FromJson("{not json").ProjectName);
        Assert.Equal("Sample Avenue Sewer", new FieldNotesHeader { ProjectName = " Sample Avenue Sewer " }.ProjectIdentifier);
    }

    [Fact]
    public void TheFingerprintIgnoresTheIssueAndFollowsTheContent()
    {
        var project = DipNotesSamples.Project(DipNotesSamples.Ssmh1025());
        var report = DipNotesSamples.Report(project);
        var first = FieldNotesLayout.Build(report, new FieldNotesIssue { Issued = DateTimeOffset.Now, Revision = 0 }, true).Fingerprint();
        var later = FieldNotesLayout.Build(report, new FieldNotesIssue { Issued = DateTimeOffset.Now.AddDays(3), Revision = 4, Supersedes = "x_R3.pdf" }, true).Fingerprint();
        Assert.Equal(first, later);

        project.Structures[0].Field.Pipes[0].MeasuredDip = 6.26;
        var changed = FieldNotesLayout.Build(DipNotesSamples.Report(project), new FieldNotesIssue(), true).Fingerprint();
        Assert.NotEqual(first, changed);
    }

    // ================================================================ settings

    [Fact]
    public void FieldNotesSettingsDefaultSafelyAndRoundTrip()
    {
        var d = new UtilitySettings();
        Assert.Equal(@"Survey\Field Notes\Utility Dips", d.FieldNotesFolder);
        Assert.False(d.FieldNotesAutoGenerate);
        Assert.False(d.FieldNotesOverwrite);
        Assert.True(d.FieldNotesOpenAfterSave);
        Assert.True(d.FieldNotesSketches);

        d.FieldNotesFolder = @"Docs\Dips";
        d.FieldNotesAutoGenerate = true;
        d.FieldNotesOverwrite = true;
        d.FieldNotesOpenAfterSave = false;
        d.FieldNotesSketches = false;
        var back = Newtonsoft.Json.JsonConvert.DeserializeObject<UtilitySettings>(Newtonsoft.Json.JsonConvert.SerializeObject(d))!;
        Assert.Equal(@"Docs\Dips", back.FieldNotesFolder);
        Assert.True(back.FieldNotesAutoGenerate);
        Assert.True(back.FieldNotesOverwrite);
        Assert.False(back.FieldNotesOpenAfterSave);
        Assert.False(back.FieldNotesSketches);

        // Settings saved before the field notes existed keep the defaults.
        var old = Newtonsoft.Json.JsonConvert.DeserializeObject<UtilitySettings>("{\"elevationDecimals\":2}")!;
        Assert.Equal(@"Survey\Field Notes\Utility Dips", old.FieldNotesFolder);
        Assert.True(old.FieldNotesSketches);

        var problems = new List<string>();
        new UtilitySettings { FieldNotesFolder = "Survey|Notes" }.Validate(problems);
        Assert.Contains(problems, p => p.Contains("field notes folder"));
        problems.Clear();
        new UtilitySettings { FieldNotesFolder = @"P:\Jobs\Field Notes" }.Validate(problems);
        Assert.Empty(problems);
    }
}
