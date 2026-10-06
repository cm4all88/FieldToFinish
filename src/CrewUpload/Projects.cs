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

        /// <summary>Saved, but something around it (rolling the backups) did not work; already logged.</summary>
        public string Warning { get; set; }
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

        /// <summary>How long a save waits for another PM's lock before saying who holds it.</summary>
        internal TimeSpan LockWait { get; set; } = TimeSpan.FromSeconds(15);

        /// <summary>A held lock older than this is reported as probably left by a crashed PC (still never broken).</summary>
        internal TimeSpan StaleLockAfter { get; set; } = TimeSpan.FromMinutes(2);

        /// <summary>Pauses between retries of one file operation on the share (about 8 s in all).</summary>
        internal int[] RetryDelaysMs { get; set; } = { 100, 250, 500, 1000, 2000, 4000 };

        /// <summary>Test hook: runs before every attempt of every save step, and may throw to simulate a share error.</summary>
        internal Action<string, int> BeforeFileStep { get; set; }

        /// <summary>Where save failures are logged: beside the registry, and on this PC.</summary>
        public RegistryLog Log { get; set; }

        public ProjectRegistry(string path, bool requireUnc = true, int backups = 5, int retries = 5, int retryDelayMs = 300)
        {
            _path = path;
            _requireUnc = requireUnc;
            _backups = Math.Max(1, backups);
            _retries = Math.Max(1, retries);
            _retryDelayMs = Math.Max(0, retryDelayMs);
            Log = new RegistryLog(string.IsNullOrEmpty(path) ? null : Path.Combine(Folder, RegistryLog.FileName), RegistryLog.LocalPath);
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
            var step = "open registry folder";
            try
            {
                var retry = new Retrier(RetryDelaysMs, Log) { BeforeAttempt = BeforeFileStep };
                retry.Run(step, Folder, () => Directory.CreateDirectory(Folder));
                step = "acquire registry lock";
                using (RegistryLock.Acquire(LockPath, LockWait, StaleLockAfter, Log))
                {
                    step = "read live registry";
                    RegistrySnapshot live;
                    if (retry.Run("check live registry exists", _path, () => File.Exists(_path)))
                    {
                        var text = retry.Run(step, _path, () => ReadShared(_path));
                        try { live = Parse(text, _path); }
                        catch (InvalidDataException e)
                        {
                            throw new InvalidDataException(e.Message + " Nothing was saved. Restore " + BackupPath(1) + " (or an older backup) over it first.", e);
                        }
                    }
                    else live = FirstGoodBackup() ?? new RegistrySnapshot();

                    step = "check for other PMs' changes";
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
                    step = "write new registry";
                    var warning = WriteAtomically(next, retry, s => step = s);

                    // The swap is done. Re-read what is now live; if the share hiccups, the version just
                    // validated is what is there.
                    RegistrySnapshot saved;
                    try { saved = Parse(retry.Run("re-read saved registry", _path, () => ReadShared(_path)), _path); }
                    catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is InvalidDataException) { saved = next; }
                    return new RegistrySaveResult { Entry = updated, Snapshot = saved, MergedOtherChanges = merged, Warning = warning };
                }
            }
            catch (RegistryConflictException)
            {
                throw; // not a failure: the PM is told to refresh
            }
            catch (RegistryLockedException)
            {
                throw; // logged where it was raised
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is InvalidDataException || e is ArgumentException)
            {
                Log.Error("save registry: " + step, _path, e, "key " + key + "; nothing was saved unless a later entry says so");
                throw;
            }
        }

        /// <summary>
        /// Writes the complete registry to a temporary file in the same folder, flushes and checks it,
        /// copies the live file to a fresh backup, then replaces the live file in one step. The live
        /// file is never deleted or renamed away at any point: until the replace succeeds it is the old
        /// version, after it the new one. Old backups are rolled only after the replace, and a failure
        /// there is a warning, not a failed save. Returns that warning, or null.
        /// </summary>
        private string WriteAtomically(RegistrySnapshot next, Retrier retry, Action<string> stepIs)
        {
            var stem = Path.GetFileNameWithoutExtension(_path);
            var ext = Path.GetExtension(_path);
            var id = Guid.NewGuid().ToString("N");
            var temp = Path.Combine(Folder, stem + ".tmp-" + id + ext);
            var backupNew = Path.Combine(Folder, stem + ".backup-new-" + id + ext);
            try
            {
                stepIs("write temporary registry");
                var bytes = new UTF8Encoding(false).GetBytes(JsonConvert.SerializeObject(next, Formatting.Indented));
                retry.Run("write temporary registry", temp, () =>
                {
                    using (var s = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        s.Write(bytes, 0, bytes.Length);
                        s.Flush(true); // on disk, not just in a cache, before it can become the live file
                    }
                });
                AfterTempWritten?.Invoke(temp);

                // Read back exactly what is on disk and check it before it replaces anything.
                stepIs("read back temporary registry");
                var check = Parse(retry.Run("read back temporary registry", temp, () => ReadShared(temp)), temp);
                if (check.Revision != next.Revision || check.Projects.Count != next.Projects.Count)
                    throw new InvalidDataException("The new project list did not read back as written. Nothing was saved.");

                var liveExists = retry.Run("check live registry exists", _path, () => File.Exists(_path));
                if (liveExists)
                {
                    // A copy, so the live file stays exactly where it is.
                    stepIs("copy live registry to backup");
                    retry.Run("copy live registry to backup", backupNew, () => File.Copy(_path, backupNew, true));

                    // No backup argument: with one, Windows renames the live file away first and, if the
                    // next step fails, can leave no live file. Without one, a failed replace leaves the
                    // live file as it was.
                    stepIs("replace live registry");
                    retry.Run("replace live registry", _path, () =>
                    {
                        // A replace the server finished but whose reply was lost leaves no temp file and
                        // our revision live: that is success, not something to retry.
                        if (!File.Exists(temp) && IsRevision(_path, next.Revision)) return;
                        File.Replace(temp, _path, null, true);
                    });
                }
                else
                {
                    stepIs("create live registry");
                    retry.Run("create live registry", _path, () =>
                    {
                        if (!File.Exists(temp) && IsRevision(_path, next.Revision)) return;
                        File.Move(temp, _path);
                    });
                }

                if (!liveExists) return null;
                stepIs("rotate registry backups");
                try
                {
                    RotateBackups(retry);
                    retry.Run("rename new backup to backup-1", BackupPath(1), () => File.Move(backupNew, BackupPath(1)));
                    return null;
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    var warning = "Saved, but the backups could not be rolled (" + e.Message + "). The previous version is kept as "
                        + Path.GetFileName(backupNew) + ".";
                    Log.Warn("rotate registry backups", Folder, e, warning);
                    backupNew = null; // keep it: it is the only copy of the previous version
                    return warning;
                }
            }
            finally
            {
                // Only our own temporary files are ever deleted here -- never the live registry.
                TryDelete(temp, retry);
                if (backupNew != null) TryDelete(backupNew, retry);
            }
        }

        private static bool IsRevision(string path, int revision)
        {
            try { return File.Exists(path) && Parse(ReadShared(path), path).Revision == revision; }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is InvalidDataException) { return false; }
        }

        private void TryDelete(string path, Retrier retry)
        {
            try { retry.Run("delete temporary file", path, () => { if (File.Exists(path)) File.Delete(path); }); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
        }

        /// <summary>
        /// backup-(N-1) -> backup-N ... backup-1 -> backup-2, freeing backup-1 for the version just
        /// replaced. Only backups are touched.
        /// </summary>
        private void RotateBackups(Retrier retry)
        {
            var oldest = BackupPath(_backups);
            retry.Run("delete oldest backup", oldest, () => { if (File.Exists(oldest)) File.Delete(oldest); });
            for (var n = _backups - 1; n >= 1; n--)
            {
                var from = BackupPath(n);
                var to = BackupPath(n + 1);
                retry.Run("roll backup-" + n + " to backup-" + (n + 1), from, () => { if (File.Exists(from)) File.Move(from, to); });
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
