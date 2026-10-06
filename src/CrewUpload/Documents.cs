using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CrewUpload
{
    /// <summary>
    /// Starts a new field document -- a lineout, a field notes sheet, a cut sheet -- in the
    /// project, named and filed exactly as an upload of that type would be.
    /// </summary>
    public sealed class DocumentMaker
    {
        private readonly JobFolderConfig _config;
        private readonly UploadPlanner _planner;

        public DocumentMaker(JobFolderConfig config)
        {
            _config = config;
            _planner = new UploadPlanner(config);
        }

        /// <summary>The template file for a document, or null when none is set or it cannot be found.</summary>
        public string TemplatePath(DocumentTemplate document) => document == null ? null : _config.ResolveFile(document.Template);

        /// <summary>
        /// Makes the document and returns its path. A copy of the template when there is one;
        /// otherwise a text sheet headed with the project, date and crew, followed by
        /// <paramref name="body"/> (what the crew typed, for typed field notes).
        /// </summary>
        public string Create(ProjectFolder project, DocumentTemplate document, string crew, DateTime date, string body = null)
        {
            var category = _config.Category(document.Category);
            if (category == null) throw new InvalidOperationException("'" + document.Name + "' uses category '" + document.Category + "', which is not in job-folders.json.");
            return Create(project, category, crew, date, TemplatePath(document), document.Name, body);
        }

        /// <summary>Typed field notes, saved as a text file in the field notes folder.</summary>
        public string CreateNotes(ProjectFolder project, UploadCategory category, string crew, DateTime date, string body) =>
            Create(project, category, crew, date, null, category.Name, body);

        private string Create(ProjectFolder project, UploadCategory category, string crew, DateTime date, string template, string title, string body)
        {
            var values = _planner.Values(project, category, crew, date.Date, title);
            var folder = Naming.Combine(project.Path, category.Folder, values);
            var ext = template != null ? Path.GetExtension(template) : ".txt";
            var path = _planner.NextFreeName(folder, _planner.PatternFor(category), values, ext, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            Directory.CreateDirectory(folder);

            if (template != null)
            {
                File.Copy(template, path, false);
                return path;
            }

            var sb = new StringBuilder();
            sb.AppendLine((title ?? category.Name).ToUpperInvariant());
            sb.AppendLine("Project: " + project.Display);
            if (!string.IsNullOrWhiteSpace(project.Info.Client)) sb.AppendLine("Client:  " + project.Info.Client);
            sb.AppendLine("Date:    " + date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
            sb.AppendLine("Crew:    " + (crew ?? string.Empty).Trim().ToUpperInvariant());
            sb.AppendLine(new string('-', 60));
            if (!string.IsNullOrEmpty(body)) sb.AppendLine(body.TrimEnd());
            File.WriteAllText(path, sb.ToString());
            return path;
        }
    }
}
