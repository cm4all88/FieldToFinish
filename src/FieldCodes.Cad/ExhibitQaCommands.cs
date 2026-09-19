using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using FieldCodes.Easements;
using FieldCodes.Exhibits;
using FieldCodes.Settings;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcEntity = Autodesk.AutoCAD.DatabaseServices.Entity;

namespace FieldCodes.Cad
{
    /// <summary>
    /// FTFEXHIBITQA: a short pre-plot review of the current FTF exhibit, sorted into ERROR, REVIEW and
    /// INFO. It says whether the exhibit is ready for a surveyor's review -- never that it is approved
    /// or complete; the surveyor makes that call. FTFEXHIBITPREVIEW: a PDF of the exhibit, plotted with
    /// the profile's plotting standard, to look at.
    /// </summary>
    public sealed class ExhibitQaCommands
    {
        internal const string Error = "Error", Review = "Warning", Info = "";

        [CommandMethod("FTFEXHIBITQA", CommandFlags.Modal)]
        public void Qa()
        {
            List<InspectRow> rows = null;
            string title = null;
            FtfSession.Run("FTFEXHIBITQA", (db, tr, ed) =>
            {
                var settings = FtfSession.SettingsFor(db, FtfSession.Rules(db));
                var exhibit = PickExhibit(db, tr, ed, "FTFEXHIBITQA");
                if (exhibit == null) return;
                var records = DrawingStore.LoadEasements(db, tr);
                rows = Check(db, tr, exhibit, records, settings);
                var verdict = Verdict(rows);
                title = "EXHIBIT QA -- " + exhibit.LayoutName;
                exhibit.QaUtc = DateTime.UtcNow;
                exhibit.QaSummary = Counts(rows) + " -- " + verdict;
                DrawingStore.SaveExhibit(db, tr, exhibit);

                ed.WriteMessage("\n" + AsText(title, rows));
            });
            if (rows != null && !EasementInspectCommands.Headless())
                EasementInspectCommands.ShowWindow(title + ": " + Verdict(rows), rows);
        }

        [CommandMethod("FTFEXHIBITPREVIEW", CommandFlags.Modal)]
        public void Preview()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;
            string layout = null, device = null, media = null, styleSheet = null, file = null;
            var portrait = true;
            FtfSession.Run("FTFEXHIBITPREVIEW", (db, tr, ed2) =>
            {
                var settings = FtfSession.SettingsFor(db, FtfSession.Rules(db));
                var exhibit = PickExhibit(db, tr, ed2, "FTFEXHIBITPREVIEW");
                if (exhibit == null) return;
                var xs = ExhibitCommands.ExhibitProfile(exhibit.ProfileName, settings);
                var layouts = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
                if (!layouts.Contains(exhibit.LayoutName)) { ed2.WriteMessage("\nFTFEXHIBITPREVIEW: the layout \"" + exhibit.LayoutName + "\" is not in the drawing.\n"); return; }
                var lay = (Layout)tr.GetObject(layouts.GetAt(exhibit.LayoutName), OpenMode.ForRead);
                layout = exhibit.LayoutName;
                device = string.IsNullOrWhiteSpace(xs.PlotDevice) ? lay.PlotConfigurationName : xs.PlotDevice;
                var canonical = string.IsNullOrWhiteSpace(xs.MediaName) ? lay.CanonicalMediaName : xs.MediaName;
                styleSheet = string.IsNullOrWhiteSpace(xs.PlotStyleTable) ? lay.CurrentStyleSheet : xs.PlotStyleTable;
                try
                {
                    using (var ps = new PlotSettings(false))
                    {
                        ps.CopyFrom(lay);
                        var validator = PlotSettingsValidator.Current;
                        validator.SetPlotConfigurationName(ps, device, canonical);
                        media = validator.GetLocaleMediaName(ps, canonical);
                        // The sheet's drawing is laid out upright on the paper as the profile describes it.
                        portrait = xs.SheetHeightIn >= xs.SheetWidthIn;
                    }
                }
                catch (Autodesk.AutoCAD.Runtime.Exception ex)
                {
                    ed2.WriteMessage("\nFTFEXHIBITPREVIEW: the profile's plotter \"" + device + "\" with paper \"" + canonical + "\" is not available here (" + ex.Message + ").\n");
                    layout = null;
                    return;
                }
                var folder = Path.Combine(Path.GetTempPath(), "FTF Exhibit Preview");
                Directory.CreateDirectory(folder);
                var name = Path.GetFileNameWithoutExtension(db.Filename) + " - " + exhibit.LayoutName + " - " + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                file = Path.Combine(folder, new string(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray()) + ".pdf");
            });
            if (layout == null) return;

            var background = AcadApp.GetSystemVariable("BACKGROUNDPLOT");
            try
            {
                AcadApp.SetSystemVariable("BACKGROUNDPLOT", 0);
                // The page setup answers (orientation, plot area) come from the layout; the plotter, paper and
                // plot style table from the profile.
                ed.Command("_.-PLOT", "_Y", layout, device, media, "_I", portrait ? "_P" : "_L", "_N", "_L", "1:1", "0.00,0.00", "_Y",
                           string.IsNullOrWhiteSpace(styleSheet) ? "." : styleSheet, "_Y", "_N", "_N", "_N", file, "_N", "_Y");
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                ed.WriteMessage("\nFTFEXHIBITPREVIEW: the plot did not run: " + ex.Message + "\n");
                return;
            }
            finally
            {
                AcadApp.SetSystemVariable("BACKGROUNDPLOT", background);
            }

            if (!File.Exists(file)) { ed.WriteMessage("\nFTFEXHIBITPREVIEW: no PDF was written -- check the plotter \"" + device + "\".\n"); return; }
            ed.WriteMessage("\nFTFEXHIBITPREVIEW: " + file + "\n  This is a preview to look at. A clean plot does not mean the exhibit is correct.\n");
            if (!EasementInspectCommands.Headless()) OpenFile(file);
        }

        /// <summary>
        /// FTFEXHIBITSTAMP: the surveyor (or the drafter for them) names the stamp block to place on an exhibit whose
        /// profile places stamp blocks. FTF offers the blocks and places the one chosen at the profile's stamp place --
        /// it never picks a surveyor or a seal from the project, and "None" takes the stamp off again.
        /// </summary>
        [CommandMethod("FTFEXHIBITSTAMP", CommandFlags.Modal)]
        public void Stamp()
        {
            FtfSession.Run("FTFEXHIBITSTAMP", (db, tr, ed) =>
            {
                var rules = FtfSession.Rules(db);
                var settings = FtfSession.SettingsFor(db, rules);
                var exhibit = PickExhibit(db, tr, ed, "FTFEXHIBITSTAMP");
                if (exhibit == null) return;
                var xs = ExhibitCommands.ExhibitProfile(exhibit.ProfileName, settings);
                if (!string.Equals((xs.StampMode ?? string.Empty).Trim(), ExhibitSettings.StampBlock, StringComparison.OrdinalIgnoreCase))
                {
                    ed.WriteMessage("\nFTFEXHIBITSTAMP: the exhibit's profile places " +
                                    (string.Equals((xs.StampMode ?? string.Empty).Trim(), ExhibitSettings.StampPlaceholder, StringComparison.OrdinalIgnoreCase) ? "a stamp placeholder only" : "no stamp") +
                                    "; set the profile's stamp to Block to place a stamp block. Nothing changed.\n");
                    return;
                }
                var names = StampChoices(db, tr, xs);
                if (names.Count == 0) { ed.WriteMessage("\nFTFEXHIBITSTAMP: no stamp blocks found in " + (string.IsNullOrWhiteSpace(xs.StampLibrary) ? "this drawing" : xs.StampLibrary) + ". Nothing changed.\n"); return; }
                ed.WriteMessage("\n  Stamp blocks: " + string.Join(", ", names.ToArray()));
                var answer = ed.GetString(new PromptStringOptions("\nStamp block to place on " + exhibit.LayoutName + " (nothing is chosen for you), or None to take it off: ") { AllowSpaces = true });
                if (answer.Status != PromptStatus.OK || string.IsNullOrWhiteSpace(answer.StringResult)) { ed.WriteMessage("\nFTFEXHIBITSTAMP: nothing chosen; nothing changed.\n"); return; }
                var typed = answer.StringResult.Trim();
                if (string.Equals(typed, "None", StringComparison.OrdinalIgnoreCase)) exhibit.StampBlock = null;
                else
                {
                    var match = names.FirstOrDefault(n => string.Equals(n, typed, StringComparison.OrdinalIgnoreCase));
                    if (match == null) { ed.WriteMessage("\nFTFEXHIBITSTAMP: no stamp block \"" + typed + "\"; nothing changed.\n"); return; }
                    var confirm = EasementCommands.Keyword(ed, "\nPlace stamp block \"" + match + "\" on " + exhibit.LayoutName + "? The stamp remains the surveyor's responsibility [Yes/No] <No>: ", "No", "Yes", "No");
                    if (confirm != "Yes") { ed.WriteMessage("\nFTFEXHIBITSTAMP: not placed; nothing changed.\n"); return; }
                    exhibit.StampBlock = match;
                }
                var records = DrawingStore.LoadEasements(db, tr);
                var chosen = exhibit.Sources.Select(s => records.FirstOrDefault(r => r.Id == s.EasementId)).Where(r => r != null).ToList();
                var builder = new ExhibitBuilder(db, tr, ed, settings, xs, rules.Version, exhibit, chosen, exhibit.Items.ToList());
                if (!builder.Build(exhibit.ScaleChosenByUser ? exhibit.Scale : (double?)null)) return;
                exhibit.RebuiltUtc = DateTime.UtcNow;
                DrawingStore.SaveExhibit(db, tr, exhibit);
                ed.WriteMessage("\nFTFEXHIBITSTAMP: " + (exhibit.StampBlock == null ? "stamp taken off; the placeholder is shown." : "stamp block \"" + exhibit.StampBlock + "\" placed on " + exhibit.LayoutName + ".") +
                                " The surveyor checks and signs the exhibit.\n");
            });
        }

        /// <summary>Blocks a stamp can be chosen from: the profile's stamp library, or this drawing's own blocks.</summary>
        private static List<string> StampChoices(Database db, Transaction tr, ExhibitSettings xs)
        {
            Func<Database, Transaction, List<string>> read = (source, t) =>
            {
                var list = new List<string>();
                var blocks = (BlockTable)t.GetObject(source.BlockTableId, OpenMode.ForRead);
                foreach (ObjectId id in blocks)
                {
                    var b = (BlockTableRecord)t.GetObject(id, OpenMode.ForRead);
                    if (b.IsLayout || b.IsAnonymous || b.IsFromExternalReference || b.IsDependent || b.Name.StartsWith("*", StringComparison.Ordinal) ||
                        b.Name.StartsWith("FTF_", StringComparison.OrdinalIgnoreCase) || b.Name.StartsWith("A$", StringComparison.Ordinal)) continue;
                    list.Add(b.Name);
                }
                return list.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
            };
            if (string.IsNullOrWhiteSpace(xs.StampLibrary)) return read(db, tr);
            if (!File.Exists(xs.StampLibrary)) return new List<string>();
            try
            {
                using (var source = new Database(false, true))
                {
                    source.ReadDwgFile(xs.StampLibrary, FileShare.Read, true, string.Empty);
                    using (var st = source.TransactionManager.StartOpenCloseTransaction())
                        return read(source, st);
                }
            }
            catch (Autodesk.AutoCAD.Runtime.Exception) { return new List<string>(); }
            catch (IOException) { return new List<string>(); }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void OpenFile(string file)
        {
            try { System.Diagnostics.Process.Start(file); }
            catch (System.ComponentModel.Win32Exception) { }
        }

        /// <summary>The exhibit on the current layout, or one named at the prompt.</summary>
        internal static ExhibitRecord PickExhibit(Database db, Transaction tr, Editor ed, string command)
        {
            var exhibits = DrawingStore.LoadExhibits(db, tr);
            if (exhibits.Count == 0) { ed.WriteMessage("\n" + command + ": no FTF exhibits in this drawing.\n"); return null; }
            var current = exhibits.FirstOrDefault(x => string.Equals(x.LayoutName, LayoutManager.Current.CurrentLayout, StringComparison.OrdinalIgnoreCase));
            var fallback = current ?? exhibits[0];
            var answer = ed.GetString(new PromptStringOptions("\nExhibit layout (" + string.Join(", ", exhibits.Select(x => x.LayoutName).ToArray()) + ") <" + fallback.LayoutName + ">: ") { AllowSpaces = true });
            if (answer.Status == PromptStatus.Cancel) return null;
            var exhibit = answer.Status == PromptStatus.OK && !string.IsNullOrWhiteSpace(answer.StringResult)
                ? exhibits.FirstOrDefault(x => string.Equals(x.LayoutName, answer.StringResult.Trim(), StringComparison.OrdinalIgnoreCase))
                : fallback;
            if (exhibit == null) ed.WriteMessage("\n" + command + ": no exhibit layout by that name.\n");
            return exhibit;
        }

        // ================================================================ checks

        private const string Geometry = ExhibitQaReport.Geometry, SourceData = ExhibitQaReport.SourceData, LegalReproduction = ExhibitQaReport.LegalReproduction,
                             Drafting = ExhibitQaReport.Drafting, SheetPlot = ExhibitQaReport.SheetPlot, ManualReview = ExhibitQaReport.ManualReview;

        /// <summary>
        /// The QA rows. Each row's Section is its category -- GEOMETRY, SOURCE DATA, LEGAL REPRODUCTION (what can change
        /// survey content), DRAFTING, SHEET / PLOT, MANUAL REVIEW (cleanup and the surveyor's own actions) -- so it is
        /// plain which findings touch the survey and which are drafting.
        /// </summary>
        internal static List<InspectRow> Check(Database db, Transaction tr, ExhibitRecord x, IList<EasementRecord> records, FtfSettings settings)
        {
            var rows = new List<InspectRow>();
            Action<string, string, string, string, string> add = (severity, category, item, value, handle) =>
                rows.Add(new InspectRow { Section = category, Item = item, Value = value, Handle = handle, Layout = handle == null ? null : x.LayoutName, Severity = severity, Source = "FTFEXHIBITQA" });
            string profileProblem;
            var xs = ExhibitCommands.ExhibitProfile(x.ProfileName, settings, out profileProblem);
            var es = settings.Easements;
            var upf = settings.General.UnitsPerFoot;
            if (profileProblem != null) add(Review, SheetPlot, "Profile", profileProblem, null);

            // Source data: stale exhibit, survey changes, easements no longer stored.
            var shown = x.Sources.Select(s => records.FirstOrDefault(r => r.Id == s.EasementId)).ToList();
            foreach (var s in ExhibitPlanner.StaleSources(x, records)) add(Error, SourceData, "Exhibit out of date", s + " Run FTFEXHIBITREBUILD.", null);
            foreach (var missing in x.Sources.Where(s => records.All(r => r.Id != s.EasementId)))
                add(Error, SourceData, missing.Title, "no longer stored in the drawing", null);
            foreach (var r in shown.Where(r => r != null))
            {
                var changes = EasementCommands.Changes(db, tr, r);
                if (changes.Count > 0) add(Error, SourceData, r.Title, "not current with the survey: " + changes[0] + (changes.Count > 1 ? " (+" + (changes.Count - 1) + " more)" : string.Empty) + " Run FTFEASEMENTCHECK.", null);
                else add(Info, SourceData, r.Title, "current with the survey", null);
                if (r.LotLines != null && r.LotLines.Count > 0)
                    add(Info, SourceData, r.Title, "lot built by FTF from " + r.LotLines.Count + " separate lot lines (checked closed; the lines are unchanged)", null);
                var tie = r.TieCourses();
                if (Ties.Follows(tie))
                    add(Info, SourceData, r.Title, "commencement tie follows the drawn line: " + tie.Count + " courses" + (tie.Any(t => t.Course.Kind == CourseKind.Arc) ? ", including a curve" : string.Empty) + " (not a chord)", null);

                // Geometry (the CAD boundary itself) and legal reproduction (the stated courses reproduce it).
                var closure = Closure.ForRecord(r, es);
                foreach (var item in closure.Items.Where(i => i.Problems.Count > 0))
                    add(Error, item.Cad != null ? Geometry : LegalReproduction, r.Title, item.Name + ": " + item.Problems.First(), null);
                if (closure.Items.Any(i => i.Cad != null) && closure.Items.Where(i => i.Cad != null).All(i => i.Problems.Count == 0))
                    add(Info, Geometry, r.Title, "boundary closes", null);
                if (closure.Ready) add(Info, LegalReproduction, r.Title, "stated courses reproduce the CAD easement", null);

                if (r.LegalStatus != null && r.LegalStatus.Contains("NOT READY")) add(Error, LegalReproduction, r.Title, r.LegalStatus, null);
                else if (r.LegalStatus != null && r.LegalStatus.Contains("OUT OF DATE")) add(Review, LegalReproduction, r.Title, r.LegalStatus, null);
                else if (r.Legal == null) add(Info, LegalReproduction, r.Title, "no legal draft written yet (FTFEASEMENTLEGAL)", null);
                else
                {
                    var blanks = LegalBlanks(r, records, es);
                    if (blanks.Count > 0) add(Review, LegalReproduction, r.Title, "unresolved blank(s) in the legal draft: " + string.Join("; ", blanks.ToArray()), null);
                    else add(Info, LegalReproduction, r.Title, r.LegalStatus ?? "written", null);
                }

                if (r.ComponentsOverlap)
                    add(Review, Geometry, r.Title, "components overlap: SUM OF COMPONENT AREAS " + r.AreaSquareFeet.ToString("N0", CultureInfo.InvariantCulture) + " SF, TOTAL PHYSICAL AREA " +
                        (r.PhysicalAreaSquareFeet.HasValue ? r.PhysicalAreaSquareFeet.Value.ToString("N0", CultureInfo.InvariantCulture) + " SF" : "not worked out") + " -- which the description states is the surveyor's decision", null);
                foreach (var ex in r.Exclusions ?? new List<Exclusion>())
                    add(ex.Effect == "NONE" ? Review : Info, Geometry, r.Title,
                        "exclusion " + (ex.Description ?? ex.Kind.ToLowerInvariant()) + ": " + (ex.Effect == "NONE" ? "does not touch the easement" : ex.RemovedSquareFeet.ToString("N0", CultureInfo.InvariantCulture) + " SF removed (" + (ex.Effect == "HOLE" ? "hole" : "cut") + ")"), null);
            }

            // Sheet: the information the profile expects.
            var tokens = x.Info.Tokens();
            foreach (var field in ExhibitSettings.Split(xs.RequiredInfo))
            {
                var token = "{" + field.Trim('{', '}') + "}";
                string value;
                var known = tokens.Keys.FirstOrDefault(k => string.Equals(k, token, StringComparison.OrdinalIgnoreCase));
                if (known == null) { add(Review, SheetPlot, field, "not an exhibit field FTF knows -- check the profile's required information", null); continue; }
                tokens.TryGetValue(known, out value);
                if (string.IsNullOrWhiteSpace(value)) add(Error, SheetPlot, field, "required by the profile and empty", null);
            }
            if (string.IsNullOrWhiteSpace(x.Info.Purpose)) add(Review, SheetPlot, "purpose", "the exhibit does not name the easement type", null);

            // The sheet as it is now: generated items present, edited, moved, erased.
            var owned = Ownership.FindOwnedInLayouts(db, tr, x.LayoutName, s => s.PointNumber == x.Id).ToDictionary(kv => kv.Key.Handle.ToString(), kv => kv.Key);
            var present = new Dictionary<string, AcEntity>();
            foreach (var item in x.Items)
            {
                ObjectId id;
                if (item.Handle == null || !owned.TryGetValue(item.Handle, out id)) { add(Review, Drafting, Describe(item, records), "generated object erased from the sheet", null); continue; }
                var entity = (AcEntity)tr.GetObject(id, OpenMode.ForRead);
                present[item.Key] = entity;
                if (ExhibitCommands.Edited(entity, item)) add(Review, ManualReview, Describe(item, records), "generated text edited by hand -- check it still agrees with the easement", item.Handle);
            }
            foreach (var conflict in x.Review.Where(r => r.Message.StartsWith("CONFLICT", StringComparison.Ordinal)))
                add(Error, ManualReview, "Conflict", conflict.Message, null);

            Func<string, bool> has = key => present.ContainsKey(key);
            if (xs.DrawNorthArrow && !has("NORTH") && string.IsNullOrWhiteSpace(xs.ScaleBarBlock)) add(Error, SheetPlot, "North arrow", "the profile draws one and it is missing", null);
            if (xs.DrawScaleBar && !has("SCALEBAR")) add(Error, SheetPlot, "Scale", "the profile shows the scale and it is missing", null);
            if (xs.DrawTitle && !has("TITLE")) add(Error, SheetPlot, "Title", "the exhibit title is missing", null);
            var arrowShown = (xs.DrawNorthArrow && has("NORTH")) || (!string.IsNullOrWhiteSpace(xs.ScaleBarBlock) && has("SCALEBAR"));
            if (arrowShown)
            {
                var turned = Math.Abs(x.RotationDegrees) > 1e-9;
                if (x.NorthArrowVerified)
                    add(Info, Drafting, "North arrow", turned ? "turned " + x.RotationDegrees.ToString("0.#", CultureInfo.InvariantCulture) + " degrees with the view and checked" : "north up", null);
                else if (turned)
                    add(Error, Drafting, "North arrow", "the view is turned " + x.RotationDegrees.ToString("0.#", CultureInfo.InvariantCulture) + " degrees and the north arrow was not shown to point north", null);
            }
            foreach (var r in shown.Where(r => r != null))
            {
                if (xs.DrawPointLabels)
                {
                    if (r.PointOfCommencement != null && r.CommencementTie != null && !r.IsTemporary && !has("POINT:" + r.Id + ":POC"))
                        add(Error, Drafting, r.Title, "no POINT OF COMMENCEMENT leader", null);
                    if (!r.IsTemporary && (r.RouteCourses ?? new List<CourseData>()).Count > 0 && !has("POINT:" + r.Id + ":POB"))
                        add(Error, Drafting, r.Title, "no POINT OF BEGINNING leader", null);
                }
                var areaShown = has("AREA:" + r.Id) ||
                                (present.ContainsKey("AREATABLE") && (ExhibitCommands.TextOf(present["AREATABLE"]) ?? string.Empty).Contains(r.DisplayAreaSquareFeet.ToString("N0", CultureInfo.InvariantCulture))) ||
                                (present.ContainsKey("LEGEND") && (x.Items.First(i => i.Key == "LEGEND").Text ?? string.Empty).Contains(r.DisplayAreaSquareFeet.ToString("N0", CultureInfo.InvariantCulture)));
                if (!areaShown) add(Error, Drafting, r.Title, "the easement's area is not shown anywhere on the sheet", null);
            }

            // The viewport: scale, fit, layers, hatch.
            var vp = x.ViewportHandle == null ? null : EasementCommands.Resolve(db, tr, x.ViewportHandle) as Viewport;
            if (vp == null) add(Error, SheetPlot, "Viewport", "the exhibit viewport is missing", null);
            else
            {
                var actual = 1.0 / vp.CustomScale / upf;
                if (Math.Abs(actual - x.Scale) > x.Scale * 1e-6) add(Review, SheetPlot, "Scale", "the viewport is at 1\" = " + actual.ToString("0.##", CultureInfo.InvariantCulture) + "', not the exhibit's 1\" = " + x.Scale.ToString("0.##", CultureInfo.InvariantCulture) + "'", x.ViewportHandle);
                else if (!xs.ScaleList().Any(s => Math.Abs(s - actual) < 1e-6)) add(Review, SheetPlot, "Scale", "1\" = " + actual.ToString("0.##", CultureInfo.InvariantCulture) + "' is not one of the profile's scales", x.ViewportHandle);
                else add(Info, SheetPlot, "Scale", "1\" = " + actual.ToString("0.##", CultureInfo.InvariantCulture) + "'" + (Math.Abs(x.RotationDegrees) > 1e-9 ? ", view turned " + x.RotationDegrees.ToString("0.#", CultureInfo.InvariantCulture) + " degrees" : ", north up"), x.ViewportHandle);
                if (!vp.Locked && xs.LockViewport) add(Review, SheetPlot, "Lock", "the viewport is not locked", x.ViewportHandle);
                var frozen = vp.GetFrozenLayers().Cast<ObjectId>().Select(id => ((LayerTableRecord)tr.GetObject(id, OpenMode.ForRead)).Name).OrderBy(n => n).ToList();
                add(Info, SheetPlot, "Hidden layers", frozen.Count == 0 ? "none" : string.Join(", ", frozen.ToArray()), x.ViewportHandle);
                if (x.UserLayers != null && x.UserLayers.Count > 0) add(Review, ManualReview, "Layers set by hand", string.Join(", ", x.UserLayers.ToArray()) + " -- FTF leaves these as set", x.ViewportHandle);
                HatchSpacing(db, tr, shown.Where(r => r != null).ToList(), actual * upf, add);
            }
            foreach (var note in x.Review.Where(n => n.ItemKey == "VIEWPORT" && n.Message.Contains("does not fit")))
                add(Error, SheetPlot, "Fit", note.Message, x.ViewportHandle);
            foreach (var note in x.Review.Where(n => n.ItemKey == "VIEWPORT" && (n.Message.Contains("object(s) of easements not on this exhibit") || n.Message.Contains("annotative label(s) in view") || n.Message.Contains("its hatch is one object"))))
                add(Review, Drafting, "Layers", note.Message, x.ViewportHandle);
            foreach (var note in x.Review.Where(n => n.Message.Contains("hatch pattern scale")))
                add(Info, ManualReview, "Hatch", note.Message, null);
            foreach (var note in x.Review.Where(n => n.Message.Contains("width dimension is drawn")))
                add(Review, Drafting, "Width dimension", note.Message, null);

            // Collisions and printable area, measured on the sheet as it is now.
            if (vp != null)
            {
                var boxes = new List<SheetBox>();
                foreach (var item in x.Items)
                {
                    AcEntity entity;
                    if (item.Kind == "VIEWPORT" || !present.TryGetValue(item.Key, out entity)) continue;
                    try
                    {
                        var e = entity.GeometricExtents;
                        var box = new SheetBox { Key = item.Key, Kind = item.Kind, Description = Describe(item, records), Rect = new SheetRect(e.MinPoint.X, e.MinPoint.Y, e.MaxPoint.X, e.MaxPoint.Y) };
                        var turned = entity as MText;
                        if (turned != null && turned.Attachment == AttachmentPoint.MiddleCenter && Math.Abs(Math.Sin(2 * turned.Rotation)) > 1e-6)
                            box.Corners = ExhibitReview.TurnedBox(new P2(turned.Location.X, turned.Location.Y), turned.Rotation, turned.ActualWidth, turned.ActualHeight);
                        boxes.Add(box);
                    }
                    catch (Autodesk.AutoCAD.Runtime.Exception) { }
                }
                var vpRect = new SheetRect(vp.CenterPoint.X - vp.Width / 2, vp.CenterPoint.Y - vp.Height / 2, vp.CenterPoint.X + vp.Width / 2, vp.CenterPoint.Y + vp.Height / 2);
                var framePlots = ((LayerTableRecord)tr.GetObject(vp.LayerId, OpenMode.ForRead)).IsPlottable;
                var printable = new SheetRect(xs.MarginIn, xs.MarginIn, xs.SheetWidthIn - xs.MarginIn, xs.SheetHeightIn - xs.MarginIn);
                var lines = new List<Tuple<P2, P2>>();
                var center = new P2(vp.CenterPoint.X, vp.CenterPoint.Y);
                foreach (var r in shown.Where(r => r != null))
                    foreach (var loop in new[] { r.BoundaryCourses }.Concat(r.Holes ?? new List<List<CourseData>>()).Where(l => l != null))
                        foreach (var d in loop)
                        {
                            var steps = d.Course.Kind == CourseKind.Arc ? Math.Max(2, (int)Math.Ceiling(d.Course.Sweep / (Math.PI / 18))) : 1;
                            for (var i = 0; i < steps; i++)
                                lines.Add(Tuple.Create(ExhibitPlanner.ToPaper(d.Course.PointAt(d.Course.Length * i / steps), x.ViewCenter, x.Scale, x.RotationDegrees, center, upf),
                                                       ExhibitPlanner.ToPaper(d.Course.PointAt(d.Course.Length * (i + 1) / steps), x.ViewCenter, x.Scale, x.RotationDegrees, center, upf)));
                        }
                var findings = ExhibitReview.Check(boxes, vpRect, printable, lines, null, framePlots).ToList();
                // When the view does not fit, everything cut off by the viewport edge is one problem, already an error above.
                var cutOff = findings.Where(f => f.Message.EndsWith("runs outside the viewport.", StringComparison.Ordinal)).ToList();
                if (cutOff.Count > 3 && x.Review.Any(n => n.ItemKey == "VIEWPORT" && n.Message.Contains("does not fit")))
                {
                    add(Review, Drafting, "Viewport edge", cutOff.Count + " labels and leaders run outside the viewport because the view does not fit at this scale: " +
                        string.Join(", ", cutOff.Select(f => f.ItemKey == null ? "sheet" : Describe(x.Items.FirstOrDefault(i => i.Key == f.ItemKey) ?? new ExhibitItem { Key = f.ItemKey }, records)).Distinct().Take(6).ToArray()) +
                        (cutOff.Count > 6 ? ", ..." : string.Empty), null);
                    findings = findings.Except(cutOff).ToList();
                }
                foreach (var finding in findings)
                {
                    var outside = finding.Message.Contains("printable area");
                    var isTable = finding.ItemKey != null && finding.ItemKey.EndsWith("TABLE", StringComparison.Ordinal);
                    add(outside && (finding.Severity == "Error" || isTable) ? Error : Review, outside ? SheetPlot : Drafting, finding.ItemKey ?? "Sheet", finding.Message,
                        finding.ItemKey != null && x.Items.Any(i => i.Key == finding.ItemKey) ? x.Items.First(i => i.Key == finding.ItemKey).Handle : null);
                }
            }

            // Plotting standard and the files the profile relies on.
            var layouts = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
            if (!layouts.Contains(x.LayoutName)) add(Error, SheetPlot, "Layout", "the exhibit layout is missing", null);
            else
            {
                var lay = (Layout)tr.GetObject(layouts.GetAt(x.LayoutName), OpenMode.ForRead);
                if (!string.IsNullOrWhiteSpace(xs.PlotDevice) && !string.Equals(lay.PlotConfigurationName, xs.PlotDevice, StringComparison.OrdinalIgnoreCase))
                    add(Review, SheetPlot, "Plotter", "the layout plots to \"" + lay.PlotConfigurationName + "\"; the profile uses \"" + xs.PlotDevice + "\"", null);
                if (!string.IsNullOrWhiteSpace(xs.MediaName) && !string.Equals(lay.CanonicalMediaName, xs.MediaName, StringComparison.OrdinalIgnoreCase))
                    add(Error, SheetPlot, "Paper", "the layout's paper is \"" + lay.CanonicalMediaName + "\"; the profile's is \"" + xs.MediaName + "\"", null);
                var mm = lay.PlotPaperSize;
                var inches = new[] { mm.X / 25.4, mm.Y / 25.4 }.OrderBy(v => v).ToArray();
                var want = new[] { xs.SheetWidthIn, xs.SheetHeightIn }.OrderBy(v => v).ToArray();
                if (Math.Abs(inches[0] - want[0]) > 0.05 || Math.Abs(inches[1] - want[1]) > 0.05)
                    add(Error, SheetPlot, "Paper size", inches[0].ToString("0.##", CultureInfo.InvariantCulture) + " x " + inches[1].ToString("0.##", CultureInfo.InvariantCulture) + " in, but the sheet is laid out for " + want[0].ToString("0.##", CultureInfo.InvariantCulture) + " x " + want[1].ToString("0.##", CultureInfo.InvariantCulture), null);
                else add(Info, SheetPlot, "Paper size", inches[0].ToString("0.##", CultureInfo.InvariantCulture) + " x " + inches[1].ToString("0.##", CultureInfo.InvariantCulture) + " in, " + lay.PlotConfigurationName, null);
                if (!string.IsNullOrWhiteSpace(xs.PlotStyleTable) && !string.Equals(lay.CurrentStyleSheet, xs.PlotStyleTable, StringComparison.OrdinalIgnoreCase))
                    add(Review, SheetPlot, "Plot style table", "the layout uses \"" + lay.CurrentStyleSheet + "\"; the profile uses \"" + xs.PlotStyleTable + "\"", null);
            }
            foreach (var f in new[] { Tuple.Create("Template drawing", xs.TemplateFile), Tuple.Create("Block library", xs.BlockLibrary), Tuple.Create("Title block drawing", xs.TitleBlockPath), Tuple.Create("Stamp library", xs.StampLibrary) })
                if (!string.IsNullOrWhiteSpace(f.Item2) && !File.Exists(f.Item2)) add(Review, SheetPlot, f.Item1, f.Item2 + " cannot be found from this computer", null);
            foreach (var style in OfficeStylesMissing(db, tr, xs))
                add(Review, Drafting, "Office style", style, null);
            foreach (var block in new[] { xs.TitleBlockName, xs.ScaleBarBlock, xs.DrawNorthArrow ? xs.NorthArrowBlock : null }.Concat(xs.SheetBlockList().Select(b => b.Name)).Where(n => !string.IsNullOrWhiteSpace(n)))
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                if (!bt.Has(block)) add(Review, SheetPlot, "Block " + block, "not in the drawing (the exhibit could not place it)", null);
            }

            // What is left to people, not FTF.
            foreach (var note in x.Review.Where(n => n.Message.Contains("attributes left as the block defines them")))
                add(Info, ManualReview, "Title block attributes", note.Message, null);
            foreach (var note in x.Review.Where(n => n.Message.Contains(" was not filled: ") || n.Message.Contains("shows the block's own value")))
                add(Review, ManualReview, "Title block attributes", note.Message, null);
            foreach (var note in x.Review.Where(n => n.Message.Contains("keeps the value typed by hand") || n.Message.Contains("stays where it was moved by hand")))
                add(Info, ManualReview, "Kept from the drafter", note.Message, null);
            foreach (var note in x.Review.Where(n => n.Message.Contains("was moved by hand, but it now reads")))
                add(Review, ManualReview, "Moved label", note.Message, null);
            var stampMode = (xs.StampMode ?? ExhibitSettings.StampNone).Trim();
            if (!string.Equals(stampMode, ExhibitSettings.StampNone, StringComparison.OrdinalIgnoreCase))
                add(Info, ManualReview, "Surveyor stamp", has("STAMP") && !string.IsNullOrWhiteSpace(x.StampBlock) && string.Equals(stampMode, ExhibitSettings.StampBlock, StringComparison.OrdinalIgnoreCase)
                    ? "stamp block \"" + x.StampBlock + "\" placed as chosen with FTFEXHIBITSTAMP -- the surveyor's to check and sign"
                    : has("STAMP") ? "placeholder only -- the surveyor places the stamp" : "no stamp place on the sheet (erased by hand)", null);
            add(Info, ManualReview, "Surveyor review", "legal descriptions and exhibits are drafts until the surveyor reviews them; FTF does not approve them", null);
            return rows;
        }

        /// <summary>
        /// The office styles the profile names that this drawing does not have. FTF never makes its own copy of an office
        /// style; without it the exhibit falls back to the drawing's current style, which the drafter should know.
        /// </summary>
        internal static List<string> OfficeStylesMissing(Database db, Transaction tr, ExhibitSettings xs)
        {
            var missing = new List<string>();
            foreach (var text in new[] { xs.TextStyle, xs.TitleTextStyle }.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase))
                if (Setup.DrawingResources.FindTextStyle(db, tr, text).IsNull)
                    missing.Add("text style \"" + text + "\" is not in this drawing; the exhibit used the current text style");
            if (!string.IsNullOrWhiteSpace(xs.DimensionStyle) && !((DimStyleTable)tr.GetObject(db.DimStyleTableId, OpenMode.ForRead)).Has(xs.DimensionStyle))
                missing.Add("dimension style \"" + xs.DimensionStyle + "\" is not in this drawing; width dimensions used the current dimension style");
            if (!string.IsNullOrWhiteSpace(xs.LeaderStyle) && !((DBDictionary)tr.GetObject(db.MLeaderStyleDictionaryId, OpenMode.ForRead)).Contains(xs.LeaderStyle))
                missing.Add("multileader style \"" + xs.LeaderStyle + "\" is not in this drawing; POB/POC leaders used the current multileader style");
            if (!string.IsNullOrWhiteSpace(xs.TableStyle) && !((DBDictionary)tr.GetObject(db.TableStyleDictionaryId, OpenMode.ForRead)).Contains(xs.TableStyle))
                missing.Add("table style \"" + xs.TableStyle + "\" is not in this drawing; line/curve tables used the current table style");
            return missing;
        }

        /// <summary>The smallest distance between an easement hatch's pattern lines, drawing units; null for a hatch without lines.</summary>
        internal static double? LineSpacing(Hatch hatch)
        {
            double? spacing = null;
            for (var i = 0; i < hatch.NumberOfPatternDefinitions; i++)
            {
                var d = hatch.GetPatternDefinitionAt(i);
                // Pattern offsets are stored scaled and turned into the drawing; the line spacing is the offset across the line.
                var across = Math.Abs(-Math.Sin(d.Angle) * d.OffsetX + Math.Cos(d.Angle) * d.OffsetY);
                if (across > 1e-9 && (!spacing.HasValue || across < spacing.Value)) spacing = across;
            }
            return spacing;
        }

        /// <summary>How far apart each easement hatch's lines print at the viewport scale: readable, nearly solid, or too open.</summary>
        internal static void HatchSpacing(Database db, Transaction tr, IList<EasementRecord> shown, double unitsPerPaperInch, Action<string, string, string, string, string> add)
        {
            var ids = new HashSet<string>(shown.Select(r => r.Id));
            foreach (var kv in Ownership.FindOwned(db, tr, s => ids.Contains(s.PointNumber ?? string.Empty)))
            {
                var hatch = tr.GetObject(kv.Key, OpenMode.ForRead) as Hatch;
                if (hatch == null || hatch.PatternType == HatchPatternType.PreDefined && string.Equals(hatch.PatternName, "SOLID", StringComparison.OrdinalIgnoreCase)) continue;
                var spacing = LineSpacing(hatch);
                if (!spacing.HasValue) continue;
                var printed = spacing.Value / unitsPerPaperInch;
                var record = shown.First(r => r.Id == kv.Value.PointNumber);
                var text = hatch.PatternName + " lines print " + printed.ToString("0.000", CultureInfo.InvariantCulture) + "\" apart";
                if (printed < 0.02) add(Review, Drafting, record.Title + " hatch", text + " -- dense enough to print nearly solid at this scale; the surveyor decides whether to change the hatch scale", null);
                else if (printed > 0.5) add(Review, Drafting, record.Title + " hatch", text + " -- open enough that a narrow easement may show few or no hatch lines at this scale", null);
                else add(Info, Drafting, record.Title + " hatch", text, null);
            }
        }

        /// <summary>The bracketed blanks a legal draft written now from the stored names would still have.</summary>
        private static List<string> LegalBlanks(EasementRecord r, IList<EasementRecord> records, EasementSettings es)
        {
            var temporary = r.IsPortion || r.IsArea || r.GroupId == null ? null : records.FirstOrDefault(t => t.IsTemporary && t.GroupId == r.GroupId && t.Id != r.Id);
            LegalDraft draft;
            try
            {
                draft = r.IsPortion ? LegalDescriptionWriter.WritePortion(r, r.Legal, es, null)
                      : r.IsArea ? LegalDescriptionWriter.WriteArea(r, r.Legal, es, null)
                      : r.IsTemporary ? null : LegalDescriptionWriter.Write(r, temporary, r.Legal, es, null);
            }
            catch (InvalidOperationException) { return new List<string>(); }
            if (draft == null) return new List<string>();
            var blanks = new List<string>();
            var text = draft.Text ?? string.Empty;
            for (var i = text.IndexOf('['); i >= 0; i = text.IndexOf('[', i + 1))
            {
                var end = text.IndexOf(']', i);
                if (end < 0) break;
                var blank = text.Substring(i, end - i + 1);
                if (!blanks.Contains(blank)) blanks.Add(blank);
            }
            return blanks;
        }

        private static string Describe(ExhibitItem item, IList<EasementRecord> records)
        {
            var parts = (item.Key ?? string.Empty).Split(':');
            if (parts.Length < 2 || parts[1].Length != 32) return item.Key;
            var r = records.FirstOrDefault(q => q.Id == parts[1]);
            return parts[0] + (parts.Length > 2 ? " " + parts[2] : string.Empty) + " -- " + (r == null ? "easement no longer stored" : r.Title);
        }

        internal static string Verdict(IList<InspectRow> rows)
        {
            return ExhibitQaReport.Verdict(Lines(rows));
        }

        internal static string Counts(IList<InspectRow> rows)
        {
            return ExhibitQaReport.Counts(Lines(rows));
        }

        internal static string AsText(string title, IList<InspectRow> rows)
        {
            return ExhibitQaReport.Format(title, Lines(rows));
        }

        private static List<QaLine> Lines(IEnumerable<InspectRow> rows)
        {
            return rows.Select(r => new QaLine { Severity = r.Severity, Category = r.Section, Item = r.Item, Value = r.Value }).ToList();
        }
    }
}
