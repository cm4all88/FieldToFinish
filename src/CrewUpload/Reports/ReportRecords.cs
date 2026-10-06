using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace CrewUpload.Reports
{
    /// <summary>
    /// The structured record of every submitted daily report: one JSON file per report, named by its
    /// ReportID, in [adminFolder]\Records\yyyy\yyyy-MM. These records -- never PDF file names -- are
    /// what the admin view reads. Each record is also kept on the PC (the crew's report history); if
    /// the admin folder cannot be reached, the record waits in the PC's outbox and is sent later.
    /// Records are written once and never changed.
    /// </summary>
    public sealed class ReportRecords
    {
        public const string RecordsFolder = "Records";

        private readonly string _admin;
        private readonly string _local;

        public ReportRecords(string adminFolder, string localFolder)
        {
            _admin = string.IsNullOrWhiteSpace(adminFolder) ? null : adminFolder;
            _local = localFolder;
        }

        public static ReportRecords For(JobFolderConfig config) =>
            new ReportRecords((config.DailyReport ?? new DailyReportSettings()).AdminFolder, DefaultLocal);

        /// <summary>%LOCALAPPDATA%\FieldToFinish\CrewUpload\DailyReports</summary>
        public static string DefaultLocal =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FieldToFinish", "CrewUpload", "DailyReports");

        public string AdminRoot => _admin == null ? null : Path.Combine(_admin, RecordsFolder);
        private string HistoryFolder => Path.Combine(_local, "History");
        private string OutboxFolder => Path.Combine(_local, "Outbox");

        public string AdminFolderFor(DateTime date) =>
            AdminRoot == null ? null : Path.Combine(AdminRoot, date.ToString("yyyy", CultureInfo.InvariantCulture), date.ToString("yyyy-MM", CultureInfo.InvariantCulture));

        public static string FileNameOf(DailyReport r) => Naming.Clean(r.ReportId) + ".json";

        /// <summary>
        /// Records a submitted report: in the PC's history, then in the admin folder. Returns the admin
        /// record's path, or null with <paramref name="problem"/> when it is waiting in the outbox.
        /// </summary>
        public string Save(DailyReport r, out string problem)
        {
            if (string.IsNullOrEmpty(r.ReportId)) throw new ArgumentException("The report has no ReportID; submit it first.");
            problem = null;
            var json = JsonConvert.SerializeObject(r, Formatting.Indented);
            try { WriteOnce(Path.Combine(HistoryFolder, FileNameOf(r)), json); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { problem = "Not kept in this PC's history: " + e.Message; }

            try
            {
                if (_admin == null) throw new IOException("No admin folder is set.");
                var path = Path.Combine(AdminFolderFor(r.Date), FileNameOf(r));
                WriteOnce(path, json);
                return path;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is NotSupportedException)
            {
                try
                {
                    WriteOnce(Path.Combine(OutboxFolder, FileNameOf(r)), json);
                    problem = "The admin folder could not be reached (" + e.Message + "). The record is saved on this PC and will be sent next time.";
                }
                catch (Exception e2) when (e2 is IOException || e2 is UnauthorizedAccessException)
                {
                    problem = "The record could not be saved: " + e.Message + " / " + e2.Message;
                }
                return null;
            }
        }

        /// <summary>Records still waiting to reach the admin folder.</summary>
        public int Pending => Directory.Exists(OutboxFolder) ? Directory.GetFiles(OutboxFolder, "*.json").Length : 0;

        /// <summary>Sends what is in the outbox. Returns how many went; the rest stay for next time.</summary>
        public int SendPending(out string problem)
        {
            problem = null;
            if (_admin == null || !Directory.Exists(OutboxFolder)) return 0;
            var sent = 0;
            foreach (var file in Directory.GetFiles(OutboxFolder, "*.json"))
            {
                try
                {
                    var json = File.ReadAllText(file, Encoding.UTF8);
                    var r = JsonConvert.DeserializeObject<DailyReport>(json);
                    var path = Path.Combine(AdminFolderFor(r.Date), FileNameOf(r));
                    if (!File.Exists(path)) WriteOnce(path, json);
                    File.Delete(file);
                    sent++;
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is JsonException || e is ArgumentException)
                {
                    problem = e.Message;
                }
            }
            return sent;
        }

        /// <summary>This PC's submitted reports, newest first, and whether each has reached the admin folder.</summary>
        public List<KeyValuePair<DailyReport, bool>> History()
        {
            var waiting = Directory.Exists(OutboxFolder)
                ? new HashSet<string>(Directory.GetFiles(OutboxFolder, "*.json").Select(Path.GetFileName), StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>();
            return ReadFolder(HistoryFolder, SearchOption.TopDirectoryOnly, new List<string>())
                .OrderByDescending(r => r.SubmittedTime).ThenByDescending(r => r.Date)
                .Select(r => new KeyValuePair<DailyReport, bool>(r, !waiting.Contains(FileNameOf(r))))
                .ToList();
        }

        /// <summary>
        /// Every record in the admin folder for the dates given. A record that cannot be read is named
        /// in <paramref name="problems"/> and skipped; one bad file never hides the rest.
        /// </summary>
        public List<DailyReport> Read(DateTime from, DateTime to, List<string> problems)
        {
            var all = new List<DailyReport>();
            if (AdminRoot == null) { problems.Add("No admin folder is set."); return all; }
            for (var month = new DateTime(from.Year, from.Month, 1); month <= to.Date; month = month.AddMonths(1))
            {
                var folder = AdminFolderFor(month);
                try
                {
                    if (!Directory.Exists(folder)) continue;
                    all.AddRange(ReadFolder(folder, SearchOption.TopDirectoryOnly, problems).Where(r => r.Date.Date >= from.Date && r.Date.Date <= to.Date));
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    problems.Add(folder + ": " + e.Message);
                }
            }
            // a record sent twice (outbox and direct) is one report
            return all.GroupBy(r => r.ReportId, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
        }

        private static List<DailyReport> ReadFolder(string folder, SearchOption option, List<string> problems)
        {
            var list = new List<DailyReport>();
            if (!Directory.Exists(folder)) return list;
            foreach (var file in Directory.GetFiles(folder, "*.json", option))
            {
                try
                {
                    string text;
                    using (var s = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var rd = new StreamReader(s, Encoding.UTF8))
                        text = rd.ReadToEnd();
                    var r = JsonConvert.DeserializeObject<DailyReport>(text);
                    if (r == null || string.IsNullOrEmpty(r.ReportId)) throw new InvalidDataException("not a report record");
                    list.Add(r);
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is JsonException || e is InvalidDataException)
                {
                    problems.Add(Path.GetFileName(file) + ": " + e.Message);
                }
            }
            return list;
        }

        /// <summary>Writes a new file whole: to a temp name first, then renamed, so a reader never sees half a record.</summary>
        internal static void WriteOnce(string path, string json)
        {
            var dir = Path.GetDirectoryName(path);
            Directory.CreateDirectory(dir);
            if (File.Exists(path)) throw new IOException(Path.GetFileName(path) + " already exists; records are never replaced.");
            var temp = Path.Combine(dir, "." + Path.GetFileNameWithoutExtension(path) + "." + Guid.NewGuid().ToString("N").Substring(0, 6) + ".tmp");
            try
            {
                var bytes = new UTF8Encoding(false).GetBytes(json);
                using (var s = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    s.Write(bytes, 0, bytes.Length);
                    s.Flush(true);
                }
                File.Move(temp, path); // fails rather than replace
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
    }
}
