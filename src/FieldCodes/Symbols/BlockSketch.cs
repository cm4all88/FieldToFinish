using System;
using System.Collections.Generic;
using System.Linq;
using FieldCodes.Easements;

namespace FieldCodes.Symbols
{
    /// <summary>One drawn path of a block preview: a polyline in block coordinates, the block's
    /// base point (where it is inserted) at 0,0.</summary>
    public sealed class SketchPath
    {
        public SketchPath(IEnumerable<P2> points, bool closed)
        {
            Points = (points ?? Enumerable.Empty<P2>()).ToList();
            Closed = closed;
        }

        public IList<P2> Points { get; private set; }
        public bool Closed { get; private set; }
    }

    /// <summary>
    /// A block definition reduced to what a preview can draw: lines, polylines (bulges as short
    /// chords), arcs and circles, every one a polyline in block coordinates with the block's base
    /// point at 0,0 -- the point the block is inserted at. What it cannot draw (text, hatches,
    /// splines, ellipses, references nested more than one level) is counted, so the preview can say
    /// it is simplified rather than pretend to be the symbol.
    ///
    /// No Autodesk types: the CAD layer reads a BlockTableRecord into one of these
    /// (FieldCodes.Cad.BlockSketchReader), the tests build them by hand.
    /// </summary>
    public sealed class BlockSketch
    {
        /// <summary>Chords per full turn when an arc, circle or bulge is drawn as straight pieces.</summary>
        public const int SegmentsPerCircle = 48;

        private readonly List<SketchPath> _paths = new List<SketchPath>();
        private readonly List<string> _skipped = new List<string>();
        private readonly List<string> _attributeTags = new List<string>();

        public BlockSketch(string name)
        {
            Name = name ?? string.Empty;
        }

        public string Name { get; private set; }

        public IList<SketchPath> Paths { get { return _paths; } }

        /// <summary>One entry per entity that could not be drawn, by kind (Text, Hatch...).</summary>
        public IList<string> Skipped { get { return _skipped; } }

        /// <summary>The tags of the block's (non-constant) attribute definitions, in drawing order.</summary>
        public IList<string> AttributeTags { get { return _attributeTags; } }

        /// <summary>True when something in the block was left out of the drawing.</summary>
        public bool Simplified { get { return _skipped.Count > 0; } }

        public bool IsEmpty { get { return _paths.Count == 0; } }

        /// <summary>"3 text, 1 hatch": what was left out, for the "simplified preview" note.</summary>
        public string SkippedSummary()
        {
            return string.Join(", ", _skipped.GroupBy(k => k, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.Count() + " " + g.Key.ToLowerInvariant()).ToArray());
        }

        public void Skip(string kind)
        {
            _skipped.Add(string.IsNullOrWhiteSpace(kind) ? "object" : kind.Trim());
        }

        public void AddAttributeTag(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return;
            if (!_attributeTags.Contains(tag.Trim(), StringComparer.OrdinalIgnoreCase)) _attributeTags.Add(tag.Trim());
        }

        public void AddLine(P2 a, P2 b)
        {
            _paths.Add(new SketchPath(new[] { a, b }, false));
        }

        /// <summary>A lightweight polyline. <paramref name="bulges"/>[i] belongs to the segment from
        /// vertex i to vertex i+1 (and the last one to the closing segment): tan of a quarter of the
        /// included angle, positive counter-clockwise, as AutoCAD stores it. Null means straight.</summary>
        public void AddPolyline(IList<P2> vertices, IList<double> bulges, bool closed)
        {
            if (vertices == null || vertices.Count == 0) return;
            var points = new List<P2> { vertices[0] };
            var segments = closed ? vertices.Count : vertices.Count - 1;
            for (var i = 0; i < segments; i++)
            {
                var a = vertices[i];
                var b = vertices[(i + 1) % vertices.Count];
                var bulge = bulges != null && i < bulges.Count ? bulges[i] : 0.0;
                points.AddRange(BulgePoints(a, b, bulge, SegmentsPerCircle));
            }
            // A closed path's last point repeats the first; the path itself records that it closes.
            if (closed && points.Count > 1) points.RemoveAt(points.Count - 1);
            if (points.Count == 1) points.Add(points[0]);
            _paths.Add(new SketchPath(points, closed));
        }

        /// <summary>An arc from <paramref name="startRadians"/> counter-clockwise to
        /// <paramref name="endRadians"/>, as an AutoCAD Arc stores it (angles from east).</summary>
        public void AddArc(P2 center, double radius, double startRadians, double endRadians)
        {
            if (!(radius > 0)) return;
            var sweep = endRadians - startRadians;
            while (sweep <= 0) sweep += 2 * Math.PI;
            while (sweep > 2 * Math.PI + 1e-12) sweep -= 2 * Math.PI;
            var n = Math.Max(2, (int)Math.Ceiling(sweep / (2 * Math.PI) * SegmentsPerCircle));
            var points = new List<P2>(n + 1);
            for (var k = 0; k <= n; k++)
            {
                var a = startRadians + sweep * k / n;
                points.Add(new P2(center.X + radius * Math.Cos(a), center.Y + radius * Math.Sin(a)));
            }
            _paths.Add(new SketchPath(points, false));
        }

        public void AddCircle(P2 center, double radius)
        {
            if (!(radius > 0)) return;
            var points = new List<P2>(SegmentsPerCircle);
            for (var k = 0; k < SegmentsPerCircle; k++)
            {
                var a = 2 * Math.PI * k / SegmentsPerCircle;
                points.Add(new P2(center.X + radius * Math.Cos(a), center.Y + radius * Math.Sin(a)));
            }
            _paths.Add(new SketchPath(points, true));
        }

        /// <summary>A filled 2D solid or any closed outline, drawn as its edge.</summary>
        public void AddOutline(IList<P2> points)
        {
            if (points == null || points.Count < 2) return;
            _paths.Add(new SketchPath(points, true));
        }

        /// <summary>
        /// A block nested in this one, inserted at <paramref name="position"/> (this block's
        /// coordinates) with its rotation (radians, counter-clockwise) and scale. The inner sketch
        /// is in its own block coordinates (its base point at 0,0). What the inner block skipped is
        /// skipped here too, so a nested hatch still makes the preview "simplified".
        /// </summary>
        public void AddNested(BlockSketch inner, P2 position, double rotationRadians, double scaleX, double scaleY)
        {
            if (inner == null) return;
            var cos = Math.Cos(rotationRadians);
            var sin = Math.Sin(rotationRadians);
            Func<P2, P2> map = p =>
            {
                var x = p.X * scaleX;
                var y = p.Y * scaleY;
                return new P2(position.X + x * cos - y * sin, position.Y + x * sin + y * cos);
            };
            foreach (var path in inner.Paths)
                _paths.Add(new SketchPath(path.Points.Select(map), path.Closed));
            foreach (var kind in inner.Skipped) _skipped.Add(kind);
        }

        /// <summary>Everything moved so <paramref name="basePoint"/> becomes 0,0 (the CAD reader
        /// collects in definition coordinates, then moves the block's own base point to the origin).</summary>
        public void MoveBasePoint(P2 basePoint)
        {
            if (Math.Abs(basePoint.X) < 1e-15 && Math.Abs(basePoint.Y) < 1e-15) return;
            for (var i = 0; i < _paths.Count; i++)
                _paths[i] = new SketchPath(_paths[i].Points.Select(p => p - basePoint), _paths[i].Closed);
        }

        /// <summary>The furthest any drawn point lies from the insertion point (0,0). The preview
        /// fits this circle, so turning the symbol never changes the zoom.</summary>
        public double Radius()
        {
            var r = 0.0;
            foreach (var path in _paths)
                foreach (var p in path.Points)
                {
                    var d = p.Length;
                    if (d > r && !double.IsNaN(d) && !double.IsInfinity(d)) r = d;
                }
            return r;
        }

        /// <summary>
        /// The points along a polyline segment from <paramref name="a"/> to <paramref name="b"/>
        /// with the given bulge, excluding <paramref name="a"/> and ending exactly on
        /// <paramref name="b"/>. A zero bulge is the straight segment (just <paramref name="b"/>).
        /// </summary>
        public static IList<P2> BulgePoints(P2 a, P2 b, double bulge, int segmentsPerCircle)
        {
            var chord = a.DistanceTo(b);
            if (Math.Abs(bulge) < 1e-12 || chord < 1e-12) return new List<P2> { b };

            // Included angle (signed: positive counter-clockwise) and the centre, which sits on the
            // chord's perpendicular bisector: (c/2)(1 - bulge^2)/(2 bulge) to the left of a->b.
            var theta = 4.0 * Math.Atan(bulge);
            var mid = new P2((a.X + b.X) / 2.0, (a.Y + b.Y) / 2.0);
            var dir = new P2((b.X - a.X) / chord, (b.Y - a.Y) / chord);
            var left = new P2(-dir.Y, dir.X);
            var offset = chord / 2.0 * (1.0 - bulge * bulge) / (2.0 * bulge);
            var center = mid + left * offset;
            var radius = center.DistanceTo(a);
            var start = Math.Atan2(a.Y - center.Y, a.X - center.X);

            var n = Math.Max(2, (int)Math.Ceiling(Math.Abs(theta) / (2 * Math.PI) * Math.Max(4, segmentsPerCircle)));
            var points = new List<P2>(n);
            for (var k = 1; k < n; k++)
            {
                var ang = start + theta * k / n;
                points.Add(new P2(center.X + radius * Math.Cos(ang), center.Y + radius * Math.Sin(ang)));
            }
            points.Add(b);
            return points;
        }
    }
}
