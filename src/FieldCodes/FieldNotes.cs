using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace FieldCodes
{
    /// <summary>
    /// Everything a survey point carries that could hold the crew's note: the raw description
    /// (the code, and often the note typed after it), the full description, and the point's
    /// user-defined properties. Plain data, so where a note comes from can be decided and tested
    /// without Civil 3D.
    /// </summary>
    public sealed class PointRecord
    {
        public string Number { get; set; }
        public string RawDescription { get; set; }
        public string FullDescription { get; set; }

        /// <summary>The description-key format that produced <see cref="FullDescription"/>.
        /// A format with $ parameters derives the full description from the code; it never holds a
        /// note of its own.</summary>
        public string DescriptionFormat { get; set; }

        /// <summary>User-defined properties with a value, as name/value, in the drawing's order.</summary>
        public IList<KeyValuePair<string, string>> Properties { get; set; }

        public PointRecord()
        {
            Properties = new List<KeyValuePair<string, string>>();
        }
    }

    /// <summary>
    /// Finds the crew's note for a point when it is not typed after the code. A crew that shoots
    /// a pole as "PP" and writes "1234 PSE XFMR" in the data collector's note field gets a point
    /// whose raw description is just "PP"; the note arrives in a user-defined property (Note,
    /// Comment, Description 1...) or in the full description, depending on how the points were
    /// imported. Without this, FTF labels such a pole "PP".
    ///
    /// <see cref="Source"/> picks where to look:
    ///   * "" (automatic): note-like point properties first -- any whose name says note, comment,
    ///     remark or memo, or Description 1/2 -- then the full description when it adds to the
    ///     code (it starts with the code, or it differs between points sharing a code, so it is
    ///     not a description key's fixed wording such as "POWER POLE" for every PP).
    ///   * "Full Description": always the full description, where it differs from the raw one.
    ///   * "None": never; only what is typed after the code counts.
    ///   * anything else: the user-defined property of that name.
    /// </summary>
    public sealed class FieldNoteReader
    {
        public const string Automatic = "";
        public const string FullDescriptionSource = "Full Description";
        public const string NoneSource = "None";

        private static readonly Regex NoteLikeName = new Regex(
            @"NOTE|COMMENT|REMARK|MEMO|^\s*DESC(?:RIPTION)?\s*[0-9]*\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex Spaces = new Regex(@"\s+", RegexOptions.CultureInvariant);

        private readonly HashSet<string> _fixedWordingCodes =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, int> _counts =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public string Source { get; private set; }

        /// <summary>A point property whose name says it holds the crew's note: Note, Field Notes,
        /// Comment, Remarks, Memo, Description, Description 1...</summary>
        public static bool IsNoteLikeName(string name)
        {
            return !string.IsNullOrWhiteSpace(name) && NoteLikeName.IsMatch(name);
        }

        /// <param name="source">Where to look; see the class summary. Null means automatic.</param>
        /// <param name="drawing">Every point in the drawing: a full description that reads the
        /// same on every point of a code is a description key's wording, not a note.</param>
        public FieldNoteReader(string source, IEnumerable<PointRecord> drawing)
        {
            Source = (source ?? string.Empty).Trim();

            var wording = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            var points = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in drawing ?? Enumerable.Empty<PointRecord>())
            {
                if (p == null) continue;
                var raw = Normalize(p.RawDescription);
                if (raw.Length == 0) continue;
                var code = FieldCodeParser.FirstToken(raw);
                int n;
                points[code] = points.TryGetValue(code, out n) ? n + 1 : 1;

                HashSet<string> seen;
                if (!wording.TryGetValue(code, out seen))
                    wording[code] = seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                seen.Add(Normalize(p.FullDescription));
            }

            foreach (var pair in wording)
            {
                if (points[pair.Key] < 2 || pair.Value.Count != 1) continue;
                var only = pair.Value.First();
                if (only.Length > 0 && !StartsWithWord(only, pair.Key))
                    _fixedWordingCodes.Add(pair.Key);
            }
        }

        /// <summary>
        /// The note for one point, or null when it has none beyond its raw description.
        /// <paramref name="from"/> names where it was found ("property Note", "Full Description").
        /// The note never repeats the code: a full description "PP 1234 PSE" gives "1234 PSE".
        /// </summary>
        public string NoteFor(PointRecord point, out string from)
        {
            from = null;
            if (point == null) return null;
            if (string.Equals(Source, NoneSource, StringComparison.OrdinalIgnoreCase)) return null;

            var raw = Normalize(point.RawDescription);
            string note;

            if (string.Equals(Source, FullDescriptionSource, StringComparison.OrdinalIgnoreCase))
            {
                note = Strip(Normalize(point.FullDescription), raw);
                return Found(note, FullDescriptionSource, out from);
            }

            if (Source.Length > 0)
            {
                note = Strip(Join(point.Properties.Where(p => string.Equals(
                    (p.Key ?? string.Empty).Trim(), Source, StringComparison.OrdinalIgnoreCase))), raw);
                return Found(note, "property " + Source, out from);
            }

            // Automatic: the note-like properties, then the full description when it is a note.
            var noteLike = point.Properties.Where(p => IsNoteLikeName(p.Key)).ToList();
            note = Strip(Join(noteLike), raw);
            if (note != null)
                return Found(note, "property " + string.Join(" + ", noteLike
                    .Where(p => Normalize(p.Value).Length > 0).Select(p => p.Key.Trim()).ToArray()), out from);

            var full = Normalize(point.FullDescription);
            if (full.Length == 0 || raw.Length == 0 ||
                string.Equals(full, raw, StringComparison.OrdinalIgnoreCase)) return null;

            if (StartsWithWord(full, raw))
                return Found(Strip(full, raw), FullDescriptionSource, out from);

            var code = FieldCodeParser.FirstToken(raw);
            var bare = !FieldCodeParser.HasDataBeyondCode(raw);
            var derived = (point.DescriptionFormat ?? string.Empty).IndexOf('$') >= 0;
            if (bare && !derived && !_fixedWordingCodes.Contains(code))
                return Found(Strip(full, raw), FullDescriptionSource, out from);

            return null;
        }

        /// <summary>How many notes came from each place, for the command's summary.</summary>
        public IList<string> Summary()
        {
            return _counts.OrderByDescending(p => p.Value)
                          .Select(p => p.Value + " from " + p.Key)
                          .ToList();
        }

        public int NotesFound { get { return _counts.Values.Sum(); } }

        private string Found(string note, string where, out string from)
        {
            from = null;
            if (string.IsNullOrEmpty(note)) return null;
            from = where;
            int n;
            _counts[where] = _counts.TryGetValue(where, out n) ? n + 1 : 1;
            return note;
        }

        private static string Join(IEnumerable<KeyValuePair<string, string>> properties)
        {
            return Normalize(string.Join(" ", properties.Select(p => Normalize(p.Value)).ToArray()));
        }

        /// <summary>The note without the raw description or code it may repeat; null when
        /// nothing is left.</summary>
        internal static string Strip(string note, string raw)
        {
            note = Normalize(note);
            raw = Normalize(raw);
            if (note.Length == 0) return null;
            if (raw.Length > 0)
            {
                if (string.Equals(note, raw, StringComparison.OrdinalIgnoreCase)) return null;
                if (StartsWithWord(note, raw)) note = note.Substring(raw.Length).Trim();
                else
                {
                    var code = FieldCodeParser.FirstToken(raw);
                    if (StartsWithWord(note, code)) note = note.Substring(code.Length).Trim();
                }
            }
            return note.Length == 0 ? null : note;
        }

        private static bool StartsWithWord(string text, string word)
        {
            if (string.IsNullOrEmpty(word) || text.Length < word.Length) return false;
            if (!text.StartsWith(word, StringComparison.OrdinalIgnoreCase)) return false;
            return text.Length == word.Length || text[word.Length] == ' ';
        }

        internal static string Normalize(string text)
        {
            return string.IsNullOrWhiteSpace(text) ? string.Empty : Spaces.Replace(text.Trim(), " ");
        }
    }
}
