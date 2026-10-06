using Newtonsoft.Json;

namespace CrewUpload.Schedule.Tests
{
    /// <summary>A schedule folder on disk, shaped like the hand-off's examples (v66 files).</summary>
    internal sealed class ScheduleFixture : IDisposable
    {
        public string Folder { get; }

        public ScheduleFixture()
        {
            Folder = Path.Combine(Path.GetTempPath(), "sched-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Folder);
        }

        public void Write(string name, object value) =>
            File.WriteAllText(Path.Combine(Folder, name), value as string ?? JsonConvert.SerializeObject(value, Formatting.Indented));

        /// <summary>The hand-off's TDLE crew: Jim, Colston, Jeff and Ryan on the field, Dalton processing in the office.</summary>
        public static ScheduleFixture Standard()
        {
            var f = new ScheduleFixture();
            f.Write("pso-master.json", new
            {
                employees = new object[]
                {
                    new { id = "jim_martin", name = "Jim Martin", groupId = "g1", active = true },
                    new { id = "colston_bravo", name = "Colston Bravo", groupId = "g1", active = true },
                    new { id = "jeff_bearson", name = "Jeff Bearson", groupId = "g1", active = true },
                    new { id = "ryan_meldrum", name = "Ryan Meldrum", groupId = "g1", active = true },
                    new { id = "dalton_puffer", name = "Dalton Puffer", groupId = "g4", active = true },
                    new { id = "tim_do", name = "Tim Do", groupId = "g2", active = true },
                    new { id = "old_hand", name = "Old Hand", groupId = "g1", active = false },
                },
                pms = new object[] { new { id = "pm_casey", name = "Casey" }, new { id = "pm_ann", name = "Ann" }, new { id = "pm_gone", name = "No feed" } },
                activities = new { field = new[] { "Topo", "Staking", "Scanning", "Wetlands", "Control", "As-builts" }, office = new[] { "Processing", "Drafting" } },
            });
            f.Write("pm-pm_casey.json", new
            {
                pmId = "pm_casey",
                projects = new object[]
                {
                    new { id = "2l8vhmyoodt", pmId = "pm_casey", name = "TDLE 554-1800-119 3.1.3", jobNum = "", taskNum = "", updatedAt = "2026-07-01T00:00:00.000Z" },
                    new { id = "fieldday", pmId = "pm_casey", name = "Field Day", updatedAt = "2026-07-01T00:00:00.000Z" },
                },
                assignments = new object[]
                {
                    A("s0trcectgfb", "jim_martin", "2l8vhmyoodt", "FIELD", "2026-07-13", "2026-07-14", "Topo", new[] { "colston_bravo", "jeff_bearson", "ryan_meldrum" }),
                    A("a2", "colston_bravo", "2l8vhmyoodt", "FIELD", "2026-07-13", "2026-07-14", "Topo", new[] { "jim_martin", "jeff_bearson", "ryan_meldrum" }),
                    A("a3", "jeff_bearson", "2l8vhmyoodt", "FIELD", "2026-07-13", "2026-07-14", "Topo", new[] { "jim_martin", "colston_bravo", "ryan_meldrum" }),
                    A("a4", "ryan_meldrum", "2l8vhmyoodt", "OOT_FIELD", "2026-07-13", "2026-07-14", "Topo", new[] { "jim_martin", "colston_bravo", "jeff_bearson" }),
                    A("9d9aq26qjlr", "dalton_puffer", "2l8vhmyoodt", "OFFICE", "2026-07-13", "2026-07-14", "Processing", new string[0]),
                    A("old", "old_hand", "2l8vhmyoodt", "FIELD", "2026-07-13", "2026-07-13", "", new string[0]),
                    new { id = "15bwnstbp4x", employeeId = "jim_martin", projectId = (string)null, label = "OFF", type = "OFF", start = "2026-07-15", end = "2026-07-15", comments = "", withIds = new string[0], pmId = "pm_casey", updatedAt = "2026-07-14T20:14:34.587422" },
                    A("week", "tim_do", "fieldday", "FIELD", "2026-07-17", "2026-07-20", "Control", new string[0]),
                    A("sat", "tim_do", "fieldday", "FIELD", "2026-07-25", "2026-07-25", "", new string[0]),
                },
            });
            f.Write("pm-pm_ann.json", new
            {
                pmId = "pm_ann",
                projects = new object[] { new { id = "ann1", pmId = "pm_ann", name = "Mercer 247-2535-013", updatedAt = "2026-07-01T00:00:00.000Z" } },
                assignments = new object[]
                {
                    A("ann-a", "tim_do", "ann1", "FIELD", "2026-07-13", "2026-07-13", "Staking", new string[0]),
                    new { id = "q7m2kd81xza", employeeId = "jeff_bearson", projectId = "ann1", label = "", type = "FIELD", start = "2026-07-14", end = "2026-07-14", comments = "Set control, 7:00 start", task = "PMX 126 — north segment control", withIds = new string[0], locked = true, pmId = (string)null, updatedAt = "2026-07-10T22:14:09.771Z" },
                },
            });
            // a crew member's PTO request waiting for approval: not on the schedule yet
            f.Write("pso-requests.json", new object[]
            {
                new { id = "req1", employeeId = "colston_bravo", projectId = "ann1", label = "", type = "FIELD", start = "2026-07-16", end = "2026-07-16", comments = "", withIds = new string[0], approval = "pending" },
            });
            f.Write("pso-overrides.json", "[]");
            return f;
        }

        public static object A(string id, string emp, string proj, string type, string start, string end, string comments, string[] with) =>
            new { id, employeeId = emp, projectId = proj, label = "", type, start, end, comments, withIds = with, locked = false, pmId = (string)null, updatedAt = "2026-07-05T10:00:00.000Z" };

        public void Dispose()
        {
            try { Directory.Delete(Folder, true); } catch (IOException) { }
        }
    }
}
