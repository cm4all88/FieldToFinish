using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using FieldCodes.Geometry;
using FieldCodes.Linework;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcEntity = Autodesk.AutoCAD.DatabaseServices.Entity;

namespace FieldCodes.Cad
{
    /// <summary>
    /// Plan-furniture commands: spot elevations off the surface, the control
    /// table, and the legend. Everything drawn is FTF-owned annotation; the
    /// surface, points and linework are only read.
    ///
    /// UNTESTED against a drawing; the placement and derivation maths is unit
    /// tested.
    /// </summary>
    public sealed class SpotCommands
    {
        // -------------------------------------------------------------- FTFSPOT

        /// <summary>
        /// Spot elevations, fast: click, click, click. Each click puts an X on
        /// the spot, reads the elevation off the TIN surface, and sets the value
        /// beside it at the configured angle and height (Leroy 60 by default).
        /// Dense clusters -- wheelchair ramps -- stay legible: each new text
        /// dodges the ones already placed, deterministically. Esc finishes.
        /// </summary>
        [CommandMethod("FTFSPOT", CommandFlags.Modal)]
        public void FtfSpot()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            var ed = doc.Editor;
            var db = doc.Database;

            RulesConfig cfg;
            FieldCodes.Settings.FtfSettings settings;
            try
            {
                cfg = FtfSession.Rules(db);
                settings = FtfSession.SettingsFor(db, cfg);
            }
            catch (ConfigException ex)
            {
                ed.WriteMessage("\nRules problem: {0}\n", ex.Message);
                return;
            }

            var sp = settings.Spots;
            var scale = CadUtil.DrawingUnitsPerPlottedUnit(db);
            var textHeight = sp.TextPlotted * scale;
            var markerHalf = sp.MarkerPlotted * scale / 2.0;
            var angle = sp.AngleDegrees * Math.PI / 180.0;
            var clearance = markerHalf + textHeight;
            var format = "F" + Math.Max(0, Math.Min(4, sp.Decimals))
                .ToString(CultureInfo.InvariantCulture);

            ObjectId surfaceId;
            string surfaceName;
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                surfaceId = FindSurface(db, tr, out surfaceName);
                tr.Commit();
            }
            if (surfaceId.IsNull)
            {
                ed.WriteMessage("\nFTFSPOT: no TIN surface in this drawing to read " +
                                "elevations from.\n");
                return;
            }
            ed.WriteMessage("\nFTFSPOT: elevations from surface \"{0}\"; text at " +
                            "{1:0}°, {2:0.00}\" plotted. Esc to finish.",
                            surfaceName, sp.AngleDegrees, sp.TextPlotted);

            // Everything already placed is an obstacle, so a second session packs
            // in cleanly beside the first.
            var occupied = new List<SpotBox>();
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (var pair in Ownership.FindOwned(db, tr,
                             s => s.Kind == FtfEntityKind.Spot))
                {
                    var text = tr.GetObject(pair.Key, OpenMode.ForRead, false, true)
                        as MText;
                    if (text == null) continue;
                    try
                    {
                        var e = text.GeometricExtents;
                        occupied.Add(new SpotBox
                        {
                            MinX = e.MinPoint.X, MinY = e.MinPoint.Y,
                            MaxX = e.MaxPoint.X, MaxY = e.MaxPoint.Y
                        });
                    }
                    catch (Autodesk.AutoCAD.Runtime.Exception) { }
                }
                tr.Commit();
            }

            var placed = 0;
            while (true)
            {
                var picked = ed.GetPoint(new PromptPointOptions(
                    "\nSpot elevation point (Esc to finish): "));
                if (picked.Status != PromptStatus.OK) break;

                using (doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    if (PlaceSpot(db, tr, ed, surfaceId, picked.Value, sp, scale,
                                  textHeight, markerHalf, angle, clearance, format,
                                  occupied, cfg.Version))
                        placed++;
                    tr.Commit();
                }
            }

            ed.WriteMessage("\nFTFSPOT: {0} spot elevation(s) placed.\n", placed);
        }

        private static bool PlaceSpot(Database db, Transaction tr, Editor ed,
                                      ObjectId surfaceId, Point3d picked,
                                      FieldCodes.Settings.SpotSettings sp,
                                      double scale, double textHeight,
                                      double markerHalf, double angle,
                                      double clearance, string format,
                                      List<SpotBox> occupied, string rulesVersion)
        {
            double elevation;
            try
            {
                var surface = (Autodesk.Civil.DatabaseServices.TinSurface)
                    tr.GetObject(surfaceId, OpenMode.ForRead);
                elevation = surface.FindElevationAtXY(picked.X, picked.Y);
            }
            catch (System.Exception)
            {
                ed.WriteMessage("\n  Outside the surface; no spot placed there.");
                return false;
            }

            var value = elevation.ToString(format, CultureInfo.InvariantCulture);
            Ownership.EnsureRegApp(db, tr);

            var markerLayer = CadUtil.EnsureLayer(db, tr, sp.MarkerLayer);
            var x = picked.X;
            var y = picked.Y;

            foreach (var diagonal in new[] { 1.0, -1.0 })
            {
                using (var stroke = new Line(
                    new Point3d(x - markerHalf, y - diagonal * markerHalf, 0),
                    new Point3d(x + markerHalf, y + diagonal * markerHalf, 0)))
                {
                    stroke.LayerId = markerLayer;
                    CadUtil.AddToModelSpace(db, tr, stroke);
                    Ownership.Stamp(stroke, value, rulesVersion, FtfEntityKind.Spot,
                                    null);
                }
            }

            var textLayer = CadUtil.EnsureLayer(db, tr, sp.TextLayer);
            using (var text = new MText())
            {
                text.SetDatabaseDefaults(db);
                text.Contents = value;
                text.TextHeight = textHeight;
                text.Attachment = AttachmentPoint.MiddleCenter;
                text.Rotation = angle;
                text.Location = new Point3d(x, y, 0);
                text.LayerId = textLayer;
                CadUtil.AddToModelSpace(db, tr, text);

                // Measure the real rotated box, then let the placer find clear
                // ground beside the X -- the standard perch first, alternates when
                // a neighbouring spot is already there.
                double w, h, ox, oy;
                if (!CadUtil.TryMeasureText(text, out w, out h, out ox, out oy))
                {
                    w = textHeight * value.Length * 0.8;
                    h = textHeight * 1.4;
                }

                double tx, ty;
                SpotPlacer.Place(x, y, angle, clearance + Math.Min(w, h) / 2.0,
                                 w, h, occupied, out tx, out ty);
                text.Location = new Point3d(tx, ty, 0);

                Ownership.Stamp(text, value, rulesVersion, FtfEntityKind.Spot,
                                new Point3d(tx, ty, 0));
            }

            ed.WriteMessage("\n  {0}", value);
            return true;
        }

        private static ObjectId FindSurface(Database db, Transaction tr,
                                            out string name)
        {
            name = null;
            var ms = CadUtil.ModelSpace(db, tr, OpenMode.ForRead);
            foreach (ObjectId id in ms)
            {
                if (id.IsErased) continue;
                var surface = tr.GetObject(id, OpenMode.ForRead, false, true)
                    as Autodesk.Civil.DatabaseServices.TinSurface;
                if (surface == null) continue;
                name = surface.Name;
                return id;
            }
            return ObjectId.Null;
        }

        // ----------------------------------------------------------- FTFCONTROL

        /// <summary>
        /// The control table: every control shot and found monument (the codes in
        /// controlCodes), with point number, northing, easting, elevation and the
        /// raw note. Re-running keeps the table where it is, like the schedule.
        /// </summary>
        [CommandMethod("FTFCONTROL", CommandFlags.Modal)]
        public void FtfControl()
        {
            FtfSession.Run("FTFCONTROL", (db, tr, ed) =>
            {
                var cfg = FtfSession.Rules(db);
                var settings = FtfSession.SettingsFor(db, cfg);

                var codes = new HashSet<string>(
                    (cfg.ControlCodes ?? new List<string>())
                        .Where(c => !string.IsNullOrWhiteSpace(c))
                        .Select(c => c.Trim()),
                    StringComparer.OrdinalIgnoreCase);
                if (codes.Count == 0)
                {
                    ed.WriteMessage("\nFTFCONTROL: no controlCodes configured in the " +
                                    "rules.\n");
                    return;
                }

                // Latitude/longitude come from the drawing's assigned coordinate
                // system, when it has one -- never computed by guesswork.
                GeoLocationData geo = null;
                try
                {
                    var geoId = db.GeoDataObject;
                    if (!geoId.IsNull)
                        geo = tr.GetObject(geoId, OpenMode.ForRead) as GeoLocationData;
                }
                catch (Autodesk.AutoCAD.Runtime.Exception) { }

                var rows = new List<string[]>();
                var export = new List<string[]>();
                foreach (var cogo in CadUtil.CogoPointsInOrder(db, tr))
                {
                    var raw = cogo.RawDescription ?? string.Empty;
                    var token = raw.Split(new[] { ' ', '\t' },
                        StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                    if (token == null || !codes.Contains(token)) continue;

                    rows.Add(new[]
                    {
                        cogo.PointNumber.ToString(CultureInfo.InvariantCulture),
                        cogo.Northing.ToString("F2", CultureInfo.InvariantCulture),
                        cogo.Easting.ToString("F2", CultureInfo.InvariantCulture),
                        cogo.Elevation.ToString("F2", CultureInfo.InvariantCulture),
                        raw
                    });

                    var latitude = string.Empty;
                    var longitude = string.Empty;
                    if (geo != null)
                    {
                        try
                        {
                            var lla = geo.TransformToLonLatAlt(
                                new Point3d(cogo.Easting, cogo.Northing,
                                            cogo.Elevation));
                            longitude = lla.X.ToString("F8", CultureInfo.InvariantCulture);
                            latitude = lla.Y.ToString("F8", CultureInfo.InvariantCulture);
                        }
                        catch (System.Exception) { }
                    }

                    export.Add(new[]
                    {
                        rows[rows.Count - 1][0], rows[rows.Count - 1][1],
                        rows[rows.Count - 1][2], rows[rows.Count - 1][3],
                        latitude, longitude, raw
                    });
                }

                if (rows.Count == 0)
                {
                    ed.WriteMessage("\nFTFCONTROL: no control points found.\n");
                    return;
                }

                // Keep the position an earlier table (or the surveyor) chose.
                var position = Point3d.Origin;
                var havePosition = false;
                foreach (var pair in Ownership.FindOwned(db, tr,
                             s => s.Kind == FtfEntityKind.ControlTable))
                {
                    var previous = tr.GetObject(pair.Key, OpenMode.ForRead, false, true)
                        as Table;
                    if (previous != null)
                    {
                        position = previous.Position;
                        havePosition = true;
                    }
                }
                Ownership.DeleteOwnedKinds(db, tr, FtfEntityKind.ControlTable);

                if (!havePosition)
                {
                    var picked = ed.GetPoint(new PromptPointOptions(
                        "\nTop-left corner for the control table: "));
                    if (picked.Status != PromptStatus.OK)
                    {
                        ed.WriteMessage("\nCancelled; no table drawn.\n");
                        return;
                    }
                    position = picked.Value;
                }

                DrawControlTable(db, tr, rows, position,
                    settings.Tags.TableTextHeightPlotted *
                    CadUtil.DrawingUnitsPerPlottedUnit(db), cfg.Version);

                ed.WriteMessage("\nFTFCONTROL: {0} control point(s) tabled.\n",
                                rows.Count);

                ExportControlCsv(db, ed, export, geo != null);
            });
        }

        /// <summary>
        /// The control export beside the drawing: everything in the table plus
        /// latitude and longitude when the drawing carries a coordinate system.
        /// Without one the columns stay empty and the message says how to fix it.
        /// </summary>
        private static void ExportControlCsv(Database db, Editor ed,
                                             IList<string[]> export, bool hasGeo)
        {
            if (string.IsNullOrEmpty(db.Filename)) return;

            var path = System.IO.Path.ChangeExtension(db.Filename, null) +
                       ".ftf-control.csv";
            var lines = new List<string>
            {
                "Point,Northing,Easting,Elevation,Latitude,Longitude,Description"
            };
            foreach (var row in export)
                lines.Add(string.Join(",", row.Select(Csv).ToArray()));

            try
            {
                System.IO.File.WriteAllLines(path, lines.ToArray(),
                    new System.Text.UTF8Encoding(true));
                ed.WriteMessage("\nControl export: {0}", path);
                if (!hasGeo)
                    ed.WriteMessage("\n  Latitude/longitude left empty: this " +
                                    "drawing has no coordinate system assigned. " +
                                    "Set one (GEOGRAPHICLOCATION, or the zone in " +
                                    "Drawing Settings) and run FTFCONTROL again.");
                ed.WriteMessage("\n");
            }
            catch (System.IO.IOException ex)
            {
                ed.WriteMessage("\nCould not write {0}: {1}\n", path, ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                ed.WriteMessage("\nCould not write {0}: {1}\n", path, ex.Message);
            }
        }

        private static string Csv(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0
                ? "\"" + value.Replace("\"", "\"\"") + "\""
                : value;
        }

        private static void DrawControlTable(Database db, Transaction tr,
                                             IList<string[]> rows, Point3d position,
                                             double textHeight, string rulesVersion)
        {
            var headers = new[] { "PT", "NORTHING", "EASTING", "ELEV", "DESCRIPTION" };

            using (var table = new Table())
            {
                table.SetDatabaseDefaults(db);
                table.TableStyle = db.Tablestyle;
                table.Position = position;
                table.SetSize(rows.Count + 2, headers.Length);
                foreach (var tableRow in table.Rows) tableRow.Height = textHeight * 2.0;

                table.Cells[0, 0].TextString = "CONTROL TABLE";
                table.Cells[0, 0].TextHeight = textHeight * 1.2;

                for (var c = 0; c < headers.Length; c++)
                {
                    table.Cells[1, c].TextString = headers[c];
                    table.Cells[1, c].TextHeight = textHeight;
                }

                for (var r = 0; r < rows.Count; r++)
                for (var c = 0; c < headers.Length; c++)
                {
                    table.Cells[r + 2, c].TextString = rows[r][c];
                    table.Cells[r + 2, c].TextHeight = textHeight;
                }

                // Columns sized to their widest entry so nothing wraps.
                for (var c = 0; c < headers.Length; c++)
                {
                    var widest = headers[c].Length;
                    foreach (var row in rows)
                        widest = Math.Max(widest, row[c].Length);
                    table.Columns[c].Width = textHeight * (widest * 0.9 + 2);
                }

                table.LayerId = CadUtil.EnsureLayer(db, tr, "V-CTRL-TABL");
                table.GenerateLayout();
                CadUtil.AddToModelSpace(db, tr, table);
                Ownership.EnsureRegApp(db, tr);
                Ownership.Stamp(table, "control", rulesVersion,
                                FtfEntityKind.ControlTable, position);
            }
        }

        // ------------------------------------------------------------ FTFLEGEND

        /// <summary>
        /// The legend, derived from what this drawing actually contains: every
        /// identified line feature, drawn as a sample of its own layer's
        /// symbology beside its name. Re-running keeps the legend where it is.
        /// Point symbols live inside Civil 3D point styles, so this legend covers
        /// the linework.
        /// </summary>
        [CommandMethod("FTFLEGEND", CommandFlags.Modal)]
        public void FtfLegend()
        {
            FtfSession.Run("FTFLEGEND", (db, tr, ed) =>
            {
                var cfg = FtfSession.Rules(db);
                var settings = FtfSession.SettingsFor(db, cfg);
                var ls = settings.LineLabels;

                var inventory = FtfLineworkService.Scan();
                if (inventory.RulesError != null)
                {
                    ed.WriteMessage("\nRules problem: {0}\n", inventory.RulesError);
                    return;
                }

                var entries = LegendBuilder.Rows(inventory.Rows);
                if (entries.Count == 0)
                {
                    ed.WriteMessage("\nFTFLEGEND: no identified line features to " +
                                    "put in a legend.\n");
                    return;
                }

                var scale = CadUtil.DrawingUnitsPerPlottedUnit(db);
                var textHeight = ls.TextHeightPlotted * scale;

                var position = Point3d.Origin;
                var havePosition = false;
                foreach (var pair in Ownership.FindOwned(db, tr,
                             s => s.Kind == FtfEntityKind.Legend))
                {
                    var title = tr.GetObject(pair.Key, OpenMode.ForRead, false, true)
                        as MText;
                    if (title != null && title.Contents == "LEGEND")
                    {
                        position = title.Location;
                        havePosition = true;
                    }
                }
                Ownership.DeleteOwnedKinds(db, tr, FtfEntityKind.Legend);

                if (!havePosition)
                {
                    var picked = ed.GetPoint(new PromptPointOptions(
                        "\nTop-left corner for the legend: "));
                    if (picked.Status != PromptStatus.OK)
                    {
                        ed.WriteMessage("\nCancelled; no legend drawn.\n");
                        return;
                    }
                    position = picked.Value;
                }

                Ownership.EnsureRegApp(db, tr);
                var textLayer = CadUtil.EnsureLayer(db, tr, ls.DefaultLabelLayer);
                var sampleLength = 0.6 * scale;
                var rowStep = textHeight * 2.2;

                using (var title = new MText())
                {
                    title.SetDatabaseDefaults(db);
                    title.Contents = "LEGEND";
                    title.TextHeight = textHeight * 1.2;
                    title.Attachment = AttachmentPoint.TopLeft;
                    title.Location = position;
                    title.LayerId = textLayer;
                    CadUtil.AddToModelSpace(db, tr, title);
                    Ownership.Stamp(title, "legend", cfg.Version,
                                    FtfEntityKind.Legend, position);
                }

                var y = position.Y - rowStep * 1.4;
                foreach (var entry in entries)
                {
                    // The sample line sits ON the feature's own layer, so it shows
                    // the true office symbology -- colour and linetype ByLayer.
                    using (var sample = new Line(
                        new Point3d(position.X, y, 0),
                        new Point3d(position.X + sampleLength, y, 0)))
                    {
                        sample.LayerId = CadUtil.EnsureLayer(db, tr, entry.Layer);
                        CadUtil.AddToModelSpace(db, tr, sample);
                        Ownership.Stamp(sample, entry.FeatureName, cfg.Version,
                                        FtfEntityKind.Legend, null);
                    }

                    using (var name = new MText())
                    {
                        name.SetDatabaseDefaults(db);
                        name.Contents = entry.FeatureName.ToUpperInvariant();
                        name.TextHeight = textHeight;
                        name.Attachment = AttachmentPoint.MiddleLeft;
                        name.Location = new Point3d(
                            position.X + sampleLength + textHeight, y, 0);
                        name.LayerId = textLayer;
                        CadUtil.AddToModelSpace(db, tr, name);
                        Ownership.Stamp(name, entry.FeatureName, cfg.Version,
                                        FtfEntityKind.Legend, null);
                    }

                    y -= rowStep;
                }

                ed.WriteMessage("\nFTFLEGEND: {0} feature(s) in the legend. Point " +
                                "symbols live in Civil 3D point styles, so the " +
                                "legend covers the linework.\n", entries.Count);
            });
        }
    }
}
