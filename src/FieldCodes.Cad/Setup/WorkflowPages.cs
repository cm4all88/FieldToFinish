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

    // ------------------------------------------------------------------ symbols

    /// <summary>
    /// Settings > Symbols and Rotations: how FTFORIENT and FTFPOINTS turn the symbols Civil 3D
    /// draws. READ ONLY here: the standard lives in the rules file's "orientation" section
    /// (office standard), so the page shows what is in force and says where to change it.
    /// </summary>
    internal sealed class SymbolsPage : SetupPage
    {
        private ListView _symbols;

        public override string Title { get { return "Symbols and Rotations"; } }
        public override string AffectedCommands { get { return "FTFORIENT, FTFORIENTRESET, FTFPOINTS (field azimuths)"; } }
        public override bool IsReadOnly { get { return true; } }

        protected override void BuildBody()
        {
            Note("Read only: the symbol standard is the office's, in rules.json > \"orientation\". " +
                 "Each symbol's zero is the direction its block points at rotation 0; a marker is turned by " +
                 "the facing direction minus that zero. Symmetric symbols (period 0) are never turned.");
            _symbols = new ListView
            {
                View = View.Details, FullRowSelect = true, GridLines = true, Width = 620, Height = 230,
                HeaderStyle = ColumnHeaderStyle.Clickable
            };
            _symbols.Columns.Add("Codes", 120);
            _symbols.Columns.Add("Kind", 90);
            _symbols.Columns.Add("Block", 90);
            _symbols.Columns.Add("Zero (CAD deg)", 90);
            _symbols.Columns.Add("Repeats", 60);
            _symbols.Columns.Add("Meaning", 170);
            AddFullWidth(_symbols);

            var o = Context.Rules != null ? Context.Rules.Orientation : null;
            if (o != null)
            {
                ReadOnlyRow("Turned at or above confidence", o.ApplyMinimumConfidence.ToString("0.00", CultureInfo.InvariantCulture));
                ReadOnlyRow("Weakest evidence applied", o.ApplyWeakestTier.ToString());
                ReadOnlyRow("QC layers (not plotted)", o.QcLayer + ", " + o.QcInferredLayer);
                foreach (var r in o.Symbols ?? new List<FieldCodes.Orient.SymbolRule>())
                    _symbols.Items.Add(new ListViewItem(new[]
                    {
                        string.Join(" ", (r.Codes ?? new List<string>()).ToArray()), r.Kind.ToString(), r.Block ?? string.Empty,
                        r.ReferenceCadDegrees.ToString("0", CultureInfo.InvariantCulture),
                        r.Period <= 0 ? "never turned" : r.Period.ToString("0", CultureInfo.InvariantCulture),
                        r.Meaning ?? string.Empty
                    }));
            }
            Note("Run FTFORIENT > Preview to see every decision and its evidence before anything turns; " +
                 "FTFORIENTRESET puts every marker back. A marker someone turned by hand is kept.");
        }

        public override void LoadFrom(FtfSettings s) { }
        public override void SaveTo(FtfSettings s, ICollection<string> problems) { }
        public override void RestoreDefaults(FtfSettings s) { }
    }

    // ------------------------------------------------------------------ profiles

    /// <summary>
    /// Settings > Profiles: the settings levels in force for this drawing (project file, a named
    /// project/client profile, the user's own defaults, the office standard, built-in), the
    /// profiles on this computer, and import / export of a whole configuration. A profile file
    /// keeps only what differs from the office standard and the saver's defaults. Saving a
    /// profile or a project file never touches the office file.
    /// </summary>
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
    /// Settings > Diagnostics: the codes marked "intentionally ignored" on the Unknown Codes
    /// screen. An ignored code is reported as informational, not as needing configuration; a
    /// rule added for it later still wins. Codes are added from Unknown Codes (Mark Ignored).
    /// </summary>
    internal sealed class DiagnosticsPage : SetupPage
    {
        private System.Windows.Forms.ListBox _codes;

        public override string Title { get { return "Diagnostics"; } }
        public override string AffectedCommands { get { return "FTFPOINTS and the FTF window's Unknown Codes, Overview and Reports"; } }

        protected override void BuildBody()
        {
            Heading("Codes you marked as intentionally ignored");
            _codes = new System.Windows.Forms.ListBox { Width = 220, Height = 160, IntegralHeight = false, Sorted = true };
            var remove = new System.Windows.Forms.Button { Text = "Stop Ignoring" };
            remove.Click += (s, e) =>
            {
                if (_codes.SelectedItem != null) { _codes.Items.Remove(_codes.SelectedItem); MarkChanged(); }
            };
            AddWithSideButtons(_codes, SideButtons(remove));
            Note("Add codes from Unknown Codes > Mark Ignored. A code also covers its sequence numbers (XYZ covers XYZ2). " +
                 "Errors (blank or unreadable descriptions) are never hidden by this list.");
        }

        public override void LoadFrom(FtfSettings s)
        {
            _codes.Items.Clear();
            foreach (var c in s.Diagnostics != null ? s.Diagnostics.Codes() : new List<string>()) _codes.Items.Add(c);
        }

        public override void SaveTo(FtfSettings s, ICollection<string> problems)
        {
            if (s.Diagnostics == null) s.Diagnostics = new DiagnosticsSettings();
            s.Diagnostics.IgnoredCodes = _codes.Items.Cast<object>().Select(o => o.ToString()).ToList();
        }

        public override void RestoreDefaults(FtfSettings s)
        {
            if (s.Diagnostics != null) s.Diagnostics.RestoreDefaults();
            LoadFrom(s);
        }
    }
}
