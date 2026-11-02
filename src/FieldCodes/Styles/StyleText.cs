using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace FieldCodes.Styles
{
    /// <summary>
    /// Turns a label's text into what a text style asks for: numbers rounded, upper case,
    /// prefix and suffix, and the width factor and oblique angle as MTEXT codes (MTEXT has
    /// no width-factor property of its own; the code is what AutoCAD's own editor writes).
    /// </summary>
    public static class StyleText
    {
        public const string LineBreak = "\\P";

        // A number written with a decimal point, not part of a word or a longer dotted run.
        private static readonly Regex DecimalNumber = new Regex(
            @"(?<![\w.])(?<int>\d+)\.(?<frac>\d+)(?![\w.]|\.\d)", RegexOptions.CultureInvariant);

        /// <summary>The MTEXT contents for a label.</summary>
        public static string Compose(string labelText, ResolvedTextStyle style)
        {
            var text = labelText ?? string.Empty;
            if (style == null) return text;

            if (style.Decimals.HasValue) text = RoundNumbers(text, style.Decimals.Value);
            if (style.UpperCase) text = UpperCase(text);
            text = (style.Prefix ?? string.Empty) + text + (style.Suffix ?? string.Empty);

            var codes = new StringBuilder();
            if (Math.Abs(style.WidthFactor - 1.0) > 1e-6 && style.WidthFactor > 0)
                codes.Append("\\W").Append(style.WidthFactor.ToString("0.###", CultureInfo.InvariantCulture)).Append(';');
            if (Math.Abs(style.ObliqueDegrees) > 1e-6)
                codes.Append("\\Q").Append(style.ObliqueDegrees.ToString("0.###", CultureInfo.InvariantCulture)).Append(';');
            if (codes.Length == 0) return text;

            // Width and oblique codes hold until the next paragraph, so each line carries them.
            var lines = text.Split(new[] { LineBreak }, StringSplitOptions.None);
            return "{" + codes + string.Join(LineBreak + codes, lines) + "}";
        }

        /// <summary>
        /// Rounds every number written with a decimal point to the given places, padding with
        /// zeros (6.4 to 6.40 at two places). Whole numbers -- pole numbers, sign numbers, tag
        /// numbers -- are left exactly as written.
        /// </summary>
        public static string RoundNumbers(string text, int decimals)
        {
            if (string.IsNullOrEmpty(text)) return text;
            var places = Math.Max(0, Math.Min(8, decimals));
            var lines = text.Split(new[] { LineBreak }, StringSplitOptions.None);
            for (var i = 0; i < lines.Length; i++)
            {
                lines[i] = DecimalNumber.Replace(lines[i], m =>
                {
                    decimal value;
                    if (!decimal.TryParse(m.Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value))
                        return m.Value;
                    return Math.Round(value, places, MidpointRounding.AwayFromZero)
                               .ToString("F" + places.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
                });
            }
            return string.Join(LineBreak, lines);
        }

        // MTEXT codes whose argument runs to a semicolon: font, height, width, oblique,
        // tracking, alignment, colour, paragraph, stacking.
        private const string CodesWithArgument = "fFHWQTAcCpS";

        /// <summary>Upper case, leaving MTEXT formatting codes as they are.</summary>
        public static string UpperCase(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            var sb = new StringBuilder(text.Length);
            var i = 0;
            while (i < text.Length)
            {
                var c = text[i];
                if (c == '\\' && i + 1 < text.Length)
                {
                    var code = text[i + 1];
                    sb.Append(c).Append(code);
                    i += 2;
                    if (CodesWithArgument.IndexOf(code) >= 0)
                    {
                        var end = text.IndexOf(';', i);
                        if (end < 0) end = text.Length - 1;
                        sb.Append(text, i, end - i + 1);
                        i = end + 1;
                    }
                    continue;
                }
                sb.Append(char.ToUpperInvariant(c));
                i++;
            }
            return sb.ToString();
        }

        /// <summary>
        /// The angle label text is turned to, in degrees counter-clockwise from east, never
        /// upside down: a symbol at 200 degrees gets text at 20.
        /// </summary>
        public static double AngleDegrees(ResolvedTextStyle style, double? symbolDegrees)
        {
            if (style == null) return 0;
            double angle;
            switch (style.Rotation)
            {
                case TextRotation.Fixed: angle = style.RotationDegrees; break;
                case TextRotation.FollowSymbol:
                    if (!symbolDegrees.HasValue) return 0;
                    angle = symbolDegrees.Value;
                    break;
                default: return 0;
            }
            return Readable(angle);
        }

        /// <summary>An angle folded into (-90, 90] so text reads left to right.</summary>
        public static double Readable(double degrees)
        {
            var a = degrees % 360.0;
            if (a < 0) a += 360.0;
            if (a > 90.0 && a <= 270.0) a -= 180.0;
            else if (a > 270.0) a -= 360.0;
            if (Math.Abs(a + 90.0) < 1e-9) a = 90.0;
            return a;
        }

        /// <summary>What the label will read, MTEXT codes removed: for previews and reports.</summary>
        public static string Plain(string contents)
        {
            if (string.IsNullOrEmpty(contents)) return contents ?? string.Empty;
            var sb = new StringBuilder(contents.Length);
            var i = 0;
            while (i < contents.Length)
            {
                var c = contents[i];
                if (c == '{' || c == '}') { i++; continue; }
                if (c == '\\' && i + 1 < contents.Length)
                {
                    var code = contents[i + 1];
                    i += 2;
                    if (code == 'P') { sb.Append('\n'); continue; }
                    if (code == '\\' || code == '{' || code == '}') { sb.Append(code); continue; }
                    if (code == '~') { sb.Append(' '); continue; }
                    if (CodesWithArgument.IndexOf(code) >= 0)
                    {
                        var end = contents.IndexOf(';', i);
                        i = end < 0 ? contents.Length : end + 1;
                    }
                    continue;
                }
                sb.Append(c);
                i++;
            }
            return sb.ToString();
        }

        /// <summary>The lines of a label's plain text.</summary>
        public static IList<string> PlainLines(string contents)
        {
            return Plain(contents).Split('\n');
        }
    }
}
