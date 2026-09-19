using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Autodesk.AutoCAD.EditorInput;
using FieldCodes.Easements;
using FieldCodes.RecordSurvey;
using FieldCodes.Settings;

using AcDocument = Autodesk.AutoCAD.ApplicationServices.Document;

namespace FieldCodes.Cad.Ui
{
    /// <summary>
    /// The review before anything is drawn: every extracted call in a table (course, bearing,
    /// distance, curve data, record source, object type, confidence, status), the page image
    /// beside it with the selected call's source highlighted, and the tools to approve, edit,
    /// take an alternative reading, reorder, assign figures and pick start points. Build is
    /// enabled only when the gate says nothing is unresolved. Every action goes through
    /// ReviewSession, which is unit tested; this form is only its face.
    ///
    /// UNTESTED in Civil 3D (WinForms, never shown outside it).
    /// </summary>
    internal sealed class RecordReviewForm : Form
    {
        private readonly AcDocument _doc;
        private readonly ReviewSession _session;
        private readonly DocumentText _text;
        private readonly StandardsResolution _standards;
        private readonly FtfSettings _settings;
        private readonly double _upf;

        private DataGridView _grid;
        private PictureBox _page;
        private Panel _pageHost;
        private ComboBox _zoom;
        private ComboBox _pageNumber;
        private TextBox _details;
        private TextBox _bearing, _distance, _curveValue, _scale, _north, _figureName;
        private ComboBox _target, _curveElement, _figure, _objectType;
        private NumericUpDown _order;
        private CheckBox _reversed, _figureClosed;
        private ListBox _alternatives, _figures, _issues;
        private Label _gate, _figureStart;
        private Button _build;
        private Image _pageImage;
        private int _pageShown = -1;
        private string _selected;

        public RecordReviewForm(AcDocument doc, ReviewSession session, DocumentText text, StandardsResolution standards, DrawingInventory inventory, FtfSettings settings)
        {
            _doc = doc; _session = session; _text = text; _standards = standards; _settings = settings;
            _upf = settings.General.UnitsPerFoot > 0 ? settings.General.UnitsPerFoot : 1.0;
            DipBuilderForm.UseTheme(ThemePreference.LoadDark());
            Text = "FTFRECORD -- review the extracted calls before anything is built";
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = false;
            BackColor = DipBuilderForm.Ground;
            ForeColor = DipBuilderForm.Ink;
            Font = DipBuilderForm.F(10f, false);
            var area = Screen.FromPoint(Cursor.Position).WorkingArea;
            ClientSize = new Size(Math.Min(1500, area.Width - 60), Math.Min(920, area.Height - 60));
            MinimumSize = new Size(1000, 640);
            Build();
            LoadRows();
            RefreshGate();
        }

        // ------------------------------------------------------------ layout

        private void Build()
        {
            var header = new Panel { Dock = DockStyle.Top, Height = 64, BackColor = DipBuilderForm.Surface, Padding = new Padding(14, 8, 14, 6) };
            var d = _session.Project.Document;
            var title = new Label { AutoSize = true, Font = DipBuilderForm.F(13f, true), Text = d.SurveyType + ": " + (d.Title ?? Path.GetFileName(d.Path ?? string.Empty)), Location = new Point(14, 8) };
            var sub = new Label { AutoSize = true, ForeColor = DipBuilderForm.Muted, Location = new Point(14, 36),
                Text = (d.RecordingNumber != null ? "AFN " + d.RecordingNumber + "   " : string.Empty) + (d.County ?? string.Empty) + "   " +
                       _session.Project.Calls.Count + " calls, " + _session.Project.Calls.Count(c => c.Status == CallStatus.NeedsReview) + " need review   " +
                       "review threshold " + _settings.RecordSurvey.ReviewThreshold.ToString("0.00", CultureInfo.InvariantCulture) + "   build from " + _settings.RecordSurvey.BuildFrom };
            header.Controls.Add(title); header.Controls.Add(sub);

            var tools = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(10, 6, 10, 2), WrapContents = false };
            tools.Controls.Add(Btn("Approve all above threshold", (s, e) => { var n = _session.ApproveAllAbove(_settings.RecordSurvey.ReviewThreshold); Status(n + " call(s) approved."); LoadRows(); }));
            tools.Controls.Add(Lbl("Scale 1\" ="));
            _scale = new TextBox { Width = 50, Text = d.ScaleFeetPerInch.HasValue ? d.ScaleFeetPerInch.Value.ToString("0.#", CultureInfo.InvariantCulture) : string.Empty };
            tools.Controls.Add(_scale);
            tools.Controls.Add(Lbl("'   north rotation"));
            _north = new TextBox { Width = 40, Text = "0" };
            tools.Controls.Add(_north);
            tools.Controls.Add(Btn("Order courses from the page", (s, e) => AutoOrder()));
            tools.Controls.Add(Btn("Show standards issues", (s, e) => ShowIssues()));

            var footer = new Panel { Dock = DockStyle.Bottom, Height = 46, BackColor = DipBuilderForm.Surface, Padding = new Padding(14, 8, 14, 8) };
            _gate = new Label { AutoSize = true, Location = new Point(14, 14), ForeColor = DipBuilderForm.Warn };
            _build = new Button { Text = "Build", Width = 120, Height = 30, Anchor = AnchorStyles.Right | AnchorStyles.Top, DialogResult = DialogResult.None };
            _build.Location = new Point(footer.Width - 280, 8);
            _build.Click += (s, e) => TryBuild();
            var cancel = new Button { Text = "Cancel", Width = 120, Height = 30, Anchor = AnchorStyles.Right | AnchorStyles.Top, DialogResult = DialogResult.Cancel };
            cancel.Location = new Point(footer.Width - 150, 8);
            footer.Controls.Add(_gate); footer.Controls.Add(_build); footer.Controls.Add(cancel);
            CancelButton = cancel;

            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterDistance = 860 };
            var left = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 420 };

            _grid = new DataGridView
            {
                Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = DipBuilderForm.Surface
            };
            foreach (var c in new[] { "Course", "Type", "Bearing", "Distance", "Curve", "Record source", "Object type", "Confidence", "Status", "Flags" })
                _grid.Columns.Add(c, c);
            _grid.Columns["Course"].FillWeight = 90; _grid.Columns["Type"].FillWeight = 40; _grid.Columns["Curve"].FillWeight = 120; _grid.Columns["Flags"].FillWeight = 140;
            _grid.Columns["Confidence"].FillWeight = 50; _grid.Columns["Status"].FillWeight = 60;
            _grid.SelectionChanged += (s, e) => OnRowSelected();
            _grid.CellFormatting += (s, e) => { var row = _grid.Rows[e.RowIndex].Tag as ReviewRow; if (row == null) return; if (row.Status == CallStatus.NeedsReview) e.CellStyle.ForeColor = DipBuilderForm.Warn; else if (row.Status == CallStatus.Rejected) e.CellStyle.ForeColor = DipBuilderForm.Muted; else if (row.Status == CallStatus.Approved) e.CellStyle.ForeColor = DipBuilderForm.Good; };
            left.Panel1.Controls.Add(_grid);
            left.Panel2.Controls.Add(BuildEditor());

            var right = new Panel { Dock = DockStyle.Fill };
            var pageTools = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false, Padding = new Padding(6, 4, 6, 2) };
            pageTools.Controls.Add(Lbl("Page"));
            _pageNumber = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 60 };
            if (_text != null) foreach (var p in _text.Pages) _pageNumber.Items.Add(p.Number.ToString(CultureInfo.InvariantCulture));
            _pageNumber.SelectedIndexChanged += (s, e) => ShowPage(_pageNumber.SelectedIndex + 1, null);
            pageTools.Controls.Add(_pageNumber);
            pageTools.Controls.Add(Lbl("Zoom"));
            _zoom = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 70 };
            foreach (var z in new[] { "25%", "50%", "75%", "100%", "150%", "200%" }) _zoom.Items.Add(z);
            _zoom.SelectedIndex = 1;
            _zoom.SelectedIndexChanged += (s, e) => ShowPage(_pageShown, _selected);
            pageTools.Controls.Add(_zoom);
            pageTools.Controls.Add(Lbl("The selected call's source is outlined on the page."));
            _pageHost = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = DipBuilderForm.PreviewBack };
            _page = new PictureBox { SizeMode = PictureBoxSizeMode.AutoSize, Location = new Point(0, 0) };
            _pageHost.Controls.Add(_page);
            right.Controls.Add(_pageHost); right.Controls.Add(pageTools);

            split.Panel1.Controls.Add(left);
            split.Panel2.Controls.Add(right);
            Controls.Add(split); Controls.Add(tools); Controls.Add(footer); Controls.Add(header);
            if (_pageNumber.Items.Count > 0) _pageNumber.SelectedIndex = 0;
        }

        private Control BuildEditor()
        {
            var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Padding = new Padding(8, 4, 8, 4) };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));

            // Column 1: details and the value editors.
            var col1 = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
            _details = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Width = 380, Height = 150, BackColor = DipBuilderForm.Surface, ForeColor = DipBuilderForm.Ink };
            col1.Controls.Add(_details);
            var edit = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            edit.Controls.Add(Lbl("Value of"));
            _target = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90 };
            edit.Controls.Add(_target);
            edit.Controls.Add(Lbl("bearing")); _bearing = new TextBox { Width = 120 }; edit.Controls.Add(_bearing);
            edit.Controls.Add(Lbl("distance")); _distance = new TextBox { Width = 80 }; edit.Controls.Add(_distance);
            edit.Controls.Add(Btn("Apply", (s, e) => ApplyLineEdit()));
            col1.Controls.Add(edit);
            var curve = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            curve.Controls.Add(Lbl("Curve element"));
            _curveElement = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90 };
            foreach (var el in new[] { "R", "DELTA", "L", "CH", "CB", "T", "TURN", "TANGENT" }) _curveElement.Items.Add(el);
            _curveElement.SelectedIndex = 0;
            curve.Controls.Add(_curveElement);
            _curveValue = new TextBox { Width = 140 }; curve.Controls.Add(_curveValue);
            curve.Controls.Add(Btn("Apply", (s, e) => ApplyCurveEdit()));
            col1.Controls.Add(curve);
            var actions = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            actions.Controls.Add(Btn("Approve", (s, e) => Act(_session.Approve)));
            actions.Controls.Add(Btn("Reject", (s, e) => Act(_session.Reject)));
            actions.Controls.Add(Btn("Reopen", (s, e) => Act(_session.Reopen)));
            col1.Controls.Add(actions);
            table.Controls.Add(col1, 0, 0);

            // Column 2: alternatives and figure assignment.
            var col2 = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
            col2.Controls.Add(Lbl("Alternative readings (offered, never applied on their own)"));
            _alternatives = new ListBox { Width = 300, Height = 70, BackColor = DipBuilderForm.Surface, ForeColor = DipBuilderForm.Ink };
            col2.Controls.Add(_alternatives);
            col2.Controls.Add(Btn("Use the selected alternative", (s, e) => UseAlternative()));
            var assign = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            assign.Controls.Add(Lbl("Figure"));
            _figure = new ComboBox { Width = 120 };
            assign.Controls.Add(_figure);
            assign.Controls.Add(Lbl("order")); _order = new NumericUpDown { Width = 50, Minimum = 0, Maximum = 999 }; assign.Controls.Add(_order);
            _reversed = new CheckBox { Text = "reversed", AutoSize = true }; assign.Controls.Add(_reversed);
            col2.Controls.Add(assign);
            var type = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            type.Controls.Add(Lbl("Object type"));
            _objectType = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130 };
            foreach (var en in _settings.RecordSurvey.Entities.Where(x => x.Enabled)) _objectType.Items.Add(en.Name);
            type.Controls.Add(_objectType);
            type.Controls.Add(Btn("Assign", (s, e) => Assign()));
            type.Controls.Add(Btn("Up", (s, e) => Move(-1)));
            type.Controls.Add(Btn("Down", (s, e) => Move(1)));
            col2.Controls.Add(type);
            table.Controls.Add(col2, 1, 0);

            // Column 3: figures and their start points.
            var col3 = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
            col3.Controls.Add(Lbl("Figures (lots, tracts, boundaries)"));
            _figures = new ListBox { Width = 260, Height = 90, BackColor = DipBuilderForm.Surface, ForeColor = DipBuilderForm.Ink };
            _figures.SelectedIndexChanged += (s, e) => OnFigureSelected();
            col3.Controls.Add(_figures);
            _figureStart = new Label { AutoSize = true, ForeColor = DipBuilderForm.Muted, Text = "start: -" };
            col3.Controls.Add(_figureStart);
            var fig = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            _figureClosed = new CheckBox { Text = "closed", AutoSize = true };
            _figureClosed.CheckedChanged += (s, e) => { var f = _figures.SelectedItem as string; if (f != null && !_loadingFigure) { _session.SetFigureClosed(f, _figureClosed.Checked); RefreshGate(); } };
            fig.Controls.Add(_figureClosed);
            fig.Controls.Add(Btn("Pick start in drawing", (s, e) => PickStart()));
            col3.Controls.Add(fig);
            var add = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            _figureName = new TextBox { Width = 120 };
            add.Controls.Add(_figureName);
            add.Controls.Add(Btn("Add figure", (s, e) => { if (_figureName.Text.Trim().Length == 0) return; _session.AddFigure(_figureName.Text.Trim(), "Figure", true); LoadFigures(); }));
            col3.Controls.Add(add);
            col3.Controls.Add(Lbl("Closure preview"));
            _issues = new ListBox { Width = 260, Height = 80, BackColor = DipBuilderForm.Surface, ForeColor = DipBuilderForm.Ink };
            col3.Controls.Add(_issues);
            table.Controls.Add(col3, 2, 0);
            return table;
        }

        private static Label Lbl(string text) { return new Label { Text = text, AutoSize = true, Margin = new Padding(4, 7, 4, 0) }; }

        private static Button Btn(string text, EventHandler click)
        {
            var b = new Button { Text = text, AutoSize = true, Margin = new Padding(3), MinimumSize = new Size(70, 26) };
            b.Click += click;
            return b;
        }

        // ------------------------------------------------------------ data

        private bool _loadingFigure;

        private void LoadRows()
        {
            var selected = _selected;
            _grid.Rows.Clear();
            foreach (var row in _session.Rows())
            {
                var i = _grid.Rows.Add(row.Course, row.Type, row.Bearing, row.Distance, row.CurveInfo, row.RecordSource, row.ObjectType,
                                       row.Confidence.ToString("0.00", CultureInfo.InvariantCulture), row.Status.ToString(), row.Flags);
                _grid.Rows[i].Tag = row;
            }
            LoadFigures();
            if (selected != null)
                foreach (DataGridViewRow r in _grid.Rows)
                    if (((ReviewRow)r.Tag).CallId == selected) { r.Selected = true; _grid.FirstDisplayedScrollingRowIndex = Math.Max(0, r.Index - 3); break; }
            RefreshGate();
        }

        private void LoadFigures()
        {
            var selected = _figures.SelectedItem as string;
            _figures.Items.Clear();
            _figure.Items.Clear();
            foreach (var f in _session.Project.Figures) { _figures.Items.Add(f.Name); _figure.Items.Add(f.Name); }
            if (selected != null && _figures.Items.Contains(selected)) _figures.SelectedItem = selected;
        }

        private SurveyCall Selected()
        {
            if (_grid.SelectedRows.Count == 0) return null;
            var row = _grid.SelectedRows[0].Tag as ReviewRow;
            return row == null ? null : _session.Project.FindCall(row.CallId);
        }

        private void OnRowSelected()
        {
            var call = Selected();
            if (call == null) return;
            _selected = call.Id;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(call.Id + "  " + call.Kind + "  status " + call.Status + "  confidence " + call.Confidence.ToString("0.00", CultureInfo.InvariantCulture) + "  basis " + call.Basis);
            if (call.Source != null) sb.AppendLine("Source: " + call.Source);
            if (call.Measured != null && !call.Measured.Empty) sb.AppendLine("Measured: " + call.Measured.Describe(_settings.RecordSurvey.BearingSecondsDecimals, _settings.RecordSurvey.DistanceDecimals));
            foreach (var r in call.Records)
            {
                var reference = _session.Project.FindReference(r.SourceId);
                sb.AppendLine("Record " + (r.SourceId.Length > 0 ? r.SourceId : "(document)") + ": " + r.Describe(_settings.RecordSurvey.BearingSecondsDecimals, _settings.RecordSurvey.DistanceDecimals) + (reference != null ? "   [" + reference.Description + "]" : string.Empty));
            }
            if (call.Curve != null) sb.AppendLine("Curve: " + ReviewSession.CurveSummary(call.Curve, _settings.RecordSurvey));
            foreach (var n in call.Notes) sb.AppendLine("- " + n);
            foreach (var e in call.Edits) sb.AppendLine("edit: " + e);
            _details.Text = sb.ToString();

            _target.Items.Clear();
            _target.Items.Add("measured");
            foreach (var r in call.Records) _target.Items.Add(r.SourceId.Length > 0 ? r.SourceId : "(record)");
            _target.SelectedIndex = call.Measured != null && !call.Measured.Empty ? 0 : Math.Min(1, _target.Items.Count - 1);
            _alternatives.Items.Clear();
            foreach (var a in call.Alternatives) _alternatives.Items.Add(a.ToString());
            _figure.Text = call.Figure ?? string.Empty;
            _order.Value = Math.Min(_order.Maximum, call.Order);
            _reversed.Checked = call.Reversed;
            _objectType.SelectedItem = call.ObjectType;
            _bearing.Text = string.Empty; _distance.Text = string.Empty; _curveValue.Text = string.Empty;

            if (call.Source != null) ShowPage(call.Source.Page, call.Id);
        }

        private void OnFigureSelected()
        {
            var name = _figures.SelectedItem as string;
            var f = _session.Project.Figures.FirstOrDefault(x => x.Name == name);
            if (f == null) return;
            _loadingFigure = true;
            _figureClosed.Checked = f.Closed;
            _loadingFigure = false;
            _figureStart.Text = f.Start.HasValue ? string.Format(CultureInfo.InvariantCulture, "start: {0:0.###}, {1:0.###}", f.Start.Value.X, f.Start.Value.Y) : "start: not picked";
        }

        // ------------------------------------------------------------ page

        private void ShowPage(int page, string highlightCallId)
        {
            if (_text == null || page < 1 || page > _text.Pages.Count) return;
            var p = _text.Pages[page - 1];
            if (_pageShown != page)
            {
                if (_pageImage != null) { _pageImage.Dispose(); _pageImage = null; }
                if (!string.IsNullOrEmpty(p.ImagePath) && File.Exists(p.ImagePath))
                {
                    try { using (var fs = new FileStream(p.ImagePath, FileMode.Open, FileAccess.Read)) _pageImage = Image.FromStream(fs); }
                    catch (Exception) { _pageImage = null; }
                }
                _pageShown = page;
                if (_pageNumber.SelectedIndex != page - 1) _pageNumber.SelectedIndex = page - 1;
            }
            var zoom = new[] { 0.25, 0.5, 0.75, 1.0, 1.5, 2.0 }[Math.Max(0, _zoom.SelectedIndex)];
            var width = (int)Math.Max(1, (p.WidthPx > 0 ? p.WidthPx : 1000) * zoom);
            var height = (int)Math.Max(1, (p.HeightPx > 0 ? p.HeightPx : 1000) * zoom);
            var bitmap = new Bitmap(width, height);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.White);
                if (_pageImage != null) g.DrawImage(_pageImage, 0, 0, width, height);
                else
                {
                    // No page image (sidecar without images): draw the text boxes so the review still points somewhere.
                    using (var pen = new Pen(Color.Gray))
                        foreach (var line in p.Lines) g.DrawRectangle(pen, (float)(line.Box.X * zoom), (float)(line.Box.Y * zoom), (float)Math.Max(1, line.Box.Width * zoom), (float)Math.Max(1, line.Box.Height * zoom));
                }
                var call = highlightCallId != null ? _session.Project.FindCall(highlightCallId) : null;
                if (call != null && call.Source != null && call.Source.Page == page && call.Source.Box != null)
                {
                    var b = call.Source.Box;
                    using (var pen = new Pen(Color.Red, 3f))
                        g.DrawRectangle(pen, (float)(b.X * zoom) - 4, (float)(b.Y * zoom) - 4, (float)(b.Width * zoom) + 8, (float)(b.Height * zoom) + 8);
                    foreach (var extra in new[] { call.Measured != null ? call.Measured.DistanceSource : null }.Concat(call.Records.Select(r => r.DistanceSource)).Where(x => x != null && x.Page == page && x.Box != null))
                        using (var pen = new Pen(Color.OrangeRed, 2f))
                            g.DrawRectangle(pen, (float)(extra.Box.X * zoom) - 3, (float)(extra.Box.Y * zoom) - 3, (float)(extra.Box.Width * zoom) + 6, (float)(extra.Box.Height * zoom) + 6);
                    var old = _page.Image;
                    _page.Image = bitmap;
                    if (old != null) old.Dispose();
                    _pageHost.AutoScrollPosition = new Point((int)Math.Max(0, b.CenterX * zoom - _pageHost.ClientSize.Width / 2), (int)Math.Max(0, b.CenterY * zoom - _pageHost.ClientSize.Height / 2));
                    return;
                }
            }
            var previous = _page.Image;
            _page.Image = bitmap;
            if (previous != null) previous.Dispose();
        }

        // ------------------------------------------------------------ actions

        private void Status(string message)
        {
            _gate.ForeColor = DipBuilderForm.Muted;
            _gate.Text = message;
        }

        private void Act(Func<string, ReviewOutcome> action)
        {
            var call = Selected();
            if (call == null) return;
            var outcome = action(call.Id);
            if (!outcome.Ok) { MessageBox.Show(this, outcome.Message, "FTFRECORD", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            LoadRows();
        }

        private void ApplyLineEdit()
        {
            var call = Selected();
            if (call == null) return;
            var target = _target.SelectedItem as string ?? "measured";
            if (target == "(record)") target = string.Empty;
            var messages = new List<string>();
            if (_bearing.Text.Trim().Length > 0) { var o = _session.EditBearing(call.Id, target, _bearing.Text); if (!o.Ok) messages.Add(o.Message); }
            if (_distance.Text.Trim().Length > 0) { var o = _session.EditDistance(call.Id, target, _distance.Text); if (!o.Ok) messages.Add(o.Message); }
            if (messages.Count > 0) MessageBox.Show(this, string.Join(Environment.NewLine, messages.ToArray()), "Not applied", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            LoadRows();
            OnRowSelected();
        }

        private void ApplyCurveEdit()
        {
            var call = Selected();
            if (call == null) return;
            var o = _session.EditCurve(call.Id, _curveElement.SelectedItem as string, _curveValue.Text);
            if (!o.Ok) MessageBox.Show(this, o.Message, "Not applied", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            else Status(o.Message);
            LoadRows();
            OnRowSelected();
        }

        private void UseAlternative()
        {
            var call = Selected();
            if (call == null || _alternatives.SelectedIndex < 0) return;
            var o = _session.ChooseAlternative(call.Id, _alternatives.SelectedIndex);
            if (!o.Ok) MessageBox.Show(this, o.Message, "Not applied", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            LoadRows();
            OnRowSelected();
        }

        private void Assign()
        {
            var call = Selected();
            if (call == null) return;
            var figure = (_figure.Text ?? string.Empty).Trim();
            _session.Assign(call.Id, figure, (int)_order.Value);
            _session.SetReversed(call.Id, _reversed.Checked);
            var type = _objectType.SelectedItem as string;
            if (type != null) _session.SetObjectType(call.Id, type);
            LoadRows();
        }

        private void Move(int delta)
        {
            var call = Selected();
            if (call == null) return;
            _session.Move(call.Id, delta);
            LoadRows();
        }

        private void AutoOrder()
        {
            double scale;
            if (!double.TryParse(_scale.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out scale) || scale <= 0)
            {
                MessageBox.Show(this, "Enter the drawing scale in feet per inch (the plat's SCALE note) to order the courses from where their labels sit.", "FTFRECORD", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            double north;
            double.TryParse(_north.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out north);
            _session.Project.Document.ScaleFeetPerInch = scale;
            var dpi = _text != null && _text.Pages.Count > 0 && _text.Pages[0].Dpi > 0 ? _text.Pages[0].Dpi : _settings.RecordSurvey.OcrDpi;
            var overwrite = MessageBox.Show(this, "Replace the current figure assignment and order with the page's?", "FTFRECORD", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
            var result = _session.AutoOrder(new AssemblyOptions { ScaleFeetPerInch = scale, Dpi = dpi, NorthRotationDegrees = north }, overwrite);
            var summary = string.Join(Environment.NewLine, result.Figures.Select(f => f.Name + ": " + f.CallIds.Count + " course(s)" + (f.Closed ? ", closed" : " -- " + string.Join(" ", f.Notes.ToArray()))).Concat(result.Problems).ToArray());
            Status(result.Figures.Count + " figure(s) proposed.");
            if (summary.Length > 0) MessageBox.Show(this, summary, "Ordered from the page", MessageBoxButtons.OK, MessageBoxIcon.Information);
            LoadRows();
        }

        private void PickStart()
        {
            var name = _figures.SelectedItem as string;
            if (name == null) { MessageBox.Show(this, "Select a figure first.", "FTFRECORD", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            var ed = _doc.Editor;
            PromptPointResult picked;
            using (ed.StartUserInteraction(this))
            {
                picked = ed.GetPoint(new PromptPointOptions("\nStart point (Point of Beginning) of " + name + ": ") { AllowNone = false });
            }
            if (picked.Status != PromptStatus.OK) return;
            var p = CadUtil.Flatten(picked.Value);
            var notes = _session.PlaceConnected(name, new P2(p.X, p.Y), _upf);
            OnFigureSelected();
            RefreshGate();
            if (notes.Count > 0) MessageBox.Show(this, string.Join(Environment.NewLine, notes.ToArray()), "Figures placed", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ShowIssues()
        {
            var lines = _standards.Issues.Select(i => i.ToString()).ToList();
            if (lines.Count == 0) lines.Add("Every layer, style and block the record needs is in this drawing.");
            MessageBox.Show(this, string.Join(Environment.NewLine, lines.ToArray()), "Standards against this drawing", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void RefreshGate()
        {
            var gate = _session.Gate();
            _build.Enabled = gate.Ready;
            _gate.ForeColor = gate.Ready ? DipBuilderForm.Good : DipBuilderForm.Warn;
            _gate.Text = gate.Ready
                ? "Ready to build." + (gate.Warnings.Count > 0 ? " " + gate.Warnings[0] : string.Empty)
                : gate.Blockers[0] + (gate.Blockers.Count > 1 ? " (+" + (gate.Blockers.Count - 1) + " more)" : string.Empty);
            _issues.Items.Clear();
            try
            {
                foreach (var t in _session.Preview(_upf))
                {
                    if (t.Closure == null) { _issues.Items.Add(t.Figure + ": " + string.Join("; ", t.Problems.ToArray())); continue; }
                    _issues.Items.Add(t.Figure + ": " + (t.Closure.Closed ? "closure " + t.Closure.Misclosure.ToString("0.000", CultureInfo.InvariantCulture) + "' (" + TraverseBuilder.PrecisionText(t.Closure) + ")" : "open") + (t.Ok ? string.Empty : " -- " + string.Join("; ", t.Problems.ToArray())));
                    foreach (var sug in t.Suggestions) _issues.Items.Add("  alternative: " + sug);
                }
            }
            catch (Exception ex) { _issues.Items.Add("preview failed: " + ex.Message); }
        }

        private void TryBuild()
        {
            var gate = _session.Gate();
            if (!gate.Ready)
            {
                MessageBox.Show(this, string.Join(Environment.NewLine, gate.Blockers.ToArray()), "Not ready to build", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (gate.Warnings.Count > 0 &&
                MessageBox.Show(this, string.Join(Environment.NewLine, gate.Warnings.ToArray()) + Environment.NewLine + Environment.NewLine + "Build anyway?", "FTFRECORD", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            DialogResult = DialogResult.OK;
            Close();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_pageImage != null) _pageImage.Dispose();
                if (_page != null && _page.Image != null) _page.Image.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
