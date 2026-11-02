using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using FieldCodes.Annotation;
using FieldCodes.Settings;

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

        /// <summary>What a run would do, without changing anything: labels and tags to create,
        /// update, skip or flag, by family and reason.</summary>
        private static void Preview()
        {
            FtfSession.Run("FTFLABELALL", (db, tr, ed) =>
            {
                var cfg = FtfSession.Rules(db);
                var settings = FtfSession.SettingsFor(db, cfg);
                var ann = settings.Annotation;
                var parser = new FieldCodeParser(cfg);
                var scan = AnnotationScan.Read(db, tr, cfg, settings, parser, CogoNotes.For(db, tr, settings));
                scan.KeptByHand = LabelCommands.KeepHandMovedLabels(db, tr, settings.Cleanup.MovedTolerance);
                var labelScope = new HashSet<AnnotationCategory>(ann.Categories ?? new List<AnnotationCategory>());
                var tagTrees = ann.TreeAnnotation == FieldCodes.Settings.TreeAnnotation.TagOnly && labelScope.Contains(AnnotationCategory.Trees);
                var tagScope = tagTrees ? new HashSet<AnnotationCategory> { AnnotationCategory.Trees } : new HashSet<AnnotationCategory>();
                var plan = scan.Plan(ann, labelScope, tagScope, tagTrees);

                ed.WriteMessage("\n=== FTFLABELALL preview: nothing is changed ===");
                foreach (var line in plan.Summary(tagTrees)) ed.WriteMessage("\n" + line);
                ed.WriteMessage(labelScope.Contains(AnnotationCategory.Lines)
                    ? string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "\nLine labels: recognised lines of {0:0.#} ft or more (Settings > Line Labels).", settings.LineLabels.MinLengthFeet)
                    : "\nLine labels: not run (line features are not selected in Settings > Label All).");
                if (scan.DepthNotesKeptOut > 0)
                    ed.WriteMessage("\n{0} point(s) would be labelled from their code: their note is a dip measurement.", scan.DepthNotesKeptOut);
                ed.WriteMessage("\nFTFLABELALL > Run to label; Settings > Label All to change what is included.\n");
            });
        }

        [CommandMethod("FTFLABELALL", CommandFlags.Modal)]
        public void LabelAll()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;

            var ask = new PromptKeywordOptions("\nLabel All [Preview/Run] <Run>: ") { AllowNone = true };
            ask.Keywords.Add("Preview");
            ask.Keywords.Add("Run");
            ask.Keywords.Default = "Run";
            var answer = ed.GetKeywords(ask);
            if (answer.Status != PromptStatus.OK && answer.Status != PromptStatus.None) return;
            if (answer.Status == PromptStatus.OK && answer.StringResult == "Preview")
            {
                Preview();
                return;
            }

            // What Label All covers: the families ticked in Settings > Label All; line labels
            // only when line features are ticked; trees by tag instead of label when trees are
            // set to Tag only (never both).
            FieldCodes.Settings.AnnotationSettings ann = null;
            FtfSession.Run("FTFLABELALL", (db, tr, e) => { ann = FtfSession.SettingsFor(db, FtfSession.Rules(db)).Annotation; });
            if (ann == null) return;
            var plan = new ProcessPlan
            {
                Preset = ProcessPreset.Custom,
                LabelFamilies = new HashSet<AnnotationCategory>(ann.Categories ?? new List<AnnotationCategory>())
            };
            var tagTrees = ann.TreeAnnotation == FieldCodes.Settings.TreeAnnotation.TagOnly && plan.LabelFamilies.Contains(AnnotationCategory.Trees);
            if (tagTrees) plan.TagFamilies.Add(AnnotationCategory.Trees);

            var linework = new LineworkCommands();
            var points = new LabelCommands();
            var tags = new TagCommands();
            var order = new DrawOrderCommands();
            var stages = new List<Stage>();
            if (plan.LabelFamilies.Contains(AnnotationCategory.Lines))
                stages.Add(new Stage { Title = "Linework labels", Run = () => linework.RunLineLabels(true) });
            stages.Add(new Stage { Title = "Point labels", Run = points.FtfLabels });
            if (tagTrees) stages.Add(new Stage { Title = "Tree tags", Run = tags.FtfTags });
            stages.Add(new Stage { Title = "Draw order", Run = order.FtfOrder });

            ed.WriteMessage("\n=== FTFLABELALL: label everything -- linework, then points ===");
            var failed = new List<string>();
            using (AnnotationRun.Use(plan))
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
