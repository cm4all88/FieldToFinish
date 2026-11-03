using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FieldCodes.Review;
using FieldCodes.Settings;

namespace FieldCodes.Linework
{
    /// <summary>One survey shot as the continuity check sees it, in collection order.</summary>
    public sealed class ContinuityShot
    {
        public string Number { get; set; }
        public string Handle { get; set; }
        public string RawDescription { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        /// <summary>The shot's linework coding (<see cref="LineworkCoding.Parse(string, RulesConfig)"/>).</summary>
        public LineworkShot Coding { get; set; }
    }

    /// <summary>One drawn line: a polyline, 3D polyline, line, arc, survey figure or feature line.</summary>
    public sealed class ContinuityLine
    {
        public string Handle { get; set; }
        public string Layer { get; set; }
        public string EntityType { get; set; }
        public bool Closed { get; set; }
        /// <summary>An approved correction FTF drew (counted as drawn for the ledger, never as
        /// closing a finding: a correction does not make the survey linework continuous).</summary>
        public bool IsCorrection { get; set; }
        /// <summary>The figure name when the line is a survey figure or feature line.</summary>
        public string FigureName { get; set; }
        /// <summary>Plan vertices with Z, in order: [x, y, z].</summary>
        public List<double[]> Vertices { get; set; }
        /// <summary>The shot numbers at each vertex (same order as <see cref="Vertices"/>).</summary>
        public List<List<string>> ShotsAtVertex { get; set; }
        /// <summary>The feature the line is identified as when no shot says (figure name, layer).</summary>
        public string FeatureCode { get; set; }

        public ContinuityLine()
        {
            Vertices = new List<double[]>();
            ShotsAtVertex = new List<List<string>>();
        }
    }

    /// <summary>Everything the continuity check reads. Built once by the CAD collector.</summary>
    public sealed class ContinuityInput
    {
        /// <summary>Every shot, in collection order (point number order).</summary>
        public IList<ContinuityShot> Shots { get; set; }
        public IList<ContinuityLine> Lines { get; set; }
        /// <summary>Point numbers coded as gate posts.</summary>
        public ICollection<string> GatePosts { get; set; }
        public ContinuitySettings Settings { get; set; }
        /// <summary>Drawing units per foot.</summary>
        public double UnitsPerFoot { get; set; }
        /// <summary>The feature family a figure code belongs to (a code's aliases map to it);
        /// null: the code itself.</summary>
        public Func<string, string> FamilyOf { get; set; }
        /// <summary>The layer a correction for a family goes on when no drawn line of it says.</summary>
        public Func<string, string> LayerOf { get; set; }

        public ContinuityInput()
        {
            Shots = new List<ContinuityShot>();
            Lines = new List<ContinuityLine>();
            GatePosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Settings = new ContinuitySettings();
            UnitsPerFoot = 1.0;
        }
    }

    /// <summary>How a coded string came to an end.</summary>
    public enum StringEnd
    {
        /// <summary>The crew coded the end (E).</summary>
        Explicit,
        /// <summary>The same figure was begun again (B) before it was ended: an interrupted collection.</summary>
        ReBegun,
        /// <summary>The figure was closed (C).</summary>
        Closed,
        /// <summary>No later shot carries the figure.</summary>
        EndOfData
    }

    /// <summary>One string of shots the crew coded as a figure: begin to end.</summary>
    public sealed class CodedString
    {
        public string FigureId { get; set; }
        public string Code { get; set; }
        public string Family { get; set; }
        public List<string> Shots { get; set; }
        public bool ExplicitBegin { get; set; }
        public StringEnd End { get; set; }
        /// <summary>Shots that are on a curve (between P and T, or OC): they need not be vertices.</summary>
        public HashSet<string> CurveShots { get; set; }

        public CodedString()
        {
            Shots = new List<string>();
            CurveShots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>The confidence of a continuity finding.</summary>
    public enum GapConfidence { Probable, Possible, Intentional }

    /// <summary>One thing the continuity check found.</summary>
    public sealed class ContinuityFinding
    {
        public string Kind { get; set; }
        public string Id { get; set; }
        public string FigureId { get; set; }
        public string Family { get; set; }
        public GapConfidence Confidence { get; set; }
        public List<string> Shots { get; set; }
        public List<string> Lines { get; set; }
        public List<string> Evidence { get; set; }
        public List<double[]> Geometry { get; set; }
        public string Layer { get; set; }
        public string Issue { get; set; }
        public string Reason { get; set; }
        public string Fingerprint { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double LengthFeet { get; set; }

        public ContinuityFinding()
        {
            Shots = new List<string>();
            Lines = new List<string>();
            Evidence = new List<string>();
            Geometry = new List<double[]>();
        }

        /// <summary>The review item for it (intentional findings are informational only and are
        /// not review items).</summary>
        public ReviewItem ToReviewItem(IDictionary<string, ContinuityShot> shots)
        {
            var item = ReviewKinds.New(Kind, Id.Substring(Kind.Length + 1), Reason);
            item.Source = ReviewKinds.ContinuitySource;
            item.Issue = Issue;
            item.Code = FigureId;
            item.FeatureType = "Line";
            item.Points = string.Join(", ", Shots.ToArray());
            item.Evidence = new List<string>(Evidence);
            item.Confidence = Confidence.ToString().ToLowerInvariant();
            item.Geometry = Geometry.Select(p => (double[])p.Clone()).ToList();
            item.TargetLayer = Layer;
            item.Fingerprint = Fingerprint;
            item.X = X;
            item.Y = Y;
            foreach (var h in Lines) if (!string.IsNullOrEmpty(h) && !item.Handles.Contains(h)) item.Handles.Add(h);
            foreach (var n in Shots)
            {
                ContinuityShot s;
                if (shots != null && shots.TryGetValue(n, out s) && !string.IsNullOrEmpty(s.Handle) && !item.Handles.Contains(s.Handle)) item.Handles.Add(s.Handle);
            }
            if (Confidence == GapConfidence.Possible) item.Severity = ReviewSeverity.Warning;
            return item;
        }
    }

    /// <summary>The check's result: what it found, the strings it read, and which shots are on a
    /// drawn line of their own feature (for the point ledger).</summary>
    public sealed class ContinuityResult
    {
        public List<ContinuityFinding> Findings { get; private set; }
        public List<CodedString> Strings { get; private set; }

        /// <summary>(shot number, family) -> handles of the drawn lines of that family the shot is
        /// on (a vertex, or on a segment within the tolerance), corrections included.</summary>
        public Dictionary<string, List<string>> Coverage { get; private set; }

        /// <summary>(shot number, figure id) -> the finding ids that name the shot on that figure.</summary>
        public Dictionary<string, List<string>> FindingsByShot { get; private set; }

        /// <summary>The findings about a shot on one figure (a shot on two figures can have a
        /// finding on one and none on the other).</summary>
        public IList<string> FindingsFor(string shot, string figureId)
        {
            List<string> ids;
            return FindingsByShot.TryGetValue(Key(shot, figureId), out ids) ? ids : new List<string>();
        }

        public ContinuityResult()
        {
            Findings = new List<ContinuityFinding>();
            Strings = new List<CodedString>();
            Coverage = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            FindingsByShot = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        }

        public static string Key(string shot, string family)
        {
            return (shot ?? string.Empty) + "|" + (family ?? string.Empty).ToUpperInvariant();
        }

        /// <summary>The drawn lines of a family a shot is on.</summary>
        public IList<string> LinesOf(string shot, string family)
        {
            List<string> lines;
            return Coverage.TryGetValue(Key(shot, family), out lines) ? lines : new List<string>();
        }

        public IList<ReviewItem> ReviewItems(IDictionary<string, ContinuityShot> shots)
        {
            return Findings.Where(f => f.Confidence != GapConfidence.Intentional).Select(f => f.ToReviewItem(shots)).ToList();
        }
    }

    /// <summary>
    /// Linework continuity: reads the strings the crew coded (begin, continue, end, close, curves,
    /// interleaved figures, re-begun figures) from the shots in collection order, and compares
    /// them with the lines drawn through those shots. Finds:
    /// <list type="bullet">
    /// <item>a coded string drawn as two lines with a gap between (a missing connection);</item>
    /// <item>a figure re-begun after an interruption whose two parts line up (a probable missing
    /// connection) or might (possible), weighed on identity, coding, sequence, gap length,
    /// alignment, crossings and gate posts -- never on consecutive numbers alone;</item>
    /// <item>a line that runs past a shot coded onto it (a missing intermediate vertex);</item>
    /// <item>coded shots with no line between them (a missing segment, or a whole string).</item>
    /// </list>
    /// Gate openings and gaps crossed by another feature are intentional. Read-only: it proposes,
    /// it never draws or changes anything.
    /// </summary>
    public static class LineContinuity
    {
        public static ContinuityResult Analyze(ContinuityInput input)
        {
            var result = new ContinuityResult();
            if (input == null) return result;
            var settings = input.Settings ?? new ContinuitySettings();
            var upf = input.UnitsPerFoot > 0 ? input.UnitsPerFoot : 1.0;
            var tol = Math.Max(1e-6, settings.OnLineToleranceFeet * upf);
            Func<string, string> family = code =>
            {
                var c = ContinuitySettings.BaseCode(code);
                var f = input.FamilyOf != null ? input.FamilyOf(c) : null;
                return string.IsNullOrWhiteSpace(f) ? c : f.Trim().ToUpperInvariant();
            };

            var shots = new Dictionary<string, ContinuityShot>(StringComparer.OrdinalIgnoreCase);
            var order = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var sequence = new List<ContinuityShot>();
            foreach (var s in input.Shots ?? new List<ContinuityShot>())
            {
                if (s == null || string.IsNullOrWhiteSpace(s.Number) || shots.ContainsKey(s.Number)) continue;
                shots[s.Number] = s;
                order[s.Number] = sequence.Count;
                sequence.Add(s);
            }

            // ---- the strings the crew coded
            result.Strings.AddRange(ReadStrings(sequence, shots, settings, family));

            // ---- the lines, and the family each one is drawn for
            var lines = (input.Lines ?? new List<ContinuityLine>()).Where(l => l != null && l.Vertices != null && l.Vertices.Count >= 2).ToList();
            var lineFamily = new Dictionary<ContinuityLine, string>();
            foreach (var l in lines) lineFamily[l] = FamilyOfLine(l, shots, family);

            // ---- coverage: which shots are on a drawn line of their family
            var coverageAll = Cover(result.Strings, lines, lineFamily, shots, tol, true);
            var coverageSurvey = Cover(result.Strings, lines, lineFamily, shots, tol, false);
            foreach (var pair in coverageAll) result.Coverage[pair.Key] = pair.Value.Select(l => l.Handle).Distinct().ToList();

            // ---- findings inside each coded string
            foreach (var str in result.Strings.Where(x => x.Shots.Count >= 2))
            {
                var rules = settings.For(str.Code);
                if (rules == null || !rules.Enabled) continue;
                Inside(str, shots, coverageSurvey, lines, lineFamily, input, rules, tol, upf, result);
            }

            // ---- interrupted collection: the same figure begun again
            Between(result.Strings, sequence, shots, order, coverageSurvey, lines, lineFamily, input, settings, tol, upf, result);

            foreach (var f in result.Findings)
                foreach (var n in f.Shots)
                {
                    List<string> ids;
                    var key = ContinuityResult.Key(n, f.FigureId);
                    if (!result.FindingsByShot.TryGetValue(key, out ids)) { ids = new List<string>(); result.FindingsByShot[key] = ids; }
                    if (!ids.Contains(f.Id)) ids.Add(f.Id);
                }
            return result;
        }

        // ------------------------------------------------------------------ strings

        private static IEnumerable<CodedString> ReadStrings(IList<ContinuityShot> sequence, IDictionary<string, ContinuityShot> shots,
                                                           ContinuitySettings settings, Func<string, string> family)
        {
            var done = new List<CodedString>();
            var open = new Dictionary<string, CodedString>(StringComparer.OrdinalIgnoreCase);
            var inCurve = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var shot in sequence)
            {
                if (shot.Coding == null) continue;
                foreach (var f in shot.Coding.Figures)
                {
                    if (string.IsNullOrWhiteSpace(f.FigureId)) continue;
                    var id = f.FigureId.ToUpperInvariant();
                    CodedString str;
                    open.TryGetValue(id, out str);
                    if (f.Begins)
                    {
                        if (str != null) { str.End = StringEnd.ReBegun; done.Add(str); }
                        str = new CodedString { FigureId = id, Code = f.Code.ToUpperInvariant(), Family = family(f.Code), ExplicitBegin = true };
                        open[id] = str;
                        inCurve.Remove(id);
                    }
                    else if (str == null)
                    {
                        str = new CodedString { FigureId = id, Code = f.Code.ToUpperInvariant(), Family = family(f.Code), ExplicitBegin = false };
                        open[id] = str;
                    }

                    // A recalled point joins the string before this shot; a connected one after it.
                    foreach (var c in f.Commands.Where(c => c.Kind == LineworkCommandKind.RecallPoint && c.Parameters.Count > 0))
                        if (shots.ContainsKey(c.Parameters[0]) && (str.Shots.Count == 0 || str.Shots[str.Shots.Count - 1] != c.Parameters[0]))
                            str.Shots.Add(c.Parameters[0]);
                    if (str.Shots.Count == 0 || str.Shots[str.Shots.Count - 1] != shot.Number) str.Shots.Add(shot.Number);
                    foreach (var c in f.Commands.Where(c => c.Kind == LineworkCommandKind.ConnectPoint && c.Parameters.Count > 0))
                        if (shots.ContainsKey(c.Parameters[0])) str.Shots.Add(c.Parameters[0]);

                    if (f.Has(LineworkCommandKind.CurveBegin)) inCurve.Add(id);
                    else if (inCurve.Contains(id)) str.CurveShots.Add(shot.Number);
                    if (f.Has(LineworkCommandKind.CurveEnd)) inCurve.Remove(id);
                    if (f.Has(LineworkCommandKind.PointOnCurve)) str.CurveShots.Add(shot.Number);

                    if (f.Closes) { str.End = StringEnd.Closed; done.Add(str); open.Remove(id); inCurve.Remove(id); }
                    else if (f.Ends) { str.End = StringEnd.Explicit; done.Add(str); open.Remove(id); inCurve.Remove(id); }
                }
            }
            foreach (var str in open.Values) { str.End = StringEnd.EndOfData; done.Add(str); }
            // In order of their first shot, so a rerun reports them the same way.
            var first = sequence.Select((s, i) => new { s.Number, i }).ToDictionary(x => x.Number, x => x.i, StringComparer.OrdinalIgnoreCase);
            return done.Where(s => s.Shots.Count > 0)
                       .OrderBy(s => first.ContainsKey(s.Shots[0]) ? first[s.Shots[0]] : int.MaxValue)
                       .ThenBy(s => s.FigureId, StringComparer.Ordinal)
                       .ToList();
        }

        /// <summary>The family a line is drawn for: what most of its shots are coded, else what it
        /// was identified as (figure name, layer).</summary>
        private static string FamilyOfLine(ContinuityLine line, IDictionary<string, ContinuityShot> shots, Func<string, string> family)
        {
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var at in line.ShotsAtVertex ?? new List<List<string>>())
            {
                var here = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var n in at ?? new List<string>())
                {
                    ContinuityShot s;
                    if (!shots.TryGetValue(n, out s) || s.Coding == null) continue;
                    foreach (var f in s.Coding.Figures) here.Add(family(f.Code));
                }
                foreach (var f in here) { int c; counts.TryGetValue(f, out c); counts[f] = c + 1; }
            }
            if (!string.IsNullOrWhiteSpace(line.FigureName))
            {
                var code = family(ContinuitySettings.BaseCode(line.FigureName));
                int c; counts.TryGetValue(code, out c); counts[code] = c + 1000;   // the figure's own name wins
            }
            if (counts.Count > 0) return counts.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal).First().Key;
            return string.IsNullOrWhiteSpace(line.FeatureCode) ? null : family(line.FeatureCode);
        }

        /// <summary>(shot, family) -> the lines of that family the shot is on.</summary>
        private static Dictionary<string, List<ContinuityLine>> Cover(IEnumerable<CodedString> strings, IList<ContinuityLine> lines,
            IDictionary<ContinuityLine, string> lineFamily, IDictionary<string, ContinuityShot> shots, double tol, bool withCorrections)
        {
            var map = new Dictionary<string, List<ContinuityLine>>(StringComparer.OrdinalIgnoreCase);
            var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in strings) foreach (var n in s.Shots) wanted.Add(ContinuityResult.Key(n, s.Family));

            var byFamily = lines.Where(l => withCorrections || !l.IsCorrection)
                                .GroupBy(l => lineFamily[l] ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
            foreach (var key in wanted)
            {
                var bar = key.LastIndexOf('|');
                var number = key.Substring(0, bar);
                var fam = key.Substring(bar + 1);
                ContinuityShot shot;
                List<ContinuityLine> candidates;
                if (!shots.TryGetValue(number, out shot) || !byFamily.TryGetValue(fam, out candidates)) continue;
                foreach (var line in candidates)
                    if (VertexOf(line, number) >= 0 || Distance(line, shot.X, shot.Y) <= tol)
                    {
                        List<ContinuityLine> list;
                        if (!map.TryGetValue(key, out list)) { list = new List<ContinuityLine>(); map[key] = list; }
                        list.Add(line);
                    }
            }
            return map;
        }

        private static List<ContinuityLine> On(IDictionary<string, List<ContinuityLine>> coverage, string shot, string family)
        {
            List<ContinuityLine> list;
            return coverage.TryGetValue(ContinuityResult.Key(shot, family), out list) ? list : new List<ContinuityLine>();
        }

        // ------------------------------------------------------------- inside a string

        private static void Inside(CodedString str, IDictionary<string, ContinuityShot> shots, IDictionary<string, List<ContinuityLine>> coverage,
                                   IList<ContinuityLine> lines, IDictionary<ContinuityLine, string> lineFamily, ContinuityInput input,
                                   FeatureContinuity rules, double tol, double upf, ContinuityResult result)
        {
            var sequence = new List<string>(str.Shots);
            if (str.End == StringEnd.Closed && sequence.Count >= 3) sequence.Add(sequence[0]);

            // Which consecutive pairs are drawn: both shots on one line of the family.
            var drawn = new bool[sequence.Count - 1];
            for (var i = 0; i < drawn.Length; i++)
            {
                var a = On(coverage, sequence[i], str.Family);
                var b = On(coverage, sequence[i + 1], str.Family);
                drawn[i] = sequence[i] == sequence[i + 1] || a.Any(l => b.Contains(l) && Joined(l, sequence[i], sequence[i + 1]));
            }

            var i0 = 0;
            while (i0 < drawn.Length)
            {
                if (drawn[i0]) { i0++; continue; }
                var i1 = i0;
                while (i1 + 1 < drawn.Length && !drawn[i1 + 1]) i1++;
                // Undrawn run: edges i0..i1, shots i0..i1+1.
                var run = sequence.GetRange(i0, i1 - i0 + 2);
                Classify(str, run, shots, coverage, lines, lineFamily, input, rules, tol, upf, result);
                i0 = i1 + 1;
            }
        }

        private static void Classify(CodedString str, List<string> run, IDictionary<string, ContinuityShot> shots,
                                     IDictionary<string, List<ContinuityLine>> coverage, IList<ContinuityLine> lines,
                                     IDictionary<ContinuityLine, string> lineFamily, ContinuityInput input, FeatureContinuity rules,
                                     double tol, double upf, ContinuityResult result)
        {
            var first = run[0];
            var last = run[run.Count - 1];
            var interior = run.Skip(1).Take(run.Count - 2).ToList();
            var onFirst = On(coverage, first, str.Family);
            var onLast = On(coverage, last, str.Family);
            var interiorUncovered = interior.All(n => On(coverage, n, str.Family).Count == 0);
            var shared = onFirst.Where(l => onLast.Contains(l) && Joined(l, first, last)).ToList();

            // The line goes straight from the first shot to the last and skips the ones coded between.
            if (interior.Count > 0 && interiorUncovered && shared.Count > 0)
            {
                var line = shared[0];
                var f = New(ReviewKinds.LineMissingVertex, str, run, shots, input, line.Layer);
                f.Lines.Add(line.Handle);
                f.Confidence = GapConfidence.Probable;
                var off = interior.Select(n => Distance(line, shots[n].X, shots[n].Y) / upf).Max();
                f.Issue = "Line skips " + Plural(interior.Count, "coded shot") + " (" + str.FigureId + ")";
                f.Reason = str.FigureId + ": the line goes from " + first + " to " + last + " but " + string.Join(", ", interior.ToArray()) +
                           (interior.Count == 1 ? " is" : " are") + " coded between them, up to " + Ft(off) + " off it.";
                f.Evidence.Add("same coded string " + str.FigureId + " (" + string.Join(" ", str.Shots.ToArray()) + ")");
                f.Evidence.Add("drawn line " + line.Handle + " has " + first + " and " + last + " but not " + string.Join(", ", interior.ToArray()));
                f.Evidence.Add("largest offset from the line: " + Ft(off));
                Finish(f, shots, line);
                result.Findings.Add(f);
                return;
            }

            // Two drawn lines of the same coded string with nothing between them.
            if (interior.Count == 0 && onFirst.Count > 0 && onLast.Count > 0)
            {
                var a = onFirst[0];
                var b = onLast[0];
                var f = New(ReviewKinds.LineGap, str, run, shots, input, a.Layer);
                f.Lines.Add(a.Handle);
                if (!f.Lines.Contains(b.Handle)) f.Lines.Add(b.Handle);
                var gap = PlanDistance(shots[first], shots[last]) / upf;
                f.LengthFeet = gap;
                f.Evidence.Add("one coded string " + str.FigureId + ": " + first + " and " + last + " are consecutive shots of it, coded continuous");
                f.Evidence.Add("drawn as " + (a == b ? "one line that does not join them" : "two lines (" + a.Handle + ", " + b.Handle + ")") + ", " + Ft(gap) + " apart");
                f.Confidence = GapConfidence.Probable;
                string why;
                if (GateAt(shots[first], shots[last], input, rules, tol, out why))
                {
                    f.Confidence = GapConfidence.Intentional;
                    f.Evidence.Add(why);
                }
                else
                {
                    bool identified;
                    var crossing = Crossing(shots[first], shots[last], lines, lineFamily, str.Family, a, b, out identified);
                    if (crossing != null)
                    {
                        f.Confidence = GapConfidence.Possible;
                        f.Evidence.Add("crossed by " + crossing + ": the drafter may have stopped the line there");
                    }
                }
                f.Issue = (f.Confidence == GapConfidence.Probable ? "Probable" : f.Confidence == GapConfidence.Possible ? "Possible" : "Intentional") +
                          " missing connection (" + str.FigureId + " " + first + "-" + last + ")";
                f.Reason = str.FigureId + " is coded continuous from " + first + " to " + last + " but the drawing has a " + Ft(gap) + " gap there.";
                Finish(f, shots, null, a, b);
                result.Findings.Add(f);
                return;
            }

            // Shots coded as one string with no line between them: part or all of it is not drawn.
            var whole = run.Count >= str.Shots.Count && str.Shots.All(n => On(coverage, n, str.Family).Count == 0);
            var seg = New(ReviewKinds.LineMissingSegment, str, run, shots, input, onFirst.Count > 0 ? onFirst[0].Layer : onLast.Count > 0 ? onLast[0].Layer : null);
            foreach (var l in onFirst.Concat(onLast)) if (!seg.Lines.Contains(l.Handle)) seg.Lines.Add(l.Handle);
            seg.Confidence = GapConfidence.Probable;
            var length = 0.0;
            for (var i = 1; i < run.Count; i++) length += PlanDistance(shots[run[i - 1]], shots[run[i]]);
            seg.LengthFeet = length / upf;
            seg.Issue = whole ? "Coded string not drawn (" + str.FigureId + ", " + Plural(str.Shots.Count, "shot") + ")"
                              : "Coded segment not drawn (" + str.FigureId + " " + first + "-" + last + ")";
            seg.Reason = whole
                ? str.FigureId + " is coded through " + string.Join(", ", str.Shots.ToArray()) + " but no line of it is drawn."
                : str.FigureId + " is coded through " + string.Join(", ", run.ToArray()) + " but no line joins " + (run.Count == 2 ? "them" : "those shots") + ".";
            seg.Evidence.Add("coded string " + str.FigureId + (str.ExplicitBegin ? " (begun with B)" : string.Empty) + ": " + string.Join(" ", str.Shots.ToArray()));
            seg.Evidence.Add(whole ? "none of its shots is on a line of " + str.Family : Ft(seg.LengthFeet) + " of it has no line");
            Finish(seg, shots, null);
            result.Findings.Add(seg);
        }

        // ---------------------------------------------------------- between strings

        private static void Between(IList<CodedString> strings, IList<ContinuityShot> sequence, IDictionary<string, ContinuityShot> shots, IDictionary<string, int> order,
                                    IDictionary<string, List<ContinuityLine>> coverage, IList<ContinuityLine> lines,
                                    IDictionary<ContinuityLine, string> lineFamily, ContinuityInput input, ContinuitySettings settings,
                                    double tol, double upf, ContinuityResult result)
        {
            var ordered = strings.OrderBy(s => order.ContainsKey(s.Shots[0]) ? order[s.Shots[0]] : int.MaxValue).ToList();
            foreach (var group in ordered.GroupBy(s => s.FigureId, StringComparer.OrdinalIgnoreCase))
            {
                var list = group.ToList();
                for (var k = 0; k + 1 < list.Count; k++)
                {
                    var s1 = list[k];
                    var s2 = list[k + 1];
                    if (s1.End == StringEnd.Closed) continue;
                    var rules = settings.For(s1.Code);
                    if (rules == null || !rules.Enabled) continue;
                    var a = s1.Shots[s1.Shots.Count - 1];
                    var b = s2.Shots[0];
                    if (a == b) continue;
                    var onA = On(coverage, a, s1.Family);
                    var onB = On(coverage, b, s2.Family);
                    if (onA.Count == 0 || onB.Count == 0) continue;     // an undrawn end is a missing segment, reported above
                    if (onA.Any(l => onB.Contains(l) && Joined(l, a, b))) continue;   // one line already joins them

                    var gap = PlanDistance(shots[a], shots[b]);
                    var spacing = MedianSpacing(s1, s2, shots);
                    var limit = rules.MaxGapFeet * upf;
                    if (spacing > 0) limit = Math.Min(limit, rules.SpacingFactor * spacing);
                    if (gap > limit) continue;                         // too far apart to be one string

                    var f = New(ReviewKinds.LineGap, s1, new List<string> { a, b }, shots, input, onA[0].Layer);
                    f.Lines.Add(onA[0].Handle);
                    if (!f.Lines.Contains(onB[0].Handle)) f.Lines.Add(onB[0].Handle);
                    f.LengthFeet = gap / upf;
                    var score = 0;
                    var against = 0;
                    f.Evidence.Add("same figure " + s1.FigureId + " on both sides");

                    if (s1.End == StringEnd.ReBegun) { score += 2; f.Evidence.Add(a + " is not coded E: " + s1.FigureId + " was begun again at " + b); }
                    else if (s1.End == StringEnd.Explicit) { against++; f.Evidence.Add(a + " is coded E (the crew ended the line)"); }

                    var between = order[b] - order[a] - 1;
                    if (between <= rules.SequenceWindow)
                    {
                        score++;
                        var codes = new List<string>();
                        var sameFamilyElsewhere = false;
                        for (var i = order[a] + 1; i < order[b]; i++)
                        {
                            var s = sequence[i];
                            var code = s.Coding != null && s.Coding.Figures.Count > 0 ? s.Coding.Figures[0].FigureId
                                     : s.Coding != null && s.Coding.Leading.Count > 0 ? s.Coding.Leading[0] : FieldCodeParser.FirstToken(s.RawDescription ?? string.Empty);
                            if (!string.IsNullOrWhiteSpace(code) && !codes.Contains(code)) codes.Add(code);
                            if (s.Coding != null && s.Coding.Figures.Any(x => !string.Equals(x.FigureId, s1.FigureId, StringComparison.OrdinalIgnoreCase) &&
                                                                              string.Equals(ContinuitySettings.BaseCode(x.Code), ContinuitySettings.BaseCode(s1.Code), StringComparison.OrdinalIgnoreCase)))
                                sameFamilyElsewhere = true;
                        }
                        f.Evidence.Add(between == 0 ? "no shot between " + a + " and " + b
                                                    : Plural(between, "shot") + " between " + a + " and " + b + " (" + string.Join(", ", codes.Take(8).ToArray()) + (codes.Count > 8 ? ", ..." : string.Empty) + ")");
                        if (sameFamilyElsewhere) { against++; f.Evidence.Add("the same feature was shot on another string in between"); }
                    }
                    else f.Evidence.Add(Plural(between, "shot") + " between " + a + " and " + b + ": more than the " + rules.SequenceWindow + " the setting allows");

                    f.Evidence.Add(Ft(gap / upf) + " gap" + (spacing > 0 ? ", median shot spacing " + Ft(spacing / upf) : string.Empty) +
                                   " (limit " + Ft(limit / upf) + ")");
                    score++;

                    var deflection = Deflection(s1, s2, shots);
                    if (deflection.HasValue)
                    {
                        if (deflection.Value <= rules.MaxDeflectionDegrees) { score++; f.Evidence.Add("lines up: turns " + Deg(deflection.Value) + " across the gap"); }
                        else { against++; f.Evidence.Add("turns " + Deg(deflection.Value) + " across the gap (more than " + Deg(rules.MaxDeflectionDegrees) + ")"); }
                    }

                    string why;
                    var intentional = false;
                    if (GateAt(shots[a], shots[b], input, rules, tol, out why)) { intentional = true; f.Evidence.Add(why); }
                    else
                    {
                        bool identified;
                        var crossing = Crossing(shots[a], shots[b], lines, lineFamily, s1.Family, onA[0], onB[0], out identified);
                        if (crossing != null)
                        {
                            // Only a surveyed feature (a driveway edge, a road, a wall) explains a break;
                            // a line nothing identifies (a contour, a parcel line) only weakens the case.
                            f.Evidence.Add("crossed by " + crossing + (identified ? string.Empty : " (not an identified feature)"));
                            if (identified && rules.CrossingMeansIntentional) intentional = true; else against++;
                        }
                    }

                    if (intentional) f.Confidence = GapConfidence.Intentional;
                    else if (s1.End == StringEnd.ReBegun && against == 0 && score >= 5) f.Confidence = GapConfidence.Probable;
                    else if (score - against >= 2) f.Confidence = GapConfidence.Possible;
                    else continue;                                    // not enough to call it a gap

                    f.Issue = (f.Confidence == GapConfidence.Probable ? "Probable" : f.Confidence == GapConfidence.Possible ? "Possible" : "Intentional") +
                              " missing connection (" + s1.FigureId + " " + a + "-" + b + ")";
                    f.Reason = s1.FigureId + " stops at " + a + " and starts again at " + b + ", " + Ft(gap / upf) + " away" +
                               (f.Confidence == GapConfidence.Intentional ? ": an intentional break." : ".");
                    Finish(f, shots, null, onA[0], onB[0]);
                    result.Findings.Add(f);
                }
            }
        }

        // ------------------------------------------------------------------ evidence

        private static bool GateAt(ContinuityShot a, ContinuityShot b, ContinuityInput input, FeatureContinuity rules, double tol, out string why)
        {
            why = null;
            if (!rules.GatePostsMeanIntentional || input.GatePosts == null || input.GatePosts.Count == 0) return false;
            Func<ContinuityShot, bool> post = s =>
                input.GatePosts.Contains(s.Number) ||
                input.Shots.Any(p => input.GatePosts.Contains(p.Number) && Math.Sqrt((p.X - s.X) * (p.X - s.X) + (p.Y - s.Y) * (p.Y - s.Y)) <= Math.Max(tol, 0.5 * input.UnitsPerFoot));
            if (post(a) && post(b)) { why = "gate posts at both ends: a gate opening"; return true; }
            return false;
        }

        /// <summary>The other line (another feature) the gap crosses, described -- an identified
        /// feature first; null for none.</summary>
        private static string Crossing(ContinuityShot a, ContinuityShot b, IList<ContinuityLine> lines, IDictionary<ContinuityLine, string> lineFamily,
                                       string family, ContinuityLine skipA, ContinuityLine skipB, out bool identified)
        {
            identified = false;
            string unidentified = null;
            var minX = Math.Min(a.X, b.X); var maxX = Math.Max(a.X, b.X);
            var minY = Math.Min(a.Y, b.Y); var maxY = Math.Max(a.Y, b.Y);
            foreach (var l in lines)
            {
                if (l == skipA || l == skipB || l.IsCorrection) continue;
                string f;
                if (lineFamily.TryGetValue(l, out f) && string.Equals(f, family, StringComparison.OrdinalIgnoreCase)) continue;
                for (var i = 1; i < l.Vertices.Count; i++)
                {
                    var p = l.Vertices[i - 1];
                    var q = l.Vertices[i];
                    if (Math.Max(p[0], q[0]) < minX || Math.Min(p[0], q[0]) > maxX || Math.Max(p[1], q[1]) < minY || Math.Min(p[1], q[1]) > maxY) continue;
                    if (!Cross(a.X, a.Y, b.X, b.Y, p[0], p[1], q[0], q[1])) continue;
                    var what = (string.IsNullOrWhiteSpace(f) ? "a line" : f) + " (" + (string.IsNullOrWhiteSpace(l.Layer) ? l.Handle : l.Layer + " " + l.Handle) + ")";
                    if (!string.IsNullOrWhiteSpace(f)) { identified = true; return what; }
                    if (unidentified == null) unidentified = what;
                    break;
                }
            }
            return unidentified;
        }

        private static double MedianSpacing(CodedString s1, CodedString s2, IDictionary<string, ContinuityShot> shots)
        {
            var d = new List<double>();
            foreach (var s in new[] { s1, s2 })
                for (var i = 1; i < s.Shots.Count; i++)
                {
                    var x = PlanDistance(shots[s.Shots[i - 1]], shots[s.Shots[i]]);
                    if (x > 1e-9) d.Add(x);
                }
            if (d.Count == 0) return 0;
            d.Sort();
            return d.Count % 2 == 1 ? d[d.Count / 2] : (d[d.Count / 2 - 1] + d[d.Count / 2]) / 2.0;
        }

        /// <summary>The larger turn across the gap: from string 1's last direction to the gap, and
        /// from the gap to string 2's first direction. Null when either string has one shot.</summary>
        private static double? Deflection(CodedString s1, CodedString s2, IDictionary<string, ContinuityShot> shots)
        {
            if (s1.Shots.Count < 2 || s2.Shots.Count < 2) return null;
            var p0 = shots[s1.Shots[s1.Shots.Count - 2]];
            var p1 = shots[s1.Shots[s1.Shots.Count - 1]];
            var q0 = shots[s2.Shots[0]];
            var q1 = shots[s2.Shots[1]];
            var t1 = Turn(p0, p1, q0);
            var t2 = Turn(p1, q0, q1);
            if (!t1.HasValue || !t2.HasValue) return null;
            return Math.Max(t1.Value, t2.Value);
        }

        private static double? Turn(ContinuityShot a, ContinuityShot b, ContinuityShot c)
        {
            var ux = b.X - a.X; var uy = b.Y - a.Y;
            var vx = c.X - b.X; var vy = c.Y - b.Y;
            var lu = Math.Sqrt(ux * ux + uy * uy); var lv = Math.Sqrt(vx * vx + vy * vy);
            if (lu < 1e-9 || lv < 1e-9) return null;
            var cos = Math.Max(-1.0, Math.Min(1.0, (ux * vx + uy * vy) / (lu * lv)));
            return Math.Acos(cos) * 180.0 / Math.PI;
        }

        // ------------------------------------------------------------------ building

        private static ContinuityFinding New(string kind, CodedString str, IList<string> run, IDictionary<string, ContinuityShot> shots,
                                             ContinuityInput input, string layer)
        {
            var f = new ContinuityFinding
            {
                Kind = kind,
                FigureId = str.FigureId,
                Family = str.Family,
                Layer = !string.IsNullOrWhiteSpace(layer) ? layer : input.LayerOf != null ? input.LayerOf(str.Family) : null
            };
            f.Shots.AddRange(run.Distinct(StringComparer.OrdinalIgnoreCase));
            f.Id = kind + ":" + str.FigureId + ":" + (kind == ReviewKinds.LineMissingVertex ? string.Join("-", f.Shots.ToArray()) : run[0] + "-" + run[run.Count - 1]);
            foreach (var n in run) { var s = shots[n]; f.Geometry.Add(new[] { s.X, s.Y, s.Z }); }
            return f;
        }

        private static void Finish(ContinuityFinding f, IDictionary<string, ContinuityShot> shots, ContinuityLine line, params ContinuityLine[] ends)
        {
            var parts = new List<string> { f.Kind, f.FigureId };
            foreach (var n in f.Shots)
            {
                var s = shots[n];
                parts.Add(n + "|" + (s.RawDescription ?? string.Empty).Trim() + "|" + R(s.X) + "|" + R(s.Y) + "|" + R(s.Z));
            }
            foreach (var h in f.Lines) parts.Add("line " + h);
            if (line != null) foreach (var v in line.Vertices) parts.Add(R(v[0]) + "," + R(v[1]));
            // The ends of the lines either side of a gap: a line extended toward the other (and
            // still short of it) is a changed source, so a decision about the gap reopens.
            foreach (var l in ends ?? new ContinuityLine[0])
                if (l != null && l.Vertices.Count > 0)
                {
                    var p = l.Vertices[0];
                    var q = l.Vertices[l.Vertices.Count - 1];
                    parts.Add("ends " + l.Handle + " " + R(p[0]) + "," + R(p[1]) + " " + R(q[0]) + "," + R(q[1]));
                }
            f.Fingerprint = Fingerprint.Of(parts);
            var mid = f.Geometry.Count > 0 ? f.Geometry[f.Geometry.Count / 2] : null;
            if (f.Geometry.Count == 2) mid = new[] { (f.Geometry[0][0] + f.Geometry[1][0]) / 2, (f.Geometry[0][1] + f.Geometry[1][1]) / 2, 0.0 };
            if (mid != null) { f.X = mid[0]; f.Y = mid[1]; }
        }

        // ------------------------------------------------------------------ geometry

        /// <summary>The line runs from one shot to the other: next to each other along it (vertices
        /// between them carry no shot -- a tessellated curve), or either one is on it between
        /// vertices (a curve shot).</summary>
        private static bool Joined(ContinuityLine line, string a, string b)
        {
            var all = VerticesOf(line, a);
            var bll = VerticesOf(line, b);
            if (all.Count == 0 || bll.Count == 0) return true;
            // A closed polyline can carry its first shot at its last vertex too: any pair of the
            // shots' vertices that are joined will do.
            foreach (var va in all)
                foreach (var vb in bll)
                    if (JoinedAt(line, va, vb)) return true;
            return false;
        }

        private static List<int> VerticesOf(ContinuityLine line, string shot)
        {
            var found = new List<int>();
            for (var i = 0; i < line.ShotsAtVertex.Count; i++)
                if (line.ShotsAtVertex[i] != null && line.ShotsAtVertex[i].Contains(shot)) found.Add(i);
            return found;
        }

        private static bool JoinedAt(ContinuityLine line, int va, int vb)
        {
            if (va == vb) return true;
            var lo = Math.Min(va, vb);
            var hi = Math.Max(va, vb);
            var n = line.Vertices.Count;
            Func<int, bool> bare = i => line.ShotsAtVertex.Count <= i || line.ShotsAtVertex[i] == null || line.ShotsAtVertex[i].Count == 0;
            var inside = true;
            for (var i = lo + 1; i < hi && inside; i++) inside = bare(i);
            if (inside) return true;
            if (!line.Closed) return false;
            for (var i = hi + 1; i < n; i++) if (!bare(i)) return false;
            for (var i = 0; i < lo; i++) if (!bare(i)) return false;
            return true;
        }

        private static int VertexOf(ContinuityLine line, string shot)
        {
            for (var i = 0; i < line.ShotsAtVertex.Count; i++)
                if (line.ShotsAtVertex[i] != null && line.ShotsAtVertex[i].Contains(shot)) return i;
            return -1;
        }

        public static double Distance(ContinuityLine line, double x, double y)
        {
            var best = double.MaxValue;
            var n = line.Vertices.Count;
            var segments = line.Closed ? n : n - 1;
            for (var i = 0; i < segments; i++)
            {
                var p = line.Vertices[i];
                var q = line.Vertices[(i + 1) % n];
                best = Math.Min(best, SegmentDistance(x, y, p[0], p[1], q[0], q[1]));
            }
            return best;
        }

        private static double SegmentDistance(double x, double y, double ax, double ay, double bx, double by)
        {
            var dx = bx - ax; var dy = by - ay;
            var len = dx * dx + dy * dy;
            var t = len < 1e-18 ? 0 : Math.Max(0, Math.Min(1, ((x - ax) * dx + (y - ay) * dy) / len));
            var px = ax + t * dx; var py = ay + t * dy;
            return Math.Sqrt((x - px) * (x - px) + (y - py) * (y - py));
        }

        private static bool Cross(double ax, double ay, double bx, double by, double cx, double cy, double dx, double dy)
        {
            var d1 = Orient(cx, cy, dx, dy, ax, ay);
            var d2 = Orient(cx, cy, dx, dy, bx, by);
            var d3 = Orient(ax, ay, bx, by, cx, cy);
            var d4 = Orient(ax, ay, bx, by, dx, dy);
            return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
        }

        private static double Orient(double ax, double ay, double bx, double by, double cx, double cy)
        {
            return (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
        }

        private static double PlanDistance(ContinuityShot a, ContinuityShot b)
        {
            return Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
        }

        private static string R(double v) { return v.ToString("0.###", CultureInfo.InvariantCulture); }
        private static string Ft(double feet) { return feet.ToString("0.0", CultureInfo.InvariantCulture) + "'"; }
        private static string Deg(double d) { return d.ToString("0", CultureInfo.InvariantCulture) + " deg"; }
        private static string Plural(int n, string what) { return n.ToString(CultureInfo.InvariantCulture) + " " + what + (n == 1 ? string.Empty : "s"); }
    }
}
