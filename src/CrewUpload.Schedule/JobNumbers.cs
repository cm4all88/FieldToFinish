using System.Text.RegularExpressions;

namespace CrewUpload.Schedule
{
    /// <summary>
    /// Where the schedule keeps a project's number: the app's jobNumOf() (jobNum, else a number in the
    /// name), plus the unhyphenated 10-digit form the hand-off found in live data ("2472535013").
    /// </summary>
    internal static class JobNumbers
    {
        // jobNumOf(): /\b\d{3}-\d{3,4}-\d{2,3}\b/
        private static readonly Regex Hyphenated = new Regex(@"\b\d{3}-\d{3,4}-\d{2,3}\b", RegexOptions.CultureInvariant);
        private static readonly Regex Flat = new Regex(@"\b(\d{3})(\d{4})(\d{3})\b", RegexOptions.CultureInvariant);

        // a trailing task in the name: "TDLE 554-1800-119 3.1.3" -> 3.1.3
        private static readonly Regex DottedTask = new Regex(@"(?<![\d.-])\d{1,3}(?:\.\d{1,3})+(?![\d.-])", RegexOptions.CultureInvariant);

        public static string JobNumOf(ProjectRecord p)
        {
            if (p == null) return null;
            if (!string.IsNullOrWhiteSpace(p.JobNum)) return p.JobNum.Trim();
            return JobNumOf(p.Name);
        }

        public static string JobNumOf(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var m = Hyphenated.Match(name);
            if (m.Success) return m.Value;
            var f = Flat.Match(name);
            return f.Success ? f.Groups[1].Value + "-" + f.Groups[2].Value + "-" + f.Groups[3].Value : null;
        }

        /// <summary>The project's task: its taskNum, else a dotted task in the name, else null.</summary>
        public static string TaskOf(ProjectRecord p)
        {
            if (p == null) return null;
            if (!string.IsNullOrWhiteSpace(p.TaskNum)) return p.TaskNum.Trim();
            if (string.IsNullOrEmpty(p.Name)) return null;
            var rest = Hyphenated.Replace(p.Name, " ");
            var m = DottedTask.Match(rest);
            return m.Success ? m.Value : null;
        }
    }
}
