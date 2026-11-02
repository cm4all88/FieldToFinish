using System;
using System.Collections.Generic;
using System.Linq;
using FieldCodes.Annotation;
using FieldCodes.Settings;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace FieldCodes.Cad
{
    /// <summary>
    /// The workflows saved in Settings > Workflows, for the Process page and FTFRUN: which ones the
    /// drawing in front has, and the plan each runs. A workflow's follow-up commands are queued on
    /// the command line when its run ends (<see cref="AnnotationRun.FollowUp"/>), the same whether
    /// it ran from FTFRUN or the window. UNTESTED in Civil 3D; the plans are unit tested.
    /// </summary>
    internal static class FtfWorkflows
    {
        /// <summary>The saved workflows in the settings in force for the drawing in front; none
        /// when there is no drawing or its settings cannot be read.</summary>
        public static IList<WorkflowDefinition> Current()
        {
            try
            {
                var doc = AcadApp.DocumentManager.MdiActiveDocument;
                if (doc == null) return new List<WorkflowDefinition>();
                var settings = FtfSession.ResolveSettings(doc.Database, FtfSession.Rules(doc.Database)).Settings;
                return For(settings);
            }
            catch (System.Exception) { return new List<WorkflowDefinition>(); }
        }

        /// <summary>The named workflows in <paramref name="settings"/>, in their saved order.</summary>
        public static IList<WorkflowDefinition> For(FtfSettings settings)
        {
            if (settings == null || settings.Workflows == null || settings.Workflows.Items == null)
                return new List<WorkflowDefinition>();
            return settings.Workflows.Items.Where(w => w != null && !string.IsNullOrWhiteSpace(w.Name)).ToList();
        }

        /// <summary>The plan a workflow runs, built as a preset's is.</summary>
        public static ProcessPlan Plan(WorkflowDefinition workflow, AnnotationSettings annotation)
        {
            AnnotationRun.FollowUp = QueueFollowUps;
            return ProcessPresets.Build(workflow, annotation);
        }

        /// <summary>Queues a finished workflow's follow-up commands, each as if typed. They are on
        /// the allow-list because none of them stops to ask anything.</summary>
        private static void QueueFollowUps(ProcessPlan plan)
        {
            try
            {
                var doc = AcadApp.DocumentManager.MdiActiveDocument;
                if (doc == null || plan == null || plan.ThenRun.Count == 0) return;
                doc.Editor.WriteMessage("\n{0}: then {1}.\n", plan.Title, string.Join(", ", plan.ThenRun.ToArray()));
                foreach (var command in plan.ThenRun)
                    if (SettingsRegistry.WorkflowFollowUps.Contains(command))
                        doc.SendStringToExecute(command + " ", true, false, true);
            }
            catch (System.Exception)
            {
                // A follow-up that cannot be queued never undoes the run it follows.
            }
        }
    }
}
