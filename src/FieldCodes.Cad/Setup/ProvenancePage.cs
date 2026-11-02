using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using FieldCodes.Settings;

namespace FieldCodes.Cad.Setup
{
    /// <summary>
    /// Settings > Where Values Come From: every setting, the value in use, the level that
    /// supplies it (Built-in, Office, My defaults, Profile, Project) and what the other
    /// levels say. Inherit removes the selected value from its level so the level below
    /// shows through; Clear Level empties one level. The office level asks for its PIN.
    ///
    /// Shows the settings as last saved: edits not yet saved on other pages are not here.
    ///
    /// UNTESTED in Civil 3D: compiled against the API stand-ins only. The values, levels and
    /// inherit operations it shows come from SettingsLayers, which is unit tested.
    /// </summary>
    internal sealed class ProvenancePage : SetupPage
    {
        /// <summary>The label to register this page under in the Settings navigation.</summary>
        public const string NavLabel = "Where Values Come From";

        private sealed class Row
        {
            public SettingProvenance Provenance;
            public string Overridden;
            public string Search;
        }

        private static readonly string[] FilterNames =
        {
            "All levels", "Project", "Profile", "My defaults", "Office", "Built-in"
        };

        private static readonly SettingsLevel?[] FilterLevels =
        {
            null, SettingsLevel.Project, SettingsLevel.Profile, SettingsLevel.User, SettingsLevel.Office, SettingsLevel.BuiltIn
        };

        private readonly List<Row> _rows = new List<Row>();
        private Label _levels;
        private TextBox _search;
        private ComboBox _from;
        private CheckBox _onlyOverridden;
        private Button _inherit;
        private Button _clear;
        private ListView _list;
        private Label _status;
        private Font _bold;
        private IDictionary<string, SettingProvenance> _shown;
        private int _sortColumn;
        private bool _sortDescending;

        public override string Title { get { return NavLabel; } }
        public override string AffectedCommands { get { return "Every FTF command uses the values in force shown here"; } }
        public override bool IsReadOnly { get { return true; } }

        protected override void BuildBody() { }

        protected override Control BuildFilled()
        {
            var layout = new TableLayoutPanel { ColumnCount = 1, RowCount = 4 };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            _levels = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(900, 0),
                Margin = new Padding(0, 0, 0, 8)
            };
            layout.Controls.Add(_levels, 0, 0);

            var bar = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Dock = DockStyle.Fill, Margin = new Padding(0) };
            bar.Controls.Add(new Label { Text = "Search:", AutoSize = true, Margin = new Padding(0, 7, 4, 0) });
            _search = new TextBox { Width = 200, Tag = Ui.SettingsPanel.ViewOnly };
            _search.TextChanged += (s, e) => ShowRows();
            bar.Controls.Add(_search);

            bar.Controls.Add(new Label { Text = "From:", AutoSize = true, Margin = new Padding(10, 7, 4, 0) });
            _from = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120, Tag = Ui.SettingsPanel.ViewOnly };
            foreach (var name in FilterNames) _from.Items.Add(name);
            _from.SelectedIndex = 0;
            _from.SelectedIndexChanged += (s, e) => { ShowRows(); UpdateButtons(); };
            bar.Controls.Add(_from);

            _onlyOverridden = new CheckBox
            {
                Text = "Only values that override another level",
                AutoSize = true,
                Margin = new Padding(10, 5, 0, 0),
                Tag = Ui.SettingsPanel.ViewOnly
            };
            _onlyOverridden.CheckedChanged += (s, e) => ShowRows();
            bar.Controls.Add(_onlyOverridden);

            _inherit = new Button { Text = "Inherit", AutoSize = true, MinimumSize = new Size(84, 26), Margin = new Padding(14, 2, 0, 0) };
            _inherit.Click += (s, e) => InheritSelected();
            Tip(_inherit, "Stop setting the selected value at its level, so the level below supplies it.");
            bar.Controls.Add(_inherit);

            _clear = new Button { Text = "Clear Level...", AutoSize = true, MinimumSize = new Size(96, 26), Margin = new Padding(6, 2, 0, 0) };
            _clear.Click += (s, e) => ClearChosenLevel();
            Tip(_clear, "Remove every value the level chosen under From sets.");
            bar.Controls.Add(_clear);
            layout.Controls.Add(bar, 0, 1);

            _list = new ListView
            {
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                MultiSelect = false,
                HideSelection = false,
                ShowItemToolTips = true,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 6, 0, 4)
            };
            _list.Columns.Add("Setting", 330);
            _list.Columns.Add("Value in use", 170);
            _list.Columns.Add("From", 120);
            _list.Columns.Add("Overridden values", 330);
            _list.ColumnClick += (s, e) => SortBy(e.Column);
            _list.SelectedIndexChanged += (s, e) => UpdateButtons();
            _list.DoubleClick += (s, e) => InheritSelected();
            layout.Controls.Add(_list, 0, 2);

            _status = new Label
            {
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
                MaximumSize = new Size(900, 0),
                Margin = new Padding(0, 2, 0, 0),
                Text = "Highest level wins: Project, then Profile, then My defaults, then Office, then Built-in. " +
                       "My defaults sit below profiles and projects, so they never override a project's standard. " +
                       "Values are as last saved."
            };
            layout.Controls.Add(_status, 0, 3);
            return layout;
        }

        public override void LoadFrom(FtfSettings settings)
        {
            Fill();
        }

        public override void OnShown()
        {
            // Saving or discarding refreshes the resolution, which rebuilds its provenance;
            // show what is in force now, without refilling for nothing.
            var r = Context.Resolution;
            if (r == null || !ReferenceEquals(r.Provenance, _shown)) Fill();
        }

        public override void SaveTo(FtfSettings settings, ICollection<string> problems) { }
        public override void RestoreDefaults(FtfSettings settings) { }

        // ------------------------------------------------------------------ table

        private void Fill()
        {
            if (_list == null) return;
            var r = Context.Resolution;
            _levels.Text = DescribeLevels(r);

            _rows.Clear();
            _shown = r == null ? null : r.Provenance;
            if (r != null && r.Levels != null)
            {
                foreach (var p in r.Provenance.Values)
                {
                    var overridden = string.Join("; ", p.Overridden.Select(v => v.ToString()).ToArray());
                    _rows.Add(new Row
                    {
                        Provenance = p,
                        Overridden = overridden,
                        Search = (p.DisplayName + " " + p.Path + " " + p.ValueText + " " + p.FromName + " " + overridden)
                    });
                }
            }
            ShowRows();
            UpdateButtons();
        }

        private void ShowRows()
        {
            if (_list == null) return;
            var text = (_search.Text ?? string.Empty).Trim();
            var level = FilterLevels[Math.Max(0, _from.SelectedIndex)];

            _list.BeginUpdate();
            try
            {
                _list.ListViewItemSorter = null;
                _list.Items.Clear();
                foreach (var row in _rows)
                {
                    var p = row.Provenance;
                    if (level.HasValue && p.From != level.Value) continue;
                    if (_onlyOverridden.Checked && p.Overridden.Count == 0) continue;
                    if (text.Length > 0 && row.Search.IndexOf(text, StringComparison.OrdinalIgnoreCase) < 0) continue;

                    var item = new ListViewItem(new[]
                    {
                        p.DisplayName,
                        p.ValueText,
                        p.FromName,
                        row.Overridden
                    })
                    {
                        Tag = p,
                        ToolTipText = p.Path + Environment.NewLine + "In use: " + p.ValueText + " (" + p.FromName + ")" +
                                      (row.Overridden.Length > 0 ? Environment.NewLine + "Overrides: " + row.Overridden : string.Empty)
                    };
                    if (p.From != SettingsLevel.BuiltIn)
                    {
                        if (_bold == null) _bold = new Font(_list.Font, FontStyle.Bold);
                        item.Font = _bold;     // set by a level, not built in
                    }
                    _list.Items.Add(item);
                }
                _list.ListViewItemSorter = new ColumnSorter(_sortColumn, _sortDescending);
            }
            finally { _list.EndUpdate(); }
        }

        private void SortBy(int column)
        {
            if (column == _sortColumn) _sortDescending = !_sortDescending;
            else { _sortColumn = column; _sortDescending = false; }
            _list.ListViewItemSorter = new ColumnSorter(_sortColumn, _sortDescending);
            _list.Sort();
        }

        private sealed class ColumnSorter : IComparer
        {
            private readonly int _column;
            private readonly bool _descending;

            public ColumnSorter(int column, bool descending)
            {
                _column = column;
                _descending = descending;
            }

            public int Compare(object x, object y)
            {
                var a = Cell(x as ListViewItem);
                var b = Cell(y as ListViewItem);
                double da, db;
                int result;
                if (double.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out da) &&
                    double.TryParse(b, NumberStyles.Float, CultureInfo.InvariantCulture, out db))
                    result = da.CompareTo(db);
                else
                    result = string.Compare(a, b, StringComparison.CurrentCultureIgnoreCase);
                return _descending ? -result : result;
            }

            private string Cell(ListViewItem item)
            {
                if (item == null || _column >= item.SubItems.Count) return string.Empty;
                return item.SubItems[_column].Text ?? string.Empty;
            }
        }

        private SettingProvenance Selected()
        {
            return _list.SelectedItems.Count == 0 ? null : _list.SelectedItems[0].Tag as SettingProvenance;
        }

        private SettingsLevel? ChosenLevel()
        {
            return FilterLevels[Math.Max(0, _from.SelectedIndex)];
        }

        private void UpdateButtons()
        {
            if (_inherit == null) return;
            var selected = Selected();
            _inherit.Enabled = selected != null && selected.From != SettingsLevel.BuiltIn;

            var level = ChosenLevel();
            var info = level.HasValue && Context.Resolution != null ? Context.Resolution.LevelInfo(level.Value) : null;
            _clear.Enabled = info != null && info.Level != SettingsLevel.BuiltIn && info.Exists;
        }

        // ----------------------------------------------------------------- actions

        private void InheritSelected()
        {
            var p = Selected();
            var r = Context.Resolution;
            if (p == null || r == null) return;
            if (p.From == SettingsLevel.BuiltIn)
            {
                _status.Text = p.DisplayName + " is the built-in value already; no level sets it.";
                return;
            }
            if (!ReadyToChangeFiles()) return;

            var level = r.LevelInfo(p.From);
            var question = new StringBuilder();
            question.AppendLine("Stop setting \"" + p.DisplayName + "\" in " + p.FromName + "?");
            question.AppendLine();
            question.AppendLine("It is " + p.ValueText + " there. It would then be " +
                                SettingsLayers.Describe(p.InheritedValue) + ", from " + p.InheritsFromName + ".");
            question.AppendLine();
            question.Append("File: " + level.Path);
            if (level.Complete)
                question.Append(Environment.NewLine + Environment.NewLine +
                    "This is a complete settings file from an earlier version. It is first reduced to the values " +
                    "that differ from the levels below it, so those levels apply again.");
            if (MessageBox.Show(_list, question.ToString(), "Inherit", MessageBoxButtons.YesNo,
                                MessageBoxIcon.Question) != DialogResult.Yes) return;

            string pin = null, newPin;
            if (p.From == SettingsLevel.Office &&
                !Ui.PinDialog.AuthorizeOffice(_list, level.Path, false, out pin, out newPin)) return;

            if (!Change(() => SettingsLayers.Inherit(r, p.From, p.Path, pin))) return;
            var now = r.Provenance.ContainsKey(p.Path) ? r.Provenance[p.Path] : null;
            _status.Text = p.DisplayName + " is no longer set in " + p.FromName +
                           (now == null ? "." : "; it is now " + now.ValueText + ", from " + now.FromName + ".");
        }

        private void ClearChosenLevel()
        {
            var r = Context.Resolution;
            var level = ChosenLevel();
            if (r == null || !level.HasValue || level.Value == SettingsLevel.BuiltIn) return;
            var info = r.LevelInfo(level.Value);
            if (info == null || !info.Exists) return;
            if (!ReadyToChangeFiles()) return;

            var question = "Remove every value " + info.Name + " sets (" + info.ValueCount + ")?" +
                           Environment.NewLine + Environment.NewLine +
                           "Each then comes from the levels below. A copy is kept as " + Path.GetFileName(info.Path) + ".bak." +
                           Environment.NewLine + Environment.NewLine + "File: " + info.Path;
            if (MessageBox.Show(_list, question, "Clear Level", MessageBoxButtons.YesNo,
                                MessageBoxIcon.Warning) != DialogResult.Yes) return;

            string pin = null, newPin;
            if (level.Value == SettingsLevel.Office &&
                !Ui.PinDialog.AuthorizeOffice(_list, info.Path, false, out pin, out newPin)) return;

            if (!Change(() => SettingsLayers.ClearLevel(r, level.Value, pin))) return;
            _status.Text = info.Name + " no longer sets any value.";
        }

        /// <summary>Inherit and Clear write the files and then reload every page from them, so
        /// unsaved edits elsewhere in the window would be lost: they must be saved or
        /// discarded first.</summary>
        private bool ReadyToChangeFiles()
        {
            return NoUnsavedEdits(_list);
        }

        /// <summary>False, after saying so, when the Settings window holding
        /// <paramref name="from"/> has edits not yet saved.</summary>
        internal static bool NoUnsavedEdits(Control from)
        {
            for (var c = from; c != null; c = c.Parent)
            {
                var panel = c as Ui.SettingsPanel;
                if (panel == null) continue;
                if (!panel.HasUnsavedChanges) return true;
                MessageBox.Show(from,
                    "There are unsaved changes in this window. Save or discard them first (buttons at the bottom), " +
                    "then try again.",
                    "Unsaved changes", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }
            return true;
        }

        private bool Change(Func<bool> change)
        {
            try
            {
                change();
            }
            catch (ConfigException ex)
            {
                MessageBox.Show(_list, ex.Message, "Settings not changed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            catch (IOException ex)
            {
                MessageBox.Show(_list, ex.Message, "Settings not changed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            catch (UnauthorizedAccessException ex)
            {
                MessageBox.Show(_list, ex.Message, "Settings not changed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            FtfSession.InvalidateSettings();
            // Every page shows the values now in force; this page refills from LoadFrom.
            if (Context.Reload != null) Context.Reload();
            else Fill();
            return true;
        }

        // -------------------------------------------------------------- level text

        /// <summary>Each level, highest first: its file, how many values it sets, and its state.</summary>
        internal static string DescribeLevels(SettingsResolution r)
        {
            if (r == null || r.Levels == null) return "Settings were not resolved.";

            var sb = new StringBuilder();
            for (var i = r.Levels.Count - 1; i >= 0; i--)
            {
                var l = r.Levels[i];
                sb.Append(l.Name).Append(": ");
                if (l.Level == SettingsLevel.BuiltIn)
                {
                    sb.Append(r.MigratedFromRules ? "compiled into FTF, seeded from rules.json" : "compiled into FTF");
                }
                else if (l.Path == null)
                {
                    sb.Append(l.Level == SettingsLevel.Project ? "none (the drawing has not been saved)"
                            : l.Level == SettingsLevel.Profile ? "none selected"
                            : "none");
                }
                else
                {
                    sb.Append(l.Exists
                        ? l.ValueCount.ToString(CultureInfo.InvariantCulture) + (l.ValueCount == 1 ? " value" : " values")
                        : "no file");
                    sb.Append(" - ").Append(l.Path);
                    if (l.Exists && l.Complete)
                        sb.Append(" (complete file from an earlier version: the levels below it are not used until it is saved again)");
                    if (l.Exists && l.Shadowed)
                        sb.Append(" (not used: a complete file above hides it)");
                    if (l.Level == SettingsLevel.Office && l.Exists && l.PinProtected)
                        sb.Append(" (PIN protected)");
                }
                if (l.Level == SettingsLevel.Profile && l.Path != null && !l.Exists && !string.IsNullOrWhiteSpace(r.MissingProfile))
                    sb.Append(" (selected but not on this computer)");
                sb.AppendLine();
            }
            return sb.ToString().TrimEnd();
        }
    }
}
