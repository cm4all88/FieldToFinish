#if TRACE_EDITION
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;
using FieldCodes.TraceUi;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace FieldCodes.Cad
{
    /// <summary>
    /// What is inside the TRACE palette: the header, the Run TRACE button with its step list,
    /// and every TRACE tool grouped as on the ribbon. It only dispatches commands; all work is
    /// done by the existing code. Sections collapse; the step ticks are remembered per user.
    /// </summary>
    internal sealed class TracePanel : UserControl
    {
        private static readonly Color Navy = Color.FromArgb(0x0B, 0x2E, 0x4F);
        private static readonly Color Blue = Color.FromArgb(0x1E, 0x6F, 0xB8);
        private static readonly Color Surface = Color.FromArgb(0xF4, 0xF6, 0xF9);
        private static readonly Color Border = Color.FromArgb(0xD5, 0xDD, 0xE5);
        private static readonly Color Slate = Color.FromArgb(0x52, 0x62, 0x7A);

        private readonly ToolTip _tips = new ToolTip();
        private readonly Panel _scroll = new Panel();
        private readonly FlowLayoutPanel _stack = new FlowLayoutPanel();
        private readonly Header _header = new Header();
        private readonly List<CheckBox> _stepChecks = new List<CheckBox>();
        private readonly List<string> _stepNames = new List<string>();
        private readonly List<Control> _fullWidth = new List<Control>();
        private readonly List<FlowLayoutPanel> _tileRows = new List<FlowLayoutPanel>();
        private Button _run;
        private Label _runNote;
        private TraceUiState _state;
        private bool _loading;

        public TracePanel()
        {
            SuspendLayout();
            Dock = DockStyle.Fill;
            BackColor = Surface;
            Font = new Font("Segoe UI", 9f);
            AutoScaleMode = AutoScaleMode.None;

            _state = TraceUiState.Load(TraceUiState.DefaultPath());

            _header.Dock = DockStyle.Top;
            _header.Height = Px(58);
            UpdateDocumentName();

            _scroll.Dock = DockStyle.Fill;
            _scroll.AutoScroll = true;
            _scroll.BackColor = Surface;
            _scroll.Padding = new Padding(Px(10), Px(8), Px(10), Px(8));

            _stack.FlowDirection = FlowDirection.TopDown;
            _stack.WrapContents = false;
            _stack.AutoSize = true;
            _stack.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _stack.Dock = DockStyle.Top;
            _stack.BackColor = Surface;
            _stack.Margin = Padding.Empty;

            BuildRunSection();
            foreach (var group in TraceCatalog.Groups) BuildGroup(group);
            BuildFooter();

            _scroll.Controls.Add(_stack);
            Controls.Add(_scroll);
            Controls.Add(_header);

            _scroll.Resize += (s, e) => FitWidths();
            ResumeLayout(true);
            FitWidths();

            try { AcadApp.DocumentManager.DocumentBecameCurrent += OnDocumentChanged; }
            catch (System.Exception) { }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try { AcadApp.DocumentManager.DocumentBecameCurrent -= OnDocumentChanged; }
                catch (System.Exception) { }
                _tips.Dispose();
            }
            base.Dispose(disposing);
        }

        // ---------------------------------------------------------------- layout helpers

        private int Px(int px)
        {
            return (int)Math.Round(px * DeviceDpi / 96.0);
        }

        private void FitWidths()
        {
            var width = Math.Max(Px(240), _scroll.ClientSize.Width - _scroll.Padding.Horizontal);
            foreach (var c in _fullWidth) c.Width = width;
            foreach (var row in _tileRows)
            {
                row.MaximumSize = new Size(width, 0);
                row.Width = width;
            }
        }

        // ------------------------------------------------------------------- Run TRACE

        private void BuildRunSection()
        {
            _run = new Button
            {
                Text = "Run TRACE",
                Height = Px(46),
                FlatStyle = FlatStyle.Flat,
                BackColor = Blue,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 12f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                UseMnemonic = false,
                Margin = new Padding(0, 0, 0, Px(6))
            };
            _run.FlatAppearance.BorderSize = 0;
            _run.Click += (s, e) =>
            {
                SaveSelection();
                TraceDispatch.Send(TraceCatalog.RunCommand);
            };
            _tips.SetToolTip(_run, "Run the ticked steps below. Your own edits are left alone, and running again never duplicates.");
            _stack.Controls.Add(_run);
            _fullWidth.Add(_run);

            var caption = new Label
            {
                Text = "Steps to run",
                AutoSize = true,
                ForeColor = Slate,
                Margin = new Padding(0, 0, 0, Px(2))
            };
            _stack.Controls.Add(caption);

            // The pipeline decides what the steps are and what order they run in.
            var steps = new FtfPipeline().Steps;
            var selected = new HashSet<string>(
                _state.SelectedSteps(steps.Select(s => s.Name)), StringComparer.OrdinalIgnoreCase);

            _loading = true;
            foreach (var step in steps)
            {
                var check = new CheckBox
                {
                    Text = TraceCatalog.StepTitle(step.Name),
                    Checked = selected.Contains(step.Name),
                    AutoSize = true,
                    UseMnemonic = false,
                    Margin = new Padding(Px(4), 0, 0, Px(1))
                };
                check.CheckedChanged += (s, e) => { if (!_loading) SaveSelection(); };
                _tips.SetToolTip(check, step.Description);
                _stack.Controls.Add(check);
                _stepChecks.Add(check);
                _stepNames.Add(step.Name);
            }
            _loading = false;

            _runNote = new Label
            {
                AutoSize = true,
                ForeColor = Color.FromArgb(0x9A, 0x5B, 0x00),
                Margin = new Padding(0, Px(2), 0, Px(6)),
                Text = string.Empty
            };
            _stack.Controls.Add(_runNote);
            UpdateRunState();
        }

        private void SaveSelection()
        {
            var ticked = new List<string>();
            for (var i = 0; i < _stepChecks.Count; i++)
                if (_stepChecks[i].Checked) ticked.Add(_stepNames[i]);

            _state.SetSelectedSteps(ticked);
            _state.Save(TraceUiState.DefaultPath());
            UpdateRunState();
        }

        private void UpdateRunState()
        {
            var any = _stepChecks.Any(c => c.Checked);
            _run.Enabled = any;
            _run.BackColor = any ? Blue : Color.FromArgb(0x9F, 0xB4, 0xC9);
            _runNote.Text = any ? string.Empty : "Tick at least one step to run.";
        }

        // ---------------------------------------------------------------------- groups

        private void BuildGroup(TraceGroup group)
        {
            var tiles = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Surface,
                Margin = new Padding(0, 0, 0, Px(4))
            };

            foreach (var b in group.Buttons) tiles.Controls.Add(Tile(b));

            var title = group.Title;
            var header = new Label
            {
                Text = "▾  " + title.ToUpperInvariant(),
                AutoSize = false,
                Height = Px(26),
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Slate,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Margin = new Padding(0, Px(6), 0, 0),
                UseMnemonic = false
            };
            header.Click += (s, e) =>
            {
                tiles.Visible = !tiles.Visible;
                header.Text = (tiles.Visible ? "▾  " : "▸  ") + title.ToUpperInvariant();
            };
            _tips.SetToolTip(header, "Click to collapse or expand");

            _stack.Controls.Add(header);
            _stack.Controls.Add(tiles);
            _fullWidth.Add(header);
            _tileRows.Add(tiles);
        }

        private Button Tile(TraceButton b)
        {
            var button = new Button
            {
                Text = b.Text,
                Image = TraceIconGdi.Render(b.Icon, Px(24), true),
                TextImageRelation = TextImageRelation.ImageAboveText,
                ImageAlign = ContentAlignment.TopCenter,
                TextAlign = ContentAlignment.BottomCenter,
                Width = Px(86),
                Height = Px(68),
                Margin = new Padding(0, 0, Px(6), Px(6)),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.White,
                ForeColor = Navy,
                Font = new Font("Segoe UI", 8f),
                UseMnemonic = false,
                Cursor = Cursors.Hand
            };
            button.FlatAppearance.BorderColor = Border;
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(0xE3, 0xEE, 0xF9);
            var command = b.Command;
            button.Click += (s, e) => TraceDispatch.Send(command);
            _tips.SetToolTip(button, b.Tip);
            return button;
        }

        // ---------------------------------------------------------------------- footer

        private void BuildFooter()
        {
            var version = typeof(TracePanel).Assembly.GetName().Version;
            var footer = new Label
            {
                Text = "TRACE " + (version == null ? string.Empty : version.ToString(3)) +
                       "  ·  Survey Drafting Automation for Civil 3D",
                AutoSize = false,
                Height = Px(34),
                ForeColor = Slate,
                Font = new Font("Segoe UI", 8f),
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0, Px(8), 0, 0),
                UseMnemonic = false
            };
            _stack.Controls.Add(footer);
            _fullWidth.Add(footer);
        }

        // --------------------------------------------------------------- drawing name

        private void OnDocumentChanged(object sender, DocumentCollectionEventArgs e)
        {
            UpdateDocumentName();
        }

        private void UpdateDocumentName()
        {
            try
            {
                var doc = AcadApp.DocumentManager.MdiActiveDocument;
                _header.Subtitle = doc == null ? "No drawing open" : System.IO.Path.GetFileName(doc.Name);
            }
            catch (System.Exception)
            {
                _header.Subtitle = string.Empty;
            }
        }

        // ---------------------------------------------------------------------- header

        private sealed class Header : Panel
        {
            private string _subtitle = string.Empty;

            public Header()
            {
                DoubleBuffered = true;
                BackColor = Navy;
            }

            public string Subtitle
            {
                get { return _subtitle; }
                set { _subtitle = value ?? string.Empty; Invalidate(); }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                var g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                var tile = Height - 2 * (Height / 6);
                var top = (Height - tile) / 2f;
                TraceIconGdi.Draw(g, TraceIcons.Get("trace"), 12f, top, tile, false);

                var textX = 12f + tile + 10f;
                using (var title = new Font("Segoe UI", 15f, FontStyle.Bold | FontStyle.Italic))
                using (var sub = new Font("Segoe UI", 8f))
                using (var white = new SolidBrush(Color.White))
                using (var light = new SolidBrush(Color.FromArgb(0x9F, 0xC0, 0xE0)))
                {
                    g.DrawString("TRACE", title, white, textX, top - 3f);
                    g.DrawString(_subtitle, sub, light, textX, top + title.GetHeight(g) - 2f);
                }
            }
        }
    }
}
#endif
