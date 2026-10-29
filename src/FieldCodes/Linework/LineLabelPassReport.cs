using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FieldCodes.Linework
{
    /// <summary>
    /// What the whole-drawing line label pass did, and -- the useful part -- what it did
    /// not label and why, grouped so a drawing with 40 unlabelled fence lines says so in
    /// one line instead of a count: "FNC Fence: no label configured (40 lines)".
    /// </summary>
    public sealed class LineLabelPassReport
    {
        private readonly Dictionary<string, int> _notConfigured = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _ambiguous = new Dictionary<string, int>(StringComparer.Ordinal);

        public int LabelsPlaced { get; set; }
        public int FeaturesLabelled { get; set; }
        public int GateLabels { get; set; }
        public int TooShort { get; set; }

        /// <summary>Lines that already carry a label placed by hand (FTFLABELLINE): left alone,
        /// so a re-run never puts a second label beside the surveyor's.</summary>
        public int AlreadyByHand { get; set; }

        /// <summary>Lines identified but left unlabelled, all reasons together.</summary>
        public int Unlabelled
        {
            get { return _notConfigured.Values.Sum() + _ambiguous.Values.Sum(); }
        }

        /// <summary>
        /// A line FTF identified but could not label: either a candidate has no wording
        /// configured, or the candidates (sharing a layer) are labelled differently.
        /// </summary>
        public void NotLabelled(IList<LineFeatureRule> candidates, string layer)
        {
            if (candidates == null || candidates.Count == 0) return;
            if (LineworkCatalog.DisagreeingChoices(candidates).Count > 0)
            {
                var key = (layer ?? "?") + ": could be " + string.Join(" or ", candidates
                    .Select(c => c.Code + " (" + c.Label.Trim() + ")").ToArray()) +
                    " -- label these with FTFLABELLINE, which asks which it is";
                Count(_ambiguous, key);
                return;
            }
            foreach (var c in candidates.Where(c => c != null && (!c.Enabled || string.IsNullOrWhiteSpace(c.Label))))
            {
                var name = string.IsNullOrWhiteSpace(c.Name) ? string.Empty : " " + c.Name;
                Count(_notConfigured, c.Code + name + ": " + (c.Enabled ? "no label configured" : "turned off"));
            }
        }

        private static void Count(Dictionary<string, int> into, string key)
        {
            int n;
            into[key] = into.TryGetValue(key, out n) ? n + 1 : 1;
        }

        /// <summary>The report, a line per group, most frequent first.</summary>
        public IList<string> Lines(double minLengthFeet)
        {
            var lines = new List<string>
            {
                string.Format(CultureInfo.InvariantCulture, "{0} label(s) on {1} line(s){2}.",
                    LabelsPlaced, FeaturesLabelled,
                    GateLabels > 0 ? ", " + GateLabels.ToString(CultureInfo.InvariantCulture) + " gate/span label(s)" : string.Empty)
            };
            if (AlreadyByHand > 0)
                lines.Add(string.Format(CultureInfo.InvariantCulture, "{0} line(s) already labelled by hand (FTFLABELLINE) were left as they are.", AlreadyByHand));
            if (TooShort > 0)
                lines.Add(string.Format(CultureInfo.InvariantCulture, "{0} line(s) shorter than {1:0.#} ft left unlabelled (Settings > Line Labels).", TooShort, minLengthFeet));
            foreach (var kv in _notConfigured.OrderByDescending(k => k.Value).ThenBy(k => k.Key, StringComparer.Ordinal))
                lines.Add(string.Format(CultureInfo.InvariantCulture, "Not labelled: {0} ({1} line(s)).", kv.Key, kv.Value));
            foreach (var kv in _ambiguous.OrderByDescending(k => k.Value).ThenBy(k => k.Key, StringComparer.Ordinal))
                lines.Add(string.Format(CultureInfo.InvariantCulture, "Not labelled: {0} ({1} line(s)).", kv.Key, kv.Value));
            return lines;
        }
    }
}
