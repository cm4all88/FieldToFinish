using System;
using System.Collections.Generic;
using System.Linq;

namespace FieldCodes.Utilities
{
    /// <summary>
    /// How a pipe material is written down, wherever it comes from -- a field note, the entry
    /// panel, or a drafter typing one that is not on the list.
    ///
    /// Capitals, because that is how the schedule and the labels read. And the office's own
    /// spelling where a material has more than one: ductile iron gets written DI as often as
    /// DIP, and two spellings of one material means two rows in a table that should have one.
    /// </summary>
    public static class PipeMaterials
    {
        /// <summary>The material as the office writes it, or null when nothing was given.</summary>
        public static string Normalize(string text)
        {
            var material = (text ?? string.Empty).Trim().ToUpperInvariant();
            if (material.Length == 0) return null;

            // Ductile iron: written DIP on our drawings, however the crew wrote it.
            if (material == "DI" || material == "D.I." || material == "D.I") return "DIP";

            return material;
        }

        /// <summary>
        /// The configured materials plus the spellings a crew might write for them, so a note
        /// saying DI is still read as a material even though the office writes DIP.
        /// </summary>
        public static IEnumerable<string> WithSpellings(IEnumerable<string> configured)
        {
            var all = new List<string>();
            foreach (var material in configured ?? Enumerable.Empty<string>())
            {
                var text = (material ?? string.Empty).Trim();
                if (text.Length > 0) all.Add(text);
            }
            foreach (var spelling in new[] { "DI", "D.I.", "D.I" })
                if (!all.Contains(spelling, StringComparer.OrdinalIgnoreCase)) all.Add(spelling);
            return all;
        }
    }
}
