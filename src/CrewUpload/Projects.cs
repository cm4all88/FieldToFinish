using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Newtonsoft.Json;

namespace CrewUpload
{
    /// <summary>What is known about a project: its number, and the names around its Survey folder.</summary>
    public sealed class ProjectInfo
    {
        /// <summary>Client-task as crews write it: 1800-119. The number in every upload's name.</summary>
        public string ProjectNumber { get; set; }

        /// <summary>The project folder's name above the Survey folder: "554-1800-119 TDLE Phase 3".</summary>
        public string ProjectName { get; set; }

        /// <summary>The client folder above that: "1800-HDR".</summary>
        public string Client { get; set; }
    }

    /// <summary>A registered project, ready for uploads.</summary>
    public sealed class ProjectFolder
    {
        /// <summary>The project's base Survey folder, as the PM registered it (UNC).</summary>
        public string Path { get; set; }

        /// <summary>[Survey]\02Field\01FLD_DR_FN_DCfile\Unprocessed: the only place uploads are written.</summary>
        public string UploadRoot { get; set; }

        public ProjectInfo Info { get; set; }

        public string Display => string.IsNullOrWhiteSpace(Info.ProjectName) ? Info.ProjectNumber : Info.ProjectName;
    }

    /// <summary>One change to a registration, kept forever: registered, moved, made inactive or active again.</summary>
    public sealed class RegistrationChange
    {
        public const string Registered = "registered", Moved = "moved", Deactivated = "inactive", Reactivated = "active";

        [JsonProperty("change")] public string Change { get; set; }
        [JsonProperty("surveyFolder")] public string SurveyFolder { get; set; }
        [JsonProperty("previousFolder")] public string PreviousFolder { get; set; }
        [JsonProperty("by")] public string By { get; set; }
        [JsonProperty("on")] public DateTime On { get; set; }
    }

    /// <summary>One project's registration: client and task, and the base Survey folder the PM picked.</summary>
    public sealed class ProjectRegistration
    {
        /// <summary>####-###: 1800-119. The registry's key; zeros are kept (1800-011 is not 1800-119).</summary>
        [JsonProperty("key")] public string Key { get; set; }
        [JsonProperty("client")] public string Client { get; set; }
        [JsonProperty("task")] public string Task { get; set; }

        /// <summary>
        /// The base Survey folder, always UNC:
        /// \\parametrix.com\pmx\PSO\Projects\Clients\1800-HDR\554-1800-119 TDLE Phase 3\99Svcs\Survey
        /// </summary>
        [JsonProperty("surveyFolder")] public string SurveyFolder { get; set; }

        /// <summary>Open for crew uploads. A finished project is made inactive, never deleted.</summary>
        [JsonProperty("active")] public bool Active { get; set; } = true;

        [JsonProperty("registeredBy")] public string RegisteredBy { get; set; }
        [JsonProperty("registeredOn")] public DateTime RegisteredOn { get; set; }
        [JsonProperty("history")] public List<RegistrationChange> History { get; set; } = new List<RegistrationChange>();

        [JsonIgnore] public string ProjectNumber => Key;
    }

    /// <summary>The registry as read at one moment. Its revision is what a later save is checked against.</summary>
    public sealed class RegistrySnapshot
    {
        [JsonProperty("version")] public string Version { get; set; } = "2";

        /// <summary>Goes up by one on every save. A save based on an older revision is checked for clashes.</summary>
        [JsonProperty("revision")] public int Revision { get; set; }
        [JsonProperty("savedBy")] public string SavedBy { get; set; }
        [JsonProperty("savedOn")] public DateTime? SavedOn { get; set; }
        [JsonProperty("projects")] public List<ProjectRegistration> Projects { get; set; } = new List<ProjectRegistration>();

        /// <summary>Set when the live file could not be read and a backup was used instead.</summary>
        [JsonIgnore] public string ReadFromBackup { get; set; }

        public ProjectRegistration Get(string key) =>
            Projects.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Thrown when another PM changed the same project after this one opened the setup screen.</summary>
    public sealed class RegistryConflictException : Exception
    {
        public RegistryConflictException(string message) : base(message) { }
    }

    /// <summary>What a save did.</summary>
    public sealed class RegistrySaveResult
    {
        public ProjectRegistration Entry { get; set; }

        /// <summary>The registry as it now is; base the next change on this.</summary>
        public RegistrySnapshot Snapshot { get; set; }

        /// <summary>Other PMs had saved changes to other projects meanwhile; they were kept.</summary>
        public bool MergedOtherChanges { get; set; }

        /// <summary>Nothing needed changing.</summary>
        public bool Unchanged { get; set; }
    }

    /// <summary>
    /// The list of projects open for crew uploads: one JSON file in a shared configuration folder.
    ///
    /// Crews only read it. The live file is only ever replaced whole, never written in place, so a
    /// reader sees the old version or the new one and nothing in between; a read that hits the file
    /// mid-swap or locked is retried, and if the live file cannot be read at all the newest good
    /// backup is used.
    ///
    /// A PM save: takes the writers' lock (project-registry.lock), re-reads the live file, checks it
    /// against the revision the PM's screen was based on, writes the complete new registry to a
    /// temporary file in the same folder, flushes it to disk, reads it back and checks it, then
    /// atomically replaces the live file, keeping the previous one as backup-1 and rolling older
    /// backups up to backup-N. A write that fails its check is discarded and the live file is untouched.
    ///
    /// Who may save is decided by the folder's NTFS permissions (PMs/admins Modify, crews Read).
    /// </summary>
    public sealed class ProjectRegistry
    {
        private readonly string _path;
        private readonly bool _requireUnc;
        private readonly int _backups;
        private readonly int _retries;
        private readonly int _retryDelayMs;

        /// <summary>Test hook: runs on the temporary file after it is written and before it is checked.</summary>
        internal Action<string> AfterTempWritten { get; set; }

        public ProjectRegistry(string path, bool requireUnc = true, int backups = 5, int retries = 5, int retryDelayMs = 300)
        {
            _path = path;
            _requireUnc = requireUnc;
            _backups = Math.Max(1, backups);
            _retries = Math.Max(1, retries);
            _retryDelayMs = Math.Max(0, retryDelayMs);
        }

        public static ProjectRegistry For(JobFolderConfig config) =>
            new ProjectRegistry(config.RegistryPath, config.RequireUncPaths, config.RegistryBackups);

        public string FilePath => _path;
        private string Folder => Path.GetDirectoryName(Path.GetFullPath(_path));
        private string LockPath => Path.Combine(Folder, Path.GetFileNameWithoutExtension(_path) + ".lock");

        public string BackupPath(int n) => Path.Combine(Folder, Path.GetFileNameWithoutExtension(_path) + ".backup-" + n + Path.GetExtension(_path));

        public static bool IsUnc(string path) =>
            !string.IsNullOrEmpty(path) && (path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal));

        public static string KeyFor(string projectNumber)
        {
            var key = JobFolderConfig.NormalizeProjectNumber(projectNumber);
            return Regex.IsMatch(key, "^[0-9]{4}-[0-9]{3}$") ? key : null;
        }

        // ------------------------------------------------------------------ reading

        /// <summary>
        /// The registry now. Retries a briefly unavailable file a few times; if the live file is
        /// unreadable or damaged, the newest good backup (marked <see cref="RegistrySnapshot.ReadFromBackup"/>).
        /// An empty registry when none has been saved yet.
        /// </summary>
        public RegistrySnapshot Load()
        {
            Exception last = null;
            for (var attempt = 1; attempt <= _retries; attempt++)
            {
                try
                {
                    if (!File.Exists(_path))
                    {
                        if (!Directory.Exists(Folder)) throw new DirectoryNotFoundException("The project list folder " + Folder + " cannot be reached.");
                        return FirstGoodBackup() ?? new RegistrySnapshot();
                    }
                    return Parse(ReadShared(_path), _path);
                }
                catch (InvalidDataException e)
                {
                    // Damaged live file (hand-edited, say): use the last good copy rather than nothing.
                    var backup = FirstGoodBackup();
                    if (backup != null) return backup;
                    throw new InvalidDataException(e.Message + " No good backup was found either.", e);
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    last = e;
                    if (attempt < _retries) Thread.Sleep(_retryDelayMs);
                }
            }
            throw new IOException("The project list " + _path + " could not be read after " + _retries + " tries: " + last?.Message, last);
        }

        /// <summary>The registration for <paramref name="projectNumber"/>, active or not; null when there is none.</summary>
        public ProjectRegistration Find(string projectNumber)
        {
            var key = KeyFor(projectNumber);
            return key == null ? null : Load().Get(key);
        }

        public IReadOnlyList<ProjectRegistration> All() => Load().Projects.OrderBy(p => p.Key, StringComparer.Ordinal).ToList();

        private static string ReadShared(string path)
        {
            using (var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var r = new StreamReader(s, Encoding.UTF8))
                return r.ReadToEnd();
        }

        private static RegistrySnapshot Parse(string text, string where)
        {
            RegistrySnapshot doc;
            try { doc = JsonConvert.DeserializeObject<RegistrySnapshot>(text); }
            catch (JsonException e) { throw new InvalidDataException(where + " is not a readable project list: " + e.Message, e); }
            if (doc == null || doc.Projects == null) throw new InvalidDataException(where + " is empty or not a project list.");
            // Version 1 files had no key: derive it, padded to ####-###.
            foreach (var p in doc.Projects.Where(p => p != null && string.IsNullOrEmpty(p.Key)))
                p.Key = KeyFor((p.Client ?? string.Empty) + "-" + (p.Task ?? string.Empty));
            foreach (var p in doc.Projects.Where(p => p?.Key != null && KeyFor(p.Key) == p.Key))
            {
                var parts = p.Key.Split('-');
                p.Client = parts[0];
                p.Task = parts[1];
            }
            var problems = Problems(doc);
            if (problems.Count > 0) throw new InvalidDataException(where + ": " + string.Join(" ", problems));
            return doc;
        }

        private static List<string> Problems(RegistrySnapshot doc)
        {
            var problems = new List<string>();
            foreach (var p in doc.Projects)
            {
                if (p == null || KeyFor(p.Key) != p.Key) problems.Add("Entry '" + p?.Key + "' does not have a ####-### key.");
                else if (string.IsNullOrWhiteSpace(p.SurveyFolder)) problems.Add(p.Key + " has no Survey folder.");
            }
            foreach (var g in doc.Projects.Where(p => p != null).GroupBy(p => p.Key, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
                problems.Add(g.Key + " is listed more than once.");
            return problems;
        }

        private RegistrySnapshot FirstGoodBackup()
        {
            for (var n = 1; n <= _backups; n++)
            {
                var b = BackupPath(n);
                try
                {
                    if (!File.Exists(b)) continue;
                    var doc = Parse(ReadShared(b), b);
                    doc.ReadFromBackup = b;
                    return doc;
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is InvalidDataException)
                {
                }
            }
            return null;
        }

        // ------------------------------------------------------------------ writing

        /// <summary>
        /// Registers <paramref name="projectNumber"/> to a base Survey folder, or moves it there. The
        /// folder must exist and be UNC (callers turn mapped-drive picks into UNC first). Nothing is
        /// created on the share. <paramref name="basis"/> is the snapshot the PM's screen showed.
        /// </summary>
        public RegistrySaveResult Register(string projectNumber, string surveyFolder, string user, RegistrySnapshot basis = null)
        {
            var key = KeyFor(projectNumber) ?? throw new ArgumentException("'" + projectNumber + "' is not a client-task number like 1800-119.");
            var folder = (surveyFolder ?? string.Empty).Trim().TrimEnd('\\', '/');
            if (_requireUnc && !IsUnc(folder))
                throw new ArgumentException(folder + " is not a network (UNC) path. Pick the folder through \\\\parametrix.com\\... rather than a drive letter.");
            if (!Directory.Exists(folder))
                throw new DirectoryNotFoundException("The folder " + folder + " does not exist. Register the project after its folders have been created.");

            return Save(key, user, basis, (entry, now) =>
            {
                if (entry == null)
                {
                    var parts = key.Split('-');
                    entry = new ProjectRegistration { Key = key, Client = parts[0], Task = parts[1], SurveyFolder = folder, RegisteredBy = user, RegisteredOn = now };
                    entry.History.Add(new RegistrationChange { Change = RegistrationChange.Registered, SurveyFolder = folder, By = user, On = now });
                    return entry;
                }
                if (Same(entry.SurveyFolder, folder)) return null;
                entry.History.Add(new RegistrationChange { Change = RegistrationChange.Moved, SurveyFolder = folder, PreviousFolder = entry.SurveyFolder, By = user, On = now });
                entry.SurveyFolder = folder;
                entry.RegisteredBy = user;
                entry.RegisteredOn = now;
                return entry;
            });
        }

        /// <summary>Makes a project inactive (gone from crew selection, kept with its history) or active again.</summary>
        public RegistrySaveResult SetActive(string projectNumber, bool active, string user, RegistrySnapshot basis = null)
        {
            var key = KeyFor(projectNumber) ?? throw new ArgumentException("'" + projectNumber + "' is not a client-task number like 1800-119.");
            return Save(key, user, basis, (entry, now) =>
            {
                if (entry == null) throw new InvalidOperationException(key + " is not registered.");
                if (entry.Active == active) return null;
                entry.Active = active;
                entry.History.Add(new RegistrationChange { Change = active ? RegistrationChange.Reactivated : RegistrationChange.Deactivated, SurveyFolder = entry.SurveyFolder, By = user, On = now });
                return entry;
            });
        }

        /// <summary>
        /// The one way the live file changes. <paramref name="change"/> gets a copy of the live entry
        /// (or null) and returns the new entry, or null for "no change".
        /// </summary>
        private RegistrySaveResult Save(string key, string user, RegistrySnapshot basis, Func<ProjectRegistration, DateTime, ProjectRegistration> change)
        {
            Directory.CreateDirectory(Folder);
            using (TakeLock())
            {
                RegistrySnapshot live;
                if (File.Exists(_path))
                {
                    try { live = Parse(ReadShared(_path), _path); }
                    catch (InvalidDataException e)
                    {
                        throw new InvalidDataException(e.Message + " Nothing was saved. Restore " + BackupPath(1) + " (or an older backup) over it first.", e);
                    }
                }
                else live = FirstGoodBackup() ?? new RegistrySnapshot();

                var merged = false;
                if (basis != null && basis.Revision != live.Revision)
                {
                    // Someone saved since this PM's screen was loaded. Changes to other projects are
                    // kept; a change to this same project is theirs to look at first.
                    if (Json(basis.Get(key)) != Json(live.Get(key)))
                        throw new RegistryConflictException(key + " was changed by " + (live.SavedBy ?? "someone else")
                            + (live.SavedOn.HasValue ? " at " + live.SavedOn.Value.ToString("HH:mm") : string.Empty)
                            + " after you opened this screen. Refresh to see their change, then make yours again.");
                    merged = true;
                }

                var current = live.Get(key);
                var copy = current == null ? null : JsonConvert.DeserializeObject<ProjectRegistration>(Json(current));
                var now = DateTime.Now;
                var updated = change(copy, now);
                if (updated == null)
                    return new RegistrySaveResult { Entry = current, Snapshot = live, Unchanged = true, MergedOtherChanges = merged };

                var next = new RegistrySnapshot
                {
                    Revision = live.Revision + 1,
                    SavedBy = user,
                    SavedOn = now,
                    Projects = live.Projects.Where(p => !string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase)).Concat(new[] { updated })
                        .OrderBy(p => p.Key, StringComparer.Ordinal).ToList(),
                };
                WriteAtomically(next);
                return new RegistrySaveResult { Entry = updated, Snapshot = Parse(ReadShared(_path), _path), MergedOtherChanges = merged };
            }
        }

        private void WriteAtomically(RegistrySnapshot next)
        {
            var temp = Path.Combine(Folder, Path.GetFileNameWithoutExtension(_path) + ".tmp-" + Guid.NewGuid().ToString("N") + Path.GetExtension(_path));
            try
            {
                var bytes = new UTF8Encoding(false).GetBytes(JsonConvert.SerializeObject(next, Formatting.Indented));
                using (var s = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    s.Write(bytes, 0, bytes.Length);
                    s.Flush(true); // on disk, not just in a cache, before it can become the live file
                }
                AfterTempWritten?.Invoke(temp);

                // Read back exactly what is on disk and check it before it replaces anything.
                var check = Parse(ReadShared(temp), temp);
                if (check.Revision != next.Revision || check.Projects.Count != next.Projects.Count)
                    throw new InvalidDataException("The new project list did not read back as written. Nothing was saved.");

                RotateBackups();
                if (File.Exists(_path)) File.Replace(temp, _path, BackupPath(1), true);
                else File.Move(temp, _path);
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        /// <summary>backup-(N-1) -> backup-N ... backup-1 -> backup-2; File.Replace then makes the live file backup-1.</summary>
        private void RotateBackups()
        {
            var oldest = BackupPath(_backups);
            if (File.Exists(oldest)) File.Delete(oldest);
            for (var n = _backups - 1; n >= 1; n--)
            {
                var from = BackupPath(n);
                if (File.Exists(from)) File.Move(from, BackupPath(n + 1));
            }
        }

        /// <summary>
        /// The writers' lock: a file held open exclusively and deleted on close, so it cannot be left
        /// behind by a crashed PC. Readers never take it.
        /// </summary>
        private IDisposable TakeLock()
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    return new FileStream(LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
                }
                catch (IOException) when (attempt < 20)
                {
                    Thread.Sleep(250);
                }
                catch (IOException e)
                {
                    throw new IOException("Another PM is saving the project list. Try again in a moment.", e);
                }
            }
        }

        private static string Json(ProjectRegistration p) => p == null ? null : JsonConvert.SerializeObject(p);

        private static bool Same(string a, string b) => string.Equals((a ?? string.Empty).TrimEnd('\\', '/'), (b ?? string.Empty).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Turns a registered client-task into the place its uploads go. The company folder structure is
    /// never searched or guessed: a project is open for uploads only once a PM has registered it.
    /// </summary>
    public sealed class ProjectStore
    {
        private readonly JobFolderConfig _config;
        private readonly ProjectRegistry _registry;

        public ProjectStore(JobFolderConfig config, ProjectRegistry registry)
        {
            _config = config;
            _registry = registry;
        }

        public ProjectRegistry Registry => _registry;

        /// <summary>
        /// The project for crew uploads: registered and active. Null when it is not registered or is
        /// inactive. <paramref name="problem"/> says which, in words for the crew.
        /// </summary>
        public ProjectFolder Find(string projectNumber, out string problem)
        {
            var key = ProjectRegistry.KeyFor(projectNumber);
            if (key == null) { problem = "'" + projectNumber + "' is not a client-task number like 1800-119."; return null; }
            var reg = _registry.Load().Get(key);
            if (reg == null) { problem = key + " has not been set up for crew uploads yet. The PM needs to set the project location."; return null; }
            if (!reg.Active) { problem = key + " is closed for crew uploads (inactive). Ask the PM if it should be reopened."; return null; }
            problem = null;
            return FromRegistration(reg);
        }

        public ProjectFolder Find(string projectNumber)
        {
            string problem;
            return Find(projectNumber, out problem);
        }

        /// <summary>The active projects, for the crew's list.</summary>
        public IReadOnlyList<string> ActiveProjects() => _registry.All().Where(r => r.Active).Select(r => r.Key).ToList();

        public ProjectFolder FromRegistration(ProjectRegistration reg)
        {
            var number = reg.Key;
            var survey = reg.SurveyFolder;
            // Name the project after the folder above the Survey folder that carries its number.
            string project = null, client = null;
            var dir = survey;
            while (!string.IsNullOrEmpty(dir))
            {
                var name = Path.GetFileName(dir.TrimEnd('\\', '/'));
                if (LooksLike(name, number))
                {
                    project = name;
                    client = Path.GetFileName(Path.GetDirectoryName(dir.TrimEnd('\\', '/')) ?? string.Empty);
                    break;
                }
                dir = Path.GetDirectoryName(dir.TrimEnd('\\', '/'));
            }
            return new ProjectFolder
            {
                Path = survey,
                UploadRoot = Naming.Combine(survey, _config.UnprocessedFolder, new Dictionary<string, string>()),
                Info = new ProjectInfo { ProjectNumber = number, ProjectName = project ?? number, Client = client },
            };
        }

        /// <summary>True when the folder name looks like this project: "1800-119 ..." or "554-1800-119 ...".</summary>
        public static bool LooksLike(string folderName, string projectNumber)
        {
            var number = JobFolderConfig.NormalizeProjectNumber(projectNumber);
            return Regex.IsMatch(folderName ?? string.Empty, @"^(?:[0-9A-Za-z]+-)?" + Regex.Escape(number) + @"($|[\s_-])", RegexOptions.IgnoreCase);
        }
    }
}
