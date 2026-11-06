using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace FieldCodes
{
    /// <summary>
    /// Which per-user settings folder (under %APPDATA%) this build uses.
    ///
    ///   Commercial TRACE edition  ->  %APPDATA%\TRACE
    ///   Internal Parametrix build ->  %APPDATA%\FieldToFinish   (unchanged)
    ///
    /// The edition is stamped into the CAD assembly at build time as
    /// AssemblyMetadata("TraceEdition", "Default" | "Parametrix") by the TraceEdition
    /// MSBuild property. This class finds that stamp, so the choice needs no code in
    /// the CAD layer and cannot drift from the factory rules the same build ships.
    ///
    /// Anything unstamped (unit tests, other hosts) keeps the legacy folder, so existing
    /// behaviour is the default and TRACE is the explicit case.
    ///
    /// There is deliberately no migration: a TRACE install starts from the neutral
    /// factory defaults and customers import their own configuration.
    /// </summary>
    public static class Branding
    {
        public const string InternalFolderName = "FieldToFinish";
        public const string CommercialFolderName = "TRACE";
        public const string EditionMetadataKey = "TraceEdition";

        private static readonly object Gate = new object();
        private static string _resolved;

        public static string ConfigFolderName
        {
            get
            {
                lock (Gate)
                {
                    if (_resolved == null)
                        _resolved = Resolve(AppDomain.CurrentDomain.GetAssemblies(), EditionMetadataKey);
                    return _resolved;
                }
            }
        }

        /// <summary>True in the commercial TRACE edition.</summary>
        public static bool IsCommercial
        {
            get { return ConfigFolderName == CommercialFolderName; }
        }

        /// <summary>Prefix for the files TRACE writes beside drawings and settings: trace, or the internal ftf.</summary>
        public static string FilePrefix
        {
            get { return IsCommercial ? "trace" : "ftf"; }
        }

        /// <summary>The product name to show in windows: TRACE, or the internal name.</summary>
        public static string ProductName
        {
            get { return IsCommercial ? "TRACE" : "Field to Finish"; }
        }

        /// <summary>Maps an edition stamp to a folder. Only "Default" is the commercial
        /// edition; anything else, including no stamp, is the internal folder.</summary>
        public static string FolderForEdition(string edition)
        {
            return string.Equals(edition, "Default", StringComparison.OrdinalIgnoreCase)
                ? CommercialFolderName
                : InternalFolderName;
        }

        /// <summary>Looks for the edition stamp among the given assemblies.</summary>
        internal static string Resolve(IEnumerable<Assembly> assemblies, string metadataKey)
        {
            foreach (var assembly in assemblies)
            {
                var name = assembly.GetName().Name ?? "";
                if (name.StartsWith("System", StringComparison.Ordinal) ||
                    name.StartsWith("Microsoft", StringComparison.Ordinal) ||
                    name == "mscorlib" || name == "netstandard")
                    continue;

                try
                {
                    var stamp = assembly.GetCustomAttributes(typeof(AssemblyMetadataAttribute), false)
                        .OfType<AssemblyMetadataAttribute>()
                        .FirstOrDefault(a => a.Key == metadataKey);
                    if (stamp != null) return FolderForEdition(stamp.Value);
                }
                catch (Exception)
                {
                    // An assembly that cannot report its attributes is not the stamped one.
                }
            }
            return InternalFolderName;
        }

        /// <summary>Test hook: forget the cached answer.</summary>
        internal static void ResetForTests()
        {
            lock (Gate) { _resolved = null; }
        }
    }
}
