using System;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using FieldCodes.Linework;
using FieldCodes.Sheets;

using AcEntity = Autodesk.AutoCAD.DatabaseServices.Entity;

namespace FieldCodes.Cad
{
    /// <summary>
    /// Sheet production helpers, both drawing ONLY FTF-owned annotation in model
    /// space -- footprint rectangles, match lines and their labels. Layout tabs,
    /// viewports and the survey itself are never modified.
    ///
    /// FTFSHEETS reads the plot windows that already exist: every layout's
    /// viewports, projected into model space. FTFSHEETPLAN proposes windows that
    /// do not exist yet: the best-fit grid of sheets over the site at the
    /// configured paper size and scale. Both finish with match lines where
    /// adjacent windows meet. Safe to re-run; FTFCLEAN removes everything.
    ///
    /// UNTESTED against a drawing; the fitting and matching maths is unit tested.
    /// </summary>
    public sealed class SheetCommands
    {
        // ------------------------------------------------------------ FTFSHEETS

        [CommandMethod("FTFSHEETS", CommandFlags.Modal)]
        public void FtfSheets()
        {
            FtfSession.Run("FTFSHEETS", (db, tr, ed) =>
            {
                var cfg = FtfSession.Rules(db);
                var settings = FtfSession.SettingsFor(db, cfg);
                var ss = settings.Sheets;

                Ownership.EnsureRegApp(db, tr);
                RemovePrevious(db, tr, ed, "FTFSHEETS");

                var windows = new List<SheetWindow>();
                var uninitialized = new List<string>();

                var layouts = (DBDictionary)tr.GetObject(db.LayoutDictionaryId,
                                                         OpenMode.ForRead);
                foreach (DBDictionaryEntry entry in layouts)
                {
                    var layout = (Layout)tr.GetObject(entry.Value, OpenMode.ForRead);
                    if (layout.ModelType) continue;

                    var space = (BlockTableRecord)tr.GetObject(
                        layout.BlockTableRecordId, OpenMode.ForRead);

                    var found = 0;
                    foreach (ObjectId id in space)
                    {
                        if (id.IsErased) continue;
                        var viewport = tr.GetObject(id, OpenMode.ForRead, false, true)
                            as Viewport;
                        if (viewport == null || viewport.Number == 1) continue;

                        SheetWindow window;
                        if (!TryFootprint(viewport, layout.LayoutName, out window))
                        {
                            ed.WriteMessage("\n  {0}: a viewport is not a straight " +
                                            "plan view; skipped.", layout.LayoutName);
                            continue;
                        }
                        windows.Add(window);
                        found++;
                    }
                    if (found == 0) uninitialized.Add(layout.LayoutName);
                }

                if (windows.Count == 0)
                {
                    ed.WriteMessage("\nFTFSHEETS: no paper-space viewports found. " +
                                    "Open each layout tab once so its viewport " +
                                    "initialises, then run again.\n");
                    return;
                }

                DrawEverything(db, tr, ed, ss, cfg.Version, windows);

                if (uninitialized.Count > 0)
                    ed.WriteMessage("\n  No viewport found on: {0}.",
                        string.Join(", ", uninitialized.ToArray()));
            });
        }

        // --------------------------------------------------------- FTFSHEETPLAN

        /// <summary>
        /// Proposes the sheet set: lays the configured paper (17x11 at 1"=20' by
        /// default, Settings > Sheets) over everything in model space for the
        /// fewest sheets, both orientations tried, neighbours overlapping so the
        /// match lines have a seam. Draws only the proposal -- no layouts or
        /// viewports are created.
        /// </summary>
        [CommandMethod("FTFSHEETPLAN", CommandFlags.Modal)]
        public void FtfSheetPlan()
        {
            FtfSession.Run("FTFSHEETPLAN", (db, tr, ed) =>
            {
                var cfg = FtfSession.Rules(db);
                var settings = FtfSession.SettingsFor(db, cfg);
                var ss = settings.Sheets;

                Ownership.EnsureRegApp(db, tr);
                RemovePrevious(db, tr, ed, "FTFSHEETPLAN");

                double minX, minY, maxX, maxY;
                if (!SurveyExtent(db, tr, out minX, out minY, out maxX, out maxY))
                {
                    ed.WriteMessage("\nFTFSHEETPLAN: nothing in model space to lay " +
                                    "sheets over.\n");
                    return;
                }

                // Printable window in model units: paper minus margins, times the
                // plot scale, times the drawing's units per foot.
                var unitsPerFoot = settings.General.UnitsPerFoot;
                var printableLong =
                    (Math.Max(ss.SheetWidthIn, ss.SheetHeightIn) - 2 * ss.MarginIn) *
                    ss.PlotScaleFeetPerInch * unitsPerFoot;
                var printableShort =
                    (Math.Min(ss.SheetWidthIn, ss.SheetHeightIn) - 2 * ss.MarginIn) *
                    ss.PlotScaleFeetPerInch * unitsPerFoot;

                var plan = SheetPlanner.Plan(minX, minY, maxX, maxY,
                    printableLong, printableShort, ss.OverlapPercent / 100.0);

                ed.WriteMessage(
                    "\nFTFSHEETPLAN: {0} x {1}\" at 1\"={2:0.#}', {3:0.#}\" margins, " +
                    "{4:0.#}% overlap -> {5} sheet(s), {6} ({7} row(s) x {8} column(s)).",
                    ss.SheetWidthIn, ss.SheetHeightIn, ss.PlotScaleFeetPerInch,
                    ss.MarginIn, ss.OverlapPercent,
                    plan.Windows.Count,
                    plan.Landscape ? "landscape" : "portrait",
                    plan.Rows, plan.Columns);

                DrawEverything(db, tr, ed, ss, cfg.Version, plan.Windows);
            });
        }

        // ------------------------------------------------------------ FTFKEYMAP

        /// <summary>
        /// Puts the little index diagram on every sheet: all the sheet windows at
        /// true relative positions, this sheet's cell drawn heavy, each cell
        /// numbered. Drawn in each layout's paper space, tucked into the lower
        /// right of the sheet's viewport. Re-running replaces each layout's key
        /// map; nothing else on the layout is touched.
        /// </summary>
        [CommandMethod("FTFKEYMAP", CommandFlags.Modal)]
        public void FtfKeymap()
        {
            FtfSession.Run("FTFKEYMAP", (db, tr, ed) =>
            {
                var cfg = FtfSession.Rules(db);
                var settings = FtfSession.SettingsFor(db, cfg);
                var ss = settings.Sheets;

                var windows = DrawnWindows(db, tr);
                if (windows.Count == 0)
                {
                    ed.WriteMessage("\nFTFKEYMAP: no sheet windows in the drawing. " +
                                    "Run FTFSHEETPLAN or FTFSHEETS first.\n");
                    return;
                }

                Ownership.EnsureRegApp(db, tr);
                var layerId = CadUtil.EnsureLayer(db, tr, ss.WindowLayer);

                var placed = 0;
                var layouts = (DBDictionary)tr.GetObject(db.LayoutDictionaryId,
                                                         OpenMode.ForRead);
                foreach (DBDictionaryEntry entry in layouts)
                {
                    var layout = (Layout)tr.GetObject(entry.Value, OpenMode.ForRead);
                    if (layout.ModelType) continue;

                    var space = (BlockTableRecord)tr.GetObject(
                        layout.BlockTableRecordId, OpenMode.ForRead);

                    RemoveKeymap(tr, space);

                    // Anchored inside the sheet's viewport, lower right -- a real
                    // reference point regardless of where the paper origin sits.
                    var frame = MainViewport(tr, space);
                    if (frame == null) continue;

                    var map = KeymapBuilder.Build(windows, layout.LayoutName,
                                                  ss.KeymapWidthIn);
                    if (map.Cells.Count == 0) continue;

                    var inset = 0.25;
                    var origin = new Point3d(
                        frame.CenterPoint.X + frame.Width / 2.0 - inset - map.Width,
                        frame.CenterPoint.Y - frame.Height / 2.0 + inset,
                        0.0);

                    DrawKeymap(db, tr, space, map, origin, layerId, cfg.Version,
                               layout.LayoutName);
                    placed++;
                }

                ed.WriteMessage("\nFTFKEYMAP: key map placed on {0} sheet(s).\n",
                                placed);
            });
        }

        /// <summary>Erases this layout's previous key map. Paper-space entities
        /// never show up in the model-space cleanup, so each layout squares its
        /// own away here.</summary>
        private static void RemoveKeymap(Transaction tr, BlockTableRecord space)
        {
            var doomed = new List<ObjectId>();
            foreach (ObjectId id in space)
            {
                if (id.IsErased) continue;
                var entity = tr.GetObject(id, OpenMode.ForRead, false, true) as AcEntity;
                if (entity == null) continue;
                var stamp = Ownership.Read(entity);
                if (stamp != null && stamp.Kind == FtfEntityKind.KeyMap)
                    doomed.Add(id);
            }
            foreach (var id in doomed)
            {
                var entity = (AcEntity)tr.GetObject(id, OpenMode.ForWrite);
                entity.Erase();
            }
        }

        /// <summary>The layout's sheet viewport -- the biggest one that is not the
        /// paper view.</summary>
        private static Viewport MainViewport(Transaction tr, BlockTableRecord space)
        {
            Viewport best = null;
            foreach (ObjectId id in space)
            {
                if (id.IsErased) continue;
                var viewport = tr.GetObject(id, OpenMode.ForRead, false, true) as Viewport;
                if (viewport == null || viewport.Number == 1) continue;
                if (best == null || viewport.Width * viewport.Height >
                                    best.Width * best.Height)
                    best = viewport;
            }
            return best;
        }

        private static void DrawKeymap(Database db, Transaction tr,
                                       BlockTableRecord space, Keymap map,
                                       Point3d origin, ObjectId layerId,
                                       string rulesVersion, string layoutName)
        {
            foreach (var cell in map.Cells)
            {
                var x = origin.X + cell.X;
                var y = origin.Y + cell.Y;

                using (var rectangle = new Polyline(4))
                {
                    rectangle.AddVertexAt(0, new Point2d(x, y), 0, 0, 0);
                    rectangle.AddVertexAt(1, new Point2d(x + cell.W, y), 0, 0, 0);
                    rectangle.AddVertexAt(2, new Point2d(x + cell.W, y + cell.H), 0, 0, 0);
                    rectangle.AddVertexAt(3, new Point2d(x, y + cell.H), 0, 0, 0);
                    rectangle.Closed = true;
                    rectangle.LayerId = layerId;

                    // The current sheet reads heavy; the neighbours stay hairline.
                    if (cell.Current) rectangle.ConstantWidth = 0.03;

                    space.UpgradeOpen();
                    space.AppendEntity(rectangle);
                    tr.AddNewlyCreatedDBObject(rectangle, true);
                    Ownership.Stamp(rectangle, layoutName, rulesVersion,
                                    FtfEntityKind.KeyMap, null);
                }

                var textHeight = Math.Max(0.06, Math.Min(0.15,
                    Math.Min(cell.W, cell.H) * 0.35));
                using (var number = new MText())
                {
                    number.SetDatabaseDefaults(db);
                    number.Contents = cell.ShortName;
                    number.TextHeight = textHeight;
                    number.Attachment = AttachmentPoint.MiddleCenter;
                    number.Location = new Point3d(x + cell.W / 2.0,
                                                  y + cell.H / 2.0, 0.0);
                    number.LayerId = layerId;

                    space.AppendEntity(number);
                    tr.AddNewlyCreatedDBObject(number, true);
                    Ownership.Stamp(number, layoutName, rulesVersion,
                                    FtfEntityKind.KeyMap, null);
                }
            }
        }

        // --------------------------------------------------------- FTFSHEETMAKE

        /// <summary>
        /// Materialises the drawn sheet plan: one layout per sheet window found in
        /// model space, each with a locked viewport showing exactly that window at
        /// the planned scale. It reads the RECTANGLES, not the original plan -- so
        /// a window the surveyor nudged or stretched before running this is
        /// honoured exactly. Existing layouts with the same name are skipped,
        /// never overwritten, and FTFCLEAN never deletes a layout: creating sheets
        /// is one-way by design.
        /// </summary>
        [CommandMethod("FTFSHEETMAKE", CommandFlags.Modal)]
        public void FtfSheetMake()
        {
            FtfSession.Run("FTFSHEETMAKE", (db, tr, ed) =>
            {
                var cfg = FtfSession.Rules(db);
                var settings = FtfSession.SettingsFor(db, cfg);
                var ss = settings.Sheets;

                var windows = DrawnWindows(db, tr);
                if (windows.Count == 0)
                {
                    ed.WriteMessage("\nFTFSHEETMAKE: no sheet windows in the drawing. " +
                                    "Run FTFSHEETPLAN (or FTFSHEETS) first, adjust " +
                                    "the rectangles if needed, then run this.\n");
                    return;
                }

                // Plan-set order: by the trailing number in the name where there is
                // one, alphabetically otherwise.
                windows.Sort((a, b) =>
                {
                    int na, nb;
                    var hasA = TryTrailingNumber(a.Name, out na);
                    var hasB = TryTrailingNumber(b.Name, out nb);
                    if (hasA && hasB) return na.CompareTo(nb);
                    return string.CompareOrdinal(a.Name, b.Name);
                });

                var printableW = ss.SheetWidthIn - 2 * ss.MarginIn;
                var printableH = ss.SheetHeightIn - 2 * ss.MarginIn;

                var layouts = (DBDictionary)tr.GetObject(db.LayoutDictionaryId,
                                                         OpenMode.ForRead);
                var manager = LayoutManager.Current;
                var made = 0;
                var skipped = new List<string>();

                foreach (var window in windows)
                {
                    var name = string.IsNullOrWhiteSpace(window.Name)
                        ? "SHEET" : window.Name.Trim();

                    if (layouts.Contains(name)) { skipped.Add(name); continue; }

                    var layoutId = manager.CreateLayout(name);
                    var layout = (Layout)tr.GetObject(layoutId, OpenMode.ForWrite);
                    TrySetPaper(layout, ss);

                    var space = (BlockTableRecord)tr.GetObject(
                        layout.BlockTableRecordId, OpenMode.ForWrite);

                    // Landscape window on portrait-planned paper (or the reverse):
                    // the viewport takes the orientation the window actually has.
                    var windowLandscape = (window.MaxX - window.MinX) >=
                                          (window.MaxY - window.MinY);
                    var paperW = windowLandscape
                        ? Math.Max(printableW, printableH)
                        : Math.Min(printableW, printableH);
                    var paperH = windowLandscape
                        ? Math.Min(printableW, printableH)
                        : Math.Max(printableW, printableH);

                    using (var viewport = new Viewport())
                    {
                        viewport.SetDatabaseDefaults(db);
                        viewport.CenterPoint = new Point3d(ss.MarginIn + paperW / 2.0,
                                                           ss.MarginIn + paperH / 2.0,
                                                           0.0);
                        viewport.Width = paperW;
                        viewport.Height = paperH;

                        space.AppendEntity(viewport);
                        tr.AddNewlyCreatedDBObject(viewport, true);

                        viewport.On = true;
                        viewport.ViewCenter = new Point2d(
                            (window.MinX + window.MaxX) / 2.0,
                            (window.MinY + window.MaxY) / 2.0);
                        viewport.ViewHeight = window.MaxY - window.MinY;

                        // Locked, so panning around a sheet cannot silently change
                        // its scale or drift it off its match lines.
                        viewport.Locked = true;

                        Ownership.Stamp(viewport, name, cfg.Version,
                                        FtfEntityKind.SheetArea, null);
                    }

                    made++;
                }

                ed.WriteMessage("\nFTFSHEETMAKE: {0} layout(s) created with locked " +
                                "viewports at the planned scale.", made);
                if (skipped.Count > 0)
                    ed.WriteMessage("\n  Already existed, left untouched: {0}.",
                                    string.Join(", ", skipped.ToArray()));
                ed.WriteMessage("\n  Assign your office page setup to the new " +
                                "layouts before plotting; FTF does not change " +
                                "plotter configuration.\n");
            });
        }

        /// <summary>The sheet-window rectangles FTF drew (and the surveyor may have
        /// adjusted), read back with their names.</summary>
        private static List<SheetWindow> DrawnWindows(Database db, Transaction tr)
        {
            var windows = new List<SheetWindow>();

            var ms = CadUtil.ModelSpace(db, tr, OpenMode.ForRead);
            foreach (ObjectId id in ms)
            {
                if (id.IsErased) continue;
                var polyline = tr.GetObject(id, OpenMode.ForRead, false, true) as Polyline;
                if (polyline == null) continue;

                var stamp = Ownership.Read(polyline);
                if (stamp == null || stamp.Kind != FtfEntityKind.SheetArea) continue;

                Extents3d extents;
                try { extents = polyline.GeometricExtents; }
                catch (Autodesk.AutoCAD.Runtime.Exception) { continue; }

                windows.Add(new SheetWindow
                {
                    Name = stamp.PointNumber,
                    MinX = extents.MinPoint.X,
                    MinY = extents.MinPoint.Y,
                    MaxX = extents.MaxPoint.X,
                    MaxY = extents.MaxPoint.Y
                });
            }

            return windows;
        }

        /// <summary>Best effort at the configured paper on a device-free setup.
        /// Page setups are plotter-specific office territory, so failure here is
        /// silent and the layout keeps the drawing's default.</summary>
        private static void TrySetPaper(Layout layout,
                                        FieldCodes.Settings.SheetSettings ss)
        {
            try
            {
                var validator = PlotSettingsValidator.Current;
                using (var plot = new PlotSettings(layout.ModelType))
                {
                    plot.CopyFrom(layout);
                    validator.SetPlotConfigurationName(plot, "None",
                        string.Format(CultureInfo.InvariantCulture,
                            "ANSI_B_({0:0.00}_x_{1:0.00}_Inches)",
                            Math.Max(ss.SheetWidthIn, ss.SheetHeightIn),
                            Math.Min(ss.SheetWidthIn, ss.SheetHeightIn)));
                    layout.CopyFrom(plot);
                }
            }
            catch (System.Exception)
            {
                // The office page setup gets assigned by a person.
            }
        }

        private static bool TryTrailingNumber(string name, out int number)
        {
            number = 0;
            if (string.IsNullOrWhiteSpace(name)) return false;

            var trimmed = name.Trim();
            var start = trimmed.Length;
            while (start > 0 && char.IsDigit(trimmed[start - 1])) start--;
            if (start == trimmed.Length) return false;

            return int.TryParse(trimmed.Substring(start), NumberStyles.Integer,
                                CultureInfo.InvariantCulture, out number);
        }

        // ------------------------------------------------------------- plumbing

        private static void RemovePrevious(Database db, Transaction tr, Editor ed,
                                           string command)
        {
            var removed = Ownership.DeleteOwnedKinds(db, tr,
                FtfEntityKind.SheetArea, FtfEntityKind.MatchLine);
            if (removed > 0)
                ed.WriteMessage("\n{0}: removed {1} previous sheet entities.",
                                command, removed);
        }

        /// <summary>A viewport's model-space footprint. Plan views only; twisted
        /// or 3D views are refused rather than approximated.</summary>
        private static bool TryFootprint(Viewport viewport, string layoutName,
                                         out SheetWindow window)
        {
            window = default(SheetWindow);

            var direction = viewport.ViewDirection;
            if (direction.GetNormal().DotProduct(Vector3d.ZAxis) < 0.9999) return false;
            if (Math.Abs(viewport.TwistAngle) > 1e-6) return false;
            if (viewport.Height <= 0 || viewport.ViewHeight <= 0) return false;

            var modelPerPaper = viewport.ViewHeight / viewport.Height;
            var halfW = viewport.Width * modelPerPaper / 2.0;
            var halfH = viewport.ViewHeight / 2.0;
            var centre = viewport.ViewCenter;

            window = new SheetWindow
            {
                Name = layoutName,
                MinX = centre.X - halfW,
                MinY = centre.Y - halfH,
                MaxX = centre.X + halfW,
                MaxY = centre.Y + halfH
            };
            return true;
        }

        /// <summary>The extent of everything in model space that is not FTF's own
        /// output -- the site the sheets must cover.</summary>
        private static bool SurveyExtent(Database db, Transaction tr,
                                         out double minX, out double minY,
                                         out double maxX, out double maxY)
        {
            minX = minY = double.MaxValue;
            maxX = maxY = double.MinValue;
            var any = false;

            var ms = CadUtil.ModelSpace(db, tr, OpenMode.ForRead);
            foreach (ObjectId id in ms)
            {
                if (id.IsErased) continue;
                var entity = tr.GetObject(id, OpenMode.ForRead, false, true) as AcEntity;
                if (entity == null || Ownership.Read(entity) != null) continue;

                Extents3d extents;
                try { extents = entity.GeometricExtents; }
                catch (Autodesk.AutoCAD.Runtime.Exception) { continue; }

                minX = Math.Min(minX, extents.MinPoint.X);
                minY = Math.Min(minY, extents.MinPoint.Y);
                maxX = Math.Max(maxX, extents.MaxPoint.X);
                maxY = Math.Max(maxY, extents.MaxPoint.Y);
                any = true;
            }

            return any && maxX > minX && maxY > minY;
        }

        private static void DrawEverything(Database db, Transaction tr, Editor ed,
                                           FieldCodes.Settings.SheetSettings ss,
                                           string rulesVersion,
                                           IList<SheetWindow> windows)
        {
            var scale = CadUtil.DrawingUnitsPerPlottedUnit(db);
            var textHeight = ss.MatchlineTextPlotted * scale;

            var windowLayer = CadUtil.EnsureLayer(db, tr, ss.WindowLayer);
            foreach (var window in windows)
                DrawWindow(db, tr, window, windowLayer, textHeight, ss.DrawWindowLabels,
                           rulesVersion);

            var matchLayer = CadUtil.EnsureLayer(db, tr, ss.MatchlineLayer);
            var matchLines = SheetMatcher.FindMatchLines(windows);
            foreach (var line in matchLines)
                DrawMatchLine(db, tr, line, matchLayer, textHeight,
                              ss.MatchlineLabelFormat, rulesVersion);

            ed.WriteMessage("\n{0} sheet window(s) drawn, {1} match line(s).\n",
                            windows.Count, matchLines.Count);
        }

        private static void DrawWindow(Database db, Transaction tr, SheetWindow window,
                                       ObjectId layerId, double textHeight,
                                       bool label, string rulesVersion)
        {
            using (var rectangle = new Polyline(4))
            {
                rectangle.AddVertexAt(0, new Point2d(window.MinX, window.MinY), 0, 0, 0);
                rectangle.AddVertexAt(1, new Point2d(window.MaxX, window.MinY), 0, 0, 0);
                rectangle.AddVertexAt(2, new Point2d(window.MaxX, window.MaxY), 0, 0, 0);
                rectangle.AddVertexAt(3, new Point2d(window.MinX, window.MaxY), 0, 0, 0);
                rectangle.Closed = true;
                rectangle.LayerId = layerId;

                CadUtil.AddToModelSpace(db, tr, rectangle);
                Ownership.Stamp(rectangle, window.Name, rulesVersion,
                                FtfEntityKind.SheetArea, null);
            }

            if (!label) return;

            using (var text = new MText())
            {
                text.SetDatabaseDefaults(db);
                text.Contents = window.Name;
                text.TextHeight = textHeight;
                text.Attachment = AttachmentPoint.TopLeft;
                text.Location = new Point3d(window.MinX + textHeight,
                                            window.MaxY - textHeight, 0.0);
                text.LayerId = layerId;

                CadUtil.AddToModelSpace(db, tr, text);
                Ownership.Stamp(text, window.Name, rulesVersion,
                                FtfEntityKind.SheetArea, null);
            }
        }

        private static void DrawMatchLine(Database db, Transaction tr, MatchLine line,
                                          ObjectId layerId, double textHeight,
                                          string format, string rulesVersion)
        {
            using (var seam = new Line(new Point3d(line.X1, line.Y1, 0),
                                       new Point3d(line.X2, line.Y2, 0)))
            {
                seam.LayerId = layerId;
                CadUtil.AddToModelSpace(db, tr, seam);
                Ownership.Stamp(seam, line.SideAName + "/" + line.SideBName,
                                rulesVersion, FtfEntityKind.MatchLine, null);
            }

            // One label each side of the seam, each pointing at the OTHER sheet --
            // read from sheet A, the label names sheet B. Same placement maths as
            // every other FTF label, so left and right cannot flip.
            var midX = (line.X1 + line.X2) / 2.0;
            var midY = (line.Y1 + line.Y2) / 2.0;
            var direction = line.Vertical ? Math.PI / 2.0 : 0.0;
            var offset = textHeight * 1.2;

            PlaceMatchText(db, tr, format, line.SideBName,
                LineLabelPlanner.PlaceAt(midX, midY, direction, LineLabelSide.Left,
                                         offset, true),
                layerId, textHeight, rulesVersion, line);
            PlaceMatchText(db, tr, format, line.SideAName,
                LineLabelPlanner.PlaceAt(midX, midY, direction, LineLabelSide.Right,
                                         offset, true),
                layerId, textHeight, rulesVersion, line);
        }

        private static void PlaceMatchText(Database db, Transaction tr, string format,
                                           string otherSheet, PlannedLineLabel plan,
                                           ObjectId layerId, double textHeight,
                                           string rulesVersion, MatchLine line)
        {
            var wording = (format ?? "MATCHLINE - SEE SHEET {sheet}")
                .Replace("{sheet}", otherSheet.ToUpperInvariant());

            using (var text = new MText())
            {
                text.SetDatabaseDefaults(db);
                text.Contents = wording;
                text.TextHeight = textHeight;
                text.Attachment = AttachmentPoint.MiddleCenter;
                text.Location = new Point3d(plan.X, plan.Y, 0.0);
                text.Rotation = plan.RotationRadians;
                text.LayerId = layerId;

                CadUtil.AddToModelSpace(db, tr, text);
                Ownership.Stamp(text, line.SideAName + "/" + line.SideBName,
                                rulesVersion, FtfEntityKind.MatchLine, null);
            }
        }
    }
}
