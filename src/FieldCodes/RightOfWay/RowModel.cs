using System;
using System.Collections.Generic;
using FieldCodes.Easements;

namespace FieldCodes.Row
{
    /// <summary>What FTFROW is drawing. A run is one road frontage picked monument to
    /// monument; a block is a closed ring of corners, each side fronting a road or not;
    /// an intersection is a ring of corners whose sides cross the roads.</summary>
    public enum RowMode { Run, Block, Intersection }

    public enum MonumentKind { Found, Set }

    /// <summary>Which side of the picked line, looking along the order the monuments were
    /// picked, the road lies on.</summary>
    public enum RowRoadSide { Left, Right }

    /// <summary>What the picked monuments sit on.</summary>
    public enum RowReference { RowLine, Centerline }

    /// <summary>What one side of a ring is. Frontage: a right-of-way line with the road
    /// beyond it. Cross: a line across a road, so its length is that road's right-of-way
    /// width. Plain: a line with no road (the back of a block against another tract).</summary>
    public enum RowFaceKind { Frontage, Cross, Plain }

    public enum RowLayerRole { RowLine, Centerline, CrossLine }

    /// <summary>One picked monument. <see cref="Location"/> is where the monument is; the
    /// right-of-way corner it marks is <see cref="Corner"/>, which differs when the
    /// monument is an offset monument.</summary>
    public sealed class RowMonument
    {
        public string PointNumber { get; set; }
        public P2 Location { get; set; }
        public MonumentKind Kind { get; set; }
        public string Description { get; set; }

        /// <summary>The monument is not on the corner it marks.</summary>
        public bool IsOffset { get; set; }

        /// <summary>Distance from the monument to the corner, feet.</summary>
        public double OffsetFeet { get; set; }

        /// <summary>Direction from the monument TO the corner, azimuth degrees. Always
        /// that way round: it is the direction a crew walks from the monument.</summary>
        public double OffsetAzimuthDegrees { get; set; }

        public RowMonument()
        {
            Description = string.Empty;
        }

        /// <summary>The corner this monument marks, in drawing units.</summary>
        public P2 Corner(double unitsPerFoot)
        {
            if (!IsOffset) return Location;
            var radians = OffsetAzimuthDegrees * Math.PI / 180.0;
            var distance = OffsetFeet * unitsPerFoot;
            return new P2(Location.X + distance * Math.Sin(radians), Location.Y + distance * Math.Cos(radians));
        }
    }

    /// <summary>One side of a ring, between two consecutive corners.</summary>
    public sealed class RowFace
    {
        public RowFaceKind Kind { get; set; }
        public string RoadName { get; set; }

        /// <summary>The road's right-of-way width, feet. Required for a frontage; for a
        /// cross line it is the record width the measured length is checked against.</summary>
        public double WidthFeet { get; set; }

        public RowFace()
        {
            RoadName = string.Empty;
        }

        public static RowFace Frontage(string road, double widthFeet)
        {
            return new RowFace { Kind = RowFaceKind.Frontage, RoadName = road ?? string.Empty, WidthFeet = widthFeet };
        }

        public static RowFace Cross(string road, double recordWidthFeet)
        {
            return new RowFace { Kind = RowFaceKind.Cross, RoadName = road ?? string.Empty, WidthFeet = recordWidthFeet };
        }

        public static RowFace Plain()
        {
            return new RowFace { Kind = RowFaceKind.Plain };
        }
    }

    /// <summary>Everything the drafter chose, in drawing units where it is geometry and
    /// feet where it is a dimension.</summary>
    public sealed class RowJob
    {
        public RowMode Mode { get; set; }
        public double UnitsPerFoot { get; set; }
        public List<RowMonument> Monuments { get; private set; }

        // Run.
        public RowReference Reference { get; set; }
        public RowRoadSide RoadSide { get; set; }
        public string RoadName { get; set; }
        public double WidthFeet { get; set; }
        public double CenterlineLeftFeet { get; set; }
        public double CenterlineRightFeet { get; set; }

        // Block and intersection: one face per side, one radius per corner (feet, 0 = sharp).
        public List<RowFace> Faces { get; private set; }
        public List<double> CornerRadiiFeet { get; private set; }

        public bool DrawPickedLine { get; set; }
        public bool DrawCenterline { get; set; }
        public bool LabelOppositeLine { get; set; }
        public bool LabelCenterline { get; set; }
        public bool EveryMonumentDimension { get; set; }
        public double WidthToleranceFeet { get; set; }

        public RowJob()
        {
            UnitsPerFoot = 1.0;
            Monuments = new List<RowMonument>();
            Faces = new List<RowFace>();
            CornerRadiiFeet = new List<double>();
            RoadName = string.Empty;
            DrawPickedLine = true;
            DrawCenterline = true;
            LabelOppositeLine = true;
            WidthToleranceFeet = 0.05;
        }

        public bool IsRing { get { return Mode != RowMode.Run; } }
    }

    // ------------------------------------------------------------------ the plan

    public enum RowLineSource { Picked, Opposite, Centerline, Connector, Corner }

    /// <summary>One piece of drawn linework: a line or an arc.</summary>
    public sealed class RowLine
    {
        public RowLayerRole Role { get; set; }
        public RowLineSource Source { get; set; }
        public Course Course { get; set; }

        /// <summary>The side of the ring (or the run's course) this came from.</summary>
        public int FaceIndex { get; set; }

        public string RoadName { get; set; }

        /// <summary>Bearing, distance and (for an arc) curve data, from the geometry.</summary>
        public CourseData Data { get; set; }

        /// <summary>Whether this piece carries a bearing/distance label.</summary>
        public bool Annotate { get; set; }

        /// <summary>The text lines to write beside it. Filled by <see cref="RowLabels"/>.</summary>
        public List<string> Text { get; private set; }

        public RowLine()
        {
            RoadName = string.Empty;
            Text = new List<string>();
        }
    }

    /// <summary>A right-of-way dimension: from a corner square across to the opposite
    /// line, or along a cross line from corner to corner.</summary>
    public sealed class RowDimension
    {
        public P2 From { get; set; }
        public P2 To { get; set; }

        /// <summary>The length actually drawn, feet.</summary>
        public double MeasuredFeet { get; set; }

        /// <summary>The width the drafter gave; 0 when none was.</summary>
        public double RecordFeet { get; set; }

        public string RoadName { get; set; }
        public bool IsCross { get; set; }

        /// <summary>False when a cross line's measured length misses the record width by
        /// more than the tolerance.</summary>
        public bool WithinTolerance { get; set; }

        public string Text { get; set; }

        public RowDimension()
        {
            RoadName = string.Empty;
            Text = string.Empty;
            WithinTolerance = true;
        }
    }

    /// <summary>A monument to draw: its symbol where it is, its note, and for an offset
    /// monument the tie to the corner it marks.</summary>
    public sealed class RowMonumentMark
    {
        public RowMonument Monument { get; set; }
        public int Index { get; set; }
        public P2 Location { get; set; }
        public P2 Corner { get; set; }

        /// <summary>Unit vector pointing from the monument to where its note goes.</summary>
        public P2 NoteDirection { get; set; }

        /// <summary>Unit vector along the picked line here; the note is set along it.</summary>
        public P2 AlongDirection { get; set; }

        /// <summary>The note, one line per entry. Filled by <see cref="RowLabels"/>.</summary>
        public List<string> Text { get; private set; }

        /// <summary>Monument to corner, for an offset monument; null otherwise.</summary>
        public CourseData Tie { get; set; }

        public RowMonumentMark()
        {
            Text = new List<string>();
        }
    }

    /// <summary>A road name set along the road.</summary>
    public sealed class RowRoadLabel
    {
        public string RoadName { get; set; }
        public P2 At { get; set; }

        /// <summary>Direction of the road at the label, unit vector.</summary>
        public P2 Along { get; set; }

        public string Text { get; set; }

        public RowRoadLabel()
        {
            Text = string.Empty;
        }
    }

    /// <summary>The result of building a job: geometry and what to say about it, with
    /// no CAD in sight. Errors mean nothing was built.</summary>
    public sealed class RowPlan
    {
        public List<string> Errors { get; private set; }
        public List<string> Warnings { get; private set; }

        /// <summary>Things worth telling the drafter that are not problems.</summary>
        public List<string> Notes { get; private set; }

        public List<RowLine> Lines { get; private set; }
        public List<RowDimension> Dimensions { get; private set; }
        public List<RowMonumentMark> Monuments { get; private set; }
        public List<RowRoadLabel> RoadLabels { get; private set; }

        /// <summary>The corners as the monuments mark them, in order.</summary>
        public List<P2> Corners { get; private set; }

        public RowPlan()
        {
            Errors = new List<string>();
            Warnings = new List<string>();
            Notes = new List<string>();
            Lines = new List<RowLine>();
            Dimensions = new List<RowDimension>();
            Monuments = new List<RowMonumentMark>();
            RoadLabels = new List<RowRoadLabel>();
            Corners = new List<P2>();
        }

        public bool Ok { get { return Errors.Count == 0; } }
    }
}
