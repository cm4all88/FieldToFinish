using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FieldCodes.Compare
{
    public sealed class LayerState
    {
        public string Name { get; set; }
        public bool Exists { get; set; }
        public bool Plottable { get; set; }
        public bool Frozen { get; set; }
        public bool Off { get; set; }
    }

    public sealed class EntityState
    {
        public string Handle { get; set; }
        public bool Exists { get; set; }
        public string Layer { get; set; }
        /// <summary>Whether the object will actually plot, resolved from everything that
        /// decides it (visibility, its layer, a block definition's own layers, a COGO
        /// point's style). Null means it could not be determined, which fails the check.</summary>
        public bool? Plots { get; set; }
        public string PlotNote { get; set; }
    }

    public sealed class XrefState
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public bool Resolved { get; set; }
        public bool Loaded { get; set; }
    }

    /// <summary>Everything the final QC needs, gathered by the CAD side in one read-only pass.</summary>
    public sealed class QcInput
    {
        public CompareProfile Profile { get; set; }
        public ComparisonResult Result { get; set; }
        public ChangePlan Plan { get; set; }
        public AppliedChangeSet Applied { get; set; }
        /// <summary>Layer states by name (every nonplot layer, and every layer a result sits on).</summary>
        public Dictionary<string, LayerState> Layers { get; set; }
        /// <summary>Current state of every survey entity in the results and every split copy.</summary>
        public Dictionary<string, EntityState> Entities { get; set; }
        /// <summary>Split copies found in the drawing that no applied record owns: leftovers of an earlier run.</summary>
        public List<string> StaleSplitHandles { get; set; }
        public List<XrefState> Xrefs { get; set; }
        public string DrawingPath { get; set; }
        public string OutputFolder { get; set; }
        public bool OutputFolderWritable { get; set; }
        /// <summary>Non-blocking findings the CAD side gathered (a block whose definition
        /// draws on its own layers, COGO point styles with fixed layers).</summary>
        public List<string> Warnings { get; set; }

        public QcInput()
        {
            Layers = new Dictionary<string, LayerState>(StringComparer.OrdinalIgnoreCase);
            Entities = new Dictionary<string, EntityState>(StringComparer.OrdinalIgnoreCase);
            StaleSplitHandles = new List<string>();
            Xrefs = new List<XrefState>();
            Warnings = new List<string>();
        }
    }

    public sealed class QcCheck
    {
        public string Code { get; set; }
        public Severity Severity { get; set; }
        public bool Blocking { get; set; }
        public bool Passed { get; set; }
        public string Message { get; set; }

        public override string ToString()
        {
            return (Passed ? "[PASS] " : Blocking ? "[FAIL] " : "[WARN] ") + Code + ": " + Message;
        }
    }

    /// <summary>
    /// The final QC before a deliverable is written. Every check reports; a failed
    /// blocking check stops the save. A deliverable is never reported as good while a
    /// blocking check fails.
    /// </summary>
    public static class CompareQc
    {
        public static List<QcCheck> Evaluate(QcInput input)
        {
            var checks = new List<QcCheck>();
            if (input == null) throw new ArgumentNullException("input");
            var profile = input.Profile ?? new CompareProfile();
            var result = input.Result;

            // 1. All required sources evaluated.
            if (result == null)
            {
                Add(checks, "SOURCES", true, false, "No comparison has been run in this drawing.");
                return checks;
            }
            var incomplete = result.Issues.Where(i => i.MakesIncomplete).ToList();
            Add(checks, "SOURCES", profile.Qc.BlockOnIncompleteSources, incomplete.Count == 0 && !result.ConsultantIncomplete,
                incomplete.Count == 0 && !result.ConsultantIncomplete
                    ? string.Format(CultureInfo.InvariantCulture, "{0} source file(s) read; consultant data complete.", result.FilesRead.Distinct().Count())
                    : "Consultant data was incomplete when compared: " + string.Join("; ", incomplete.Select(i => i.Message).ToArray()));

            // 2. Unresolved critical discrepancies.
            var summary = result.Summary();
            Add(checks, "DISCREPANCIES", profile.Qc.BlockOnDiscrepancies, summary.Discrepancies == 0,
                summary.Discrepancies == 0 ? "No unresolved discrepancies." : summary.Discrepancies + " discrepancy(ies) await a decision.");
            Add(checks, "REVIEW", profile.Qc.BlockOnNeedsReview, summary.NeedsReview == 0,
                summary.NeedsReview == 0 ? "Nothing awaits review." : summary.NeedsReview + " feature(s) await review.");
            Add(checks, "NOT-EVALUATED", profile.Qc.BlockOnNotEvaluated, summary.NotEvaluated == 0,
                summary.NotEvaluated == 0 ? "Every feature was evaluated." : summary.NotEvaluated + " feature(s) could not be evaluated (unmapped layers or unreadable objects); they still plot.");

            // 3. Nonplot layer settings.
            var badLayers = new List<string>();
            var usedNonplot = result.Results.Where(r => r.Action != PlannedAction.None && !string.IsNullOrEmpty(r.NonplotLayer)).Select(r => r.NonplotLayer).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var name in profile.NonplotLayers.All)
            {
                LayerState state;
                var used = usedNonplot.Contains(name, StringComparer.OrdinalIgnoreCase);
                if (!input.Layers.TryGetValue(name, out state) || !state.Exists)
                {
                    if (used) badLayers.Add(name + " (missing)");
                    continue;
                }
                if (state.Plottable) badLayers.Add(name + " (plottable)");
            }
            Add(checks, "NONPLOT-LAYERS", true, badLayers.Count == 0,
                badLayers.Count == 0 ? "Comparison nonplot layers exist where used and do not plot." : "Nonplot layer problem: " + string.Join(", ", badLayers.ToArray()));

            // 4. Missing features will plot. 5. Matched duplicates will not.
            var notPlotting = new List<string>();
            var stillPlotting = new List<string>();
            var notApplied = new List<string>();
            var unverified = new List<string>();
            foreach (var r in result.Results)
            {
                EntityState entity;
                input.Entities.TryGetValue(r.Handle, out entity);
                if (r.EffectiveStatus == MatchStatus.Missing || (r.Action == PlannedAction.None && r.EffectiveStatus != MatchStatus.Excluded && r.EffectiveStatus != MatchStatus.Matched))
                {
                    if (entity == null) continue;
                    if (!entity.Exists) { notPlotting.Add(r.Handle + " (erased)"); continue; }
                    var plots = Plots(entity, input, profile);
                    if (plots == false) notPlotting.Add(r.Handle + " (" + (entity.PlotNote ?? "will not plot") + ")");
                    else if (plots == null) unverified.Add(r.Handle + " (" + (entity.PlotNote ?? "plot state unknown") + ")");
                }
                if (r.Action == PlannedAction.Nonplot || r.Action == PlannedAction.SplitNonplot)
                {
                    var applied = input.Applied != null ? input.Applied.Find(r.Handle) : null;
                    if (applied == null) { notApplied.Add(r.Handle + (string.IsNullOrEmpty(r.ApplyProblem) ? string.Empty : " (" + r.ApplyProblem + ")")); continue; }
                    if (entity == null) continue;
                    if (!entity.Exists) { stillPlotting.Add(r.Handle + " (erased)"); continue; }
                    var plots = Plots(entity, input, profile);
                    if (plots == true) stillPlotting.Add(r.Handle + " (" + (entity.PlotNote ?? "still plots") + ")");
                    else if (plots == null) unverified.Add(r.Handle + " (" + (entity.PlotNote ?? "plot state unknown") + ")");
                    if (r.Action == PlannedAction.SplitNonplot)
                        foreach (var h in applied.SplitHandles)
                        {
                            EntityState copy;
                            if (!input.Entities.TryGetValue(h, out copy) || !copy.Exists) continue;
                            var copyPlots = Plots(copy, input, profile);
                            if (copyPlots == false) notPlotting.Add(h + " (split copy of " + r.Handle + ": " + (copy.PlotNote ?? "will not plot") + ")");
                            else if (copyPlots == null) unverified.Add(h + " (split copy of " + r.Handle + ")");
                        }
                }
            }
            Add(checks, "MISSING-PLOT", true, notPlotting.Count == 0,
                notPlotting.Count == 0 ? "Every feature meant for delivery is on a plotting layer." : notPlotting.Count + " feature(s) meant to plot will not: " + Preview(notPlotting));
            Add(checks, "APPLIED", true, notApplied.Count == 0,
                notApplied.Count == 0 ? "Every planned change has been applied and verified." : notApplied.Count + " planned change(s) not applied (run Apply, or decide the ones it refused): " + Preview(notApplied));
            Add(checks, "DUPLICATES-NONPLOT", true, stillPlotting.Count == 0,
                stillPlotting.Count == 0 ? "Every matched duplicate will not plot (layer, style and block content checked)." : stillPlotting.Count + " matched duplicate(s) still plot: " + Preview(stillPlotting));
            Add(checks, "PLOT-VERIFIED", true, unverified.Count == 0,
                unverified.Count == 0 ? "The plot state of every object was determined." : unverified.Count + " object(s) whose plot state could not be determined: " + Preview(unverified));

            // 6. Duplicate additions from previous runs.
            var missingSplits = new List<string>();
            if (input.Applied != null)
                foreach (var change in input.Applied.Changes)
                    foreach (var h in change.SplitHandles)
                    {
                        EntityState e;
                        if (!input.Entities.TryGetValue(h, out e) || !e.Exists) missingSplits.Add(h);
                    }
            Add(checks, "PREVIOUS-RUNS", true, input.StaleSplitHandles.Count == 0,
                input.StaleSplitHandles.Count == 0 ? "No leftover geometry from earlier runs." : input.StaleSplitHandles.Count + " split copy(ies) from an earlier run are not owned by the current record (run Restore, then Apply again): " + Preview(input.StaleSplitHandles));
            if (missingSplits.Count > 0)
                Add(checks, "SPLIT-COPIES", false, false, missingSplits.Count + " split copy(ies) recorded as created were erased by hand: " + Preview(missingSplits));

            // 7. Xrefs.
            var unresolved = input.Xrefs.Where(x => !x.Resolved).ToList();
            var unloaded = input.Xrefs.Where(x => x.Resolved && !x.Loaded).ToList();
            Add(checks, "XREFS", true, unresolved.Count == 0,
                unresolved.Count == 0
                    ? (input.Xrefs.Count == 0 ? "No external references." : input.Xrefs.Count + " external reference(s) resolve.")
                    : "Unresolved external reference(s): " + string.Join(", ", unresolved.Select(x => x.Name + " -> " + x.Path).ToArray()));
            if (unloaded.Count > 0)
                Add(checks, "XREFS-UNLOADED", false, false, "Unloaded external reference(s): " + string.Join(", ", unloaded.Select(x => x.Name).ToArray()));

            // 8. Output can be written.
            Add(checks, "OUTPUT", true, !string.IsNullOrEmpty(input.DrawingPath) && (string.IsNullOrEmpty(input.OutputFolder) || input.OutputFolderWritable),
                string.IsNullOrEmpty(input.DrawingPath) ? "The drawing has never been saved."
                : string.IsNullOrEmpty(input.OutputFolder) ? "No delivery folder set; the save will ask for one."
                : input.OutputFolderWritable ? "Delivery folder is writable: " + input.OutputFolder
                : "Delivery folder cannot be written: " + input.OutputFolder);

            foreach (var warning in input.Warnings ?? new List<string>())
                Add(checks, "WARNING", false, false, warning);

            return checks;
        }

        /// <summary>The object's real plot state when the gatherer resolved it; otherwise
        /// the layer-based answer, which is all a core-only caller can know.</summary>
        private static bool? Plots(EntityState entity, QcInput input, CompareProfile profile)
        {
            if (entity.Plots.HasValue) return entity.Plots;
            if (entity.PlotNote != null) return null;     // resolved, and found undeterminable
            if (profile.NonplotLayers.Contains(entity.Layer)) return false;
            LayerState layer;
            if (input.Layers.TryGetValue(entity.Layer ?? string.Empty, out layer))
                return layer.Exists && layer.Plottable && !layer.Frozen && !layer.Off;
            return true;
        }

        public static bool Passes(IEnumerable<QcCheck> checks)
        {
            return checks != null && checks.All(c => c.Passed || !c.Blocking);
        }

        public static string Render(IEnumerable<QcCheck> checks)
        {
            var list = (checks ?? new List<QcCheck>()).ToList();
            var lines = list.Select(c => "  " + c.ToString()).ToList();
            lines.Insert(0, Passes(list) ? "Final QC: PASSED (" + list.Count(c => !c.Passed) + " warning(s))" : "Final QC: FAILED - " + list.Count(c => !c.Passed && c.Blocking) + " blocking problem(s)");
            return string.Join(Environment.NewLine, lines.ToArray());
        }

        private static void Add(List<QcCheck> checks, string code, bool blocking, bool passed, string message)
        {
            checks.Add(new QcCheck { Code = code, Blocking = blocking, Passed = passed, Severity = passed ? Severity.Info : blocking ? Severity.Error : Severity.Warning, Message = message });
        }

        private static string Preview(List<string> items)
        {
            return string.Join(", ", items.Take(8).ToArray()) + (items.Count > 8 ? ", ..." : string.Empty);
        }
    }
}
