using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using Newtonsoft.Json;

namespace CrewUpload
{
    /// <summary>
    /// The project registry is held by another PM's save. Never broken by this app: the message says
    /// who holds it and since when, so a lock that outlives a crashed PC can be released by IT.
    /// </summary>
    public sealed class RegistryLockedException : IOException
    {
        public RegistryLockedException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>Who holds the registry lock. Diagnostic only: the open lock file is the lock.</summary>
    internal sealed class LockOwner
    {
        [JsonProperty("user")] public string User { get; set; }
        [JsonProperty("machine")] public string Machine { get; set; }
        [JsonProperty("processId")] public int ProcessId { get; set; }
        [JsonProperty("processStarted")] public DateTime? ProcessStarted { get; set; }
        [JsonProperty("acquiredUtc")] public DateTime AcquiredUtc { get; set; }
        [JsonProperty("releasedUtc")] public DateTime? ReleasedUtc { get; set; }

        public static LockOwner Me()
        {
            var me = new LockOwner { User = RegistryLog.UserName, Machine = Environment.MachineName, AcquiredUtc = DateTime.UtcNow };
            try
            {
                using (var p = Process.GetCurrentProcess())
                {
                    me.ProcessId = p.Id;
                    me.ProcessStarted = p.StartTime.ToUniversalTime();
                }
            }
            catch (Exception e) when (e is InvalidOperationException || e is NotSupportedException || e is System.ComponentModel.Win32Exception)
            {
            }
            return me;
        }

        public override string ToString() => User + " on " + Machine + (ProcessId > 0 ? " (process " + ProcessId + ")" : string.Empty);
    }

    /// <summary>
    /// The writers' lock on the registry folder.
    ///
    /// The lock is <c>project-registry.lock</c> held open with no sharing: while one PM's save has it
    /// open, nobody else can open it, delete it or replace it -- the file server enforces that, so an
    /// active lock can never be removed by another PM. It is opened delete-on-close, and emptied before
    /// it is closed.
    ///
    /// Stale locks: a lock file that is still there but that nobody holds open (a crash where the
    /// delete-on-close did not happen) simply opens -- the server has already released it -- and is
    /// taken over; that is logged with the previous owner. A lock file that is still <em>held</em> --
    /// including by a PC that crashed or dropped off the network, whose handle the file server keeps
    /// until it times out the connection -- is never broken: after waiting, the PM is told who holds
    /// it and since when, and past the stale timeout how IT can close it on the server.
    ///
    /// Who holds it is written to <c>project-registry.lock.owner.json</c> beside it, since the lock
    /// itself cannot be read while held.
    /// </summary>
    internal sealed class RegistryLock : IDisposable
    {
        private readonly FileStream _handle;
        private readonly string _ownerPath;

        private RegistryLock(FileStream handle, string ownerPath)
        {
            _handle = handle;
            _ownerPath = ownerPath;
        }

        public static RegistryLock Acquire(string lockPath, TimeSpan wait, TimeSpan staleAfter, RegistryLog log)
        {
            var ownerPath = lockPath + ".owner.json";
            var deadline = DateTime.UtcNow + wait;
            Exception last = null;
            while (true)
            {
                FileStream handle = null;
                try
                {
                    handle = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.DeleteOnClose);
                }
                catch (IOException e) when (!(e is FileNotFoundException) && !(e is DirectoryNotFoundException))
                {
                    last = e; // held by someone else -- or a transient SMB error; either way, wait
                }
                catch (UnauthorizedAccessException e)
                {
                    // Windows answers "access denied" while the previous holder's lock file is being
                    // deleted on close (delete pending), not only for real permission problems. Wait.
                    last = e;
                }

                if (handle != null)
                {
                    // Ours. Anything left in it, or an owner file not marked released, is a stale lock
                    // the server had already let go of: say so, then take it over.
                    var previous = ReadOwner(ownerPath);
                    if (handle.Length > 0 || (previous != null && previous.ReleasedUtc == null))
                        log.Warn("recover stale registry lock", lockPath, null,
                            "lock file was not held open; previous owner " + (previous?.ToString() ?? "unknown")
                            + (previous != null ? ", acquired " + previous.AcquiredUtc.ToString("u", CultureInfo.InvariantCulture) : string.Empty));
                    var me = LockOwner.Me();
                    WriteOwner(ownerPath, me, log);
                    var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(me));
                    handle.SetLength(0);
                    handle.Write(bytes, 0, bytes.Length);
                    handle.Flush(true);
                    return new RegistryLock(handle, ownerPath);
                }

                if (DateTime.UtcNow >= deadline)
                {
                    var owner = ReadOwner(ownerPath);
                    if (last is UnauthorizedAccessException && (owner == null || owner.ReleasedUtc != null))
                    {
                        // Denied the whole time with nobody holding it: a real permission problem.
                        log.Error("acquire registry lock", lockPath, last, "access denied for the whole wait and no current holder: check Modify on the folder");
                        throw (UnauthorizedAccessException)last;
                    }
                    var since = owner?.AcquiredUtc ?? SafeWriteTime(lockPath);
                    var age = since.HasValue ? DateTime.UtcNow - since.Value : (TimeSpan?)null;
                    var who = owner != null && owner.ReleasedUtc == null ? owner.ToString() : "an unknown process";
                    var message = new StringBuilder("The project list is locked by " + who
                        + (since.HasValue ? " since " + since.Value.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture) : string.Empty)
                        + (age.HasValue ? " (" + Describe(age.Value) + ")" : string.Empty) + ". Nothing was saved.");
                    if (age.HasValue && age.Value > staleAfter)
                        message.Append(" That is far longer than a save takes. If that PC crashed or lost its network connection, the file server"
                            + " releases the lock when it drops the connection, usually within minutes. The app never breaks a lock that is still"
                            + " held. If it does not clear, IT can close " + Path.GetFileName(lockPath) + " on the file server"
                            + " (Computer Management > Shared Folders > Open Files).");
                    else
                        message.Append(" Another PM is probably saving right now; try again in a moment.");
                    log.Error("acquire registry lock", lockPath, last, "held by " + who + (age.HasValue ? " for " + Describe(age.Value) : string.Empty));
                    throw new RegistryLockedException(message.ToString(), last);
                }
                Thread.Sleep(250);
            }
        }

        public void Dispose()
        {
            // Mark released before letting go, so the next PM can tell a clean release from a crash.
            try
            {
                var me = ReadOwner(_ownerPath);
                if (me != null && me.ProcessId == SafePid() && string.Equals(me.Machine, Environment.MachineName, StringComparison.OrdinalIgnoreCase))
                {
                    me.ReleasedUtc = DateTime.UtcNow;
                    File.WriteAllText(_ownerPath, JsonConvert.SerializeObject(me));
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
            try
            {
                _handle.SetLength(0);
                _handle.Flush(true);
            }
            catch (IOException) { }
            _handle.Dispose(); // delete-on-close; if the server keeps the file, the next PM reclaims it
        }

        private static void WriteOwner(string path, LockOwner owner, RegistryLog log)
        {
            try { File.WriteAllText(path, JsonConvert.SerializeObject(owner, Formatting.Indented)); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                log.Warn("write registry lock owner", path, e, "diagnostic only; the save continues");
            }
        }

        internal static LockOwner ReadOwner(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                using (var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var r = new StreamReader(s))
                    return JsonConvert.DeserializeObject<LockOwner>(r.ReadToEnd());
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is JsonException)
            {
                return null;
            }
        }

        private static DateTime? SafeWriteTime(string path)
        {
            try { return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : (DateTime?)null; }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { return null; }
        }

        private static int SafePid()
        {
            try { using (var p = Process.GetCurrentProcess()) return p.Id; }
            catch (Exception e) when (e is InvalidOperationException || e is NotSupportedException) { return -1; }
        }

        internal static string Describe(TimeSpan t) =>
            t.TotalMinutes >= 1 ? ((int)t.TotalMinutes) + " min " + t.Seconds + " s" : ((int)t.TotalSeconds) + " s";
    }

    /// <summary>
    /// Retries a file operation through the short failures a Windows share produces: sharing
    /// violations, antivirus scanners holding a just-written file, brief SMB disconnects. A missing
    /// file or folder is not retried -- that is not going to fix itself.
    /// </summary>
    internal sealed class Retrier
    {
        private readonly int[] _delaysMs;
        private readonly RegistryLog _log;

        /// <summary>Test hook: runs before every attempt of every operation, and may throw to simulate a share error.</summary>
        internal Action<string, int> BeforeAttempt { get; set; }

        public Retrier(int[] delaysMs, RegistryLog log)
        {
            _delaysMs = delaysMs;
            _log = log;
        }

        public void Run(string operation, string path, Action action) => Run<object>(operation, path, () => { action(); return null; });

        public T Run<T>(string operation, string path, Func<T> action)
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    BeforeAttempt?.Invoke(operation, attempt);
                    var result = action();
                    if (attempt > 1) _log.Warn(operation, path, null, "succeeded after " + attempt + " attempts");
                    return result;
                }
                catch (Exception e) when (IsTransient(e) && attempt <= _delaysMs.Length)
                {
                    if (attempt == 1) _log.Warn(operation, path, e, "retrying");
                    Thread.Sleep(_delaysMs[attempt - 1]);
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    _log.Error(operation, path, e, "gave up after " + attempt + " attempt(s)");
                    throw;
                }
            }
        }

        internal static bool IsTransient(Exception e) =>
            (e is IOException && !(e is FileNotFoundException) && !(e is DirectoryNotFoundException) && !(e is PathTooLongException))
            // A scanner or the server can refuse access for a moment; a real permission problem fails every attempt.
            || e is UnauthorizedAccessException;
    }

    /// <summary>
    /// A troubleshooting log for registry saves: every failure and every retry, with the Windows
    /// error, the operation attempted, who, which PC, and which path. Written beside the registry
    /// (registry-errors.log, where IT will look) and also on this PC (in case the share is the
    /// problem). Logging never throws.
    /// </summary>
    public sealed class RegistryLog
    {
        public const string FileName = "registry-errors.log";

        private readonly string[] _paths;

        public RegistryLog(params string[] paths)
        {
            _paths = paths ?? new string[0];
        }

        /// <summary>The PC's copy: %LOCALAPPDATA%\FieldToFinish\CrewUpload\registry-errors.log.</summary>
        public static string LocalPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FieldToFinish", "CrewUpload", FileName);

        public static string UserName
        {
            get
            {
                var domain = Environment.UserDomainName;
                return string.IsNullOrEmpty(domain) || string.Equals(domain, Environment.MachineName, StringComparison.OrdinalIgnoreCase)
                    ? Environment.UserName
                    : domain + "\\" + Environment.UserName;
            }
        }

        public IReadOnlyList<string> Paths => _paths;

        public void Error(string operation, string path, Exception e, string detail = null) => Write("ERROR", operation, path, e, detail);

        public void Warn(string operation, string path, Exception e, string detail = null) => Write("WARN", operation, path, e, detail);

        /// <summary>The Windows error code behind an exception: 32 for a sharing violation, 5 for access denied, 64 for a dropped network name.</summary>
        public static string WindowsError(Exception e)
        {
            if (e == null) return string.Empty;
            var hr = e.HResult;
            var code = (hr & unchecked((int)0xFFFF0000)) == unchecked((int)0x80070000) ? (hr & 0xFFFF).ToString(CultureInfo.InvariantCulture) : "?";
            return "win32=" + code + " hresult=0x" + hr.ToString("X8", CultureInfo.InvariantCulture);
        }

        private void Write(string level, string operation, string path, Exception e, string detail)
        {
            var line = new StringBuilder()
                .Append(DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture))
                .Append('\t').Append(level)
                .Append("\top=\"").Append(operation).Append('"')
                .Append("\tpath=\"").Append(path).Append('"')
                .Append("\tuser=\"").Append(UserName).Append('"')
                .Append("\tmachine=\"").Append(Environment.MachineName).Append('"');
            if (e != null)
                line.Append('\t').Append(WindowsError(e))
                    .Append("\terror=\"").Append(e.GetType().Name).Append(": ").Append(OneLine(e.Message)).Append('"');
            if (!string.IsNullOrEmpty(detail)) line.Append("\tdetail=\"").Append(OneLine(detail)).Append('"');
            line.AppendLine();

            foreach (var p in _paths)
            {
                if (string.IsNullOrEmpty(p)) continue;
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(p));
                    File.AppendAllText(p, line.ToString());
                }
                catch (Exception)
                {
                    // Never let logging turn one failure into two.
                }
            }
        }

        private static string OneLine(string s) => (s ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Replace("\"", "'");
    }
}
