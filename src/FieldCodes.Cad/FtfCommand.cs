using System;
using System.Linq;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace FieldCodes.Cad
{
    /// <summary>
    /// The two entry points to the finishing pass.
    ///
    /// FTF opens the main window -- the one command the department has to remember.
    /// FTFRUN processes the whole drawing directly from the command line. Both drive
    /// the same FtfPipeline, as do the individual FTF* commands, so none of them can
    /// drift apart.
    ///
    /// UNTESTED: never run against a drawing.
    /// </summary>
    public sealed class FtfCommand
    {
        private static Ui.FtfMainForm _window;

        /// <summary>
        /// Opens (or fronts) the window, optionally landing on a page. FTFSETUP uses
        /// this to land on Settings, so setup is the same experience everywhere.
        /// </summary>
        internal static void ShowWindow(string page)
        {
            // A failure building or showing the window must surface as a message,
            // not take Civil 3D down -- an unhandled exception here is fatal to
            // the whole session.
            try
            {
                if (_window == null || _window.IsDisposed)
                {
                    _window = new Ui.FtfMainForm();
                    // Modeless: the editor stays usable, and finishing stages that
                    // prompt (the schedule asks for its corner) still can.
                    AcadApp.ShowModelessDialog(_window);
                }
                else
                {
                    _window.Activate();
                    _window.RefreshStatus();
                }

                if (page != null) _window.ShowPageByName(page);
            }
            catch (System.Exception ex)
            {
                _window = null;
                var doc = AcadApp.DocumentManager.MdiActiveDocument;
                if (doc != null)
                    doc.Editor.WriteMessage(
                        "\nFTF: the window could not open: {0}\n{1}\n",
                        ex.Message, ex.StackTrace);
            }
        }

        /// <summary>Opens the window on one settings page ("Gates", "Review and QC").</summary>
        internal static void ShowSettings(string section)
        {
            ShowWindow(null);
            if (_window != null && !_window.IsDisposed) _window.OpenSettings(section);
        }

        /// <summary>
        /// Opens the Field to Finish window. Deliberately does NOT process anything:
        /// processing is a button in the window, or FTFRUN for the command line.
        /// </summary>
        [CommandMethod("FTF", CommandFlags.Modal)]
        public void OpenWindow()
        {
            ShowWindow(null);
        }

        [CommandMethod("FTFRUN", CommandFlags.Modal)]
        public void RunAll()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            var ed = doc.Editor;
            var pipeline = new FtfPipeline();

            // The settings first: the workflows saved in them are choices too.
            FieldCodes.Settings.FtfSettings settings = null;
            FtfSession.Run("FTFRUN", (db, tr, e) => { settings = FtfSession.SettingsFor(db, FtfSession.Rules(db)); });
            if (settings == null) return;
            var ann = settings.Annotation;

            // Which preset: the same as the Process page -- the four built-in choices, then each
            // saved workflow by its keyword (Settings > Workflows). "All" is every stage as before,
            // except symbol orientation, which is its own decision (FTFORIENT).
            var workflows = FieldCodes.Settings.WorkflowSettings.Choices(FtfWorkflows.For(settings));
            var keywords = FieldCodes.Settings.WorkflowSettings.BuiltInKeywords.Concat(workflows.Select(w => w.Key)).ToArray();
            var ask = new PromptKeywordOptions("\nProcess [" + string.Join("/", keywords) + "] <Standard>: ",
                                               string.Join(" ", keywords)) { AllowNone = true };
            ask.Keywords.Default = "Standard";
            var answer = ed.GetKeywords(ask);
            if (answer.Status != PromptStatus.OK && answer.Status != PromptStatus.None) return;
            var choice = answer.Status == PromptStatus.None ? "Standard" : answer.StringResult;

            FieldCodes.Annotation.ProcessPlan plan;
            var workflow = workflows.Where(w => string.Equals(w.Key, choice, StringComparison.OrdinalIgnoreCase))
                                    .Select(w => w.Value).FirstOrDefault();
            if (workflow != null)
                plan = FtfWorkflows.Plan(workflow, ann);
            else switch (choice)
            {
                case "Trees": plan = FieldCodes.Annotation.ProcessPresets.Build(FieldCodes.Annotation.ProcessPreset.TreesAndVegetation, ann); break;
                case "Annotations": plan = FieldCodes.Annotation.ProcessPresets.Build(FieldCodes.Annotation.ProcessPreset.AnnotationsOnly, ann); break;
                case "All":
                    plan = FieldCodes.Annotation.ProcessPresets.Build(FieldCodes.Annotation.ProcessPreset.Custom, ann,
                        pipeline.Steps.Select(st => st.Name).Where(n => n != "FTFORIENT"));
                    break;
                default: plan = FieldCodes.Annotation.ProcessPresets.Build(FieldCodes.Annotation.ProcessPreset.StandardTopographic, ann); break;
            }

            var total = plan.Stages.Count;
            var index = 0;

            ed.WriteMessage("\n=== Field to Finish: {0} ===\n", plan.Title);
            if (workflow != null && !string.IsNullOrWhiteSpace(workflow.Description))
                ed.WriteMessage("  {0}\n", workflow.Description.Trim());
            foreach (var note in plan.Adjustments) ed.WriteMessage("  {0}\n", note);

            FtfRunResult result;
            using (FieldCodes.Annotation.AnnotationRun.Use(plan))
                result = pipeline.Run(
                plan.Stages,
                step =>
                {
                    index++;
                    ed.WriteMessage("\n--- [{0}/{1}] {2} - {3} ---",
                                    index, total, step.Name, step.Description);
                },
                (step, ex) =>
                {
                    ed.WriteMessage("\n{0} failed: {1}", step.Name, ex.Message);
                    return AskToContinue(ed, step.Name);
                });

            // Point accountability and linework continuity, after every run -- a run that stopped
            // early too (the summary says so). Writes only the review list, the ledger and the
            // non-plotted previews; one U undoes the run and its checks together.
            ed.WriteMessage("\n\n--- Point accountability and linework continuity{0} ---", result.StoppedEarly ? " (after a run that stopped early)" : string.Empty);
            var checks = PostRunChecks.AfterProcessing("FTFRUN " + choice + (result.StoppedEarly ? " (stopped early)" : string.Empty));

            // The drawing remembers its last processing run (Overview > Last processed).
            try { FtfStatusService.RecordRun(result, FtfStatusService.Gather(), "FTFRUN " + choice, null); }
            catch (System.Exception ex) { ed.WriteMessage("\n(Could not record the run in the drawing: {0})", ex.Message); }

            ed.WriteMessage("\n\n=== Finished: {0} of {1} step(s) ===\n",
                            result.Completed, total);
            if (checks != null) ed.WriteMessage("{0}\n", checks.OneLine());

            if (result.Failed == 0)
                ed.WriteMessage("Run FTFRUN again at any time; every step deletes its own " +
                                "previous output first, so nothing duplicates.\n");
        }

        private static bool AskToContinue(Editor ed, string failedStep)
        {
            var options = new PromptKeywordOptions("\nContinue with the remaining steps?")
            {
                AllowNone = false
            };
            options.Keywords.Add("Yes");
            options.Keywords.Add("No");
            options.Keywords.Default = "No";

            var answer = ed.GetKeywords(options);

            if (answer.Status == PromptStatus.OK &&
                string.Equals(answer.StringResult, "Yes", StringComparison.OrdinalIgnoreCase))
                return true;

            ed.WriteMessage("\nStopped after {0}. Fix it and run FTFRUN again, or run the " +
                            "remaining commands individually.\n", failedStep);
            return false;
        }
    }
}
