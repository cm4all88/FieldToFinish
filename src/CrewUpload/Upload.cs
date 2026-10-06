using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace CrewUpload
{
    /// <summary>What the crew chose for a file whose name is already taken by a different file.</summary>
    public enum ConflictChoice
    {
        /// <summary>Not decided: the file blocks the upload until it is.</summary>
        Undecided = 0,
        /// <summary>Upload it next to the other one, numbered -2, -3 ...</summary>
        KeepBoth = 1,
        /// <summary>Leave it out of this upload.</summary>
        Skip = 2,
    }

    /// <summary>Where a file stands.</summary>
    public enum UploadState
    {
        Ready,
        /// <summary>A file of the same name and byte size is already in the job.</summary>
        AlreadyUploaded,
        /// <summary>A file of the same name but a different size is in the job. Needs a decision.</summary>
        Conflict,
        Skipped,
        Done,
        Failed,
    }

    /// <summary>One file a crew dropped, and where it is going.</summary>
    public sealed class UploadItem
    {
        public string SourcePath { get; set; }

        /// <summary>As the crew dropped it: "20260128-JAM-1521-799-TOPO\Photos\IMG_0412.JPG".</summary>
        public string DroppedAs { get; set; }

        /// <summary>Folders between what was dropped and the file, outermost first. Their names are hints.</summary>
        public IList<string> FolderHints { get; set; } = new List<string>();

        /// <summary>
        /// The download folder it came in, when the crew dropped an already-named download. That
        /// folder is kept as it is in the job, and its name is never read for the file's type, so
        /// crew initials like "FN" never decide it.
        /// </summary>
        public string NamePrefix { get; set; }

        /// <summary>Its subfolder inside that download ("Photos"), kept in the job. Empty at the top.</summary>
        public string RelativeFolder { get; set; }

        public UploadCategory Category { get; set; }

        /// <summary>Full path it will be copied to. Set by <see cref="UploadPlanner.Assign"/>.</summary>
        public string Destination { get; set; }

        /// <summary>The file in the job with this file's name and byte size: it is not copied again.</summary>
        public string AlreadyUploadedAs { get; set; }

        /// <summary>The file in the job with this file's name but a different size.</summary>
        public string ConflictWith { get; set; }

        public ConflictChoice Choice { get; set; }

        /// <summary>Why it was not uploaded, after <see cref="UploadRunner.Run"/>.</summary>
        public string Error { get; set; }

        public bool Done { get; set; }

        /// <summary>Why the last upload of it failed. Kept through re-planning so the crew still sees it.</summary>
        public string LastError { get; set; }

        /// <summary>Copied and checked, or already there: what is now in the job.</summary>
        public string FinalPath => Destination ?? AlreadyUploadedAs;

        public UploadState State =>
            Error != null ? UploadState.Failed
            : Done ? UploadState.Done
            : Choice == ConflictChoice.Skip && ConflictWith != null ? UploadState.Skipped
            : ConflictWith != null && Destination == null && AlreadyUploadedAs == null ? UploadState.Conflict
            : AlreadyUploadedAs != null ? UploadState.AlreadyUploaded
            : UploadState.Ready;

        /// <summary>Nothing to copy: already in the job, or left out by the crew.</summary>
        public bool Skip => State == UploadState.AlreadyUploaded || State == UploadState.Skipped;
    }

    /// <summary>
    /// Turns what a crew dropped into a list of files with a type, a correct name and a place in
    /// the job's download folder. Nothing is copied here, so the crew sees and can correct the
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
        /// Files and folders as dropped. A folder brings in every file under it. Hidden files, Office
        /// lock files (~$...), Windows thumbnail caches and an earlier upload's manifest are left
        /// behind. With <paramref name="category"/> -- the crew dropped onto that type's box -- every
        /// file is that type and nothing is guessed.
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
                    // An already-named download: kept whole in the job, and its name is the files'
                    // prefix, not a hint.
                    var download = DownloadNames.Parse(_config, root) != null;
                    var before = items.Count;
                    AddFolder(items, seen, path, download ? new List<string>() : new List<string> { root }, root);
                    if (download)
                        for (var i = before; i < items.Count; i++)
                        {
                            items[i].NamePrefix = root;
                            items[i].RelativeFolder = string.Join("\\", items[i].FolderHints);
                            items[i].Category = Classify(Path.GetFileName(items[i].SourcePath), items[i].FolderHints, root);
                        }
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
            if (string.Equals(name, _config.ManifestFile, StringComparison.OrdinalIgnoreCase)) return;
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
        /// The category whose longest keyword appears in the name, so "as-built notes" is as-built
        /// notes, not field notes, whatever order the categories are listed in.
        /// </summary>
        private UploadCategory ByKeyword(string name) =>
            _config.Categories
                .SelectMany(c => c.Keywords.Where(k => Naming.HasWord(name, k)).Select(k => new { c, k.Length }))
                .OrderByDescending(x => x.Length)
                .Select(x => x.c)
                .FirstOrDefault();

        /// <summary>
        /// The category for a file: a keyword in its own name first, then in the folders it came
        /// in (nearest first), then its extension, then the fallback.
        /// </summary>
        public UploadCategory Classify(string fileName, IList<string> folderHints = null, string namePrefix = null)
        {
            var bare = Path.GetFileNameWithoutExtension(fileName);
            if (!string.IsNullOrEmpty(namePrefix) && bare.StartsWith(namePrefix, StringComparison.OrdinalIgnoreCase))
                bare = bare.Substring(namePrefix.Length);
            var byName = ByKeyword(bare);
            if (byName != null) return byName;

            if (folderHints != null)
                for (var i = folderHints.Count - 1; i >= 0; i--)
                {
                    var byFolder = ByKeyword(folderHints[i]);
                    if (byFolder != null) return byFolder;
                }

            var ext = Path.GetExtension(fileName);
            var byExt = _config.Categories.FirstOrDefault(c => c.Extensions.Any(e => string.Equals(e, ext, StringComparison.OrdinalIgnoreCase)));
            return byExt ?? _config.FallbackCategory;
        }

        /// <summary>The first dropped folder whose name is a download name, read back; null when none is.</summary>
        public ParsedDownload FindDownload(IEnumerable<string> dropped)
        {
            foreach (var raw in dropped ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(raw) || !Directory.Exists(raw)) continue;
                var parsed = DownloadNames.Parse(_config, Path.GetFileName(Path.GetFullPath(raw).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
                if (parsed != null) return parsed;
            }
            return null;
        }

        /// <summary>[Survey]\02Field\01FLD_DR_FN_DCfile\Unprocessed: where every download folder goes.</summary>
        public string DownloadsRoot(ProjectFolder project) => project.UploadRoot;

        /// <summary>The download folder for a visit: [downloads root]\20260128-JAM-1521-799-TOPO.</summary>
        public string DownloadFolder(ProjectFolder project, FieldVisit visit) =>
            Path.Combine(DownloadsRoot(project), DownloadNames.Name(_config, project.Info.ProjectNumber, visit));

        /// <summary>
        /// Gives every item its destination. A dropped download keeps its own folder name and subfolders
        /// under the project's Unprocessed folder; loose files go in the visit's download folder. Names follow the
        /// download (raw data) plus the type's suffix, photos become client-task-date-camera number.
        ///
        /// Nothing in the job is ever overwritten. A file of the same name and byte size already
        /// there counts as uploaded. The same name with a different size is a conflict, held until
        /// the crew decides (<see cref="UploadItem.Choice"/>); only then are files hashed, to find
        /// out whether the file is already in the job under another name. Two files in the same
        /// batch that would get the same name are numbered -2, -3. Call again after any change.
        /// </summary>
        public void Assign(ProjectFolder project, IList<UploadItem> items, FieldVisit visit)
        {
            var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var root = DownloadsRoot(project);
            var visitFolder = DownloadNames.Name(_config, project.Info.ProjectNumber, visit);
            foreach (var item in items)
            {
                if (item.Done) continue;
                item.Destination = null;
                item.AlreadyUploadedAs = null;
                item.ConflictWith = null;
                item.Error = null;
                item.Category = item.Category ?? Classify(Path.GetFileName(item.SourcePath), item.FolderHints, item.NamePrefix);

                var folderName = item.NamePrefix ?? visitFolder;
                var values = Values(project, item.Category, visit, Path.GetFileNameWithoutExtension(item.SourcePath));
                values["download"] = folderName;
                var folder = Path.Combine(root, folderName);
                folder = item.NamePrefix != null
                    ? Naming.Combine(folder, item.RelativeFolder, values)
                    : Naming.Combine(folder, item.Category.Folder, values);

                var ext = Path.GetExtension(item.SourcePath);
                var pattern = PatternFor(item.Category);
                var candidate = NameFor(folder, pattern, values, ext, reserved, false);

                if (!File.Exists(candidate) && !Directory.Exists(candidate))
                {
                    item.Destination = candidate;
                    continue;
                }

                if (File.Exists(candidate) && Length(candidate) == Length(item.SourcePath))
                {
                    item.AlreadyUploadedAs = candidate;
                    continue;
                }

                item.ConflictWith = candidate;
                if (item.Choice != ConflictChoice.KeepBoth) continue;

                // Resolving the conflict: is this exact file in the job already, under another name?
                var same = SameContent(folder, item.SourcePath);
                if (same != null)
                {
                    item.AlreadyUploadedAs = same;
                    continue;
                }
                item.Destination = NameFor(folder, pattern, values, ext, reserved, true);
            }
        }

        internal string PatternFor(UploadCategory category) =>
            string.IsNullOrWhiteSpace(category.FileName) ? _config.FileName : category.FileName;

        internal Dictionary<string, string> Values(ProjectFolder project, UploadCategory category, FieldVisit visit, string original)
        {
            var values = DownloadNames.Values(_config, project.Info.ProjectNumber, visit);
            values["download"] = DownloadNames.Name(_config, project.Info.ProjectNumber, visit);
            values["projectName"] = project.Info.ProjectName;
            values["client"] = project.Info.Client;
            values["code"] = category.Code;
            values["category"] = category.Name;
            values["original"] = original ?? string.Empty;
            values["number"] = CameraNumber(original);
            return values;
        }

        /// <summary>
        /// The camera's number in a photo name -- the last run of digits: IMG_0412 -> 0412,
        /// DSC01234 -> 01234. A name with no digits is kept whole.
        /// </summary>
        internal static string CameraNumber(string original)
        {
            var m = Regex.Match(original ?? string.Empty, @"(\d+)(?!.*\d)");
            return m.Success ? m.Groups[1].Value : (original ?? string.Empty);
        }

        /// <summary>
        /// The pattern's name, numbered -2, -3 ... past names taken earlier in this batch and, with
        /// <paramref name="checkDisk"/>, past names already in the folder. A pattern with {seq}
        /// numbers every file instead.
        /// </summary>
        internal string NameFor(string folder, string pattern, Dictionary<string, string> values, string extension, HashSet<string> reserved, bool checkDisk)
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
                if (reserved.Contains(candidate)) continue;
                if (checkDisk && (File.Exists(candidate) || Directory.Exists(candidate))) continue;
                reserved.Add(candidate);
                return candidate;
            }
            throw new IOException("No free file name left in " + folder);
        }

        /// <summary>Kept for documents, which are new files: the first name free on disk.</summary>
        internal string NextFreeName(string folder, string pattern, Dictionary<string, string> values, string extension, HashSet<string> reserved) =>
            NameFor(folder, pattern, values, extension, reserved, true);

        internal static long Length(string path)
        {
            try { return new FileInfo(path).Length; }
            catch (IOException) { return -1; }
            catch (UnauthorizedAccessException) { return -1; }
        }

        /// <summary>A file in the folder with the same bytes as the source, or null. Hashes same-size files only.</summary>
        internal static string SameContent(string folder, string source)
        {
            if (!Directory.Exists(folder)) return null;
            var length = Length(source);
            if (length < 0) return null;

            string hash = null;
            foreach (var f in Directory.GetFiles(folder))
            {
                if (Length(f) != length || string.Equals(Path.GetFullPath(f), Path.GetFullPath(source), StringComparison.OrdinalIgnoreCase)) continue;
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

    /// <summary>How an upload went. Complete only when every file was checked in the job.</summary>
    public sealed class UploadResult
    {
        public int Copied { get; set; }
        public int AlreadyUploaded { get; set; }
        public int Skipped { get; set; }

        /// <summary>Files that could not be copied, or are not in the job at their source's size afterwards.</summary>
        public List<UploadItem> Problems { get; } = new List<UploadItem>();

        /// <summary>Conflicts nobody decided on; they were not copied.</summary>
        public int Undecided { get; set; }

        public bool Complete => Problems.Count == 0 && Undecided == 0;
    }

    /// <summary>Copies a planned upload into the job, checks it, and records it in each download's manifest.</summary>
    public sealed class UploadRunner
    {
        public const string ManifestHeader = "Uploaded,User,Computer,Crew,Type,OriginalFile,OriginalPath,FinalFile,FinalPath,Bytes,Result";

        private readonly JobFolderConfig _config;

        public UploadRunner(JobFolderConfig config)
        {
            _config = config;
        }

        /// <summary>
        /// Copies each file that is ready; the crew's originals are never moved or deleted. Each copy
        /// goes to a .partial file first and is renamed only once its size matches, and the rename
        /// refuses to replace anything. Then every file is checked again -- the source still there,
        /// the job's copy there at the same byte size -- before the result can say complete.
        /// One failure does not stop the rest; it is reported on the item.
        /// </summary>
        public UploadResult Run(ProjectFolder project, IList<UploadItem> items, string crew, Action<UploadItem> progress = null)
        {
            var result = new UploadResult();
            var batch = items.Where(i => !i.Done).ToList();
            // The registered Survey folder has to be there already: the app makes the Unprocessed
            // folders inside it, never the project's own folders.
            var surveyMissing = !Directory.Exists(project.Path);
            foreach (var item in batch)
            {
                switch (item.State)
                {
                    case UploadState.Conflict:
                        result.Undecided++;
                        progress?.Invoke(item);
                        continue;
                    case UploadState.AlreadyUploaded:
                    case UploadState.Skipped:
                        progress?.Invoke(item);
                        continue;
                }

                item.Error = null;
                var partial = item.Destination + ".partial";
                try
                {
                    if (surveyMissing) throw new DirectoryNotFoundException("The registered Survey folder " + project.Path + " cannot be found. Ask the PM to check the project location.");
                    if (string.IsNullOrEmpty(item.Destination)) throw new InvalidOperationException("No destination was planned.");
                    if (!IsInside(item.Destination, project.UploadRoot)) throw new InvalidOperationException("Refused: " + item.Destination + " is outside the project's Unprocessed folder.");
                    if (File.Exists(item.Destination)) throw new IOException(Path.GetFileName(item.Destination) + " appeared in the job while uploading. Nothing was overwritten; check the list again.");
                    Directory.CreateDirectory(Path.GetDirectoryName(item.Destination));
                    File.Copy(item.SourcePath, partial, true);
                    if (UploadPlanner.Length(partial) != UploadPlanner.Length(item.SourcePath))
                        throw new IOException("The copy is not the same size as the original.");
                    File.Move(partial, item.Destination); // throws rather than replace an existing file
                    File.SetLastWriteTime(item.Destination, File.GetLastWriteTime(item.SourcePath));
                    result.Copied++;
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is InvalidOperationException)
                {
                    item.Error = e.Message;
                    try { if (File.Exists(partial)) File.Delete(partial); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
                progress?.Invoke(item);
            }

            Verify(batch, result);
            foreach (var item in batch) item.LastError = item.Error;
            WriteManifests(project, batch, crew);
            return result;
        }

        /// <summary>True when <paramref name="path"/> is somewhere below <paramref name="folder"/>.</summary>
        internal static bool IsInside(string path, string folder)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(folder)) return false;
            var p = Path.GetFullPath(path);
            var f = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return p.StartsWith(f, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Every file that should be in the job is there at its source's size, and the source is untouched.</summary>
        internal static void Verify(IEnumerable<UploadItem> items, UploadResult result)
        {
            foreach (var item in items)
            {
                var state = item.State;
                if (state == UploadState.Conflict) continue;
                if (state == UploadState.Skipped) { result.Skipped++; continue; }
                if (state == UploadState.Failed) { result.Problems.Add(item); continue; }

                var source = UploadPlanner.Length(item.SourcePath);
                var target = item.FinalPath == null ? -1 : UploadPlanner.Length(item.FinalPath);
                if (source < 0) item.Error = "The original is no longer where it was dropped from.";
                else if (target < 0) item.Error = "Not found in the job after copying.";
                else if (source != target) item.Error = "In the job at " + target + " bytes; the original is " + source + ".";

                if (item.Error != null) { result.Problems.Add(item); continue; }
                if (state == UploadState.AlreadyUploaded) { result.AlreadyUploaded++; item.Done = true; }
                else item.Done = true;
            }
        }

        private void WriteManifests(ProjectFolder project, IEnumerable<UploadItem> items, string crew)
        {
            if (string.IsNullOrWhiteSpace(_config.ManifestFile) || !Directory.Exists(project.Path)) return;
            // One manifest per download folder: the folder directly under the downloads root.
            foreach (var group in items.Where(i => i.FinalPath != null || i.ConflictWith != null).GroupBy(i => DownloadFolderOf(i.FinalPath ?? i.ConflictWith), StringComparer.OrdinalIgnoreCase))
            {
                if (group.Key == null || !IsInside(Path.Combine(group.Key, _config.ManifestFile), project.UploadRoot)) continue;
                var sb = new StringBuilder();
                foreach (var item in group) sb.AppendLine(Row(crew, item));
                var path = Path.Combine(group.Key, _config.ManifestFile);
                try
                {
                    Directory.CreateDirectory(group.Key);
                    var header = File.Exists(path) ? string.Empty : ManifestHeader + Environment.NewLine;
                    File.AppendAllText(path, header + sb);
                }
                catch (IOException)
                {
                    // The manifest is open in Excel. The files are in and checked; a missing row is
                    // not worth failing the upload over.
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        private string DownloadFolderOf(string path)
        {
            // Walk up to the folder whose parent is the Unprocessed folder (…\Unprocessed\<download>).
            var downloads = Naming.Segments(_config.UnprocessedFolder).LastOrDefault();
            var dir = Path.GetDirectoryName(path);
            while (dir != null)
            {
                var parent = Path.GetDirectoryName(dir);
                if (parent != null && string.Equals(Path.GetFileName(parent), downloads, StringComparison.OrdinalIgnoreCase)) return dir;
                dir = parent;
            }
            return Path.GetDirectoryName(path);
        }

        private static string Row(string crew, UploadItem item)
        {
            string result;
            switch (item.State)
            {
                case UploadState.Done: result = item.AlreadyUploadedAs != null ? "already uploaded (same name and size)" : "uploaded and verified"; break;
                case UploadState.Conflict: result = "CONFLICT: a different file of this name is in the job; not uploaded"; break;
                case UploadState.Skipped: result = "skipped by crew (name conflict)"; break;
                case UploadState.Failed: result = "FAILED: " + item.Error; break;
                default: result = item.State.ToString(); break;
            }
            var final = item.FinalPath ?? item.ConflictWith;
            return string.Join(",", new[]
            {
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                Environment.UserName,
                Environment.MachineName,
                (crew ?? string.Empty).Trim().ToUpperInvariant(),
                item.Category?.Name,
                Path.GetFileName(item.SourcePath),
                item.SourcePath,
                item.State == UploadState.Done ? Path.GetFileName(final) : string.Empty,
                item.State == UploadState.Done ? final : string.Empty,
                UploadPlanner.Length(item.SourcePath).ToString(CultureInfo.InvariantCulture),
                result,
            }.Select(Csv));
        }

        internal static string Csv(string value)
        {
            var v = value ?? string.Empty;
            return v.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
        }
    }
}
