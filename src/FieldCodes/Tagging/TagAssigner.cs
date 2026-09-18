using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace FieldCodes.Tagging
{
    /// <summary>A tag number bound to the point it came from.</summary>
    public sealed class TagAssignment
    {
        public string PointNumber { get; set; }
        public string Prefix { get; set; }
        public int Number { get; set; }

        /// <summary>What is drawn on the plan and printed in the table: T1, T12, S3.</summary>
        public string Text
        {
            get { return (Prefix ?? string.Empty) + Number.ToString(CultureInfo.InvariantCulture); }
        }

        /// <summary>False when this tag already existed and was carried over.</summary>
        public bool IsNew { get; set; }
    }

    /// <summary>
    /// Hands out tag numbers, and keeps them.
    ///
    /// A tag that has appeared on a submitted plan must never move to a different tree.
    /// Numbers already recorded in XData are reused as-is, and new ones continue past
    /// the highest number ever issued for that prefix. Deleting a tree therefore leaves
    /// a gap rather than shuffling every tag after it -- a gap is a question someone can
    /// answer, whereas a silent renumber invalidates every drawing already issued.
    ///
    /// No Autodesk types: the CAD layer supplies what it read from XData and draws what
    /// comes back.
    /// </summary>
    public sealed class TagAssigner
    {
        private static readonly Regex TagPattern =
            new Regex(@"^(?<prefix>[A-Za-z_-]*)(?<number>[0-9]+)$",
                      RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>First number issued for a prefix that has none yet.</summary>
        public int StartNumber { get; set; }

        public TagAssigner()
        {
            StartNumber = 1;
        }

        /// <summary>
        /// Assigns a tag to every point whose rule defines a tag prefix.
        /// </summary>
        /// <param name="points">
        /// Points in the order they should be numbered. Deterministic ordering is the
        /// caller's job -- the CAD layer sorts by point number.
        /// </param>
        /// <param name="existing">
        /// Point number to previously issued tag text, read from XData. Null or empty
        /// on a first run.
        /// </param>
        public IList<TagAssignment> Assign(IEnumerable<ParsedPoint> points,
                                           IDictionary<string, string> existing)
        {
            if (points == null) throw new ArgumentNullException("points");

            var previous = existing ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // Highest number ever issued per prefix, taken from every recorded tag --
            // including tags whose point has since been deleted, so a number is never
            // handed to a second tree.
            var highest = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var text in previous.Values)
            {
                string prefix;
                int number;
                if (!TrySplit(text, out prefix, out number)) continue;

                int current;
                if (!highest.TryGetValue(prefix, out current) || number > current)
                    highest[prefix] = number;
            }

            var results = new List<TagAssignment>();

            foreach (var point in points)
            {
                if (point == null) continue;
                if (string.IsNullOrEmpty(point.TagPrefix)) continue;
                if (point.Ignored || point.Unhandled || point.HasErrors) continue;

                var prefix = point.TagPrefix;

                string carried;
                if (previous.TryGetValue(point.PointNumber ?? string.Empty, out carried))
                {
                    string oldPrefix;
                    int oldNumber;
                    if (TrySplit(carried, out oldPrefix, out oldNumber) &&
                        string.Equals(oldPrefix, prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        results.Add(new TagAssignment
                        {
                            PointNumber = point.PointNumber,
                            Prefix = oldPrefix,
                            Number = oldNumber,
                            IsNew = false
                        });
                        continue;
                    }
                }

                int next;
                next = highest.TryGetValue(prefix, out next) ? next + 1 : StartNumber;
                highest[prefix] = next;

                results.Add(new TagAssignment
                {
                    PointNumber = point.PointNumber,
                    Prefix = prefix,
                    Number = next,
                    IsNew = true
                });
            }

            return results;
        }

        /// <summary>
        /// Splits "T12" into "T" and 12. False for anything else. Public so the CAD
        /// layer can rebuild assignments from the tags already in the drawing rather
        /// than recomputing them and risking disagreement with the plan.
        /// </summary>
        public static bool TrySplit(string tagText, out string prefix, out int number)
        {
            prefix = string.Empty;
            number = 0;

            if (string.IsNullOrWhiteSpace(tagText)) return false;

            var match = TagPattern.Match(tagText.Trim());
            if (!match.Success) return false;

            prefix = match.Groups["prefix"].Value;
            return int.TryParse(match.Groups["number"].Value, NumberStyles.Integer,
                                CultureInfo.InvariantCulture, out number);
        }
    }
}
