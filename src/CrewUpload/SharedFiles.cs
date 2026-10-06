using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace CrewUpload
{
    /// <summary>A small shared settings document: revision-numbered so a stale editor cannot overwrite a newer save.</summary>
    public interface ISharedDocument
    {
        int Revision { get; set; }
        string SavedBy { get; set; }
        DateTime? SavedOn { get; set; }

        /// <summary>What is wrong with it, or null. A document with problems is never written.</summary>
        string Problems();
    }

    /// <summary>
    /// A shared JSON settings file beside the project registry (crew-settings.json), written the same
    /// way the registry is: a writers' lock, a temp file flushed and read back before it replaces the
    /// live file, a copy of the old one kept as a backup, retries through share hiccups, and every
    /// failure logged. Readers retry and fall back to the newest good backup. The registry keeps its
    /// own tested code; this is the same sequence for documents that change rarely and as a whole.
    /// </summary>
    public sealed class SharedJsonFile<T> where T : class, ISharedDocument, new()
    {
        private readonly string _path;
        private readonly int _backups;

        internal int[] RetryDelaysMs { get; set; } = { 100, 250, 500, 1000, 2000, 4000 };
        internal TimeSpan LockWait { get; set; } = TimeSpan.FromSeconds(10);
        internal TimeSpan StaleLockAfter { get; set; } = TimeSpan.FromMinutes(2);
        public RegistryLog Log { get; set; }

        public SharedJsonFile(string path, int backups = 3)
        {
            _path = path;
            _backups = Math.Max(1, backups);
            Log = new RegistryLog(string.IsNullOrEmpty(path) ? null : Path.Combine(Folder, RegistryLog.FileName), RegistryLog.LocalPath);
        }

        public string FilePath => _path;
        private string Folder => Path.GetDirectoryName(Path.GetFullPath(_path));
        private string Stem => Path.GetFileNameWithoutExtension(_path);
        private string LockPath => Path.Combine(Folder, Stem + ".lock");
        public string BackupPath(int n) => Path.Combine(Folder, Stem + ".backup-" + n + Path.GetExtension(_path));

        /// <summary>The document now; a new, empty one when there is none yet. Throws only when neither it nor a backup can be read.</summary>
        public T Load()
        {
            var retry = new Retrier(RetryDelaysMs, Log);
            Exception failure = null;
            try
            {
                if (!retry.Run("check " + Stem + " exists", _path, () => File.Exists(_path)))
                    return FirstGoodBackup() ?? new T();
                return Parse(retry.Run("read " + Stem, _path, () => ReadShared(_path)), _path);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is InvalidDataException)
            {
                failure = e;
            }
            var backup = FirstGoodBackup();
            if (backup != null) return backup;
            throw failure is InvalidDataException ? failure : new IOException("Cannot read " + _path + ": " + failure.Message, failure);
        }

        /// <summary>
        /// Saves <paramref name="next"/>. <paramref name="basisRevision"/> is the revision the editor
        /// showed; if someone saved since, nothing is written and <see cref="RegistryConflictException"/>
        /// says who.
        /// </summary>
        public T Save(T next, string user, int basisRevision)
        {
            var problems = next.Problems();
            if (problems != null) throw new InvalidDataException(problems);
            var retry = new Retrier(RetryDelaysMs, Log);
            retry.Run("open settings folder", Folder, () => Directory.CreateDirectory(Folder));
            using (RegistryLock.Acquire(LockPath, LockWait, StaleLockAfter, Log))
            {
                T live = null;
                if (retry.Run("check " + Stem + " exists", _path, () => File.Exists(_path)))
                    live = Parse(retry.Run("read " + Stem, _path, () => ReadShared(_path)), _path);
                var liveRevision = live?.Revision ?? 0;
                if (liveRevision != basisRevision)
                    throw new RegistryConflictException(Path.GetFileName(_path) + " was changed by " + (live?.SavedBy ?? "someone else")
                        + (live?.SavedOn != null ? " at " + live.SavedOn.Value.ToString("HH:mm") : string.Empty)
                        + " after you opened it. Refresh to see their change, then make yours again.");

                next.Revision = liveRevision + 1;
                next.SavedBy = user;
                next.SavedOn = DateTime.Now;
                Write(next, live != null, retry);
                return next;
            }
        }

        private void Write(T next, bool liveExists, Retrier retry)
        {
            var id = Guid.NewGuid().ToString("N");
            var ext = Path.GetExtension(_path);
            var temp = Path.Combine(Folder, Stem + ".tmp-" + id + ext);
            var backupNew = Path.Combine(Folder, Stem + ".backup-new-" + id + ext);
            try
            {
                var bytes = new UTF8Encoding(false).GetBytes(JsonConvert.SerializeObject(next, Formatting.Indented));
                retry.Run("write temporary " + Stem, temp, () =>
                {
                    using (var s = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        s.Write(bytes, 0, bytes.Length);
                        s.Flush(true);
                    }
                });
                var check = Parse(retry.Run("read back temporary " + Stem, temp, () => ReadShared(temp)), temp);
                if (check.Revision != next.Revision) throw new InvalidDataException("The new " + Stem + " did not read back as written. Nothing was saved.");

                if (liveExists)
                {
                    retry.Run("copy " + Stem + " to backup", backupNew, () => File.Copy(_path, backupNew, true));
                    retry.Run("replace " + Stem, _path, () =>
                    {
                        if (!File.Exists(temp) && IsRevision(_path, next.Revision)) return;
                        File.Replace(temp, _path, null, true); // no backup argument: a failed replace leaves the live file as it was
                    });
                    try
                    {
                        var oldest = BackupPath(_backups);
                        retry.Run("delete oldest backup", oldest, () => { if (File.Exists(oldest)) File.Delete(oldest); });
                        for (var n = _backups - 1; n >= 1; n--)
                        {
                            var from = BackupPath(n);
                            var to = BackupPath(n + 1);
                            retry.Run("roll backup", from, () => { if (File.Exists(from)) File.Move(from, to); });
                        }
                        retry.Run("rename new backup", BackupPath(1), () => File.Move(backupNew, BackupPath(1)));
                    }
                    catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                    {
                        Log.Warn("rotate " + Stem + " backups", Folder, e, "saved; previous version kept as " + Path.GetFileName(backupNew));
                        backupNew = null;
                    }
                }
                else
                {
                    retry.Run("create " + Stem, _path, () =>
                    {
                        if (!File.Exists(temp) && IsRevision(_path, next.Revision)) return;
                        File.Move(temp, _path);
                    });
                }
            }
            finally
            {
                TryDelete(temp, retry);
                if (backupNew != null) TryDelete(backupNew, retry);
            }
        }

        private T FirstGoodBackup()
        {
            for (var n = 1; n <= _backups; n++)
            {
                var b = BackupPath(n);
                try { if (File.Exists(b)) return Parse(ReadShared(b), b); }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is InvalidDataException) { }
            }
            return null;
        }

        private static T Parse(string text, string where)
        {
            T doc;
            try { doc = JsonConvert.DeserializeObject<T>(text); }
            catch (JsonException e) { throw new InvalidDataException(where + " is not readable: " + e.Message, e); }
            if (doc == null) throw new InvalidDataException(where + " is empty.");
            var problems = doc.Problems();
            if (problems != null) throw new InvalidDataException(where + ": " + problems);
            return doc;
        }

        private static string ReadShared(string path)
        {
            using (var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var r = new StreamReader(s, Encoding.UTF8))
                return r.ReadToEnd();
        }

        private static bool IsRevision(string path, int revision)
        {
            try { return File.Exists(path) && Parse(ReadShared(path), path).Revision == revision; }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is InvalidDataException) { return false; }
        }

        private static void TryDelete(string path, Retrier retry)
        {
            try { retry.Run("delete temporary file", path, () => { if (File.Exists(path)) File.Delete(path); }); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
        }
    }
}
