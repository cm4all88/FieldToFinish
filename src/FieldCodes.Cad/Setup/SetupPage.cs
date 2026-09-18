using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using FieldCodes.Settings;

namespace FieldCodes.Cad.Setup
{
    /// <summary>Everything a page needs to build itself.</summary>
    internal sealed class SetupContext
    {
        public DrawingResources Drawing { get; set; }
        public RulesConfig Rules { get; set; }
        public string RulesPath { get; set; }
        public string RulesError { get; set; }
        public SettingsResolution Resolution { get; set; }

        /// <summary>Which configuration level the rules came from, and where an edit
        /// would be saved. Never the factory file.</summary>
        public FieldCodes.Editing.RulesResolution RulesResolution { get; set; }
    }

    /// <summary>
    /// One tab of the setup window.
    ///
    /// Rows go into a TableLayoutPanel that sizes itself from its contents. Nothing
    /// here positions a control by pixel: an earlier version did, and it clipped its
    /// own captions, drew one list on top of another, and fell apart at display
    /// scaling above 100%.
    ///
    /// Adding a tab for a new command means writing one of these and adding it to the
    /// list in SetupForm. The form itself knows nothing about any individual page.
    /// </summary>
    internal abstract class SetupPage
    {
        /// <summary>Widest a wrapping note may run before it folds.</summary>
        protected const int NoteWidth = 540;

        protected SetupContext Context { get; private set; }
        private TableLayoutPanel _grid;
        private int _row;

        public abstract string Title { get; }
        public abstract string AffectedCommands { get; }

        /// <summary>Read-only pages get no Restore Defaults button.</summary>
        public virtual bool IsReadOnly { get { return false; } }

        public Control Build(SetupContext context)
        {
            Context = context;

            var scroll = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(14, 12, 14, 14)
            };

            _grid = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                GrowStyle = TableLayoutPanelGrowStyle.AddRows
            };
            _grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            _row = 0;

            var caption = new Label
            {
                Text = "Affects: " + AffectedCommands,
                ForeColor = SystemColors.GrayText,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 10)
            };
            AddFullWidth(caption);

            BuildBody();

            scroll.Controls.Add(_grid);
            return scroll;
        }

        protected abstract void BuildBody();

        /// <summary>Pull current values into the controls.</summary>
        public abstract void LoadFrom(FtfSettings settings);

        /// <summary>Push control values back, adding a message per invalid entry.</summary>
        public abstract void SaveTo(FtfSettings settings, ICollection<string> problems);

        /// <summary>Reset just this page's section, then reload the controls.</summary>
        public abstract void RestoreDefaults(FtfSettings settings);

        /// <summary>Recompute any derived text after an edit. Optional.</summary>
        public virtual void Refresh() { }

        // ------------------------------------------------------------ row builders

        protected Label Heading(string text)
        {
            var label = new Label
            {
                Text = text,
                Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
                AutoSize = true,
                Margin = new Padding(0, _row == 1 ? 0 : 14, 0, 4)
            };
            AddFullWidth(label);
            return label;
        }

        /// <summary>
        /// Hides or shows a labelled row (the input and its caption). Auto-sized rows
        /// collapse when their controls are invisible, so a section that does not
        /// apply -- a dripline on a power pole -- simply is not there.
        /// </summary>
        protected void SetRowVisible(Control input, bool visible)
        {
            var position = _grid.GetPositionFromControl(input);
            var caption = _grid.GetControlFromPosition(0, position.Row);
            if (caption != null && caption != input) caption.Visible = visible;
            input.Visible = visible;
        }

        /// <summary>Grey explanatory text. Wraps rather than running off the edge.</summary>
        protected Label Note(string text)
        {
            var label = new Label
            {
                Text = text,
                ForeColor = SystemColors.GrayText,
                AutoSize = true,
                MaximumSize = new Size(NoteWidth, 0),
                Margin = new Padding(0, 4, 0, 6)
            };
            AddFullWidth(label);
            return label;
        }

        /// <summary>A grey line that gets rewritten as values change.</summary>
        protected Label LiveNote()
        {
            var label = new Label
            {
                Text = string.Empty,
                ForeColor = SystemColors.GrayText,
                AutoSize = true,
                MaximumSize = new Size(NoteWidth, 0),
                Margin = new Padding(0, 2, 0, 6)
            };
            AddFullWidth(label);
            return label;
        }

        protected TextBox TextRow(string caption, string hint, int width = 120)
        {
            var box = new TextBox { Width = width, Anchor = AnchorStyles.Left };
            AddLabelled(caption, box, hint);
            return box;
        }

        protected ComboBox ComboRow(string caption, IEnumerable<object> items, string hint,
                                    int width = 220)
        {
            var combo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = width,
                Anchor = AnchorStyles.Left
            };
            foreach (var item in items) combo.Items.Add(item);
            AddLabelled(caption, combo, hint);
            return combo;
        }

        protected CheckBox CheckRow(string caption, string hint)
        {
            var check = new CheckBox
            {
                Text = Escape(caption),
                AutoSize = true,
                Margin = new Padding(0, 3, 0, 3)
            };
            if (!string.IsNullOrEmpty(hint)) Tip(check, hint);
            AddFullWidth(check);
            return check;
        }

        protected Label ReadOnlyRow(string caption, string value)
        {
            var name = new Label
            {
                Text = Escape(caption),
                AutoSize = true,
                Margin = new Padding(0, 3, 14, 3)
            };
            var val = new Label
            {
                Text = Escape(value ?? string.Empty),
                AutoSize = true,
                MaximumSize = new Size(NoteWidth - 120, 0),
                Margin = new Padding(0, 3, 0, 3)
            };

            _grid.Controls.Add(name, 0, _row);
            _grid.Controls.Add(val, 1, _row);
            _row++;
            return val;
        }

        /// <summary>A control that spans both columns.</summary>
        protected T AddFullWidth<T>(T control) where T : Control
        {
            _grid.Controls.Add(control, 0, _row);
            _grid.SetColumnSpan(control, 2);
            _row++;
            return control;
        }

        /// <summary>A control with buttons stacked beside it.</summary>
        protected FlowLayoutPanel SideButtons(params Button[] buttons)
        {
            var flow = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(8, 0, 0, 0),
                WrapContents = false
            };

            foreach (var button in buttons)
            {
                button.AutoSize = true;
                button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                button.MinimumSize = new Size(84, 26);
                button.Margin = new Padding(0, 0, 0, 4);
                flow.Controls.Add(button);
            }

            return flow;
        }

        /// <summary>Puts a control in the left column and its buttons in the right.</summary>
        protected void AddWithSideButtons(Control control, FlowLayoutPanel buttons)
        {
            _grid.Controls.Add(control, 0, _row);
            _grid.Controls.Add(buttons, 1, _row);
            _row++;
        }

        private void AddLabelled(string caption, Control input, string hint)
        {
            var label = new Label
            {
                Text = Escape(caption),
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 6, 14, 3)
            };
            input.Margin = new Padding(0, 3, 0, 3);

            _grid.Controls.Add(label, 0, _row);
            _grid.Controls.Add(input, 1, _row);
            _row++;

            if (!string.IsNullOrEmpty(hint))
            {
                Tip(input, hint);
                Tip(label, hint);
            }
        }

        private static readonly ToolTip Tips = new ToolTip { AutoPopDelay = 20000 };

        protected static void Tip(Control control, string text)
        {
            Tips.SetToolTip(control, text);
        }

        /// <summary>
        /// Doubles ampersands. WinForms reads a single &amp; as a keyboard mnemonic, so
        /// "Tags &amp; Table" renders as "Tags Table" with a letter underlined.
        /// </summary>
        protected static string Escape(string text)
        {
            return string.IsNullOrEmpty(text) ? text : text.Replace("&", "&&");
        }

        // ------------------------------------------------------------ value helpers

        protected static string Fmt(double v)
        {
            return v.ToString("0.######", CultureInfo.InvariantCulture);
        }

        protected static bool ReadDouble(TextBox box, string name, Func<double, bool> valid,
                                         string requirement, ICollection<string> problems,
                                         ref double target)
        {
            double value;
            if (!double.TryParse((box.Text ?? string.Empty).Trim(),
                                 NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                problems.Add(name + " is not a number.");
                return false;
            }

            if (!valid(value))
            {
                problems.Add(name + " " + requirement + ".");
                return false;
            }

            target = value;
            return true;
        }

        protected static bool ReadInt(TextBox box, string name, Func<int, bool> valid,
                                      string requirement, ICollection<string> problems,
                                      ref int target)
        {
            int value;
            if (!int.TryParse((box.Text ?? string.Empty).Trim(),
                              NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                problems.Add(name + " is not a whole number.");
                return false;
            }

            if (!valid(value))
            {
                problems.Add(name + " " + requirement + ".");
                return false;
            }

            target = value;
            return true;
        }
    }
}
