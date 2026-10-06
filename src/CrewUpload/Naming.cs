using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CrewUpload
{
    /// <summary>
    /// Fills in {token} patterns and keeps the result a legal Windows file name. The rules
    /// are Windows' even when the tests run elsewhere: the files end up on a Windows share.
    /// </summary>
    public static class Naming
    {
        /// <summary>Characters Windows refuses in a file or folder name.</summary>
        public static readonly char[] WindowsInvalid = { '<', '>', ':', '"', '/', '\\', '|', '?', '*' };

        private static readonly Regex Token = new Regex(@"\{([A-Za-z]+)\}", RegexOptions.Compiled);

        /// <summary>
        /// Replaces every {token} it knows. Unknown tokens are left as typed so a typo in the
        /// config shows up in the name instead of silently vanishing.
        /// </summary>
        public static string Fill(string pattern, IDictionary<string, string> values)
        {
            if (string.IsNullOrEmpty(pattern)) return string.Empty;
            return Token.Replace(pattern, m =>
            {
                string v;
                return values.TryGetValue(m.Groups[1].Value, out v) ? (v ?? string.Empty) : m.Value;
            });
        }

        /// <summary>
        /// One legal file or folder name: bad characters become '-', runs of separators collapse,
        /// and a trailing dot or space (which Windows strips and then cannot find) is removed.
        /// </summary>
        public static string Clean(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            var sb = new StringBuilder(name.Length);
            foreach (var ch in name)
                sb.Append(ch < 32 || Array.IndexOf(WindowsInvalid, ch) >= 0 ? '-' : ch);
            var s = Regex.Replace(sb.ToString(), @"\s+", " ");
            s = Regex.Replace(s, @"-{2,}", "-");
            // A blank token leaves "SV--FN" or "SV-FN-": tidy the joints it leaves behind.
            s = Regex.Replace(s, @"\s*-\s*-\s*", "-");
            s = Regex.Replace(s, @"(^[\s\-_]+)|([\s\-_.]+$)", string.Empty);
            return s;
        }

        /// <summary>A relative folder from config, split on either slash.</summary>
        public static IEnumerable<string> Segments(string relative) =>
            (relative ?? string.Empty).Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => s.Length > 0);

        /// <summary>
        /// Joins a config folder ("Survey\Field\Photos\{date}") onto a real directory, filling
        /// tokens and cleaning each segment. Config uses backslashes; this works on any OS.
        /// </summary>
        public static string Combine(string root, string relative, IDictionary<string, string> values)
        {
            var path = root;
            foreach (var segment in Segments(relative))
            {
                var part = Clean(Fill(segment, values));
                if (part.Length == 0 || part == "." || part == "..") continue;
                path = Path.Combine(path, part);
            }
            return path;
        }

        /// <summary>
        /// The config folder with every segment that holds a token dropped: the part of the
        /// tree that exists for every project and can be made up front.
        /// </summary>
        public static string StaticPart(string relative) =>
            string.Join("\\", Segments(relative).TakeWhile(s => s.IndexOf('{') < 0));

        public static string Sequence(int n, int digits) => n.ToString(new string('0', Math.Max(1, digits)), CultureInfo.InvariantCulture);

        /// <summary>
        /// Lower-cased, with '_', '-' and '.' turned into spaces, so "Field_Notes-0603" and
        /// "field notes" compare as words.
        /// </summary>
        public static string Words(string name) =>
            " " + Regex.Replace((name ?? string.Empty).ToLowerInvariant(), @"[\s_\-.()\[\]]+", " ").Trim() + " ";

        /// <summary>True when the keyword appears in the name as whole words, not inside another word.</summary>
        public static bool HasWord(string name, string keyword)
        {
            var k = Words(keyword).Trim();
            if (k.Length == 0) return false;
            // Words() pads with spaces; digits glued to a word ("notes0603") still count.
            var w = Words(name);
            return Regex.IsMatch(w, @"(?<![a-z])" + Regex.Escape(k) + @"(?![a-z])");
        }
    }
}
