using System;
using System.Linq;
using FieldCodes.Sheets;
using Xunit;

namespace FieldCodes.Tests
{
    /// <summary>
    /// The lightning line drawn where a viewport cuts, and the arithmetic that puts it in the
    /// survey rather than on the sheet.
    /// </summary>
    public class BreakLineTests
    {
        private static double Distance(BreakPoint a, BreakPoint b)
        {
            return Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
        }

        /// <summary>Signed distance from the straight run, positive to the left of it.</summary>
        private static double OffsetFromRun(BreakPoint p, double x1, double y1, double x2, double y2)
        {
            var dx = x2 - x1;
            var dy = y2 - y1;
            var length = Math.Sqrt(dx * dx + dy * dy);
            return ((p.X - x1) * (-dy) + (p.Y - y1) * dx) / length;
        }

        /// <summary>
        /// A right-of-way width is a figure a drafter states, not a course carried to two
        /// places: 30 feet reads 30', and only an odd one carries decimals.
        /// </summary>
        [Theory]
        [InlineData(30.0, "30'")]
        [InlineData(30.25, "30.25'")]
        [InlineData(30.5, "30.5'")]
        [InlineData(30.004, "30'")]
        [InlineData(0.0, "0'")]
        [InlineData(7.1, "7.1'")]
        public void a_width_drops_its_trailing_zeros(double feet, string expected)
        {
            Assert.Equal(expected, FieldCodes.Drafting.SurveyDirection.FormatDistanceTrimmed(feet, 2, true));
        }

        [Fact]
        public void a_width_can_be_stated_without_the_foot_mark()
        {
            Assert.Equal("30", FieldCodes.Drafting.SurveyDirection.FormatDistanceTrimmed(30, 2, false));
        }

        [Fact]
        public void it_starts_and_ends_exactly_on_the_two_ends()
        {
            // A match line that overshot its corner would not meet the next sheet.
            var zig = BreakLine.Zigzag(100, 200, 400, 200, 2, 10);

            Assert.Equal(100, zig.First().X, 9);
            Assert.Equal(200, zig.First().Y, 9);
            Assert.Equal(400, zig.Last().X, 9);
            Assert.Equal(200, zig.Last().Y, 9);
        }

        [Fact]
        public void the_teeth_alternate_either_side_by_the_amplitude()
        {
            var zig = BreakLine.Zigzag(0, 0, 100, 0, 3, 10);

            var offsets = zig.Skip(1).Take(zig.Count - 2)
                             .Select(p => OffsetFromRun(p, 0, 0, 100, 0)).ToList();
            Assert.True(offsets.Count >= 4, "a 100 unit run at a 10 unit period has teeth");
            for (var i = 0; i < offsets.Count; i++)
                Assert.Equal(i % 2 == 0 ? 3 : -3, offsets[i], 9);
        }

        [Fact]
        public void the_teeth_follow_a_run_at_any_angle()
        {
            var zig = BreakLine.Zigzag(0, 0, 0, 50, 2, 10);

            var offsets = zig.Skip(1).Take(zig.Count - 2)
                             .Select(p => Math.Abs(OffsetFromRun(p, 0, 0, 0, 50))).ToList();
            Assert.NotEmpty(offsets);
            Assert.All(offsets, o => Assert.Equal(2, o, 9));
        }

        [Theory]
        [InlineData(0, 0, 0, 0)]      // no run at all
        [InlineData(0, 0, 100, 0)]    // no amplitude
        public void a_run_with_nothing_to_draw_is_a_straight_line(double x1, double y1, double x2, double y2)
        {
            var zig = BreakLine.Zigzag(x1, y1, x2, y2, 0, 10);
            Assert.Equal(2, zig.Count);
        }

        [Fact]
        public void a_run_too_short_for_a_tooth_stays_straight()
        {
            var zig = BreakLine.Zigzag(0, 0, 1, 0, 2, 10);
            Assert.Equal(2, zig.Count);
        }

        [Fact]
        public void the_middle_of_the_sheet_is_the_middle_of_the_view()
        {
            var at = BreakLine.PaperToModel(8.5, 5.5, 8.5, 5.5, 5000, 4000, 240, 0);

            Assert.Equal(5000, at.X, 9);
            Assert.Equal(4000, at.Y, 9);
        }

        [Fact]
        public void an_inch_on_the_sheet_is_the_viewport_scale_in_the_survey()
        {
            // 1" = 20', so an inch right of centre is 20 feet east of the view centre.
            var at = BreakLine.PaperToModel(9.5, 5.5, 8.5, 5.5, 5000, 4000, 20, 0);

            Assert.Equal(5020, at.X, 9);
            Assert.Equal(4000, at.Y, 9);
        }

        [Fact]
        public void a_turned_viewport_turns_the_line_with_it()
        {
            // Quarter turn: what is to the right on the sheet is to the north in the survey.
            var at = BreakLine.PaperToModel(9.5, 5.5, 8.5, 5.5, 5000, 4000, 20, Math.PI / 2);

            Assert.Equal(5000, at.X, 6);
            Assert.Equal(4020, at.Y, 6);
        }

        [Fact]
        public void the_corners_come_back_in_order_and_the_right_size()
        {
            // A 6" x 4" viewport at 1" = 10', north up: 60' x 40' on the ground.
            var corners = BreakLine.ViewportCornersInModel(8.5, 5.5, 6, 4, 1000, 2000, 10, 0);

            Assert.Equal(4, corners.Count);
            Assert.Equal(970, corners[0].X, 9);
            Assert.Equal(1980, corners[0].Y, 9);
            Assert.Equal(1030, corners[2].X, 9);
            Assert.Equal(2020, corners[2].Y, 9);
            Assert.Equal(60, Distance(corners[0], corners[1]), 9);
            Assert.Equal(40, Distance(corners[1], corners[2]), 9);
        }

        [Fact]
        public void picking_near_an_edge_picks_that_edge()
        {
            var corners = BreakLine.ViewportCornersInModel(8.5, 5.5, 6, 4, 1000, 2000, 10, 0);

            Assert.Equal(0, BreakLine.NearestSide(corners, 1000, 1979));   // bottom
            Assert.Equal(1, BreakLine.NearestSide(corners, 1031, 2000));   // right
            Assert.Equal(2, BreakLine.NearestSide(corners, 1000, 2021));   // top
            Assert.Equal(3, BreakLine.NearestSide(corners, 969, 2000));    // left
        }
    }
}
