using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CrewUpload.App
{
    /// <summary>
    /// Pick the project, drop what the crew brought back, check the names, upload. Every file
    /// is shown with its type, new name and destination before anything is copied, and the
    /// type can be changed per file.
    /// </summary>
    internal sealed class MainForm : Form
    {
        internal static readonly Color Accent = Color.FromArgb(0, 102, 153);
        internal static readonly Color Muted = Color.FromArgb(96, 96, 96);
        private static readonly Color DropIdle = Color.FromArgb(240, 245, 250);
        private static readonly Color DropHot = Color.FromArgb(214, 234, 248);
        private static readonly Color Bad = Color.FromArgb(176, 0, 32);
        private static readonly Color Good = Color.FromArgb(0, 120, 60);

        private readonly JobFolderConfig _config;
        private readonly ProjectStore _projects;
        private readonly UploadPlanner _planner;
        private readonly Remembered _remembered = Remembered.Load();
        private readonly List<UploadItem> _items = new List<UploadItem>();

        private readonly ComboBox _number;
        private readonly Label _projectLabel;
        private readonly TextBox _crew;
        private readonly DateTimePicker _date;
        private readonly Panel _drop;
        private readonly Label _dropText;
        private readonly DataGridView _grid;
        private readonly DataGridViewComboBoxColumn _typeColumn;
        private readonly Button _upload;
        private readonly Button _openFolder;
        private readonly FlowLayoutPanel _documents;
        private readonly Label _status;

        private ProjectFolder _project;
        private bool _busy;

        public MainForm(JobFolderConfig config, string projectNumber, IList<string> dropped)
        {
            _config = config;
            _projects = new ProjectStore(config);
            _planner = new UploadPlanner(config);

            Text = "Crew Upload";
            Font = new Font("Segoe UI", 10f);
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(900, 600);
            Size = new Size(1150, 760);
            BackColor = Color.White;
            AllowDrop = true;

            // ---- project and crew
            var top = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 6, Padding = new Padding(14, 12, 14, 4) };
            for (var i = 0; i < 6; i++) top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.ColumnStyles[5] = new ColumnStyle(SizeType.Percent, 100);

            top.Controls.Add(Caption("Project number"), 0, 0);
            _number = new ComboBox { Width = 190, Font = new Font("Segoe UI", 12f), DropDownStyle = ComboBoxStyle.DropDown };
            _number.Items.AddRange(_remembered.RecentProjects.Cast<object>().ToArray());
            _number.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; FindProject(); } };
            _number.SelectionChangeCommitted += (s, e) => BeginInvoke((Action)FindProject);
            top.Controls.Add(_number, 1, 0);
            var find = FlatButton("Find", false);
            find.Click += (s, e) => FindProject();
            top.Controls.Add(find, 2, 0);
            var create = FlatButton("New project (PM)...", false);
            create.Click += (s, e) => NewProject();
            top.Controls.Add(create, 3, 0);
            _openFolder = FlatButton("Open job folder", false);
            _openFolder.Enabled = false;
            _openFolder.Click += (s, e) => { if (_project != null) Process.Start("explorer.exe", "\"" + _project.Path + "\""); };
            top.Controls.Add(_openFolder, 4, 0);

            _projectLabel = new Label { AutoSize = true, Font = new Font("Segoe UI", 12f, FontStyle.Bold), ForeColor = Muted, Margin = new Padding(3, 4, 3, 8), Text = "Type the project number and press Enter." };
            top.Controls.Add(_projectLabel, 1, 1);
            top.SetColumnSpan(_projectLabel, 5);

            top.Controls.Add(Caption("Crew initials"), 0, 2);
            _crew = new TextBox { Width = 90, CharacterCasing = CharacterCasing.Upper, Text = _remembered.Crew ?? string.Empty };
            _crew.Leave += (s, e) => Replan();
            top.Controls.Add(_crew, 1, 2);
            top.Controls.Add(Caption("Field date"), 2, 2);
            _date = new DateTimePicker { Format = DateTimePickerFormat.Short, Width = 140, Value = DateTime.Today };
            _date.ValueChanged += (s, e) => Replan();
            top.Controls.Add(_date, 3, 2);
            var dateNote = new Label { AutoSize = true, ForeColor = Muted, Text = "Photos use the day they were taken.", Margin = new Padding(3, 6, 3, 3) };
            top.Controls.Add(dateNote, 4, 2);
            top.SetColumnSpan(dateNote, 2);

            // ---- drop zone
            _drop = new Panel { Dock = DockStyle.Top, Height = 110, BackColor = DropIdle, Margin = new Padding(14), Cursor = Cursors.Hand, AllowDrop = true };
            _dropText = new Label
            {
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 13f), ForeColor = Accent,
                Text = "Drag the crew's folders, field notes and photos here\r\nor click to choose files",
            };
            _drop.Controls.Add(_dropText);
            _drop.Paint += (s, e) =>
            {
                using (var pen = new Pen(Accent, 2) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash })
                    e.Graphics.DrawRectangle(pen, 1, 1, _drop.Width - 3, _drop.Height - 3);
            };
            _dropText.Click += (s, e) => Browse();
            var dropWrap = new Panel { Dock = DockStyle.Top, Height = 130, Padding = new Padding(14, 6, 14, 14) };
            dropWrap.Controls.Add(_drop);

            // ---- the plan
            _grid = new DataGridView
            {
                Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToResizeRows = false, RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = Color.White, BorderStyle = BorderStyle.None, AllowDrop = true, EditMode = DataGridViewEditMode.EditOnEnter,
            };
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "file", HeaderText = "Dropped file", ReadOnly = true, FillWeight = 26 });
            _typeColumn = new DataGridViewComboBoxColumn
            {
                Name = "type", HeaderText = "Type", FillWeight = 16, FlatStyle = FlatStyle.Flat,
                DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton,
            };
            _typeColumn.Items.AddRange(_config.Categories.Select(c => (object)c.Name).ToArray());
            _grid.Columns.Add(_typeColumn);
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "name", HeaderText = "New name", ReadOnly = true, FillWeight = 30 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "folder", HeaderText = "Goes to (in the job)", ReadOnly = true, FillWeight = 22 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "status", HeaderText = "", ReadOnly = true, FillWeight = 12 });
            _grid.CurrentCellDirtyStateChanged += (s, e) => { if (_grid.IsCurrentCellDirty) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            _grid.CellValueChanged += TypeChanged;
            _grid.DataError += (s, e) => e.ThrowException = false;
            _grid.KeyDown += (s, e) => { if (e.KeyCode == Keys.Delete && !_busy) RemoveSelected(); };
            var gridWrap = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 0, 14, 0) };
            gridWrap.Controls.Add(_grid);

            // ---- actions
            var bottom = new TableLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, ColumnCount = 2, Padding = new Padding(14, 8, 14, 12) };
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _documents = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
            foreach (var doc in _config.Documents)
            {
                var d = doc;
                var b = FlatButton(d.Name, false);
                b.Click += (s, e) => NewDocument(d);
                _documents.Controls.Add(b);
            }
            var typed = FlatButton("Type field notes...", false);
            typed.Click += (s, e) => TypeNotes();
            _documents.Controls.Add(typed);
            _documents.Enabled = false;
            bottom.Controls.Add(_documents, 0, 0);

            var right = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
            var clear = FlatButton("Clear list", false);
            clear.Click += (s, e) => { if (!_busy) { _items.Clear(); Refill(); } };
            right.Controls.Add(clear);
            _upload = FlatButton("Upload to job", true);
            _upload.Click += async (s, e) => await Upload();
            right.Controls.Add(_upload);
            bottom.Controls.Add(right, 1, 0);

            _status = new Label { Dock = DockStyle.Bottom, Height = 28, Padding = new Padding(16, 4, 16, 4), ForeColor = Muted, BackColor = Color.FromArgb(245, 245, 245) };

            Controls.Add(gridWrap);
            Controls.Add(dropWrap);
            Controls.Add(top);
            Controls.Add(bottom);
            Controls.Add(_status);

            foreach (var target in new Control[] { this, _drop, _dropText, _grid })
            {
                target.AllowDrop = true;
                target.DragEnter += OnDragEnter;
                target.DragLeave += (s, e) => _drop.BackColor = DropIdle;
                target.DragDrop += OnDragDrop;
            }

            FormClosing += (s, e) =>
            {
                if (_busy) { e.Cancel = true; return; }
                _remembered.Crew = _crew.Text.Trim();
                _remembered.Save();
            };

            Refill();
            if (!string.IsNullOrWhiteSpace(projectNumber))
            {
                _number.Text = projectNumber;
                Shown += (s, e) => FindProject();
            }
            else if (!Directory.Exists(_config.JobsRoot))
            {
                Say("The jobs folder " + _config.JobsRoot + " cannot be reached. Connect to the office network or VPN.", Bad);
            }
            if (dropped != null && dropped.Count > 0) Shown += (s, e) => AddPaths(dropped);
        }

        // ------------------------------------------------------------------ project

        private void FindProject()
        {
            var number = JobFolderConfig.NormalizeProjectNumber(_number.Text);
            _number.Text = number;
            if (number.Length == 0) return;
            if (!_config.IsValidProjectNumber(number))
            {
                SetProject(null, "'" + number + "' is not a project number.");
                return;
            }
            if (!Directory.Exists(_config.JobsRoot))
            {
                SetProject(null, "Cannot reach " + _config.JobsRoot + ". Connect to the office network or VPN.");
                return;
            }
            try
            {
                var found = _projects.Find(number);
                SetProject(found, found == null ? "No job folder for " + number + ". Check the number, or ask the PM to create the project." : null);
            }
            catch (InvalidOperationException e)
            {
                SetProject(null, e.Message);
            }
        }

        private void SetProject(ProjectFolder project, string problem)
        {
            _project = project;
            _openFolder.Enabled = _documents.Enabled = project != null;
            if (project != null)
            {
                _projectLabel.Text = project.Display + "     " + project.Path;
                _projectLabel.ForeColor = Good;
                _remembered.Used(project.Info.ProjectNumber);
                if (!_number.Items.Contains(project.Info.ProjectNumber)) _number.Items.Insert(0, project.Info.ProjectNumber);
                Say(project.HasInfoFile ? "Project found." : "Project found (an older folder: its name was read from the folder).", Good);
            }
            else
            {
                _projectLabel.Text = problem;
                _projectLabel.ForeColor = Bad;
            }
            Replan();
        }

        private void NewProject()
        {
            using (var form = new NewProjectForm(_config, _number.Text))
            {
                if (form.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    var created = _projects.Create(form.Info);
                    _number.Text = created.Info.ProjectNumber;
                    SetProject(created, null);
                    Say("Project " + created.Info.ProjectNumber + " created at " + created.Path, Good);
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is InvalidOperationException)
                {
                    MessageBox.Show(this, e.Message, "New project", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        // ------------------------------------------------------------------ dropping

        private void OnDragEnter(object sender, DragEventArgs e)
        {
            var ok = !_busy && e.Data.GetDataPresent(DataFormats.FileDrop);
            e.Effect = ok ? DragDropEffects.Copy : DragDropEffects.None;
            if (ok) _drop.BackColor = DropHot;
        }

        private void OnDragDrop(object sender, DragEventArgs e)
        {
            _drop.BackColor = DropIdle;
            if (_busy) return;
            var paths = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (paths != null) AddPaths(paths);
        }

        private void Browse()
        {
            if (_busy) return;
            using (var dialog = new OpenFileDialog { Multiselect = true, Title = "Choose field notes, photos and data" })
                if (dialog.ShowDialog(this) == DialogResult.OK) AddPaths(dialog.FileNames);
        }

        private void AddPaths(IEnumerable<string> paths)
        {
            List<UploadItem> found;
            try
            {
                found = _planner.Collect(paths);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Say("Could not read what was dropped: " + ex.Message, Bad);
                return;
            }
            var known = new HashSet<string>(_items.Select(i => i.SourcePath), StringComparer.OrdinalIgnoreCase);
            var added = found.Where(i => known.Add(i.SourcePath)).ToList();
            _items.AddRange(added);
            Replan();
            Say(added.Count + " file" + (added.Count == 1 ? "" : "s") + " added." + (_project == null ? " Pick the project to see their names." : " Check the types, then Upload."), Muted);
        }

        private void RemoveSelected()
        {
            foreach (DataGridViewRow row in _grid.SelectedRows)
                if (row.Tag is UploadItem item) _items.Remove(item);
            Replan();
        }

        private void TypeChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex != _typeColumn.Index) return;
            var item = _grid.Rows[e.RowIndex].Tag as UploadItem;
            var name = _grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value as string;
            var category = _config.Categories.FirstOrDefault(c => c.Name == name);
            if (item == null || category == null || item.Category == category) return;
            item.Category = category;
            BeginInvoke((Action)Replan);
        }

        // ------------------------------------------------------------------ planning

        private void Replan()
        {
            if (_busy) return;
            _items.RemoveAll(i => i.Done);
            if (_project != null && _items.Count > 0)
            {
                try
                {
                    _planner.Assign(_project, _items, _crew.Text, _date.Value.Date);
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    Say("Could not read the job folder: " + e.Message, Bad);
                }
            }
            Refill();
        }

        private void Refill()
        {
            _grid.CellValueChanged -= TypeChanged;
            _grid.Rows.Clear();
            foreach (var item in _items)
            {
                var i = _grid.Rows.Add();
                var row = _grid.Rows[i];
                row.Tag = item;
                row.Cells["file"].Value = item.DroppedAs;
                row.Cells["type"].Value = item.Category?.Name;
                if (_project == null)
                {
                    row.Cells["name"].Value = "(pick the project)";
                    row.Cells["name"].Style.ForeColor = Muted;
                }
                else if (item.Skip)
                {
                    row.Cells["name"].Value = Path.GetFileName(item.AlreadyUploadedAs);
                    row.Cells["folder"].Value = Relative(Path.GetDirectoryName(item.AlreadyUploadedAs));
                    row.Cells["status"].Value = "Already in job";
                    row.DefaultCellStyle.ForeColor = Muted;
                }
                else
                {
                    row.Cells["name"].Value = Path.GetFileName(item.Destination);
                    row.Cells["folder"].Value = Relative(Path.GetDirectoryName(item.Destination));
                }
                if (item.Error != null)
                {
                    row.Cells["status"].Value = "Failed";
                    row.Cells["status"].ToolTipText = item.Error;
                    row.Cells["status"].Style.ForeColor = Bad;
                }
            }
            _grid.CellValueChanged += TypeChanged;

            var toCopy = _items.Count(i => !i.Skip && !i.Done);
            _upload.Enabled = !_busy && _project != null && toCopy > 0;
            _upload.Text = toCopy > 0 ? "Upload " + toCopy + " file" + (toCopy == 1 ? "" : "s") + " to job" : "Upload to job";
            _dropText.Text = _items.Count == 0
                ? "Drag the crew's folders, field notes and photos here\r\nor click to choose files"
                : _items.Count + " file" + (_items.Count == 1 ? "" : "s") + " ready -- drop more here, or click to choose files\r\nChange a Type if it guessed wrong. Delete removes a file from the list.";
        }

        private string Relative(string folder)
        {
            if (_project == null || folder == null) return folder;
            return folder.StartsWith(_project.Path, StringComparison.OrdinalIgnoreCase)
                ? folder.Substring(_project.Path.Length).TrimStart('\\', '/')
                : folder;
        }

        // ------------------------------------------------------------------ upload

        private async Task Upload()
        {
            if (_busy || _project == null) return;
            if (string.IsNullOrWhiteSpace(_crew.Text))
            {
                MessageBox.Show(this, "Enter the crew's initials first, so the office knows who brought this in.", "Upload", MessageBoxButtons.OK, MessageBoxIcon.Information);
                _crew.Focus();
                return;
            }

            Replan();
            var project = _project;
            var crew = _crew.Text.Trim();
            var batch = _items.ToList();
            var total = batch.Count;
            var count = 0;
            _busy = true;
            _upload.Enabled = false;
            UseWaitCursor = true;
            int copied;
            try
            {
                var runner = new UploadRunner(_config);
                copied = await Task.Run(() => runner.Run(project, batch, crew, item =>
                    BeginInvoke((Action)(() => Say("Uploading " + (++count) + " of " + total + ": " + Path.GetFileName(item.SourcePath), Muted)))));
            }
            finally
            {
                _busy = false;
                UseWaitCursor = false;
            }

            var failed = batch.Where(i => i.Error != null).ToList();
            _remembered.Crew = crew;
            _remembered.Save();
            Replan();
            if (failed.Count == 0)
            {
                Say(copied + " file" + (copied == 1 ? "" : "s") + " uploaded to " + project.Display + ". The originals were left where they were.", Good);
            }
            else
            {
                Say(copied + " uploaded, " + failed.Count + " failed -- they are still in the list. Hover Failed for the reason.", Bad);
                MessageBox.Show(this, string.Join("\n", failed.Take(10).Select(f => Path.GetFileName(f.SourcePath) + ": " + f.Error)), "Some files did not upload", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // ------------------------------------------------------------------ documents

        private void NewDocument(DocumentTemplate document)
        {
            if (_project == null) return;
            try
            {
                var maker = new DocumentMaker(_config);
                if (maker.TemplatePath(document) == null && !string.IsNullOrWhiteSpace(document.Template))
                    Say("Template " + document.Template + " not found; started a blank text sheet instead.", Bad);
                var path = maker.Create(_project, document, _crew.Text, _date.Value.Date);
                if (maker.TemplatePath(document) != null) Say("Created " + Path.GetFileName(path) + " in " + Relative(Path.GetDirectoryName(path)), Good);
                Open(path);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is InvalidOperationException)
            {
                MessageBox.Show(this, e.Message, document.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void TypeNotes()
        {
            if (_project == null) return;
            var category = _config.Category("notes") ?? _config.FallbackCategory;
            using (var form = new NotesForm(_project.Display, _crew.Text, _date.Value.Date))
            {
                if (form.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    var path = new DocumentMaker(_config).CreateNotes(_project, category, _crew.Text, _date.Value.Date, form.Notes);
                    Say("Field notes saved as " + Path.GetFileName(path) + " in " + Relative(Path.GetDirectoryName(path)), Good);
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    MessageBox.Show(this, e.Message, "Field notes", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private void Open(string path)
        {
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception || e is InvalidOperationException)
            {
                Say("Created " + path + " but nothing on this PC opens that kind of file.", Bad);
            }
        }

        // ------------------------------------------------------------------ helpers

        private void Say(string text, Color color)
        {
            _status.Text = text;
            _status.ForeColor = color;
        }

        internal static Label Caption(string text) =>
            new Label { Text = text, AutoSize = true, ForeColor = Muted, Margin = new Padding(3, 8, 8, 3) };

        internal static Button FlatButton(string text, bool primary)
        {
            var b = new Button
            {
                Text = text, AutoSize = true, FlatStyle = FlatStyle.Flat, Padding = new Padding(10, 3, 10, 3), Margin = new Padding(3, 3, 6, 3),
                BackColor = primary ? Accent : Color.White, ForeColor = primary ? Color.White : Accent, Cursor = Cursors.Hand,
            };
            b.FlatAppearance.BorderColor = Accent;
            if (primary) b.Font = new Font("Segoe UI", 11f, FontStyle.Bold);
            return b;
        }
    }
}
