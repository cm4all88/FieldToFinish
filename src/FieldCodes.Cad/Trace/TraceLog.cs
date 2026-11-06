#if TRACE_EDITION
using System;
using System.IO;

namespace FieldCodes.Cad
{
    /// <summary>
    /// A small startup log at %APPDATA%\TRACE\trace-startup.log. The ribbon and palette must never
    /// crash Civil 3D, so they swallow failures; this records what happened so a missing tab can
    /// be diagnosed. Never throws.
    /// </summary>
    internal static class TraceLog
    {
        private const long MaxBytes = 200 * 1024;

        public static string PathOnDisk()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                Branding.ConfigFolderName,
                "trace-startup.log");
        }

        public static void Write(string message)
        {
            try
            {
                var path = PathOnDisk();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                if (File.Exists(path) && new FileInfo(path).Length > MaxBytes) File.Delete(path);
                File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine);
            }
            catch (System.Exception)
            {
                // Logging must never cause a problem of its own.
            }
        }

        public static void Write(string where, System.Exception ex)
        {
            Write(where + " FAILED: " + ex);
        }
    }
}
#endif
