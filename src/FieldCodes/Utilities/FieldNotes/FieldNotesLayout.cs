using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FieldCodes.Settings;

namespace FieldCodes.Utilities.FieldNotes
{
    /// <summary>When and as what a field-notes PDF is issued. Printed on the pages; not part of the content.</summary>
    public sealed class FieldNotesIssue
    {
        public DateTimeOffset Issued { get; set; }

        /// <summary>0 for the first issue of a file name, then 1, 2... for _R1, _R2.</summary>
        public int Revision { get; set; }

        public string FileName { get; set; }

        /// <summary>The earlier file this revision supersedes, if any.</summary>
        public string Supersedes { get; set; }

        public FieldNotesIssue() { Issued = DateTimeOffset.Now; }
    }

    /// <summary>
    /// Lays a field-notes record out on US Letter pages in the manner of a survey field book: a running head and
    /// foot on every page (project, date, crew, page x of n), then one block per structure -- the structure's
    /// facts beside a plan sketch, a ruled table of its pipes, the note as it was written, and its warnings.
    /// Measured and recorded values are black; calculated elevations are red, and an invert is red and bold.
    /// Pure: no files, no fonts, no PDF -- the result is a list of drawing operations per page.
    /// </summary>
    public static class FieldNotesLayout
    {
        // Page geometry, in points. One inch is 72.
        private const double Left = 42, Right = 570, Width = Right - Left;
        private const double ContentTop = 80, ContentBottom = 740;

        private const double SketchWidth = 214, SketchHeight = 168;

        // Pipe table columns: left edge offsets from the margin.
        private const double ColPipe = 0, ColFlow = 25, ColSize = 55, ColMaterial = 105, ColDirection = 157,
                             ColMdRight = 252, ColMeasuredTo = 262, ColElevation = 328, ColConnects = 406;

        public static LayoutDocument Build(FieldNotesReport report, FieldNotesIssue issue, bool sketches)
        {
            if (report == null) throw new ArgumentNullException("report");
            var composer = new Composer(report, issue ?? new FieldNotesIssue(), sketches);
            return composer.Compose();
        }

        /// <summary>"not recorded", the words for anything nobody wrote down.</summary>
        public const string NotRecorded = "not recorded";

        // =============================================================== composer

        private sealed class Row
        {
            public double Height;
            public Action<LayoutPage, double> Draw;
            public bool IsPipe;
        }

        private sealed class Run
        {
            public string Text;
            public PageFont Font;
            public double Size;
            public PageColor Color;
            public TextRole Role;

            public Run(string text, PageFont font, double size, PageColor color, TextRole role)
            {
                Text = text; Font = font; Size = size; Color = color; Role = role;
            }

            public double Width { get { return FontMetrics.Width(Font, Size, Text); } }
        }

        private sealed class Composer
        {
            private readonly FieldNotesReport _report;
            private readonly FieldNotesIssue _issue;
            private readonly bool _sketches;
            private readonly LayoutDocument _doc = new LayoutDocument();
            private LayoutPage _page;
            private double _y;

            public Composer(FieldNotesReport report, FieldNotesIssue issue, bool sketches)
            {
                _report = report;
                _issue = issue;
                _sketches = sketches;
            }

            public LayoutDocument Compose()
            {
                NewPage();
                Place(IntroRows(), 0, null, null);
                foreach (var structure in _report.Structures)
                {
                    // A structure is kept on one page, as it would be in a field book. Only one taller than a
                    // page is split, and then never before its heading, facts, sketch and first pipe.
                    var rows = StructureRows(structure);
                    var total = rows.Sum(r => r.Height);
                    var keep = total <= ContentBottom - ContentTop
                        ? rows.Count
                        : Math.Min(rows.Count, 3 + (structure.Pipes.Count > 0 ? 1 : 0));
                    Place(rows, keep, () => ContinuedRow(structure), TableHeaderRow());
                }
                var count = _doc.Pages.Count;
                for (var i = 0; i < count; i++) Furniture(_doc.Pages[i], i + 1, count);
                return _doc;
            }

            private void NewPage()
            {
                _page = new LayoutPage();
                _doc.Pages.Add(_page);
                _y = ContentTop;
            }

            /// <summary>Puts rows down the page. The first keepTogether rows go on one page; after a page break
            /// in the middle of a structure, a "(continued)" heading and, before a pipe row, the table head repeat.</summary>
            private void Place(IList<Row> rows, int keepTogether, Func<Row> continued, Row tableHeader)
            {
                if (rows.Count == 0) return;
                var lead = 0.0;
                for (var i = 0; i < keepTogether && i < rows.Count; i++) lead += rows[i].Height;
                if (_y + lead > ContentBottom && _y > ContentTop + 0.5) NewPage();

                for (var i = 0; i < rows.Count; i++)
                {
                    var row = rows[i];
                    if (i >= keepTogether && _y + row.Height > ContentBottom && _y > ContentTop + 0.5)
                    {
                        NewPage();
                        if (continued != null) Put(continued());
                        if (row.IsPipe && tableHeader != null) Put(tableHeader);
                    }
                    Put(row);
                }
            }

            private void Put(Row row)
            {
                row.Draw(_page, _y);
                _y += row.Height;
            }

            // ----------------------------------------------------------- furniture

            private void Furniture(LayoutPage page, int number, int count)
            {
                var h = _report.Header;
                Text(page, Left, 46, PageFont.HelveticaBold, 12.5, PageColor.Black, "UTILITY DIP FIELD NOTES", TextRole.Chrome);

                var id = !string.IsNullOrWhiteSpace(h.ProjectNumber) ? "PROJECT NO. " + h.ProjectNumber.Trim() : null;
                if (id != null) TextRight(page, Right, 46, PageFont.HelveticaBold, 10, PageColor.Black, id, TextRole.Recorded);
                else TextRight(page, Right, 46, PageFont.HelveticaOblique, 9, PageColor.Black, "Project number " + NotRecorded, TextRole.Recorded);

                var name = !string.IsNullOrWhiteSpace(h.ProjectName) ? h.ProjectName.Trim() : null;
                var nameRuns = name != null
                    ? new List<Run> { new Run(Fit(name, PageFont.Helvetica, 8.5, Width * 0.48), PageFont.Helvetica, 8.5, PageColor.Black, TextRole.Recorded) }
                    : new List<Run> { new Run("Project name " + NotRecorded, PageFont.HelveticaOblique, 8.5, PageColor.Black, TextRole.Recorded) };
                Runs(page, Left, 58, nameRuns);

                var right = new List<Run>
                {
                    new Run("Observed ", PageFont.Helvetica, 8.5, PageColor.Black, TextRole.Chrome),
                    Value(h.ObservationDate.HasValue ? h.ObservationDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null, 8.5),
                    new Run("     Crew ", PageFont.Helvetica, 8.5, PageColor.Black, TextRole.Chrome),
                    Value(string.IsNullOrWhiteSpace(h.Crew) ? null : Fit(h.Crew.Trim(), PageFont.Helvetica, 8.5, Width * 0.3), 8.5)
                };
                Runs(page, Right - right.Sum(r => r.Width), 58, right);

                Line(page, Left, 63, Right, 63, 1.1, PageColor.Black, null);
                Line(page, Left, 65.4, Right, 65.4, 0.4, PageColor.Black, null);

                // Foot: the project again (a loose page still says whose it is), the issue, the page number.
                Line(page, Left, 748, Right, 748, 0.4, PageColor.Gray, null);
                var ident = Identification();
                Text(page, Left, 760, PageFont.Helvetica, 7.5, PageColor.Black, Fit(ident, PageFont.Helvetica, 7.5, 228), TextRole.Recorded);
                var issued = "Issued " + _issue.Issued.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) +
                             (_issue.Revision > 0 ? "  ·  Revision R" + _issue.Revision.ToString(CultureInfo.InvariantCulture) : string.Empty);
                TextCenter(page, (Left + Right) / 2 + 20, 760, PageFont.Helvetica, 7.5, PageColor.Black, issued, TextRole.Volatile);
                TextRight(page, Right, 760, PageFont.HelveticaBold, 7.5, PageColor.Black,
                          "Page " + number.ToString(CultureInfo.InvariantCulture) + " of " + count.ToString(CultureInfo.InvariantCulture), TextRole.Chrome);
            }

            private string Identification()
            {
                var h = _report.Header;
                var parts = new List<string>();
                if (!string.IsNullOrWhiteSpace(h.ProjectNumber)) parts.Add(h.ProjectNumber.Trim());
                if (!string.IsNullOrWhiteSpace(h.ProjectName)) parts.Add(h.ProjectName.Trim());
                if (parts.Count == 0 && !string.IsNullOrWhiteSpace(_report.DrawingName)) parts.Add(_report.DrawingName.Trim());
                if (parts.Count == 0) parts.Add("Project " + NotRecorded);
                return string.Join("  ·  ", parts.ToArray());
            }

            // --------------------------------------------------------------- intro

            private IList<Row> IntroRows()
            {
                var rows = new List<Row>();
                var h = _report.Header;
                var issued = _issue.Issued.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) +
                             (_issue.Revision > 0 ? ", revision R" + _issue.Revision.ToString(CultureInfo.InvariantCulture) : ", first issue") +
                             (!string.IsNullOrEmpty(_issue.Supersedes) ? " (supersedes " + _issue.Supersedes + ")" : string.Empty);
                rows.Add(GridRow("PROJECT", h.ProjectName, "PROJECT NO.", h.ProjectNumber, TextRole.Recorded));
                rows.Add(GridRow("DATE OF OBSERVATION",
                                 h.ObservationDate.HasValue ? h.ObservationDate.Value.ToString("yyyy-MM-dd (dddd)", CultureInfo.InvariantCulture) : null,
                                 "CREW / OBSERVER", h.Crew, TextRole.Recorded));
                rows.Add(GridRow("DRAWING", _report.DrawingName, "ISSUED", issued, TextRole.Volatile));
                rows.Add(Spacer(4));
                rows.Add(LegendRow());
                rows.Add(Spacer(8));

                var included = _report.Structures.Select(s => s.Label).ToList();
                rows.Add(Caption("IN THIS RECORD (" + included.Count.ToString(CultureInfo.InvariantCulture) + ")"));
                rows.AddRange(Paragraph(included.Count > 0
                    ? string.Join("  ·  ", included.ToArray())
                    : "No completed observations were selected for this record.", PageFont.Helvetica, 8.8, 11, 0, TextRole.Recorded));

                if (_report.Incomplete.Count > 0)
                {
                    rows.Add(Spacer(4));
                    rows.Add(Caption("NOT INCLUDED — OBSERVATION INCOMPLETE (" +
                                     _report.Incomplete.Count.ToString(CultureInfo.InvariantCulture) + ")"));
                    foreach (var x in _report.Incomplete)
                        rows.AddRange(Paragraph(x.Label + ": " + string.Join("; ", x.Reasons.ToArray()) + ".",
                                                PageFont.Helvetica, 7.8, 9.4, 8, TextRole.Warning));
                }
                if (!string.IsNullOrWhiteSpace(_report.ConventionNote))
                {
                    rows.Add(Spacer(4));
                    rows.AddRange(Paragraph("Dips not marked INV or TOP in the field note follow the office convention: “" +
                                            _report.ConventionNote.Trim().TrimEnd('.') + "”.",
                                            PageFont.HelveticaOblique, 7.8, 9.4, 0, TextRole.Recorded));
                }
                rows.Add(Spacer(6));
                rows.Add(RuleRow(1.2, 10));
                return rows;
            }

            private Row GridRow(string label1, string value1, string label2, string value2, TextRole role2)
            {
                const double half = Width / 2;
                var lines1 = value1 != null && value1.Trim().Length > 0 ? FontMetrics.Wrap(value1.Trim(), PageFont.Helvetica, 9.5, half - 12) : null;
                var lines2 = value2 != null && value2.Trim().Length > 0 ? FontMetrics.Wrap(value2.Trim(), PageFont.Helvetica, 9.5, half - 12) : null;
                var n = Math.Max(lines1 != null ? lines1.Count : 1, lines2 != null ? lines2.Count : 1);
                return new Row
                {
                    Height = 9 + n * 11.5 + 4,
                    Draw = (page, y) =>
                    {
                        Cell(page, Left, y, label1, lines1, TextRole.Recorded);
                        Cell(page, Left + half, y, label2, lines2, role2);
                    }
                };
            }

            private void Cell(LayoutPage page, double x, double y, string label, IList<string> lines, TextRole role)
            {
                Text(page, x, y + 7, PageFont.HelveticaBold, 6.8, PageColor.Black, label, TextRole.Chrome);
                if (lines == null)
                {
                    Text(page, x, y + 17.5, PageFont.HelveticaOblique, 9.5, PageColor.Black, NotRecorded, role == TextRole.Volatile ? role : TextRole.Recorded);
                    return;
                }
                for (var i = 0; i < lines.Count; i++)
                    Text(page, x, y + 17.5 + i * 11.5, PageFont.Helvetica, 9.5, PageColor.Black, lines[i], role);
            }

            private Row LegendRow()
            {
                const double pad = 7, key = 92;
                var entries = new List<KeyValuePair<Run, string>>
                {
                    Pair(new Run("MD", PageFont.Helvetica, 8.8, PageColor.Black, TextRole.Chrome),
                         "Measured depth below the rim, in feet, exactly as recorded in the field (black)."),
                    Pair(new Run("INV", PageFont.HelveticaBold, 8.8, PageColor.Red, TextRole.Chrome),
                         "Calculated invert elevation = surveyed rim elevation − MD (red, bold)."),
                    Pair(new Run("TOP  SPR  BOT  WL", PageFont.Helvetica, 8.8, PageColor.Red, TextRole.Chrome),
                         "Other calculated elevations: top of pipe, springline, structure bottom, water surface (red). None of these is an invert."),
                    Pair(new Run("Black", PageFont.HelveticaBold, 8.8, PageColor.Black, TextRole.Chrome),
                         "Everything recorded: structure, sizes, materials, directions, depths, conditions and notes. \"not recorded\" means nobody recorded it.")
                };
                var textWidth = Width - 2 * pad - key;
                var wrapped = entries.Select(e => FontMetrics.Wrap(e.Value, PageFont.Helvetica, 7.8, textWidth)).ToList();
                var sketchLine = _sketches ? 1 : 0;
                var height = pad + 10 + wrapped.Sum(w => w.Count * 9.4 + 2) + sketchLine * 12 + pad;
                return new Row
                {
                    Height = height,
                    Draw = (page, y) =>
                    {
                        page.Ops.Add(new RectOp { X = Left, Y = y, W = Width, H = height, Width = 0.6, Stroke = PageColor.Gray });
                        Text(page, Left + pad, y + pad + 6, PageFont.HelveticaBold, 6.8, PageColor.Black, "HOW TO READ THESE NOTES", TextRole.Chrome);
                        var ly = y + pad + 17;
                        for (var i = 0; i < entries.Count; i++)
                        {
                            var run = entries[i].Key;
                            Text(page, Left + pad, ly, run.Font, run.Size, run.Color, run.Text, run.Role);
                            foreach (var line in wrapped[i])
                            {
                                Text(page, Left + pad + key, ly, PageFont.Helvetica, 7.8, PageColor.Black, line, TextRole.Chrome);
                                ly += 9.4;
                            }
                            ly += 2;
                        }
                        if (_sketches)
                        {
                            var x = Left + pad;
                            Line(page, x, ly - 2.5, x + 22, ly - 2.5, 1.2, PageColor.Black, null);
                            x += 26;
                            x += Text(page, x, ly, PageFont.Helvetica, 7.8, PageColor.Black, "direction recorded in the field", TextRole.Chrome) + 14;
                            Line(page, x, ly - 2.5, x + 22, ly - 2.5, 1.2, PageColor.Black, new double[] { 3, 2 });
                            x += 26;
                            x += Text(page, x, ly, PageFont.Helvetica, 7.8, PageColor.Black,
                                      "(inferred): direction not observed at the structure", TextRole.Chrome) + 14;
                            Text(page, x, ly, PageFont.HelveticaOblique, 7.8, PageColor.Black, "Plan sketches: north up, not to scale.", TextRole.Chrome);
                        }
                    }
                };
            }

            private static KeyValuePair<Run, string> Pair(Run run, string text)
            {
                return new KeyValuePair<Run, string>(run, text);
            }

            // ----------------------------------------------------------- structure

            private IList<Row> StructureRows(StructureNotes s)
            {
                var rows = new List<Row>();
                rows.Add(TitleRow(s, false));
                rows.Add(InfoRow(s));
                rows.Add(TableHeaderRow());
                foreach (var p in s.Pipes) rows.Add(PipeRow(s, p));
                if (s.Pipes.Count == 0)
                    rows.AddRange(Paragraph("No pipes recorded.", PageFont.HelveticaOblique, 8.5, 11, 0, TextRole.Recorded));

                if (s.RawLines.Count > 0)
                {
                    rows.Add(Spacer(3));
                    rows.Add(Caption("FIELD NOTE AS RECORDED"));
                    foreach (var line in s.RawLines)
                        rows.AddRange(Paragraph(line, PageFont.Courier, 8, 9.6, 8, TextRole.Recorded));
                }

                var notes = new List<string>(s.Notes);
                if (notes.Count > 0)
                {
                    rows.Add(Spacer(3));
                    rows.Add(Caption("NOTES"));
                    foreach (var n in notes) rows.AddRange(Paragraph(n, PageFont.Helvetica, 8.5, 10.5, 8, TextRole.Recorded));
                }

                rows.Add(Spacer(3));
                rows.Add(Caption("WARNINGS AND QUALIFICATIONS"));
                if (s.Warnings.Count == 0)
                    rows.AddRange(Paragraph("None from the Dip Builder's checks.", PageFont.HelveticaOblique, 7.8, 9.4, 8, TextRole.Warning));
                foreach (var w in s.Warnings)
                {
                    var prefix = w.Severity == Severity.Error ? "PROBLEM: " : w.Severity == Severity.Info ? "Note: " : string.Empty;
                    rows.AddRange(Paragraph("• " + prefix + w.Text, PageFont.Helvetica, 7.8, 9.4, 8, TextRole.Warning));
                }
                rows.Add(Spacer(5));
                rows.Add(RuleRow(1.2, 12));
                return rows;
            }

            private Row ContinuedRow(StructureNotes s)
            {
                return TitleRow(s, true);
            }

            private Row TitleRow(StructureNotes s, bool continued)
            {
                return new Row
                {
                    Height = continued ? 22 : 26,
                    Draw = (page, y) =>
                    {
                        var x = Left;
                        x += Text(page, x, y + 14, PageFont.HelveticaBold, continued ? 11 : 13, PageColor.Black, s.Label, TextRole.Recorded);
                        if (continued)
                        {
                            Text(page, x + 6, y + 14, PageFont.HelveticaOblique, 9, PageColor.Black, "(continued)", TextRole.Chrome);
                        }
                        else
                        {
                            var kind = (s.TypeName ?? "type " + NotRecorded) + "   ·   " + s.System + (s.System == UtilitySystem.Other ? " utility" : string.Empty);
                            Text(page, x + 10, y + 14, PageFont.Helvetica, 9, PageColor.Black, kind, TextRole.Recorded);
                        }
                        TextRight(page, Right, y + 14, PageFont.Helvetica, 8.5, PageColor.Black,
                                  "POINT " + (s.PointNumber ?? NotRecorded), TextRole.Recorded);
                        Line(page, Left, y + (continued ? 18 : 19), Right, y + (continued ? 18 : 19), 0.7, PageColor.Black, null);
                    }
                };
            }

            private Row InfoRow(StructureNotes s)
            {
                const double labelWidth = 84, lineHeight = 12.4;
                var infoWidth = _sketches ? Width - SketchWidth - 14 : Width;
                var valueWidth = infoWidth - labelWidth;
                var lines = InfoLines(s, valueWidth);
                var infoHeight = lines.Sum(l => l.Value.Count) * lineHeight + 4;
                var height = Math.Max(infoHeight, _sketches ? SketchHeight + 4 : 0) + 8;
                return new Row
                {
                    Height = height,
                    Draw = (page, y) =>
                    {
                        var ly = y + 13;
                        foreach (var line in lines)
                        {
                            Text(page, Left, ly, PageFont.HelveticaBold, 6.8, PageColor.Black, line.Key, TextRole.Chrome);
                            foreach (var runs in line.Value)
                            {
                                Runs(page, Left + labelWidth, ly, runs);
                                ly += lineHeight;
                            }
                        }
                        if (_sketches) Sketch(page, Right - SketchWidth, y + 4, SketchWidth, SketchHeight, s);
                    }
                };
            }

            /// <summary>The structure's facts, label by label. Each value is one or more lines of runs.</summary>
            private List<KeyValuePair<string, List<List<Run>>>> InfoLines(StructureNotes s, double width)
            {
                var lines = new List<KeyValuePair<string, List<List<Run>>>>();
                Action<string, List<Run>> add = (label, runs) =>
                    lines.Add(new KeyValuePair<string, List<List<Run>>>(label, new List<List<Run>> { runs }));
                Action<string, string> addText = (label, text) =>
                {
                    if (text == null) { add(label, new List<Run> { NotRecordedRun(9) }); return; }
                    var wrapped = FontMetrics.Wrap(text, PageFont.Helvetica, 9, width);
                    lines.Add(new KeyValuePair<string, List<List<Run>>>(label,
                        wrapped.Select(w => new List<Run> { new Run(w, PageFont.Helvetica, 9, PageColor.Black, TextRole.Recorded) }).ToList()));
                };

                addText("UTILITY", s.System.ToString() + (s.System == UtilitySystem.Other ? " / unknown" : string.Empty));
                var type = s.TypeName != null ? s.TypeName + " (" + s.Code + ")" : s.Code;
                if (s.TypeSetByDrafter && !string.IsNullOrEmpty(s.FieldCode) && !string.Equals(s.FieldCode, s.Code, StringComparison.OrdinalIgnoreCase))
                    type += "; set by the drafter, field code " + s.FieldCode;
                addText("STRUCTURE", type);
                addText("DESCRIPTION", s.Description);

                if (s.RimText != null)
                    add("RIM ELEVATION", new List<Run>
                    {
                        new Run(s.RimText, PageFont.HelveticaBold, 9, PageColor.Black, TextRole.Recorded),
                        new Run("   surveyed point " + (s.PointNumber ?? "?"), PageFont.Helvetica, 7.5, PageColor.Black, TextRole.Recorded)
                    });
                else addText("RIM ELEVATION", null);

                if (s.Northing.HasValue && s.Easting.HasValue)
                    addText("POSITION", "N " + s.Northing.Value.ToString("0.00", CultureInfo.InvariantCulture) +
                                        "   E " + s.Easting.Value.ToString("0.00", CultureInfo.InvariantCulture));

                if (s.Shape != StructureShape.NoSize)
                    addText(s.Shape == StructureShape.Rectangular ? "INSIDE SIZE" : "DIAMETER",
                            s.InsideSize != null ? s.InsideSize + (s.InsideSizeSource != null ? "  (" + s.InsideSizeSource + ")" : string.Empty) : null);

                add("BOTTOM", DepthRuns(s.BottomDipText, "BOT", s.BottomText, NotRecorded));
                var dry = s.Conditions.Any(c => string.Equals(c, "DRY", StringComparison.OrdinalIgnoreCase));
                add("WATER", s.WaterDipText == null && dry
                    ? new List<Run> { new Run("DRY (no water noted)", PageFont.Helvetica, 9, PageColor.Black, TextRole.Recorded) }
                    : DepthRuns(s.WaterDipText, "WL", s.WaterText, NotRecorded));

                var conditions = s.Conditions.Where(c => !(dry && s.WaterDipText == null && string.Equals(c, "DRY", StringComparison.OrdinalIgnoreCase))).ToList();
                if (conditions.Count > 0) addText("CONDITIONS", string.Join(", ", conditions.ToArray()));

                var ins = s.Pipes.Count(p => p.Role == FlowRole.In);
                var outs = s.Pipes.Count(p => p.Role == FlowRole.Out);
                var unknown = s.Pipes.Count - ins - outs;
                var config = s.Pipes.Count.ToString(CultureInfo.InvariantCulture) + (s.Pipes.Count == 1 ? " pipe" : " pipes");
                var parts = new List<string>();
                if (ins > 0) parts.Add(ins.ToString(CultureInfo.InvariantCulture) + " in");
                if (outs > 0) parts.Add(outs.ToString(CultureInfo.InvariantCulture) + " out");
                if (unknown > 0) parts.Add(unknown.ToString(CultureInfo.InvariantCulture) + " flow not noted");
                if (parts.Count > 0) config += ": " + string.Join(", ", parts.ToArray());
                addText("PIPES", config);
                return lines;
            }

            /// <summary>"MD 7.82   BOT 117.61": the measured depth black, the elevation it gives red.</summary>
            private static List<Run> DepthRuns(string dip, string label, string elevation, string missing)
            {
                if (dip == null) return new List<Run> { new Run(missing, PageFont.HelveticaOblique, 9, PageColor.Black, TextRole.Recorded) };
                var runs = new List<Run> { new Run("MD " + dip, PageFont.Helvetica, 9, PageColor.Black, TextRole.MeasuredDepth) };
                if (elevation != null)
                    runs.Add(new Run("     " + label + " " + elevation, PageFont.Helvetica, 9, PageColor.Red, TextRole.CalculatedElevation));
                return runs;
            }

            // --------------------------------------------------------------- pipes

            private Row TableHeaderRow()
            {
                return new Row
                {
                    Height = 17,
                    Draw = (page, y) =>
                    {
                        var by = y + 10;
                        const double size = 6.6;
                        Text(page, Left + ColPipe, by, PageFont.HelveticaBold, size, PageColor.Black, "PIPE", TextRole.Chrome);
                        Text(page, Left + ColFlow, by, PageFont.HelveticaBold, size, PageColor.Black, "FLOW", TextRole.Chrome);
                        Text(page, Left + ColSize, by, PageFont.HelveticaBold, size, PageColor.Black, "SIZE", TextRole.Chrome);
                        Text(page, Left + ColMaterial, by, PageFont.HelveticaBold, size, PageColor.Black, "MATERIAL", TextRole.Chrome);
                        Text(page, Left + ColDirection, by, PageFont.HelveticaBold, size, PageColor.Black, "DIRECTION", TextRole.Chrome);
                        TextRight(page, Left + ColMdRight, by, PageFont.HelveticaBold, size, PageColor.Black, "MD (FT)", TextRole.Chrome);
                        Text(page, Left + ColMeasuredTo, by, PageFont.HelveticaBold, size, PageColor.Black, "MEASURED TO", TextRole.Chrome);
                        Text(page, Left + ColElevation, by, PageFont.HelveticaBold, size, PageColor.Black, "CALC. ELEV. (FT)", TextRole.Chrome);
                        Text(page, Left + ColConnects, by, PageFont.HelveticaBold, size, PageColor.Black, "CONNECTS TO", TextRole.Chrome);
                        Line(page, Left, y + 13.5, Right, y + 13.5, 0.6, PageColor.Black, null);
                    }
                };
            }

            private Row PipeRow(StructureNotes s, PipeNotes p)
            {
                const double main = 8.8, detail = 7.3, mainLine = 10.6, detailLine = 9;
                var connects = FontMetrics.Wrap(p.ConnectsTo ?? NotRecorded, PageFont.Helvetica, 8, Width - ColConnects - 2);
                var details = FontMetrics.Wrap(DetailText(p), PageFont.Helvetica, detail, Width - ColFlow - 4);
                var showRaw = p.RawText != null && s.RawLines.Count == 0;
                var mainLines = Math.Max(1, connects.Count);
                var height = 4 + mainLines * mainLine + details.Count * detailLine + (showRaw ? detailLine : 0) + 4;

                return new Row
                {
                    Height = height,
                    IsPipe = true,
                    Draw = (page, y) =>
                    {
                        var by = y + 4 + 8;
                        Text(page, Left + ColPipe, by, PageFont.HelveticaBold, main, PageColor.Black, p.Key, TextRole.Recorded);
                        Text(page, Left + ColFlow, by, PageFont.Helvetica, main, PageColor.Black,
                             p.Role == FlowRole.In ? "IN" : p.Role == FlowRole.Out ? "OUT" : "—", TextRole.Recorded);

                        if (p.Size != null)
                            Text(page, Left + ColSize, by, PageFont.Helvetica, main, PageColor.Black, p.Size + (p.CarriedOver.Contains("size") ? "†" : string.Empty), TextRole.Recorded);
                        else NotRecordedText(page, Left + ColSize, by, main);

                        if (p.Material != null)
                            Text(page, Left + ColMaterial, by, PageFont.Helvetica, main, PageColor.Black,
                                 Fit(p.Material, PageFont.Helvetica, main, ColDirection - ColMaterial - 6) + (p.CarriedOver.Contains("material") ? "†" : string.Empty), TextRole.Recorded);
                        else NotRecordedText(page, Left + ColMaterial, by, main);

                        if (p.Direction != null)
                            Text(page, Left + ColDirection, by, PageFont.Helvetica, main, PageColor.Black,
                                 Fit(p.Direction, PageFont.Helvetica, main, ColMdRight - ColDirection - 40) + (p.CarriedOver.Contains("direction") ? "†" : string.Empty), TextRole.Recorded);
                        else NotRecordedText(page, Left + ColDirection, by, main);

                        if (p.MeasuredDipText != null)
                            TextRight(page, Left + ColMdRight, by, PageFont.Helvetica, main, PageColor.Black, p.MeasuredDipText, TextRole.MeasuredDepth);
                        else
                            TextRight(page, Left + ColMdRight, by, PageFont.HelveticaOblique, 7.6, PageColor.Black, "not dipped", TextRole.MeasuredDepth);

                        Text(page, Left + ColMeasuredTo, by, PageFont.Helvetica, 8, PageColor.Black,
                             p.MeasuredDipText != null ? Fit(p.ReferenceWords, PageFont.Helvetica, 8, ColElevation - ColMeasuredTo - 4) : "—", TextRole.Recorded);

                        if (p.ElevationText != null)
                        {
                            var invert = p.ElevationKind == ElevationKind.Invert;
                            Text(page, Left + ColElevation, by, invert ? PageFont.HelveticaBold : PageFont.Helvetica, main, PageColor.Red,
                                 p.ElevationLabel + " " + p.ElevationText, invert ? TextRole.CalculatedInvert : TextRole.CalculatedElevation);
                        }
                        else Text(page, Left + ColElevation, by, PageFont.Helvetica, main, PageColor.Black, "—", TextRole.Recorded);

                        for (var i = 0; i < connects.Count; i++)
                            Text(page, Left + ColConnects, by + i * mainLine, PageFont.Helvetica, 8, PageColor.Black, connects[i], TextRole.Recorded);

                        var dy = y + 4 + mainLines * mainLine + 6.5;
                        foreach (var line in details)
                        {
                            Text(page, Left + ColFlow, dy, PageFont.Helvetica, detail, PageColor.Black, line, TextRole.Recorded);
                            dy += detailLine;
                        }
                        if (showRaw)
                        {
                            var x = Left + ColFlow + Text(page, Left + ColFlow, dy, PageFont.Helvetica, detail, PageColor.Black, "As recorded: ", TextRole.Chrome);
                            Text(page, x, dy, PageFont.Courier, detail + 0.4, PageColor.Black, p.RawText, TextRole.Recorded);
                        }
                        Line(page, Left, y + height - 0.5, Right, y + height - 0.5, 0.3, PageColor.Gray, new double[] { 1, 1.5 });
                    }
                };
            }

            /// <summary>The pipe's qualifications in sentences: what the dip was taken to and on whose word, why
            /// there is no invert, conditions, notes, values carried over from the other end, and the slope.</summary>
            private static string DetailText(PipeNotes p)
            {
                var parts = new List<string>();
                if (p.MeasuredDipText != null)
                    parts.Add("Measured to " + p.ReferenceWords.ToLowerInvariant() + " (" + p.ReferenceBasisWords + ").");
                if (p.InvertNote != null) parts.Add(p.InvertNote);
                if (p.Shape != null) parts.Add("Shape " + p.Shape + ".");
                if (p.MeasuredDipText == null && p.Conditions.Count > 0)
                    parts.Add("Not dipped: " + string.Join(", ", p.Conditions.ToArray()) + ".");
                else if (p.Conditions.Count > 0)
                    parts.Add("Condition: " + string.Join(", ", p.Conditions.ToArray()) + ".");
                if (p.Notes != null) parts.Add("Note: " + p.Notes + (p.Notes.EndsWith(".", StringComparison.Ordinal) ? string.Empty : "."));
                if (p.CarriedOver.Count > 0)
                    parts.Add("† " + string.Join(", ", p.CarriedOver.ToArray()) + " carried over from " +
                              (string.IsNullOrEmpty(p.CarriedOverFrom) ? "the connected pipe" : p.CarriedOverFrom) +
                              "; not observed at this structure.");
                if (p.Slope != null) parts.Add(p.Slope);
                return string.Join("  ", parts.ToArray());
            }

            // -------------------------------------------------------------- sketch

            private void Sketch(LayoutPage page, double x0, double y0, double w, double h, StructureNotes s)
            {
                var sketch = s.Sketch ?? new StructureSketch();
                // Field-book graph paper behind it.
                for (var gx = x0 + 10; gx < x0 + w - 0.5; gx += 10) Line(page, gx, y0, gx, y0 + h, 0.25, PageColor.Grid, null);
                for (var gy = y0 + 10; gy < y0 + h - 0.5; gy += 10) Line(page, x0, gy, x0 + w, gy, 0.25, PageColor.Grid, null);
                page.Ops.Add(new RectOp { X = x0, Y = y0, W = w, H = h, Width = 0.6, Stroke = PageColor.Gray });
                Text(page, x0 + 4, y0 + 9, PageFont.HelveticaBold, 6.2, PageColor.Black, "PLAN SKETCH", TextRole.Chrome);
                Text(page, x0 + 4, y0 + h - 4, PageFont.Helvetica, 5.6, PageColor.Black, "NOT TO SCALE", TextRole.Chrome);

                var cx = x0 + w / 2;
                var cy = y0 + h / 2 + 3;
                var taken = new List<double[]>();                         // x, y, w, h of everything placed
                taken.Add(new[] { x0, y0, 52.0, 12.0 });                 // the title
                taken.Add(new[] { x0, y0 + h - 11, 44.0, 11.0 });        // "not to scale"

                // The structure: a circle for a round structure, a square for a rectangular one, a dot for a fitting.
                double edge;
                if (sketch.Shape == StructureShape.Rectangular)
                {
                    const double half = 11;
                    page.Ops.Add(new RectOp { X = cx - half, Y = cy - half, W = 2 * half, H = 2 * half, Width = 1.1, Stroke = PageColor.Black, Fill = PageColor.White });
                    edge = half;
                }
                else if (sketch.Shape == StructureShape.NoSize)
                {
                    page.Ops.Add(new CircleOp { X = cx, Y = cy, Radius = 3.5, Width = 0.8, Fill = PageColor.Black });
                    edge = 3.5;
                }
                else
                {
                    page.Ops.Add(new CircleOp { X = cx, Y = cy, Radius = 12, Width = 1.1, Stroke = PageColor.Black, Fill = PageColor.White });
                    edge = 12;
                }
                if (sketch.Shape != StructureShape.NoSize && !string.IsNullOrEmpty(sketch.Code) &&
                    FontMetrics.Width(PageFont.HelveticaBold, 5.2, sketch.Code) < 2 * edge - 2)
                    TextCenter(page, cx, cy + 1.9, PageFont.HelveticaBold, 5.2, PageColor.Black, sketch.Code, TextRole.Recorded);
                taken.Add(new[] { cx - edge - 2, cy - edge - 2, 2 * edge + 4, 2 * edge + 4 });

                if (sketch.HasNorthArrow)
                {
                    var nx = x0 + w - 12;
                    Line(page, nx, y0 + 26, nx, y0 + 11, 0.8, PageColor.Black, null);
                    Polygon(page, PageColor.Black, nx, y0 + 6, nx - 3, y0 + 13, nx + 3, y0 + 13);
                    TextCenter(page, nx, y0 + 34, PageFont.HelveticaBold, 7, PageColor.Black, "N", TextRole.Chrome);
                    taken.Add(new[] { nx - 7, y0 + 3, 14.0, 34.0 });
                }

                // Pipes observed in nearly the same direction sit side by side rather than on top of each other.
                var ordered = sketch.Pipes.ToList();
                var offsets = new double[ordered.Count];
                for (var i = 0; i < ordered.Count; i++)
                {
                    var same = ordered.Where(o => ConnectionFinder.AngleBetween(o.AzimuthDegrees, ordered[i].AzimuthDegrees) < 9).ToList();
                    var k = same.IndexOf(ordered[i]);
                    var step = same.Count > 1 ? Math.Min(6.0, 20.0 / (same.Count - 1)) : 0;   // never wider than the structure
                    offsets[i] = (k - (same.Count - 1) / 2.0) * step;
                }

                const double ray = 46;
                for (var i = 0; i < ordered.Count; i++)
                {
                    var pipe = ordered[i];
                    var az = pipe.AzimuthDegrees * Math.PI / 180.0;
                    var ux = Math.Sin(az);
                    var uy = -Math.Cos(az);                               // page y runs down; north is up
                    var px = -uy * offsets[i];                            // perpendicular shift
                    var py = ux * offsets[i];
                    var start = sketch.Shape == StructureShape.Rectangular ? edge / Math.Max(Math.Abs(ux), Math.Abs(uy)) : edge;
                    if (Math.Abs(offsets[i]) > 0 && sketch.Shape != StructureShape.Rectangular)
                        start = Math.Sqrt(Math.Max(0, edge * edge - offsets[i] * offsets[i]));
                    var sx = cx + px + ux * start;
                    var sy = cy + py + uy * start;
                    var ex = cx + px + ux * ray;
                    var ey = cy + py + uy * ray;
                    Line(page, sx, sy, ex, ey, 1.3, PageColor.Black, pipe.Inferred ? new double[] { 3, 2 } : null);

                    // Flow arrow halfway along: toward the structure for IN, away for OUT.
                    if (pipe.Role != FlowRole.Unknown)
                    {
                        var d = pipe.Role == FlowRole.Out ? 1.0 : -1.0;
                        var mx = cx + px + ux * (start + (ray - start) * 0.55);
                        var my = cy + py + uy * (start + (ray - start) * 0.55);
                        var tx = mx + ux * d * 3.6;
                        var ty = my + uy * d * 3.6;
                        var bx = mx - ux * d * 3.6;
                        var bby = my - uy * d * 3.6;
                        Polygon(page, PageColor.Black, tx, ty, bx - uy * 2.7, bby + ux * 2.7, bx + uy * 2.7, bby - ux * 2.7);
                    }

                    PipeLabel(page, x0, y0, w, h, cx + px + ux * (ray + 4), cy + py + uy * (ray + 4), ux, uy, pipe, taken);
                }

                if (sketch.NotDrawn.Count > 0)
                    TextRight(page, x0 + w - 4, y0 + h - 4, PageFont.HelveticaOblique, 5.8, PageColor.Black,
                              "Not drawn, no direction recorded: " + string.Join(", ", sketch.NotDrawn.ToArray()), TextRole.Recorded);
            }

            private void PipeLabel(LayoutPage page, double x0, double y0, double w, double h, double ax, double ay,
                                   double ux, double uy, SketchPipe pipe, List<double[]> taken)
            {
                var lines = new List<Run> { new Run(pipe.Caption, PageFont.Helvetica, 6.2, PageColor.Black, TextRole.Recorded) };
                if (pipe.ElevationText != null)
                    lines.Add(new Run(pipe.ElevationLabel + " " + pipe.ElevationText, PageFont.HelveticaBold, 6.2, PageColor.Red,
                                      pipe.ElevationLabel == "INV" ? TextRole.CalculatedInvert : TextRole.CalculatedElevation));
                if (pipe.ConnectsTo != null) lines.Add(new Run("to " + pipe.ConnectsTo, PageFont.Helvetica, 5.8, PageColor.Black, TextRole.Recorded));
                if (pipe.Inferred) lines.Add(new Run("(inferred)", PageFont.HelveticaOblique, 5.8, PageColor.Black, TextRole.Recorded));

                const double lineHeight = 6.9;
                var bw = lines.Max(l => l.Width);
                var bh = lines.Count * lineHeight;
                var bx = ux > 0.35 ? ax : ux < -0.35 ? ax - bw : ax - bw / 2;
                var by = uy > 0.35 ? ay : uy < -0.35 ? ay - bh : ay - bh / 2;

                // Keep it inside the sketch and clear of everything already placed.
                double[] box = null;
                for (var attempt = 0; attempt < 12; attempt++)
                {
                    // First further out along the pipe, then alternately above and below.
                    var step = attempt - 3;
                    var tx = bx + (attempt < 4 ? ux * attempt * 5 : 0);
                    var ty = by + (attempt < 4 ? uy * attempt * 5 : ((step + 1) / 2) * 7.5 * (step % 2 == 1 ? -1 : 1));
                    tx = Math.Max(x0 + 3, Math.Min(x0 + w - 3 - bw, tx));
                    ty = Math.Max(y0 + 3, Math.Min(y0 + h - 3 - bh, ty));
                    var candidate = new[] { tx, ty, bw, bh };
                    if (box == null) box = candidate;
                    if (!taken.Any(t => Overlap(t, candidate))) { box = candidate; break; }
                }
                taken.Add(box);
                for (var i = 0; i < lines.Count; i++)
                {
                    var run = lines[i];
                    var lx = ux > 0.35 ? box[0] : ux < -0.35 ? box[0] + bw - run.Width : box[0] + (bw - run.Width) / 2;
                    Text(page, lx, box[1] + (i + 1) * lineHeight - 1.6, run.Font, run.Size, run.Color, run.Text, run.Role);
                }
            }

            private static bool Overlap(double[] a, double[] b)
            {
                return a[0] < b[0] + b[2] && b[0] < a[0] + a[2] && a[1] < b[1] + b[3] && b[1] < a[1] + a[3];
            }

            // ------------------------------------------------------------- helpers

            private Row Caption(string text)
            {
                return new Row
                {
                    Height = 12,
                    Draw = (page, y) => Text(page, Left, y + 9, PageFont.HelveticaBold, 6.8, PageColor.Black, text, TextRole.Chrome)
                };
            }

            private static Row Spacer(double height)
            {
                return new Row { Height = height, Draw = (page, y) => { } };
            }

            private static Row RuleRow(double width, double gap)
            {
                return new Row
                {
                    Height = gap,
                    Draw = (page, y) => Line(page, Left, y + 2, Right, y + 2, width, PageColor.Black, null)
                };
            }

            /// <summary>Wrapped text, one row per line so a long paragraph can break across pages.</summary>
            private IList<Row> Paragraph(string text, PageFont font, double size, double lineHeight, double indent, TextRole role)
            {
                var rows = new List<Row>();
                var x = Left + indent;
                foreach (var line in FontMetrics.Wrap(text, font, size, Width - indent))
                {
                    var l = line;
                    rows.Add(new Row { Height = lineHeight, Draw = (page, y) => Text(page, x, y + lineHeight - 2.5, font, size, PageColor.Black, l, role) });
                }
                return rows;
            }

            private static Run Value(string text, double size)
            {
                return text == null ? NotRecordedRun(size) : new Run(text, PageFont.Helvetica, size, PageColor.Black, TextRole.Recorded);
            }

            private static Run NotRecordedRun(double size)
            {
                return new Run(NotRecorded, PageFont.HelveticaOblique, size, PageColor.Black, TextRole.Recorded);
            }

            private static void NotRecordedText(LayoutPage page, double x, double y, double size)
            {
                Text(page, x, y, PageFont.HelveticaOblique, Math.Min(size, 7.6), PageColor.Black, NotRecorded, TextRole.Recorded);
            }

            private static double Runs(LayoutPage page, double x, double y, IList<Run> runs)
            {
                var start = x;
                foreach (var r in runs) x += Text(page, x, y, r.Font, r.Size, r.Color, r.Text, r.Role);
                return x - start;
            }

            /// <summary>The text, shortened with "..." to fit the width.</summary>
            private static string Fit(string text, PageFont font, double size, double width)
            {
                if (text == null || FontMetrics.Width(font, size, text) <= width) return text;
                var t = text;
                while (t.Length > 1 && FontMetrics.Width(font, size, t + "...") > width) t = t.Substring(0, t.Length - 1);
                return t.TrimEnd() + "...";
            }
        }

        // ---------------------------------------------------------- primitives

        private static double Text(LayoutPage page, double x, double y, PageFont font, double size, PageColor color, string text, TextRole role)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            page.Ops.Add(new TextOp { X = x, Y = y, Font = font, Size = size, Color = color, Text = text, Role = role });
            return FontMetrics.Width(font, size, text);
        }

        private static void TextRight(LayoutPage page, double right, double y, PageFont font, double size, PageColor color, string text, TextRole role)
        {
            Text(page, right - FontMetrics.Width(font, size, text), y, font, size, color, text, role);
        }

        private static void TextCenter(LayoutPage page, double center, double y, PageFont font, double size, PageColor color, string text, TextRole role)
        {
            Text(page, center - FontMetrics.Width(font, size, text) / 2, y, font, size, color, text, role);
        }

        private static void Line(LayoutPage page, double x1, double y1, double x2, double y2, double width, PageColor color, double[] dash)
        {
            page.Ops.Add(new LineOp { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Width = width, Color = color, Dash = dash });
        }

        private static void Polygon(LayoutPage page, PageColor fill, params double[] points)
        {
            var op = new PolygonOp { Fill = fill };
            foreach (var p in points) op.Points.Add(p);
            page.Ops.Add(op);
        }
    }
}
