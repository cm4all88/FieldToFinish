using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using FieldCodes.Geometry;
using FieldCodes.Stamps;

using AcEntity = Autodesk.AutoCAD.DatabaseServices.Entity;
using CogoPoint = Autodesk.Civil.DatabaseServices.CogoPoint;
using FeatureLine = Autodesk.Civil.DatabaseServices.FeatureLine;
using SurveyFigure = Autodesk.Civil.DatabaseServices.SurveyFigure;

namespace FieldCodes.Cad
{
    /// <summary>
    /// FTFWHAT: what an object is and what made it. For each selected object: its type, its
    /// layer (and whether that layer plots), and its owner -- a Civil 3D object (a COGO point, its
    /// style and label style, a survey figure), an FTF object with the command that made it, a
    /// copy someone made of FTF output, or neither (drawn by hand or brought in). For a COGO point
    /// it also says whether FTFORIENT turned its marker, from what, and whether a drafter set it.
    /// With nothing selected, it counts the whole drawing by owner. It changes nothing.
    /// </summary>
    public sealed class OwnerCommands
    {
        [CommandMethod("FTFWHAT", CommandFlags.Modal | CommandFlags.UsePickSet)]
        public void What()
        {
            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;
            var sel = ed.SelectImplied();
            if (sel.Status != PromptStatus.OK)
            {
                var ask = new PromptSelectionOptions { MessageForAdding = "\nSelect objects to identify (Enter for a count of the whole drawing): " };
                sel = ed.GetSelection(ask);
            }
            var ids = sel.Status == PromptStatus.OK && sel.Value != null ? sel.Value.GetObjectIds() : null;

            FtfSession.Run("FTFWHAT", (db, tr, e) =>
            {
                if (ids == null || ids.Length == 0) { Inventory(db, tr, e); return; }
                foreach (var id in ids.Take(50))
                {
                    var entity = tr.GetObject(id, OpenMode.ForRead, false, true) as AcEntity;
                    if (entity == null) continue;
                    foreach (var line in Describe(db, tr, entity)) e.WriteMessage("\n" + line);
                }
                if (ids.Length > 50) e.WriteMessage("\n... and {0} more (select fewer to see them).", ids.Length - 50);
                e.WriteMessage("\n");
            });
        }

        internal static IList<string> Describe(Database db, Transaction tr, AcEntity entity)
        {
            var lines = new List<string>();
            var layer = entity.Layer;
            var plots = LayerPlots(tr, entity.LayerId);
            lines.Add(string.Format(CultureInfo.InvariantCulture, "{0} on layer {1}{2} (handle {3})",
                                    TypeName(entity), layer, plots ? "" : " (does not plot)", entity.Handle));

            var cogo = entity as CogoPoint;
            if (cogo != null)
            {
                lines.Add("  owner:  Civil 3D: COGO point " + cogo.PointNumber.ToString(CultureInfo.InvariantCulture) +
                          ", description \"" + (cogo.RawDescription ?? "") + "\"" +
                          "; symbol from point style " + StyleName(tr, cogo.StyleId) + ", label from point label style " + StyleName(tr, cogo.LabelStyleId) +
                          " (FTF never creates or erases COGO points)");
                var record = OrientationCommands.Record.Read(cogo);
                var now = Angles.ToDegrees(Angles.NormalizeRadians(cogo.MarkerRotation));
                if (record == null)
                    lines.Add(string.Format(CultureInfo.InvariantCulture, "  marker: {0:0.0} deg, not turned by FTFORIENT", now));
                else if (record.Manual)
                    lines.Add(string.Format(CultureInfo.InvariantCulture, "  marker: {0:0.0} deg, set by a drafter with FTFORIENTPOINT (kept by FTFORIENT); {1:0.0} before FTF",
                                            now, Angles.ToDegrees(Angles.NormalizeRadians(record.OriginalMarker))));
                else
                    lines.Add(string.Format(CultureInfo.InvariantCulture, "  marker: {0:0.0} deg; FTFORIENT set {1:0.0} ({2}, confidence {3:0.00}{4}); {5:0.0} before FTF{6}",
                                            now, Angles.ToDegrees(Angles.NormalizeRadians(record.Applied)), record.Tier, record.Confidence,
                                            string.IsNullOrEmpty(record.Association) ? "" : ", " + record.Association,
                                            Angles.ToDegrees(Angles.NormalizeRadians(record.OriginalMarker)),
                                            Math.Abs(Angles.NormalizeRadians(cogo.MarkerRotation) - Angles.NormalizeRadians(record.Applied)) > 1e-6 ? "; turned by hand since" : ""));
                lines.Add("  why:    FTFORIENTPOINT " + cogo.PointNumber.ToString(CultureInfo.InvariantCulture) + " shows the decision and its evidence");
                return lines;
            }

            var stamp = Ownership.Read(entity);
            if (stamp != null)
            {
                lines.Add("  owner:  FieldToFinish: " + OwnerNames.For(stamp.Kind.ToString()) +
                          (string.IsNullOrEmpty(stamp.PointNumber) ? "" : "; for point " + stamp.PointNumber) +
                          (string.IsNullOrEmpty(stamp.RulesVersion) ? "" : "; rules " + stamp.RulesVersion));
                return lines;
            }
            if (Ownership.IsUserCopy(entity))
            {
                var copied = Ownership.ReadIncludingCopies(entity);
                lines.Add("  owner:  a copy someone made of FTF output (" + OwnerNames.For(copied != null ? copied.Kind.ToString() : null) +
                          "); FTF no longer manages it and never erases it");
                return lines;
            }
            if (entity is SurveyFigure) { lines.Add("  owner:  Civil 3D: survey figure " + ((SurveyFigure)entity).Name + " (survey linework; FTF only reads it)"); return lines; }
            if (entity is FeatureLine) { lines.Add("  owner:  Civil 3D: feature line " + ((FeatureLine)entity).Name + " (FTF only reads it)"); return lines; }
            if (entity is Autodesk.Civil.DatabaseServices.Label) { lines.Add("  owner:  Civil 3D: a label object, shown by its own Civil 3D label style"); return lines; }
            if (entity.GetType().Namespace != null && entity.GetType().Namespace.StartsWith("Autodesk.Civil", StringComparison.Ordinal))
            {
                lines.Add("  owner:  Civil 3D: " + entity.GetType().Name);
                return lines;
            }
            lines.Add("  owner:  not made by FieldToFinish (no FTF stamp): an original drawing object, drawn by hand, or from another program");
            return lines;
        }

        private static void Inventory(Database db, Transaction tr, Editor ed)
        {
            var ms = CadUtil.ModelSpace(db, tr, OpenMode.ForRead);
            var byKind = new Dictionary<string, int>(StringComparer.Ordinal);
            int total = 0, cogo = 0, turned = 0, manual = 0, copies = 0, civil = 0, other = 0;
            foreach (ObjectId id in ms)
            {
                if (id.IsErased) continue;
                var entity = tr.GetObject(id, OpenMode.ForRead, false, true) as AcEntity;
                if (entity == null) continue;
                total++;
                var point = entity as CogoPoint;
                if (point != null)
                {
                    cogo++;
                    var record = OrientationCommands.Record.Read(point);
                    if (record != null) { if (record.Manual) manual++; else turned++; }
                    continue;
                }
                var stamp = Ownership.Read(entity);
                if (stamp != null)
                {
                    var key = stamp.Kind.ToString();
                    int n;
                    byKind.TryGetValue(key, out n);
                    byKind[key] = n + 1;
                    continue;
                }
                if (Ownership.IsUserCopy(entity)) { copies++; continue; }
                if (entity.GetType().Namespace != null && entity.GetType().Namespace.StartsWith("Autodesk.Civil", StringComparison.Ordinal)) { civil++; continue; }
                other++;
            }
            ed.WriteMessage("\nFTFWHAT: {0} object(s) in model space.", total);
            ed.WriteMessage("\n  Civil 3D: {0} COGO point(s) ({1} marker(s) turned by FTFORIENT, {2} set by a drafter), {3} other Civil 3D object(s).",
                            cogo, turned, manual, civil);
            var ftf = byKind.Values.Sum();
            ed.WriteMessage("\n  FieldToFinish: {0} object(s){1}", ftf, ftf > 0 ? ":" : ".");
            foreach (var kv in byKind.OrderByDescending(k => k.Value))
                ed.WriteMessage("\n    {0,6}  {1}", kv.Value, OwnerNames.For(kv.Key));
            if (copies > 0) ed.WriteMessage("\n  Copies someone made of FTF output (no longer FTF's): {0}.", copies);
            ed.WriteMessage("\n  Not made by FieldToFinish or Civil 3D (original or hand-drawn): {0}.", other);
            ed.WriteMessage("\nSelect objects with FTFWHAT to see each one's owner.\n");
        }

        private static string TypeName(AcEntity e)
        {
            if (e is CogoPoint) return "COGO point";
            if (e is SurveyFigure) return "Survey figure";
            if (e is FeatureLine) return "Feature line";
            if (e is MText) return "MText";
            if (e is DBText) return "Text";
            if (e is MLeader) return "Multileader";
            var block = e as BlockReference;
            if (block != null) return "Block " + BlockName(block);
            return e.GetType().Name;
        }

        private static string BlockName(BlockReference b)
        {
            try { return b.Name; }
            catch (Autodesk.AutoCAD.Runtime.Exception) { return "(unnamed)"; }
        }

        private static string StyleName(Transaction tr, ObjectId id)
        {
            if (id.IsNull) return "(none)";
            try
            {
                var style = tr.GetObject(id, OpenMode.ForRead, false, true) as Autodesk.Civil.DatabaseServices.Styles.StyleBase;
                var name = style != null ? style.Name : null;
                return string.IsNullOrEmpty(name) ? "(unnamed)" : "\"" + name + "\"";
            }
            catch (Autodesk.AutoCAD.Runtime.Exception) { return "(unreadable)"; }
        }

        private static bool LayerPlots(Transaction tr, ObjectId layerId)
        {
            try
            {
                var layer = tr.GetObject(layerId, OpenMode.ForRead) as LayerTableRecord;
                return layer == null || layer.IsPlottable;
            }
            catch (Autodesk.AutoCAD.Runtime.Exception) { return true; }
        }
    }
}
