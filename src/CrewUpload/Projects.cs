using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Newtonsoft.Json;

namespace CrewUpload
{
    /// <summary>What is known about a project: its number, and the names around its Survey folder.</summary>
    public sealed class ProjectInfo
    {
        /// <summary>Client-task as crews write it: 1800-119. The number in every upload's name.</summary>
        public string ProjectNumber { get; set; }

        /// <summary>The project folder's name above the Survey folder: "554-1800-119 TDLE Phase 3".</summary>
        public string ProjectName { get; set; }

        /// <summary>The client folder above that: "1800-HDR".</summary>
        public string Client { get; set; }
    }

    /// <summary>A registered project, ready for uploads.</summary>
    public sealed class ProjectFolder
    {
        /// <summary>The project's base Survey folder, as the PM registered it (UNC).</summary>
        public string Path { get; set; }

        /// <summary>[Survey]\02Field\01FLD_DR_FN_DCfile\Unprocessed: the only place uploads are written.</summary>
        public string UploadRoot { get; set; }

        public ProjectInfo Info { get; set; }

        public string Display => string.IsNullOrWhiteSpace(Info.ProjectName) ? Info.ProjectNumber : Info.ProjectName;
    }

    /// <summary>An earlier Survey folder a project was registered to, kept when the PM changes it.</summary>
    public sealed class PreviousLocation
    {
        [JsonProperty("surveyFolder")] public string SurveyFolder { get; set; }
        [JsonProperty("registeredBy")] public string RegisteredBy { get; set; }
        [JsonProperty("registeredOn")] public DateTime RegisteredOn { get; set; }
        [JsonProperty("replacedBy")] public string ReplacedBy { get; set; }
        [JsonProperty("replacedOn")] public DateTime ReplacedOn { get; set; }
    }

    /// <summary>One project's registration: client and task, and the base Survey folder the PM picked.</summary>
    public sealed class ProjectRegistration
    {
        [JsonProperty("client")] public string Client { get; set; }
        [JsonProperty("task")] public string Task { get; set; }

        /// <summary>
        /// The base Survey folder, always UNC:
        /// \\parametrix.com\pmx\PSO\Projects\Clients\1800-HDR\554-1800-119 TDLE Phase 3\99Svcs\Survey
        /// </summary>
        [JsonProperty("surveyFolder")] public string SurveyFolder { get; set; }

        [JsonProperty("registeredBy")] public string RegisteredBy { get; set; }
        [JsonProperty("registeredOn")] public DateTime RegisteredOn { get; set; }
        [JsonProperty("history")] public List<PreviousLocation> History { get; set; } = new List<PreviousLocation>();

        [JsonIgnore] public string ProjectNumber => Client + "-" + Task;
    }

    /// <summary>
    /// The list of projects open for crew uploads, kept in one JSON file on the share so every PC sees
    /// the same thing. A PM registers a project once by picking its base Survey folder, and may change
    /// it later; the old location is kept in the entry's history. Crews only read it.
    /// </summary>
    public sealed class ProjectRegistry
    {
        private readonly string _path;
        private readonly bool _requireUnc;

        public ProjectRegistry(string path, bool requireUnc = true)
        {
            _path = path;
            _requireUnc = requireUnc;
        }

        public string FilePath => _path;

        private sealed class Document
        {
            [JsonProperty("version")] public string Version { get; set; } = "1";
            [JsonProperty("projects")] public List<ProjectRegistration> Projects { get; set; } = new List<ProjectRegistration>();
        }

        public static bool IsUnc(string path) =>
            !string.IsNullOrEmpty(path) && (path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal));

        /// <summary>The registration for client-task <paramref name="projectNumber"/>, or null when the PM has not set it up.</summary>
        public ProjectRegistration Find(string projectNumber)
        {
            string client, task;
            if (!JobFolderConfig.SplitProjectNumber(projectNumber, out client, out task)) return null;
            return Read().Projects.FirstOrDefault(p => Same(p.Client, client) && Same(p.Task, task));
        }

        public IReadOnlyList<ProjectRegistration> All() => Read().Projects.OrderBy(p => p.ProjectNumber, StringComparer.OrdinalIgnoreCase).ToList();

        /// <summary>
        /// Registers client-task to a base Survey folder, or moves an existing registration there,
        /// keeping the old one in its history. The folder must exist and, on the share, be a UNC path
        /// (callers turn mapped-drive picks into UNC first). Nothing is created.
        /// </summary>
        public ProjectRegistration Register(string projectNumber, string surveyFolder, string user)
        {
            string client, task;
            if (!JobFolderConfig.SplitProjectNumber(projectNumber, out client, out task))
                throw new ArgumentException("'" + projectNumber + "' is not a client-task number like 1800-119.");
            var folder = (surveyFolder ?? string.Empty).Trim().TrimEnd('\\', '/');
            if (_requireUnc && !IsUnc(folder))
                throw new ArgumentException(folder + " is not a network (UNC) path. Pick the folder through \\\\parametrix.com\\... rather than a drive letter.");
            if (!Directory.Exists(folder))
                throw new DirectoryNotFoundException("The folder " + folder + " does not exist. Register the project after its folders have been created.");

            ProjectRegistration entry = null;
            Update(doc =>
            {
                entry = doc.Projects.FirstOrDefault(p => Same(p.Client, client) && Same(p.Task, task));
                var now = DateTime.Now;
                if (entry == null)
                {
                    entry = new ProjectRegistration { Client = client, Task = task };
                    doc.Projects.Add(entry);
                }
                else if (!Same(entry.SurveyFolder, folder))
                {
                    entry.History.Add(new PreviousLocation
                    {
                        SurveyFolder = entry.SurveyFolder, RegisteredBy = entry.RegisteredBy, RegisteredOn = entry.RegisteredOn,
                        ReplacedBy = user, ReplacedOn = now,
                    });
                }
                else
                {
                    return; // already registered there
                }
                entry.SurveyFolder = folder;
                entry.RegisteredBy = user;
                entry.RegisteredOn = now;
            });
            return entry;
        }

        private static bool Same(string a, string b) => string.Equals((a ?? string.Empty).TrimEnd('\\', '/'), (b ?? string.Empty).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

        private Document Read()
        {
            if (string.IsNullOrWhiteSpace(_path) || !File.Exists(_path)) return new Document();
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    using (var s = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read))
                    using (var r = new StreamReader(s, Encoding.UTF8))
                        return JsonConvert.DeserializeObject<Document>(r.ReadToEnd()) ?? new Document();
                }
                catch (IOException) when (attempt < 10)
                {
                    Thread.Sleep(200); // a PM is saving it right now
                }
            }
        }

        /// <summary>Read, change and write the file under an exclusive lock, so two PMs saving at once cannot lose an entry.</summary>
        private void Update(Action<Document> change)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path)));
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    using (var s = new FileStream(_path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                    {
                        var text = new StreamReader(s, Encoding.UTF8).ReadToEnd();
                        var doc = (text.Length == 0 ? null : JsonConvert.DeserializeObject<Document>(text)) ?? new Document();
                        change(doc);
                        var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(doc, Formatting.Indented));
                        s.SetLength(0);
                        s.Write(bytes, 0, bytes.Length);
                        return;
                    }
                }
                catch (IOException) when (attempt < 10)
                {
                    Thread.Sleep(200);
                }
            }
        }
    }

    /// <summary>
    /// Turns a registered client-task into the place its uploads go. The company folder structure is
    /// never searched or guessed: a project is open for uploads only once a PM has registered it.
    /// </summary>
    public sealed class ProjectStore
    {
        private readonly JobFolderConfig _config;
        private readonly ProjectRegistry _registry;

        public ProjectStore(JobFolderConfig config, ProjectRegistry registry)
        {
            _config = config;
            _registry = registry;
        }

        public ProjectRegistry Registry => _registry;

        /// <summary>The registered project for client-task <paramref name="projectNumber"/>, or null when it is not registered.</summary>
        public ProjectFolder Find(string projectNumber)
        {
            var reg = _registry.Find(projectNumber);
            return reg == null ? null : FromRegistration(reg);
        }

        public ProjectFolder FromRegistration(ProjectRegistration reg)
        {
            var number = reg.ProjectNumber.ToUpperInvariant();
            var survey = reg.SurveyFolder;
            // Name the project after the folder above the Survey folder that carries its number.
            string project = null, client = null;
            var dir = survey;
            while (!string.IsNullOrEmpty(dir))
            {
                var name = Path.GetFileName(dir.TrimEnd('\\', '/'));
                if (LooksLike(name, number))
                {
                    project = name;
                    client = Path.GetFileName(Path.GetDirectoryName(dir.TrimEnd('\\', '/')) ?? string.Empty);
                    break;
                }
                dir = Path.GetDirectoryName(dir.TrimEnd('\\', '/'));
            }
            return new ProjectFolder
            {
                Path = survey,
                UploadRoot = Naming.Combine(survey, _config.UnprocessedFolder, new Dictionary<string, string>()),
                Info = new ProjectInfo { ProjectNumber = number, ProjectName = project ?? number, Client = client },
            };
        }

        /// <summary>True when the folder name looks like this project: "1800-119 ..." or "554-1800-119 ...".</summary>
        public static bool LooksLike(string folderName, string projectNumber)
        {
            var number = JobFolderConfig.NormalizeProjectNumber(projectNumber);
            return Regex.IsMatch(folderName ?? string.Empty, @"^(?:[0-9A-Za-z]+-)?" + Regex.Escape(number) + @"($|[\s_-])", RegexOptions.IgnoreCase);
        }
    }
}
