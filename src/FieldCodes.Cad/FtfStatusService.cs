using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using FieldCodes.Reporting;
using FieldCodes.Review;

namespace FieldCodes.Cad
{
    /// <summary>A snapshot of where the active drawing stands with FTF.</summary>
    public sealed class FtfDrawingStatus
    {
        public string DrawingName { get; set; }
        public string DrawingPath { get; set; }

        /// <summary>Every CogoPoint in model space. Civil 3D owns these.</summary>
        public int TotalPoints { get; set; }

        /// <summary>Points whose code matched a rule and parsed cleanly.</summary>
        public int Recognized { get; set; }

        /// <summary>Points carrying data no rule is configured for (codes you marked
        /// ignored are not counted).</summary>
        public int Unconfigured { get; set; }

        /// <summary>Linework, never-draw, ignored-by-you and bare codes -- not FTF's job.</summary>
        public int OutsideScope { get; set; }

        /// <summary>Points that could not be finished: blank, unreadable or invalid.</summary>
        public int Errors { get; set; }

        /// <summary>Entities FTF created and still owns via XData, model and paper space.</summary>
        public int OwnedEntities { get; set; }

        /// <summary>The full point breakdown the Overview shows.</summary>
        public PointTally Tally { get; set; }

        /// <summary>FTF-owned entities by ownership kind name (Label, Mask, DripArc...).</summary>
        public IDictionary<string, int> OwnedByKind { get; set; }

        /// <summary>The last processing run stored in the drawing, or null.</summary>
        public ProcessingRecord LastRun { get; set; }

        /// <summary>Which settings file is in force, in words, with its path.</summary>
        public string SettingsText { get; set; }

        /// <summary>Which rules file is in force, in words, with its path.</summary>
        public string RulesText { get; set; }

        /// <summary>When this snapshot was taken.</summary>
        public DateTime GatheredLocal { get; set; }

        public string ExceptionReportPath { get; set; }
        public string UnhandledReportPath { get; set; }
        public string RulesError { get; set; }

        public bool RulesLoaded { get { return RulesError == null; } }

        public FtfDrawingStatus()
        {
            Tally = new PointTally();
            OwnedByKind = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Gathers the status the FTF window shows. Read-only: nothing here changes the
    /// drawing, so refreshing the window is always safe -- except
    /// <see cref="RecordRun"/>, which the window calls once after processing to store
    /// when it ran and how it ended.
    ///
    /// UNTESTED: never run against a drawing. The counting it delegates to
    /// (CodeDiagnostics, ProcessingRecord) is unit tested.
    /// </summary>
    internal static class FtfStatusService
    {
        /// <summary>Null when no drawing is active.</summary>
        public static FtfDrawingStatus Gather()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return null;

            var db = doc.Database;
            var status = new FtfDrawingStatus
            {
                DrawingPath = db.Filename ?? string.Empty,
                DrawingName = string.IsNullOrEmpty(db.Filename)
                    ? "(never saved)"
                    : Path.GetFileName(db.Filename),
                GatheredLocal = DateTime.Now
            };

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                RulesConfig cfg = null;
                FieldCodes.Settings.FtfSettings settings = null;
                try
                {
                    status.RulesText = ConfigurationSummary.DescribeRules(FtfSession.ResolveRules(db));
                    cfg = FtfSession.Rules(db);
                    settings = FtfSession.SettingsFor(db, cfg);
                    status.SettingsText = ConfigurationSummary.DescribeSettings(
                        FtfSession.ResolveSettings(db, cfg));
                }
                catch (ConfigException ex)
                {
                    status.RulesError = ex.Message;
                }

                if (cfg != null)
                {
                    var parser = new FieldCodeParser(cfg);
                    var notes = CogoNotes.For(db, tr, settings);
                    var diagnostics = new CodeDiagnostics(cfg,
                        settings != null && settings.Diagnostics != null
                            ? settings.Diagnostics.Codes()
                            : null);

                    foreach (var cogo in CadUtil.CogoPointsInOrder(db, tr))
                    {
                        var parsed = notes.Parse(parser, cogo);
                        TreeCommands.MissingBlock(db, tr, parsed);   // the same error FTFPOINTS reports
                        status.Tally.Add(diagnostics.Classify(parsed));
                    }

                    status.TotalPoints = status.Tally.Total;
                    status.Recognized = status.Tally.Processed;
                    status.Unconfigured = status.Tally.NeedsConfiguration;
                    status.OutsideScope = status.Tally.SkippedIntentionally;
                    status.Errors = status.Tally.Errors;
                }

                CountOwned(status, Ownership.FindOwned(db, tr, null));
                CountOwned(status, Ownership.FindOwnedInLayouts(db, tr, null, null));

                status.LastRun = DrawingStore.ReadLastRun(db, tr);
                tr.Commit();
            }

            status.ExceptionReportPath = ExceptionReport.PathFor(status.DrawingPath);
            status.UnhandledReportPath = UnhandledSummary.PathFor(status.DrawingPath);
            return status;
        }

        private static void CountOwned(FtfDrawingStatus status,
                                       IList<KeyValuePair<ObjectId, FtfStamp>> owned)
        {
            foreach (var pair in owned)
            {
                var kind = pair.Value.Kind.ToString();
                int n;
                status.OwnedByKind.TryGetValue(kind, out n);
                status.OwnedByKind[kind] = n + 1;
                status.OwnedEntities++;
            }
        }

        /// <summary>
        /// Stores the run that just finished in the drawing -- when, what ran, how it
        /// ended, and the counts straight afterwards -- so the Overview can say when
        /// this drawing was last processed even after it is closed and reopened.
        /// Writes one Xrecord in FTF's own dictionary; touches nothing else. Returns
        /// the record (also when it could not be stored, e.g. a read-only drawing).
        /// </summary>
        public static ProcessingRecord RecordRun(FtfRunResult result, FtfDrawingStatus after,
                                                 string source, Action<string> log)
        {
            var record = new ProcessingRecord
            {
                FinishedUtc = DateTime.UtcNow,
                Source = source ?? string.Empty,
                User = SafeUserName(),
                StoppedEarly = result != null && result.StoppedEarly
            };

            if (result != null)
                foreach (var outcome in result.Outcomes)
                    record.Stages.Add(new ProcessingStage
                    {
                        Name = outcome.Name,
                        Succeeded = outcome.Succeeded,
                        Error = outcome.Error
                    });

            if (after != null)
            {
                record.Points = after.Tally.Total;
                record.Processed = after.Tally.Processed;
                record.Errors = after.Tally.Errors;
                record.NeedsConfiguration = after.Tally.NeedsConfiguration;
                record.Skipped = after.Tally.SkippedIntentionally;
                record.OwnedObjects = after.OwnedEntities;
            }

            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return record;

            try
            {
                using (doc.LockDocument())
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    DrawingStore.WriteLastRun(doc.Database, tr, record);
                    tr.Commit();
                }
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                if (log != null)
                    log("Could not record the run in the drawing: " + ex.Message);
            }

            return record;
        }

        private static string SafeUserName()
        {
            try { return Environment.UserName; }
            catch (InvalidOperationException) { return null; }
            catch (System.Security.SecurityException) { return null; }
        }

        /// <summary>"14:32" today, "2026-10-30 14:32" otherwise.</summary>
        public static string When(DateTime local)
        {
            return local.Date == DateTime.Today
                ? local.ToString("HH:mm", CultureInfo.InvariantCulture)
                : local.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        }
    }
}
