using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FieldCodes.Settings;

namespace FieldCodes.Standards
{
    /// <summary>One line of the standards view.</summary>
    public sealed class StandardsItem
    {
        public string Name { get; set; }
        public string Value { get; set; }

        /// <summary>
        /// Where this comes from: "Settings", "Rules: id", "Built-in", or "Planned".
        /// Planned means the behaviour is fixed today and a setting does not exist yet
        /// -- shown honestly rather than invented to populate the page.
        /// </summary>
        public string Source { get; set; }

        public StandardsItem(string name, string value, string source)
        {
            Name = name;
            Value = value;
            Source = source;
        }
    }

    public sealed class StandardsSection
    {
        public string Title { get; set; }
        public string Note { get; set; }
        public IList<StandardsItem> Items { get; set; }

        public StandardsSection(string title, string note)
        {
            Title = title;
            Note = note;
            Items = new List<StandardsItem>();
        }
    }

    /// <summary>
    /// Assembles the read-only standards view from the two places configuration
    /// actually lives: the rules file (grammar and per-feature layers) and
    /// ftf-settings.json (behaviour). Nothing here writes, and nothing here invents:
    /// every line is either a real configured value, a real built-in behaviour, or
    /// explicitly marked Planned.
    ///
    /// No Autodesk types -- fully unit-testable.
    /// </summary>
    public sealed class StandardsViewBuilder
    {
        public const string Settings = "Settings";
        public const string BuiltIn = "Built-in";
        public const string Planned = "Planned";

        private readonly RulesConfig _rules;
        private readonly FtfSettings _settings;

        public StandardsViewBuilder(RulesConfig rules, FtfSettings settings)
        {
            if (rules == null) throw new ArgumentNullException("rules");
            if (settings == null) throw new ArgumentNullException("settings");
            _rules = rules;
            _settings = settings;
        }

        public IList<StandardsSection> Build()
        {
            return new List<StandardsSection>
            {
                Layers(), Labels(), Tags(), Tables(), DrawingOrder(), FeatureDefaults()
            };
        }

        // ------------------------------------------------------------------ layers

        private StandardsSection Layers()
        {
            var section = new StandardsSection("Layers",
                "Every layer FTF finishing writes to. Feature symbols are not listed: " +
                "they sit on the layers Civil 3D's description keys assign.");

            // Label layers, grouped so shared layers list every rule using them.
            var labelLayers = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var dripLayers = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var rule in _rules.Codes)
            {
                if (rule.Label != null && !string.IsNullOrEmpty(rule.Label.Layer))
                    Append(labelLayers, rule.Label.Layer, rule.Id);
                if (rule.DripLine != null && !string.IsNullOrEmpty(rule.DripLine.Layer))
                    Append(dripLayers, rule.DripLine.Layer, rule.Id);
            }

            foreach (var pair in labelLayers.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
                section.Items.Add(new StandardsItem(pair.Key,
                    "Labels: " + string.Join(", ", pair.Value.ToArray()),
                    "Rules: " + string.Join(", ", pair.Value.ToArray())));

            foreach (var pair in dripLayers.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
                section.Items.Add(new StandardsItem(pair.Key,
                    "Driplines: " + string.Join(", ", pair.Value.ToArray()),
                    "Rules: " + string.Join(", ", pair.Value.ToArray())));

            section.Items.Add(new StandardsItem(
                OrDefault(_settings.Tags.TagLayer, "(the label's layer)"),
                "Tags", Settings));
            section.Items.Add(new StandardsItem(
                OrDefault(_settings.Tags.TableLayer, "(current layer)"),
                "Schedule table", Settings));
            section.Items.Add(new StandardsItem(
                OrDefault(_settings.Labels.MaskLayer, "(the label's layer)"),
                "Label masks", Settings));

            var suffixes = _rules.Modifiers
                .Where(m => !string.IsNullOrEmpty(m.LayerSuffix))
                .Select(m => m.LayerSuffix)
                .ToArray();
            if (suffixes.Length > 0)
                section.Items.Add(new StandardsItem(
                    string.Join(", ", suffixes),
                    "Modifier suffixes, appended to every layer a modified feature draws",
                    "Rules: modifiers"));

            return section;
        }

        // ------------------------------------------------------------------ labels

        private StandardsSection Labels()
        {
            var lp = _settings.Labels;
            var section = new StandardsSection("Labels",
                "Applies to every label FTF places. Per-feature text and layer come " +
                "from each feature's rule.");

            section.Items.Add(new StandardsItem("Text style",
                OrDefault(lp.TextStyle, "(drawing's current style)"), Settings));
            section.Items.Add(new StandardsItem("Text height (plotted)", Num(lp.TextHeightPlotted), Settings));
            section.Items.Add(new StandardsItem("Padding around text (plotted)", Num(lp.PaddingPlotted), Settings));
            section.Items.Add(new StandardsItem("Offset from point (plotted)", Num(lp.BaseOffsetPlotted), Settings));
            section.Items.Add(new StandardsItem("Search rings",
                lp.RingCount + " ring(s), step " + Num(lp.RingStepPlotted) + " plotted", Settings));
            section.Items.Add(new StandardsItem("Mask under labels",
                lp.DrawMask ? "On" : "Off", Settings));
            section.Items.Add(new StandardsItem("Leader arrowhead",
                lp.LeaderArrowhead ? "On, size " + Num(lp.LeaderArrowSizePlotted) + " plotted" : "Off",
                Settings));
            section.Items.Add(new StandardsItem("Collision boxes",
                "Measured from the actual text geometry, never estimated", BuiltIn));
            section.Items.Add(new StandardsItem("Leader trigger",
                "Per feature rule: Always, Never, or Auto (when the first ring is full)",
                "Rules: per feature"));
            section.Items.Add(new StandardsItem("Justification",
                "Left-justified today; not yet a standard", Planned));
            section.Items.Add(new StandardsItem("Label rotation",
                "Horizontal today; rotate-with-feature is not yet a standard", Planned));

            return section;
        }

        // -------------------------------------------------------------------- tags

        private StandardsSection Tags()
        {
            var ts = _settings.Tags;
            var section = new StandardsSection("Tags",
                "Tag identity is permanent: a number printed on an issued plan never " +
                "moves to a different feature.");

            var prefixes = _rules.Codes
                .Where(c => !string.IsNullOrEmpty(c.TagPrefix))
                .GroupBy(c => c.TagPrefix, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.Key + " (" + string.Join(", ", g.Select(c => c.Id).ToArray()) + ")")
                .ToArray();

            section.Items.Add(new StandardsItem("Prefixes in use",
                prefixes.Length > 0 ? string.Join(", ", prefixes) : "(none)",
                "Rules: per feature"));
            section.Items.Add(new StandardsItem("Tag layer",
                OrDefault(ts.TagLayer, "(the label's layer)"), Settings));
            section.Items.Add(new StandardsItem("Text style",
                OrDefault(ts.TagTextStyle, "(drawing's current style)"), Settings));
            section.Items.Add(new StandardsItem("Text height (plotted)", Num(ts.TagTextHeightPlotted), Settings));
            section.Items.Add(new StandardsItem("Offset from point (plotted)", Num(ts.TagOffsetPlotted), Settings));
            section.Items.Add(new StandardsItem("Start numbering at",
                ts.StartNumber.ToString(CultureInfo.InvariantCulture), Settings));
            section.Items.Add(new StandardsItem("Leader",
                ts.TagLeader ? "Drawn when a tag cannot sit next to its point" : "Off", Settings));
            section.Items.Add(new StandardsItem("Numbering",
                "Stored on the tag itself; re-runs reissue the same number; a deleted " +
                "feature leaves a gap rather than renumbering", BuiltIn));

            return section;
        }

        // ------------------------------------------------------------------ tables

        private StandardsSection Tables()
        {
            var ts = _settings.Tags;
            var section = new StandardsSection("Tables",
                "The schedule built from tagged features, in tag order.");

            section.Items.Add(new StandardsItem("Title",
                OrDefault(ts.TableTitle, "(none)"), Settings));
            section.Items.Add(new StandardsItem("Layer",
                OrDefault(ts.TableLayer, "(current layer)"), Settings));
            section.Items.Add(new StandardsItem("Text height (plotted)", Num(ts.TableTextHeightPlotted), Settings));
            section.Items.Add(new StandardsItem("Columns",
                ts.TableColumns != null && ts.TableColumns.Count > 0
                    ? string.Join(", ", ts.TableColumns.ToArray())
                    : "(defaults)", Settings));
            section.Items.Add(new StandardsItem("Row height",
                "2.0 x text height; title row 1.2 x that", BuiltIn));
            section.Items.Add(new StandardsItem("Column widths",
                "Sized to the widest entry, header included, so nothing wraps", BuiltIn));
            section.Items.Add(new StandardsItem("Table style",
                "(drawing's current table style)", BuiltIn));
            section.Items.Add(new StandardsItem("Placement",
                "Prompted once; reappears in the same place on every re-run", BuiltIn));

            return section;
        }

        // ------------------------------------------------------------ drawing order

        private StandardsSection DrawingOrder()
        {
            var d = _settings.DrawOrder;
            var section = new StandardsSection("Drawing Order",
                "Bands from bottom to top. Re-runnable on its own because other " +
                "operations disturb draw order.");

            section.Items.Add(new StandardsItem("Band 1 (bottom): maskable linework",
                "Contours, driplines, road linework - content labels may sit on", BuiltIn));
            section.Items.Add(new StandardsItem("Band 2: masks",
                "Wipeouts under labels, above the linework they hide", BuiltIn));
            section.Items.Add(new StandardsItem("Band 3: symbols",
                "Survey symbols and protected features - never hidden by a mask", BuiltIn));
            section.Items.Add(new StandardsItem("Band 4 (top): labels and tags",
                "All annotation, above everything", BuiltIn));

            section.Items.Add(new StandardsItem("Protected layers",
                d.ProtectedLayers != null && d.ProtectedLayers.Count > 0
                    ? string.Join(", ", d.ProtectedLayers.ToArray())
                    : "(none)", Settings));
            section.Items.Add(new StandardsItem("Maskable layers",
                d.MaskableLayers != null && d.MaskableLayers.Count > 0
                    ? string.Join(", ", d.MaskableLayers.ToArray())
                    : "(none)", Settings));
            section.Items.Add(new StandardsItem("Modifier suffix handling",
                "-DEAD, -RMV, -PROT are stripped before matching, so a modified " +
                "feature bands the same as an unmodified one", BuiltIn));

            return section;
        }

        // --------------------------------------------------------- feature defaults

        private StandardsSection FeatureDefaults()
        {
            var section = new StandardsSection("Feature Defaults",
                "What applies when an individual feature rule does not say otherwise.");

            section.Items.Add(new StandardsItem("Symbol ownership",
                "Civil 3D's description keys place every symbol; FTF inserts a block " +
                "only when a rule explicitly opts in with insertBlock", BuiltIn));
            section.Items.Add(new StandardsItem("Leader mode",
                "Auto - a leader appears only when the label cannot sit on the first ring",
                BuiltIn));
            section.Items.Add(new StandardsItem("Dripline trimming",
                _settings.Drip.UnifyByDefault
                    ? "Overlapping canopies trimmed to their outer envelope"
                    : "Canopies drawn whole", Settings));
            section.Items.Add(new StandardsItem("Drawing units per survey foot",
                Num(_settings.General.UnitsPerFoot), Settings));
            section.Items.Add(new StandardsItem("Multi-stem average",
                _settings.Trees.MultiStemAverage + ", " + _settings.Trees.TrunkDecimals +
                " decimal place(s) in labels", Settings));
            section.Items.Add(new StandardsItem("Hand-placed annotation",
                "Anything moved more than " + Num(_settings.Cleanup.MovedTolerance) +
                " drawing units is left alone and routed around", Settings));
            section.Items.Add(new StandardsItem("Bare codes",
                "A description carrying only its code produces no finishing", BuiltIn));
            section.Items.Add(new StandardsItem("Unknown codes",
                "A code with data but no rule is reported for review, never guessed at",
                BuiltIn));

            return section;
        }

        // ------------------------------------------------------------------ helpers

        private static void Append(Dictionary<string, List<string>> map, string key, string value)
        {
            List<string> list;
            if (!map.TryGetValue(key, out list))
            {
                list = new List<string>();
                map[key] = list;
            }
            list.Add(value);
        }

        private static string OrDefault(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }

        private static string Num(double v)
        {
            return v.ToString("0.######", CultureInfo.InvariantCulture);
        }
    }
}
