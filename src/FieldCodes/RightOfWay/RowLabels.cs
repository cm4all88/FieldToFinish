using System;
using System.Collections.Generic;
using System.Globalization;
using FieldCodes.Drafting;
using FieldCodes.Easements;
using FieldCodes.Settings;

namespace FieldCodes.Row
{
    /// <summary>
    /// The words on a right-of-way drawing, in the office's formats. Every figure is read
    /// from the geometry of the plan, so a label cannot disagree with the line it labels.
    /// </summary>
    public static class RowLabels
    {
        /// <summary>
        /// Fills the text of every line, dimension, monument and road name in the plan.
        /// <paramref name="lineType"/> is the Drafting Lines type the right-of-way lines
        /// use: its annotation kind, bearing format and number formats decide how a course
        /// reads. <paramref name="degreeSymbol"/> is "%%d" for DBText and "°" elsewhere.
        /// </summary>
        public static void Apply(RowPlan plan, RightOfWaySettings settings, DraftingLineType lineType,
                                 double unitsPerFoot, string degreeSymbol)
        {
            if (plan == null) throw new ArgumentNullException("plan");
            if (settings == null) throw new ArgumentNullException("settings");
            var type = lineType ?? new DraftingLineType();
            var upf = unitsPerFoot > 0 ? unitsPerFoot : 1.0;

            foreach (var line in plan.Lines)
            {
                line.Text.Clear();
                if (!line.Annotate) continue;
                line.Text.AddRange(Course(line.Data, type, upf, degreeSymbol));
            }

            foreach (var dimension in plan.Dimensions)
                dimension.Text = Fit(Dimension(dimension.IsCross && dimension.RecordFeet > 0 ? dimension.RecordFeet : dimension.MeasuredFeet, settings), settings, degreeSymbol);

            foreach (var mark in plan.Monuments)
            {
                mark.Text.Clear();
                mark.Text.Add(Fit(Monument(mark.Monument, settings), settings, degreeSymbol));
                if (mark.Monument.IsOffset)
                    mark.Text.Add(Fit(OffsetNote(mark.Monument, settings, type, degreeSymbol), settings, degreeSymbol));
            }

            foreach (var label in plan.RoadLabels)
                label.Text = Fit(RoadName(label.RoadName, settings), settings, degreeSymbol);
        }

        // ---------------------------------------------------------------- courses

        /// <summary>The text beside a course: bearing and distance for a line, the curve data
        /// for an arc. A type whose annotation is None writes nothing.</summary>
        public static IList<string> Course(CourseData data, DraftingLineType type, double unitsPerFoot, string degreeSymbol)
        {
            var lines = new List<string>();
            if (data == null || type == null || !type.WantsAnnotation) return lines;

            var feet = data.Length / unitsPerFoot;
            var distance = SurveyDirection.FormatDistance(feet, type.DistanceDecimals, type.FootSymbol);

            if (data.Radius.HasValue)
            {
                lines.Add("R=" + SurveyDirection.FormatDistance(data.Radius.Value / unitsPerFoot, type.DistanceDecimals, type.FootSymbol));
                lines.Add("L=" + distance);
                lines.Add("Δ=" + SurveyDirection.FormatAzimuth(data.DeltaDegrees ?? 0.0, type.BearingSecondsDecimals, degreeSymbol));
                if (data.ChordAzimuthDegrees.HasValue && data.ChordLength.HasValue)
                    lines.Add("CH=" + Direction(type, data.ChordAzimuthDegrees.Value, degreeSymbol) + " " +
                              SurveyDirection.FormatDistance(data.ChordLength.Value / unitsPerFoot, type.DistanceDecimals, type.FootSymbol));
                return lines;
            }

            var bearing = Direction(type, data.AzimuthDegrees, degreeSymbol);
            switch (type.AnnotationKind)
            {
                case DraftingLineType.AnnotationBearingOnly:
                    lines.Add(bearing);
                    break;
                case DraftingLineType.AnnotationDistanceOnly:
                    lines.Add(distance);
                    break;
                case DraftingLineType.AnnotationFeatureText:
                    if (!string.IsNullOrWhiteSpace(type.FeatureText)) lines.Add(type.FeatureText.Trim());
                    break;
                default:
                    if (type.Stacked)
                    {
                        lines.Add(bearing);
                        lines.Add(distance);
                    }
                    else lines.Add(bearing + "  " + distance);
                    break;
            }
            return lines;
        }

        public static string Direction(DraftingLineType type, double azimuth, string degreeSymbol)
        {
            return type.WantsAzimuthFormat
                ? SurveyDirection.FormatAzimuth(azimuth, type.BearingSecondsDecimals, degreeSymbol)
                : SurveyDirection.FormatBearing(azimuth, type.BearingSecondsDecimals, degreeSymbol);
        }

        // -------------------------------------------------------------- monuments

        public static string Monument(RowMonument monument, RightOfWaySettings settings)
        {
            var format = monument.Kind == MonumentKind.Found ? settings.FoundFormat : settings.SetFormat;
            return Tokens(format, new Dictionary<string, string>
            {
                { "desc", (monument.Description ?? string.Empty).Trim() },
                { "pt", (monument.PointNumber ?? string.Empty).Trim() }
            });
        }

        /// <summary>The note for an offset monument. {dir} reads from the monument to the
        /// corner, {fromdir} the other way round; the wording says which.</summary>
        public static string OffsetNote(RowMonument monument, RightOfWaySettings settings, DraftingLineType type, string degreeSymbol)
        {
            var distance = monument.OffsetFeet.ToString("F" + type.DistanceDecimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
            return Tokens(settings.OffsetFormat, new Dictionary<string, string>
            {
                { "dist", distance },
                { "dir", Direction(type, monument.OffsetAzimuthDegrees, degreeSymbol) },
                { "fromdir", Direction(type, monument.OffsetAzimuthDegrees + 180.0, degreeSymbol) }
            });
        }

        // ------------------------------------------------------------- dimensions

        public static string Dimension(double widthFeet, RightOfWaySettings settings)
        {
            return Tokens(settings.DimensionFormat, new Dictionary<string, string> { { "width", Width(widthFeet) } });
        }

        public static string RoadName(string name, RightOfWaySettings settings)
        {
            return Tokens(settings.RoadNameFormat, new Dictionary<string, string> { { "name", (name ?? string.Empty).Trim() } });
        }

        /// <summary>60 not 60.00, 59.97 as is: up to two decimals, no trailing zeros.</summary>
        public static string Width(double feet)
        {
            return feet.ToString("0.##", CultureInfo.InvariantCulture);
        }

        // ---------------------------------------------------------------- helpers

        private static string Tokens(string format, IDictionary<string, string> values)
        {
            var text = format ?? string.Empty;
            foreach (var pair in values) text = text.Replace("{" + pair.Key + "}", pair.Value);
            return text.Trim();
        }

        /// <summary>
        /// Capitals when the office wants them -- the usual plat convention -- but never over the
        /// degree token. That token is a control code, not a word: AutoCAD renders %%D the same as
        /// %%d, but every reader that turns it back into a degree sign (the exhibit curve table,
        /// the inspector, the label-width check) matches "%%d" ordinally and would miss %%D.
        /// Restoring the symbol as it was given is a no-op for a caller that passes "°".
        /// </summary>
        private static string Fit(string text, RightOfWaySettings settings, string degreeSymbol)
        {
            if (!settings.UpperCase) return text;
            var upper = text.ToUpperInvariant();
            return string.IsNullOrEmpty(degreeSymbol)
                ? upper
                : upper.Replace(degreeSymbol.ToUpperInvariant(), degreeSymbol);
        }
    }
}
