using System;
using System.Globalization;

namespace FieldCodes.Drafting
{
    /// <summary>Outcome of parsing a typed direction or distance. Never guesses:
    /// anything ambiguous or malformed is refused with a message that says what a
    /// valid entry looks like.</summary>
    public sealed class SurveyValueParse
    {
        public bool Ok { get; private set; }

        /// <summary>Azimuth in degrees clockwise from north for directions; survey
        /// feet for distances. Meaningless when <see cref="Ok"/> is false.</summary>
        public double Value { get; private set; }

        /// <summary>Why the entry was refused. Null when it parsed.</summary>
        public string Error { get; private set; }

        public static SurveyValueParse Success(double value)
        {
            return new SurveyValueParse { Ok = true, Value = value };
        }

        public static SurveyValueParse Failure(string error)
        {
            return new SurveyValueParse { Ok = false, Error = error };
        }
    }

    /// <summary>
    /// Quadrant bearings and azimuths for the survey drafting commands: strict
    /// parsing of what the user types, and formatting of what the geometry actually
    /// produced. The annotation never repeats the typed value -- it is recomputed
    /// from the drawn line's own endpoints, so the text can never disagree with the
    /// geometry.
    ///
    /// Everything works in survey azimuth (degrees clockwise from north); the
    /// existing <see cref="FieldCodes.Geometry.Angles"/> conversions carry that to
    /// AutoCAD's conventions. Pure: no Autodesk types, fully unit tested.
    /// </summary>
    public static class SurveyDirection
    {
        // ------------------------------------------------------------------ parsing

        /// <summary>
        /// Parses a quadrant bearing: "N45.2536E" (D.MMSS, the Civil 3D entry
        /// convention: 45°25'36"), "N 42 18 36 E", "N42d18'36\"E", "S 10°00'30\" W".
        /// The angle must sit between 0 and 90. Returns the equivalent azimuth in
        /// degrees.
        ///
        /// The dot form is ALWAYS packed D.MMSS, never decimal degrees -- the two
        /// readings collide on the same digits, and the office types bearings the
        /// Civil 3D way. Decimal degrees have no entry form here on purpose.
        /// </summary>
        public static SurveyValueParse ParseBearing(string text)
        {
            var s = (text ?? string.Empty).Trim().ToUpperInvariant();
            if (s.Length < 2)
                return SurveyValueParse.Failure(
                    "Enter a quadrant bearing such as N 42 18 36 E or N42d18'36\"E.");

            var ns = s[0];
            var ew = s[s.Length - 1];
            if ((ns != 'N' && ns != 'S') || (ew != 'E' && ew != 'W'))
                return SurveyValueParse.Failure(
                    "A bearing starts with N or S and ends with E or W: N45.2536E.");

            var body = s.Substring(1, s.Length - 2).Trim();
            if (body.Length == 0)
                return SurveyValueParse.Failure(
                    "A bearing needs an angle between the quadrant letters: N45.2536E.");

            double angle;
            string error;
            if (!TryParseDms(body, out angle, out error))
                return SurveyValueParse.Failure(error);

            if (angle < 0 || angle > 90.0)
                return SurveyValueParse.Failure(string.Format(CultureInfo.InvariantCulture,
                    "A bearing angle must be between 0 and 90 degrees; got {0:0.######}.",
                    angle));

            double azimuth;
            if (ns == 'N') azimuth = ew == 'E' ? angle : 360.0 - angle;
            else azimuth = ew == 'E' ? 180.0 - angle : 180.0 + angle;

            return SurveyValueParse.Success(Geometry.Angles.NormalizeDegrees(azimuth));
        }

        /// <summary>
        /// Parses an azimuth (clockwise from north): "215.3000" (D.MMSS, the Civil
        /// 3D entry convention: 215°30'00") or degrees-minutes-seconds ("215 30 00",
        /// "215°30'00\""). 0 to 360. The dot form is always packed D.MMSS, matching
        /// the bearing entry -- one convention for every angle typed.
        /// </summary>
        public static SurveyValueParse ParseAzimuth(string text)
        {
            var body = (text ?? string.Empty).Trim();
            if (body.Length == 0)
                return SurveyValueParse.Failure(
                    "Enter an azimuth in D.MMSS (215.3000 = 215d30'00\") or DMS " +
                    "(215 30 00).");

            double angle;
            string error;
            if (!TryParseDms(body, out angle, out error))
                return SurveyValueParse.Failure(error);

            if (angle < 0 || angle > 360.0)
                return SurveyValueParse.Failure(string.Format(CultureInfo.InvariantCulture,
                    "An azimuth must be between 0 and 360 degrees; got {0:0.######}.",
                    angle));

            return SurveyValueParse.Success(Geometry.Angles.NormalizeDegrees(angle));
        }

        /// <summary>
        /// Parses a distance in survey feet. A trailing foot symbol or "FT" is
        /// accepted and ignored; the value must be greater than zero.
        /// </summary>
        public static SurveyValueParse ParseDistance(string text)
        {
            var s = (text ?? string.Empty).Trim();

            if (s.EndsWith("'", StringComparison.Ordinal))
                s = s.Substring(0, s.Length - 1).Trim();
            else if (s.EndsWith("FT", StringComparison.OrdinalIgnoreCase))
                s = s.Substring(0, s.Length - 2).Trim();

            double value;
            if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture,
                                 out value))
                return SurveyValueParse.Failure(
                    "Enter a distance in survey feet, such as 184.27.");

            if (value <= 0)
                return SurveyValueParse.Failure("A course distance must be greater than zero.");

            return SurveyValueParse.Success(value);
        }

        /// <summary>
        /// One angle body as degrees. A single number with a dot is packed D.MMSS
        /// ("45.2536" = 45°25'36"); otherwise "42 18" (deg min) or "42 18 36"
        /// (deg min sec), with °, º, d, ', ", or dashes accepted as the separators.
        /// Only the last component may carry decimals; minutes and seconds must be
        /// under 60.
        /// </summary>
        private static bool TryParseDms(string body, out double degrees, out string error)
        {
            degrees = 0.0;
            error = null;

            var cleaned = body
                .Replace("°", " ")     // °
                .Replace("º", " ")     // º
                .Replace("D", " ").Replace("d", " ")
                .Replace("’", " ")     // ’
                .Replace("”", " ")     // ”
                .Replace("'", " ")
                .Replace("\"", " ");

            // A dash separates components only BETWEEN digits (42-18-36). Anywhere
            // else it stays put, so "-5" still reads as negative and is refused
            // rather than silently becoming 5.
            var chars = cleaned.ToCharArray();
            for (var i = 1; i < chars.Length - 1; i++)
            {
                if (chars[i] == '-' && char.IsDigit(chars[i - 1]) &&
                    char.IsDigit(chars[i + 1]))
                    chars[i] = ' ';
            }
            cleaned = new string(chars);

            var parts = cleaned.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 1 || parts.Length > 3)
            {
                error = "Enter the angle as D.MMSS (45.2536 = 45d25'36\") or " +
                        "degrees minutes seconds (45 25 36).";
                return false;
            }

            // One number containing a dot is the packed Civil 3D entry form: the
            // digits after the dot are positional minutes then seconds, never a
            // decimal fraction of a degree.
            if (parts.Length == 1 && parts[0].IndexOf('.') >= 0)
                return TryParsePackedDms(parts[0], out degrees, out error);

            var values = new double[parts.Length];
            for (var i = 0; i < parts.Length; i++)
            {
                if (!double.TryParse(parts[i], NumberStyles.Float,
                                     CultureInfo.InvariantCulture, out values[i]) ||
                    values[i] < 0)
                {
                    error = "'" + parts[i] + "' is not a valid angle component.";
                    return false;
                }

                // 42.5 18 would silently mean two different things; refuse it.
                if (i < parts.Length - 1 && values[i] != Math.Floor(values[i]))
                {
                    error = "Only the last angle component may have decimals: " +
                            "42 18 36.5, not 42.5 18.";
                    return false;
                }
            }

            if (parts.Length >= 2 && values[1] >= 60.0)
            {
                error = "Minutes must be under 60.";
                return false;
            }
            if (parts.Length == 3 && values[2] >= 60.0)
            {
                error = "Seconds must be under 60.";
                return false;
            }

            degrees = values[0];
            if (parts.Length >= 2) degrees += values[1] / 60.0;
            if (parts.Length == 3) degrees += values[2] / 3600.0;
            return true;
        }

        /// <summary>
        /// Packed D.MMSS: "45.2536" is 45°25'36". After the dot the digits are
        /// positional -- two for minutes, two for seconds, anything further is a
        /// decimal fraction of the seconds ("45.253612" = 45°25'36.12"). Short
        /// entries pad with zeros on the right, so "45.5" is 45°50'00", exactly as
        /// Civil 3D reads it. Minutes or seconds of 60 and up are refused loudly:
        /// "42.75" is 75 minutes, not a decimal, and silently reinterpreting it
        /// would draw the wrong course.
        /// </summary>
        private static bool TryParsePackedDms(string token, out double degrees,
                                              out string error)
        {
            degrees = 0.0;
            error = null;

            var dot = token.IndexOf('.');
            var degPart = token.Substring(0, dot);
            var frac = token.Substring(dot + 1);

            int wholeDegrees;
            if (degPart.Length == 0 ||
                !int.TryParse(degPart, NumberStyles.None, CultureInfo.InvariantCulture,
                              out wholeDegrees))
            {
                error = "'" + token + "' is not a valid D.MMSS angle " +
                        "(45.2536 = 45d25'36\").";
                return false;
            }

            foreach (var c in frac)
            {
                if (c < '0' || c > '9')
                {
                    error = "'" + token + "' is not a valid D.MMSS angle " +
                            "(45.2536 = 45d25'36\").";
                    return false;
                }
            }

            var minutesDigits = (frac + "00").Substring(0, 2);
            var secondsDigits = frac.Length > 2
                ? (frac.Substring(2) + "00").Substring(0, 2)
                : "00";
            var secondsFraction = frac.Length > 4 ? frac.Substring(4) : string.Empty;

            var minutes = int.Parse(minutesDigits, CultureInfo.InvariantCulture);
            var seconds = double.Parse(
                secondsDigits + (secondsFraction.Length > 0 ? "." + secondsFraction : ""),
                NumberStyles.Float, CultureInfo.InvariantCulture);

            if (minutes >= 60)
            {
                error = "In D.MMSS entry '" + token + "' reads as " + minutes +
                        " minutes; minutes must be under 60. (The dot form is " +
                        "degrees.minutes-seconds, not a decimal.)";
                return false;
            }
            if (seconds >= 60.0)
            {
                error = "In D.MMSS entry '" + token + "' reads as " +
                        seconds.ToString("0.##", CultureInfo.InvariantCulture) +
                        " seconds; seconds must be under 60.";
                return false;
            }

            degrees = wholeDegrees + minutes / 60.0 + seconds / 3600.0;
            return true;
        }

        // ----------------------------------------------------------------- geometry

        /// <summary>Azimuth (degrees clockwise from north) of the vector from a
        /// course's start to its end. This is what annotation reports -- always the
        /// drawn geometry, never the typed entry.</summary>
        public static double AzimuthFromVector(double dx, double dy)
        {
            return Geometry.Angles.NormalizeDegrees(
                Geometry.Angles.ToDegrees(Math.Atan2(dx, dy)));
        }

        // --------------------------------------------------------------- formatting

        /// <summary>
        /// Formats an azimuth as a quadrant bearing: "N 42°18'36\" E".
        /// <paramref name="degreeSymbol"/> lets the CAD layer pass "%%d" (what DBText
        /// renders as °) while tests and the command line use "°" directly.
        /// Rounding carries properly: N 41°59'59.7" E at zero decimals is
        /// N 42°00'00" E, never N 41°59'60" E.
        /// </summary>
        public static string FormatBearing(double azimuthDegrees, int secondsDecimals,
                                           string degreeSymbol)
        {
            return FormatBearing(azimuthDegrees, secondsDecimals, degreeSymbol, true);
        }

        /// <summary>The same bearing, written with spaces (N 01°22'31" E) or without
        /// (N01°22'31"E, as exhibit drawings label it).</summary>
        public static string FormatBearing(double azimuthDegrees, int secondsDecimals,
                                           string degreeSymbol, bool spaced)
        {
            var azimuth = Geometry.Angles.NormalizeDegrees(azimuthDegrees);

            // Quadrant bearings with two-digit degrees. Due east and due west are both
            // written from north (N 90°00'00" E, N 90°00'00" W); due south is S 00°00'00" E.
            // The quadrant is chosen AFTER rounding so a direction that rounds onto an
            // axis still reads by that convention.
            var factor = Math.Pow(10.0, Math.Max(0, secondsDecimals));
            var roundedSeconds = Math.Round(azimuth * 3600.0 * factor, MidpointRounding.AwayFromZero) / factor;
            var rounded = roundedSeconds / 3600.0;
            if (rounded >= 360.0) rounded = 0.0;

            char ns, ew;
            double angle;
            if (rounded <= 90.0) { ns = 'N'; ew = 'E'; angle = rounded; }
            else if (rounded <= 180.0) { ns = 'S'; ew = 'E'; angle = 180.0 - rounded; }
            else if (rounded < 270.0) { ns = 'S'; ew = 'W'; angle = rounded - 180.0; }
            else { ns = 'N'; ew = 'W'; angle = 360.0 - rounded; }

            return string.Format(CultureInfo.InvariantCulture, spaced ? "{0} {1} {2}" : "{0}{1}{2}",
                ns, FormatDms(angle, secondsDecimals, degreeSymbol, true), ew);
        }

        /// <summary>Formats a direction as a whole-circle azimuth: "215°30'00\"".
        /// The same carry-safe rounding as the quadrant form.</summary>
        public static string FormatAzimuth(double azimuthDegrees, int secondsDecimals,
                                           string degreeSymbol)
        {
            var azimuth = Geometry.Angles.NormalizeDegrees(azimuthDegrees);
            var text = FormatDms(azimuth, secondsDecimals, degreeSymbol, false);

            // Rounding 359°59'59.7" carries all the way to 360°: report it as 0°.
            return text.StartsWith("360", StringComparison.Ordinal)
                ? FormatDms(0.0, secondsDecimals, degreeSymbol, false)
                : text;
        }

        /// <summary>An angle in degrees as DMS text: "42°18'36\"".</summary>
        private static string FormatDms(double angle, int secondsDecimals,
                                        string degreeSymbol, bool twoDigitDegrees)
        {
            if (secondsDecimals < 0) secondsDecimals = 0;

            // Round in total seconds first, then decompose, so a carry can never
            // produce 60 in the seconds or minutes slot.
            var factor = Math.Pow(10.0, secondsDecimals);
            var totalSeconds = Math.Round(angle * 3600.0 * factor,
                                          MidpointRounding.AwayFromZero) / factor;

            var deg = (int)(totalSeconds / 3600.0);
            var remainder = totalSeconds - deg * 3600.0;
            var min = (int)(remainder / 60.0);
            var sec = remainder - min * 60.0;

            // Floating point can leave sec at 59.999...; the string must not read 60.
            if (Math.Round(sec, secondsDecimals) >= 60.0) { sec = 0.0; min++; }
            if (min >= 60) { min -= 60; deg++; }

            var secondsFormat = secondsDecimals == 0
                ? "00"
                : "00." + new string('0', secondsDecimals);

            return string.Format(CultureInfo.InvariantCulture,
                "{0}{1}{2:00}'{3}\"",
                deg.ToString(twoDigitDegrees ? "00" : "0", CultureInfo.InvariantCulture), degreeSymbol, min,
                sec.ToString(secondsFormat, CultureInfo.InvariantCulture));
        }

        /// <summary>Formats a distance in survey feet: "184.27'" (or without the
        /// foot symbol when the standard says so).</summary>
        public static string FormatDistance(double distanceFeet, int decimals,
                                            bool footSymbol)
        {
            if (decimals < 0) decimals = 0;
            var text = distanceFeet.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture),
                                             CultureInfo.InvariantCulture);
            return footSymbol ? text + "'" : text;
        }
    }
}
