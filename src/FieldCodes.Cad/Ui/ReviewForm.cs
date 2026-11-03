using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using FieldCodes.Review;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace FieldCodes.Cad.Ui
{
    /// <summary>
    /// FTFREVIEW: every review item in the drawing, one row each -- what and where, the issue,
    /// why, what to do, how serious, whether it holds back Drafting QC Complete, and where it
    /// stands. Zoom to it, open the tool that fixes it or the settings that govern it, recheck,
    /// mark it not applicable (with a reason) or accept it. Nothing here blocks drafting.
    /// </summary>
    internal sealed class ReviewForm : Form
    {
        private readonly ComboBox _show;
        private readonly ComboBox _source;
        private readonly ListView _list;
        private readonly Label _summary;
        private readonly Label _detail;
        private readonly List<Button> _itemButtons = new List<Button>();
        private ReviewRegister _register = new ReviewRegister();
        private FieldCodes.Settings.FtfSettings _settings;
        private bool _loading;

        private const string ShowOpen = "Open";
        private const string ShowRequired = "Required and open";
        private const string ShowClosed = "Resolved, accepted, not applicable";
        private const string ShowAll = "All";
        private const string AllSources = "(every check)";

        public ReviewForm()
        {
            Text = "FieldToFinish Review";
            Size = new Size(1180, 640);
            MinimumSize = new Size(820, 420);
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = false;

            var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8, 8, 8, 4), WrapContents = false };
            top.Controls.Add(new Label { Text = "Show", AutoSize = true, Margin = new Padding(0, 7, 4, 0) });
            _show = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
            _show.Items.AddRange(new object[] { ShowOpen, ShowRequired, ShowClosed, ShowAll });
            _show.SelectedIndex = 0;
            _show.SelectedIndexChanged += (s, e) => { if (!_loading) Fill(); };
            top.Controls.Add(_show);
            top.Controls.Add(new Label { Text = "from", AutoSize = true, Margin = new Padding(10, 7, 4, 0) });
            _source = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
            _source.SelectedIndexChanged += (s, e) => { if (!_loading) Fill(); };
            top.Controls.Add(_source);
            top.Controls.Add(Button("Recheck all", "Rechecks the codes, linework continuity and point accountability (FTFREVIEWCHECK). Writes only the review list, the point ledger and the QC previews.",
                () => Run("FTFREVIEWCHECK"), false));
            top.Controls.Add(Button("Refresh", "Reads the list from the drawing again.", Reload, false));
            top.Controls.Add(Button("Drafting QC Complete...", "Marks the drawing Drafting QC Complete (FTFQC); refused while required items are open.",
                () => Run("FTFQC"), false));
            top.Controls.Add(Button("Settings", "Settings > Review and QC: which items are required.",
                () => FtfCommand.ShowSettings("Review and QC"), false));

            _summary = new Label { Dock = DockStyle.Top, AutoSize = false, Height = 24, Padding = new Padding(10, 4, 8, 0) };

            _list = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                HideSelection = false,
                MultiSelect = true,
                GridLines = true
            };
            foreach (var c in new[] { "Point / entity", "Feature", "Code", "Issue", "Why", "What to do", "Severity", "Required", "Status", "Note" })
                _list.Columns.Add(c);
            _list.Columns[0].Width = 110; _list.Columns[1].Width = 80; _list.Columns[2].Width = 60; _list.Columns[3].Width = 220; _list.Columns[4].Width = 300;
            _list.Columns[5].Width = 240; _list.Columns[6].Width = 70; _list.Columns[7].Width = 64; _list.Columns[8].Width = 96; _list.Columns[9].Width = 200;
            _list.SelectedIndexChanged += (s, e) => ShowSelected();
            _list.DoubleClick += (s, e) => Zoom();

            _detail = new Label { Dock = DockStyle.Bottom, AutoSize = false, Height = 96, Padding = new Padding(10, 4, 8, 4) };

            var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(8, 4, 8, 8), WrapContents = true };
            actions.Controls.Add(Button("Zoom to", "Zooms to it and selects what it is about (double-click a row does the same).", Zoom, true));
            actions.Controls.Add(Button("Fix...", "Runs the command that corrects it (FTFGATE for a gate, FTFORIENT for a symbol...).", Fix, true));
            actions.Controls.Add(Button("Its settings", "Opens the settings page that governs it.", OpenItsSettings, true));
            actions.Controls.Add(Button("Recheck", "Runs the check that raised it again; an item it no longer finds is resolved.", Recheck, true));
            actions.Controls.Add(Button("Approve correction...", "Draws FTF's proposed correction as a separate line on the feature's layer (the survey linework is never changed) and closes the item.", ApproveCorrection, true));
            actions.Controls.Add(Button("Not applicable...", "Closes the selected items with one reason; reruns keep them closed unless their source changes.", NotApplicable, true));
            actions.Controls.Add(Button("Accept...", "Records that you reviewed the selected items and accept them as drawn (point and linework items need a note).", Accept, true));
            actions.Controls.Add(Button("Reopen", "Puts the selected items back on the open list.", Reopen, true));
            actions.Controls.Add(Button("Select same kind", "Selects every listed item of the same kind and code, for one decision on all of them.", SelectSame, true));

            Controls.Add(_list);
            Controls.Add(_summary);
            Controls.Add(top);
            Controls.Add(_detail);
            Controls.Add(actions);
        }

        private Button Button(string text, string tip, Action click, bool needsItem)
        {
            var b = new Button { Text = text, AutoSize = true, Margin = new Padding(4, 2, 4, 2) };
            new ToolTip().SetToolTip(b, tip);
            b.Click += (s, e) =>
            {
                try { click(); }
                catch (System.Exception ex) { MessageBox.Show(this, ex.Message, "FieldToFinish Review", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            };
            if (needsItem) { b.Enabled = false; _itemButtons.Add(b); }
            return b;
        }

        /// <summary>Reads the list from the active drawing.</summary>
        public void Reload()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) { _register = new ReviewRegister(); Fill(); return; }
            var db = doc.Database;
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                _register = DrawingStore.ReadReview(db, tr);
                try
                {
                    var cfg = FtfSession.Rules(db);
                    _settings = FtfSession.SettingsFor(db, cfg);
                }
                catch (ConfigException) { _settings = null; }
                tr.Commit();
            }

            _loading = true;
            var chosen = _source.SelectedItem as string;
            _source.Items.Clear();
            _source.Items.Add(AllSources);
            foreach (var s in _register.Items.Select(i => i.Source).Where(s => !string.IsNullOrEmpty(s)).Distinct().OrderBy(s => s))
                _source.Items.Add(s);
            _source.SelectedItem = chosen != null && _source.Items.Contains(chosen) ? chosen : AllSources;
            _loading = false;
            Fill();
        }

        private void Fill()
        {
            var keep = Selected();
            _list.BeginUpdate();
            _list.Items.Clear();
            var show = _show.SelectedItem as string ?? ShowOpen;
            var source = _source.SelectedItem as string;
            IEnumerable<ReviewItem> items = _register.Items;
            if (show == ShowOpen) items = items.Where(i => i.IsOpen);
            else if (show == ShowRequired) items = items.Where(i => i.IsOpen && i.Required);
            else if (show == ShowClosed) items = items.Where(i => !i.IsOpen);
            if (!string.IsNullOrEmpty(source) && source != AllSources) items = items.Where(i => i.Source == source);

            foreach (var i in items.OrderByDescending(i => i.IsOpen && i.Required)
                                   .ThenByDescending(i => i.Severity)
                                   .ThenBy(i => i.Source)
                                   .ThenBy(i => i.Points, StringComparer.Ordinal))
            {
                var row = new ListViewItem(new[]
                {
                    string.IsNullOrWhiteSpace(i.Points) ? (i.Handles.Count > 0 ? "entity " + i.Handles[0] : "-") : i.Points,
                    i.FeatureType ?? string.Empty,
                    i.Code ?? string.Empty,
                    i.Issue ?? string.Empty,
                    i.Reason ?? string.Empty,
                    i.SuggestedAction ?? string.Empty,
                    i.Severity.ToString(),
                    i.Required ? "Yes" : "",
                    Status(i.Status),
                    Note(i)
                }) { Tag = i };
                if (i.IsOpen && i.Severity == ReviewSeverity.Error) row.ForeColor = Color.Firebrick;
                else if (!i.IsOpen) row.ForeColor = SystemColors.GrayText;
                _list.Items.Add(row);
                if (keep != null && keep.Id == i.Id) row.Selected = true;
            }
            _list.EndUpdate();

            var qc = _register.QcCompleteAt.HasValue
                ? "Drafting QC Complete " + _register.QcCompleteAt.Value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " by " + _register.QcCompleteBy
                : "Drafting QC Complete: not marked";
            _summary.Text = string.Format(CultureInfo.InvariantCulture,
                "{0} open, {1} required and open, {2} closed.   {3}.", _register.Open.Count, _register.RequiredOpen.Count,
                _register.Items.Count - _register.Open.Count, qc);
            _summary.ForeColor = _register.RequiredOpen.Count > 0 ? Color.Firebrick : SystemColors.ControlText;
            ShowSelected();
        }

        private static string Status(ReviewItemStatus s)
        {
            switch (s)
            {
                case ReviewItemStatus.NotApplicable: return "Not applicable";
                case ReviewItemStatus.Corrected: return "Corrected";
                default: return s.ToString();
            }
        }

        private static string Note(ReviewItem i)
        {
            if (string.IsNullOrWhiteSpace(i.Note)) return string.Empty;
            return i.Note + (string.IsNullOrWhiteSpace(i.ReviewedBy) ? string.Empty : " (" + i.ReviewedBy + ")");
        }

        private ReviewItem Selected()
        {
            return _list.SelectedItems.Count == 0 ? null : _list.SelectedItems[0].Tag as ReviewItem;
        }

        private List<ReviewItem> SelectedAll()
        {
            return _list.SelectedItems.Cast<ListViewItem>().Select(r => r.Tag as ReviewItem).Where(i => i != null).ToList();
        }

        private void ShowSelected()
        {
            var i = Selected();
            var count = _list.SelectedItems.Count;
            foreach (var b in _itemButtons) b.Enabled = i != null;
            if (i == null) { _detail.Text = "Select an item (Ctrl or Shift for several). Double-click zooms to it."; return; }
            var text = new System.Text.StringBuilder();
            if (count > 1) text.Append(count.ToString(CultureInfo.InvariantCulture)).Append(" items selected; showing the first.  ");
            text.Append(i.Source).Append(" -- ").Append(ReviewRegister.Describe(i)).Append(": ").Append(i.Reason);
            if (!string.IsNullOrEmpty(i.Confidence)) text.Append("  [").Append(i.Confidence).Append("]");
            text.Append(Environment.NewLine).Append("What to do: ").Append(i.SuggestedAction);
            if (!string.IsNullOrEmpty(i.FixCommand)) text.Append("  (Fix runs ").Append(i.FixCommand).Append(")");
            if (i.HasProposal && i.IsOpen) text.Append("  A proposed correction is previewed on the QC layer.");
            if (i.Evidence != null && i.Evidence.Count > 0)
                text.Append(Environment.NewLine).Append("Evidence: ").Append(string.Join("; ", i.Evidence.Take(5).ToArray()));
            if (i.History != null && i.History.Count > 0)
                text.Append(Environment.NewLine).Append("History: ").Append(string.Join("; ", i.History.Skip(Math.Max(0, i.History.Count - 4))
                    .Select(h => h.At.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " " + h.Action + (string.IsNullOrWhiteSpace(h.By) || h.By == "FTF" ? string.Empty : " by " + h.By)).ToArray()));
            else text.Append("   First found ").Append(i.FirstSeen.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)).Append(".");
            _detail.Text = text.ToString();
        }

        private void SelectSame()
        {
            var i = Selected();
            if (i == null) return;
            _list.BeginUpdate();
            foreach (ListViewItem row in _list.Items)
            {
                var other = row.Tag as ReviewItem;
                row.Selected = other != null && other.Kind == i.Kind && string.Equals(other.Code ?? string.Empty, i.Code ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            }
            _list.EndUpdate();
            ShowSelected();
        }

        private void ApproveCorrection()
        {
            var i = Selected();
            if (i == null) return;
            if (!i.HasProposal || !i.IsOpen)
            {
                MessageBox.Show(this, "FTF has no correction to draw for this item" + (i.IsOpen ? "." : " (it is not open)."), "FieldToFinish Review");
                return;
            }
            var note = Ask("Approve correction", "Approve the proposed correction for \"" + ReviewRegister.Describe(i) + "\"? A separate line is drawn on " +
                           (i.TargetLayer ?? "the current layer") + "; the survey linework is not changed. Your note (recorded with your name):");
            if (note == null) return;
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var db = doc.Database;
            string handle = null, problem = null;
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var cfg = FtfSession.Rules(db);
                var register = DrawingStore.ReadReview(db, tr);
                if (LineCorrections.Approve(db, tr, cfg, register, i.Id, note, LineworkCommands.Reviewer(_settings != null ? _settings.Review : null), out handle, out problem))
                {
                    DrawingStore.WriteReview(db, tr, register);
                    tr.Commit();
                }
            }
            if (problem != null) MessageBox.Show(this, "Not approved: " + problem, "FieldToFinish Review", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Reload();
        }

        private void Zoom()
        {
            var i = Selected();
            if (i == null) return;
            if (!ReviewStore.ZoomTo(AcadApp.DocumentManager.MdiActiveDocument, i))
                MessageBox.Show(this, "This item has no location in the drawing.", "FieldToFinish Review");
        }

        private void Fix()
        {
            var i = Selected();
            if (i == null) return;
            if (string.IsNullOrEmpty(i.FixCommand))
            {
                MessageBox.Show(this, "No command corrects this one directly: " + i.SuggestedAction, "FieldToFinish Review");
                return;
            }
            ReviewStore.ZoomTo(AcadApp.DocumentManager.MdiActiveDocument, i);
            Run(i.FixCommand);
        }

        private void OpenItsSettings()
        {
            var i = Selected();
            if (i == null) return;
            if (string.IsNullOrEmpty(i.SettingsPage))
            {
                MessageBox.Show(this, "No setting governs this one: " + i.SuggestedAction, "FieldToFinish Review");
                return;
            }
            FtfCommand.ShowSettings(i.SettingsPage);
        }

        private void Recheck()
        {
            var i = Selected();
            if (i == null) return;
            var command = RecheckCommand(i);
            if (command == null)
            {
                MessageBox.Show(this, "Run " + (i.FixCommand ?? "the command") + " again to recheck this one.", "FieldToFinish Review");
                return;
            }
            Run(command);
        }

        /// <summary>The command whose check raised an item.</summary>
        internal static string RecheckCommand(ReviewItem i)
        {
            switch (i.Source)
            {
                case ReviewChecks.CodesSource: return "FTFREVIEWCHECK";
                case LineworkCommands.GateReviewSource: return "FTFGATES";
                case ReviewChecks.OrientationSource: return "FTFORIENT";
                case ReviewChecks.FenceHeightsSource:
                case ReviewChecks.LineLabelsSource: return "FTFLINELABELS";
                case ReviewStore.CommandsSource: return i.FixCommand;
                case ReviewKinds.PointsSource:
                case ReviewKinds.ContinuitySource: return "FTFREVIEWCHECK";
                default: return null;
            }
        }

        private void NotApplicable()
        {
            var all = SelectedAll();
            if (all.Count == 0) return;
            var what = all.Count == 1 ? "\"" + ReviewRegister.Describe(all[0]) + "\"" : all.Count.ToString(CultureInfo.InvariantCulture) + " selected items";
            var reason = Ask("Not applicable", "Why does " + what + " not apply? (recorded with your name; it reopens if the source changes)");
            if (reason == null) return;
            Change((r, by, now) =>
            {
                string problem;
                r.MarkNotApplicable(all.Select(x => x.Id), reason, by, now, out problem);
                if (problem != null) throw new InvalidOperationException(problem);
            });
        }

        private void Accept()
        {
            var all = SelectedAll();
            if (all.Count == 0) return;
            string note = null;
            if (all.Any(x => { var k = ReviewKinds.Get(x.Kind); return k != null && k.NeedsNote; }))
            {
                note = Ask("Accept", "Why is " + (all.Count == 1 ? "\"" + ReviewRegister.Describe(all[0]) + "\"" : "each selected item") +
                           " accepted as drawn? Point and linework items need a documented disposition.");
                if (note == null) return;
            }
            Change((r, by, now) =>
            {
                foreach (var x in all)
                {
                    string problem;
                    if (!r.Accept(x.Id, note, by, now, out problem)) throw new InvalidOperationException(problem);
                }
            });
        }

        private void Reopen()
        {
            var all = SelectedAll();
            if (all.Count == 0) return;
            Change((r, by, now) => { foreach (var x in all) r.Reopen(x.Id, by, now); });
        }

        /// <summary>Changes the list in the drawing (one undoable transaction) and shows it.</summary>
        private void Change(Action<ReviewRegister, string, DateTime> change)
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var db = doc.Database;
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var register = DrawingStore.ReadReview(db, tr);
                change(register, LineworkCommands.Reviewer(_settings != null ? _settings.Review : null), DateTime.Now);
                DrawingStore.WriteReview(db, tr, register);
                tr.Commit();
            }
            Reload();
        }

        private static void Run(string command)
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null || string.IsNullOrWhiteSpace(command)) return;
            doc.SendStringToExecute(command + " ", true, false, true);
        }

        private string Ask(string title, string prompt)
        {
            using (var f = new Form
            {
                Text = title,
                Size = new Size(520, 170),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                MinimizeBox = false,
                MaximizeBox = false
            })
            {
                var label = new Label { Text = prompt, Left = 12, Top = 12, Width = 480, Height = 34 };
                var box = new TextBox { Left = 12, Top = 50, Width = 480 };
                var ok = new Button { Text = "OK", Left = 316, Top = 86, Width = 84, DialogResult = DialogResult.OK };
                var cancel = new Button { Text = "Cancel", Left = 408, Top = 86, Width = 84, DialogResult = DialogResult.Cancel };
                f.Controls.AddRange(new Control[] { label, box, ok, cancel });
                f.AcceptButton = ok;
                f.CancelButton = cancel;
                while (true)
                {
                    if (f.ShowDialog(this) != DialogResult.OK) return null;
                    if (!string.IsNullOrWhiteSpace(box.Text)) return box.Text.Trim();
                    MessageBox.Show(this, "Give a reason; it is what the next reviewer reads.", title);
                }
            }
        }
    }
}
