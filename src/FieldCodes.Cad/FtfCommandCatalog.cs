using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Autodesk.AutoCAD.Runtime;
using FieldCodes.Commands;

namespace FieldCodes.Cad
{
    /// <summary>
    /// The commands this plugin registers, read from its own [CommandMethod] attributes at run
    /// time, so a command added in code is in Settings > Commands (and can go on My Commands)
    /// without being listed anywhere. Commands flagged NoHistory are internal plumbing (the Dip
    /// Builder's and the comparison's queued actions) and are left out.
    /// </summary>
    internal static class FtfCommandCatalog
    {
        private static IList<IList<string>> _registered;

        /// <summary>The names each command method registers, the method's names together.</summary>
        public static IList<IList<string>> Registered()
        {
            if (_registered != null) return _registered;

            var found = new List<IList<string>>();
            Type[] types;
            try { types = typeof(FtfCommandCatalog).Assembly.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray(); }

            foreach (var type in types)
            {
                MethodInfo[] methods;
                try { methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly); }
                catch (System.Exception) { continue; }

                foreach (var method in methods)
                {
                    object[] attributes;
                    try { attributes = method.GetCustomAttributes(typeof(CommandMethodAttribute), false); }
                    catch (System.Exception) { continue; }
                    if (attributes.Length == 0) continue;

                    var commands = attributes.Cast<CommandMethodAttribute>().ToList();
                    if (commands.Any(a => (a.Flags & CommandFlags.NoHistory) != 0)) continue;
                    var names = commands.Select(a => a.GlobalName).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
                    if (names.Count > 0) found.Add(names);
                }
            }
            _registered = found;
            return _registered;
        }

        /// <summary>Every registered name, aliases included.</summary>
        public static ISet<string> Names()
        {
            return new HashSet<string>(Registered().SelectMany(n => n), StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>What the processing stages do, for commands the ribbon does not describe.</summary>
        public static IDictionary<string, string> Descriptions()
        {
            var descriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var step in new FtfPipeline().Steps)
                    descriptions[step.Name] = step.Title + ": " + step.Description.TrimEnd('.') + ".";
            }
            catch (System.Exception) { }
            return descriptions;
        }

        /// <summary>Settings > Commands' list: every command, its description, ribbon place,
        /// settings pages and whether it is a processing stage.</summary>
        public static IList<CommandEntry> Entries()
        {
            return CommandCatalog.Build(Registered().Select(n => (IEnumerable<string>)n), Setup.SettingsPages.All, Descriptions());
        }
    }
}
