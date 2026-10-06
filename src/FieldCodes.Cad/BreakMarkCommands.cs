using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using FieldCodes.Settings;
using FieldCodes.Sheets;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace FieldCodes.Cad
{
    /// <summary>
    /// Marks that say a line carries on somewhere this sheet does not show: the tilde where a
    /// run leaves the view, and the zigzag along the seam a viewport cuts.
    ///
    /// Both are deliberate drafting rather than pipeline output, so FTFCLEAN leaves them where
    /// the drafter put them.
    ///
    /// UNTESTED against a drawing; the geometry behind the zigzag is unit tested.
    /// </summary>
    public sealed class BreakMarkCommands
    {
        // --------------------------------------------------------------- FTFTILDE

        /// <summary>
        /// Puts the tilde on a line where it runs off the view -- at a viewport edge, or
        /// anywhere else the drafter needs it. Across the line rather than along it, at the
        /// point picked, on the text layer that line's labels use.
        /// </summary>
        [CommandMethod("FTFTILDE", CommandFlags.Modal)]
        public void FtfTilde()
        {
            FtfSession.Run("FTFTILDE", (db, tr, ed) =>
            {
                var cfg = FtfSession.Rules(db);
                var settings = FtfSession.SettingsFor(db, cfg);
                var us = settings.Dips;
                var mark = string.IsNullOrWhiteSpace(us.OutsideLimitsMark) ? "~" : us.OutsideLimitsMark;

                var placed = 0;
                while (true)
                {
                    var options = new PromptEntityOptions(
                        "\nPick the line where it leaves the view (Enter to finish): ");
                    options.SetRejectMessage("\nPick a line, polyline or arc.");
                    options.AddAllowedClass(typeof(Curve), false);
                    options.AllowNone = true;

                    var picked = ed.GetEntity(options);
                    if (picked.Status != PromptStatus.OK) break;

                    var curve = tr.GetObject(picked.ObjectId, OpenMode.ForRead) as Curve;
                    if (curve == null) continue;

                    Point3d at;
                    Vector3d along;
                    try
                    {
                        at = curve.GetClosestPointTo(picked.PickedPoint, false);
                        along = curve.GetFirstDerivative(at);
                    }
                    catch (Autodesk.AutoCAD.Runtime.Exception)
                    {
                        ed.WriteMessage("\nThat point is not on the line; pick again.");
                        continue;
                    }
                    if (along.Length <= 0) continue;

                    var layer = TextLayerFor(db, tr, curve.Layer, settings, ed);
                    var id = DrawMark(db, tr, settings, cfg.Version, mark, at, Math.Atan2(along.Y, along.X), layer);
                    if (!id.IsNull) placed++;
                }

                if (placed > 0) ed.WriteMessage("\nFTFTILDE: placed {0} mark(s).\n", placed);
            });
        }

        /// <summary>
        /// Which layer a mark on this line belongs on. A tilde is not only a utility thing --
        /// it goes on paint, a fence, a lot line, anything that runs off the sheet -- so the
        /// rule has to work for all of them.
        ///
        /// A line drawn to one of the configured utility standards gets that standard's text
        /// layer, so a hand-placed mark sits with the automatic ones. Everything else goes
        /// through the same resolver the line labels use: the office's own -TEXT-E twin of the
        /// line's layer when that layer exists in this drawing, and the configured default
        /// when it does not. Nothing is invented, and the drafter is told which it chose.
        /// </summary>
        private static string TextLayerFor(Database db, Transaction tr, string lineLayer,
                                           FtfSettings settings, Editor ed)
        {
            if (string.IsNullOrWhiteSpace(lineLayer)) return lineLayer;

            foreach (var standard in settings.Dips.Systems)
            {
                if (!string.Equals(standard.PipeLayer, lineLayer, StringComparison.OrdinalIgnoreCase)) continue;
                if (string.IsNullOrWhiteSpace(standard.LabelLayer)) break;
                return standard.LabelLayer;
            }

            var resolution = FieldCodes.Linework.LabelLayerResolver.Resolve(
                lineLayer, null, FtfLineworkService.LayerNames(db, tr),
                settings.LineLabels.DefaultLabelLayer);
            ed.WriteMessage("\nLayer: {0}", resolution.Describe());
            return resolution.Layer;
        }

        private static ObjectId DrawMark(Database db, Transaction tr, FtfSettings settings,
                                         string rulesVersion, string mark, Point3d at,
                                         double alongRadians, string layerName)
        {
            var us = settings.Dips;
            var scale = CadUtil.DrawingUnitsPerPlottedUnit(db);
            Ownership.EnsureRegApp(db, tr);

            using (var text = new MText())
            {
                text.SetDatabaseDefaults(db);
                var style = Setup.DrawingResources.FindTextStyle(db, tr, us.TextStyle);
                if (!style.IsNull) text.TextStyleId = style;
                text.Contents = mark;
                text.TextHeight = us.TextHeightPlotted * scale;
                text.Attachment = AttachmentPoint.MiddleCenter;
                text.Location = at;
                // Across the line, the way a drafter ticks a run that carries on.
                text.Rotation = alongRadians + Math.PI / 2.0;
                text.LayerId = ProductionLayers.Get(db, tr, layerName, settings);
                CadUtil.AddToModelSpace(db, tr, text);
                Ownership.Stamp(text, null, rulesVersion, FtfEntityKind.BreakMark, null, "break mark");
                return text.ObjectId;
            }
        }

        // ----------------------------------------------------------- FTFMATCHLINE

        /// <summary>
        /// The zigzag along the seam a viewport cuts, drawn in model space so both sheets show
        /// the same line from their own side. Run it on the layout: pick the viewport, then the
        /// side the match line follows.
        ///
        /// The teeth are sized in plotted units and converted through the viewport's own scale,
        /// so a 1" = 20' sheet and a 1" = 100' sheet draw the same zigzag on paper.
        /// </summary>
        [CommandMethod("FTFMATCHLINE", CommandFlags.Modal)]
        public void FtfMatchLine()
        {
            FtfSession.Run("FTFMATCHLINE", (db, tr, ed) =>
            {
                var cfg = FtfSession.Rules(db);
                var settings = FtfSession.SettingsFor(db, cfg);
                var sheets = settings.Sheets;

                if (db.TileMode)
                {
                    ed.WriteMessage("\nFTFMATCHLINE: switch to the layout with the viewport on it, " +
                                    "then run this again.\n");
                    return;
                }

                var options = new PromptEntityOptions("\nPick the viewport the match line follows: ");
                options.SetRejectMessage("\nPick a viewport.");
                options.AddAllowedClass(typeof(Viewport), true);
                var picked = ed.GetEntity(options);
                if (picked.Status != PromptStatus.OK) return;

                var viewport = (Viewport)tr.GetObject(picked.ObjectId, OpenMode.ForRead);
                if (viewport.Height <= 0 || viewport.ViewHeight <= 0)
                {
                    ed.WriteMessage("\nFTFMATCHLINE: that viewport has no view to follow.\n");
                    return;
                }

                var side = ed.GetPoint("\nPick the side of the viewport the match line follows: ");
                if (side.Status != PromptStatus.OK) return;

                // Which edge was meant is decided on the sheet, where the drafter is looking.
                var onPaper = PaperCorners(viewport);
                var index = BreakLine.NearestSide(onPaper, side.Value.X, side.Value.Y);

                var scale = viewport.ViewHeight / viewport.Height;      // model units per paper unit
                var inModel = BreakLine.ViewportCornersInModel(
                    viewport.CenterPoint.X, viewport.CenterPoint.Y,
                    viewport.Width, viewport.Height,
                    viewport.ViewCenter.X, viewport.ViewCenter.Y,
                    scale, viewport.TwistAngle);

                var from = inModel[index];
                var to = inModel[(index + 1) % inModel.Count];
                var zig = BreakLine.Zigzag(from.X, from.Y, to.X, to.Y,
                                           sheets.MatchlineZigAmplitudePlotted * scale,
                                           sheets.MatchlineZigPeriodPlotted * scale);

                Ownership.EnsureRegApp(db, tr);
                using (var line = new Polyline())
                {
                    line.SetDatabaseDefaults(db);
                    for (var i = 0; i < zig.Count; i++)
                        line.AddVertexAt(i, new Point2d(zig[i].X, zig[i].Y), 0, 0, 0);
                    line.LayerId = ProductionLayers.Get(db, tr, sheets.MatchlineLayer, settings);
                    CadUtil.AddToModelSpace(db, tr, line);
                    Ownership.Stamp(line, null, cfg.Version, FtfEntityKind.BreakLine, null, "match line");
                }

                ed.WriteMessage("\nFTFMATCHLINE: drawn in model space on {0}, {1} ft long, " +
                                "teeth {2}\" on the sheet.\n",
                                sheets.MatchlineLayer,
                                Distance(from, to).ToString("0.0", CultureInfo.InvariantCulture),
                                sheets.MatchlineZigAmplitudePlotted.ToString("0.00", CultureInfo.InvariantCulture));
            });
        }

        private static IList<BreakPoint> PaperCorners(Viewport viewport)
        {
            var halfWidth = viewport.Width / 2.0;
            var halfHeight = viewport.Height / 2.0;
            var x = viewport.CenterPoint.X;
            var y = viewport.CenterPoint.Y;
            return new List<BreakPoint>
            {
                new BreakPoint(x - halfWidth, y - halfHeight),
                new BreakPoint(x + halfWidth, y - halfHeight),
                new BreakPoint(x + halfWidth, y + halfHeight),
                new BreakPoint(x - halfWidth, y + halfHeight)
            };
        }

        private static double Distance(BreakPoint a, BreakPoint b)
        {
            return Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
        }
    }
}
