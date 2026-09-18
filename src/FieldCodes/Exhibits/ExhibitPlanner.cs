using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using FieldCodes.Easements;
using FieldCodes.Settings;

namespace FieldCodes.Exhibits
{
    /// <summary>An axis-aligned rectangle on the sheet, inches.</summary>
    public struct SheetRect
    {
        public readonly double MinX, MinY, MaxX, MaxY;

        public SheetRect(double minX, double minY, double maxX, double maxY)
        {
            MinX = Math.Min(minX, maxX); MaxX = Math.Max(minX, maxX);
            MinY = Math.Min(minY, maxY); MaxY = Math.Max(minY, maxY);
        }

        public double Width { get { return MaxX - MinX; } }
        public double Height { get { return MaxY - MinY; } }

        public bool Overlaps(SheetRect o, double slack)
        {
            return MinX < o.MaxX - slack && o.MinX < MaxX - slack && MinY < o.MaxY - slack && o.MinY < MaxY - slack;
        }

        public bool Within(SheetRect o, double slack)
        {
            return MinX >= o.MinX - slack && MaxX <= o.MaxX + slack && MinY >= o.MinY - slack && MaxY <= o.MaxY + slack;
        }

        public bool Contains(P2 p) { return p.X >= MinX && p.X <= MaxX && p.Y >= MinY && p.Y <= MaxY; }

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "({0:0.00},{1:0.00})-({2:0.00},{3:0.00})", MinX, MinY, MaxX, MaxY);
        }
    }

    /// <summary>How the viewport frames the easements.</summary>
    public sealed class ExhibitFit
    {
        /// <summary>Feet per paper inch.</summary>
        public double Scale { get; set; }
        public double RotationDegrees { get; set; }
        public P2 ViewCenter { get; set; }
        /// <summary>False when even the smallest scale offered cannot show everything.</summary>
        public bool Fits { get; set; }
        public double NorthUpScale { get; set; }
        public string Reason { get; set; }
    }

    public sealed class AreaRow
    {
        public string Label { get; set; }
        public double SquareFeet { get; set; }
        public bool Combined { get; set; }
    }

    /// <summary>
    /// The deterministic parts of an exhibit: which scale, whether to turn the view, where a
    /// model point lands on the sheet, the scale bar, the area rows and whether an exhibit is
    /// out of date. The view is turned; survey geometry never is.
    /// </summary>
    public static class ExhibitPlanner
    {
        /// <summary>
        /// Picks the largest readable scale (the fewest feet per inch) that shows every point in the
        /// viewport with the margin kept clear. With rotation allowed, the view is turned to the
        /// geometry's long axis only when that gives a larger scale than north up.
        /// </summary>
        public static ExhibitFit Choose(IList<P2> points, double viewportWidthIn, double viewportHeightIn, IList<double> scales,
                                        double margin, bool allowRotate, double unitsPerFoot, double? forcedScale, double? forcedRotation)
        {
            if (points == null || points.Count == 0) throw new ArgumentException("Nothing to frame.");
            var sorted = scales.OrderBy(s => s).ToList();

            Func<double, ExhibitFit> fitAt = twist =>
            {
                var rotated = points.Select(p => Rotate(p, twist)).ToList();
                double minX = rotated.Min(p => p.X), maxX = rotated.Max(p => p.X), minY = rotated.Min(p => p.Y), maxY = rotated.Max(p => p.Y);
                var usableW = viewportWidthIn * (1 - 2 * margin);
                var usableH = viewportHeightIn * (1 - 2 * margin);
                var needed = Math.Max((maxX - minX) / unitsPerFoot / usableW, (maxY - minY) / unitsPerFoot / usableH);
                var chosen = forcedScale ?? sorted.FirstOrDefault(s => s >= needed - 1e-9);
                var fits = forcedScale.HasValue ? forcedScale.Value >= needed - 1e-9 : chosen > 0;
                if (chosen <= 0) chosen = sorted.Count > 0 ? sorted[sorted.Count - 1] : needed;
                var center = Rotate(new P2((minX + maxX) / 2, (minY + maxY) / 2), -twist);
                return new ExhibitFit { Scale = chosen, RotationDegrees = twist, ViewCenter = center, Fits = fits };
            };

            var north = fitAt(forcedRotation ?? 0);
            north.NorthUpScale = fitAt(0).Scale;
            if (forcedRotation.HasValue) { north.Reason = "view rotation set by the drafter"; return north; }
            north.Reason = "north up";
            if (!allowRotate) return north;

            var axis = LongAxisDegrees(points);
            var best = north;
            foreach (var candidate in new[] { -axis, 90 - axis })
            {
                var twist = Normalize(Math.Round(candidate));
                if (Math.Abs(twist) < 0.5) continue;
                var fit = fitAt(twist);
                if (fit.Fits && fit.Scale < best.Scale - 1e-9)
                {
                    fit.NorthUpScale = north.NorthUpScale;
                    fit.Reason = "view turned " + twist.ToString("0", CultureInfo.InvariantCulture) + " degrees: 1\" = " +
                                 fit.Scale.ToString("0", CultureInfo.InvariantCulture) + "' instead of 1\" = " +
                                 north.Scale.ToString("0", CultureInfo.InvariantCulture) + "' north up";
                    best = fit;
                }
            }
            return best;
        }

        /// <summary>The direction (degrees counter-clockwise from east) of the narrowest bounding box.</summary>
        public static double LongAxisDegrees(IList<P2> points)
        {
            var bestAngle = 0.0;
            var bestArea = double.MaxValue;
            var bestLong = 0.0;
            for (var tenths = 0; tenths < 1800; tenths += 5)
            {
                var a = tenths / 10.0;
                var r = points.Select(p => Rotate(p, -a)).ToList();
                double w = r.Max(p => p.X) - r.Min(p => p.X), h = r.Max(p => p.Y) - r.Min(p => p.Y);
                if (w * h < bestArea - 1e-9) { bestArea = w * h; bestAngle = a; bestLong = w >= h ? 0 : 90; }
            }
            return Normalize(bestAngle + bestLong);
        }

        /// <summary>
        /// Where a model point lands on the sheet: the viewport shows <paramref name="viewCenter"/> at
        /// <paramref name="viewportCenter"/>, turned by the view rotation, at the scale.
        /// </summary>
        public static P2 ToPaper(P2 model, P2 viewCenter, double scale, double rotationDegrees, P2 viewportCenter, double unitsPerFoot)
        {
            var d = Rotate(model - viewCenter, rotationDegrees);
            return new P2(viewportCenter.X + d.X / unitsPerFoot / scale, viewportCenter.Y + d.Y / unitsPerFoot / scale);
        }

        /// <summary>The paper direction of model north (0,1), for the north arrow.</summary>
        public static P2 NorthOnPaper(double rotationDegrees)
        {
            return Rotate(new P2(0, 1), rotationDegrees);
        }

        public static P2 Rotate(P2 p, double degrees)
        {
            var a = degrees * Math.PI / 180;
            double c = Math.Cos(a), s = Math.Sin(a);
            return new P2(p.X * c - p.Y * s, p.X * s + p.Y * c);
        }

        private static double Normalize(double degrees)
        {
            while (degrees > 90) degrees -= 180;
            while (degrees <= -90) degrees += 180;
            return degrees;
        }

        /// <summary>
        /// Scale bar marks: whole multiples of the scale close to the preferred length, split in
        /// four. Returns (feet, inches) pairs from 0 to the end.
        /// </summary>
        public static List<KeyValuePair<double, double>> ScaleBar(double scale, double preferredLengthIn)
        {
            var feet = Math.Max(1, Math.Round(preferredLengthIn)) * scale;
            var marks = new List<KeyValuePair<double, double>>();
            for (var i = 0; i <= 4; i++) marks.Add(new KeyValuePair<double, double>(feet * i / 4, feet * i / 4 / scale));
            return marks;
        }

        /// <summary>Rows for the area summary: each easement (and component when there are several), then a
        /// combined row under the profile's label -- never called a legal total unless the profile names it so.</summary>
        public static List<AreaRow> AreaRows(IList<EasementRecord> records, ExhibitSettings settings)
        {
            var rows = new List<AreaRow>();
            foreach (var r in records)
            {
                rows.Add(new AreaRow { Label = r.Title, SquareFeet = r.DisplayAreaSquareFeet });
                if (r.Components != null && r.Components.Count > 0)
                {
                    rows.Add(new AreaRow { Label = "  COMPONENT A", SquareFeet = r.PrimaryAreaSquareFeet ?? r.AreaSquareFeet });
                    foreach (var c in r.Components) rows.Add(new AreaRow { Label = "  COMPONENT " + c.Label, SquareFeet = c.AreaSquareFeet });
                    if (r.ComponentsOverlap)
                    {
                        // Both, named for what they are; the table does not pick one for the legal description.
                        rows.Add(new AreaRow { Label = "  SUM OF COMPONENT AREAS", SquareFeet = r.AreaSquareFeet });
                        if (r.PhysicalAreaSquareFeet.HasValue) rows.Add(new AreaRow { Label = "  TOTAL PHYSICAL AREA", SquareFeet = r.PhysicalAreaSquareFeet.Value });
                    }
                }
            }
            if (settings.AreaTableCombined && records.Count > 1)
                rows.Add(new AreaRow { Label = settings.CombinedAreaLabel, SquareFeet = records.Sum(r => r.DisplayAreaSquareFeet), Combined = true });
            return rows;
        }

        public static bool WantsAreaTable(IList<EasementRecord> records, ExhibitSettings settings)
        {
            if (string.Equals(settings.AreaTable, "Always", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(settings.AreaTable, "Never", StringComparison.OrdinalIgnoreCase)) return false;
            return records.Count > 1 || records.Any(r => r.Components != null && r.Components.Count > 0);
        }

        /// <summary>What an exhibit remembers of an easement; a change means the exhibit may be stale.</summary>
        public static string SourceFingerprint(EasementRecord r)
        {
            var sb = new StringBuilder();
            sb.Append(r.DraftedFingerprint).Append('|')
              .Append(r.AreaSquareFeet.ToString("F3", CultureInfo.InvariantCulture)).Append('|')
              .Append(r.Title).Append('|')
              .Append(r.Holes == null ? 0 : r.Holes.Count).Append('|');
            foreach (var c in r.Components ?? new List<EasementComponent>())
                sb.Append(c.Label).Append(':').Append(c.AreaSquareFeet.ToString("F3", CultureInfo.InvariantCulture)).Append(';');
            var tie = r.TieCourses();
            if (tie.Count > 0) sb.Append(EasementAnnotation.Fingerprint(tie.Select(t => t.Course)));
            foreach (var d in r.RouteCourses ?? new List<CourseData>()) sb.Append(EasementAnnotation.Fingerprint(new[] { d.Course }));
            using (var sha = SHA256.Create())
                return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString())).Take(12).Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
        }

        /// <summary>Easements whose current state differs from what the exhibit was built from.</summary>
        public static List<string> StaleSources(ExhibitRecord exhibit, IList<EasementRecord> records)
        {
            var stale = new List<string>();
            foreach (var s in exhibit.Sources)
            {
                var r = records.FirstOrDefault(x => x.Id == s.EasementId);
                if (r == null) stale.Add(s.Title + " is no longer stored in the drawing.");
                else if (SourceFingerprint(r) != s.Fingerprint) stale.Add(r.Title + " has changed since the exhibit was built.");
            }
            return stale;
        }
    }

    /// <summary>Something placed on the sheet, for the collision review.</summary>
    public sealed class SheetBox
    {
        public string Key { get; set; }
        public string Description { get; set; }
        /// <summary>LABEL, LEADER and DIMENSION belong inside the viewport; the rest are sheet furniture.</summary>
        public string Kind { get; set; }
        public SheetRect Rect { get; set; }
        /// <summary>The true outline of turned text (four corners); null when the rectangle is the outline.</summary>
        public P2[] Corners { get; set; }
        public IList<P2> Outline { get { return Corners ?? ExhibitReview.CornersOf(Rect); } }
        public bool InViewport { get { return Kind == "LABEL" || Kind == "LEADER" || Kind == "DIMENSION" || Kind == "AREALABEL"; } }
    }

    /// <summary>
    /// Looks over a generated exhibit for what a drafter would fix: annotation outside the
    /// viewport or printable area, overlaps, title/area labels sitting on the easement lines,
    /// and labels that could not be placed. It reports; it does not keep shuffling things.
    /// </summary>
    public static class ExhibitReview
    {
        public static List<ExhibitReviewItem> Check(IList<SheetBox> boxes, SheetRect viewport, SheetRect printable,
                                                    IList<Tuple<P2, P2>> easementLines, IEnumerable<string> notPlaced)
        {
            return Check(boxes, viewport, printable, easementLines, notPlaced, true);
        }

        /// <summary>
        /// The review. <paramref name="viewportFramePlots"/> false (the viewport sits on a no-plot layer, as
        /// office exhibits do): sheet furniture crossing the viewport's edge is normal practice, not an issue.
        /// </summary>
        public static List<ExhibitReviewItem> Check(IList<SheetBox> boxes, SheetRect viewport, SheetRect printable,
                                                    IList<Tuple<P2, P2>> easementLines, IEnumerable<string> notPlaced, bool viewportFramePlots)
        {
            var items = new List<ExhibitReviewItem>();
            const double slack = 0.01;

            foreach (var b in boxes)
            {
                var outsideViewport = b.InViewport && !b.Outline.All(p => Inside(viewport, p, slack));
                if (outsideViewport)
                    Add(items, "Warning", b.Key, b.Description + " runs outside the viewport.");
                // Annotation already reported outside the viewport is not reported twice; a title block or border
                // is drawn for the whole sheet by the office and is not held to the printable margin.
                if (!outsideViewport && b.Kind != "TITLEBLOCK" && b.Kind != "BORDER" && !b.Rect.Within(printable, slack))
                    Add(items, b.InViewport ? "Warning" : "Error", b.Key, b.Description + " is outside the printable area of the sheet.");
                if (viewportFramePlots && !b.InViewport && b.Kind != "BORDER" && b.Rect.Overlaps(viewport, slack) && !b.Rect.Within(viewport, slack) && b.Kind != "TITLEBLOCK")
                    Add(items, "Warning", b.Key, b.Description + " overlaps the viewport edge.");
            }

            for (var i = 0; i < boxes.Count; i++)
            for (var j = i + 1; j < boxes.Count; j++)
            {
                var a = boxes[i];
                var b = boxes[j];
                if (a.Kind == "BORDER" || b.Kind == "BORDER" || a.Kind == "TITLEBLOCK" || b.Kind == "TITLEBLOCK") continue;
                if (!a.Rect.Overlaps(b.Rect, slack)) continue;
                if ((a.Corners != null || b.Corners != null) && !QuadsOverlap(a.Outline, b.Outline, slack)) continue;
                var severity = a.Kind == "LEADER" || b.Kind == "LEADER" ? "Warning" : "Warning";
                Add(items, severity, a.Key, a.Description + " overlaps " + b.Description + ".");
            }

            foreach (var b in boxes.Where(x => x.Kind == "AREALABEL"))
                if (easementLines.Any(l => SegmentHitsQuad(l.Item1, l.Item2, b.Outline)))
                    Add(items, "Warning", b.Key, b.Description + " sits on the easement lines.");

            foreach (var n in notPlaced ?? new string[0])
                Add(items, "Warning", null, n);
            return items;
        }

        /// <summary>
        /// A place for a horizontal label of the given size inside an area outline (paper polygons, holes
        /// excluded) that no easement line crosses and no other annotation covers, nearest the preferred
        /// point. Null when there is no such place -- the label then goes at the preferred point and the
        /// review says so; nothing is squeezed or shrunk.
        /// </summary>
        public static P2? ClearSpot(IList<P2> outer, IList<IList<P2>> holes, SheetRect viewport, IList<Tuple<P2, P2>> lines,
                                    IList<SheetRect> taken, double width, double height, P2 preferred, int steps = 40, double besideWithin = 0)
        {
            var inside = ClearSpotInside(outer, holes, viewport, lines, taken, width, height, preferred, steps);
            if (inside.HasValue || besideWithin <= 0) return inside;

            // Too small on paper for its label: beside it instead, the nearest clear place within reach.
            P2? best = null;
            var bestDistance = double.MaxValue;
            for (var i = 0; i <= steps; i++)
            for (var j = 0; j <= steps; j++)
            {
                var c = new P2(preferred.X - besideWithin + 2 * besideWithin * i / steps, preferred.Y - besideWithin + 2 * besideWithin * j / steps);
                var d = c.DistanceTo(preferred);
                if (d >= bestDistance || d > besideWithin) continue;
                var box = new SheetRect(c.X - width / 2, c.Y - height / 2, c.X + width / 2, c.Y + height / 2);
                if (!box.Within(viewport, 0)) continue;
                if (lines != null && lines.Any(l => SegmentHitsRect(l.Item1, l.Item2, box))) continue;
                if (taken != null && taken.Any(t => t.Overlaps(box, 0))) continue;
                if (outer.Any(box.Contains)) continue;
                best = c;
                bestDistance = d;
            }
            return best;
        }

        private static P2? ClearSpotInside(IList<P2> outer, IList<IList<P2>> holes, SheetRect viewport, IList<Tuple<P2, P2>> lines,
                                           IList<SheetRect> taken, double width, double height, P2 preferred, int steps)
        {
            if (outer == null || outer.Count < 3 || width <= 0 || height <= 0) return null;
            var bounds = new SheetRect(outer.Min(p => p.X), outer.Min(p => p.Y), outer.Max(p => p.X), outer.Max(p => p.Y));
            P2? best = null;
            var bestDistance = double.MaxValue;
            for (var i = 0; i <= steps; i++)
            for (var j = 0; j <= steps; j++)
            {
                var c = new P2(bounds.MinX + bounds.Width * i / steps, bounds.MinY + bounds.Height * j / steps);
                var d = c.DistanceTo(preferred);
                if (d >= bestDistance) continue;
                var box = new SheetRect(c.X - width / 2, c.Y - height / 2, c.X + width / 2, c.Y + height / 2);
                if (!box.Within(viewport, 0)) continue;
                var corners = new[] { new P2(box.MinX, box.MinY), new P2(box.MaxX, box.MinY), new P2(box.MaxX, box.MaxY), new P2(box.MinX, box.MaxY), c };
                if (!corners.All(p => InPolygon(outer, p) && (holes == null || !holes.Any(h => InPolygon(h, p))))) continue;
                if (lines != null && lines.Any(l => SegmentHitsRect(l.Item1, l.Item2, box))) continue;
                if (taken != null && taken.Any(t => t.Overlaps(box, 0))) continue;
                best = c;
                bestDistance = d;
            }
            return best;
        }

        /// <summary>
        /// The first of the candidate places (centre and angle, in order of preference) where a turned
        /// label of the given size stays in the viewport, off the easement lines and clear of the other
        /// annotation; null when none is.
        /// </summary>
        public static int? FirstClear(IList<Tuple<P2, double>> candidates, double width, double height, SheetRect viewport,
                                      IList<Tuple<P2, P2>> lines, IList<SheetRect> taken)
        {
            int conflicts;
            var best = FewestConflicts(candidates, width, height, viewport, lines, taken, out conflicts);
            return conflicts == 0 ? best : (int?)null;
        }

        /// <summary>The candidate with the fewest conflicts (earliest on a tie): leaving the viewport counts
        /// most, then covering other annotation, then crossing lines. The review still lists what remains.</summary>
        public static int FewestConflicts(IList<Tuple<P2, double>> candidates, double width, double height, SheetRect viewport,
                                          IList<Tuple<P2, P2>> lines, IList<SheetRect> taken, out int conflicts)
        {
            var best = 0;
            conflicts = int.MaxValue;
            for (var i = 0; i < candidates.Count; i++)
            {
                var quad = TurnedBox(candidates[i].Item1, candidates[i].Item2, width, height);
                var score = 0;
                if (!quad.All(p => Inside(viewport, p, 0))) score += 100;
                if (taken != null) score += 10 * taken.Count(r => QuadsOverlap(CornersOf(r), quad, 0));
                if (lines != null) score += lines.Count(l => SegmentHitsQuad(l.Item1, l.Item2, quad));
                if (score < conflicts) { conflicts = score; best = i; }
                if (score == 0) break;
            }
            return best;
        }

        /// <summary>Corners of a width x height box centred on a point and turned by an angle (radians).</summary>
        public static P2[] TurnedBox(P2 center, double angle, double width, double height)
        {
            var u = new P2(Math.Cos(angle), Math.Sin(angle));
            var v = new P2(-u.Y, u.X);
            return new[]
            {
                center - u * (width / 2) - v * (height / 2), center + u * (width / 2) - v * (height / 2),
                center + u * (width / 2) + v * (height / 2), center - u * (width / 2) + v * (height / 2)
            };
        }

        public static P2[] CornersOf(SheetRect r)
        {
            return new[] { new P2(r.MinX, r.MinY), new P2(r.MaxX, r.MinY), new P2(r.MaxX, r.MaxY), new P2(r.MinX, r.MaxY) };
        }

        private static bool Inside(SheetRect r, P2 p, double slack)
        {
            return p.X >= r.MinX - slack && p.X <= r.MaxX + slack && p.Y >= r.MinY - slack && p.Y <= r.MaxY + slack;
        }

        /// <summary>A segment touches a convex outline: an end inside it, or crossing an edge.</summary>
        public static bool SegmentHitsQuad(P2 a, P2 b, IList<P2> quad)
        {
            if (InPolygon(quad, a) || InPolygon(quad, b)) return true;
            for (var i = 0; i < quad.Count; i++)
                if (SegmentsCross(a, b, quad[i], quad[(i + 1) % quad.Count])) return true;
            return false;
        }

        /// <summary>Two convex outlines overlap by more than the slack (separating axis test).</summary>
        public static bool QuadsOverlap(IList<P2> a, IList<P2> b, double slack)
        {
            foreach (var shape in new[] { a, b })
                for (var i = 0; i < shape.Count; i++)
                {
                    var edge = shape[(i + 1) % shape.Count] - shape[i];
                    var axis = new P2(-edge.Y, edge.X).Normalized();
                    if (axis.Length < 1e-12) continue;
                    double minA = a.Min(p => P2.Dot(p, axis)), maxA = a.Max(p => P2.Dot(p, axis));
                    double minB = b.Min(p => P2.Dot(p, axis)), maxB = b.Max(p => P2.Dot(p, axis));
                    if (maxA <= minB + slack || maxB <= minA + slack) return false;
                }
            return true;
        }

        public static bool InPolygon(IList<P2> polygon, P2 p)
        {
            var inside = false;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                var a = polygon[i];
                var b = polygon[j];
                if ((a.Y > p.Y) != (b.Y > p.Y) && p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
            }
            return inside;
        }

        private static void Add(List<ExhibitReviewItem> items, string severity, string key, string message)
        {
            if (items.Any(x => x.Message == message)) return;
            items.Add(new ExhibitReviewItem { Severity = severity, ItemKey = key, Message = message });
        }

        public static bool SegmentHitsRect(P2 a, P2 b, SheetRect r)
        {
            if (r.Contains(a) || r.Contains(b)) return true;
            var corners = new[] { new P2(r.MinX, r.MinY), new P2(r.MaxX, r.MinY), new P2(r.MaxX, r.MaxY), new P2(r.MinX, r.MaxY) };
            for (var i = 0; i < 4; i++)
                if (SegmentsCross(a, b, corners[i], corners[(i + 1) % 4])) return true;
            return false;
        }

        private static bool SegmentsCross(P2 p1, P2 p2, P2 q1, P2 q2)
        {
            double d1 = P2.Cross(p2 - p1, q1 - p1), d2 = P2.Cross(p2 - p1, q2 - p1);
            double d3 = P2.Cross(q2 - q1, p1 - q1), d4 = P2.Cross(q2 - q1, p2 - q1);
            return ((d1 > 0) != (d2 > 0)) && ((d3 > 0) != (d4 > 0));
        }
    }
}
