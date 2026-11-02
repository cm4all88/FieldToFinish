using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using FieldCodes.Settings;
using Autodesk.AutoCAD.DatabaseServices;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace FieldCodes.Cad.Setup
{
    /// <summary>
    /// Settings > Layers: every layer FieldToFinish draws on, what goes on it, where the name
    /// is configured and how it stands in this drawing -- with a "New Layer" column to send any
    /// of them to the project's layer. The new layer is a project mapping
    /// (<see cref="GeneralSettings.LayerMappings"/>, saved to the settings level chosen at the
    /// bottom of the window), and every command applies it (<see cref="ProjectLayers"/>).
    ///
    /// One mapping per layer: two features that share a layer move together, and the table
    /// says so. A layer for one feature only is changed on that feature's own page ("Edit
    /// feature layer").
    /// </summary>
    internal sealed class LayersPage : SetupPage
    {
        private const int ColFeature = 0, ColType = 1, ColCurrent = 2, ColNew = 3, ColSource = 4, ColStatus = 5;

        private sealed class Row
        {
            public LayerUse Use;
        }

        private DataGridView _grid;
        private TextBox _search;
        private ComboBox _filter;
        private Label _summary;
        private readonly List<Row> _rows = new List<Row>();
        private readonly Dictionary<string, string> _pending = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private List<LayerMapping> _loaded = new List<LayerMapping>();
        private List<string> _drawingLayers = new List<string>();
        private bool _loading;

        public override string Title { get { return "Layers"; } }
        public override string AffectedCommands { get { return "Every command that draws: labels, driplines, tags, schedule, spots, contours, line labels, production tools"; } }

        protected override void BuildBody() { }

        protected override Control BuildFilled()
        {
            var root = new TableLayoutPanel { ColumnCount = 1, RowCount = 4 };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            root.Controls.Add(new Label
            {
                Text = "New Layer sends a layer to the project's own (typed, or picked from the drawing). " +
                       "It applies to every feature on that layer and is saved to the level chosen below. " +
                       "Grey cells are read only.",
                AutoSize = true, MaximumSize = new Size(760, 0), ForeColor = SystemColors.GrayText,
                Margin = new Padding(0, 0, 0, 6)
            }, 0, 0);

            var bar = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = new Padding(0, 0, 0, 4) };
            bar.Controls.Add(new Label { Text = "Search", AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
            _search = new TextBox { Width = 200, Tag = Ui.SettingsPanel.ViewOnly };
            _search.TextChanged += (s, e) => Fill();
            bar.Controls.Add(_search);
            _filter = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 210, Margin = new Padding(10, 3, 0, 0), Tag = Ui.SettingsPanel.ViewOnly };
            _filter.Items.AddRange(new object[]
            {
                "All layers", "Changed (New Layer set)", "Missing from the drawing", "From rules.json", "From settings"
            });
            _filter.SelectedIndex = 0;
            _filter.SelectedIndexChanged += (s, e) => Fill();
            bar.Controls.Add(_filter);

            var pick = new Button { Text = "Pick from Drawing...", AutoSize = true, Margin = new Padding(14, 2, 0, 0) };
            pick.Click += (s, e) => PickFromDrawing();
            bar.Controls.Add(pick);
            var clear = new Button { Text = "Keep Current", AutoSize = true, Margin = new Padding(6, 2, 0, 0) };
            clear.Click += (s, e) => SetSelected(null);
            Tip(clear, "Clears New Layer on the selected rows: FTF uses the current layer.");
            bar.Controls.Add(clear);
            var create = new Button { Text = "Create Missing", AutoSize = true, Margin = new Padding(6, 2, 0, 0) };
            create.Click += (s, e) => CreateMissing();
            Tip(create, "Creates the selected layers that are not in the drawing yet, each taking the colour and lineweight " +
                        "of its family (V-UTIL-POWR-TEXT-E beside V-UTIL-POWR-E). Nothing else in the drawing changes.");
            bar.Controls.Add(create);
            var edit = new Button { Text = "Edit Feature Layer", AutoSize = true, Margin = new Padding(6, 2, 0, 0) };
            edit.Click += (s, e) => EditFeature();
            Tip(edit, "Opens the page that sets this feature's own layer -- for a change to one feature only.");
            bar.Controls.Add(edit);
            root.Controls.Add(bar, 0, 1);

            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = SystemColors.Window,
                BorderStyle = BorderStyle.FixedSingle,
                EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2
            };
            AddColumn("Feature", 26, true);
            AddColumn("Object Type", 14, true);
            AddColumn("Current Layer", 18, true);
            AddColumn("New Layer", 18, false);
            AddColumn("Source", 10, true);
            AddColumn("Status", 14, true);
            _grid.CellEndEdit += (s, e) => OnEdited(e.RowIndex);
            _grid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0 && e.ColumnIndex == ColNew) _grid.BeginEdit(true); };
            _grid.EditingControlShowing += (s, e) =>
            {
                var box = e.Control as TextBox;
                if (box == null) return;
                box.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
                box.AutoCompleteSource = AutoCompleteSource.CustomSource;
                var source = new AutoCompleteStringCollection();
                source.AddRange(_drawingLayers.ToArray());
                box.AutoCompleteCustomSource = source;
            };
            root.Controls.Add(_grid, 0, 2);

            _summary = new Label { AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(0, 6, 0, 0) };
            root.Controls.Add(_summary, 0, 3);
            return root;
        }

        private void AddColumn(string header, float weight, bool readOnly)
        {
            var column = new DataGridViewTextBoxColumn
            {
                HeaderText = header,
                FillWeight = weight,
                ReadOnly = readOnly,
                SortMode = DataGridViewColumnSortMode.Automatic
            };
            if (readOnly) column.DefaultCellStyle.BackColor = Color.FromArgb(246, 246, 248);
            else column.DefaultCellStyle.Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold);
            _grid.Columns.Add(column);
        }

        // ------------------------------------------------------------ data

        public override void LoadFrom(FtfSettings s)
        {
            _loading = true;
            _loaded = new List<LayerMapping>(s.General.LayerMappings ?? new List<LayerMapping>());
            _pending.Clear();
            foreach (var m in _loaded.Where(m => m != null && !string.IsNullOrWhiteSpace(m.From)))
                if (!_pending.ContainsKey(m.From.Trim())) _pending[m.From.Trim()] = (m.To ?? string.Empty).Trim();

            _drawingLayers = (Context.Drawing != null ? Context.Drawing.Layers : new List<string>())
                             .Where(l => !string.IsNullOrWhiteSpace(l)).OrderBy(l => l, StringComparer.OrdinalIgnoreCase).ToList();
            _rows.Clear();
            foreach (var use in LayerCatalog.Build(Context.Rules, s)
                                            .OrderBy(u => u.Source).ThenBy(u => u.Feature, StringComparer.OrdinalIgnoreCase))
                _rows.Add(new Row { Use = use });
            Fill();
            _loading = false;
        }

        private void Fill()
        {
            if (_grid == null) return;
            var text = (_search.Text ?? string.Empty).Trim();
            _grid.Rows.Clear();
            foreach (var row in _rows)
            {
                var u = row.Use;
                var mapped = NewFor(u.Layer);
                var status = Status(u.Layer);
                if (text.Length > 0 && (u.Feature + " " + u.ObjectType + " " + u.Layer + " " + mapped)
                        .IndexOf(text, StringComparison.OrdinalIgnoreCase) < 0) continue;
                switch (_filter.SelectedIndex)
                {
                    case 1: if (string.IsNullOrEmpty(mapped)) continue; break;
                    case 2: if (status == LayerStatus.Exists || status == LayerStatus.MappedExists) continue; break;
                    case 3: if (u.Source != LayerSource.Rules) continue; break;
                    case 4: if (u.Source != LayerSource.Settings) continue; break;
                }
                var i = _grid.Rows.Add(u.Feature, u.ObjectType, u.Layer, mapped, SourceText(u), LayerCatalog.Describe(status));
                _grid.Rows[i].Tag = row;
                _grid.Rows[i].Cells[ColSource].ToolTipText = u.EditedIn;
                var shared = _rows.Count(r => string.Equals(r.Use.Layer, u.Layer, StringComparison.OrdinalIgnoreCase)) - 1;
                if (shared > 0)
                    _grid.Rows[i].Cells[ColCurrent].ToolTipText = "Also used by " + shared + " other feature(s): a New Layer moves them together.";
                if (status == LayerStatus.WillBeCreated || status == LayerStatus.SimilarExists)
                    _grid.Rows[i].Cells[ColStatus].Style.ForeColor = Color.FromArgb(160, 90, 0);
            }
            var changed = _pending.Count(p => !string.IsNullOrEmpty(p.Value));
            var missing = _rows.Select(r => r.Use.Layer).Distinct(StringComparer.OrdinalIgnoreCase)
                               .Count(l => Status(l) == LayerStatus.WillBeCreated || Status(l) == LayerStatus.SimilarExists);
            _summary.Text = string.Format("{0} feature layer(s), {1} distinct layer(s); {2} sent to a project layer; {3} not in this drawing yet.",
                _rows.Count, _rows.Select(r => r.Use.Layer).Distinct(StringComparer.OrdinalIgnoreCase).Count(), changed, missing);
        }

        private static string SourceText(LayerUse u)
        {
            switch (u.Source)
            {
                case LayerSource.Rules: return "Rules";
                case LayerSource.Settings: return "Settings";
                default: return "Built in";
            }
        }

        private string NewFor(string layer)
        {
            string to;
            return _pending.TryGetValue(layer ?? string.Empty, out to) ? to : string.Empty;
        }

        private LayerStatus Status(string layer)
        {
            var mappings = _pending.Where(p => !string.IsNullOrEmpty(p.Value)).Select(p => new LayerMapping { From = p.Key, To = p.Value });
            return LayerCatalog.StatusOf(layer, _drawingLayers, mappings);
        }

        private void OnEdited(int rowIndex)
        {
            if (_loading || rowIndex < 0 || rowIndex >= _grid.Rows.Count) return;
            var row = _grid.Rows[rowIndex].Tag as Row;
            if (row == null) return;
            var value = Convert.ToString(_grid.Rows[rowIndex].Cells[ColNew].Value ?? string.Empty).Trim();
            Set(row.Use.Layer, value);
            BeginInvokeFill();
        }

        private void BeginInvokeFill()
        {
            // Refilling inside the edit event would re-enter the grid.
            if (_grid.IsHandleCreated) _grid.BeginInvoke((Action)Fill);
            else Fill();
        }

        private void Set(string layer, string to)
        {
            if (string.IsNullOrWhiteSpace(layer)) return;
            if (string.IsNullOrWhiteSpace(to) || string.Equals(to, layer, StringComparison.OrdinalIgnoreCase)) _pending.Remove(layer);
            else _pending[layer] = to.Trim();
            MarkChanged();
        }

        private IEnumerable<Row> Selected()
        {
            return _grid.SelectedRows.Cast<DataGridViewRow>().Select(r => r.Tag as Row).Where(r => r != null);
        }

        private void SetSelected(string to)
        {
            foreach (var row in Selected().ToList()) Set(row.Use.Layer, to);
            Fill();
        }

        private void PickFromDrawing()
        {
            if (!Selected().Any()) { MessageBox.Show(_grid, "Select one or more rows first.", "Layers"); return; }
            using (var form = new Form
            {
                Text = "Layers in this drawing", Width = 380, Height = 460, StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.SizableToolWindow, MinimizeBox = false, MaximizeBox = false
            })
            {
                var filter = new TextBox { Dock = DockStyle.Top };
                var list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
                Action refill = () =>
                {
                    list.BeginUpdate();
                    list.Items.Clear();
                    foreach (var l in _drawingLayers.Where(l => l.IndexOf(filter.Text ?? string.Empty, StringComparison.OrdinalIgnoreCase) >= 0))
                        list.Items.Add(l);
                    list.EndUpdate();
                };
                filter.TextChanged += (s, e) => refill();
                var ok = new Button { Text = "Use Layer", Dock = DockStyle.Bottom, DialogResult = DialogResult.OK };
                list.DoubleClick += (s, e) => { if (list.SelectedItem != null) form.DialogResult = DialogResult.OK; };
                form.Controls.Add(list);
                form.Controls.Add(filter);
                form.Controls.Add(ok);
                form.AcceptButton = ok;
                refill();
                if (form.ShowDialog(_grid) == DialogResult.OK && list.SelectedItem != null)
                    SetSelected(list.SelectedItem.ToString());
            }
        }

        /// <summary>Creates the selected layers that are not in the drawing, under the name FTF
        /// will use (the New Layer when set), with their family's look. Nothing else changes.</summary>
        private void CreateMissing()
        {
            var names = Selected().Select(r => string.IsNullOrEmpty(NewFor(r.Use.Layer)) ? r.Use.Layer : NewFor(r.Use.Layer))
                                  .Distinct(StringComparer.OrdinalIgnoreCase)
                                  .Where(n => !_drawingLayers.Contains(n, StringComparer.OrdinalIgnoreCase)).ToList();
            if (names.Count == 0) { MessageBox.Show(_grid, "The selected layers are already in the drawing.", "Layers"); return; }
            string problem;
            var bad = names.FirstOrDefault(n => !LayerMap.IsValidName(n, out problem));
            if (bad != null) { LayerMap.IsValidName(bad, out problem); MessageBox.Show(_grid, "\"" + bad + "\": " + problem + ".", "Layers"); return; }
            if (MessageBox.Show(_grid, "Create " + names.Count + " layer(s) in this drawing?\n\n" + string.Join("\n", names.Take(20).ToArray()),
                                "Create Missing Layers", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var table = (LayerTable)tr.GetObject(doc.Database.LayerTableId, OpenMode.ForRead);
                foreach (var n in names)
                    if (!table.Has(n))
                    {
                        // Straight to the table entry, not through the project mapping: this is
                        // the layer the user asked for.
                        CadUtil.EnsureLayerExact(doc.Database, tr, n);
                    }
                tr.Commit();
            }
            _drawingLayers.AddRange(names);
            _drawingLayers.Sort(StringComparer.OrdinalIgnoreCase);
            Fill();
        }

        private void EditFeature()
        {
            var row = Selected().FirstOrDefault();
            if (row == null) return;
            if (Context.Navigate != null && !string.IsNullOrEmpty(row.Use.SettingsSection))
                Context.Navigate(row.Use.SettingsSection);
            else
                MessageBox.Show(_grid, "This layer is set in " + row.Use.EditedIn + ".", "Edit Feature Layer");
        }

        public override void SaveTo(FtfSettings s, ICollection<string> problems)
        {
            foreach (var p in _pending.Where(p => !string.IsNullOrEmpty(p.Value)))
            {
                string why;
                if (!LayerMap.IsValidName(p.Value, out why)) problems.Add("Layers: \"" + p.Value + "\" for " + p.Key + ": " + why + ".");
            }
            // Mappings for layers not in the table (an older profile's, a production layer
            // FTF no longer lists) are kept as they were.
            var listed = new HashSet<string>(_rows.Select(r => r.Use.Layer), StringComparer.OrdinalIgnoreCase);
            var result = _loaded.Where(m => m != null && !string.IsNullOrWhiteSpace(m.From) && !listed.Contains(m.From.Trim())
                                            && !_pending.ContainsKey(m.From.Trim())).ToList();
            foreach (var p in _pending)
                LayerMap.Set(result, p.Key, p.Value);
            s.General.LayerMappings = result;
        }

        public override void RestoreDefaults(FtfSettings s)
        {
            s.General.LayerMappings = new List<LayerMapping>();
            LoadFrom(s);
            MarkChanged();
        }
    }
}
