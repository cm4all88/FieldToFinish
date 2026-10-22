using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using FieldCodes.Compare;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace FieldCodes.Cad.Compare
{
    /// <summary>FTFTRIM and FTFTRIMRESTORE: trim the aerial base inside our limits of survey
    /// and flag the seams; put it all back.</summary>
    public sealed class TrimCommands
    {
        /// <summary>FTFTRIM: in the aerial base drawing with our finished site(s) attached as xrefs
        /// (or one chosen file): our survey source, then the limits of survey from their layer or
        /// picked, then trim, seams, clouds and reports in one go.</summary>
        [CommandMethod("FTFTRIM", CommandFlags.Modal)]
        public void Trim()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;

            FtfSession.Run("FTFTRIM", (db, tr, e) =>
            {
                TrimSession.EnsureLoaded(db, tr);
                var xrefs = CompareDrawingReader.XrefStates(db, tr);
                e.WriteMessage("\nThis drawing is the aerial base. External references: {0}", xrefs.Count == 0 ? "(none)" :
                    string.Join(", ", xrefs.Select(x => x.Name + (x.Loaded ? string.Empty : x.Resolved ? " (unloaded)" : " (UNRESOLVED)")).ToArray()));
                var source = new PromptKeywordOptions("\nOur survey comes from [Xrefs/File] <" + (TrimSession.SurveySource.IsFile ? "File" : "Xrefs") + ">: ", "Xrefs File") { AllowNone = true };
                var answer = e.GetKeywords(source);
                if (answer.Status != PromptStatus.OK && answer.Status != PromptStatus.None) return;
                if (answer.Status == PromptStatus.OK && answer.StringResult == "File")
                {
                    var open = e.GetFileNameForOpen(new PromptOpenFileOptions("\nOur finished survey drawing") { Filter = "Drawing (*.dwg)|*.dwg" });
                    if (open.Status != PromptStatus.OK) return;
                    TrimSession.SurveySource.Mode = "File";
                    TrimSession.SurveySource.FilePath = open.StringResult;
                }
                else if (answer.Status == PromptStatus.OK)
                {
                    TrimSession.SurveySource.Mode = "Xrefs";
                    TrimSession.SurveySource.XrefNames = new List<string>();
                }

                var how = new PromptKeywordOptions("\nLimits of survey [Layer/Select] <Layer>: ", "Layer Select") { AllowNone = true };
                var which = e.GetKeywords(how);
                if (which.Status != PromptStatus.OK && which.Status != PromptStatus.None) return;
                ICollection<ObjectId> picked = null;
                if (which.Status == PromptStatus.OK && which.StringResult == "Select")
                {
                    var selection = e.GetSelection(new PromptSelectionOptions { MessageForAdding = "\nSelect the closed polyline(s) marking our limits of survey: " });
                    if (selection.Status != PromptStatus.OK) return;
                    picked = selection.Value.GetObjectIds();
                }
                TrimSession.Run(db, tr, e, picked);
            });
        }

        [CommandMethod("FTFTRIMRESTORE", CommandFlags.Modal)]
        public void Restore()
        {
            FtfSession.Run("FTFTRIMRESTORE", (db, tr, ed) => TrimSession.RestoreAll(db, tr, ed));
        }
    }
}
