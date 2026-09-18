using System;
using System.Collections.Generic;
using System.Linq;

namespace FieldCodes.Linework
{
    /// <summary>What the survey points that created a line agree it is.</summary>
    public sealed class PointNoteConsensus
    {
        /// <summary>The feature(s) every note agrees on -- the intersection across
        /// notes, so a chained "BLD B EC B" shot narrows against a plain "EC B" one.
        /// Empty when the notes disagree or none carried a known line code.</summary>
        public IList<LineFeatureRule> Features { get; set; }

        /// <summary>Distinct codes seen when the notes could NOT agree. Reported,
        /// never chosen from.</summary>
        public IList<string> DisagreeingCodes { get; set; }

        /// <summary>The directional modifier when every note that has one agrees
        /// ("ASPH L"). Null when absent or conflicting.</summary>
        public LineLabelSide? Side { get; set; }
        public bool SideConflict { get; set; }

        /// <summary>How many notes carried a known line code.</summary>
        public int NoteCount { get; set; }

        /// <summary>Distinct raw notes that counted, for display.</summary>
        public IList<string> SampleNotes { get; set; }

        public PointNoteConsensus()
        {
            Features = new List<LineFeatureRule>();
            DisagreeingCodes = new List<string>();
            SampleNotes = new List<string>();
        }
    }

    /// <summary>
    /// Reads line identity out of the survey points that created the line. TBC
    /// draws linework THROUGH the shot points, so a vertex that coincides exactly
    /// with a CogoPoint was created by that point -- and the point's raw field note
    /// ("FOG B", "LNDY B", "RWC B", "ASPH L") still carries what the flattened
    /// layer erased: which stripe, which wall subtype, even LEFT/RIGHT intent.
    ///
    /// Discipline: notes that carry no known line code are ignored; the surviving
    /// notes must agree (set intersection) or nothing is claimed. This is exact
    /// evidence from coincident geometry, not proximity guessing -- near-misses are
    /// deliberately not matched by the caller.
    /// </summary>
    public static class PointNotes
    {
        public static PointNoteConsensus Consensus(IEnumerable<string> descriptions,
                                                   LineworkCatalog catalog)
        {
            if (catalog == null) throw new ArgumentNullException("catalog");

            var result = new PointNoteConsensus();
            List<LineFeatureRule> agreed = null;
            var seenCodes = new List<string>();
            var sides = new List<LineLabelSide>();

            foreach (var raw in descriptions ?? Enumerable.Empty<string>())
            {
                var features = catalog.FindInDescription(raw);
                if (features.Count == 0) continue;      // not a linework note

                result.NoteCount++;
                var note = raw.Trim();
                if (!result.SampleNotes.Contains(note) && result.SampleNotes.Count < 6)
                    result.SampleNotes.Add(note);

                foreach (var f in features)
                    if (!seenCodes.Contains(f.Code)) seenCodes.Add(f.Code);

                agreed = agreed == null
                    ? new List<LineFeatureRule>(features)
                    : agreed.Where(features.Contains).ToList();

                string ignored;
                LineLabelSide? side;
                LineSideParser.Split(raw, out ignored, out side);
                if (side.HasValue && !sides.Contains(side.Value)) sides.Add(side.Value);
            }

            if (agreed != null && agreed.Count > 0)
                result.Features = agreed;
            else if (seenCodes.Count > 0)
                result.DisagreeingCodes = seenCodes;

            if (sides.Count == 1) result.Side = sides[0];
            else if (sides.Count > 1) result.SideConflict = true;

            return result;
        }
    }
}
