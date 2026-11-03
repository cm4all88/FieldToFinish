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

        /// <summary>Rechecks the codes (read-only): unknown codes, unreadable descriptions, invalid
        /// values, unrecognised linework words. Only the review list changes.</summary>
        [CommandMethod("FTFREVIEWCHECK", CommandFlags.Modal)]
        public void FtfReviewCheck()
        {
            FtfSession.Run("FTFREVIEWCHECK", (db, tr, ed) =>
            {
                var cfg = FtfSession.Rules(db);
                var settings = FtfSession.SettingsFor(db, cfg);
                int codes, modifiers;
                var added = ReviewStore.CheckCodes(db, tr, cfg, settings, out codes, out modifiers);
                var register = DrawingStore.ReadReview(db, tr);
                ed.WriteMessage("\nFTFREVIEWCHECK: {0} code item(s), {1} linework word item(s); {2} new. " +
                                "{3} open item(s), {4} required. FTFREVIEW lists them.\n",
                                codes, modifiers, added, register.Open.Count, register.RequiredOpen.Count);
            });
            if (_form != null && !_form.IsDisposed) _form.Reload();
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
