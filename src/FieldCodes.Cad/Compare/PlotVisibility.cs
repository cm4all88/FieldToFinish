using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.Civil.DatabaseServices;

using AcEntity = Autodesk.AutoCAD.DatabaseServices.Entity;

namespace FieldCodes.Cad.Compare
{
    /// <summary>
    /// Whether an object will actually appear on a plot, resolved from everything that
    /// decides it: its own visibility, its layer, the layers a block definition draws
    /// on (recursively, with layer 0 following the insert), block attributes, and a COGO
    /// point's style and label. Used right after every change to prove it took, and by
    /// the final QC. "Unknown" is a real answer here and is treated as a failure by both.
    ///
    /// UNTESTED in Civil 3D.
    /// </summary>
    internal static class PlotVisibility
    {
        public sealed class Verdict
        {
            public bool? Plots;
            public string Note;
            public static Verdict Yes(string note) { return new Verdict { Plots = true, Note = note }; }
            public static Verdict No(string note) { return new Verdict { Plots = false, Note = note }; }
            public static Verdict Unknown(string note) { return new Verdict { Plots = null, Note = note }; }
        }

        private sealed class LayerFacts { public bool Exists, Plottable, Frozen, Off; }

        private static Transaction _cacheOwner;
        private static readonly Dictionary<string, LayerFacts> LayerCache = new Dictionary<string, LayerFacts>(StringComparer.OrdinalIgnoreCase);

        public static Verdict Of(AcEntity entity, Database db, Transaction tr)
        {
            if (entity == null) return Verdict.No("erased");
            try
            {
                if (!entity.Visible) return Verdict.No("invisible");

                var cogo = entity as CogoPoint;
                if (cogo != null) return CogoPointPlotting.Plots(cogo, db, tr, name => LayerPlots(db, tr, name));

                var br = entity as BlockReference;
                if (br != null) return OfBlock(br, db, tr);

                return LayerPlots(db, tr, entity.Layer)
                    ? Verdict.Yes("layer " + entity.Layer + " plots")
                    : Verdict.No("layer " + entity.Layer + " does not plot");
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex) { return Verdict.Unknown("could not read the object: " + ex.Message); }
        }

        /// <summary>A layer plots when it exists, is plottable, thawed and on.</summary>
        public static bool LayerPlots(Database db, Transaction tr, string name)
        {
            var facts = Facts(db, tr, name);
            return facts.Exists && facts.Plottable && !facts.Frozen && !facts.Off;
        }

        public static bool LayerFrozen(Database db, Transaction tr, string name)
        {
            return Facts(db, tr, name).Frozen;
        }

        private static LayerFacts Facts(Database db, Transaction tr, string name)
        {
            if (!ReferenceEquals(_cacheOwner, tr)) { _cacheOwner = tr; LayerCache.Clear(); }
            LayerFacts facts;
            var key = name ?? string.Empty;
            if (LayerCache.TryGetValue(key, out facts)) return facts;
            facts = new LayerFacts();
            var table = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (!string.IsNullOrEmpty(name) && table.Has(name))
            {
                var record = (LayerTableRecord)tr.GetObject(table[name], OpenMode.ForRead);
                facts.Exists = true;
                facts.Plottable = record.IsPlottable;
                facts.Frozen = record.IsFrozen;
                facts.Off = record.IsOff;
            }
            LayerCache[key] = facts;
            return facts;
        }

        // ================================================================= blocks

        private static Verdict OfBlock(BlockReference br, Database db, Transaction tr)
        {
            // A frozen insert layer hides the whole block; off or no-plot hides only what
            // follows the insert's layer (layer 0 content and the insert itself).
            if (LayerFrozen(db, tr, br.Layer)) return Verdict.No("insert layer " + br.Layer + " frozen");

            foreach (ObjectId attId in br.AttributeCollection)
            {
                if (attId.IsErased) continue;
                var att = tr.GetObject(attId, OpenMode.ForRead) as AttributeReference;
                if (att == null || att.Invisible || !att.Visible) continue;
                if (LayerPlots(db, tr, att.Layer)) return Verdict.Yes("attribute " + att.Tag + " on layer " + att.Layer + " plots");
            }

            var btr = tr.GetObject(br.BlockTableRecord, OpenMode.ForRead) as BlockTableRecord;
            if (btr == null) return Verdict.Unknown("block definition could not be opened");
            return ContentPlots(btr, br.Layer, db, tr, 0);
        }

        /// <summary>True when anything the definition draws lands on a plotting layer, with
        /// layer 0 content taking the insert's layer. Nested blocks are followed.</summary>
        public static Verdict ContentPlots(BlockTableRecord btr, string insertLayer, Database db, Transaction tr, int depth)
        {
            if (depth > 6) return Verdict.Unknown("block nesting deeper than 6: " + btr.Name);
            foreach (ObjectId id in btr)
            {
                if (id.IsErased) continue;
                var inner = tr.GetObject(id, OpenMode.ForRead, false, true) as AcEntity;
                if (inner == null || inner is AttributeDefinition || !inner.Visible) continue;
                var layer = inner.Layer == "0" ? insertLayer : inner.Layer;
                var nested = inner as BlockReference;
                if (nested != null)
                {
                    if (LayerFrozen(db, tr, layer)) continue;
                    var nestedDef = tr.GetObject(nested.BlockTableRecord, OpenMode.ForRead) as BlockTableRecord;
                    if (nestedDef == null) return Verdict.Unknown("nested block definition could not be opened");
                    var verdict = ContentPlots(nestedDef, layer, db, tr, depth + 1);
                    if (verdict.Plots != false) return verdict;
                    continue;
                }
                if (LayerPlots(db, tr, layer))
                    return Verdict.Yes("block " + btr.Name + " draws on layer " + layer + (inner.Layer == "0" ? " (layer 0, follows the insert)" : string.Empty));
            }
            return Verdict.No("block " + btr.Name + ": nothing it draws is on a plotting layer");
        }

        /// <summary>Whether a definition draws anything on a layer OTHER than 0, i.e. content
        /// that ignores the insert's layer. Decides whether a nonplot twin is needed.</summary>
        public static bool DrawsOnOwnLayers(BlockTableRecord btr, Transaction tr, int depth)
        {
            if (depth > 6) return true;
            foreach (ObjectId id in btr)
            {
                if (id.IsErased) continue;
                var inner = tr.GetObject(id, OpenMode.ForRead, false, true) as AcEntity;
                if (inner == null || inner is AttributeDefinition || !inner.Visible) continue;
                if (inner.Layer != "0") return true;
                var nested = inner as BlockReference;
                if (nested != null)
                {
                    var nestedDef = tr.GetObject(nested.BlockTableRecord, OpenMode.ForRead) as BlockTableRecord;
                    if (nestedDef != null && DrawsOnOwnLayers(nestedDef, tr, depth + 1)) return true;
                }
            }
            return false;
        }
    }
}
