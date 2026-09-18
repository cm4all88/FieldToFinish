using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using FieldCodes.Easements;

namespace FieldCodes.Cad.Ui
{
    /// <summary>
    /// The draft legal description beside the blanks it needs. The courses, ties, widths
    /// and areas come from the easement; the drafter names the parcel, corners and lines,
    /// and the draft updates as they type. Anything still blank shows in [BRACKETS] and in
    /// the checks below the text.
    /// </summary>
    internal sealed class LegalDescriptionForm : Form
    {
        private readonly EasementLegalCommand.LegalSession _session;
        private readonly TextBox _draft;
        private readonly Label _checks;
        private readonly List<KeyValuePair<EasementLegalCommand.LegalField, TextBox>> _boxes =
            new List<KeyValuePair<EasementLegalCommand.LegalField, TextBox>>();

        public LegalDescriptionForm(EasementLegalCommand.LegalSession session)
        {
            _session = session;
            DipBuilderForm.UseTheme(ThemePreference.LoadDark());
            Text = "Draft Legal Description";
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = false;
            MinimizeBox = false;
            BackColor = DipBuilderForm.Ground;
            ForeColor = DipBuilderForm.Ink;
            Font = DipBuilderForm.F(10.5f, false);
            var area = Screen.FromPoint(Cursor.Position).WorkingArea;
            ClientSize = new Size(Math.Min(1240, area.Width - 60), Math.Min(800, area.Height - 60));
            MinimumSize = new Size(Math.Min(900, area.Width), Math.Min(560, area.Height));

            var header = new Panel { Dock = DockStyle.Top, BackColor = DipBuilderForm.Surface, Padding = new Padding(20, 12, 20, 10) };
            var heading = new Label { AutoSize = true, Text = session.Easement.Title, Font = DipBuilderForm.F(15f, true), ForeColor = DipBuilderForm.Ink };
            var guide = new Label
            {
                AutoSize = true, ForeColor = DipBuilderForm.Muted, Margin = new Padding(1, 4, 0, 0),
                Text = "Courses, ties, widths and areas come from the easement. Name the parcel, corners and lines; the draft updates as you type."
            };
            var headerText = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Dock = DockStyle.Fill };
            headerText.Controls.Add(heading);
            headerText.Controls.Add(guide);
            header.Controls.Add(headerText);
            header.Height = heading.PreferredHeight + guide.PreferredHeight + header.Padding.Vertical + 8;

            // Left: the blanks.
            var left = new Panel { Dock = DockStyle.Left, Width = 440, BackColor = DipBuilderForm.Surface, Padding = new Padding(18, 14, 18, 14) };
            var stack = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
            var width = left.Width - left.Padding.Horizontal - 24;
            foreach (var field in session.Fields)
            {
                stack.Controls.Add(new Label
                {
                    AutoSize = true, Text = field.Caption.ToUpperInvariant(), ForeColor = DipBuilderForm.Muted,
                    Font = DipBuilderForm.F(8.5f, false), Margin = new Padding(0, 0, 0, 2)
                });
                var box = new TextBox
                {
                    Width = width, Multiline = field.Multiline, Height = field.Multiline ? 96 : 0, ScrollBars = field.Multiline ? ScrollBars.Vertical : ScrollBars.None,
                    Text = field.Get(session.Inputs) ?? string.Empty, CharacterCasing = CharacterCasing.Upper, BorderStyle = BorderStyle.FixedSingle,
                    BackColor = DipBuilderForm.Dark ? DipBuilderForm.Calculated : Color.White, ForeColor = DipBuilderForm.Ink,
                    Font = DipBuilderForm.F(10f, false), Margin = new Padding(0, 0, 0, 2)
                };
                box.TextChanged += (s, e) => Rewrite();
                stack.Controls.Add(box);
                stack.Controls.Add(new Label
                {
                    AutoSize = true, MaximumSize = new Size(width, 0), Text = "e.g. " + field.Hint, ForeColor = DipBuilderForm.Muted,
                    Font = DipBuilderForm.F(8.5f, false), Margin = new Padding(0, 0, 0, 12)
                });
                _boxes.Add(new KeyValuePair<EasementLegalCommand.LegalField, TextBox>(field, box));
            }
            left.Controls.Add(stack);

            // Right: the draft and what to check.
            var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(18, 14, 20, 14), BackColor = DipBuilderForm.Ground };
            _draft = new TextBox
            {
                Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, WordWrap = true,
                BorderStyle = BorderStyle.FixedSingle, BackColor = DipBuilderForm.PreviewBack, ForeColor = DipBuilderForm.Ink,
                Font = DipBuilderForm.F(10.5f, false)
            };
            _checks = new Label
            {
                Dock = DockStyle.Bottom, AutoSize = true, MaximumSize = new Size(700, 0), ForeColor = DipBuilderForm.Warn,
                Font = DipBuilderForm.F(9.5f, false), Padding = new Padding(0, 10, 0, 0)
            };
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 10, 0, 0) };
            var save = Btn("Save draft", true);
            save.Click += (s, e) => { Apply(); DialogResult = DialogResult.OK; Close(); };
            var copy = Btn("Copy text", false);
            copy.Click += (s, e) => { try { Clipboard.SetText(_draft.Text); } catch (System.Runtime.InteropServices.ExternalException) { } };
            var cancel = Btn("Cancel", false);
            cancel.DialogResult = DialogResult.Cancel;
            buttons.Controls.Add(save);
            buttons.Controls.Add(copy);
            buttons.Controls.Add(cancel);
            CancelButton = cancel;
            right.Controls.Add(_draft);
            right.Controls.Add(_checks);
            right.Controls.Add(buttons);

            Controls.Add(right);
            Controls.Add(left);
            Controls.Add(header);
            Rewrite();
        }

        private static Button Btn(string text, bool primary)
        {
            var b = new Button
            {
                Text = text, AutoSize = true, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand,
                BackColor = primary ? DipBuilderForm.Accent : DipBuilderForm.Surface, ForeColor = primary ? Color.White : DipBuilderForm.Ink,
                Font = DipBuilderForm.F(10f, false), Padding = new Padding(10, 3, 10, 3), Margin = new Padding(8, 0, 0, 0)
            };
            b.FlatAppearance.BorderColor = primary ? DipBuilderForm.Accent : DipBuilderForm.ButtonBorder;
            b.FlatAppearance.MouseOverBackColor = primary ? DipBuilderForm.AccentHover : DipBuilderForm.AccentSoft;
            return b;
        }

        private void Apply()
        {
            foreach (var pair in _boxes)
                pair.Key.Set(_session.Inputs, string.IsNullOrWhiteSpace(pair.Value.Text) ? null : pair.Value.Text.Trim());
        }

        private void Rewrite()
        {
            if (_draft == null) return;
            Apply();
            var draft = _session.Write(_session.Inputs);
            _draft.Text = draft.Text.Replace("\r\n", "\n").Replace("\n", "\r\n");
            _checks.Text = draft.Checks.Count == 0 ? "Nothing left blank." : "Check before use:\n" + string.Join("\n", draft.Checks.Select(c => "- " + c).ToArray());
            _checks.ForeColor = draft.Checks.Count == 0 ? DipBuilderForm.Good : DipBuilderForm.Warn;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                var on = DipBuilderForm.Dark ? 1 : 0;
                DwmSetWindowAttribute(Handle, 20, ref on, sizeof(int));
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    }
}
