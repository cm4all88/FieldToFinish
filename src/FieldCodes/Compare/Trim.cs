using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace FieldCodes.Compare
{
    // ======================================================================= footprints

    /// <summary>A closed polygon marking our limits of survey: inside it, our map replaces
    /// the aerial base.</summary>
    public sealed class Footprint
    {
        public string Id { get; set; }
        /// <summary>Where it came from: an xref name, or "picked".</summary>
        public string Source { get; set; }
        public string Layer { get; set; }
        public List<CPoint> Polygon { get; set; }

        public Footprint() { Polygon = new List<CPoint>(); }

        [JsonIgnore] public Box2 Bounds { get { return Polygon == null || Polygon.Count == 0 ? Box2.Empty : Box2.Of(Polygon); } }

        public static bool LooksClosed(IList<CPoint> v)
        {
            if (v == null || v.Count < 3) return false;
            var first = v[0];
            var last = v[v.Count - 1];
            return Math.Abs(first.X - last.X) < 0.01 && Math.Abs(first.Y - last.Y) < 0.01 ? v.Count >= 4 : true;
        }
    }

    /// <summary>Exact station intervals of a polyline inside and outside a set of footprints:
    /// every segment is cut where it crosses a footprint edge, and each piece is classed by
    /// where its midpoint lies.</summary>
    public static class FootprintClipper
    {
        public static bool Inside(CPoint p, IList<Footprint> footprints)
        {
            if (footprints == null) return false;
            foreach (var fp in footprints)
            {
                if (fp.Polygon == null || fp.Polygon.Count < 3) continue;
                var b = fp.Bounds;
                if (p.X < b.MinX - 1e-9 || p.X > b.MaxX + 1e-9 || p.Y < b.MinY - 1e-9 || p.Y > b.MaxY + 1e-9) continue;
                if (Geometry2.PointInPolygon(p, fp.Polygon)) return true;
            }
            return false;
        }

        public static List<CoverageInterval> Intervals(IList<CPoint> v, double[] stations, IList<Footprint> footprints)
        {
            var result = new List<CoverageInterval>();
            if (v == null || v.Count == 0 || stations == null || stations.Length == 0) return result;
            var length = stations[stations.Length - 1];
            if (v.Count == 1 || length <= 1e-9)
            {
                result.Add(new CoverageInterval { Start = 0, End = length, Kind = Inside(v[0], footprints) ? IntervalKind.Covered : IntervalKind.Uncovered, StartPoint = v[0], EndPoint = v[v.Count - 1] });
                return result;
            }

            var cuts = new List<double> { 0, length };
            for (var i = 0; i + 1 < v.Count; i++)
            {
                var a = v[i];
                var b = v[i + 1];
                var segLen = stations[i + 1] - stations[i];
                if (segLen <= 1e-12) continue;
                var segMinX = Math.Min(a.X, b.X); var segMaxX = Math.Max(a.X, b.X);
                var segMinY = Math.Min(a.Y, b.Y); var segMaxY = Math.Max(a.Y, b.Y);
                foreach (var fp in footprints)
                {
                    if (fp.Polygon == null || fp.Polygon.Count < 3) continue;
                    var fb = fp.Bounds;
                    if (segMaxX < fb.MinX || segMinX > fb.MaxX || segMaxY < fb.MinY || segMinY > fb.MaxY) continue;
                    var poly = fp.Polygon;
                    for (var j = 0; j < poly.Count; j++)
                    {
                        var c = poly[j];
                        var d = poly[(j + 1) % poly.Count];
                        double t, u;
                        if (Geometry2.SegmentsIntersect(a, b, c, d, out t, out u))
                            cuts.Add(stations[i] + Math.Max(0, Math.Min(1, t)) * segLen);
                    }
                }
            }
            cuts.Sort();
            var unique = new List<double>();
            foreach (var s in cuts) if (unique.Count == 0 || s - unique[unique.Count - 1] > 1e-6) unique.Add(s);

            for (var k = 0; k + 1 < unique.Count; k++)
            {
                var s0 = unique[k];
                var s1 = unique[k + 1];
                if (s1 - s0 <= 1e-9) continue;
                var mid = Geometry2.PointAtStation(v, stations, (s0 + s1) / 2.0);
                var kind = Inside(mid, footprints) ? IntervalKind.Covered : IntervalKind.Uncovered;
                var last = result.Count > 0 ? result[result.Count - 1] : null;
                if (last != null && last.Kind == kind)
                {
                    last.End = s1;
                    last.EndPoint = Geometry2.PointAtStation(v, stations, s1);
                    continue;
                }
                result.Add(new CoverageInterval
                {
                    Start = s0, End = s1, Kind = kind,
                    StartPoint = Geometry2.PointAtStation(v, stations, s0),
                    EndPoint = Geometry2.PointAtStation(v, stations, s1)
                });
            }
            return result;
        }
    }

    // ============================================================================ results

    [JsonConverter(typeof(StringEnumConverter))]
    public enum TrimDisposition
    {
        /// <summary>Not inside any footprint: untouched.</summary>
        Outside,
        /// <summary>Entirely inside: goes to its nonplot layer.</summary>
        Inside,
        /// <summary>Crosses a footprint edge: the original goes nonplot and the outside part is copied back plotting.</summary>
        Split,
        /// <summary>Inside or across an edge but of a kind that cannot be cut (hatch, dimension, unreadable): left alone, reported.</summary>
        Straddles
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum SeamStatus { Aligned, Discrepancy, NoContinuation, NeedsReview }

    public sealed class TrimEntry
    {
        public string Handle { get; set; }
        public string EntityType { get; set; }
        public string Layer { get; set; }
        public FeatureKind Kind { get; set; }
        public string Category { get; set; }
        public TrimDisposition Disposition { get; set; }
        public double InsideLength { get; set; }
        public double OutsideLength { get; set; }
        public string NonplotLayer { get; set; }
        public string Note { get; set; }
        /// <summary>Set after apply when the change was refused.</summary>
        public string ApplyProblem { get; set; }
    }

    public sealed class SeamFinding
    {
        public string Id { get; set; }
        public string FootprintId { get; set; }
        public string AerialHandle { get; set; }
        public string AerialLayer { get; set; }
        public string Category { get; set; }
        public CPoint Cut { get; set; }
        public string SurveyHandle { get; set; }
        public string SurveyLayer { get; set; }
        public double? Offset { get; set; }
        public double? ElevationDifference { get; set; }
        public double? AngleDegrees { get; set; }
        public SeamStatus Status { get; set; }
        public string Note { get; set; }
    }

    public sealed class TrimSummary
    {
        public int Entities, Inside, Split, Outside, Straddles, Seams, Aligned, Discrepancies, NoContinuation, Review;
    }

    public sealed class TrimResult
    {
        public string RunId { get; set; }
        public DateTime RunUtc { get; set; }
        public string ProfileName { get; set; }
        public string HostName { get; set; }
        public string SurveyName { get; set; }
        public double UnitsPerFoot { get; set; }
        public List<Footprint> Footprints { get; set; }
        public List<TrimEntry> Entries { get; set; }
        public List<SeamFinding> Seams { get; set; }
        public List<DatasetIssue> Issues { get; set; }
        /// <summary>The planned changes in the form the apply/restore machinery reads
        /// (one FeatureResult per aerial object that changes, with its intervals).</summary>
        public ComparisonResult Features { get; set; }

        public TrimResult()
        {
            Footprints = new List<Footprint>();
            Entries = new List<TrimEntry>();
            Seams = new List<SeamFinding>();
            Issues = new List<DatasetIssue>();
        }

        public TrimSummary Summary()
        {
            var s = new TrimSummary { Entities = Entries.Count, Seams = Seams.Count };
            foreach (var e in Entries)
                switch (e.Disposition)
                {
                    case TrimDisposition.Inside: s.Inside++; break;
                    case TrimDisposition.Split: s.Split++; break;
                    case TrimDisposition.Outside: s.Outside++; break;
                    case TrimDisposition.Straddles: s.Straddles++; break;
                }
            foreach (var f in Seams)
                switch (f.Status)
                {
                    case SeamStatus.Aligned: s.Aligned++; break;
                    case SeamStatus.Discrepancy: s.Discrepancies++; break;
                    case SeamStatus.NoContinuation: s.NoContinuation++; break;
                    case SeamStatus.NeedsReview: s.Review++; break;
                }
            return s;
        }

        public string ToJson() { return JsonConvert.SerializeObject(this, Formatting.None, CompareJson.Settings); }

        public static TrimResult FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            return JsonConvert.DeserializeObject<TrimResult>(json, CompareJson.Settings);
        }
    }

    // ============================================================================ planner

    /// <summary>
    /// Decides what happens to every aerial object given our footprints: inside goes
    /// nonplot, a crossing line is split exactly at the footprint edge with the outside
    /// part kept plotting, outside is untouched. At every cut the seam is checked against
    /// our continuing line. Pure geometry: nothing here touches a drawing.
    /// </summary>
    public static class TrimPlanner
    {
        public static TrimResult Plan(CompareDataset aerial, CompareDataset survey, IList<Footprint> footprints, CompareProfile profile, out ChangePlan plan)
        {
            if (aerial == null) throw new ArgumentNullException("aerial");
            if (profile == null) throw new ArgumentNullException("profile");
            footprints = footprints ?? new List<Footprint>();
            var upf = aerial.UnitsPerFoot <= 0 ? 1.0 : aerial.UnitsPerFoot;
            var result = new TrimResult
            {
                RunId = Guid.NewGuid().ToString("N").Substring(0, 8),
                RunUtc = DateTime.UtcNow,
                ProfileName = profile.Name,
                HostName = aerial.Name,
                SurveyName = survey != null ? survey.Name : null,
                UnitsPerFoot = upf,
                Footprints = footprints.ToList()
            };
            result.Features = new ComparisonResult
            {
                RunId = result.RunId, RunUtc = result.RunUtc, ProfileName = profile.Name,
                SurveyName = aerial.Name, ConsultantName = result.SurveyName, UnitsPerFoot = upf
            };
            plan = new ChangePlan();
            if (footprints.Count == 0)
                result.Issues.Add(new DatasetIssue { Severity = Severity.Error, Code = "NO-FOOTPRINT", Message = "No limits-of-survey polygon was found; nothing is trimmed.", MakesIncomplete = true });
            foreach (var issue in aerial.Issues) result.Issues.Add(issue);
            if (survey != null) foreach (var issue in survey.Issues) result.Issues.Add(issue);

            var classifier = new FeatureClassifier(profile);
            foreach (var f in aerial.Features) classifier.Classify(f);
            var surveyLines = new List<CompareFeature>();
            if (survey != null)
                foreach (var f in survey.Features)
                {
                    classifier.Classify(f);
                    if (f.Kind == FeatureKind.Line && f.Vertices != null && f.Vertices.Count >= 2 && string.IsNullOrEmpty(f.ReadProblem)) surveyLines.Add(f);
                }

            var minPiece = profile.Trim.MinPieceFeet * upf;
            var seamNumber = 0;
            foreach (var f in aerial.Features)
            {
                var entry = new TrimEntry
                {
                    Handle = f.Handle, EntityType = f.EntityType, Layer = f.Layer, Kind = f.Kind,
                    Category = f.Class != null && !f.Class.IsUnknown ? f.Class.Category : string.Empty,
                    Disposition = TrimDisposition.Outside
                };
                result.Entries.Add(entry);
                if (footprints.Count == 0) continue;
                if (profile.IsIgnored(f)) { entry.Note = "layer is on the profile's ignore list"; continue; }

                if (f.Kind == FeatureKind.Line && f.Vertices != null && f.Vertices.Count >= 2 && string.IsNullOrEmpty(f.ReadProblem))
                {
                    var intervals = FootprintClipper.Intervals(f.Vertices, f.Stations, footprints);
                    entry.InsideLength = intervals.Where(i => i.Kind == IntervalKind.Covered).Sum(i => i.Length);
                    entry.OutsideLength = intervals.Where(i => i.Kind == IntervalKind.Uncovered).Sum(i => i.Length);
                    if (entry.InsideLength <= 1e-9) continue;

                    var fr = FeatureResult.From(f);
                    fr.Intervals = intervals;
                    fr.CoveredLength = entry.InsideLength;
                    fr.UncoveredLength = entry.OutsideLength;
                    fr.Confidence = 1.0;
                    var category = profile.Category(fr.Category);
                    var nonplot = ChangePlanner.NonplotLayerFor(fr, category, profile);
                    entry.NonplotLayer = nonplot;

                    var keep = intervals.Where(i => i.Kind == IntervalKind.Uncovered && i.Length >= minPiece).ToList();
                    var dropped = intervals.Count(i => i.Kind == IntervalKind.Uncovered && i.Length < minPiece);
                    PlannedAction action;
                    if (keep.Count == 0)
                    {
                        entry.Disposition = TrimDisposition.Inside;
                        action = PlannedAction.Nonplot;
                        if (dropped > 0)
                            entry.Note = string.Format(CultureInfo.InvariantCulture, "{0} outside remnant(s) shorter than {1:0.0} ft go nonplot with it", dropped, profile.Trim.MinPieceFeet);
                        fr.Status = MatchStatus.Matched;
                    }
                    else
                    {
                        entry.Disposition = TrimDisposition.Split;
                        action = PlannedAction.SplitNonplot;
                        fr.Status = MatchStatus.Partial;
                        if (dropped > 0)
                            entry.Note = string.Format(CultureInfo.InvariantCulture, "{0} outside remnant(s) shorter than {1:0.0} ft go nonplot with it", dropped, profile.Trim.MinPieceFeet);
                    }
                    fr.EffectiveStatus = fr.Status;
                    fr.Action = action;
                    fr.NonplotLayer = nonplot;
                    result.Features.Results.Add(fr);
                    plan.Changes.Add(new PlannedChange
                    {
                        Handle = f.Handle, Kind = f.Kind, Category = fr.Category, FromLayer = f.Layer, ToLayer = nonplot,
                        Action = action, KeepIntervals = keep, AutoDecided = true
                    });

                    // Seams: every boundary between an inside and an outside stretch.
                    for (var i = 0; i + 1 < intervals.Count; i++)
                    {
                        if (intervals[i].Kind == intervals[i + 1].Kind) continue;
                        var cut = intervals[i].EndPoint;
                        var station = intervals[i].End;
                        var seam = SeamMatcher.Check(f, cut, station, surveyLines, profile, upf);
                        seam.Id = "S" + (++seamNumber).ToString(CultureInfo.InvariantCulture);
                        seam.FootprintId = FootprintAt(cut, footprints);
                        result.Seams.Add(seam);
                    }
                    continue;
                }

                // Points, blocks, annotation, and things that cannot be cut.
                var inside = FootprintClipper.Inside(f.Position, footprints);
                if (!inside) continue;
                if (!string.IsNullOrEmpty(f.ReadProblem) || (f.Kind == FeatureKind.Line && (f.Vertices == null || f.Vertices.Count < 2)))
                {
                    entry.Disposition = TrimDisposition.Straddles;
                    entry.Note = (f.ReadProblem ?? "geometry could not be read") + "; inside a footprint but not trimmed, check by hand";
                    continue;
                }
                {
                    var fr = FeatureResult.From(f);
                    fr.Status = MatchStatus.Matched;
                    fr.EffectiveStatus = MatchStatus.Matched;
                    fr.Confidence = 1.0;
                    var category = profile.Category(fr.Category);
                    var nonplot = ChangePlanner.NonplotLayerFor(fr, category, profile);
                    fr.Action = PlannedAction.Nonplot;
                    fr.NonplotLayer = nonplot;
                    entry.Disposition = TrimDisposition.Inside;
                    entry.NonplotLayer = nonplot;
                    result.Features.Results.Add(fr);
                    plan.Changes.Add(new PlannedChange
                    {
                        Handle = f.Handle, Kind = f.Kind, Category = fr.Category, FromLayer = f.Layer, ToLayer = nonplot,
                        Action = PlannedAction.Nonplot, AutoDecided = true
                    });
                }
            }
            return result;
        }

        private static string FootprintAt(CPoint p, IList<Footprint> footprints)
        {
            foreach (var fp in footprints)
            {
                if (fp.Polygon == null || fp.Polygon.Count < 3) continue;
                // The cut sits on the edge: nudge the test inward is unnecessary, PointInPolygon
                // on the edge is acceptable either way; the nearest footprint by bounds wins.
                var b = fp.Bounds;
                if (p.X >= b.MinX - 0.01 && p.X <= b.MaxX + 0.01 && p.Y >= b.MinY - 0.01 && p.Y <= b.MaxY + 0.01) return fp.Id;
            }
            return footprints.Count > 0 ? footprints[0].Id : string.Empty;
        }
    }

    /// <summary>At a cut, does our line carry on where the aerial one stops, and how well.</summary>
    public static class SeamMatcher
    {
        public static SeamFinding Check(CompareFeature aerial, CPoint cut, double station, IList<CompareFeature> surveyLines, CompareProfile profile, double upf)
        {
            var finding = new SeamFinding
            {
                AerialHandle = aerial.Handle, AerialLayer = aerial.Layer,
                Category = aerial.Class != null && !aerial.Class.IsUnknown ? aerial.Class.Category : string.Empty,
                Cut = cut
            };
            var search = profile.Trim.SeamSearchFeet * upf;
            var offTol = profile.Trim.SeamOffsetFeet * upf;
            var elevTol = profile.Trim.SeamElevationFeet * upf;
            var aerialKnown = aerial.Class != null && !aerial.Class.IsUnknown;
            double adx, ady;
            Geometry2.DirectionAtStation(aerial.Vertices, aerial.Stations, station, out adx, out ady);
            var aerialZ = RealZ(cut);

            CompareFeature best = null;
            double bestDistance = double.MaxValue, bestStation = 0, bestAngle = 0;
            var bestScore = int.MinValue;
            foreach (var s in surveyLines)
            {
                var b = s.Bounds;
                if (b.IsEmpty || cut.X < b.MinX - search || cut.X > b.MaxX + search || cut.Y < b.MinY - search || cut.Y > b.MaxY + search) continue;
                double st; int seg;
                var d = Geometry2.ClosestOnPolyline(cut, s.Vertices, s.Stations, out st, out seg);
                if (d > search) continue;
                var surveyKnown = s.Class != null && !s.Class.IsUnknown;
                var sameCategory = aerialKnown && surveyKnown && string.Equals(s.Class.Category, aerial.Class.Category, StringComparison.OrdinalIgnoreCase);
                if (aerialKnown && surveyKnown && !sameCategory) continue;   // a different feature type is not the continuation
                double sdx, sdy;
                Geometry2.DirectionAtStation(s.Vertices, s.Stations, st, out sdx, out sdy);
                var angle = Geometry2.AngleBetweenUndirected(adx, ady, sdx, sdy);
                var score = (sameCategory ? 2 : 0) + (angle <= profile.Trim.SeamAngleDegrees ? 1 : 0);
                if (score > bestScore || (score == bestScore && d < bestDistance))
                {
                    best = s; bestScore = score; bestDistance = d; bestStation = st; bestAngle = angle;
                }
            }

            if (best == null)
            {
                finding.Status = SeamStatus.NoContinuation;
                finding.Note = string.Format(CultureInfo.InvariantCulture, "no surveyed {0} within {1:0.0} ft of the cut: aerial feature we did not pick up, or theirs ends here",
                    aerialKnown ? aerial.Class.Category : "line", profile.Trim.SeamSearchFeet);
                return finding;
            }

            finding.SurveyHandle = best.Handle;
            finding.SurveyLayer = best.Layer;
            finding.Offset = bestDistance;
            finding.AngleDegrees = bestAngle;
            var surveyZ = RealZ(Geometry2.PointAtStation(best.Vertices, best.Stations, bestStation));
            if (aerialZ.HasValue && surveyZ.HasValue) finding.ElevationDifference = aerialZ.Value - surveyZ.Value;

            var notes = new List<string>();
            var bestKnown = best.Class != null && !best.Class.IsUnknown;
            if (!aerialKnown || !bestKnown)
                notes.Add("feature type unknown on " + (!aerialKnown ? "the aerial side (layer " + aerial.Layer + ")" : "our side (layer " + best.Layer + ")") + "; nearest line used");
            if (bestAngle > profile.Trim.SeamAngleDegrees)
                notes.Add(string.Format(CultureInfo.InvariantCulture, "directions differ by {0:0} degrees", bestAngle));
            var offsetBad = bestDistance > offTol;
            var elevationBad = finding.ElevationDifference.HasValue && Math.Abs(finding.ElevationDifference.Value) > elevTol;
            if (offsetBad)
                notes.Add(string.Format(CultureInfo.InvariantCulture, "{0:0.00} ft from our line at the seam, more than the {1:0.00} ft tolerance", bestDistance / upf, profile.Trim.SeamOffsetFeet));
            if (elevationBad)
                notes.Add(string.Format(CultureInfo.InvariantCulture, "elevation differs by {0:0.00} ft at the seam (aerial {1:0.00}, ours {2:0.00}), more than the {3:0.00} ft tolerance",
                    finding.ElevationDifference.Value / upf, aerialZ.Value, surveyZ.Value, profile.Trim.SeamElevationFeet));

            if (!aerialKnown || !bestKnown || bestAngle > profile.Trim.SeamAngleDegrees) finding.Status = SeamStatus.NeedsReview;
            else if (offsetBad || elevationBad) finding.Status = SeamStatus.Discrepancy;
            else finding.Status = SeamStatus.Aligned;
            if (notes.Count == 0)
                notes.Add(string.Format(CultureInfo.InvariantCulture, "aligned: {0:0.00} ft offset{1}", bestDistance / upf,
                    finding.ElevationDifference.HasValue ? string.Format(CultureInfo.InvariantCulture, ", {0:0.00} ft elevation difference", finding.ElevationDifference.Value / upf) : string.Empty));
            finding.Note = string.Join("; ", notes.ToArray());
            return finding;
        }

        private static double? RealZ(CPoint p)
        {
            if (!p.HasZ || double.IsNaN(p.Z) || Math.Abs(p.Z) < 0.01) return null;
            return p.Z;
        }
    }

    // ============================================================================= report

    public static class TrimReport
    {
        public const string Suffix = ".ftf-trim.csv";
        public const string SeamSuffix = ".ftf-seams.csv";
        public const string Header = "Host,Handle,EntityType,Layer,Category,Disposition,InsideFt,OutsideFt,NonplotLayer,ApplyProblem,Note";
        public const string SeamHeader = "Host,Seam,Footprint,AerialHandle,AerialLayer,Category,CutX,CutY,SurveyHandle,SurveyLayer,OffsetFt,ElevationDiffFt,AngleDeg,Status,Note";

        public static string PathFor(string drawingPath, string suffix)
        {
            if (string.IsNullOrWhiteSpace(drawingPath)) return null;
            try
            {
                var dir = Path.GetDirectoryName(drawingPath);
                var name = Path.GetFileNameWithoutExtension(drawingPath);
                if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(name)) return null;
                return Path.Combine(dir, name + suffix);
            }
            catch (ArgumentException) { return null; }
        }

        public static string Render(TrimResult result)
        {
            var sb = new StringBuilder();
            sb.AppendLine(Header);
            if (result == null) return sb.ToString();
            var upf = result.UnitsPerFoot <= 0 ? 1.0 : result.UnitsPerFoot;
            foreach (var e in result.Entries.OrderBy(x => Order(x.Disposition)).ThenBy(x => x.Layer, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Handle, StringComparer.OrdinalIgnoreCase))
            {
                if (e.Disposition == TrimDisposition.Outside && string.IsNullOrEmpty(e.Note)) continue;   // the bulk of the base map: untouched, not listed
                var fields = new[]
                {
                    result.HostName ?? string.Empty, e.Handle ?? string.Empty, e.EntityType ?? string.Empty, e.Layer ?? string.Empty, e.Category ?? string.Empty,
                    e.Disposition.ToString(), (e.InsideLength / upf).ToString("0.00", CultureInfo.InvariantCulture), (e.OutsideLength / upf).ToString("0.00", CultureInfo.InvariantCulture),
                    e.NonplotLayer ?? string.Empty, e.ApplyProblem ?? string.Empty, e.Note ?? string.Empty
                };
                sb.AppendLine(string.Join(",", fields.Select(CompareReport.Csv).ToArray()));
            }
            return sb.ToString();
        }

        public static string RenderSeams(TrimResult result)
        {
            var sb = new StringBuilder();
            sb.AppendLine(SeamHeader);
            if (result == null) return sb.ToString();
            var upf = result.UnitsPerFoot <= 0 ? 1.0 : result.UnitsPerFoot;
            foreach (var f in result.Seams.OrderBy(x => SeamOrder(x.Status)).ThenBy(x => x.Id, StringComparer.OrdinalIgnoreCase))
            {
                var fields = new[]
                {
                    result.HostName ?? string.Empty, f.Id ?? string.Empty, f.FootprintId ?? string.Empty, f.AerialHandle ?? string.Empty, f.AerialLayer ?? string.Empty, f.Category ?? string.Empty,
                    f.Cut.X.ToString("0.000", CultureInfo.InvariantCulture), f.Cut.Y.ToString("0.000", CultureInfo.InvariantCulture),
                    f.SurveyHandle ?? string.Empty, f.SurveyLayer ?? string.Empty,
                    f.Offset.HasValue ? (f.Offset.Value / upf).ToString("0.00", CultureInfo.InvariantCulture) : string.Empty,
                    f.ElevationDifference.HasValue ? (f.ElevationDifference.Value / upf).ToString("0.00", CultureInfo.InvariantCulture) : string.Empty,
                    f.AngleDegrees.HasValue ? f.AngleDegrees.Value.ToString("0", CultureInfo.InvariantCulture) : string.Empty,
                    f.Status.ToString(), f.Note ?? string.Empty
                };
                sb.AppendLine(string.Join(",", fields.Select(CompareReport.Csv).ToArray()));
            }
            return sb.ToString();
        }

        public static string SummaryText(TrimResult result)
        {
            if (result == null) return "No trim has been run.";
            var s = result.Summary();
            return string.Format(CultureInfo.InvariantCulture,
                "FTFTRIM: {0} footprint(s); aerial objects: {1} inside (nonplot), {2} split at the edge, {3} not trimmed (check by hand), {4} outside. " +
                "Seams: {5} aligned, {6} discrepancies, {7} with no continuation, {8} for review.",
                result.Footprints.Count, s.Inside, s.Split, s.Straddles, s.Outside, s.Aligned, s.Discrepancies, s.NoContinuation, s.Review);
        }

        private static int Order(TrimDisposition d)
        {
            switch (d) { case TrimDisposition.Straddles: return 0; case TrimDisposition.Split: return 1; case TrimDisposition.Inside: return 2; default: return 3; }
        }

        private static int SeamOrder(SeamStatus s)
        {
            switch (s) { case SeamStatus.Discrepancy: return 0; case SeamStatus.NoContinuation: return 1; case SeamStatus.NeedsReview: return 2; default: return 3; }
        }
    }
}
