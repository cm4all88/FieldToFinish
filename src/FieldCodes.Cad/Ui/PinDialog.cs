using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using FieldCodes.Settings;

namespace FieldCodes.Cad.Ui
{
    /// <summary>
    /// Asks for the office PIN, or for a new one, before the office standard is changed.
    /// The PIN typed here is only ever compared with, or turned into, the salted hash in
    /// the office file (OfficeAuthorization); it is never written anywhere.
    ///
    /// UNTESTED in Civil 3D: compiled against the API stand-ins only.
    /// </summary>
    internal static class PinDialog
    {
        /// <summary>The PIN typed, or null when cancelled.</summary>
        public static string Ask(IWin32Window owner, string title, string prompt)
        {
            return Show(owner, title, prompt, false);
        }

        /// <summary>A new PIN, typed twice, or null when cancelled.</summary>
        public static string Create(IWin32Window owner)
        {
            return Show(owner, "Set office PIN",
                "Choose an office PIN (at least " + OfficeAuthorization.MinimumPinLength + " characters). " +
                "Changing the office standard will then ask for it. Only a salted hash is stored in the " +
                "office file, so FTF cannot show the PIN again. If it is forgotten, someone who can edit " +
                "the plugin folder removes the \"admin\" section from ftf-settings.json there.", true);
        }

        /// <summary>
        /// The checks before the office standard changes: its folder must be writable from this
        /// computer, and a PIN, when the office file has one, must be entered correctly. With
        /// no PIN set, <paramref name="offerNewPin"/> offers to set one (optional). False means
        /// stop: the person has been told why.
        /// </summary>
        public static bool AuthorizeOffice(IWin32Window owner, string officePath, bool offerNewPin,
                                           out string pin, out string newPin)
        {
            pin = null;
            newPin = null;

            var problem = OfficeAuthorization.WriteProblem(officePath);
            if (problem != null)
            {
                MessageBox.Show(owner,
                    "The office standard cannot be changed from this computer:" + Environment.NewLine +
                    Environment.NewLine + problem + Environment.NewLine + Environment.NewLine +
                    "Save to the project or to My defaults instead, or ask whoever looks after the FTF install.",
                    "Office standard", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            bool protectedByPin;
            try { protectedByPin = OfficeAuthorization.HasPin(officePath); }
            catch (ConfigException ex)
            {
                MessageBox.Show(owner, ex.Message, "Office standard", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            catch (IOException ex)
            {
                MessageBox.Show(owner, ex.Message, "Office standard", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            if (protectedByPin)
            {
                var typed = Ask(owner, "Office standard",
                    "The office standard is protected. Enter the office PIN to change it.");
                if (typed == null) return false;
                if (!OfficeAuthorization.VerifyPin(officePath, typed))
                {
                    MessageBox.Show(owner, "That is not the office PIN. Nothing was changed.", "Office standard",
                                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }
                pin = typed;
                return true;
            }

            if (offerNewPin)
            {
                var answer = MessageBox.Show(owner,
                    "The office standard has no PIN, so anyone who can write to the plugin folder can change it " +
                    "from this window." + Environment.NewLine + Environment.NewLine +
                    "Set an office PIN now? (Optional: No saves without one.)",
                    "Office standard", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (answer == DialogResult.Cancel) return false;
                if (answer == DialogResult.Yes)
                {
                    newPin = Create(owner);
                    if (newPin == null) return false;
                }
            }
            return true;
        }

        private static string Show(IWin32Window owner, string title, string prompt, bool confirm)
        {
            using (var form = new Form
            {
                Text = title,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                MinimizeBox = false,
                MaximizeBox = false,
                ShowInTaskbar = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(12)
            })
            {
                var grid = new TableLayoutPanel
                {
                    ColumnCount = 2,
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    Dock = DockStyle.Fill
                };
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

                var text = new Label
                {
                    Text = prompt,
                    AutoSize = true,
                    MaximumSize = new Size(380, 0),
                    Margin = new Padding(0, 0, 0, 10)
                };
                grid.Controls.Add(text, 0, 0);
                grid.SetColumnSpan(text, 2);

                var first = new TextBox { UseSystemPasswordChar = true, Width = 180, Margin = new Padding(0, 3, 0, 3) };
                grid.Controls.Add(new Label { Text = "PIN:", AutoSize = true, Margin = new Padding(0, 6, 10, 0) }, 0, 1);
                grid.Controls.Add(first, 1, 1);

                TextBox second = null;
                if (confirm)
                {
                    second = new TextBox { UseSystemPasswordChar = true, Width = 180, Margin = new Padding(0, 3, 0, 3) };
                    grid.Controls.Add(new Label { Text = "Again:", AutoSize = true, Margin = new Padding(0, 6, 10, 0) }, 0, 2);
                    grid.Controls.Add(second, 1, 2);
                }

                var error = new Label
                {
                    Text = string.Empty,
                    AutoSize = true,
                    ForeColor = Color.FromArgb(170, 30, 30),
                    MaximumSize = new Size(380, 0),
                    Margin = new Padding(0, 4, 0, 4)
                };
                grid.Controls.Add(error, 0, 3);
                grid.SetColumnSpan(error, 2);

                var ok = new Button { Text = "OK", AutoSize = true, MinimumSize = new Size(80, 26) };
                var cancel = new Button { Text = "Cancel", AutoSize = true, MinimumSize = new Size(80, 26), DialogResult = DialogResult.Cancel };
                var buttons = new FlowLayoutPanel
                {
                    FlowDirection = FlowDirection.RightToLeft,
                    AutoSize = true,
                    Dock = DockStyle.Fill,
                    Margin = new Padding(0, 6, 0, 0)
                };
                buttons.Controls.Add(cancel);
                buttons.Controls.Add(ok);
                grid.Controls.Add(buttons, 0, 4);
                grid.SetColumnSpan(buttons, 2);

                ok.Click += (s, e) =>
                {
                    var pin = first.Text ?? string.Empty;
                    string problem = null;
                    if (confirm)
                    {
                        problem = OfficeAuthorization.PinProblem(pin);
                        if (problem == null && !string.Equals(pin, second.Text ?? string.Empty, StringComparison.Ordinal))
                            problem = "The two PINs are not the same.";
                    }
                    else if (pin.Length == 0)
                    {
                        problem = "Enter the PIN.";
                    }

                    if (problem != null)
                    {
                        error.Text = problem;
                        return;
                    }
                    form.DialogResult = DialogResult.OK;
                };

                form.Controls.Add(grid);
                form.AcceptButton = ok;
                form.CancelButton = cancel;

                return form.ShowDialog(owner) == DialogResult.OK ? first.Text : null;
            }
        }
    }
}
