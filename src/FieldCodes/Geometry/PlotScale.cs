using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace FieldCodes.Geometry
{
    /// <summary>
    /// How many drawing units one plotted inch of annotation covers, from the drawing's annotation
    /// scale and its units. Every FTF annotation height is a plotted height (0.08 in, say) times
    /// this.
    ///
    /// An annotation scale is only a ratio of drawing units to paper units, and the same name can
    /// carry different ratios: a "1\" = 20'" scale made for a drawing in feet is 1:20, but the
    /// same scale made the AutoCAD way, in inches, is 1:240. Taken as it stands, the second makes
    /// every label twelve times too big in a drawing in feet. So an imperial scale whose name says
    /// what it means ("1\" = 20'", "1/4\" = 1'-0\"", "1 in = 20 ft") is read by its name, in the
    /// drawing's own units (INSUNITS); anything else (1:20, metric scales) is taken by its ratio,
    /// as before.
    /// </summary>
    public static class PlotScale
    {
        public sealed class Reading
        {
            /// <summary>Drawing units per plotted inch.</summary>
            public double DrawingUnitsPerPlottedInch { get; set; }

            /// <summary>True when the scale's ratio disagreed with its name and the name was used.</summary>
            public bool Corrected { get; set; }

            /// <summary>How it was worked out, for messages and reports.</summary>
            public string Basis { get; set; }
        }

        /// <summary>Drawing units per foot for an INSUNITS code, or null when the drawing has none
        /// (unitless) or a unit FTF does not convert.</summary>
        public static double? UnitsPerFoot(int insunits)
        {
            switch (insunits)
            {
                case 1: return 12.0;            // inches
                case 2: return 1.0;             // feet
                case 4: return 304.8;           // millimetres
                case 5: return 30.48;           // centimetres
                case 6: return 0.3048;          // metres
                case 10: return 1.0 / 3.0;      // yards
                case 14: return 3.048;          // decimetres
                case 21: return 1.0;            // US survey feet
                case 22: return 12.0;           // US survey inches
                case 23: return 1.0 / 3.0;      // US survey yards
                default: return null;
            }
        }

        private static bool IsMetric(int insunits)
        {
            return insunits == 4 || insunits == 5 || insunits == 6 || insunits == 14;
        }

        private static string UnitName(int insunits)
        {
            switch (insunits)
            {
                case 1: case 22: return "inches";
                case 2: return "feet";
                case 21: return "US survey feet";
                case 10: case 23: return "yards";
                case 0: return "no units (feet assumed)";
                default: return "INSUNITS " + insunits.ToString(CultureInfo.InvariantCulture);
            }
        }

        private const string Number = @"(?:\d+(?:\.\d+)?|\d+/\d+|\d+\s*-\s*\d+/\d+|\d+\s+\d+/\d+)";
        private static readonly Regex Imperial = new Regex(
            @"^\s*(?<paper>" + Number + @")\s*(?:[""”″]|in\b|inch(?:es)?\b)\s*=\s*" +
            @"(?<feet>" + Number + @")\s*(?:['’′]|ft\b|feet\b|foot\b)" +
            @"(?:\s*-?\s*(?<inches>" + Number + @")\s*(?:[""”″]|in\b|inch(?:es)?\b))?\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>Feet on the ground per plotted inch, from a scale name such as
        /// "1\" = 20'", "1/4\" = 1'-0\"" or "1 in = 20 ft". False for any other name.</summary>
        public static bool TryReadName(string name, out double feetPerPlottedInch)
        {
            feetPerPlottedInch = 0;
            if (string.IsNullOrWhiteSpace(name)) return false;
            var m = Imperial.Match(name);
            if (!m.Success) return false;
            double paper, feet, inches = 0;
            if (!TryNumber(m.Groups["paper"].Value, out paper) || !TryNumber(m.Groups["feet"].Value, out feet)) return false;
            if (m.Groups["inches"].Success && !TryNumber(m.Groups["inches"].Value, out inches)) return false;
            var real = feet + inches / 12.0;
            if (paper <= 0 || real <= 0) return false;
            feetPerPlottedInch = real / paper;
            return true;
        }

        private static bool TryNumber(string text, out double value)
        {
            value = 0;
            var t = Regex.Replace(text.Trim(), @"\s*-\s*", " ");
            var parts = t.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                var slash = part.IndexOf('/');
                double v;
                if (slash > 0)
                {
                    double n, d;
                    if (!double.TryParse(part.Substring(0, slash), NumberStyles.Float, CultureInfo.InvariantCulture, out n) ||
                        !double.TryParse(part.Substring(slash + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out d) || d == 0) return false;
                    v = n / d;
                }
                else if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return false;
                value += v;
            }
            return parts.Length > 0;
        }

        /// <summary>
        /// Drawing units per plotted inch for an annotation scale (its name, paper units and
        /// drawing units, as the drawing holds it) in a drawing with the given INSUNITS.
        /// </summary>
        public static Reading Resolve(string scaleName, double paperUnits, double drawingUnits, int insunits)
        {
            if (paperUnits <= 0 || drawingUnits <= 0 || double.IsNaN(paperUnits) || double.IsNaN(drawingUnits))
                return new Reading { DrawingUnitsPerPlottedInch = 1.0, Basis = "no usable annotation scale: 1:1" };
            var ratio = drawingUnits / paperUnits;
            var label = string.IsNullOrWhiteSpace(scaleName) ? "the annotation scale" : "\"" + scaleName.Trim() + "\"";

            double feetPerInch;
            if (!IsMetric(insunits) && TryReadName(scaleName, out feetPerInch))
            {
                var unitsPerFoot = UnitsPerFoot(insunits) ?? 1.0;      // unitless survey drawings are in feet
                var expected = feetPerInch * unitsPerFoot;
                if (Math.Abs(ratio - expected) <= expected * 1e-6)
                    return new Reading { DrawingUnitsPerPlottedInch = expected, Basis = label + " in a drawing in " + UnitName(insunits) };
                return new Reading
                {
                    DrawingUnitsPerPlottedInch = expected, Corrected = true,
                    Basis = string.Format(CultureInfo.InvariantCulture,
                        "{0} is defined as 1:{1:0.####}, but in a drawing in {2} it means {3:0.####} drawing units per plotted inch; the name is used",
                        label, ratio, UnitName(insunits), expected)
                };
            }
            return new Reading
            {
                DrawingUnitsPerPlottedInch = ratio,
                Basis = string.Format(CultureInfo.InvariantCulture, "{0} (1:{1:0.####})", label, ratio)
            };
        }
    }
}
