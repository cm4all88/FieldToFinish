using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;

namespace FieldCodes.Easements
{
    /// <summary>
    /// An area that is excluded from an easement: "EXCEPT THE NORTH 20 FEET THEREOF", or
    /// "EXCEPT THAT PORTION LYING WITHIN THE EXISTING 15 FOOT UTILITY EASEMENT". The definition
    /// is kept -- the boundary object, or the portion calls and what they are measured on --
    /// so a rebuild recomputes it from the current drawing.
    /// </summary>
    public sealed class Exclusion
    {
        public const string BoundaryKind = "BOUNDARY";
        public const string PortionKind = "PORTION";

        [JsonProperty("kind")] public string Kind { get; set; }

        /// <summary>For a boundary exclusion: the closed boundary object.</summary>
        [JsonProperty("source")] public GeometrySource Source { get; set; }

        /// <summary>For a portion exclusion: its calls ("the NORTH 20 feet").</summary>
        [JsonProperty("portion")] public List<PortionStep> PortionSteps { get; set; }

        /// <summary>For a portion exclusion: the boundary the calls are measured on. Null means
        /// the easement (component) itself -- "THEREOF".</summary>
        [JsonProperty("of")] public GeometrySource Of { get; set; }

        /// <summary>How the legal draft names a boundary exclusion: "THE EXISTING 15 FOOT UTILITY EASEMENT ...".</summary>
        [JsonProperty("description")] public string Description { get; set; }

        /// <summary>The excluded shape as last built.</summary>
        [JsonProperty("loop")] public List<Course> Loop { get; set; }

        /// <summary>HOLE (inside the easement), CUT (changes its outline) or NONE (does not touch it).</summary>
        [JsonProperty("effect")] public string Effect { get; set; }

        /// <summary>Square feet actually removed from the easement.</summary>
        [JsonProperty("removed")] public double RemovedSquareFeet { get; set; }
    }

    /// <summary>
    /// A further area of the same easement: "... TOGETHER WITH THE SOUTH 15 FEET OF ...".
    /// Component A is the easement record's own geometry; B, C... are kept here, each with
    /// its own definition, exclusions and area.
    /// </summary>
    public sealed class EasementComponent
    {
        public const string PortionKind = "PORTION";
        public const string AreaKind = "AREA";
        public const string BoundaryKind = "BOUNDARY";

        [JsonProperty("label")] public string Label { get; set; }

        /// <summary>TOGETHER WITH, AND or ALSO -- as chosen; null when not chosen yet.</summary>
        [JsonProperty("connector")] public string Connector { get; set; }

        [JsonProperty("kind")] public string Kind { get; set; }

        /// <summary>The lot a portion component is measured on, or the boundary object of a boundary component.</summary>
        [JsonProperty("parcel")] public GeometrySource Parcel { get; set; }
        [JsonProperty("portion")] public List<PortionStep> PortionSteps { get; set; }

        [JsonProperty("corners")] public List<SelectedLocation> AnglePoints { get; set; }
        [JsonProperty("sides")] public List<AreaSide> AreaSides { get; set; }
        [JsonProperty("poc")] public SelectedLocation PointOfCommencement { get; set; }
        [JsonProperty("tie")] public CourseData CommencementTie { get; set; }

        /// <summary>The tie as the courses it runs when it follows a line or curve; see <see cref="EasementRecord.CommencementTieCourses"/>.</summary>
        [JsonProperty("tieCourses")] public List<CourseData> CommencementTieCourses { get; set; }
        [JsonProperty("tieFollows")] public List<GeometrySource> CommencementTieFollows { get; set; }

        public IList<CourseData> TieCourses()
        {
            return Ties.Of(CommencementTie, CommencementTieCourses);
        }

        [JsonProperty("exclusions")] public List<Exclusion> Exclusions { get; set; }

        [JsonProperty("courses")] public List<CourseData> Courses { get; set; }
        [JsonProperty("boundary")] public List<CourseData> BoundaryCourses { get; set; }
        [JsonProperty("holes")] public List<List<CourseData>> Holes { get; set; }
        [JsonProperty("areaSqFt")] public double AreaSquareFeet { get; set; }

        public EasementComponent()
        {
            Exclusions = new List<Exclusion>();
            Holes = new List<List<CourseData>>();
        }
    }

    /// <summary>An area with an outer boundary and any interior holes.</summary>
    public sealed class RegionShape
    {
        public List<Course> Outer { get; set; }
        public List<List<Course>> Holes { get; set; }

        public RegionShape(IEnumerable<Course> outer)
        {
            Outer = outer.ToList();
            Holes = new List<List<Course>>();
        }

        public double Area
        {
            get { return Math.Abs(Loops.SignedArea(Outer)) - Holes.Sum(h => Math.Abs(Loops.SignedArea(h))); }
        }

        public bool Contains(P2 p)
        {
            return StripTrim.Inside(Outer, p) && !Holes.Any(h => StripTrim.Inside(h, p));
        }
    }

    public sealed class RegionResult
    {
        public RegionShape Region { get; set; }
        public string Effect { get; set; }
        public double Removed { get; set; }
        public List<string> Errors { get; private set; }
        public List<string> Warnings { get; private set; }
        public bool Ok { get { return Errors.Count == 0; } }

        public RegionResult()
        {
            Errors = new List<string>();
            Warnings = new List<string>();
        }
    }

    /// <summary>
    /// Removes excluded areas from an easement. Exact: the easement is cut with the existing
    /// strip trimming engine, so arcs stay arcs. Cases that need a surveyor's decision --
    /// an exclusion that splits the easement into separate parts, exclusions that overlap
    /// each other, one that swallows the whole easement -- are reported, never resolved.
    /// </summary>
    public static class RegionBuilder
    {
        public static RegionResult Exclude(RegionShape region, IList<Course> exclusion, double tolerance)
        {
            var result = new RegionResult { Region = region, Effect = "NONE" };
            if (exclusion == null || exclusion.Count == 0) { result.Errors.Add("The exclusion has no boundary."); return result; }
            if (Loops.LargestGap(exclusion) > tolerance) { result.Errors.Add("The exclusion boundary does not close."); return result; }
            var before = region.Area;

            // An exclusion that never meets the outline is a hole, or misses, or swallows it.
            var touches = Intersects(region.Outer, exclusion, tolerance);
            TrimResult split = null;
            if (touches)
            {
                split = StripTrim.Split(region.Outer, new List<IList<Course>> { exclusion }, tolerance);
                if (!split.Ok) { result.Errors.AddRange(split.Errors); return result; }
            }

            if (split != null && split.TrimCuts[0] && split.Pieces.Count > 1)
            {
                // The exclusion crosses the outline: keep the pieces outside it.
                var keep = split.Pieces.Where(p => !StripTrim.Inside(exclusion, p.InsidePoint)).Select(p => p.Number).ToList();
                if (keep.Count == 0) { result.Errors.Add("The exclusion covers the whole easement; nothing would be left."); return result; }
                if (keep.Count == split.Pieces.Count) { result.Warnings.Add("The exclusion touches the easement but removes nothing from it."); return result; }
                string failure;
                var outer = keep.Count == 1 ? split.Pieces.First(p => p.Number == keep[0]).Loop : StripTrim.Merge(split, keep, tolerance, out failure);
                if (outer == null)
                {
                    result.Errors.Add("The exclusion divides the easement into separate parts. Whether that is intended, and how the parts are described, is a surveyor's decision -- describe them as components instead.");
                    return result;
                }
                if (Loops.SignedArea(outer) * Loops.SignedArea(region.Outer) < 0) outer = EasementBuilder.Reverse(outer);

                var shaped = new RegionShape(outer);
                foreach (var hole in region.Holes)
                {
                    if (Intersects(hole, exclusion, tolerance) || StripTrim.Inside(exclusion, StripTrim.PointInside(hole, tolerance)))
                    {
                        result.Errors.Add("This exclusion overlaps an earlier exclusion. Overlapping exceptions are not resolved automatically -- a surveyor must decide how they are described.");
                        return result;
                    }
                    if (StripTrim.Inside(outer, StripTrim.PointInside(hole, tolerance))) shaped.Holes.Add(hole);
                }
                result.Region = shaped;
                result.Effect = "CUT";
            }
            else
            {
                var probe = StripTrim.PointInside(exclusion, tolerance);
                var exclusionInside = exclusion.All(c => StripTrim.Inside(region.Outer, c.PointAt(c.Length / 2)) || StripTrim.OnOutline(region.Outer, c.PointAt(c.Length / 2), tolerance))
                                      && StripTrim.Inside(region.Outer, probe);
                var regionInside = StripTrim.Inside(exclusion, StripTrim.PointInside(region.Outer, tolerance));
                if (exclusionInside && !regionInside)
                {
                    foreach (var hole in region.Holes)
                        if (Intersects(hole, exclusion, tolerance) || StripTrim.Inside(hole, probe) || StripTrim.Inside(exclusion, StripTrim.PointInside(hole, tolerance)))
                        {
                            result.Errors.Add("This exclusion overlaps an earlier exclusion. Overlapping exceptions are not resolved automatically -- a surveyor must decide how they are described.");
                            return result;
                        }
                    var shaped = new RegionShape(region.Outer);
                    shaped.Holes.AddRange(region.Holes);
                    var hole2 = exclusion.ToList();
                    if (Loops.SignedArea(hole2) * Loops.SignedArea(region.Outer) > 0) hole2 = EasementBuilder.Reverse(hole2);
                    shaped.Holes.Add(hole2);
                    result.Region = shaped;
                    result.Effect = "HOLE";
                }
                else if (regionInside)
                {
                    result.Errors.Add("The exclusion covers the whole easement; nothing would be left.");
                    return result;
                }
                else
                {
                    result.Warnings.Add("The exclusion does not touch this easement, so it removes nothing.");
                    return result;
                }
            }

            result.Removed = before - result.Region.Area;
            if (result.Region.Area <= tolerance) result.Errors.Add("Nothing of the easement is left after the exclusion.");
            return result;
        }

        private static bool Intersects(IList<Course> a, IList<Course> b, double tolerance)
        {
            foreach (var x in a)
            foreach (var y in b)
                if (Intersections.Bounded(x, y, tolerance).Count > 0) return true;
            return false;
        }

        /// <summary>
        /// Area shared by two components, so a total that double-counts is flagged. Zero when
        /// they only touch.
        /// </summary>
        public static double Overlap(RegionShape a, RegionShape b, double tolerance)
        {
            var split = Intersects(a.Outer, b.Outer, tolerance) ? StripTrim.Split(a.Outer, new List<IList<Course>> { b.Outer }, tolerance) : null;
            if (split != null && !split.Ok) return 0;
            if (split == null || !split.TrimCuts[0] || split.Pieces.Count == 1)
            {
                if (StripTrim.Inside(b.Outer, StripTrim.PointInside(a.Outer, tolerance))) return a.Area;
                if (StripTrim.Inside(a.Outer, StripTrim.PointInside(b.Outer, tolerance))) return b.Area;
                return 0;
            }
            return split.Pieces.Where(p => b.Contains(p.InsidePoint) && a.Contains(p.InsidePoint)).Sum(p => p.Area);
        }

        /// <summary>
        /// The ground covered by at least one region -- an overlap counted once. Worked out exactly
        /// from the shared pieces (inclusion and exclusion over every combination of regions). Null
        /// when the overlaps are tangled in a way this does not resolve exactly (a hole crossing another
        /// region's outline, a region floating inside another with holes involved, more than six
        /// regions): the caller then reports that the physical area was not worked out, rather than
        /// showing a number that might be wrong.
        /// </summary>
        public static double? UnionArea(IList<RegionShape> regions, double tolerance)
        {
            var n = regions == null ? 0 : regions.Count;
            if (n == 0) return 0;
            if (n > 6) return null;
            double total = 0;
            for (var mask = 1; mask < (1 << n); mask++)
            {
                var subset = Enumerable.Range(0, n).Where(i => (mask & (1 << i)) != 0).Select(i => regions[i]).ToList();
                double area;
                if (subset.Count == 1) area = subset[0].Area;
                else
                {
                    var shared = IntersectionArea(subset, tolerance);
                    if (!shared.HasValue) return null;
                    area = shared.Value;
                }
                total += (subset.Count % 2 == 1 ? 1 : -1) * area;
            }
            return total;
        }

        /// <summary>Area common to every region, or null when it cannot be worked out exactly.</summary>
        public static double? IntersectionArea(IList<RegionShape> regions, double tolerance)
        {
            var first = regions.OrderBy(r => r.Area).First();
            var others = regions.Where(r => !ReferenceEquals(r, first)).ToList();
            var loops = others.SelectMany(r => new[] { r.Outer }.Concat(r.Holes)).ToList();
            var crossing = loops.Where(l => Intersects(first.Outer, l, tolerance)).ToList();

            foreach (var l in loops.Where(l => !crossing.Contains(l)))
            {
                // Entirely outside the first region's outline: harmless. Entirely inside: not resolved here.
                if (StripTrim.Inside(first.Outer, StripTrim.PointInside(l, tolerance))) return null;
            }
            foreach (var hole in first.Holes)
                if (crossing.Any(l => Intersects(hole, l, tolerance))) return null;

            List<TrimPiece> pieces;
            if (crossing.Count == 0)
                pieces = new List<TrimPiece> { new TrimPiece { Loop = first.Outer, Area = Math.Abs(Loops.SignedArea(first.Outer)), InsidePoint = StripTrim.PointInside(first.Outer, tolerance) } };
            else
            {
                var split = StripTrim.Split(first.Outer, crossing.Cast<IList<Course>>().ToList(), tolerance);
                if (!split.Ok) return null;
                pieces = split.Pieces;
            }

            double area = 0;
            foreach (var piece in pieces)
            {
                // A piece counts when every region covers it; the first region's own holes come out of it.
                var inside = piece.InsidePoint;
                if (!StripTrim.Inside(first.Outer, inside) || !others.All(r => r.Contains(inside))) continue;
                area += piece.Area;
                foreach (var hole in first.Holes)
                    if (StripTrim.Inside(piece.Loop, StripTrim.PointInside(hole, tolerance))) area -= Math.Abs(Loops.SignedArea(hole));
            }
            return Math.Max(0, area);
        }

        /// <summary>Next component label: B after A, C after B...</summary>
        public static string NextLabel(IEnumerable<EasementComponent> components)
        {
            var used = new HashSet<string>(components.Select(c => c.Label));
            for (var c = 'B'; c <= 'Z'; c++)
                if (!used.Contains(c.ToString(CultureInfo.InvariantCulture))) return c.ToString(CultureInfo.InvariantCulture);
            return "Z" + used.Count.ToString(CultureInfo.InvariantCulture);
        }
    }
}
