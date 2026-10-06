using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace CrewUpload.App
{
    /// <summary>
    /// The PM's window: register a project for crew uploads by picking its base Survey folder, or
    /// change where it points. Picks on a mapped drive are stored by their UNC path. Nothing on the
    /// share is created or moved from here.
    /// </summary>
    internal sealed class ProjectSetupForm : Form
    {
        private readonly JobFolderConfig _config;
        private readonly ProjectStore _store;
        private readonly TextBox _number;
        private readonly Label _current;
        private readonly TextBox _chosen;
        private readonly Label _uploads;
        private readonly Button _save;
        private readonly ListView _list;

        public string Registered { get; private set; }

        public ProjectSetupForm(JobFolderConfig config, ProjectStore store, string number)
        {
            _config = config;
            _store = store;
            Text = "Project setup (PM)";
            Font = Theme.Body(10f);
            ForeColor = Theme.Charcoal;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            ShowInTaskbar = false;
            BackColor = Color.White;
            ClientSize = new Size(900, 560);

            var stack = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3, Padding = new Padding(16, 14, 16, 6) };
            stack.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            stack.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var intro = new Label
            {
                AutoSize = true, MaximumSize = new Size(860, 0), Margin = new Padding(0, 0, 0, 10),
                Text = "Register a project once its folders exist: pick its base Survey folder. Crew uploads then go to "
                    + config.UnprocessedFolder + " under it, and nowhere else.",
            };
            stack.Controls.Add(intro, 0, 0);
            stack.SetColumnSpan(intro, 3);

            stack.Controls.Add(MainForm.Caption("Client-task"), 0, 1);
            _number = new TextBox { Width = 160, CharacterCasing = CharacterCasing.Upper, Text = JobFolderConfig.NormalizeProjectNumber(number) };
            _number.TextChanged += (s, e) => { _chosen.Text = string.Empty; ShowState(); };
            stack.Controls.Add(_number, 1, 1);

            stack.Controls.Add(MainForm.Caption("Registered to"), 0, 2);
            _current = new Label { AutoSize = true, MaximumSize = new Size(700, 0), Margin = new Padding(3, 8, 3, 3) };
            stack.Controls.Add(_current, 1, 2);
            stack.SetColumnSpan(_current, 2);

            stack.Controls.Add(MainForm.Caption("Survey folder"), 0, 3);
            _chosen = new TextBox { Dock = DockStyle.Fill, ReadOnly = true, BackColor = Theme.LightGray4 };
            stack.Controls.Add(_chosen, 1, 3);
            var browse = Theme.Button("Choose folder...", false);
            browse.Click += (s, e) => Choose();
            stack.Controls.Add(browse, 2, 3);

            stack.Controls.Add(MainForm.Caption("Uploads will go to"), 0, 4);
            _uploads = new Label { AutoSize = true, MaximumSize = new Size(700, 0), ForeColor = Theme.MediumGray, Margin = new Padding(3, 8, 3, 3) };
            stack.Controls.Add(_uploads, 1, 4);
            stack.SetColumnSpan(_uploads, 2);

            _save = Theme.Button("Register project", true);
            _save.Click += (s, e) => Save();
            var close = Theme.Button("Close", false);
            close.DialogResult = DialogResult.Cancel;
            var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 8) };
            buttons.Controls.Add(_save);
            buttons.Controls.Add(close);
            stack.Controls.Add(buttons, 0, 5);
            stack.SetColumnSpan(buttons, 3);
            CancelButton = close;

            _list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, BorderStyle = BorderStyle.FixedSingle };
            _list.Columns.Add("Project", 90);
            _list.Columns.Add("Survey folder", 560);
            _list.Columns.Add("By", 80);
            _list.Columns.Add("On", 130);
            _list.SelectedIndexChanged += (s, e) => { if (_list.SelectedItems.Count == 1) _number.Text = _list.SelectedItems[0].Text; };
            var listWrap = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 0, 16, 16) };
            listWrap.Controls.Add(_list);
            var listHead = new Label { Dock = DockStyle.Top, Height = 26, Padding = new Padding(16, 6, 0, 0), Font = Theme.Body(10f, FontStyle.Bold), Text = "Registered projects" };

            Controls.Add(listWrap);
            Controls.Add(listHead);
            Controls.Add(stack);
            Fill();
            ShowState();
        }

        private string Number => JobFolderConfig.NormalizeProjectNumber(_number.Text);

        private void Fill()
        {
            _list.Items.Clear();
            try
            {
                foreach (var r in _store.Registry.All())
                {
                    var row = new ListViewItem(r.ProjectNumber);
                    row.SubItems.Add(r.SurveyFolder);
                    row.SubItems.Add(r.RegisteredBy);
                    row.SubItems.Add(r.RegisteredOn.ToString("yyyy-MM-dd HH:mm"));
                    if (!Directory.Exists(r.SurveyFolder)) row.ForeColor = Theme.Red;
                    _list.Items.Add(row);
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                _current.Text = "Cannot read the project list " + _store.Registry.FilePath + ": " + e.Message;
            }
        }

        private void ShowState()
        {
            var reg = _config.IsValidProjectNumber(Number) ? _store.Registry.Find(Number) : null;
            _current.ForeColor = Theme.Charcoal;
            _current.Text = !_config.IsValidProjectNumber(Number) ? "Type a client-task number like 1800-119."
                : reg == null ? "Not registered yet -- crews cannot upload to " + Number + "."
                : reg.SurveyFolder + "\r\nby " + reg.RegisteredBy + ", " + reg.RegisteredOn.ToString("yyyy-MM-dd")
                    + (reg.History.Count > 0 ? "  (moved " + reg.History.Count + " time(s) before)" : string.Empty);
            if (reg != null && !Directory.Exists(reg.SurveyFolder))
            {
                _current.Text += "\r\nThis folder cannot be found now. Choose its new location.";
                _current.ForeColor = Theme.Red;
            }
            var path = _chosen.Text.Length > 0 ? _chosen.Text : reg?.SurveyFolder;
            _uploads.Text = path == null ? string.Empty : Path.Combine(path, _config.UnprocessedFolder) + "\\<crew download folder>";
            _save.Text = reg == null ? "Register project" : "Change location";
            _save.Enabled = _config.IsValidProjectNumber(Number) && _chosen.Text.Length > 0
                && (reg == null || !string.Equals(reg.SurveyFolder, _chosen.Text, StringComparison.OrdinalIgnoreCase));
        }

        private void Choose()
        {
            var reg = _config.IsValidProjectNumber(Number) ? _store.Registry.Find(Number) : null;
            using (var dialog = new FolderBrowserDialog { Description = "Choose the base Survey folder for " + Number + " (for example ...\\99Svcs\\Survey)", ShowNewFolderButton = false })
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
                if (!AncestorLooksLike(path, Number))
                    warnings.Add("No folder above it is named for " + Number + ".");
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
            var reg = _store.Registry.Find(Number);
            if (reg != null && MessageBox.Show(this, "Change " + Number + " from\n" + reg.SurveyFolder + "\nto\n" + _chosen.Text
                    + "?\n\nNew uploads go to the new location. Nothing already uploaded is moved.", Text,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            try
            {
                _store.Registry.Register(Number, _chosen.Text, Environment.UserName);
                Registered = Number;
                _chosen.Text = string.Empty;
                Fill();
                ShowState();
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException)
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
