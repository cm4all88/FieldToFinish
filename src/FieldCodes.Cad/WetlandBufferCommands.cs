using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using FieldCodes.Easements;
using FieldCodes.Wetlands;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcEntity = Autodesk.AutoCAD.DatabaseServices.Entity;

namespace FieldCodes.Cad
{
    /// <summary>
    /// FTFWETLANDBUFFER (FTFWLB): the wetland buffer, from the office's WLB routine, done exactly.
    ///
    /// Select wetland lines; give the buffer distance and, if wanted, a smoothing radius for the
    /// inside corners. A closed wetland is buffered outward; an open edge asks which side. The
    /// buffer has round, tangent outside corners and is everywhere exactly the distance from the
    /// wetland; smoothing rounds the inside corners too (out by distance + radius, back in by the
    /// radius), so it never comes closer. Where a hard-surface line (rules.json wetlandBuffer
    /// hardSurfaceLayers: edges of asphalt, concrete, gravel, curbs, buildings) lies between the
    /// buffer and the wetland, the buffer is cut there and the part beyond is not drawn. A part
    /// only partly behind a hard surface is kept and reported to check.
    ///
    /// The wetland and the hard-surface lines are read, never changed. The buffer is FTF's
    /// drafting on the wetland buffer layer: FTFCLEAN leaves it, FTFWHAT names it, and running
    /// the command again on the same wetland replaces its buffer instead of adding a second.
    ///
    /// Its settings are on Settings > Survey Drafting > Wetland Buffers (WetlandBufferPage).
    ///
    /// UNTESTED in Civil 3D. The offset and the trim are unit tested in FieldCodes.Wetlands.
    /// </summary>
    public sealed class WetlandBufferCommands
    {
        private static double? _lastDistance;
        private static double? _lastSmoothing;
        private static bool? _lastTrim;

        /// <summary>Chord length for lines that are not lines and arcs (splines, ellipses): read
        /// as short straight pieces this long at most, in drawing units.</summary>
        private const double SampleStep = 0.25;

        private sealed class Wetland
        {
            public ObjectId Id;
            public string Handle;
            public string Layer;
            public IList<Course> Courses;
            public bool Closed;
            public int Side = 1;
            public string Note;
        }

        [CommandMethod("FTFWETLANDBUFFER", CommandFlags.Modal | CommandFlags.UsePickSet)]
        [CommandMethod("FTFWLB", CommandFlags.Modal | CommandFlags.UsePickSet)]
        public void FtfWetlandBuffer()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;
            var db = doc.Database;

            RulesConfig cfg;
            try { cfg = FtfSession.Rules(db); }
            catch (ConfigException ex)
            {
                ed.WriteMessage("\nRules problem: {0}\n", ex.Message);
                return;
            }
            var rule = cfg.WetlandBuffer ?? new WetlandBufferRule();

            // ---------------------------------------------------------------- what to buffer
            var selection = ed.SelectImplied();
            if (selection.Status != PromptStatus.OK)
                selection = ed.GetSelection(new PromptSelectionOptions
                {
                    MessageForAdding = "\nSelect wetland lines (closed ones are buffered outward; for an open edge you pick the side): "
                });
            if (selection.Status != PromptStatus.OK || selection.Value == null) return;

            var wetlands = new List<Wetland>();
            // A full transaction: reading a 3D polyline's vertices needs one.
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (var id in selection.Value.GetObjectIds())
                {
                    var entity = tr.GetObject(id, OpenMode.ForRead, false, true) as AcEntity;
                    if (entity == null) continue;
                    if (Ownership.Read(entity) != null)
                    {
                        ed.WriteMessage("\n  Skipped an FTF object on {0} (handle {1}): select the wetland line itself.", entity.Layer, entity.Handle);
                        continue;
                    }
                    string note;
                    bool closed;
                    var courses = Read(entity, out closed, out note);
                    if (courses == null)
                    {
                        ed.WriteMessage("\n  Skipped {0} on {1} (handle {2}): {3}", entity.GetType().Name, entity.Layer, entity.Handle, note);
                        continue;
                    }
                    wetlands.Add(new Wetland
                    {
                        Id = id, Handle = entity.Handle.ToString(), Layer = entity.Layer,
                        Courses = courses, Closed = closed, Note = note
                    });
                }
                tr.Commit();
            }
            if (wetlands.Count == 0)
            {
                ed.WriteMessage("\nFTFWETLANDBUFFER: no wetland lines selected.\n");
                return;
            }

            // ---------------------------------------------------------------- how
            var distanceAsk = new PromptDistanceOptions("\nBuffer distance")
            {
                DefaultValue = _lastDistance ?? rule.Distance, UseDefaultValue = true, AllowNegative = false, AllowZero = false
            };
            var distance = ed.GetDistance(distanceAsk);
            if (distance.Status != PromptStatus.OK) return;
            _lastDistance = distance.Value;

            var smoothingAsk = new PromptDistanceOptions("\nSmoothing radius for inside corners, 0 = none")
            {
                DefaultValue = _lastSmoothing ?? rule.Smoothing, UseDefaultValue = true, AllowNegative = false, AllowZero = true
            };
            var smoothing = ed.GetDistance(smoothingAsk);
            if (smoothing.Status != PromptStatus.OK) return;
            _lastSmoothing = smoothing.Value;

            var trimDefault = _lastTrim ?? rule.TrimAtHardSurfaces;
            var trimAsk = new PromptKeywordOptions(string.Format(CultureInfo.InvariantCulture,
                "\nCut the buffer back at hard surfaces (edges of asphalt, concrete, gravel, curbs, buildings)? [Yes/No] <{0}>: ",
                trimDefault ? "Yes" : "No")) { AllowNone = true };
            trimAsk.Keywords.Add("Yes");
            trimAsk.Keywords.Add("No");
            trimAsk.Keywords.Default = trimDefault ? "Yes" : "No";
            var trimAnswer = ed.GetKeywords(trimAsk);
            if (trimAnswer.Status != PromptStatus.OK && trimAnswer.Status != PromptStatus.None) return;
            var trim = trimAnswer.Status == PromptStatus.None ? trimDefault : trimAnswer.StringResult == "Yes";
            _lastTrim = trim;

            for (var i = 0; i < wetlands.Count; i++)
            {
                var w = wetlands[i];
                if (w.Closed) continue;
                var pick = ed.GetPoint(new PromptPointOptions(string.Format(CultureInfo.InvariantCulture,
                    "\nPick the side to buffer for the open line on {0} (handle {1}): ", w.Layer, w.Handle)));
                if (pick.Status != PromptStatus.OK) return;
                w.Side = WetlandBuffer.SideOf(w.Courses, new P2(pick.Value.X, pick.Value.Y));
            }

            // ---------------------------------------------------------------- draw
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var hard = trim ? HardSurfaces(db, tr, rule, wetlands) : new List<HardSurfaceLine>();
                if (trim)
                    ed.WriteMessage("\n{0} hard-surface line(s) on {1}.", hard.Count,
                                    rule.HardSurfaceLayers.Count > 0 ? string.Join(", ", rule.HardSurfaceLayers.ToArray()) : "(no layers listed)");
                Ownership.EnsureRegApp(db, tr);
                var layerId = CadUtil.EnsureLayer(db, tr, rule.Layer);
                var drawn = 0;
                foreach (var w in wetlands)
                {
                    var reach = distance.Value + smoothing.Value;
                    var near = Box.Around(w.Courses, reach * 1.5 + 1);
                    var result = WetlandBuffer.Build(new WetlandBufferRequest
                    {
                        Wetland = w.Courses, Closed = w.Closed, Side = w.Side, Distance = distance.Value, Smoothing = smoothing.Value,
                        HardSurfaces = hard.Where(h => near.Touches(h.Courses)).ToList()
                    });

                    ed.WriteMessage("\nWetland on {0} (handle {1}, {2}):", w.Layer, w.Handle, w.Closed ? "closed" : (w.Side > 0 ? "open, left side" : "open, right side"));
                    if (!string.IsNullOrEmpty(w.Note)) ed.WriteMessage("\n  {0}", w.Note);
                    foreach (var p in result.Problems) ed.WriteMessage("\n  NOT DRAWN: {0}", p);
                    if (result.Problems.Count > 0) continue;

                    // The same wetland again: its earlier buffer goes, never a second one.
                    var replaced = 0;
                    foreach (var old in Ownership.FindOwned(db, tr, s => s.Kind == FtfEntityKind.WetlandBuffer && s.PointNumber == w.Handle))
                    {
                        var entity = tr.GetObject(old.Key, OpenMode.ForWrite, false, true) as AcEntity;
                        if (entity == null) continue;
                        entity.Erase();
                        replaced++;
                    }

                    var tag = string.Format(CultureInfo.InvariantCulture, "D={0:0.###};R={1:0.###};trim={2}", distance.Value, smoothing.Value, trim ? "yes" : "no");
                    foreach (var path in result.Paths)
                    {
                        var pl = EasementCommands.ToPolyline(path.Courses, path.Closed);
                        pl.SetDatabaseDefaults(db);
                        pl.LayerId = layerId;
                        CadUtil.AddToModelSpace(db, tr, pl);
                        Ownership.Stamp(pl, w.Handle, cfg.Version, FtfEntityKind.WetlandBuffer, null, tag);
                        drawn++;
                    }

                    ed.WriteMessage("\n  Buffer {0} drawn on {1}: {2} line(s), {3} long{4}.",
                                    distance.Value.ToString("0.###", CultureInfo.InvariantCulture), rule.Layer, result.Paths.Count,
                                    result.Paths.Sum(p => p.Length).ToString("0.##", CultureInfo.InvariantCulture),
                                    replaced > 0 ? "; replaced the buffer drawn earlier for this wetland" : string.Empty);
                    if (smoothing.Value > 0)
                        ed.WriteMessage("\n  Inside corners rounded to {0}; nowhere closer than {1} to the wetland.",
                                        smoothing.Value.ToString("0.###", CultureInfo.InvariantCulture),
                                        result.ClosestApproach.ToString("0.###", CultureInfo.InvariantCulture));
                    foreach (var group in result.Trimmed.GroupBy(t => t.BlockedBy.Layer))
                        ed.WriteMessage("\n  Cut back at {0}: {1} piece(s), {2} not drawn (a hard surface lies between them and the wetland).",
                                        group.Key, group.Count(), group.Sum(t => t.Length).ToString("0.##", CultureInfo.InvariantCulture));
                    foreach (var n in result.Notes) ed.WriteMessage("\n  CHECK: {0}", n);
                }
                tr.Commit();
                ed.WriteMessage("\nFTFWETLANDBUFFER: {0} buffer line(s) drawn. The wetland and hard-surface lines were not changed.\n", drawn);
            }
        }

        // ------------------------------------------------------------------ reading

        /// <summary>A line's courses, exact when it is lines and arcs; a spline, an ellipse or a
        /// survey figure that is not is read as short chords (and says so).</summary>
        private static IList<Course> Read(AcEntity entity, out bool closed, out string note)
        {
            note = null;
            closed = false;
            var circle = entity as Circle;
            if (circle != null)
            {
                closed = true;
                var c = new P2(circle.Center.X, circle.Center.Y);
                var east = c + new P2(circle.Radius, 0);
                var west = c + new P2(-circle.Radius, 0);
                return new List<Course> { Course.Arc(east, west, c, true), Course.Arc(west, east, c, true) };
            }
            var curve = entity as Curve;
            if (curve == null)
            {
                note = "not a line, polyline, arc, circle, spline or survey figure";
                return null;
            }
            string problem;
            var courses = EasementCommands.Extract(entity, out problem);
            if (courses == null || courses.Count == 0)
            {
                courses = Sample(curve);
                if (courses == null)
                {
                    note = problem ?? "its geometry could not be read";
                    return null;
                }
                note = string.Format(CultureInfo.InvariantCulture, "read as {0} short straight pieces (it is not made of lines and arcs)", courses.Count);
            }
            var gap = courses[0].Start.DistanceTo(courses[courses.Count - 1].End);
            var isClosed = false;
            try { isClosed = curve.Closed; } catch (Autodesk.AutoCAD.Runtime.Exception) { }
            closed = isClosed || gap < 1e-6;
            if (isClosed && gap > 1e-6) courses.Add(Course.Line(courses[courses.Count - 1].End, courses[0].Start));
            return courses;
        }

        private static IList<Course> Sample(Curve curve)
        {
            try
            {
                var length = curve.GetDistanceAtParameter(curve.EndParam) - curve.GetDistanceAtParameter(curve.StartParam);
                if (!(length > 0)) return null;
                var n = (int)Math.Min(20000, Math.Max(32, Math.Ceiling(length / SampleStep)));
                var points = new List<P2>();
                for (var i = 0; i <= n; i++)
                {
                    var p = curve.GetPointAtDist(length * i / n);
                    points.Add(new P2(p.X, p.Y));
                }
                var courses = new List<Course>();
                for (var i = 1; i < points.Count; i++)
                    if (points[i].DistanceTo(points[i - 1]) > 1e-9) courses.Add(Course.Line(points[i - 1], points[i]));
                return courses.Count > 0 ? courses : null;
            }
            catch (Autodesk.AutoCAD.Runtime.Exception) { return null; }
        }

        /// <summary>Every line on a hard-surface layer in model space (read only), except the
        /// wetland lines themselves and FTF's own buffers.</summary>
        private static List<HardSurfaceLine> HardSurfaces(Database db, Transaction tr, WetlandBufferRule rule, IList<Wetland> wetlands)
        {
            var skip = new HashSet<ObjectId>(wetlands.Select(w => w.Id));
            var found = new List<HardSurfaceLine>();
            var ms = CadUtil.ModelSpace(db, tr, OpenMode.ForRead);
            foreach (ObjectId id in ms)
            {
                if (id.IsErased || skip.Contains(id)) continue;
                var entity = tr.GetObject(id, OpenMode.ForRead, false, true) as AcEntity;
                if (!(entity is Curve) || !rule.IsHardSurface(entity.Layer)) continue;
                var stamp = Ownership.Read(entity);
                if (stamp != null && stamp.Kind == FtfEntityKind.WetlandBuffer) continue;
                bool closed;
                string note;
                var courses = Read(entity, out closed, out note);
                if (courses == null) continue;
                found.Add(new HardSurfaceLine(entity.Handle.ToString(), entity.Layer, courses));
            }
            return found;
        }

        /// <summary>A box round a wetland and its buffer, to leave far-off hard surfaces out.</summary>
        private struct Box
        {
            private double _minX, _minY, _maxX, _maxY;

            public static Box Around(IEnumerable<Course> courses, double pad)
            {
                var b = new Box { _minX = double.MaxValue, _minY = double.MaxValue, _maxX = double.MinValue, _maxY = double.MinValue };
                foreach (var c in courses)
                    foreach (var p in new[] { c.Start, c.End, c.PointAt(c.Length / 2) })
                    {
                        b._minX = Math.Min(b._minX, p.X); b._maxX = Math.Max(b._maxX, p.X);
                        b._minY = Math.Min(b._minY, p.Y); b._maxY = Math.Max(b._maxY, p.Y);
                    }
                b._minX -= pad; b._minY -= pad; b._maxX += pad; b._maxY += pad;
                return b;
            }

            public bool Touches(IEnumerable<Course> courses)
            {
                foreach (var c in courses)
                {
                    var r = c.Kind == CourseKind.Arc ? c.Radius : 0;
                    var lo = c.Kind == CourseKind.Arc ? new P2(c.Center.X - r, c.Center.Y - r) : new P2(Math.Min(c.Start.X, c.End.X), Math.Min(c.Start.Y, c.End.Y));
                    var hi = c.Kind == CourseKind.Arc ? new P2(c.Center.X + r, c.Center.Y + r) : new P2(Math.Max(c.Start.X, c.End.X), Math.Max(c.Start.Y, c.End.Y));
                    if (lo.X <= _maxX && hi.X >= _minX && lo.Y <= _maxY && hi.Y >= _minY) return true;
                }
                return false;
            }
        }
    }
}
