using System;
using System.Collections.Generic;

namespace FieldCodes.Compare
{
    /// <summary>
    /// Uniform grid over feature bounding boxes. A query for a box returns only the
    /// features whose boxes touch the same cells, so matching a survey feature costs
    /// a handful of candidates rather than a pass over the whole consultant drawing.
    /// Same idea as the drip-line CircleGrid, generalised to boxes.
    /// </summary>
    public sealed class SpatialGrid
    {
        private readonly IList<CompareFeature> _features;
        private readonly Dictionary<long, List<int>> _cells = new Dictionary<long, List<int>>();
        private readonly double _cell;
        private readonly bool _degenerate;

        /// <summary>Cell size in drawing units. 50 is a good default for site plans in feet:
        /// a few cells per short line, not thousands for a long one.</summary>
        public SpatialGrid(IList<CompareFeature> features, double cellSize)
        {
            _features = features ?? new List<CompareFeature>();
            _cell = cellSize;
            _degenerate = double.IsNaN(cellSize) || double.IsInfinity(cellSize) || cellSize <= 0;
            if (_degenerate) return;

            for (var i = 0; i < _features.Count; i++)
            {
                var b = _features[i].Bounds;
                if (b.IsEmpty) continue;
                foreach (var key in KeysFor(b))
                {
                    List<int> bucket;
                    if (!_cells.TryGetValue(key, out bucket))
                    {
                        bucket = new List<int>();
                        _cells[key] = bucket;
                    }
                    bucket.Add(i);
                }
            }
        }

        public int Count { get { return _features.Count; } }
        public CompareFeature this[int index] { get { return _features[index]; } }

        /// <summary>Indices of features whose bounds intersect the box, ascending.</summary>
        public IList<int> Query(Box2 box)
        {
            var result = new List<int>();
            if (box.IsEmpty) return result;

            if (_degenerate)
            {
                for (var i = 0; i < _features.Count; i++)
                    if (_features[i].Bounds.Intersects(box)) result.Add(i);
                return result;
            }

            var seen = new HashSet<int>();
            foreach (var key in KeysFor(box))
            {
                List<int> bucket;
                if (!_cells.TryGetValue(key, out bucket)) continue;
                for (var k = 0; k < bucket.Count; k++)
                {
                    var j = bucket[k];
                    if (!seen.Add(j)) continue;
                    if (_features[j].Bounds.Intersects(box)) result.Add(j);
                }
            }
            result.Sort();
            return result;
        }

        public IList<CompareFeature> QueryFeatures(Box2 box)
        {
            var found = new List<CompareFeature>();
            foreach (var i in Query(box)) found.Add(_features[i]);
            return found;
        }

        private IEnumerable<long> KeysFor(Box2 b)
        {
            var minX = Cell(b.MinX);
            var maxX = Cell(b.MaxX);
            var minY = Cell(b.MinY);
            var maxY = Cell(b.MaxY);
            for (var gx = minX; gx <= maxX; gx++)
                for (var gy = minY; gy <= maxY; gy++)
                    yield return ((long)gx << 32) ^ (uint)gy;
        }

        private int Cell(double v)
        {
            var c = Math.Floor(v / _cell);
            if (c > int.MaxValue / 2) return int.MaxValue / 2;
            if (c < int.MinValue / 2) return int.MinValue / 2;
            return (int)c;
        }
    }
}
