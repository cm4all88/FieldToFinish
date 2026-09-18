using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using FieldCodes.Settings;

namespace FieldCodes.Utilities
{
    /// <summary>Every QC check the dip builder runs. Names are stable so a finding
    /// can be filtered, counted and sent to the field.</summary>
    public enum QcCode
    {
        SlopeOutOfRange, ReverseFlow, InvertAboveRim, InvertBelowBottom, InsufficientCover,
        PipeTooLargeForStructure, SizeMismatch, MaterialMismatch, DirectionMismatch,
        MissingOppositePipe, DuplicatePipe, SuspiciousStacking, CrossingClearance,
        UnresolvedConnection, SubmergedPipe, SiltedPipe, BlockedPipe, UnableToDip,
        UnknownDirection, NoCadPoint, StaleSurvey, MalformedNote, InvalidGeometry, UnconfirmedReference, StructureSizeMissing
    }

    public sealed class QcFinding
    {
        public QcCode Code { get; set; }
        public Severity Severity { get; set; }
        public string StructureId { get; set; }
        public string PipeId { get; set; }
        public string ConnectionId { get; set; }
        public string Message { get; set; }

        /// <summary>True only when drafting would produce invalid geometry. Everything
        /// else is a warning the drafter reviews; it never blocks.</summary>
        public bool BlocksDrafting { get; set; }

        /// <summary>True when the finding needs the field crew, not the drafter.</summary>
        public bool NeedsFieldRevisit { get; set; }
    }

    public sealed class UtilityReviewSummary
    {
        public int Structures { get; set; }
        public int Pipes { get; set; }
        public int Warnings { get; set; }
        public int UnresolvedConnections { get; set; }
        public int MissingOppositeDips { get; set; }
        public int StaleStructures { get; set; }

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture,
                "{0} structures, {1} pipes, {2} warnings, {3} unresolved connections, {4} missing opposite dips{5}",
                Structures, Pipes, Warnings, UnresolvedConnections, MissingOppositeDips,
                StaleStructures > 0 ? ", " + StaleStructures + " stale" : string.Empty);
        }
    }

    /// <summary>
    /// Configurable QC over the whole dip project. Findings flag, they never
    /// correct: a suspicious invert stays exactly as observed, with a warning.
    /// </summary>
    public static class UtilityQc
    {
        public static IList<QcFinding> Evaluate(UtilityProject project, UtilitySettings settings)
        {
            settings = settings ?? new UtilitySettings();
            var findings = new List<QcFinding>();
            if (project == null) return findings;

            foreach (var structure in project.Structures)
                CheckStructure(project, structure, settings, findings);

            foreach (var connection in project.Connections)
                CheckConnection(project, connection, settings, findings);

            CheckCrossings(project, settings, findings);

            // Checks the drafter switched off. A check that stops invalid geometry being
            // drawn is never removed.
            if (settings.DisabledChecks != null && settings.DisabledChecks.Count > 0)
                findings.RemoveAll(f => !f.BlocksDrafting && settings.DisabledChecks.Contains(f.Code));
            return findings;
        }

        private static void CheckStructure(UtilityProject project, StructureRecord s,
                                           UtilitySettings settings, List<QcFinding> findings)
        {
            if (s.Cad == null)
            {
                Add(findings, QcCode.NoCadPoint, Severity.Error, s, null, null,
                    s.Label + " has no surveyed CAD point; no elevations can be calculated.", true, false);
                return;
            }

            var standard = settings.Standard(s.System);
            var bottom = DipElevations.Bottom(s);
            var water = DipElevations.Water(s);
            DimensionSource widthSource;
            var insideWidth = StructureDimensions.InsideWidth(s, settings, out widthSource);

            // A round structure should carry its diameter. A gentle note, not a warning.
            if (StructureDimensions.ShapeOf(s, settings) == StructureShape.Round && !insideWidth.HasValue)
                Add(findings, QcCode.StructureSizeMissing, Severity.Info, s, null, null,
                    s.Label + ": diameter not entered.", false, false);

            foreach (var problem in s.Field.NoteProblems ?? new List<string>())
                Add(findings, QcCode.MalformedNote, Severity.Warning, s, null, null,
                    s.Label + " field note unreadable -- " + problem, false, true);

            foreach (var pipe in s.Field.Pipes)
            {
                var name = s.Label + " " + ConnectionFinder.Describe(pipe);

                // Pipes nobody has connected yet are office work, not a field question.
                if (pipe.Direction.IsKnown && project.ConnectionFor(s.Id, pipe.Id) == null)
                    Add(findings, QcCode.UnresolvedConnection, Severity.Warning, s, pipe, null,
                        name + ": not connected yet -- find its connection.", false, false);

                if (pipe.ReferenceUnconfirmed && pipe.MeasuredDip.HasValue)
                    Add(findings, QcCode.UnconfirmedReference, Severity.Warning, s, pipe, null,
                        name + ": the dip does not say what it was measured to (assumed " +
                        DipElevations.Describe(settings.AssumedPipeReference) +
                        ", not confirmed). No slope is calculated until the reference is confirmed.", false, false);

                if (!pipe.Direction.IsKnown)
                    Add(findings, QcCode.UnknownDirection, Severity.Warning, s, pipe, null,
                        name + ": direction unknown -- cannot be connected.", false, true);

                if (pipe.HasCondition("UNABLE TO DIP") || (!pipe.MeasuredDip.HasValue))
                    Add(findings, QcCode.UnableToDip, Severity.Warning, s, pipe, null,
                        name + ": no measure down (MD) recorded.", false, true);
                if (pipe.HasCondition("SUBMERGED"))
                    Add(findings, QcCode.SubmergedPipe, Severity.Warning, s, pipe, null, name + ": noted submerged.", false, false);
                if (pipe.HasCondition("SILTED"))
                    Add(findings, QcCode.SiltedPipe, Severity.Warning, s, pipe, null, name + ": noted silted.", false, false);
                if (pipe.HasCondition("BLOCKED"))
                    Add(findings, QcCode.BlockedPipe, Severity.Warning, s, pipe, null, name + ": noted blocked.", false, true);

                double? invert, crown;
                DipElevations.Envelope(s, pipe, out invert, out crown);
                var measured = DipElevations.Pipe(s, pipe);

                if (measured != null && measured.Value > s.Cad.Rim)
                    Add(findings, QcCode.InvertAboveRim, Severity.Error, s, pipe, null,
                        name + ": measured elevation is above the rim.", false, true);

                if (invert.HasValue && bottom != null && invert.Value < bottom.Value - 0.01)
                    Add(findings, QcCode.InvertBelowBottom, Severity.Warning, s, pipe, null,
                        string.Format(CultureInfo.InvariantCulture,
                            "{0}: invert {1:0.00} is below the structure bottom {2:0.00}.", name, invert.Value, bottom.Value),
                        false, true);

                if (crown.HasValue && standard.CoverCheckEnabled && s.Cad.Rim - crown.Value < standard.MinCoverFt)
                    Add(findings, QcCode.InsufficientCover, Severity.Warning, s, pipe, null,
                        string.Format(CultureInfo.InvariantCulture,
                            "{0}: {1:0.00}' of cover at the structure, under the {2:0.00}' QC warning threshold (not a design requirement).",
                            name, s.Cad.Rim - crown.Value, standard.MinCoverFt), false, false);

                if (crown.HasValue && water != null && water.Value > crown.Value + 0.01 && !pipe.HasCondition("SUBMERGED"))
                    Add(findings, QcCode.SubmergedPipe, Severity.Warning, s, pipe, null,
                        string.Format(CultureInfo.InvariantCulture,
                            "{0}: water at {1:0.00} is above the pipe crown {2:0.00} (submerged).",
                            name, water.Value, crown.Value), false, false);

                // Only when the inside size is actually known; never guessed from the type.
                if (insideWidth.HasValue && pipe.WidthIn.HasValue && pipe.WidthIn.Value > insideWidth.Value)
                    Add(findings, QcCode.PipeTooLargeForStructure, Severity.Warning, s, pipe, null,
                        string.Format(CultureInfo.InvariantCulture,
                            "{0}: {1}\" pipe is wider than the {2}\" inside width ({3}).",
                            name, pipe.WidthIn.Value, insideWidth.Value, StructureDimensions.Describe(widthSource)), false, true);
            }

            // Two pipes in the same direction at the same structure.
            var pipes = s.Field.Pipes.Where(p => p.Direction.IsKnown).ToList();
            for (var i = 0; i < pipes.Count; i++)
            for (var j = i + 1; j < pipes.Count; j++)
            {
                var a = pipes[i];
                var b = pipes[j];
                if (ConnectionFinder.AngleBetween(a.Direction.AzimuthDegrees.Value, b.Direction.AzimuthDegrees.Value) > 10.0)
                    continue;

                var sameSize = a.WidthIn == b.WidthIn && a.HeightIn == b.HeightIn;
                var sameDip = a.MeasuredDip.HasValue && b.MeasuredDip.HasValue &&
                              Math.Abs(a.MeasuredDip.Value - b.MeasuredDip.Value) < 0.05;
                if (sameSize && sameDip)
                {
                    Add(findings, QcCode.DuplicatePipe, Severity.Warning, s, b, null,
                        s.Label + ": two " + ConnectionFinder.Describe(a) + " pipes at the same dip -- possible duplicate entry.",
                        false, true);
                    continue;
                }

                double? ia, ca, ib, cb;
                DipElevations.Envelope(s, a, out ia, out ca);
                DipElevations.Envelope(s, b, out ib, out cb);
                if (ia.HasValue && ca.HasValue && ib.HasValue && cb.HasValue &&
                    ia.Value < cb.Value && ib.Value < ca.Value)
                    Add(findings, QcCode.SuspiciousStacking, Severity.Warning, s, b, null,
                        s.Label + ": " + ConnectionFinder.Describe(a) + " and " + ConnectionFinder.Describe(b) +
                        " run the same way and overlap vertically.", false, true);
            }
        }

        private static void CheckConnection(UtilityProject project, PipeConnection c,
                                            UtilitySettings settings, List<QcFinding> findings)
        {
            var from = project.Structure(c.FromStructureId);
            var to = project.Structure(c.ToStructureId);
            var fromPipe = project.Pipe(c.FromStructureId, c.FromPipeId);
            var toPipe = c.ToPipeId != null ? project.Pipe(c.ToStructureId, c.ToPipeId) : null;
            if (from == null || fromPipe == null) return;
            var name = from.Label + " " + ConnectionFinder.Describe(fromPipe);
            if (c.Status == ConnectionStatus.OutsideSurveyLimits) return;   // settled: nothing to check or chase

            if (to == null || c.Status == ConnectionStatus.Unresolved || c.Status == ConnectionStatus.LeftUnresolved)
            {
                // Left unresolved on purpose means the office could not find where it
                // goes -- the crew has to trace it.
                var leftOnPurpose = c.Status == ConnectionStatus.LeftUnresolved;
                Add(findings, QcCode.UnresolvedConnection, Severity.Warning, from, fromPipe, c,
                    name + (leftOnPurpose
                        ? ": where it goes could not be determined from the survey -- trace the run."
                        : ": connection unresolved."), false, leftOnPurpose);
                return;
            }

            if (from.Id == to.Id || SlopeCalculator.Distance(from, to) < 0.01)
            {
                Add(findings, QcCode.InvalidGeometry, Severity.Error, from, fromPipe, c,
                    name + ": both ends are at the same location.", true, false);
                return;
            }

            if (toPipe == null)
            {
                Add(findings, QcCode.MissingOppositePipe, Severity.Warning, from, fromPipe, c,
                    name + ": runs to " + to.Label + ", which has no corresponding field dip observed.", false, true);
            }
            else
            {
                if (fromPipe.WidthIn != toPipe.WidthIn || fromPipe.HeightIn != toPipe.HeightIn)
                    Add(findings, QcCode.SizeMismatch, Severity.Warning, from, fromPipe, c,
                        string.Format("{0}: size {1} here but {2} at {3}.", name,
                            UtilityLabelFormatter.FormatSize(fromPipe), UtilityLabelFormatter.FormatSize(toPipe), to.Label),
                        false, true);

                if (fromPipe.Material != null && toPipe.Material != null &&
                    !string.Equals(fromPipe.Material, toPipe.Material, StringComparison.OrdinalIgnoreCase))
                    Add(findings, QcCode.MaterialMismatch, Severity.Warning, from, fromPipe, c,
                        name + ": material " + fromPipe.Material + " here but " + toPipe.Material + " at " + to.Label + ".",
                        false, true);

                if (from.Cad != null && to.Cad != null && toPipe.Direction.IsKnown && fromPipe.Direction.IsKnown)
                {
                    var bearing = Drafting.SurveyDirection.AzimuthFromVector(to.Cad.Easting - from.Cad.Easting,
                                                                             to.Cad.Northing - from.Cad.Northing);
                    var offHere = ConnectionFinder.AngleBetween(fromPipe.Direction.AzimuthDegrees.Value, bearing);
                    var offThere = ConnectionFinder.AngleBetween(toPipe.Direction.AzimuthDegrees.Value, (bearing + 180) % 360);
                    if (offHere > settings.SearchConeDegrees || offThere > settings.SearchConeDegrees)
                        Add(findings, QcCode.DirectionMismatch, Severity.Warning, from, fromPipe, c,
                            string.Format(CultureInfo.InvariantCulture,
                                "{0}: observed directions are {1:0}° and {2:0}° off the line between the structures.",
                                name, offHere, offThere), false, true);
                }
            }

            var slope = SlopeCalculator.Compute(project, c);
            if (slope.Basis == SlopeBasis.CalculatedFromBothObservations)
            {
                var standard = settings.Standard(from.System);
                if (standard.SlopeCheckEnabled &&
                    (slope.SlopePercent < standard.MinSlopePercent || slope.SlopePercent > standard.MaxSlopePercent))
                    Add(findings, QcCode.SlopeOutOfRange, Severity.Warning, from, fromPipe, c,
                        string.Format(CultureInfo.InvariantCulture,
                            "{0}: calculated slope {1:0.00}% is outside the {2} QC warning range {3:0.##}%-{4:0.##}% (not a design requirement).",
                            name, slope.SlopePercent.Value, from.System, standard.MinSlopePercent, standard.MaxSlopePercent),
                        false, false);

                // The field roles say which way the water should go.
                var fallsTowardTo = slope.SignedPercentFromTo < 0;
                var saysOut = fromPipe.Role == FlowRole.Out || (toPipe != null && toPipe.Role == FlowRole.In);
                var saysIn = fromPipe.Role == FlowRole.In || (toPipe != null && toPipe.Role == FlowRole.Out);
                if ((saysOut && !fallsTowardTo) || (saysIn && fallsTowardTo))
                    Add(findings, QcCode.ReverseFlow, Severity.Warning, from, fromPipe, c,
                        name + ": the noted IN/OUT disagrees with the calculated fall -- apparent reverse flow.",
                        false, true);
            }
        }

        /// <summary>Storm and sanitary runs that cross in plan with too little vertical
        /// separation. Only runs whose both ends were dipped are compared.</summary>
        private static void CheckCrossings(UtilityProject project, UtilitySettings settings, List<QcFinding> findings)
        {
            var runs = project.Connections.Where(c => c.IsAccepted && c.ToStructureId != null).ToList();
            for (var i = 0; i < runs.Count; i++)
            for (var j = i + 1; j < runs.Count; j++)
            {
                var a = runs[i];
                var b = runs[j];
                var a0 = project.Structure(a.FromStructureId);
                var a1 = project.Structure(a.ToStructureId);
                var b0 = project.Structure(b.FromStructureId);
                var b1 = project.Structure(b.ToStructureId);
                if (a0 == null || a1 == null || b0 == null || b1 == null) continue;
                if (a0.Cad == null || a1.Cad == null || b0.Cad == null || b1.Cad == null) continue;

                var systems = new[] { a0.System, b0.System };
                if (!(systems.Contains(UtilitySystem.Storm) && systems.Contains(UtilitySystem.Sanitary))) continue;

                double ta, tb;
                if (!SegmentsCross(a0, a1, b0, b1, out ta, out tb)) continue;

                var ea = InvertAlong(project, a, ta);
                var eb = InvertAlong(project, b, tb);
                if (!ea.HasValue || !eb.HasValue) continue;

                var pa = project.Pipe(a.FromStructureId, a.FromPipeId);
                var pb = project.Pipe(b.FromStructureId, b.FromPipeId);
                var riseA = pa != null && pa.HeightIn.HasValue ? pa.HeightIn.Value / 12.0 : 0.0;
                var riseB = pb != null && pb.HeightIn.HasValue ? pb.HeightIn.Value / 12.0 : 0.0;

                var clearance = ea.Value > eb.Value
                    ? ea.Value - (eb.Value + riseB)
                    : eb.Value - (ea.Value + riseA);

                if (clearance < settings.CrossingClearanceFt)
                    Add(findings, QcCode.CrossingClearance, Severity.Warning, a0, pa, a,
                        string.Format(CultureInfo.InvariantCulture,
                            "{0}-{1} crosses {2}-{3} with {4:0.00}' vertical clearance (minimum {5:0.00}').",
                            a0.Label, a1.Label, b0.Label, b1.Label, clearance, settings.CrossingClearanceFt),
                        false, false);
            }
        }

        private static double? InvertAlong(UtilityProject project, PipeConnection c, double t)
        {
            var slope = SlopeCalculator.Compute(project, c);
            if (slope.Basis != SlopeBasis.CalculatedFromBothObservations) return null;
            if (slope.FromElevation.Reference != MeasurementReference.Invert) return null;
            return slope.FromElevation.Value + (slope.ToElevation.Value - slope.FromElevation.Value) * t;
        }

        private static bool SegmentsCross(StructureRecord a0, StructureRecord a1, StructureRecord b0,
                                          StructureRecord b1, out double ta, out double tb)
        {
            ta = tb = 0;
            double x1 = a0.Cad.Easting, y1 = a0.Cad.Northing, x2 = a1.Cad.Easting, y2 = a1.Cad.Northing;
            double x3 = b0.Cad.Easting, y3 = b0.Cad.Northing, x4 = b1.Cad.Easting, y4 = b1.Cad.Northing;
            var d = (x2 - x1) * (y4 - y3) - (y2 - y1) * (x4 - x3);
            if (Math.Abs(d) < 1e-12) return false;
            ta = ((x3 - x1) * (y4 - y3) - (y3 - y1) * (x4 - x3)) / d;
            tb = ((x3 - x1) * (y2 - y1) - (y3 - y1) * (x2 - x1)) / d;
            return ta > 0.001 && ta < 0.999 && tb > 0.001 && tb < 0.999;
        }

        private static void Add(List<QcFinding> findings, QcCode code, Severity severity, StructureRecord s,
                                PipeObservation pipe, PipeConnection c, string message, bool blocks, bool revisit)
        {
            findings.Add(new QcFinding
            {
                Code = code,
                Severity = severity,
                StructureId = s != null ? s.Id : null,
                PipeId = pipe != null ? pipe.Id : null,
                ConnectionId = c != null ? c.Id : null,
                Message = message,
                BlocksDrafting = blocks,
                NeedsFieldRevisit = revisit
            });
        }

        // ---------------------------------------------------------------- review

        public static UtilityReviewSummary Summarize(UtilityProject project, IList<QcFinding> findings,
                                                     int staleStructures)
        {
            findings = findings ?? new List<QcFinding>();
            return new UtilityReviewSummary
            {
                Structures = project.Structures.Count,
                Pipes = project.Structures.Sum(s => s.Field.Pipes.Count),
                Warnings = findings.Count(f => f.Severity != Severity.Info),
                UnresolvedConnections = project.Structures.Sum(s => s.Field.Pipes.Count(p =>
                {
                    var c = project.ConnectionFor(s.Id, p.Id);
                    return c == null || (!c.IsAccepted && c.Status != ConnectionStatus.OutsideSurveyLimits);
                })),
                MissingOppositeDips = findings.Count(f => f.Code == QcCode.MissingOppositePipe),
                StaleStructures = staleStructures
            };
        }

        /// <summary>The crew's list: every finding the field has to answer, plus the
        /// drafter's own notes. Plain text, grouped by structure.</summary>
        public static string FieldRevisitText(UtilityProject project, IList<QcFinding> findings)
        {
            var sb = new StringBuilder();
            var grouped = (findings ?? new List<QcFinding>())
                .Where(f => f.NeedsFieldRevisit)
                .GroupBy(f => f.StructureId);

            foreach (var group in grouped)
            {
                var structure = project.Structure(group.Key);
                sb.AppendLine(structure != null ? structure.Label : "(unknown structure)");
                foreach (var f in group) sb.AppendLine("  " + Tidy(f.Message, structure));
                sb.AppendLine();
            }

            foreach (var item in project.ManualRevisit)
            {
                sb.AppendLine(item.StructureLabel);
                sb.AppendLine("  " + item.Text);
                sb.AppendLine();
            }

            return sb.ToString().TrimEnd();
        }

        private static string Tidy(string message, StructureRecord structure)
        {
            if (structure != null && message.StartsWith(structure.Label + " ", StringComparison.Ordinal))
                message = message.Substring(structure.Label.Length + 1);
            return message;
        }

        // ------------------------------------------------------------ staleness

        /// <summary>Structures whose live CAD point no longer matches the snapshot the
        /// calculations were made from. Nothing is changed -- the caller offers a
        /// rebuild.</summary>
        public static IList<StructureRecord> StaleStructures(UtilityProject project,
                                                             IDictionary<string, CadStructureSnapshot> livePoints)
        {
            var stale = new List<StructureRecord>();
            foreach (var s in project.Structures)
            {
                if (s.Cad == null) continue;
                CadStructureSnapshot live;
                if (!livePoints.TryGetValue(s.Cad.PointNumber ?? string.Empty, out live))
                {
                    stale.Add(s);
                    continue;
                }
                if (Math.Abs(live.Rim - s.Cad.Rim) > 0.005 ||
                    Math.Abs(live.Northing - s.Cad.Northing) > 0.01 ||
                    Math.Abs(live.Easting - s.Cad.Easting) > 0.01)
                    stale.Add(s);
            }
            return stale;
        }
    }
}
