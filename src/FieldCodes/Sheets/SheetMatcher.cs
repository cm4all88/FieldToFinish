using System;
using System.Collections.Generic;

namespace FieldCodes.Sheets
{
    /// <summary>One layout viewport's footprint in model space, axis-aligned.</summary>
    public struct SheetWindow
    {
        public string Name;
        public double MinX, MinY, MaxX, MaxY;

        public double Width { get { return MaxX - MinX; } }
        public double Height { get { return MaxY - MinY; } }
    }

    /// <summary>One match line between two adjacent sheets, in model space.</summary>
    public struct MatchLine
    {
        public double X1, Y1, X2, Y2;
        public bool Vertical;

        /// <summary>The sheet on the left (vertical) or below (horizontal).</summary>
        public string SideAName;
        /// <summary>The sheet on the right (vertical) or above (horizontal).</summary>
        public string SideBName;
    }

    /// <summary>
    /// Finds where adjacent sheet windows meet. Two windows are adjacent when they
    /// butt together or overlap thinly along one axis (an overlap band up to half
    /// the narrower sheet, or a hairline gap) while sharing most of their run along
    /// the other. The match line sits centred in the seam, spanning the shared run.
    /// Diagonal neighbours and stacked duplicates match nothing -- a match line is
    /// never guessed.
    /// </summary>
    public static class SheetMatcher
    {
        /// <summary>Gap tolerance, as a fraction of the narrower sheet.</summary>
        private const double GapFraction = 0.02;
        /// <summary>Widest overlap band that still reads as a seam.</summary>
        private const double BandFraction = 0.5;
        /// <summary>Minimum shared run along the seam, as a fraction.</summary>
        private const double RunFraction = 0.5;

        public static IList<MatchLine> FindMatchLines(IList<SheetWindow> windows)
        {
            var lines = new List<MatchLine>();
            if (windows == null) return lines;

            for (var i = 0; i < windows.Count; i++)
            for (var j = i + 1; j < windows.Count; j++)
            {
                MatchLine line;
                if (TryVertical(windows[i], windows[j], out line) ||
                    TryVertical(windows[j], windows[i], out line) ||
                    TryHorizontal(windows[i], windows[j], out line) ||
                    TryHorizontal(windows[j], windows[i], out line))
                    lines.Add(line);
            }

            return lines;
        }

        /// <summary>left | right: a vertical seam between a left and a right sheet.</summary>
        private static bool TryVertical(SheetWindow left, SheetWindow right,
                                        out MatchLine line)
        {
            line = default(MatchLine);
            if (left.MinX > right.MinX) return false;

            var narrower = Math.Min(left.Width, right.Width);
            var band = left.MaxX - right.MinX;          // + overlap, - gap
            if (band < -GapFraction * narrower || band > BandFraction * narrower)
                return false;

            var runStart = Math.Max(left.MinY, right.MinY);
            var runEnd = Math.Min(left.MaxY, right.MaxY);
            if (runEnd - runStart < RunFraction * Math.Min(left.Height, right.Height))
                return false;

            var x = (left.MaxX + right.MinX) / 2.0;
            line = new MatchLine
            {
                X1 = x, Y1 = runStart, X2 = x, Y2 = runEnd,
                Vertical = true,
                SideAName = left.Name,
                SideBName = right.Name
            };
            return true;
        }

        /// <summary>below / above: a horizontal seam between stacked sheets.</summary>
        private static bool TryHorizontal(SheetWindow below, SheetWindow above,
                                          out MatchLine line)
        {
            line = default(MatchLine);
            if (below.MinY > above.MinY) return false;

            var shorter = Math.Min(below.Height, above.Height);
            var band = below.MaxY - above.MinY;
            if (band < -GapFraction * shorter || band > BandFraction * shorter)
                return false;

            var runStart = Math.Max(below.MinX, above.MinX);
            var runEnd = Math.Min(below.MaxX, above.MaxX);
            if (runEnd - runStart < RunFraction * Math.Min(below.Width, above.Width))
                return false;

            var y = (below.MaxY + above.MinY) / 2.0;
            line = new MatchLine
            {
                X1 = runStart, Y1 = y, X2 = runEnd, Y2 = y,
                Vertical = false,
                SideAName = below.Name,
                SideBName = above.Name
            };
            return true;
        }
    }
}
