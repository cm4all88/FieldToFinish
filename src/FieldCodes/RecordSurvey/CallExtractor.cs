using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace FieldCodes.RecordSurvey
{
    public sealed class ExtractionOptions
    {
        /// <summary>Calls under this confidence (0..1) need review before geometry is built.</summary>
        public double ReviewThreshold { get; set; }
        /// <summary>How many digit-confusion alternatives to offer for a low-confidence number.</summary>
        public int MaxAlternatives { get; set; }
        /// <summary>How far, in line heights, a bearing looks for a distance written on a separate line.</summary>
        public double PairingReach { get; set; }

        public ExtractionOptions()
        {
            ReviewThreshold = 0.85;
            MaxAlternatives = 2;
            PairingReach = 3.0;
        }
    }

    /// <summary>
    /// Turns positioned document text into survey calls, figures, references, monuments and
    /// notes. It reads what is written and records how sure it is; it never fills a gap with
    /// a guess. A bearing with no distance stays a bearing with no distance, flagged.
    /// </summary>
    public static class CallExtractor
    {
        private sealed class Fragment
        {
            public SurveyToken Bearing;
            public SurveyToken Distance;
            public string Tag = string.Empty;
            public ClassifiedLine Line;
        }

        public static RecordSurveyProject Extract(DocumentText text, ExtractionOptions options)
        {
            options = options ?? new ExtractionOptions();
            var project = new RecordSurveyProject { ReviewThreshold = options.ReviewThreshold };
            if (text == null) return project;
            project.Document.Path = text.DocumentPath;
            project.Document.Pages = text.Pages.Count;
            project.OcrPath = null;

            // A sidecar written pass by pass (one set of lines per rotation) merges here exactly as
            // the Windows reader merges its own passes; a merged document is unchanged by it.
            var classified = new List<ClassifiedLine>();
            foreach (var page in text.Pages)
            {
                if (page.Lines.Select(l => l.PassRotationDegrees).Distinct().Count() > 1)
                    page.Lines = PageGeometry.Merge(page.Lines);
                foreach (var line in page.Lines)
                    classified.Add(EntityClassifier.Classify(line, page.Number));
            }

            ReadDocumentInfo(project, classified);
            ReadReferences(project, classified);
            ReadFigures(project, classified);
            ReadCourses(project, classified, options);
            ReadCurves(project, classified, options);
            ReadMonuments(project, classified);
            ReadAnnotations(project, classified);

            foreach (var call in project.Calls) Classify(call, options);
            foreach (var m in project.Monuments) m.ReviewStatus = m.Confidence >= options.ReviewThreshold ? CallStatus.Extracted : CallStatus.NeedsReview;

            var prose = project.Calls.Where(c => c.Figure == "Description").OrderBy(c => c.Source != null ? c.Source.Page : 0).ThenBy(c => c.Source != null && c.Source.Box != null ? c.Source.Box.Y : 0).ThenBy(c => c.Source != null && c.Source.Box != null ? c.Source.Box.X : 0).ToList();
            if (prose.Count > 0)
            {
                for (var i = 0; i < prose.Count; i++) prose[i].Order = i + 1;
                project.Figures.Add(new SurveyFigure { Name = "Description", Kind = "Description", Closed = false });
                project.Warnings.Add(prose.Count + " call(s) were read from the legal description text; they are grouped as the open figure \"Description\" in reading order, separate from the plan.");
            }
            return project;
        }

        // ------------------------------------------------------------ document

        private static void ReadDocumentInfo(RecordSurveyProject project, List<ClassifiedLine> lines)
        {
            var doc = project.Document;
            doc.SurveyType = EntityClassifier.SurveyTypeFrom(lines);
            // The title is the biggest title-ish text on the sheet, not the first description sentence.
            var title = lines.Where(l => l.Kind == SurveyEntityKind.SurveyTitle && l.Line.Box != null).OrderByDescending(l => l.Line.Box.Height).FirstOrDefault();
            if (title != null) doc.Title = title.Line.Text.Trim();
            var scale = lines.Select(EntityClassifier.ScaleOf).FirstOrDefault(s => s.HasValue);
            if (scale.HasValue) doc.ScaleFeetPerInch = scale;
            var sheet = lines.FirstOrDefault(l => l.Kind == SurveyEntityKind.SheetNumber);
            if (sheet != null) doc.Sheet = sheet.Key;
            var basis = lines.FirstOrDefault(l => l.Kind == SurveyEntityKind.BasisOfBearing);
            if (basis != null) doc.BasisOfBearing = basis.Line.Text.Trim();
            var surveyor = lines.FirstOrDefault(l => l.Kind == SurveyEntityKind.Surveyor);
            if (surveyor != null) doc.Surveyor = surveyor.Line.Text.Trim();
            // The county named most often (OCR mangles it now and then), four letters or more.
            var county = lines.Select(l => Regex.Match(l.Line.Text ?? string.Empty, @"\b([A-Z]{4,})\s+COUNTY\b", RegexOptions.IgnoreCase))
                              .Where(m => m.Success).GroupBy(m => m.Groups[1].Value.ToUpperInvariant())
                              .OrderByDescending(g => g.Count()).FirstOrDefault();
            if (county != null) doc.County = county.Key + " COUNTY";

            // The document's own recording number: a reference line with no R-key, nearest the
            // title. Keyed lines (R1: AFN ...) are the surveys it cites.
            var own = lines.Where(l => l.Kind == SurveyEntityKind.RecordReference && !IsKeyed(l.Key)).ToList();
            if (own.Count > 0)
            {
                var r = EntityClassifier.ReferenceOf(own[0]);
                doc.RecordingNumber = r.RecordingNumber;
                if (r.Volume != null) doc.VolumePage = "VOL " + r.Volume + " PG " + r.Page + (r.Kind != null ? " (" + r.Kind + ")" : string.Empty);
            }
        }

        private static bool IsKeyed(string key)
        {
            return key != null && Regex.IsMatch(key, @"^R\d{1,2}$");
        }

        private static void ReadReferences(RecordSurveyProject project, List<ClassifiedLine> lines)
        {
            foreach (var l in lines.Where(x => x.Kind == SurveyEntityKind.RecordReference || x.Kind == SurveyEntityKind.RecordSourceKey))
            {
                var r = EntityClassifier.ReferenceOf(l);
                if (string.IsNullOrEmpty(r.Id))
                {
                    // An un-keyed reference other than the document's own is still kept, as "REF n".
                    if (l == lines.FirstOrDefault(x => x.Kind == SurveyEntityKind.RecordReference && !IsKeyed(x.Key))) continue;
                    r.Id = "REF" + (project.References.Count(x => x.Id.StartsWith("REF", StringComparison.Ordinal)) + 1).ToString(CultureInfo.InvariantCulture);
                }
                if (project.FindReference(r.Id) != null)
                {
                    project.Warnings.Add("Record source " + r.Id + " is defined more than once on the document; the first definition is used.");
                    continue;
                }
                project.References.Add(r);
            }
        }

        private static void ReadFigures(RecordSurveyProject project, List<ClassifiedLine> lines)
        {
            foreach (var l in lines.Where(x => x.Kind == SurveyEntityKind.LotNumber))
            {
                var parts = (l.Key ?? string.Empty).Split('/');
                var lot = parts[0];
                var block = parts.Length > 1 ? parts[1] : null;
                var name = "Lot " + lot + (block != null ? " Block " + block : string.Empty);
                if (project.Figures.Any(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase))) continue;
                project.Figures.Add(new SurveyFigure { Name = name, Kind = "Lot", Lot = lot, Block = block, Source = l.Source, PageHint = l.Line.Box });
            }
            foreach (var l in lines.Where(x => x.Kind == SurveyEntityKind.Tract))
            {
                var name = "Tract " + l.Key;
                if (project.Figures.Any(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase))) continue;
                project.Figures.Add(new SurveyFigure { Name = name, Kind = "Tract", Lot = l.Key, Source = l.Source, PageHint = l.Line.Box });
            }
        }

        // ------------------------------------------------------------ courses

        private static void ReadCourses(RecordSurveyProject project, List<ClassifiedLine> lines, ExtractionOptions options)
        {
            var fragments = new List<Fragment>();
            var loneDistances = lines.Where(l => l.Kind == SurveyEntityKind.Distance || l.Kind == SurveyEntityKind.Number).ToList();
            var usedDistances = new HashSet<ClassifiedLine>();

            foreach (var l in lines.Where(x => x.Kind == SurveyEntityKind.BearingDistance || x.Kind == SurveyEntityKind.Bearing || x.Kind == SurveyEntityKind.LineTableRow))
            {
                var lineFragments = FragmentsOf(l);

                // A bearing written on its own line: its distance is usually the next line down
                // (stacked annotation). Pair with the nearest unused lone distance within reach,
                // reading in the same direction; say so in the confidence.
                foreach (var f in lineFragments.Where(x => x.Bearing != null && x.Distance == null))
                {
                    if (l.InProse) continue;
                    var reach = options.PairingReach * Math.Max(1.0, l.Line.Box.Height);
                    var candidate = loneDistances
                        .Where(d => !usedDistances.Contains(d) && d.Page == l.Page && Math.Abs(PageGeometry.Normalize(d.Line.Box.RotationDegrees - l.Line.Box.RotationDegrees)) < 15.0)
                        .Select(d => new { d, dist = PageGeometry.CentreDistance(d.Line.Box, l.Line.Box) })
                        .Where(x => x.dist <= reach)
                        .OrderBy(x => x.dist)
                        .FirstOrDefault();
                    if (candidate == null) continue;
                    usedDistances.Add(candidate.d);
                    f.Distance = candidate.d.Tokens.First(t => t.Kind == SurveyTokenKind.Distance && t.Read != null && t.Read.Ok);
                    f.Distance.Read.Notes.Add("Distance paired from the line below/beside the bearing.");
                    f.Distance.Read.Confidence *= candidate.d.Kind == SurveyEntityKind.Number ? 0.8 : 0.9;
                    if (candidate.d.Kind == SurveyEntityKind.Number) f.Distance.Read.Notes.Add("A bare number with no foot mark; it may be a lot number rather than this course's distance.");
                    var dTag = candidate.d.Tokens.FirstOrDefault(t => t.Kind == SurveyTokenKind.Tag);
                    if (dTag != null && f.Tag.Length == 0) f.Tag = dTag.Name;
                    f.Distance.Read.Raw = candidate.d.Line.Text;
                    // Remember where the distance came from for the source reference.
                    f.Distance.Name = "page:" + candidate.d.Page + ":" + candidate.d.Line.Box.X.ToString("0", CultureInfo.InvariantCulture) + ":" + candidate.d.Line.Box.Y.ToString("0", CultureInfo.InvariantCulture) + ":" +
                                      candidate.d.Line.Box.Width.ToString("0", CultureInfo.InvariantCulture) + ":" + candidate.d.Line.Box.Height.ToString("0", CultureInfo.InvariantCulture) + ":" + candidate.d.Line.EffectiveConfidence.ToString("0.###", CultureInfo.InvariantCulture);
                }
                fragments.AddRange(lineFragments);
            }

            // Fragments on one line that share a bearing (R/M pairs) become one call.
            foreach (var group in fragments.Where(f => f.Bearing != null || f.Distance != null).GroupBy(f => f.Line))
            {
                var list = group.ToList();
                var i = 0;
                while (i < list.Count)
                {
                    var call = new SurveyCall { Id = project.NextCallId() };
                    var first = list[i];
                    Attach(call, first);
                    i++;
                    // Following fragments with a record/measured tag and no bearing of their own, or the
                    // same bearing, belong to the same course.
                    while (i < list.Count && Companion(first, list[i]))
                    {
                        Attach(call, list[i]);
                        i++;
                    }
                    Finish(call, group.Key, options);
                    project.Calls.Add(call);
                }
            }

            MergeStackedRecordAndMeasured(project, options);

            // Bare numbers not used as a distance are kept for the ordering step: one inside a
            // closed loop names the lot.
            foreach (var l in loneDistances.Where(x => x.Kind == SurveyEntityKind.Number && !usedDistances.Contains(x)))
                project.Annotations.Add(new SurveyAnnotation { Kind = SurveyEntityKind.Number, Text = l.Key, Source = l.Source, Confidence = l.Line.EffectiveConfidence });
        }

        private static List<Fragment> FragmentsOf(ClassifiedLine l)
        {
            var result = new List<Fragment>();
            Fragment current = null;
            string pendingTag = null;
            foreach (var t in l.Tokens)
            {
                switch (t.Kind)
                {
                    case SurveyTokenKind.Bearing:
                        if (t.Read == null || !t.Read.Ok) break;
                        if (current != null && current.Bearing != null && current.Distance == null && current.Tag.Length == 0)
                        {
                            // Two bearings in a row (R and M with distances after): keep both fragments.
                        }
                        current = new Fragment { Bearing = t, Line = l };
                        if (pendingTag != null) { current.Tag = pendingTag; pendingTag = null; }
                        result.Add(current);
                        break;
                    case SurveyTokenKind.Distance:
                        if (t.Read == null || !t.Read.Ok) break;
                        if (current == null || current.Distance != null)
                        {
                            // A second distance after one bearing: "N..E 1320.45' (R) 1320.38' (M)" -- a new
                            // fragment sharing the bearing.
                            var shared = current != null ? current.Bearing : null;
                            current = new Fragment { Bearing = shared, Line = l };
                            if (pendingTag != null) { current.Tag = pendingTag; pendingTag = null; }
                            result.Add(current);
                        }
                        current.Distance = t;
                        break;
                    case SurveyTokenKind.Tag:
                        if (current != null && current.Tag.Length == 0 && (current.Bearing != null || current.Distance != null)) current.Tag = t.Name;
                        else pendingTag = t.Name;
                        break;
                    case SurveyTokenKind.LineTag:
                        // "L1 N 45°00'00" E 25.00'": the tag names the course, kept in the tag slot with a prefix.
                        pendingTag = pendingTag ?? string.Empty;
                        break;
                }
            }
            return result;
        }

        private static bool Companion(Fragment first, Fragment next)
        {
            if (next.Bearing == null) return true;                       // a distance-only fragment continues the course
            if (ReferenceEquals(next.Bearing, first.Bearing)) return true;
            if (first.Bearing == null || next.Bearing.Read == null || first.Bearing.Read == null) return false;
            // The record and measured bearings of one course are within a degree of each other and one is tagged.
            var close = Math.Abs(CurveSolver.AngleDiff(first.Bearing.Read.Value, next.Bearing.Read.Value)) < 1.0;
            return close && (next.Tag.Length > 0 || first.Tag.Length > 0);
        }

        private static void Attach(SurveyCall call, Fragment f)
        {
            var tags = (f.Tag ?? string.Empty).Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries);
            if (tags.Length == 0) tags = new[] { string.Empty };
            foreach (var tag in tags)
            {
                CallValue target;
                if (tag == "M") { target = call.Measured; }
                else if (tag == "C") { var rv = new RecordValue { SourceId = "C" }; call.Records.Add(rv); target = rv; call.Basis = ValueBasis.Calculated; }
                else
                {
                    var existing = call.Records.FirstOrDefault(r => r.SourceId == tag && (r.Empty || (f.Bearing == null) != (r.AzimuthDegrees == null)));
                    if (existing == null) { existing = new RecordValue { SourceId = tag }; call.Records.Add(existing); }
                    target = existing;
                }
                if (f.Bearing != null && f.Bearing.Read != null && f.Bearing.Read.Ok)
                {
                    target.AzimuthDegrees = f.Bearing.Read.Value;
                    target.BearingText = f.Bearing.Read.Normalized;
                    target.BearingSource = new SourceRef(f.Line.Page, f.Line.Line.Box, f.Bearing.Text, f.Line.Line.EffectiveConfidence);
                    target.Confidence = Math.Min(target.Confidence, f.Bearing.Read.Confidence * f.Line.Line.EffectiveConfidence);
                }
                if (f.Distance != null && f.Distance.Read != null && f.Distance.Read.Ok)
                {
                    target.DistanceFeet = f.Distance.Read.Value;
                    target.DistanceText = f.Distance.Read.Normalized;
                    target.DistanceSource = SourceOfDistance(f);
                    target.Confidence = Math.Min(target.Confidence, f.Distance.Read.Confidence * target.DistanceSource.OcrConfidence);
                    if (f.Distance.Read.Unit == "m") call.Notes.Add("Distance written in metres; converted to US survey feet.");
                }
            }
        }

        private static SourceRef SourceOfDistance(Fragment f)
        {
            var name = f.Distance.Name;
            if (name != null && name.StartsWith("page:", StringComparison.Ordinal))
            {
                var parts = name.Split(':');
                return new SourceRef(int.Parse(parts[1], CultureInfo.InvariantCulture),
                    new PageBox(double.Parse(parts[2], CultureInfo.InvariantCulture), double.Parse(parts[3], CultureInfo.InvariantCulture),
                                double.Parse(parts[4], CultureInfo.InvariantCulture), double.Parse(parts[5], CultureInfo.InvariantCulture), f.Line.Line.Box.RotationDegrees),
                    f.Distance.Read.Raw, double.Parse(parts[6], CultureInfo.InvariantCulture));
            }
            return new SourceRef(f.Line.Page, f.Line.Line.Box, f.Distance.Text, f.Line.Line.EffectiveConfidence);
        }

        private static void Finish(SurveyCall call, ClassifiedLine line, ExtractionOptions options)
        {
            call.Source = line.Source;
            call.PageHint = line.InProse ? null : line.Line.Box;
            if (line.InProse)
            {
                // Legal description calls are grouped into their own open figure, in reading order, and
                // never ordered from the page: the text block is not where the line is.
                call.Figure = "Description";
                call.Notes.Add("Read from the legal description text, not from a label on the plan.");
            }
            var lineTag = line.Tokens.FirstOrDefault(t => t.Kind == SurveyTokenKind.LineTag);
            if (lineTag != null) call.Notes.Add("Tagged " + lineTag.Name + " on the document.");

            // Values that were never written are empty; a bearing without a distance is reported, not invented.
            var values = new List<CallValue> { call.Measured }.Concat(call.Records.Cast<CallValue>()).Where(v => !v.Empty).ToList();
            if (values.Count == 0) { call.Confidence = 0; call.Notes.Add("No readable bearing or distance."); return; }
            foreach (var v in values)
            {
                if (!v.AzimuthDegrees.HasValue) call.Notes.Add(Describe(call, v) + ": no bearing was read for this value.");
                if (!v.DistanceFeet.HasValue) call.Notes.Add(Describe(call, v) + ": no distance was read for this value.");
            }
            call.Confidence = values.Min(v => v.Confidence);
            if (call.Basis != ValueBasis.Calculated)
                call.Basis = call.Measured != null && !call.Measured.Empty && call.Records.Count == 0 ? ValueBasis.Measured : ValueBasis.Recorded;

            AddAlternatives(call, line, options);
        }

        private static string Describe(SurveyCall call, CallValue v)
        {
            if (ReferenceEquals(v, call.Measured)) return "Measured";
            var r = v as RecordValue;
            return r != null && r.SourceId.Length > 0 ? "Record " + r.SourceId : "Record";
        }

        /// <summary>
        /// Alternatives come from a second OCR pass that read the same spot differently (kept
        /// as "[alt]" words by the merge), and from digit confusions when the reading is under
        /// the threshold. Offered, never applied.
        /// </summary>
        private static void AddAlternatives(SurveyCall call, ClassifiedLine line, ExtractionOptions options)
        {
            foreach (var alt in line.Line.Words.Where(w => (w.Text ?? string.Empty).StartsWith("[alt] ", StringComparison.Ordinal)))
            {
                var text = alt.Text.Substring(6);
                var tokens = SurveyCallParser.Tokenize(text);
                var b = tokens.FirstOrDefault(t => t.Kind == SurveyTokenKind.Bearing && t.Read != null && t.Read.Ok);
                var d = tokens.FirstOrDefault(t => t.Kind == SurveyTokenKind.Distance && t.Read != null && t.Read.Ok);
                foreach (var target in Targets(call))
                {
                    if (b != null && target.Value.AzimuthDegrees.HasValue && Math.Abs(CurveSolver.AngleDiff(b.Read.Value, target.Value.AzimuthDegrees.Value)) * 3600 > 0.5)
                        call.Alternatives.Add(new CallAlternative { Field = "bearing", Text = b.Read.Normalized, Value = b.Read.Value, Confidence = alt.Confidence * b.Read.Confidence, Reason = "second OCR pass", Target = target.Key });
                    if (d != null && target.Value.DistanceFeet.HasValue && Math.Abs(d.Read.Value - target.Value.DistanceFeet.Value) > 1e-6)
                        call.Alternatives.Add(new CallAlternative { Field = "distance", Text = d.Read.Normalized, Value = d.Read.Value, Confidence = alt.Confidence * d.Read.Confidence, Reason = "second OCR pass", Target = target.Key });
                }
            }

            foreach (var target in Targets(call))
            {
                var v = target.Value;
                if (v.DistanceFeet.HasValue && v.Confidence < options.ReviewThreshold)
                {
                    var text = v.DistanceFeet.Value.ToString("0.00", CultureInfo.InvariantCulture);
                    foreach (var a in SurveyCallParser.DigitAlternatives(text, v.Confidence, options.MaxAlternatives))
                        if (!call.Alternatives.Any(x => x.Field == "distance" && Math.Abs(x.Value - a.Value) < 1e-6))
                            call.Alternatives.Add(new CallAlternative { Field = "distance", Text = a.Text + "'", Value = a.Value, Confidence = a.Confidence, Reason = a.Reason, Target = target.Key });
                }
            }
        }

        private static IEnumerable<KeyValuePair<string, CallValue>> Targets(SurveyCall call)
        {
            if (call.Measured != null && !call.Measured.Empty) yield return new KeyValuePair<string, CallValue>("measured", call.Measured);
            foreach (var r in call.Records.Where(r => !r.Empty)) yield return new KeyValuePair<string, CallValue>(r.SourceId ?? string.Empty, r);
        }

        /// <summary>
        /// "R1: N 89°42'18" E 1320.45'" on one line and "M: N 89°42'21" E 1320.38'" on the next are
        /// one course. Calls read from adjacent lines whose bearings agree within a degree, one
        /// carrying only record values and the other only a measured value, are merged.
        /// </summary>
        private static void MergeStackedRecordAndMeasured(RecordSurveyProject project, ExtractionOptions options)
        {
            var merged = true;
            while (merged)
            {
                merged = false;
                for (var i = 0; i < project.Calls.Count && !merged; i++)
                for (var j = i + 1; j < project.Calls.Count && !merged; j++)
                {
                    var a = project.Calls[i];
                    var b = project.Calls[j];
                    if (a.Kind != CallKind.Line || b.Kind != CallKind.Line) continue;
                    if (a.Source == null || b.Source == null || a.Source.Page != b.Source.Page) continue;
                    var aMeasuredOnly = !a.Measured.Empty && a.Records.Count == 0;
                    var bMeasuredOnly = !b.Measured.Empty && b.Records.Count == 0;
                    var aRecordOnly = a.Measured.Empty && a.Records.Count > 0 && a.Records.All(r => r.SourceId.Length > 0 && r.SourceId != "C");
                    var bRecordOnly = b.Measured.Empty && b.Records.Count > 0 && b.Records.All(r => r.SourceId.Length > 0 && r.SourceId != "C");
                    if (!((aMeasuredOnly && bRecordOnly) || (aRecordOnly && bMeasuredOnly))) continue;
                    var reach = options.PairingReach * Math.Max(a.Source.Box.Height, b.Source.Box.Height);
                    if (PageGeometry.CentreDistance(a.Source.Box, b.Source.Box) > reach) continue;
                    var av = aMeasuredOnly ? a.Measured : a.Records[0];
                    var bv = bMeasuredOnly ? b.Measured : b.Records[0];
                    if (!av.AzimuthDegrees.HasValue || !bv.AzimuthDegrees.HasValue) continue;
                    if (Math.Abs(CurveSolver.AngleDiff(av.AzimuthDegrees.Value, bv.AzimuthDegrees.Value)) > 1.0) continue;

                    var keep = aRecordOnly ? a : b;
                    var other = aRecordOnly ? b : a;
                    keep.Measured = other.Measured;
                    keep.Confidence = Math.Min(keep.Confidence, other.Confidence);
                    keep.Source = new SourceRef(keep.Source.Page, keep.Source.Box.Union(other.Source.Box), keep.Source.RawText + " | " + other.Source.RawText, Math.Min(keep.Source.OcrConfidence, other.Source.OcrConfidence));
                    keep.Notes.Add("Record and measured values read from adjacent lines and joined as one course.");
                    keep.Alternatives.AddRange(other.Alternatives);
                    keep.Notes.AddRange(other.Notes);
                    keep.Basis = ValueBasis.Recorded;
                    project.Calls.Remove(other);
                    merged = true;
                }
            }
        }

        // ------------------------------------------------------------ curves

        private static void ReadCurves(RecordSurveyProject project, List<ClassifiedLine> lines, ExtractionOptions options)
        {
            // In-place curve data: "R=250.00' L=197.22' Δ=45°12'10"" on one line, or stacked on
            // adjacent lines. Consecutive CurveData lines within reach form one curve.
            var data = lines.Where(l => l.Kind == SurveyEntityKind.CurveData).OrderBy(l => l.Page).ThenBy(l => l.Line.Box.Y).ThenBy(l => l.Line.Box.X).ToList();
            var groups = new List<List<ClassifiedLine>>();
            foreach (var l in data)
            {
                var group = groups.LastOrDefault();
                if (group != null && group[0].Page == l.Page && PageGeometry.CentreDistance(group[group.Count - 1].Line.Box, l.Line.Box) <= options.PairingReach * Math.Max(1.0, l.Line.Box.Height)
                    && !KeysOverlap(group, l))
                    group.Add(l);
                else
                    groups.Add(new List<ClassifiedLine> { l });
            }

            foreach (var group in groups)
            {
                var call = new SurveyCall { Id = project.NextCallId(), Kind = CallKind.Curve, Curve = new CurveSpec() };
                var confidence = 1.0;
                PageBox box = null;
                foreach (var l in group)
                {
                    box = box == null ? l.Line.Box : box.Union(l.Line.Box);
                    confidence = Math.Min(confidence, l.Line.EffectiveConfidence);
                    ApplyCurveTokens(call, l, ref confidence);
                }
                call.Source = new SourceRef(group[0].Page, box, string.Join(" | ", group.Select(g => g.Line.Text).ToArray()), group.Min(g => g.Line.EffectiveConfidence));
                call.PageHint = box;
                call.Confidence = confidence;
                FinishCurve(call, options);
                project.Calls.Add(call);
            }

            // Curve table rows: "C1 250.00' 45°12'10" 197.22' N 45°12'10" E 192.06'".
            var rows = lines.Where(l => l.Kind == SurveyEntityKind.CurveTableRow).ToList();
            if (rows.Count > 0)
            {
                var header = lines.Where(l => l.Kind == SurveyEntityKind.Legend && Regex.IsMatch(l.Line.Text ?? string.Empty, @"RADIUS|DELTA|LENGTH|CHORD", RegexOptions.IgnoreCase)).Select(l => HeaderColumns(l.Line.Text)).FirstOrDefault(h => h != null && h.Count >= 2);
                foreach (var row in rows)
                {
                    var call = new SurveyCall { Id = project.NextCallId(), Kind = CallKind.Curve, Curve = new CurveSpec { Tag = row.Key } };
                    call.Source = row.Source;
                    call.Confidence = row.Line.EffectiveConfidence;
                    ReadCurveRow(call, row, header, options);
                    // The tag on the drawing ("C1" on its own) says where the curve is.
                    var tagOnPlan = lines.FirstOrDefault(l => l.Kind == SurveyEntityKind.Other && l.Tokens.Count == 1 && l.Tokens[0].Kind == SurveyTokenKind.CurveTag && l.Tokens[0].Name == row.Key);
                    if (tagOnPlan != null) call.PageHint = tagOnPlan.Line.Box;
                    FinishCurve(call, options);
                    project.Calls.Add(call);
                }
            }
        }

        private static bool KeysOverlap(List<ClassifiedLine> group, ClassifiedLine next)
        {
            var have = new HashSet<string>(group.SelectMany(g => g.Tokens.Where(t => t.Kind == SurveyTokenKind.CurveKey).Select(t => t.Name)));
            return next.Tokens.Any(t => t.Kind == SurveyTokenKind.CurveKey && have.Contains(t.Name));
        }

        private static void ApplyCurveTokens(SurveyCall call, ClassifiedLine l, ref double confidence)
        {
            var c = call.Curve;
            var tokens = l.Tokens;
            for (var i = 0; i < tokens.Count; i++)
            {
                var t = tokens[i];
                if (t.Kind == SurveyTokenKind.CurveTag && c.Tag == null) { c.Tag = t.Name; continue; }
                if (t.Kind == SurveyTokenKind.Word)
                {
                    var w = t.Text.ToUpperInvariant().Trim('.', ',', ';');
                    if (w == "LEFT" || w == "LT" || w == "(LEFT)") c.Turn = "LEFT";
                    else if (w == "RIGHT" || w == "RT" || w == "(RIGHT)") c.Turn = "RIGHT";
                    else if (w == "NON-TANGENT" || w == "NONTANGENT" || w == "NON") c.TangentToPrevious = false;
                    else if (w == "TANGENT") c.TangentToPrevious = true;
                    continue;
                }
                if (t.Kind != SurveyTokenKind.CurveKey) continue;
                var value = i + 1 < tokens.Count ? tokens[i + 1] : null;
                if (value == null || value.Read == null || !value.Read.Ok) { call.Notes.Add("Curve element " + t.Name + " has no readable value after it."); continue; }
                var src = new SourceRef(l.Page, l.Line.Box, t.Text + value.Text, l.Line.EffectiveConfidence);
                confidence = Math.Min(confidence, value.Read.Confidence);
                var name = t.Name;
                if (name == "A") name = value.Kind == SurveyTokenKind.Angle ? "DELTA" : "L";
                if (name == "DELTA" && t.Text.Trim().StartsWith("D", StringComparison.OrdinalIgnoreCase) && !t.Text.Contains("Δ") && !t.Text.ToUpperInvariant().Contains("DELTA"))
                {
                    // "D 6°CL": the degree of curve (chord definition), not the central angle -- when the group
                    // also states Δ, or the value is followed by CL/AR. Noted, not used: R is what the office draws from.
                    var after = i + 2 < tokens.Count ? tokens[i + 2].Text.ToUpperInvariant() : string.Empty;
                    var hasDelta = tokens.Any(x => x.Kind == SurveyTokenKind.CurveKey && (x.Text.Contains("Δ") || x.Text.ToUpperInvariant().Contains("DELTA"))) ||
                                   call.Curve.Stated("DELTA");
                    if (hasDelta || after.StartsWith("CL") || after.StartsWith("AR"))
                    {
                        call.Notes.Add("Degree of curve " + value.Read.Normalized + " read and set aside; the curve is built from R and Δ.");
                        continue;
                    }
                }
                switch (name)
                {
                    case "R": if (value.Kind == SurveyTokenKind.Distance) { c.Radius = value.Read.Value; Stated(c, "R", src); } break;
                    case "L": if (value.Kind == SurveyTokenKind.Distance) { c.ArcLength = value.Read.Value; Stated(c, "L", src); } break;
                    case "DELTA": if (value.Kind == SurveyTokenKind.Angle) { c.DeltaDegrees = value.Read.Value; Stated(c, "DELTA", src); }
                                  else if (value.Kind == SurveyTokenKind.Distance && !c.ArcLength.HasValue) { c.ArcLength = value.Read.Value; Stated(c, "L", src); call.Notes.Add("'D=' followed by a distance was read as the arc length."); }
                                  break;
                    case "CH": if (value.Kind == SurveyTokenKind.Distance) { c.ChordLength = value.Read.Value; Stated(c, "CH", src); }
                               else if (value.Kind == SurveyTokenKind.Bearing) { c.ChordAzimuthDegrees = value.Read.Value; Stated(c, "CB", src); }
                               break;
                    case "CB": if (value.Kind == SurveyTokenKind.Bearing) { c.ChordAzimuthDegrees = value.Read.Value; Stated(c, "CB", src); } break;
                    case "T": if (value.Kind == SurveyTokenKind.Distance) { c.TangentLength = value.Read.Value; Stated(c, "T", src); } break;
                }
                // "CB=N 45°12'10" E 192.06'": the distance after a chord bearing is the chord.
                if (t.Name == "CB" || (t.Name == "CH" && value.Kind == SurveyTokenKind.Bearing))
                {
                    var after = i + 2 < tokens.Count ? tokens[i + 2] : null;
                    if (after != null && after.Kind == SurveyTokenKind.Distance && after.Read != null && after.Read.Ok && !c.ChordLength.HasValue)
                    {
                        c.ChordLength = after.Read.Value;
                        Stated(c, "CH", new SourceRef(l.Page, l.Line.Box, after.Text, l.Line.EffectiveConfidence));
                    }
                }
            }
        }

        private static void Stated(CurveSpec c, string element, SourceRef src)
        {
            if (!c.StatedElements.Contains(element)) c.StatedElements.Add(element);
            c.Sources.Add(src);
        }

        private static List<string> HeaderColumns(string text)
        {
            var t = (text ?? string.Empty).ToUpperInvariant();
            var columns = new List<KeyValuePair<int, string>>();
            Action<string, string> find = (pattern, name) =>
            {
                var m = Regex.Match(t, pattern);
                if (m.Success) columns.Add(new KeyValuePair<int, string>(m.Index, name));
            };
            find(@"\bRADIUS\b|\bRAD\b|\bR\b", "R");
            find(@"\bDELTA\b|Δ|\bCENTRAL ANGLE\b", "DELTA");
            find(@"\bARC\b|\bLENGTH\b|\bL\b", "L");
            find(@"\bCHORD\s+(?:BEARING|BRG|DIRECTION)\b|\bCH\.?\s*BRG\b|\bCB\b", "CB");
            find(@"\bCHORD(?!\s+(?:BEARING|BRG|DIRECTION))\b|\bCH\b|\bCHORD\s+(?:DIST|LENGTH)\b", "CH");
            find(@"\bTANGENT\b|\bTAN\b", "T");
            return columns.OrderBy(c => c.Key).Select(c => c.Value).ToList();
        }

        private static void ReadCurveRow(SurveyCall call, ClassifiedLine row, List<string> header, ExtractionOptions options)
        {
            var c = call.Curve;
            var values = row.Tokens.Where(t => (t.Kind == SurveyTokenKind.Distance || t.Kind == SurveyTokenKind.Angle || t.Kind == SurveyTokenKind.Bearing) && t.Read != null && t.Read.Ok).ToList();
            var bearing = values.FirstOrDefault(v => v.Kind == SurveyTokenKind.Bearing);
            var angle = values.FirstOrDefault(v => v.Kind == SurveyTokenKind.Angle);
            var distances = values.Where(v => v.Kind == SurveyTokenKind.Distance).ToList();
            var src = row.Source;
            if (bearing != null) { c.ChordAzimuthDegrees = bearing.Read.Value; Stated(c, "CB", src); }
            if (angle != null) { c.DeltaDegrees = angle.Read.Value; Stated(c, "DELTA", src); }
            call.Confidence = Math.Min(call.Confidence, values.Min(v => v.Read.Confidence));

            // Column names from the header, matched to the distances in order; else the maths decides.
            var distanceColumns = header != null ? header.Where(h => h == "R" || h == "L" || h == "CH" || h == "T").ToList() : null;
            if (distanceColumns != null && distanceColumns.Count == distances.Count)
            {
                for (var i = 0; i < distances.Count; i++)
                {
                    var v = distances[i].Read.Value;
                    switch (distanceColumns[i])
                    {
                        case "R": c.Radius = v; Stated(c, "R", src); break;
                        case "L": c.ArcLength = v; Stated(c, "L", src); break;
                        case "CH": c.ChordLength = v; Stated(c, "CH", src); break;
                        case "T": c.TangentLength = v; Stated(c, "T", src); break;
                    }
                }
                call.Notes.Add("Curve table columns taken from the table header.");
                return;
            }

            if (angle != null && distances.Count >= 2 && distances.Count <= 3)
            {
                var identified = CurveSolver.IdentifyColumns(distances.Select(d => d.Read.Value).ToList(), angle.Read.Value, 0.05);
                if (identified != null)
                {
                    c.Radius = identified.Radius; c.ArcLength = identified.ArcLength; c.ChordLength = identified.ChordLength;
                    foreach (var e in identified.StatedElements) Stated(c, e, src);
                    call.Notes.Add("Curve table columns identified by the curve equations (only one assignment of radius, arc and chord fits Δ).");
                    call.Confidence *= 0.95;
                    return;
                }
            }

            // Nothing settles the columns: keep the numbers as read, unassigned, for the reviewer.
            call.Notes.Add("Curve table columns could not be identified (" + distances.Count + " distances" + (angle != null ? ", Δ" : ", no Δ") + "); assign them in the review.");
            for (var i = 0; i < distances.Count; i++)
                call.Notes.Add("  value " + (i + 1) + ": " + distances[i].Read.Normalized);
            call.Confidence = Math.Min(call.Confidence, options.ReviewThreshold - 0.01);
        }

        private static void FinishCurve(SurveyCall call, ExtractionOptions options)
        {
            var c = call.Curve;
            var stated = c.StatedElements.Count(e => e == "R" || e == "DELTA" || e == "L" || e == "CH" || e == "T");
            if (stated < 2)
            {
                call.Notes.Add("Only " + stated + " curve element(s) read; a curve needs two of R, Δ, L, CH, T.");
                call.Confidence = Math.Min(call.Confidence, options.ReviewThreshold - 0.01);
                return;
            }
            var solution = CurveSolver.Solve(c, 0.02, 10.0);
            if (!solution.Ok) { call.Notes.Add(solution.Error); call.Confidence = Math.Min(call.Confidence, options.ReviewThreshold - 0.01); return; }
            foreach (var d in solution.Disagreements)
            {
                call.Notes.Add("Curve elements disagree: " + d + ".");
                call.Confidence = Math.Min(call.Confidence, options.ReviewThreshold - 0.01);
            }
            if (string.IsNullOrEmpty(c.Turn) && !c.ChordAzimuthDegrees.HasValue)
                call.Notes.Add("Turn direction not stated; it is settled by the traverse or entered in the review.");
        }

        // ------------------------------------------------------------ monuments, notes

        private static void ReadMonuments(RecordSurveyProject project, List<ClassifiedLine> lines)
        {
            var n = 0;
            var legends = lines.Where(x => x.Kind == SurveyEntityKind.Legend && Regex.IsMatch(x.Line.Text ?? string.Empty, @"^\s*LEGEND\b", RegexOptions.IgnoreCase)).ToList();
            foreach (var l in lines.Where(x => x.Kind == SurveyEntityKind.Monument))
            {
                // A monument description listed under a LEGEND heading explains a symbol; it marks no
                // corner. Kept as an annotation, not offered as a monument to place.
                if (legends.Any(g => g.Page == l.Page && l.Line.Box.Y > g.Line.Box.Y && l.Line.Box.Y - g.Line.Box.Y < 8 * Math.Max(1.0, g.Line.Box.Height) &&
                                     Math.Abs(l.Line.Box.X - g.Line.Box.X) < 4 * Math.Max(1.0, g.Line.Box.Height)))
                {
                    project.Annotations.Add(new SurveyAnnotation { Kind = SurveyEntityKind.Legend, Text = l.Line.Text.Trim(), Source = l.Source, Confidence = l.Confidence * l.Line.EffectiveConfidence });
                    continue;
                }
                n++;
                project.Monuments.Add(new MonumentCall
                {
                    Id = "M" + n.ToString(CultureInfo.InvariantCulture),
                    Status = EntityClassifier.MonumentStatusOf(l.Line.Text),
                    Description = l.Line.Text.Trim(),
                    Source = l.Source,
                    Confidence = l.Confidence * l.Line.EffectiveConfidence,
                    PageHint = l.Line.Box
                });
            }
        }

        private static void ReadAnnotations(RecordSurveyProject project, List<ClassifiedLine> lines)
        {
            var kinds = new[]
            {
                SurveyEntityKind.BasisOfBearing, SurveyEntityKind.StreetName, SurveyEntityKind.Note, SurveyEntityKind.Legend,
                SurveyEntityKind.Surveyor, SurveyEntityKind.Scale, SurveyEntityKind.SheetNumber, SurveyEntityKind.SurveyTitle,
                SurveyEntityKind.BlockNumber, SurveyEntityKind.Certification
            };
            foreach (var l in lines.Where(x => kinds.Contains(x.Kind)))
                project.Annotations.Add(new SurveyAnnotation { Kind = l.Kind, Text = l.Line.Text.Trim(), Source = l.Source, Confidence = l.Confidence * l.Line.EffectiveConfidence });
        }

        // ------------------------------------------------------------ status

        /// <summary>Extracted or NeedsReview, by confidence, completeness and ambiguity. Never Approved: that is the reviewer's.</summary>
        public static void Classify(SurveyCall call, ExtractionOptions options)
        {
            if (call.Status == CallStatus.Approved || call.Status == CallStatus.Rejected) return;
            var needs = call.Confidence < options.ReviewThreshold;
            if (call.Kind == CallKind.Line)
            {
                ValueBasis b;
                if (call.GeometryValue(true, out b) == null) needs = true;
                if (call.Alternatives.Any(a => a.Reason == "second OCR pass")) needs = true;
            }
            else
            {
                if (call.Curve == null) needs = true;
                else
                {
                    var solution = CurveSolver.Solve(call.Curve, 0.02, 10.0);
                    if (!solution.Ok || solution.Disagreements.Count > 0) needs = true;
                }
            }
            call.Status = needs ? CallStatus.NeedsReview : CallStatus.Extracted;
        }
    }
}
