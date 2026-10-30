using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using FieldCodes.Easements;
using FieldCodes.Lots;
using FieldCodes.Settings;

namespace FieldCodes.Cad.Ui
{
    /// <summary>
    /// The FTFLOTS popup. The block's corners are already picked; this asks how to divide it: which
    /// side the lots front, how many and how wide, how deep, whether an alley runs behind or between
    /// two rows, where each lot drains, the building setbacks and utility easements, and the minimums
    /// the plat is checked against.
    ///
    /// It reads and writes a <see cref="LotJob"/> and does no geometry. Everything typed is parsed
    /// strictly and a bad entry is reported, never guessed at.
    ///
    /// Driven in Civil 3D by tests\LiveSmoke\rowlots-ui\run-rowlots-ui.ps1: the equal division,
    /// the second row appearing and going again, the alley, a per-lot drainage override, and the
    /// layout at 100%, 125% and 150% text.
    /// </summary>
    internal sealed class LotForm : Form
    {
        private static readonly string[] DrainNames =
        {
            "Block default", "Toward the front", "Toward the rear", "Toward the left", "Toward the right", "Both ways", "No arrow"
        };

        private static readonly LotDrain?[] DrainValues =
        {
            null, LotDrain.ToFront, LotDrain.ToRear, LotDrain.ToLeft, LotDrain.ToRight, LotDrain.FrontAndRear, LotDrain.None
        };

        private static readonly string[] CloseNames = { "Refuse", "First lot takes it", "Last lot takes it" };
        private static readonly string[] AlleyNames = { "No alley", "Along the back", "Between two rows" };

        private readonly LotJob _job;
        private readonly IList<string> _corners;

        private TextBox _block;
        private TextBox _first;
        private RowEditor _a;
        private RowEditor _b;
        private CheckBox _useB;
        private ComboBox _alley;
        private TextBox _alleyWidth;
        private ComboBox _backSide;
        private ComboBox _defaultDrain;
        private TextBox _slope;
        private TextBox _frontSetback, _sideSetback, _cornerSetback, _rearSetback;
        private TextBox _frontEasement, _sideEasement, _rearEasement;
        private TextBox _minArea, _minWidth;
        private DataGridView _radii;
        private Label _errors;
        private Panel _body;

        public LotForm(LotJob job, LotSettings settings, IList<string> cornerLabels)
        {
            _job = job;
            _corners = cornerLabels;

            DipBuilderForm.UseTheme(ThemePreference.LoadDark());
            Text = "Platted Block";
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = false;
            MinimizeBox = false;
            BackColor = DipBuilderForm.Ground;
            ForeColor = DipBuilderForm.Ink;
            Font = DipBuilderForm.F(10.5f, false);
            var area = Screen.FromPoint(Cursor.Position).WorkingArea;
            ClientSize = new Size(Math.Min(1000, area.Width - 60), Math.Min(860, area.Height - 60));
            MinimumSize = new Size(Math.Min(780, area.Width), Math.Min(520, area.Height));

            var header = new Panel { Dock = DockStyle.Top, BackColor = DipBuilderForm.Surface, Padding = new Padding(20, 12, 20, 10) };
            var heading = new Label { AutoSize = true, Text = "Divide the block into lots", Font = DipBuilderForm.F(15f, true), ForeColor = DipBuilderForm.Ink };
            // Folds inside the window rather than running off its edge -- see the same note in
            // RowForm: the header's height is measured from this label.
            var sub = new Label
            {
                AutoSize = true, ForeColor = DipBuilderForm.Muted, Margin = new Padding(1, 4, 0, 0),
                MaximumSize = new Size(ClientSize.Width - 40, 0),
                Text = "Lot lines are cut square to the side the lots front. Frontages must add up to that side."
            };
            var headerText = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Dock = DockStyle.Fill };
            headerText.Controls.Add(heading);
            headerText.Controls.Add(sub);
            header.Controls.Add(headerText);
            header.Height = heading.PreferredHeight + sub.PreferredHeight + header.Padding.Vertical + 8;

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(18, 8, 20, 12), BackColor = DipBuilderForm.Ground };
            var ok = Btn("Draw", true);
            ok.Click += (s, e) => { if (Collect()) { DialogResult = DialogResult.OK; Close(); } };
            var cancel = Btn("Cancel", false);
            cancel.DialogResult = DialogResult.Cancel;
            buttons.Controls.Add(ok);
            buttons.Controls.Add(cancel);
            CancelButton = cancel;

            _errors = new Label
            {
                Dock = DockStyle.Bottom, AutoSize = true, MaximumSize = new Size(1000, 0), ForeColor = DipBuilderForm.Bad,
                Font = DipBuilderForm.F(9.5f, false), Padding = new Padding(18, 6, 20, 0)
            };

            var stack = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true,
                Padding = new Padding(18, 12, 20, 8), BackColor = DipBuilderForm.Ground
            };

            _block = Box(160, job.BlockName);
            _first = Box(70, job.FirstLotNumber.ToString(CultureInfo.InvariantCulture));
            stack.Controls.Add(Heading("BLOCK"));
            stack.Controls.Add(Row(Field("BLOCK NAME OR LETTER", _block), Field("FIRST LOT NUMBER", _first)));

            var sides = SideNames();
            _a = new RowEditor(sides, _job, settings, 0);
            stack.Controls.Add(Heading("LOTS"));
            stack.Controls.Add(_a);

            _useB = new CheckBox { Text = "A second row of lots on another side of the block", AutoSize = true, ForeColor = DipBuilderForm.Ink, Margin = new Padding(0, 10, 0, 4) };
            _b = new RowEditor(sides, _job, settings, 1);
            _b.Visible = false;
            _useB.CheckedChanged += (s, e) => { _b.Visible = _useB.Checked; };
            stack.Controls.Add(_useB);
            stack.Controls.Add(_b);

            _alley = Combo(200, AlleyNames);
            _alley.SelectedIndex = 0;
            _alleyWidth = Box(80, settings.DefaultAlleyWidthFeet > 0 ? LotLabels.Feet(settings.DefaultAlleyWidthFeet) : string.Empty);
            _backSide = Combo(280, new[] { "Furthest from the front" }.Concat(sides).ToArray());
            _backSide.SelectedIndex = 0;
            stack.Controls.Add(Heading("ALLEY"));
            stack.Controls.Add(Row(Field("ALLEY", _alley), Field("WIDTH (FT)", _alleyWidth), Field("RUNS ALONG (REAR ALLEY)", _backSide)));

            _defaultDrain = Combo(200, DrainNames.Skip(1).ToArray());
            _defaultDrain.SelectedIndex = 0;
            _slope = Box(70, LotLabels.Feet(settings.DefaultSlopePercent));
            stack.Controls.Add(Heading("DRAINAGE"));
            stack.Controls.Add(Row(Field("EVERY LOT DRAINS", _defaultDrain), Field("SLOPE (%)", _slope)));
            stack.Controls.Add(Hint("Left and right are as seen standing on the front line looking into the lot. Set a single lot differently in the lot table."));

            _frontSetback = Box(70, Feet(settings.DefaultFrontSetbackFeet));
            _sideSetback = Box(70, Feet(settings.DefaultSideSetbackFeet));
            _cornerSetback = Box(70, Feet(settings.DefaultCornerSideSetbackFeet));
            _rearSetback = Box(70, Feet(settings.DefaultRearSetbackFeet));
            _frontEasement = Box(70, Feet(settings.DefaultFrontEasementFeet));
            _sideEasement = Box(70, Feet(settings.DefaultSideEasementFeet));
            _rearEasement = Box(70, Feet(settings.DefaultRearEasementFeet));
            stack.Controls.Add(Heading("BUILDING SETBACKS (FT)"));
            stack.Controls.Add(Row(Field("FRONT", _frontSetback), Field("SIDE", _sideSetback), Field("CORNER LOT STREET SIDE", _cornerSetback), Field("REAR", _rearSetback)));
            stack.Controls.Add(Heading("UTILITY EASEMENTS (FT)"));
            stack.Controls.Add(Row(Field("FRONT", _frontEasement), Field("SIDE", _sideEasement), Field("REAR", _rearEasement)));
            stack.Controls.Add(Hint("Leave a distance at 0 to draw nothing for it."));

            _minArea = Box(90, Feet(settings.MinimumLotAreaSqFt));
            _minWidth = Box(90, Feet(settings.MinimumLotWidthFeet));
            stack.Controls.Add(Heading("CHECKS"));
            stack.Controls.Add(Row(Field("MINIMUM LOT AREA (SQ FT)", _minArea), Field("MINIMUM FRONTAGE (FT)", _minWidth)));

            _radii = NewGrid();
            _radii.Height = 28 + 26 * Math.Min(job.Corners.Count, 8);
            _radii.Width = 420;
            _radii.Columns.Add(Col("corner", "BLOCK CORNER", 220, true));
            _radii.Columns.Add(Col("radius", "CORNER RADIUS (FT)", 180, false));
            for (var i = 0; i < job.Corners.Count; i++)
                _radii.Rows.Add(_corners[i], i < job.CornerRadiiFeet.Count && job.CornerRadiiFeet[i] > 0 ? LotLabels.Feet(job.CornerRadiiFeet[i]) : string.Empty);
            stack.Controls.Add(Heading("ROUNDED CORNERS"));
            stack.Controls.Add(Hint("A radius rounds that block corner on the lot that has it. Leave blank for a sharp corner."));
            stack.Controls.Add(_radii);

            Controls.Add(stack);
            Controls.Add(_errors);
            Controls.Add(buttons);
            Controls.Add(header);

            // The lot fields fold into the body rather than running off its edge, and follow it
            // as the window is resized.
            _body = stack;
            Load += (s, e) => FitRows();
            stack.ClientSizeChanged += (s, e) => FitRows();
        }

        private void FitRows()
        {
            if (_body == null) return;
            var width = _body.ClientSize.Width - _body.Padding.Horizontal;
            _a.FitTo(width);
            _b.FitTo(width);
        }

        private string[] SideNames()
        {
            var n = _job.Corners.Count;
            var names = new string[n];
            for (var i = 0; i < n; i++)
            {
                var length = _job.Corners[i].DistanceTo(_job.Corners[(i + 1) % n]) / _job.UnitsPerFoot;
                names[i] = string.Format(CultureInfo.InvariantCulture, "Side {0}: {1} to {2}, {3:0.00}'",
                    i + 1, _corners[i], _corners[(i + 1) % n], length);
            }
            return names;
        }

        private static string Feet(double value)
        {
            return value > 0 ? LotLabels.Feet(value) : "0";
        }

        // ---------------------------------------------------------------- reading

        private bool Collect()
        {
            var problems = new List<string>();
            _job.BlockName = _block.Text.Trim();
            _job.FirstLotNumber = (int)Number(_first.Text, "The first lot number", 1, problems);

            _job.RowA = _a.Read(problems, "The first row");
            _job.RowB = null;
            if (_useB.Checked) _job.RowB = _b.Read(problems, "The second row");

            switch (_alley.SelectedIndex)
            {
                case 1: _job.Alley = LotAlleyMode.Rear; break;
                case 2: _job.Alley = LotAlleyMode.MidBlock; break;
                default: _job.Alley = LotAlleyMode.None; break;
            }
            _job.AlleyWidthFeet = _job.Alley == LotAlleyMode.None ? 0 : Number(_alleyWidth.Text, "The alley width", _job.Alley == LotAlleyMode.Rear ? 1e-9 : 0, problems);
            _job.BackSide = _backSide.SelectedIndex - 1;

            _job.DefaultDrain = DrainValues[_defaultDrain.SelectedIndex + 1].Value;
            _job.SlopePercent = Number(_slope.Text, "The slope", 0, problems);

            _job.FrontSetbackFeet = Number(_frontSetback.Text, "The front setback", 0, problems);
            _job.SideSetbackFeet = Number(_sideSetback.Text, "The side setback", 0, problems);
            _job.CornerSideSetbackFeet = Number(_cornerSetback.Text, "The corner lot street-side setback", 0, problems);
            _job.RearSetbackFeet = Number(_rearSetback.Text, "The rear setback", 0, problems);
            _job.FrontEasementFeet = Number(_frontEasement.Text, "The front easement", 0, problems);
            _job.SideEasementFeet = Number(_sideEasement.Text, "The side easement", 0, problems);
            _job.RearEasementFeet = Number(_rearEasement.Text, "The rear easement", 0, problems);
            _job.MinLotAreaSqFt = Number(_minArea.Text, "The minimum lot area", 0, problems);
            _job.MinLotWidthFeet = Number(_minWidth.Text, "The minimum frontage", 0, problems);

            _job.CornerRadiiFeet.Clear();
            for (var i = 0; i < _job.Corners.Count; i++)
            {
                var text = Convert.ToString(_radii.Rows[i].Cells["radius"].Value, CultureInfo.InvariantCulture);
                _job.CornerRadiiFeet.Add(string.IsNullOrWhiteSpace(text) ? 0.0 : Number(text, "The radius at " + _corners[i], 0, problems));
            }

            // Per-lot drainage: lots are numbered on from the first lot across both rows.
            _job.DrainOverrides.Clear();
            var number = _job.FirstLotNumber;
            foreach (var editor in new[] { _a, _useB.Checked ? _b : null })
            {
                if (editor == null) continue;
                foreach (var drain in editor.Drains())
                {
                    if (drain.HasValue) _job.DrainOverrides[number] = drain.Value;
                    number++;
                }
            }

            if (problems.Count > 0)
            {
                _errors.Text = string.Join("\n", problems.ToArray());
                return false;
            }

            _errors.Text = string.Empty;
            return true;
        }

        /// <summary>A number at or above the minimum; below it, or not a number, is reported.</summary>
        internal static double Number(string text, string what, double minimum, ICollection<string> problems)
        {
            double value;
            if (!double.TryParse((text ?? string.Empty).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                problems.Add(what + " is not a number.");
                return 0;
            }
            if (value < minimum)
            {
                problems.Add(what + (minimum > 0 ? " must be at least " + minimum.ToString("0.##", CultureInfo.InvariantCulture) + "." : " cannot be negative."));
                return 0;
            }
            return value;
        }

        // ---------------------------------------------------------------- one row

        /// <summary>One row of lots: the side it fronts, how many lots and how wide, the depth, and
        /// a table to adjust each lot's frontage and where it drains.</summary>
        private sealed class RowEditor : Panel
        {
            private readonly LotJob _job;
            private readonly ComboBox _side;
            private readonly TextBox _count;
            private readonly TextBox _extra;
            private readonly TextBox _depth;
            private readonly ComboBox _close;
            private readonly DataGridView _lots;
            private readonly FlowLayoutPanel _fields;

            public RowEditor(string[] sides, LotJob job, LotSettings settings, int rowIndex)
            {
                _job = job;
                AutoSize = true;
                Margin = new Padding(0, 0, 0, 8);
                BackColor = DipBuilderForm.Ground;

                _side = Combo(420, sides);
                _side.SelectedIndex = Math.Min(rowIndex == 0 ? 0 : 2, sides.Length - 1);
                _count = Box(60, "4");
                _extra = Box(70, "0");
                _depth = Box(80, string.Empty);
                _close = Combo(160, CloseNames);
                _close.SelectedIndex = 0;

                var divide = Btn("Divide equally", false);
                divide.Margin = new Padding(0, 16, 18, 8);
                divide.Click += (s, e) => Divide();
                _side.SelectedIndexChanged += (s, e) => Divide();

                _lots = NewGrid();
                _lots.Width = 460;
                _lots.Height = 200;
                _lots.Columns.Add(Col("lot", "LOT", 70, true));
                _lots.Columns.Add(Col("width", "FRONTAGE (FT)", 150, false));
                var drain = new DataGridViewComboBoxColumn { Name = "drain", HeaderText = "DRAINS", Width = 200, FlatStyle = FlatStyle.Flat, SortMode = DataGridViewColumnSortMode.NotSortable };
                drain.Items.AddRange(DrainNames);
                _lots.Columns.Add(drain);

                var fields = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, WrapContents = true, Dock = DockStyle.Top };
                _fields = fields;
                fields.Controls.Add(Field("LOTS FRONT", _side));
                fields.Controls.Add(Field("HOW MANY", _count));
                fields.Controls.Add(Field("EXTRA ON CORNER LOTS (FT)", _extra));
                fields.Controls.Add(divide);
                fields.Controls.Add(Field("LOT DEPTH (FT, BLANK = TO THE BACK)", _depth));
                fields.Controls.Add(Field("IF THE WIDTHS MISS THE SIDE", _close));

                var host = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
                host.Controls.Add(fields);
                host.Controls.Add(_lots);
                Controls.Add(host);

                Divide();
            }

            private double SideFeet()
            {
                var n = _job.Corners.Count;
                var i = _side.SelectedIndex;
                return _job.Corners[i].DistanceTo(_job.Corners[(i + 1) % n]) / _job.UnitsPerFoot;
            }

            private void Divide()
            {
                int count;
                double extra;
                if (!int.TryParse(_count.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out count) || count < 1) return;
                if (!double.TryParse(_extra.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out extra)) extra = 0;

                var widths = LotRow.EqualWidths(SideFeet(), count, extra);
                _lots.Rows.Clear();
                for (var i = 0; i < widths.Count; i++)
                    _lots.Rows.Add((i + 1).ToString(CultureInfo.InvariantCulture), widths[i].ToString("0.####", CultureInfo.InvariantCulture), DrainNames[0]);
            }

            public IEnumerable<LotDrain?> Drains()
            {
                foreach (DataGridViewRow row in _lots.Rows)
                {
                    var name = Convert.ToString(row.Cells["drain"].Value, CultureInfo.InvariantCulture);
                    var index = Array.IndexOf(DrainNames, name);
                    yield return index <= 0 ? null : DrainValues[index];
                }
            }

            /// <summary>
            /// Folds the row of fields into <paramref name="width"/> instead of letting it run
            /// past the window's edge. A FlowLayoutPanel only wraps when something fixes its
            /// width, and every container between here and the window sizes itself to what it
            /// holds -- so without this the row grows and the body scrolls sideways instead.
            /// Setting the same width twice is ignored, so this cannot chase a scrollbar that
            /// appears and disappears.
            /// </summary>
            public void FitTo(int width)
            {
                if (width < 200 || _fields.MaximumSize.Width == width) return;
                _fields.MaximumSize = new Size(width, 0);
                _fields.MinimumSize = new Size(width, 0);

                // The row folds once its own width is fixed, but a wrapping flow panel still
                // reports the width it would want unwrapped, and the body sizes its scrollbars
                // from what its children say they want. Fix this editor's width too, so what it
                // reports is what it is.
                MaximumSize = new Size(width, 0);
                MinimumSize = new Size(width, 0);
            }

            public LotRow Read(ICollection<string> problems, string name)
            {
                var row = new LotRow { FrontSide = _side.SelectedIndex };
                for (var i = 0; i < _lots.Rows.Count; i++)
                    row.WidthsFeet.Add(Number(Convert.ToString(_lots.Rows[i].Cells["width"].Value, CultureInfo.InvariantCulture),
                                              name + ", lot " + (i + 1) + " frontage", 1e-9, problems));
                if (_depth.Text.Trim().Length > 0) row.DepthFeet = Number(_depth.Text, name + ": the lot depth", 1e-9, problems);
                row.Close = _close.SelectedIndex == 1 ? LotWidthClose.FirstLot : _close.SelectedIndex == 2 ? LotWidthClose.LastLot : LotWidthClose.Refuse;
                return row;
            }
        }

        // ---------------------------------------------------------------- controls

        private static TextBox Box(int width, string text)
        {
            return new TextBox
            {
                Width = width, Text = text ?? string.Empty, BorderStyle = BorderStyle.FixedSingle,
                BackColor = DipBuilderForm.Dark ? DipBuilderForm.Calculated : Color.White, ForeColor = DipBuilderForm.Ink,
                Font = DipBuilderForm.F(10f, false)
            };
        }

        private static ComboBox Combo(int width, string[] items)
        {
            var combo = new ComboBox { Width = width, DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, Font = DipBuilderForm.F(10f, false) };
            combo.Items.AddRange(items);
            return combo;
        }

        private static Control Field(string caption, Control input)
        {
            var host = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 18, 8) };
            host.Controls.Add(new Label
            {
                AutoSize = true, Text = caption, ForeColor = DipBuilderForm.Muted, Font = DipBuilderForm.F(8.5f, false), Margin = new Padding(0, 0, 0, 2)
            });
            host.Controls.Add(input);
            return host;
        }

        private static Control Row(params Control[] fields)
        {
            var row = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, WrapContents = true };
            foreach (var field in fields) row.Controls.Add(field);
            return row;
        }

        private static Label Heading(string text)
        {
            return new Label
            {
                AutoSize = true, Text = text, ForeColor = DipBuilderForm.Muted, Font = DipBuilderForm.F(8.5f, false),
                Margin = new Padding(0, 12, 0, 4)
            };
        }

        private static Label Hint(string text)
        {
            return new Label
            {
                AutoSize = true, MaximumSize = new Size(820, 0), Text = text, ForeColor = DipBuilderForm.Muted,
                Font = DipBuilderForm.F(8.5f, false), Margin = new Padding(0, 0, 0, 6)
            };
        }

        private static DataGridView NewGrid()
        {
            var grid = new DataGridView
            {
                AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false,
                RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.CellSelect, EditMode = DataGridViewEditMode.EditOnEnter,
                BorderStyle = BorderStyle.FixedSingle, EnableHeadersVisualStyles = false, BackgroundColor = DipBuilderForm.Surface,
                GridColor = DipBuilderForm.Rule, ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
                AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells
            };
            grid.DefaultCellStyle.BackColor = DipBuilderForm.Surface;
            grid.DefaultCellStyle.ForeColor = DipBuilderForm.Ink;
            grid.DefaultCellStyle.SelectionBackColor = DipBuilderForm.AccentSoft;
            grid.DefaultCellStyle.SelectionForeColor = DipBuilderForm.Ink;
            grid.ColumnHeadersDefaultCellStyle.BackColor = DipBuilderForm.Ground;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = DipBuilderForm.Muted;
            grid.ColumnHeadersDefaultCellStyle.Font = DipBuilderForm.F(8.5f, false);
            return grid;
        }

        private static DataGridViewTextBoxColumn Col(string name, string header, int width, bool readOnly)
        {
            return new DataGridViewTextBoxColumn { Name = name, HeaderText = header, Width = width, ReadOnly = readOnly, SortMode = DataGridViewColumnSortMode.NotSortable };
        }

        private static Button Btn(string text, bool primary)
        {
            var b = new Button
            {
                Text = text, AutoSize = true, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand,
                BackColor = primary ? DipBuilderForm.Accent : DipBuilderForm.Surface, ForeColor = primary ? Color.White : DipBuilderForm.Ink,
                Padding = new Padding(14, 4, 14, 4), Margin = new Padding(8, 0, 0, 0)
            };
            b.FlatAppearance.BorderColor = DipBuilderForm.ButtonBorder;
            return b;
        }
    }
}
