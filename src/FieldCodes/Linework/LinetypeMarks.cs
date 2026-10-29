using System;
using System.Collections.Generic;
using System.Linq;

namespace FieldCodes.Linework
{
    /// <summary>
    /// A linetype that draws its own letter or symbol along the line -- water
    /// ---W---W---, power ---P---P---, a fence's x's -- already says what the line is,
    /// and the legend explains it. Such a line gets no separate text label.
    ///
    /// The marks are read from the linetype's own definition (its text and shape
    /// elements), never guessed from its name.
    /// </summary>
    public static class LinetypeMarks
    {
        /// <summary>What a shape element is called when the linetype draws a symbol, not a letter.</summary>
        public const string Symbol = "symbol";

        public static bool IsMarked(IEnumerable<string> marks)
        {
            return marks != null && marks.Any();
        }

        /// <summary>"WATER (W)", "FENCE2 (symbol)", "UTIL (G/W)": the linetype and what it draws.</summary>
        public static string Describe(string linetypeName, IEnumerable<string> marks)
        {
            var distinct = new List<string>();
            foreach (var m in marks ?? Enumerable.Empty<string>())
            {
                var mark = string.IsNullOrWhiteSpace(m) ? Symbol : m.Trim();
                if (!distinct.Contains(mark, StringComparer.OrdinalIgnoreCase)) distinct.Add(mark);
            }
            var name = string.IsNullOrWhiteSpace(linetypeName) ? "(unnamed linetype)" : linetypeName.Trim();
            return distinct.Count == 0 ? name : name + " (" + string.Join("/", distinct.ToArray()) + ")";
        }
    }
}
