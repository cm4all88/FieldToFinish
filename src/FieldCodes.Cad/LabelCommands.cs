using System;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using FieldCodes.Geometry;

using AcEntity = Autodesk.AutoCAD.DatabaseServices.Entity;

namespace FieldCodes.Cad
{
    /// <summary>
    /// Label placement and collision avoidance.
    ///
    /// The search itself lives in FieldCodes.Geometry.LabelPlacer and is unit tested.
    /// This class gathers obstacles out of the drawing, sizes boxes at plot scale, and
    /// draws the results.
    ///
    /// Re-run behaviour: a label whose actual position no longer matches the position
    /// stored in its XData was moved by hand. It is left exactly where it is, and
    /// becomes a hard obstacle so the automatic ones route around the human's choice.
    ///
    /// UNTESTED: never run against a drawing.
    /// </summary>
    public sealed class LabelCommands
    {
        [CommandMethod("FTFLABELS", CommandFlags.Modal)]
        public void FtfLabels()
        {
            FtfSession.Run("FTFLABELS", (db, tr, ed) =>
            {
                var cfg = FtfSession.Rules(db);
                var settings = FtfSession.SettingsFor(db, cfg);
                var lp = settings.Labels;

                var parser = new FieldCodeParser(cfg);
                var classifier = new LayerClassifier(cfg);

                Ownership.EnsureRegApp(db, tr);

                var scale = CadUtil.DrawingUnitsPerPlottedUnit(db);
                var textHeight = lp.TextHeightPlotted * scale;
                var padding = lp.PaddingPlotted * scale;
                var styleId = Setup.DrawingResources.FindTextStyle(db, tr, lp.TextStyle);

                if (!string.IsNullOrWhiteSpace(lp.TextStyle) && styleId.IsNull)
                    ed.WriteMessage(
                        "\nFTFLABELS: text style '{0}' is not in this drawing; using the " +
                        "current style instead.", lp.TextStyle);

                var placer = new LabelPlacer
                {
                    BaseOffset = lp.BaseOffsetPlotted * scale,
                    RingStep = lp.RingStepPlotted * scale,
                    RingCount = Math.Max(1, lp.RingCount)
                };

                // Labels a human has moved stay put and become obstacles.
                var keptByHand = KeepHandMovedLabels(db, tr, settings.Cleanup.MovedTolerance);

                Ownership.DeleteOwned(db, tr,
                    s => (s.Kind == FtfEntityKind.Label ||
                          s.Kind == FtfEntityKind.Leader ||
                          s.Kind == FtfEntityKind.Mask) &&
                         !keptByHand.Contains(s.PointNumber));

                var obstacles = GatherObstacles(db, tr, classifier);

                var placed = 0;
                var leaders = 0;
                var failed = 0;

                foreach (var cogo in CadUtil.CogoPointsInOrder(db, tr))
                {
                    var number = cogo.PointNumber.ToString(CultureInfo.InvariantCulture);
                    if (keptByHand.Contains(number)) continue;

                    var parsed = parser.Parse(number, cogo.RawDescription);
                    if (parsed.HasErrors) continue;
                    if (string.IsNullOrEmpty(parsed.LabelText)) continue;

                    var anchor = CadUtil.Flatten(cogo.Location);

                    // The text is created first so its real size can be measured. A
                    // character-width estimate is only right for one font; with any
                    // other style the boxes clear each other and the glyphs do not.
                    var text = NewLabelText(db, tr, parsed, textHeight, styleId, anchor);

                    double w, h, offsetX, offsetY;
                    if (!CadUtil.TryMeasureText(text, out w, out h, out offsetX, out offsetY))
                    {
                        text.Erase();
                        failed++;
                        ed.WriteMessage("\n  point {0}: label text has no measurable size.", number);
                        continue;
                    }

                    var placement = placer.Place(anchor.X, anchor.Y,
                                                 w + padding * 2.0, h + padding * 2.0,
                                                 obstacles);

                    if (!placement.Placed)
                    {
                        // No fallback position. A label dumped on top of a protected
                        // symbol is worse than a label that is reported as missing.
                        text.Erase();
                        failed++;
                        ed.WriteMessage(
                            "\n  point {0}: no clear label position found.", number);
                        continue;
                    }

                    // Move the text so its *extents*, not its insertion point, land
                    // where the placer decided.
                    var target = new Point3d(
                        placement.Bounds.MinX + padding - offsetX,
                        placement.Bounds.MinY + padding - offsetY,
                        0.0);

                    var wantsLeader =
                        parsed.Leader != LeaderMode.Never &&
                        (parsed.Leader == LeaderMode.Always ||
                         (parsed.Leader == LeaderMode.Auto && placement.NeedsLeader));

                    if (wantsLeader)
                    {
                        // Text and leader as ONE multileader, styled by the drawing's
                        // current multileader style: grip-move the text and the
                        // leader re-routes with it. The measuring MText is replaced.
                        text.Erase();

                        using (var leadered = CadUtil.NewLeaderedLabel(db, tr,
                                   parsed.LabelText, textHeight, styleId,
                                   CadUtil.EnsureLayer(db, tr, parsed.LabelLayer),
                                   target, anchor))
                        {
                            Ownership.Stamp(leadered, parsed.PointNumber, cfg.Version,
                                            FtfEntityKind.Label, target);
                            if (lp.DrawMask)
                                DrawMask(db, tr, parsed, placement, lp.MaskLayer,
                                         cfg.Version, leadered.ObjectId);
                        }
                        leaders++;
                    }
                    else
                    {
                        text.Location = target;
                        Ownership.Stamp(text, parsed.PointNumber, cfg.Version,
                                        FtfEntityKind.Label, target);
                        if (lp.DrawMask)
                            DrawMask(db, tr, parsed, placement, lp.MaskLayer,
                                     cfg.Version, text.ObjectId);
                    }

                    placed++;

                    // Each label becomes a hard obstacle for the ones after it.
                    obstacles.Add(new Obstacle(placement.Bounds, ObstacleClass.Hard,
                                               "label:" + number));
                }

                ed.WriteMessage(
                    "\nFTFLABELS: {0} placed, {1} leader(s), {2} kept as hand-placed, " +
                    "{3} with no clear position.\n",
                    placed, leaders, keptByHand.Count, failed);

                // Label and mask are a pair: rebuild every mask around where its
                // label ACTUALLY is, so a hand-moved label's mask follows it.
                MaskSync.Rebuild(db, tr, ed, settings, cfg.Version);
            });
        }

        // ------------------------------------------------------------ hand-moved rule

        /// <summary>
        /// Finds labels sitting somewhere other than where we last computed. Those were
        /// moved deliberately; we neither delete nor re-place them.
        /// </summary>
        private static HashSet<string> KeepHandMovedLabels(Database db, Transaction tr,
                                                           double tolerance)
        {
            var kept = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var owned = Ownership.FindOwned(db, tr, s => s.Kind == FtfEntityKind.Label);

            foreach (var pair in owned)
            {
                var stamp = pair.Value;
                if (!stamp.ComputedPosition.HasValue) continue;

                var entity = tr.GetObject(pair.Key, OpenMode.ForRead, false, true) as AcEntity;
                if (entity == null) continue;

                Point3d actual;
                if (!TryAnchorOf(entity, out actual)) continue;

                if (stamp.WasMovedByHand(actual, tolerance))
                    kept.Add(stamp.PointNumber);
            }

            return kept;
        }

        private static bool TryAnchorOf(AcEntity entity, out Point3d position)
        {
            var text = entity as DBText;
            if (text != null) { position = text.Position; return true; }

            var mtext = entity as MText;
            if (mtext != null) { position = mtext.Location; return true; }

            // A leadered label is one multileader: its anchor for hand-move
            // detection is where the text sits, not where the arrow points.
            var mleader = entity as MLeader;
            if (mleader != null) { position = mleader.TextLocation; return true; }

            position = Point3d.Origin;
            return false;
        }

        // ------------------------------------------------------------------ obstacles

        /// <summary>
        /// Every existing entity, boxed and classified. Entities we own are excluded --
        /// they are about to be redrawn -- except hand-moved labels, which
        /// <see cref="KeepHandMovedLabels"/> has already left in place and which are
        /// picked up here as hard obstacles.
        /// </summary>
        private static List<Obstacle> GatherObstacles(Database db, Transaction tr,
                                                      LayerClassifier classifier)
        {
            var obstacles = new List<Obstacle>();
            var ms = CadUtil.ModelSpace(db, tr, OpenMode.ForRead);

            foreach (ObjectId id in ms)
            {
                if (id.IsErased) continue;

                var entity = tr.GetObject(id, OpenMode.ForRead, false, true) as AcEntity;
                if (entity == null) continue;

                Box2d box;
                if (!TryBox(entity, out box)) continue;

                // A CogoPoint draws its own symbol and label through its point style,
                // and that symbol is the tree. It is not ours and it is on whatever
                // layer the point group uses, so classify it by type: never cover it.
                if (entity is Autodesk.Civil.DatabaseServices.CogoPoint)
                {
                    obstacles.Add(new Obstacle(box, ObstacleClass.Hard, "cogopoint"));
                    continue;
                }

                var stamp = Ownership.Read(entity);

                ObstacleClass cls;
                if (stamp != null)
                {
                    // Our own symbols must not be covered; our labels are hard too.
                    // Line labels are position-fixed on their line, so point labels
                    // route around them, never the other way around.
                    if (stamp.Kind == FtfEntityKind.Block ||
                        stamp.Kind == FtfEntityKind.Label ||
                        stamp.Kind == FtfEntityKind.LineLabel)
                        cls = ObstacleClass.Hard;
                    else
                        continue;   // drip arcs, masks, leaders: not obstacles
                }
                else
                {
                    cls = classifier.ObstacleForLayer(entity.Layer);
                }

                obstacles.Add(new Obstacle(box, cls, entity.Layer));
            }

            return obstacles;
        }

        /// <summary>
        /// Bounding box of an entity. Some entities have no extents (empty blocks,
        /// degenerate geometry) and throw rather than returning anything useful.
        /// </summary>
        private static bool TryBox(AcEntity entity, out Box2d box)
        {
            box = default(Box2d);
            try
            {
                var e = entity.GeometricExtents;
                box = new Box2d(e.MinPoint.X, e.MinPoint.Y, e.MaxPoint.X, e.MaxPoint.Y);
                return true;
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                return false;
            }
        }

        // ------------------------------------------------------------------ drawing

        /// <summary>
        /// Creates the label text at the anchor and adds it to the drawing so it can be
        /// measured. The caller moves it once the placer has chosen a home. MTEXT, per
        /// the office standard for label text.
        /// </summary>
        private static MText NewLabelText(Database db, Transaction tr, ParsedPoint parsed,
                                          double textHeight, ObjectId styleId, Point3d anchor)
        {
            var text = new MText();
            text.SetDatabaseDefaults(db);

            if (!styleId.IsNull) text.TextStyleId = styleId;

            text.Contents = parsed.LabelText;
            text.TextHeight = textHeight;
            text.Location = anchor;
            text.LayerId = CadUtil.EnsureLayer(db, tr, parsed.LabelLayer);

            CadUtil.AddToModelSpace(db, tr, text);
            return text;
        }

        private static void DrawMask(Database db, Transaction tr, ParsedPoint parsed,
                                     LabelPlacement placement, string maskLayer,
                                     string rulesVersion, ObjectId textId)
        {
            var b = placement.Bounds;

            var points = new Point2dCollection
            {
                new Point2d(b.MinX, b.MinY),
                new Point2d(b.MaxX, b.MinY),
                new Point2d(b.MaxX, b.MaxY),
                new Point2d(b.MinX, b.MaxY),
                new Point2d(b.MinX, b.MinY)    // closed
            };

            using (var wipeout = new Wipeout())
            {
                wipeout.SetDatabaseDefaults(db);
                wipeout.SetFrom(points, Vector3d.ZAxis);
                wipeout.LayerId = CadUtil.EnsureLayer(db, tr,
                    string.IsNullOrWhiteSpace(maskLayer) ? parsed.LabelLayer : maskLayer);

                CadUtil.AddToModelSpace(db, tr, wipeout);
                Ownership.Stamp(wipeout, parsed.PointNumber, rulesVersion,
                                FtfEntityKind.Mask, null);

                // The wipeout was added AFTER its text, so by default it would draw
                // on top and blank the label out. Order the pair at creation --
                // geometry, then mask, then text -- so FTFLABELS is correct on its
                // own, not only after a draw-order pass.
                var ms = CadUtil.ModelSpace(db, tr, OpenMode.ForRead);
                var drawOrder = (DrawOrderTable)tr.GetObject(ms.DrawOrderTableId,
                                                             OpenMode.ForWrite);
                using (var ids = new ObjectIdCollection())
                {
                    ids.Add(wipeout.ObjectId);
                    drawOrder.MoveBelow(ids, textId);
                }
            }
        }

    }
}
