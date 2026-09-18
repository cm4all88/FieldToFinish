using System;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace FieldCodes.Cad
{
    /// <summary>
    /// The one wipeout-under-text builder. Line labels and drafted-course annotation
    /// both come through here, so a mask can never be sized or ordered two different
    /// ways. The caller stamps the returned entity with its own ownership kind --
    /// this class decides geometry and draw order, never ownership.
    /// </summary>
    internal static class TextMask
    {
        /// <summary>
        /// A wipeout under the text, rotated with it -- corners computed from the
        /// measured extents so diagonal text does not over-mask. Placed just below
        /// the text in the draw order, so the mask can never hide the label it
        /// exists to serve. Returns ObjectId.Null when the text has no measurable
        /// extents (empty or degenerate text).
        /// </summary>
        public static ObjectId Place(Database db, Transaction tr, Entity text,
                                     Point3d centre, double rotation,
                                     double textHeight, ObjectId layerId)
        {
            double w, h, ox, oy;
            if (!CadUtil.TryMeasureText(text, out w, out h, out ox, out oy))
                return ObjectId.Null;

            // The measured extents are the rotated text's bounding box; recover the
            // text's own width from them, then build the rotated rectangle directly.
            var cos = Math.Abs(Math.Cos(rotation));
            var sin = Math.Abs(Math.Sin(rotation));
            var denominator = cos * cos - sin * sin;

            double width;
            if (Math.Abs(denominator) < 1e-6)
            {
                width = Math.Max(w, h);          // 45-degree text: fall back safely
            }
            else
            {
                width = (w * cos - h * sin) / denominator;
            }

            return PlaceBox(db, tr, text.ObjectId, centre, rotation, width,
                            textHeight, layerId);
        }

        /// <summary>
        /// The same mask from an already-known text width -- used when the width
        /// cannot be recovered from entity extents, e.g. a multileader whose
        /// extents include the leader line.
        /// </summary>
        public static ObjectId PlaceBox(Database db, Transaction tr, ObjectId belowId,
                                        Point3d centre, double rotation, double width,
                                        double textHeight, ObjectId layerId)
        {
            var pad = textHeight * 0.25;
            var halfW = width / 2.0 + pad;
            var halfH = textHeight / 2.0 + pad;

            var u = new Vector3d(Math.Cos(rotation), Math.Sin(rotation), 0.0);
            var v = new Vector3d(-Math.Sin(rotation), Math.Cos(rotation), 0.0);

            var points = new Point2dCollection();
            foreach (var corner in new[]
            {
                centre - u * halfW - v * halfH,
                centre + u * halfW - v * halfH,
                centre + u * halfW + v * halfH,
                centre - u * halfW + v * halfH,
                centre - u * halfW - v * halfH
            })
                points.Add(new Point2d(corner.X, corner.Y));

            using (var wipeout = new Wipeout())
            {
                wipeout.SetDatabaseDefaults(db);
                wipeout.SetFrom(points, Vector3d.ZAxis);
                wipeout.LayerId = layerId;

                CadUtil.AddToModelSpace(db, tr, wipeout);

                // The pair's display order, enforced at creation: source geometry,
                // then the mask, then the text. The wipeout was added after the text
                // (so it already sits above the linework); move it just below its own
                // text so it can never hide the label it exists to serve.
                var ms = CadUtil.ModelSpace(db, tr, OpenMode.ForRead);
                var drawOrder = (DrawOrderTable)tr.GetObject(ms.DrawOrderTableId,
                                                             OpenMode.ForWrite);
                using (var ids = new ObjectIdCollection())
                {
                    ids.Add(wipeout.ObjectId);
                    drawOrder.MoveBelow(ids, belowId);
                }

                return wipeout.ObjectId;
            }
        }
    }
}
