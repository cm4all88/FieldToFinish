using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace CrewUpload
{
    /// <summary>One file a crew dropped, and where it is going.</summary>
    public sealed class UploadItem
    {
        public string SourcePath { get; set; }

        /// <summary>As the crew dropped it: "Photos 0603\IMG_0412.JPG".</summary>
        public string DroppedAs { get; set; }

        /// <summary>Folders between what was dropped and the file, outermost first. Their names are hints.</summary>
        public IList<string> FolderHints { get; set; } = new List<string>();

        public UploadCategory Category { get; set; }

        /// <summary>The date that goes into its name and dated folder.</summary>
        public DateTime Date { get; set; }

        /// <summary>Full path it will be copied to. Set by <see cref="UploadPlanner.Assign"/>.</summary>
        public string Destination { get; set; }

        /// <summary>Set when an identical file is already in the job: it is not copied again.</summary>
        public string AlreadyUploadedAs { get; set; }

        /// <summary>Why it was not uploaded, after <see cref="UploadRunner.Run"/>.</summary>
        public string Error { get; set; }

        public bool Done { get; set; }

        public bool Skip => AlreadyUploadedAs != null;
    }

    /// <summary>
    /// Turns what a crew dropped into a list of files with a category, a correct name and a
    /// place in the job folder. Nothing is copied here, so the crew sees and can correct the
    /// whole plan first.
    /// </summary>
    public sealed class UploadPlanner
    {
        private static readonly HashSet<string> Junk = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "thumbs.db", "desktop.ini", ".ds_store", "ehthumbs.db"
        };

        private readonly JobFolderConfig _config;

        public UploadPlanner(JobFolderConfig config)
        {
            _config = config;
        }

        /// <summary>
        /// Files and folders as dropped. A folder brings in every file under it; its name and
        /// its subfolders' names become hints ("Lineouts\..." is a lineout). Hidden files, Office
        /// lock files (~$...) and Windows thumbnail caches are left behind.
        /// With <paramref name="category"/> -- the crew dropped onto that type's box -- every file
        /// is that type and nothing is guessed.
        /// </summary>
        public List<UploadItem> Collect(IEnumerable<string> dropped, UploadCategory category = null)
        {
            var items = new List<UploadItem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in dropped ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                var path = Path.GetFullPath(raw);
                if (Directory.Exists(path))
                {
                    var root = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    AddFolder(items, seen, path, new List<string> { root }, root);
                }
                else if (File.Exists(path))
                {
                    Add(items, seen, path, new List<string>(), Path.GetFileName(path));
                }
            }
            if (category != null)
                foreach (var item in items) item.Category = category;
            return items;
        }

        private void AddFolder(List<UploadItem> items, HashSet<string> seen, string dir, List<string> hints, string shown)
        {
            foreach (var file in Directory.GetFiles(dir).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                Add(items, seen, file, hints, shown + "\\" + Path.GetFileName(file));
            foreach (var sub in Directory.GetDirectories(dir).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                var name = Path.GetFileName(sub);
                if (name.StartsWith(".", StringComparison.Ordinal) || IsHidden(sub)) continue;
                AddFolder(items, seen, sub, new List<string>(hints) { name }, shown + "\\" + name);
            }
        }

        private void Add(List<UploadItem> items, HashSet<string> seen, string file, List<string> hints, string shown)
        {
            var name = Path.GetFileName(file);
            if (Junk.Contains(name) || name.StartsWith("~$", StringComparison.Ordinal) || name.StartsWith(".", StringComparison.Ordinal) || IsHidden(file)) return;
            if (!seen.Add(file)) return;
            var item = new UploadItem { SourcePath = file, DroppedAs = shown, FolderHints = hints };
            item.Category = Classify(name, hints);
            items.Add(item);
        }

        private static bool IsHidden(string path)
        {
            try { return (File.GetAttributes(path) & (FileAttributes.Hidden | FileAttributes.System)) != 0; }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return true; }
        }

        /// <summary>
        /// The category for a file: a keyword in its own name first, then in the folders it came
        /// in (nearest first), then its extension, then the fallback. Categories are tried in
        /// config order, so the more specific ones go first.
        /// </summary>
        public UploadCategory Classify(string fileName, IList<string> folderHints = null)
        {
            var bare = Path.GetFileNameWithoutExtension(fileName);
            var byName = _config.Categories.FirstOrDefault(c => c.Keywords.Any(k => Naming.HasWord(bare, k)));
            if (byName != null) return byName;

            if (folderHints != null)
                for (var i = folderHints.Count - 1; i >= 0; i--)
                {
                    var hint = folderHints[i];
                    var byFolder = _config.Categories.FirstOrDefault(c => c.Keywords.Any(k => Naming.HasWord(hint, k)));
                    if (byFolder != null) return byFolder;
                }

            var ext = Path.GetExtension(fileName);
            var byExt = _config.Categories.FirstOrDefault(c => c.Extensions.Any(e => string.Equals(e, ext, StringComparison.OrdinalIgnoreCase)));
            return byExt ?? _config.FallbackCategory;
        }

        /// <summary>
        /// Gives every item its date, destination and name. Numbers continue from what is already
        /// in the job folder and from earlier items in the same batch, so nothing is overwritten.
        /// A file whose exact contents are already in its destination folder is marked
        /// <see cref="UploadItem.AlreadyUploadedAs"/> instead: dropping the same card twice
        /// does not make a second copy. Call again after the crew changes a type.
        /// </summary>
        public void Assign(ProjectFolder project, IList<UploadItem> items, string crew, DateTime fieldDate)
        {
            var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in items)
            {
                item.Destination = null;
                item.AlreadyUploadedAs = null;
                item.Category = item.Category ?? Classify(Path.GetFileName(item.SourcePath), item.FolderHints);
                item.Date = item.Category.UseFileDate ? FileDate(item.SourcePath, fieldDate) : fieldDate.Date;

                var values = Values(project, item.Category, crew, item.Date, Path.GetFileNameWithoutExtension(item.SourcePath));
                var folder = Naming.Combine(project.Path, item.Category.Folder, values);

                var existing = SameContent(folder, item.SourcePath);
                if (existing != null)
                {
                    item.AlreadyUploadedAs = existing;
                    continue;
                }

                item.Destination = NextFreeName(folder, PatternFor(item.Category), values, Path.GetExtension(item.SourcePath), reserved);
            }
        }

        internal string PatternFor(UploadCategory category) =>
            string.IsNullOrWhiteSpace(category.FileName) ? _config.FileName : category.FileName;

        internal Dictionary<string, string> Values(ProjectFolder project, UploadCategory category, string crew, DateTime date, string original)
        {
            return new Dictionary<string, string>
            {
                { "projectNumber", project.Info.ProjectNumber },
                { "projectName", project.Info.ProjectName },
                { "client", project.Info.Client },
                { "code", category.Code },
                { "category", category.Name },
                { "date", date.ToString(_config.DateFormat, CultureInfo.InvariantCulture) },
                { "year", date.Year.ToString(CultureInfo.InvariantCulture) },
                { "crew", (crew ?? string.Empty).Trim().ToUpperInvariant() },
                { "original", original ?? string.Empty },
            };
        }

        /// <summary>
        /// The first name not on disk and not already taken in this batch. With {seq} in the
        /// pattern the first file is -01; without it the first file gets the plain name and a
        /// clash adds -02, -03 ...
        /// </summary>
        internal string NextFreeName(string folder, string pattern, Dictionary<string, string> values, string extension, HashSet<string> reserved)
        {
            var ext = (extension ?? string.Empty).ToLowerInvariant();
            var hasSeq = pattern.IndexOf("{seq}", StringComparison.Ordinal) >= 0;
            for (var n = 1; n < 100000; n++)
            {
                var v = new Dictionary<string, string>(values) { ["seq"] = Naming.Sequence(n, _config.SequenceDigits) };
                var name = Naming.Clean(Naming.Fill(pattern, v));
                if (!hasSeq && n > 1) name += "-" + v["seq"];
                if (name.Length == 0) name = "upload";
                var candidate = Path.Combine(folder, name + ext);
                if (reserved.Contains(candidate) || File.Exists(candidate) || Directory.Exists(candidate)) continue;
                reserved.Add(candidate);
                return candidate;
            }
            throw new IOException("No free file name left in " + folder);
        }

        private static DateTime FileDate(string path, DateTime fallback)
        {
            try { return File.GetLastWriteTime(path).Date; }
            catch (IOException) { return fallback.Date; }
            catch (UnauthorizedAccessException) { return fallback.Date; }
        }

        /// <summary>A file in the folder with the same bytes as the source, or null.</summary>
        internal static string SameContent(string folder, string source)
        {
            if (!Directory.Exists(folder)) return null;
            long length;
            try { length = new FileInfo(source).Length; }
            catch (IOException) { return null; }

            string hash = null;
            foreach (var f in Directory.GetFiles(folder))
            {
                FileInfo fi;
                try { fi = new FileInfo(f); }
                catch (IOException) { continue; }
                if (fi.Length != length || string.Equals(fi.FullName, Path.GetFullPath(source), StringComparison.OrdinalIgnoreCase)) continue;
                hash = hash ?? Hash(source);
                if (string.Equals(hash, Hash(f), StringComparison.Ordinal)) return f;
            }
            return null;
        }

        internal static string Hash(string path)
        {
            using (var sha = SHA256.Create())
            using (var s = File.OpenRead(path))
                return Convert.ToBase64String(sha.ComputeHash(s));
        }
    }

    /// <summary>Copies a planned upload into the job and records it in the upload log.</summary>
    public sealed class UploadRunner
    {
        public const string LogHeader = "Uploaded,User,Computer,Crew,Type,Source,Destination,Bytes,Result";

        private readonly JobFolderConfig _config;

        public UploadRunner(JobFolderConfig config)
        {
            _config = config;
        }

        /// <summary>
        /// Copies each item that is not already in the job. The crew's originals are never moved
        /// or deleted. Each copy goes to a .partial file first and is renamed only once its size
        /// matches, so a dropped network connection never leaves a half photo under a good name.
        /// One failure does not stop the rest; it is reported on the item.
        /// </summary>
        public int Run(ProjectFolder project, IEnumerable<UploadItem> items, string crew, Action<UploadItem> progress = null)
        {
            var copied = 0;
            var log = new StringBuilder();
            foreach (var item in items)
            {
                item.Error = null;
                if (item.Done) continue;
                if (item.Skip)
                {
                    log.AppendLine(LogRow(crew, item, "already in job as " + Path.GetFileName(item.AlreadyUploadedAs)));
                    item.Done = true;
                    progress?.Invoke(item);
                    continue;
                }
                var partial = item.Destination + ".partial";
                try
                {
                    if (string.IsNullOrEmpty(item.Destination)) throw new InvalidOperationException("No destination was planned.");
                    if (File.Exists(item.Destination)) throw new IOException(Path.GetFileName(item.Destination) + " appeared in the job while uploading. Upload again to renumber.");
                    Directory.CreateDirectory(Path.GetDirectoryName(item.Destination));
                    File.Copy(item.SourcePath, partial, true);
                    if (new FileInfo(partial).Length != new FileInfo(item.SourcePath).Length)
                        throw new IOException("The copy is not the same size as the original.");
                    File.Move(partial, item.Destination);
                    File.SetLastWriteTime(item.Destination, File.GetLastWriteTime(item.SourcePath));
                    item.Done = true;
                    copied++;
                    log.AppendLine(LogRow(crew, item, "uploaded"));
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is InvalidOperationException)
                {
                    item.Error = e.Message;
                    try { if (File.Exists(partial)) File.Delete(partial); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                    log.AppendLine(LogRow(crew, item, "FAILED: " + e.Message));
                }
                progress?.Invoke(item);
            }
            WriteLog(project, log.ToString());
            return copied;
        }

        private string LogRow(string crew, UploadItem item, string result)
        {
            return string.Join(",", new[]
            {
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                Environment.UserName,
                Environment.MachineName,
                (crew ?? string.Empty).Trim().ToUpperInvariant(),
                item.Category?.Name,
                item.SourcePath,
                item.Destination ?? item.AlreadyUploadedAs,
                SafeLength(item.SourcePath).ToString(CultureInfo.InvariantCulture),
                result,
            }.Select(Csv));
        }

        private static long SafeLength(string path)
        {
            try { return new FileInfo(path).Length; }
            catch (IOException) { return 0; }
        }

        internal static string Csv(string value)
        {
            var v = value ?? string.Empty;
            return v.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
        }

        internal void WriteLog(ProjectFolder project, string rows)
        {
            if (string.IsNullOrEmpty(rows) || string.IsNullOrWhiteSpace(_config.LogFile)) return;
            var path = Naming.Combine(project.Path, _config.LogFile, new Dictionary<string, string>());
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var header = File.Exists(path) ? string.Empty : LogHeader + Environment.NewLine;
                File.AppendAllText(path, header + rows);
            }
            catch (IOException)
            {
                // The log is open in Excel. The files are in; losing a log row is not worth failing the upload.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
