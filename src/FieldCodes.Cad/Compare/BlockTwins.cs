using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;

using AcEntity = Autodesk.AutoCAD.DatabaseServices.Entity;

namespace FieldCodes.Cad.Compare
{
    /// <summary>
    /// Nonplot twins of block definitions. A block whose definition draws on its own
    /// layers keeps plotting those parts wherever its insert sits, and the shared
    /// definition must not be edited (every other insert would change). So the insert is
    /// pointed at a copy of the definition, "PMX-COMP-NP$name", whose every entity sits
    /// on the comparison nonplot layer; restore points the insert back and erases twins
    /// nothing references any more. The original definition is never touched.
    ///
    /// UNTESTED in Civil 3D.
    /// </summary>
    internal static class BlockTwins
    {
        public const string Prefix = "PMX-COMP-NP$";

        public static bool IsTwin(string name)
        {
            return !string.IsNullOrEmpty(name) && name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The twin of a definition, created on first use.</summary>
        public static ObjectId Ensure(Database db, Transaction tr, ObjectId originalId, string nonplotLayer, int depth)
        {
            var original = (BlockTableRecord)tr.GetObject(originalId, OpenMode.ForRead);
            if (IsTwin(original.Name)) return originalId;
            if (depth > 4) throw new InvalidOperationException("block " + original.Name + " is nested too deeply to twin");

            var name = Prefix + original.Name.TrimStart('*');
            var table = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            if (table.Has(name)) return table[name];

            table.UpgradeOpen();
            var twin = new BlockTableRecord
            {
                Name = name,
                Origin = original.Origin,
                Units = original.Units,
                Explodable = original.Explodable,
                BlockScaling = original.BlockScaling,
                Comments = "FTFCOMPARE nonplot twin of " + original.Name + ". Erased by FTFCOMPARERESTORE when unused."
            };
            var twinId = table.Add(twin);
            tr.AddNewlyCreatedDBObject(twin, true);

            var ids = new ObjectIdCollection();
            foreach (ObjectId id in original) if (!id.IsErased) ids.Add(id);
            if (ids.Count > 0)
            {
                var mapping = new IdMapping();
                db.DeepCloneObjects(ids, twinId, mapping, false);
            }

            foreach (ObjectId id in twin)
            {
                if (id.IsErased) continue;
                var entity = tr.GetObject(id, OpenMode.ForWrite, false, true) as AcEntity;
                if (entity == null) continue;
                entity.Layer = nonplotLayer;
                var nested = entity as BlockReference;
                if (nested == null) continue;
                var nestedDef = tr.GetObject(nested.BlockTableRecord, OpenMode.ForRead) as BlockTableRecord;
                if (nestedDef != null && PlotVisibility.DrawsOnOwnLayers(nestedDef, tr, 0))
                    nested.BlockTableRecord = Ensure(db, tr, nested.BlockTableRecord, nonplotLayer, depth + 1);
            }
            return twinId;
        }

        /// <summary>Erases every twin no insert references any more. Returns how many.</summary>
        public static int Purge(Database db, Transaction tr)
        {
            var erased = 0;
            for (var pass = 0; pass < 6; pass++)
            {
                var doomed = new List<ObjectId>();
                var table = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                foreach (ObjectId id in table)
                {
                    if (id.IsErased) continue;
                    var btr = tr.GetObject(id, OpenMode.ForRead) as BlockTableRecord;
                    if (btr == null || !IsTwin(btr.Name)) continue;
                    if (btr.GetBlockReferenceIds(true, true).Count == 0) doomed.Add(id);
                }
                if (doomed.Count == 0) break;
                foreach (var id in doomed)
                {
                    var btr = (BlockTableRecord)tr.GetObject(id, OpenMode.ForWrite);
                    btr.Erase();
                    erased++;
                }
            }
            return erased;
        }
    }
}
