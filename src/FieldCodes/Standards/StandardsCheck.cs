using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using FieldCodes.Settings;

namespace FieldCodes.Standards
{
    /// <summary>What a checked name is.</summary>
    public enum StandardsKind
    {
        Layer = 0,
        LayerStandard = 1,
        Linetype = 2,
        TextStyle = 3,
        MultileaderStyle = 4,
        TableStyle = 5,
        DimensionStyle = 6,
        LineLabelStyle = 7,
        CurveLabelStyle = 8,
        Block = 9
    }

    /// <summary>The fix a check result offers. None is ever applied without the user choosing it.</summary>
    public enum StandardsFix
    {
        /// <summary>Nothing FTF can do from here; the choices say what to do.</summary>
        None = 0,
        /// <summary>Create the layer now, with its layer standard.</summary>
        CreateLayer = 1,
        /// <summary>Send the FTF layer to one of <see cref="StandardsIssue.Candidates"/> (a project mapping).</summary>
        MapLayer = 2,
        /// <summary>Load the linetype from acad.lin.</summary>
        LoadLinetype = 3,
        /// <summary>Write one of <see cref="StandardsIssue.Candidates"/> (what the drawing has) to the setting.</summary>
        ChooseExisting = 4
    }

    /// <summary>The names a drawing has, by kind. A null list was not read, and that kind is not checked.</summary>
    public sealed class DrawingNames
    {
        public IList<string> Layers { get; set; }
        public IList<string> Linetypes { get; set; }
        public IList<string> TextStyles { get; set; }
        public IList<string> MultileaderStyles { get; set; }
        public IList<string> TableStyles { get; set; }
        public IList<string> DimensionStyles { get; set; }
        public IList<string> LineLabelStyles { get; set; }
        public IList<string> CurveLabelStyles { get; set; }
        public IList<string> Blocks { get; set; }

        /// <summary>The linetypes acad.lin defines; null when the file could not be read.</summary>
        public IList<string> LoadableLinetypes { get; set; }
    }

    /// <summary>
    /// One or more settings fields that name the same thing, and how to write a new value to all
    /// of them -- the "choose an existing style" fix. Writes into the settings object that was
    /// checked; saving it is the caller's business.
    /// </summary>
    public sealed class SettingReference
    {
        private readonly List<KeyValuePair<object, PropertyInfo>> _targets = new List<KeyValuePair<object, PropertyInfo>>();
        private readonly List<string> _fields = new List<string>();

        /// <summary>Where each field is edited: "Settings > Point Labels: Text style".</summary>
        public IList<string> Fields { get { return _fields; } }

        /// <summary>The settings page of the first field ("Point Labels").</summary>
        public string Page { get; internal set; }

        internal void Add(object owner, PropertyInfo property, string field)
        {
            _targets.Add(new KeyValuePair<object, PropertyInfo>(owner, property));
            if (!_fields.Contains(field)) _fields.Add(field);
        }

        internal void Merge(SettingReference other)
        {
            foreach (var t in other._targets) _targets.Add(t);
            foreach (var f in other._fields) if (!_fields.Contains(f)) _fields.Add(f);
        }

        /// <summary>Writes the value to every field this reference covers.</summary>
        public void Write(string value)
        {
            foreach (var t in _targets) t.Value.SetValue(t.Key, value, null);
        }
    }

    /// <summary>One thing the settings or rules name that this drawing does not have, or that is wrong.</summary>
    public sealed class StandardsIssue
    {
        public StandardsIssue()
        {
            Choices = new List<string>();
            Candidates = new List<string>();
        }

        public Severity Severity { get; set; }
        public StandardsKind Kind { get; set; }
        /// <summary>Where it is referenced, for people: "Settings > Point Labels: Text style".</summary>
        public string What { get; set; }
        /// <summary>The name that is missing or wrong.</summary>
        public string Name { get; set; }
        public string Problem { get; set; }
        /// <summary>What the user can do, in words. Offered, never applied without being chosen.</summary>
        public IList<string> Choices { get; private set; }
        public StandardsFix Fix { get; set; }
        /// <summary>For MapLayer and ChooseExisting: what the drawing has, likeliest first.</summary>
        public IList<string> Candidates { get; private set; }
        /// <summary>For ChooseExisting: the fields the chosen name is written to.</summary>
        public SettingReference Setting { get; set; }
        /// <summary>For a layer: the FTF layer as configured, before the project mapping (what a mapping maps).</summary>
        public string ConfiguredLayer { get; set; }

        public static string KindName(StandardsKind kind)
        {
            switch (kind)
            {
                case StandardsKind.Layer: return "Layer";
                case StandardsKind.LayerStandard: return "Layer standard";
                case StandardsKind.Linetype: return "Linetype";
                case StandardsKind.TextStyle: return "Text style";
                case StandardsKind.MultileaderStyle: return "Multileader style";
                case StandardsKind.TableStyle: return "Table style";
                case StandardsKind.DimensionStyle: return "Dimension style";
                case StandardsKind.LineLabelStyle: return "Civil 3D line label style";
                case StandardsKind.CurveLabelStyle: return "Civil 3D curve label style";
                default: return "Block";
            }
        }

        public override string ToString()
        {
            return Severity + ": " + KindName(Kind) + " " + Name + " -- " + Problem;
        }
    }

    /// <summary>
    /// "Check Standards": everything the settings and rules name, checked against what the drawing
    /// has -- layers, linetypes, text, multileader, table and dimension styles, Civil 3D label
    /// styles and blocks -- and the layer standards themselves. Nothing is changed here: each
    /// result says what is wrong and offers choices, and the caller applies one only when the user
    /// picks it. No Autodesk types.
    /// </summary>
    public static class StandardsCheck
    {
        public static IList<StandardsIssue> Run(RulesConfig rules, FtfSettings settings, DrawingNames drawing)
        {
            var issues = new List<StandardsIssue>();
            if (drawing == null) drawing = new DrawingNames();
            if (settings != null) settings.FillMissingSections();

            CheckLayers(issues, rules, settings, drawing);
            CheckLayerStandards(issues, rules, settings, drawing);
            if (settings != null) CheckSettingNames(issues, settings, drawing);
            CheckRuleBlocks(issues, rules, drawing);

            return issues.OrderByDescending(i => i.Severity)
                         .ThenBy(i => i.Kind)
                         .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                         .ToList();
        }

        /// <summary>"1 error, 2 warnings, 5 notes" -- or "Everything the settings name is in this drawing."</summary>
        public static string Summary(ICollection<StandardsIssue> issues)
        {
            if (issues == null || issues.Count == 0) return "Everything the settings and rules name is in this drawing.";
            var errors = issues.Count(i => i.Severity == Severity.Error);
            var warnings = issues.Count(i => i.Severity == Severity.Warning);
            var notes = issues.Count(i => i.Severity == Severity.Info);
            return string.Format(CultureInfo.InvariantCulture, "{0} error(s), {1} warning(s), {2} note(s).", errors, warnings, notes);
        }

        // ------------------------------------------------------------ layers

        private static void CheckLayers(List<StandardsIssue> issues, RulesConfig rules, FtfSettings settings, DrawingNames drawing)
        {
            if (drawing.Layers == null) return;
            var mappings = settings != null ? settings.General.LayerMappings : null;
            var map = new LayerMap(mappings);
            var standards = settings != null ? settings.LayerStandards : null;
            // A layer used only when the drawing has it, and a modifier's layer (made only for a point that
            // carries the modifier), are not missing in any useful sense.
            var uses = LayerCatalog.Build(rules, settings, drawing.Layers).Where(u => !u.OnlyIfPresent && u.Modifier == null).ToList();

            foreach (var group in uses.GroupBy(u => u.Layer, StringComparer.OrdinalIgnoreCase))
            {
                var configured = group.Key;
                var status = LayerCatalog.StatusOf(configured, drawing.Layers, mappings);
                if (status == LayerStatus.Exists || status == LayerStatus.MappedExists) continue;

                var effective = LayerCatalog.Effective(configured, map);
                var resolution = ProductionLayerResolver.Resolve(configured, drawing.Layers, mappings);
                var features = group.Select(u => u.Feature).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var issue = new StandardsIssue
                {
                    Kind = StandardsKind.Layer,
                    Name = effective,
                    ConfiguredLayer = configured,
                    What = Join(features.Select(f => f + " (" + group.First(u => u.Feature == f).ObjectType + ")"), 2)
                };
                var standard = standards != null && !standards.For(effective).IsEmpty ? " with its layer standard" : " taking the look of its family";
                switch (status)
                {
                    case LayerStatus.SpelledDifferently:
                        issue.Severity = Severity.Warning;
                        issue.Problem = "Not in this drawing; the drawing has " + resolution.Layer + " (the same name spelled differently). " +
                                        "Production tools use it; other commands would create " + effective + ".";
                        issue.Fix = StandardsFix.MapLayer;
                        issue.Candidates.Add(resolution.Layer);
                        issue.Choices.Add("Send it to " + resolution.Layer + " (New Layer)");
                        issue.Choices.Add("Create " + effective);
                        break;
                    case LayerStatus.SimilarExists:
                        issue.Severity = Severity.Warning;
                        issue.Problem = "Not in this drawing; similar layers are: " + Join(resolution.Similar, 4) + ". " +
                                        "Production tools ask before creating it; other commands create it.";
                        issue.Fix = StandardsFix.MapLayer;
                        foreach (var s in resolution.Similar) issue.Candidates.Add(s);
                        issue.Choices.Add("Send it to one of the similar layers (New Layer)");
                        issue.Choices.Add("Create " + effective);
                        break;
                    default:
                        issue.Severity = Severity.Info;
                        issue.Problem = "Not in this drawing; FTF creates it on first use" + standard + ".";
                        issue.Fix = StandardsFix.CreateLayer;
                        foreach (var s in Similar(effective, drawing.Layers).Take(20)) issue.Candidates.Add(s);
                        issue.Choices.Add("Create it now");
                        issue.Choices.Add("Send it to a layer the drawing has (New Layer)");
                        break;
                }
                issues.Add(issue);
            }
        }

        // ------------------------------------------------------------ layer standards

        private static void CheckLayerStandards(List<StandardsIssue> issues, RulesConfig rules, FtfSettings settings, DrawingNames drawing)
        {
            if (settings == null || settings.LayerStandards == null) return;
            var standards = (settings.LayerStandards.Layers ?? new List<LayerStandard>()).Where(s => s != null).ToList();
            if (standards.Count == 0) return;

            var used = LayerCatalog.EffectiveLayers(LayerCatalog.Build(rules, settings, drawing.Layers), settings.General.LayerMappings);
            var linetypeRefs = new Dictionary<string, SettingReference>(StringComparer.OrdinalIgnoreCase);
            var property = typeof(LayerStandard).GetProperty("Linetype");

            foreach (var s in standards)
            {
                var name = (s.Name ?? string.Empty).Trim();
                foreach (var problem in LayerStandards.Problems(s))
                {
                    var issue = new StandardsIssue
                    {
                        Severity = Severity.Error, Kind = StandardsKind.LayerStandard, Name = name.Length == 0 ? "(no name)" : name,
                        What = "Settings > Layers: layer standard", Problem = Capital(problem) + ".", Fix = StandardsFix.None
                    };
                    issue.Choices.Add("Correct it on Settings > Layers");
                    issues.Add(issue);
                }

                if (name.Length > 0 && !used.Any(l => LayerStandards.Matches(name, l)))
                {
                    var issue = new StandardsIssue
                    {
                        Severity = Severity.Info, Kind = StandardsKind.LayerStandard, Name = name, What = "Settings > Layers: layer standard",
                        Problem = s.IsPattern
                            ? "Matches none of the layers FTF uses; it applies only if FTF later creates a matching layer."
                            : "FTF does not use this layer; the standard applies only if FTF creates it.",
                        Fix = StandardsFix.None
                    };
                    issue.Choices.Add("Check the spelling, or remove the standard");
                    issues.Add(issue);
                }

                string why;
                if (!string.IsNullOrWhiteSpace(s.Linetype) && LayerStandards.IsValidLinetypeName(s.Linetype.Trim(), out why))
                {
                    SettingReference reference;
                    if (!linetypeRefs.TryGetValue(s.Linetype.Trim(), out reference))
                        linetypeRefs[s.Linetype.Trim()] = reference = new SettingReference { Page = "Layers" };
                    reference.Add(s, property, "Settings > Layers: standard for " + name);
                }
            }

            foreach (var pair in linetypeRefs)
                AddLinetype(issues, pair.Key, pair.Value, drawing, "layers with this standard get Continuous until it is loaded");
        }

        private static void AddLinetype(List<StandardsIssue> issues, string linetype, SettingReference reference, DrawingNames drawing, string consequence)
        {
            if (drawing.Linetypes == null || Has(drawing.Linetypes, linetype)) return;
            var issue = new StandardsIssue
            {
                Kind = StandardsKind.Linetype, Name = linetype, What = Join(reference.Fields, 3), Setting = reference
            };
            var loadable = drawing.LoadableLinetypes == null ? (bool?)null : Has(drawing.LoadableLinetypes, linetype);
            if (loadable == true)
            {
                issue.Severity = Severity.Info;
                issue.Problem = "Not in this drawing; acad.lin has it, and FTF loads it when it is needed.";
                issue.Fix = StandardsFix.LoadLinetype;
                issue.Choices.Add("Load it from acad.lin now");
            }
            else if (loadable == null)
            {
                issue.Severity = Severity.Warning;
                issue.Problem = "Not in this drawing; FTF tries acad.lin when it is needed (acad.lin could not be read to check).";
                issue.Fix = StandardsFix.LoadLinetype;
                issue.Choices.Add("Try loading it from acad.lin now");
            }
            else
            {
                issue.Severity = Severity.Error;
                issue.Problem = "Not in this drawing and not in acad.lin: " + consequence + ".";
                issue.Fix = StandardsFix.ChooseExisting;
                foreach (var c in Similar(linetype, drawing.Linetypes)) issue.Candidates.Add(c);
                issue.Choices.Add("Choose a linetype the drawing has");
                issue.Choices.Add("Load it from the office .lin file (LINETYPE command), then check again");
            }
            issues.Add(issue);
        }

        // ------------------------------------------------------------ styles and blocks in settings

        private static void CheckSettingNames(List<StandardsIssue> issues, FtfSettings settings, DrawingNames drawing)
        {
            var found = new Dictionary<string, SettingReference>(StringComparer.OrdinalIgnoreCase);
            var kinds = new Dictionary<string, StandardsKind>(StringComparer.OrdinalIgnoreCase);
            foreach (var section in settings.Sections)
            {
                if (section is LayerStandardsSettings) continue;
                Walk(section, section.Title, null, 0, (kind, name, owner, prop, field) =>
                {
                    // A style assignment names FTF's own text and leader styles, not the drawing's;
                    // the styles section validates those. AutoCAD's own arrowheads (_Dot...) are
                    // loaded on first use, and ByLayer / ByBlock are not linetypes to load.
                    if (owner is FieldCodes.Styles.StyleAssignment) return;
                    if (kind == StandardsKind.Block && FieldCodes.Styles.StyleCheck.IsBuiltInArrow(name)) return;
                    if (kind == StandardsKind.Linetype && FieldCodes.Styles.StyleCheck.IsSpecialLinetype(name) &&
                        !string.Equals(name, "Continuous", StringComparison.OrdinalIgnoreCase)) return;
                    var key = (int)kind + "|" + name;
                    SettingReference reference;
                    if (!found.TryGetValue(key, out reference))
                    {
                        found[key] = reference = new SettingReference { Page = LayerCatalog.PageFor(section.Title, field) };
                        kinds[key] = kind;
                    }
                    reference.Add(owner, prop, field);
                });
            }

            foreach (var pair in found)
            {
                var kind = kinds[pair.Key];
                var name = pair.Key.Substring(pair.Key.IndexOf('|') + 1);
                if (kind == StandardsKind.Linetype)
                {
                    AddLinetype(issues, name, pair.Value, drawing, "the objects these settings draw use their layer's linetype instead");
                    continue;
                }
                var existing = Existing(drawing, kind);
                if (existing == null || Has(existing, name)) continue;
                var issue = new StandardsIssue
                {
                    Severity = Severity.Warning, Kind = kind, Name = name, What = Join(pair.Value.Fields, 3), Setting = pair.Value,
                    Fix = existing.Count > 0 ? StandardsFix.ChooseExisting : StandardsFix.None,
                    Problem = StandardsIssue.KindName(kind) + " \"" + name + "\" is not in this drawing."
                };
                foreach (var c in Similar(name, existing)) issue.Candidates.Add(c);
                if (existing.Count > 0) issue.Choices.Add("Choose a " + StandardsIssue.KindName(kind).ToLowerInvariant() + " the drawing has");
                issue.Choices.Add(kind == StandardsKind.Block
                    ? "Insert the block definition from the office library, then check again"
                    : "Import it from the office template (DesignCenter), then check again");
                issue.Choices.Add("Clear the setting on Settings > " + pair.Value.Page);
                issues.Add(issue);
            }
        }

        private static IList<string> Existing(DrawingNames drawing, StandardsKind kind)
        {
            switch (kind)
            {
                case StandardsKind.TextStyle: return drawing.TextStyles;
                case StandardsKind.MultileaderStyle: return drawing.MultileaderStyles;
                case StandardsKind.TableStyle: return drawing.TableStyles;
                case StandardsKind.DimensionStyle: return drawing.DimensionStyles;
                case StandardsKind.LineLabelStyle: return drawing.LineLabelStyles;
                case StandardsKind.CurveLabelStyle: return drawing.CurveLabelStyles;
                case StandardsKind.Block: return drawing.Blocks;
                case StandardsKind.Linetype: return drawing.Linetypes;
                default: return drawing.Layers;
            }
        }

        /// <summary>What a settings field names, by its property name: ...TextStyle, ...LeaderStyle,
        /// ...TableStyle, ...DimensionStyle, ...LineLabelStyle, ...CurveLabelStyle, ...Linetype, ...Block.</summary>
        public static StandardsKind? KindOf(string property)
        {
            if (string.IsNullOrEmpty(property)) return null;
            Func<string, bool> ends = s => property.EndsWith(s, StringComparison.Ordinal);
            if (ends("TextStyle")) return StandardsKind.TextStyle;
            if (ends("LeaderStyle")) return StandardsKind.MultileaderStyle;
            if (ends("TableStyle")) return StandardsKind.TableStyle;
            if (ends("DimensionStyle") || ends("DimStyle")) return StandardsKind.DimensionStyle;
            if (ends("CurveLabelStyle")) return StandardsKind.CurveLabelStyle;
            if (ends("LineLabelStyle")) return StandardsKind.LineLabelStyle;
            if (ends("Linetype")) return StandardsKind.Linetype;
            if (ends("Block")) return StandardsKind.Block;
            return null;
        }

        private delegate void Found(StandardsKind kind, string name, object owner, PropertyInfo property, string field);

        /// <summary>Every named style, linetype and block a settings section refers to, by reflection,
        /// so a new section's fields are checked without anyone remembering to add them.</summary>
        private static void Walk(object node, string section, string path, int depth, Found found)
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
                    var kind = KindOf(prop.Name);
                    var text = ((string)value).Trim();
                    if (kind == null || !prop.CanWrite || text.Length == 0 || text.IndexOf('{') >= 0) continue;
                    var words = LayerCatalog.Humanize(prop.Name);
                    var page = LayerCatalog.PageFor(section, words);
                    found(kind.Value, text, node, prop, "Settings > " + page + ": " + (path == null ? string.Empty : path + " ") +
                                                        (path == null ? words : words.ToLowerInvariant()));
                    continue;
                }
                if (prop.PropertyType == typeof(List<string>) || prop.PropertyType.IsPrimitive || prop.PropertyType.IsEnum) continue;

                var list = value as IEnumerable;
                if (list != null)
                {
                    foreach (var item in list)
                    {
                        if (item == null || item is string || item.GetType().IsPrimitive) continue;
                        var name = LayerCatalog.NameOf(item);
                        Walk(item, section, path == null ? name : path + " " + name, depth + 1, found);
                    }
                    continue;
                }
                if (prop.PropertyType.IsClass && prop.PropertyType.Namespace != null &&
                    prop.PropertyType.Namespace.StartsWith("FieldCodes", StringComparison.Ordinal))
                    Walk(value, section, path, depth + 1, found);
            }
        }

        // ------------------------------------------------------------ blocks in rules

        private static void CheckRuleBlocks(List<StandardsIssue> issues, RulesConfig rules, DrawingNames drawing)
        {
            if (rules == null || drawing.Blocks == null) return;
            foreach (var c in rules.Codes ?? new List<CodeRule>())
            {
                if (c == null || !c.InsertBlock || string.IsNullOrWhiteSpace(c.Block) || c.Block.IndexOf('{') >= 0) continue;
                if (Has(drawing.Blocks, c.Block.Trim())) continue;
                var issue = new StandardsIssue
                {
                    Severity = Severity.Error, Kind = StandardsKind.Block, Name = c.Block.Trim(),
                    What = "rules.json: code " + c.Id + " block (Settings > Point Features)",
                    Problem = "Not in this drawing: points of this code get no symbol, and FTF reports each one rather than insert another block.",
                    Fix = StandardsFix.None
                };
                foreach (var s in Similar(c.Block, drawing.Blocks)) issue.Candidates.Add(s);
                issue.Choices.Add("Insert the block definition from the office library, then check again");
                issue.Choices.Add("Change the rule's block on Settings > Point Features");
                issues.Add(issue);
            }

            if ((rules.Codes ?? new List<CodeRule>()).Any(c => c != null && c.InsertBlock))
                foreach (var m in rules.Modifiers ?? new List<ModifierRule>())
                {
                    if (m == null || string.IsNullOrWhiteSpace(m.Block) || m.Block.IndexOf('{') >= 0 || Has(drawing.Blocks, m.Block.Trim())) continue;
                    var issue = new StandardsIssue
                    {
                        Severity = Severity.Warning, Kind = StandardsKind.Block, Name = m.Block.Trim(),
                        What = "rules.json: modifier " + m.Id + " block",
                        Problem = "Not in this drawing: a symbol point carrying this modifier gets no symbol.", Fix = StandardsFix.None
                    };
                    issue.Choices.Add("Insert the block definition from the office library, then check again");
                    issues.Add(issue);
                }

            var orientation = rules.Orientation;
            if (orientation == null) return;
            foreach (var s in orientation.Symbols ?? new List<FieldCodes.Orient.SymbolRule>())
            {
                if (s == null || string.IsNullOrWhiteSpace(s.Block) || s.Block.Trim().StartsWith("(", StringComparison.Ordinal)) continue;
                if (drawing.Blocks.Any(b => FieldCodes.Orient.OrientationStandard.Glob(s.Block, b))) continue;
                var issue = new StandardsIssue
                {
                    Severity = Severity.Info, Kind = StandardsKind.Block, Name = s.Block.Trim(),
                    What = "rules.json: orientation symbol for " + Join(s.Codes ?? new List<string>(), 4),
                    Problem = "No block in this drawing matches it. FTFORIENT's reference direction for these codes (" + (s.Meaning ?? "its zero") +
                              ") is the one drawn in that block; a symbol drawn with another block keeps its angle as measured.",
                    Fix = StandardsFix.None
                };
                issue.Choices.Add("Nothing, if the drawing has no such points");
                issue.Choices.Add("Insert the office symbol blocks, or correct the orientation standard in rules.json");
                issues.Add(issue);
            }
        }

        // ------------------------------------------------------------ helpers

        private static bool Has(IEnumerable<string> names, string name)
        {
            return names != null && names.Any(n => string.Equals((n ?? string.Empty).Trim(), (name ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase));
        }

        private static string Join(IEnumerable<string> items, int max)
        {
            var list = (items ?? Enumerable.Empty<string>()).Where(i => !string.IsNullOrWhiteSpace(i)).ToList();
            var shown = string.Join("; ", list.Take(max).ToArray());
            return list.Count > max ? shown + " (+" + (list.Count - max) + " more)" : shown;
        }

        private static string Capital(string text)
        {
            return string.IsNullOrEmpty(text) ? text : char.ToUpperInvariant(text[0]) + text.Substring(1);
        }

        /// <summary>What the drawing has, likeliest replacement first: the same name spelled
        /// differently, then names sharing the most of its start, then the rest alphabetically.</summary>
        public static IList<string> Similar(string name, IEnumerable<string> existing)
        {
            var key = ProductionLayerResolver.Normalize(name);
            return (existing ?? Enumerable.Empty<string>())
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(n => new { n, k = ProductionLayerResolver.Normalize(n) })
                .OrderByDescending(x => x.k == key)
                .ThenByDescending(x => key.Length > 0 && (x.k.Contains(key) || key.Contains(x.k)))
                .ThenByDescending(x => CommonPrefix(x.k, key))
                .ThenBy(x => x.n, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.n)
                .ToList();
        }

        private static int CommonPrefix(string a, string b)
        {
            var n = 0;
            while (n < a.Length && n < b.Length && a[n] == b[n]) n++;
            return n;
        }

        /// <summary>The linetype names a .lin file defines: each "*NAME,description" line.</summary>
        public static IList<string> LinetypesIn(IEnumerable<string> linFileLines)
        {
            var names = new List<string>();
            foreach (var raw in linFileLines ?? Enumerable.Empty<string>())
            {
                var line = (raw ?? string.Empty).Trim();
                if (line.Length < 2 || line[0] != '*') continue;
                var comma = line.IndexOf(',');
                var name = (comma > 0 ? line.Substring(1, comma - 1) : line.Substring(1)).Trim();
                if (name.Length > 0 && !names.Contains(name, StringComparer.OrdinalIgnoreCase)) names.Add(name);
            }
            return names;
        }
    }
}
