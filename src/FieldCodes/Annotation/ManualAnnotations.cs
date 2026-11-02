using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace FieldCodes.Annotation
{
    /// <summary>A piece of text already on the plan that FTF did not draw.</summary>
    public sealed class ExistingAnnotation
    {
        public string Text { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
    }

    /// <summary>
    /// Labels someone typed by hand. Before FTF labels a point it asks whether a hand label
    /// near it already says the same thing; if so the point is left to that label rather than
    /// labelled twice. "The same thing" is judged on the words, ignoring MText formatting,
    /// case, punctuation and spacing: "18in Fir" matches FTF's "18" FIR".
    /// </summary>
    public sealed class ManualAnnotationIndex
    {
        private readonly double _cell;
        private readonly Dictionary<long, List<Tuple<ExistingAnnotation, string[]>>> _grid =
            new Dictionary<long, List<Tuple<ExistingAnnotation, string[]>>>();

        public ManualAnnotationIndex(IEnumerable<ExistingAnnotation> annotations, double cellSize)
        {
            _cell = cellSize > 0 ? cellSize : 10;
            foreach (var a in annotations ?? Enumerable.Empty<ExistingAnnotation>())
            {
                if (a == null || string.IsNullOrWhiteSpace(a.Text)) continue;
                var words = Words(a.Text);
                if (words.Length == 0) continue;
                var key = Key((long)Math.Floor(a.X / _cell), (long)Math.Floor(a.Y / _cell));
                List<Tuple<ExistingAnnotation, string[]>> bucket;
                if (!_grid.TryGetValue(key, out bucket)) _grid[key] = bucket = new List<Tuple<ExistingAnnotation, string[]>>();
                bucket.Add(Tuple.Create(a, words));
            }
        }

        /// <summary>True when a hand label within <paramref name="radius"/> of the point says
        /// what FTF's label would say.</summary>
        public bool HasMatch(string labelText, double x, double y, double radius)
        {
            var mine = Words(labelText);
            if (mine.Length == 0 || radius <= 0) return false;
            var reach = (long)Math.Ceiling(radius / _cell);
            var cx = (long)Math.Floor(x / _cell);
            var cy = (long)Math.Floor(y / _cell);
            for (var i = cx - reach; i <= cx + reach; i++)
                for (var j = cy - reach; j <= cy + reach; j++)
                {
                    List<Tuple<ExistingAnnotation, string[]>> bucket;
                    if (!_grid.TryGetValue(Key(i, j), out bucket)) continue;
                    foreach (var t in bucket)
                    {
                        var dx = t.Item1.X - x; var dy = t.Item1.Y - y;
                        if (dx * dx + dy * dy > radius * radius) continue;
                        if (SaysTheSame(mine, t.Item2)) return true;
                    }
                }
            return false;
        }

        /// <summary>Same words: every word of the shorter is in the longer, and the shorter
        /// is at least half the longer (so "FIR" alone does not match "18" FIR TO REMAIN").</summary>
        internal static bool SaysTheSame(string[] a, string[] b)
        {
            if (a.Length == 0 || b.Length == 0) return false;
            var shorter = a.Length <= b.Length ? a : b;
            var longer = a.Length <= b.Length ? b : a;
            if (shorter.Length * 2 < longer.Length) return false;
            var set = new HashSet<string>(longer, StringComparer.Ordinal);
            return shorter.All(set.Contains);
        }

        private static readonly Regex Formatting = new Regex(@"\\[A-Za-z][^;\\{}]*;|\\P|\\~|[{}]", RegexOptions.CultureInvariant);

        internal static string[] Words(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return new string[0];
            var plain = Formatting.Replace(text, " ").ToUpperInvariant();
            plain = Regex.Replace(plain, @"(\d)\s*(IN|INCH|INCHES|"")(?![A-Z])", "$1");     // 18in, 18 in, 18" -> 18
            plain = Regex.Replace(plain, @"[^A-Z0-9.]+", " ");
            return plain.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(w => w.Trim('.')).Where(w => w.Length > 0).ToArray();
        }

        private static long Key(long i, long j) { return (i << 32) ^ (j & 0xffffffffL); }
    }
}
