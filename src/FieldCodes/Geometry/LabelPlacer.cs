using System;
using System.Collections.Generic;

namespace FieldCodes.Geometry
{
    /// <summary>An axis-aligned box in drawing units.</summary>
    public struct Box2d
    {
        public double MinX { get; private set; }
        public double MinY { get; private set; }
        public double MaxX { get; private set; }
        public double MaxY { get; private set; }

        public Box2d(double minX, double minY, double maxX, double maxY)
            : this()
        {
            MinX = Math.Min(minX, maxX);
            MinY = Math.Min(minY, maxY);
            MaxX = Math.Max(minX, maxX);
            MaxY = Math.Max(minY, maxY);
        }

        public static Box2d FromSize(double minX, double minY, double width, double height)
        {
            return new Box2d(minX, minY, minX + width, minY + height);
        }

        public double Width { get { return MaxX - MinX; } }
        public double Height { get { return MaxY - MinY; } }
        public double CentreX { get { return (MinX + MaxX) / 2.0; } }
        public double CentreY { get { return (MinY + MaxY) / 2.0; } }

        public bool Intersects(Box2d other)
        {
            return !(other.MinX >= MaxX || other.MaxX <= MinX ||
                     other.MinY >= MaxY || other.MaxY <= MinY);
        }

        public double OverlapArea(Box2d other)
        {
            var w = Math.Min(MaxX, other.MaxX) - Math.Max(MinX, other.MinX);
            var h = Math.Min(MaxY, other.MaxY) - Math.Max(MinY, other.MinY);
            if (w <= 0 || h <= 0) return 0.0;
            return w * h;
        }

        public Box2d Inflate(double margin)
        {
            return new Box2d(MinX - margin, MinY - margin, MaxX + margin, MaxY + margin);
        }
    }

    /// <summary>Something a label must not, or would rather not, sit on top of.</summary>
    public sealed class Obstacle
    {
        public Box2d Bounds { get; set; }
        public ObstacleClass Class { get; set; }

        /// <summary>For diagnostics only.</summary>
        public string Tag { get; set; }

        public Obstacle() { }

        public Obstacle(Box2d bounds, ObstacleClass cls, string tag = null)
        {
            Bounds = bounds;
            Class = cls;
            Tag = tag;
        }
    }

    /// <summary>The eight candidate directions, in the order they are tried.</summary>
    public enum LabelDirection
    {
        NE = 0, E = 1, SE = 2, NW = 3, W = 4, SW = 5, N = 6, S = 7
    }

    /// <summary>Where a label ended up, and what it cost.</summary>
    public sealed class LabelPlacement
    {
        public bool Placed { get; set; }
        public Box2d Bounds { get; set; }
        public LabelDirection Direction { get; set; }

        /// <summary>0 is the base ring. Anything above it needs a leader.</summary>
        public int Ring { get; set; }

        public bool NeedsLeader { get; set; }

        /// <summary>Label sits on soft obstacles and must be masked.</summary>
        public bool MasksSoftObstacles { get; set; }

        /// <summary>Total soft-obstacle area covered, for reporting.</summary>
        public double SoftOverlapArea { get; set; }
    }

    /// <summary>
    /// Chooses where a label goes.
    ///
    /// Candidates are the eight compass directions at increasing offsets. Hard
    /// obstacles (protected symbols, other labels) are never overlapped. Soft
    /// obstacles (existing text) are avoided, and masked only when nothing better
    /// exists. Free obstacles (linework, canopies) are ignored -- they get masked.
    ///
    /// The base ring is tried first with no leader. If every direction there collides,
    /// the search moves outward and the result is flagged as needing one.
    ///
    /// Boxes arrive already sized at plot scale; this class does no scaling itself.
    /// </summary>
    public sealed class LabelPlacer
    {
        private static readonly LabelDirection[] Order =
        {
            LabelDirection.NE, LabelDirection.E, LabelDirection.SE,
            LabelDirection.NW, LabelDirection.W, LabelDirection.SW,
            LabelDirection.N,  LabelDirection.S
        };

        /// <summary>
        /// The order candidates are tried within a ring. Earlier directions win ties.
        /// Exposed so the ordering contract can be asserted directly -- adjacent
        /// candidate boxes overlap each other, so it cannot be probed from outside
        /// by blocking one direction at a time.
        /// </summary>
        public static IList<LabelDirection> SearchOrder
        {
            get { return Array.AsReadOnly(Order); }
        }

        /// <summary>Gap between the anchor point and the near edge of the label.</summary>
        public double BaseOffset { get; set; }

        /// <summary>Number of rings tried, including the base ring.</summary>
        public int RingCount { get; set; }

        /// <summary>Each ring is this much further out than the previous one.</summary>
        public double RingStep { get; set; }

        public LabelPlacer()
        {
            BaseOffset = 1.0;
            RingCount = 4;
            RingStep = 1.0;
        }

        /// <summary>
        /// Finds a home for a label of the given size anchored at a point.
        /// Returns <c>Placed = false</c> only when every ring collides with something
        /// hard, in which case the caller must decide -- report it, do not guess.
        /// </summary>
        public LabelPlacement Place(double anchorX, double anchorY,
                                    double width, double height,
                                    IList<Obstacle> obstacles)
        {
            if (obstacles == null) obstacles = new Obstacle[0];

            LabelPlacement best = null;

            for (var ring = 0; ring < Math.Max(1, RingCount); ring++)
            {
                var offset = BaseOffset + ring * RingStep;

                for (var d = 0; d < Order.Length; d++)
                {
                    var box = BoxFor(Order[d], anchorX, anchorY, width, height, offset);

                    if (HitsHard(box, obstacles)) continue;

                    var soft = SoftArea(box, obstacles);

                    var candidate = new LabelPlacement
                    {
                        Placed = true,
                        Bounds = box,
                        Direction = Order[d],
                        Ring = ring,
                        NeedsLeader = ring > 0,
                        MasksSoftObstacles = soft > 0,
                        SoftOverlapArea = soft
                    };

                    // A clean spot wins immediately; direction order breaks ties.
                    if (soft <= 0) return candidate;

                    if (best == null || soft < best.SoftOverlapArea) best = candidate;
                }

                // Prefer a soft-overlapping spot in this ring over moving further out:
                // a nearer label with a mask reads better than a distant one on a leader.
                if (best != null) return best;
            }

            return new LabelPlacement { Placed = false, NeedsLeader = true };
        }

        /// <summary>
        /// Box for one candidate. The label is pushed <paramref name="offset"/> clear of
        /// the anchor along the given direction, and centred across it on the other axis.
        /// </summary>
        public static Box2d BoxFor(LabelDirection direction, double ax, double ay,
                                   double w, double h, double offset)
        {
            double minX, minY;

            switch (direction)
            {
                case LabelDirection.E:
                    minX = ax + offset; minY = ay - h / 2.0; break;
                case LabelDirection.W:
                    minX = ax - offset - w; minY = ay - h / 2.0; break;
                case LabelDirection.N:
                    minX = ax - w / 2.0; minY = ay + offset; break;
                case LabelDirection.S:
                    minX = ax - w / 2.0; minY = ay - offset - h; break;
                case LabelDirection.NE:
                    minX = ax + offset; minY = ay + offset; break;
                case LabelDirection.SE:
                    minX = ax + offset; minY = ay - offset - h; break;
                case LabelDirection.NW:
                    minX = ax - offset - w; minY = ay + offset; break;
                default: // SW
                    minX = ax - offset - w; minY = ay - offset - h; break;
            }

            return Box2d.FromSize(minX, minY, w, h);
        }

        private static bool HitsHard(Box2d box, IList<Obstacle> obstacles)
        {
            for (var i = 0; i < obstacles.Count; i++)
            {
                var o = obstacles[i];
                if (o == null || o.Class != ObstacleClass.Hard) continue;
                if (box.Intersects(o.Bounds)) return true;
            }
            return false;
        }

        private static double SoftArea(Box2d box, IList<Obstacle> obstacles)
        {
            var total = 0.0;
            for (var i = 0; i < obstacles.Count; i++)
            {
                var o = obstacles[i];
                if (o == null || o.Class != ObstacleClass.Soft) continue;
                total += box.OverlapArea(o.Bounds);
            }
            return total;
        }
    }
}
