using System;
using System.Collections.Generic;
using System.Linq;

namespace FieldCodes.Utilities
{
    /// <summary>
    /// Completing the far end of a connected pipe while walking the network. A connection made from one structure
    /// ("SDMH 1047's N/NW pipe runs to CB 1048") has no pipe at the far end until the drafter enters the one observed
    /// there. Nothing here creates that pipe on its own: the drafter asks for it, fills in this structure's own
    /// measure down, and only then is it added and tied to the connection as its other end.
    /// </summary>
    public static class NetworkCompletion
    {
        /// <summary>Accepted connections that arrive at this structure with no pipe entered here for them yet.</summary>
        public static IList<PipeConnection> Waiting(UtilityProject project, StructureRecord structure)
        {
            if (project == null || structure == null) return new List<PipeConnection>();
            return project.Connections
                .Where(c => c.IsAccepted && c.ToStructureId == structure.Id && c.ToPipeId == null &&
                            project.Pipe(c.FromStructureId, c.FromPipeId) != null)
                .ToList();
        }

        /// <summary>The Add pipe entry to start the far end with: opposite direction, size and material, marked as copied.</summary>
        public static QuickPipeEntry Prefill(UtilityProject project, PipeConnection connection)
        {
            var from = project.Structure(connection.FromStructureId);
            var pipe = project.Pipe(connection.FromStructureId, connection.FromPipeId);
            return QuickPipeEntry.ForOtherEnd(pipe, from != null ? from.Label : null);
        }

        /// <summary>
        /// Adds the drafter's pipe at this structure and records it as the far end of the connection. Returns null
        /// when done, or why it was not (the connection changed, or already has its far end); in that case nothing
        /// is added. The connection's status, basis and confidence are kept; one line and one override say what the
        /// drafter did.
        /// </summary>
        public static string Complete(UtilityProject project, string connectionId, string structureId, PipeObservation pipe)
        {
            var c = project.Connections.FirstOrDefault(x => x.Id == connectionId);
            var structure = project.Structure(structureId);
            if (c == null || structure == null || pipe == null) return "The connection is no longer there.";
            if (!c.IsAccepted || c.ToStructureId != structureId) return "The connection no longer runs to " + structure.Label + ".";
            if (c.ToPipeId != null) return "That connection already has its pipe at " + structure.Label + ".";
            var from = project.Structure(c.FromStructureId);
            var source = project.Pipe(c.FromStructureId, c.FromPipeId);
            if (from == null || source == null) return "The pipe it comes from is no longer there.";

            structure.Field.Pipes.Add(pipe);
            c.ToPipeId = pipe.Id;
            c.Basis.Add("Other end entered at " + structure.Label + " by the drafter (completed from " + from.Label +
                        "); its measure down is " + structure.Label + "'s own observation");
            project.Overrides.Add(new ManualOverride
            {
                Target = c.Id,
                What = "Pipe completed from connected pipe",
                Generated = from.Label + ": " + ConnectionFinder.Describe(source),
                Entered = structure.Label + ": " + ConnectionFinder.Describe(pipe) +
                          (pipe.Prefilled != null && pipe.Prefilled.Count > 0 ? " (copied: " + string.Join(", ", pipe.Prefilled.ToArray()) + ")" : string.Empty),
                Utc = DateTime.UtcNow
            });
            return null;
        }
    }
}
