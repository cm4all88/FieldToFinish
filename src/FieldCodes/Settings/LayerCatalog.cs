using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace FieldCodes.Settings
{
    /// <summary>Where a layer name comes from, which is also where it is edited.</summary>
    public enum LayerSource
    {
        /// <summary>A code rule, symbol label or line feature in rules.json (office standard).</summary>
        Rules = 0,
        /// <summary>A settings section (drawing, profile, user or office settings file).</summary>
        Settings = 1,
        /// <summary>Fixed in the plugin.</summary>
        BuiltIn = 2
    }

    /// <summary>The kind of thing a layer carries, for filtering Settings > Layers. Every object
    /// FTF draws falls in one of these.</summary>
    public enum LayerFamily
    {
        /// <summary>Labels FTF writes at COGO points (code rules), and their masks.</summary>
        PointLabels = 0,
        /// <summary>Symbols FTF inserts, and labels at points whose symbol Civil 3D draws.</summary>
        Symbols = 1,
        /// <summary>Line labels, drafting-line bearing and distance labels.</summary>
        LineworkLabels = 2,
        /// <summary>Tree labels, tags, symbols and driplines.</summary>
        Trees = 3,
        /// <summary>Dip Builder pipes, invert and structure annotation.</summary>
        UtilityDips = 4,
        /// <summary>Leaders and multileaders. FTF draws each on the layer of the label it belongs to.</summary>
        Leaders = 5,
        SpotElevations = 6,
        Contours = 7,
        /// <summary>Stripes, stop bars, crosswalks, pavement arrows and words.</summary>
        PavementMarkings = 8,
        /// <summary>Survey control and monument labels and symbols.</summary>
        SurveyControl = 9,
        /// <summary>Schedules, control, easement and record tables, and the legend.</summary>
        TablesAndLegends = 10,
        /// <summary>Easements, exhibits, recorded surveys, right of way, platted blocks.</summary>
        EasementAndBoundary = 11,
        /// <summary>Everything else FTF draws: benches, sheet windows, orientation QC, drafting lines.</summary>
        Other = 12
    }

    /// <summary>One thing FTF draws on a layer.</summary>
    public sealed class LayerUse
    {
        /// <summary>The feature or tool: "Tree (rule tree)", "Tags", "Contours".</summary>
        public string Feature { get; set; }
        /// <summary>What goes on it: "Label", "Dripline", "Tag", "Schedule", "Marker"...</summary>
        public string ObjectType { get; set; }
        /// <summary>The layer as configured, before any project mapping.</summary>
        public string Layer { get; set; }
        public LayerSource Source { get; set; }
        /// <summary>Where to change the configured name: "rules.json: codes[tree].label.layer",
        /// "Settings > Tags".</summary>
        public string EditedIn { get; set; }
        /// <summary>The settings page that edits it, when there is one.</summary>
        public string SettingsSection { get; set; }
        /// <summary>The kind of object, for the family filter.</summary>
        public LayerFamily Family { get; set; }
        /// <summary>The rules.json modifier whose layer suffix makes this layer ("dead" for
        /// V-SURF-VEGE-TEXT-E-DEAD); null for the plain layer.</summary>
        public string Modifier { get; set; }
        /// <summary>True for a layer FTF uses only when the drawing already has it (a line label's
        /// text layer derived from its linework layer); FTF never creates it.</summary>
        public bool OnlyIfPresent { get; set; }
    }

    /// <summary>How one layer ends up in the drawing after mapping.</summary>
    public enum LayerStatus
    {
        /// <summary>In the drawing under the name FTF will use.</summary>
        Exists = 0,
        /// <summary>Mapped to a project layer that is in the drawing.</summary>
        MappedExists = 1,
        /// <summary>Not in the drawing: FTF creates it (taking the look of its family) on first use.</summary>
        WillBeCreated = 2,
        /// <summary>Not in the drawing, but the drawing has the same name spelled differently: that one is used.</summary>
        SpelledDifferently = 3,
        /// <summary>Not in the drawing, and similar layers are: FTF asks before creating it.</summary>
        SimilarExists = 4
    }

    /// <summary>
    /// Every layer FieldToFinish draws on, which feature puts what on it, and where the name
    /// is configured -- the table on Settings > Layers. The project mapping (an FTF layer and
    /// the project's layer used in its place, kept in <see cref="GeneralSettings.LayerMappings"/>)
    /// applies to every command through <see cref="LayerMap"/>, so changing a layer here is
    /// honoured by labels, driplines, tags, the schedule, spots, contours, line labels and the
    /// production tools alike.
    /// </summary>
    public static class LayerCatalog
    {
        /// <summary>Leaders are drawn on their label's layer: the row says so.</summary>
        public const string LeaderOnLabelLayer = "Leader (multileader), on the label layer";

        /// <summary>The fixed layer FTFCONTROL draws the control table on.</summary>
        public const string ControlTableFeature = "Survey control table (FTFCONTROL)";

        public static IList<LayerUse> Build(RulesConfig rules, FtfSettings settings)
        {
            return Build(rules, settings, null);
        }

        /// <summary>
        /// As above. <paramref name="drawingLayers"/>, when given, settles each labelled line
        /// feature's text layer the way FTFLINELABELS does (a text layer derived from its linework
        /// layer when the drawing has one, else the default label layer); without it every line
        /// label without its own layer is listed on the default label layer.
        /// </summary>
        public static IList<LayerUse> Build(RulesConfig rules, FtfSettings settings, IEnumerable<string> drawingLayers)
        {
            var uses = new List<LayerUse>();
            if (rules != null)
            {
                foreach (var c in rules.Codes ?? new List<CodeRule>())
                {
                    if (c == null) continue;
                    var name = (string.IsNullOrWhiteSpace(c.Description) ? c.Id : c.Description) + " (" + c.Id + ")";
                    var family = FamilyOf(c);
                    if (c.Label != null) Add(uses, name, "Label", c.Label.Layer, LayerSource.Rules, "rules.json: code " + c.Id + " label layer", "Point Features", family);
                    if (c.DripLine != null) Add(uses, name, "Dripline", c.DripLine.Layer, LayerSource.Rules, "rules.json: code " + c.Id + " dripline layer", "Point Features", LayerFamily.Trees);
                    if (c.InsertBlock) Add(uses, name, "Inserted symbol", c.BlockLayer, LayerSource.Rules, "rules.json: code " + c.Id + " block layer", "Point Features",
                                           family == LayerFamily.PointLabels ? LayerFamily.Symbols : family);
                }
                foreach (var sl in rules.SymbolLabels ?? new List<SymbolLabelRule>())
                    if (sl != null && sl.Enabled)
                        Add(uses, "Code " + sl.Code, "Label", sl.Layer, LayerSource.Rules, "rules.json: symbolLabels " + sl.Code, "Point Features",
                            SymbolFamily(rules, sl.Code));
                foreach (var lf in rules.LineFeatures ?? new List<LineFeatureRule>())
                    if (lf != null && !string.IsNullOrWhiteSpace(lf.Label) && !string.IsNullOrWhiteSpace(lf.LabelLayer))
                        Add(uses, "Line " + (lf.Name ?? lf.Code) + " (" + lf.Code + ")", "Line label", lf.LabelLayer, LayerSource.Rules,
                            "rules.json: lineFeatures " + lf.Code + " labelLayer", "Line Labels", LineFamily(rules, lf));
                if (rules.WetlandBuffer != null)
                    Add(uses, "Wetland buffer", "Buffer line", rules.WetlandBuffer.Layer, LayerSource.Rules, "rules.json: wetlandBuffer layer", null, LayerFamily.Other);
                if (rules.Bench != null)
                {
                    Add(uses, "Bench", "Outline", rules.Bench.Layer, LayerSource.Rules, "rules.json: bench layer", null, LayerFamily.Other);
                    Add(uses, "Bench", "Size label", rules.Bench.LabelLayer, LayerSource.Rules, "rules.json: bench labelLayer", null, LayerFamily.Other);
                }
                if (rules.Orientation != null)
                {
                    Add(uses, "Symbol orientation (FTFORIENT)", "QC arrows (not plotted)", rules.Orientation.QcLayer, LayerSource.Rules, "rules.json: orientation qcLayer", null, LayerFamily.Other);
                    Add(uses, "Symbol orientation (FTFORIENT)", "Inferred connections (not plotted)", rules.Orientation.QcInferredLayer, LayerSource.Rules, "rules.json: orientation qcInferredLayer", null, LayerFamily.Other);
                }
            }

            if (settings != null)
            {
                settings.FillMissingSections();
                var start = uses.Count;
                foreach (var section in settings.Sections)
                {
                    if (section is LayerStandardsSettings) continue;     // properties, not layers FTF draws on
                    Walk(uses, section, section.Title, section.Title, 0);
                }
                foreach (var u in uses.Skip(start))
                {
                    var page = PageFor(u.SettingsSection, u.ObjectType);
                    if (u.Feature == u.SettingsSection) u.Feature = page;
                    else if (u.Feature.StartsWith(u.SettingsSection + ": ", StringComparison.Ordinal))
                        u.Feature = page + u.Feature.Substring(u.SettingsSection.Length);
                    u.SettingsSection = page;
                    u.EditedIn = "Settings > " + page;
                    u.Family = SettingsFamily(page, u.ObjectType);
                }
            }

            AddCoverage(uses, rules, settings, drawingLayers);
            AddModifierLayers(uses, rules);

            return uses.Where(u => !string.IsNullOrWhiteSpace(u.Layer))
                       .GroupBy(u => u.Feature + "|" + u.ObjectType + "|" + u.Layer, StringComparer.OrdinalIgnoreCase)
                       .Select(g => g.First())
                       .ToList();
        }

        /// <summary>
        /// Objects that sit on a layer named elsewhere, listed so every kind of thing FTF draws is
        /// in the table: leaders (on their label's layer), tags that follow their feature's label,
        /// labels of line features without a layer of their own, the legend, and the control
        /// table, whose layer is fixed in the plugin (a New Layer still moves it).
        /// </summary>
        private static void AddCoverage(List<LayerUse> uses, RulesConfig rules, FtfSettings settings, IEnumerable<string> drawingLayers)
        {
            var tagLayer = settings != null && settings.Tags != null ? (settings.Tags.TagLayer ?? string.Empty).Trim() : string.Empty;
            var tagLeaders = settings != null && settings.Tags != null && settings.Tags.TagLeader;
            if (rules != null)
            {
                foreach (var c in rules.Codes ?? new List<CodeRule>())
                {
                    if (c == null || c.Label == null || string.IsNullOrWhiteSpace(c.Label.Layer)) continue;
                    var name = (string.IsNullOrWhiteSpace(c.Description) ? c.Id : c.Description) + " (" + c.Id + ")";
                    var family = FamilyOf(c);
                    if (c.Label.Leader != LeaderMode.Never)
                        Add(uses, name, LeaderOnLabelLayer, c.Label.Layer, LayerSource.Rules, "rules.json: code " + c.Id + " label layer", "Point Features", LayerFamily.Leaders);
                    if (!string.IsNullOrWhiteSpace(c.TagPrefix) && tagLayer.Length == 0)
                    {
                        Add(uses, name, "Tag, on the label layer (Settings > Tags can name a tag layer)", c.Label.Layer, LayerSource.Rules,
                            "rules.json: code " + c.Id + " label layer", "Tags", family == LayerFamily.Trees ? LayerFamily.Trees : LayerFamily.PointLabels);
                        if (tagLeaders)
                            Add(uses, name, "Tag leader (multileader), on the label layer", c.Label.Layer, LayerSource.Rules,
                                "rules.json: code " + c.Id + " label layer", "Tags", LayerFamily.Leaders);
                    }
                }

                // A labelled line feature without its own label layer: FTFLINELABELS puts the text on
                // the office text layer that matches its linework when the drawing has one, else on
                // the default label layer.
                var defaultLabel = settings != null && settings.LineLabels != null ? settings.LineLabels.DefaultLabelLayer : null;
                var existing = FieldCodes.Linework.LabelLayerResolver.BuildCatalog(drawingLayers);
                foreach (var lf in rules.LineFeatures ?? new List<LineFeatureRule>())
                {
                    if (lf == null || !lf.Enabled || string.IsNullOrWhiteSpace(lf.Label) || !string.IsNullOrWhiteSpace(lf.LabelLayer)) continue;
                    var resolution = FieldCodes.Linework.LabelLayerResolver.Resolve(lf.Layer, new List<LineFeatureRule> { lf }, existing, defaultLabel);
                    if (string.IsNullOrWhiteSpace(resolution.Layer)) continue;
                    var derived = resolution.Source == FieldCodes.Linework.LabelLayerSource.DerivedText ||
                                  resolution.Source == FieldCodes.Linework.LabelLayerSource.FamilyText;
                    var use = Add(uses, "Line " + (lf.Name ?? lf.Code) + " (" + lf.Code + ")",
                                  derived ? "Line label, on the drawing's text layer for " + lf.Layer : "Line label, on the default label layer",
                                  resolution.Layer, derived ? LayerSource.Rules : LayerSource.Settings,
                                  derived ? "the drawing's own layer; rules.json: lineFeatures " + lf.Code + " labelLayer names one instead" : "Settings > Line Labels",
                                  "Line Labels", LineFamily(rules, lf));
                    if (use != null) use.OnlyIfPresent = derived;
                }
            }

            if (settings == null) return;

            if (settings.LineLabels != null && !string.IsNullOrWhiteSpace(settings.LineLabels.DefaultLabelLayer))
                Add(uses, "Legend (FTFLEGEND)", "Legend title and names, on the default line label layer", settings.LineLabels.DefaultLabelLayer,
                    LayerSource.Settings, "Settings > Line Labels", "Line Labels", LayerFamily.TablesAndLegends);

            if (tagLayer.Length > 0 && tagLeaders)
                Add(uses, "Tags", "Tag leader (multileader), on the tag layer", tagLayer, LayerSource.Settings, "Settings > Tags", "Tags", LayerFamily.Leaders);

            foreach (var system in (settings.Dips != null ? settings.Dips.Systems : null) ?? new List<UtilitySystemStandard>())
                if (system != null && !string.IsNullOrWhiteSpace(system.StructureLabelLayer))
                    Add(uses, "Utility Dips: " + system.System, "Structure callout leader (multileader), on the structure label layer",
                        system.StructureLabelLayer, LayerSource.Settings, "Settings > Utility Dips", "Utility Dips", LayerFamily.Leaders);

            var es = settings.Easements;
            if (es != null)
            {
                Add(uses, "Strip Easements", "Callout leader (multileader), on the text layer", es.TextLayer, LayerSource.Settings,
                    "Settings > Strip Easements", "Strip Easements", LayerFamily.Leaders);
                Add(uses, "Strip Easements", "Temporary callout leader (multileader), on the temporary text layer", es.TemporaryText(), LayerSource.Settings,
                    "Settings > Strip Easements", "Strip Easements", LayerFamily.Leaders);
            }
            if (settings.Exhibits != null)
                Add(uses, "Easement Exhibits", "POB / POC and area leaders (multileader), on the annotation layer", settings.Exhibits.AnnotationLayer,
                    LayerSource.Settings, "Settings > Easement Exhibits", "Easement Exhibits", LayerFamily.Leaders);

            Add(uses, ControlTableFeature, "Table", PmxLayers.TableLayer, LayerSource.BuiltIn,
                "Fixed in the plugin: send it to the project's layer with New Layer", null, LayerFamily.TablesAndLegends);
        }

        /// <summary>A modifier with a layer suffix (DEAD: -DEAD) sends a point's label, dripline and
        /// symbol to the suffixed layer, which FTF creates: V-SURF-VEGE-TEXT-E-DEAD.</summary>
        private static void AddModifierLayers(List<LayerUse> uses, RulesConfig rules)
        {
            if (rules == null) return;
            var modifiers = (rules.Modifiers ?? new List<ModifierRule>()).Where(m => m != null && !string.IsNullOrWhiteSpace(m.LayerSuffix)).ToList();
            if (modifiers.Count == 0) return;
            // Only a rule whose pattern takes modifiers (a "mods" group) can carry one.
            foreach (var c in (rules.Codes ?? new List<CodeRule>()).Where(c => c != null && (c.Match ?? string.Empty).IndexOf("?<mods>", StringComparison.Ordinal) >= 0))
            {
                var name = (string.IsNullOrWhiteSpace(c.Description) ? c.Id : c.Description) + " (" + c.Id + ")";
                var family = FamilyOf(c);
                var layers = new List<KeyValuePair<string, string>>();
                if (c.Label != null) layers.Add(new KeyValuePair<string, string>("Label", c.Label.Layer));
                if (c.DripLine != null) layers.Add(new KeyValuePair<string, string>("Dripline", c.DripLine.Layer));
                if (c.InsertBlock) layers.Add(new KeyValuePair<string, string>("Inserted symbol", c.BlockLayer));
                foreach (var layer in layers.Where(l => !string.IsNullOrWhiteSpace(l.Value) && l.Value.IndexOf('{') < 0))
                    foreach (var m in modifiers)
                    {
                        var kind = layer.Key == "Dripline" ? LayerFamily.Trees
                                 : layer.Key == "Inserted symbol" && family == LayerFamily.PointLabels ? LayerFamily.Symbols : family;
                        var use = Add(uses, name, layer.Key + " (" + m.Id.ToUpperInvariant() + " modifier)", layer.Value.Trim() + m.LayerSuffix.Trim(),
                                      LayerSource.Rules, "rules.json: code " + c.Id + " " + layer.Key.ToLowerInvariant() + " layer + modifier " + m.Id + " layerSuffix",
                                      "Point Features", kind);
                        if (use != null) use.Modifier = m.Id;
                    }
            }
        }

        // ------------------------------------------------------------ families

        public static string FamilyName(LayerFamily family)
        {
            switch (family)
            {
                case LayerFamily.PointLabels: return "COGO point labels";
                case LayerFamily.Symbols: return "Point and feature symbols";
                case LayerFamily.LineworkLabels: return "Linework labels";
                case LayerFamily.Trees: return "Trees (labels, tags, symbols, driplines)";
                case LayerFamily.UtilityDips: return "Utility dips and inverts";
                case LayerFamily.Leaders: return "Leaders and multileaders";
                case LayerFamily.SpotElevations: return "Spot elevations";
                case LayerFamily.Contours: return "Contours";
                case LayerFamily.PavementMarkings: return "Pavement markings";
                case LayerFamily.SurveyControl: return "Survey control";
                case LayerFamily.TablesAndLegends: return "Tables and legends";
                case LayerFamily.EasementAndBoundary: return "Easement and boundary";
                default: return "Other FTF objects";
            }
        }

        private static bool Says(CodeRule c, params string[] words)
        {
            var text = ((c.Id ?? string.Empty) + " " + (c.Description ?? string.Empty) + " " + (c.Category ?? string.Empty)).ToUpperInvariant();
            return words.Any(w => text.IndexOf(w, StringComparison.Ordinal) >= 0);
        }

        /// <summary>The family of a code rule's label and symbol.</summary>
        public static LayerFamily FamilyOf(CodeRule c)
        {
            if (c == null) return LayerFamily.PointLabels;
            if ((c.Species != null && c.Species.Count > 0) || c.DripLine != null || Says(c, "TREE")) return LayerFamily.Trees;
            if (Says(c, "CONTROL", "MONUMENT", "BENCHMARK")) return LayerFamily.SurveyControl;
            if (Says(c, "PAVEMENT", "MARKING", "ARROW", "STRIPE", "STOP BAR", "CROSSWALK")) return LayerFamily.PavementMarkings;
            return LayerFamily.PointLabels;
        }

        private static LayerFamily SymbolFamily(RulesConfig rules, string code)
        {
            var bare = FieldCodes.Orient.OrientationStandard.BaseCode(code);
            if ((rules.ControlCodes ?? new List<string>()).Any(c => string.Equals(c, bare, StringComparison.OrdinalIgnoreCase))) return LayerFamily.SurveyControl;
            var o = rules.Orientation;
            if (o != null && (o.Symbols ?? new List<FieldCodes.Orient.SymbolRule>())
                    .Where(s => s != null && (s.Kind == FieldCodes.Orient.OrientKind.PavementArrow || s.Kind == FieldCodes.Orient.OrientKind.PavementWord))
                    .Any(s => (s.Codes ?? new List<string>()).Any(g => FieldCodes.Orient.OrientationStandard.Glob(g, bare))))
                return LayerFamily.PavementMarkings;
            return LayerFamily.Symbols;
        }

        private static readonly string[] PavementTokens = { "CHAN", "STRP", "STRIPE", "MRKG", "PVMT", "PAINT" };

        private static LayerFamily LineFamily(RulesConfig rules, LineFeatureRule lf)
        {
            var tokens = (lf.Layer ?? string.Empty).ToUpperInvariant().Split('-', '_');
            if (tokens.Any(t => PavementTokens.Contains(t))) return LayerFamily.PavementMarkings;
            var o = rules.Orientation;
            var bare = FieldCodes.Orient.OrientationStandard.BaseCode(lf.Code);
            if (o != null && (o.Lines ?? new List<FieldCodes.Orient.LineRoleRule>())
                    .Where(r => r != null && (r.Role == FieldCodes.Orient.LineRole.StopBar || r.Role == FieldCodes.Orient.LineRole.Crosswalk ||
                                              r.Role == FieldCodes.Orient.LineRole.LaneLine))
                    .Any(r => (r.Codes ?? new List<string>()).Any(g => FieldCodes.Orient.OrientationStandard.Glob(g, bare))))
                return LayerFamily.PavementMarkings;
            return LayerFamily.LineworkLabels;
        }

        /// <summary>The family of a settings field, by the page that edits it.</summary>
        private static LayerFamily SettingsFamily(string page, string objectType)
        {
            var type = objectType ?? string.Empty;
            Func<string, bool> has = w => type.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0;
            switch (page ?? string.Empty)
            {
                case "Point Labels": return LayerFamily.PointLabels;
                case "Line Labels": return LayerFamily.LineworkLabels;
                case "Drafting Lines": return has("label") || has("annotation") ? LayerFamily.LineworkLabels : LayerFamily.Other;
                case "Tags": return LayerFamily.Trees;
                case "Schedule": return LayerFamily.TablesAndLegends;
                case "Spot Elevations": return LayerFamily.SpotElevations;
                case "Contours": return LayerFamily.Contours;
                case "Utility Dips": return LayerFamily.UtilityDips;
                case "Strip Easements":
                case "Easement Exhibits":
                case "Recorded Surveys":
                case "Right of Way":
                case "Platted Blocks":
                    if (has("table")) return LayerFamily.TablesAndLegends;
                    if (has("monument")) return LayerFamily.SurveyControl;
                    return LayerFamily.EasementAndBoundary;
                default: return LayerFamily.Other;
            }
        }

        // ------------------------------------------------------------ settings walk

        /// <summary>Settings sections, read by reflection so a new section's layer fields appear
        /// here without anyone remembering to add them: every string property named ...Layer,
        /// including inside lists (drafting line types, dip standards, record entities).</summary>
        private static void Walk(List<LayerUse> uses, object node, string feature, string section, int depth)
        {
            if (node == null || depth > 4) return;
            foreach (var prop in node.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (prop.GetIndexParameters().Length > 0) continue;
                if (prop.GetCustomAttributes(typeof(Newtonsoft.Json.JsonIgnoreAttribute), true).Length > 0) continue;
                object value;
                try { value = prop.GetValue(node, null); } catch (TargetInvocationException) { continue; }
                if (value == null) continue;

                if (prop.PropertyType == typeof(string))
                {
                    if (prop.Name.EndsWith("Layer", StringComparison.Ordinal) && prop.Name != "Layer" || prop.Name == "Layer")
                        Add(uses, feature, Humanize(node.GetType().Name, prop.Name), (string)value, LayerSource.Settings, "Settings > " + section, section, LayerFamily.Other);
                    continue;
                }
                if (prop.PropertyType == typeof(List<string>) || prop.PropertyType.IsPrimitive || prop.PropertyType.IsEnum) continue;

                var list = value as IEnumerable;
                if (list != null && !(value is string))
                {
                    foreach (var item in list)
                    {
                        if (item == null || item is string || item.GetType().IsPrimitive) continue;
                        Walk(uses, item, feature + ": " + NameOf(item), section, depth + 1);
                    }
                    continue;
                }
                if (prop.PropertyType.IsClass && prop.PropertyType.Namespace != null &&
                    prop.PropertyType.Namespace.StartsWith("FieldCodes", StringComparison.Ordinal))
                    Walk(uses, value, feature, section, depth + 1);
            }
        }

        /// <summary>The settings page that edits a section's field (the window's names, which
        /// follow the workflow rather than the JSON sections).</summary>
        public static string PageFor(string sectionTitle, string objectType)
        {
            switch (sectionTitle ?? string.Empty)
            {
                case "Tags & Table": return string.Equals(objectType, "Table", StringComparison.OrdinalIgnoreCase) ? "Schedule" : "Tags";
                case "Labels": return "Point Labels";
                case "Drip Lines": return "Driplines";
                case "Draw Order": return "Drawing Order";
                case "Spot Shots": return "Spot Elevations";
                case "Storm & Sewer Dips": return "Utility Dips";
                case "Cleanup & Re-run": return "Advanced Settings";
                case "Layer Standards": return "Layers";
                default: return sectionTitle;
            }
        }

        internal static string NameOf(object item)
        {
            foreach (var n in new[] { "Name", "Label", "Title", "Code", "Id", "Key", "System", "Kind", "Status" })
            {
                var p = item.GetType().GetProperty(n, BindingFlags.Public | BindingFlags.Instance);
                if (p == null || p.PropertyType == typeof(bool)) continue;
                var v = p.GetValue(item, null);
                if (v != null && !string.IsNullOrWhiteSpace(v.ToString())) return v.ToString();
            }
            return item.GetType().Name;
        }

        /// <summary>What a settings field's layer carries, in words.</summary>
        private static string Humanize(string type, string property)
        {
            switch (type + "." + property)
            {
                case "ContourSettings.Layer": return "Contour lines";
                case "MonumentStandard.Layer": return "Monument symbol";
                case "MonumentStandard.LabelLayer": return "Monument label";
                case "RecordEntityStandard.Layer": return "Linework";
                case "DraftingLineType.Layer": return "Linework";
                case "DraftingLineType.AnnotationLayer": return "Bearing / distance labels";
            }
            return Humanize(property);
        }

        internal static string Humanize(string property)
        {
            if (property == "Layer") return "Linework / symbol";
            var bare = property.EndsWith("Layer", StringComparison.Ordinal) ? property.Substring(0, property.Length - 5) : property;
            var words = Regex.Replace(bare, "(?<=[a-z])(?=[A-Z])", " ").Trim();
            return words.Length == 0 ? "Linework / symbol" : char.ToUpperInvariant(words[0]) + words.Substring(1).ToLowerInvariant();
        }

        private static LayerUse Add(List<LayerUse> uses, string feature, string type, string layer, LayerSource source, string editedIn, string section,
                                    LayerFamily family)
        {
            if (string.IsNullOrWhiteSpace(layer)) return null;
            var use = new LayerUse
            {
                Feature = feature, ObjectType = type, Layer = layer.Trim(), Source = source, EditedIn = editedIn,
                SettingsSection = section, Family = family
            };
            uses.Add(use);
            return use;
        }

        /// <summary>The status of a configured layer against the drawing's layers, after mapping.</summary>
        public static LayerStatus StatusOf(string configured, IEnumerable<string> drawingLayers, IEnumerable<LayerMapping> mappings)
        {
            var r = ProductionLayerResolver.Resolve(configured, drawingLayers, mappings);
            switch (r.Decision)
            {
                case LayerDecision.Existing: return LayerStatus.Exists;
                case LayerDecision.Mapped: return LayerStatus.MappedExists;
                case LayerDecision.SameNameDifferentSpelling: return LayerStatus.SpelledDifferently;
                case LayerDecision.AskAboutSimilar: return LayerStatus.SimilarExists;
                default: return LayerStatus.WillBeCreated;
            }
        }

        public static string Describe(LayerStatus status)
        {
            switch (status)
            {
                case LayerStatus.Exists: return "In drawing";
                case LayerStatus.MappedExists: return "Mapped (in drawing)";
                case LayerStatus.SpelledDifferently: return "Uses same name, other spelling";
                case LayerStatus.SimilarExists: return "Missing; similar layers exist";
                default: return "Missing; created on first use";
            }
        }

        /// <summary>The layer FTF actually draws on for a configured one: its project mapping, or itself.</summary>
        public static string Effective(string configured, LayerMap map)
        {
            if (string.IsNullOrWhiteSpace(configured)) return configured;
            return map == null ? configured.Trim() : map.Apply(configured.Trim()).Trim();
        }

        /// <summary>Every layer FTF draws on, after the project mapping, once each: the layers
        /// FTFLAYERUPDATE may change and nothing else.</summary>
        public static IList<string> EffectiveLayers(IEnumerable<LayerUse> uses, IEnumerable<LayerMapping> mappings)
        {
            var map = new LayerMap(mappings);
            return (uses ?? Enumerable.Empty<LayerUse>())
                .Where(u => u != null && !string.IsNullOrWhiteSpace(u.Layer))
                .Select(u => Effective(u.Layer, map))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(l => l, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    /// <summary>
    /// The project's layer mapping, applied to every layer FTF creates or draws on. One
    /// mapping per FTF layer; the first wins. Mapping is applied once (a mapping's target is
    /// never mapped again), so two rows cannot send a layer round in a loop.
    /// </summary>
    public sealed class LayerMap
    {
        private readonly Dictionary<string, string> _map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public LayerMap(IEnumerable<LayerMapping> mappings)
        {
            foreach (var m in mappings ?? Enumerable.Empty<LayerMapping>())
            {
                if (m == null || string.IsNullOrWhiteSpace(m.From) || string.IsNullOrWhiteSpace(m.To)) continue;
                var from = m.From.Trim();
                if (!_map.ContainsKey(from)) _map[from] = m.To.Trim();
            }
        }

        public int Count { get { return _map.Count; } }

        /// <summary>The layer to use for <paramref name="layer"/>: its mapping, or itself.</summary>
        public string Apply(string layer)
        {
            if (string.IsNullOrWhiteSpace(layer)) return layer;
            string to;
            return _map.TryGetValue(layer.Trim(), out to) ? to : layer;
        }

        /// <summary>Sets or clears one mapping in a settings list, keeping the others and their
        /// order. A target equal to the source, or blank, removes the mapping.</summary>
        public static void Set(IList<LayerMapping> mappings, string from, string to)
        {
            if (mappings == null || string.IsNullOrWhiteSpace(from)) return;
            var spelled = from.Trim();
            for (var i = mappings.Count - 1; i >= 0; i--)
                if (mappings[i] != null && string.Equals((mappings[i].From ?? string.Empty).Trim(), from.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    spelled = mappings[i].From.Trim();     // keep the name as the office wrote it
                    mappings.RemoveAt(i);
                }
            if (string.IsNullOrWhiteSpace(to) || string.Equals(to.Trim(), from.Trim(), StringComparison.OrdinalIgnoreCase)) return;
            mappings.Add(new LayerMapping { From = spelled, To = to.Trim() });
        }

        /// <summary>A layer name AutoCAD accepts: not blank, at most 255 characters, none of &lt;&gt;/\":;?*|,=`.</summary>
        public static bool IsValidName(string name, out string problem)
        {
            problem = null;
            if (string.IsNullOrWhiteSpace(name)) { problem = "a layer name cannot be blank"; return false; }
            if (name.Length > 255) { problem = "a layer name can be at most 255 characters"; return false; }
            if (name.IndexOfAny("<>/\\\":;?*|,=`".ToCharArray()) >= 0) { problem = "a layer name cannot contain < > / \\ \" : ; ? * | , = `"; return false; }
            if (name.Trim() != name) { problem = "a layer name cannot start or end with a space"; return false; }
            return true;
        }
    }
}
