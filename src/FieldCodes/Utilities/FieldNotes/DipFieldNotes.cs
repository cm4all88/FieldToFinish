using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FieldCodes.Settings;

namespace FieldCodes.Utilities.FieldNotes
{
    /// <summary>How one save behaves.</summary>
    public sealed class FieldNotesSaveOptions
    {
        /// <summary>Replace the newest file of the same name instead of writing the next revision. Off by default:
        /// an issued record is never overwritten unless this is chosen.</summary>
        public bool Overwrite { get; set; }

        /// <summary>Do nothing when the newest file of the same name already says exactly this (automatic saves).</summary>
        public bool SkipIfUnchanged { get; set; }

        public bool Sketches { get; set; }

        /// <summary>Flate-compress page contents. Tests turn it off to read the operators directly.</summary>
        public bool Compress { get; set; }

        public DateTimeOffset Issued { get; set; }

        public FieldNotesSaveOptions()
        {
            Sketches = true;
            Compress = true;
            Issued = DateTimeOffset.Now;
        }
    }

    public enum FieldNotesOutcome { Written, Replaced, Unchanged, NothingToSave, Failed }

    public sealed class FieldNotesSaveResult
    {
        public FieldNotesOutcome Outcome { get; set; }
        public string Path { get; set; }
        public int Revision { get; set; }
        public string Supersedes { get; set; }
        public int Pages { get; set; }
        public int Structures { get; set; }
        public int Incomplete { get; set; }
        public string Message { get; set; }
    }

    /// <summary>
    /// Dip field notes from start to finish: the record from the project, the pages from the record, the PDF from the
    /// pages, and the file -- written once, never over an earlier issue unless overwrite was asked for.
    /// </summary>
    public static class DipFieldNotes
    {
        /// <summary>The PDF bytes for a record.</summary>
        public static byte[] Render(FieldNotesReport report, FieldNotesIssue issue, bool sketches, bool compress)
        {
            var document = FieldNotesLayout.Build(report, issue, sketches);
            return PdfWriter.Write(document, Metadata(report, issue, document.Fingerprint()), compress);
        }

        public static PdfMetadata Metadata(FieldNotesReport report, FieldNotesIssue issue, string fingerprint)
        {
            var h = report.Header ?? new FieldNotesHeader();
            var id = h.ProjectIdentifier ?? report.DrawingName;
            return new PdfMetadata
            {
                Title = "Utility Dip Field Notes" + (id != null ? " - " + id : string.Empty) +
                        (h.ObservationDate.HasValue ? " - " + h.ObservationDate.Value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) : string.Empty),
                Subject = "Storm and sanitary structure dips: measured depths and calculated invert elevations",
                Keywords = string.Join(", ", new[] { h.ProjectNumber, h.ProjectName }.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray()),
                Author = string.IsNullOrWhiteSpace(h.Crew) ? null : h.Crew.Trim(),
                Created = issue != null ? issue.Issued : DateTimeOffset.Now,
                ContentHash = fingerprint
            };
        }

        /// <summary>
        /// Writes the record into the folder. The name comes from <see cref="FieldNotesFiles.BaseName"/>; an existing
        /// file of that name is never touched unless options.Overwrite is set -- the new one is the next revision,
        /// created with CreateNew so even a file that appears in the meantime is not overwritten.
        /// </summary>
        public static FieldNotesSaveResult Save(FieldNotesReport report, string folder, string baseName, FieldNotesSaveOptions options)
        {
            options = options ?? new FieldNotesSaveOptions();
            var result = new FieldNotesSaveResult
            {
                Structures = report.Structures.Count,
                Incomplete = report.Incomplete.Count
            };
            if (report.Structures.Count == 0)
            {
                result.Outcome = FieldNotesOutcome.NothingToSave;
                result.Message = report.Incomplete.Count > 0
                    ? "No completed dip observations to save (" + report.Incomplete.Count + " incomplete)."
                    : "No completed dip observations to save.";
                return result;
            }
            if (string.IsNullOrWhiteSpace(folder))
            {
                result.Outcome = FieldNotesOutcome.Failed;
                result.Message = "Save the drawing first: the field notes are saved under its project folder.";
                return result;
            }

            try
            {
                Directory.CreateDirectory(folder);
                var existing = Directory.GetFiles(folder, "*.pdf").Select(System.IO.Path.GetFileName).ToList();
                var target = FieldNotesFiles.Plan(folder, baseName, options.Overwrite, existing);
                var issue = new FieldNotesIssue
                {
                    Issued = options.Issued,
                    Revision = target.Revision,
                    FileName = target.FileName,
                    Supersedes = target.Supersedes
                };
                var document = FieldNotesLayout.Build(report, issue, options.Sketches);
                var fingerprint = document.Fingerprint();
                result.Pages = document.Pages.Count;

                if (options.SkipIfUnchanged && target.Latest != null)
                {
                    var latest = System.IO.Path.Combine(folder, target.Latest);
                    if (FieldNotesFiles.ReadContentHash(File.ReadAllBytes(latest)) == fingerprint)
                    {
                        result.Outcome = FieldNotesOutcome.Unchanged;
                        result.Path = latest;
                        result.Message = "Field notes unchanged since " + target.Latest + "; nothing written.";
                        return result;
                    }
                }

                var bytes = PdfWriter.Write(document, Metadata(report, issue, fingerprint), options.Compress);
                if (target.Replaces)
                {
                    File.WriteAllBytes(target.Path, bytes);
                    result.Outcome = FieldNotesOutcome.Replaced;
                }
                else
                {
                    using (var stream = new FileStream(target.Path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        stream.Write(bytes, 0, bytes.Length);
                    result.Outcome = FieldNotesOutcome.Written;
                }
                result.Path = target.Path;
                result.Revision = target.Revision;
                result.Supersedes = target.Supersedes;
                result.Message = (result.Outcome == FieldNotesOutcome.Replaced ? "Field notes replaced: " : "Field notes saved: ") + target.Path +
                                 (target.Supersedes != null ? " (revision R" + target.Revision + ", supersedes " + target.Supersedes + ")" : string.Empty);
                return result;
            }
            catch (IOException ex)
            {
                return Failed(result, "The field notes could not be written (" + ex.Message + "). If the PDF is open, close it and save again.");
            }
            catch (UnauthorizedAccessException ex)
            {
                return Failed(result, "The field notes folder cannot be written to (" + ex.Message + ").");
            }
        }

        private static FieldNotesSaveResult Failed(FieldNotesSaveResult result, string message)
        {
            result.Outcome = FieldNotesOutcome.Failed;
            result.Message = message;
            return result;
        }

        /// <summary>The structures a save should look at: every structure whose surveyed point no longer matches
        /// the snapshot its elevations were calculated from (UtilityQc.StaleStructures).</summary>
        public static ICollection<string> StaleIds(UtilityProject project, IDictionary<string, CadStructureSnapshot> livePoints)
        {
            if (project == null || livePoints == null) return new HashSet<string>();
            return new HashSet<string>(UtilityQc.StaleStructures(project, livePoints).Select(s => s.Id));
        }

        /// <summary>The structures whose observation is complete, for the default selection.</summary>
        public static IList<StructureRecord> Complete(UtilityProject project, ICollection<string> staleIds)
        {
            if (project == null) return new List<StructureRecord>();
            return project.Structures.Where(s => DipObservationStatus.Check(project, s, staleIds).IsComplete).ToList();
        }

        /// <summary>Settings shorthand used by the CAD layer: sketches on unless the office turned them off.</summary>
        public static FieldNotesSaveOptions OptionsFrom(UtilitySettings settings, bool overwrite, bool automatic)
        {
            settings = settings ?? new UtilitySettings();
            return new FieldNotesSaveOptions
            {
                Overwrite = overwrite,
                SkipIfUnchanged = automatic,
                Sketches = settings.FieldNotesSketches,
                Issued = DateTimeOffset.Now
            };
        }
    }
}
