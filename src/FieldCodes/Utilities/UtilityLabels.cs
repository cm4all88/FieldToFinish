using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using FieldCodes.Settings;

namespace FieldCodes.Utilities
{
    /// <summary>
    /// Builds pipe and structure label text from the profile's formats. Formats use
    /// {tokens}; a [bracketed] part drops out entirely when any token inside it has
    /// no value, so a pipe with no calculated slope reads "18\" RCP SD" rather than
    /// "18\" RCP SD @ ".
    /// </summary>
    public static class UtilityLabelFormatter
    {
        private static readonly Regex Optional = new Regex(@"\[(?<body>[^\[\]]*)\]", RegexOptions.CultureInvariant);
        private static readonly Regex Token = new Regex(@"\{(?<name>[a-z]+)\}", RegexOptions.CultureInvariant);

        public static string Apply(string format, IDictionary<string, string> values)
        {
            if (string.IsNullOrEmpty(format)) return string.Empty;

            var withOptional = Optional.Replace(format, m =>
            {
                var body = m.Groups["body"].Value;
                foreach (Match t in Token.Matches(body))
                {
                    string v;
                    if (!values.TryGetValue(t.Groups["name"].Value, out v) || string.IsNullOrEmpty(v))
                        return string.Empty;
                }
                return body;
            });

            var result = Token.Replace(withOptional, m =>
            {
                string v;
                return values.TryGetValue(m.Groups["name"].Value, out v) ? v ?? string.Empty : string.Empty;
            });

            return Regex.Replace(result, @"\s{2,}", " ").Trim();
        }

        public static string FormatSize(PipeObservation pipe)
        {
            if (pipe == null || !pipe.WidthIn.HasValue) return "?\"";
            var w = Num(pipe.WidthIn.Value);
            if (pipe.Shape == PipeShape.Round ||
                !pipe.HeightIn.HasValue || Math.Abs(pipe.HeightIn.Value - pipe.WidthIn.Value) < 0.01)
                return w + "\"";
            return w + "\"x" + Num(pipe.HeightIn.Value) + "\"";
        }

        private static string Num(double value)
        {
            return SizeNumber(value);
        }

        /// <summary>
        /// A size in inches with the precision it was entered with and no padding: 12, 17.5, 1.25. The stored value
        /// is never rounded for a label.
        /// </summary>
        public static string SizeNumber(double value)
        {
            var r = value.ToString("R", CultureInfo.InvariantCulture);
            return r.IndexOf('E') < 0 ? r : value.ToString("0.###############", CultureInfo.InvariantCulture);
        }

        public static string PipeLabel(UtilityProject project, PipeConnection connection,
                                       UtilitySettings settings)
        {
            var from = project.Structure(connection.FromStructureId);
            var pipe = project.Pipe(connection.FromStructureId, connection.FromPipeId);
            var slope = SlopeCalculator.Compute(project, connection);
            var standard = settings.Standard(from != null ? from.System : UtilitySystem.Other);

            var values = new Dictionary<string, string>
            {
                { "size", FormatSize(pipe) },
                { "material", pipe != null ? pipe.Material : null },
                { "system", standard.Abbreviation },
                { "slope", slope.SlopePercent.HasValue
                    ? slope.SlopePercent.Value.ToString("F" + settings.SlopeDecimals, CultureInfo.InvariantCulture) + "%"
                    : null }
            };
            return Apply(settings.PipeLabelFormat, values);
        }

        /// <summary>The structure label, one entry per line, ready to preview.</summary>
        public static IList<string> StructureLabel(UtilityProject project, StructureRecord structure,
                                                   UtilitySettings settings)
        {
            var lines = new List<string>();
            if (structure == null) return lines;
            var elevationFormat = "F" + settings.ElevationDecimals.ToString(CultureInfo.InvariantCulture);

            lines.Add(Apply(settings.StructureHeaderFormat, new Dictionary<string, string>
            {
                { "code", structure.EffectiveCode },
                { "number", structure.Field.PointNumber },
                { "type", structure.StructureType },
                { "size", StructureDimensions.SizeText(structure, settings) }
            }));

            if (structure.Cad != null)
                lines.Add(Apply(settings.RimLineFormat, new Dictionary<string, string>
                {
                    { "rim", structure.Cad.Rim.ToString(elevationFormat, CultureInfo.InvariantCulture) }
                }));

            var ordered = structure.Field.Pipes
                .Select((p, i) => new { Pipe = p, Index = i })
                .OrderBy(x => x.Pipe.Role == FlowRole.In ? 0 : x.Pipe.Role == FlowRole.Out ? 1 : 2)
                .ThenBy(x => x.Index)
                .Select(x => x.Pipe);

            foreach (var pipe in ordered)
            {
                var elevation = DipElevations.Pipe(structure, pipe);
                var values = new Dictionary<string, string>
                {
                    { "prefix", Prefix(pipe.Reference, settings) },
                    { "role", pipe.Role == FlowRole.Unknown ? null : pipe.Role.ToString().ToUpperInvariant() },
                    { "direction", pipe.Direction != null ? pipe.Direction.Text : "?" },
                    { "elevation", elevation != null ? elevation.Value.ToString(elevationFormat, CultureInfo.InvariantCulture) : null },
                    { "size", FormatSize(pipe) },
                    { "material", pipe.Material },
                    { "dip", pipe.MeasuredDip.HasValue ? pipe.MeasuredDip.Value.ToString("F2", CultureInfo.InvariantCulture) : null }
                };
                lines.Add(Apply(elevation != null ? settings.PipeLineFormat : settings.UndippedPipeLineFormat, values));
            }

            var bottom = DipElevations.Bottom(structure);
            if (bottom != null)
                lines.Add(Apply(settings.BottomLineFormat, new Dictionary<string, string>
                {
                    { "bottom", bottom.Value.ToString(elevationFormat, CultureInfo.InvariantCulture) }
                }));

            var water = DipElevations.Water(structure);
            if (water != null)
                lines.Add(Apply(settings.WaterLineFormat, new Dictionary<string, string>
                {
                    { "water", water.Value.ToString(elevationFormat, CultureInfo.InvariantCulture) }
                }));

            return lines.Where(l => l.Length > 0).ToList();
        }

        public static string Prefix(MeasurementReference reference, UtilitySettings settings)
        {
            switch (reference)
            {
                case MeasurementReference.Unspecified: return settings.PrefixUnconfirmed;
                case MeasurementReference.Invert: return settings.PrefixInvert;
                case MeasurementReference.TopOfPipe: return settings.PrefixTop;
                case MeasurementReference.Springline: return settings.PrefixSpringline;
                default: return reference.ToString().ToUpperInvariant();
            }
        }
    }

    /// <summary>The traceable story behind a drafted pipe or structure.</summary>
    public static class UtilityProvenance
    {
        public static string ForConnection(UtilityProject project, PipeConnection connection,
                                           IList<QcFinding> findings, UtilitySettings settings)
        {
            var sb = new StringBuilder();
            var from = project.Structure(connection.FromStructureId);
            var to = project.Structure(connection.ToStructureId);
            var fromPipe = project.Pipe(connection.FromStructureId, connection.FromPipeId);
            var toPipe = connection.ToPipeId != null ? project.Pipe(connection.ToStructureId, connection.ToPipeId) : null;

            sb.AppendLine("PIPE " + (from != null ? from.Label : "?") + " -> " + (to != null ? to.Label : "(unresolved)"));
            AppendEnd(sb, "From", from, fromPipe);
            AppendEnd(sb, "To", to, toPipe);
            if (to != null && toPipe == null)
                sb.AppendLine("  To: no corresponding field dip observed at " + to.Label);

            sb.AppendLine("  Connection: " + connection.Status + (connection.Confidence != Confidence.None
                ? " (" + connection.Confidence + " confidence)" : string.Empty));
            foreach (var basis in connection.Basis) sb.AppendLine("    - " + basis);
            if (!string.IsNullOrEmpty(connection.OverrideNote))
                sb.AppendLine("  Manual override note: " + connection.OverrideNote);

            if (to != null)
            {
                var slope = SlopeCalculator.Compute(project, connection);
                sb.AppendLine("  Slope: " + slope.Explanation);
            }

            foreach (var o in project.Overrides.Where(o => o.Target == connection.Id))
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "  Override: {0} -- {1} ({2:yyyy-MM-dd HH:mm} UTC)",
                                            o.What, o.Entered, o.Utc));

            var mine = (findings ?? new List<QcFinding>()).Where(f => f.ConnectionId == connection.Id ||
                (f.StructureId == connection.FromStructureId && f.PipeId == connection.FromPipeId)).ToList();
            sb.AppendLine(mine.Count == 0 ? "  Warnings: none" : "  Warnings:");
            foreach (var f in mine) sb.AppendLine("    ! " + f.Message);

            return sb.ToString();
        }

        private static string ReferenceText(PipeObservation pipe)
        {
            switch (pipe.ReferenceBasis)
            {
                case ReferenceBasis.NotStated: return " (NOT STATED in the note -- unconfirmed)";
                case ReferenceBasis.FieldNoteConvention: return " (by field note convention: " + pipe.ReferenceNote + ")";
                case ReferenceBasis.ConfirmedByDrafter: return " (unmarked in the note; confirmed by the drafter)";
                case ReferenceBasis.EnteredByDrafter: return " (entered by the drafter)";
                default: return " (stated in the field note)";
            }
        }

        private static void AppendEnd(StringBuilder sb, string end, StructureRecord structure, PipeObservation pipe)
        {
            if (structure == null) return;
            sb.AppendLine("  " + end + ": " + structure.Label +
                          (structure.Cad != null
                              ? string.Format(CultureInfo.InvariantCulture, " (CAD rim {0:0.00}, N {1:0.00}, E {2:0.00})",
                                              structure.Cad.Rim, structure.Cad.Northing, structure.Cad.Easting)
                              : " (no CAD point)"));
            if (pipe == null) return;

            sb.AppendLine("    Observed: " + ConnectionFinder.Describe(pipe) +
                          (pipe.MeasuredDip.HasValue
                              ? string.Format(CultureInfo.InvariantCulture, ", dip {0:0.00}' to {1}{2}",
                                              pipe.MeasuredDip.Value, DipElevations.Describe(pipe.Reference),
                                              ReferenceText(pipe))
                              : ", not dipped") +
                          (pipe.Role != FlowRole.Unknown ? ", " + pipe.Role.ToString().ToUpperInvariant() : string.Empty) +
                          " [" + pipe.Source + "]");
            if (!string.IsNullOrEmpty(pipe.RawText)) sb.AppendLine("    Field note: " + pipe.RawText.Trim());

            var elevation = DipElevations.Pipe(structure, pipe);
            if (elevation != null) sb.AppendLine("    Calculated: " + elevation.Formula + string.Format(CultureInfo.InvariantCulture, " = {0:0.00}", elevation.Value));
        }
    }
}
