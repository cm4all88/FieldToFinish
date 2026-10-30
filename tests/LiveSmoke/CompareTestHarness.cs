// Live-smoke harness for FTFCOMPARE. Runs inside accoreconsole with the real plugin
// loaded. Standalone on purpose: it reads layers and FTF XData by hand, so it checks
// what is really in the drawing rather than what the plugin believes.
//
// COMPARESEEDCONSULTANT  run 1: draws the consultant's base map in the open drawing,
//                which the script then saves as compare-consultant.dwg (AutoCAD saves
//                it; no side database is built in managed code).
// COMPARESEED    run 2: attaches that file as a clipped xref and draws the survey:
//                curbs, a fence, poles and manholes with labels; the xref shows some of
//                them, part of one curb, a different rim at one MH.
// COMPARENOTE    a progress marker the script drops between commands; every step also
//                appends to compare-progress.txt, which survives a crash.
// TRIMSEEDSURVEY / TRIMSEED / TRIMVERIFY  the FTFTRIM scenario: our site (with its limits
//                of survey) attached to an aerial host; the aerial is trimmed inside the
//                limits, the seams checked, and everything restored.
// COMPAREVERIFY  checks every expected plotting state after FTFCOMPARERUN/APPLY:
//                matched objects on PMX-COMP-NP-*, missing ones untouched, the split
//                copy present, the nonplot layers not plottable; and after RESTORE,
//                everything back exactly.
//
// Compiled with the .NET Framework csc (C# 5 only -- no interpolation, no ?. ).
using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AcDb = Autodesk.AutoCAD.DatabaseServices;
using AcFilters = Autodesk.AutoCAD.DatabaseServices.Filters;
using CivApp = Autodesk.Civil.ApplicationServices;
using CivDb = Autodesk.Civil.DatabaseServices;
using CivStyles = Autodesk.Civil.DatabaseServices.Styles;

[assembly: CommandClass(typeof(FtfCompareTest.CompareTestCommands))]

namespace FtfCompareTest
{
    public class CompareTestCommands
    {
        private const string FtfApp = "FTF_FIELDTOFINISH";
        private static int _failures;

        private static void Check(Editor ed, bool ok, string what)
        {
            if (!ok) _failures++;
            // A marker of its own: FTFCOMPAREQC prints [PASS]/[FAIL] lines too, and the
            // build script must count only these.
            ed.WriteMessage("\n  [VERIFY {0}] {1}", ok ? "PASS" : "FAIL", what);
        }

        private static string Here()
        {
            return Path.GetDirectoryName(typeof(CompareTestCommands).Assembly.Location);
        }

        // ------------------------------------------------------------------ seed

        /// <summary>Crash-proof progress: appended to compare-progress.txt beside the harness
        /// before each step, so a run that dies without console output still says where.</summary>
        private static void Progress(string step)
        {
            try { File.AppendAllText(Path.Combine(Here(), "compare-progress.txt"), DateTime.Now.ToString("HH:mm:ss") + "  " + step + "\r\n"); }
            catch (System.Exception) { }
        }

        /// <summary>COMPARENOTE word: a progress marker the script drops between commands.</summary>
        [CommandMethod("COMPARENOTE")]
        public void Note()
        {
            Editor ed = Application.DocumentManager.MdiActiveDocument.Editor;
            PromptResult r = ed.GetString("\nNote: ");
            string text = r.Status == PromptStatus.OK ? r.StringResult : "(none)";
            Progress("note: " + text);
            ed.WriteMessage("\nCOMPARENOTE: {0}", text);
        }

        /// <summary>Run 1, in the drawing the script then saves as compare-consultant.dwg: the
        /// consultant's base map. AutoCAD itself saves it (SAVEAS in the script); nothing here
        /// creates a side database or writes a file.</summary>
        [CommandMethod("COMPARESEEDCONSULTANT")]
        public void SeedConsultant()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            AcDb.Database db = doc.Database;
            Progress("consultant: start");
            using (AcDb.Transaction tr = db.TransactionManager.StartTransaction())
            {
                AcDb.BlockTableRecord ms = (AcDb.BlockTableRecord)tr.GetObject(AcDb.SymbolUtilityServices.GetBlockModelSpaceId(db), AcDb.OpenMode.ForWrite);
                AddLayer(db, tr, "E-CURB"); AddLayer(db, tr, "E-FENCE"); AddLayer(db, tr, "E-PWR"); AddLayer(db, tr, "E-STRM-MH"); AddLayer(db, tr, "E-TEXT"); AddLayer(db, tr, "E-COMM");
                Progress("consultant: layers");
                // full curb: covers survey curb A entirely; half of curb B
                AddLine(ms, tr, "E-CURB", 1000, 1000, 1100, 1000);
                AddLine(ms, tr, "E-CURB", 1000, 1050, 1100, 1050);      // survey B runs to 1200
                // fence present
                AddLine(ms, tr, "E-FENCE", 1000, 1100, 1300, 1100);
                Progress("consultant: lines");
                // one pole of two, 0.25 ft from ours (within the 0.5 ft discrepancy tolerance)
                AddBlock(db, ms, tr, "POLE", "E-PWR", 1000.2, 1200.15);
                // a handhole 0.95 ft from ours: the same feature, out of tolerance -> discrepancy
                AddBlock(db, ms, tr, "HH", "E-COMM", 1120.9, 1260.3);
                // a storm manhole, with a rim label that disagrees with ours
                AddBlock(db, ms, tr, "STMH", "E-STRM-MH", 1050.3, 1200.1);
                Progress("consultant: blocks");
                AddText(ms, tr, "E-TEXT", "RIM 121.00", 1052, 1203);
                Progress("consultant: text");
                tr.Commit();
            }
            db.Insunits = AcDb.UnitsValue.Feet;
            Progress("consultant: done");
            ed.WriteMessage("\nCOMPARESEEDCONSULTANT: drawn; the script now saves this drawing as compare-consultant.dwg.");
        }

        /// <summary>Run 2, in the seed drawing: attaches compare-consultant.dwg (saved by run 1)
        /// as a clipped xref and draws the survey objects.</summary>
        [CommandMethod("COMPARESEED")]
        public void Seed()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            AcDb.Database db = doc.Database;
            string consultantPath = Path.Combine(Here(), "compare-consultant.dwg");
            Progress("seed: start");
            if (!File.Exists(consultantPath))
            {
                ed.WriteMessage("\nCOMPARESEED: {0} is missing; run COMPARESEEDCONSULTANT first.", consultantPath);
                return;
            }

            // --- attach it, and draw the survey ------------------------------------
            using (AcDb.Transaction tr = db.TransactionManager.StartTransaction())
            {
                AcDb.ObjectId xrefId = db.AttachXref(consultantPath, "BASE");
                Progress("seed: xref attached");
                AcDb.BlockTableRecord ms = (AcDb.BlockTableRecord)tr.GetObject(AcDb.SymbolUtilityServices.GetBlockModelSpaceId(db), AcDb.OpenMode.ForWrite);
                AcDb.BlockReference br = new AcDb.BlockReference(new Point3d(0, 0, 0), xrefId);
                ms.AppendEntity(br);
                tr.AddNewlyCreatedDBObject(br, true);
                Progress("seed: xref inserted");

                // Clip the xref to x <= 1150: the fence beyond the clip is NOT shown by the
                // consultant and must not be suppressed.
                try { ClipXref(br, tr, 900, 900, 1150, 1400); }
                catch (System.Exception ex) { ed.WriteMessage("\n  (clip not applied: {0})", ex.Message); }
                Progress("seed: xref clipped");

                AddLayer(db, tr, "V-SURF-CURB-E"); AddLayer(db, tr, "V-SURF-FENC-E"); AddLayer(db, tr, "V-UTIL-STRM-TEXT"); AddLayer(db, tr, "V-NODE");
                AddLayer(db, tr, "V-UTIL-STRC"); AddLayer(db, tr, "V-UTIL-STRC-SYM"); AddLayer(db, tr, "V-NODE-MARK");
                Progress("seed: survey layers");
                // A survey symbol block whose definition draws on its own layer: the insert
                // alone going nonplot would leave the circle plotting.
                AddBlockOnLayer(db, ms, tr, "SYMMH", "V-UTIL-STRC", "V-UTIL-STRC-SYM", 1050, 1200);
                Progress("seed: survey block");
                AddLine(ms, tr, "V-SURF-CURB-E", 1000, 1000, 1100, 1000);                 // CURBA: matched
                AddLine(ms, tr, "V-SURF-CURB-E", 1000, 1050, 1200, 1050);                 // CURBB: partial (1100-1200 missing)
                for (int i = 0; i < 3; i++) AddLine(ms, tr, "V-SURF-FENC-E", 1000 + i * 100, 1100, 1098 + i * 100, 1100); // fragmented fence: matched
                AddLine(ms, tr, "V-SURF-CURB-E", 1000, 1300, 1100, 1300);                 // CURBC: missing
                Progress("seed: survey lines");
                AddText(ms, tr, "V-UTIL-STRM-TEXT", "RIM 120.50", 1052, 1197);           // conflicts with theirs
                AddText(ms, tr, "V-UTIL-STRM-TEXT", "IE N 115.20", 1052, 1194);          // they lack it
                Progress("seed: survey text");
                tr.Commit();
                Progress("seed: survey committed");
            }

            CivApp.CivilDocument cdoc = CivApp.CivilApplication.ActiveDocument;
            Progress("seed: civil document");
            using (AcDb.Transaction tr = db.TransactionManager.StartTransaction())
            {
                AddCogo(cdoc, tr, ed, "PP 1", 1000, 1200, 0, 501);        // matched pole
                AddCogo(cdoc, tr, ed, "PP 2", 1100, 1200, 0, 502);        // missing pole
                AddCogo(cdoc, tr, ed, "SDMH 1", 1050, 1200, 120.5, 503);  // matched manhole
                AddCogo(cdoc, tr, ed, "HH 1", 1150, 1250, 0, 504);        // missing handhole
                AddCogo(cdoc, tr, ed, "HH 2", 1120, 1260, 0, 505);        // handhole 0.95 ft from theirs: discrepancy
                Progress("seed: cogo points");

                // The matched pole gets a point style whose marker layer is FIXED to
                // V-NODE-MARK: moving the point's layer alone would leave the marker plotting.
                try
                {
                    CivStyles.PointStyleCollection styles = cdoc.Styles.PointStyles;
                    AcDb.ObjectId styleId = styles.Contains("FIXED-MARK") ? styles["FIXED-MARK"] : styles.Add("FIXED-MARK");
                    CivStyles.PointStyle style = (CivStyles.PointStyle)tr.GetObject(styleId, AcDb.OpenMode.ForWrite);
                    FtfCompareTest.Shim.PointStyleShim.SetPlanMarkerLayer(style, "V-NODE-MARK");
                    foreach (AcDb.ObjectId pid in cdoc.CogoPoints)
                    {
                        CivDb.CogoPoint p = tr.GetObject(pid, AcDb.OpenMode.ForWrite) as CivDb.CogoPoint;
                        if (p != null && p.PointNumber == 501) p.StyleId = styleId;
                    }
                }
                catch (System.Exception ex) { ed.WriteMessage("\n  (fixed-layer point style not applied: {0})", ex.Message); }
                Progress("seed: point style");
                tr.Commit();
                Progress("seed: points committed");
            }
            db.Insunits = AcDb.UnitsValue.Feet;
            Progress("seed: done");
            ed.WriteMessage("\nCOMPARESEED: consultant xref {0} attached; survey drawn.", consultantPath);
        }

        // ------------------------------------------------------------------ trim

        /// <summary>Run A for the trim: our finished site, saved by the script as trim-survey.dwg.
        /// A curb that continues the aerial curb 0.2 ft off (aligned seams), a curb 0.9 ft off the
        /// second aerial curb (discrepancy seams), and the limits of survey on V-SURV-LIMITS.</summary>
        [CommandMethod("TRIMSEEDSURVEY")]
        public void SeedTrimSurvey()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            AcDb.Database db = doc.Database;
            Progress("trim survey: start");
            using (AcDb.Transaction tr = db.TransactionManager.StartTransaction())
            {
                AcDb.BlockTableRecord ms = (AcDb.BlockTableRecord)tr.GetObject(AcDb.SymbolUtilityServices.GetBlockModelSpaceId(db), AcDb.OpenMode.ForWrite);
                AddLayer(db, tr, "V-SURF-CURB-E"); AddLayer(db, tr, "V-SURV-LIMITS");
                AddLine(ms, tr, "V-SURF-CURB-E", 950, 1000.2, 1150, 1000.2);
                AddLine(ms, tr, "V-SURF-CURB-E", 950, 1050.9, 1150, 1050.9);
                AcDb.Polyline limits = new AcDb.Polyline();
                limits.AddVertexAt(0, new Point2d(950, 950), 0, 0, 0);
                limits.AddVertexAt(1, new Point2d(1150, 950), 0, 0, 0);
                limits.AddVertexAt(2, new Point2d(1150, 1150), 0, 0, 0);
                limits.AddVertexAt(3, new Point2d(950, 1150), 0, 0, 0);
                limits.Closed = true;
                limits.Layer = "V-SURV-LIMITS";
                ms.AppendEntity(limits);
                tr.AddNewlyCreatedDBObject(limits, true);
                AcDb.LayerTableRecord lim = (AcDb.LayerTableRecord)tr.GetObject(((AcDb.LayerTable)tr.GetObject(db.LayerTableId, AcDb.OpenMode.ForRead))["V-SURV-LIMITS"], AcDb.OpenMode.ForWrite);
                lim.IsPlottable = false;   // limits usually sit on a no-plot layer; they must still be found
                tr.Commit();
            }
            db.Insunits = AcDb.UnitsValue.Feet;
            Progress("trim survey: done");
            ed.WriteMessage("\nTRIMSEEDSURVEY: drawn; the script now saves this drawing as trim-survey.dwg.");
        }

        /// <summary>Run B for the trim, in the aerial host: aerial curbs crossing the limits, one
        /// inside, one outside, a pole and a label inside, and trim-survey.dwg attached as SITE1.</summary>
        [CommandMethod("TRIMSEED")]
        public void SeedTrim()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            AcDb.Database db = doc.Database;
            string surveyPath = Path.Combine(Here(), "trim-survey.dwg");
            Progress("trim seed: start");
            if (!File.Exists(surveyPath))
            {
                ed.WriteMessage("\nTRIMSEED: {0} is missing; run TRIMSEEDSURVEY first.", surveyPath);
                return;
            }
            using (AcDb.Transaction tr = db.TransactionManager.StartTransaction())
            {
                AcDb.BlockTableRecord ms = (AcDb.BlockTableRecord)tr.GetObject(AcDb.SymbolUtilityServices.GetBlockModelSpaceId(db), AcDb.OpenMode.ForWrite);
                AddLayer(db, tr, "E-CURB"); AddLayer(db, tr, "E-PWR"); AddLayer(db, tr, "E-TEXT");
                AddLine(ms, tr, "E-CURB", 900, 1000, 1200, 1000);     // crosses: aligned seams (ours 0.2 ft off)
                AddLine(ms, tr, "E-CURB", 900, 1050, 1200, 1050);     // crosses: discrepancy seams (ours 0.9 ft off)
                AddLine(ms, tr, "E-CURB", 1000, 1100, 1100, 1100);    // entirely inside
                AddLine(ms, tr, "E-CURB", 1000, 1300, 1100, 1300);    // entirely outside
                AddBlock(db, ms, tr, "POLE", "E-PWR", 1000, 1120);    // inside
                AddText(ms, tr, "E-TEXT", "RIM 121.00", 1050, 1130); // inside
                Progress("trim seed: aerial drawn");
                AcDb.ObjectId xrefId = db.AttachXref(surveyPath, "SITE1");
                AcDb.BlockReference br = new AcDb.BlockReference(new Point3d(0, 0, 0), xrefId);
                ms.AppendEntity(br);
                tr.AddNewlyCreatedDBObject(br, true);
                Progress("trim seed: survey xref attached");
                tr.Commit();
            }
            db.Insunits = AcDb.UnitsValue.Feet;
            Progress("trim seed: done");
            ed.WriteMessage("\nTRIMSEED: aerial drawn; survey xref {0} attached.", surveyPath);
        }

        /// <summary>TRIMVERIFY Applied|Restored</summary>
        [CommandMethod("TRIMVERIFY")]
        public void VerifyTrim()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            AcDb.Database db = doc.Database;
            PromptResult mode = ed.GetString("\nVerify what [Applied/Restored]: ");
            bool applied = mode.Status == PromptStatus.OK && mode.StringResult.StartsWith("A", StringComparison.OrdinalIgnoreCase);
            _failures = 0;
            Progress("trim verify: start " + (applied ? "Applied" : "Restored"));

            using (AcDb.Transaction tr = db.TransactionManager.StartTransaction())
            {
                AcDb.LayerTable lt = (AcDb.LayerTable)tr.GetObject(db.LayerTableId, AcDb.OpenMode.ForRead);
                var byKey = new Dictionary<string, List<string>>();
                int pieces = 0, clouds = 0;
                string poleLayer = null, textLayer = null, xrefLayer = null;
                AcDb.BlockTableRecord ms = (AcDb.BlockTableRecord)tr.GetObject(AcDb.SymbolUtilityServices.GetBlockModelSpaceId(db), AcDb.OpenMode.ForRead);
                foreach (AcDb.ObjectId id in ms)
                {
                    AcDb.Entity e = tr.GetObject(id, AcDb.OpenMode.ForRead) as AcDb.Entity;
                    if (e == null) continue;
                    AcDb.ResultBuffer xd = e.GetXDataForApplication(FtfApp);
                    bool owned = false;
                    if (xd != null)
                    {
                        foreach (AcDb.TypedValue tv in xd)
                        {
                            if (tv.TypeCode != 1070) continue;
                            short kind = Convert.ToInt16(tv.Value);
                            if (kind == 37) { pieces++; owned = true; }
                            if (kind == 38) { clouds++; owned = true; }
                        }
                        xd.Dispose();
                    }
                    AcDb.BlockReference insert = e as AcDb.BlockReference;
                    if (insert != null)
                    {
                        AcDb.BlockTableRecord def = (AcDb.BlockTableRecord)tr.GetObject(insert.BlockTableRecord, AcDb.OpenMode.ForRead);
                        if (def.IsFromExternalReference) xrefLayer = e.Layer;
                        else if (def.Name == "POLE" || def.Name.EndsWith("$POLE")) poleLayer = e.Layer;
                        continue;
                    }
                    AcDb.DBText t = e as AcDb.DBText;
                    if (t != null && t.TextString == "RIM 121.00") { textLayer = e.Layer; continue; }
                    AcDb.Line l = e as AcDb.Line;
                    if (l != null && !owned && Math.Abs(l.StartPoint.Y - l.EndPoint.Y) < 0.01)
                    {
                        string key = "LINE y=" + ((int)Math.Round(l.StartPoint.Y));
                        if (!byKey.ContainsKey(key)) byKey[key] = new List<string>();
                        byKey[key].Add(e.Layer);
                    }
                }

                if (applied)
                {
                    Check(ed, lt.Has("E-CURB-NP") && !((AcDb.LayerTableRecord)tr.GetObject(lt["E-CURB-NP"], AcDb.OpenMode.ForRead)).IsPlottable, "E-CURB-NP exists and does not plot");
                    Check(ed, Has(byKey, "LINE y=1000", "E-CURB-NP"), "aerial curb crossing the limits (aligned) is nonplot");
                    Check(ed, Has(byKey, "LINE y=1050", "E-CURB-NP"), "aerial curb crossing the limits (off by 0.9 ft) is nonplot");
                    Check(ed, Has(byKey, "LINE y=1100", "E-CURB-NP"), "aerial curb inside the limits is nonplot");
                    Check(ed, Has(byKey, "LINE y=1300", "E-CURB"), "aerial curb outside the limits is untouched");
                    Check(ed, pieces == 4, "the outside parts of the two crossing curbs exist as plotting pieces (" + pieces + ", expected 4)");
                    Check(ed, poleLayer == "E-PWR-NP", "aerial pole inside the limits is nonplot (" + poleLayer + ")");
                    Check(ed, textLayer == "E-TEXT-NP", "aerial label inside the limits is nonplot (" + textLayer + ")");
                    Check(ed, clouds == 4, "the two off seams are clouded and noted (" + clouds + " mark objects, expected 4)");
                    Check(ed, xrefLayer != null && !xrefLayer.EndsWith("-NP"), "our survey xref insert is untouched (" + xrefLayer + ")");
                    Check(ed, File.Exists(Path.Combine(Path.GetDirectoryName(db.Filename), Path.GetFileNameWithoutExtension(db.Filename) + ".ftf-seams.csv")), "seam report written");
                    Check(ed, File.Exists(Path.Combine(Path.GetDirectoryName(db.Filename), Path.GetFileNameWithoutExtension(db.Filename) + ".ftf-trim.csv")), "trim report written");
                }
                else
                {
                    Check(ed, Has(byKey, "LINE y=1000", "E-CURB") && Has(byKey, "LINE y=1050", "E-CURB") && Has(byKey, "LINE y=1100", "E-CURB"), "aerial curbs back on E-CURB");
                    Check(ed, pieces == 0, "no outside pieces remain");
                    Check(ed, clouds == 0, "no seam marks remain");
                    Check(ed, poleLayer == "E-PWR", "aerial pole back on E-PWR (" + poleLayer + ")");
                    Check(ed, textLayer == "E-TEXT", "aerial label back on E-TEXT (" + textLayer + ")");
                }
                tr.Commit();
            }
            Progress("trim verify: done, " + _failures + " failure(s)");
            ed.WriteMessage("\nTRIMVERIFY: {0} failure(s)\n", _failures);
        }

        private static void ClipXref(AcDb.BlockReference br, AcDb.Transaction tr, double x1, double y1, double x2, double y2)
        {
            Point2dCollection points = new Point2dCollection();
            points.Add(new Point2d(x1, y1));
            points.Add(new Point2d(x2, y2));
            AcFilters.SpatialFilterDefinition definition = new AcFilters.SpatialFilterDefinition(points, Vector3d.ZAxis, 0.0, 0.0, 0.0, true);
            AcFilters.SpatialFilter filter = new AcFilters.SpatialFilter();
            filter.Definition = definition;
            if (br.ExtensionDictionary.IsNull) br.CreateExtensionDictionary();
            AcDb.DBDictionary ext = (AcDb.DBDictionary)tr.GetObject(br.ExtensionDictionary, AcDb.OpenMode.ForWrite);
            AcDb.DBDictionary filters;
            if (ext.Contains("ACAD_FILTER")) filters = (AcDb.DBDictionary)tr.GetObject(ext.GetAt("ACAD_FILTER"), AcDb.OpenMode.ForWrite);
            else
            {
                filters = new AcDb.DBDictionary();
                ext.SetAt("ACAD_FILTER", filters);
                tr.AddNewlyCreatedDBObject(filters, true);
            }
            filters.SetAt("SPATIAL", filter);
            tr.AddNewlyCreatedDBObject(filter, true);
        }

        private static void AddBlockOnLayer(AcDb.Database db, AcDb.BlockTableRecord ms, AcDb.Transaction tr, string name, string insertLayer, string geometryLayer, double x, double y)
        {
            AcDb.BlockTable bt = (AcDb.BlockTable)tr.GetObject(db.BlockTableId, AcDb.OpenMode.ForWrite);
            AcDb.ObjectId defId;
            if (bt.Has(name)) defId = bt[name];
            else
            {
                AcDb.BlockTableRecord def = new AcDb.BlockTableRecord();
                def.Name = name;
                defId = bt.Add(def);
                tr.AddNewlyCreatedDBObject(def, true);
                AcDb.Circle c = new AcDb.Circle(new Point3d(0, 0, 0), Vector3d.ZAxis, 1.5);
                c.Layer = geometryLayer;
                def.AppendEntity(c);
                tr.AddNewlyCreatedDBObject(c, true);
            }
            AcDb.BlockReference br = new AcDb.BlockReference(new Point3d(x, y, 0), defId);
            br.Layer = insertLayer;
            ms.AppendEntity(br);
            tr.AddNewlyCreatedDBObject(br, true);
        }

        private static void AddLayer(AcDb.Database db, AcDb.Transaction tr, string name)
        {
            AcDb.LayerTable lt = (AcDb.LayerTable)tr.GetObject(db.LayerTableId, AcDb.OpenMode.ForWrite);
            if (lt.Has(name)) return;
            AcDb.LayerTableRecord r = new AcDb.LayerTableRecord();
            r.Name = name;
            lt.Add(r);
            tr.AddNewlyCreatedDBObject(r, true);
        }

        private static void AddLine(AcDb.BlockTableRecord ms, AcDb.Transaction tr, string layer, double x1, double y1, double x2, double y2)
        {
            AcDb.Line l = new AcDb.Line(new Point3d(x1, y1, 0), new Point3d(x2, y2, 0));
            l.Layer = layer;
            ms.AppendEntity(l);
            tr.AddNewlyCreatedDBObject(l, true);
        }

        private static void AddText(AcDb.BlockTableRecord ms, AcDb.Transaction tr, string layer, string text, double x, double y)
        {
            AcDb.DBText t = new AcDb.DBText();
            t.TextString = text;
            t.Position = new Point3d(x, y, 0);
            t.Height = 1.0;
            t.Layer = layer;
            ms.AppendEntity(t);
            tr.AddNewlyCreatedDBObject(t, true);
        }

        private static void AddBlock(AcDb.Database db, AcDb.BlockTableRecord ms, AcDb.Transaction tr, string name, string layer, double x, double y)
        {
            AcDb.BlockTable bt = (AcDb.BlockTable)tr.GetObject(db.BlockTableId, AcDb.OpenMode.ForWrite);
            AcDb.ObjectId defId;
            if (bt.Has(name)) defId = bt[name];
            else
            {
                AcDb.BlockTableRecord def = new AcDb.BlockTableRecord();
                def.Name = name;
                defId = bt.Add(def);
                tr.AddNewlyCreatedDBObject(def, true);
                AcDb.Circle c = new AcDb.Circle(new Point3d(0, 0, 0), Vector3d.ZAxis, 1.0);
                c.Layer = "0";
                def.AppendEntity(c);
                tr.AddNewlyCreatedDBObject(c, true);
            }
            AcDb.BlockReference br = new AcDb.BlockReference(new Point3d(x, y, 0), defId);
            br.Layer = layer;
            ms.AppendEntity(br);
            tr.AddNewlyCreatedDBObject(br, true);
        }

        private static void AddCogo(CivApp.CivilDocument cdoc, AcDb.Transaction tr, Editor ed, string description, double x, double y, double z, uint number)
        {
            AcDb.ObjectId id = cdoc.CogoPoints.Add(new Point3d(x, y, z), description, false);
            CivDb.CogoPoint p = tr.GetObject(id, AcDb.OpenMode.ForWrite) as CivDb.CogoPoint;
            if (p == null) return;
            p.Layer = "V-NODE";   // explicit: the template's point layer setting must not decide the test
            try { p.PointNumber = number; } catch (System.Exception ex) { ed.WriteMessage("\n  point number {0}: {1}", number, ex.Message); }
        }

        // ---------------------------------------------------------------- verify

        /// <summary>COMPAREVERIFY Applied|Restored</summary>
        [CommandMethod("COMPAREVERIFY")]
        public void Verify()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            AcDb.Database db = doc.Database;
            PromptResult mode = ed.GetString("\nVerify what [Applied/Restored]: ");
            bool applied = mode.Status == PromptStatus.OK && mode.StringResult.StartsWith("A", StringComparison.OrdinalIgnoreCase);
            _failures = 0;
            Progress("verify: start " + (applied ? "Applied" : "Restored"));

            using (AcDb.Transaction tr = db.TransactionManager.StartTransaction())
            {
                AcDb.LayerTable lt = (AcDb.LayerTable)tr.GetObject(db.LayerTableId, AcDb.OpenMode.ForRead);
                // Suffix layering: every object goes to its own layer's name + "-NP".
                string[] nonplot = { "V-SURF-CURB-E-NP", "V-SURF-FENC-E-NP", "V-NODE-NP", "V-UTIL-STRC-NP", "V-UTIL-STRC-SYM-NP" };
                if (applied)
                    foreach (string name in nonplot)
                    {
                        bool ok = lt.Has(name) && !((AcDb.LayerTableRecord)tr.GetObject(lt[name], AcDb.OpenMode.ForRead)).IsPlottable;
                        Check(ed, ok, name + " exists and does not plot");
                    }

                var byKey = new Dictionary<string, List<string>>();   // "LINE y=1000" -> layers
                int splits = 0;
                int clouds = 0;
                string symmhDefinition = null;
                string symmhLayer = null;
                string pole1Style = null;
                bool pole1LabelVisible = true;
                AcDb.BlockTable bt = (AcDb.BlockTable)tr.GetObject(db.BlockTableId, AcDb.OpenMode.ForRead);
                AcDb.BlockTableRecord ms = (AcDb.BlockTableRecord)tr.GetObject(AcDb.SymbolUtilityServices.GetBlockModelSpaceId(db), AcDb.OpenMode.ForRead);
                foreach (AcDb.ObjectId id in ms)
                {
                    AcDb.Entity e = tr.GetObject(id, AcDb.OpenMode.ForRead) as AcDb.Entity;
                    if (e == null) continue;
                    AcDb.ResultBuffer xd = e.GetXDataForApplication(FtfApp);
                    if (xd != null)
                    {
                        // kind 35 = CompareSplit, 36 = CompareCloud (revision cloud or its note)
                        foreach (AcDb.TypedValue tv in xd)
                        {
                            if (tv.TypeCode != 1070) continue;
                            short kind = Convert.ToInt16(tv.Value);
                            if (kind == 35) splits++;
                            if (kind == 36) clouds++;
                        }
                        xd.Dispose();
                    }
                    AcDb.BlockReference insert = e as AcDb.BlockReference;
                    if (insert != null)
                    {
                        AcDb.BlockTableRecord def = (AcDb.BlockTableRecord)tr.GetObject(insert.BlockTableRecord, AcDb.OpenMode.ForRead);
                        if (def.Name == "SYMMH" || def.Name.EndsWith("$SYMMH")) { symmhDefinition = def.Name; symmhLayer = e.Layer; }
                    }
                    CivDb.CogoPoint cogo = e as CivDb.CogoPoint;
                    if (cogo != null && cogo.PointNumber == 501)
                    {
                        pole1LabelVisible = cogo.IsLabelVisible;
                        try { pole1Style = ((CivStyles.PointStyle)tr.GetObject(cogo.StyleId, AcDb.OpenMode.ForRead)).Name; } catch (System.Exception) { pole1Style = "?"; }
                    }
                    string key = KeyOf(e);
                    if (key == null) continue;
                    if (!byKey.ContainsKey(key)) byKey[key] = new List<string>();
                    byKey[key].Add(e.Layer);
                }

                if (applied)
                {
                    Check(ed, Has(byKey, "LINE y=1000", "V-SURF-CURB-E-NP"), "curb A (fully represented) is nonplot");
                    Check(ed, Has(byKey, "LINE y=1050", "V-SURF-CURB-E-NP"), "curb B original (partly represented) is nonplot");
                    Check(ed, splits >= 1, "curb B uncovered portion exists as a split copy (" + splits + ")");
                    Check(ed, Has(byKey, "LINE y=1100", "V-SURF-FENC-E-NP"), "fence inside the xref clip is nonplot");
                    Check(ed, Has(byKey, "LINE y=1100", "V-SURF-FENC-E"), "fence beyond the xref clip (x > 1150) still plots");
                    Check(ed, symmhDefinition != null && symmhDefinition.StartsWith("PMX-COMP-NP$"), "survey block with own-layer geometry points at a nonplot twin (" + symmhDefinition + ")");
                    Check(ed, symmhLayer == "V-UTIL-STRC-NP", "survey block insert is on V-UTIL-STRC-NP (" + symmhLayer + ")");
                    Check(ed, pole1Style == "PMX-COMP-NP", "pole with a fixed-layer point style got the nonplot point style (" + pole1Style + ")");
                    Check(ed, !pole1LabelVisible, "matched pole's label is hidden");
                    Check(ed, Has(byKey, "LINE y=1300", "V-SURF-CURB-E"), "curb C (missing) still plots");
                    Check(ed, Has(byKey, "COGO PP 1", "V-NODE-NP"), "matched pole is nonplot");
                    Check(ed, Has(byKey, "COGO PP 2", "V-NODE"), "missing pole still plots");
                    Check(ed, Has(byKey, "COGO SDMH 1", "V-NODE-NP"), "matched manhole is nonplot");
                    Check(ed, Has(byKey, "COGO HH 1", "V-NODE"), "missing handhole still plots");
                    Check(ed, Has(byKey, "COGO HH 2", "V-NODE"), "handhole 0.95 ft from theirs (discrepancy) still plots");
                    Check(ed, clouds >= 2, "discrepancies are marked with a revision cloud and a note (" + clouds + " mark objects)");
                    Check(ed, lt.Has("PMX-COMP-REVIEW") && ((AcDb.LayerTableRecord)tr.GetObject(lt["PMX-COMP-REVIEW"], AcDb.OpenMode.ForRead)).IsPlottable, "review layer PMX-COMP-REVIEW exists and plots");
                    Check(ed, Has(byKey, "TEXT RIM 120.50", "V-UTIL-STRM-TEXT"), "conflicting rim label still plots (discrepancy, not auto-applied)");
                    Check(ed, Has(byKey, "TEXT IE N 115.20", "V-UTIL-STRM-TEXT"), "invert label they lack still plots");
                }
                else
                {
                    Check(ed, splits == 0, "no split copies remain");
                    Check(ed, clouds == 0, "no revision clouds or notes remain");
                    Check(ed, Has(byKey, "LINE y=1000", "V-SURF-CURB-E"), "curb A back on V-SURF-CURB-E");
                    Check(ed, Has(byKey, "LINE y=1050", "V-SURF-CURB-E"), "curb B back on V-SURF-CURB-E");
                    Check(ed, Has(byKey, "LINE y=1100", "V-SURF-FENC-E"), "fence back on V-SURF-FENC-E");
                    Check(ed, Has(byKey, "COGO PP 1", "V-NODE"), "pole back on its layer");
                    Check(ed, Has(byKey, "COGO SDMH 1", "V-NODE"), "manhole back on its layer");
                    Check(ed, symmhDefinition == "SYMMH", "survey block points at its original definition again (" + symmhDefinition + ")");
                    Check(ed, !bt.Has("PMX-COMP-NP$SYMMH"), "nonplot twin definition purged");
                    Check(ed, pole1Style == "FIXED-MARK", "pole's original point style restored (" + pole1Style + ")");
                    Check(ed, pole1LabelVisible, "pole's label visible again");
                }
                tr.Commit();
            }

            string report = Path.Combine(Path.GetDirectoryName(db.Filename), Path.GetFileNameWithoutExtension(db.Filename) + ".ftf-compare.csv");
            Check(ed, File.Exists(report), "CSV report written: " + report);
            Progress("verify: done, " + _failures + " failure(s)");
            ed.WriteMessage("\nCOMPAREVERIFY: {0} failure(s)\n", _failures);
        }

        private static bool Has(Dictionary<string, List<string>> byKey, string key, string layer)
        {
            List<string> layers;
            if (!byKey.TryGetValue(key, out layers)) return false;
            foreach (string l in layers) if (string.Equals(l, layer, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static string KeyOf(AcDb.Entity e)
        {
            AcDb.Line l = e as AcDb.Line;
            if (l != null && Math.Abs(l.StartPoint.Y - l.EndPoint.Y) < 0.01) return "LINE y=" + ((int)Math.Round(l.StartPoint.Y));
            CivDb.CogoPoint p = e as CivDb.CogoPoint;
            if (p != null) return "COGO " + p.RawDescription;
            AcDb.DBText t = e as AcDb.DBText;
            if (t != null) return "TEXT " + t.TextString;
            return null;
        }
    }
}

// Same using set as the plugin's CogoPointPlotting.cs, which compiles against the 2024
// API: the PointDisplayStyleType enum is found through these without naming its
// namespace (it is not in Autodesk.Civil, as the first harness build found out).
namespace FtfCompareTest.Shim
{
    using Autodesk.Civil;
    using Autodesk.Civil.ApplicationServices;
    using Autodesk.Civil.DatabaseServices;
    using Autodesk.Civil.DatabaseServices.Styles;

    internal static class PointStyleShim
    {
        public static void SetPlanMarkerLayer(PointStyle style, string layer)
        {
            style.GetDisplayStylePlan(PointDisplayStyleType.Marker).Layer = layer;
        }
    }
}
