using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using FieldCodes.Easements;
using FieldCodes.Settings;

namespace FieldCodes.Cad.Ui
{
    /// <summary>
    /// The strip after trimming, before anything is drawn. Each piece the trim lines
    /// made can be clicked to keep or remove it; the title, area and any problems
    /// update as the drafter goes. Nothing reaches the drawing until "Draw easement".
    /// </summary>
    internal sealed class EasementPreviewForm : Form
    {
        private readonly EasementCommands.TrimPreview _preview;
        private readonly HashSet<int> _keep;
        private readonly TrimCanvas _canvas;
        private readonly TextBox _purpose;
        private readonly Label _title;
        private readonly Label _area;
        private readonly Label _pieces;
        private readonly Label _temporaryCaption;
        private readonly Label _temporary;
        private readonly Label _tiesCaption;
        private readonly Label _ties;
        private readonly Label _problem;
        private readonly Label _notes;
        private readonly Button _draw;

        // The drafting panel. These are not readonly: they are built in a helper the
        // constructor calls, not in the constructor body.
        private CheckBox _hatchOn;
        private ComboBox _hatchPattern;
        private CheckBox _temporaryHatchOn;
        private ComboBox _temporaryHatchPattern;
        private ComboBox _labelWhere;
        private CheckBox _centerlineOn;
        private CheckBox _sidelinesOn;
        private CheckBox _widthDimensions;
        private CheckBox _pointLabelsOn;
        private readonly Dictionary<string, TextBox> _layerBoxes = new Dictionary<string, TextBox>();
        private bool _loading;

        public EasementPreviewForm(EasementCommands.TrimPreview preview)
        {
            _preview = preview;
            // The drafting panel edits this easement's own copy. Without one it would be
            // editing the office settings, which must never change from here.
            if (_preview.Drafting == null) _preview.Drafting = _preview.Settings.Copy();
            _keep = new HashSet<int>(preview.Keep ?? new List<int> { 1 });

            DipBuilderForm.UseTheme(ThemePreference.LoadDark());
            Text = "Strip Easement Preview";
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = false;
            MinimizeBox = false;
            BackColor = DipBuilderForm.Ground;
            ForeColor = DipBuilderForm.Ink;
            Font = DipBuilderForm.F(10.5f, false);
            var area = Screen.FromPoint(Cursor.Position).WorkingArea;
            ClientSize = new Size(Math.Min(1460, area.Width - 60), Math.Min(800, area.Height - 60));
            MinimumSize = new Size(Math.Min(1040, area.Width), Math.Min(560, area.Height));

            // Header.
            var header = new Panel { Dock = DockStyle.Top, BackColor = DipBuilderForm.Surface, Padding = new Padding(20, 12, 20, 10) };
            var heading = new Label { AutoSize = true, Text = "Check the easement before it is drawn", Font = DipBuilderForm.F(15f, true), ForeColor = DipBuilderForm.Ink };
            var guide = new Label
            {
                AutoSize = true, ForeColor = DipBuilderForm.Muted, Margin = new Padding(1, 4, 0, 0),
                Text = "Click a piece to keep or remove it; set the drafting on the left. Nothing is drawn until Draw easement."
            };
            var headerText = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Dock = DockStyle.Fill };
            headerText.Controls.Add(heading);
            headerText.Controls.Add(guide);
            header.Controls.Add(headerText);
            header.Height = heading.PreferredHeight + guide.PreferredHeight + header.Padding.Vertical + 8;
            header.Paint += (s, e) => { using (var pen = new Pen(DipBuilderForm.Rule)) e.Graphics.DrawLine(pen, 0, header.Height - 1, header.Width, header.Height - 1); };

            // Side panel: title, area, problems, buttons.
            var side = new Panel { Dock = DockStyle.Right, Width = 340, BackColor = DipBuilderForm.Surface, Padding = new Padding(18, 16, 18, 16) };
            side.Paint += (s, e) => { using (var pen = new Pen(DipBuilderForm.Rule)) e.Graphics.DrawLine(pen, 0, 0, 0, side.Height); };

            var stack = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
            var width = side.Width - side.Padding.Horizontal - 8;
            stack.Controls.Add(Caption("Purpose"));
            _purpose = new TextBox
            {
                Width = width, Text = preview.Purpose, CharacterCasing = CharacterCasing.Upper, BorderStyle = BorderStyle.FixedSingle,
                BackColor = DipBuilderForm.Dark ? DipBuilderForm.Calculated : Color.White, ForeColor = DipBuilderForm.Ink,
                Font = DipBuilderForm.F(11f, false), Margin = new Padding(0, 2, 0, 14)
            };
            _purpose.TextChanged += (s, e) => Recalculate(true);
            stack.Controls.Add(_purpose);

            stack.Controls.Add(Caption("Title"));
            _title = Value(width, DipBuilderForm.F(12f, true));
            stack.Controls.Add(_title);
            stack.Controls.Add(Caption("Kept area"));
            _area = Value(width, DipBuilderForm.F(12f, true));
            stack.Controls.Add(_area);
            _temporaryCaption = Caption("Temporary construction easement");
            stack.Controls.Add(_temporaryCaption);
            _temporary = Value(width, DipBuilderForm.F(11f, false));
            stack.Controls.Add(_temporary);
            _tiesCaption = Caption("Ties");
            stack.Controls.Add(_tiesCaption);
            _ties = Value(width, DipBuilderForm.F(10f, false));
            stack.Controls.Add(_ties);
            stack.Controls.Add(Caption("Pieces"));
            _pieces = Value(width, DipBuilderForm.F(10.5f, false));
            stack.Controls.Add(_pieces);

            _problem = Value(width, DipBuilderForm.F(10.5f, false));
            _problem.ForeColor = DipBuilderForm.Bad;
            stack.Controls.Add(_problem);

            _notes = Value(width, DipBuilderForm.F(9.5f, false));
            _notes.ForeColor = DipBuilderForm.Muted;
            stack.Controls.Add(_notes);

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 10, 0, 0) };
            _draw = Btn("Draw easement", true);
            _draw.Click += (s, e) => Accept();
            var cancel = Btn("Cancel", false);
            cancel.DialogResult = DialogResult.Cancel;
            buttons.Controls.Add(_draw);
            buttons.Controls.Add(cancel);
            AcceptButton = _draw;
            CancelButton = cancel;

            side.Controls.Add(stack);
            side.Controls.Add(buttons);

            _canvas = new TrimCanvas(preview, _keep) { Dock = DockStyle.Fill };
            _canvas.PieceClicked += number =>
            {
                if (!_keep.Remove(number)) _keep.Add(number);
                Recalculate(true);
            };

            Controls.Add(_canvas);
            Controls.Add(side);
            Controls.Add(BuildDrafting());
            Controls.Add(header);
            LoadDrafting();
            Recalculate(true);
        }

        /// <summary>The settings this easement will be drafted with -- the drafting panel's copy.</summary>
        private EasementSettings Chosen { get { return _preview.Drafting; } }

        /// <summary>
        /// The drafting panel: hatch, course labels, width dimensions and the layers each
        /// piece goes on, as the office profile has them. Changing one changes this
        /// easement only; the office settings are never written to from here.
        /// </summary>
        private Panel BuildDrafting()
        {
            var panel = new Panel { Dock = DockStyle.Left, Width = 306, BackColor = DipBuilderForm.Surface, Padding = new Padding(18, 14, 14, 14) };
            panel.Paint += (s, e) => { using (var pen = new Pen(DipBuilderForm.Rule)) e.Graphics.DrawLine(pen, panel.Width - 1, 0, panel.Width - 1, panel.Height); };
            var stack = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
            var width = panel.Width - panel.Padding.Horizontal - 20;

            stack.Controls.Add(Caption("Hatch"));
            _hatchOn = Tick("Hatch the easement");
            _hatchOn.CheckedChanged += (s, e) => Changed(() => { Chosen.DrawHatch = _hatchOn.Checked; _hatchPattern.Enabled = _hatchOn.Checked; });
            stack.Controls.Add(_hatchOn);
            _hatchPattern = Patterns(width, Chosen.HatchPattern);
            _hatchPattern.TextChanged += (s, e) => Changed(() => Chosen.HatchPattern = _hatchPattern.Text.Trim());
            stack.Controls.Add(_hatchPattern);

            if (_preview.TemporarySplit != null)
            {
                _temporaryHatchOn = Tick("Hatch the temporary easement");
                _temporaryHatchOn.CheckedChanged += (s, e) => Changed(() =>
                {
                    _temporaryHatchPattern.Enabled = _temporaryHatchOn.Checked;
                    Chosen.TemporaryHatchPattern = _temporaryHatchOn.Checked
                        ? (_temporaryHatchPattern.Text.Trim().Length > 0 ? _temporaryHatchPattern.Text.Trim() : "ANSI37")
                        : string.Empty;
                });
                stack.Controls.Add(_temporaryHatchOn);
                _temporaryHatchPattern = Patterns(width, Chosen.TemporaryHatchPattern);
                _temporaryHatchPattern.TextChanged += (s, e) => Changed(() =>
                {
                    if (_temporaryHatchOn.Checked) Chosen.TemporaryHatchPattern = _temporaryHatchPattern.Text.Trim();
                });
                stack.Controls.Add(_temporaryHatchPattern);
            }

            stack.Controls.Add(Caption("Course labels"));
            _labelWhere = new ComboBox
            {
                Width = width, DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat,
                BackColor = DipBuilderForm.Dark ? DipBuilderForm.Calculated : Color.White, ForeColor = DipBuilderForm.Ink,
                Font = DipBuilderForm.F(10f, false), Margin = new Padding(0, 2, 0, 12)
            };
            _labelWhere.Items.AddRange(new object[] { "Along the centerline", "Around the outline", "In a line / curve table", "No course labels" });
            _labelWhere.SelectedIndexChanged += (s, e) => Changed(ApplyLabelChoice);
            stack.Controls.Add(_labelWhere);

            stack.Controls.Add(Caption("Lines and dimensions"));
            _centerlineOn = Tick("Draw the centerline");
            _centerlineOn.CheckedChanged += (s, e) => Changed(() => Chosen.DrawCenterline = _centerlineOn.Checked);
            stack.Controls.Add(_centerlineOn);
            _sidelinesOn = Tick("Draw the sidelines");
            _sidelinesOn.CheckedChanged += (s, e) => Changed(() => Chosen.DrawSidelines = _sidelinesOn.Checked);
            stack.Controls.Add(_sidelinesOn);
            _widthDimensions = Tick("Dimension the width");
            _widthDimensions.CheckedChanged += (s, e) => Changed(() => Chosen.DrawWidthDimensions = _widthDimensions.Checked);
            stack.Controls.Add(_widthDimensions);
            stack.Controls.Add(new Label
            {
                AutoSize = true, MaximumSize = new Size(width, 0), ForeColor = DipBuilderForm.Muted,
                Font = DipBuilderForm.F(9f, false), Margin = new Padding(20, 0, 0, 10),
                Text = string.IsNullOrWhiteSpace(Chosen.DimensionStyleOverride)
                    ? "in the drawing's current dimension style"
                    : "in dimension style " + Chosen.DimensionStyleOverride
            });
            _pointLabelsOn = Tick("Label the POC, POB and terminus");
            _pointLabelsOn.CheckedChanged += (s, e) => Changed(() => Chosen.DrawPointLabels = _pointLabelsOn.Checked);
            stack.Controls.Add(_pointLabelsOn);

            stack.Controls.Add(Caption("Layers"));
            LayerBox(stack, width, "Boundary", Chosen.BoundaryLayer, v => Chosen.BoundaryLayer = v);
            LayerBox(stack, width, "Hatch", Chosen.HatchLayer, v => Chosen.HatchLayer = v);
            LayerBox(stack, width, "Text", Chosen.TextLayer, v => Chosen.TextLayer = v);
            LayerBox(stack, width, "Dimensions", Chosen.DimensionLayer, v => Chosen.DimensionLayer = v);
            LayerBox(stack, width, "Centerline", Chosen.CenterlineLayer, v => Chosen.CenterlineLayer = v);
            LayerBox(stack, width, "Sidelines", Chosen.SidelineLayer, v => Chosen.SidelineLayer = v);
            if (_preview.TemporarySplit != null)
            {
                LayerBox(stack, width, "Temporary outline", Chosen.TemporaryLayer, v => Chosen.TemporaryLayer = v);
                LayerBox(stack, width, "Temporary hatch", Chosen.TemporaryHatchLayer, v => Chosen.TemporaryHatchLayer = v);
                LayerBox(stack, width, "Temporary text", Chosen.TemporaryTextLayer, v => Chosen.TemporaryTextLayer = v);
                LayerBox(stack, width, "Temporary dimensions", Chosen.TemporaryDimensionLayer, v => Chosen.TemporaryDimensionLayer = v);
            }
            stack.Controls.Add(new Label
            {
                AutoSize = true, MaximumSize = new Size(width, 0), ForeColor = DipBuilderForm.Muted, Font = DipBuilderForm.F(9f, false),
                Margin = new Padding(0, 6, 0, 0),
                Text = "These start from the office profile. What you change here is drawn for this easement and kept with it, so a rebuild draws it the same way."
            });

            panel.Controls.Add(stack);
            return panel;
        }

        /// <summary>Puts the settings on the controls, without treating that as a change.</summary>
        private void LoadDrafting()
        {
            _loading = true;
            _hatchOn.Checked = Chosen.DrawHatch;
            _hatchPattern.Enabled = Chosen.DrawHatch;
            if (_temporaryHatchOn != null)
            {
                _temporaryHatchOn.Checked = !string.IsNullOrWhiteSpace(Chosen.TemporaryHatchPattern);
                _temporaryHatchPattern.Enabled = _temporaryHatchOn.Checked;
            }
            _labelWhere.SelectedIndex = Chosen.LabelMode == EasementLabelMode.None ? 3
                : Chosen.LabelCenterline ? 0
                : Chosen.LabelMode == EasementLabelMode.Table ? 2 : 1;
            _centerlineOn.Checked = Chosen.DrawCenterline;
            _sidelinesOn.Checked = Chosen.DrawSidelines;
            _widthDimensions.Checked = Chosen.DrawWidthDimensions;
            _pointLabelsOn.Checked = Chosen.DrawPointLabels;
            _loading = false;
        }

        private void ApplyLabelChoice()
        {
            switch (_labelWhere.SelectedIndex)
            {
                case 0:
                    Chosen.LabelCenterline = true;
                    if (Chosen.LabelMode == EasementLabelMode.None) Chosen.LabelMode = EasementLabelMode.Auto;
                    break;
                case 1: Chosen.LabelCenterline = false; Chosen.LabelMode = EasementLabelMode.Direct; break;
                case 2: Chosen.LabelCenterline = false; Chosen.LabelMode = EasementLabelMode.Table; break;
                default: Chosen.LabelCenterline = false; Chosen.LabelMode = EasementLabelMode.None; break;
            }
        }

        private void Changed(Action apply)
        {
            if (_loading) return;
            apply();
            Recalculate(true);
        }

        private CheckBox Tick(string text)
        {
            return new CheckBox
            {
                Text = text, AutoSize = true, ForeColor = DipBuilderForm.Ink, Font = DipBuilderForm.F(10f, false),
                FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 2, 0, 4)
            };
        }

        /// <summary>The office hatch patterns, with whatever the profile uses first. The list is
        /// a shortcut, not a limit: the drawing's pattern file decides what is possible.</summary>
        private ComboBox Patterns(int width, string current)
        {
            var box = new ComboBox
            {
                Width = width, FlatStyle = FlatStyle.Flat, Font = DipBuilderForm.F(10f, false),
                BackColor = DipBuilderForm.Dark ? DipBuilderForm.Calculated : Color.White, ForeColor = DipBuilderForm.Ink,
                Margin = new Padding(20, 0, 0, 10)
            };
            foreach (var name in new[] { "ANSI31", "ANSI32", "ANSI33", "ANSI37", "ANSI38", "DOTS", "GRAVEL", "EARTH", "SOLID" })
                box.Items.Add(name);
            var now = (current ?? string.Empty).Trim();
            if (now.Length > 0 && !box.Items.Contains(now)) box.Items.Insert(0, now);
            box.Text = now;
            return box;
        }

        private void LayerBox(Control stack, int width, string caption, string value, Action<string> write)
        {
            stack.Controls.Add(new Label
            {
                AutoSize = true, Text = caption, ForeColor = DipBuilderForm.Muted,
                Font = DipBuilderForm.F(9f, false), Margin = new Padding(0, 2, 0, 0)
            });
            var box = new TextBox
            {
                Width = width, Text = value ?? string.Empty, BorderStyle = BorderStyle.FixedSingle,
                BackColor = DipBuilderForm.Dark ? DipBuilderForm.Calculated : Color.White, ForeColor = DipBuilderForm.Ink,
                Font = DipBuilderForm.F(10f, false), Margin = new Padding(0, 0, 0, 6)
            };
            box.TextChanged += (s, e) => { if (!_loading) write(box.Text.Trim()); };
            stack.Controls.Add(box);
            _layerBoxes[caption] = box;
        }

        private static Label Caption(string text)
        {
            return new Label
            {
                AutoSize = true, Text = text.ToUpperInvariant(), ForeColor = DipBuilderForm.Muted,
                Font = DipBuilderForm.F(8.5f, false), Margin = new Padding(0, 0, 0, 2)
            };
        }

        private static Label Value(int width, Font font)
        {
            return new Label { AutoSize = true, MaximumSize = new Size(width, 0), Font = font, ForeColor = DipBuilderForm.Ink, Margin = new Padding(0, 0, 0, 14) };
        }

        private static Button Btn(string text, bool primary)
        {
            var b = new Button
            {
                Text = text, AutoSize = true, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand,
                BackColor = primary ? DipBuilderForm.Accent : DipBuilderForm.Surface, ForeColor = primary ? Color.White : DipBuilderForm.Ink,
                Font = DipBuilderForm.F(10f, false), Padding = new Padding(10, 3, 10, 3), Margin = new Padding(8, 0, 0, 0)
            };
            b.FlatAppearance.BorderColor = primary ? DipBuilderForm.Accent : DipBuilderForm.ButtonBorder;
            b.FlatAppearance.MouseOverBackColor = primary ? DipBuilderForm.AccentHover : DipBuilderForm.AccentSoft;
            return b;
        }

        private void Recalculate(bool geometry)
        {
            var purpose = string.IsNullOrWhiteSpace(_purpose.Text) ? _preview.Settings.DefaultPurpose : _purpose.Text.Trim();
            _title.Text = EasementAnnotation.Title(_preview.Width, purpose, _preview.Settings);
            if (!geometry) return;

            string failure;
            var merged = StripTrim.Merge(_preview.Split, _keep, _preview.Tolerance, out failure);
            var upf = _preview.UnitsPerFoot;
            var s = _preview.Settings;
            const string deg = "°";
            List<List<Course>> parts = null;
            if (merged != null)
            {
                var sqft = Math.Abs(Loops.SignedArea(merged)) / (upf * upf);
                _area.Text = string.Join("\n", EasementAnnotation.AreaLines(sqft, s, _purpose.Text).ToArray());
                parts = StripTrim.PartsInside(_preview.Route, merged, _preview.Tolerance);
                if (parts.Count == 0)
                    failure = "The easement line does not run through the kept pieces.";
            }
            else
                _area.Text = "-";

            // The temporary construction easement keeps what surrounds the kept pieces.
            _canvas.TemporaryKeep = new HashSet<int>();
            _temporaryCaption.Visible = _temporary.Visible = _preview.TemporarySplit != null;
            if (_preview.TemporarySplit != null)
            {
                P2? missing;
                var keepPoints = _preview.Split.Pieces.Where(p => _keep.Contains(p.Number)).Select(p => p.InsidePoint);
                _canvas.TemporaryKeep = new HashSet<int>(EasementCommands.PiecesAt(_preview.TemporarySplit, keepPoints, out missing));
                string temporaryFailure;
                var temporary = _canvas.TemporaryKeep.Count == 0 ? null
                    : StripTrim.Merge(_preview.TemporarySplit, _canvas.TemporaryKeep, _preview.Tolerance, out temporaryFailure);
                var title = EasementAnnotation.Title(_preview.TemporaryWidth, s.TemporaryPurpose, s);
                if (temporary != null)
                {
                    var sqft = Math.Abs(Loops.SignedArea(temporary)) / (upf * upf);
                    _temporary.Text = title + "\n" + string.Join("\n", EasementAnnotation.AreaLines(sqft, s, s.TemporaryPurpose).ToArray());
                }
                else
                {
                    _temporary.Text = title + "\n-";
                    if (failure == null && merged != null) failure = "The temporary construction easement could not be matched to the kept pieces.";
                }
            }

            // Ties from the Point of Commencement and to the terminus corner, as they will be described.
            var ties = new List<string>();
            _canvas.Beginning = _canvas.Terminus = null;
            if (parts != null && parts.Count > 0)
            {
                var beginning = parts[0][0].Start;
                var last = parts[parts.Count - 1];
                var terminus = last[last.Count - 1].End;
                _canvas.Beginning = beginning;
                _canvas.Terminus = terminus;
                if (_preview.Commencement.HasValue)
                    ties.Add("Commencement to beginning:\n  " + EasementAnnotation.LineText(EasementAnnotation.Describe(Course.Line(_preview.Commencement.Value, beginning)), s, deg));
                if (_preview.TerminusCorner.HasValue)
                    ties.Add("Terminus to corner:\n  " + EasementAnnotation.LineText(EasementAnnotation.Describe(Course.Line(terminus, _preview.TerminusCorner.Value)), s, deg));
            }
            _ties.Text = string.Join("\n", ties.ToArray());
            _tiesCaption.Visible = _ties.Visible = ties.Count > 0;

            // What the drafting will look like, at the size it will really be drawn.
            var plot = Math.Max(1e-9, _preview.PlotScale);
            _canvas.TextHeight = Chosen.TextHeightPlotted * plot;
            _canvas.HatchSpacing = Chosen.HatchScale * plot * HatchPatternSpacing;
            _canvas.HatchPattern = Chosen.DrawHatch ? Chosen.HatchPattern : null;
            _canvas.TemporaryHatchPattern = Chosen.TemporaryHatchPattern;
            _canvas.Labels = new List<PreviewLabel>();
            _canvas.Title = null;
            _canvas.Dimension = null;
            if (merged != null && parts != null && parts.Count > 0) BuildDraftingPreview(merged, parts);

            var total = _preview.Split.Pieces.Count;
            _pieces.Text = total == 1
                ? "One piece -- the trim lines do not divide the strip."
                : string.Format("{0} of {1} kept. Blue pieces are kept; outlined pieces are left out.", _keep.Count, total);
            _problem.Text = failure ?? string.Empty;
            _problem.Visible = failure != null;
            _notes.Text = _preview.Notes.Count == 0 ? string.Empty : "Notes:\n" + string.Join("\n", _preview.Notes.Select(n => "- " + n).ToArray());
            _draw.Enabled = failure == null;
            _canvas.Invalidate();
        }

        /// <summary>
        /// ANSI31 and its family draw their lines 0.125 drawing units apart at scale 1.
        /// The preview uses that to show the hatch at the density it will really have.
        /// </summary>
        private const double HatchPatternSpacing = 0.125;

        private const string Degree = "\u00b0";

        /// <summary>
        /// The title, course labels and width dimension exactly where the command would
        /// draw them, so what the drafter sees is what the drawing gets. The geometry is
        /// not touched: this only reads it.
        /// </summary>
        private void BuildDraftingPreview(IList<Course> boundary, List<List<Course>> parts)
        {
            var es = Chosen;
            var height = _canvas.TextHeight;
            var longest = parts.OrderByDescending(EasementBuilder.RouteLength).First();
            var outer = _preview.TemporaryWidth ?? _preview.Width;

            // Title and area, along the middle of the strip.
            P2 direction;
            var middle = EasementAnnotation.LabelPoint(longest, _preview.Width, out direction);
            if (!StripTrim.Inside(boundary, middle)) middle = StripTrim.PointInside(boundary, _preview.Tolerance);
            var titleLines = new List<string> { _title.Text };
            titleLines.AddRange(_area.Text.Split('\n'));
            _canvas.Title = new PreviewLabel
            {
                At = middle, Rotation = EasementCommands.Readable(Math.Atan2(direction.Y, direction.X)), Lines = titleLines
            };

            if (es.LabelMode != EasementLabelMode.None)
            {
                if (es.LabelCenterline)
                {
                    var ties = new List<CourseData>();
                    if (_preview.Commencement.HasValue && _canvas.Beginning.HasValue)
                        ties.Add(EasementAnnotation.Describe(Course.Line(_preview.Commencement.Value, _canvas.Beginning.Value)));
                    CourseData terminusTie = null;
                    if (_preview.TerminusCorner.HasValue && _canvas.Terminus.HasValue)
                        terminusTie = EasementAnnotation.Describe(Course.Line(_canvas.Terminus.Value, _preview.TerminusCorner.Value));

                    foreach (var label in EasementAnnotation.PlanLabels(ties, parts.SelectMany(part => part), terminusTie, es, height, Degree))
                    {
                        var lines = label.InTable ? new List<string> { label.Data.Id } : label.Lines.ToList();
                        var clearance = label.IsTie ? 0.0 : outer.Left;
                        Add(label.Data.Course, lines, clearance + height * (0.9 * lines.Count + 0.4));
                    }
                }
                else if (es.LabelMode == EasementLabelMode.Direct)
                {
                    var outward = Loops.SignedArea(boundary) > 0 ? -1.0 : 1.0;
                    foreach (var d in EasementAnnotation.Number(boundary, es))
                    {
                        var lines = d.Course.Kind == CourseKind.Line
                            ? new List<string> { EasementAnnotation.LineText(d, es, Degree) }
                            : EasementAnnotation.CurveLines(d, es, Degree).ToList();
                        Add(d.Course, lines, outward * height * (0.9 * lines.Count + 0.5));
                    }
                }
                else
                {
                    // Table: each course is tagged on the plan and listed in the table.
                    var outward = Loops.SignedArea(boundary) > 0 ? -1.0 : 1.0;
                    foreach (var d in EasementAnnotation.Number(boundary, es))
                        Add(d.Course, new List<string> { d.Id }, outward * height * 1.2);
                }
            }

            if (es.DrawWidthDimensions) BuildWidthDimension(boundary, longest, height);
        }

        /// <summary>One course label, offset from the middle of its course to the left.</summary>
        private void Add(Course c, List<string> lines, double offset)
        {
            var mid = c.PointAt(c.Length / 2.0);
            var dir = c.DirectionAt(c.Length / 2.0);
            _canvas.Labels.Add(new PreviewLabel
            {
                At = mid + dir.LeftNormal() * offset,
                Rotation = EasementCommands.Readable(Math.Atan2(dir.Y, dir.X)),
                Lines = lines
            });
        }

        /// <summary>Where the width dimension lands: a quarter of the way along, or the first
        /// place after that where a trim has not narrowed the strip -- as the command does.</summary>
        private void BuildWidthDimension(IList<Course> boundary, List<Course> centerline, double height)
        {
            var length = EasementBuilder.RouteLength(centerline);
            foreach (var fraction in new[] { 0.25, 0.5, 0.75, 0.125, 0.375, 0.625, 0.875 })
            {
                P2 direction;
                var at = EasementBuilder.PointAtStation(centerline, length * fraction, out direction);
                var normal = direction.LeftNormal();
                var left = at + normal * _preview.Width.Left;
                var right = at - normal * _preview.Width.Right;
                if (!StripTrim.OnOutline(boundary, left, _preview.Tolerance * 10) ||
                    !StripTrim.OnOutline(boundary, right, _preview.Tolerance * 10)) continue;

                _canvas.Dimension = new PreviewDimension
                {
                    Left = left, Right = right, Offset = direction * (height * 3.0),
                    Text = EasementAnnotation.Distance((_preview.Width.Left + _preview.Width.Right) / _preview.UnitsPerFoot, _preview.Settings)
                };
                return;
            }
        }

        private void Accept()
        {
            _preview.Keep = _keep.OrderBy(k => k).ToList();
            _preview.Purpose = string.IsNullOrWhiteSpace(_purpose.Text) ? _preview.Settings.DefaultPurpose : _purpose.Text.Trim().ToUpperInvariant();
            DialogResult = DialogResult.OK;
            Close();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                var on = DipBuilderForm.Dark ? 1 : 0;
                DwmSetWindowAttribute(Handle, 20, ref on, sizeof(int));
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    }

    /// <summary>One piece of drafted text in the preview, where the command would put it.</summary>
    internal sealed class PreviewLabel
    {
        public P2 At;
        public double Rotation;
        public IList<string> Lines;
    }

    /// <summary>The width dimension: across the strip, with its dimension line offset along it.</summary>
    internal sealed class PreviewDimension
    {
        public P2 Left;
        public P2 Right;

        /// <summary>How far along the easement the dimension line sits, away from the two sidelines.</summary>
        public P2 Offset;
        public string Text;
    }

    /// <summary>The trimmed strip drawn to scale: pieces, trim lines, the easement line
    /// and its angle points. North is up.</summary>
    internal sealed class TrimCanvas : Control
    {
        private readonly EasementCommands.TrimPreview _preview;
        private readonly HashSet<int> _keep;
        private double _scale = 1;
        private P2 _center;
        private int _hover;
        private Point _dragFrom;
        private bool _dragging;
        private bool _dragged;

        public event Action<int> PieceClicked;

        /// <summary>Pieces of the temporary construction easement that are kept.</summary>
        public HashSet<int> TemporaryKeep { get; set; }

        /// <summary>The drafting as it will be drawn: hatch, text and the width dimension, all
        /// at the size the drawing's annotation scale gives them.</summary>
        public string HatchPattern { get; set; }
        public string TemporaryHatchPattern { get; set; }
        public double HatchSpacing { get; set; }
        public double TextHeight { get; set; }
        public IList<PreviewLabel> Labels { get; set; }
        public PreviewLabel Title { get; set; }
        public PreviewDimension Dimension { get; set; }
        public P2? Beginning { get; set; }
        public P2? Terminus { get; set; }

        // Markup colours: the easement line red and angle points green, as on a sketch.
        private static Color RouteColor { get { return DipBuilderForm.Dark ? Color.FromArgb(240, 96, 88) : Color.FromArgb(200, 40, 32); } }
        private static Color AngleColor { get { return DipBuilderForm.Dark ? Color.FromArgb(96, 200, 120) : Color.FromArgb(24, 130, 60); } }
        private static Color TrimColor { get { return DipBuilderForm.Dark ? Color.FromArgb(176, 140, 240) : Color.FromArgb(104, 64, 170); } }

        public TrimCanvas(EasementCommands.TrimPreview preview, HashSet<int> keep)
        {
            _preview = preview;
            _keep = keep;
            DoubleBuffered = true;
            ResizeRedraw = true;
            BackColor = DipBuilderForm.Surface;
            Font = DipBuilderForm.F(9.5f, false);
            Cursor = Cursors.Hand;
            Fit();
        }

        private IEnumerable<P2> ExtentPoints()
        {
            foreach (var piece in _preview.Split.Pieces)
                foreach (var c in piece.Loop) { yield return c.Start; yield return c.PointAt(c.Length / 2); }
            foreach (var c in _preview.Route) { yield return c.Start; yield return c.End; }
            if (_preview.TemporarySplit != null)
                foreach (var piece in _preview.TemporarySplit.Pieces)
                    foreach (var c in piece.Loop) yield return c.Start;
            if (_preview.Commencement.HasValue) yield return _preview.Commencement.Value;
            if (_preview.TerminusCorner.HasValue) yield return _preview.TerminusCorner.Value;
        }

        /// <summary>The band along the bottom kept for the legend, one row per line it needs.</summary>
        private int LegendBand { get { return _legendRows * (Font.Height + 6) + 16; } }

        private int _legendRows = 1;

        private Rectangle View { get { return new Rectangle(0, 0, Width, Math.Max(1, Height - LegendBand)); } }

        /// <summary>Fits the strip in the view with a margin. Until the drafter zooms or
        /// pans, the view refits whenever the window is resized.</summary>
        private void Fit()
        {
            _userMoved = false;
            var points = ExtentPoints().ToList();
            if (points.Count == 0) return;
            double minX = points.Min(p => p.X), maxX = points.Max(p => p.X), minY = points.Min(p => p.Y), maxY = points.Max(p => p.Y);
            _center = new P2((minX + maxX) / 2, (minY + maxY) / 2);
            const int margin = 48;
            var w = Math.Max(1, View.Width - 2 * margin);
            var h = Math.Max(1, View.Height - 2 * margin);
            _scale = Math.Min(w / Math.Max(1e-6, maxX - minX), h / Math.Max(1e-6, maxY - minY));
            Invalidate();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (Width > 0 && Height > 0 && !_userMoved) Fit();
        }

        private bool _userMoved;

        private PointF ToScreen(P2 p)
        {
            return new PointF((float)(View.Width / 2.0 + (p.X - _center.X) * _scale), (float)(View.Height / 2.0 - (p.Y - _center.Y) * _scale));
        }

        private P2 World(Point p)
        {
            return new P2(_center.X + (p.X - View.Width / 2.0) / _scale, _center.Y - (p.Y - View.Height / 2.0) / _scale);
        }

        private PointF[] Path(IEnumerable<Course> courses)
        {
            var points = new List<PointF>();
            foreach (var c in courses)
            {
                var steps = c.Kind == CourseKind.Arc ? Math.Max(2, (int)Math.Ceiling(c.Sweep / (Math.PI / 90))) : 1;
                if (points.Count == 0) points.Add(ToScreen(c.Start));
                for (var i = 1; i <= steps; i++) points.Add(ToScreen(c.PointAt(c.Length * i / steps)));
            }
            return points.ToArray();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var rows = LegendRows();
            if (rows != _legendRows)
            {
                _legendRows = rows;
                if (!_userMoved) Fit();
            }
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);
            g.SetClip(View);

            // Trim lines first, running past the strip so their direction reads.
            using (var pen = new Pen(TrimColor, 2f))
                foreach (var trim in _preview.Trims)
                {
                    var path = Path(trim);
                    if (path.Length > 1) g.DrawLines(pen, path);
                }

            foreach (var piece in _preview.Split.Pieces)
            {
                var path = Path(piece.Loop);
                if (path.Length < 3) continue;
                var kept = _keep.Contains(piece.Number);
                var hover = piece.Number == _hover;
                using (var fill = new SolidBrush(Color.FromArgb(kept ? (hover ? 90 : 60) : (hover ? 60 : 0), DipBuilderForm.Accent)))
                    g.FillPolygon(fill, path);
                if (kept) PaintHatch(g, path, HatchPattern, DipBuilderForm.Ink);
                using (var pen = new Pen(kept ? DipBuilderForm.Accent : DipBuilderForm.Muted, kept ? 2.2f : 1.4f))
                {
                    if (!kept) pen.DashStyle = DashStyle.Dash;
                    g.DrawPolygon(pen, path);
                }
            }

            // The temporary construction easement: a dashed outline around the kept pieces.
            if (_preview.TemporarySplit != null && TemporaryKeep != null)
                foreach (var piece in _preview.TemporarySplit.Pieces.Where(p => TemporaryKeep.Contains(p.Number)))
                {
                    var path = Path(piece.Loop);
                    if (path.Length < 3) continue;
                    using (var fill = new SolidBrush(Color.FromArgb(25, DipBuilderForm.Accent))) g.FillPolygon(fill, path);
                    PaintHatch(g, path, TemporaryHatchPattern, DipBuilderForm.Muted);
                    using (var pen = new Pen(DipBuilderForm.Accent, 1.4f) { DashStyle = DashStyle.Dash }) g.DrawPolygon(pen, path);
                }

            // Ties: dashed lines to the Point of Commencement and the terminus corner.
            using (var tie = new Pen(DipBuilderForm.Ink, 1.2f) { DashStyle = DashStyle.Dot })
            using (var mark = new SolidBrush(DipBuilderForm.Ink))
            {
                if (_preview.Commencement.HasValue)
                {
                    var c = ToScreen(_preview.Commencement.Value);
                    if (Beginning.HasValue) g.DrawLine(tie, c, ToScreen(Beginning.Value));
                    g.FillRectangle(mark, c.X - 5, c.Y - 5, 10, 10);
                    TextRenderer.DrawText(g, "POC", Font, new Point((int)c.X + 8, (int)c.Y - Font.Height), DipBuilderForm.Ink);
                }
                if (_preview.TerminusCorner.HasValue)
                {
                    var c = ToScreen(_preview.TerminusCorner.Value);
                    if (Terminus.HasValue) g.DrawLine(tie, c, ToScreen(Terminus.Value));
                    g.FillRectangle(mark, c.X - 5, c.Y - 5, 10, 10);
                }
                if (Beginning.HasValue)
                {
                    var b = ToScreen(Beginning.Value);
                    TextRenderer.DrawText(g, "POB", Font, new Point((int)b.X + 8, (int)b.Y + 2), DipBuilderForm.Ink);
                }
                if (Terminus.HasValue)
                {
                    var end = ToScreen(Terminus.Value);
                    TextRenderer.DrawText(g, "TERMINUS", Font, new Point((int)end.X + 8, (int)end.Y + 2), DipBuilderForm.Ink);
                }
            }

            using (var pen = new Pen(RouteColor, 1.8f) { DashStyle = DashStyle.DashDot })
            {
                var route = Path(_preview.Route);
                if (route.Length > 1) g.DrawLines(pen, route);
            }
            using (var dot = new SolidBrush(AngleColor))
                foreach (var p in _preview.AnglePoints)
                {
                    var s = ToScreen(p);
                    g.FillEllipse(dot, s.X - 5, s.Y - 5, 10, 10);
                }

            PaintDrafting(g);

            // Piece numbers, only when there is a choice to make.
            if (_preview.Split.Pieces.Count > 1)
                foreach (var piece in _preview.Split.Pieces)
                {
                    // Slivers too small to see at this zoom get no badge; zoom in to pick them.
                    if (piece.Area * _scale * _scale < 900 && !_keep.Contains(piece.Number)) continue;
                    var s = ToScreen(piece.InsidePoint);
                    var kept = _keep.Contains(piece.Number);
                    var text = piece.Number.ToString();
                    var size = TextRenderer.MeasureText(text, Font);
                    var d = Math.Max(size.Width, size.Height) + 6;
                    var r = new Rectangle((int)(s.X - d / 2f), (int)(s.Y - d / 2f), d, d);
                    using (var back = new SolidBrush(kept ? DipBuilderForm.Accent : DipBuilderForm.Surface)) g.FillEllipse(back, r);
                    using (var pen = new Pen(kept ? DipBuilderForm.Accent : DipBuilderForm.Muted, 1.2f)) g.DrawEllipse(pen, r);
                    TextRenderer.DrawText(g, text, Font, r, kept ? Color.White : DipBuilderForm.Ink,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }

            DrawNorth(g);
            g.ResetClip();
            using (var band = new SolidBrush(DipBuilderForm.Surface)) g.FillRectangle(band, 0, View.Height, Width, LegendBand);
            using (var rule = new Pen(DipBuilderForm.Rule)) g.DrawLine(rule, 0, View.Height, Width, View.Height);
            DrawLegend(g);
        }

        /// <summary>
        /// The drafted text and the width dimension, drawn at the size they will really be:
        /// text too small to read here will be too small on the sheet as well.
        /// </summary>
        private void PaintDrafting(Graphics g)
        {
            if (Labels != null)
                foreach (var label in Labels) PaintLabel(g, label, DipBuilderForm.Ink);
            if (Title != null) PaintLabel(g, Title, DipBuilderForm.Ink);
            if (Dimension != null) PaintDimension(g, Dimension);
        }

        private void PaintLabel(Graphics g, PreviewLabel label, Color color)
        {
            var px = (float)(TextHeight * _scale);
            if (label == null || label.Lines == null || label.Lines.Count == 0) return;
            if (px < 3.5f) return;                       // smaller than this is a smudge; zoom in to read it

            var state = g.Save();
            var at = ToScreen(label.At);
            g.TranslateTransform(at.X, at.Y);
            g.RotateTransform((float)(-label.Rotation * 180.0 / Math.PI));
            // A CAD text height is the height of a capital; a font's em box is taller.
            using (var font = new Font(Font.FontFamily, px * 1.35f, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var brush = new SolidBrush(color))
            {
                var step = px * 1.5f;
                var y = -step * label.Lines.Count / 2f;
                foreach (var line in label.Lines)
                {
                    var size = g.MeasureString(line, font);
                    g.DrawString(line, font, brush, -size.Width / 2f, y);
                    y += step;
                }
            }
            g.Restore(state);
        }

        private void PaintDimension(Graphics g, PreviewDimension dim)
        {
            var left = ToScreen(dim.Left);
            var right = ToScreen(dim.Right);
            var a = ToScreen(dim.Left + dim.Offset);
            var b = ToScreen(dim.Right + dim.Offset);
            using (var pen = new Pen(DipBuilderForm.Ink, 1f))
            {
                g.DrawLine(pen, left, a);
                g.DrawLine(pen, right, b);
                g.DrawLine(pen, a, b);
                Arrow(g, pen, a, b);
                Arrow(g, pen, b, a);
            }
            var px = (float)(TextHeight * _scale);
            if (px < 3.5f || string.IsNullOrEmpty(dim.Text)) return;
            var middle = new P2((dim.Left.X + dim.Right.X) / 2 + dim.Offset.X,
                                (dim.Left.Y + dim.Right.Y) / 2 + dim.Offset.Y);
            var direction = dim.Right - dim.Left;
            PaintLabel(g, new PreviewLabel
            {
                At = middle + direction.Normalized().LeftNormal() * (TextHeight * 0.8),
                Rotation = EasementCommands.Readable(Math.Atan2(direction.Y, direction.X)),
                Lines = new List<string> { dim.Text }
            }, DipBuilderForm.Ink);
        }

        private static void Arrow(Graphics g, Pen pen, PointF tip, PointF from)
        {
            var dx = from.X - tip.X;
            var dy = from.Y - tip.Y;
            var length = (float)Math.Sqrt(dx * dx + dy * dy);
            if (length < 1f) return;
            dx /= length;
            dy /= length;
            const float size = 8f;
            var baseX = tip.X + dx * size;
            var baseY = tip.Y + dy * size;
            using (var brush = new SolidBrush(pen.Color))
                g.FillPolygon(brush, new[]
                {
                    tip,
                    new PointF(baseX - dy * size / 3f, baseY + dx * size / 3f),
                    new PointF(baseX + dy * size / 3f, baseY - dx * size / 3f)
                });
        }

        /// <summary>
        /// The hatch pattern inside one piece. The lines are drawn at the spacing the
        /// pattern will really have, so a hatch that will plot as a solid smudge looks
        /// like one here. Only the ANSI family's angles are known; anything else is
        /// shown as 45-degree lines rather than guessed at.
        /// </summary>
        private void PaintHatch(Graphics g, PointF[] path, string pattern, Color color)
        {
            var name = (pattern ?? string.Empty).Trim().ToUpperInvariant();
            if (name.Length == 0 || path.Length < 3) return;

            if (name == "SOLID")
            {
                using (var brush = new SolidBrush(Color.FromArgb(70, color))) g.FillPolygon(brush, path);
                return;
            }

            var spacing = (float)(HatchSpacing * _scale);
            if (spacing < 3f) spacing = 3f;              // denser than this is a smudge on screen
            var angles = name == "ANSI37" || name == "ANSI33" ? new[] { 45.0, -45.0 } : new[] { 45.0 };

            var clip = g.Clip;
            using (var shape = new GraphicsPath())
            {
                shape.AddPolygon(path);
                g.SetClip(shape, CombineMode.Intersect);
                using (var pen = new Pen(Color.FromArgb(130, color), 1f))
                    foreach (var angle in angles) HatchLines(g, pen, path, angle, spacing);
                g.Clip = clip;
            }
            clip.Dispose();
        }

        private static void HatchLines(Graphics g, Pen pen, PointF[] path, double degrees, float spacing)
        {
            float minX = path.Min(q => q.X), maxX = path.Max(q => q.X);
            float minY = path.Min(q => q.Y), maxY = path.Max(q => q.Y);
            var cx = (minX + maxX) / 2f;
            var cy = (minY + maxY) / 2f;
            var reach = (float)Math.Sqrt((maxX - minX) * (maxX - minX) + (maxY - minY) * (maxY - minY)) / 2f + spacing;

            var radians = degrees * Math.PI / 180.0;
            var dx = (float)Math.Cos(radians);
            var dy = (float)-Math.Sin(radians);
            var nx = -dy;
            var ny = dx;

            var steps = (int)Math.Ceiling(reach / spacing);
            if (steps > 400) return;                     // far too dense to be worth drawing
            for (var i = -steps; i <= steps; i++)
            {
                var ox = cx + nx * i * spacing;
                var oy = cy + ny * i * spacing;
                g.DrawLine(pen, ox - dx * reach, oy - dy * reach, ox + dx * reach, oy + dy * reach);
            }
        }

        private void DrawNorth(Graphics g)
        {
            var cx = Width - 26f;
            const float top = 14f;
            using (var ink = new SolidBrush(DipBuilderForm.Ink))
            using (var surface = new SolidBrush(DipBuilderForm.Surface))
            using (var pen = new Pen(DipBuilderForm.Ink, 1.2f))
            {
                var tip = new PointF(cx, top);
                var notch = new PointF(cx, top + 13);
                g.FillPolygon(ink, new[] { tip, new PointF(cx - 7, top + 18), notch });
                g.FillPolygon(surface, new[] { tip, new PointF(cx + 7, top + 18), notch });
                g.DrawPolygon(pen, new[] { tip, new PointF(cx + 7, top + 18), notch, new PointF(cx - 7, top + 18) });
            }
            var size = TextRenderer.MeasureText("N", Font);
            TextRenderer.DrawText(g, "N", Font, new Point((int)cx - size.Width / 2, (int)top + 20), DipBuilderForm.Ink);
        }

        /// <summary>What the legend explains: how to draw each mark, and what it is called.</summary>
        private List<KeyValuePair<Action<Graphics, int, int>, string>> LegendItems()
        {
            var items = new List<KeyValuePair<Action<Graphics, int, int>, string>>();
            Action<Action<Graphics, int, int>, string> add = (mark, text) =>
                items.Add(new KeyValuePair<Action<Graphics, int, int>, string>(mark, text));

            add((g, x, y) => { using (var p = new Pen(RouteColor, 1.8f) { DashStyle = DashStyle.DashDot }) g.DrawLine(p, x, y, x + 22, y); }, "easement line");
            add((g, x, y) => { using (var b = new SolidBrush(AngleColor)) g.FillEllipse(b, x + 6, y - 5, 10, 10); }, "angle points");
            add((g, x, y) => { using (var p = new Pen(TrimColor, 2f)) g.DrawLine(p, x, y, x + 22, y); }, "trim lines");
            add((g, x, y) => { using (var b = new SolidBrush(Color.FromArgb(110, DipBuilderForm.Accent))) g.FillRectangle(b, x + 2, y - 7, 18, 14); }, "kept");
            add((g, x, y) => { using (var p = new Pen(DipBuilderForm.Muted, 1.4f) { DashStyle = DashStyle.Dash }) g.DrawRectangle(p, x + 2, y - 7, 18, 14); }, "left out");
            if (_preview.TemporarySplit != null)
                add((g, x, y) => { using (var p = new Pen(DipBuilderForm.Accent, 1.4f) { DashStyle = DashStyle.Dash }) g.DrawRectangle(p, x + 2, y - 7, 18, 14); }, "temporary");
            return items;
        }

        /// <summary>How many rows the legend needs at this width. A narrow window wraps it
        /// rather than dropping what the marks mean.</summary>
        private int LegendRows()
        {
            var rows = 1;
            var x = 16;
            foreach (var item in LegendItems())
            {
                var needed = 28 + TextRenderer.MeasureText(item.Value, Font).Width + 18;
                if (x > 16 && x + needed > Width - 8) { rows++; x = 16; }
                x += needed;
            }
            return rows;
        }

        private void DrawLegend(Graphics g)
        {
            var row = 0;
            var x = 16;
            foreach (var item in LegendItems())
            {
                var needed = 28 + TextRenderer.MeasureText(item.Value, Font).Width + 18;
                if (x > 16 && x + needed > Width - 8) { row++; x = 16; }
                var y = View.Height + 10 + row * (Font.Height + 6) + Font.Height / 2;
                item.Key(g, x, y);
                TextRenderer.DrawText(g, item.Value, Font, new Point(x + 28, y - Font.Height / 2), DipBuilderForm.Muted);
                x += needed;
            }
        }

        private int PieceAt(Point p)
        {
            if (p.Y >= View.Height) return 0;
            var piece = _preview.Split.PieceAt(World(p));
            return piece == null ? 0 : piece.Number;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            _dragFrom = e.Location;
            _dragging = true;
            _dragged = false;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragging && e.Button != MouseButtons.None)
            {
                var dx = e.X - _dragFrom.X;
                var dy = e.Y - _dragFrom.Y;
                if (_dragged || Math.Abs(dx) + Math.Abs(dy) > 4)
                {
                    _dragged = true;
                    _userMoved = true;
                    _center = new P2(_center.X - dx / _scale, _center.Y + dy / _scale);
                    _dragFrom = e.Location;
                    Cursor = Cursors.SizeAll;
                    Invalidate();
                }
                return;
            }
            var hover = PieceAt(e.Location);
            if (hover != _hover) { _hover = hover; Invalidate(); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _dragging = false;
            Cursor = Cursors.Hand;
            if (_dragged || e.Button != MouseButtons.Left) return;
            var number = PieceAt(e.Location);
            if (number > 0 && _preview.Split.Pieces.Count > 1 && PieceClicked != null) PieceClicked(number);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hover != 0) { _hover = 0; Invalidate(); }
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            Fit();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            _userMoved = true;
            var before = World(e.Location);
            _scale *= e.Delta > 0 ? 1.25 : 0.8;
            var after = World(e.Location);
            _center = new P2(_center.X + before.X - after.X, _center.Y + before.Y - after.Y);
            Invalidate();
        }
    }
}
