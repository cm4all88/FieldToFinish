using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FieldCodes.Drafting;
using FieldCodes.Settings;

namespace FieldCodes.Utilities.FieldNotes
{
    /// <summary>
    /// Reads the Dip Builder's project into a field-notes record. Every elevation comes from DipElevations, every
    /// slope from SlopeCalculator and every warning from UtilityQc -- the record says exactly what the Dip Builder,
    /// its labels and its QC say, and nothing they do not. Measured depths are copied with the precision they were
    /// recorded with; calculated elevations are rounded the way the structure labels are (ElevationDecimals).
    /// </summary>
    public static class FieldNotesReportBuilder
    {
        /// <summary>
        /// A measured depth as it prints: every digit that was recorded (nothing is rounded away), written to at
        /// least hundredths the way dips are read and the structure labels show them -- 6.8 prints 6.80, 6.125 stays.
        /// </summary>
        public static string Depth(double value)
        {
            var text = QuickPipeEntry.Exact(value);
            var dot = text.IndexOf('.');
            if (dot < 0) return text + ".00";
            return text.Length - dot - 1 == 1 ? text + "0" : text;
        }

        /// <summary>P1, P2... -- the pipes in the order they were entered at the structure.</summary>
        public static string PipeKey(int index)
        {
            return "P" + (index + 1).ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Builds the record. selectedStructureIds limits it to those structures (null: every complete one).
        /// Incomplete observations are never included, whether selected or not; every incomplete structure in the
        /// project is listed in <see cref="FieldNotesReport.Incomplete"/> with what it is missing.
        /// staleStructureIds are structures whose survey point changed since their elevations were calculated.
        /// </summary>
        public static FieldNotesReport Build(UtilityProject project, UtilitySettings settings, FieldNotesHeader header,
                                             string drawingName, ICollection<string> selectedStructureIds,
                                             ICollection<string> staleStructureIds)
        {
            settings = settings ?? new UtilitySettings();
            var report = new FieldNotesReport
            {
                Header = header != null ? header.Copy() : new FieldNotesHeader(),
                DrawingName = drawingName,
                ElevationDecimals = Math.Max(0, Math.Min(4, settings.ElevationDecimals))
            };
            if (project == null) return report;

            var findings = UtilityQc.Evaluate(project, settings);
            foreach (var structure in project.Structures)
            {
                var status = DipObservationStatus.Check(project, structure, staleStructureIds);
                if (!status.IsComplete)
                {
                    var excluded = new ExcludedObservation { StructureId = structure.Id, Label = structure.Label };
                    foreach (var reason in status.Missing) excluded.Reasons.Add(reason);
                    report.Incomplete.Add(excluded);
                    continue;
                }
                if (selectedStructureIds != null && !selectedStructureIds.Contains(structure.Id)) continue;
                report.Structures.Add(Structure(project, structure, settings, findings, report.ElevationDecimals));
            }

            var byConvention = report.Structures.SelectMany(s => s.Pipes)
                .Any(p => p.ReferenceBasisWords != null && p.ReferenceBasisWords.StartsWith("office", StringComparison.Ordinal));
            if (byConvention)
                report.ConventionNote = string.IsNullOrWhiteSpace(settings.UnmarkedDipConventionSource)
                    ? "Unmarked dips are inverts by the office field-note convention."
                    : settings.UnmarkedDipConventionSource.Trim();
            return report;
        }

        private static StructureNotes Structure(UtilityProject project, StructureRecord s, UtilitySettings settings,
                                                IList<QcFinding> findings, int decimals)
        {
            var elevation = "F" + decimals.ToString(CultureInfo.InvariantCulture);
            var rule = settings.FindCode(s.EffectiveCode) ?? settings.FindCode(s.StructureType);
            var notes = new StructureNotes
            {
                StructureId = s.Id,
                Label = s.Label,
                PointNumber = s.Field.PointNumber,
                Code = s.EffectiveCode,
                FieldCode = s.Field.FieldCode,
                TypeSetByDrafter = s.TypeSetByDrafter,
                TypeName = rule != null ? rule.Type : null,
                System = s.System,
                Shape = StructureDimensions.ShapeOf(s, settings),
                Description = s.Cad != null && !string.IsNullOrWhiteSpace(s.Cad.Description) ? s.Cad.Description.Trim() : null
            };

            if (s.Cad != null)
            {
                notes.Rim = s.Cad.Rim;
                notes.RimText = s.Cad.Rim.ToString(elevation, CultureInfo.InvariantCulture);
                notes.Northing = s.Cad.Northing;
                notes.Easting = s.Cad.Easting;
            }

            notes.InsideSize = StructureDimensions.SizeText(s, settings);
            if (notes.InsideSize != null)
            {
                DimensionSource source;
                StructureDimensions.InsideWidth(s, settings, out source);
                notes.InsideSizeSource = source == DimensionSource.FieldObserved ? "field observed"
                    : source == DimensionSource.UserEntry ? "entered by drafter"
                    : source == DimensionSource.Profile ? "office standard size, not observed"
                    : null;
            }

            if (s.Field.BottomDip.HasValue)
            {
                notes.BottomDipText = Depth(s.Field.BottomDip.Value);
                notes.Bottom = DipElevations.Bottom(s);
                if (notes.Bottom != null) notes.BottomText = notes.Bottom.Value.ToString(elevation, CultureInfo.InvariantCulture);
            }
            if (s.Field.WaterDip.HasValue)
            {
                notes.WaterDipText = Depth(s.Field.WaterDip.Value);
                notes.Water = DipElevations.Water(s);
                if (notes.Water != null) notes.WaterText = notes.Water.Value.ToString(elevation, CultureInfo.InvariantCulture);
            }

            foreach (var c in s.Field.Conditions ?? new List<string>()) notes.Conditions.Add(c);
            foreach (var n in s.Field.Notes ?? new List<string>()) if (!string.IsNullOrWhiteSpace(n)) notes.Notes.Add(n.Trim());
            if (!string.IsNullOrWhiteSpace(s.Field.RawText))
                foreach (var line in s.Field.RawText.Replace("\r\n", "\n").Split('\n'))
                    if (line.Trim().Length > 0) notes.RawLines.Add(line.Trim());

            for (var i = 0; i < s.Field.Pipes.Count; i++)
                notes.Pipes.Add(Pipe(project, s, s.Field.Pipes[i], i, settings, elevation));

            foreach (var f in findings.Where(f => f.StructureId == s.Id).OrderByDescending(f => f.Severity))
                notes.Warnings.Add(new FieldNotesWarning { Severity = f.Severity, Text = Tidy(f, s) });
            foreach (var item in project.ManualRevisit.Where(r => r != null && string.Equals(r.StructureLabel, s.Label, StringComparison.OrdinalIgnoreCase)))
                notes.Warnings.Add(new FieldNotesWarning { Severity = Severity.Info, Text = "Office note for the field: " + item.Text });

            notes.Sketch = Sketch(project, s, notes);
            return notes;
        }

        private static PipeNotes Pipe(UtilityProject project, StructureRecord s, PipeObservation pipe, int index,
                                      UtilitySettings settings, string elevation)
        {
            var notes = new PipeNotes
            {
                Key = PipeKey(index),
                PipeId = pipe.Id,
                Role = pipe.Role,
                Size = pipe.WidthIn.HasValue ? UtilityLabelFormatter.FormatSize(pipe) : null,
                Shape = pipe.Shape != PipeShape.Round ? pipe.Shape.ToString().ToUpperInvariant() : null,
                Material = string.IsNullOrWhiteSpace(pipe.Material) ? null : pipe.Material.Trim(),
                Direction = pipe.Direction != null && pipe.Direction.IsKnown && !string.IsNullOrWhiteSpace(pipe.Direction.Text)
                    ? pipe.Direction.Text.Trim() : null,
                MeasuredDip = pipe.MeasuredDip,
                MeasuredDipText = pipe.MeasuredDip.HasValue ? Depth(pipe.MeasuredDip.Value) : null,
                Reference = pipe.Reference,
                ReferenceWords = ReferenceWords(pipe.Reference),
                ReferenceBasisWords = BasisWords(pipe),
                Notes = string.IsNullOrWhiteSpace(pipe.Notes) ? null : pipe.Notes.Trim(),
                RawText = string.IsNullOrWhiteSpace(pipe.RawText) ? null : pipe.RawText.Trim()
            };
            foreach (var c in pipe.Conditions ?? new List<string>()) notes.Conditions.Add(c);
            foreach (var p in pipe.Prefilled ?? new List<string>()) notes.CarriedOver.Add(p);
            notes.CarriedOverFrom = pipe.PrefilledFrom;

            // The library's calculation, and only where the reference makes it honest: an invert only from a dip
            // taken to the invert (DipElevations.Envelope), never from a top-of-pipe dip or an unstated one.
            var measured = DipElevations.Pipe(s, pipe);
            double? invert, crown;
            DipElevations.Envelope(s, pipe, out invert, out crown);
            if (measured != null)
            {
                switch (pipe.Reference)
                {
                    case MeasurementReference.Invert:
                        notes.Elevation = measured;
                        notes.ElevationKind = ElevationKind.Invert;
                        notes.ElevationLabel = "INV";
                        notes.ElevationText = (invert ?? measured.Value).ToString(elevation, CultureInfo.InvariantCulture);
                        break;
                    case MeasurementReference.TopOfPipe:
                        notes.Elevation = measured;
                        notes.ElevationKind = ElevationKind.TopOfPipe;
                        notes.ElevationLabel = "TOP";
                        notes.ElevationText = measured.Value.ToString(elevation, CultureInfo.InvariantCulture);
                        notes.InvertNote = "Invert not calculated: the dip was taken to the top of the pipe and the wall thickness is not known.";
                        break;
                    case MeasurementReference.Springline:
                        notes.Elevation = measured;
                        notes.ElevationKind = ElevationKind.Springline;
                        notes.ElevationLabel = "SPR";
                        notes.ElevationText = measured.Value.ToString(elevation, CultureInfo.InvariantCulture);
                        notes.InvertNote = "Invert not calculated: the dip was taken to the springline.";
                        break;
                    case MeasurementReference.Unspecified:
                        // Never presented as an elevation of anything until someone says what it was taken to.
                        notes.InvertNote = "No elevation given: the dip does not say what it was measured to.";
                        break;
                    default:
                        notes.Elevation = measured;
                        notes.ElevationKind = ElevationKind.Other;
                        notes.ElevationLabel = "EL";
                        notes.ElevationText = measured.Value.ToString(elevation, CultureInfo.InvariantCulture);
                        notes.InvertNote = "Not an invert: the dip was taken to the " + DipElevations.Describe(pipe.Reference) + ".";
                        break;
                }
            }

            var c2 = project.ConnectionFor(s.Id, pipe.Id);
            if (c2 == null) notes.ConnectsTo = "Not connected";
            else
            {
                switch (c2.Status)
                {
                    case ConnectionStatus.Confirmed:
                    case ConnectionStatus.ManualOverride:
                        var otherId = c2.FromStructureId == s.Id && c2.FromPipeId == pipe.Id ? c2.ToStructureId : c2.FromStructureId;
                        var other = project.Structure(otherId);
                        notes.ConnectsToShort = other != null ? other.Label : null;
                        var farPipeMissing = c2.FromStructureId == s.Id && c2.FromPipeId == pipe.Id && c2.ToPipeId == null;
                        notes.ConnectsTo = (other != null ? other.Label : "(structure missing)") +
                                           (c2.Status == ConnectionStatus.Confirmed ? " (confirmed)" : " (drafter's pick)") +
                                           (farPipeMissing ? "; no pipe observed at that end" : string.Empty);
                        if (other != null && c2.ToStructureId != null) notes.Slope = SlopeText(project, c2, settings);
                        break;
                    case ConnectionStatus.OutsideSurveyLimits:
                        notes.ConnectsTo = "Runs outside the survey limits";
                        break;
                    case ConnectionStatus.LeftUnresolved:
                        notes.ConnectsTo = "Not resolved (left open)";
                        break;
                    case ConnectionStatus.Probable:
                        notes.ConnectsTo = "Suggested only, not confirmed";
                        break;
                    default:
                        notes.ConnectsTo = "Not connected";
                        break;
                }
            }
            return notes;
        }

        private static string SlopeText(UtilityProject project, PipeConnection connection, UtilitySettings settings)
        {
            var slope = SlopeCalculator.Compute(project, connection);
            if (slope.Basis != SlopeBasis.CalculatedFromBothObservations || !slope.SlopePercent.HasValue)
                return slope.Explanation;
            var low = project.Structure(slope.SignedPercentFromTo < 0 ? connection.ToStructureId : connection.FromStructureId);
            var format = "F" + Math.Max(0, Math.Min(4, settings.SlopeDecimals)).ToString(CultureInfo.InvariantCulture);
            return "Slope " + slope.SlopePercent.Value.ToString(format, CultureInfo.InvariantCulture) +
                   "% calculated from the dips at both ends" +
                   (low != null && Math.Abs(slope.SignedPercentFromTo ?? 0) > 1e-9 ? ", low end at " + low.Label : string.Empty) + ".";
        }

        private static StructureSketch Sketch(UtilityProject project, StructureRecord s, StructureNotes notes)
        {
            var sketch = new StructureSketch { Shape = notes.Shape, Code = notes.Code };
            for (var i = 0; i < s.Field.Pipes.Count; i++)
            {
                var pipe = s.Field.Pipes[i];
                var pn = notes.Pipes[i];
                double azimuth;
                SketchDirectionBasis basis;
                if (pipe.Direction != null && pipe.Direction.IsKnown)
                {
                    azimuth = pipe.Direction.AzimuthDegrees.Value;
                    basis = pipe.IsPrefilled("direction") ? SketchDirectionBasis.CarriedOver : SketchDirectionBasis.Observed;
                }
                else
                {
                    // No direction recorded. Only an accepted connection to a surveyed structure gives one, and it is
                    // drawn as inferred; otherwise the pipe is listed and not drawn at all.
                    var c = project.ConnectionFor(s.Id, pipe.Id);
                    StructureRecord other = null;
                    if (c != null && c.IsAccepted && c.ToStructureId != null)
                        other = project.Structure(c.FromStructureId == s.Id && c.FromPipeId == pipe.Id ? c.ToStructureId : c.FromStructureId);
                    if (other == null || other.Cad == null || s.Cad == null || SlopeCalculator.Distance(s, other) < 0.01)
                    {
                        sketch.NotDrawn.Add(pn.Key);
                        continue;
                    }
                    azimuth = SurveyDirection.AzimuthFromVector(other.Cad.Easting - s.Cad.Easting, other.Cad.Northing - s.Cad.Northing);
                    basis = SketchDirectionBasis.FromConnection;
                }

                sketch.Pipes.Add(new SketchPipe
                {
                    Key = pn.Key,
                    AzimuthDegrees = azimuth,
                    Basis = basis,
                    Role = pn.Role,
                    Caption = pn.Key + " " + (pn.Size ?? "size?") + (pn.Material != null ? " " + pn.Material : string.Empty),
                    ElevationLabel = pn.ElevationLabel,
                    ElevationText = pn.ElevationText,
                    ConnectsTo = pn.ConnectsToShort
                });
            }
            return sketch;
        }

        /// <summary>Invert, Top of pipe... with a capital, for the "measured to" column.</summary>
        public static string ReferenceWords(MeasurementReference reference)
        {
            if (reference == MeasurementReference.Unspecified) return "Not stated";
            var d = DipElevations.Describe(reference);
            return d.Length == 0 ? d : char.ToUpperInvariant(d[0]) + d.Substring(1);
        }

        private static string BasisWords(PipeObservation pipe)
        {
            switch (pipe.ReferenceBasis)
            {
                case ReferenceBasis.StatedInFieldNote: return "stated in the field note";
                case ReferenceBasis.FieldNoteConvention: return "office field-note convention, unmarked dip";
                case ReferenceBasis.ConfirmedByDrafter: return "unmarked in the note; confirmed by the drafter";
                case ReferenceBasis.EnteredByDrafter: return "entered by the drafter from the field book";
                default: return "not stated";
            }
        }

        /// <summary>
        /// QC messages start with the structure's label, which is noise under its own heading, and name a pipe by
        /// its description; here the pipe also gets its P-number so the warning can be found in the table.
        /// </summary>
        private static string Tidy(QcFinding finding, StructureRecord structure)
        {
            var message = finding.Message ?? string.Empty;
            if (structure != null && message.StartsWith(structure.Label + ": ", StringComparison.Ordinal))
                message = message.Substring(structure.Label.Length + 2);
            else if (structure != null && message.StartsWith(structure.Label + " ", StringComparison.Ordinal))
                message = message.Substring(structure.Label.Length + 1);
            if (message.Length > 0) message = char.ToUpperInvariant(message[0]) + message.Substring(1);
            if (structure != null && finding.PipeId != null)
            {
                var index = structure.Field.Pipes.FindIndex(p => p.Id == finding.PipeId);
                if (index >= 0)
                {
                    var described = ConnectionFinder.Describe(structure.Field.Pipes[index]);
                    message = message.StartsWith(described, StringComparison.Ordinal)
                        ? PipeKey(index) + " " + message
                        : PipeKey(index) + ": " + message;
                }
            }
            return message.Trim();
        }
    }
}
