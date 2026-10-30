// Headless checks for FTFCONTOUR on a TIN surface built for the purpose:
//
// CONTOURSEED     builds FTF-SMOKE: a plane rising east (z = 10 + 0.04x over 100 x 60) with two cone hills at
//                 (30,30) and (70,30); a second surface FTF-SMOKE-FLAT so the surface has to be chosen; a closed
//                 boundary rectangle (20,10)-(80,50); and a drafter's own polyline that must never be touched.
//                 Snapshots the surface: statistics and every triangle's vertices.
// CONTOURVERIFY   checks the stage named:
//                 UNDONE    one U after FTFCONTOUR left no FTF contour behind.
//                 FIRST     12.6, 13.0 and 13.5 drawn; 12.6 in at least two disconnected pieces, one closed round
//                           the west hill and one open; nothing at 99 or 5; every vertex on the surface at its
//                           contour elevation; every polyline's Elevation exactly the number typed.
//                 AGAIN     re-running 12.6 replaced its own contours: the same count, no duplicates.
//                 BOUNDARY  13.0 re-run inside the boundary with labels: no piece straddles the boundary, the new
//                           pieces and every label are inside it, one mask per label.
//                 Every stage also checks that the surface's statistics and triangles are exactly the seed's, that
//                 none of Civil 3D's own extracted polylines was left in the drawing, and that the drafter's
//                 polyline is untouched.
//
// Compiled with the .NET Framework csc (C# 5 only).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AcDb = Autodesk.AutoCAD.DatabaseServices;
using Civil = Autodesk.Civil.DatabaseServices;

[assembly: CommandClass(typeof(FtfContourTest.ContourTestCommands))]

namespace FtfContourTest
{
    public class ContourTestCommands
    {
        private static readonly CultureInfo C = CultureInfo.InvariantCulture;
        private const string AppName = "FTF_FIELDTOFINISH";
        private const short ContourKind = 42;
        private const short LabelKind = 43;
        private const short MaskKind = 44;
        private const double Tolerance = 1e-6;

        private static int _failures;
        private static string _stats;
        private static string _triangles;
        private static string _drafterPolyline;
        private static int _plainCurves;
        private static int _firstAt126;
        private static AcDb.ObjectId _surfaceId;
        private static AcDb.ObjectId _drafterId;

        private static void Check(Editor ed, bool ok, string what)
        {
            if (!ok) _failures++;
            ed.WriteMessage("\n  [{0}] {1}", ok ? "PASS" : "FAIL", what);
        }

        private static double Distance(double x, double y, double cx, double cy)
        {
            return Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
        }

        private static double Z(double x, double y)
        {
            return 10.0 + 0.04 * x
                   + Math.Max(0.0, 3.0 - 0.25 * Distance(x, y, 30, 30))
                   + Math.Max(0.0, 3.0 - 0.25 * Distance(x, y, 70, 30));
        }

        [CommandMethod("CONTOURSEED")]
        public void Seed()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                _surfaceId = Civil.TinSurface.Create(db, "FTF-SMOKE");
                var surface = (Civil.TinSurface)tr.GetObject(_surfaceId, AcDb.OpenMode.ForWrite);
                var points = new Point3dCollection();
                for (var x = 0; x <= 100; x += 2)
                    for (var y = 0; y <= 60; y += 2)
                        points.Add(new Point3d(x, y, Z(x, y)));
                surface.AddVertices(points);
                surface.Rebuild();

                var flatId = Civil.TinSurface.Create(db, "FTF-SMOKE-FLAT");
                var flat = (Civil.TinSurface)tr.GetObject(flatId, AcDb.OpenMode.ForWrite);
                var corners = new Point3dCollection();
                corners.Add(new Point3d(0, 0, 5));
                corners.Add(new Point3d(100, 0, 5));
                corners.Add(new Point3d(100, 60, 6));
                corners.Add(new Point3d(0, 60, 6));
                flat.AddVertices(corners);
                flat.Rebuild();

                var ms = (AcDb.BlockTableRecord)tr.GetObject(AcDb.SymbolUtilityServices.GetBlockModelSpaceId(db),
                                                             AcDb.OpenMode.ForWrite);
                var limit = new AcDb.Polyline();
                limit.AddVertexAt(0, new Point2d(20, 10), 0, 0, 0);
                limit.AddVertexAt(1, new Point2d(80, 10), 0, 0, 0);
                limit.AddVertexAt(2, new Point2d(80, 50), 0, 0, 0);
                limit.AddVertexAt(3, new Point2d(20, 50), 0, 0, 0);
                limit.Closed = true;
                ms.AppendEntity(limit);
                tr.AddNewlyCreatedDBObject(limit, true);

                var drafter = new AcDb.Polyline();
                drafter.AddVertexAt(0, new Point2d(0, -10), 0, 0, 0);
                drafter.AddVertexAt(1, new Point2d(100, -10), 0, 0, 0);
                drafter.Elevation = 12.6;
                ms.AppendEntity(drafter);
                tr.AddNewlyCreatedDBObject(drafter, true);
                _drafterId = drafter.ObjectId;
                tr.Commit();
            }

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var surface = (Civil.TinSurface)tr.GetObject(_surfaceId, AcDb.OpenMode.ForRead);
                _stats = Stats(surface);
                _triangles = Triangles(surface);
                _drafterPolyline = Describe((AcDb.Polyline)tr.GetObject(_drafterId, AcDb.OpenMode.ForRead));
                _plainCurves = PlainCurves(db, tr);
                tr.Commit();
            }
            ed.WriteMessage("\nCONTOURSEED: FTF-SMOKE {0}; {1} unstamped polylines in model space.", _stats, _plainCurves);
        }

        [CommandMethod("CONTOURVERIFY")]
        public void Verify()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;
            var stage = ed.GetString("\nStage: ").StringResult.Trim().ToUpperInvariant();
            var before = _failures;

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var surface = (Civil.TinSurface)tr.GetObject(_surfaceId, AcDb.OpenMode.ForRead);
                Check(ed, Stats(surface) == _stats, "surface statistics identical to the seed (" + Stats(surface) + ")");
                Check(ed, Triangles(surface) == _triangles, "every triangle identical to the seed");
                Check(ed, !surface.IsOutOfDate, "surface not marked out of date");
                Check(ed, PlainCurves(db, tr) == _plainCurves, "no extracted (unstamped) polyline left in the drawing");
                Check(ed, Describe((AcDb.Polyline)tr.GetObject(_drafterId, AcDb.OpenMode.ForRead)) == _drafterPolyline,
                      "the drafter's own polyline untouched");

                var contours = new List<AcDb.Polyline>();
                var labels = new List<AcDb.MText>();
                var masks = 0;
                var ms = (AcDb.BlockTableRecord)tr.GetObject(AcDb.SymbolUtilityServices.GetBlockModelSpaceId(db),
                                                             AcDb.OpenMode.ForRead);
                foreach (AcDb.ObjectId id in ms)
                {
                    var entity = tr.GetObject(id, AcDb.OpenMode.ForRead) as AcDb.Entity;
                    var kind = Kind(entity);
                    if (kind == ContourKind) contours.Add(entity as AcDb.Polyline);
                    else if (kind == LabelKind) labels.Add(entity as AcDb.MText);
                    else if (kind == MaskKind) masks++;
                }

                Check(ed, contours.All(p => p != null), "every FTF contour is a lightweight polyline");
                contours = contours.Where(p => p != null).ToList();
                Check(ed, contours.All(p => p.Layer == "V-TOPO-CONT-E"), "every contour on V-TOPO-CONT-E");
                Check(ed, contours.All(p => p.Elevation == 12.6 || p.Elevation == 13.0 || p.Elevation == 13.5),
                      "every contour's Elevation is exactly a number that was typed");

                var worst = 0.0;
                var read = 0;
                foreach (var pl in contours)
                    for (var i = 0; i < pl.NumberOfVertices; i++)
                    {
                        var p = pl.GetPoint2dAt(i);
                        try
                        {
                            worst = Math.Max(worst, Math.Abs(surface.FindElevationAtXY(p.X, p.Y) - pl.Elevation));
                            read++;
                        }
                        catch (System.Exception) { }
                    }
                Check(ed, worst <= Tolerance, string.Format(C, "{0} contour vertices on the surface at their elevation (worst {1:0.0000000})", read, worst));

                var at126 = contours.Where(p => p.Elevation == 12.6).ToList();
                var at13 = contours.Where(p => p.Elevation == 13.0).ToList();
                var at135 = contours.Where(p => p.Elevation == 13.5).ToList();

                switch (stage)
                {
                    case "UNDONE":
                        Check(ed, contours.Count == 0 && labels.Count == 0 && masks == 0, "one U removed every FTF contour, label and mask");
                        break;

                    case "FIRST":
                        Check(ed, at126.Count >= 2, "12.6 drawn in " + at126.Count + " pieces (two or more: disconnected)");
                        Check(ed, at126.Any(p => p.Closed), "12.6 has a closed loop round the west hill");
                        Check(ed, at126.Any(p => !p.Closed), "12.6 has an open piece across the slope");
                        Check(ed, at13.Count > 0 && at135.Count > 0, "13.0 and 13.5 drawn");
                        Check(ed, labels.Count == 0, "no labels (labels default off)");
                        _firstAt126 = at126.Count;
                        break;

                    case "AGAIN":
                        Check(ed, at126.Count == _firstAt126, "re-running 12.6 replaced its own contours (" + at126.Count + " pieces, was " + _firstAt126 + ")");
                        break;

                    case "BOUNDARY":
                        var inside = 0;
                        var straddling = 0;
                        foreach (var pl in at13)
                        {
                            var anyIn = false;
                            var anyOut = false;
                            for (var i = 0; i < pl.NumberOfVertices; i++)
                            {
                                var p = pl.GetPoint2dAt(i);
                                var strictlyIn = p.X > 20 + Tolerance && p.X < 80 - Tolerance && p.Y > 10 + Tolerance && p.Y < 50 - Tolerance;
                                var strictlyOut = p.X < 20 - Tolerance || p.X > 80 + Tolerance || p.Y < 10 - Tolerance || p.Y > 50 + Tolerance;
                                anyIn |= strictlyIn;
                                anyOut |= strictlyOut;
                            }
                            if (anyIn && anyOut) straddling++;
                            else if (anyIn) inside++;
                        }
                        Check(ed, straddling == 0, "no 13.0 piece straddles the boundary (" + straddling + ")");
                        Check(ed, inside > 0, "13.0 drawn inside the boundary (" + inside + " pieces)");
                        Check(ed, labels.Count > 0, labels.Count + " elevation labels placed");
                        Check(ed, labels.All(t => t.Location.X >= 20 && t.Location.X <= 80 && t.Location.Y >= 10 && t.Location.Y <= 50),
                              "every label inside the boundary");
                        Check(ed, labels.All(t => t.Contents == "13.0"), "labels read 13.0, as typed");
                        Check(ed, masks == labels.Count, "one mask per label");
                        break;

                    default:
                        Check(ed, false, "unknown stage " + stage);
                        break;
                }
                tr.Commit();
            }
            ed.WriteMessage("\nCONTOURVERIFY {0}: {1}\n", stage, _failures == before ? "PASS" : "FAIL");
        }

        private static short Kind(AcDb.Entity entity)
        {
            if (entity == null) return 0;
            using (var rb = entity.GetXDataForApplication(AppName))
            {
                if (rb == null) return 0;
                foreach (AcDb.TypedValue tv in rb)
                    if (tv.TypeCode == (short)AcDb.DxfCode.ExtendedDataInteger16) return Convert.ToInt16(tv.Value);
            }
            return 0;
        }

        /// <summary>Polylines of any kind in model space that FTF does not own.</summary>
        private static int PlainCurves(AcDb.Database db, AcDb.Transaction tr)
        {
            var ms = (AcDb.BlockTableRecord)tr.GetObject(AcDb.SymbolUtilityServices.GetBlockModelSpaceId(db), AcDb.OpenMode.ForRead);
            var count = 0;
            foreach (AcDb.ObjectId id in ms)
            {
                var entity = tr.GetObject(id, AcDb.OpenMode.ForRead) as AcDb.Entity;
                if ((entity is AcDb.Polyline || entity is AcDb.Polyline2d || entity is AcDb.Polyline3d) && Kind(entity) == 0)
                    count++;
            }
            return count;
        }

        private static string Stats(Civil.TinSurface surface)
        {
            var g = surface.GetGeneralProperties();
            return string.Format(C, "{0} points, {1} triangles, z {2:R}..{3:R} mean {4:R}, style {5}",
                g.NumberOfPoints, surface.GetTinProperties().NumberOfTriangles,
                g.MinimumElevation, g.MaximumElevation, g.MeanElevation, surface.StyleId.Handle);
        }

        private static string Triangles(Civil.TinSurface surface)
        {
            var all = new List<string>();
            foreach (Civil.TinSurfaceTriangle t in surface.GetTriangles(true))
            {
                var v = new[] { t.Vertex1.Location, t.Vertex2.Location, t.Vertex3.Location }
                    .Select(p => string.Format(C, "{0:R},{1:R},{2:R}", p.X, p.Y, p.Z))
                    .OrderBy(s => s, StringComparer.Ordinal);
                all.Add(string.Join(" ", v.ToArray()));
            }
            all.Sort(StringComparer.Ordinal);
            return string.Join("|", all.ToArray());
        }

        private static string Describe(AcDb.Polyline pl)
        {
            var parts = new List<string>();
            for (var i = 0; i < pl.NumberOfVertices; i++)
                parts.Add(string.Format(C, "{0:R},{1:R}", pl.GetPoint2dAt(i).X, pl.GetPoint2dAt(i).Y));
            return string.Format(C, "{0} z{1:R} {2}", pl.Layer, pl.Elevation, string.Join(" ", parts.ToArray()));
        }
    }
}
