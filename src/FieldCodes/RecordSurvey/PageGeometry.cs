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
            foreach (var hit in (hits ?? Enumerable.Empty<DocumentLine>())
                     .Where(h => h != null && h.Box != null && !string.IsNullOrWhiteSpace(h.Text))
                     .OrderByDescending(h => h.EffectiveConfidence))
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
        public static double CentreDistance(PageBox a, PageBox b)
        {
            var dx = a.CenterX - b.CenterX;
            var dy = a.CenterY - b.CenterY;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
