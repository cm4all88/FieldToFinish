// Harness for production-drawing tests of FTF exhibits. Runs in accoreconsole on COPIES of real
// office drawings. Nothing here changes survey geometry.
//
// RWERASE   erases listed objects by handle (a drafter's hand-drawn exhibit annotation, to rebuild the
//           state before the exhibit was drafted -- never survey linework).
// RWCLOCK   writes a timestamp line, so command durations can be read from the log.
// RWREPORT  writes every FTF exhibit in the drawing: layout, scale, items with sheet boxes, review, QA.
//
// Compiled with the .NET Framework csc (C# 5 only).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;

[assembly: CommandClass(typeof(FtfRealWorld.RealWorldHarness))]

namespace FtfRealWorld
{
    public class RealWorldHarness
    {
        private static readonly CultureInfo C = CultureInfo.InvariantCulture;

        static RealWorldHarness()
        {
            AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
            {
                var name = new AssemblyName(e.Name).Name;
                var loaded = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == name);
                if (loaded != null) return loaded;
                var plugin = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "FieldCodes.Cad");
                if (plugin == null) return null;
                var path = Path.Combine(Path.GetDirectoryName(plugin.Location), name + ".dll");
                return File.Exists(path) ? Assembly.LoadFrom(path) : null;
            };
        }

        [CommandMethod("RWERASE")]
        public void Erase()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var r = doc.Editor.GetString(new PromptStringOptions("\nHandles (comma separated): ") { AllowSpaces = false });
            if (r.Status != PromptStatus.OK) return;
            var erased = 0;
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                foreach (var h in r.StringResult.Split(','))
                {
                    long value;
                    if (!long.TryParse(h.Trim(), NumberStyles.HexNumber, C, out value)) continue;
                    ObjectId id;
                    if (!doc.Database.TryGetObjectId(new Handle(value), out id) || id.IsErased) { doc.Editor.WriteMessage("\nRWERASE: no object " + h); continue; }
                    var e = (Entity)tr.GetObject(id, OpenMode.ForWrite);
                    doc.Editor.WriteMessage("\nRWERASE: " + h + " " + e.GetType().Name + " on " + e.Layer);
                    e.Erase();
                    erased++;
                }
                tr.Commit();
            }
            doc.Editor.WriteMessage("\nRWERASE: " + erased + " object(s) erased.\n");
        }

        [CommandMethod("RWCLOCK", CommandFlags.NoUndoMarker)]
        public void Clock()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var r = doc.Editor.GetString(new PromptStringOptions("\nLabel: ") { AllowSpaces = false });
            doc.Editor.WriteMessage("\nRWCLOCK " + (r.Status == PromptStatus.OK ? r.StringResult : "") + " " + DateTime.Now.ToString("HH:mm:ss.fff", C) + "\n");
        }

        [CommandMethod("RWREPORT", CommandFlags.NoUndoMarker)]
        public void Report()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var r = doc.Editor.GetString(new PromptStringOptions("\nOutput file: ") { AllowSpaces = true });
            if (r.Status != PromptStatus.OK) return;
            var sb = new StringBuilder();
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
                if (!nod.Contains("FTF_FIELDTOFINISH")) { sb.AppendLine("no FTF data"); }
                else
                {
                    var root = (DBDictionary)tr.GetObject(nod.GetAt("FTF_FIELDTOFINISH"), OpenMode.ForRead);
                    foreach (var store in new[] { "EASEMENTS", "EXHIBITS" })
                    {
                        if (!root.Contains(store)) continue;
                        var dict = (DBDictionary)tr.GetObject(root.GetAt(store), OpenMode.ForRead);
                        foreach (DBDictionaryEntry entry in dict)
                        {
                            var json = Join(tr, dict, entry.Key);
                            if (store == "EASEMENTS")
                            {
                                var e = FieldCodes.Easements.EasementRecord.FromJson(json);
                                sb.AppendLine(string.Format(C, "EASEMENT {0} area {1:0.##} display {2:0.##} courses {3} warnings: {4}", e.Title, e.AreaSquareFeet, e.DisplayAreaSquareFeet,
                                    e.BoundaryCourses == null ? 0 : e.BoundaryCourses.Count, string.Join(" | ", (e.Warnings ?? new List<string>()).ToArray())));
                                foreach (var s in e.RouteSources ?? new List<FieldCodes.Easements.GeometrySource>()) sb.AppendLine("  source " + s.Role + " " + s.EntityType + " " + s.Handle);
                                // Geometry, so two runs can be compared course by course.
                                if (e.PointOfCommencement != null) sb.AppendLine(string.Format(C, "  poc {0:0.000},{1:0.000}", e.PointOfCommencement.X, e.PointOfCommencement.Y));
                                if (e.CommencementTie != null) sb.AppendLine("  tie " + CourseText(e.CommencementTie));
                                foreach (var d in TieList(e)) sb.AppendLine("  tiecourse " + CourseText(d));
                                foreach (var d in e.RouteCourses ?? new List<FieldCodes.Easements.CourseData>()) sb.AppendLine("  route " + CourseText(d));
                                foreach (var d in e.BoundaryCourses ?? new List<FieldCodes.Easements.CourseData>()) sb.AppendLine("  boundary " + CourseText(d));
                                foreach (var hatch in HatchesOf(tr, db, e.Id)) sb.AppendLine("  hatch " + hatch);
                                var lotLines = typeof(FieldCodes.Easements.EasementRecord).GetProperty("LotLines");
                                var lines = lotLines == null ? null : lotLines.GetValue(e, null) as System.Collections.ICollection;
                                if (lines != null) sb.AppendLine("  lot lines " + lines.Count);
                                continue;
                            }
                            var x = FieldCodes.Exhibits.ExhibitRecord.FromJson(json);
                            sb.AppendLine(string.Format(C, "EXHIBIT {0}: 1\" = {1}' rotation {2:0.#} profile {3} items {4} QA: {5}", x.LayoutName, x.Scale, x.RotationDegrees, x.ProfileName, x.Items.Count, x.QaSummary));
                            sb.AppendLine("  north arrow checked: " + NorthVerified(x) + "; stamp block: " + (StampBlock(x) ?? "(none chosen)"));
                            foreach (var item in x.Items)
                            {
                                long value;
                                ObjectId id;
                                string box = "(missing)";
                                if (item.Handle != null && long.TryParse(item.Handle, NumberStyles.HexNumber, C, out value) && db.TryGetObjectId(new Handle(value), out id) && !id.IsErased)
                                {
                                    var ent = (Entity)tr.GetObject(id, OpenMode.ForRead);
                                    try { var g = ent.GeometricExtents; box = string.Format(C, "{0:0.00},{1:0.00}-{2:0.00},{3:0.00} {4} on {5}", g.MinPoint.X, g.MinPoint.Y, g.MaxPoint.X, g.MaxPoint.Y, ent.GetType().Name, ent.Layer); }
                                    catch (Autodesk.AutoCAD.Runtime.Exception) { box = ent.GetType().Name + " on " + ent.Layer; }
                                    var br = ent as BlockReference;
                                    if (br != null)
                                    {
                                        foreach (ObjectId a in br.AttributeCollection)
                                        {
                                            var ar = (AttributeReference)tr.GetObject(a, OpenMode.ForRead);
                                            box += " [" + ar.Tag + "=" + ar.TextString + "]";
                                        }
                                        if (br.IsDynamicBlock)
                                            foreach (DynamicBlockReferenceProperty p in br.DynamicBlockReferencePropertyCollection)
                                                if (p.PropertyName.StartsWith("Visibility", StringComparison.Ordinal) || p.PropertyName.StartsWith("Angle", StringComparison.Ordinal))
                                                    box += " {" + p.PropertyName + "=" + (p.Value is double ? ((double)p.Value * 180 / Math.PI).ToString("0.###", C) + " deg" : Convert.ToString(p.Value, C)) + "}";
                                    }
                                }
                                sb.AppendLine("  item " + item.Key + " [" + item.Kind + "] " + box + " : " + (item.Text ?? string.Empty).Replace("\n", " "));
                            }
                            foreach (var v in x.Review) sb.AppendLine("  review " + v);
                            foreach (var kv in x.ViewportLayers ?? new Dictionary<string, bool>()) sb.AppendLine("  vplayer " + kv.Key + " frozen=" + kv.Value);
                        }
                    }
                }
            }
            File.WriteAllText(r.StringResult, sb.ToString(), new UTF8Encoding(true));
            doc.Editor.WriteMessage("\nRWREPORT: wrote " + r.StringResult + "\n");
        }

        /// <summary>
        /// RWTWIST: turns an exhibit's viewport view by hand, as a drafter would (the view centre and scale kept), so a
        /// rebuild keeps the turn and must show north correctly.
        /// </summary>
        [CommandMethod("RWTWIST")]
        public void Twist()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var layout = doc.Editor.GetString(new PromptStringOptions("\nExhibit layout: ") { AllowSpaces = true });
            var degrees = doc.Editor.GetString("\nView turn (degrees counter-clockwise): ");
            double turn;
            if (layout.Status != PromptStatus.OK || degrees.Status != PromptStatus.OK || !double.TryParse(degrees.StringResult, NumberStyles.Float, C, out turn)) return;
            LayoutManager.Current.CurrentLayout = layout.StringResult.Trim();
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var x = Exhibits(tr, doc.Database).First(e => string.Equals(e.LayoutName, layout.StringResult.Trim(), StringComparison.OrdinalIgnoreCase));
                long value;
                ObjectId id;
                if (!long.TryParse(x.ViewportHandle, NumberStyles.HexNumber, C, out value) || !doc.Database.TryGetObjectId(new Handle(value), out id)) return;
                var vp = (Viewport)tr.GetObject(id, OpenMode.ForWrite);
                vp.Locked = false;
                var a = turn * Math.PI / 180;
                var c = x.ViewCenter;
                vp.TwistAngle = a;
                vp.ViewCenter = new Autodesk.AutoCAD.Geometry.Point2d(c.X * Math.Cos(a) - c.Y * Math.Sin(a), c.X * Math.Sin(a) + c.Y * Math.Cos(a));
                tr.Commit();
            }
            LayoutManager.Current.CurrentLayout = "Model";
            doc.Editor.WriteMessage("\nRWTWIST: the viewport view is turned " + turn.ToString(C) + " degrees by hand.\n");
        }

        /// <summary>RWPNG: plots an exhibit layout to a PNG to look at (PublishToWeb PNG, scaled to fit), with the office plot styles.</summary>
        [CommandMethod("RWPNG")]
        public void Png()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var layoutName = doc.Editor.GetString(new PromptStringOptions("\nLayout: ") { AllowSpaces = true });
            var file = doc.Editor.GetString(new PromptStringOptions("\nPNG file: ") { AllowSpaces = true });
            if (layoutName.Status != PromptStatus.OK || file.Status != PromptStatus.OK) return;
            try
            {
                LayoutManager.Current.CurrentLayout = layoutName.StringResult.Trim();
                Application.SetSystemVariable("BACKGROUNDPLOT", 0);
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    var layouts = (DBDictionary)tr.GetObject(doc.Database.LayoutDictionaryId, OpenMode.ForRead);
                    var lay = (Layout)tr.GetObject(layouts.GetAt(layoutName.StringResult.Trim()), OpenMode.ForRead);
                    using (var ps = new PlotSettings(lay.ModelType))
                    {
                        var step = "copy";
                        ps.CopyFrom(lay);
                        var v = PlotSettingsValidator.Current;
                        try
                        {
                            step = "device";
                            v.SetPlotConfigurationName(ps, "PublishToWeb PNG.pc3", null);
                            v.RefreshLists(ps);
                            var media = v.GetCanonicalMediaNameList(ps).Cast<string>().ToList();
                            var big = media.FirstOrDefault(m => m.Contains("1600.00_x_1280.00")) ?? media.FirstOrDefault(m => m.Contains("1280")) ?? media.Last();
                            step = "media " + big;
                            v.SetPlotConfigurationName(ps, "PublishToWeb PNG.pc3", big);
                            step = "units";
                            v.SetPlotPaperUnits(ps, PlotPaperUnit.Pixels);
                            step = "type";
                            v.SetPlotType(ps, Autodesk.AutoCAD.DatabaseServices.PlotType.Extents);
                            step = "scale";
                            v.SetUseStandardScale(ps, true);
                            v.SetStdScaleType(ps, StdScaleType.ScaleToFit);
                            step = "center";
                            v.SetPlotCentered(ps, true);
                            step = "rotation";
                            v.SetPlotRotation(ps, PlotRotation.Degrees000);
                            step = "styles";
                            if (v.GetPlotStyleSheetList().Cast<string>().Contains("PMX Survey BW.ctb")) v.SetCurrentStyleSheet(ps, "PMX Survey BW.ctb");
                        }
                        catch (System.Exception ex)
                        {
                            throw new InvalidOperationException("at " + step + ": " + ex.Message);
                        }
                        var info = new Autodesk.AutoCAD.PlottingServices.PlotInfo { Layout = lay.ObjectId, OverrideSettings = ps };
                        new Autodesk.AutoCAD.PlottingServices.PlotInfoValidator { MediaMatchingPolicy = Autodesk.AutoCAD.PlottingServices.MatchingPolicy.MatchEnabled }.Validate(info);
                        using (var engine = Autodesk.AutoCAD.PlottingServices.PlotFactory.CreatePublishEngine())
                        {
                            engine.BeginPlot(null, null);
                            engine.BeginDocument(info, doc.Name, null, 1, true, file.StringResult.Trim());
                            engine.BeginPage(new Autodesk.AutoCAD.PlottingServices.PlotPageInfo(), info, true, null);
                            engine.BeginGenerateGraphics(null);
                            engine.EndGenerateGraphics(null);
                            engine.EndPage(null);
                            engine.EndDocument(null);
                            engine.EndPlot(null);
                        }
                    }
                    tr.Commit();
                }
                doc.Editor.WriteMessage("\nRWPNG: wrote " + file.StringResult.Trim() + "\n");
            }
            catch (System.Exception ex)
            {
                doc.Editor.WriteMessage("\nRWPNG: " + ex.Message + "\n");
            }
            finally
            {
                try { LayoutManager.Current.CurrentLayout = "Model"; } catch (System.Exception) { }
            }
        }

        private static List<FieldCodes.Exhibits.ExhibitRecord> Exhibits(Transaction tr, Database db)
        {
            var list = new List<FieldCodes.Exhibits.ExhibitRecord>();
            var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
            if (!nod.Contains("FTF_FIELDTOFINISH")) return list;
            var root = (DBDictionary)tr.GetObject(nod.GetAt("FTF_FIELDTOFINISH"), OpenMode.ForRead);
            if (!root.Contains("EXHIBITS")) return list;
            var dict = (DBDictionary)tr.GetObject(root.GetAt("EXHIBITS"), OpenMode.ForRead);
            foreach (DBDictionaryEntry entry in dict) list.Add(FieldCodes.Exhibits.ExhibitRecord.FromJson(Join(tr, dict, entry.Key)));
            return list;
        }

        private static string NorthVerified(FieldCodes.Exhibits.ExhibitRecord x)
        {
            var p = typeof(FieldCodes.Exhibits.ExhibitRecord).GetProperty("NorthArrowVerified");
            return p == null ? "n/a" : Convert.ToString(p.GetValue(x, null), C);
        }

        private static string StampBlock(FieldCodes.Exhibits.ExhibitRecord x)
        {
            var p = typeof(FieldCodes.Exhibits.ExhibitRecord).GetProperty("StampBlock");
            return p == null ? null : p.GetValue(x, null) as string;
        }

        /// <summary>The easement's model-space hatches: pattern, pattern scale and line spacing (drawing units).</summary>
        private static IEnumerable<string> HatchesOf(Transaction tr, Database db, string id)
        {
            var list = new List<string>();
            var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
            foreach (ObjectId oid in ms)
            {
                if (oid.ObjectClass.DxfName != "HATCH") continue;
                var h = (Hatch)tr.GetObject(oid, OpenMode.ForRead);
                using (var rb = h.GetXDataForApplication("FTF_FIELDTOFINISH"))
                {
                    if (rb == null || !rb.AsArray().Any(v => v.Value is string && (string)v.Value == id)) continue;
                }
                var spacing = double.MaxValue;
                for (var i = 0; i < h.NumberOfPatternDefinitions; i++)
                {
                    var d = h.GetPatternDefinitionAt(i);
                    var across = Math.Abs(-Math.Sin(d.Angle) * d.OffsetX + Math.Cos(d.Angle) * d.OffsetY);
                    if (across > 1e-9) spacing = Math.Min(spacing, across);
                }
                list.Add(string.Format(C, "{0} {1} scale {2:0.####} spacing {3:0.####}", h.Handle, h.PatternName, h.PatternScale, spacing));
            }
            return list;
        }

        private static string CourseText(FieldCodes.Easements.CourseData d)
        {
            var c = d.Course;
            return string.Format(C, "{0} {1:0.000},{2:0.000} -> {3:0.000},{4:0.000} len {5:0.00} az {6:0.0000}{7}", c.Kind, c.Start.X, c.Start.Y, c.End.X, c.End.Y, d.Length, d.AzimuthDegrees,
                                 c.Kind == FieldCodes.Easements.CourseKind.Arc ? string.Format(C, " r {0:0.00} delta {1:0.0000}", d.Radius ?? 0, d.DeltaDegrees ?? 0) : string.Empty);
        }

        /// <summary>The tie courses, read by reflection so the harness also reads drawings made before curved ties.</summary>
        private static IEnumerable<FieldCodes.Easements.CourseData> TieList(FieldCodes.Easements.EasementRecord e)
        {
            var p = typeof(FieldCodes.Easements.EasementRecord).GetProperty("CommencementTieCourses");
            var list = p == null ? null : p.GetValue(e, null) as IEnumerable<FieldCodes.Easements.CourseData>;
            return list ?? new List<FieldCodes.Easements.CourseData>();
        }

        private static string Join(Transaction tr, DBDictionary dict, string key)
        {
            var xr = (Xrecord)tr.GetObject(dict.GetAt(key), OpenMode.ForRead);
            var sb = new StringBuilder();
            foreach (TypedValue tv in xr.Data) if (tv.TypeCode == (int)DxfCode.Text || tv.TypeCode == 1 || tv.TypeCode == 300 || tv.TypeCode == 301 || tv.TypeCode == 302) sb.Append(Convert.ToString(tv.Value, C));
            return sb.ToString();
        }
    }
}
