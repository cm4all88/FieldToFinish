using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using FieldCodes.Drafting;
using FieldCodes.Row;
using FieldCodes.Settings;

namespace FieldCodes.Cad.Ui
{
    /// <summary>
    /// The FTFROW popup. The drafter has already picked the monuments; this asks for what
    /// the survey does not know: the road, its right-of-way width, which side the road is
    /// on, what each monument is, and whether it is an offset monument. A block or an
    /// intersection also asks what each side is: a right-of-way frontage, a line across a
    /// road, or neither.
    ///
    /// The window reads and writes a <see cref="RowJob"/>; it does no geometry. Everything
    /// typed is parsed strictly and a bad entry is reported, never guessed at.
    /// </summary>
    internal sealed class RowForm : Form
    {
        private static string _lastRoad = string.Empty;
        private static string _lastDescription = string.Empty;

        private readonly RowJob _job;
        private readonly RightOfWaySettings _settings;

        private TextBox _road;
        private TextBox _width;
        private ComboBox _reference;
        private ComboBox _side;
        private TextBox _left;
        private TextBox _right;
        private CheckBox _centerline;
        private CheckBox _every;
        private DataGridView _monuments;
        private DataGridView _sides;
        private Label _errors;

        private const string OnRowLine = "On the right-of-way line";
        private const string OnCenterline = "On the road centerline";
        private const string RoadLeft = "Road is to the LEFT";
        private const string RoadRight = "Road is to the RIGHT";

        private static readonly string[] Kinds = { "Found", "Set" };
        private static readonly string[] FaceKinds = { "Frontage", "Cross", "Plain" };

        public RowForm(RowJob job, RightOfWaySettings settings)
        {
            _job = job;
            _settings = settings;

            DipBuilderForm.UseTheme(ThemePreference.LoadDark());
            Text = "Right of Way";
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = false;
            MinimizeBox = false;
            BackColor = DipBuilderForm.Ground;
            ForeColor = DipBuilderForm.Ink;
            Font = DipBuilderForm.F(10.5f, false);
            var area = Screen.FromPoint(Cursor.Position).WorkingArea;
            ClientSize = new Size(Math.Min(1120, area.Width - 60), Math.Min(job.IsRing ? 760 : 560, area.Height - 60));
            MinimumSize = new Size(Math.Min(860, area.Width), Math.Min(460, area.Height));

            var title = job.Mode == RowMode.Run ? "Right-of-way run" : job.Mode == RowMode.Block ? "Platted block" : "Intersection";
            var guide = job.Mode == RowMode.Run
                ? "Name the road and give its right-of-way width. The opposite right-of-way line and the centerline are offset to the road side."
                : job.Mode == RowMode.Block
                    ? "Say what each side of the block fronts. A frontage is offset by its road's right-of-way width."
                    : "Each side of the ring crosses a road. Its right-of-way width is checked against the distance between the monuments.";
            var header = new Panel { Dock = DockStyle.Top, BackColor = DipBuilderForm.Surface, Padding = new Padding(20, 12, 20, 10) };
            var heading = new Label { AutoSize = true, Text = title, Font = DipBuilderForm.F(15f, true), ForeColor = DipBuilderForm.Ink };
            var sub = new Label { AutoSize = true, ForeColor = DipBuilderForm.Muted, Margin = new Padding(1, 4, 0, 0), Text = guide };
            var headerText = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Dock = DockStyle.Fill };
            headerText.Controls.Add(heading);
            headerText.Controls.Add(sub);
            header.Controls.Add(headerText);
            header.Height = heading.PreferredHeight + sub.PreferredHeight + header.Padding.Vertical + 8;

            var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(18, 14, 20, 8), BackColor = DipBuilderForm.Ground };

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(18, 8, 20, 12), BackColor = DipBuilderForm.Ground };
            var ok = Btn("Draw", true);
            ok.Click += (s, e) => { if (Collect()) { DialogResult = DialogResult.OK; Close(); } };
            var cancel = Btn("Cancel", false);
            cancel.DialogResult = DialogResult.Cancel;
            buttons.Controls.Add(ok);
            buttons.Controls.Add(cancel);
            CancelButton = cancel;
            AcceptButton = null;

            _errors = new Label
            {
                Dock = DockStyle.Bottom, AutoSize = true, MaximumSize = new Size(1000, 0), ForeColor = DipBuilderForm.Bad,
                Font = DipBuilderForm.F(9.5f, false), Padding = new Padding(18, 6, 20, 0)
            };

            BuildMonumentGrid();
            var gridHost = new Panel { Dock = DockStyle.Fill };
            if (job.IsRing)
            {
                BuildSideGrid();
                var split = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
                split.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                split.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
                split.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                split.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
                split.Controls.Add(Caption("MONUMENTS, IN THE ORDER PICKED"), 0, 0);
                split.Controls.Add(_monuments, 0, 1);
                split.Controls.Add(Caption("SIDES: EACH RUNS FROM ONE MONUMENT TO THE NEXT"), 0, 2);
                split.Controls.Add(_sides, 0, 3);
                gridHost.Controls.Add(split);
            }
            else
            {
                gridHost.Controls.Add(_monuments);
                gridHost.Controls.Add(Caption("MONUMENTS, IN THE ORDER PICKED"));
            }

            body.Controls.Add(gridHost);
            if (!job.IsRing) body.Controls.Add(BuildRunFields());
            body.Controls.Add(BuildOptions());

            Controls.Add(body);
            Controls.Add(_errors);
            Controls.Add(buttons);
            Controls.Add(header);
            ApplyReference();
        }

        // -------------------------------------------------------------------- run

        private Control BuildRunFields()
        {
            var panel = new FlowLayoutPanel
            {
                Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = true,
                Padding = new Padding(0, 0, 0, 12)
            };

            _road = Box(240, string.IsNullOrEmpty(_job.RoadName) ? _lastRoad : _job.RoadName);
            _width = Box(90, _job.WidthFeet > 0 ? _job.WidthFeet.ToString("0.##", CultureInfo.InvariantCulture) : string.Empty);
            _reference = Combo(220, new[] { OnRowLine, OnCenterline });
            _reference.SelectedItem = _job.Reference == RowReference.Centerline ? OnCenterline : OnRowLine;
            _reference.SelectedIndexChanged += (s, e) => ApplyReference();
            _side = Combo(160, new[] { RoadLeft, RoadRight });
            _side.SelectedItem = _job.RoadSide == RowRoadSide.Left ? RoadLeft : RoadRight;
            _left = Box(70, string.Empty);
            _right = Box(70, string.Empty);

            panel.Controls.Add(Field("ROAD NAME", _road));
            panel.Controls.Add(Field("RIGHT-OF-WAY WIDTH (FT)", _width));
            panel.Controls.Add(Field("MONUMENTS ARE", _reference));
            panel.Controls.Add(Field("WHICH SIDE", _side));
            panel.Controls.Add(Field("LEFT OF CENTERLINE (FT)", _left));
            panel.Controls.Add(Field("RIGHT OF CENTERLINE (FT)", _right));
            return panel;
        }

        private void ApplyReference()
        {
            if (_reference == null) return;
            var centre = Convert.ToString(_reference.SelectedItem) == OnCenterline;
            _width.Parent.Visible = !centre;
            _side.Parent.Visible = !centre;
            _left.Parent.Visible = centre;
            _right.Parent.Visible = centre;
        }

        private Control BuildOptions()
        {
            var panel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = true,
                Padding = new Padding(0, 10, 0, 0)
            };
            _centerline = new CheckBox { Text = "Draw the road centerline", AutoSize = true, Checked = _job.DrawCenterline, ForeColor = DipBuilderForm.Ink, Margin = new Padding(0, 0, 24, 0) };
            _every = new CheckBox
            {
                Text = "Dimension the width at every monument", AutoSize = true, Checked = _job.EveryMonumentDimension, ForeColor = DipBuilderForm.Ink
            };
            panel.Controls.Add(_centerline);
            if (_job.Mode != RowMode.Intersection) panel.Controls.Add(_every);
            return panel;
        }

        // ------------------------------------------------------------------ grids

        private void BuildMonumentGrid()
        {
            _monuments = NewGrid();
            _monuments.Columns.Add(TextCol("pt", "POINT", 70, true));
            _monuments.Columns.Add(ChoiceCol("kind", "KIND", 90, Kinds));
            _monuments.Columns.Add(TextCol("desc", "DESCRIPTION", 260, false));
            _monuments.Columns.Add(new DataGridViewCheckBoxColumn { Name = "offset", HeaderText = "OFFSET", Width = 70 });
            _monuments.Columns.Add(TextCol("dist", "OFFSET DIST (FT)", 120, false));
            _monuments.Columns.Add(TextCol("dir", "DIRECTION TO CORNER", 190, false));
            var radiusColumn = TextCol("radius", "CORNER RADIUS (FT)", 140, false);
            radiusColumn.Visible = _job.Mode == RowMode.Block;
            _monuments.Columns.Add(radiusColumn);

            for (var i = 0; i < _job.Monuments.Count; i++)
            {
                var m = _job.Monuments[i];
                var radius = i < _job.CornerRadiiFeet.Count ? _job.CornerRadiiFeet[i] : 0.0;
                _monuments.Rows.Add(
                    string.IsNullOrWhiteSpace(m.PointNumber) ? (i + 1).ToString(CultureInfo.InvariantCulture) : m.PointNumber,
                    m.Kind == MonumentKind.Set ? "Set" : "Found",
                    string.IsNullOrEmpty(m.Description) ? _lastDescription : m.Description,
                    m.IsOffset,
                    m.IsOffset ? m.OffsetFeet.ToString("0.####", CultureInfo.InvariantCulture) : string.Empty,
                    string.Empty,
                    radius > 0 ? radius.ToString("0.##", CultureInfo.InvariantCulture) : string.Empty);
            }
        }

        private void BuildSideGrid()
        {
            _sides = NewGrid();
            _sides.Columns.Add(TextCol("side", "SIDE", 130, true));
            _sides.Columns.Add(ChoiceCol("type", "THIS SIDE IS", 130, FaceKinds));
            _sides.Columns.Add(TextCol("road", "ROAD NAME", 260, false));
            _sides.Columns.Add(TextCol("width", "RIGHT-OF-WAY WIDTH (FT)", 190, false));
            _sides.Columns.Add(TextCol("between", "BETWEEN PICKS (FT)", 150, true));

            var n = _job.Monuments.Count;
            for (var i = 0; i < n; i++)
            {
                var a = _job.Monuments[i];
                var b = _job.Monuments[(i + 1) % n];
                var face = i < _job.Faces.Count ? _job.Faces[i] : RowFace.Plain();
                var between = a.Location.DistanceTo(b.Location) / _job.UnitsPerFoot;
                _sides.Rows.Add(
                    PointLabel(a, i) + " to " + PointLabel(b, (i + 1) % n),
                    face.Kind.ToString(),
                    face.RoadName,
                    face.WidthFeet > 0 ? face.WidthFeet.ToString("0.##", CultureInfo.InvariantCulture) : string.Empty,
                    between.ToString("0.00", CultureInfo.InvariantCulture));
            }
        }

        private static string PointLabel(RowMonument m, int index)
        {
            return string.IsNullOrWhiteSpace(m.PointNumber) ? (index + 1).ToString(CultureInfo.InvariantCulture) : m.PointNumber;
        }

        private static DataGridView NewGrid()
        {
            var grid = new DataGridView
            {
                Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false,
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

        private static DataGridViewTextBoxColumn TextCol(string name, string header, int width, bool readOnly)
        {
            return new DataGridViewTextBoxColumn { Name = name, HeaderText = header, Width = width, ReadOnly = readOnly, SortMode = DataGridViewColumnSortMode.NotSortable };
        }

        private static DataGridViewComboBoxColumn ChoiceCol(string name, string header, int width, string[] items)
        {
            var column = new DataGridViewComboBoxColumn
            {
                Name = name, HeaderText = header, Width = width, FlatStyle = FlatStyle.Flat, SortMode = DataGridViewColumnSortMode.NotSortable
            };
            column.Items.AddRange(items);
            return column;
        }

        // ---------------------------------------------------------------- reading

        /// <summary>Reads every entry into the job. False, with the reasons on screen, when
        /// anything typed cannot be read.</summary>
        private bool Collect()
        {
            var problems = new List<string>();

            if (!_job.IsRing)
            {
                _job.RoadName = _road.Text.Trim();
                var centre = Convert.ToString(_reference.SelectedItem) == OnCenterline;
                _job.Reference = centre ? RowReference.Centerline : RowReference.RowLine;
                _job.RoadSide = Convert.ToString(_side.SelectedItem) == RoadLeft ? RowRoadSide.Left : RowRoadSide.Right;
                if (centre)
                {
                    _job.CenterlineLeftFeet = Number(_left.Text, "The left width", true, problems);
                    _job.CenterlineRightFeet = Number(_right.Text, "The right width", true, problems);
                }
                else _job.WidthFeet = Number(_width.Text, "The right-of-way width", true, problems);
            }

            _job.DrawCenterline = _centerline.Checked;
            _job.EveryMonumentDimension = _every.Checked;

            _job.CornerRadiiFeet.Clear();
            for (var i = 0; i < _job.Monuments.Count; i++)
            {
                var row = _monuments.Rows[i];
                var m = _job.Monuments[i];
                var name = "Monument " + Cell(row, "pt");
                m.Kind = Cell(row, "kind") == "Set" ? MonumentKind.Set : MonumentKind.Found;
                m.Description = Cell(row, "desc").Trim();
                m.IsOffset = row.Cells["offset"].Value != null && (bool)row.Cells["offset"].Value;
                if (m.IsOffset)
                {
                    m.OffsetFeet = Number(Cell(row, "dist"), name + ": the offset distance", true, problems);
                    var direction = Direction(Cell(row, "dir"), name, problems);
                    if (direction.HasValue) m.OffsetAzimuthDegrees = direction.Value;
                }
                else
                {
                    m.OffsetFeet = 0;
                    m.OffsetAzimuthDegrees = 0;
                }

                var radius = 0.0;
                if (_job.Mode == RowMode.Block && Cell(row, "radius").Trim().Length > 0)
                    radius = Number(Cell(row, "radius"), name + ": the corner radius", false, problems);
                _job.CornerRadiiFeet.Add(radius);
            }

            if (_job.IsRing)
            {
                _job.Faces.Clear();
                for (var i = 0; i < _job.Monuments.Count; i++)
                {
                    var row = _sides.Rows[i];
                    var kind = (RowFaceKind)Enum.Parse(typeof(RowFaceKind), Cell(row, "type"));
                    var face = new RowFace { Kind = kind, RoadName = Cell(row, "road").Trim() };
                    var text = Cell(row, "width").Trim();
                    if (kind == RowFaceKind.Frontage) face.WidthFeet = Number(text, "Side " + Cell(row, "side") + ": the right-of-way width", true, problems);
                    else if (text.Length > 0) face.WidthFeet = Number(text, "Side " + Cell(row, "side") + ": the right-of-way width", false, problems);
                    _job.Faces.Add(face);
                }
            }

            if (problems.Count > 0)
            {
                _errors.Text = string.Join("\n", problems.ToArray());
                return false;
            }

            _errors.Text = string.Empty;
            if (!_job.IsRing) _lastRoad = _job.RoadName;
            var described = _job.Monuments.Select(m => m.Description).FirstOrDefault(d => !string.IsNullOrWhiteSpace(d));
            if (described != null) _lastDescription = described;
            return true;
        }

        private static string Cell(DataGridViewRow row, string column)
        {
            var value = row.Cells[column].Value;
            return value == null ? string.Empty : Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static double Number(string text, string what, bool mustBePositive, ICollection<string> problems)
        {
            double value;
            if (!double.TryParse((text ?? string.Empty).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                problems.Add(what + " is not a number.");
                return 0;
            }
            if (mustBePositive ? !(value > 0) : value < 0)
            {
                problems.Add(what + (mustBePositive ? " must be greater than zero." : " cannot be negative."));
                return 0;
            }
            return value;
        }

        /// <summary>A bearing (N45.2536E) or an azimuth (135.0000), D.MMSS like everywhere else in FTF.</summary>
        private static double? Direction(string text, string what, ICollection<string> problems)
        {
            var t = (text ?? string.Empty).Trim();
            if (t.Length == 0)
            {
                problems.Add(what + ": give the direction from the monument to the corner, e.g. N45.2536E.");
                return null;
            }
            var parsed = char.IsLetter(t[0]) ? SurveyDirection.ParseBearing(t) : SurveyDirection.ParseAzimuth(t);
            if (!parsed.Ok)
            {
                problems.Add(what + ": " + parsed.Error);
                return null;
            }
            return parsed.Value;
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

        private static Label Caption(string text)
        {
            return new Label
            {
                AutoSize = true, Text = text, ForeColor = DipBuilderForm.Muted, Font = DipBuilderForm.F(8.5f, false),
                Dock = DockStyle.Top, Padding = new Padding(0, 0, 0, 4)
            };
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
