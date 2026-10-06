using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CrewUpload.Schedule
{
    /// <summary>
    /// Writes "a daily report was filed" into the schedule's pso-progress.json, the file the Survey
    /// Schedule's own end-of-day reports go to -- and only that file; the PM files are never written.
    /// The write is the app's own flushProgress(): read what is on disk, merge by entry id keeping the
    /// newest "at", write it back. An entry's existing percent and done flag are kept: a daily report
    /// says the day was reported, not that the work is finished.
    /// </summary>
    internal static class ProgressWriter
    {
        public const string FileName = "pso-progress.json";

        public static bool MarkReported(string folder, IEnumerable<string> assignmentIds, string reportId, string activity, out string message,
            int attempts = 3)
        {
            message = null;
            var ids = (assignmentIds ?? Enumerable.Empty<string>()).Where(i => !string.IsNullOrEmpty(i)).Distinct().ToList();
            if (ids.Count == 0) { message = "Nothing on the schedule to mark."; return false; }
            var path = Path.Combine(folder ?? string.Empty, FileName);
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) { message = "Schedule unavailable; the report was filed but not marked on the schedule."; return false; }

            for (var attempt = 1; attempt <= attempts; attempt++)
            {
                try
                {
                    var at = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
                    var disk = Read(path);
                    // const map={}; disk.forEach(r=>{const c=map[r.id];if(!c||(r.at||"")>(c.at||""))map[r.id]=r;});
                    var map = new Dictionary<string, JObject>(StringComparer.Ordinal);
                    var order = new List<string>();
                    foreach (var r in disk)
                    {
                        var id = (string)r["id"];
                        if (id == null) continue;
                        JObject c;
                        if (!map.TryGetValue(id, out c)) { map[id] = r; order.Add(id); }
                        else if (string.CompareOrdinal((string)r["at"] ?? "", (string)c["at"] ?? "") > 0) map[id] = r;
                    }
                    foreach (var id in ids)
                    {
                        JObject cur;
                        map.TryGetValue(id, out cur);
                        var rec = cur != null ? (JObject)cur.DeepClone() : new JObject { ["id"] = id, ["pct"] = 0, ["done"] = false, ["note"] = "" };
                        rec["reportId"] = reportId;
                        rec["reportedAt"] = at;
                        var note = "Daily report " + reportId + " filed";
                        var old = (string)rec["note"] ?? "";
                        rec["note"] = (old.Length == 0 || old.StartsWith("Daily report ", StringComparison.Ordinal) ? note : old + " | " + note);
                        if (((string)rec["note"]).Length > 300) rec["note"] = ((string)rec["note"]).Substring(0, 300); // the app keeps notes to 300
                        if (!string.IsNullOrEmpty(activity)) rec["activity"] = activity.Length > 60 ? activity.Substring(0, 60) : activity;
                        foreach (var k in new[] { "ctrlPts", "topoPts", "areaDesc", "sub" }) if (rec[k] == null) rec[k] = "";
                        rec["at"] = at;
                        if (!map.ContainsKey(id)) order.Add(id);
                        map[id] = rec;
                    }
                    Write(path, new JArray(order.Select(i => map[i])));

                    // the schedule app writes this file too, with no lock: check ours survived
                    var back = Read(path).Where(r => ids.Contains((string)r["id"]) && (string)r["reportId"] == reportId).Select(r => (string)r["id"]).Distinct().Count();
                    if (back == ids.Count) return true;
                    message = "Another save to the schedule's progress file crossed this one.";
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is JsonException)
                {
                    message = "The schedule's progress file could not be updated: " + e.Message;
                }
                Thread.Sleep(200 * attempt);
            }
            return false;
        }

        private static List<JObject> Read(string path)
        {
            if (!File.Exists(path)) return new List<JObject>();
            string text;
            using (var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var r = new StreamReader(s, Encoding.UTF8))
                text = r.ReadToEnd();
            if (string.IsNullOrWhiteSpace(text)) return new List<JObject>();
            // timestamps stay exactly as the app wrote them (compared as text); a file that does not parse
            // is not overwritten: the exception stops the write
            JToken token;
            using (var reader = new JsonTextReader(new StringReader(text)) { DateParseHandling = DateParseHandling.None })
                token = JToken.ReadFrom(reader);
            return token is JArray a ? a.OfType<JObject>().ToList() : new List<JObject>();
        }

        private static void Write(string path, JArray records)
        {
            var dir = Path.GetDirectoryName(path);
            var temp = Path.Combine(dir, "." + FileName + "." + Guid.NewGuid().ToString("N").Substring(0, 6) + ".tmp");
            try
            {
                // writeJson(): JSON.stringify(obj,null,1)
                var bytes = new UTF8Encoding(false).GetBytes(records.ToString(Formatting.Indented));
                using (var s = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    s.Write(bytes, 0, bytes.Length);
                    s.Flush(true);
                }
                if (File.Exists(path)) File.Replace(temp, path, null, true);
                else File.Move(temp, path);
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
    }
}
