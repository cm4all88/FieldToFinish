using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using FieldCodes.Settings;
using FieldCodes.Utilities;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using CogoPoint = Autodesk.Civil.DatabaseServices.CogoPoint;
using Extents3d = Autodesk.AutoCAD.DatabaseServices.Extents3d;
using ObjectId = Autodesk.AutoCAD.DatabaseServices.ObjectId;
using OpenMode = Autodesk.AutoCAD.DatabaseServices.OpenMode;

namespace FieldCodes.Cad.Ui
{
    /// <summary>
    /// The Storm / Sewer Dip Builder. The drafter types what the crew wrote on paper;
    /// the drawing supplies the surveyed rim and position; every calculated value is
    /// shown as calculated. Every change to the drawing goes through DipSession, so
    /// it happens inside one AutoCAD command and undoes as one step.
    ///
    /// Build tab: three steps on the left (structure, pipes, connections), the live
    /// label and a local map on the right. Map tab: every structure and connection,
    /// with draw-all and label-all. Warnings sit quietly at the bottom.
    /// </summary>
    internal sealed class DipBuilderForm : Form
    {
        // Palette: one working blue on a quiet ground. Amber, red and green only ever
        // mean state. Two sets: light, and dark to sit beside Civil 3D's dark theme.
        internal static Color Ground, Surface, Rule, Ink, Muted, Accent, AccentHover, AccentSoft, ButtonBorder,
                              Calculated, Unconfirmed, PreviewBack, Warn, Bad, Good;

        internal static bool Dark { get; private set; }

        internal static void UseTheme(bool dark)
        {
            Dark = dark;
            if (dark)
            {
                Ground = Color.FromArgb(40, 45, 54);
                Surface = Color.FromArgb(52, 58, 69);
                Rule = Color.FromArgb(78, 86, 99);
                Ink = Color.FromArgb(242, 244, 247);
                Muted = Color.FromArgb(206, 213, 222);
                Accent = Color.FromArgb(42, 120, 206);
                AccentHover = Color.FromArgb(60, 140, 226);
                AccentSoft = Color.FromArgb(44, 72, 104);
                ButtonBorder = Color.FromArgb(104, 114, 128);
                Calculated = Color.FromArgb(60, 67, 79);
                Unconfirmed = Color.FromArgb(92, 74, 40);
                PreviewBack = Color.FromArgb(32, 36, 43);
                Warn = Color.FromArgb(240, 180, 90);
                Bad = Color.FromArgb(255, 128, 116);
                Good = Color.FromArgb(120, 214, 146);
            }
            else
            {
                Ground = Color.FromArgb(238, 241, 245);
                Surface = Color.White;
                Rule = Color.FromArgb(196, 204, 214);
                Ink = Color.FromArgb(16, 22, 30);
                Muted = Color.FromArgb(44, 54, 66);
                Accent = Color.FromArgb(10, 90, 170);
                AccentHover = Color.FromArgb(8, 74, 140);
                AccentSoft = Color.FromArgb(214, 230, 247);
                ButtonBorder = Color.FromArgb(150, 162, 176);
                Calculated = Color.FromArgb(240, 243, 247);
                Unconfirmed = Color.FromArgb(255, 236, 196);
                PreviewBack = Color.FromArgb(248, 250, 252);
                Warn = Color.FromArgb(140, 80, 0);
                Bad = Color.FromArgb(176, 30, 20);
                Good = Color.FromArgb(22, 110, 44);
            }
        }

        static DipBuilderForm() { UseTheme(true); }

        // Verdana: wide, open letters that stay legible at small sizes on any screen.
        private const string Face = "Verdana";

        /// <summary>A font at the given design size. Verdana runs large, so sizes are
        /// scaled down to match; headings (11 and up) are bold.</summary>
        internal static Font F(float size, bool heavy)
        {
            return new Font(Face, size * 0.86f, heavy && size >= 11f ? FontStyle.Bold : FontStyle.Regular);
        }

        private string _structureId;
        private FtfSettings _settings;
        private IList<ConnectionCandidate> _candidates = new List<ConnectionCandidate>();
        private bool _labelEdited;
        private bool _settingPreview;
        private bool _syncing;
        private string _generatedLabel;
        private bool _loading;
        private bool _warningsOpen;

        // view
        private TabControl _tabs;
        private CheckBox _advancedToggle;
        private readonly List<Control> _advancedControls = new List<Control>();
        private static readonly string[] AdvancedColumns = { "H", "Shape", "Reference", "RefStatus", "Role", "Conditions", "Notes", "Connection", "Source" };
        private static readonly string[] BasicOnlyColumns = { "Top" };

        // header
        private Panel _header;
        private FlowLayoutPanel _headerText;
        private Label _structureTitle;
        private Label _pointInfo;

        // structure
        private ComboBox _type;
        private ComboBox _system;
        private Label _status;
        private TextBox _bottom;
        private TextBox _water;
        private Label _calcDips;
        private FlowLayoutPanel _sizeGroup;
        private Label _sizeCaption;
        private TextBox _insideWidth;
        private Label _lengthCaption;
        private TextBox _insideLength;
        private Label _insideSource;

        // typed notes (advanced)
        private TextBox _notes;
        private ListBox _diagnostics;

        // pipes
        private DataGridView _grid;

        // connections
        private ListView _connections;
        private Label _candidateCaption;
        private ListView _candidateList;
        private Label _connectionState;

        // right-hand side
        private Panel _side;
        private TextBox _labelPreview;
        private Label _labelState;
        private ConnectionMapView _map;

        // map tab
        private ConnectionMapView _projectMap;
        private Label _projectSummary;

        // warnings
        private Panel _warningsPanel;
        private LinkLabel _warningsToggle;
        private CheckBox _darkToggle;
        private ListBox _warnings;

        // review
        private Label _summary;
        private ListView _findings;
        private TextBox _revisit;
        private TextBox _revisitAdd;

        public DipBuilderForm()
        {
            Text = "Storm / Sewer Dip Builder";
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = false;
            MinimizeBox = true;
            MaximizeBox = true;
            UseTheme(ThemePreference.LoadDark());
            BackColor = Ground;
            ForeColor = Ink;
            Font = F(10.5f, false);

            // Fit a laptop screen: never open taller or wider than the working area.
            var area = Screen.FromPoint(Cursor.Position).WorkingArea;
            ClientSize = new Size(Math.Min(1320, area.Width - 40), Math.Min(880, area.Height - 60));
            // Civil 3D restores the last size the window had; the minimum keeps an old, smaller
            // saved size from cramping the two-column layout.
            MinimumSize = new Size(Math.Min(1180, area.Width), Math.Min(620, area.Height));

            // The stock tab strip ignores colours; its headers are hidden and drawn as a
            // row of flat tab buttons instead.
            _tabs = new TabControl { Dock = DockStyle.Fill, Appearance = TabAppearance.FlatButtons, ItemSize = new Size(0, 1), SizeMode = TabSizeMode.Fixed };
            _tabs.TabPages.Add(BuildStructureTab());
            _tabs.TabPages.Add(BuildMapTab());
            _tabs.TabPages.Add(BuildReviewTab());
            _tabs.TabPages.Add(BuildRevisitTab());
            _tabs.SelectedIndexChanged += (s, e) => { RefreshReview(); if (_projectMap != null) _projectMap.Invalidate(); };
            var tabHost = new Panel { Dock = DockStyle.Fill, BackColor = Ground };
            tabHost.Controls.Add(_tabs);
            _tabs.Dock = DockStyle.None;
            tabHost.Resize += (s, e) => _tabs.Bounds = new Rectangle(-4, -5, tabHost.ClientSize.Width + 8, tabHost.ClientSize.Height + 9);
            Controls.Add(tabHost);
            Controls.Add(BuildTabBar());
            Controls.Add(BuildWarningsPanel());
            Controls.Add(BuildStatusBar());
            Controls.Add(BuildHeader());

            FormClosed += (s, e) => DipSession.Form = null;
            ApplyView();
            ApplyTheme(this);
        }

        /// <summary>Structure to reopen after the window is rebuilt for a theme change.</summary>
        private static string _reopenStructure;

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // Dark title bar to match (Windows 10 2004 and later; ignored elsewhere).
            try
            {
                var on = Dark ? 1 : 0;
                DwmSetWindowAttribute(Handle, 20, ref on, sizeof(int));
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (_reopenStructure != null)
            {
                var id = _reopenStructure;
                _reopenStructure = null;
                OpenStructure(id);
            }
        }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        [System.Runtime.InteropServices.DllImport("uxtheme.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hwnd, string appName, string idList);

        /// <summary>Colours the stock controls that do not take them from their parent:
        /// text boxes, lists, combos and the grid, plus dark scroll bars in the dark theme.</summary>
        private static void ApplyTheme(Control root)
        {
            foreach (Control c in root.Controls)
            {
                if (c is TextBox && c.BackColor == PreviewBack) { c.ForeColor = Ink; }
                else if (c is TextBox && !((TextBox)c).ReadOnly) { c.BackColor = Dark ? Calculated : Color.White; c.ForeColor = Ink; }
                else if (c is TextBox) { c.ForeColor = Ink; }
                else if (c is ComboBox) { var cb = (ComboBox)c; cb.FlatStyle = FlatStyle.Flat; cb.BackColor = Dark ? Calculated : Color.White; cb.ForeColor = Ink; }
                else if (c is ListBox) { c.BackColor = Surface; c.ForeColor = Ink; }
                else if (c is ListView) { ThemeList((ListView)c); }
                else if (c is CheckBox) { c.ForeColor = Ink; }
                else if (c is DataGridView)
                {
                    var g = (DataGridView)c;
                    g.BackgroundColor = Surface;
                    g.EnableHeadersVisualStyles = false;
                    g.DefaultCellStyle.BackColor = Surface;
                    g.RowHeadersDefaultCellStyle.BackColor = Ground;
                }
                if (Dark && (c is ListView || c is ListBox || c is TextBox || c is DataGridView || c is ScrollableControl))
                {
                    var control = c;
                    EventHandler dark = (s, e) => { try { SetWindowTheme(control.Handle, "DarkMode_Explorer", null); } catch (DllNotFoundException) { } };
                    if (c.IsHandleCreated) dark(null, EventArgs.Empty); else c.HandleCreated += dark;
                }
                ApplyTheme(c);
            }
        }

        /// <summary>The stock list header ignores colours, so headers are drawn here; rows
        /// keep their default drawing, including per-cell status colours.</summary>
        private static void ThemeList(ListView list)
        {
            list.BackColor = Surface;
            list.ForeColor = Ink;
            list.OwnerDraw = true;
            list.DrawItem += (s, e) => { };
            list.DrawSubItem += (s, e) =>
            {
                var selected = e.Item.Selected;
                using (var back = new SolidBrush(selected ? AccentSoft : list.BackColor)) e.Graphics.FillRectangle(back, e.Bounds);
                var sub = e.SubItem;
                var color = e.Item.UseItemStyleForSubItems || sub == null ? e.Item.ForeColor : sub.ForeColor;
                if (color == list.BackColor || color.IsEmpty) color = Ink;
                var font = e.Item.UseItemStyleForSubItems || sub == null ? e.Item.Font : sub.Font;
                var text = new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height);
                TextRenderer.DrawText(e.Graphics, sub != null ? sub.Text : e.Item.Text, font, text, color,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            };
            // The last column fills the width, so no unpainted header strip is left over.
            EventHandler fill = (s, e) =>
            {
                if (list.View != View.Details || list.Columns.Count == 0) return;
                var others = 0;
                for (var i = 0; i < list.Columns.Count - 1; i++) others += list.Columns[i].Width;
                var last = list.Columns[list.Columns.Count - 1];
                var width = Math.Max(60, list.ClientSize.Width - others);
                if (last.Width != width) last.Width = width;
            };
            list.Resize += fill;
            list.ColumnWidthChanged += (s, e) => { if (e.ColumnIndex != list.Columns.Count - 1) fill(s, e); };
            list.HandleCreated += fill;
            list.DrawColumnHeader += (s, e) =>
            {
                using (var back = new SolidBrush(Ground)) e.Graphics.FillRectangle(back, e.Bounds);
                using (var line = new Pen(Rule))
                {
                    e.Graphics.DrawLine(line, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
                    e.Graphics.DrawLine(line, e.Bounds.Right - 1, e.Bounds.Top + 4, e.Bounds.Right - 1, e.Bounds.Bottom - 5);
                }
                var text = new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height);
                TextRenderer.DrawText(e.Graphics, e.Header.Text, list.Font, text, Muted,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            };
        }

        private void SwitchTheme(bool dark)
        {
            ThemePreference.SaveDark(dark);
            _reopenStructure = _structureId;
            var doc = Autodesk.AutoCAD.ApplicationServices.Core.Application.DocumentManager.MdiActiveDocument;
            Close();
            if (doc != null) doc.SendStringToExecute("_FTFDIP ", true, false, false);
        }

        // ================================================================ chrome

        private Control BuildHeader()
        {
            var header = new Panel { Dock = DockStyle.Top, BackColor = Surface, Padding = new Padding(20, 10, 20, 10) };
            _header = header;
            header.Paint += (s, e) => e.Graphics.DrawLine(new Pen(Rule), 0, header.Height - 1, header.Width, header.Height - 1);

            _structureTitle = new Label { AutoSize = true, Text = "No structure selected", Font = F(15f, true), ForeColor = Ink };
            _pointInfo = new Label
            {
                AutoSize = true, ForeColor = Muted, Margin = new Padding(1, 2, 0, 0),
                Text = "Start with step 1: select the structure's survey point in the drawing."
            };
            var left = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Dock = DockStyle.Left };
            _headerText = left;
            left.Controls.Add(_structureTitle);
            left.Controls.Add(_pointInfo);

            _advancedToggle = new CheckBox { Text = "Show advanced options", AutoSize = true, Checked = ViewPreference.Load(), ForeColor = Ink };
            _advancedToggle.CheckedChanged += (s, e) =>
            {
                ViewPreference.Save(_advancedToggle.Checked);
                ApplyView();
            };
            _darkToggle = new CheckBox { Text = "Dark theme", AutoSize = true, Checked = Dark, ForeColor = Ink, Margin = new Padding(0, 3, 18, 3) };
            _darkToggle.CheckedChanged += (s, e) => SwitchTheme(_darkToggle.Checked);
            var right = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Right, WrapContents = false, Padding = new Padding(0, 12, 0, 0) };
            right.Controls.Add(_darkToggle);
            right.Controls.Add(_advancedToggle);

            header.Controls.Add(left);
            header.Controls.Add(right);
            header.Height = 76;
            return header;
        }

        private Control BuildStatusBar()
        {
            var bar = new Panel { Dock = DockStyle.Bottom, BackColor = Surface, Padding = new Padding(20, 5, 20, 5) };
            _status = new Label { Dock = DockStyle.Fill, ForeColor = Muted, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, Text = "Ready." };
            bar.Controls.Add(_status);
            bar.Height = Font.Height + bar.Padding.Vertical + 8;
            return bar;
        }

        /// <summary>Warnings for the selected structure, kept quiet and out of the way.
        /// It opens by itself only when something is actually wrong (an error).</summary>
        private Control BuildWarningsPanel()
        {
            var panel = new Panel { Dock = DockStyle.Bottom, BackColor = Surface, Padding = new Padding(20, 4, 20, 4) };
            panel.Paint += (s, e) => e.Graphics.DrawLine(new Pen(Rule), 0, 0, panel.Width, 0);
            _warningsPanel = panel;
            _warningsToggle = new LinkLabel
            {
                Dock = DockStyle.Top, AutoSize = false, Height = Font.Height + 8, LinkColor = Muted, ActiveLinkColor = Accent,
                LinkBehavior = LinkBehavior.HoverUnderline, TextAlign = ContentAlignment.MiddleLeft, Text = "No warnings"
            };
            _warningsToggle.LinkClicked += (s, e) => { _warningsOpen = !_warningsOpen; FitAll(); UpdateWarningsHeader(); };
            _warnings = new ListBox
            {
                Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, DrawMode = DrawMode.OwnerDrawVariable,
                BackColor = Surface, Font = F(9.75f, false)
            };
            _warnings.MeasureItem += (s, e) =>
            {
                var text = WarningText(_warnings.Items[e.Index]);
                var width = Math.Max(200, _warnings.ClientSize.Width - 24);
                e.ItemHeight = TextRenderer.MeasureText(text, _warnings.Font, new Size(width, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height + 4;
            };
            _warnings.DrawItem += (s, e) =>
            {
                if (e.Index < 0) return;
                var item = _warnings.Items[e.Index];
                var finding = item as QcFinding;
                var severity = finding != null ? finding.Severity : Severity.Info;
                var dot = severity == Severity.Error ? Bad : severity == Severity.Warning ? Warn : Rule;
                e.Graphics.FillRectangle(new SolidBrush(Surface), e.Bounds);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.FillEllipse(new SolidBrush(dot), new Rectangle(e.Bounds.X + 2, e.Bounds.Y + _warnings.Font.Height / 2 - 2, 6, 6));
                TextRenderer.DrawText(e.Graphics, WarningText(item), _warnings.Font,
                    new Rectangle(e.Bounds.X + 14, e.Bounds.Y, e.Bounds.Width - 14, e.Bounds.Height),
                    severity == Severity.Error ? Bad : Muted, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            };
            panel.Controls.Add(_warnings);
            panel.Controls.Add(_warningsToggle);
            return panel;
        }

        private Control BuildTabBar()
        {
            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false, BackColor = Ground, Padding = new Padding(16, 8, 16, 0) };
            var buttons = new List<Button>();
            Action mark = () =>
            {
                for (var i = 0; i < buttons.Count; i++)
                {
                    var on = i == Math.Max(0, _tabs.SelectedIndex);
                    buttons[i].BackColor = on ? Surface : Ground;
                    buttons[i].ForeColor = on ? Ink : Muted;
                    buttons[i].FlatAppearance.BorderColor = on ? Rule : Ground;
                    buttons[i].Font = on ? new Font(Face, 10.5f * 0.86f, FontStyle.Bold) : F(10.5f, false);
                }
            };
            for (var i = 0; i < _tabs.TabPages.Count; i++)
            {
                var index = i;
                var b = new Button
                {
                    Text = _tabs.TabPages[i].Text, AutoSize = true, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand,
                    Padding = new Padding(14, 4, 14, 4), Margin = new Padding(0, 0, 4, 0)
                };
                b.FlatAppearance.MouseOverBackColor = Surface;
                b.Click += (s, e) => _tabs.SelectedIndex = index;
                b.Paint += (s, e) =>
                {
                    if (index != Math.Max(0, _tabs.SelectedIndex)) return;
                    using (var accent = new SolidBrush(Accent)) e.Graphics.FillRectangle(accent, 0, b.Height - 3, b.Width, 3);
                };
                buttons.Add(b);
                bar.Controls.Add(b);
            }
            _tabs.SelectedIndexChanged += (s, e) => { mark(); foreach (var b in buttons) b.Invalidate(); };
            _tabs.HandleCreated += (s, e) => mark();
            mark();
            return bar;
        }

        private static string WarningText(object item)
        {
            var finding = item as QcFinding;
            return finding != null ? finding.Message : Convert.ToString(item);
        }

        private void UpdateWarningsHeader()
        {
            var count = _warnings.Items.Count;
            var errors = _warnings.Items.OfType<QcFinding>().Count(f => f.Severity == Severity.Error);
            _warningsToggle.Text = count == 0
                ? "No warnings for this structure"
                : (errors > 0 ? errors + " problem(s) need attention  -  " : string.Empty) +
                  count + " note(s) for this structure   " + (_warningsOpen ? "(hide)" : "(show)");
            _warningsToggle.LinkColor = errors > 0 ? Bad : Muted;
        }

        // ================================================================ layout

        private static Button Btn(string text, EventHandler click, bool primary = false)
        {
            var b = new Button
            {
                Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlatStyle = FlatStyle.Flat,
                BackColor = primary ? Accent : Surface, ForeColor = primary ? Color.White : Ink,
                Font = F(9.5f, true), Padding = new Padding(6, 0, 6, 0), Margin = new Padding(0, 0, 6, 6),
                Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderColor = primary ? Accent : ButtonBorder;
            b.FlatAppearance.MouseOverBackColor = primary ? AccentHover : AccentSoft;
            b.Click += click;
            return b;
        }

        private T Advanced<T>(T control) where T : Control
        {
            _advancedControls.Add(control);
            return control;
        }

        private void ApplyView()
        {
            var advanced = _advancedToggle != null && _advancedToggle.Checked;
            foreach (var c in _advancedControls) c.Visible = advanced;
            if (_grid != null)
            {
                foreach (var name in AdvancedColumns) _grid.Columns[name].Visible = advanced;
                foreach (var name in BasicOnlyColumns) _grid.Columns[name].Visible = !advanced;
                _grid.Columns["W"].HeaderText = advanced ? "Width (in)" : "Size (in)";
            }
            RefreshSizeFields();
            FitAll();
        }

        private readonly List<Action> _fitters = new List<Action>();
        private Panel _scroll;
        private Control _connectionGroup;

        /// <summary>
        /// One numbered step: a white card with its number, title, one line of plain
        /// guidance, and its controls. Sized from its contents, so it grows with larger
        /// Windows text and wrapping button rows. fillLines is how many text lines a
        /// grid, list or text box inside it gets.
        /// </summary>
        private Control Step(string number, string title, string guidance, Control content, int fillLines)
        {
            var card = new Panel { Dock = DockStyle.Top, BackColor = Surface, Padding = new Padding(18, 12, 18, 12) };
            card.Paint += (s, e) => e.Graphics.DrawRectangle(new Pen(Rule), 0, 0, card.Width - 1, card.Height - 1);

            var heading = new Label
            {
                Dock = DockStyle.Top, AutoSize = false, ForeColor = Ink, Font = F(12f, true),
                Text = title, TextAlign = ContentAlignment.MiddleLeft
            };
            if (number != null)
                heading.Paint += (s, e) =>
                {
                    // The step number sits in a small blue disc left of the title.
                    var size = heading.Font.Height;
                    var r = new Rectangle(0, (heading.Height - size) / 2, size, size);
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    e.Graphics.FillEllipse(new SolidBrush(Accent), r);
                    TextRenderer.DrawText(e.Graphics, number, F(9.5f, true), r, Color.White,
                                          TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                };
            var hint = new Label { Dock = DockStyle.Top, AutoSize = false, ForeColor = Muted, Font = F(10f, false), Text = guidance ?? string.Empty };
            content.Dock = DockStyle.Fill;

            card.Controls.Add(content);
            card.Controls.Add(hint);
            card.Controls.Add(heading);

            var spacer = new Panel { Dock = DockStyle.Top, Height = 12, BackColor = Ground };
            var wrapper = new Panel { Dock = DockStyle.Top, BackColor = Ground };
            wrapper.Controls.Add(card);
            wrapper.Controls.Add(spacer);

            _fitters.Add(() =>
            {
                var inner = Math.Max(120, card.ClientSize.Width - card.Padding.Horizontal);
                heading.Height = heading.Font.Height + 10;
                if (number != null) heading.Padding = new Padding(heading.Font.Height + 10, 0, 0, 0);
                hint.Height = string.IsNullOrEmpty(hint.Text) ? 0 : TextRenderer.MeasureText(hint.Text, hint.Font, new Size(inner, 0), TextFormatFlags.WordBreak).Height + 6;
                var need = ContentHeight(content, inner, fillLines);
                var height = card.Padding.Vertical + heading.Height + hint.Height + need + 2;
                if (card.Height != height) card.Height = height;
                if (wrapper.Height != height + spacer.Height) wrapper.Height = height + spacer.Height;
            });
            return wrapper;
        }

        private int ContentHeight(Control content, int inner, int fillLines)
        {
            var width = content.Width > 0 ? content.Width : inner;
            if (content is FlowLayoutPanel)
                return content.GetPreferredSize(new Size(width, 0)).Height;
            var need = 0;
            foreach (Control c in content.Controls)
            {
                if (c.Dock != DockStyle.Top || !c.Visible) continue;
                need += c is FlowLayoutPanel ? c.GetPreferredSize(new Size(width, 0)).Height : c.Height;
            }
            return need + fillLines * (Font.Height + 10);
        }

        private bool _fitting;

        private void FitAll()
        {
            if (_fitting || _notes == null) return;
            _fitting = true;
            try
            {
                _notes.Height = _notes.Font.Height * 6 + 6;
                var total = 0;
                for (var i = 0; i < _diagnostics.Items.Count; i++) total += _diagnostics.GetItemHeight(i);
                _diagnostics.Height = _diagnostics.Items.Count == 0 ? 0 : Math.Min(total, Font.Height * 9) + 4;
                _diagnostics.Visible = _diagnostics.Items.Count > 0;
                _grid.RowTemplate.Height = Font.Height + 12;
                foreach (DataGridViewRow row in _grid.Rows) row.Height = Font.Height + 12;

                var hasCandidates = _candidateList.Items.Count > 0;
                _candidateList.Visible = _candidateCaption.Visible = hasCandidates;
                _candidateList.Height = hasCandidates ? (Font.Height + 8) * Math.Min(4, _candidateList.Items.Count + 1) + 8 : 0;
                _candidateCaption.Height = hasCandidates ? Font.Height + 10 : 0;

                foreach (var fit in _fitters) fit();
                if (_header != null)
                {
                    var need = _headerText.GetPreferredSize(Size.Empty).Height + _header.Padding.Vertical + 2;
                    if (_header.Height != need) _header.Height = need;
                }
                if (_side != null && _side.Parent != null)
                {
                    // The right-hand panel takes about a third of the window, within limits.
                    var width = Math.Max(Font.Height * 15, Math.Min(Font.Height * 24, _side.Parent.ClientSize.Width * 30 / 100));
                    if (_side.Width != width) _side.Width = width;
                }
                if (_warningsPanel != null)
                {
                    var listHeight = 0;
                    if (_warningsOpen)
                        for (var i = 0; i < _warnings.Items.Count; i++) listHeight += _warnings.GetItemHeight(i);
                    var height = _warningsToggle.Height + _warningsPanel.Padding.Vertical + Math.Min(listHeight, Font.Height * 7);
                    if (_warningsPanel.Height != height) _warningsPanel.Height = height;
                }
            }
            finally { _fitting = false; }
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            FitAll();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            FitAll();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            FitAll();
        }

        private static FlowLayoutPanel Row(params Control[] controls)
        {
            var f = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Dock = DockStyle.Top, BackColor = Surface };
            f.Controls.AddRange(controls);
            return f;
        }

        private static Label Caption(string text, int leftMargin)
        {
            return new Label { Text = text, AutoSize = true, Margin = new Padding(leftMargin, 7, 6, 0) };
        }

        private static Label Hint(string text)
        {
            return new Label { Text = text, AutoSize = true, ForeColor = Muted, Font = F(10f, false), Margin = new Padding(0, 7, 0, 0) };
        }

        private static TextBox Box(int width)
        {
            return new TextBox { Width = width, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 3, 6, 8), Font = F(10.5f, true) };
        }

        private TabPage BuildStructureTab()
        {
            var page = new TabPage("Build") { BackColor = Ground };
            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(16, 6, 8, 16), BackColor = Ground };
            _scroll = scroll;
            scroll.Resize += (s, e) => FitAll();

            // Steps are added bottom-up because each docks to the top.

            // advanced: typed notes ------------------------------------------
            _notes = new TextBox
            {
                Multiline = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 11f),
                Dock = DockStyle.Top, BorderStyle = BorderStyle.FixedSingle
            };
            _diagnostics = new ListBox
            {
                Dock = DockStyle.Top, HorizontalScrollbar = false, BorderStyle = BorderStyle.None,
                DrawMode = DrawMode.OwnerDrawVariable, BackColor = Surface, Font = F(9.75f, false)
            };
            _diagnostics.DrawItem += DrawDiagnostic;
            _diagnostics.MeasureItem += (s, e) =>
            {
                var text = Convert.ToString(_diagnostics.Items[e.Index]);
                var width = Math.Max(200, _diagnostics.ClientSize.Width - 24);
                e.ItemHeight = TextRenderer.MeasureText(text, _diagnostics.Font, new Size(width, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height + 6;
            };
            var notesPanel = new Panel { BackColor = Surface };
            notesPanel.Controls.Add(_diagnostics);
            notesPanel.Controls.Add(Row(Btn("Read notes", OnReadNotes), Btn("Import notes file...", OnImportFile)));
            notesPanel.Controls.Add(_notes);
            scroll.Controls.Add(Advanced(Step(null, "Typed field notes",
                "Only when the notes were typed up electronically. PT 1045 SDMH / BOT 7.82 / 12 RCP N 6.41",
                notesPanel, 0)));

            // 3 connections ---------------------------------------------------
            _connections = new ListView
            {
                Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false,
                BorderStyle = BorderStyle.FixedSingle, Font = F(10f, true)
            };
            _connections.Columns.Add("Pipe", 170);
            _connections.Columns.Add("MD (ft)", 80);
            _connections.Columns.Add("Runs to", 170);
            _connections.Columns.Add("Status", 220);
            _connections.SelectedIndexChanged += (s, e) =>
            {
                if (_syncing || _connections.SelectedItems.Count == 0) return;
                _syncing = true;
                try { SelectRow(_connections.SelectedItems[0].Tag as string); }
                finally { _syncing = false; }
            };

            _candidateList = new ListView
            {
                Dock = DockStyle.Top, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false,
                BorderStyle = BorderStyle.FixedSingle, Font = F(10f, false)
            };
            foreach (var h in new[] { "Runs to", "Distance", "Off line", "Pipe observed there", "Confidence", "Why" })
                _candidateList.Columns.Add(h, h == "Why" ? 420 : h == "Pipe observed there" ? 160 : 95);
            _candidateCaption = new Label { Dock = DockStyle.Top, AutoSize = false, ForeColor = Ink, TextAlign = ContentAlignment.BottomLeft };
            _connectionState = new Label { Dock = DockStyle.Top, AutoSize = false, ForeColor = Muted, Font = F(10f, false), Height = 26, TextAlign = ContentAlignment.MiddleLeft };

            var connPanel = new Panel { BackColor = Surface };
            connPanel.Controls.Add(_connections);
            connPanel.Controls.Add(_candidateList);
            connPanel.Controls.Add(_candidateCaption);
            connPanel.Controls.Add(_connectionState);
            connPanel.Controls.Add(Row(
                Btn("Find connections", OnFindConnections, true),
                Btn("Confirm selected", OnConfirm),
                Btn("Pick a different structure...", OnManualPick),
                Btn("Leave unresolved", OnLeaveUnresolved),
                Btn("Runs outside survey limits", OnOutsideLimits),
                Btn("Draw this structure's pipes", (s, e) => OnDraw(false)),
                Advanced(Btn("Draw all confirmed pipes", (s, e) => OnDraw(true)))));
            _connectionGroup = Step("3", "Connections",
                "Select a pipe and find where it runs. Nothing connects until you confirm. The list shows each pipe and where it goes.",
                connPanel, 5);
            scroll.Controls.Add(_connectionGroup);

            // 2 pipes -----------------------------------------------------------
            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                BackgroundColor = Surface,
                BorderStyle = BorderStyle.FixedSingle,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                GridColor = Rule,
                EnableHeadersVisualStyles = false,
                ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            };
            _grid.ColumnHeadersDefaultCellStyle.BackColor = Ground;
            _grid.ColumnHeadersDefaultCellStyle.ForeColor = Muted;
            _grid.ColumnHeadersDefaultCellStyle.Font = F(9.5f, true);
            _grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(4, 6, 4, 6);
            _grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Ground;
            _grid.DefaultCellStyle.Font = F(10.5f, true);
            _grid.DefaultCellStyle.Padding = new Padding(4, 0, 4, 0);
            _grid.DefaultCellStyle.SelectionBackColor = AccentSoft;
            _grid.DefaultCellStyle.SelectionForeColor = Ink;
            _grid.DefaultCellStyle.ForeColor = Ink;

            AddText("W", "Size (in)", 55);
            AddText("H", "Height (in)", 55);
            AddCombo("Shape", "Shape", Enum.GetNames(typeof(PipeShape)), 70);
            AddText("Material", "Material", 70);
            AddText("Direction", "Direction", 65);
            AddText("Dip", "MD (ft)", 60);
            _grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Top", HeaderText = "Top of pipe", FillWeight = 55, MinimumWidth = 60 });
            AddCombo("Reference", "Measured to", Enum.GetNames(typeof(MeasurementReference)), 90, ReferenceWords);
            AddText("Calc", "Elevation", 110, true);
            AddText("RefStatus", "Reference basis", 130, true);
            AddCombo("Role", "In / Out", Enum.GetNames(typeof(FlowRole)), 60, n => n == "Unknown" ? "-" : n.ToUpperInvariant());
            AddText("Conditions", "Conditions", 90);
            AddText("Notes", "Notes", 90);
            AddText("Connection", "Connection", 150, true);
            AddText("Source", "Source", 70, true);
            _grid.DataError += (s, e) => { e.ThrowException = false; };
            _grid.SelectionChanged += (s, e) =>
            {
                _candidates = new List<ConnectionCandidate>();
                _candidateList.Items.Clear();
                ShowConnectionState();
                SyncConnectionSelection();
                if (_map != null) _map.Invalidate();
                FitAll();
            };

            // Edits save when the cell is left. Drop-downs and the check box save at once.
            _grid.CurrentCellDirtyStateChanged += (s, e) =>
            {
                if (_grid.IsCurrentCellDirty && (_grid.CurrentCell is DataGridViewComboBoxCell || _grid.CurrentCell is DataGridViewCheckBoxCell))
                    _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            _grid.CellValueChanged += (s, e) =>
            {
                if (!_loading && e.RowIndex >= 0) SaveRow(e.RowIndex, _grid.Columns[e.ColumnIndex].Name);
                if (!_loading) UpdateLivePreview();
            };
            // While a value is being typed the label on the right follows it.
            _grid.EditingControlShowing += (s, e) =>
            {
                e.Control.TextChanged -= OnTyping;
                e.Control.TextChanged += OnTyping;
            };

            var pipePanel = new Panel { BackColor = Surface };
            pipePanel.Controls.Add(_grid);
            pipePanel.Controls.Add(Row(
                Btn("Add pipe", OnAddPipe),
                Btn("Delete pipe", OnDeletePipe),
                Advanced(Btn("Confirm unmarked dips as inverts", (s, e) => OnConfirmInvert(true))),
                Advanced(Btn("Move up", (s, e) => MovePipe(-1))),
                Advanced(Btn("Move down", (s, e) => MovePipe(1)))));
            scroll.Controls.Add(Step("2", "Pipes",
                "Type each pipe from the field book. MD is the invert unless you tick Top of pipe. Changes save when you leave a cell.",
                pipePanel, 6));

            // 1 structure -------------------------------------------------------
            _type = new ComboBox { Width = 110, Margin = new Padding(0, 3, 6, 8) };
            _system = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110, Margin = new Padding(0, 3, 6, 8) };
            _system.Items.AddRange(Enum.GetNames(typeof(UtilitySystem)));
            _type.SelectionChangeCommitted += (s, e) => BeginInvoke(new Action(SaveStructureFields));
            _type.Leave += (s, e) => SaveStructureFields();
            _system.SelectionChangeCommitted += (s, e) => BeginInvoke(new Action(SaveStructureFields));

            _bottom = Box(64);
            _water = Box(64);
            _insideWidth = Box(56);
            _insideLength = Box(56);
            _sizeCaption = Caption("Diameter (in)", 0);
            _lengthCaption = Caption("x", 0);
            _calcDips = new Label { AutoSize = true, ForeColor = Muted, Font = F(10f, false), Margin = new Padding(6, 7, 0, 0) };
            _insideSource = new Label { AutoSize = true, ForeColor = Muted, Font = F(9.5f, false), Margin = new Padding(0, 7, 0, 0) };
            foreach (var box in new[] { _bottom, _water, _insideWidth, _insideLength })
            {
                box.Leave += (s, e) => SaveStructureFields();
                box.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; SaveStructureFields(); } };
                box.TextChanged += OnTyping;
            }

            var structurePanel = new Panel { BackColor = Surface };
            // The size caption and its boxes move as one piece, so the label always sits
            // in front of its box.
            _sizeGroup = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Surface, Margin = Padding.Empty };
            _sizeGroup.Controls.AddRange(new Control[] { _sizeCaption, _insideWidth, _lengthCaption, _insideLength });
            structurePanel.Controls.Add(Row(
                _sizeGroup,
                Caption("Bottom MD (ft)", 14), _bottom,
                Caption("Water MD (ft)", 10), _water,
                _calcDips,
                Advanced(_insideSource)));
            structurePanel.Controls.Add(Row(
                Btn("Select structure point...", OnPickStructure, true),
                Advanced(Caption("Type", 12)), Advanced(_type),
                Advanced(Caption("System", 6)), Advanced(_system),
                Advanced(Btn("Reload from drawing", (s, e) => { DipSession.Reload(); DipSession.Post("reload", (db, tr, ed, p, st, v) => false); }))));
            scroll.Controls.Add(Step("1", "Structure",
                "Pick the structure's survey point; rim, position and description come from the drawing. Then enter its size and the bottom and water MDs.",
                structurePanel, 0));

            _side = BuildSidePanel();
            page.Controls.Add(scroll);
            page.Controls.Add(_side);
            return page;
        }

        /// <summary>The leader label as it will be drawn, and the local connection map.</summary>
        private Panel BuildSidePanel()
        {
            var side = new Panel { Dock = DockStyle.Right, BackColor = Ground, Padding = new Padding(8, 6, 16, 16), Width = 420 };

            var labelCard = new Panel { Dock = DockStyle.Fill, BackColor = Surface, Padding = new Padding(16, 12, 16, 12) };
            labelCard.Paint += (s, e) => e.Graphics.DrawRectangle(new Pen(Rule), 0, 0, labelCard.Width - 1, labelCard.Height - 1);
            var labelTitle = new Label { Dock = DockStyle.Top, Text = "Structure label", Font = F(12f, true), Height = 28 };
            _labelState = new Label { Dock = DockStyle.Top, ForeColor = Muted, Font = F(10f, false), Height = 24, Text = "Updates as you type." };
            _labelPreview = new TextBox
            {
                Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, WordWrap = false,
                BorderStyle = BorderStyle.FixedSingle, Font = new Font("Consolas", 11.5f, FontStyle.Bold), BackColor = PreviewBack, ForeColor = Ink
            };
            _labelPreview.TextChanged += (s, e) =>
            {
                if (_settingPreview || _generatedLabel == null) return;
                _labelEdited = _labelPreview.Text != _generatedLabel;
                _labelState.Text = _labelEdited ? "Edited by hand -- live updates paused." : "Updates as you type.";
                _labelState.ForeColor = _labelEdited ? Warn : Muted;
            };
            var labelButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, BackColor = Surface, Padding = new Padding(0, 8, 0, 0) };
            labelButtons.Controls.Add(Btn("Place structure label...", OnPlaceLabel, true));
            labelButtons.Controls.Add(Btn("Regenerate label", (s, e) => { _labelEdited = false; UpdateLivePreview(); }));
            labelCard.Controls.Add(_labelPreview);
            labelCard.Controls.Add(labelButtons);
            labelCard.Controls.Add(_labelState);
            labelCard.Controls.Add(labelTitle);

            var mapCard = new Panel { Dock = DockStyle.Fill, BackColor = Surface, Padding = new Padding(12, 10, 12, 10) };
            mapCard.Paint += (s, e) => e.Graphics.DrawRectangle(new Pen(Rule), 0, 0, mapCard.Width - 1, mapCard.Height - 1);
            var mapTitle = new Label { Dock = DockStyle.Top, Text = "Nearby connections", Font = F(12f, true), Height = 28 };
            _map = new ConnectionMapView(() => DipSession.Project, () => _structureId, () => SelectedPipeId()) { Dock = DockStyle.Fill };
            _map.StructureClicked += OpenStructure;
            mapCard.Controls.Add(_map);
            mapCard.Controls.Add(mapTitle);

            var split = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Ground };
            split.RowStyles.Add(new RowStyle(SizeType.Percent, 53));
            split.RowStyles.Add(new RowStyle(SizeType.Absolute, 12));
            split.RowStyles.Add(new RowStyle(SizeType.Percent, 47));
            labelCard.Margin = Padding.Empty;
            mapCard.Margin = Padding.Empty;
            split.Controls.Add(labelCard, 0, 0);
            split.Controls.Add(new Panel { Margin = Padding.Empty, BackColor = Ground }, 0, 1);
            split.Controls.Add(mapCard, 0, 2);
            side.Controls.Add(split);
            return side;
        }

        /// <summary>All structures and connections before anything is drawn.</summary>
        private TabPage BuildMapTab()
        {
            var page = new TabPage("Map") { BackColor = Ground, Padding = new Padding(16) };
            var card = new Panel { Dock = DockStyle.Fill, BackColor = Surface, Padding = new Padding(12) };
            card.Paint += (s, e) => e.Graphics.DrawRectangle(new Pen(Rule), 0, 0, card.Width - 1, card.Height - 1);
            _projectMap = new ConnectionMapView(() => DipSession.Project, () => _structureId, () => SelectedPipeId()) { Dock = DockStyle.Fill, ShowAll = true };
            _projectMap.StructureClicked += OpenStructure;
            card.Controls.Add(_projectMap);

            _projectSummary = new Label { AutoSize = true, ForeColor = Muted, Font = F(10f, false), Margin = new Padding(8, 7, 0, 0) };
            var row = Row(
                Btn("Draw all confirmed pipes", (s, e) => OnDraw(true), true),
                Btn("Label all structures", OnLabelAll),
                Btn("Refresh", (s, e) => { RefreshMapSummary(); _projectMap.Invalidate(); }),
                _projectSummary);
            row.BackColor = Ground;
            row.Padding = new Padding(0, 0, 0, 6);

            page.Controls.Add(card);
            page.Controls.Add(row);
            return page;
        }

        private void OpenStructure(string structureId)
        {
            if (structureId == null) return;
            _structureId = structureId;
            _labelEdited = false;
            _candidates = new List<ConnectionCandidate>();
            _candidateList.Items.Clear();
            _map.Candidates = null;
            _tabs.SelectedIndex = 0;
            RefreshFromSession();
        }

        private void RefreshMapSummary()
        {
            var project = DipSession.Project;
            if (project == null || _projectSummary == null) return;
            var pipes = project.Structures.Sum(s => s.Field.Pipes.Count);
            var connected = project.Structures.Sum(s => s.Field.Pipes.Count(p =>
            {
                var c = project.ConnectionFor(s.Id, p.Id);
                return c != null && c.IsAccepted;
            }));
            var drawn = project.Connections.Count(c => c.Drafted);
            _projectSummary.Text = string.Format(CultureInfo.InvariantCulture,
                "{0} structures   {1} of {2} pipe ends connected   {3} runs drawn   --   click a structure to open it",
                project.Structures.Count, connected, pipes, drawn);
        }

        private void DrawDiagnostic(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            var text = Convert.ToString(_diagnostics.Items[e.Index]);
            var color = text.StartsWith("Error", StringComparison.Ordinal) ? Bad
                      : text.StartsWith("Warning", StringComparison.Ordinal) ? Warn
                      : Muted;
            e.Graphics.FillRectangle(new SolidBrush(Surface), e.Bounds);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.FillEllipse(new SolidBrush(color), new Rectangle(e.Bounds.X + 2, e.Bounds.Y + _diagnostics.Font.Height / 2 - 2, 6, 6));
            var textBounds = new Rectangle(e.Bounds.X + 14, e.Bounds.Y + 1, e.Bounds.Width - 14, e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, text, _diagnostics.Font, textBounds, Muted,
                                  TextFormatFlags.WordBreak | TextFormatFlags.Left | TextFormatFlags.NoPrefix);
        }

        private void AddText(string name, string header, int weight, bool calculated = false)
        {
            var column = new DataGridViewTextBoxColumn { Name = name, HeaderText = header, ReadOnly = calculated, FillWeight = weight, MinimumWidth = 50 };
            if (calculated) column.DefaultCellStyle.BackColor = Calculated;
            _grid.Columns.Add(column);
        }

        private sealed class Choice
        {
            public string Value { get; set; }
            public string Display { get; set; }
        }

        /// <summary>A drop-down column that stores the internal value and shows plain words.</summary>
        private void AddCombo(string name, string header, string[] items, int weight, Func<string, string> words = null)
        {
            var column = new DataGridViewComboBoxColumn
            {
                Name = name, HeaderText = header, FlatStyle = FlatStyle.Flat, FillWeight = weight, MinimumWidth = 60,
                DisplayStyle = DataGridViewComboBoxDisplayStyle.Nothing,
                DataSource = items.Select(i => new Choice { Value = i, Display = words != null ? words(i) : i }).ToList(),
                ValueMember = "Value", DisplayMember = "Display"
            };
            _grid.Columns.Add(column);
        }

        private static string ReferenceWords(string name)
        {
            switch (name)
            {
                case "Unspecified": return "Not stated";
                case "TopOfPipe": return "Top of pipe";
                case "BottomOfStructure": return "Bottom of structure";
                case "WaterLevel": return "Water level";
                case "TopOfGrate": return "Top of grate";
                case "TopOfCasting": return "Top of casting";
                default: return name;
            }
        }

        internal static string StatusWords(ConnectionStatus status)
        {
            switch (status)
            {
                case ConnectionStatus.Confirmed: return "Confirmed";
                case ConnectionStatus.ManualOverride: return "Picked by drafter";
                case ConnectionStatus.LeftUnresolved: return "Left unresolved";
                case ConnectionStatus.Probable: return "Suggested";
                case ConnectionStatus.OutsideSurveyLimits: return "Outside survey limits";
                default: return "Not connected";
            }
        }

        private TabPage BuildReviewTab()
        {
            var page = new TabPage("Review") { BackColor = Ground, Padding = new Padding(16) };
            _summary = new Label { Dock = DockStyle.Top, Height = 40, Font = F(12f, true), ForeColor = Ink, TextAlign = ContentAlignment.MiddleLeft };
            _findings = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, BorderStyle = BorderStyle.FixedSingle, Font = F(10f, false) };
            _findings.Columns.Add("Severity", 90);
            _findings.Columns.Add("Check", 170);
            _findings.Columns.Add("Structure", 120);
            _findings.Columns.Add("Finding", 700);
            _findings.DoubleClick += OnFindingClicked;

            page.Controls.Add(_findings);
            var row = Row(
                Btn("Check for changed survey points / rebuild...", OnRebuild),
                Btn("Refresh", (s, e) => RefreshReview()),
                Hint("Double-click a finding to zoom to it in the drawing."));
            row.BackColor = Ground;
            page.Controls.Add(row);
            page.Controls.Add(_summary);
            return page;
        }

        private TabPage BuildRevisitTab()
        {
            var page = new TabPage("Field Revisit") { BackColor = Ground, Padding = new Padding(16) };
            _revisit = new TextBox
            {
                Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both,
                Font = new Font("Consolas", 11f), BackColor = Surface, BorderStyle = BorderStyle.FixedSingle
            };
            _revisitAdd = Box(420);
            page.Controls.Add(_revisit);
            var row = Row(
                Btn("Copy for the crew", (s, e) => { if (_revisit.Text.Length > 0) Clipboard.SetText(_revisit.Text); Say("Revisit list copied.", Good); }, true),
                Btn("Export beside the drawing", OnExportRevisit),
                Advanced(Caption("Add a note for this structure:", 12)),
                Advanced(_revisitAdd),
                Advanced(Btn("Add", OnAddRevisit)));
            row.BackColor = Ground;
            page.Controls.Add(row);
            return page;
        }

        // ============================================================ refreshing

        private StructureRecord Current
        {
            get { return DipSession.Project != null && _structureId != null ? DipSession.Project.Structure(_structureId) : null; }
        }

        private PipeObservation SelectedPipe
        {
            get
            {
                var s = Current;
                if (s == null || _grid.CurrentRow == null) return null;
                var id = _grid.CurrentRow.Tag as string;
                return s.Field.Pipes.FirstOrDefault(p => p.Id == id);
            }
        }

        public void RefreshFromSession()
        {
            _settings = null;
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc != null)
            {
                try
                {
                    var rules = FtfSession.Rules(doc.Database);
                    _settings = FtfSession.SettingsFor(doc.Database, rules);
                }
                catch (ConfigException) { }
            }
            if (_settings == null) _settings = new FtfSettings();

            if (_type.Items.Count == 0)
                foreach (var code in _settings.Dips.StructureCodes) _type.Items.Add(code.Code);

            if (Current == null && DipSession.Project != null && DipSession.Project.Structures.Count > 0 && _structureId != null)
                _structureId = null;

            RefreshStructure();
            RefreshReview();
            RefreshMapSummary();
            if (DipSession.LastMessage != null) Say(DipSession.LastMessage, Bad);
            FitAll();
            _map.Invalidate();
            _projectMap.Invalidate();
        }

        private void RefreshSizeFields()
        {
            if (_sizeCaption == null) return;
            var s = Current;
            var shape = s != null && _settings != null ? StructureDimensions.ShapeOf(s, _settings.Dips) : StructureShape.Round;
            var sized = s != null && shape != StructureShape.NoSize;
            _sizeGroup.Visible = sized;
            _sizeCaption.Text = shape == StructureShape.Rectangular ? "Inside W x L (in)" : "Diameter (in)";
            _lengthCaption.Visible = _insideLength.Visible = shape == StructureShape.Rectangular;
        }

        private void RefreshStructure()
        {
            var s = Current;
            _loading = true;
            try
            {
                RefreshSizeFields();
                if (s == null)
                {
                    _grid.Rows.Clear();
                    _connections.Items.Clear();
                    _structureTitle.Text = "No structure selected";
                    _pointInfo.Text = "Start with step 1: select the structure's survey point in the drawing.";
                    _bottom.Text = _water.Text = _calcDips.Text = _insideWidth.Text = _insideLength.Text = _insideSource.Text = string.Empty;
                    SetPreview(string.Empty);
                    RefreshWarnings();
                    return;
                }

                _structureTitle.Text = s.Label;
                _pointInfo.Text = s.Cad == null
                    ? "No survey point in the drawing"
                    : string.Format(CultureInfo.InvariantCulture, "RIM {3:0.00}      N {1:0.00}   E {2:0.00}      Description \"{4}\"      {5}   (point {0})",
                                    s.Field.PointNumber, s.Cad.Northing, s.Cad.Easting, s.Cad.Rim, s.Cad.Description, s.System);
                if (!_type.Focused) _type.Text = s.StructureType ?? string.Empty;
                _system.SelectedItem = s.System.ToString();

                // A box the drafter is typing in is never overwritten by a refresh.
                if (!_bottom.Focused) _bottom.Text = Num(s.Field.BottomDip);
                if (!_water.Focused) _water.Text = Num(s.Field.WaterDip);
                var bottom = DipElevations.Bottom(s);
                var water = DipElevations.Water(s);
                _calcDips.Text = (bottom != null ? "BOT " + bottom.Value.ToString("0.00", CultureInfo.InvariantCulture) + "   " : string.Empty) +
                                 (water != null ? "WL " + water.Value.ToString("0.00", CultureInfo.InvariantCulture) : string.Empty);

                if (!_insideWidth.Focused) _insideWidth.Text = Num(s.EnteredInsideWidthIn);
                if (!_insideLength.Focused) _insideLength.Text = Num(s.EnteredInsideLengthIn);
                DimensionSource widthSource;
                var width = StructureDimensions.InsideWidth(s, _settings != null ? _settings.Dips : null, out widthSource);
                _insideSource.Text = width.HasValue
                    ? "Using " + Num(width) + "\" (" + StructureDimensions.Describe(widthSource) + ")"
                    : string.Empty;

                // Rows are updated in place when they are still the same pipes, so a
                // refresh never throws away the row or the cell being worked on.
                var sameRows = _grid.Rows.Count == s.Field.Pipes.Count &&
                               s.Field.Pipes.Select((p, i) => (_grid.Rows[i].Tag as string) == p.Id).All(x => x);
                if (!sameRows)
                {
                    var keep = SelectedPipeId();
                    _grid.Rows.Clear();
                    foreach (var p in s.Field.Pipes)
                    {
                        var index = _grid.Rows.Add();
                        _grid.Rows[index].Tag = p.Id;
                    }
                    SelectRow(keep);
                }

                for (var i = 0; i < s.Field.Pipes.Count; i++)
                    FillRow(_grid.Rows[i], s, s.Field.Pipes[i]);

                RefreshConnectionsList(s);
            }
            finally
            {
                _loading = false;
            }

            UpdateLivePreview();
            ShowConnectionState();
            RefreshWarnings();
        }

        private void RefreshConnectionsList(StructureRecord s)
        {
            var keep = SelectedPipeId();
            _connections.BeginUpdate();
            _connections.Items.Clear();
            foreach (var p in s.Field.Pipes)
            {
                var c = DipSession.Project.ConnectionFor(s.Id, p.Id);
                StructureRecord other = null;
                if (c != null) other = DipSession.Project.Structure(c.FromStructureId == s.Id ? c.ToStructureId : c.FromStructureId);
                var accepted = c != null && c.IsAccepted;
                var item = new ListViewItem(new[]
                {
                    ConnectionFinder.Describe(p),
                    Num(p.MeasuredDip),
                    other != null ? other.Label : "-",
                    c == null ? "Not connected" : StatusWords(c.Status) + (c.Drafted ? ", drawn" : string.Empty)
                }) { Tag = p.Id, UseItemStyleForSubItems = false };
                item.SubItems[3].ForeColor = accepted ? Good : c != null && c.Status == ConnectionStatus.LeftUnresolved ? Warn : Muted;
                _connections.Items.Add(item);
            }
            _connections.EndUpdate();
            SyncConnectionSelection(keep);
        }

        private void SyncConnectionSelection(string pipeId = null)
        {
            if (_syncing || _connections == null) return;
            pipeId = pipeId ?? SelectedPipeId();
            _syncing = true;
            try
            {
                foreach (ListViewItem item in _connections.Items)
                    item.Selected = (item.Tag as string) == pipeId;
            }
            finally { _syncing = false; }
        }

        private void RefreshWarnings()
        {
            if (_warnings == null) return;
            _warnings.BeginUpdate();
            _warnings.Items.Clear();
            var s = Current;
            if (s != null && _settings != null && DipSession.Project != null)
            {
                foreach (var f in UtilityQc.Evaluate(DipSession.Project, _settings.Dips)
                                           .Where(f => f.StructureId == s.Id)
                                           .OrderByDescending(f => f.Severity))
                    _warnings.Items.Add(f);
            }
            _warnings.EndUpdate();
            // Only a real problem opens the panel by itself.
            if (_warnings.Items.OfType<QcFinding>().Any(f => f.Severity == Severity.Error)) _warningsOpen = true;
            UpdateWarningsHeader();
            FitAll();
        }

        private void FillRow(DataGridViewRow row, StructureRecord s, PipeObservation p)
        {
            var elevation = DipElevations.Pipe(s, p);
            var c = DipSession.Project.ConnectionFor(s.Id, p.Id);
            string connection = "not connected";
            if (c != null)
            {
                var otherId = c.FromStructureId == s.Id ? c.ToStructureId : c.FromStructureId;
                var other = DipSession.Project.Structure(otherId);
                connection = StatusWords(c.Status) + (other != null ? " -> " + other.Label : string.Empty) + (c.Drafted ? " (drawn)" : string.Empty);
            }

            string calc;
            if (elevation == null) calc = "-";
            else if (p.ReferenceUnconfirmed)
                calc = elevation.Value.ToString("0.00", CultureInfo.InvariantCulture) + "  (assumed " +
                       DipElevations.Describe(_settings.Dips.AssumedPipeReference) + "?)";
            else
                calc = elevation.Value.ToString("0.00", CultureInfo.InvariantCulture) + " " + DipElevations.Describe(p.Reference);

            SetCell(row, "W", Num(p.WidthIn));
            SetCell(row, "H", Num(p.HeightIn));
            SetCell(row, "Shape", p.Shape.ToString());
            SetCell(row, "Material", p.Material ?? string.Empty);
            SetCell(row, "Direction", p.Direction != null ? p.Direction.Text ?? string.Empty : string.Empty);
            SetCell(row, "Dip", Num(p.MeasuredDip));
            SetCheck(row, "Top", p.Reference == MeasurementReference.TopOfPipe);
            SetCell(row, "Reference", p.Reference.ToString());
            SetCell(row, "Calc", calc);
            SetCell(row, "RefStatus", ReferenceStatus(p));
            SetCell(row, "Role", p.Role.ToString());
            SetCell(row, "Conditions", string.Join(", ", p.Conditions.ToArray()));
            SetCell(row, "Notes", p.Notes ?? string.Empty);
            SetCell(row, "Connection", connection);
            SetCell(row, "Source", p.Source.ToString());
            row.ErrorText = string.Empty;

            var flag = p.ReferenceUnconfirmed && p.MeasuredDip.HasValue;
            foreach (var column in new[] { "Reference", "Calc", "RefStatus" })
                row.Cells[column].Style.BackColor = flag ? Unconfirmed : Color.Empty;
        }

        private void SetCell(DataGridViewRow row, string column, string value)
        {
            var cell = row.Cells[column];
            if (_grid.IsCurrentCellInEditMode && _grid.CurrentCell == cell) return;
            if (!Equals(cell.Value as string, value)) cell.Value = value;
        }

        private void SetCheck(DataGridViewRow row, string column, bool value)
        {
            var cell = row.Cells[column];
            if (_grid.IsCurrentCellInEditMode && _grid.CurrentCell == cell) return;
            if (!(cell.Value is bool) || (bool)cell.Value != value) cell.Value = value;
        }

        private string SelectedPipeId()
        {
            return _grid != null && _grid.CurrentRow != null ? _grid.CurrentRow.Tag as string : null;
        }

        private void SelectRow(string pipeId)
        {
            foreach (DataGridViewRow row in _grid.Rows)
                if ((row.Tag as string) == pipeId)
                {
                    _grid.CurrentCell = row.Cells["Material"];
                    return;
                }
        }

        // ============================================================ live label

        private void OnTyping(object sender, EventArgs e)
        {
            if (!_loading) UpdateLivePreview();
        }

        private void SetPreview(string text)
        {
            _settingPreview = true;
            try { _labelPreview.Text = text; }
            finally { _settingPreview = false; }
        }

        /// <summary>
        /// Rebuilds the structure label from what is on screen right now -- including a
        /// value still being typed -- on a throwaway copy of the data. Nothing is saved
        /// by this; it only shows what the label will say.
        /// </summary>
        private void UpdateLivePreview()
        {
            var s = Current;
            if (s == null || _settings == null || _labelPreview == null) return;
            if (_labelEdited) return;

            var copy = UtilityProject.FromJson(DipSession.Project.ToJson());
            var structure = copy.Structure(s.Id);
            if (structure == null) return;

            double? bottom, water, width, length;
            var ignore = new List<string>();
            if (TryDip(_bottom.Text, "", ignore, out bottom)) structure.Field.BottomDip = bottom;
            if (TryDip(_water.Text, "", ignore, out water)) structure.Field.WaterDip = water;
            if (TryNumber(_insideWidth.Text, "", ignore, out width)) structure.EnteredInsideWidthIn = width;
            if (TryNumber(_insideLength.Text, "", ignore, out length)) structure.EnteredInsideLengthIn = length;

            foreach (DataGridViewRow row in _grid.Rows)
            {
                var pipe = structure.Field.Pipes.FirstOrDefault(p => p.Id == (row.Tag as string));
                if (pipe == null) continue;
                double? size, dip;
                if (TryNumber(Live(row, "W"), "", ignore, out size)) { pipe.WidthIn = size; pipe.HeightIn = size; }
                var material = Live(row, "Material").Trim().ToUpperInvariant();
                pipe.Material = material.Length == 0 ? null : material;
                var directionText = Live(row, "Direction").Trim();
                var direction = directionText.Length == 0 ? ObservedDirection.Unknown("?") : DipNoteParser.ParseDirection(directionText);
                if (direction != null) pipe.Direction = direction;
                if (TryDip(Live(row, "Dip"), "", ignore, out dip)) pipe.MeasuredDip = dip;
                var top = row.Cells["Top"].Value is bool && (bool)row.Cells["Top"].Value;
                if (top) pipe.Reference = MeasurementReference.TopOfPipe;
                else if (pipe.Reference == MeasurementReference.TopOfPipe) pipe.Reference = MeasurementReference.Invert;
            }

            var generated = string.Join(Environment.NewLine,
                UtilityLabelFormatter.StructureLabel(copy, structure, _settings.Dips).ToArray());
            _generatedLabel = generated;
            if (_labelPreview.Text != generated) SetPreview(generated);
            _labelState.Text = "Updates as you type.";
            _labelState.ForeColor = Muted;
        }

        /// <summary>A cell's value, or the text being typed into it right now.</summary>
        private string Live(DataGridViewRow row, string column)
        {
            var cell = row.Cells[column];
            if (_grid.IsCurrentCellInEditMode && _grid.CurrentCell == cell && _grid.EditingControl != null)
                return _grid.EditingControl.Text ?? string.Empty;
            return Cell(row, column);
        }

        private void RefreshReview()
        {
            if (DipSession.Project == null || _settings == null) return;
            var findings = UtilityQc.Evaluate(DipSession.Project, _settings.Dips);
            _summary.Text = UtilityQc.Summarize(DipSession.Project, findings, 0).ToString();

            _findings.BeginUpdate();
            _findings.Items.Clear();
            foreach (var f in findings.OrderByDescending(f => f.Severity))
            {
                var s = DipSession.Project.Structure(f.StructureId);
                var item = new ListViewItem(new[] { f.Severity.ToString(), f.Code.ToString(), s != null ? s.Label : string.Empty, f.Message })
                {
                    Tag = f,
                    ForeColor = f.Severity == Severity.Error ? Bad : Muted
                };
                _findings.Items.Add(item);
            }
            _findings.EndUpdate();

            _revisit.Text = UtilityQc.FieldRevisitText(DipSession.Project, findings)
                .Replace("\n", Environment.NewLine).Replace("\r\r", "\r");
        }

        private void ShowConnectionState()
        {
            var s = Current;
            var p = SelectedPipe;
            if (s == null || p == null) { _connectionState.Text = string.Empty; return; }
            var c = DipSession.Project.ConnectionFor(s.Id, p.Id);
            _connectionState.ForeColor = c != null && c.IsAccepted ? Good : Muted;
            _connectionState.Text = "Selected: " + ConnectionFinder.Describe(p) + " -- " +
                (c == null ? "not connected" : StatusWords(c.Status).ToLowerInvariant() + (c.Basis.Count > 0 ? " (" + c.Basis[0] + ")" : string.Empty));
        }

        private string ReferenceStatus(PipeObservation p)
        {
            switch (p.ReferenceBasis)
            {
                case ReferenceBasis.NotStated:
                    return p.MeasuredDip.HasValue
                        ? "NOT STATED - assumed " + DipElevations.Describe(_settings.Dips.AssumedPipeReference) + ", confirm"
                        : "not stated";
                case ReferenceBasis.FieldNoteConvention: return "office default (invert)";
                case ReferenceBasis.ConfirmedByDrafter: return "confirmed by drafter";
                case ReferenceBasis.EnteredByDrafter: return "set by drafter";
                default: return "stated in field note";
            }
        }

        private void Say(string text, Color color)
        {
            _status.Text = text;
            _status.ForeColor = color;
        }

        private static string Num(double? v)
        {
            return v.HasValue ? v.Value.ToString("0.##", CultureInfo.InvariantCulture) : string.Empty;
        }

        // ============================================================ saving edits
        //
        // Every action captures IDs, never objects: the dip data can be reloaded from
        // the drawing between a click and the moment its command runs (after an undo,
        // for instance), and a change applied to a stale copy would silently vanish.

        private void SaveRow(int rowIndex, string changedColumn)
        {
            var s = Current;
            if (s == null || rowIndex >= _grid.Rows.Count) return;
            var row = _grid.Rows[rowIndex];
            var pipeId = row.Tag as string;
            var structureId = s.Id;
            var problems = new List<string>();

            double? width, height, dip;
            TryNumber(Cell(row, "W"), "Size", problems, out width);
            TryNumber(Cell(row, "H"), "Height", problems, out height);
            TryDip(Cell(row, "Dip"), "MD", problems, out dip);

            var directionText = Cell(row, "Direction").Trim();
            var direction = directionText.Length == 0 ? ObservedDirection.Unknown("?") : DipNoteParser.ParseDirection(directionText);
            if (direction == null) problems.Add("\"" + directionText + "\" is not a direction (N, SW, N45E, AZ215)");

            if (problems.Count > 0)
            {
                row.ErrorText = string.Join("; ", problems.ToArray());
                Say("Not saved: " + row.ErrorText + ". Fix the cell and leave it again.", Bad);
                return;
            }
            row.ErrorText = string.Empty;

            PipeShape shape;
            MeasurementReference reference;
            FlowRole role;
            Enum.TryParse(Cell(row, "Shape"), out shape);
            Enum.TryParse(Cell(row, "Reference"), out reference);
            Enum.TryParse(Cell(row, "Role"), out role);
            var topTicked = row.Cells["Top"].Value is bool && (bool)row.Cells["Top"].Value;
            var fromTopBox = changedColumn == "Top";
            var material = Cell(row, "Material").Trim().ToUpperInvariant();
            var conditions = Cell(row, "Conditions").Split(',').Select(c => c.Trim().ToUpperInvariant()).Where(c => c.Length > 0).ToList();
            var notes = Cell(row, "Notes");

            var posted = DipSession.Post("edit pipe", (db, tr, ed, project, settings, version) =>
            {
                var structure = project.Structure(structureId);
                var pipe = project.Pipe(structureId, pipeId);
                if (structure == null || pipe == null) return false;

                // The Top of pipe box, when that is what changed, decides the reference:
                // ticked is top of pipe, unticked is the invert.
                if (fromTopBox)
                    reference = topTicked ? MeasurementReference.TopOfPipe
                              : pipe.Reference == MeasurementReference.TopOfPipe ? MeasurementReference.Invert : pipe.Reference;

                var before = ConnectionFinder.Describe(pipe) + " MD " + Num(pipe.MeasuredDip) + " " + pipe.Reference;
                var confirming = pipe.ReferenceUnconfirmed && reference != MeasurementReference.Unspecified;
                var referenceChanged = !confirming && pipe.Reference != reference;
                var measurementChanged = pipe.WidthIn != width || pipe.HeightIn != (height ?? width) || pipe.Shape != shape ||
                                         (pipe.Material ?? string.Empty) != material || pipe.MeasuredDip != dip ||
                                         (pipe.Direction.Text ?? string.Empty) != (direction.Text ?? string.Empty);
                var otherChanged = pipe.Role != role || !pipe.Conditions.SequenceEqual(conditions) || (pipe.Notes ?? string.Empty) != notes;
                if (!confirming && !referenceChanged && !measurementChanged && !otherChanged) return false;

                if (confirming) ObservationReview.ConfirmReference(project, structure, pipe, reference);
                if (referenceChanged)
                {
                    project.Overrides.Add(new ManualOverride
                    {
                        Target = pipe.Id, What = "Measurement reference set by drafter",
                        Generated = pipe.Reference + " (" + pipe.ReferenceBasis + ")", Entered = reference.ToString(), Utc = DateTime.UtcNow
                    });
                    pipe.Reference = reference;
                    pipe.ReferenceBasis = ReferenceBasis.EnteredByDrafter;
                }

                pipe.WidthIn = width;
                pipe.HeightIn = height ?? width;
                pipe.Shape = shape;
                pipe.Material = material.Length == 0 ? null : material;
                pipe.Direction = direction;
                pipe.MeasuredDip = dip;
                pipe.Role = role;
                pipe.Conditions = conditions;
                pipe.Notes = notes;

                if (measurementChanged && pipe.Source == ObservationSource.FieldNote)
                {
                    // A field note value changed by hand is visibly the drafter's entry now.
                    pipe.Source = ObservationSource.UserEntry;
                    project.Overrides.Add(new ManualOverride
                    {
                        Target = pipe.Id, What = "Observation edited by drafter", Generated = before,
                        Entered = ConnectionFinder.Describe(pipe) + " MD " + Num(pipe.MeasuredDip) + " " + pipe.Reference,
                        Utc = DateTime.UtcNow
                    });
                }
                return true;
            });
            if (posted) Say("Saved.", Good);
        }

        private void SaveStructureFields()
        {
            var s = Current;
            if (s == null || _loading) return;
            var problems = new List<string>();
            double? bottom, water, inside, length;
            TryDip(_bottom.Text, "Bottom MD", problems, out bottom);
            TryDip(_water.Text, "Water MD", problems, out water);
            TryNumber(_insideWidth.Text, "Size", problems, out inside);
            TryNumber(_insideLength.Text, "Length", problems, out length);
            if (problems.Count > 0)
            {
                Say("Not saved: " + string.Join("; ", problems.ToArray()), Bad);
                return;
            }

            var type = _type.Text.Trim().ToUpperInvariant();
            UtilitySystem system;
            if (!Enum.TryParse(Convert.ToString(_system.SelectedItem), out system)) system = s.System;
            if (s.Field.BottomDip == bottom && s.Field.WaterDip == water && s.EnteredInsideWidthIn == inside &&
                s.EnteredInsideLengthIn == length &&
                (type.Length == 0 || type == (s.StructureType ?? string.Empty)) && system == s.System)
                return;

            var structureId = s.Id;
            var posted = DipSession.Post("edit structure", (db, tr, ed, project, settings, version) =>
            {
                var structure = project.Structure(structureId);
                if (structure == null) return false;
                if (structure.Field.BottomDip != bottom || structure.Field.WaterDip != water)
                    project.Overrides.Add(new ManualOverride
                    {
                        Target = structure.Id, What = "Structure MDs entered by drafter",
                        Generated = "BOT " + Num(structure.Field.BottomDip) + " WL " + Num(structure.Field.WaterDip),
                        Entered = "BOT " + Num(bottom) + " WL " + Num(water), Utc = DateTime.UtcNow
                    });
                structure.Field.BottomDip = bottom;
                structure.Field.WaterDip = water;
                if (structure.EnteredInsideWidthIn != inside || structure.EnteredInsideLengthIn != length)
                    project.Overrides.Add(new ManualOverride
                    {
                        Target = structure.Id, What = "Structure size entered by drafter",
                        Generated = Num(structure.EnteredInsideWidthIn) + " x " + Num(structure.EnteredInsideLengthIn),
                        Entered = Num(inside) + " x " + Num(length), Utc = DateTime.UtcNow
                    });
                structure.EnteredInsideWidthIn = inside;
                structure.EnteredInsideLengthIn = length;
                if (type.Length > 0) structure.StructureType = type;
                structure.System = system;
                return true;
            });
            if (posted) Say("Saved.", Good);
        }

        private static string Cell(DataGridViewRow row, string column)
        {
            var v = row.Cells[column].Value;
            return v == null ? string.Empty : Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        private static bool TryNumber(string text, string what, List<string> problems, out double? value)
        {
            value = null;
            if (string.IsNullOrWhiteSpace(text)) return true;
            double v;
            if (double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v) && v > 0 && !double.IsNaN(v))
            {
                value = v;
                return true;
            }
            problems.Add(what + " \"" + text + "\" is not a positive number");
            return false;
        }

        private static bool TryDip(string text, string what, List<string> problems, out double? value)
        {
            value = null;
            if (string.IsNullOrWhiteSpace(text)) return true;
            double v;
            if (double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v) && v >= 0 && !double.IsNaN(v))
            {
                value = v;
                return true;
            }
            problems.Add(what + " \"" + text + "\" is not a measure down (0 or more feet below the rim)");
            return false;
        }

        // ============================================================== actions

        private void OnPickStructure(object sender, EventArgs e)
        {
            var posted = DipSession.Post("select structure", (db, tr, ed, project, settings, version) =>
            {
                var options = new PromptEntityOptions("\nSelect the surveyed structure point: ");
                options.SetRejectMessage("\nThat is not a COGO point.");
                options.AddAllowedClass(typeof(CogoPoint), true);
                var picked = ed.GetEntity(options);
                if (picked.Status != PromptStatus.OK) return false;

                var point = (CogoPoint)tr.GetObject(picked.ObjectId, OpenMode.ForRead);
                var record = UtilityCadService.EnsureStructure(project, UtilityCadService.Snapshot(point), settings.Dips);
                _structureId = record.Id;
                _labelEdited = false;
                ed.WriteMessage("\nDip Builder: {0}, rim {1:0.00} from the drawing.", record.Label, record.Cad.Rim);
                return true;
            });
            if (posted) Say("Pick the structure's survey point in the drawing...", Muted);
        }

        private void OnReadNotes(object sender, EventArgs e)
        {
            var text = _notes.Text;
            if (Current != null && !text.TrimStart().StartsWith("PT", StringComparison.OrdinalIgnoreCase))
                text = "PT " + Current.Field.PointNumber + " " + (Current.Field.FieldCode ?? Current.StructureType ?? string.Empty) +
                       Environment.NewLine + text;
            ReadNotes(text);
        }

        private void OnImportFile(object sender, EventArgs e)
        {
            using (var dialog = new OpenFileDialog { Filter = "Field notes (*.txt;*.csv)|*.txt;*.csv|All files|*.*" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                ReadNotes(File.ReadAllText(dialog.FileName));
            }
        }

        private void ReadNotes(string text)
        {
            if (_settings == null) return;
            var parsed = new DipNoteParser(_settings.Dips).Parse(text);

            _diagnostics.Items.Clear();
            foreach (var d in parsed.Diagnostics) _diagnostics.Items.Add(d.Severity + ": " + d);

            if (parsed.Structures.Count == 0)
            {
                _diagnostics.Items.Add("Error: No \"PT <number>\" structure blocks found.");
                FitAll();
                return;
            }
            FitAll();
            _labelEdited = false;

            var posted = DipSession.Post("read notes", (db, tr, ed, project, settings, version) =>
            {
                var live = UtilityCadService.LivePoints(db, tr);
                foreach (var message in UtilityCadService.ImportNotes(project, parsed, live, settings.Dips))
                    ed.WriteMessage("\n  " + message);
                var first = project.StructureByPoint(parsed.Structures[0].PointNumber);
                if (first != null) _structureId = first.Id;
                return true;
            });
            if (posted) Say(parsed.Structures.Count + " structure(s) read from the notes.", Good);
        }

        private void OnConfirmInvert(bool allOnStructure)
        {
            var s = Current;
            if (s == null) return;
            var pipeIds = allOnStructure
                ? s.Field.Pipes.Where(p => p.ReferenceUnconfirmed && p.MeasuredDip.HasValue).Select(p => p.Id).ToList()
                : new[] { SelectedPipe }.Where(p => p != null && p.ReferenceUnconfirmed).Select(p => p.Id).ToList();
            if (pipeIds.Count == 0)
            {
                Say("No unmarked MDs waiting for confirmation on this structure.", Muted);
                return;
            }

            var structureId = s.Id;
            var posted = DipSession.Post("confirm invert", (db, tr, ed, project, settings, version) =>
            {
                var structure = project.Structure(structureId);
                if (structure == null) return false;
                var confirmed = pipeIds.Count(id => ObservationReview.ConfirmReference(project, structure,
                                                        project.Pipe(structureId, id), MeasurementReference.Invert));
                ed.WriteMessage("\nDip Builder: {0} MD(s) at {1} confirmed as inverts. The measurements are unchanged.", confirmed, structure.Label);
                return confirmed > 0;
            });
            if (posted) Say(pipeIds.Count + " MD(s) confirmed as inverts.", Good);
        }

        private void OnAddPipe(object sender, EventArgs e)
        {
            var s = Current;
            if (s == null) { Say("Select a structure first (step 1).", Bad); return; }
            _grid.EndEdit();   // a half-typed cell is saved, not lost
            var structureId = s.Id;
            var newId = Guid.NewGuid().ToString("N");
            var conventionNote = _settings.Dips.UnmarkedDipConventionSource;
            var convention = _settings.Dips.UnmarkedDipsAreInvertsByConvention;
            var posted = DipSession.Post("add pipe", (db, tr, ed, project, settings, version) =>
            {
                var structure = project.Structure(structureId);
                if (structure == null) return false;
                structure.Field.Pipes.Add(new PipeObservation
                {
                    Id = newId,
                    Source = ObservationSource.UserEntry,
                    Reference = convention ? MeasurementReference.Invert : MeasurementReference.Unspecified,
                    ReferenceBasis = convention ? ReferenceBasis.FieldNoteConvention : ReferenceBasis.NotStated,
                    ReferenceNote = convention ? conventionNote : null,
                    Direction = ObservedDirection.Unknown("?")
                });
                BeginInvoke(new Action(() =>
                {
                    foreach (DataGridViewRow row in _grid.Rows)
                        if ((row.Tag as string) == newId) { _grid.CurrentCell = row.Cells["W"]; _grid.BeginEdit(true); }
                }));
                return true;
            });
            if (posted) Say("Pipe added -- type its size, material, direction and MD.", Muted);
        }

        private void OnDeletePipe(object sender, EventArgs e)
        {
            var s = Current;
            var p = SelectedPipe;
            if (s == null || p == null) { Say("Select a pipe row first.", Muted); return; }
            if (MessageBox.Show(this, "Delete the pipe " + ConnectionFinder.Describe(p) + "? Its connection is removed too; drawn pipes stay until you redraw.",
                                "Delete pipe", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

            var structureId = s.Id;
            var pipeId = p.Id;
            DipSession.Post("delete pipe", (db, tr, ed, project, settings, version) =>
            {
                var structure = project.Structure(structureId);
                var pipe = project.Pipe(structureId, pipeId);
                if (structure == null || pipe == null) return false;
                structure.Field.Pipes.Remove(pipe);
                project.Connections.RemoveAll(c => (c.FromStructureId == structureId && c.FromPipeId == pipeId) ||
                                                   (c.ToStructureId == structureId && c.ToPipeId == pipeId));
                project.Overrides.Add(new ManualOverride { Target = structureId, What = "Observation deleted by drafter", Generated = pipe.RawText ?? ConnectionFinder.Describe(pipe), Utc = DateTime.UtcNow });
                return true;
            });
        }

        private void MovePipe(int delta)
        {
            var s = Current;
            var p = SelectedPipe;
            if (s == null || p == null) return;
            var structureId = s.Id;
            var pipeId = p.Id;
            DipSession.Post("reorder", (db, tr, ed, project, settings, version) =>
            {
                var structure = project.Structure(structureId);
                var pipe = project.Pipe(structureId, pipeId);
                if (structure == null || pipe == null) return false;
                var index = structure.Field.Pipes.IndexOf(pipe);
                var target = index + delta;
                if (target < 0 || target >= structure.Field.Pipes.Count) return false;
                structure.Field.Pipes.RemoveAt(index);
                structure.Field.Pipes.Insert(target, pipe);
                return true;
            });
        }

        private void OnFindConnections(object sender, EventArgs e)
        {
            var s = Current;
            var p = SelectedPipe;
            _candidateList.Items.Clear();
            if (s == null || p == null || _settings == null) { Say("Select a pipe first.", Muted); FitAll(); return; }

            if (!p.Direction.IsKnown)
            {
                Say("That pipe has no direction, so there is nothing to search along. Enter one in step 2.", Warn);
                FitAll();
                return;
            }

            _candidates = ConnectionFinder.Find(DipSession.Project, s, p, _settings.Dips);
            foreach (var c in _candidates)
            {
                var item = new ListViewItem(new[]
                {
                    c.Structure.Label,
                    c.Distance.ToString("0.00", CultureInfo.InvariantCulture) + "'",
                    c.DeviationDegrees.ToString("0.0", CultureInfo.InvariantCulture) + "°",
                    c.MatchingPipe != null ? ConnectionFinder.Describe(c.MatchingPipe) : "none observed",
                    c.Confidence.ToString(),
                    string.Join("; ", c.Basis.ToArray())
                }) { UseItemStyleForSubItems = false };
                item.SubItems[4].ForeColor = c.Confidence == Confidence.High ? Good : c.Confidence == Confidence.Medium ? Warn : Muted;
                _candidateList.Items.Add(item);
            }
            if (_candidateList.Items.Count > 0) _candidateList.Items[0].Selected = true;
            _candidateCaption.Text = "Where " + ConnectionFinder.Describe(p) + " may run -- the best match is selected:";
            if (_scroll != null && _connectionGroup != null) _scroll.ScrollControlIntoView(_connectionGroup);
            Say(_candidates.Count == 0
                ? "No surveyed structure in that direction yet. If you know where it goes, use Pick a different structure."
                : _candidates.Count + " possible connection(s). Confirm selected if the highlighted one is right.", Muted);
            _map.Candidates = _candidates.Select(c => c.Structure.Id).ToList();
            _map.Invalidate();
            FitAll();
        }

        private void OnConfirm(object sender, EventArgs e)
        {
            var s = Current;
            var p = SelectedPipe;
            if (s == null || p == null || _candidateList.SelectedIndices.Count == 0)
            {
                Say("Find connections and select one first.", Muted);
                return;
            }
            var candidate = _candidates[_candidateList.SelectedIndices[0]];
            var structureId = s.Id;
            var pipeId = p.Id;

            var posted = DipSession.Post("confirm connection", (db, tr, ed, project, settings, version) =>
            {
                var structure = project.Structure(structureId);
                var pipe = project.Pipe(structureId, pipeId);
                if (structure == null || pipe == null) return false;
                ConnectionFinder.Accept(project, structure, pipe, candidate, false, null);
                return true;
            });
            if (!posted) return;
            _map.Candidates = null;
            _candidateList.Items.Clear();
            FitAll();
            Say("Connected to " + candidate.Structure.Label + ".", Good);
        }

        private void OnManualPick(object sender, EventArgs e)
        {
            var s = Current;
            var p = SelectedPipe;
            if (s == null || p == null) { Say("Select a pipe first.", Muted); return; }
            var structureId = s.Id;
            var pipeId = p.Id;

            DipSession.Post("manual connection", (db, tr, ed, project, settings, version) =>
            {
                var structure = project.Structure(structureId);
                var pipe = project.Pipe(structureId, pipeId);
                if (structure == null || pipe == null) return false;

                var options = new PromptEntityOptions("\nSelect the structure point this pipe runs to: ");
                options.SetRejectMessage("\nThat is not a COGO point.");
                options.AddAllowedClass(typeof(CogoPoint), true);
                var picked = ed.GetEntity(options);
                if (picked.Status != PromptStatus.OK) return false;

                var point = (CogoPoint)tr.GetObject(picked.ObjectId, OpenMode.ForRead);
                var target = UtilityCadService.EnsureStructure(project, UtilityCadService.Snapshot(point), settings.Dips);
                if (target.Id == structure.Id) { ed.WriteMessage("\nA pipe cannot connect a structure to itself."); return false; }

                var note = ed.GetString(new PromptStringOptions("\nReason for the manual connection (optional): ") { AllowSpaces = true });
                var candidate = ConnectionFinder.ManualCandidate(project, structure, pipe, target, settings.Dips);
                ConnectionFinder.Accept(project, structure, pipe, candidate, true, note.Status == PromptStatus.OK ? note.StringResult : null);
                ed.WriteMessage("\nManual connection {0} -> {1} recorded as a manual override.", structure.Label, target.Label);
                return true;
            });
        }

        private void OnLeaveUnresolved(object sender, EventArgs e)
        {
            var s = Current;
            var p = SelectedPipe;
            if (s == null || p == null) { Say("Select a pipe first.", Muted); return; }
            var structureId = s.Id;
            var pipeId = p.Id;
            var posted = DipSession.Post("leave unresolved", (db, tr, ed, project, settings, version) =>
            {
                var structure = project.Structure(structureId);
                var pipe = project.Pipe(structureId, pipeId);
                if (structure == null || pipe == null) return false;
                ConnectionFinder.LeaveUnresolved(project, structure, pipe);
                return true;
            });
            if (posted) Say("Left unresolved -- it goes on the field revisit list.", Muted);
        }

        private void OnOutsideLimits(object sender, EventArgs e)
        {
            var s = Current;
            var p = SelectedPipe;
            if (s == null || p == null) { Say("Select a pipe first.", Muted); return; }
            var structureId = s.Id;
            var pipeId = p.Id;
            var posted = DipSession.Post("outside survey limits", (db, tr, ed, project, settings, version) =>
            {
                var structure = project.Structure(structureId);
                var pipe = project.Pipe(structureId, pipeId);
                if (structure == null || pipe == null) return false;
                ConnectionFinder.MarkOutsideLimits(project, structure, pipe);
                return true;
            });
            if (posted) Say(ConnectionFinder.Describe(p) + " runs outside the survey limits -- nothing to connect, nothing to chase.", Muted);
        }

        private void OnDraw(bool all)
        {
            var s = Current;
            if (!all && s == null) { Say("Select a structure first (step 1).", Bad); return; }
            var structureId = s != null ? s.Id : null;

            var posted = DipSession.Post("draw pipes", (db, tr, ed, project, settings, version) =>
            {
                UtilityCommands.DrawConnections(db, tr, ed, project, settings, version,
                    c => all || c.FromStructureId == structureId || c.ToStructureId == structureId);
                return true;
            });
            if (posted) Say("Drawing confirmed pipes -- answer any question at the command line.", Muted);
        }

        private void OnLabelAll(object sender, EventArgs e)
        {
            var posted = DipSession.Post("label all structures", (db, tr, ed, project, settings, version) =>
                UtilityCommands.LabelAll(db, tr, ed, project, settings, version) > 0);
            if (posted) Say("Labelling every structure that has pipes and no label yet.", Muted);
        }

        private void OnPlaceLabel(object sender, EventArgs e)
        {
            var s = Current;
            if (s == null || s.Cad == null) { Say("Select a structure first (step 1).", Bad); return; }
            _grid.EndEdit();
            var text = _labelPreview.Text;
            var edited = _labelEdited;
            var generated = _generatedLabel;
            var structureId = s.Id;

            var posted = DipSession.Post("place structure label", (db, tr, ed, project, settings, version) =>
            {
                var structure = project.Structure(structureId);
                if (structure == null || structure.Cad == null) return false;
                var options = new PromptPointOptions("\nLabel text location: ")
                {
                    UseBasePoint = true,
                    BasePoint = new Point3d(structure.Cad.Easting, structure.Cad.Northing, 0)
                };
                var at = ed.GetPoint(options);
                if (at.Status != PromptStatus.OK) return false;
                var styles = UtilityCadService.MissingStyles(db, tr, settings.Dips);
                if (styles != null) ed.WriteMessage("\n" + styles);

                UtilityCadService.PlaceStructureLabel(db, tr, structure, text, at.Value, settings, version);
                project.Overrides.RemoveAll(o => o.Target == structureId && o.What == "Structure label text");
                if (edited)
                    project.Overrides.Add(new ManualOverride { Target = structureId, What = "Structure label text", Generated = generated, Entered = text, Utc = DateTime.UtcNow });
                return true;
            });
            if (posted) Say("Click where the label text goes.", Muted);
        }

        private void OnFindingClicked(object sender, EventArgs e)
        {
            if (_findings.SelectedItems.Count == 0) return;
            var finding = (QcFinding)_findings.SelectedItems[0].Tag;
            var structure = DipSession.Project.Structure(finding.StructureId);
            if (structure != null) _structureId = structure.Id;
            var pointNumber = structure != null && structure.Cad != null ? structure.Cad.PointNumber : null;

            DipSession.Post("zoom", (db, tr, ed, project, settings, version) =>
            {
                var ids = new List<ObjectId>();
                if (finding.ConnectionId != null)
                    ids.AddRange(Ownership.FindOwned(db, tr, st => st.PointNumber == finding.ConnectionId).Select(x => x.Key));
                if (pointNumber != null)
                {
                    var point = UtilityCadService.PointId(db, tr, pointNumber);
                    if (!point.IsNull) ids.Add(point);
                }
                if (ids.Count == 0) return false;

                Extents3d? extents = null;
                foreach (var id in ids)
                {
                    try
                    {
                        var ext = ((Autodesk.AutoCAD.DatabaseServices.Entity)tr.GetObject(id, OpenMode.ForRead)).GeometricExtents;
                        if (extents == null) extents = ext;
                        else { var x = extents.Value; x.AddExtents(ext); extents = x; }
                    }
                    catch (Autodesk.AutoCAD.Runtime.Exception) { }
                }
                if (extents.HasValue) UtilityCadService.ZoomTo(ed, extents.Value);
                ed.SetImpliedSelection(ids.ToArray());
                return false;
            });
        }

        private void OnRebuild(object sender, EventArgs e)
        {
            DipSession.Post("rebuild", (db, tr, ed, project, settings, version) =>
            {
                var live = UtilityCadService.LivePoints(db, tr);
                var stale = UtilityQc.StaleStructures(project, live);
                if (stale.Count == 0)
                {
                    ed.WriteMessage("\nDip Builder: no structure has changed since it was calculated.");
                    return false;
                }
                return UtilityCommands.Rebuild(db, tr, ed, project, settings, version, stale, live, true) > 0;
            });
        }

        private void OnExportRevisit(object sender, EventArgs e)
        {
            var text = _revisit.Text;
            DipSession.Post("export revisit", (db, tr, ed, project, settings, version) =>
            {
                var path = UtilityCommands.ExportRevisitFile(db, text);
                ed.WriteMessage(path == null ? "\nSave the drawing first." : "\nField revisit list: " + path);
                return false;
            });
        }

        private void OnAddRevisit(object sender, EventArgs e)
        {
            var s = Current;
            var text = _revisitAdd.Text.Trim();
            if (s == null || text.Length == 0) return;
            _revisitAdd.Text = string.Empty;
            var label = s.Label;
            DipSession.Post("add revisit note", (db, tr, ed, project, settings, version) =>
            {
                project.ManualRevisit.Add(new RevisitItem { StructureLabel = label, Text = text, Manual = true });
                return true;
            });
        }
    }

    /// <summary>
    /// A plan sketch of the dip project, north up. On the Build tab it shows the
    /// selected structure and its neighbours; on the Map tab (ShowAll) every
    /// structure and connection. Confirmed runs are solid, manual picks dashed, and
    /// pipes not connected yet are short dashed stubs in their field direction.
    /// Clicking a structure opens it.
    /// </summary>
    internal sealed class ConnectionMapView : Control
    {
        private readonly Func<UtilityProject> _project;
        private readonly Func<string> _structureId;
        private readonly Func<string> _selectedPipeId;
        private readonly List<KeyValuePair<string, PointF>> _hits = new List<KeyValuePair<string, PointF>>();

        /// <summary>Structures the last connection search offered, drawn highlighted.</summary>
        public IList<string> Candidates { get; set; }

        /// <summary>Show every structure in the project rather than the neighbourhood.</summary>
        public bool ShowAll { get; set; }

        /// <summary>Raised with the structure id when a structure is clicked.</summary>
        public event Action<string> StructureClicked;

        public ConnectionMapView(Func<UtilityProject> project, Func<string> structureId, Func<string> selectedPipeId)
        {
            _project = project;
            _structureId = structureId;
            _selectedPipeId = selectedPipeId;
            DoubleBuffered = true;
            BackColor = DipBuilderForm.Surface;
            Font = DipBuilderForm.F(10f, false);
            ResizeRedraw = true;
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            var best = _hits.OrderBy(h => Math.Pow(h.Value.X - e.X, 2) + Math.Pow(h.Value.Y - e.Y, 2)).FirstOrDefault();
            if (best.Key == null) return;
            if (Math.Pow(best.Value.X - e.X, 2) + Math.Pow(best.Value.Y - e.Y, 2) > 14 * 14) return;
            var handler = StructureClicked;
            if (handler != null) handler(best.Key);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            _hits.Clear();
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var project = _project();
            var currentId = _structureId();
            var structures = project == null ? new List<StructureRecord>() : project.Structures.Where(s => s.Cad != null).ToList();
            var current = structures.FirstOrDefault(s => s.Id == currentId);

            if (structures.Count == 0 || (!ShowAll && current == null))
            {
                TextRenderer.DrawText(g, ShowAll ? "No structures yet. Select a structure and enter its pipes on the Build tab." : "Select a structure to see its connections.",
                                      Font, ClientRectangle, DipBuilderForm.Muted,
                                      TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
                return;
            }

            List<StructureRecord> shown;
            if (ShowAll) shown = structures;
            else
            {
                // The selected structure with everything connected to it, plus neighbours.
                var linked = new HashSet<string> { current.Id };
                foreach (var c in project.Connections.Where(c => c.ToStructureId != null && (c.FromStructureId == current.Id || c.ToStructureId == current.Id)))
                {
                    linked.Add(c.FromStructureId);
                    linked.Add(c.ToStructureId);
                }
                if (Candidates != null) foreach (var id in Candidates) linked.Add(id);
                shown = structures.Where(s => linked.Contains(s.Id) || Distance(s, current) <= 250).ToList();
            }

            // The compass has its own corner; the drawing never runs under it.
            const int compassWidth = 34;
            var legendHeight = LegendRows() * (Font.Height + 4) + 8;
            var box = new Rectangle(20, 16, Math.Max(40, Width - 40 - compassWidth), Math.Max(40, Height - 32 - legendHeight));

            double minE = shown.Min(s => s.Cad.Easting), maxE = shown.Max(s => s.Cad.Easting);
            double minN = shown.Min(s => s.Cad.Northing), maxN = shown.Max(s => s.Cad.Northing);
            var span = Math.Max(60.0, Math.Max((maxE - minE) / Math.Max(1.0, box.Width / (double)box.Height), maxN - minN));
            var spanE = span * box.Width / (double)box.Height;
            var scale = Math.Min(box.Width / Math.Max(1.0, Math.Max(spanE, maxE - minE)), box.Height / span) * 0.9;
            var midE = (minE + maxE) / 2;
            var midN = (minN + maxN) / 2;
            Func<double, double, PointF> at = (east, north) => new PointF(
                (float)(box.Left + box.Width / 2.0 + (east - midE) * scale),
                (float)(box.Top + box.Height / 2.0 - (north - midN) * scale));

            // runs
            foreach (var c in project.Connections.Where(c => c.ToStructureId != null && c.IsAccepted))
            {
                var a = shown.FirstOrDefault(s => s.Id == c.FromStructureId);
                var b = shown.FirstOrDefault(s => s.Id == c.ToStructureId);
                if (a == null || b == null) continue;
                var mine = current != null && (a.Id == current.Id || b.Id == current.Id);
                var color = ShowAll || mine ? DipBuilderForm.Accent : DipBuilderForm.Rule;
                using (var pen = new Pen(color, c.Drafted ? 3f : 2f))
                {
                    if (c.Status == ConnectionStatus.ManualOverride) pen.DashStyle = DashStyle.Dash;
                    var pa = at(a.Cad.Easting, a.Cad.Northing);
                    var pb = at(b.Cad.Easting, b.Cad.Northing);
                    g.DrawLine(pen, pa, pb);
                    var runLength = Math.Sqrt((pa.X - pb.X) * (pa.X - pb.X) + (pa.Y - pb.Y) * (pa.Y - pb.Y));
                    if ((ShowAll || mine) && runLength > 90)
                    {
                        var pipe = project.Pipe(c.FromStructureId, c.FromPipeId);
                        if (pipe != null)
                            TextRenderer.DrawText(g, UtilityLabelFormatter.FormatSize(pipe) + " " + (pipe.Material ?? ""), Font,
                                                  Point.Round(new PointF((pa.X + pb.X) / 2 + 5, (pa.Y + pb.Y) / 2 + 2)), DipBuilderForm.Muted);
                    }
                }
            }

            // pipes not connected yet
            var selectedPipe = _selectedPipeId();
            foreach (var s in ShowAll ? shown : new List<StructureRecord> { current })
            {
                var origin = at(s.Cad.Easting, s.Cad.Northing);
                foreach (var pipe in s.Field.Pipes)
                {
                    var c = project.ConnectionFor(s.Id, pipe.Id);
                    if ((c != null && c.IsAccepted) || !pipe.Direction.IsKnown) continue;
                    var az = pipe.Direction.AzimuthDegrees.Value * Math.PI / 180.0;
                    var length = Math.Min(box.Width, box.Height) * (ShowAll ? 0.07f : 0.2f);
                    var end = new PointF(origin.X + (float)(Math.Sin(az) * length), origin.Y - (float)(Math.Cos(az) * length));
                    var selected = pipe.Id == selectedPipe;
                    if (c != null && c.Status == ConnectionStatus.OutsideSurveyLimits)
                    {
                        using (var pen = new Pen(selected ? DipBuilderForm.Accent : DipBuilderForm.Muted, 1.8f))
                        {
                            pen.CustomEndCap = new AdjustableArrowCap(4, 5);
                            g.DrawLine(pen, origin, end);
                        }
                        continue;
                    }
                    using (var pen = new Pen(selected ? DipBuilderForm.Accent : DipBuilderForm.Warn, selected ? 2.5f : 1.6f) { DashStyle = DashStyle.Dash })
                        g.DrawLine(pen, origin, end);
                    if (!ShowAll)
                    {
                        var stubText = UtilityLabelFormatter.FormatSize(pipe) + " " + (pipe.Direction.Text ?? "");
                        var stubSize = TextRenderer.MeasureText(stubText, Font);
                        var tx = Math.Sin(az) >= 0 ? end.X + 4 : end.X - stubSize.Width - 4;
                        var ty = Math.Cos(az) >= 0 ? end.Y - stubSize.Height : end.Y;
                        TextRenderer.DrawText(g, stubText, Font, Point.Round(new PointF(tx, ty)), DipBuilderForm.Warn);
                    }
                }
            }

            // structures, the selected one last so its name is never covered
            foreach (var s in shown.OrderBy(x => current != null && x.Id == current.Id ? 1 : 0))
            {
                var p = at(s.Cad.Easting, s.Cad.Northing);
                _hits.Add(new KeyValuePair<string, PointF>(s.Id, p));
                var isCurrent = current != null && s.Id == current.Id;
                var isCandidate = Candidates != null && Candidates.Contains(s.Id);
                var r = isCurrent ? 7f : 5.5f;
                using (var fill = new SolidBrush(isCurrent ? DipBuilderForm.Accent : DipBuilderForm.Surface))
                using (var pen = new Pen(isCandidate ? DipBuilderForm.Good : DipBuilderForm.Accent, isCandidate ? 2.5f : 1.8f))
                {
                    g.FillEllipse(fill, p.X - r, p.Y - r, 2 * r, 2 * r);
                    g.DrawEllipse(pen, p.X - r, p.Y - r, 2 * r, 2 * r);
                }
                var labelFont = isCurrent ? new Font(Font.FontFamily, Font.Size + 0.5f, FontStyle.Bold) : Font;
                var labelSize = TextRenderer.MeasureText(s.Label, labelFont);
                var labelAt = isCurrent
                    ? Point.Round(new PointF(p.X - labelSize.Width / 2f, p.Y - r - labelSize.Height - 3))
                    : Point.Round(new PointF(p.X + r + 4, p.Y - r - labelSize.Height + 4));
                // Keep names inside the map and clear of the compass corner.
                var rightLimit = Width - (labelAt.Y < 50 ? compassWidth + 8 : 4);
                if (labelAt.X + labelSize.Width > rightLimit)
                    labelAt.X = isCurrent ? rightLimit - labelSize.Width : (int)(p.X - r - 4 - labelSize.Width);
                if (labelAt.X < 2) labelAt.X = 2;
                if (labelAt.Y < 2) labelAt.Y = 2;
                using (var backing = new SolidBrush(Color.FromArgb(225, DipBuilderForm.Surface)))
                    g.FillRectangle(backing, new Rectangle(labelAt, labelSize));
                TextRenderer.DrawText(g, s.Label, labelFont, labelAt, DipBuilderForm.Ink);
            }

            DrawCompass(g, new Point(Width - compassWidth / 2 - 8, 14));
            DrawLegend(g, Height - legendHeight + 2);
        }

        /// <summary>A small, centred north arrow: a split triangle over the letter N.</summary>
        private void DrawCompass(Graphics g, Point top)
        {
            var cx = top.X;
            var tip = new PointF(cx, top.Y);
            var baseY = top.Y + 18;
            using (var dark = new SolidBrush(DipBuilderForm.Ink))
            using (var light = new SolidBrush(DipBuilderForm.Surface))
            using (var outline = new Pen(DipBuilderForm.Ink, 1.2f))
            {
                var leftHalf = new[] { tip, new PointF(cx - 7, baseY), new PointF(cx, baseY - 5) };
                var rightHalf = new[] { tip, new PointF(cx + 7, baseY), new PointF(cx, baseY - 5) };
                g.FillPolygon(dark, leftHalf);
                g.FillPolygon(light, rightHalf);
                g.DrawPolygon(outline, new[] { tip, new PointF(cx + 7, baseY), new PointF(cx, baseY - 5), new PointF(cx - 7, baseY) });
            }
            var n = "N";
            var size = TextRenderer.MeasureText(n, Font);
            TextRenderer.DrawText(g, n, Font, new Point(cx - size.Width / 2, baseY + 1), DipBuilderForm.Ink);
        }

        private static readonly string[] LegendText = { "connected", "not connected yet", "outside survey limits" };

        /// <summary>Where each legend entry goes: entries wrap to a new row rather than run
        /// off a narrow map.</summary>
        private List<Point> LegendLayout()
        {
            var spots = new List<Point>();
            int x = 14, row = 0;
            foreach (var text in LegendText)
            {
                var width = 26 + TextRenderer.MeasureText(text, Font).Width;
                if (x > 14 && x + width > Width - 8) { x = 14; row++; }
                spots.Add(new Point(x, row));
                x += width + 16;
            }
            return spots;
        }

        private int LegendRows()
        {
            return LegendLayout().Max(p => p.Y) + 1;
        }

        private void DrawLegend(Graphics g, int y)
        {
            var spots = LegendLayout();
            var rowHeight = Font.Height + 4;
            for (var i = 0; i < LegendText.Length; i++)
            {
                var x = spots[i].X;
                var top = y + spots[i].Y * rowHeight;
                var mid = top + Font.Height / 2;
                if (i == 0) using (var pen = new Pen(DipBuilderForm.Accent, 2.5f)) g.DrawLine(pen, x, mid, x + 22, mid);
                if (i == 1) using (var pen = new Pen(DipBuilderForm.Warn, 1.6f) { DashStyle = DashStyle.Dash }) g.DrawLine(pen, x, mid, x + 22, mid);
                if (i == 2) using (var pen = new Pen(DipBuilderForm.Muted, 1.8f) { CustomEndCap = new AdjustableArrowCap(4, 5) }) g.DrawLine(pen, x, mid, x + 22, mid);
                TextRenderer.DrawText(g, LegendText[i], Font, new Point(x + 26, top), DipBuilderForm.Muted);
            }
        }

        private static double Distance(StructureRecord a, StructureRecord b)
        {
            var dx = a.Cad.Easting - b.Cad.Easting;
            var dy = a.Cad.Northing - b.Cad.Northing;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }

    /// <summary>Remembers the light or dark theme for this user. Dark by default, to sit
    /// beside Civil 3D's dark theme.</summary>
    internal static class ThemePreference
    {
        private static string PathFor()
        {
            return System.IO.Path.Combine(System.IO.Path.GetDirectoryName(FtfSettings.UserProfilePath()), "dip-builder-theme.txt");
        }

        public static bool LoadDark()
        {
            try { return !File.Exists(PathFor()) || File.ReadAllText(PathFor()).Trim() != "light"; }
            catch (IOException) { return true; }
            catch (UnauthorizedAccessException) { return true; }
        }

        public static void SaveDark(bool dark)
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PathFor()));
                File.WriteAllText(PathFor(), dark ? "dark" : "light");
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>Remembers Basic / Advanced for this user between sessions.</summary>
    internal static class ViewPreference
    {
        private static string PathFor()
        {
            return System.IO.Path.Combine(System.IO.Path.GetDirectoryName(FtfSettings.UserProfilePath()), "dip-builder-view.txt");
        }

        public static bool Load()
        {
            try { return File.Exists(PathFor()) && File.ReadAllText(PathFor()).Trim() == "advanced"; }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        public static void Save(bool advanced)
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PathFor()));
                File.WriteAllText(PathFor(), advanced ? "advanced" : "basic");
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
