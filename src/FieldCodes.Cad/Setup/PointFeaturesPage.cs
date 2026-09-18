using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using FieldCodes.Editing;
using FieldCodes.Settings;

namespace FieldCodes.Cad.Setup
{
    /// <summary>
    /// The Point Features section: the configured feature rules, a prominent live
    /// preview, and the editor for the safe subset of each rule.
    ///
    /// The editor works on a RuleDocument -- the rules file as a JSON DOM -- so
    /// saving preserves every comment, unknown property and unknown rule. Saves go
    /// where the configuration hierarchy says: the office configuration (created on
    /// first save), or a drawing override when one is active -- never the factory
    /// file. It edits finishing only; it cannot create blocks, symbols or Civil 3D
    /// geometry of any kind.
    ///
    /// Advanced internals -- match patterns, species maps, modifier definitions,
    /// rotation configuration -- are deliberately read-only until their UI is
    /// designed on purpose.
    ///
    /// UNTESTED as UI; the document, validation, save and preview behaviour are
    /// unit tested.
    /// </summary>
    internal sealed class PointFeaturesPage : SetupPage
    {
        private ListView _grid;
        private RuleDocument _document;
        private RulesConfig _candidate;
        private RulesResolution _resolution;
        private Label _sourceLine;
        private Button _restoreFactory;
        private bool _dirty;

        // preview
        private TextBox _testInput;
        private TextBox _preview;

        // identity
        private Label _identity;
        private TextBox _description;
        private CheckBox _enabled;

        // label finishing
        private TextBox _labelFormat;
        private ComboBox _labelLayer;
        private ComboBox _leader;

        // rotation / tags / dripline
        private Label _rotationRow;
        private TextBox _tagPrefix;
        private Label _dripHeading;
        private ComboBox _dripLayer;

        private Label _symbolRow;
        private Label _problems;

        public override string Title { get { return "Point Features"; } }
        public override string AffectedCommands { get { return "FTFPOINTS, FTFLABELS, FTFTAGS"; } }

        // The rules file has its own save path with its own buttons; the settings
        // footer must not imply it can restore this page.
        public override bool IsReadOnly { get { return true; } }

        protected override void BuildBody()
        {
            Heading("Configured features");
            Note("Civil 3D creates the point, symbol and linework. FTF finishes the " +
                 "existing Civil 3D result -- it never creates survey geometry, and " +
                 "never inserts a symbol unless a rule explicitly and safely opts in.");

            _sourceLine = LiveNote();

            _grid = new ListView
            {
                View = View.Details,
                FullRowSelect = true,
                HideSelection = false,
                MultiSelect = false,
                Width = 560,
                Height = 138
            };
            _grid.Columns.Add("Code", 70);
            _grid.Columns.Add("Feature", 120);
            _grid.Columns.Add("Label", 140);
            _grid.Columns.Add("Tag", 40);
            _grid.Columns.Add("Layer", 120);
            _grid.Columns.Add("On", 38);
            _grid.SelectedIndexChanged += (s, e) => LoadSelected();
            AddFullWidth(_grid);

            // ---- preview: deliberately right under the grid, before the editor ----

            Heading("Try a description");
            _testInput = TextRow("Description",
                "Parsed against the staged rules, so Apply changes this preview. " +
                "Read-only with respect to the drawing.", 260);

            _preview = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Width = 560,
                Height = 132,
                BackColor = SystemColors.Window,
                Font = new Font("Consolas", 9f)
            };
            AddFullWidth(_preview);
            _testInput.TextChanged += (s, e) => RunPreview();

            // ------------------------------- identity ------------------------------

            Heading("Identity");
            _identity = AddFullWidth(new Label
            {
                Text = "(select a feature above)",
                Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
                AutoSize = true
            });
            _description = TextRow("Feature name", "Shown in grids and previews.", 240);
            _enabled = CheckRow("Enabled",
                "A disabled feature stops matching; its codes report as unknown again.");

            Heading("Civil 3D ownership");
            _symbolRow = ReadOnlyRow("Symbol", "-");

            // -------------------------- label finishing ---------------------------

            Heading("Label finishing");
            _labelFormat = TextRow("Label text", "Text with {placeholders} from the pattern.", 240);
            _labelLayer = ComboRow("Label layer", LayerChoices(), null);
            _leader = ComboRow("Leader", new object[] { "Auto", "Always", "Never" },
                "Auto draws a leader only when the label cannot sit next to the point.");

            Heading("Rotation");
            _rotationRow = ReadOnlyRow("Behaviour", "-");

            Heading("Tags");
            _tagPrefix = TextRow("Prefix", "Blank disables tagging. Letters only.", 60);
            Note("Tagged features are numbered on placement, placed beside the point " +
                 "with collision avoidance, and included in the schedule. Numbers are " +
                 "never reissued.");

            _dripHeading = Heading("Dripline");
            _dripLayer = ComboRow("Dripline layer", LayerChoices(), null);

            Heading("Other finishing");
            Note(ModifierSummary());

            // ------------------------------- actions -------------------------------

            _problems = AddFullWidth(new Label
            {
                Text = string.Empty,
                ForeColor = Color.FromArgb(170, 0, 0),
                AutoSize = true,
                MaximumSize = new Size(560, 0)
            });

            var apply = new Button { Text = "Apply" };
            var cancel = new Button { Text = "Cancel" };
            var save = new Button { Text = "Save Rules" };
            var reload = new Button { Text = "Reload" };
            _restoreFactory = new Button { Text = "Restore Factory Defaults" };

            apply.Click += (s, e) => ApplyClicked();
            cancel.Click += (s, e) => LoadSelected();
            save.Click += (s, e) => SaveClicked();
            reload.Click += (s, e) => ReloadClicked(true);
            _restoreFactory.Click += (s, e) => RestoreFactoryClicked();

            AddWithSideButtons(new Label
            {
                Text = "Apply stages the change; Save Rules writes the active " +
                       "configuration (atomic, with a backup). Reload discards unsaved " +
                       "edits. Restore Factory Defaults replaces the office " +
                       "configuration with the shipped rules -- a separate, deliberate act.",
                ForeColor = SystemColors.GrayText,
                AutoSize = true,
                MaximumSize = new Size(280, 0)
            }, SideButtons(apply, cancel, save, reload, _restoreFactory));

            ReloadClicked(false);
        }

        private string ModifierSummary()
        {
            if (Context.Rules == null || Context.Rules.Modifiers.Count == 0)
                return "No modifiers configured.";

            var parts = Context.Rules.Modifiers
                .OrderBy(m => m.Priority)
                .Select(m => m.Id.ToUpperInvariant() +
                             (string.IsNullOrEmpty(m.LayerSuffix) ? "" : " (" + m.LayerSuffix + ")"))
                .ToArray();

            return "Field modifiers apply to every feature automatically: " +
                   string.Join(", ", parts) + ". They adjust the label and suffix the " +
                   "layers, in priority order regardless of where they appear in the " +
                   "description. Defined in the rules file.";
        }

        private object[] LayerChoices()
        {
            var items = new List<object>();
            foreach (var name in Context.Drawing.Layers) items.Add(name);
            return items.ToArray();
        }

        // ------------------------------------------------------------------ loading

        /// <summary>
        /// Reload discards unsaved edits and re-reads the ACTIVE configuration. It
        /// never restores factory defaults -- that is a separate, explicit button.
        /// </summary>
        private void ReloadClicked(bool announce)
        {
            _resolution = Context.RulesResolution == null
                ? null
                : RulesStore.Resolve(Context.RulesResolution.DrawingDirectory,
                                     Context.RulesResolution.PluginDirectory);

            var active = _resolution != null ? _resolution.ActivePath : Context.RulesPath;

            try
            {
                _document = string.IsNullOrEmpty(active) ? null : RuleDocument.Load(active);
            }
            catch (ConfigException ex)
            {
                _problems.Text = ex.Message;
                _document = null;
            }

            _dirty = false;
            ShowSource();
            RefreshCandidate();
            PopulateGrid(null);
            if (announce) _problems.Text = "Reloaded from " + active + " (unsaved edits discarded)";
        }

        private void ShowSource()
        {
            if (_restoreFactory != null)
                _restoreFactory.Enabled = _resolution != null &&
                                          !string.IsNullOrEmpty(_resolution.FactoryPath);

            if (_sourceLine == null || _resolution == null)
            {
                if (_sourceLine != null) _sourceLine.Text = string.Empty;
                return;
            }

            switch (_resolution.Source)
            {
                case RulesSource.Drawing:
                    _sourceLine.Text = "Active: DRAWING OVERRIDE - " + _resolution.ActivePath +
                                       ". Edits stay with this job.";
                    break;
                case RulesSource.Office:
                    _sourceLine.Text = "Active: OFFICE CONFIGURATION - " + _resolution.ActivePath +
                                       ". Survives every plugin update.";
                    break;
                default:
                    _sourceLine.Text = "Active: FACTORY DEFAULTS (shipped with the plugin). " +
                                       "Your first save creates the office configuration at " +
                                       _resolution.OfficePath + "; the shipped file is never edited.";
                    break;
            }
        }

        private void RefreshCandidate()
        {
            if (_document == null) { _candidate = Context.Rules; return; }
            try
            {
                _candidate = _document.CompileCandidate();
            }
            catch (ConfigException ex)
            {
                _problems.Text = ex.Message;
            }
        }

        private void PopulateGrid(string selectId)
        {
            _grid.BeginUpdate();
            _grid.Items.Clear();

            if (_candidate != null)
            {
                foreach (var rule in _candidate.Codes)
                {
                    var item = new ListViewItem(CodesOf(rule));
                    item.SubItems.Add(string.IsNullOrEmpty(rule.Description)
                        ? rule.Id : rule.Description);
                    item.SubItems.Add(rule.Label != null ? rule.Label.Format : "-");
                    item.SubItems.Add(string.IsNullOrEmpty(rule.TagPrefix) ? "-" : rule.TagPrefix);
                    item.SubItems.Add(rule.Label != null ? rule.Label.Layer : "-");
                    item.SubItems.Add(rule.Enabled ? "Yes" : "no");
                    item.Tag = rule.Id;
                    if (!rule.Enabled) item.ForeColor = SystemColors.GrayText;
                    _grid.Items.Add(item);

                    if (selectId != null &&
                        string.Equals(rule.Id, selectId, StringComparison.OrdinalIgnoreCase))
                        item.Selected = true;
                }
            }

            _grid.EndUpdate();
            if (_grid.SelectedItems.Count == 0 && _grid.Items.Count > 0)
                _grid.Items[0].Selected = true;
        }

        private string SelectedId()
        {
            return _grid.SelectedItems.Count == 0
                ? null
                : (string)_grid.SelectedItems[0].Tag;
        }

        /// <summary>Fills the editor from the staged document; doubles as Cancel.</summary>
        private void LoadSelected()
        {
            var id = SelectedId();
            var rule = id != null && _document != null ? _document.GetEditable(id) : null;

            var have = rule != null;
            _enabled.Enabled = _labelFormat.Enabled = _labelLayer.Enabled = have;
            _leader.Enabled = _tagPrefix.Enabled = _description.Enabled = have;

            // The dripline section exists only for features that have one. A power
            // pole must not look like a tree.
            var drip = have && rule.HasDripline;
            _dripHeading.Visible = drip;
            SetRowVisible(_dripLayer, drip);

            if (!have)
            {
                _identity.Text = "(select a feature above)";
                return;
            }

            _identity.Text = (string.IsNullOrEmpty(rule.Description) ? rule.Id : rule.Description) +
                             "   -   rule '" + rule.Id + "'   -   code: " + CodesOfSelected(id);
            _symbolRow.Text = rule.SymbolSummary;
            _rotationRow.Text = rule.RotationSummary == "None"
                ? "None"
                : "Field azimuth rotates the existing Civil 3D marker (" +
                  rule.RotationSummary + ")";

            _description.Text = rule.Description;
            _enabled.Checked = rule.Enabled;
            _labelFormat.Text = rule.LabelFormat;
            ComboHelp.SelectOrAdd(_labelLayer, rule.LabelLayer, false);
            _leader.SelectedItem = rule.Leader;
            if (_leader.SelectedIndex < 0) _leader.SelectedIndex = 0;
            _tagPrefix.Text = rule.TagPrefix;
            if (drip) ComboHelp.SelectOrAdd(_dripLayer, rule.DripLayer, false);

            _problems.Text = _dirty ? "(unsaved changes)" : string.Empty;
        }

        private string CodesOfSelected(string id)
        {
            var rule = _candidate != null
                ? _candidate.Codes.FirstOrDefault(
                    c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase))
                : null;
            return rule == null ? "?" : CodesOf(rule);
        }

        private static string CodesOf(CodeRule rule)
        {
            if (rule.Species != null && rule.Species.Count > 0)
                return string.Join(", ", rule.Species.Keys.ToArray());

            var codes = RuleDocument.LiteralCodes(rule);
            return codes.Count > 0 ? string.Join(", ", codes.ToArray()) : rule.Id;
        }

        // ------------------------------------------------------------------ actions

        private void ApplyClicked()
        {
            var id = SelectedId();
            if (id == null || _document == null) return;

            var edit = _document.GetEditable(id);
            edit.Enabled = _enabled.Checked;
            edit.Description = (_description.Text ?? string.Empty).Trim();
            edit.LabelFormat = (_labelFormat.Text ?? string.Empty).Trim();
            edit.LabelLayer = _labelLayer.SelectedItem != null
                ? Convert.ToString(_labelLayer.SelectedItem) : string.Empty;
            edit.Leader = _leader.SelectedItem != null
                ? Convert.ToString(_leader.SelectedItem) : "Auto";
            edit.TagPrefix = (_tagPrefix.Text ?? string.Empty).Trim();
            if (edit.HasDripline && _dripLayer.SelectedItem != null)
                edit.DripLayer = Convert.ToString(_dripLayer.SelectedItem);

            _document.ApplyEdit(edit);

            var problems = _document.Validate();
            if (problems.Count > 0)
            {
                _problems.Text = string.Join(Environment.NewLine, problems.ToArray());
            }
            else
            {
                _dirty = true;
                _problems.Text = "(applied - unsaved changes; Save Rules writes the file)";
            }

            RefreshCandidate();
            PopulateGrid(id);
            RunPreview();
        }

        private void SaveClicked()
        {
            var target = _resolution != null ? _resolution.SaveTarget : null;
            if (_document == null || string.IsNullOrEmpty(target)) return;

            try
            {
                // Never the factory file: SaveTarget is the office configuration, or a
                // drawing override when one is active.
                _document.Save(target);
                _dirty = false;
                _problems.ForeColor = SystemColors.ControlText;
                _problems.Text = "Saved to " + target + ". Previous version kept as .bak.";

                // Saving may have created the office level; the active source changes.
                ReloadClicked(false);
            }
            catch (ConfigException ex)
            {
                _problems.ForeColor = Color.FromArgb(170, 0, 0);
                _problems.Text = ex.Message;
            }
            catch (System.IO.IOException ex)
            {
                _problems.ForeColor = Color.FromArgb(170, 0, 0);
                _problems.Text = "Could not write: " + ex.Message;
            }
        }

        private void RestoreFactoryClicked()
        {
            if (_resolution == null || string.IsNullOrEmpty(_resolution.FactoryPath)) return;

            var answer = MessageBox.Show(
                "Replace the office configuration with the shipped factory rules?\n\n" +
                "Your current office configuration will be kept as .bak. Drawing " +
                "overrides are not touched.",
                "Restore Factory Defaults",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
            if (answer != DialogResult.OK) return;

            try
            {
                RulesStore.RestoreFactoryDefaults(_resolution.FactoryPath, _resolution.OfficePath);
                ReloadClicked(false);
                _problems.ForeColor = SystemColors.ControlText;
                _problems.Text = "Factory defaults restored to " + _resolution.OfficePath +
                                 "; previous configuration kept as .bak.";
            }
            catch (ConfigException ex)
            {
                _problems.ForeColor = Color.FromArgb(170, 0, 0);
                _problems.Text = ex.Message;
            }
            catch (System.IO.IOException ex)
            {
                _problems.ForeColor = Color.FromArgb(170, 0, 0);
                _problems.Text = "Could not restore: " + ex.Message;
            }
        }

        // ------------------------------------------------------------------ preview

        /// <summary>
        /// The live preview: parses against the staged rules and renders what FTF
        /// would do. Purely in memory -- it can never touch the drawing.
        /// </summary>
        private void RunPreview()
        {
            if (_candidate == null) return;

            var text = (_testInput.Text ?? string.Empty).Trim();
            if (text.Length == 0) { _preview.Text = string.Empty; return; }

            try
            {
                var parsed = new FieldCodeParser(_candidate).Parse("preview", text);

                var rule = parsed.RuleId == null
                    ? null
                    : _candidate.Codes.FirstOrDefault(c =>
                        string.Equals(c.Id, parsed.RuleId, StringComparison.OrdinalIgnoreCase));

                var title = rule != null && !string.IsNullOrEmpty(rule.Description)
                    ? rule.Description
                    : parsed.RuleId;

                _preview.Text = FeaturePreview.Render(parsed, title);
            }
            catch (System.Exception ex)
            {
                _preview.Text = ex.Message;
            }
        }

        public override void LoadFrom(FtfSettings s) { }
        public override void SaveTo(FtfSettings s, ICollection<string> problems) { }
        public override void RestoreDefaults(FtfSettings s) { }
    }
}
