using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FieldCodes.Settings;

namespace FieldCodes.Annotation
{
    /// <summary>What a run would do to one point's annotation.</summary>
    public enum PlanOutcome
    {
        /// <summary>No FTF label/tag there yet: one is drawn.</summary>
        Create,
        /// <summary>FTF's earlier label/tag there is replaced (same point, current text).</summary>
        Update,
        /// <summary>Left alone, on purpose: the reason says why.</summary>
        Skip,
        /// <summary>Needs attention: the description has errors, so nothing is drawn.</summary>
        Flag
    }

    public enum PlanKind { Label, Tag }

    /// <summary>One point as the CAD layer found it before a run.</summary>
    public sealed class AnnotationCandidate
    {
        public ParsedPoint Parsed { get; set; }
        /// <summary>FTF already drew this kind of annotation for the point.</summary>
        public bool HasFtfLabel { get; set; }
        public bool HasFtfTag { get; set; }
        /// <summary>The FTF label was moved by hand since FTF placed it: it is kept.</summary>
        public bool LabelMovedByHand { get; set; }
        /// <summary>Someone already typed a label for this point (not FTF's).</summary>
        public bool ManualLabelNearby { get; set; }
    }

    public sealed class PlannedAnnotation
    {
        public string PointNumber { get; set; }
        public string Code { get; set; }
        public AnnotationCategory Category { get; set; }
        public PlanKind Kind { get; set; }
        public PlanOutcome Outcome { get; set; }
        public string Reason { get; set; }
        public string Text { get; set; }
    }

    /// <summary>
    /// The preview Label All and Process Drawing show before anything changes: how many
    /// labels and tags would be created, updated, skipped or flagged, by family, and why.
    /// Built by the same <see cref="AnnotationPolicy"/> the commands use, so the preview
    /// cannot disagree with the run.
    /// </summary>
    public sealed class AnnotationPlan
    {
        public IList<PlannedAnnotation> Items { get; private set; }

        public AnnotationPlan() { Items = new List<PlannedAnnotation>(); }

        public int Count(PlanKind kind, PlanOutcome outcome)
        {
            return Items.Count(i => i.Kind == kind && i.Outcome == outcome);
        }

        public static AnnotationPlan Build(IEnumerable<AnnotationCandidate> candidates, AnnotationSettings settings,
                                           ICollection<AnnotationCategory> labelScope, ICollection<AnnotationCategory> tagScope,
                                           bool includeTags, RulesConfig rules = null)
        {
            var plan = new AnnotationPlan();
            foreach (var c in candidates ?? Enumerable.Empty<AnnotationCandidate>())
            {
                var p = c.Parsed;
                if (p == null || p.Ignored || p.NoContent || p.Unhandled) continue;
                var code = p.Code ?? FieldCodeParser.FirstToken(p.RawDescription ?? string.Empty);

                // ---- label
                if (p.HasErrors)
                {
                    plan.Items.Add(new PlannedAnnotation
                    {
                        PointNumber = p.PointNumber, Code = code, Category = AnnotationPolicy.CategoryOf(p, rules), Kind = PlanKind.Label,
                        Outcome = PlanOutcome.Flag,
                        Reason = p.Diagnostics.Where(d => d.Severity == Severity.Error).Select(d => d.Message).FirstOrDefault() ?? "description has errors"
                    });
                }
                else if (!string.IsNullOrEmpty(p.LabelText))
                {
                    var d = AnnotationPolicy.Label(p, settings, labelScope, rules);
                    var item = new PlannedAnnotation { PointNumber = p.PointNumber, Code = code, Category = d.Category, Kind = PlanKind.Label, Text = p.LabelText };
                    if (!d.Annotate) { item.Outcome = PlanOutcome.Skip; item.Reason = d.Reason; }
                    else if (c.LabelMovedByHand) { item.Outcome = PlanOutcome.Skip; item.Reason = "moved by hand: kept"; }
                    else if (c.ManualLabelNearby && !c.HasFtfLabel && (settings == null || settings.RespectManualLabels))
                    { item.Outcome = PlanOutcome.Skip; item.Reason = "already labelled by hand"; }
                    else item.Outcome = c.HasFtfLabel ? PlanOutcome.Update : PlanOutcome.Create;
                    plan.Items.Add(item);
                }

                // ---- tag
                if (includeTags && !string.IsNullOrEmpty(p.TagPrefix) && !p.HasErrors)
                {
                    var t = AnnotationPolicy.Tag(p, settings, tagScope, rules);
                    plan.Items.Add(new PlannedAnnotation
                    {
                        PointNumber = p.PointNumber, Code = code, Category = t.Category, Kind = PlanKind.Tag,
                        Outcome = !t.Annotate ? PlanOutcome.Skip : c.HasFtfTag ? PlanOutcome.Update : PlanOutcome.Create,
                        Reason = t.Reason
                    });
                }
            }
            return plan;
        }

        /// <summary>A few lines for the command line or the preview box.</summary>
        public IList<string> Summary(bool includeTags)
        {
            var lines = new List<string>();
            lines.Add(string.Format(CultureInfo.InvariantCulture,
                "Labels: {0} to create, {1} to update, {2} skipped, {3} flagged.",
                Count(PlanKind.Label, PlanOutcome.Create), Count(PlanKind.Label, PlanOutcome.Update),
                Count(PlanKind.Label, PlanOutcome.Skip), Count(PlanKind.Label, PlanOutcome.Flag)));
            if (includeTags)
                lines.Add(string.Format(CultureInfo.InvariantCulture,
                    "Tags: {0} to create, {1} to reissue, {2} skipped.",
                    Count(PlanKind.Tag, PlanOutcome.Create), Count(PlanKind.Tag, PlanOutcome.Update), Count(PlanKind.Tag, PlanOutcome.Skip)));
            foreach (var g in Items.Where(i => i.Outcome == PlanOutcome.Create || i.Outcome == PlanOutcome.Update)
                                   .GroupBy(i => i.Category).OrderBy(g => g.Key))
                lines.Add(string.Format(CultureInfo.InvariantCulture, "  {0}: {1} label(s), {2} tag(s)",
                    AnnotationPolicy.Name(g.Key), g.Count(i => i.Kind == PlanKind.Label), g.Count(i => i.Kind == PlanKind.Tag)));
            foreach (var g in Items.Where(i => i.Outcome == PlanOutcome.Skip).GroupBy(i => i.Reason).OrderByDescending(g => g.Count()))
                lines.Add(string.Format(CultureInfo.InvariantCulture, "  skipped, {0}: {1}", g.Key, g.Count()));
            foreach (var i in Items.Where(x => x.Outcome == PlanOutcome.Flag).Take(10))
                lines.Add(string.Format(CultureInfo.InvariantCulture, "  FLAG point {0} ({1}): {2}", i.PointNumber, i.Code, i.Reason));
            var more = Items.Count(x => x.Outcome == PlanOutcome.Flag) - 10;
            if (more > 0) lines.Add("  ... and " + more + " more flagged.");
            return lines;
        }
    }
}
