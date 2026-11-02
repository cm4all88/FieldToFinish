using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using FieldCodes.Settings;
using FieldCodes.Cad.Setup;

namespace FieldCodes.Cad.Ui
{
    /// <summary>
    /// The Settings page inside the FTF window -- the old setup dialog's pages,
    /// re-hosted so configuration feels like part of the same application instead of
    /// a separate legacy dialog.
    ///
    /// Nothing about the settings themselves changed: the same SetupPage classes,
    /// the same FtfSettings model, the same validation, the same save targets. This
    /// class only replaces the window they used to live in. One configuration
    /// system, one place to see it.
    ///
    /// Shown in Civil 3D by tests\LiveSmoke\rowlots-ui\run-rowlots-ui.ps1, which navigates to
    /// the Right of Way and Platted Blocks sections and checks each page builds, fills itself,
    /// offers only layers that are in the drawing and saves back what it shows. The other
    /// sections' pages are still only reached by hand.
    /// </summary>
    internal sealed class SettingsPanel : Panel
    {
        private sealed class Section
        {
            public string Label;
            public SetupPage Page;

            /// <summary>A non-selectable group caption in the nav, not a page.</summary>
            public bool IsGroup { get { return Page == null; } }
        }

        private readonly SetupContext _context;
        private readonly Action<string> _log;
        private readonly List<Section> _sections = new List<Section>();
        private readonly Dictionary<string, Control> _built =
            new Dictionary<string, Control>(StringComparer.Ordinal);

        private ListBox _nav;
        private Panel _host;
        private ComboBox _saveTarget;
        private Button _save;
        private Button _restore;
        private Button _discard;
        private Label _dirtyNote;
        private bool _dirty;

        /// <summary>True while any page has edits not yet saved.</summary>
        public bool HasUnsavedChanges { get { return _dirty; } }

        private bool _loading;

        /// <summary>Tag for a control that filters the view (search, filter) rather than editing a setting.</summary>
        public const string ViewOnly = "view-only";

        /// <summary>Any edit in a page's inputs marks the window unsaved -- every page, without
        /// each one having to remember to say so. Values set while a page loads do not count.</summary>
        private void WatchForEdits(Control root)
        {
            foreach (Control c in root.Controls)
            {
                // Search boxes and view filters change what is shown, not a setting.
                if (Equals(c.Tag, ViewOnly)) continue;
                var text = c as TextBoxBase;
                var check = c as CheckBox;
                var radio = c as RadioButton;
                var combo = c as ComboBox;
                var checkedList = c as CheckedListBox;
                var grid = c as DataGridView;
                var numeric = c as NumericUpDown;
                if (text != null && !text.ReadOnly) text.TextChanged += (s, e) => MarkDirty();
                else if (check != null) check.CheckedChanged += (s, e) => MarkDirty();
                else if (radio != null) radio.CheckedChanged += (s, e) => MarkDirty();
                else if (checkedList != null) checkedList.ItemCheck += (s, e) => MarkDirty();
                else if (combo != null) { combo.SelectedIndexChanged += (s, e) => MarkDirty(); combo.TextChanged += (s, e) => MarkDirty(); }
                else if (grid != null) grid.CellValueChanged += (s, e) => MarkDirty();
                else if (numeric != null) numeric.ValueChanged += (s, e) => MarkDirty();
                WatchForEdits(c);
            }
        }

        private void MarkDirty()
        {
            if (_loading) return;
            _dirty = true;
            if (_dirtyNote != null) _dirtyNote.Text = "Unsaved changes";
            if (_discard != null) _discard.Enabled = true;
        }

        private void MarkClean()
        {
            _dirty = false;
            if (_dirtyNote != null) _dirtyNote.Text = string.Empty;
            if (_discard != null) _discard.Enabled = false;
        }

        /// <summary>Every page reloads from the settings object (after an import).</summary>
        private void ReloadAll()
        {
            _loading = true;
            try
            {
                foreach (var section in _sections)
                {
                    Control built;
                    if (section.IsGroup || !_built.TryGetValue(section.Label, out built)) continue;
                    section.Page.LoadFrom(_context.Resolution.Settings);
                }
            }
            finally { _loading = false; }
        }

        public SettingsPanel(SetupContext context, Action<string> log)
        {
            _context = context;
            _log = log ?? delegate { };

            Dock = DockStyle.Fill;

            _context.Navigate = SelectSection;
            _context.Changed = MarkDirty;
            _context.Reload = ReloadAll;

            // Grouped by workflow: field-to-finish processing, survey drafting, boundary and
            // easement production, administration, then any group a newer page names. Group
            // captions are visual only. Labels are the navigation keys other pages use
            // (SelectSection), so a rename keeps an alias. Nothing is listed here: every page
            // registers itself with [SettingsPage(...)] (docs/settings-registry.md), and a page
            // for a new command appears in its group without this window changing.
            foreach (var entry in SettingsPages.Navigation())
            {
                if (entry.IsGroup) { AddGroup(entry.Caption); continue; }

                SetupPage page;
                try { page = SettingsPages.Create(entry.Page); }
                catch (Exception ex)
                {
                    // One page that cannot be made must not take the whole window with it.
                    _log("Settings: the " + entry.Page.Label + " page could not be created: " +
                         (ex.InnerException ?? ex).Message);
                    continue;
                }
                AddSection(entry.Page.Label, page);
                foreach (var alias in entry.Page.Aliases)
                    if (!Renamed.ContainsKey(alias)) Renamed[alias] = entry.Page.Label;
            }
            foreach (var problem in SettingsPages.Problems) _log("Settings registry: " + problem);

            BuildLayout();
            ShowSection(1);
        }

        private void AddSection(string label, SetupPage page)
        {
            _sections.Add(new Section { Label = label, Page = page });
        }

        private void AddGroup(string label)
        {
            _sections.Add(new Section { Label = label, Page = null });
        }

        private void BuildLayout()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 2
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _nav = new ListBox
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                BackColor = Color.FromArgb(248, 249, 251),
                IntegralHeight = false,
                ItemHeight = 26,
                DrawMode = DrawMode.OwnerDrawFixed
            };
            foreach (var section in _sections) _nav.Items.Add(section.Label);
            _nav.DrawItem += DrawNavItem;
            _nav.SelectedIndexChanged += (s, e) => ShowSection(_nav.SelectedIndex);
            root.Controls.Add(_nav, 0, 0);

            _host = new Panel { Dock = DockStyle.Fill };
            root.Controls.Add(_host, 1, 0);

            root.Controls.Add(BuildFooter(), 0, 1);
            root.SetColumnSpan(root.GetControlFromPosition(0, 1), 2);

            Controls.Add(root);
        }

        private void DrawNavItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;

            var section = _sections[e.Index];

            if (section.IsGroup)
            {
                using (var brush = new SolidBrush(Color.FromArgb(248, 249, 251)))
                    e.Graphics.FillRectangle(brush, e.Bounds);

                using (var small = new Font(e.Font.FontFamily, e.Font.Size - 1.5f,
                                            FontStyle.Bold))
                    TextRenderer.DrawText(e.Graphics, section.Label, small,
                        new Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 8,
                                      e.Bounds.Height),
                        Color.FromArgb(130, 135, 145),
                        TextFormatFlags.Bottom | TextFormatFlags.Left |
                        TextFormatFlags.NoPrefix);
                return;
            }

            var selected = (e.State & DrawItemState.Selected) != 0;
            var back = selected ? Color.White : Color.FromArgb(248, 249, 251);

            using (var brush = new SolidBrush(back))
                e.Graphics.FillRectangle(brush, e.Bounds);

            if (selected)
                using (var accent = new SolidBrush(Color.FromArgb(0, 90, 158)))
                    e.Graphics.FillRectangle(accent, e.Bounds.X, e.Bounds.Y, 3, e.Bounds.Height);

            var font = selected ? new Font(e.Font, FontStyle.Bold) : e.Font;
            TextRenderer.DrawText(e.Graphics, section.Label, font,
                new Rectangle(e.Bounds.X + 16, e.Bounds.Y, e.Bounds.Width - 16, e.Bounds.Height),
                SystemColors.ControlText,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left |
                TextFormatFlags.NoPrefix);
        }

        /// <summary>A level the Save button writes to.</summary>
        private sealed class SaveTarget
        {
            public SettingsLevel Level;
            public string Text;
            public override string ToString() { return Text; }
        }

        private Label _targetNote;

        /// <summary>
        /// Save target and buttons, with a line under them saying which level and file a
        /// save goes to. Levels, lowest first: built-in, office standard, my defaults,
        /// profile, project. Save writes only what changed, and each file keeps only what
        /// differs from the levels below it (SettingsLayers).
        /// </summary>
        private Control BuildFooter()
        {
            var outer = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(6, 6, 6, 4)
            };
            outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var footer = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                WrapContents = false,
                Margin = new Padding(0)
            };

            footer.Controls.Add(new Label
            {
                Text = "Save settings to:",
                AutoSize = true,
                Margin = new Padding(0, 7, 6, 0)
            });

            _saveTarget = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 290
            };
            _saveTarget.Items.Add(new SaveTarget { Level = SettingsLevel.Project, Text = "Project (this drawing's folder)" });
            var profile = _context.Resolution.ProfileName;
            if (!string.IsNullOrWhiteSpace(profile))
                _saveTarget.Items.Add(new SaveTarget { Level = SettingsLevel.Profile, Text = "Project profile \"" + profile + "\"" });
            _saveTarget.Items.Add(new SaveTarget { Level = SettingsLevel.User, Text = "My defaults (all drawings, below project)" });
            _saveTarget.Items.Add(new SaveTarget { Level = SettingsLevel.Office, Text = "Office standard (authorization required)" });

            // A saved drawing saves to its project by default; an unsaved one has no project
            // folder yet, so to the person's own defaults.
            var start = _context.Resolution.PathFor(SettingsLevel.Project) != null ? SettingsLevel.Project : SettingsLevel.User;
            for (var i = 0; i < _saveTarget.Items.Count; i++)
                if (((SaveTarget)_saveTarget.Items[i]).Level == start) _saveTarget.SelectedIndex = i;
            _saveTarget.SelectedIndexChanged += (s, e) => UpdateTargetNote();
            footer.Controls.Add(_saveTarget);

            _save = new Button
            {
                Text = "Save",
                AutoSize = true,
                MinimumSize = new Size(96, 28),
                Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
                Margin = new Padding(10, 2, 0, 0)
            };
            _save.Click += (s, e) => SaveAll();
            footer.Controls.Add(_save);

            _restore = new Button
            {
                Text = "Restore section defaults",
                AutoSize = true,
                MinimumSize = new Size(150, 28),
                Margin = new Padding(8, 2, 0, 0)
            };
            _restore.Click += (s, e) => RestoreCurrent();
            footer.Controls.Add(_restore);

            _discard = new Button
            {
                Text = "Discard Changes",
                AutoSize = true,
                MinimumSize = new Size(110, 28),
                Margin = new Padding(8, 2, 0, 0),
                Enabled = false
            };
            // Re-reads every level (not just the top file): with layered settings the top
            // file alone holds only that level's overrides.
            _discard.Click += (s, e) => DiscardToSaved();
            footer.Controls.Add(_discard);

            _dirtyNote = new Label
            {
                Text = string.Empty,
                AutoSize = true,
                ForeColor = Color.FromArgb(160, 90, 0),
                Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
                Margin = new Padding(12, 8, 0, 0)
            };
            footer.Controls.Add(_dirtyNote);

            if (_sections.Exists(x => !x.IsGroup && string.Equals(x.Label, ProvenancePage.NavLabel, StringComparison.OrdinalIgnoreCase)))
            {
                var where = new LinkLabel { Text = "Where values come from", AutoSize = true, Margin = new Padding(12, 8, 0, 0) };
                where.LinkClicked += (s, e) => SelectSection(ProvenancePage.NavLabel);
                footer.Controls.Add(where);
            }

            outer.Controls.Add(footer, 0, 0);

            _targetNote = new Label
            {
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
                MaximumSize = new Size(1000, 0),
                Margin = new Padding(2, 4, 0, 0)
            };
            outer.Controls.Add(_targetNote, 0, 1);
            UpdateTargetNote();

            return outer;
        }

        private SaveTarget SelectedTarget()
        {
            return _saveTarget == null ? null : _saveTarget.SelectedItem as SaveTarget;
        }

        /// <summary>Which level and file the next save goes to, and what that level means.</summary>
        private void UpdateTargetNote()
        {
            if (_targetNote == null) return;
            var target = SelectedTarget();
            if (target == null) { _targetNote.Text = string.Empty; return; }

            var path = TargetPath();
            string text;
            switch (target.Level)
            {
                case SettingsLevel.Project:
                    text = path == null
                        ? "Project: this drawing has not been saved, so there is no project folder yet."
                        : "Saves to Project: " + path + ". Keeps only this project's differences from the levels " +
                          "below it, and overrides them for drawings in this folder.";
                    break;
                case SettingsLevel.Profile:
                    text = "Saves to profile \"" + _context.Resolution.ProfileName + "\": " + path + ". Keeps only its " +
                           "differences from the office standard and your defaults; a project file still overrides it.";
                    break;
                case SettingsLevel.User:
                    text = "Saves to My defaults: " + path + ". Your own defaults for every drawing. They sit below " +
                           "profiles and project files, so they never override a project's standard.";
                    break;
                default:
                    text = "Saves to the Office standard: " + (path ?? "(plugin folder unknown)") + ". Written whole; " +
                           "asks for the office PIN when one is set. An FTF update replaces the plugin folder: keep a copy " +
                           "(docs\\settings-levels.md).";
                    break;
            }

            var above = 0;
            if (target.Level != SettingsLevel.Project)
                foreach (var p in _context.Resolution.Provenance.Values)
                    if (p.From > target.Level) above++;
            if (above > 0)
                text += " " + above + (above == 1 ? " value is" : " values are") +
                        " set by a higher level and stay in force for this drawing.";

            _targetNote.Text = text;
        }

        /// <summary>Throws away edits: every page goes back to the values in force, read again
        /// from every level's file.</summary>
        private void DiscardToSaved()
        {
            if (MessageBox.Show(this, "Discard the changes you have not saved?", "Settings",
                                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try
            {
                SettingsLayers.Refresh(_context.Resolution);
            }
            catch (ConfigException ex)
            {
                MessageBox.Show(this, ex.Message, "Settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            catch (IOException ex)
            {
                MessageBox.Show(this, ex.Message, "Settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            ReloadAll();
            MarkClean();
            UpdateTargetNote();
            _log("Unsaved settings changes discarded.");
        }

        /// <summary>Earlier names of sections, so links from other windows keep working.</summary>
        private static readonly Dictionary<string, string> Renamed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Trees & Driplines", "Trees" },
            { "Spot Shots", "Spot Elevations" },
            { "Storm & Sewer Dips", "Utility Dips" },
            { "Advanced", "Advanced Settings" },
            { "Cleanup & Re-run", "Advanced Settings" },
            { "Draw Order", "Drawing Order" },
            { "Labels", "Point Labels" },
            { "Tags & Table", "Tags" },
            { "Drip Lines", "Driplines" }
        };

        /// <summary>Navigate to a section by its label -- used by other pages that
        /// send the user here ("Configure Rules..." lands on Point Features).</summary>
        public void SelectSection(string label)
        {
            string now;
            if (label != null && Renamed.TryGetValue(label, out now)) label = now;
            for (var i = 0; i < _sections.Count; i++)
            {
                if (_sections[i].IsGroup) continue;
                if (string.Equals(_sections[i].Label, label, StringComparison.OrdinalIgnoreCase))
                {
                    ShowSection(i);
                    return;
                }
            }
        }

        // ------------------------------------------------------------------ paging

        private int _lastIndex = -1;

        private void ShowSection(int index)
        {
            if (index < 0 || index >= _sections.Count) return;

            // Group captions cannot be a destination: arriving on one slides the
            // selection onward in the direction the user was moving.
            if (_sections[index].IsGroup)
            {
                var next = index > _lastIndex ? index + 1 : index - 1;
                if (next < 0 || next >= _sections.Count) next = _lastIndex;
                ShowSection(next);
                return;
            }

            if (_nav.SelectedIndex != index) { _nav.SelectedIndex = index; return; }
            _lastIndex = index;

            var section = _sections[index];

            Control control;
            if (!_built.TryGetValue(section.Label, out control))
            {
                _loading = true;
                try
                {
                    control = section.Page.Build(_context);
                    control.Dock = DockStyle.Fill;
                    section.Page.LoadFrom(_context.Resolution.Settings);
                }
                finally { _loading = false; }
                WatchForEdits(control);
                _built[section.Label] = control;
                _host.Controls.Add(control);
            }

            foreach (Control child in _host.Controls) child.Visible = false;
            control.Visible = true;
            section.Page.OnShown();

            _restore.Enabled = !section.Page.IsReadOnly;
        }

        // ------------------------------------------------------------------ saving

        /// <summary>
        /// Saves what changed in the window to the chosen level. Only the changes go to that
        /// level -- never values that came from other levels -- and the level's file keeps
        /// only what differs from what it inherits. The office standard asks for its PIN
        /// (or offers to set one) and needs a writable plugin folder.
        /// </summary>
        private void SaveAll()
        {
            var settings = _context.Resolution.Settings;
            var problems = new List<string>();

            // Only pages that have been built have edits worth collecting; the rest
            // still hold the loaded values.
            foreach (var section in _sections)
                if (!section.IsGroup && _built.ContainsKey(section.Label))
                    section.Page.SaveTo(settings, problems);

            settings.Validate(problems);

            if (problems.Count > 0)
            {
                MessageBox.Show(this,
                    string.Join(Environment.NewLine, problems.ToArray()),
                    "Settings not saved", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var target = SelectedTarget();
            var path = TargetPath();
            if (target == null || path == null)
            {
                MessageBox.Show(this,
                    target != null && target.Level == SettingsLevel.Office
                        ? "FTF cannot tell which folder it was loaded from, so there is no office standard file to save to."
                        : "This drawing has never been saved, so there is no project folder to put settings in. " +
                          "Choose \"My defaults\" instead, or save the drawing and open Settings again.",
                    "Cannot save here", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string pin = null, newPin = null;
            if (target.Level == SettingsLevel.Office)
            {
                if (MessageBox.Show(this,
                        "Save your changes to the OFFICE STANDARD?" + Environment.NewLine + Environment.NewLine + path +
                        Environment.NewLine + Environment.NewLine +
                        "Every drawing that does not set these values in its project, its profile or the user's own " +
                        "defaults will use them.",
                        "Office standard", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
                    return;
                if (!PinDialog.AuthorizeOffice(this, path, true, out pin, out newPin)) return;
            }

            try
            {
                var result = SettingsLayers.SaveChanges(_context.Resolution, target.Level, settings, pin, newPin);
                FtfSession.InvalidateSettings();
                ReloadAll();        // every page shows the values now in force
                MarkClean();
                UpdateTargetNote();
                _log(result.Summary());

                if (result.Hidden.Count > 0)
                {
                    const int listed = 15;
                    var lines = result.Hidden.Take(listed).ToList();
                    if (result.Hidden.Count > listed) lines.Add("... and " + (result.Hidden.Count - listed) + " more");
                    MessageBox.Show(this,
                        "Saved to " + result.LevelName + ", but a higher level sets these, so this drawing keeps " +
                        "the higher level's value:" + Environment.NewLine + Environment.NewLine +
                        string.Join(Environment.NewLine, lines.ToArray()),
                        "Saved, but not in force here", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (ConfigException ex)
            {
                MessageBox.Show(this, ex.Message, "Settings not saved",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (IOException ex)
            {
                MessageBox.Show(this, "Could not write " + path + ":" +
                                Environment.NewLine + ex.Message,
                                "Settings not saved", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (UnauthorizedAccessException ex)
            {
                MessageBox.Show(this, "Could not write " + path + ":" +
                                Environment.NewLine + ex.Message,
                                "Settings not saved", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>The file the chosen level reads and saves: beside the drawing, the
        /// profile, %APPDATA%, or the plugin folder. Null when the level has none (a drawing
        /// that was not saved when Settings opened).</summary>
        private string TargetPath()
        {
            var target = SelectedTarget();
            return target == null ? null : _context.Resolution.PathFor(target.Level);
        }

        private void RestoreCurrent()
        {
            var index = _nav.SelectedIndex;
            if (index < 0) return;

            var section = _sections[index];
            if (section.IsGroup || section.Page.IsReadOnly) return;

            var answer = MessageBox.Show(this,
                "Reset the " + section.Label + " section to its shipped defaults?",
                "Restore defaults", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);

            if (answer == DialogResult.OK)
            {
                _loading = true;
                try { section.Page.RestoreDefaults(_context.Resolution.Settings); }
                finally { _loading = false; }
                MarkDirty();      // restored in the window; Save writes it
            }
        }
    }
}
