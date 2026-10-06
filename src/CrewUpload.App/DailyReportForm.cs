using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using CrewUpload.Reports;

namespace CrewUpload.App
{
    /// <summary>
    /// The crew's daily report, laid out like Form 03-SV-125-GW. Opens with whatever the upload screen
    /// (or the schedule) already knows; every field stays editable, and what is submitted is what the
    /// crew says happened. Submitting files the PDF in the project, in the admin folder and on this PC.
    /// </summary>
    internal sealed class DailyReportForm : Form
    {
        private readonly JobFolderConfig _config;
        private readonly ProjectStore _projects;
        private readonly CrewSettings _crewList;
        private readonly DailyReportSettings _form;

        private readonly Panel _top;
        private readonly DateTimePicker _date;
        private readonly TextBox _number, _job, _task, _subtask, _workOrder, _weather, _crew, _dataFile, _control, _owner, _start, _finish;
        private readonly ComboBox _work;
        private readonly Label _projectState, _crewNames, _total, _status;
        private readonly NumericUpDown _hours;
        private readonly List<CheckBox> _equipment = new List<CheckBox>();
        private readonly List<RadioButton> _vehicles = new List<RadioButton>();
        private readonly List<CheckBox> _observations = new List<CheckBox>();
        private readonly List<CheckBox> _precautions = new List<CheckBox>();
        private readonly TextBox _notes, _extras, _otherObservation, _otherPrecaution;
        private readonly Button _submit;
        private DailyReport _draft;

        /// <summary>The report as submitted, or null.</summary>
        public SubmitResult Result { get; private set; }

        /// <summary>A strip above the form for whoever opened it (the schedule's choices); empty by default.</summary>
        public Panel TopStrip => _top;

        public DailyReportForm(JobFolderConfig config, ProjectStore projects, CrewSettings crewList, DailyReport draft)
        {
            _config = config;
            _projects = projects;
            _crewList = crewList ?? new CrewSettings();
            _form = config.DailyReport ?? new DailyReportSettings();

            Text = "Daily field report";
            Font = Theme.Body(9.5f);
            ForeColor = Theme.Charcoal;
            BackColor = Color.White;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(980, 820);
            MinimumSize = new Size(800, 500);

            var body = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(14, 8, 14, 8) };

            // ---- job
            var job = Grid(4);
            _date = new DateTimePicker { Format = DateTimePickerFormat.Short, Width = 130 };
            _number = Box(150);
            _number.CharacterCasing = CharacterCasing.Upper;
            _number.Leave += (s, e) => CheckProject();
            _projectState = new Label { AutoSize = true, ForeColor = Theme.MediumGray, Margin = new Padding(3, 6, 3, 3), MaximumSize = new Size(380, 0) };
            _job = Box(320);
            _task = Box(80);
            _subtask = Box(80);
            _work = new ComboBox { Width = 200, DropDownStyle = ComboBoxStyle.DropDown };
            _work.Items.AddRange(config.WorkTypes.Select(w => (object)w.Code).ToArray());
            _workOrder = Box(150);
            _weather = Box(200);
            Add(job, 0, "Date", _date, "Project #", Flow(_number, _projectState));
            Add(job, 1, "Job name", _job, "Task #", Flow(_task, Caption("Subtask #"), _subtask));
            Add(job, 2, "Work type", _work, "Work order", _workOrder);
            Add(job, 3, "Weather (optional)", _weather, null, null);
            body.Controls.Add(Section("Job", job));

            // ---- crew and day
            var crew = Grid(4);
            _crew = Box(200);
            _crew.CharacterCasing = CharacterCasing.Upper;
            _crew.TextChanged += (s, e) => ShowCrew();
            _crewNames = new Label { AutoSize = true, ForeColor = Theme.MediumGray, Margin = new Padding(3, 6, 3, 3), MaximumSize = new Size(560, 0) };
            _hours = new NumericUpDown { Width = 80, DecimalPlaces = 2, Increment = 0.25m, Minimum = 0, Maximum = 24 };
            _dataFile = Box(320);
            _control = Box(200);
            Add(crew, 0, "Crew initials", Flow(_crew, _crewNames), null, null);
            crew.SetColumnSpan(crew.GetControlFromPosition(1, 0), 3);
            Add(crew, 1, "Hours", _hours, "Control file", _control);
            Add(crew, 2, "Data file name", _dataFile, null, null);
            body.Controls.Add(Section("Crew (yours first; others separated by spaces)", crew));

            // ---- equipment, vehicle, mileage
            var equipment = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0, 0, 30, 0) };
            foreach (var e in _form.Equipment.Select(e => e.Name).Concat(new[] { _form.NoEquipment }))
            {
                var cb = new CheckBox { Text = e, AutoSize = true, Margin = new Padding(3, 2, 3, 2), Tag = e };
                _equipment.Add(cb);
                equipment.Controls.Add(cb);
            }
            var noEquipment = _equipment.Last();
            noEquipment.CheckedChanged += (s, e) => { if (noEquipment.Checked) foreach (var c in _equipment.Where(c => c != noEquipment)) c.Checked = false; };
            foreach (var c in _equipment.Where(c => c != noEquipment)) c.CheckedChanged += (s, e) => { if (((CheckBox)s).Checked) noEquipment.Checked = false; };

            var vehicles = new TableLayoutPanel { AutoSize = true, ColumnCount = 3, Margin = new Padding(0) };
            var none = new RadioButton { Text = "No vehicle", AutoSize = true, Tag = null, Margin = new Padding(3, 2, 12, 2) };
            _vehicles.Add(none);
            var personal = new RadioButton { Text = _form.PersonalAuto, AutoSize = true, Tag = _form.PersonalAuto, Margin = new Padding(3, 2, 12, 2) };
            _vehicles.Add(personal);
            _owner = Box(150);
            _owner.Enabled = false;
            personal.CheckedChanged += (s, e) => _owner.Enabled = personal.Checked;
            var perColumn = (int)Math.Ceiling(_form.Vehicles.Count / 3.0);
            for (var i = 0; i < _form.Vehicles.Count; i++)
            {
                var v = _form.Vehicles[i];
                var rb = new RadioButton { Text = v.Id + "  " + v.Description, AutoSize = true, Tag = v.Id, Margin = new Padding(3, 2, 6, 2), Font = Theme.Body(8.5f) };
                _vehicles.Add(rb);
                vehicles.Controls.Add(rb, i / perColumn, i % perColumn);
            }
            _start = Box(80);
            _finish = Box(80);
            _start.TextChanged += (s, e) => ShowTotal();
            _finish.TextChanged += (s, e) => ShowTotal();
            _total = new Label { AutoSize = true, Margin = new Padding(3, 6, 3, 3), Font = Theme.Body(9.5f, FontStyle.Bold) };
            var vehicleBlock = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            vehicleBlock.Controls.Add(Flow(none, personal, Caption("owner"), _owner));
            vehicleBlock.Controls.Add(vehicles);
            vehicleBlock.Controls.Add(Flow(Caption("Mileage start"), _start, Caption("finish"), _finish, Caption("total"), _total));
            body.Controls.Add(Section("Equipment and vehicle", Flow(equipment, vehicleBlock)));

            // ---- comments
            _notes = new TextBox { Multiline = true, Width = 900, Height = 90, ScrollBars = ScrollBars.Vertical, AcceptsReturn = true };
            body.Controls.Add(Section("Comments", _notes));

            // ---- safety
            _otherObservation = Box(300);
            body.Controls.Add(Section("Site safety observations", Checks(_form.SafetyObservations, _observations, _otherObservation)));
            _otherPrecaution = Box(300);
            body.Controls.Add(Section("Site safety precautions", Checks(_form.SafetyPrecautions, _precautions, _otherPrecaution)));

            _extras = new TextBox { Multiline = true, Width = 900, Height = 50, ScrollBars = ScrollBars.Vertical, AcceptsReturn = true };
            body.Controls.Add(Section("Extras", _extras));

            // ---- bottom
            _status = new Label { Dock = DockStyle.Fill, Padding = new Padding(0, 8, 0, 0), ForeColor = Theme.MediumGray };
            _submit = Theme.Button("Submit report", true);
            _submit.Click += (s, e) => Submit();
            var preview = Theme.Button("Preview PDF", false);
            preview.Click += (s, e) => Preview();
            var cancel = Theme.Button("Cancel", false);
            cancel.DialogResult = DialogResult.Cancel;
            CancelButton = cancel;
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
            buttons.Controls.Add(_submit);
            buttons.Controls.Add(preview);
            buttons.Controls.Add(cancel);
            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 54, Padding = new Padding(14, 8, 14, 8), BackColor = Theme.LightGray4 };
            bottom.Controls.Add(_status);
            bottom.Controls.Add(buttons);

            _top = new Panel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(14, 8, 14, 0) };

            Controls.Add(body);
            Controls.Add(_top);
            Controls.Add(bottom);

            ApplyDraft(draft ?? new DailyReport { Date = DateTime.Today });
        }

        // ------------------------------------------------------------------ filling in

        /// <summary>Puts a draft into the form. Used when it opens and when the crew picks another schedule entry.</summary>
        public void ApplyDraft(DailyReport d)
        {
            _draft = d;
            _date.Value = d.Date == default(DateTime) ? DateTime.Today : d.Date;
            _number.Text = d.ProjectNumber ?? string.Empty;
            _job.Text = d.ProjectName ?? string.Empty;
            _task.Text = d.TaskNumber ?? string.Empty;
            _subtask.Text = d.Subtask ?? string.Empty;
            _work.Text = d.WorkType ?? string.Empty;
            _workOrder.Text = d.WorkOrder ?? string.Empty;
            _weather.Text = d.Weather ?? string.Empty;
            _crew.Text = string.Join(" ", d.Crew.Select(c => c.Initials));
            _hours.Value = d.Hours.HasValue ? Math.Max(0, Math.Min(24, d.Hours.Value)) : 0;
            _dataFile.Text = d.DataFileName ?? string.Empty;
            _control.Text = d.ControlFile ?? string.Empty;
            foreach (var c in _equipment) c.Checked = d.Equipment.Any(x => Same(x, (string)c.Tag));
            var vehicle = _vehicles.FirstOrDefault(v => Same(v.Tag as string, d.Vehicle)) ?? _vehicles[0];
            vehicle.Checked = true;
            _owner.Text = d.PersonalAutoOwner ?? string.Empty;
            _start.Text = d.Mileage.Start?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
            _finish.Text = d.Mileage.Finish?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
            _notes.Text = d.Notes ?? string.Empty;
            SetChecks(_observations, _otherObservation, d.SafetyObservations);
            SetChecks(_precautions, _otherPrecaution, d.SafetyPrecautions);
            _extras.Text = d.Extras ?? string.Empty;
            CheckProject();
            ShowCrew();
            ShowTotal();
        }

        /// <summary>What the form says now.</summary>
        public DailyReport Collect()
        {
            var r = new DailyReport
            {
                Date = _date.Value.Date,
                ProjectNumber = JobFolderConfig.NormalizeFullNumber(_number.Text),
                ProjectName = Clean(_job.Text),
                TaskNumber = Clean(_task.Text),
                Subtask = Clean(_subtask.Text),
                WorkType = Clean(_work.Text)?.ToUpperInvariant(),
                WorkOrder = Clean(_workOrder.Text),
                Weather = Clean(_weather.Text),
                Hours = _hours.Value > 0 ? _hours.Value : (decimal?)null,
                DataFileName = Clean(_dataFile.Text),
                ControlFile = Clean(_control.Text),
                Vehicle = _vehicles.FirstOrDefault(v => v.Checked)?.Tag as string,
                Notes = Clean(_notes.Text),
                Extras = Clean(_extras.Text),
                // what the schedule contributed is kept with the report; what the crew typed wins
                ScheduleProjectId = _draft?.ScheduleProjectId,
                ScheduleAssignmentIds = _draft?.ScheduleAssignmentIds?.ToList() ?? new List<string>(),
                Source = _draft?.Source ?? "manual",
            };
            if (string.IsNullOrEmpty(r.ProjectNumber)) r.ProjectNumber = null;
            r.Crew.AddRange(DailyReportDraft.ParseCrew(_crew.Text, _crewList));
            foreach (var c in r.Crew)
            {
                // a name the schedule supplied for someone not in the crew list
                var known = _draft?.Crew.FirstOrDefault(x => x.Initials == c.Initials);
                if (c.Name == null && known != null) { c.Name = known.Name; c.ScheduleEmployeeId = c.ScheduleEmployeeId ?? known.ScheduleEmployeeId; }
            }
            if (Same(r.Vehicle, _form.PersonalAuto)) r.PersonalAutoOwner = Clean(_owner.Text);
            r.Equipment.AddRange(_equipment.Where(c => c.Checked).Select(c => (string)c.Tag));
            r.Mileage.Start = Number(_start.Text);
            r.Mileage.Finish = Number(_finish.Text);
            r.SafetyObservations.AddRange(Checked(_observations, _otherObservation));
            r.SafetyPrecautions.AddRange(Checked(_precautions, _otherPrecaution));
            return r;
        }

        private ProjectFolder FindProject(out string problem)
        {
            problem = null;
            var number = _number.Text.Trim();
            if (number.Length == 0 || _projects == null) return null;
            try
            {
                List<ProjectRegistration> candidates;
                return _projects.Find(number, out problem, out candidates);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is InvalidDataException)
            {
                problem = "The project list cannot be reached: " + e.Message;
                return null;
            }
        }

        private void CheckProject()
        {
            string problem;
            var p = FindProject(out problem);
            if (p != null)
            {
                _projectState.Text = "Filed in " + p.Display;
                _projectState.ForeColor = Theme.MediumGray;
                if (_job.Text.Length == 0) _job.Text = DailyReportDraft.JobName(p.Info.ProjectName, p.Info.FullNumber, p.Info.ProjectNumber) ?? string.Empty;
                if (p.Info.FullNumber != null && _number.Text != p.Info.FullNumber) _number.Text = p.Info.FullNumber;
            }
            else
            {
                _projectState.Text = _number.Text.Trim().Length == 0 ? string.Empty
                    : (problem ?? "Not registered") + " -- the report is still filed for admin and on this PC.";
                _projectState.ForeColor = Theme.Red;
            }
        }

        private void ShowCrew()
        {
            var people = DailyReportDraft.ParseCrew(_crew.Text, _crewList);
            _crewNames.Text = people.Count == 0 ? string.Empty
                : (people.Count == 1 ? "Solo crew: " : people.Count + "-person crew: ")
                  + string.Join(", ", people.Select(p => p.Name ?? _draft?.Crew.FirstOrDefault(x => x.Initials == p.Initials)?.Name ?? p.Initials + " (not in the crew list)"));
        }

        private void ShowTotal()
        {
            var s = Number(_start.Text);
            var f = Number(_finish.Text);
            _total.Text = s.HasValue && f.HasValue ? (f - s).Value.ToString(CultureInfo.InvariantCulture) + " mi" : string.Empty;
            _total.ForeColor = s.HasValue && f.HasValue && f < s ? Theme.Red : Theme.Charcoal;
        }

        // ------------------------------------------------------------------ preview and submit

        private void Preview()
        {
            var r = Collect();
            r.ReportId = "(preview)";
            r.SubmittedBy = RegistryLog.UserName;
            try
            {
                var path = Path.Combine(Path.GetTempPath(), "daily-report-preview-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".pdf");
                File.WriteAllBytes(path, new DailyReportFiler(_config).Render(r));
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is System.ComponentModel.Win32Exception)
            {
                Say("Could not show the preview: " + e.Message, true);
            }
        }

        private void Submit()
        {
            var r = Collect();
            var problems = r.Problems();
            if (problems.Count > 0)
            {
                Say(string.Join(" ", problems), true);
                return;
            }
            string problem;
            var project = FindProject(out problem);
            if (project == null && MessageBox.Show(this, (problem ?? r.ProjectNumber + " is not registered for uploads.")
                    + "\n\nSubmit anyway? The report is filed for admin and on this PC, but not in the project folder.", Text,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            Cursor = Cursors.WaitCursor;
            try { Result = new DailyReportFiler(_config).Submit(r, project); }
            catch (InvalidOperationException e) { Say(e.Message, true); return; }
            finally { Cursor = Cursors.Default; }

            if (!Result.Filed)
            {
                MessageBox.Show(this, "The report could not be saved anywhere:\n\n" + string.Join("\n", Result.Copies.Select(c => c.Kind + ": " + c.Error))
                    + "\n\nNothing was submitted. Check the network and try again.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Result = null;
                return;
            }
            OnSubmitted(Result);
            var lines = Result.Copies.Select(c => (c.Kind == FiledCopy.Project ? "Project: " : c.Kind == FiledCopy.Admin ? "Admin: " : "This PC: ")
                + (c.Ok ? c.Path : "not saved -- " + c.Error));
            MessageBox.Show(this, "Report " + Result.Report.ReportId + " submitted.\n\n" + string.Join("\n", lines), Text, MessageBoxButtons.OK,
                Result.Copies.All(c => c.Ok) ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            DialogResult = DialogResult.OK;
            Close();
        }

        /// <summary>After the PDFs are filed: the hook for recording the report. Never stops the submit.</summary>
        public event Action<SubmitResult> Submitted;

        private void OnSubmitted(SubmitResult result)
        {
            try { Submitted?.Invoke(result); }
            catch (Exception e) when (!(e is OutOfMemoryException)) { Say(e.Message, true); }
        }

        private void Say(string text, bool warn)
        {
            _status.Text = text;
            _status.ForeColor = warn ? Theme.Red : Theme.MediumGray;
        }

        // ------------------------------------------------------------------ layout helpers

        private static TableLayoutPanel Grid(int columns)
        {
            var t = new TableLayoutPanel { AutoSize = true, ColumnCount = columns, Margin = new Padding(0) };
            for (var i = 0; i < columns; i++) t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            return t;
        }

        private static void Add(TableLayoutPanel t, int row, string label1, Control c1, string label2, Control c2)
        {
            t.Controls.Add(Caption(label1), 0, row);
            t.Controls.Add(c1, 1, row);
            if (label2 != null) t.Controls.Add(Caption(label2), 2, row);
            if (c2 != null) t.Controls.Add(c2, 3, row);
        }

        private static Control Section(string title, Control content)
        {
            var p = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0, 4, 0, 8) };
            p.Controls.Add(new Label { Text = title, AutoSize = true, Font = Theme.Body(10.5f, FontStyle.Bold), ForeColor = Theme.Charcoal, Margin = new Padding(0, 6, 0, 4) });
            p.Controls.Add(content);
            return p;
        }

        private static FlowLayoutPanel Flow(params Control[] controls)
        {
            var f = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            f.Controls.AddRange(controls);
            return f;
        }

        private Control Checks(List<string> options, List<CheckBox> into, TextBox other)
        {
            var t = new TableLayoutPanel { AutoSize = true, ColumnCount = 3, Margin = new Padding(0) };
            var rows = (int)Math.Ceiling(options.Count / 3.0);
            for (var i = 0; i < options.Count; i++)
            {
                var cb = new CheckBox { Text = options[i], Tag = options[i], AutoSize = true, Margin = new Padding(3, 1, 18, 1) };
                into.Add(cb);
                t.Controls.Add(cb, i / rows, i % rows);
            }
            var wrap = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0) };
            wrap.Controls.Add(t);
            wrap.Controls.Add(Flow(Caption("Other"), other));
            return wrap;
        }

        private static void SetChecks(List<CheckBox> boxes, TextBox other, List<string> chosen)
        {
            foreach (var c in boxes) c.Checked = chosen.Any(x => Same(x, (string)c.Tag));
            other.Text = string.Join("; ", chosen.Where(x => !boxes.Any(b => Same(x, (string)b.Tag))));
        }

        private static IEnumerable<string> Checked(List<CheckBox> boxes, TextBox other) =>
            boxes.Where(c => c.Checked).Select(c => (string)c.Tag)
                .Concat((other.Text ?? string.Empty).Split(';').Select(x => x.Trim()).Where(x => x.Length > 0));

        private static TextBox Box(int width) => new TextBox { Width = width, Margin = new Padding(3, 3, 3, 3) };

        private static Label Caption(string text) => MainForm.Caption(text);

        private static bool Same(string a, string b) => string.Equals((a ?? string.Empty).Trim(), (b ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);

        private static string Clean(string s)
        {
            var t = (s ?? string.Empty).Trim();
            return t.Length == 0 ? null : t;
        }

        private static int? Number(string s)
        {
            int n;
            return int.TryParse((s ?? string.Empty).Replace(",", string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n) ? n : (int?)null;
        }
    }
}
