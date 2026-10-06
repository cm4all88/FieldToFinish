using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using CrewUpload.Reports;

namespace CrewUpload.App
{
    /// <summary>The daily reports submitted from this PC, newest first, with whether each reached the admin folder.</summary>
    internal sealed class ReportHistoryForm : Form
    {
        private readonly ReportRecords _records;
        private readonly ListView _list;
        private readonly Label _state;

        public ReportHistoryForm(ReportRecords records)
        {
            _records = records;
            Text = "My daily reports";
            Font = Theme.Body(10f);
            ForeColor = Theme.Charcoal;
            BackColor = Color.White;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(900, 480);

            _list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, BorderStyle = BorderStyle.FixedSingle };
            _list.Columns.Add("Date", 90);
            _list.Columns.Add("Project", 110);
            _list.Columns.Add("Job", 180);
            _list.Columns.Add("Crew", 110);
            _list.Columns.Add("Work", 70);
            _list.Columns.Add("Report ID", 200);
            _list.Columns.Add("Admin", 110);
            _list.DoubleClick += (s, e) => OpenSelected();
            var wrap = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 12, 14, 6) };
            wrap.Controls.Add(_list);

            _state = new Label { Dock = DockStyle.Fill, ForeColor = Theme.MediumGray, Padding = new Padding(0, 8, 0, 0) };
            var open = Theme.Button("Open PDF", false);
            open.Click += (s, e) => OpenSelected();
            var send = Theme.Button("Send waiting records", false);
            send.Click += (s, e) => { string p; var n = _records.SendPending(out p); Fill(); Say(n + " sent." + (p != null ? " Still waiting: " + p : string.Empty), p != null); };
            var close = Theme.Button("Close", true);
            close.DialogResult = DialogResult.Cancel;
            CancelButton = close;
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
            buttons.Controls.Add(close);
            buttons.Controls.Add(open);
            buttons.Controls.Add(send);
            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 54, Padding = new Padding(14, 8, 14, 8) };
            bottom.Controls.Add(_state);
            bottom.Controls.Add(buttons);
            Controls.Add(wrap);
            Controls.Add(bottom);
            Fill();
        }

        private void Fill()
        {
            _list.Items.Clear();
            try
            {
                foreach (var h in _records.History())
                {
                    var r = h.Key;
                    var row = new ListViewItem(r.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) { Tag = r };
                    row.SubItems.Add(r.ProjectNumber ?? string.Empty);
                    row.SubItems.Add(r.ProjectName ?? string.Empty);
                    row.SubItems.Add(r.CrewInitials);
                    row.SubItems.Add(r.WorkType ?? string.Empty);
                    row.SubItems.Add(r.ReportId);
                    row.SubItems.Add(h.Value ? "recorded" : "waiting to send");
                    if (!h.Value) row.ForeColor = Theme.Red;
                    _list.Items.Add(row);
                }
                var waiting = _records.Pending;
                Say(_list.Items.Count == 0 ? "No reports submitted from this PC yet."
                    : waiting > 0 ? waiting + " record(s) could not reach the admin folder yet; they are sent when it can be reached." : string.Empty, waiting > 0);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Say("Cannot read this PC's report history: " + e.Message, true);
            }
        }

        private void OpenSelected()
        {
            if (_list.SelectedItems.Count != 1) return;
            var r = (DailyReport)_list.SelectedItems[0].Tag;
            var path = new[] { r.PdfPath, r.AdminPdfPath }.FirstOrDefault(p => !string.IsNullOrEmpty(p) && File.Exists(p));
            if (path == null) { Say("The PDF cannot be reached from here: " + (r.PdfPath ?? "(none)"), true); return; }
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (System.ComponentModel.Win32Exception e) { Say(e.Message, true); }
        }

        private void Say(string text, bool warn)
        {
            _state.Text = text;
            _state.ForeColor = warn ? Theme.Red : Theme.MediumGray;
        }
    }
}
