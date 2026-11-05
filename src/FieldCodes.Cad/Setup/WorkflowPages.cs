using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using FieldCodes.Annotation;
using FieldCodes.Settings;
using Newtonsoft.Json;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace FieldCodes.Cad.Setup
{
    // ------------------------------------------------------------------ driplines

    /// <summary>Settings > Driplines: the canopy trimming and linetype (formerly half of
    /// "Trees &amp; Driplines"). The drip layer belongs to each tree rule (Point Features).</summary>
    [SettingsPage(Group = "FieldToFinish", Label = "Driplines", Order = 110, Sections = "drip", Commands = "FTFDRIP")]
    internal sealed class DriplinesPage : SetupPage
    {
        private CheckBox _unify;
        private ComboBox _dripLinetype;

        public override string Title { get { return "Driplines"; } }
        public override string AffectedCommands { get { return "FTFDRIP"; } }

        protected override void BuildBody()
        {
            _unify = CheckRow("Trim overlapping canopies to their outer envelope",
                "Off draws every canopy as a full circle, overlaps and all. A code " +
                "rule that states its own preference wins over this.");

            var linetypes = new List<object> { "(layer's linetype)" };
            foreach (var name in Context.Drawing.Linetypes) linetypes.Add(name);
            _dripLinetype = ComboRow("Drip linetype", linetypes,
                "The linetype the drip circles and arcs are drawn with -- typically a " +
                "dashed standard. A linetype the drawing does not have yet is loaded " +
                "from acad.lin automatically.");
            Note("The drip LAYER belongs to each tree rule (Point Features), so different species " +
                 "can go on different layers; Settings > Layers can send it to a project layer.");
            _unify.CheckedChanged += (s, e) => MarkChanged();
        }

        public override void LoadFrom(FtfSettings s)
        {
            _unify.Checked = s.Drip.UnifyByDefault;
            ComboHelp.SelectOrAdd(_dripLinetype, s.Drip.Linetype, true);
        }

        public override void SaveTo(FtfSettings s, ICollection<string> problems)
        {
            s.Drip.UnifyByDefault = _unify.Checked;
            s.Drip.Linetype = _dripLinetype.SelectedIndex <= 0
                ? string.Empty
                : Convert.ToString(_dripLinetype.SelectedItem);
        }

        public override void RestoreDefaults(FtfSettings s)
        {
            s.Drip.RestoreDefaults();
            LoadFrom(s);
        }
    }

    // ------------------------------------------------------------------ label all

    /// <summary>
    /// Settings > Label All: which feature families Label All (and the label and tag stages of
    /// Process Drawing) annotate, and what is never labelled. How trees are annotated is on
    /// the Trees page; whether a schedule is drawn is on the Schedule page.
    /// </summary>
    [SettingsPage(Group = "FieldToFinish", Label = "Label All", Order = 80, Sections = "annotation", Commands = "FTFLABELALL,FTFLABELS,FTFTAGS,FTFTABLE,FTFRUN")]
    internal sealed class LabelAllPage : SetupPage
    {
        private CheckedListBox _families;
        private CheckBox _depthNotes;
        private CheckBox _manual;
        private TextBox _search;
        private TextBox _noLabel;
        private Label _treeNote;

        public override string Title { get { return "Label All"; } }
        public override string AffectedCommands { get { return "FTFLABELALL, FTFLABELS, FTFTAGS, Process Drawing"; } }

        protected override void BuildBody()
        {
            Heading("Feature families Label All processes");
            _families = new CheckedListBox { Width = 320, Height = 150, CheckOnClick = true, IntegralHeight = false };
            foreach (var c in AnnotationPolicy.All()) _families.Items.Add(new Family(c));
            _families.ItemCheck += (s, e) => MarkChanged();
            AddFullWidth(_families);
            Note("A family that is not ticked is left alone: no new labels, and labels FTF drew there " +
                 "before are kept, not erased. Storm and sewer structures are off by default: their " +
                 "dips are recorded and labelled by the Dip Builder.");
            _treeNote = LiveNote();

            Heading("Never labelled");
            _depthNotes = CheckRow("Keep dip measurements out of label text",
                "A crew note such as \"12 RCP N 6.41\", \"MD 6.25\" or \"BOT 7.82\" is not used as a label: the " +
                "point is labelled from its code, and the Dip Builder labels the depths.");
            _noLabel = TextRow("Words that mean \"do not label\"",
                "Separated by spaces or commas; matched after the code or in the note: \"PP 1234 NOLABEL\".", 240);

            Heading("Labels already on the plan");
            _manual = CheckRow("Do not label a point that already has a hand label saying the same thing",
                "Text someone typed (not FTF's) near the point, with the same words, counts as its label.");
            _search = TextRow("Look this far from the point (plotted inches)", null);
            Note("Labels FTF placed and someone then moved are always kept where they were put.");
        }

        private sealed class Family
        {
            public readonly AnnotationCategory Category;
            public Family(AnnotationCategory c) { Category = c; }
            public override string ToString() { return AnnotationPolicy.Name(Category); }
        }

        public override void LoadFrom(FtfSettings s)
        {
            for (var i = 0; i < _families.Items.Count; i++)
                _families.SetItemChecked(i, s.Annotation.Includes(((Family)_families.Items[i]).Category));
            _depthNotes.Checked = s.Annotation.ExcludeDepthNotes;
            _noLabel.Text = string.Join(" ", (s.Annotation.NoLabelWords ?? new List<string>()).ToArray());
            _manual.Checked = s.Annotation.RespectManualLabels;
            _search.Text = Fmt(s.Annotation.ExistingLabelSearchPlotted);
            _treeNote.Text = "Trees: " + Describe(s.Annotation.TreeAnnotation) + " (Trees page). Tree schedule: " +
                             (s.Annotation.ScheduleTrees ? "drawn" : "not drawn") + " (Schedule page).";
        }

        private static string Describe(TreeAnnotation t)
        {
            switch (t)
            {
                case TreeAnnotation.TagOnly: return "tag only";
                case TreeAnnotation.None: return "no tree label";
                default: return "description only";
            }
        }

        public override void SaveTo(FtfSettings s, ICollection<string> problems)
        {
            var families = new List<AnnotationCategory>();
            for (var i = 0; i < _families.Items.Count; i++)
                if (_families.GetItemChecked(i)) families.Add(((Family)_families.Items[i]).Category);
            s.Annotation.Categories = families;
            s.Annotation.ExcludeDepthNotes = _depthNotes.Checked;
            s.Annotation.NoLabelWords = (_noLabel.Text ?? string.Empty)
                .Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(w => w.Trim().ToUpperInvariant()).Distinct().ToList();
            s.Annotation.RespectManualLabels = _manual.Checked;
            var search = s.Annotation.ExistingLabelSearchPlotted;
            ReadDouble(_search, "Label All: search distance for hand labels", v => v >= 0 && v <= 5,
                       "must be between 0 and 5 plotted inches", problems, ref search);
            s.Annotation.ExistingLabelSearchPlotted = search;
        }

        public override void RestoreDefaults(FtfSettings s)
        {
            var keepTrees = s.Annotation.TreeAnnotation;
            var keepSchedule = s.Annotation.ScheduleTrees;
            s.Annotation.RestoreDefaults();
            s.Annotation.TreeAnnotation = keepTrees;          // their own pages restore these
            s.Annotation.ScheduleTrees = keepSchedule;
            LoadFrom(s);
        }
    }

    // ------------------------------------------------------------------ profiles

    /// <summary>
    /// Settings > Profiles: the settings levels in force for this drawing (project file, a named
    /// project/client profile, the user's own defaults, the office standard, built-in), the
    /// profiles on this computer, and import / export of a whole configuration. A profile file
    /// keeps only what differs from the office standard and the saver's defaults. Saving a
    /// profile or a project file never touches the office file.
    /// </summary>
    [SettingsPage(Group = "Administration", Label = "Profiles", Order = 10, Commands = "FTFPROFILE,*")]
    internal sealed class ProfilesPage : SetupPage
    {
        private ListBox _profiles;
        private Label _active;
        private FtfSettings _settings;

        public override string Title { get { return "Profiles"; } }
        public override string AffectedCommands { get { return "FTFPROFILE; every command reads the settings in force"; } }
        public override bool IsReadOnly { get { return true; } }

        protected override void BuildBody()
        {
            Heading("Settings levels for this drawing (highest first)");
            _active = LiveNote();
            _active.ForeColor = SystemColors.ControlText;

            Heading("Project and client profiles on this computer");
            _profiles = new ListBox { Width = 300, Height = 120, IntegralHeight = false };
            var use = new Button { Text = "Use for Drawing" };
            use.Click += (s, e) => UseProfile();
            var none = new Button { Text = "Stop Using" };
            none.Click += (s, e) => UseProfile(clear: true);
            var save = new Button { Text = "Save As Profile..." };
            save.Click += (s, e) => SaveAsProfile();
            AddWithSideButtons(_profiles, SideButtons(use, none, save));
            Note("A profile holds a client's or project's settings (layers, labels, standards): only the values " +
                 "that differ from the office standard and your defaults. It sits above your defaults and below the " +
                 "project file. Selecting one for a drawing never changes the office or your own settings.");

            Heading("Import and export");
            var export = new Button { Text = "Export Settings...", AutoSize = true };
            export.Click += (s, e) => Export();
            var import = new Button { Text = "Import Settings...", AutoSize = true };
            import.Click += (s, e) => Import();
            var row = new FlowLayoutPanel { AutoSize = true };
            row.Controls.Add(export);
            row.Controls.Add(import);
            AddFullWidth(row);
            Note("Export writes every setting in force (as last saved) to one complete file. Import reads one, " +
                 "checks it, and loads it into this window: a complete file replaces every value, a file of " +
                 "overrides changes only the values it holds. Nothing is written until you Save, to the level " +
                 "chosen below.");
        }

        public override void LoadFrom(FtfSettings s)
        {
            _settings = s;
            var r = Context.Resolution;
            _active.Text = r == null ? "Shipped defaults." : ProvenancePage.DescribeLevels(r);
            _profiles.Items.Clear();
            foreach (var p in FtfSettings.ListProfiles()) _profiles.Items.Add(p);
            if (r != null && !string.IsNullOrWhiteSpace(r.ProfileName)) _profiles.SelectedItem = r.ProfileName;
        }

        private void UseProfile(bool clear = false)
        {
            var name = clear ? string.Empty : Convert.ToString(_profiles.SelectedItem);
            if (!clear && string.IsNullOrWhiteSpace(name)) return;
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                DrawingStore.WriteProfileName(doc.Database, tr, name);
                tr.Commit();
            }
            FtfSession.InvalidateSettings();
            MessageBox.Show(_profiles, clear ? "This drawing no longer uses a profile. Reopen Settings to see the settings now in force."
                                             : "This drawing now uses profile \"" + name + "\". Reopen Settings to edit it.", "Profiles");
        }

        private void SaveAsProfile()
        {
            var name = Prompt("Profile name (project or client):");
            if (string.IsNullOrWhiteSpace(name)) return;
            // The drawing's own profile: the window reloads the values in force afterwards,
            // which would lose edits not yet saved.
            var own = string.Equals(name, Context.Resolution.ProfileName, StringComparison.OrdinalIgnoreCase);
            if (own && !ProvenancePage.NoUnsavedEdits(_profiles)) return;
            if (FtfSettings.ProfileExists(name) &&
                MessageBox.Show(_profiles, "Profile \"" + name + "\" exists. Replace it with the settings shown in this window (as last saved)?",
                                "Profiles", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            // Stored as the values that differ from the office standard and your defaults.
            try { FtfSettings.SaveProfile(name, _settings); }
            catch (ConfigException ex) { MessageBox.Show(_profiles, ex.Message, "Profile not saved"); return; }
            catch (IOException ex) { MessageBox.Show(_profiles, ex.Message, "Profile not saved"); return; }
            catch (UnauthorizedAccessException ex) { MessageBox.Show(_profiles, ex.Message, "Profile not saved"); return; }
            FtfSession.InvalidateSettings();
            if (own && Context.Reload != null) Context.Reload();
            else LoadFrom(_settings);
        }

        private void Export()
        {
            using (var dialog = new SaveFileDialog { Filter = "FTF settings (*.json)|*.json", FileName = "ftf-settings.json" })
            {
                if (dialog.ShowDialog() != DialogResult.OK) return;
                // A detached copy, so an export is always one complete file -- even when the file
                // chosen happens to be one of this drawing's level files.
                var copy = SettingsLayers.FromJson(SettingsLayers.ToJson(_settings));
                try { copy.Save(dialog.FileName); MessageBox.Show(_profiles, "Exported to " + dialog.FileName, "Profiles"); }
                catch (ConfigException ex) { MessageBox.Show(_profiles, ex.Message, "Not exported"); }
                catch (IOException ex) { MessageBox.Show(_profiles, ex.Message, "Not exported"); }
                catch (UnauthorizedAccessException ex) { MessageBox.Show(_profiles, ex.Message, "Not exported"); }
            }
        }

        private void Import()
        {
            using (var dialog = new OpenFileDialog { Filter = "FTF settings (*.json)|*.json" })
            {
                if (dialog.ShowDialog() != DialogResult.OK) return;
                FtfSettings loaded;
                // A complete file replaces every value; a file of overrides changes only its own.
                try { loaded = SettingsLayers.ReadForImport(dialog.FileName, _settings); }
                catch (ConfigException ex) { MessageBox.Show(_profiles, ex.Message, "Not imported"); return; }
                catch (IOException ex) { MessageBox.Show(_profiles, ex.Message, "Not imported"); return; }
                var problems = new List<string>();
                loaded.Validate(problems);
                if (problems.Count > 0) { MessageBox.Show(_profiles, string.Join("\n", problems.ToArray()), "Not imported"); return; }
                if (MessageBox.Show(_profiles, "Load these settings into the window? Nothing is saved until you press Save.",
                                    "Import Settings", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
                // Copy section by section onto the settings the window holds, then rebuild the pages.
                foreach (var prop in typeof(FtfSettings).GetProperties().Where(p => p.CanWrite && p.CanRead &&
                         p.GetCustomAttributes(typeof(JsonPropertyAttribute), true).Length > 0))
                    prop.SetValue(_settings, prop.GetValue(loaded, null), null);
                if (Context.Reload != null) Context.Reload();
                MarkChanged();
            }
        }

        private string Prompt(string caption)
        {
            using (var form = new Form { Text = "Profiles", Width = 360, Height = 130, StartPosition = FormStartPosition.CenterParent,
                                          FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false })
            {
                var label = new Label { Text = caption, Dock = DockStyle.Top, Height = 22 };
                var box = new TextBox { Dock = DockStyle.Top };
                var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Dock = DockStyle.Bottom };
                form.Controls.Add(ok);
                form.Controls.Add(box);
                form.Controls.Add(label);
                form.AcceptButton = ok;
                return form.ShowDialog(_profiles) == DialogResult.OK ? (box.Text ?? string.Empty).Trim() : null;
            }
        }

        public override void SaveTo(FtfSettings s, ICollection<string> problems) { }
        public override void RestoreDefaults(FtfSettings s) { }
    }
}

namespace FieldCodes.Cad.Setup
{
    /// <summary>
    /// Settings > Diagnostics: the codes the office has marked "intentionally ignored". An
    /// ignored code is reported as informational, not as needing configuration; a rule added for
    /// it later still wins. Codes are added here (Add Code...) or from Unknown Codes (Mark
    /// Ignored); each row says why, what accounts for its points instead (a surface built from
    /// them, or nothing), and who decided it and when.
    /// </summary>
    [SettingsPage(Group = "Administration", Label = "Diagnostics", Order = 20, Sections = "diagnostics", Commands = "FTFPOINTS,FTF,FTFQC")]
    internal sealed class DiagnosticsPage : SetupPage
    {
        private DataGridView _codes;

        private const int ColCode = 0, ColReason = 1, ColExpect = 2, ColDecided = 3;

        // What an ignore entry says holds its points instead ("expect" in the file), in the
        // order the column offers them. Index IS the mapping, so keep these together.
        private static readonly string[] ExpectValues = { string.Empty, "surface" };
        private static readonly string[] ExpectLabels = { "Nothing (just excluded)", "A surface built from them" };

        private bool _filling;

        public override string Title { get { return "Diagnostics"; } }
        public override string AffectedCommands { get { return "FTFPOINTS, FTFQC and the FTF window's Unknown Codes, Overview and Reports"; } }

        protected override void BuildBody()
        {
            Heading("Codes you marked as intentionally ignored");
            Note("FTF draws nothing for these codes and reports their points as ignored by you, not as unknown codes. " +
                 "A code also covers its sequence numbers (XYZ covers XYZ2). A rule added for the code later still wins. " +
                 "Errors (blank or unreadable descriptions) are never hidden by this list.");

            _codes = new DataGridView
            {
                Width = NoteWidth, Height = 200, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
                RowHeadersVisible = false, BackgroundColor = SystemColors.Window, MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            };
            _codes.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Code", ReadOnly = true, FillWeight = 18,
                ToolTipText = "The field code, as shot. Add Code... adds one; Stop Ignoring takes one off." });
            _codes.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Why it is ignored", FillWeight = 46,
                ToolTipText = "The reason, in plain words. Point Accountability (FTFQC) lists the points of a code ignored without a reason for review." });
            var expect = new DataGridViewComboBoxColumn { HeaderText = "What accounts for its points", FillWeight = 36, FlatStyle = FlatStyle.Flat,
                ToolTipText = "A surface built from them: Point Accountability (FTFQC) checks each of its points is in a surface (a ground shot used " +
                              "for the TIN). Nothing: the points are simply excluded, with the reason." };
            expect.Items.AddRange(ExpectLabels.Cast<object>().ToArray());
            _codes.Columns.Add(expect);
            _codes.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Decided by", ReadOnly = true, FillWeight = 26,
                ToolTipText = "Who last set the reason or what accounts for the points, and when. Recorded for you." });
            _codes.DataError += (s, e) => { e.ThrowException = false; };
            _codes.CurrentCellDirtyStateChanged += (s, e) =>
            {
                if (_codes.IsCurrentCellDirty && _codes.CurrentCell is DataGridViewComboBoxCell)
                    _codes.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            _codes.CellValueChanged += (s, e) => OnEdited(e.RowIndex, e.ColumnIndex);

            var add = new Button { Text = "Add Code..." };
            add.Click += (s, e) => AddCode();
            Tip(add, "Type a field code to ignore. FTFPOINTS then draws nothing for it and reports its points as ignored by you.");
            var remove = new Button { Text = "Stop Ignoring" };
            remove.Click += (s, e) =>
            {
                if (_codes.CurrentRow == null) return;
                _codes.Rows.Remove(_codes.CurrentRow);
                MarkChanged();
            };
            Tip(remove, "Takes the selected code off the list: its points are reported as unknown codes again until a rule is added.");
            AddWithSideButtons(_codes, SideButtons(add, remove));
            Note("Give every code a reason: Point Accountability lists the points of a code ignored without one for review. " +
                 "Codes can also be marked from the FTF window: Unknown Codes > Mark Ignored.");
        }

        private static string ExpectLabel(string expect)
        {
            var index = Array.FindIndex(ExpectValues, v => string.Equals(v, (expect ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase));
            return ExpectLabels[index < 0 ? 0 : index];
        }

        private static string ExpectValue(object label)
        {
            var index = Array.IndexOf(ExpectLabels, Convert.ToString(label));
            return ExpectValues[index < 0 ? 0 : index];
        }

        private static string DecidedBy(CodeNote note)
        {
            if (note == null) return string.Empty;
            var who = string.IsNullOrWhiteSpace(note.By) ? string.Empty : note.By.Trim();
            var when = note.At.HasValue ? note.At.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : string.Empty;
            return who.Length > 0 && when.Length > 0 ? who + ", " + when : who + when;
        }

        /// <summary>One row per ignored code; the row keeps its own copy of the note.</summary>
        private void AddRow(CodeNote note)
        {
            _filling = true;
            try
            {
                var index = _codes.Rows.Add(note.Code, note.Reason ?? string.Empty, ExpectLabel(note.Expect), DecidedBy(note));
                _codes.Rows[index].Tag = note;
            }
            finally { _filling = false; }
        }

        private void OnEdited(int row, int column)
        {
            if (_filling || row < 0 || row >= _codes.Rows.Count) return;
            if (column != ColReason && column != ColExpect) return;
            var note = _codes.Rows[row].Tag as CodeNote;
            if (note == null) return;
            note.Reason = (Convert.ToString(_codes.Rows[row].Cells[ColReason].Value) ?? string.Empty).Trim();
            note.Expect = ExpectValue(_codes.Rows[row].Cells[ColExpect].Value);
            note.By = Environment.UserName;
            note.At = DateTime.Now;
            _filling = true;
            try { _codes.Rows[row].Cells[ColDecided].Value = DecidedBy(note); }
            finally { _filling = false; }
            MarkChanged();
        }

        private IEnumerable<string> Listed()
        {
            return _codes.Rows.Cast<DataGridViewRow>().Select(r => r.Tag as CodeNote).Where(n => n != null).Select(n => n.Code);
        }

        private void AddCode()
        {
            var typed = Prompt.Show("Field code to ignore (letters, digits, - or _):", "Ignore a code");
            if (typed == null) return;
            var code = typed.Trim().ToUpperInvariant();
            string problem = null;
            if (code.Length == 0) problem = "Type the code as it is shot, e.g. XYZ.";
            else if (!System.Text.RegularExpressions.Regex.IsMatch(code, @"^[A-Za-z0-9_-]+$"))
                problem = "\"" + code + "\" is not a plain field code: use letters, digits, - or _ only (no spaces).";
            else
            {
                var covering = Listed().FirstOrDefault(c => DiagnosticsSettings.Matches(new[] { c }, code));
                if (covering != null)
                    problem = string.Equals(covering, code, StringComparison.OrdinalIgnoreCase)
                        ? code + " is already ignored."
                        : code + " is already covered by " + covering + " (a code covers its sequence numbers).";
            }
            if (problem != null)
            {
                MessageBox.Show(_codes.FindForm(), problem, "Ignore a code", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            AddRow(new CodeNote { Code = code, By = Environment.UserName, At = DateTime.Now });
            var last = _codes.Rows[_codes.Rows.Count - 1];
            _codes.CurrentCell = last.Cells[ColReason];
            _codes.BeginEdit(true);
            MarkChanged();
        }

        public override void LoadFrom(FtfSettings s)
        {
            _codes.Rows.Clear();
            var d = s.Diagnostics ?? new DiagnosticsSettings();
            foreach (var code in d.Codes())
            {
                var note = d.NoteFor(code) ?? new CodeNote { Code = code };
                // A copy: editing a row must not change the loaded settings before Save.
                AddRow(new CodeNote { Code = code, Reason = note.Reason, Expect = note.Expect, By = note.By, At = note.At });
            }
        }

        public override void SaveTo(FtfSettings s, ICollection<string> problems)
        {
            _codes.EndEdit();
            if (s.Diagnostics == null) s.Diagnostics = new DiagnosticsSettings();
            var list = new NotedCodeList();
            foreach (DataGridViewRow row in _codes.Rows)
            {
                var note = row.Tag as CodeNote;
                if (note == null || string.IsNullOrWhiteSpace(note.Code)) continue;
                list.Add(note.Code);
                var reason = (note.Reason ?? string.Empty).Trim();
                var expect = (note.Expect ?? string.Empty).Trim();
                if (reason.Length > 0 || expect.Length > 0)
                    list.SetNote(new CodeNote
                    {
                        Code = note.Code,
                        Reason = reason.Length > 0 ? reason : null,
                        Expect = expect.Length > 0 ? expect : null,
                        By = note.By,
                        At = note.At
                    });
            }
            s.Diagnostics.IgnoredCodes = list;
        }

        public override void RestoreDefaults(FtfSettings s)
        {
            if (s.Diagnostics != null) s.Diagnostics.RestoreDefaults();
            LoadFrom(s);
        }
    }
}
