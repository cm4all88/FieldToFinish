using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using FieldCodes.Easements;
using FieldCodes.Geometry;

namespace FieldCodes.Orient
{
    /// <summary>
    /// Directions and rotations, in one place. Survey azimuth: degrees clockwise from north.
    /// AutoCAD angle: degrees counter-clockwise from east. A marker rotation turns the block
    /// about its insertion point, so the rotation that points a symbol's reference direction
    /// at an azimuth is that azimuth's AutoCAD angle minus the reference's angle in the block,
    /// reduced to the symbol's period: a bowtie valve turned 190 degrees looks the same as one
    /// turned 10, and is written as 10.
    /// </summary>
    public static class SymbolRotation
    {
        /// <summary>Marker rotation in AutoCAD degrees [0, period), or null for a symbol that
        /// looks the same at every rotation.</summary>
        public static double? MarkerDegrees(SymbolRule symbol, double azimuth)
        {
            if (symbol == null || !symbol.Directional) return null;
            var cad = Angles.AzimuthToCadDegrees(azimuth);
            var r = Angles.NormalizeDegrees(cad - symbol.ReferenceCadDegrees);
            if (symbol.Period < 360 - 1e-9) r %= symbol.Period;
            if (r > symbol.Period - 1e-9) r = 0;
            return r;
        }

        /// <summary>
        /// The marker rotation (AutoCAD degrees) for a parsed point. A field azimuth ("SN 135")
        /// is the direction the feature faces, so when the standard knows the code's symbol the
        /// block's own zero is taken off: a PMX sign, drawn facing north, turns 315 - 90 = 225
        /// to face 135, where turning it by the azimuth's AutoCAD angle alone (315) would leave it
        /// facing 45. A code the standard does not know, a symmetric symbol, or a rule that gives
        /// a plain AutoCAD angle keeps the angle as given.
        /// </summary>
        public static double MarkerDegreesFor(OrientationStandard std, ParsedPoint p)
        {
            if (p == null || !p.RotationDegrees.HasValue) throw new ArgumentException("no rotation", "p");
            var cad = p.RotationDegrees.Value;
            if (!p.RotationIsAzimuth || std == null) return cad;
            var marker = MarkerDegrees(std.SymbolFor(p.Code), Angles.NormalizeDegrees(90.0 - cad));
            return marker ?? cad;
        }

        /// <summary>The same as API radians (what CogoPoint.MarkerRotation takes).</summary>
        public static double? MarkerRadians(SymbolRule symbol, double azimuth)
        {
            var d = MarkerDegrees(symbol, azimuth);
            return d.HasValue ? Angles.CadDegreesToApiRadians(d.Value) : (double?)null;
        }

        /// <summary>The azimuth a marker rotation (radians) points the symbol's reference at.</summary>
        public static double AzimuthOfRotation(SymbolRule symbol, double markerRadians)
        {
            var cad = Angles.ToDegrees(markerRadians) + (symbol != null ? symbol.ReferenceCadDegrees : 0);
            return Angles.NormalizeDegrees(90.0 - cad);
        }

        public static double AzimuthOf(P2 direction)
        {
            return Angles.NormalizeDegrees(Angles.ToDegrees(Math.Atan2(direction.X, direction.Y)));
        }

        public static P2 DirectionOf(double azimuth)
        {
            var a = Angles.ToRadians(azimuth);
            return new P2(Math.Sin(a), Math.Cos(a));
        }

        /// <summary>Smallest difference between two azimuths, in [0, 180] (or [0, period/2]).</summary>
        public static double Difference(double a, double b, double period = 360)
        {
            var p = period <= 0 ? 360 : period;
            var d = Math.Abs(a - b) % p;
            return Math.Min(d, p - d);
        }

        public static double Reverse(double azimuth) { return Angles.NormalizeDegrees(azimuth + 180); }
    }

    /// <summary>
    /// Directions the crew recorded in a description or note. Any of these is a measurement:
    /// "AZ 135" / "AZ=135" / "135AZ"; "FACES NE" / "FACING SW"; for a hydrant "NOZ N",
    /// "STEAMER E", "PUMPER 270"; for a light "ARM W", "ARMS N S"; for a valve an axis "N-S",
    /// "NE-SW". Several arms give several directions.
    /// </summary>
    public static class MeasuredDirection
    {
        private static readonly Dictionary<string, double> Cardinal = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            { "N", 0 }, { "NNE", 22.5 }, { "NE", 45 }, { "ENE", 67.5 }, { "E", 90 }, { "ESE", 112.5 }, { "SE", 135 },
            { "SSE", 157.5 }, { "S", 180 }, { "SSW", 202.5 }, { "SW", 225 }, { "WSW", 247.5 }, { "W", 270 },
            { "WNW", 292.5 }, { "NW", 315 }, { "NNW", 337.5 },
            { "NORTH", 0 }, { "EAST", 90 }, { "SOUTH", 180 }, { "WEST", 270 }
        };

        private const string Dir = @"(?<d>NNE|ENE|ESE|SSE|SSW|WSW|WNW|NNW|NORTH|EAST|SOUTH|WEST|NE|SE|SW|NW|N|E|S|W|[0-9]{1,3}(?:\.[0-9]+)?)";

        private static readonly Regex Azimuth = new Regex(@"\bAZ\s*[=:]?\s*(?<a>[0-9]{1,3}(?:\.[0-9]+)?)\b|\b(?<a>[0-9]{1,3}(?:\.[0-9]+)?)\s*AZ\b",
                                                          RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex Facing = new Regex(@"\b(?:FACES|FACING|FACE|FCG|NOZ|NOZZLE|STEAMER|PUMPER|STMR|ARMS?|POINTS?|TO)\s+(?<list>" + Dir + @"(?:\s*[,&/ ]\s*" + Dir + @")*)\b",
                                                         RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex Axis = new Regex(@"\b(?<a>NNE|ENE|ESE|SSE|SSW|WSW|WNW|NNW|NE|SE|SW|NW|N|E|S|W)\s*-\s*(?<b>NNE|ENE|ESE|SSE|SSW|WSW|WNW|NNW|NE|SE|SW|NW|N|E|S|W)\b|\b(?<ns>NS|EW)\b",
                                                       RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>All directions recorded, in the order written; empty when none.</summary>
        public static IList<double> Read(string description, string note, OrientKind kind)
        {
            var found = new List<double>();
            foreach (var text in new[] { description, note })
            {
                if (string.IsNullOrWhiteSpace(text)) continue;
                foreach (Match m in Azimuth.Matches(text))
                    Add(found, Parse(m.Groups["a"].Value));
                foreach (Match m in Facing.Matches(text))
                    foreach (Match d in Regex.Matches(m.Groups["list"].Value, Dir, RegexOptions.IgnoreCase))
                        Add(found, Parse(d.Value));
                if (kind == OrientKind.WaterValve || kind == OrientKind.GasValve)
                    foreach (Match m in Axis.Matches(text))
                    {
                        if (m.Groups["ns"].Success) Add(found, m.Groups["ns"].Value.ToUpperInvariant() == "NS" ? 0 : 90);
                        else Add(found, Parse(m.Groups["a"].Value));
                    }
            }
            return found;
        }

        private static void Add(List<double> list, double? azimuth)
        {
            if (!azimuth.HasValue) return;
            var a = Angles.NormalizeDegrees(azimuth.Value);
            if (!list.Any(x => SymbolRotation.Difference(x, a) < 1e-6)) list.Add(a);
        }

        private static double? Parse(string token)
        {
            double v;
            if (Cardinal.TryGetValue(token.Trim(), out v)) return v;
            if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out v) && v >= 0 && v <= 360) return v;
            return null;
        }
    }
}
