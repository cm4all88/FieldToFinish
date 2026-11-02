using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using FieldCodes.Easements;
using FieldCodes.Symbols;

using AcEntity = Autodesk.AutoCAD.DatabaseServices.Entity;

namespace FieldCodes.Cad
{
    /// <summary>
    /// Reads a block definition into a <see cref="BlockSketch"/> for the symbol preview: lines,
    /// lightweight and old-style polylines (bulges as chords), arcs, circles and 2D solids, plus
    /// block references nested one level deep. Text, hatches, splines, ellipses, deeper nesting
    /// and anything else are counted as skipped, so the preview says it is simplified. Attribute
    /// definitions are recorded by tag (for the attribute templates), not drawn.
    ///
    /// Reads only; opens nothing for write. UNTESTED against a drawing: the geometry it hands
    /// on (bulges, nesting, base point) is unit tested in FieldCodes.Symbols.
    /// </summary>
    internal static class BlockSketchReader
    {
        /// <summary>The named block of <paramref name="db"/>, or null when the drawing has no block
        /// of that name (or it is a layout).</summary>
        public static BlockSketch Read(Database db, string blockName)
        {
            if (db == null || string.IsNullOrWhiteSpace(blockName)) return null;
            var name = blockName.Trim();
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var table = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                if (!table.Has(name)) return null;
                var record = (BlockTableRecord)tr.GetObject(table[name], OpenMode.ForRead);
                if (record.IsLayout) return null;
                var sketch = Read(tr, record);
                tr.Commit();
                return sketch;
            }
        }

        /// <summary>A block definition already open in a transaction, its base point moved to 0,0.</summary>
        public static BlockSketch Read(Transaction tr, BlockTableRecord record)
        {
            return Collect(tr, record, 0);
        }

        private static BlockSketch Collect(Transaction tr, BlockTableRecord record, int depth)
        {
            var sketch = new BlockSketch(record.Name);
            foreach (ObjectId id in record)
            {
                AcEntity entity;
                try { entity = tr.GetObject(id, OpenMode.ForRead, false) as AcEntity; }
                catch (Autodesk.AutoCAD.Runtime.Exception) { sketch.Skip("unreadable"); continue; }
                if (entity == null || !entity.Visible) continue;
                Add(tr, sketch, entity, depth);
            }
            sketch.MoveBasePoint(P(record.Origin));
            return sketch;
        }

        private static void Add(Transaction tr, BlockSketch sketch, AcEntity entity, int depth)
        {
            // AttributeDefinition is a DBText: it has to be asked first.
            var attribute = entity as AttributeDefinition;
            if (attribute != null)
            {
                if (!attribute.Constant) sketch.AddAttributeTag(attribute.Tag);
                sketch.Skip("attribute");
                return;
            }

            var line = entity as Line;
            if (line != null) { sketch.AddLine(P(line.StartPoint), P(line.EndPoint)); return; }

            var polyline = entity as Polyline;
            if (polyline != null) { AddPolyline(sketch, polyline); return; }

            var arc = entity as Arc;
            if (arc != null)
            {
                // An arc drawn with its normal down (mirrored) runs clockwise seen from above:
                // its angles mirror about the y axis.
                if (arc.Normal.Z < 0)
                    sketch.AddArc(P(arc.Center), arc.Radius, Math.PI - arc.EndAngle, Math.PI - arc.StartAngle);
                else
                    sketch.AddArc(P(arc.Center), arc.Radius, arc.StartAngle, arc.EndAngle);
                return;
            }

            var circle = entity as Circle;
            if (circle != null) { sketch.AddCircle(P(circle.Center), circle.Radius); return; }

            var old2d = entity as Polyline2d;
            if (old2d != null) { AddPolyline2d(tr, sketch, old2d); return; }

            var old3d = entity as Polyline3d;
            if (old3d != null) { AddPolyline3d(tr, sketch, old3d); return; }

            var solid = entity as Solid;
            if (solid != null)
            {
                // A 2D solid's corners go 0, 1, 3, 2 round its edge.
                sketch.AddOutline(new List<P2>
                {
                    P(solid.GetPointAt(0)), P(solid.GetPointAt(1)), P(solid.GetPointAt(3)), P(solid.GetPointAt(2))
                });
                return;
            }

            // Point objects in a symbol are construction (or defpoints): nothing to show.
            if (entity is DBPoint) return;

            var reference = entity as BlockReference;
            if (reference != null) { AddNested(tr, sketch, reference, depth); return; }

            if (entity is DBText || entity is MText) { sketch.Skip("text"); return; }
            if (entity is Hatch) { sketch.Skip("hatch"); return; }
            sketch.Skip(entity.GetType().Name);
        }

        private static void AddPolyline(BlockSketch sketch, Polyline polyline)
        {
            var count = polyline.NumberOfVertices;
            if (count == 0) return;
            var flip = polyline.Normal.Z < 0;
            var vertices = new List<P2>(count);
            var bulges = new List<double>(count);
            for (var i = 0; i < count; i++)
            {
                vertices.Add(P(polyline.GetPoint3dAt(i)));
                var bulge = polyline.GetBulgeAt(i);
                bulges.Add(flip ? -bulge : bulge);
            }
            sketch.AddPolyline(vertices, bulges, polyline.Closed);
        }

        private static void AddPolyline2d(Transaction tr, BlockSketch sketch, Polyline2d polyline)
        {
            var vertices = new List<P2>();
            var bulges = new List<double>();
            foreach (ObjectId id in polyline)
            {
                var vertex = tr.GetObject(id, OpenMode.ForRead) as Vertex2d;
                if (vertex == null) continue;
                vertices.Add(P(polyline.VertexPosition(vertex)));
                bulges.Add(vertex.Bulge);
            }
            sketch.AddPolyline(vertices, bulges, polyline.Closed);
        }

        private static void AddPolyline3d(Transaction tr, BlockSketch sketch, Polyline3d polyline)
        {
            var vertices = new List<P2>();
            foreach (ObjectId id in polyline)
            {
                var vertex = tr.GetObject(id, OpenMode.ForRead) as PolylineVertex3d;
                if (vertex != null) vertices.Add(P(vertex.Position));
            }
            sketch.AddPolyline(vertices, null, polyline.Closed);
        }

        /// <summary>One level of nesting: the inner block's geometry placed by the reference's
        /// position, rotation and scale. Deeper, or turned over (normal down), is left out.</summary>
        private static void AddNested(Transaction tr, BlockSketch sketch, BlockReference reference, int depth)
        {
            if (depth >= 1) { sketch.Skip("nested block"); return; }
            if (reference.Normal.Z < 0) { sketch.Skip("mirrored block"); return; }
            BlockTableRecord inner;
            try { inner = tr.GetObject(reference.BlockTableRecord, OpenMode.ForRead) as BlockTableRecord; }
            catch (Autodesk.AutoCAD.Runtime.Exception) { sketch.Skip("nested block"); return; }
            if (inner == null) { sketch.Skip("nested block"); return; }
            var scale = reference.ScaleFactors;
            sketch.AddNested(Collect(tr, inner, depth + 1), P(reference.Position), reference.Rotation, scale.X, scale.Y);
        }

        private static P2 P(Point3d p)
        {
            return new P2(p.X, p.Y);
        }
    }
}
