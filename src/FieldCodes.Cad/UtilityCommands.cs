using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.DatabaseServices;
using FieldCodes.Settings;
using FieldCodes.Utilities;
using FieldCodes.Utilities.FieldNotes;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcEntity = Autodesk.AutoCAD.DatabaseServices.Entity;

namespace FieldCodes.Cad
{
    /// <summary>
    /// Shared state between the Dip Builder window and the drawing. The window
    /// never touches the database directly: it queues work and runs FTFDIPACT, so
    /// every change happens inside a real AutoCAD command -- one command, one undo.
    /// </summary>
    internal static class DipSession
    {
        public static UtilityProject Project;
        public static IntPtr ProjectDatabase;
        public static Ui.DipBuilderForm Form;

        public delegate bool DipWork(Database db, Transaction tr, Editor ed, UtilityProject project,
                                     FtfSettings settings, string rulesVersion);

        // Every click is queued and runs in order, one FTFDIPACT each -- a second click
        // before the first has run must never replace it.
        private static readonly Queue<KeyValuePair<string, DipWork>> Pending = new Queue<KeyValuePair<string, DipWork>>();
        public static string LastMessage;

        public static UtilityProject ProjectFor(Database db, Transaction tr)
        {
            if (Project == null || ProjectDatabase != db.UnmanagedObject)
            {
                Project = DrawingStore.LoadDips(db, tr);
                ProjectDatabase = db.UnmanagedObject;
            }
            return Project;
        }

        public static void Reload()
        {
            Project = null;
        }

        /// <summary>Queues work for the drawing. Refused (false) while a command is
        /// running, because the queued command name would be typed into its prompt.</summary>
        public static bool Post(string name, DipWork work)
        {
            return Post(name, work, false);
        }

        /// <summary>fromOwnCommand: posted by FTF itself at the end of a command that is
        /// not prompting (opening the window, after an undo), so it is safe to queue.</summary>
        public static bool Post(string name, DipWork work, bool fromOwnCommand)
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return false;
            if (!fromOwnCommand && !string.IsNullOrEmpty(doc.CommandInProgress))
            {
                LastMessage = "Finish or cancel the command in progress (" + doc.CommandInProgress + ") first.";
                if (Form != null && !Form.IsDisposed) Form.BeginInvoke(new Action(Form.RefreshFromSession));
                return false;
            }
            Pending.Enqueue(new KeyValuePair<string, DipWork>(name, work));
            doc.SendStringToExecute("FTFDIPACT ", true, false, true);
            return true;
        }

        public static void RunPending()
        {
            if (Pending.Count == 0) return;
            var next = Pending.Dequeue();
            var name = next.Key;
            var work = next.Value;

            LastMessage = null;
            FtfSession.Run("FTFDIP " + name, (db, tr, ed) =>
            {
                var rules = FtfSession.Rules(db);
                var settings = FtfSession.SettingsFor(db, rules);
                var project = ProjectFor(db, tr);
                if (work(db, tr, ed, project, settings, rules.Version))
                    DrawingStore.SaveDips(db, tr, project);
            });
            if (FtfSession.LastRunError != null)
            {
                LastMessage = "Failed: " + FtfSession.LastRunError;
                Reload();
            }

            if (Form != null && !Form.IsDisposed)
                Form.BeginInvoke(new Action(Form.RefreshFromSession));
        }
    }

    public sealed class UtilityCommands
    {
        private static bool _undoHooked;

        [CommandMethod("FTFDIP", CommandFlags.Modal)]
        public void OpenDipBuilder()
        {
            HookUndo();
            if (DipSession.Form == null || DipSession.Form.IsDisposed)
            {
                DipSession.Reload();
                DipSession.Form = new Ui.DipBuilderForm();
                AcadApp.ShowModelessDialog(DipSession.Form);
            }
            else
            {
                DipSession.Form.Activate();
            }
            DipSession.Post("open", (db, tr, ed, project, settings, version) => false, true);
        }

        /// <summary>Runs one queued Dip Builder action. Not meant to be typed.</summary>
        [CommandMethod("FTFDIPACT", CommandFlags.Modal | CommandFlags.NoHistory)]
        public void RunDipAction()
        {
            DipSession.RunPending();
        }

        /// <summary>After an undo or redo the drawing's dip data may have changed
        /// underneath the window, so the window reloads it.</summary>
        private static void HookUndo()
        {
            if (_undoHooked) return;
            _undoHooked = true;
            // Only a different drawing needs the dip data reloaded. Clicking a window
            // button also re-activates the SAME drawing; reloading then used to swap the
            // data out from under the click, and its change was lost.
            AcadApp.DocumentManager.DocumentActivated += (s, e) =>
            {
                if (e.Document != null && DipSession.ProjectDatabase != e.Document.Database.UnmanagedObject)
                    DipSession.Reload();
            };
            foreach (Document doc in AcadApp.DocumentManager) Hook(doc);
            AcadApp.DocumentManager.DocumentCreated += (s, e) => Hook(e.Document);
        }

        private static void Hook(Document doc)
        {
            doc.CommandEnded += (s, e) =>
            {
                var name = e.GlobalCommandName.ToUpperInvariant();
                if (name == "U" || name == "UNDO" || name == "REDO" || name == "MREDO")
                {
                    DipSession.Reload();
                    if (DipSession.Form != null && !DipSession.Form.IsDisposed)
                        DipSession.Post("reload", (db, tr, ed, project, settings, version) => false, true);
                }
            };
        }

        // ========================================================= command line

        /// <summary>
        /// Reads a field-notes file into the drawing's dip project -- the command-line
        /// twin of the window's "Import notes file". Existing observations for the
        /// structures in the file are replaced by the file's; nothing else changes.
        /// </summary>
        [CommandMethod("FTFDIPNOTES", CommandFlags.Modal)]
        public void ImportNotesFile()
        {
            FtfSession.Run("FTFDIPNOTES", (db, tr, ed) =>
            {
                var rules = FtfSession.Rules(db);
                var settings = FtfSession.SettingsFor(db, rules);

                var fallback = string.IsNullOrEmpty(db.Filename) ? null : Path.ChangeExtension(db.Filename, null) + ".dip-notes.txt";
                var prompt = new PromptStringOptions("\nField notes file" + (fallback != null ? " <" + fallback + ">" : string.Empty) + ": ") { AllowSpaces = true };
                var answer = ed.GetString(prompt);
                if (answer.Status != PromptStatus.OK) return;
                var path = string.IsNullOrWhiteSpace(answer.StringResult) ? fallback : answer.StringResult.Trim().Trim('"');
                if (path == null || !File.Exists(path)) { ed.WriteMessage("\nFTFDIPNOTES: no notes file at \"{0}\".\n", path); return; }

                var parsed = new DipNoteParser(settings.Dips).Parse(File.ReadAllText(path));
                foreach (var d in parsed.Diagnostics) ed.WriteMessage("\n  {0}: {1}", d.Severity, d);
                if (parsed.Structures.Count == 0) { ed.WriteMessage("\nFTFDIPNOTES: no \"PT <number>\" blocks in the file.\n"); return; }

                DipSession.Reload();
                var project = DipSession.ProjectFor(db, tr);
                foreach (var message in UtilityCadService.ImportNotes(project, parsed, UtilityCadService.LivePoints(db, tr), settings.Dips))
                    ed.WriteMessage("\n  " + message);
                DrawingStore.SaveDips(db, tr, project);
                ed.WriteMessage("\nFTFDIPNOTES: {0} structure block(s) read from {1}.\n", parsed.Structures.Count, Path.GetFileName(path));
            });
        }

        /// <summary>
        /// Walks every unresolved pipe that has an observed direction, lists the
        /// candidate structures with their confidence and reasons, and asks. A
        /// suggestion is never accepted without an answer.
        /// </summary>
        [CommandMethod("FTFDIPCONNECT", CommandFlags.Modal)]
        public void ConnectPipes()
        {
            FtfSession.Run("FTFDIPCONNECT", (db, tr, ed) =>
            {
                var rules = FtfSession.Rules(db);
                var settings = FtfSession.SettingsFor(db, rules);
                DipSession.Reload();
                var project = DipSession.ProjectFor(db, tr);
                var changed = 0;

                foreach (var structure in project.Structures.ToList())
                {
                    foreach (var pipe in structure.Field.Pipes.ToList())
                    {
                        var existing = project.ConnectionFor(structure.Id, pipe.Id);
                        if (existing != null && existing.Status != ConnectionStatus.Unresolved) continue;

                        ed.WriteMessage("\n{0}: {1}", structure.Label, ConnectionFinder.Describe(pipe));
                        if (!pipe.Direction.IsKnown)
                        {
                            ed.WriteMessage("\n  No observed direction -- left for the field revisit list.");
                            continue;
                        }

                        var candidates = ConnectionFinder.Find(project, structure, pipe, settings.Dips);
                        for (var i = 0; i < candidates.Count; i++)
                        {
                            var c = candidates[i];
                            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                                "\n  {0}. {1}  {2:0.0}'  {3:0.0} deg off  {4} confidence -- {5}",
                                i + 1, c.Structure.Label, c.Distance, c.DeviationDegrees, c.Confidence,
                                string.Join("; ", c.Basis.ToArray())));
                        }
                        if (candidates.Count == 0) ed.WriteMessage("\n  No surveyed structure in the search cone.");

                        var options = new PromptIntegerOptions("\n  Candidate number or [Manual/Leave/Outside/Skip] <Skip>: ")
                        {
                            AllowNone = true, AllowZero = false, AllowNegative = false, LowerLimit = 1,
                            UpperLimit = Math.Max(1, candidates.Count)
                        };
                        options.AppendKeywordsToMessage = false;
                        options.Keywords.Add("Manual");
                        options.Keywords.Add("Leave");
                        options.Keywords.Add("Outside");
                        options.Keywords.Add("Skip");
                        options.Keywords.Default = "Skip";
                        var answer = ed.GetInteger(options);
                        if (answer.Status == PromptStatus.Cancel) return;

                        if (answer.Status == PromptStatus.OK && answer.Value <= candidates.Count && candidates.Count > 0)
                        {
                            ConnectionFinder.Accept(project, structure, pipe, candidates[answer.Value - 1], false, null);
                            ed.WriteMessage("\n  Confirmed to {0}.", candidates[answer.Value - 1].Structure.Label);
                            changed++;
                        }
                        else if (answer.Status == PromptStatus.Keyword && answer.StringResult == "Manual")
                        {
                            var number = ed.GetString("\n  Point number of the structure it runs to: ");
                            if (number.Status != PromptStatus.OK) continue;
                            var live = UtilityCadService.LivePoints(db, tr);
                            CadStructureSnapshot snapshot;
                            if (!live.TryGetValue(number.StringResult.Trim(), out snapshot))
                            { ed.WriteMessage("\n  No COGO point {0} in the drawing.", number.StringResult.Trim()); continue; }
                            var target = UtilityCadService.EnsureStructure(project, snapshot, settings.Dips);
                            if (target.Id == structure.Id) { ed.WriteMessage("\n  A pipe cannot connect a structure to itself."); continue; }
                            var note = ed.GetString(new PromptStringOptions("\n  Reason (optional): ") { AllowSpaces = true });
                            ConnectionFinder.Accept(project, structure, pipe, ConnectionFinder.ManualCandidate(project, structure, pipe, target, settings.Dips),
                                                    true, note.Status == PromptStatus.OK ? note.StringResult : null);
                            ed.WriteMessage("\n  Manual connection to {0} recorded as an override.", target.Label);
                            changed++;
                        }
                        else if (answer.Status == PromptStatus.Keyword && answer.StringResult == "Outside")
                        {
                            ConnectionFinder.MarkOutsideLimits(project, structure, pipe);
                            ed.WriteMessage("\n  Recorded as running outside the survey limits.");
                            changed++;
                        }
                        else if (answer.Status == PromptStatus.Keyword && answer.StringResult == "Leave")
                        {
                            ConnectionFinder.LeaveUnresolved(project, structure, pipe);
                            changed++;
                        }
                    }
                }

                if (changed > 0) DrawingStore.SaveDips(db, tr, project);
                ed.WriteMessage("\nFTFDIPCONNECT: {0} connection decision(s) recorded.\n", changed);
            });
        }

        /// <summary>
        /// Walks every dipped pipe whose note did not say what the dip was measured to,
        /// shows the assumed elevation, and asks. Confirming changes the reference only;
        /// the measurement and its field-note source are never touched.
        /// </summary>
        [CommandMethod("FTFDIPCONFIRM", CommandFlags.Modal)]
        public void ConfirmReferences()
        {
            FtfSession.Run("FTFDIPCONFIRM", (db, tr, ed) =>
            {
                var rules = FtfSession.Rules(db);
                var settings = FtfSession.SettingsFor(db, rules);
                DipSession.Reload();
                var project = DipSession.ProjectFor(db, tr);
                var changed = 0;

                foreach (var structure in project.Structures)
                foreach (var pipe in structure.Field.Pipes.Where(p => p.ReferenceUnconfirmed && p.MeasuredDip.HasValue).ToList())
                {
                    var assumed = ObservationReview.Assumed(structure, pipe, settings.Dips);
                    ed.WriteMessage("\n{0} {1}: dip {2:0.00}' -- the note does not say what it was measured to.{3}",
                        structure.Label, ConnectionFinder.Describe(pipe), pipe.MeasuredDip.Value,
                        assumed != null ? " If invert: " + assumed.Value.ToString("0.00", CultureInfo.InvariantCulture) : string.Empty);
                    var options = new PromptKeywordOptions("\n  Measured to [Invert/Top/Springline/Skip] <Skip>: ", "Invert Top Springline Skip");
                    options.Keywords.Default = "Skip";
                    options.AllowNone = true;
                    var answer = ed.GetKeywords(options);
                    if (answer.Status == PromptStatus.Cancel) return;
                    if (answer.Status != PromptStatus.OK || answer.StringResult == "Skip") continue;

                    var reference = answer.StringResult == "Top" ? MeasurementReference.TopOfPipe
                        : answer.StringResult == "Springline" ? MeasurementReference.Springline
                        : MeasurementReference.Invert;
                    if (ObservationReview.ConfirmReference(project, structure, pipe, reference)) changed++;
                }

                if (changed > 0) DrawingStore.SaveDips(db, tr, project);
                ed.WriteMessage("\nFTFDIPCONFIRM: {0} measurement reference(s) confirmed.\n", changed);
            });
        }

        /// <summary>Drafts every confirmed connection, asking what to do where a pipe
        /// is already drawn.</summary>
        [CommandMethod("FTFDIPDRAW", CommandFlags.Modal)]
        public void DrawPipes()
        {
            FtfSession.Run("FTFDIPDRAW", (db, tr, ed) =>
            {
                var rules = FtfSession.Rules(db);
                var settings = FtfSession.SettingsFor(db, rules);
                DipSession.Reload();
                var project = DipSession.ProjectFor(db, tr);
                DrawConnections(db, tr, ed, project, settings, rules.Version, c => true);
                DrawingStore.SaveDips(db, tr, project);
                ed.WriteMessage("\n");
            });
        }

        /// <summary>Places a structure's leader label from its observations.</summary>
        [CommandMethod("FTFDIPLABEL", CommandFlags.Modal)]
        public void LabelStructure()
        {
            FtfSession.Run("FTFDIPLABEL", (db, tr, ed) =>
            {
                var rules = FtfSession.Rules(db);
                var settings = FtfSession.SettingsFor(db, rules);
                DipSession.Reload();
                var project = DipSession.ProjectFor(db, tr);

                var number = ed.GetString("\nStructure point number: ");
                if (number.Status != PromptStatus.OK) return;
                var structure = project.StructureByPoint(number.StringResult.Trim());
                if (structure == null || structure.Cad == null)
                { ed.WriteMessage("\nFTFDIPLABEL: point {0} is not in the dip project -- read its notes first.\n", number.StringResult.Trim()); return; }

                var lines = UtilityLabelFormatter.StructureLabel(project, structure, settings.Dips);
                foreach (var line in lines) ed.WriteMessage("\n  " + line);

                var at = ed.GetPoint(new PromptPointOptions("\nLabel text location: ")
                {
                    UseBasePoint = true, BasePoint = new Point3d(structure.Cad.Easting, structure.Cad.Northing, 0)
                });
                if (at.Status != PromptStatus.OK) return;
                var styles = UtilityCadService.MissingStyles(db, tr, settings.Dips);
                if (styles != null) ed.WriteMessage("\n" + styles);

                UtilityCadService.PlaceStructureLabel(db, tr, structure, string.Join("\n", lines.ToArray()),
                                                      at.Value.TransformBy(ed.CurrentUserCoordinateSystem), settings, rules.Version);
                project.Overrides.RemoveAll(o => o.Target == structure.Id && o.What == "Structure label text");
                DrawingStore.SaveDips(db, tr, project);
                AfterStructuresFinished(db, tr, ed, project, settings, new[] { structure.Id });
                ed.WriteMessage("\n");
            });
        }

        /// <summary>
        /// Places a leader label for every structure that has pipes and no label yet,
        /// set up and to the right of its point. Labels already placed -- including ones
        /// moved or edited by hand -- are left exactly as they are.
        /// </summary>
        internal static int LabelAll(Database db, Transaction tr, Editor ed, UtilityProject project, FtfSettings settings, string version,
                                     IList<string> labelled = null)
        {
            var offset = CadUtil.DrawingUnitsPerPlottedUnit(db) * 0.6;
            var placed = 0;
            var styles = UtilityCadService.MissingStyles(db, tr, settings.Dips);
            if (styles != null) ed.WriteMessage("\n" + styles);
            var scaleNote = UtilityCadService.ScaleNote(db, settings.Dips);
            if (scaleNote != null) ed.WriteMessage("\n" + scaleNote);
            foreach (var structure in project.Structures.Where(s => s.Cad != null && s.Field.Pipes.Count > 0))
            {
                if (UtilityCadService.StructureLabelLocation(db, tr, structure).HasValue) continue;
                var text = string.Join("\n", UtilityLabelFormatter.StructureLabel(project, structure, settings.Dips).ToArray());
                UtilityCadService.PlaceStructureLabel(db, tr, structure, text,
                    new Point3d(structure.Cad.Easting + offset, structure.Cad.Northing + offset, 0), settings, version);
                if (labelled != null) labelled.Add(structure.Id);
                placed++;
            }
            ed.WriteMessage("\nDip Builder: {0} structure label(s) placed; structures already labelled were left as they are.", placed);
            return placed;
        }

        /// <summary>Draws accepted connections that pass the filter. Invalid geometry
        /// is refused; every warning is printed; existing pipes are never replaced
        /// without an answer.</summary>
        internal static int DrawConnections(Database db, Transaction tr, Editor ed, UtilityProject project,
                                            FtfSettings settings, string version, Func<PipeConnection, bool> filter)
        {
            var findings = UtilityQc.Evaluate(project, settings.Dips);
            var drawn = 0;
            var styles = UtilityCadService.MissingStyles(db, tr, settings.Dips);
            if (styles != null) ed.WriteMessage("\n" + styles);
            var scaleNote = UtilityCadService.ScaleNote(db, settings.Dips);
            if (scaleNote != null) ed.WriteMessage("\n" + scaleNote);

            foreach (var c in project.Connections.Where(c => c.IsAccepted && c.ToStructureId != null && filter(c)).ToList())
            {
                var from = project.Structure(c.FromStructureId);
                var to = project.Structure(c.ToStructureId);

                if (findings.Any(f => f.ConnectionId == c.Id && f.BlocksDrafting))
                {
                    ed.WriteMessage("\n  {0} -> {1}: not drawn -- the geometry would be invalid.", from.Label, to.Label);
                    continue;
                }
                foreach (var f in findings.Where(f => f.ConnectionId == c.Id || (f.StructureId == c.FromStructureId && f.PipeId == c.FromPipeId)))
                    ed.WriteMessage("\n  ! " + f.Message);

                var existing = UtilityCadService.FindExisting(db, tr, project, c, settings.Dips, settings.General.UnitsPerFoot, settings.General.LayerMappings);
                var labelOnly = false;
                if (existing.Count > 0)
                {
                    var owned = existing.Any(x => x.OwnedByFtf);
                    ed.WriteMessage("\n  {0} -> {1}: {2} existing pipe object(s) found between these structures ({3}).",
                                    from.Label, to.Label, existing.Count, owned ? "drafted by FTF" : "drawn by hand");
                    var options = new PromptKeywordOptions("\n  Existing pipe [Update/Replace/Keep/New] <Keep>: ", "Update Replace Keep New");
                    options.Keywords.Default = "Keep";
                    options.AllowNone = true;
                    var answer = ed.GetKeywords(options);
                    var choice = answer.Status == PromptStatus.OK ? answer.StringResult : "Keep";

                    if (choice == "Keep") continue;
                    if (choice == "Replace")
                    {
                        UtilityCadService.EraseConnectionDrafting(db, tr, c.Id);
                        foreach (var x in existing.Where(x => !x.Id.IsErased))
                            ((AcEntity)tr.GetObject(x.Id, OpenMode.ForWrite)).Erase();
                    }
                    else if (choice == "Update")
                    {
                        UtilityCadService.EraseConnectionDrafting(db, tr, c.Id);
                        var handDrawn = existing.Where(x => !x.OwnedByFtf && !x.Id.IsErased).ToList();
                        if (handDrawn.Count > 0)
                        {
                            foreach (var x in handDrawn)
                                UtilityCadService.Adopt(db, tr, x.Id, project, c, settings, version);
                            labelOnly = true;
                            project.Overrides.Add(new ManualOverride { Target = c.Id, What = "Existing hand-drawn pipe adopted", Entered = handDrawn.Count + " object(s)", Utc = DateTime.UtcNow });
                        }
                    }
                    // "New" draws alongside whatever is there.
                }
                else
                {
                    UtilityCadService.EraseConnectionDrafting(db, tr, c.Id);
                }

                UtilityCadService.DrawPipe(db, tr, project, c, settings, version, labelOnly);
                drawn++;
            }

            // A pipe running outside the survey limits has no far structure to draw to, but the
            // plan should still show that one leaves the structure that way.
            var stubs = 0;
            foreach (var c in project.Connections.Where(c => c.Status == ConnectionStatus.OutsideSurveyLimits && filter(c)).ToList())
            {
                UtilityCadService.EraseConnectionDrafting(db, tr, c.Id);
                if (UtilityCadService.DrawOutsideLimitsStub(db, tr, project, c, settings, version).Count > 0) stubs++;
            }

            ed.WriteMessage("\nDip Builder: {0} pipe(s) drafted{1}.", drawn,
                stubs > 0 ? ", and " + stubs + " stub(s) for pipes running outside the survey limits" : string.Empty);
            return drawn;
        }

        // ================================================================= check

        /// <summary>
        /// Finds structures whose surveyed point moved or changed elevation since
        /// their pipes and labels were drafted, and offers to rebuild each one.
        /// Nothing is changed without a yes.
        /// </summary>
        [CommandMethod("FTFDIPCHECK", CommandFlags.Modal)]
        public void CheckDips()
        {
            FtfSession.Run("FTFDIPCHECK", (db, tr, ed) =>
            {
                var rules = FtfSession.Rules(db);
                var settings = FtfSession.SettingsFor(db, rules);
                DipSession.Reload();
                var project = DipSession.ProjectFor(db, tr);
                var live = UtilityCadService.LivePoints(db, tr);
                var stale = UtilityQc.StaleStructures(project, live);

                var findings = UtilityQc.Evaluate(project, settings.Dips);
                ed.WriteMessage("\nFTFDIPCHECK: {0}", UtilityQc.Summarize(project, findings, stale.Count));

                if (stale.Count == 0)
                {
                    ed.WriteMessage("\nNo structure has changed since it was calculated.\n");
                    return;
                }

                var rebuilt = Rebuild(db, tr, ed, project, settings, rules.Version, stale, live, true);
                if (rebuilt > 0) DrawingStore.SaveDips(db, tr, project);
                ed.WriteMessage("\n{0} structure(s) rebuilt.\n", rebuilt);
            });
        }

        internal static int Rebuild(Database db, Transaction tr, Editor ed, UtilityProject project,
                                    FtfSettings settings, string version, IList<StructureRecord> stale,
                                    IDictionary<string, CadStructureSnapshot> live, bool ask)
        {
            var rebuilt = 0;
            var all = !ask;
            foreach (var structure in stale)
            {
                CadStructureSnapshot now;
                live.TryGetValue(structure.Cad.PointNumber ?? string.Empty, out now);
                if (now == null)
                {
                    ed.WriteMessage("\n  {0}: its point is no longer in the drawing -- review manually.", structure.Label);
                    continue;
                }

                ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                    "\n  {0}: rim {1:0.00} -> {2:0.00}, moved {3:0.00}'.", structure.Label, structure.Cad.Rim, now.Rim,
                    Math.Sqrt(Math.Pow(now.Easting - structure.Cad.Easting, 2) + Math.Pow(now.Northing - structure.Cad.Northing, 2))));

                if (!all)
                {
                    var options = new PromptKeywordOptions("\n  Rebuild its calculations and drafting? [Yes/No/All] <Yes>: ", "Yes No All");
                    options.Keywords.Default = "Yes";
                    var answer = ed.GetKeywords(options);
                    if (answer.Status != PromptStatus.OK || answer.StringResult == "No") continue;
                    if (answer.StringResult == "All") all = true;
                }

                var labelAt = UtilityCadService.StructureLabelLocation(db, tr, structure);
                structure.Cad = now;                          // the survey is the authority

                foreach (var c in project.Connections.Where(c => c.Drafted &&
                             (c.FromStructureId == structure.Id || c.ToStructureId == structure.Id)).ToList())
                {
                    UtilityCadService.EraseConnectionDrafting(db, tr, c.Id);
                    UtilityCadService.DrawPipe(db, tr, project, c, settings, version, false);
                }

                if (labelAt.HasValue)
                {
                    var edited = project.Overrides.Any(o => o.Target == structure.Id && o.What == "Structure label text");
                    if (edited)
                        ed.WriteMessage("\n  {0}: its label text was edited by hand and is left as is -- review it.", structure.Label);
                    else
                        UtilityCadService.PlaceStructureLabel(db, tr, structure,
                            string.Join("\n", UtilityLabelFormatter.StructureLabel(project, structure, settings.Dips).ToArray()),
                            labelAt.Value, settings, version);
                }
                rebuilt++;
            }
            return rebuilt;
        }

        // =============================================================== inspect

        /// <summary>Shows where a drafted pipe or structure label's information came
        /// from: point, rim, dip, reference, calculation, connection and warnings.</summary>
        [CommandMethod("FTFDIPINSPECT", CommandFlags.Modal)]
        public void InspectDip()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;

            var picked = ed.GetEntity("\nSelect a drafted pipe, pipe label or structure label: ");
            if (picked.Status != PromptStatus.OK) return;

            FtfSession.Run("FTFDIPINSPECT", (db, tr, ed2) =>
            {
                var rules = FtfSession.Rules(db);
                var settings = FtfSession.SettingsFor(db, rules);
                var project = DrawingStore.LoadDips(db, tr);
                var entity = (AcEntity)tr.GetObject(picked.ObjectId, OpenMode.ForRead);
                var stamp = Ownership.Read(entity);
                var findings = UtilityQc.Evaluate(project, settings.Dips);

                if (stamp == null)
                {
                    ed.WriteMessage("\nThat object was not drafted by the Dip Builder.\n");
                    return;
                }

                if (stamp.Kind == FtfEntityKind.UtilityPipe || stamp.Kind == FtfEntityKind.UtilityPipeLabel)
                {
                    var c = project.Connections.FirstOrDefault(x => x.Id == stamp.PointNumber);
                    ed.WriteMessage("\n" + (c == null
                        ? "The pipe's record is missing from the drawing's dip data (was it undone?)."
                        : UtilityProvenance.ForConnection(project, c, findings, settings.Dips)) + "\n");
                    return;
                }

                if (stamp.Kind == FtfEntityKind.StructureLabel)
                {
                    var s = project.Structure(stamp.PointNumber);
                    ed.WriteMessage("\n" + (s == null ? "The structure's record is missing." : DescribeStructure(project, s, findings)) + "\n");
                    return;
                }

                ed.WriteMessage("\nThat FTF object is not part of the Dip Builder.\n");
            });
        }

        internal static string DescribeStructure(UtilityProject project, StructureRecord s, IList<QcFinding> findings)
        {
            var sb = new StringBuilder();
            sb.AppendLine("STRUCTURE " + s.Label + " (" + s.System + ", " + (s.StructureType ?? "?") + ")");
            if (s.Cad != null)
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "  CAD point {0}: rim {1:0.00}, N {2:0.00}, E {3:0.00}, description \"{4}\"",
                    s.Cad.PointNumber, s.Cad.Rim, s.Cad.Northing, s.Cad.Easting, s.Cad.Description));
            var bottom = DipElevations.Bottom(s);
            if (bottom != null) sb.AppendLine("  Bottom: dip " + s.Field.BottomDip.Value.ToString("0.00", CultureInfo.InvariantCulture) + "' -> " + bottom.Formula + " = " + bottom.Value.ToString("0.00", CultureInfo.InvariantCulture));
            var water = DipElevations.Water(s);
            if (water != null) sb.AppendLine("  Water: dip " + s.Field.WaterDip.Value.ToString("0.00", CultureInfo.InvariantCulture) + "' -> " + water.Formula + " = " + water.Value.ToString("0.00", CultureInfo.InvariantCulture));
            foreach (var p in s.Field.Pipes)
            {
                var e = DipElevations.Pipe(s, p);
                var c = project.ConnectionFor(s.Id, p.Id);
                sb.AppendLine("  Pipe " + ConnectionFinder.Describe(p) + " [" + p.Source + "]" +
                              (e != null ? ": " + e.Formula + " = " + e.Value.ToString("0.00", CultureInfo.InvariantCulture) +
                                           (p.ReferenceUnconfirmed ? " (REFERENCE NOT STATED - unconfirmed)" : p.ReferenceBasis == ReferenceBasis.StatedInFieldNote ? string.Empty : " (" + p.ReferenceBasis + ")") : ": not dipped") +
                              " -- " + (c == null ? "unresolved" : c.Status.ToString()));
                if (!string.IsNullOrEmpty(p.RawText)) sb.AppendLine("    Field note: " + p.RawText.Trim());
            }
            foreach (var o in project.Overrides.Where(o => o.Target == s.Id))
                sb.AppendLine("  Override: " + o.What + " -- " + o.Entered);
            foreach (var f in findings.Where(f => f.StructureId == s.Id))
                sb.AppendLine("  ! " + f.Message);
            return sb.ToString();
        }

        // =============================================================== revisit

        /// <summary>Writes the field revisit list beside the drawing.</summary>
        [CommandMethod("FTFDIPREVISIT", CommandFlags.Modal)]
        public void ExportRevisit()
        {
            FtfSession.Run("FTFDIPREVISIT", (db, tr, ed) =>
            {
                var rules = FtfSession.Rules(db);
                var settings = FtfSession.SettingsFor(db, rules);
                var project = DrawingStore.LoadDips(db, tr);
                var text = UtilityQc.FieldRevisitText(project, UtilityQc.Evaluate(project, settings.Dips));
                var path = ExportRevisitFile(db, text);
                ed.WriteMessage(path == null
                    ? "\nSave the drawing first; the list is written beside it.\n"
                    : "\nField revisit list: " + path + "\n");
            });
        }

        internal static string ExportRevisitFile(Database db, string text)
        {
            if (string.IsNullOrEmpty(db.Filename)) return null;
            var path = Path.ChangeExtension(db.Filename, null) + ".ftf-field-revisit.txt";
            File.WriteAllText(path, "FIELD REVISIT - " + Path.GetFileName(db.Filename) + Environment.NewLine +
                                    DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) +
                                    Environment.NewLine + Environment.NewLine + text, new UTF8Encoding(true));
            return path;
        }

        // =========================================================== field notes PDF

        /// <summary>
        /// Saves the dip field notes PDF for the drawing's completed observations, using the field-notes header
        /// stored in the drawing (project, number, date of observation, crew). Asks for each header value at the
        /// command line, Enter keeping the one shown; "." clears it. The PDF goes in the field notes folder
        /// (Settings > Storm &amp; Sewer Dips) as a new revision unless the office chose to overwrite.
        /// </summary>
        [CommandMethod("FTFDIPPDF", CommandFlags.Modal)]
        public void SaveFieldNotesPdf()
        {
            FtfSession.Run("FTFDIPPDF", (db, tr, ed) =>
            {
                var rules = FtfSession.Rules(db);
                var settings = FtfSession.SettingsFor(db, rules);
                DipSession.Reload();
                var project = DipSession.ProjectFor(db, tr);

                var header = DipFieldNotesStore.Suggest(db, tr);
                string answer;
                if (!AskHeader(ed, "Project name", header.ProjectName, out answer)) return;
                header.ProjectName = answer;
                if (!AskHeader(ed, "Project number", header.ProjectNumber, out answer)) return;
                header.ProjectNumber = answer;
                while (true)
                {
                    if (!AskHeader(ed, "Date of observation (YYYY-MM-DD)", header.ObservationDateText, out answer)) return;
                    var check = new FieldNotesHeader { ObservationDateText = answer };
                    if (answer == null || check.ObservationDate.HasValue) { header.ObservationDateText = answer; break; }
                    ed.WriteMessage("\n  Type the date as YYYY-MM-DD, for example 2026-10-28, or . for not recorded.");
                }
                if (!AskHeader(ed, "Crew / observer", header.Crew, out answer)) return;
                header.Crew = answer;
                DipFieldNotesStore.Save(db, tr, header);

                var result = SaveFieldNotes(db, tr, ed, project, settings, header, null, settings.Dips.FieldNotesOverwrite, false, null);
                if (settings.Dips.FieldNotesOpenAfterSave &&
                    (result.Outcome == FieldNotesOutcome.Written || result.Outcome == FieldNotesOutcome.Replaced))
                    OpenPdf(result.Path, ed);
                ed.WriteMessage("\n");
            });
        }

        /// <summary>One header prompt. False when cancelled; the value is null when cleared or left blank.</summary>
        private static bool AskHeader(Editor ed, string what, string current, out string value)
        {
            value = current;
            var shown = string.IsNullOrWhiteSpace(current) ? "not recorded" : current.Trim();
            var options = new PromptStringOptions("\n" + what + " <" + shown + ">: ") { AllowSpaces = true };
            var result = ed.GetString(options);
            if (result.Status == PromptStatus.Cancel) return false;
            if (result.Status != PromptStatus.OK) return true;
            var typed = (result.StringResult ?? string.Empty).Trim();
            if (typed.Length == 0) return true;
            value = typed == "." ? null : typed;
            return true;
        }

        /// <summary>
        /// Writes the field notes PDF for the selected structures (null: every complete one). Incomplete observations
        /// are never included -- they are listed in the PDF as not included, with what they are missing. Nothing is
        /// written for an unsaved drawing unless a folder is given. Returns what happened; the message is also
        /// written to the command line.
        /// </summary>
        internal static FieldNotesSaveResult SaveFieldNotes(Database db, Transaction tr, Editor ed, UtilityProject project,
                                                            FtfSettings settings, FieldNotesHeader header,
                                                            ICollection<string> structureIds, bool overwrite, bool automatic,
                                                            string folder)
        {
            var drawing = SavedDrawingPath(db);
            folder = !string.IsNullOrWhiteSpace(folder)
                ? folder.Trim()
                : FieldNotesFiles.ResolveFolder(drawing, settings.Dips.FieldNotesFolder, Directory.Exists);
            var stale = DipFieldNotes.StaleIds(project, UtilityCadService.LivePoints(db, tr));
            var report = FieldNotesReportBuilder.Build(project, settings.Dips, header,
                drawing != null ? Path.GetFileName(drawing) : null, structureIds, stale);
            var result = DipFieldNotes.Save(report, folder, FieldNotesFiles.BaseName(header, drawing),
                                            DipFieldNotes.OptionsFrom(settings.Dips, overwrite, automatic));

            ed.WriteMessage("\nDip field notes: " + result.Message);
            if (result.Outcome == FieldNotesOutcome.Written || result.Outcome == FieldNotesOutcome.Replaced)
                ed.WriteMessage("\n  {0} structure(s), {1} page(s){2}.", result.Structures, result.Pages,
                                result.Incomplete > 0 ? "; " + result.Incomplete + " incomplete observation(s) listed as not included" : string.Empty);
            return result;
        }

        /// <summary>
        /// Called when the Dip Builder's finishing step for a structure -- placing its label -- has been done. With
        /// "save field notes automatically" on, and at least one of those structures complete, the field notes PDF is
        /// saved for every complete observation: a new revision only when something changed (or the newest file
        /// replaced, when the office chose overwrite). Never opened, never marks anything complete, and never lets a
        /// problem here undo the label that was just placed. Returns what was saved, or null when nothing was tried.
        /// </summary>
        internal static FieldNotesSaveResult AfterStructuresFinished(Database db, Transaction tr, Editor ed, UtilityProject project,
                                                                     FtfSettings settings, IEnumerable<string> finishedIds)
        {
            if (!settings.Dips.FieldNotesAutoGenerate || project == null) return null;
            try
            {
                var ids = (finishedIds ?? Enumerable.Empty<string>()).Where(id => id != null).Distinct().ToList();
                if (ids.Count == 0) return null;
                var stale = DipFieldNotes.StaleIds(project, UtilityCadService.LivePoints(db, tr));
                var complete = ids.Where(id => DipObservationStatus.Check(project, project.Structure(id), stale).IsComplete).ToList();
                if (complete.Count == 0)
                {
                    foreach (var id in ids)
                    {
                        var s = project.Structure(id);
                        var status = DipObservationStatus.Check(project, s, stale);
                        ed.WriteMessage("\nDip field notes not updated: {0} is not complete ({1}).",
                                        s != null ? s.Label : "structure", string.Join("; ", status.Missing.ToArray()));
                    }
                    return new FieldNotesSaveResult
                    {
                        Outcome = FieldNotesOutcome.NothingToSave,
                        Message = "Field notes not updated: the structure's observation is not complete."
                    };
                }
                if (SavedDrawingPath(db) == null)
                {
                    ed.WriteMessage("\nDip field notes not saved automatically: save the drawing first.");
                    return new FieldNotesSaveResult { Outcome = FieldNotesOutcome.Failed, Message = "Field notes not saved: save the drawing first." };
                }
                return SaveFieldNotes(db, tr, ed, project, settings, DipFieldNotesStore.Suggest(db, tr), null,
                                      settings.Dips.FieldNotesOverwrite, true, null);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\nDip field notes were not saved automatically: " + ex.Message);
                return new FieldNotesSaveResult { Outcome = FieldNotesOutcome.Failed, Message = "Field notes not saved automatically: " + ex.Message };
            }
        }

        /// <summary>What the Save Field Notes PDF window starts from, read inside a command.</summary>
        internal static Ui.DipFieldNotesPrep PrepareFieldNotes(Database db, Transaction tr, UtilityProject project, FtfSettings settings)
        {
            var drawing = SavedDrawingPath(db);
            var stale = DipFieldNotes.StaleIds(project, UtilityCadService.LivePoints(db, tr));
            var prep = new Ui.DipFieldNotesPrep
            {
                Header = DipFieldNotesStore.Suggest(db, tr),
                DrawingPath = drawing,
                Folder = FieldNotesFiles.ResolveFolder(drawing, settings.Dips.FieldNotesFolder, Directory.Exists),
                Overwrite = settings.Dips.FieldNotesOverwrite,
                OpenAfterSave = settings.Dips.FieldNotesOpenAfterSave
            };
            foreach (var s in project.Structures)
            {
                var status = DipObservationStatus.Check(project, s, stale);
                prep.Choices.Add(new Ui.DipFieldNotesChoice
                {
                    StructureId = s.Id,
                    Label = s.Label,
                    Complete = status.IsComplete,
                    Missing = string.Join("; ", status.Missing.ToArray())
                });
            }
            return prep;
        }

        /// <summary>The drawing's file, or null when it has never been saved (a new drawing reports its template).</summary>
        internal static string SavedDrawingPath(Database db)
        {
            var name = db.Filename;
            if (string.IsNullOrWhiteSpace(name)) return null;
            if (name.EndsWith(".dwt", StringComparison.OrdinalIgnoreCase)) return null;
            return name;
        }

        /// <summary>Opens a saved PDF in the default viewer. A machine with no PDF viewer is told, not crashed.</summary>
        internal static void OpenPdf(string path, Editor ed)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            try { System.Diagnostics.Process.Start(path); }
            catch (System.ComponentModel.Win32Exception ex)
            {
                if (ed != null) ed.WriteMessage("\nThe PDF was saved but could not be opened ({0}): {1}", ex.Message, path);
            }
            catch (InvalidOperationException ex)
            {
                if (ed != null) ed.WriteMessage("\nThe PDF was saved but could not be opened ({0}): {1}", ex.Message, path);
            }
        }
    }
}
