using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FieldCodes.Drafting;
using FieldCodes.Settings;

namespace FieldCodes.Utilities
{
    /// <summary>
    /// Elevations from rim and dip. Every result carries the rim, the dip and the
    /// reference it came from, so it can be reproduced. The elevation returned is
    /// the elevation OF THE MEASURED REFERENCE: a top-of-pipe dip gives the top of
    /// the pipe, never an invert.
    /// </summary>
    public static class DipElevations
    {
        public static CalculatedElevation FromDip(double rim, double dip, MeasurementReference reference)
        {
            return new CalculatedElevation
            {
                Value = rim - dip,
                RimUsed = rim,
                DipUsed = dip,
                Reference = reference,
                Formula = string.Format(CultureInfo.InvariantCulture,
                    "{0} = rim {1:0.00} - dip {2:0.00}", Describe(reference), rim, dip)
            };
        }

        /// <summary>The elevation of what the pipe dip was measured to, or null when
        /// the pipe was not dipped or the structure has no surveyed rim.</summary>
        public static CalculatedElevation Pipe(StructureRecord structure, PipeObservation pipe)
        {
            if (structure == null || structure.Cad == null || pipe == null) return null;
            if (!pipe.MeasuredDip.HasValue) return null;
            return FromDip(structure.Cad.Rim, pipe.MeasuredDip.Value, pipe.Reference);
        }

        public static CalculatedElevation Bottom(StructureRecord structure)
        {
            if (structure == null || structure.Cad == null || !structure.Field.BottomDip.HasValue) return null;
            return FromDip(structure.Cad.Rim, structure.Field.BottomDip.Value, MeasurementReference.BottomOfStructure);
        }

        public static CalculatedElevation Water(StructureRecord structure)
        {
            if (structure == null || structure.Cad == null || !structure.Field.WaterDip.HasValue) return null;
            return FromDip(structure.Cad.Rim, structure.Field.WaterDip.Value, MeasurementReference.WaterLevel);
        }

        /// <summary>
        /// The pipe's invert and crown where they follow from what was measured WITHOUT
        /// assuming wall thickness: an invert dip gives the invert, and the crown is
        /// the invert plus the nominal rise; a top-of-pipe dip gives the crown only.
        /// Returns false for either value that cannot be stated honestly.
        /// </summary>
        public static void Envelope(StructureRecord structure, PipeObservation pipe,
                                    out double? invert, out double? crown)
        {
            invert = null;
            crown = null;
            var measured = Pipe(structure, pipe);
            if (measured == null) return;

            var riseFt = pipe.HeightIn.HasValue ? pipe.HeightIn.Value / 12.0 : (double?)null;
            switch (measured.Reference)
            {
                case MeasurementReference.Invert:
                    invert = measured.Value;
                    if (riseFt.HasValue) crown = measured.Value + riseFt.Value;
                    break;
                case MeasurementReference.TopOfPipe:
                    crown = measured.Value;
                    break;
            }
        }

        public static string Describe(MeasurementReference reference)
        {
            switch (reference)
            {
                case MeasurementReference.Unspecified: return "elevation (reference not stated)";
                case MeasurementReference.Invert: return "invert";
                case MeasurementReference.TopOfPipe: return "top of pipe";
                case MeasurementReference.Springline: return "springline";
                case MeasurementReference.BottomOfStructure: return "bottom of structure";
                case MeasurementReference.WaterLevel: return "water level";
                case MeasurementReference.TopOfGrate: return "top of grate";
                case MeasurementReference.TopOfCasting: return "top of casting";
                default: return "other reference";
            }
        }
    }

    /// <summary>
    /// The drafter's review of what the crew recorded. Confirming a reference never
    /// changes the measurement; it records who decided and when.
    /// </summary>
    public static class ObservationReview
    {
        /// <summary>The elevation an unmarked dip would give if the assumed reference is
        /// right -- for display beside the flag, never for QC, slope or labels as fact.</summary>
        public static CalculatedElevation Assumed(StructureRecord structure, PipeObservation pipe,
                                                  UtilitySettings settings)
        {
            if (pipe == null || !pipe.ReferenceUnconfirmed) return null;
            var e = DipElevations.Pipe(structure, pipe);
            if (e == null) return null;
            e.Formula = "assumed " + DipElevations.Describe(settings.AssumedPipeReference) +
                        " (not confirmed) = rim " + e.RimUsed.ToString("0.00", CultureInfo.InvariantCulture) +
                        " - dip " + e.DipUsed.ToString("0.00", CultureInfo.InvariantCulture);
            return e;
        }

        /// <summary>Confirms what an unmarked dip was measured to. The dip value and its
        /// field-note source are untouched.</summary>
        public static bool ConfirmReference(UtilityProject project, StructureRecord structure,
                                            PipeObservation pipe, MeasurementReference reference)
        {
            if (pipe == null || reference == MeasurementReference.Unspecified) return false;
            if (!pipe.ReferenceUnconfirmed && pipe.Reference == reference) return false;

            var before = pipe.Reference + " (" + pipe.ReferenceBasis + ")";
            pipe.Reference = reference;
            pipe.ReferenceBasis = ReferenceBasis.ConfirmedByDrafter;
            project.Overrides.Add(new ManualOverride
            {
                Target = pipe.Id,
                What = "Measurement reference confirmed",
                Generated = before,
                Entered = (structure != null ? structure.Label + " " : string.Empty) +
                          ConnectionFinder.Describe(pipe) + ": " + DipElevations.Describe(reference),
                Utc = DateTime.UtcNow
            });
            return true;
        }
    }

    /// <summary>A structure's inside width and where it came from: the field note,
    /// then the drafter's entry, then a documented profile standard. Unknown when none
    /// of those say -- a size is never inferred from the structure type alone.</summary>
    public static class StructureDimensions
    {
        public static double? InsideWidth(StructureRecord structure, UtilitySettings settings,
                                          out DimensionSource source)
        {
            source = DimensionSource.Unknown;
            if (structure == null) return null;
            if (structure.Field.InsideWidthIn.HasValue)
            {
                source = DimensionSource.FieldObserved;
                return structure.Field.InsideWidthIn;
            }
            if (structure.EnteredInsideWidthIn.HasValue)
            {
                source = DimensionSource.UserEntry;
                return structure.EnteredInsideWidthIn;
            }
            var rule = settings != null ? settings.FindCode(structure.EffectiveCode) : null;
            if (rule != null && rule.InsideWidthIn.HasValue)
            {
                source = DimensionSource.Profile;
                return rule.InsideWidthIn;
            }
            return null;
        }

        /// <summary>Round, rectangular or no size, from the structure's code.</summary>
        public static StructureShape ShapeOf(StructureRecord structure, UtilitySettings settings)
        {
            // The drafter's type when they set one, otherwise the field code (see StructureRecord.EffectiveCode).
            var rule = structure != null && settings != null ? settings.FindCode(structure.EffectiveCode) : null;
            if (rule == null && structure != null && settings != null) rule = settings.FindCode(structure.StructureType);
            return rule != null ? rule.Shape : StructureShape.Round;
        }

        /// <summary>The size as it reads on a label: 48" for a round structure, 24"x36" for
        /// a rectangular one, nothing when unknown or not sized.</summary>
        public static string SizeText(StructureRecord structure, UtilitySettings settings)
        {
            var shape = ShapeOf(structure, settings);
            if (shape == StructureShape.NoSize) return null;
            DimensionSource source;
            var width = InsideWidth(structure, settings, out source);
            if (!width.HasValue) return null;
            var w = UtilityLabelFormatter.SizeNumber(width.Value) + "\"";
            if (shape == StructureShape.Rectangular && structure.EnteredInsideLengthIn.HasValue)
                return w + "x" + UtilityLabelFormatter.SizeNumber(structure.EnteredInsideLengthIn.Value) + "\"";
            return w;
        }

        public static string Describe(DimensionSource source)
        {
            switch (source)
            {
                case DimensionSource.FieldObserved: return "FIELD OBSERVED";
                case DimensionSource.UserEntry: return "ENTERED BY DRAFTER";
                case DimensionSource.Profile: return "PROFILE";
                default: return "UNKNOWN";
            }
        }
    }

    /// <summary>How a slope was (or was not) obtained.</summary>
    public enum SlopeBasis
    {
        /// <summary>Both ends were dipped to the same kind of reference.</summary>
        CalculatedFromBothObservations,
        /// <summary>Only one end has an observed elevation; no slope is stated.</summary>
        MissingOppositeObservation,
        /// <summary>Both dipped, but to different references -- not comparable.</summary>
        IncomparableReferences,
        /// <summary>The structures lack survey positions or coincide.</summary>
        NoGeometry
    }

    public sealed class SlopeResult
    {
        public SlopeBasis Basis { get; set; }

        /// <summary>Percent, positive downhill from the higher end to the lower.</summary>
        public double? SlopePercent { get; set; }

        /// <summary>Signed percent from the FROM structure to the TO structure:
        /// negative means the pipe falls toward TO.</summary>
        public double? SignedPercentFromTo { get; set; }

        public double HorizontalDistance { get; set; }
        public CalculatedElevation FromElevation { get; set; }
        public CalculatedElevation ToElevation { get; set; }
        public string Explanation { get; set; }
    }

    /// <summary>
    /// Pipe slope, only ever from two field observations. With one end dipped, the
    /// result says so and states no slope -- the missing invert is never made up.
    /// </summary>
    public static class SlopeCalculator
    {
        public static double Distance(StructureRecord a, StructureRecord b)
        {
            if (a == null || b == null || a.Cad == null || b.Cad == null) return 0;
            var dx = b.Cad.Easting - a.Cad.Easting;
            var dy = b.Cad.Northing - a.Cad.Northing;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        public static SlopeResult Compute(UtilityProject project, PipeConnection connection)
        {
            var from = project.Structure(connection.FromStructureId);
            var to = project.Structure(connection.ToStructureId);
            var result = new SlopeResult { HorizontalDistance = Distance(from, to) };

            if (from == null || to == null || from.Cad == null || to.Cad == null ||
                result.HorizontalDistance < 0.01)
            {
                result.Basis = SlopeBasis.NoGeometry;
                result.Explanation = "No slope: the structures have no usable survey positions.";
                return result;
            }

            var fromPipe = project.Pipe(from.Id, connection.FromPipeId);
            var toPipe = connection.ToPipeId != null ? project.Pipe(to.Id, connection.ToPipeId) : null;

            result.FromElevation = DipElevations.Pipe(from, fromPipe);
            result.ToElevation = DipElevations.Pipe(to, toPipe);

            if (result.FromElevation == null || result.ToElevation == null)
            {
                result.Basis = SlopeBasis.MissingOppositeObservation;
                result.Explanation = "No slope: " +
                    (result.FromElevation == null ? from.Label : to.Label) +
                    " has no observed dip for this pipe. The missing elevation is not assumed.";
                return result;
            }

            if (fromPipe.ReferenceUnconfirmed || toPipe.ReferenceUnconfirmed)
            {
                result.Basis = SlopeBasis.IncomparableReferences;
                result.Explanation = "No slope: the dip at " +
                    (fromPipe.ReferenceUnconfirmed ? from.Label : to.Label) +
                    " does not say what it was measured to. Confirm the reference first.";
                return result;
            }

            if (!Comparable(fromPipe, toPipe))
            {
                result.Basis = SlopeBasis.IncomparableReferences;
                result.Explanation = string.Format(CultureInfo.InvariantCulture,
                    "No slope: dips were taken to {0} at {1} and {2} at {3}, which are not comparable.",
                    DipElevations.Describe(fromPipe.Reference), from.Label,
                    DipElevations.Describe(toPipe.Reference), to.Label);
                return result;
            }

            var drop = result.FromElevation.Value - result.ToElevation.Value;
            result.SignedPercentFromTo = -drop / result.HorizontalDistance * 100.0;
            result.SlopePercent = Math.Abs(drop) / result.HorizontalDistance * 100.0;
            result.Basis = SlopeBasis.CalculatedFromBothObservations;
            result.Explanation = string.Format(CultureInfo.InvariantCulture,
                "Calculated from field dips at both structures: ({0:0.00} - {1:0.00}) / {2:0.00}' = {3:0.000}%",
                result.FromElevation.Value, result.ToElevation.Value, result.HorizontalDistance,
                result.SlopePercent.Value);
            return result;
        }

        /// <summary>Two dips describe the same line along the pipe only when taken to
        /// the same reference -- and, for crown or springline, on pipes of the same
        /// rise, so the offset from the invert cancels.</summary>
        public static bool Comparable(PipeObservation a, PipeObservation b)
        {
            if (a == null || b == null || a.Reference != b.Reference) return false;
            if (a.Reference == MeasurementReference.Invert) return true;
            if (a.Reference == MeasurementReference.TopOfPipe || a.Reference == MeasurementReference.Springline)
                return a.HeightIn.HasValue && b.HeightIn.HasValue &&
                       Math.Abs(a.HeightIn.Value - b.HeightIn.Value) < 0.01;
            return false;
        }
    }

    /// <summary>One structure the search thinks this pipe may run to.</summary>
    public sealed class ConnectionCandidate
    {
        public StructureRecord Structure { get; set; }
        public PipeObservation MatchingPipe { get; set; }
        public double Distance { get; set; }
        public double AzimuthToCandidate { get; set; }
        public double DeviationDegrees { get; set; }
        public double Score { get; set; }
        public Confidence Confidence { get; set; }
        public IList<string> Basis { get; set; }

        public ConnectionCandidate() { Basis = new List<string>(); }
    }

    /// <summary>
    /// Suggests which structure an observed pipe runs to. The observed direction
    /// opens a search cone; structures inside it are ranked by how far off the
    /// direction they are, how far away, whether the far structure has a matching
    /// observed pipe pointing back, and whether size, material and system agree.
    /// A suggestion is only ever a suggestion: nothing is confirmed here, and CAD
    /// geometry never creates a pipe on its own -- the search starts from an
    /// observed pipe or it does not start.
    /// </summary>
    public static class ConnectionFinder
    {
        public static IList<ConnectionCandidate> Find(UtilityProject project,
                                                      StructureRecord from,
                                                      PipeObservation pipe,
                                                      UtilitySettings settings)
        {
            var candidates = new List<ConnectionCandidate>();
            if (project == null || from == null || pipe == null || from.Cad == null) return candidates;
            if (!pipe.Direction.IsKnown) return candidates;

            settings = settings ?? new UtilitySettings();
            var cone = settings.SearchConeDegrees;
            var maxDistance = settings.SearchDistanceFt;
            var azimuth = pipe.Direction.AzimuthDegrees.Value;

            foreach (var target in project.Structures)
            {
                if (target.Id == from.Id || target.Cad == null) continue;

                var dx = target.Cad.Easting - from.Cad.Easting;
                var dy = target.Cad.Northing - from.Cad.Northing;
                var distance = Math.Sqrt(dx * dx + dy * dy);
                if (distance < 0.01 || distance > maxDistance) continue;

                var bearingTo = SurveyDirection.AzimuthFromVector(dx, dy);
                var deviation = AngleBetween(azimuth, bearingTo);
                if (deviation > cone) continue;

                var candidate = new ConnectionCandidate
                {
                    Structure = target,
                    Distance = distance,
                    AzimuthToCandidate = bearingTo,
                    DeviationDegrees = deviation
                };

                var score = (1.0 - deviation / cone) * 40.0 + (1.0 - distance / maxDistance) * 15.0;
                candidate.Basis.Add(string.Format(CultureInfo.InvariantCulture,
                    "{0:0.0}° off the observed {1} direction, {2:0.00}' away",
                    deviation, pipe.Direction.Text, distance));

                // A pipe observed at the far structure pointing back at this one.
                var back = (bearingTo + 180.0) % 360.0;
                PipeObservation match = null;
                var matchDeviation = double.MaxValue;
                foreach (var other in target.Field.Pipes)
                {
                    if (!other.Direction.IsKnown) continue;
                    var d = AngleBetween(other.Direction.AzimuthDegrees.Value, back);
                    var taken = project.ConnectionFor(target.Id, other.Id);
                    if (taken != null && taken.FromPipeId != pipe.Id && taken.ToPipeId != pipe.Id) continue;
                    if (d <= cone && d < matchDeviation)
                    {
                        match = other;
                        matchDeviation = d;
                    }
                }

                var sizeMatch = false;
                var materialMatch = false;
                if (match != null)
                {
                    candidate.MatchingPipe = match;
                    score += 30.0;
                    candidate.Basis.Add("Matching field observation: " + Describe(match));

                    sizeMatch = pipe.WidthIn.HasValue && match.WidthIn.HasValue &&
                                Math.Abs(pipe.WidthIn.Value - match.WidthIn.Value) < 0.01 &&
                                (!pipe.HeightIn.HasValue || !match.HeightIn.HasValue ||
                                 Math.Abs(pipe.HeightIn.Value - match.HeightIn.Value) < 0.01);
                    if (sizeMatch) score += 8.0;
                    else candidate.Basis.Add("Size differs from the opposite observation");

                    materialMatch = pipe.Material != null && match.Material != null &&
                                    string.Equals(pipe.Material, match.Material, StringComparison.OrdinalIgnoreCase);
                    if (materialMatch) score += 5.0;
                    else if (pipe.Material != null && match.Material != null)
                        candidate.Basis.Add("Material differs from the opposite observation");

                    // Flow sense: an OUT pipe should run to a lower IN at the far end.
                    var here = DipElevations.Pipe(from, pipe);
                    var there = DipElevations.Pipe(target, match);
                    if (here != null && there != null && SlopeCalculator.Comparable(pipe, match))
                    {
                        var fallsAway = here.Value > there.Value;
                        var expectFall = pipe.Role == FlowRole.Out || match.Role == FlowRole.In;
                        var expectRise = pipe.Role == FlowRole.In || match.Role == FlowRole.Out;
                        if ((expectFall && fallsAway) || (expectRise && !fallsAway)) score += 5.0;
                    }
                }
                else
                {
                    candidate.Basis.Add("Based on field direction and CAD geometry; no corresponding field dip observed at " + target.Label);
                }

                if (target.System == from.System) score += 7.0;
                else if (target.System != UtilitySystem.Other && from.System != UtilitySystem.Other)
                {
                    score -= 15.0;
                    candidate.Basis.Add("Different utility system (" + from.System + " to " + target.System + ")");
                }

                candidate.Score = score;
                candidate.Confidence = match == null
                    ? Confidence.Low
                    : (sizeMatch && (materialMatch || pipe.Material == null || match.Material == null) &&
                       deviation <= cone / 2.0 && matchDeviation <= cone / 2.0)
                        ? Confidence.High
                        : Confidence.Medium;

                candidates.Add(candidate);
            }

            return candidates.OrderByDescending(c => c.Score).ToList();
        }

        /// <summary>The smallest angle between two azimuths, 0-180 degrees.</summary>
        public static double AngleBetween(double a, double b)
        {
            var d = Math.Abs(((a - b) % 360.0 + 360.0) % 360.0);
            return d > 180.0 ? 360.0 - d : d;
        }

        public static string Describe(PipeObservation pipe)
        {
            if (pipe == null) return "(none)";
            var parts = new List<string>();
            parts.Add(UtilityLabelFormatter.FormatSize(pipe));
            if (!string.IsNullOrEmpty(pipe.Material)) parts.Add(pipe.Material);
            parts.Add(pipe.Direction != null ? (pipe.Direction.Text ?? "?") : "?");
            return string.Join(" ", parts.ToArray());
        }

        /// <summary>
        /// Records the drafter's decision. Confirming a candidate never invents the
        /// opposite observation: with no matching pipe at the far structure, the
        /// connection stays one-sided and says so.
        /// </summary>
        public static PipeConnection Accept(UtilityProject project, StructureRecord from,
                                            PipeObservation pipe, ConnectionCandidate candidate,
                                            bool manual, string note)
        {
            var existing = project.ConnectionFor(from.Id, pipe.Id);
            if (existing != null) project.Connections.Remove(existing);

            var connection = new PipeConnection
            {
                FromStructureId = from.Id,
                FromPipeId = pipe.Id,
                ToStructureId = candidate.Structure.Id,
                ToPipeId = candidate.MatchingPipe != null ? candidate.MatchingPipe.Id : null,
                Status = manual ? ConnectionStatus.ManualOverride : ConnectionStatus.Confirmed,
                Confidence = manual ? Confidence.None : candidate.Confidence,
                OverrideNote = note
            };
            foreach (var line in candidate.Basis) connection.Basis.Add(line);
            if (manual)
                connection.Basis.Add("Connection chosen manually by the drafter" +
                                     (string.IsNullOrEmpty(note) ? "." : ": " + note));

            project.Connections.Add(connection);
            project.Overrides.Add(new ManualOverride
            {
                Target = connection.Id,
                What = manual ? "Manual connection" : "Confirmed connection",
                Generated = manual ? null : candidate.Confidence + " confidence candidate",
                Entered = from.Label + " -> " + candidate.Structure.Label,
                Utc = DateTime.UtcNow
            });
            return connection;
        }

        /// <summary>A manual pick of a structure the search did not offer: scored as
        /// a candidate so the basis still describes what the field notes support.</summary>
        public static ConnectionCandidate ManualCandidate(UtilityProject project, StructureRecord from,
                                                          PipeObservation pipe, StructureRecord target,
                                                          UtilitySettings settings)
        {
            settings = settings ?? new UtilitySettings();
            var searched = Find(project, from, pipe, settings).FirstOrDefault(c => c.Structure.Id == target.Id);
            if (searched != null) return searched;

            var candidate = new ConnectionCandidate
            {
                Structure = target,
                Distance = SlopeCalculator.Distance(from, target),
                Confidence = Confidence.None
            };

            if (from.Cad != null && target.Cad != null)
            {
                candidate.AzimuthToCandidate = SurveyDirection.AzimuthFromVector(
                    target.Cad.Easting - from.Cad.Easting, target.Cad.Northing - from.Cad.Northing);
                if (pipe.Direction.IsKnown)
                {
                    candidate.DeviationDegrees = AngleBetween(pipe.Direction.AzimuthDegrees.Value,
                                                              candidate.AzimuthToCandidate);
                    candidate.Basis.Add(string.Format(CultureInfo.InvariantCulture,
                        "Outside the search: {0:0.0}° off the observed {1} direction, {2:0.00}' away",
                        candidate.DeviationDegrees, pipe.Direction.Text, candidate.Distance));
                }

                // Still record an opposite observation if one points back.
                var back = (candidate.AzimuthToCandidate + 180.0) % 360.0;
                foreach (var other in target.Field.Pipes)
                {
                    if (!other.Direction.IsKnown) continue;
                    if (AngleBetween(other.Direction.AzimuthDegrees.Value, back) <= settings.SearchConeDegrees)
                    {
                        candidate.MatchingPipe = other;
                        candidate.Basis.Add("Matching field observation: " + Describe(other));
                        break;
                    }
                }
            }

            if (candidate.MatchingPipe == null)
                candidate.Basis.Add("No corresponding field dip observed at " + target.Label);
            return candidate;
        }

        public static void LeaveUnresolved(UtilityProject project, StructureRecord from, PipeObservation pipe)
        {
            var existing = project.ConnectionFor(from.Id, pipe.Id);
            if (existing != null) project.Connections.Remove(existing);
            project.Connections.Add(new PipeConnection
            {
                FromStructureId = from.Id,
                FromPipeId = pipe.Id,
                Status = ConnectionStatus.LeftUnresolved,
                Basis = { "Left unresolved by the drafter" }
            });
        }

        /// <summary>Records that a pipe runs outside the survey limits: settled, with no
        /// structure at the far end and nothing for the crew to chase.</summary>
        public static void MarkOutsideLimits(UtilityProject project, StructureRecord from, PipeObservation pipe)
        {
            var existing = project.ConnectionFor(from.Id, pipe.Id);
            if (existing != null) project.Connections.Remove(existing);
            project.Connections.Add(new PipeConnection
            {
                FromStructureId = from.Id,
                FromPipeId = pipe.Id,
                Status = ConnectionStatus.OutsideSurveyLimits,
                Basis = { "Runs outside the survey limits (drafter)" }
            });
        }
    }
}

namespace FieldCodes.Utilities
{
    /// <summary>How a pipe is represented in the drawing. The observed width
    /// controls the geometry -- for non-round pipe that is the span, not the rise.</summary>
    public static class PipeDraftingRules
    {
        /// <summary>Double line when the observed width exceeds the profile threshold;
        /// at or under it, a single centerline.</summary>
        public static bool DrawDoubleLine(PipeObservation pipe, FieldCodes.Settings.UtilitySettings settings)
        {
            return pipe != null && pipe.WidthIn.HasValue && settings != null &&
                   pipe.WidthIn.Value > settings.DoubleLineThresholdIn;
        }

        /// <summary>Half the observed width in feet: the offset of each side line.</summary>
        public static double HalfWidthFeet(PipeObservation pipe)
        {
            return pipe != null && pipe.WidthIn.HasValue ? pipe.WidthIn.Value / 24.0 : 0.0;
        }
    }
}
