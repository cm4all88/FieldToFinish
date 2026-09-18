using System;
using System.Collections.Generic;
using System.Linq;

namespace FieldCodes.Linework
{
    /// <summary>How a line label's layer was chosen.</summary>
    public enum LabelLayerSource
    {
        /// <summary>The feature rule names a label layer explicitly. Highest priority.</summary>
        ExplicitRule = 1,
        /// <summary>An office-standard text layer derived from the source layer's own
        /// name EXISTS in the drawing (V-CHAN-STRP-E -> V-CHAN-STRP-TEXT-E).</summary>
        DerivedText = 2,
        /// <summary>The source layer's family has exactly one text layer in the
        /// drawing, so there is nothing to choose between.</summary>
        FamilyText = 3,
        /// <summary>The configured default: no suitable text layer exists, or several
        /// were plausible and FTF refuses to guess between them.</summary>
        Default = 4
    }

    /// <summary>The outcome, always usable: Layer is never null.</summary>
    public sealed class LabelLayerResolution
    {
        public string Layer { get; set; }
        public LabelLayerSource Source { get; set; }

        /// <summary>The plausible family text layers when ambiguity forced the
        /// default. Empty otherwise. Reported, never chosen from.</summary>
        public IList<string> FamilyCandidates { get; set; }

        public LabelLayerResolution() { FamilyCandidates = new List<string>(); }

        /// <summary>Human-readable form for the preview and Feature Review.</summary>
        public string Describe()
        {
            switch (Source)
            {
                case LabelLayerSource.ExplicitRule:
                    return Layer + " (feature rule)";
                case LabelLayerSource.DerivedText:
                    return Layer + " (matches source layer)";
                case LabelLayerSource.FamilyText:
                    return Layer + " (family text layer)";
                default:
                    return FamilyCandidates.Count > 0
                        ? Layer + " (default - ambiguous: " +
                          string.Join(", ", FamilyCandidates.ToArray()) + ")"
                        : Layer + " (default - no family text layer found)";
            }
        }
    }

    /// <summary>
    /// Resolves the layer a line label belongs on. The drawing's ACTUAL layer table
    /// is the authority -- the patterns below were read from the office's real
    /// standard (553-2750-051-SV-BASE, 177 layers), where existing linework
    /// V-&lt;GROUP&gt;-&lt;FEAT&gt;-E pairs with text V-&lt;GROUP&gt;-&lt;FEAT&gt;-TEXT-E, and less
    /// specific features fall back to the group's text layer (V-SURF-FENC-CHNL-E
    /// pairs with V-SURF-FENC-TEXT-E).
    ///
    /// Priority: the feature rule's explicit label layer; then a text layer derived
    /// from the source layer's own name, most specific first, ONLY when that exact
    /// layer already exists; then the family's single text layer when exactly one
    /// exists. Several plausible family layers, or none: the configured default,
    /// with the candidates reported so the ambiguity is visible. This resolver can
    /// only ever return a layer that exists or one that was explicitly configured --
    /// it is structurally incapable of inventing a layer name that gets created.
    /// </summary>
    public static class LabelLayerResolver
    {
        /// <summary>The existing-layer catalog: case-insensitive, as AutoCAD layer
        /// names are.</summary>
        public static HashSet<string> BuildCatalog(IEnumerable<string> layerNames)
        {
            return new HashSet<string>(
                (layerNames ?? Enumerable.Empty<string>())
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Select(n => n.Trim()),
                StringComparer.OrdinalIgnoreCase);
        }

        public static LabelLayerResolution Resolve(string sourceLayer,
                                                   IList<LineFeatureRule> candidates,
                                                   HashSet<string> existingLayers,
                                                   string configuredDefault)
        {
            // 1. Explicit configuration always wins.
            if (candidates != null)
            {
                foreach (var candidate in candidates)
                {
                    if (candidate != null && !string.IsNullOrWhiteSpace(candidate.LabelLayer))
                        return new LabelLayerResolution
                        {
                            Layer = candidate.LabelLayer.Trim(),
                            Source = LabelLayerSource.ExplicitRule
                        };
                }
            }

            existingLayers = existingLayers ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var stem = Stem(sourceLayer, out var hadESuffix);
            if (stem.Length >= 2)
            {
                // 2. Derive the office-standard text layer from the source layer's own
                // name, most specific first, preserving the source's -E form before
                // trying the other. Every step is an exact existence check.
                var suffixes = hadESuffix
                    ? new[] { "-TEXT-E", "-TEXT" }
                    : new[] { "-TEXT", "-TEXT-E" };

                for (var take = stem.Length; take >= 2; take--)
                {
                    var prefix = string.Join("-", stem.Take(take).ToArray());
                    foreach (var suffix in suffixes)
                    {
                        var derived = prefix + suffix;
                        if (existingLayers.Contains(derived))
                            return new LabelLayerResolution
                            {
                                Layer = derived,
                                Source = LabelLayerSource.DerivedText
                            };
                    }
                }

                // 3. The family's text layers: everything in the drawing under the
                // same discipline-group prefix carrying a TEXT token.
                var familyPrefix = stem[0] + "-" + stem[1] + "-";
                var family = existingLayers
                    .Where(n => n.StartsWith(familyPrefix, StringComparison.OrdinalIgnoreCase) &&
                                n.Split('-').Any(t =>
                                    string.Equals(t, "TEXT", StringComparison.OrdinalIgnoreCase)))
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (family.Count == 1)
                    return new LabelLayerResolution
                    {
                        Layer = family[0],
                        Source = LabelLayerSource.FamilyText
                    };

                if (family.Count > 1)
                    return new LabelLayerResolution
                    {
                        Layer = configuredDefault,
                        Source = LabelLayerSource.Default,
                        FamilyCandidates = family
                    };
            }

            // 4. Nothing suitable exists: the configured default, visibly.
            return new LabelLayerResolution
            {
                Layer = configuredDefault,
                Source = LabelLayerSource.Default
            };
        }

        /// <summary>Layer name tokens with a trailing existing-marker E removed.
        /// "V-CHAN-STRP-E" -> [V, CHAN, STRP], noting the E form.</summary>
        private static string[] Stem(string sourceLayer, out bool hadESuffix)
        {
            hadESuffix = false;
            if (string.IsNullOrWhiteSpace(sourceLayer)) return new string[0];

            var tokens = sourceLayer.Trim().Split('-')
                .Where(t => t.Length > 0)
                .ToArray();

            if (tokens.Length > 1 &&
                string.Equals(tokens[tokens.Length - 1], "E", StringComparison.OrdinalIgnoreCase))
            {
                hadESuffix = true;
                tokens = tokens.Take(tokens.Length - 1).ToArray();
            }

            return tokens;
        }
    }
}
