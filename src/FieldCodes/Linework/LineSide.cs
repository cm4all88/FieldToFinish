using System;
using System.Collections.Generic;

namespace FieldCodes.Linework
{
    /// <summary>Which side of the existing line a label sits on.</summary>
    public enum LineLabelSide
    {
        /// <summary>No opinion at this level -- fall through to the next one.</summary>
        Auto = 0,
        /// <summary>Centred on the line itself.</summary>
        OnLine = 1,
        /// <summary>Left of the line, relative to the line's own direction.</summary>
        Left = 2,
        /// <summary>Right of the line, relative to the line's own direction.</summary>
        Right = 3
    }

    /// <summary>
    /// Recognises directional modifiers in source line coding.
    ///
    /// LEFT/L and RIGHT/R are equivalent, and they are only ever read from the
    /// modifier position -- the tokens AFTER the code token. The code token itself is
    /// never interpreted as a direction, so a figure that happens to be named "L"
    /// stays a code, and an L or R buried inside a word ("RL", "WALL") means nothing.
    ///
    /// Left and right are relative to the line's own direction (its parameterisation),
    /// never to screen orientation.
    /// </summary>
    public static class LineSideParser
    {
        /// <summary>LEFT/L -> Left, RIGHT/R -> Right; null for anything else.</summary>
        public static LineLabelSide? TryParseToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return null;

            switch (token.Trim().ToUpperInvariant())
            {
                case "LEFT":
                case "L":
                    return LineLabelSide.Left;
                case "RIGHT":
                case "R":
                    return LineLabelSide.Right;
                default:
                    return null;
            }
        }

        /// <summary>
        /// Splits source coding such as "ASPH LEFT" into the code part and the
        /// directional modifier. The first token is always the code; the first
        /// directional token after it wins. Returns the coding unchanged when no
        /// modifier is present.
        /// </summary>
        public static void Split(string sourceCoding, out string codePart,
                                 out LineLabelSide? side)
        {
            codePart = sourceCoding;
            side = null;

            if (string.IsNullOrWhiteSpace(sourceCoding)) return;

            var tokens = sourceCoding.Trim().Split(
                new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) return;

            codePart = tokens[0];

            for (var i = 1; i < tokens.Length; i++)
            {
                var parsed = TryParseToken(tokens[i]);
                if (parsed.HasValue)
                {
                    side = parsed;
                    return;
                }
            }
        }

        /// <summary>
        /// The side a cursor position chooses during interactive placement.
        /// (dx, dy) is the vector from the nearest point on the line to the cursor;
        /// directionRadians is the line's tangent there. Within the on-line band the
        /// label sits on the line itself; outside it, the sign of the cross product
        /// tangent x toCursor says which line-relative side the cursor is on -- so
        /// the label always lands on the side the user is pointing at, regardless of
        /// which way the line happens to run or how the text is flipped to read.
        /// </summary>
        public static LineLabelSide FromCursor(double directionRadians, double dx,
                                               double dy, double onLineBand)
        {
            var distance = Math.Sqrt(dx * dx + dy * dy);
            if (distance <= onLineBand) return LineLabelSide.OnLine;

            var cross = Math.Cos(directionRadians) * dy - Math.Sin(directionRadians) * dx;
            return cross >= 0 ? LineLabelSide.Left : LineLabelSide.Right;
        }

        /// <summary>Configured placement string to a side. Unknown text is Auto, so a
        /// typo falls through to the default rather than inventing a side.</summary>
        public static LineLabelSide ParsePlacement(string placement)
        {
            if (string.IsNullOrWhiteSpace(placement)) return LineLabelSide.Auto;

            switch (placement.Trim().Replace(" ", "").ToUpperInvariant())
            {
                case "LEFT": return LineLabelSide.Left;
                case "RIGHT": return LineLabelSide.Right;
                case "ONLINE": return LineLabelSide.OnLine;
                default: return LineLabelSide.Auto;
            }
        }

        /// <summary>
        /// Resolves the side one feature's labels actually use, in precedence order:
        /// the source coding's own modifier, then the feature rule's configured
        /// placement, then the settings default. Auto at any level falls through;
        /// nothing here ever guesses a side the coding did not specify.
        /// </summary>
        public static LineLabelSide Resolve(LineLabelSide? sourceSide,
                                            IList<LineFeatureRule> candidates,
                                            LineLabelSide configuredDefault)
        {
            if (sourceSide.HasValue && sourceSide.Value != LineLabelSide.Auto)
                return sourceSide.Value;

            if (candidates != null)
            {
                foreach (var candidate in candidates)
                {
                    var placement = ParsePlacement(candidate.Placement);
                    if (placement != LineLabelSide.Auto) return placement;
                }
            }

            return configuredDefault == LineLabelSide.Auto
                ? LineLabelSide.OnLine
                : configuredDefault;
        }

        public static string Describe(LineLabelSide side, double offsetFeet)
        {
            switch (side)
            {
                case LineLabelSide.Left:
                    return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "left side, {0:0.#} ft offset", offsetFeet);
                case LineLabelSide.Right:
                    return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "right side, {0:0.#} ft offset", offsetFeet);
                default:
                    return "on line";
            }
        }
    }
}
