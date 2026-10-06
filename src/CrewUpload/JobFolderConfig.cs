using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace CrewUpload
{
    /// <summary>
    /// What the crew went out to do: TOPO, LINEOUT, STAKE ... The code ends the download
    /// folder's name (20260128-JAM-1521-799-TOPO) and so every file in it.
    /// </summary>
    public sealed class WorkType
    {
        [JsonProperty("code")] public string Code { get; set; }
        [JsonProperty("name")] public string Name { get; set; }

        public override string ToString() => Code + " - " + Name;
    }

    /// <summary>
    /// One kind of file inside a crew download: photos, field notes, data files, as-built notes.
    /// Decides where in the download folder it goes and the suffix on its name.
    /// </summary>
    public sealed class UploadCategory
    {
        /// <summary>Stable id used by documents and the upload log. Never shown.</summary>
        [JsonProperty("key")] public string Key { get; set; }

        /// <summary>What the crew sees in the Type column.</summary>
        [JsonProperty("name")] public string Name { get; set; }

        /// <summary>Goes into the file name through {code}: ...-TOPO-FN.pdf. May be empty (raw data has none).</summary>
        [JsonProperty("code")] public string Code { get; set; }

        /// <summary>Subfolder of the download folder ("Photos"). Empty = the download folder itself.</summary>
        [JsonProperty("folder")] public string Folder { get; set; }

        /// <summary>
        /// Words that put a file in this category when they appear in its name or in the
        /// name of a folder it was dropped in ("lineout", "field notes"). Checked before
        /// extensions, so a lineout sketch photographed as a .jpg is still a lineout.
        /// </summary>
        [JsonProperty("keywords")] public List<string> Keywords { get; set; } = new List<string>();

        /// <summary>Extensions, with the dot. Used when no keyword matched.</summary>
        [JsonProperty("extensions")] public List<string> Extensions { get; set; } = new List<string>();

        /// <summary>Overrides the config-wide file name pattern for this category.</summary>
        [JsonProperty("fileName")] public string FileName { get; set; }

        /// <summary>The category used when nothing else matches. Exactly one should be set.</summary>
        [JsonProperty("fallback")] public bool Fallback { get; set; }

        /// <summary>
        /// Colour of its drop box and Type stripe, "#RRGGBB". The brand guide reserves the
        /// secondary and tertiary palette for exactly this: telling categories apart.
        /// </summary>
        [JsonProperty("color")] public string Color { get; set; }

        public override string ToString() => Name;
    }

    /// <summary>
    /// Something the crew can start from the tool, already named and in the right folder:
    /// a field lineout, a blank field notes sheet.
    /// </summary>
    public sealed class DocumentTemplate
    {
        /// <summary>Button caption: "New field lineout".</summary>
        [JsonProperty("name")] public string Name { get; set; }

        /// <summary>Key of the category that decides its folder and name.</summary>
        [JsonProperty("category")] public string Category { get; set; }

        /// <summary>
        /// File copied to make the new document (.xlsx, .docx, .pdf, .dwg ...). Relative paths
        /// are relative to the config file. Without one a plain text sheet is written.
        /// </summary>
        [JsonProperty("template")] public string Template { get; set; }

        public override string ToString() => Name;
    }

    /// <summary>
    /// How the upload window looks, from the Parametrix brand guide (Nov 2023): charcoal, red and
    /// white, the primary logo on white, the ix formation as a corner element, and the guide's
    /// Microsoft Office stand-ins for the brand fonts (Rockwell for Klinic Slab headlines,
    /// Franklin Gothic for body text).
    /// </summary>
    public sealed class Branding
    {
        [JsonProperty("companyName")] public string CompanyName { get; set; } = "Parametrix";

        [JsonProperty("appTitle")] public string AppTitle { get; set; } = "Crew Upload";

        /// <summary>Primary (charcoal/red) logo PNG for the white header. Relative to the config file.</summary>
        [JsonProperty("logo")] public string Logo { get; set; } = @"branding\parametrix-logo.png";

        /// <summary>The ix formation, set in the bottom-left corner. Optional.</summary>
        [JsonProperty("ixMark")] public string IxMark { get; set; } = @"branding\parametrix-ix.png";

        /// <summary>Text, outlines and secondary buttons: Charcoal.</summary>
        [JsonProperty("primaryColor")] public string PrimaryColor { get; set; } = "#333333";

        /// <summary>The one thing to press, problems, the spacer arrow: Parametrix Red. Used sparingly.</summary>
        [JsonProperty("accentColor")] public string AccentColor { get; set; } = "#EE3D24";

        /// <summary>First installed font wins.</summary>
        [JsonProperty("headlineFonts")] public List<string> HeadlineFonts { get; set; } = new List<string>();

        [JsonProperty("bodyFonts")] public List<string> BodyFonts { get; set; } = new List<string>();

        internal static bool IsColor(string value) =>
            value != null && Regex.IsMatch(value, "^#[0-9A-Fa-f]{6}$");
    }

    /// <summary>
    /// The office job folder standard: where projects live, what folders a new project gets,
    /// and how uploaded files are sorted and named. Ships as job-folders.json beside the app
    /// so the CAD manager changes it without a rebuild.
    /// </summary>
    public sealed class JobFolderConfig
    {
        public const string DefaultFileName = "job-folders.json";

        [JsonProperty("version")] public string Version { get; set; } = "1";

        /// <summary>
        /// Where the PM's folder picker opens when registering a project: the Clients folder on the
        /// share. Only a starting point -- the app never searches it or decides a folder from it.
        /// </summary>
        [JsonProperty("jobsRoot")] public string JobsRoot { get; set; }

        /// <summary>
        /// The shared list of registered projects (client-task -> base Survey folder), in its own
        /// configuration folder on the share -- never beside the exe -- so every PC reads one list and
        /// the folder's NTFS permissions decide who may change it (PMs/admins Modify, crews Read).
        /// </summary>
        [JsonProperty("registryFile")] public string RegistryFile { get; set; } = @"\\parametrix.com\pmx\PSO\Shared\Divisions\00Survey\FLD\CrewUpload\Config\project-registry.json";

        /// <summary>How many earlier versions of the registry are kept: project-registry.backup-1.json (newest) to -N.</summary>
        [JsonProperty("registryBackups")] public int RegistryBackups { get; set; } = 5;

        /// <summary>
        /// Windows user names allowed to open project setup, as a second check after --setup. Empty
        /// lets anyone who starts with --setup in. A convenience only: the registry folder's NTFS
        /// permissions are what actually stop a crew member changing it.
        /// </summary>
        [JsonProperty("projectManagers")] public List<string> ProjectManagers { get; set; } = new List<string>();

        /// <summary>Only UNC paths may be registered, never a mapped drive letter.</summary>
        [JsonProperty("requireUncPaths")] public bool RequireUncPaths { get; set; } = true;

        /// <summary>A client-task number (1800-119) has to match this before anything is looked up.</summary>
        [JsonProperty("projectNumberPattern")] public string ProjectNumberPattern { get; set; } = @"^([0-9]{3}-)?[0-9]{4}-[0-9]{3}$";

        /// <summary>
        /// Under the registered base Survey folder, the one place the app writes: each crew download
        /// keeps its own folder in here. Moving it on to its processed location is office work.
        /// </summary>
        [JsonProperty("unprocessedFolder")] public string UnprocessedFolder { get; set; } = @"02Field\01FLD_DR_FN_DCfile\Unprocessed";

        /// <summary>
        /// Name of one crew download: {date}, {crew}, {projectNumber}, {workType}. The same pattern
        /// reads a folder the crew already named, so dropping it fills in the project, crew, date and type.
        /// </summary>
        [JsonProperty("downloadName")] public string DownloadName { get; set; } = "{date}-{crew}-{projectNumber}-{workType}";

        /// <summary>
        /// Name of a file in the download, without extension: {download} (the folder's name),
        /// {code}, {original}, {number} (the last run of digits in the original name: the camera's
        /// photo number), and the download's own tokens. A category's fileName overrides it.
        /// </summary>
        [JsonProperty("fileName")] public string FileName { get; set; } = "{download}-{code}";

        [JsonProperty("dateFormat")] public string DateFormat { get; set; } = "yyyyMMdd";

        /// <summary>
        /// Digits of the number added when a name is taken: 1 gives -2, -3 ...; a pattern with {seq}
        /// numbers every file instead.
        /// </summary>
        [JsonProperty("sequenceDigits")] public int SequenceDigits { get; set; } = 1;

        [JsonProperty("workTypes")] public List<WorkType> WorkTypes { get; set; } = new List<WorkType>();

        [JsonProperty("categories")] public List<UploadCategory> Categories { get; set; } = new List<UploadCategory>();

        [JsonProperty("documents")] public List<DocumentTemplate> Documents { get; set; } = new List<DocumentTemplate>();

        /// <summary>
        /// CSV kept in each download folder, mapping every original file name to its final name,
        /// with who uploaded it and when. Appended to, never rewritten.
        /// </summary>
        [JsonProperty("manifestFile")] public string ManifestFile { get; set; } = "upload-manifest.csv";

        [JsonProperty("branding")] public Branding Branding { get; set; } = new Branding();

        /// <summary>Folder the config was read from; relative template paths resolve against it.</summary>
        [JsonIgnore] public string BaseDirectory { get; set; }

        public WorkType WorkType(string code) =>
            WorkTypes.FirstOrDefault(w => string.Equals(w.Code, code, StringComparison.OrdinalIgnoreCase));

        public UploadCategory Category(string key) =>
            Categories.FirstOrDefault(c => string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase));

        /// <summary>A file named in the config, resolved against the config's folder. Null when not set or not there.</summary>
        public string ResolveFile(string relative)
        {
            if (string.IsNullOrWhiteSpace(relative)) return null;
            var path = Path.IsPathRooted(relative)
                ? relative
                : Naming.Combine(BaseDirectory ?? AppDomain.CurrentDomain.BaseDirectory, relative, new Dictionary<string, string>());
            return File.Exists(path) ? path : null;
        }

        /// <summary>The registry file's full path.</summary>
        [JsonIgnore]
        public string RegistryPath =>
            string.IsNullOrWhiteSpace(RegistryFile) ? null
            : Path.IsPathRooted(RegistryFile) || ProjectRegistry.IsUnc(RegistryFile) ? RegistryFile
            : Naming.Combine(BaseDirectory ?? AppDomain.CurrentDomain.BaseDirectory, RegistryFile, new Dictionary<string, string>());

        [JsonIgnore] public UploadCategory FallbackCategory => Categories.FirstOrDefault(c => c.Fallback) ?? Categories.LastOrDefault();

        /// <summary>
        /// Client-task, trimmed and upper-cased. A full number with its prefix (554-1800-119) comes
        /// back as the client-task crews use in names (1800-119).
        /// </summary>
        public static string NormalizeProjectNumber(string number)
        {
            string clientTask, phase;
            return ParseProjectNumber(number, out clientTask, out phase) ? clientTask : Compact(number);
        }

        /// <summary>
        /// Reads a project number as crews and folders write it. A Parametrix project number is
        /// prefix-client-task, 3-4-3 (554-1800-119), with the client in the middle; crews often write just
        /// client-task (1800-119); either may end in a phase (554-1800-119-141, 1800-119-141). Parts come
        /// back padded -- prefix ###, client ####, task ### -- so zeros are never dropped (1800-011 is
        /// not 1800-119). Prefix and phase are null when absent. False when there is no client-task.
        /// </summary>
        public static bool ParseProjectNumber(string number, out string prefix, out string clientTask, out string phase)
        {
            prefix = clientTask = phase = null;
            var parts = Compact(number).Split('-');
            // The client is the 4-digit part followed by a task of up to 3 digits. Only a prefix of up to
            // 3 digits may come before it, and only a phase after the task.
            for (var pass = 0; pass < 2 && clientTask == null; pass++)
                for (var i = 0; i <= 1 && i + 1 < parts.Length; i++)
                {
                    if (i == 1 && !Regex.IsMatch(parts[0], "^[0-9]{1,3}$")) continue;
                    var clientOk = pass == 0 ? Regex.IsMatch(parts[i], "^[0-9]{4}$") : Regex.IsMatch(parts[i], "^[0-9]{1,4}$");
                    if (!clientOk || !Regex.IsMatch(parts[i + 1], "^[0-9]{1,3}$")) continue;
                    if (i + 2 < parts.Length && !Regex.IsMatch(parts[i + 2], "^[0-9]{1,4}$")) continue;
                    if (i + 3 < parts.Length) continue;
                    prefix = i == 1 ? parts[0].PadLeft(3, '0') : null;
                    clientTask = parts[i].PadLeft(4, '0') + "-" + parts[i + 1].PadLeft(3, '0');
                    phase = i + 2 < parts.Length ? parts[i + 2] : null;
                    break;
                }
            return clientTask != null;
        }

        public static bool ParseProjectNumber(string number, out string clientTask, out string phase)
        {
            string prefix;
            return ParseProjectNumber(number, out prefix, out clientTask, out phase);
        }

        /// <summary>The full 3-4-3 number (554-1800-119) when the prefix is there, else client-task (1800-119).</summary>
        public static string NormalizeFullNumber(string number)
        {
            string prefix, clientTask, phase;
            if (!ParseProjectNumber(number, out prefix, out clientTask, out phase)) return Compact(number);
            return prefix == null ? clientTask : prefix + "-" + clientTask;
        }

        /// <summary>A phase as crews type it: digits only, or null for none.</summary>
        public static string NormalizePhase(string phase)
        {
            var p = Compact(phase);
            return p.Length == 0 ? null : p;
        }

        public static bool IsValidPhase(string phase) => phase == null || Regex.IsMatch(phase, "^[0-9]{1,4}$");

        private static string Compact(string s) => Regex.Replace((s ?? string.Empty).Trim().ToUpperInvariant(), @"\s+", string.Empty);

        /// <summary>True when the user may open project setup (after --setup). Not a security boundary.</summary>
        public bool IsProjectManager(string userName, string domainUser = null)
        {
            if (ProjectManagers == null || ProjectManagers.Count == 0) return true;
            return ProjectManagers.Any(m => string.Equals(m, userName, StringComparison.OrdinalIgnoreCase)
                || (domainUser != null && string.Equals(m, domainUser, StringComparison.OrdinalIgnoreCase)));
        }

        /// <summary>1800-119 -> client 1800, task 119.</summary>
        public static bool SplitProjectNumber(string number, out string client, out string task)
        {
            var parts = NormalizeProjectNumber(number).Split('-');
            client = parts.Length == 2 ? parts[0] : null;
            task = parts.Length == 2 ? parts[1] : null;
            return parts.Length == 2 && client.Length > 0 && task.Length > 0;
        }

        /// <summary>
        /// A full number (554-1800-119) or client-task (1800-119), with or without a phase. Either form
        /// matching projectNumberPattern is enough, so an older settings file still accepts full numbers.
        /// </summary>
        public bool IsValidProjectNumber(string number)
        {
            string prefix, clientTask, phase;
            if (!ParseProjectNumber(number, out prefix, out clientTask, out phase)) return false;
            var full = prefix == null ? clientTask : prefix + "-" + clientTask;
            var pattern = ProjectNumberPattern ?? ".+";
            return Regex.IsMatch(full, pattern, RegexOptions.IgnoreCase) || Regex.IsMatch(clientTask, pattern, RegexOptions.IgnoreCase);
        }

        public static JobFolderConfig Load(string path)
        {
            var config = JsonConvert.DeserializeObject<JobFolderConfig>(File.ReadAllText(path)) ?? new JobFolderConfig();
            config.BaseDirectory = Path.GetDirectoryName(Path.GetFullPath(path));
            config.LoadedFrom = Path.GetFullPath(path);
            return config;
        }

        /// <summary>The job-folders.json this config was read from; Settings saves back to it.</summary>
        [JsonIgnore] public string LoadedFrom { get; set; }

        /// <summary>
        /// Writes the config to a temporary file beside <paramref name="path"/>, checks it reads back,
        /// then swaps it in, so a failed save never leaves a half-written job-folders.json.
        /// </summary>
        public void Save(string path)
        {
            var text = JsonConvert.SerializeObject(this, Formatting.Indented);
            var temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllText(temp, text);
                if (JsonConvert.DeserializeObject<JobFolderConfig>(File.ReadAllText(temp)) == null)
                    throw new InvalidDataException("The settings did not read back. Nothing was saved.");
                if (File.Exists(path)) File.Replace(temp, path, null, true);
                else File.Move(temp, path);
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        /// <summary>Everything wrong with the config, in words a CAD manager can act on.</summary>
        public void Validate(ICollection<string> problems)
        {
            if (string.IsNullOrWhiteSpace(RegistryFile)) problems.Add("registryFile is not set: the shared list of registered projects.");
            else if (RequireUncPaths && !ProjectRegistry.IsUnc(RegistryFile))
                problems.Add("registryFile must be a permanent UNC path in the shared config folder, e.g. \\\\parametrix.com\\pmx\\...\\CrewUpload\\Config\\project-registry.json.");
            if (RegistryBackups < 1 || RegistryBackups > 20) problems.Add("registryBackups must be 1 to 20.");
            if (string.IsNullOrWhiteSpace(UnprocessedFolder) || Path.IsPathRooted(UnprocessedFolder) || Naming.Segments(UnprocessedFolder).Any(x => x == ".."))
                problems.Add("unprocessedFolder must be a folder inside the project's Survey folder.");
            if (SequenceDigits < 1 || SequenceDigits > 6) problems.Add("sequenceDigits must be 1 to 6.");
            if (string.IsNullOrWhiteSpace(FileName)) problems.Add("fileName is empty.");
            if (string.IsNullOrWhiteSpace(DownloadName) || DownloadName.IndexOf("{projectNumber}", StringComparison.Ordinal) < 0)
                problems.Add("downloadName must contain {projectNumber}.");
            if (WorkTypes.Count == 0) problems.Add("No workTypes: a download needs one (TOPO, LINEOUT ...).");
            foreach (var w in WorkTypes)
                if (string.IsNullOrWhiteSpace(w.Code) || !Regex.IsMatch(w.Code, "^[A-Za-z0-9]+$"))
                    problems.Add("Work type '" + w.Name + "' needs a code of letters and digits only.");
            foreach (var g in WorkTypes.GroupBy(w => (w.Code ?? string.Empty).ToUpperInvariant()).Where(g => g.Count() > 1))
                problems.Add("Work type code '" + g.Key + "' is used more than once.");
            if (string.IsNullOrWhiteSpace(ManifestFile) || ManifestFile.IndexOfAny(Naming.WindowsInvalid) >= 0)
                problems.Add("manifestFile must be a plain file name.");
            try { Regex.IsMatch(string.Empty, ProjectNumberPattern ?? string.Empty); }
            catch (ArgumentException e) { problems.Add("projectNumberPattern is not a valid pattern: " + e.Message); }
            try { DateTime.Today.ToString(DateFormat); }
            catch (FormatException) { problems.Add("dateFormat '" + DateFormat + "' is not a date format."); }

            if (Branding == null) Branding = new Branding();
            if (!Branding.IsColor(Branding.PrimaryColor)) problems.Add("branding.primaryColor must be a colour like #333333.");
            if (!Branding.IsColor(Branding.AccentColor)) problems.Add("branding.accentColor must be a colour like #EE3D24.");

            if (Categories.Count == 0) problems.Add("No categories: nothing would know where a file goes.");
            foreach (var g in Categories.GroupBy(c => (c.Key ?? string.Empty).ToLowerInvariant()).Where(g => g.Count() > 1))
                problems.Add("Category key '" + g.Key + "' is used more than once.");
            foreach (var c in Categories)
            {
                var label = "Category '" + (c.Name ?? c.Key) + "'";
                if (string.IsNullOrWhiteSpace(c.Key)) problems.Add(label + " has no key.");
                if (string.IsNullOrWhiteSpace(c.Name)) problems.Add("Category '" + c.Key + "' has no name.");
                if (Path.IsPathRooted(c.Folder ?? string.Empty) || Naming.Segments(c.Folder).Any(x => x == ".."))
                    problems.Add(label + " folder must stay inside the download folder.");
                if (!string.IsNullOrEmpty(c.Code) && c.Code.IndexOfAny(Naming.WindowsInvalid) >= 0) problems.Add(label + " code has characters a file name cannot hold.");
                if (c.Color != null && !Branding.IsColor(c.Color)) problems.Add(label + " color must be like #3FB549.");
                if (c.Extensions.Any(e => string.IsNullOrEmpty(e) || e[0] != '.')) problems.Add(label + " extensions must start with a dot.");
            }
            if (Categories.Count(c => c.Fallback) != 1) problems.Add("Exactly one category should be the fallback, for files nothing else matches.");

            foreach (var d in Documents)
                if (Category(d.Category) == null)
                    problems.Add("Document '" + d.Name + "' uses category '" + d.Category + "', which does not exist.");
        }

        /// <summary>
        /// The shipped standard. config\job-folders.json is a copy of this; a test keeps the
        /// two in step.
        /// </summary>
        public static JobFolderConfig CreateDefault()
        {
            return new JobFolderConfig
            {
                JobsRoot = @"\\parametrix.com\pmx\PSO\Projects\Clients",
                WorkTypes = new List<WorkType>
                {
                    new WorkType { Code = "TOPO", Name = "Topographic survey" },
                    new WorkType { Code = "LINEOUT", Name = "Field lineout" },
                    new WorkType { Code = "STAKE", Name = "Construction stakeout" },
                    new WorkType { Code = "BNDY", Name = "Boundary" },
                    new WorkType { Code = "CTRL", Name = "Control" },
                    new WorkType { Code = "ASBLT", Name = "As-built" },
                    new WorkType { Code = "ESMT", Name = "Easement" },
                },
                Categories = new List<UploadCategory>
                {
                    new UploadCategory
                    {
                        // 20260128-JAM-1521-799-TOPO.job / .jxl: the data carries the download's own name.
                        Key = "data", Name = "Job files", Code = "", Color = "#0073BB", Folder = "", FileName = "{download}", Fallback = true,
                        Keywords = { "raw", "raw data", "rawdata", "data" },
                        Extensions = { ".job", ".jxl", ".raw", ".rw5", ".dc", ".t01", ".t02", ".t04", ".dat", ".fbk", ".gsi", ".sdr", ".crd", ".csv", ".pnt", ".tsj", ".jbk" },
                    },
                    new UploadCategory
                    {
                        Key = "notes", Name = "Field notes", Code = "FN", Color = "#3FB549", Folder = "",
                        Keywords = { "fn", "field notes", "field note", "fieldnotes", "notes", "fieldbook", "field book" },
                    },
                    new UploadCategory
                    {
                        Key = "asbuilt", Name = "As-built notes", Code = "ASB", Color = "#F36E21", Folder = "",
                        Keywords = { "asb", "asbuilt", "as built", "asbuilts", "as builts" },
                    },
                    new UploadCategory
                    {
                        // Client-task-date-camera number: IMG_0412.JPG -> 1521-799-20260128-0412.jpg.
                        Key = "photos", Name = "Photos", Code = "PHOTO", Color = "#FCC214", Folder = "Photos", FileName = "{projectNumber}-{date}-{number}",
                        Keywords = { "photo", "photos", "pics", "pictures", "picture" },
                        Extensions = { ".jpg", ".jpeg", ".png", ".heic", ".heif", ".gif", ".bmp", ".tif", ".tiff", ".mp4", ".mov" },
                    },
                },
                Branding = new Branding
                {
                    HeadlineFonts = { "Klinic Slab", "Rockwell", "Georgia" },
                    BodyFonts = { "Franklin Gothic URW", "Franklin Gothic Book", "Segoe UI" },
                },
                Documents = new List<DocumentTemplate>
                {
                    new DocumentTemplate { Name = "New field notes", Category = "notes", Template = @"templates\Field Notes.docx" },
                    new DocumentTemplate { Name = "New as-built notes", Category = "asbuilt", Template = @"templates\As-Built Notes.docx" },
                },
            };
        }
    }
}
