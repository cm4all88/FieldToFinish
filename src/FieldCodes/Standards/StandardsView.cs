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
        /// Where this comes from: "Settings", "Rules: id", "Built-in", "Civil 3D",
        /// "Drawing", or "Not available yet". The last means the behaviour is fixed
        /// today and no setting exists -- shown honestly rather than invented to
        /// populate the page, and never presented as something that can be changed.
        /// </summary>
        public string Source { get; set; }

        /// <summary>The Settings section that edits this value ("Point Labels"), or
        /// null when nothing in FTF edits it.</summary>
        public string SettingsSection { get; set; }

        /// <summary>How to change it, in words: "Settings > Point Labels",
        /// "rules.json: modifiers (not editable in FTF)", "Fixed - not a setting".</summary>
        public string EditHint { get; set; }

        /// <summary>False for a standard that is not available yet: nothing changes it.</summary>
        public bool Operational
        {
            get { return !string.Equals(Source, StandardsViewBuilder.Planned, StringComparison.Ordinal); }
        }

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
    /// every line is either a real configured value that the commands read, a real
    /// built-in behaviour, something inherited from Civil 3D or the drawing, or
    /// explicitly marked not available yet.
    ///
    /// Each item says where it is edited, so the page can jump to the Settings
    /// section that changes it -- and only items a Settings section really changes
    /// carry one.
    ///
    /// No Autodesk types -- fully unit-testable.
    /// </summary>
    public sealed class StandardsViewBuilder
    {
        public const string Settings = "Settings";
        public const string BuiltIn = "Built-in";
        public const string Civil3D = "Civil 3D";
        public const string Drawing = "Drawing";

        /// <summary>Not yet a standard: fixed behaviour, no setting changes it.</summary>
        public const string Planned = "Not available yet";

        // Settings sections, by the labels the Settings page navigates by.
        public const string GeneralSection = "General";
        public const string PointFeaturesSection = "Point Features";
        public const string TreesSection = "Trees & Driplines";
        public const string PointLabelsSection = "Point Labels";
        public const string LineLabelsSection = "Line Labels";
        public const string TagsSection = "Tags";
        public const string ScheduleSection = "Schedule";
        public const string DrawOrderSection = "Drawing Order";
        public const string AdvancedSection = "Advanced";

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
                Layers(), PointLabels(), LineLabels(), Tags(), Tables(), DrawingOrder(),
                FeatureDefaults(), CodeHandling()
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
                section.Items.Add(RuleItem(pair.Key,
                    "Point labels: " + string.Join(", ", pair.Value.ToArray()),
                    "Rules: " + string.Join(", ", pair.Value.ToArray()), true));

            foreach (var pair in dripLayers.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
                section.Items.Add(RuleItem(pair.Key,
                    "Driplines: " + string.Join(", ", pair.Value.ToArray()),
                    "Rules: " + string.Join(", ", pair.Value.ToArray()), true));

            section.Items.Add(SettingItem(
                OrDefault(_settings.Tags.TagLayer, "(each feature's label layer)"),
                "Tags", TagsSection));
            section.Items.Add(SettingItem(
                OrDefault(_settings.Tags.TableLayer, "(current layer)"),
                "Schedule table", ScheduleSection));
            section.Items.Add(SettingItem(
                OrDefault(_settings.Labels.MaskLayer, "(the label's layer)"),
                "Point-label masks (line-label masks sit on the line label's own layer)",
                PointLabelsSection));
            section.Items.Add(SettingItem(
                OrDefault((_settings.LineLabels ?? new LineLabelSettings()).DefaultLabelLayer, "(none)"),
                "Line labels, fallback only: a feature's own label layer, then the matching " +
                "-TEXT layer, come first", LineLabelsSection));

            var suffixes = Suffixes();
            if (suffixes.Length > 0)
                section.Items.Add(RulesFileItem(
                    string.Join(", ", suffixes),
                    "Modifier suffixes, appended to every layer a modified feature draws",
                    "Rules: modifiers", "rules.json: modifiers[].layerSuffix"));

            return section;
        }

        // ------------------------------------------------------------ point labels

        private StandardsSection PointLabels()
        {
            var lp = _settings.Labels;
            var section = new StandardsSection("Point Labels",
                "FTFLABELS: the labels beside survey points. Tags and line labels have " +
                "their own standards below. Per-feature text and layer come from each " +
                "feature's rule.");

            section.Items.Add(SettingItem("Text style",
                OrDefault(lp.TextStyle, "(drawing's current style)"), PointLabelsSection));
            section.Items.Add(SettingItem("Text height (plotted)", Num(lp.TextHeightPlotted), PointLabelsSection));
            section.Items.Add(SettingItem("Padding around text (plotted)", Num(lp.PaddingPlotted), PointLabelsSection));
            section.Items.Add(SettingItem("Offset from point (plotted)", Num(lp.BaseOffsetPlotted), PointLabelsSection));
            section.Items.Add(SettingItem("Search rings",
                lp.RingCount + " ring(s), step " + Num(lp.RingStepPlotted) + " plotted", PointLabelsSection));
            section.Items.Add(SettingItem("Mask under labels",
                lp.DrawMask ? "On" : "Off", PointLabelsSection));
            section.Items.Add(SettingItem("Leader arrowhead",
                lp.SizeLeaderArrows ? Num(lp.LeaderArrowSizePlotted) + " plotted" : "As the multileader style draws it",
                PointLabelsSection));
            section.Items.Add(SettingItem("Crew notes read from",
                OrDefault(lp.NoteSource, "Automatic: a Note/Comment point property, else the Full Description"),
                PointLabelsSection));
            section.Items.Add(Fixed("Collision boxes",
                "Measured from the actual text geometry, never estimated"));
            section.Items.Add(RuleItem("Leader trigger",
                "Per feature rule: Always, Never, or Auto (when the first ring is full)",
                "Rules: per feature", true));
            section.Items.Add(NotAvailable("Justification",
                "Left-justified MText today; not yet a standard"));
            section.Items.Add(NotAvailable("Label rotation",
                "Horizontal today; rotate-with-feature is not yet a standard"));

            return section;
        }

        // ------------------------------------------------------------- line labels

        private StandardsSection LineLabels()
        {
            var ls = _settings.LineLabels ?? new LineLabelSettings();
            var section = new StandardsSection("Line Labels",
                "FTFLABELLINE (click, slide, place) and the optional automatic pass " +
                "FTFLINELABELS share these. Label text per feature comes from the rules' " +
                "line features.");

            var labelled = (_rules.LineFeatures ?? new List<LineFeatureRule>())
                .Count(f => f != null && f.Enabled && !string.IsNullOrWhiteSpace(f.Label));
            var total = (_rules.LineFeatures ?? new List<LineFeatureRule>()).Count(f => f != null);
            section.Items.Add(new StandardsItem("Line features with label text",
                labelled + " of " + total + " configured",
                "Rules: lineFeatures")
            {
                SettingsSection = LineLabelsSection,
                EditHint = "Rules file (rules.json): lineFeatures[].label"
            });

            section.Items.Add(SettingItem("Text style",
                OrDefault(ls.TextStyle, "(drawing's current style)"), LineLabelsSection));
            section.Items.Add(SettingItem("Text height (plotted)", Num(ls.TextHeightPlotted), LineLabelsSection));
            section.Items.Add(SettingItem("Alignment",
                ls.AlignToLine ? "Along the line, turned to read upright" : "Horizontal", LineLabelsSection));
            section.Items.Add(SettingItem("Default placement",
                OrDefault(ls.DefaultPlacement, "OnLine") + " (coding LEFT/RIGHT and a feature's placement win)",
                LineLabelsSection));
            section.Items.Add(SettingItem("Side offset", Num(ls.SideOffsetFeet) + " ft", LineLabelsSection));
            section.Items.Add(SettingItem("Mask under labels", ls.DrawMask ? "On" : "Off", LineLabelsSection));
            section.Items.Add(SettingItem("Lettered linetypes",
                ls.LeaveMarkedLinetypesToLegend
                    ? "Left to the legend by the automatic pass (alwaysLabel features excepted)"
                    : "Labelled like any other line", LineLabelsSection));
            section.Items.Add(SettingItem("Automatic pass (FTFLINELABELS in FTFRUN)",
                ls.Enabled ? "On" : "Off - FTFLABELLINE and FTFLABELALL only", LineLabelsSection));
            section.Items.Add(SettingItem("Minimum length (automatic pass only)",
                Num(ls.MinLengthFeet) + " ft", LineLabelsSection));
            section.Items.Add(SettingItem("Repeat interval (automatic pass only)",
                Num(ls.RepeatIntervalFeet) + " ft, " + Num(ls.EndClearanceFeet) + " ft end clearance",
                LineLabelsSection));
            section.Items.Add(SettingItem("Driveway wording (FTFLABELDRIVEWAY)",
                OrDefault(ls.DrivewayFormat, "{type} DWY"), LineLabelsSection));
            section.Items.Add(Fixed("Duplicate prevention",
                "The automatic pass replaces only its own earlier labels and skips a line " +
                "already labelled with FTFLABELLINE; hand-placed labels are never replaced"));

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

            section.Items.Add(RuleItem("Prefixes in use",
                prefixes.Length > 0 ? string.Join(", ", prefixes) : "(none)",
                "Rules: per feature", true));
            section.Items.Add(SettingItem("Tag layer",
                OrDefault(ts.TagLayer, "(each feature's label layer)"), TagsSection));
            section.Items.Add(SettingItem("Text style",
                OrDefault(ts.TagTextStyle, "(drawing's current style)"), TagsSection));
            section.Items.Add(SettingItem("Text height (plotted)", Num(ts.TagTextHeightPlotted), TagsSection));
            section.Items.Add(SettingItem("Offset from point (plotted)", Num(ts.TagOffsetPlotted), TagsSection));
            section.Items.Add(SettingItem("Start numbering at",
                ts.StartNumber.ToString(CultureInfo.InvariantCulture), TagsSection));
            section.Items.Add(SettingItem("Leader",
                ts.TagLeader ? "Drawn when a tag cannot sit next to its point" : "Off", TagsSection));
            section.Items.Add(Fixed("Numbering",
                "Stored on the tag itself; re-runs reissue the same number; a deleted " +
                "feature leaves a gap rather than renumbering"));

            return section;
        }

        // ------------------------------------------------------------------ tables

        private StandardsSection Tables()
        {
            var ts = _settings.Tags;
            var section = new StandardsSection("Tables",
                "The schedule built from tagged features, in tag order.");

            section.Items.Add(SettingItem("Title",
                OrDefault(ts.TableTitle, "(none)"), ScheduleSection));
            section.Items.Add(SettingItem("Layer",
                OrDefault(ts.TableLayer, "(current layer)"), ScheduleSection));
            section.Items.Add(SettingItem("Text height (plotted)", Num(ts.TableTextHeightPlotted), ScheduleSection));
            section.Items.Add(SettingItem("Columns",
                ts.TableColumns != null && ts.TableColumns.Count > 0
                    ? string.Join(", ", ts.TableColumns.ToArray())
                    : "(defaults)", ScheduleSection));
            section.Items.Add(Fixed("Row height",
                "2.0 x text height; title row 1.2 x that"));
            section.Items.Add(Fixed("Column widths",
                "Estimated from the longest entry, header included"));
            section.Items.Add(new StandardsItem("Table style",
                "The drawing's current table style (TABLESTYLE)", Drawing)
            {
                EditHint = "Inherited from the drawing: set the current table style in Civil 3D before running FTFTABLE"
            });
            section.Items.Add(Fixed("Placement",
                "Prompted once; reappears in the same place on every re-run"));

            return section;
        }

        // ------------------------------------------------------------ drawing order

        private StandardsSection DrawingOrder()
        {
            var d = _settings.DrawOrder;
            var section = new StandardsSection("Drawing Order",
                "Bands from bottom to top. Re-runnable on its own because other " +
                "operations disturb draw order.");

            section.Items.Add(Fixed("Band 1 (bottom): maskable linework",
                "Layers on the maskable list - content labels may sit on"));
            section.Items.Add(Fixed("Band 2: masks",
                "Wipeouts under labels, above the linework they hide"));
            section.Items.Add(Fixed("Band 3: symbols",
                "Survey points, protected layers and any layer not listed as maskable - " +
                "never hidden by a mask"));
            section.Items.Add(Fixed("Band 4 (top): labels and tags",
                "All FTF annotation, above everything"));

            section.Items.Add(SettingItem("Protected layers",
                d.ProtectedLayers != null && d.ProtectedLayers.Count > 0
                    ? string.Join(", ", d.ProtectedLayers.ToArray())
                    : "(none)", DrawOrderSection));
            section.Items.Add(SettingItem("Maskable layers",
                d.MaskableLayers != null && d.MaskableLayers.Count > 0
                    ? string.Join(", ", d.MaskableLayers.ToArray())
                    : "(none)", DrawOrderSection));

            var suffixes = Suffixes();
            section.Items.Add(RulesFileItem("Modifier suffix handling",
                (suffixes.Length > 0 ? string.Join(", ", suffixes) : "(no modifier suffixes)") +
                " are stripped before matching, so a modified feature bands the same as an " +
                "unmodified one",
                "Rules: modifiers", "rules.json: modifiers[].layerSuffix"));

            return section;
        }

        // --------------------------------------------------------- feature defaults

        private StandardsSection FeatureDefaults()
        {
            var section = new StandardsSection("Feature Defaults",
                "What applies when an individual feature rule does not say otherwise.");

            section.Items.Add(new StandardsItem("Symbol ownership",
                "Civil 3D's description keys place every symbol; FTF inserts a block " +
                "only when a rule explicitly opts in with insertBlock", Civil3D)
            {
                EditHint = "Inherited from Civil 3D: edit the description key set; insertBlock is in rules.json"
            });
            section.Items.Add(Fixed("Leader mode",
                "Auto - a leader appears only when the label cannot sit on the first ring " +
                "(a rule may say Always or Never)"));
            section.Items.Add(SettingItem("Dripline trimming",
                _settings.Drip.UnifyByDefault
                    ? "Overlapping canopies trimmed to their outer envelope (a rule with unify off draws whole)"
                    : "Canopies drawn whole", TreesSection));
            section.Items.Add(SettingItem("Drawing units per survey foot",
                Num(_settings.General.UnitsPerFoot), GeneralSection));
            section.Items.Add(SettingItem("Multi-stem average",
                _settings.Trees.MultiStemAverage + ", " + _settings.Trees.TrunkDecimals +
                " decimal place(s) in labels", TreesSection));
            section.Items.Add(SettingItem("Hand-moved point labels",
                "A point label moved more than " + Num(_settings.Cleanup.MovedTolerance) +
                " drawing units is kept and routed around by FTFLABELS (line labels placed " +
                "with FTFLABELLINE are always kept)", AdvancedSection));

            return section;
        }

        // ------------------------------------------------------------- field codes

        private StandardsSection CodeHandling()
        {
            var section = new StandardsSection("Field Codes",
                "How codes FTF does not finish are treated. None of these is an error.");

            var symbolCodes = (_rules.SymbolLabels ?? new List<SymbolLabelRule>())
                .Where(s => s != null && s.Enabled && !string.IsNullOrWhiteSpace(s.Code))
                .Select(s => s.Code.Trim())
                .ToArray();
            section.Items.Add(RulesFileItem("Bare codes",
                "A description carrying only its code gets no finishing" +
                (symbolCodes.Length > 0
                    ? ", except " + symbolCodes.Length + " code(s) labelled with the code itself (" +
                      string.Join(", ", symbolCodes.Take(8).ToArray()) + (symbolCodes.Length > 8 ? "..." : "") + ")"
                    : string.Empty),
                "Rules: symbolLabels", "rules.json: symbolLabels"));

            section.Items.Add(RulesFileItem("Linework codes",
                Count(_rules.LineworkCodes) + " code(s): Civil 3D builds the figures; reported as Informational",
                "Rules: lineworkCodes", "rules.json: lineworkCodes"));
            section.Items.Add(RulesFileItem("Never-draw codes",
                Count(_rules.IgnoreCodes) + " code(s)" + List(_rules.IgnoreCodes),
                "Rules: ignoreCodes", "rules.json: ignoreCodes"));

            var mine = (_settings.Diagnostics ?? new DiagnosticsSettings()).Codes();
            section.Items.Add(new StandardsItem("Codes ignored by you",
                mine.Count == 0
                    ? "(none)"
                    : mine.Count + " code(s)" + List(mine) + " - skipped only when no rule matches",
                Settings)
            {
                EditHint = "Unknown Codes page: Mark Ignored / Stop Ignoring (settings: diagnostics.ignoredCodes)"
            });

            section.Items.Add(Fixed("Unknown codes",
                "A code with data but no rule is reported under Needs Configuration for " +
                "review, never guessed at"));

            return section;
        }

        // ------------------------------------------------------------------ helpers

        private static StandardsItem SettingItem(string name, string value, string settingsSection)
        {
            return new StandardsItem(name, value, Settings)
            {
                SettingsSection = settingsSection,
                EditHint = "Settings > " + settingsSection
            };
        }

        /// <summary>Per-feature rule values. The safe subset (label layer, leader,
        /// tag prefix, dripline layer) is edited on Settings > Point Features.</summary>
        private static StandardsItem RuleItem(string name, string value, string source, bool pointFeatures)
        {
            return new StandardsItem(name, value, source)
            {
                SettingsSection = pointFeatures ? PointFeaturesSection : null,
                EditHint = pointFeatures
                    ? "Settings > Point Features (saved to the rules file)"
                    : "Rules file (rules.json)"
            };
        }

        /// <summary>Rules-file values with no editor in FTF.</summary>
        private static StandardsItem RulesFileItem(string name, string value, string source, string where)
        {
            return new StandardsItem(name, value, source)
            {
                EditHint = "Inherited from the rules file: " + where + " - not editable in FTF; " +
                           "edit the rules file (FTFWHERE shows which one is in force)"
            };
        }

        private static StandardsItem Fixed(string name, string value)
        {
            return new StandardsItem(name, value, BuiltIn) { EditHint = "Fixed in FTF - not a setting" };
        }

        private static StandardsItem NotAvailable(string name, string value)
        {
            return new StandardsItem(name, value, Planned)
            {
                EditHint = "Not available yet: nothing changes this today"
            };
        }

        private string[] Suffixes()
        {
            return _rules.Modifiers
                .Where(m => !string.IsNullOrEmpty(m.LayerSuffix))
                .Select(m => m.LayerSuffix)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

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

        private static int Count(ICollection<string> values)
        {
            return values == null ? 0 : values.Count(v => !string.IsNullOrWhiteSpace(v));
        }

        private static string List(IEnumerable<string> values)
        {
            var shown = (values ?? Enumerable.Empty<string>())
                .Where(v => !string.IsNullOrWhiteSpace(v)).Take(10).ToArray();
            return shown.Length == 0 ? string.Empty : " (" + string.Join(", ", shown) + ")";
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
