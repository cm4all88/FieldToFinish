using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FieldCodes.Easements;
using FieldCodes.Row;

namespace FieldCodes.Lots
{
    /// <summary>
    /// Divides a platted block into lots: the lot lines square to the front, an alley at the rear
    /// or between two rows, rounded corners, building setbacks and utility easements, surface
    /// drainage arrows and the checks a plat reviewer makes (frontages add up to the side, areas
    /// add up to the block, minimum area and width).
    ///
    /// Each lot is the block cut by half-planes, which keeps every lot's corners exact and lets
    /// the cutting line that made each edge say what the edge is. Pure and unit tested; the CAD
    /// layer only draws the plan. Anything that cannot be done is an error and nothing is built.
    /// </summary>
    public static class LotBuilder
    {
        private struct Frame
        {
            public P2 Origin;
            public P2 U;
            public P2 N;
            public double Length;
            public int Side;
        }

        private struct Plane
        {
            public P2 Origin;
            public P2 Normal;
            public double Offset;
            public int Tag;
        }

        private const int RearTag = 2000;

        public static LotPlan Build(LotJob job)
        {
            var plan = new LotPlan();
            if (job == null) { plan.Errors.Add("No lot job."); return plan; }
            plan.Title = job.BlockName ?? string.Empty;

            Validate(job, plan);
            if (!plan.Ok) return plan;

            var upf = job.UnitsPerFoot;
            var ring = job.Corners;
            var n = ring.Count;
            var signed = RowGeometry.SignedArea(ring);
            var ccw = signed > 0;
            plan.BlockAreaSqFt = Math.Abs(signed) / (upf * upf);
            plan.Minimum = new P2(ring.Min(p => p.X), ring.Min(p => p.Y));
            plan.Maximum = new P2(ring.Max(p => p.X), ring.Max(p => p.Y));
            if (!LotGeometry.IsConvex(ring))
                plan.Warnings.Add("The block is not convex; check every lot before relying on it.");

            var tolerance = job.ToleranceFeet * upf;
            var mode = job.Alley;
            var alleyWidth = job.AlleyWidthFeet * upf;
            if (job.RowB != null && mode == LotAlleyMode.None)
            {
                mode = LotAlleyMode.MidBlock;
                alleyWidth = 0;
            }

            var frameA = MakeFrame(ring, job.RowA.FrontSide, ccw);
            var frameB = job.RowB != null ? MakeFrame(ring, job.RowB.FrontSide, ccw) : default(Frame);
            var clipsA = new List<Plane>();
            var clipsB = new List<Plane>();
            List<P2> alleyPolygon = null;
            List<P2> remainderPolygon = null;

            var depthA = job.RowA.DepthFeet * upf;
            var depthB = job.RowB != null ? job.RowB.DepthFeet * upf : 0.0;
            var middle = frameA.Origin + frameA.U * (frameA.Length / 2.0);
            var blockDepth = LotGeometry.RayExit(middle, frameA.N, ring);

            if (mode == LotAlleyMode.MidBlock)
            {
                if (!(blockDepth > 0)) { plan.Errors.Add("The block's depth cannot be measured from the first row's side."); return plan; }

                double dA;
                if (depthA > 0 && depthB > 0)
                {
                    dA = depthA;
                    var off = blockDepth - depthA - depthB - alleyWidth;
                    if (Math.Abs(off) > Math.Max(tolerance, 0.01 * upf))
                    {
                        plan.Errors.Add(string.Format(CultureInfo.InvariantCulture,
                            "The two lot depths and the alley do not add up to the block: {0:0.00}' + {1:0.00}' + {2:0.00}' is {3:0.00}' against a block {4:0.00}' deep.",
                            depthA / upf, depthB / upf, alleyWidth / upf, (depthA + depthB + alleyWidth) / upf, blockDepth / upf));
                        return plan;
                    }
                }
                else if (depthA > 0) dA = depthA;
                else if (depthB > 0) dA = blockDepth - depthB - alleyWidth;
                else dA = (blockDepth - alleyWidth) / 2.0;

                if (dA <= tolerance || dA + alleyWidth >= blockDepth - tolerance)
                {
                    plan.Errors.Add("The alley and the lot depths do not fit inside the block.");
                    return plan;
                }

                clipsA.Add(new Plane { Origin = frameA.Origin, Normal = frameA.N * -1.0, Offset = -dA, Tag = RearTag });
                clipsB.Add(new Plane { Origin = frameA.Origin, Normal = frameA.N, Offset = dA + alleyWidth, Tag = RearTag + 1 });

                if (alleyWidth > tolerance)
                {
                    var band = LotGeometry.FromRing(ring);
                    band = LotGeometry.Clip(band, frameA.Origin, frameA.N, dA, RearTag + 2);
                    band = LotGeometry.Clip(band, frameA.Origin, frameA.N * -1.0, -(dA + alleyWidth), RearTag + 3);
                    alleyPolygon = LotGeometry.Points(band);
                }
            }
            else if (mode == LotAlleyMode.Rear)
            {
                var back = job.BackSide >= 0 ? job.BackSide : FarthestSide(ring, frameA);
                var nb = InwardNormal(ring, back, ccw);
                var pb = ring[back];
                if (!(alleyWidth > 0)) { plan.Errors.Add("Give the alley's width."); return plan; }

                clipsA.Add(new Plane { Origin = pb, Normal = nb, Offset = alleyWidth, Tag = RearTag });
                if (depthA > 0) clipsA.Add(new Plane { Origin = frameA.Origin, Normal = frameA.N * -1.0, Offset = -depthA, Tag = RearTag + 1 });

                var band = LotGeometry.FromRing(ring);
                band = LotGeometry.Clip(band, pb, nb * -1.0, -alleyWidth, RearTag + 2);
                alleyPolygon = LotGeometry.Points(band);
            }
            else if (depthA > 0)
            {
                if (blockDepth > 0 && depthA >= blockDepth - tolerance)
                {
                    plan.Errors.Add(string.Format(CultureInfo.InvariantCulture,
                        "A lot depth of {0:0.00}' reaches the back of a block that is {1:0.00}' deep; leave the depth blank to run the lots to the back.",
                        depthA / upf, blockDepth / upf));
                    return plan;
                }
                clipsA.Add(new Plane { Origin = frameA.Origin, Normal = frameA.N * -1.0, Offset = -depthA, Tag = RearTag });
                var rest = LotGeometry.FromRing(ring);
                rest = LotGeometry.Clip(rest, frameA.Origin, frameA.N, depthA, RearTag + 1);
                remainderPolygon = LotGeometry.Points(rest);
            }

            var number = job.FirstLotNumber;
            var lots = new List<Lot>();
            var envelopesAdded = new List<LotEnvelope>();
            BuildRow(job, plan, job.RowA, 0, frameA, clipsA, ccw, ref number, lots, envelopesAdded);
            if (job.RowB != null) BuildRow(job, plan, job.RowB, 1, frameB, clipsB, ccw, ref number, lots, envelopesAdded);
            if (!plan.Ok) return plan;

            plan.Lots.AddRange(lots);
            plan.Envelopes.AddRange(envelopesAdded);

            if (alleyPolygon != null && alleyPolygon.Count >= 3)
            {
                var areaUnits = LotGeometry.Area(alleyPolygon);
                plan.Alley = new LotAlley
                {
                    Polygon = alleyPolygon,
                    AreaSqFt = areaUnits / (upf * upf),
                    WidthFeet = job.AlleyWidthFeet,
                    LabelPoint = LotGeometry.Centroid(alleyPolygon)
                };
            }
            if (remainderPolygon != null && remainderPolygon.Count >= 3)
            {
                plan.Remainder = remainderPolygon;
                plan.RemainderSqFt = LotGeometry.Area(remainderPolygon) / (upf * upf);
            }

            EmitLines(job, plan, ring);
            CheckClosure(job, plan);
            return plan;
        }

        // ---------------------------------------------------------------- validation

        private static void Validate(LotJob job, LotPlan plan)
        {
            if (!(job.UnitsPerFoot > 0) || double.IsInfinity(job.UnitsPerFoot))
                plan.Errors.Add("Units per foot must be greater than zero.");

            var n = job.Corners.Count;
            if (n < 3) { plan.Errors.Add("A block needs at least 3 corners."); return; }
            for (var i = 0; i < n; i++)
                if (job.Corners[i].DistanceTo(job.Corners[(i + 1) % n]) < 1e-6)
                    plan.Errors.Add("Corners " + (i + 1) + " and " + (((i + 1) % n) + 1) + " are the same point.");
            if (!plan.Ok) return;
            if (Math.Abs(RowGeometry.SignedArea(job.Corners)) < 1e-6) { plan.Errors.Add("The corners enclose no area."); return; }
            if (job.CornerRadiiFeet.Any(r => r < 0)) plan.Errors.Add("A corner radius cannot be negative.");

            ValidateRow(job, plan, job.RowA, "The first row", n);
            if (job.RowB != null)
            {
                ValidateRow(job, plan, job.RowB, "The second row", n);
                if (job.RowB.FrontSide == job.RowA.FrontSide)
                    plan.Errors.Add("The two rows cannot front the same side of the block.");
                if (job.Alley == LotAlleyMode.Rear)
                    plan.Errors.Add("A rear alley serves one row of lots; with two rows use an alley between them.");
            }
            else if (job.Alley == LotAlleyMode.MidBlock)
                plan.Errors.Add("An alley between two rows needs a second row of lots.");

            if (job.AlleyWidthFeet < 0) plan.Errors.Add("The alley width cannot be negative.");
            if (job.Alley == LotAlleyMode.Rear && job.BackSide >= n) plan.Errors.Add("The alley side is not a side of this block.");
            if (job.Alley == LotAlleyMode.Rear && job.BackSide == job.RowA.FrontSide) plan.Errors.Add("The alley cannot run along the side the lots front.");

            foreach (var value in new[]
            {
                job.FrontSetbackFeet, job.SideSetbackFeet, job.CornerSideSetbackFeet, job.RearSetbackFeet,
                job.FrontEasementFeet, job.SideEasementFeet, job.RearEasementFeet, job.MinLotAreaSqFt, job.MinLotWidthFeet
            })
                if (value < 0) { plan.Errors.Add("Setbacks, easements and minimums cannot be negative."); break; }
        }

        private static void ValidateRow(LotJob job, LotPlan plan, LotRow row, string name, int n)
        {
            if (row.FrontSide < 0 || row.FrontSide >= n) { plan.Errors.Add(name + " does not front a side of this block."); return; }
            if (row.WidthsFeet.Count < 1) plan.Errors.Add(name + " has no lots.");
            if (row.WidthsFeet.Any(w => !(w > 0))) plan.Errors.Add(name + ": every lot needs a frontage greater than zero.");
            if (row.DepthFeet < 0) plan.Errors.Add(name + ": the depth cannot be negative.");
        }

        // ------------------------------------------------------------------- frames

        private static P2 InwardNormal(IList<P2> ring, int side, bool ccw)
        {
            var direction = RowGeometry.Dir(ring[side], ring[(side + 1) % ring.Count]);
            var left = direction.LeftNormal();
            return ccw ? left : left * -1.0;
        }

        private static Frame MakeFrame(IList<P2> ring, int side, bool ccw)
        {
            var a = ring[side];
            var b = ring[(side + 1) % ring.Count];
            return new Frame
            {
                Origin = a,
                U = RowGeometry.Dir(a, b),
                N = InwardNormal(ring, side, ccw),
                Length = a.DistanceTo(b),
                Side = side
            };
        }

        /// <summary>The side whose middle is furthest from the front line: the back of the block.</summary>
        private static int FarthestSide(IList<P2> ring, Frame front)
        {
            var best = -1;
            var distance = double.MinValue;
            for (var j = 0; j < ring.Count; j++)
            {
                if (j == front.Side) continue;
                var mid = (ring[j] + ring[(j + 1) % ring.Count]) * 0.5;
                var d = P2.Dot(mid - front.Origin, front.N);
                if (d > distance) { distance = d; best = j; }
            }
            return best;
        }

        // ---------------------------------------------------------------------- rows

        private static void BuildRow(LotJob job, LotPlan plan, LotRow row, int rowIndex, Frame f, IList<Plane> clips,
                                     bool ccw, ref int number, IList<Lot> lots, IList<LotEnvelope> envelopes)
        {
            var upf = job.UnitsPerFoot;
            var ring = job.Corners;
            var n = ring.Count;
            var name = rowIndex == 0 ? "The first row" : "The second row";

            var widths = ResolveWidths(job, plan, row, f.Length / upf, name);
            if (widths == null) return;

            var station = 0.0;
            for (var i = 0; i < widths.Count; i++)
            {
                var from = station;
                var to = station + widths[i] * upf;
                station = to;

                var poly = LotGeometry.FromRing(ring);
                if (i > 0) poly = LotGeometry.Clip(poly, f.Origin + f.U * from, f.U, 0, 1000 + rowIndex * 100 + i);
                if (i < widths.Count - 1) poly = LotGeometry.Clip(poly, f.Origin + f.U * to, f.U * -1.0, 0, 1000 + rowIndex * 100 + i + 1);
                foreach (var clip in clips) poly = LotGeometry.Clip(poly, clip.Origin, clip.Normal, clip.Offset, clip.Tag);

                if (poly.Count < 3)
                {
                    plan.Errors.Add(name + ": lot " + (i + 1) + " has no ground inside the block.");
                    return;
                }

                var points = LotGeometry.Points(poly);
                var lot = new Lot
                {
                    Number = number.ToString(CultureInfo.InvariantCulture),
                    Index = lots.Count,
                    Row = rowIndex,
                    Polygon = points,
                    FrontageFeet = widths[i],
                    Inward = f.N,
                    Along = f.U,
                    LabelPoint = LotGeometry.Centroid(points)
                };
                number++;

                var areaUnits = LotGeometry.Area(points);
                RoundLot(job, plan, lot, ring, ref areaUnits);
                lot.AreaSqFt = areaUnits / (upf * upf);
                lot.MeanDepthFeet = widths[i] > 0 ? lot.AreaSqFt / widths[i] : 0.0;

                var roles = poly.Select(v => RoleOf(v.Tag, f.Side, n)).ToList();
                lot.IsCorner = roles.Contains(LotEdgeRole.StreetSide);

                Drain(job, lot);
                AddEnvelope(job, poly, roles, LotOffsetKind.Setback, lot.Index, envelopes);
                AddEnvelope(job, poly, roles, LotOffsetKind.Easement, lot.Index, envelopes);
                CheckLot(job, plan, lot, envelopes);
                lots.Add(lot);
            }
        }

        /// <summary>The frontages in feet, adjusted so they add up to the side. Null when they cannot be.</summary>
        private static List<double> ResolveWidths(LotJob job, LotPlan plan, LotRow row, double sideFeet, string name)
        {
            var widths = row.WidthsFeet.ToList();
            var difference = sideFeet - widths.Sum();
            if (Math.Abs(difference) <= job.ToleranceFeet) return widths;

            if (row.Close == LotWidthClose.Refuse)
            {
                plan.Errors.Add(string.Format(CultureInfo.InvariantCulture,
                    "{0}: the lot frontages add up to {1:0.00}' but the side is {2:0.00}' ({3:+0.00;-0.00}'). Fix a width, or let the first or last lot take the difference.",
                    name, widths.Sum(), sideFeet, difference));
                return null;
            }

            var index = row.Close == LotWidthClose.FirstLot ? 0 : widths.Count - 1;
            if (widths[index] + difference <= 0)
            {
                plan.Errors.Add(name + ": the " + (index == 0 ? "first" : "last") + " lot cannot take a difference of " +
                                difference.ToString("+0.00;-0.00", CultureInfo.InvariantCulture) + "'.");
                return null;
            }
            widths[index] += difference;
            plan.Notes.Add(string.Format(CultureInfo.InvariantCulture, "{0}: the {1} lot's frontage is {2:0.00}' ({3:+0.00;-0.00}' to close the side).",
                name, index == 0 ? "first" : "last", widths[index], difference));
            return widths;
        }

        private static LotEdgeRole RoleOf(int tag, int frontSide, int n)
        {
            if (tag >= RearTag) return LotEdgeRole.Rear;
            if (tag >= 1000) return LotEdgeRole.Side;
            if (tag == frontSide) return LotEdgeRole.Front;
            if (tag == (frontSide + 1) % n || tag == (frontSide - 1 + n) % n) return LotEdgeRole.StreetSide;
            return LotEdgeRole.Rear;
        }

        // ------------------------------------------------------------------ corners

        /// <summary>The lot's boundary, with an arc wherever one of its corners is a rounded block
        /// corner. The area loses what the arc cuts off.</summary>
        private static void RoundLot(LotJob job, LotPlan plan, Lot lot, IList<P2> ring, ref double areaUnits)
        {
            var upf = job.UnitsPerFoot;
            var radii = new List<double>();
            var any = false;
            foreach (var p in lot.Polygon)
            {
                var r = 0.0;
                for (var k = 0; k < ring.Count && k < job.CornerRadiiFeet.Count; k++)
                    if (job.CornerRadiiFeet[k] > 0 && p.DistanceTo(ring[k]) < 1e-6) r = job.CornerRadiiFeet[k] * upf;
                radii.Add(r);
                if (r > 0) any = true;
            }

            if (!any)
            {
                for (var i = 0; i < lot.Polygon.Count; i++)
                    lot.Boundary.Add(Course.Line(lot.Polygon[i], lot.Polygon[(i + 1) % lot.Polygon.Count]));
                return;
            }

            var warnings = new List<string>();
            var pieces = RowGeometry.RoundRing(lot.Polygon, radii, warnings, "lot corners");
            foreach (var w in warnings) plan.Warnings.Add("Lot " + lot.Number + ": " + w);
            foreach (var piece in pieces)
            {
                lot.Boundary.Add(piece.Course);
                if (!piece.IsArc) continue;
                var sweep = piece.Course.Sweep;
                areaUnits -= piece.Course.Radius * piece.Course.Radius * (Math.Tan(sweep / 2.0) - sweep / 2.0);
            }
        }

        // -------------------------------------------------------------------- drain

        private static void Drain(LotJob job, Lot lot)
        {
            var drain = job.DefaultDrain;
            int key;
            if (int.TryParse(lot.Number, NumberStyles.Integer, CultureInfo.InvariantCulture, out key) && job.DrainOverrides.ContainsKey(key))
                drain = job.DrainOverrides[key];
            if (drain == LotDrain.None) return;

            var upf = job.UnitsPerFoot;
            var n = lot.Inward;
            var left = n.LeftNormal();
            var length = Math.Min(Math.Min(0.35 * lot.MeanDepthFeet, 0.6 * lot.FrontageFeet), 20.0) * upf;
            if (!(length > 0)) return;

            var centre = lot.LabelPoint + n * (0.28 * lot.MeanDepthFeet * upf);
            var directions = new List<P2>();
            switch (drain)
            {
                case LotDrain.ToFront: directions.Add(n * -1.0); break;
                case LotDrain.ToRear: directions.Add(n); break;
                case LotDrain.ToLeft: directions.Add(left); break;
                case LotDrain.ToRight: directions.Add(left * -1.0); break;
                case LotDrain.FrontAndRear:
                    directions.Add(n * -1.0);
                    directions.Add(n);
                    break;
            }

            foreach (var direction in directions)
            {
                var both = directions.Count > 1;
                lot.Drains.Add(new LotDrainArrow
                {
                    From = both ? centre : centre - direction * (length / 2.0),
                    To = both ? centre + direction * (length / 2.0) : centre + direction * (length / 2.0),
                    Percent = job.SlopePercent
                });
            }
        }

        // ----------------------------------------------------------------- envelopes

        private static void AddEnvelope(LotJob job, IList<TagVertex> poly, IList<LotEdgeRole> roles, LotOffsetKind kind,
                                        int lotIndex, IList<LotEnvelope> envelopes)
        {
            Func<LotEdgeRole, double> distance;
            if (kind == LotOffsetKind.Setback)
            {
                distance = role =>
                {
                    switch (role)
                    {
                        case LotEdgeRole.Front: return job.FrontSetbackFeet;
                        case LotEdgeRole.Rear: return job.RearSetbackFeet;
                        case LotEdgeRole.StreetSide: return job.CornerSideSetbackFeet > 0 ? job.CornerSideSetbackFeet : job.SideSetbackFeet;
                        default: return job.SideSetbackFeet;
                    }
                };
            }
            else
            {
                distance = role =>
                {
                    switch (role)
                    {
                        case LotEdgeRole.Front: return job.FrontEasementFeet;
                        case LotEdgeRole.Rear: return job.RearEasementFeet;
                        default: return job.SideEasementFeet;
                    }
                };
            }

            if (Enumerable.Range(0, roles.Count).All(i => !(distance(roles[i]) > 0))) return;

            var upf = job.UnitsPerFoot;
            var points = LotGeometry.Points(poly);
            var ccw = RowGeometry.SignedArea(points) > 0;
            var current = points.Select(p => new TagVertex(p, 0)).ToList();
            for (var i = 0; i < points.Count; i++)
            {
                var d = distance(roles[i]) * upf;
                if (!(d > 0)) continue;
                var a = points[i];
                var b = points[(i + 1) % points.Count];
                var left = RowGeometry.Dir(a, b).LeftNormal();
                current = LotGeometry.Clip(current, a, ccw ? left : left * -1.0, d, 0);
                if (current.Count < 3) break;
            }

            envelopes.Add(new LotEnvelope { Kind = kind, LotIndex = lotIndex, Polygon = current.Count < 3 ? new List<P2>() : LotGeometry.Points(current) });
        }

        private static void CheckLot(LotJob job, LotPlan plan, Lot lot, IList<LotEnvelope> envelopes)
        {
            if (job.MinLotAreaSqFt > 0 && lot.AreaSqFt < job.MinLotAreaSqFt)
                plan.Warnings.Add(string.Format(CultureInfo.InvariantCulture, "Lot {0} is {1:#,##0} sq ft, under the {2:#,##0} sq ft minimum.",
                    lot.Number, lot.AreaSqFt, job.MinLotAreaSqFt));
            if (job.MinLotWidthFeet > 0 && lot.FrontageFeet < job.MinLotWidthFeet)
                plan.Warnings.Add(string.Format(CultureInfo.InvariantCulture, "Lot {0} is {1:0.00}' wide, under the {2:0.##}' minimum.",
                    lot.Number, lot.FrontageFeet, job.MinLotWidthFeet));
            foreach (var envelope in envelopes)
                if (envelope.LotIndex == lot.Index && envelope.Kind == LotOffsetKind.Setback && envelope.Polygon.Count < 3)
                    plan.Warnings.Add("Lot " + lot.Number + ": the setbacks leave no buildable area.");
        }

        // -------------------------------------------------------------------- lines

        private static void EmitLines(LotJob job, LotPlan plan, IList<P2> ring)
        {
            var upf = job.UnitsPerFoot;
            var tolerance = 1e-6 * Math.Max(1.0, upf);
            var keys = new HashSet<string>();
            var segments = new List<KeyValuePair<P2, P2>>();

            Func<P2, bool> onRing = p =>
            {
                for (var i = 0; i < ring.Count; i++)
                    if (LotGeometry.DistanceToSegment(p, ring[i], ring[(i + 1) % ring.Count]) <= tolerance) return true;
                return false;
            };

            Action<Course, LotLineKind, bool> add = (course, kind, perimeter) =>
            {
                var key = Key(course);
                if (!keys.Add(key)) return;
                plan.Lines.Add(new LotLine { Kind = kind, Course = course, Data = EasementAnnotation.Describe(course), DistanceOnly = perimeter });
                if (course.Kind == CourseKind.Line) segments.Add(new KeyValuePair<P2, P2>(course.Start, course.End));
            };

            foreach (var lot in plan.Lots)
            {
                foreach (var course in lot.Boundary)
                {
                    var perimeter = course.Kind == CourseKind.Arc ||
                                    (onRing(course.Start) && onRing(course.End) && onRing((course.Start + course.End) * 0.5));
                    add(course, perimeter ? LotLineKind.Perimeter : LotLineKind.Lot, perimeter);
                }
            }

            Action<IList<P2>, LotLineKind> addRegion = (polygon, kind) =>
            {
                for (var i = 0; i < polygon.Count; i++)
                {
                    var a = polygon[i];
                    var b = polygon[(i + 1) % polygon.Count];
                    var covered = true;
                    foreach (var t in new[] { 0.1, 0.3, 0.5, 0.7, 0.9 })
                    {
                        var p = a + (b - a) * t;
                        if (!segments.Any(s => LotGeometry.DistanceToSegment(p, s.Key, s.Value) <= tolerance)) { covered = false; break; }
                    }
                    if (covered) continue;
                    var perimeter = onRing(a) && onRing(b) && onRing((a + b) * 0.5);
                    add(Course.Line(a, b), kind, perimeter);
                }
            };

            if (plan.Alley != null) addRegion(plan.Alley.Polygon, LotLineKind.Alley);
            if (plan.Remainder != null) addRegion(plan.Remainder, LotLineKind.Lot);
        }

        private static string Key(Course course)
        {
            Func<P2, string> p = q => Math.Round(q.X, 4).ToString("0.####", CultureInfo.InvariantCulture) + "," +
                                      Math.Round(q.Y, 4).ToString("0.####", CultureInfo.InvariantCulture);
            if (course.Kind == CourseKind.Arc)
                return "A:" + p(course.Start) + ">" + p(course.End) + "@" + p(course.Center);
            var a = p(course.Start);
            var b = p(course.End);
            return string.CompareOrdinal(a, b) <= 0 ? "L:" + a + "|" + b : "L:" + b + "|" + a;
        }

        // ------------------------------------------------------------------ closure

        private static void CheckClosure(LotJob job, LotPlan plan)
        {
            var lots = plan.Lots.Sum(l => l.AreaSqFt);
            var alley = plan.Alley != null ? plan.Alley.AreaSqFt : 0.0;
            var left = plan.BlockAreaSqFt - lots - alley;

            if (plan.Remainder != null)
            {
                left -= plan.RemainderSqFt;
                plan.Notes.Add(string.Format(CultureInfo.InvariantCulture,
                    "{0:#,##0} sq ft ({1:0.000} ac) at the back of the block is behind the lots and is not divided.",
                    plan.RemainderSqFt, plan.RemainderSqFt / 43560.0));
            }

            if (Math.Abs(left) > Math.Max(0.5, plan.BlockAreaSqFt * 1e-5))
            {
                if (job.Alley == LotAlleyMode.Rear && job.RowA.DepthFeet > 0)
                    plan.Notes.Add(string.Format(CultureInfo.InvariantCulture,
                        "{0:#,##0} sq ft lies between the lots and the alley and belongs to neither.", left));
                else
                    plan.Warnings.Add(string.Format(CultureInfo.InvariantCulture,
                        "The lot and alley areas are {0:#,##0.00} sq ft short of the block's {1:#,##0.00}; check the lots.",
                        lots + alley, plan.BlockAreaSqFt));
            }
        }
    }
}
