using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using FieldCodes.Cad.Setup;
using FieldCodes.Settings;
using FieldCodes.Standards;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace FieldCodes.Cad
{
    /// <summary>
    /// FTFLAYERUPDATE: brings the layers FTF uses in this drawing to their layer standards
    /// (Settings > Layers) -- a preview first, then only on Yes. Layer properties only.
    ///
    /// FTFCHECKSTANDARDS: lists what the settings and rules name that this drawing does not have
    /// (layers, linetypes, text, multileader, table and dimension styles, Civil 3D label styles,
    /// blocks) with the choices for each, and opens a window to apply any of them.
    ///
    /// UNTESTED in Civil 3D: the decisions are FieldCodes.Settings.LayerStandards and
    /// FieldCodes.Standards.StandardsCheck, which are unit tested.
    /// </summary>
    public sealed class LayerStandardCommands
    {
        [CommandMethod("FTFLAYERUPDATE", CommandFlags.Modal)]
        public void FtfLayerUpdate()
        {
            FtfSession.Run("FTFLAYERUPDATE", (db, tr, ed) =>
            {
                var rules = FtfSession.Rules(db);
                var settings = FtfSession.SettingsFor(db, rules);
                var layers = LayerStandardsCad.ReadLayers(db, tr);
                var plan = LayerStandardsCad.Plan(rules, settings, layers, settings.LayerStandards, settings.General.LayerMappings);

                ed.WriteMessage("\nFTFLAYERUPDATE: the {0} layer(s) FTF uses in this drawing, against the layer standards (Settings > Layers).",
                                plan.LayersCompared);
                if ((settings.LayerStandards.Layers ?? new List<LayerStandard>()).Count == 0)
                {
                    ed.WriteMessage("\n  No layer standards are set. Set colour, linetype, lineweight and plot on Settings > Layers.\n");
                    return;
                }
                foreach (var problem in plan.Problems) ed.WriteMessage("\n  Not applied: {0}", problem);
                if (plan.Changes.Count == 0)
                {
                    ed.WriteMessage("\n  Every layer FTF uses already matches its standard ({0} have none). Nothing changed.\n", plan.LayersWithoutStandard);
                    return;
                }

                foreach (var layer in plan.Changes.GroupBy(c => c.Layer, StringComparer.OrdinalIgnoreCase))
                {
                    ed.WriteMessage("\n  {0}", layer.Key);
                    foreach (var c in layer)
                        ed.WriteMessage("\n      {0,-13}{1} -> {2}{3}", LayerStandards.PropertyName(c.Property), c.From, c.To,
                                        string.Equals(c.Standard, c.Layer, StringComparison.OrdinalIgnoreCase) ? string.Empty : "   (standard " + c.Standard + ")");
                }
                ed.WriteMessage("\n  {0} change(s) on {1} layer(s); {2} layer(s) have no standard. Nothing has changed yet.",
                                plan.Changes.Count, plan.LayersChanged, plan.LayersWithoutStandard);
                ed.WriteMessage("\n  Only these layers' properties change: no object is moved, restyled or erased, no layer is renamed, " +
                                "and layers FTF does not use are not touched.");

                var options = new PromptKeywordOptions("\nApply these changes? [Yes/No] <No>: ", "Yes No");
                options.Keywords.Default = "No";
                options.AllowNone = true;
                var answer = ed.GetKeywords(options);
                if (answer.Status != PromptStatus.OK || answer.StringResult != "Yes")
                {
                    ed.WriteMessage("\n  Nothing changed.\n");
                    return;
                }

                var result = LayerStandardsCad.ApplyChanges(db, tr, plan.Changes, settings.LayerStandards);
                ed.WriteMessage("\n" + LayerStandardsCad.Describe(result).Replace("\n", "\n  ") + "\n");
            });
        }

        [CommandMethod("FTFCHECKSTANDARDS", CommandFlags.Modal)]
        public void FtfCheckStandards()
        {
            IList<StandardsIssue> issues = null;
            SettingsResolution resolution = null;
            RulesConfig rules = null;

            FtfSession.Run("FTFCHECKSTANDARDS", (db, tr, ed) =>
            {
                try { rules = FtfSession.Rules(db); }
                catch (ConfigException ex)
                {
                    rules = null;
                    ed.WriteMessage("\n  Rules: {0} Codes, symbol labels and line features are not checked.", ex.Message);
                }
                resolution = FtfSession.ResolveSettings(db, rules);
                if (rules != null) resolution.Settings.ApplyTo(rules);
                issues = StandardsCheck.Run(rules, resolution.Settings, LayerStandardsCad.ReadNames(db, tr));

                ed.WriteMessage("\nFTFCHECKSTANDARDS: what the settings and rules name, against this drawing. {0}", StandardsCheck.Summary(issues));
                foreach (var issue in issues)
                {
                    ed.WriteMessage("\n  {0,-8}{1} {2} -- {3}", issue.Severity == FieldCodes.Severity.Error ? "ERROR" : issue.Severity == FieldCodes.Severity.Warning ? "WARNING" : "note",
                                    StandardsIssue.KindName(issue.Kind), issue.Name, issue.Problem);
                    ed.WriteMessage("\n          Referenced by: {0}", issue.What);
                    if (issue.Choices.Count > 0) ed.WriteMessage("\n          Choices: {0}", string.Join("; ", issue.Choices.ToArray()));
                }
                ed.WriteMessage("\n");
            });

            if (issues == null || resolution == null || !issues.Any(i => i.Fix != StandardsFix.None)) return;
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            ShowFixes(doc.Editor, issues, new CommandFixer(resolution));
        }

        /// <summary>The fix window. Kept in its own method that is never inlined, as the other commands' windows are:
        /// accoreconsole overflows its stack JIT-compiling a method that names a dialog type and ShowModalDialog.</summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void ShowFixes(Editor ed, IList<StandardsIssue> issues, IStandardsFixer fixer)
        {
            using (var form = new StandardsCheckForm(issues, fixer, "this drawing, with the settings in force"))
                AcadApp.ShowModalDialog(form);
            ed.WriteMessage("\nFTFCHECKSTANDARDS: run it again to see what is left.\n");
        }

        /// <summary>FTFCHECKSTANDARDS' fixes: drawing changes at once; a setting change saved to the settings
        /// file in force (or the user's own when the defaults are in use), as Ignore Code does.</summary>
        private sealed class CommandFixer : IStandardsFixer
        {
            private readonly SettingsResolution _resolution;

            public CommandFixer(SettingsResolution resolution) { _resolution = resolution; }

            public FixOutcome CreateLayer(StandardsIssue issue)
            {
                return LayerStandardsCad.CreateLayerNow(issue.Name, _resolution.Settings.LayerStandards);
            }

            public FixOutcome LoadLinetype(StandardsIssue issue)
            {
                return LayerStandardsCad.LoadLinetypeNow(issue.Name);
            }

            public FixOutcome MapLayer(StandardsIssue issue, string target)
            {
                var from = issue.ConfiguredLayer ?? issue.Name;
                LayerMap.Set(_resolution.Settings.General.LayerMappings, from, target);
                return Save("Layer " + from + " now goes to " + target);
            }

            public FixOutcome UseExisting(StandardsIssue issue, string name)
            {
                if (issue.Setting == null) return FixOutcome.Failed("This one is not a setting FTF can write.");
                issue.Setting.Write(name);
                return Save(string.Join("; ", issue.Setting.Fields.ToArray()) + " = " + name);
            }

            private FixOutcome Save(string what)
            {
                var path = DiagnosticsSettings.SavePathFor(_resolution);
                string failure = null;
                try { _resolution.Settings.Save(path); }
                catch (ConfigException ex) { failure = ex.Message; }
                catch (IOException ex) { failure = ex.Message; }
                catch (UnauthorizedAccessException ex) { failure = ex.Message; }
                // Every cache built from the settings (the layer mapping and standards) is rebuilt; after a
                // failure that also drops the unsaved change, since the next read is from the file.
                FtfSession.InvalidateSettings();
                return failure == null
                    ? FixOutcome.Ok(what + " (saved to " + path + ").")
                    : FixOutcome.Failed("Could not save to " + path + ": " + failure);
            }
        }
    }
}
