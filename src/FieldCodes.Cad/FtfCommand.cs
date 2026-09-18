using System;
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
            var total = pipeline.Steps.Count;
            var index = 0;

            ed.WriteMessage("\n=== Field to Finish: polishing the Civil 3D survey result ===\n");

            var result = pipeline.Run(
                null,
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
