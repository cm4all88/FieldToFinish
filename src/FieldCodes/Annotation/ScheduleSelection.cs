using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FieldCodes.Settings;
using FieldCodes.Tagging;

namespace FieldCodes.Annotation
{
    /// <summary>
    /// Which rows the schedule (FTFTABLE) lists. Trees are in it only when the tree schedule is
    /// switched on -- whatever the tree annotation -- so tags and schedule are chosen
    /// separately: tagged trees are listed by tag, trees annotated with their description (or
    /// nothing) by point number. Other tagged features are listed as before.
    /// </summary>
    public static class ScheduleSelection
    {
        public static IList<TagAssignment> Rows(IEnumerable<TagAssignment> tags, IEnumerable<ParsedPoint> points,
                                                AnnotationSettings settings, RulesConfig rules = null)
        {
            var s = settings ?? new AnnotationSettings();
            var byPoint = new Dictionary<string, ParsedPoint>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in points ?? Enumerable.Empty<ParsedPoint>())
                if (p != null && !string.IsNullOrEmpty(p.PointNumber)) byPoint[p.PointNumber] = p;

            Func<string, bool> isTree = n =>
            {
                ParsedPoint p;
                return byPoint.TryGetValue(n ?? string.Empty, out p) && AnnotationPolicy.CategoryOf(p, rules) == AnnotationCategory.Trees;
            };

            var rows = (tags ?? Enumerable.Empty<TagAssignment>())
                .Where(t => t != null && (s.ScheduleTrees || !isTree(t.PointNumber)))
                .ToList();

            if (s.ScheduleTrees)
            {
                var listed = new HashSet<string>(rows.Select(r => r.PointNumber), StringComparer.OrdinalIgnoreCase);
                foreach (var p in byPoint.Values)
                {
                    if (listed.Contains(p.PointNumber) || p.HasErrors || p.Ignored || p.Unhandled) continue;
                    if (AnnotationPolicy.CategoryOf(p, rules) != AnnotationCategory.Trees) continue;
                    int number;
                    int.TryParse(p.PointNumber, NumberStyles.Integer, CultureInfo.InvariantCulture, out number);
                    rows.Add(new TagAssignment { PointNumber = p.PointNumber, Prefix = string.Empty, Number = number, Untagged = true });
                }
            }
            return rows;
        }
    }
}
