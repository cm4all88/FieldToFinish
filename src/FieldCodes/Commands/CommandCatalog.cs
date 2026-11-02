using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using FieldCodes.Annotation;
using FieldCodes.Settings;

namespace FieldCodes.Commands
{
    /// <summary>One FTF command, as Settings > Commands lists it.</summary>
    public sealed class CommandEntry
    {
        internal CommandEntry(string command, IList<string> aliases, string title, string description,
                              IList<string> ribbonPlaces, IList<string> settingsPages, int stageNumber, bool canFollowWorkflow)
        {
            Command = command;
            Aliases = new ReadOnlyCollection<string>(aliases);
            Title = title ?? string.Empty;
            Description = description ?? string.Empty;
            RibbonPlaces = new ReadOnlyCollection<string>(ribbonPlaces);
            SettingsPages = new ReadOnlyCollection<string>(settingsPages);
            StageNumber = stageNumber;
            CanFollowWorkflow = canFollowWorkflow;
        }

        /// <summary>The command as it is typed.</summary>
        public string Command { get; private set; }

        /// <summary>Other names the same command answers to (FTFL for FTFLABELLINE).</summary>
        public IList<string> Aliases { get; private set; }

        /// <summary>Its ribbon button's name, on one line; empty when it is not on the ribbon.</summary>
        public string Title { get; private set; }

        /// <summary>What it does: the ribbon tooltip, or the processing stage's description.</summary>
        public string Description { get; private set; }

        /// <summary>Where it is on the ribbon ("FieldToFinish > Labels"), once per tab it sits on.</summary>
        public IList<string> RibbonPlaces { get; private set; }

        /// <summary>Labels of the Settings pages that name this command.</summary>
        public IList<string> SettingsPages { get; private set; }

        /// <summary>Its position among the processing stages (1 = first), or 0 when it is not one.</summary>
        public int StageNumber { get; private set; }

        public bool IsStage { get { return StageNumber > 0; } }

        /// <summary>A saved workflow may queue it after its stages.</summary>
        public bool CanFollowWorkflow { get; private set; }

        public bool OnRibbon { get { return RibbonPlaces.Count > 0; } }

        /// <summary>True when the text appears in the command, an alias, the title, the description,
        /// a ribbon place or a settings page (ignoring case). Empty text matches everything.</summary>
        public bool Matches(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return true;
            var t = text.Trim();
            return new[] { Command, Title, Description }.Concat(Aliases).Concat(RibbonPlaces).Concat(SettingsPages)
                .Any(s => s != null && s.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        public override string ToString() { return Command; }
    }

    /// <summary>
    /// Every FTF command in one list: what it does, where it is on the ribbon, the Settings pages
    /// that change it, and whether it is a processing stage. Built from what the plugin registers
    /// (its [CommandMethod] attributes, read at run time) and from the Settings page registry, so a
    /// new command or page appears without this class changing.
    /// </summary>
    public static class CommandCatalog
    {
        /// <summary>
        /// The catalog. <paramref name="registered"/> holds, per command method, the names it is
        /// registered under; the name on the ribbon (or the first) is the command, the rest its
        /// aliases. A command on the ribbon but not in <paramref name="registered"/> is still listed.
        /// <paramref name="descriptions"/> describes commands the ribbon does not (the processing
        /// stages' descriptions). Ribbon commands come first, in ribbon order; the rest alphabetically.
        /// </summary>
        public static IList<CommandEntry> Build(IEnumerable<IEnumerable<string>> registered,
                                                IEnumerable<SettingsPageInfo> pages,
                                                IDictionary<string, string> descriptions = null)
        {
            var pageList = (pages ?? Enumerable.Empty<SettingsPageInfo>()).Where(p => p != null && p.IsListed).ToList();
            var onRibbon = new HashSet<string>(RibbonLayout.Commands, StringComparer.OrdinalIgnoreCase);

            // Command -> its aliases, one entry per method.
            var groups = new List<List<string>>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var names in registered ?? Enumerable.Empty<IEnumerable<string>>())
            {
                var list = (names ?? Enumerable.Empty<string>())
                    .Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim().ToUpperInvariant())
                    .Where(n => seen.Add(n)).ToList();
                if (list.Count == 0) continue;
                var primary = list.FirstOrDefault(onRibbon.Contains) ?? list[0];
                list.Remove(primary);
                list.Insert(0, primary);
                groups.Add(list);
            }
            foreach (var command in RibbonLayout.Commands)
                if (seen.Add(command)) groups.Add(new List<string> { command });

            var ribbonOrder = RibbonLayout.Commands.Select((c, i) => new { c, i })
                .ToDictionary(x => x.c, x => x.i, StringComparer.OrdinalIgnoreCase);

            var entries = new List<CommandEntry>();
            foreach (var group in groups)
            {
                var command = group[0];
                var aliases = group.Skip(1).ToList();
                var buttons = RibbonLayout.Tabs
                    .SelectMany(t => t.Panels.SelectMany(p => p.Buttons.Where(b => string.Equals(b.Command, command, StringComparison.OrdinalIgnoreCase))
                                                                         .Select(b => new { Tab = t, Panel = p, Button = b })))
                    .ToList();

                string description = null;
                if (buttons.Count > 0) description = buttons[0].Button.Tip;
                else if (descriptions != null) descriptions.TryGetValue(command, out description);

                var places = buttons.Select(x => x.Tab.Title + " > " + x.Panel.Title).Distinct().ToList();
                var settings = pageList.Where(p => group.Any(p.Names)).Select(p => p.Label).Distinct().ToList();
                var stage = ProcessStage.InOrder.IndexOf(command) + 1;

                entries.Add(new CommandEntry(command, aliases, buttons.Count > 0 ? buttons[0].Button.Title : string.Empty,
                    description, places, settings, stage, SettingsRegistry.WorkflowFollowUps.Contains(command)));
            }

            int position;
            return entries
                .OrderBy(e => ribbonOrder.TryGetValue(e.Command, out position) ? position : int.MaxValue)
                .ThenBy(e => e.Command, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>Labels of the listed pages every command follows (General, Layers, Profiles).</summary>
        public static IList<string> PagesForEveryCommand(IEnumerable<SettingsPageInfo> pages)
        {
            return SettingsRegistry.Arrange(pages).Where(e => !e.IsGroup && e.Page.AllCommands).Select(e => e.Page.Label).ToList();
        }
    }
}
