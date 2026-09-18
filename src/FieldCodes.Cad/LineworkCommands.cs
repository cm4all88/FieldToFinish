using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using FieldCodes.Linework;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcEntity = Autodesk.AutoCAD.DatabaseServices.Entity;

namespace FieldCodes.Cad
{
    /// <summary>
    /// Linework commands: the read-only inventory (FTFLINES) and the line labelling
    /// stage (FTFLINELABELS).
    ///
    /// The product boundary, enforced structurally: source polylines are opened FOR
    /// READ ONLY and are never modified, moved or recreated. FTF creates nothing but
    /// the annotation -- MTEXT labels and their masks -- stamped with its own ownership kinds
    /// (LineLabel, LineMask) so FTFCLEAN and re-runs manage them without touching
    /// point labels or the survey linework.
    ///
    /// UNTESTED against a drawing; planning, identification and label resolution are
    /// unit tested.
    /// </summary>
    public sealed class LineworkCommands
    {
        /// <summary>
        /// Stamped into the tag slot of labels placed interactively by FTFLABELLINE.
        /// The bulk FTFLINELABELS pass never deletes a label carrying this mark: the
        /// surveyor put it exactly where it is. FTFCLEAN still removes everything.
        /// </summary>
        internal const string ManualLineLabelMark = "manual";

        [CommandMethod("FTFLINES", CommandFlags.Modal)]
        public void FtfLines()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            var ed = doc.Editor;

            ed.WriteMessage("\nFTFLINES: read-only linework inventory. Nothing in the " +
                            "drawing is created or modified.\n");

            var result = FtfLineworkService.Scan();

            if (result.RulesError != null)
            {
                ed.WriteMessage("\nRules problem: {0}\n", result.RulesError);
                return;
            }

            ed.WriteMessage("\n{0}\n",
                LineworkInventoryCsv.Summary(result.Rows, result.FtfOwnedByKind));

            var path = LineworkInventoryCsv.PathFor(doc.Database.Filename);
            if (path == null)
            {
                ed.WriteMessage("\nDrawing has never been saved, so no CSV was written. " +
                                "Save it and run FTFLINES again for the full listing.\n");
                return;
            }

            try
            {
                System.IO.File.WriteAllText(path,
                    LineworkInventoryCsv.Render(result.Rows),
                    new System.Text.UTF8Encoding(true));
                ed.WriteMessage("\nFull inventory ({0} row(s)): {1}\n",
                                result.Rows.Count, path);
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

        /// <summary>
        /// Labels existing Civil 3D linework per the configured standards: one label
        /// at the midpoint of short features, repeated labels on long ones, text
        /// following the line but never upside down. Safe to run twice -- it deletes
        /// only its own previous output first.
        /// </summary>
        [CommandMethod("FTFLINELABELS", CommandFlags.Modal)]
        public void FtfLineLabels()
        {
            FtfSession.Run("FTFLINELABELS", (db, tr, ed) =>
            {
                var cfg = FtfSession.Rules(db);
                var settings = FtfSession.SettingsFor(db, cfg);
                var ls = settings.LineLabels;

                if (!ls.Enabled)
                {
                    ed.WriteMessage("\nFTFLINELABELS: line labelling is turned off in " +
                                    "settings.\n");
                    return;
                }

                var catalog = new LineworkCatalog(cfg.LineFeatures, cfg.IgnoreLineNames);
                Ownership.EnsureRegApp(db, tr);

                // The bulk pass regenerates only its OWN previous output. Labels the
                // surveyor placed with FTFLABELLINE carry the manual mark and are
                // never touched here -- manual drafting wins.
                var kept = 0;
                var removed = Ownership.DeleteOwned(db, tr, s =>
                {
                    if (s.Kind != FtfEntityKind.LineLabel &&
                        s.Kind != FtfEntityKind.LineMask) return false;
                    if (string.Equals(s.TagText, ManualLineLabelMark, StringComparison.Ordinal))
                    {
                        kept++;
                        return false;
                    }
                    return true;
                });
                if (removed > 0)
                    ed.WriteMessage("\nFTFLINELABELS: removed {0} previous line label " +
                                    "entities.", removed);
                if (kept > 0)
                    ed.WriteMessage("\nFTFLINELABELS: kept {0} manually placed " +
                                    "entities (FTFLABELLINE).", kept);

                var unitsPerFoot = settings.General.UnitsPerFoot;
                var scale = CadUtil.DrawingUnitsPerPlottedUnit(db);
                var textHeight = ls.TextHeightPlotted * scale;
                var styleId = Setup.DrawingResources.FindTextStyle(db, tr, ls.TextStyle);

                if (!string.IsNullOrWhiteSpace(ls.TextStyle) && styleId.IsNull)
                    ed.WriteMessage("\nFTFLINELABELS: text style '{0}' is not in this " +
                                    "drawing; using the current style.", ls.TextStyle);

                var defaultSide = LineSideParser.ParsePlacement(ls.DefaultPlacement);
                var sideOffset = ls.SideOffsetFeet * unitsPerFoot;
                var layerCatalog = FtfLineworkService.LayerNames(db, tr);

                var featuresLabelled = 0;
                var labelsPlaced = 0;
                var skippedShort = 0;
                var notConfigured = 0;

                var ms = CadUtil.ModelSpace(db, tr, OpenMode.ForRead);

                foreach (ObjectId id in ms)
                {
                    if (id.IsErased) continue;

                    // FOR READ, and never upgraded: the source polyline cannot be
                    // modified through this code path.
                    var entity = tr.GetObject(id, OpenMode.ForRead, false, true) as AcEntity;
                    if (entity == null) continue;
                    if (Ownership.Read(entity) != null) continue;

                    var curve = entity as Curve;
                    if (curve == null) continue;

                    string figureName = null;
                    var figure = entity as Autodesk.Civil.DatabaseServices.SurveyFigure;
                    if (figure != null) figureName = figure.Name;
                    else
                    {
                        var featureLine = entity as Autodesk.Civil.DatabaseServices.FeatureLine;
                        if (featureLine != null) figureName = featureLine.Name;
                    }

                    var trimbleName = FtfLineworkService.TrimbleNameOf(entity);
                    var candidates = catalog.Candidates(figureName, trimbleName,
                                                        entity.Layer);
                    if (candidates.Count == 0) continue;

                    var labelText = LineworkCatalog.UnanimousLabel(candidates);
                    if (labelText == null) { notConfigured++; continue; }

                    // The side comes from the source coding's own modifier ("ASPH
                    // LEFT") -- read from the figure name, or from TrimbleName XData
                    // when that is all the export preserved -- then the feature's
                    // configured placement, then the default. Never guessed. Left and
                    // right are relative to the line's own direction, and the offset
                    // never touches the line itself.
                    string ignoredCode;
                    LineLabelSide? sourceSide;
                    LineSideParser.Split(figureName, out ignoredCode, out sourceSide);
                    if (sourceSide == null)
                        LineSideParser.Split(trimbleName, out ignoredCode, out sourceSide);

                    var options = new LineLabelOptions
                    {
                        MinLength = ls.MinLengthFeet * unitsPerFoot,
                        RepeatInterval = ls.RepeatIntervalFeet * unitsPerFoot,
                        EndClearance = ls.EndClearanceFeet * unitsPerFoot,
                        AlignToLine = ls.AlignToLine,
                        Side = LineSideParser.Resolve(sourceSide, candidates, defaultSide),
                        SideOffset = sideOffset
                    };

                    var path = new CurvePath(curve);
                    if (path.Length < options.MinLength) { skippedShort++; continue; }

                    // One resolver for the bulk pass, the interactive command and the
                    // review preview: rule's explicit layer, else the office-standard
                    // text layer that actually exists, else the configured default.
                    var layer = LabelLayerResolver.Resolve(entity.Layer, candidates,
                        layerCatalog, ls.DefaultLabelLayer).Layer;
                    var layerId = CadUtil.EnsureLayer(db, tr, layer);

                    var planned = LineLabelPlanner.Plan(path, options);
                    foreach (var plan in planned)
                    {
                        PlaceLineLabel(db, tr, plan, labelText, textHeight, styleId,
                                       layerId, layer, ls.DrawMask, candidates[0].Code,
                                       cfg.Version);
                        labelsPlaced++;
                    }

                    if (planned.Count > 0) featuresLabelled++;
                }

                ed.WriteMessage(
                    "\nFTFLINELABELS: {0} label(s) on {1} feature(s); {2} skipped as " +
                    "shorter than {3:0.#} ft; {4} identified feature(s) have no " +
                    "labelling standard configured.\n",
                    labelsPlaced, featuresLabelled, skippedShort, ls.MinLengthFeet,
                    notConfigured);

                // Rebuild every mask around its label's actual position -- a moved
                // manual label's mask follows it.
                MaskSync.Rebuild(db, tr, ed, settings, cfg.Version);
            });
        }

        /// <summary>
        /// The primary line-labelling workflow: click a line, slide the live label
        /// preview along it, click to place. The cursor says WHERE along the feature
        /// and WHICH side; everything else -- text, layer, style, height, offset
        /// distance, mask -- comes from the configured standard. Line annotation is
        /// context-dependent drafting, so the surveyor decides which lines get
        /// labels; the bulk FTFLINELABELS pass remains available as a diagnostic.
        ///
        /// The source line is opened FOR READ and never modified. FTF creates only
        /// the annotation and its optional mask, stamped with the manual mark so the
        /// bulk pass never regenerates over them (FTFCLEAN still removes everything).
        /// </summary>
        [CommandMethod("FTFLABELLINE", CommandFlags.Modal)]
        [CommandMethod("FTFL", CommandFlags.Modal)]
        public void FtfLabelLine()
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

            var ls = settings.LineLabels;
            var catalog = new LineworkCatalog(cfg.LineFeatures, cfg.IgnoreLineNames);
            var unitsPerFoot = settings.General.UnitsPerFoot;
            var placedCount = 0;

            while (true)
            {
                var options = new PromptEntityOptions(
                    "\nSelect the line to label (Enter/Esc to finish): ");
                options.AllowNone = true;
                var picked = ed.GetEntity(options);
                if (picked.Status != PromptStatus.OK) break;

                using (doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    if (PlaceOneInteractiveLabel(doc, tr, picked.ObjectId, catalog, ls,
                                                 unitsPerFoot, cfg.Version,
                                                 cfg.SpanMarkers))
                        placedCount++;
                    tr.Commit();
                }
            }

            ed.WriteMessage("\nFTFLABELLINE: {0} label(s) placed.\n", placedCount);
        }

        /// <summary>One pick-identify-jig-place cycle. Returns true if a label was
        /// placed. Every refusal explains itself; nothing is ever guessed.</summary>
        private static bool PlaceOneInteractiveLabel(
            Autodesk.AutoCAD.ApplicationServices.Document doc, Transaction tr,
            ObjectId pickedId, LineworkCatalog catalog,
            FieldCodes.Settings.LineLabelSettings ls, double unitsPerFoot,
            string rulesVersion, IList<SpanMarkerRule> spanMarkers)
        {
            var ed = doc.Editor;
            var db = doc.Database;

            var entity = tr.GetObject(pickedId, OpenMode.ForRead, false, true) as AcEntity;
            var curve = entity as Curve;
            if (curve == null)
            {
                ed.WriteMessage("\nThat object is not a line, polyline, arc or figure.");
                return false;
            }
            if (Ownership.Read(entity) != null)
            {
                ed.WriteMessage("\nThat is FTF's own finishing output, not survey " +
                                "linework. Pick the source line itself.");
                return false;
            }

            // Identify: figure name, then TrimbleName XData, then layer -- the same
            // precedence as everywhere else in FTF.
            string figureName = null;
            var figure = entity as Autodesk.Civil.DatabaseServices.SurveyFigure;
            if (figure != null) figureName = figure.Name;
            else
            {
                var featureLine = entity as Autodesk.Civil.DatabaseServices.FeatureLine;
                if (featureLine != null) figureName = featureLine.Name;
            }
            var trimbleName = FtfLineworkService.TrimbleNameOf(entity);

            // Staged identification: figure name, then TrimbleName XData, then the
            // raw notes of the survey points this line was drawn through, then the
            // layer. Point notes outrank the layer because they carry what the
            // flattened layer erased -- which stripe, which wall subtype.
            IList<LineFeatureRule> candidates = null;
            string how = null;
            PointNoteConsensus notes = null;

            var byFigure = catalog.FindByFigureName(figureName);
            if (byFigure != null)
            {
                candidates = new List<LineFeatureRule> { byFigure };
                how = "figure name";
            }

            if (candidates == null)
            {
                var byTrimble = catalog.FindByTrimbleName(trimbleName);
                if (byTrimble.Count > 0) { candidates = byTrimble; how = "Trimble name"; }
            }

            if (candidates == null)
            {
                notes = PointNotes.Consensus(NotesAtVertices(db, tr, curve), catalog);
                if (notes.Features.Count > 0)
                {
                    candidates = notes.Features;
                    how = "point notes";
                }
                else if (notes.DisagreeingCodes.Count > 0)
                {
                    ed.WriteMessage(
                        "\nThe survey points along this line disagree ({0}) -- " +
                        "falling back to the layer.",
                        string.Join(" vs ", notes.DisagreeingCodes.ToArray()));
                }
            }

            if (candidates == null)
            {
                var byLayer = catalog.FindByLayer(entity.Layer);
                if (byLayer.Count > 0) { candidates = byLayer; how = "layer"; }
            }

            if (candidates == null)
            {
                ed.WriteMessage(
                    "\nNot a configured line feature. Found: layer \"{0}\"{1}{2}{3}. " +
                    "Nothing was placed.",
                    entity.Layer,
                    string.IsNullOrEmpty(figureName) ? "" : ", figure \"" + figureName + "\"",
                    string.IsNullOrEmpty(trimbleName) ? "" : ", Trimble name \"" + trimbleName + "\"",
                    notes != null && notes.SampleNotes.Count > 0
                        ? ", point notes " + string.Join(" / ", notes.SampleNotes.ToArray())
                        : "");
                return false;
            }

            var labelText = LineworkCatalog.UnanimousLabel(candidates);
            if (labelText == null)
            {
                var descriptions = string.Join(", ", candidates
                    .Select(c => c.Code + (string.IsNullOrEmpty(c.Label)
                        ? " (no label configured)" : " -> \"" + c.Label + "\""))
                    .ToArray());
                ed.WriteMessage(
                    "\nIdentified, but no single labelling standard applies: {0}. " +
                    "FTF will not choose silently -- nothing was placed.", descriptions);
                return false;
            }

            var featureName = string.Join(" / ", candidates
                .Select(c => string.IsNullOrEmpty(c.Name) ? c.Code : c.Name)
                .Distinct().ToArray());

            // The label layer: the rule's explicit choice, else the office-standard
            // text layer that actually exists in this drawing, else the default --
            // resolved by the same service the bulk pass and the review use.
            var layerCatalog = FtfLineworkService.LayerNames(db, tr);
            var resolution = LabelLayerResolver.Resolve(entity.Layer, candidates,
                                                        layerCatalog, ls.DefaultLabelLayer);

            var wordings = LineworkCatalog.LabelChoices(candidates);

            ed.WriteMessage("\nFeature: {0} ({1})", featureName, how);
            ed.WriteMessage("\nSource layer: {0}", entity.Layer);
            if (notes != null && notes.NoteCount > 0)
                ed.WriteMessage("\nPoint notes: {0} ({1} vertex point(s))",
                                string.Join(", ", notes.SampleNotes.ToArray()),
                                notes.NoteCount);
            ed.WriteMessage("\nLabel layer: {0}", resolution.Describe());
            ed.WriteMessage("\nLabel: {0}{1}", labelText,
                wordings.Count > 1
                    ? "  (type W for: " + string.Join(" / ",
                          wordings.Skip(1).ToArray()) + ")"
                    : "");

            // Presentation standard -- identical inputs to the bulk engine.
            var scale = CadUtil.DrawingUnitsPerPlottedUnit(db);
            var textHeight = ls.TextHeightPlotted * scale;
            var styleId = Setup.DrawingResources.FindTextStyle(db, tr, ls.TextStyle);
            var sideOffset = ls.SideOffsetFeet * unitsPerFoot;

            // Within half the configured offset of the line, the label snaps ON the
            // line; beyond it, it offsets to the cursor's side at the full standard
            // distance. Both thresholds derive from the standard, not the mouse.
            var onLineBand = sideOffset / 2.0;

            using (var preview = new MText())
            {
                preview.SetDatabaseDefaults(db);
                if (!styleId.IsNull) preview.TextStyleId = styleId;
                preview.Contents = labelText;
                preview.TextHeight = textHeight;
                preview.Attachment = AttachmentPoint.MiddleCenter;
                preview.Location = Point3d.Origin;

                var jig = new LineLabelJig(preview, curve, db, sideOffset, onLineBand,
                                           ls.AlignToLine, wordings);
                var dragged = ed.Drag(jig);

                // A wording switch ends the drag with a keyword; keep jigging until
                // the user actually clicks or cancels.
                while (dragged.Status == PromptStatus.Keyword)
                    dragged = ed.Drag(jig);

                if (dragged.Status != PromptStatus.OK || jig.Plan == null)
                {
                    ed.WriteMessage("\nCancelled; nothing was placed.");
                    return false;
                }
                labelText = jig.CurrentWording;

                var layerName = resolution.Layer;
                var layerId = CadUtil.EnsureLayer(db, tr, layerName);
                Ownership.EnsureRegApp(db, tr);

                PlaceLineLabel(db, tr, jig.Plan, labelText, textHeight, styleId,
                               layerId, layerName, ls.DrawMask, candidates[0].Code,
                               rulesVersion, ManualLineLabelMark);

                ed.WriteMessage("\nPlaced \"{0}\" on layer {1}, {2}.",
                                labelText, layerName,
                                LineSideParser.Describe(jig.Side, ls.SideOffsetFeet));

                // Span markers ride along: two fence shots noted GATE get their
                // GATE label centred between them, no extra clicks.
                PlaceSpanLabels(db, tr, ed, curve, spanMarkers, ls, textHeight,
                                styleId, layerId, layerName, candidates[0].Code,
                                rulesVersion);
                return true;
            }
        }

        /// <summary>
        /// Labels the space BETWEEN two edges: a driveway between its two
        /// edge-of-asphalt lines, a path between its edges. Pick both edges, slide
        /// the preview -- it stays centred on the midline, following the edges'
        /// averaged direction -- and click to place. Wording switches alternates,
        /// exactly like FTFLABELLINE. Both edges stay read-only; the label carries
        /// the manual mark so the bulk pass never touches it.
        /// </summary>
        [CommandMethod("FTFLABELBETWEEN", CommandFlags.Modal)]
        [CommandMethod("FTFB", CommandFlags.Modal)]
        public void FtfLabelBetween()
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

            var ls = settings.LineLabels;
            var catalog = new LineworkCatalog(cfg.LineFeatures, cfg.IgnoreLineNames);
            var placedCount = 0;

            while (true)
            {
                var first = PickEdge(ed, "\nSelect the first edge (Enter/Esc to finish): ");
                if (first == ObjectId.Null) break;

                var second = PickEdge(ed, "\nSelect the second edge: ");
                if (second == ObjectId.Null) break;

                if (first == second)
                {
                    ed.WriteMessage("\nThat is the same line twice. Pick the two " +
                                    "edges of the feature.");
                    continue;
                }

                using (doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    if (PlaceOneBetweenLabel(doc, tr, first, second, catalog, ls,
                                             cfg.Version))
                        placedCount++;
                    tr.Commit();
                }
            }

            ed.WriteMessage("\nFTFLABELBETWEEN: {0} label(s) placed.\n", placedCount);
        }

        private static ObjectId PickEdge(Editor ed, string message)
        {
            var options = new PromptEntityOptions(message) { AllowNone = true };
            var picked = ed.GetEntity(options);
            return picked.Status == PromptStatus.OK ? picked.ObjectId : ObjectId.Null;
        }

        private static bool PlaceOneBetweenLabel(
            Autodesk.AutoCAD.ApplicationServices.Document doc, Transaction tr,
            ObjectId firstId, ObjectId secondId, LineworkCatalog catalog,
            FieldCodes.Settings.LineLabelSettings ls, string rulesVersion)
        {
            var ed = doc.Editor;
            var db = doc.Database;

            var curveA = tr.GetObject(firstId, OpenMode.ForRead, false, true) as Curve;
            var curveB = tr.GetObject(secondId, OpenMode.ForRead, false, true) as Curve;
            if (curveA == null || curveB == null)
            {
                ed.WriteMessage("\nBoth picks must be lines, polylines, arcs or figures.");
                return false;
            }
            if (Ownership.Read(curveA) != null || Ownership.Read(curveB) != null)
            {
                ed.WriteMessage("\nOne of those is FTF's own output. Pick the source " +
                                "edges themselves.");
                return false;
            }

            // The FIRST edge picked decides everything -- identity, wording, layer,
            // text settings. The second edge only says where the far side is, so a
            // driveway between an asphalt edge and a curb labels as the asphalt.
            string how;
            var candidates = StagedCandidates(db, tr, curveA, catalog, out how);
            if (candidates == null)
            {
                ed.WriteMessage(
                    "\nThe first edge is not a configured line feature (layer " +
                    "\"{0}\"). Pick the feature's own edge first -- nothing was " +
                    "placed.", curveA.Layer);
                return false;
            }

            // Between two edges the label names the SURFACE -- ASPHALT, not EDGE OF
            // PAVEMENT -- from the feature's configured between-wordings.
            var wordings = LineworkCatalog.BetweenChoices(candidates);
            if (wordings.Count == 0)
            {
                ed.WriteMessage(
                    "\nIdentified, but no labelling standard is configured for " +
                    "{0}. Nothing was placed.", candidates[0].Code);
                return false;
            }
            var labelText = wordings[0];

            var layerCatalog = FtfLineworkService.LayerNames(db, tr);
            var resolution = LabelLayerResolver.Resolve(curveA.Layer, candidates,
                                                        layerCatalog, ls.DefaultLabelLayer);

            var featureName = string.Join(" / ", candidates
                .Select(c => string.IsNullOrEmpty(c.Name) ? c.Code : c.Name)
                .Distinct().ToArray());
            ed.WriteMessage("\nFeature: {0} (first edge, by {1})", featureName, how);
            ed.WriteMessage("\nBetween: {0} and {1}", curveA.Layer, curveB.Layer);
            ed.WriteMessage("\nLabel layer: {0}", resolution.Describe());
            ed.WriteMessage("\nLabel: {0}{1}", labelText,
                wordings.Count > 1
                    ? "  (type W for: " + string.Join(" / ",
                          wordings.Skip(1).ToArray()) + ")"
                    : "");

            var scale = CadUtil.DrawingUnitsPerPlottedUnit(db);
            var textHeight = ls.TextHeightPlotted * scale;
            var styleId = Setup.DrawingResources.FindTextStyle(db, tr, ls.TextStyle);

            using (var preview = new MText())
            {
                preview.SetDatabaseDefaults(db);
                if (!styleId.IsNull) preview.TextStyleId = styleId;
                preview.Contents = labelText;
                preview.TextHeight = textHeight;
                preview.Attachment = AttachmentPoint.MiddleCenter;
                preview.Location = Point3d.Origin;

                var jig = new LineBetweenJig(preview, curveA, curveB, db,
                                             ls.AlignToLine, wordings);
                var dragged = ed.Drag(jig);
                while (dragged.Status == PromptStatus.Keyword)
                    dragged = ed.Drag(jig);

                if (dragged.Status != PromptStatus.OK || jig.Plan == null)
                {
                    ed.WriteMessage("\nCancelled; nothing was placed.");
                    return false;
                }

                var layerId = CadUtil.EnsureLayer(db, tr, resolution.Layer);
                Ownership.EnsureRegApp(db, tr);

                PlaceLineLabel(db, tr, jig.Plan, jig.CurrentWording, textHeight,
                               styleId, layerId, resolution.Layer, ls.DrawMask,
                               candidates[0].Code, rulesVersion, ManualLineLabelMark);

                ed.WriteMessage("\nPlaced \"{0}\" on layer {1}, centred between the " +
                                "edges.", jig.CurrentWording, resolution.Layer);
                return true;
            }
        }

        /// <summary>
        /// Labels a staircase and says how many steps: select the stair lines (each
        /// drawn tread edge is one line), and the count fills the configured format
        /// -- "STAIRS (12 STEPS)". The label reads along the treads' shared
        /// direction and follows the cursor freely; click to place. The stair
        /// lines themselves stay read-only.
        /// </summary>
        [CommandMethod("FTFLABELSTAIRS", CommandFlags.Modal | CommandFlags.UsePickSet)]
        [CommandMethod("FTFS", CommandFlags.Modal | CommandFlags.UsePickSet)]
        public void FtfLabelStairs()
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

            var ls = settings.LineLabels;

            var selection = ed.GetSelection(new PromptSelectionOptions
            {
                MessageForAdding = "\nSelect the stair lines (each tread edge is one line): "
            });
            if (selection.Status != PromptStatus.OK) return;

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var directions = new List<double>();
                var sumX = 0.0;
                var sumY = 0.0;
                string sourceLayer = null;
                var skipped = 0;

                foreach (SelectedObject picked in selection.Value)
                {
                    var curve = tr.GetObject(picked.ObjectId, OpenMode.ForRead, false, true)
                        as Curve;
                    if (curve == null || Ownership.Read(curve) != null) { skipped++; continue; }

                    Point3d start, end;
                    try { start = curve.StartPoint; end = curve.EndPoint; }
                    catch (Autodesk.AutoCAD.Runtime.Exception) { skipped++; continue; }

                    if (start.DistanceTo(end) < 1e-9) { skipped++; continue; }

                    directions.Add(Math.Atan2(end.Y - start.Y, end.X - start.X));
                    sumX += (start.X + end.X) / 2.0;
                    sumY += (start.Y + end.Y) / 2.0;
                    if (sourceLayer == null) sourceLayer = curve.Layer;
                }

                var count = directions.Count;
                if (count == 0)
                {
                    ed.WriteMessage("\nNo usable stair lines in that selection.\n");
                    tr.Commit();
                    return;
                }
                if (skipped > 0)
                    ed.WriteMessage("\n{0} selected object(s) were not stair lines and " +
                                    "were not counted.", skipped);

                var label = (cfg.StairsLabelFormat ?? "{count} STEPS")
                    .Replace("{count}", count.ToString(
                        System.Globalization.CultureInfo.InvariantCulture));

                var direction = LineLabelPlanner.AverageDirection(directions);

                var catalog = new LineworkCatalog(cfg.LineFeatures, cfg.IgnoreLineNames);
                var resolution = LabelLayerResolver.Resolve(sourceLayer,
                    catalog.FindByLayer(sourceLayer),
                    FtfLineworkService.LayerNames(db, tr), ls.DefaultLabelLayer);

                ed.WriteMessage("\n{0} stair line(s) selected.", count);
                ed.WriteMessage("\nLabel layer: {0}", resolution.Describe());
                ed.WriteMessage("\nLabel: {0}", label);

                var scale = CadUtil.DrawingUnitsPerPlottedUnit(db);
                var textHeight = ls.TextHeightPlotted * scale;
                var styleId = Setup.DrawingResources.FindTextStyle(db, tr, ls.TextStyle);

                // Centred in the staircase automatically: the average of the tread
                // midpoints, reading along the treads. Nudge it afterwards if a
                // tread runs through it -- it is a manual label and stays put.
                var plan = LineLabelPlanner.PlaceAt(sumX / count, sumY / count,
                    direction, LineLabelSide.OnLine, 0.0, ls.AlignToLine);

                var layerId = CadUtil.EnsureLayer(db, tr, resolution.Layer);
                Ownership.EnsureRegApp(db, tr);

                PlaceLineLabel(db, tr, plan, label, textHeight, styleId,
                               layerId, resolution.Layer, ls.DrawMask, "STR",
                               cfg.Version, ManualLineLabelMark);

                ed.WriteMessage("\nPlaced \"{0}\" centred in the stairs, on layer {1}.\n",
                                label, resolution.Layer);

                tr.Commit();
            }
        }

        /// <summary>The staged identification -- figure name, TrimbleName, vertex
        /// point notes, layer -- as candidate features, or null when nothing
        /// identifies the line. Shared by the between-edges command.</summary>
        private static IList<LineFeatureRule> StagedCandidates(
            Database db, Transaction tr, Curve curve, LineworkCatalog catalog,
            out string how)
        {
            string figureName = null;
            var figure = curve as Autodesk.Civil.DatabaseServices.SurveyFigure;
            if (figure != null) figureName = figure.Name;
            else
            {
                var featureLine = curve as Autodesk.Civil.DatabaseServices.FeatureLine;
                if (featureLine != null) figureName = featureLine.Name;
            }

            var byFigure = catalog.FindByFigureName(figureName);
            if (byFigure != null)
            {
                how = "figure name";
                return new List<LineFeatureRule> { byFigure };
            }

            var trimbleName = FtfLineworkService.TrimbleNameOf(curve);
            var byTrimble = catalog.FindByTrimbleName(trimbleName);
            if (byTrimble.Count > 0) { how = "Trimble name"; return byTrimble; }

            var notes = PointNotes.Consensus(NotesAtVertices(db, tr, curve), catalog);
            if (notes.Features.Count > 0) { how = "point notes"; return notes.Features; }

            var byLayer = catalog.FindByLayer(curve.Layer);
            if (byLayer.Count > 0) { how = "layer"; return byLayer; }

            how = null;
            return null;
        }

        /// <summary>
        /// The between-edges preview: for each cursor sample, the nearest point on
        /// EACH edge and the tangent there; the label sits at their midpoint,
        /// following the averaged direction. Both curves are only ever read.
        /// </summary>
        private sealed class LineBetweenJig : EntityJig
        {
            private readonly Curve _curveA;
            private readonly Curve _curveB;
            private readonly bool _alignToLine;
            private readonly IList<string> _wordings;
            private int _wordingIndex;
            private Point3d _cursor;

            public PlannedLineLabel Plan { get; private set; }

            public string CurrentWording
            {
                get { return _wordings[_wordingIndex % _wordings.Count]; }
            }

            public LineBetweenJig(MText preview, Curve curveA, Curve curveB,
                                  Database db, bool alignToLine, IList<string> wordings)
                : base(preview)
            {
                _curveA = curveA;
                _curveB = curveB;
                _alignToLine = alignToLine;
                _wordings = wordings != null && wordings.Count > 0
                    ? wordings
                    : new List<string> { preview.Contents };
                _cursor = new Point3d(double.NaN, double.NaN, 0.0);
            }

            protected override SamplerStatus Sampler(JigPrompts prompts)
            {
                var options = new JigPromptPointOptions(
                    "\nPosition the label between the edges -- \"" + CurrentWording +
                    "\" (click to place): ");
                options.UserInputControls = UserInputControls.Accept3dCoordinates;
                if (_wordings.Count > 1) options.Keywords.Add("Wording");

                var result = prompts.AcquirePoint(options);

                if (result.Status == PromptStatus.Keyword)
                {
                    _wordingIndex++;
                    return SamplerStatus.OK;
                }

                if (result.Status != PromptStatus.OK) return SamplerStatus.Cancel;

                if (!double.IsNaN(_cursor.X) &&
                    result.Value.DistanceTo(_cursor) < 1e-9)
                    return SamplerStatus.NoChange;

                _cursor = result.Value;
                return SamplerStatus.OK;
            }

            protected override bool Update()
            {
                double ax, ay, aDir, bx, by, bDir;
                try
                {
                    var flat = new Point3d(_cursor.X, _cursor.Y, 0.0);

                    var closestA = _curveA.GetClosestPointTo(flat, false);
                    var paramA = _curveA.GetParameterAtPoint(closestA);
                    var derivA = _curveA.GetFirstDerivative(paramA);
                    ax = closestA.X; ay = closestA.Y;
                    aDir = Math.Atan2(derivA.Y, derivA.X);

                    var closestB = _curveB.GetClosestPointTo(flat, false);
                    var paramB = _curveB.GetParameterAtPoint(closestB);
                    var derivB = _curveB.GetFirstDerivative(paramB);
                    bx = closestB.X; by = closestB.Y;
                    bDir = Math.Atan2(derivB.Y, derivB.X);
                }
                catch (Autodesk.AutoCAD.Runtime.Exception)
                {
                    return false;
                }

                Plan = LineLabelPlanner.PlaceBetween(ax, ay, aDir, bx, by, bDir,
                                                     _alignToLine);

                var text = (MText)Entity;
                if (!string.Equals(text.Contents, CurrentWording, StringComparison.Ordinal))
                    text.Contents = CurrentWording;
                text.Location = new Point3d(Plan.X, Plan.Y, 0.0);
                text.Rotation = Plan.RotationRadians;
                return true;
            }
        }

        /// <summary>
        /// Raw descriptions of every CogoPoint that coincides EXACTLY with a vertex
        /// of the line (0.01-unit buckets). TBC draws linework through the shot
        /// points, so a coincident point created that vertex and its field note
        /// names the line. Near-misses are deliberately not matched -- coincidence
        /// is evidence, proximity would be guessing. Read-only throughout.
        /// </summary>
        private static IList<string> NotesAtVertices(Database db, Transaction tr,
                                                     Curve curve)
        {
            var notes = new List<string>();

            var byBucket = BuildNoteIndex(db, tr);
            if (byBucket.Count == 0) return notes;

            foreach (var vertex in VerticesOf(curve))
            {
                List<string> here;
                if (byBucket.TryGetValue(Bucket(vertex), out here))
                    notes.AddRange(here);
            }

            return notes;
        }

        /// <summary>Every survey point's raw note, indexed by its 0.01-unit
        /// position bucket, so vertices can look their creating shots up.</summary>
        private static Dictionary<string, List<string>> BuildNoteIndex(Database db,
                                                                       Transaction tr)
        {
            var byBucket = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var cogo in CadUtil.CogoPointsInOrder(db, tr))
            {
                var description = cogo.RawDescription;
                if (string.IsNullOrWhiteSpace(description)) continue;

                var key = Bucket(cogo.Location);
                List<string> list;
                if (!byBucket.TryGetValue(key, out list))
                {
                    list = new List<string>();
                    byBucket[key] = list;
                }
                list.Add(description);
            }
            return byBucket;
        }

        /// <summary>
        /// Reads span markers along the line and labels each marked pair: two fence
        /// shots noted GATE get a GATE label centred between them, on the line,
        /// reading along it. An odd marker out is reported, never guessed.
        /// </summary>
        private static int PlaceSpanLabels(Database db, Transaction tr, Editor ed,
                                           Curve curve,
                                           IList<SpanMarkerRule> markers,
                                           FieldCodes.Settings.LineLabelSettings ls,
                                           double textHeight, ObjectId styleId,
                                           ObjectId layerId, string layerName,
                                           string sourceCode, string rulesVersion)
        {
            if (markers == null || markers.Count == 0) return 0;

            var index = BuildNoteIndex(db, tr);
            if (index.Count == 0) return 0;

            var placedTotal = 0;

            foreach (var marker in markers)
            {
                if (marker == null || !marker.Enabled ||
                    string.IsNullOrWhiteSpace(marker.Token)) continue;

                var distances = new List<double>();
                foreach (var vertex in VerticesOf(curve))
                {
                    List<string> notes;
                    if (!index.TryGetValue(Bucket(vertex), out notes)) continue;
                    if (!notes.Any(n => SpanFinder.HasToken(n, marker.Token))) continue;

                    try { distances.Add(curve.GetDistAtPoint(vertex)); }
                    catch (Autodesk.AutoCAD.Runtime.Exception) { }
                }

                bool leftover;
                var spans = SpanFinder.Pair(distances, out leftover);

                foreach (var span in spans)
                {
                    double x, y, direction;
                    try
                    {
                        var parameter = curve.GetParameterAtDistance(span.Middle);
                        var point = curve.GetPointAtParameter(parameter);
                        var derivative = curve.GetFirstDerivative(parameter);
                        x = point.X; y = point.Y;
                        direction = Math.Atan2(derivative.Y, derivative.X);
                    }
                    catch (Autodesk.AutoCAD.Runtime.Exception) { continue; }

                    var plan = LineLabelPlanner.PlaceAt(x, y, direction,
                        LineLabelSide.OnLine, 0.0, ls.AlignToLine, span.Middle);
                    var label = string.IsNullOrWhiteSpace(marker.Label)
                        ? marker.Token.Trim() : marker.Label;

                    PlaceLineLabel(db, tr, plan, label, textHeight, styleId, layerId,
                                   layerName, ls.DrawMask, sourceCode, rulesVersion,
                                   ManualLineLabelMark);
                    placedTotal++;
                }

                if (spans.Count > 0 || leftover)
                    ed.WriteMessage("\n{0} {1} label(s) placed between marked points{2}.",
                        spans.Count, marker.Token,
                        leftover ? " -- one marked point had no partner and was " +
                                   "left unlabelled" : "");
            }

            return placedTotal;
        }

        private static string Bucket(Point3d p)
        {
            return Math.Round(p.X, 2).ToString("0.##",
                       System.Globalization.CultureInfo.InvariantCulture) + ":" +
                   Math.Round(p.Y, 2).ToString("0.##",
                       System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>The line's vertices. Polylines put a vertex at every integer
        /// parameter; anything else contributes its two ends.</summary>
        private static IEnumerable<Point3d> VerticesOf(Curve curve)
        {
            var isPolyline = curve is Polyline || curve is Polyline2d ||
                             curve is Polyline3d;
            double start, end;
            try
            {
                start = curve.StartParam;
                end = curve.EndParam;
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                yield break;
            }

            if (!isPolyline)
            {
                yield return curve.StartPoint;
                yield return curve.EndPoint;
                yield break;
            }

            for (var p = Math.Ceiling(start); p <= end + 1e-9; p += 1.0)
            {
                Point3d point;
                try { point = curve.GetPointAtParameter(Math.Min(p, end)); }
                catch (Autodesk.AutoCAD.Runtime.Exception) { continue; }
                yield return point;
            }
        }

        /// <summary>
        /// The live preview: an EntityJig around one MText. Each cursor sample finds
        /// the nearest point on the source curve, reads the local tangent there, and
        /// runs the SAME placement calculation the engine uses -- so what tracks the
        /// cursor is exactly what one click commits. The curve is only ever read.
        /// </summary>
        private sealed class LineLabelJig : EntityJig
        {
            private readonly Curve _curve;
            private readonly double _sideOffset;
            private readonly double _onLineBand;
            private readonly bool _alignToLine;
            private readonly IList<string> _wordings;
            private int _wordingIndex;
            private Point3d _cursor;

            public PlannedLineLabel Plan { get; private set; }
            public LineLabelSide Side { get; private set; }

            /// <summary>The wording currently on the cursor -- what a click places.</summary>
            public string CurrentWording
            {
                get { return _wordings[_wordingIndex % _wordings.Count]; }
            }

            public LineLabelJig(MText preview, Curve curve, Database db,
                                double sideOffset, double onLineBand, bool alignToLine,
                                IList<string> wordings)
                : base(preview)
            {
                _curve = curve;
                _sideOffset = sideOffset;
                _onLineBand = onLineBand;
                _alignToLine = alignToLine;
                _wordings = wordings != null && wordings.Count > 0
                    ? wordings
                    : new List<string> { preview.Contents };
                _cursor = new Point3d(double.NaN, double.NaN, 0.0);
                Side = LineLabelSide.OnLine;
            }

            protected override SamplerStatus Sampler(JigPrompts prompts)
            {
                var options = new JigPromptPointOptions(
                    "\nPosition the label -- \"" + CurrentWording + "\" (cursor side " +
                    "chooses the side; click to place): ");
                options.UserInputControls = UserInputControls.Accept3dCoordinates;
                if (_wordings.Count > 1) options.Keywords.Add("Wording");

                var result = prompts.AcquirePoint(options);

                // Typing the Wording keyword switches to the next configured text --
                // EDGE OF PAVEMENT to EOP -- right on the cursor. The drag call
                // returns Keyword and the command re-enters it immediately.
                if (result.Status == PromptStatus.Keyword)
                {
                    _wordingIndex++;
                    return SamplerStatus.OK;
                }

                if (result.Status != PromptStatus.OK) return SamplerStatus.Cancel;

                if (!double.IsNaN(_cursor.X) &&
                    result.Value.DistanceTo(_cursor) < 1e-9)
                    return SamplerStatus.NoChange;

                _cursor = result.Value;
                return SamplerStatus.OK;
            }

            protected override bool Update()
            {
                double x, y, direction, dx, dy;
                try
                {
                    // Nearest point on the source geometry and the tangent there --
                    // curves get the local tangent at the placement spot.
                    var flat = new Point3d(_cursor.X, _cursor.Y, 0.0);
                    var closest = _curve.GetClosestPointTo(flat, false);
                    var parameter = _curve.GetParameterAtPoint(closest);
                    var derivative = _curve.GetFirstDerivative(parameter);

                    direction = Math.Atan2(derivative.Y, derivative.X);
                    x = closest.X;
                    y = closest.Y;
                    dx = flat.X - closest.X;
                    dy = flat.Y - closest.Y;
                }
                catch (Autodesk.AutoCAD.Runtime.Exception)
                {
                    return false;       // degenerate geometry: keep the last preview
                }

                // Side from the cursor, offset distance from the standard. The offset
                // uses the raw tangent, so the readability flip below can never move
                // the label off the side the user is pointing at.
                Side = LineSideParser.FromCursor(direction, dx, dy, _onLineBand);
                Plan = LineLabelPlanner.PlaceAt(x, y, direction, Side, _sideOffset,
                                                _alignToLine);

                var text = (MText)Entity;
                if (!string.Equals(text.Contents, CurrentWording, StringComparison.Ordinal))
                    text.Contents = CurrentWording;
                text.Location = new Point3d(Plan.X, Plan.Y, 0.0);
                text.Rotation = Plan.RotationRadians;
                return true;
            }
        }

        /// <summary>
        /// READ-ONLY metadata inspection of the drawing's line entities: XData (all
        /// applications), extension dictionaries (where Map 3D object data and AEC
        /// property sets physically live), hyperlinks, and the drawing's RegApp table.
        /// Written to a text report beside the drawing.
        ///
        /// Purpose: determine whether TBC-exported polylines retain ANY surviving
        /// link to their source field coding (ASPH L, RWC1 ...) before concluding the
        /// coding is gone. Nothing is created, modified or stamped.
        /// </summary>
        [CommandMethod("FTFLINEMETA", CommandFlags.Modal)]
        public void FtfLineMeta()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            var ed = doc.Editor;
            var db = doc.Database;
            var report = new System.Text.StringBuilder();

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                report.AppendLine("FTF LINEWORK METADATA INSPECTION (read-only)");
                report.AppendLine("Drawing: " + (db.Filename ?? "(unsaved)"));
                report.AppendLine();

                // Every application that ever attached XData anywhere in the drawing.
                report.AppendLine("=== Registered XData applications (whole drawing) ===");
                var regApps = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
                foreach (ObjectId id in regApps)
                {
                    var app = (RegAppTableRecord)tr.GetObject(id, OpenMode.ForRead);
                    report.AppendLine("  " + app.Name);
                }
                report.AppendLine();

                var ms = CadUtil.ModelSpace(db, tr, OpenMode.ForRead);

                var perLayerSampled = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var withXData = 0;
                var withExtDict = 0;
                var withHyperlinks = 0;
                var total = 0;
                const int samplesPerLayer = 3;

                foreach (ObjectId id in ms)
                {
                    if (id.IsErased) continue;

                    var entity = tr.GetObject(id, OpenMode.ForRead, false, true) as AcEntity;
                    if (entity == null) continue;
                    if (Ownership.Read(entity) != null) continue;
                    if (!(entity is Curve)) continue;

                    total++;

                    var hasXData = entity.XData != null;
                    var hasExtDict = entity.ExtensionDictionary != ObjectId.Null;
                    var hasLinks = entity.Hyperlinks != null && entity.Hyperlinks.Count > 0;
                    if (hasXData) withXData++;
                    if (hasExtDict) withExtDict++;
                    if (hasLinks) withHyperlinks++;

                    int sampled;
                    perLayerSampled.TryGetValue(entity.Layer, out sampled);
                    if (sampled >= samplesPerLayer && !hasXData && !hasExtDict && !hasLinks)
                        continue;
                    if (sampled >= samplesPerLayer * 2) continue;
                    perLayerSampled[entity.Layer] = sampled + 1;

                    report.AppendLine("--- " + entity.GetType().Name + "  handle=" +
                                      entity.Handle + "  layer=" + entity.Layer + " ---");

                    DumpXData(entity, report);
                    DumpExtensionDictionary(tr, entity, report);
                    DumpHyperlinks(entity, report);
                    report.AppendLine();
                }

                report.AppendLine("=== Summary ===");
                report.AppendLine(string.Format(
                    "  {0} line entities: {1} with XData, {2} with extension " +
                    "dictionaries, {3} with hyperlinks.",
                    total, withXData, withExtDict, withHyperlinks));

                tr.Commit();
            }

            var path = db.Filename == null
                ? null
                : System.IO.Path.ChangeExtension(db.Filename, null) + ".ftf-linemeta.txt";

            if (path != null)
            {
                try
                {
                    System.IO.File.WriteAllText(path, report.ToString(),
                        new System.Text.UTF8Encoding(true));
                    ed.WriteMessage("\nFTFLINEMETA: report written to {0}\n", path);
                }
                catch (System.IO.IOException ex)
                {
                    ed.WriteMessage("\nCould not write {0}: {1}\n", path, ex.Message);
                }
            }
            else
            {
                ed.WriteMessage("\n{0}\n", report);
            }
        }

        private static void DumpXData(AcEntity entity, System.Text.StringBuilder report)
        {
            using (var xdata = entity.XData)
            {
                if (xdata == null)
                {
                    report.AppendLine("  XData: none");
                    return;
                }

                report.AppendLine("  XData:");
                foreach (TypedValue tv in xdata)
                    report.AppendLine("    [" + tv.TypeCode + "] " + Shorten(tv.Value));
            }
        }

        private static void DumpExtensionDictionary(Transaction tr, AcEntity entity,
                                                    System.Text.StringBuilder report)
        {
            if (entity.ExtensionDictionary == ObjectId.Null)
            {
                report.AppendLine("  Extension dictionary: none");
                return;
            }

            report.AppendLine("  Extension dictionary:");
            var dictionary = tr.GetObject(entity.ExtensionDictionary,
                                          OpenMode.ForRead) as DBDictionary;
            if (dictionary == null) return;

            foreach (DBDictionaryEntry entry in dictionary)
            {
                var value = tr.GetObject(entry.Value, OpenMode.ForRead, false, true);
                report.AppendLine("    " + entry.Key + " (" + value.GetType().Name + ")");

                var xrecord = value as Xrecord;
                if (xrecord != null)
                {
                    using (var data = xrecord.Data)
                    {
                        if (data == null) continue;
                        var shown = 0;
                        foreach (TypedValue tv in data)
                        {
                            report.AppendLine("      [" + tv.TypeCode + "] " + Shorten(tv.Value));
                            if (++shown >= 12) { report.AppendLine("      ..."); break; }
                        }
                    }
                }

                var nested = value as DBDictionary;
                if (nested != null)
                {
                    foreach (DBDictionaryEntry inner in nested)
                        report.AppendLine("      " + inner.Key);
                }
            }
        }

        private static void DumpHyperlinks(AcEntity entity, System.Text.StringBuilder report)
        {
            if (entity.Hyperlinks == null || entity.Hyperlinks.Count == 0) return;

            report.AppendLine("  Hyperlinks:");
            foreach (HyperLink link in entity.Hyperlinks)
                report.AppendLine("    " + Shorten(link.Name) + " | " +
                                  Shorten(link.Description));
        }

        private static string Shorten(object value)
        {
            var text = value == null ? "(null)" : value.ToString();
            return text.Length <= 90 ? text : text.Substring(0, 90) + "...";
        }

        private static void PlaceLineLabel(Database db, Transaction tr,
                                           PlannedLineLabel plan, string labelText,
                                           double textHeight, ObjectId styleId,
                                           ObjectId layerId, string layerName,
                                           bool drawMask, string sourceCode,
                                           string rulesVersion, string tagText = null)
        {
            var position = new Point3d(plan.X, plan.Y, 0.0);

            // MTEXT, per the office standard for label text.
            using (var text = new MText())
            {
                text.SetDatabaseDefaults(db);
                if (!styleId.IsNull) text.TextStyleId = styleId;

                text.Contents = labelText;
                text.TextHeight = textHeight;
                text.Rotation = plan.RotationRadians;

                // Centred on the placement point, both axes.
                text.Attachment = AttachmentPoint.MiddleCenter;
                text.Location = position;
                text.LayerId = layerId;

                CadUtil.AddToModelSpace(db, tr, text);

                if (drawMask)
                    PlaceLineMask(db, tr, text, position, plan.RotationRadians,
                                  textHeight, layerId, sourceCode, rulesVersion, tagText);

                // Stamped with the source code in the identity slot, so a report can
                // say which feature family produced it.
                Ownership.Stamp(text, sourceCode, rulesVersion,
                                FtfEntityKind.LineLabel, position, tagText);
            }
        }

        /// <summary>
        /// A wipeout under the label -- geometry and draw order from the shared
        /// TextMask builder, stamped here with the line-label ownership kind.
        /// </summary>
        private static void PlaceLineMask(Database db, Transaction tr, AcEntity text,
                                          Point3d centre, double rotation,
                                          double textHeight, ObjectId layerId,
                                          string sourceCode, string rulesVersion,
                                          string tagText = null)
        {
            var maskId = TextMask.Place(db, tr, text, centre, rotation, textHeight,
                                        layerId);
            if (maskId.IsNull) return;

            var wipeout = (AcEntity)tr.GetObject(maskId, OpenMode.ForWrite);
            Ownership.Stamp(wipeout, sourceCode, rulesVersion,
                            FtfEntityKind.LineMask, null, tagText);
        }

        /// <summary>
        /// An AutoCAD curve as the planner's read-only path. Point and tangent come
        /// from the curve's own parameterisation, so labels follow arcs correctly.
        /// </summary>
        private sealed class CurvePath : ILinePath
        {
            private readonly Curve _curve;
            private readonly double _length;

            public CurvePath(Curve curve)
            {
                _curve = curve;
                double length;
                try
                {
                    length = curve.GetDistanceAtParameter(curve.EndParam);
                }
                catch (Autodesk.AutoCAD.Runtime.Exception)
                {
                    length = 0.0;
                }
                _length = length;
            }

            public double Length { get { return _length; } }

            public void At(double distance, out double x, out double y,
                           out double directionRadians)
            {
                if (distance < 0) distance = 0;
                if (distance > _length) distance = _length;

                var parameter = _curve.GetParameterAtDistance(distance);
                var point = _curve.GetPointAtParameter(parameter);
                var derivative = _curve.GetFirstDerivative(parameter);

                x = point.X;
                y = point.Y;
                directionRadians = Math.Atan2(derivative.Y, derivative.X);
            }
        }
    }
}
