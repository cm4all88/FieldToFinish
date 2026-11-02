using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;

namespace FieldCodes.Settings
{
    /// <summary>
    /// Registers a Settings page, in one place, on the page itself:
    ///
    ///     [SettingsPage(Group = "FieldToFinish", Label = "Tags", Order = 90,
    ///                   Sections = "tags", Commands = "FTFTAGS")]
    ///     internal sealed class TagsPage : SetupPage
    ///
    /// The Settings window builds its navigation from these: a page with this attribute appears
    /// under its group at its order, and nothing else has to be edited. A page whose file cannot
    /// carry the attribute is registered from the assembly instead, naming the page:
    ///
    ///     [assembly: SettingsPage(Page = typeof(FieldCodes.Cad.Setup.LayersPage), Group = ..., ...)]
    ///
    /// Write every argument as a plain literal (a string, a number, typeof(...)): the tests read
    /// these from the source, since the Civil 3D project cannot be loaded without Civil 3D.
    /// docs/settings-registry.md has the whole contract.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
    public sealed class SettingsPageAttribute : Attribute
    {
        /// <summary>The workflow group the page sits under: FieldToFinish, Survey Drafting,
        /// Boundary and Easements, Administration, or a new group's name.</summary>
        public string Group { get; set; }

        /// <summary>The page's name in the navigation, and the key other pages navigate by.</summary>
        public string Label { get; set; }

        /// <summary>Place within the group, smallest first. Ties are broken by label.</summary>
        public int Order { get; set; }

        /// <summary>The settings sections the page edits, comma separated, by their key in
        /// ftf-settings.json ("tags", "annotation"). Empty for a page that edits no section.</summary>
        public string Sections { get; set; }

        /// <summary>The commands whose behaviour the page changes, comma separated, exactly as
        /// typed. "*" for a page every command follows (General, Layers).</summary>
        public string Commands { get; set; }

        /// <summary>Labels the page had before, comma separated, so links to the old name still land.</summary>
        public string Aliases { get; set; }

        /// <summary>When set, the page is registered but not shown in the navigation, and this says why.</summary>
        public string Unlisted { get; set; }

        /// <summary>The page class, for a registration on the assembly. Ignored on a class.</summary>
        public Type Page { get; set; }
    }

    /// <summary>One registered Settings page, read from its <see cref="SettingsPageAttribute"/>.</summary>
    public sealed class SettingsPageInfo
    {
        public SettingsPageInfo(string pageName, Type pageType, string group, string label, int order,
                                IEnumerable<string> sections, IEnumerable<string> commands, bool allCommands,
                                IEnumerable<string> aliases, string unlisted)
        {
            PageName = pageName ?? (pageType != null ? pageType.Name : string.Empty);
            PageType = pageType;
            Group = SettingsRegistry.CanonicalGroup(group);
            Label = (label ?? string.Empty).Trim();
            Order = order;
            Sections = new ReadOnlyCollection<string>((sections ?? Enumerable.Empty<string>()).ToList());
            Commands = new ReadOnlyCollection<string>((commands ?? Enumerable.Empty<string>()).ToList());
            AllCommands = allCommands;
            Aliases = new ReadOnlyCollection<string>((aliases ?? Enumerable.Empty<string>()).ToList());
            Unlisted = string.IsNullOrWhiteSpace(unlisted) ? null : unlisted.Trim();
        }

        /// <summary>The page class's name.</summary>
        public string PageName { get; private set; }

        /// <summary>The page class, or null when the registration was read from source text.</summary>
        public Type PageType { get; private set; }

        public string Group { get; private set; }
        public string Label { get; private set; }
        public int Order { get; private set; }

        /// <summary>Settings section keys (ftf-settings.json) the page edits.</summary>
        public IList<string> Sections { get; private set; }

        /// <summary>Commands named by the registration (without the "*").</summary>
        public IList<string> Commands { get; private set; }

        /// <summary>The page changes every command (General, Layers, Profiles).</summary>
        public bool AllCommands { get; private set; }

        public IList<string> Aliases { get; private set; }

        /// <summary>Why the page is kept out of the navigation; null for a listed page.</summary>
        public string Unlisted { get; private set; }

        public bool IsListed { get { return Unlisted == null; } }

        /// <summary>True when the registration names the command (not merely "every command").</summary>
        public bool Names(string command)
        {
            return command != null && Commands.Contains(command.Trim(), StringComparer.OrdinalIgnoreCase);
        }

        public bool Edits(string sectionKey)
        {
            return sectionKey != null && Sections.Contains(sectionKey.Trim(), StringComparer.OrdinalIgnoreCase);
        }

        public override string ToString() { return Group + " > " + Label + " (" + PageName + ")"; }
    }

    /// <summary>One line of the Settings navigation: a group caption or a page.</summary>
    public sealed class SettingsNavEntry
    {
        internal SettingsNavEntry(string group, string caption, SettingsPageInfo page)
        {
            Group = group;
            Caption = caption;
            Page = page;
        }

        /// <summary>The group's name ("Survey Drafting").</summary>
        public string Group { get; private set; }

        /// <summary>What the navigation shows: the group in capitals, or the page's label.</summary>
        public string Caption { get; private set; }

        /// <summary>The page, or null for a group caption.</summary>
        public SettingsPageInfo Page { get; private set; }

        public bool IsGroup { get { return Page == null; } }

        public override string ToString() { return Caption; }
    }

    /// <summary>
    /// The Settings pages and how the window arranges them: groups in a fixed order (the four
    /// kinds of work, then any group a newer page names), pages by their order within a group.
    /// Pages register themselves with <see cref="SettingsPageAttribute"/>; this class only reads
    /// and arranges the registrations, so a new command's page needs no edit here.
    /// </summary>
    public static class SettingsRegistry
    {
        public const string FieldToFinish = "FieldToFinish";
        public const string SurveyDrafting = "Survey Drafting";
        public const string BoundaryAndEasements = "Boundary and Easements";
        public const string Administration = "Administration";

        /// <summary>The group a page with no group goes under (and is reported for).</summary>
        public const string OtherGroup = "Other";

        private static readonly ReadOnlyCollection<string> GroupList = new ReadOnlyCollection<string>(
            new[] { FieldToFinish, SurveyDrafting, BoundaryAndEasements, Administration });

        /// <summary>The groups, in the order the navigation shows them. A group not listed here
        /// follows them, alphabetically.</summary>
        public static IList<string> Groups { get { return GroupList; } }

        private static readonly ReadOnlyDictionary<string, string> NoPage =
            new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        /// <summary>
        /// Settings sections deliberately edited by no page, by key, with the reason. Every other
        /// section in <see cref="FtfSettings.Sections"/> must be edited by a registered page; the
        /// tests hold that. Empty today.
        /// </summary>
        public static IDictionary<string, string> SectionsWithoutPage { get { return NoPage; } }

        private static readonly ReadOnlyCollection<string> FollowUps = new ReadOnlyCollection<string>(
            new[] { "FTFLINES", "FTFKEYMAP", "FTFEASEMENTEXPORT" });

        /// <summary>
        /// Commands a saved workflow may run after its stages. Only commands that never stop to
        /// ask anything belong here: they are queued on the command line once the run ends, and a
        /// prompt nobody expects would sit there waiting. FTFLEGEND and FTFCONTROL are not here --
        /// each asks for a corner when there is no legend or table yet.
        /// </summary>
        public static IList<string> WorkflowFollowUps { get { return FollowUps; } }

        // ------------------------------------------------------------------ reading

        /// <summary>The registration an attribute on <paramref name="pageType"/> makes.</summary>
        public static SettingsPageInfo FromAttribute(SettingsPageAttribute attribute, Type pageType)
        {
            if (attribute == null) throw new ArgumentNullException("attribute");
            return Read(attribute, pageType != null ? pageType.Name : null, pageType);
        }

        /// <summary>The registration an attribute makes, for a page known only by its class name
        /// (read from source text).</summary>
        public static SettingsPageInfo FromAttribute(SettingsPageAttribute attribute, string pageName)
        {
            if (attribute == null) throw new ArgumentNullException("attribute");
            return Read(attribute, pageName, null);
        }

        private static SettingsPageInfo Read(SettingsPageAttribute a, string pageName, Type pageType)
        {
            var commands = Split(a.Commands);
            var all = commands.Contains("*");
            return new SettingsPageInfo(pageName, pageType, a.Group, a.Label, a.Order, Split(a.Sections),
                commands.Where(c => c != "*").Select(c => c.ToUpperInvariant()).Distinct().ToList(),
                all, Split(a.Aliases), a.Unlisted);
        }

        /// <summary>
        /// Every page registered in <paramref name="assembly"/>: classes deriving from
        /// <paramref name="pageBase"/> with the attribute, and assembly-level registrations naming
        /// one. A registration that names something that is not a page is reported, not used.
        /// </summary>
        public static IList<SettingsPageInfo> Discover(Assembly assembly, Type pageBase, ICollection<string> problems = null)
        {
            if (assembly == null) throw new ArgumentNullException("assembly");
            if (pageBase == null) throw new ArgumentNullException("pageBase");

            var found = new List<SettingsPageInfo>();
            foreach (var type in LoadableTypes(assembly))
            {
                object[] attributes;
                try { attributes = type.GetCustomAttributes(typeof(SettingsPageAttribute), false); }
                catch (Exception) { continue; }
                if (attributes.Length == 0) continue;

                if (!IsPage(type, pageBase))
                {
                    if (problems != null) problems.Add(type.Name + " is registered as a Settings page but is not one.");
                    continue;
                }
                foreach (SettingsPageAttribute a in attributes) found.Add(FromAttribute(a, type));
            }

            foreach (SettingsPageAttribute a in assembly.GetCustomAttributes(typeof(SettingsPageAttribute), false))
            {
                if (a.Page == null)
                {
                    if (problems != null) problems.Add("An assembly registration for \"" + a.Label + "\" does not name its page (Page = typeof(...)).");
                    continue;
                }
                if (!IsPage(a.Page, pageBase))
                {
                    if (problems != null) problems.Add(a.Page.Name + " is registered as a Settings page but is not one.");
                    continue;
                }
                found.Add(FromAttribute(a, a.Page));
            }
            return found;
        }

        /// <summary>Pages in <paramref name="assembly"/> that no registration mentions: they would
        /// never appear in the window.</summary>
        public static IList<Type> Unregistered(Assembly assembly, Type pageBase)
        {
            var registered = new HashSet<Type>(Discover(assembly, pageBase).Select(p => p.PageType));
            return LoadableTypes(assembly).Where(t => IsPage(t, pageBase) && !registered.Contains(t))
                                          .OrderBy(t => t.Name, StringComparer.Ordinal).ToList();
        }

        private static bool IsPage(Type type, Type pageBase)
        {
            return type != pageBase && pageBase.IsAssignableFrom(type) && !type.IsAbstract;
        }

        private static IEnumerable<Type> LoadableTypes(Assembly assembly)
        {
            try { return assembly.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t != null).ToArray(); }
        }

        // ---------------------------------------------------------------- arranging

        /// <summary>A known group's own spelling for any casing of it; another name trimmed; no
        /// name at all becomes <see cref="OtherGroup"/>.</summary>
        public static string CanonicalGroup(string group)
        {
            if (string.IsNullOrWhiteSpace(group)) return OtherGroup;
            var trimmed = group.Trim();
            foreach (var known in GroupList)
                if (string.Equals(known, trimmed, StringComparison.OrdinalIgnoreCase)) return known;
            return trimmed;
        }

        /// <summary>
        /// The navigation: each group's caption followed by its pages. Groups in
        /// <see cref="Groups"/> order, any other group after them alphabetically; pages by order,
        /// then label. Unlisted pages are left out.
        /// </summary>
        public static IList<SettingsNavEntry> Arrange(IEnumerable<SettingsPageInfo> pages)
        {
            var nav = new List<SettingsNavEntry>();
            var groups = (pages ?? Enumerable.Empty<SettingsPageInfo>())
                .Where(p => p != null && p.IsListed)
                .GroupBy(p => p.Group, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => GroupRank(g.Key))
                .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

            foreach (var group in groups)
            {
                nav.Add(new SettingsNavEntry(group.Key, group.Key.ToUpperInvariant(), null));
                foreach (var page in group.OrderBy(p => p.Order).ThenBy(p => p.Label, StringComparer.OrdinalIgnoreCase))
                    nav.Add(new SettingsNavEntry(group.Key, page.Label, page));
            }
            return nav;
        }

        private static int GroupRank(string group)
        {
            var index = GroupList.IndexOf(group);
            return index >= 0 ? index : GroupList.Count;
        }

        // --------------------------------------------------------------- checking

        /// <summary>
        /// Problems with a set of registrations, in words: a page without a label or group, two
        /// pages (or a page and an earlier name) with one label, a page registered twice, a
        /// section key ftf-settings.json does not have, a command name that is not one.
        /// </summary>
        public static void Validate(IEnumerable<SettingsPageInfo> pages, ICollection<string> problems)
        {
            if (problems == null) throw new ArgumentNullException("problems");
            var list = (pages ?? Enumerable.Empty<SettingsPageInfo>()).Where(p => p != null).ToList();
            var keys = new HashSet<string>(SectionKeys(), StringComparer.OrdinalIgnoreCase);
            var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var page in list)
            {
                var who = string.IsNullOrEmpty(page.PageName) ? "A page" : page.PageName;
                if (page.Label.Length == 0) problems.Add(who + " is registered without a label.");
                if (page.IsListed && page.Group == OtherGroup)
                    problems.Add(who + " is registered without a group.");

                foreach (var name in new[] { page.Label }.Concat(page.Aliases))
                {
                    if (name.Length == 0) continue;
                    string other;
                    if (labels.TryGetValue(name, out other))
                        problems.Add("\"" + name + "\" names both " + other + " and " + who + ".");
                    else
                        labels[name] = who;
                }

                foreach (var key in page.Sections)
                    if (!keys.Contains(key))
                        problems.Add(who + " edits \"" + key + "\", which is not a section of ftf-settings.json.");

                foreach (var command in page.Commands)
                    if (!IsCommandName(command))
                        problems.Add(who + " names \"" + command + "\", which is not a command name.");
            }

            foreach (var twice in list.Where(p => !string.IsNullOrEmpty(p.PageName))
                                      .GroupBy(p => p.PageName, StringComparer.Ordinal).Where(g => g.Count() > 1))
                problems.Add(twice.Key + " is registered " + twice.Count() + " times.");
        }

        /// <summary>A command as it is typed: capital letters and digits, starting with a letter.</summary>
        public static bool IsCommandName(string command)
        {
            if (string.IsNullOrEmpty(command) || command[0] < 'A' || command[0] > 'Z') return false;
            return command.All(c => (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'));
        }

        // ---------------------------------------------------------------- sections

        /// <summary>Every section's key in ftf-settings.json ("general", "lineLabels"...).</summary>
        public static IList<string> SectionKeys()
        {
            return SectionProperties().Select(p => p.Key).ToList();
        }

        /// <summary>The key <paramref name="section"/> is saved under in <paramref name="settings"/>,
        /// or null when it is not one of its sections.</summary>
        public static string SectionKey(FtfSettings settings, ISettingsSection section)
        {
            if (settings == null || section == null) return null;
            foreach (var pair in SectionProperties())
                if (ReferenceEquals(pair.Value.GetValue(settings, null), section)) return pair.Key;
            return null;
        }

        private static IEnumerable<KeyValuePair<string, PropertyInfo>> SectionProperties()
        {
            foreach (var property in typeof(FtfSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!typeof(ISettingsSection).IsAssignableFrom(property.PropertyType)) continue;
                var json = property.GetCustomAttributes(typeof(JsonPropertyAttribute), true)
                                   .OfType<JsonPropertyAttribute>().FirstOrDefault();
                if (json == null || string.IsNullOrEmpty(json.PropertyName)) continue;
                yield return new KeyValuePair<string, PropertyInfo>(json.PropertyName, property);
            }
        }

        /// <summary>A comma- or semicolon-separated list, trimmed, blanks dropped.</summary>
        public static IList<string> Split(string list)
        {
            if (string.IsNullOrWhiteSpace(list)) return new List<string>();
            return list.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                       .Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        }
    }
}
