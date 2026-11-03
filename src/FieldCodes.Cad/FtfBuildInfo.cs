using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.Runtime;
using FieldCodes.Deploy;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace FieldCodes.Cad
{
    /// <summary>
    /// Which FTF this Civil 3D session is running, and how it compares with what is installed
    /// and with the latest approved build. The build is read from the stamp build\FtfBuildInfo.targets
    /// wrote into this assembly; the installed bundle from its build-info.json; the latest approved
    /// build from what UPDATEFTF.ps1 last recorded. Nothing here touches the network or git.
    /// </summary>
    public static class FtfBuildInfo
    {
        public const string BundleName = "FieldToFinish.bundle";
        private const string BuildInfoFile = "build-info.json";

        /// <summary>The build stamped into this assembly.</summary>
        public static BuildStamp Running
        {
            get { return BuildStamp.FromAssembly(typeof(FtfBuildInfo).Assembly); }
        }

        /// <summary>The DLL this session loaded.</summary>
        public static string RunningFrom
        {
            get
            {
                try
                {
                    var assembly = typeof(FtfBuildInfo).Assembly;
                    if (!string.IsNullOrEmpty(assembly.Location)) return assembly.Location;
                    return new Uri(assembly.CodeBase).LocalPath;
                }
                catch (System.Exception) { return null; }
            }
        }

        /// <summary>%APPDATA%\Autodesk\ApplicationPlugins\FieldToFinish.bundle -- where UPDATEFTF and the installer put it.</summary>
        public static string InstalledBundleFolder
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                    "Autodesk", "ApplicationPlugins", BundleName);
            }
        }

        public static bool RunningFromInstalledBundle
        {
            get
            {
                var from = RunningFrom;
                if (string.IsNullOrEmpty(from)) return false;
                try
                {
                    var bundle = Path.GetFullPath(InstalledBundleFolder).TrimEnd('\\') + "\\";
                    return Path.GetFullPath(from).StartsWith(bundle, StringComparison.OrdinalIgnoreCase);
                }
                catch (System.Exception) { return false; }
            }
        }

        /// <summary>The installed bundle's build, or null when nothing is installed or it has no build-info.json.</summary>
        public static BuildStamp Installed
        {
            get
            {
                try
                {
                    var file = Path.Combine(InstalledBundleFolder, BuildInfoFile);
                    if (File.Exists(file)) return BuildStamp.FromJson(File.ReadAllText(file)) ?? new BuildStamp();
                    // An install from before build-info.json: there, but without a commit to compare.
                    return File.Exists(Path.Combine(InstalledBundleFolder, "PackageContents.xml")) ? new BuildStamp() : null;
                }
                catch (System.Exception) { return null; }
            }
        }

        /// <summary>The latest approved commit as UPDATEFTF.ps1 last recorded it on this machine.</summary>
        public static ApprovedHead LatestApproved
        {
            get
            {
                try
                {
                    var file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                            "FieldToFinish", "latest-approved.json");
                    return File.Exists(file) ? ApprovedHead.FromJson(File.ReadAllText(file)) : null;
                }
                catch (System.Exception) { return null; }
            }
        }

        /// <summary>Everything FTFVERSION prints.</summary>
        public static IList<string> Report()
        {
            return VersionReport.Lines(Running, RunningFrom, RunningFromInstalledBundle, Installed,
                                       InstalledBundleFolder, LatestApproved);
        }

        /// <summary>One line for windows and the startup notice.</summary>
        public static string OneLine()
        {
            var running = Running;
            return "Field to Finish -- " + (running.HasCommit ? running.Describe() : "a build without version information");
        }

        private static bool _announced;

        /// <summary>
        /// Puts one line on the command line once Civil 3D is idle after loading FTF, plus any
        /// WARNING / RESTART / UPDATE verdict, so a stale or development build is obvious at a glance.
        /// Never a reason for loading to fail.
        /// </summary>
        public static void AnnounceWhenReady()
        {
            try { AcadApp.Idle += OnIdle; }
            catch (System.Exception) { }
        }

        private static void OnIdle(object sender, EventArgs e)
        {
            try
            {
                var doc = AcadApp.DocumentManager.MdiActiveDocument;
                if (doc == null) return;            // wait for a drawing to talk to
                AcadApp.Idle -= OnIdle;
                if (_announced) return;
                _announced = true;

                var running = Running;
                var verdicts = VersionReport.Verdicts(running, RunningFromInstalledBundle, Installed, LatestApproved)
                                            .Where(v => !v.StartsWith("STATUS", StringComparison.Ordinal))
                                            .ToList();
                doc.Editor.WriteMessage("\n" + OneLine() + ". FTFVERSION for details.");
                foreach (var verdict in verdicts) doc.Editor.WriteMessage("\n  " + verdict);
                doc.Editor.WriteMessage("\n");
            }
            catch (System.Exception)
            {
                try { AcadApp.Idle -= OnIdle; } catch (System.Exception) { }
            }
        }
    }

    /// <summary>
    /// Counts the FTF commands that actually ran in this session. The headless smoke tests read
    /// it (by reflection) to prove a command was registered AND executed: an "Unknown command"
    /// in a script leaves the count where it was, and the test fails instead of passing on
    /// checks that never ran.
    /// </summary>
    public static class FtfCommandTrace
    {
        private static readonly Dictionary<string, int> Started = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, int> Finished = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The environment variable naming the diagnostic trace file. The headless smoke
        /// tests set it; an office session never does, and then nothing is written.</summary>
        public const string TraceVariable = "FTF_TRACE_FILE";

        private static readonly string TraceFile = ReadTraceFile();
        private static readonly object TraceLock = new object();

        private static string ReadTraceFile()
        {
            try
            {
                var path = Environment.GetEnvironmentVariable(TraceVariable);
                return string.IsNullOrWhiteSpace(path) ? null : path.Trim();
            }
            catch (System.Security.SecurityException) { return null; }
        }

        /// <summary>Whether <see cref="Step"/> writes anything: test before building a message
        /// that costs something.</summary>
        public static bool Tracing { get { return TraceFile != null; } }

        public static void Start(string command)
        {
            lock (Started) { int n; Started.TryGetValue(command, out n); Started[command] = n + 1; }
            if (Tracing) Step(command + " started");
        }

        /// <summary>The command got to its end without throwing (whether or not it drew anything).</summary>
        public static void Finish(string command)
        {
            lock (Started) { int n; Finished.TryGetValue(command, out n); Finished[command] = n + 1; }
            if (Tracing) Step(command + " finished");
        }

        /// <summary>
        /// One line, "TRACE &lt;time&gt; &lt;what&gt;", appended to the trace file and written through to
        /// the disk before this returns, so the last step before an unexpected end of the process
        /// (a crash, an exception nothing caught, a kill on a timeout) is still there to read. Never
        /// throws: diagnostics must not be what breaks a command.
        /// </summary>
        public static void Step(string what)
        {
            if (TraceFile == null) return;
            var line = "TRACE " + DateTime.Now.ToString("HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture) + " " +
                       (what ?? string.Empty).Replace("\r", " ").Replace("\n", " | ") + Environment.NewLine;
            lock (TraceLock)
            {
                try
                {
                    var bytes = System.Text.Encoding.UTF8.GetBytes(line);
                    using (var stream = new FileStream(TraceFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite, 4096, FileOptions.WriteThrough))
                    {
                        stream.Write(bytes, 0, bytes.Length);
                        stream.Flush(true);
                    }
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                catch (System.Security.SecurityException) { }
            }
        }

        /// <summary>The command threw: its full exception, inner exceptions and stack traces
        /// included, into the trace file.</summary>
        public static void Failed(string command, System.Exception ex)
        {
            if (TraceFile == null || ex == null) return;
            Step(command + " threw " + ex.GetType().FullName + ": " + ex.Message);
            foreach (var line in ex.ToString().Split('\n'))
                if (line.Trim().Length > 0) Step("  " + line.TrimEnd('\r'));
        }

        public static int StartedCount(string command)
        {
            lock (Started) { int n; return Started.TryGetValue(command, out n) ? n : 0; }
        }

        public static int FinishedCount(string command)
        {
            lock (Started) { int n; return Finished.TryGetValue(command, out n) ? n : 0; }
        }
    }

    public sealed class VersionCommands
    {
        /// <summary>
        /// Which FTF this is: the running build (commit, branch, date, approved or development),
        /// where it was loaded from, the installed build, the latest approved build UPDATEFTF
        /// knows of, and what to do if they differ.
        /// </summary>
        [CommandMethod("FTFVERSION", CommandFlags.Modal)]
        public void FtfVersion()
        {
            FtfCommandTrace.Start("FTFVERSION");
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            foreach (var line in FtfBuildInfo.Report()) doc.Editor.WriteMessage("\n" + line);
            doc.Editor.WriteMessage("\n");
            FtfCommandTrace.Finish("FTFVERSION");
        }
    }
}
