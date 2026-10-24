using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FieldCodes.Easements;

namespace FieldCodes.Row
{
    /// <summary>
    /// Turns picked monuments and the right-of-way dimensions into drawable geometry:
    /// the lines between corners, the opposite right-of-way line and centerline offset to
    /// the road side, the dimensions across, corner arcs, monument marks and road names.
    ///
    /// Pure and unit tested; the CAD layer only draws what is returned. Anything the
    /// builder cannot do it says in <see cref="RowPlan.Errors"/> and builds nothing,
    /// rather than drawing a plausible-looking right of way from bad input.
    /// </summary>
    public static class RowBuilder
    {
        public static RowPlan Build(RowJob job)
        {
            var plan = new RowPlan();
            if (job == null) { plan.Errors.Add("No right-of-way job."); return plan; }

            Validate(job, plan);
            if (!plan.Ok) return plan;

            var upf = job.UnitsPerFoot;
            foreach (var m in job.Monuments) plan.Corners.Add(m.Corner(upf));

            if (job.IsRing) BuildRing(job, plan);
            else BuildRun(job, plan);

            if (plan.Ok) BuildMonuments(job, plan);
            return plan;
        }

        // ---------------------------------------------------------------- validation

        private static void Validate(RowJob job, RowPlan plan)
        {
            if (!(job.UnitsPerFoot > 0) || double.IsInfinity(job.UnitsPerFoot))
                plan.Errors.Add("Units per foot must be greater than zero.");

            var n = job.Monuments.Count;
            var minimum = job.Mode == RowMode.Run ? 2 : 3;
            if (n < minimum)
            {
                plan.Errors.Add(string.Format(CultureInfo.InvariantCulture,
                    "{0} needs at least {1} monuments; {2} picked.",
                    job.Mode == RowMode.Run ? "A run" : job.Mode == RowMode.Block ? "A block" : "An intersection", minimum, n));
                return;
            }

            for (var i = 0; i < n; i++)
            {
                var m = job.Monuments[i];
                if (m == null) { plan.Errors.Add("Monument " + (i + 1) + " is missing."); return; }
                if (m.IsOffset && !(m.OffsetFeet > 0))
                    plan.Errors.Add("Monument " + Name(job, i) + " is marked offset but has no offset distance.");
                if (m.IsOffset && (m.OffsetAzimuthDegrees < 0 || m.OffsetAzimuthDegrees > 360))
                    plan.Errors.Add("Monument " + Name(job, i) + ": the offset direction must be an azimuth from 0 to 360.");
            }
            if (!plan.Ok) return;

            var corners = job.Monuments.Select(m => m.Corner(job.UnitsPerFoot)).ToList();
            var courses = job.IsRing ? n : n - 1;
            for (var i = 0; i < courses; i++)
            {
                var j = (i + 1) % n;
                if (corners[i].DistanceTo(corners[j]) < 1e-6)
                    plan.Errors.Add("Monuments " + Name(job, i) + " and " + Name(job, j) + " mark the same corner.");
            }
            if (!plan.Ok) return;

            var turns = job.IsRing ? n : n - 2;
            for (var t = 0; t < turns; t++)
            {
                var k = job.IsRing ? t : t + 1;
                var a = RowGeometry.Dir(corners[(k - 1 + n) % n], corners[k]);
                var b = RowGeometry.Dir(corners[k], corners[(k + 1) % n]);
                if (Math.Abs(P2.Cross(a, b)) < 1e-9 && P2.Dot(a, b) < 0)
                    plan.Errors.Add("The line doubles back on itself at monument " + Name(job, k) + ".");
            }

            if (job.Mode == RowMode.Run)
            {
                if (job.Reference == RowReference.RowLine)
                {
                    if (!(job.WidthFeet > 0)) plan.Errors.Add("Give the right-of-way width.");
                }
                else if (!(job.CenterlineLeftFeet > 0) || !(job.CenterlineRightFeet > 0))
                {
                    plan.Errors.Add("Monuments on the centerline need a width on both sides. " +
                                    "For monuments on one right-of-way line, choose that instead.");
                }
                return;
            }

            if (job.Faces.Count != n)
                plan.Errors.Add(string.Format(CultureInfo.InvariantCulture,
                    "{0} monuments make {0} sides; {1} sides were described.", n, job.Faces.Count));
            else
                for (var i = 0; i < n; i++)
                    if (job.Faces[i].Kind == RowFaceKind.Frontage && !(job.Faces[i].WidthFeet > 0))
                        plan.Errors.Add("Side " + (i + 1) + " (" + Name(job, i) + " to " + Name(job, (i + 1) % n) +
                                        ") fronts a road; give its right-of-way width.");

            if (job.CornerRadiiFeet.Any(r => r < 0)) plan.Errors.Add("A corner radius cannot be negative.");

            if (plan.Ok && Math.Abs(RowGeometry.SignedArea(corners)) < 1e-6 * job.UnitsPerFoot * job.UnitsPerFoot)
                plan.Errors.Add("The monuments enclose no area.");
        }

        private static string Name(RowJob job, int index)
        {
            var m = job.Monuments[index];
            return string.IsNullOrWhiteSpace(m.PointNumber)
                ? (index + 1).ToString(CultureInfo.InvariantCulture)
                : m.PointNumber.Trim();
        }

        // ---------------------------------------------------------------------- run

        private static void BuildRun(RowJob job, RowPlan plan)
        {
            var upf = job.UnitsPerFoot;
            var corners = plan.Corners;
            var courses = corners.Count - 1;
            var sign = job.RoadSide == RowRoadSide.Left ? 1.0 : -1.0;
            var road = job.RoadName ?? string.Empty;
            var warnings = plan.Warnings;

            if (job.Reference == RowReference.RowLine)
            {
                var width = job.WidthFeet * upf;
                var opposite = RowGeometry.OffsetChain(corners, false, Repeat(sign * width, courses), warnings, "Right of way");
                AddPicked(job, plan, corners, false, Enumerable.Repeat(road, courses).ToList(), null);
                AddOffset(plan, opposite, RowLayerRole.RowLine, RowLineSource.Opposite, job.LabelOppositeLine, i => road);

                if (job.DrawCenterline)
                {
                    var centre = RowGeometry.OffsetChain(corners, false, Repeat(sign * width / 2.0, courses), warnings, "Centerline");
                    AddOffset(plan, centre, RowLayerRole.Centerline, RowLineSource.Centerline, job.LabelCenterline, i => road);
                }

                PlaceDimensions(job, plan, opposite, Enumerable.Range(0, corners.Count).ToList(), false,
                                i => road);
                PlaceRoadLabels(plan, corners, Enumerable.Range(0, courses).ToList(), opposite,
                                i => road, 0.5);
                return;
            }

            // Monuments on the centerline: the road runs out to both sides.
            var left = RowGeometry.OffsetChain(corners, false, Repeat(job.CenterlineLeftFeet * upf, courses), warnings, "Right of way, left");
            var right = RowGeometry.OffsetChain(corners, false, Repeat(-job.CenterlineRightFeet * upf, courses), warnings, "Right of way, right");
            if (job.DrawCenterline)
                AddPicked(job, plan, corners, false, Enumerable.Repeat(road, courses).ToList(), RowLayerRole.Centerline);
            AddOffset(plan, left, RowLayerRole.RowLine, RowLineSource.Opposite, job.LabelOppositeLine, i => road);
            AddOffset(plan, right, RowLayerRole.RowLine, RowLineSource.Opposite, job.LabelOppositeLine, i => road);
            PlaceAcrossDimensions(job, plan, left, right, road);
            PlaceRoadLabels(plan, corners, Enumerable.Range(0, courses).ToList(), null, i => road, 0.5);
        }

        // --------------------------------------------------------------------- ring

        private static void BuildRing(RowJob job, RowPlan plan)
        {
            var upf = job.UnitsPerFoot;
            var corners = plan.Corners;
            var n = corners.Count;
            var faces = job.Faces;

            // Counter-clockwise puts the inside on the left, so the road is on the right.
            var outward = RowGeometry.SignedArea(corners) > 0 ? -1.0 : 1.0;

            // Rounded corners on the picked ring. A corner touching a cross line cannot be
            // rounded: a line across a road is not a right-of-way line.
            var radii = new List<double>();
            for (var k = 0; k < n; k++)
            {
                var r = (k < job.CornerRadiiFeet.Count ? job.CornerRadiiFeet[k] : 0.0) * upf;
                var touchesCross = faces[k].Kind == RowFaceKind.Cross || faces[(k - 1 + n) % n].Kind == RowFaceKind.Cross;
                if (r > 0 && touchesCross)
                {
                    plan.Notes.Add("Monument " + Name(job, k) + ": a corner radius does not apply where a side crosses a road; it was left sharp.");
                    r = 0;
                }
                radii.Add(r);
            }

            var pieces = RowGeometry.RoundRing(corners, radii, plan.Warnings);
            foreach (var piece in pieces)
            {
                var face = piece.IsArc ? faces[piece.Index] : faces[piece.Index];
                var cross = !piece.IsArc && face.Kind == RowFaceKind.Cross;
                if (!cross && !job.DrawPickedLine) continue;
                var line = new RowLine
                {
                    Role = cross ? RowLayerRole.CrossLine : RowLayerRole.RowLine,
                    Source = piece.IsArc ? RowLineSource.Corner : RowLineSource.Picked,
                    Course = piece.Course,
                    FaceIndex = piece.Index,
                    RoadName = face.RoadName ?? string.Empty,
                    Data = EasementAnnotation.Describe(piece.Course),
                    Annotate = true
                };
                plan.Lines.Add(line);
                if (piece.IsArc)
                    plan.Notes.Add("Monument " + Name(job, piece.Index) + " is at the point of intersection of the rounded corner; the arc is drawn tangent to both sides.");
            }

            // Frontage runs: maximal stretches of sides that front a road, offset to the road.
            var frontage = faces.Select(f => f.Kind == RowFaceKind.Frontage).ToList();
            if (frontage.All(x => x))
            {
                OffsetRun(job, plan, outward, Enumerable.Range(0, n).ToList(), true);
            }
            else
            {
                for (var s = 0; s < n; s++)
                {
                    if (!frontage[s] || frontage[(s - 1 + n) % n]) continue;
                    var run = new List<int>();
                    for (var k = s; frontage[k % n] && run.Count < n; k++) run.Add(k % n);
                    OffsetRun(job, plan, outward, run, false);
                }
            }

            // Lines across a road: the measured length is that road's right-of-way width.
            for (var i = 0; i < n; i++)
            {
                if (faces[i].Kind != RowFaceKind.Cross) continue;
                var a = corners[i];
                var b = corners[(i + 1) % n];
                var measured = a.DistanceTo(b) / upf;
                var record = faces[i].WidthFeet;
                var within = !(record > 0) || Math.Abs(measured - record) <= job.WidthToleranceFeet;
                plan.Dimensions.Add(new RowDimension
                {
                    From = a, To = b, MeasuredFeet = measured, RecordFeet = record,
                    RoadName = faces[i].RoadName ?? string.Empty, IsCross = true, WithinTolerance = within
                });
                if (!within)
                    plan.Warnings.Add(string.Format(CultureInfo.InvariantCulture,
                        "{0}: the monuments are {1:0.00}' apart across the road but the record right of way is {2:0.##}'.",
                        string.IsNullOrWhiteSpace(faces[i].RoadName) ? "Side " + (i + 1) : faces[i].RoadName, measured, record));

                if (!string.IsNullOrWhiteSpace(faces[i].RoadName))
                {
                    var across = RowGeometry.Dir(a, b);
                    plan.RoadLabels.Add(new RowRoadLabel
                    {
                        RoadName = faces[i].RoadName,
                        At = (a + b) * 0.5,
                        Along = new P2(-across.Y, across.X)
                    });
                }
            }
        }

        private static void OffsetRun(RowJob job, RowPlan plan, double outward, IList<int> faceIndexes, bool closed)
        {
            var upf = job.UnitsPerFoot;
            var n = plan.Corners.Count;
            var runCorners = new List<P2>();
            var cornerIndexes = new List<int>();
            for (var i = 0; i < faceIndexes.Count; i++)
            {
                runCorners.Add(plan.Corners[faceIndexes[i]]);
                cornerIndexes.Add(faceIndexes[i]);
            }
            if (!closed)
            {
                var last = (faceIndexes[faceIndexes.Count - 1] + 1) % n;
                runCorners.Add(plan.Corners[last]);
                cornerIndexes.Add(last);
            }

            var courses = faceIndexes.Count;
            var distances = faceIndexes.Select(f => outward * job.Faces[f].WidthFeet * upf).ToList();
            var opposite = RowGeometry.OffsetChain(runCorners, closed, distances, plan.Warnings, "Right of way");
            Func<int, string> roadOf = i => job.Faces[faceIndexes[Math.Min(i, courses - 1)]].RoadName ?? string.Empty;

            AddOffset(plan, opposite, RowLayerRole.RowLine, RowLineSource.Opposite, job.LabelOppositeLine,
                      i => roadOf(i), faceIndexes);

            if (job.DrawCenterline)
            {
                var centre = RowGeometry.OffsetChain(runCorners, closed, distances.Select(d => d / 2.0).ToList(), plan.Warnings, "Centerline");
                AddOffset(plan, centre, RowLayerRole.Centerline, RowLineSource.Centerline, job.LabelCenterline,
                          i => roadOf(i), faceIndexes);
            }

            PlaceDimensions(job, plan, opposite, Enumerable.Range(0, runCorners.Count).ToList(), closed, roadOf);
            PlaceRoadLabels(plan, runCorners, Enumerable.Range(0, courses).ToList(), opposite, roadOf, 0.5);
        }

        // ------------------------------------------------------------------- pieces

        private static List<double> Repeat(double value, int count)
        {
            return Enumerable.Repeat(value, count).ToList();
        }

        private static void AddPicked(RowJob job, RowPlan plan, IList<P2> corners, bool closed,
                                      IList<string> roads, RowLayerRole? role)
        {
            if (!role.HasValue && !job.DrawPickedLine) return;
            var courses = closed ? corners.Count : corners.Count - 1;
            for (var i = 0; i < courses; i++)
            {
                var course = Course.Line(corners[i], corners[(i + 1) % corners.Count]);
                plan.Lines.Add(new RowLine
                {
                    Role = role ?? RowLayerRole.RowLine,
                    Source = role.HasValue ? RowLineSource.Centerline : RowLineSource.Picked,
                    Course = course, FaceIndex = i, RoadName = roads[i],
                    Data = EasementAnnotation.Describe(course),
                    Annotate = true
                });
            }
        }

        private static void AddOffset(RowPlan plan, RowGeometry.OffsetResult offset, RowLayerRole role,
                                      RowLineSource source, bool annotate, Func<int, string> road,
                                      IList<int> faceIndexes = null)
        {
            foreach (var segment in offset.Segments)
            {
                var connector = segment.Key < 0;
                var local = connector ? 0 : segment.Key;
                plan.Lines.Add(new RowLine
                {
                    Role = role,
                    Source = connector ? RowLineSource.Connector : source,
                    Course = segment.Value,
                    FaceIndex = faceIndexes != null ? faceIndexes[local] : local,
                    RoadName = road(local),
                    Data = EasementAnnotation.Describe(segment.Value),
                    Annotate = annotate && !connector
                });
            }
        }

        // ---------------------------------------------------------------- dimensions

        /// <summary>
        /// The right-of-way width, square across from the picked corner to the opposite
        /// line. The foot is accepted only where it lands ON the drawn opposite line; at an
        /// angle point it may land beyond the join, and then the neighbouring course is
        /// tried. A corner with no square dimension is reported, never forced.
        /// </summary>
        private static void PlaceDimensions(RowJob job, RowPlan plan, RowGeometry.OffsetResult offset,
                                            IList<int> cornerOrder, bool closed, Func<int, string> road)
        {
            var upf = job.UnitsPerFoot;
            var courses = offset.Courses;
            var cornerCount = closed ? courses : courses + 1;

            var wanted = new List<int>();
            if (closed || job.EveryMonumentDimension)
                for (var k = 0; k < cornerCount; k++) wanted.Add(k);
            else
            {
                wanted.Add(0);
                wanted.Add(courses);
            }

            foreach (var k in wanted)
            {
                var runCorner = offset.Corners[k % offset.Corners.Length];
                var order = new List<int>();
                if (closed || k < courses) order.Add(k % courses);
                if (closed || k > 0) order.Add((k - 1 + courses) % courses);

                var placed = false;
                foreach (var c in order)
                {
                    var foot = runCorner + offset.Normals[c] * offset.Distances[c];
                    var along = P2.Dot(foot - offset.Starts[c], offset.Directions[c]);
                    var length = P2.Dot(offset.Ends[c] - offset.Starts[c], offset.Directions[c]);
                    if (along < -1e-6 || along > length + 1e-6) continue;

                    if (plan.Dimensions.Any(d => d.From.DistanceTo(runCorner) < 1e-6 && d.To.DistanceTo(foot) < 1e-6)) { placed = true; break; }
                    plan.Dimensions.Add(new RowDimension
                    {
                        From = runCorner, To = foot, MeasuredFeet = Math.Abs(offset.Distances[c]) / upf,
                        RecordFeet = Math.Abs(offset.Distances[c]) / upf, RoadName = road(c)
                    });
                    placed = true;
                    break;
                }

                if (!placed)
                    plan.Warnings.Add("No square right-of-way dimension fits at monument " + (k + 1) +
                                      "; the offset line does not reach straight across from it.");
            }
        }

        private static void PlaceAcrossDimensions(RowJob job, RowPlan plan, RowGeometry.OffsetResult left,
                                                  RowGeometry.OffsetResult right, string road)
        {
            var upf = job.UnitsPerFoot;
            var courses = left.Courses;
            var wanted = new List<int> { 0, courses };
            if (job.EveryMonumentDimension)
                wanted = Enumerable.Range(0, courses + 1).ToList();

            foreach (var k in wanted)
            {
                var corner = plan.Corners[k];
                var order = new List<int>();
                if (k < courses) order.Add(k);
                if (k > 0) order.Add(k - 1);

                var placed = false;
                foreach (var c in order)
                {
                    var a = corner + left.Normals[c] * left.Distances[c];
                    var b = corner + right.Normals[c] * right.Distances[c];
                    if (!OnSegment(a, left, c) || !OnSegment(b, right, c)) continue;
                    plan.Dimensions.Add(new RowDimension
                    {
                        From = a, To = b, MeasuredFeet = a.DistanceTo(b) / upf,
                        RecordFeet = a.DistanceTo(b) / upf, RoadName = road
                    });
                    placed = true;
                    break;
                }
                if (!placed)
                    plan.Warnings.Add("No square right-of-way dimension fits at monument " + (k + 1) + ".");
            }
        }

        private static bool OnSegment(P2 foot, RowGeometry.OffsetResult o, int c)
        {
            var along = P2.Dot(foot - o.Starts[c], o.Directions[c]);
            var length = P2.Dot(o.Ends[c] - o.Starts[c], o.Directions[c]);
            return along >= -1e-6 && along <= length + 1e-6;
        }

        // -------------------------------------------------------------- road names

        private static void PlaceRoadLabels(RowPlan plan, IList<P2> corners, IList<int> courseIndexes,
                                            RowGeometry.OffsetResult offset, Func<int, string> road, double fraction)
        {
            var best = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var i in courseIndexes)
            {
                var name = road(i);
                if (string.IsNullOrWhiteSpace(name)) continue;
                int current;
                if (!best.TryGetValue(name, out current) ||
                    corners[i].DistanceTo(corners[(i + 1) % corners.Count]) >
                    corners[current].DistanceTo(corners[(current + 1) % corners.Count]))
                    best[name] = i;
            }

            foreach (var pair in best)
            {
                var i = pair.Value;
                var a = corners[i];
                var b = corners[(i + 1) % corners.Count];
                var at = a + (b - a) * fraction;
                if (offset != null) at = at + offset.Normals[i] * (offset.Distances[i] / 2.0);
                plan.RoadLabels.Add(new RowRoadLabel { RoadName = pair.Key, At = at, Along = RowGeometry.Dir(a, b) });
            }
        }

        // ---------------------------------------------------------------- monuments

        private static void BuildMonuments(RowJob job, RowPlan plan)
        {
            var corners = plan.Corners;
            var n = corners.Count;
            var upf = job.UnitsPerFoot;
            var ring = job.IsRing;

            var inwardIsLeft = true;
            var awayFromRoadSign = 1.0;
            if (ring) inwardIsLeft = RowGeometry.SignedArea(corners) > 0;
            else if (job.Reference == RowReference.RowLine) awayFromRoadSign = job.RoadSide == RowRoadSide.Left ? -1.0 : 1.0;

            for (var k = 0; k < n; k++)
            {
                P2 inDir, outDir;
                if (ring)
                {
                    inDir = RowGeometry.Dir(corners[(k - 1 + n) % n], corners[k]);
                    outDir = RowGeometry.Dir(corners[k], corners[(k + 1) % n]);
                }
                else
                {
                    outDir = k < n - 1 ? RowGeometry.Dir(corners[k], corners[k + 1]) : RowGeometry.Dir(corners[k - 1], corners[k]);
                    inDir = k > 0 ? RowGeometry.Dir(corners[k - 1], corners[k]) : outDir;
                }

                var bisector = (inDir.LeftNormal() + outDir.LeftNormal()).Normalized();
                if (bisector.Length < 0.5) bisector = outDir.LeftNormal();
                var note = ring ? (inwardIsLeft ? bisector : bisector * -1.0) : bisector * awayFromRoadSign;

                var mark = new RowMonumentMark
                {
                    Monument = job.Monuments[k],
                    Index = k,
                    Location = job.Monuments[k].Location,
                    Corner = corners[k],
                    NoteDirection = note,
                    AlongDirection = outDir
                };
                if (job.Monuments[k].IsOffset)
                {
                    mark.Tie = EasementAnnotation.Describe(Course.Line(mark.Location, mark.Corner));
                    plan.Notes.Add(string.Format(CultureInfo.InvariantCulture,
                        "Monument {0} is an offset monument: the corner it marks is {1:0.00}' away.", Name(job, k), job.Monuments[k].OffsetFeet));
                }
                plan.Monuments.Add(mark);
            }
        }
    }
}
