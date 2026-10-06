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

        /// <summary>Folder that holds every project folder, e.g. a mapped network share.</summary>
        [JsonProperty("jobsRoot")] public string JobsRoot { get; set; }

        /// <summary>
        /// How deep below jobsRoot a project folder may be. 1 = directly inside it;
        /// 2 also finds projects filed by year or client (Jobs\2026\2169171001 ...).
        /// </summary>
        [JsonProperty("searchDepth")] public int SearchDepth { get; set; } = 2;

        /// <summary>Where a new project is created, under jobsRoot. May use {year}. Empty = jobsRoot itself.</summary>
        [JsonProperty("newProjectParent")] public string NewProjectParent { get; set; } = string.Empty;

        /// <summary>A project number has to match this before anything is looked up or created.</summary>
        [JsonProperty("projectNumberPattern")] public string ProjectNumberPattern { get; set; } = @"^[0-9][0-9A-Z.\-]{2,}$";

        /// <summary>Name of a new project folder. {projectNumber}, {projectName}, {client}.</summary>
        [JsonProperty("projectFolderName")] public string ProjectFolderName { get; set; } = "{projectNumber} {projectName}";

        /// <summary>Where download folders go, under the project folder.</summary>
        [JsonProperty("downloadsFolder")] public string DownloadsFolder { get; set; } = @"Survey\Field\Downloads";

        /// <summary>
        /// Name of one crew download: {date}, {crew}, {projectNumber}, {workType}. The same pattern
        /// reads a folder the crew already named, so dropping it fills in the project, crew, date and type.
        /// </summary>
        [JsonProperty("downloadName")] public string DownloadName { get; set; } = "{date}-{crew}-{projectNumber}-{workType}";

        /// <summary>
        /// Name of a file in the download, without extension: {download} (the folder's name),
        /// {code}, {original}, and the download's own tokens. A category's fileName overrides it.
        /// </summary>
        [JsonProperty("fileName")] public string FileName { get; set; } = "{download}-{code}";

        [JsonProperty("dateFormat")] public string DateFormat { get; set; } = "yyyyMMdd";

        /// <summary>
        /// Digits of the number added when a name is taken: 1 gives -2, -3 ...; a pattern with {seq}
        /// numbers every file instead.
        /// </summary>
        [JsonProperty("sequenceDigits")] public int SequenceDigits { get; set; } = 1;

        [JsonProperty("workTypes")] public List<WorkType> WorkTypes { get; set; } = new List<WorkType>();

        /// <summary>Folders every new project gets, under the project folder.</summary>
        [JsonProperty("projectFolders")] public List<string> ProjectFolders { get; set; } = new List<string>();

        [JsonProperty("categories")] public List<UploadCategory> Categories { get; set; } = new List<UploadCategory>();

        [JsonProperty("documents")] public List<DocumentTemplate> Documents { get; set; } = new List<DocumentTemplate>();

        /// <summary>CSV under the project folder recording every upload: who, when, from where, to where.</summary>
        [JsonProperty("logFile")] public string LogFile { get; set; } = @"Survey\Field\upload-log.csv";

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

        [JsonIgnore] public UploadCategory FallbackCategory => Categories.FirstOrDefault(c => c.Fallback) ?? Categories.LastOrDefault();

        /// <summary>Trimmed and upper-cased: crews type "2169171001 " and "554-3744-009a".</summary>
        public static string NormalizeProjectNumber(string number) => (number ?? string.Empty).Trim().ToUpperInvariant();

        public bool IsValidProjectNumber(string number)
        {
            var n = NormalizeProjectNumber(number);
            return n.Length > 0 && n.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && n.IndexOfAny(Naming.WindowsInvalid) < 0
                && Regex.IsMatch(n, ProjectNumberPattern ?? ".+", RegexOptions.IgnoreCase);
        }

        public static JobFolderConfig Load(string path)
        {
            var config = JsonConvert.DeserializeObject<JobFolderConfig>(File.ReadAllText(path)) ?? new JobFolderConfig();
            config.BaseDirectory = Path.GetDirectoryName(Path.GetFullPath(path));
            return config;
        }

        public void Save(string path)
        {
            File.WriteAllText(path, JsonConvert.SerializeObject(this, Formatting.Indented));
        }

        /// <summary>Everything wrong with the config, in words a CAD manager can act on.</summary>
        public void Validate(ICollection<string> problems)
        {
            if (string.IsNullOrWhiteSpace(JobsRoot)) problems.Add("jobsRoot is not set: the folder that holds the project folders.");
            if (SearchDepth < 1 || SearchDepth > 4) problems.Add("searchDepth must be 1 to 4.");
            if (SequenceDigits < 1 || SequenceDigits > 6) problems.Add("sequenceDigits must be 1 to 6.");
            if (string.IsNullOrWhiteSpace(FileName)) problems.Add("fileName is empty.");
            if (string.IsNullOrWhiteSpace(DownloadName) || DownloadName.IndexOf("{projectNumber}", StringComparison.Ordinal) < 0)
                problems.Add("downloadName must contain {projectNumber}.");
            if (Path.IsPathRooted(DownloadsFolder ?? string.Empty) || Naming.Segments(DownloadsFolder).Any(x => x == ".."))
                problems.Add("downloadsFolder must stay inside the project folder.");
            if (WorkTypes.Count == 0) problems.Add("No workTypes: a download needs one (TOPO, LINEOUT ...).");
            foreach (var w in WorkTypes)
                if (string.IsNullOrWhiteSpace(w.Code) || !Regex.IsMatch(w.Code, "^[A-Za-z0-9]+$"))
                    problems.Add("Work type '" + w.Name + "' needs a code of letters and digits only.");
            foreach (var g in WorkTypes.GroupBy(w => (w.Code ?? string.Empty).ToUpperInvariant()).Where(g => g.Count() > 1))
                problems.Add("Work type code '" + g.Key + "' is used more than once.");
            if (string.IsNullOrWhiteSpace(ProjectFolderName) || ProjectFolderName.IndexOf("{projectNumber}", StringComparison.Ordinal) < 0)
                problems.Add("projectFolderName must contain {projectNumber}, or projects cannot be found again.");
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
                JobsRoot = @"U:\PSO\Jobs",
                ProjectFolders = new List<string>
                {
                    @"Admin",
                    @"Survey\CAD",
                    @"Survey\Calcs",
                    @"Survey\Control",
                    @"Survey\Deliverables",
                    @"Survey\Field",
                    @"Survey\Research",
                },
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
                        // Photos keep the camera's names inside the download's Photos folder.
                        Key = "photos", Name = "Photos", Code = "PHOTO", Color = "#FCC214", Folder = "Photos", FileName = "{original}",
                        Keywords = { "photo", "photos", "pics", "pictures", "picture" },
                        Extensions = { ".jpg", ".jpeg", ".png", ".heic", ".heif", ".gif", ".bmp", ".tif", ".tiff", ".mp4", ".mov" },
                    },
                    new UploadCategory
                    {
                        Key = "notes", Name = "Field notes", Code = "FN", Color = "#3FB549", Folder = "",
                        Keywords = { "fn", "field notes", "field note", "fieldnotes", "notes", "fieldbook", "field book" },
                    },
                    new UploadCategory
                    {
                        // 20260128-JAM-1521-799-TOPO.job / .jxl: the data carries the download's own name.
                        Key = "data", Name = "Data files", Code = "", Color = "#0073BB", Folder = "", FileName = "{download}", Fallback = true,
                        Keywords = { "raw", "raw data", "rawdata", "data" },
                        Extensions = { ".job", ".jxl", ".raw", ".rw5", ".dc", ".t01", ".t02", ".t04", ".dat", ".fbk", ".gsi", ".sdr", ".crd", ".csv", ".pnt", ".tsj", ".jbk" },
                    },
                    new UploadCategory
                    {
                        Key = "asbuilt", Name = "As-built notes", Code = "ASB", Color = "#F36E21", Folder = "",
                        Keywords = { "asb", "asbuilt", "as built", "asbuilts", "as builts" },
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
