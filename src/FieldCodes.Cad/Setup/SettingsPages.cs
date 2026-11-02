using System;
using System.Collections.Generic;
using System.Linq;
using FieldCodes.Settings;

namespace FieldCodes.Cad.Setup
{
    /// <summary>
    /// The Settings pages this plugin registers (<see cref="SettingsPageAttribute"/> on each
    /// <see cref="SetupPage"/>, or on the assembly), read once by reflection. The Settings window
    /// builds its navigation from here, and Settings > Commands links commands to pages from here,
    /// so a page for a new command shows up in both without either being edited.
    /// </summary>
    internal static class SettingsPages
    {
        private static IList<SettingsPageInfo> _all;
        private static IList<string> _problems;

        /// <summary>Every registered page, listed or not, each once (a page registered twice
        /// keeps its first registration; the duplicate is in <see cref="Problems"/>).</summary>
        public static IList<SettingsPageInfo> All
        {
            get
            {
                if (_all == null) Read();
                return _all;
            }
        }

        /// <summary>What is wrong with the registrations, in words: unregistered pages,
        /// duplicate labels, unknown sections. Empty when all is well.</summary>
        public static IList<string> Problems
        {
            get
            {
                if (_problems == null) Read();
                return _problems;
            }
        }

        /// <summary>Group captions and pages, in the order the window shows them.</summary>
        public static IList<SettingsNavEntry> Navigation()
        {
            return SettingsRegistry.Arrange(All);
        }

        /// <summary>A new instance of the registered page.</summary>
        public static SetupPage Create(SettingsPageInfo page)
        {
            if (page == null || page.PageType == null) throw new ArgumentException("The page has no class to create.");
            return (SetupPage)Activator.CreateInstance(page.PageType, true);
        }

        private static void Read()
        {
            var problems = new List<string>();
            var assembly = typeof(SetupPage).Assembly;
            var found = SettingsRegistry.Discover(assembly, typeof(SetupPage), problems);

            foreach (var missing in SettingsRegistry.Unregistered(assembly, typeof(SetupPage)))
                problems.Add(missing.Name + " has no [SettingsPage] registration, so it is not in the window.");
            SettingsRegistry.Validate(found, problems);

            // One label, one page: the window keys its built pages by label.
            var labels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var types = new HashSet<Type>();
            _all = found.Where(p => p.Label.Length > 0 && labels.Add(p.Label) && types.Add(p.PageType)).ToList();
            _problems = problems;
        }
    }
}
