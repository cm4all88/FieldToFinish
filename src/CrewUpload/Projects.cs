using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace CrewUpload
{
    /// <summary>What is known about a project, read from its folder's name.</summary>
    public sealed class ProjectInfo
    {
        /// <summary>Client-task as crews write it: 1800-119. The number in every upload's name.</summary>
        public string ProjectNumber { get; set; }

        /// <summary>The rest of the folder name: "TDLE Phase 3".</summary>
        public string ProjectName { get; set; }

        /// <summary>The client folder it sits in: "1800-SoundTransit".</summary>
        public string Client { get; set; }
    }

    /// <summary>A project folder that exists on the share.</summary>
    public sealed class ProjectFolder
    {
        public string Path { get; set; }
        public ProjectInfo Info { get; set; }

        /// <summary>The folder's own name: "554-1800-119 TDLE Phase 3".</summary>
        public string FolderName => System.IO.Path.GetFileName(Path);

        public string Display => FolderName;
    }

    /// <summary>
    /// Finds a project folder on the Parametrix share:
    /// [projectsRoot]\[client folder]\[project folder], for example
    /// \\parametrix.com\pmx\PSO\Projects\Clients\1800-SoundTransit\554-1800-119 TDLE Phase 3.
    /// Never creates one: a project that cannot be found is the user's to point to.
    /// </summary>
    public sealed class ProjectStore
    {
        private readonly JobFolderConfig _config;

        public ProjectStore(JobFolderConfig config)
        {
            _config = config;
        }

        /// <summary>
        /// Every project folder that is client-task <paramref name="projectNumber"/>: under a client
        /// folder named for the client ("1800-..." or "1800"), a folder named for client-task, with or
        /// without a leading prefix ("1800-119 ..." or "554-1800-119 ..."). The caller uses the answer
        /// only when there is exactly one; zero or several go to the user to choose, never a guess.
        /// </summary>
        public List<ProjectFolder> Resolve(string projectNumber)
        {
            var number = JobFolderConfig.NormalizeProjectNumber(projectNumber);
            var found = new List<ProjectFolder>();
            string client, task;
            if (!JobFolderConfig.SplitProjectNumber(number, out client, out task)) return found;
            if (string.IsNullOrWhiteSpace(_config.JobsRoot) || !Directory.Exists(_config.JobsRoot)) return found;

            foreach (var clientDir in Children(_config.JobsRoot).Where(d => IsClientFolderName(System.IO.Path.GetFileName(d), client)))
                foreach (var projectDir in Children(clientDir).Where(d => IsProjectFolderName(System.IO.Path.GetFileName(d), number)))
                    found.Add(Open(projectDir, number));
            return found;
        }

        /// <summary>
        /// A project folder the user pointed to, read as project <paramref name="projectNumber"/>.
        /// It has to exist; nothing is created.
        /// </summary>
        public ProjectFolder Open(string path, string projectNumber)
        {
            if (!Directory.Exists(path)) throw new DirectoryNotFoundException("The folder " + path + " does not exist.");
            var number = JobFolderConfig.NormalizeProjectNumber(projectNumber);
            var name = System.IO.Path.GetFileName(path.TrimEnd('\\', '/'));
            var m = ProjectName(number).Match(name);
            return new ProjectFolder
            {
                Path = path,
                Info = new ProjectInfo
                {
                    ProjectNumber = number,
                    ProjectName = m.Success ? m.Groups["name"].Value.Trim(' ', '-', '_') : name,
                    Client = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(path.TrimEnd('\\', '/'))),
                },
            };
        }

        /// <summary>True when the folder name looks like it is this project, for checking a folder the user picked.</summary>
        public static bool LooksLike(string folderName, string projectNumber) =>
            IsProjectFolderName(folderName ?? string.Empty, JobFolderConfig.NormalizeProjectNumber(projectNumber));

        internal static bool IsClientFolderName(string folderName, string client) =>
            Regex.IsMatch(folderName, "^" + Regex.Escape(client) + @"($|[\s_-])", RegexOptions.IgnoreCase);

        internal static bool IsProjectFolderName(string folderName, string number) => ProjectName(number).IsMatch(folderName);

        private static Regex ProjectName(string number) =>
            new Regex(@"^(?:[0-9A-Za-z]+-)?" + Regex.Escape(number) + @"(?<name>$|[\s_-].*)", RegexOptions.IgnoreCase);

        private static IEnumerable<string> Children(string dir)
        {
            try { return Directory.GetDirectories(dir).OrderBy(d => d, StringComparer.OrdinalIgnoreCase).ToList(); }
            catch (UnauthorizedAccessException) { return Enumerable.Empty<string>(); }
            catch (IOException) { return Enumerable.Empty<string>(); }
        }
    }
}
