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
    /// Settings > Survey Drafting > Wetland Buffers: FTFWETLANDBUFFER's settings, the rules file's
    /// wetlandBuffer section. The buffer layer, the distance and smoothing radius the command
    /// offers first, whether its "cut back at hard surfaces" prompt defaults to Yes, and which
    /// layers are hard surfaces, with which of the open drawing's layers that list matches.
    ///
    /// Like Symbols and Rotations, edits are staged on a RuleDocument (comments and the rest of
    /// the file kept) and written by this page's own Save button, after re-reading the file, to
    /// the office standard or the drawing override -- never the factory file. The window's Save
    /// writes ftf-settings.json only.
    ///
    /// UNTESTED as UI; the document editing and checks are unit tested
    /// (WetlandBufferSettingsEditingTests).
    /// </summary>
    [SettingsPage(Group = "Survey Drafting", Label = "Wetland Buffers", Order = 35, Commands = "FTFWETLANDBUFFER,FTFWLB")]
    internal sealed class WetlandBufferPage : SetupPage
    {
        private static readonly Color Bad = Color.FromArgb(170, 0, 0);
        private static readonly Color Pending = Color.FromArgb(160, 90, 0);

        /// <summary>Words in a layer name that suggest a hard surface, to list those layers first
        /// when adding from the drawing.</summary>
        private static readonly string[] HardWords = { "ASPH", "CONC", "CURB", "GRVL", "GRAV", "SHLD", "PAVE", "BRCK", "BLDG", "WALK", "DRIV", "ROAD" };

        private RulesResolution _resolution;
        private string _activePath;
        private RuleDocument _document;
        private bool _loading;
        private bool _dirty;

        private Label _source;
        private ComboBox _layer;
        private TextBox _distance;
        private TextBox _smoothing;
        private CheckBox _trim;
        private TextBox _hardLayers;
        private Label _matches;
        private Label _status;
        private Label _unsaved;
        private Button _save;

        public override string Title { get { return "Wetland Buffers"; } }

        public override string AffectedCommands { get { return "FTFWETLANDBUFFER (FTFWLB)"; } }

        protected override void BuildBody()
        {
            _source = LiveNote();

            Heading("Buffer");
            _layer = ComboRow("Buffer layer", LayerChoices(),
                "The layer FTFWETLANDBUFFER draws the buffer line on. PMX: V-WATR-WTLD-BUFR-E. Type a name or pick one of this drawing's layers.", 280);
            _layer.DropDownStyle = ComboBoxStyle.DropDown;
            _distance = TextRow("Distance offered first",
                "The buffer distance the command offers, in drawing units (feet in a survey drawing). The drafter can type another each time; the command remembers it for the session.", 80);
            _smoothing = TextRow("Smoothing radius offered first",
                "Rounds the inside corners with this radius; 0 leaves them sharp. Either way the buffer is never closer to the wetland than the distance.", 80);
            Note("Outside corners are always round, with the buffer distance as their radius, so every point of the buffer is exactly that far from the wetland.");

            Heading("Hard surfaces");
            _trim = CheckRow("Cut the buffer back at hard surfaces (the command's default answer; the drafter can say No)",
                "Where a line on one of the layers below lies between the buffer and the wetland, the buffer stops at it.");
            Note("A hard-surface line that crosses the buffer cuts it there. A piece is not drawn when a hard surface lies between it and the wetland, " +
                 "so the buffer stops at the edge of the road instead of running across the road. A piece only partly behind one (a building inside " +
                 "the buffer) is kept and listed as CHECK. The road and wetland lines are never changed.");
            AddFullWidth(new Label { Text = "Hard-surface layers, one per line (* matches anything, as in V-SURF-ASPH*):", AutoSize = true, Margin = new Padding(0, 6, 0, 2) });
            _hardLayers = new TextBox
            {
                Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical, WordWrap = false,
                Width = 320, Height = 190, Margin = new Padding(0, 0, 0, 3)
            };
            Tip(_hardLayers, "Lines, polylines, arcs, circles, splines, 3D polylines, survey figures and feature lines on these layers are read as hard surfaces.");
            var add = new Button { Text = "Add Drawing Layers..." };
            var pmx = new Button { Text = "Use the PMX List" };
            Tip(add, "Pick layers of the open drawing to add to the list.");
            Tip(pmx, "Replaces the list with PMX's hard surfaces: asphalt (with overlays and patches), concrete, curbs, gravel, shoulders, brick and buildings.");
            add.Click += (s, e) => AddDrawingLayers();
            pmx.Click += (s, e) =>
            {
                _hardLayers.Lines = WetlandBufferRule.DefaultHardSurfaceLayers().ToArray();
            };
            AddWithSideButtons(_hardLayers, SideButtons(add, pmx));
            _matches = LiveNote();

            _status = LiveNote();
            _save = new Button { Text = "Save Wetland Buffer Settings", AutoSize = true, Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold) };
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

            _layer.TextChanged += (s, e) => Stage();
            _distance.TextChanged += (s, e) => Stage();
            _smoothing.TextChanged += (s, e) => Stage();
            _trim.CheckedChanged += (s, e) => Stage();
            _hardLayers.TextChanged += (s, e) => Stage();

            Reload(false);
        }

        // The rules file is read and written by this page itself (Reload, Save Wetland Buffer
        // Settings), not through ftf-settings.json.
        public override void LoadFrom(FtfSettings settings) { }

        public override void SaveTo(FtfSettings settings, ICollection<string> problems) { }

        /// <summary>Shows the built-in values; Save Wetland Buffer Settings writes them.</summary>
        public override void RestoreDefaults(FtfSettings settings)
        {
            Show(EditableWetlandBuffer.Defaults());
            Stage();
            if (_dirty) Info("The built-in values are shown. Save Wetland Buffer Settings writes them to the rules file.");
        }

        // ================================================================== loading

        /// <summary>Discards staged edits and reads the active rules file again.</summary>
        private void Reload(bool announce)
        {
            _resolution = Context.RulesResolution == null
                ? null
                : RulesStore.Resolve(Context.RulesResolution.DrawingDirectory, Context.RulesResolution.PluginDirectory);
            _activePath = _resolution != null ? _resolution.ActivePath : Context.RulesPath;
            _document = null;
            string loadProblem = null;
            try
            {
                if (!string.IsNullOrEmpty(_activePath)) _document = RuleDocument.Load(_activePath);
            }
            catch (ConfigException ex) { loadProblem = ex.Message; }
            catch (IOException ex) { loadProblem = ex.Message; }

            var values = _document != null ? _document.GetWetlandBuffer() : EditableWetlandBuffer.Defaults();
            Show(values);
            ShowSource(values);
            ShowMatches(values);
            SetDirty(false);
            _save.Enabled = _document != null;
            if (loadProblem != null) Problem("The rules file could not be read, so the built-in values are shown and nothing can be saved: " + loadProblem);
            else if (announce) Info("Reloaded from " + _activePath + " (unsaved edits discarded).");
            else Info(string.Empty);
        }

        private void Show(EditableWetlandBuffer values)
        {
            _loading = true;
            try
            {
                _layer.Text = values.Layer ?? string.Empty;
                _distance.Text = Fmt(values.Distance);
                _smoothing.Text = Fmt(values.Smoothing);
                _trim.Checked = values.TrimAtHardSurfaces;
                _hardLayers.Lines = (values.HardSurfaceLayers ?? new List<string>()).ToArray();
            }
            finally { _loading = false; }
        }

        private void ShowSource(EditableWetlandBuffer values)
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
            if (!values.InFile)
                where = (where.Length > 0 ? where + " " : string.Empty) + "The file has no wetlandBuffer section yet: the built-in values are shown, and a save writes them in.";
            _source.Text = where;
        }

        private IEnumerable<object> LayerChoices()
        {
            var layers = new List<string>();
            if (Context != null && Context.Drawing != null && Context.Drawing.Layers != null) layers.AddRange(Context.Drawing.Layers);
            if (!layers.Contains(PmxLayers.WetlandBufferLayer, StringComparer.OrdinalIgnoreCase)) layers.Add(PmxLayers.WetlandBufferLayer);
            return layers.OrderBy(l => l, StringComparer.OrdinalIgnoreCase).Cast<object>();
        }

        // ================================================================== editing

        /// <summary>Puts the controls on the staged document and shows what is wrong, if anything.</summary>
        private void Stage()
        {
            if (_loading) return;
            var problems = new List<string>();
            var edit = Read(problems);
            ShowMatches(edit);
            if (problems.Count == 0 && _document != null)
            {
                _document.ApplyWetlandBuffer(edit);
                problems.AddRange(_document.Validate());
            }
            SetDirty(true);
            if (problems.Count > 0) Problem(string.Join(Environment.NewLine, problems.ToArray()));
            else Info(string.Empty);
        }

        private EditableWetlandBuffer Read(ICollection<string> problems)
        {
            var current = _document != null ? _document.GetWetlandBuffer() : EditableWetlandBuffer.Defaults();
            var distance = current.Distance;
            var smoothing = current.Smoothing;
            ReadDouble(_distance, "Wetland Buffers: the distance", v => v > 0, "must be greater than zero", problems, ref distance);
            ReadDouble(_smoothing, "Wetland Buffers: the smoothing radius", v => v >= 0, "cannot be negative (0 = none)", problems, ref smoothing);
            return new EditableWetlandBuffer
            {
                Layer = (_layer.Text ?? string.Empty).Trim(),
                Distance = distance,
                Smoothing = smoothing,
                TrimAtHardSurfaces = _trim.Checked,
                HardSurfaceLayers = EditableWetlandBuffer.CleanLayers(_hardLayers.Lines)
            };
        }

        /// <summary>Which of the open drawing's layers the list makes hard surfaces, and which
        /// listed layers the drawing does not have.</summary>
        private void ShowMatches(EditableWetlandBuffer values)
        {
            var patterns = EditableWetlandBuffer.CleanLayers(values.HardSurfaceLayers);
            if (patterns.Count == 0)
            {
                _matches.ForeColor = Pending;
                _matches.Text = values.TrimAtHardSurfaces
                    ? "No hard-surface layers are listed, so nothing will be cut back."
                    : "No hard-surface layers are listed.";
                return;
            }
            _matches.ForeColor = SystemColors.GrayText;
            var layers = Context != null && Context.Drawing != null && Context.Drawing.Layers != null ? Context.Drawing.Layers : new List<string>();
            if (layers.Count == 0)
            {
                _matches.Text = "Open a drawing to see which of its layers these match.";
                return;
            }
            var rule = new WetlandBufferRule { HardSurfaceLayers = patterns };
            var hard = layers.Where(rule.IsHardSurface).OrderBy(l => l, StringComparer.OrdinalIgnoreCase).ToList();
            var unused = patterns.Where(p => !layers.Any(l => new WetlandBufferRule { HardSurfaceLayers = new List<string> { p } }.IsHardSurface(l))).ToList();
            var text = hard.Count == 0
                ? "None of this drawing's layers is a hard surface by this list."
                : string.Format(CultureInfo.InvariantCulture, "In this drawing, {0} layer(s) are hard surfaces: {1}{2}.", hard.Count,
                                string.Join(", ", hard.Take(12).ToArray()), hard.Count > 12 ? ", ..." : string.Empty);
            if (unused.Count > 0)
                text += string.Format(CultureInfo.InvariantCulture, " Not in this drawing: {0}{1}.",
                                      string.Join(", ", unused.Take(12).ToArray()), unused.Count > 12 ? ", ..." : string.Empty);
            var buffer = (values.Layer ?? string.Empty).Trim();
            if (buffer.Length > 0 && rule.IsHardSurface(buffer))
            {
                _matches.ForeColor = Pending;
                text += " The buffer layer is in the list itself; FTF's own buffers are never read as hard surfaces, but other lines on it would be.";
            }
            _matches.Text = text;
        }

        private void AddDrawingLayers()
        {
            var layers = Context != null && Context.Drawing != null && Context.Drawing.Layers != null ? Context.Drawing.Layers : new List<string>();
            if (layers.Count == 0) { Info("Open a drawing to add its layers."); return; }
            var listed = new WetlandBufferRule { HardSurfaceLayers = EditableWetlandBuffer.CleanLayers(_hardLayers.Lines) };
            // Layers whose names look like hard surfaces first, then the rest; those already
            // covered by the list are left out.
            var offered = layers.Where(l => !listed.IsHardSurface(l))
                                .OrderBy(l => HardWords.Any(w => l.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0) ? 0 : 1)
                                .ThenBy(l => l, StringComparer.OrdinalIgnoreCase).ToList();
            if (offered.Count == 0) { Info("Every layer of this drawing is already a hard surface by the list."); return; }

            using (var form = new Form
            {
                Text = "Add hard-surface layers", Width = 460, Height = 480, StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.Sizable, MinimizeBox = false, MaximizeBox = false
            })
            {
                var caption = new Label
                {
                    Text = "Tick the layers whose lines are hard surfaces. Layers whose names suggest one are listed first.",
                    Dock = DockStyle.Top, Height = 36
                };
                var list = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false };
                foreach (var l in offered) list.Items.Add(l);
                var ok = new Button { Text = "Add", DialogResult = DialogResult.OK, Dock = DockStyle.Bottom };
                form.Controls.Add(list);
                form.Controls.Add(ok);
                form.Controls.Add(caption);
                form.AcceptButton = ok;
                if (form.ShowDialog(_hardLayers) != DialogResult.OK || list.CheckedItems.Count == 0) return;
                var lines = _hardLayers.Lines.ToList();
                lines.AddRange(list.CheckedItems.Cast<object>().Select(o => o.ToString()));
                _hardLayers.Lines = EditableWetlandBuffer.CleanLayers(lines).ToArray();
            }
        }

        // ================================================================== save

        private void SaveClicked()
        {
            var target = _resolution != null ? _resolution.SaveTarget : null;
            if (_document == null || string.IsNullOrEmpty(target)) { Problem("There is no rules file to save to."); return; }
            if (!_dirty) { Info("No wetland buffer changes to save."); return; }

            var problems = new List<string>();
            var edit = Read(problems);
            if (problems.Count > 0) { Problem("Not saved: " + string.Join(Environment.NewLine, problems.ToArray())); return; }
            _document.ApplyWetlandBuffer(edit);
            problems.AddRange(_document.Validate());
            if (problems.Count > 0) { Problem("Not saved: " + string.Join(Environment.NewLine, problems.ToArray())); return; }

            // The file as it is now (another page may have saved it since this one read it), with
            // this page's settings put on it, so neither page's save loses the other's.
            RuleDocument fresh;
            try { fresh = RuleDocument.Load(_activePath); }
            catch (ConfigException ex) { Problem("Not saved: " + ex.Message); return; }
            catch (IOException ex) { Problem("Not saved: " + ex.Message); return; }
            _document.CopyWetlandBufferTo(fresh);

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
            Info("Saved to " + target + ". Previous version kept as .bak. FTFWETLANDBUFFER uses it from its next run.");
        }

        // ================================================================== plumbing

        private void SetDirty(bool dirty)
        {
            _dirty = dirty;
            if (_unsaved != null) _unsaved.Text = dirty ? "Unsaved wetland buffer changes -- Save Wetland Buffer Settings writes them" : string.Empty;
        }

        private void Problem(string text)
        {
            _status.ForeColor = Bad;
            _status.Text = text ?? string.Empty;
        }

        private void Info(string text)
        {
            _status.ForeColor = SystemColors.ControlText;
            _status.Text = text ?? string.Empty;
        }
    }
}
