using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using CrewUpload.Integration;
using CrewUpload.Reports;

namespace CrewUpload.App
{
    /// <summary>
    /// Admin: scheduled versus reported for a date range -- reported crews, missing reports, and
    /// reports for work that was not on the schedule. Built from the report records and the schedule.
    /// Without the schedule it lists the reports alone.
    /// </summary>
    internal sealed class AdminReportsForm : Form
    {
        private readonly JobFolderConfig _config;
        private readonly ProjectRegistry _registry;
        private readonly IScheduleSource _schedule;
        private readonly DateTimePicker _from, _to;
        private readonly ListView _list;
        private readonly Label _summary;
        private readonly CheckBox _onlyProblems;
        private List<ComparisonRow> _rows = new List<ComparisonRow>();

        public AdminReportsForm(JobFolderConfig config, ProjectRegistry registry, IScheduleSource schedule)
        {
            _config = config;
            _registry = registry;
            _schedule = schedule;
            Text = "Daily reports: scheduled vs reported";
            Font = Theme.Body(10f);
            ForeColor = Theme.Charcoal;
            BackColor = Color.White;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(1100, 620);

            var monday = DateTime.Today.AddDays(-(((int)DateTime.Today.DayOfWeek + 6) % 7));
            _from = new DateTimePicker { Format = DateTimePickerFormat.Short, Width = 120, Value = monday };
            _to = new DateTimePicker { Format = DateTimePickerFormat.Short, Width = 120, Value = DateTime.Today };
            var refresh = Theme.Button("Show", true);
            refresh.Click += (s, e) => Reload();
            _onlyProblems = new CheckBox { Text = "Only missing and unscheduled", AutoSize = true, Margin = new Padding(16, 8, 3, 3) };
            _onlyProblems.CheckedChanged += (s, e) => Fill();
            var head = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(14, 12, 14, 4), WrapContents = false };
            head.Controls.Add(MainForm.Caption("From"));
            head.Controls.Add(_from);
            head.Controls.Add(MainForm.Caption("to"));
            head.Controls.Add(_to);
            head.Controls.Add(refresh);
            head.Controls.Add(_onlyProblems);

            _summary = new Label { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(16, 4, 16, 6), ForeColor = Theme.MediumGray, MaximumSize = new Size(1080, 0) };

            _list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, BorderStyle = BorderStyle.FixedSingle };
            _list.Columns.Add("Date", 90);
            _list.Columns.Add("Status", 160);
            _list.Columns.Add("Project", 230);
            _list.Columns.Add("Number", 100);
            _list.Columns.Add("Crew", 240);
            _list.Columns.Add("Work", 80);
            _list.Columns.Add("Report", 190);
            _list.DoubleClick += (s, e) => OpenSelected();
            var wrap = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 0, 14, 6) };
            wrap.Controls.Add(_list);

            var export = Theme.Button("Export CSV...", false);
            export.Click += (s, e) => Export();
            var open = Theme.Button("Open PDF", false);
            open.Click += (s, e) => OpenSelected();
            var close = Theme.Button("Close", false);
            close.DialogResult = DialogResult.Cancel;
            CancelButton = close;
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(14, 4, 14, 10) };
            buttons.Controls.Add(close);
            buttons.Controls.Add(open);
            buttons.Controls.Add(export);

            Controls.Add(wrap);
            Controls.Add(_summary);
            Controls.Add(head);
            Controls.Add(buttons);
            Shown += (s, e) => Reload();
        }

        private void Reload()
        {
            Cursor = Cursors.WaitCursor;
            try
            {
                var from = _from.Value.Date;
                var to = _to.Value.Date < from ? from : _to.Value.Date;
                var problems = new List<string>();
                var reports = ReportRecords.For(_config).Read(from, to, problems);
                RegistrySnapshot registry = null;
                try { registry = _registry.Load(); }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is InvalidDataException) { problems.Add("Project list: " + e.Message); }
                var schedule = _schedule;
                if (schedule != null) schedule.Refresh();
                _rows = ReportComparison.Compare(from, to, schedule, reports, registry, DateTime.Today);

                var noSchedule = schedule == null ? "The schedule is not connected, so reports are listed without comparing. "
                    : !schedule.Available ? (schedule.Message ?? SchedulePrefill.Unavailable) + " Reports are listed without comparing. " : string.Empty;
                _summary.Text = noSchedule
                    + Count(ComparisonStatus.Reported) + " reported, " + Count(ComparisonStatus.Missing) + " missing, "
                    + Count(ComparisonStatus.NotYet) + " not reported yet today, " + Count(ComparisonStatus.NotScheduled) + " reported but not scheduled"
                    + (Count(ComparisonStatus.ReportOnly) > 0 ? ", " + Count(ComparisonStatus.ReportOnly) + " reports" : string.Empty) + "."
                    + (problems.Count > 0 ? "  Could not read: " + string.Join("; ", problems.Take(3)) + (problems.Count > 3 ? " ..." : string.Empty) : string.Empty);
                _summary.ForeColor = problems.Count > 0 || noSchedule.Length > 0 ? Theme.Red : Theme.MediumGray;
                Fill();
            }
            finally { Cursor = Cursors.Default; }
        }

        private int Count(ComparisonStatus s) => _rows.Count(r => r.Status == s);

        private void Fill()
        {
            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (var x in _rows.Where(r => !_onlyProblems.Checked || r.Status == ComparisonStatus.Missing || r.Status == ComparisonStatus.NotScheduled))
            {
                var row = new ListViewItem(x.Date.ToString("ddd M/d", CultureInfo.InvariantCulture)) { Tag = x };
                row.SubItems.Add(ReportComparison.Describe(x.Status));
                row.SubItems.Add(x.Project ?? string.Empty);
                row.SubItems.Add(x.ProjectNumber ?? string.Empty);
                row.SubItems.Add(x.Crew ?? string.Empty);
                row.SubItems.Add(x.Work ?? string.Empty);
                row.SubItems.Add(string.Join(", ", x.Reports.Select(r => r.ReportId)));
                if (x.Status == ComparisonStatus.Missing) row.ForeColor = Theme.Red;
                else if (x.Status == ComparisonStatus.NotScheduled) row.ForeColor = Color.FromArgb(0xB3, 0x5C, 0x00);
                else if (x.Status == ComparisonStatus.NotYet) row.ForeColor = Theme.MediumGray;
                _list.Items.Add(row);
            }
            _list.EndUpdate();
        }

        private void OpenSelected()
        {
            if (_list.SelectedItems.Count != 1) return;
            var x = (ComparisonRow)_list.SelectedItems[0].Tag;
            var path = x.Reports.SelectMany(r => new[] { r.AdminPdfPath, r.PdfPath }).FirstOrDefault(p => !string.IsNullOrEmpty(p) && File.Exists(p));
            if (path == null) return;
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (System.ComponentModel.Win32Exception) { }
        }

        private void Export()
        {
            using (var save = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = "daily-reports-" + _from.Value.ToString("yyyyMMdd") + "-" + _to.Value.ToString("yyyyMMdd") + ".csv" })
            {
                if (save.ShowDialog(this) != DialogResult.OK) return;
                try { File.WriteAllText(save.FileName, ReportComparison.Csv(_rows), new UTF8Encoding(true)); }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    MessageBox.Show(this, e.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }
    }
}
