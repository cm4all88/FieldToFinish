using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace CrewUpload
{
    /// <summary>
    /// One kind of thing a crew brings back: photos, field notes, a lineout, raw data.
    /// Decides which job subfolder a file lands in and the code in its name.
    /// </summary>
    public sealed class UploadCategory
    {
        /// <summary>Stable id used by documents and the upload log. Never shown.</summary>
        [JsonProperty("key")] public string Key { get; set; }

        /// <summary>What the crew sees in the Type column.</summary>
        [JsonProperty("name")] public string Name { get; set; }

        /// <summary>Goes into the file name through {code}: SV-2169171001-PHOTO-...</summary>
        [JsonProperty("code")] public string Code { get; set; }

        /// <summary>Path under the project folder. May use {date}, {year}, {crew}.</summary>
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

        /// <summary>
        /// Date the file by when it was last modified rather than the upload date. For
        /// photos: a crew uploading Friday's pictures on Monday still files them under Friday.
        /// </summary>
        [JsonProperty("useFileDate")] public bool UseFileDate { get; set; }

        /// <summary>The category used when nothing else matches. Exactly one should be set.</summary>
        [JsonProperty("fallback")] public bool Fallback { get; set; }

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

    /// <summary>How the upload window looks: the company's logo, name and colours.</summary>
    public sealed class Branding
    {
        [JsonProperty("companyName")] public string CompanyName { get; set; } = "Parametrix";

        [JsonProperty("appTitle")] public string AppTitle { get; set; } = "Crew Upload";

        /// <summary>PNG shown in the header. Relative paths are relative to the config file. Without it the company name is drawn as a wordmark.</summary>
        [JsonProperty("logo")] public string Logo { get; set; } = @"branding\parametrix-logo.png";

        /// <summary>Header band and primary buttons, "#RRGGBB".</summary>
        [JsonProperty("primaryColor")] public string PrimaryColor { get; set; } = "#00395D";

        /// <summary>Highlights: the drop box a file is being dragged over, counts, "#RRGGBB".</summary>
        [JsonProperty("accentColor")] public string AccentColor { get; set; } = "#78BE20";

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

        /// <summary>
        /// Name of an uploaded file, without extension. {projectNumber}, {code}, {date}, {seq},
        /// {crew}, {original}, {projectName}, {client}, {year}.
        /// </summary>
        [JsonProperty("fileName")] public string FileName { get; set; } = "SV-{projectNumber}-{code}-{date}-{seq}";

        [JsonProperty("dateFormat")] public string DateFormat { get; set; } = "yyyyMMdd";

        /// <summary>Digits in {seq}: 2 gives -01, -02 ...</summary>
        [JsonProperty("sequenceDigits")] public int SequenceDigits { get; set; } = 2;

        /// <summary>Folders every new project gets, under the project folder.</summary>
        [JsonProperty("projectFolders")] public List<string> ProjectFolders { get; set; } = new List<string>();

        [JsonProperty("categories")] public List<UploadCategory> Categories { get; set; } = new List<UploadCategory>();

        [JsonProperty("documents")] public List<DocumentTemplate> Documents { get; set; } = new List<DocumentTemplate>();

        /// <summary>CSV under the project folder recording every upload: who, when, from where, to where.</summary>
        [JsonProperty("logFile")] public string LogFile { get; set; } = @"Survey\Field\upload-log.csv";

        [JsonProperty("branding")] public Branding Branding { get; set; } = new Branding();

        /// <summary>Folder the config was read from; relative template paths resolve against it.</summary>
        [JsonIgnore] public string BaseDirectory { get; set; }

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
            if (string.IsNullOrWhiteSpace(ProjectFolderName) || ProjectFolderName.IndexOf("{projectNumber}", StringComparison.Ordinal) < 0)
                problems.Add("projectFolderName must contain {projectNumber}, or projects cannot be found again.");
            try { Regex.IsMatch(string.Empty, ProjectNumberPattern ?? string.Empty); }
            catch (ArgumentException e) { problems.Add("projectNumberPattern is not a valid pattern: " + e.Message); }
            try { DateTime.Today.ToString(DateFormat); }
            catch (FormatException) { problems.Add("dateFormat '" + DateFormat + "' is not a date format."); }

            if (Branding == null) Branding = new Branding();
            if (!Branding.IsColor(Branding.PrimaryColor)) problems.Add("branding.primaryColor must be a colour like #00395D.");
            if (!Branding.IsColor(Branding.AccentColor)) problems.Add("branding.accentColor must be a colour like #78BE20.");

            if (Categories.Count == 0) problems.Add("No categories: nothing would know where a file goes.");
            foreach (var g in Categories.GroupBy(c => (c.Key ?? string.Empty).ToLowerInvariant()).Where(g => g.Count() > 1))
                problems.Add("Category key '" + g.Key + "' is used more than once.");
            foreach (var c in Categories)
            {
                var label = "Category '" + (c.Name ?? c.Key) + "'";
                if (string.IsNullOrWhiteSpace(c.Key)) problems.Add(label + " has no key.");
                if (string.IsNullOrWhiteSpace(c.Name)) problems.Add("Category '" + c.Key + "' has no name.");
                if (string.IsNullOrWhiteSpace(c.Folder)) problems.Add(label + " has no folder.");
                else if (Path.IsPathRooted(c.Folder) || Naming.Segments(c.Folder).Any(s => s == ".."))
                    problems.Add(label + " folder must stay inside the project folder.");
                if (string.IsNullOrWhiteSpace(c.Code)) problems.Add(label + " has no code.");
                else if (c.Code.IndexOfAny(Naming.WindowsInvalid) >= 0) problems.Add(label + " code has characters a file name cannot hold.");
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
                Categories = new List<UploadCategory>
                {
                    new UploadCategory
                    {
                        Key = "lineout", Name = "Field lineout", Code = "LINEOUT", Folder = @"Survey\Field\Lineouts",
                        Keywords = { "lineout", "line out", "line-out", "lineouts" },
                    },
                    new UploadCategory
                    {
                        Key = "stakeout", Name = "Stakeout / cut sheet", Code = "STAKE", Folder = @"Survey\Field\Stakeout",
                        Keywords = { "stakeout", "stake out", "staking", "cut sheet", "cutsheet", "cut sheets", "cutsheets" },
                    },
                    new UploadCategory
                    {
                        Key = "control", Name = "Control", Code = "CTRL", Folder = @"Survey\Control",
                        Keywords = { "control", "ctrl", "opus", "static" },
                    },
                    new UploadCategory
                    {
                        Key = "notes", Name = "Field notes", Code = "FN", Folder = @"Survey\Field\Field Notes",
                        Keywords = { "field notes", "field note", "fieldnotes", "notes", "note", "fieldbook", "field book", "fn" },
                        Extensions = { ".txt", ".rtf", ".doc", ".docx" },
                    },
                    new UploadCategory
                    {
                        Key = "photos", Name = "Photos", Code = "PHOTO", Folder = @"Survey\Field\Photos\{date}", UseFileDate = true,
                        Keywords = { "photo", "photos", "pics", "pictures", "picture" },
                        Extensions = { ".jpg", ".jpeg", ".png", ".heic", ".heif", ".gif", ".bmp", ".tif", ".tiff", ".mp4", ".mov" },
                    },
                    new UploadCategory
                    {
                        // Data collector files keep their own name in the new one: the job file
                        // name is what the crew and the office both search for.
                        Key = "rawdata", Name = "Raw data", Code = "RAW", Folder = @"Survey\Field\Raw Data\{date}",
                        FileName = "SV-{projectNumber}-{code}-{date}-{original}",
                        Keywords = { "raw", "raw data", "rawdata" },
                        Extensions = { ".job", ".jxl", ".raw", ".rw5", ".dc", ".t01", ".t02", ".t04", ".dat", ".fbk", ".gsi", ".sdr", ".crd", ".csv", ".pnt", ".tsj", ".jbk" },
                    },
                    new UploadCategory
                    {
                        Key = "other", Name = "Other field document", Code = "MISC", Folder = @"Survey\Field\Other", Fallback = true,
                    },
                },
                Documents = new List<DocumentTemplate>
                {
                    new DocumentTemplate { Name = "New field lineout", Category = "lineout", Template = @"templates\Field Lineout.xlsx" },
                    new DocumentTemplate { Name = "New field notes", Category = "notes", Template = @"templates\Field Notes.docx" },
                    new DocumentTemplate { Name = "New cut sheet", Category = "stakeout", Template = @"templates\Cut Sheet.xlsx" },
                },
            };
        }
    }
}
