using System;
using System.Globalization;

namespace FieldCodes.Deploy
{
    /// <summary>What comparing this machine's FTF against the office copy concluded.</summary>
    public enum UpdateState
    {
        /// <summary>Nothing to compare: no office copy recorded, or it could not be read.</summary>
        Unknown,

        /// <summary>This machine has what the office copy has.</summary>
        Current,

        /// <summary>The office copy is newer; it installs itself when Civil 3D closes.</summary>
        UpdateWaiting,

        /// <summary>This machine is ahead of the office copy -- a developer's own build. Left alone.</summary>
        AheadOfOffice
    }

    /// <summary>
    /// One line, written into the bundle when FTF is packaged, that says which build this is:
    ///
    ///   FTF 2026-09-29 20:34  dip-workflow-simplification a1da667  (Release, Civil 3D 2024)
    ///
    /// The stamp is what decides an update, not the branch or the commit: a drafter's machine
    /// takes the office copy when the office copy is newer, and never goes backwards. An office
    /// copy that was rolled back to an older build therefore does NOT drag machines back with
    /// it -- rolling back is a deliberate act, so it is done by re-running the installer, not
    /// by a check nobody is watching.
    /// </summary>
    public sealed class OfficeBuild
    {
        private OfficeBuild() { }

        /// <summary>The whole line, as it was written and as it is shown to the drafter.</summary>
        public string Line { get; private set; }

        /// <summary>When the build was packaged, to the minute. Null when the line is not one of ours.</summary>
        public DateTime? Stamp { get; private set; }

        /// <summary>The short commit, when the line carries one -- for reading out to whoever asks.</summary>
        public string Commit { get; private set; }

        private const string StampFormat = "yyyy-MM-dd HH:mm";

        /// <summary>
        /// Reads a version line. Anything unreadable comes back with a null stamp rather than
        /// throwing: a version file is just a file, and a surveyor's Civil 3D must start anyway.
        /// </summary>
        public static OfficeBuild Read(string line)
        {
            var build = new OfficeBuild { Line = (line ?? string.Empty).Trim() };
            if (build.Line.Length == 0) return build;

            var words = build.Line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length < 3 || !string.Equals(words[0], "FTF", StringComparison.OrdinalIgnoreCase))
                return build;

            DateTime stamp;
            if (DateTime.TryParseExact(words[1] + " " + words[2], StampFormat,
                                       CultureInfo.InvariantCulture, DateTimeStyles.None, out stamp))
                build.Stamp = stamp;

            // The commit is the last word of the branch/commit pair, before the "(Release, ...)".
            for (var i = 3; i < words.Length; i++)
            {
                if (words[i].StartsWith("(", StringComparison.Ordinal)) break;
                if (IsCommit(words[i])) build.Commit = words[i];
            }

            return build;
        }

        private static bool IsCommit(string word)
        {
            if (word.Length < 7 || word.Length > 40) return false;
            foreach (var c in word)
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
            return true;
        }

        /// <summary>Short enough for a window: the date and time, and the commit when there is one.</summary>
        public string Short()
        {
            if (!Stamp.HasValue) return string.IsNullOrEmpty(Line) ? "unknown build" : Line;
            var text = Stamp.Value.ToString(StampFormat, CultureInfo.InvariantCulture);
            return string.IsNullOrEmpty(Commit) ? text : text + " (" + Commit + ")";
        }

        /// <summary>
        /// What to do about the office copy. Only a strictly newer office build is taken, and
        /// only when both lines are readable -- a missing or unreadable file changes nothing.
        /// </summary>
        public static UpdateState Compare(OfficeBuild installed, OfficeBuild office)
        {
            if (installed == null || office == null) return UpdateState.Unknown;
            if (!installed.Stamp.HasValue || !office.Stamp.HasValue) return UpdateState.Unknown;
            if (office.Stamp.Value > installed.Stamp.Value) return UpdateState.UpdateWaiting;
            if (office.Stamp.Value < installed.Stamp.Value) return UpdateState.AheadOfOffice;
            return UpdateState.Current;
        }

        /// <summary>The sentence the drafter reads. Never an instruction to do anything by hand.</summary>
        public static string Sentence(UpdateState state, OfficeBuild installed, OfficeBuild office)
        {
            var mine = installed == null ? "unknown build" : installed.Short();
            switch (state)
            {
                case UpdateState.UpdateWaiting:
                    return "A newer FTF is on the office copy (" + (office == null ? "newer" : office.Short()) +
                           "). It installs itself when you close Civil 3D -- nothing to do.";
                case UpdateState.Current:
                    return "FTF " + mine + " -- the same build as the office copy.";
                case UpdateState.AheadOfOffice:
                    return "FTF " + mine + " -- newer than the office copy, so it is left alone.";
                default:
                    return "FTF " + mine + " -- installed by hand; no office copy to check against.";
            }
        }
    }
}
