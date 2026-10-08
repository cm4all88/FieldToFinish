using System;
using System.Globalization;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using FieldCodes.Drafting;
using FieldCodes.Settings;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace FieldCodes.Cad
{
    /// <summary>
    /// The width between a centerline and a right-of-way line, stated on the plan: click the
    /// centerline, click the right-of-way, and the measured distance is written midway between
    /// them -- 30' for a 30.00 ft half width, 30.25' for an odd one.
    ///
    /// Measured where the right-of-way line was clicked, perpendicular from there to the
    /// centerline, so the drafter chooses the station rather than FTF choosing one. Where the
    /// two lines are not parallel there is no single width, and it says so instead of writing a
    /// number that is only true at one spot.
    ///
    /// UNTESTED against a drawing; the wording is unit tested.
    /// </summary>
    public sealed class RowWidthCommand
    {
        /// <summary>Beyond this, the two lines are not parallel enough for one width to mean anything.</summary>
        private const double ParallelToleranceDegrees = 1.0;

        [CommandMethod("FTFROWDIM", CommandFlags.Modal)]
        public void FtfRowDim()
        {
            FtfSession.Run("FTFROWDIM", (db, tr, ed) =>
            {
                var cfg = FtfSession.Rules(db);
                var settings = FtfSession.SettingsFor(db, cfg);
                var ls = settings.LineLabels;

                var placed = 0;
                while (true)
                {
                    var centerline = PickCurve(ed, tr, "\nPick the centerline (Enter to finish): ");
                    if (centerline == null) break;
                    var row = PickCurve(ed, tr, "\nPick the right-of-way line: ", out var rowPick);
                    if (row == null) continue;

                    Point3d atRow, onCenterline;
                    try
                    {
                        atRow = row.GetClosestPointTo(rowPick, false);
                        onCenterline = centerline.GetClosestPointTo(atRow, false);
                    }
                    catch (Autodesk.AutoCAD.Runtime.Exception)
                    {
                        ed.WriteMessage("\nThose two lines do not give a width here; pick again.");
                        continue;
                    }

                    var across = atRow - onCenterline;
                    if (across.Length <= 0)
                    {
                        ed.WriteMessage("\nThe right-of-way meets the centerline there, so there is no width to state.");
                        continue;
                    }

                    var apart = AngleBetween(centerline, onCenterline, row, atRow);
                    if (apart > ParallelToleranceDegrees)
                        ed.WriteMessage(
                            "\nThose lines are {0:0.0}° apart, so the width changes along them. " +
                            "This one is measured where you clicked the right-of-way.", apart);

                    var feet = across.Length / settings.General.UnitsPerFoot;
                    var text = SurveyDirection.FormatDistanceTrimmed(feet, ls.RowWidthDecimals, true);
                    var layer = FieldCodes.Linework.LabelLayerResolver.Resolve(
                        row.Layer, null, FtfLineworkService.LayerNames(db, tr), ls.DefaultLabelLayer);

                    Write(db, tr, settings, cfg.Version, text,
                          onCenterline + across * 0.5, Math.Atan2(across.Y, across.X), layer.Layer);

                    ed.WriteMessage("\nFTFROWDIM: {0} on {1}.", text, layer.Layer);
                    placed++;
                }

                if (placed > 0) ed.WriteMessage("\nFTFROWDIM: {0} width(s) stated.\n", placed);
            });
        }

        private static Curve PickCurve(Editor ed, Transaction tr, string prompt)
        {
            Point3d ignored;
            return PickCurve(ed, tr, prompt, out ignored);
        }

        private static Curve PickCurve(Editor ed, Transaction tr, string prompt, out Point3d pickedAt)
        {
            pickedAt = Point3d.Origin;
            var options = new PromptEntityOptions(prompt);
            options.SetRejectMessage("\nPick a line, polyline or arc.");
            options.AddAllowedClass(typeof(Curve), false);
            options.AllowNone = true;

            var picked = ed.GetEntity(options);
            if (picked.Status != PromptStatus.OK) return null;
            pickedAt = picked.PickedPoint;
            return tr.GetObject(picked.ObjectId, OpenMode.ForRead) as Curve;
        }

        /// <summary>How far from parallel the two lines run where they were measured.</summary>
        private static double AngleBetween(Curve a, Point3d onA, Curve b, Point3d onB)
        {
            try
            {
                var ta = a.GetFirstDerivative(onA);
                var tb = b.GetFirstDerivative(onB);
                if (ta.Length <= 0 || tb.Length <= 0) return 0;
                var degrees = ta.GetAngleTo(tb) * 180.0 / Math.PI;
                return degrees > 90 ? 180 - degrees : degrees;   // direction of travel does not matter
            }
            catch (Autodesk.AutoCAD.Runtime.Exception) { return 0; }
        }

        private static void Write(Database db, Transaction tr, FtfSettings settings, string rulesVersion,
                                  string text, Point3d at, double acrossRadians, string layerName)
        {
            var ls = settings.LineLabels;
            var scale = CadUtil.DrawingUnitsPerPlottedUnit(db);
            Ownership.EnsureRegApp(db, tr);

            // Right-reading: a width is read from the bottom or the right of the sheet, never
            // upside down, whichever way the road runs.
            var rotation = acrossRadians;
            while (rotation <= -Math.PI / 2.0) rotation += Math.PI;
            while (rotation > Math.PI / 2.0) rotation -= Math.PI;

            using (var label = new MText())
            {
                label.SetDatabaseDefaults(db);
                var style = Setup.DrawingResources.FindTextStyle(db, tr, ls.TextStyle);
                if (!style.IsNull) label.TextStyleId = style;
                label.Contents = text;
                label.TextHeight = ls.RowWidthTextPlotted * scale;
                label.Attachment = AttachmentPoint.MiddleCenter;
                label.Location = at;
                label.Rotation = rotation;
                label.LayerId = ProductionLayers.Get(db, tr, layerName, settings);
                CadUtil.AddToModelSpace(db, tr, label);
                Ownership.Stamp(label, null, rulesVersion, FtfEntityKind.RowWidth, null, "row width");
            }
        }
    }
}
