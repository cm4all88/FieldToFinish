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
