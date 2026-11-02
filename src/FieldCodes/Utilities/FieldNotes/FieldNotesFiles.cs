using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace FieldCodes.Utilities.FieldNotes
{
    /// <summary>Where the next field-notes PDF goes, and what it replaces or supersedes.</summary>
    public sealed class FieldNotesTarget
    {
        public string Folder { get; set; }
        public string FileName { get; set; }
        public string Path { get { return System.IO.Path.Combine(Folder ?? string.Empty, FileName ?? string.Empty); } }

        /// <summary>0 for the plain name, 1 for _R1...</summary>
        public int Revision { get; set; }

        /// <summary>True when an existing file of this name is replaced (overwrite chosen explicitly).</summary>
        public bool Replaces { get; set; }

        /// <summary>The newest earlier issue of the same record, or null when there is none.</summary>
        public string Latest { get; set; }

        /// <summary>The file this one supersedes, for a new revision.</summary>
        public string Supersedes { get; set; }
    }

    /// <summary>
    /// File names and folders for dip field notes. Pure apart from the directory checks it is handed, so the rules
    /// are tested without a disk: a name is [project]_Utility-Dip-Field-Notes_[yyyy-MM-dd].pdf; an issued file is
    /// never overwritten unless overwrite is chosen -- the next save is _R1, _R2...
    /// </summary>
    public static class FieldNotesFiles
    {
        public const string DefaultFolder = @"Survey\Field Notes\Utility Dips";
        public const string NameStem = "Utility-Dip-Field-Notes";

        /// <summary>
        /// The folder the field notes go in. An absolute setting is used as it is. A relative one (the default
        /// Survey\Field Notes\Utility Dips) is under the project folder, found from the drawing: if the drawing sits
        /// inside a folder named like the setting's first part ("...\26-104\Survey\CAD\base.dwg"), the project is
        /// that folder's parent; otherwise the nearest of the drawing's folder and its two parents that already holds
        /// such a folder ("...\26-104\CAD\base.dwg" beside "...\26-104\Survey"); otherwise the drawing's own folder.
        /// Null when the drawing has never been saved. Either slash separates folders.
        /// </summary>
        public static string ResolveFolder(string drawingPath, string configured, Func<string, bool> directoryExists)
        {
            if (string.IsNullOrWhiteSpace(drawingPath)) return null;
            var setting = string.IsNullOrWhiteSpace(configured) ? DefaultFolder : configured.Trim();
            if (IsAbsolute(setting)) return setting;

            var parts = setting.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries)
                               .Where(p => p != ".").ToArray();
            string drawingFolder;
            try { drawingFolder = Path.GetDirectoryName(drawingPath); }
            catch (ArgumentException) { return null; }
            if (string.IsNullOrEmpty(drawingFolder)) return null;
            if (parts.Length == 0) return drawingFolder;

            var first = parts[0];
            var project = drawingFolder;
            var found = false;

            // Inside it: ...\Survey\CAD\base.dwg -> the project is the folder above Survey.
            var dir = drawingFolder;
            for (var level = 0; level < 4 && !string.IsNullOrEmpty(dir); level++)
            {
                if (string.Equals(Path.GetFileName(dir), first, StringComparison.OrdinalIgnoreCase))
                {
                    var parent = Path.GetDirectoryName(dir);
                    if (!string.IsNullOrEmpty(parent)) { project = parent; found = true; }
                    break;
                }
                dir = Path.GetDirectoryName(dir);
            }

            // Beside it: ...\CAD\base.dwg next to ...\Survey.
            if (!found && directoryExists != null)
            {
                dir = drawingFolder;
                for (var level = 0; level < 3 && !string.IsNullOrEmpty(dir); level++)
                {
                    bool exists;
                    try { exists = directoryExists(Path.Combine(dir, first)); }
                    catch (IOException) { exists = false; }
                    catch (UnauthorizedAccessException) { exists = false; }
                    if (exists) { project = dir; break; }
                    dir = Path.GetDirectoryName(dir);
                }
            }

            var result = project;
            foreach (var p in parts) result = Path.Combine(result, p);
            return result;
        }

        private static bool IsAbsolute(string path)
        {
            if (path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal)) return true;
            if (path.Length >= 3 && char.IsLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/')) return true;
            try { return Path.IsPathRooted(path) && !path.StartsWith(@"\", StringComparison.Ordinal); }
            catch (ArgumentException) { return false; }
        }

        /// <summary>
        /// The file name without revision or extension: the project (number, else name, else the drawing's name),
        /// the record, and the date of observation ("undated" when none was recorded).
        /// 26-104_Utility-Dip-Field-Notes_2026-10-28.
        /// </summary>
        public static string BaseName(FieldNotesHeader header, string drawingPath)
        {
            var id = header != null ? header.ProjectIdentifier : null;
            if (id == null && !string.IsNullOrWhiteSpace(drawingPath))
            {
                try { id = Path.GetFileNameWithoutExtension(drawingPath); }
                catch (ArgumentException) { id = null; }
            }
            if (string.IsNullOrWhiteSpace(id)) id = "Dip-Project";
            var date = header != null && header.ObservationDate.HasValue
                ? header.ObservationDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : "undated";
            return Sanitize(id) + "_" + NameStem + "_" + date;
        }

        /// <summary>
        /// Text made safe as part of a file name on Windows: characters Windows refuses become '-', runs of spaces
        /// become one '-', and trailing dots and spaces go. The same on every platform.
        /// </summary>
        public static string Sanitize(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "Dip-Project";
            var sb = new StringBuilder();
            foreach (var ch in text.Trim())
            {
                if (ch < 32 || "<>:\"/\\|?*".IndexOf(ch) >= 0) sb.Append('-');
                else if (char.IsWhiteSpace(ch)) sb.Append('-');
                else sb.Append(ch);
            }
            var s = Regex.Replace(sb.ToString(), "-{2,}", "-").Trim('-', '.', ' ');
            if (s.Length > 80) s = s.Substring(0, 80).TrimEnd('-', '.', ' ');
            return s.Length == 0 ? "Dip-Project" : s;
        }

        /// <summary>
        /// Picks the file for the next save. Without overwrite: the plain name if nothing of that name exists, else
        /// one revision past the highest one there (_R1, _R2...), so an issued record is never overwritten. With
        /// overwrite: the newest existing file of that name is replaced (or the plain name when there is none).
        /// </summary>
        public static FieldNotesTarget Plan(string folder, string baseName, bool overwrite, IEnumerable<string> existingFileNames)
        {
            var pattern = new Regex("^" + Regex.Escape(baseName) + @"(?:_R(?<rev>\d{1,4}))?\.pdf$",
                                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            var revisions = new SortedDictionary<int, string>();
            foreach (var raw in existingFileNames ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrEmpty(raw)) continue;
                var name = Path.GetFileName(raw);
                var m = pattern.Match(name);
                if (!m.Success) continue;
                var rev = m.Groups["rev"].Success ? int.Parse(m.Groups["rev"].Value, CultureInfo.InvariantCulture) : 0;
                revisions[rev] = name;
            }

            var target = new FieldNotesTarget { Folder = folder };
            if (revisions.Count == 0)
            {
                target.FileName = baseName + ".pdf";
                return target;
            }

            var highest = revisions.Keys.Max();
            target.Latest = revisions[highest];
            if (overwrite)
            {
                target.FileName = revisions[highest];
                target.Revision = highest;
                target.Replaces = true;
                return target;
            }
            target.Revision = highest + 1;
            target.FileName = FileName(baseName, target.Revision);
            target.Supersedes = revisions[highest];
            return target;
        }

        public static string FileName(string baseName, int revision)
        {
            return baseName + (revision > 0 ? "_R" + revision.ToString(CultureInfo.InvariantCulture) : string.Empty) + ".pdf";
        }

        /// <summary>The content fingerprint stored in a PDF this writer made, or null.</summary>
        public static string ReadContentHash(byte[] pdf)
        {
            if (pdf == null || pdf.Length == 0) return null;
            var chars = new char[pdf.Length];
            for (var i = 0; i < pdf.Length; i++) chars[i] = (char)pdf[i];
            var text = new string(chars);
            var m = Regex.Match(text, "/" + PdfWriter.ContentHashKey + @"\s*\((?<h>[0-9a-fA-F]+)\)");
            return m.Success ? m.Groups["h"].Value.ToLowerInvariant() : null;
        }
    }
}
