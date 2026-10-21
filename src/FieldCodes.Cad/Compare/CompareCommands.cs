using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using FieldCodes.Compare;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace FieldCodes.Cad.Compare
{
    /// <summary>
    /// FTFCOMPARE and its command-line twins. Every command goes through
    /// FtfSession.Run (one transaction, one undo, errors on the command line) and the
    /// shared CompareSession service, so the window and the commands cannot disagree.
    ///
    /// UNTESTED in Civil 3D.
    /// </summary>
    public sealed class CompareCommands
    {
        private static bool _hooked;

        /// <summary>Opens the review window. Nothing runs until a button is pressed.</summary>
        [CommandMethod("FTFCOMPARE", CommandFlags.Modal)]
        public void OpenWindow()
        {
            Hook();
            if (CompareSession.Form == null || CompareSession.Form.IsDisposed)
            {
                CompareSession.Form = new Ui.CompareReviewForm();
                AcadApp.ShowModelessDialog(CompareSession.Form);
            }
            else
            {
                CompareSession.Form.Activate();
            }
            CompareSession.Post("open", (db, tr, ed) => false, true);
        }

        /// <summary>Runs one queued window action. Not meant to be typed.</summary>
        [CommandMethod("FTFCOMPAREACT", CommandFlags.Modal | CommandFlags.NoHistory)]
        public void RunAction()
        {
            CompareSession.RunPending();
        }

        internal static void Hook()
        {
            if (_hooked) return;
            _hooked = true;
            AcadApp.DocumentManager.DocumentActivated += (s, e) =>
            {
                if (e.Document != null && CompareSession.DatabaseKey != e.Document.Database.UnmanagedObject)
                {
                    CompareSession.Reset();
                    CompareSession.RefreshForm();
                }
            };
            foreach (Document doc in AcadApp.DocumentManager) HookDocument(doc);
            AcadApp.DocumentManager.DocumentCreated += (s, e) => HookDocument(e.Document);
        }

        /// <summary>After SAVE AS the result and report files follow the drawing to its new
        /// name, so a working copy saved as a separate comparison drawing keeps its review
        /// (the applied record and decisions already travel inside the DWG).</summary>
        private static void HookDocument(Document doc)
        {
            if (doc == null) return;
            doc.CommandEnded += (s, e) =>
            {
                var name = e.GlobalCommandName.ToUpperInvariant();
                if (name != "SAVEAS" && name != "QSAVE" && name != "SAVE" && name != "SAVEALL") return;
                if (CompareSession.Result == null || CompareSession.DatabaseKey != doc.Database.UnmanagedObject) return;
                CompareSession.SaveResult(doc.Database);
                CompareSession.WriteReport(doc.Database, null);
            };
        }

        // ============================================================ command line

        /// <summary>Compares all survey objects, a selection, or a window, and reports.</summary>
        [CommandMethod("FTFCOMPARERUN", CommandFlags.Modal | CommandFlags.UsePickSet)]
        public void Run()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;
            Hook();

            ICollection<ObjectId> only = null;
            var options = new PromptKeywordOptions("\nCompare [All/Select/Window] <All>: ", "All Select Window") { AllowNone = true };
            options.Keywords.Default = "All";
            var answer = ed.GetKeywords(options);
            if (answer.Status != PromptStatus.OK && answer.Status != PromptStatus.None) return;
            var choice = answer.Status == PromptStatus.OK ? answer.StringResult : "All";
            if (choice == "Select")
            {
                var selection = ed.GetSelection(new PromptSelectionOptions { MessageForAdding = "\nSelect survey objects to compare: " });
                if (selection.Status != PromptStatus.OK) return;
                only = selection.Value.GetObjectIds();
            }
            else if (choice == "Window")
            {
                var first = ed.GetPoint("\nFirst corner of the comparison window: ");
                if (first.Status != PromptStatus.OK) return;
                var second = ed.GetCorner("\nOpposite corner: ", first.Value);
                if (second.Status != PromptStatus.OK) return;
                var selection = ed.SelectCrossingWindow(first.Value, second.Value);
                if (selection.Status != PromptStatus.OK) { ed.WriteMessage("\nNothing in that window."); return; }
                only = selection.Value.GetObjectIds();
            }

            FtfSession.Run("FTFCOMPARERUN", (db, tr, e) => CompareSession.RunComparison(db, tr, e, only));
            CompareSession.RefreshForm();
        }

        [CommandMethod("FTFCOMPAREAPPLY", CommandFlags.Modal)]
        public void Apply()
        {
            FtfSession.Run("FTFCOMPAREAPPLY", (db, tr, ed) => CompareSession.ApplyPlan(db, tr, ed));
            CompareSession.RefreshForm();
        }

        [CommandMethod("FTFCOMPARERESTORE", CommandFlags.Modal)]
        public void Restore()
        {
            FtfSession.Run("FTFCOMPARERESTORE", (db, tr, ed) => CompareSession.RestoreAll(db, tr, ed));
            CompareSession.RefreshForm();
        }

        [CommandMethod("FTFCOMPAREQC", CommandFlags.Modal)]
        public void Qc()
        {
            FtfSession.Run("FTFCOMPAREQC", (db, tr, ed) => CompareSession.RunQc(db, tr, ed));
            CompareSession.RefreshForm();
        }

        /// <summary>Final QC and the deliverable copy in the delivery folder.</summary>
        [CommandMethod("FTFCOMPARESAVE", CommandFlags.Modal)]
        public void Save()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;
            string folder = null;
            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartOpenCloseTransaction())
            {
                CompareSession.EnsureLoaded(doc.Database, tr);
                folder = CompareSession.DeliveryFolder(doc.Database);
                tr.Commit();
            }
            if (string.IsNullOrWhiteSpace(folder))
            {
                var typed = ed.GetString(new PromptStringOptions("\nDelivery folder: ") { AllowSpaces = true });
                if (typed.Status != PromptStatus.OK || string.IsNullOrWhiteSpace(typed.StringResult)) return;
                folder = typed.StringResult.Trim();
            }
            CompareSession.PendingSavePath = null;
            FtfSession.Run("FTFCOMPARESAVE", (db, tr, e) => CompareSession.PrepareDeliverable(db, tr, e, folder));
            if (FtfSession.LastRunError == null && CompareSession.PendingSavePath != null) CompareSession.CompleteSave();
            CompareSession.RefreshForm();
        }

        /// <summary>A decision from the command line (also what scripts and the smoke test use):
        /// FTFCOMPAREDECIDE handle AcceptMatch|KeepPlotting|MakeNonplot|AcceptPartial|Exclude|None,
        /// or FTFCOMPAREDECIDE Confident (accept every confident result), or
        /// FTFCOMPAREDECIDE Unresolved (keep every unresolved result plotting).</summary>
        [CommandMethod("FTFCOMPAREDECIDE", CommandFlags.Modal)]
        public void Decide()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;
            var handle = ed.GetString(new PromptStringOptions("\nSurvey object handle (or Confident to accept every confident result, Unresolved to keep every unresolved one plotting): ") { AllowSpaces = false });
            if (handle.Status != PromptStatus.OK || string.IsNullOrWhiteSpace(handle.StringResult)) return;
            string decision = null;
            var bulk = string.Equals(handle.StringResult, "Confident", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(handle.StringResult, "Unresolved", StringComparison.OrdinalIgnoreCase);
            if (!bulk)
            {
                var options = new PromptKeywordOptions("\nDecision [AcceptMatch/KeepPlotting/MakeNonplot/AcceptPartial/Exclude/None]: ", "AcceptMatch KeepPlotting MakeNonplot AcceptPartial Exclude None");
                var answer = ed.GetKeywords(options);
                if (answer.Status != PromptStatus.OK) return;
                decision = answer.StringResult;
            }
            FtfSession.Run("FTFCOMPAREDECIDE", (db, tr, e) =>
            {
                CompareSession.EnsureLoaded(db, tr);
                if (CompareSession.Result == null) { e.WriteMessage("\nRun the comparison first."); return; }
                int n;
                if (decision == null)
                    n = string.Equals(handle.StringResult, "Unresolved", StringComparison.OrdinalIgnoreCase) ? CompareSession.KeepUnresolvedPlotting() : CompareSession.AcceptConfident();
                else n = CompareSession.Decide(new[] { handle.StringResult.Trim() }, (ReviewDecision)Enum.Parse(typeof(ReviewDecision), decision), "command line");
                CompareSession.SaveDecisions(db, tr);
                e.WriteMessage("\n{0} decision(s) recorded. Run FTFCOMPAREAPPLY to apply them.", n);
            });
            CompareSession.RefreshForm();
        }

        /// <summary>Read-only report of what the comparison holds in this drawing: applied
        /// changes, objects on the nonplot layers, split copies, block twins, restyled points.
        /// "CLEAN" after a restore means nothing of the comparison is left in effect.</summary>
        [CommandMethod("FTFCOMPAREAUDIT", CommandFlags.Modal)]
        public void Audit()
        {
            FtfSession.Run("FTFCOMPAREAUDIT", (db, tr, ed) => CompareSession.Audit(db, tr, ed));
        }

        /// <summary>Lists, chooses or saves the drawing's comparison profile.</summary>
        [CommandMethod("FTFCOMPAREPROFILE", CommandFlags.Modal)]
        public void ProfileCommand()
        {
            FtfSession.Run("FTFCOMPAREPROFILE", (db, tr, ed) =>
            {
                CompareSession.EnsureLoaded(db, tr);
                var names = CompareProfile.ListProfiles(CompareSession.PluginDirectory());
                ed.WriteMessage("\nComparison profile for this drawing: {0} [{1}]", CompareSession.Profile.Name, CompareSession.ProfileLevel);
                ed.WriteMessage("\nProfiles available: {0}", string.Join(", ", names.ToArray()));
                ed.WriteMessage("\nUser profiles folder: {0}", CompareProfile.ProfilesDirectory());
                var options = new PromptKeywordOptions("\n[Use/SaveAs/Edit/List] <exit>: ", "Use SaveAs Edit List") { AllowNone = true };
                var answer = ed.GetKeywords(options);
                if (answer.Status != PromptStatus.OK) return;
                switch (answer.StringResult)
                {
                    case "Use":
                    {
                        var name = ed.GetString(new PromptStringOptions("\nProfile name to use: ") { AllowSpaces = true });
                        if (name.Status != PromptStatus.OK || string.IsNullOrWhiteSpace(name.StringResult)) return;
                        CompareSession.ChooseProfile(db, tr, name.StringResult.Trim());
                        CompareSession.RebuildPlan();
                        ed.WriteMessage("\nThis drawing now compares with profile \"{0}\" [{1}]. Re-run the comparison to apply it.", CompareSession.Profile.Name, CompareSession.ProfileLevel);
                        break;
                    }
                    case "SaveAs":
                    {
                        var name = ed.GetString(new PromptStringOptions("\nSave the current profile as: ") { AllowSpaces = true });
                        if (name.Status != PromptStatus.OK || string.IsNullOrWhiteSpace(name.StringResult)) return;
                        var copy = CompareProfile.FromJson(CompareSession.Profile.ToJson());
                        copy.Name = name.StringResult.Trim();
                        var path = CompareProfile.ProfilePath(copy.Name);
                        copy.Save(path);
                        CompareSession.ChooseProfile(db, tr, copy.Name);
                        ed.WriteMessage("\nSaved {0}. Edit the JSON to map this consultant's layers, blocks and words; it is reused for every site that chooses it.", path);
                        break;
                    }
                    case "Edit":
                    {
                        var path = CompareProfile.ProfilePath(CompareSession.Profile.Name);
                        if (!File.Exists(path))
                        {
                            CompareSession.Profile.Save(path);
                            ed.WriteMessage("\nThe profile was built in; a copy is now at {0}.", path);
                        }
                        try { System.Diagnostics.Process.Start(path); }
                        catch (System.ComponentModel.Win32Exception ex) { ed.WriteMessage("\nCould not open {0}: {1}", path, ex.Message); }
                        break;
                    }
                    case "List":
                    {
                        var p = CompareSession.Profile;
                        ed.WriteMessage("\n  Tolerances: point {0} ft, line offset {1} ft, angle {2} deg, gap {3} ft, min uncovered {4} ft, elevation {5} ft, auto-accept {6:0.00}",
                            p.Tolerances.PointFeet, p.Tolerances.LineOffsetFeet, p.Tolerances.AngleDegrees, p.Tolerances.GapFeet, p.Tolerances.MinUncoveredFeet, p.Tolerances.ElevationFeet, p.Tolerances.AutoAcceptConfidence);
                        ed.WriteMessage("\n  Nonplot layers: {0}", p.NonplotLayers.Describe);
                        foreach (var c in p.Categories.Where(c => c.Enabled))
                            ed.WriteMessage("\n  {0,-18} {1,-5} {2,-9} survey: {3} | consultant: {4}", c.Key, c.Kind, c.System,
                                Describe(c.Survey), Describe(c.Consultant));
                        break;
                    }
                }
            });
            CompareSession.RefreshForm();
        }

        private static string Describe(SourceMapping m)
        {
            var parts = new List<string>();
            if (m.Codes.Count > 0) parts.Add("codes " + string.Join("/", m.Codes.Take(6).ToArray()) + (m.Codes.Count > 6 ? "..." : string.Empty));
            if (m.Layers.Count > 0) parts.Add("layers " + string.Join("/", m.Layers.Take(4).ToArray()) + (m.Layers.Count > 4 ? "..." : string.Empty));
            if (m.Blocks.Count > 0) parts.Add("blocks " + string.Join("/", m.Blocks.Take(4).ToArray()) + (m.Blocks.Count > 4 ? "..." : string.Empty));
            if (m.Keywords.Count > 0) parts.Add("words " + string.Join("/", m.Keywords.Take(3).ToArray()) + (m.Keywords.Count > 3 ? "..." : string.Empty));
            return parts.Count == 0 ? "-" : string.Join("; ", parts.ToArray());
        }

        /// <summary>Where the consultant's data is: this drawing's xrefs (all or chosen) or a file.</summary>
        [CommandMethod("FTFCOMPARESOURCE", CommandFlags.Modal)]
        public void SourceCommand()
        {
            FtfSession.Run("FTFCOMPARESOURCE", (db, tr, ed) =>
            {
                CompareSession.EnsureLoaded(db, tr);
                var xrefs = CompareDrawingReader.XrefStates(db, tr);
                ed.WriteMessage("\nConsultant source now: {0}", CompareSession.Source.Describe());
                ed.WriteMessage("\nExternal references in this drawing: {0}", xrefs.Count == 0 ? "(none)" :
                    string.Join(", ", xrefs.Select(x => x.Name + (x.Loaded ? string.Empty : x.Resolved ? " (unloaded)" : " (UNRESOLVED)")).ToArray()));
                var options = new PromptKeywordOptions("\n[Xrefs/Chosen/File] <keep>: ", "Xrefs Chosen File") { AllowNone = true };
                var answer = ed.GetKeywords(options);
                if (answer.Status != PromptStatus.OK) return;
                switch (answer.StringResult)
                {
                    case "Xrefs":
                        CompareSession.Source.Mode = "Xrefs";
                        CompareSession.Source.XrefNames = new List<string>();
                        break;
                    case "Chosen":
                    {
                        var typed = ed.GetString(new PromptStringOptions("\nXref names, comma separated: ") { AllowSpaces = true });
                        if (typed.Status != PromptStatus.OK) return;
                        CompareSession.Source.Mode = "Xrefs";
                        CompareSession.Source.XrefNames = typed.StringResult.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
                        break;
                    }
                    case "File":
                    {
                        var open = ed.GetFileNameForOpen(new PromptOpenFileOptions("\nConsultant drawing") { Filter = "Drawing (*.dwg)|*.dwg" });
                        if (open.Status != PromptStatus.OK) return;
                        CompareSession.Source.Mode = "File";
                        CompareSession.Source.FilePath = open.StringResult;
                        break;
                    }
                }
                CompareSession.SaveSource(db, tr);
                ed.WriteMessage("\nConsultant source: {0}. Re-run the comparison.", CompareSession.Source.Describe());
            });
            CompareSession.RefreshForm();
        }

        /// <summary>The comparison limits for this drawing: a polygon picked on screen, or cleared.</summary>
        [CommandMethod("FTFCOMPAREAREA", CommandFlags.Modal)]
        public void AreaCommand()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;
            var options = new PromptKeywordOptions("\nComparison limits [Pick/Object/Clear] <Pick>: ", "Pick Object Clear") { AllowNone = true };
            options.Keywords.Default = "Pick";
            var answer = ed.GetKeywords(options);
            if (answer.Status != PromptStatus.OK && answer.Status != PromptStatus.None) return;
            var choice = answer.Status == PromptStatus.OK ? answer.StringResult : "Pick";

            var points = new List<CPoint>();
            ObjectId polygonId = ObjectId.Null;
            if (choice == "Pick")
            {
                var first = ed.GetPoint("\nFirst corner of the comparison area: ");
                if (first.Status != PromptStatus.OK) return;
                points.Add(new CPoint(first.Value.X, first.Value.Y));
                while (true)
                {
                    var next = ed.GetPoint(new PromptPointOptions("\nNext corner (Enter to finish): ") { AllowNone = true, UseBasePoint = true, BasePoint = first.Value });
                    if (next.Status != PromptStatus.OK) break;
                    points.Add(new CPoint(next.Value.X, next.Value.Y));
                }
                if (points.Count < 3) { ed.WriteMessage("\nAt least three corners are needed."); return; }
            }
            else if (choice == "Object")
            {
                var picked = ed.GetEntity("\nSelect a closed polyline marking the comparison area: ");
                if (picked.Status != PromptStatus.OK) return;
                polygonId = picked.ObjectId;
            }

            FtfSession.Run("FTFCOMPAREAREA", (db, tr, e) =>
            {
                CompareSession.EnsureLoaded(db, tr);
                if (choice == "Clear")
                {
                    CompareSession.Source.Boundary = new List<double[]>();
                }
                else
                {
                    if (!polygonId.IsNull)
                    {
                        var pl = tr.GetObject(polygonId, OpenMode.ForRead) as Autodesk.AutoCAD.DatabaseServices.Polyline;
                        if (pl == null) { e.WriteMessage("\nThat is not a polyline."); return; }
                        for (var i = 0; i < pl.NumberOfVertices; i++) { var p = pl.GetPoint3dAt(i); points.Add(new CPoint(p.X, p.Y)); }
                        if (points.Count < 3) { e.WriteMessage("\nThe polyline needs at least three vertices."); return; }
                    }
                    CompareSession.Source.Boundary = points.Select(p => new[] { p.X, p.Y }).ToList();
                }
                CompareSession.SaveSource(db, tr);
                CompareSession.RebuildPlan();
                e.WriteMessage(choice == "Clear" ? "\nComparison limits cleared: the consultant data's own extent applies." : "\nComparison limits set ({0} corners). Re-run the comparison.", points.Count);
            });
            CompareSession.RefreshForm();
        }

        // ================================================================== batch

        /// <summary>Processes a manifest of sites in sequence. Unresolved sites go to a
        /// needs-review folder, never to the approved one.</summary>
        [CommandMethod("FTFCOMPAREBATCH", CommandFlags.Modal)]
        public void Batch()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;
            var open = ed.GetFileNameForOpen(new PromptOpenFileOptions("\nBatch manifest (JSON)") { Filter = "Batch manifest (*.json)|*.json" });
            if (open.Status != PromptStatus.OK) return;

            BatchManifest manifest;
            try { manifest = BatchManifest.Load(open.StringResult); }
            catch (ConfigException ex) { ed.WriteMessage("\n{0}", ex.Message); return; }
            var problems = new List<string>();
            manifest.Validate(problems);
            if (problems.Count > 0) { ed.WriteMessage("\nManifest problems:\n  {0}", string.Join("\n  ", problems.ToArray())); return; }

            var outcomes = CompareBatch.Run(manifest, open.StringResult, ed);
            var summaryPath = Path.Combine(Path.GetDirectoryName(open.StringResult) ?? ".", Path.GetFileNameWithoutExtension(open.StringResult) + ".batch-summary.csv");
            try { File.WriteAllText(summaryPath, BatchSummary.RenderCsv(outcomes)); ed.WriteMessage("\nBatch summary: {0}", summaryPath); }
            catch (IOException ex) { ed.WriteMessage("\nCould not write the batch summary: {0}", ex.Message); }
            ed.WriteMessage("\n{0}\n", BatchSummary.RenderText(outcomes));
        }
    }

    /// <summary>
    /// The batch: each site's survey drawing is opened as a side database (never as a
    /// document), compared, confident results applied, and saved under the output folder
    /// -- approved/ when nothing is unresolved, needs-review/ otherwise. Source files are
    /// only read.
    /// </summary>
    internal static class CompareBatch
    {
        public static IList<BatchSiteOutcome> Run(BatchManifest manifest, string manifestPath, Editor ed)
        {
            var outcomes = new List<BatchSiteOutcome>();
            var manifestDir = Path.GetDirectoryName(manifestPath) ?? string.Empty;
            var settings = CompareSession.SettingsFor(AcadApp.DocumentManager.MdiActiveDocument.Database);

            foreach (var site in manifest.Sites)
            {
                var outcome = new BatchSiteOutcome { Name = string.IsNullOrWhiteSpace(site.Name) ? Path.GetFileNameWithoutExtension(site.SurveyDrawing) : site.Name };
                var watch = System.Diagnostics.Stopwatch.StartNew();
                ed.WriteMessage("\n=== Site {0} ===", outcome.Name);
                try
                {
                    var surveyPath = Absolute(site.SurveyDrawing, manifestDir);
                    if (!File.Exists(surveyPath)) throw new ConfigException("Survey drawing not found: " + surveyPath);
                    var outputFolder = Absolute(manifest.OutputFolderFor(site), manifestDir);
                    var profileName = manifest.ProfileFor(site);

                    using (var db = new Database(false, true))
                    {
                        db.ReadDwgFile(surveyPath, FileShare.Read, true, string.Empty);
                        db.CloseInput(true);
                        var previous = HostApplicationServices.WorkingDatabase;
                        HostApplicationServices.WorkingDatabase = db;
                        try
                        {
                            using (var tr = db.TransactionManager.StartTransaction())
                            {
                                var rules = CompareSession.RulesOrNull(db);
                                var resolution = CompareProfile.Resolve(profileName, Path.GetDirectoryName(surveyPath), CompareSession.PluginDirectory(), rules);
                                var profile = resolution.Profile;
                                if (rules != null && resolution.Level != "BuiltIn") profile.ApplySurveyDefaultsFrom(rules);

                                var spec = new ConsultantSourceSpec();
                                if (!string.IsNullOrWhiteSpace(site.ConsultantDrawing)) { spec.Mode = "File"; spec.FilePath = Absolute(site.ConsultantDrawing, manifestDir); }

                                // An earlier application in the file is undone first, so nothing accumulates.
                                var earlier = AppliedChangeSet.FromJson(DrawingStore.ReadCompareText(db, tr, "APPLIED"));
                                if (earlier != null && earlier.Changes.Count > 0)
                                {
                                    int orphans, twins;
                                    CompareLayerManager.RestoreAll(db, tr, earlier, null, out orphans, out twins);
                                    DrawingStore.DeleteCompareText(db, tr, "APPLIED");
                                }

                                CompareDataset survey, consultant;
                                using (var progress = new EditorProgress(ed))
                                {
                                    progress.Stage("Reading " + Path.GetFileName(surveyPath));
                                    survey = CompareDrawingReader.ReadSurvey(db, tr, profile, null, progress);
                                    progress.Stage("Reading consultant data");
                                    consultant = CompareDrawingReader.ReadConsultant(db, tr, spec, profile, survey.UnitsPerFoot, progress);
                                    var result = new ComparisonEngine(profile).Run(survey, consultant, progress);

                                    var decisions = ReviewDecisionSet.FromJson(DrawingStore.ReadCompareText(db, tr, "DECISIONS"));
                                    var sidecar = ReviewDecisionSet.PathFor(surveyPath);
                                    if (decisions.Entries.Count == 0 && sidecar != null && File.Exists(sidecar)) decisions = ReviewDecisionSet.FromJson(File.ReadAllText(sidecar));
                                    var stale = new List<string>();
                                    decisions.ApplyTo(result, stale);

                                    var plan = ChangePlanner.Build(result, profile);
                                    int applied, reversed, skipped;
                                    var problems = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                                    var record = CompareLayerManager.Reconcile(db, tr, plan, result, profile, rules != null ? rules.Version : string.Empty, null, true, null,
                                                                              out applied, out reversed, out skipped, problems);
                                    foreach (var kv in problems) { var pr = result.Find(kv.Key); if (pr != null) { pr.ApplyProblem = kv.Value; pr.Note("not applied: " + kv.Value); } }
                                    DrawingStore.WriteCompareText(db, tr, "APPLIED", record.ToJson());
                                    DrawingStore.WriteCompareText(db, tr, "DECISIONS", decisions.ToJson());
                                    DrawingStore.WriteCompareText(db, tr, "PROFILE", profile.Name);

                                    var summary = result.Summary();
                                    outcome.Summary = summary;
                                    outcome.Applied = applied;
                                    outcome.NeedsReview = summary.Unresolved > 0 || result.ConsultantIncomplete || plan.AwaitingReview > 0;

                                    var input = CompareLayerManager.GatherQc(db, tr, profile, result, plan, record, outputFolder);
                                    var checks = CompareQc.Evaluate(input);
                                    if (!CompareQc.Passes(checks)) outcome.NeedsReview = true;

                                    var subfolder = Path.Combine(outputFolder, outcome.NeedsReview ? "needs-review" : "approved");
                                    Directory.CreateDirectory(subfolder);
                                    var stem = Path.GetFileNameWithoutExtension(surveyPath) + (settings.Compare.DeliverySuffix ?? string.Empty);
                                    outcome.ReportPath = Path.Combine(subfolder, stem + CompareReport.Suffix);
                                    File.WriteAllText(outcome.ReportPath, CompareReport.Render(result, outcome.Name), new System.Text.UTF8Encoding(true));
                                    File.WriteAllText(Path.Combine(subfolder, stem + ".qc.txt"), CompareQc.Render(checks) + Environment.NewLine + Environment.NewLine + CompareReport.SummaryText(result, plan));
                                    tr.Commit();

                                    outcome.OutputPath = Path.Combine(subfolder, stem + ".dwg");
                                    ed.WriteMessage("\n  {0}", summary);
                                }
                            }
                            db.SaveAs(outcome.OutputPath, DwgVersion.Current);
                            outcome.Succeeded = File.Exists(outcome.OutputPath);
                            if (!outcome.Succeeded) outcome.Error = "the output drawing did not save";
                        }
                        finally { HostApplicationServices.WorkingDatabase = previous; }
                    }
                }
                catch (ConfigException ex) { outcome.Error = ex.Message; }
                catch (Autodesk.AutoCAD.Runtime.Exception ex) { outcome.Error = "AutoCAD: " + ex.Message; }
                catch (IOException ex) { outcome.Error = ex.Message; }
                catch (OperationCanceledException) { outcome.Error = "cancelled"; outcomes.Add(outcome); break; }
                outcome.Seconds = watch.Elapsed.TotalSeconds;
                if (outcome.Error != null) ed.WriteMessage("\n  FAILED: {0}", outcome.Error);
                else ed.WriteMessage("\n  {0} -> {1}", outcome.NeedsReview ? "NEEDS REVIEW" : "approved", outcome.OutputPath);
                outcomes.Add(outcome);
            }
            return outcomes;
        }

        private static string Absolute(string path, string baseDir)
        {
            if (string.IsNullOrWhiteSpace(path)) return path;
            return Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(baseDir, path));
        }
    }
}
