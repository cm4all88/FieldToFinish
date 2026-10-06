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
                if (WNetGetConnection(drive, remote, ref length) != 0) return path; // a local drive
                return remote.ToString().TrimEnd('\\') + path.Substring(2);
            }
            catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException)
            {
                return path; // not Windows
            }
        }

        /// <summary>True when the path is on the project share by its UNC name.</summary>
        public static bool IsUnder(string path, string root) =>
            !string.IsNullOrEmpty(root) && Path.GetFullPath(path).TrimEnd('\\').StartsWith(Path.GetFullPath(root).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
    }
}
