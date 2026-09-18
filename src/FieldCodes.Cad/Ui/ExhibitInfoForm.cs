using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using FieldCodes.Exhibits;
using FieldCodes.Settings;

namespace FieldCodes.Cad.Ui
{
    /// <summary>
    /// Exhibit information, profile, scale and view orientation for FTFEXHIBIT. Nothing is
    /// required: blanks are simply left off the sheet.
    /// </summary>
    internal sealed class ExhibitInfoForm : Form
    {
        private readonly ExhibitCommands.ExhibitRequest _request;
        private readonly List<KeyValuePair<TextBox, Action<string>>> _fields = new List<KeyValuePair<TextBox, Action<string>>>();
        private readonly ComboBox _profile;
        private readonly ComboBox _scale;
        private readonly CheckBox _rotate;
        private readonly ComboBox _power;
        private readonly ComboBox _hatches;

        public ExhibitInfoForm(ExhibitCommands.ExhibitRequest request)
        {
            _request = request;
            DipBuilderForm.UseTheme(ThemePreference.LoadDark());
            Text = "Easement Exhibit";
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = false;
            BackColor = DipBuilderForm.Surface;
            ForeColor = DipBuilderForm.Ink;
            Font = DipBuilderForm.F(10f, false);
            AutoScroll = true;

            var stack = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true, Padding = new Padding(18, 14, 18, 8) };
            stack.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            stack.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 460));
            var heading = new Label { Text = "Exhibit information -- blanks are left off the sheet", AutoSize = true, Font = DipBuilderForm.F(12f, true), Margin = new Padding(0, 0, 0, 10) };
            stack.Controls.Add(heading, 0, 0);
            stack.SetColumnSpan(heading, 2);

            var info = request.Exhibit.Info;
            Field(stack, "Exhibit title", info.Title, v => info.Title = v);
            Field(stack, "Location", info.Location, v => info.Location = v);
            Field(stack, "Project", info.Project, v => info.Project = v);
            Field(stack, "Parcel", info.Parcel, v => info.Parcel = v);
            Field(stack, "Owner", info.Owner, v => info.Owner = v);
            Field(stack, "APN", info.Apn, v => info.Apn = v);
            Field(stack, "County", info.County, v => info.County = v);
            Field(stack, "Purpose", info.Purpose, v => info.Purpose = v);
            Field(stack, "Sheet", info.Sheet, v => info.Sheet = v);
            Field(stack, "Prepared by", info.PreparedBy, v => info.PreparedBy = v);
            Field(stack, "Date", info.Date, v => info.Date = v);
            Field(stack, "Project number", info.ProjectNumber, v => info.ProjectNumber = v);
            Field(stack, "Client", info.Client, v => info.Client = v);
            Field(stack, "Checked by", info.CheckedBy, v => info.CheckedBy = v);
            Field(stack, "Revision", info.Revision, v => info.Revision = v);

            _profile = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300 };
            _profile.Items.Add("(drawing settings)");
            foreach (var p in request.ProfileNames) _profile.Items.Add(p);
            _profile.SelectedItem = string.IsNullOrWhiteSpace(request.Profile) || !request.ProfileNames.Contains(request.Profile) ? "(drawing settings)" : request.Profile;
            Row(stack, "Exhibit profile", _profile);

            _scale = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 230 };
            _scale.Items.Add("Auto (largest that fits)");
            foreach (var s in request.Scales) _scale.Items.Add("1\" = " + s.ToString("0.##", CultureInfo.InvariantCulture) + "'");
            _scale.SelectedIndex = 0;
            Row(stack, "Scale", _scale);

            _rotate = new CheckBox { Text = "Turn the view when that allows a larger scale\n(the survey geometry itself is never rotated)", AutoSize = false, Size = new Size(450, 42) };
            Row(stack, "View", _rotate);

            // In this exhibit's viewport only; the profile's choice unless changed here.
            _power = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300 };
            _power.Items.AddRange(new object[] { "As the profile says", "Show", "Hide", "Leave as the drawing has it" });
            _power.SelectedIndex = Index(request.Exhibit.OverheadPower);
            Row(stack, "Overhead power", _power);
            _hatches = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300 };
            _hatches.Items.AddRange(new object[] { "As the profile says", "Show", "Hide", "Leave as the drawing has it", "Only where they belong to this exhibit" });
            _hatches.SelectedIndex = Index(request.Exhibit.OtherHatches);
            Row(stack, "Other exhibits' hatches", _hatches);

            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 12, 0, 0) };
            var ok = new Button { Text = "Create exhibit", AutoSize = true, FlatStyle = FlatStyle.Flat, BackColor = DipBuilderForm.Accent, ForeColor = Color.White, Padding = new Padding(10, 3, 10, 3) };
            ok.Click += (s, e) => Accept();
            var cancel = new Button { Text = "Cancel", AutoSize = true, FlatStyle = FlatStyle.Flat, DialogResult = DialogResult.Cancel, Padding = new Padding(10, 3, 10, 3), Margin = new Padding(0, 0, 8, 0) };
            buttons.Controls.Add(ok);
            buttons.Controls.Add(cancel);
            stack.Controls.Add(buttons);
            stack.SetColumnSpan(buttons, 2);
            AcceptButton = ok;
            CancelButton = cancel;

            Controls.Add(stack);
            var area = Screen.FromPoint(Cursor.Position).WorkingArea;
            ClientSize = new Size(Math.Min(660, area.Width - 40), Math.Min(stack.PreferredSize.Height + 10, area.Height - 60));
        }

        private void Field(TableLayoutPanel stack, string caption, string value, Action<string> set)
        {
            var box = new TextBox { Width = 450, Text = value ?? string.Empty, CharacterCasing = CharacterCasing.Upper, BorderStyle = BorderStyle.FixedSingle };
            Row(stack, caption, box);
            _fields.Add(new KeyValuePair<TextBox, Action<string>>(box, set));
        }

        private static readonly string[] Choices = { null, ExhibitSettings.Show, ExhibitSettings.Hide, ExhibitSettings.User, ExhibitSettings.Relevant };

        private static int Index(string choice)
        {
            var i = Array.FindIndex(Choices, c => c != null && string.Equals(c, choice, StringComparison.OrdinalIgnoreCase));
            return i < 0 ? 0 : i;
        }

        private static string Choice(int index)
        {
            return index > 0 && index < Choices.Length ? Choices[index] : null;
        }

        private static void Row(TableLayoutPanel stack, string caption, Control control)
        {
            stack.Controls.Add(new Label { Text = caption, AutoSize = true, Margin = new Padding(0, 6, 6, 4), ForeColor = DipBuilderForm.Muted });
            control.Margin = new Padding(0, 2, 0, 4);
            stack.Controls.Add(control);
        }

        private void Accept()
        {
            foreach (var f in _fields) f.Value(string.IsNullOrWhiteSpace(f.Key.Text) ? null : f.Key.Text.Trim());
            _request.Profile = _profile.SelectedIndex <= 0 ? null : (string)_profile.SelectedItem;
            _request.Scale = _scale.SelectedIndex <= 0 ? (double?)null : _request.Scales[_scale.SelectedIndex - 1];
            _request.AllowRotate = _rotate.Checked;
            _request.Exhibit.OverheadPower = Choice(_power.SelectedIndex);
            _request.Exhibit.OtherHatches = Choice(_hatches.SelectedIndex);
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
