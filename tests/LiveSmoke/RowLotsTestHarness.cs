// Live-smoke harness for FTFROW and FTFLOTS. Standalone on purpose: it reads FTF's
// XData by hand rather than referencing FieldCodes.Cad, so it verifies what is
// actually stamped in the drawing, not what the plugin thinks it stamped.
//
// ROWSEED   creates the office layers the settings file points at, and the monument
//           and block-corner COGO points the script picks.
// ROWVERIFY dumps the FTF-owned entities added SINCE THE LAST ROWVERIFY (kind, layer,
//           geometry, text), with a per-layer tally. One call after each command, so
//           each command's output is read on its own.
// ROWTALLY  dumps everything FTF owns, at the end.
//
// Compiled with the .NET Framework csc (C# 5 only -- no interpolation, no $"").
using System;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AcDb = Autodesk.AutoCAD.DatabaseServices;
using CivApp = Autodesk.Civil.ApplicationServices;
using CivDb = Autodesk.Civil.DatabaseServices;

[assembly: CommandClass(typeof(FtfRowLotsTest.RowLotsTestCommands))]

namespace FtfRowLotsTest
{
    public class RowLotsTestCommands
    {
        private const string FtfApp = "FTF_FIELDTOFINISH";

        /// <summary>Handles already reported, so each ROWVERIFY shows one command's work.</summary>
        private static readonly Dictionary<string, bool> Seen = new Dictionary<string, bool>();

        /// <summary>
        /// The layers FTF Settings points at for the right-of-way and platted-block pages.
        /// FTF never creates an office layer from a configured name, so the drawing has to
        /// have them -- that refusal is the behaviour under test, not an obstacle to it.
        /// </summary>
        private static readonly string[] Layers =
        {
            // Right of way.
            "V-ROAD-ROW-E", "V-ROAD-ROW-TEXT-E", "V-PROP-MON-E", "V-ROAD-CNTR-E",
            "V-SURV-MONU-E", "V-SURV-MONU-TEXT-E", "V-ROAD-ROW-DIM-E", "V-ROAD-NAME-E",
            // Platted blocks.
            "V-PROP-LOT-E", "V-PROP-LOT-TEXT-E", "V-PROP-SETB-E", "V-PROP-ESMT-E",
            "V-PROP-DRAN-E", "V-PROP-NOTE-E"
        };

        /// <summary>
        /// Point number, easting, northing. 101-103 are a right-of-way run's monuments;
        /// 201-204 a platted block's corners; 301-304 an intersection's; 401-404 the block
        /// FTFLOTS divides. Every ring is picked counter-clockwise, as a drafter would walk it.
        /// </summary>
        private static readonly double[][] Points =
        {
            new double[] { 101, 5000, 5000 },
            new double[] { 102, 5100, 5000 },
            new double[] { 103, 5200, 5000 },

            new double[] { 201, 6000, 5000 },
            new double[] { 202, 6200, 5000 },
            new double[] { 203, 6200, 5100 },
            new double[] { 204, 6000, 5100 },

            new double[] { 301, 7000, 5000 },
            new double[] { 302, 7100, 5000 },
            new double[] { 303, 7100, 5100 },
            new double[] { 304, 7000, 5100 },

            new double[] { 401, 8000, 5000 },
            new double[] { 402, 8200, 5000 },
            new double[] { 403, 8200, 5100 },
            new double[] { 404, 8000, 5100 }
        };

        [CommandMethod("ROWSEED")]
        public void Seed()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            AcDb.Database db = doc.Database;
            CivApp.CivilDocument cdoc = CivApp.CivilApplication.ActiveDocument;

            using (AcDb.Transaction tr = db.TransactionManager.StartTransaction())
            {
                AcDb.LayerTable lt = (AcDb.LayerTable)tr.GetObject(db.LayerTableId, AcDb.OpenMode.ForWrite);
                int made = 0;
                for (int i = 0; i < Layers.Length; i++)
                {
                    if (lt.Has(Layers[i])) continue;
                    AcDb.LayerTableRecord ltr = new AcDb.LayerTableRecord();
                    ltr.Name = Layers[i];
                    lt.Add(ltr);
                    tr.AddNewlyCreatedDBObject(ltr, true);
                    made++;
                }
                tr.Commit();
                ed.WriteMessage("\nROWSEED: {0} layer(s) created, {1} already there.", made, Layers.Length - made);
            }

            using (AcDb.Transaction tr = db.TransactionManager.StartTransaction())
            {
                for (int i = 0; i < Points.Length; i++)
                {
                    Point3d loc = new Point3d(Points[i][1], Points[i][2], 0);
                    AcDb.ObjectId pid = cdoc.CogoPoints.Add(loc, "MON", false);
                    CivDb.CogoPoint cp = tr.GetObject(pid, AcDb.OpenMode.ForWrite) as CivDb.CogoPoint;
                    if (cp == null) continue;

                    uint wanted = (uint)Points[i][0];
                    try { cp.PointNumber = wanted; }
                    catch (System.Exception ex)
                    {
                        ed.WriteMessage("\nROWSEED: could not set point number {0}: {1}", wanted, ex.Message);
                    }
                }
                tr.Commit();
            }

            ed.WriteMessage("\nROWSEED: {0} survey point(s) at the monument and corner locations.", Points.Length);
            ed.WriteMessage("\nROWSEED: done.\n");
        }

        /// <summary>
        /// Takes one configured layer out of the drawing, so the next FTFROW has to refuse.
        /// FTF never creates an office layer from a configured name; this is how that is
        /// checked rather than taken on trust. ROWSHOW puts it back.
        /// </summary>
        [CommandMethod("ROWHIDE")]
        public void Hide()
        {
            Rename("V-SURV-MONU-E", "V-SURV-MONU-E-GONE");
        }

        [CommandMethod("ROWSHOW")]
        public void Show()
        {
            Rename("V-SURV-MONU-E-GONE", "V-SURV-MONU-E");
        }

        private static void Rename(string from, string to)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            AcDb.Database db = doc.Database;

            using (AcDb.Transaction tr = db.TransactionManager.StartTransaction())
            {
                AcDb.LayerTable lt = (AcDb.LayerTable)tr.GetObject(db.LayerTableId, AcDb.OpenMode.ForRead);
                if (!lt.Has(from))
                {
                    ed.WriteMessage("\nROWHIDE: no layer {0}.\n", from);
                    return;
                }
                AcDb.LayerTableRecord ltr = (AcDb.LayerTableRecord)tr.GetObject(lt[from], AcDb.OpenMode.ForWrite);
                ltr.Name = to;
                tr.Commit();
                ed.WriteMessage("\nROWHIDE: layer {0} is now {1}.\n", from, to);
            }
        }

        [CommandMethod("ROWVERIFY")]
        public void Verify()
        {
            Dump(false);
        }

        [CommandMethod("ROWTALLY")]
        public void Tally()
        {
            Dump(true);
        }

        private static void Dump(bool everything)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            AcDb.Database db = doc.Database;

            string tag = everything ? "ROWTALLY" : "ROWVERIFY";
            Dictionary<string, int> byLayer = new Dictionary<string, int>();
            Dictionary<string, int> byKind = new Dictionary<string, int>();
            int shown = 0;

            using (AcDb.Transaction tr = db.TransactionManager.StartTransaction())
            {
                AcDb.BlockTable bt = (AcDb.BlockTable)tr.GetObject(db.BlockTableId, AcDb.OpenMode.ForRead);
                AcDb.BlockTableRecord ms = (AcDb.BlockTableRecord)tr.GetObject(
                    bt[AcDb.BlockTableRecord.ModelSpace], AcDb.OpenMode.ForRead);

                foreach (AcDb.ObjectId id in ms)
                {
                    if (id.IsErased) continue;
                    AcDb.Entity ent = tr.GetObject(id, AcDb.OpenMode.ForRead, false, true) as AcDb.Entity;
                    if (ent == null) continue;

                    short kind = -1;
                    string source = null;
                    using (AcDb.ResultBuffer rb = ent.GetXDataForApplication(FtfApp))
                    {
                        if (rb == null) continue;
                        int stringIndex = 0;
                        foreach (AcDb.TypedValue tv in rb)
                        {
                            // Strings in stamp order: schema, rules version, then what made
                            // it ("ROW", "LOTS"), then the tag text.
                            if (tv.TypeCode == (int)AcDb.DxfCode.ExtendedDataAsciiString)
                            {
                                if (stringIndex == 2) source = tv.Value as string;
                                stringIndex++;
                            }
                            else if (tv.TypeCode == (int)AcDb.DxfCode.ExtendedDataInteger16)
                            {
                                kind = Convert.ToInt16(tv.Value);
                            }
                        }
                    }

                    string handle = ent.Handle.ToString();
                    bool already = Seen.ContainsKey(handle);
                    Seen[handle] = true;
                    if (already && !everything) continue;

                    shown++;
                    Add(byLayer, ent.Layer);
                    Add(byKind, KindName(kind));

                    ed.WriteMessage("\n{0}: {1} kind={2} layer={3} src={4}{5}", tag, ent.GetType().Name,
                                    KindName(kind), ent.Layer, source == null ? "?" : source, Detail(ent));
                }

                tr.Commit();
            }

            ed.WriteMessage("\n{0}: {1} FTF-owned object(s){2}.", tag, shown, everything ? " in the drawing" : " since the last check");
            ed.WriteMessage("\n{0}: by kind  {1}", tag, Join(byKind));
            ed.WriteMessage("\n{0}: by layer {1}\n", tag, Join(byLayer));
        }

        private static string Detail(AcDb.Entity ent)
        {
            AcDb.Line line = ent as AcDb.Line;
            if (line != null)
                return string.Format(CultureInfo.InvariantCulture,
                    " ({0:0.###},{1:0.###})->({2:0.###},{3:0.###}) len={4:0.###} lt={5}",
                    line.StartPoint.X, line.StartPoint.Y, line.EndPoint.X, line.EndPoint.Y, line.Length, line.Linetype);

            AcDb.Arc arc = ent as AcDb.Arc;
            if (arc != null)
                return string.Format(CultureInfo.InvariantCulture,
                    " centre=({0:0.###},{1:0.###}) r={2:0.###} len={3:0.###}",
                    arc.Center.X, arc.Center.Y, arc.Radius, arc.Length);

            AcDb.Circle circle = ent as AcDb.Circle;
            if (circle != null)
                return string.Format(CultureInfo.InvariantCulture, " at=({0:0.###},{1:0.###}) r={2:0.###}",
                    circle.Center.X, circle.Center.Y, circle.Radius);

            AcDb.Polyline pl = ent as AcDb.Polyline;
            if (pl != null)
            {
                string corners = string.Empty;
                for (int i = 0; i < pl.NumberOfVertices; i++)
                {
                    Point2d p = pl.GetPoint2dAt(i);
                    corners += string.Format(CultureInfo.InvariantCulture, "{0}({1:0.##},{2:0.##})",
                                             i == 0 ? string.Empty : " ", p.X, p.Y);
                }
                return string.Format(CultureInfo.InvariantCulture, " closed={0} [{1}]", pl.Closed, corners);
            }

            AcDb.DBText text = ent as AcDb.DBText;
            if (text != null)
            {
                // Left-justified text carries no alignment point, so report the one that
                // means something: AutoCAD leaves AlignmentPoint at the origin there.
                bool centred = text.HorizontalMode != AcDb.TextHorizontalMode.TextLeft;
                Point3d at = centred ? text.AlignmentPoint : text.Position;
                return string.Format(CultureInfo.InvariantCulture,
                    " text=[{0}] rot={1:0.##}deg {2}=({3:0.###},{4:0.###}) h={5:0.###}",
                    text.TextString, text.Rotation * 180.0 / Math.PI,
                    centred ? "mid" : "left", at.X, at.Y, text.Height);
            }

            return string.Empty;
        }

        private static void Add(Dictionary<string, int> tally, string key)
        {
            if (tally.ContainsKey(key)) tally[key]++;
            else tally[key] = 1;
        }

        private static string Join(Dictionary<string, int> tally)
        {
            List<string> parts = new List<string>();
            foreach (KeyValuePair<string, int> pair in tally) parts.Add(pair.Key + "=" + pair.Value);
            parts.Sort();
            return parts.Count == 0 ? "(none)" : string.Join(" ", parts.ToArray());
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
