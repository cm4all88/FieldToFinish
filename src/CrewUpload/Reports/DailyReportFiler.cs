using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace CrewUpload.Reports
{
    /// <summary>Where one copy of a submitted report went, or why it could not.</summary>
    public sealed class FiledCopy
    {
        public const string Project = "project", Admin = "admin", Local = "local", Record = "record";

        public string Kind { get; set; }
        public string Path { get; set; }
        public string Error { get; set; }
        public bool Ok => Error == null && Path != null;
    }

    public sealed class SubmitResult
    {
        public DailyReport Report { get; set; }
        public List<FiledCopy> Copies { get; set; } = new List<FiledCopy>();
        public bool Filed => Copies.Any(c => c.Ok);
        public FiledCopy Copy(string kind) => Copies.FirstOrDefault(c => c.Kind == kind);
    }

    /// <summary>
    /// Submits a daily report: gives it its ReportID, draws the PDF once, and files a copy in the
    /// project (beside the crew's download, in Unprocessed), one in the admin folder and one on this
    /// PC. Nothing is ever overwritten -- a second report the same day is filed as -2. One copy
    /// failing (the share is down) does not stop the others.
    /// </summary>
    public sealed class DailyReportFiler
    {
        private readonly JobFolderConfig _config;

        public DailyReportFiler(JobFolderConfig config) { _config = config; Records = ReportRecords.For(config); }

        /// <summary>Where the structured record goes after the PDFs are filed; null for no record.</summary>
        public ReportRecords Records { get; set; }

        private DailyReportSettings Form => _config.DailyReport ?? new DailyReportSettings();

        /// <summary>The admin folder for a report date: [adminFolder]\2026\2026-05.</summary>
        public string AdminFolderFor(DateTime date) =>
            string.IsNullOrWhiteSpace(Form.AdminFolder) ? null
            : Path.Combine(Form.AdminFolder, date.ToString("yyyy", CultureInfo.InvariantCulture), date.ToString("yyyy-MM", CultureInfo.InvariantCulture));

        public string LocalFolder =>
            string.IsNullOrWhiteSpace(Form.LocalFolder) ? null : Environment.ExpandEnvironmentVariables(Form.LocalFolder);

        /// <summary>20260507-JBB-1800-119-STK-DR.pdf: the download's name with the report suffix.</summary>
        public string FileName(DailyReport r)
        {
            var stem = !string.IsNullOrWhiteSpace(r.DataFileName) ? r.DataFileName.Trim()
                : r.Date.ToString(_config.DateFormat, CultureInfo.InvariantCulture) + "-" + (r.Lead?.Initials ?? "X") + "-" + JobFolderConfig.NormalizeProjectNumber(r.ProjectNumber);
            return Naming.Clean(stem + (Form.FileSuffix ?? string.Empty)) + ".pdf";
        }

        public byte[] Render(DailyReport r) => DailyReportPdf.Render(r, Form);

        /// <param name="report">The crew's report; its ReportID, SubmittedBy/Time and PDF paths are filled in.</param>
        /// <param name="project">The registered project, or null when the report's project is not registered (no project copy then).</param>
        public SubmitResult Submit(DailyReport report, ProjectFolder project)
        {
            var problems = report.Problems();
            if (problems.Count > 0) throw new InvalidOperationException(string.Join(" ", problems));

            report.ReportId = DailyReport.NewId(report.Date, report.Lead?.Initials);
            report.SubmittedBy = RegistryLog.UserName;
            report.SubmittedTime = DateTimeOffset.Now;
            var name = FileName(report);
            var result = new SubmitResult { Report = report };

            var projectCopy = new FiledCopy { Kind = FiledCopy.Project };
            if (project == null) projectCopy.Error = "The project is not registered, so no copy was filed in its folder.";
            else
            {
                var folder = Path.Combine(project.UploadRoot, Naming.Clean(string.IsNullOrWhiteSpace(report.DataFileName) ? Path.GetFileNameWithoutExtension(name) : report.DataFileName.Trim()));
                if (!UploadRunner.IsInside(folder, project.UploadRoot)) projectCopy.Error = "Refused: " + folder + " is outside the project's Unprocessed folder.";
                else projectCopy.Path = folder;
            }
            var adminCopy = new FiledCopy { Kind = FiledCopy.Admin, Path = AdminFolderFor(report.Date) };
            if (adminCopy.Path == null) adminCopy.Error = "No admin folder is set.";
            var localCopy = new FiledCopy { Kind = FiledCopy.Local, Path = LocalFolder };
            if (localCopy.Path == null) localCopy.Error = "No local folder is set.";
            result.Copies.AddRange(new[] { projectCopy, adminCopy, localCopy });

            var pdf = Render(report);
            foreach (var c in result.Copies.Where(c => c.Error == null))
            {
                try
                {
                    Directory.CreateDirectory(c.Path);
                    c.Path = Unused(Path.Combine(c.Path, name));
                    WriteNew(c.Path, pdf);
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is NotSupportedException || e is ArgumentException)
                {
                    c.Error = e.Message;
                }
            }
            report.AdminPdfPath = adminCopy.Ok ? adminCopy.Path : null;
            report.PdfPath = projectCopy.Ok ? projectCopy.Path : report.AdminPdfPath;
            if (report.PdfPath == null) report.PdfPath = result.Copies.FirstOrDefault(c => c.Ok)?.Path;

            // The record says where the PDFs went, so it is written last. Only a report that was filed somewhere is recorded.
            if (Records != null && result.Filed)
            {
                string problem;
                var record = new FiledCopy { Kind = FiledCopy.Record };
                try
                {
                    record.Path = Records.Save(report, out problem);
                    record.Error = record.Path == null ? problem ?? "Not recorded." : null;
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException)
                {
                    record.Error = e.Message;
                }
                result.Copies.Add(record);
            }
            return result;
        }

        /// <summary>The path, or the first free -2, -3 ... beside it: an existing report is never replaced.</summary>
        internal static string Unused(string path)
        {
            if (!File.Exists(path)) return path;
            var dir = Path.GetDirectoryName(path);
            var stem = Path.GetFileNameWithoutExtension(path);
            var ext = Path.GetExtension(path);
            for (var n = 2; ; n++)
            {
                var p = Path.Combine(dir, stem + "-" + n + ext);
                if (!File.Exists(p)) return p;
            }
        }

        /// <summary>Writes a file that must not exist yet (CreateNew), flushed to disk.</summary>
        internal static void WriteNew(string path, byte[] bytes)
        {
            using (var s = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                s.Write(bytes, 0, bytes.Length);
                s.Flush(true);
            }
        }
    }
}
