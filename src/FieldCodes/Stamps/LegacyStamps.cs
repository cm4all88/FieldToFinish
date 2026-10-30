using System;

namespace FieldCodes.Stamps
{
    /// <summary>
    /// Ownership kinds that changed number when branches were brought together on the
    /// integration branch. The kind is stored in each entity's XData, so a drawing stamped by an
    /// earlier build keeps its old number forever; this reads such a stamp back as what it is.
    ///
    /// FTFCOMPARE and FTFTRIM were built on a branch where they took 35-38, numbers FTFRECORD
    /// already used. On the integration branch they are 45-48. A stamp from that build is
    /// recognised by its tag text, which FTFRECORD never writes (its tags are call and monument
    /// ids such as C12 or M3, under a REC- project id).
    /// </summary>
    public static class LegacyStamps
    {
        public const short CompareSplit = 45;
        public const short CompareCloud = 46;
        public const short TrimSplit = 47;
        public const short TrimCloud = 48;

        /// <summary>The kind this stamp means today.</summary>
        public static short Remap(short kind, string pointNumber, string tagText)
        {
            if (kind < 35 || kind > 38) return kind;
            if (pointNumber != null && pointNumber.StartsWith("REC-", StringComparison.Ordinal)) return kind;

            switch (tagText ?? string.Empty)
            {
                case "COMPARE": return kind == 35 ? CompareSplit : kind;
                case "COMPARE-CLOUD": return kind == 36 ? CompareCloud : kind;
                case "TRIM": return kind == 37 ? TrimSplit : kind;
                case "TRIM-CLOUD": return kind == 38 ? TrimCloud : kind;
                default: return kind;
            }
        }
    }
}
