using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using FieldCodes.Easements;
using FieldCodes.Linework;

using AcEntity = Autodesk.AutoCAD.DatabaseServices.Entity;

namespace FieldCodes.Cad
{
    /// <summary>What the primitives below need to draw: the drawing, the text standard, and the
    /// stamp every entity carries so FTFDRAFTCLEAN can find it.</summary>
    internal sealed class DraftContext
    {
        public Database Db;
        public Transaction Tr;
        public string Version;

        /// <summary>The name stamped on every entity ("ROW", "LOTS"): what made it.</summary>
        public string Source;
        public ObjectId TextStyle;
        public double TextHeight;
        public double TextOffset;
    }

    /// <summary>
    /// The few drawing operations FTFROW and FTFLOTS share: a line or arc from a course, a
    /// segment, centred or left-set text, and the bearing/distance/curve text beside a course.
    /// Everything is stamped DraftLine or DraftAnnotation, so FTFCLEAN leaves it alone and
    /// FTFDRAFTCLEAN manages it. Each returns how many entities it added.
    ///
    /// UNTESTED against a drawing; written to the same patterns as FTFDRAWLINE.
    /// </summary>
    internal static class DraftPrimitives
    {
        public static Point3d Pt(P2 p)
        {
            return new Point3d(p.X, p.Y, 0.0);
        }

        /// <summary>Strict lookup: the layer's id, or Null. Never creates -- that is the whole
        /// difference from CadUtil.EnsureLayer.</summary>
        public static ObjectId FindLayer(Database db, Transaction tr, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return ObjectId.Null;
            var table = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            var trimmed = name.Trim();
            return table.Has(trimmed) ? table[trimmed] : ObjectId.Null;
        }

        public static int DrawCourse(DraftContext c, Course course, ObjectId layer, string linetype)
        {
            AcEntity entity;
            if (course.Kind == CourseKind.Line)
                entity = new Line(Pt(course.Start), Pt(course.End));
            else
            {
                var start = course.CounterClockwise ? course.StartAngle : course.EndAngle;
                var end = course.CounterClockwise ? course.EndAngle : course.StartAngle;
                entity = new Arc(Pt(course.Center), Vector3d.ZAxis, course.Radius, start, end);
            }

            using (entity)
            {
                entity.SetDatabaseDefaults(c.Db);
                entity.LayerId = layer;
                if (!string.IsNullOrEmpty(linetype)) entity.Linetype = linetype;
                CadUtil.AddToModelSpace(c.Db, c.Tr, entity);
                Ownership.Stamp(entity, c.Source, c.Version, FtfEntityKind.DraftLine, null);
            }
            return 1;
        }

        public static int DrawSegment(DraftContext c, Point3d a, Point3d b, ObjectId layer)
        {
            using (var line = new Line(a, b))
            {
                line.SetDatabaseDefaults(c.Db);
                line.LayerId = layer;
                CadUtil.AddToModelSpace(c.Db, c.Tr, line);
                Ownership.Stamp(line, c.Source, c.Version, FtfEntityKind.DraftLine, null);
            }
            return 1;
        }

        /// <summary>A closed outline, as one polyline.</summary>
        public static int DrawOutline(DraftContext c, IList<P2> corners, ObjectId layer)
        {
            if (corners == null || corners.Count < 3) return 0;
            using (var outline = new Polyline())
            {
                outline.SetDatabaseDefaults(c.Db);
                for (var i = 0; i < corners.Count; i++)
                    outline.AddVertexAt(i, new Point2d(corners[i].X, corners[i].Y), 0.0, 0.0, 0.0);
                outline.Closed = true;
                outline.LayerId = layer;
                CadUtil.AddToModelSpace(c.Db, c.Tr, outline);
                Ownership.Stamp(outline, c.Source, c.Version, FtfEntityKind.DraftLine, null);
            }
            return 1;
        }

        public static int DrawCircle(DraftContext c, Point3d centre, double radius, ObjectId layer)
        {
            using (var circle = new Circle(centre, Vector3d.ZAxis, radius))
            {
                circle.SetDatabaseDefaults(c.Db);
                circle.LayerId = layer;
                CadUtil.AddToModelSpace(c.Db, c.Tr, circle);
                Ownership.Stamp(circle, c.Source, c.Version, FtfEntityKind.DraftLine, null);
            }
            return 1;
        }

        /// <summary>One text, centred on <paramref name="position"/> (or set from it, left to right).</summary>
        public static int DrawText(DraftContext c, string content, Point3d position, double rotation, ObjectId layer, bool centred = true)
        {
            if (string.IsNullOrWhiteSpace(content)) return 0;
            using (var text = new DBText())
            {
                text.SetDatabaseDefaults(c.Db);
                if (!c.TextStyle.IsNull) text.TextStyleId = c.TextStyle;
                text.TextString = content;
                text.Height = c.TextHeight;
                text.Rotation = rotation;
                if (centred)
                {
                    text.HorizontalMode = TextHorizontalMode.TextCenter;
                    text.VerticalMode = TextVerticalMode.TextVerticalMid;
                    text.AlignmentPoint = position;
                }
                else
                {
                    text.Position = position;
                }
                text.LayerId = layer;
                CadUtil.AddToModelSpace(c.Db, c.Tr, text);
                Ownership.Stamp(text, c.Source, c.Version, FtfEntityKind.DraftAnnotation, position);
            }
            return 1;
        }

        /// <summary>
        /// The text beside a course: one line above, below or on a straight line as
        /// <paramref name="placement"/> says; two lines above and below; more stacked below.
        /// For an arc the lines stack outward from its middle. Never upside down: the reading
        /// direction flips the text, not the figures, which came from the geometry.
        /// </summary>
        public static int Annotate(DraftContext c, Course course, IList<string> lines, ObjectId layer, string placement)
        {
            if (lines == null || lines.Count == 0) return 0;
            var count = 0;
            var gap = c.TextOffset + c.TextHeight / 2.0;
            var step = c.TextHeight * 1.3;

            if (course.Kind == CourseKind.Arc)
            {
                var sweep = course.Sweep;
                var middle = course.CounterClockwise ? course.StartAngle + sweep / 2.0 : course.StartAngle - sweep / 2.0;
                var radial = new Vector3d(Math.Cos(middle), Math.Sin(middle), 0.0);
                var onArc = new Point3d(course.Center.X + course.Radius * Math.Cos(middle), course.Center.Y + course.Radius * Math.Sin(middle), 0.0);
                var tangent = course.CounterClockwise ? middle + Math.PI / 2.0 : middle - Math.PI / 2.0;
                var rotation = LineLabelPlanner.NormalizeReadable(tangent);
                for (var i = 0; i < lines.Count; i++)
                    count += DrawText(c, lines[i], onArc + radial * (gap + i * step), rotation, layer);
                return count;
            }

            var mid = new Point3d((course.Start.X + course.End.X) / 2.0, (course.Start.Y + course.End.Y) / 2.0, 0.0);
            var readable = LineLabelPlanner.NormalizeReadable(Math.Atan2(course.End.Y - course.Start.Y, course.End.X - course.Start.X));
            var up = new Vector3d(-Math.Sin(readable), Math.Cos(readable), 0.0);

            if (lines.Count == 1)
            {
                var where = (placement ?? "Above").Trim().Replace(" ", string.Empty);
                Point3d position;
                if (string.Equals(where, "Below", StringComparison.OrdinalIgnoreCase)) position = mid - up * gap;
                else if (string.Equals(where, "OnLine", StringComparison.OrdinalIgnoreCase)) position = mid;
                else position = mid + up * gap;
                return DrawText(c, lines[0], position, readable, layer);
            }

            if (lines.Count == 2)
            {
                count += DrawText(c, lines[0], mid + up * gap, readable, layer);
                count += DrawText(c, lines[1], mid - up * gap, readable, layer);
                return count;
            }

            for (var i = 0; i < lines.Count; i++)
                count += DrawText(c, lines[i], mid - up * (gap + i * step), readable, layer);
            return count;
        }

        /// <summary>Lines of text centred on a point and stacked down from it, set along a direction
        /// but never upside down.</summary>
        public static int DrawBlock(DraftContext c, IList<string> lines, P2 centre, P2 along, ObjectId layer)
        {
            if (lines == null || lines.Count == 0) return 0;
            var rotation = LineLabelPlanner.NormalizeReadable(Math.Atan2(along.Y, along.X));
            var up = new Vector3d(-Math.Sin(rotation), Math.Cos(rotation), 0.0);
            var step = c.TextHeight * 1.3;
            var top = (lines.Count - 1) * step / 2.0;
            var count = 0;
            for (var i = 0; i < lines.Count; i++)
                count += DrawText(c, lines[i], Pt(centre) + up * (top - i * step), rotation, layer);
            return count;
        }
    }
}
