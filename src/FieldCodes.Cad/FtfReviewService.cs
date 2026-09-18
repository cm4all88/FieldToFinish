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

        public FtfReviewResult() { Rows = new List<ReviewRow>(); }
    }

    /// <summary>
    /// The dry run: parses every point and describes what FTF would do, while doing
    /// nothing. Strictly read-only -- points are opened for read, no entity is
    /// created, erased, rotated or stamped, and the transaction commits having
    /// changed nothing. Opening or refreshing Feature Review can never modify the
    /// drawing.
    ///
    /// UNTESTED against a drawing; the row-building it delegates to is unit tested.
    /// </summary>
    internal static class FtfReviewService
    {
        public static FtfReviewResult Scan()
        {
            var result = new FtfReviewResult();

            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return result;

            var db = doc.Database;

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                RulesConfig cfg;
                try
                {
                    cfg = FtfSession.Rules(db);
                    FtfSession.SettingsFor(db, cfg);
                }
                catch (ConfigException ex)
                {
                    result.RulesError = ex.Message;
                    tr.Commit();
                    return result;
                }

                var parser = new FieldCodeParser(cfg);
                var builder = new FeatureReviewBuilder(cfg);

                foreach (var cogo in CadUtil.CogoPointsInOrder(db, tr))
                {
                    var parsed = parser.Parse(
                        cogo.PointNumber.ToString(CultureInfo.InvariantCulture),
                        cogo.RawDescription);

                    result.Rows.Add(builder.Build(parsed));
                }

                tr.Commit();
            }

            return result;
        }
    }
}
