using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using FieldCodes.Settings;
using FieldCodes.Standards;

namespace FieldCodes.Cad.Setup
{
    /// <summary>What a fix did: done, and a sentence to show.</summary>
    internal sealed class FixOutcome
    {
        public bool Done { get; set; }
        public string Message { get; set; }

        public static FixOutcome Ok(string message) { return new FixOutcome { Done = true, Message = message }; }
        public static FixOutcome Failed(string message) { return new FixOutcome { Done = false, Message = message }; }
    }

    /// <summary>
    /// Carries out the fix a user picked in the Check Standards window. Settings > Layers writes
    /// to the settings being edited (saved with the window's Save); FTFCHECKSTANDARDS writes to the
    /// settings file in force. Drawing fixes (a layer, a linetype) happen at once, in a transaction.
    /// </summary>
    internal interface IStandardsFixer
    {
        FixOutcome CreateLayer(StandardsIssue issue);
        FixOutcome LoadLinetype(StandardsIssue issue);
        FixOutcome MapLayer(StandardsIssue issue, string target);
        FixOutcome UseExisting(StandardsIssue issue, string name);
    }

    /// <summary>
    /// The preview before layers already in the drawing change: every property of every layer FTF
    /// uses that differs from its standard, ticked; nothing changes until "Apply Checked Changes".
    /// </summary>
    internal sealed class LayerUpdatePreviewForm : Form
    {
        private readonly ListView _list;
        private readonly LayerUpdatePlan _plan;

        public LayerUpdatePreviewForm(LayerUpdatePlan plan, string source)
        {
            _plan = plan ?? new LayerUpdatePlan();
            Text = "Update Drawing Layers - Preview";
            Width = 860;
            Height = 520;
            MinimumSize = new Size(600, 360);
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            ShowInTaskbar = false;

            var top = new Label
            {
                Dock = DockStyle.Top,
                AutoSize = false,
                Height = 64,
                Padding = new Padding(8, 8, 8, 0),
                Text = string.Format(
                    "{0} change(s) on {1} of the {2} layer(s) FTF uses in this drawing ({3} have no standard). Nothing has changed yet.\n" +
                    "Only these layers' properties change: no object is moved, restyled or erased, no layer is renamed, and layers FTF does " +
                    "not use are not touched. Untick anything to leave as it is.\nStandards: {4}",
                    _plan.Changes.Count, _plan.LayersChanged, _plan.LayersCompared, _plan.LayersWithoutStandard, source)
            };

            _list = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                CheckBoxes = true,
                FullRowSelect = true,
                GridLines = true,
                HideSelection = false
            };
            _list.Columns.Add("Layer", 230);
            _list.Columns.Add("Property", 90);
            _list.Columns.Add("In drawing now", 150);
            _list.Columns.Add("Standard", 150);
            _list.Columns.Add("From standard", 180);
            foreach (var change in _plan.Changes)
            {
                var item = new ListViewItem(new[]
                {
                    change.Layer, LayerStandards.PropertyName(change.Property), change.From, change.To,
                    string.Equals(change.Standard, change.Layer, StringComparison.OrdinalIgnoreCase) ? "(its own)" : change.Standard
                })
                { Checked = true, Tag = change };
                _list.Items.Add(item);
            }

            var problems = new Label
            {
                Dock = DockStyle.Bottom,
                AutoSize = false,
                Height = _plan.Problems.Count == 0 ? 0 : 44,
                ForeColor = Color.FromArgb(160, 60, 0),
                Padding = new Padding(8, 4, 8, 0),
                Text = _plan.Problems.Count == 0 ? string.Empty : "Not applied (fix the standard first): " + string.Join("; ", _plan.Problems.Take(6).ToArray())
            };

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 40, Padding = new Padding(6) };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
            var apply = new Button { Text = "Apply Checked Changes", DialogResult = DialogResult.OK, AutoSize = true, Enabled = _plan.Changes.Count > 0 };
            var none = new Button { Text = "Uncheck All", AutoSize = true };
            var all = new Button { Text = "Check All", AutoSize = true };
            none.Click += (s, e) => { foreach (ListViewItem i in _list.Items) i.Checked = false; };
            all.Click += (s, e) => { foreach (ListViewItem i in _list.Items) i.Checked = true; };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(apply);
            buttons.Controls.Add(none);
            buttons.Controls.Add(all);
            AcceptButton = apply;
            CancelButton = cancel;

            Controls.Add(_list);
            Controls.Add(problems);
            Controls.Add(buttons);
            Controls.Add(top);
        }

        /// <summary>The ticked changes.</summary>
        public IList<LayerPropertyChange> Chosen
        {
            get { return _list.Items.Cast<ListViewItem>().Where(i => i.Checked).Select(i => (LayerPropertyChange)i.Tag).ToList(); }
        }
    }

    /// <summary>
    /// Check Standards: what the settings and rules name that this drawing does not have, and a
    /// fix for each that has one. A fix happens only when its button is pressed.
    /// </summary>
    internal sealed class StandardsCheckForm : Form
    {
        private readonly IList<StandardsIssue> _issues;
        private readonly IStandardsFixer _fixer;
        private readonly Dictionary<StandardsIssue, string> _fixed = new Dictionary<StandardsIssue, string>();
        private readonly DataGridView _grid;
        private readonly CheckBox _notes;
        private readonly Label _detail;
        private readonly ComboBox _choice;
        private readonly Button _act;
        private readonly Button _instead;
        private readonly Button _createAll;
        private readonly Label _status;

        public StandardsCheckForm(IList<StandardsIssue> issues, IStandardsFixer fixer, string where)
        {
            _issues = issues ?? new List<StandardsIssue>();
            _fixer = fixer;
            Text = "Check Standards";
            Width = 1000;
            Height = 620;
            MinimumSize = new Size(700, 420);
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            ShowInTaskbar = false;

            var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 54, Padding = new Padding(8, 6, 8, 0), WrapContents = true };
            top.Controls.Add(new Label
            {
                AutoSize = true,
                MaximumSize = new Size(940, 0),
                Text = StandardsCheck.Summary(_issues) + " Checked: " + where + ". Nothing is changed unless you pick a fix below."
            });
            _notes = new CheckBox { Text = "Show notes (layers FTF will create, unused standards)", AutoSize = true, Checked = true };
            _notes.CheckedChanged += (s, e) => Fill();
            top.Controls.Add(_notes);

            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = SystemColors.Window
            };
            _grid.Columns.Add("Severity", "Severity");
            _grid.Columns.Add("Kind", "What");
            _grid.Columns.Add("Name", "Name");
            _grid.Columns.Add("Where", "Referenced by");
            _grid.Columns.Add("Problem", "Problem");
            _grid.Columns.Add("Choices", "Choices");
            _grid.Columns[0].FillWeight = 8;
            _grid.Columns[1].FillWeight = 11;
            _grid.Columns[2].FillWeight = 15;
            _grid.Columns[3].FillWeight = 20;
            _grid.Columns[4].FillWeight = 28;
            _grid.Columns[5].FillWeight = 18;
            _grid.SelectionChanged += (s, e) => ShowSelected();

            var bottom = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 132, ColumnCount = 1, RowCount = 3, Padding = new Padding(8, 4, 8, 4) };
            bottom.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            bottom.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            bottom.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            _detail = new Label { Dock = DockStyle.Fill, AutoSize = false };
            bottom.Controls.Add(_detail, 0, 0);

            var row = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            _choice = new ComboBox
            {
                Width = 320,
                DropDownStyle = ComboBoxStyle.DropDown,
                AutoCompleteMode = AutoCompleteMode.SuggestAppend,
                AutoCompleteSource = AutoCompleteSource.ListItems
            };
            _act = new Button { AutoSize = true, Text = "Fix", Enabled = false };
            _act.Click += (s, e) => Act();
            _instead = new Button { AutoSize = true, Text = "Send to the Chosen Layer Instead", Visible = false };
            _instead.Click += (s, e) => SendInstead();
            _createAll = new Button { AutoSize = true, Text = "Create All Listed Layers" };
            _createAll.Click += (s, e) => CreateAll();
            var close = new Button { AutoSize = true, Text = "Close", DialogResult = DialogResult.Cancel };
            row.Controls.Add(_choice);
            row.Controls.Add(_act);
            row.Controls.Add(_instead);
            row.Controls.Add(_createAll);
            row.Controls.Add(close);
            bottom.Controls.Add(row, 0, 1);
            _status = new Label { Dock = DockStyle.Fill, AutoSize = false, ForeColor = Color.FromArgb(0, 110, 0) };
            bottom.Controls.Add(_status, 0, 2);
            CancelButton = close;

            Controls.Add(_grid);
            Controls.Add(bottom);
            Controls.Add(top);
            Fill();
        }

        private void Fill()
        {
            _grid.Rows.Clear();
            foreach (var issue in _issues)
            {
                if (!_notes.Checked && issue.Severity == Severity.Info) continue;
                string done;
                var fixedText = _fixed.TryGetValue(issue, out done) ? "Fixed: " + done : string.Join("; ", issue.Choices.ToArray());
                var i = _grid.Rows.Add(SeverityText(issue.Severity), StandardsIssue.KindName(issue.Kind), issue.Name, issue.What, issue.Problem, fixedText);
                var r = _grid.Rows[i];
                r.Tag = issue;
                r.Cells[3].ToolTipText = issue.What;
                r.Cells[4].ToolTipText = issue.Problem;
                if (_fixed.ContainsKey(issue)) r.DefaultCellStyle.ForeColor = SystemColors.GrayText;
                else if (issue.Severity == Severity.Error) r.Cells[0].Style.ForeColor = Color.FromArgb(180, 0, 0);
                else if (issue.Severity == Severity.Warning) r.Cells[0].Style.ForeColor = Color.FromArgb(160, 90, 0);
            }
            _createAll.Visible = _fixer != null && _issues.Any(x => x.Fix == StandardsFix.CreateLayer && !_fixed.ContainsKey(x));
            ShowSelected();
        }

        private static string SeverityText(Severity s)
        {
            return s == Severity.Error ? "Error" : s == Severity.Warning ? "Warning" : "Note";
        }

        private StandardsIssue Selected()
        {
            return _grid.SelectedRows.Count == 0 ? null : _grid.SelectedRows[0].Tag as StandardsIssue;
        }

        private void ShowSelected()
        {
            var issue = Selected();
            _choice.Items.Clear();
            _choice.Text = string.Empty;
            if (issue == null)
            {
                _detail.Text = "Select a row to see its choices.";
                _act.Enabled = false;
                _choice.Enabled = false;
                _instead.Visible = false;
                return;
            }
            _detail.Text = StandardsIssue.KindName(issue.Kind) + " " + issue.Name + ": " + issue.Problem + "\nChoices: " + string.Join("; ", issue.Choices.ToArray());
            var done = _fixed.ContainsKey(issue);
            var needsChoice = issue.Fix == StandardsFix.MapLayer || issue.Fix == StandardsFix.ChooseExisting;
            foreach (var c in issue.Candidates) _choice.Items.Add(c);
            if (_choice.Items.Count > 0) _choice.SelectedIndex = 0;
            // A layer FTF would create can instead be sent to one the drawing has.
            var canSendInstead = issue.Fix == StandardsFix.CreateLayer && _choice.Items.Count > 0 && _fixer != null && !done;
            _instead.Visible = canSendInstead;
            _choice.Enabled = (needsChoice || canSendInstead) && !done;
            _act.Enabled = _fixer != null && !done && issue.Fix != StandardsFix.None && (!needsChoice || _choice.Items.Count > 0);
            switch (issue.Fix)
            {
                case StandardsFix.CreateLayer: _act.Text = "Create Layer " + issue.Name; break;
                case StandardsFix.LoadLinetype: _act.Text = "Load " + issue.Name + " from acad.lin"; break;
                case StandardsFix.MapLayer: _act.Text = "Send to This Layer"; break;
                case StandardsFix.ChooseExisting: _act.Text = "Use This " + StandardsIssue.KindName(issue.Kind); break;
                default: _act.Text = "No fix from here"; break;
            }
        }

        private void Act()
        {
            var issue = Selected();
            if (issue == null || _fixer == null) return;
            var choice = (_choice.Text ?? string.Empty).Trim();
            if ((issue.Fix == StandardsFix.MapLayer || issue.Fix == StandardsFix.ChooseExisting) &&
                !issue.Candidates.Contains(choice, StringComparer.OrdinalIgnoreCase))
            {
                _status.ForeColor = Color.FromArgb(180, 0, 0);
                _status.Text = "\"" + choice + "\" is not in this drawing: choose one from the list.";
                return;
            }
            FixOutcome outcome;
            switch (issue.Fix)
            {
                case StandardsFix.CreateLayer: outcome = _fixer.CreateLayer(issue); break;
                case StandardsFix.LoadLinetype: outcome = _fixer.LoadLinetype(issue); break;
                case StandardsFix.MapLayer: outcome = _fixer.MapLayer(issue, choice); break;
                case StandardsFix.ChooseExisting: outcome = _fixer.UseExisting(issue, choice); break;
                default: return;
            }
            Report(issue, outcome);
            Fill();
        }

        private void SendInstead()
        {
            var issue = Selected();
            if (issue == null || _fixer == null) return;
            var choice = (_choice.Text ?? string.Empty).Trim();
            if (!issue.Candidates.Contains(choice, StringComparer.OrdinalIgnoreCase))
            {
                _status.ForeColor = Color.FromArgb(180, 0, 0);
                _status.Text = "\"" + choice + "\" is not in this drawing: choose one from the list.";
                return;
            }
            Report(issue, _fixer.MapLayer(issue, choice));
            Fill();
        }

        private void CreateAll()
        {
            var pending = _issues.Where(x => x.Fix == StandardsFix.CreateLayer && !_fixed.ContainsKey(x)).ToList();
            if (pending.Count == 0) return;
            if (MessageBox.Show(this, "Create " + pending.Count + " layer(s) in this drawing, each with its layer standard?\n\n" +
                                      string.Join("\n", pending.Take(20).Select(p => p.Name).ToArray()) + (pending.Count > 20 ? "\n..." : string.Empty),
                                "Create Layers", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            var made = 0;
            foreach (var issue in pending)
            {
                var outcome = _fixer.CreateLayer(issue);
                if (outcome != null && outcome.Done) { _fixed[issue] = outcome.Message; made++; }
                else if (outcome != null) { _status.ForeColor = Color.FromArgb(180, 0, 0); _status.Text = outcome.Message; }
            }
            if (made > 0) { _status.ForeColor = Color.FromArgb(0, 110, 0); _status.Text = "Created " + made + " layer(s)."; }
            Fill();
        }

        private void Report(StandardsIssue issue, FixOutcome outcome)
        {
            if (outcome == null) return;
            if (outcome.Done) _fixed[issue] = outcome.Message;
            _status.ForeColor = outcome.Done ? Color.FromArgb(0, 110, 0) : Color.FromArgb(180, 0, 0);
            _status.Text = outcome.Message;
        }
    }
}
