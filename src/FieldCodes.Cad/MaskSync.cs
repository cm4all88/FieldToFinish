using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;

using AcEntity = Autodesk.AutoCAD.DatabaseServices.Entity;

namespace FieldCodes.Cad
{
    /// <summary>
    /// Keeps every label and its mask together: the label and the wipeout are a
    /// pair, and a hand-moved label must have its mask recomputed around where the
    /// label actually is. Rather than trying to match old masks to moved labels,
    /// this deletes every FTF label mask and rebuilds one around each label's
    /// CURRENT geometry -- pairing can never go stale because it is never stored.
    ///
    /// Runs automatically at the end of FTFLABELS, FTFLINELABELS and FTFORDER, so
    /// nudging labels and then running any finishing pass squares the masks up.
    /// Draw order per pair stays source, then mask, then text.
    /// </summary>
    internal static class MaskSync
    {
        public static void Rebuild(Database db, Transaction tr, Editor ed,
                                   FieldCodes.Settings.FtfSettings settings,
                                   string rulesVersion)
        {
            Ownership.DeleteOwnedKinds(db, tr, FtfEntityKind.Mask,
                                       FtfEntityKind.LineMask);

            var pointMasks = settings.Labels.DrawMask;
            var lineMasks = settings.LineLabels.DrawMask;
            var maskLayerName = settings.Labels.MaskLayer;

            var rebuilt = 0;
            var labels = Ownership.FindOwned(db, tr, s =>
                s.Kind == FtfEntityKind.Label || s.Kind == FtfEntityKind.LineLabel);

            foreach (var pair in labels)
            {
                var stamp = pair.Value;
                if (stamp.Kind == FtfEntityKind.Label && !pointMasks) continue;
                if (stamp.Kind == FtfEntityKind.LineLabel && !lineMasks) continue;

                var entity = tr.GetObject(pair.Key, OpenMode.ForRead, false, true)
                    as AcEntity;
                if (entity == null) continue;

                var maskKind = stamp.Kind == FtfEntityKind.Label
                    ? FtfEntityKind.Mask : FtfEntityKind.LineMask;

                // Point-label masks honour the configured mask layer; line-label
                // masks sit on the label's own layer, as they always have.
                var layerId = stamp.Kind == FtfEntityKind.Label &&
                              !string.IsNullOrWhiteSpace(maskLayerName)
                    ? CadUtil.EnsureLayer(db, tr, maskLayerName)
                    : entity.LayerId;

                var maskId = MaskFor(db, tr, entity);
                if (maskId.IsNull) continue;

                var wipeout = (AcEntity)tr.GetObject(maskId, OpenMode.ForWrite);
                wipeout.LayerId = layerId;
                Ownership.Stamp(wipeout, stamp.PointNumber, rulesVersion, maskKind,
                                null, stamp.TagText);
                rebuilt++;
            }

            if (rebuilt > 0)
                ed.WriteMessage("\nMasks squared up under {0} label(s), including " +
                                "any moved by hand.", rebuilt);
        }

        /// <summary>A mask around the entity's CURRENT geometry, whatever kind of
        /// text it is.</summary>
        private static ObjectId MaskFor(Database db, Transaction tr, AcEntity entity)
        {
            var mtext = entity as MText;
            if (mtext != null)
            {
                var centre = CentreOf(mtext);
                return TextMask.Place(db, tr, mtext, centre, mtext.Rotation,
                                      mtext.TextHeight, entity.LayerId);
            }

            var dbtext = entity as DBText;
            if (dbtext != null)
            {
                var centre = CentreOf(dbtext);
                return TextMask.Place(db, tr, dbtext, centre, dbtext.Rotation,
                                      dbtext.Height, entity.LayerId);
            }

            // A leadered label: its extents include the leader line, so the box
            // comes from the embedded text instead. If the text cannot be measured
            // the label simply gets no mask -- never a wrong one.
            var leader = entity as MLeader;
            if (leader != null && leader.ContentType == ContentType.MTextContent)
            {
                try
                {
                    using (var content = leader.MText)
                    {
                        if (content == null) return ObjectId.Null;

                        var w = content.ActualWidth;
                        var h = content.ActualHeight;
                        var r = content.Rotation;

                        // TextLocation is the text's top-left corner; the mask
                        // wants the middle of the box, along the rotated axes.
                        var location = leader.TextLocation;
                        var centre = new Point3d(
                            location.X + Math.Cos(r) * w / 2 + Math.Sin(r) * h / 2,
                            location.Y + Math.Sin(r) * w / 2 - Math.Cos(r) * h / 2,
                            0.0);

                        return TextMask.PlaceBox(db, tr, leader.ObjectId, centre, r,
                                                 w, h, entity.LayerId);
                    }
                }
                catch (Autodesk.AutoCAD.Runtime.Exception)
                {
                    return ObjectId.Null;
                }
            }

            return ObjectId.Null;
        }

        /// <summary>The middle of the text's box, from its own extents -- correct
        /// for any attachment point and any rotation.</summary>
        private static Point3d CentreOf(AcEntity text)
        {
            try
            {
                var extents = text.GeometricExtents;
                return new Point3d((extents.MinPoint.X + extents.MaxPoint.X) / 2.0,
                                   (extents.MinPoint.Y + extents.MaxPoint.Y) / 2.0,
                                   0.0);
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                return CadUtil.PositionOf(text);
            }
        }
    }
}
