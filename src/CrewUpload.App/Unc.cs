using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace CrewUpload.App
{
    /// <summary>
    /// Turns a path on a mapped drive (U:\PSO\...) into its UNC path (\\parametrix.com\pmx\PSO\...),
    /// so what the app records and opens never depends on the drive letters this PC happens to have.
    /// </summary>
    internal static class Unc
    {
        [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
        private static extern int WNetGetConnection(string localName, StringBuilder remoteName, ref int length);

        public static string FromMapped(string path)
        {
            if (string.IsNullOrEmpty(path) || path.Length < 2 || path[1] != ':') return path;
            try
            {
                var drive = path.Substring(0, 2);
                var length = 512;
                var remote = new StringBuilder(length);
                if (WNetGetConnection(drive, remote, ref length) == 0 && remote.Length > 0)
                    return remote.ToString().TrimEnd('\\') + path.Substring(2);
            }
            catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException)
            {
                // not Windows
            }

            // Second source: a drive mapped by logon script or Group Policy is recorded per user under
            // HKCU\Network\<letter>\RemotePath even when the call above cannot see it (e.g. elevated).
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Network\" + path.Substring(0, 1)))
                {
                    var remote = key?.GetValue("RemotePath") as string;
                    if (!string.IsNullOrEmpty(remote)) return remote.TrimEnd('\\') + path.Substring(2);
                }
            }
            catch (Exception e) when (e is System.Security.SecurityException || e is UnauthorizedAccessException || e is IOException)
            {
            }
            return path; // a local drive, or a mapping Windows will not describe: the caller says so
        }

        /// <summary>True for a drive-letter path (U:\...), as opposed to a UNC one.</summary>
        public static bool IsDriveLetter(string path) => !string.IsNullOrEmpty(path) && path.Length >= 2 && path[1] == ':';
    }
}
