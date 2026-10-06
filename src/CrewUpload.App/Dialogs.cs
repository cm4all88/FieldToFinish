using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace CrewUpload.App
{
    /// <summary>
    /// Shown when a client-task number does not lead to exactly one project folder: the crew picks
    /// the right one, or browses to it. Nothing is ever created from here.
    /// </summary>
    internal sealed class PickProjectForm : Form
    {
        private readonly ListBox _list;
        private readonly string _root;
        private readonly string _number;

        public string ChosenPath { get; private set; }

        public PickProjectForm(string number, IList<ProjectFolder> candidates, string root)
        {
            _root = root;
            _number = number;
            Text = "Which project is " + number + "?";
            Font = Theme.Body(10f);
            ForeColor = Theme.Charcoal;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = MaximizeBox = false;
            ShowInTaskbar = false;
            BackColor = Color.White;
            ClientSize = new Size(720, 380);

            var head = new Label
            {
                Dock = DockStyle.Top, Height = 64, Padding = new Padding(14, 12, 14, 0),
                Text = candidates.Count == 0
                    ? "No project folder for " + number + " was found under\r\n" + root + ". Browse to it -- nothing will be created."
                    : candidates.Count + " folders match " + number + ". Pick the one this download belongs to.",
            };
            _list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, Font = Theme.Body(10.5f) };
            // "1800-SoundTransit\554-1800-119 TDLE Phase 3": the part of the path that tells them apart.
            foreach (var c in candidates) _list.Items.Add(new Choice { Path = c.Path, Shown = (c.Info.Client ?? string.Empty) + "\\" + c.FolderName });
            _list.DoubleClick += (s, e) => Choose();
            var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 0, 14, 0) };
            body.Controls.Add(_list);

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Padding = new Padding(10) };
            var ok = Theme.Button("Use this project", true);
            ok.Enabled = candidates.Count > 0;
            ok.Click += (s, e) => Choose();
            var browse = Theme.Button("Browse...", false);
            browse.Click += (s, e) => Browse();
            var cancel = Theme.Button("Cancel", false);
            cancel.DialogResult = DialogResult.Cancel;
            buttons.Controls.Add(ok);
            buttons.Controls.Add(browse);
            buttons.Controls.Add(cancel);
            CancelButton = cancel;

            Controls.Add(body);
            Controls.Add(head);
            Controls.Add(buttons);
        }

        private void Choose()
        {
            if (_list.SelectedItem == null) return;
            ChosenPath = ((Choice)_list.SelectedItem).Path;
            DialogResult = DialogResult.OK;
        }

        private sealed class Choice
        {
            public string Path;
            public string Shown;
            public override string ToString() => Shown;
        }

        private void Browse()
        {
            using (var dialog = new FolderBrowserDialog { Description = "Choose the project folder for " + _number + " (an existing folder; nothing is created)", ShowNewFolderButton = false })
            {
                if (Directory.Exists(_root)) dialog.SelectedPath = _root;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                var path = Unc.FromMapped(dialog.SelectedPath);
                if (!ProjectStore.LooksLike(Path.GetFileName(path), _number)
                    && MessageBox.Show(this, Path.GetFileName(path) + " does not look like project " + _number + ". Use it anyway?", Text,
                        MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                    return;
                ChosenPath = path;
                DialogResult = DialogResult.OK;
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
