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
        public static IList<LayerUse> Build(RulesConfig rules, FtfSettings settings)
        {
            var uses = new List<LayerUse>();
            if (rules != null)
            {
                foreach (var c in rules.Codes ?? new List<CodeRule>())
                {
                    var name = (string.IsNullOrWhiteSpace(c.Description) ? c.Id : c.Description) + " (" + c.Id + ")";
                    if (c.Label != null) Add(uses, name, "Label", c.Label.Layer, LayerSource.Rules, "rules.json: code " + c.Id + " label layer", "Point Features");
                    if (c.DripLine != null) Add(uses, name, "Dripline", c.DripLine.Layer, LayerSource.Rules, "rules.json: code " + c.Id + " dripline layer", "Point Features");
                    if (c.InsertBlock) Add(uses, name, "Inserted symbol", c.BlockLayer, LayerSource.Rules, "rules.json: code " + c.Id + " block layer", "Point Features");
                }
                foreach (var sl in rules.SymbolLabels ?? new List<SymbolLabelRule>())
                    if (sl.Enabled) Add(uses, "Code " + sl.Code, "Label", sl.Layer, LayerSource.Rules, "rules.json: symbolLabels " + sl.Code, "Point Features");
                foreach (var lf in rules.LineFeatures ?? new List<LineFeatureRule>())
                    if (!string.IsNullOrWhiteSpace(lf.Label) && !string.IsNullOrWhiteSpace(lf.LabelLayer))
                        Add(uses, "Line " + (lf.Name ?? lf.Code) + " (" + lf.Code + ")", "Line label", lf.LabelLayer, LayerSource.Rules,
                            "rules.json: lineFeatures " + lf.Code + " labelLayer", "Line Labels");
                if (rules.Bench != null)
                {
                    Add(uses, "Bench", "Outline", rules.Bench.Layer, LayerSource.Rules, "rules.json: bench layer", null);
                    Add(uses, "Bench", "Size label", rules.Bench.LabelLayer, LayerSource.Rules, "rules.json: bench labelLayer", null);
                }
                if (rules.Orientation != null)
                {
                    Add(uses, "Symbol orientation (FTFORIENT)", "QC arrows (not plotted)", rules.Orientation.QcLayer, LayerSource.Rules, "rules.json: orientation qcLayer", null);
                    Add(uses, "Symbol orientation (FTFORIENT)", "Inferred connections (not plotted)", rules.Orientation.QcInferredLayer, LayerSource.Rules, "rules.json: orientation qcInferredLayer", null);
                }
            }

            if (settings != null)
            {
                settings.FillMissingSections();
                foreach (var section in settings.Sections)
                    Walk(uses, section, section.Title, section.Title, 0);
                foreach (var u in uses.Where(x => x.Source == LayerSource.Settings))
                {
                    var page = PageFor(u.SettingsSection, u.ObjectType);
                    if (u.Feature == u.SettingsSection) u.Feature = page;
                    else if (u.Feature.StartsWith(u.SettingsSection + ": ", StringComparison.Ordinal))
                        u.Feature = page + u.Feature.Substring(u.SettingsSection.Length);
                    u.SettingsSection = page;
                    u.EditedIn = "Settings > " + page;
                }
            }

            return uses.Where(u => !string.IsNullOrWhiteSpace(u.Layer))
                       .GroupBy(u => u.Feature + "|" + u.ObjectType + "|" + u.Layer, StringComparer.OrdinalIgnoreCase)
                       .Select(g => g.First())
                       .ToList();
        }

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
                        Add(uses, feature, Humanize(prop.Name), (string)value, LayerSource.Settings, "Settings > " + section, section);
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
                default: return sectionTitle;
            }
        }

        private static string NameOf(object item)
        {
            foreach (var n in new[] { "Name", "Label", "Title", "Code", "Id", "Key", "System", "Kind" })
            {
                var p = item.GetType().GetProperty(n, BindingFlags.Public | BindingFlags.Instance);
                if (p == null) continue;
                var v = p.GetValue(item, null);
                if (v != null && !string.IsNullOrWhiteSpace(v.ToString())) return v.ToString();
            }
            return item.GetType().Name;
        }

        private static string Humanize(string property)
        {
            if (property == "Layer") return "Linework / symbol";
            var bare = property.EndsWith("Layer", StringComparison.Ordinal) ? property.Substring(0, property.Length - 5) : property;
            var words = Regex.Replace(bare, "(?<=[a-z])(?=[A-Z])", " ").Trim();
            return words.Length == 0 ? "Linework / symbol" : char.ToUpperInvariant(words[0]) + words.Substring(1).ToLowerInvariant();
        }

        private static void Add(List<LayerUse> uses, string feature, string type, string layer, LayerSource source, string editedIn, string section)
        {
            if (string.IsNullOrWhiteSpace(layer)) return;
            uses.Add(new LayerUse { Feature = feature, ObjectType = type, Layer = layer.Trim(), Source = source, EditedIn = editedIn, SettingsSection = section });
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
