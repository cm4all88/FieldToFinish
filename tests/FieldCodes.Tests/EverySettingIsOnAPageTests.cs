using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using FieldCodes.Settings;
using Newtonsoft.Json;

namespace FieldCodes.Tests;

/// <summary>
/// "Everything needs to be in Settings": every value in ftf-settings.json that a command reads is
/// shown AND saved by the Settings window, so nobody has to edit the file by hand to change it.
///
/// The settings come from the section classes themselves (every [JsonProperty] of every section
/// on FtfSettings, and of the objects and list items inside them). The pages are read as source
/// text -- the Civil 3D project cannot be loaded here -- from src/FieldCodes.Cad/Setup and the
/// Ui files the Settings window uses. A setting counts as on a page when some page:
///
/// - reads it in LoadFrom or in anything else that is not only reached from SaveTo or
///   RestoreDefaults (a value read in SaveTo just to seed ReadDouble is not shown to anyone), and
/// - writes it in SaveTo or in anything else that is not only reached from LoadFrom or
///   RestoreDefaults (a value reset in RestoreDefaults is not saved from what was typed).
///
/// Only accesses through something that IS that setting's object count: <c>s.Easements.X</c>, a
/// local bound to it (<c>var e = s.Easements;</c>), a parameter, field or local of its type, a
/// loop over its list, or an object initializer of its type. The same property name on another
/// object (a control's <c>.Enabled</c>, another section's <c>.TableStyle</c>) does not count.
/// Comments and the text inside string literals are blanked before anything is read, and a page
/// registered Unlisted (never shown) does not count. The command windows in Ui (the Dip Builder,
/// the easement preview) are not the Settings window, so what they read does not count either.
///
/// What it trusts: an edit made in an event handler or a dialog (to the working copy a page keeps
/// of a list item) counts as saved, because the page's SaveTo then saves that copy -- and the list
/// itself is a setting too, so it must still be written by SaveTo to pass. A page that copies
/// values some way this cannot read (reflection, a JSON round trip) fails here; say what it does
/// in ReadThrough or NotOnAPage.
///
/// When a setting fails: put it on the page that edits its section (the message names the page),
/// or, if it really is edited elsewhere or is not a user setting, add it to NotOnAPage with the
/// reason.
/// </summary>
public sealed class EverySettingIsOnAPageTests
{
    /// <summary>Settings no Settings page has to load and save, each with the reason.</summary>
    internal static readonly Dictionary<string, string> NotOnAPage = new(StringComparer.Ordinal)
    {
        ["drafting.lineTypes[].name"] =
            "A drafting line type's name is what it is: FTFDRAWLINE offers the types by name and Right of Way and Platted " +
            "Blocks name the type they draw with. Drafting Lines edits each type's standard, picked by that name; it does not rename types.",
        ["dips.systems[].system"] =
            "The key of each row: Utility Dips shows one row per utility system (Storm, Sanitary, Culvert, Water, Other) " +
            "and saves each row under its system. There is nothing to choose.",
    };

    /// <summary>Settings a page shows through a member of their object rather than the property
    /// itself (a lookup, or a yes/no view of a keyword); that member counts as reading it. A
    /// member with arguments counts only when called with exactly those.</summary>
    internal static readonly Dictionary<string, string[]> ReadThrough = new(StringComparer.Ordinal)
    {
        ["drafting.lineTypes[].annotation"] = new[] { "AnnotationKind" },
        ["drafting.lineTypes[].bearingFormat"] = new[] { "WantsAzimuthFormat" },
        ["dips.systems"] = new[] { "Standard" },
        ["rightOfWay.dimensionPolicy"] = new[] { "DimensionEveryMonument" },
        ["annotation.categories"] = new[] { "Includes" },
        ["diagnostics.ignoredCodes"] = new[] { "Codes", "NoteFor" },
        ["layerStandards.layers[].linetype"] = new[] { "Text(LayerProperty.Linetype)" },
        ["layerStandards.layers[].transparency"] = new[] { "Text(LayerProperty.Transparency)" },
        ["layerStandards.layers[].description"] = new[] { "Text(LayerProperty.Description)" },
        ["review.required"] = new[] { "IsRequired" },
    };

    /// <summary>The Settings window's source, read once for the tests that use it.</summary>
    private static readonly Lazy<PageScan> Window = new(() => PageScan.Of(PageScan.SettingsWindowSources(),
        CadSource.Registrations().Where(r => !r.IsListed).Select(r => r.PageName)));

    [Fact]
    public void EverySettingIsLoadedAndSavedByASettingsPage()
    {
        var settings = SettingTree.All();
        Assert.True(settings.Count > 400, "Only " + settings.Count + " settings were found; the reflection walk has stopped working.");

        var scan = Window.Value;
        var registrations = CadSource.Registrations();
        var missing = new List<string>();
        foreach (var setting in settings)
        {
            if (NotOnAPage.ContainsKey(setting.Path)) continue;
            var loaded = scan.IsRead(setting, ReadThrough);
            var saved = scan.IsWritten(setting);
            if (loaded && saved) continue;

            var section = setting.Path.Split('.', '[')[0];
            var pages = registrations.Where(r => r.Sections.Contains(section, StringComparer.OrdinalIgnoreCase))
                                     .Select(r => r.Label + " (" + r.PageName + ")").ToList();
            missing.Add(setting.Path + " [" + setting.Owner.Name + "." + setting.Property + "]: " +
                        (!loaded && !saved ? "no page shows or saves it" : !loaded ? "no page shows it (LoadFrom)" : "no page saves it (SaveTo)") +
                        (pages.Count > 0 ? "; its section is edited on " + string.Join(", ", pages) : "; no page edits its section"));
        }

        Assert.True(missing.Count == 0,
            missing.Count + " setting(s) cannot be changed in the Settings window:" + Environment.NewLine +
            string.Join(Environment.NewLine, missing) + Environment.NewLine +
            "Put each on its page (LoadFrom shows it, SaveTo saves it, with a tooltip saying what it does), " +
            "or add it to EverySettingIsOnAPageTests.NotOnAPage with the reason.");
    }

    [Fact]
    public void TheExceptionsNameRealSettingsAndStillNeedToBeExceptions()
    {
        var settings = SettingTree.All().ToDictionary(s => s.Path);
        var scan = Window.Value;
        var stale = new List<string>();
        foreach (var pair in NotOnAPage)
        {
            Assert.False(string.IsNullOrWhiteSpace(pair.Value), pair.Key + " is excused without a reason.");
            if (!settings.TryGetValue(pair.Key, out var setting)) { stale.Add(pair.Key + " is not a setting (renamed or removed?)"); continue; }
            if (scan.IsRead(setting, ReadThrough) && scan.IsWritten(setting))
                stale.Add(pair.Key + " is on a page now; take it out of NotOnAPage");
        }
        foreach (var pair in ReadThrough)
        {
            if (!settings.TryGetValue(pair.Key, out var setting)) { stale.Add(pair.Key + " (ReadThrough) is not a setting"); continue; }
            foreach (var member in pair.Value)
                if (setting.Holder.GetMember(member.Split('(')[0], BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static).Length == 0)
                    stale.Add(pair.Key + " (ReadThrough): " + setting.Holder.Name + " has no member " + member);
        }
        Assert.True(stale.Count == 0, string.Join(Environment.NewLine, stale));
    }

    // ------------------------------------------------------------------ the scan itself

    [Fact]
    public void TheScanFindsWhatTodaysPagesDo()
    {
        var all = SettingTree.All().ToDictionary(s => s.Path);
        var scan = Window.Value;

        // var e = s.Easements; ... e.BoundaryLayer = ... (Strip Easements).
        Assert.True(scan.IsRead(all["easements.boundaryLayer"], ReadThrough));
        Assert.True(scan.IsWritten(all["easements.boundaryLayer"]));
        // s.Sheets.MarginIn directly on the settings object (Sheets).
        Assert.True(scan.IsRead(all["sheets.marginIn"], ReadThrough));
        Assert.True(scan.IsWritten(all["sheets.marginIn"]));
        // A list item shown by a method with a typed parameter, and written in a helper SaveTo calls
        // (Drafting Lines: ShowType(DraftingLineType type), CommitCurrent()).
        Assert.True(scan.IsRead(all["drafting.lineTypes[].featureText"], ReadThrough));
        Assert.True(scan.IsWritten(all["drafting.lineTypes[].featureText"]));
        // Written by an object initializer (Utility Dips: new UtilitySystemStandard { PipeLayer = ... }).
        Assert.True(scan.IsWritten(all["dips.systems[].pipeLayer"]));
    }

    [Fact]
    public void ASettingOnlyReadToSeedSaveToIsNotShown()
    {
        var scan = PageScan.Of(new[] { ("Fake.cs", @"
            internal sealed class FakePage : SetupPage
            {
                public override void LoadFrom(FtfSettings s) { _a.Text = Fmt(s.Sheets.MarginIn); }
                public override void SaveTo(FtfSettings s, ICollection<string> problems)
                {
                    var v = s.Sheets.KeymapWidthIn; ReadDouble(_k, ""Sheets: key map"", x => x > 0, ""must be"", problems, ref v); s.Sheets.KeymapWidthIn = v;
                    Write(s);
                }
                private void Write(FtfSettings s) { s.Sheets.MarginIn = 1; }
                public override void RestoreDefaults(FtfSettings s) { s.Sheets.OverlapPercent = 5; LoadFrom(s); }
            }") });
        var all = SettingTree.All().ToDictionary(x => x.Path);

        Assert.True(scan.IsRead(all["sheets.marginIn"], ReadThrough));
        Assert.True(scan.IsWritten(all["sheets.marginIn"]));          // through a helper SaveTo calls
        Assert.False(scan.IsRead(all["sheets.keymapWidthIn"], ReadThrough));   // read only in SaveTo
        Assert.True(scan.IsWritten(all["sheets.keymapWidthIn"]));
        Assert.False(scan.IsWritten(all["sheets.overlapPercent"]));    // only reset by RestoreDefaults
    }

    [Fact]
    public void TheSameNameOnAnotherObjectDoesNotCount()
    {
        var scan = PageScan.Of(new[] { ("Fake.cs", @"
            internal sealed class FakePage : SetupPage
            {
                private CheckBox _enabled;
                public override void LoadFrom(FtfSettings s)
                {
                    _enabled.Enabled = s.Exhibits.TableStyle.Length > 0;   // a control, then another section
                    var shown = ""s.Easements.TableStyle"";                // text, not code
                    // s.Easements.TableColor = ""1"";
                }
                public override void SaveTo(FtfSettings s, ICollection<string> problems) { s.Exhibits.TableStyle = _x.Text; _enabled.Enabled = true; }
                public override void RestoreDefaults(FtfSettings s) { }
            }") });
        var all = SettingTree.All().ToDictionary(x => x.Path);

        Assert.True(scan.IsRead(all["exhibits.tableStyle"], ReadThrough));
        Assert.True(scan.IsWritten(all["exhibits.tableStyle"]));
        Assert.False(scan.IsRead(all["easements.tableStyle"], ReadThrough));
        Assert.False(scan.IsWritten(all["easements.tableColor"]));
        Assert.False(scan.IsRead(all["lineLabels.enabled"], ReadThrough));
        Assert.False(scan.IsWritten(all["lineLabels.enabled"]));
    }

    [Fact]
    public void APageTheWindowNeverShowsDoesNotCount()
    {
        const string page = @"
            internal sealed class HiddenPage : SetupPage
            {
                public override void LoadFrom(FtfSettings s) { _a.Text = Fmt(s.Sheets.MarginIn); }
                public override void SaveTo(FtfSettings s, ICollection<string> problems) { s.Sheets.MarginIn = 1; }
                public override void RestoreDefaults(FtfSettings s) { }
            }";
        var margin = SettingTree.All().Single(x => x.Path == "sheets.marginIn");

        Assert.True(PageScan.Of(new[] { ("Hidden.cs", page) }).IsRead(margin, ReadThrough));
        var unlisted = PageScan.Of(new[] { ("Hidden.cs", page) }, new[] { "HiddenPage" });
        Assert.False(unlisted.IsRead(margin, ReadThrough));
        Assert.False(unlisted.IsWritten(margin));
    }

    [Fact]
    public void ListItemsCountThroughLoopsIndexersAndInitializers()
    {
        var scan = PageScan.Of(new[] { ("Fake.cs", @"
            internal sealed class FakePage : SetupPage
            {
                private List<DraftingLineType> _working;
                public override void LoadFrom(FtfSettings s)
                {
                    foreach (var t in s.Drafting.LineTypes) _list.Items.Add(t.Name + t.Layer);
                    var names = s.RecordSurvey.Monuments.Where(m => m != null).Select(m => m.Block).ToList();
                }
                public override void SaveTo(FtfSettings s, ICollection<string> problems)
                {
                    var type = _working[_current];
                    type.Layer = ""x"";
                    s.RecordSurvey.Monuments = new List<MonumentStandard> { new MonumentStandard { Block = ""B"", Status = ""Found"" } };
                }
                public override void RestoreDefaults(FtfSettings s) { }
            }") });
        var all = SettingTree.All().ToDictionary(x => x.Path);

        Assert.True(scan.IsRead(all["drafting.lineTypes[].layer"], ReadThrough));
        Assert.True(scan.IsWritten(all["drafting.lineTypes[].layer"]));
        Assert.False(scan.IsWritten(all["drafting.lineTypes[].name"]));
        Assert.True(scan.IsRead(all["recordSurvey.monuments[].block"], ReadThrough));
        Assert.True(scan.IsWritten(all["recordSurvey.monuments[].block"]));
        Assert.True(scan.IsWritten(all["recordSurvey.monuments[].status"]));
        Assert.False(scan.IsRead(all["recordSurvey.monuments[].status"], ReadThrough));
    }
}

// ============================================================================ the settings

/// <summary>One value in ftf-settings.json: its JSON path, the C# property, and the object holding it.</summary>
internal sealed record Setting(string Path, string Property, Type Holder, SettingNode Owner);

/// <summary>An object in the settings tree: a section, an object inside one, or the items of a list.</summary>
internal sealed class SettingNode
{
    public SettingNode(Type type, string path, string? member, bool isItem, SettingNode? parent)
    {
        Type = type; Path = path; Member = member; IsItem = isItem; Parent = parent;
    }

    public Type Type { get; }
    public string Path { get; }
    /// <summary>The C# property that holds this object (or list) on its parent; for a section,
    /// the property on FtfSettings.</summary>
    public string? Member { get; }
    /// <summary>True for the items of a list.</summary>
    public bool IsItem { get; }
    public SettingNode? Parent { get; }
    public string Name => Type.Name;
}

internal static class SettingTree
{
    /// <summary>Element types JSON reaches that reflection does not see: the noted code list keeps a
    /// CodeNote (reason, expect, by, at) for each ignored code.</summary>
    private static readonly Dictionary<string, Type> HiddenItems = new(StringComparer.Ordinal)
    {
        ["diagnostics.ignoredCodes"] = typeof(FieldCodes.CodeNote),
    };

    public static IList<SettingNode> Nodes()
    {
        var nodes = new List<SettingNode>();
        foreach (var property in typeof(FtfSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!typeof(ISettingsSection).IsAssignableFrom(property.PropertyType)) continue;
            var key = property.GetCustomAttribute<JsonPropertyAttribute>()?.PropertyName;
            Assert.False(string.IsNullOrEmpty(key), "FtfSettings." + property.Name + " has no [JsonProperty] key.");
            Walk(new SettingNode(property.PropertyType, key!, property.Name, false, null), nodes, new HashSet<Type>());
        }
        return nodes;
    }

    public static IList<Setting> All()
    {
        var settings = new List<Setting>();
        foreach (var node in Nodes())
            foreach (var property in JsonProperties(node.Type))
                settings.Add(new Setting(node.Path + "." + property.GetCustomAttribute<JsonPropertyAttribute>()!.PropertyName,
                                         property.Name, node.Type, node));
        return settings;
    }

    private static IEnumerable<PropertyInfo> JsonProperties(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetCustomAttribute<JsonPropertyAttribute>() != null && p.GetCustomAttribute<JsonIgnoreAttribute>() == null);

    private static void Walk(SettingNode node, List<SettingNode> nodes, HashSet<Type> onPath)
    {
        nodes.Add(node);
        if (!onPath.Add(node.Type)) return;
        foreach (var property in JsonProperties(node.Type))
        {
            var path = node.Path + "." + property.GetCustomAttribute<JsonPropertyAttribute>()!.PropertyName;
            if (HiddenItems.TryGetValue(path, out var hidden))
            {
                Walk(new SettingNode(hidden, path + "[]", property.Name, true, node), nodes, onPath);
                continue;
            }
            var item = ItemType(property.PropertyType);
            if (item != null && IsSettingsObject(item))
                Walk(new SettingNode(item, path + "[]", property.Name, true, node), nodes, onPath);
            else if (IsSettingsObject(property.PropertyType))
                Walk(new SettingNode(property.PropertyType, path, property.Name, false, node), nodes, onPath);
        }
        onPath.Remove(node.Type);
    }

    private static bool IsSettingsObject(Type type) =>
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
        // A dictionary's items are key/value pairs: a leaf here (its values are plain).
        return item.IsGenericType && item.GetGenericTypeDefinition() == typeof(KeyValuePair<,>) ? null : item;
    }
}

// ============================================================================ the pages

/// <summary>
/// What the Settings window's source does with each settings object: which properties it reads
/// and writes, and from where (LoadFrom, SaveTo, RestoreDefaults, or anything else).
/// </summary>
internal sealed class PageScan
{
    [Flags]
    private enum Kind { None = 0, Load = 1, Save = 2, Restore = 4, Other = 8 }

    private sealed class Method
    {
        public Method(string name, string text, ClassText owner) { Name = name; Text = text; Owner = owner; }
        public string Name { get; }
        public string Text { get; }
        public ClassText Owner { get; }
        public Kind Kinds { get; set; }
        public bool CountsAsLoad => Kinds == Kind.None || (Kinds & (Kind.Load | Kind.Other)) != 0;
        public bool CountsAsSave => Kinds == Kind.None || (Kinds & (Kind.Save | Kind.Other)) != 0;
    }

    private sealed class ClassText
    {
        public ClassText(string name, string? baseName) { Name = name; Base = baseName; }
        public string Name { get; }
        public string? Base { get; }
        public List<Method> Methods { get; } = new();
        /// <summary>Field and property declarations (everything outside method bodies).</summary>
        public StringBuilder Fields { get; } = new();
    }

    private readonly List<ClassText> _classes;
    private readonly IList<SettingNode> _nodes = SettingTree.Nodes();
    private readonly Dictionary<(Method, SettingNode), string> _receivers = new();
    private readonly Dictionary<SettingNode, string> _members = new();

    private readonly HashSet<string> _hidden;

    private PageScan(List<ClassText> classes, IEnumerable<string> hidden)
    {
        _classes = classes;
        _hidden = new HashSet<string>(hidden, StringComparer.Ordinal);
    }

    /// <summary>The methods that count: not those of a page the window never shows (registered Unlisted).</summary>
    private IEnumerable<Method> Counted => _classes.Where(c => !_hidden.Contains(c.Name)).SelectMany(c => c.Methods);

    /// <summary>The Settings window's source: every file in Setup, and each Ui file whose class
    /// the Setup code uses (the Settings panel, the block chooser, ...). The command windows in
    /// Ui are not the Settings window, so what they read does not count.</summary>
    public static IList<(string Path, string Text)> SettingsWindowSources()
    {
        var setup = Directory.EnumerateFiles(Path.Combine(CadSource.Repo(), "src", "FieldCodes.Cad", "Setup"), "*.cs").OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => (Path: f, Text: File.ReadAllText(f))).ToList();
        Assert.True(setup.Count >= 25, "Only " + setup.Count + " files in src/FieldCodes.Cad/Setup.");
        var setupCode = string.Join("\n", setup.Select(f => Strip(f.Text)));
        var ui = Directory.EnumerateFiles(Path.Combine(CadSource.Repo(), "src", "FieldCodes.Cad", "Ui"), "*.cs").OrderBy(f => f, StringComparer.Ordinal)
            .Where(f => Regex.IsMatch(setupCode, @"\b" + Regex.Escape(Path.GetFileNameWithoutExtension(f)) + @"\b"))
            .Select(f => (Path: f, Text: File.ReadAllText(f))).ToList();
        Assert.Contains(ui, f => Path.GetFileName(f.Path) == "SettingsPanel.cs");
        return setup.Concat(ui).ToList();
    }

    /// <param name="hidden">Pages registered but never shown (Unlisted): what they do does not count.</param>
    public static PageScan Of(IEnumerable<(string Path, string Text)> files, IEnumerable<string>? hidden = null)
    {
        var classes = new List<ClassText>();
        foreach (var (_, text) in files) classes.AddRange(ReadClasses(Strip(text)));
        var scan = new PageScan(classes, hidden ?? Enumerable.Empty<string>());
        scan.Classify();
        return scan;
    }

    // ------------------------------------------------------------------ questions

    /// <summary>True when some method that counts as showing settings reads this one: the
    /// property, or a member <paramref name="readThrough"/> names for it ("Standard", or
    /// "Text(LayerProperty.Linetype)" for a call with those arguments).</summary>
    public bool IsRead(Setting setting, IReadOnlyDictionary<string, string[]> readThrough)
    {
        var members = new List<string> { Regex.Escape(setting.Property) + @"\b(?!\s*(?:<[^<>]*>\s*)?\()" };
        if (readThrough.TryGetValue(setting.Path, out var more)) members.AddRange(more.Select(MemberPattern));
        foreach (var method in Counted.Where(m => m.CountsAsLoad))
            foreach (var member in members)
                if (Accesses(method, setting.Owner, member).Any(write => !write)) return true;
        return false;
    }

    /// <summary>True when some method that counts as saving settings assigns this one, changes
    /// the list it is, or sets it in an object initializer.</summary>
    public bool IsWritten(Setting setting)
    {
        var member = Regex.Escape(setting.Property) + @"\b(?!\s*(?:<[^<>]*>\s*)?\()";
        foreach (var method in Counted.Where(m => m.CountsAsSave))
        {
            if (Accesses(method, setting.Owner, member).Any(write => write)) return true;
            if (InitializerWrites(method, setting.Owner.Type).Contains(setting.Property)) return true;
        }
        return false;
    }

    /// <summary>"Standard" is the member; "Text(LayerProperty.Linetype)" is that call with those arguments.</summary>
    private static string MemberPattern(string member)
    {
        var open = member.IndexOf('(');
        if (open < 0) return Regex.Escape(member) + @"\b";
        var args = member.Substring(open + 1).TrimEnd(')');
        return Regex.Escape(member.Substring(0, open)) + @"\s*\(\s*" +
               string.Join(@"\s*", Regex.Split(args, @"\s*([.,])\s*").Where(t => t.Length > 0).Select(Regex.Escape)) + @"\s*\)";
    }

    private static readonly Regex QuickName = new(@"^\\?@?(\w+)");

    /// <summary>Each access of <paramref name="member"/> on the node's object in this method:
    /// true for a write.</summary>
    private IEnumerable<bool> Accesses(Method method, SettingNode node, string member)
    {
        var name = QuickName.Match(member).Groups[1].Value;
        if (name.Length > 0 && method.Text.IndexOf(name, StringComparison.Ordinal) < 0) yield break;
        var receiver = Receiver(method, node);
        if (receiver == null) yield break;
        var access = new Regex("(?:" + receiver + @")\s*\??\.\s*" + member +
                               @"(?<after>\s*(?:=(?![=>])|\+=|-=|\?\?=)|\s*\??\.\s*(?:Add|AddRange|Clear|Insert|InsertRange|Remove|RemoveAt|RemoveAll|Sort|Reverse|SetNote|RemoveNote)\s*\(|\s*\[[^\]]*\]\s*=(?![=>]))?");
        foreach (Match m in access.Matches(method.Text))
            yield return m.Groups["after"].Success && m.Groups["after"].Length > 0;
    }

    private static IEnumerable<string> InitializerWrites(Method method, Type type)
    {
        var opening = new Regex(@"\bnew\s+(?:[\w.]+\.)?" + Regex.Escape(type.Name) + @"\s*(?:\((?:[^()]|\([^()]*\))*\))?\s*\{");
        foreach (Match m in opening.Matches(method.Text))
        {
            var body = Block(method.Text, m.Index + m.Length - 1);
            foreach (var part in SplitTopLevel(body, ',', false))
            {
                var assignment = Regex.Match(part, @"^\s*(\w+)\s*=(?![=>])");
                if (assignment.Success) yield return assignment.Groups[1].Value;
            }
        }
    }

    // ------------------------------------------------------------------ receivers

    private const string Ident = @"@?[A-Za-z_]\w*";

    /// <summary>The start of an expression before the part a receiver matches (<c>s</c> in
    /// <c>s.Drafting.LineTypes</c>, <c>Context.Resolution.Settings</c>).</summary>
    private static readonly Regex LambdaFirstParameter = new(
        @"(?:\(\s*(?<n>@?[A-Za-z_]\w*)\s*(?:,\s*@?[A-Za-z_]\w*\s*)*\)|(?<![\w.])(?<n>@?[A-Za-z_]\w*))\s*=>");

    private const string Lead =@"(?:[\w.]|\[[^\]]*\]|\((?:[^()]|\([^()]*\))*\))*?";

    /// <summary>A regex matching an expression that denotes an instance of the node's object in
    /// this method, or null when nothing in the method can.</summary>
    private string? Receiver(Method method, SettingNode node)
    {
        if (_receivers.TryGetValue((method, node), out var cached)) return cached;
        var parts = new List<string>();

        // Through the object holding it: settings.Easements, d.Continuity, x.LineTypes[i].
        if (node.Parent == null) parts.Add(@"\.\s*" + node.Member + @"\b");
        else
        {
            var parent = Receiver(method, node.Parent);
            if (parent != null)
                parts.Add("(?:" + parent + @")\s*\??\.\s*" + node.Member + @"\b" + (node.IsItem ? @"\s*\[[^\]]*\]" : @"(?!\s*[\[(])"));
        }

        // Fields and properties of its type on any class of the window: form.Current, entry.Note.
        var member = MemberChain(node);
        if (member != null) parts.Add(member);

        // Names bound to it in this method or its class.
        var scope = method.Text + "\n" + string.Join("\n", Family(method.Owner).Select(c => c.Fields.ToString()));
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in Typed(node.Type).Matches(scope)) names.Add(m.Groups["n"].Value);
        var holderExpr = HolderExpression(method, node);
        if (holderExpr != null)
        {
            if (node.IsItem)
            {
                var lists = new HashSet<string>(StringComparer.Ordinal);
                foreach (Match m in TypedList(node.Type).Matches(scope)) lists.Add(m.Groups["n"].Value);
                var list = "(?:" + holderExpr + (lists.Count > 0 ? "|" + Names(lists) : string.Empty) + ")";
                BindItems(method.Text, list, names);
                parts.Add("(?:" + list + @")\s*\[[^\]]*\]");
            }
        }
        // var x = y as T; var x = (T)y; y is T x.
        foreach (Match m in new Regex(@"\bvar\s+(?<n>" + Ident + @")\s*=\s*(?:[^;]*?\bas\s+(?:\w+\.)*" + Regex.Escape(node.Type.Name) +
                                      @"\b|\(\s*(?:\w+\.)*" + Regex.Escape(node.Type.Name) + @"\s*\))").Matches(method.Text))
            names.Add(m.Groups["n"].Value);
        foreach (Match m in new Regex(@"\bis\s+(?:\w+\.)*" + Regex.Escape(node.Type.Name) + @"\s+(?<n>" + Ident + ")").Matches(method.Text))
            names.Add(m.Groups["n"].Value);
        // var x = <parent>.Lookup(...) where Lookup returns this type (UtilitySettings.Standard).
        if (node.Parent != null)
        {
            var parent = Receiver(method, node.Parent);
            var lookups = node.Parent.Type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.ReturnType == node.Type).Select(m => m.Name).Distinct().ToList();
            if (parent != null && lookups.Count > 0)
                foreach (Match m in new Regex(@"\bvar\s+(?<n>" + Ident + @")\s*=\s*" + Lead + "(?:" + parent + @")\s*\??\.\s*(?:" + string.Join("|", lookups) + @")\s*\(").Matches(method.Text))
                    names.Add(m.Groups["n"].Value);
        }
        foreach (Match m in new Regex(@"\bvar\s+(?<n>" + Ident + @")\s*=\s*new\s+(?:[\w.]+\.)?" + Regex.Escape(node.Type.Name) + @"\s*[({]").Matches(method.Text))
            names.Add(m.Groups["n"].Value);
        // Table-driven pages: x => x.Prop and (x, v) => x.Prop = v where the code declares
        // Func<ThisType, ...> / Action<ThisType, ...> (Easement Exhibits).
        if (Regex.IsMatch(method.Text, @"\b(?:Func|Action|Predicate|Converter|Comparison)\s*<\s*(?:\w+\.)*" + Regex.Escape(node.Type.Name) + @"\s*[,>]"))
            foreach (Match m in LambdaFirstParameter.Matches(method.Text)) names.Add(m.Groups["n"].Value);

        // var x = <any of the above> (or cond ? it : null, or it ?? fallback), until nothing new.
        for (var pass = 0; pass < 4 && parts.Count + names.Count > 0; pass++)
        {
            var known = string.Join("|", parts.Concat(names.Count > 0 ? new[] { Names(names) } : Array.Empty<string>()).Select(p => "(?:" + p + ")"));
            var before = names.Count;
            foreach (Match m in new Regex(@"\bvar\s+(?<n>" + Ident + @")\s*=\s*(?:[^;?]*\?(?![?.])\s*)?\(?\s*" + Lead + "(?:" + known + @")\s*(?:;|\?\?|:|\))").Matches(method.Text))
                names.Add(m.Groups["n"].Value);
            if (names.Count == before) break;
        }
        if (names.Count > 0) parts.Add(Names(names));

        var result = parts.Count == 0 ? null : string.Join("|", parts.Select(p => "(?:" + p + ")"));
        _receivers[(method, node)] = result!;
        return result;
    }

    /// <summary>The expression for the node's holder: the list itself for list items
    /// (settings.Drafting.LineTypes), the property for an object.</summary>
    private string? HolderExpression(Method method, SettingNode node)
    {
        if (node.Parent == null) return @"\.\s*" + node.Member + @"\b";
        var parent = Receiver(method, node.Parent);
        return parent == null ? null : "(?:" + parent + @")\s*\??\.\s*" + node.Member + @"\b(?!\s*\()";
    }

    /// <summary>Names a loop, an index or a lambda over the list binds to its items.</summary>
    private static void BindItems(string text, string list, HashSet<string> names)
    {
        foreach (Match m in new Regex(@"\bforeach\s*\(\s*(?:var|[\w.<>?]+)\s+(?<n>" + Ident + @")\s+in\s+\(?\s*" + Lead + "(?:" + list + @")").Matches(text))
            names.Add(m.Groups["n"].Value);
        foreach (Match m in new Regex(@"\bvar\s+(?<n>" + Ident + @")\s*=\s*" + Lead + "(?:" + list + @")\s*\[").Matches(text))
            names.Add(m.Groups["n"].Value);

        // list.Where(x => ...).Select(y => ...): every lambda up to and including the first Select.
        foreach (Match m in new Regex("(?:" + list + ")").Matches(text))
        {
            var at = m.Index + m.Length;
            while (true)
            {
                var call = new Regex(@"\G\s*\??\.\s*(?<method>\w+)\s*\(").Match(text, at);
                if (!call.Success) break;
                var open = call.Index + call.Length - 1;
                var args = Parens(text, open);
                var lambda = Regex.Match(args, @"^\s*\(?\s*(?<n>" + Ident + @")\s*\)?\s*=>");
                if (lambda.Success) names.Add(lambda.Groups["n"].Value);
                var name = call.Groups["method"].Value;
                if (name == "Select" || name == "SelectMany") break;
                var bind = Regex.Match(text.Substring(0, m.Index), @"\bvar\s+(?<n>" + Ident + @")\s*=\s*[^;=]*$");
                if (bind.Success && (name == "First" || name == "FirstOrDefault" || name == "Single" || name == "SingleOrDefault" ||
                                     name == "Last" || name == "LastOrDefault" || name == "Find" || name == "ElementAt"))
                    names.Add(bind.Groups["n"].Value);
                at = open + args.Length + 2;
            }
        }
    }

    /// <summary>Fields and properties of the node's type (or a list of it) declared by any class
    /// of the window, reached as a member: <c>entry.Note.Reason</c>.</summary>
    private string? MemberChain(SettingNode node)
    {
        if (_members.TryGetValue(node, out var cached)) return cached;
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in _classes)
        {
            var fields = c.Fields.ToString();
            foreach (Match m in (node.IsItem ? TypedList(node.Type) : Typed(node.Type)).Matches(fields)) names.Add(m.Groups["n"].Value);
        }
        string? result = null;
        if (names.Count > 0)
            result = @"\.\s*" + Names(names) + (node.IsItem ? @"\s*\[[^\]]*\]" : @"(?!\s*[\[(])");
        _members[node] = result!;
        return result;
    }

    private static Regex Typed(Type type) => new(
        @"(?<![\w.<])(?:[\w]+\.)*" + Regex.Escape(type.Name) + @"\??\s+(?<n>" + Ident + @")\s*(?=[=;,){]|\bin\b)");

    private static Regex TypedList(Type type) => new(
        @"(?:(?:List|IList|IEnumerable|ICollection|IReadOnlyList|IReadOnlyCollection|BindingList)\s*<\s*(?:[\w]+\.)*" + Regex.Escape(type.Name) +
        @"\s*>|(?:[\w]+\.)*" + Regex.Escape(type.Name) + @"\s*\[\s*\])\s+(?<n>" + Ident + @")\s*(?=[=;,){]|\bin\b)");

    private static string Names(IEnumerable<string> names) =>
        @"(?<![\w.])(?:" + string.Join("|", names.Select(Regex.Escape)) + @")\b";

    // ------------------------------------------------------------------ classification

    private IEnumerable<ClassText> Family(ClassText c)
    {
        var seen = new HashSet<ClassText>();
        for (var at = c; at != null && seen.Add(at); at = at.Base == null ? null : _classes.FirstOrDefault(x => x.Name == at.Base))
            yield return at;
    }

    private static readonly Regex OwnCall = new(@"(?:\b(?:this|base)\s*\.\s*|(?<![\w.]\s*))(?<n>\w+)\s*\(");

    private bool IsPage(ClassText c) => Family(c).Any(x => x.Base == "SetupPage") || c.Name == "SetupPage";

    /// <summary>
    /// Marks each method of a page with where it is reached from. LoadFrom, SaveTo and
    /// RestoreDefaults are their own; any other method nothing in the page calls (BuildBody, an
    /// event handler) is Other, and so is everything it calls. Methods of the window's other
    /// classes (dialogs, helpers) are Other.
    /// </summary>
    private void Classify()
    {
        foreach (var c in _classes)
        {
            if (!IsPage(c)) { foreach (var m in c.Methods) m.Kinds |= Kind.Other; continue; }
            var family = Family(c).SelectMany(x => x.Methods).ToList();
            var byName = family.GroupBy(m => m.Name).ToDictionary(g => g.Key, g => g.ToList());
            // Calls on the page itself: Fill(...), this.Fill(...) -- not LayerCatalog.Build(...).
            var calls = family.ToDictionary(m => m, m => OwnCall.Matches(m.Text).Select(x => x.Groups["n"].Value)
                .Where(byName.ContainsKey).SelectMany(n => byName[n]).Where(x => x != m).Distinct().ToList());
            var called = new HashSet<Method>(calls.Values.SelectMany(v => v));
            foreach (var root in family)
            {
                var kind = root.Name == "LoadFrom" ? Kind.Load : root.Name == "SaveTo" ? Kind.Save
                         : root.Name == "RestoreDefaults" ? Kind.Restore : called.Contains(root) ? Kind.None : Kind.Other;
                if (kind == Kind.None) continue;
                var stack = new Stack<Method>(new[] { root });
                var seen = new HashSet<Method>();
                while (stack.Count > 0)
                {
                    var m = stack.Pop();
                    if (!seen.Add(m)) continue;
                    m.Kinds |= kind;
                    foreach (var next in calls[m])
                        if (next.Name != "LoadFrom" && next.Name != "SaveTo" && next.Name != "RestoreDefaults") stack.Push(next);
                }
            }
        }
    }

    // ------------------------------------------------------------------ reading C#

    private static readonly HashSet<string> NotMethods = new(StringComparer.Ordinal)
        { "if", "for", "foreach", "while", "switch", "catch", "using", "lock", "fixed", "return", "new", "get", "set", "nameof", "typeof", "base", "this", "when" };

    private static IEnumerable<ClassText> ReadClasses(string code)
    {
        foreach (Match m in Regex.Matches(code, @"\b(?:class|struct)\s+(?<name>\w+)(?:\s*<[^{]*?>)?\s*(?::\s*(?<base>[^{]+?))?\s*(?:where\s[^{]*)?\{"))
        {
            var baseName = m.Groups["base"].Success
                ? Regex.Replace(SplitTopLevel(m.Groups["base"].Value, ',', true).First().Trim(), @"<.*$", string.Empty).Split('.').Last().Trim()
                : null;
            var c = new ClassText(m.Groups["name"].Value, baseName);
            ReadMembers(Block(code, m.Index + m.Length - 1), c);
            yield return c;
        }
    }

    /// <summary>Splits a class body into its methods (and accessors) and its field declarations.</summary>
    private static void ReadMembers(string body, ClassText c)
    {
        var start = 0;
        var i = 0;
        while (i < body.Length)
        {
            var ch = body[i];
            if (ch == ';')
            {
                var segment = body.Substring(start, i - start + 1);
                var arrow = Regex.Match(segment, @"(?<name>\w+)\s*(?:<[^<>]*>)?\s*\((?<params>[^()]*(?:\([^()]*\)[^()]*)*)\)\s*=>");
                // An expression-bodied method (Name(...) => ...;) unless an assignment comes first
                // (a field initialized with a lambda).
                if (arrow.Success && !NotMethods.Contains(arrow.Groups["name"].Value) && arrow.Index < FirstAssignment(segment))
                    c.Methods.Add(new Method(arrow.Groups["name"].Value, segment, c));
                else
                {
                    c.Fields.Append(segment).Append('\n');
                    // A field's initializer is code too (a lambda, a list of defaults).
                    c.Methods.Add(new Method("<field>", segment, c));
                }
                start = i + 1;
                i++;
                continue;
            }
            if (ch == '{')
            {
                var header = body.Substring(start, i - start);
                var block = Block(body, i);
                var end = i + block.Length + 2;
                if (Regex.IsMatch(header, @"\b(?:class|struct|interface|enum)\b"))
                {
                    start = end; i = end; continue;   // a nested type: read as a class of its own
                }
                var signature = Regex.Match(header, @"(?<name>\w+)\s*(?:<[^<>]*>)?\s*\((?<params>(?:[^()]|\([^()]*\))*)\)\s*(?::\s*(?:base|this)\s*\((?:[^()]|\([^()]*\))*\))?\s*(?:where\s[^{]*)?$");
                if (FirstAssignment(header) < header.Length)
                {
                    i = end; continue;                 // a field initializer: the statement runs to its ';'
                }
                if (signature.Success && !NotMethods.Contains(signature.Groups["name"].Value))
                    c.Methods.Add(new Method(signature.Groups["name"].Value, header + "{" + block + "}", c));
                else
                {
                    // A property: its declaration is a field of the class, its accessors are code.
                    c.Fields.Append(header).Append(";\n");
                    c.Methods.Add(new Method("<" + Regex.Match(header, @"(\w+)\s*$").Groups[1].Value + ">", block, c));
                }
                start = end; i = end;
                continue;
            }
            i++;
        }
    }

    private static int FirstAssignment(string text)
    {
        var m = Regex.Match(text, @"(?<![=!<>+\-*/%&|^?])=(?![=>])");
        return m.Success ? m.Index : text.Length;
    }

    /// <summary>The text inside the braces opening at <paramref name="open"/>.</summary>
    private static string Block(string text, int open)
    {
        var depth = 0;
        for (var i = open; i < text.Length; i++)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}' && --depth == 0) return text.Substring(open + 1, i - open - 1);
        }
        return text.Substring(open + 1);
    }

    private static string Parens(string text, int open)
    {
        var depth = 0;
        for (var i = open; i < text.Length; i++)
        {
            if (text[i] == '(') depth++;
            else if (text[i] == ')' && --depth == 0) return text.Substring(open + 1, i - open - 1);
        }
        return text.Substring(open + 1);
    }

    /// <summary>Splits at <paramref name="separator"/> outside brackets (and outside &lt;&gt; when
    /// <paramref name="angles"/>: a type list, where there are no lambdas or comparisons).</summary>
    private static IEnumerable<string> SplitTopLevel(string text, char separator, bool angles)
    {
        var depth = 0;
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch == '(' || ch == '{' || ch == '[' || (angles && ch == '<')) depth++;
            else if (ch == ')' || ch == '}' || ch == ']' || (angles && ch == '>')) depth--;
            else if (ch == separator && depth == 0) { yield return text.Substring(start, i - start); start = i + 1; }
        }
        yield return text.Substring(start);
    }

    /// <summary>The code with comments removed and the text of every string and character
    /// literal blanked (the holes of an interpolated string are kept: they are code).</summary>
    internal static string Strip(string source)
    {
        var sb = new StringBuilder(source.Length);
        var i = 0;
        Code(source, ref i, sb, false);
        return sb.ToString();
    }

    private static void Code(string s, ref int i, StringBuilder sb, bool hole)
    {
        var depth = 0;
        while (i < s.Length)
        {
            var c = s[i];
            var next = i + 1 < s.Length ? s[i + 1] : '\0';
            if (c == '/' && next == '/') { while (i < s.Length && s[i] != '\n') i++; continue; }
            if (c == '/' && next == '*')
            {
                i += 2;
                while (i < s.Length && !(s[i] == '*' && i + 1 < s.Length && s[i + 1] == '/')) { if (s[i] == '\n') sb.Append('\n'); i++; }
                i += 2;
                continue;
            }
            if (c == '\'')
            {
                sb.Append("' '");
                i++;
                if (i < s.Length && s[i] == '\\') i += 2; else i++;
                while (i < s.Length && s[i] != '\'') i++;
                i++;
                continue;
            }
            var verbatim = (c == '@' && next == '"') || (c == '@' && next == '$') || (c == '$' && next == '@');
            var interpolated = c == '$' || (c == '@' && next == '$');
            if (c == '"' || verbatim || (c == '$' && next == '"'))
            {
                while (i < s.Length && s[i] != '"') i++;   // past the prefix
                i++;
                sb.Append('"');
                StringBody(s, ref i, sb, verbatim, interpolated);
                sb.Append('"');
                continue;
            }
            if (hole)
            {
                if (c == '{' ) depth++;
                else if (c == '}') { if (depth == 0) return; depth--; }
                else if (c == ':' && depth == 0 && !InParens(sb)) { while (i < s.Length && s[i] != '}') i++; return; }
            }
            sb.Append(c);
            i++;
        }
    }

    private static bool InParens(StringBuilder sb)
    {
        var depth = 0;
        for (var k = sb.Length - 1; k >= 0; k--)
        {
            if (sb[k] == ')') depth++;
            else if (sb[k] == '(') { if (depth == 0) return true; depth--; }
            else if (sb[k] == '{' || sb[k] == '"') return false;
        }
        return false;
    }

    private static void StringBody(string s, ref int i, StringBuilder sb, bool verbatim, bool interpolated)
    {
        while (i < s.Length)
        {
            var c = s[i];
            if (verbatim && c == '"' && i + 1 < s.Length && s[i + 1] == '"') { i += 2; continue; }
            if (c == '"') { i++; return; }
            if (!verbatim && c == '\\') { i += 2; continue; }
            if (!verbatim && c == '\n') return;                // an unterminated literal: stop at the line end
            if (interpolated && c == '{')
            {
                if (i + 1 < s.Length && s[i + 1] == '{') { i += 2; continue; }
                i++;
                sb.Append('{');
                Code(s, ref i, sb, true);
                sb.Append('}');
                if (i < s.Length && s[i] == '}') i++;
                continue;
            }
            i++;
        }
    }
}
