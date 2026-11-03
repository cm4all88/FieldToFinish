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
    public sealed partial class LineworkCommands
    {
        /// <summary>
        /// Stamped into the tag slot of labels placed interactively by FTFLABELLINE.
        /// The bulk FTFLINELABELS pass never deletes a label carrying this mark: the
        /// surveyor put it exactly where it is. FTFCLEAN still removes everything.
        /// </summary>
        internal const string ManualLineLabelMark = "manual";

        /// <summary>The last bulk pass's report, for FTFLABELALL's summary.</summary>
        internal static LineLabelPassReport LastReport { get; private set; }

        [CommandMethod("FTFLINES", CommandFlags.Modal)]
        public void FtfLines()
        {
            // Traced so the Civil 3D harnesses can prove it ran (it does not go through FtfSession.Run).
            FtfCommandTrace.Start("FTFLINES");
            try { Lines(); }
            catch (System.Exception ex)
            {
                // Recorded for the smoke test's trace, then left to AutoCAD as before: headless, an
                // exception nothing catches can end accoreconsole without a word.
                FtfCommandTrace.Failed("FTFLINES", ex);
                throw;
            }
            FtfCommandTrace.Finish("FTFLINES");
        }

        private static void Lines()
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
            RunLineLabels(false);
        }

        /// <summary>
        /// The whole-drawing line label pass. <paramref name="force"/> runs it even when the
        /// automatic pass is off in Settings > Line Labels: FTFLABELALL is an explicit
        /// request to label everything, the setting only governs FTFRUN.
        /// </summary>
        internal void RunLineLabels(bool force)
        {
            LastReport = null;
            FtfSession.Run("FTFLINELABELS", (db, tr, ed) =>
            {
                var cfg = FtfSession.Rules(db);
                var settings = FtfSession.SettingsFor(db, cfg);
                var ls = settings.LineLabels;

                if (!ls.Enabled && !force)
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
                    // Gate labels are the gate pass's to keep, replace or remove (it keeps the
                    // ones moved or reworded by hand).
                    if (IsGateTag(s.TagText)) return false;
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

                // Line labels by feature code: Settings > Style Assignments (Line labels),
                // falling back to Line Labels. Missing styles are listed at the end.
                var styler = new AnnotationStyler(db, tr, settings, cfg);
                var turnedOff = 0;

                var defaultSide = LineSideParser.ParsePlacement(ls.DefaultPlacement);
                var sideOffset = ls.SideOffsetFeet * unitsPerFoot;
                var layerCatalog = FtfLineworkService.LayerNames(db, tr);

                var report = new LineLabelPassReport();

                // Every survey point's note, indexed once: the same evidence FTFLABELLINE
                // reads, so a fence polyline on the layer FNC and FHW share is named by the
                // shots it was drawn through.
                var noteIndex = BuildNoteIndex(db, tr);
                var heightIssues = new List<HeightIssue>();
                var lineItems = new Dictionary<string, FieldCodes.Review.ReviewItem>(StringComparer.Ordinal);

                // Gate posts are the gate pass's (after the lines): the older two-marked-shots
                // rule keeps only the span markers that are not gate posts.
                var spanMarkers = SpanMarkersWithoutGates(cfg.SpanMarkers, settings.Gates);

                // Labels placed by hand, by feature code. A line carrying one -- on it, or at
                // the side offset from it -- is the surveyor's, and the pass leaves it be.
                var manual = ManualLabelPositions(db, tr);
                var manualReach = sideOffset + textHeight * 1.5;
                var linetypeCache = new Dictionary<ObjectId, LinetypeLook>();

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
                    string how;
                    var candidates = StagedCandidates(db, tr, curve, catalog, out how, noteIndex);
                    if (candidates == null || candidates.Count == 0) continue;

                    // A fence's height, read off its shots ("FNC B 3.5") and carried along it until
                    // another shot records a different one: each label says the height where it sits.
                    // Read for every fence, labelled here or not, so an unreadable one is reported.
                    var heights = string.IsNullOrWhiteSpace(ls.HeightFormat) || !LineHeights.Applies(candidates[0])
                        ? null : RecordedHeights(curve, candidates, noteIndex, ls.HeightMaxFeet, report.Problems,
                                                 settings.Gates.PostCodes, heightIssues);

                    var labelText = LineworkCatalog.UnanimousLabel(candidates);
                    if (labelText == null)
                    {
                        report.NotLabelled(candidates, entity.Layer);
                        UnidentifiedLine(lineItems, entity, candidates);
                        continue;
                    }

                    if (HasManualLabel(curve, candidates, manual, manualReach)) { report.AlreadyByHand++; continue; }

                    // A linetype that draws its own letter or symbol (water ---W---, power
                    // ---P---) names the line, and the legend explains it: no text label.
                    // A gate along it still gets its GATE label -- the legend cannot show that.
                    if (ls.LeaveMarkedLinetypesToLegend && !LineworkCatalog.LabelsOverMarkedLinetype(candidates))
                    {
                        var look = LinetypeOf(db, tr, entity, linetypeCache);
                        if (look.Marks.Count > 0)
                        {
                            var gateLayer = LabelLayerResolver.Resolve(entity.Layer, candidates,
                                layerCatalog, ls.DefaultLabelLayer).Layer;
                            report.GateLabels += PlaceSpanLabels(db, tr, null, curve, spanMarkers, ls,
                                                                 textHeight, styleId, CadUtil.EnsureLayer(db, tr, gateLayer),
                                                                 gateLayer, candidates[0].Code, cfg.Version,
                                                                 noteIndex, null);
                            report.ToLegend(look.Name, look.Marks);
                            continue;
                        }
                    }

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
                    if (path.Length < options.MinLength) { report.TooShort++; continue; }

                    // One resolver for the bulk pass, the interactive command and the
                    // review preview: rule's explicit layer, else the office-standard
                    // text layer that actually exists, else the configured default.
                    var layer = LabelLayerResolver.Resolve(entity.Layer, candidates,
                        layerCatalog, ls.DefaultLabelLayer).Layer;

                    var lineStyle = styler.For(new FieldCodes.Styles.StyleSubject
                    {
                        Kind = FieldCodes.Styles.StyledKind.LineLabel,
                        Category = FieldCodes.Settings.AnnotationCategory.Lines,
                        Code = candidates[0].Code,
                        BaseLayer = layer
                    });
                    if (!lineStyle.Enabled) { turnedOff++; continue; }
                    if (!string.IsNullOrWhiteSpace(lineStyle.Layer)) layer = lineStyle.Layer;
                    var layerId = CadUtil.EnsureLayer(db, tr, layer);

                    var planned = LineLabelPlanner.Plan(path, options);
                    foreach (var plan in planned)
                    {
                        var text = heights == null || heights.Count == 0 ? labelText
                            : LineHeights.Compose(ls.HeightFormat, labelText, LineHeights.At(heights, plan.Distance), ls.HeightDecimals);
                        PlaceStyledLineLabel(db, tr, styler, lineStyle, plan, text,
                                             layerId, candidates[0].Code, cfg.Version);
                        report.LabelsPlaced++;
                    }

                    if (planned.Count > 0) report.FeaturesLabelled++;

                    // Gates: two shots noted with a span marker (FGP) along the line get a
                    // GATE label between them, as FTFLABELLINE places. Stamped as this pass's
                    // own output, so the next run regenerates rather than duplicates them.
                    report.GateLabels += PlaceSpanLabels(db, tr, null, curve, spanMarkers, ls,
                                                         textHeight, styleId, layerId, layer,
                                                         candidates[0].Code, cfg.Version,
                                                         noteIndex, null);
                }

                // Gates: posts paired into gates and labelled between them, after every fence
                // is known. Unsure pairs go to the review list.
                var gates = RunGatePass(db, tr, ed, settings, cfg, styler, noteIndex, ObjectId.Null);
                report.GateLabels += gates.Placed;

                // Fence heights that could not be read, lines identified but not labelled: on the
                // drawing's review list (FTFREVIEW) until a drafter deals with them.
                ReviewStore.Report(db, tr, FieldCodes.Review.ReviewChecks.FenceHeightsSource,
                                   HeightReviewItems(noteIndex, heightIssues), true, settings.Review);
                ReviewStore.Report(db, tr, FieldCodes.Review.ReviewChecks.LineLabelsSource,
                                   lineItems.Values, true, settings.Review);

                foreach (var line in report.Lines(ls.MinLengthFeet))
                    ed.WriteMessage("\nFTFLINELABELS: " + line);
                WriteGateOutcome(ed, "FTFLINELABELS", gates);
                if (turnedOff > 0)
                    ed.WriteMessage("\nFTFLINELABELS: {0} line(s) not labelled: their label is turned off " +
                                    "(Settings > Style Assignments).", turnedOff);
                ed.WriteMessage("\n");
                styler.WriteProblems(ed, "FTFLINELABELS");
                LastReport = report;

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
                                                 SpanMarkersWithoutGates(cfg.SpanMarkers, settings.Gates)))
                    {
                        placedCount++;
                        // The gates in this fence ride along, labelled between their posts.
                        if (settings.Gates.Enabled)
                        {
                            var styler = new AnnotationStyler(db, tr, settings, cfg);
                            var gates = RunGatePass(db, tr, ed, settings, cfg, styler, null, picked.ObjectId);
                            if (gates.Placed + gates.Unchanged + gates.KeptByHand > 0)
                                ed.WriteMessage("\n{0} gate label(s) on this fence ({1} new).",
                                                gates.Placed + gates.Unchanged + gates.KeptByHand, gates.Placed);
                        }
                    }
                    tr.Commit();
                }
            }

            ed.WriteMessage("\nFTFLABELLINE: {0} label(s) placed.\n", placedCount);
        }

        /// <summary>
        /// What should it say? Asked only where the rules have no answer: a line on a layer
        /// nobody has configured, or a feature configured without wording. The drafter
        /// deciding is not FTF guessing -- and it is better than a command that stops dead at
        /// a crosswalk because nobody has written down what a crosswalk is called.
        ///
        /// Nothing is remembered: the wording goes on this label, and the next line asks
        /// again. Wording that gets typed every day belongs in the rules, where everyone gets
        /// it.
        /// </summary>
        private static string AskForWording(Editor ed)
        {
            var options = new PromptStringOptions("\nWhat should it say? (Enter to skip): ");
            options.AllowSpaces = true;
            var typed = ed.GetString(options);
            if (typed.Status != PromptStatus.OK) return null;
            var text = (typed.StringResult ?? string.Empty).Trim();
            return text.Length == 0 ? null : text;
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

            string typedWording = null;
            if (candidates == null)
            {
                ed.WriteMessage(
                    "\nNot a configured line feature. Found: layer \"{0}\"{1}{2}{3}.",
                    entity.Layer,
                    string.IsNullOrEmpty(figureName) ? "" : ", figure \"" + figureName + "\"",
                    string.IsNullOrEmpty(trimbleName) ? "" : ", Trimble name \"" + trimbleName + "\"",
                    notes != null && notes.SampleNotes.Count > 0
                        ? ", point notes " + string.Join(" / ", notes.SampleNotes.ToArray())
                        : "");

                typedWording = AskForWording(ed);
                if (typedWording == null)
                {
                    ed.WriteMessage("\nNothing was placed.");
                    return false;
                }
                candidates = new List<LineFeatureRule>();
            }

            var labelText = typedWording ?? LineworkCatalog.UnanimousLabel(candidates);
            if (labelText == null)
            {
                // A layer two features share (FNC and FHW on V-SURF-FENC-E), both labelled
                // but differently, and nothing else to tell them apart: ask. The person
                // decides; FTF still never chooses.
                var choices = LineworkCatalog.DisagreeingChoices(candidates);
                if (choices.Count > 0)
                {
                    var chosen = AskWhichFeature(ed, entity.Layer, how, choices);
                    if (chosen == null)
                    {
                        ed.WriteMessage("\nNothing was placed.");
                        return false;
                    }
                    candidates = new List<LineFeatureRule> { chosen };
                    labelText = chosen.Label;
                    how = "your choice";
                }
            }
            if (labelText == null)
            {
                var descriptions = string.Join(", ", candidates
                    .Select(c => c.Code + (string.IsNullOrEmpty(c.Label)
                        ? " (no label configured)" : " -> \"" + c.Label + "\""))
                    .ToArray());
                ed.WriteMessage(
                    "\nIdentified, but no single labelling standard applies: {0}. " +
                    "FTF will not choose one silently.", descriptions);

                typedWording = AskForWording(ed);
                if (typedWording == null)
                {
                    ed.WriteMessage("\nNothing was placed.");
                    return false;
                }
                labelText = typedWording;
            }

            // Placed as asked, with a word: the automatic pass leaves lines like this to the legend.
            var linetype = LinetypeOf(db, tr, entity, null);
            if (linetype.Marks.Count > 0 && !LineworkCatalog.LabelsOverMarkedLinetype(candidates))
                ed.WriteMessage("\nIts linetype {0} draws its own letter or symbol; the automatic " +
                                "pass leaves lines like this to the legend. Placing the label you asked for.",
                                LinetypeMarks.Describe(linetype.Name, linetype.Marks));

            var featureName = candidates.Count == 0
                ? "typed by hand"
                : string.Join(" / ", candidates
                    .Select(c => string.IsNullOrEmpty(c.Name) ? c.Code : c.Name)
                    .Distinct().ToArray());
            var featureCode = candidates.Count == 0 ? string.Empty : candidates[0].Code;
            if (typedWording != null) how = how ?? "typed";

            // The label layer: the rule's explicit choice, else the office-standard
            // text layer that actually exists in this drawing, else the default --
            // resolved by the same service the bulk pass and the review use.
            var layerCatalog = FtfLineworkService.LayerNames(db, tr);
            var resolution = LabelLayerResolver.Resolve(entity.Layer, candidates,
                                                        layerCatalog, ls.DefaultLabelLayer);

            var wordings = typedWording != null
                ? new List<string> { typedWording }
                : LineworkCatalog.LabelChoices(candidates);

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

                // A fence with heights on its shots ("FNC B 3.5"): the label says the height
                // in force where the cursor is, carried from the last shot that recorded one.
                if (typedWording == null && candidates.Count > 0 && !string.IsNullOrWhiteSpace(ls.HeightFormat) &&
                    LineHeights.Applies(candidates[0]))
                {
                    var heightProblems = new List<string>();
                    var heights = RecordedHeights(curve, candidates, BuildNoteIndex(db, tr), ls.HeightMaxFeet, heightProblems);
                    foreach (var problem in heightProblems) ed.WriteMessage("\nCheck: {0}.", problem);
                    if (heights.Count > 0)
                    {
                        ed.WriteMessage("\nFence height(s) recorded along this line: {0}.",
                                        string.Join(", ", heights.OrderBy(h => h.Station)
                                                                  .Select(h => h.Written + "'").Distinct().ToArray()));
                        jig.TextAt = (wording, station) => LineHeights.Compose(ls.HeightFormat, wording,
                            LineHeights.At(heights, station), ls.HeightDecimals);
                    }
                }
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
                labelText = jig.CurrentText ?? jig.CurrentWording;

                var layerName = resolution.Layer;
                var layerId = CadUtil.EnsureLayer(db, tr, layerName);
                Ownership.EnsureRegApp(db, tr);

                PlaceLineLabel(db, tr, jig.Plan, labelText, textHeight, styleId,
                               layerId, layerName, ls.DrawMask, featureCode,
                               rulesVersion, ManualLineLabelMark);

                ed.WriteMessage("\nPlaced \"{0}\" on layer {1}, {2}.",
                                labelText, layerName,
                                LineSideParser.Describe(jig.Side, ls.SideOffsetFeet));

                // Span markers ride along: two fence shots noted GATE get their
                // GATE label centred between them, no extra clicks.
                PlaceSpanLabels(db, tr, ed, curve, spanMarkers, ls, textHeight,
                                styleId, layerId, layerName, featureCode,
                                rulesVersion);
                return true;
            }
        }

        /// <summary>
        /// Labels the space BETWEEN two edges: a driveway between its two
        /// edge-of-asphalt lines, a path between its edges -- or two parts of ONE line,
        /// such as a hedge drawn as a single U-shaped 3D polyline, picked on its two
        /// sides. Pick both edges, slide the preview -- it starts centred between the
        /// picked places and stays centred, following the sides' averaged direction,
        /// measured in plan -- and click to place. Wording switches alternates, exactly
        /// like FTFLABELLINE; T types your own. Both edges stay read-only; the label
        /// carries the manual mark so the bulk pass never touches it.
        /// </summary>
        [CommandMethod("FTFLABELBETWEEN", CommandFlags.Modal)]
        [CommandMethod("FTFB", CommandFlags.Modal)]
        public void FtfLabelBetween()
        {
            RunBetween("FTFLABELBETWEEN", false);
        }

        /// <summary>
        /// FTFLABELBETWEEN for a driveway: pick its two edges exactly the same way, and the
        /// label is the first edge's surface type read from its layer with DWY after it --
        /// an edge on V-SURF-ASPH-E labels "ASPH DWY", on V-SURF-CONC-E "CONC DWY". The
        /// wording is Settings > Line Labels > Driveway wording ("{type} DWY"); W switches
        /// to the spelled-out word when the feature has one (ASPHALT DWY). The label layer
        /// is the same one FTFLABELBETWEEN would use. Works on any surface layer, configured
        /// line feature or not.
        /// </summary>
        [CommandMethod("FTFLABELDRIVEWAY", CommandFlags.Modal)]
        [CommandMethod("FTFDWY", CommandFlags.Modal)]
        public void FtfLabelDriveway()
        {
            RunBetween("FTFLABELDRIVEWAY", true);
        }

        private static void RunBetween(string commandName, bool driveway)
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
                if (first == null) break;

                // The same line again is fine: a hedge drawn as one U-shaped 3D polyline is
                // labelled between its two legs. WHERE each pick lands names the side.
                var second = PickEdge(ed, "\nSelect the second edge (or the other side of the same line): ");
                if (second == null) break;

                using (doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    if (PlaceOneBetweenLabel(doc, tr, first, second, catalog, ls,
                                             cfg.Version, driveway))
                        placedCount++;
                    tr.Commit();
                }
            }

            ed.WriteMessage("\n{0}: {1} label(s) placed.\n", commandName, placedCount);
        }

        /// <summary>A line's effective linetype and the letters or symbols it draws.</summary>
        private sealed class LinetypeLook
        {
            public string Name;
            public IList<string> Marks = new List<string>();
        }

        /// <summary>
        /// The linetype a line is drawn with -- its own, or its layer's when it is ByLayer --
        /// and the text and shape elements of that linetype's definition: "W" for water
        /// ---W---, "symbol" for a shape such as a fence's x. Nothing for a plain dash pattern.
        /// A survey figure styled with a lettered linetype of its own (rather than by layer) is
        /// not seen here.
        /// </summary>
        private static LinetypeLook LinetypeOf(Database db, Transaction tr, AcEntity entity,
                                               Dictionary<ObjectId, LinetypeLook> cache)
        {
            var none = new LinetypeLook();
            ObjectId id;
            try
            {
                id = entity.LinetypeId;
                if (id.IsNull || id == db.ByLayerLinetype)
                {
                    var layer = tr.GetObject(entity.LayerId, OpenMode.ForRead) as LayerTableRecord;
                    id = layer != null ? layer.LinetypeObjectId : ObjectId.Null;
                }
                else if (id == db.ByBlockLinetype)
                {
                    return none;   // in model space ByBlock draws continuous
                }
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                return none;
            }
            if (id.IsNull) return none;

            LinetypeLook look;
            if (cache != null && cache.TryGetValue(id, out look)) return look;

            look = new LinetypeLook();
            try
            {
                var record = tr.GetObject(id, OpenMode.ForRead) as LinetypeTableRecord;
                if (record != null)
                {
                    look.Name = record.Name;
                    for (var i = 0; i < record.NumDashes; i++)
                    {
                        // A text or shape element carries the style it is drawn with; a plain
                        // dash or gap carries none.
                        if (record.ShapeStyleAt(i).IsNull) continue;
                        var text = record.TextAt(i);
                        look.Marks.Add(string.IsNullOrWhiteSpace(text) ? LinetypeMarks.Symbol : text.Trim());
                    }
                }
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                look = new LinetypeLook();
            }
            if (cache != null) cache[id] = look;
            return look;
        }

        /// <summary>Where every hand-placed line label sits, keyed by the feature code it was
        /// stamped with.</summary>
        private static Dictionary<string, List<Point3d>> ManualLabelPositions(Database db, Transaction tr)
        {
            var byCode = new Dictionary<string, List<Point3d>>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in Ownership.FindOwned(db, tr, s => s.Kind == FtfEntityKind.LineLabel &&
                                                             string.Equals(s.TagText, ManualLineLabelMark, StringComparison.Ordinal)))
            {
                var entity = tr.GetObject(pair.Key, OpenMode.ForRead, false, true) as AcEntity;
                if (entity == null || string.IsNullOrEmpty(pair.Value.PointNumber)) continue;
                List<Point3d> list;
                if (!byCode.TryGetValue(pair.Value.PointNumber, out list))
                {
                    list = new List<Point3d>();
                    byCode[pair.Value.PointNumber] = list;
                }
                list.Add(CadUtil.Flatten(CadUtil.PositionOf(entity)));
            }
            return byCode;
        }

        /// <summary>True when a hand-placed label of one of these features sits within reach
        /// of the line, measured in plan (a sloping 3D polyline is measured straight down).</summary>
        private static bool HasManualLabel(Curve curve, IList<LineFeatureRule> candidates,
                                           Dictionary<string, List<Point3d>> manual, double reach)
        {
            if (manual.Count == 0) return false;
            foreach (var candidate in candidates)
            {
                List<Point3d> positions;
                if (candidate == null || !manual.TryGetValue(candidate.Code, out positions)) continue;
                foreach (var p in positions)
                {
                    Point3d closest;
                    try { closest = curve.GetClosestPointTo(p, Vector3d.ZAxis, false); }
                    catch (Autodesk.AutoCAD.Runtime.Exception)
                    {
                        try { closest = curve.GetClosestPointTo(p, false); }
                        catch (Autodesk.AutoCAD.Runtime.Exception) { continue; }
                    }
                    var dx = closest.X - p.X;
                    var dy = closest.Y - p.Y;
                    if (Math.Sqrt(dx * dx + dy * dy) <= reach) return true;
                }
            }
            return false;
        }

        /// <summary>Asks which of several features sharing a layer this line is. Null when
        /// the person cancels.</summary>
        private static LineFeatureRule AskWhichFeature(Editor ed, string layer, string how,
                                                       IList<LineFeatureRule> choices)
        {
            ed.WriteMessage("\nThis line's {0} ({1}) is used by more than one feature:", how ?? "layer", layer);
            foreach (var c in choices)
                ed.WriteMessage("\n  {0}  {1} -> \"{2}\"", c.Code,
                    string.IsNullOrEmpty(c.Name) ? c.Code : c.Name, c.Label);

            // Keywords are the feature codes: letters and digits, no spaces.
            var codes = choices.Select(c => c.Code.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var options = new PromptKeywordOptions(
                "\nWhich is it? [" + string.Join("/", codes.ToArray()) + "] <cancel>: ",
                string.Join(" ", codes.ToArray()))
            {
                AllowNone = true
            };
            var answer = ed.GetKeywords(options);
            if (answer.Status != PromptStatus.OK || string.IsNullOrEmpty(answer.StringResult)) return null;
            return choices.FirstOrDefault(c => string.Equals(c.Code.Trim(), answer.StringResult, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>One FTFLABELBETWEEN pick: the line, and WHERE on it, in world coordinates.
        /// The place matters as much as the line -- two picks on one U-shaped hedge name its
        /// two legs.</summary>
        private sealed class EdgePick
        {
            public ObjectId Id;
            public Point3d At;
        }

        /// <summary>Null when the person finishes (Enter or Esc).</summary>
        private static EdgePick PickEdge(Editor ed, string message)
        {
            var options = new PromptEntityOptions(message) { AllowNone = true };
            var picked = ed.GetEntity(options);
            if (picked.Status != PromptStatus.OK) return null;

            // The pick point arrives in the current UCS; the lines are read in world coordinates.
            return new EdgePick
            {
                Id = picked.ObjectId,
                At = picked.PickedPoint.TransformBy(ed.CurrentUserCoordinateSystem)
            };
        }

        /// <summary>What kind of line a pick landed on, for the command's messages.</summary>
        private static string BetweenKindOf(Curve curve)
        {
            if (curve is Autodesk.Civil.DatabaseServices.SurveyFigure) return "survey figure";
            if (curve is Autodesk.Civil.DatabaseServices.FeatureLine) return "feature line";
            if (curve is Polyline3d) return "3D polyline";
            if (curve is Polyline || curve is Polyline2d) return "polyline";
            if (curve is Arc) return "arc";
            if (curve is Autodesk.AutoCAD.DatabaseServices.Line) return "line";
            return curve.GetType().Name.ToLowerInvariant();
        }

        /// <summary>
        /// A source line's trace in plan, for FTFLABELBETWEEN: every vertex exactly, arcs and
        /// bulges filled in with short chords, Z handed over but never used. Read through the
        /// distance queries every Curve answers -- a 2D polyline, a 3D polyline at any elevation,
        /// a survey figure or feature line (its vertices from GetPoints, as everywhere else in
        /// FTF). The line is only read: nothing is exploded, converted or written.
        /// </summary>
        private static PlanPath BetweenPathOf(Curve curve)
        {
            const int arcPieces = 24;
            var points = new List<PlanPoint>();

            double startDistance, endDistance;
            try
            {
                startDistance = curve.GetDistanceAtParameter(curve.StartParam);
                endDistance = curve.GetDistanceAtParameter(curve.EndParam);
            }
            catch (System.Exception)
            {
                startDistance = endDistance = 0.0;
            }

            if (endDistance > startDistance)
            {
                // The corners: a polyline's vertex at every whole parameter, a figure's every
                // point, otherwise the two ends.
                var stations = new List<double> { startDistance, endDistance };
                if (curve is Polyline || curve is Polyline2d || curve is Polyline3d)
                {
                    for (var p = Math.Ceiling(curve.StartParam); p < curve.EndParam; p += 1.0)
                    {
                        try { stations.Add(curve.GetDistanceAtParameter(p)); }
                        catch (Autodesk.AutoCAD.Runtime.Exception) { }
                    }
                }
                else
                {
                    foreach (var vertex in VerticesOf(curve))
                    {
                        // The vertex is ON the line in 3D, so its nearest point is itself.
                        try { stations.Add(curve.GetDistAtPoint(curve.GetClosestPointTo(vertex, false))); }
                        catch (System.Exception) { }
                    }
                }
                stations.Sort();

                Func<double, Point3d?> at = d =>
                {
                    try { return curve.GetPointAtDist(Math.Max(startDistance, Math.Min(endDistance, d))); }
                    catch (System.Exception) { return null; }
                };

                Point3d? previous = null;
                var previousStation = double.NegativeInfinity;
                foreach (var station in stations)
                {
                    if (station - previousStation < 1e-9) continue;
                    var here = at(station);
                    if (here == null) continue;

                    // A segment whose middle is off its chord in plan is an arc (a bulge, a
                    // figure's curve, a spline-fit polyline): fill it in.
                    if (previous != null)
                    {
                        var mid = at((previousStation + station) / 2.0);
                        if (mid != null)
                        {
                            var chordX = (previous.Value.X + here.Value.X) / 2.0;
                            var chordY = (previous.Value.Y + here.Value.Y) / 2.0;
                            var off = Math.Sqrt((mid.Value.X - chordX) * (mid.Value.X - chordX) +
                                                (mid.Value.Y - chordY) * (mid.Value.Y - chordY));
                            if (off > Math.Max(1e-6, (station - previousStation) * 1e-4))
                            {
                                for (var k = 1; k < arcPieces; k++)
                                {
                                    var inner = at(previousStation + (station - previousStation) * k / arcPieces);
                                    if (inner != null)
                                        points.Add(new PlanPoint(inner.Value.X, inner.Value.Y, inner.Value.Z));
                                }
                            }
                        }
                    }

                    points.Add(new PlanPoint(here.Value.X, here.Value.Y, here.Value.Z));
                    previous = here;
                    previousStation = station;
                }
            }

            // A line that will not answer distance queries: its corners, joined straight.
            if (points.Count < 2)
            {
                points.Clear();
                foreach (var vertex in VerticesOf(curve))
                    points.Add(new PlanPoint(vertex.X, vertex.Y, vertex.Z));
            }

            return new PlanPath(points);
        }

        private static bool PlaceOneBetweenLabel(
            Autodesk.AutoCAD.ApplicationServices.Document doc, Transaction tr,
            EdgePick first, EdgePick second, LineworkCatalog catalog,
            FieldCodes.Settings.LineLabelSettings ls, string rulesVersion, bool driveway)
        {
            var ed = doc.Editor;
            var db = doc.Database;

            // FOR READ, both of them, and never upgraded: the edges are only measured. Two
            // picks on the same line are two places on one object, opened once.
            var oneLine = first.Id == second.Id;
            var curveA = tr.GetObject(first.Id, OpenMode.ForRead, false, true) as Curve;
            var curveB = oneLine
                ? curveA
                : tr.GetObject(second.Id, OpenMode.ForRead, false, true) as Curve;
            if (curveA == null || curveB == null)
            {
                ed.WriteMessage("\nBoth picks must be lines, polylines, 3D polylines, arcs or " +
                                "figures.");
                return false;
            }
            if (Ownership.Read(curveA) != null || Ownership.Read(curveB) != null)
            {
                ed.WriteMessage("\nOne of those is FTF's own output. Pick the source " +
                                "edges themselves.");
                return false;
            }

            // Where the picks landed, in plan, and the run of linework around each: the two
            // sides the label goes between. Refused before anything is asked.
            var pathA = BetweenPathOf(curveA);
            var sides = oneLine
                ? LabelBetween.OneLine(pathA, first.At.X, first.At.Y, second.At.X, second.At.Y)
                : LabelBetween.TwoLines(pathA, first.At.X, first.At.Y,
                                        BetweenPathOf(curveB), second.At.X, second.At.Y);
            if (sides.Problem != null)
            {
                ed.WriteMessage("\n{0} Nothing was placed.", sides.Problem);
                return false;
            }

            // The FIRST edge picked decides everything -- identity, wording, layer,
            // text settings. The second edge only says where the far side is, so a
            // driveway between an asphalt edge and a curb labels as the asphalt.
            string how;
            var candidates = StagedCandidates(db, tr, curveA, catalog, out how);
            IList<string> wordings;
            if (driveway)
            {
                // A driveway is labelled by its surface type, read from the first edge's
                // layer; the edge need not be a configured feature.
                wordings = FieldCodes.Linework.DrivewayWording.Choices(curveA.Layer, candidates, ls.DrivewayFormat);
                if (wordings.Count == 0)
                {
                    ed.WriteMessage(
                        "\nNo surface type can be read from the first edge's layer \"{0}\" " +
                        "(expected a name like V-SURF-ASPH-E). Nothing was placed.", curveA.Layer);
                    return false;
                }
                if (candidates == null)
                {
                    candidates = new List<LineFeatureRule>();
                    how = "layer name";
                }
            }
            else
            {
                // Between two edges the label names the SURFACE -- ASPHALT, not EDGE OF
                // PAVEMENT -- from the feature's configured between-wordings.
                wordings = candidates == null
                    ? new List<string>()
                    : LineworkCatalog.BetweenChoices(candidates);
                if (wordings.Count == 0)
                {
                    // Nothing configured says what this is (a hedge on a layer nobody has
                    // set up, a feature with no wording): the drafter says it, as FTFLABELLINE
                    // asks. Nothing is remembered.
                    if (candidates == null)
                        ed.WriteMessage("\nThe first edge is not a configured line feature " +
                                        "(layer \"{0}\").", curveA.Layer);
                    else
                        ed.WriteMessage("\nIdentified as {0}, but no single labelling standard " +
                                        "applies to it.", candidates[0].Code);

                    var typed = AskForWording(ed);
                    if (typed == null)
                    {
                        ed.WriteMessage("\nNothing was placed.");
                        return false;
                    }
                    wordings = new List<string> { typed };
                    if (candidates == null)
                    {
                        candidates = new List<LineFeatureRule>();
                        how = "typed";
                    }
                }
            }
            var labelText = wordings[0];
            var sourceCode = candidates.Count > 0 ? candidates[0].Code : driveway ? "DWY" : string.Empty;

            var layerCatalog = FtfLineworkService.LayerNames(db, tr);
            var resolution = LabelLayerResolver.Resolve(curveA.Layer, candidates,
                                                        layerCatalog, ls.DefaultLabelLayer);

            var featureName = candidates.Count > 0
                ? string.Join(" / ", candidates
                    .Select(c => string.IsNullOrEmpty(c.Name) ? c.Code : c.Name)
                    .Distinct().ToArray())
                : curveA.Layer;
            ed.WriteMessage("\nFeature: {0} (first edge, by {1})", featureName, how);
            if (oneLine)
                ed.WriteMessage("\nBetween: two parts of one {0} on {1}", BetweenKindOf(curveA), curveA.Layer);
            else
                ed.WriteMessage("\nBetween: {0} on {1} and {2} on {3}", BetweenKindOf(curveA), curveA.Layer,
                                BetweenKindOf(curveB), curveB.Layer);
            ed.WriteMessage("\nLabel layer: {0}", resolution.Describe());
            ed.WriteMessage("\nLabel: {0}{1}  (type T to type your own)", labelText,
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

                // The preview starts centred between the picked parts and follows the cursor
                // along the space between them.
                var jig = new LineBetweenJig(preview, sides, ls.AlignToLine, wordings);
                var dragged = ed.Drag(jig);
                while (dragged.Status == PromptStatus.Keyword)
                {
                    if (jig.TypingRequested)
                    {
                        jig.TypingRequested = false;
                        var typed = AskForWording(ed);
                        if (typed != null) jig.UseTypedWording(typed);
                    }
                    dragged = ed.Drag(jig);
                }

                if (dragged.Status != PromptStatus.OK || jig.Plan == null)
                {
                    ed.WriteMessage("\nCancelled; nothing was placed.");
                    return false;
                }

                var layerId = CadUtil.EnsureLayer(db, tr, resolution.Layer);
                Ownership.EnsureRegApp(db, tr);

                // A new MTEXT (and its mask) only; both edges were only ever read.
                PlaceLineLabel(db, tr, jig.Plan, jig.CurrentWording, textHeight,
                               styleId, layerId, resolution.Layer, ls.DrawMask,
                               sourceCode, rulesVersion, ManualLineLabelMark);

                ed.WriteMessage("\nPlaced \"{0}\" on layer {1}, centred between {2}.",
                                jig.CurrentWording, resolution.Layer,
                                oneLine ? "the two picked parts of the line" : "the edges");
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
            out string how, SourcePointIndex noteIndex = null)
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

            var notes = PointNotes.Consensus(NotesAtVertices(db, tr, curve, noteIndex), catalog);
            if (notes.Features.Count > 0) { how = "point notes"; return notes.Features; }

            var byLayer = catalog.FindByLayer(curve.Layer);
            if (byLayer.Count > 0) { how = "layer"; return byLayer; }

            how = null;
            return null;
        }

        /// <summary>
        /// The between-edges preview: for each cursor sample, the nearest point IN PLAN on
        /// each picked side and the direction there; the label sits at their midpoint,
        /// following the averaged direction (LabelBetween.Place, the same calculation the
        /// start position uses). It works on the sides' plan traces, read once, so the
        /// lines' elevations never pull the label about and the lines themselves are never
        /// touched.
        /// </summary>
        private sealed class LineBetweenJig : EntityJig
        {
            private const string TypeKeyword = "Text";

            private readonly BetweenSides _sides;
            private readonly bool _alignToLine;
            private readonly List<string> _wordings;
            private int _wordingIndex;
            private Point3d _cursor;

            public PlannedLineLabel Plan { get; private set; }

            /// <summary>The person typed T: the command asks for the wording, then drags on.</summary>
            public bool TypingRequested { get; set; }

            public string CurrentWording
            {
                get { return _wordings[_wordingIndex % _wordings.Count]; }
            }

            public LineBetweenJig(MText preview, BetweenSides sides, bool alignToLine,
                                  IList<string> wordings)
                : base(preview)
            {
                _sides = sides;
                _alignToLine = alignToLine;
                _wordings = wordings != null && wordings.Count > 0
                    ? new List<string>(wordings)
                    : new List<string> { preview.Contents };
                _cursor = new Point3d(double.NaN, double.NaN, 0.0);

                // Centred between the picked parts before the cursor moves at all.
                Plan = LabelBetween.Start(sides, alignToLine);
                preview.Location = new Point3d(Plan.X, Plan.Y, 0.0);
                preview.Rotation = Plan.RotationRadians;
            }

            /// <summary>Puts typed wording on the cursor; W still cycles the configured ones.</summary>
            public void UseTypedWording(string text)
            {
                _wordings.RemoveAll(w => string.Equals(w, text, StringComparison.OrdinalIgnoreCase));
                _wordings.Insert(0, text);
                _wordingIndex = 0;
            }

            protected override SamplerStatus Sampler(JigPrompts prompts)
            {
                var options = new JigPromptPointOptions(
                    "\nPosition the label between the edges -- \"" + CurrentWording +
                    "\" (click to place): ");
                options.UserInputControls = UserInputControls.Accept3dCoordinates;
                if (_wordings.Count > 1) options.Keywords.Add("Wording");
                options.Keywords.Add(TypeKeyword);

                var result = prompts.AcquirePoint(options);

                if (result.Status == PromptStatus.Keyword)
                {
                    if (string.Equals(result.StringResult, TypeKeyword, StringComparison.OrdinalIgnoreCase))
                        TypingRequested = true;
                    else
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
                // Before the first cursor sample (a keyword typed straight away) the label
                // stays where it started, centred between the picks.
                if (!double.IsNaN(_cursor.X))
                    Plan = LabelBetween.Place(_sides, _cursor.X, _cursor.Y, _alignToLine);

                var text = (MText)Entity;
                if (!string.Equals(text.Contents, CurrentWording, StringComparison.Ordinal))
                    text.Contents = CurrentWording;
                text.Location = new Point3d(Plan.X, Plan.Y, 0.0);
                text.Rotation = Plan.RotationRadians;
                return true;
            }
        }

        /// <summary>
        /// Raw descriptions of every CogoPoint that coincides with a vertex of the line (within the
        /// source index's tolerance, 0.01 units, neighbouring cells included). TBC draws linework
        /// through the shot points, so a coincident point created that vertex and its field note names
        /// the line. Near-misses beyond the tolerance are deliberately not matched -- coincidence is
        /// evidence, proximity would be guessing. Read-only throughout.
        /// </summary>
        private static IList<string> NotesAtVertices(Database db, Transaction tr,
                                                     Curve curve,
                                                     SourcePointIndex index = null)
        {
            var notes = new List<string>();

            var sources = index ?? BuildNoteIndex(db, tr);
            if (sources.Count == 0) return notes;

            foreach (var vertex in VerticesOf(curve))
                notes.AddRange(sources.DescriptionsAt(vertex.X, vertex.Y));

            return notes;
        }

        /// <summary>Every survey point -- number, handle, raw description, location -- indexed by
        /// position, so vertices can look their creating shots up.</summary>
        private static SourcePointIndex BuildNoteIndex(Database db, Transaction tr)
        {
            // The crew's notes too (Settings > Point Labels > Read notes from), for fence heights
            // written in the field notes rather than the description.
            CogoNotes notes = null;
            try { notes = CogoNotes.For(db, tr, FtfSession.SettingsFor(db, FtfSession.Rules(db))); }
            catch (ConfigException) { notes = null; }
            return SourcePointReader.Read(db, tr, notes);
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
                                           string sourceCode, string rulesVersion,
                                           SourcePointIndex noteIndex = null,
                                           string mark = ManualLineLabelMark)
        {
            if (markers == null || markers.Count == 0) return 0;

            var index = noteIndex ?? BuildNoteIndex(db, tr);
            if (index.Count == 0) return 0;

            var placedTotal = 0;

            foreach (var marker in markers)
            {
                if (marker == null || !marker.Enabled ||
                    string.IsNullOrWhiteSpace(marker.Token)) continue;

                var distances = new List<double>();
                foreach (var vertex in VerticesOf(curve))
                {
                    var notes = index.DescriptionsAt(vertex.X, vertex.Y);
                    if (notes.Count == 0) continue;
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
                                   mark);
                    placedTotal++;
                }

                if (ed != null && (spans.Count > 0 || leftover))
                    ed.WriteMessage("\n{0} {1} label(s) placed between marked points{2}.",
                        spans.Count, marker.Token,
                        leftover ? " -- one marked point had no partner and was " +
                                   "left unlabelled" : "");
            }

            return placedTotal;
        }

        /// <summary>A fence height that could not be read, where and why.</summary>
        private sealed class HeightIssue
        {
            public Point3d At;
            public string Description;
            public string Problem;
        }

        /// <summary>Review items for unreadable fence heights, each tied to its shot (point number and
        /// handle from the shared source index).</summary>
        private static IList<FieldCodes.Review.ReviewItem> HeightReviewItems(SourcePointIndex sources, IList<HeightIssue> issues)
        {
            var items = new List<FieldCodes.Review.ReviewItem>();
            if (issues == null || issues.Count == 0) return items;
            foreach (var issue in issues)
            {
                var here = sources == null ? new List<SourcePoint>() : sources.At(issue.At.X, issue.At.Y);
                var shot = here.FirstOrDefault(p => p.RawDescription == issue.Description) ?? here.FirstOrDefault();
                items.Add(FieldCodes.Review.ReviewChecks.FenceHeight(
                    shot == null ? null : shot.Number, shot == null ? null : shot.Handle,
                    issue.At.X, issue.At.Y, issue.Problem));
            }
            return items;
        }

        /// <summary>One review item per layer and set of candidate features FTF could not label.</summary>
        private static void UnidentifiedLine(Dictionary<string, FieldCodes.Review.ReviewItem> items, AcEntity entity,
                                             IList<LineFeatureRule> candidates)
        {
            var codes = string.Join("/", candidates.Select(c => c.Code).Distinct().ToArray());
            var key = entity.Layer + ":" + codes;
            FieldCodes.Review.ReviewItem item;
            if (!items.TryGetValue(key, out item))
            {
                var choices = LineworkCatalog.DisagreeingChoices(candidates);
                item = FieldCodes.Review.ReviewKinds.New(FieldCodes.Review.ReviewKinds.LineUnidentified, key,
                    choices.Count > 0
                        ? "lines on " + entity.Layer + " could be " + string.Join(" or ", candidates.Select(c => c.Code + " (" + c.Label + ")").ToArray())
                        : "lines on " + entity.Layer + " are " + codes + ", which has no label configured");
                item.Issue = choices.Count > 0 ? "Line could be more than one feature" : "Line feature has no label";
                item.FeatureType = "Line";
                Extents3d box;
                try
                {
                    box = entity.GeometricExtents;
                    item.X = (box.MinPoint.X + box.MaxPoint.X) / 2;
                    item.Y = (box.MinPoint.Y + box.MaxPoint.Y) / 2;
                }
                catch (Autodesk.AutoCAD.Runtime.Exception) { }
                items[key] = item;
            }
            if (item.Handles.Count < 50) item.Handles.Add(entity.Handle.ToString());
            item.Points = item.Handles.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " line(s)";
        }

        /// <summary>
        /// The heights a line's own shots record, by distance along it: at each vertex, the shot
        /// there whose code is this feature's ("FNC B 3.5" on a fence). A number that cannot be a
        /// height is added to <paramref name="problems"/> and not used.
        /// </summary>
        private static IList<RecordedHeight> RecordedHeights(Curve curve, IList<LineFeatureRule> features,
                                                             SourcePointIndex index, double maxFeet,
                                                             IList<string> problems, ICollection<string> stopCodes = null,
                                                             IList<HeightIssue> issues = null)
        {
            var codes = new HashSet<string>(features.SelectMany(f => new[] { f.Code }.Concat(f.Aliases ?? new List<string>()))
                                                    .Where(c => !string.IsNullOrWhiteSpace(c)), StringComparer.OrdinalIgnoreCase);
            if (stopCodes == null) stopCodes = new FieldCodes.Settings.GateSettings().PostCodes;
            var heights = new List<RecordedHeight>();
            foreach (var vertex in VerticesOf(curve))
            {
                if (index == null) break;
                foreach (var description in index.DescriptionsWithNotesAt(vertex.X, vertex.Y))
                {
                    // The fence's code anywhere in the shot ("FGP FNC B 5" is a gate post on the
                    // fence); the height is the number after it, before a gate post's own code.
                    string written, problem;
                    var feet = LineHeights.Read(description, codes, stopCodes, maxFeet, out written, out problem);
                    if (problem != null)
                    {
                        problems.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture, "fence height at {0:0.00}, {1:0.00}: {2}", vertex.X, vertex.Y, problem));
                        if (issues != null) issues.Add(new HeightIssue { At = vertex, Description = description, Problem = problem });
                        continue;
                    }
                    if (!feet.HasValue) continue;
                    double station;
                    try { station = curve.GetDistAtPoint(vertex); }
                    catch (Autodesk.AutoCAD.Runtime.Exception) { continue; }
                    heights.Add(new RecordedHeight { Station = station, Feet = feet.Value, Written = written, Description = description });
                }
            }
            return heights;
        }


        /// <summary>The line's vertices. Polylines put a vertex at every integer
        /// parameter; anything else contributes its two ends.</summary>
        internal static IEnumerable<Point3d> VerticesOf(Curve curve)
        {
            // A survey figure or feature line: every point it was built from, not only its ends,
            // so a shot part way along it (a new fence height, a gate post) is found.
            var featureLine = curve as Autodesk.Civil.DatabaseServices.FeatureLine;
            if (featureLine != null)
            {
                Point3dCollection points = null;
                try { points = featureLine.GetPoints(Autodesk.Civil.FeatureLinePointType.AllPoints); }
                catch (System.Exception) { points = null; }
                if (points != null && points.Count > 0)
                {
                    foreach (Point3d p in points) yield return p;
                    yield break;
                }
            }

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

            /// <summary>Optional: the text for a wording at a distance along the line (a fence's
            /// height where the cursor is). Null shows the wording as it is.</summary>
            public Func<string, double, string> TextAt { get; set; }

            /// <summary>The text on the cursor now, height included -- what a click places.</summary>
            public string CurrentText { get; private set; }

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
                double x, y, direction, dx, dy, station;
                try
                {
                    // Nearest point on the source geometry and the tangent there --
                    // curves get the local tangent at the placement spot.
                    // Measured in plan (projected along Z): a 3D polyline or survey figure
                    // hundreds of feet up must follow the cursor, not its lowest point.
                    var flat = new Point3d(_cursor.X, _cursor.Y, 0.0);
                    var closest = _curve.GetClosestPointTo(flat, Vector3d.ZAxis, false);
                    var parameter = _curve.GetParameterAtPoint(closest);
                    var derivative = _curve.GetFirstDerivative(parameter);
                    station = _curve.GetDistanceAtParameter(parameter);

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
                Plan.Distance = station;

                CurrentText = TextAt == null ? CurrentWording : TextAt(CurrentWording, station);
                var text = (MText)Entity;
                if (!string.Equals(text.Contents, CurrentText, StringComparison.Ordinal))
                    text.Contents = CurrentText;
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

        /// <summary>
        /// A line label in its annotation style: text style, height, colour, prefix, suffix,
        /// rounding, width and mask from the style; position and angle from the line, as
        /// always. Stamped with the style so FTFRESTYLE can update it later.
        /// </summary>
        private static ObjectId PlaceStyledLineLabel(Database db, Transaction tr, AnnotationStyler styler,
                                                 FieldCodes.Styles.ResolvedAnnotationStyle style,
                                                 PlannedLineLabel plan, string labelText,
                                                 ObjectId layerId, string sourceCode, string rulesVersion,
                                                 string tagText = null)
        {
            var position = new Point3d(plan.X, plan.Y, 0.0);
            var t = style.Text;
            var height = styler.ModelHeight(t);
            var contents = FieldCodes.Styles.StyleText.Compose(labelText, t);

            using (var text = styler.NewText(labelText, t, layerId, position, null))
            {
                text.Rotation = plan.RotationRadians;
                text.Attachment = AttachmentPoint.MiddleCenter;
                text.Location = position;

                if (t.Mask == FieldCodes.Styles.MaskMode.Wipeout)
                    PlaceLineMask(db, tr, text, position, plan.RotationRadians,
                                  height, layerId, sourceCode, rulesVersion, tagText);

                styler.Finish(text, t);
                Ownership.Stamp(text, sourceCode, rulesVersion, FtfEntityKind.LineLabel, position,
                                tagText, AnnotationStyler.StyleKey(style), labelText, contents);
                return text.ObjectId;
            }
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
