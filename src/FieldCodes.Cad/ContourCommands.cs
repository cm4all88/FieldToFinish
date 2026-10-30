using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using FieldCodes.Contours;
using FieldCodes.Easements;
using FieldCodes.Settings;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcEntity = Autodesk.AutoCAD.DatabaseServices.Entity;
using CivilDocument = Autodesk.Civil.ApplicationServices.CivilDocument;
using TinSurface = Autodesk.Civil.DatabaseServices.TinSurface;

namespace FieldCodes.Cad
{
    /// <summary>
    /// FTFCONTOUR: contour polylines at exact elevations, taken off an existing TIN surface.
    ///
    /// Pick the surface, type an elevation (12.6), get the contour: every piece where the
    /// surface crosses that elevation, as a polyline whose elevation is exactly the number
    /// typed, on the contour layer. Several elevations (12.6, 13.0, 13.5) or a run
    /// (10 to 14 by 0.5) go in one command; B limits the contours to a closed polyline or
    /// circle; L toggles elevation labels.
    ///
    /// The surface is opened for read and nothing else. Civil 3D's own extraction
    /// (TinSurface.ExtractContoursAt) supplies the geometry; its temporary polylines are
    /// read and erased, and FTF's are drawn in their place. Nothing is added to the surface
    /// definition; no triangle, breakline, boundary, point, style or contour interval is
    /// touched; the surface is never rebuilt -- an out-of-date surface is reported and
    /// contoured as last built. The surface's statistics are read before and after, and if
    /// they differ in any way the whole command is rolled back.
    ///
    /// Everything happens in one transaction, so one U undoes it. Running it again at the
    /// same elevation on the same surface replaces its own earlier contours (inside the
    /// boundary, when one is used) rather than drawing duplicates.
    ///
    /// UNTESTED against Civil 3D: the parsing, clipping, tracing, label spacing and report
    /// are unit tested; the API calls here are checked only by the compile check.
    /// </summary>
    public sealed class ContourCommands
    {
        /// <summary>The surface picked last time, offered as the default.</summary>
        private static ObjectId _lastSurface = ObjectId.Null;

        /// <summary>Arcs in a boundary are followed to within this, in drawing units.</summary>
        private const double ChordTolerance = 1e-4;

        private sealed class SurfaceChoice
        {
            public ObjectId Id;
            public string Name;
        }

        /// <summary>Thrown when the surface reads differently after extraction: rolls everything back.</summary>
        private sealed class SurfaceChangedException : System.Exception
        {
            public SurfaceChangedException(string message) : base(message) { }
        }

        [CommandMethod("FTFCONTOUR", CommandFlags.Modal)]
        public void FtfContour()
        {
            // Lets the headless smoke test prove this command really ran (see FtfCommandTrace).
            FtfCommandTrace.Start("FTFCONTOUR");
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;
            var db = doc.Database;

            RulesConfig cfg;
            FtfSettings settings;
            try
            {
                cfg = FtfSession.Rules(db);
                settings = FtfSession.SettingsFor(db, cfg);
            }
            catch (ConfigException ex)
            {
                ed.WriteMessage("\nFTFCONTOUR: rules problem.\n{0}\n", ex.Message);
                return;
            }

            var cs = settings.Contours;
            var problems = new List<string>();
            cs.Validate(problems);
            if (problems.Count > 0)
            {
                ed.WriteMessage("\nFTFCONTOUR: the contour settings need attention (FTFSETUP, Contours):\n  {0}\n",
                                string.Join("\n  ", problems.ToArray()));
                return;
            }

            // ------------------------------------------------------------ the surface
            List<SurfaceChoice> surfaces;
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                surfaces = TinSurfaces(db, tr);
                tr.Commit();
            }
            if (surfaces.Count == 0)
            {
                ed.WriteMessage("\nFTFCONTOUR: this drawing has no TIN surface to contour.\n");
                return;
            }

            var surfaceId = AskSurface(ed, surfaces);
            if (surfaceId.IsNull) return;
            _lastSurface = surfaceId;
            var surfaceName = surfaces.First(s => s.Id == surfaceId).Name;

            // ------------------------------------------- elevations, boundary and labels
            var labels = cs.LabelByDefault;
            ContourBoundary boundary = null;
            string boundaryName = null;
            var units = UnitsWord(db.Insunits);
            ElevationList elevations;
            while (true)
            {
                var prompt = new PromptStringOptions(string.Format(CultureInfo.InvariantCulture,
                    "\nElevation(s) on \"{0}\"{1} -- 12.6, or 12.6, 13, 13.5, or 10 to 14 by 0.5 " +
                    "[Boundary={2} / Labels={3}]: ",
                    surfaceName, units.Length > 0 ? " in " + UnitsName(db.Insunits) : string.Empty,
                    boundaryName ?? "none", labels ? "on" : "off"))
                {
                    AllowSpaces = true
                };
                var answer = ed.GetString(prompt);
                if (answer.Status != PromptStatus.OK)
                {
                    ed.WriteMessage("\nFTFCONTOUR cancelled; nothing drawn.\n");
                    return;
                }

                var typed = (answer.StringResult ?? string.Empty).Trim();
                if (typed.Length == 0)
                {
                    ed.WriteMessage("\nType an elevation (Esc to cancel).");
                    continue;
                }
                if (IsKeyword(typed, "BOUNDARY"))
                {
                    AskBoundary(doc, ed, ref boundary, ref boundaryName);
                    continue;
                }
                if (IsKeyword(typed, "LABELS"))
                {
                    labels = !labels;
                    ed.WriteMessage("\nLabels {0}.", labels ? "on" : "off");
                    continue;
                }

                elevations = ContourElevations.Parse(typed);
                foreach (var note in elevations.Notes) ed.WriteMessage("\n  {0}", note);
                if (elevations.Ok) break;
                foreach (var problem in elevations.Problems) ed.WriteMessage("\n  {0}", problem);
            }

            // ------------------------------------------------------- one transaction
            var report = new ContourRunReport
            {
                SurfaceName = surfaceName,
                Units = units,
                Clipped = boundary != null,
                Labelled = labels
            };
            var temporaries = new List<ObjectId>();
            var committed = false;

            using (doc.LockDocument())
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    try
                    {
                        Draw(db, tr, ed, surfaceId, elevations, boundary, labels, settings, cfg.Version,
                             temporaries, report);
                        tr.Commit();
                        committed = true;
                    }
                    catch (SurfaceChangedException ex)
                    {
                        ed.WriteMessage("\nFTFCONTOUR: {0}\nEverything was rolled back; nothing drawn.\n", ex.Message);
                    }
                    catch (Autodesk.AutoCAD.Runtime.Exception ex)
                    {
                        ed.WriteMessage("\nFTFCONTOUR: AutoCAD error: {0} ({1}). Nothing drawn.\n",
                                        ex.Message, ex.ErrorStatus);
                    }
                    catch (System.Exception ex)
                    {
                        ed.WriteMessage("\nFTFCONTOUR: {0}. Nothing drawn.\n", ex.Message);
                    }
                }

                // Civil 3D's extraction adds its polylines to the drawing itself. If the run
                // failed after that, make sure none of them is left behind.
                if (!committed) EraseLeftovers(db, temporaries);
            }

            if (!committed) return;
            foreach (var line in report.Lines()) ed.WriteMessage("\n{0}", line);
            ed.WriteMessage("\n");
            FtfSession.RegenIfPending(ed);   // labels shown above their masks
            FtfCommandTrace.Finish("FTFCONTOUR");
        }

        // ================================================================== drawing

        private static void Draw(Database db, Transaction tr, Editor ed, ObjectId surfaceId,
                                 ElevationList elevations, ContourBoundary boundary, bool labels,
                                 FtfSettings settings, string rulesVersion, List<ObjectId> temporaries,
                                 ContourRunReport report)
        {
            var cs = settings.Contours;

            // Read only, for the whole command: AutoCAD itself refuses any change to an
            // object opened this way.
            var surface = (TinSurface)tr.GetObject(surfaceId, OpenMode.ForRead);
            var before = Fingerprint(surface);
            report.Before = before;
            report.SurfaceName = surface.Name;

            if (before.OutOfDate == true)
                ed.WriteMessage("\nNote: surface \"{0}\" is out of date. FTFCONTOUR never rebuilds a surface; " +
                                "these contours follow it as last built. Rebuild it yourself first if its " +
                                "definition has changed.", surface.Name);

            var surfaceHandle = surface.Handle.ToString();
            var layerId = ProductionLayers.Get(db, tr, cs.Layer, settings);
            var labelLayerId = labels ? ProductionLayers.Get(db, tr, cs.LabelLayer, settings) : ObjectId.Null;
            var layer = tr.GetObject(layerId, OpenMode.ForRead) as LayerTableRecord;
            report.Layer = layer != null ? layer.Name : cs.Layer;
            Ownership.EnsureRegApp(db, tr);

            var earlier = EarlierContours(db, tr, surfaceHandle);
            List<TinTriangle> triangles = null;

            foreach (var elevation in elevations.Elevations)
            {
                var result = new ContourElevationResult { Elevation = elevation };
                report.Results.Add(result);
                var key = ContourKeys.Owner(surfaceHandle, elevation.Value);

                // Off the surface entirely: nothing to extract, and the surface is not asked.
                var outcome = ContourRunReport.Classify(elevation.Value, before.MinimumElevation,
                                                        before.MaximumElevation, 1, 1, false);
                if (outcome == ContourOutcome.BelowSurface || outcome == ContourOutcome.AboveSurface)
                {
                    result.Outcome = outcome;
                    result.Replaced += ReplaceEarlier(db, tr, earlier, key, boundary, rulesVersion, result);
                    continue;
                }

                IList<ContourPath> extracted;
                try
                {
                    double? difference;
                    extracted = Extract(surface, tr, elevation.Value, temporaries, out difference);
                    result.ExtractedElevationDifference = difference;
                }
                catch (System.Exception ex)
                {
                    // Civil 3D's extraction failed: trace the same contour off the surface's
                    // visible triangles instead (read only), and say so. Anything the failed
                    // call did add to the drawing goes first.
                    EraseTemporaries(tr, temporaries);
                    result.Traced = true;
                    result.ExtractionProblem = ex.Message;
                    if (triangles == null) triangles = VisibleTriangles(surface);
                    extracted = ContourTracer.Trace(triangles, elevation.Value);
                }

                var pieces = (boundary == null
                        ? extracted
                        : ContourClipper.ClipAll(extracted, boundary, true, ChordTolerance))
                    .Where(p => p.IsDrawable && p.Length > 0)
                    .ToList();

                result.Extracted = extracted.Count;
                result.Outcome = ContourRunReport.Classify(elevation.Value, before.MinimumElevation,
                                                           before.MaximumElevation, extracted.Count,
                                                           pieces.Count, boundary != null);

                // The surface's contour at this elevation (inside the boundary) is now exactly
                // these pieces: earlier FTF contours there are replaced, not duplicated.
                result.Replaced += ReplaceEarlier(db, tr, earlier, key, boundary, rulesVersion, result);

                foreach (var piece in pieces)
                {
                    DrawPiece(db, tr, piece, elevation, key, layerId, rulesVersion);
                    result.Pieces++;
                    if (piece.Closed) result.ClosedPieces++;
                    result.Vertices += piece.Vertices.Count;
                    result.Length += piece.Length;

                    if (labels)
                        result.Labels += Label(db, tr, piece, elevation, key, labelLayerId, settings, rulesVersion);
                }

                if (cs.CheckAgainstSurface)
                    CheckAgainstSurface(surface, pieces, elevation.Value, result);
            }

            // None of Civil 3D's own extracted polylines may survive the command.
            EraseTemporaries(tr, temporaries);

            // Proof the surface came through untouched; any difference rolls the lot back.
            var after = Fingerprint(surface);
            report.After = after;
            var differences = after.DifferencesFrom(before);
            if (differences.Count > 0)
                throw new SurfaceChangedException("surface \"" + surface.Name + "\" reads differently after " +
                                                  "extraction (" + string.Join("; ", differences.ToArray()) + ").");
        }

        /// <summary>
        /// Civil 3D's contour at one elevation. ExtractContoursAt adds polylines to the drawing;
        /// each is read exactly (vertices, bulges, closure) and erased, so only FTF's own
        /// polylines remain.
        /// </summary>
        private static IList<ContourPath> Extract(TinSurface surface, Transaction tr, double elevation,
                                                  List<ObjectId> temporaries, out double? elevationDifference)
        {
            elevationDifference = null;
            var paths = new List<ContourPath>();
            var ids = surface.ExtractContoursAt(elevation);
            if (ids == null) return paths;

            var list = new List<ObjectId>();
            foreach (ObjectId id in ids) list.Add(id);
            temporaries.AddRange(list);

            foreach (var id in list)
            {
                if (id.IsNull || id.IsErased) continue;
                var entity = tr.GetObject(id, OpenMode.ForWrite, false, true) as AcEntity;
                if (entity == null) continue;

                var curve = entity as Curve;
                if (curve != null)
                {
                    double worst;
                    var path = ReadPath(curve, elevation, out worst);
                    if (!elevationDifference.HasValue || worst > elevationDifference.Value)
                        elevationDifference = worst;
                    if (path != null)
                    {
                        var clean = path.WithoutRepeats(0.0);
                        if (clean.IsDrawable) paths.Add(clean);
                    }
                }
                entity.Erase();
            }
            return paths;
        }

        /// <summary>
        /// A curve's plan geometry, exactly: a lightweight polyline vertex for vertex with its
        /// bulges; any other polyline by its vertices. <paramref name="worst"/> is the largest
        /// difference between the curve's own elevation and <paramref name="elevation"/>.
        /// </summary>
        private static ContourPath ReadPath(Curve curve, double elevation, out double worst)
        {
            worst = 0;
            var pl = curve as Polyline;
            if (pl != null && Math.Abs(pl.Normal.Z - 1.0) < 1e-12)
            {
                var vertices = new List<ContourVertex>();
                for (var i = 0; i < pl.NumberOfVertices; i++)
                {
                    var p = pl.GetPoint2dAt(i);
                    vertices.Add(new ContourVertex(p.X, p.Y, pl.GetBulgeAt(i)));
                }
                worst = Math.Abs(pl.Elevation - elevation);
                return new ContourPath(vertices, pl.Closed);
            }

            // Polyline2d/3d (or a tilted polyline): vertices sit at whole-number parameters.
            var points = new List<ContourVertex>();
            var last = (int)Math.Round(curve.EndParam);
            var first = (int)Math.Round(curve.StartParam);
            if (curve.Closed && last > first) last--;   // the closing vertex is the first again
            for (var k = first; k <= last; k++)
            {
                var p = curve.GetPointAtParameter(k);
                points.Add(new ContourVertex(p.X, p.Y));
                worst = Math.Max(worst, Math.Abs(p.Z - elevation));
            }
            return new ContourPath(points, curve.Closed);
        }

        private static void DrawPiece(Database db, Transaction tr, ContourPath piece, ContourElevation elevation,
                                      string key, ObjectId layerId, string rulesVersion)
        {
            var pl = new Polyline(piece.Vertices.Count);
            pl.SetDatabaseDefaults(db);
            for (var i = 0; i < piece.Vertices.Count; i++)
            {
                var v = piece.Vertices[i];
                pl.AddVertexAt(i, new Point2d(v.X, v.Y), v.Bulge, 0.0, 0.0);
            }
            pl.Closed = piece.Closed;
            pl.Elevation = elevation.Value;       // exactly the number typed, never rounded
            pl.LayerId = layerId;
            pl.ColorIndex = 256;                  // the contour layer decides how it looks
            pl.Linetype = "ByLayer";
            pl.LineWeight = LineWeight.ByLayer;
            CadUtil.AddToModelSpace(db, tr, pl);
            Ownership.Stamp(pl, key, rulesVersion, FtfEntityKind.Contour, null, elevation.Text);
        }

        private static int Label(Database db, Transaction tr, ContourPath piece, ContourElevation elevation,
                                 string key, ObjectId layerId, FtfSettings settings, string rulesVersion)
        {
            var cs = settings.Contours;
            var scale = CadUtil.DrawingUnitsPerPlottedUnit(db);
            var textHeight = cs.LabelTextPlotted * scale;
            var content = cs.LabelText(elevation.Value, elevation.Text);
            var estimatedWidth = textHeight * (0.8 * content.Length + 0.5);
            var styleId = Setup.DrawingResources.FindTextStyle(db, tr, settings.LineLabels.TextStyle);

            var placed = 0;
            foreach (var spot in ContourLabelPlanner.Plan(piece, estimatedWidth,
                                                          cs.LabelSpacingPlotted * scale, ChordTolerance))
            {
                var at = new Point3d(spot.At.X, spot.At.Y, 0.0);
                var text = new MText();
                text.SetDatabaseDefaults(db);
                if (!styleId.IsNull) text.TextStyleId = styleId;
                text.Contents = content;
                text.TextHeight = textHeight;
                text.Attachment = AttachmentPoint.MiddleCenter;
                text.Location = at;
                text.Rotation = spot.Rotation;
                text.LayerId = layerId;
                CadUtil.AddToModelSpace(db, tr, text);
                Ownership.Stamp(text, key, rulesVersion, FtfEntityKind.ContourLabel, at, elevation.Text);
                placed++;

                if (!cs.MaskLabels) continue;
                var maskId = TextMask.Place(db, tr, text, at, spot.Rotation, textHeight, layerId);
                if (maskId.IsNull) continue;
                var mask = (AcEntity)tr.GetObject(maskId, OpenMode.ForWrite);
                Ownership.Stamp(mask, key, rulesVersion, FtfEntityKind.ContourMask, at, elevation.Text);
            }
            return placed;
        }

        /// <summary>
        /// Reads the surface elevation back at a few vertices of each piece and records the
        /// largest difference from the contour elevation. FindElevationAtXY only reads.
        /// </summary>
        private static void CheckAgainstSurface(TinSurface surface, IList<ContourPath> pieces, double elevation,
                                                ContourElevationResult result)
        {
            const int PerPiece = 4;
            const int PerElevation = 80;
            foreach (var piece in pieces)
            {
                var n = piece.Vertices.Count;
                var picks = new SortedSet<int> { 0, n / 3, (2 * n) / 3, n - 1 };
                foreach (var i in picks.Take(PerPiece))
                {
                    if (result.Checked >= PerElevation) return;
                    if (piece.Vertices[i].Bulge != 0.0 || (i > 0 && piece.Vertices[i - 1].Bulge != 0.0))
                        continue;   // arcs come only from smoothing; check straight-segment vertices
                    double z;
                    try
                    {
                        z = surface.FindElevationAtXY(piece.Vertices[i].X, piece.Vertices[i].Y);
                    }
                    catch (System.Exception)
                    {
                        continue;   // on the very edge of a hidden area: nothing to read there
                    }
                    var deviation = Math.Abs(z - elevation);
                    result.Checked++;
                    if (!result.WorstDeviation.HasValue || deviation > result.WorstDeviation.Value)
                        result.WorstDeviation = deviation;
                }
            }
        }

        // ============================================================ replacing earlier runs

        private sealed class Earlier
        {
            public ObjectId Id;
            public FtfStamp Stamp;
        }

        /// <summary>FTF contours, labels and masks already drawn from this surface, by key.</summary>
        private static Dictionary<string, List<Earlier>> EarlierContours(Database db, Transaction tr,
                                                                         string surfaceHandle)
        {
            var prefix = surfaceHandle + "@";
            var byKey = new Dictionary<string, List<Earlier>>(StringComparer.Ordinal);
            foreach (var pair in Ownership.FindOwned(db, tr, s =>
                         (s.Kind == FtfEntityKind.Contour || s.Kind == FtfEntityKind.ContourLabel ||
                          s.Kind == FtfEntityKind.ContourMask) &&
                         s.PointNumber != null && s.PointNumber.StartsWith(prefix, StringComparison.Ordinal)))
            {
                List<Earlier> list;
                if (!byKey.TryGetValue(pair.Value.PointNumber, out list))
                    byKey[pair.Value.PointNumber] = list = new List<Earlier>();
                list.Add(new Earlier { Id = pair.Key, Stamp = pair.Value });
            }
            return byKey;
        }

        /// <summary>
        /// Removes this command's earlier work at the same surface and elevation. Without a
        /// boundary all of it goes. With one, only what lies inside goes: an earlier contour
        /// that runs out of the boundary is trimmed back to its outside part, so the new and
        /// old contours meet at the boundary without overlapping. Returns the earlier contour
        /// pieces removed.
        /// </summary>
        private static int ReplaceEarlier(Database db, Transaction tr, Dictionary<string, List<Earlier>> earlier,
                                          string key, ContourBoundary boundary, string rulesVersion,
                                          ContourElevationResult result)
        {
            List<Earlier> list;
            if (!earlier.TryGetValue(key, out list)) return 0;
            earlier.Remove(key);

            var removed = 0;
            foreach (var item in list)
            {
                if (item.Id.IsErased) continue;
                var entity = tr.GetObject(item.Id, OpenMode.ForRead, false, true) as AcEntity;
                if (entity == null) continue;

                if (boundary == null)
                {
                    entity.UpgradeOpen();
                    entity.Erase();
                    if (item.Stamp.Kind == FtfEntityKind.Contour) removed++;
                    continue;
                }

                if (item.Stamp.Kind != FtfEntityKind.Contour)
                {
                    var where = item.Stamp.ComputedPosition ?? CadUtil.PositionOf(entity);
                    if (boundary.Contains(new P2(where.X, where.Y)))
                    {
                        entity.UpgradeOpen();
                        entity.Erase();
                    }
                    continue;
                }

                var curve = entity as Curve;
                if (curve == null) continue;
                double ignored;
                var path = ReadPath(curve, 0.0, out ignored);
                var outside = ContourClipper.Clip(path, boundary, false, ChordTolerance);
                if (outside.Count == 1 && ReferenceEquals(outside[0], path)) continue;   // wholly outside: keep

                var pl = curve as Polyline;
                var z = pl != null ? pl.Elevation : curve.StartPoint.Z;
                var layerId = entity.LayerId;
                entity.UpgradeOpen();
                entity.Erase();
                if (outside.Count == 0)
                {
                    removed++;
                    continue;
                }

                result.TrimmedToOutside++;
                foreach (var part in outside)
                {
                    var keep = new Polyline(part.Vertices.Count);
                    keep.SetDatabaseDefaults(db);
                    for (var i = 0; i < part.Vertices.Count; i++)
                        keep.AddVertexAt(i, new Point2d(part.Vertices[i].X, part.Vertices[i].Y),
                                         part.Vertices[i].Bulge, 0.0, 0.0);
                    keep.Closed = part.Closed;
                    keep.Elevation = z;
                    keep.LayerId = layerId;
                    keep.ColorIndex = 256;
                    keep.Linetype = "ByLayer";
                    keep.LineWeight = LineWeight.ByLayer;
                    CadUtil.AddToModelSpace(db, tr, keep);
                    Ownership.Stamp(keep, key, rulesVersion, FtfEntityKind.Contour, null, item.Stamp.TagText);
                }
            }
            return removed;
        }

        // ============================================================ the surface, read only

        private static List<SurfaceChoice> TinSurfaces(Database db, Transaction tr)
        {
            var found = new List<SurfaceChoice>();
            ObjectIdCollection ids;
            try
            {
                ids = CivilDocument.GetCivilDocument(db).GetSurfaceIds();
            }
            catch (System.Exception)
            {
                return found;
            }

            foreach (ObjectId id in ids)
            {
                if (id.IsNull || id.IsErased) continue;
                var surface = tr.GetObject(id, OpenMode.ForRead, false, true) as TinSurface;
                if (surface == null) continue;
                found.Add(new SurfaceChoice { Id = id, Name = surface.Name });
            }
            return found.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static ObjectId AskSurface(Editor ed, List<SurfaceChoice> surfaces)
        {
            var fallback = surfaces.FirstOrDefault(s => s.Id == _lastSurface) ??
                           (surfaces.Count == 1 ? surfaces[0] : null);

            while (true)
            {
                var options = new PromptEntityOptions(fallback != null
                    ? "\nSelect a TIN surface or [Name] <" + fallback.Name + ">: "
                    : "\nSelect a TIN surface or [Name]: ", "Name");
                options.SetRejectMessage("\nThat is not a TIN surface.");
                options.AddAllowedClass(typeof(TinSurface), false);
                options.AllowNone = fallback != null;
                var picked = ed.GetEntity(options);

                if (picked.Status == PromptStatus.OK)
                {
                    var match = surfaces.FirstOrDefault(s => s.Id == picked.ObjectId);
                    if (match != null) return match.Id;
                    ed.WriteMessage("\nThat surface is not one FTFCONTOUR can read.");
                    continue;
                }
                if (picked.Status == PromptStatus.None && fallback != null) return fallback.Id;
                if (picked.Status != PromptStatus.Keyword) return ObjectId.Null;

                // By name, for a surface hidden by its style or off screen.
                for (var i = 0; i < surfaces.Count; i++)
                    ed.WriteMessage("\n  {0,3}  {1}", i + 1, surfaces[i].Name);
                var byName = ed.GetString(new PromptStringOptions("\nSurface name or number: ") { AllowSpaces = true });
                if (byName.Status != PromptStatus.OK) return ObjectId.Null;
                var typed = (byName.StringResult ?? string.Empty).Trim();
                int number;
                if (int.TryParse(typed, NumberStyles.Integer, CultureInfo.InvariantCulture, out number) &&
                    number >= 1 && number <= surfaces.Count)
                    return surfaces[number - 1].Id;
                var named = surfaces.FirstOrDefault(s => string.Equals(s.Name, typed, StringComparison.OrdinalIgnoreCase));
                if (named != null) return named.Id;
                ed.WriteMessage("\nNo TIN surface called \"{0}\".", typed);
            }
        }

        private static SurfaceFingerprint Fingerprint(TinSurface surface)
        {
            var f = new SurfaceFingerprint();
            try { f.Name = surface.Name; } catch (System.Exception) { }
            try { f.OutOfDate = surface.IsOutOfDate; } catch (System.Exception) { }
            try { f.Style = surface.StyleId.Handle.ToString(); } catch (System.Exception) { }
            try
            {
                var general = surface.GetGeneralProperties();
                f.Points = (long)general.NumberOfPoints;
                f.MinimumElevation = general.MinimumElevation;
                f.MaximumElevation = general.MaximumElevation;
                f.MeanElevation = general.MeanElevation;
            }
            catch (System.Exception)
            {
                // An empty surface has no statistics (InvalidOperationException); it is simply
                // not compared, and every elevation reports that it found nothing.
            }
            try { f.Triangles = (long)surface.GetTinProperties().NumberOfTriangles; } catch (System.Exception) { }
            return f;
        }

        /// <summary>The surface's visible triangles, for the fallback tracer. Read only.</summary>
        private static List<TinTriangle> VisibleTriangles(TinSurface surface)
        {
            var list = new List<TinTriangle>();
            foreach (Autodesk.Civil.DatabaseServices.TinSurfaceTriangle t in surface.GetTriangles(false))
            {
                var a = t.Vertex1.Location;
                var b = t.Vertex2.Location;
                var c = t.Vertex3.Location;
                list.Add(new TinTriangle(new TinPoint(a.X, a.Y, a.Z), new TinPoint(b.X, b.Y, b.Z),
                                         new TinPoint(c.X, c.Y, c.Z)));
            }
            return list;
        }

        // ============================================================ the boundary

        private static void AskBoundary(Autodesk.AutoCAD.ApplicationServices.Document doc, Editor ed,
                                        ref ContourBoundary boundary, ref string boundaryName)
        {
            var options = new PromptEntityOptions(
                "\nClosed polyline or circle to contour inside, or [None] <keep current>: ", "None");
            options.SetRejectMessage("\nPick a closed polyline or a circle.");
            options.AddAllowedClass(typeof(Polyline), false);
            options.AddAllowedClass(typeof(Polyline2d), false);
            options.AddAllowedClass(typeof(Polyline3d), false);
            options.AddAllowedClass(typeof(Circle), false);
            options.AllowNone = true;
            var picked = ed.GetEntity(options);

            if (picked.Status == PromptStatus.Keyword)
            {
                boundary = null;
                boundaryName = null;
                ed.WriteMessage("\nNo boundary: the whole surface.");
                return;
            }
            if (picked.Status != PromptStatus.OK) return;

            var db = doc.Database;
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var entity = tr.GetObject(picked.ObjectId, OpenMode.ForRead) as AcEntity;
                ContourPath outline = null;
                var circle = entity as Circle;
                if (circle != null)
                {
                    outline = new ContourPath(new[]
                    {
                        new ContourVertex(circle.Center.X - circle.Radius, circle.Center.Y, 1.0),
                        new ContourVertex(circle.Center.X + circle.Radius, circle.Center.Y, 1.0)
                    }, true);
                }
                else
                {
                    var curve = entity as Curve;
                    double ignored;
                    if (curve != null) outline = ReadPath(curve, 0.0, out ignored);
                }

                string problem;
                var limit = ContourBoundary.FromPath(outline, ChordTolerance, out problem);
                tr.Commit();
                if (limit == null)
                {
                    ed.WriteMessage("\nThat cannot be a boundary: {0}.", problem);
                    return;
                }

                boundary = limit;
                boundaryName = (circle != null ? "circle" : "polyline") + " " + picked.ObjectId.Handle;
                ed.WriteMessage("\nBoundary set: contours are clipped to it ({0:0.##} sq {1}).",
                                limit.Area, UnitsWord(db.Insunits).Length > 0 ? UnitsWord(db.Insunits) : "units");
            }
        }

        // ============================================================ helpers

        /// <summary>True when the reply is the keyword or a leading part of it (B, BO, BOUNDARY).</summary>
        private static bool IsKeyword(string typed, string keyword)
        {
            if (typed.Length == 0 || !typed.All(char.IsLetter)) return false;
            return keyword.StartsWith(typed.ToUpperInvariant(), StringComparison.Ordinal);
        }

        private static void EraseLeftovers(Database db, IEnumerable<ObjectId> ids)
        {
            try
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    foreach (var id in ids)
                    {
                        if (id.IsNull || id.IsErased || !id.IsValid) continue;
                        var entity = tr.GetObject(id, OpenMode.ForWrite, false, true) as AcEntity;
                        if (entity != null) entity.Erase();
                    }
                    tr.Commit();
                }
            }
            catch (System.Exception)
            {
                // Best effort: the run has already reported its failure.
            }
        }

        /// <summary>Erases any of Civil 3D's extracted polylines still in the drawing.</summary>
        private static void EraseTemporaries(Transaction tr, IEnumerable<ObjectId> ids)
        {
            foreach (var id in ids)
            {
                if (id.IsNull || id.IsErased || !id.IsValid) continue;
                var entity = tr.GetObject(id, OpenMode.ForWrite, false, true) as AcEntity;
                if (entity != null && !entity.IsErased) entity.Erase();
            }
        }

        private static string UnitsWord(UnitsValue units)
        {
            switch (units)
            {
                case UnitsValue.Feet:
                case UnitsValue.USSurveyFeet: return "ft";
                case UnitsValue.Meters: return "m";
                case UnitsValue.Inches:
                case UnitsValue.USSurveyInch: return "in";
                case UnitsValue.Millimeters: return "mm";
                case UnitsValue.Centimeters: return "cm";
                default: return string.Empty;
            }
        }

        private static string UnitsName(UnitsValue units)
        {
            switch (units)
            {
                case UnitsValue.Feet: return "feet";
                case UnitsValue.USSurveyFeet: return "US survey feet";
                case UnitsValue.Meters: return "meters";
                case UnitsValue.Inches:
                case UnitsValue.USSurveyInch: return "inches";
                case UnitsValue.Millimeters: return "millimeters";
                case UnitsValue.Centimeters: return "centimeters";
                default: return "drawing units";
            }
        }
    }
}
