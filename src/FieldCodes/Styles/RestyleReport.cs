using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FieldCodes.Styles
{
    /// <summary>
    /// What FTFRESTYLE found and would change (or changed): per kind of annotation, how many
    /// FTF entities there are, how many differ from the current styles and in what way, and
    /// how many keep their wording because someone edited it. Built the same way for the
    /// preview and the run, so the preview is exactly what Apply will do.
    /// </summary>
    public sealed class RestyleReport
    {
        private sealed class Tally
        {
            public int Seen;
            public int Changing;
            public int WordingKept;
            public readonly SortedDictionary<string, int> Changes = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }

        private readonly SortedDictionary<StyledKind, Tally> _kinds = new SortedDictionary<StyledKind, Tally>();
        public int UserCopiesLeftAlone { get; set; }
        public int NoPoint { get; set; }

        public int Seen { get { return _kinds.Values.Sum(t => t.Seen); } }
        public int Changing { get { return _kinds.Values.Sum(t => t.Changing); } }

        /// <summary>Records one entity and its differences ("text height 0.08 to 0.10").</summary>
        public void Saw(StyledKind kind, ICollection<string> changes, bool wordingKept)
        {
            Tally t;
            if (!_kinds.TryGetValue(kind, out t)) { t = new Tally(); _kinds[kind] = t; }
            t.Seen++;
            if (wordingKept) t.WordingKept++;
            if (changes == null || changes.Count == 0) return;
            t.Changing++;
            foreach (var c in changes.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                int n;
                t.Changes[c] = t.Changes.TryGetValue(c, out n) ? n + 1 : 1;
            }
        }

        public IList<string> Lines(bool applied)
        {
            var lines = new List<string>();
            if (_kinds.Count == 0)
            {
                lines.Add("No FTF labels, tags or line labels in this drawing.");
                return lines;
            }
            foreach (var pair in _kinds)
            {
                var t = pair.Value;
                lines.Add(string.Format(CultureInfo.InvariantCulture, "{0}: {1} FTF {2}, {3} {4}.",
                    Plural(pair.Key), t.Seen, t.Seen == 1 ? "entity" : "entities", t.Changing,
                    applied ? "updated" : (t.Changing == 1 ? "would change" : "would change")));
                foreach (var c in t.Changes.OrderByDescending(c => c.Value).ThenBy(c => c.Key, StringComparer.OrdinalIgnoreCase))
                    lines.Add(string.Format(CultureInfo.InvariantCulture, "    {0} x{1}", c.Key, c.Value));
                if (t.WordingKept > 0)
                    lines.Add(string.Format(CultureInfo.InvariantCulture,
                        "    {0} kept their wording: it was edited by hand (only the look is updated).", t.WordingKept));
            }
            if (UserCopiesLeftAlone > 0)
                lines.Add(string.Format(CultureInfo.InvariantCulture,
                    "{0} copies someone made of FTF annotation are theirs and were left alone.", UserCopiesLeftAlone));
            if (NoPoint > 0)
                lines.Add(string.Format(CultureInfo.InvariantCulture,
                    "{0} label(s) whose point is no longer in the drawing were left alone (FTFLABELS clears them).", NoPoint));
            return lines;
        }

        public static string Plural(StyledKind kind)
        {
            switch (kind)
            {
                case StyledKind.Tag: return "Tags";
                case StyledKind.LineLabel: return "Line labels";
                default: return "Point labels";
            }
        }

        /// <summary>"text height 0.08 to 0.10" -- or null when nothing differs.</summary>
        public static string Change(string what, string before, string after)
        {
            var b = string.IsNullOrWhiteSpace(before) ? "(none)" : before.Trim();
            var a = string.IsNullOrWhiteSpace(after) ? "(none)" : after.Trim();
            return string.Equals(b, a, StringComparison.OrdinalIgnoreCase) ? null : what + " " + b + " to " + a;
        }

        public static string Change(string what, double before, double after)
        {
            if (Math.Abs(before - after) <= Math.Max(1e-6, Math.Abs(after) * 1e-4)) return null;
            return what + " " + before.ToString("0.###", CultureInfo.InvariantCulture) + " to " +
                   after.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }
}
