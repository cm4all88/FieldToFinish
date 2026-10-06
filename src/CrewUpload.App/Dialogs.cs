using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace CrewUpload.App
{
    /// <summary>
    /// The PM's window: register a project for crew uploads by picking its base Survey folder, change
    /// where it points, or close it (inactive) when the project finishes. Picks on a mapped drive are
    /// stored by their UNC path. Nothing on the share is created or moved from here.
    ///
    /// Every change is checked against the registry as this screen loaded it: another PM's change to
    /// a different project is kept, a change to the same project stops this one and refreshes.
    /// </summary>
    internal sealed class ProjectSetupForm : Form
    {
        private readonly JobFolderConfig _config;
        private readonly ProjectRegistry _registry;
        private readonly TextBox _number;
        private readonly Label _current;
        private readonly TextBox _chosen;
        private readonly Label _uploads;
        private readonly Label _notice;
        private readonly Button _save;
        private readonly Button _active;
        private readonly ListView _list;
        private readonly CheckBox _showInactive;
        private RegistrySnapshot _snapshot;

        public bool Changed { get; private set; }

        public ProjectSetupForm(JobFolderConfig config, ProjectRegistry registry, string number)
        {
            _config = config;
            _registry = registry;
            Text = "Project setup (PM)";
            Font = Theme.Body(10f);
            ForeColor = Theme.Charcoal;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            ShowInTaskbar = false;
            BackColor = Color.White;
            ClientSize = new Size(940, 600);

            var stack = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3, Padding = new Padding(16, 14, 16, 6) };
            stack.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            stack.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var intro = new Label
            {
                AutoSize = true, MaximumSize = new Size(900, 0), Margin = new Padding(0, 0, 0, 10),
                Text = "Register a project once its folders exist: pick its base Survey folder. Crew uploads then go to "
                    + config.UnprocessedFolder + " under it, and nowhere else. Finished projects are made inactive, never deleted.",
            };
            stack.Controls.Add(intro, 0, 0);
            stack.SetColumnSpan(intro, 3);

            stack.Controls.Add(MainForm.Caption("Client-task"), 0, 1);
            _number = new TextBox { Width = 160, CharacterCasing = CharacterCasing.Upper, Text = JobFolderConfig.NormalizeProjectNumber(number) };
            _number.TextChanged += (s, e) => { _chosen.Text = string.Empty; ShowState(); };
            stack.Controls.Add(_number, 1, 1);

            stack.Controls.Add(MainForm.Caption("Registered to"), 0, 2);
            _current = new Label { AutoSize = true, MaximumSize = new Size(740, 0), Margin = new Padding(3, 8, 3, 3) };
            stack.Controls.Add(_current, 1, 2);
            stack.SetColumnSpan(_current, 2);

            stack.Controls.Add(MainForm.Caption("Survey folder"), 0, 3);
            _chosen = new TextBox { Dock = DockStyle.Fill, ReadOnly = true, BackColor = Theme.LightGray4 };
            stack.Controls.Add(_chosen, 1, 3);
            var browse = Theme.Button("Choose folder...", false);
            browse.Click += (s, e) => Choose();
            stack.Controls.Add(browse, 2, 3);

            stack.Controls.Add(MainForm.Caption("Uploads will go to"), 0, 4);
            _uploads = new Label { AutoSize = true, MaximumSize = new Size(740, 0), ForeColor = Theme.MediumGray, Margin = new Padding(3, 8, 3, 3) };
            stack.Controls.Add(_uploads, 1, 4);
            stack.SetColumnSpan(_uploads, 2);

            _notice = new Label { AutoSize = true, MaximumSize = new Size(900, 0), Margin = new Padding(0, 6, 0, 0) };
            stack.Controls.Add(_notice, 0, 5);
            stack.SetColumnSpan(_notice, 3);

            _save = Theme.Button("Register project", true);
            _save.Click += (s, e) => Save();
            _active = Theme.Button("Mark inactive", false);
            _active.Click += (s, e) => ToggleActive();
            var refresh = Theme.Button("Refresh", false);
            refresh.Click += (s, e) => Reload(null);
            var close = Theme.Button("Close", false);
            close.DialogResult = DialogResult.Cancel;
            var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 8) };
            buttons.Controls.Add(_save);
            buttons.Controls.Add(_active);
            buttons.Controls.Add(refresh);
            buttons.Controls.Add(close);
            stack.Controls.Add(buttons, 0, 6);
            stack.SetColumnSpan(buttons, 3);
            CancelButton = close;

            _list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, BorderStyle = BorderStyle.FixedSingle };
            _list.Columns.Add("Project", 90);
            _list.Columns.Add("Status", 70);
            _list.Columns.Add("Survey folder", 520);
            _list.Columns.Add("By", 80);
            _list.Columns.Add("On", 130);
            _list.SelectedIndexChanged += (s, e) => { if (_list.SelectedItems.Count == 1) _number.Text = _list.SelectedItems[0].Text; };
            var listWrap = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 0, 16, 16) };
            listWrap.Controls.Add(_list);
            var listHead = new Panel { Dock = DockStyle.Top, Height = 30, Padding = new Padding(16, 4, 16, 0) };
            listHead.Controls.Add(new Label { Dock = DockStyle.Left, AutoSize = true, Font = Theme.Body(10f, FontStyle.Bold), Text = "Registered projects" });
            _showInactive = new CheckBox { Dock = DockStyle.Right, AutoSize = true, Text = "Show inactive" };
            _showInactive.CheckedChanged += (s, e) => Fill();
            listHead.Controls.Add(_showInactive);

            Controls.Add(listWrap);
            Controls.Add(listHead);
            Controls.Add(stack);
            Reload(null);
        }

        private string Key => ProjectRegistry.KeyFor(_number.Text);

        /// <summary>Re-reads the registry; everything this screen does from here is checked against it.</summary>
        private void Reload(string message)
        {
            try
            {
                _snapshot = _registry.Load();
                Say(message ?? (_snapshot.ReadFromBackup != null
                    ? "The live project list could not be read; showing " + Path.GetFileName(_snapshot.ReadFromBackup) + ". Saving is blocked until it is restored."
                    : string.Empty), _snapshot.ReadFromBackup != null || message != null);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is InvalidDataException)
            {
                _snapshot = new RegistrySnapshot();
                Say("Cannot read the project list " + _registry.FilePath + ": " + e.Message, true);
            }
            Fill();
            ShowState();
        }

        private void Say(string text, bool warn)
        {
            _notice.Text = text;
            _notice.ForeColor = warn ? Theme.Red : Theme.MediumGray;
        }

        private void Fill()
        {
            _list.Items.Clear();
            foreach (var r in _snapshot.Projects.Where(p => p.Active || _showInactive.Checked).OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                var row = new ListViewItem(r.Key);
                row.SubItems.Add(r.Active ? "Active" : "Inactive");
                row.SubItems.Add(r.SurveyFolder);
                row.SubItems.Add(r.RegisteredBy);
                row.SubItems.Add(r.RegisteredOn.ToString("yyyy-MM-dd HH:mm"));
                if (!r.Active) row.ForeColor = Theme.MediumGray;
                else if (!Directory.Exists(r.SurveyFolder)) row.ForeColor = Theme.Red;
                _list.Items.Add(row);
            }
        }

        private void ShowState()
        {
            var reg = Key == null ? null : _snapshot.Get(Key);
            _current.ForeColor = Theme.Charcoal;
            _current.Text = Key == null ? "Type a client-task number like 1800-119."
                : reg == null ? "Not registered yet -- crews cannot upload to " + Key + "."
                : reg.SurveyFolder + "\r\n" + (reg.Active ? "Active" : "INACTIVE -- hidden from crews") + ", set by " + reg.RegisteredBy + " on " + reg.RegisteredOn.ToString("yyyy-MM-dd")
                    + (reg.History.Count > 1 ? "  (" + reg.History.Count + " changes in its history)" : string.Empty);
            if (reg != null && reg.Active && !Directory.Exists(reg.SurveyFolder))
            {
                _current.Text += "\r\nThis folder cannot be found now. Choose its new location.";
                _current.ForeColor = Theme.Red;
            }
            var path = _chosen.Text.Length > 0 ? _chosen.Text : reg?.SurveyFolder;
            _uploads.Text = path == null ? string.Empty : Path.Combine(path, _config.UnprocessedFolder) + "\\<crew download folder>";
            var blocked = _snapshot.ReadFromBackup != null;
            _save.Text = reg == null ? "Register project" : "Change location";
            _save.Enabled = !blocked && Key != null && _chosen.Text.Length > 0
                && (reg == null || !string.Equals(reg.SurveyFolder, _chosen.Text, StringComparison.OrdinalIgnoreCase));
            _active.Visible = reg != null;
            _active.Enabled = !blocked;
            _active.Text = reg != null && !reg.Active ? "Reactivate" : "Mark inactive";
        }

        private void Choose()
        {
            if (Key == null) return;
            var reg = _snapshot.Get(Key);
            using (var dialog = new FolderBrowserDialog { Description = "Choose the base Survey folder for " + Key + " (for example ...\\99Svcs\\Survey)", ShowNewFolderButton = false })
            {
                var start = reg != null && Directory.Exists(reg.SurveyFolder) ? reg.SurveyFolder : _config.JobsRoot;
                if (!string.IsNullOrEmpty(start) && Directory.Exists(start)) dialog.SelectedPath = start;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                var path = Unc.FromMapped(dialog.SelectedPath).TrimEnd('\\');
                if (_config.RequireUncPaths && !ProjectRegistry.IsUnc(path))
                {
                    MessageBox.Show(this, path + " is on a local or unmapped drive, not the network share. Choose the folder on \\\\parametrix.com\\...",
                        Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                var warnings = new List<string>();
                if (!string.Equals(Path.GetFileName(path), "Survey", StringComparison.OrdinalIgnoreCase))
                    warnings.Add("The folder is called \"" + Path.GetFileName(path) + "\", not \"Survey\".");
                if (!AncestorLooksLike(path, Key))
                    warnings.Add("No folder above it is named for " + Key + ".");
                if (warnings.Count > 0 && MessageBox.Show(this, string.Join("\n", warnings) + "\n\nUse " + path + " anyway?", Text,
                        MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                    return;
                _chosen.Text = path;
                ShowState();
            }
        }

        private static bool AncestorLooksLike(string path, string number)
        {
            for (var dir = path; !string.IsNullOrEmpty(dir); dir = Path.GetDirectoryName(dir))
                if (ProjectStore.LooksLike(Path.GetFileName(dir), number)) return true;
            return false;
        }

        private void Save()
        {
            var reg = _snapshot.Get(Key);
            if (reg != null && MessageBox.Show(this, "Change " + Key + " from\n" + reg.SurveyFolder + "\nto\n" + _chosen.Text
                    + "?\n\nNew uploads go to the new location. Nothing already uploaded is moved.", Text,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            var path = _chosen.Text;
            Run(() => _registry.Register(Key, path, Environment.UserName, _snapshot), (reg == null ? "Registered " : "Moved ") + Key + ".");
        }

        private void ToggleActive()
        {
            var reg = _snapshot.Get(Key);
            if (reg == null) return;
            var makeActive = !reg.Active;
            if (!makeActive && MessageBox.Show(this, "Close " + Key + " for crew uploads?\n\nIt disappears from the crews' list. Its registration and history are kept, and it can be reactivated.",
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            Run(() => _registry.SetActive(Key, makeActive, Environment.UserName, _snapshot), Key + (makeActive ? " reactivated." : " made inactive."));
        }

        private void Run(Func<RegistrySaveResult> save, string done)
        {
            try
            {
                var result = save();
                _snapshot = result.Snapshot;
                Changed = true;
                _chosen.Text = string.Empty;
                Fill();
                ShowState();
                Say(done + (result.MergedOtherChanges ? " Changes other PMs saved meanwhile were kept." : string.Empty), false);
            }
            catch (RegistryConflictException e)
            {
                MessageBox.Show(this, e.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _chosen.Text = string.Empty;
                Reload("Refreshed: " + Key + " was changed by another PM. Check it, then make your change again.");
            }
            catch (UnauthorizedAccessException)
            {
                MessageBox.Show(this, "You do not have permission to change the project list in\n" + Path.GetDirectoryName(_registry.FilePath)
                    + "\n\nPMs need Modify on that folder; ask IT.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception e) when (e is IOException || e is ArgumentException || e is InvalidDataException || e is InvalidOperationException)
            {
                MessageBox.Show(this, e.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }

    /// <summary>Field notes typed straight in, for crews without a scanner.</summary>
    internal sealed class NotesForm : Form
    {
        private readonly TextBox _text;

        public string Notes => _text.Text;

        public NotesForm(string project, string crew, DateTime date)
        {
            Text = "Field notes -- " + project;
            Font = Theme.Body(10f);
            ForeColor = Theme.Charcoal;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = false;
            Size = new Size(700, 520);
            BackColor = Color.White;

            var head = new Label
            {
                Dock = DockStyle.Top, Height = 34, Padding = new Padding(12, 8, 12, 0), ForeColor = MainForm.Muted,
                Text = date.ToString("yyyy-MM-dd") + "   crew " + (string.IsNullOrWhiteSpace(crew) ? "(no initials)" : crew.ToUpperInvariant()) + "   -- saved in the field notes folder, named for the project",
            };
            _text = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, AcceptsReturn = true, AcceptsTab = true, Font = Theme.Body(11f) };
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Padding = new Padding(8) };
            var ok = Theme.Button("Save notes", true);
            ok.Click += (s, e) => { if (_text.Text.Trim().Length > 0) DialogResult = DialogResult.OK; };
            var cancel = Theme.Button("Cancel", false);
            cancel.DialogResult = DialogResult.Cancel;
            buttons.Controls.Add(ok);
            buttons.Controls.Add(cancel);
            CancelButton = cancel;

            var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 4, 12, 4) };
            body.Controls.Add(_text);
            Controls.Add(body);
            Controls.Add(head);
            Controls.Add(buttons);
        }
    }
}
