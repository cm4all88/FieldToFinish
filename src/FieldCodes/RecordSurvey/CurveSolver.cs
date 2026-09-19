using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FieldCodes.Easements;

namespace FieldCodes.RecordSurvey
{
    /// <summary>The full set of curve elements reconciled from whatever the document stated.</summary>
    public sealed class CurveSolution
    {
        public bool Ok { get; set; }
        public string Error { get; set; }
        public double Radius { get; set; }
        public double DeltaDegrees { get; set; }
        public double ArcLength { get; set; }
        public double ChordLength { get; set; }
        public double TangentLength { get; set; }
        /// <summary>Which two stated elements the solution was computed from.</summary>
        public string SolvedFrom { get; set; }
        /// <summary>Elements that were not stated and had to be calculated.</summary>
        public List<string> Calculated { get; private set; }
        /// <summary>Stated elements that disagree with the solution beyond tolerance -- "arc length 197.22' stated, 197.20' by R and Δ".</summary>
        public List<string> Disagreements { get; private set; }
        public double MaxDistanceDisagreement { get; set; }
        public double MaxAngleDisagreementSeconds { get; set; }

        public CurveSolution()
        {
            Calculated = new List<string>();
            Disagreements = new List<string>();
        }

        [JsonIgnoreAttributeStub] public double DeltaRadians { get { return DeltaDegrees * Math.PI / 180.0; } }
    }

    /// <summary>Placeholder attribute so the solution stays a plain object (no JSON attributes needed here).</summary>
    [AttributeUsage(AttributeTargets.Property)]
    internal sealed class JsonIgnoreAttributeStub : Attribute { }

    /// <summary>A curve placed in the plane.</summary>
    public sealed class PlacedCurve
    {
        public bool Ok { get; set; }
        public string Error { get; set; }
        public Course Course { get; set; }
        public double ChordAzimuthDegrees { get; set; }
        public double TangentInAzimuthDegrees { get; set; }
        public double TangentOutAzimuthDegrees { get; set; }
        public bool TurnsLeft { get; set; }
        /// <summary>How the curve was oriented: "chord bearing", "tangent to the previous course", "radial bearing".</summary>
        public string Method { get; set; }
        public List<string> Notes { get; private set; }

        public PlacedCurve() { Notes = new List<string>(); }
    }

    /// <summary>
    /// Curve mathematics for recorded calls. Any two independent elements of radius, central
    /// angle, arc length, chord length and tangent length define the curve; the rest are
    /// calculated and every stated element is checked against the result. Nothing is adjusted:
    /// a stated arc that disagrees with R and Δ is reported, and the geometry is built from the
    /// elements the surveyor chose to trust (radius and central angle first, as the field does).
    /// </summary>
    public static class CurveSolver
    {
        /// <summary>
        /// Reconciles the stated elements. Preference order for the defining pair: R+Δ, R+L,
        /// R+CH, R+T, Δ+L, Δ+CH, Δ+T, L+CH, L+T, CH+T. A single element, or none, is refused.
        /// </summary>
        public static CurveSolution Solve(CurveSpec spec, double distanceTolerance, double angleToleranceSeconds)
        {
            var s = new CurveSolution();
            if (spec == null) { s.Error = "No curve data."; return s; }

            var r = Positive(spec.Radius);
            var d = spec.DeltaDegrees.HasValue && spec.DeltaDegrees.Value > 0 && spec.DeltaDegrees.Value < 360 ? spec.DeltaDegrees.Value * Math.PI / 180.0 : (double?)null;
            var l = Positive(spec.ArcLength);
            var ch = Positive(spec.ChordLength);
            var t = Positive(spec.TangentLength);

            double R, D;
            if (r.HasValue && d.HasValue) { R = r.Value; D = d.Value; s.SolvedFrom = "R and Δ"; }
            else if (r.HasValue && l.HasValue) { R = r.Value; D = l.Value / R; s.SolvedFrom = "R and L"; }
            else if (r.HasValue && ch.HasValue)
            {
                var ratio = ch.Value / (2.0 * r.Value);
                if (ratio > 1.0 + 1e-9) { s.Error = "The chord (" + Ft(ch.Value) + ") is longer than the diameter (" + Ft(2 * r.Value) + "); the radius or chord is misread."; return s; }
                R = r.Value; D = 2.0 * Math.Asin(Math.Min(1.0, ratio)); s.SolvedFrom = "R and CH";
                s.Calculated.Add("Δ from R and CH assumes the minor arc (Δ under 180°).");
            }
            else if (r.HasValue && t.HasValue) { R = r.Value; D = 2.0 * Math.Atan(t.Value / R); s.SolvedFrom = "R and T"; }
            else if (d.HasValue && l.HasValue) { D = d.Value; R = l.Value / D; s.SolvedFrom = "Δ and L"; }
            else if (d.HasValue && ch.HasValue) { D = d.Value; R = ch.Value / (2.0 * Math.Sin(D / 2.0)); s.SolvedFrom = "Δ and CH"; }
            else if (d.HasValue && t.HasValue) { D = d.Value; R = t.Value / Math.Tan(D / 2.0); s.SolvedFrom = "Δ and T"; }
            else if (l.HasValue && ch.HasValue)
            {
                if (ch.Value > l.Value + 1e-9) { s.Error = "The chord (" + Ft(ch.Value) + ") is longer than the arc (" + Ft(l.Value) + "); one of them is misread."; return s; }
                if (!SolveDeltaFromArcAndChord(l.Value, ch.Value, out D)) { s.Error = "Arc and chord do not define a curve (the chord is too short for the arc)."; return s; }
                R = l.Value / D; s.SolvedFrom = "L and CH";
            }
            else if (l.HasValue && t.HasValue)
            {
                if (!SolveDeltaFromArcAndTangent(l.Value, t.Value, out D)) { s.Error = "Arc and tangent do not define a curve."; return s; }
                R = l.Value / D; s.SolvedFrom = "L and T";
            }
            else if (ch.HasValue && t.HasValue)
            {
                var cosHalf = ch.Value / (2.0 * t.Value);
                if (cosHalf >= 1.0 || cosHalf <= 0) { s.Error = "Chord and tangent do not define a curve (the chord must be shorter than twice the tangent)."; return s; }
                D = 2.0 * Math.Acos(cosHalf); R = t.Value / Math.Tan(D / 2.0); s.SolvedFrom = "CH and T";
            }
            else
            {
                s.Error = "A curve needs two of radius, central angle, arc length, chord length and tangent; only " +
                          Count(r, d, l, ch, t) + " readable element(s) were found.";
                return s;
            }

            if (R <= 0 || double.IsNaN(R) || double.IsInfinity(R) || D <= 0 || D >= 2 * Math.PI)
            {
                s.Error = "The stated elements do not describe a real curve.";
                return s;
            }

            s.Ok = true;
            s.Radius = R;
            s.DeltaDegrees = D * 180.0 / Math.PI;
            s.ArcLength = R * D;
            s.ChordLength = 2.0 * R * Math.Sin(D / 2.0);
            s.TangentLength = D < Math.PI - 1e-9 ? R * Math.Tan(D / 2.0) : double.NaN;

            if (!r.HasValue) s.Calculated.Add("R");
            if (!d.HasValue) s.Calculated.Add("Δ");
            if (!l.HasValue) s.Calculated.Add("L");
            if (!ch.HasValue) s.Calculated.Add("CH");
            if (!t.HasValue) s.Calculated.Add("T");

            // Every stated element is checked against the solution; over-specified curves
            // that disagree are reported, never averaged.
            Check(s, "radius", r, s.Radius, distanceTolerance);
            Check(s, "arc length", l, s.ArcLength, distanceTolerance);
            Check(s, "chord length", ch, s.ChordLength, distanceTolerance);
            if (!double.IsNaN(s.TangentLength)) Check(s, "tangent", t, s.TangentLength, distanceTolerance);
            if (d.HasValue)
            {
                var seconds = Math.Abs(d.Value - D) * 180.0 / Math.PI * 3600.0;
                s.MaxAngleDisagreementSeconds = Math.Max(s.MaxAngleDisagreementSeconds, seconds);
                if (seconds > angleToleranceSeconds)
                    s.Disagreements.Add(string.Format(CultureInfo.InvariantCulture,
                        "central angle {0} stated, {1} by {2} ({3:0.#}\" apart)",
                        Drafting.SurveyDirection.FormatAzimuth(d.Value * 180.0 / Math.PI, 0, "°"),
                        Drafting.SurveyDirection.FormatAzimuth(s.DeltaDegrees, 0, "°"), s.SolvedFrom, seconds));
            }
            return s;
        }

        private static void Check(CurveSolution s, string name, double? stated, double solved, double tolerance)
        {
            if (!stated.HasValue) return;
            var diff = Math.Abs(stated.Value - solved);
            s.MaxDistanceDisagreement = Math.Max(s.MaxDistanceDisagreement, diff);
            if (diff > tolerance)
                s.Disagreements.Add(string.Format(CultureInfo.InvariantCulture, "{0} {1} stated, {2} by {3} ({4} apart)",
                    name, Ft(stated.Value), Ft(solved), s.SolvedFrom, Ft(diff)));
        }

        private static double? Positive(double? v)
        {
            return v.HasValue && v.Value > 0 && !double.IsNaN(v.Value) && !double.IsInfinity(v.Value) ? v : null;
        }

        private static int Count(params double?[] values) { return values.Count(v => v.HasValue); }

        internal static string Ft(double feet) { return feet.ToString("0.00", CultureInfo.InvariantCulture) + "'"; }

        /// <summary>2R sin(Δ/2) = CH with L = RΔ gives sin(Δ/2)/(Δ/2) = CH/L: bisection on Δ in (0, 2π).</summary>
        private static bool SolveDeltaFromArcAndChord(double arc, double chord, out double delta)
        {
            delta = 0;
            var target = chord / arc;                  // sin(x)/x with x = Δ/2, decreasing on (0, π)
            if (target >= 1.0 - 1e-12) { delta = 1e-9; return false; }
            double lo = 1e-9, hi = Math.PI - 1e-9;
            for (var i = 0; i < 200; i++)
            {
                var mid = (lo + hi) / 2.0;
                var f = Math.Sin(mid) / mid;
                if (f > target) lo = mid; else hi = mid;
            }
            delta = 2.0 * ((lo + hi) / 2.0);
            return delta > 0 && delta < 2 * Math.PI;
        }

        /// <summary>T/L = tan(Δ/2)/Δ: increasing on (0, π), bisection.</summary>
        private static bool SolveDeltaFromArcAndTangent(double arc, double tangent, out double delta)
        {
            delta = 0;
            var target = tangent / arc;
            if (target <= 0.5) return false;           // tan(x)/(2x) -> 1/2 as x -> 0
            double lo = 1e-9, hi = Math.PI - 1e-6;
            for (var i = 0; i < 200; i++)
            {
                var mid = (lo + hi) / 2.0;
                var f = Math.Tan(mid / 2.0) / mid;
                if (f < target) lo = mid; else hi = mid;
            }
            delta = (lo + hi) / 2.0;
            return delta > 0 && delta < Math.PI;
        }

        // ------------------------------------------------------------ placement

        /// <summary>
        /// Places a solved curve from its start point. The orientation comes, in order of
        /// preference, from a stated chord bearing, from tangency to the previous course
        /// (<paramref name="tangentInAzimuth"/>), or from a stated radial bearing; the turn
        /// from the stated direction or, with a chord bearing and a previous course, from
        /// the geometry. When none of these settles the curve it is refused -- a curve is
        /// never drawn on a guessed side.
        /// </summary>
        public static PlacedCurve Place(CurveSolution solution, CurveSpec spec, P2 start, double? tangentInAzimuth, bool reversed)
        {
            var p = new PlacedCurve();
            if (solution == null || !solution.Ok) { p.Error = solution != null ? solution.Error : "No curve solution."; return p; }

            var delta = solution.DeltaRadians;
            var chord = solution.ChordLength;
            var turn = ParseTurn(spec.Turn);
            if (reversed && turn.HasValue) turn = !turn.Value;

            double? chordAz = spec.ChordAzimuthDegrees;
            if (chordAz.HasValue && reversed) chordAz = Geometry.Angles.NormalizeDegrees(chordAz.Value + 180.0);
            double? radialAz = spec.RadialInAzimuthDegrees;

            if (chordAz.HasValue)
            {
                p.Method = "chord bearing";
                if (!turn.HasValue && tangentInAzimuth.HasValue)
                {
                    // The side is settled by which way the chord leaves the incoming tangent.
                    var swing = Geometry.Angles.NormalizeDegrees(chordAz.Value - tangentInAzimuth.Value);
                    if (swing > 180.0) swing -= 360.0;
                    if (Math.Abs(swing) < 1.0 / 3600.0) { p.Error = "The chord bearing equals the incoming tangent; the curve's side cannot be determined."; return p; }
                    turn = swing < 0;   // chord swings counter-clockwise (left) from the tangent
                    p.Notes.Add("Turn direction taken from the chord bearing against the previous course: " + (turn.Value ? "LEFT" : "RIGHT") + ".");
                }
                if (!turn.HasValue) { p.Error = "The curve's turn direction (left or right) is not stated and there is no previous course to settle it. Enter it in the review."; return p; }

                if (tangentInAzimuth.HasValue && spec.TangentToPrevious != false)
                {
                    // Tangency check: for a tangent curve the chord sits Δ/2 off the tangent.
                    var expected = Geometry.Angles.NormalizeDegrees(tangentInAzimuth.Value + (turn.Value ? -1 : 1) * solution.DeltaDegrees / 2.0);
                    var off = Math.Abs(AngleDiff(expected, chordAz.Value));
                    if (off * 3600.0 > 30.0)
                        p.Notes.Add(string.Format(CultureInfo.InvariantCulture,
                            "Non-tangent to the previous course: the chord bearing sits {0:0.#}\" from a tangent curve's chord.", off * 3600.0));
                }
            }
            else if (tangentInAzimuth.HasValue && spec.TangentToPrevious != false)
            {
                p.Method = "tangent to the previous course";
                if (!turn.HasValue) { p.Error = "The curve's turn direction (left or right) is not stated. Enter it in the review; it cannot be inferred."; return p; }
                chordAz = Geometry.Angles.NormalizeDegrees(tangentInAzimuth.Value + (turn.Value ? -1 : 1) * solution.DeltaDegrees / 2.0);
                if (spec.TangentToPrevious == null) p.Notes.Add("Assumed tangent to the previous course (the document does not say).");
            }
            else if (radialAz.HasValue)
            {
                p.Method = "radial bearing";
                if (!turn.HasValue) { p.Error = "A radial bearing places the curve only with a stated turn direction."; return p; }
                var radial = reversed ? radialAz.Value : radialAz.Value;
                // Travelling with the centre on the left (a left turn) the tangent is the radial turned 90° clockwise.
                var tangentAz = Geometry.Angles.NormalizeDegrees(radial + (turn.Value ? 90.0 : -90.0));
                chordAz = Geometry.Angles.NormalizeDegrees(tangentAz + (turn.Value ? -1 : 1) * solution.DeltaDegrees / 2.0);
            }
            else
            {
                p.Error = "The curve cannot be placed: no chord bearing, no radial bearing, and no previous course to be tangent to. Enter a chord bearing in the review.";
                return p;
            }

            var az = chordAz.Value * Math.PI / 180.0;
            var end = new P2(start.X + chord * Math.Sin(az), start.Y + chord * Math.Cos(az));
            var mid = (start + end) * 0.5;
            var chordDir = (end - start).Normalized();
            var left = chordDir.LeftNormal();
            var m = Math.Sqrt(Math.Max(0, solution.Radius * solution.Radius - chord * chord / 4.0));
            // Minor arc: the centre is on the inside of the turn; major arc: on the outside.
            var inside = delta <= Math.PI;
            var centre = turn.Value
                ? (inside ? mid + left * m : mid - left * m)
                : (inside ? mid - left * m : mid + left * m);

            p.Course = Course.Arc(start, end, centre, turn.Value);
            p.Ok = true;
            p.TurnsLeft = turn.Value;
            p.ChordAzimuthDegrees = chordAz.Value;
            p.TangentInAzimuthDegrees = Geometry.Angles.NormalizeDegrees(chordAz.Value - (turn.Value ? -1 : 1) * solution.DeltaDegrees / 2.0);
            p.TangentOutAzimuthDegrees = Geometry.Angles.NormalizeDegrees(chordAz.Value + (turn.Value ? -1 : 1) * solution.DeltaDegrees / 2.0);
            return p;
        }

        /// <summary>"LEFT"/"L"/"LT" true, "RIGHT"/"R"/"RT" false, anything else null.</summary>
        public static bool? ParseTurn(string turn)
        {
            var t = (turn ?? string.Empty).Trim().ToUpperInvariant();
            if (t == "LEFT" || t == "L" || t == "LT" || t == "CCW") return true;
            if (t == "RIGHT" || t == "R" || t == "RT" || t == "CW") return false;
            return null;
        }

        /// <summary>Signed difference b - a in degrees, folded into (-180, 180].</summary>
        public static double AngleDiff(double a, double b)
        {
            var d = (b - a) % 360.0;
            if (d <= -180.0) d += 360.0;
            if (d > 180.0) d -= 360.0;
            return d;
        }

        // ------------------------------------------------------------ table columns

        /// <summary>
        /// A curve table row without a usable header gives three distances and an angle in
        /// unknown order. The curve equations say which is which: only one assignment of
        /// radius, arc and chord satisfies L = RΔ and CH = 2R sin(Δ/2). Returns null when no
        /// assignment fits or more than one does.
        /// </summary>
        public static CurveSpec IdentifyColumns(IList<double> distances, double deltaDegrees, double tolerance)
        {
            if (distances == null || distances.Count < 2 || distances.Count > 3 || deltaDegrees <= 0) return null;
            var delta = deltaDegrees * Math.PI / 180.0;
            var found = new List<CurveSpec>();
            var values = distances.ToArray();
            foreach (var perm in Permutations(values.Length))
            {
                var radius = values[perm[0]];
                var second = values[perm[1]];
                var third = values.Length == 3 ? values[perm[2]] : (double?)null;
                var arc = radius * delta;
                var chord = 2.0 * radius * Math.Sin(delta / 2.0);
                // second = arc, third = chord (or only the arc, or only the chord)
                if (values.Length == 3)
                {
                    if (Math.Abs(second - arc) <= tolerance && Math.Abs(third.Value - chord) <= tolerance)
                        found.Add(new CurveSpec { Radius = radius, DeltaDegrees = deltaDegrees, ArcLength = second, ChordLength = third, StatedElements = { "R", "DELTA", "L", "CH" } });
                }
                else
                {
                    if (Math.Abs(second - arc) <= tolerance)
                        found.Add(new CurveSpec { Radius = radius, DeltaDegrees = deltaDegrees, ArcLength = second, StatedElements = { "R", "DELTA", "L" } });
                    else if (Math.Abs(second - chord) <= tolerance)
                        found.Add(new CurveSpec { Radius = radius, DeltaDegrees = deltaDegrees, ChordLength = second, StatedElements = { "R", "DELTA", "CH" } });
                }
            }
            // Distinct assignments only: with two equal distances the same spec appears twice.
            var distinct = new List<CurveSpec>();
            foreach (var f in found)
                if (!distinct.Any(d => Same(d, f))) distinct.Add(f);
            return distinct.Count == 1 ? distinct[0] : null;
        }

        private static bool Same(CurveSpec a, CurveSpec b)
        {
            return Eq(a.Radius, b.Radius) && Eq(a.ArcLength, b.ArcLength) && Eq(a.ChordLength, b.ChordLength);
        }

        private static bool Eq(double? a, double? b)
        {
            return a.HasValue == b.HasValue && (!a.HasValue || Math.Abs(a.Value - b.Value) < 1e-9);
        }

        private static IEnumerable<int[]> Permutations(int n)
        {
            if (n == 2) { yield return new[] { 0, 1 }; yield return new[] { 1, 0 }; yield break; }
            yield return new[] { 0, 1, 2 }; yield return new[] { 0, 2, 1 }; yield return new[] { 1, 0, 2 };
            yield return new[] { 1, 2, 0 }; yield return new[] { 2, 0, 1 }; yield return new[] { 2, 1, 0 };
        }
    }
}
