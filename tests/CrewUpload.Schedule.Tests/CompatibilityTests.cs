using System.Diagnostics;
using Newtonsoft.Json;

namespace CrewUpload.Schedule.Tests
{
    /// <summary>Runs only where Node.js is installed; the compatibility check needs it to run the Schedule's own code.</summary>
    public sealed class NodeFactAttribute : FactAttribute
    {
        public NodeFactAttribute()
        {
            if (CompatibilityTests.Node() == null) Skip = "Node.js not found: the Schedule compatibility check (tests/schedule-compat) cannot run here.";
        }
    }

    /// <summary>
    /// Proves Crew Upload's ScheduleAssembler (a C# port of the Schedule's assembleState()) gives the
    /// same projects and entries as the Schedule's own code, extracted verbatim from its HTML into
    /// tests/schedule-compat/assembleState.reference.js, on hand-made and randomly generated folders.
    /// When the Schedule changes, re-extract (node extract.js "Survey Schedule.html" --write) and run
    /// this; a difference fails the build instead of drifting quietly.
    /// </summary>
    public class CompatibilityTests
    {
        internal static string Node()
        {
            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
                foreach (var name in new[] { "node", "node.exe" })
                {
                    var p = Path.Combine(dir, name);
                    if (File.Exists(p)) return p;
                }
            return null;
        }

        private static string CompatDir()
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d != null && !Directory.Exists(Path.Combine(d.FullName, "tests", "schedule-compat"))) d = d.Parent;
            return d == null ? null : Path.Combine(d.FullName, "tests", "schedule-compat");
        }

        private static void AssertSame(IEnumerable<string> folders)
        {
            ScheduleDiagnostics.ReadRetryDelaysMs = new[] { 1 };
            var args = new List<string> { Path.Combine(CompatDir(), "compat.js") };
            foreach (var f in folders)
            {
                var cs = Path.Combine(Path.GetTempPath(), "cs-" + Guid.NewGuid().ToString("N") + ".json");
                File.WriteAllText(cs, ScheduleDiagnostics.AssembledJson(f) ?? "");
                args.AddRange(new[] { "--folder", f, "--csharp", cs });
            }
            var psi = new ProcessStartInfo(Node()) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using (var p = Process.Start(psi)!)
            {
                var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                p.WaitForExit();
                Assert.True(p.ExitCode == 0, output);
            }
        }

        [NodeFact]
        public void TheReferenceIsTheExtractedScheduleCode()
        {
            var text = File.ReadAllText(Path.Combine(CompatDir(), "assembleState.reference.js"));
            var sha = text.Split('\n')[1].Replace("// sha256 ", "").Trim();
            var code = string.Join("\n", text.Split('\n').Skip(4));
            var actual = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(code))).ToLowerInvariant();
            Assert.Equal(sha, actual); // nobody has hand-edited the Schedule's code kept here
        }

        [NodeFact]
        public void HandMadeCasesMatchTheSchedule()
        {
            using (var standard = ScheduleFixture.Standard())
            using (var overridden = ScheduleFixture.Standard())
            using (var empty = new ScheduleFixture())
            {
                overridden.Write("pso-overrides.json", new object[]
                {
                    new { id = "2l8vhmyoodt", kind = "proj", at = "2026-07-02T00:00:00.000Z", byPm = "pm_ann", data = new { id = "2l8vhmyoodt", pmId = "pm_casey", name = "TDLE renamed", updatedAt = "2026-07-02T00:00:00.000Z" } },
                    new { id = "a4", kind = "asn", at = "2026-07-06T00:00:00.000Z", byPm = "pm_ann", op = "delete" },
                    new { id = "a3", kind = "asn", at = "2026-07-05T10:00:00.000Z", byPm = "pm_ann", op = "delete" },
                    new { id = "ann-a", kind = "asn", at = "2026-07-07T00:00:00.000Z", byPm = "pm_casey", op = "upsert", data = ScheduleFixture.A("ann-a", "tim_do", "2l8vhmyoodt", "FIELD", "2026-07-13", "2026-07-13", "Topo", new string[0]) },
                    new { id = "new", kind = "asn", at = "2026-07-08T00:00:00.000Z", byPm = "pm_casey", op = "upsert", data = ScheduleFixture.A("new", "tim_do", "fieldday", "OFF", "2026-07-20", "2026-07-24", "", new string[0]) },
                });
                empty.Write("pso-master.json", "{\"employees\":[]}");
                AssertSame(new[] { standard.Folder, overridden.Folder, empty.Folder });
            }
        }

        [NodeFact]
        public void RandomSchedulesMatchTheSchedule()
        {
            var folders = new List<ScheduleFixture>();
            try
            {
                for (var seed = 1; seed <= 300; seed++) folders.Add(Random(seed));
                AssertSame(folders.Select(f => f.Folder));
            }
            finally { foreach (var f in folders) f.Dispose(); }
        }

        /// <summary>A schedule folder full of the awkward cases: ties, missing times, numeric ids, missing or broken feeds.</summary>
        private static ScheduleFixture Random(int seed)
        {
            var r = new Random(seed);
            var f = new ScheduleFixture();
            string Pick(params string[] xs) => xs[r.Next(xs.Length)];
            string At() => r.Next(8) == 0 ? null : "2026-07-0" + r.Next(1, 4) + "T10:00:00." + Pick("000", "500", "500") + "Z";
            var ids = Enumerable.Range(0, 12).Select(i => r.Next(5) == 0 ? (100 - i).ToString() : "e" + i).ToList();
            var people = new[] { "jim", "jeff", "tim", "ann", "bo" };
            var pms = Enumerable.Range(0, r.Next(1, 4)).Select(i => "pm" + i).ToList();
            f.Write("pso-master.json", new { employees = people.Select(p => new { id = p, name = p.ToUpper(), active = r.Next(6) > 0 }), pms = pms.Select(p => new { id = p, name = p }) });
            var projIds = new[] { "p1", "p2", "p3", "7" };
            foreach (var pm in pms)
            {
                var roll = r.Next(10);
                if (roll == 0) continue;                                     // feed missing
                if (roll == 1) { f.Write("pm-" + pm + ".json", "{ broken"); continue; }
                var projects = projIds.Where(_ => r.Next(3) == 0).Select(p => (object)new Dictionary<string, object>
                {
                    ["id"] = p, ["pmId"] = pm, ["name"] = "Proj " + p + " 554-1800-1" + r.Next(10, 99), ["updatedAt"] = At(), ["folder"] = r.Next(2) == 0 ? null : "\\\\s\\" + p,
                }).ToList();
                var assignments = ids.Where(_ => r.Next(3) == 0).Select(id => Entry(r, id, people, projIds, At())).ToList();
                f.Write("pm-" + pm + ".json", new { pmId = pm, projects, assignments, notes = new object[0] });
            }
            f.Write("pso-requests.json", Enumerable.Range(0, r.Next(3)).Select(i => Entry(r, "req" + i, people, projIds, At(), pending: true)).ToList());
            var overrides = new List<object>();
            for (var i = 0; i < r.Next(0, 10); i++)
            {
                var id = Pick(ids.Concat(projIds).Concat(new[] { "req0" }).ToArray());
                var kind = projIds.Contains(id) && r.Next(3) > 0 ? "proj" : "asn";
                object data = kind == "proj"
                    ? (object)new Dictionary<string, object> { ["id"] = r.Next(6) == 0 ? Pick(projIds) : id, ["pmId"] = Pick(pms.ToArray()), ["name"] = "Override " + i, ["updatedAt"] = At() }
                    : Entry(r, id, people, projIds, At());
                var o = new Dictionary<string, object> { ["id"] = id, ["kind"] = kind, ["byPm"] = Pick(pms.ToArray()), ["op"] = r.Next(4) == 0 ? "delete" : "upsert" };
                var at = At();
                if (at != null) o["at"] = at;
                var d = r.Next(8);
                if (d == 0) o["data"] = null; else if (d == 1) o["data"] = false; else if (d != 2) o["data"] = data;
                overrides.Add(o);
            }
            f.Write("pso-overrides.json", overrides);
            return f;
        }

        private static object Entry(Random r, string id, string[] people, string[] projIds, string at, bool pending = false)
        {
            var start = new DateTime(2026, 7, 10).AddDays(r.Next(10));
            var e = new Dictionary<string, object>
            {
                ["id"] = id, ["employeeId"] = people[r.Next(people.Length)], ["projectId"] = r.Next(5) == 0 ? null : projIds[r.Next(projIds.Length)],
                ["label"] = r.Next(5) == 0 ? "OFF" : "", ["type"] = new[] { "FIELD", "OFFICE", "OFF", "OOT_FIELD", "FIELD_OFFICE" }[r.Next(5)],
                ["start"] = start.ToString("yyyy-MM-dd"), ["end"] = start.AddDays(r.Next(4)).ToString("yyyy-MM-dd"),
                ["comments"] = "c" + r.Next(9), ["withIds"] = people.Where(_ => r.Next(4) == 0).ToArray(), ["locked"] = r.Next(2) == 0, ["pmId"] = null,
            };
            if (at != null) e["updatedAt"] = at;
            if (r.Next(4) == 0) e["task"] = "3." + r.Next(9);
            if (pending) { e["approval"] = "pending"; e["requestedAt"] = at; }
            return e;
        }
    }
}
