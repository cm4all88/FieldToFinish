using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Civil.DatabaseServices;

// Both namespaces define Entity; the AutoCAD one is what model space holds.
using AcEntity = Autodesk.AutoCAD.DatabaseServices.Entity;

namespace FieldCodes.Cad
{
    /// <summary>
    /// Thin helpers over the AutoCAD database. Nothing here decides policy -- that
    /// lives in FieldCodes, which has no Autodesk dependency and can be unit tested.
    ///
    /// UNTESTED: no drawing was open when this was written. See README "Needs
    /// verifying" before trusting any of it.
    /// </summary>
    internal static class CadUtil
    {
        /// <summary>
        /// Drawing units per plotted unit, from the current annotation scale.
        /// A 1"=20' sheet returns 240. Falls back to 1.0 when no scale is set.
        /// </summary>
        public static double DrawingUnitsPerPlottedUnit(Database db)
        {
            var scale = db.Cannoscale;
            if (scale == null) return 1.0;
            if (scale.PaperUnits <= 0.0) return 1.0;
            return scale.DrawingUnits / scale.PaperUnits;
        }

        public static BlockTableRecord ModelSpace(Database db, Transaction tr, OpenMode mode)
        {
            var id = SymbolUtilityServices.GetBlockModelSpaceId(db);
            return (BlockTableRecord)tr.GetObject(id, mode);
        }

        /// <summary>
        /// The drawing's own layer for the family a name belongs to: for "V-ESMT-PATT-E" it looks for "V-ESMT-E",
        /// then "V-ESMT". Null when the drawing has no layer in that family.
        /// </summary>
        private static LayerTableRecord FamilyLayer(LayerTable table, Transaction tr, string name)
        {
            var parts = (name ?? string.Empty).Split('-');
            if (parts.Length < 3) return null;
            var suffix = parts[parts.Length - 1];
            for (var keep = parts.Length - 1; keep >= 2; keep--)
            {
                var stem = string.Join("-", parts.Take(keep).ToArray());
                foreach (var candidate in new[] { stem + "-" + suffix, stem })
                    if (!string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase) && table.Has(candidate))
                        return (LayerTableRecord)tr.GetObject(table[candidate], OpenMode.ForRead);
            }
            return null;
        }

        /// <summary>Returns the layer's id, creating it if it does not exist.</summary>
        public static ObjectId EnsureLayer(Database db, Transaction tr, string name)
        {
            if (string.IsNullOrEmpty(name)) return db.Clayer;

            var table = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (table.Has(name)) return table[name];

            // A new layer in a family the drawing already has (V-ESMT-PATT-E beside V-ESMT-E) takes that family's
            // look, so it reads as the office's rather than plain white. FTF is not deciding a standard here: it
            // follows the layer the office drew, and the drafter can set it afterwards.
            var family = FamilyLayer(table, tr, name);
            table.UpgradeOpen();
            using (var record = new LayerTableRecord())
            {
                record.Name = name;
                if (family != null)
                {
                    // Colour and lineweight only: the family's linetype belongs to its lines (V-ESMT-E is HIDDEN2),
                    // and hatch, text and dimension layers are drawn continuous.
                    record.Color = family.Color;
                    record.LineWeight = family.LineWeight;
                }
                var id = table.Add(record);
                tr.AddNewlyCreatedDBObject(record, true);
                return id;
            }
        }

        /// <summary>
        /// Finds a block definition. Returns ObjectId.Null when it is not in the
        /// drawing -- the caller reports that as an error rather than substituting
        /// some other block.
        /// </summary>
        public static ObjectId FindBlock(Database db, Transaction tr, string name)
        {
            if (string.IsNullOrEmpty(name)) return ObjectId.Null;

            var table = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            return table.Has(name) ? table[name] : ObjectId.Null;
        }

        /// <summary>
        /// Every CogoPoint in model space, in ascending point-number order.
        ///
        /// Model space is walked directly rather than going through
        /// CivilApplication.ActiveDocument.CogoPoints, which lives in AeccMgd.dll --
        /// an assembly this project deliberately does not reference.
        /// </summary>
        public static IList<CogoPoint> CogoPointsInOrder(Database db, Transaction tr)
        {
            var points = new List<CogoPoint>();
            var ms = ModelSpace(db, tr, OpenMode.ForRead);

            foreach (ObjectId id in ms)
            {
                if (id.IsErased) continue;
                var pt = tr.GetObject(id, OpenMode.ForRead, false, true) as CogoPoint;
                if (pt != null) points.Add(pt);
            }

            // Deterministic ordering: identical input must produce identical output,
            // or every drawing comparison shows phantom changes on re-run.
            points.Sort((a, b) => a.PointNumber.CompareTo(b.PointNumber));
            return points;
        }

        public static void AddToModelSpace(Database db, Transaction tr, AcEntity entity)
        {
            var ms = ModelSpace(db, tr, OpenMode.ForWrite);
            ms.AppendEntity(entity);
            tr.AddNewlyCreatedDBObject(entity, true);
        }

        /// <summary>Kept as a thin alias; the conversion itself lives in
        /// FieldCodes.Geometry.Angles so there is one tested definition.</summary>
        public static double DegreesToRadians(double degrees)
        {
            return FieldCodes.Geometry.Angles.CadDegreesToApiRadians(degrees);
        }

        /// <summary>
        /// True size of a text entity, and where its extents sit relative to its
        /// insertion point.
        ///
        /// Label collision boxes are measured rather than estimated. A character-width
        /// guess is only ever right for one font: with any other style the drawn glyphs
        /// are wider than the reserved box, the boxes do not overlap, and the text does.
        ///
        /// The offset matters as much as the size -- the insertion point is on the
        /// baseline at the left, so the extents start below and beside it.
        /// </summary>
        public static bool TryMeasureText(AcEntity text, out double width, out double height,
                                          out double offsetX, out double offsetY)
        {
            width = height = offsetX = offsetY = 0.0;
            if (text == null) return false;

            try
            {
                var extents = text.GeometricExtents;

                width = extents.MaxPoint.X - extents.MinPoint.X;
                height = extents.MaxPoint.Y - extents.MinPoint.Y;

                var position = PositionOf(text);
                offsetX = extents.MinPoint.X - position.X;
                offsetY = extents.MinPoint.Y - position.Y;

                return width > 0 && height > 0;
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                // Empty or degenerate text has no extents.
                return false;
            }
        }

        /// <summary>
        /// A leader-only multileader from the anchor to the label box corner. The
        /// office standard: anything with a leader is an MLEADER entity, never the
        /// legacy Leader. Content-free on purpose -- the label text is its own
        /// MText, so hand-moving either piece behaves predictably.
        /// </summary>
        public static MLeader NewLeaderOnly(Database db, Transaction tr, Point3d anchor,
                                            Point3d target, ObjectId layerId,
                                            bool arrowhead, double arrowSize)
        {
            var leader = new MLeader();
            leader.SetDatabaseDefaults(db);

            // The drawing's CURRENT multileader style drives the look -- arrowhead,
            // dogleg, landing -- so FTF leaders match whatever the office template
            // says a leader is, rather than a hardcoded opinion.
            if (!db.MLeaderstyle.IsNull) leader.MLeaderStyle = db.MLeaderstyle;
            leader.ContentType = ContentType.NoneContent;

            var line = leader.AddLeaderLine(anchor);
            leader.AddLastVertex(line, target);

            leader.LayerId = layerId;
            AddToModelSpace(db, tr, leader);
            return leader;
        }

        /// <summary>
        /// Label and leader as ONE multileader with MTEXT content: grip-move the
        /// text and the leader re-routes with it, exactly like a hand-drawn
        /// multileader. Styled by the drawing's current multileader style. The
        /// arrow lands on the anchor (the feature); the text sits at textLocation.
        /// </summary>
        public static MLeader NewLeaderedLabel(Database db, Transaction tr,
                                               string contents, double textHeight,
                                               ObjectId styleId, ObjectId layerId,
                                               Point3d textLocation, Point3d anchor)
        {
            return NewLeaderedLabel(db, tr, contents, textHeight, styleId, layerId, textLocation, anchor, ObjectId.Null);
        }

        /// <summary>As above with a named multileader style (<paramref name="leaderStyleId"/>); Null uses the current one.</summary>
        public static MLeader NewLeaderedLabel(Database db, Transaction tr,
                                               string contents, double textHeight,
                                               ObjectId styleId, ObjectId layerId,
                                               Point3d textLocation, Point3d anchor, ObjectId leaderStyleId)
        {
            var leader = new MLeader();
            leader.SetDatabaseDefaults(db);
            if (!leaderStyleId.IsNull) leader.MLeaderStyle = leaderStyleId;
            else if (!db.MLeaderstyle.IsNull) leader.MLeaderStyle = db.MLeaderstyle;
            leader.ContentType = ContentType.MTextContent;

            using (var text = new MText())
            {
                text.SetDatabaseDefaults(db);
                if (!styleId.IsNull) text.TextStyleId = styleId;
                text.Contents = contents;
                text.TextHeight = textHeight;
                text.Location = textLocation;
                leader.MText = text;
            }
            leader.TextLocation = textLocation;

            leader.AddLeaderLine(anchor);
            leader.LayerId = layerId;
            AddToModelSpace(db, tr, leader);
            return leader;
        }

        public static Point3d PositionOf(AcEntity entity)
        {
            var text = entity as DBText;
            if (text != null) return text.Position;

            var mtext = entity as MText;
            if (mtext != null) return mtext.Location;

            var mleader = entity as MLeader;
            if (mleader != null) return mleader.TextLocation;

            return Point3d.Origin;
        }

        public static Point3d Flatten(Point3d p)
        {
            return new Point3d(p.X, p.Y, 0.0);
        }
    }
}
