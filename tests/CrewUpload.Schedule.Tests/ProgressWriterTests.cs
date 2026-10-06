using System.Security.Cryptography;
using CrewUpload.Integration;
using Newtonsoft.Json.Linq;

namespace CrewUpload.Schedule.Tests
{
    public class ProgressWriterTests
    {
        private static string Progress(ScheduleFixture f) => Path.Combine(f.Folder, "pso-progress.json");

        private static JArray Parse(string text)
        {
            using (var r = new Newtonsoft.Json.JsonTextReader(new StringReader(text)) { DateParseHandling = Newtonsoft.Json.DateParseHandling.None })
                return (JArray)JToken.ReadFrom(r);
        }

        [Fact]
        public void MarksEachCrewEntryAndKeepsWhatWasThere()
        {
            using (var f = ScheduleFixture.Standard())
            {
                f.Write("pso-progress.json", new object[]
                {
                    // the crew lead's own end-of-day report from the schedule app
                    new { id = "s0trcectgfb", pct = 60, done = false, note = "north half done", ctrlPts = "CP-12", topoPts = "1240", areaDesc = "", sub = "", activity = "Topo", at = "2026-07-13T23:00:00.000Z", future = "kept" },
                    new { id = "other", pct = 100, done = true, note = "", ctrlPts = "", topoPts = "", areaDesc = "", sub = "", activity = "", at = "2026-07-01T00:00:00.000Z" },
                });
                string hash(string p) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)));
                var pmBefore = hash(Path.Combine(f.Folder, "pm-pm_casey.json"));
                var s = new ScheduleFolderSource(f.Folder);

                var ok = s.MarkReported(new[] { "s0trcectgfb", "a2" }, "DR-20260713-JAM-1234abcd", "Topo", out var message);
                Assert.True(ok, message);

                var recs = Parse(File.ReadAllText(Progress(f))).OfType<JObject>().ToDictionary(r => (string)r["id"]!);
                Assert.Equal(3, recs.Count);
                var lead = recs["s0trcectgfb"];
                Assert.Equal(60, (int)lead["pct"]!);          // a report is not "complete"
                Assert.False((bool)lead["done"]!);
                Assert.Equal("CP-12", (string?)lead["ctrlPts"]);
                Assert.Equal("kept", (string?)lead["future"]);
                Assert.Equal("north half done | Daily report DR-20260713-JAM-1234abcd filed", (string?)lead["note"]);
                Assert.Equal("DR-20260713-JAM-1234abcd", (string?)lead["reportId"]);
                Assert.True(string.CompareOrdinal((string)lead["at"]!, "2026-07-13T23:00:00.000Z") > 0); // newer, so the app's merge keeps it

                var partner = recs["a2"];
                Assert.Equal(0, (int)partner["pct"]!);
                Assert.Equal("Topo", (string?)partner["activity"]);
                Assert.Equal("", (string?)partner["topoPts"]);
                Assert.Equal(100, (int)recs["other"]["pct"]!);  // other entries untouched

                Assert.Equal(pmBefore, hash(Path.Combine(f.Folder, "pm-pm_casey.json")));
                Assert.Empty(Directory.GetFiles(f.Folder, "*.tmp"));
            }
        }

        [Fact]
        public void CreatesTheFileWhenThereIsNone()
        {
            using (var f = ScheduleFixture.Standard())
            {
                Assert.True(new ScheduleFolderSource(f.Folder).MarkReported(new[] { "a3" }, "DR-1", null, out _));
                Assert.Single(Parse(File.ReadAllText(Progress(f))));
            }
        }

        [Fact]
        public void NeverOverwritesAProgressFileItCannotRead()
        {
            using (var f = ScheduleFixture.Standard())
            {
                f.Write("pso-progress.json", "[{ half written");
                Assert.False(new ScheduleFolderSource(f.Folder).MarkReported(new[] { "a3" }, "DR-1", null, out var message));
                Assert.NotNull(message);
                Assert.Equal("[{ half written", File.ReadAllText(Progress(f)));
            }
        }

        [Fact]
        public void UnreachableScheduleFailsQuietly()
        {
            var s = new ScheduleFolderSource(Path.Combine(Path.GetTempPath(), "gone-" + Guid.NewGuid().ToString("N")));
            Assert.False(s.MarkReported(new[] { "a1" }, "DR-1", null, out var message));
            Assert.Contains("not marked", message);
            Assert.False(s.MarkReported(Array.Empty<string>(), "DR-1", null, out _));
        }
    }
}
