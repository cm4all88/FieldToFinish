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

        private void BuildLineFeaturesPage()
        {
            var page = NewPage("Line Features");

            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1 };
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            var top = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                AutoSize = true,
                WrapContents = false
            };
            PageHeader(top, "Lines");
            Caption(top, "Civil 3D creates the survey figures and linework from the field " +
                         "codes; FTF labels the existing lines. To place a label, run " +
                         "FTFLABELLINE: click the line, slide the preview to where the " +
                         "label belongs, click to place. This inventory shows what each " +
                         "line was identified as and what its label would say. Read-only: " +
                         "scanning changes nothing.");

            _lineSummary = Caption(top, "...");

            var toolbar = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            var rescan = SecondaryButton("Scan Drawing (read-only)", null);
            rescan.Click += (s, e) => { _lineworkScanned = false; EnsureLineworkScanned(); };
            toolbar.Controls.Add(rescan);
            top.Controls.Add(toolbar);

            grid.Controls.Add(top, 0, 0);

            _lineGrid = new ListView
            {
                View = View.Details,
                FullRowSelect = true,
                HideSelection = false,
                MultiSelect = false,
                Dock = DockStyle.Fill
            };
            _lineGrid.Columns.Add("Civil 3D object", 100);
            _lineGrid.Columns.Add("Figure", 70);
            _lineGrid.Columns.Add("Layer", 150);
            _lineGrid.Columns.Add("Feature", 140);
            _lineGrid.Columns.Add("Code", 70);
            _lineGrid.Columns.Add("Length", 65);
            _lineGrid.Columns.Add("Label layer", 200);
            _lineGrid.Columns.Add("Proposed FTF action", 260);
            grid.Controls.Add(_lineGrid, 0, 1);

            page.Controls.Add(grid);
        }

        /// <summary>
        /// Fills the Line Features inventory from one read-only scan. Nothing in this
        /// path creates, modifies or stamps anything -- Civil 3D owns the linework.
        /// </summary>
        private void EnsureLineworkScanned()
        {
            if (_lineworkScanned) return;
            _lineworkScanned = true;

            InAutoCad(null, () =>
            {
                var result = FtfLineworkService.Scan();

                if (result.RulesError != null)
                    Log("Rules problem: " + result.RulesError);

                _lineGrid.BeginUpdate();
                _lineGrid.Items.Clear();

                foreach (var row in result.Rows)
                {
                    var item = new ListViewItem(row.EntityType);
                    item.SubItems.Add(string.IsNullOrEmpty(row.FigureName) ? "-" : row.FigureName);
                    item.SubItems.Add(row.Layer);
                    item.SubItems.Add(row.FeatureName);
                    item.SubItems.Add(row.Codes);
                    item.SubItems.Add(row.LengthText);
                    item.SubItems.Add(string.IsNullOrEmpty(row.LabelLayerText)
                        ? "-" : row.LabelLayerText);
                    item.SubItems.Add(row.ProposedAction);

                    if (row.Source == FieldCodes.Linework.LineIdentitySource.None)
                        item.ForeColor = CaptionGray;
                    else if (row.Codes != null && row.Codes.IndexOf('/') >= 0)
                        item.ForeColor = Color.FromArgb(178, 108, 0);

                    _lineGrid.Items.Add(item);
                }

                _lineGrid.EndUpdate();

                _lineSummary.Text = string.Format(
                    "{0} line object(s): {1} identified, {2} ambiguous (shared layer), " +
                    "{3} not configured as line features.",
                    result.Rows.Count, result.Identified, result.Ambiguous, result.Unidentified);
            });
        }

        // ================================================================ overview

        private void BuildOverviewPage()
        {
            var page = NewPage("Overview");
            var stack = PageStack(page);

            PageHeader(stack, "Overview");

            stack.Controls.Add(new Label
            {
                Text = "CURRENT DRAWING",
                Font = new Font(SystemFonts.DefaultFont.FontFamily, 7.5f, FontStyle.Bold),
                ForeColor = CaptionGray,
                AutoSize = true,
                Margin = new Padding(1, 2, 0, 0)
            });

            _overviewDrawing = new Label
            {
                Text = "...",
                Font = new Font(SystemFonts.DefaultFont.FontFamily, 11f, FontStyle.Bold),
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 14)
            };
            stack.Controls.Add(_overviewDrawing);

            // One plain sentence instead of a dashboard: what is in the drawing and
            // whether anything needs a decision. The numbers live on the Review
            // pages for whoever wants them.
            _overviewSummary = new Label
            {
                Text = "...",
                AutoSize = true,
                MaximumSize = new Size(560, 0),
                Margin = new Padding(0, 0, 0, 2)
            };
            stack.Controls.Add(_overviewSummary);

            _overviewAttention = new LinkLabel
            {
                Text = string.Empty,
                AutoSize = true,
                MaximumSize = new Size(560, 0),
                LinkColor = Color.FromArgb(178, 108, 0),
                Margin = new Padding(0, 2, 0, 0),
                Visible = false
            };
            _overviewAttention.LinkClicked += (s, e) => ShowPage("Unknown Codes");
            stack.Controls.Add(_overviewAttention);

            _rulesProblem = new Label
            {
                Text = string.Empty,
                AutoSize = true,
                MaximumSize = new Size(560, 0),
                ForeColor = Color.FromArgb(170, 0, 0),
                Margin = new Padding(0, 2, 0, 8)
            };
            stack.Controls.Add(_rulesProblem);

            SectionGap(stack);

            // The three things a surveyor actually opens this window to do.
            var run = PrimaryButton("Process Drawing",
                "Run every finishing stage, in order.");
            run.Click += (s, e) => RunStages(null);
            stack.Controls.Add(run);
            Caption(stack, "Runs all the automatic finishing: point handling, " +
                           "driplines, labels, tags, the schedule and draw order.");

            var labelLine = SecondaryButton("Label a Line", null);
            labelLine.Margin = new Padding(0, 10, 0, 0);
            labelLine.Click += (s, e) => StartLabelLine();
            stack.Controls.Add(labelLine);
            Caption(stack, "Starts FTFLABELLINE in the drawing: click a line, slide " +
                           "the label where it belongs, click to place.");

            var review = SecondaryButton("Review What FTF Will Do", null);
            review.Margin = new Padding(0, 10, 0, 0);
            review.Click += (s, e) => ShowPage("Feature Review");
            stack.Controls.Add(review);
            Caption(stack, "A read-only preview of every point and line -- nothing " +
                           "in the drawing changes.");

            SectionGap(stack);
            var refresh = SecondaryButton("Refresh Status", null);
            refresh.Click += (s, e) => RefreshStatus();
            stack.Controls.Add(refresh);
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

        private void BuildStandardsPage()
        {
            var page = NewPage("Standards");

            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1 };
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            var top = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                AutoSize = true,
                WrapContents = false
            };
            PageHeader(top, "Standards");
            Caption(top, "Civil 3D creates the survey points, symbols and linework. FTF " +
                         "applies finishing standards to the Civil 3D result. This page " +
                         "shows those standards and where each one is defined; anything " +
                         "not yet governed by a setting is marked Planned rather than " +
                         "invented. Editing happens in Settings.");

            var refresh = SecondaryButton("Reload Standards", null);
            refresh.Click += (s, e) => { _standardsBuilt = false; EnsureStandardsBuilt(); };
            top.Controls.Add(refresh);

            grid.Controls.Add(top, 0, 0);

            _standardsGrid = new ListView
            {
                View = View.Details,
                FullRowSelect = true,
                HideSelection = false,
                MultiSelect = false,
                Dock = DockStyle.Fill,
                ShowGroups = true
            };
            _standardsGrid.Columns.Add("Standard", 210);
            _standardsGrid.Columns.Add("Value", 330);
            _standardsGrid.Columns.Add("Defined by", 110);
            grid.Controls.Add(_standardsGrid, 0, 1);

            page.Controls.Add(grid);
        }

        /// <summary>
        /// Read-only: assembles the view from the loaded rules and resolved settings.
        /// Nothing is written and no drawing entity is touched.
        /// </summary>
        private void EnsureStandardsBuilt()
        {
            if (_standardsBuilt) return;
            _standardsBuilt = true;

            InAutoCad(null, () =>
            {
                var doc = AcadApp.DocumentManager.MdiActiveDocument;
                if (doc == null) { _standardsBuilt = false; return; }

                FieldCodes.RulesConfig rules;
                FieldCodes.Settings.FtfSettings settings;
                try
                {
                    rules = FtfSession.Rules(doc.Database);
                    settings = FtfSession.SettingsFor(doc.Database, rules);
                }
                catch (FieldCodes.ConfigException ex)
                {
                    Log("Rules problem: " + ex.Message);
                    _standardsBuilt = false;
                    return;
                }

                var sections = new FieldCodes.Standards.StandardsViewBuilder(rules, settings).Build();

                _standardsGrid.BeginUpdate();
                _standardsGrid.Groups.Clear();
                _standardsGrid.Items.Clear();

                foreach (var section in sections)
                {
                    var group = new ListViewGroup(section.Title.Replace("&", "&&"));
                    _standardsGrid.Groups.Add(group);

                    foreach (var entry in section.Items)
                    {
                        var item = new ListViewItem(entry.Name) { Group = group };
                        item.SubItems.Add(entry.Value);
                        item.SubItems.Add(entry.Source);

                        if (entry.Source == FieldCodes.Standards.StandardsViewBuilder.Planned)
                            item.ForeColor = CaptionGray;

                        _standardsGrid.Items.Add(item);
                    }
                }

                _standardsGrid.EndUpdate();
            });
        }

        private static readonly string[] ReviewFilters =
        {
            "All recognized", "Trees", "Power / Utility", "Signs", "Control",
            "Other point features", "Unknown / not configured", "Warnings & errors",
            "Everything"
        };

        private void BuildFeatureReviewPage()
        {
            var page = NewPage("Feature Review");

            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1 };
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // header + toolbar
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 62f));  // list
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 38f));  // detail

            var top = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                AutoSize = true,
                WrapContents = false
            };
            PageHeader(top, "Points");
            Caption(top, "A read-only preview of the finishing FTF plans for this drawing. " +
                         "Scanning changes nothing: Civil 3D's points, symbols and linework " +
                         "are only read.");

            var toolbar = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            _reviewFilter = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 200
            };
            foreach (var f in ReviewFilters) _reviewFilter.Items.Add(f);
            _reviewFilter.SelectedIndex = 0;
            _reviewFilter.SelectedIndexChanged += (s, e) => ApplyReviewFilter();
            toolbar.Controls.Add(_reviewFilter);

            var rescan = SecondaryButton("Scan Drawing (read-only)", null);
            rescan.Margin = new Padding(8, 0, 0, 0);
            rescan.Click += (s, e) => { _reviewScanned = false; EnsureReviewScanned(); };
            toolbar.Controls.Add(rescan);
            top.Controls.Add(toolbar);

            grid.Controls.Add(top, 0, 0);

            _reviewGrid = new ListView
            {
                View = View.Details,
                FullRowSelect = true,
                HideSelection = false,
                MultiSelect = false,
                Dock = DockStyle.Fill
            };
            _reviewGrid.Columns.Add("Point", 60);
            _reviewGrid.Columns.Add("Raw Description", 170);
            _reviewGrid.Columns.Add("Feature", 100);
            _reviewGrid.Columns.Add("Rule", 80);
            _reviewGrid.Columns.Add("Planned Actions", 260);
            _reviewGrid.Columns.Add("Status", 90);
            _reviewGrid.SelectedIndexChanged += (s, e) =>
            {
                _reviewDetail.Text = _reviewGrid.SelectedItems.Count == 0
                    ? string.Empty
                    : FeatureReviewBuilder.DetailText(
                        _reviewGrid.SelectedItems[0].Tag as ReviewRow);
            };
            grid.Controls.Add(_reviewGrid, 0, 1);

            _reviewDetail = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(250, 250, 252),
                Margin = new Padding(0, 6, 0, 0)
            };
            grid.Controls.Add(_reviewDetail, 0, 2);

            page.Controls.Add(grid);
        }

        private void BuildUnknownCodesPage()
        {
            var page = NewPage("Unknown Codes");

            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1 };
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var top = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                AutoSize = true,
                WrapContents = false
            };
            PageHeader(top, "Unknown Codes");
            _unknownSummary = Caption(top, "...");
            grid.Controls.Add(top, 0, 0);

            _unknownGrid = new ListView
            {
                View = View.Details,
                FullRowSelect = true,
                HideSelection = false,
                MultiSelect = false,
                Dock = DockStyle.Fill
            };
            _unknownGrid.Columns.Add("Code / Description", 170);
            _unknownGrid.Columns.Add("Count", 55);
            _unknownGrid.Columns.Add("Example Point(s)", 110);
            _unknownGrid.Columns.Add("Reason", 330);
            grid.Controls.Add(_unknownGrid, 0, 1);

            var buttons = new FlowLayoutPanel
            {
                AutoSize = true,
                WrapContents = false,
                Margin = new Padding(0, 6, 0, 0)
            };

            var rescan = SecondaryButton("Scan Drawing (read-only)", null);
            rescan.Click += (s, e) => { _reviewScanned = false; EnsureReviewScanned(); };
            buttons.Controls.Add(rescan);

            var configure = SecondaryButton("Configure Rules...",
                "Opens Settings at Point Features, where the configured rules live.");
            configure.Margin = new Padding(8, 0, 0, 0);
            configure.Click += (s, e) => OpenSettingsSection("Point Features");
            buttons.Controls.Add(configure);

            var csv = SecondaryButton("Open CSV Report", null);
            csv.Margin = new Padding(8, 0, 0, 0);
            csv.Click += (s, e) => OpenReport(
                _status != null ? _status.UnhandledReportPath : null,
                "No unknown-code report yet. Run Point Finishing first.");
            buttons.Controls.Add(csv);

            grid.Controls.Add(buttons, 0, 2);
            page.Controls.Add(grid);
        }

        private void BuildReportsPage()
        {
            var page = NewPage("Reports");
            var stack = PageStack(page);

            PageHeader(stack, "Reports");
            Caption(stack, "The CSV reports are written next to the drawing on every Point " +
                           "Finishing run; the summary reflects processing started from " +
                           "this window.");

            SectionHeader(stack, "Exception report");
            Caption(stack, "Points that matched a rule and failed inside it -- the ones " +
                           "worth fixing in the field data.");
            var exceptions = SecondaryButton("Open Exception Report", null);
            exceptions.Click += (s, e) => OpenReport(
                _status != null ? _status.ExceptionReportPath : null,
                "No exception report yet. Run Point Finishing first.");
            stack.Controls.Add(exceptions);

            SectionGap(stack);
            SectionHeader(stack, "Unknown codes report");
            Caption(stack, "The same information as the Unknown Codes page, as a CSV with " +
                           "sample descriptions per code.");
            var unknown = SecondaryButton("Open Unknown Codes Report", null);
            unknown.Click += (s, e) => OpenReport(
                _status != null ? _status.UnhandledReportPath : null,
                "No unknown-code report yet. Run Point Finishing first.");
            stack.Controls.Add(unknown);

            SectionGap(stack);
            SectionHeader(stack, "Processing summary");
            _runSummary = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Width = 560,
                Height = 150,
                BackColor = Color.FromArgb(250, 250, 252),
                Text = "No processing has been run from this window yet."
            };
            stack.Controls.Add(_runSummary);
        }

        // ========================================================== review scanning

        /// <summary>
        /// Fills Feature Review and Unknown Codes from one read-only scan. Nothing in
        /// this path creates, erases, rotates, labels or tags anything.
        /// </summary>
        private void EnsureReviewScanned()
        {
            if (_reviewScanned) return;
            _reviewScanned = true;

            InAutoCad(null, () =>
            {
                var result = FtfReviewService.Scan();
                _reviewRows = result.Rows;

                if (result.RulesError != null)
                    Log("Rules problem: " + result.RulesError);

                ApplyReviewFilter();
                PopulateUnknownGrid();
            });
        }

        private void ApplyReviewFilter()
        {
            if (_reviewGrid == null || _reviewRows == null) return;

            var filter = _reviewFilter.SelectedIndex < 0
                ? ReviewFilters[0]
                : (string)_reviewFilter.SelectedItem;

            _reviewGrid.BeginUpdate();
            _reviewGrid.Items.Clear();

            foreach (var row in _reviewRows)
            {
                if (!MatchesFilter(row, filter)) continue;

                var item = new ListViewItem(row.PointNumber);
                item.SubItems.Add(row.RawDescription);
                item.SubItems.Add(CategoryName(row.Category));
                item.SubItems.Add(row.RuleId ?? "-");
                item.SubItems.Add(row.ActionsSummary);
                item.SubItems.Add(row.StatusText);
                item.Tag = row;

                if (row.Status == ReviewStatus.Error)
                    item.ForeColor = Color.FromArgb(170, 0, 0);
                else if (row.Status == ReviewStatus.Warning)
                    item.ForeColor = Color.FromArgb(178, 108, 0);

                _reviewGrid.Items.Add(item);
            }

            _reviewGrid.EndUpdate();
            _reviewDetail.Text = string.Format("{0} point(s) shown - {1}",
                _reviewGrid.Items.Count, filter);
        }

        private static bool MatchesFilter(ReviewRow row, string filter)
        {
            switch (filter)
            {
                case "Trees": return row.Category == ReviewCategory.Tree;
                case "Power / Utility": return row.Category == ReviewCategory.Utility;
                case "Signs": return row.Category == ReviewCategory.Sign;
                case "Control": return row.Category == ReviewCategory.Control;
                case "Other point features": return row.Category == ReviewCategory.OtherFeature;
                case "Unknown / not configured": return row.Status == ReviewStatus.NotConfigured;
                case "Warnings & errors":
                    return row.Status == ReviewStatus.Warning || row.Status == ReviewStatus.Error;
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

        private void PopulateUnknownGrid()
        {
            if (_unknownGrid == null || _reviewRows == null) return;

            var groups = CodeGrouping.BuildUnknownView(_reviewRows);

            _unknownGrid.BeginUpdate();
            _unknownGrid.Items.Clear();

            foreach (var group in groups)
            {
                var item = new ListViewItem(group.Example);
                item.SubItems.Add(group.Count.ToString());
                item.SubItems.Add(string.Join(", ", group.ExamplePoints.ToArray()));
                item.SubItems.Add(group.Reason);

                if (group.Status == ReviewStatus.Error)
                    item.ForeColor = Color.FromArgb(170, 0, 0);
                else if (group.Status == ReviewStatus.NotConfigured)
                    item.ForeColor = Color.FromArgb(178, 108, 0);

                _unknownGrid.Items.Add(item);
            }

            _unknownGrid.EndUpdate();
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
            CommandRow(stack, "FTFLABELLINE   (or FTFL)",
                "Label a line: click it, slide the live preview along it, click to " +
                "place. The cursor picks the spot and the side; text, layer, offset " +
                "and mask come from the standards.");
            CommandRow(stack, "FTFLABELBETWEEN   (or FTFB)",
                "Label the space between two edges -- a driveway between its " +
                "edge-of-asphalt lines. Pick both edges; the label rides the " +
                "midline; click to place.");
            CommandRow(stack, "FTFLABELSTAIRS   (or FTFS)",
                "Label a staircase: select the stair lines (each tread edge is one " +
                "line) and the step-count label places itself centred in them.");
            CommandRow(stack, "FTFSPOT",
                "Spot elevations off the surface: click, click, click. An X at " +
                "each spot, the elevation beside it at the office angle, and " +
                "close-together spots never overlap.");
            CommandRow(stack, "FTFRUN",
                "The whole pipeline in one go -- the same thing as Process Drawing.");
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
                "Draft cadastral lines by bearing and distance, and remove them.");

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

            var version = typeof(FtfMainForm).Assembly.GetName().Version;
            Caption(stack, "Field to Finish " + version);
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
                return;
            }

            _overviewDrawing.Text = _status.DrawingName;

            _overviewSummary.Text = string.Format(
                "{0} survey point(s); {1} get FTF finishing. FTF has {2} entit{3} " +
                "in this drawing.",
                _status.TotalPoints, _status.Recognized,
                _status.OwnedEntities, _status.OwnedEntities == 1 ? "y" : "ies");

            // Amber for configuration gaps, red for actual parse errors -- and only
            // shown at all when there is something to decide.
            var attention = _status.Unconfigured + _status.Errors;
            _overviewAttention.Visible = attention > 0;
            if (attention > 0)
            {
                _overviewAttention.LinkColor = _status.Errors > 0
                    ? Color.FromArgb(170, 0, 0)
                    : Color.FromArgb(178, 108, 0);
                _overviewAttention.Text = _status.Errors > 0
                    ? string.Format("{0} point(s) failed to parse and {1} carry data " +
                                    "no rule handles -- see Unknown Codes.",
                                    _status.Errors, _status.Unconfigured)
                    : string.Format("{0} point(s) carry data no rule handles yet -- " +
                                    "see Unknown Codes.", _status.Unconfigured);
            }

            _rulesProblem.Text = _status.RulesError ?? string.Empty;

            _unknownSummary.Text = string.Format(
                "{0} point(s) carry data no rule handles yet{1}. The report lists each code " +
                "with real sample descriptions.",
                _status.Unconfigured,
                _status.Errors > 0 ? ", and " + _status.Errors + " failed to parse" : string.Empty);
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
            });
        }

        private void UpdateRunSummary(FtfRunResult result)
        {
            if (_runSummary == null) return;

            var lines = new List<string>();
            lines.Add("Last run: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            foreach (var outcome in result.Outcomes)
                lines.Add(string.Format("  {0}  {1}",
                    outcome.Succeeded ? "OK    " : "FAILED",
                    outcome.Name + (outcome.Error != null ? " - " + outcome.Error : string.Empty)));

            lines.Add(string.Format("{0} completed, {1} failed{2}.",
                result.Completed, result.Failed,
                result.StoppedEarly ? " (stopped early)" : string.Empty));

            _runSummary.Text = string.Join(Environment.NewLine, lines.ToArray());
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
