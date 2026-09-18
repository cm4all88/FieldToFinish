// Live-smoke harness for the FTFDRAWLINE drafting world. Standalone on purpose:
// it reads FTF's XData by hand rather than referencing FieldCodes.Cad, so it
// verifies what is actually stamped in the drawing, not what the plugin thinks
// it stamped.
//
// DRAFTSEED   creates the office layers and two COGO points the script uses.
// DRAFTVERIFY dumps every FTF-owned entity (kind, layer, geometry, text).
//
// Compiled with the .NET Framework csc (C# 5 only -- no interpolation).
using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AcDb = Autodesk.AutoCAD.DatabaseServices;
using CivApp = Autodesk.Civil.ApplicationServices;
using CivDb = Autodesk.Civil.DatabaseServices;

[assembly: CommandClass(typeof(FtfDraftTest.DraftTestCommands))]

namespace FtfDraftTest
{
    public class DraftTestCommands
    {
        private const string FtfApp = "FTF_FIELDTOFINISH";

        [CommandMethod("DRAFTSEED")]
        public void Seed()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            AcDb.Database db = doc.Database;
            CivApp.CivilDocument cdoc = CivApp.CivilApplication.ActiveDocument;

            using (AcDb.Transaction tr = db.TransactionManager.StartTransaction())
            {
                // The office layers the settings file points at. V-PROP-BNDY-TEXT-E
                // exists so the smart annotation-layer derivation has something real
                // to find; V-ROAD-CNTR-E has no text layer so the fallback shows.
                AcDb.LayerTable lt = (AcDb.LayerTable)tr.GetObject(
                    db.LayerTableId, AcDb.OpenMode.ForWrite);
                string[] names = new string[]
                {
                    "V-PROP-BNDY-E", "V-PROP-BNDY-TEXT-E", "V-ROAD-CNTR-E"
                };
                for (int i = 0; i < names.Length; i++)
                {
                    if (lt.Has(names[i])) continue;
                    AcDb.LayerTableRecord ltr = new AcDb.LayerTableRecord();
                    ltr.Name = names[i];
                    lt.Add(ltr);
                    tr.AddNewlyCreatedDBObject(ltr, true);
                }

                tr.Commit();
            }

            // Two known COGO points for the COGO construction methods.
            double[][] pts = new double[][]
            {
                new double[] { 800, 500, 101 },
                new double[] { 800, 650, 102 }
            };

            using (AcDb.Transaction tr = db.TransactionManager.StartTransaction())
            {
                for (int i = 0; i < pts.Length; i++)
                {
                    Point3d loc = new Point3d(pts[i][0], pts[i][1], 0);
                    AcDb.ObjectId pid = cdoc.CogoPoints.Add(loc, "TESTPT", false);
                    CivDb.CogoPoint cp = tr.GetObject(pid, AcDb.OpenMode.ForWrite)
                        as CivDb.CogoPoint;
                    if (cp == null) continue;

                    uint wanted = (uint)pts[i][2];
                    try { cp.PointNumber = wanted; }
                    catch (System.Exception ex)
                    {
                        ed.WriteMessage("\nDRAFTSEED: could not set point number {0}: {1}",
                            wanted, ex.Message);
                    }
                    ed.WriteMessage("\nDRAFTSEED: point {0} at ({1:0.###},{2:0.###})",
                        cp.PointNumber, loc.X, loc.Y);
                }
                tr.Commit();
            }

            ed.WriteMessage("\nDRAFTSEED: done.\n");
        }

        [CommandMethod("DRAFTVERIFY")]
        public void Verify()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            AcDb.Database db = doc.Database;

            Dictionary<short, int> byKind = new Dictionary<short, int>();
            int owned = 0;

            using (AcDb.Transaction tr = db.TransactionManager.StartTransaction())
            {
                AcDb.BlockTable bt = (AcDb.BlockTable)tr.GetObject(
                    db.BlockTableId, AcDb.OpenMode.ForRead);
                AcDb.BlockTableRecord ms = (AcDb.BlockTableRecord)tr.GetObject(
                    bt[AcDb.BlockTableRecord.ModelSpace], AcDb.OpenMode.ForRead);

                foreach (AcDb.ObjectId id in ms)
                {
                    if (id.IsErased) continue;
                    AcDb.Entity ent = tr.GetObject(id, AcDb.OpenMode.ForRead, false, true)
                        as AcDb.Entity;
                    if (ent == null) continue;

                    short kind = -1;
                    string typeName = null;
                    using (AcDb.ResultBuffer rb = ent.GetXDataForApplication(FtfApp))
                    {
                        if (rb == null) continue;
                        int stringIndex = 0;
                        foreach (AcDb.TypedValue tv in rb)
                        {
                            if (tv.TypeCode == (int)AcDb.DxfCode.ExtendedDataAsciiString)
                            {
                                if (stringIndex == 2) typeName = tv.Value as string;
                                stringIndex++;
                            }
                            else if (tv.TypeCode == (int)AcDb.DxfCode.ExtendedDataInteger16)
                            {
                                kind = Convert.ToInt16(tv.Value);
                            }
                        }
                    }

                    owned++;
                    if (byKind.ContainsKey(kind)) byKind[kind]++;
                    else byKind[kind] = 1;

                    string detail = "";
                    AcDb.Line line = ent as AcDb.Line;
                    AcDb.DBText text = ent as AcDb.DBText;
                    if (line != null)
                        detail = string.Format(
                            " start=({0:0.###},{1:0.###}) end=({2:0.###},{3:0.###}) lt={4}",
                            line.StartPoint.X, line.StartPoint.Y,
                            line.EndPoint.X, line.EndPoint.Y, line.Linetype);
                    else if (text != null)
                        detail = string.Format(
                            " text=[{0}] rot={1:0.##}deg at=({2:0.###},{3:0.###}) style={4}",
                            text.TextString, text.Rotation * 180.0 / Math.PI,
                            text.AlignmentPoint.X, text.AlignmentPoint.Y,
                            text.TextStyleName);

                    ed.WriteMessage("\nDRAFTVERIFY: kind={0}({1}) {2} layer={3} type={4}{5}",
                        kind, KindName(kind), ent.GetType().Name, ent.Layer,
                        typeName == null ? "?" : typeName, detail);
                }

                tr.Commit();
            }

            List<string> parts = new List<string>();
            foreach (KeyValuePair<short, int> pair in byKind)
                parts.Add(KindName(pair.Key) + "=" + pair.Value);
            ed.WriteMessage("\nDRAFTVERIFY: {0} FTF-owned entities. {1}\n",
                owned, string.Join(" ", parts.ToArray()));
        }

        private static string KindName(short kind)
        {
            switch (kind)
            {
                case 3: return "Label";
                case 4: return "Mask";
                case 8: return "LineLabel";
                case 9: return "LineMask";
                case 10: return "DraftLine";
                case 11: return "DraftAnnotation";
                case 12: return "DraftMask";
                default: return "Kind" + kind;
            }
        }
    }
}
