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
        private readonly ComboBox _work;
        private readonly Label _downloadLabel;
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
            _openFolder = Theme.Button("Open job folder", false);
            _openFolder.Enabled = false;
            _openFolder.Click += (s, e) => { if (_project != null) Process.Start("explorer.exe", "\"" + _project.Path + "\""); };
            top.Controls.Add(_openFolder, 3, 0);

            _projectLabel = new Label { AutoSize = true, Font = Theme.Body(12f, FontStyle.Bold), ForeColor = Muted, Margin = new Padding(3, 4, 3, 8), Text = "Type the project number and press Enter." };
            top.Controls.Add(_projectLabel, 1, 1);
            top.SetColumnSpan(_projectLabel, 5);

            top.Controls.Add(Caption("Crew initials"), 0, 2);
            _crew = new TextBox { Width = 90, CharacterCasing = CharacterCasing.Upper, Text = _remembered.Crew ?? string.Empty };
            _crew.TextChanged += (s, e) => Replan();
            top.Controls.Add(_crew, 1, 2);
            top.Controls.Add(Caption("Field date"), 2, 2);
            _date = new DateTimePicker { Format = DateTimePickerFormat.Short, Width = 140, Value = DateTime.Today };
            _date.ValueChanged += (s, e) => Replan();
            top.Controls.Add(_date, 3, 2);
            var workRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            workRow.Controls.Add(Caption("Work type"));
            _work = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 250 };
            _work.Items.AddRange(_config.WorkTypes.Cast<object>().ToArray());
            _work.SelectedIndexChanged += (s, e) => Replan();
            workRow.Controls.Add(_work);
            top.Controls.Add(workRow, 4, 2);
            top.SetColumnSpan(workRow, 2);

            top.Controls.Add(Caption("Download folder"), 0, 3);
            _downloadLabel = new Label { AutoSize = true, Font = Theme.Body(12f, FontStyle.Bold), Margin = new Padding(3, 7, 3, 6) };
            top.Controls.Add(_downloadLabel, 1, 3);
            top.SetColumnSpan(_downloadLabel, 5);

            // ---- one box per type: the box decides the type, nothing is guessed
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
                Text = HelpText,
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
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "folder", HeaderText = "Goes in", ReadOnly = true, FillWeight = 22 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "status", HeaderText = "", ReadOnly = true, FillWeight = 12 });
            _grid.CurrentCellDirtyStateChanged += (s, e) => { if (_grid.IsCurrentCellDirty) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            _grid.CellValueChanged += TypeChanged;
            _grid.DataError += (s, e) => e.ThrowException = false;
            _grid.CellPainting += PaintTypeStripe;
            _grid.KeyDown += (s, e) => { if (e.KeyCode == Keys.Delete && !_busy) RemoveSelected(); };
            var menu = new ContextMenuStrip();
            menu.Items.Add("Keep both (upload as -2)", null, (s, e) => SetChoice(ConflictChoice.KeepBoth));
            menu.Items.Add("Skip this file", null, (s, e) => SetChoice(ConflictChoice.Skip));
            menu.Items.Add("Undecided", null, (s, e) => SetChoice(ConflictChoice.Undecided));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Remove from list", null, (s, e) => { if (!_busy) RemoveSelected(); });
            _grid.ContextMenuStrip = menu;
            _grid.CellMouseDown += (s, e) =>
            {
                if (e.Button == MouseButtons.Right && e.RowIndex >= 0 && !_grid.Rows[e.RowIndex].Selected)
                {
                    _grid.ClearSelection();
                    _grid.Rows[e.RowIndex].Selected = true;
                }
            };
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

            Replan();
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

        private const string HelpText = "Drop each kind of file on its box -- a folder works too. A folder named like 20260128-JAM-1521-799-TOPO fills in the project, crew, date and work type.";

        /// <summary>What the form says this download is; null until crew and work type are filled in.</summary>
        private FieldVisit Visit
        {
            get
            {
                var work = _work.SelectedItem as WorkType;
                if (string.IsNullOrWhiteSpace(_crew.Text) || work == null) return null;
                return new FieldVisit { Crew = _crew.Text.Trim(), Date = _date.Value.Date, WorkType = work.Code };
            }
        }

        // ------------------------------------------------------------------ project

        /// <summary>
        /// Looks the client-task number up under the Clients share. Exactly one folder is used;
        /// zero or several go to the crew to choose. A project folder is never created.
        /// </summary>
        private void FindProject()
        {
            var number = JobFolderConfig.NormalizeProjectNumber(_number.Text);
            _number.Text = number;
            if (number.Length == 0) return;
            if (!_config.IsValidProjectNumber(number))
            {
                SetProject(null, "'" + number + "' is not a client-task number like 1800-119.");
                return;
            }
            if (!Directory.Exists(_config.JobsRoot))
            {
                SetProject(null, "Cannot reach " + _config.JobsRoot + ". Connect to the office network or VPN.");
                return;
            }

            var found = _projects.Resolve(number);
            if (found.Count == 1)
            {
                SetProject(found[0], null);
                return;
            }
            using (var pick = new PickProjectForm(number, found, _config.JobsRoot))
            {
                if (pick.ShowDialog(this) != DialogResult.OK)
                {
                    SetProject(null, found.Count == 0
                        ? "No project folder found for " + number + ". Nothing was created."
                        : found.Count + " folders match " + number + ". Pick one to upload.");
                    return;
                }
                try
                {
                    SetProject(_projects.Open(pick.ChosenPath, number), null);
                }
                catch (DirectoryNotFoundException e)
                {
                    SetProject(null, e.Message);
                }
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
                Say(Directory.Exists(_planner.DownloadsRoot(project))
                    ? "Project found."
                    : "Project found. Its " + _config.DownloadsFolder + " folder will be made on the first upload.", Good);
            }
            else
            {
                _projectLabel.Text = problem;
                _projectLabel.ForeColor = Bad;
            }
            Replan();
        }

        // ------------------------------------------------------------------ dropping

        /// <summary>
        /// A dropped download folder named 20260128-JAM-1521-799-TOPO fills in the project, crew,
        /// date and work type, so the crew types nothing.
        /// </summary>
        private void ReadDownloadName(IEnumerable<string> paths)
        {
            var parsed = _planner.FindDownload(paths);
            if (parsed == null) return;
            var v = parsed.Visit;
            if (!string.IsNullOrEmpty(v.Crew)) _crew.Text = v.Crew;
            if (v.Date != default(DateTime)) _date.Value = v.Date;
            if (!string.IsNullOrEmpty(v.WorkType))
            {
                var work = _config.WorkType(v.WorkType);
                if (work == null)
                {
                    work = new WorkType { Code = v.WorkType, Name = "(not in the office list)" };
                    _work.Items.Add(work);
                }
                _work.SelectedItem = work;
            }
            if (_project == null || !string.Equals(_project.Info.ProjectNumber, parsed.ProjectNumber, StringComparison.OrdinalIgnoreCase))
            {
                _number.Text = parsed.ProjectNumber;
                FindProject();
            }
        }

        private void Browse(UploadCategory category)
        {
            if (_busy) return;
            using (var dialog = new OpenFileDialog { Multiselect = true, Title = "Choose " + category.Name.ToLowerInvariant() + " to upload" })
                if (dialog.ShowDialog(this) == DialogResult.OK) AddPaths(dialog.FileNames, category);
        }

        /// <summary>
        /// Adds files to the list. With a category (dropped on its box) they are that type; a file
        /// already in the list that is dropped on another box changes to that type. Without one
        /// (the command line) the type is guessed from names, folders and extensions. A folder
        /// already named as a download fills in the form either way.
        /// </summary>
        private void AddPaths(IEnumerable<string> paths, UploadCategory category = null)
        {
            paths = paths.ToList();
            ReadDownloadName(paths);
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
            Say(added + " file" + (added == 1 ? "" : "s") + " added" + what + "." + (_project == null ? " Pick the project to see their names." : Visit == null ? " Fill in the crew and work type to see their names." : " Check the names, then Upload."), Muted);
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
            var visit = Visit;
            var kept = _items.Select(i => i.NamePrefix).Where(n => n != null).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            _downloadLabel.Text = _project == null ? "(pick the project)"
                : kept.Count > 0 ? string.Join(",  ", kept) + "   (kept as the crew named it)"
                : visit == null ? "(fill in the crew initials and work type)"
                : DownloadNames.Name(_config, _project.Info.ProjectNumber, visit);
            _downloadLabel.ForeColor = _project != null && visit != null ? Theme.Charcoal : Muted;
            if (_project != null && visit != null && _items.Count > 0)
            {
                try
                {
                    _planner.Assign(_project, _items, visit);
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
            var visit = Visit;
            var visitFolder = _project != null && visit != null ? _planner.DownloadFolder(_project, visit) : null;
            foreach (var item in _items)
            {
                var download = _project == null ? null : item.NamePrefix != null ? Path.Combine(_planner.DownloadsRoot(_project), item.NamePrefix) : visitFolder;
                var i = _grid.Rows.Add();
                var row = _grid.Rows[i];
                row.Tag = item;
                // Inside a named download the folder name is already shown above: leave it off.
                row.Cells["file"].Value = item.NamePrefix != null && item.DroppedAs.StartsWith(item.NamePrefix + "\\", StringComparison.OrdinalIgnoreCase)
                    ? item.DroppedAs.Substring(item.NamePrefix.Length + 1)
                    : item.DroppedAs;
                row.Cells["file"].ToolTipText = item.SourcePath;
                row.Cells["type"].Value = item.Category?.Name;
                if (_project == null || Visit == null)
                {
                    row.Cells["name"].Value = _project == null ? "(pick the project)" : "(fill in crew and work type)";
                    row.Cells["name"].Style.ForeColor = Muted;
                }
                else
                {
                    var shown = item.FinalPath ?? item.ConflictWith;
                    row.Cells["name"].Value = Path.GetFileName(shown);
                    row.Cells["folder"].Value = InDownload(download, Path.GetDirectoryName(shown));
                    row.Cells["folder"].ToolTipText = shown;
                    switch (item.State)
                    {
                        case UploadState.AlreadyUploaded:
                            row.Cells["status"].Value = "Already in job";
                            row.Cells["status"].ToolTipText = "A file of this name and size is already there: " + item.AlreadyUploadedAs;
                            row.DefaultCellStyle.ForeColor = Muted;
                            break;
                        case UploadState.Conflict:
                            row.Cells["status"].Value = "Conflict";
                            row.Cells["status"].ToolTipText = "A different file (" + UploadPlannerLength(item.ConflictWith) + " bytes) already has this name; this one is "
                                + UploadPlannerLength(item.SourcePath) + ". Right-click to keep both or skip.";
                            row.Cells["status"].Style.ForeColor = Bad;
                            break;
                        case UploadState.Skipped:
                            row.Cells["status"].Value = "Skipped";
                            row.DefaultCellStyle.ForeColor = Muted;
                            break;
                        case UploadState.Failed:
                            row.Cells["status"].Value = "Failed";
                            row.Cells["status"].ToolTipText = item.Error;
                            row.Cells["status"].Style.ForeColor = Bad;
                            break;
                        default:
                            if (item.LastError != null)
                            {
                                row.Cells["status"].Value = "Failed -- retry";
                                row.Cells["status"].ToolTipText = item.LastError;
                                row.Cells["status"].Style.ForeColor = Bad;
                            }
                            else if (item.ConflictWith != null) row.Cells["status"].Value = "Keep both";
                            break;
                    }
                }
            }
            _grid.CellValueChanged += TypeChanged;

            var toCopy = _items.Count(i => i.State == UploadState.Ready || i.State == UploadState.Conflict || i.State == UploadState.Failed);
            _upload.Enabled = !_busy && _project != null && Visit != null && _items.Count(i => !i.Done) > 0;
            _upload.Text = toCopy > 0 ? "Upload " + toCopy + " file" + (toCopy == 1 ? "" : "s") + " to job" : "Check and record";
            foreach (var box in _boxes) box.SetCount(_items.Count(i => i.Category == box.Category));
            _dropHelp.Text = _items.Count == 0
                ? HelpText
                : "Check the names below. Drop a file on another box, or change its Type, to move it. Delete removes it from the list.";
        }

        private static long UploadPlannerLength(string path)
        {
            try { return path == null ? -1 : new FileInfo(path).Length; }
            catch (IOException) { return -1; }
        }

        /// <summary>Where in the download folder: "(download folder)" or "Photos".</summary>
        private string InDownload(string download, string folder)
        {
            if (download == null || folder == null) return Relative(folder);
            if (string.Equals(folder.TrimEnd('\\', '/'), download.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)) return "(download folder)";
            return folder.StartsWith(download, StringComparison.OrdinalIgnoreCase) ? folder.Substring(download.Length).TrimStart('\\', '/') : Relative(folder);
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
            if (Visit == null)
            {
                MessageBox.Show(this, "Enter the crew's initials and pick the work type first: they make the download folder's name.", "Upload", MessageBoxButtons.OK, MessageBoxIcon.Information);
                if (string.IsNullOrWhiteSpace(_crew.Text)) _crew.Focus(); else _work.Focus();
                return;
            }

            Replan();
            if (!ResolveConflicts()) return;
            var project = _project;
            var crew = _crew.Text.Trim();
            var batch = _items.ToList();
            var total = batch.Count;
            var count = 0;
            _busy = true;
            _upload.Enabled = false;
            UseWaitCursor = true;
            UploadResult result;
            try
            {
                var runner = new UploadRunner(_config);
                result = await Task.Run(() => runner.Run(project, batch, crew, item =>
                    BeginInvoke((Action)(() => Say("Uploading " + (++count) + " of " + total + ": " + Path.GetFileName(item.SourcePath), Muted)))));
            }
            finally
            {
                _busy = false;
                UseWaitCursor = false;
            }

            _remembered.Crew = crew;
            _remembered.Save();
            Replan();
            var summary = result.Copied + " copied, " + result.AlreadyUploaded + " already in the job" + (result.Skipped > 0 ? ", " + result.Skipped + " skipped" : string.Empty);
            if (result.Complete)
            {
                Say("Upload complete and verified: " + summary + ". Every file is in " + project.Display + " at its original size; the originals were left where they were.", Good);
            }
            else
            {
                Say("Upload NOT complete: " + summary + ", " + (result.Problems.Count + result.Undecided) + " not verified -- they are still in the list.", Bad);
                MessageBox.Show(this, string.Join("\n", result.Problems.Take(10).Select(f => Path.GetFileName(f.SourcePath) + ": " + f.Error))
                    + (result.Undecided > 0 ? "\n" + result.Undecided + " name conflict(s) were not decided and were not uploaded." : string.Empty),
                    "Upload not complete", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>
        /// Files whose name is taken in the job by a different file are never copied over it.
        /// Asks once for all of them: keep both (the new one numbered -2), or skip them.
        /// False when the crew cancels.
        /// </summary>
        private bool ResolveConflicts()
        {
            var conflicts = _items.Where(i => i.State == UploadState.Conflict).ToList();
            if (conflicts.Count == 0) return true;
            var list = string.Join("\n", conflicts.Take(8).Select(c => "  " + Path.GetFileName(c.ConflictWith)
                + "  (job: " + UploadPlannerLength(c.ConflictWith) + " bytes, this one: " + UploadPlannerLength(c.SourcePath) + ")"));
            var answer = MessageBox.Show(this,
                conflicts.Count + " file(s) have the same name as a different file already in the job:\n\n" + list
                + (conflicts.Count > 8 ? "\n  ..." : string.Empty)
                + "\n\nNothing in the job will be overwritten.\n\nYes: keep both (the new file gets -2)\nNo: skip these files\nCancel: stop and look first",
                "Name conflicts", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
            if (answer == DialogResult.Cancel) return false;
            foreach (var c in conflicts) c.Choice = answer == DialogResult.Yes ? ConflictChoice.KeepBoth : ConflictChoice.Skip;
            Replan();
            return true;
        }

        private void SetChoice(ConflictChoice choice)
        {
            foreach (DataGridViewRow row in _grid.SelectedRows)
                if (row.Tag is UploadItem item && item.ConflictWith != null) item.Choice = choice;
            Replan();
        }

        // ------------------------------------------------------------------ documents

        private bool HaveVisit()
        {
            if (_project == null) return false;
            if (Visit != null) return true;
            MessageBox.Show(this, "Enter the crew's initials and pick the work type first: they name the document.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }

        private void NewDocument(DocumentTemplate document)
        {
            if (!HaveVisit()) return;
            try
            {
                var maker = new DocumentMaker(_config);
                if (maker.TemplatePath(document) == null && !string.IsNullOrWhiteSpace(document.Template))
                    Say("Template " + document.Template + " not found; started a blank text sheet instead.", Bad);
                var path = maker.Create(_project, document, Visit);
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
            if (!HaveVisit()) return;
            var category = _config.Category("notes") ?? _config.FallbackCategory;
            using (var form = new NotesForm(_project.Display, _crew.Text, _date.Value.Date))
            {
                if (form.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    var path = new DocumentMaker(_config).CreateNotes(_project, category, Visit, form.Notes);
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
