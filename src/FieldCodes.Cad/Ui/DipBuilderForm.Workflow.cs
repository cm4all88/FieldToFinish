using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using FieldCodes.Settings;
using FieldCodes.Utilities;

using CogoPoint = Autodesk.Civil.DatabaseServices.CogoPoint;
using OpenMode = Autodesk.AutoCAD.DatabaseServices.OpenMode;

namespace FieldCodes.Cad.Ui
{
    // =====================================================================
    // The production workflow: walking the network.
    //
    // Everything here is about the four questions a drafter has in front of a
    // structure -- where am I, what is connected, what is drawn, what next --
    // answered in the window instead of at the command line.
    //
    // The decisions are made by DipWorkflow in FieldCodes, which is testable
    // without AutoCAD. This file is the presentation of them: buttons, labels
    // and the two picks that genuinely need the drawing.
    // =====================================================================
    internal sealed partial class DipBuilderForm
    {
        // Session navigation. Not project data -- see DipNavigator.
        private readonly DipNavigator _nav = new DipNavigator();

        private Button _backButton;
        private Button _forwardButton;
        private Button _recentButton;
        private Label _statusChip;
        private ContextMenuStrip _recentMenu;

        // The selected pipe's state and actions.
        private Panel _pipeCard;
        private Label _pipeHeadline;
        private Label _pipeConnection;
        private Label _pipeReasons;
        private Label _pipeBothEnds;
        private Label _pipeDrawing;
        private Label _pipeNext;
        private LinkLabel _openFarEnd;
        private Button _connectsToButton;
        private Button _drawSelectedButton;
        private Button _moveLabelButton;
        private Button _labelTextButton;

        // ============================================================ navigation

        /// <summary>Back / Forward / recents / Add-Open, above everything else,
        /// so returning to a structure never means picking it out of the drawing
        /// again.</summary>
        private Control BuildNavigationBar()
        {
            var bar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top, AutoSize = true, WrapContents = false,
                BackColor = Surface, Padding = new Padding(20, 6, 20, 2)
            };

            _backButton = Btn("< Back", (s, e) => NavigateTo(_nav.Back()));
            _forwardButton = Btn("Forward >", (s, e) => NavigateTo(_nav.Forward()));
            _recentButton = Btn("Recent...", OnShowRecent);
            _recentMenu = new ContextMenuStrip();

            _statusChip = new Label
            {
                AutoSize = true, Font = F(9f, true), ForeColor = Muted,
                Margin = new Padding(14, 6, 0, 0), Text = string.Empty
            };

            bar.Controls.Add(_backButton);
            bar.Controls.Add(_forwardButton);
            bar.Controls.Add(_recentButton);
            bar.Controls.Add(Btn("Add / Open structure...", OnAddOrOpenStructure, true));
            bar.Controls.Add(_statusChip);
            return bar;
        }

        private void NavigateTo(string structureId)
        {
            if (structureId == null) { Say("Nothing to go to.", Muted); return; }
            _structureId = structureId;
            _labelEdited = false;
            _candidates = new List<ConnectionCandidate>();
            if (_candidateList != null) _candidateList.Items.Clear();
            if (_map != null) _map.Candidates = null;
            if (_tabs != null) _tabs.SelectedIndex = 0;
            RefreshFromSession();
        }

        private void OnShowRecent(object sender, EventArgs e)
        {
            var project = DipSession.Project;
            if (project == null) return;
            _nav.Prune(project);
            _recentMenu.Items.Clear();

            foreach (var id in _nav.Recent)
            {
                var structure = project.Structure(id);
                if (structure == null) continue;
                var status = DipWorkflow.StatusOf(project, structure, CurrentFindings());
                var item = new ToolStripMenuItem(structure.Label + "   -   " + DipWorkflow.Describe(status));
                var target = id;
                item.Click += (s2, e2) => { _nav.Open(target); NavigateTo(target); };
                _recentMenu.Items.Add(item);
            }

            if (_recentMenu.Items.Count == 0)
                _recentMenu.Items.Add(new ToolStripMenuItem("Nothing visited yet this session") { Enabled = false });
            _recentMenu.Show(_recentButton, new Point(0, _recentButton.Height));
        }

        /// <summary>
        /// One action for both cases. Pick a structure point: if the project already
        /// has it, open it; if not, add it with the existing creation logic and ask
        /// whether to go there. Nothing is duplicated and nothing is closed.
        /// </summary>
        private void OnAddOrOpenStructure(object sender, EventArgs e)
        {
            var cameFrom = _structureId;
            var posted = DipSession.Post("add or open structure", (db, tr, ed, project, settings, version) =>
            {
                var options = new PromptEntityOptions("\nSelect the structure's survey point: ");
                options.SetRejectMessage("\nThat is not a COGO point.");
                options.AddAllowedClass(typeof(CogoPoint), true);
                var picked = ed.GetEntity(options);
                if (picked.Status != PromptStatus.OK) return false;

                var point = (CogoPoint)tr.GetObject(picked.ObjectId, OpenMode.ForRead);
                var existing = project.StructureByPoint(
                    point.PointNumber.ToString(CultureInfo.InvariantCulture));
                var isNew = existing == null;

                var record = UtilityCadService.EnsureStructure(project, UtilityCadService.Snapshot(point), settings.Dips);
                ed.WriteMessage(isNew
                    ? "\nDip Builder: {0} added, rim {1:0.00} from the drawing."
                    : "\nDip Builder: {0} opened, rim {1:0.00} from the drawing.", record.Label, record.Cad.Rim);

                var target = record.Id;
                var label = record.Label;
                BeginInvoke(new Action(() => AfterStructurePicked(target, label, isNew, cameFrom)));
                return true;
            });
            if (posted) Say("Pick the structure's survey point in the drawing...", Muted);
        }

        /// <summary>After adding one, say plainly what happened and let the drafter
        /// decide whether to go there -- rather than moving them silently.</summary>
        private void AfterStructurePicked(string structureId, string label, bool isNew, string cameFrom)
        {
            if (!isNew || cameFrom == null || cameFrom == structureId)
            {
                _nav.Open(structureId);
                NavigateTo(structureId);
                return;
            }

            var here = DipSession.Project != null ? DipSession.Project.Structure(cameFrom) : null;
            var stayText = here != null ? "Stay at " + here.Label : "Stay where I am";
            var answer = MessageBox.Show(this,
                label + " added to the dip project.\n\nOpen it now, or stay where you are?\n\n" +
                "Yes  -  Open " + label + "\nNo   -  " + stayText,
                "Structure added", MessageBoxButtons.YesNo, MessageBoxIcon.Information);

            if (answer == DialogResult.Yes)
            {
                _nav.Open(structureId);
                NavigateTo(structureId);
            }
            else
            {
                Say(label + " added. Still working on " + (here != null ? here.Label : "this structure") + ".", Good);
                RefreshWorkflow();
            }
        }

        // ====================================================== the selected pipe

        private Control BuildPipeCard()
        {
            var card = new Panel
            {
                Dock = DockStyle.Top, AutoSize = true, BackColor = Surface,
                Padding = new Padding(14, 10, 14, 10), Margin = new Padding(0, 6, 0, 6)
            };
            _pipeCard = card;
            card.Paint += (s, e) => e.Graphics.DrawRectangle(new Pen(Rule), 0, 0, card.Width - 1, card.Height - 1);

            var stack = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Dock = DockStyle.Top
            };

            _pipeHeadline = new Label { AutoSize = true, Font = F(11f, true), ForeColor = Ink, Text = "No pipe selected" };
            _pipeConnection = new Label { AutoSize = true, ForeColor = Muted, Margin = new Padding(0, 4, 0, 0) };
            _openFarEnd = new LinkLabel { AutoSize = true, Visible = false, Margin = new Padding(0, 2, 0, 0) };
            _openFarEnd.LinkClicked += OnOpenFarEnd;
            _pipeBothEnds = new Label { AutoSize = true, ForeColor = Muted, Margin = new Padding(0, 2, 0, 0) };
            _pipeReasons = new Label { AutoSize = true, ForeColor = Muted, MaximumSize = new Size(520, 0), Margin = new Padding(0, 2, 0, 0) };
            _pipeDrawing = new Label { AutoSize = true, ForeColor = Muted, Margin = new Padding(0, 4, 0, 0) };
            _pipeNext = new Label { AutoSize = true, Font = F(9.5f, true), ForeColor = Ink, Margin = new Padding(0, 6, 0, 2) };

            _connectsToButton = Btn("Connects to...", OnConnectsTo, true);
            _drawSelectedButton = Btn("Draw pipe + label", OnDrawSelectedPipe);
            _moveLabelButton = Btn("Move label...", OnMovePipeLabel);
            _labelTextButton = Btn("Label text...", OnEditPipeLabelText);

            var actions = Row(_connectsToButton, _drawSelectedButton, _moveLabelButton, _labelTextButton);
            actions.Margin = new Padding(0, 6, 0, 0);

            foreach (Control c in new Control[]
                     { _pipeHeadline, _pipeConnection, _openFarEnd, _pipeBothEnds, _pipeReasons, _pipeDrawing, _pipeNext, actions })
                stack.Controls.Add(c);

            card.Controls.Add(stack);
            return card;
        }

        private IList<QcFinding> CurrentFindings()
        {
            var project = DipSession.Project;
            if (project == null || _settings == null) return new List<QcFinding>();
            try { return UtilityQc.Evaluate(project, _settings.Dips); }
            catch (Exception) { return new List<QcFinding>(); }
        }

        private PipeWorkflowState SelectedState()
        {
            var project = DipSession.Project;
            var s = Current;
            var p = SelectedPipe;
            if (project == null || s == null || p == null) return null;
            return DipWorkflow.Describe(project, s, p, CurrentFindings());
        }

        /// <summary>Redraws the navigation chip and the selected pipe card from the
        /// current state. Safe to call as often as the form likes.</summary>
        internal void RefreshWorkflow()
        {
            var project = DipSession.Project;
            if (_backButton != null)
            {
                _backButton.Enabled = _nav.CanGoBack;
                _forwardButton.Enabled = _nav.CanGoForward;
            }

            if (_statusChip != null)
            {
                var s = Current;
                if (s == null || project == null) _statusChip.Text = string.Empty;
                else
                {
                    var status = DipWorkflow.StatusOf(project, s, CurrentFindings());
                    _statusChip.Text = s.Label + "  -  " + DipWorkflow.Describe(status);
                    _statusChip.ForeColor = ColourFor(status);
                }
            }

            if (_pipeCard == null) return;
            var state = SelectedState();
            if (state == null)
            {
                _pipeHeadline.Text = "No pipe selected";
                _pipeConnection.Text = "Select a pipe row to see where it connects and what has been drawn.";
                _openFarEnd.Visible = false;
                _pipeBothEnds.Text = _pipeReasons.Text = _pipeDrawing.Text = _pipeNext.Text = string.Empty;
                _connectsToButton.Enabled = _drawSelectedButton.Enabled =
                    _moveLabelButton.Enabled = _labelTextButton.Enabled = false;
                return;
            }

            _pipeHeadline.Text = ConnectionFinder.Describe(state.Pipe) +
                                 (state.Elevation != null
                                     ? "    " + DipElevations.Describe(state.Pipe.Reference) + " " +
                                       state.Elevation.Value.ToString("0.00", CultureInfo.InvariantCulture)
                                     : string.Empty);

            _pipeConnection.Text = "Connection: " + DipWorkflow.Describe(state.ConnectionState) +
                (state.ConnectedTo != null ? "  ->  " + state.ConnectedTo.Label : string.Empty) +
                (state.ConnectionConfidence != Confidence.None
                    ? "   (" + state.ConnectionConfidence + " confidence)" : string.Empty);
            _pipeConnection.ForeColor = state.ConnectionState == PipeConnectionState.Suggested ? Warn : Muted;

            _openFarEnd.Visible = state.ConnectedTo != null;
            if (state.ConnectedTo != null) _openFarEnd.Text = "Open " + state.ConnectedTo.Label;

            if (state.ConnectedTo == null) _pipeBothEnds.Text = string.Empty;
            else if (state.BothEndsObserved && state.Slope != null && state.Slope.SlopePercent.HasValue)
            {
                _pipeBothEnds.Text = string.Format(CultureInfo.InvariantCulture,
                    "Both ends observed.   Length {0:0.00}'   Slope {1:0.00}%",
                    state.Slope.HorizontalDistance, state.Slope.SlopePercent.Value);
                _pipeBothEnds.ForeColor = Good;
            }
            else
            {
                _pipeBothEnds.Text = state.Slope != null ? state.Slope.Explanation : "Far end not observed.";
                _pipeBothEnds.ForeColor = Muted;
            }

            _pipeReasons.Text = state.ConnectionReasons.Count > 0
                ? "Why: " + string.Join("; ", state.ConnectionReasons.ToArray()) : string.Empty;

            _pipeDrawing.Text = "Drawing: " + DipWorkflow.Describe(state.DraftingState) +
                                "     Label: " + DipWorkflow.Describe(state.LabelState);
            _pipeDrawing.ForeColor =
                state.DraftingState == PipeDraftingState.DrawingStale ? Warn :
                state.DraftingState == PipeDraftingState.Drawn ? Good : Muted;

            _pipeNext.Text = state.NextAction != null ? "Next:  " + state.NextAction : "Nothing outstanding on this pipe.";
            _pipeNext.ForeColor = state.NextAction != null ? Ink : Good;

            _connectsToButton.Enabled = true;
            _drawSelectedButton.Enabled = state.DraftingState == PipeDraftingState.ReadyToDraw ||
                                          state.DraftingState == PipeDraftingState.DrawingStale ||
                                          state.DraftingState == PipeDraftingState.Drawn;
            _drawSelectedButton.Text = state.DraftingState == PipeDraftingState.DrawingStale
                ? "Update drawing" : "Draw pipe + label";
            _moveLabelButton.Enabled = state.Connection != null && state.Connection.Drafted;
            _labelTextButton.Enabled = state.Connection != null && state.Connection.IsAccepted;
        }

        private static Color ColourFor(StructureStatus status)
        {
            switch (status)
            {
                case StructureStatus.Complete: return Good;
                case StructureStatus.FieldRevisit:
                case StructureStatus.NeedsReview:
                case StructureStatus.DrawingStale: return Warn;
                default: return Muted;
            }
        }

        private void OnOpenFarEnd(object sender, LinkLabelLinkClickedEventArgs e)
        {
            var state = SelectedState();
            if (state == null || state.ConnectedTo == null) return;
            _nav.Open(state.ConnectedTo.Id);
            NavigateTo(state.ConnectedTo.Id);
        }

        // ================================================== connects to...

        /// <summary>
        /// The direct route: with a pipe selected, click the structure it runs to.
        /// No find, no candidate list, no confirm step. It goes through the existing
        /// ManualCandidate / Accept path, so it is recorded as the drafter's decision
        /// and never as a field observation.
        /// </summary>
        private void OnConnectsTo(object sender, EventArgs e)
        {
            var s = Current;
            var p = SelectedPipe;
            if (s == null || p == null) { Say("Select a pipe row first.", Muted); return; }

            // The reason is asked here, in the window, not at the command line.
            string note;
            using (var ask = new ManualConnectionReasonDialog(ConnectionFinder.Describe(p)))
            {
                if (ask.ShowDialog(this) != DialogResult.OK) return;
                note = ask.Reason;
            }

            var structureId = s.Id;
            var pipeId = p.Id;
            var cameFrom = s.Id;

            var posted = DipSession.Post("connects to", (db, tr, ed, project, settings, version) =>
            {
                var structure = project.Structure(structureId);
                var pipe = project.Pipe(structureId, pipeId);
                if (structure == null || pipe == null) return false;

                var options = new PromptEntityOptions("\nClick the structure this pipe runs to: ");
                options.SetRejectMessage("\nThat is not a COGO point.");
                options.AddAllowedClass(typeof(CogoPoint), true);
                var picked = ed.GetEntity(options);
                if (picked.Status != PromptStatus.OK) return false;

                var point = (CogoPoint)tr.GetObject(picked.ObjectId, OpenMode.ForRead);
                var target = UtilityCadService.EnsureStructure(project, UtilityCadService.Snapshot(point), settings.Dips);
                if (target.Id == structure.Id)
                {
                    ed.WriteMessage("\nA pipe cannot connect a structure to itself.");
                    return false;
                }

                var candidate = ConnectionFinder.ManualCandidate(project, structure, pipe, target, settings.Dips);
                ConnectionFinder.Accept(project, structure, pipe, candidate, true, note);
                ed.WriteMessage("\nDip Builder: {0} -> {1} recorded as a drafter-selected connection.",
                                structure.Label, target.Label);

                var targetId = target.Id;
                var targetLabel = target.Label;
                BeginInvoke(new Action(() => AfterConnected(targetId, targetLabel, cameFrom)));
                return true;
            });
            if (posted) Say("Click the structure this pipe runs to...", Muted);
        }

        /// <summary>Offers the far structure straight away: this is what makes it feel
        /// like walking the network rather than filing separate records.</summary>
        private void AfterConnected(string targetId, string targetLabel, string cameFrom)
        {
            RefreshFromSession();
            var here = DipSession.Project != null ? DipSession.Project.Structure(cameFrom) : null;
            var answer = MessageBox.Show(this,
                "Connected to " + targetLabel + ".\n\nOpen it now, or stay here?\n\n" +
                "Yes  -  Open " + targetLabel + "\nNo   -  Stay at " + (here != null ? here.Label : "this structure"),
                "Connected", MessageBoxButtons.YesNo, MessageBoxIcon.Information);

            if (answer == DialogResult.Yes)
            {
                _nav.Open(targetId);
                NavigateTo(targetId);
            }
            else Say("Connected to " + targetLabel + ".", Good);
        }

        // ========================================================= draw one pipe

        /// <summary>Draws the selected pipe and its label, and nothing else.</summary>
        private void OnDrawSelectedPipe(object sender, EventArgs e)
        {
            var state = SelectedState();
            if (state == null || state.Connection == null || !state.Connection.IsAccepted)
            { Say("Connect this pipe first.", Muted); return; }

            var connectionId = state.Connection.Id;
            var posted = DipSession.Post("draw selected pipe", (db, tr, ed, project, settings, version) =>
            {
                UtilityCommands.DrawConnections(db, tr, ed, project, settings, version,
                    c => c.Id == connectionId, AskAboutExistingPipe);
                return true;
            });
            if (posted) Say("Drawing the selected pipe and its label...", Muted);
        }

        /// <summary>
        /// The existing-pipe question, in the window. Same four answers and the same
        /// meanings as the command line; hand-drawn geometry is still never erased
        /// without an explicit Replace.
        /// </summary>
        private ExistingPipeDecision AskAboutExistingPipe(PipeConnection connection, IList<ExistingPipe> existing)
        {
            var project = DipSession.Project;
            var from = project != null ? project.Structure(connection.FromStructureId) : null;
            var to = project != null ? project.Structure(connection.ToStructureId) : null;
            var ownedByFtf = existing.Any(x => x.OwnedByFtf);
            var layers = string.Join(", ", existing.Select(x => x.Layer).Distinct().ToArray());

            var decision = ExistingPipeDecision.Keep;
            Action ask = () =>
            {
                using (var dialog = new ExistingPipeDialog(
                           (from != null ? from.Label : "?") + "  ->  " + (to != null ? to.Label : "?"),
                           existing.Count, ownedByFtf, layers))
                {
                    dialog.ShowDialog(this);
                    decision = dialog.Decision;
                }
            };

            // Posted work runs on AutoCAD's main thread, which is this window's
            // thread, so this normally just runs. The check keeps it correct if that
            // ever stops being true -- and the answer must come back synchronously,
            // because the drafting decision is waiting on it.
            if (InvokeRequired) Invoke(ask); else ask();
            return decision;
        }

        // ============================================================== the label

        /// <summary>Click where the pipe label goes. The position is kept on the
        /// connection, so a later redraw does not move it back to the midpoint.</summary>
        private void OnMovePipeLabel(object sender, EventArgs e)
        {
            var state = SelectedState();
            if (state == null || state.Connection == null || !state.Connection.Drafted)
            { Say("Draw the pipe first.", Muted); return; }

            var connectionId = state.Connection.Id;
            var posted = DipSession.Post("move pipe label", (db, tr, ed, project, settings, version) =>
            {
                var connection = project.Connections.FirstOrDefault(c => c.Id == connectionId);
                if (connection == null) return false;

                var at = ed.GetPoint(new PromptPointOptions("\nWhere should the pipe label sit? "));
                if (at.Status != PromptStatus.OK) return false;

                var point = at.Value.TransformBy(ed.CurrentUserCoordinateSystem);

                // Keep the rotation the label already has, so moving it does not also
                // silently re-angle it.
                Point3d current; double rotation; bool moved; string text;
                if (!UtilityCadService.TryReadPipeLabel(db, tr, connection, out current, out rotation, out moved, out text))
                    rotation = 0;

                DipWorkflow.PlaceLabel(connection, point.X, point.Y, rotation);
                project.Overrides.Add(new ManualOverride
                {
                    Target = connection.Id, What = "Pipe label moved by drafter",
                    Entered = string.Format(CultureInfo.InvariantCulture, "{0:0.00}, {1:0.00}", point.X, point.Y),
                    Utc = DateTime.UtcNow
                });

                // Redraw just this pipe so the label lands where they clicked.
                UtilityCommands.DrawConnections(db, tr, ed, project, settings, version,
                    c => c.Id == connectionId, (c, x) => ExistingPipeDecision.Replace);
                ed.WriteMessage("\nDip Builder: pipe label placed. A redraw keeps it here.");
                return true;
            });
            if (posted) Say("Click where the pipe label should sit...", Muted);
        }

        /// <summary>Edit the label text. An override is kept beside the generated text
        /// so it stays visible as an override, and survives a redraw.</summary>
        private void OnEditPipeLabelText(object sender, EventArgs e)
        {
            var state = SelectedState();
            var project = DipSession.Project;
            if (state == null || state.Connection == null || project == null || _settings == null)
            { Say("Select a connected pipe first.", Muted); return; }

            var generated = UtilityLabelFormatter.PipeLabel(project, state.Connection, _settings.Dips);
            var current = state.Connection.LabelTextOverride ?? generated;

            string entered;
            using (var dialog = new PipeLabelTextDialog(generated, current))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                entered = dialog.LabelText;
            }

            var connectionId = state.Connection.Id;
            var wasDrafted = state.Connection.Drafted;
            var posted = DipSession.Post("pipe label text", (db, tr, ed, project2, settings, version) =>
            {
                var connection = project2.Connections.FirstOrDefault(c => c.Id == connectionId);
                if (connection == null) return false;

                DipWorkflow.OverrideLabelText(project2, connection, settings.Dips, entered);
                if (connection.LabelTextIsOverridden)
                    project2.Overrides.Add(new ManualOverride
                    {
                        Target = connection.Id, What = "Pipe label text",
                        Generated = connection.LabelTextOverrodeGenerated, Entered = entered, Utc = DateTime.UtcNow
                    });

                if (wasDrafted)
                    UtilityCommands.DrawConnections(db, tr, ed, project2, settings, version,
                        c => c.Id == connectionId, (c, x) => ExistingPipeDecision.Replace);
                return true;
            });
            if (posted) Say("Pipe label text updated.", Good);
        }

        // ============================================================ draw ready

        /// <summary>Everything accepted and not yet drawn, or drawn and now stale.</summary>
        private void OnDrawAllReady(object sender, EventArgs e)
        {
            var project = DipSession.Project;
            if (project == null) return;
            var ready = DipWorkflow.ReadyToDraw(project, CurrentFindings()).Select(c => c.Id).ToList();
            if (ready.Count == 0) { Say("Nothing is waiting to be drawn.", Good); return; }

            var posted = DipSession.Post("draw all ready pipes", (db, tr, ed, p2, settings, version) =>
            {
                UtilityCommands.DrawConnections(db, tr, ed, p2, settings, version,
                    c => ready.Contains(c.Id), AskAboutExistingPipe);
                return true;
            });
            if (posted) Say("Drawing " + ready.Count + " pipe(s) that are ready...", Muted);
        }
    }
}
