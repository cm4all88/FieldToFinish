using System;
using System.Globalization;
using System.IO;
using Autodesk.AutoCAD.ApplicationServices;
using FieldCodes.Reporting;

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

        /// <summary>Points carrying data no rule is configured for.</summary>
        public int Unconfigured { get; set; }

        /// <summary>Linework points, bare codes and never-draw codes -- not FTF's job.</summary>
        public int OutsideScope { get; set; }

        /// <summary>Points that matched a rule and then failed inside it.</summary>
        public int Errors { get; set; }

        /// <summary>Entities FTF created and still owns via XData.</summary>
        public int OwnedEntities { get; set; }

        public string ExceptionReportPath { get; set; }
        public string UnhandledReportPath { get; set; }
        public string RulesError { get; set; }

        public bool RulesLoaded { get { return RulesError == null; } }
    }

    /// <summary>
    /// Gathers the status the FTF window shows. Read-only: nothing here changes the
    /// drawing, so refreshing the window is always safe.
    ///
    /// UNTESTED: never run against a drawing.
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
                    : Path.GetFileName(db.Filename)
            };

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                RulesConfig cfg = null;
                try
                {
                    cfg = FtfSession.Rules(db);
                    FtfSession.SettingsFor(db, cfg);
                }
                catch (ConfigException ex)
                {
                    status.RulesError = ex.Message;
                }

                if (cfg != null)
                {
                    var parser = new FieldCodeParser(cfg);

                    foreach (var cogo in CadUtil.CogoPointsInOrder(db, tr))
                    {
                        status.TotalPoints++;

                        var parsed = parser.Parse(
                            cogo.PointNumber.ToString(CultureInfo.InvariantCulture),
                            cogo.RawDescription);

                        if (parsed.Ignored || parsed.NoContent) status.OutsideScope++;
                        else if (parsed.Unhandled) status.Unconfigured++;
                        else if (parsed.HasErrors) status.Errors++;
                        else status.Recognized++;
                    }
                }

                status.OwnedEntities = Ownership.FindOwned(db, tr, null).Count;
                tr.Commit();
            }

            status.ExceptionReportPath = ExceptionReport.PathFor(status.DrawingPath);
            status.UnhandledReportPath = UnhandledSummary.PathFor(status.DrawingPath);
            return status;
        }
    }
}
