using System.Text.RegularExpressions;
using FieldCodes.Settings;

// A page registered on the assembly, as LayersPage and PointFeaturesPage are in the plugin.
[assembly: SettingsPage(Page = typeof(FieldCodes.Tests.FakePageC), Group = "Reports", Label = "Fake C", Order = 1)]

namespace FieldCodes.Tests;

/// <summary>
/// The Settings page registry, held to its promises without Civil 3D: every SetupPage in the plugin
/// is registered, the navigation it builds keeps today's pages in today's groups and order, every
/// settings section is edited by some page, and a page's "Affects:" caption agrees with its
/// registration. The plugin's own source is read, since the Civil 3D project cannot be loaded here.
/// </summary>
public sealed class SettingsRegistryTests
{
    /// <summary>The navigation before the registry, group by group (SettingsPanel at integration
    /// 9851696), plus the two pages this change adds at the end of their groups.</summary>
    private static readonly (string Group, string[] Labels)[] Expected =
    {
        ("FIELDTOFINISH", new[] { "General", "Layers", "Point Features", "Symbols and Rotations", "Trees", "Point Labels",
                                  "Line Labels", "Label All", "Tags", "Schedule", "Driplines", "Drawing Order", "Workflows" }),
        ("SURVEY DRAFTING", new[] { "Spot Elevations", "Contours", "Drafting Lines", "Sheets", "Utility Dips" }),
        ("BOUNDARY AND EASEMENTS", new[] { "Right of Way", "Platted Blocks", "Recorded Surveys", "Survey Comparison",
                                           "Strip Easements", "Easement Exhibits" }),
        ("ADMINISTRATION", new[] { "Profiles", "Diagnostics", "Advanced Settings", "Commands" }),
    };

    // ------------------------------------------------------------ the plugin's pages

    [Fact]
    public void EverySetupPageInThePluginIsRegistered()
    {
        var pages = CadSource.SetupPages();
        Assert.True(pages.Count >= 28, "Only " + pages.Count + " SetupPage classes were found; the source scan has stopped working.");

        var registered = CadSource.Registrations().Select(r => r.PageName).ToList();
        var missing = pages.Where(p => !registered.Contains(p)).ToList();
        Assert.True(missing.Count == 0,
            "Not registered, so never shown in Settings: " + string.Join(", ", missing) +
            ". Put [SettingsPage(Group = ..., Label = ..., Order = ...)] above the class (docs/settings-registry.md).");

        var strays = registered.Where(r => !pages.Contains(r)).ToList();
        Assert.True(strays.Count == 0, "Registered but not a SetupPage: " + string.Join(", ", strays));
    }

    [Fact]
    public void ThePluginsRegistrationsAreValid()
    {
        var problems = new List<string>();
        SettingsRegistry.Validate(CadSource.Registrations(), problems);
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void TheNavigationKeepsTodaysPagesInTheirGroupsAndOrder()
    {
        var nav = SettingsRegistry.Arrange(CadSource.Registrations());

        // The four groups lead, in this order; a group a newer page names may follow them.
        var captions = nav.Where(e => e.IsGroup).Select(e => e.Caption).ToList();
        Assert.Equal(Expected.Select(e => e.Group), captions.Take(4));

        // Today's pages, group by group, in exactly today's order. Pages added later may sit
        // between them; they are left out of this comparison, not forbidden.
        var known = new HashSet<string>(Expected.SelectMany(e => e.Labels));
        var actual = new List<string>();
        foreach (var entry in nav)
            if (entry.IsGroup ? captions.IndexOf(entry.Caption) < 4 : known.Contains(entry.Caption))
                actual.Add(entry.IsGroup ? "[" + entry.Caption + "]" : entry.Caption);
        var expected = Expected.SelectMany(e => new[] { "[" + e.Group + "]" }.Concat(e.Labels)).ToList();
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void FieldCodesIsRegisteredButNotShown()
    {
        var page = CadSource.Registrations().Single(r => r.PageName == "FieldCodesPage");
        Assert.False(page.IsListed);
        Assert.DoesNotContain(SettingsRegistry.Arrange(CadSource.Registrations()), e => e.Caption == "Field Codes");
    }

    [Fact]
    public void EverySettingsSectionIsEditedByARegisteredPage()
    {
        var settings = new FtfSettings();
        var edited = new HashSet<string>(CadSource.Registrations().SelectMany(r => r.Sections), StringComparer.OrdinalIgnoreCase);
        foreach (var section in settings.Sections)
        {
            var key = SettingsRegistry.SectionKey(settings, section);
            Assert.False(key == null, section.Title + " is in Sections but not saved under any key.");
            string? reason;
            if (SettingsRegistry.SectionsWithoutPage.TryGetValue(key!, out reason))
                Assert.False(string.IsNullOrWhiteSpace(reason), key + " is marked as having no page but says no reason.");
            else
                Assert.True(edited.Contains(key!), "No registered page edits \"" + key + "\" (" + section.Title +
                    "). Name it in a page's Sections, or add it to SettingsRegistry.SectionsWithoutPage with the reason.");
        }
    }

    [Fact]
    public void EveryCommandARegistrationNamesIsRegisteredByThePlugin()
    {
        var commands = CadSource.RegisteredCommands();
        Assert.Contains("FTFRUN", commands);
        var unknown = CadSource.Registrations()
            .SelectMany(r => r.Commands.Where(c => !commands.Contains(c)).Select(c => r.PageName + ": " + c)).ToList();
        Assert.True(unknown.Count == 0, "Commands the plugin does not register: " + string.Join(", ", unknown));
    }

    [Fact]
    public void APagesAffectsCaptionAgreesWithItsRegistration()
    {
        // Every command the page's own "Affects:" line names is in its registration, so Settings >
        // Commands links that command to the page.
        var commands = CadSource.RegisteredCommands();
        var registrations = CadSource.Registrations().ToDictionary(r => r.PageName);
        var mismatches = new List<string>();
        foreach (var page in CadSource.SetupPageClasses())
        {
            var caption = Regex.Match(page.Body, @"AffectedCommands\s*\{\s*get\s*\{\s*return\s*""(?<t>[^""]*)""");
            if (!caption.Success || !registrations.ContainsKey(page.Name)) continue;
            var registration = registrations[page.Name];
            foreach (Match token in Regex.Matches(caption.Groups["t"].Value, @"\b[A-Z][A-Z0-9]{2,}\b"))
                if (commands.Contains(token.Value) && !registration.Names(token.Value))
                    mismatches.Add(page.Name + " names " + token.Value);
        }
        Assert.True(mismatches.Count == 0, "Add these to the page's Commands: " + string.Join("; ", mismatches));
    }

    // ------------------------------------------------------------- the follow-ups

    [Fact]
    public void WorkflowFollowUpsAreRegisteredAndNeverAskAnything()
    {
        var commands = CadSource.RegisteredCommands();
        Assert.NotEmpty(SettingsRegistry.WorkflowFollowUps);

        // The prompt detector itself works: FTFLEGEND asks for a corner, which is why it is not allowed.
        Assert.Matches(CadSource.Prompt, CadSource.MethodBody("FTFLEGEND"));
        Assert.Matches(CadSource.Prompt, CadSource.MethodBody("FTFCONTROL"));

        foreach (var command in SettingsRegistry.WorkflowFollowUps)
        {
            Assert.Contains(command, commands);
            Assert.Contains(command, FieldCodes.Commands.RibbonLayout.Commands);
            Assert.DoesNotMatch(CadSource.Prompt, CadSource.MethodBody(command));
        }
    }

    // --------------------------------------------------------------- arranging

    [Fact]
    public void KnownGroupsComeFirstInTheirOrderAndANewGroupFollows()
    {
        var nav = SettingsRegistry.Arrange(new[]
        {
            Page("Reports", "Report Options", 10),
            Page("administration", "Profiles", 10),
            Page("FieldToFinish", "General", 10),
            Page("Archive", "Old Things", 10),
            Page("Survey Drafting", "Contours", 20),
            Page("Survey Drafting", "Spot Elevations", 10),
        });

        Assert.Equal(new[]
        {
            "FIELDTOFINISH", "General", "SURVEY DRAFTING", "Spot Elevations", "Contours",
            "ADMINISTRATION", "Profiles", "ARCHIVE", "Old Things", "REPORTS", "Report Options"
        }, nav.Select(e => e.Caption));
        Assert.Equal("Administration", nav.Single(e => e.Caption == "Profiles").Group);   // any casing finds the group
    }

    [Fact]
    public void PagesSortByOrderThenLabelAndUnlistedPagesStayOut()
    {
        var nav = SettingsRegistry.Arrange(new[]
        {
            Page("Administration", "Zeta", 5),
            Page("Administration", "Alpha", 5),
            Page("Administration", "First", 1),
            new SettingsPageInfo("HiddenPage", null, "Administration", "Hidden", 0, null, null, false, null, "kept for later"),
        });
        Assert.Equal(new[] { "ADMINISTRATION", "First", "Alpha", "Zeta" }, nav.Select(e => e.Caption));
    }

    // ---------------------------------------------------------------- checking

    [Fact]
    public void ValidateReportsEachKindOfMistake()
    {
        var problems = new List<string>();
        SettingsRegistry.Validate(new[]
        {
            Page("FieldToFinish", "Tags", 1, sections: "tags", commands: "FTFTAGS", name: "TagsPage"),
            Page("FieldToFinish", "tags", 2, name: "OtherTagsPage"),                       // same label, other case
            Page("FieldToFinish", "Fonts", 3, sections: "fonts", name: "FontsPage"),       // no such section
            Page("FieldToFinish", "Bad", 4, commands: "ftf tags", name: "BadPage"),        // not a command name
            Page("", "Groupless", 5, name: "GrouplessPage"),
            Page("FieldToFinish", "", 6, name: "LabellessPage"),
            Page("FieldToFinish", "Renamed", 7, aliases: "Tags", name: "RenamedPage"),     // alias takes a label
            Page("FieldToFinish", "Twice", 8, name: "TagsPage"),                           // one class, two registrations
        }, problems);

        Assert.Contains(problems, p => p.Contains("\"tags\" names both TagsPage and OtherTagsPage"));
        Assert.Contains(problems, p => p.Contains("FontsPage edits \"fonts\""));
        Assert.Contains(problems, p => p.Contains("BadPage names \"FTF TAGS\""));
        Assert.Contains(problems, p => p.Contains("GrouplessPage is registered without a group"));
        Assert.Contains(problems, p => p.Contains("LabellessPage is registered without a label"));
        Assert.Contains(problems, p => p.Contains("\"Tags\" names both TagsPage and RenamedPage"));
        Assert.Contains(problems, p => p.Contains("TagsPage is registered 2 times"));
    }

    [Fact]
    public void TheAttributeReadsListsAndTheEveryCommandMark()
    {
        var info = SettingsRegistry.FromAttribute(new SettingsPageAttribute
        {
            Group = " survey drafting ", Label = " Sheets ", Order = 40,
            Sections = "sheets; drafting", Commands = "ftfsheets, *, FTFKEYMAP", Aliases = "Plan Sheets"
        }, "SheetsPage");

        Assert.Equal("Survey Drafting", info.Group);
        Assert.Equal("Sheets", info.Label);
        Assert.Equal(new[] { "sheets", "drafting" }, info.Sections);
        Assert.Equal(new[] { "FTFSHEETS", "FTFKEYMAP" }, info.Commands);
        Assert.True(info.AllCommands);
        Assert.True(info.Names("ftfkeymap"));
        Assert.True(info.Edits("Drafting"));
        Assert.Equal(new[] { "Plan Sheets" }, info.Aliases);
        Assert.True(info.IsListed);
    }

    [Fact]
    public void SectionKeysAreTheSettingsFileKeys()
    {
        var keys = SettingsRegistry.SectionKeys();
        Assert.Contains("general", keys);
        Assert.Contains("lineLabels", keys);
        Assert.Contains("workflows", keys);
        Assert.Contains("diagnostics", keys);
        Assert.DoesNotContain("version", keys);

        var settings = new FtfSettings();
        Assert.Equal("tags", SettingsRegistry.SectionKey(settings, settings.Tags));
        Assert.Equal("workflows", SettingsRegistry.SectionKey(settings, settings.Workflows));
        Assert.Null(SettingsRegistry.SectionKey(settings, new TagSettings()));
    }

    // ---------------------------------------------------------- discovering

    [Fact]
    public void DiscoveryReadsClassAndAssemblyRegistrationsAndFindsTheUnregistered()
    {
        var problems = new List<string>();
        var found = SettingsRegistry.Discover(typeof(FakePageBase).Assembly, typeof(FakePageBase), problems);

        Assert.Equal(new[] { "FakePageA", "FakePageC" }, found.Select(f => f.PageName).OrderBy(n => n));
        Assert.Equal(typeof(FakePageC), found.Single(f => f.Label == "Fake C").PageType);
        Assert.Equal(new[] { "FTFTAGS" }, found.Single(f => f.Label == "Fake A").Commands);
        Assert.Contains(problems, p => p.StartsWith("NotAPage is registered as a Settings page but is not one"));

        Assert.Equal(new[] { typeof(FakePageB) }, SettingsRegistry.Unregistered(typeof(FakePageBase).Assembly, typeof(FakePageBase)));

        // A new group's pages arrive under it, after the four known groups, with no other change.
        Assert.Equal(new[] { "REPORTS", "Fake C", "Fake A" }, SettingsRegistry.Arrange(found).Select(e => e.Caption));
    }

    // ----------------------------------------------------------------- helpers

    private static SettingsPageInfo Page(string group, string label, int order, string? sections = null,
                                         string? commands = null, string? aliases = null, string? name = null)
    {
        return SettingsRegistry.FromAttribute(new SettingsPageAttribute
        {
            Group = group, Label = label, Order = order, Sections = sections, Commands = commands, Aliases = aliases
        }, name ?? label.Replace(" ", "") + "Page");
    }
}

// --------------------------------------------------------- types for discovery

public abstract class FakePageBase { }

[SettingsPage(Group = "Reports", Label = "Fake A", Order = 2, Sections = "tags", Commands = "FTFTAGS")]
public sealed class FakePageA : FakePageBase { }

/// <summary>A page nobody registered: discovery must say so.</summary>
public sealed class FakePageB : FakePageBase { }

/// <summary>Registered on the assembly, at the top of this file.</summary>
public sealed class FakePageC : FakePageBase { }

[SettingsPage(Group = "Reports", Label = "Not a page")]
public sealed class NotAPage { }

/// <summary>
/// The Civil 3D project's source, read as text: its SetupPage classes, their [SettingsPage]
/// registrations (on the class or on the assembly), its [CommandMethod] names and their bodies.
/// Registrations are written with literal arguments precisely so they can be read here.
/// </summary>
internal static class CadSource
{
    public sealed record PageClass(string Name, string Base, bool IsAbstract, string Body);

    /// <summary>A call that stops for the user: a prompt on the command line or a modal window.</summary>
    public static readonly Regex Prompt = new(
        @"\b(?:ed|Editor|editor)\s*\.\s*Get(?:Point|Keywords|Selection|Entity|String|Integer|Double|Distance|Angle|Corner|NestedEntity)\s*\(|MessageBox\s*\.\s*Show\s*\(|ShowModalDialog\s*\(|\.ShowDialog\s*\(");

    private static readonly Regex ClassDeclaration = new(
        @"(?m)^[ \t]*(?<mods>(?:(?:public|internal|private|protected|sealed|abstract|static|partial)\s+)*)class\s+(?<name>\w+)(?:<[^>]*>)?\s*:\s*(?<base>[\w.]+)");

    private const string Args = @"(?<args>(?:[^()""]|""(?:[^""\\]|\\.)*""|\([^()]*\))*)";

    private static readonly Regex ClassRegistration = new(
        @"\[(?:FieldCodes\.Settings\.)?SettingsPage\(" + Args + @"\)\]" +
        @"(?:\s*(?:\[[^\]\r\n]*\]|///[^\r\n]*))*\s*(?:(?:public|internal|private|protected|sealed|abstract|static|partial)\s+)*class\s+(?<name>\w+)");

    private static readonly Regex AssemblyRegistration = new(
        @"\[assembly:\s*(?:FieldCodes\.Settings\.)?SettingsPage\(" + Args + @"\)\]");

    private static readonly Regex Argument = new(
        @"(?<key>\w+)\s*=\s*(?:""(?<s>(?:[^""\\]|\\.)*)""|(?<n>-?\d+)|typeof\(\s*(?<t>[\w.]+)\s*\))");

    private static readonly Regex CommandAttribute = new(
        @"^\s*\[\s*CommandMethod\(\s*""([^""]+)""\s*(?:,\s*""([^""]+)"")?");

    public static string Repo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "src", "FieldCodes.Cad"))) dir = dir.Parent;
        Assert.True(dir != null, "No folder above " + AppContext.BaseDirectory + " holds src/FieldCodes.Cad.");
        return dir!.FullName;
    }

    private static IEnumerable<(string Path, string Text)> Files()
    {
        var cad = Path.Combine(Repo(), "src", "FieldCodes.Cad");
        foreach (var file in Directory.EnumerateFiles(cad, "*.cs", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(cad, file).Replace('\\', '/');
            if (relative.StartsWith("obj/") || relative.StartsWith("bin/")) continue;
            yield return (file, File.ReadAllText(file));
        }
    }

    /// <summary>Every class with a base type, its body running to the next class in its file.</summary>
    public static IList<PageClass> Classes()
    {
        var classes = new List<PageClass>();
        foreach (var (_, text) in Files())
        {
            var matches = ClassDeclaration.Matches(text).Cast<Match>().ToList();
            for (var i = 0; i < matches.Count; i++)
            {
                var m = matches[i];
                var end = i + 1 < matches.Count ? matches[i + 1].Index : text.Length;
                classes.Add(new PageClass(m.Groups["name"].Value, m.Groups["base"].Value.Split('.').Last(),
                    m.Groups["mods"].Value.Contains("abstract"), text.Substring(m.Index, end - m.Index)));
            }
        }
        return classes;
    }

    /// <summary>The concrete SetupPage classes, at any depth of inheritance.</summary>
    public static IList<PageClass> SetupPageClasses()
    {
        var classes = Classes();
        var pages = new HashSet<string> { "SetupPage" };
        for (var grew = true; grew;)
        {
            grew = false;
            foreach (var c in classes)
                if (pages.Contains(c.Base) && pages.Add(c.Name)) grew = true;
        }
        return classes.Where(c => c.Name != "SetupPage" && pages.Contains(c.Name) && !c.IsAbstract).ToList();
    }

    public static IList<string> SetupPages() => SetupPageClasses().Select(c => c.Name).ToList();

    /// <summary>Every [SettingsPage] registration in the plugin, on a class or on the assembly.</summary>
    public static IList<SettingsPageInfo> Registrations()
    {
        var found = new List<SettingsPageInfo>();
        foreach (var (path, text) in Files())
        {
            foreach (Match m in ClassRegistration.Matches(text))
                found.Add(SettingsRegistry.FromAttribute(Read(m.Groups["args"].Value, path, out _), m.Groups["name"].Value));
            foreach (Match m in AssemblyRegistration.Matches(text))
            {
                var attribute = Read(m.Groups["args"].Value, path, out var page);
                Assert.True(page != null, Path.GetFileName(path) + ": an assembly registration must name its page (Page = typeof(...)).");
                found.Add(SettingsRegistry.FromAttribute(attribute, page!));
            }
        }
        Assert.True(found.Count > 0, "No [SettingsPage] registrations were found under src/FieldCodes.Cad.");
        return found;
    }

    private static SettingsPageAttribute Read(string args, string path, out string? page)
    {
        page = null;
        var attribute = new SettingsPageAttribute();
        var matches = Argument.Matches(args).Cast<Match>().ToList();
        var leftover = Argument.Replace(args, string.Empty).Replace(",", string.Empty).Trim();
        Assert.True(leftover.Length == 0, Path.GetFileName(path) + ": write [SettingsPage] arguments as Name = literal; could not read \"" + leftover + "\".");
        foreach (var m in matches)
        {
            var key = m.Groups["key"].Value;
            if (key == "Page") { page = m.Groups["t"].Value.Split('.').Last(); continue; }
            var property = typeof(SettingsPageAttribute).GetProperty(key);
            Assert.True(property != null, Path.GetFileName(path) + ": [SettingsPage] has no " + key + ".");
            property!.SetValue(attribute, m.Groups["n"].Success
                ? int.Parse(m.Groups["n"].Value)
                : Regex.Unescape(m.Groups["s"].Value));
        }
        return attribute;
    }

    /// <summary>Every command name registered by a [CommandMethod] attribute.</summary>
    public static HashSet<string> RegisteredCommands() =>
        new(CommandGroups().SelectMany(g => g), StringComparer.OrdinalIgnoreCase);

    /// <summary>The names each command method registers: consecutive [CommandMethod] lines belong
    /// to one method.</summary>
    public static IList<IList<string>> CommandGroups()
    {
        var groups = new List<IList<string>>();
        foreach (var (_, text) in Files())
        {
            List<string>? current = null;
            foreach (var line in text.Split('\n'))
            {
                var m = CommandAttribute.Match(line);
                if (m.Success)
                {
                    current ??= new List<string>();
                    current.Add(m.Groups[2].Success ? m.Groups[2].Value : m.Groups[1].Value);
                    continue;
                }
                if (current != null && line.Trim().Length > 0) { groups.Add(current); current = null; }
            }
        }
        return groups;
    }

    /// <summary>The body of the method registered as <paramref name="command"/>, braces balanced.
    /// Shallow: what it calls is not followed.</summary>
    public static string MethodBody(string command)
    {
        foreach (var (_, text) in Files())
        {
            var at = text.IndexOf("[CommandMethod(\"" + command + "\"", StringComparison.Ordinal);
            if (at < 0) continue;
            var open = text.IndexOf('{', at);
            var depth = 0;
            for (var i = open; i < text.Length; i++)
            {
                if (text[i] == '{') depth++;
                else if (text[i] == '}' && --depth == 0) return text.Substring(open, i - open + 1);
            }
        }
        Assert.Fail(command + " is not registered under src/FieldCodes.Cad.");
        return string.Empty;
    }
}
