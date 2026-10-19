using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using FieldCodes.Compare;
using FieldCodes.Settings;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace FieldCodes.Cad.Compare
{
    /// <summary>Stage messages on the command line, a progress meter for long loops,
    /// and the user's Esc as cancellation.</summary>
    internal sealed class EditorProgress : ICompareProgress, IDisposable
    {
        private readonly Editor _ed;
        private ProgressMeter _meter;
        private string _stage;

        public EditorProgress(Editor ed) { _ed = ed; }

        public void Stage(string name)
        {
            _stage = name;
            if (_ed != null) _ed.WriteMessage("\n  {0}...", name);
            if (_meter != null) { _meter.Stop(); _meter.Dispose(); _meter = null; }
        }

        public void Step(int done, int total)
        {
            if (_meter == null)
            {
                _meter = new ProgressMeter();
                _meter.Start(_stage ?? "FTFCOMPARE");
                _meter.SetLimit(total > 0 ? total : 1000000);
            }
            try { _meter.MeterProgress(); } catch (Autodesk.AutoCAD.Runtime.Exception) { }
        }

        public bool IsCancelled
        {
            get { try { return HostApplicationServices.Current.UserBreak(); } catch (Autodesk.AutoCAD.Runtime.Exception) { return false; } }
        }

        public void Dispose()
        {
            if (_meter != null) { _meter.Stop(); _meter.Dispose(); _meter = null; }
        }
    }

    /// <summary>
    /// The comparison state for the active drawing, and the service the window and the
    /// commands share. The window never touches the database: it posts work and
    /// FTFCOMPAREACT runs it inside a real command (one undo each), exactly as the Dip
    /// Builder does. What must travel with the DWG (the applied record, decisions, the
    /// chosen profile and source) is stored in the drawing; the full result sits in a
    /// JSON file beside it.
    ///
    /// UNTESTED in Civil 3D.
    /// </summary>
    internal static class CompareSession
    {
        public const string ResultSuffix = ".ftf-compare-result.json";
        private const string AppliedKey = "APPLIED";
        private const string DecisionsKey = "DECISIONS";
        private const string ProfileKey = "PROFILE";
        private const string SourceKey = "SOURCE";

        public static CompareProfile Profile;
        public static string ProfileLevel;
        public static ComparisonResult Result;
        public static ReviewDecisionSet Decisions = new ReviewDecisionSet();
        public static AppliedChangeSet Applied;
        public static ChangePlan Plan;
        public static ConsultantSourceSpec Source = new ConsultantSourceSpec();
        public static IList<QcCheck> LastQc;
        public static Dictionary<string, ConsultantShape> Shapes = new Dictionary<string, ConsultantShape>(StringComparer.Ordinal);
        public static IntPtr DatabaseKey = IntPtr.Zero;
        public static Ui.CompareReviewForm Form;
        public static string LastMessage;
        public static string LastSummary;

        public delegate bool CompareWork(Database db, Transaction tr, Editor ed);
        private static readonly Queue<KeyValuePair<string, CompareWork>> Pending = new Queue<KeyValuePair<string, CompareWork>>();

        // ================================================================== queue

        public static bool Post(string name, CompareWork work) { return Post(name, work, false); }

        public static bool Post(string name, CompareWork work, bool fromOwnCommand)
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return false;
            if (!fromOwnCommand && !string.IsNullOrEmpty(doc.CommandInProgress))
            {
                LastMessage = "Finish or cancel the command in progress (" + doc.CommandInProgress + ") first.";
                RefreshForm();
                return false;
            }
            Pending.Enqueue(new KeyValuePair<string, CompareWork>(name, work));
            doc.SendStringToExecute("FTFCOMPAREACT ", true, false, true);
            return true;
        }

        public static void RunPending()
        {
            if (Pending.Count == 0) return;
            var next = Pending.Dequeue();
            LastMessage = null;
            PendingSavePath = null;
            FtfSession.Run("FTFCOMPARE " + next.Key, (db, tr, ed) =>
            {
                EnsureLoaded(db, tr);
                next.Value(db, tr, ed);
            });
            if (FtfSession.LastRunError != null) LastMessage = "Failed: " + FtfSession.LastRunError;
            else if (PendingSavePath != null) CompleteSave();
            RefreshForm();
        }

        /// <summary>Set by PrepareDeliverable once QC passed; the DWG copy is written after the
        /// command's transaction has committed, never inside it.</summary>
        public static string PendingSavePath;

        public static void CompleteSave()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            var path = PendingSavePath;
            PendingSavePath = null;
            if (doc == null || string.IsNullOrEmpty(path)) return;
            try
            {
                using (doc.LockDocument())
                {
                    // A copy, not a rename: the working drawing stays the working drawing.
                    doc.Database.SaveAs(path, false, DwgVersion.Current, doc.Database.SecurityParameters);
                }
                if (!File.Exists(path)) throw new IOException("the file was not written");
                LastMessage = "Deliverable written: " + path;
                doc.Editor.WriteMessage("\nFTFCOMPARESAVE: deliverable written to {0}\n", path);
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex) { LastMessage = "Deliverable NOT written: " + ex.Message; doc.Editor.WriteMessage("\nFTFCOMPARESAVE failed: {0}\n", ex.Message); }
            catch (IOException ex) { LastMessage = "Deliverable NOT written: " + ex.Message; doc.Editor.WriteMessage("\nFTFCOMPARESAVE failed: {0}\n", ex.Message); }
        }

        public static void RefreshForm()
        {
            if (Form != null && !Form.IsDisposed) Form.BeginInvoke(new Action(Form.RefreshFromSession));
        }

        // ================================================================== state

        /// <summary>Loads the drawing's stored comparison state when the drawing changed.</summary>
        public static void EnsureLoaded(Database db, Transaction tr)
        {
            if (DatabaseKey == db.UnmanagedObject && Profile != null) return;
            Reset();
            DatabaseKey = db.UnmanagedObject;
            Applied = AppliedChangeSet.FromJson(DrawingStore.ReadCompareText(db, tr, AppliedKey));
            Decisions = ReviewDecisionSet.FromJson(DrawingStore.ReadCompareText(db, tr, DecisionsKey));
            Source = ConsultantSourceSpec.FromJson(DrawingStore.ReadCompareText(db, tr, SourceKey)) ?? DefaultSource(db);
            var resultPath = ResultPathFor(db.Filename);
            if (resultPath != null && File.Exists(resultPath))
            {
                try
                {
                    Result = ComparisonResult.FromJson(File.ReadAllText(resultPath));
                    if (Result != null) foreach (var shape in Result.ConsultantShapes) Shapes[shape.Id] = shape;
                }
                catch (Newtonsoft.Json.JsonException) { Result = null; }
                catch (IOException) { Result = null; }
            }
            LoadProfile(db, tr);
            if (Result != null) RebuildPlan();
        }

        public static void Reset()
        {
            Profile = null; ProfileLevel = null; Result = null; Decisions = new ReviewDecisionSet(); Applied = null; Plan = null;
            Source = new ConsultantSourceSpec(); LastQc = null; Shapes.Clear(); DatabaseKey = IntPtr.Zero;
        }

        private static ConsultantSourceSpec DefaultSource(Database db)
        {
            var settings = SettingsFor(db);
            return new ConsultantSourceSpec { Mode = settings.Compare.ConsultantSource, FilePath = settings.Compare.ConsultantFile };
        }

        public static FtfSettings SettingsFor(Database db)
        {
            RulesConfig rules = null;
            try { rules = FtfSession.Rules(db); } catch (ConfigException) { }
            return FtfSession.ResolveSettings(db, rules).Settings;
        }

        public static RulesConfig RulesOrNull(Database db)
        {
            try { return FtfSession.Rules(db); } catch (ConfigException) { return null; }
        }

        public static string PluginDirectory()
        {
            try
            {
                var location = typeof(CompareSession).Assembly.Location;
                return string.IsNullOrEmpty(location) ? null : Path.GetDirectoryName(location);
            }
            catch (NotSupportedException) { return null; }
        }

        public static string DrawingDirectory(Database db)
        {
            try { return string.IsNullOrEmpty(db.Filename) ? null : Path.GetDirectoryName(db.Filename); }
            catch (ArgumentException) { return null; }
        }

        /// <summary>The profile for this drawing: its own choice, else the settings' active one.</summary>
        public static void LoadProfile(Database db, Transaction tr)
        {
            var chosen = DrawingStore.ReadCompareText(db, tr, ProfileKey);
            var settings = SettingsFor(db);
            var name = string.IsNullOrWhiteSpace(chosen) ? settings.Compare.ActiveProfile : chosen;
            var resolution = CompareProfile.Resolve(name, DrawingDirectory(db), PluginDirectory(), RulesOrNull(db));
            Profile = resolution.Profile;
            ProfileLevel = resolution.Level + (resolution.LoadedFrom == null ? string.Empty : " (" + resolution.LoadedFrom + ")");
            if (resolution.Level == "User" || resolution.Level == "Factory")
            {
                // A saved profile may predate the office rules: add the survey layers anyway.
                var rules = RulesOrNull(db);
                if (rules != null) Profile.ApplySurveyDefaultsFrom(rules);
            }
        }

        public static void ChooseProfile(Database db, Transaction tr, string name)
        {
            DrawingStore.WriteCompareText(db, tr, ProfileKey, name ?? string.Empty);
            Profile = null;
            LoadProfile(db, tr);
        }

        public static void SaveSource(Database db, Transaction tr)
        {
            DrawingStore.WriteCompareText(db, tr, SourceKey, Source.ToJson());
        }

        public static void SaveDecisions(Database db, Transaction tr)
        {
            DrawingStore.WriteCompareText(db, tr, DecisionsKey, Decisions.ToJson());
            var settings = SettingsFor(db);
            var path = ReviewDecisionSet.PathFor(db.Filename);
            if (settings.Compare.DecisionsBesideDrawing && path != null)
            {
                try { File.WriteAllText(path, Decisions.ToJson()); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        public static void SaveApplied(Database db, Transaction tr)
        {
            if (Applied == null || Applied.Changes.Count == 0) DrawingStore.DeleteCompareText(db, tr, AppliedKey);
            else DrawingStore.WriteCompareText(db, tr, AppliedKey, Applied.ToJson());
        }

        public static string ResultPathFor(string drawingPath)
        {
            if (string.IsNullOrWhiteSpace(drawingPath)) return null;
            try
            {
                var dir = Path.GetDirectoryName(drawingPath);
                var name = Path.GetFileNameWithoutExtension(drawingPath);
                return string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(name) ? null : Path.Combine(dir, name + ResultSuffix);
            }
            catch (ArgumentException) { return null; }
        }

        public static void SaveResult(Database db)
        {
            var path = ResultPathFor(db.Filename);
            if (path == null || Result == null) return;
            try { File.WriteAllText(path, Result.ToJson()); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        public static ChangePlan RebuildPlan()
        {
            if (Result == null || Profile == null) { Plan = null; return null; }
            Plan = ChangePlanner.Build(Result, EffectiveProfile());
            return Plan;
        }

        /// <summary>The profile with this drawing's own comparison limits applied.</summary>
        public static CompareProfile EffectiveProfile()
        {
            if (Profile == null) return null;
            if (Source == null || !Source.HasBoundary) return Profile;
            var copy = CompareProfile.FromJson(Profile.ToJson());
            copy.Boundary.Points = Source.Boundary.Select(p => new[] { p[0], p[1] }).ToList();
            return copy;
        }

        // ==================================================================== run

        /// <summary>The comparison: restore any earlier application, read both sides,
        /// compare, re-apply stored decisions, plan, persist, report.</summary>
        public static void RunComparison(Database db, Transaction tr, Editor ed, ICollection<ObjectId> onlyThese)
        {
            EnsureLoaded(db, tr);
            if (Profile == null) LoadProfile(db, tr);
            var settings = SettingsFor(db);
            var profile = EffectiveProfile();

            if (Applied != null && Applied.Changes.Count > 0)
            {
                int orphans;
                var restored = CompareLayerManager.RestoreAll(db, tr, Applied, ed, out orphans);
                ed.WriteMessage("\n  Earlier comparison changes restored first ({0} object(s), {1} split copy(ies) removed) so nothing accumulates.", restored, orphans + Applied.Changes.Sum(c => c.SplitHandles.Count));
                Applied = null;
                SaveApplied(db, tr);
            }

            ed.WriteMessage("\nFTFCOMPARE: profile {0} [{1}]; consultant source: {2}.", profile.Name, ProfileLevel, Source.Describe());
            if (Source.HasBoundary) ed.WriteMessage("\n  Comparison limits: a {0}-point area set for this drawing.", Source.Boundary.Count);

            using (var progress = new EditorProgress(ed))
            {
                progress.Stage("Reading Parametrix survey");
                var survey = CompareDrawingReader.ReadSurvey(db, tr, profile, onlyThese, progress);
                progress.Stage("Reading consultant drawing");
                var consultant = CompareDrawingReader.ReadConsultant(db, tr, Source, profile, survey.UnitsPerFoot, progress);
                ed.WriteMessage("\n  survey: {0} feature(s); consultant: {1} feature(s) from {2} file(s).", survey.Features.Count, consultant.Features.Count, consultant.FilesRead.Count);
                foreach (var issue in consultant.Issues.Concat(survey.Issues).Where(i => i.Severity != Severity.Info))
                    ed.WriteMessage("\n  {0} {1}: {2}", issue.Severity.ToString().ToUpperInvariant(), issue.Code, issue.Message);

                var engine = new ComparisonEngine(profile);
                Result = engine.Run(survey, consultant, progress);
            }
            Shapes.Clear();
            foreach (var shape in Result.ConsultantShapes) Shapes[shape.Id] = shape;

            var stale = new List<string>();
            var reapplied = Decisions.ApplyTo(Result, stale);
            if (reapplied > 0 || stale.Count > 0)
                ed.WriteMessage("\n  Review decisions re-applied: {0}; set aside because the feature changed: {1}.", reapplied, stale.Count);
            RebuildPlan();
            SaveResult(db);
            SaveSource(db, tr);
            SaveDecisions(db, tr);

            LastSummary = CompareReport.SummaryText(Result, Plan);
            ed.WriteMessage("\n{0}\n", LastSummary);
            if (settings.Compare.WriteCsvReport) WriteReport(db, ed);
        }

        public static string WriteReport(Database db, Editor ed)
        {
            if (Result == null) return null;
            var path = CompareReport.PathFor(db.Filename);
            if (path == null)
            {
                if (ed != null) ed.WriteMessage("\n  The drawing has never been saved, so no CSV report was written.");
                return null;
            }
            try
            {
                File.WriteAllText(path, CompareReport.Render(Result, Path.GetFileNameWithoutExtension(db.Filename)), new System.Text.UTF8Encoding(true));
                if (ed != null) ed.WriteMessage("\n  Report: {0}", path);
                return path;
            }
            catch (IOException ex) { if (ed != null) ed.WriteMessage("\n  Could not write {0}: {1}", path, ex.Message); }
            catch (UnauthorizedAccessException ex) { if (ed != null) ed.WriteMessage("\n  Could not write {0}: {1}", path, ex.Message); }
            return null;
        }

        // ================================================================== apply

        public static void ApplyPlan(Database db, Transaction tr, Editor ed)
        {
            EnsureLoaded(db, tr);
            if (Result == null) throw new InvalidOperationException("Run the comparison first (FTFCOMPARERUN).");
            var settings = SettingsFor(db);
            var profile = EffectiveProfile();
            var plan = RebuildPlan();
            var rules = RulesOrNull(db);
            int applied, reversed, skipped;
            Applied = CompareLayerManager.Reconcile(db, tr, plan, Result, profile, rules != null ? rules.Version : string.Empty, Applied,
                                                    settings.Compare.ApplyAutomaticResults, ed, out applied, out reversed, out skipped);
            SaveApplied(db, tr);
            SaveDecisions(db, tr);
            ed.WriteMessage("\nFTFCOMPAREAPPLY: {0} object(s) moved to nonplot ({1} split), {2} reversed, {3} skipped; {4} awaiting review, {5} keep plotting.",
                applied, plan.SplitCount, reversed, skipped, plan.AwaitingReview, plan.KeptPlotting);
            if (!settings.Compare.ApplyAutomaticResults)
                ed.WriteMessage("\n  Automatic results are not applied (settings): only reviewed decisions moved.");
            LastSummary = CompareReport.SummaryText(Result, plan);
        }

        public static void RestoreAll(Database db, Transaction tr, Editor ed)
        {
            EnsureLoaded(db, tr);
            int orphans;
            var restored = CompareLayerManager.RestoreAll(db, tr, Applied, ed, out orphans);
            var copies = Applied != null ? Applied.Changes.Sum(c => c.SplitHandles.Count) : 0;
            Applied = null;
            SaveApplied(db, tr);
            if (Result != null) foreach (var r in Result.Results) { r.Action = PlannedAction.None; }
            RebuildPlan();
            ed.WriteMessage("\nFTFCOMPARERESTORE: {0} object(s) back on their original layers; {1} split copy(ies) erased. Decisions are kept.",
                restored, copies + orphans);
        }

        // ================================================================= decide

        public static int Decide(IEnumerable<string> handles, ReviewDecision decision, string note)
        {
            if (Result == null) return 0;
            var n = 0;
            foreach (var handle in handles)
            {
                var r = Result.Find(handle);
                if (r == null) continue;
                Decisions.Set(r, decision, note);
                n++;
            }
            RebuildPlan();
            return n;
        }

        /// <summary>Accepts every automatic result the engine is confident in, so nobody
        /// clicks through hundreds of obvious matches. Nothing uncertain is touched.</summary>
        public static int AcceptConfident()
        {
            if (Result == null || Profile == null) return 0;
            var threshold = Profile.Tolerances.AutoAcceptConfidence;
            var n = 0;
            foreach (var r in Result.Results)
            {
                if (r.Decision != ReviewDecision.None || r.Confidence < threshold) continue;
                if (r.Status == MatchStatus.Matched && !r.HasOutsideLimits) { Decisions.Set(r, ReviewDecision.AcceptMatch, "bulk: confident match"); n++; }
                else if (r.Status == MatchStatus.Partial && !r.HasOutsideLimits) { Decisions.Set(r, ReviewDecision.AcceptPartial, "bulk: confident partial"); n++; }
            }
            RebuildPlan();
            return n;
        }

        // ===================================================================== QC

        public static IList<QcCheck> RunQc(Database db, Transaction tr, Editor ed)
        {
            EnsureLoaded(db, tr);
            var profile = EffectiveProfile() ?? new CompareProfile();
            var folder = DeliveryFolder(db);
            var input = CompareLayerManager.GatherQc(db, tr, profile, Result, RebuildPlan(), Applied, folder);
            LastQc = CompareQc.Evaluate(input);
            if (ed != null) ed.WriteMessage("\n{0}\n", CompareQc.Render(LastQc));
            return LastQc;
        }

        public static string DeliveryFolder(Database db)
        {
            if (Profile != null && !string.IsNullOrWhiteSpace(Profile.DeliveryFolder)) return Profile.DeliveryFolder;
            var settings = SettingsFor(db);
            return string.IsNullOrWhiteSpace(settings.Compare.DeliveryFolder) ? null : settings.Compare.DeliveryFolder;
        }

        /// <summary>
        /// Final QC, the reports, and the path the deliverable goes to. Refuses while a
        /// blocking check fails. The DWG itself is written by <see cref="CompleteSave"/>
        /// after the transaction commits (set in <see cref="PendingSavePath"/>).
        /// </summary>
        public static string PrepareDeliverable(Database db, Transaction tr, Editor ed, string folder)
        {
            EnsureLoaded(db, tr);
            var settings = SettingsFor(db);
            folder = string.IsNullOrWhiteSpace(folder) ? DeliveryFolder(db) : folder;
            if (string.IsNullOrWhiteSpace(folder)) throw new ConfigException("No delivery folder: set it in Settings > Survey Comparison, in the profile, or give it to the command.");
            if (string.IsNullOrEmpty(db.Filename)) throw new ConfigException("Save the drawing first.");

            var checks = RunQc(db, tr, ed);
            if (!CompareQc.Passes(checks))
                throw new ConfigException("Final QC failed; the deliverable was NOT written. Fix the blocking items above and run FTFCOMPARESAVE again.");

            Directory.CreateDirectory(folder);
            var name = Path.GetFileNameWithoutExtension(db.Filename) + (settings.Compare.DeliverySuffix ?? string.Empty) + ".dwg";
            var output = Path.Combine(folder, name);
            PendingSavePath = output;

            var report = Path.Combine(folder, Path.GetFileNameWithoutExtension(db.Filename) + (settings.Compare.DeliverySuffix ?? string.Empty) + CompareReport.Suffix);
            try
            {
                File.WriteAllText(report, CompareReport.Render(Result, Path.GetFileNameWithoutExtension(db.Filename)), new System.Text.UTF8Encoding(true));
                File.WriteAllText(Path.ChangeExtension(report, ".qc.txt"), CompareQc.Render(checks) + Environment.NewLine + Environment.NewLine + CompareReport.SummaryText(Result, Plan));
            }
            catch (IOException ex) { ed.WriteMessage("\n  Report not written beside the deliverable: {0}", ex.Message); }

            var xrefs = CompareDrawingReader.XrefStates(db, tr);
            ed.WriteMessage("\nFTFCOMPARESAVE: QC passed; writing {0}", output);
            ed.WriteMessage("\n  Report: {0}", report);
            if (xrefs.Count > 0)
                ed.WriteMessage("\n  The deliverable depends on {0} external reference(s): {1}. Upload them with it (same relative paths).",
                    xrefs.Count, string.Join(", ", xrefs.Select(x => x.Name + " -> " + x.Path).ToArray()));
            return output;
        }
    }
}
