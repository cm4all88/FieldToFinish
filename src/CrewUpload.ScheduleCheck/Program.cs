using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using CrewUpload.Schedule;

namespace CrewUpload.ScheduleCheck
{
    /// <summary>
    /// ScheduleCheck &lt;schedule folder&gt; [--date yyyy-MM-dd ...] [--person employee_id] [--assembled out.json] [--out report.txt]
    ///
    /// Reads the folder exactly as Crew Upload's integration does and prints what it sees on each date.
    /// Fingerprints every schedule file before and after; exit code 2 if anything changed while it ran
    /// (which would be someone else's save -- this program opens the files for reading only).
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            if (args.Length == 0 || args[0].StartsWith("-", StringComparison.Ordinal))
            {
                Console.Error.WriteLine("ScheduleCheck <schedule folder> [--date yyyy-MM-dd ...] [--person employee_id] [--assembled out.json] [--out report.txt]");
                return 1;
            }
            var folder = args[0];
            var dates = new List<DateTime>();
            string person = null, assembled = null, outFile = null;
            for (var i = 1; i < args.Length; i++)
            {
                var a = args[i];
                var v = i + 1 < args.Length ? args[i + 1] : null;
                if (a == "--date" && v != null) { dates.Add(DateTime.ParseExact(v, "yyyy-MM-dd", CultureInfo.InvariantCulture)); i++; }
                else if (a == "--person" && v != null) { person = v; i++; }
                else if (a == "--assembled" && v != null) { assembled = v; i++; }
                else if (a == "--out" && v != null) { outFile = v; i++; }
                else { Console.Error.WriteLine("Unknown argument: " + a); return 1; }
            }
            if (dates.Count == 0) dates.Add(DateTime.Today);

            var before = ScheduleDiagnostics.Fingerprint(folder);
            var report = new System.Text.StringBuilder();
            report.AppendLine("ScheduleCheck " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " on " + Environment.MachineName + " as " + Environment.UserName);
            report.AppendLine("Files before:");
            foreach (var f in before) report.AppendLine("  " + f);
            report.AppendLine();
            report.Append(ScheduleDiagnostics.Report(folder, dates, person));

            if (assembled != null)
            {
                var json = ScheduleDiagnostics.AssembledJson(folder);
                if (json == null) { report.AppendLine("No assembled output: the schedule could not be read."); }
                else { File.WriteAllText(assembled, json); report.AppendLine("Assembled schedule written to " + assembled); }
            }

            var after = ScheduleDiagnostics.Fingerprint(folder);
            var changed = before.Select(b => b.Name).Union(after.Select(a => a.Name), StringComparer.OrdinalIgnoreCase)
                .Where(n => !Same(before.FirstOrDefault(b => b.Name == n), after.FirstOrDefault(a => a.Name == n))).ToList();
            report.AppendLine();
            report.AppendLine(changed.Count == 0
                ? "Schedule files UNCHANGED (" + after.Count + " files, SHA-256 identical before and after)."
                : "CHANGED while this ran: " + string.Join(", ", changed) + ". This program only reads; check whether someone saved the schedule meanwhile.");

            Console.Write(report.ToString());
            if (outFile != null) File.WriteAllText(outFile, report.ToString());
            return changed.Count == 0 ? 0 : 2;
        }

        private static bool Same(FileFingerprint a, FileFingerprint b) =>
            a != null && b != null && a.Length == b.Length && a.Sha256 == b.Sha256 && a.LastWriteUtc == b.LastWriteUtc;
    }
}
