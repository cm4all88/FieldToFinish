using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using FieldCodes.Settings;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FieldCodes.Tests;

/// <summary>
/// "Everything needs to be in Settings", for the rules file (rules.json): every key it holds is
/// changed in the Settings window, or is a seed for a setting that is (and the settings file's own
/// guard, EverySettingIsOnAPageTests, checks that one), or is not a setting at all. A key added to
/// RulesConfig without a page fails here, naming the key.
///
/// The keys come from RulesConfig itself (every [JsonProperty]) and, inside each edited section,
/// from the section's classes (their [JsonProperty]s, list items included). For each edited key:
///
/// - the page named for it is registered and shown in the Settings window;
/// - that page (or a window it opens) calls the RuleDocument methods named for it, and those
///   methods exist;
/// - the editing layer (src/FieldCodes/Editing) names the key and every key inside it, as JSON
///   text: what it does not name, it cannot read or write. The drafting standard is the one section
///   edited as typed objects (written whole); there the page and its window must use every
///   property instead.
///
/// This is a tripwire, not the proof that each page round-trips its values: the page's own
/// editing tests (BenchSettingsEditingTests, FieldCodeListsEditingTests, ...) are that.
/// </summary>
public sealed class EveryRuleIsOnAPageTests
{
    /// <summary>A rules-file key and where the Settings window edits it.</summary>
    internal sealed record OnAPage(string Page, string[] Writes, string[]? Opens = null, bool Typed = false);

    internal static readonly Dictionary<string, OnAPage> Edited = new(StringComparer.Ordinal)
    {
        ["ignoreCodes"] = new("Field Code Lists", new[] { "ApplyCodeLists" }),
        ["lineworkCodes"] = new("Field Code Lists", new[] { "ApplyCodeLists" }),
        ["lineworkCommands"] = new("Field Code Lists", new[] { "ApplyCodeLists" }),
        ["symbolLabels"] = new("Field Code Lists", new[] { "ApplyCodeLists" }),
        ["controlCodes"] = new("Field Code Lists", new[] { "ApplyCodeLists" }),
        ["modifiers"] = new("Field Code Lists", new[] { "ApplyCodeLists" }),
        ["lineFeatures"] = new("Line Labels", new[] { "ApplyLineFeature" }),
        ["spanMarkers"] = new("Line Labels", new[] { "ApplyLineLabelRules" }),
        ["stairsLabelFormat"] = new("Line Labels", new[] { "ApplyLineLabelRules" }),
        ["ignoreLineNames"] = new("Line Labels", new[] { "ApplyLineLabelRules" }),
        ["codes"] = new("Point Features", new[] { "ApplyEdit", "DeleteRule" }),
        ["featureStandard"] = new("Point Features", new[] { "SetFeatureStandard" }, Opens: new[] { "FeatureStandardForm" }, Typed: true),
        ["orientation"] = new("Symbols and Rotations", new[] { "SetSymbols", "ApplyOrientationApply", "ApplyOrientationDetails" }),
        ["bench"] = new("Benches", new[] { "ApplyBench" }),
        ["wetlandBuffer"] = new("Wetland Buffers", new[] { "ApplyWetlandBuffer" }),
    };

    /// <summary>Keys that only seed ftf-settings.json the first time (FtfSettings.MigrateFrom):
    /// after that the settings file wins, so the setting is changed there, on its page. Each rules
    /// key (or key inside a section) with the setting that takes it over.</summary>
    internal static readonly Dictionary<string, string> SeedOnly = new(StringComparer.Ordinal)
    {
        ["unitsPerFoot"] = "general.unitsPerFoot",
        ["trunkDecimals"] = "trees.trunkDecimals",
        ["multiStemAverage"] = "trees.multiStemAverage",
        ["protectedLayers"] = "drawOrder.protectedLayers",
        ["maskableLayers"] = "drawOrder.maskableLayers",
        ["labelPlacement.textHeightPlotted"] = "labels.textHeightPlotted",
        ["labelPlacement.paddingPlotted"] = "labels.paddingPlotted",
        ["labelPlacement.baseOffsetPlotted"] = "labels.baseOffsetPlotted",
        ["labelPlacement.ringStepPlotted"] = "labels.ringStepPlotted",
        ["labelPlacement.ringCount"] = "labels.ringCount",
        ["labelPlacement.movedTolerance"] = "cleanup.movedTolerance",
    };

    /// <summary>Keys that are not settings, each with the reason.</summary>
    internal static readonly Dictionary<string, string> NotASetting = new(StringComparer.Ordinal)
    {
        ["version"] = "The rules file's format version: FTF reads it to know how to read the file; nobody chooses it.",
    };

    /// <summary>Items JSON reaches that reflection does not see: each ignored code may be written as
    /// an object with its reason (NotedCodeListConverter).</summary>
    private static readonly Dictionary<string, Type> HiddenItems = new(StringComparer.Ordinal)
    {
        ["ignoreCodes"] = typeof(CodeNote),
    };

    [Fact]
    public void EveryRulesFileKeyIsEditedInSettingsOrSaysWhy()
    {
        var keys = TopKeys();
        Assert.True(keys.Count >= 20, "Only " + keys.Count + " rules keys were found; the reflection walk has stopped working.");
        var seedSections = SeedOnly.Keys.Select(k => k.Split('.')[0]).ToHashSet(StringComparer.Ordinal);

        var problems = new List<string>();
        foreach (var key in keys)
        {
            var places = (Edited.ContainsKey(key) ? 1 : 0) + (seedSections.Contains(key) ? 1 : 0) + (NotASetting.ContainsKey(key) ? 1 : 0);
            if (places == 0)
                problems.Add("rules.json \"" + key + "\" has no place in the Settings window. Edit it on a page (and list it in Edited), " +
                             "or say why not (SeedOnly, NotASetting).");
            else if (places > 1)
                problems.Add("rules.json \"" + key + "\" is listed more than once (Edited, SeedOnly, NotASetting).");
        }
        foreach (var stale in Edited.Keys.Concat(seedSections).Concat(NotASetting.Keys).Where(k => !keys.Contains(k)).Distinct())
            problems.Add("\"" + stale + "\" is listed here but RulesConfig has no such key any more.");
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void EachEditedKeyIsWrittenByThePageThatShowsIt()
    {
        var registrations = CadSource.Registrations();
        var problems = new List<string>();
        foreach (var (key, place) in Edited)
        {
            var page = registrations.FirstOrDefault(r => string.Equals(r.Label, place.Page, StringComparison.Ordinal));
            if (page == null) { problems.Add(key + ": no Settings page is called \"" + place.Page + "\"."); continue; }
            if (page.Unlisted != null) { problems.Add(key + ": " + place.Page + " is not shown in the Settings window (" + page.Unlisted + ")."); continue; }

            var source = ClassSource(page.PageName);
            if (source.Length == 0) { problems.Add(key + ": the source of " + page.PageName + " (" + place.Page + ") was not found."); continue; }
            foreach (var window in place.Opens ?? Array.Empty<string>())
            {
                if (!Regex.IsMatch(source, @"\bnew\s+" + window + @"\s*\("))
                    problems.Add(key + ": " + place.Page + " does not open " + window + ".");
                source += "\n" + ClassSource(window);
            }
            foreach (var method in place.Writes)
            {
                if (typeof(FieldCodes.Editing.RuleDocument).GetMethod(method, BindingFlags.Public | BindingFlags.Instance) == null)
                    problems.Add(key + ": RuleDocument has no " + method + ".");
                else if (!Regex.IsMatch(source, @"\.\s*" + method + @"\s*\("))
                    problems.Add(key + ": " + place.Page + " never calls " + method + ", so what it shows is not saved.");
            }
        }
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void EveryKeyInsideAnEditedSectionIsHandledByItsEditor()
    {
        var editing = EditingSource();
        var problems = new List<string>();
        foreach (var (key, place) in Edited)
        {
            if (place.Typed)
            {
                var registration = CadSource.Registrations().First(r => r.Label == place.Page);
                var source = PageScan.Strip(ClassSource(registration.PageName) + "\n" +
                                            string.Join("\n", (place.Opens ?? Array.Empty<string>()).Select(ClassSource)));
                problems.AddRange(UnusedProperties(key, source));
            }
            else
                problems.AddRange(Unnamed(key, editing));
        }
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void EachSeedOnlyKeyIsCarriedToASettingOnAPage()
    {
        var settings = SettingTree.All().Select(s => s.Path).ToHashSet(StringComparer.Ordinal);
        var problems = new List<string>();

        // Every key of a seed-only section has its successor.
        foreach (var section in SeedOnly.Keys.Where(k => k.Contains('.')).Select(k => k.Split('.')[0]).Distinct())
            foreach (var inner in Fields(section).Where(f => f.Path != section))
                if (!SeedOnly.ContainsKey(inner.Path)) problems.Add(inner.Path + " is in a seed-only section but no setting takes it over.");

        foreach (var (rules, setting) in SeedOnly)
        {
            if (!settings.Contains(setting)) problems.Add(rules + ": ftf-settings.json has no " + setting + ".");
            else if (EverySettingIsOnAPageTests.NotOnAPage.ContainsKey(setting)) problems.Add(rules + ": " + setting + " is not on a Settings page.");
        }

        // FtfSettings.MigrateFrom really carries each value over: distinct values in, the same out.
        var seed = new RulesConfig
        {
            UnitsPerFoot = 0.3048, TrunkDecimals = 2, MultiStemAverage = StemAverageMethod.Quadratic,
            ProtectedLayers = new List<string> { "SEED-PROTECTED-*" }, MaskableLayers = new List<string> { "SEED-MASKABLE-*" },
            LabelPlacement = new LabelPlacementRule
            {
                TextHeightPlotted = 0.123, PaddingPlotted = 0.031, BaseOffsetPlotted = 0.047, RingStepPlotted = 0.061, RingCount = 7, MovedTolerance = 0.019
            }
        };
        var migrated = new FtfSettings();
        migrated.MigrateFrom(seed);
        var from = JObject.FromObject(seed);
        var to = JObject.FromObject(migrated);
        foreach (var (rules, setting) in SeedOnly)
        {
            var a = from.SelectToken(rules);
            var b = to.SelectToken(setting);
            if (a == null || b == null || !JToken.DeepEquals(a, b))
                problems.Add(rules + " is not carried to " + setting + " (" + a + " became " + (b?.ToString() ?? "nothing") + ").");
        }

        // The shipped file says which keys are only seeds.
        var shipped = JObject.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "rules.json")));
        var note = (string?)shipped["//SETTINGS-MOVED"] ?? string.Empty;
        foreach (var key in SeedOnly.Keys.Select(k => k.Split('.')[0]).Distinct())
            if (!Regex.IsMatch(note, @"\b" + key + @"\b")) problems.Add("rules.json's //SETTINGS-MOVED note does not name " + key + ".");

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    // ------------------------------------------------------------------ the checks catch gaps

    [Fact]
    public void TheWalkFindsTheKeysInsideASection()
    {
        Assert.Equal(new[] { "bench", "bench.codes", "bench.labelFormat", "bench.labelLayer", "bench.layer" },
                     Fields("bench").Select(f => f.Path).OrderBy(p => p, StringComparer.Ordinal));
        Assert.Contains(Fields("codes"), f => f.Path == "codes[].label.layer");
        Assert.Contains(Fields("ignoreCodes"), f => f.Path == "ignoreCodes[].reason");
        Assert.True(Edited.Keys.Sum(k => Fields(k).Count) > 150, "The walk inside the edited sections found too little.");
    }

    [Fact]
    public void AKeyTheEditorDoesNotNameIsReported()
    {
        const string editor = "o[\"layer\"] = x; o[\"labelLayer\"] = y; o[\"labelFormat\"] = z; // \"codes\" is mentioned only here";
        var problems = Unnamed("bench", Comments(editor)).ToList();
        Assert.Contains(problems, p => p.Contains("bench.codes"));
        Assert.Contains(problems, p => p.Contains("\"bench\""));
        Assert.DoesNotContain(problems, p => p.Contains("bench.layer"));
    }

    [Fact]
    public void APropertyTheTypedEditorDoesNotUseIsReported()
    {
        var problems = UnusedProperties("featureStandard", PageScan.Strip("var r = x.Requirements; // .SignCategories\nvar s = \".Name\";")).ToList();
        Assert.Contains(problems, p => p.Contains("featureStandard.signCategories"));
        Assert.Contains(problems, p => p.Contains("featureStandard.requirements[].name"));
        Assert.DoesNotContain(problems, p => p.EndsWith("featureStandard.requirements: never used by its editor."));
    }

    // ------------------------------------------------------------------ helpers

    internal sealed record Field(string Path, PropertyInfo Property);

    private static IEnumerable<PropertyInfo> JsonProperties(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetCustomAttribute<JsonPropertyAttribute>() != null && p.GetCustomAttribute<JsonIgnoreAttribute>() == null);

    private static string JsonName(PropertyInfo p) => p.GetCustomAttribute<JsonPropertyAttribute>()!.PropertyName!;

    private static HashSet<string> TopKeys() => JsonProperties(typeof(RulesConfig)).Select(JsonName).ToHashSet(StringComparer.Ordinal);

    /// <summary>The key itself and every key inside it, at any depth (list items as "[]").</summary>
    internal static IList<Field> Fields(string key)
    {
        var top = JsonProperties(typeof(RulesConfig)).Single(p => JsonName(p) == key);
        var fields = new List<Field> { new(key, top) };
        var item = HiddenItems.TryGetValue(key, out var hidden) ? hidden : ItemType(top.PropertyType);
        if (item != null && IsRulesObject(item)) Walk(item, key + "[]", fields, new HashSet<Type>());
        else if (IsRulesObject(top.PropertyType)) Walk(top.PropertyType, key, fields, new HashSet<Type>());
        return fields;
    }

    private static void Walk(Type type, string path, List<Field> fields, HashSet<Type> onPath)
    {
        if (!onPath.Add(type)) return;
        foreach (var p in JsonProperties(type))
        {
            var here = path + "." + JsonName(p);
            fields.Add(new Field(here, p));
            var item = ItemType(p.PropertyType);
            if (item != null && IsRulesObject(item)) Walk(item, here + "[]", fields, onPath);
            else if (IsRulesObject(p.PropertyType)) Walk(p.PropertyType, here, fields, onPath);
        }
        onPath.Remove(type);
    }

    private static bool IsRulesObject(Type type) =>
        type.IsClass && type != typeof(string) && type.Namespace != null && type.Namespace.StartsWith("FieldCodes", StringComparison.Ordinal) &&
        !typeof(System.Collections.IEnumerable).IsAssignableFrom(type);

    private static Type? ItemType(Type type)
    {
        if (type.IsArray) return type.GetElementType();
        if (!typeof(System.Collections.IEnumerable).IsAssignableFrom(type) || type == typeof(string)) return null;
        var list = type.GetInterfaces().Concat(new[] { type })
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
        if (list == null) return null;
        var item = list.GetGenericArguments()[0];
        return item.IsGenericType && item.GetGenericTypeDefinition() == typeof(KeyValuePair<,>) ? null : item;
    }

    /// <summary>Keys of the section the editing source never names as JSON text.</summary>
    private static IEnumerable<string> Unnamed(string key, string editing)
    {
        foreach (var f in Fields(key))
            if (editing.IndexOf("\"" + JsonName(f.Property) + "\"", StringComparison.Ordinal) < 0)
                yield return (f.Path == key ? "\"" + key + "\"" : f.Path) + ": the editing layer (src/FieldCodes/Editing) never names it, " +
                             "so the Settings window cannot change it.";
    }

    /// <summary>Properties of a section edited as typed objects that its page and windows never use.</summary>
    private static IEnumerable<string> UnusedProperties(string key, string strippedSource)
    {
        foreach (var f in Fields(key).Where(f => f.Path != key))
            if (!Regex.IsMatch(strippedSource, @"\.\s*" + f.Property.Name + @"\b"))
                yield return f.Path + ": never used by its editor.";
    }

    private static string EditingSource()
    {
        var dir = Path.Combine(CadSource.Repo(), "src", "FieldCodes", "Editing");
        var files = Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal).ToList();
        Assert.NotEmpty(files);
        return string.Join("\n", files.Select(f => Comments(File.ReadAllText(f))));
    }

    /// <summary>Every file of the plugin that declares this class (all its partial parts).</summary>
    private static string ClassSource(string name)
    {
        var cad = Path.Combine(CadSource.Repo(), "src", "FieldCodes.Cad");
        var declares = new Regex(@"\bclass\s+" + Regex.Escape(name) + @"\b");
        var parts = Directory.EnumerateFiles(cad, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Replace('\\', '/').Contains("/obj/") && !f.Replace('\\', '/').Contains("/bin/"))
            .OrderBy(f => f, StringComparer.Ordinal)
            .Select(File.ReadAllText)
            .Where(t => declares.IsMatch(PageScan.Strip(t)));
        return string.Join("\n", parts);
    }

    /// <summary>The code with its comments removed and its strings kept.</summary>
    internal static string Comments(string code)
    {
        var sb = new StringBuilder(code.Length);
        for (var i = 0; i < code.Length; i++)
        {
            var c = code[i];
            if (c == '/' && i + 1 < code.Length && code[i + 1] == '/')
            {
                while (i < code.Length && code[i] != '\n') i++;
                sb.Append('\n');
                continue;
            }
            if (c == '/' && i + 1 < code.Length && code[i + 1] == '*')
            {
                var end = code.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = end < 0 ? code.Length : end + 1;
                sb.Append(' ');
                continue;
            }
            if (c == '"' || c == '\'')
            {
                var verbatim = c == '"' && i > 0 && code[i - 1] == '@';
                sb.Append(c);
                for (i++; i < code.Length; i++)
                {
                    sb.Append(code[i]);
                    if (!verbatim && code[i] == '\\' && i + 1 < code.Length) { sb.Append(code[++i]); continue; }
                    if (code[i] == c)
                    {
                        if (verbatim && i + 1 < code.Length && code[i + 1] == '"') { sb.Append(code[++i]); continue; }
                        break;
                    }
                    if (!verbatim && code[i] == '\n') break;
                }
                continue;
            }
            sb.Append(c);
        }
        return sb.ToString();
    }
}
