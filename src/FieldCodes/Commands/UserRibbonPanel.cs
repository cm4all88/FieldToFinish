using System;
using System.Collections.Generic;
using System.Linq;
using FieldCodes.Settings;

namespace FieldCodes.Commands
{
    /// <summary>
    /// The person's own "My Commands" panel: the commands they ticked in Settings > Commands,
    /// appended to the FieldToFinish tab when the ribbon is built. It is never part of
    /// <see cref="RibbonLayout"/> -- the office ribbon stays the same for everyone and stays
    /// tested as it is; this panel is per user, built at run time.
    /// </summary>
    public static class UserRibbonPanel
    {
        /// <summary>The panel's title on the ribbon.</summary>
        public const string Title = "My Commands";

        /// <summary>
        /// The panel for these commands, in the order given, each once. A command on the ribbon
        /// keeps its button's name, icon and tooltip; one that is not gets a text button named
        /// after it, described by <paramref name="descriptions"/> when it has an entry. Names that
        /// are not command names are skipped. Null when nothing is left to show.
        /// </summary>
        public static RibbonPanelLayout Build(IEnumerable<string> commands, IDictionary<string, string> descriptions = null)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var buttons = new List<RibbonButtonLayout>();
            foreach (var raw in commands ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                var command = raw.Trim().ToUpperInvariant();
                if (!SettingsRegistry.IsCommandName(command) || !seen.Add(command)) continue;

                var existing = RibbonLayout.AllButtons.FirstOrDefault(b => string.Equals(b.Command, command, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    buttons.Add(new RibbonButtonLayout(existing.Title, command, existing.Icon, RibbonButtonSize.Small, true, existing.Tip));
                    continue;
                }

                string tip;
                if (descriptions == null || !descriptions.TryGetValue(command, out tip) || string.IsNullOrWhiteSpace(tip))
                    tip = "Runs " + command + ", as if typed at the command line.";
                buttons.Add(new RibbonButtonLayout(command, command, null, RibbonButtonSize.Small, true, tip));
            }
            return buttons.Count == 0 ? null : new RibbonPanelLayout(Title, buttons);
        }
    }
}
