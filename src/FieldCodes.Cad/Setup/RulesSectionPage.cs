using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using FieldCodes.Editing;
using FieldCodes.Settings;

namespace FieldCodes.Cad.Setup
{
    /// <summary>
    /// A Settings page for one section of the rules file (Benches, Wetland Buffers). It reads the
    /// active rules file, stages each edit on a RuleDocument (comments and the rest of the file
    /// kept) and checks it as it is typed. Its own Save button re-reads the file, puts this
    /// section on it (so another page's save in between is kept), checks it and writes it to the
    /// office standard or the drawing override -- never the factory file. Discard Edits reads the
    /// file again; Restore Defaults shows the built-in values for Save to write. The window's
    /// Save writes ftf-settings.json only.
    ///
    /// A page built on this one makes its rows in BuildRows, calls Stage() from each control's
    /// change event, and says how its section is shown, read from the controls and copied.
    /// </summary>
    internal abstract class RulesSectionPage : SetupPage
    {
        private static readonly Color Bad = Color.FromArgb(170, 0, 0);

        /// <summary>For notes that ask for attention without being errors.</summary>
        protected static readonly Color Pending = Color.FromArgb(160, 90, 0);

        private RulesResolution _resolution;
        private string _activePath;
        private bool _dirty;
        private Label _source;
        private Label _status;
        private Label _unsaved;
        private Button _save;

        /// <summary>The document being edited; null when the rules file could not be read.</summary>
        protected RuleDocument Document { get; private set; }

        /// <summary>True while the controls are being filled, so their change events stage nothing.</summary>
        protected bool Loading { get; private set; }

        /// <summary>The section's key in the rules file ("bench").</summary>
        protected abstract string SectionKey { get; }

        /// <summary>What the edits are, for messages ("bench").</summary>
        protected abstract string WhatChanges { get; }

        /// <summary>The Save button's text ("Save Bench Settings").</summary>
        protected abstract string SaveText { get; }

        /// <summary>Who reads it ("FTFBENCH").</summary>
        protected abstract string UsedBy { get; }

        /// <summary>Adds the page's own rows.</summary>
        protected abstract void BuildRows();

        /// <summary>Fills the controls from the document, or with the built-in values when it is
        /// null. True when the file has the section.</summary>
        protected abstract bool ShowSection(RuleDocument document);

        /// <summary>Fills the controls with the built-in values.</summary>
        protected abstract void ShowBuiltIn();

        /// <summary>Reads the controls, adding a message per field that cannot be read; puts them
        /// on the document when it is not null and every field could be read.</summary>
        protected abstract void ApplyControls(RuleDocument document, ICollection<string> problems);

        /// <summary>Puts this page's section of one document onto another.</summary>
        protected abstract void CopySection(RuleDocument from, RuleDocument to);

        /// <summary>After every load and edit: refresh previews and live notes. Optional.</summary>
        protected virtual void Edited() { }

        protected sealed override void BuildBody()
        {
            _source = LiveNote();
            BuildRows();

            _status = LiveNote();
            _save = new Button { Text = SaveText, AutoSize = true, Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold) };
            var discard = new Button { Text = "Discard Edits", AutoSize = true };
            _unsaved = new Label { AutoSize = true, ForeColor = Pending, Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold), Margin = new Padding(8, 8, 0, 0) };
            Tip(_save, "Writes these settings to the active rules configuration (atomic; the previous version is kept as .bak). The window's Save does not write the rules file.");
            Tip(discard, "Throws away what has not been saved and reads the rules file again.");
            _save.Click += (s, e) => SaveClicked();
            discard.Click += (s, e) => Reload(true);
            var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = new Padding(0, 8, 0, 0) };
            buttons.Controls.Add(_save);
            buttons.Controls.Add(discard);
            buttons.Controls.Add(_unsaved);
            AddFullWidth(buttons);

            Reload(false);
        }

        // The rules file is read and written by the page itself, not through ftf-settings.json.
        public override void LoadFrom(FtfSettings settings) { }

        public override void SaveTo(FtfSettings settings, ICollection<string> problems) { }

        public override void RestoreDefaults(FtfSettings settings)
        {
            Fill(ShowBuiltIn);
            Stage();
            if (_dirty) Info("The built-in values are shown. " + SaveText + " writes them to the rules file.");
        }

        /// <summary>Call from every control's change event: puts the edit on the staged document
        /// and shows what is wrong, if anything.</summary>
        protected void Stage()
        {
            if (Loading || _status == null) return;      // still building the page
            var problems = new List<string>();
            ApplyControls(Document, problems);
            if (problems.Count == 0 && Document != null) problems.AddRange(Document.Validate());
            SetDirty(true);
            Edited();
            if (problems.Count > 0) Problem(string.Join(Environment.NewLine, problems.ToArray()));
            else Info(string.Empty);
        }

        /// <summary>This drawing's layers for a layer box, with the given names always offered.</summary>
        protected IEnumerable<object> LayerChoices(params string[] always)
        {
            var layers = new List<string>();
            if (Context != null && Context.Drawing != null && Context.Drawing.Layers != null) layers.AddRange(Context.Drawing.Layers);
            foreach (var name in always)
                if (!string.IsNullOrEmpty(name) && !layers.Contains(name, StringComparer.OrdinalIgnoreCase)) layers.Add(name);
            return layers.OrderBy(l => l, StringComparer.OrdinalIgnoreCase).Cast<object>();
        }

        /// <summary>A layer box: pick one of the drawing's layers or type a name.</summary>
        protected ComboBox LayerRow(string caption, string hint, params string[] always)
        {
            var combo = ComboRow(caption, LayerChoices(always), hint, 280);
            combo.DropDownStyle = ComboBoxStyle.DropDown;
            combo.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            combo.AutoCompleteSource = AutoCompleteSource.ListItems;
            combo.TextChanged += (s, e) => Stage();
            return combo;
        }

        protected void Info(string text)
        {
            _status.ForeColor = SystemColors.ControlText;
            _status.Text = text ?? string.Empty;
        }

        protected void Problem(string text)
        {
            _status.ForeColor = Bad;
            _status.Text = text ?? string.Empty;
        }

        // ================================================================== loading and saving

        private void Fill(Action fill)
        {
            Loading = true;
            try { fill(); }
            finally { Loading = false; }
        }

        private void Reload(bool announce)
        {
            _resolution = Context.RulesResolution == null
                ? null
                : RulesStore.Resolve(Context.RulesResolution.DrawingDirectory, Context.RulesResolution.PluginDirectory);
            _activePath = _resolution != null ? _resolution.ActivePath : Context.RulesPath;
            Document = null;
            string loadProblem = null;
            try
            {
                if (!string.IsNullOrEmpty(_activePath)) Document = RuleDocument.Load(_activePath);
            }
            catch (ConfigException ex) { loadProblem = ex.Message; }
            catch (IOException ex) { loadProblem = ex.Message; }

            var inFile = false;
            Fill(() => inFile = ShowSection(Document));
            ShowSource(inFile);
            SetDirty(false);
            Edited();
            _save.Enabled = Document != null;
            if (loadProblem != null) Problem("The rules file could not be read, so the built-in values are shown and nothing can be saved: " + loadProblem);
            else if (announce) Info("Reloaded from " + _activePath + " (unsaved edits discarded).");
            else Info(string.Empty);
        }

        private void ShowSource(bool inFile)
        {
            var where = string.Empty;
            if (_resolution != null)
            {
                switch (_resolution.Source)
                {
                    case RulesSource.Drawing:
                        where = "Editing the DRAWING OVERRIDE (this job only): " + _resolution.ActivePath;
                        break;
                    case RulesSource.Office:
                        where = "Editing the OFFICE STANDARD: " + _resolution.ActivePath;
                        break;
                    default:
                        where = "Showing the FACTORY rules. Your first save creates the office standard at " + _resolution.OfficePath +
                                "; the shipped file is never edited.";
                        break;
                }
            }
            if (!inFile && Document != null)
                where = (where.Length > 0 ? where + " " : string.Empty) + "The file has no " + SectionKey +
                        " section yet: the built-in values are shown, and a save writes them in.";
            _source.Text = where;
        }

        private void SaveClicked()
        {
            var target = _resolution != null ? _resolution.SaveTarget : null;
            if (Document == null || string.IsNullOrEmpty(target)) { Problem("There is no rules file to save to."); return; }
            if (!_dirty) { Info("No " + WhatChanges + " changes to save."); return; }

            var problems = new List<string>();
            ApplyControls(Document, problems);
            if (problems.Count == 0) problems.AddRange(Document.Validate());
            if (problems.Count > 0) { Problem("Not saved: " + string.Join(Environment.NewLine, problems.ToArray())); return; }

            // The file as it is now (another page may have saved it since this one read it), with
            // this page's section put on it, so neither page's save loses the other's.
            RuleDocument fresh;
            try { fresh = RuleDocument.Load(_activePath); }
            catch (ConfigException ex) { Problem("Not saved: " + ex.Message); return; }
            catch (IOException ex) { Problem("Not saved: " + ex.Message); return; }
            CopySection(Document, fresh);

            problems = fresh.Validate().ToList();
            if (problems.Count > 0) { Problem("Not saved: " + string.Join(Environment.NewLine, problems.ToArray())); return; }
            try
            {
                // Never the factory file: SaveTarget is the office configuration or the drawing override.
                fresh.Save(target);
            }
            catch (ConfigException ex) { Problem(ex.Message); return; }
            catch (IOException ex) { Problem("Could not write: " + ex.Message); return; }
            catch (UnauthorizedAccessException ex) { Problem("Could not write: " + ex.Message); return; }

            Reload(false);
            Info("Saved to " + target + ". Previous version kept as .bak. " + UsedBy + " uses it from its next run.");
        }

        private void SetDirty(bool dirty)
        {
            _dirty = dirty;
            if (_unsaved != null)
                _unsaved.Text = dirty ? "Unsaved " + WhatChanges + " changes -- " + SaveText + " writes them" : string.Empty;
        }
    }
}
