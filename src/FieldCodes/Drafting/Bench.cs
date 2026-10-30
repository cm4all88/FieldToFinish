using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using FieldCodes.Easements;
using FieldCodes.Linework;

namespace FieldCodes.Drafting
{
    /// <summary>What one end shot's description says about the bench.</summary>
    public sealed class BenchReading
    {
        public double? DepthFeet { get; set; }
        public double? HeightFeet { get; set; }

        /// <summary>Numbers in the description with no D/H to say what they are. Shown to the
        /// person, never assigned: "1.5 1.5" could be depth then height or the other way.</summary>
        public List<string> UnreadNumbers { get; private set; }

        public List<string> Problems { get; private set; }

        public BenchReading()
        {
            UnreadNumbers = new List<string>();
            Problems = new List<string>();
        }
    }

    /// <summary>
    /// Reads a bench's depth and height out of the notes a crew writes on its end shots:
    /// D1.5, D 1.5, D=1.5, DEPTH 1.5, 1.5D, 1.5' DEEP, 18IN D; H1.5, HT 1.5, 1.5H, 16" HIGH.
    /// Feet unless the value is marked " or IN. A number with no letter beside it is not
    /// guessed at; it is reported.
    /// </summary>
    public static class BenchNotes
    {
        private const string Number = @"(?<v>[0-9]+(?:\.[0-9]+)?|\.[0-9]+)\s*(?<u>''|""|'|FT\.?|FEET|IN\.?|INCH(?:ES)?)?";
        private const string DepthKey = @"DEPTH|DEEP|DEP|DP|D";
        private const string HeightKey = @"HEIGHT|HGT|HIGH|HT|TALL|H";

        // One pass, leftmost first: "H 1.5 D 1.5" reads H then D; "1.5 D 1.5 H" reads 1.5D then
        // 1.5H. A key must stand apart from other letters (the D of BENCHD is not a depth).
        private static readonly Regex Keyed = new Regex(
            @"(?<![A-Z])(?<k>" + DepthKey + "|" + HeightKey + @")\s*[=:]?\s*" + Number +
            @"|" + Number + @"\s*(?<k>" + DepthKey + "|" + HeightKey + @")(?![A-Z])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex Bare = new Regex(@"(?<![A-Z0-9.])[0-9]+(?:\.[0-9]+)?(?![0-9.A-Z])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static BenchReading Read(string description)
        {
            var reading = new BenchReading();
            var text = Regex.Replace((description ?? string.Empty).Trim(), @"\s+", " ");
            // The code is the first word; trailing figure digits on it (BENCH2) are not data.
            var space = text.IndexOf(' ');
            var notes = space < 0 ? string.Empty : text.Substring(space + 1);
            if (notes.Length == 0) return reading;

            var used = new bool[notes.Length];
            foreach (Match m in Keyed.Matches(notes))
            {
                for (var i = m.Index; i < m.Index + m.Length; i++) used[i] = true;
                double value;
                if (!double.TryParse(m.Groups["v"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value) || value <= 0)
                {
                    reading.Problems.Add("'" + m.Value.Trim() + "' is not a size.");
                    continue;
                }
                var unit = m.Groups["u"].Value.ToUpperInvariant();
                if (unit == "\"" || unit == "''" || unit.StartsWith("IN", StringComparison.Ordinal)) value /= 12.0;

                var key = m.Groups["k"].Value.ToUpperInvariant();
                var isDepth = Regex.IsMatch(key, "^(?:" + DepthKey + ")$", RegexOptions.IgnoreCase);
                if (isDepth) Set(reading, true, value, m.Value.Trim());
                else Set(reading, false, value, m.Value.Trim());
            }

            foreach (Match m in Bare.Matches(notes))
            {
                var free = true;
                for (var i = m.Index; i < m.Index + m.Length; i++) if (used[i]) { free = false; break; }
                if (free) reading.UnreadNumbers.Add(m.Value);
            }
            return reading;
        }

        private static void Set(BenchReading reading, bool depth, double value, string written)
        {
            var existing = depth ? reading.DepthFeet : reading.HeightFeet;
            if (existing.HasValue && Math.Abs(existing.Value - value) > 0.005)
            {
                reading.Problems.Add("Two different " + (depth ? "depths" : "heights") + " are noted (" +
                    Feet(existing.Value) + " and " + written + ").");
                return;
            }
            if (depth) reading.DepthFeet = value; else reading.HeightFeet = value;
        }

        /// <summary>
        /// The two ends' readings as one: a value noted on either end is used; the same value
        /// on both is fine; two different values are a problem the person settles.
        /// </summary>
        public static BenchReading Combine(BenchReading a, BenchReading b)
        {
            var result = new BenchReading();
            foreach (var r in new[] { a, b })
            {
                if (r == null) continue;
                result.Problems.AddRange(r.Problems);
                result.UnreadNumbers.AddRange(r.UnreadNumbers);
                if (r.DepthFeet.HasValue) Set(result, true, r.DepthFeet.Value, Feet(r.DepthFeet.Value));
                if (r.HeightFeet.HasValue) Set(result, false, r.HeightFeet.Value, Feet(r.HeightFeet.Value));
            }
            return result;
        }

        private static string Feet(double value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture) + "'";
        }
    }

    /// <summary>The outline and label of one bench, in drawing units.</summary>
    public sealed class BenchPlan
    {
        /// <summary>Four corners in order around the outline; draw closed.</summary>
        public P2[] Corners { get; set; }
        public P2 Centre { get; set; }

        /// <summary>Direction of the bench (end to end), folded so the label never reads
        /// upside down.</summary>
        public double LabelRotationRadians { get; set; }

        public double LengthFeet { get; set; }
        public double DepthFeet { get; set; }
        public double? HeightFeet { get; set; }

        public IList<string> LabelLines { get; set; }

        /// <summary>The label as MText contents (lines joined with \P).</summary>
        public string LabelText { get; set; }

        /// <summary>Reasons the bench cannot be drawn. Empty when it can.</summary>
        public List<string> Problems { get; private set; }

        /// <summary>Drawable, but worth a look.</summary>
        public List<string> Warnings { get; private set; }

        public bool Ok { get { return Problems.Count == 0; } }

        public BenchPlan()
        {
            Corners = new P2[0];
            LabelLines = new List<string>();
            Problems = new List<string>();
            Warnings = new List<string>();
        }
    }

    /// <summary>
    /// A bench from its two end shots: the shots are the middles of the two ends, so the
    /// outline runs the shot-to-shot line offset half the depth to each side, and the ends
    /// close it. Nothing is adjusted -- the length is what was shot.
    /// </summary>
    public static class BenchPlanner
    {
        public static BenchPlan Plan(P2 endA, P2 endB, double depthFeet, double? heightFeet, string labelFormat, double unitsPerFoot)
        {
            var plan = new BenchPlan();
            if (unitsPerFoot <= 0) unitsPerFoot = 1.0;
            var along = endB - endA;
            var length = along.Length;
            plan.LengthFeet = length / unitsPerFoot;
            plan.DepthFeet = depthFeet;
            plan.HeightFeet = heightFeet;

            if (plan.LengthFeet < 0.05)
                plan.Problems.Add("The two end points are the same spot; a bench needs one shot at each end.");
            if (depthFeet <= 0)
                plan.Problems.Add("The depth must be greater than zero.");
            if (heightFeet.HasValue && heightFeet.Value <= 0)
                plan.Problems.Add("The height must be greater than zero.");
            if (!plan.Ok) return plan;

            if (depthFeet > plan.LengthFeet)
                plan.Warnings.Add(string.Format(CultureInfo.InvariantCulture,
                    "The depth ({0:0.##}') is more than the length between the shots ({1:0.##}'): check the two points are the bench's ends, not its front and back.",
                    depthFeet, plan.LengthFeet));
            if (plan.LengthFeet > 40.0)
                plan.Warnings.Add(string.Format(CultureInfo.InvariantCulture,
                    "The ends are {0:0.#}' apart, longer than any bench: check the points.", plan.LengthFeet));

            var u = along * (1.0 / length);
            var n = new P2(-u.Y, u.X);
            var half = depthFeet * unitsPerFoot / 2.0;
            plan.Corners = new[] { endA + n * half, endB + n * half, endB - n * half, endA - n * half };
            plan.Centre = new P2((endA.X + endB.X) / 2.0, (endA.Y + endB.Y) / 2.0);
            plan.LabelRotationRadians = LineLabelPlanner.NormalizeReadable(Math.Atan2(u.Y, u.X));

            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["length"] = plan.LengthFeet.ToString("R", CultureInfo.InvariantCulture),
                ["depth"] = depthFeet.ToString("R", CultureInfo.InvariantCulture)
            };
            if (heightFeet.HasValue) fields["height"] = heightFeet.Value.ToString("R", CultureInfo.InvariantCulture);
            plan.LabelLines = FieldCodeParser.ComposeLines(labelFormat ?? string.Empty, fields);
            plan.LabelText = string.Join("\\P", plan.LabelLines.ToArray());
            return plan;
        }

        /// <summary>True when the format shows the height, so it is worth asking for one.</summary>
        public static bool UsesHeight(string labelFormat)
        {
            return !string.IsNullOrEmpty(labelFormat) &&
                   Regex.IsMatch(labelFormat, @"\{height(?::[^}]*)?\}", RegexOptions.IgnoreCase);
        }

        /// <summary>True when the description's code is one of the bench codes (trailing figure
        /// digits ignored: BENCHL2 is BENCHL).</summary>
        public static bool IsBenchCode(string description, IEnumerable<string> codes)
        {
            var text = (description ?? string.Empty).Trim();
            if (text.Length == 0) return false;
            var space = text.IndexOf(' ');
            var code = (space < 0 ? text : text.Substring(0, space)).TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
            return (codes ?? Enumerable.Empty<string>()).Any(c => string.Equals(c, code, StringComparison.OrdinalIgnoreCase));
        }
    }
}
