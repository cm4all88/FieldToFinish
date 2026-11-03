using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using FieldCodes.Editing;
using FieldCodes.Settings;
using FieldCodes.Standards;
using Newtonsoft.Json;

namespace FieldCodes.Cad.Setup
{
    /// <summary>
    /// Settings > Point Features > Drafting Standard: what each field code and feature family needs
    /// on the plan -- class, disposition, symbol, label, surface, rotation, review, layer, symbol and
    /// label styles, and exceptions (a sign category, or words in the description) -- plus the words
    /// that put a sign in a category. Point accountability, Label All, FTFLABELS and FTFORIENT all read
    /// this one standard.
    ///
    /// Every entry in force is listed; a built-in entry's codes, family and description pattern are
    /// its identity and stay fixed (switch it off and add your own instead). OK stages the result on
    /// the page's rules document, and Save Rules writes it: only what differs from the built-in PMX
    /// standard goes into the file (RuleDocument.SetFeatureStandard, unit tested).
    ///
    /// UNTESTED as UI.
    /// </summary>
    internal sealed class FeatureStandardForm : Form
    {
        private const string Inherit = "(inherit)";
        private const int ColOn = 0, ColName = 1, ColCodes = 2, ColFamily = 3, ColMatch = 4, ColClass = 5, ColDisposition = 6,
                          ColSymbol = 7, ColLabel = 8, ColSurface = 9, ColRotation = 10, ColReview = 11, ColLayer = 12,
                          ColSymbolStyle = 13, ColLabelStyle = 14, ColNote = 15, ColExceptions = 16;

        private readonly RuleDocument _document;
        private readonly DataGridView _rows;
        private readonly DataGridView _signs;
        private readonly TextBox _exceptions;
        private readonly Label _exceptionsCaption;
        private readonly TextBox _try;
        private readonly Label _result;
        private readonly Label _problems;
        private DataGridViewRow _exceptionsRow;
        private bool _loading;

        public FeatureStandardForm(RuleDocument document)
        {
            _document = document;
            Text = "Drafting Standard";
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(1180, 760);
            MinimumSize = new Size(900, 560);
            ShowInTaskbar = false;

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 9, Padding = new Padding(10) };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 62f));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 90f));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 38f));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(layout);

            layout.Controls.Add(Wrap(
                "What each feature needs on the plan. A point takes the default row, then its family's row, then a row that names its " +
                "words (only when no rule or list knows its code), then its own code's row; then any exception that fits (a sign's category, " +
                "or words in its description). (inherit) leaves the value to the rows before. Symbol: None, PointStyle (any visible marker, " +
                "or one of the symbol styles named), Specific (its own symbol block). Label: AsPlanned (what its rule writes, where Label All " +
                "labels its family), None, Required, PointLabel (its own Civil 3D label counts where FTF's label would say no more than the description). Rotate No: turned only by a direction the " +
                "crew recorded, never assumed; left to (inherit), FTFORIENT's own rules decide. Built-in rows keep their codes, family and pattern; switch one off and add your own instead.", 1140), 0, 0);

            _rows = Grid();
            _rows.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "On", Width = 34, ToolTipText = "Off: the row is not used (a built-in row stays in the file as switched off)." });
            _rows.Columns.Add(TextColumn("Name", 120, "Shown in the evidence and reports."));
            _rows.Columns.Add(TextColumn("Codes", 90, "Field codes, comma separated; * and ? allowed; trailing figure digits ignored."));
            _rows.Columns.Add(Choice("Family", 90, new[] { string.Empty, FeatureRequirement.DefaultFamily }.Concat(Families()).ToArray(), "A Label All family, or * for the default row."));
            _rows.Columns.Add(TextColumn("Description match", 110, "A regular expression on the description, for a feature shot without a code FTF knows (\\bSIGN\\b)."));
            _rows.Columns.Add(Choice("Class", 80, Options<PointClass>(), null));
            _rows.Columns.Add(Choice("Disposition", 96, Options<IntendedDisposition>(), "How it should end up on the plan."));
            _rows.Columns.Add(Choice("Symbol", 78, Options<SymbolRequirement>(), null));
            _rows.Columns.Add(Choice("Label", 82, Options<LabelRequirement>(), null));
            _rows.Columns.Add(Choice("Surface", 66, YesNo(), "Surface data: accounted for as surface only; membership is checked only when Settings > Point Accountability asks."));
            _rows.Columns.Add(Choice("Rotate", 62, YesNo(), "Yes: turning its symbol is part of the deliverable. No: turned only by a direction the crew recorded, never assumed (a private sign). (inherit) to the end: FTFORIENT's own rules decide."));
            _rows.Columns.Add(Choice("Review", 62, YesNo(), "Every such point is listed for a person to look at."));
            _rows.Columns.Add(TextColumn("Layer", 90, "The layer it is drafted on (reported in the evidence)."));
            _rows.Columns.Add(TextColumn("Symbol style", 100, "Point styles or symbol blocks that show it, comma separated, * and ? allowed."));
            _rows.Columns.Add(TextColumn("Label style", 90, "Point label styles that label it (with Label PointLabel)."));
            _rows.Columns.Add(TextColumn("Note", 110, "Why: shown in the evidence and the Review window."));
            _rows.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Exceptions", Width = 120, ReadOnly = true, ToolTipText = "Edited below for the selected row." });
            _rows.AllowUserToAddRows = true;
            _rows.AllowUserToDeleteRows = true;
            _rows.UserDeletingRow += (s, e) =>
            {
                var r = e.Row.Tag as FeatureRequirement;
                if (r != null && RuleDocument.BuiltInRequirement(r.Key) != null)
                {
                    e.Cancel = true;
                    _problems.Text = "A built-in row cannot be deleted: untick On to switch it off.";
                }
            };
            _rows.SelectionChanged += (s, e) => ShowExceptions();
            _rows.CellValueChanged += (s, e) => { if (!_loading) UpdatePreview(); };
            _rows.CurrentCellDirtyStateChanged += (s, e) => { if (_rows.IsCurrentCellDirty && _rows.CurrentCell is DataGridViewCheckBoxCell) _rows.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            _rows.DataError += (s, e) => { e.ThrowException = false; };
            layout.Controls.Add(_rows, 0, 1);

            _exceptionsCaption = new Label { AutoSize = true, Margin = new Padding(0, 6, 0, 2), Text = "Exceptions of the selected row (JSON)" };
            layout.Controls.Add(_exceptionsCaption, 0, 2);
            _exceptions = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, AcceptsReturn = true, Font = new Font("Consolas", 9f) };
            _exceptions.Leave += (s, e) => TakeExceptions();
            layout.Controls.Add(_exceptions, 0, 3);

            layout.Controls.Add(new Label
            {
                AutoSize = true, Margin = new Padding(0, 8, 0, 2),
                Text = "Sign categories: codes that are always the category, and words that put a sign's description in it (first category that fits wins)."
            }, 0, 4);
            _signs = Grid();
            _signs.Columns.Add(Choice("Category", 110, Enum.GetNames(typeof(SignCategory)).Where(n => n != SignCategory.None.ToString()).ToArray(), null));
            _signs.Columns.Add(TextColumn("Codes", 160, "Comma separated; * and ? allowed."));
            _signs.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Words", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, ToolTipText = "Comma separated words or phrases." });
            _signs.AllowUserToAddRows = true;
            _signs.AllowUserToDeleteRows = true;
            _signs.CellValueChanged += (s, e) => { if (!_loading) UpdatePreview(); };
            _signs.DataError += (s, e) => { e.ThrowException = false; };
            layout.Controls.Add(_signs, 0, 5);

            var tryRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 8, 0, 0) };
            tryRow.Controls.Add(new Label { Text = "Try a description:", AutoSize = true, Margin = new Padding(0, 6, 6, 0) });
            _try = new TextBox { Width = 260, Text = "HOUSE NUMBER 4735 SIGN" };
            _try.TextChanged += (s, e) => UpdatePreview();
            tryRow.Controls.Add(_try);
            layout.Controls.Add(tryRow, 0, 6);
            _result = new Label { AutoSize = true, MaximumSize = new Size(1140, 0), Margin = new Padding(0, 4, 0, 4) };
            _problems = new Label { AutoSize = true, MaximumSize = new Size(1140, 0), ForeColor = Color.FromArgb(170, 0, 0) };
            var messages = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            messages.Controls.Add(_result);
            messages.Controls.Add(_problems);
            layout.Controls.Add(messages, 0, 7);

            var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
            var ok = new Button { Text = "OK", AutoSize = true, Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold) };
            var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
            var builtIn = new Button { Text = "Built-in Standard", AutoSize = true };
            ok.Click += (s, e) => OkClicked();
            builtIn.Click += (s, e) =>
            {
                Fill(FeatureStandard.Pmx().Requirements, FeatureStandard.Pmx().SignCategories);
                _problems.Text = "Showing the built-in PMX standard; OK then Save Rules removes the office's changes.";
            };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);
            buttons.Controls.Add(builtIn);
            layout.Controls.Add(buttons, 0, 8);
            CancelButton = cancel;

            Fill(_document.GetFeatureRequirements(), _document.GetSignCategories());
        }

        // ------------------------------------------------------------------ rows

        private void Fill(IEnumerable<FeatureRequirement> requirements, IEnumerable<SignCategoryRule> signs)
        {
            _loading = true;
            try
            {
                _rows.Rows.Clear();
                foreach (var r in requirements) AddRow(r.Copy());
                _signs.Rows.Clear();
                foreach (var c in signs)
                    _signs.Rows.Add(c.Category.ToString(), string.Join(", ", (c.Codes ?? new List<string>()).ToArray()), string.Join(", ", (c.Words ?? new List<string>()).ToArray()));
            }
            finally { _loading = false; }
            _exceptionsRow = null;
            ShowExceptions();
            UpdatePreview();
        }

        private void AddRow(FeatureRequirement r)
        {
            // The family cell is a list: a family written in other case shows as the list's own.
            var family = new[] { FeatureRequirement.DefaultFamily }.Concat(Families())
                .FirstOrDefault(n => string.Equals(n, (r.Family ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
            var i = _rows.Rows.Add(r.Enabled, r.Name ?? string.Empty, string.Join(", ", (r.Codes ?? new List<string>()).ToArray()), family,
                                   r.Match ?? string.Empty, Show(r.Classification), Show(r.Disposition), Show(r.Symbol), Show(r.Label),
                                   Show(r.Surface), Show(r.Rotation), Show(r.Review), r.Layer ?? string.Empty, r.SymbolStyle ?? string.Empty,
                                   r.LabelStyle ?? string.Empty, r.Note ?? string.Empty, ExceptionSummary(r.Exceptions));
            var row = _rows.Rows[i];
            row.Tag = r;
            if (RuleDocument.BuiltInRequirement(r.Key) != null)
            {
                foreach (var c in new[] { ColCodes, ColFamily, ColMatch })
                {
                    row.Cells[c].ReadOnly = true;
                    row.Cells[c].Style.ForeColor = SystemColors.GrayText;
                }
                row.Cells[ColName].ToolTipText = "Built in (PMX)" + (RuleDocument.SameAsBuiltIn(r) ? "" : ", changed by the office");
            }
        }

        /// <summary>The rows as requirements, in the grid's order.</summary>
        private List<FeatureRequirement> ReadRows(ICollection<string> problems)
        {
            _rows.EndEdit();
            TakeExceptions();
            var result = new List<FeatureRequirement>();
            foreach (DataGridViewRow row in _rows.Rows)
            {
                if (row.IsNewRow) continue;
                var before = row.Tag as FeatureRequirement;
                var r = before != null ? before.Copy() : new FeatureRequirement();
                r.Enabled = row.Cells[ColOn].Value == null || Convert.ToBoolean(row.Cells[ColOn].Value);
                r.Name = Cell(row, ColName);
                if (before == null || RuleDocument.BuiltInRequirement(before.Key) == null)
                {
                    r.Codes = SettingsRegistry.Split(Cell(row, ColCodes)).Select(x => x.ToUpperInvariant()).ToList();
                    r.Family = Cell(row, ColFamily);
                    r.Match = Cell(row, ColMatch);
                }
                r.Classification = Read<PointClass>(row, ColClass);
                r.Disposition = Read<IntendedDisposition>(row, ColDisposition);
                r.Symbol = Read<SymbolRequirement>(row, ColSymbol);
                r.Label = Read<LabelRequirement>(row, ColLabel);
                r.Surface = ReadFlag(row, ColSurface);
                r.Rotation = ReadFlag(row, ColRotation);
                r.Review = ReadFlag(row, ColReview);
                r.Layer = Cell(row, ColLayer);
                r.SymbolStyle = Cell(row, ColSymbolStyle);
                r.LabelStyle = Cell(row, ColLabelStyle);
                r.Note = Cell(row, ColNote);
                if ((r.Codes == null || r.Codes.Count == 0) && string.IsNullOrWhiteSpace(r.Family) && string.IsNullOrWhiteSpace(r.Match))
                {
                    problems.Add("Row " + (row.Index + 1) + (string.IsNullOrWhiteSpace(r.Name) ? "" : " (" + r.Name + ")") + " needs codes, a family or a description match.");
                    continue;
                }
                result.Add(r);
            }
            return result;
        }

        private List<SignCategoryRule> ReadSigns(ICollection<string> problems)
        {
            _signs.EndEdit();
            var result = new List<SignCategoryRule>();
            foreach (DataGridViewRow row in _signs.Rows)
            {
                if (row.IsNewRow) continue;
                SignCategory category;
                if (!Enum.TryParse(Cell(row, 0) ?? string.Empty, true, out category) || category == SignCategory.None)
                {
                    problems.Add("Sign category row " + (row.Index + 1) + " needs a category.");
                    continue;
                }
                if (result.Any(c => c.Category == category)) { problems.Add("Sign category " + category + " is listed twice."); continue; }
                result.Add(new SignCategoryRule
                {
                    Category = category,
                    Codes = SettingsRegistry.Split(Cell(row, 1)).Select(x => x.ToUpperInvariant()).ToList(),
                    Words = SettingsRegistry.Split(Cell(row, 2)).Select(x => x.ToUpperInvariant()).ToList()
                });
            }
            return result;
        }

        // ------------------------------------------------------------------ exceptions

        private void ShowExceptions()
        {
            if (_loading) return;
            TakeExceptions();
            var row = _rows.CurrentRow;
            _exceptionsRow = row != null && !row.IsNewRow ? row : null;
            var r = _exceptionsRow != null ? _exceptionsRow.Tag as FeatureRequirement : null;
            _exceptions.Enabled = _exceptionsRow != null;
            _exceptionsCaption.Text = "Exceptions of " + (_exceptionsRow == null ? "the selected row" : "row " + (_exceptionsRow.Index + 1) + " " + Cell(_exceptionsRow, ColName)) +
                                      " (JSON: [ { \"name\", \"signCategory\" or \"when\", then the fields to change: \"symbol\": \"PointStyle\", \"rotation\": false ... } ])";
            _exceptions.Text = r != null && r.Exceptions != null && r.Exceptions.Count > 0
                ? JsonConvert.SerializeObject(r.Exceptions, Formatting.Indented) : string.Empty;
        }

        /// <summary>Puts the exceptions text back on its row; a text that does not read is reported and kept.</summary>
        private bool TakeExceptions()
        {
            if (_exceptionsRow == null || _exceptionsRow.DataGridView == null) return true;
            var r = _exceptionsRow.Tag as FeatureRequirement;
            if (r == null) { r = new FeatureRequirement(); _exceptionsRow.Tag = r; }
            var text = (_exceptions.Text ?? string.Empty).Trim();
            try
            {
                r.Exceptions = text.Length == 0 ? null : JsonConvert.DeserializeObject<List<FeatureException>>(text);
                _exceptionsRow.Cells[ColExceptions].Value = ExceptionSummary(r.Exceptions);
                return true;
            }
            catch (JsonException ex)
            {
                _problems.Text = "The exceptions of row " + (_exceptionsRow.Index + 1) + " do not read: " + ex.Message;
                return false;
            }
        }

        private static string ExceptionSummary(List<FeatureException> exceptions)
        {
            if (exceptions == null || exceptions.Count == 0) return string.Empty;
            return string.Join("; ", exceptions.Select(x => !string.IsNullOrWhiteSpace(x.Name) ? x.Name
                : x.SignCategory.HasValue ? x.SignCategory.Value.ToString() : "when " + x.When).ToArray());
        }

        // ------------------------------------------------------------------ preview and OK

        private void UpdatePreview()
        {
            if (_loading || _result == null) return;
            var problems = new List<string>();
            var rows = ReadRows(problems);
            var signs = ReadSigns(problems);
            if (problems.Count > 0) { _problems.Text = string.Join(Environment.NewLine, problems.ToArray()); _result.Text = string.Empty; return; }
            try
            {
                var copy = RuleDocument.FromText(_document.Render());
                copy.SetFeatureStandard(rows, signs);
                var cfg = copy.CompileCandidate();
                var text = (_try.Text ?? string.Empty).Trim();
                _problems.Text = string.Empty;
                if (text.Length == 0) { _result.Text = string.Empty; return; }
                var parsed = new FieldCodeParser(cfg).Parse("preview", text);
                var req = FeatureRequirements.For(cfg, parsed, text);
                _result.Text = "\"" + text + "\": " + EffectiveRequirement.ClassName(req.Classification) +
                               (req.SignCategory != SignCategory.None ? " (" + EffectiveRequirement.SignName(req.SignCategory) + " sign)" : string.Empty) +
                               " -- " + req.Describe() + ". From: " + req.Source + ".";
            }
            catch (ConfigException ex) { _problems.Text = ex.Message; _result.Text = string.Empty; }
        }

        private void OkClicked()
        {
            var problems = new List<string>();
            if (!TakeExceptions()) return;
            var rows = ReadRows(problems);
            var signs = ReadSigns(problems);
            if (problems.Count > 0) { _problems.Text = string.Join(Environment.NewLine, problems.ToArray()); return; }
            try { _document.SetFeatureStandard(rows, signs); }
            catch (ConfigException ex) { _problems.Text = ex.Message; return; }
            DialogResult = DialogResult.OK;
            Close();
        }

        // ------------------------------------------------------------------ helpers

        private static DataGridView Grid()
        {
            return new DataGridView
            {
                Dock = DockStyle.Fill,
                RowHeadersVisible = true,
                AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None,
                SelectionMode = DataGridViewSelectionMode.CellSelect,
                MultiSelect = false,
                EditMode = DataGridViewEditMode.EditOnEnter
            };
        }

        private static DataGridViewTextBoxColumn TextColumn(string header, int width, string tip)
        {
            return new DataGridViewTextBoxColumn { HeaderText = header, Width = width, ToolTipText = tip };
        }

        private static DataGridViewComboBoxColumn Choice(string header, int width, string[] items, string tip)
        {
            var c = new DataGridViewComboBoxColumn { HeaderText = header, Width = width, ToolTipText = tip, FlatStyle = FlatStyle.Flat };
            c.Items.AddRange(items.Cast<object>().ToArray());
            return c;
        }

        private static string[] Options<T>() where T : struct
        {
            return new[] { Inherit }.Concat(Enum.GetNames(typeof(T))).ToArray();
        }

        private static string[] YesNo() { return new[] { Inherit, "Yes", "No" }; }

        private static IEnumerable<string> Families()
        {
            return Enum.GetNames(typeof(AnnotationCategory)).Where(n => n != AnnotationCategory.Lines.ToString());
        }

        private static string Show<T>(T? value) where T : struct { return value.HasValue ? value.Value.ToString() : Inherit; }

        private static string Show(bool? value) { return value.HasValue ? (value.Value ? "Yes" : "No") : Inherit; }

        private static T? Read<T>(DataGridViewRow row, int column) where T : struct
        {
            var text = Cell(row, column);
            T value;
            return text == null || text == Inherit || !Enum.TryParse(text, true, out value) ? (T?)null : value;
        }

        private static bool? ReadFlag(DataGridViewRow row, int column)
        {
            var text = Cell(row, column);
            return text == "Yes" ? true : text == "No" ? (bool?)false : null;
        }

        private static string Cell(DataGridViewRow row, int column)
        {
            var v = Convert.ToString(row.Cells[column].Value);
            return string.IsNullOrWhiteSpace(v) ? null : v.Trim();
        }

        private static Label Wrap(string text, int width)
        {
            return new Label { Text = text, AutoSize = true, MaximumSize = new Size(width, 0), Margin = new Padding(0, 0, 0, 6) };
        }
    }
}
