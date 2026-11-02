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

            // Which preset: the same four as the Process page. "All" is every stage as before,
            // except symbol orientation, which is its own decision (FTFORIENT).
            var ask = new PromptKeywordOptions("\nProcess [Standard/Trees/Annotations/All] <Standard>: ") { AllowNone = true };
            foreach (var k in new[] { "Standard", "Trees", "Annotations", "All" }) ask.Keywords.Add(k);
            ask.Keywords.Default = "Standard";
            var answer = ed.GetKeywords(ask);
            if (answer.Status != PromptStatus.OK && answer.Status != PromptStatus.None) return;
            var choice = answer.Status == PromptStatus.None ? "Standard" : answer.StringResult;

            FieldCodes.Settings.AnnotationSettings ann = null;
            FtfSession.Run("FTFRUN", (db, tr, e) => { ann = FtfSession.SettingsFor(db, FtfSession.Rules(db)).Annotation; });
            if (ann == null) return;

            FieldCodes.Annotation.ProcessPlan plan;
            switch (choice)
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

            ed.WriteMessage("\n=== Field to Finish: {0} ===\n", FieldCodes.Annotation.ProcessPresets.Title(plan.Preset));
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

            ed.WriteMessage("\n\n=== Finished: {0} of {1} step(s) ===\n",
                            result.Completed, total);

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
