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
            // (fs0.overrides||[]).forEach(o=>{if(!ovr[o.id]||o.at>ovr[o.id].at)ovr[o.id]=o;});
            var ovr = new Dictionary<string, OverrideRecord>(StringComparer.Ordinal);
            var order = new List<string>();
            foreach (var o in overrides ?? Enumerable.Empty<OverrideRecord>())
            {
                if (o?.Id == null) continue;
                OverrideRecord cur;
                if (!ovr.TryGetValue(o.Id, out cur)) { ovr[o.Id] = o; order.Add(o.Id); }
                else if (JsGreater(o.At, cur.At)) ovr[o.Id] = o;
            }

            // Object.values(ovr).forEach(...) -- in JavaScript's key order
            foreach (var o in JsKeyOrder(order).Select(id => ovr[id]))
            {
                if (o.Kind == "proj")
                {
                    if (IsFalsy(o.Data)) continue;                                   // if(!o.data)return;
                    var data = o.Data.ToObject<ProjectRecord>();
                    var pi = s.Projects.FindIndex(p => p.Id == data.Id);
                    var pcur = pi >= 0 ? s.Projects[pi] : null;
                    if (pcur != null && JsLessOrEqual(o.At, pcur.UpdatedAt ?? "")) continue; // owner's newer settings win
                    if (pi >= 0) s.Projects[pi] = data; else s.Projects.Add(data);
                    s.AppliedOverrides.Add(o.Id);
                    continue;
                }
                var idx = s.Assignments.FindIndex(a => a.Id == o.Id);
                var curA = idx >= 0 ? s.Assignments[idx] : null;
                var curAt = curA?.UpdatedAt ?? "";                                   // (cur&&cur.updatedAt)||""
                if (JsLessOrEqual(o.At, curAt)) continue;                            // owner has since made a newer edit -- theirs wins
                if (o.Op == "delete") { if (idx >= 0) { s.Assignments.RemoveAt(idx); s.AppliedOverrides.Add(o.Id); } }
                else if (!IsFalsy(o.Data))
                {
                    var data = o.Data.ToObject<AssignmentRecord>();
                    if (idx >= 0) s.Assignments[idx] = data; else s.Assignments.Add(data);
                    s.AppliedOverrides.Add(o.Id);
                }
            }
            return s;
        }

        // ---- JavaScript's semantics, which the app relies on ----

        /// <summary>a &gt; b in JavaScript for the app's timestamps: strings compare by UTF-16 code units; a missing value compares false.</summary>
        internal static bool JsGreater(string a, string b) => a != null && b != null && string.CompareOrdinal(a, b) > 0;

        /// <summary>a &lt;= b in JavaScript: false when either is missing (undefined &lt;= "" is false, so the override applies).</summary>
        internal static bool JsLessOrEqual(string a, string b) => a != null && b != null && string.CompareOrdinal(a, b) <= 0;

        /// <summary>!value in JavaScript for the override's data: missing, null, false, 0 or "".</summary>
        private static bool IsFalsy(Newtonsoft.Json.Linq.JToken t)
        {
            if (t == null) return true;
            switch (t.Type)
            {
                case Newtonsoft.Json.Linq.JTokenType.Null:
                case Newtonsoft.Json.Linq.JTokenType.Undefined: return true;
                case Newtonsoft.Json.Linq.JTokenType.Boolean: return !(bool)t;
                case Newtonsoft.Json.Linq.JTokenType.Integer:
                case Newtonsoft.Json.Linq.JTokenType.Float: return (double)t == 0;
                case Newtonsoft.Json.Linq.JTokenType.String: return ((string)t).Length == 0;
                default: return false;
            }
        }

        /// <summary>
        /// The order Object.values() visits keys: integer-like keys ("12") first, ascending, then the
        /// rest in the order they were added.
        /// </summary>
        internal static IEnumerable<string> JsKeyOrder(List<string> keys)
        {
            Func<string, bool> isIndex = k =>
            {
                uint n;
                return k.Length > 0 && (k == "0" || k[0] != '0') && k.All(char.IsDigit)
                    && uint.TryParse(k, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out n) && n < uint.MaxValue;
            };
            return keys.Where(isIndex).OrderBy(k => uint.Parse(k, System.Globalization.CultureInfo.InvariantCulture))
                .Concat(keys.Where(k => !isIndex(k)));
        }

    }
}
