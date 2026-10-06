using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using Newtonsoft.Json;

namespace CrewUpload.App
{
    internal static class Program
    {
        /// <summary>
        /// CrewUpload.exe [--config path\job-folders.json] [project number] [files or folders...]
        /// Without --config the job-folders.json beside the exe is used. Files and folders on the
        /// command line are added as if dropped, so a shortcut in the Explorer "Send to" menu works.
        /// </summary>
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            string configPath = null;
            string project = null;
            var dropped = new List<string>();
            for (var i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "--config", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) configPath = args[++i];
                else if (File.Exists(args[i]) || Directory.Exists(args[i])) dropped.Add(args[i]);
                else project = args[i];
            }
            configPath = configPath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, JobFolderConfig.DefaultFileName);

            JobFolderConfig config;
            try
            {
                config = JobFolderConfig.Load(configPath);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is JsonException)
            {
                MessageBox.Show("Could not read the job folder standard:\n" + configPath + "\n\n" + e.Message, "Crew Upload", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var problems = new List<string>();
            config.Validate(problems);
            if (problems.Count > 0)
            {
                MessageBox.Show("job-folders.json needs fixing before crews can upload (tell the CAD manager):\n\n- " + string.Join("\n- ", problems)
                    + "\n\n" + configPath, "Crew Upload", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            Application.Run(new MainForm(config, project, dropped));
        }
    }

    /// <summary>What the app remembers between runs, per Windows user.</summary>
    internal sealed class Remembered
    {
        [JsonProperty("crew")] public string Crew { get; set; }
        [JsonProperty("recentProjects")] public List<string> RecentProjects { get; set; } = new List<string>();

        private static string PathOf => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FieldToFinish", "crew-upload.json");

        public static Remembered Load()
        {
            try
            {
                return File.Exists(PathOf) ? JsonConvert.DeserializeObject<Remembered>(File.ReadAllText(PathOf)) ?? new Remembered() : new Remembered();
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is JsonException)
            {
                return new Remembered();
            }
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(PathOf));
                File.WriteAllText(PathOf, JsonConvert.SerializeObject(this, Formatting.Indented));
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                // Remembering the crew's initials is a convenience; never fail over it.
            }
        }

        public void Used(string project)
        {
            RecentProjects.RemoveAll(p => string.Equals(p, project, StringComparison.OrdinalIgnoreCase));
            RecentProjects.Insert(0, project);
            if (RecentProjects.Count > 10) RecentProjects.RemoveRange(10, RecentProjects.Count - 10);
        }
    }
}
