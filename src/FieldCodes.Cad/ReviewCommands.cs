using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using FieldCodes.Review;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcEntity = Autodesk.AutoCAD.DatabaseServices.Entity;

namespace FieldCodes.Cad
{
    /// <summary>
    /// The drawing's review list: everything FTF could not decide, kept in the drawing until a
    /// drafter deals with it. FTFREVIEW shows it; FTFREVIEWCHECK rechecks the codes; FTFQC marks
    /// the drawing Drafting QC Complete -- refused while required items are open. Nothing here
    /// blocks saving or drafting.
    /// </summary>
    internal static class ReviewStore
    {
        public const string CommandsSource = "Commands";

        /// <summary>Records what one check found, in the caller's transaction.</summary>
        public static int Report(Database db, Transaction tr, string source, IEnumerable<ReviewItem> items, bool complete,
                                 FieldCodes.Settings.ReviewSettings settings)
        {
            var register = DrawingStore.ReadReview(db, tr);
            var added = register.Report(source, items, DateTime.Now, complete, settings);
            DrawingStore.WriteReview(db, tr, register);
            return added;
        }

        /// <summary>A command that did not finish goes on the list; best effort, never throws.</summary>
        public static void CommandFailed(Database db, string command, string message)
        {
            try
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var item = ReviewKinds.New(ReviewKinds.CommandError, command, command + " stopped: " + message);
                    item.Issue = command + " did not finish";
                    item.FixCommand = command;
                    var register = DrawingStore.ReadReview(db, tr);
                    register.Report(CommandsSource, new[] { item }, DateTime.Now, false, null);
                    DrawingStore.WriteReview(db, tr, register);
                    tr.Commit();
                }
            }
            catch (System.Exception) { }
        }

        /// <summary>A command that finished closes its own earlier failure; writes only when
        /// there is one, so a clean run leaves the drawing as it was.</summary>
        public static void CommandSucceeded(Database db, string command)
        {
            try
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var register = DrawingStore.ReadReview(db, tr);
                    var item = register.Find(ReviewKinds.CommandError + ":" + command);
                    if (item != null && item.IsOpen)
                    {
                        register.Resolve(item.Id, command + " finished " + DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), "FTF", DateTime.Now);
                        DrawingStore.WriteReview(db, tr, register);
                    }
                    tr.Commit();
                }
            }
            catch (System.Exception) { }
        }

        /// <summary>The read-only checks over the points: unknown codes, unreadable descriptions,
        /// invalid values, and words after a linework code that are not line commands.</summary>
        public static int CheckCodes(Database db, Transaction tr, RulesConfig cfg, FieldCodes.Settings.FtfSettings settings,
                                     out int codeItems, out int modifierItems)
        {
            var ignored = settings.Diagnostics != null ? settings.Diagnostics.Codes() : new List<string>();
            var diagnostics = new CodeDiagnostics(cfg, ignored);
            var parser = new FieldCodeParser(cfg);
            var notes = CogoNotes.For(db, tr, settings);
            var points = new List<ReviewPoint>();
            foreach (var cogo in CadUtil.CogoPointsInOrder(db, tr))
            {
                ParsedPoint parsed;
                try { parsed = notes.Parse(parser, cogo); }
                catch (Autodesk.AutoCAD.Runtime.Exception) { continue; }
                TreeCommands.MissingBlock(db, tr, parsed);
                points.Add(new ReviewPoint
                {
                    Number = cogo.PointNumber.ToString(CultureInfo.InvariantCulture),
                    Description = cogo.RawDescription,
                    Handle = cogo.Handle.ToString(),
                    X = cogo.Location.X,
                    Y = cogo.Location.Y,
                    Parsed = parsed
                });
            }

            var codes = ReviewChecks.Codes(points, diagnostics);
            // Codes a linework word may legitimately be: every rule's code, control codes,
            // ignored codes, gate posts.
            var known = new List<string>();
            known.AddRange(cfg.IgnoreCodes ?? new List<string>());
            known.AddRange(cfg.ControlCodes ?? new List<string>());
            known.AddRange(ignored);
            known.AddRange(settings.Gates.PostCodes ?? new List<string>());
            foreach (var f in cfg.LineFeatures ?? new List<LineFeatureRule>())
            {
                known.Add(f.Code);
                if (f.Aliases != null) known.AddRange(f.Aliases);
            }
            foreach (var s in cfg.SymbolLabels ?? new List<SymbolLabelRule>()) known.Add(s.Code);
            foreach (var m in cfg.SpanMarkers ?? new List<SpanMarkerRule>()) known.Add(m.Token);
            var modifiers = ReviewChecks.LineworkModifiers(points, cfg.LineworkCodes ?? new List<string>(), known, cfg.LineworkCommands);

            codeItems = codes.Count;
            modifierItems = modifiers.Count;
            return Report(db, tr, ReviewChecks.CodesSource, codes.Concat(modifiers), true, settings.Review);
        }

        /// <summary>The handle's entity, or null.</summary>
        public static ObjectId Resolve(Database db, string handle)
        {
            long value;
            if (string.IsNullOrWhiteSpace(handle) ||
                !long.TryParse(handle, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value)) return ObjectId.Null;
            ObjectId id;
            return db.TryGetObjectId(new Handle(value), out id) && !id.IsErased ? id : ObjectId.Null;
        }

        /// <summary>Zooms to an item and selects what it is about, so it shows highlighted.</summary>
        public static bool ZoomTo(Autodesk.AutoCAD.ApplicationServices.Document doc, ReviewItem item)
        {
            if (doc == null || item == null) return false;
            var db = doc.Database;
            var ed = doc.Editor;
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ids = new List<ObjectId>();
                Extents3d? box = null;
                foreach (var h in item.Handles ?? new List<string>())
                {
                    var id = Resolve(db, h);
                    if (id.IsNull) continue;
                    ids.Add(id);
                    var entity = tr.GetObject(id, OpenMode.ForRead, false, true) as AcEntity;
                    if (entity == null) continue;
                    try
                    {
                        var e = entity.GeometricExtents;
                        if (box == null) box = e; else { var b = box.Value; b.AddExtents(e); box = b; }
                    }
                    catch (Autodesk.AutoCAD.Runtime.Exception) { }
                }
                if (item.X.HasValue && item.Y.HasValue)
                {
                    var p = new Point3d(item.X.Value, item.Y.Value, 0);
                    var e = new Extents3d(new Point3d(p.X - 10, p.Y - 10, 0), new Point3d(p.X + 10, p.Y + 10, 0));
                    if (box == null) box = e; else { var b = box.Value; b.AddExtents(e); box = b; }
                }
                tr.Commit();
                if (box == null) return false;
                UtilityCadService.ZoomTo(ed, box.Value);
                if (ids.Count > 0) ed.SetImpliedSelection(ids.ToArray());
                return true;
            }
        }
    }

    public sealed class ReviewCommands
    {
        private static Ui.ReviewForm _form;

        /// <summary>The review list window: everything that needs a drafter, in one place.</summary>
        [CommandMethod("FTFREVIEW", CommandFlags.Modal)]
        public void FtfReview()
        {
            try
            {
                if (_form == null || _form.IsDisposed)
                {
                    _form = new Ui.ReviewForm();
                    AcadApp.ShowModelessDialog(_form);
                }
                else
                {
                    _form.Activate();
                }
                _form.Reload();
            }
            catch (System.Exception ex)
            {
                _form = null;
                var doc = AcadApp.DocumentManager.MdiActiveDocument;
                if (doc != null) doc.Editor.WriteMessage("\nFTFREVIEW: the window could not open: {0}\n", ex.Message);
            }
        }

        /// <summary>Rechecks everything the review list is built from: the codes (unknown codes,
        /// unreadable descriptions, invalid values, unrecognised linework words), linework
        /// continuity and point accountability. Never writes a point or a line of the survey: only
        /// the review list, the point ledger, the non-plotted proposal previews and the reports.</summary>
        [CommandMethod("FTFREVIEWCHECK", CommandFlags.Modal)]
        public void FtfReviewCheck()
        {
            FtfSession.Run("FTFREVIEWCHECK", (db, tr, ed) =>
            {
                var cfg = FtfSession.Rules(db);
                var settings = FtfSession.SettingsFor(db, cfg);
                var summary = PostRunChecks.Run(db, tr, cfg, settings, "FTFREVIEWCHECK");
                ed.WriteMessage("\nFTFREVIEWCHECK:");
                PostRunChecks.Print(ed, summary);
                var register = DrawingStore.ReadReview(db, tr);
                ed.WriteMessage("{0} open item(s), {1} required. FTFREVIEW lists them.\n", register.Open.Count, register.RequiredOpen.Count);
            });
            if (_form != null && !_form.IsDisposed) _form.Reload();
        }

        /// <summary>
        /// The review list on the command line (for scripts and the keyboard): List the open items,
        /// Approve a proposed correction, mark items NotApplicable or Accept them with a reason, Reopen
        /// them, or Zoom to one. Items are named by id; a trailing * names every id that starts so
        /// (point.duplicate:*), and several can be given separated by commas.
        /// </summary>
        [CommandMethod("-FTFREVIEW", CommandFlags.Modal)]
        public void FtfReviewCommandLine()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;
            var ask = new PromptKeywordOptions("\nReview [List/Approve/NotApplicable/Accept/Reopen/Zoom] <List>: ") { AllowNone = true };
            foreach (var k in new[] { "List", "Approve", "NotApplicable", "Accept", "Reopen", "Zoom" }) ask.Keywords.Add(k);
            ask.Keywords.Default = "List";
            var answer = ed.GetKeywords(ask);
            if (answer.Status != PromptStatus.OK && answer.Status != PromptStatus.None) return;
            var action = answer.Status == PromptStatus.None ? "List" : answer.StringResult;

            var which = Text(ed, action == "List" ? "\nShow items whose id starts with <all open>: " : "\nItem id(s) (a trailing * for all that start so): ", action == "List");
            if (which == null) return;
            string note = null;
            if (action == "Approve" || action == "NotApplicable" || action == "Accept")
            {
                note = Text(ed, action == "NotApplicable" ? "\nWhy does it not apply: " : "\nNote (recorded with your name): ", false);
                if (note == null) return;
            }

            ReviewItem zoom = null;
            FtfSession.Run("-FTFREVIEW", (db, tr, e) =>
            {
                var cfg = FtfSession.Rules(db);
                var settings = FtfSession.SettingsFor(db, cfg);
                var register = DrawingStore.ReadReview(db, tr);
                var by = LineworkCommands.Reviewer(settings.Review);
                var now = DateTime.Now;
                var ids = Match(register, which, action == "List" || action == "Approve" || action == "NotApplicable" || action == "Accept" || action == "Zoom");
                switch (action)
                {
                    case "List":
                        foreach (var i in ids.Take(300))
                            e.WriteMessage("\n  {0}{1}  {2}  [{3}]{4}", i.Required ? "* " : "  ", i.Id, ReviewRegister.Describe(i), i.Status,
                                           i.HasProposal && i.IsOpen ? " (correction proposed)" : string.Empty);
                        e.WriteMessage("\n{0} item(s); * = required before Drafting QC Complete.\n", ids.Count);
                        return;
                    case "Approve":
                        foreach (var i in ids)
                        {
                            string handle, problem;
                            if (LineCorrections.Approve(db, tr, cfg, register, i.Id, note, by, out handle, out problem))
                                e.WriteMessage("\n  {0}: correction drawn ({1}).", i.Id, handle);
                            else e.WriteMessage("\n  {0}: not approved -- {1}", i.Id, problem);
                        }
                        break;
                    case "NotApplicable":
                    {
                        string problem;
                        var n = register.MarkNotApplicable(ids.Select(i => i.Id), note, by, now, out problem);
                        e.WriteMessage("\n  {0} item(s) marked not applicable.{1}", n, problem == null ? string.Empty : " " + problem);
                        break;
                    }
                    case "Accept":
                        foreach (var i in ids)
                        {
                            string problem;
                            if (register.Accept(i.Id, note, by, now, out problem)) e.WriteMessage("\n  {0}: accepted.", i.Id);
                            else e.WriteMessage("\n  {0}: not accepted -- {1}", i.Id, problem);
                        }
                        break;
                    case "Reopen":
                        foreach (var i in ids) { register.Reopen(i.Id, by, now); e.WriteMessage("\n  {0}: reopened.", i.Id); }
                        break;
                    case "Zoom":
                        zoom = ids.FirstOrDefault();
                        return;
                }
                DrawingStore.WriteReview(db, tr, register);
                e.WriteMessage("\n{0} open, {1} required and open.\n", register.Open.Count, register.RequiredOpen.Count);
            });
            if (zoom != null && !ReviewStore.ZoomTo(doc, zoom)) ed.WriteMessage("\n{0} has no location in the drawing.\n", zoom.Id);
            if (_form != null && !_form.IsDisposed) _form.Reload();
        }

        private static string Text(Editor ed, string prompt, bool allowEmpty)
        {
            var o = new PromptStringOptions(prompt) { AllowSpaces = true };
            var r = ed.GetString(o);
            if (r.Status != PromptStatus.OK) return null;
            var t = (r.StringResult ?? string.Empty).Trim();
            if (t.Length == 0 && !allowEmpty) return null;
            return t;
        }

        /// <summary>Items named by id; "x*" names every id that starts with x; commas separate several.
        /// An empty pattern names every open item.</summary>
        internal static List<ReviewItem> Match(ReviewRegister register, string pattern, bool openOnly)
        {
            var found = new List<ReviewItem>();
            var parts = (pattern ?? string.Empty).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
            foreach (var i in register.Items)
            {
                if (openOnly && !i.IsOpen && parts.Count == 0) continue;
                var hit = parts.Count == 0 || parts.Any(p => p.EndsWith("*", StringComparison.Ordinal)
                    ? i.Id.StartsWith(p.Substring(0, p.Length - 1), StringComparison.OrdinalIgnoreCase)
                    : string.Equals(i.Id, p, StringComparison.OrdinalIgnoreCase));
                if (!hit) continue;
                if (openOnly && !i.IsOpen && parts.Any(p => p.EndsWith("*", StringComparison.Ordinal))) continue;
                found.Add(i);
            }
            return found;
        }

        /// <summary>
        /// Drafting QC Complete. Lists what is still open; marks the drawing complete only when no
        /// required item is (Settings > Review and QC). Undo takes the mark off.
        /// </summary>
        [CommandMethod("FTFQC", CommandFlags.Modal)]
        public void FtfQc()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;
            FtfSession.Run("FTFQC", (db, tr, edd) =>
            {
                var cfg = FtfSession.Rules(db);
                var settings = FtfSession.SettingsFor(db, cfg);
                // Never judged on a stale list: the checks run first, so a point or a line changed
                // since the last run cannot slip past Drafting QC Complete.
                var checks = PostRunChecks.Run(db, tr, cfg, settings, "FTFQC");
                ed.WriteMessage("\nFTFQC: checks rerun. {0}", checks.OneLine());
                var register = DrawingStore.ReadReview(db, tr);
                ed.WriteMessage("\nFTFQC: {0} open review item(s), {1} required.", register.Open.Count, register.RequiredOpen.Count);
                foreach (var item in register.RequiredOpen.Take(15))
                    ed.WriteMessage("\n  Required: {0} -- {1}", ReviewRegister.Describe(item), item.SuggestedAction);
                if (register.RequiredOpen.Count > 15) ed.WriteMessage("\n  ...");
                ed.WriteMessage(register.QcCompleteAt.HasValue
                    ? "\nDrafting QC Complete: marked " + register.QcCompleteAt.Value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " by " + register.QcCompleteBy + "."
                    : "\nDrafting QC Complete: not marked.");

                var options = new PromptKeywordOptions("\nMark Drafting QC Complete? [Complete/Undo/Exit] <Exit>: ");
                options.Keywords.Add("Complete");
                options.Keywords.Add("Undo");
                options.Keywords.Add("Exit");
                options.AllowNone = true;
                var answer = ed.GetKeywords(options);
                if (answer.Status != PromptStatus.OK || answer.StringResult == "Exit") return;
                if (answer.StringResult == "Undo")
                {
                    register.QcCompleteAt = null;
                    register.QcCompleteBy = null;
                    DrawingStore.WriteReview(db, tr, register);
                    ed.WriteMessage("\nFTFQC: Drafting QC Complete taken off.\n");
                    return;
                }
                string problem;
                if (!register.MarkQcComplete(settings.Review, LineworkCommands.Reviewer(settings.Review), DateTime.Now, out problem))
                {
                    ed.WriteMessage("\nFTFQC: not marked. {0} FTFREVIEW lists them.\n", problem);
                    return;
                }
                DrawingStore.WriteReview(db, tr, register);
                ed.WriteMessage("\nFTFQC: Drafting QC Complete marked by {0}.\n", register.QcCompleteBy);
            });
            if (_form != null && !_form.IsDisposed) _form.Reload();
        }

        internal static void ReloadWindow()
        {
            if (_form != null && !_form.IsDisposed) _form.Reload();
        }
    }
}
