// Live-smoke harness for FTFCOMPARE. Runs inside accoreconsole with the real plugin
// loaded. Standalone on purpose: it reads layers and FTF XData by hand, so it checks
// what is really in the drawing rather than what the plugin believes.
//
// COMPARESEED    builds a "consultant" xref drawing and a survey drawing beside it:
//                the survey has curbs, a fence, poles and manholes with labels; the
//                xref shows some of them, part of one curb, a different rim at one MH.
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

        /// <summary>Writes compare-consultant.dwg beside the harness, attaches it to the
        /// open (seed) drawing as an xref, and draws the survey objects.</summary>
        [CommandMethod("COMPARESEED")]
        public void Seed()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            AcDb.Database db = doc.Database;
            CivApp.CivilDocument cdoc = CivApp.CivilApplication.ActiveDocument;
            string consultantPath = Path.Combine(Here(), "compare-consultant.dwg");

            // --- the consultant drawing, as a separate file ------------------------
            using (AcDb.Database cdb = new AcDb.Database(true, true))
            {
                using (AcDb.Transaction tr = cdb.TransactionManager.StartTransaction())
                {
                    AcDb.BlockTableRecord ms = (AcDb.BlockTableRecord)tr.GetObject(AcDb.SymbolUtilityServices.GetBlockModelSpaceId(cdb), AcDb.OpenMode.ForWrite);
                    AddLayer(cdb, tr, "E-CURB"); AddLayer(cdb, tr, "E-FENCE"); AddLayer(cdb, tr, "E-PWR"); AddLayer(cdb, tr, "E-STRM-MH"); AddLayer(cdb, tr, "E-TEXT");
                    // full curb: covers survey curb A entirely; half of curb B
                    AddLine(ms, tr, "E-CURB", 1000, 1000, 1100, 1000);
                    AddLine(ms, tr, "E-CURB", 1000, 1050, 1100, 1050);      // survey B runs to 1200
                    // fence present
                    AddLine(ms, tr, "E-FENCE", 1000, 1100, 1300, 1100);
                    // one pole of two
                    AddBlock(cdb, ms, tr, "POLE", "E-PWR", 1000.4, 1200.3);
                    // a storm manhole, with a rim label that disagrees with ours
                    AddBlock(cdb, ms, tr, "STMH", "E-STRM-MH", 1050.3, 1200.1);
                    AddText(ms, tr, "E-TEXT", "RIM 121.00", 1052, 1203);
                    tr.Commit();
                }
                cdb.Insunits = AcDb.UnitsValue.Feet;
                cdb.SaveAs(consultantPath, AcDb.DwgVersion.Current);
            }

            // --- attach it, and draw the survey ------------------------------------
            using (AcDb.Transaction tr = db.TransactionManager.StartTransaction())
            {
                AcDb.ObjectId xrefId = db.AttachXref(consultantPath, "BASE");
                AcDb.BlockTableRecord ms = (AcDb.BlockTableRecord)tr.GetObject(AcDb.SymbolUtilityServices.GetBlockModelSpaceId(db), AcDb.OpenMode.ForWrite);
                AcDb.BlockReference br = new AcDb.BlockReference(new Point3d(0, 0, 0), xrefId);
                ms.AppendEntity(br);
                tr.AddNewlyCreatedDBObject(br, true);

                // Clip the xref to x <= 1150: the fence beyond the clip is NOT shown by the
                // consultant and must not be suppressed.
                try { ClipXref(br, tr, 900, 900, 1150, 1400); }
                catch (System.Exception ex) { ed.WriteMessage("\n  (clip not applied: {0})", ex.Message); }

                AddLayer(db, tr, "V-SURF-CURB-E"); AddLayer(db, tr, "V-SURF-FENC-E"); AddLayer(db, tr, "V-UTIL-STRM-TEXT"); AddLayer(db, tr, "V-NODE");
                AddLayer(db, tr, "V-UTIL-STRC"); AddLayer(db, tr, "V-UTIL-STRC-SYM"); AddLayer(db, tr, "V-NODE-MARK");
                // A survey symbol block whose definition draws on its own layer: the insert
                // alone going nonplot would leave the circle plotting.
                AddBlockOnLayer(db, ms, tr, "SYMMH", "V-UTIL-STRC", "V-UTIL-STRC-SYM", 1050, 1200);
                AddLine(ms, tr, "V-SURF-CURB-E", 1000, 1000, 1100, 1000);                 // CURBA: matched
                AddLine(ms, tr, "V-SURF-CURB-E", 1000, 1050, 1200, 1050);                 // CURBB: partial (1100-1200 missing)
                for (int i = 0; i < 3; i++) AddLine(ms, tr, "V-SURF-FENC-E", 1000 + i * 100, 1100, 1098 + i * 100, 1100); // fragmented fence: matched
                AddLine(ms, tr, "V-SURF-CURB-E", 1000, 1300, 1100, 1300);                 // CURBC: missing
                AddText(ms, tr, "V-UTIL-STRM-TEXT", "RIM 120.50", 1052, 1197);           // conflicts with theirs
                AddText(ms, tr, "V-UTIL-STRM-TEXT", "IE N 115.20", 1052, 1194);          // they lack it
                tr.Commit();
            }
            using (AcDb.Transaction tr = db.TransactionManager.StartTransaction())
            {
                AddCogo(cdoc, tr, ed, "PP 1", 1000, 1200, 0, 501);        // matched pole
                AddCogo(cdoc, tr, ed, "PP 2", 1100, 1200, 0, 502);        // missing pole
                AddCogo(cdoc, tr, ed, "SDMH 1", 1050, 1200, 120.5, 503);  // matched manhole
                AddCogo(cdoc, tr, ed, "HH 1", 1150, 1250, 0, 504);        // missing handhole

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
                tr.Commit();
            }
            db.Insunits = AcDb.UnitsValue.Feet;
            ed.WriteMessage("\nCOMPARESEED: consultant xref {0} attached; survey drawn.", consultantPath);
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

            using (AcDb.Transaction tr = db.TransactionManager.StartTransaction())
            {
                AcDb.LayerTable lt = (AcDb.LayerTable)tr.GetObject(db.LayerTableId, AcDb.OpenMode.ForRead);
                string[] nonplot = { "PMX-COMP-NP-POINT", "PMX-COMP-NP-LINE", "PMX-COMP-NP-BLOCK", "PMX-COMP-NP-TEXT", "PMX-COMP-NP-UTILITY" };
                if (applied)
                    foreach (string name in nonplot)
                    {
                        bool ok = lt.Has(name) && !((AcDb.LayerTableRecord)tr.GetObject(lt[name], AcDb.OpenMode.ForRead)).IsPlottable;
                        Check(ed, ok, name + " exists and does not plot");
                    }

                var byKey = new Dictionary<string, List<string>>();   // "LINE y=1000" -> layers
                int splits = 0;
                string symmhDefinition = null;
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
                        // kind 35 = CompareSplit
                        foreach (AcDb.TypedValue tv in xd)
                            if (tv.TypeCode == 1070 && Convert.ToInt16(tv.Value) == 35) splits++;
                        xd.Dispose();
                    }
                    AcDb.BlockReference insert = e as AcDb.BlockReference;
                    if (insert != null)
                    {
                        AcDb.BlockTableRecord def = (AcDb.BlockTableRecord)tr.GetObject(insert.BlockTableRecord, AcDb.OpenMode.ForRead);
                        if (def.Name == "SYMMH" || def.Name.EndsWith("$SYMMH")) symmhDefinition = def.Name;
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
                    Check(ed, Has(byKey, "LINE y=1000", "PMX-COMP-NP-LINE"), "curb A (fully represented) is nonplot");
                    Check(ed, Has(byKey, "LINE y=1050", "PMX-COMP-NP-LINE"), "curb B original (partly represented) is nonplot");
                    Check(ed, splits >= 1, "curb B uncovered portion exists as a split copy (" + splits + ")");
                    Check(ed, Has(byKey, "LINE y=1100", "PMX-COMP-NP-LINE"), "fence inside the xref clip is nonplot");
                    Check(ed, Has(byKey, "LINE y=1100", "V-SURF-FENC-E"), "fence beyond the xref clip (x > 1150) still plots");
                    Check(ed, symmhDefinition != null && symmhDefinition.StartsWith("PMX-COMP-NP$"), "survey block with own-layer geometry points at a nonplot twin (" + symmhDefinition + ")");
                    Check(ed, pole1Style == "PMX-COMP-NP", "pole with a fixed-layer point style got the nonplot point style (" + pole1Style + ")");
                    Check(ed, !pole1LabelVisible, "matched pole's label is hidden");
                    Check(ed, Has(byKey, "LINE y=1300", "V-SURF-CURB-E"), "curb C (missing) still plots");
                    Check(ed, Has(byKey, "COGO PP 1", "PMX-COMP-NP-UTILITY"), "matched pole is nonplot");
                    Check(ed, !Has(byKey, "COGO PP 2", "PMX-COMP-NP-UTILITY"), "missing pole still plots");
                    Check(ed, Has(byKey, "COGO SDMH 1", "PMX-COMP-NP-UTILITY"), "matched manhole is nonplot");
                    Check(ed, !Has(byKey, "COGO HH 1", "PMX-COMP-NP-UTILITY"), "missing handhole still plots");
                    Check(ed, Has(byKey, "TEXT RIM 120.50", "V-UTIL-STRM-TEXT"), "conflicting rim label still plots (discrepancy, not auto-applied)");
                    Check(ed, Has(byKey, "TEXT IE N 115.20", "V-UTIL-STRM-TEXT"), "invert label they lack still plots");
                }
                else
                {
                    Check(ed, splits == 0, "no split copies remain");
                    Check(ed, Has(byKey, "LINE y=1000", "V-SURF-CURB-E"), "curb A back on V-SURF-CURB-E");
                    Check(ed, Has(byKey, "LINE y=1050", "V-SURF-CURB-E"), "curb B back on V-SURF-CURB-E");
                    Check(ed, Has(byKey, "LINE y=1100", "V-SURF-FENC-E"), "fence back on V-SURF-FENC-E");
                    Check(ed, !Has(byKey, "COGO PP 1", "PMX-COMP-NP-UTILITY"), "pole back on its layer");
                    Check(ed, !Has(byKey, "COGO SDMH 1", "PMX-COMP-NP-UTILITY"), "manhole back on its layer");
                    Check(ed, symmhDefinition == "SYMMH", "survey block points at its original definition again (" + symmhDefinition + ")");
                    Check(ed, !bt.Has("PMX-COMP-NP$SYMMH"), "nonplot twin definition purged");
                    Check(ed, pole1Style == "FIXED-MARK", "pole's original point style restored (" + pole1Style + ")");
                    Check(ed, pole1LabelVisible, "pole's label visible again");
                }
                tr.Commit();
            }

            string report = Path.Combine(Path.GetDirectoryName(db.Filename), Path.GetFileNameWithoutExtension(db.Filename) + ".ftf-compare.csv");
            Check(ed, File.Exists(report), "CSV report written: " + report);
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
