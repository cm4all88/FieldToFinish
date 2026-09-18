using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FieldCodes.Settings;

namespace FieldCodes.Utilities
{
    // =====================================================================
    // The production workflow layer.
    //
    // Everything here reads the existing model and reports what state the work
    // is in. Nothing here observes, calculates a dip, finds a connection or
    // drafts: it answers the questions a drafter asks while walking a network --
    // where am I, what is connected, what is drawn, what should I do next --
    // from data the engine below already produced.
    //
    // It lives in FieldCodes rather than FieldCodes.Cad so it is testable
    // without AutoCAD. The window is a consumer of this, not the owner of it.
    // =====================================================================

    /// <summary>Where a pipe's far end stands.</summary>
    public enum PipeConnectionState
    {
        /// <summary>No connection record at all: nobody has looked yet.</summary>
        NotExamined,
        /// <summary>The search suggested one; nobody has confirmed it.</summary>
        Suggested,
        /// <summary>The drafter confirmed a searched candidate.</summary>
        Confirmed,
        /// <summary>The drafter picked the far structure themselves.</summary>
        DrafterSelected,
        /// <summary>Examined and deliberately left open. A valid survey state.</summary>
        LeftUnresolved,
        /// <summary>Runs beyond the survey. Settled, and not a field question.</summary>
        OutsideSurveyLimits
    }

    /// <summary>What exists in the drawing for this pipe.</summary>
    public enum PipeDraftingState
    {
        /// <summary>No accepted connection, so there is nothing to draw yet.</summary>
        NotReady,
        /// <summary>Accepted and drawable; not drawn.</summary>
        ReadyToDraw,
        /// <summary>Drawn, and the drawing still matches the field data.</summary>
        Drawn,
        /// <summary>Drawn, but an observation or the connection changed afterwards.
        /// The drawing in the file is no longer what the data says.</summary>
        DrawingStale,
        /// <summary>Accepted, but QC says drafting it would produce invalid geometry.</summary>
        BlockedByQc
    }

    /// <summary>What exists for this pipe's label.</summary>
    public enum PipeLabelState
    {
        /// <summary>The pipe is not drawn, so no label is expected.</summary>
        NotApplicable,
        /// <summary>Drawn with no label recorded.</summary>
        NoLabel,
        /// <summary>Generated text, at the position FTF computed.</summary>
        LabelPlaced,
        /// <summary>Generated text, at a position the drafter chose.</summary>
        LabelMoved,
        /// <summary>Text the drafter typed, replacing the generated text.</summary>
        LabelTextOverridden,
        /// <summary>The label no longer matches what the text would generate now.</summary>
        LabelNeedsUpdate
    }

    /// <summary>How far along a structure is. Derived from observations,
    /// connections, drafting and QC -- never from "every box has a value".</summary>
    public enum StructureStatus
    {
        /// <summary>No pipes observed yet.</summary>
        NoObservations,
        /// <summary>Pipes observed, at least one with nowhere to go yet.</summary>
        NeedsConnections,
        /// <summary>Every pipe settled, at least one accepted pipe not drawn.</summary>
        ReadyToDraw,
        /// <summary>Some accepted pipes drawn, some not.</summary>
        DrawingIncomplete,
        /// <summary>Drawn, but at least one drawing no longer matches the data.</summary>
        DrawingStale,
        /// <summary>A QC finding wants the drafter.</summary>
        NeedsReview,
        /// <summary>A QC finding wants the field crew.</summary>
        FieldRevisit,
        /// <summary>Every pipe settled and every drawable pipe drawn and current.</summary>
        Complete
    }

    /// <summary>What to do with CAD geometry already sitting between two structures.
    /// The same four answers FTFDIPDRAW has always asked for, as a value the window
    /// can supply so the question does not have to interrupt at the command line.</summary>
    public enum ExistingPipeDecision
    {
        /// <summary>Leave what is there; draw nothing. The safe default.</summary>
        Keep,
        /// <summary>Take the hand-drawn geometry under management and label it.
        /// Its geometry is not changed.</summary>
        AdoptOrUpdate,
        /// <summary>Erase what is there and draw the FTF pipe.</summary>
        Replace,
        /// <summary>Draw the FTF pipe alongside whatever is there.</summary>
        CreateNew
    }

    /// <summary>Everything the window needs to show for one observed pipe, in one
    /// object, so a row can be drawn without asking six different services.</summary>
    public sealed class PipeWorkflowState
    {
        public StructureRecord Structure { get; set; }
        public PipeObservation Pipe { get; set; }
        public PipeConnection Connection { get; set; }

        public PipeConnectionState ConnectionState { get; set; }
        public PipeDraftingState DraftingState { get; set; }
        public PipeLabelState LabelState { get; set; }

        /// <summary>The structure at the far end, when one is recorded.</summary>
        public StructureRecord ConnectedTo { get; set; }

        /// <summary>The far structure's matching observation, when the far end was
        /// dipped. Null means the far end is not observed -- it is never inferred.</summary>
        public PipeObservation OppositePipe { get; set; }

        /// <summary>True only when both ends carry a real field observation of this
        /// pipe. A drafter-selected connection with nothing observed at the far end
        /// is false, however confident the drafter is.</summary>
        public bool BothEndsObserved { get; set; }

        public CalculatedElevation Elevation { get; set; }
        public SlopeResult Slope { get; set; }

        /// <summary>QC findings that name this pipe or its connection.</summary>
        public IList<QcFinding> Findings { get; set; }

        /// <summary>Why the connection is what it is -- the candidate reasons the
        /// search recorded, or the drafter's note.</summary>
        public IList<string> ConnectionReasons
        {
            get { return Connection != null ? Connection.Basis : new List<string>(); }
        }

        public Confidence ConnectionConfidence
        {
            get { return Connection != null ? Connection.Confidence : Confidence.None; }
        }

        public PipeWorkflowState() { Findings = new List<QcFinding>(); }

        /// <summary>One line naming the next useful action, or null when this pipe
        /// needs nothing. This is the answer to "what do I do next?".</summary>
        public string NextAction
        {
            get
            {
                if (Pipe != null && Pipe.ReferenceUnconfirmed && Pipe.MeasuredDip.HasValue)
                    return "Confirm what the dip was measured to";
                switch (ConnectionState)
                {
                    case PipeConnectionState.NotExamined:
                        return "Connect this pipe, or leave it unresolved";
                    case PipeConnectionState.Suggested:
                        return "Confirm the suggested connection, pick another structure, or leave it unresolved";
                    case PipeConnectionState.LeftUnresolved:
                    case PipeConnectionState.OutsideSurveyLimits:
                        return null;
                }
                switch (DraftingState)
                {
                    case PipeDraftingState.BlockedByQc: return "Review: drafting this would produce invalid geometry";
                    case PipeDraftingState.ReadyToDraw: return "Draw pipe + label";
                    case PipeDraftingState.DrawingStale:
                        return Connection != null && Connection.HasSupersededDrafting && !Connection.Drafted
                            ? "Redraw -- this pipe now runs somewhere else and the old one is still drawn"
                            : "Update the drawing -- the data changed after it was drawn";
                }
                if (LabelState == PipeLabelState.NoLabel) return "Place the pipe label";
                if (LabelState == PipeLabelState.LabelNeedsUpdate) return "Update the label text";
                return null;
            }
        }
    }

    /// <summary>
    /// Reads the project and reports the state of the work. Pure: it changes
    /// nothing, so the window can call it as often as it likes.
    /// </summary>
    public static class DipWorkflow
    {
        /// <summary>What the drawing was made from. Any change to the pipe as
        /// observed, to either structure's surveyed position, or to which structure
        /// the pipe runs to, changes this string -- and the drawing becomes stale.
        /// Label text is deliberately not in it: retyping a label does not make the
        /// pipe geometry wrong.</summary>
        public static string Fingerprint(UtilityProject project, PipeConnection connection)
        {
            if (project == null || connection == null) return null;
            var from = project.Structure(connection.FromStructureId);
            var to = connection.ToStructureId != null ? project.Structure(connection.ToStructureId) : null;
            var pipe = project.Pipe(connection.FromStructureId, connection.FromPipeId);
            var opposite = to != null && connection.ToPipeId != null
                ? project.Pipe(to.Id, connection.ToPipeId) : null;

            var parts = new List<string>
            {
                connection.ToStructureId ?? "-",
                connection.ToPipeId ?? "-",
                connection.Status.ToString(),
                Geom(from), Geom(to), Obs(pipe), Obs(opposite)
            };
            return string.Join("|", parts.ToArray());
        }

        private static string Geom(StructureRecord s)
        {
            if (s == null || s.Cad == null) return "-";
            return string.Format(CultureInfo.InvariantCulture, "{0:0.000},{1:0.000},{2:0.000}",
                                 s.Cad.Easting, s.Cad.Northing, s.Cad.Rim);
        }

        private static string Obs(PipeObservation p)
        {
            if (p == null) return "-";
            return string.Format(CultureInfo.InvariantCulture, "{0},{1},{2},{3},{4},{5}",
                p.WidthIn, p.HeightIn, p.Material, p.MeasuredDip, p.Reference,
                p.Direction != null ? p.Direction.Text : null);
        }

        public static PipeConnectionState StateOf(PipeConnection connection)
        {
            if (connection == null) return PipeConnectionState.NotExamined;
            switch (connection.Status)
            {
                case ConnectionStatus.Probable: return PipeConnectionState.Suggested;
                case ConnectionStatus.Confirmed: return PipeConnectionState.Confirmed;
                case ConnectionStatus.ManualOverride: return PipeConnectionState.DrafterSelected;
                case ConnectionStatus.LeftUnresolved: return PipeConnectionState.LeftUnresolved;
                case ConnectionStatus.OutsideSurveyLimits: return PipeConnectionState.OutsideSurveyLimits;
                default: return PipeConnectionState.NotExamined;
            }
        }

        /// <summary>The state of one observed pipe, from every part of the model.</summary>
        public static PipeWorkflowState Describe(UtilityProject project, StructureRecord structure,
                                                 PipeObservation pipe, IList<QcFinding> findings)
        {
            var state = new PipeWorkflowState { Structure = structure, Pipe = pipe };
            if (project == null || structure == null || pipe == null) return state;

            state.Connection = project.ConnectionFor(structure.Id, pipe.Id);
            state.ConnectionState = StateOf(state.Connection);
            state.Elevation = DipElevations.Pipe(structure, pipe);

            if (findings != null)
                state.Findings = findings.Where(f =>
                    (state.Connection != null && f.ConnectionId == state.Connection.Id) ||
                    (f.StructureId == structure.Id && f.PipeId == pipe.Id)).ToList();

            var c = state.Connection;
            if (c != null && c.ToStructureId != null)
            {
                state.ConnectedTo = project.Structure(c.ToStructureId);

                // The far end counts as observed only when a real observation is
                // recorded there. A manual pick does not create one.
                if (c.ToPipeId != null && state.ConnectedTo != null)
                    state.OppositePipe = project.Pipe(state.ConnectedTo.Id, c.ToPipeId);

                state.BothEndsObserved = state.OppositePipe != null &&
                                         pipe.MeasuredDip.HasValue &&
                                         state.OppositePipe.MeasuredDip.HasValue;

                if (c.IsAccepted) state.Slope = SlopeCalculator.Compute(project, c);
            }

            state.DraftingState = DraftingStateOf(project, c, state.Findings);
            state.LabelState = LabelStateOf(project, c, state.DraftingState);
            return state;
        }

        public static PipeDraftingState DraftingStateOf(UtilityProject project, PipeConnection connection,
                                                        IList<QcFinding> findings)
        {
            if (connection == null || !connection.IsAccepted || connection.ToStructureId == null)
                return PipeDraftingState.NotReady;

            if (findings != null && findings.Any(f => f.BlocksDrafting))
                return PipeDraftingState.BlockedByQc;

            // Something was drafted for the connection this one replaced and is still
            // in the drawing. Whatever else is true, the file does not match the data.
            if (connection.HasSupersededDrafting && !connection.Drafted)
                return PipeDraftingState.DrawingStale;

            if (!connection.Drafted) return PipeDraftingState.ReadyToDraw;

            // Drafted before fingerprints were recorded: the drawing may or may not
            // still match, and saying "current" would be a guess. Treat the known
            // state -- drawn -- and let a redraw record a fingerprint.
            if (connection.DraftedFingerprint == null) return PipeDraftingState.Drawn;

            return connection.DraftedFingerprint == Fingerprint(project, connection)
                ? PipeDraftingState.Drawn
                : PipeDraftingState.DrawingStale;
        }

        public static PipeLabelState LabelStateOf(UtilityProject project, PipeConnection connection,
                                                  PipeDraftingState drafting)
        {
            if (connection == null || !connection.Drafted) return PipeLabelState.NotApplicable;
            if (connection.LabelTextIsOverridden) return PipeLabelState.LabelTextOverridden;
            if (drafting == PipeDraftingState.DrawingStale) return PipeLabelState.LabelNeedsUpdate;
            if (connection.LabelWasPlacedByDrafter) return PipeLabelState.LabelMoved;
            return PipeLabelState.LabelPlaced;
        }

        /// <summary>Every pipe at a structure, in observation order.</summary>
        public static IList<PipeWorkflowState> DescribeStructure(UtilityProject project, StructureRecord structure,
                                                                 IList<QcFinding> findings)
        {
            var list = new List<PipeWorkflowState>();
            if (project == null || structure == null) return list;
            foreach (var pipe in structure.Field.Pipes)
                list.Add(Describe(project, structure, pipe, findings));
            return list;
        }

        /// <summary>
        /// How far along a structure is. A structure is Complete only when every
        /// observed pipe has been settled one way or another and everything drawable
        /// is drawn and current -- never merely because the fields are filled in.
        /// </summary>
        public static StructureStatus StatusOf(UtilityProject project, StructureRecord structure,
                                               IList<QcFinding> findings)
        {
            if (project == null || structure == null) return StructureStatus.NoObservations;
            var pipes = DescribeStructure(project, structure, findings);
            if (pipes.Count == 0) return StructureStatus.NoObservations;

            var mine = findings == null ? new List<QcFinding>()
                : findings.Where(f => f.StructureId == structure.Id ||
                                      pipes.Any(p => p.Connection != null && f.ConnectionId == p.Connection.Id)).ToList();

            if (mine.Any(f => f.NeedsFieldRevisit)) return StructureStatus.FieldRevisit;

            var unsettled = pipes.Count(p => p.ConnectionState == PipeConnectionState.NotExamined ||
                                             p.ConnectionState == PipeConnectionState.Suggested);
            if (unsettled > 0) return StructureStatus.NeedsConnections;

            if (mine.Any(f => f.Severity == Severity.Error)) return StructureStatus.NeedsReview;

            var drawable = pipes.Where(p => p.DraftingState != PipeDraftingState.NotReady).ToList();
            if (drawable.Any(p => p.DraftingState == PipeDraftingState.DrawingStale))
                return StructureStatus.DrawingStale;
            if (drawable.Any(p => p.DraftingState == PipeDraftingState.BlockedByQc))
                return StructureStatus.NeedsReview;

            var ready = drawable.Count(p => p.DraftingState == PipeDraftingState.ReadyToDraw);
            var drawn = drawable.Count(p => p.DraftingState == PipeDraftingState.Drawn);
            if (ready > 0) return drawn > 0 ? StructureStatus.DrawingIncomplete : StructureStatus.ReadyToDraw;

            if (mine.Any(f => f.Severity == Severity.Warning)) return StructureStatus.NeedsReview;
            return StructureStatus.Complete;
        }

        public static string Describe(StructureStatus status)
        {
            switch (status)
            {
                case StructureStatus.NoObservations: return "No observations";
                case StructureStatus.NeedsConnections: return "Needs connections";
                case StructureStatus.ReadyToDraw: return "Ready to draw";
                case StructureStatus.DrawingIncomplete: return "Drawing incomplete";
                case StructureStatus.DrawingStale: return "Drawing out of date";
                case StructureStatus.NeedsReview: return "Needs review";
                case StructureStatus.FieldRevisit: return "Field revisit";
                default: return "Complete";
            }
        }

        public static string Describe(PipeConnectionState state)
        {
            switch (state)
            {
                case PipeConnectionState.NotExamined: return "Not connected";
                case PipeConnectionState.Suggested: return "Suggested -- not confirmed";
                case PipeConnectionState.Confirmed: return "Confirmed";
                case PipeConnectionState.DrafterSelected: return "Drafter selected";
                case PipeConnectionState.LeftUnresolved: return "Left unresolved";
                default: return "Outside survey limits";
            }
        }

        public static string Describe(PipeDraftingState state)
        {
            switch (state)
            {
                case PipeDraftingState.NotReady: return "Not ready";
                case PipeDraftingState.ReadyToDraw: return "Ready to draw";
                case PipeDraftingState.Drawn: return "Drawn";
                case PipeDraftingState.DrawingStale: return "Drawing out of date";
                default: return "Blocked -- review";
            }
        }

        public static string Describe(PipeLabelState state)
        {
            switch (state)
            {
                case PipeLabelState.NotApplicable: return "--";
                case PipeLabelState.NoLabel: return "No label";
                case PipeLabelState.LabelPlaced: return "Label placed";
                case PipeLabelState.LabelMoved: return "Label moved";
                case PipeLabelState.LabelTextOverridden: return "Label text edited";
                default: return "Label out of date";
            }
        }

        /// <summary>
        /// Records that a connection has just been drafted: what it was drawn from,
        /// and when. Called by the drafting path so staleness can be detected later.
        /// </summary>
        public static void RecordDrafted(UtilityProject project, PipeConnection connection)
        {
            if (project == null || connection == null) return;
            connection.Drafted = true;
            connection.DraftedFingerprint = Fingerprint(project, connection);
            connection.DraftedUtc = DateTime.UtcNow;
            // The superseded drafting is erased as part of drawing, so the debt is paid.
            connection.SupersededDraftingId = null;
        }

        /// <summary>The structures a pipe could be drawn for right now.</summary>
        public static IList<PipeConnection> ReadyToDraw(UtilityProject project, IList<QcFinding> findings)
        {
            var ready = new List<PipeConnection>();
            if (project == null) return ready;
            foreach (var c in project.Connections)
            {
                var mine = findings == null ? null
                    : findings.Where(f => f.ConnectionId == c.Id).ToList();
                var state = DraftingStateOf(project, c, mine);
                if (state == PipeDraftingState.ReadyToDraw || state == PipeDraftingState.DrawingStale)
                    ready.Add(c);
            }
            return ready;
        }
    }
}
