using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace FieldCodes.Compare
{
    /// <summary>Progress and cancellation for a run. The CAD side shows stages on the
    /// command line and checks the user break; tests pass nothing.</summary>
    public interface ICompareProgress
    {
        void Stage(string name);
        void Step(int done, int total);
        bool IsCancelled { get; }
    }

    internal sealed class NoProgress : ICompareProgress
    {
        public void Stage(string name) { }
        public void Step(int done, int total) { }
        public bool IsCancelled { get { return false; } }
    }

    /// <summary>Everything the matchers share for one run.</summary>
    public sealed class MatchContext
    {
        public CompareProfile Profile;
        public CompareDataset Survey;
        public CompareDataset Consultant;
        public SpatialGrid ConsultantPoints;
        public SpatialGrid ConsultantLines;
        public SpatialGrid ConsultantAnnotation;
        public SpatialGrid SurveyPoints;
        public SpatialGrid SurveyLines;
        public Dictionary<string, CompareFeature> ConsultantById;
        public Dictionary<string, CompareFeature> SurveyById;
        public Dictionary<string, FeatureResult> ResultByFeatureId;
        /// <summary>Consultant feature id → annotation features that belong to it.</summary>
        public Dictionary<string, List<CompareFeature>> ConsultantAnnotationByFeature;
        public bool ConsultantIncomplete;
        public List<CPoint> Boundary;
        public Box2? ConsultantExtent;
        public double UnitsPerFoot = 1.0;

        public double Feet(double feet) { return feet * UnitsPerFoot; }

        /// <summary>Outside the comparison limits: the profile polygon when there is one,
        /// else (when enabled) the consultant data's own extent plus a margin.</summary>
        public bool IsOutsideLimits(CPoint p)
        {
            if (Boundary != null && Boundary.Count >= 3) return !Geometry2.PointInPolygon(p, Boundary);
            if (ConsultantExtent.HasValue) return !ConsultantExtent.Value.Contains(p);
            return false;
        }

        public bool HasLimits { get { return (Boundary != null && Boundary.Count >= 3) || ConsultantExtent.HasValue; } }
    }

    /// <summary>
    /// The comparison itself: classify both datasets, index the consultant's, then decide
    /// each survey feature -- points by position and type, linework by coverage along its
    /// length, annotation by the facts it carries against what the consultant shows at the
    /// same feature. Pure computation over extracted data; it never touches a drawing.
    /// </summary>
    public sealed class ComparisonEngine
    {
        private readonly CompareProfile _profile;

        public ComparisonEngine(CompareProfile profile)
        {
            if (profile == null) throw new ArgumentNullException("profile");
            _profile = profile;
        }

        public ComparisonResult Run(CompareDataset survey, CompareDataset consultant, ICompareProgress progress)
        {
            if (survey == null) throw new ArgumentNullException("survey");
            if (consultant == null) throw new ArgumentNullException("consultant");
            progress = progress ?? new NoProgress();
            var watch = Stopwatch.StartNew();

            var result = new ComparisonResult
            {
                ProfileName = _profile.Name,
                SurveyName = survey.Name,
                ConsultantName = consultant.Name,
                UnitsPerFoot = survey.UnitsPerFoot
            };

            // Units must already agree: the reader scales a consultant file only when the
            // profile says so explicitly, and never guesses.
            if (Math.Abs(survey.UnitsPerFoot - consultant.UnitsPerFoot) > 1e-9)
                throw new ConfigException(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "Drawing units differ: the survey has {0} unit(s) per foot and the consultant data {1}. " +
                    "Set units.consultantUnitsPerFoot in the comparison profile to confirm the conversion; nothing is scaled without it.",
                    survey.UnitsPerFoot, consultant.UnitsPerFoot));

            progress.Stage("Classifying features");
            var classifier = new FeatureClassifier(_profile);
            classifier.ClassifyAll(survey);
            classifier.ClassifyAll(consultant);

            foreach (var f in survey.Features.Where(f => f.Kind != FeatureKind.Annotation && f.Class.IsUnknown && !_profile.IsIgnored(f)))
                Count(result.UnknownSurveyLayers, f.Layer);
            foreach (var f in consultant.Features.Where(f => f.Kind != FeatureKind.Annotation && f.Class.IsUnknown && !_profile.IsIgnored(f)))
                Count(result.UnknownConsultantLayers, f.Layer);

            result.Issues.AddRange(survey.Issues);
            result.Issues.AddRange(consultant.Issues);
            result.FilesRead.AddRange(survey.FilesRead);
            result.FilesRead.AddRange(consultant.FilesRead);
            result.ConsultantIncomplete = consultant.Incomplete;
            result.ConsultantFeatures = consultant.Features.Count;
            if (result.UnknownConsultantLayers.Count > 0)
                result.Issues.Add(new DatasetIssue
                {
                    Severity = Severity.Warning,
                    Code = "UNKNOWN-LAYER",
                    Message = string.Format("{0} consultant object(s) on {1} layer(s) the profile does not map were not compared; survey features beside them are sent to review.",
                        result.UnknownConsultantLayers.Values.Sum(), result.UnknownConsultantLayers.Count)
                });

            progress.Stage("Indexing consultant data");
            var ctx = new MatchContext
            {
                Profile = _profile,
                Survey = survey,
                Consultant = consultant,
                UnitsPerFoot = survey.UnitsPerFoot,
                ConsultantIncomplete = consultant.Incomplete,
                ConsultantById = new Dictionary<string, CompareFeature>(StringComparer.Ordinal),
                SurveyById = new Dictionary<string, CompareFeature>(StringComparer.Ordinal),
                ResultByFeatureId = new Dictionary<string, FeatureResult>(StringComparer.Ordinal),
                ConsultantAnnotationByFeature = new Dictionary<string, List<CompareFeature>>(StringComparer.Ordinal)
            };
            foreach (var f in consultant.Features) ctx.ConsultantById[f.Id] = f;
            foreach (var f in survey.Features) ctx.SurveyById[f.Id] = f;

            var cell = ctx.Feet(50.0);
            var cPoints = consultant.Features.Where(f => f.Kind == FeatureKind.Point && !_profile.IsIgnored(f)).ToList();
            var cLines = consultant.Features.Where(f => f.Kind == FeatureKind.Line && !_profile.IsIgnored(f)).ToList();
            var cText = consultant.Features.Where(f => f.Kind == FeatureKind.Annotation && !_profile.IsIgnored(f)).ToList();
            ctx.ConsultantPoints = new SpatialGrid(cPoints, cell);
            ctx.ConsultantLines = new SpatialGrid(cLines, cell);
            ctx.ConsultantAnnotation = new SpatialGrid(cText, cell);
            var sPoints = survey.Features.Where(f => f.Kind == FeatureKind.Point && !_profile.IsIgnored(f)).ToList();
            var sLines = survey.Features.Where(f => f.Kind == FeatureKind.Line && !_profile.IsIgnored(f)).ToList();
            ctx.SurveyPoints = new SpatialGrid(sPoints, cell);
            ctx.SurveyLines = new SpatialGrid(sLines, cell);

            if (_profile.Boundary.HasPolygon) ctx.Boundary = _profile.Boundary.Polygon;
            else if (_profile.Boundary.UseConsultantExtentWhenNoBoundary && (cPoints.Count + cLines.Count) > 0)
            {
                var extent = Box2.Empty;
                foreach (var f in cPoints) extent = extent.Union(f.Bounds);
                foreach (var f in cLines) extent = extent.Union(f.Bounds);
                if (!extent.IsEmpty) ctx.ConsultantExtent = extent.Expand(ctx.Feet(_profile.Boundary.ConsultantExtentMarginFeet));
            }

            // Annotation belongs to a feature: its attribute owner, or the nearest feature
            // within the search radius. Both sides, so facts can be compared per feature.
            progress.Stage("Associating annotation");
            AnnotationMatcher.Associate(consultant, ctx.ConsultantPoints, ctx.ConsultantLines, ctx, _profile);
            AnnotationMatcher.Associate(survey, ctx.SurveyPoints, ctx.SurveyLines, ctx, _profile);
            foreach (var f in cText)
            {
                if (string.IsNullOrEmpty(f.AssociatedFeatureId)) continue;
                List<CompareFeature> list;
                if (!ctx.ConsultantAnnotationByFeature.TryGetValue(f.AssociatedFeatureId, out list))
                    ctx.ConsultantAnnotationByFeature[f.AssociatedFeatureId] = list = new List<CompareFeature>();
                list.Add(f);
            }
            foreach (var f in consultant.Features.Concat(survey.Features))
            {
                if (f.Facts != null) continue;
                if (f.Opaque) { f.Facts = new List<AnnotationFact>(); continue; }
                f.Facts = f.Kind == FeatureKind.Annotation
                    ? AnnotationParser.Parse(f.Description, _profile.Annotation)
                    : FactsFromAttributes(f);
            }
            foreach (var kv in consultant.HiddenCounts)
                result.Issues.Add(new DatasetIssue { Severity = Severity.Info, Code = "HIDDEN", Message = kv.Value + " consultant object(s) not shown (" + kv.Key + ") were left out: they cannot represent anything." });
            foreach (var kv in survey.HiddenCounts)
                result.Issues.Add(new DatasetIssue { Severity = Severity.Info, Code = "HIDDEN", Message = kv.Value + " survey object(s) not shown (" + kv.Key + ") were left out of the comparison." });

            // Points, then lines, then annotation (which needs its feature's result).
            var points = survey.Features.Where(f => f.Kind == FeatureKind.Point).ToList();
            var lines = survey.Features.Where(f => f.Kind == FeatureKind.Line).ToList();
            var texts = survey.Features.Where(f => f.Kind == FeatureKind.Annotation).ToList();

            progress.Stage("Comparing point features");
            var done = 0;
            foreach (var f in points)
            {
                if (progress.IsCancelled) throw new OperationCanceledException("Comparison cancelled.");
                Add(result, ctx, f, Evaluate(f, ctx, () => PointMatcher.Match(f, ctx)));
                if (++done % 200 == 0) progress.Step(done, points.Count);
            }

            progress.Stage("Comparing linework");
            done = 0;
            foreach (var f in lines)
            {
                if (progress.IsCancelled) throw new OperationCanceledException("Comparison cancelled.");
                Add(result, ctx, f, Evaluate(f, ctx, () => LinearMatcher.Match(f, ctx)));
                if (++done % 200 == 0) progress.Step(done, lines.Count);
            }

            progress.Stage("Comparing annotation");
            done = 0;
            foreach (var f in texts)
            {
                if (progress.IsCancelled) throw new OperationCanceledException("Comparison cancelled.");
                Add(result, ctx, f, Evaluate(f, ctx, () => AnnotationMatcher.Match(f, ctx)));
                if (++done % 500 == 0) progress.Step(done, texts.Count);
            }

            progress.Stage("Preparing review results");
            foreach (var r in result.Results) r.EffectiveStatus = r.Status;
            var shapeIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var r in result.Results)
                foreach (var id in r.MatchedIds)
                {
                    CompareFeature cf;
                    if (shapeIds.Add(id) && ctx.ConsultantById.TryGetValue(id, out cf))
                        result.ConsultantShapes.Add(ConsultantShape.From(cf));
                }
            result.ElapsedSeconds = watch.Elapsed.TotalSeconds;
            return result;
        }

        private FeatureResult Evaluate(CompareFeature f, MatchContext ctx, Func<FeatureResult> matcher)
        {
            FeatureResult r;
            if (_profile.IsIgnored(f))
            {
                r = FeatureResult.From(f);
                r.Status = MatchStatus.Excluded;
                r.Confidence = 1.0;
                r.Note("layer is on the profile's ignore list");
                return r;
            }
            if (!string.IsNullOrEmpty(f.ReadProblem))
            {
                r = FeatureResult.From(f);
                r.Status = MatchStatus.NotEvaluated;
                r.Note(f.ReadProblem);
                return r;
            }
            if (f.Kind != FeatureKind.Annotation && f.Class.IsUnknown)
            {
                r = FeatureResult.From(f);
                r.Status = MatchStatus.NotEvaluated;
                r.Note(f.Class.Evidence);
                return r;
            }
            return matcher();
        }

        private static void Add(ComparisonResult result, MatchContext ctx, CompareFeature f, FeatureResult r)
        {
            result.Results.Add(r);
            ctx.ResultByFeatureId[f.Id] = r;
        }

        private static void Count(Dictionary<string, int> counts, string layer)
        {
            int n;
            counts.TryGetValue(layer ?? string.Empty, out n);
            counts[layer ?? string.Empty] = n + 1;
        }

        private List<AnnotationFact> FactsFromAttributes(CompareFeature f)
        {
            var facts = new List<AnnotationFact>();
            if (f.Attributes == null) return facts;
            foreach (var kv in f.Attributes)
            {
                if (string.IsNullOrWhiteSpace(kv.Value)) continue;
                var tag = (kv.Key ?? string.Empty).ToUpperInvariant();
                // An identifier (point number, structure ID, name) is never a measured
                // value: a bare "501" must not be read as an elevation the consultant lacks.
                if (IsIdentifierTag(tag))
                {
                    facts.Add(new AnnotationFact { Key = "ID", Text = kv.Value });
                    continue;
                }
                // A tag that names the value is the strongest evidence of what it is.
                if (_profile.Annotation.RimKeywords.Any(k => tag.Contains(k.ToUpperInvariant())))
                {
                    var n = ParseNumber(kv.Value);
                    if (n.HasValue) { facts.Add(new AnnotationFact { Key = "RIM", Text = kv.Value, Number = n }); continue; }
                }
                if (_profile.Annotation.InvertKeywords.Any(k => tag.Contains(k.ToUpperInvariant().Replace(".", string.Empty))))
                {
                    var n = ParseNumber(kv.Value);
                    if (n.HasValue) { facts.Add(new AnnotationFact { Key = "IE", Text = kv.Value, Number = n }); continue; }
                }
                foreach (var fact in AnnotationParser.Parse(kv.Value, _profile.Annotation))
                    if (fact.Key != "TEXT") facts.Add(fact);
            }
            return facts;
        }

        internal static bool IsIdentifierTag(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return false;
            var t = tag.ToUpperInvariant().Replace("_", string.Empty).Replace("-", string.Empty).Replace(" ", string.Empty).Replace(".", string.Empty);
            return t == "ID" || t == "NO" || t == "NUM" || t == "NUMBER" || t == "NAME" || t == "HANDLE" || t == "TAG" || t == "PT" || t == "POINT" ||
                   t.EndsWith("ID", StringComparison.Ordinal) || t.EndsWith("NO", StringComparison.Ordinal) || t.EndsWith("NUM", StringComparison.Ordinal) ||
                   t.Contains("NUMBER") || t.Contains("NAME") || t.Contains("POINT");
        }

        private static double? ParseNumber(string s)
        {
            double v;
            return double.TryParse((s ?? string.Empty).Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v) ? v : (double?)null;
        }
    }
}
