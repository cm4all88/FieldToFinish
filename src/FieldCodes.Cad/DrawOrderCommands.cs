using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;

using AcEntity = Autodesk.AutoCAD.DatabaseServices.Entity;

namespace FieldCodes.Cad
{
    /// <summary>
    /// Draw-order banding.
    ///
    /// Its own command because plenty of other operations disturb draw order --
    /// exploding a block, pasting, a civil object rebuilding -- and the fix needs to
    /// be one word rather than a re-run of everything.
    ///
    /// Bands, bottom to top: maskable linework, masks, protected symbols, labels.
    /// Entities are classified by layer through LayerClassifier (which strips modifier
    /// suffixes), never by block name -- a block called TREE-DECIDUOUS-CLUSTER tells
    /// you nothing about what should sit on top of it.
    ///
    /// UNTESTED: never run against a drawing.
    /// </summary>
    public sealed class DrawOrderCommands
    {
        [CommandMethod("FTFORDER", CommandFlags.Modal)]
        public void FtfOrder()
        {
            FtfSession.Run("FTFORDER", (db, tr, ed) =>
            {
                var cfg = FtfSession.Rules(db);

                // Applies the protected/maskable layer lists from settings onto the
                // rules object the classifier reads.
                var settings = FtfSession.SettingsFor(db, cfg);

                // Masks first, order second: any label the surveyor nudged gets its
                // mask rebuilt around the new position, and the fresh masks are
                // then banded with everything else.
                MaskSync.Rebuild(db, tr, ed, settings, cfg.Version);

                var classifier = new LayerClassifier(cfg);

                var ms = CadUtil.ModelSpace(db, tr, OpenMode.ForWrite);

                // Four buckets, indexed by DrawOrderBand.
                var bands = new List<ObjectId>[4];
                for (var i = 0; i < bands.Length; i++) bands[i] = new List<ObjectId>();

                var counted = 0;

                foreach (ObjectId id in ms)
                {
                    if (id.IsErased) continue;

                    var entity = tr.GetObject(id, OpenMode.ForRead, false, true) as AcEntity;
                    if (entity == null) continue;

                    bands[(int)BandFor(entity, classifier)].Add(id);
                    counted++;
                }

                // DrawOrderTable is the managed wrapper over the block's sortents table.
                var sortEnts = (DrawOrderTable)tr.GetObject(ms.DrawOrderTableId, OpenMode.ForWrite);

                // MoveToTop preserves the relative order of the ids it is given, so
                // raising the bands in ascending order leaves labels on top.
                for (var band = 0; band < bands.Length; band++)
                {
                    if (bands[band].Count == 0) continue;

                    using (var ids = new ObjectIdCollection(bands[band].ToArray()))
                    {
                        sortEnts.MoveToTop(ids);
                    }
                }

                ed.WriteMessage(
                    "\nFTFORDER: {0} entities banded -- {1} linework, {2} mask(s), " +
                    "{3} symbol(s), {4} label(s).\n",
                    counted,
                    bands[(int)DrawOrderBand.MaskableLinework].Count,
                    bands[(int)DrawOrderBand.Mask].Count,
                    bands[(int)DrawOrderBand.Symbol].Count,
                    bands[(int)DrawOrderBand.Label].Count);
            });
        }

        /// <summary>
        /// Masks are ours and are identified by XData, since a wipeout's layer says
        /// nothing useful. Everything else is classified by layer.
        /// </summary>
        internal static DrawOrderBand BandFor(AcEntity entity, LayerClassifier classifier)
        {
            // CogoPoints carry the tree symbol via their point style, so they belong in
            // the symbol band regardless of which layer the point group puts them on.
            if (entity is Autodesk.Civil.DatabaseServices.CogoPoint)
                return DrawOrderBand.Symbol;

            var stamp = Ownership.Read(entity);
            if (stamp != null)
            {
                if (stamp.Kind == FtfEntityKind.Mask || stamp.Kind == FtfEntityKind.LineMask)
                    return DrawOrderBand.Mask;

                // Line labels live on a layer no code rule derives, so band them by
                // what they are rather than falling through to layer classification.
                if (stamp.Kind == FtfEntityKind.LineLabel)
                    return DrawOrderBand.Label;
            }

            // A wipeout we do not own is still a mask as far as ordering goes.
            if (entity is Wipeout && stamp == null)
                return DrawOrderBand.Mask;

            return LayerClassifier.BandOf(classifier.Classify(entity.Layer));
        }
    }
}
