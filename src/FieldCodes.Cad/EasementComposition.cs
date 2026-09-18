using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using FieldCodes.Easements;
using FieldCodes.Settings;

using AcEntity = Autodesk.AutoCAD.DatabaseServices.Entity;

namespace FieldCodes.Cad
{
    /// <summary>
    /// Exclusions and further components of an easement, resolved from their source objects in
    /// the current drawing. The strip, portion and area builders make component A exactly as
    /// before; this takes that boundary, removes exclusions, builds components B, C..., and
    /// draws the regions -- holes included -- with the hatch respecting them.
    /// </summary>
    internal static class EasementComposition
    {
        internal sealed class Composed
        {
            public RegionShape Primary;
            public readonly List<KeyValuePair<EasementComponent, RegionShape>> Components = new List<KeyValuePair<EasementComponent, RegionShape>>();
            public readonly List<string> Errors = new List<string>();
            public readonly List<string> Warnings = new List<string>();
            public readonly List<GeometrySource> Sources = new List<GeometrySource>();
            public bool Ok { get { return Errors.Count == 0; } }
            public bool Overlaps;
        }

        internal const string RolePrefixExclusion = "EXCLUSION ";
        internal const string RolePrefixComponent = "COMPONENT ";

        /// <summary>Works out the final regions. Errors mean nothing should be drawn.</summary>
        internal static Composed Apply(Database db, Transaction tr, EasementRecord record, IList<Course> primaryOuter, FtfSettings settings)
        {
            var es = settings.Easements;
            var tolerance = es.ToleranceFt * settings.General.UnitsPerFoot;
            var upf2 = settings.General.UnitsPerFoot * settings.General.UnitsPerFoot;
            var composed = new Composed { Primary = new RegionShape(primaryOuter) };

            composed.Primary = Exclude(db, tr, composed, composed.Primary, record.Exclusions, tolerance, upf2, "EXCLUSION ", string.Empty, settings);
            if (!composed.Ok) return composed;

            foreach (var c in record.Components ?? new List<EasementComponent>())
            {
                var outer = ComponentOuter(db, tr, record, c, settings, composed);
                if (outer == null) return composed;
                var region = Exclude(db, tr, composed, new RegionShape(outer), c.Exclusions,
                                     tolerance, upf2, RolePrefixComponent + c.Label + " EXCLUSION ", " of component " + c.Label, settings);
                if (!composed.Ok) return composed;
                composed.Components.Add(new KeyValuePair<EasementComponent, RegionShape>(c, region));
            }

            // Components that share area make any total double-count: say so, do not fix it.
            var all = new List<KeyValuePair<string, RegionShape>> { new KeyValuePair<string, RegionShape>("A", composed.Primary) };
            all.AddRange(composed.Components.Select(kv => new KeyValuePair<string, RegionShape>(kv.Key.Label, kv.Value)));
            for (var i = 0; i < all.Count; i++)
            for (var j = i + 1; j < all.Count; j++)
            {
                var shared = RegionBuilder.Overlap(all[i].Value, all[j].Value, tolerance);
                if (shared > tolerance) composed.Overlaps = true;
                if (shared > tolerance)
                    composed.Warnings.Add("Components " + all[i].Key + " and " + all[j].Key + " overlap by " +
                                          (shared / upf2).ToString("N0", CultureInfo.InvariantCulture) + " sq ft; the total counts that area twice.");
            }
            return composed;
        }

        private static RegionShape Exclude(Database db, Transaction tr, Composed composed, RegionShape region, IList<Exclusion> exclusions,
                                           double tolerance, double upf2, string rolePrefix, string whose, FtfSettings settings)
        {
            if (exclusions == null) return region;
            for (var i = 0; i < exclusions.Count; i++)
            {
                var x = exclusions[i];
                var name = "Exclusion " + (i + 1) + whose;
                var role = rolePrefix + (i + 1).ToString(CultureInfo.InvariantCulture);
                List<Course> loop;
                string problem;
                if (x.Kind == Exclusion.PortionKind)
                {
                    IList<Course> basis = region.Outer;
                    if (x.Of != null)
                    {
                        basis = Extract(db, tr, x.Of.Handle, out problem);
                        if (basis == null) { composed.Errors.Add(name + ": the boundary it is measured on " + problem); return region; }
                        composed.Sources.Add(Source(x.Of, role + " OF", basis));
                    }
                    var steps = RefreshSteps(db, tr, x.PortionSteps, basis, x.Of, composed, name, role);
                    if (steps == null) return region;
                    x.PortionSteps = steps;
                    var portion = PortionBuilder.Build(basis, steps, tolerance);
                    if (!portion.Ok) { composed.Errors.AddRange(portion.Errors.Select(e => name + ": " + e)); return region; }
                    composed.Warnings.AddRange(portion.Warnings.Select(w => name + ": " + w));
                    loop = portion.Loop;
                }
                else
                {
                    var boundary = Extract(db, tr, x.Source != null ? x.Source.Handle : null, out problem);
                    if (boundary == null) { composed.Errors.Add(name + ": the excluded boundary " + problem); return region; }
                    loop = boundary.ToList();
                    composed.Sources.Add(Source(x.Source, role, boundary));
                }

                var result = RegionBuilder.Exclude(region, loop, tolerance);
                composed.Warnings.AddRange(result.Warnings.Select(w => name + ": " + w));
                if (!result.Ok) { composed.Errors.AddRange(result.Errors.Select(e => name + ": " + e)); return region; }
                x.Loop = loop;
                x.Effect = result.Effect;
                x.RemovedSquareFeet = result.Removed / upf2;
                region = result.Region;
            }
            return region;
        }

        private static List<PortionStep> RefreshSteps(Database db, Transaction tr, IList<PortionStep> steps, IList<Course> basis, GeometrySource basisSource,
                                                      Composed composed, string name, string role)
        {
            var refreshed = new List<PortionStep>();
            for (var i = 0; i < (steps ?? new List<PortionStep>()).Count; i++)
            {
                var s = steps[i];
                IList<Course> courses;
                string problem = null;
                if (basisSource != null && s.Handle == basisSource.Handle) courses = basis;
                else courses = string.IsNullOrWhiteSpace(s.Handle) ? new List<Course> { s.Line } : Extract(db, tr, s.Handle, out problem);
                if (courses == null || s.Segment >= courses.Count)
                {
                    composed.Errors.Add(name + ": the " + (s.Side ?? string.Empty).ToLowerInvariant() + " line " + (problem ?? "changed shape") );
                    return null;
                }
                refreshed.Add(new PortionStep { Side = s.Side, Distance = s.Distance, Line = courses[s.Segment], Handle = s.Handle, Segment = s.Segment });
                if (!string.IsNullOrWhiteSpace(s.Handle) && (basisSource == null || s.Handle != basisSource.Handle))
                    composed.Sources.Add(new GeometrySource { Handle = s.Handle, EntityType = "Line", Role = role + " LINE " + (i + 1), Fingerprint = EasementAnnotation.Fingerprint(courses) });
            }
            return refreshed;
        }

        private static List<Course> ComponentOuter(Database db, Transaction tr, EasementRecord record, EasementComponent c, FtfSettings settings, Composed composed)
        {
            var es = settings.Easements;
            var tolerance = es.ToleranceFt * settings.General.UnitsPerFoot;
            var name = "Component " + c.Label;
            var role = RolePrefixComponent + c.Label;
            string problem;
            if (c.Kind == EasementComponent.PortionKind)
            {
                var lot = Extract(db, tr, c.Parcel != null ? c.Parcel.Handle : null, out problem);
                if (lot == null) { composed.Errors.Add(name + ": its lot boundary " + problem); return null; }
                composed.Sources.Add(Source(c.Parcel, role + " LOT", lot));
                var steps = RefreshSteps(db, tr, c.PortionSteps, lot, c.Parcel, composed, name, role);
                if (steps == null) return null;
                c.PortionSteps = steps;
                var portion = PortionBuilder.Build(lot, steps, tolerance);
                if (!portion.Ok) { composed.Errors.AddRange(portion.Errors.Select(e => name + ": " + e)); return null; }
                composed.Warnings.AddRange(portion.Warnings.Select(w => name + ": " + w));
                c.Courses = null;
                return portion.Loop;
            }
            if (c.Kind == EasementComponent.BoundaryKind)
            {
                var boundary = Extract(db, tr, c.Parcel != null ? c.Parcel.Handle : null, out problem);
                if (boundary == null) { composed.Errors.Add(name + ": its boundary " + problem); return null; }
                composed.Sources.Add(Source(c.Parcel, role + " BOUNDARY", boundary));
                var check = AreaPath.Check(boundary, tolerance);
                if (check.Count > 0) { composed.Errors.AddRange(check.Select(e => name + ": " + e)); return null; }
                return boundary.ToList();
            }

            // A clicked area: corners and followed sides as they are now.
            var corners = new List<SelectedLocation>();
            if (c.PointOfCommencement != null)
            {
                var poc = EasementCommands.Refresh(db, tr, record, c.PointOfCommencement, role + " POC");
                if (poc == null) { composed.Errors.Add(name + ": its Point of Commencement was erased."); return null; }
                c.PointOfCommencement = poc;
                composed.Sources.Add(EasementCommands.SourceForLocation(db, tr, poc, role + " POC"));
            }
            for (var i = 0; i < (c.AnglePoints ?? new List<SelectedLocation>()).Count; i++)
            {
                var cornerRole = role + " " + EasementCommands.AngleRole(i);
                var corner = EasementCommands.Refresh(db, tr, record, c.AnglePoints[i], cornerRole);
                if (corner == null) { composed.Errors.Add(name + ": corner " + (i + 1) + " was erased."); return null; }
                corners.Add(corner);
                composed.Sources.Add(EasementCommands.SourceForLocation(db, tr, corner, cornerRole));
            }
            var loop = new List<Course>();
            for (var i = 0; i < corners.Count; i++)
            {
                var from = corners[i].Point;
                var to = corners[(i + 1) % corners.Count].Point;
                var side = c.AreaSides != null && i < c.AreaSides.Count ? c.AreaSides[i] : new AreaSide();
                if (string.IsNullOrWhiteSpace(side.FollowHandle)) { loop.Add(Course.Line(from, to)); continue; }
                var path = Extract(db, tr, side.FollowHandle, out problem);
                if (path == null) { composed.Errors.Add(name + ": the line side " + (i + 1) + " follows " + problem); return null; }
                var courses = AreaPath.Between(path, from, to, tolerance, out problem);
                if (courses == null) { composed.Errors.Add(name + ", side " + (i + 1) + ": " + problem); return null; }
                loop.AddRange(courses);
                composed.Sources.Add(new GeometrySource { Handle = side.FollowHandle, EntityType = side.FollowType, Role = role + " SIDE " + (i + 1) + " FOLLOWS", Fingerprint = EasementAnnotation.Fingerprint(path) });
            }
            loop = loop.Where(x => x.Length > tolerance).ToList();
            var problems = AreaPath.Check(loop, tolerance);
            if (problems.Count > 0) { composed.Errors.AddRange(problems.Select(e => name + ": " + e)); return null; }
            c.AnglePoints = corners;
            c.Courses = EasementAnnotation.Number(loop, es);
            c.CommencementTie = null;
            c.CommencementTieCourses = null;
            if (c.PointOfCommencement != null)
            {
                var follows = new List<GeometrySource>();
                var tiePath = EasementAreaCommands.TiePath(db, tr, c.PointOfCommencement.Point, corners[0].Point, c.CommencementTieFollows, tolerance, role + " ", follows, out problem);
                if (tiePath == null) { composed.Errors.Add(name + ": the commencement tie -- " + problem); return null; }
                CourseData tie;
                List<CourseData> tieCourses;
                Ties.Set(tiePath, out tie, out tieCourses);
                c.CommencementTie = tie;
                c.CommencementTieCourses = tieCourses;
                if (follows.Count > 0) composed.Sources.AddRange(follows);
            }
            return loop;
        }

        private static IList<Course> Extract(Database db, Transaction tr, string handle, out string problem)
        {
            problem = null;
            var entity = EasementCommands.Resolve(db, tr, handle);
            if (entity == null) { problem = "was erased."; return null; }
            var courses = EasementCommands.Extract(entity, out problem);
            if (courses == null) problem = "cannot be read (" + problem + ").";
            return courses;
        }

        private static GeometrySource Source(GeometrySource stored, string role, IList<Course> courses)
        {
            return new GeometrySource { Handle = stored.Handle, EntityType = stored.EntityType, Role = role, Fingerprint = EasementAnnotation.Fingerprint(courses) };
        }

        /// <summary>Writes the regions into the record: A's outline and holes, each component, the areas, and the sources for the change check.</summary>
        internal static void Store(EasementRecord record, Composed composed, FtfSettings settings)
        {
            var es = settings.Easements;
            var upf2 = settings.General.UnitsPerFoot * settings.General.UnitsPerFoot;
            var hasComposition = record.HasComposition;
            record.BoundaryCourses = EasementAnnotation.Number(composed.Primary.Outer, es);
            record.Holes = composed.Primary.Holes.Select(h => EasementAnnotation.Number(h, es)).ToList();
            record.PrimaryAreaSquareFeet = hasComposition ? composed.Primary.Area / upf2 : (double?)null;
            foreach (var kv in composed.Components)
            {
                kv.Key.BoundaryCourses = EasementAnnotation.Number(kv.Value.Outer, es);
                kv.Key.Holes = kv.Value.Holes.Select(h => EasementAnnotation.Number(h, es)).ToList();
                kv.Key.AreaSquareFeet = kv.Value.Area / upf2;
            }
            record.AreaSquareFeet = (composed.Primary.Area + composed.Components.Sum(kv => kv.Value.Area)) / upf2;
            record.ComponentsOverlap = composed.Overlaps;
            if (composed.Components.Count == 0) record.PhysicalAreaSquareFeet = null;
            else if (!composed.Overlaps) record.PhysicalAreaSquareFeet = record.AreaSquareFeet;
            else
            {
                var regions = new List<RegionShape> { composed.Primary };
                regions.AddRange(composed.Components.Select(kv => kv.Value));
                var union = RegionBuilder.UnionArea(regions, es.ToleranceFt * settings.General.UnitsPerFoot);
                record.PhysicalAreaSquareFeet = union.HasValue ? union.Value / upf2 : (double?)null;
                var note = union.HasValue
                    ? "SUM OF COMPONENT AREAS = " + record.AreaSquareFeet.ToString("N0", CultureInfo.InvariantCulture) + " SF; TOTAL PHYSICAL AREA = " +
                      record.PhysicalAreaSquareFeet.Value.ToString("N0", CultureInfo.InvariantCulture) + " SF (overlap counted once). Which belongs in the legal description is the surveyor's decision."
                    : "Components overlap in a way FTF did not resolve exactly; only the SUM OF COMPONENT AREAS (" + record.AreaSquareFeet.ToString("N0", CultureInfo.InvariantCulture) +
                      " SF, overlap counted twice) is known. Work out the physical area before stating one.";
                if (!composed.Warnings.Contains(note)) composed.Warnings.Add(note);
            }
            record.Acres = record.AreaSquareFeet / EasementAnnotation.SquareFeetPerAcre;

            record.RouteSources = (record.RouteSources ?? new List<GeometrySource>())
                .Where(s => !s.Role.StartsWith(RolePrefixExclusion, StringComparison.Ordinal) && !s.Role.StartsWith(RolePrefixComponent, StringComparison.Ordinal))
                .Concat(composed.Sources).ToList();
            foreach (var w in composed.Warnings)
                if (!record.Warnings.Contains(w)) record.Warnings.Add(w);
        }

        /// <summary>
        /// Draws every region: outlines (A's outer outline stays the EasementBoundary; holes and
        /// component outlines are EasementLine) and one hatch per region with its holes left open.
        /// Component tags B, C... sit inside each component.
        /// </summary>
        internal static Polyline DraftRegions(Database db, Transaction tr, Editor ed, Composed composed, Action<AcEntity, FtfEntityKind, string> add,
                                              string outlineLayer, bool hatch, string pattern, string hatchLayer, double hatchScale,
                                              string textLayer, ObjectId textStyle, double textHeight, double tolerance)
        {
            Polyline primaryOutline = null;
            var regions = new List<KeyValuePair<string, RegionShape>> { new KeyValuePair<string, RegionShape>(null, composed.Primary) };
            regions.AddRange(composed.Components.Select(kv => new KeyValuePair<string, RegionShape>(kv.Key.Label, kv.Value)));
            foreach (var kv in regions)
            {
                var outline = EasementCommands.ToPolyline(kv.Value.Outer, true);
                add(outline, kv.Key == null ? FtfEntityKind.EasementBoundary : FtfEntityKind.EasementLine, outlineLayer);
                if (kv.Key == null) primaryOutline = outline;
                var holes = new List<Polyline>();
                foreach (var h in kv.Value.Holes)
                {
                    var hole = EasementCommands.ToPolyline(h, true);
                    add(hole, FtfEntityKind.EasementLine, outlineLayer);
                    holes.Add(hole);
                }
                if (hatch && !string.IsNullOrWhiteSpace(pattern)) Hatch(db, tr, ed, add, outline, holes, pattern, hatchLayer, hatchScale);
                if (kv.Key != null || composed.Components.Count > 0)
                {
                    var at = PointInside(kv.Value, tolerance);
                    var tag = new MText();
                    tag.SetDatabaseDefaults(db);
                    if (!textStyle.IsNull) tag.TextStyleId = textStyle;
                    tag.TextHeight = textHeight * 1.4;
                    tag.Attachment = AttachmentPoint.MiddleCenter;
                    tag.Location = new Point3d(at.X, at.Y, 0);
                    tag.Contents = kv.Key ?? "A";
                    add(tag, FtfEntityKind.EasementText, textLayer);
                }
            }
            return primaryOutline;
        }

        private static P2 PointInside(RegionShape region, double tolerance)
        {
            var p = StripTrim.PointInside(region.Outer, tolerance);
            if (region.Contains(p)) return p;
            foreach (var c in region.Outer)
            {
                var mid = c.PointAt(c.Length / 2);
                foreach (var step in new[] { 1.0, 3.0, 10.0 })
                {
                    var q = mid + c.DirectionAt(c.Length / 2).LeftNormal() * (Loops.SignedArea(region.Outer) > 0 ? step : -step);
                    if (region.Contains(q)) return q;
                }
            }
            return p;
        }

        private static void Hatch(Database db, Transaction tr, Editor ed, Action<AcEntity, FtfEntityKind, string> add, Polyline outer,
                                  IList<Polyline> holes, string pattern, string layer, double scale)
        {
            try
            {
                var hatch = new Hatch();
                hatch.SetDatabaseDefaults(db);
                add(hatch, FtfEntityKind.EasementHatch, layer);
                hatch.PatternScale = scale;
                hatch.SetHatchPattern(HatchPatternType.PreDefined, pattern);
                hatch.Associative = false;
                hatch.HatchStyle = HatchStyle.Normal;
                hatch.AppendLoop(HatchLoopTypes.External, new ObjectIdCollection { outer.ObjectId });
                foreach (var hole in holes) hatch.AppendLoop(HatchLoopTypes.Default, new ObjectIdCollection { hole.ObjectId });
                hatch.EvaluateHatch(true);
                var ms = CadUtil.ModelSpace(db, tr, OpenMode.ForRead);
                ((DrawOrderTable)tr.GetObject(ms.DrawOrderTableId, OpenMode.ForWrite)).MoveToBottom(new ObjectIdCollection { hatch.ObjectId });
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                ed.WriteMessage("\n  Hatch \"{0}\" could not be made ({1}); the outline is drawn without it.", pattern, ex.Message);
            }
        }
    }
}
