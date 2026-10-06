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
    /// Pick the project, drop what the crew brought back onto the box for its type, check the
    /// names, upload. Every file is shown with its type, new name and destination before anything
    /// is copied, and the type can still be changed per file.
    /// </summary>
    internal sealed class MainForm : Form
    {
        internal static Color Muted => Theme.Muted;
        // Red for problems only; everything that went right is plain charcoal.
        private static Color Bad => Theme.Red;
        private static Color Good => Theme.Charcoal;

        private readonly JobFolderConfig _config;
        private readonly ProjectStore _projects;
        private readonly UploadPlanner _planner;
        private readonly Remembered _remembered = Remembered.Load();
        private readonly List<UploadItem> _items = new List<UploadItem>();

        private readonly ComboBox _number;
        private readonly Label _projectLabel;
        private readonly TextBox _crew;
        private readonly DateTimePicker _date;
        private readonly List<DropBox> _boxes = new List<DropBox>();
        private readonly Label _dropHelp;
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

            Text = (Theme.CompanyName.Length > 0 ? Theme.CompanyName + " " : string.Empty) + Theme.AppTitle;
            Font = Theme.Body(10f);
            ForeColor = Theme.Charcoal;
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(900, 600);
            Size = new Size(1180, 860);
            BackColor = Color.White;

            // ---- project and crew
            var top = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 6, Padding = new Padding(14, 12, 14, 4) };
            for (var i = 0; i < 6; i++) top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.ColumnStyles[5] = new ColumnStyle(SizeType.Percent, 100);

            top.Controls.Add(Caption("Project number"), 0, 0);
            _number = new ComboBox { Width = 190, Font = Theme.Body(12f), DropDownStyle = ComboBoxStyle.DropDown };
            _number.Items.AddRange(_remembered.RecentProjects.Cast<object>().ToArray());
            _number.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; FindProject(); } };
            _number.SelectionChangeCommitted += (s, e) => BeginInvoke((Action)FindProject);
            top.Controls.Add(_number, 1, 0);
            var find = Theme.Button("Find", false);
            find.Click += (s, e) => FindProject();
            top.Controls.Add(find, 2, 0);
            var create = Theme.Button("New project (PM)...", false);
            create.Click += (s, e) => NewProject();
            top.Controls.Add(create, 3, 0);
            _openFolder = Theme.Button("Open job folder", false);
            _openFolder.Enabled = false;
            _openFolder.Click += (s, e) => { if (_project != null) Process.Start("explorer.exe", "\"" + _project.Path + "\""); };
            top.Controls.Add(_openFolder, 4, 0);

            _projectLabel = new Label { AutoSize = true, Font = Theme.Body(12f, FontStyle.Bold), ForeColor = Muted, Margin = new Padding(3, 4, 3, 8), Text = "Type the project number and press Enter." };
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

            // ---- one drop box per type: the box decides the type, nothing is guessed
            var boxes = new TableLayoutPanel { Dock = DockStyle.Top, Height = 128, ColumnCount = _config.Categories.Count, RowCount = 1, Padding = new Padding(9, 0, 9, 0) };
            foreach (var category in _config.Categories)
            {
                boxes.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / _config.Categories.Count));
                var box = new DropBox(category);
                box.FilesDropped += (b, paths) => { if (!_busy) AddPaths(paths, b.Category); };
                box.Browse += b => Browse(b.Category);
                _boxes.Add(box);
                boxes.Controls.Add(box);
            }
            _dropHelp = new Label
            {
                Dock = DockStyle.Top, Height = 30, Padding = new Padding(16, 8, 16, 0), ForeColor = Theme.Muted,
                Text = "Drag each kind of file onto its box -- a whole folder works too. Or click a box to choose files.",
            };
            var dropWrap = new Panel { Dock = DockStyle.Top, Height = 168, Padding = new Padding(0, 0, 0, 10) };
            dropWrap.Controls.Add(boxes);
            dropWrap.Controls.Add(_dropHelp);

            // ---- the plan
            _grid = new DataGridView
            {
                Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToResizeRows = false, RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = Color.White, BorderStyle = BorderStyle.None, EditMode = DataGridViewEditMode.EditOnEnter,
            };
            _grid.EnableHeadersVisualStyles = false;
            _grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
            _grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Theme.LightGray3, ForeColor = Theme.Charcoal, Font = Theme.Body(10f, FontStyle.Bold),
                SelectionBackColor = Theme.LightGray3, SelectionForeColor = Theme.Charcoal, Padding = new Padding(4, 4, 4, 4),
            };
            _grid.DefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.White, ForeColor = Theme.Charcoal, Font = Theme.Body(10f),
                SelectionBackColor = Theme.LightGray2, SelectionForeColor = Theme.Charcoal, Padding = new Padding(4, 0, 4, 0),
            };
            _grid.GridColor = Theme.LightGray2;
            _grid.RowTemplate.Height = 28;
            _grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "file", HeaderText = "Dropped file", ReadOnly = true, FillWeight = 26 });
            _typeColumn = new DataGridViewComboBoxColumn
            {
                Name = "type", HeaderText = "Type", FillWeight = 16, FlatStyle = FlatStyle.Flat,
                DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton,
                DefaultCellStyle = new DataGridViewCellStyle { Padding = new Padding(10, 0, 2, 0) },
            };
            _typeColumn.Items.AddRange(_config.Categories.Select(c => (object)c.Name).ToArray());
            _grid.Columns.Add(_typeColumn);
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "name", HeaderText = "New name", ReadOnly = true, FillWeight = 30 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "folder", HeaderText = "Goes to (in the job)", ReadOnly = true, FillWeight = 22 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "status", HeaderText = "", ReadOnly = true, FillWeight = 12 });
            _grid.CurrentCellDirtyStateChanged += (s, e) => { if (_grid.IsCurrentCellDirty) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            _grid.CellValueChanged += TypeChanged;
            _grid.DataError += (s, e) => e.ThrowException = false;
            _grid.CellPainting += PaintTypeStripe;
            _grid.KeyDown += (s, e) => { if (e.KeyCode == Keys.Delete && !_busy) RemoveSelected(); };
            var gridWrap = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 0, 14, 0) };
            gridWrap.Controls.Add(_grid);

            // ---- actions
            // The ix formation sits in the bottom-left corner, bleeding off the left and bottom
            // edges as the guide's letterhead and business card show it.
            var ixWidth = Theme.Ix == null ? 0 : 64;
            var bottom = new TableLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, ColumnCount = 2, Padding = new Padding(14 + ixWidth, 10, 16, 14), BackColor = Color.White };
            bottom.Paint += (s, e) =>
            {
                if (Theme.Ix == null) return;
                var h = bottom.Height - 8f;
                var w = Theme.Ix.Width * h / Theme.Ix.Height;
                e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                e.Graphics.DrawImage(Theme.Ix, 0, bottom.Height - h, w, h);
            };
            bottom.Resize += (s, e) => bottom.Invalidate();
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _documents = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
            foreach (var doc in _config.Documents)
            {
                var d = doc;
                var b = Theme.Button(d.Name, false);
                b.Click += (s, e) => NewDocument(d);
                _documents.Controls.Add(b);
            }
            var typed = Theme.Button("Type field notes...", false);
            typed.Click += (s, e) => TypeNotes();
            _documents.Controls.Add(typed);
            _documents.Enabled = false;
            bottom.Controls.Add(_documents, 0, 0);

            var right = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
            var clear = Theme.Button("Clear list", false);
            clear.Click += (s, e) => { if (!_busy) { _items.Clear(); Refill(); } };
            right.Controls.Add(clear);
            _upload = Theme.Button("Upload to job", true);
            _upload.Click += async (s, e) => await Upload();
            right.Controls.Add(_upload);
            bottom.Controls.Add(right, 1, 0);

            _status = new Label { Dock = DockStyle.Bottom, Height = 30, Padding = new Padding(16, 6, 16, 4), ForeColor = Muted, BackColor = Theme.LightGray4 };

            Controls.Add(gridWrap);
            Controls.Add(dropWrap);
            Controls.Add(top);
            Controls.Add(Theme.Header());
            Controls.Add(_status);
            Controls.Add(bottom);

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

        private void Browse(UploadCategory category)
        {
            if (_busy) return;
            using (var dialog = new OpenFileDialog { Multiselect = true, Title = "Choose " + category.Name.ToLowerInvariant() + " to upload" })
                if (dialog.ShowDialog(this) == DialogResult.OK) AddPaths(dialog.FileNames, category);
        }

        /// <summary>
        /// Adds files to the list. With a category (dropped on its box) they are that type; a file
        /// already in the list that is dropped on another box changes to that type. Without one
        /// (the command line) the type is guessed from names and extensions.
        /// </summary>
        private void AddPaths(IEnumerable<string> paths, UploadCategory category = null)
        {
            List<UploadItem> found;
            try
            {
                found = _planner.Collect(paths, category);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Say("Could not read what was dropped: " + ex.Message, Bad);
                return;
            }
            var known = _items.ToDictionary(i => i.SourcePath, StringComparer.OrdinalIgnoreCase);
            var added = 0;
            foreach (var item in found)
            {
                UploadItem existing;
                if (known.TryGetValue(item.SourcePath, out existing))
                {
                    if (category != null) existing.Category = category;
                    continue;
                }
                known[item.SourcePath] = item;
                _items.Add(item);
                added++;
            }
            Replan();
            var what = category == null ? string.Empty : " as " + category.Name.ToLowerInvariant();
            Say(added + " file" + (added == 1 ? "" : "s") + " added" + what + "." + (_project == null ? " Pick the project to see their names." : " Check the names, then Upload."), Muted);
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
            foreach (var box in _boxes) box.SetCount(_items.Count(i => i.Category == box.Category && !i.Skip));
            _dropHelp.Text = _items.Count == 0
                ? "Drag each kind of file onto its box -- a whole folder works too. Or click a box to choose files."
                : "Check the names below. Drop on another box, or change the Type, to move a file. Delete removes it from the list.";
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

        /// <summary>A bar in the type's colour at the left of the Type cell, matching its drop box.</summary>
        private void PaintTypeStripe(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex != _typeColumn.Index) return;
            var item = _grid.Rows[e.RowIndex].Tag as UploadItem;
            if (item?.Category == null) return;
            e.Paint(e.ClipBounds, DataGridViewPaintParts.All);
            using (var b = new SolidBrush(Theme.Parse(item.Category.Color, Theme.MediumGray)))
                e.Graphics.FillRectangle(b, e.CellBounds.Left, e.CellBounds.Top + 3, 5, e.CellBounds.Height - 7);
            e.Handled = true;
        }

        // ------------------------------------------------------------------ helpers

        private void Say(string text, Color color)
        {
            _status.Text = text;
            _status.ForeColor = color;
        }

        internal static Label Caption(string text) =>
            new Label { Text = text, AutoSize = true, ForeColor = Theme.MediumGray, Font = Theme.Body(10f), Margin = new Padding(3, 8, 8, 3) };
    }
}
