using System;
using System.Collections.Generic;
using System.Linq;

namespace FieldCodes.Linework
{
    /// <summary>
    /// One pair of marked vertices along a line, by distance along it.
    /// </summary>
    public struct MarkedSpan
    {
        public double Start;
        public double End;

        public double Middle { get { return (Start + End) / 2.0; } }
    }

    /// <summary>
    /// Reads span markers out of the survey points a line was drawn through: two
    /// fence shots noted GATE define a gate, and the label belongs centred between
    /// them. Token-exact matching ("FCK B GATE" carries GATE; "GATEPOST" does not)
    /// and strict pairing -- consecutive marked points along the line pair up, and
    /// an odd one out is reported, never guessed into a span.
    /// </summary>
    public static class SpanFinder
    {
        /// <summary>True when the note carries the marker as its own token.</summary>
        public static bool HasToken(string description, string token)
        {
            if (string.IsNullOrWhiteSpace(description) ||
                string.IsNullOrWhiteSpace(token)) return false;

            return description
                .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .Any(t => string.Equals(t, token.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Pairs marked positions along the line: first with second, third with
        /// fourth. Distances are deduplicated and sorted first, so vertex order
        /// and doubled shots cannot scramble the pairing.
        /// </summary>
        public static IList<MarkedSpan> Pair(IEnumerable<double> markedDistances,
                                             out bool unpairedLeftover)
        {
            var sorted = (markedDistances ?? Enumerable.Empty<double>())
                .Distinct()
                .OrderBy(d => d)
                .ToList();

            var spans = new List<MarkedSpan>();
            for (var i = 0; i + 1 < sorted.Count; i += 2)
                spans.Add(new MarkedSpan { Start = sorted[i], End = sorted[i + 1] });

            unpairedLeftover = sorted.Count % 2 == 1;
            return spans;
        }
    }
}
