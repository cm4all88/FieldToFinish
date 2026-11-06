#if TRACE_EDITION
using System;
using System.Linq;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using FieldCodes.TraceUi;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace FieldCodes.Cad
{
    /// <summary>
    /// The two TRACE commands behind the ribbon and the palette: show the palette, and run the
    /// ticked steps. Every other button runs an existing command directly.
    /// </summary>
    public sealed class TraceCommands
    {
        /// <summary>Shows or hides the TRACE palette.</summary>
        [CommandMethod("TRACE", CommandFlags.Modal)]
        public void ShowPalette()
        {
            try
            {
                TracePalette.Toggle();
            }
            catch (System.Exception ex)
            {
                var doc = AcadApp.DocumentManager.MdiActiveDocument;
                if (doc != null) doc.Editor.WriteMessage("\nTRACE: the palette could not open: {0}\n", ex.Message);
            }
        }

        /// <summary>
        /// Runs the steps the technician ticked in the palette, in pipeline order. Each step is
        /// the same existing step the individual commands use, and each replaces only its own
        /// previous output, so your own edits are left alone and running again never duplicates.
        /// </summary>
        [CommandMethod("TRACERUN", CommandFlags.Modal)]
        public void Run()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            var ed = doc.Editor;
            var pipeline = new FtfPipeline();
            var state = TraceUiState.Load(TraceUiState.DefaultPath());
            var selected = state.SelectedSteps(pipeline.Steps.Select(s => s.Name));

            if (selected.Count == 0)
            {
                ed.WriteMessage("\nTRACE: no steps are ticked. Tick the steps you want in the TRACE palette.\n");
                return;
            }

            ed.WriteMessage("\n=== TRACE: running {0} step(s) ===\n", selected.Count);
            var index = 0;

            var result = pipeline.Run(
                selected,
                step =>
                {
                    index++;
                    ed.WriteMessage("\n--- [{0}/{1}] {2} ---", index, selected.Count, TraceCatalog.StepTitle(step.Name));
                },
                (step, ex) =>
                {
                    ed.WriteMessage("\n{0} failed: {1}", TraceCatalog.StepTitle(step.Name), ex.Message);
                    return AskToContinue(ed, TraceCatalog.StepTitle(step.Name));
                });

            ed.WriteMessage("\n\n=== TRACE finished: {0} of {1} step(s) ===\n", result.Completed, selected.Count);
            if (result.Failed == 0)
                ed.WriteMessage("Run TRACE again at any time. Your own edits are left alone and nothing duplicates.\n");
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

            ed.WriteMessage("\nStopped after {0}. Fix it and run TRACE again, or run the remaining tools individually.\n", failedStep);
            return false;
        }
    }
}
#endif
