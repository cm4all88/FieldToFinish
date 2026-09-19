using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using FieldCodes.Settings;
using FieldCodes.Utilities;

namespace FieldCodes.Cad.Ui
{
    /// <summary>A control whose height follows from the width it is given.</summary>
    internal interface IFitsWidth
    {
        void FitWidth(int width);
    }

    /// <summary>
    /// The Add pipe panel: record a pipe the way it reads in the field book, in a few clicks.
    /// Direction (16 buttons) -> size (the usual ones for this structure, Larger for the rest or a typed size) ->
    /// material (the usual ones, More... for the rest or a typed one) -> measure down and what it was measured to.
    /// Every choice is a shortcut, never a rule: whatever was observed can be entered as it was observed.
    /// </summary>
    internal sealed class PipeQuickEntry : Panel, IFitsWidth
    {
        public const string LargerText = "Larger";
        public const string CommonSizesText = "Usual sizes";
        public const string MoreText = "More...";
        public const string UsualMaterialsText = "Usual materials";
        public const string UnknownDirection = "?";

        // Pipe references the panel offers first; the rest of the model's references sit under More.
        private static readonly MeasurementReference[] MainReferences =
        {
            MeasurementReference.Unspecified, MeasurementReference.Invert,
            MeasurementReference.TopOfPipe, MeasurementReference.Springline
        };

        private readonly Label _title;
        private readonly FlowLayoutPanel _sections;
        private readonly CompassPicker _compass;
        private readonly TextBox _directionTyped;
        private readonly Label _directionNote;
        private readonly FlowLayoutPanel _sizes;
        private readonly TextBox _sizeTyped;
        private readonly Button _sizeUse;
        private readonly FlowLayoutPanel _materials;
        private readonly TextBox _materialTyped;
        private readonly Button _materialUse;
        private readonly TextBox _dip;
        private readonly FlowLayoutPanel _references;
        private readonly Label _preview;
        private readonly Label _problem;
        private readonly Button _add;
        private readonly Button _addNext;
        private readonly FlowLayoutPanel _bottom;
        private readonly Panel _sizeSection, _materialSection;

        private PipeChoiceSet _choices = new PipeChoiceSet();
        private bool _largerShown, _moreShown, _moreReferencesShown;
        private readonly QuickPipeEntry _entry = new QuickPipeEntry();
        private List<string> _prefilledStart = new List<string>();
        private string _prefilledFrom;
        private readonly HashSet<string> _touched = new HashSet<string>();
        private readonly Label _prefillNote;
        private readonly FlowLayoutPanel _top;
        private Func<PipeObservation, string> _summary = p => QuickPipeEntry.Summary(p, null);

        /// <summary>The pipe being edited, or null when a new pipe is being added.</summary>
        public string EditingPipeId { get; private set; }

        /// <summary>Raised with the entry and whether the panel should stay open for the next pipe.</summary>
        public event Action<QuickPipeEntry, bool> Submitted;
        public event EventHandler Cancelled;

        public PipeQuickEntry()
        {
            BackColor = DipBuilderForm.Calculated;
            Padding = new Padding(12, 8, 12, 8);
            Margin = Padding.Empty;

            _title = new Label { AutoSize = true, Font = DipBuilderForm.F(11f, true), ForeColor = DipBuilderForm.Ink, Margin = new Padding(0, 0, 0, 6) };

            // Direction: a compass, north up. Click the way the field notes say; ? in the middle when not recorded.
            _compass = new CompassPicker { Margin = new Padding(0, 0, 0, 4) };
            _compass.DirectionPicked += PickDirection;
            _directionTyped = SmallBox(110);
            _directionTyped.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; UseTypedDirection(); } };
            _directionTyped.Leave += (s, e) => { if (_directionTyped.Text.Trim().Length > 0) UseTypedDirection(); };
            _directionNote = new Label { AutoSize = true, ForeColor = DipBuilderForm.Muted, Font = DipBuilderForm.F(9f, false), Margin = new Padding(0, 2, 0, 0), MaximumSize = new Size(260, 0) };
            var directionSection = Section("Direction", 310,
                _compass,
                Line(Hint("or type"), _directionTyped, Hint("N45E, AZ215")),
                _directionNote);

            _sizes = new FlowLayoutPanel { AutoSize = true, WrapContents = true, MaximumSize = new Size(250, 0), Margin = Padding.Empty, BackColor = Color.Transparent };
            _sizeTyped = SmallBox(70);
            _sizeTyped.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; UseTypedSize(); } };
            _sizeUse = Choice("Use size", null, (s, e) => UseTypedSize(), 0);
            _sizeSection = Section("Size (in)", 260, _sizes, Line(Hint("Any size"), _sizeTyped, _sizeUse));

            _materials = new FlowLayoutPanel { AutoSize = true, WrapContents = true, MaximumSize = new Size(260, 0), Margin = Padding.Empty, BackColor = Color.Transparent };
            _materialTyped = SmallBox(120);
            _materialTyped.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; UseTypedMaterial(); } };
            _materialUse = Choice("Use material", null, (s, e) => UseTypedMaterial(), 0);
            _materialSection = Section("Material", 270, _materials, Line(Hint("Other"), _materialTyped, _materialUse));

            _dip = SmallBox(80);
            _dip.TextChanged += (s, e) => { ReadDip(false); UpdatePreview(); };
            _dip.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Submit(false); } };
            _references = new FlowLayoutPanel { AutoSize = true, WrapContents = true, MaximumSize = new Size(230, 0), Margin = Padding.Empty, BackColor = Color.Transparent };
            var measureSection = Section("Measure down", 240,
                Line(Hint("MD (ft)"), _dip),
                Hint("Measured to"),
                _references);

            _sections = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = false, WrapContents = true, BackColor = Color.Transparent, Margin = Padding.Empty };
            _sections.Controls.AddRange(new Control[] { directionSection, _sizeSection, _materialSection, measureSection });

            _preview = new Label { AutoSize = true, Font = new Font("Consolas", 11.5f, FontStyle.Bold), ForeColor = DipBuilderForm.Ink, Margin = new Padding(0, 6, 16, 0) };
            _problem = new Label { AutoSize = true, ForeColor = DipBuilderForm.Bad, Margin = new Padding(0, 8, 12, 0) };
            _add = Choice("Add pipe", null, (s, e) => Submit(false), 0);
            _addNext = Choice("Add + next", null, (s, e) => Submit(true), 0);
            var cancel = Choice("Cancel", null, (s, e) => { if (Cancelled != null) Cancelled(this, EventArgs.Empty); }, 0);
            MakePrimary(_add);
            _bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = false, WrapContents = true, BackColor = Color.Transparent };
            _bottom.Controls.AddRange(new Control[] { _preview, _add, _addNext, cancel, _problem });

            _prefillNote = new Label
            {
                AutoSize = true, ForeColor = DipBuilderForm.Warn, Font = DipBuilderForm.F(9.75f, false), Margin = new Padding(0, 0, 0, 6),
                MaximumSize = new Size(1000, 0), Visible = false, UseMnemonic = false
            };
            var top = _top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Color.Transparent };
            top.Controls.Add(_title);
            top.Controls.Add(_prefillNote);

            Controls.Add(_sections);
            Controls.Add(_bottom);
            Controls.Add(top);
            Paint += (s, e) => e.Graphics.DrawRectangle(new Pen(DipBuilderForm.Accent), 0, 0, Width - 1, Height - 1);
        }

        // ------------------------------------------------------------- opening

        /// <summary>The connection whose far end is being completed, or null.</summary>
        public string CompletingConnectionId { get; private set; }

        /// <summary>Opens the panel for a new pipe (editing null) or for an existing observation.</summary>
        public void Begin(PipeChoiceSet choices, PipeObservation editing, string structureLabel, Func<PipeObservation, string> summary)
        {
            Start(choices, editing != null ? QuickPipeEntry.From(editing) : new QuickPipeEntry(), editing, null, structureLabel, null, summary);
        }

        /// <summary>
        /// Opens the panel to enter this structure's end of a connected pipe, starting from what the other end says:
        /// the opposite direction, size and material, shown lighter as copied. The measure down starts empty.
        /// </summary>
        public void BeginComplete(PipeChoiceSet choices, QuickPipeEntry prefill, string connectionId, string structureLabel,
                                  string sourceLabel, Func<PipeObservation, string> summary)
        {
            Start(choices, prefill, null, connectionId, structureLabel, sourceLabel, summary);
        }

        private void Start(PipeChoiceSet choices, QuickPipeEntry from, PipeObservation editing, string connectionId,
                           string structureLabel, string sourceLabel, Func<PipeObservation, string> summary)
        {
            _summary = summary ?? _summary;
            EditingPipeId = editing != null ? editing.Id : null;
            CompletingConnectionId = connectionId;
            _entry.Direction = from.Direction;
            _entry.SizeIn = from.SizeIn;
            _entry.Material = from.Material;
            // The measure down is never carried over from another structure.
            _entry.MeasuredDip = connectionId != null ? null : from.MeasuredDip;
            _entry.Reference = editing != null ? editing.Reference : MeasurementReference.Unspecified;
            _prefilledStart = (from.Prefilled ?? new List<string>()).ToList();
            _prefilledFrom = from.PrefilledFrom;
            _touched.Clear();

            _title.Text = connectionId != null ? "Complete pipe from " + sourceLabel + " at " + structureLabel
                        : editing != null ? "Edit pipe at " + structureLabel : "Add pipe at " + structureLabel;
            _add.Text = connectionId != null ? "Add matching pipe" : editing != null ? "Save pipe" : "Add pipe";
            _addNext.Visible = editing == null && connectionId == null;
            _prefillNote.Text = _prefilledStart.Count == 0 ? string.Empty
                : "Copied from " + (_prefilledFrom ?? "the connected pipe") + ": " + string.Join(", ", _prefilledStart.ToArray()) +
                  " (shown lighter). Click a value to confirm or change it as observed here" +
                  (connectionId != null ? ", then enter the MD measured here." : ".");
            _prefillNote.Visible = _prefillNote.Text.Length > 0;
            _directionTyped.Text = string.Empty;
            _sizeTyped.Text = string.Empty;
            _materialTyped.Text = string.Empty;
            _dip.Text = _entry.MeasuredDip.HasValue ? QuickPipeEntry.Exact(_entry.MeasuredDip.Value) : string.Empty;
            _problem.Text = string.Empty;
            _moreReferencesShown = !MainReferences.Contains(_entry.Reference);
            SetChoices(choices);
            // A value no button shows (a field-measured 17.5", a material off every list) sits in its typed box.
            if (_entry.SizeIn.HasValue && !_choices.CommonSizes.Concat(_choices.LargerSizes).Any(v => Same(v, _entry.SizeIn.Value)))
                _sizeTyped.Text = QuickPipeEntry.Exact(_entry.SizeIn.Value);
            if (!string.IsNullOrEmpty(_entry.Material) && !_choices.CommonMaterials.Concat(_choices.MoreMaterials).Contains(_entry.Material))
                _materialTyped.Text = _entry.Material;
        }

        /// <summary>New buttons for a changed structure type or system; what was already chosen is kept.</summary>
        public void SetChoices(PipeChoiceSet choices)
        {
            _choices = choices ?? new PipeChoiceSet();
            // A value the structure's usual list lacks opens the longer list, so it is seen as chosen.
            _largerShown = _entry.SizeIn.HasValue && !_choices.CommonSizes.Any(v => Same(v, _entry.SizeIn.Value));
            _moreShown = !string.IsNullOrEmpty(_entry.Material) && !_choices.CommonMaterials.Contains(_entry.Material);
            BuildSizes();
            BuildMaterials();
            BuildReferences();
            ShowDirection();
            UpdatePreview();
            FitWidth(Width);
        }

        public string ChoiceRule { get { return _choices.RuleName; } }

        // ----------------------------------------------------------- direction

        /// <summary>Copied values the drafter has not clicked, typed or changed yet.</summary>
        private bool StillPrefilled(string field) { return _prefilledStart.Contains(field) && !_touched.Contains(field); }

        private void PickDirection(string name)
        {
            _touched.Add(QuickPipeEntry.DirectionField);
            _entry.Direction = name == UnknownDirection ? ObservedDirection.Unknown("?") : DirectionShortcuts.For(name);
            _directionTyped.Text = string.Empty;
            ShowDirection();
            UpdatePreview();
        }

        private void UseTypedDirection()
        {
            var text = _directionTyped.Text.Trim();
            if (text.Length == 0) return;
            var d = DirectionShortcuts.Parse(text);
            if (d == null) { _problem.Text = "\"" + text + "\" is not a direction (N/NE, N45E, AZ215, ?)."; return; }
            _problem.Text = string.Empty;
            _touched.Add(QuickPipeEntry.DirectionField);
            _entry.Direction = d;
            ShowDirection();
            UpdatePreview();
        }

        private void ShowDirection()
        {
            var button = DirectionShortcuts.ButtonFor(_entry.Direction);
            var unknown = _entry.Direction != null && !_entry.Direction.IsKnown && _entry.Direction.Text == "?";
            _compass.Selected = unknown ? CompassPicker.Unknown : button;
            _compass.SelectedIsPrefilled = StillPrefilled(QuickPipeEntry.DirectionField);
            // A bearing or azimuth from the notes has no button; it is kept exactly as observed.
            _directionNote.Text = _entry.Direction != null && _entry.Direction.IsKnown && button == null
                ? "Kept as observed: " + _entry.Direction.Text
                : string.Empty;
        }

        // ---------------------------------------------------------------- size

        private void BuildSizes()
        {
            _sizes.SuspendLayout();
            _sizes.Controls.Clear();
            var list = _largerShown ? _choices.LargerSizes : _choices.CommonSizes;
            foreach (var v in list)
            {
                var size = v;
                var b = Choice(SizeText(size), "size", (s, e) => { _touched.Add(QuickPipeEntry.SizeField); _entry.SizeIn = size; _sizeTyped.Text = string.Empty; MarkSize(); UpdatePreview(); }, 46);
                _sizes.Controls.Add(b);
            }
            _sizes.Controls.Add(Choice(_largerShown ? CommonSizesText : LargerText, null, (s, e) =>
            {
                _largerShown = !_largerShown;
                BuildSizes();
                FitWidth(Width);
            }, 0));
            _sizes.ResumeLayout();
            // Any size can be typed from the longer list: a field-measured 17.5" is kept as 17.5".
            _sizeTyped.Parent.Visible = _largerShown;
            MarkSize();
        }

        private void UseTypedSize()
        {
            var text = _sizeTyped.Text.Trim();
            if (text.Length == 0) return;
            double v;
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out v) || double.IsNaN(v) || double.IsInfinity(v) || v <= 0)
            {
                _problem.Text = "Size \"" + text + "\" is not a positive number of inches.";
                return;
            }
            _problem.Text = string.Empty;
            _touched.Add(QuickPipeEntry.SizeField);
            _entry.SizeIn = v;
            MarkSize();
            UpdatePreview();
        }

        private void MarkSize()
        {
            foreach (Control c in _sizes.Controls)
            {
                var b = c as Button;
                if (b == null || b.Tag as string != "size") continue;
                Mark(b, _entry.SizeIn.HasValue && b.Text == SizeText(_entry.SizeIn.Value), StillPrefilled(QuickPipeEntry.SizeField));
            }
        }

        public static string SizeText(double inches) { return QuickPipeEntry.Exact(inches) + "\""; }

        // ------------------------------------------------------------ material

        private void BuildMaterials()
        {
            _materials.SuspendLayout();
            _materials.Controls.Clear();
            foreach (var m in _moreShown ? _choices.MoreMaterials : _choices.CommonMaterials)
            {
                var material = m;
                _materials.Controls.Add(Choice(material, "material", (s, e) =>
                {
                    _touched.Add(QuickPipeEntry.MaterialField);
                    _entry.Material = material;
                    _materialTyped.Text = string.Empty;
                    MarkMaterial();
                    UpdatePreview();
                    _dip.Focus();
                }, 52));
            }
            _materials.Controls.Add(Choice(_moreShown ? UsualMaterialsText : MoreText, null, (s, e) =>
            {
                _moreShown = !_moreShown;
                BuildMaterials();
                FitWidth(Width);
            }, 0));
            _materials.ResumeLayout();
            _materialTyped.Parent.Visible = _moreShown;
            MarkMaterial();
        }

        private void UseTypedMaterial()
        {
            var text = _materialTyped.Text.Trim();
            if (text.Length == 0) return;
            _problem.Text = string.Empty;
            _touched.Add(QuickPipeEntry.MaterialField);
            _entry.Material = text.ToUpperInvariant();
            MarkMaterial();
            UpdatePreview();
        }

        private void MarkMaterial()
        {
            foreach (Control c in _materials.Controls)
            {
                var b = c as Button;
                if (b == null || b.Tag as string != "material") continue;
                Mark(b, string.Equals(b.Text, _entry.Material, StringComparison.OrdinalIgnoreCase), StillPrefilled(QuickPipeEntry.MaterialField));
            }
        }

        // ------------------------------------------------------- measure down

        private void BuildReferences()
        {
            _references.SuspendLayout();
            _references.Controls.Clear();
            var shown = _moreReferencesShown
                ? (IEnumerable<MeasurementReference>)Enum.GetValues(typeof(MeasurementReference)).Cast<MeasurementReference>()
                : MainReferences;
            foreach (var r in shown)
            {
                var reference = r;
                var b = Choice(ReferenceWords(reference), "reference", (s, e) => { _entry.Reference = reference; MarkReferences(); UpdatePreview(); }, 0);
                b.Name = reference.ToString();
                _references.Controls.Add(b);
            }
            if (!_moreReferencesShown)
                _references.Controls.Add(Choice(MoreText, null, (s, e) => { _moreReferencesShown = true; BuildReferences(); FitWidth(Width); }, 0));
            _references.ResumeLayout();
            MarkReferences();
        }

        private void MarkReferences()
        {
            foreach (Control c in _references.Controls)
            {
                var b = c as Button;
                if (b == null || b.Tag as string != "reference") continue;
                Mark(b, b.Name == _entry.Reference.ToString());
            }
        }

        internal static string ReferenceWords(MeasurementReference reference)
        {
            switch (reference)
            {
                case MeasurementReference.Unspecified: return "Not stated";
                case MeasurementReference.TopOfPipe: return "Top of pipe";
                case MeasurementReference.BottomOfStructure: return "Bottom of structure";
                case MeasurementReference.WaterLevel: return "Water level";
                case MeasurementReference.TopOfGrate: return "Top of grate";
                case MeasurementReference.TopOfCasting: return "Top of casting";
                default: return reference.ToString();
            }
        }

        private bool ReadDip(bool report)
        {
            var text = _dip.Text.Trim();
            if (text.Length == 0) { _entry.MeasuredDip = null; return true; }
            double v;
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out v) && v >= 0 && !double.IsNaN(v) && !double.IsInfinity(v))
            {
                _entry.MeasuredDip = v;
                return true;
            }
            if (report) _problem.Text = "MD \"" + text + "\" is not a measure down (0 or more feet below the rim).";
            return false;
        }

        // -------------------------------------------------------------- submit

        private void Submit(bool next)
        {
            if (!ReadDip(true)) return;
            // Typed values not yet applied with their buttons are still what the drafter means.
            if (_directionTyped.Text.Trim().Length > 0) UseTypedDirection();
            // A typed box still holding the value it started with (a copied 17.5") is not a new entry.
            double typedSize;
            if (_largerShown && _sizeTyped.Text.Trim().Length > 0 &&
                !(double.TryParse(_sizeTyped.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out typedSize) &&
                  _entry.SizeIn.HasValue && Same(typedSize, _entry.SizeIn.Value)))
                UseTypedSize();
            if (_moreShown && _materialTyped.Text.Trim().Length > 0 &&
                !string.Equals(_materialTyped.Text.Trim(), _entry.Material, StringComparison.OrdinalIgnoreCase))
                UseTypedMaterial();
            if (_problem.Text.Length > 0) return;

            var entry = new QuickPipeEntry
            {
                Direction = _entry.Direction,
                SizeIn = _entry.SizeIn,
                Material = _entry.Material,
                MeasuredDip = _entry.MeasuredDip,
                Reference = _entry.Reference,
                // Copied values stay marked as copied until the drafter clicks, types or changes them here.
                Prefilled = _prefilledStart.Where(f => !_touched.Contains(f)).ToList(),
                PrefilledFrom = _prefilledFrom
            };
            if (Submitted != null) Submitted(entry, next);
        }

        /// <summary>What the panel would record right now.</summary>
        public QuickPipeEntry Current { get { return _entry; } }

        private void UpdatePreview()
        {
            var pipe = new PipeObservation();
            _entry.ApplyTo(pipe);
            pipe.Reference = _entry.Reference;
            _preview.Text = _summary(pipe);
            _preview.ForeColor = _entry.MeasuredDip.HasValue && _entry.Reference == MeasurementReference.Unspecified ? DipBuilderForm.Warn : DipBuilderForm.Ink;
        }

        // -------------------------------------------------------------- layout

        public void FitWidth(int width)
        {
            if (width <= 0) return;
            var inner = Math.Max(200, width - Padding.Horizontal);
            _sections.Width = inner;
            _sections.Height = _sections.GetPreferredSize(new Size(inner, 0)).Height;
            _bottom.Height = _bottom.GetPreferredSize(new Size(inner, 0)).Height;
            _prefillNote.MaximumSize = new Size(inner, 0);
            _top.Height = _top.GetPreferredSize(new Size(inner, 0)).Height;
            var height = Padding.Vertical + _top.Height + 2 + _sections.Height + _bottom.Height + 4;
            if (Height != height) Height = height;
        }

        private static Panel Section(string caption, int width, params Control[] rows)
        {
            var flow = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Width = width,
                MinimumSize = new Size(width, 0), MaximumSize = new Size(width, 0), BackColor = Color.Transparent,
                Margin = new Padding(0, 0, 14, 6)
            };
            flow.Controls.Add(new Label { Text = caption, AutoSize = true, Font = DipBuilderForm.F(9.5f, true), ForeColor = DipBuilderForm.Muted, Margin = new Padding(0, 0, 0, 3) });
            flow.Controls.AddRange(rows);
            return flow;
        }

        private static FlowLayoutPanel Line(params Control[] controls)
        {
            var f = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 0), BackColor = Color.Transparent };
            f.Controls.AddRange(controls);
            return f;
        }

        private static Label Hint(string text)
        {
            return new Label { Text = text, AutoSize = true, ForeColor = DipBuilderForm.Muted, Font = DipBuilderForm.F(9f, false), Margin = new Padding(0, 6, 4, 0) };
        }

        private static TextBox SmallBox(int width)
        {
            return new TextBox
            {
                Width = width, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 2, 4, 2), Font = DipBuilderForm.F(10.5f, true),
                BackColor = DipBuilderForm.Dark ? DipBuilderForm.Surface : Color.White, ForeColor = DipBuilderForm.Ink
            };
        }

        /// <summary>A flat button; tag says which group of choices it belongs to (it shows as picked when chosen).</summary>
        private static Button Choice(string text, string group, EventHandler click, int minWidth)
        {
            var b = new Button
            {
                Text = text, Tag = group, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlatStyle = FlatStyle.Flat,
                BackColor = DipBuilderForm.Surface, ForeColor = DipBuilderForm.Ink, Font = DipBuilderForm.F(9.5f, true),
                Margin = new Padding(0, 0, 4, 4), Padding = new Padding(2, 0, 2, 0), Cursor = Cursors.Hand,
                MinimumSize = new Size(minWidth, 0), UseMnemonic = false
            };
            b.FlatAppearance.BorderColor = DipBuilderForm.ButtonBorder;
            b.FlatAppearance.MouseOverBackColor = DipBuilderForm.AccentSoft;
            b.Click += click;
            return b;
        }

        private static void MakePrimary(Button b)
        {
            b.BackColor = DipBuilderForm.Accent;
            b.ForeColor = Color.White;
            b.FlatAppearance.BorderColor = DipBuilderForm.Accent;
            b.FlatAppearance.MouseOverBackColor = DipBuilderForm.AccentHover;
        }

        /// <summary>Shows a choice as picked (filled) or not; a copied value that is not confirmed yet is lighter.</summary>
        private static void Mark(Button b, bool picked, bool prefilled = false)
        {
            b.BackColor = picked ? (prefilled ? DipBuilderForm.AccentSoft : DipBuilderForm.Accent) : DipBuilderForm.Surface;
            b.ForeColor = picked && !prefilled ? Color.White : DipBuilderForm.Ink;
            b.FlatAppearance.BorderColor = picked ? DipBuilderForm.Accent : DipBuilderForm.ButtonBorder;
            b.FlatAppearance.BorderSize = picked && prefilled ? 2 : 1;
        }

        /// <summary>True when the button shows as picked (for the tests and for screen readers' sake).</summary>
        internal static bool IsPicked(Button b) { return b.BackColor == DipBuilderForm.Accent; }

        /// <summary>True when the button shows a copied value that has not been confirmed here.</summary>
        internal static bool IsPrefilledPick(Button b) { return b.BackColor == DipBuilderForm.AccentSoft && b.FlatAppearance.BorderSize == 2; }

        private static bool Same(double a, double b) { return Math.Abs(a - b) < 1e-9; }
    }
}
