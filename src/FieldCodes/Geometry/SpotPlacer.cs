using System;
using System.Collections.Generic;

namespace FieldCodes.Geometry
{
    /// <summary>An axis-aligned box already occupied by a spot text.</summary>
    public struct SpotBox
    {
        public double MinX, MinY, MaxX, MaxY;

        public bool Intersects(SpotBox other)
        {
            return MinX < other.MaxX && MaxX > other.MinX &&
                   MinY < other.MaxY && MaxY > other.MinY;
        }
    }

    /// <summary>
    /// Places spot-elevation text so dense clusters stay legible: a wheelchair
    /// ramp gets a dozen shots a foot apart, and every label must land clear of
    /// the ones already placed. The text prefers its standard perch -- up the
    /// label angle from the X -- then tries the mirrored side, the two
    /// perpendiculars, and finally steps outward along the angle until clear.
    /// Deterministic: the same clicks always give the same layout.
    /// </summary>
    public static class SpotPlacer
    {
        /// <summary>
        /// Chooses the centre of the text box for a spot at (x, y): the office's
        /// standard perch up the label angle, alternates when it is taken. The
        /// clearance is the gap between the X and the text box centre; the chosen
        /// box is appended to the occupied list.
        /// </summary>
        public static void Place(double x, double y, double angleRadians,
                                 double clearance, double width, double height,
                                 IList<SpotBox> occupied,
                                 out double textX, out double textY)
        {
            var cos = Math.Cos(angleRadians);
            var sin = Math.Sin(angleRadians);

            // The candidate directions, in preference order: the standard side,
            // its mirror, then the two perpendiculars.
            var directions = new[]
            {
                new[] { cos, sin },
                new[] { -cos, -sin },
                new[] { -sin, cos },
                new[] { sin, -cos }
            };

            for (var ring = 0; ring < 6; ring++)
            {
                var reach = clearance * (1.0 + ring * 0.75);
                foreach (var direction in directions)
                {
                    var cx = x + direction[0] * reach;
                    var cy = y + direction[1] * reach;
                    var candidate = BoxAt(cx, cy, width, height);

                    if (!Collides(candidate, occupied))
                    {
                        occupied.Add(candidate);
                        textX = cx;
                        textY = cy;
                        return;
                    }
                }
            }

            // Everything within six rings is taken: place at the standard spot
            // anyway -- a crowded label beats a missing elevation.
            textX = x + cos * clearance;
            textY = y + sin * clearance;
            occupied.Add(BoxAt(textX, textY, width, height));
        }

        private static SpotBox BoxAt(double cx, double cy, double w, double h)
        {
            return new SpotBox
            {
                MinX = cx - w / 2, MinY = cy - h / 2,
                MaxX = cx + w / 2, MaxY = cy + h / 2
            };
        }

        private static bool Collides(SpotBox candidate, IList<SpotBox> occupied)
        {
            foreach (var box in occupied)
                if (candidate.Intersects(box)) return true;
            return false;
        }
    }
}
