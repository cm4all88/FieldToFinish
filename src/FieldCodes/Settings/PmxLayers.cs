using System;
using System.Collections.Generic;
using System.Linq;

namespace FieldCodes.Settings
{
    /// <summary>
    /// The PMX layer standard FTF draws to, taken from a completed PMX Civil 3D drawing's
    /// 588-layer template: V-&lt;GROUP&gt;-&lt;FEATURE&gt;-E (-E = existing), with -PNTS-E, -SYMB-E
    /// and -TEXT-E companions (V-UTIL-POWR-E, V-UTIL-POWR-SYMB-E, V-UTIL-POWR-TEXT-E).
    ///
    /// Earlier builds labelled on layers that are not in that template (V-UTIL-POWR-TEXT,
    /// V-SIGN-TEXT, V-SITE-TEXT, V-TREE-DRIP...). Those names are retired: a settings file or
    /// a rules file saved by an earlier build is upgraded when it loads, name for name, so the
    /// office's own copies move to the standard with the shipped defaults. Only the exact
    /// retired FTF names are touched; any other layer an office chose is left alone.
    /// </summary>
    public static class PmxLayers
    {
        /// <summary>Labels may never cover these: utilities, signals, control, signs, symbols.</summary>
        public static readonly string[] Protected =
        {
            "V-UTIL-*", "V-SIGL-*", "V-CTRL-*", "V-SURF-SIGN-*", "V-SURF-*-SYMB-E", "V-PROP-*-SYMB*"
        };

        /// <summary>Linework a label may sit on and mask: contours, canopy, TIN lines, road
        /// construction lines.</summary>
        public static readonly string[] Maskable =
        {
            "V-TOPO-CONT-*", "V-TOPO-MAJR-E", "V-TOPO-MINR-E", "V-SURF-VEGE-E", "V-TINN-*", "V-ROAD-*"
        };

        /// <summary>Where a line label goes when no text layer matches its line.</summary>
        public const string LineLabelFallback = "V-TEXT-E";

        /// <summary>Tag numbers go on each feature's own text layer (blank = the label's layer).</summary>
        public const string TagLayer = "";

        /// <summary>Schedules and tables.</summary>
        public const string TableLayer = "V-TEXT-TABL";

        /// <summary>Tree drip lines: vegetation linework.</summary>
        public const string DripLayer = "V-SURF-VEGE-E";

        /// <summary>Bench outline and label (PMX puts benches with landscape features).</summary>
        public const string BenchLayer = "V-SURF-LAND-E";
        public const string BenchLabelLayer = "V-SURF-LAND-TEXT-E";

        /// <summary>Wetland buffer lines (FTFWETLANDBUFFER): PMX's wetland buffer layer.</summary>
        public const string WetlandBufferLayer = "V-WATR-WTLD-BUFR-E";

        internal static readonly string[] RetiredProtected = { "V-SIGN", "V-UTIL-*", "V-STRC-*", "C-STRM-STRC" };
        internal static readonly string[] RetiredMaskable = { "V-TREE-DRIP", "C-TOPO-*", "V-ROAD-*" };

        private static readonly Dictionary<string, string> Retired =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "V-UTIL-POWR-TEXT", "V-UTIL-POWR-TEXT-E" },
                { "V-UTIL-WATR-TEXT", "V-UTIL-WATR-TEXT-E" },
                { "V-UTIL-NGAS-TEXT", "V-UTIL-NGAS-TEXT-E" },
                { "V-UTIL-STRM-TEXT", "V-UTIL-STRM-TEXT-E" },
                { "V-UTIL-STRC-TEXT", "V-UTIL-STRC-TEXT-E" },
                { "V-CTRL-TEXT", "V-CTRL-TEXT-E" },
                { "V-SIGN-TEXT", "V-SURF-SIGN-TEXT-E" },
                { "V-TREE-TEXT", "V-SURF-VEGE-TEXT-E" },
                { "V-TREE-DRIP", DripLayer },
                { "V-SITE-FURN-E", BenchLayer },
                { "V-LINE-TEXT", LineLabelFallback },
                { "V-TREE-TAG", TagLayer },
                { "V-TREE-TABL", TableLayer },
                { "V-CTRL-TABL", TableLayer }
            };

        /// <summary>
        /// The PMX layer for a retired FTF layer name; any other name comes back unchanged.
        /// <paramref name="feature"/> (a rule id or field code) settles the names that served
        /// several features: V-SITE-TEXT was mailboxes, posts and benches; V-UTIL-POWR-TEXT
        /// was also light poles.
        /// </summary>
        public static string Upgrade(string layer, string feature = null)
        {
            if (string.IsNullOrWhiteSpace(layer)) return layer;
            var name = layer.Trim();
            var f = (feature ?? string.Empty).Trim().ToUpperInvariant();

            if (string.Equals(name, "V-SITE-TEXT", StringComparison.OrdinalIgnoreCase))
            {
                if (f == "MAILBOX" || f == "MB") return "V-SURF-MBOX-TEXT-E";
                if (f == "BOLLARD" || f == "POST" || f == "BOL") return "V-SURF-POST-TEXT-E";
                return BenchLabelLayer;
            }
            if (string.Equals(name, "V-UTIL-POWR-TEXT", StringComparison.OrdinalIgnoreCase) &&
                (f == "LIGHT-POLE" || f == "LT" || f == "LP"))
                return "V-UTIL-LITG-TEXT-E";

            string pmx;
            return Retired.TryGetValue(name, out pmx) ? pmx : layer;
        }

        /// <summary>A list still exactly the retired shipped default (in any order) becomes the
        /// PMX default; a list the office edited is kept. True when it changed.</summary>
        public static bool UpgradeList(List<string> list, string[] retired, string[] replacement)
        {
            if (list == null) return false;
            var current = new HashSet<string>(list.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()),
                                              StringComparer.OrdinalIgnoreCase);
            if (!current.SetEquals(retired)) return false;
            list.Clear();
            list.AddRange(replacement);
            return true;
        }

        /// <summary>Upgrades a rules file in place. Returns how many values changed.</summary>
        public static int Upgrade(RulesConfig rules)
        {
            if (rules == null) return 0;
            var changed = 0;

            foreach (var rule in rules.Codes ?? new List<CodeRule>())
            {
                if (rule == null) continue;
                if (rule.Label != null) changed += Set(rule.Label.Layer, Upgrade(rule.Label.Layer, rule.Id), v => rule.Label.Layer = v);
                if (rule.DripLine != null) changed += Set(rule.DripLine.Layer, Upgrade(rule.DripLine.Layer, rule.Id), v => rule.DripLine.Layer = v);
            }
            foreach (var symbol in rules.SymbolLabels ?? new List<SymbolLabelRule>())
            {
                if (symbol == null) continue;
                changed += Set(symbol.Layer, Upgrade(symbol.Layer, symbol.Code), v => symbol.Layer = v);
            }
            if (rules.Bench != null)
            {
                changed += Set(rules.Bench.Layer, Upgrade(rules.Bench.Layer, "bench"), v => rules.Bench.Layer = v);
                changed += Set(rules.Bench.LabelLayer, Upgrade(rules.Bench.LabelLayer, "bench"), v => rules.Bench.LabelLayer = v);
            }
            if (UpgradeList(rules.ProtectedLayers, RetiredProtected, Protected)) changed++;
            if (UpgradeList(rules.MaskableLayers, RetiredMaskable, Maskable)) changed++;
            return changed;
        }

        /// <summary>Upgrades a settings file in place. Returns how many values changed.</summary>
        public static int Upgrade(FtfSettings settings)
        {
            if (settings == null) return 0;
            var changed = 0;
            if (settings.DrawOrder != null)
            {
                if (UpgradeList(settings.DrawOrder.ProtectedLayers, RetiredProtected, Protected)) changed++;
                if (UpgradeList(settings.DrawOrder.MaskableLayers, RetiredMaskable, Maskable)) changed++;
            }
            if (settings.LineLabels != null)
                changed += Set(settings.LineLabels.DefaultLabelLayer, Upgrade(settings.LineLabels.DefaultLabelLayer),
                               v => settings.LineLabels.DefaultLabelLayer = v);
            if (settings.Tags != null)
            {
                changed += Set(settings.Tags.TagLayer, Upgrade(settings.Tags.TagLayer), v => settings.Tags.TagLayer = v);
                changed += Set(settings.Tags.TableLayer, Upgrade(settings.Tags.TableLayer), v => settings.Tags.TableLayer = v);
            }
            if (settings.Labels != null)
                changed += Set(settings.Labels.MaskLayer, Upgrade(settings.Labels.MaskLayer), v => settings.Labels.MaskLayer = v);
            return changed;
        }

        private static int Set(string before, string after, Action<string> assign)
        {
            if (string.Equals(before, after, StringComparison.Ordinal)) return 0;
            assign(after);
            return 1;
        }
    }
}
