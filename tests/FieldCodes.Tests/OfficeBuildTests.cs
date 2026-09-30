using System;
using FieldCodes.Deploy;
using Xunit;

namespace FieldCodes.Tests
{
    /// <summary>
    /// The version line a build is stamped with, and the one decision made from it: whether the
    /// office copy is newer than what this machine has.
    /// </summary>
    public class OfficeBuildTests
    {
        private const string Release =
            "FTF 2026-09-29 20:34  dip-workflow-simplification a1da667  (Release, Civil 3D 2024)";

        [Fact]
        public void reads_the_stamp_and_the_commit()
        {
            var build = OfficeBuild.Read(Release);

            Assert.Equal(new DateTime(2026, 9, 29, 20, 34, 0), build.Stamp);
            Assert.Equal("a1da667", build.Commit);
            Assert.Equal("2026-09-29 20:34 (a1da667)", build.Short());
        }

        [Fact]
        public void a_branch_name_is_not_mistaken_for_a_commit()
        {
            // "decafbad" would pass for a commit; a branch called that must not become one.
            var build = OfficeBuild.Read("FTF 2026-09-29 20:34  main abc1234  (Release, Civil 3D 2024)");
            Assert.Equal("abc1234", build.Commit);
        }

        [Fact]
        public void a_line_without_a_commit_still_reads()
        {
            var build = OfficeBuild.Read("FTF 2026-09-29 20:34  (Release, Civil 3D 2024)");
            Assert.Equal(new DateTime(2026, 9, 29, 20, 34, 0), build.Stamp);
            Assert.Null(build.Commit);
            Assert.Equal("2026-09-29 20:34", build.Short());
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("something else entirely")]
        [InlineData("FTF")]
        [InlineData("FTF not-a-date 20:34")]
        [InlineData("FTF 2026-13-45 99:99")]
        public void anything_unreadable_comes_back_without_a_stamp(string line)
        {
            var build = OfficeBuild.Read(line);
            Assert.Null(build.Stamp);
        }

        [Fact]
        public void a_newer_office_copy_is_an_update()
        {
            var mine = OfficeBuild.Read("FTF 2026-09-29 20:34  main aaaaaaa  (Release, Civil 3D 2024)");
            var office = OfficeBuild.Read("FTF 2026-09-30 08:15  main bbbbbbb  (Release, Civil 3D 2024)");

            Assert.Equal(UpdateState.UpdateWaiting, OfficeBuild.Compare(mine, office));
        }

        [Fact]
        public void the_same_build_is_current_even_from_a_different_commit()
        {
            // Same minute, different commit: nothing is reinstalled on a hunch.
            var mine = OfficeBuild.Read("FTF 2026-09-29 20:34  main aaaaaaa  (Release, Civil 3D 2024)");
            var office = OfficeBuild.Read("FTF 2026-09-29 20:34  main bbbbbbb  (Release, Civil 3D 2024)");

            Assert.Equal(UpdateState.Current, OfficeBuild.Compare(mine, office));
        }

        [Fact]
        public void an_older_office_copy_never_drags_a_machine_backwards()
        {
            var mine = OfficeBuild.Read("FTF 2026-09-30 08:15  main bbbbbbb  (Debug, Civil 3D 2024)");
            var office = OfficeBuild.Read("FTF 2026-09-29 20:34  main aaaaaaa  (Release, Civil 3D 2024)");

            Assert.Equal(UpdateState.AheadOfOffice, OfficeBuild.Compare(mine, office));
        }

        [Fact]
        public void an_unreadable_or_missing_file_decides_nothing()
        {
            var mine = OfficeBuild.Read(Release);

            Assert.Equal(UpdateState.Unknown, OfficeBuild.Compare(mine, OfficeBuild.Read(null)));
            Assert.Equal(UpdateState.Unknown, OfficeBuild.Compare(OfficeBuild.Read("junk"), mine));
            Assert.Equal(UpdateState.Unknown, OfficeBuild.Compare(mine, null));
            Assert.Equal(UpdateState.Unknown, OfficeBuild.Compare(null, mine));
        }

        [Fact]
        public void the_sentence_says_what_happens_next_and_never_asks_for_work()
        {
            var mine = OfficeBuild.Read(Release);
            var office = OfficeBuild.Read("FTF 2026-09-30 08:15  main bbbbbbb  (Release, Civil 3D 2024)");

            var waiting = OfficeBuild.Sentence(UpdateState.UpdateWaiting, mine, office);
            Assert.Contains("installs itself when you close Civil 3D", waiting);
            Assert.Contains("2026-09-30 08:15", waiting);

            Assert.Contains("the same build as the office copy", OfficeBuild.Sentence(UpdateState.Current, mine, mine));
            Assert.Contains("left alone", OfficeBuild.Sentence(UpdateState.AheadOfOffice, mine, office));
            Assert.Contains("no office copy", OfficeBuild.Sentence(UpdateState.Unknown, mine, null));
        }
    }
}
