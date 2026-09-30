using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;
using Autodesk.AutoCAD.Runtime;
using FieldCodes.Deploy;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace FieldCodes.Cad
{
    /// <summary>
    /// Keeps a surveyor's FTF the same as the office copy without anybody having to think about it.
    ///
    /// Installing records where it was installed from (the setup folder on the office drive). At
    /// startup FTF reads that folder's version line -- on a background thread, so a slow or absent
    /// network never delays Civil 3D -- and if the office copy is newer it says so in the FTF
    /// window. The copy itself happens when Civil 3D closes: a loaded assembly cannot be replaced
    /// underneath a running session, so the installer is launched at quit and waits for the process
    /// to end. The next start is current.
    ///
    /// Deliberately quiet: nothing is downloaded, nothing is prompted, nothing interrupts drafting,
    /// and a drawing in progress is never touched. If the office drive is unreachable, or FTF was
    /// installed by hand, this does nothing at all.
    /// </summary>
    public static class OfficeUpdate
    {
        private const string VersionFile = "version.txt";
        private const string SourceFile = "source.txt";
        private const string Installer = "install.ps1";

        /// <summary>The bundle folder to read instead of the one this assembly sits in. For tests.</summary>
        public static string BundleOverride { get; set; }

        /// <summary>
        /// False stops the installer being launched at quit, whatever the check found. The UI test
        /// sets it: a test must be able to exercise the decision without installing anything.
        /// </summary>
        public static bool Armed { get; set; }

        public static UpdateState State { get; private set; }
        public static OfficeBuild Installed { get; private set; }
        public static OfficeBuild Office { get; private set; }

        /// <summary>Where the office copy is, as installing recorded it. Null when installed by hand.</summary>
        public static string OfficeFolder { get; private set; }

        /// <summary>What went wrong reading the office copy, for FTFUPDATE to report. Null when nothing did.</summary>
        public static string Trouble { get; private set; }

        /// <summary>The sentence the drafter reads, in the FTF window and from FTFUPDATE.</summary>
        public static string Sentence
        {
            get { return OfficeBuild.Sentence(State, Installed, Office); }
        }

        /// <summary>
        /// Called once as FTF loads. The check runs on its own thread; the quit hook is registered
        /// straight away, because by the time the drafter closes Civil 3D the answer is long in.
        /// </summary>
        public static void Start()
        {
            Armed = true;
            try
            {
                AcadApp.QuitWillStart += (s, e) => ApplyAtQuit();
                var thread = new Thread(() => Check()) { IsBackground = true, Name = "FTF office update check" };
                thread.Start();
            }
            catch (System.Exception ex)
            {
                Trouble = ex.Message;   // never a reason to fail loading
            }
        }

        /// <summary>Reads both version lines and decides. Safe to call again -- FTFUPDATE does.</summary>
        public static UpdateState Check()
        {
            State = UpdateState.Unknown;
            Installed = null;
            Office = null;
            OfficeFolder = null;
            Trouble = null;

            try
            {
                var bundle = BundleFolder();
                if (bundle == null) { Trouble = "FTF is not running from an installed bundle."; return State; }

                Installed = OfficeBuild.Read(FirstLine(Path.Combine(bundle, VersionFile)));

                var recorded = FirstLine(Path.Combine(bundle, SourceFile));
                if (string.IsNullOrEmpty(recorded))
                {
                    Trouble = "This FTF was installed by hand, so there is no office copy to check.";
                    return State;
                }

                OfficeFolder = recorded.Trim();
                if (!LooksLikeSetupFolder(OfficeFolder))
                {
                    Trouble = "The office copy is not reachable right now (" + OfficeFolder + ").";
                    return State;
                }

                Office = OfficeBuild.Read(FirstLine(Path.Combine(OfficeFolder, VersionFile)));
                State = OfficeBuild.Compare(Installed, Office);
            }
            catch (System.Exception ex)
            {
                Trouble = ex.Message;
            }

            return State;
        }

        /// <summary>
        /// Launches the office copy's own installer, which waits for Civil 3D to close and then
        /// replaces the bundle. Nothing is copied by this process: it is the one holding the
        /// assembly open.
        /// </summary>
        private static void ApplyAtQuit()
        {
            try
            {
                var arguments = UpdaterArguments();
                if (arguments == null) return;

                Process.Start(new ProcessStartInfo("powershell.exe")
                {
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetTempPath()
                });
            }
            catch (System.Exception)
            {
                // Quitting must never be interrupted. A missed update is caught at the next start.
            }
        }

        /// <summary>
        /// What would be run as Civil 3D closes, or null when nothing should be. Public because
        /// the decision to launch an installer is worth testing, and running one in a test is not.
        /// </summary>
        public static string UpdaterArguments()
        {
            if (!Armed || State != UpdateState.UpdateWaiting) return null;
            if (!LooksLikeSetupFolder(OfficeFolder)) return null;

            return "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" +
                   Path.Combine(OfficeFolder, Installer) +
                   "\" -WaitForCivil3D -Source \"" + OfficeFolder + "\"";
        }

        /// <summary>The bundle root: this assembly sits two folders down, in Contents4.</summary>
        private static string BundleFolder()
        {
            if (!string.IsNullOrEmpty(BundleOverride)) return BundleOverride;

            var here = Path.GetDirectoryName(new Uri(Assembly.GetExecutingAssembly().CodeBase).LocalPath);
            if (here == null) return null;
            var contents = Path.GetDirectoryName(here);
            var bundle = contents == null ? null : Path.GetDirectoryName(contents);
            if (bundle == null) return null;
            return File.Exists(Path.Combine(bundle, VersionFile)) || File.Exists(Path.Combine(bundle, SourceFile))
                 ? bundle : null;
        }

        /// <summary>A folder only counts as the office copy when it holds a setup folder's parts.</summary>
        private static bool LooksLikeSetupFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder)) return false;
            try
            {
                return File.Exists(Path.Combine(folder, Installer)) &&
                       File.Exists(Path.Combine(folder, "Bundle\\PackageContents.xml"));
            }
            catch (System.Exception) { return false; }
        }

        private static string FirstLine(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                using (var reader = new StreamReader(path)) return reader.ReadLine();
            }
            catch (System.Exception) { return null; }
        }

        /// <summary>Everything FTFUPDATE prints, and what the FTF window shows in one line.</summary>
        public static string Report()
        {
            var text = Sentence;
            if (!string.IsNullOrEmpty(OfficeFolder)) text += "\n  office copy: " + OfficeFolder;
            if (!string.IsNullOrEmpty(Trouble)) text += "\n  " + Trouble;
            return text;
        }
    }

    public sealed class OfficeUpdateCommands
    {
        /// <summary>
        /// Says which FTF this machine has, which one the office has, and what happens next.
        /// Nothing to answer: an update installs itself when Civil 3D closes.
        /// </summary>
        [CommandMethod("FTFUPDATE", CommandFlags.Modal)]
        public void Update()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            OfficeUpdate.Check();
            doc.Editor.WriteMessage("\n" + OfficeUpdate.Report() + "\n");

            if (OfficeUpdate.State == UpdateState.UpdateWaiting)
                doc.Editor.WriteMessage(
                    "Close Civil 3D when you are at a good stopping point; the update installs itself.\n");
        }
    }
}
