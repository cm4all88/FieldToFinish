using System;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.AutoCAD.ApplicationServices;
using FieldCodes.Review;

namespace FieldCodes.Cad
{
    /// <summary>Everything the review pages show for one scan of the drawing.</summary>
    public sealed class FtfReviewResult
    {
        public IList<ReviewRow> Rows { get; set; }
        public string RulesError { get; set; }

        /// <summary>The classifier the scan used: Error / Needs Configuration /
        /// Informational, with the codes marked ignored in settings. Null when the
        /// rules could not be loaded.</summary>
        public CodeDiagnostics Diagnostics { get; set; }

        /// <summary>Codes marked ignored in settings when the scan ran.</summary>
        public IList<string> IgnoredCodes { get; set; }

        /// <summary>When the scan ran (local time).</summary>
        public DateTime ScannedLocal { get; set; }

        public FtfReviewResult()
        {
            Rows = new List<ReviewRow>();
            IgnoredCodes = new List<string>();
        }

        /// <summary>The parsed points behind the rows, in scan order.</summary>
        public IEnumerable<ParsedPoint> Points()
        {
            foreach (var row in Rows)
                if (row != null && row.Parsed != null) yield return row.Parsed;
        }
    }

    /// <summary>
    /// The dry run: parses every point and describes what FTF would do, while doing
    /// nothing. Strictly read-only -- points are opened for read, no entity is
    /// created, erased, rotated or stamped, and the transaction commits having
    /// changed nothing. Opening or refreshing Feature Review can never modify the
    /// drawing.
    ///
    /// UNTESTED against a drawing; the row-building and classification it delegates
    /// to are unit tested.
    /// </summary>
    internal static class FtfReviewService
    {
        public static FtfReviewResult Scan()
        {
            var result = new FtfReviewResult { ScannedLocal = DateTime.Now };

            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return result;

            var db = doc.Database;

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                RulesConfig cfg;
                FieldCodes.Settings.FtfSettings settings;
                try
                {
                    cfg = FtfSession.Rules(db);
                    settings = FtfSession.SettingsFor(db, cfg);
                }
                catch (ConfigException ex)
                {
                    result.RulesError = ex.Message;
                    tr.Commit();
                    return result;
                }

                result.IgnoredCodes = settings.Diagnostics != null
                    ? settings.Diagnostics.Codes()
                    : new List<string>();

                var parser = new FieldCodeParser(cfg);
                var builder = new FeatureReviewBuilder(cfg, result.IgnoredCodes);
                result.Diagnostics = new CodeDiagnostics(cfg, result.IgnoredCodes);
                var notes = CogoNotes.For(db, tr, settings);

                foreach (var cogo in CadUtil.CogoPointsInOrder(db, tr))
                {
                    var parsed = notes.Parse(parser, cogo);
                    TreeCommands.MissingBlock(db, tr, parsed);

                    result.Rows.Add(builder.Build(parsed));
                }

                tr.Commit();
            }

            return result;
        }
    }
}
