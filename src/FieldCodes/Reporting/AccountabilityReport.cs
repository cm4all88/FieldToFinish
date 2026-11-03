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
        public const string ControlChecksSuffix = ".ftf-control-checks.csv";

        public const string PointsHeader =
            "Point,Handle,RawDescription,Easting,Northing,Elevation,Code,ReadAs,Class,Standard,Needs,Surface,Disposition,Represented,DraftingComplete,Expected,Found,Missing,Evidence,Issues,Decision,FirstSeen,LastSeen,Deleted";

        public const string ControlChecksHeader =
            "CheckPoint,CheckDescription,Easting,Northing,Elevation,ControlPoint,ControlDescription,DeltaNorthing,DeltaEasting,Horizontal,DeltaElevation,Note";

        public const string ContinuityHeader =
            "Id,Kind,Confidence,Figure,Feature,Shots,Lines,LengthFt,Status,Decision,Issue,Evidence";

        public static string PointsPathFor(string drawingPath) { return PathFor(drawingPath, PointsSuffix); }
        public static string ContinuityPathFor(string drawingPath) { return PathFor(drawingPath, ContinuitySuffix); }
        public static string ControlChecksPathFor(string drawingPath) { return PathFor(drawingPath, ControlChecksSuffix); }

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
                  .Append(Csv(p.Classification)).Append(',')
                  .Append(Csv(p.Standard)).Append(',')
                  .Append(Csv(p.Needs)).Append(',')
                  .Append(Csv(p.SurfaceStatus)).Append(',')
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

        /// <summary>
        /// Control check shots (the standard's Control/Check disposition: ZK) against the control
        /// they were shot on: the nearest control point (a control code, in the drawing now)
        /// within <paramref name="searchFeet"/>, and the differences. A check shot with no control
        /// point in reach is listed with a note, never dropped.
        /// </summary>
        public static string RenderControlChecks(PointLedger ledger, double searchFeet, double unitsPerFoot)
        {
            var sb = new StringBuilder();
            sb.Append(ControlChecksHeader).Append("\r\n");
            var points = (ledger ?? new PointLedger()).Points.Where(p => !p.Deleted).ToList();
            var upf = unitsPerFoot > 0 ? unitsPerFoot : 1.0;
            var reach = Math.Max(0, searchFeet) * upf;
            var control = points.Where(p => p.Found.Contains("control") || p.Found.Contains("control table")).ToList();
            foreach (var c in points.Where(p => p.Found.Contains("control check")))
            {
                var near = control.Where(k => k.Number != c.Number)
                                  .Select(k => new { k, d = Math.Sqrt((k.X - c.X) * (k.X - c.X) + (k.Y - c.Y) * (k.Y - c.Y)) })
                                  .Where(x => x.d <= reach).OrderBy(x => x.d).FirstOrDefault();
                sb.Append(Csv(c.Number)).Append(',').Append(Csv(c.RawDescription)).Append(',')
                  .Append(N(c.X)).Append(',').Append(N(c.Y)).Append(',').Append(N(c.Z)).Append(',');
                if (near == null)
                    sb.Append(",,,,,,").Append(Csv("no control point within " + FieldCodes.Standards.FeatureStandard.Fmt(searchFeet) + " ft"));
                else
                    sb.Append(Csv(near.k.Number)).Append(',').Append(Csv(near.k.RawDescription)).Append(',')
                      .Append(N((c.Y - near.k.Y) / upf)).Append(',').Append(N((c.X - near.k.X) / upf)).Append(',')
                      .Append(N(near.d / upf)).Append(',').Append(N((c.Z - near.k.Z) / upf)).Append(',');
                sb.Append("\r\n");
            }
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
