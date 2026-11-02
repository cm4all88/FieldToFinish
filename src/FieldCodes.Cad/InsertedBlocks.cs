using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Civil.DatabaseServices;
using FieldCodes.Easements;
using FieldCodes.Geometry;
using FieldCodes.Symbols;

namespace FieldCodes.Cad
{
    /// <summary>
    /// What happens to a block FTFPOINTS inserted for a rule that opted in (insertBlock), beyond
    /// placing it: its attributes filled from the rule's templates, and -- for a rule whose blocks
    /// turn with the point's marker -- bringing the blocks round after FTFORIENT turned the markers.
    ///
    /// UNTESTED against a drawing; the text and placement decisions are unit tested
    /// (FieldCodes.Symbols.AttributeTemplate, BlockInsertion).
    /// </summary>
    internal static class InsertedBlocks
    {
        /// <summary>
        /// Gives a newly inserted reference (already in the database) its attribute references:
        /// one per non-constant attribute definition of the block, placed as the definition says.
        /// A tag with a template gets the template's text; any other keeps the definition's
        /// default. Returns how many were filled from a template.
        /// </summary>
        public static int FillAttributes(Transaction tr, BlockReference reference, ObjectId definitionId,
                                         IDictionary<string, string> templates, IDictionary<string, string> values)
        {
            var definition = (BlockTableRecord)tr.GetObject(definitionId, OpenMode.ForRead);
            if (!definition.HasAttributeDefinitions) return 0;

            var filled = 0;
            foreach (ObjectId id in definition)
            {
                var attribute = tr.GetObject(id, OpenMode.ForRead) as AttributeDefinition;
                if (attribute == null || attribute.Constant) continue;

                var attRef = new AttributeReference();
                attRef.SetAttributeFromBlock(attribute, reference.BlockTransform);
                string template;
                if (templates != null && templates.TryGetValue(attribute.Tag, out template) && !string.IsNullOrEmpty(template))
                {
                    attRef.TextString = AttributeTemplate.Format(template, values);
                    filled++;
                }
                reference.AttributeCollection.AppendAttribute(attRef);
                tr.AddNewlyCreatedDBObject(attRef, true);
            }
            return filled;
        }

        /// <summary>
        /// Turns every block FTFPOINTS inserted for a rule with blockRotation "Marker" to its point's
        /// current marker rotation (and moves it, for an offset that turns with the block).
        /// FTFPOINTS reads the marker when it places the block; FTFORIENT changes markers after
        /// that, so it calls this once it has applied its decisions. Attributes turn with the
        /// block. Returns how many blocks moved.
        /// </summary>
        public static int FollowMarkers(Database db, Transaction tr)
        {
            var cfg = FtfSession.Rules(db);
            if (!cfg.Codes.Any(c => c.InsertBlock && c.BlockRotation == BlockRotationMethod.Marker)) return 0;

            var settings = FtfSession.SettingsFor(db, cfg);
            var parser = new FieldCodeParser(cfg);
            var notes = CogoNotes.For(db, tr, settings);
            var points = new Dictionary<string, CogoPoint>(StringComparer.OrdinalIgnoreCase);
            foreach (var cogo in CadUtil.CogoPointsInOrder(db, tr))
                points[cogo.PointNumber.ToString(CultureInfo.InvariantCulture)] = cogo;

            var moved = 0;
            foreach (var owned in Ownership.FindOwned(db, tr, s => s.Kind == FtfEntityKind.Block))
            {
                CogoPoint cogo;
                if (owned.Value.PointNumber == null || !points.TryGetValue(owned.Value.PointNumber, out cogo)) continue;
                var parsed = notes.Parse(parser, cogo);
                var rule = cfg.Codes.FirstOrDefault(c => string.Equals(c.Id, parsed.RuleId, StringComparison.OrdinalIgnoreCase));
                if (rule == null || !rule.InsertBlock || rule.BlockRotation != BlockRotationMethod.Marker) continue;

                var degrees = BlockInsertion.RotationDegrees(rule, parsed, cfg.Orientation, parsed.BlockName,
                                                             Angles.ToDegrees(cogo.MarkerRotation)) ?? 0.0;
                var at = BlockInsertion.Position(rule, new P2(cogo.Location.X, cogo.Location.Y), degrees);
                try
                {
                    var reference = tr.GetObject(owned.Key, OpenMode.ForWrite) as BlockReference;
                    if (reference == null) continue;
                    var target = Angles.CadDegreesToApiRadians(degrees);
                    var turn = target - reference.Rotation;
                    var shift = reference.Position.GetVectorTo(new Point3d(at.X, at.Y, reference.Position.Z));
                    if (Math.Abs(turn) < 1e-9 && shift.Length < 1e-9) continue;
                    // TransformBy, not Rotation/Position: the attribute references go round too.
                    if (Math.Abs(turn) >= 1e-9) reference.TransformBy(Matrix3d.Rotation(turn, Vector3d.ZAxis, reference.Position));
                    if (shift.Length >= 1e-9) reference.TransformBy(Matrix3d.Displacement(shift));
                    moved++;
                }
                catch (Autodesk.AutoCAD.Runtime.Exception)
                {
                    // A block on a locked layer is left as it is.
                }
            }
            return moved;
        }
    }
}
