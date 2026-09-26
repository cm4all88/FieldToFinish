// Read-only dump of a drawing's drafting standard, for building an FTF exhibit profile from a
// real office deliverable: layouts and page setups, viewports and their frozen layers, paper-space
// blocks and attributes, text/dimension/leader/table styles, layers, hatches, and the text,
// leaders, dimensions and tables actually used. Opens nothing for write.
//
// Compiled with the .NET Framework csc (C# 5 only). Command: STDDUMP <output file>
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

[assembly: CommandClass(typeof(FtfRealWorld.StandardsDump))]

namespace FtfRealWorld
{
    public class StandardsDump
    {
        private static readonly CultureInfo C = CultureInfo.InvariantCulture;

        [CommandMethod("STDDUMP", CommandFlags.NoUndoMarker)]
        public void Dump()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;
            var db = doc.Database;
            var r = ed.GetString(new PromptStringOptions("\nOutput file: ") { AllowSpaces = true });
            if (r.Status != PromptStatus.OK) return;
            var sb = new StringBuilder();
            Action<string> w = s => sb.AppendLine(s);
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                w("DRAWING " + db.Filename);
                w(string.Format(C, "INSUNITS {0} LTSCALE {1} MEASUREMENT {2} DIMSTYLE {3} TEXTSTYLE {4} CMLEADERSTYLE {5} CTABLESTYLE {6}",
                    db.Insunits, db.Ltscale, db.Measurement, Name(tr, db.Dimstyle), Name(tr, db.Textstyle), Name(tr, db.MLeaderstyle), Name(tr, db.Tablestyle)));

                w("\n== XREFS / BLOCKS");
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                foreach (ObjectId id in bt)
                {
                    var b = (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead);
                    if (b.IsFromExternalReference) w("XREF " + b.Name + " -> " + b.PathName);
                    else if (!b.IsLayout && !b.IsAnonymous && !b.Name.StartsWith("*", StringComparison.Ordinal))
                    {
                        var attdefs = new List<string>();
                        foreach (ObjectId e in b)
                        {
                            var ad = tr.GetObject(e, OpenMode.ForRead) as AttributeDefinition;
                            if (ad != null) attdefs.Add(ad.Tag + (ad.Constant ? "(const)" : "") + "=\"" + ad.TextString + "\" prompt \"" + ad.Prompt + "\"");
                        }
                        w("BLOCK " + b.Name + (attdefs.Count > 0 ? "  ATTDEFS: " + string.Join("; ", attdefs.ToArray()) : ""));
                    }
                }

                w("\n== TEXT STYLES");
                var ts = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
                foreach (ObjectId id in ts)
                {
                    var s = (TextStyleTableRecord)tr.GetObject(id, OpenMode.ForRead);
                    if (string.IsNullOrEmpty(s.Name)) continue;
                    w(string.Format(C, "TEXTSTYLE {0}: font {1} ttf {2} height {3} width {4} oblique {5:0.##} annotative {6}", s.Name, s.FileName, s.Font.TypeFace, s.TextSize, s.XScale, s.ObliquingAngle * 180 / Math.PI, s.Annotative));
                }

                w("\n== DIM STYLES");
                var ds = (DimStyleTable)tr.GetObject(db.DimStyleTableId, OpenMode.ForRead);
                foreach (ObjectId id in ds)
                {
                    var s = (DimStyleTableRecord)tr.GetObject(id, OpenMode.ForRead);
                    w(string.Format(C, "DIMSTYLE {0}: txt {1} asz {2} gap {3} exo {4} exe {5} dec {6} post \"{7}\" tad {8} scale {9} lfac {10} txsty {11} blk \"{12}\" zin {13} annotative {14}",
                        s.Name, s.Dimtxt, s.Dimasz, s.Dimgap, s.Dimexo, s.Dimexe, s.Dimdec, s.Dimpost, s.Dimtad, s.Dimscale, s.Dimlfac, Name(tr, s.Dimtxsty), BlockName(tr, s.Dimblk), s.Dimzin, s.Annotative));
                }

                w("\n== MLEADER STYLES");
                var mls = (DBDictionary)tr.GetObject(db.MLeaderStyleDictionaryId, OpenMode.ForRead);
                foreach (DBDictionaryEntry e in mls)
                {
                    var s = tr.GetObject(e.Value, OpenMode.ForRead) as MLeaderStyle;
                    if (s == null) continue;
                    w(string.Format(C, "MLEADERSTYLE {0}: text {1} height {2} arrow {3} {4} landing {5} dogleg {6} gap {7} scale {8} annotative {9} textstyle {10} attach L{11} R{12}",
                        e.Key, s.ContentType, s.TextHeight, BlockName(tr, s.ArrowSymbolId), s.ArrowSize, s.EnableLanding, s.DoglegLength, s.LandingGap, s.Scale, s.Annotative, Name(tr, s.TextStyleId),
                        s.TextAttachmentType, s.TextAlignmentType));
                }

                w("\n== TABLE STYLES");
                var tss = (DBDictionary)tr.GetObject(db.TableStyleDictionaryId, OpenMode.ForRead);
                foreach (DBDictionaryEntry e in tss)
                {
                    var s = tr.GetObject(e.Value, OpenMode.ForRead) as TableStyle;
                    if (s == null) continue;
                    w(string.Format(C, "TABLESTYLE {0}: title h {1} header h {2} data h {3} margin h {4} v {5} textstyle {6}",
                        e.Key, s.TextHeight(RowType.TitleRow), s.TextHeight(RowType.HeaderRow), s.TextHeight(RowType.DataRow), s.HorizontalCellMargin, s.VerticalCellMargin, Name(tr, s.TextStyle(RowType.DataRow))));
                }

                w("\n== LAYERS");
                var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    var e = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (e == null) continue;
                    int n; counts.TryGetValue(e.Layer, out n); counts[e.Layer] = n + 1;
                }
                foreach (ObjectId id in lt)
                {
                    var l = (LayerTableRecord)tr.GetObject(id, OpenMode.ForRead);
                    int n; counts.TryGetValue(l.Name, out n);
                    w(string.Format(C, "LAYER {0}: color {1} linetype {2} lw {3} off {4} frozen {5} plot {6} msEntities {7}", l.Name, l.Color, Name(tr, l.LinetypeObjectId), l.LineWeight, l.IsOff, l.IsFrozen, l.IsPlottable, n));
                }

                w("\n== MODEL SPACE ENTITY TYPES");
                foreach (var g in ms.Cast<ObjectId>().GroupBy(id => id.ObjectClass.DxfName).OrderByDescending(g => g.Count()))
                    w("TYPE " + g.Key + " " + g.Count());

                w("\n== MODEL SPACE ANNOTATION");
                foreach (ObjectId id in ms) Annotation(tr, id, w, "MS");

                w("\n== LAYOUTS");
                var layouts = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
                foreach (DBDictionaryEntry e in layouts)
                {
                    var lay = (Layout)tr.GetObject(e.Value, OpenMode.ForRead);
                    w(string.Format(C, "LAYOUT {0} tab {1}: device \"{2}\" media \"{3}\" styleSheet \"{4}\" paper {5:0.###}x{6:0.###} {7} margins L{8:0.###} B{9:0.###} R{10:0.###} T{11:0.###} rotation {12} plotType {13} scale {14} lw {15}",
                        e.Key, lay.TabOrder, lay.PlotConfigurationName, lay.CanonicalMediaName, lay.CurrentStyleSheet, lay.PlotPaperSize.X, lay.PlotPaperSize.Y, lay.PlotPaperUnits,
                        lay.PlotPaperMargins.MinPoint.X, lay.PlotPaperMargins.MinPoint.Y, lay.PlotPaperMargins.MaxPoint.X, lay.PlotPaperMargins.MaxPoint.Y, lay.PlotRotation, lay.PlotType,
                        lay.UseStandardScale ? lay.StdScaleType.ToString() : lay.CustomPrintScale.Numerator + "/" + lay.CustomPrintScale.Denominator, lay.PrintLineweights));
                    if (lay.ModelType) continue;
                    var space = (BlockTableRecord)tr.GetObject(lay.BlockTableRecordId, OpenMode.ForRead);
                    foreach (ObjectId id in space)
                    {
                        var vp = tr.GetObject(id, OpenMode.ForRead) as Viewport;
                        if (vp != null)
                        {
                            var frozen = vp.GetFrozenLayers().Cast<ObjectId>().Select(f => Name(tr, f)).ToList();
                            w(string.Format(C, "  VIEWPORT #{0} layer {1} center {2:0.###},{3:0.###} size {4:0.###}x{5:0.###} scale 1:{6:0.###} twist {7:0.####} locked {8} on {9} clip {10} viewCenter {11:0.###},{12:0.###} target {13} vpFrozen[{14}]: {15}",
                                vp.Number, vp.Layer, vp.CenterPoint.X, vp.CenterPoint.Y, vp.Width, vp.Height, vp.CustomScale > 0 ? 1 / vp.CustomScale : 0, vp.TwistAngle * 180 / Math.PI, vp.Locked, vp.On, vp.NonRectClipOn,
                                vp.ViewCenter.X, vp.ViewCenter.Y, vp.ViewTarget, frozen.Count, string.Join(", ", frozen.ToArray())));
                            continue;
                        }
                        Annotation(tr, id, w, "  PS");
                    }
                }
            }
            File.WriteAllText(r.StringResult, sb.ToString(), new UTF8Encoding(true));
            ed.WriteMessage("\nSTDDUMP: wrote " + r.StringResult + "\n");
        }

        private static void Annotation(Transaction tr, ObjectId id, Action<string> w, string where)
        {
            var e = tr.GetObject(id, OpenMode.ForRead) as Entity;
            if (e == null) return;
            var mt = e as MText;
            if (mt != null) { w(string.Format(C, "{0} MTEXT layer {1} style {2} h {3:0.###} rot {4:0.#} at {5:0.###},{6:0.###} attach {7} w {8:0.###} mask {9}: {10}", where, e.Layer, Name(tr, mt.TextStyleId), mt.TextHeight, mt.Rotation * 180 / Math.PI, mt.Location.X, mt.Location.Y, mt.Attachment, mt.Width, Mask(mt), Clip(mt.Contents))); return; }
            var t = e as DBText;
            if (t != null) { w(string.Format(C, "{0} TEXT layer {1} style {2} h {3:0.###} rot {4:0.#} at {5:0.###},{6:0.###} just {7}: {8}", where, e.Layer, Name(tr, t.TextStyleId), t.Height, t.Rotation * 180 / Math.PI, t.Position.X, t.Position.Y, t.Justify, Clip(t.TextString))); return; }
            var ml = e as MLeader;
            if (ml != null)
            {
                // Where the text sits and where the arrow lands: what tells a callout beside the
                // plan apart from one written across it.
                var at = ml.TextLocation;
                var arrow = "-";
                try { if (ml.LeaderCount > 0) arrow = string.Format(C, "{0:0.###},{1:0.###}", ml.GetFirstVertex(0).X, ml.GetFirstVertex(0).Y); }
                catch (Autodesk.AutoCAD.Runtime.Exception) { }
                w(string.Format(C, "{0} MLEADER layer {1} style {2} scale {3} arrow {4} text h {5} at {6:0.###},{7:0.###} points to {8} mask {9}: {10}",
                    where, e.Layer, Name(tr, ml.MLeaderStyle), ml.Scale, ml.ArrowSize, ml.MText == null ? 0 : ml.MText.TextHeight,
                    at.X, at.Y, arrow, ml.MText == null ? "-" : Mask(ml.MText), ml.MText == null ? "" : Clip(ml.MText.Contents)));
                return;
            }
            var ld = e as Leader;
            if (ld != null) { w(string.Format(C, "{0} LEADER layer {1} dimstyle {2} vertices {3}", where, e.Layer, Name(tr, ld.DimensionStyle), ld.NumVertices)); return; }
            var d = e as Dimension;
            if (d != null) { w(string.Format(C, "{0} DIM {1} layer {2} style {3} measure {4:0.###} text \"{5}\" txt {6} scale {7} lfac {8}", where, d.GetType().Name, e.Layer, Name(tr, d.DimensionStyle), d.Measurement, d.DimensionText, d.Dimtxt, d.Dimscale, d.Dimlfac)); return; }
            var tb = e as Table;
            if (tb != null)
            {
                var cells = new List<string>();
                for (var r = 0; r < Math.Min(tb.Rows.Count, 4); r++)
                    for (var c = 0; c < tb.Columns.Count; c++) cells.Add(tb.Cells[r, c].TextString);
                w(string.Format(C, "{0} TABLE layer {1} style {2} at {3:0.###},{4:0.###} rows {5} cols {6} size {7:0.###}x{8:0.###} rowH {9:0.###} textH {10:0.###}: {11}", where, e.Layer, Name(tr, tb.TableStyle), tb.Position.X, tb.Position.Y, tb.Rows.Count, tb.Columns.Count, tb.Width, tb.Height, tb.Rows.Count > 2 ? tb.Rows[2].Height : 0, tb.Rows.Count > 2 ? tb.Cells[2, 0].TextHeight ?? 0 : 0, Clip(string.Join(" | ", cells.ToArray()))));
                return;
            }
            var h = e as Hatch;
            if (h != null) { w(string.Format(C, "{0} HATCH layer {1} pattern {2} scale {3} angle {4:0.#} area {5:0.##} associative {6} color {7}", where, e.Layer, h.PatternName, h.PatternScale, h.PatternAngle * 180 / Math.PI, SafeArea(h), h.Associative, e.Color)); return; }
            var br = e as BlockReference;
            if (br != null)
            {
                var name = br.IsDynamicBlock ? Name(tr, br.DynamicBlockTableRecord) : br.Name;
                var atts = new List<string>();
                foreach (ObjectId a in br.AttributeCollection)
                {
                    var ar = (AttributeReference)tr.GetObject(a, OpenMode.ForRead);
                    atts.Add(ar.Tag + "=\"" + Clip(ar.IsMTextAttribute ? ar.MTextAttribute.Contents : ar.TextString) + "\"" + string.Format(C, " h{0:0.###}", ar.Height));
                }
                var ext = "";
                try { var g = br.GeometricExtents; ext = string.Format(C, " ext {0:0.##},{1:0.##}-{2:0.##},{3:0.##}", g.MinPoint.X, g.MinPoint.Y, g.MaxPoint.X, g.MaxPoint.Y); } catch (Autodesk.AutoCAD.Runtime.Exception) { }
                var dyn = new List<string>();
                if (br.IsDynamicBlock)
                    foreach (DynamicBlockReferenceProperty p in br.DynamicBlockReferencePropertyCollection)
                    {
                        var allowed = p.GetAllowedValues();
                        dyn.Add(p.PropertyName + "=" + Convert.ToString(p.Value, C) + (allowed != null && allowed.Length > 0 ? " {" + string.Join("|", allowed.Select(a => Convert.ToString(a, C)).ToArray()) + "}" : "") + (p.ReadOnly ? " (ro)" : ""));
                    }
                w(string.Format(C, "{0} INSERT {1} layer {2} at {3:0.###},{4:0.###} scale {5:0.###} rot {6:0.#}{7} ATTS: {8}{9}", where, name, e.Layer, br.Position.X, br.Position.Y, br.ScaleFactors.X, br.Rotation * 180 / Math.PI, ext, string.Join("; ", atts.ToArray()),
                    dyn.Count > 0 ? " DYN: " + string.Join("; ", dyn.ToArray()) : ""));
                return;
            }
            if (where.Trim() == "PS")
            {
                var extent = "";
                try { var g = e.GeometricExtents; extent = string.Format(C, " ext {0:0.##},{1:0.##}-{2:0.##},{3:0.##}", g.MinPoint.X, g.MinPoint.Y, g.MaxPoint.X, g.MaxPoint.Y); } catch (Autodesk.AutoCAD.Runtime.Exception) { }
                w(string.Format(C, "{0} {1} layer {2} lw {3} color {4}{5}", where, e.GetType().Name, e.Layer, e.LineWeight, e.Color, extent));
            }
        }

        /// <summary>Model-space geometry in a window: type, layer, handle and vertices. GEODUMP minX,minY,maxX,maxY file</summary>
        [CommandMethod("GEODUMP", CommandFlags.NoUndoMarker)]
        public void GeoDump()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;
            var db = doc.Database;
            var win = ed.GetString(new PromptStringOptions("\nWindow minX,minY,maxX,maxY: ") { AllowSpaces = false });
            var file = ed.GetString(new PromptStringOptions("\nOutput file: ") { AllowSpaces = true });
            if (win.Status != PromptStatus.OK || file.Status != PromptStatus.OK) return;
            var v = win.StringResult.Split(',').Select(s => double.Parse(s, C)).ToArray();
            var sb = new StringBuilder();
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    var e = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (e == null) continue;
                    Autodesk.AutoCAD.DatabaseServices.Extents3d g;
                    try { g = e.GeometricExtents; } catch (Autodesk.AutoCAD.Runtime.Exception) { continue; }
                    if (g.MaxPoint.X < v[0] || g.MinPoint.X > v[2] || g.MaxPoint.Y < v[1] || g.MinPoint.Y > v[3]) continue;
                    var line = e.GetType().Name + " " + e.Handle + " layer " + e.Layer;
                    var pl = e as Polyline;
                    if (pl != null)
                    {
                        var pts = new List<string>();
                        for (var i = 0; i < pl.NumberOfVertices; i++)
                        {
                            var p = pl.GetPoint2dAt(i);
                            var bulge = pl.GetBulgeAt(i);
                            pts.Add(string.Format(C, "{0:0.####},{1:0.####}{2}", p.X, p.Y, Math.Abs(bulge) > 1e-12 ? string.Format(C, "(b{0:0.######})", bulge) : ""));
                        }
                        line += (pl.Closed ? " closed" : "") + ": " + string.Join(" ", pts.ToArray());
                    }
                    var ln = e as Line;
                    if (ln != null) line += string.Format(C, ": {0:0.####},{1:0.####} {2:0.####},{3:0.####}", ln.StartPoint.X, ln.StartPoint.Y, ln.EndPoint.X, ln.EndPoint.Y);
                    var arc = e as Arc;
                    if (arc != null) line += string.Format(C, ": center {0:0.####},{1:0.####} r {2:0.####} {3:0.####}..{4:0.####}", arc.Center.X, arc.Center.Y, arc.Radius, arc.StartAngle * 180 / Math.PI, arc.EndAngle * 180 / Math.PI);
                    var mt = e as MText;
                    if (mt != null) line += string.Format(C, ": at {0:0.##},{1:0.##} {2}", mt.Location.X, mt.Location.Y, Clip(mt.Contents));
                    var h = e as Hatch;
                    if (h != null) line += " pattern " + h.PatternName;
                    if (e.GetType().Name == "CogoPoint" || id.ObjectClass.DxfName == "AECC_COGO_POINT")
                    {
                        var loc = e.GetType().GetProperty("Location");
                        var num = e.GetType().GetProperty("PointNumber");
                        var desc = e.GetType().GetProperty("RawDescription");
                        line += " point " + (num == null ? "" : Convert.ToString(num.GetValue(e, null), C)) + " " + (loc == null ? "" : Convert.ToString(loc.GetValue(e, null), C)) + " " + (desc == null ? "" : Convert.ToString(desc.GetValue(e, null), C));
                    }
                    var br = e as BlockReference;
                    if (br != null) line += string.Format(C, " block {0} at {1:0.###},{2:0.###}", br.Name, br.Position.X, br.Position.Y);
                    sb.AppendLine(line);
                }
            }
            File.WriteAllText(file.StringResult, sb.ToString(), new UTF8Encoding(true));
            ed.WriteMessage("\nGEODUMP: wrote " + file.StringResult + "\n");
        }

        [CommandMethod("BLKDUMP", CommandFlags.NoUndoMarker)]
        public void BlockDump()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;
            var db = doc.Database;
            var names = ed.GetString(new PromptStringOptions("\nBlock names (comma separated): ") { AllowSpaces = false });
            var file = ed.GetString(new PromptStringOptions("\nOutput file: ") { AllowSpaces = true });
            if (names.Status != PromptStatus.OK || file.Status != PromptStatus.OK) return;
            var sb = new StringBuilder();
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                foreach (var name in names.StringResult.Split(','))
                {
                    if (!bt.Has(name)) { sb.AppendLine("BLOCK " + name + ": not in drawing"); continue; }
                    var b = (BlockTableRecord)tr.GetObject(bt[name], OpenMode.ForRead);
                    sb.AppendLine("BLOCK " + name + " origin " + b.Origin + " units " + b.Units + " annotative " + b.Annotative);
                    foreach (ObjectId id in b)
                    {
                        var e = (Entity)tr.GetObject(id, OpenMode.ForRead);
                        string ext = "";
                        try { var g = e.GeometricExtents; ext = string.Format(C, " ext {0:0.###},{1:0.###}-{2:0.###},{3:0.###}", g.MinPoint.X, g.MinPoint.Y, g.MaxPoint.X, g.MaxPoint.Y); } catch (Autodesk.AutoCAD.Runtime.Exception) { }
                        var line = "  " + e.GetType().Name + " layer " + e.Layer + ext;
                        var ad = e as AttributeDefinition;
                        if (ad != null)
                        {
                            line += " ATTDEF " + ad.Tag + " = \"" + ad.TextString + "\" h " + ad.Height.ToString(C) + " style " + Name(tr, ad.TextStyleId) + " mtext " + ad.IsMTextAttributeDefinition;
                            if (ad.HasFields) { try { line += " FIELD " + ((Field)tr.GetObject(ad.GetField("TEXT"), OpenMode.ForRead)).GetFieldCode(); } catch (System.Exception ex) { line += " FIELD ? " + ex.Message; } }
                        }
                        var mt = e as MText;
                        if (mt != null)
                        {
                            line += " MTEXT h " + mt.TextHeight.ToString(C) + " style " + Name(tr, mt.TextStyleId) + ": " + Clip(mt.Contents);
                            if (mt.HasFields) { try { line += " FIELD " + ((Field)tr.GetObject(mt.GetField("TEXT"), OpenMode.ForRead)).GetFieldCode(); } catch (System.Exception ex) { line += " FIELD ? " + ex.Message; } }
                        }
                        var t = e as DBText;
                        if (t != null && ad == null) line += " TEXT h " + t.Height.ToString(C) + " style " + Name(tr, t.TextStyleId) + ": " + Clip(t.TextString);
                        var br = e as BlockReference;
                        if (br != null) line += " INSERT " + br.Name + " at " + br.Position;
                        sb.AppendLine(line);
                    }
                    if (b.IsDynamicBlock)
                    {
                        // Dynamic parameters show on a reference: a temporary one in this work copy, erased again.
                        using (var wt = db.TransactionManager.StartTransaction())
                        {
                            var ms = (BlockTableRecord)wt.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                            var r = new BlockReference(Point3d.Origin, b.ObjectId);
                            ms.AppendEntity(r);
                            wt.AddNewlyCreatedDBObject(r, true);
                            foreach (DynamicBlockReferenceProperty p in r.DynamicBlockReferencePropertyCollection)
                            {
                                var allowed = p.GetAllowedValues();
                                sb.AppendLine("  DYN " + p.PropertyName + " type " + p.PropertyTypeCode + " readonly " + p.ReadOnly + " value " + Convert.ToString(p.Value, C) +
                                              " unit " + p.UnitsType + (allowed != null && allowed.Length > 0 ? " allowed [" + string.Join("; ", allowed.Select(a => Convert.ToString(a, C)).ToArray()) + "]" : string.Empty) +
                                              " desc " + p.Description);
                            }
                            r.Erase();
                            wt.Commit();
                        }
                    }
                }
            }
            File.WriteAllText(file.StringResult, sb.ToString(), new UTF8Encoding(true));
            ed.WriteMessage("\nBLKDUMP: wrote " + file.StringResult + "\n");
        }

        private static double SafeArea(Hatch h) { try { return h.Area; } catch (Autodesk.AutoCAD.Runtime.Exception) { return -1; } }
        /// <summary>An MText's background mask: off, or on with its size factor and fill colour.</summary>
        private static string Mask(MText mt)
        {
            if (mt == null || !mt.BackgroundFill) return "False";
            var colour = mt.UseBackgroundColor ? "drawing background" : (mt.BackgroundFillColor == null ? "?" : mt.BackgroundFillColor.ColorNameForDisplay);
            return string.Format(C, "True x{0:0.##} {1}", mt.BackgroundScaleFactor, colour);
        }

        private static string Clip(string s) { s = (s ?? "").Replace("\r", " ").Replace("\n", " "); return s.Length > 160 ? s.Substring(0, 160) + "..." : s; }

        private static string Name(Transaction tr, ObjectId id)
        {
            if (id.IsNull || !id.IsValid) return "(none)";
            var o = tr.GetObject(id, OpenMode.ForRead);
            var s = o as SymbolTableRecord;
            if (s != null) return s.Name;
            var ml = o as MLeaderStyle;
            if (ml != null) return ml.Name;
            var ts = o as TableStyle;
            if (ts != null) return ts.Name;
            return o.GetType().Name;
        }

        private static string BlockName(Transaction tr, ObjectId id) { return id.IsNull ? "(default)" : Name(tr, id); }
    }
}
