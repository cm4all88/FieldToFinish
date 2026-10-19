using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace FieldCodes.Compare
{
    /// <summary>What the comparison concluded about one survey feature.</summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum MatchStatus
    {
        /// <summary>The consultant already shows it. Ours goes nonplot.</summary>
        Matched,
        /// <summary>Not represented in the consultant's drawing. Ours keeps plotting.</summary>
        Missing,
        /// <summary>Part of our linework is represented, part is not.</summary>
        Partial,
        /// <summary>Same object, but a value disagrees (elevation, size, type). Review.</summary>
        Discrepancy,
        /// <summary>Could not be decided with confidence. Review.</summary>
        NeedsReview,
        /// <summary>Could not be compared: unknown layer, unsupported object, unresolved
        /// reference. Keeps plotting; reported. NOT evidence of anything.</summary>
        NotEvaluated,
        /// <summary>Left out of the comparison: ignored layer, outside the limits, or
        /// excluded by the reviewer.</summary>
        Excluded
    }

    /// <summary>A reviewer's decision about one result.</summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum ReviewDecision { None, AcceptMatch, KeepPlotting, MakeNonplot, AcceptPartial, Exclude }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum IntervalKind { Covered, Uncovered, OutsideLimits }

    /// <summary>What applying the result does to the survey object.</summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum PlannedAction { None, Nonplot, SplitNonplot }

    /// <summary>A stretch of a survey line, by station from its start.</summary>
    public sealed class CoverageInterval
    {
        public double Start { get; set; }
        public double End { get; set; }
        public IntervalKind Kind { get; set; }
        /// <summary>Consultant feature(s) covering it, for Covered intervals.</summary>
        public List<string> CoveredBy { get; set; }
        public double MeanOffset { get; set; }

        /// <summary>The interval's ends as points on our line, so the split can be made at
        /// the real curve's closest points rather than by station arithmetic.</summary>
        public CPoint StartPoint { get; set; }
        public CPoint EndPoint { get; set; }
        /// <summary>Farthest any sample in the interval sits from the nearest consultant
        /// line of the same category (for uncovered intervals: how far off we are).</summary>
        public double MaxDistance { get; set; }

        public CoverageInterval() { CoveredBy = new List<string>(); }

        [JsonIgnore] public double Length { get { return Math.Max(0, End - Start); } }

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "{0} {1:0.0}-{2:0.0}", Kind, Start, End);
        }
    }

    public sealed class FeatureResult
    {
        public string FeatureId { get; set; }
        public string Handle { get; set; }
        public string OwnerPath { get; set; }
        public FeatureKind Kind { get; set; }
        public string EntityType { get; set; }
        public string Category { get; set; }
        public string System { get; set; }
        public string SourceLayer { get; set; }
        public string Description { get; set; }
        public string BlockName { get; set; }
        public CPoint Location { get; set; }
        public Box2 Bounds { get; set; }
        public double Length { get; set; }
        public string Fingerprint { get; set; }

        public MatchStatus Status { get; set; }
        /// <summary>0..1: how sure the automatic result is.</summary>
        public double Confidence { get; set; }
        public List<string> Notes { get; set; }

        /// <summary>Consultant features this one corresponds to.</summary>
        public List<string> MatchedIds { get; set; }
        public List<string> MatchedHandles { get; set; }
        public double? HorizontalOffset { get; set; }
        public double? VerticalDifference { get; set; }

        /// <summary>Linework only: station intervals, in order, covering the whole length.</summary>
        public List<CoverageInterval> Intervals { get; set; }
        public double CoveredLength { get; set; }
        public double UncoveredLength { get; set; }
        public double OutsideLength { get; set; }

        /// <summary>Annotation: the feature it belongs to.</summary>
        public string AssociatedFeatureId { get; set; }

        public ReviewDecision Decision { get; set; }
        public string DecisionNote { get; set; }

        /// <summary>Set by the planner: where the object goes, if anywhere.</summary>
        public PlannedAction Action { get; set; }
        public string NonplotLayer { get; set; }
        /// <summary>Set by the planner: the status after the decision is taken into account.</summary>
        public MatchStatus EffectiveStatus { get; set; }

        /// <summary>Why the last apply refused this change (the object would still plot,
        /// its plot state could not be determined, a point group overrides its style...).
        /// The object was left untouched; the plan still wants it, so QC blocks until a
        /// person decides.</summary>
        public string ApplyProblem { get; set; }

        public FeatureResult()
        {
            Notes = new List<string>();
            MatchedIds = new List<string>();
            MatchedHandles = new List<string>();
            Intervals = new List<CoverageInterval>();
        }

        public void Note(string text)
        {
            if (!string.IsNullOrEmpty(text) && !Notes.Contains(text)) Notes.Add(text);
        }

        [JsonIgnore] public string NotesText { get { return string.Join("; ", Notes.ToArray()); } }

        [JsonIgnore]
        public bool HasOutsideLimits { get { return Intervals.Any(i => i.Kind == IntervalKind.OutsideLimits && i.Length > 0); } }

        public static FeatureResult From(CompareFeature f)
        {
            var r = new FeatureResult
            {
                FeatureId = f.Id,
                Handle = f.Handle,
                OwnerPath = f.OwnerPath ?? string.Empty,
                Kind = f.Kind,
                EntityType = f.EntityType,
                Category = f.Class != null ? f.Class.Category : string.Empty,
                System = f.Class != null ? f.Class.System : string.Empty,
                SourceLayer = f.Layer,
                Description = f.Description ?? string.Empty,
                BlockName = f.BlockName ?? string.Empty,
                Bounds = f.Bounds,
                Length = f.Kind == FeatureKind.Line ? f.Length : 0,
                Fingerprint = f.Fingerprint(),
                AssociatedFeatureId = f.AssociatedFeatureId
            };
            if (f.Kind == FeatureKind.Line && f.Vertices != null && f.Vertices.Count > 0)
                r.Location = Geometry2.PointAtStation(f.Vertices, f.Stations, f.Length / 2);
            else
                r.Location = f.Position;
            return r;
        }
    }

    public sealed class ComparisonSummary
    {
        public int Total { get; set; }
        public int Matched { get; set; }
        public int Missing { get; set; }
        public int Partial { get; set; }
        public int Discrepancies { get; set; }
        public int NeedsReview { get; set; }
        public int NotEvaluated { get; set; }
        public int Excluded { get; set; }
        public int Decided { get; set; }

        [JsonIgnore] public int Unresolved { get { return Discrepancies + NeedsReview + NotEvaluated; } }

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture,
                "{0} evaluated: {1} matched, {2} missing, {3} partial, {4} discrepancies, {5} need review, {6} not evaluated, {7} excluded",
                Total, Matched, Missing, Partial, Discrepancies, NeedsReview, NotEvaluated, Excluded);
        }
    }

    /// <summary>The whole run: every survey feature's result plus what limited the run.</summary>
    public sealed class ComparisonResult
    {
        public string RunId { get; set; }
        public DateTime RunUtc { get; set; }
        public string ProfileName { get; set; }
        public string SurveyName { get; set; }
        public string ConsultantName { get; set; }
        public double UnitsPerFoot { get; set; }
        public List<FeatureResult> Results { get; set; }
        public List<DatasetIssue> Issues { get; set; }
        /// <summary>True when consultant data could not be fully read: "missing" was
        /// downgraded to "not evaluated" throughout.</summary>
        public bool ConsultantIncomplete { get; set; }
        public Dictionary<string, int> UnknownSurveyLayers { get; set; }
        public Dictionary<string, int> UnknownConsultantLayers { get; set; }
        public int ConsultantFeatures { get; set; }
        public List<string> FilesRead { get; set; }
        public double ElapsedSeconds { get; set; }
        /// <summary>Geometry of every consultant feature that some result matched.</summary>
        public List<ConsultantShape> ConsultantShapes { get; set; }

        public ComparisonResult()
        {
            RunId = Guid.NewGuid().ToString("N").Substring(0, 12);
            RunUtc = DateTime.UtcNow;
            Results = new List<FeatureResult>();
            Issues = new List<DatasetIssue>();
            UnknownSurveyLayers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            UnknownConsultantLayers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            FilesRead = new List<string>();
            ConsultantShapes = new List<ConsultantShape>();
            UnitsPerFoot = 1.0;
        }

        public FeatureResult Find(string handle)
        {
            return Results.FirstOrDefault(r => string.Equals(r.Handle, handle, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Counts by the EFFECTIVE status (decisions applied, plan built).</summary>
        public ComparisonSummary Summary()
        {
            var s = new ComparisonSummary { Total = Results.Count };
            foreach (var r in Results)
            {
                switch (r.EffectiveStatus)
                {
                    case MatchStatus.Matched: s.Matched++; break;
                    case MatchStatus.Missing: s.Missing++; break;
                    case MatchStatus.Partial: s.Partial++; break;
                    case MatchStatus.Discrepancy: s.Discrepancies++; break;
                    case MatchStatus.NeedsReview: s.NeedsReview++; break;
                    case MatchStatus.NotEvaluated: s.NotEvaluated++; break;
                    case MatchStatus.Excluded: s.Excluded++; break;
                }
                if (r.Decision != ReviewDecision.None) s.Decided++;
            }
            return s;
        }

        public string ToJson() { return JsonConvert.SerializeObject(this, Formatting.None, CompareJson.Settings); }

        public static ComparisonResult FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            return JsonConvert.DeserializeObject<ComparisonResult>(json, CompareJson.Settings);
        }
    }

    // ===================================================================== decisions

    public sealed class DecisionEntry
    {
        public string Handle { get; set; }
        public string Fingerprint { get; set; }
        public ReviewDecision Decision { get; set; }
        public string Note { get; set; }
        public DateTime DecidedUtc { get; set; }
        /// <summary>The automatic status at the time, for the audit trail.</summary>
        public MatchStatus StatusWhenDecided { get; set; }
    }

    /// <summary>
    /// Review decisions, persistent and reusable. Each is tied to the feature's
    /// fingerprint, so after a re-run (a revised consultant drawing, a new profile) a
    /// decision is re-applied only while the survey feature itself is unchanged.
    /// </summary>
    public sealed class ReviewDecisionSet
    {
        public const string Suffix = ".ftf-compare-decisions.json";

        public List<DecisionEntry> Entries { get; set; }

        public ReviewDecisionSet() { Entries = new List<DecisionEntry>(); }

        public static string PathFor(string drawingPath)
        {
            if (string.IsNullOrWhiteSpace(drawingPath)) return null;
            try
            {
                var dir = Path.GetDirectoryName(drawingPath);
                var name = Path.GetFileNameWithoutExtension(drawingPath);
                if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(name)) return null;
                return Path.Combine(dir, name + Suffix);
            }
            catch (ArgumentException) { return null; }
        }

        public DecisionEntry Find(string handle)
        {
            return Entries.FirstOrDefault(e => string.Equals(e.Handle, handle, StringComparison.OrdinalIgnoreCase));
        }

        public void Set(FeatureResult result, ReviewDecision decision, string note)
        {
            if (result == null) return;
            Entries.RemoveAll(e => string.Equals(e.Handle, result.Handle, StringComparison.OrdinalIgnoreCase));
            if (decision == ReviewDecision.None)
            {
                result.Decision = ReviewDecision.None;
                result.DecisionNote = null;
                return;
            }
            Entries.Add(new DecisionEntry
            {
                Handle = result.Handle,
                Fingerprint = result.Fingerprint,
                Decision = decision,
                Note = note,
                DecidedUtc = DateTime.UtcNow,
                StatusWhenDecided = result.Status
            });
            result.Decision = decision;
            result.DecisionNote = note;
        }

        /// <summary>Re-applies stored decisions to a fresh result. Returns how many applied;
        /// decisions whose feature changed are reported in <paramref name="stale"/> and
        /// left unapplied -- the reviewer looks again.</summary>
        public int ApplyTo(ComparisonResult result, ICollection<string> stale)
        {
            var applied = 0;
            if (result == null) return 0;
            foreach (var entry in Entries)
            {
                var r = result.Find(entry.Handle);
                if (r == null) continue;
                if (!string.Equals(r.Fingerprint, entry.Fingerprint, StringComparison.Ordinal))
                {
                    r.Note("earlier decision (" + entry.Decision + ") not applied: the feature changed since it was made");
                    if (stale != null) stale.Add(entry.Handle);
                    continue;
                }
                r.Decision = entry.Decision;
                r.DecisionNote = entry.Note;
                applied++;
            }
            return applied;
        }

        public string ToJson() { return JsonConvert.SerializeObject(this, Formatting.Indented, CompareJson.Settings); }

        public static ReviewDecisionSet FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new ReviewDecisionSet();
            var set = JsonConvert.DeserializeObject<ReviewDecisionSet>(json, CompareJson.Settings) ?? new ReviewDecisionSet();
            if (set.Entries == null) set.Entries = new List<DecisionEntry>();
            return set;
        }
    }

    // ========================================================================= plan

    public sealed class PlannedChange
    {
        public string Handle { get; set; }
        public FeatureKind Kind { get; set; }
        public string Category { get; set; }
        public string FromLayer { get; set; }
        public string ToLayer { get; set; }
        public PlannedAction Action { get; set; }
        /// <summary>For a split: the stations to KEEP PLOTTING (copied to the original layer).</summary>
        public List<CoverageInterval> KeepIntervals { get; set; }
        public bool AutoDecided { get; set; }

        public PlannedChange() { KeepIntervals = new List<CoverageInterval>(); }

        /// <summary>A stable text form of the kept stations, compared against what was applied.</summary>
        [JsonIgnore]
        public string IntervalsKey
        {
            get
            {
                return Action + ":" + string.Join(";", KeepIntervals.Select(i =>
                    i.Start.ToString("0.00", CultureInfo.InvariantCulture) + "-" + i.End.ToString("0.00", CultureInfo.InvariantCulture)).ToArray());
            }
        }
    }

    /// <summary>The geometry of a consultant feature our result matched, kept so the review
    /// window can draw it without opening the consultant drawing again.</summary>
    public sealed class ConsultantShape
    {
        public string Id { get; set; }
        public string Handle { get; set; }
        public string OwnerPath { get; set; }
        public string Layer { get; set; }
        public bool IsPoint { get; set; }
        public CPoint Position { get; set; }
        public List<CPoint> Vertices { get; set; }

        public static ConsultantShape From(CompareFeature f)
        {
            return new ConsultantShape
            {
                Id = f.Id, Handle = f.Handle, OwnerPath = f.OwnerPath, Layer = f.Layer,
                IsPoint = f.Kind != FeatureKind.Line, Position = f.Position,
                Vertices = f.Kind == FeatureKind.Line && f.Vertices != null ? new List<CPoint>(f.Vertices) : null
            };
        }
    }

    public sealed class ChangePlan
    {
        public List<PlannedChange> Changes { get; set; }
        public int KeptPlotting { get; set; }
        public int AwaitingReview { get; set; }

        public ChangePlan() { Changes = new List<PlannedChange>(); }

        [JsonIgnore] public int NonplotCount { get { return Changes.Count(c => c.Action == PlannedAction.Nonplot); } }
        [JsonIgnore] public int SplitCount { get { return Changes.Count(c => c.Action == PlannedAction.SplitNonplot); } }
    }

    /// <summary>
    /// Turns results and decisions into the exact layer changes to make. Conservative by
    /// construction: only a confident automatic match, or an explicit decision, moves
    /// anything. Everything else keeps plotting until a person says otherwise.
    /// </summary>
    public static class ChangePlanner
    {
        public static ChangePlan Build(ComparisonResult result, CompareProfile profile)
        {
            if (result == null) throw new ArgumentNullException("result");
            if (profile == null) throw new ArgumentNullException("profile");
            var plan = new ChangePlan();
            var threshold = profile.Tolerances.AutoAcceptConfidence;

            foreach (var r in result.Results)
            {
                r.Action = PlannedAction.None;
                r.NonplotLayer = null;
                var category = profile.Category(r.Category);
                var nonplot = NonplotLayerFor(r, category, profile);
                var effective = r.Status;
                PlannedAction action = PlannedAction.None;
                var auto = false;

                switch (r.Decision)
                {
                    case ReviewDecision.AcceptMatch:
                    case ReviewDecision.MakeNonplot:
                        // "They have it": everything inside the limits goes nonplot; a portion
                        // beyond the limits plots only when the profile says so.
                        effective = MatchStatus.Matched;
                        action = OutsideKeep(r, profile).Count == 0 ? PlannedAction.Nonplot : PlannedAction.SplitNonplot;
                        break;
                    case ReviewDecision.AcceptPartial:
                        if (KeepIntervals(r, profile, true).Count == 0) { effective = MatchStatus.Matched; action = PlannedAction.Nonplot; }
                        else { effective = MatchStatus.Partial; action = PlannedAction.SplitNonplot; }
                        break;
                    case ReviewDecision.KeepPlotting:
                        // Resolved: ours plots as though the consultant did not have it.
                        effective = MatchStatus.Missing;
                        action = PlannedAction.None;
                        break;
                    case ReviewDecision.Exclude:
                        effective = MatchStatus.Excluded;
                        break;
                    default:
                        auto = true;
                        if (r.Kind == FeatureKind.Line && r.HasOutsideLimits && profile.Boundary.OutsideLimitsAction == OutsideLimitsAction.Review &&
                            (r.Status == MatchStatus.Matched || r.Status == MatchStatus.Partial))
                        {
                            effective = MatchStatus.NeedsReview;
                            r.Note("continues beyond the comparison limits: decide whether that portion plots");
                        }
                        else if (r.Status == MatchStatus.Matched)
                        {
                            if (r.Confidence >= threshold)
                                action = KeepIntervals(r, profile, false).Count == 0 ? PlannedAction.Nonplot : PlannedAction.SplitNonplot;
                            else effective = MatchStatus.NeedsReview;
                        }
                        else if (r.Status == MatchStatus.Partial)
                        {
                            if (r.Confidence >= threshold)
                                action = KeepIntervals(r, profile, false).Count == 0 ? PlannedAction.Nonplot : PlannedAction.SplitNonplot;
                            else effective = MatchStatus.NeedsReview;
                        }
                        break;
                }

                r.EffectiveStatus = effective;
                r.Action = action;
                if (action == PlannedAction.None)
                {
                    if (effective == MatchStatus.NeedsReview || effective == MatchStatus.Discrepancy || effective == MatchStatus.NotEvaluated) plan.AwaitingReview++;
                    else plan.KeptPlotting++;
                    continue;
                }

                r.NonplotLayer = nonplot;
                var change = new PlannedChange
                {
                    Handle = r.Handle,
                    Kind = r.Kind,
                    Category = r.Category,
                    FromLayer = r.SourceLayer,
                    ToLayer = nonplot,
                    Action = action,
                    AutoDecided = auto
                };
                if (action == PlannedAction.SplitNonplot)
                    change.KeepIntervals = r.Decision == ReviewDecision.AcceptMatch || r.Decision == ReviewDecision.MakeNonplot
                        ? OutsideKeep(r, profile)
                        : KeepIntervals(r, profile, r.Decision == ReviewDecision.AcceptPartial);
                plan.Changes.Add(change);
            }
            return plan;
        }

        /// <summary>The nonplot layer for a result: text for annotation, utility for utility
        /// systems, else line / block / point.</summary>
        public static string NonplotLayerFor(FeatureResult r, CategoryRule category, CompareProfile profile)
        {
            if (r.Kind == FeatureKind.Annotation) return profile.NonplotLayers.Text;
            if (category != null && category.IsUtility) return profile.NonplotLayers.Utility;
            if (r.Kind == FeatureKind.Line) return profile.NonplotLayers.Line;
            return !string.IsNullOrEmpty(r.BlockName) ? profile.NonplotLayers.Block : profile.NonplotLayers.Point;
        }

        /// <summary>Intervals that stay on the plotting layer: the uncovered ones, plus the
        /// outside-limits ones when the profile (or the reviewer's partial acceptance,
        /// which keeps them) says they plot.</summary>
        public static List<CoverageInterval> KeepIntervals(FeatureResult r, CompareProfile profile, bool reviewerAccepted)
        {
            var keep = new List<CoverageInterval>();
            foreach (var i in r.Intervals)
            {
                if (i.Length <= 0) continue;
                if (i.Kind == IntervalKind.Uncovered) keep.Add(i);
                else if (i.Kind == IntervalKind.OutsideLimits)
                {
                    var plots = profile.Boundary.OutsideLimitsAction == OutsideLimitsAction.KeepPlotting ||
                                (profile.Boundary.OutsideLimitsAction == OutsideLimitsAction.Review && reviewerAccepted);
                    if (plots) keep.Add(i);
                }
            }
            return MergeAdjacent(keep);
        }

        /// <summary>Only the outside-limits portions, and only when the profile keeps them plotting.</summary>
        private static List<CoverageInterval> OutsideKeep(FeatureResult r, CompareProfile profile)
        {
            if (profile.Boundary.OutsideLimitsAction != OutsideLimitsAction.KeepPlotting) return new List<CoverageInterval>();
            return MergeAdjacent(r.Intervals.Where(i => i.Kind == IntervalKind.OutsideLimits && i.Length > 0).ToList());
        }

        private static List<CoverageInterval> MergeAdjacent(List<CoverageInterval> intervals)
        {
            var merged = new List<CoverageInterval>();
            foreach (var i in intervals.OrderBy(x => x.Start))
            {
                if (merged.Count > 0 && i.Start - merged[merged.Count - 1].End <= 1e-6)
                {
                    var last = merged[merged.Count - 1];
                    if (i.End > last.End) { last.End = i.End; last.EndPoint = i.EndPoint; }
                    continue;
                }
                merged.Add(new CoverageInterval { Start = i.Start, End = i.End, Kind = IntervalKind.Uncovered, StartPoint = i.StartPoint, EndPoint = i.EndPoint, MaxDistance = i.MaxDistance });
            }
            return merged;
        }
    }

    // ====================================================================== applied

    /// <summary>What was really changed in the drawing, with everything needed to put it back.</summary>
    public sealed class AppliedChange
    {
        public string Handle { get; set; }
        public FeatureKind Kind { get; set; }
        public string Category { get; set; }
        public string EntityType { get; set; }
        public string OriginalLayer { get; set; }
        public string OriginalColor { get; set; }
        public string OriginalLinetype { get; set; }
        public string OriginalLineweight { get; set; }
        public string OriginalPlotStyle { get; set; }
        public string OriginalTransparency { get; set; }
        public string NonplotLayer { get; set; }
        public PlannedAction Action { get; set; }
        /// <summary>Entities created to keep the uncovered portions plotting.</summary>
        public List<string> SplitHandles { get; set; }
        /// <summary>The kept stations the split was made from, so a changed plan is detected.</summary>
        public string IntervalsKey { get; set; }
        /// <summary>Attribute handle → its original layer, for block attributes moved with their block.</summary>
        public Dictionary<string, string> AttributeLayers { get; set; }
        /// <summary>True when a split had to be approximated with a 3D polyline (closed or Civil 3D curve).</summary>
        public bool SplitApproximated { get; set; }

        /// <summary>How the object was made nonplot: Layer, LayerAndPointStyle, BlockTwin,
        /// Invisible. Restore reverses each.</summary>
        public string Method { get; set; }
        /// <summary>Entity visibility before the change (Invisible method).</summary>
        public bool? OriginalVisible { get; set; }
        /// <summary>Block definition the insert pointed at before it was switched to a nonplot twin (handle).</summary>
        public string OriginalBlockDefinition { get; set; }
        public string TwinBlockDefinition { get; set; }
        /// <summary>COGO point: the point style before the nonplot style was assigned (handle), and label visibility.</summary>
        public string OriginalPointStyle { get; set; }
        public bool? OriginalLabelVisible { get; set; }
        /// <summary>True when the drawing was checked right after the change and the object
        /// really does not plot. False is never stored: a change that could not be verified
        /// is reverted instead.</summary>
        public bool Verified { get; set; }

        public AppliedChange() { SplitHandles = new List<string>(); AttributeLayers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); }
    }

    public sealed class AppliedChangeSet
    {
        public string RunId { get; set; }
        public DateTime AppliedUtc { get; set; }
        public string Profile { get; set; }
        public string DrawingPath { get; set; }
        public List<AppliedChange> Changes { get; set; }

        public AppliedChangeSet() { Changes = new List<AppliedChange>(); }

        public AppliedChange Find(string handle)
        {
            return Changes.FirstOrDefault(c => string.Equals(c.Handle, handle, StringComparison.OrdinalIgnoreCase));
        }

        public string ToJson() { return JsonConvert.SerializeObject(this, Formatting.None, CompareJson.Settings); }

        public static AppliedChangeSet FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            var set = JsonConvert.DeserializeObject<AppliedChangeSet>(json, CompareJson.Settings);
            if (set != null && set.Changes == null) set.Changes = new List<AppliedChange>();
            return set;
        }
    }
}
