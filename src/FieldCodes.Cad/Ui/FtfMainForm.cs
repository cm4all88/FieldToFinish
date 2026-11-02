using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

using FieldCodes.Cad.Setup;
using FieldCodes.Review;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace FieldCodes.Cad.Ui
{
    /// <summary>
    /// The Field to Finish window -- the one command the department remembers.
    ///
    /// Left navigation, pages on the right: Overview and Process are fully built,
    /// the remaining pages launch existing tools or hold their place until each area
    /// is designed properly. Everything the window does goes through the same code
    /// the commands use -- FtfPipeline for processing, the command classes for tools
    /// -- so the window, FTFRUN and the individual FTF* commands cannot drift apart.
    ///
    /// Modeless on purpose: a modal window would block the editor, and finishing
    /// stages legitimately prompt (the schedule asks for a corner on its first run).
    ///
    /// UI only -- no processing logic lives in this file.
    ///
    /// UNTESTED: never shown in AutoCAD in this form.
    /// </summary>
    internal sealed class FtfMainForm : Form
    {
        private readonly FtfPipeline _pipeline = new FtfPipeline();

        // navigation
        private readonly Dictionary<string, Panel> _pages =
            new Dictionary<string, Panel>(StringComparer.Ordinal);
        private readonly Dictionary<string, Button> _navButtons =
            new Dictionary<string, Button>(StringComparer.Ordinal);
        private Panel _content;
        private string _currentPage;

        // overview
        private Label _overviewDrawing;
        private Label _overviewSummary;
        private LinkLabel _overviewAttention;
        private Label _buildNote;
        private Label _rulesProblem;

        // process
        private readonly List<CheckBox> _stageChecks = new List<CheckBox>();

        // feature review / unknown codes / reports
        private Label _unknownSummary;
        private ComboBox _reviewFilter;
        private ListView _reviewGrid;
        private TextBox _reviewDetail;
        private ListView _unknownGrid;
        private TextBox _runSummary;
        private IList<ReviewRow> _reviewRows;
        private bool _reviewScanned;

        // line features
        private ListView _lineGrid;
        private Label _lineSummary;
        private bool _lineworkScanned;
        private SettingsPanel _settingsPanel;
        private string _pendingSettingsSection;

        // log
        private Panel _logPanel;
        private TextBox _log;

        // every button that must lock while AutoCAD is working
        private readonly List<Button> _actionButtons = new List<Button>();

        private FtfDrawingStatus _status;
        private bool _busy;

        private static readonly Color NavBack = Color.FromArgb(243, 243, 245);
        private static readonly Color NavSelected = Color.White;
        private static readonly Color CaptionGray = Color.FromArgb(110, 110, 115);
        private static readonly Color Accent = Color.FromArgb(0, 90, 158);

        public FtfMainForm()
        {
            SuspendLayout();

            Text = "Field to Finish";
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = true;
            MaximizeBox = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.White;

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 2
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 168));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            root.Controls.Add(BuildNav(), 0, 0);

            _content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(22, 16, 22, 12) };
            root.Controls.Add(_content, 1, 0);

            root.Controls.Add(BuildLogPanel(), 0, 1);
            root.SetColumnSpan(_logPanel, 2);

            Controls.Add(root);

            BuildPages();

            var working = Screen.PrimaryScreen.WorkingArea;
            ClientSize = new Size(Math.Min(880, working.Width - 80),
                                  Math.Min(600, working.Height - 80));
            MinimumSize = new Size(780, 560);

            ResumeLayout(true);

            ShowPage("Overview");
            Load += (s, e) => RefreshStatus();
        }

        // ============================================================== navigation

        private Control BuildNav()
        {
            var nav = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = NavBack,
                Padding = new Padding(0, 14, 0, 0)
            };

            var stack = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Padding = new Padding(0, 4, 0, 0)
            };

            var title = new Label
            {
                Text = "FIELD TO FINISH",
                Font = new Font(SystemFonts.DefaultFont.FontFamily, 9.5f, FontStyle.Bold),
                ForeColor = Accent,
                AutoSize = true,
                Margin = new Padding(16, 0, 0, 12)
            };
            stack.Controls.Add(title);

            // The nav is a purpose-shaped hierarchy, in the order the work happens:
            // see where the drawing stands and run the finishing; review what came
            // out; adjust the standards; look something up. Each entry is
            // "page key|display name" -- keys never change, display can.
            foreach (var group in new[]
            {
                new[] { "DRAWING", "Overview|Overview", "Process|Process" },
                new[] { "REVIEW", "Feature Review|Points", "Line Features|Lines",
                        "Unknown Codes|Unknown Codes", "Reports|Reports" },
                new[] { "SETUP", "Settings|Settings", "Standards|Standards" },
                new[] { "HELP", "Commands|Commands", "About|About" }
            })
            {
                stack.Controls.Add(new Label
                {
                    Text = group[0],
                    Font = new Font(SystemFonts.DefaultFont.FontFamily, 7.5f, FontStyle.Bold),
                    ForeColor = Color.FromArgb(130, 135, 145),
                    AutoSize = true,
                    Margin = new Padding(16, 10, 0, 2)
                });

                foreach (var entry in group.Skip(1))
                    AddNavButton(stack, entry.Split('|')[0], entry.Split('|')[1]);
            }

            nav.Controls.Add(stack);
            return nav;
        }

        private void AddNavButton(FlowLayoutPanel stack, string name, string display)
        {
            {
                var button = new Button
                {
                    Text = "  " + display,
                    TextAlign = ContentAlignment.MiddleLeft,
                    FlatStyle = FlatStyle.Flat,
                    BackColor = NavBack,
                    Size = new Size(168, 34),
                    Margin = new Padding(0),
                    TabStop = false
                };
                button.FlatAppearance.BorderSize = 0;
                button.FlatAppearance.MouseOverBackColor = Color.FromArgb(232, 236, 240);
                button.Click += (s, e) => ShowPage(name);
                button.Paint += (s, e) =>
                {
                    // A 3px accent bar marks the selected page far more clearly than
                    // background colour alone.
                    if (Equals(button.Tag, "selected"))
                        using (var brush = new SolidBrush(Accent))
                            e.Graphics.FillRectangle(brush, 0, 2, 3, button.Height - 4);
                };

                _navButtons[name] = button;
                stack.Controls.Add(button);
            }
        }

        private void ShowPage(string name)
        {
            Panel page;
            if (!_pages.TryGetValue(name, out page)) return;

            foreach (var pair in _pages) pair.Value.Visible = false;
            page.Visible = true;
            _currentPage = name;

            foreach (var pair in _navButtons)
            {
                var selected = pair.Key == name;
                pair.Value.BackColor = selected ? NavSelected : NavBack;
                pair.Value.Font = selected
                    ? new Font(SystemFonts.DefaultFont, FontStyle.Bold)
                    : SystemFonts.DefaultFont;
                pair.Value.Tag = selected ? "selected" : null;
                pair.Value.Invalidate();
            }

            if (name == "Settings") EnsureSettingsBuilt();
            if (name == "Feature Review" || name == "Unknown Codes") EnsureReviewScanned();
            if (name == "Standards") EnsureStandardsBuilt();
            if (name == "Line Features") EnsureLineworkScanned();
        }

        private Panel NewPage(string name)
        {
            var page = new Panel { Dock = DockStyle.Fill, Visible = false, AutoScroll = true };
            _pages[name] = page;
            _content.Controls.Add(page);
            return page;
        }

        private void BuildPages()
        {
            BuildOverviewPage();
            BuildProcessPage();
            BuildSettingsPage();
            BuildStandardsPage();
            BuildFeatureReviewPage();
            BuildLineFeaturesPage();
            BuildUnknownCodesPage();
            BuildReportsPage();
            BuildCommandsPage();
            BuildAboutPage();
        }

        // ============================================================ line features

        private static readonly string[] LineFilters =
        {
            "All lines", "Needs attention", "Ready to label", "Below minimum length",
            "Not line features"
        };

        private ComboBox _lineFilter;
        private TextBox _lineDetail;
        private ListViewSorting _lineSort;
        private FtfLineworkResult _linework;

        private void BuildLineFeaturesPage()
        {
            var page = NewPage("Line Features");

            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1 };
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            var top = TopStack();
            PageHeader(top, "Lines");
            Caption(top, "Civil 3D creates the figures and linework; FTF labels the existing " +
                         "lines. Label one with FTFLABELLINE: click the line, slide the " +
                         "preview, click to place. Select a line to see why it has its " +
                         "status and which settings govern its label. Scanning is read-only.");

            _lineSummary = Caption(top, "...");

            var toolbar = ToolRow();
            _lineFilter = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
            foreach (var f in LineFilters) _lineFilter.Items.Add(f);
            _lineFilter.SelectedIndex = 0;
            _lineFilter.SelectedIndexChanged += (s, e) => ApplyLineFilter();
            toolbar.Controls.Add(_lineFilter);

            var rescan = ToolButton("Scan Drawing (read-only)", null, true);
            rescan.Click += (s, e) => { _lineworkScanned = false; EnsureLineworkScanned(); };
            toolbar.Controls.Add(rescan);

            var labelLine = ToolButton("Label a Line...",
                "Starts FTFLABELLINE in the drawing: click a line, slide the label where it " +
                "belongs, click to place.", true);
            labelLine.Click += (s, e) => StartLabelLine();
            toolbar.Controls.Add(labelLine);

            var settings = ToolButton("Line Label Settings...",
                "Opens Settings > Line Labels at the selected line's feature.", false);
            settings.Click += (s, e) => OpenLineLabelSettings();
            toolbar.Controls.Add(settings);

            top.Controls.Add(toolbar);
            grid.Controls.Add(top, 0, 0);

            _lineGrid = DetailsList();
            _lineGrid.Columns.Add("Status", 120);
            _lineGrid.Columns.Add("Reason", 260);
            _lineGrid.Columns.Add("Feature", 115);
            _lineGrid.Columns.Add("Code", 55);
            _lineGrid.Columns.Add("Length", 60);
            _lineGrid.Columns.Add("Layer", 150);
            _lineGrid.Columns.Add("Figure", 65);
            _lineGrid.Columns.Add("Civil 3D object", 95);
            _lineSort = ListViewSorting.Attach(_lineGrid);
            _lineGrid.SelectedIndexChanged += (s, e) => ShowLineDetail();
            _lineGrid.DoubleClick += (s, e) => OpenLineLabelSettings();

            _lineDetail = DetailBox();
            grid.Controls.Add(ListAndDetail(_lineGrid, _lineDetail, 170), 0, 1);

            page.Controls.Add(grid);
        }

        /// <summary>
        /// Fills the Line Features inventory from one read-only scan. Nothing in this
        /// path creates, modifies or stamps anything -- Civil 3D owns the linework.
        /// </summary>
        private void EnsureLineworkScanned()
        {
            if (_lineworkScanned || _busy) return;
            _lineworkScanned = true;

            InAutoCad(null, () =>
            {
                var result = FtfLineworkService.Scan();
                _linework = result;

                if (result.RulesError != null)
                    Log("Rules problem: " + result.RulesError);

                ApplyLineFilter();

                var counts = FieldCodes.Review.LineDiagnostics.Count(
                    result.Items.Select(i => i.Status));
                var own = result.FtfOwnedByKind.Values.Sum();
                _lineSummary.Text = string.Format(
                    "{0} line object(s): {1}{2}",
                    result.Items.Count, FieldCodes.Review.LineDiagnostics.Summary(counts),
                    own > 0
                        ? string.Format(" FTF's own curves ({0}: driplines, leaders...) are not listed.", own)
                        : string.Empty);
            });
        }

        private void ApplyLineFilter()
        {
            if (_lineGrid == null || _linework == null) return;

            var filter = _lineFilter.SelectedIndex < 0
                ? LineFilters[0]
                : (string)_lineFilter.SelectedItem;

            var items = new List<ListViewItem>();
            foreach (var line in _linework.Items)
            {
                if (!MatchesLineFilter(line.Status, filter)) continue;

                var row = line.Row;
                var item = new ListViewItem(line.Status.Short);
                item.SubItems.Add(line.Status.Reason);
                item.SubItems.Add(string.IsNullOrEmpty(row.FeatureName) ? "-" : row.FeatureName);
                item.SubItems.Add(string.IsNullOrEmpty(row.Codes) ? "-" : row.Codes);
                item.SubItems.Add(row.LengthText);
                item.SubItems.Add(row.Layer);
                item.SubItems.Add(string.IsNullOrEmpty(row.FigureName) ? "-" : row.FigureName);
                item.SubItems.Add(row.EntityType);
                item.Tag = line;

                if (line.Status.NeedsAttention)
                    item.ForeColor = Amber;
                else if (line.Status.Kind != FieldCodes.Review.LineStatusKind.Ready)
                    item.ForeColor = CaptionGray;

                items.Add(item);
            }

            _lineGrid.BeginUpdate();
            _lineGrid.Items.Clear();
            _lineGrid.Items.AddRange(items.ToArray());
            _lineGrid.EndUpdate();

            _lineDetail.Text = string.Format(
                "{0} line(s) shown - {1}. Select a line to see what decides its label; " +
                "double-click to open its label settings.", items.Count, filter);
        }

        private static bool MatchesLineFilter(FieldCodes.Review.LineStatus status, string filter)
        {
            switch (filter)
            {
                case "Needs attention": return status.NeedsAttention;
                case "Ready to label": return status.Kind == FieldCodes.Review.LineStatusKind.Ready;
                case "Below minimum length": return status.Kind == FieldCodes.Review.LineStatusKind.TooShort;
                case "Not line features":
                    return status.Kind == FieldCodes.Review.LineStatusKind.NotConfigured ||
                           status.Kind == FieldCodes.Review.LineStatusKind.IgnoredName;
                default: return true;
            }
        }

        private FtfLineItem SelectedLine()
        {
            return _lineGrid != null && _lineGrid.SelectedItems.Count > 0
                ? _lineGrid.SelectedItems[0].Tag as FtfLineItem
                : null;
        }

        private void ShowLineDetail()
        {
            var line = SelectedLine();
            if (line == null) return;

            var row = line.Row;
            var lines = new List<string>();
            lines.Add(string.Format("{0}{1} - {2}{3}, layer {4}, length {5}",
                string.IsNullOrEmpty(row.FeatureName) || row.FeatureName == "-" ? "Unidentified line" : row.FeatureName,
                string.IsNullOrEmpty(row.Codes) || row.Codes == "-" ? string.Empty : " (" + row.Codes + ")",
                row.EntityType,
                string.IsNullOrEmpty(row.FigureName) ? string.Empty : " " + row.FigureName,
                row.Layer, row.LengthText));
            lines.Add(line.Status.Short + ": " + line.Status.Reason);
            lines.Add("Next: " + line.Status.Action);
            if (row.Source != FieldCodes.Linework.LineIdentitySource.None)
                lines.Add("Identified by " + row.SourceText + ".");
            if (!string.IsNullOrEmpty(row.TrimbleName))
                lines.Add("TBC name: " + row.TrimbleName);

            lines.Add(string.Empty);
            lines.Add("WHAT DECIDES THIS LINE'S LABEL (current value - where it is set)");
            foreach (var setting in line.Settings)
                lines.Add(string.Format("  {0}: {1}   [{2}]", setting.Name, setting.Value, setting.Where));

            _lineDetail.Text = string.Join(Environment.NewLine, lines.ToArray());
        }

        /// <summary>Settings > Line Labels, told which feature the person picked.
        /// Never places or changes a label: FTFLABELLINE stays the way to label.</summary>
        private void OpenLineLabelSettings()
        {
            var line = SelectedLine();
            if (line != null)
            {
                var codes = line.Candidates.Select(c => c.Code).ToList();
                LineLabelNavigation.Request(codes.FirstOrDefault(), codes,
                                            line.Row.FigureName, line.Row.Layer);
            }
            else
            {
                LineLabelNavigation.Clear();
            }

            OpenSettingsSection("Line Labels");
        }

        // ================================================================ overview

        // Value labels of the two status columns, by row name.
        private readonly Dictionary<string, Label> _drawingStats =
            new Dictionary<string, Label>(StringComparer.Ordinal);
        private readonly Dictionary<string, Label> _sessionStats =
            new Dictionary<string, Label>(StringComparer.Ordinal);
        private Label _overviewConfig;

        // This session: what this window has done since it opened -- kept apart from
        // what the drawing already held when it was opened.
        private readonly DateTime _sessionOpened = DateTime.Now;
        private int _sessionRuns;
        private FieldCodes.Reporting.ProcessingRecord _sessionLastRun;
        private readonly Dictionary<string, int> _sessionOwnedChange =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private bool _sessionOwnedMeasured;
        private IDictionary<string, int> _ownedBeforeRun;

        private void BuildOverviewPage()
        {
            var page = NewPage("Overview");
            var stack = PageStack(page);

            PageHeader(stack, "Overview");

            stack.Controls.Add(SmallCaps("CURRENT DRAWING", new Padding(1, 2, 0, 0)));

            _overviewDrawing = new Label
            {
                Text = "...",
                Font = new Font(SystemFonts.DefaultFont.FontFamily, 11f, FontStyle.Bold),
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 6)
            };
            stack.Controls.Add(_overviewDrawing);

            _overviewSummary = new Label
            {
                Text = "...",
                AutoSize = true,
                MaximumSize = new Size(620, 0),
                Margin = new Padding(0, 0, 0, 2)
            };
            stack.Controls.Add(_overviewSummary);

            _overviewAttention = new LinkLabel
            {
                Text = string.Empty,
                AutoSize = true,
                MaximumSize = new Size(620, 0),
                LinkColor = Amber,
                Margin = new Padding(0, 2, 0, 0),
                Visible = false
            };
            _overviewAttention.LinkClicked += (s, e) =>
                JumpToUnknownCodes(_status != null && _status.Errors > 0 ? UnknownErrors : UnknownAttention);
            stack.Controls.Add(_overviewAttention);

            _rulesProblem = new Label
            {
                Text = string.Empty,
                AutoSize = true,
                MaximumSize = new Size(620, 0),
                ForeColor = ErrorRed,
                Margin = new Padding(0, 2, 0, 4)
            };
            stack.Controls.Add(_rulesProblem);

            // Two columns: what the drawing holds (including earlier sessions' work)
            // beside what this window has done since it opened.
            var columns = new TableLayoutPanel
            {
                ColumnCount = 2,
                RowCount = 1,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0, 4, 0, 4)
            };
            columns.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            columns.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            columns.Controls.Add(StatsBlock("IN THIS DRAWING",
                "Read from the drawing now, including earlier sessions' work.", _drawingStats,
                new[]
                {
                    "COGO points", "Eligible for FTF", "  processed", "  errors",
                    "Need configuration", "Skipped on purpose", "FTF objects",
                    "Last processed", "Last outcome"
                }), 0, 0);
            columns.Controls.Add(StatsBlock("THIS SESSION",
                "What this window has done since it opened.", _sessionStats,
                new[] { "Window opened", "Processing runs", "Last run", "FTF objects", "Review scan" }),
                1, 0);
            stack.Controls.Add(columns);

            WireJump(_drawingStats["errors"], UnknownErrors);
            WireJump(_drawingStats["Need configuration"], UnknownConfiguration);
            WireJump(_drawingStats["Skipped on purpose"], UnknownInformational);

            _overviewConfig = new Label
            {
                Text = string.Empty,
                AutoSize = true,
                MaximumSize = new Size(620, 0),
                ForeColor = CaptionGray,
                Margin = new Padding(0, 0, 0, 6)
            };
            stack.Controls.Add(_overviewConfig);

            // The three things a surveyor actually opens this window to do.
            var actions = new FlowLayoutPanel
            {
                AutoSize = true,
                WrapContents = false,
                Margin = new Padding(0, 4, 0, 2)
            };

            var run = PrimaryButton("Process Drawing",
                "Runs every finishing stage, in order: point handling, driplines, labels, " +
                "tags, the schedule and draw order.");
            run.Click += (s, e) => RunStages(null);
            actions.Controls.Add(run);

            var labelLine = SecondaryButton("Label a Line",
                "Starts FTFLABELLINE in the drawing: click a line, slide the label where it " +
                "belongs, click to place.");
            labelLine.Margin = new Padding(8, 0, 0, 0);
            labelLine.Click += (s, e) => StartLabelLine();
            actions.Controls.Add(labelLine);

            var review = SecondaryButton("Review What FTF Will Do",
                "A read-only preview of every point -- nothing in the drawing changes.");
            review.Margin = new Padding(8, 0, 0, 0);
            review.Click += (s, e) => ShowPage("Feature Review");
            actions.Controls.Add(review);

            stack.Controls.Add(actions);

            var refresh = LinkButton("Refresh Status");
            refresh.Margin = new Padding(0, 4, 0, 0);
            refresh.Click += (s, e) => RefreshStatus();
            stack.Controls.Add(refresh);

            Caption(stack, "Process Drawing runs all the automatic finishing. Label a Line " +
                           "starts FTFLABELLINE: click a line, slide the label, click to place. " +
                           "The review is read-only.");

            // Which FTF this is, and -- the one thing worth interrupting for -- that a newer one
            // from the office will install itself at close. Nothing to answer either way.
            _buildNote = new Label
            {
                Text = string.Empty,
                AutoSize = true,
                MaximumSize = new Size(620, 0),
                ForeColor = CaptionGray,
                Margin = new Padding(0, 0, 0, 6)
            };
            stack.Controls.Add(_buildNote);
        }

        /// <summary>A titled two-column block of "name  value" rows. The value labels
        /// are returned through <paramref name="values"/>, keyed by the trimmed name;
        /// a name starting with spaces is indented under the row above.</summary>
        private static Control StatsBlock(string title, string note, IDictionary<string, Label> values,
                                          string[] rows)
        {
            var grid = new TableLayoutPanel
            {
                ColumnCount = 2,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0, 0, 18, 0)
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 122));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var header = SmallCaps(title, new Padding(0, 4, 0, 0));
            grid.Controls.Add(header, 0, 0);
            grid.SetColumnSpan(header, 2);

            var caption = new Label
            {
                Text = note,
                AutoSize = true,
                MaximumSize = new Size(300, 0),
                ForeColor = CaptionGray,
                Font = new Font(SystemFonts.DefaultFont.FontFamily, 7.5f),
                Margin = new Padding(0, 0, 0, 4)
            };
            grid.Controls.Add(caption, 0, 1);
            grid.SetColumnSpan(caption, 2);

            var r = 2;
            foreach (var name in rows)
            {
                var indented = name.StartsWith(" ", StringComparison.Ordinal);
                var key = name.Trim();

                grid.Controls.Add(new Label
                {
                    Text = key,
                    AutoSize = true,
                    ForeColor = CaptionGray,
                    Margin = new Padding(indented ? 12 : 0, 2, 4, 2)
                }, 0, r);

                var value = new Label
                {
                    Text = "-",
                    AutoSize = true,
                    MaximumSize = new Size(190, 0),
                    Margin = new Padding(0, 2, 0, 2)
                };
                grid.Controls.Add(value, 1, r);
                values[key] = value;
                r++;
            }

            return grid;
        }

        private static Label SmallCaps(string text, Padding margin)
        {
            return new Label
            {
                Text = text,
                Font = new Font(SystemFonts.DefaultFont.FontFamily, 7.5f, FontStyle.Bold),
                ForeColor = CaptionGray,
                AutoSize = true,
                Margin = margin
            };
        }

        /// <summary>Makes a status value open Unknown Codes at a category.</summary>
        private void WireJump(Label value, string filter)
        {
            value.Cursor = Cursors.Hand;
            value.Click += (s, e) => JumpToUnknownCodes(filter);
        }

        private static void SetStat(IDictionary<string, Label> values, string key, string text, Color color)
        {
            Label label;
            if (!values.TryGetValue(key, out label)) return;
            label.Text = text;
            label.ForeColor = color;
        }

        private static string N(int value)
        {
            return value.ToString("#,0", System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>Hands control to the interactive line-label command: the window
        /// gets out of the way and FTFLABELLINE starts at the command line.</summary>
        private void StartLabelLine()
        {
            try
            {
                WindowState = FormWindowState.Minimized;
                AcadApp.DocumentManager.ExecuteInApplicationContext(state =>
                {
                    var doc = AcadApp.DocumentManager.MdiActiveDocument;
                    if (doc != null)
                        doc.SendStringToExecute("FTFLABELLINE ", true, false, true);
                }, null);
            }
            catch (Exception)
            {
                // Never let a button crash the session; the command can be typed.
            }
        }

        // ================================================================= process

        private void BuildProcessPage()
        {
            var page = NewPage("Process");
            var stack = PageStack(page);

            PageHeader(stack, "Process");
            Caption(stack, "Finishing stages run in this order regardless of selection; " +
                           "draw order must always be last.");

            var rows = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                Margin = new Padding(0, 6, 0, 4)
            };

            foreach (var step in _pipeline.Steps)
            {
                var check = new CheckBox
                {
                    Text = step.Title,
                    Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
                    Checked = true,
                    AutoSize = true,
                    Margin = new Padding(2, 6, 0, 0)
                };
                var description = new Label
                {
                    Text = step.Description,
                    ForeColor = CaptionGray,
                    AutoSize = true,
                    Margin = new Padding(22, 0, 0, 4)
                };

                _stageChecks.Add(check);
                rows.Controls.Add(check);
                rows.Controls.Add(description);
            }

            stack.Controls.Add(rows);

            var selectRow = new FlowLayoutPanel
            {
                AutoSize = true,
                WrapContents = false,
                Margin = new Padding(0, 2, 0, 10)
            };
            var selectAll = LinkButton("Select All");
            selectAll.Click += (s, e) => SetAllStages(true);
            var clearAll = LinkButton("Clear All");
            clearAll.Click += (s, e) => SetAllStages(false);
            selectRow.Controls.Add(selectAll);
            selectRow.Controls.Add(clearAll);
            stack.Controls.Add(selectRow);

            SectionGap(stack);

            var buttons = new FlowLayoutPanel
            {
                AutoSize = true,
                WrapContents = false,
                Margin = new Padding(0)
            };

            var runAll = PrimaryButton("Process Drawing", "Run every finishing stage.");
            runAll.Click += (s, e) => RunStages(null);

            var runSelected = SecondaryButton("Run Selected",
                "Run only the ticked stages, in pipeline order.");
            runSelected.Margin = new Padding(10, 0, 0, 0);
            runSelected.Click += (s, e) => RunStages(SelectedStageNames());

            buttons.Controls.Add(runAll);
            buttons.Controls.Add(runSelected);
            stack.Controls.Add(buttons);

            SectionGap(stack);
            SectionHeader(stack, "Cleanup");
            Caption(stack, "Removes everything FTF created. Civil 3D points, symbols and " +
                           "linework are never touched.");

            var clean = SecondaryButton("Clean FTF Output", null);
            clean.Click += (s, e) => InAutoCad("Clean", RunClean);
            stack.Controls.Add(clean);
        }

        private void SetAllStages(bool value)
        {
            foreach (var check in _stageChecks) check.Checked = value;
        }

        private void RunClean()
        {
            new TreeCommands().FtfClean();
            _status = FtfStatusService.Gather();
            ShowStatus();
        }

        // ============================================== settings / standards / etc

        private Panel _settingsHost;
        private bool _settingsBuilt;

        private void BuildSettingsPage()
        {
            // Built lazily on first visit: the section pages need the drawing's layers
            // and text styles, which takes a document lock this window must not hold
            // at construction time.
            _settingsHost = NewPage("Settings");
            _settingsHost.Controls.Add(new Label
            {
                Text = "Loading settings...",
                ForeColor = CaptionGray,
                AutoSize = true,
                Location = new Point(4, 8)
            });
        }

        private void EnsureSettingsBuilt()
        {
            if (_settingsBuilt) return;
            _settingsBuilt = true;

            InAutoCad(null, () =>
            {
                var doc = AcadApp.DocumentManager.MdiActiveDocument;
                if (doc == null) { _settingsBuilt = false; return; }

                SetupContext context;
                using (doc.LockDocument())
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    context = SettingsCommands.BuildContext(doc.Database, tr);
                    tr.Commit();
                }

                _settingsHost.Controls.Clear();
                _settingsPanel = new SettingsPanel(context, Log);
                _settingsHost.Controls.Add(_settingsPanel);

                // A navigation request (Unknown Codes' "Configure Rules...") may have
                // arrived before the panel existed.
                if (_pendingSettingsSection != null)
                {
                    _settingsPanel.SelectSection(_pendingSettingsSection);
                    _pendingSettingsSection = null;
                }
            });
        }

        private ListView _standardsGrid;
        private bool _standardsBuilt;
        private TextBox _standardsDetail;
        private Label _standardsSource;
        private Button _standardsEdit;
        private ListViewSorting _standardsSort;

        private void BuildStandardsPage()
        {
            var page = NewPage("Standards");

            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1 };
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            var top = TopStack();
            PageHeader(top, "Standards");
            Caption(top, "What FTF applies to the Civil 3D result, and where each value is " +
                         "defined. Settings and Point Features rows change the output when " +
                         "edited -- double-click one to open its section. Rules-file rows are " +
                         "edited in the rules file; Built-in, Civil 3D and Drawing rows are " +
                         "fixed or inherited; \"Not available yet\" means nothing changes it today.");

            _standardsSource = new Label
            {
                Text = string.Empty,
                AutoSize = true,
                MaximumSize = new Size(620, 0),
                ForeColor = CaptionGray,
                Margin = new Padding(0, 0, 0, 4)
            };
            top.Controls.Add(_standardsSource);

            var toolbar = ToolRow();
            var refresh = ToolButton("Reload Standards", null, true);
            refresh.Click += (s, e) => { _standardsBuilt = false; EnsureStandardsBuilt(); };
            toolbar.Controls.Add(refresh);

            _standardsEdit = ToolButton("Edit in Settings...",
                "Opens the Settings section that changes the selected standard.", false);
            _standardsEdit.Enabled = false;
            _standardsEdit.Click += (s, e) => EditSelectedStandard();
            toolbar.Controls.Add(_standardsEdit);
            top.Controls.Add(toolbar);

            grid.Controls.Add(top, 0, 0);

            _standardsGrid = DetailsList();
            _standardsGrid.ShowGroups = true;
            _standardsGrid.ShowItemToolTips = true;
            _standardsGrid.Columns.Add("Standard", 190);
            _standardsGrid.Columns.Add("Value", 250);
            _standardsGrid.Columns.Add("Defined by", 105);
            _standardsGrid.Columns.Add("Edit in", 110);
            _standardsSort = ListViewSorting.Attach(_standardsGrid);
            _standardsGrid.SelectedIndexChanged += (s, e) => ShowStandardDetail();
            _standardsGrid.DoubleClick += (s, e) => EditSelectedStandard();

            _standardsDetail = DetailBox();
            grid.Controls.Add(ListAndDetail(_standardsGrid, _standardsDetail, 92), 0, 1);

            page.Controls.Add(grid);
        }

        /// <summary>
        /// Read-only: assembles the view from the loaded rules and resolved settings.
        /// Nothing is written and no drawing entity is touched.
        /// </summary>
        private void EnsureStandardsBuilt()
        {
            if (_standardsBuilt || _busy) return;
            _standardsBuilt = true;

            InAutoCad(null, () =>
            {
                var doc = AcadApp.DocumentManager.MdiActiveDocument;
                if (doc == null) { _standardsBuilt = false; return; }

                FieldCodes.RulesConfig rules;
                FieldCodes.Settings.FtfSettings settings;
                string settingsText;
                try
                {
                    rules = FtfSession.Rules(doc.Database);
                    settings = FtfSession.SettingsFor(doc.Database, rules);
                    settingsText = ConfigurationSummary.DescribeSettings(
                        FtfSession.ResolveSettings(doc.Database, rules));
                }
                catch (FieldCodes.ConfigException ex)
                {
                    Log("Rules problem: " + ex.Message);
                    _standardsBuilt = false;
                    return;
                }

                _standardsSource.Text =
                    "Settings in force: " + settingsText + Environment.NewLine +
                    "Rules in force: " + ConfigurationSummary.DescribeRules(FtfSession.ResolveRules(doc.Database));

                var sections = new FieldCodes.Standards.StandardsViewBuilder(rules, settings).Build();

                _standardsGrid.BeginUpdate();
                _standardsGrid.Groups.Clear();
                _standardsGrid.Items.Clear();

                var items = new List<ListViewItem>();
                foreach (var section in sections)
                {
                    var group = new ListViewGroup(section.Title.Replace("&", "&&"));
                    _standardsGrid.Groups.Add(group);

                    foreach (var entry in section.Items)
                    {
                        var item = new ListViewItem(entry.Name) { Group = group, Tag = entry };
                        item.SubItems.Add(entry.Value);
                        item.SubItems.Add(entry.Source);
                        item.SubItems.Add(entry.SettingsSection ?? "-");
                        item.ToolTipText = entry.EditHint;

                        if (!entry.Operational) item.ForeColor = CaptionGray;

                        items.Add(item);
                    }
                }

                _standardsGrid.Items.AddRange(items.ToArray());
                _standardsGrid.EndUpdate();
                _standardsDetail.Text = "Select a standard to see where it is defined and how to change it.";
                _standardsEdit.Enabled = false;
            });
        }

        private FieldCodes.Standards.StandardsItem SelectedStandard()
        {
            return _standardsGrid != null && _standardsGrid.SelectedItems.Count > 0
                ? _standardsGrid.SelectedItems[0].Tag as FieldCodes.Standards.StandardsItem
                : null;
        }

        private void ShowStandardDetail()
        {
            var entry = SelectedStandard();
            _standardsEdit.Enabled = entry != null && entry.SettingsSection != null;
            if (entry == null) return;

            var lines = new List<string>
            {
                entry.Name + ": " + entry.Value,
                "Defined by: " + entry.Source,
                "How to change it: " + (entry.EditHint ?? "-")
            };
            if (!entry.Operational)
                lines.Add("Not available yet: shown so you know the behaviour is fixed. No setting changes it.");
            else if (entry.SettingsSection != null)
                lines.Add("Double-click, or Edit in Settings..., to open Settings > " + entry.SettingsSection + ".");

            _standardsDetail.Text = string.Join(Environment.NewLine, lines.ToArray());
        }

        private void EditSelectedStandard()
        {
            var entry = SelectedStandard();
            if (entry == null || entry.SettingsSection == null) return;
            OpenSettingsSection(entry.SettingsSection);
        }

        private static readonly string[] ReviewFilters =
        {
            "All recognized", "Trees", "Power / Utility", "Signs", "Control",
            "Other point features", "Errors", "Warnings", "Needs configuration",
            "Informational (skipped)", "Everything"
        };

        private static readonly Color ErrorRed = Color.FromArgb(170, 0, 0);
        private static readonly Color Amber = Color.FromArgb(178, 108, 0);

        // review scan shared by Points, Unknown Codes and Reports
        private FtfReviewResult _review;
        private readonly Dictionary<ReviewRow, CodeIssue> _reviewIssues =
            new Dictionary<ReviewRow, CodeIssue>();
        private ListViewSorting _reviewSort;

        private void BuildFeatureReviewPage()
        {
            var page = NewPage("Feature Review");

            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1 };
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // header + toolbar
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f)); // list + detail

            var top = TopStack();
            PageHeader(top, "Points");
            Caption(top, "A read-only preview of the finishing FTF plans for this drawing. " +
                         "Scanning changes nothing: Civil 3D's points, symbols and linework " +
                         "are only read. Click a column heading to sort.");

            var toolbar = ToolRow();
            _reviewFilter = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 180
            };
            foreach (var f in ReviewFilters) _reviewFilter.Items.Add(f);
            _reviewFilter.SelectedIndex = 0;
            _reviewFilter.SelectedIndexChanged += (s, e) => ApplyReviewFilter();
            toolbar.Controls.Add(_reviewFilter);

            var rescan = ToolButton("Scan Drawing (read-only)", null, true);
            rescan.Click += (s, e) => { _reviewScanned = false; EnsureReviewScanned(); };
            toolbar.Controls.Add(rescan);
            top.Controls.Add(toolbar);

            grid.Controls.Add(top, 0, 0);

            _reviewGrid = DetailsList();
            _reviewGrid.Columns.Add("Point", 55);
            _reviewGrid.Columns.Add("Raw Description", 165);
            _reviewGrid.Columns.Add("Status", 110);
            _reviewGrid.Columns.Add("Feature", 95);
            _reviewGrid.Columns.Add("Rule", 80);
            _reviewGrid.Columns.Add("Planned Actions", 260);
            _reviewSort = ListViewSorting.Attach(_reviewGrid);
            ListViewSorting.StretchLastColumn(_reviewGrid, 220);
            _reviewGrid.SelectedIndexChanged += (s, e) =>
            {
                if (_reviewGrid.SelectedItems.Count == 0) return;
                var row = _reviewGrid.SelectedItems[0].Tag as ReviewRow;
                var detail = FeatureReviewBuilder.DetailText(row);
                CodeIssue issue;
                if (row != null && _reviewIssues.TryGetValue(row, out issue) &&
                    issue.Category != CodeIssueCategory.None)
                    detail = CodeDiagnostics.CategoryText(issue.Category) + " - " + issue.Label +
                             ": " + issue.Action + Environment.NewLine + detail;
                _reviewDetail.Text = detail;
            };

            _reviewDetail = DetailBox();
            grid.Controls.Add(ListAndDetail(_reviewGrid, _reviewDetail, 130), 0, 1);

            page.Controls.Add(grid);
        }

        // ------------------------------------------------------------- unknown codes

        private const string UnknownAttention = "Needs attention";
        private const string UnknownErrors = "Errors";
        private const string UnknownConfiguration = "Needs configuration";
        private const string UnknownInformational = "Informational";
        private const string UnknownAll = "All";

        private ListViewSorting _unknownSort;
        private TextBox _unknownDetail;
        private readonly Dictionary<string, RadioButton> _unknownFilters =
            new Dictionary<string, RadioButton>(StringComparer.Ordinal);
        private string _unknownFilter = UnknownAttention;
        private IList<CodeIssueGroup> _unknownGroups = new List<CodeIssueGroup>();
        private Button _configureRule;
        private Button _markIgnored;
        private Button _stopIgnoring;

        private void BuildUnknownCodesPage()
        {
            var page = NewPage("Unknown Codes");

            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1 };
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var top = TopStack();
            PageHeader(top, "Unknown Codes");
            _unknownSummary = Caption(top, "...");

            // One button per category, with its count: real problems apart from
            // setup decisions apart from normal conditions.
            var strip = new FlowLayoutPanel
            {
                AutoSize = true,
                WrapContents = false,
                Margin = new Padding(0, 0, 0, 2)
            };
            foreach (var name in new[]
                     {
                         UnknownAttention, UnknownErrors, UnknownConfiguration,
                         UnknownInformational, UnknownAll
                     })
            {
                var filter = name;
                var button = new RadioButton
                {
                    Text = filter,
                    Appearance = Appearance.Button,
                    AutoSize = true,
                    TextAlign = ContentAlignment.MiddleCenter,
                    MinimumSize = new Size(0, 26),
                    Margin = new Padding(0, 0, 4, 0),
                    Checked = filter == _unknownFilter
                };
                if (filter == UnknownErrors) button.ForeColor = ErrorRed;
                else if (filter == UnknownConfiguration) button.ForeColor = Amber;
                button.CheckedChanged += (s, e) =>
                {
                    if (!button.Checked) return;
                    _unknownFilter = filter;
                    PopulateUnknownGrid();
                };
                _unknownFilters[filter] = button;
                strip.Controls.Add(button);
            }
            top.Controls.Add(strip);
            grid.Controls.Add(top, 0, 0);

            _unknownGrid = DetailsList();
            _unknownGrid.Columns.Add("Category", 115);
            _unknownGrid.Columns.Add("Code", 65);
            _unknownGrid.Columns.Add("Points", 50);
            _unknownGrid.Columns.Add("Kind", 115);
            _unknownGrid.Columns.Add("Example", 150);
            _unknownGrid.Columns.Add("Reason", 300);
            _unknownSort = ListViewSorting.Attach(_unknownGrid);
            ListViewSorting.StretchLastColumn(_unknownGrid, 240);
            _unknownGrid.SelectedIndexChanged += (s, e) => ShowUnknownDetail();
            _unknownGrid.DoubleClick += (s, e) =>
            {
                var g = SelectedGroup();
                if (g != null && CanConfigure(g)) ConfigureRuleFor(g);
            };

            _unknownDetail = DetailBox();
            grid.Controls.Add(ListAndDetail(_unknownGrid, _unknownDetail, 110), 0, 1);

            var buttons = ToolRow();
            buttons.Margin = new Padding(0, 6, 0, 0);

            _configureRule = ToolButton("Configure Rule...",
                "Opens Settings > Point Features with a new rule started for the selected " +
                "code -- or, for an error, the rule the point matched.", false);
            _configureRule.Click += (s, e) =>
            {
                var g = SelectedGroup();
                if (g != null && CanConfigure(g)) ConfigureRuleFor(g);
            };
            buttons.Controls.Add(_configureRule);

            _markIgnored = ToolButton("Mark Ignored",
                "This code never needs finishing: stop reporting it. Saved in settings " +
                "(diagnostics.ignoredCodes); a rule added later still wins.", false);
            _markIgnored.Click += (s, e) =>
            {
                var g = SelectedGroup();
                if (g != null && g.Kind == CodeIssueKind.NoRule) SetCodeIgnored(g.Code, true);
            };
            buttons.Controls.Add(_markIgnored);

            _stopIgnoring = ToolButton("Stop Ignoring",
                "Report this code again under Needs Configuration.", false);
            _stopIgnoring.Click += (s, e) =>
            {
                var g = SelectedGroup();
                if (g != null && g.Kind == CodeIssueKind.IgnoredByUser) SetCodeIgnored(g.Code, false);
            };
            buttons.Controls.Add(_stopIgnoring);

            var rescan = ToolButton("Scan Drawing (read-only)", null, true);
            rescan.Margin = new Padding(16, 0, 0, 0);
            rescan.Click += (s, e) => { _reviewScanned = false; EnsureReviewScanned(); };
            buttons.Controls.Add(rescan);

            var csv = ToolButton("Open CSV Report", null, true);
            csv.Click += (s, e) => OpenReport(
                _status != null ? _status.UnhandledReportPath : null,
                "No unknown-code report yet. Run Point Finishing first.");
            buttons.Controls.Add(csv);

            grid.Controls.Add(buttons, 0, 2);
            page.Controls.Add(grid);
            UpdateUnknownButtons();
        }

        // ------------------------------------------------------------------ reports

        private static readonly string[] IssueFilters =
        {
            "Errors and warnings", "Errors", "Warnings", "Needs configuration", "All issues"
        };

        private ComboBox _issueFilter;
        private ListView _issueGrid;
        private ListViewSorting _issueSort;
        private Label _reportCounts;
        private Label _reportConfig;
        private IList<PointIssue> _issues = new List<PointIssue>();
        private readonly Dictionary<string, ReviewRow> _rowsByPoint =
            new Dictionary<string, ReviewRow>(StringComparer.OrdinalIgnoreCase);

        private void BuildReportsPage()
        {
            var page = NewPage("Reports");

            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1 };
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var top = TopStack();
            PageHeader(top, "Reports");
            _reportCounts = new Label
            {
                Text = "...",
                AutoSize = true,
                MaximumSize = new Size(620, 0),
                Margin = new Padding(0, 0, 0, 2)
            };
            top.Controls.Add(_reportCounts);
            _reportConfig = new Label
            {
                Text = string.Empty,
                AutoSize = true,
                MaximumSize = new Size(620, 0),
                ForeColor = CaptionGray,
                Margin = new Padding(0, 0, 0, 4)
            };
            top.Controls.Add(_reportConfig);

            var toolbar = ToolRow();
            _issueFilter = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
            foreach (var f in IssueFilters) _issueFilter.Items.Add(f);
            _issueFilter.SelectedIndex = 0;
            _issueFilter.SelectedIndexChanged += (s, e) => PopulateIssues();
            toolbar.Controls.Add(_issueFilter);
            var rescan = ToolButton("Scan Drawing (read-only)", null, true);
            rescan.Click += (s, e) => { _reviewScanned = false; EnsureReviewScanned(); };
            toolbar.Controls.Add(rescan);
            top.Controls.Add(toolbar);
            grid.Controls.Add(top, 0, 0);

            _issueGrid = DetailsList();
            _issueGrid.Columns.Add("Severity", 85);
            _issueGrid.Columns.Add("Point", 55);
            _issueGrid.Columns.Add("Code", 60);
            _issueGrid.Columns.Add("Kind", 110);
            _issueGrid.Columns.Add("Description", 150);
            _issueGrid.Columns.Add("Reason", 300);
            _issueSort = ListViewSorting.Attach(_issueGrid);
            ListViewSorting.StretchLastColumn(_issueGrid, 220);
            _issueGrid.SelectedIndexChanged += (s, e) => ShowIssueDetail();

            _runSummary = DetailBox();
            _runSummary.Text = "No processing has been run from this window yet.";
            grid.Controls.Add(ListAndDetail(_issueGrid, _runSummary, 140), 0, 1);

            var buttons = ToolRow();
            buttons.Margin = new Padding(0, 6, 0, 0);
            var exceptions = ToolButton("Open Exception Report (CSV)",
                "Points that matched a rule and failed inside it, written beside the drawing " +
                "by Point Finishing.", true);
            exceptions.Click += (s, e) => OpenReport(
                _status != null ? _status.ExceptionReportPath : null,
                "No exception report yet. Run Point Finishing first.");
            buttons.Controls.Add(exceptions);
            var unknown = ToolButton("Open Unknown Codes Report (CSV)",
                "Each unconfigured code with sample descriptions, written beside the drawing " +
                "by Point Finishing.", true);
            unknown.Click += (s, e) => OpenReport(
                _status != null ? _status.UnhandledReportPath : null,
                "No unknown-code report yet. Run Point Finishing first.");
            buttons.Controls.Add(unknown);
            grid.Controls.Add(buttons, 0, 2);

            page.Controls.Add(grid);

            // The list comes from the same read-only scan as Points and Unknown Codes.
            page.VisibleChanged += (s, e) => { if (page.Visible) EnsureReviewScanned(); };
        }

        // ========================================================== review scanning

        /// <summary>
        /// Fills Feature Review, Unknown Codes and Reports from one read-only scan.
        /// Nothing in this path creates, erases, rotates, labels or tags anything.
        /// </summary>
        private void EnsureReviewScanned()
        {
            // While AutoCAD is busy the scan would be refused; leave it for the next visit.
            if (_reviewScanned || _busy) return;
            _reviewScanned = true;

            InAutoCad(null, ScanReviewNow);
        }

        /// <summary>The scan itself; runs inside an AutoCAD call already under way.</summary>
        private void ScanReviewNow()
        {
            var result = FtfReviewService.Scan();
            _review = result;
            _reviewRows = result.Rows;
            _reviewScanned = true;

            _reviewIssues.Clear();
            _rowsByPoint.Clear();
            if (result.Diagnostics != null)
                foreach (var row in result.Rows)
                {
                    if (row == null || row.Parsed == null) continue;
                    _reviewIssues[row] = result.Diagnostics.Classify(row.Parsed);
                    if (!string.IsNullOrEmpty(row.PointNumber)) _rowsByPoint[row.PointNumber] = row;
                }

            _unknownGroups = result.Diagnostics != null
                ? result.Diagnostics.Group(result.Points())
                : new List<CodeIssueGroup>();
            _issues = result.Diagnostics != null
                ? result.Diagnostics.Issues(result.Points())
                : new List<PointIssue>();

            if (result.RulesError != null)
                Log("Rules problem: " + result.RulesError);

            ApplyReviewFilter();
            PopulateUnknownGrid();
            PopulateIssues();
            ShowSession();
        }

        private void ApplyReviewFilter()
        {
            if (_reviewGrid == null || _reviewRows == null) return;

            var filter = _reviewFilter.SelectedIndex < 0
                ? ReviewFilters[0]
                : (string)_reviewFilter.SelectedItem;

            var items = new List<ListViewItem>();
            foreach (var row in _reviewRows)
            {
                CodeIssue issue;
                _reviewIssues.TryGetValue(row, out issue);
                if (!MatchesFilter(row, issue, filter)) continue;

                var item = new ListViewItem(row.PointNumber);
                item.SubItems.Add(row.RawDescription);
                item.SubItems.Add(issue == null || issue.Category == CodeIssueCategory.None
                    ? row.StatusText
                    : issue.Label);
                item.SubItems.Add(CategoryName(row.Category));
                item.SubItems.Add(row.RuleId ?? "-");
                item.SubItems.Add(row.ActionsSummary);
                item.Tag = row;

                if (row.Status == ReviewStatus.Error)
                    item.ForeColor = ErrorRed;
                else if (row.Status == ReviewStatus.Warning || row.Status == ReviewStatus.NotConfigured)
                    item.ForeColor = Amber;
                else if (issue != null && issue.Category == CodeIssueCategory.Informational)
                    item.ForeColor = CaptionGray;

                items.Add(item);
            }

            _reviewGrid.BeginUpdate();
            _reviewGrid.Items.Clear();
            _reviewGrid.Items.AddRange(items.ToArray());
            _reviewGrid.EndUpdate();
            _reviewDetail.Text = string.Format("{0} point(s) shown - {1}",
                _reviewGrid.Items.Count, filter);
        }

        private static bool MatchesFilter(ReviewRow row, CodeIssue issue, string filter)
        {
            var category = issue != null ? issue.Category : CodeIssueCategory.None;
            switch (filter)
            {
                case "Trees": return row.Category == ReviewCategory.Tree;
                case "Power / Utility": return row.Category == ReviewCategory.Utility;
                case "Signs": return row.Category == ReviewCategory.Sign;
                case "Control": return row.Category == ReviewCategory.Control;
                case "Other point features": return row.Category == ReviewCategory.OtherFeature;
                case "Errors": return category == CodeIssueCategory.Error;
                case "Warnings": return row.Status == ReviewStatus.Warning;
                case "Needs configuration": return category == CodeIssueCategory.NeedsConfiguration;
                case "Informational (skipped)": return category == CodeIssueCategory.Informational;
                case "Everything": return true;
                default:    // All recognized
                    return row.Category == ReviewCategory.Tree ||
                           row.Category == ReviewCategory.Utility ||
                           row.Category == ReviewCategory.Sign ||
                           row.Category == ReviewCategory.Control ||
                           row.Category == ReviewCategory.OtherFeature;
            }
        }

        private static string CategoryName(ReviewCategory category)
        {
            switch (category)
            {
                case ReviewCategory.Tree: return "Tree";
                case ReviewCategory.Utility: return "Power / Utility";
                case ReviewCategory.Sign: return "Sign";
                case ReviewCategory.Control: return "Control";
                case ReviewCategory.OtherFeature: return "Point feature";
                case ReviewCategory.NotConfigured: return "Not configured";
                case ReviewCategory.NoData: return "No data";
                default: return "Linework / ignored";
            }
        }

        // ------------------------------------------------------------- unknown codes

        private static bool InFilter(CodeIssueGroup group, string filter)
        {
            switch (filter)
            {
                case UnknownErrors: return group.Category == CodeIssueCategory.Error;
                case UnknownConfiguration: return group.Category == CodeIssueCategory.NeedsConfiguration;
                case UnknownInformational: return group.Category == CodeIssueCategory.Informational;
                case UnknownAll: return true;
                default:
                    return group.Category == CodeIssueCategory.Error ||
                           group.Category == CodeIssueCategory.NeedsConfiguration;
            }
        }

        private void PopulateUnknownGrid()
        {
            if (_unknownGrid == null) return;
            var groups = _unknownGroups ?? new List<CodeIssueGroup>();

            // Counts on the filter buttons: points, with codes in brackets.
            foreach (var pair in _unknownFilters)
            {
                var inFilter = groups.Where(g => InFilter(g, pair.Key)).ToList();
                pair.Value.Text = string.Format("{0} ({1})", pair.Key, N(inFilter.Sum(g => g.Count)));
            }

            var items = new List<ListViewItem>();
            foreach (var group in groups)
            {
                if (!InFilter(group, _unknownFilter)) continue;

                var item = new ListViewItem(CodeDiagnostics.CategoryText(group.Category));
                item.SubItems.Add(group.Code);
                item.SubItems.Add(group.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
                item.SubItems.Add(group.Label);
                item.SubItems.Add(group.Example);
                item.SubItems.Add(group.Reason);
                item.Tag = group;

                if (group.Category == CodeIssueCategory.Error) item.ForeColor = ErrorRed;
                else if (group.Category == CodeIssueCategory.NeedsConfiguration) item.ForeColor = Amber;
                else item.ForeColor = CaptionGray;

                items.Add(item);
            }

            _unknownGrid.BeginUpdate();
            _unknownGrid.Items.Clear();
            _unknownGrid.Items.AddRange(items.ToArray());
            _unknownGrid.EndUpdate();

            var errors = groups.Where(g => g.Category == CodeIssueCategory.Error).ToList();
            var config = groups.Where(g => g.Category == CodeIssueCategory.NeedsConfiguration).ToList();
            var info = groups.Where(g => g.Category == CodeIssueCategory.Informational).ToList();
            // Before the first scan the summary comes from the status (ShowStatus).
            if (_review != null)
                _unknownSummary.Text = string.Format(
                    "Errors: {0} point(s) FTF could not finish. Needs configuration: {1} point(s) " +
                    "in {2} code(s) carry data no rule handles. Informational: {3} point(s) " +
                    "skipped on purpose (Civil 3D linework, never-draw and ignored codes, bare " +
                    "codes) -- nothing to do.",
                    N(errors.Sum(g => g.Count)), N(config.Sum(g => g.Count)), config.Count,
                    N(info.Sum(g => g.Count)));

            _unknownDetail.Text = items.Count == 0
                ? (_review == null ? "Scan the drawing to list its codes." : "Nothing in this category.")
                : "Select a code to see why it is listed and what to do. Double-click a code " +
                  "that needs configuration to start its rule.";
            UpdateUnknownButtons();
        }

        private CodeIssueGroup SelectedGroup()
        {
            return _unknownGrid != null && _unknownGrid.SelectedItems.Count > 0
                ? _unknownGrid.SelectedItems[0].Tag as CodeIssueGroup
                : null;
        }

        private static bool CanConfigure(CodeIssueGroup group)
        {
            return group.Kind == CodeIssueKind.NoRule ||
                   (group.Category == CodeIssueCategory.Error && !string.IsNullOrEmpty(group.RuleId));
        }

        private void UpdateUnknownButtons()
        {
            var group = SelectedGroup();
            if (_configureRule != null) _configureRule.Enabled = group != null && CanConfigure(group);
            if (_markIgnored != null) _markIgnored.Enabled = group != null && group.Kind == CodeIssueKind.NoRule;
            if (_stopIgnoring != null) _stopIgnoring.Enabled = group != null && group.Kind == CodeIssueKind.IgnoredByUser;
        }

        private void ShowUnknownDetail()
        {
            UpdateUnknownButtons();
            var group = SelectedGroup();
            if (group == null) return;

            var lines = new List<string>
            {
                CodeDiagnostics.CategoryText(group.Category) + ": " + group.Label + " - " + group.Code +
                    (string.IsNullOrEmpty(group.RuleId) ? string.Empty : "  (rule " + group.RuleId + ")"),
                group.Reason,
                "What to do: " + group.Action,
                string.Format("{0} point(s): {1}{2}", group.Count,
                    string.Join(", ", group.ExamplePoints.ToArray()),
                    group.Count > group.ExamplePoints.Count ? ", ..." : string.Empty)
            };
            if (group.Samples.Count > 0)
                lines.Add("Descriptions: " + string.Join("  |  ", group.Samples.ToArray()));

            _unknownDetail.Text = string.Join(Environment.NewLine, lines.ToArray());
        }

        /// <summary>Settings > Point Features, told which code (and sample) to start a
        /// rule for -- or, for an error, which rule the point matched.</summary>
        private void ConfigureRuleFor(CodeIssueGroup group)
        {
            PointFeatureNavigation.Request(
                group.Code == CodeDiagnostics.BlankCode ? null : group.Code,
                group.Example, group.RuleId);
            OpenSettingsSection("Point Features");
        }

        /// <summary>
        /// Marks a code ignored (or not) and saves it: to the settings file in force
        /// when that is the drawing's folder, a profile or your own settings, otherwise
        /// to your own settings. Then rescans so the lists move it at once.
        /// </summary>
        private void SetCodeIgnored(string code, bool ignore)
        {
            InAutoCad(null, () =>
            {
                var doc = AcadApp.DocumentManager.MdiActiveDocument;
                if (doc == null) return;

                var rules = FtfSession.Rules(doc.Database);
                var resolution = FtfSession.ResolveSettings(doc.Database, rules);
                var settings = resolution.Settings;
                if (settings.Diagnostics == null)
                    settings.Diagnostics = new FieldCodes.Settings.DiagnosticsSettings();

                var changed = ignore ? settings.Diagnostics.Ignore(code) : settings.Diagnostics.Unignore(code);
                if (!changed)
                {
                    Log(ignore ? code + " is already ignored." : code + " was not ignored.");
                    return;
                }

                var path = FieldCodes.Settings.DiagnosticsSettings.SavePathFor(resolution);
                string failure = null;
                try
                {
                    settings.Save(path);
                }
                catch (FieldCodes.ConfigException ex) { failure = ex.Message; }
                catch (IOException ex) { failure = ex.Message; }
                catch (UnauthorizedAccessException ex) { failure = ex.Message; }

                if (failure != null)
                {
                    // Put the in-memory settings back the way the file still has them.
                    if (ignore) settings.Diagnostics.Unignore(code);
                    else settings.Diagnostics.Ignore(code);
                    Log("Could not save the ignored codes to " + path + ": " + failure);
                    return;
                }

                // A save to a different file than the one in force changes which file
                // wins next time; let the session find it.
                if (!string.Equals(path, resolution.LoadedFrom, StringComparison.OrdinalIgnoreCase))
                    FtfSession.InvalidateSettings();

                Log((ignore ? "Ignoring " : "No longer ignoring ") + code + " - saved to " + path);

                ScanReviewNow();
                _status = FtfStatusService.Gather();
                ShowStatus();
            });
        }

        /// <summary>Opens Unknown Codes at a category.</summary>
        private void JumpToUnknownCodes(string filter)
        {
            RadioButton button;
            if (_unknownFilters.TryGetValue(filter, out button)) button.Checked = true;
            ShowPage("Unknown Codes");
        }

        // ------------------------------------------------------------------ reports

        private void PopulateIssues()
        {
            if (_issueGrid == null) return;

            var filter = _issueFilter.SelectedIndex < 0 ? IssueFilters[0] : (string)_issueFilter.SelectedItem;
            var items = new List<ListViewItem>();
            foreach (var issue in _issues ?? new List<PointIssue>())
            {
                var show =
                    filter == "Errors" ? issue.Severity == CodeDiagnostics.ErrorSeverity :
                    filter == "Warnings" ? issue.Severity == CodeDiagnostics.WarningSeverity :
                    filter == "Needs configuration" ? issue.Severity == CodeDiagnostics.ConfigurationSeverity :
                    filter == "All issues" ||
                        issue.Severity == CodeDiagnostics.ErrorSeverity ||
                        issue.Severity == CodeDiagnostics.WarningSeverity;
                if (!show) continue;

                var item = new ListViewItem(issue.Severity);
                item.SubItems.Add(issue.PointNumber);
                item.SubItems.Add(issue.Code);
                item.SubItems.Add(issue.Kind);
                item.SubItems.Add(issue.Description);
                item.SubItems.Add(issue.Reason);
                item.Tag = issue;
                if (issue.Severity == CodeDiagnostics.ErrorSeverity) item.ForeColor = ErrorRed;
                else item.ForeColor = Amber;
                items.Add(item);
            }

            _issueGrid.BeginUpdate();
            _issueGrid.Items.Clear();
            _issueGrid.Items.AddRange(items.ToArray());
            _issueGrid.EndUpdate();

            ShowReportSummary();
        }

        private void ShowIssueDetail()
        {
            if (_issueGrid.SelectedItems.Count == 0) { ShowReportSummary(); return; }
            var issue = _issueGrid.SelectedItems[0].Tag as PointIssue;
            if (issue == null) return;

            var text = string.Format("{0} - {1}: point {2}, code {3}{4}{5}{6}",
                issue.Severity, issue.Kind, issue.PointNumber, issue.Code,
                string.IsNullOrEmpty(issue.RuleId) ? string.Empty : ", rule " + issue.RuleId,
                Environment.NewLine, issue.Reason);

            ReviewRow row;
            if (issue.PointNumber != null && _rowsByPoint.TryGetValue(issue.PointNumber, out row))
                text += Environment.NewLine + Environment.NewLine + FeatureReviewBuilder.DetailText(row);

            _runSummary.Text = text;
        }

        /// <summary>The detail pane with nothing selected: the last run stored in the
        /// drawing, then what this session did.</summary>
        private void ShowReportSummary()
        {
            if (_runSummary == null) return;
            if (_issueGrid != null && _issueGrid.SelectedItems.Count > 0) return;   // keep the issue shown

            var lines = new List<string>();
            lines.Add("LAST PROCESSING RUN (stored in this drawing)");
            var record = _status != null ? _status.LastRun : null;
            lines.Add(record != null
                ? record.Describe()
                : "No run recorded in this drawing. Runs from this window are recorded; " +
                  "FTFRUN and single commands at the command line are not.");

            lines.Add(string.Empty);
            lines.Add("THIS SESSION");
            lines.Add(string.Format("{0} processing run(s) from this window since {1}.",
                _sessionRuns, FtfStatusService.When(_sessionOpened)));
            if (_review != null)
                lines.Add(string.Format("Review scanned at {0}: {1} issue(s) listed above.",
                    FtfStatusService.When(_review.ScannedLocal), _issueGrid != null ? _issueGrid.Items.Count : 0));

            _runSummary.Text = string.Join(Environment.NewLine, lines.ToArray());
        }

        private void OpenSettingsSection(string section)
        {
            _pendingSettingsSection = section;
            ShowPage("Settings");

            // The settings panel builds lazily; if it exists, jump now, otherwise
            // EnsureSettingsBuilt applies the pending section when it finishes.
            if (_settingsPanel != null)
            {
                _settingsPanel.SelectSection(section);
                _pendingSettingsSection = null;
            }
        }

        // ------------------------------------------------------- page building blocks

        private static FlowLayoutPanel TopStack()
        {
            return new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                AutoSize = true,
                WrapContents = false
            };
        }

        private static FlowLayoutPanel ToolRow()
        {
            return new FlowLayoutPanel
            {
                AutoSize = true,
                WrapContents = false,
                Margin = new Padding(0, 2, 0, 2)
            };
        }

        /// <summary>A compact toolbar button. <paramref name="locksWhileBusy"/> puts it
        /// with the buttons that disable while AutoCAD works; selection-driven buttons
        /// manage their own Enabled state instead.</summary>
        private Button ToolButton(string text, string hint, bool locksWhileBusy)
        {
            var button = new Button
            {
                Text = text,
                AutoSize = true,
                MinimumSize = new Size(90, 28),
                Margin = new Padding(0, 0, 6, 0)
            };
            if (!string.IsNullOrEmpty(hint)) new ToolTip().SetToolTip(button, hint);
            if (locksWhileBusy) _actionButtons.Add(button);
            return button;
        }

        private static ListView DetailsList()
        {
            return new ListView
            {
                View = View.Details,
                FullRowSelect = true,
                HideSelection = false,
                MultiSelect = false,
                Dock = DockStyle.Fill
            };
        }

        private static TextBox DetailBox()
        {
            return new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(250, 250, 252)
            };
        }

        /// <summary>A list above its detail with a draggable split; the detail keeps
        /// its height when the window grows, so the list gets the new room.</summary>
        private static SplitContainer ListAndDetail(Control list, Control detail, int detailHeight)
        {
            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                FixedPanel = FixedPanel.Panel2,
                SplitterWidth = 6,
                Margin = new Padding(0, 4, 0, 0)
            };
            split.Panel1.Controls.Add(list);
            split.Panel2.Controls.Add(detail);

            // The splitter can only be placed once the container has its real height.
            var placed = false;
            split.SizeChanged += (s, e) =>
            {
                if (placed || split.Height < detailHeight + 120) return;
                placed = true;
                try
                {
                    split.SplitterDistance = split.Height - detailHeight - split.SplitterWidth;
                }
                catch (ArgumentException) { }
                catch (InvalidOperationException) { }
            };
            return split;
        }

        /// <summary>The command reference, in the product where it belongs: what FTF
        /// adds to Civil 3D, grouped by how a surveyor reaches for it.</summary>
        private void BuildCommandsPage()
        {
            var page = NewPage("Commands");
            var stack = PageStack(page);

            PageHeader(stack, "Commands");
            Caption(stack, "Everything FTF adds to Civil 3D -- all of it also on the FTF " +
                           "ribbon tab. Every command deletes only its own previous " +
                           "output before redrawing; Civil 3D's points, symbols and " +
                           "linework are never touched.");

            CommandGroup(stack, "Daily workflow");
            CommandRow(stack, "FTF",
                "Opens this window. Never processes anything on its own.");
            CommandRow(stack, "FTFLABELALL",
                "Label everything once the points and linework are in: every " +
                "recognised line (with gate labels between marked shots), then " +
                "every point, then draw order. Labels you placed or moved by hand " +
                "are kept; run it again after more linework arrives. Lists any line " +
                "it could not label and why.");
            CommandRow(stack, "FTFLABELLINE   (or FTFL)",
                "Label a line: click it, slide the live preview along it, click to " +
                "place. The cursor picks the spot and the side; text, layer, offset " +
                "and mask come from the standards.");
            CommandRow(stack, "FTFLABELBETWEEN   (or FTFB)",
                "Label the space between two edges -- a driveway between its " +
                "edge-of-asphalt lines. Pick both edges; the label rides the " +
                "midline; click to place.");
            CommandRow(stack, "FTFLABELDRIVEWAY   (or FTFDWY)",
                "Label a driveway: pick its two edges as for FTFLABELBETWEEN. The " +
                "label is the first edge's surface type, read from its layer, with " +
                "DWY after it -- ASPH DWY off V-SURF-ASPH-E. W switches to the " +
                "spelled-out word where the feature has one (ASPHALT DWY).");
            CommandRow(stack, "FTFBENCH",
                "Draw a bench from its two end shots: pick the points (or the line " +
                "between them). The depth comes from the notes (D1.5, 1.5D, DEPTH " +
                "18IN) or is asked for; the outline is offset half the depth each " +
                "side with the ends closed, labelled with its size, BENCH 3'x1.5'.");
            CommandRow(stack, "FTFLABELSTAIRS   (or FTFS)",
                "Label a staircase: select the stair lines (each tread edge is one " +
                "line) and the step-count label places itself centred in them.");
            CommandRow(stack, "FTFSPOT",
                "Spot elevations off the surface: click, click, click. An X at " +
                "each spot, the elevation beside it at the office angle, and " +
                "close-together spots never overlap.");
            CommandRow(stack, "FTFCONTOUR",
                "Contour polylines at an exact elevation off a TIN surface: pick the " +
                "surface, type 12.6 (or 12.6, 13, 13.5, or 10 to 14 by 0.5). Every " +
                "piece where the surface crosses it, at exactly that elevation, on the " +
                "contour layer. B clips to a closed boundary, L adds elevation labels. " +
                "The surface is only read -- never rebuilt or edited.");
            CommandRow(stack, "FTFORIENT",
                "Turn the survey symbols Civil 3D already drew to face the way the features " +
                "do: signs at the traffic they face, hydrant nozzles at the street, light and " +
                "signal arms over the road, valve pipe axes along the main, arrows and painted " +
                "words with their lane. Uses the crew's measured direction first, then stop " +
                "bars, intersections and roads. Unsure ones are left alone and listed for " +
                "review. P previews; nothing but marker rotation is changed.");
            CommandRow(stack, "FTFORIENTRESET",
                "Put every symbol FTFORIENT turned back as it was, and remove its QC arrows.");
            CommandRow(stack, "FTFRUN",
                "The whole pipeline in one go -- the same thing as Process Drawing.");

            CommandGroup(stack, "Recorded surveys");
            CommandRow(stack, "FTFRECORD",
                "Upload a recorded plat, short plat, Record of Survey or BLA (PDF/TIFF/JPG/PNG). " +
                "The document is read, the bearings, distances, curves, lots, monuments and " +
                "record references are extracted with a confidence for each, and a review " +
                "table shows every call against the page. Approve, edit or reject, then Build: " +
                "the geometry is traversed from the written calls, never traced from the picture.");
            CommandRow(stack, "FTFRECORDCHECK",
                "Compares the drawing with the record: bearing, distance and curve mismatches, " +
                "closure per lot, shared boundaries, duplicates, missing lines, labels and " +
                "monuments, and layer/style against the standard. Reports; adjusts nothing.");
            CommandRow(stack, "FTFRECORDLABEL",
                "Places or refreshes the bearing/distance, curve, lot and record-vs-measured " +
                "labels for a reconstructed survey, in the office standard.");
            CommandRow(stack, "FTFRECORDSOURCE",
                "Click a reconstructed line: which document, sheet and call it came from, the " +
                "record and measured values, confidence, and the spot on the page.");
            CommandRow(stack, "FTFRECORDREBUILD",
                "Reopens the review for a stored record and rebuilds the geometry after edits.");
            CommandRow(stack, "FTFCLEAN",
                "Removes everything FTF created, including hand-placed labels.");

            CommandGroup(stack, "Individual stages (each safe to re-run)");
            CommandRow(stack, "FTFPOINTS",
                "Reads every point's field code, applies the rules, writes the " +
                "exception report. (Also answers to FTFTREES.)");
            CommandRow(stack, "FTFDRIP",
                "Tree driplines, trimmed to their outer envelope, in the configured " +
                "linetype.");
            CommandRow(stack, "FTFLABELS",
                "Point labels with collision avoidance -- trees, control aliases, and " +
                "the symbol labels (CB, SSMH, WFH...). Labels you move by hand stay " +
                "where you put them.");
            CommandRow(stack, "FTFTAGS",
                "Numbered tags (T1, P2, S3), the same numbers reissued on every run.");
            CommandRow(stack, "FTFTABLE",
                "The schedule, keeping its position on re-run.");
            CommandRow(stack, "FTFCONTROL",
                "The control table: every control shot and found monument with " +
                "point number, northing, easting and elevation. Keeps its position.");
            CommandRow(stack, "FTFLEGEND",
                "The legend, built from what the drawing actually contains: each " +
                "identified line feature drawn in its own layer's symbology.");
            CommandRow(stack, "FTFORDER",
                "Draw-order banding: maskable linework, then masks, then symbols, " +
                "then labels. Run last.");
            CommandRow(stack, "FTFLINELABELS",
                "The optional bulk line-label pass. Off by default in Settings; never " +
                "touches labels placed with FTFLABELLINE.");
            CommandRow(stack, "FTFDRAWLINE / FTFDRAFTCLEAN",
                "Draft cadastral lines by bearing and distance, and remove them. " +
                "FTFDRAFTCLEAN also manages benches drawn with FTFBENCH.");
            CommandRow(stack, "FTFLOTS",
                "Divide a platted block into lots: pick its corners, say which side the lots front, " +
                "how many and how wide, the alley, setbacks, easements and where each lot drains.");
            CommandRow(stack, "FTFROW",
                "Pick monuments, give the road and its right-of-way width: draws the right-of-way " +
                "lines with bearings and distances, the opposite line and centerline, the width " +
                "dimensions and the monument notes. Runs, blocks and intersections.");

            CommandGroup(stack, "Sheet production");
            CommandRow(stack, "FTFSHEETPLAN",
                "Lays the configured paper (17 x 11 at 1\"=20' by default) over the " +
                "site for the fewest prints and draws the proposed sheet windows " +
                "and match lines in model space.");
            CommandRow(stack, "FTFSHEETMAKE",
                "Creates one layout per drawn sheet window, each with a locked " +
                "viewport at the planned scale. Adjust the rectangles first if " +
                "you want; existing layouts are never touched.");
            CommandRow(stack, "FTFSHEETS",
                "Draws each layout viewport's plot window in model space, with " +
                "match lines where adjacent sheets meet.");
            CommandRow(stack, "FTFKEYMAP",
                "Puts the index diagram on every sheet -- the whole grid, this " +
                "sheet drawn heavy, tucked in the viewport's lower right.");

            CommandGroup(stack, "Storm / sewer dips");
            CommandRow(stack, "FTFDIP",
                "Opens the Dip Builder: pick a structure point, read the field notes " +
                "(PT 1045 SDMH / 12 RCP N 6.41 ...), confirm each pipe's connection, " +
                "draw pipes and structure labels, review QC and the field revisit list.");
            CommandRow(stack, "FTFDIPCHECK",
                "Finds structures whose survey point moved or whose rim changed and " +
                "offers to rebuild each one. Nothing changes without a yes.");
            CommandRow(stack, "FTFDIPINSPECT",
                "Click a drafted pipe or structure label to see where every number came " +
                "from: the field note, the CAD rim, the calculation and any overrides.");
            CommandRow(stack, "FTFDIPREVISIT",
                "Writes the field revisit list (missing dips, unresolved pipes...) " +
                "beside the drawing for the crew.");

            CommandGroup(stack, "Survey comparison (FTFCOMPARE)");
            CommandRow(stack, "FTFCOMPARE",
                "Opens the comparison window: choose the consultant source (xrefs or a file) and " +
                "profile, run, review the exceptions, apply the plotting changes, run final QC, " +
                "save the ACC deliverable.");
            CommandRow(stack, "FTFCOMPARERUN",
                "Runs the comparison from the command line (all survey objects, a selection, or " +
                "a window) and writes the CSV report beside the drawing.");
            CommandRow(stack, "FTFCOMPAREAPPLY   /   FTFCOMPARERESTORE",
                "Moves matched duplicates to nonplot layers (each object's own layer plus -NP, " +
                "or the fixed PMX-COMP-NP-* set, per the profile; partly represented lines are " +
                "split) -- and puts every object back exactly as it was.");
            CommandRow(stack, "FTFCOMPAREQC   /   FTFCOMPARESAVE",
                "The final QC checks, and the deliverable save to the delivery folder, which " +
                "refuses to write while a blocking check fails.");
            CommandRow(stack, "FTFCOMPAREBATCH",
                "Processes a manifest of sites in sequence; sites with unresolved results go to " +
                "a needs-review folder, never to the approved one.");
            CommandRow(stack, "FTFCOMPAREPROFILE   /   FTFCOMPARESOURCE   /   FTFCOMPAREAREA",
                "Choose the comparison profile, the consultant source and the comparison limits " +
                "for this drawing.");

            CommandGroup(stack, "Aerial base trim for the ACC merge (FTFTRIM)");
            CommandRow(stack, "FTFTRIM",
                "In the aerial base drawing with our finished site(s) attached as xrefs: inside our " +
                "limits of survey the aerial goes to its -NP layers, a line crossing the limit is cut " +
                "there with the outside part kept plotting, and every seam is checked against our " +
                "line -- clouded and noted when it is off, listed in the CSV reports.");
            CommandRow(stack, "FTFTRIMRESTORE",
                "Puts every aerial object back exactly, erases the outside pieces and the seam marks.");

            CommandGroup(stack, "Easements and profiles");
            CommandRow(stack, "STRIPEASEMENT   (or FTFEASEMENT)",
                "Builds a strip easement: an optional Point of Commencement, its angle points " +
                "(or a line), the width and an optional temporary construction easement width, " +
                "the lot lines it is trimmed to and an optional terminus corner, then keep the " +
                "right pieces in the preview. Records the ties, exact courses and each area.");
            CommandRow(stack, "FTFEASEMENTCHECK",
                "Compares every stored easement with the survey objects it was built " +
                "from and offers a rebuild where they changed.");
            CommandRow(stack, "PORTIONEASEMENT   (or FTFPORTION)",
                "An easement over part of a lot: \"the west 10 feet of the south 50 feet\". Pick " +
                "the lot, then each lot line and its distance, measured at right angles. Drawn, " +
                "hatched, dimensioned and stored for rebuilds and a draft legal description.");
            CommandRow(stack, "CONSTRUCTIONAREA   (or FTFAREA)",
                "A metes and bounds area clicked corner by corner, following lot lines or curves " +
                "where it runs along them. Own layer and hatch, each side labelled per the clicks, " +
                "Point of Beginning / Commencement leaders and the area.");
            CommandRow(stack, "FTFEASEMENTEXCLUDE",
                "Excludes an area from an easement: an existing closed boundary (a hole or a cut), or " +
                "portion calls such as EXCEPT THE NORTH 20 FEET THEREOF. Rebuilds keep it current.");
            CommandRow(stack, "FTFEASEMENTCOMPONENT",
                "Adds a further area to the same easement (TOGETHER WITH / AND / ALSO): a portion, a " +
                "clicked area or a closed boundary, each with its own source and area.");
            CommandRow(stack, "FTFEASEMENTGROUP",
                "Puts easements in one exhibit group -- a permanent easement and its temporary one -- " +
                "and marks portions as permanent or temporary for the profile's styling.");
            CommandRow(stack, "FTFEASEMENTINSPECT   (FTFEXHIBITINSPECT)",
                "Audits one easement or exhibit: every item with where it came from, closure of the CAD " +
                "geometry and of the stated courses, stale status and warnings. Double-click to zoom to a source.");
            CommandRow(stack, "FTFEXHIBIT",
                "Builds an editable exhibit layout from selected easements: viewport at a profile scale, " +
                "north arrow, scale bar, legend, area and line tables, labels, POC/POB, notes, and a review list.");
            CommandRow(stack, "FTFEXHIBITREBUILD",
                "Updates an exhibit after its easements change, keeping moved tables, title, notes, legend " +
                "and north arrow, and flagging any hand-edited text instead of overwriting it.");
            CommandRow(stack, "FTFEASEMENTLEGAL",
                "Writes a DRAFT legal description for an easement and its temporary " +
                "construction easement in the office Exhibit A pattern: name the parcel, " +
                "corners and lines, and it is saved beside the drawing for surveyor review.");
            CommandRow(stack, "FTFEASEMENTEXPORT",
                "Writes each easement's ordered geometry beside the drawing -- raw " +
                "material for a legal description, marked as not one.");
            CommandRow(stack, "FTFPROFILE",
                "Chooses the drafting profile (project or client standards) this " +
                "drawing uses, or saves the current settings as a new one.");

            CommandGroup(stack, "Read-only checks (change nothing, safe anytime)");
            CommandRow(stack, "FTFLINES",
                "Linework inventory: every line, how it was identified, its label " +
                "layer. Summary on screen, full CSV beside the drawing.");
            CommandRow(stack, "FTFLINEMETA",
                "Metadata probe: what survived the TBC export on each line, to a " +
                "text report beside the drawing.");
            CommandRow(stack, "FTFBLOCKS",
                "Lists the block definitions in the drawing.");
            CommandRow(stack, "FTFWHERE",
                "Shows where FTF found its rules and settings files and which one " +
                "wins. Run this first when anything complains about configuration.");

            CommandGroup(stack, "Settings");
            CommandRow(stack, "FTFSETUP",
                "Opens Settings in this window. (Also FTFSETTINGS, FTFCONFIG and " +
                "FTFOPTIONS.)");
        }

        private static void CommandGroup(FlowLayoutPanel stack, string text)
        {
            stack.Controls.Add(new Label
            {
                Text = text,
                Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
                AutoSize = true,
                Margin = new Padding(0, 14, 0, 2)
            });
        }

        private static void CommandRow(FlowLayoutPanel stack, string name, string what)
        {
            stack.Controls.Add(new Label
            {
                Text = name,
                Font = new Font("Consolas", 9f, FontStyle.Bold),
                AutoSize = true,
                Margin = new Padding(0, 6, 0, 0)
            });
            Caption(stack, what);
        }

        private void BuildAboutPage()
        {
            var page = NewPage("About");
            var stack = PageStack(page);

            PageHeader(stack, "About");

            Caption(stack, FtfBuildInfo.OneLine());
            foreach (var verdict in FieldCodes.Deploy.VersionReport.Verdicts(
                         FtfBuildInfo.Running, FtfBuildInfo.RunningFromInstalledBundle,
                         FtfBuildInfo.Installed, FtfBuildInfo.LatestApproved))
                Caption(stack, verdict);
            SectionGap(stack);
            Caption(stack, "FTF is not a replacement for Civil 3D Field to Finish. Civil 3D " +
                           "creates the survey geometry -- points, description-key symbols " +
                           "and coded linework. FTF is the post-processing and presentation " +
                           "layer that polishes that geometry into the office standard: " +
                           "labels and conflict avoidance, leaders, tags, tree driplines, " +
                           "schedules, modifier treatment, draw order, and cleanup of " +
                           "everything it created.");
        }

        // ========================================================== page plumbing

        private static FlowLayoutPanel PageStack(Panel page)
        {
            var stack = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true
            };
            page.Controls.Add(stack);
            return stack;
        }

        private static void PageHeader(FlowLayoutPanel stack, string text)
        {
            stack.Controls.Add(new Label
            {
                Text = text,
                Font = new Font(SystemFonts.DefaultFont.FontFamily, 14f, FontStyle.Bold),
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 4)
            });
        }

        private static void SectionHeader(FlowLayoutPanel stack, string text)
        {
            stack.Controls.Add(new Label
            {
                Text = text,
                Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
                AutoSize = true,
                Margin = new Padding(0, 2, 0, 2)
            });
        }

        private static Label Caption(FlowLayoutPanel stack, string text)
        {
            var label = new Label
            {
                Text = text,
                ForeColor = CaptionGray,
                AutoSize = true,
                MaximumSize = new Size(560, 0),
                Margin = new Padding(0, 0, 0, 8)
            };
            stack.Controls.Add(label);
            return label;
        }

        private static void SectionGap(FlowLayoutPanel stack)
        {
            stack.Controls.Add(new Label { Text = " ", AutoSize = true, Margin = new Padding(0, 4, 0, 4) });
        }

        private Button PrimaryButton(string text, string hint)
        {
            var button = new Button
            {
                Text = text,
                AutoSize = true,
                MinimumSize = new Size(180, 36),
                FlatStyle = FlatStyle.Flat,
                BackColor = Accent,
                ForeColor = Color.White,
                Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
                Margin = new Padding(0)
            };
            button.FlatAppearance.BorderSize = 0;
            if (!string.IsNullOrEmpty(hint)) new ToolTip().SetToolTip(button, hint);
            _actionButtons.Add(button);
            return button;
        }

        private Button SecondaryButton(string text, string hint)
        {
            var button = new Button
            {
                Text = text,
                AutoSize = true,
                MinimumSize = new Size(150, 30),
                Margin = new Padding(0)
            };
            if (!string.IsNullOrEmpty(hint)) new ToolTip().SetToolTip(button, hint);
            _actionButtons.Add(button);
            return button;
        }

        private static Button LinkButton(string text)
        {
            var button = new Button
            {
                Text = text,
                AutoSize = true,
                FlatStyle = FlatStyle.Flat,
                ForeColor = Accent,
                TabStop = false,
                Margin = new Padding(0, 0, 8, 0)
            };
            button.FlatAppearance.BorderSize = 0;
            return button;
        }

        // ==================================================================== log

        private Control BuildLogPanel()
        {
            _logPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Visible = false,                 // collapsed until processing begins
                Height = 148,
                Padding = new Padding(10, 4, 10, 8),
                BackColor = Color.White
            };

            var header = new Label
            {
                Text = "PROCESSING LOG",
                Font = new Font(SystemFonts.DefaultFont.FontFamily, 8f, FontStyle.Bold),
                ForeColor = CaptionGray,
                Dock = DockStyle.Top,
                Height = 18
            };

            _log = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.FromArgb(250, 250, 252)
            };

            _logPanel.Controls.Add(_log);
            _logPanel.Controls.Add(header);
            return _logPanel;
        }

        private void Log(string message)
        {
            if (_log == null) return;
            if (!_logPanel.Visible) _logPanel.Visible = true;
            _log.AppendText(message + Environment.NewLine);
        }

        // ================================================================== status

        public void RefreshStatus()
        {
            InAutoCad(null, () => { _status = FtfStatusService.Gather(); ShowStatus(); });
        }

        /// <summary>Navigate from outside -- FTFSETUP lands on Settings this way.</summary>
        public void ShowPageByName(string name)
        {
            ShowPage(name);
        }

        private void ShowStatus()
        {
            if (_status == null)
            {
                _overviewDrawing.Text = "(no drawing open)";
                _overviewSummary.Text = "Open a drawing to see where it stands.";
                _overviewAttention.Visible = false;
                _unknownSummary.Text = "No drawing open.";
                foreach (var label in _drawingStats.Values) { label.Text = "-"; label.ForeColor = SystemColors.ControlText; }
                if (_reportCounts != null) _reportCounts.Text = "No drawing open.";
                ShowSession();
                ShowBuild();
                return;
            }

            var t = _status.Tally;
            _overviewDrawing.Text = _status.DrawingName;

            _overviewSummary.Text = string.Format(
                "{0} survey point(s); {1} eligible for FTF finishing. FTF has {2} object(s) " +
                "in this drawing.",
                N(t.Total), N(t.Eligible), N(_status.OwnedEntities));

            // Amber for configuration gaps, red for points that could not be finished --
            // and only shown at all when there is something to decide.
            var attention = t.NeedsConfiguration + t.Errors;
            _overviewAttention.Visible = attention > 0;
            if (attention > 0)
            {
                _overviewAttention.LinkColor = t.Errors > 0 ? ErrorRed : Amber;
                _overviewAttention.Text = t.Errors > 0
                    ? string.Format("{0} point(s) could not be finished and {1} carry data " +
                                    "no rule handles -- see Unknown Codes.",
                                    N(t.Errors), N(t.NeedsConfiguration))
                    : string.Format("{0} point(s) in {1} code(s) carry data no rule handles " +
                                    "yet -- see Unknown Codes.", N(t.NeedsConfiguration), t.UnknownCodes);
            }

            _rulesProblem.Text = _status.RulesError ?? string.Empty;

            // ---- in this drawing
            var normal = SystemColors.ControlText;
            SetStat(_drawingStats, "COGO points", N(t.Total), normal);
            SetStat(_drawingStats, "Eligible for FTF", N(t.Eligible), normal);
            SetStat(_drawingStats, "processed",
                N(t.Processed) + (t.ProcessedWithWarnings > 0
                    ? " (" + N(t.ProcessedWithWarnings) + " with warnings)" : string.Empty), normal);
            SetStat(_drawingStats, "errors",
                t.Errors == 0
                    ? "0"
                    : N(t.Errors) + " (" + Parts(
                          Part(t.BlankDescriptions, "blank"), Part(t.ParseFailures, "unreadable"),
                          Part(t.InvalidData, "invalid value")) + ")",
                t.Errors > 0 ? ErrorRed : normal);
            SetStat(_drawingStats, "Need configuration",
                t.NeedsConfiguration == 0
                    ? "0"
                    : N(t.NeedsConfiguration) + " point(s), " + t.UnknownCodes + " unknown code(s)",
                t.NeedsConfiguration > 0 ? Amber : normal);
            SetStat(_drawingStats, "Skipped on purpose",
                t.SkippedIntentionally == 0
                    ? "0"
                    : N(t.SkippedIntentionally) + " (" + Parts(
                          Part(t.Linework, "linework"), Part(t.IgnoredByRules, "never-draw"),
                          Part(t.IgnoredByUser, "ignored by you"), Part(t.BareCodes, "bare codes")) + ")",
                normal);
            SetStat(_drawingStats, "FTF objects",
                _status.OwnedEntities == 0
                    ? "None"
                    : N(_status.OwnedEntities) + ": " +
                      FieldCodes.Reporting.OwnedObjects.Describe(_status.OwnedByKind, 3),
                normal);

            var record = _status.LastRun;
            SetStat(_drawingStats, "Last processed",
                record == null
                    ? "Not recorded"
                    : record.FinishedLocalText() + (string.IsNullOrEmpty(record.User) ? string.Empty : " by " + record.User),
                record == null ? CaptionGray : normal);
            SetStat(_drawingStats, "Last outcome",
                record == null ? "-" : record.OutcomeText,
                record == null ? normal
                    : record.Outcome == FieldCodes.Reporting.ProcessingOutcome.Succeeded ? normal
                    : ErrorRed);

            _overviewConfig.Text = "Settings: " + (_status.SettingsText ?? "-") + Environment.NewLine +
                                   "Rules: " + (_status.RulesText ?? "-");

            ShowSession();
            ShowBuild();

            // Before a scan, Unknown Codes and Reports summarise from the status.
            if (_review == null)
                _unknownSummary.Text = string.Format(
                    "{0} point(s) could not be finished; {1} carry data no rule handles; {2} " +
                    "are skipped on purpose. Scan to list them by code.",
                    N(t.Errors), N(t.NeedsConfiguration), N(t.SkippedIntentionally));

            if (_reportCounts != null)
            {
                _reportCounts.Text = string.Format(
                    "In this drawing: {0} point(s) - {1} processed{2}, {3} error(s), {4} needing " +
                    "configuration, {5} skipped on purpose. Last processed: {6}.",
                    N(t.Total), N(t.Processed),
                    t.ProcessedWithWarnings > 0 ? " (" + N(t.ProcessedWithWarnings) + " with warnings)" : string.Empty,
                    N(t.Errors), N(t.NeedsConfiguration), N(t.SkippedIntentionally),
                    record == null ? "not recorded" : record.FinishedLocalText() + ", " + record.OutcomeText);
                _reportConfig.Text = "Settings: " + (_status.SettingsText ?? "-");
            }
            ShowReportSummary();
        }

        /// <summary>The This Session column: only what this window did.</summary>
        private void ShowSession()
        {
            if (_sessionStats.Count == 0) return;
            var normal = SystemColors.ControlText;

            SetStat(_sessionStats, "Window opened", FtfStatusService.When(_sessionOpened), normal);
            SetStat(_sessionStats, "Processing runs", _sessionRuns == 0 ? "None yet" : N(_sessionRuns), normal);
            SetStat(_sessionStats, "Last run",
                _sessionLastRun == null
                    ? "-"
                    : FtfStatusService.When(_sessionLastRun.FinishedUtc.ToLocalTime()) + " - " + _sessionLastRun.OutcomeText,
                _sessionLastRun == null ||
                _sessionLastRun.Outcome == FieldCodes.Reporting.ProcessingOutcome.Succeeded ? normal : ErrorRed);
            SetStat(_sessionStats, "FTF objects", SessionObjectsText(), normal);
            SetStat(_sessionStats, "Review scan",
                _review == null
                    ? "Not scanned"
                    : FtfStatusService.When(_review.ScannedLocal) + " - " + N(_review.Rows.Count) + " point(s)",
                normal);
        }

        private string SessionObjectsText()
        {
            if (_sessionRuns == 0) return "-";
            if (!_sessionOwnedMeasured) return "Not measured";

            var net = _sessionOwnedChange.Values.Sum();
            if (_sessionOwnedChange.Count == 0) return "No change";

            var parts = _sessionOwnedChange
                .Where(kv => kv.Value != 0)
                .OrderByDescending(kv => Math.Abs(kv.Value))
                .Take(3)
                .Select(kv => Signed(kv.Value) + " " + FieldCodes.Reporting.OwnedObjects.NameOf(kv.Key))
                .ToArray();
            return Signed(net) + " net (" + string.Join(", ", parts) + ")";
        }

        private static string Signed(int value)
        {
            return (value > 0 ? "+" : string.Empty) + N(value);
        }

        private static string Part(int count, string what)
        {
            return count > 0 ? N(count) + " " + what : null;
        }

        private static string Parts(params string[] parts)
        {
            return string.Join(", ", parts.Where(p => p != null).ToArray());
        }

        /// <summary>
        /// Which FTF this is. An update waiting on the office copy is worth saying in the amber
        /// used for "there is something to know", but it still needs nothing from the drafter.
        /// </summary>
        private void ShowBuild()
        {
            if (_buildNote == null) return;
            _buildNote.Text = FtfBuildInfo.OneLine() + "\n" + OfficeUpdate.Sentence;
            _buildNote.ForeColor = OfficeUpdate.State == FieldCodes.Deploy.UpdateState.UpdateWaiting
                ? Color.FromArgb(178, 108, 0)
                : CaptionGray;
        }

        // ============================================================== processing

        private IList<string> SelectedStageNames()
        {
            var names = new List<string>();
            for (var i = 0; i < _stageChecks.Count; i++)
                if (_stageChecks[i].Checked) names.Add(_pipeline.Steps[i].Name);
            return names;
        }

        private void RunStages(IList<string> selected)
        {
            if (selected != null && selected.Count == 0)
            {
                Log("Nothing selected.");
                return;
            }

            InAutoCad("Processing", () =>
            {
                var result = _pipeline.Run(
                    selected,
                    step => Log(step.Title + "..."),
                    (step, ex) => { Log(step.Title + " FAILED: " + ex.Message); return false; });

                Log(string.Format("Done: {0} stage(s) completed{1}.",
                    result.Completed,
                    result.Failed > 0 ? ", " + result.Failed + " failed" : string.Empty));

                if (result.Failed == 0)
                    Log("Safe to run again at any time; every stage replaces its own output.");

                // The drawing changed, so the dry-run preview is stale until rescanned.
                _reviewScanned = false;

                UpdateRunSummary(result);
                _status = FtfStatusService.Gather();
                ShowStatus();
                RecordSessionRun(result);
            });
        }

        private void UpdateRunSummary(FtfRunResult result)
        {
            // Called straight after a run, before the status is gathered again: what FTF
            // owned beforehand is what this run's additions are measured against. The
            // Reports detail is rebuilt from the stored record in RecordSessionRun.
            _ownedBeforeRun = _status != null
                ? new Dictionary<string, int>(_status.OwnedByKind, StringComparer.OrdinalIgnoreCase)
                : null;

            if (_runSummary != null && result != null)
                _runSummary.Text = string.Format("Run finished: {0} completed, {1} failed{2}.",
                    result.Completed, result.Failed,
                    result.StoppedEarly ? " (stopped early)" : string.Empty);
        }

        /// <summary>
        /// After a run, with the fresh status: stores the run in the drawing (when, what
        /// ran, how it ended), adds it to this session's tally, and rescans the review
        /// if a review page is open so it never shows the pre-run drawing.
        /// </summary>
        private void RecordSessionRun(FtfRunResult result)
        {
            var record = FtfStatusService.RecordRun(result, _status, "FTF window", Log);
            if (_status != null) _status.LastRun = record;

            _sessionRuns++;
            _sessionLastRun = record;

            if (_ownedBeforeRun != null && _status != null)
            {
                foreach (var change in FieldCodes.Reporting.OwnedObjects.Difference(_ownedBeforeRun, _status.OwnedByKind))
                {
                    int n;
                    _sessionOwnedChange.TryGetValue(change.Key, out n);
                    _sessionOwnedChange[change.Key] = n + change.Value;
                }
                _sessionOwnedMeasured = true;
            }
            _ownedBeforeRun = null;

            if (_currentPage == "Feature Review" || _currentPage == "Unknown Codes" ||
                _currentPage == "Reports")
                ScanReviewNow();

            ShowStatus();
        }

        // =================================================================== tools

        private void OpenReport(string path, string missingMessage)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                Log(missingMessage);
                return;
            }

            try
            {
                Process.Start(path);
            }
            catch (System.Exception ex)
            {
                Log("Could not open " + path + ": " + ex.Message);
            }
        }

        // ================================================================ plumbing

        /// <summary>
        /// Runs work in AutoCAD's application context. A modeless window lives outside
        /// the document context, so anything touching the drawing has to be marshalled
        /// across or document locks and editor prompts misbehave.
        /// </summary>
        private void InAutoCad(string what, Action work)
        {
            if (_busy) { Log("Still working on the previous action."); return; }

            _busy = true;
            SetButtonsEnabled(false);
            if (what != null) Log("=== " + what + " ===");

            try
            {
                AcadApp.DocumentManager.ExecuteInApplicationContext(unused =>
                {
                    try
                    {
                        work();
                    }
                    catch (System.Exception ex)
                    {
                        Log("Failed: " + ex.Message);
                    }
                    finally
                    {
                        _busy = false;
                        SetButtonsEnabled(true);
                    }
                }, null);
            }
            catch (System.Exception ex)
            {
                _busy = false;
                SetButtonsEnabled(true);
                Log("Could not reach AutoCAD: " + ex.Message);
            }
        }

        private void SetButtonsEnabled(bool enabled)
        {
            foreach (var button in _actionButtons)
                if (button != null && !button.IsDisposed) button.Enabled = enabled;
        }
    }
}
