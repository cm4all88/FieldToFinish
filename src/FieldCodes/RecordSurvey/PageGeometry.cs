using System;
using System.Collections.Generic;
using System.Linq;

namespace FieldCodes.RecordSurvey
{
    /// <summary>
    /// Page-pixel mathematics for multi-pass OCR. Survey annotation runs along the lines it
    /// labels, at every angle, and an OCR engine reads horizontal text best; so a page is read
    /// several times, each time rotated, and every hit is mapped back onto the unrotated page
    /// before the passes are merged. Pure: fully unit tested.
    /// </summary>
    public static class PageGeometry
    {
        /// <summary>
        /// Maps a box found on a page image that was turned CLOCKWISE on screen by
        /// <paramref name="passDegrees"/> (about the page centre, onto a canvas of the given
        /// size) back to the unrotated page. Turning the picture clockwise by 90° makes text
        /// that ran up the page read left to right, so a word found horizontal on that canvas
        /// had a reading direction of 90° on the page -- recorded in the result's rotation.
        /// The result is the axis-aligned box around the mapped corners.
        /// </summary>
        public static PageBox Unrotate(PageBox onRotated, double passDegrees,
                                       double rotatedWidth, double rotatedHeight,
                                       double pageWidth, double pageHeight)
        {
            if (onRotated == null) return null;
            var pass = Normalize(passDegrees);
            if (Math.Abs(pass) < 1e-9)
                return new PageBox(onRotated.X, onRotated.Y, onRotated.Width, onRotated.Height, onRotated.RotationDegrees);

            // Image coordinates run y-down. Turning the picture clockwise by θ maps a page offset
            // (dx, dy) to (dx cos θ - dy sin θ, dx sin θ + dy cos θ); undo it with the transpose.
            var theta = pass * Math.PI / 180.0;
            var cos = Math.Cos(theta);
            var sin = Math.Sin(theta);
            var rcx = rotatedWidth / 2.0;
            var rcy = rotatedHeight / 2.0;
            var pcx = pageWidth / 2.0;
            var pcy = pageHeight / 2.0;

            var corners = new[]
            {
                new[] { onRotated.X, onRotated.Y },
                new[] { onRotated.Right, onRotated.Y },
                new[] { onRotated.Right, onRotated.Bottom },
                new[] { onRotated.X, onRotated.Bottom }
            };

            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (var c in corners)
            {
                var dx = c[0] - rcx;
                var dy = c[1] - rcy;
                var px = pcx + dx * cos + dy * sin;
                var py = pcy - dx * sin + dy * cos;
                minX = Math.Min(minX, px); maxX = Math.Max(maxX, px);
                minY = Math.Min(minY, py); maxY = Math.Max(maxY, py);
            }

            return new PageBox(minX, minY, maxX - minX, maxY - minY, Normalize(onRotated.RotationDegrees + pass));
        }

        /// <summary>The canvas size needed to hold a page rotated by an angle without clipping.</summary>
        public static void RotatedCanvas(double pageWidth, double pageHeight, double degrees,
                                         out double canvasWidth, out double canvasHeight)
        {
            var theta = degrees * Math.PI / 180.0;
            var cos = Math.Abs(Math.Cos(theta));
            var sin = Math.Abs(Math.Sin(theta));
            // cos(90°) is 6e-17, not zero: round away the noise before taking the ceiling.
            canvasWidth = Math.Ceiling(Math.Round(pageWidth * cos + pageHeight * sin, 6));
            canvasHeight = Math.Ceiling(Math.Round(pageWidth * sin + pageHeight * cos, 6));
        }

        /// <summary>Folds degrees into (-180, 180].</summary>
        public static double Normalize(double degrees)
        {
            var d = degrees % 360.0;
            if (d <= -180.0) d += 360.0;
            if (d > 180.0) d -= 360.0;
            return d;
        }

        /// <summary>
        /// Merges the lines of several OCR passes over one page. Two hits whose boxes cover
        /// mostly the same ground are the same piece of text read twice: the more confident
        /// reading is kept and the other becomes its alternative (kept on the word list as a
        /// note, never silently dropped -- the review shows it when the two disagree).
        /// </summary>
        public static List<DocumentLine> Merge(IEnumerable<DocumentLine> hits, double overlapFraction = 0.6)
        {
            var kept = new List<DocumentLine>();
            // The pass that read a spot upside down gives much the same confidence as the one that
            // read it the right way up ("1334 001 =HONI" against "INCH = 100 FEET"); words a plat
            // uses tip the choice, and the loser is kept as an alternative.
            foreach (var hit in (hits ?? Enumerable.Empty<DocumentLine>())
                     .Where(h => h != null && h.Box != null && !string.IsNullOrWhiteSpace(h.Text))
                     .OrderByDescending(h => h.EffectiveConfidence + VocabularyBonus(h.Text)))
            {
                var duplicate = kept.FirstOrDefault(k => IsSameSpot(k.Box, hit.Box, overlapFraction));
                if (duplicate == null)
                {
                    kept.Add(hit);
                    continue;
                }

                // A lower-confidence reading of the same spot that differs in text is worth
                // remembering: OCR ambiguity handling turns it into an alternative.
                if (!string.Equals(Canon(duplicate.Text), Canon(hit.Text), StringComparison.Ordinal))
                    duplicate.Words.Add(new DocumentWord("[alt] " + hit.Text, hit.Box, hit.EffectiveConfidence));
            }
            return kept.OrderBy(l => l.Box.Y).ThenBy(l => l.Box.X).ToList();
        }

        private static readonly HashSet<string> Vocabulary = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "INCH", "INCHES", "FEET", "FOOT", "SCALE", "LOT", "LOTS", "BLOCK", "TRACT", "TRACTS", "PLAT", "SHEET", "NORTH", "SOUTH", "EAST", "WEST",
            "THENCE", "ALONG", "LINE", "LINES", "CORNER", "SECTION", "TOWNSHIP", "RANGE", "COUNTY", "WASHINGTON", "ROAD", "STREET", "AVENUE",
            "EASEMENT", "FOUND", "SET", "REBAR", "CAP", "MONUMENT", "PIPE", "IRON", "CONCRETE", "BRASS", "DISK", "NAIL", "CENTER", "CENTERLINE",
            "RIGHT", "WAY", "RADIUS", "DELTA", "LENGTH", "CHORD", "TANGENT", "CURVE", "TABLE", "BEARING", "BEARINGS", "DISTANCE", "BASIS",
            "THE", "AND", "SAID", "POINT", "BEGINNING", "RECORD", "SURVEY", "AUDITOR", "FILE", "VOLUME", "PAGE", "PAGES", "RECORDS",
            "ADDITION", "ACRES", "DEDICATION", "OWNER", "OWNERS", "SURVEYOR", "ENGINEER", "CERTIFICATE", "APPROVED", "DATE", "NOTES", "LEGEND",
            "PARCEL", "DEED", "UTILITY", "UTILITIES", "DRAINAGE", "ACCESS", "INGRESS", "EGRESS", "PUBLIC", "PRIVATE", "WIDE", "MEASURED",
            "CALCULATED", "HELD", "PER", "TOTAL", "AREA", "SQUARE", "WITH", "FROM", "THAT", "THIS", "PORTION", "QUARTER", "MERIDIAN",
            "WILLAMETTE", "DESCRIBED", "FOLLOWS", "EXCEPT", "SUBJECT", "SHORT", "BOUNDARY", "ADJUSTMENT", "LARGE", "SUBDIVISION", "EXHIBIT",
            "BEING", "PART", "NORTHEAST", "NORTHWEST", "SOUTHEAST", "SOUTHWEST", "NORTHERLY", "SOUTHERLY", "EASTERLY", "WESTERLY", "STATE"
        };

        /// <summary>A small bonus for readings made of words a recorded survey uses: up to three words count.</summary>
        public static double VocabularyBonus(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            var words = 0;
            foreach (var token in text.Split(new[] { ' ', '\t', ',', ';', '.', ':', '(', ')', '"', '\'' }, StringSplitOptions.RemoveEmptyEntries))
                if (token.Length >= 3 && Vocabulary.Contains(token)) words++;
            return 0.06 * Math.Min(3, words);
        }

        public static bool IsSameSpot(PageBox a, PageBox b, double fraction)
        {
            var overlap = a.Overlap(b);
            if (overlap <= 0) return false;
            var smaller = Math.Min(a.Area, b.Area);
            return smaller > 0 && overlap / smaller >= fraction;
        }

        private static string Canon(string s)
        {
            return new string((s ?? string.Empty).Where(c => !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();
        }

        /// <summary>Distance between two box centres, page pixels.</summary>
        /// <summary>
        /// A line's extent in its own reading frame: along the text (the reading direction) and
        /// down the page of that text (where its next line sits). A box turned by rot reads along
        /// (cos -rot, sin -rot): text read at the 270° pass (rot -90) runs down the page and its
        /// paragraph advances to the left, as the sideways legal description of a sideways sheet does.
        /// </summary>
        public struct TextFrame
        {
            public double Along0, Along1, Down0, Down1;
            public double Length { get { return Along1 - Along0; } }
            public double Thickness { get { return Down1 - Down0; } }
        }

        public static TextFrame FrameOf(PageBox box)
        {
            var phi = -(box.RotationDegrees) * Math.PI / 180.0;
            var ax = Math.Cos(phi); var ay = Math.Sin(phi);
            var dx = -Math.Sin(phi); var dy = Math.Cos(phi);
            var f = new TextFrame { Along0 = double.MaxValue, Along1 = double.MinValue, Down0 = double.MaxValue, Down1 = double.MinValue };
            foreach (var c in new[] { new[] { box.X, box.Y }, new[] { box.Right, box.Y }, new[] { box.X, box.Bottom }, new[] { box.Right, box.Bottom } })
            {
                var a = c[0] * ax + c[1] * ay;
                var d = c[0] * dx + c[1] * dy;
                if (a < f.Along0) f.Along0 = a;
                if (a > f.Along1) f.Along1 = a;
                if (d < f.Down0) f.Down0 = d;
                if (d > f.Down1) f.Down1 = d;
            }
            return f;
        }

        public static bool SameRotation(PageBox a, PageBox b)
        {
            return Math.Abs(Normalize(a.RotationDegrees - b.RotationDegrees)) < 15.0;
        }

        /// <summary>
        /// Pieces of one printed line, read as separate lines (a sparse-text OCR pass breaks a line
        /// at every wide gap), put back together in reading order: same rotation, sharing their
        /// baseline band, the same height within reason, and no more than <paramref name="maxGapInHeights"/>
        /// line-heights apart along the text. Everything else is left as read.
        /// </summary>
        public static List<DocumentLine> JoinFragments(IList<DocumentLine> lines, double maxGapInHeights = 2.0)
        {
            var result = new List<DocumentLine>();
            if (lines == null) return result;
            var framed = lines.Where(l => l != null && l.Box != null && l.Box.Width > 0 && l.Box.Height > 0)
                              .Select(l => new { Line = l, Frame = FrameOf(l.Box) })
                              // Reading order first, so the first piece of a line seeds its chain and the
                              // later pieces are taken up by it rather than left standing alone.
                              .OrderBy(x => x.Frame.Along0).ThenBy(x => x.Frame.Down0).ToList();
            var chains = new Dictionary<DocumentLine, List<DocumentLine>>();
            var used = new HashSet<DocumentLine>();
            foreach (var seed in framed)
            {
                if (used.Contains(seed.Line)) continue;
                used.Add(seed.Line);
                var chain = new List<DocumentLine> { seed.Line };
                var last = seed;
                while (true)
                {
                    var best = (object)null;
                    var bestGap = double.MaxValue;
                    foreach (var c in framed)
                    {
                        if (used.Contains(c.Line) || !SameRotation(c.Line.Box, last.Line.Box)) continue;
                        var t = Math.Min(last.Frame.Thickness, c.Frame.Thickness);
                        if (t <= 0) continue;
                        var ratio = c.Frame.Thickness / last.Frame.Thickness;
                        if (ratio < 0.6 || ratio > 1.6) continue;
                        var band = Math.Min(c.Frame.Down1, last.Frame.Down1) - Math.Max(c.Frame.Down0, last.Frame.Down0);
                        if (band < 0.5 * t) continue;
                        var gap = c.Frame.Along0 - last.Frame.Along1;
                        if (gap < -0.3 * t || gap > maxGapInHeights * t) continue;
                        if (gap < bestGap) { bestGap = gap; best = c; }
                    }
                    if (best == null) break;
                    var next = framed.First(x => ReferenceEquals(x, best));
                    used.Add(next.Line);
                    chain.Add(next.Line);
                    last = next;
                }
                foreach (var member in chain) chains[member] = chain;
            }
            // The page's own order is kept: a joined line stands where its first-read piece stood.
            var emitted = new HashSet<DocumentLine>();
            foreach (var l in lines)
            {
                List<DocumentLine> chain;
                if (l == null || !chains.TryGetValue(l, out chain)) { result.Add(l); continue; }
                if (emitted.Contains(l)) continue;
                foreach (var member in chain) emitted.Add(member);
                result.Add(chain.Count == 1 ? chain[0] : Joined(chain, " "));
            }
            return result;
        }

        /// <summary>One line out of several, in the order given: texts joined by the separator, the box
        /// their union, the words all of them, the confidence the lowest.</summary>
        public static DocumentLine Joined(IList<DocumentLine> parts, string separator)
        {
            var box = parts[0].Box;
            foreach (var m in parts.Skip(1)) box = box.Union(m.Box);
            var line = new DocumentLine(string.Join(separator, parts.Select(m => (m.Text ?? string.Empty).Trim()).ToArray()), box, parts.Min(m => m.Confidence))
            {
                PassRotationDegrees = parts[0].PassRotationDegrees
            };
            foreach (var m in parts) line.Words.AddRange(m.Words);
            return line;
        }

        public static double CentreDistance(PageBox a, PageBox b)
        {
            var dx = a.CenterX - b.CenterX;
            var dy = a.CenterY - b.CenterY;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
