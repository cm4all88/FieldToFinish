using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using CrewUpload.Integration;

namespace CrewUpload.App
{
    /// <summary>
    /// "TODAY" on the main window: what the schedule has this person on, with Daily Report, Upload
    /// Files and Open Project for each. Only there when the schedule integration is; when the
    /// schedule has nothing (or cannot be read) it says so in one grey line and nothing else changes.
    /// </summary>
    internal sealed class TodayCard : Panel
    {
        private readonly FlowLayoutPanel _rows;
        private readonly Label _title;

        public event Action<PrefillChoice> ReportClicked;
        public event Action<PrefillChoice> UploadClicked;
        public event Action<PrefillChoice> OpenClicked;

        public TodayCard()
        {
            Dock = DockStyle.Top;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(14, 0, 14, 8);
            var frame = new Panel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Theme.LightGray4, Padding = new Padding(12, 8, 12, 8) };
            _title = new Label { AutoSize = true, Font = Theme.Body(10f, FontStyle.Bold), ForeColor = Theme.Charcoal, Dock = DockStyle.Top, Text = "TODAY" };
            _rows = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0) };
            frame.Controls.Add(_rows);
            frame.Controls.Add(_title);
            Controls.Add(frame);
            Visible = false;
        }

        public void ShowLoading(DateTime date)
        {
            SetTitle(date, null);
            _rows.Controls.Clear();
            _rows.Controls.Add(Note("Checking the schedule..."));
            Visible = true;
        }

        public void Show(PrefillResult result, DateTime date)
        {
            SetTitle(date, result?.Person);
            _rows.SuspendLayout();
            _rows.Controls.Clear();
            if (result == null || result.Choices.Count == 0)
                _rows.Controls.Add(Note(result?.Message ?? SchedulePrefill.Unavailable));
            foreach (var c in result?.Choices ?? Enumerable.Empty<PrefillChoice>())
            {
                var choice = c;
                var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 2) };
                row.Controls.Add(new Label
                {
                    AutoSize = true, MaximumSize = new Size(640, 0), Margin = new Padding(0, 8, 12, 0), Font = Theme.Body(10f),
                    Text = choice.Label + (choice.Project == null ? "   (not linked to a registered project)" : string.Empty),
                });
                var report = Theme.Button("Daily Report", true);
                report.Click += (s, e) => ReportClicked?.Invoke(choice);
                var upload = Theme.Button("Upload Files", false);
                upload.Click += (s, e) => UploadClicked?.Invoke(choice);
                var open = Theme.Button("Open Project", false);
                open.Enabled = choice.Project != null;
                open.Click += (s, e) => OpenClicked?.Invoke(choice);
                row.Controls.Add(report);
                row.Controls.Add(upload);
                row.Controls.Add(open);
                _rows.Controls.Add(row);
            }
            _rows.ResumeLayout();
            Visible = true;
        }

        private void SetTitle(DateTime date, CrewMember person) =>
            _title.Text = (date.Date == DateTime.Today ? "TODAY" : date.ToString("dddd M/d").ToUpperInvariant())
                + (person == null ? string.Empty : "  ·  " + (person.Name ?? person.Initials));

        private static Label Note(string text) =>
            new Label { AutoSize = true, ForeColor = Theme.MediumGray, Margin = new Padding(0, 4, 0, 2), Text = text };
    }
}
