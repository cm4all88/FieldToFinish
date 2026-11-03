using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using FieldCodes.Linework;
using FieldCodes.Review;

namespace FieldCodes.Reporting
{
    /// <summary>
    /// The point ledger and the linework continuity findings as CSV files beside the drawing:
    /// the numerical report that goes with the review queue (FTFREVIEW). One row per point, every
    /// point ever recorded in the drawing (deleted ones too), with what it needs, what the drawing
    /// holds for it, and how it is accounted for. No Autodesk types, so it is unit tested.
    /// </summary>
    public static class AccountabilityReport
    {
        public const string PointsSuffix = ".ftf-points.csv";
        public const string ContinuitySuffix = ".ftf-continuity.csv";

        public const string PointsHeader =
            "Point,Handle,RawDescription,Easting,Northing,Elevation,Code,ReadAs,Disposition,Represented,DraftingComplete,Expected,Found,Missing,Evidence,Issues,Decision,FirstSeen,LastSeen,Deleted";

        public const string ContinuityHeader =
            "Id,Kind,Confidence,Figure,Feature,Shots,Lines,LengthFt,Status,Decision,Issue,Evidence";

        public static string PointsPathFor(string drawingPath) { return PathFor(drawingPath, PointsSuffix); }
        public static string ContinuityPathFor(string drawingPath) { return PathFor(drawingPath, ContinuitySuffix); }

        private static string PathFor(string drawingPath, string suffix)
        {
            if (string.IsNullOrWhiteSpace(drawingPath)) return null;
            try
            {
                var dir = Path.GetDirectoryName(drawingPath);
                var name = Path.GetFileNameWithoutExtension(drawingPath);
                if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(name)) return null;
                return Path.Combine(dir, name + suffix);
            }
            catch (ArgumentException) { return null; }
        }

        public static string RenderPoints(PointLedger ledger)
        {
            var sb = new StringBuilder();
            sb.Append(PointsHeader).Append("\r\n");
            foreach (var p in (ledger ?? new PointLedger()).Points)
            {
                sb.Append(Csv(p.Number)).Append(',')
                  .Append(Csv(p.Handle)).Append(',')
                  .Append(Csv(p.RawDescription)).Append(',')
                  .Append(N(p.X)).Append(',').Append(N(p.Y)).Append(',').Append(N(p.Z)).Append(',')
                  .Append(Csv(p.Code)).Append(',')
                  .Append(Csv(p.Reading)).Append(',')
                  .Append(Csv(p.Disposition.ToString())).Append(',')
                  .Append(p.Represented ? "yes" : "no").Append(',')
                  .Append(p.DraftingComplete ? "yes" : "no").Append(',')
                  .Append(Csv(Join(p.Expected))).Append(',')
                  .Append(Csv(Join(p.Found))).Append(',')
                  .Append(Csv(Join(p.Missing))).Append(',')
                  .Append(Csv(Join(p.Evidence))).Append(',')
                  .Append(Csv(Join(p.Issues))).Append(',')
                  .Append(Csv(p.Decision)).Append(',')
                  .Append(p.FirstSeen.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)).Append(',')
                  .Append(p.LastSeen.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)).Append(',')
                  .Append(p.Deleted ? "yes" : "no")
                  .Append("\r\n");
            }
            var run = ledger != null ? ledger.LastRun : null;
            if (run != null) sb.Append("\r\n").Append(Csv("Summary: " + run.Describe())).Append("\r\n");
            return sb.ToString();
        }

        public static string RenderContinuity(ContinuityResult result, ReviewRegister register)
        {
            var sb = new StringBuilder();
            sb.Append(ContinuityHeader).Append("\r\n");
            foreach (var f in (result ?? new ContinuityResult()).Findings)
            {
                var item = register == null ? null : register.Find(f.Id);
                var status = f.Confidence == GapConfidence.Intentional ? "intentional (not a review item)" : item == null ? "open" : item.Status.ToString();
                sb.Append(Csv(f.Id)).Append(',')
                  .Append(Csv(f.Kind)).Append(',')
                  .Append(Csv(f.Confidence.ToString().ToLowerInvariant())).Append(',')
                  .Append(Csv(f.FigureId)).Append(',')
                  .Append(Csv(f.Family)).Append(',')
                  .Append(Csv(string.Join(" ", f.Shots.ToArray()))).Append(',')
                  .Append(Csv(string.Join(" ", f.Lines.ToArray()))).Append(',')
                  .Append(f.LengthFeet.ToString("0.00", CultureInfo.InvariantCulture)).Append(',')
                  .Append(Csv(status)).Append(',')
                  .Append(Csv(item != null && item.IsDecided ? item.Note + (string.IsNullOrWhiteSpace(item.ReviewedBy) ? string.Empty : " (" + item.ReviewedBy + ")") : string.Empty)).Append(',')
                  .Append(Csv(f.Issue)).Append(',')
                  .Append(Csv(Join(f.Evidence)))
                  .Append("\r\n");
            }
            return sb.ToString();
        }

        private static string Join(IEnumerable<string> list)
        {
            return list == null ? string.Empty : string.Join("; ", list.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray());
        }

        private static string N(double v) { return v.ToString("0.000", CultureInfo.InvariantCulture); }

        private static string Csv(string value)
        {
            var v = value ?? string.Empty;
            if (v.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0) return v;
            return "\"" + v.Replace("\"", "\"\"") + "\"";
        }
    }
}
