using System;
using System.Collections.Generic;
using FieldCodes.Easements;

namespace FieldCodes.Lots
{
    /// <summary>Where the leftover from a lot row's frontage goes when the typed widths do not
    /// add up to the side they divide.</summary>
    public enum LotWidthClose { Refuse, FirstLot, LastLot }

    /// <summary>What runs between the lots and the back of the block.</summary>
    public enum LotAlleyMode
    {
        /// <summary>No alley.</summary>
        None,
        /// <summary>One row of lots on the front side, an alley along the back side behind them.</summary>
        Rear,
        /// <summary>Two rows of lots back to back with an alley between them.</summary>
        MidBlock
    }

    /// <summary>Where the surface of a lot drains. Left and right are as seen standing on the
    /// front line looking into the lot.</summary>
    public enum LotDrain { ToFront, ToRear, ToLeft, ToRight, FrontAndRear, None }

    /// <summary>One row of lots along one side of the block.</summary>
    public sealed class LotRow
    {
        /// <summary>The block side the lots front, 0-based: side i runs from corner i to the next.</summary>
        public int FrontSide { get; set; }

        /// <summary>Frontage of each lot, feet, in order from the side's first corner.</summary>
        public List<double> WidthsFeet { get; private set; }

        /// <summary>Lot depth, feet. 0 lets the block, the alley and the other row decide.</summary>
        public double DepthFeet { get; set; }

        public LotWidthClose Close { get; set; }

        public LotRow()
        {
            WidthsFeet = new List<double>();
        }

        /// <summary>The widths of <paramref name="count"/> lots dividing <paramref name="totalFeet"/>:
        /// the two end lots wider than the others by <paramref name="cornerExtraFeet"/> (corner lots
        /// carry a street side yard). Equal when the extra is zero.</summary>
        public static List<double> EqualWidths(double totalFeet, int count, double cornerExtraFeet)
        {
            var widths = new List<double>();
            if (count < 1 || !(totalFeet > 0)) return widths;
            var extra = count > 1 ? Math.Max(0.0, cornerExtraFeet) : 0.0;
            var inner = (totalFeet - 2.0 * extra) / count;
            for (var i = 0; i < count; i++)
                widths.Add(count > 1 && (i == 0 || i == count - 1) ? inner + extra : inner);
            return widths;
        }
    }

    /// <summary>Everything chosen for subdividing one platted block.</summary>
    public sealed class LotJob
    {
        public string BlockName { get; set; }
        public double UnitsPerFoot { get; set; }

        /// <summary>The block's corners in order, drawing units.</summary>
        public List<P2> Corners { get; private set; }

        /// <summary>Corner radius at each corner, feet; 0 is sharp.</summary>
        public List<double> CornerRadiiFeet { get; private set; }

        public LotRow RowA { get; set; }
        public LotRow RowB { get; set; }

        public LotAlleyMode Alley { get; set; }
        public double AlleyWidthFeet { get; set; }

        /// <summary>The side the alley runs along for a rear alley; -1 chooses the side furthest from the front.</summary>
        public int BackSide { get; set; }

        public int FirstLotNumber { get; set; }

        public LotDrain DefaultDrain { get; set; }
        public Dictionary<int, LotDrain> DrainOverrides { get; private set; }
        public double SlopePercent { get; set; }

        // Building setbacks and utility easements, feet; 0 omits one.
        public double FrontSetbackFeet { get; set; }
        public double SideSetbackFeet { get; set; }
        public double CornerSideSetbackFeet { get; set; }
        public double RearSetbackFeet { get; set; }
        public double FrontEasementFeet { get; set; }
        public double SideEasementFeet { get; set; }
        public double RearEasementFeet { get; set; }

        /// <summary>Zero omits the check.</summary>
        public double MinLotAreaSqFt { get; set; }
        public double MinLotWidthFeet { get; set; }

        public double ToleranceFeet { get; set; }

        public LotJob()
        {
            BlockName = string.Empty;
            UnitsPerFoot = 1.0;
            Corners = new List<P2>();
            CornerRadiiFeet = new List<double>();
            RowA = new LotRow();
            BackSide = -1;
            FirstLotNumber = 1;
            DefaultDrain = LotDrain.ToFront;
            DrainOverrides = new Dictionary<int, LotDrain>();
            SlopePercent = 2.0;
            ToleranceFeet = 0.01;
        }
    }

    // ------------------------------------------------------------------ the plan

    public enum LotEdgeRole { Front, Rear, Side, StreetSide }

    /// <summary>A lot after the block is divided.</summary>
    public sealed class Lot
    {
        /// <summary>The number as written, "5".</summary>
        public string Number { get; set; }
        public int Index { get; set; }

        /// <summary>0 for the first row, 1 for the second.</summary>
        public int Row { get; set; }

        /// <summary>The corners of the lot with sharp corners.</summary>
        public List<P2> Polygon { get; set; }

        /// <summary>The lot's boundary as drawn: lines, with arcs where a block corner is rounded.</summary>
        public List<Course> Boundary { get; set; }

        public double AreaSqFt { get; set; }
        public double FrontageFeet { get; set; }
        public double MeanDepthFeet { get; set; }
        public bool IsCorner { get; set; }
        public P2 LabelPoint { get; set; }

        /// <summary>Unit vector into the block from the front line.</summary>
        public P2 Inward { get; set; }

        /// <summary>Unit vector along the front line.</summary>
        public P2 Along { get; set; }

        /// <summary>One arrow, or two when the lot drains both ways.</summary>
        public List<LotDrainArrow> Drains { get; private set; }

        /// <summary>The lot's note lines. Filled by <see cref="LotLabels"/>.</summary>
        public List<string> Text { get; private set; }

        public Lot()
        {
            Text = new List<string>();
            Polygon = new List<P2>();
            Boundary = new List<Course>();
            Drains = new List<LotDrainArrow>();
            Number = string.Empty;
        }
    }

    /// <summary>A surface-drainage arrow: from where it starts to where it points.</summary>
    public sealed class LotDrainArrow
    {
        public P2 From { get; set; }
        public P2 To { get; set; }
        public double Percent { get; set; }
        public string Text { get; set; }

        public LotDrainArrow() { Text = string.Empty; }
    }

    public enum LotLineKind { Lot, Perimeter, Alley }

    /// <summary>One distinct line to draw. A line two lots share is one line.</summary>
    public sealed class LotLine
    {
        public LotLineKind Kind { get; set; }
        public Course Course { get; set; }
        public CourseData Data { get; set; }

        /// <summary>A line on the block's own boundary carries its distance only: the block's
        /// bearings belong to the right-of-way drawing.</summary>
        public bool DistanceOnly { get; set; }

        public List<string> Text { get; private set; }

        public LotLine() { Text = new List<string>(); }
    }

    public enum LotOffsetKind { Setback, Easement }

    /// <summary>The building envelope or utility-easement boundary inside a lot.</summary>
    public sealed class LotEnvelope
    {
        public LotOffsetKind Kind { get; set; }
        public int LotIndex { get; set; }
        public List<P2> Polygon { get; set; }
    }

    /// <summary>The alley, as its own region.</summary>
    public sealed class LotAlley
    {
        public List<P2> Polygon { get; set; }
        public double AreaSqFt { get; set; }
        public double WidthFeet { get; set; }
        public P2 LabelPoint { get; set; }
        public string Text { get; set; }

        public LotAlley() { Text = string.Empty; Polygon = new List<P2>(); }
    }

    /// <summary>Block subdivision with no CAD in it. Errors mean nothing was built.</summary>
    public sealed class LotPlan
    {
        public List<string> Errors { get; private set; }
        public List<string> Warnings { get; private set; }
        public List<string> Notes { get; private set; }

        public List<Lot> Lots { get; private set; }
        public List<LotLine> Lines { get; private set; }
        public List<LotEnvelope> Envelopes { get; private set; }
        public LotAlley Alley { get; set; }

        /// <summary>Ground left over when the lots are shallower than the block and no alley takes it.</summary>
        public List<P2> Remainder { get; set; }
        public double RemainderSqFt { get; set; }

        public double BlockAreaSqFt { get; set; }
        public string Title { get; set; }

        /// <summary>The block's bounding box, drawing units; the title and notes are set from it.</summary>
        public P2 Minimum { get; set; }
        public P2 Maximum { get; set; }

        /// <summary>The plat notes about setbacks and easements. Filled by <see cref="LotLabels"/>.</summary>
        public List<string> GeneralNotes { get; private set; }

        public LotPlan()
        {
            Errors = new List<string>();
            Warnings = new List<string>();
            Notes = new List<string>();
            Lots = new List<Lot>();
            Lines = new List<LotLine>();
            Envelopes = new List<LotEnvelope>();
            GeneralNotes = new List<string>();
            Title = string.Empty;
        }

        public bool Ok { get { return Errors.Count == 0; } }
    }
}
