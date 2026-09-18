using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace FieldCodes
{
    /// <summary>What a layer *is*, independent of its review state.</summary>
    public enum LayerRole
    {
        /// <summary>Not named by any rule. Treated conservatively downstream.</summary>
        Unclassified = 0,
        /// <summary>Linework a label may sit on and mask freely -- drip circles, contours.</summary>
        MaskableLinework = 1,
        /// <summary>A symbol that must stay legible: protected existing features, and our own blocks.</summary>
        Symbol = 2,
        /// <summary>A label layer produced by one of the code rules.</summary>
        Label = 3
    }

    /// <summary>Draw-order bands, bottom to top.</summary>
    public enum DrawOrderBand
    {
        MaskableLinework = 0,
        /// <summary>Wipeouts we create. Identified by XData ownership, not by layer.</summary>
        Mask = 1,
        Symbol = 2,
        Label = 3
    }

    /// <summary>How label placement must treat an obstacle.</summary>
    public enum ObstacleClass
    {
        /// <summary>Overlap and mask freely.</summary>
        Free = 0,
        /// <summary>Avoid; mask only when no candidate position is left.</summary>
        Soft = 1,
        /// <summary>Never overlap.</summary>
        Hard = 2
    }

    /// <summary>
    /// Decides what band and obstacle class a layer belongs to.
    ///
    /// Modifiers append a layerSuffix to every layer they touch (-DEAD, -RMV, -PROT),
    /// so a protected sign lands on V-SIGN-DEAD and a drip circle on V-TREE-DRIP-DEAD.
    /// Those suffixes are review annotations, not a change in what the entity is, so
    /// they are stripped before matching. Without that, a DEAD sign silently stops
    /// being protected and every modified tree falls into no class at all.
    ///
    /// Label and symbol layers are derived from the code rules rather than listed
    /// separately, so adding a code rule cannot leave its layers unclassified.
    /// </summary>
    public sealed class LayerClassifier
    {
        private static readonly Regex Placeholder =
            new Regex(@"\{[^}]*\}", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private readonly string[] _suffixes;
        private readonly Regex[] _protectedPatterns;
        private readonly Regex[] _maskablePatterns;
        private readonly Regex[] _labelPatterns;
        private readonly Regex[] _symbolPatterns;

        public LayerClassifier(RulesConfig cfg)
        {
            if (cfg == null) throw new ArgumentNullException("cfg");

            // Longest first, so -DEAD-RMV strips predictably regardless of listing order.
            _suffixes = cfg.Modifiers
                .Where(m => !string.IsNullOrEmpty(m.LayerSuffix))
                .Select(m => m.LayerSuffix)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(s => s.Length)
                .ToArray();

            _protectedPatterns = Compile(cfg.ProtectedLayers);
            _maskablePatterns = Compile(cfg.MaskableLayers);

            _labelPatterns = Compile(cfg.Codes
                .Where(c => c.Label != null && !string.IsNullOrWhiteSpace(c.Label.Layer))
                .Select(c => c.Label.Layer));

            _symbolPatterns = Compile(cfg.Codes
                .Where(c => !string.IsNullOrWhiteSpace(c.BlockLayer))
                .Select(c => c.BlockLayer));
        }

        /// <summary>Layer suffixes contributed by modifiers, longest first.</summary>
        public IEnumerable<string> ModifierSuffixes { get { return _suffixes; } }

        /// <summary>
        /// Removes trailing modifier suffixes. V-TREE-DRIP-DEAD-RMV becomes V-TREE-DRIP.
        /// Never strips a layer down to nothing.
        /// </summary>
        public string StripModifierSuffixes(string layer)
        {
            if (string.IsNullOrEmpty(layer)) return layer;

            var current = layer;
            var changed = true;
            while (changed)
            {
                changed = false;
                foreach (var s in _suffixes)
                {
                    if (current.Length > s.Length &&
                        current.EndsWith(s, StringComparison.OrdinalIgnoreCase))
                    {
                        current = current.Substring(0, current.Length - s.Length);
                        changed = true;
                        break;
                    }
                }
            }
            return current;
        }

        /// <summary>
        /// Classifies a layer name. Precedence is Label, then Symbol, then maskable
        /// linework -- so a layer named by two lists resolves the same way every run.
        /// </summary>
        public LayerRole Classify(string layer)
        {
            if (string.IsNullOrWhiteSpace(layer)) return LayerRole.Unclassified;

            var b = StripModifierSuffixes(layer.Trim());

            if (Matches(_labelPatterns, b)) return LayerRole.Label;
            if (Matches(_symbolPatterns, b)) return LayerRole.Symbol;
            if (Matches(_protectedPatterns, b)) return LayerRole.Symbol;
            if (Matches(_maskablePatterns, b)) return LayerRole.MaskableLinework;

            return LayerRole.Unclassified;
        }

        /// <summary>
        /// Band for a role. Masks are not layer-derived -- the CAD layer assigns
        /// DrawOrderBand.Mask to the wipeouts it owns.
        /// </summary>
        public static DrawOrderBand BandOf(LayerRole role)
        {
            switch (role)
            {
                case LayerRole.Label: return DrawOrderBand.Label;
                case LayerRole.Symbol: return DrawOrderBand.Symbol;
                case LayerRole.MaskableLinework: return DrawOrderBand.MaskableLinework;
                default: return DrawOrderBand.Symbol;   // unknown: keep it above masks
            }
        }

        /// <summary>
        /// Obstacle class for a role. Unclassified is Soft rather than Free: masking
        /// something we failed to recognise is worse than nudging a label.
        /// </summary>
        public static ObstacleClass ObstacleOf(LayerRole role)
        {
            switch (role)
            {
                case LayerRole.Label: return ObstacleClass.Hard;
                case LayerRole.Symbol: return ObstacleClass.Hard;
                case LayerRole.MaskableLinework: return ObstacleClass.Free;
                default: return ObstacleClass.Soft;
            }
        }

        public ObstacleClass ObstacleForLayer(string layer)
        {
            return ObstacleOf(Classify(layer));
        }

        // ------------------------------------------------------------------ helpers

        private static bool Matches(Regex[] patterns, string value)
        {
            for (var i = 0; i < patterns.Length; i++)
                if (patterns[i].IsMatch(value)) return true;
            return false;
        }

        private static Regex[] Compile(IEnumerable<string> patterns)
        {
            if (patterns == null) return new Regex[0];
            return patterns
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => Glob(p.Trim()))
                .ToArray();
        }

        /// <summary>
        /// Layer patterns use shell globs (V-UTIL-*). A {placeholder} left in a layer
        /// template from the rules file becomes a wildcard too, so a templated layer
        /// name still classifies.
        /// </summary>
        private static Regex Glob(string pattern)
        {
            var expanded = Placeholder.Replace(pattern, "*");

            var sb = new StringBuilder("^");
            foreach (var ch in expanded)
            {
                if (ch == '*') sb.Append(".*");
                else if (ch == '?') sb.Append('.');
                else sb.Append(Regex.Escape(ch.ToString()));
            }
            sb.Append('$');

            return new Regex(sb.ToString(),
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
        }
    }
}
