using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using FieldCodes.Easements;
using FieldCodes.Review;
using FieldCodes.Settings;

namespace FieldCodes.Linework
{
    /// <summary>A shot coded as a gate post.</summary>
    public sealed class GatePost
    {
        /// <summary>The point number.</summary>
        public string Id { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public string Description { get; set; }
        /// <summary>The COGO point's handle, to zoom to it from the review list.</summary>
        public string Handle { get; set; }

        public P2 At { get { return new P2(X, Y); } }
    }

    /// <summary>A fence line, flattened to plan (Z is not used to pair posts).</summary>
    public sealed class GateFence
    {
        /// <summary>The fence entity's handle.</summary>
        public string Id { get; set; }
        public IList<P2> Points { get; set; }
        public string Code { get; set; }
        public string Layer { get; set; }
    }

    public enum GateDecision
    {
        /// <summary>Labelled between the posts.</summary>
        Label = 0,
        /// <summary>Paired, but not labelled: the width needs a drafter.</summary>
        Review = 1
    }

    /// <summary>Two posts FTF paired into one gate opening.</summary>
    public sealed class GatePair
    {
        public GatePost A { get; set; }
        public GatePost B { get; set; }
        /// <summary>Plan distance between the post shots, centre to centre (ft).</summary>
        public double Spacing { get; set; }
        /// <summary>A width the crew wrote on a post ("FGP 12"), when there is one.</summary>
        public double? Recorded { get; set; }
        /// <summary>The width the label is built from.</summary>
        public double Width { get; set; }
        /// <summary>True when the width is one the crew recorded (and the posts agree with it);
        /// false when it is the calculated post spacing, not a measured clear opening.</summary>
        public bool Verified { get; set; }
        /// <summary>What the width is: "post spacing, centre to centre" or "recorded by the crew".</summary>
        public string Basis { get; set; }
        /// <summary>The label, or null when the gate is for review.</summary>
        public string Text { get; set; }
        public GateDecision Decision { get; set; }
        /// <summary>For a gate under review, its review kind and why.</summary>
        public string ReviewKind { get; set; }
        public string Reason { get; set; }
        /// <summary>Why the two posts are one gate.</summary>
        public string Evidence { get; set; }
        /// <summary>The fence the gate is in (the label takes its layer); null when none.</summary>
        public GateFence Fence { get; set; }
        /// <summary>Order-free identity of the pair, kept on the label so a rerun finds it.</summary>
        public string Key { get; set; }
        public double MidX { get; set; }
        public double MidY { get; set; }
        /// <summary>Direction from post A to post B, radians from east.</summary>
        public double Direction { get; set; }
    }

    public sealed class GateResult
    {
        public List<GatePair> Pairs { get; private set; }
        /// <summary>Everything a drafter must look at: unpaired posts, ambiguous pairs, widths
        /// out of range, recorded widths that disagree with the posts.</summary>
        public List<ReviewItem> Review { get; private set; }

        public GateResult()
        {
            Pairs = new List<GatePair>();
            Review = new List<ReviewItem>();
        }

        public int Labelled { get { return Pairs.Count(p => p.Decision == GateDecision.Label); } }
    }

    /// <summary>The order-free key of a gate (its two post numbers).</summary>
    public static class GateKeys
    {
        public static string For(string a, string b)
        {
            a = (a ?? string.Empty).Trim();
            b = (b ?? string.Empty).Trim();
            return string.CompareOrdinal(a, b) <= 0 ? a + "-" + b : b + "-" + a;
        }

        /// <summary>The two post numbers of a key, or null.</summary>
        public static string[] Posts(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;
            var i = key.IndexOf('-');
            if (i <= 0 || i >= key.Length - 1) return null;
            return new[] { key.Substring(0, i), key.Substring(i + 1) };
        }
    }

    /// <summary>The width rules: under the narrow limit review (or the narrow label), up to the
    /// plain limit "GATE", up to the widest "6' GATE", wider review.</summary>
    public static class GateWidths
    {
        /// <summary>The width rounded to the settings' step (nearest; halves round up).</summary>
        public static double Round(double width, GateSettings s)
        {
            var step = s != null && s.RoundToFeet > 0 ? s.RoundToFeet : 1.0;
            return Math.Round(Math.Round(width / step, MidpointRounding.AwayFromZero) * step, 6);
        }

        /// <summary>The rounded width as written: at most <paramref name="decimals"/> places,
        /// trailing zeros dropped (6, 6.5).</summary>
        public static string Format(double rounded, int decimals)
        {
            var f = decimals <= 0 ? "0" : "0." + new string('#', Math.Min(decimals, 3));
            return rounded.ToString(f, CultureInfo.InvariantCulture);
        }

        /// <summary>The label for a gate this wide, or null with the review kind and reason.</summary>
        public static string Label(double width, GateSettings s, out string reviewKind, out string reason)
        {
            reviewKind = null;
            reason = null;
            var w = Feet(width);
            if (width < s.NarrowBelowFeet)
            {
                if (!string.IsNullOrWhiteSpace(s.NarrowText)) return s.NarrowText.Trim();
                reviewKind = ReviewKinds.GateNarrow;
                reason = w + " ft is under the narrowest gate believed (" + Feet(s.NarrowBelowFeet) + " ft)";
                return null;
            }
            if (width < s.PlainBelowFeet) return (s.PlainText ?? "GATE").Trim();
            if (width <= s.MaxWidthFeet)
                return (s.WidthFormat ?? "{width}' GATE").Replace("{width}", Format(Round(width, s), s.Decimals)).Trim();
            reviewKind = ReviewKinds.GateWide;
            reason = w + " ft is over the widest gate believed (" + Feet(s.MaxWidthFeet) + " ft)";
            return null;
        }

        internal static string Feet(double v)
        {
            return v.ToString("0.##", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// Pairs gate posts into gates. A pair needs evidence, never distance alone: both posts on
    /// one fence line next to each other, or fence ends on both sides of the opening (strong);
    /// a fence at one of the posts (good). Posts away from any fence are only proposed for
    /// review unless Settings > Gates allows pairing them. A post with two plausible partners
    /// is ambiguous and left for a drafter; nothing is moved.
    /// </summary>
    public static class GateFinder
    {
        private static readonly Regex Width = new Regex(@"^(?<v>[0-9]+(?:\.[0-9]+)?)\s*(?<u>'|FT|FEET|FOOT)?$",
                                                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>True when the description carries a gate post code as a word ("FGP",
        /// "FGP FNC", "FNC B FGP"); a fused number is allowed (FGP1).</summary>
        public static bool IsPost(string description, ICollection<string> postCodes)
        {
            return LineHeights.TokensAfter(description, postCodes, null) != null;
        }

        /// <summary>
        /// A width the crew wrote after the gate post code ("FGP 12", "FGP 12'"), up to the next
        /// fence code ("FGP 12 FNC B 5": 12 is the gate, 5 the fence). A number under 2 ft with
        /// no unit is not a width (a post number).
        /// </summary>
        public static double? RecordedWidth(string description, ICollection<string> postCodes, ICollection<string> stopCodes)
        {
            var after = LineHeights.TokensAfter(description, postCodes, stopCodes);
            if (after == null) return null;
            foreach (var t in after)
            {
                var m = Width.Match(t);
                if (!m.Success) continue;
                double v;
                if (!double.TryParse(m.Groups["v"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) continue;
                if (v < 2 && !m.Groups["u"].Success) continue;
                return v;
            }
            return null;
        }

        private sealed class OnFence
        {
            public GateFence Fence;
            public double Offset;
            public double Station;
            public bool AtEnd;
        }

        private sealed class Candidate
        {
            public int P;
            public int Q;
            public double D;
            public int Score;
            public string Evidence;
            public GateFence Fence;
        }

        /// <summary>
        /// Finds the gates among <paramref name="posts"/>. <paramref name="confirmed"/> are post
        /// numbers a drafter already paired (FTFGATE): they are left out entirely.
        /// <paramref name="fenceCodes"/> stop a recorded width ("FGP 12 FNC").
        /// </summary>
        public static GateResult Find(IList<GatePost> posts, IList<GateFence> fences, GateSettings s,
                                      ICollection<string> confirmed = null, ICollection<string> fenceCodes = null)
        {
            if (s == null) s = new GateSettings();
            var result = new GateResult();
            var all = Distinct(posts ?? new List<GatePost>())
                .Where(p => confirmed == null || !confirmed.Contains(p.Id))
                .ToList();
            var fenceList = (fences ?? new List<GateFence>()).Where(f => f != null && f.Points != null && f.Points.Count >= 2).ToList();
            var reach = s.FenceReachFeet;

            var on = all.Select(p => FencesAt(p.At, fenceList, reach)).ToList();

            // Candidate pairs, each with the evidence that the two posts are one gate.
            var candidates = new List<Candidate>();
            for (var i = 0; i < all.Count; i++)
                for (var j = i + 1; j < all.Count; j++)
                {
                    var d = all[i].At.DistanceTo(all[j].At);
                    if (d < 0.25 || d > s.SearchFeet) continue;
                    var c = Evidence(i, j, d, all, on, reach);
                    if (c != null) candidates.Add(c);
                }

            var minScore = s.PairWithoutFence ? 1 : 2;
            var paired = new HashSet<int>();
            var accepted = new List<Candidate>();
            var margin = 1.0 + Math.Max(0, s.AmbiguityPercent) / 100.0;

            bool changed;
            do
            {
                changed = false;
                foreach (var c in candidates.Where(c => c.Score >= minScore)
                                            .OrderByDescending(c => c.Score).ThenBy(c => c.D).ToList())
                {
                    if (paired.Contains(c.P) || paired.Contains(c.Q)) continue;
                    if (!BestFor(c, c.P, candidates, paired, minScore) || !BestFor(c, c.Q, candidates, paired, minScore)) continue;
                    if (Rival(c, c.P, candidates, paired, minScore, margin) != null ||
                        Rival(c, c.Q, candidates, paired, minScore, margin) != null) continue;
                    accepted.Add(c);
                    paired.Add(c.P);
                    paired.Add(c.Q);
                    changed = true;
                }
            } while (changed);

            // A post whose partner had another plausible partner that is now left alone: the
            // pairing could have gone either way, so a drafter decides.
            var ambiguous = new List<HashSet<int>>();
            foreach (var c in accepted.ToList())
            {
                var group = new HashSet<int> { c.P, c.Q };
                foreach (var end in new[] { c.P, c.Q })
                    foreach (var other in candidates.Where(o => o != c && o.Score >= minScore && o.Score >= c.Score && (o.P == end || o.Q == end)))
                    {
                        var partner = other.P == end ? other.Q : other.P;
                        if (!paired.Contains(partner)) group.Add(partner);
                    }
                if (group.Count > 2)
                {
                    accepted.Remove(c);
                    ambiguous.Add(group);
                }
            }
            foreach (var g in ambiguous) foreach (var i in g) paired.Add(i);

            // Unpaired posts that still have plausible partners among the unpaired: ambiguous groups.
            var left = Enumerable.Range(0, all.Count).Where(i => !paired.Contains(i)).ToList();
            var leftSet = new HashSet<int>(left);
            var live = candidates.Where(c => c.Score >= minScore && leftSet.Contains(c.P) && leftSet.Contains(c.Q)).ToList();
            foreach (var group in Components(left, live).Where(g => g.Count > 1))
            {
                ambiguous.Add(group);
                foreach (var i in group) paired.Add(i);
            }

            foreach (var c in accepted.OrderBy(c => all[c.P].Id, StringComparer.Ordinal))
                result.Pairs.Add(Build(all[c.P], all[c.Q], c, s, postCodes: s.PostCodes, fenceCodes: fenceCodes, review: result.Review));

            foreach (var g in ambiguous)
            {
                var members = g.Select(i => all[i]).OrderBy(p => p.Id, StringComparer.Ordinal).ToList();
                var ids = string.Join(", ", members.Select(p => p.Id).ToArray());
                var spans = candidates.Where(c => g.Contains(c.P) && g.Contains(c.Q) && c.Score >= minScore)
                                      .OrderBy(c => c.D)
                                      .Select(c => all[c.P].Id + "-" + all[c.Q].Id + " " + GateWidths.Feet(c.D) + " ft")
                                      .ToArray();
                var item = ReviewKinds.New(ReviewKinds.GateAmbiguous, string.Join(",", members.Select(p => p.Id).ToArray()),
                    "posts " + ids + " could pair more than one way (" + string.Join("; ", spans) + "); no gate was labelled");
                item.Points = ids;
                item.Handles = members.Where(p => !string.IsNullOrEmpty(p.Handle)).Select(p => p.Handle).ToList();
                item.X = members.Average(p => p.X);
                item.Y = members.Average(p => p.Y);
                result.Review.Add(item);
            }

            // Posts with nobody: alone, or with only a partner away from any fence.
            foreach (var i in Enumerable.Range(0, all.Count).Where(i => !paired.Contains(i)))
            {
                var p = all[i];
                var weak = candidates.Where(c => (c.P == i || c.Q == i) && c.Score < minScore && c.Score > 0)
                                     .OrderBy(c => c.D).FirstOrDefault();
                ReviewItem item;
                if (weak != null)
                {
                    var partner = all[weak.P == i ? weak.Q : weak.P];
                    var key = GateKeys.For(p.Id, partner.Id);
                    if (result.Review.Any(r => r.Id == ReviewKinds.GateNoFence + ":" + key)) continue;
                    item = ReviewKinds.New(ReviewKinds.GateNoFence, key,
                        "posts " + key.Replace("-", " and ") + " are " + GateWidths.Feet(weak.D) +
                        " ft apart but neither is within " + GateWidths.Feet(reach) + " ft of a fence line");
                    item.Points = string.Join(", ", new[] { p.Id, partner.Id }.OrderBy(x => x, StringComparer.Ordinal).ToArray());
                    item.Handles = new[] { p.Handle, partner.Handle }.Where(h => !string.IsNullOrEmpty(h)).ToList();
                    item.X = (p.X + partner.X) / 2;
                    item.Y = (p.Y + partner.Y) / 2;
                }
                else
                {
                    item = ReviewKinds.New(ReviewKinds.GateUnpaired, p.Id,
                        on[i].Count > 0
                            ? "no other gate post next to it on the fence within " + GateWidths.Feet(s.SearchFeet) + " ft"
                            : "no other gate post within " + GateWidths.Feet(s.SearchFeet) + " ft, and no fence line within " +
                              GateWidths.Feet(reach) + " ft");
                    item.Points = p.Id;
                    if (!string.IsNullOrEmpty(p.Handle)) item.Handles.Add(p.Handle);
                    item.X = p.X;
                    item.Y = p.Y;
                }
                result.Review.Add(item);
            }

            return result;
        }

        /// <summary>One gate from two posts the drafter picked (FTFGATE), or FTF paired.</summary>
        public static GatePair Build(GatePost a, GatePost b, GateSettings s, ICollection<string> fenceCodes = null)
        {
            var review = new List<ReviewItem>();
            var d = a.At.DistanceTo(b.At);
            return Build(a, b, new Candidate { D = d, Evidence = "picked by the drafter" }, s ?? new GateSettings(),
                         (s ?? new GateSettings()).PostCodes, fenceCodes, review);
        }

        private static GatePair Build(GatePost a, GatePost b, Candidate c, GateSettings s, ICollection<string> postCodes,
                                      ICollection<string> fenceCodes, List<ReviewItem> review)
        {
            if (string.CompareOrdinal(a.Id ?? string.Empty, b.Id ?? string.Empty) > 0) { var t = a; a = b; b = t; }
            var pair = new GatePair
            {
                A = a,
                B = b,
                Spacing = c.D,
                Evidence = c.Evidence,
                Fence = c.Fence,
                Key = GateKeys.For(a.Id, b.Id),
                MidX = (a.X + b.X) / 2,
                MidY = (a.Y + b.Y) / 2,
                Direction = Math.Atan2(b.Y - a.Y, b.X - a.X)
            };

            var codes = postCodes ?? new List<string> { "FGP" };
            var ra = RecordedWidth(a.Description, codes, fenceCodes);
            var rb = RecordedWidth(b.Description, codes, fenceCodes);
            var ids = a.Id + " and " + b.Id;
            string kind = null, reason = null;
            if (ra.HasValue && rb.HasValue && Math.Abs(ra.Value - rb.Value) > s.RecordedToleranceFeet)
            {
                kind = ReviewKinds.GateWidthDisagrees;
                reason = "posts " + ids + " record different widths (" + GateWidths.Feet(ra.Value) + " and " +
                         GateWidths.Feet(rb.Value) + " ft); the posts are " + GateWidths.Feet(c.D) + " ft apart";
            }
            var recorded = ra ?? rb;
            pair.Recorded = recorded;
            if (kind == null && recorded.HasValue && Math.Abs(recorded.Value - c.D) > s.RecordedToleranceFeet)
            {
                kind = ReviewKinds.GateWidthDisagrees;
                reason = "the crew recorded " + GateWidths.Feet(recorded.Value) + " ft but posts " + ids + " are " +
                         GateWidths.Feet(c.D) + " ft apart";
            }

            if (kind == null && recorded.HasValue)
            {
                pair.Width = recorded.Value;
                pair.Verified = true;
                pair.Basis = "recorded by the crew (posts " + GateWidths.Feet(c.D) + " ft apart)";
            }
            else
            {
                pair.Width = c.D;
                pair.Verified = false;
                pair.Basis = "post spacing, centre to centre (not a measured clear opening)";
            }

            if (kind == null)
                pair.Text = GateWidths.Label(pair.Width, s, out kind, out reason);
            if (kind != null && reason != null && !reason.Contains(ids) && kind != ReviewKinds.GateWidthDisagrees)
                reason = "posts " + ids + ": " + reason;

            pair.Decision = kind == null ? GateDecision.Label : GateDecision.Review;
            pair.ReviewKind = kind;
            pair.Reason = reason;
            if (kind != null)
            {
                var item = ReviewKinds.New(kind, pair.Key, reason + "; no gate was labelled");
                item.Points = a.Id + ", " + b.Id;
                item.Handles = new[] { a.Handle, b.Handle }.Where(h => !string.IsNullOrEmpty(h)).ToList();
                item.X = pair.MidX;
                item.Y = pair.MidY;
                review.Add(item);
            }
            return pair;
        }

        private static Candidate Evidence(int i, int j, double d, IList<GatePost> posts, IList<List<OnFence>> on, double reach)
        {
            var a = on[i];
            var b = on[j];

            // Both on one fence, next to each other along it, and the fence runs straight
            // between them (around a corner is not one opening).
            foreach (var fa in a)
                foreach (var fb in b.Where(x => ReferenceEquals(x.Fence, fa.Fence)))
                {
                    var along = Math.Abs(fa.Station - fb.Station);
                    if (along > d * 1.2 + 1.0) continue;
                    var lo = Math.Min(fa.Station, fb.Station);
                    var hi = Math.Max(fa.Station, fb.Station);
                    var between = false;
                    for (var k = 0; k < posts.Count && !between; k++)
                    {
                        if (k == i || k == j) continue;
                        between = on[k].Any(o => ReferenceEquals(o.Fence, fa.Fence) && o.Station > lo + 0.01 && o.Station < hi - 0.01);
                    }
                    if (between) continue;
                    return new Candidate { P = i, Q = j, D = d, Score = 3, Fence = fa.Fence,
                                           Evidence = "both posts on fence " + Name(fa.Fence) + ", next to each other" };
                }

            // The fence stops at both posts: the opening between two fence ends.
            var endA = a.FirstOrDefault(x => x.AtEnd);
            var endB = b.FirstOrDefault(x => x.AtEnd);
            if (endA != null && endB != null)
                return new Candidate { P = i, Q = j, D = d, Score = 3, Fence = endA.Fence,
                                       Evidence = "fence ends at both posts" };

            // Both on fences, neither where a fence stops: posts on two different fences
            // (parallel runs, a corner) are not one gate.
            if (a.Count > 0 && b.Count > 0)
            {
                if (endA == null && endB == null) return null;
                var end = endA ?? endB;
                return new Candidate { P = i, Q = j, D = d, Score = 2, Fence = end.Fence,
                                       Evidence = "a fence ends at one post, the other is on a fence" };
            }

            if (a.Count > 0 || b.Count > 0)
            {
                var f = (a.Count > 0 ? a : b)[0];
                return new Candidate { P = i, Q = j, D = d, Score = 2, Fence = f.Fence,
                                       Evidence = "one post on fence " + Name(f.Fence) };
            }

            return new Candidate { P = i, Q = j, D = d, Score = 1, Evidence = "no fence at either post" };
        }

        private static bool BestFor(Candidate c, int post, IList<Candidate> all, HashSet<int> paired, int minScore)
        {
            foreach (var o in all)
            {
                if (o == c || o.Score < minScore || (o.P != post && o.Q != post)) continue;
                var partner = o.P == post ? o.Q : o.P;
                if (paired.Contains(partner)) continue;
                if (o.Score > c.Score || (o.Score == c.Score && o.D < c.D)) return false;
            }
            return true;
        }

        private static Candidate Rival(Candidate c, int post, IList<Candidate> all, HashSet<int> paired, int minScore, double margin)
        {
            foreach (var o in all)
            {
                if (o == c || o.Score < minScore || (o.P != post && o.Q != post)) continue;
                var partner = o.P == post ? o.Q : o.P;
                if (paired.Contains(partner)) continue;
                if (o.Score == c.Score && o.D <= c.D * margin) return o;
            }
            return null;
        }

        private static List<HashSet<int>> Components(IList<int> nodes, IList<Candidate> edges)
        {
            var groups = new List<HashSet<int>>();
            var seen = new HashSet<int>();
            foreach (var n in nodes)
            {
                if (!seen.Add(n)) continue;
                var g = new HashSet<int> { n };
                var stack = new Stack<int>();
                stack.Push(n);
                while (stack.Count > 0)
                {
                    var x = stack.Pop();
                    foreach (var e in edges.Where(e => e.P == x || e.Q == x))
                    {
                        var y = e.P == x ? e.Q : e.P;
                        if (seen.Add(y)) { g.Add(y); stack.Push(y); }
                    }
                }
                groups.Add(g);
            }
            return groups;
        }

        /// <summary>The same post shot twice (or listed twice) counts once.</summary>
        private static IEnumerable<GatePost> Distinct(IEnumerable<GatePost> posts)
        {
            var kept = new List<GatePost>();
            foreach (var p in posts.Where(p => p != null).OrderBy(p => p.Id, StringComparer.Ordinal))
            {
                if (kept.Any(k => k.Id == p.Id || k.At.DistanceTo(p.At) < 0.1)) continue;
                kept.Add(p);
            }
            return kept;
        }

        private static List<OnFence> FencesAt(P2 p, IList<GateFence> fences, double reach)
        {
            var found = new List<OnFence>();
            foreach (var f in fences)
            {
                double offset, station, length;
                Closest(p, f.Points, out offset, out station, out length);
                if (offset > reach) continue;
                var atEnd = p.DistanceTo(f.Points[0]) <= reach || p.DistanceTo(f.Points[f.Points.Count - 1]) <= reach;
                // A closed fence has no ends.
                if (f.Points[0].DistanceTo(f.Points[f.Points.Count - 1]) < 0.01) atEnd = false;
                found.Add(new OnFence { Fence = f, Offset = offset, Station = station, AtEnd = atEnd });
            }
            return found.OrderBy(o => o.Offset).ToList();
        }

        /// <summary>Plan distance from a point to a polyline, and the distance along it there.</summary>
        public static void Closest(P2 p, IList<P2> line, out double offset, out double station, out double length)
        {
            offset = double.MaxValue;
            station = 0;
            length = 0;
            for (var k = 0; k + 1 < line.Count; k++)
            {
                var a = line[k];
                var b = line[k + 1];
                var ab = b - a;
                var len = ab.Length;
                double t = 0;
                if (len > 1e-12) t = Math.Max(0, Math.Min(1, P2.Dot(p - a, ab) / (len * len)));
                var q = a + ab * t;
                var dist = p.DistanceTo(q);
                if (dist < offset)
                {
                    offset = dist;
                    station = length + len * t;
                }
                length += len;
            }
            if (line.Count == 1) { offset = p.DistanceTo(line[0]); station = 0; }
        }

        private static string Name(GateFence f)
        {
            if (f == null) return string.Empty;
            return string.IsNullOrWhiteSpace(f.Code) ? (f.Layer ?? f.Id ?? string.Empty) : f.Code;
        }
    }
}
