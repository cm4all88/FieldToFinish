using System;
using System.Collections.Generic;
using System.Linq;

namespace FieldCodes.Linework
{
    /// <summary>
    /// FTFLABELDRIVEWAY's wording: the first edge's surface type, read from its layer, with
    /// DWY after it -- an edge on V-SURF-ASPH-E labels "ASPH DWY", on V-SURF-CONC-E "CONC DWY".
    /// The format comes from Settings > Line Labels ("{type} DWY" by default).
    /// </summary>
    public static class DrivewayWording
    {
        public const string DefaultFormat = "{type} DWY";

        /// <summary>
        /// The type field of a layer name: the last field once a one-letter status field (the
        /// -E of existing, -N of new, -D of demolish) is set aside. V-SURF-ASPH-E -> ASPH,
        /// V-SURF-GRVL-E -> GRVL, ASPHALT -> ASPHALT. Null when there is no field with a letter in it.
        /// </summary>
        public static string LayerType(string layer)
        {
            if (string.IsNullOrWhiteSpace(layer)) return null;
            var fields = layer.Trim().Split(new[] { '-', '_', ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            if (fields.Count > 1 && fields[fields.Count - 1].Length == 1) fields.RemoveAt(fields.Count - 1);
            if (fields.Count == 0) return null;
            var type = fields[fields.Count - 1].ToUpperInvariant();
            return type.Any(char.IsLetter) ? type : null;
        }

        /// <summary>
        /// The wordings to offer, the layer's type first. The others are the first edge
        /// feature's between-wordings that spell the same type out in one word (ASPHALT for
        /// ASPH), so W switches between "ASPH DWY" and "ASPHALT DWY". Empty when the layer
        /// has no readable type.
        /// </summary>
        public static IList<string> Choices(string layer, IList<LineFeatureRule> candidates, string format)
        {
            var choices = new List<string>();
            var type = LayerType(layer);
            if (type == null) return choices;
            if (string.IsNullOrWhiteSpace(format)) format = DefaultFormat;

            Add(choices, Apply(format, type));
            foreach (var wording in LineworkCatalog.BetweenChoices(candidates))
            {
                var word = wording.Trim().ToUpperInvariant();
                if (word.IndexOf(' ') >= 0 || word.Length <= type.Length) continue;
                if (!word.StartsWith(type, StringComparison.Ordinal)) continue;
                Add(choices, Apply(format, word));
            }
            return choices;
        }

        public static string Apply(string format, string type)
        {
            return (format ?? DefaultFormat).Replace("{type}", type ?? string.Empty).Trim();
        }

        private static void Add(List<string> choices, string wording)
        {
            if (wording.Length == 0) return;
            if (!choices.Any(c => string.Equals(c, wording, StringComparison.OrdinalIgnoreCase))) choices.Add(wording);
        }
    }
}
