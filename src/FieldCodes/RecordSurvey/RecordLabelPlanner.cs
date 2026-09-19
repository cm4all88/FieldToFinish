using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FieldCodes.Drafting;
using FieldCodes.Easements;
using FieldCodes.Linework;
using FieldCodes.Settings;

namespace FieldCodes.RecordSurvey
{
    /// <summary>A label the planner wants placed: the text, where, at what angle, and how it was decided.</summary>
    public sealed class PlannedRecordLabel
    {
        public string CallId { get; set; }
        public string Figure { get; set; }
        /// <summary>"Line", "Curve", "Lot", "Area", "Monument".</summary>
        public string Kind { get; set; }
        public List<string> Lines { get; private set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double RotationRadians { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        /// <summary>"outside", "inside", "left", "right", "on".</summary>
        public string Side { get; set; }
        public double AlongRatio { get; set; }
        /// <summary>The label could not be placed clear of everything; it sits at the least bad spot and is flagged.</summary>
        public bool Conflict { get; set; }
        /// <summary>In table mode the label is a tag (L1, C3) and the values go in the table.</summary>
        public bool InTable { get; set; }
        public string Tag { get; set; }
        public string Note { get; set; }
        /// <summary>The course the label belongs to, for a Civil 3D label anchored to its entity.</summary>
        public Course Course { get; set; }

        public PlannedRecordLabel() { Lines = new List<string>(); }

        public string Text { get { return string.Join("\n", Lines.ToArray()); } }
    }

    /// <summary>Anything a label must not cover: a monument symbol, another label, a course.</summary>
    public sealed class LabelObstacle
    {
        public double X { get; set; }
        public double Y { get; set; }
        /// <summary>Circle radius for point obstacles; zero for boxes.</summary>
        public double Radius { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public double RotationRadians { get; set; }
        public string What { get; set; }
    }

    public sealed class LabelPlanOptions
    {
        public double TextHeight { get; set; }
        public double Offset { get; set; }
        /// <summary>Measures a string's width in drawing units. The CAD layer measures the real style; tests estimate.</summary>
        public Func<string, double> MeasureWidth { get; set; }
        public double LineSpacing { get; set; }
        public RecordSurveySettings Settings { get; set; }
        public double UnitsPerFoot { get; set; }
        /// <summary>Monument symbol radius, drawing units, kept clear at corners.</summary>
        public double MonumentClearance { get; set; }

        public LabelPlanOptions()
        {
            TextHeight = 1.0;
            Offset = 0.5;
            LineSpacing = 1.5;
            UnitsPerFoot = 1.0;
            Settings = new RecordSurveySettings();
            MonumentClearance = 0;
            MeasureWidth = s => (s ?? string.Empty).Replace("%%d", "d").Length * 0.75;
        }
    }

    public sealed class LabelPlan
    {
        public List<PlannedRecordLabel> Labels { get; private set; }
        public List<string> Notes { get; private set; }
        /// <summary>Courses tagged into the table: tag, call id, text lines.</summary>
        public List<PlannedRecordLabel> TableRows { get; private set; }
        public LabelPlan() { Labels = new List<PlannedRecordLabel>(); Notes = new List<string>(); TableRows = new List<PlannedRecordLabel>(); }
    }

    /// <summary>
    /// Decides the text and the position of every label on a reconstructed survey. Text
    /// comes from the built geometry and the call's record attributes; position is chosen
    /// from a ranked list of candidates along each course, taking the first that clears
    /// every other course, every monument and every label already placed. Shared lines are
    /// labelled once. Text is never upside down. Pure and unit tested; the CAD layer only
    /// supplies the real text measurement.
    /// </summary>
    public static class RecordLabelPlanner
    {
        public static LabelPlan Plan(IList<TraverseResult> figures, IList<SharedLine> shared, IList<LabelObstacle> fixedObstacles,
                                     LabelPlanOptions options)
        {
            options = options ?? new LabelPlanOptions();
            var s = options.Settings ?? new RecordSurveySettings();
            var plan = new LabelPlan();
            if (figures == null || figures.Count == 0 || !s.LabelsEnabled) return plan;

            var obstacles = new List<LabelObstacle>(fixedObstacles ?? new List<LabelObstacle>());
            var courses = figures.SelectMany(f => f.Courses.Where(c => c.Placed).Select(c => c.Course)).ToList();
            var placed = new List<PlannedRecordLabel>();
            var lineTags = 0;
            var curveTags = 0;

            // Which figure owns each shared line: the first owner labels it.
            var labelOnce = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (shared != null)
                foreach (var sl in shared.Where(x => x.IsShared))
                    foreach (var owner in sl.Owners.Skip(1)) labelOnce.Add(owner.Value);

            foreach (var figure in figures)
            {
                var loop = figure.Courses.Where(c => c.Placed).Select(c => c.Course).ToList();
                var ccw = figure.Closure != null && figure.Closure.Closed && loop.Count >= 3 && Loops.SignedArea(loop) > 0;
                for (var i = 0; i < figure.Courses.Count; i++)
                {
                    var tc = figure.Courses[i];
                    if (!tc.Placed) continue;
                    var call = tc.Call;
                    if (labelOnce.Contains(call.Id)) continue;
                    var standard = s.FindEntity(call.ObjectType);
                    if (standard != null && !standard.Label) continue;

                    var lines = call.Kind == CallKind.Line ? LineText(tc, s) : CurveText(tc, s);
                    var label = new PlannedRecordLabel { CallId = call.Id, Figure = figure.Figure, Kind = call.Kind == CallKind.Line ? "Line" : "Curve", Course = tc.Course, AlongRatio = 0.5 };
                    label.Lines.AddRange(lines);

                    var room = tc.Course.Kind == CourseKind.Line ? tc.Course.Length : tc.Course.Start.DistanceTo(tc.Course.End);
                    var fits = FitsAlong(lines, room, options);
                    if (s.LabelTableMode || (s.LabelAutoMode && !fits))
                    {
                        label.InTable = true;
                        label.Tag = call.Kind == CallKind.Line
                            ? s.LinePrefix + (++lineTags).ToString(CultureInfo.InvariantCulture)
                            : s.CurvePrefix + (++curveTags).ToString(CultureInfo.InvariantCulture);
                        plan.TableRows.Add(label);
                        var tagLabel = new PlannedRecordLabel { CallId = call.Id, Figure = figure.Figure, Kind = label.Kind, Course = tc.Course, InTable = true, Tag = label.Tag };
                        tagLabel.Lines.Add(label.Tag);
                        Place(tagLabel, tc.Course, ccw, courses, obstacles, placed, options);
                        placed.Add(tagLabel);
                        plan.Labels.Add(tagLabel);
                        continue;
                    }
                    if (!fits) label.Note = "Longer than the course it labels.";
                    Place(label, tc.Course, ccw, courses, obstacles, placed, options);
                    placed.Add(label);
                    plan.Labels.Add(label);
                }

                // Lot number and area in the middle of a closed figure.
                if (s.LabelLots && figure.Closure != null && figure.Closure.Closed && loop.Count >= 3 && !string.IsNullOrEmpty(figure.Figure))
                {
                    var lot = new PlannedRecordLabel { Figure = figure.Figure, Kind = "Lot", AlongRatio = 0 };
                    lot.Lines.Add(LotText(figure.Figure, s));
                    if (s.LabelAreas) lot.Lines.Add(AreaText(figure.Closure.Area, s));
                    var c = Centroid(figure.Vertices);
                    lot.X = c.X; lot.Y = c.Y; lot.RotationRadians = 0;
                    Size(lot, options, s.LotTextHeightPlotted / s.TextHeightPlotted);
                    lot.Conflict = Collides(lot, courses, null, obstacles, placed, options);
                    if (lot.Conflict) lot.Note = "The figure's centroid is not clear; move the lot label by hand.";
                    placed.Add(lot);
                    plan.Labels.Add(lot);
                }
            }

            foreach (var l in plan.Labels.Where(l => l.Conflict))
                plan.Notes.Add((l.CallId ?? l.Figure) + ": " + (l.Note ?? "no clear position; placed at the least crowded candidate."));
            return plan;
        }

        // ------------------------------------------------------------ text

        public static List<string> LineText(TraverseCourse tc, RecordSurveySettings s)
        {
            var lines = new List<string>();
            var call = tc.Call;
            var az = tc.Course.Kind == CourseKind.Line
                ? SurveyDirection.AzimuthFromVector(tc.Course.End.X - tc.Course.Start.X, tc.Course.End.Y - tc.Course.Start.Y)
                : tc.AzimuthUsed;
            // The label reads the course as written on the record, not as traversed.
            if (call.Reversed) az = Geometry.Angles.NormalizeDegrees(az + 180.0);
            var built = Bearing(az, s) + " " + Distance(tc.LengthUsedFeet, s);

            if (s.LabelRecordAndMeasured && call.HasRecordAndMeasured)
            {
                var m = call.Measured;
                var mText = m.Complete ? Bearing(m.AzimuthDegrees.Value, s) + " " + Distance(m.DistanceFeet.Value, s) : built;
                lines.Add(Fill(s.MeasuredLabelFormat, mText, "M"));
                foreach (var r in call.Records.Where(r => r.Complete))
                    lines.Add(Fill(s.RecordLabelFormat, Bearing(r.AzimuthDegrees.Value, s) + " " + Distance(r.DistanceFeet.Value, s), r.SourceId.Length > 0 ? r.SourceId : "R"));
                return lines;
            }

            if (s.Stacked)
            {
                lines.Add(Bearing(az, s));
                lines.Add(Distance(tc.LengthUsedFeet, s) + Suffix(call, tc));
            }
            else lines.Add(built + Suffix(call, tc));
            return lines;
        }

        private static string Suffix(SurveyCall call, TraverseCourse tc)
        {
            // A course built from one named record source says so, so the label never passes a
            // record value off as measured or vice versa.
            if (tc.Basis == ValueBasis.Measured && call.Records.Any(x => !x.Empty)) return " (M)";
            var primary = call.Record;
            if (primary != null && !string.IsNullOrEmpty(primary.SourceId) && primary.SourceId != "C") return " (" + primary.SourceId + ")";
            if (tc.Basis == ValueBasis.Calculated) return " (C)";
            return string.Empty;
        }

        public static List<string> CurveText(TraverseCourse tc, RecordSurveySettings s)
        {
            var lines = new List<string>();
            var sol = tc.CurveSolution;
            if (sol == null || !sol.Ok) { lines.Add("CURVE"); return lines; }
            lines.Add("R=" + Distance(sol.Radius / 1.0, s));
            lines.Add("L=" + Distance(sol.ArcLength, s));
            lines.Add("Δ=" + SurveyDirection.FormatAzimuth(sol.DeltaDegrees, s.BearingSecondsDecimals, "°"));
            if (s.CurveShowChord)
                lines.Add("CH=" + Bearing(tc.Placement != null ? tc.Placement.ChordAzimuthDegrees : tc.AzimuthUsed, s) + " " + Distance(sol.ChordLength, s));
            if (s.CurveShowTangent && !double.IsNaN(sol.TangentLength)) lines.Add("T=" + Distance(sol.TangentLength, s));
            return lines;
        }

        public static string Bearing(double azimuth, RecordSurveySettings s)
        {
            return SurveyDirection.FormatBearing(azimuth, s.BearingSecondsDecimals, "°", s.BearingSpaces);
        }

        public static string Distance(double feet, RecordSurveySettings s)
        {
            return SurveyDirection.FormatDistance(feet, s.DistanceDecimals, s.FootSymbol);
        }

        private static string Fill(string format, string value, string source)
        {
            return (format ?? "{value}").Replace("{value}", value).Replace("{source}", source).Trim();
        }

        public static string LotText(string figure, RecordSurveySettings s)
        {
            var name = figure ?? string.Empty;
            var lot = name;
            var block = string.Empty;
            var parts = name.Split(' ');
            if (parts.Length >= 2 && (parts[0].Equals("Lot", StringComparison.OrdinalIgnoreCase) || parts[0].Equals("Tract", StringComparison.OrdinalIgnoreCase)))
            {
                lot = parts[1];
                var b = Array.IndexOf(parts, "Block");
                if (b >= 0 && b + 1 < parts.Length) block = parts[b + 1];
                if (parts[0].Equals("Tract", StringComparison.OrdinalIgnoreCase)) return "TRACT " + lot.ToUpperInvariant();
            }
            return (s.LotLabelFormat ?? "LOT {lot}").Replace("{lot}", lot).Replace("{block}", block).Trim().ToUpperInvariant();
        }

        public static string AreaText(double squareFeet, RecordSurveySettings s)
        {
            return (s.AreaLabelFormat ?? "{sqft} SQ. FT.").Replace("{sqft}", squareFeet.ToString("N0", CultureInfo.InvariantCulture))
                .Replace("{acres}", (squareFeet / 43560.0).ToString("0.###", CultureInfo.InvariantCulture));
        }

        public static bool FitsAlong(IList<string> lines, double room, LabelPlanOptions options)
        {
            if (lines == null || lines.Count == 0) return true;
            var longest = lines.Max(l => options.MeasureWidth(l) * options.TextHeight);
            return longest <= room * 0.95;
        }

        // ------------------------------------------------------------ placement

        private static void Place(PlannedRecordLabel label, Course course, bool figureCcw, List<Course> courses,
                                  List<LabelObstacle> obstacles, List<PlannedRecordLabel> placed, LabelPlanOptions options)
        {
            Size(label, options, 1.0);
            PlannedRecordLabel best = null;
            var bestScore = double.MaxValue;
            // Outside the figure first (for a counter-clockwise loop the outside is to the right of travel),
            // at the midpoint, then a little either way, then the other side, then a further ring.
            var outsideLeft = !figureCcw;
            foreach (var ring in new[] { 1, 2 })
            foreach (var side in new[] { outsideLeft, !outsideLeft })
            foreach (var t in new[] { 0.5, 0.4, 0.6, 0.3, 0.7 })
            {
                var candidate = Candidate(label, course, t, side, ring, options);
                var score = Overlap(candidate, course, courses, obstacles, placed, options);
                if (score <= 0)
                {
                    Copy(candidate, label);
                    label.Side = side == outsideLeft ? "outside" : "inside";
                    label.AlongRatio = t;
                    label.Conflict = false;
                    return;
                }
                if (score < bestScore) { bestScore = score; best = candidate; best.Side = side == outsideLeft ? "outside" : "inside"; best.AlongRatio = t; }
            }
            Copy(best, label);
            label.Side = best.Side;
            label.AlongRatio = best.AlongRatio;
            label.Conflict = true;
            label.Note = label.Note ?? "No clear position; placed at the least crowded candidate. Check it.";
        }

        private static void Copy(PlannedRecordLabel from, PlannedRecordLabel to)
        {
            to.X = from.X; to.Y = from.Y; to.RotationRadians = from.RotationRadians; to.Width = from.Width; to.Height = from.Height;
        }

        private static void Size(PlannedRecordLabel label, LabelPlanOptions options, double heightFactor)
        {
            var h = options.TextHeight * heightFactor;
            label.Width = label.Lines.Count == 0 ? 0 : label.Lines.Max(l => options.MeasureWidth(l) * h);
            label.Height = label.Lines.Count == 0 ? h : h + (label.Lines.Count - 1) * h * options.LineSpacing;
        }

        private static PlannedRecordLabel Candidate(PlannedRecordLabel label, Course course, double t, bool left, int ring, LabelPlanOptions options)
        {
            var along = course.Length * t;
            var p = course.PointAt(along);
            var dir = course.DirectionAt(along);
            var normal = left ? dir.LeftNormal() : dir.LeftNormal() * -1.0;
            var gap = options.Offset + (ring - 1) * options.TextHeight * 1.6 + label.Height / 2.0;
            var centre = p + normal * gap;
            var rotation = LineLabelPlanner.NormalizeReadable(Math.Atan2(dir.Y, dir.X));
            return new PlannedRecordLabel { X = centre.X, Y = centre.Y, RotationRadians = rotation, Width = label.Width, Height = label.Height };
        }

        /// <summary>A collision score: zero when clear, larger the worse. Courses other than the label's own count, monuments count, placed labels count.</summary>
        private static double Overlap(PlannedRecordLabel c, Course own, List<Course> courses, List<LabelObstacle> obstacles,
                                      List<PlannedRecordLabel> placed, LabelPlanOptions options)
        {
            var score = 0.0;
            var corners = Corners(c.X, c.Y, c.Width, c.Height, c.RotationRadians);
            foreach (var course in courses)
            {
                if (ReferenceEquals(course, own)) continue;
                if (CrossesBox(course, corners)) score += 1.0;
            }
            foreach (var o in obstacles ?? new List<LabelObstacle>())
            {
                if (o.Radius > 0)
                {
                    if (PointToBox(o.X, o.Y, corners) < o.Radius) score += 1.0;
                }
                else if (BoxesOverlap(corners, Corners(o.X, o.Y, o.Width, o.Height, o.RotationRadians))) score += 1.0;
            }
            foreach (var p in placed)
                if (BoxesOverlap(corners, Corners(p.X, p.Y, p.Width, p.Height, p.RotationRadians))) score += 1.0;
            return score;
        }

        private static bool Collides(PlannedRecordLabel c, List<Course> courses, Course own, List<LabelObstacle> obstacles,
                                     List<PlannedRecordLabel> placed, LabelPlanOptions options)
        {
            return Overlap(c, own, courses, obstacles, placed, options) > 0;
        }

        internal static P2[] Corners(double cx, double cy, double w, double h, double rotation)
        {
            var u = new P2(Math.Cos(rotation), Math.Sin(rotation));
            var v = new P2(-Math.Sin(rotation), Math.Cos(rotation));
            var centre = new P2(cx, cy);
            var hw = w / 2.0; var hh = h / 2.0;
            return new[] { centre - u * hw - v * hh, centre + u * hw - v * hh, centre + u * hw + v * hh, centre - u * hw + v * hh };
        }

        /// <summary>Separating axis test for two rectangles.</summary>
        internal static bool BoxesOverlap(P2[] a, P2[] b)
        {
            foreach (var poly in new[] { a, b })
            for (var i = 0; i < 4; i++)
            {
                var edge = poly[(i + 1) % 4] - poly[i];
                var axis = new P2(-edge.Y, edge.X);
                double minA = double.MaxValue, maxA = double.MinValue, minB = double.MaxValue, maxB = double.MinValue;
                foreach (var p in a) { var d = P2.Dot(p, axis); minA = Math.Min(minA, d); maxA = Math.Max(maxA, d); }
                foreach (var p in b) { var d = P2.Dot(p, axis); minB = Math.Min(minB, d); maxB = Math.Max(maxB, d); }
                if (maxA <= minB || maxB <= minA) return false;
            }
            return true;
        }

        internal static bool CrossesBox(Course course, P2[] box)
        {
            // Sample the course finely enough for an arc; a line needs only its ends and the edges.
            var samples = course.Kind == CourseKind.Line ? 2 : Math.Max(8, (int)(course.Sweep * 8));
            var prev = course.PointAt(0);
            if (InsideBox(prev, box)) return true;
            for (var i = 1; i <= samples; i++)
            {
                var p = course.PointAt(course.Length * i / samples);
                if (InsideBox(p, box)) return true;
                for (var e = 0; e < 4; e++)
                    if (SegmentsCross(prev, p, box[e], box[(e + 1) % 4])) return true;
                prev = p;
            }
            return false;
        }

        internal static bool InsideBox(P2 p, P2[] box)
        {
            for (var i = 0; i < 4; i++)
            {
                var edge = box[(i + 1) % 4] - box[i];
                if (P2.Cross(edge, p - box[i]) < -1e-9) return false;
            }
            return true;
        }

        private static bool SegmentsCross(P2 a0, P2 a1, P2 b0, P2 b1)
        {
            var d1 = P2.Cross(b1 - b0, a0 - b0);
            var d2 = P2.Cross(b1 - b0, a1 - b0);
            var d3 = P2.Cross(a1 - a0, b0 - a0);
            var d4 = P2.Cross(a1 - a0, b1 - a0);
            return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
        }

        private static double PointToBox(double x, double y, P2[] box)
        {
            var p = new P2(x, y);
            if (InsideBox(p, box)) return 0;
            var best = double.MaxValue;
            for (var i = 0; i < 4; i++)
            {
                var a = box[i]; var b = box[(i + 1) % 4];
                var ab = b - a;
                var t = Math.Max(0, Math.Min(1, P2.Dot(p - a, ab) / Math.Max(1e-12, P2.Dot(ab, ab))));
                best = Math.Min(best, p.DistanceTo(a + ab * t));
            }
            return best;
        }

        internal static P2 Centroid(IList<P2> vertices)
        {
            if (vertices == null || vertices.Count == 0) return new P2(0, 0);
            var pts = vertices.Count > 1 && vertices[0].DistanceTo(vertices[vertices.Count - 1]) < 1e-6 ? vertices.Take(vertices.Count - 1).ToList() : vertices.ToList();
            if (pts.Count < 3) return pts[0];
            double a = 0, cx = 0, cy = 0;
            for (var i = 0; i < pts.Count; i++)
            {
                var p = pts[i]; var q = pts[(i + 1) % pts.Count];
                var cross = p.X * q.Y - q.X * p.Y;
                a += cross; cx += (p.X + q.X) * cross; cy += (p.Y + q.Y) * cross;
            }
            if (Math.Abs(a) < 1e-9) return new P2(pts.Average(p => p.X), pts.Average(p => p.Y));
            return new P2(cx / (3 * a), cy / (3 * a));
        }
    }
}
