using System;
using System.Collections.Generic;
using System.Linq;

namespace CrewUpload.Schedule
{
    /// <summary>
    /// Puts the schedule's files together exactly as the Survey Schedule app does in assembleState()
    /// (Survey Schedule PSO.html, v66), so Crew Upload sees the same board the PMs see. Only the
    /// parts that depend on the app's own open session (its unsaved requests and overrides) are left
    /// out: a reader has none. If assembleState() changes, this changes with it -- nothing here is a
    /// separate idea of how the schedule works.
    /// </summary>
    internal static class ScheduleAssembler
    {
        public static AssembledSchedule Assemble(MasterFile master, IDictionary<string, PmFeed> feeds,
            IEnumerable<AssignmentRecord> requests, IEnumerable<OverrideRecord> overrides)
        {
            var s = new AssembledSchedule { Master = master };

            // for(const pm of s.pms){ ... s.projects.push(...f.projects); s.assignments.push(...f.assignments) }
            foreach (var pm in master.Pms ?? new List<PmRecord>())
            {
                PmFeed f;
                if (pm?.Id == null || !feeds.TryGetValue(pm.Id, out f) || f == null) f = new PmFeed(); // emptyFeed(pm.id)
                s.Projects.AddRange((f.Projects ?? new List<ProjectRecord>()).Where(p => p != null));
                s.Assignments.AddRange((f.Assignments ?? new List<AssignmentRecord>()).Where(a => a != null));
            }

            // s.assignments.push(...finalReq)
            s.Assignments.AddRange((requests ?? Enumerable.Empty<AssignmentRecord>()).Where(r => r != null));

            // cross-PM overrides: newest change per entry wins
            var ovr = new Dictionary<string, OverrideRecord>(StringComparer.Ordinal);
            var order = new List<string>();
            foreach (var o in overrides ?? Enumerable.Empty<OverrideRecord>())
            {
                if (o?.Id == null) continue;
                OverrideRecord cur;
                if (!ovr.TryGetValue(o.Id, out cur)) { ovr[o.Id] = o; order.Add(o.Id); }
                else if (Later(o.At, cur.At)) ovr[o.Id] = o;
            }

            foreach (var o in order.Select(id => ovr[id]))
            {
                if (o.Kind == "proj")
                {
                    if (o.Data == null || o.Data.Type == Newtonsoft.Json.Linq.JTokenType.Null) continue;
                    var data = o.Data.ToObject<ProjectRecord>();
                    var pi = s.Projects.FindIndex(p => p.Id == data.Id);
                    var pcur = pi >= 0 ? s.Projects[pi] : null;
                    if (pcur != null && !Later(o.At, pcur.UpdatedAt ?? "")) continue; // owner's newer settings win
                    if (pi >= 0) s.Projects[pi] = data; else s.Projects.Add(data);
                    continue;
                }
                var idx = s.Assignments.FindIndex(a => a.Id == o.Id);
                var curA = idx >= 0 ? s.Assignments[idx] : null;
                var curAt = curA?.UpdatedAt ?? "";
                if (!Later(o.At, curAt)) continue; // owner has since made a newer edit -- theirs wins
                if (o.Op == "delete") { if (idx >= 0) s.Assignments.RemoveAt(idx); }
                else if (o.Data != null && o.Data.Type != Newtonsoft.Json.Linq.JTokenType.Null)
                {
                    var data = o.Data.ToObject<AssignmentRecord>();
                    if (idx >= 0) s.Assignments[idx] = data; else s.Assignments.Add(data);
                }
            }
            return s;
        }

        /// <summary>JavaScript's a &gt; b on strings: the app compares ISO timestamps as text.</summary>
        private static bool Later(string a, string b) => string.CompareOrdinal(a ?? "", b ?? "") > 0;
    }
}
