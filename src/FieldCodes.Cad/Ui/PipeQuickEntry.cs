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
    /// <summary>A control that lays itself out to a width and reports the height it needs.</summary>
    internal interface IFitsWidth
    {
        void FitWidth(int width);
    }

    /// <summary>
    /// Capturing one pipe, the way the field book is read out: size, then type, then direction,
    /// then the measure down.
    ///
    /// One row of chips says what the pipe is so far, and ONE chooser strip opens under it. The
    /// strip is the same strip at every step -- it holds the sizes, then the materials, then the
    /// sixteen directions -- so the screen does not grow and there is nothing to navigate back
    /// from. Choosing advances to the next step on its own; choosing a direction closes the strip
    /// and puts the caret in the measure down, which is the only thing normally typed. Enter
    /// finishes the pipe. There is no Add button: Enter is the end of typing the number, not
    /// another step.
    ///
    /// Nothing is filled in for the drafter except the measurement reference, which starts at the
    /// office's configured default (invert) and is visible as a chip so it can be seen and
    /// changed. Size, type and direction are unknown until chosen, and the measure down cannot be
    /// typed until they are, so Enter can never save half a pipe.
    /// </summary>
    internal sealed class PipeQuickEntry : Panel, IFitsWidth
    {
        public const string LargerText = "Larger";
        public const string CommonSizesText = "Usual sizes";
        public const string MoreText = "More...";
        public const string UsualMaterialsText = "Usual materials";
        public const string UnknownDirection = "?";

        /// <summary>Which chooser the one strip is currently holding.</summary>
        private enum Step { None, Size, Type, Direction, Reference }

        private static readonly MeasurementReference[] References =
        {
            MeasurementReference.Invert, MeasurementReference.TopOfPipe, MeasurementReference.Springline,
            MeasurementReference.BottomOfStructure, MeasurementReference.WaterLevel, MeasurementReference.Unspecified
        };

        private readonly Label _title;
        private readonly FlowLayoutPanel _chips;
        private readonly Button _sizeChip, _typeChip, _dirChip, _refChip;
        private readonly TextBox _dip;
        private readonly Label _enterHint;
        private readonly Panel _strip;
        private readonly Label _preview;
        private readonly Label _problem;
        private readonly Label _prefillNote;
        private readonly FlowLayoutPanel _top;

        private readonly QuickPipeEntry _entry = new QuickPipeEntry();
        private PipeChoiceSet _choices = new PipeChoiceSet();
        private Func<PipeObservation, string> _summary = p => string.Empty;
        private Step _step = Step.None;
        private bool _largerShown, _moreShown, _dialShown;
        private List<string> _prefilledStart = new List<string>();
        private string _prefilledFrom;
        private readonly HashSet<string> _touched = new HashSet<string>();

        /// <summary>The pipe being edited, or null when a new one is being captured.</summary>
        public string EditingPipeId { get; private set; }

        /// <summary>The connection whose far end is being entered, or null.</summary>
        public string CompletingConnectionId { get; private set; }

        /// <summary>Raised when Enter finishes a pipe. The bool asks for another empty slot.</summary>
        public event Action<QuickPipeEntry, bool> Submitted;
        public event EventHandler Cancelled;

        public PipeQuickEntry()
        {
            BackColor = DipBuilderForm.Surface;
            Padding = new Padding(0, 2, 0, 2);

            _title = new Label { AutoSize = true, ForeColor = DipBuilderForm.Muted, Font = DipBuilderForm.F(9.5f, false), UseMnemonic = false, Margin = new Padding(0, 0, 0, 3) };
            _prefillNote = new Label
            {
                AutoSize = true, ForeColor = DipBuilderForm.Warn, Font = DipBuilderForm.F(9.5f, false),
                MaximumSize = new Size(1000, 0), Visible = false, UseMnemonic = false, Margin = new Padding(0, 0, 0, 4)
            };
            _top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Color.Transparent };
            _top.Controls.Add(_title);
            _top.Controls.Add(_prefillNote);

            _sizeChip = Chip("Size", () => Open(Step.Size));
            _typeChip = Chip("Type", () => Open(Step.Type));
            _dirChip = Chip("Direction", () => Open(Step.Direction));
            _refChip = Chip("IE", () => Open(Step.Reference));

            _dip = new TextBox
            {
                Width = 130, Font = new Font("Consolas", 13f, FontStyle.Regular), BorderStyle = BorderStyle.FixedSingle,
                BackColor = DipBuilderForm.Dark ? DipBuilderForm.Calculated : Color.White, ForeColor = DipBuilderForm.Ink,
                Margin = new Padding(4, 0, 6, 3), Enabled = false
            };
            _dip.TextChanged += (s, e) => { ReadDip(false); UpdatePreview(); };
            _dip.KeyDown += (s, e) =>
            {
                if (e.KeyCode != Keys.Enter && e.KeyCode != Keys.Escape) return;
                e.SuppressKeyPress = true;
                PressInMd(e.KeyCode);
            };
            _enterHint = new Label { AutoSize = true, ForeColor = DipBuilderForm.Muted, Font = DipBuilderForm.F(9f, false), Margin = new Padding(0, 7, 0, 0), Text = "↵ saves" };

            _chips = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, BackColor = Color.Transparent, Margin = Padding.Empty };
            _chips.Controls.AddRange(new Control[] { _sizeChip, _typeChip, _dirChip, _refChip, _dip, _enterHint });

            _strip = new Panel { Dock = DockStyle.Top, BackColor = DipBuilderForm.Ground, Padding = new Padding(8, 6, 8, 6), Visible = false };

            _preview = new Label { Dock = DockStyle.Top, AutoSize = false, Height = 20, Font = new Font("Consolas", 10f, FontStyle.Regular), ForeColor = DipBuilderForm.Muted, UseMnemonic = false };
            _problem = new Label { Dock = DockStyle.Top, AutoSize = false, Height = 0, ForeColor = DipBuilderForm.Bad, Font = DipBuilderForm.F(9.5f, false), UseMnemonic = false };

            Controls.Add(_problem);
            Controls.Add(_preview);
            Controls.Add(_strip);
            Controls.Add(_chips);
            Controls.Add(_top);
        }

        // ------------------------------------------------------------- opening

        public void Begin(PipeChoiceSet choices, PipeObservation editing, string structureLabel, Func<PipeObservation, string> summary)
        {
            Start(choices, editing != null ? QuickPipeEntry.From(editing) : new QuickPipeEntry(), editing, null, structureLabel, null, summary);
        }

        /// <summary>
        /// Opens to enter this structure's end of a connected pipe, starting from what the other
        /// end says: the opposite direction, size and material, marked as copied. The measure down
        /// starts empty -- it is this structure's own observation.
        /// </summary>
        public void BeginComplete(PipeChoiceSet choices, QuickPipeEntry prefill, string connectionId, string structureLabel,
                                  string sourceLabel, Func<PipeObservation, string> summary)
        {
            Start(choices, prefill, null, connectionId, structureLabel, sourceLabel, summary);
        }

        /// <summary>
        /// The words above the chips: which pipe of how many. Set while the slot is open -- the
        /// pipe before it lands a moment after the slot opens -- it updates what the drafter reads.
        /// </summary>
        public string SlotText
        {
            get { return _slotText; }
            set
            {
                _slotText = value;
                if (Visible && EditingPipeId == null && CompletingConnectionId == null)
                    _title.Text = value ?? "New pipe";
            }
        }

        private string _slotText;

        private void Start(PipeChoiceSet choices, QuickPipeEntry from, PipeObservation editing, string connectionId,
                           string structureLabel, string sourceLabel, Func<PipeObservation, string> summary)
        {
            _summary = summary ?? _summary;
            EditingPipeId = editing != null ? editing.Id : null;
            CompletingConnectionId = connectionId;

            _entry.Direction = from.Direction;
            _entry.SizeIn = from.SizeIn;
            _entry.Material = from.Material;
            _entry.MeasuredDip = connectionId != null ? null : from.MeasuredDip;
            // The office's configured default, visible on its chip and one click to change. An
            // edit keeps whatever the pipe already says.
            _entry.Reference = editing != null ? editing.Reference : MeasurementReference.Invert;
            _entry.ReferenceFromDrafter = editing != null && editing.ReferenceBasis == ReferenceBasis.EnteredByDrafter;
            _prefilledStart = (from.Prefilled ?? new List<string>()).ToList();
            _prefilledFrom = from.PrefilledFrom;
            _touched.Clear();

            _title.Text = connectionId != null ? "Complete pipe from " + sourceLabel
                        : editing != null ? "Edit pipe" : SlotText ?? "New pipe";
            _prefillNote.Text = _prefilledStart.Count == 0 ? string.Empty
                : "Copied from " + (_prefilledFrom ?? "the connected pipe") + ": " + string.Join(", ", _prefilledStart.ToArray()) +
                  ". Change any of it as observed here" + (connectionId != null ? ", then enter the MD measured here." : ".");
            _prefillNote.Visible = _prefillNote.Text.Length > 0;

            _dip.Text = _entry.MeasuredDip.HasValue ? QuickPipeEntry.Exact(_entry.MeasuredDip.Value) : string.Empty;
            _problem.Text = string.Empty;
            _largerShown = _moreShown = _dialShown = false;
            MdFocusAsked = false;
            _choices = choices ?? new PipeChoiceSet();

            RefreshChips();
            // A fresh pipe opens on its first question, so the three choices are three clicks.
            Open(_entry.SizeIn.HasValue ? (_entry.Material != null ? Step.None : Step.Type) : Step.Size);
            if (Complete()) FocusDip();
        }

        /// <summary>New buttons for a changed structure type or system; what was chosen is kept.</summary>
        public void SetChoices(PipeChoiceSet choices)
        {
            _choices = choices ?? new PipeChoiceSet();
            RefreshChips();
            if (_step != Step.None) Open(_step);
            UpdatePreview();
            FitWidth(Width);
        }

        public string ChoiceRule { get { return _choices.RuleName; } }

        // --------------------------------------------------------------- chips

        private Button Chip(string text, Action click)
        {
            var b = new Button
            {
                Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlatStyle = FlatStyle.Flat,
                Font = DipBuilderForm.F(10f, false), Margin = new Padding(0, 0, 5, 3), Padding = new Padding(6, 2, 6, 2),
                Cursor = Cursors.Hand, UseMnemonic = false
            };
            b.Click += (s, e) => click();
            return b;
        }

        /// <summary>
        /// A chip either names what it is waiting for, or shows the value. The one FTF is waiting
        /// on next is the only one that stands out, so the eye is led along the row.
        /// </summary>
        private void Dress(Button chip, string label, string value, bool next)
        {
            var has = !string.IsNullOrEmpty(value);
            chip.Text = has ? value : label;
            chip.Font = has ? new Font("Consolas", 10.5f, FontStyle.Regular) : DipBuilderForm.F(10f, false);
            chip.ForeColor = has ? DipBuilderForm.Ink : next ? DipBuilderForm.Accent : DipBuilderForm.Muted;
            chip.BackColor = has ? DipBuilderForm.Surface : DipBuilderForm.Ground;
            chip.FlatAppearance.BorderColor = has ? DipBuilderForm.ButtonBorder : next ? DipBuilderForm.Accent : DipBuilderForm.Rule;
            chip.FlatAppearance.MouseOverBackColor = DipBuilderForm.AccentSoft;
        }

        private void RefreshChips()
        {
            var size = _entry.SizeIn.HasValue ? SizeText(_entry.SizeIn.Value) : null;
            var type = string.IsNullOrWhiteSpace(_entry.Material) ? null : _entry.Material;
            var dir = _entry.Direction != null ? DirectionShortcuts.ButtonFor(_entry.Direction) : null;
            if (_entry.Direction != null && string.IsNullOrEmpty(dir)) dir = _entry.Direction.Text;

            Dress(_sizeChip, "Size", size, size == null);
            Dress(_typeChip, "Type", type, size != null && type == null);
            Dress(_dirChip, "Direction", dir, size != null && type != null && dir == null);
            Dress(_refChip, "IE", ReferenceChip(_entry.Reference) + " ▾", false);
            _refChip.ForeColor = DipBuilderForm.Muted;

            var ready = Complete();
            _dip.Enabled = ready;
            _enterHint.Visible = ready;
            UpdatePreview();
        }

        private bool Complete()
        {
            return _entry.SizeIn.HasValue && !string.IsNullOrWhiteSpace(_entry.Material) && _entry.Direction != null;
        }

        private static string ReferenceChip(MeasurementReference reference)
        {
            switch (reference)
            {
                case MeasurementReference.Invert: return "IE";
                case MeasurementReference.TopOfPipe: return "TOP";
                case MeasurementReference.Springline: return "SPR";
                case MeasurementReference.BottomOfStructure: return "BOT";
                case MeasurementReference.WaterLevel: return "WL";
                default: return "not stated";
            }
        }

        private static string ReferenceWords(MeasurementReference reference)
        {
            switch (reference)
            {
                case MeasurementReference.Invert: return "Invert";
                case MeasurementReference.TopOfPipe: return "Top of pipe";
                case MeasurementReference.Springline: return "Springline";
                case MeasurementReference.BottomOfStructure: return "Bottom";
                case MeasurementReference.WaterLevel: return "Water";
                default: return "Not stated";
            }
        }

        // -------------------------------------------------------------- strip

        /// <summary>Puts one chooser in the strip. Step.None closes it.</summary>
        private void Open(Step step)
        {
            _step = step;
            _strip.SuspendLayout();
            foreach (Control c in _strip.Controls.Cast<Control>().ToList()) { _strip.Controls.Remove(c); c.Dispose(); }

            var row = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, BackColor = Color.Transparent, Margin = Padding.Empty };
            switch (step)
            {
                case Step.Size: BuildSizes(row); break;
                case Step.Type: BuildMaterials(row); break;
                case Step.Direction: BuildDirections(row); break;
                case Step.Reference: BuildReferences(row); break;
            }
            if (step != Step.None) _strip.Controls.Add(row);
            _strip.Visible = step != Step.None;
            _strip.ResumeLayout();
            RefreshChips();
            FitWidth(Width);
        }

        private Button Choice(string text, Action click, bool quiet = false)
        {
            var b = new Button
            {
                Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlatStyle = FlatStyle.Flat,
                Font = quiet ? DipBuilderForm.F(9.5f, false) : new Font("Consolas", 10.5f, FontStyle.Regular),
                Margin = new Padding(0, 0, 4, 3), Padding = new Padding(6, 2, 6, 2), Cursor = Cursors.Hand,
                BackColor = DipBuilderForm.Surface, ForeColor = quiet ? DipBuilderForm.Muted : DipBuilderForm.Ink, UseMnemonic = false
            };
            b.FlatAppearance.BorderColor = DipBuilderForm.ButtonBorder;
            b.FlatAppearance.MouseOverBackColor = DipBuilderForm.AccentSoft;
            b.Click += (s, e) => click();
            return b;
        }

        private TextBox SmallBox(int width, string text = "")
        {
            return new TextBox
            {
                Width = width, Text = text, BorderStyle = BorderStyle.FixedSingle, Font = DipBuilderForm.F(10f, false),
                BackColor = DipBuilderForm.Dark ? DipBuilderForm.Calculated : Color.White, ForeColor = DipBuilderForm.Ink,
                Margin = new Padding(0, 1, 4, 3)
            };
        }

        private void BuildSizes(FlowLayoutPanel row)
        {
            var sizes = _largerShown ? _choices.LargerSizes : _choices.CommonSizes;
            foreach (var value in sizes)
            {
                var v = value;
                row.Controls.Add(Choice(SizeText(v), () => PickSize(v)));
            }
            row.Controls.Add(Choice(_largerShown ? CommonSizesText : LargerText, () => { _largerShown = !_largerShown; Open(Step.Size); }, true));
            if (_largerShown)
            {
                var typed = SmallBox(70);
                typed.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; UseTypedSize(typed.Text); } };
                row.Controls.Add(typed);
                row.Controls.Add(Choice("Use", () => UseTypedSize(typed.Text), true));
            }
        }

        private void BuildMaterials(FlowLayoutPanel row)
        {
            var materials = _moreShown ? _choices.MoreMaterials : _choices.CommonMaterials;
            foreach (var value in materials)
            {
                var v = value;
                row.Controls.Add(Choice(v, () => PickMaterial(v)));
            }
            row.Controls.Add(Choice(_moreShown ? UsualMaterialsText : MoreText, () => { _moreShown = !_moreShown; Open(Step.Type); }, true));
            if (_moreShown)
            {
                var typed = SmallBox(120);
                typed.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; PickMaterial(typed.Text); } };
                row.Controls.Add(typed);
                row.Controls.Add(Choice("Use", () => PickMaterial(typed.Text), true));
            }
        }

        /// <summary>
        /// The sixteen, in compass order, as two rows of eight -- read and hit like any other
        /// list. A typed bearing or azimuth covers anything off the sixteen, and the dial is
        /// there for anyone who would rather aim.
        /// </summary>
        private void BuildDirections(FlowLayoutPanel row)
        {
            if (_dialShown)
            {
                var dial = new CompassPicker { Width = 150, Height = 150, Margin = new Padding(0, 0, 8, 3) };
                dial.DirectionPicked += PickDirection;
                if (_entry.Direction != null) dial.Selected = DirectionShortcuts.ButtonFor(_entry.Direction);
                row.Controls.Add(dial);
            }
            else
            {
                var grid = new TableLayoutPanel { ColumnCount = 8, RowCount = 2, AutoSize = true, Margin = new Padding(0, 0, 0, 3), BackColor = Color.Transparent };
                for (var i = 0; i < 8; i++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 12.5f));
                foreach (var name in DirectionShortcuts.Names)
                {
                    var n = name;
                    var b = Choice(n, () => PickDirection(n));
                    b.Margin = new Padding(0, 0, 3, 3);
                    b.AutoSize = false;
                    b.Width = 56;
                    b.Height = 26;
                    grid.Controls.Add(b);
                }
                row.Controls.Add(grid);
            }

            var line = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Color.Transparent, Margin = Padding.Empty };
            var typed = SmallBox(150);
            typed.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; UseTypedDirection(typed.Text); } };
            line.Controls.Add(new Label { Text = "or", AutoSize = true, ForeColor = DipBuilderForm.Muted, Font = DipBuilderForm.F(9.5f, false), Margin = new Padding(0, 5, 5, 0) });
            line.Controls.Add(typed);
            line.Controls.Add(Choice("Use", () => UseTypedDirection(typed.Text), true));
            line.Controls.Add(Choice(UnknownDirection, () => PickDirection(UnknownDirection), true));
            line.Controls.Add(Choice(_dialShown ? "rows" : "◎ dial", () => { _dialShown = !_dialShown; Open(Step.Direction); }, true));
            row.Controls.Add(line);
        }

        private void BuildReferences(FlowLayoutPanel row)
        {
            foreach (var reference in References)
            {
                var r = reference;
                var b = Choice(ReferenceWords(r), () => PickReference(r), r == MeasurementReference.Unspecified);
                if (r == _entry.Reference)
                {
                    b.BackColor = DipBuilderForm.AccentSoft;
                    b.FlatAppearance.BorderColor = DipBuilderForm.Accent;
                }
                row.Controls.Add(b);
            }
        }

        // -------------------------------------------------------------- picking

        private void PickSize(double inches)
        {
            _touched.Add(QuickPipeEntry.SizeField);
            _entry.SizeIn = inches;
            Advance(Step.Size);
        }

        private void UseTypedSize(string text)
        {
            double value;
            if (!double.TryParse((text ?? string.Empty).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) || value <= 0)
            {
                Problem("Enter the size in inches, as it was measured.");
                return;
            }
            PickSize(value);
        }

        private void PickMaterial(string material)
        {
            var value = (material ?? string.Empty).Trim().ToUpperInvariant();
            if (value.Length == 0) { Problem("Enter the material, or pick one."); return; }
            _touched.Add(QuickPipeEntry.MaterialField);
            _entry.Material = value;
            Advance(Step.Type);
        }

        private void PickDirection(string name)
        {
            _touched.Add(QuickPipeEntry.DirectionField);
            _entry.Direction = name == UnknownDirection ? ObservedDirection.Unknown("?") : DirectionShortcuts.For(name);
            Advance(Step.Direction);
        }

        private void UseTypedDirection(string text)
        {
            // A drafter who types just a number means an azimuth -- a bare number is never a bearing.
            var typed = (text ?? string.Empty).Trim();
            double azimuth;
            if (double.TryParse(typed, NumberStyles.Float, CultureInfo.InvariantCulture, out azimuth) &&
                azimuth >= 0 && azimuth <= 360)
                typed = "AZ" + typed;

            var direction = DirectionShortcuts.Parse(typed);
            if (direction == null) { Problem("That is not a direction FTF can read. Try N, N/NE, a bearing or an azimuth."); return; }
            _touched.Add(QuickPipeEntry.DirectionField);
            _entry.Direction = direction;
            Advance(Step.Direction);
        }

        private void PickReference(MeasurementReference reference)
        {
            _entry.Reference = reference;
            _entry.ReferenceFromDrafter = true;
            Advance(Step.Reference);
        }

        /// <summary>
        /// Moves to whatever is still unanswered after this step, or closes the strip and takes
        /// the caret to the measure down when the pipe is described.
        /// </summary>
        private void Advance(Step from)
        {
            Problem(null);
            if (!_entry.SizeIn.HasValue) { Open(Step.Size); return; }
            if (string.IsNullOrWhiteSpace(_entry.Material)) { Open(Step.Type); return; }
            if (_entry.Direction == null) { Open(Step.Direction); return; }
            Open(Step.None);
            FocusDip();
        }

        private void FocusDip()
        {
            if (!_dip.Enabled) return;
            MdFocusAsked = true;
            _dip.Focus();
            _dip.SelectionStart = _dip.Text.Length;
        }

        /// <summary>
        /// Enter finishes the pipe; Escape abandons the slot. Behind a method so the UI test can
        /// press the key without a focused window -- everything but the WinForms plumbing runs.
        /// </summary>
        public void PressInMd(Keys key)
        {
            if (key == Keys.Enter) Submit();
            else if (key == Keys.Escape) Cancel();
        }

        // ------------------------------------------------- what the panel has so far
        // Read by the Dip UI test so it checks the contract rather than the controls.

        public double? ChosenSize { get { return _entry.SizeIn; } }
        public string ChosenType { get { return _entry.Material; } }

        public string ChosenDirection
        {
            get
            {
                if (_entry.Direction == null) return null;
                var name = DirectionShortcuts.ButtonFor(_entry.Direction);
                return string.IsNullOrEmpty(name) ? _entry.Direction.Text : name;
            }
        }

        public double? ChosenAzimuth { get { return _entry.Direction != null ? _entry.Direction.AzimuthDegrees : null; } }
        public MeasurementReference ChosenReference { get { return _entry.Reference; } }
        public string ReferenceChipText { get { return _refChip.Text; } }
        public bool MdEnabled { get { return _dip.Enabled; } }

        /// <summary>Set when the panel asked for the caret; a hidden window never really gets focus.</summary>
        public bool MdFocusAsked { get; private set; }

        public string MdText { get { return _dip.Text; } set { _dip.Text = value; } }

        /// <summary>Which chooser the one strip is holding: Size, Type, Direction, Reference or None.</summary>
        public string OpenChooser { get { return _strip.Visible ? _step.ToString() : Step.None.ToString(); } }

        /// <summary>The buttons in the strip as it stands, in order.</summary>
        public IList<string> ChooserButtons
        {
            get
            {
                var found = new List<string>();
                Walk(_strip, found);
                return found;
            }
        }

        private static void Walk(Control root, List<string> into)
        {
            foreach (Control c in root.Controls)
            {
                var b = c as Button;
                if (b != null) into.Add(b.Text);
                Walk(c, into);
            }
        }

        /// <summary>Presses a button in the strip by its text. False when it is not there.</summary>
        public bool PressInChooser(string text)
        {
            var b = Find(_strip, text);
            if (b == null) return false;
            b.PerformClick();
            return true;
        }

        /// <summary>Types into the strip's own box (a typed size, material, bearing or azimuth).</summary>
        public bool TypeInChooser(string text)
        {
            var box = Boxes(_strip).FirstOrDefault();
            if (box == null) return false;
            box.Text = text;
            return true;
        }

        /// <summary>Opens a chooser as clicking its chip does.</summary>
        public void OpenChooserFor(string which)
        {
            switch ((which ?? string.Empty).ToUpperInvariant())
            {
                case "SIZE": _sizeChip.PerformClick(); break;
                case "TYPE": _typeChip.PerformClick(); break;
                case "DIRECTION": _dirChip.PerformClick(); break;
                case "REFERENCE": _refChip.PerformClick(); break;
            }
        }

        private static Button Find(Control root, string text)
        {
            foreach (Control c in root.Controls)
            {
                var b = c as Button;
                if (b != null && b.Text == text) return b;
                var inner = Find(c, text);
                if (inner != null) return inner;
            }
            return null;
        }

        private static IEnumerable<TextBox> Boxes(Control root)
        {
            foreach (Control c in root.Controls)
            {
                var t = c as TextBox;
                if (t != null) yield return t;
                foreach (var inner in Boxes(c)) yield return inner;
            }
        }

        // ------------------------------------------------------------ finishing

        private bool ReadDip(bool required)
        {
            var text = _dip.Text.Trim();
            if (text.Length == 0)
            {
                _entry.MeasuredDip = null;
                if (required) { Problem("Type the measure down, then press Enter."); return false; }
                Problem(null);
                return true;
            }
            double value;
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                Problem("The measure down must be a number in feet.");
                return false;
            }
            _entry.MeasuredDip = value;
            Problem(null);
            return true;
        }

        private void Submit()
        {
            if (!Complete()) { Problem("Choose the size, type and direction first."); return; }
            if (!ReadDip(true)) return;

            var entry = new QuickPipeEntry
            {
                Direction = _entry.Direction,
                SizeIn = _entry.SizeIn,
                Material = _entry.Material,
                MeasuredDip = _entry.MeasuredDip,
                Reference = _entry.Reference,
                ReferenceFromDrafter = _entry.ReferenceFromDrafter,
                // Copied values stay marked as copied until the drafter changes them here.
                Prefilled = _prefilledStart.Where(f => !_touched.Contains(f)).ToList(),
                PrefilledFrom = _prefilledFrom
            };
            // A new pipe asks for the next empty slot; an edit or a completion does not.
            if (Submitted != null) Submitted(entry, EditingPipeId == null && CompletingConnectionId == null);
        }

        private void Cancel()
        {
            if (Cancelled != null) Cancelled(this, EventArgs.Empty);
        }

        private void Problem(string text)
        {
            _problem.Text = text ?? string.Empty;
            _problem.Height = _problem.Text.Length == 0 ? 0 : _problem.Font.Height + 4;
        }

        private void UpdatePreview()
        {
            if (!Complete()) { _preview.Text = string.Empty; return; }
            var pipe = _entry.Create();
            _preview.Text = _summary != null ? _summary(pipe) : QuickPipeEntry.Summary(pipe, new UtilitySettings());
        }

        // ----------------------------------------------------------- housekeeping

        public static string SizeText(double inches) { return QuickPipeEntry.Exact(inches) + "\""; }

        public void FitWidth(int width)
        {
            Width = width;
            var inner = Math.Max(160, width - Padding.Horizontal);
            _top.Width = inner;
            _chips.Width = inner;
            _chips.Height = _chips.GetPreferredSize(new Size(inner, 0)).Height;
            _strip.Width = inner;
            _strip.Height = _strip.Visible
                ? _strip.Controls.Cast<Control>().Sum(c => c.GetPreferredSize(new Size(inner - _strip.Padding.Horizontal, 0)).Height) + _strip.Padding.Vertical
                : 0;
            _preview.Height = _preview.Text.Length == 0 ? 0 : 20;
            Height = Padding.Vertical + _top.Height + _chips.Height + _strip.Height + _preview.Height + _problem.Height;
        }
    }
}
