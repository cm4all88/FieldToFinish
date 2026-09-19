using System;
using System.Collections.Generic;
using System.Linq;

namespace FieldCodes.Utilities
{
    /// <summary>
    /// The 16 survey-style directions offered as one-click buttons when a pipe is entered in the Dip Builder window
    /// (N, N/NE, NE, E/NE ...). Entry shortcuts only: the field-note parser is not changed and still reads exactly the
    /// directions it always has; a pipe whose direction is a bearing or an azimuth keeps it as observed.
    /// </summary>
    public static class DirectionShortcuts
    {
        /// <summary>The 16 directions, clockwise from north, with the azimuth each one points along.</summary>
        public static readonly IList<KeyValuePair<string, double>> All = new List<KeyValuePair<string, double>>
        {
            Pair("N", 0), Pair("N/NE", 22.5), Pair("NE", 45), Pair("E/NE", 67.5),
            Pair("E", 90), Pair("E/SE", 112.5), Pair("SE", 135), Pair("S/SE", 157.5),
            Pair("S", 180), Pair("S/SW", 202.5), Pair("SW", 225), Pair("W/SW", 247.5),
            Pair("W", 270), Pair("W/NW", 292.5), Pair("NW", 315), Pair("N/NW", 337.5)
        }.AsReadOnly();

        public static IEnumerable<string> Names { get { return All.Select(p => p.Key); } }

        private static KeyValuePair<string, double> Pair(string name, double azimuth)
        {
            return new KeyValuePair<string, double>(name, azimuth);
        }

        /// <summary>The direction for one of the 16 shortcut names; null for anything else.</summary>
        public static ObservedDirection For(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            var t = name.Trim().ToUpperInvariant();
            foreach (var p in All)
                if (p.Key == t)
                    return new ObservedDirection { Text = p.Key, Kind = DirectionKind.Cardinal, AzimuthDegrees = p.Value };
            return null;
        }

        /// <summary>
        /// A direction typed or picked in the window: anything the field-note parser reads (N, SW, N45E, AZ215, ?)
        /// exactly as it reads it, and otherwise one of the 16 shortcut names. Null when it is neither.
        /// </summary>
        public static ObservedDirection Parse(string text)
        {
            return DipNoteParser.ParseDirection(text) ?? For(text);
        }

        /// <summary>
        /// The shortcut nearest an azimuth -- what a click on the compass means. Each of the 16 covers 11.25 degrees
        /// either side of its own azimuth (N: 348.75 to 11.25).
        /// </summary>
        public static string Nearest(double azimuthDegrees)
        {
            var az = ((azimuthDegrees % 360.0) + 360.0) % 360.0;
            var index = (int)Math.Floor(az / 22.5 + 0.5) % 16;
            return All[index].Key;
        }

        /// <summary>
        /// The direction pointing back the other way, written the way the original was: N/NW becomes S/SE, a bearing
        /// N45E becomes S45W, an azimuth AZ215 becomes AZ35. Null for an unknown direction -- nothing is guessed.
        /// </summary>
        public static ObservedDirection Opposite(ObservedDirection direction)
        {
            if (direction == null || !direction.IsKnown || string.IsNullOrWhiteSpace(direction.Text)) return null;
            var text = direction.Text.Trim().ToUpperInvariant();

            var shortcut = For(text);
            if (shortcut != null) return For(Nearest(shortcut.AzimuthDegrees.Value + 180.0));

            var back = (direction.AzimuthDegrees.Value + 180.0) % 360.0;
            ObservedDirection result = null;
            if ((text[0] == 'N' || text[0] == 'S') && (text[text.Length - 1] == 'E' || text[text.Length - 1] == 'W'))
            {
                // A quadrant bearing: swap both letters, keep the angle exactly as written.
                var swapped = (text[0] == 'N' ? "S" : "N") + text.Substring(1, text.Length - 2) + (text[text.Length - 1] == 'E' ? "W" : "E");
                result = DipNoteParser.ParseDirection(swapped);
            }
            else if (text.StartsWith("AZ", StringComparison.Ordinal) || text.StartsWith("@", StringComparison.Ordinal))
            {
                // Turn only the whole degrees, so minutes and seconds keep the form they were written in.
                var prefix = text.StartsWith("@", StringComparison.Ordinal) ? "@" : "AZ";
                var body = text.Substring(prefix.Length);
                var dot = body.IndexOf('.');
                int degrees;
                if (int.TryParse(dot < 0 ? body : body.Substring(0, dot), out degrees))
                    result = DipNoteParser.ParseDirection(prefix + ((degrees + 180) % 360) + (dot < 0 ? string.Empty : body.Substring(dot)));
            }
            // Only trust the written form when it really points back the other way; otherwise nothing is guessed.
            if (result != null && result.IsKnown && Math.Abs(((result.AzimuthDegrees.Value - back) % 360.0 + 540.0) % 360.0 - 180.0) < 1e-6)
                return result;
            return null;
        }

        /// <summary>The shortcut button that shows this direction, or null when it was observed some other way
        /// (a bearing, an azimuth, unknown).</summary>
        public static string ButtonFor(ObservedDirection direction)
        {
            if (direction == null || string.IsNullOrWhiteSpace(direction.Text)) return null;
            var t = direction.Text.Trim().ToUpperInvariant();
            return All.Any(p => p.Key == t) ? t : null;
        }
    }
}
