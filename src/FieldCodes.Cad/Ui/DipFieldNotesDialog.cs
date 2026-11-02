using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using FieldCodes.Utilities.FieldNotes;

namespace FieldCodes.Cad.Ui
{
    /// <summary>What the Save Field Notes PDF window starts from, read from the drawing inside a command.</summary>
    internal sealed class DipFieldNotesPrep
    {
        public FieldNotesHeader Header { get; set; }
        public string DrawingPath { get; set; }
        public string Folder { get; set; }
        public bool Overwrite { get; set; }
        public bool OpenAfterSave { get; set; }
        public List<DipFieldNotesChoice> Choices { get; private set; }

        public DipFieldNotesPrep()
        {
            Header = new FieldNotesHeader();
            Choices = new List<DipFieldNotesChoice>();
        }
    }

    /// <summary>One structure in the window's list: complete ones can be ticked, incomplete ones say what is missing.</summary>
    internal sealed class DipFieldNotesChoice
    {
        public string StructureId { get; set; }
        public string Label { get; set; }
        public bool Complete { get; set; }
        public string Missing { get; set; }

        public override string ToString()
        {
            return Complete ? Label : Label + "   —   incomplete, not included: " + Missing;
        }
    }

    /// <summary>
    /// Save Field Notes PDF: the header typed from the field book (project, number, date of observation, crew),
    /// which completed observations go in, and where the file goes. Incomplete observations are listed but cannot be
    /// ticked -- the record never presents an incomplete observation as complete. The window shows the exact file it
    /// will write, and whether that is a new revision or (only when asked) a replacement.
    /// </summary>
    internal sealed class DipFieldNotesDialog : Form
    {
        private readonly DipFieldNotesPrep _prep;
        private readonly TextBox _name, _number, _crew, _folder;
        private readonly DateTimePicker _date;
        private readonly CheckedListBox _list;
        private readonly CheckBox _overwrite, _open;
        private readonly Label _target;

        public FieldNotesHeader Header { get; private set; }
        public List<string> SelectedIds { get; private set; }
        public string Folder { get; private set; }
        public bool Overwrite { get { return _overwrite.Checked; } }
        public bool OpenAfterSave { get { return _open.Checked; } }

        public DipFieldNotesDialog(DipFieldNotesPrep prep)
        {
            _prep = prep ?? new DipFieldNotesPrep();
            Text = "Save Field Notes PDF";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            BackColor = DipBuilderForm.Ground;
            ForeColor = DipBuilderForm.Ink;
            Font = DipBuilderForm.F(10.5f, false);
            Padding = new Padding(16);
            ClientSize = new Size(760, 640);
            MinimumSize = new Size(640, 560);

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, BackColor = DipBuilderForm.Ground, AutoScroll = true };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Controls.Add(layout);

            layout.Controls.Add(new Label
            {
                AutoSize = true, Font = DipBuilderForm.F(13f, true), ForeColor = DipBuilderForm.Ink,
                Text = "Field notes PDF", Margin = new Padding(0, 0, 0, 4)
            });
            layout.Controls.Add(Hint("The completed dip observations as field-book notes: measured depths (MD) in black, " +
                                     "calculated inverts (INV) in red. Typed from the field book -- anything left blank prints as \"not recorded\"."));

            // Header ---------------------------------------------------------------------------------------
            var header = _prep.Header ?? new FieldNotesHeader();
            var grid = new TableLayoutPanel { ColumnCount = 4, AutoSize = true, Dock = DockStyle.Top, BackColor = DipBuilderForm.Ground, Margin = new Padding(0, 8, 0, 6) };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            _name = Box(header.ProjectName);
            _number = Box(header.ProjectNumber);
            _crew = Box(header.Crew);
            _date = new DateTimePicker
            {
                Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd", ShowCheckBox = true,
                Width = 150, Margin = new Padding(0, 3, 12, 6)
            };
            if (header.ObservationDate.HasValue) { _date.Value = header.ObservationDate.Value; _date.Checked = true; }
            else _date.Checked = false;
            grid.Controls.Add(Caption("Project"), 0, 0);
            grid.Controls.Add(_name, 1, 0);
            grid.Controls.Add(Caption("Project no."), 2, 0);
            grid.Controls.Add(_number, 3, 0);
            grid.Controls.Add(Caption("Date of observation"), 0, 1);
            grid.Controls.Add(_date, 1, 1);
            grid.Controls.Add(Caption("Crew / observer"), 2, 1);
            grid.Controls.Add(_crew, 3, 1);
            layout.Controls.Add(grid);
            layout.Controls.Add(Hint("Untick the date when the field book does not give one; the file is then named \"undated\"."));

            // Observations ---------------------------------------------------------------------------------
            var complete = _prep.Choices.Count(c => c.Complete);
            layout.Controls.Add(new Label
            {
                AutoSize = true, Font = DipBuilderForm.F(11f, true), ForeColor = DipBuilderForm.Ink, Margin = new Padding(0, 10, 0, 2),
                Text = "Observations (" + complete + " complete of " + _prep.Choices.Count + ")"
            });
            layout.Controls.Add(Hint("Complete: surveyed rim, every pipe dipped (or the reason it was not), and every dip says what it was taken to. " +
                                     "Incomplete observations cannot be ticked; the PDF lists them as not included."));
            _list = new CheckedListBox
            {
                Anchor = AnchorStyles.Left | AnchorStyles.Right, Width = 700, CheckOnClick = true, IntegralHeight = false, Height = 220,
                BackColor = DipBuilderForm.Surface, ForeColor = DipBuilderForm.Ink, BorderStyle = BorderStyle.FixedSingle,
                HorizontalScrollbar = true
            };
            foreach (var choice in _prep.Choices) _list.Items.Add(choice, choice.Complete);
            _list.ItemCheck += (s, e) =>
            {
                var choice = _list.Items[e.Index] as DipFieldNotesChoice;
                if (choice != null && !choice.Complete && e.NewValue == CheckState.Checked) e.NewValue = CheckState.Unchecked;
            };
            layout.Controls.Add(_list);
            var pick = Row();
            pick.Controls.Add(MakeButton("All complete", (s, e) => SetAll(true), false));
            pick.Controls.Add(MakeButton("None", (s, e) => SetAll(false), false));
            layout.Controls.Add(pick);

            // Where ----------------------------------------------------------------------------------------
            layout.Controls.Add(new Label
            {
                AutoSize = true, Font = DipBuilderForm.F(11f, true), ForeColor = DipBuilderForm.Ink, Margin = new Padding(0, 10, 0, 2),
                Text = "Save to"
            });
            var where = Row();
            _folder = Box(_prep.Folder);
            _folder.Width = 520;
            where.Controls.Add(_folder);
            where.Controls.Add(MakeButton("Browse...", OnBrowse, false));
            layout.Controls.Add(where);
            _target = Hint(string.Empty);
            layout.Controls.Add(_target);
            _overwrite = new CheckBox
            {
                AutoSize = true, ForeColor = DipBuilderForm.Ink, Checked = _prep.Overwrite, Margin = new Padding(0, 6, 0, 0),
                Text = "Replace the newest PDF of this name instead of saving a new revision (_R1, _R2...)"
            };
            _open = new CheckBox { AutoSize = true, ForeColor = DipBuilderForm.Ink, Checked = _prep.OpenAfterSave, Text = "Open the PDF after saving" };
            layout.Controls.Add(_overwrite);
            layout.Controls.Add(_open);

            // Buttons --------------------------------------------------------------------------------------
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, BackColor = DipBuilderForm.Ground, Padding = new Padding(0, 10, 0, 0) };
            var save = MakeButton("Save PDF", OnSave, true);
            var cancel = MakeButton("Cancel", (s, e) => { DialogResult = DialogResult.Cancel; Close(); }, false);
            buttons.Controls.Add(save);
            buttons.Controls.Add(cancel);
            Controls.Add(buttons);
            AcceptButton = save;
            CancelButton = cancel;

            EventHandler update = (s, e) => ShowTarget();
            _name.TextChanged += update;
            _number.TextChanged += update;
            _folder.TextChanged += update;
            _date.ValueChanged += update;
            _overwrite.CheckedChanged += update;
            ShowTarget();
        }

        private FieldNotesHeader CurrentHeader()
        {
            return new FieldNotesHeader
            {
                ProjectName = Clean(_name.Text),
                ProjectNumber = Clean(_number.Text),
                Crew = Clean(_crew.Text),
                ObservationDate = _date.Checked ? _date.Value.Date : (DateTime?)null
            };
        }

        private static string Clean(string text)
        {
            var t = (text ?? string.Empty).Trim();
            return t.Length == 0 ? null : t;
        }

        /// <summary>Shows the exact file the save will write, so a revision or a replacement is never a surprise.</summary>
        private void ShowTarget()
        {
            var folder = Clean(_folder.Text);
            if (folder == null)
            {
                _target.Text = "Save the drawing first, or browse to a folder: the field notes go under the drawing's project folder.";
                _target.ForeColor = DipBuilderForm.Warn;
                return;
            }
            var baseName = FieldNotesFiles.BaseName(CurrentHeader(), _prep.DrawingPath);
            IEnumerable<string> existing = new string[0];
            try { if (Directory.Exists(folder)) existing = Directory.GetFiles(folder, "*.pdf"); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (ArgumentException) { }
            var plan = FieldNotesFiles.Plan(folder, baseName, _overwrite.Checked, existing);
            _target.ForeColor = plan.Replaces ? DipBuilderForm.Warn : DipBuilderForm.Muted;
            _target.Text = plan.Replaces
                ? "Will REPLACE " + plan.FileName + " (revision R" + plan.Revision + ")."
                : plan.Supersedes != null
                    ? "Will save " + plan.FileName + " -- revision R" + plan.Revision + "; " + plan.Supersedes + " is kept."
                    : "Will save " + plan.FileName + ".";
        }

        private void OnBrowse(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog { Description = "Folder for the dip field notes PDF", ShowNewFolderButton = true })
            {
                var current = Clean(_folder.Text);
                try { if (current != null && Directory.Exists(current)) dialog.SelectedPath = current; }
                catch (ArgumentException) { }
                if (dialog.ShowDialog(this) == DialogResult.OK) _folder.Text = dialog.SelectedPath;
            }
        }

        private void OnSave(object sender, EventArgs e)
        {
            var ids = _list.CheckedItems.OfType<DipFieldNotesChoice>().Where(c => c.Complete).Select(c => c.StructureId).ToList();
            if (ids.Count == 0)
            {
                MessageBox.Show(this, "Tick at least one completed observation.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var folder = Clean(_folder.Text);
            if (folder == null)
            {
                MessageBox.Show(this, "Choose the folder for the PDF (or save the drawing first).", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (_overwrite.Checked && _target.Text.StartsWith("Will REPLACE", StringComparison.Ordinal) &&
                MessageBox.Show(this, _target.Text + "\n\nThe earlier field notes in that file will be lost. Replace it?", Text,
                                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;

            Header = CurrentHeader();
            SelectedIds = ids;
            Folder = folder;
            DialogResult = DialogResult.OK;
            Close();
        }

        private void SetAll(bool on)
        {
            for (var i = 0; i < _list.Items.Count; i++)
            {
                var choice = _list.Items[i] as DipFieldNotesChoice;
                _list.SetItemChecked(i, on && choice != null && choice.Complete);
            }
        }

        // ---------------------------------------------------------------- controls

        private static Label Hint(string text)
        {
            return new Label
            {
                AutoSize = true, MaximumSize = new Size(700, 0), ForeColor = DipBuilderForm.Muted, Font = DipBuilderForm.F(9.5f, false),
                Text = text, Margin = new Padding(0, 0, 0, 4)
            };
        }

        private static Label Caption(string text)
        {
            return new Label { AutoSize = true, Text = text, ForeColor = DipBuilderForm.Ink, Margin = new Padding(0, 7, 8, 0) };
        }

        private static TextBox Box(string text)
        {
            return new TextBox
            {
                Text = text ?? string.Empty, Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 3, 12, 6),
                BackColor = DipBuilderForm.Dark ? DipBuilderForm.Calculated : Color.White, ForeColor = DipBuilderForm.Ink
            };
        }

        private static FlowLayoutPanel Row()
        {
            return new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = DipBuilderForm.Ground, Margin = new Padding(0, 4, 0, 0) };
        }

        private static Button MakeButton(string text, EventHandler click, bool primary)
        {
            var b = new Button
            {
                Text = text, AutoSize = true, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand,
                BackColor = primary ? DipBuilderForm.Accent : DipBuilderForm.Surface, ForeColor = primary ? Color.White : DipBuilderForm.Ink,
                Padding = new Padding(8, 2, 8, 2), Margin = new Padding(0, 0, 8, 0)
            };
            b.FlatAppearance.BorderColor = primary ? DipBuilderForm.Accent : DipBuilderForm.ButtonBorder;
            b.Click += click;
            return b;
        }
    }
}
