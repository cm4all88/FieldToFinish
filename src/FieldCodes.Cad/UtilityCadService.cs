using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Civil.DatabaseServices;
using FieldCodes.Linework;
using FieldCodes.Settings;
using FieldCodes.Utilities;

using AcEntity = Autodesk.AutoCAD.DatabaseServices.Entity;

namespace FieldCodes.Cad
{
    /// <summary>An existing pipe-like CAD object found between two structures.</summary>
    internal sealed class ExistingPipe
    {
        public ObjectId Id;
        public bool OwnedByFtf;
        public string ConnectionId;
        public string Layer;
    }

    /// <summary>
    /// The CAD side of the dip builder: reads surveyed structure points, drafts
    /// confirmed pipes and structure labels, and finds existing geometry. Survey
    /// points are only ever read; everything drawn is FTF-owned and traceable to
    /// its connection or structure record.
    /// </summary>
    internal static class UtilityCadService
    {
        // ================================================================ points

        public static CadStructureSnapshot Snapshot(CogoPoint point)
        {
            return new CadStructureSnapshot
            {
                PointNumber = point.PointNumber.ToString(CultureInfo.InvariantCulture),
                Northing = point.Northing,
                Easting = point.Easting,
                Rim = point.Elevation,
                Description = point.RawDescription,
                Handle = point.Handle.ToString()
            };
        }

        public static Dictionary<string, CadStructureSnapshot> LivePoints(Database db, Transaction tr)
        {
            var map = new Dictionary<string, CadStructureSnapshot>(StringComparer.OrdinalIgnoreCase);
            foreach (var point in CadUtil.CogoPointsInOrder(db, tr))
            {
                var snap = Snapshot(point);
                if (!map.ContainsKey(snap.PointNumber)) map[snap.PointNumber] = snap;
            }
            return map;
        }

        /// <summary>The structure record for a surveyed point, created and classified
        /// from its description when the project does not have it yet.</summary>
        public static StructureRecord EnsureStructure(UtilityProject project, CadStructureSnapshot point,
                                                      UtilitySettings settings)
        {
            var record = project.StructureByPoint(point.PointNumber);
            if (record == null)
            {
                record = new StructureRecord();
                record.Field.PointNumber = point.PointNumber;
                project.Structures.Add(record);
            }

            if (record.Cad == null) record.Cad = point;
            Classify(record, point.Description, settings);
            return record;
        }

        public static void Classify(StructureRecord record, string description, UtilitySettings settings)
        {
            var code = record.Field.FieldCode;
            if (string.IsNullOrEmpty(code) && !string.IsNullOrWhiteSpace(description))
                code = description.Trim().Split(' ', '\t')[0].ToUpperInvariant();

            var rule = settings.FindCode(code);
            if (rule != null)
            {
                if (string.IsNullOrEmpty(record.StructureType)) record.StructureType = rule.Code;
                record.System = rule.System;
            }
            else if (string.IsNullOrEmpty(record.StructureType))
            {
                record.StructureType = code;
            }
        }

        /// <summary>
        /// Merges parsed note blocks into the project. A structure's field
        /// observations are replaced only by a new note block for the same point --
        /// the notes are the authority -- and the old block's connections that no
        /// longer have their pipe are dropped. Returns one message per structure.
        /// </summary>
        public static IList<string> ImportNotes(UtilityProject project, DipNoteParseResult parsed,
                                                IDictionary<string, CadStructureSnapshot> live,
                                                UtilitySettings settings)
        {
            var messages = new List<string>();
            foreach (var field in parsed.Structures)
            {
                CadStructureSnapshot point;
                live.TryGetValue(field.PointNumber ?? string.Empty, out point);

                var record = project.StructureByPoint(field.PointNumber);
                if (record == null)
                {
                    record = new StructureRecord();
                    project.Structures.Add(record);
                }

                var oldPipes = new HashSet<string>(record.Field.Pipes.Select(p => p.Id));
                record.Field = field;
                if (point != null) record.Cad = point;
                Classify(record, point != null ? point.Description : null, settings);

                project.Connections.RemoveAll(c =>
                    (c.FromStructureId == record.Id && oldPipes.Contains(c.FromPipeId)) ||
                    (c.ToStructureId == record.Id && c.ToPipeId != null && oldPipes.Contains(c.ToPipeId)));

                messages.Add(point == null
                    ? record.Label + ": notes read, but point " + field.PointNumber +
                      " is not in the drawing -- no rim or position, so nothing is calculated."
                    : string.Format(CultureInfo.InvariantCulture, "{0}: {1} pipe(s) read; rim {2:0.00} from the drawing.",
                                    record.Label, field.Pipes.Count, point.Rim));
            }
            return messages;
        }

        public static ObjectId PointId(Database db, Transaction tr, string pointNumber)
        {
            foreach (var point in CadUtil.CogoPointsInOrder(db, tr))
                if (point.PointNumber.ToString(CultureInfo.InvariantCulture) == pointNumber)
                    return point.ObjectId;
            return ObjectId.Null;
        }

        // ============================================================== existing

        public static IList<ExistingPipe> FindExisting(Database db, Transaction tr, UtilityProject project,
                                                       PipeConnection connection, UtilitySettings settings,
                                                       double unitsPerFoot, IList<LayerMapping> mappings = null)
        {
            var found = new List<ExistingPipe>();
            var from = project.Structure(connection.FromStructureId);
            var to = project.Structure(connection.ToStructureId);
            if (from == null || to == null || from.Cad == null || to.Cad == null) return found;

            var a = new Point3d(from.Cad.Easting, from.Cad.Northing, 0);
            var b = new Point3d(to.Cad.Easting, to.Cad.Northing, 0);
            var tol = Math.Max(0.01, settings.ExistingPipeToleranceFt * unitsPerFoot);
            // Hand-drawn pipes may be on the configured layer, the project's mapped layer,
            // or a differently spelled / near-duplicate of either.
            var pipeLayers = settings.Systems.Select(s => s.PipeLayer)
                .Concat((mappings ?? new List<LayerMapping>())
                    .Where(m => m != null && settings.Systems.Any(s => string.Equals(s.PipeLayer, m.From, StringComparison.OrdinalIgnoreCase)))
                    .Select(m => m.To))
                .Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
            Func<string, bool> isPipeLayer = layer => pipeLayers.Any(p =>
                ProductionLayerResolver.Normalize(p) == ProductionLayerResolver.Normalize(layer) ||
                ProductionLayerResolver.IsSimilar(p, layer));

            var ms = CadUtil.ModelSpace(db, tr, OpenMode.ForRead);
            foreach (ObjectId id in ms)
            {
                if (id.IsErased) continue;
                var curve = tr.GetObject(id, OpenMode.ForRead, false, true) as Curve;
                if (curve == null || !(curve is Line || curve is Polyline)) continue;

                var stamp = Ownership.Read(curve);
                var owned = stamp != null && stamp.Kind == FtfEntityKind.UtilityPipe;
                if (stamp != null && !owned) continue;
                if (!owned && !isPipeLayer(curve.Layer)) continue;

                if (owned && stamp.PointNumber == connection.Id)
                {
                    found.Add(new ExistingPipe { Id = id, OwnedByFtf = true, ConnectionId = stamp.PointNumber, Layer = curve.Layer });
                    continue;
                }

                if (NearCurve(curve, a, tol) && NearCurve(curve, b, tol) && Spans(curve, a, b))
                    found.Add(new ExistingPipe
                    {
                        Id = id, OwnedByFtf = owned, ConnectionId = owned ? stamp.PointNumber : null, Layer = curve.Layer
                    });
            }
            return found;
        }

        private static bool NearCurve(Curve curve, Point3d p, double tol)
        {
            try
            {
                var flat = new Point3d(p.X, p.Y, curve.StartPoint.Z);
                return curve.GetClosestPointTo(flat, false).DistanceTo(flat) <= tol;
            }
            catch (Autodesk.AutoCAD.Runtime.Exception) { return false; }
        }

        /// <summary>The curve runs between the two points, not merely past both.</summary>
        private static bool Spans(Curve curve, Point3d a, Point3d b)
        {
            var length = a.DistanceTo(b);
            double curveLength;
            try { curveLength = curve.GetDistanceAtParameter(curve.EndParam); }
            catch (Autodesk.AutoCAD.Runtime.Exception) { return false; }
            return curveLength <= length * 1.5 + 5.0;
        }

        // =============================================================== drafting

        /// <summary>Erases FTF's own drafting for one connection: its pipe lines and
        /// its pipe label.</summary>
        public static int EraseConnectionDrafting(Database db, Transaction tr, string connectionId)
        {
            return Ownership.DeleteOwned(db, tr, s =>
                (s.Kind == FtfEntityKind.UtilityPipe || s.Kind == FtfEntityKind.UtilityPipeLabel) &&
                s.PointNumber == connectionId);
        }

        /// <summary>
        /// Draws one confirmed pipe: a centerline, or a double line offset by half the
        /// OBSERVED width when the pipe is wider than the profile threshold. Then its
        /// label. Returns the ids drawn.
        /// </summary>
        public static IList<ObjectId> DrawPipe(Database db, Transaction tr, UtilityProject project,
                                               PipeConnection connection, FtfSettings settings,
                                               string rulesVersion, bool labelOnly)
        {
            var ids = new List<ObjectId>();
            var us = settings.Dips;
            var from = project.Structure(connection.FromStructureId);
            var to = project.Structure(connection.ToStructureId);
            var pipe = project.Pipe(connection.FromStructureId, connection.FromPipeId);
            if (from == null || to == null || pipe == null || from.Cad == null || to.Cad == null) return ids;

            var standard = us.Standard(from.System);
            var a = new Point3d(from.Cad.Easting, from.Cad.Northing, 0);
            var b = new Point3d(to.Cad.Easting, to.Cad.Northing, 0);
            if (a.DistanceTo(b) < 0.01) return ids;

            Ownership.EnsureRegApp(db, tr);
            var pipeLayer = ProductionLayers.Get(db, tr, standard.PipeLayer, settings);
            var tag = from.Field.PointNumber + ">" + to.Field.PointNumber;

            var dir = (b - a).GetNormal();
            var left = new Vector3d(-dir.Y, dir.X, 0);
            var doubleLine = PipeDraftingRules.DrawDoubleLine(pipe, us);

            if (!labelOnly)
            {
                if (doubleLine)
                {
                    var half = PipeDraftingRules.HalfWidthFeet(pipe) * settings.General.UnitsPerFoot;
                    foreach (var side in new[] { 1.0, -1.0 })
                        ids.Add(AddLine(db, tr, a + left * half * side, b + left * half * side, pipeLayer, connection.Id, tag, rulesVersion));
                    if (us.CenterlineWithDoubleLine)
                        ids.Add(AddLine(db, tr, a, b, pipeLayer, connection.Id, tag, rulesVersion));
                }
                else
                {
                    ids.Add(AddLine(db, tr, a, b, pipeLayer, connection.Id, tag, rulesVersion));
                }
            }

            // The label reads along the pipe, clear of a double line.
            var scale = CadUtil.DrawingUnitsPerPlottedUnit(db);
            var textHeight = us.TextHeightPlotted * scale;
            var clearance = (doubleLine ? PipeDraftingRules.HalfWidthFeet(pipe) * settings.General.UnitsPerFoot : 0) +
                            us.PipeLabelOffsetPlotted * scale + textHeight / 2.0;
            var mid = a + (b - a) * 0.5;
            var direction = Math.Atan2(b.Y - a.Y, b.X - a.X);
            var plan = LineLabelPlanner.PlaceAt(mid.X, mid.Y, direction, LineLabelSide.Left, clearance, true);

            using (var text = new MText())
            {
                text.SetDatabaseDefaults(db);
                var style = Setup.DrawingResources.FindTextStyle(db, tr, us.TextStyle);
                if (!style.IsNull) text.TextStyleId = style;
                text.Contents = UtilityLabelFormatter.PipeLabel(project, connection, us);
                text.TextHeight = textHeight;
                text.Attachment = AttachmentPoint.MiddleCenter;
                text.Location = new Point3d(plan.X, plan.Y, 0);
                text.Rotation = plan.RotationRadians;
                text.LayerId = ProductionLayers.Get(db, tr, standard.LabelLayer, settings);
                CadUtil.AddToModelSpace(db, tr, text);
                Ownership.Stamp(text, connection.Id, rulesVersion, FtfEntityKind.UtilityPipeLabel, null, tag);
                ids.Add(text.ObjectId);
            }

            connection.Drafted = true;
            return ids;
        }

        private static ObjectId AddLine(Database db, Transaction tr, Point3d a, Point3d b, ObjectId layer,
                                        string connectionId, string tag, string rulesVersion)
        {
            using (var line = new Line(a, b))
            {
                line.LayerId = layer;
                CadUtil.AddToModelSpace(db, tr, line);
                Ownership.Stamp(line, connectionId, rulesVersion, FtfEntityKind.UtilityPipe, null, tag);
                return line.ObjectId;
            }
        }

        /// <summary>Takes an existing drafter-drawn pipe under management: it moves to
        /// the profile's pipe layer and is linked to the connection. Its geometry is
        /// not changed.</summary>
        public static void Adopt(Database db, Transaction tr, ObjectId id, UtilityProject project,
                                 PipeConnection connection, FtfSettings settings, string rulesVersion)
        {
            var from = project.Structure(connection.FromStructureId);
            var to = project.Structure(connection.ToStructureId);
            var entity = (AcEntity)tr.GetObject(id, OpenMode.ForWrite);
            entity.LayerId = ProductionLayers.Get(db, tr, settings.Dips.Standard(from.System).PipeLayer, settings);
            Ownership.EnsureRegApp(db, tr);
            Ownership.Stamp(entity, connection.Id, rulesVersion, FtfEntityKind.UtilityPipe, null,
                            from.Field.PointNumber + ">" + to.Field.PointNumber);
        }

        public static ObjectId PlaceStructureLabel(Database db, Transaction tr, StructureRecord structure,
                                                   string text, Point3d location, FtfSettings settings,
                                                   string rulesVersion)
        {
            Ownership.DeleteOwned(db, tr, s => s.Kind == FtfEntityKind.StructureLabel && s.PointNumber == structure.Id);

            var us = settings.Dips;
            var standard = us.Standard(structure.System);
            var scale = CadUtil.DrawingUnitsPerPlottedUnit(db);
            var anchor = new Point3d(structure.Cad.Easting, structure.Cad.Northing, 0);
            var style = Setup.DrawingResources.FindTextStyle(db, tr, us.TextStyle);

            Ownership.EnsureRegApp(db, tr);
            using (var leader = CadUtil.NewLeaderedLabel(db, tr, ToMText(text), us.TextHeightPlotted * scale, style,
                       ProductionLayers.Get(db, tr, standard.StructureLabelLayer, settings), location, anchor))
            {
                Ownership.Stamp(leader, structure.Id, rulesVersion, FtfEntityKind.StructureLabel, location,
                                structure.Field.PointNumber);
                return leader.ObjectId;
            }
        }

        public static string ToMText(string text)
        {
            return string.Join("\\P", (text ?? string.Empty).Replace("\r\n", "\n").Split('\n')
                .Select(l => l.Replace("{", "\\{").Replace("}", "\\}")).ToArray());
        }

        /// <summary>Where the structure's current label text sits, if it has one.</summary>
        public static Point3d? StructureLabelLocation(Database db, Transaction tr, StructureRecord structure)
        {
            foreach (var pair in Ownership.FindOwned(db, tr, s => s.Kind == FtfEntityKind.StructureLabel && s.PointNumber == structure.Id))
            {
                var leader = tr.GetObject(pair.Key, OpenMode.ForRead) as MLeader;
                if (leader != null) return leader.TextLocation;
            }
            return null;
        }

        // ================================================================== view

        public static void ZoomTo(Editor ed, Extents3d extents)
        {
            using (var view = ed.GetCurrentView())
            {
                var worldToEye = Matrix3d.WorldToPlane(view.ViewDirection) *
                                 Matrix3d.Displacement(view.Target.GetAsVector().Negate()) *
                                 Matrix3d.Rotation(view.ViewTwist, view.ViewDirection, view.Target);
                var min = extents.MinPoint.TransformBy(worldToEye);
                var max = extents.MaxPoint.TransformBy(worldToEye);

                var width = Math.Abs(max.X - min.X);
                var height = Math.Abs(max.Y - min.Y);
                var pad = Math.Max(Math.Max(width, height) * 0.6, 40.0);

                view.CenterPoint = new Point2d((min.X + max.X) / 2.0, (min.Y + max.Y) / 2.0);
                view.Width = width + pad;
                view.Height = height + pad;
                ed.SetCurrentView(view);
            }
        }
    }
}
