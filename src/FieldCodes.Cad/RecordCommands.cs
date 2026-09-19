using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using FieldCodes.Easements;
using FieldCodes.RecordSurvey;
using FieldCodes.Settings;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcDocument = Autodesk.AutoCAD.ApplicationServices.Document;
using AcEntity = Autodesk.AutoCAD.DatabaseServices.Entity;

namespace FieldCodes.Cad
{
    /// <summary>
    /// FTFRECORD and its companions: a recorded plat or Record of Survey in, reviewed calls,
    /// mathematically reconstructed geometry out, in the office standard, with the source of
    /// every line kept on it.
    ///
    ///   FTFRECORD         upload -> read -> extract -> order -> review -> build -> QC
    ///   FTFRECORDCHECK    drawing versus record, flags only
    ///   FTFRECORDLABEL    place or refresh the labels
    ///   FTFRECORDSOURCE   where a line came from
    ///   FTFRECORDREBUILD  reopen the review and rebuild after edits
    ///
    /// The picture never becomes geometry: courses are traversed from the written calls, and
    /// a call the reader is not sure of stops the build until a person has looked at it.
    /// This is a production aid for a licensed surveyor, not a boundary determination.
    ///
    /// UNTESTED against a drawing; everything it computes is unit tested in FieldCodes.
    /// </summary>
    public sealed class RecordCommands
    {
        // ================================================================= FTFRECORD

        [CommandMethod("FTFRECORD", CommandFlags.Modal)]
        public void FtfRecord()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;
            var db = doc.Database;
            var settings = ResolveSettings(db);
            var s = settings.RecordSurvey;

            try
            {
                var path = ChooseDocument(ed, "Recorded survey document");
                if (path == null) return;

                RecordSurveyProject project;
                DocumentText text;
                if (path.EndsWith(".ftfrecord.json", StringComparison.OrdinalIgnoreCase))
                {
                    project = RecordSurveyProject.FromJson(File.ReadAllText(path));
                    text = LoadOcr(project);
                    ed.WriteMessage("\nFTFRECORD: resumed project {0} ({1}).", project.Id, project.Document.Path);
                }
                else
                {
                    ed.WriteMessage("\nFTFRECORD: reading {0} with the {1} engine ...", path, s.OcrEngine);
                    text = RecordDocumentReader.Read(path, s, m => ed.WriteMessage("\n  " + m));
                    foreach (var n in text.Notes) ed.WriteMessage("\n  note: " + n);

                    project = CallExtractor.Extract(text, new ExtractionOptions { ReviewThreshold = s.ReviewThreshold, MaxAlternatives = s.MaxAlternatives });
                    project.Profile = FtfSession.ResolveSettings(db, null).ProfileName;
                    project.BuiltFromMeasured = s.PreferMeasured;
                    if (s.KeepPageImages)
                    {
                        var cache = RecordDocumentReader.CacheFolder(path);
                        Directory.CreateDirectory(cache);
                        project.OcrPath = Path.Combine(cache, Path.GetFileNameWithoutExtension(path) + ".ocr.json");
                        File.WriteAllText(project.OcrPath, text.ToJson());
                    }
                }

                Summarize(ed, project);

                StandardsResolution standards;
                DrawingInventory inventory;
                using (doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    inventory = RecordDrafter.Inventory(db, tr);
                    tr.Commit();
                }
                standards = Resolve(settings, inventory, project);
                ReportStandards(ed, standards);

                var session = new ReviewSession(project, s);
                var dpi = text != null && text.Pages.Count > 0 && text.Pages[0].Dpi > 0 ? text.Pages[0].Dpi : s.OcrDpi;
                if (project.Document.ScaleFeetPerInch.HasValue && project.Calls.All(c => c.Order == 0))
                {
                    var assembly = session.AutoOrder(new AssemblyOptions { ScaleFeetPerInch = project.Document.ScaleFeetPerInch.Value, Dpi = dpi }, false);
                    ed.WriteMessage("\nOrdering from the page at 1\" = {0}': {1} figure(s) proposed, {2} closed.", project.Document.ScaleFeetPerInch.Value,
                                    assembly.Figures.Count, assembly.Figures.Count(f => f.Closed));
                    foreach (var f in assembly.Figures) ed.WriteMessage("\n  {0}: {1} course(s){2}", f.Name, f.CallIds.Count, f.Closed ? ", closed" : " -- " + string.Join(" ", f.Notes.ToArray()));
                    foreach (var pr in assembly.Problems) ed.WriteMessage("\n  " + pr);
                }
                else if (!project.Document.ScaleFeetPerInch.HasValue)
                    ed.WriteMessage("\nNo scale note was read; enter the scale in the review to order the courses from the page, or order them by hand.");

                if (!Review(doc, session, text, standards, inventory, settings)) { ed.WriteMessage("\nFTFRECORD: nothing built.\n"); return; }

                Build(doc, project, session, settings, standards, false);
            }
            catch (ConfigException ex)
            {
                ed.WriteMessage("\nFTFRECORD: {0}\n", ex.Message);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\nFTFRECORD failed: {0}\n{1}\n", ex.Message, ex.StackTrace);
            }
        }

        // ============================================================ FTFRECORDREBUILD

        [CommandMethod("FTFRECORDREBUILD", CommandFlags.Modal)]
        public void FtfRecordRebuild()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;
            var db = doc.Database;
            var settings = ResolveSettings(db);
            try
            {
                var project = PickProject(doc, "FTFRECORDREBUILD");
                if (project == null) return;
                List<string> edits;
                DrawingInventory inventory;
                using (doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    edits = RecordDrafter.HandEdits(db, tr, project);
                    inventory = RecordDrafter.Inventory(db, tr);
                    tr.Commit();
                }
                foreach (var e in edits) ed.WriteMessage("\n  WARNING: " + e);
                if (edits.Count > 0) ed.WriteMessage("\n  A rebuild replaces hand-edited geometry with the record. Cancel the review to keep it.");

                var standards = Resolve(settings, inventory, project);
                ReportStandards(ed, standards);
                var text = LoadOcr(project);
                var session = new ReviewSession(project, settings.RecordSurvey);
                if (!Review(doc, session, text, standards, inventory, settings)) { ed.WriteMessage("\nFTFRECORDREBUILD: nothing changed.\n"); return; }

                project.Revision++;
                Build(doc, project, session, settings, standards, true);
            }
            catch (ConfigException ex) { ed.WriteMessage("\nFTFRECORDREBUILD: {0}\n", ex.Message); }
            catch (System.Exception ex) { ed.WriteMessage("\nFTFRECORDREBUILD failed: {0}\n{1}\n", ex.Message, ex.StackTrace); }
        }

        // ============================================================== FTFRECORDLABEL

        [CommandMethod("FTFRECORDLABEL", CommandFlags.Modal)]
        public void FtfRecordLabel()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;
            var project = PickProject(doc, "FTFRECORDLABEL");
            if (project == null) return;
            FtfSession.Run("FTFRECORDLABEL", (db, tr, e) =>
            {
                var settings = ResolveSettings(db);
                var s = settings.RecordSurvey;
                if (!s.LabelsEnabled) { e.WriteMessage("\nFTFRECORDLABEL: the label mode is None under Settings > Recorded Surveys.\n"); return; }
                var inventory = RecordDrafter.Inventory(db, tr);
                var standards = Resolve(settings, inventory, project);
                ReportStandards(e, standards);

                var kept = RecordDrafter.Erase(db, tr, project, settings, false, true, true);
                if (kept.Count > 0) e.WriteMessage("\n  {0} hand-moved label(s) kept and routed around.", kept.Count);

                var outcome = new RecordBuildOutcome();
                var upf = settings.General.UnitsPerFoot > 0 ? settings.General.UnitsPerFoot : 1.0;
                outcome.Traverses = TraverseBuilder.BuildAll(project, new ReviewSession(project, s).Options(upf, false));
                outcome.Shared = SharedLineMatcher.Match(outcome.Traverses, s.SharedLineToleranceFt * upf, s.DistanceToleranceFt, s.BearingToleranceSeconds);
                var byCall = new Dictionary<string, ObjectId>(StringComparer.OrdinalIgnoreCase);
                foreach (var pair in Ownership.FindOwned(db, tr, st => st.PointNumber == project.Id && (st.Kind == FtfEntityKind.RecordLine || st.Kind == FtfEntityKind.RecordCurve)))
                {
                    byCall[pair.Value.TagText] = pair.Key;
                    var meta = RecordDrafter.ReadMetadata(tr, tr.GetObject(pair.Key, OpenMode.ForRead) as AcEntity);
                    if (meta != null) foreach (var other in meta.SharedWith) byCall[other] = pair.Key;
                }
                RecordDrafter.PlaceLabels(db, tr, e, project, settings, standards, outcome, byCall, kept);
                project.Built.RemoveAll(b => b.Role == "Label" || b.Role == "Mask" || b.Role == "Table");
                project.Built.AddRange(outcome.Built);
                DrawingStore.SaveRecordProject(db, tr, project);
                foreach (var m in outcome.Messages) e.WriteMessage("\n  " + m);
                e.WriteMessage("\nFTFRECORDLABEL: {0} label(s), {1} table(s) placed for {2}.\n", outcome.Labels, outcome.Tables, project.Id);
            });
        }

        // ============================================================= FTFRECORDSOURCE

        [CommandMethod("FTFRECORDSOURCE", CommandFlags.Modal)]
        public void FtfRecordSource()
        {
            List<InspectRow> rows = null;
            string title = null;
            FtfSession.Run("FTFRECORDSOURCE", (db, tr, ed) =>
            {
                var settings = ResolveSettings(db);
                var options = new PromptEntityOptions("\nSelect a reconstructed line, curve, label or monument: ");
                var picked = ed.GetEntity(options);
                if (picked.Status != PromptStatus.OK) return;
                var entity = (AcEntity)tr.GetObject(picked.ObjectId, OpenMode.ForRead);
                var stamp = Ownership.Read(entity);
                if (stamp == null || stamp.Kind < FtfEntityKind.RecordLine || stamp.Kind > FtfEntityKind.RecordText)
                {
                    ed.WriteMessage("\nFTFRECORDSOURCE: that object was not built by FTFRECORD.\n");
                    return;
                }
                var project = DrawingStore.LoadRecordProjects(db, tr).FirstOrDefault(p => p.Id == stamp.PointNumber);
                var meta = RecordDrafter.ReadMetadata(tr, entity);
                rows = SourceRows(entity, stamp, meta, project, settings.RecordSurvey);
                title = "Record source: " + (meta != null && !string.IsNullOrEmpty(meta.CallId) ? meta.CallId : stamp.TagText) + " (" + Path.GetFileName(meta != null ? meta.Document ?? string.Empty : string.Empty) + ")";
                if (EasementInspectCommands.Headless())
                {
                    ed.WriteMessage("\n" + EasementInspectCommands.AsText(title, rows));
                }
            });
            if (rows != null && !EasementInspectCommands.Headless()) EasementInspectCommands.ShowWindow(title, rows);
        }

        private static List<InspectRow> SourceRows(AcEntity entity, FtfStamp stamp, RecordEntityMetadata meta, RecordSurveyProject project, RecordSurveySettings s)
        {
            var rows = new List<InspectRow>();
            Action<string, string, string, string, string> add = (section, item, value, source, severity) =>
                rows.Add(new InspectRow { Section = section, Item = item, Value = value ?? "-", Source = source, Handle = entity.Handle.ToString(), Severity = severity ?? string.Empty });

            const string d = "Document";
            add(d, "Document", meta != null ? meta.Document : (project != null ? project.Document.Path : null), "Read by FTFRECORD", null);
            add(d, "Recording number", meta != null ? meta.RecordingNumber : null, "Read from the document", null);
            add(d, "Sheet", meta != null ? meta.Sheet : null, "Read from the document", null);
            add(d, "Survey type", meta != null ? meta.SurveyType : null, "From the document's title", null);
            add(d, "Project", stamp.PointNumber, "Stored in the drawing (FTFRECORD)", project == null ? "Warning" : null);
            if (project == null) add(d, "Project record", "NOT IN THIS DRAWING -- the entity metadata below is all that remains", "Named object dictionary", "Warning");

            const string c = "Course";
            add(c, "Call", stamp.TagText, "Entity stamp", null);
            add(c, "Figure", meta != null ? meta.Figure : null, "Review assignment", null);
            if (meta != null && !string.IsNullOrEmpty(meta.Lot)) add(c, "Lot", meta.Lot + (string.IsNullOrEmpty(meta.Block) ? string.Empty : ", Block " + meta.Block), "Lot label on the document", null);
            add(c, "Object type", meta != null ? meta.ObjectType : null, "Review assignment / standard", null);
            add(c, "Basis of the geometry", meta != null ? meta.Basis : null, "Recorded, Measured, Calculated, Inferred or Entered -- never mixed", null);
            add(c, "Record source", meta != null ? meta.RecordSource : null, "The (R1) tag on the call", null);
            add(c, "Record reference", meta != null ? meta.RecordReference : null, "The R1 = ... line on the document", null);
            add(c, "Record bearing / distance", meta != null ? (meta.RecordBearing ?? "-") + " / " + (meta.RecordDistance ?? "-") : null, "As read", null);
            add(c, "Measured bearing / distance", meta != null ? (meta.MeasuredBearing ?? "-") + " / " + (meta.MeasuredDistance ?? "-") : null, "As read (M)", null);
            if (meta != null && !string.IsNullOrEmpty(meta.Curve)) add(c, "Curve elements", meta.Curve, "As read; solved by the curve equations", null);
            add(c, "Confidence", meta != null ? meta.Confidence.ToString("0.00", CultureInfo.InvariantCulture) : null, "OCR agreement x parse repairs; edits raise it to 1.00", meta != null && meta.Confidence < s.ReviewThreshold ? "Warning" : null);
            add(c, "Source location", meta != null && meta.SourceBox != null ? "page " + meta.SourcePage + " at " + meta.SourceBox : null, "Pixels on the page image", null);
            add(c, "Source text", meta != null ? meta.SourceText : null, "Exactly as OCR read it", null);
            if (meta != null && meta.SharedWith.Count > 0) add(c, "Shared with", string.Join(", ", meta.SharedWith.ToArray()), "One entity for the shared lot line", null);
            add(c, "Created", meta != null ? meta.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : null, "Build time", null);

            if (project != null)
            {
                var call = project.FindCall(stamp.TagText);
                if (call != null)
                {
                    const string r = "Review";
                    add(r, "Status", call.Status.ToString(), "Review", call.Status == CallStatus.NeedsReview ? "Warning" : null);
                    foreach (var n in call.Notes) add(r, "Note", n, "Extraction", null);
                    foreach (var a in call.Alternatives) add(r, "Alternative reading", a.ToString(), "Offered, not applied", "Warning");
                    foreach (var e in call.Edits) add(r, "Edit", e, "Reviewer", null);
                    foreach (var rv in call.Records)
                    {
                        var reference = project.FindReference(rv.SourceId);
                        if (reference != null) add(r, "Reference " + rv.SourceId, reference.Description + (reference.RecordingNumber != null ? " (AFN " + reference.RecordingNumber + ")" : string.Empty), "Document", null);
                    }
                }
                var closure = project.Closures.FirstOrDefault(x => meta != null && string.Equals(x.Figure, meta.Figure, StringComparison.OrdinalIgnoreCase));
                if (closure != null)
                    add("Figure", "Closure", closure.Misclosure.ToString("0.000", CultureInfo.InvariantCulture) + "' (" + TraverseBuilder.PrecisionText(closure) + "), area " + closure.Area.ToString("N0", CultureInfo.InvariantCulture) + " sq ft",
                        "Traverse of the approved calls; nothing adjusted", closure.Misclosure > s.ClosureToleranceFt ? "Warning" : null);
            }
            return rows;
        }

        // ============================================================== FTFRECORDCHECK

        [CommandMethod("FTFRECORDCHECK", CommandFlags.Modal)]
        public void FtfRecordCheck()
        {
            var rows = new List<InspectRow>();
            var title = "Record check";
            var shown = false;
            FtfSession.Run("FTFRECORDCHECK", (db, tr, ed) =>
            {
                var settings = ResolveSettings(db);
                var s = settings.RecordSurvey;
                var projects = DrawingStore.LoadRecordProjects(db, tr);
                if (projects.Count == 0) { ed.WriteMessage("\nFTFRECORDCHECK: no recorded surveys are stored in this drawing.\n"); return; }
                var inventory = RecordDrafter.Inventory(db, tr);
                var report = new StringBuilder();
                foreach (var project in projects)
                {
                    var standards = Resolve(settings, inventory, project);
                    var cad = RecordDrafter.ReadBack(db, tr, project, settings);
                    var qc = RecordQc.Evaluate(project, cad, s, standards);
                    var text = qc.Text();
                    ed.WriteMessage("\n" + text);
                    report.AppendLine(text);
                    foreach (var issue in standards.Issues.Where(i => i.Severity == "Missing"))
                        rows.Add(new InspectRow { Section = project.Id + " standard", Item = issue.Entity + " " + issue.Resource, Value = issue.Name + ": " + issue.Effect, Source = "Settings > Recorded Surveys", Severity = "Warning" });
                    foreach (var item in qc.Items)
                        rows.Add(new InspectRow
                        {
                            Section = project.Id + (string.IsNullOrEmpty(item.Figure) ? string.Empty : " " + item.Figure),
                            Item = item.Subject, Value = (item.Message ?? string.Empty) + (item.Verdict == QcVerdict.Match ? " MATCH" : " -- " + item.Verdict),
                            Source = item.Code, Handle = item.Handle,
                            Severity = item.Verdict == QcVerdict.Match || item.Verdict == QcVerdict.Info ? string.Empty : item.Verdict == QcVerdict.Error ? "Error" : "Warning"
                        });
                }
                title = "Record check: " + projects.Count + " recorded survey(s)";
                shown = true;
                var file = ReportPath(db, settings, "ftf-record-check.txt");
                if (file != null)
                {
                    try { File.WriteAllText(file, report.ToString(), new UTF8Encoding(true)); ed.WriteMessage("\nReport written to " + file + "\n"); }
                    catch (IOException ex) { ed.WriteMessage("\nThe report could not be written: " + ex.Message + "\n"); }
                }
            });
            if (shown && !EasementInspectCommands.Headless()) EasementInspectCommands.ShowWindow(title, rows);
        }

        // ================================================================== shared

        private static FtfSettings ResolveSettings(Database db)
        {
            RulesConfig rules = null;
            try { rules = FtfSession.Rules(db); }
            catch (ConfigException) { }
            return FtfSession.ResolveSettings(db, rules).Settings;
        }

        private static StandardsResolution Resolve(FtfSettings settings, DrawingInventory inventory, RecordSurveyProject project)
        {
            var needed = project.Calls.Select(c => c.ObjectType).Where(t => !string.IsNullOrEmpty(t)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (needed.Count == 0) needed.Add(settings.RecordSurvey.DefaultObjectType);
            return StandardsResolver.Resolve(settings.RecordSurvey, inventory, settings.General.LayerMappings, needed);
        }

        private static void ReportStandards(Editor ed, StandardsResolution standards)
        {
            var missing = standards.Issues.Where(i => i.Severity == "Missing").ToList();
            var fallback = standards.Issues.Where(i => i.Severity == "Fallback").ToList();
            if (missing.Count == 0 && fallback.Count == 0) { ed.WriteMessage("\nStandards: every layer, style and block the record needs is in this drawing."); return; }
            if (missing.Count > 0)
            {
                ed.WriteMessage("\nStandards MISSING from this drawing ({0}) -- the items below are withheld, not substituted:", missing.Count);
                foreach (var i in missing) ed.WriteMessage("\n  - " + i);
            }
            if (fallback.Count > 0)
            {
                ed.WriteMessage("\nStandards falling back ({0}):", fallback.Count);
                foreach (var i in fallback) ed.WriteMessage("\n  - " + i);
            }
        }

        private static void Summarize(Editor ed, RecordSurveyProject project)
        {
            var d = project.Document;
            ed.WriteMessage("\n{0}: {1}{2}{3}{4}", d.SurveyType, d.Title ?? Path.GetFileName(d.Path ?? string.Empty),
                d.RecordingNumber != null ? ", AFN " + d.RecordingNumber : string.Empty,
                d.County != null ? ", " + d.County : string.Empty,
                d.ScaleFeetPerInch.HasValue ? ", 1\" = " + d.ScaleFeetPerInch.Value.ToString("0.#", CultureInfo.InvariantCulture) + "'" : ", scale not read");
            ed.WriteMessage("\n  {0} course(s): {1} line(s), {2} curve(s); {3} need review; {4} figure(s), {5} record reference(s), {6} monument(s).",
                project.Calls.Count, project.Calls.Count(c => c.Kind == CallKind.Line), project.Calls.Count(c => c.Kind == CallKind.Curve),
                project.Calls.Count(c => c.Status == CallStatus.NeedsReview), project.Figures.Count, project.References.Count, project.Monuments.Count);
            if (!string.IsNullOrEmpty(d.BasisOfBearing)) ed.WriteMessage("\n  " + d.BasisOfBearing);
            foreach (var w in project.Warnings) ed.WriteMessage("\n  " + w);
        }

        private static DocumentText LoadOcr(RecordSurveyProject project)
        {
            if (string.IsNullOrEmpty(project.OcrPath) || !File.Exists(project.OcrPath)) return null;
            try { return DocumentText.FromJson(File.ReadAllText(project.OcrPath)); }
            catch (ConfigException) { return null; }
        }

        private static string ChooseDocument(Editor ed, string prompt)
        {
            if (EasementInspectCommands.Headless())
            {
                var answer = ed.GetString(new PromptStringOptions("\n" + prompt + " path: ") { AllowSpaces = true });
                if (answer.Status != PromptStatus.OK || string.IsNullOrWhiteSpace(answer.StringResult)) return null;
                return answer.StringResult.Trim().Trim('"');
            }
            using (var dialog = new System.Windows.Forms.OpenFileDialog
            {
                Title = prompt,
                Filter = "Recorded surveys (*.pdf;*.tif;*.tiff;*.jpg;*.jpeg;*.png)|*.pdf;*.tif;*.tiff;*.jpg;*.jpeg;*.png|OCR text (*.ocr.json)|*.ocr.json|FTF record project (*.ftfrecord.json)|*.ftfrecord.json|All files|*.*",
                CheckFileExists = true
            })
            {
                return dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK ? dialog.FileName : null;
            }
        }

        /// <summary>
        /// The review: the window in Civil 3D; headless, every call at or above the threshold is
        /// approved and every figure starts at a typed point, so the smoke test can drive it.
        /// </summary>
        private static bool Review(AcDocument doc, ReviewSession session, DocumentText text, StandardsResolution standards, DrawingInventory inventory, FtfSettings settings)
        {
            var ed = doc.Editor;
            var upf = settings.General.UnitsPerFoot > 0 ? settings.General.UnitsPerFoot : 1.0;
            if (EasementInspectCommands.Headless())
            {
                var approved = session.ApproveAllAbove(settings.RecordSurvey.ReviewThreshold);
                ed.WriteMessage("\nHeadless review: {0} call(s) approved at or above {1:0.00}.", approved, settings.RecordSurvey.ReviewThreshold);
                foreach (var figure in session.Project.Figures.Where(f => !f.Start.HasValue && session.Project.CallsOf(f.Name).Count > 0))
                {
                    var point = ed.GetPoint(new PromptPointOptions("\nStart point of " + figure.Name + " (or Enter to place it on its shared course): ") { AllowNone = true });
                    if (point.Status == PromptStatus.OK)
                    {
                        var p = CadUtil.Flatten(point.Value);
                        foreach (var n in session.PlaceConnected(figure.Name, new P2(p.X, p.Y), upf)) ed.WriteMessage("\n  " + n);
                    }
                    else if (point.Status != PromptStatus.None) return false;
                }
                var gate = session.Gate();
                foreach (var b in gate.Blockers) ed.WriteMessage("\n  BLOCKED: " + b);
                foreach (var w in gate.Warnings) ed.WriteMessage("\n  " + w);
                return gate.Ready;
            }

            using (var form = new Ui.RecordReviewForm(doc, session, text, standards, inventory, settings))
            {
                var result = AcadApp.ShowModalDialog(form);
                return result == System.Windows.Forms.DialogResult.OK;
            }
        }

        private static void Build(AcDocument doc, RecordSurveyProject project, ReviewSession session, FtfSettings settings, StandardsResolution standards, bool rebuild)
        {
            var ed = doc.Editor;
            var db = doc.Database;
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                if (rebuild)
                {
                    var kept = RecordDrafter.Erase(db, tr, project, settings, true, true, false);
                    project.Built.Clear();
                }
                var outcome = RecordDrafter.Build(db, tr, ed, project, settings, standards, true);
                foreach (var m in outcome.Messages) ed.WriteMessage("\n  " + m);
                foreach (var p in outcome.Problems) ed.WriteMessage("\n  PROBLEM: " + p);
                project.Built.AddRange(outcome.Built);
                project.BuiltUtc = DateTime.UtcNow;
                project.BuiltFromMeasured = settings.RecordSurvey.PreferMeasured;
                DrawingStore.SaveRecordProject(db, tr, project);

                ed.WriteMessage("\n{0}: {1} line(s), {2} curve(s), {3} monument(s), {4} label(s), {5} table(s) built for {6} (revision {7}).",
                    rebuild ? "FTFRECORDREBUILD" : "FTFRECORD", outcome.Lines, outcome.Curves, outcome.Monuments, outcome.Labels, outcome.Tables, project.Id, project.Revision);
                foreach (var t in outcome.Traverses.Where(t => t.Closure != null))
                    ed.WriteMessage("\n  {0}: {1} course(s), closure {2:0.000}' ({3}), area {4:N0} sq ft", t.Figure, t.Closure.Courses, t.Closure.Misclosure,
                                    TraverseBuilder.PrecisionText(t.Closure), t.Closure.Area);

                // The QC report, straight away, so the drawing is never handed over without one.
                var cad = RecordDrafter.ReadBack(db, tr, project, settings);
                var qc = RecordQc.Evaluate(project, cad, settings.RecordSurvey, standards);
                ed.WriteMessage("\n" + qc.Text());

                if (settings.RecordSurvey.WriteProjectFile)
                {
                    var file = ReportPath(db, settings, project.Id + ".ftfrecord.json");
                    if (file != null)
                    {
                        try { File.WriteAllText(file, project.ToJson()); ed.WriteMessage("\nProject written to " + file); }
                        catch (IOException ex) { ed.WriteMessage("\nThe project file could not be written: " + ex.Message); }
                    }
                }
                ed.WriteMessage("\n");
                tr.Commit();
            }
        }

        /// <summary>Beside the drawing (or the report folder), named after the drawing. Null for an unsaved drawing.</summary>
        private static string ReportPath(Database db, FtfSettings settings, string suffix)
        {
            var dwg = db.Filename;
            if (string.IsNullOrEmpty(dwg)) return null;
            var folder = settings.General.ReportLocation == ReportLocation.CustomFolder && !string.IsNullOrWhiteSpace(settings.General.ReportFolder)
                ? settings.General.ReportFolder : Path.GetDirectoryName(dwg);
            if (string.IsNullOrEmpty(folder)) return null;
            try { Directory.CreateDirectory(folder); } catch (IOException) { return null; }
            return Path.Combine(folder, Path.GetFileNameWithoutExtension(dwg) + "." + suffix);
        }

        /// <summary>The project to work on: the only one, or the one behind a picked entity.</summary>
        private static RecordSurveyProject PickProject(AcDocument doc, string command)
        {
            var ed = doc.Editor;
            var db = doc.Database;
            IList<RecordSurveyProject> projects;
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                projects = DrawingStore.LoadRecordProjects(db, tr);
                tr.Commit();
            }
            if (projects.Count == 0) { ed.WriteMessage("\n{0}: no recorded surveys are stored in this drawing. Run FTFRECORD first.\n", command); return null; }
            if (projects.Count == 1) return projects[0];

            var options = new PromptEntityOptions("\nSelect an object of the recorded survey to work on: ");
            var picked = ed.GetEntity(options);
            if (picked.Status != PromptStatus.OK) return null;
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var stamp = Ownership.Read((AcEntity)tr.GetObject(picked.ObjectId, OpenMode.ForRead));
                tr.Commit();
                var project = stamp != null ? projects.FirstOrDefault(p => p.Id == stamp.PointNumber) : null;
                if (project == null) ed.WriteMessage("\n{0}: that object belongs to no stored recorded survey.\n", command);
                return project;
            }
        }
    }
}
