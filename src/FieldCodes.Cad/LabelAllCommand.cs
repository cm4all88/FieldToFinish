using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.Runtime;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace FieldCodes.Cad
{
    /// <summary>
    /// FTFLABELALL: one click after the points are imported and Civil 3D has built the
    /// linework. Labels every recognised line (the whole-drawing line pass, run even when
    /// the automatic pass is off in Settings > Line Labels, with gate labels between marked
    /// shots), then every point, then re-applies draw order -- lines first, because point
    /// labels steer around line labels. Labels placed or moved by hand are kept. Safe to run
    /// again after more linework comes in: each stage replaces only its own earlier output.
    ///
    /// FTFRUN is the whole finishing pass (driplines, tags, the schedule as well); this is
    /// only the labelling.
    ///
    /// UNTESTED against a drawing. The pieces it runs are the existing FTFLINELABELS,
    /// FTFLABELS and FTFORDER.
    /// </summary>
    public sealed class LabelAllCommand
    {
        private sealed class Stage
        {
            public string Title;
            public Action Run;
        }

        [CommandMethod("FTFLABELALL", CommandFlags.Modal)]
        public void LabelAll()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;

            var linework = new LineworkCommands();
            var points = new LabelCommands();
            var order = new DrawOrderCommands();
            var stages = new List<Stage>
            {
                new Stage { Title = "Linework labels", Run = () => linework.RunLineLabels(true) },
                new Stage { Title = "Point labels", Run = points.FtfLabels },
                new Stage { Title = "Draw order", Run = order.FtfOrder }
            };

            ed.WriteMessage("\n=== FTFLABELALL: label everything -- linework, then points ===");
            var failed = new List<string>();
            using (FtfSession.Batch())
            {
                for (var i = 0; i < stages.Count; i++)
                {
                    var stage = stages[i];
                    ed.WriteMessage("\n--- [{0}/{1}] {2} ---", i + 1, stages.Count, stage.Title);
                    stage.Run();
                    if (FtfSession.LastRunError != null) failed.Add(stage.Title + ": " + FtfSession.LastRunError);
                }
            }

            // Once, after all three: the screen then shows every label above its mask.
            FtfSession.RegenIfPending(ed);

            ed.WriteMessage("\n\n=== FTFLABELALL finished ===");
            var report = LineworkCommands.LastReport;
            if (report != null)
            {
                ed.WriteMessage("\nLinework: {0} label(s) on {1} line(s).", report.LabelsPlaced, report.FeaturesLabelled);
                if (report.Unlabelled > 0)
                    ed.WriteMessage("\n{0} identified line(s) were not labelled; the reasons are listed under FTFLINELABELS above.", report.Unlabelled);
            }
            foreach (var f in failed) ed.WriteMessage("\nFAILED -- " + f);
            ed.WriteMessage(failed.Count == 0
                ? "\nLabels placed or moved by hand were kept. Run FTFLABELALL again any time; it replaces only its own labels.\n"
                : "\nFix the failure above and run FTFLABELALL again; nothing it already placed duplicates.\n");
        }
    }
}
