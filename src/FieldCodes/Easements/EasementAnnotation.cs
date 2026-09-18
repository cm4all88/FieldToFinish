using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using FieldCodes.Drafting;
using FieldCodes.Settings;

namespace FieldCodes.Easements
{
    /// <summary>
    /// Bearings, distances and curve data for easement courses, in the profile's
    /// formats. The values come from the drawn geometry, so a label can never
    /// disagree with the line it annotates.
    /// </summary>
    /// <summary>How one course is labelled: on the line, or tagged into the table.</summary>
    public sealed class CourseLabel
    {
        public CourseData Data { get; set; }
        public bool IsTie { get; set; }
        public bool InTable { get; set; }
        public IList<string> Lines { get; set; }
    }

    public static class EasementAnnotation
    {
        public const double SquareFeetPerAcre = 43560.0;

        public static CourseData Describe(Course course)
        {
            var data = new CourseData
            {
                Course = course,
                Length = course.Length,
                AzimuthDegrees = SurveyDirection.AzimuthFromVector(course.End.X - course.Start.X,
                                                                   course.End.Y - course.Start.Y)
            };

            if (course.Kind == CourseKind.Arc)
            {
                var sweep = course.Sweep;
                data.Radius = course.Radius;
                data.DeltaDegrees = sweep * 180.0 / Math.PI;
                data.ChordAzimuthDegrees = data.AzimuthDegrees;
                data.ChordLength = course.Start.DistanceTo(course.End);
                data.Tangent = sweep < Math.PI - 1e-9 ? course.Radius * Math.Tan(sweep / 2.0) : (double?)null;
                data.TurnDirection = course.CounterClockwise ? "LEFT" : "RIGHT";
            }
            return data;
        }

        /// <summary>Numbers lines L1, L2... and curves C1, C2... in order.</summary>
        public static List<CourseData> Number(IEnumerable<Course> courses, EasementSettings settings)
        {
            var lines = 0;
            var curves = 0;
            var result = new List<CourseData>();
            foreach (var c in courses)
            {
                var d = Describe(c);
                d.Id = c.Kind == CourseKind.Line
                    ? settings.LinePrefix + (++lines).ToString(CultureInfo.InvariantCulture)
                    : settings.CurvePrefix + (++curves).ToString(CultureInfo.InvariantCulture);
                result.Add(d);
            }
            return result;
        }

        public static string Bearing(double azimuth, EasementSettings settings, string degreeSymbol)
        {
            return SurveyDirection.FormatBearing(azimuth, settings.BearingSecondsDecimals, degreeSymbol, settings.BearingSpaces);
        }

        public static string Distance(double feet, EasementSettings settings)
        {
            return SurveyDirection.FormatDistance(feet, settings.DistanceDecimals, settings.FootSymbol);
        }

        /// <summary>Degrees as D°MM'SS" with the profile's seconds precision.</summary>
        public static string Angle(double degrees, EasementSettings settings, string degreeSymbol)
        {
            return SurveyDirection.FormatAzimuth(degrees, settings.BearingSecondsDecimals, degreeSymbol);
        }

        public static string LineText(CourseData d, EasementSettings settings, string degreeSymbol)
        {
            return Bearing(d.AzimuthDegrees, settings, degreeSymbol) + " " + Distance(d.Length, settings);
        }

        public static IList<string> CurveLines(CourseData d, EasementSettings settings, string degreeSymbol)
        {
            var lines = new List<string>
            {
                "R=" + Distance(d.Radius ?? 0, settings),
                "L=" + Distance(d.Length, settings),
                "Δ=" + Angle(d.DeltaDegrees ?? 0, settings, degreeSymbol)
            };
            if (settings.CurveShowChord)
                lines.Add("CH=" + Bearing(d.ChordAzimuthDegrees ?? 0, settings, degreeSymbol) + " " +
                          Distance(d.ChordLength ?? 0, settings));
            if (settings.CurveShowTangent && d.Tangent.HasValue)
                lines.Add("T=" + Distance(d.Tangent.Value, settings));
            return lines;
        }

        public static string AreaLine(double squareFeet, EasementSettings settings, string purpose = null)
        {
            return Fill(settings.AreaFormat, squareFeet, settings, purpose);
        }

        /// <summary>The acres line, or empty when the profile does not show acres.</summary>
        public static string AcresLine(double squareFeet, EasementSettings settings)
        {
            if (string.IsNullOrWhiteSpace(settings.AcresFormat)) return string.Empty;
            var acres = (squareFeet / SquareFeetPerAcre).ToString(
                "F" + settings.AcresDecimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
            return settings.AcresFormat.Replace("{acres}", acres);
        }

        /// <summary>The area lines written under a title: the area, then acres when shown.</summary>
        public static List<string> AreaLines(double squareFeet, EasementSettings settings, string purpose)
        {
            var lines = new List<string> { AreaLine(squareFeet, settings, purpose) };
            var acres = AcresLine(squareFeet, settings);
            if (acres.Length > 0) lines.Add(acres);
            return lines;
        }

        /// <summary>"SAID SEWER EASEMENT CONTAINING 5,069 SQUARE FEET, MORE OR LESS."</summary>
        public static string LegalAreaLine(double squareFeet, EasementSettings settings, string purpose)
        {
            return Fill(settings.LegalAreaFormat ?? string.Empty, squareFeet, settings, purpose);
        }

        /// <summary>A clicked area's title: "TEMPORARY CONSTRUCTION AREA".</summary>
        public static string AreaTitle(string purpose, EasementSettings settings)
        {
            var p = string.IsNullOrWhiteSpace(purpose) ? settings.AreaPurpose : purpose.Trim().ToUpperInvariant();
            return (settings.AreaTitleFormat ?? "{purpose} AREA").Replace("{purpose}", p).Trim();
        }

        /// <summary>A clicked area's area line: "APPROX. TEMPORARY CONSTRUCTION AREA = 7,500 SF".</summary>
        public static string AreaOfAreaLine(double squareFeet, EasementSettings settings, string purpose)
        {
            return Fill(settings.AreaAreaFormat ?? settings.AreaFormat, squareFeet, settings, string.IsNullOrWhiteSpace(purpose) ? settings.AreaPurpose : purpose);
        }

        public static string AreaLegalLine(double squareFeet, EasementSettings settings, string purpose)
        {
            return Fill(settings.AreaLegalFormat ?? string.Empty, squareFeet, settings, string.IsNullOrWhiteSpace(purpose) ? settings.AreaPurpose : purpose);
        }

        private static string Fill(string format, double squareFeet, EasementSettings settings, string purpose)
        {
            var sqft = squareFeet.ToString("N" + settings.AreaSquareFeetDecimals.ToString(CultureInfo.InvariantCulture),
                                           CultureInfo.InvariantCulture);
            var p = string.IsNullOrWhiteSpace(purpose) ? settings.DefaultPurpose : purpose.Trim().ToUpperInvariant();
            return format.Replace("{sqft}", sqft).Replace("{purpose}", p).Replace("  ", " ").Trim();
        }

        // ============================================================ course labels

        /// <summary>
        /// How each centerline course and tie is labelled, in description order: the tie
        /// from the Point of Commencement, the centerline courses, the tie to the terminus
        /// corner. In Auto mode a course whose bearing and distance fit along it is labelled
        /// on the line; the rest are tagged L1, C1... in order and listed in the table.
        /// </summary>
        public static List<CourseLabel> PlanCenterlineLabels(CourseData commencementTie, IEnumerable<Course> centerline,
                                                             CourseData terminusTie, EasementSettings settings,
                                                             double textHeight, string degreeSymbol)
        {
            return PlanLabels(commencementTie == null ? new List<CourseData>() : new List<CourseData> { commencementTie }, centerline, terminusTie, settings, textHeight, degreeSymbol);
        }

        /// <summary>As above, with a commencement tie of one or more courses (a tie that follows a line and a curve).</summary>
        public static List<CourseLabel> PlanLabels(IList<CourseData> commencementTie, IEnumerable<Course> centerline,
                                                             CourseData terminusTie, EasementSettings settings,
                                                             double textHeight, string degreeSymbol)
        {
            var plan = new List<CourseLabel>();
            if (settings.LabelMode == EasementLabelMode.None) return plan;

            var items = new List<CourseLabel>();
            foreach (var t in commencementTie ?? new List<CourseData>()) items.Add(new CourseLabel { Data = Describe(t.Course), IsTie = true });
            items.AddRange(centerline.Select(c => new CourseLabel { Data = Describe(c) }));
            if (terminusTie != null) items.Add(new CourseLabel { Data = Describe(terminusTie.Course), IsTie = true });

            var lines = 0;
            var curves = 0;
            foreach (var item in items)
            {
                var d = item.Data;
                item.Lines = d.Course.Kind == CourseKind.Line
                    ? new List<string> { LineText(d, settings, degreeSymbol) }
                    : CurveLines(d, settings, degreeSymbol).ToList();
                item.InTable = settings.LabelMode == EasementLabelMode.Table ||
                               (settings.LabelMode == EasementLabelMode.Auto && !FitsAlong(item.Lines, d, textHeight));
                if (item.InTable)
                    d.Id = d.Course.Kind == CourseKind.Line
                        ? settings.LinePrefix + (++lines).ToString(CultureInfo.InvariantCulture)
                        : settings.CurvePrefix + (++curves).ToString(CultureInfo.InvariantCulture);
                plan.Add(item);
            }
            return plan;
        }

        /// <summary>True when the label text, at the text height, fits along the course
        /// with a little room to spare. Characters average about three quarters of the text height wide.</summary>
        public static bool FitsAlong(IList<string> lines, CourseData data, double textHeight)
        {
            var longest = lines.Count == 0 ? 0 : lines.Max(l => l.Replace("%%d", "d").Length);
            var room = data.Course.Kind == CourseKind.Line ? data.Length : (data.ChordLength ?? data.Length);
            return longest * textHeight * 0.75 <= room * 0.95;
        }

        public static string Title(WidthSpec width, string purpose, EasementSettings settings)
        {
            var total = width.Total.ToString("F" + settings.DistanceDecimals.ToString(CultureInfo.InvariantCulture),
                                             CultureInfo.InvariantCulture);
            return settings.TitleFormat
                .Replace("{width}", total)
                .Replace("{purpose}", string.IsNullOrWhiteSpace(purpose) ? settings.DefaultPurpose : purpose.Trim().ToUpperInvariant())
                .Trim();
        }

        /// <summary>A stable fingerprint of geometry, rounded to a thousandth of a foot,
        /// so a later edit to the controlling objects is detectable.</summary>
        public static string Fingerprint(IEnumerable<Course> courses)
        {
            var sb = new StringBuilder();
            foreach (var c in courses)
            {
                sb.Append(c.Kind == CourseKind.Line ? 'L' : 'A');
                Append(sb, c.Start);
                Append(sb, c.End);
                if (c.Kind == CourseKind.Arc)
                {
                    Append(sb, c.Center);
                    sb.Append(c.CounterClockwise ? '+' : '-');
                }
                sb.Append(';');
            }
            return Hash(sb.ToString());
        }

        public static string FingerprintPoint(double x, double y)
        {
            var sb = new StringBuilder();
            Append(sb, new P2(x, y));
            return Hash(sb.ToString());
        }

        private static void Append(StringBuilder sb, P2 p)
        {
            sb.Append(p.X.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
              .Append(p.Y.ToString("F3", CultureInfo.InvariantCulture)).Append('|');
        }

        private static string Hash(string text)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
                return string.Concat(bytes.Take(12).Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
            }
        }

        /// <summary>A point inside the strip for the title and area: halfway along the
        /// centerline, moved to the middle of the width.</summary>
        public static P2 LabelPoint(IList<Course> centerline, WidthSpec width, out P2 direction)
        {
            var station = EasementBuilder.RouteLength(centerline) / 2.0;
            var point = EasementBuilder.PointAtStation(centerline, station, out direction);
            return point + direction.LeftNormal() * ((width.Left - width.Right) / 2.0);
        }
    }
}
