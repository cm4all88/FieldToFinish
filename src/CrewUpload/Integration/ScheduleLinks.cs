using System;
using System.Collections.Generic;
using System.Linq;

namespace CrewUpload.Integration
{
    /// <summary>
    /// Helps a PM link a registered project to its Survey Schedule project. It only suggests: the PM
    /// always makes the choice, and nothing is linked without them.
    /// </summary>
    public static class ScheduleLinks
    {
        /// <summary>
        /// Schedule projects whose number is exactly this full 3-4-3 number (the client-task tail
        /// alone is not unique across prefixes, so it never counts). Empty when none, or the number
        /// is not a full one.
        /// </summary>
        public static List<ScheduledProject> Suggest(string fullNumber, IEnumerable<ScheduledProject> projects)
        {
            var key = ProjectRegistry.KeyFor(fullNumber);
            if (key == null || projects == null) return new List<ScheduledProject>();
            return projects.Where(p => p != null && p.Id != null && ProjectRegistry.KeyFor(p.JobNumber ?? "") == key)
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>The registered projects linked to a schedule project (normally one).</summary>
        public static List<ProjectRegistration> LinkedTo(RegistrySnapshot snapshot, string scheduleProjectId)
        {
            if (snapshot == null || string.IsNullOrEmpty(scheduleProjectId)) return new List<ProjectRegistration>();
            return snapshot.Projects.Where(p => p.Active && p.ScheduleProjectId == scheduleProjectId).ToList();
        }
    }
}

namespace CrewUpload.Integration
{
    /// <summary>One entry in a "schedule person" picker: shows the name, stores the id.</summary>
    public sealed class EmployeeChoice
    {
        public EmployeeChoice(string id, string display) { Id = id; Display = display; }

        /// <summary>The stable schedule employee id ("jeff_bearson"), or "" for not linked. What is saved.</summary>
        public string Id { get; }

        /// <summary>What the PM sees: "Jeff Bearson".</summary>
        public string Display { get; }

        public override string ToString() => Display;
    }

    public static class EmployeeChoices
    {
        public const string NotLinked = "(not linked)";

        /// <summary>
        /// "(not linked)", then the schedule's active people by name, then any ids already saved that
        /// the schedule no longer lists (shown as the id, the only name there is). Names the schedule
        /// has twice get their id added so the PM can tell them apart. Every Display is unique.
        /// </summary>
        public static List<EmployeeChoice> Build(IEnumerable<ScheduledEmployee> employees, IEnumerable<string> savedIds)
        {
            var list = new List<EmployeeChoice> { new EmployeeChoice(string.Empty, NotLinked) };
            var people = (employees ?? Enumerable.Empty<ScheduledEmployee>()).Where(e => e != null && !string.IsNullOrEmpty(e.Id))
                .GroupBy(e => e.Id).Select(g => g.First()).ToList();
            var saved = new HashSet<string>((savedIds ?? Enumerable.Empty<string>()).Where(i => !string.IsNullOrEmpty(i)), StringComparer.Ordinal);
            var shown = people.Where(e => e.Active || saved.Contains(e.Id)).ToList();
            var dupes = new HashSet<string>(shown.GroupBy(e => (e.Name ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1).Select(g => g.Key), StringComparer.OrdinalIgnoreCase);
            foreach (var e in shown.OrderBy(e => e.Name ?? e.Id, StringComparer.OrdinalIgnoreCase))
            {
                var name = string.IsNullOrWhiteSpace(e.Name) ? e.Id : e.Name.Trim();
                if (dupes.Contains(name)) name += " (" + e.Id + ")";
                if (!e.Active) name += " (inactive)";
                list.Add(new EmployeeChoice(e.Id, name));
            }
            foreach (var id in saved.Where(i => people.All(e => e.Id != i)).OrderBy(i => i, StringComparer.Ordinal))
                list.Add(new EmployeeChoice(id, id + " (not in the schedule now)"));
            // every text is unique, so the text alone identifies the person
            foreach (var g in list.GroupBy(c => c.Display, StringComparer.Ordinal).Where(g => g.Count() > 1).ToList())
                foreach (var c in g.Skip(1).ToList())
                    list[list.IndexOf(c)] = new EmployeeChoice(c.Id, c.Display + " [" + c.Id + "]");
            return list;
        }
    }
}
