using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
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

        /// <summary>Throws away edits: the settings are read again from where they came from.</summary>
        private void DiscardChanges()
        {
            if (MessageBox.Show(this, "Discard the changes you have not saved?", "Settings",
                                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            var fresh = string.IsNullOrWhiteSpace(_context.Resolution.LoadedFrom) || !File.Exists(_context.Resolution.LoadedFrom)
                ? new FtfSettings()
                : FtfSettings.Load(_context.Resolution.LoadedFrom);
            foreach (var prop in typeof(FtfSettings).GetProperties())
                if (prop.CanRead && prop.CanWrite &&
                    prop.GetCustomAttributes(typeof(Newtonsoft.Json.JsonPropertyAttribute), true).Length > 0)
                    prop.SetValue(_context.Resolution.Settings, prop.GetValue(fresh, null), null);
            ReloadAll();
            MarkClean();
            _log("Unsaved settings changes discarded.");
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
            // easement production, then administration. Group captions are visual only. Labels
            // are the navigation keys other pages use (SelectSection), so a rename keeps an alias.
            AddGroup("FIELDTOFINISH");
            AddSection("General", new GeneralPage());
            AddSection("Layers", new LayersPage());
            AddSection("Point Features", new PointFeaturesPage());
            AddSection("Symbols and Rotations", new SymbolsPage());
            AddSection("Trees", new TreesPage());
            AddSection("Point Labels", new LabelsPage());
            AddSection("Line Labels", new LineLabelsPage());
            AddSection("Label All", new LabelAllPage());
            AddSection("Tags", new TagsPage());
            AddSection("Schedule", new TablesPage());
            AddSection("Driplines", new DriplinesPage());
            AddSection("Drawing Order", new DrawOrderPage());

            AddGroup("SURVEY DRAFTING");
            AddSection("Spot Elevations", new SpotsPage());
            AddSection("Contours", new ContoursPage());
            AddSection("Drafting Lines", new DraftingLinesPage());
            AddSection("Sheets", new SheetsPage());
            AddSection("Utility Dips", new DipsPage());

            AddGroup("BOUNDARY AND EASEMENTS");
            AddSection("Right of Way", new RightOfWayPage());
            AddSection("Platted Blocks", new LotsPage());
            AddSection("Recorded Surveys", new RecordSurveyPage());
            AddSection("Survey Comparison", new ComparePage());
            AddSection("Strip Easements", new EasementsPage());
            AddSection("Easement Exhibits", new ExhibitsPage());

            AddGroup("ADMINISTRATION");
            AddSection("Profiles", new ProfilesPage());
            AddSection("Advanced Settings", new CleanupPage());

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

        private Control BuildFooter()
        {
            var footer = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                WrapContents = false,
                Padding = new Padding(6, 6, 6, 4)
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
                Width = 200
            };
            _saveTarget.Items.Add("My settings (all drawings)");
            _saveTarget.Items.Add("This drawing's folder");
            if (!string.IsNullOrWhiteSpace(_context.Resolution.ProfileName))
                _saveTarget.Items.Add("Drafting profile \"" + _context.Resolution.ProfileName + "\"");
            _saveTarget.SelectedIndex =
                _context.Resolution.Source == SettingsSource.DrawingFolder ? 1
                : _context.Resolution.Source == SettingsSource.Profile && _saveTarget.Items.Count > 2 ? 2 : 0;
            if (_saveTarget.Items.Count > 2) _saveTarget.Width = 260;
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
            _discard.Click += (s, e) => DiscardChanges();
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

            return footer;
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

            var path = TargetPath();
            if (path == null)
            {
                MessageBox.Show(this,
                    "This drawing has never been saved, so there is no folder to put " +
                    "settings in. Choose \"My settings\" instead, or save the drawing first.",
                    "Cannot save here", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                settings.Save(path);
                FtfSession.InvalidateSettings();
                MarkClean();
                _log("Settings saved to " + path);
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

        private string TargetPath()
        {
            if (_saveTarget.SelectedIndex == 2)
            {
                var profile = _context.Resolution.ProfileName;
                var path = FtfSettings.ProfilePath(profile);
                if (path != null) Directory.CreateDirectory(Path.GetDirectoryName(path));
                return path;
            }

            if (_saveTarget.SelectedIndex == 1)
            {
                var drawing = _context.Drawing != null ? _context.Drawing.DrawingPath : null;
                if (string.IsNullOrWhiteSpace(drawing)) return null;

                string dir;
                try { dir = Path.GetDirectoryName(drawing); }
                catch (ArgumentException) { return null; }

                return string.IsNullOrEmpty(dir) ? null : Path.Combine(dir, FtfSettings.FileName);
            }

            return FtfSettings.UserProfilePath();
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
