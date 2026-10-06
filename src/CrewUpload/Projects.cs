using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace CrewUpload
{
    /// <summary>What the PM typed when the project was made. Kept as project.json in its folder.</summary>
    public sealed class ProjectInfo
    {
        public const string FileName = "project.json";

        [JsonProperty("projectNumber")] public string ProjectNumber { get; set; }
        [JsonProperty("projectName")] public string ProjectName { get; set; }
        [JsonProperty("client")] public string Client { get; set; }
        [JsonProperty("projectManager")] public string ProjectManager { get; set; }
        [JsonProperty("createdBy")] public string CreatedBy { get; set; }
        [JsonProperty("created")] public DateTime? Created { get; set; }
    }

    /// <summary>A project folder that exists on disk.</summary>
    public sealed class ProjectFolder
    {
        public string Path { get; set; }
        public ProjectInfo Info { get; set; }

        /// <summary>False for a folder that predates the tool: the info was guessed from its name.</summary>
        public bool HasInfoFile { get; set; }

        public string Display => Info.ProjectNumber + (string.IsNullOrWhiteSpace(Info.ProjectName) ? string.Empty : " - " + Info.ProjectName);
    }

    /// <summary>Finds project folders by number, and makes new ones to the office layout.</summary>
    public sealed class ProjectStore
    {
        private readonly JobFolderConfig _config;

        public ProjectStore(JobFolderConfig config)
        {
            _config = config;
        }

        /// <summary>
        /// The folder for a project number: named exactly the number, or the number followed
        /// by a space, '-' or '_' and a name ("2169171001 Silver Lake"). Null when there is none.
        /// Two candidates is an error, never a guess -- uploading into the wrong job is worse
        /// than not uploading.
        /// </summary>
        public ProjectFolder Find(string projectNumber)
        {
            var number = JobFolderConfig.NormalizeProjectNumber(projectNumber);
            if (number.Length == 0 || string.IsNullOrWhiteSpace(_config.JobsRoot) || !Directory.Exists(_config.JobsRoot)) return null;

            var matches = Candidates(_config.JobsRoot, number, _config.SearchDepth).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (matches.Count == 0) return null;
            if (matches.Count > 1)
                throw new InvalidOperationException("More than one folder is project " + number + ":" + Environment.NewLine
                    + string.Join(Environment.NewLine, matches) + Environment.NewLine + "Ask the PM which one is current.");
            return Open(matches[0], number);
        }

        private static IEnumerable<string> Candidates(string dir, string number, int depth)
        {
            string[] children;
            try { children = Directory.GetDirectories(dir); }
            catch (UnauthorizedAccessException) { yield break; }
            catch (IOException) { yield break; }

            foreach (var child in children)
            {
                var name = Path.GetFileName(child);
                if (IsProjectFolderName(name, number))
                {
                    yield return child;
                    continue; // never look inside a project for another project
                }
                if (depth > 1)
                    foreach (var deeper in Candidates(child, number, depth - 1))
                        yield return deeper;
            }
        }

        internal static bool IsProjectFolderName(string folderName, string number)
        {
            if (!folderName.StartsWith(number, StringComparison.OrdinalIgnoreCase)) return false;
            if (folderName.Length == number.Length) return true;
            var next = folderName[number.Length];
            return next == ' ' || next == '-' || next == '_';
        }

        private static ProjectFolder Open(string path, string number)
        {
            var file = Path.Combine(path, ProjectInfo.FileName);
            if (File.Exists(file))
            {
                try
                {
                    var info = JsonConvert.DeserializeObject<ProjectInfo>(File.ReadAllText(file));
                    if (info != null)
                    {
                        if (string.IsNullOrWhiteSpace(info.ProjectNumber)) info.ProjectNumber = number;
                        return new ProjectFolder { Path = path, Info = info, HasInfoFile = true };
                    }
                }
                catch (JsonException)
                {
                    // A hand-edited project.json that no longer parses: fall back to the folder name.
                }
            }

            var name = Path.GetFileName(path).Substring(number.Length).Trim(' ', '-', '_');
            return new ProjectFolder { Path = path, Info = new ProjectInfo { ProjectNumber = number, ProjectName = name }, HasInfoFile = false };
        }

        /// <summary>
        /// Makes the project folder, the office's standard folders and the downloads folder,
        /// then writes project.json. Refuses a number that already
        /// has a folder.
        /// </summary>
        public ProjectFolder Create(ProjectInfo info)
        {
            if (info == null) throw new ArgumentNullException(nameof(info));
            var number = JobFolderConfig.NormalizeProjectNumber(info.ProjectNumber);
            if (!_config.IsValidProjectNumber(number))
                throw new ArgumentException("'" + info.ProjectNumber + "' is not a project number this office uses.");
            if (string.IsNullOrWhiteSpace(_config.JobsRoot) || !Directory.Exists(_config.JobsRoot))
                throw new DirectoryNotFoundException("The jobs folder '" + _config.JobsRoot + "' cannot be reached. Is the drive connected?");
            if (Find(number) != null)
                throw new InvalidOperationException("Project " + number + " already has a folder.");

            info.ProjectNumber = number;
            info.ProjectName = (info.ProjectName ?? string.Empty).Trim();
            info.Client = (info.Client ?? string.Empty).Trim();
            if (info.Created == null) info.Created = DateTime.Now;

            var values = new Dictionary<string, string>
            {
                { "projectNumber", number },
                { "projectName", info.ProjectName },
                { "client", info.Client },
                { "year", info.Created.Value.Year.ToString(CultureInfo.InvariantCulture) },
            };
            var parent = Naming.Combine(_config.JobsRoot, _config.NewProjectParent, values);
            var folderName = Naming.Clean(Naming.Fill(_config.ProjectFolderName, values));
            var path = Path.Combine(parent, folderName);

            Directory.CreateDirectory(path);
            foreach (var f in _config.ProjectFolders)
                Directory.CreateDirectory(Naming.Combine(path, f, values));
            Directory.CreateDirectory(Naming.Combine(path, Naming.StaticPart(_config.DownloadsFolder), values));

            File.WriteAllText(Path.Combine(path, ProjectInfo.FileName), JsonConvert.SerializeObject(info, Formatting.Indented));
            return new ProjectFolder { Path = path, Info = info, HasInfoFile = true };
        }
    }
}
