using System;
using System.Collections.Generic;
using System.Globalization;

namespace FieldCodes.Sheets
{
    /// <summary>A proposed set of sheet windows covering the site.</summary>
    public sealed class SheetPlan
    {
        public IList<SheetWindow> Windows { get; set; }
        public bool Landscape { get; set; }
        public int Rows { get; set; }
        public int Columns { get; set; }

        public SheetPlan() { Windows = new List<SheetWindow>(); }
    }

    /// <summary>
    /// Lays sheets over a site for the best logical fit: given the site's extent
    /// and one sheet's printable window in model units, tries both orientations,
    /// keeps whichever covers the site with fewer sheets (landscape on a tie), and
    /// centres the grid so the slack splits evenly instead of piling up on one
    /// edge. Neighbouring sheets overlap by the requested fraction, which is what
    /// gives the match lines their seam.
    ///
    /// Numbering reads like a plan set: top row first, left to right.
    /// </summary>
    public static class SheetPlanner
    {
        public static SheetPlan Plan(double minX, double minY, double maxX, double maxY,
                                     double windowLong, double windowShort,
                                     double overlapFraction)
        {
            if (windowLong < windowShort)
            {
                var t = windowLong; windowLong = windowShort; windowShort = t;
            }
            var overlap = Math.Max(0.0, Math.Min(0.45, overlapFraction));

            var landscape = Arrange(minX, minY, maxX, maxY, windowLong, windowShort, overlap, true);
            var portrait = Arrange(minX, minY, maxX, maxY, windowShort, windowLong, overlap, false);

            var landscapeCount = landscape.Rows * landscape.Columns;
            var portraitCount = portrait.Rows * portrait.Columns;

            return portraitCount < landscapeCount ? portrait : landscape;
        }

        private static SheetPlan Arrange(double minX, double minY, double maxX, double maxY,
                                         double w, double h, double overlap, bool landscape)
        {
            var extentW = Math.Max(0.0, maxX - minX);
            var extentH = Math.Max(0.0, maxY - minY);

            var stepX = w * (1.0 - overlap);
            var stepY = h * (1.0 - overlap);

            var cols = CountFor(extentW, w, stepX);
            var rows = CountFor(extentH, h, stepY);

            // Centre the grid: the slack beyond the extent splits evenly.
            var coverW = w + (cols - 1) * stepX;
            var coverH = h + (rows - 1) * stepY;
            var startX = minX - (coverW - extentW) / 2.0;
            var topY = maxY + (coverH - extentH) / 2.0;

            var plan = new SheetPlan { Landscape = landscape, Rows = rows, Columns = cols };

            var sheet = 1;
            for (var r = 0; r < rows; r++)
            for (var c = 0; c < cols; c++)
            {
                var x = startX + c * stepX;
                var y = topY - h - r * stepY;
                plan.Windows.Add(new SheetWindow
                {
                    Name = "SHEET " + sheet.ToString(CultureInfo.InvariantCulture),
                    MinX = x, MinY = y, MaxX = x + w, MaxY = y + h
                });
                sheet++;
            }

            return plan;
        }

        /// <summary>Sheets needed to span one axis: the first covers a window, each
        /// further one advances a step.</summary>
        private static int CountFor(double extent, double window, double step)
        {
            if (extent <= window || step <= 0) return 1;
            return 1 + (int)Math.Ceiling((extent - window) / step);
        }
    }
}
