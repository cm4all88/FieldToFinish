using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FieldCodes.Tagging
{
    /// <summary>Columns available in the model-space table.</summary>
    public enum TagTableColumn
    {
        Tag = 0,
        PointNumber = 1,
        Code = 2,
        Species = 3,
        Size = 4,
        StemCount = 5,
        Stems = 6,
        DripRadius = 7,
        Label = 8,
        Notes = 9
    }

    /// <summary>A table ready to draw: a title, headers, and rows of plain strings.</summary>
    public sealed class TagTableModel
    {
        public string Title { get; set; }
        public IList<string> Headers { get; set; }
        public IList<IList<string>> Rows { get; set; }

        public TagTableModel()
        {
            Headers = new List<string>();
            Rows = new List<IList<string>>();
        }

        public int ColumnCount { get { return Headers.Count; } }
        public int RowCount { get { return Rows.Count; } }
    }

    /// <summary>
    /// Builds the tag table from parsed points.
    ///
    /// Kept free of Autodesk types so the content -- which is what gets reviewed by the
    /// agency -- can be tested without a drawing. The CAD layer only turns this into a
    /// Table entity.
    /// </summary>
    public static class TagTable
    {
        public static string HeaderFor(TagTableColumn column)
        {
            switch (column)
            {
                case TagTableColumn.Tag: return "TAG";
                case TagTableColumn.PointNumber: return "PT";
                case TagTableColumn.Code: return "CODE";
                case TagTableColumn.Species: return "SPECIES";
                case TagTableColumn.Size: return "SIZE (IN)";
                case TagTableColumn.StemCount: return "STEMS";
                case TagTableColumn.Stems: return "STEM SIZES";
                case TagTableColumn.DripRadius: return "DRIP (FT)";
                case TagTableColumn.Label: return "LABEL";
                default: return "NOTES";
            }
        }

        /// <summary>Columns a tree schedule normally carries.</summary>
        public static IList<TagTableColumn> DefaultColumns()
        {
            return new List<TagTableColumn>
            {
                TagTableColumn.Tag,
                TagTableColumn.PointNumber,
                TagTableColumn.Species,
                TagTableColumn.Size,
                TagTableColumn.StemCount,
                TagTableColumn.DripRadius,
                TagTableColumn.Notes
            };
        }

        public static TagTableModel Build(IEnumerable<TagAssignment> assignments,
                                          IEnumerable<ParsedPoint> points,
                                          IList<TagTableColumn> columns,
                                          string title,
                                          int decimals)
        {
            if (assignments == null) throw new ArgumentNullException("assignments");
            if (points == null) throw new ArgumentNullException("points");

            var wanted = columns != null && columns.Count > 0 ? columns : DefaultColumns();

            var byPoint = new Dictionary<string, ParsedPoint>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in points)
            {
                if (p == null || string.IsNullOrEmpty(p.PointNumber)) continue;
                byPoint[p.PointNumber] = p;
            }

            var model = new TagTableModel { Title = title ?? "TREE SCHEDULE" };
            foreach (var column in wanted) model.Headers.Add(HeaderFor(column));

            // Tag order, not point order: the table is read by someone looking at a tag
            // on the plan and wanting its row.
            var ordered = assignments
                .OrderBy(a => a.Prefix, StringComparer.OrdinalIgnoreCase)
                .ThenBy(a => a.Number);

            foreach (var assignment in ordered)
            {
                ParsedPoint point;
                if (!byPoint.TryGetValue(assignment.PointNumber ?? string.Empty, out point))
                    continue;

                var row = new List<string>(wanted.Count);
                foreach (var column in wanted)
                    row.Add(ValueFor(column, assignment, point, decimals));

                model.Rows.Add(row);
            }

            return model;
        }

        private static string ValueFor(TagTableColumn column, TagAssignment assignment,
                                       ParsedPoint point, int decimals)
        {
            switch (column)
            {
                case TagTableColumn.Tag:
                    return assignment.Text;

                case TagTableColumn.PointNumber:
                    return point.PointNumber ?? string.Empty;

                case TagTableColumn.Code:
                    return point.Code ?? string.Empty;

                case TagTableColumn.Species:
                    return point.Species ?? string.Empty;

                case TagTableColumn.Size:
                    return point.TrunkInches.HasValue
                        ? point.TrunkInches.Value.ToString("F" + Clamp(decimals),
                                                           CultureInfo.InvariantCulture)
                        : string.Empty;

                case TagTableColumn.StemCount:
                    return point.Stems.Count > 1
                        ? point.Stems.Count.ToString(CultureInfo.InvariantCulture)
                        : string.Empty;

                case TagTableColumn.Stems:
                    // The raw stems matter: an agency using a different multi-stem
                    // formula cannot recompute the average from the average.
                    return point.Stems.Count > 1
                        ? string.Join(", ", point.Stems.Select(
                            s => s.ToString("0.##", CultureInfo.InvariantCulture)).ToArray())
                        : string.Empty;

                case TagTableColumn.DripRadius:
                    return point.DripRadius.HasValue
                        ? point.DripRadius.Value.ToString("0.##", CultureInfo.InvariantCulture)
                        : string.Empty;

                case TagTableColumn.Label:
                    return point.LabelText ?? string.Empty;

                default:
                    return Notes(point);
            }
        }

        /// <summary>
        /// Modifiers first, then anything the parser flagged. This column is what makes
        /// a dead or protected tree visible in the schedule.
        /// </summary>
        private static string Notes(ParsedPoint point)
        {
            var parts = new List<string>();

            foreach (var modifier in point.Modifiers)
                parts.Add(modifier.Id.ToUpperInvariant());

            foreach (var diagnostic in point.Diagnostics)
            {
                if (diagnostic.Severity != Severity.Warning) continue;
                if (diagnostic.Code == "FLAG") continue;      // already covered by the modifier
                parts.Add(diagnostic.Message);
            }

            return string.Join("; ", parts.ToArray());
        }

        private static int Clamp(int decimals)
        {
            if (decimals < 0) return 0;
            return decimals > 6 ? 6 : decimals;
        }
    }
}
