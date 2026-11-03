using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace FieldCodes.Review
{
    /// <summary>
    /// A short, stable hash of what a finding or a decision rests on (FNV-1a, 64 bits, hex). Two
    /// runs over unchanged points and lines give the same fingerprint; any change to a raw
    /// description, a position or a line end gives another, which is how a person's decision
    /// is known to be about something that is no longer there.
    /// </summary>
    public static class Fingerprint
    {
        public static string Of(IEnumerable<string> parts)
        {
            const ulong offset = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;
            var hash = offset;
            if (parts != null)
                foreach (var part in parts)
                {
                    foreach (var b in Encoding.UTF8.GetBytes(part ?? string.Empty)) { hash ^= b; hash *= prime; }
                    hash ^= 0x1F; hash *= prime;      // separator, so "ab","c" differs from "a","bc"
                }
            return hash.ToString("x16", CultureInfo.InvariantCulture);
        }

        public static string Of(params string[] parts)
        {
            return Of((IEnumerable<string>)parts);
        }
    }
}
