using System;
using System.Collections.Generic;
using System.Linq;
using FieldCodes.Settings;

namespace FieldCodes.Annotation
{
    /// <summary>The finishing stages, by their command names, in the only order they run.</summary>
    public static class ProcessStage
    {
        public const string Points = "FTFPOINTS";
        public const string Orient = "FTFORIENT";
        public const string Drip = "FTFDRIP";
        public const string LineLabels = "FTFLINELABELS";
        public const string Labels = "FTFLABELS";
        public const string Tags = "FTFTAGS";
        public const string Table = "FTFTABLE";
        public const string Order = "FTFORDER";

        public static readonly IList<string> InOrder = new List<string>
        {
            Points, Orient, Drip, LineLabels, Labels, Tags, Table, Order
        }.AsReadOnly();

        /// <summary>Stages that draw annotation or symbols draw order has to band afterwards.</summary>
        public static bool NeedsDrawOrder(string stage)
        {
            return stage == Drip || stage == LineLabels || stage == Labels || stage == Tags || stage == Table;
        }
    }

    public enum ProcessPreset
    {
        /// <summary>Point finishing, driplines and labels; no tree schedule, no tags unless trees
        /// are tag-annotated, no storm/sewer dip annotation.</summary>
        StandardTopographic = 0,
        /// <summary>Trees only: driplines, and the tree annotation the settings ask for (label or
        /// tag), the schedule if it is on.</summary>
        TreesAndVegetation = 1,
        /// <summary>Re-annotates what is already finished: labels, tags and schedule where they
        /// apply, then draw order. Driplines and symbol rotations are not touched.</summary>
        AnnotationsOnly = 2,
        /// <summary>The stages and families picked by hand.</summary>
        Custom = 3
    }

    /// <summary>What one Process Drawing run will do: the stages, and which families the label
    /// and tag stages cover. Nothing outside it is created, replaced or erased.</summary>
    public sealed class ProcessPlan
    {
        public ProcessPreset Preset { get; set; }
        public IList<string> Stages { get; set; }
        public ISet<AnnotationCategory> LabelFamilies { get; set; }
        public ISet<AnnotationCategory> TagFamilies { get; set; }
        /// <summary>Stage changes made to respect dependencies, in words.</summary>
        public IList<string> Adjustments { get; set; }

        public ProcessPlan()
        {
            Stages = new List<string>();
            LabelFamilies = new HashSet<AnnotationCategory>();
            TagFamilies = new HashSet<AnnotationCategory>();
            Adjustments = new List<string>();
        }

        public bool Runs(string stage) { return Stages.Contains(stage); }
    }

    /// <summary>
    /// Builds the plan for each preset from the office's settings, and puts any hand-picked
    /// selection into a safe order: draw order last, and only when something it bands ran;
    /// a schedule only from tags it can find.
    /// </summary>
    public static class ProcessPresets
    {
        public static string Title(ProcessPreset preset)
        {
            switch (preset)
            {
                case ProcessPreset.StandardTopographic: return "Standard Topographic Finishing";
                case ProcessPreset.TreesAndVegetation: return "Trees and Vegetation";
                case ProcessPreset.AnnotationsOnly: return "Annotations Only";
                default: return "Custom";
            }
        }

        public static string Describe(ProcessPreset preset)
        {
            switch (preset)
            {
                case ProcessPreset.StandardTopographic:
                    return "Point finishing, driplines and labels. No tree schedule, no tags unless trees are set to tags, no storm/sewer dip annotation.";
                case ProcessPreset.TreesAndVegetation:
                    return "Trees only: driplines and the tree label or tag the settings choose, and the schedule if it is on.";
                case ProcessPreset.AnnotationsOnly:
                    return "Refreshes FTF labels, tags and the schedule. Driplines and symbol rotations are not touched.";
                default:
                    return "The stages and feature families you pick.";
            }
        }

        public static ProcessPlan Build(ProcessPreset preset, AnnotationSettings settings,
                                        IEnumerable<string> customStages = null,
                                        IEnumerable<AnnotationCategory> customFamilies = null)
        {
            var s = settings ?? new AnnotationSettings();
            var configured = new HashSet<AnnotationCategory>(s.Categories ?? new List<AnnotationCategory>());
            var plan = new ProcessPlan { Preset = preset };
            var stages = new List<string>();
            var treesTagged = s.TreeAnnotation == TreeAnnotation.TagOnly;

            switch (preset)
            {
                case ProcessPreset.StandardTopographic:
                    stages.AddRange(new[] { ProcessStage.Points, ProcessStage.Drip, ProcessStage.LineLabels, ProcessStage.Labels });
                    plan.LabelFamilies = new HashSet<AnnotationCategory>(configured.Where(c => c != AnnotationCategory.StormSewer));
                    if (treesTagged && configured.Contains(AnnotationCategory.Trees))
                    {
                        stages.Add(ProcessStage.Tags);
                        plan.TagFamilies = new HashSet<AnnotationCategory> { AnnotationCategory.Trees };
                    }
                    break;

                case ProcessPreset.TreesAndVegetation:
                    stages.AddRange(new[] { ProcessStage.Drip, ProcessStage.Labels });
                    plan.LabelFamilies = new HashSet<AnnotationCategory> { AnnotationCategory.Trees };
                    if (treesTagged)
                    {
                        stages.Add(ProcessStage.Tags);
                        plan.TagFamilies = new HashSet<AnnotationCategory> { AnnotationCategory.Trees };
                    }
                    if (s.ScheduleTrees) stages.Add(ProcessStage.Table);
                    break;

                case ProcessPreset.AnnotationsOnly:
                    stages.AddRange(new[] { ProcessStage.LineLabels, ProcessStage.Labels, ProcessStage.Tags });
                    plan.LabelFamilies = new HashSet<AnnotationCategory>(configured);
                    plan.TagFamilies = new HashSet<AnnotationCategory>(configured);
                    if (s.ScheduleTrees || treesTagged) stages.Add(ProcessStage.Table);
                    break;

                default:
                    stages.AddRange(customStages ?? Enumerable.Empty<string>());
                    var families = customFamilies != null ? new HashSet<AnnotationCategory>(customFamilies) : configured;
                    plan.LabelFamilies = new HashSet<AnnotationCategory>(families);
                    plan.TagFamilies = new HashSet<AnnotationCategory>(families);
                    break;
            }

            // Line labels are a family like the others: off means the line pass is not run.
            if (!plan.LabelFamilies.Contains(AnnotationCategory.Lines) && stages.Remove(ProcessStage.LineLabels))
                plan.Adjustments.Add("Line labels skipped: line features are not selected.");
            if (plan.LabelFamilies.Count == 0 && stages.Remove(ProcessStage.Labels))
                plan.Adjustments.Add("Point labels skipped: no feature family is selected.");
            if (plan.TagFamilies.Count == 0 && stages.Remove(ProcessStage.Tags))
                plan.Adjustments.Add("Tags skipped: no feature family is selected.");

            plan.Stages = Normalize(stages, plan.Adjustments);
            return plan;
        }

        /// <summary>Known stages only, each once, in pipeline order; draw order last and only
        /// when a stage it bands runs.</summary>
        public static IList<string> Normalize(IEnumerable<string> stages, IList<string> adjustments)
        {
            var wanted = new HashSet<string>((stages ?? Enumerable.Empty<string>()).Where(x => x != null).Select(x => x.Trim().ToUpperInvariant()));
            foreach (var unknown in wanted.Where(w => !ProcessStage.InOrder.Contains(w)).ToList())
            {
                wanted.Remove(unknown);
                if (adjustments != null) adjustments.Add("Unknown stage " + unknown + " ignored.");
            }
            var needsOrder = wanted.Any(ProcessStage.NeedsDrawOrder);
            if (needsOrder && !wanted.Contains(ProcessStage.Order))
            {
                wanted.Add(ProcessStage.Order);
                if (adjustments != null) adjustments.Add("Draw order added: it has to run after anything that draws annotation.");
            }
            if (!needsOrder && wanted.Count > 1 && wanted.Contains(ProcessStage.Order) && adjustments != null)
                adjustments.Add("Draw order runs on its own: nothing selected draws annotation.");
            if (wanted.Contains(ProcessStage.Table) && !wanted.Contains(ProcessStage.Tags) && adjustments != null)
                adjustments.Add("The schedule is built from the tags already in the drawing.");
            return ProcessStage.InOrder.Where(wanted.Contains).ToList();
        }
    }

    /// <summary>
    /// The families the running stage covers. Process Drawing sets it for the length of a
    /// run so FTFLABELS and FTFTAGS -- which take no arguments -- annotate, replace and erase
    /// only inside the preset's families. With no run in progress the settings decide.
    /// </summary>
    public static class AnnotationRun
    {
        [ThreadStatic] private static ProcessPlan _current;

        public static ProcessPlan Current { get { return _current; } }

        public static IDisposable Use(ProcessPlan plan)
        {
            var previous = _current;
            _current = plan;
            return new Restore(() => _current = previous);
        }

        public static ICollection<AnnotationCategory> LabelScope(AnnotationSettings settings)
        {
            if (_current != null) return _current.LabelFamilies;
            return new HashSet<AnnotationCategory>((settings ?? new AnnotationSettings()).Categories ?? new List<AnnotationCategory>());
        }

        public static ICollection<AnnotationCategory> TagScope(AnnotationSettings settings)
        {
            if (_current != null) return _current.TagFamilies;
            return new HashSet<AnnotationCategory>((settings ?? new AnnotationSettings()).Categories ?? new List<AnnotationCategory>());
        }

        private sealed class Restore : IDisposable
        {
            private Action _undo;
            public Restore(Action undo) { _undo = undo; }
            public void Dispose() { if (_undo != null) { _undo(); _undo = null; } }
        }
    }
}
