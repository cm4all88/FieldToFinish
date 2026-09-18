using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace FieldCodes.Reporting
{
    /// <summary>One row of the exception report.</summary>
    public sealed class ExceptionRow
    {
        public string PointNumber { get; set; }
        public double Easting { get; set; }
        public double Northing { get; set; }
        public double Elevation { get; set; }
        public string RawDescription { get; set; }
        public string Severity { get; set; }
        public string Diagnostics { get; set; }
    }

    /// <summary>
    /// Writes the points that could not be drawn, and the ones that were drawn but
    /// look wrong, to a CSV beside the drawing.
    ///
    /// Errors here mean the upstream office audit let something through, so the report
    /// is the point of the exercise rather than an afterthought -- if it is quiet,
    /// people stop running the audit.
    ///
    /// No Autodesk types, so the formatting is unit tested. The caller supplies the
    /// drawing path.
    /// </summary>
    public static class ExceptionReport
    {
        public const string Suffix = ".ftf-exceptions.csv";

        public const string Header =
            "PointNumber,Easting,Northing,Elevation,RawDescription,Severity,Diagnostics";

        /// <summary>
        /// Report path for a drawing. Returns null when the drawing has never been
        /// saved, in which case the caller tells the user rather than inventing a path.
        /// </summary>
        public static string PathFor(string drawingPath)
        {
            if (string.IsNullOrWhiteSpace(drawingPath)) return null;

            string dir;
            string name;
            try
            {
                dir = Path.GetDirectoryName(drawingPath);
                name = Path.GetFileNameWithoutExtension(drawingPath);
            }
            catch (ArgumentException)
            {
                return null;
            }

            if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(name)) return null;
            return Path.Combine(dir, name + Suffix);
        }

        public static void Write(string path, IEnumerable<ExceptionRow> rows)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentNullException("path");

            // UTF-8 with BOM so Excel opens accented species names correctly.
            File.WriteAllText(path, Render(rows), new UTF8Encoding(true));
        }

        /// <summary>The full CSV text, header included.</summary>
        public static string Render(IEnumerable<ExceptionRow> rows)
        {
            if (rows == null) throw new ArgumentNullException("rows");

            var sb = new StringBuilder();
            sb.AppendLine(Header);

            foreach (var r in rows)
            {
                if (r == null) continue;

                sb.Append(Csv(r.PointNumber)).Append(',');
                sb.Append(Num(r.Easting)).Append(',');
                sb.Append(Num(r.Northing)).Append(',');
                sb.Append(Num(r.Elevation)).Append(',');
                sb.Append(Csv(r.RawDescription)).Append(',');
                sb.Append(Csv(r.Severity)).Append(',');
                sb.Append(Csv(r.Diagnostics));
                sb.AppendLine();
            }

            return sb.ToString();
        }

        private static string Num(double v)
        {
            return v.ToString("F4", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Quotes a CSV field. A leading =, +, - or @ is prefixed with an apostrophe:
        /// a description beginning "-DEAD" would otherwise be read as a formula when
        /// the report is opened in Excel.
        /// </summary>
        internal static string Csv(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;

            var s = value;
            if (s[0] == '=' || s[0] == '+' || s[0] == '-' || s[0] == '@')
                s = "'" + s;

            var needsQuotes = s.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0;
            if (!needsQuotes) return s;

            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
    }
}
