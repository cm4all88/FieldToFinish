using System.Collections.Generic;
using System.Linq;

namespace FieldCodes.Easements
{
    /// <summary>Commencement ties: a single straight course, or the courses of a line and curve the tie follows.</summary>
    public static class Ties
    {
        public static IList<CourseData> Of(CourseData tie, IList<CourseData> courses)
        {
            if (courses != null && courses.Count > 0) return courses.ToList();
            return tie == null ? new List<CourseData>() : new List<CourseData> { tie };
        }

        /// <summary>True when the tie is more than one straight course: it follows the ground as drawn.</summary>
        public static bool Follows(IList<CourseData> courses)
        {
            return courses != null && (courses.Count > 1 || courses.Any(c => c.Course.Kind == CourseKind.Arc));
        }

        /// <summary>Tie courses from a path, described; a single straight course is kept as a plain tie (no course list).</summary>
        public static void Set(IList<Course> path, out CourseData tie, out List<CourseData> courses)
        {
            var described = path.Select(EasementAnnotation.Describe).ToList();
            tie = described.Count > 0 ? described[0] : null;
            courses = Follows(described) ? described : null;
        }
    }
}
