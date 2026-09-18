// Live-smoke harness for the production tools: Storm/Sewer Dip Builder and
// Strip Easement Maker. Runs inside accoreconsole with the real plugin loaded.
//
// PRODSEED    layers, structure COGO points, a hand-drawn pipe, easement geometry.
// PRODMOVE    simulates a survey revision: moves a structure point, changes its
//             rim, and edits the easement route.
// PRODVERIFY  dumps what is really in the drawing: FTF-owned entities by kind,
//             the dip project and easement records stored in the drawing, and
//             checks the field observations are exactly what the notes said.
//
// Compiled with the .NET Framework csc (C# 5 only).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AcDb = Autodesk.AutoCAD.DatabaseServices;
using CivApp = Autodesk.Civil.ApplicationServices;
using CivDb = Autodesk.Civil.DatabaseServices;

[assembly: CommandClass(typeof(FtfProdTest.ProdTestCommands))]

namespace FtfProdTest
{
    public class ProdTestCommands
    {
        private const string FtfApp = "FTF_FIELDTOFINISH";

        static ProdTestCommands()
        {
            // FieldCodes.dll is loaded by the plugin from its own folder; point this
            // assembly's references at that copy.
            AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
            {
                var name = new AssemblyName(e.Name).Name;
                var loaded = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == name);
                if (loaded != null) return loaded;
                var plugin = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "FieldCodes.Cad");
                if (plugin == null) return null;
                var path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(plugin.Location), name + ".dll");
                return System.IO.File.Exists(path) ? Assembly.LoadFrom(path) : null;
            };
        }

        private static int _failures;

        private static void Check(Editor ed, bool ok, string what)
        {
            if (!ok) _failures++;
            ed.WriteMessage("\n  [{0}] {1}", ok ? "PASS" : "FAIL", what);
        }

        // ------------------------------------------------------------------ seed

        [CommandMethod("PRODSEED")]
        public void Seed()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            AcDb.Database db = doc.Database;
            CivApp.CivilDocument cdoc = CivApp.CivilApplication.ActiveDocument;

            using (AcDb.Transaction tr = db.TransactionManager.StartTransaction())
            {
                AcDb.LayerTable lt = (AcDb.LayerTable)tr.GetObject(db.LayerTableId, AcDb.OpenMode.ForWrite);
                foreach (string name in new[] { "V-UTIL-STRM-E", "V-UTIL-STRM-TEXT-E", "V-UTIL-SSWR-E", "V-UTIL-SSWR-TEXT-E", "V-PROP-LINE-E", "V-ESMT-E" })
                {
                    if (lt.Has(name)) continue;
                    AcDb.LayerTableRecord ltr = new AcDb.LayerTableRecord();
                    ltr.Name = name;
                    lt.Add(ltr);
                    tr.AddNewlyCreatedDBObject(ltr, true);
                }
                tr.Commit();
            }

            // number, E, N, elevation, raw description
            object[][] pts = new object[][]
            {
                new object[] { 1045u, 5000.0, 5000.0, 328.42, "SDMH" },
                new object[] { 1046u, 5000.0, 5180.0, 330.10, "CB" },
                new object[] { 1047u, 4850.0, 4850.0, 326.00, "SDMH" },
                new object[] { 1048u, 5120.0, 5000.0, 327.90, "CB" },
                new object[] { 2000u, 5060.0, 5100.0, 329.50, "SSMH" },
                new object[] { 2001u, 5900.0, 4900.0, 331.00, "IPF" },
                new object[] { 3001u, 7000.0, 5000.0, 330.00, "CP" },
                new object[] { 3002u, 7200.0, 5000.0, 330.00, "CP" },
                new object[] { 3003u, 7400.0, 5040.0, 330.00, "CP" }
            };

            using (AcDb.Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (object[] p in pts)
                {
                    AcDb.ObjectId id = cdoc.CogoPoints.Add(new Point3d((double)p[1], (double)p[2], (double)p[3]), (string)p[4], false);
                    CivDb.CogoPoint cp = (CivDb.CogoPoint)tr.GetObject(id, AcDb.OpenMode.ForWrite);
                    try { cp.PointNumber = (uint)p[0]; }
                    catch (System.Exception ex) { ed.WriteMessage("\nPRODSEED: point number {0}: {1}", p[0], ex.Message); }
                }

                AcDb.BlockTableRecord ms = (AcDb.BlockTableRecord)tr.GetObject(
                    AcDb.SymbolUtilityServices.GetBlockModelSpaceId(db), AcDb.OpenMode.ForWrite);

                // A pipe somebody already drew by hand between 1045 and 1047.
                AcDb.Line hand = new AcDb.Line(new Point3d(5000, 5000, 0), new Point3d(4850, 4850, 0));
                hand.Layer = "V-UTIL-STRM-E";
                ms.AppendEntity(hand);
                tr.AddNewlyCreatedDBObject(hand, true);

                // Easement 1: tangent line, 90 degree curve (R=100), tangent line.
                AcDb.Polyline route = new AcDb.Polyline();
                route.AddVertexAt(0, new Point2d(6000, 5000), 0, 0, 0);
                route.AddVertexAt(1, new Point2d(6200, 5000), Math.Tan(Math.PI / 8), 0, 0);
                route.AddVertexAt(2, new Point2d(6300, 5100), 0, 0, 0);
                route.AddVertexAt(3, new Point2d(6300, 5250), 0, 0, 0);
                route.Layer = "V-PROP-LINE-E";
                ms.AppendEntity(route);
                tr.AddNewlyCreatedDBObject(route, true);

                // A property line crossing the route's start on a skew.
                AcDb.Line property = new AcDb.Line(new Point3d(6010, 4950, 0), new Point3d(5990, 5050, 0));
                property.Layer = "V-PROP-LINE-E";
                ms.AppendEntity(property);
                tr.AddNewlyCreatedDBObject(property, true);

                // Easement 2's parent parcel.
                AcDb.Polyline parcel = new AcDb.Polyline();
                parcel.AddVertexAt(0, new Point2d(6950, 4950), 0, 0, 0);
                parcel.AddVertexAt(1, new Point2d(7300, 4950), 0, 0, 0);
                parcel.AddVertexAt(2, new Point2d(7300, 5150), 0, 0, 0);
                parcel.AddVertexAt(3, new Point2d(6950, 5150), 0, 0, 0);
                parcel.Closed = true;
                parcel.Layer = "V-PROP-LINE-E";
                ms.AppendEntity(parcel);
                tr.AddNewlyCreatedDBObject(parcel, true);

                // An existing building inside the construction area, excluded as a hole.
                AcDb.Polyline building = new AcDb.Polyline();
                building.AddVertexAt(0, new Point2d(7200, 5115), 0, 0, 0);
                building.AddVertexAt(1, new Point2d(7220, 5115), 0, 0, 0);
                building.AddVertexAt(2, new Point2d(7220, 5135), 0, 0, 0);
                building.AddVertexAt(3, new Point2d(7200, 5135), 0, 0, 0);
                building.Closed = true;
                building.Layer = "V-PROP-LINE-E";
                ms.AppendEntity(building);
                tr.AddNewlyCreatedDBObject(building, true);

                tr.Commit();
            }
            ed.WriteMessage("\nPRODSEED: done.\n");
        }

        // ------------------------------------------------------------------ move

        [CommandMethod("PRODMOVE")]
        public void Move()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            AcDb.Database db = doc.Database;
            using (AcDb.Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (AcDb.ObjectId id in CivApp.CivilApplication.ActiveDocument.CogoPoints)
                {
                    CivDb.CogoPoint cp = (CivDb.CogoPoint)tr.GetObject(id, AcDb.OpenMode.ForWrite);
                    if (cp.PointNumber != 1046) continue;
                    cp.Easting = cp.Easting + 0.5;
                    cp.Elevation = cp.Elevation + 0.10;
                }

                AcDb.BlockTableRecord ms = (AcDb.BlockTableRecord)tr.GetObject(
                    AcDb.SymbolUtilityServices.GetBlockModelSpaceId(db), AcDb.OpenMode.ForRead);
                foreach (AcDb.ObjectId id in ms)
                {
                    AcDb.Polyline pl = tr.GetObject(id, AcDb.OpenMode.ForRead) as AcDb.Polyline;
                    if (pl != null && pl.Closed && pl.NumberOfVertices == 4 && pl.GetPoint2dAt(0).GetDistanceTo(new Point2d(7200, 5115)) < 0.001)
                    {
                        // The building grows: its north-east corner moves 20' east (400 -> 600 sq ft).
                        pl.UpgradeOpen();
                        pl.SetPointAt(2, new Point2d(7240, 5135));
                        continue;
                    }
                    if (pl == null || pl.Layer != "V-PROP-LINE-E" || pl.Closed || pl.NumberOfVertices != 4) continue;
                    pl.UpgradeOpen();
                    pl.SetPointAt(3, new Point2d(6300, 5270));
                }
                tr.Commit();
            }
            doc.Editor.WriteMessage("\nPRODMOVE: 1046 moved 0.5' east and raised 0.10'; easement route extended 20'.\n");
        }

        // ---------------------------------------------------------------- verify

        [CommandMethod("PRODVERIFY", CommandFlags.NoUndoMarker)]
        public void Verify()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            _failures = 0;
            VerifyCore(doc.Database, ed);
            ed.WriteMessage("\nPRODVERIFY: {0} failure(s).\n", _failures);
        }

        private static void VerifyCore(AcDb.Database db, Editor ed)
        {
            var kinds = new SortedDictionary<int, int>();
            var layers = new SortedDictionary<string, int>();
            var texts = new List<string>();
            int handPipes = 0;
            hatchAreas.Clear();

            using (AcDb.Transaction tr = db.TransactionManager.StartTransaction())
            {
                AcDb.BlockTableRecord ms = (AcDb.BlockTableRecord)tr.GetObject(
                    AcDb.SymbolUtilityServices.GetBlockModelSpaceId(db), AcDb.OpenMode.ForRead);
                foreach (AcDb.ObjectId id in ms)
                {
                    AcDb.Entity e = tr.GetObject(id, AcDb.OpenMode.ForRead) as AcDb.Entity;
                    if (e == null) continue;
                    int kind = KindOf(e);
                    if (kind < 0)
                    {
                        if (e is AcDb.Line && e.Layer == "V-UTIL-STRM-E") handPipes++;
                        continue;
                    }
                    if (kind < 19) continue;
                    int n;
                    kinds.TryGetValue(kind, out n);
                    kinds[kind] = n + 1;
                    layers.TryGetValue(e.Layer, out n);
                    layers[e.Layer] = n + 1;

                    AcDb.MText mt = e as AcDb.MText;
                    if (mt != null) texts.Add(KindName(kind) + ": " + mt.Contents);
                    AcDb.MLeader ml = e as AcDb.MLeader;
                    if (ml != null && ml.MText != null) texts.Add(KindName(kind) + ": " + ml.MText.Contents);
                    AcDb.Table tb = e as AcDb.Table;
                    if (tb != null) texts.Add(KindName(kind) + ": " + tb.Rows.Count + " rows, title \"" + tb.Cells[0, 0].TextString + "\", first row \"" + (tb.Rows.Count > 2 ? tb.Cells[2, 0].TextString + " | " + tb.Cells[2, 1].TextString + " | " + tb.Cells[2, 2].TextString : "") + "\"");
                    AcDb.AlignedDimension dim = e as AcDb.AlignedDimension;
                    if (dim != null) texts.Add(KindName(kind) + ": measures " + dim.Measurement.ToString("0.000", CultureInfo.InvariantCulture));
                    AcDb.Polyline pl = e as AcDb.Polyline;
                    if (pl != null) texts.Add(KindName(kind) + ": " + (pl.Closed ? "closed" : "open") + " polyline, " + pl.NumberOfVertices + " vertices, area " + (pl.Closed ? pl.Area.ToString("0.00", CultureInfo.InvariantCulture) : "-"));
                    AcDb.Hatch h = e as AcDb.Hatch;
                    if (h != null) { texts.Add(KindName(kind) + ": " + h.PatternName + " area " + h.Area.ToString("0.00", CultureInfo.InvariantCulture)); hatchAreas.Add(h.Area); }
                }

                ed.WriteMessage("\n-- FTF production entities by kind");
                foreach (var kv in kinds) ed.WriteMessage("\n   {0,-20} {1}", KindName(kv.Key), kv.Value);
                ed.WriteMessage("\n-- by layer");
                foreach (var kv in layers) ed.WriteMessage("\n   {0,-24} {1}", kv.Key, kv.Value);
                ed.WriteMessage("\n-- unowned lines on V-UTIL-STRM-E: {0}", handPipes);
                ed.WriteMessage("\n-- entity details");
                foreach (string t in texts) ed.WriteMessage("\n   " + t.Replace("{", "{{").Replace("}", "}}"));

                string dips = ReadText(tr, db, "DIPS");
                var easements = ReadEasements(tr, db);
                tr.Commit();

                ed.WriteMessage("\n-- stored dip project: {0} characters", dips == null ? 0 : dips.Length);
                if (dips != null) VerifyDips(ed, dips);
                ed.WriteMessage("\n-- stored easements: {0}", easements.Count);
                VerifyEasements(ed, easements);
                using (AcDb.Transaction tr2 = db.TransactionManager.StartTransaction())
                {
                    VerifyExhibits(ed, tr2, db, easements);
                    tr2.Commit();
                }
            }
        }

        private static void VerifyDips(Editor ed, string json)
        {
            FieldCodes.Utilities.UtilityProject p = FieldCodes.Utilities.UtilityProject.FromJson(json);
            ed.WriteMessage("\n   {0} structures, {1} connections, {2} overrides, {3} manual revisit items",
                            p.Structures.Count, p.Connections.Count, p.Overrides.Count, p.ManualRevisit.Count);
            foreach (var s in p.Structures)
            {
                ed.WriteMessage("\n   {0}  rim {1}  BOT {2}  WL {3}", s.Label, s.Cad == null ? "-" : s.Cad.Rim.ToString("0.00", CultureInfo.InvariantCulture),
                                s.Field.BottomDip, s.Field.WaterDip);
                foreach (var pipe in s.Field.Pipes)
                {
                    var elev = FieldCodes.Utilities.DipElevations.Pipe(s, pipe);
                    var c = p.ConnectionFor(s.Id, pipe.Id);
                    ed.WriteMessage("\n     {0}  dip {1} {2}{3} [{4}]  {5}  -> {6}",
                        FieldCodes.Utilities.ConnectionFinder.Describe(pipe), pipe.MeasuredDip, pipe.Reference,
                        " (" + pipe.ReferenceBasis + ")", pipe.Source,
                        elev == null ? "no elevation" : elev.Formula + " = " + elev.Value.ToString("0.00", CultureInfo.InvariantCulture),
                        c == null ? "unresolved" : c.Status + "/" + c.Confidence);
                }
            }

            // The field observations must be exactly what the notes said, whatever
            // was drafted, rebuilt or undone.
            var expected = new Dictionary<string, double?[]>
            {
                { "1045", new double?[] { 6.41, 7.02, 5.73 } },
                { "1046", new double?[] { 7.20 } },
                { "1047", new double?[] { 6.10 } },
                { "2000", new double?[] { 9.00 } }
            };
            foreach (var kv in expected)
            {
                var s = p.StructureByPoint(kv.Key);
                Check(ed, s != null, "structure " + kv.Key + " is in the project");
                if (s == null) continue;
                var dipsNow = s.Field.Pipes.Select(x => x.MeasuredDip).ToArray();
                Check(ed, dipsNow.SequenceEqual(kv.Value), "PT " + kv.Key + " pipe dips unchanged (" + string.Join(", ", dipsNow.Select(d => d.HasValue ? d.Value.ToString("0.00", CultureInfo.InvariantCulture) : "none").ToArray()) + ")");
                Check(ed, s.Field.Pipes.All(x => x.Source == FieldCodes.Utilities.ObservationSource.FieldNote), "PT " + kv.Key + " observations still sourced from field notes");
            }
            var s1045 = p.StructureByPoint("1045");
            if (s1045 != null)
            {
                Check(ed, s1045.Field.BottomDip == 7.82 && s1045.Field.WaterDip == 6.94, "PT 1045 BOT 7.82 / WL 6.94 retained");
                var n = FieldCodes.Utilities.DipElevations.Pipe(s1045, s1045.Field.Pipes[0]);
                Check(ed, n != null && Math.Abs(n.Value - 322.01) < 0.005, "PT 1045 12\" RCP N invert = 328.42 - 6.41 = 322.01");
            }
            if (s1045 != null)
            {
                Check(ed, s1045.Field.Pipes[0].ReferenceBasis == FieldCodes.Utilities.ReferenceBasis.FieldNoteConvention, "PT 1045 12\" RCP N unmarked dip is the invert by the office default");
                Check(ed, s1045.Field.Pipes[2].Reference == FieldCodes.Utilities.MeasurementReference.Invert, "PT 1045 8\" PVC E unmarked dip is the invert by the office default");
            }
            var s2000 = p.StructureByPoint("2000");
            if (s2000 != null)
            {
                var top = s2000.Field.Pipes[0];
                Check(ed, top.Reference == FieldCodes.Utilities.MeasurementReference.TopOfPipe, "PT 2000 TOP dip kept as top of pipe, not converted to invert");
            }
        }

        private static readonly List<double> hatchAreas = new List<double>();

        private static void VerifyEasements(Editor ed, IList<FieldCodes.Easements.EasementRecord> records)
        {
            var utility = records.FirstOrDefault(r => r.Title.Contains("UTILITY") && !r.IsTemporary);
            var utilityTemp = utility == null ? null : records.FirstOrDefault(r => r.IsTemporary && r.GroupId == utility.GroupId);
            if (utility != null)
            {
                Check(ed, utilityTemp != null && utility.GroupId != null, "utility easement has its temporary construction easement in the same group");
                if (utilityTemp != null)
                {
                    Check(ed, utilityTemp.Title.Contains("30.00' WIDE TEMPORARY CONSTRUCTION"), "temporary easement title: " + utilityTemp.Title);
                    Check(ed, Math.Abs(utilityTemp.AreaSquareFeet - 15212.3890) < 0.01 || Math.Abs(utilityTemp.AreaSquareFeet - 15812.3890) < 0.01,
                          "temporary easement is 30 x route length (" + utilityTemp.AreaSquareFeet.ToString("0.0000", CultureInfo.InvariantCulture) + ")");
                }
                Check(ed, utility.PointOfCommencement != null && utility.PointOfCommencement.Source == FieldCodes.Easements.LocationSource.CogoPoint &&
                          utility.CommencementTie != null && Math.Abs(utility.CommencementTie.Length - 141.4214) < 0.001,
                      "Point of Commencement is COGO point 2001, tied 141.42' to the Point of Beginning");
                Check(ed, utility.BeginsOn != null, "utility easement's Point of Beginning is on the skewed property line");
                if (utility.Legal != null)
                {
                    Check(ed, utility.Legal.County == "SNOHOMISH" && utility.Legal.BeginningLine == "THE WEST LINE OF SAID LOT 1",
                          "legal description names are kept with the easement");
                    Check(ed, utility.LegalStatus.StartsWith("DRAFT", StringComparison.Ordinal), "legal status says draft: " + utility.LegalStatus);
                    var draft = FieldCodes.Easements.LegalDescriptionWriter.Write(utility, utilityTemp, utility.Legal, new FieldCodes.Settings.EasementSettings(), null);
                    Check(ed, draft.Text.Contains("TOGETHER WITH A 30.00 FOOT WIDE TEMPORARY CONSTRUCTION EASEMENT") &&
                              draft.Text.Contains("THENCE DEPARTING SAID WEST LINE, NORTH 90°00'00\" EAST 200.00 FEET;") &&
                              draft.Text.Contains("THENCE ALONG A CURVE TO THE LEFT, HAVING A RADIUS OF 100.00 FEET"),
                          "draft legal description reads from the stored easement");
                }
            }
            if (records.Count > 0) Check(ed, utility != null, "utility easement stored");
            if (utility != null)
            {
                Check(ed, utility.TruePointOfBeginning.Source == FieldCodes.Easements.LocationSource.GeometryEndpoint,
                      "utility easement starts at the route polyline's end, on the skewed property line");
                Check(ed, utility.TrimLines != null && utility.TrimLines.Count == 1 && utility.KeepPoints != null && utility.KeepPoints.Count == 1,
                      "utility easement remembers its trim line and the kept piece");
                Check(ed, Math.Abs(utility.AreaSquareFeet - 10141.5927) < 0.01 || Math.Abs(utility.AreaSquareFeet - 10541.5927) < 0.01,
                      "utility easement trimmed to the skewed line keeps 20 x route length (" + utility.AreaSquareFeet.ToString("0.0000", CultureInfo.InvariantCulture) + ")");
                Check(ed, utility.BoundaryCourses.Any(c => c.Course.Kind == FieldCodes.Easements.CourseKind.Line &&
                          Math.Abs((c.Course.Start.X - 6000) + 0.2 * (c.Course.Start.Y - 5000)) < 0.001 &&
                          Math.Abs((c.Course.End.X - 6000) + 0.2 * (c.Course.End.Y - 5000)) < 0.001),
                      "utility easement's beginning runs along the skewed property line");
            }
            var access = records.FirstOrDefault(r => r.Title.Contains("ACCESS") && !r.IsTemporary);
            if (records.Count > 0) Check(ed, access != null, "access easement stored");
            if (access != null)
            {
                Check(ed, Math.Abs(access.AreaSquareFeet - 20335.8084) < 0.01,
                      "access easement trimmed lengthwise and across by the parcel: area " + access.AreaSquareFeet.ToString("0.0000", CultureInfo.InvariantCulture) + " (expected 20335.8084)");
                Check(ed, access.BoundaryCourses.All(c => c.Course.Start.X <= 7300.001 && c.Course.Start.Y >= 4949.999),
                      "access easement lies inside the parcel's east and south lines");
                Check(ed, access.AnglePoints != null && access.AnglePoints.Count == 3, "access easement keeps its three angle points");
                Check(ed, access.EndsOn != null && access.TerminusTie != null && Math.Abs(access.TerminusTie.Length - 130.0) < 0.001 &&
                          Math.Abs(access.TerminusTie.AzimuthDegrees) < 1e-6,
                      "access terminus is on the parcel's east line, and the corner bears north 130.00'");
                var accessTemp = records.FirstOrDefault(r => r.IsTemporary && r.GroupId == access.GroupId);
                Check(ed, accessTemp != null && Math.Abs(accessTemp.AreaSquareFeet - 22005.5388) < 0.01,
                      "access temporary easement (20' left, 65' right) trimmed by the parcel: " + (accessTemp == null ? "missing" : accessTemp.AreaSquareFeet.ToString("0.0000", CultureInfo.InvariantCulture)) + " (expected 22005.5388)");
            }
            var portion = records.FirstOrDefault(r => r.IsPortion);
            if (records.Count > 0) Check(ed, portion != null, "portion easement stored");
            if (portion != null)
            {
                var portionA = portion.HasComposition ? (portion.PrimaryAreaSquareFeet ?? 0) + (portion.Exclusions ?? new List<FieldCodes.Easements.Exclusion>()).Sum(x => x.RemovedSquareFeet) : portion.AreaSquareFeet;
                Check(ed, Math.Abs(portionA - 500) < 0.01, "the west 10 feet of the south 50 feet is 500 sq ft before exclusions (" + portionA.ToString("0.0000", CultureInfo.InvariantCulture) + ")");
                Check(ed, portion.BoundaryCourses.All(c => c.Course.Start.X <= 6960.001 && c.Course.Start.Y <= 5000.001),
                      "portion lies within 10' of the west line and 50' of the south line");
                Check(ed, FieldCodes.Easements.PortionBuilder.Describe(portion.PortionSteps, 2) == "THE WEST 10.00 FEET OF THE SOUTH 50.00 FEET OF",
                      "portion calls read in order: " + FieldCodes.Easements.PortionBuilder.Describe(portion.PortionSteps, 2));
            }
            if (portion != null && portion.Components != null && portion.Components.Count > 0)
            {
                var b = portion.Components[0];
                Check(ed, b.Label == "B" && b.Connector == "TOGETHER WITH" && Math.Abs(b.AreaSquareFeet - 5250) < 0.01,
                      "component B (the south 15 feet of the lot) is 5,250 sq ft, joined TOGETHER WITH (" + b.AreaSquareFeet.ToString("0.00", CultureInfo.InvariantCulture) + ")");
                Check(ed, portion.Warnings.Any(w => w.Contains("overlap by 150")), "components A and B overlapping by 150 sq ft is reported, not hidden");
                if (portion.Exclusions != null && portion.Exclusions.Count > 0)
                {
                    var x = portion.Exclusions[0];
                    Check(ed, x.Effect == "CUT" && Math.Abs(x.RemovedSquareFeet - 200) < 0.01 && Math.Abs((portion.PrimaryAreaSquareFeet ?? 0) - 300) < 0.01,
                          "except the north 20 feet thereof removes 200 sq ft from component A, leaving 300 (" + x.Effect + ", " + x.RemovedSquareFeet.ToString("0.00", CultureInfo.InvariantCulture) + ")");
                    Check(ed, Math.Abs(portion.AreaSquareFeet - 5550) < 0.01, "total easement area after the exclusion is 300 + 5,250 = 5,550 sq ft (" + portion.AreaSquareFeet.ToString("0.00", CultureInfo.InvariantCulture) + ")");
                    var draft = FieldCodes.Easements.LegalDescriptionWriter.WritePortion(portion, portion.Legal, new FieldCodes.Settings.EasementSettings(), null);
                    Check(ed, draft.Text.Contains("EXCEPT THE NORTH 20.00 FEET THEREOF.") && draft.Text.Contains("TOGETHER WITH") && draft.Text.Contains("THE SOUTH 15.00 FEET OF"),
                          "portion legal draft carries the exception and component B");
                }
            }
            var area = records.FirstOrDefault(r => r.IsArea);
            if (area != null && area.Exclusions != null && area.Exclusions.Count > 0)
            {
                var hole = area.Exclusions[0];
                Check(ed, hole.Effect == "HOLE" && area.Holes != null && area.Holes.Count == 1, "the building inside the construction area is a hole");
                Check(ed, Math.Abs(area.AreaSquareFeet - 7100) < 0.01 || Math.Abs(area.AreaSquareFeet - 6900) < 0.01,
                      "construction area less the building: 7,500 - 400 = 7,100 (6,900 after the building grows) (" + area.AreaSquareFeet.ToString("0.00", CultureInfo.InvariantCulture) + ")");
                Check(ed, hatchAreas.Any(a => Math.Abs(a - area.AreaSquareFeet) < 0.01), "the construction area hatch leaves the building open (hatch area matches the easement area)");
                var closure = FieldCodes.Easements.Closure.ForRecord(area, new FieldCodes.Settings.EasementSettings());
                Check(ed, closure.Ready, "construction area closure: CAD boundary, hole and stated courses all check (" + string.Join("; ", closure.Problems.ToArray()) + ")");
                if (area.Legal != null)
                {
                    var draft = FieldCodes.Easements.LegalDescriptionWriter.WriteArea(area, area.Legal, new FieldCodes.Settings.EasementSettings(), null);
                    Check(ed, draft.Text.Contains("EXCEPT THAT PORTION THEREOF LYING WITHIN THE EXISTING BUILDING FOOTPRINT."), "construction area legal draft names the exclusion");
                }
            }
            if (utility != null && area != null && area.ExhibitGroup != null)
                Check(ed, area.ExhibitGroup == "UTILITY EXHIBIT" && utility.ExhibitGroup == "UTILITY EXHIBIT", "utility easement and construction area share an exhibit group");
            if (records.Count > 0) Check(ed, area != null, "construction area stored");
            if (area != null)
            {
                var outerArea = Math.Abs(FieldCodes.Easements.Loops.SignedArea(area.BoundaryCourses.Select(d => d.Course).ToList()));
                Check(ed, Math.Abs(outerArea - 7500) < 0.01 && area.RouteCourses.Count == 4,
                      "construction area following the parcel's north and east lines encloses 7,500 sq ft in 4 courses (" + outerArea.ToString("0.00", CultureInfo.InvariantCulture) + ", " + area.RouteCourses.Count + ")");
                Check(ed, area.Title == "TEMPORARY CONSTRUCTION AREA" && area.AreaSides.Count(s => s.FollowHandle != null) == 1, "construction area title and followed side recorded");
                if (area.Legal != null)
                {
                    var legal = FieldCodes.Easements.LegalDescriptionWriter.WriteArea(area, area.Legal, new FieldCodes.Settings.EasementSettings(), null);
                    Check(ed, legal.Text.Contains("BEGINNING AT A POINT ON THE NORTH LINE OF SAID LOT 1;") &&
                              legal.Text.Contains("THENCE ALONG THE NORTH AND EAST LINES OF SAID LOT 1, NORTH 90°00'00\" EAST 150.00 FEET;") &&
                              legal.Text.Contains("TO THE POINT OF BEGINNING.") &&
                              legal.Text.Contains("SAID TEMPORARY CONSTRUCTION AREA CONTAINING " + area.AreaSquareFeet.ToString("N0", CultureInfo.InvariantCulture) + " SQUARE FEET"),
                          "construction area draft legal description reads course by course");
                }
            }
            foreach (var r in records.OrderBy(x => x.CreatedUtc))
            {
                ed.WriteMessage("\n   \"{0}\" area {1:0.00} sq ft ({2:0.000} ac), {3} boundary courses, {4} drafted, clipped={5}, POC {6}, TPOB {7}",
                    r.Title, r.AreaSquareFeet, r.Acres, r.BoundaryCourses.Count, r.DraftedHandles.Count, r.ClippedToParcel,
                    r.PointOfCommencement == null ? "none" : r.PointOfCommencement.Source + " " + r.PointOfCommencement.PointNumber,
                    r.TruePointOfBeginning == null ? "none" : r.TruePointOfBeginning.Source + " " + r.TruePointOfBeginning.PointNumber);
                foreach (var src in r.RouteSources) ed.WriteMessage("\n     source {0} {1} {2}", src.Role, src.EntityType, src.Handle);
                foreach (var w in r.Warnings) ed.WriteMessage("\n     note: " + w.Replace("{", "{{").Replace("}", "}}"));
                ed.WriteMessage("\n     legal: " + r.LegalStatus);
            }
        }

        // ------------------------------------------------------------------ XData

        private static int KindOf(AcDb.Entity e)
        {
            AcDb.ResultBuffer rb = e.GetXDataForApplication(FtfApp);
            if (rb == null) return -1;
            using (rb)
            {
                foreach (AcDb.TypedValue tv in rb)
                    if (tv.TypeCode == (short)AcDb.DxfCode.ExtendedDataInteger16) return (short)tv.Value;
            }
            return -1;
        }

        private static string KindName(int kind)
        {
            switch (kind)
            {
                case 19: return "UtilityPipe";
                case 20: return "UtilityPipeLabel";
                case 21: return "StructureLabel";
                case 22: return "EasementBoundary";
                case 23: return "EasementLine";
                case 24: return "EasementHatch";
                case 25: return "EasementDimension";
                case 26: return "EasementText";
                case 27: return "EasementTable";
                case 28: return "ExhibitViewport";
                case 29: return "ExhibitText";
                case 30: return "ExhibitTable";
                case 31: return "ExhibitSymbol";
                case 32: return "ExhibitLabel";
                case 33: return "ExhibitBorder";
                case 34: return "ExhibitDimension";
                default: return "kind " + kind;
            }
        }

        private static AcDb.DBDictionary Root(AcDb.Transaction tr, AcDb.Database db)
        {
            AcDb.DBDictionary nod = (AcDb.DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, AcDb.OpenMode.ForRead);
            if (!nod.Contains(FtfApp)) return null;
            return (AcDb.DBDictionary)tr.GetObject(nod.GetAt(FtfApp), AcDb.OpenMode.ForRead);
        }

        private static string Join(AcDb.Transaction tr, AcDb.DBDictionary dict, string key)
        {
            if (dict == null || !dict.Contains(key)) return null;
            AcDb.Xrecord x = tr.GetObject(dict.GetAt(key), AcDb.OpenMode.ForRead) as AcDb.Xrecord;
            if (x == null || x.Data == null) return null;
            var sb = new StringBuilder();
            foreach (AcDb.TypedValue tv in x.Data.AsArray())
                if (tv.TypeCode == (short)AcDb.DxfCode.Text) sb.Append((string)tv.Value);
            return sb.ToString();
        }

        private static string ReadText(AcDb.Transaction tr, AcDb.Database db, string key)
        {
            return Join(tr, Root(tr, db), key);
        }

        private static IList<FieldCodes.Exhibits.ExhibitRecord> ReadExhibits(AcDb.Transaction tr, AcDb.Database db)
        {
            var list = new List<FieldCodes.Exhibits.ExhibitRecord>();
            AcDb.DBDictionary root = Root(tr, db);
            if (root == null || !root.Contains("EXHIBITS")) return list;
            AcDb.DBDictionary dict = (AcDb.DBDictionary)tr.GetObject(root.GetAt("EXHIBITS"), AcDb.OpenMode.ForRead);
            foreach (AcDb.DBDictionaryEntry entry in dict)
                list.Add(FieldCodes.Exhibits.ExhibitRecord.FromJson(Join(tr, dict, entry.Key)));
            return list;
        }

        // ------------------------------------------------------------ exhibits

        private static string _expect = "NONE";
        private static double _movedTableX, _movedTableY;
        private static string _editedLabelKey;

        [CommandMethod("PRODEXPECT", CommandFlags.NoUndoMarker)]
        public void Expect()
        {
            var r = Application.DocumentManager.MdiActiveDocument.Editor.GetString("\nExpect: ");
            if (r.Status == PromptStatus.OK) _expect = r.StringResult.Trim().ToUpperInvariant();
        }

        /// <summary>A second exhibit profile with a wide, short viewport: a long north-south easement reads better turned.</summary>
        [CommandMethod("PRODPROFILE", CommandFlags.NoUndoMarker)]
        public void WideProfile()
        {
            var s = new FieldCodes.Settings.FtfSettings();
            s.Exhibits.ViewportLeftIn = 0.75;
            s.Exhibits.ViewportBottomIn = 4.0;
            s.Exhibits.ViewportWidthIn = 7.0;
            s.Exhibits.ViewportHeightIn = 3.0;
            s.Exhibits.Orientation = "AllowRotate";
            FieldCodes.Settings.FtfSettings.SaveProfile("LIVESMOKE WIDE", s);
            Application.DocumentManager.MdiActiveDocument.Editor.WriteMessage("\nPRODPROFILE: LIVESMOKE WIDE saved.\n");
        }

        /// <summary>Hand changes on exhibit 1: move the area table, edit the construction area's area label and the title.</summary>
        [CommandMethod("PRODEXHIBITEDIT")]
        public void EditExhibit()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            AcDb.Database db = doc.Database;
            using (AcDb.Transaction tr = db.TransactionManager.StartTransaction())
            {
                var exhibit = ReadExhibits(tr, db).First(x => x.LayoutName == "EXHIBIT 1 OF 1");
                var area = ReadEasements(tr, db).First(r => r.IsArea);
                foreach (var item in exhibit.Items)
                {
                    var entity = Resolve(tr, db, item.Handle);
                    if (entity == null) continue;
                    if (item.Key == "AREATABLE")
                    {
                        var table = (AcDb.Table)tr.GetObject(entity.ObjectId, AcDb.OpenMode.ForWrite);
                        table.Position = new Point3d(table.Position.X - 0.2, table.Position.Y + 0.1, 0);
                        _movedTableX = table.Position.X;
                        _movedTableY = table.Position.Y;
                    }
                    if (item.Key == "AREA:" + area.Id || item.Key == "TITLE")
                    {
                        var text = (AcDb.MText)tr.GetObject(entity.ObjectId, AcDb.OpenMode.ForWrite);
                        text.Contents = item.Key == "TITLE" ? text.Contents + "\\PREVISED BY HAND" : "EDITED BY HAND";
                        if (item.Key != "TITLE") _editedLabelKey = item.Key;
                    }
                }
                tr.Commit();
            }
            doc.Editor.WriteMessage("\nPRODEXHIBITEDIT: area table moved, area label and title edited on EXHIBIT 1 OF 1.\n");
        }

        private static AcDb.Entity Resolve(AcDb.Transaction tr, AcDb.Database db, string handle)
        {
            long value;
            if (handle == null || !long.TryParse(handle, System.Globalization.NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value)) return null;
            AcDb.ObjectId id;
            if (!db.TryGetObjectId(new AcDb.Handle(value), out id) || id.IsErased) return null;
            return tr.GetObject(id, AcDb.OpenMode.ForRead) as AcDb.Entity;
        }

        private static bool SameAngle(double a, double b)
        {
            var d = Math.IEEERemainder(a - b, 2 * Math.PI);
            return Math.Abs(d) < 1e-6;
        }

        private static void VerifyExhibits(Editor ed, AcDb.Transaction tr, AcDb.Database db, IList<FieldCodes.Easements.EasementRecord> records)
        {
            var exhibits = ReadExhibits(tr, db);
            ed.WriteMessage("\n-- exhibits: {0} (expect {1})", exhibits.Count, _expect);
            if (_expect == "NONE" && exhibits.Count == 0) return;
            var layouts = (AcDb.DBDictionary)tr.GetObject(db.LayoutDictionaryId, AcDb.OpenMode.ForRead);
            var scales = new FieldCodes.Settings.ExhibitSettings().ScaleList();
            foreach (var x in exhibits.OrderBy(q => q.LayoutName))
            {
                ed.WriteMessage("\n   {0}: 1\" = {1}' rotation {2:0.#} items {3} sources {4} review {5}", x.LayoutName, x.Scale, x.RotationDegrees, x.Items.Count, x.Sources.Count, x.Review.Count);
                foreach (var r in x.Review) ed.WriteMessage("\n     review: " + r.ToString().Replace("{", "{{").Replace("}", "}}"));
                if (_expect == "CREATED")
                    foreach (var i in x.Items.Where(q => q.Kind != "LABEL" && q.Kind != "VIEWPORT"))
                    {
                        var e = Resolve(tr, db, i.Handle);
                        if (e == null) continue;
                        try { var g = e.GeometricExtents; ed.WriteMessage("\n     box {0}: {1:0.00},{2:0.00} - {3:0.00},{4:0.00}", i.Key, g.MinPoint.X, g.MinPoint.Y, g.MaxPoint.X, g.MaxPoint.Y); }
                        catch (Autodesk.AutoCAD.Runtime.Exception) { }
                    }
                Check(ed, layouts.Contains(x.LayoutName), x.LayoutName + ": layout exists");
                var vp = Resolve(tr, db, x.ViewportHandle) as AcDb.Viewport;
                Check(ed, vp != null && Math.Abs(1.0 / vp.CustomScale - x.Scale) < 1e-6 && SameAngle(vp.TwistAngle, x.RotationDegrees * Math.PI / 180),
                      x.LayoutName + ": viewport exists at the recorded scale and rotation");
                Check(ed, x.ScaleChosenByUser || scales.Contains(x.Scale), x.LayoutName + ": automatic scale is a profile engineering scale (1\" = " + x.Scale + "')");
                Check(ed, x.Items.Any(i => i.Key == "NORTH") && x.Items.Any(i => i.Key == "SCALEBAR"), x.LayoutName + ": north arrow and scale bar drawn");
                var north = x.Items.FirstOrDefault(i => i.Key == "NORTH");
                var arrow = north == null ? null : Resolve(tr, db, north.Handle) as AcDb.BlockReference;
                Check(ed, arrow != null && SameAngle(arrow.Rotation, x.RotationDegrees * Math.PI / 180), x.LayoutName + ": north arrow turned with the view");
                Check(ed, x.NorthArrowVerified, x.LayoutName + ": the north arrow was checked against the view's turn");
                var missing = x.Items.Where(i => i.Kind != "VIEWPORT" && Resolve(tr, db, i.Handle) == null).Select(i => i.Key).ToList();
                Check(ed, missing.Count == 0, x.LayoutName + ": every generated item is on the sheet (" + string.Join(", ", missing.ToArray()) + ")");

                if (x.LayoutName == "EXHIBIT 1 OF 1")
                {
                    Check(ed, x.Sources.Count == 3, "exhibit 1 shows the utility easement, its temporary easement and the construction area");
                    var table = x.Items.FirstOrDefault(i => i.Key == "AREATABLE");
                    var t = table == null ? null : Resolve(tr, db, table.Handle) as AcDb.Table;
                    Check(ed, t != null && t.Rows.Count == 6 && t.Cells[5, 0].TextString == "COMBINED AREA SHOWN", "exhibit 1 area table: 3 easements and a combined row not called a legal total");
                    Check(ed, x.Items.Any(i => i.Key.EndsWith(":POB", StringComparison.Ordinal)) && x.Items.Any(i => i.Key.EndsWith(":POC", StringComparison.Ordinal)), "exhibit 1 has POC and POB leaders");
                    Check(ed, x.Items.Any(i => i.Key.StartsWith("LABEL:", StringComparison.Ordinal)), "exhibit 1 has course labels");
                    Check(ed, x.Items.Any(i => i.Key.StartsWith("DIM:", StringComparison.Ordinal)), "exhibit 1 has width dimensions");
                    Check(ed, x.Items.Any(i => i.Key == "LEGEND") && x.Items.Any(i => i.Key == "TITLE"), "exhibit 1 has a legend and a title");
                    var stale = FieldCodes.Exhibits.ExhibitPlanner.StaleSources(x, records);
                    if (_expect == "REBUILT")
                    {
                        Check(ed, stale.Count == 0 && x.RebuiltUtc.HasValue, "exhibit 1 is current after FTFEXHIBITREBUILD");
                        Check(ed, t != null && Math.Abs(t.Position.X - _movedTableX) < 1e-6 && Math.Abs(t.Position.Y - _movedTableY) < 1e-6 &&
                                  Math.Abs(table.X - _movedTableX) < 1e-6, "the area table moved by hand stayed where it was moved");
                        var edited = x.Items.FirstOrDefault(i => i.Key == _editedLabelKey);
                        var label = edited == null ? null : Resolve(tr, db, edited.Handle) as AcDb.MText;
                        Check(ed, label != null && label.Contents == "EDITED BY HAND", "the hand-edited area label was not overwritten");
                        Check(ed, x.Review.Any(r => r.Message.StartsWith("CONFLICT", StringComparison.Ordinal)), "the hand-edited area label is reported as a conflict");
                        Check(ed, x.Review.Any(r => r.ItemKey == "TITLE" && r.Message.Contains("edited by hand")), "the hand-edited title is reported and kept");
                    }
                    if (_expect == "UNDONE")
                        Check(ed, stale.Count > 0, "after undoing the exhibit rebuild the exhibit is stale again");
                }
                if (x.LayoutName == "EXHIBIT 2")
                {
                    Check(ed, x.Scale == 100 && x.ScaleChosenByUser, "exhibit 2 uses the scale chosen by the drafter (1\" = 100')");
                    Check(ed, x.Items.Any(i => i.Key == "AREATABLE"), "exhibit 2 lists the portion easement's components in an area table");
                    Check(ed, x.Items.Any(i => i.Key.StartsWith("DIM:", StringComparison.Ordinal)), "exhibit 2 dimensions the portion calls");
                }
                if (x.LayoutName == "EXHIBIT 4")
                    Check(ed, Math.Abs(x.RotationDegrees) > 1 && x.RotateAllowed && x.ProfileName == "LIVESMOKE WIDE",
                          "exhibit 4 (wide viewport profile) turns the view for the long north-south easement (" + x.RotationDegrees.ToString("0.#", CultureInfo.InvariantCulture) + " degrees)");
                if (x.LayoutName == "EXHIBIT 5")
                    Check(ed, x.Review.Any(r => r.Severity == "Error" && r.Message.Contains("does not fit")) && x.Review.Count >= 2,
                          "exhibit 5 (forced to 1\" = 10') lists what needs manual cleanup instead of pretending it is clean (" + x.Review.Count + " items)");
            }
            if (_expect == "CREATED" || _expect == "REOPENED")
                Check(ed, exhibits.Count == 5, "five exhibits are stored");
        }

        private static IList<FieldCodes.Easements.EasementRecord> ReadEasements(AcDb.Transaction tr, AcDb.Database db)
        {
            var list = new List<FieldCodes.Easements.EasementRecord>();
            AcDb.DBDictionary root = Root(tr, db);
            if (root == null || !root.Contains("EASEMENTS")) return list;
            AcDb.DBDictionary dict = (AcDb.DBDictionary)tr.GetObject(root.GetAt("EASEMENTS"), AcDb.OpenMode.ForRead);
            foreach (AcDb.DBDictionaryEntry entry in dict)
                list.Add(FieldCodes.Easements.EasementRecord.FromJson(Join(tr, dict, entry.Key)));
            return list;
        }
    }
}

