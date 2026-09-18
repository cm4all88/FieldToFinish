using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.DatabaseServices;
using FieldCodes.Reporting;

namespace FieldCodes.Cad
{
    /// <summary>
    /// Point placement.
    ///
    /// UNTESTED: none of this has been run against a drawing. The parsing and
    /// geometry it calls into are unit tested; the AutoCAD calls are not.
    /// </summary>
    public sealed class TreeCommands
    {
        /// <summary>
        /// Reads every CogoPoint, parses its raw description, and places the symbol
        /// block for each one that parses cleanly. Errored points are skipped and
        /// collected into the exception report.
        ///
        /// Safe to run twice: everything this command owns is deleted first.
        /// </summary>
        [CommandMethod("FTFPOINTS", CommandFlags.Modal | CommandFlags.UsePickSet)]
        [CommandMethod("FTFTREES", CommandFlags.Modal | CommandFlags.UsePickSet)]
        public void FtfTrees()
        {
            FtfSession.Run("FTFPOINTS", (db, tr, ed) =>
            {
                var cfg = FtfSession.Rules(db);
                var settings = FtfSession.SettingsFor(db, cfg);
                var parser = new FieldCodeParser(cfg);

                Ownership.EnsureRegApp(db, tr);

                var removed = Ownership.DeleteOwnedKinds(db, tr, FtfEntityKind.Block);
                if (removed > 0)
                    ed.WriteMessage("\nFTFTREES: removed {0} previously placed block(s).", removed);

                var points = CadUtil.CogoPointsInOrder(db, tr);

                var placed = 0;
                var skipped = 0;
                var ignored = 0;
                var recognized = 0;
                var noBlockRule = 0;
                var rotated = 0;
                var rows = new List<ExceptionRow>();
                var unhandled = new FieldCodes.Reporting.UnhandledSummary();

                foreach (var cogo in points)
                {
                    // RawDescription, never DescriptionFormat: description keys may
                    // already have expanded the latter, and the grammar is defined
                    // against what the surveyor actually typed.
                    var raw = cogo.RawDescription;
                    var number = cogo.PointNumber.ToString(CultureInfo.InvariantCulture);

                    var parsed = parser.Parse(number, raw);

                    if (parsed.Ignored || parsed.NoContent) { ignored++; continue; }

                    if (parsed.Unhandled)
                    {
                        unhandled.Add(parsed.Code, number, raw);
                        continue;
                    }

                    if (parsed.HasErrors)
                    {
                        skipped++;
                        rows.Add(RowFor(cogo, parsed, "Error"));
                        continue;
                    }

                    recognized++;

                    if (parsed.HasWarnings)
                        rows.Add(RowFor(cogo, parsed, "Warning"));

                    // Point finishing, not point creation. Civil 3D's description keys
                    // already placed the symbol; rotating it is polish FTF adds.
                    if (parsed.RotationDegrees.HasValue &&
                        RotateMarker(tr, cogo, parsed.RotationDegrees.Value))
                        rotated++;

                    if (!parsed.InsertBlock || string.IsNullOrEmpty(parsed.BlockName))
                    {
                        // The normal case: Civil 3D owns the symbol. Drip lines, labels,
                        // tags and draw order still apply.
                        noBlockRule++;
                        continue;
                    }

                    var blockId = CadUtil.FindBlock(db, tr, parsed.BlockName);
                    if (blockId.IsNull)
                    {
                        skipped++;
                        parsed.Add(Severity.Error, "BLOCK",
                            string.Format("Block '{0}' is not defined in this drawing.",
                                          parsed.BlockName));
                        rows.Add(RowFor(cogo, parsed, "Error"));
                        continue;
                    }

                    PlaceBlock(db, tr, cogo, parsed, blockId, cfg.Version);
                    placed++;
                }

                WriteReport(db, ed, rows, settings);
                WriteUnhandled(db, ed, unhandled);

                // "Recognized", not "drawn": Civil 3D's description keys already placed
                // these symbols. FTF recognized the codes and will finish them -- labels,
                // driplines, tags -- it did not create the points or their symbols.
                ed.WriteMessage(
                    "\nFTFPOINTS: {0} point(s) read -- {1} recognized for FTF finishing, " +
                    "{2} not configured yet, {3} outside FTF scope (linework / bare codes), " +
                    "{4} with errors.",
                    points.Count, recognized, unhandled.TotalPoints, ignored, skipped);

                if (rotated > 0)
                    ed.WriteMessage("\n  {0} marker(s) rotated to match the field azimuth.",
                                    rotated);

                if (noBlockRule > 0)
                    ed.WriteMessage(
                        "\n  {0} point(s) left to their Civil 3D symbol, as normal. " +
                        "Run FTFDRIP, FTFLABELS and FTFTAGS for the rest of the finishing.",
                        noBlockRule);

                if (placed > 0)
                    ed.WriteMessage(
                        "\n  {0} block(s) inserted by rules that opted in with insertBlock.",
                        placed);

                ed.WriteMessage("\n");
            });
        }

        /// <summary>
        /// Reports where the plugin is looking for its rules file and what it found.
        /// Run this first when a command complains about the rules file.
        /// </summary>
        [CommandMethod("FTFWHERE", CommandFlags.Modal)]
        public void FtfWhere()
        {
            var doc = Autodesk.AutoCAD.ApplicationServices.Application
                        .DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            var ed = doc.Editor;
            var db = doc.Database;

            ed.WriteMessage("\nFTFWHERE");
            ed.WriteMessage("\n  plugin:  {0}", typeof(TreeCommands).Assembly.Location);
            ed.WriteMessage("\n  drawing: {0}",
                string.IsNullOrWhiteSpace(db.Filename) ? "(never saved)" : db.Filename);

            ed.WriteMessage("\n  rules.json search order:");
            foreach (var path in FtfSession.CandidatePaths(db))
                ed.WriteMessage("\n    [{0}] {1}",
                    System.IO.File.Exists(path) ? "found" : "  -  ", path);

            try
            {
                var cfg = FtfSession.Rules(db);
                ed.WriteMessage("\n  loaded rules version {0}: {1} code rule(s), {2} modifier(s).\n",
                    cfg.Version, cfg.Codes.Count, cfg.Modifiers.Count);
            }
            catch (ConfigException ex)
            {
                ed.WriteMessage("\n  NOT LOADED: {0}\n", ex.Message);
            }
        }

        /// <summary>
        /// Deletes everything the automatic pipeline owns. The undo button for a bad
        /// run -- which is exactly why it must NOT touch what FTFDRAWLINE drafted:
        /// that geometry is the user's deliberate work, not a run's output, and a
        /// cleanup of the pipeline must never take a boundary line with it.
        /// FTFDRAFTCLEAN manages the drafted world separately.
        /// </summary>
        [CommandMethod("FTFCLEAN", CommandFlags.Modal)]
        public void FtfClean()
        {
            FtfSession.Run("FTFCLEAN", (db, tr, ed) =>
            {
                var drafted = 0;
                var removed = Ownership.DeleteOwned(db, tr, s =>
                {
                    if (s.Kind == FtfEntityKind.DraftLine ||
                        s.Kind == FtfEntityKind.DraftAnnotation ||
                        s.Kind == FtfEntityKind.DraftMask ||
                        s.Kind >= FtfEntityKind.UtilityPipe)
                    {
                        drafted++;
                        return false;
                    }
                    return true;
                });

                ed.WriteMessage("\nFTFCLEAN: removed {0} plugin-owned entities.", removed);
                if (drafted > 0)
                    ed.WriteMessage("\nFTFCLEAN: left {0} production drafting entities " +
                                    "alone (drafted lines, dip pipes and structure labels, " +
                                    "easements). Their own tools manage those.",
                                    drafted);
                ed.WriteMessage("\n");
            });
        }

        // ------------------------------------------------------------------ placement

        /// <summary>
        /// Rotates the marker Civil 3D already drew, rather than placing a rotated copy
        /// beside it. This is the shape of everything FTF does to points: adjust what is
        /// there, do not recreate it.
        ///
        /// MarkerRotation is in radians, per Autodesk's own Civil 3D API example.
        ///
        /// NOTE: this modifies a Civil 3D owned object rather than creating an FTF owned
        /// one, so FTFCLEAN cannot undo it -- there is no entity to erase. Re-running is
        /// safe because the value is idempotent. See "Reversing modifications" in the
        /// README for the production requirement this leaves open.
        /// </summary>
        private static bool RotateMarker(Transaction tr, CogoPoint cogo, double cadDegrees)
        {
            var radians = FieldCodes.Geometry.Angles.CadDegreesToApiRadians(cadDegrees);

            // Idempotent: a re-run sets the same value, so running twice is a no-op.
            if (Math.Abs(cogo.MarkerRotation - radians) < 1e-9) return false;

            try
            {
                var writable = (CogoPoint)tr.GetObject(cogo.ObjectId, OpenMode.ForWrite);
                writable.MarkerRotation = radians;
                return true;
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                // A point locked by its point group or a read-only style is not an error
                // worth failing the run over.
                return false;
            }
        }

        private static void PlaceBlock(Database db, Transaction tr, CogoPoint cogo,
                                       ParsedPoint parsed, ObjectId blockId, string rulesVersion)
        {
            var layerId = CadUtil.EnsureLayer(db, tr, parsed.BlockLayer);
            var position = CadUtil.Flatten(cogo.Location);

            using (var br = new BlockReference(position, blockId))
            {
                br.LayerId = layerId;

                var scale = parsed.BlockScale > 0 ? parsed.BlockScale : 1.0;
                br.ScaleFactors = new Scale3d(scale, scale, scale);

                if (parsed.RotationDegrees.HasValue)
                    br.Rotation = CadUtil.DegreesToRadians(parsed.RotationDegrees.Value);

                CadUtil.AddToModelSpace(db, tr, br);

                Ownership.Stamp(br, parsed.PointNumber, rulesVersion, FtfEntityKind.Block, null);
            }
        }

        // ------------------------------------------------------------------ reporting

        /// <summary>
        /// Writes the codes nobody has configured yet, with counts and a few real
        /// descriptions each. That file is how the next set of rules gets written --
        /// guessing at a grammar is what produced a tree rule matching none of 765
        /// real points.
        /// </summary>
        internal static void WriteUnhandled(Database db, Editor ed,
                                            FieldCodes.Reporting.UnhandledSummary summary)
        {
            if (summary.TotalPoints == 0) return;

            ed.WriteMessage("\n  Not configured yet: {0}", summary.OneLine(8));

            var path = FieldCodes.Reporting.UnhandledSummary.PathFor(db.Filename);
            if (path == null)
            {
                ed.WriteMessage(
                    "\n  ({0} code(s) across {1} point(s); save the drawing to get the " +
                    "full list as a file.)",
                    summary.DistinctCodes, summary.TotalPoints);
                return;
            }

            try
            {
                summary.Write(path);
                ed.WriteMessage("\n  {0} code(s) listed in {1}", summary.DistinctCodes, path);
            }
            catch (System.IO.IOException ex)
            {
                ed.WriteMessage("\n  Could not write {0}: {1}", path, ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                ed.WriteMessage("\n  Could not write {0}: {1}", path, ex.Message);
            }
        }

        /// <summary>
        /// Beside the drawing, or in the folder the user chose. Named after the drawing
        /// either way, so reports from different jobs never collide.
        /// </summary>
        private static string ReportPath(Database db, FieldCodes.Settings.FtfSettings settings)
        {
            var beside = ExceptionReport.PathFor(db.Filename);
            if (settings.General.ReportLocation != FieldCodes.Settings.ReportLocation.CustomFolder)
                return beside;

            var folder = settings.General.ReportFolder;
            if (string.IsNullOrWhiteSpace(folder) || beside == null) return beside;

            return System.IO.Path.Combine(folder, System.IO.Path.GetFileName(beside));
        }

        internal static ExceptionRow RowFor(CogoPoint cogo, ParsedPoint parsed, string severity)
        {
            return new ExceptionRow
            {
                PointNumber = parsed.PointNumber,
                Easting = cogo.Easting,
                Northing = cogo.Northing,
                Elevation = cogo.Elevation,
                RawDescription = parsed.RawDescription,
                Severity = severity,
                Diagnostics = string.Join(" | ",
                    parsed.Diagnostics.Select(d => d.ToString()).ToArray())
            };
        }

        internal static void WriteReport(Database db, Editor ed, IList<ExceptionRow> rows,
                                         FieldCodes.Settings.FtfSettings settings)
        {
            if (rows.Count == 0 && !settings.General.WriteReportWhenEmpty)
            {
                ed.WriteMessage("\nNo exceptions; report not written.\n");
                return;
            }

            var path = ReportPath(db, settings);

            if (path == null)
            {
                if (rows.Count > 0)
                    ed.WriteMessage(
                        "\n{0} exception(s) found, but the drawing has never been saved " +
                        "so no report was written. Save the drawing and run again.\n",
                        rows.Count);
                return;
            }

            try
            {
                // A configured report folder that does not exist yet is created
                // rather than failing the run -- a typo'd or not-yet-made folder is
                // fixable in Settings, and the write should just work meanwhile.
                var dir = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);

                ExceptionReport.Write(path, rows);
                ed.WriteMessage("\nException report ({0} row(s)): {1}\n", rows.Count, path);
            }
            catch (System.IO.DirectoryNotFoundException ex)
            {
                ed.WriteMessage("\nCould not write the exception report to {0}: {1}\n",
                                path, ex.Message);
            }
            catch (System.IO.IOException ex)
            {
                ed.WriteMessage("\nCould not write the exception report to {0}: {1}\n",
                                path, ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                ed.WriteMessage("\nCould not write the exception report to {0}: {1}\n",
                                path, ex.Message);
            }
        }
    }
}
