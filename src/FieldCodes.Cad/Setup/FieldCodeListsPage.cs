using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using FieldCodes.Editing;
using FieldCodes.Settings;

namespace FieldCodes.Cad.Setup
{
    /// <summary>
    /// Settings > FieldToFinish > Field Code Lists: the rules file's lists that say how the crew's
    /// field codes are read, one tab each. Never Drawn (ignoreCodes, with the reason and what holds
    /// the points instead), Labelled Symbols (symbolLabels), Control (controlCodes), Linework
    /// (lineworkCodes), Line Commands (lineworkCommands, the built-in PMX-Universal set shown when
    /// the file has none) and Modifiers (modifiers). A box under the tabs shows how the lists, as
    /// edited, read a description. Loading, staging, checking and saving are RulesSectionPage's.
    ///
    /// UNTESTED as UI; the document editing, checks and preview are unit tested
    /// (FieldCodeListsEditingTests).
    /// </summary>
    [SettingsPage(Group = "FieldToFinish", Label = "Field Code Lists", Order = 35,
                  Commands = "FTFPOINTS,FTFLABELS,FTFTAGS,FTFTABLE,FTFDRIP,FTFLABELALL,FTFBLOCKS,FTFCONTROL,FTFREVIEWCHECK,FTFQC,FTFRUN")]
    internal sealed class FieldCodeListsPage : RulesSectionPage
    {
        private const string Nothing = "Nothing";
        private const string Surface = "A surface (TIN)";

        private DataGridView _ignore;
        private DataGridView _symbols;
        private TextBox _control;
        private TextBox _linework;
        private DataGridView _commands;
        private TextBox _escape;
        private DataGridView _modifiers;
        private Label _ignoreNote;
        private Label _symbolNote;
        private Label _controlNote;
        private Label _lineworkNote;
        private Label _commandNote;
        private Label _modifierNote;
        private TextBox _try;
        private Label _tryResult;
        private bool _commandsInFile;
        private bool _quiet;
        private RulesConfig _candidate;

        public override string Title { get { return "Field Code Lists"; } }
        public override string AffectedCommands
        {
            get { return "FTFPOINTS, FTFLABELS, FTFTAGS, FTFTABLE, FTFDRIP, FTFLABELALL, FTFBLOCKS, FTFCONTROL, FTFREVIEWCHECK, FTFQC and FTFRUN"; }
        }
        protected override string SectionKey { get { return "lineworkCommands"; } }
        protected override string WhatChanges { get { return "field code list"; } }
        protected override string SaveText { get { return "Save Field Code Lists"; } }
        protected override string UsedBy { get { return "Each command that reads field codes"; } }

        // ================================================================== building

        protected override void BuildRows()
        {
            Note("How FTF reads the crew's field codes: the codes it never draws, the bare symbol codes it labels, the control and " +
                 "linework codes, the words that begin and end a line, and the words after a code that change how a point is drawn. " +
                 "Each tab says what its list does. Problems show below as you type.");

            var tabs = new TabControl { Width = 800, Height = 470, ShowToolTips = true, Margin = new Padding(0, 4, 0, 6) };
            tabs.TabPages.Add(NeverDrawnTab());
            tabs.TabPages.Add(SymbolLabelsTab());
            tabs.TabPages.Add(ControlTab());
            tabs.TabPages.Add(LineworkTab());
            tabs.TabPages.Add(LineCommandsTab());
            tabs.TabPages.Add(ModifiersTab());
            AddFullWidth(tabs);

            Heading("Try a description");
            _try = TextRow("Description",
                "Type a point description as the crew would (GS 12, CB, FND 5/8 IR, FNC1 B 3.5, CON 18 DEAD) to see how these lists read it, " +
                "with the edits above, before they are saved.", 300);
            _tryResult = LiveNote();
            _try.TextChanged += (s, e) => ShowTry();
        }

        private TabPage NeverDrawnTab()
        {
            _ignore = Grid();
            _ignore.Columns.Add(TextColumn("Code", 14, "The bare code. A figure number after it is covered too: GS covers GS2."));
            _ignore.Columns.Add(TextColumn("Why it is not drawn", 56,
                "Write the reason down: Point Accountability lists the points of a code ignored without one for review."));
            var expect = new DataGridViewComboBoxColumn
            {
                HeaderText = "What holds its points instead", FillWeight = 30, FlatStyle = FlatStyle.Flat,
                ToolTipText = "A surface: the points are surface data, so Point Accountability checks they are in a surface. Nothing: they are only notes."
            };
            expect.Items.Add(Nothing);
            expect.Items.Add(Surface);
            _ignore.Columns.Add(expect);
            _ignoreNote = TabNote();
            return Tab(EditableCodeLists.NeverDrawnTab, "Codes FTF never draws: ignoreCodes in the rules file.",
                "Codes FTF never draws anything for; their points are passed over without a word. Example: GS, a ground shot, is surface " +
                "data for the TIN. Keep the list short: a code that simply has no rule yet belongs in the unhandled report, not here.",
                _ignore, _ignoreNote, GridButtons(_ignore, 0, ShippedButton(l => ShowIgnore(l.IgnoreCodes))));
        }

        private TabPage SymbolLabelsTab()
        {
            _symbols = Grid();
            _symbols.Columns.Add(new DataGridViewCheckBoxColumn
            {
                HeaderText = "On", FillWeight = 6, ToolTipText = "Off: the entry stays in the list, but the code is not labelled."
            });
            _symbols.Columns.Add(TextColumn("Code", 14, "The bare code, shot with nothing after it. A figure number is covered too (CB2)."));
            _symbols.Columns.Add(TextColumn("Text layer", 40,
                "The layer the label goes on: the feature family's text layer (V-UTIL-STRM-TEXT-E for storm). This drawing's layers are offered as you type."));
            _symbols.Columns.Add(TextColumn("Label (blank: the code as shot)", 40,
                "What the label says instead of the code (CATCH BASIN). Blank labels it with the code exactly as shot."));
            _symbols.EditingControlShowing += (s, e) => OfferLayers(_symbols, e, 2);
            _symbolNote = TabNote();
            return Tab(EditableCodeLists.SymbolLabelsTab, "Bare symbol codes labelled with the code itself: symbolLabels in the rules file.",
                "A bare symbol code (nothing written after it) gets no label, except the codes listed here, which are labelled with the " +
                "code itself beside the symbol. Example: CB on V-UTIL-STRM-TEXT-E labels each catch basin CB. A code a rule reads is " +
                "labelled by its rule instead.",
                _symbols, _symbolNote, GridButtons(_symbols, 1, ShippedButton(l => ShowSymbols(l.SymbolLabels))));
        }

        private TabPage ControlTab()
        {
            _control = CodeBox("One code per line. Matched against the first word of the description, exactly (FND, not FND2).");
            _controlNote = TabNote();
            return Tab(EditableCodeLists.ControlTab, "Codes whose points are survey control or found monuments: controlCodes in the rules file.",
                "Codes whose points are survey control or found monuments: FTFCONTROL lists them in the control table, and Point " +
                "Accountability expects them there. One code per line, matched against the first word exactly. Example: FND, FIP, XBM.",
                _control, _controlNote, ShippedButton(l => _control.Lines = l.ControlCodes.ToArray()));
        }

        private TabPage LineworkTab()
        {
            _linework = CodeBox("One code per line. A figure number after it is covered too: EC covers EC1 and EC2.");
            _lineworkNote = TabNote();
            return Tab(EditableCodeLists.LineworkTab, "Codes whose points make lines (Civil 3D figures): lineworkCodes in the rules file.",
                "Codes whose points make lines: Civil 3D draws the lines (figures) and FTF labels them, so the points need no rule and " +
                "are not reported as unhandled. Example: FNC (fence), EP (edge of pavement). Keep it the same as your Civil 3D figure " +
                "prefix database.",
                _linework, _lineworkNote, ShippedButton(l => _linework.Lines = l.LineworkCodes.ToArray()));
        }

        private TabPage LineCommandsTab()
        {
            _commands = Grid();
            _commands.AllowUserToDeleteRows = false;
            var name = TextColumn("Command", 16, "What Civil 3D does with the line.");
            name.ReadOnly = true;
            var meaning = TextColumn("What it does", 52, "What Civil 3D does with the line.");
            meaning.ReadOnly = true;
            _commands.Columns.Add(name);
            _commands.Columns.Add(meaning);
            _commands.Columns.Add(TextColumn("Codes (separate with spaces)", 32,
                "The words the crew writes for it after a line code. Blank: your office has no code for it. A code can do one thing only."));
            foreach (var key in EditableLineworkCommands.Keys)
            {
                var i = _commands.Rows.Add(EditableLineworkCommands.Name(key), EditableLineworkCommands.Meaning(key), string.Empty);
                _commands.Rows[i].Cells[0].Style.ForeColor = SystemColors.GrayText;
                _commands.Rows[i].Cells[1].Style.ForeColor = SystemColors.GrayText;
            }

            _escape = new TextBox { Width = 40, Margin = new Padding(0, 3, 12, 0) };
            Tip(_escape, "A word starting with it is plain text, whatever it looks like: /GATE is the word GATE, not a code. Blank: no escape character.");
            _escape.TextChanged += (s, e) => Changed();
            var escapeLabel = new Label { Text = "Escape character:", AutoSize = true, Margin = new Padding(0, 6, 4, 0) };
            Tip(escapeLabel, "A word starting with it is plain text, whatever it looks like: /GATE is the word GATE, not a code. Blank: no escape character.");
            var builtIn = new Button { Text = "Use the Built-in Set", AutoSize = true };
            Tip(builtIn, "Shows the PMX-Universal set (B begin, E end, C close, U continue, P and T curves, G, RT, X, CIR, OC, HO/VO/SO, RPN, CPN; / escapes) for Save to write.");
            builtIn.Click += (s, e) => Quietly(() => ShowCommands(EditableLineworkCommands.BuiltIn()));

            _commandNote = TabNote();
            return Tab(EditableCodeLists.LineCommandsTab, "The words that begin, end and close a line: lineworkCommands in the rules file.",
                "The words written after a line code that tell Civil 3D what to do with the line: FNC B begins a fence, FNC E ends it, " +
                "BLD C closes a building. They must be the same as your Civil 3D linework code set. Example: \"BLD B EC B\" begins a " +
                "building and an edge of concrete at the same shot.",
                _commands, _commandNote, escapeLabel, _escape, builtIn);
        }

        private TabPage ModifiersTab()
        {
            _modifiers = Grid();
            _modifiers.Columns.Add(TextColumn("Name", 10, "The modifier's name; the tag table's notes column shows it (DEAD)."));
            _modifiers.Columns.Add(TextColumn("Matches the word", 17,
                "A pattern one word after the code is matched against: ^DEAD$ is exactly DEAD, ^REM(OVE)?$ is REM or REMOVE, " +
                "^CL(?<count>[0-9]+)$ is CL and a number, kept as {count}."));
            _modifiers.Columns.Add(TextColumn("Order", 6, "Lower applies first, whatever order the words were written in."));
            _modifiers.Columns.Add(TextColumn("Adds to the label", 12,
                "Added to the label's first line exactly as typed; start it with a space: \" (DEAD)\"."));
            _modifiers.Columns.Add(TextColumn("Replaces the label with", 17,
                "The whole label instead, with {fields}: CLUSTER OF {count} TREES AVERAGE {trunk}\". Blank keeps the label. " +
                "Two modifiers that replace the label need different orders."));
            _modifiers.Columns.Add(TextColumn("Adds to layer names", 10,
                "Added to the label, symbol and dripline layer names: -DEAD puts a dead tree's label on V-SURF-VEGE-TEXT-E-DEAD."));
            _modifiers.Columns.Add(TextColumn("Uses block", 9,
                "A block FTF puts in place of the rule's own, where the rule inserts one. Usually blank: the point style shows the symbol."));
            _modifiers.Columns.Add(TextColumn("Stem count from", 10,
                "The named part of the pattern that holds the number of stems: count in ^CL(?<count>[0-9]+)$."));
            _modifiers.Columns.Add(new DataGridViewCheckBoxColumn
            {
                HeaderText = "Flag for review", FillWeight = 9, ToolTipText = "The point is listed as a warning so someone checks it."
            });
            _modifierNote = TabNote();
            return Tab(EditableCodeLists.ModifiersTab, "Words after a code that change how the point is drawn: modifiers in the rules file.",
                "Words written after a point's code that change how it is drawn: CON 18 DEAD labels the tree with (DEAD) and puts it on " +
                "the -DEAD layers. Each word is tried against the patterns from the top; the first that matches is used. Order says " +
                "which applies first when a point has several.",
                _modifiers, _modifierNote, GridButtons(_modifiers, 0, ShippedButton(l => ShowModifiers(l.Modifiers))));
        }

        // ------------------------------------------------------------------ control helpers

        private TabPage Tab(string title, string tip, string explanation, Control editor, Label note, params Control[] buttons)
        {
            var page = new TabPage(title) { ToolTipText = tip, Padding = new Padding(8), UseVisualStyleBackColor = true };
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var text = new Label { Text = explanation, AutoSize = true, MaximumSize = new Size(760, 0), Margin = new Padding(0, 0, 0, 6) };
            layout.Controls.Add(text, 0, 0);
            editor.Dock = DockStyle.Fill;
            layout.Controls.Add(editor, 0, 1);
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Dock = DockStyle.Top, Margin = new Padding(0, 4, 0, 0) };
            foreach (var b in buttons)
            {
                if (b is Button) b.Margin = new Padding(0, 0, 6, 0);
                row.Controls.Add(b);
            }
            layout.Controls.Add(row, 0, 2);
            layout.Controls.Add(note, 0, 3);
            page.Controls.Add(layout);
            return page;
        }

        private static Label TabNote()
        {
            return new Label { AutoSize = true, MaximumSize = new Size(760, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(0, 4, 0, 0) };
        }

        private TextBox CodeBox(string tip)
        {
            var box = new TextBox { Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical, WordWrap = false };
            Tip(box, tip);
            box.TextChanged += (s, e) => Changed();
            return box;
        }

        private DataGridView Grid()
        {
            var g = new DataGridView
            {
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = true,
                AllowUserToResizeRows = false,
                RowHeadersWidth = 24,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
                SelectionMode = DataGridViewSelectionMode.CellSelect,
                MultiSelect = false,
                ShowCellToolTips = true,
                BackgroundColor = SystemColors.Window
            };
            g.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.True;
            // Each keystroke is committed, so a problem shows while it is being typed.
            g.CurrentCellDirtyStateChanged += (s, e) => { if (g.IsCurrentCellDirty) g.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            g.CellValueChanged += (s, e) => Changed();
            g.RowsRemoved += (s, e) => Changed();
            g.DataError += (s, e) => { e.ThrowException = false; };
            return g;
        }

        private static DataGridViewTextBoxColumn TextColumn(string header, int weight, string tip)
        {
            return new DataGridViewTextBoxColumn { HeaderText = header, FillWeight = weight, ToolTipText = tip, SortMode = DataGridViewColumnSortMode.NotSortable };
        }

        /// <summary>Add, Remove, Move Up and Move Down for a table, and any further buttons.</summary>
        private Control[] GridButtons(DataGridView g, int firstColumn, params Control[] more)
        {
            var add = new Button { Text = "Add", AutoSize = true };
            var remove = new Button { Text = "Remove", AutoSize = true };
            var up = new Button { Text = "Move Up", AutoSize = true };
            var down = new Button { Text = "Move Down", AutoSize = true };
            Tip(add, "Adds a row at the end and starts typing in it.");
            Tip(remove, "Removes the selected row.");
            Tip(up, "Moves the selected row up. The list is read from the top.");
            Tip(down, "Moves the selected row down. The list is read from the top.");
            add.Click += (s, e) =>
            {
                var i = g.Rows.Add();
                if (g.Columns[0] is DataGridViewCheckBoxColumn) g.Rows[i].Cells[0].Value = true;
                g.CurrentCell = g.Rows[i].Cells[firstColumn];
                g.BeginEdit(true);
            };
            remove.Click += (s, e) => { if (g.CurrentRow != null) g.Rows.Remove(g.CurrentRow); };
            up.Click += (s, e) => Move(g, -1);
            down.Click += (s, e) => Move(g, 1);
            return new Control[] { add, remove, up, down }.Concat(more).ToArray();
        }

        private void Move(DataGridView g, int by)
        {
            var row = g.CurrentRow;
            if (row == null) return;
            var from = row.Index;
            var to = from + by;
            if (to < 0 || to >= g.Rows.Count) return;
            var column = g.CurrentCell != null ? g.CurrentCell.ColumnIndex : 0;
            g.EndEdit();
            Quietly(() =>
            {
                g.Rows.RemoveAt(from);
                g.Rows.Insert(to, row);
            });
            g.CurrentCell = g.Rows[to].Cells[column];
        }

        /// <summary>Replaces a list with the one FTF ships with (the factory rules file), when it can be read.</summary>
        private Button ShippedButton(Action<EditableCodeLists> show)
        {
            var b = new Button { Text = "Use the Shipped List", AutoSize = true };
            Tip(b, "Replaces this list with the one FTF ships with (the factory rules file), for Save to write.");
            b.Click += (s, e) =>
            {
                var shipped = Shipped();
                if (shipped == null) { Problem("The shipped rules file could not be read, so there is no shipped list to show."); return; }
                Quietly(() => show(shipped));
            };
            return b;
        }

        /// <summary>This drawing's layers, offered while a layer cell is typed in.</summary>
        private void OfferLayers(DataGridView grid, DataGridViewEditingControlShowingEventArgs e, int layerColumn)
        {
            var box = e.Control as TextBox;
            if (box == null) return;
            if (grid.CurrentCell != null && grid.CurrentCell.ColumnIndex == layerColumn)
            {
                var source = new AutoCompleteStringCollection();
                source.AddRange(LayerChoices().Select(o => o.ToString()).ToArray());
                box.AutoCompleteCustomSource = source;
                box.AutoCompleteSource = AutoCompleteSource.CustomSource;
                box.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            }
            else box.AutoCompleteMode = AutoCompleteMode.None;     // the editing box is shared by every column
        }

        private void Changed()
        {
            if (!_quiet) Stage();
        }

        /// <summary>Makes several changes to the controls and stages them once.</summary>
        private void Quietly(Action change)
        {
            _quiet = true;
            try { change(); }
            finally { _quiet = false; }
            Stage();
        }

        // ================================================================== RulesSectionPage

        protected override bool ShowSection(RuleDocument document)
        {
            var lists = document != null ? document.GetCodeLists() : (Shipped() ?? EditableCodeLists.Defaults());
            Show(lists);
            _commandsInFile = document == null || lists.LineworkCommandsInFile;
            return lists.LineworkCommandsInFile;
        }

        /// <summary>The shipped lists (only the linework commands are built into FTF itself; the
        /// others are the factory rules file's).</summary>
        protected override void ShowBuiltIn()
        {
            var shipped = Shipped();
            if (shipped != null) Show(shipped);
            else ShowCommands(EditableLineworkCommands.BuiltIn());
        }

        protected override void ApplyControls(RuleDocument document, ICollection<string> problems)
        {
            var lists = Read(problems);
            if (document != null && problems.Count == 0) document.ApplyCodeLists(lists);
        }

        protected override void CopySection(RuleDocument from, RuleDocument to)
        {
            from.CopyCodeListsTo(to);
        }

        protected override void Edited()
        {
            _candidate = null;
            if (Document == null) _candidate = Context.Rules;
            else
            {
                try { _candidate = Document.CompileCandidate(); }
                catch (ConfigException) { _candidate = null; }
                catch (Newtonsoft.Json.JsonException) { _candidate = null; }
            }
            var lists = Read(new List<string>());
            ShowNotes(lists);
            ShowTry();
        }

        // ================================================================== showing and reading

        private void Show(EditableCodeLists lists)
        {
            ShowIgnore(lists.IgnoreCodes);
            ShowSymbols(lists.SymbolLabels);
            _control.Lines = (lists.ControlCodes ?? new List<string>()).ToArray();
            _linework.Lines = (lists.LineworkCodes ?? new List<string>()).ToArray();
            ShowCommands(lists.LineworkCommands);
            ShowModifiers(lists.Modifiers);
        }

        private void ShowIgnore(IEnumerable<EditableIgnoreCode> entries)
        {
            _ignore.Rows.Clear();
            foreach (var entry in entries ?? Enumerable.Empty<EditableIgnoreCode>())
            {
                var i = _ignore.Rows.Add(entry.Code ?? string.Empty, entry.Reason ?? string.Empty, null);
                var cell = (DataGridViewComboBoxCell)_ignore.Rows[i].Cells[2];
                var expect = (entry.Expect ?? string.Empty).Trim();
                if (expect.Length == 0) cell.Value = Nothing;
                else if (string.Equals(expect, "surface", StringComparison.OrdinalIgnoreCase)) cell.Value = Surface;
                else
                {
                    // A value the file has that the page does not offer: shown as it is, and reported.
                    cell.Items.Add(expect);
                    cell.Value = expect;
                }
            }
        }

        private void ShowSymbols(IEnumerable<EditableSymbolLabel> entries)
        {
            _symbols.Rows.Clear();
            foreach (var s in entries ?? Enumerable.Empty<EditableSymbolLabel>())
                _symbols.Rows.Add(s.Enabled, s.Code ?? string.Empty, s.Layer ?? string.Empty, s.Label ?? string.Empty);
        }

        private void ShowCommands(EditableLineworkCommands commands)
        {
            var c = commands ?? EditableLineworkCommands.BuiltIn();
            for (var i = 0; i < EditableLineworkCommands.Keys.Length; i++)
                _commands.Rows[i].Cells[2].Value = string.Join(" ", c.For(EditableLineworkCommands.Keys[i]).ToArray());
            _escape.Text = c.Escape ?? string.Empty;
        }

        private void ShowModifiers(IEnumerable<EditableModifier> entries)
        {
            _modifiers.Rows.Clear();
            foreach (var m in entries ?? Enumerable.Empty<EditableModifier>())
                _modifiers.Rows.Add(m.Id ?? string.Empty, m.Pattern ?? string.Empty, m.Priority.ToString(CultureInfo.InvariantCulture),
                                    m.LabelSuffix ?? string.Empty, m.LabelFormat ?? string.Empty, m.LayerSuffix ?? string.Empty,
                                    m.Block ?? string.Empty, m.StemCountFrom ?? string.Empty, m.Flag);
        }

        private EditableCodeLists Read(ICollection<string> problems)
        {
            var lists = EditableCodeLists.Defaults();
            lists.LineworkCommandsInFile = _commandsInFile;

            foreach (DataGridViewRow row in _ignore.Rows)
            {
                var code = Cell(row, 0).Trim();
                var reason = Cell(row, 1).Trim();
                var shown = Cell(row, 2);
                var expect = shown == Surface ? "surface" : shown == Nothing ? string.Empty : shown.Trim();
                if (code.Length == 0 && reason.Length == 0 && expect.Length == 0) continue;
                lists.IgnoreCodes.Add(new EditableIgnoreCode { Code = code, Reason = reason, Expect = expect });
            }

            foreach (DataGridViewRow row in _symbols.Rows)
            {
                var code = Cell(row, 1).Trim();
                var layer = Cell(row, 2).Trim();
                var label = Cell(row, 3).Trim();
                if (code.Length == 0 && layer.Length == 0 && label.Length == 0) continue;
                lists.SymbolLabels.Add(new EditableSymbolLabel { Enabled = Checked(row, 0), Code = code, Layer = layer, Label = label });
            }

            lists.ControlCodes = EditableCodeLists.SplitCodes(_control.Text);
            lists.LineworkCodes = EditableCodeLists.SplitCodes(_linework.Text);

            for (var i = 0; i < EditableLineworkCommands.Keys.Length; i++)
                lists.LineworkCommands.Codes[EditableLineworkCommands.Keys[i]] = EditableCodeLists.SplitCodes(Cell(_commands.Rows[i], 2));
            lists.LineworkCommands.Escape = (_escape.Text ?? string.Empty).Trim();

            foreach (DataGridViewRow row in _modifiers.Rows)
            {
                var texts = Enumerable.Range(0, 8).Select(c => Cell(row, c)).ToList();
                if (texts.All(string.IsNullOrWhiteSpace)) continue;
                var priority = 0;
                var order = texts[2].Trim();
                if (order.Length > 0 && !int.TryParse(order, NumberStyles.Integer, CultureInfo.InvariantCulture, out priority))
                    problems.Add("Field Code Lists: " + EditableCodeLists.ModifiersTab + ", row " + (row.Index + 1).ToString(CultureInfo.InvariantCulture) +
                                 ": the order \"" + order + "\" is not a whole number.");
                lists.Modifiers.Add(new EditableModifier
                {
                    Id = texts[0].Trim(), Pattern = texts[1].Trim(), Priority = priority, LabelSuffix = texts[3], LabelFormat = texts[4],
                    LayerSuffix = texts[5].Trim(), Block = texts[6].Trim(), StemCountFrom = texts[7].Trim(), Flag = Checked(row, 8)
                });
            }
            return lists;
        }

        private static string Cell(DataGridViewRow row, int column)
        {
            var value = row.Cells[column].Value;
            return value == null ? string.Empty : Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        private static bool Checked(DataGridViewRow row, int column)
        {
            var value = row.Cells[column].Value;
            return value is bool && (bool)value;
        }

        private EditableCodeLists Shipped()
        {
            var path = Context != null && Context.RulesResolution != null ? Context.RulesResolution.FactoryPath : null;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            try { return RuleDocument.Load(path).GetCodeLists(); }
            catch (ConfigException) { return null; }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        // ================================================================== live notes

        private void ShowNotes(EditableCodeLists lists)
        {
            var unexplained = lists.IgnoreCodes.Where(i => !string.IsNullOrWhiteSpace(i.Code) && string.IsNullOrWhiteSpace(i.Reason)).Select(i => i.Code).ToList();
            SetNote(_ignoreNote, unexplained.Count > 0,
                Count(lists.IgnoreCodes.Count, "code is", "codes are") + " never drawn." +
                (unexplained.Count > 0
                    ? " No reason written for " + Names(unexplained) + ": Point Accountability lists their points for review until one is."
                    : lists.IgnoreCodes.Count > 0 ? " Each has its reason." : string.Empty));

            var off = lists.SymbolLabels.Count(s => !s.Enabled);
            var unused = new List<string>();
            if (_candidate != null)
            {
                var parser = new FieldCodeParser(_candidate);
                foreach (var s in lists.SymbolLabels.Where(s => s.Enabled && !string.IsNullOrWhiteSpace(s.Code)))
                {
                    var p = parser.Parse("preview", s.Code.Trim());
                    if (p.RuleId != FieldCodeParser.SymbolLabelRuleId) unused.Add(s.Code.Trim() + (p.Ignored ? " (never drawn or linework)" : p.Recognized ? " (rule " + p.RuleId + ")" : string.Empty));
                }
            }
            SetNote(_symbolNote, unused.Count > 0,
                Count(lists.SymbolLabels.Count(s => s.Enabled), "bare code is", "bare codes are") + " labelled with the code" +
                (off > 0 ? "; " + Count(off, "is", "are") + " turned off" : string.Empty) + "." +
                (unused.Count > 0 ? " Never labelled from here, because another list or a rule reads them first: " + Names(unused) + "." : string.Empty));

            SetNote(_controlNote, false, Count(lists.ControlCodes.Count, "control code", "control codes") + ".");
            SetNote(_lineworkNote, false, Count(lists.LineworkCodes.Count, "linework code", "linework codes") + ".");
            SetNote(_commandNote, !_commandsInFile, _commandsInFile
                ? string.Empty
                : "The rules file lists no linework commands, so the built-in PMX-Universal set is shown; saving writes it into the file.");
            SetNote(_modifierNote, false, lists.Modifiers.Count == 0
                ? "No modifiers: a word after a code that a rule does not read is reported as unrecognised."
                : "Applied in this order: " + string.Join(", ", lists.Modifiers.OrderBy(m => m.Priority).Select(m =>
                      (string.IsNullOrWhiteSpace(m.Id) ? "(no name)" : m.Id.Trim()) + " (" + m.Priority.ToString(CultureInfo.InvariantCulture) + ")").ToArray()) + ".");
        }

        private static void SetNote(Label note, bool attention, string text)
        {
            note.ForeColor = attention ? Pending : SystemColors.GrayText;
            note.Text = text ?? string.Empty;
        }

        private static string Count(int n, string one, string many)
        {
            return n.ToString(CultureInfo.InvariantCulture) + " " + (n == 1 ? one : many);
        }

        private static string Names(IList<string> names)
        {
            return string.Join(", ", names.Take(12).ToArray()) + (names.Count > 12 ? ", ..." : string.Empty);
        }

        private void ShowTry()
        {
            if (_try == null || _tryResult == null) return;
            var text = (_try.Text ?? string.Empty).Trim();
            if (text.Length == 0)
            {
                _tryResult.ForeColor = SystemColors.GrayText;
                _tryResult.Text = string.Empty;
                return;
            }
            if (_candidate == null)
            {
                _tryResult.ForeColor = Pending;
                _tryResult.Text = "The lists have a problem (shown below), so they cannot be tried until it is fixed.";
                return;
            }
            var lines = CodeListPreview.Read(_candidate, text);
            _tryResult.ForeColor = SystemColors.ControlText;
            _tryResult.Text = lines.Count == 0 ? "Nothing in these lists reads it." : string.Join(Environment.NewLine, lines.ToArray());
        }
    }
}
