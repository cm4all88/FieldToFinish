using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using FieldCodes.Compare;

namespace FieldCodes.Cad.Compare
{
    /// <summary>
    /// FTFTRIM state and flow. The host drawing is the aerial base; our finished sites are
    /// its xrefs (or one file). Inside our limits of survey the aerial goes to its -NP
    /// layers, a line crossing the limit is cut exactly there with the outside part kept
    /// plotting, and every cut is checked against our continuing line: a seam that is off
    /// gets a cloud and a note. Everything is recorded and FTFTRIMRESTORE reverses it.
    ///
    /// UNTESTED in Civil 3D until the smoke test says otherwise.
    /// </summary>
    internal static class TrimSession
    {
        private const string AppliedKey = "TRIM-APPLIED";
        private const string SourceKey = "TRIM-SOURCE";
        public const string ResultSuffix = ".ftf-trim-result.json";
        public const string CloudTag = "TRIM-CLOUD";

        public static TrimResult Result;
        public static AppliedChangeSet Applied;
        public static ConsultantSourceSpec SurveySource = new ConsultantSourceSpec { Mode = "Xrefs" };
        public static string LastSummary;
        private static IntPtr _databaseKey = IntPtr.Zero;

        public static void EnsureLoaded(Database db, Transaction tr)
        {
            if (_databaseKey == db.UnmanagedObject) return;
            Result = null; Applied = null; LastSummary = null;
            _databaseKey = db.UnmanagedObject;
            Applied = AppliedChangeSet.FromJson(DrawingStore.ReadCompareText(db, tr, AppliedKey));
            SurveySource = ConsultantSourceSpec.FromJson(DrawingStore.ReadCompareText(db, tr, SourceKey)) ?? new ConsultantSourceSpec { Mode = "Xrefs" };
            var path = ResultPathFor(db.Filename);
            if (path != null && File.Exists(path))
            {
                try { Result = TrimResult.FromJson(File.ReadAllText(path)); }
                catch (Newtonsoft.Json.JsonException) { Result = null; }
                catch (IOException) { Result = null; }
            }
        }

        public static string ResultPathFor(string drawingPath)
        {
            return TrimReport.PathFor(drawingPath, ResultSuffix);
        }

        private static void SaveState(Database db, Transaction tr)
        {
            if (Applied == null || Applied.Changes.Count == 0) DrawingStore.DeleteCompareText(db, tr, AppliedKey);
            else DrawingStore.WriteCompareText(db, tr, AppliedKey, Applied.ToJson());
            DrawingStore.WriteCompareText(db, tr, SourceKey, SurveySource.ToJson());
        }

        private static void SaveResult(Database db)
        {
            var path = ResultPathFor(db.Filename);
            if (path == null) return;
            try
            {
                if (Result == null) { if (File.Exists(path)) File.Delete(path); }
                else File.WriteAllText(path, Result.ToJson());
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        // ==================================================================== run

        public static void Run(Database db, Transaction tr, Editor ed, ICollection<ObjectId> pickedFootprints)
        {
            EnsureLoaded(db, tr);
            CompareSession.EnsureLoaded(db, tr);
            var profile = CompareSession.Profile;
            if (profile == null) throw new ConfigException("No comparison profile could be loaded.");
            var rules = CompareSession.RulesOrNull(db);

            if (Applied != null && Applied.Changes.Count > 0)
            {
                int orphans, twins;
                var restored = CompareLayerManager.RestoreAll(db, tr, Applied, ed, out orphans, out twins, FtfEntityKind.TrimSplit);
                ed.WriteMessage("\n  Earlier trim restored first ({0} object(s), {1} outside piece(s) removed) so nothing accumulates.", restored, orphans);
                Applied = null;
            }
            CompareReviewMarks.ClearKind(db, tr, FtfEntityKind.TrimCloud);

            ed.WriteMessage("\nFTFTRIM: profile {0} [{1}]; our survey from: {2}; limits layers: {3}.",
                profile.Name, CompareSession.ProfileLevel, SurveySource.Describe(), string.Join(", ", profile.Trim.LimitsLayers.ToArray()));

            ChangePlan plan;
            using (var progress = new EditorProgress(ed))
            {
                progress.Stage("Reading the aerial base (this drawing)");
                var aerial = CompareDrawingReader.ReadSurvey(db, tr, profile, null, progress, CompareSource.Consultant);
                progress.Stage("Reading our survey");
                var survey = CompareDrawingReader.ReadConsultant(db, tr, SurveySource, profile, aerial.UnitsPerFoot, progress, CompareSource.Survey);
                progress.Stage("Finding our limits of survey");
                var footprints = pickedFootprints != null && pickedFootprints.Count > 0
                    ? CompareDrawingReader.FootprintsFromEntities(db, tr, pickedFootprints)
                    : CompareDrawingReader.ReadFootprints(db, tr, SurveySource, profile.Trim.LimitsLayers);
                ed.WriteMessage("\n  aerial: {0} object(s); our survey: {1} feature(s) from {2} file(s); limits of survey: {3} polygon(s){4}.",
                    aerial.Features.Count, survey.Features.Count, survey.FilesRead.Count, footprints.Count,
                    footprints.Count == 0 ? string.Empty : " (" + string.Join(", ", footprints.Select(f => f.Id).ToArray()) + ")");
                foreach (var issue in aerial.Issues.Concat(survey.Issues).Where(i => i.Severity != Severity.Info))
                    ed.WriteMessage("\n  {0} {1}: {2}", issue.Severity.ToString().ToUpperInvariant(), issue.Code, issue.Message);

                progress.Stage("Planning the trim and checking the seams");
                Result = TrimPlanner.Plan(aerial, survey, footprints, profile, out plan);
            }
            foreach (var issue in Result.Issues.Where(i => i.Code == "NO-FOOTPRINT"))
                ed.WriteMessage("\n  ERROR {0}: {1} Put a closed polyline on a limits layer in the survey drawing, or pick one with FTFTRIM Select.", issue.Code, issue.Message);

            var problems = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int applied, reversed, skipped;
            Applied = CompareLayerManager.Reconcile(db, tr, plan, Result.Features, profile, rules != null ? rules.Version : string.Empty, null, true, ed,
                                                    out applied, out reversed, out skipped, problems, FtfEntityKind.TrimSplit);
            foreach (var kv in problems)
            {
                var entry = Result.Entries.FirstOrDefault(e => string.Equals(e.Handle, kv.Key, StringComparison.OrdinalIgnoreCase));
                if (entry != null) entry.ApplyProblem = kv.Value;
            }

            var marked = MarkSeams(db, tr, profile);
            SaveState(db, tr);
            SaveResult(db);
            LastSummary = TrimReport.SummaryText(Result);
            ed.WriteMessage("\n{0}", LastSummary);
            ed.WriteMessage("\n  Applied: {0} object(s) changed and verified, {1} refused (NOT changed, listed above); {2} seam mark(s) drawn on layer {3}.",
                applied, skipped, marked, profile.Review.Layer);
            WriteReports(db, ed);
        }

        private static int MarkSeams(Database db, Transaction tr, CompareProfile profile)
        {
            if (Result == null || !profile.Trim.DrawClouds) return 0;
            var upf = Result.UnitsPerFoot <= 0 ? 1.0 : Result.UnitsPerFoot;
            var margin = profile.Review.MarginFeet * upf;
            var n = 0;
            foreach (var seam in Result.Seams)
            {
                if (seam.Status == SeamStatus.Aligned) continue;
                var text = string.Format(CultureInfo.InvariantCulture, "FTFTRIM SEAM {0} ({1}): {2}", seam.Id,
                    string.IsNullOrEmpty(seam.Category) ? seam.AerialLayer : seam.Category, seam.Note);
                CompareReviewMarks.Mark(db, tr, seam.Cut.X - margin, seam.Cut.Y - margin, seam.Cut.X + margin, seam.Cut.Y + margin,
                    text, seam.AerialHandle, FtfEntityKind.TrimCloud, CloudTag, profile.Review, upf);
                n++;
            }
            return n;
        }

        public static void WriteReports(Database db, Editor ed)
        {
            if (Result == null) return;
            var entries = TrimReport.PathFor(db.Filename, TrimReport.Suffix);
            var seams = TrimReport.PathFor(db.Filename, TrimReport.SeamSuffix);
            if (entries == null)
            {
                if (ed != null) ed.WriteMessage("\n  The drawing has never been saved, so no CSV reports were written.");
                return;
            }
            try
            {
                File.WriteAllText(entries, TrimReport.Render(Result));
                File.WriteAllText(seams, TrimReport.RenderSeams(Result));
                if (ed != null) ed.WriteMessage("\n  Reports: {0} and {1}", entries, seams);
            }
            catch (IOException ex) { if (ed != null) ed.WriteMessage("\n  Could not write the reports: {0}", ex.Message); }
            catch (UnauthorizedAccessException ex) { if (ed != null) ed.WriteMessage("\n  Could not write the reports: {0}", ex.Message); }
        }

        // ================================================================ restore

        public static void RestoreAll(Database db, Transaction tr, Editor ed)
        {
            EnsureLoaded(db, tr);
            int orphans, twins;
            var restored = CompareLayerManager.RestoreAll(db, tr, Applied, ed, out orphans, out twins, FtfEntityKind.TrimSplit);
            var clouds = CompareReviewMarks.ClearKind(db, tr, FtfEntityKind.TrimCloud);
            Applied = null;
            Result = null;
            SaveState(db, tr);
            SaveResult(db);
            ed.WriteMessage("\nFTFTRIMRESTORE: {0} aerial object(s) back exactly as recorded; {1} outside piece(s) and {2} seam mark(s) erased. Our survey was never touched.",
                restored, orphans, clouds);
        }
    }
}
