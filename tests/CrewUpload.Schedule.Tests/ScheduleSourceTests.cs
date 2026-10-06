using System.Security.Cryptography;
using CrewUpload.Integration;
using Newtonsoft.Json.Linq;

namespace CrewUpload.Schedule.Tests
{
    public class ScheduleSourceTests
    {
        private static readonly DateTime Mon13 = new DateTime(2026, 7, 13);
        private static readonly DateTime Tue14 = new DateTime(2026, 7, 14);

        private static ScheduleFolderSource Source(ScheduleFixture f) =>
            new ScheduleFolderSource(f.Folder) { RetryDelaysMs = new[] { 1 }, Timeout = TimeSpan.FromSeconds(20), RetryAfter = TimeSpan.Zero };

        [Fact]
        public void ReadsTheHandOffCrewAsOneCrew()
        {
            using (var f = ScheduleFixture.Standard())
            {
                var s = Source(f);
                Assert.True(s.Available);
                Assert.Null(s.Message);

                var w = Assert.Single(s.WorkFor("jim_martin", Mon13));
                Assert.Equal("2l8vhmyoodt", w.ScheduleProjectId);
                Assert.Equal("TDLE 554-1800-119 3.1.3", w.ProjectName);
                Assert.Equal("554-1800-119", w.JobNumber);
                Assert.Equal("3.1.3", w.Task);
                Assert.Equal("Topo", w.Activity);
                Assert.Equal("FIELD", w.DayType);
                // grouped by project and day (out-of-town counts as field); the office person and the
                // departed employee are not on the field crew; the person asked about comes first
                Assert.Equal(new[] { "jim_martin", "colston_bravo", "jeff_bearson", "ryan_meldrum" }, w.CrewEmployeeIds);
                Assert.Equal(4, w.AssignmentIds.Count);
                Assert.Contains("s0trcectgfb", w.AssignmentIds);
            }
        }

        [Fact]
        public void RangesAreInclusiveAndSkipWeekendsTheyRunAcross()
        {
            using (var f = ScheduleFixture.Standard())
            {
                var s = Source(f);
                Assert.Single(s.WorkFor("jim_martin", Tue14));
                Assert.Empty(s.WorkFor("jim_martin", new DateTime(2026, 7, 12)));
                Assert.Empty(s.WorkFor("jim_martin", new DateTime(2026, 7, 15))); // OFF day: nothing to report

                Assert.Single(s.WorkFor("tim_do", new DateTime(2026, 7, 17)));
                Assert.Empty(s.WorkFor("tim_do", new DateTime(2026, 7, 18)));
                Assert.Empty(s.WorkFor("tim_do", new DateTime(2026, 7, 19)));
                Assert.Single(s.WorkFor("tim_do", new DateTime(2026, 7, 20)));
                Assert.Single(s.WorkFor("tim_do", new DateTime(2026, 7, 25))); // scheduled for the Saturday itself
            }
        }

        [Fact]
        public void SeveralProjectsInADayAreAllOffered()
        {
            using (var f = ScheduleFixture.Standard())
            {
                var w = Source(f).WorkFor("jeff_bearson", Tue14);
                Assert.Equal(2, w.Count);
                var ann = w.Single(x => x.ScheduleProjectId == "ann1");
                Assert.Equal("PMX 126 — north segment control", ann.Task); // the entry's own task beats the name
                Assert.Equal("247-2535-013", ann.JobNumber);
                Assert.Equal("Set control, 7:00 start", ann.Activity);
            }
        }

        [Fact]
        public void OfficeDayOnAProjectStandsAlone()
        {
            using (var f = ScheduleFixture.Standard())
            {
                var w = Assert.Single(Source(f).WorkFor("dalton_puffer", Mon13));
                Assert.Equal("OFFICE", w.DayType);
                Assert.Equal(new[] { "dalton_puffer" }, w.CrewEmployeeIds);
                Assert.Equal("Processing", w.Activity);
            }
        }

        [Fact]
        public void PendingRequestsAreNotOnTheSchedule()
        {
            using (var f = ScheduleFixture.Standard())
                Assert.Empty(Source(f).WorkFor("colston_bravo", new DateTime(2026, 7, 16)));
        }

        [Fact]
        public void CrewsOnListsFieldCrewsOnly()
        {
            using (var f = ScheduleFixture.Standard())
            {
                var crews = Source(f).CrewsOn(Mon13);
                Assert.Equal(2, crews.Count);
                var tdle = crews.Single(c => c.ScheduleProjectId == "2l8vhmyoodt");
                Assert.Equal(5, tdle.EmployeeIds.Count); // the 4-person crew + the departed placeholder row as scheduled
                Assert.DoesNotContain("dalton_puffer", tdle.EmployeeIds);
                Assert.Equal("Topo", tdle.Activity);
            }
        }

        [Fact]
        public void RosterProjectsAndActivities()
        {
            using (var f = ScheduleFixture.Standard())
            {
                var s = Source(f);
                Assert.Equal(7, s.Employees().Count);
                Assert.False(s.Employees().Single(e => e.Id == "old_hand").Active);
                Assert.Equal(3, s.Projects().Count);
                Assert.Null(s.Projects().Single(p => p.Id == "fieldday").JobNumber);
                Assert.Equal("3.1.3", s.Projects().Single(p => p.Id == "2l8vhmyoodt").TaskNumber);
                Assert.Contains("Topo", s.Activities());
                Assert.Contains("Processing", s.Activities());
            }
        }

        [Fact]
        public void ProjectOverrideReplacesUnlessTheOwnerIsNewer()
        {
            using (var f = ScheduleFixture.Standard())
            {
                f.Write("pso-overrides.json", new object[]
                {
                    new { id = "2l8vhmyoodt", kind = "proj", at = "2026-07-02T00:00:00.000Z", byPm = "pm_ann",
                          data = new { id = "2l8vhmyoodt", pmId = "pm_casey", name = "TDLE Phase 3 554-1800-119 3.1.3", jobNum = "554-1800-119", updatedAt = "2026-07-02T00:00:00.000Z" } },
                    new { id = "ann1", kind = "proj", at = "2026-06-01T00:00:00.000Z", byPm = "pm_casey",
                          data = new { id = "ann1", pmId = "pm_ann", name = "stale rename", updatedAt = "2026-06-01T00:00:00.000Z" } },
                });
                var p = Source(f).Projects();
                Assert.Equal("TDLE Phase 3 554-1800-119 3.1.3", p.Single(x => x.Id == "2l8vhmyoodt").Name);
                Assert.Equal("Mercer 247-2535-013", p.Single(x => x.Id == "ann1").Name);
            }
        }

        [Fact]
        public void AssignmentOverridesFollowTheAppsRules()
        {
            using (var f = ScheduleFixture.Standard())
            {
                f.Write("pso-overrides.json", new object[]
                {
                    // another PM removed Ryan from the crew after the owner's last edit
                    new { id = "a4", kind = "asn", at = "2026-07-06T00:00:00.000Z", byPm = "pm_ann", op = "delete" },
                    // too old: the owner edited Jeff's entry since, so this is ignored
                    new { id = "a3", kind = "asn", at = "2026-07-01T00:00:00.000Z", byPm = "pm_ann", op = "delete" },
                    // two changes to the same entry: the newest wins (moves Tim to the TDLE crew)
                    new { id = "ann-a", kind = "asn", at = "2026-07-06T00:00:00.000Z", byPm = "pm_casey", op = "delete" },
                    new { id = "ann-a", kind = "asn", at = "2026-07-07T00:00:00.000Z", byPm = "pm_casey", op = "upsert",
                          data = ScheduleFixture.A("ann-a", "tim_do", "2l8vhmyoodt", "FIELD", "2026-07-13", "2026-07-13", "Topo", new string[0]) },
                });
                var w = Assert.Single(Source(f).WorkFor("jim_martin", Mon13));
                Assert.Equal(new[] { "jim_martin", "colston_bravo", "jeff_bearson", "tim_do" }, w.CrewEmployeeIds);
            }
        }

        [Fact]
        public void TimestampsAreComparedAsTheAppWritesThem()
        {
            using (var f = ScheduleFixture.Standard())
            {
                // half a second after the owner's edit (2026-07-05T10:00:00.000Z): newer, so it applies
                f.Write("pso-overrides.json", new object[]
                {
                    new { id = "a4", kind = "asn", at = "2026-07-05T10:00:00.500Z", byPm = "pm_ann", op = "delete" },
                    new { id = "a3", kind = "asn", at = "2026-07-05T10:00:00.000Z", byPm = "pm_ann", op = "delete" }, // same instant: owner wins
                });
                var w = Assert.Single(Source(f).WorkFor("jim_martin", Mon13));
                Assert.Equal(new[] { "jim_martin", "colston_bravo", "jeff_bearson" }, w.CrewEmployeeIds);
            }
        }

        [Fact]
        public void MissingOrBrokenPmFeedCountsAsEmpty()
        {
            using (var f = ScheduleFixture.Standard())
            {
                f.Write("pm-pm_ann.json", "{ this is not json");
                var s = Source(f);
                Assert.True(s.Available);
                Assert.Single(s.WorkFor("jim_martin", Mon13));
                Assert.Empty(s.WorkFor("tim_do", Mon13));
            }
        }

        [Fact]
        public void UnreadableScheduleIsUnavailableWithTheCalmMessage()
        {
            var missing = new ScheduleFolderSource(Path.Combine(Path.GetTempPath(), "no-such-" + Guid.NewGuid().ToString("N")));
            Assert.False(missing.Available);
            Assert.Equal("Schedule unavailable. Report can still be entered manually.", missing.Message);
            Assert.Empty(missing.WorkFor("jim_martin", Mon13));
            Assert.Empty(missing.CrewsOn(Mon13));
            Assert.Empty(missing.Projects());

            using (var f = new ScheduleFixture())
            {
                f.Write("pso-master.json", "{\"employees\":[], \"pms\": ");
                var broken = Source(f);
                Assert.False(broken.Available);
                Assert.Equal(ScheduleFolderSource.UnavailableMessage, broken.Message);

                f.Write("pso-master.json", "{\"employees\":[]}"); // no pms list: not a schedule master
                Assert.False(broken.Refresh());
            }
        }

        [Fact]
        public void KeepsTheLastGoodReadWhenTheFolderGoesAway()
        {
            using (var f = ScheduleFixture.Standard())
            {
                var s = Source(f);
                Assert.Single(s.WorkFor("jim_martin", Mon13));
                File.Delete(Path.Combine(f.Folder, "pso-master.json"));
                s.Refresh();
                Assert.Equal(ScheduleFolderSource.UnavailableMessage, s.Message);
                Assert.Single(s.WorkFor("jim_martin", Mon13));
            }
        }

        [Fact]
        public void PicksUpChangesOnRefresh()
        {
            using (var f = ScheduleFixture.Standard())
            {
                var s = Source(f);
                Assert.Empty(s.WorkFor("tim_do", Tue14));
                f.Write("pso-overrides.json", new object[]
                {
                    new { id = "new1", kind = "asn", at = "2026-07-08T00:00:00.000Z", byPm = "pm_ann", op = "upsert",
                          data = ScheduleFixture.A("new1", "tim_do", "ann1", "FIELD", "2026-07-14", "2026-07-14", "Staking", new string[0]) },
                });
                File.SetLastWriteTimeUtc(Path.Combine(f.Folder, "pso-overrides.json"), DateTime.UtcNow.AddMinutes(1));
                Assert.True(s.Refresh());
                Assert.Single(s.WorkFor("tim_do", Tue14));
            }
        }

        [Fact]
        public void NeverWritesToTheScheduleFolder()
        {
            using (var f = ScheduleFixture.Standard())
            {
                string Snapshot() => string.Join("|", Directory.GetFiles(f.Folder).OrderBy(x => x).Select(p =>
                    Path.GetFileName(p) + ":" + File.GetLastWriteTimeUtc(p).Ticks + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)))));
                var before = Snapshot();
                var s = Source(f);
                s.Refresh(); s.Employees(); s.Projects(); s.WorkFor("jim_martin", Mon13); s.CrewsOn(Mon13);
                Assert.Equal(before, Snapshot());
            }
        }

        [Fact]
        public void ReadsWhileTheScheduleAppHoldsAFileOpen()
        {
            using (var f = ScheduleFixture.Standard())
            using (new FileStream(Path.Combine(f.Folder, "pm-pm_casey.json"), FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
                Assert.Single(Source(f).WorkFor("jim_martin", Mon13));
        }

        [Theory]
        [InlineData("TDLE 554-1800-119 3.1.3", "554-1800-119", "3.1.3")]
        [InlineData("2472535013 MF Snoqualmi", "247-2535-013", null)]
        [InlineData("Field Day", null, null)]
        [InlineData("Mercer Pump Station", null, null)]
        [InlineData("Orting 216-1711-005 Phase 2", "216-1711-005", null)]
        public void JobAndTaskNumbersFromTheName(string name, string job, string task)
        {
            var p = new ProjectRecord { Name = name };
            Assert.Equal(job, JobNumbers.JobNumOf(p));
            Assert.Equal(task, JobNumbers.TaskOf(p));
        }

        [Fact]
        public void JobNumFieldWinsOverTheName()
        {
            var p = new ProjectRecord { Name = "TDLE 554-1800-119", JobNum = "553-1800-120", TaskNum = "141" };
            Assert.Equal("553-1800-120", JobNumbers.JobNumOf(p));
            Assert.Equal("141", JobNumbers.TaskOf(p));
        }

        [Fact]
        public void AssemblerKeepsFieldsItDoesNotKnow()
        {
            var feeds = new Dictionary<string, PmFeed>
            {
                ["pm1"] = new PmFeed { Projects = { new ProjectRecord { Id = "p", Name = "x", Extra = new Dictionary<string, JToken> { ["folder"] = "\\\\s\\x" } } } },
            };
            var s = ScheduleAssembler.Assemble(new MasterFile { Pms = new List<PmRecord> { new PmRecord { Id = "pm1" }, new PmRecord { Id = "pm2" } } }, feeds, null, null);
            Assert.Equal("\\\\s\\x", (string)Assert.Single(s.Projects).Extra["folder"]);
        }
    }

    public class ScheduleConnectorTests
    {
        [Fact]
        public void LoadsTheIntegrationFromBesideTheApp()
        {
            using (var f = ScheduleFixture.Standard())
            {
                var config = JobFolderConfig.CreateDefault();
                config.Schedule.Folder = f.Folder;
                string message;
                var s = ScheduleConnector.Connect(config, out message);
                Assert.NotNull(s);
                Assert.Null(message);
                Assert.Single(s.WorkFor("jim_martin", new DateTime(2026, 7, 13)));
            }
        }

        [Fact]
        public void SwitchedOffOrRemovedMeansStandalone()
        {
            var config = JobFolderConfig.CreateDefault();
            string message;
            config.Features.ScheduleIntegration = false;
            Assert.Null(ScheduleConnector.Connect(config, out message));
            Assert.Null(message);

            config.Features.ScheduleIntegration = true;
            config.Schedule.IntegrationAssembly = "Not.There.dll";
            Assert.Null(ScheduleConnector.Connect(config, out message));
            Assert.Null(message);
        }

        [Fact]
        public void UnreachableFolderStillConnectsButIsUnavailable()
        {
            var config = JobFolderConfig.CreateDefault();
            config.Schedule.Folder = Path.Combine(Path.GetTempPath(), "nope-" + Guid.NewGuid().ToString("N"));
            string message;
            var s = ScheduleConnector.Connect(config, out message);
            Assert.NotNull(s);
            Assert.False(s.Available);
            Assert.Equal("Schedule unavailable. Report can still be entered manually.", s.Message);
        }
    }
}
