using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace CrewUpload.App
{
    /// <summary>The PM's form for a new project: number and name decide the folder.</summary>
    internal sealed class NewProjectForm : Form
    {
        private readonly JobFolderConfig _config;
        private readonly TextBox _number;
        private readonly TextBox _name;
        private readonly TextBox _client;
        private readonly TextBox _pm;
        private readonly Label _preview;

        public ProjectInfo Info { get; private set; }

        public NewProjectForm(JobFolderConfig config, string number)
        {
            _config = config;
            Text = "New project";
            Font = Theme.Body(10f);
            ForeColor = Theme.Charcoal;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = MaximizeBox = false;
            ShowInTaskbar = false;
            BackColor = Color.White;

            var stack = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true, Padding = new Padding(16, 14, 16, 10) };
            stack.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
            stack.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 400));

            _number = Field(stack, "Project number", JobFolderConfig.NormalizeProjectNumber(number), true);
            _name = Field(stack, "Project name", string.Empty, false);
            _client = Field(stack, "Client", string.Empty, false);
            _pm = Field(stack, "Project manager", Environment.UserName, false);

            _preview = new Label { AutoSize = false, Size = new Size(520, 60), ForeColor = MainForm.Muted, Margin = new Padding(3, 10, 3, 3) };
            stack.Controls.Add(_preview);
            stack.SetColumnSpan(_preview, 2);
            foreach (var box in new[] { _number, _name, _client }) box.TextChanged += (s, e) => Preview();

            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill };
            var ok = Theme.Button("Create project", true);
            ok.Click += (s, e) => Accept();
            var cancel = Theme.Button("Cancel", false);
            cancel.DialogResult = DialogResult.Cancel;
            buttons.Controls.Add(ok);
            buttons.Controls.Add(cancel);
            stack.Controls.Add(buttons);
            stack.SetColumnSpan(buttons, 2);
            AcceptButton = ok;
            CancelButton = cancel;

            Controls.Add(stack);
            ClientSize = new Size(570, stack.PreferredSize.Height + 6);
            Preview();
        }

        private static TextBox Field(TableLayoutPanel stack, string caption, string value, bool upper)
        {
            stack.Controls.Add(MainForm.Caption(caption));
            var box = new TextBox { Width = 390, Text = value ?? string.Empty, CharacterCasing = upper ? CharacterCasing.Upper : CharacterCasing.Normal };
            stack.Controls.Add(box);
            return box;
        }

        private void Preview()
        {
            var number = JobFolderConfig.NormalizeProjectNumber(_number.Text);
            if (!_config.IsValidProjectNumber(number))
            {
                _preview.Text = number.Length == 0 ? "Enter the project number." : "'" + number + "' does not look like a project number.";
                return;
            }
            var values = new Dictionary<string, string> { { "projectNumber", number }, { "projectName", _name.Text.Trim() }, { "client", _client.Text.Trim() }, { "year", DateTime.Today.Year.ToString() } };
            _preview.Text = "Folder:  " + Naming.Combine(_config.JobsRoot, _config.NewProjectParent, values) + "\\"
                + Naming.Clean(Naming.Fill(_config.ProjectFolderName, values))
                + "\r\nwith the office's standard folders inside.";
        }

        private void Accept()
        {
            var number = JobFolderConfig.NormalizeProjectNumber(_number.Text);
            if (!_config.IsValidProjectNumber(number))
            {
                MessageBox.Show(this, "'" + number + "' is not a valid project number.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (string.IsNullOrWhiteSpace(_name.Text))
            {
                MessageBox.Show(this, "Give the project a name; it goes on the folder.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            Info = new ProjectInfo
            {
                ProjectNumber = number,
                ProjectName = _name.Text.Trim(),
                Client = _client.Text.Trim(),
                ProjectManager = _pm.Text.Trim(),
                CreatedBy = Environment.UserName,
                Created = DateTime.Now,
            };
            DialogResult = DialogResult.OK;
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
