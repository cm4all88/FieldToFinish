using System.Globalization;
using System.Text.RegularExpressions;
using FieldCodes.Utilities;
using FieldCodes.Utilities.FieldNotes;

namespace FieldCodes.Tests;

/// <summary>
/// The field-notes PDF itself: a valid PDF 1.4 file with exact cross-reference offsets, red set before every
/// invert and black before every measured depth, file names that say the project and date, and an issued file
/// that is never overwritten unless overwrite was chosen.
/// </summary>
public sealed class DipFieldNotesPdfTests
{
    private static FieldNotesReport SampleReport()
    {
        return DipNotesSamples.Report(DipNotesSamples.Project(DipNotesSamples.Ssmh1025(), DipNotesSamples.Sdmh1047(), DipNotesSamples.Cb1048()));
    }

    private static byte[] Pdf(FieldNotesReport report, bool compress)
    {
        return DipFieldNotes.Render(report, DipNotesSamples.Issue(), true, compress);
    }

    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ftf-fieldnotes-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    // ================================================================ structure

    [Fact]
    public void ThePdfIsStructurallyValid()
    {
        foreach (var compress in new[] { false, true })
        {
            var pdf = Pdf(SampleReport(), compress);
            var text = DipNotesSamples.Latin1(pdf);

            Assert.StartsWith("%PDF-1.4\n", text);
            Assert.True(pdf[9] == (byte)'%' && pdf[10] > 127, "binary marker comment after the header");
            Assert.EndsWith("%%EOF\n", text);

            // startxref points at the xref table, and every in-use entry at its "n 0 obj".
            var startxref = int.Parse(Regex.Match(text, @"startxref\n(\d+)\n%%EOF\n$").Groups[1].Value, CultureInfo.InvariantCulture);
            Assert.Equal("xref\n", text.Substring(startxref, 5));
            var header = Regex.Match(text.Substring(startxref), @"^xref\n0 (\d+)\n");
            var size = int.Parse(header.Groups[1].Value, CultureInfo.InvariantCulture);
            var entries = text.Substring(startxref + header.Length, size * 20);
            Assert.StartsWith("0000000000 65535 f \n", entries);
            for (var n = 1; n < size; n++)
            {
                var entry = entries.Substring(n * 20, 20);
                Assert.Matches(@"^\d{10} 00000 n \n$", entry);
                var offset = int.Parse(entry.Substring(0, 10), CultureInfo.InvariantCulture);
                Assert.Equal(n + " 0 obj\n", text.Substring(offset, (n + " 0 obj\n").Length));
            }
            Assert.Equal(size, Regex.Matches(text, @"(?m)^\d+ 0 obj$").Count + 1);

            var trailer = Regex.Match(text, @"trailer\n<< /Size (\d+) /Root 1 0 R /Info 7 0 R /ID \[<([0-9A-F]{32})> <\2>\] >>");
            Assert.True(trailer.Success, "trailer");
            Assert.Equal(size, int.Parse(trailer.Groups[1].Value, CultureInfo.InvariantCulture));

            // Page tree: /Count agrees with the page objects and the laid-out pages.
            var pages = FieldNotesLayout.Build(SampleReport(), DipNotesSamples.Issue(), true).Pages.Count;
            Assert.Equal(pages, int.Parse(Regex.Match(text, @"/Type /Pages /Kids \[[^\]]*\] /Count (\d+)").Groups[1].Value, CultureInfo.InvariantCulture));
            Assert.Equal(pages, Regex.Matches(text, @"/Type /Page /Parent 2 0 R /MediaBox \[0 0 612 792\]").Count);
            Assert.Equal(pages, DipNotesSamples.ContentStreams(pdf).Count);

            Assert.Contains("/BaseFont /Helvetica /Encoding /WinAnsiEncoding", text);
            Assert.Contains("/BaseFont /Helvetica-Bold", text);
            Assert.Contains("/BaseFont /Courier", text);
            Assert.Contains("/Title (Utility Dip Field Notes - 99-001 - 2026-10-28)", text);
        }
    }

    [Fact]
    public void CompressedPagesInflateToTheSameOperators()
    {
        var plain = DipNotesSamples.ContentStreams(Pdf(SampleReport(), false));
        var packed = DipNotesSamples.ContentStreams(Pdf(SampleReport(), true));
        Assert.Equal(plain, packed);
        Assert.Contains("/Filter /FlateDecode", DipNotesSamples.Latin1(Pdf(SampleReport(), true)));
        Assert.True(Pdf(SampleReport(), true).Length < Pdf(SampleReport(), false).Length);
    }

    // ================================================================ colour

    [Fact]
    public void EveryInvertIsSetInRedAndEveryMeasuredDepthInBlack()
    {
        var report = SampleReport();
        var streams = string.Join("\n", DipNotesSamples.ContentStreams(Pdf(report, true)));
        var shown = Regex.Matches(streams, @"BT /F(\d) ([\d.]+) Tf ([\d.]+ [\d.]+ [\d.]+) rg [\d.]+ [\d.]+ Td \((.*?)\) Tj ET")
                         .Select(m => new { Font = m.Groups[1].Value, Color = m.Groups[3].Value, Text = m.Groups[4].Value })
                         .ToList();

        var inverts = report.Structures.SelectMany(s => s.Pipes).Where(p => p.ElevationKind == ElevationKind.Invert).ToList();
        Assert.Equal(5, inverts.Count);
        foreach (var p in inverts)
        {
            var ops = shown.Where(t => t.Text == "INV " + p.ElevationText).ToList();
            Assert.NotEmpty(ops);
            Assert.All(ops, t => Assert.Equal("1 0 0", t.Color));           // red
            Assert.Contains(ops, t => t.Font == "2");                         // bold in the table
        }

        foreach (var p in report.Structures.SelectMany(s => s.Pipes).Where(p => p.MeasuredDipText != null))
        {
            var md = shown.Where(t => t.Text == p.MeasuredDipText).ToList();
            Assert.NotEmpty(md);
            Assert.All(md, t => Assert.Equal("0 0 0", t.Color));              // black
        }

        // Nothing but calculated elevations is red.
        Assert.All(shown.Where(t => t.Color == "1 0 0"),
                   t => Assert.Matches(@"^(\s*(INV|TOP|SPR|EL|BOT|WL) -?\d+\.\d+|INV|TOP  SPR  BOT  WL)$", t.Text));
        // And the bottom and water elevations are red but never called an invert.
        Assert.Contains(shown, t => t.Color == "1 0 0" && t.Text.Trim() == "BOT 123.74");
        Assert.DoesNotContain(shown, t => t.Text == "INV 123.74");
    }

    [Fact]
    public void TheRedOperatorComesImmediatelyBeforeTheInvertText()
    {
        var s = DipNotesSamples.Structure("PT 1025 SSMH\n8 PVC NE 6.25 IN", 125.43, 0, 0, UtilitySystem.Sanitary);
        var streams = string.Join("\n", DipNotesSamples.ContentStreams(Pdf(DipNotesSamples.Report(DipNotesSamples.Project(s)), false)));
        Assert.Matches(@"/F2 [\d.]+ Tf 1 0 0 rg [\d.]+ [\d.]+ Td \(INV 119\.18\) Tj", streams);
        Assert.Matches(@"/F1 [\d.]+ Tf 0 0 0 rg [\d.]+ [\d.]+ Td \(6\.25\) Tj", streams);
        Assert.DoesNotMatch(@"0 0 0 rg [\d.]+ [\d.]+ Td \(INV ", streams);
    }

    [Fact]
    public void TextIsEscapedForPdf()
    {
        Assert.Equal(@"(A \(B\) \\ C)", PdfWriter.Literal(@"A (B) \ C"));
        Assert.Equal(@"(24\260)", PdfWriter.Literal("24\u00b0"));             // degree sign, WinAnsi octal
        Assert.Equal(@"(\226)", PdfWriter.Literal("\u2013"));                 // en dash
        Assert.Equal("(?)", PdfWriter.Literal("\u4e2d"));                    // nothing outside WinAnsi gets through
        Assert.Equal("D:20261102091500-08'00'", PdfWriter.PdfDate(new DateTimeOffset(2026, 11, 2, 9, 15, 0, TimeSpan.FromHours(-8))));
    }

    [Fact]
    public void LinesWrapToTheirWidth()
    {
        var lines = FontMetrics.Wrap("the quick brown fox jumps over the lazy dog again and again", PageFont.Helvetica, 10, 100);
        Assert.True(lines.Count > 1);
        Assert.All(lines, l => Assert.True(FontMetrics.Width(PageFont.Helvetica, 10, l) <= 100.0001));
        Assert.Equal(6.0, FontMetrics.Width(PageFont.Courier, 10, "A"), 6);   // Courier is 600 units
        Assert.Equal(5.56, FontMetrics.Width(PageFont.Helvetica, 10, "0"), 6);
    }

    // ================================================================ names

    [Fact]
    public void TheFileNameCarriesTheProjectAndTheDateOfObservation()
    {
        Assert.Equal("99-001_Utility-Dip-Field-Notes_2026-10-28", FieldNotesFiles.BaseName(DipNotesSamples.Header(), "/jobs/base.dwg"));

        var byName = new FieldNotesHeader { ProjectName = "Sample Ave: Phase 2/3", ObservationDate = new DateTime(2026, 1, 5) };
        Assert.Equal("Sample-Ave-Phase-2-3_Utility-Dip-Field-Notes_2026-01-05", FieldNotesFiles.BaseName(byName, null));

        var nothing = new FieldNotesHeader();
        Assert.Equal("26-104-base_Utility-Dip-Field-Notes_undated",
                     FieldNotesFiles.BaseName(nothing, Path.Combine("jobs", "26-104 base.dwg")));
        Assert.Equal("Dip-Project_Utility-Dip-Field-Notes_undated", FieldNotesFiles.BaseName(nothing, null));
        Assert.Equal("a-b-c", FieldNotesFiles.Sanitize("a<b>c?"));
        Assert.Equal("x", FieldNotesFiles.Sanitize(" x. "));
    }

    [Fact]
    public void ANewSaveIsTheNextRevisionAndNeverTheIssuedFile()
    {
        const string stem = "99-001_Utility-Dip-Field-Notes_2026-10-28";
        var first = FieldNotesFiles.Plan("f", stem, false, new string[0]);
        Assert.Equal(stem + ".pdf", first.FileName);
        Assert.Equal(0, first.Revision);
        Assert.Null(first.Latest);

        var second = FieldNotesFiles.Plan("f", stem, false, new[] { stem + ".pdf", "other.pdf", stem + ".PDF.bak" });
        Assert.Equal(stem + "_R1.pdf", second.FileName);
        Assert.Equal(1, second.Revision);
        Assert.Equal(stem + ".pdf", second.Supersedes);
        Assert.False(second.Replaces);

        // One past the highest, even with a gap -- never back into an issued number.
        var later = FieldNotesFiles.Plan("f", stem, false, new[] { stem + ".pdf", stem + "_R1.pdf", stem + "_R3.pdf" });
        Assert.Equal(stem + "_R4.pdf", later.FileName);
        Assert.Equal(stem + "_R3.pdf", later.Supersedes);

        // Overwrite, only when chosen: the newest file of that name.
        var replace = FieldNotesFiles.Plan("f", stem, true, new[] { stem + ".pdf", stem + "_R1.pdf" });
        Assert.True(replace.Replaces);
        Assert.Equal(stem + "_R1.pdf", replace.FileName);

        // Another date is another record.
        Assert.Equal(stem + ".pdf", FieldNotesFiles.Plan("f", stem, false, new[] { "99-001_Utility-Dip-Field-Notes_2026-10-27.pdf" }).FileName);
    }

    [Fact]
    public void SavingTwiceKeepsTheFirstIssueUntouched()
    {
        var dir = TempDir();
        try
        {
            var report = SampleReport();
            var stem = FieldNotesFiles.BaseName(report.Header, null);
            var options = new FieldNotesSaveOptions { Issued = new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero) };

            var one = DipFieldNotes.Save(report, dir, stem, options);
            Assert.Equal(FieldNotesOutcome.Written, one.Outcome);
            Assert.Equal(Path.Combine(dir, stem + ".pdf"), one.Path);
            var issued = File.ReadAllBytes(one.Path);

            options.Issued = options.Issued.AddHours(1);
            var two = DipFieldNotes.Save(report, dir, stem, options);
            Assert.Equal(FieldNotesOutcome.Written, two.Outcome);
            Assert.Equal(Path.Combine(dir, stem + "_R1.pdf"), two.Path);
            Assert.Equal(1, two.Revision);
            Assert.Equal(stem + ".pdf", two.Supersedes);
            Assert.Equal(issued, File.ReadAllBytes(one.Path));                 // untouched
            Assert.Contains("Revision R1", string.Join("\n", DipNotesSamples.ContentStreams(File.ReadAllBytes(two.Path))));

            // Overwrite only when asked: the newest issue is replaced, nothing else.
            options.Overwrite = true;
            var three = DipFieldNotes.Save(report, dir, stem, options);
            Assert.Equal(FieldNotesOutcome.Replaced, three.Outcome);
            Assert.Equal(two.Path, three.Path);
            Assert.Equal(issued, File.ReadAllBytes(one.Path));
            Assert.Equal(2, Directory.GetFiles(dir, "*.pdf").Length);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void AnAutomaticSaveWritesNothingWhenNothingChanged()
    {
        var dir = TempDir();
        try
        {
            var project = DipNotesSamples.Project(DipNotesSamples.Ssmh1025());
            var stem = FieldNotesFiles.BaseName(DipNotesSamples.Header(), null);
            var auto = new FieldNotesSaveOptions { SkipIfUnchanged = true };

            Assert.Equal(FieldNotesOutcome.Written, DipFieldNotes.Save(DipNotesSamples.Report(project), dir, stem, auto).Outcome);
            var again = DipFieldNotes.Save(DipNotesSamples.Report(project), dir, stem, auto);
            Assert.Equal(FieldNotesOutcome.Unchanged, again.Outcome);
            Assert.Single(Directory.GetFiles(dir, "*.pdf"));

            project.Structures.Add(DipNotesSamples.Cb1048());
            var changed = DipFieldNotes.Save(DipNotesSamples.Report(project), dir, stem, auto);
            Assert.Equal(FieldNotesOutcome.Written, changed.Outcome);
            Assert.EndsWith("_R1.pdf", changed.Path);

            var hash = FieldNotesFiles.ReadContentHash(File.ReadAllBytes(changed.Path));
            Assert.Equal(FieldNotesLayout.Build(DipNotesSamples.Report(project), new FieldNotesIssue(), true).Fingerprint(), hash);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void NothingIsWrittenWithoutACompletedObservationOrAFolder()
    {
        var incomplete = DipNotesSamples.Project(DipNotesSamples.Structure("PT 1049 CB\n12 CPEP N", 132.50, 0, 0, UtilitySystem.Storm));
        var none = DipFieldNotes.Save(DipNotesSamples.Report(incomplete), "unused", "x", new FieldNotesSaveOptions());
        Assert.Equal(FieldNotesOutcome.NothingToSave, none.Outcome);
        Assert.Contains("1 incomplete", none.Message);

        var noFolder = DipFieldNotes.Save(SampleReport(), null!, "x", new FieldNotesSaveOptions());
        Assert.Equal(FieldNotesOutcome.Failed, noFolder.Outcome);
        Assert.Contains("Save the drawing first", noFolder.Message);
    }

    // ================================================================ folder

    [Fact]
    public void TheFolderIsFoundUnderTheProjectFromTheDrawing()
    {
        var root = Path.Combine(Path.GetTempPath(), "jobs", "99-001");
        const string setting = @"Survey\Field Notes\Utility Dips";
        var expected = Path.Combine(root, "Survey", "Field Notes", "Utility Dips");

        // The drawing inside the project's Survey folder.
        Assert.Equal(expected, FieldNotesFiles.ResolveFolder(Path.Combine(root, "Survey", "CAD", "base.dwg"), setting, d => false));
        Assert.Equal(expected, FieldNotesFiles.ResolveFolder(Path.Combine(root, "survey", "base.dwg"), setting, d => false));

        // The drawing beside a Survey folder.
        var beside = Path.Combine(root, "CAD", "base.dwg");
        Assert.Equal(expected, FieldNotesFiles.ResolveFolder(beside, setting, d => d == Path.Combine(root, "Survey")));

        // Neither: under the drawing's own folder, and either slash works.
        Assert.Equal(Path.Combine(root, "CAD", "Survey", "Field Notes", "Utility Dips"),
                     FieldNotesFiles.ResolveFolder(beside, "Survey/Field Notes/Utility Dips", d => false));

        // An absolute folder is used as it is; an unsaved drawing has no folder.
        var absolute = Path.Combine(Path.GetTempPath(), "dip-notes");
        Assert.Equal(absolute, FieldNotesFiles.ResolveFolder(beside, absolute, d => false));
        Assert.Equal(@"\\server\share\Dips", FieldNotesFiles.ResolveFolder(beside, @"\\server\share\Dips", d => false));
        Assert.Null(FieldNotesFiles.ResolveFolder(null, setting, d => true));
        Assert.Null(FieldNotesFiles.ResolveFolder("", setting, d => true));

        // A blank setting means the default.
        Assert.Equal(expected, FieldNotesFiles.ResolveFolder(Path.Combine(root, "Survey", "CAD", "base.dwg"), "  ", d => false));
    }
}
