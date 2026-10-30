using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;

namespace FieldCodes.Deploy
{
    /// <summary>
    /// Which build an FTF assembly or installed bundle is: commit, branch, build date, channel.
    ///
    /// Written into each assembly at build time (build\FtfBuildInfo.targets) and into the
    /// bundle as build-info.json when it is packaged (deploy\make-release.ps1), so a running
    /// Civil 3D, the installed files and UPDATEFTF.ps1 can all be compared on the same terms.
    /// </summary>
    public sealed class BuildStamp
    {
        public const string Approved = "approved";
        public const string Dev = "dev";

        /// <summary>The assembly version (1.0.0); the commit is what identifies a build.</summary>
        [JsonProperty("version")] public string Version { get; set; }

        /// <summary>Full commit id, or null when the build could not tell.</summary>
        [JsonProperty("commit")] public string Commit { get; set; }

        [JsonProperty("branch")] public string Branch { get; set; }

        /// <summary>approved (built by UPDATEFTF from a clean origin/integration) or dev.</summary>
        [JsonProperty("channel")] public string Channel { get; set; }

        [JsonProperty("builtUtc")] public DateTime? BuiltUtc { get; set; }

        /// <summary>True when the tree it was built from had uncommitted changes.</summary>
        [JsonProperty("dirty")] public bool? Dirty { get; set; }

        /// <summary>The configuration it was built in (Release, Debug), when known.</summary>
        [JsonProperty("configuration", NullValueHandling = NullValueHandling.Ignore)]
        public string Configuration { get; set; }

        /// <summary>Program files and their SHA-256, in a bundle's build-info.json only.</summary>
        [JsonProperty("files", NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, string> Files { get; set; }

        [JsonIgnore]
        public bool HasCommit { get { return !string.IsNullOrEmpty(Commit); } }

        [JsonIgnore]
        public string ShortCommit
        {
            get { return HasCommit ? Commit.Substring(0, Math.Min(7, Commit.Length)) : "unknown"; }
        }

        /// <summary>An approved build: built by UPDATEFTF from a clean checkout of the integration branch.</summary>
        [JsonIgnore]
        public bool IsApproved
        {
            get { return string.Equals(Channel, Approved, StringComparison.OrdinalIgnoreCase) && Dirty != true && HasCommit; }
        }

        /// <summary>Same commit and same channel: the same build, whatever its date says.</summary>
        public bool SameBuildAs(BuildStamp other)
        {
            return other != null && HasCommit && other.HasCommit &&
                   string.Equals(Commit, other.Commit, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(Channel ?? string.Empty, other.Channel ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
                   Dirty == other.Dirty;
        }

        /// <summary>One line: "approved build 5c3c021 (integration), built 2026-10-30 14:05 UTC".</summary>
        public string Describe()
        {
            var kind = IsApproved ? "approved build" : (Dirty == true ? "DEV build with uncommitted changes" : "DEV build");
            var text = kind + " " + ShortCommit;
            if (!string.IsNullOrEmpty(Branch) && Branch != "unknown") text += " (" + Branch + ")";
            if (BuiltUtc.HasValue)
                text += ", built " + BuiltUtc.Value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC";
            return text;
        }

        // ------------------------------------------------------------------ reading

        /// <summary>The stamp build\FtfBuildInfo.targets wrote into an assembly.</summary>
        public static BuildStamp FromAssembly(Assembly assembly)
        {
            if (assembly == null) return new BuildStamp();
            var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var attribute in assembly.GetCustomAttributes(typeof(AssemblyMetadataAttribute), false)
                                              .OfType<AssemblyMetadataAttribute>())
                metadata[attribute.Key] = attribute.Value;

            var informational = assembly.GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false)
                                        .OfType<AssemblyInformationalVersionAttribute>()
                                        .Select(a => a.InformationalVersion)
                                        .FirstOrDefault();
            var version = assembly.GetName().Version;
            return FromMetadata(metadata, informational, version == null ? null : version.ToString(3));
        }

        /// <summary>Builds a stamp from the metadata pairs; unknown or malformed values become null.</summary>
        public static BuildStamp FromMetadata(IDictionary<string, string> metadata, string informationalVersion,
                                              string version)
        {
            metadata = metadata ?? new Dictionary<string, string>();
            string value;
            var stamp = new BuildStamp { Version = version };

            if (metadata.TryGetValue("FtfCommit", out value) && IsCommit(value)) stamp.Commit = value.ToLowerInvariant();
            if (metadata.TryGetValue("FtfBranch", out value) && Known(value)) stamp.Branch = value;
            if (metadata.TryGetValue("FtfChannel", out value) && Known(value)) stamp.Channel = value.ToLowerInvariant();
            if (metadata.TryGetValue("FtfBuildDate", out value)) stamp.BuiltUtc = ParseUtc(value);
            if (metadata.TryGetValue("FtfDirty", out value))
            {
                if (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)) stamp.Dirty = true;
                else if (string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)) stamp.Dirty = false;
            }

            if (string.IsNullOrEmpty(stamp.Version) && !string.IsNullOrEmpty(informationalVersion))
            {
                var plus = informationalVersion.IndexOf('+');
                stamp.Version = plus < 0 ? informationalVersion : informationalVersion.Substring(0, plus);
            }
            return stamp;
        }

        /// <summary>A bundle's build-info.json; null when missing or unreadable.</summary>
        public static BuildStamp FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                var stamp = JsonConvert.DeserializeObject<BuildStamp>(json);
                if (stamp == null) return null;
                if (!IsCommit(stamp.Commit)) stamp.Commit = null;
                if (stamp.BuiltUtc.HasValue && stamp.BuiltUtc.Value.Kind != DateTimeKind.Utc)
                    stamp.BuiltUtc = DateTime.SpecifyKind(stamp.BuiltUtc.Value.ToUniversalTime(), DateTimeKind.Utc);
                return stamp;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        public string ToJson()
        {
            return JsonConvert.SerializeObject(this, Formatting.Indented,
                new JsonSerializerSettings { DateFormatString = "yyyy-MM-ddTHH:mm:ssZ", DateTimeZoneHandling = DateTimeZoneHandling.Utc });
        }

        public static bool IsCommit(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length != 40) return false;
            foreach (var c in value.ToLowerInvariant())
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
            return true;
        }

        private static bool Known(string value)
        {
            return !string.IsNullOrWhiteSpace(value) && !string.Equals(value, "unknown", StringComparison.OrdinalIgnoreCase);
        }

        private static DateTime? ParseUtc(string value)
        {
            DateTime parsed;
            if (DateTime.TryParse(value, CultureInfo.InvariantCulture,
                                  DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out parsed))
                return DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
            return null;
        }
    }

    /// <summary>The latest approved commit UPDATEFTF.ps1 saw on origin/integration, as it records it.</summary>
    public sealed class ApprovedHead
    {
        [JsonProperty("commit")] public string Commit { get; set; }
        [JsonProperty("branch")] public string Branch { get; set; }
        [JsonProperty("subject")] public string Subject { get; set; }
        [JsonProperty("committedUtc")] public DateTime? CommittedUtc { get; set; }
        [JsonProperty("checkedUtc")] public DateTime? CheckedUtc { get; set; }

        public static ApprovedHead FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                var head = JsonConvert.DeserializeObject<ApprovedHead>(json);
                return head != null && BuildStamp.IsCommit(head.Commit) ? head : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }

    /// <summary>
    /// What FTFVERSION says: which build is running, where it was loaded from, which build is
    /// installed, and whether that is the latest approved one. Every sentence states a fact
    /// and, where something is off, the one thing to do about it.
    /// </summary>
    public static class VersionReport
    {
        public static IList<string> Lines(BuildStamp running, string runningFrom, bool runningFromInstalledBundle,
                                          BuildStamp installed, string installedFolder, ApprovedHead latest)
        {
            var lines = new List<string>();
            running = running ?? new BuildStamp();

            lines.Add("Field to Finish -- " + (running.HasCommit ? running.Describe() : "a build without version information"));
            if (running.HasCommit) lines.Add("  commit:    " + running.Commit);
            if (!string.IsNullOrEmpty(runningFrom)) lines.Add("  loaded:    " + runningFrom);
            if (installed != null)
                lines.Add("  installed: " + (installed.HasCommit ? installed.Describe() : "a build without version information") +
                          (string.IsNullOrEmpty(installedFolder) ? string.Empty : "  [" + installedFolder + "]"));
            else
                lines.Add("  installed: nothing in the per-user plugin folder" +
                          (string.IsNullOrEmpty(installedFolder) ? string.Empty : " (" + installedFolder + ")"));
            if (latest != null)
                lines.Add("  latest approved: " + latest.Commit.Substring(0, 7) +
                          (string.IsNullOrEmpty(latest.Subject) ? string.Empty : " \"" + latest.Subject + "\"") +
                          (latest.CheckedUtc.HasValue
                              ? " (as of UPDATEFTF " + latest.CheckedUtc.Value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC)"
                              : string.Empty));

            foreach (var line in Verdicts(running, runningFromInstalledBundle, installed, latest))
                lines.Add(line);
            return lines;
        }

        /// <summary>The conclusions, most important first. Empty never: there is always a status.</summary>
        public static IList<string> Verdicts(BuildStamp running, bool runningFromInstalledBundle,
                                             BuildStamp installed, ApprovedHead latest)
        {
            var verdicts = new List<string>();
            running = running ?? new BuildStamp();

            if (!running.HasCommit)
            {
                verdicts.Add("STATUS: this FTF carries no build information -- it predates version stamping. " +
                             "Run UPDATEFTF to install the current approved build.");
                return verdicts;
            }

            if (!runningFromInstalledBundle)
                verdicts.Add("WARNING: this session loaded FTF from outside the installed bundle (NETLOAD of a " +
                             "development build?). Restart Civil 3D without NETLOAD to run the installed build.");

            if (installed != null && installed.HasCommit && !installed.SameBuildAs(running))
                verdicts.Add("RESTART: the installed FTF is " + installed.ShortCommit +
                             " but this session is running " + running.ShortCommit +
                             ". Close and reopen Civil 3D to load the installed build.");

            if (!running.IsApproved)
                verdicts.Add("NOTE: a development build, not an approved release.");

            if (latest != null)
            {
                if (string.Equals(latest.Commit, running.Commit, StringComparison.OrdinalIgnoreCase))
                {
                    if (running.IsApproved && verdicts.Count == 0)
                        verdicts.Add("STATUS: running the latest approved build.");
                }
                else if (installed != null && string.Equals(latest.Commit, installed.Commit, StringComparison.OrdinalIgnoreCase))
                {
                    // Already covered by the RESTART line.
                }
                else
                {
                    verdicts.Add("UPDATE: a newer approved build (" + latest.Commit.Substring(0, 7) +
                                 ") is on the integration branch. Run UPDATEFTF.ps1, then restart Civil 3D.");
                }
            }
            else if (verdicts.Count == 0)
            {
                verdicts.Add("STATUS: running the installed " + (running.IsApproved ? "approved" : "development") +
                             " build. (UPDATEFTF.ps1 records the latest approved build; it has not run on this machine.)");
            }

            if (verdicts.Count == 0) verdicts.Add("STATUS: running the installed build.");
            return verdicts;
        }
    }
}
