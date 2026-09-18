using System;
using System.Collections.Generic;
using System.Linq;

namespace FieldCodes.Settings
{
    /// <summary>How a production layer was settled.</summary>
    public enum LayerDecision
    {
        /// <summary>The configured layer is already in the drawing.</summary>
        Existing,
        /// <summary>A project/profile mapping names a layer that is in the drawing.</summary>
        Mapped,
        /// <summary>The drawing has the same name written with different case or
        /// separators (V_UTIL_STRM_E); that layer is used.</summary>
        SameNameDifferentSpelling,
        /// <summary>The drawing has similar layers; the drafter must choose.</summary>
        AskAboutSimilar,
        /// <summary>Nothing like it exists; the layer is created with this name.</summary>
        Create
    }

    public sealed class LayerResolution
    {
        public string Requested { get; set; }
        public string Layer { get; set; }
        public LayerDecision Decision { get; set; }
        public IList<string> Similar { get; set; }

        public LayerResolution() { Similar = new List<string>(); }
    }

    /// <summary>
    /// Settles which layer production drafting goes on BEFORE anything is created:
    /// the drawing's own layers and the project's mappings come first, so a
    /// drawing following another standard never gains a duplicate or near-duplicate
    /// layer just because the FTF default is spelled differently.
    /// </summary>
    public static class ProductionLayerResolver
    {
        private static readonly char[] Separators = { '-', '_', ' ', '.' };

        public static LayerResolution Resolve(string configured, IEnumerable<string> drawingLayers,
                                              IEnumerable<LayerMapping> mappings)
        {
            var layers = (drawingLayers ?? Enumerable.Empty<string>()).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
            var result = new LayerResolution { Requested = configured, Layer = configured };
            if (string.IsNullOrWhiteSpace(configured)) { result.Decision = LayerDecision.Create; return result; }

            var mapped = (mappings ?? Enumerable.Empty<LayerMapping>())
                .Where(m => m != null && !string.IsNullOrWhiteSpace(m.To) &&
                            string.Equals((m.From ?? string.Empty).Trim(), configured.Trim(), StringComparison.OrdinalIgnoreCase))
                .Select(m => m.To.Trim())
                .FirstOrDefault();

            // The project standard wins over the FTF default name.
            var target = mapped ?? configured.Trim();

            var exact = layers.FirstOrDefault(l => string.Equals(l, target, StringComparison.OrdinalIgnoreCase));
            if (exact != null)
            {
                result.Layer = exact;
                result.Decision = mapped != null ? LayerDecision.Mapped : LayerDecision.Existing;
                return result;
            }

            var key = Normalize(target);
            var spelled = layers.FirstOrDefault(l => Normalize(l) == key);
            if (spelled != null)
            {
                result.Layer = spelled;
                result.Decision = LayerDecision.SameNameDifferentSpelling;
                return result;
            }

            result.Layer = target;
            result.Similar = layers.Where(l => IsSimilar(target, l)).OrderBy(l => l, StringComparer.OrdinalIgnoreCase).ToList();
            result.Decision = result.Similar.Count > 0 ? LayerDecision.AskAboutSimilar : LayerDecision.Create;
            return result;
        }

        public static string Normalize(string name)
        {
            return new string((name ?? string.Empty).Where(c => Array.IndexOf(Separators, c) < 0).ToArray())
                .ToUpperInvariant();
        }

        /// <summary>Status parts a layer name may carry at its end (NCS status field and
        /// common spellings of existing / proposed).</summary>
        private static readonly HashSet<string> StatusParts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "E", "N", "D", "F", "T", "X", "M", "EX", "EXST", "EXIST", "EXISTING", "PR", "PROP", "PROPOSED", "NEW"
        };

        /// <summary>Near-duplicates: the same name with or without a status part at the end
        /// (V-UTIL-STRM vs V-UTIL-STRM-E). A different middle part -- V-ESMT-PATT-E next to
        /// V-ESMT-E -- is a different layer, and different words are never assumed to be
        /// synonyms; that is what a mapping is for.</summary>
        public static bool IsSimilar(string a, string b)
        {
            var ta = Tokens(a);
            var tb = Tokens(b);
            if (ta.Count == 0 || tb.Count == 0 || ta[0] != tb[0]) return false;
            var shorter = ta.Count <= tb.Count ? ta : tb;
            var longer = ta.Count <= tb.Count ? tb : ta;
            if (longer.Count - shorter.Count != 1 || shorter.Count < 3) return false;
            return StatusParts.Contains(longer[longer.Count - 1]) &&
                   longer.Take(shorter.Count).SequenceEqual(shorter);
        }

        private static List<string> Tokens(string name)
        {
            return (name ?? string.Empty).Split(Separators, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.ToUpperInvariant()).ToList();
        }
    }
}
