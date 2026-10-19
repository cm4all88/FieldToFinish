using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using FieldCodes.Cad.Compare;
using FieldCodes.Compare;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using FlowDirection = System.Windows.Forms.FlowDirection;

namespace FieldCodes.Cad.Ui
{
    /// <summary>
    /// The FTFCOMPARE review window. Modeless; every drawing change is posted to
    /// FTFCOMPAREACT and runs as a real command. The list is the exception queue:
    /// filters bring the missing, partial, conflicting and uncertain results to the
    /// top; the confident matches are accepted in one click.
    ///
    /// UI only: no comparison logic lives here. UNTESTED in Civil 3D.
    /// </summary>
    internal sealed class CompareReviewForm : Form
    {
        private readonly Label _title;
        private readonly Label _status;
        private readonly ComboBox _profile;
        private readonly ComboBox _source;
        private readonly TextBox _file;
        private readonly Label _stats;
        private readonly ComboBox _filter;
        private readonly TextBox _search;
        private readonly CheckBox _zoom;
        private readonly ListView _list;
        private readonly TextBox _detail;
        private readonly Label _message;
        private readonly List<Button> _buttons = new List<Button>();
        private bool _loading;

        private static readonly string[] Filters = { "Needs attention", "All", "Missing", "Matched", "Partial", "Discrepancies", "Needs Review", "Not Evaluated", "Excluded", "Decided", "Planned changes" };

        public CompareReviewForm()
        {
            DipBuilderForm.UseTheme(ThemePreference.LoadDark());
            Text = "FTF Survey Comparison";
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = false;
            BackColor = DipBuilderForm.Ground;
            ForeColor = DipBuilderForm.Ink;
            Font = DipBuilderForm.F(10f, false);
            var area = Screen.FromPoint(Cursor.Position).WorkingArea;
            ClientSize = new Size(Math.Min(1320, area.Width - 60), Math.Min(860, area.Height - 60));
            MinimumSize = new Size(980, 640);

            // ---- header ----------------------------------------------------------
            var header = new Panel { Dock = DockStyle.Top, BackColor = DipBuilderForm.Surface, Padding = new Padding(16, 10, 16, 8), Height = 112 };
            _title = new Label { AutoSize = true, Text = "Survey comparison", Font = DipBuilderForm.F(14f, true), Location = new Point(16, 8) };
            _status = new Label { AutoSize = true, ForeColor = DipBuilderForm.Muted, Location = new Point(18, 36), Text = "No drawing." };
            header.Controls.Add(_title);
            header.Controls.Add(_status);

            var setup = new FlowLayoutPanel { Location = new Point(14, 62), AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
            setup.Controls.Add(Caption("Profile"));
            _profile = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
            _profile.SelectedIndexChanged += (s, e) => { if (!_loading) OnProfileChosen(); };
            setup.Controls.Add(_profile);
            setup.Controls.Add(Caption("Consultant"));
            _source = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
            _source.Items.AddRange(new object[] { "Xrefs in drawing", "Separate file" });
            _source.SelectedIndexChanged += (s, e) => { if (!_loading) OnSourceChanged(); };
            setup.Controls.Add(_source);
            _file = new TextBox { Width = 300, ReadOnly = true, BackColor = DipBuilderForm.Surface, ForeColor = DipBuilderForm.Ink };
            setup.Controls.Add(_file);
            setup.Controls.Add(Small("Browse...", (s, e) => Browse()));
            setup.Controls.Add(Small("Limits...", (s, e) => Send("FTFCOMPAREAREA")));
            setup.Controls.Add(Small("Edit profile", (s, e) => Send("FTFCOMPAREPROFILE")));
            setup.Controls.Add(Primary("Run comparison", (s, e) => Post("run", (db, tr, ed) => { CompareSession.RunComparison(db, tr, ed, null); return true; })));
            header.Controls.Add(setup);

            // ---- counts and filter ---------------------------------------------
            var bar = new Panel { Dock = DockStyle.Top, Height = 64, BackColor = DipBuilderForm.Ground, Padding = new Padding(16, 6, 16, 0) };
            _stats = new Label { AutoSize = true, Location = new Point(16, 6), ForeColor = DipBuilderForm.Ink, Text = "Run the comparison to see results." };
            bar.Controls.Add(_stats);
            var filterRow = new FlowLayoutPanel { Location = new Point(12, 30), AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
            filterRow.Controls.Add(Caption("Show"));
            _filter = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
            _filter.Items.AddRange(Filters);
            _filter.SelectedIndex = 0;
            _filter.SelectedIndexChanged += (s, e) => FillList();
            filterRow.Controls.Add(_filter);
            filterRow.Controls.Add(Caption("Find"));
            _search = new TextBox { Width = 180, BackColor = DipBuilderForm.Surface, ForeColor = DipBuilderForm.Ink };
            _search.TextChanged += (s, e) => FillList();
            filterRow.Controls.Add(_search);
            _zoom = new CheckBox { Text = "Zoom to selection", AutoSize = true, Checked = true, Margin = new Padding(14, 4, 0, 0) };
            filterRow.Controls.Add(_zoom);
            filterRow.Controls.Add(Small("Accept all confident", (s, e) => Post("accept confident", (db, tr, ed) =>
            {
                var n = CompareSession.AcceptConfident();
                CompareSession.SaveDecisions(db, tr);
                CompareSession.LastMessage = n + " confident result(s) accepted. Apply approved changes when ready.";
                return true;
            })));
            filterRow.Controls.Add(Small("Keep all unresolved plotting", (s, e) => Post("keep unresolved", (db, tr, ed) =>
            {
                var n = CompareSession.KeepUnresolvedPlotting();
                CompareSession.SaveDecisions(db, tr);
                CompareSession.LastMessage = n + " unresolved result(s) set to keep plotting (nothing stops plotting).";
                return true;
            })));
            bar.Controls.Add(filterRow);

            // ---- list and detail -----------------------------------------------
            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, BackColor = DipBuilderForm.Ground, SplitterWidth = 6 };
            _list = new ListView
            {
                Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = true, GridLines = false,
                BorderStyle = BorderStyle.None, BackColor = DipBuilderForm.Surface, ForeColor = DipBuilderForm.Ink, Font = DipBuilderForm.F(9.5f, false), ShowItemToolTips = true
            };
            foreach (var column in new[] { "Status|110", "Kind|70", "Category|130", "Layer|170", "Handle|80", "Offset ft|70", "dZ ft|60", "Conf|50", "Decision|110", "Notes|420" })
            {
                var parts = column.Split('|');
                _list.Columns.Add(parts[0], int.Parse(parts[1], CultureInfo.InvariantCulture));
            }
            _list.SelectedIndexChanged += (s, e) => OnSelected();
            _list.DoubleClick += (s, e) => Highlight(true);
            split.Panel1.Controls.Add(_list);

            var lower = new Panel { Dock = DockStyle.Fill, BackColor = DipBuilderForm.Ground };
            _detail = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = DipBuilderForm.Surface, ForeColor = DipBuilderForm.Ink, BorderStyle = BorderStyle.None, Font = DipBuilderForm.F(9.5f, false) };
            var actions = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(10, 6, 10, 4), BackColor = DipBuilderForm.Ground };
            actions.Controls.Add(Caption("Selected:"));
            actions.Controls.Add(Small("Accept match", (s, e) => Decide(ReviewDecision.AcceptMatch)));
            actions.Controls.Add(Small("Keep plotting", (s, e) => Decide(ReviewDecision.KeepPlotting)));
            actions.Controls.Add(Small("Make nonplot", (s, e) => Decide(ReviewDecision.MakeNonplot)));
            actions.Controls.Add(Small("Accept partial", (s, e) => Decide(ReviewDecision.AcceptPartial)));
            actions.Controls.Add(Small("Exclude", (s, e) => Decide(ReviewDecision.Exclude)));
            actions.Controls.Add(Small("Restore original", (s, e) => Decide(ReviewDecision.None)));
            actions.Controls.Add(Small("Zoom to feature", (s, e) => Highlight(true)));
            lower.Controls.Add(_detail);
            lower.Controls.Add(actions);
            split.Panel2.Controls.Add(lower);
            split.SplitterDistance = 420;

            // ---- bottom bar ----------------------------------------------------
            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(12, 8, 12, 8), BackColor = DipBuilderForm.Surface };
            bottom.Controls.Add(Small("Close", (s, e) => Close()));
            bottom.Controls.Add(Small("Open report", (s, e) => OpenReport()));
            bottom.Controls.Add(Primary("Save deliverable", (s, e) => Post("save", (db, tr, ed) => { CompareSession.PrepareDeliverable(db, tr, ed, null); return true; })));
            bottom.Controls.Add(Small("Final QC", (s, e) => Post("qc", (db, tr, ed) =>
            {
                var checks = CompareSession.RunQc(db, tr, ed);
                CompareSession.LastMessage = CompareQc.Passes(checks) ? "Final QC passed." : "Final QC FAILED: " + checks.Count(c => !c.Passed && c.Blocking) + " blocking item(s); see the command line.";
                return true;
            })));
            bottom.Controls.Add(Small("Restore drawing", (s, e) => Post("restore", (db, tr, ed) => { CompareSession.RestoreAll(db, tr, ed); return true; })));
            bottom.Controls.Add(Primary("Apply approved changes", (s, e) => Post("apply", (db, tr, ed) => { CompareSession.ApplyPlan(db, tr, ed); return true; })));
            _message = new Label { AutoSize = true, ForeColor = DipBuilderForm.Muted, Margin = new Padding(0, 8, 20, 0), Text = string.Empty };
            bottom.Controls.Add(_message);

            Controls.Add(split);
            Controls.Add(bar);
            Controls.Add(header);
            Controls.Add(bottom);

            FormClosed += (s, e) => Post("clear", (db, tr, ed) => { CompareHighlighter.Clear(tr); return false; });
        }

        // ================================================================ helpers

        private static Label Caption(string text)
        {
            return new Label { Text = text, AutoSize = true, ForeColor = DipBuilderForm.Muted, Margin = new Padding(6, 6, 2, 0) };
        }

        private Button Small(string text, EventHandler click) { return Make(text, click, false); }
        private Button Primary(string text, EventHandler click) { return Make(text, click, true); }

        private Button Make(string text, EventHandler click, bool primary)
        {
            var b = new Button
            {
                Text = text, AutoSize = true, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, Margin = new Padding(4, 0, 0, 0), Padding = new Padding(8, 2, 8, 2),
                BackColor = primary ? DipBuilderForm.Accent : DipBuilderForm.Surface, ForeColor = primary ? Color.White : DipBuilderForm.Ink
            };
            b.FlatAppearance.BorderColor = primary ? DipBuilderForm.Accent : DipBuilderForm.ButtonBorder;
            b.Click += click;
            _buttons.Add(b);
            return b;
        }

        private void Post(string name, CompareSession.CompareWork work)
        {
            if (!CompareSession.Post(name, work)) _message.Text = CompareSession.LastMessage ?? "Could not start.";
            else _message.Text = "Working: " + name + "...";
        }

        private static void Send(string command)
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc != null) doc.SendStringToExecute(command + " ", true, false, true);
        }

        private IList<FeatureResult> SelectedResults()
        {
            var list = new List<FeatureResult>();
            foreach (ListViewItem item in _list.SelectedItems)
            {
                var r = item.Tag as FeatureResult;
                if (r != null) list.Add(r);
            }
            return list;
        }

        // ================================================================ actions

        private void OnProfileChosen()
        {
            var name = _profile.SelectedItem as string;
            if (string.IsNullOrEmpty(name)) return;
            Post("profile", (db, tr, ed) =>
            {
                CompareSession.ChooseProfile(db, tr, name);
                CompareSession.RebuildPlan();
                CompareSession.LastMessage = "Profile " + name + " [" + CompareSession.ProfileLevel + "]. Re-run to compare with it.";
                return true;
            });
        }

        private void OnSourceChanged()
        {
            var file = _source.SelectedIndex == 1;
            Post("source", (db, tr, ed) =>
            {
                CompareSession.Source.Mode = file ? "File" : "Xrefs";
                CompareSession.SaveSource(db, tr);
                return true;
            });
        }

        private void Browse()
        {
            using (var dialog = new OpenFileDialog { Filter = "Drawing (*.dwg)|*.dwg", Title = "Consultant base drawing" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                var path = dialog.FileName;
                Post("source file", (db, tr, ed) =>
                {
                    CompareSession.Source.Mode = "File";
                    CompareSession.Source.FilePath = path;
                    CompareSession.SaveSource(db, tr);
                    return true;
                });
            }
        }

        private void Decide(ReviewDecision decision)
        {
            var handles = SelectedResults().Select(r => r.Handle).ToList();
            if (handles.Count == 0) { _message.Text = "Select one or more results first."; return; }
            Post(decision.ToString(), (db, tr, ed) =>
            {
                var n = CompareSession.Decide(handles, decision, "review window");
                CompareSession.SaveDecisions(db, tr);
                CompareSession.LastMessage = n + " decision(s) recorded (" + decision + "). Apply approved changes when ready.";
                return true;
            });
        }

        private void OnSelected()
        {
            var selected = SelectedResults();
            _detail.Text = selected.Count == 1 ? Describe(selected[0]) : selected.Count + " selected.";
            if (selected.Count == 1 && _zoom.Checked) Highlight(true);
        }

        private void Highlight(bool zoom)
        {
            var selected = SelectedResults();
            if (selected.Count != 1) return;
            var r = selected[0];
            var settings = CompareSession.SettingsFor(AcadApp.DocumentManager.MdiActiveDocument.Database).Compare;
            Post("highlight", (db, tr, ed) =>
            {
                var theirs = new List<ConsultantShape>();
                foreach (var id in r.MatchedIds)
                {
                    ConsultantShape shape;
                    if (CompareSession.Shapes.TryGetValue(id, out shape)) theirs.Add(shape);
                }
                CompareHighlighter.Show(db, tr, ed, r, theirs, settings.HighlightSurveyColor, settings.HighlightConsultantColor, zoom);
                return false;
            });
        }

        private void OpenReport()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var path = CompareReport.PathFor(doc.Database.Filename);
            if (path == null || !File.Exists(path)) { _message.Text = "No report yet: run the comparison (the drawing must be saved)."; return; }
            try { System.Diagnostics.Process.Start(path); }
            catch (System.ComponentModel.Win32Exception ex) { _message.Text = "Could not open the report: " + ex.Message; }
        }

        // ================================================================ display

        public void RefreshFromSession()
        {
            _loading = true;
            try
            {
                var doc = AcadApp.DocumentManager.MdiActiveDocument;
                var drawing = doc == null ? "(no drawing)" : string.IsNullOrEmpty(doc.Database.Filename) ? "(unsaved drawing)" : Path.GetFileName(doc.Database.Filename);
                var profile = CompareSession.Profile;
                _status.Text = drawing + "   |   profile: " + (profile != null ? profile.Name + " [" + CompareSession.ProfileLevel + "]" : "-") +
                               "   |   consultant: " + (CompareSession.Source != null ? CompareSession.Source.Describe() : "-") +
                               (CompareSession.Applied != null && CompareSession.Applied.Changes.Count > 0 ? "   |   " + CompareSession.Applied.Changes.Count + " change(s) applied" : "   |   nothing applied");

                _profile.Items.Clear();
                foreach (var name in CompareProfile.ListProfiles(CompareSession.PluginDirectory())) _profile.Items.Add(name);
                if (profile != null)
                {
                    var index = _profile.Items.IndexOf(profile.Name);
                    if (index < 0) { _profile.Items.Add(profile.Name); index = _profile.Items.Count - 1; }
                    _profile.SelectedIndex = index;
                }
                _source.SelectedIndex = CompareSession.Source != null && CompareSession.Source.IsFile ? 1 : 0;
                _file.Text = CompareSession.Source != null ? (CompareSession.Source.IsFile ? CompareSession.Source.FilePath : string.Empty) : string.Empty;
                _file.Visible = _source.SelectedIndex == 1;

                var result = CompareSession.Result;
                if (result == null) _stats.Text = "Run the comparison to see results.";
                else
                {
                    var s = result.Summary();
                    var plan = CompareSession.Plan;
                    _stats.Text = string.Format(CultureInfo.InvariantCulture,
                        "Evaluated {0}   Matched {1}   Missing {2}   Partial {3}   Discrepancies {4}   Needs review {5}   Not evaluated {6}   Excluded {7}   Decided {8}" +
                        (plan != null ? "     Plan: {9} nonplot, {10} split, {11} awaiting review" : string.Empty) +
                        (result.ConsultantIncomplete ? "     CONSULTANT DATA INCOMPLETE" : string.Empty),
                        s.Total, s.Matched, s.Missing, s.Partial, s.Discrepancies, s.NeedsReview, s.NotEvaluated, s.Excluded, s.Decided,
                        plan != null ? plan.NonplotCount : 0, plan != null ? plan.SplitCount : 0, plan != null ? plan.AwaitingReview : 0);
                }
                FillList();
                _message.Text = CompareSession.LastMessage ?? string.Empty;
            }
            finally { _loading = false; }
        }

        private void FillList()
        {
            var selected = new HashSet<string>(SelectedResults().Select(r => r.Handle), StringComparer.OrdinalIgnoreCase);
            _list.BeginUpdate();
            _list.Items.Clear();
            var result = CompareSession.Result;
            if (result != null)
            {
                var filter = _filter.SelectedItem as string ?? Filters[0];
                var find = (_search.Text ?? string.Empty).Trim();
                foreach (var r in result.Results.Where(r => Passes(r, filter)).Where(r => Finds(r, find)).OrderBy(r => Order(r.EffectiveStatus)).ThenBy(r => r.Category).ThenBy(r => r.Handle, StringComparer.OrdinalIgnoreCase))
                {
                    var item = new ListViewItem(new[]
                    {
                        r.EffectiveStatus.ToString(),
                        r.Kind.ToString(),
                        string.IsNullOrEmpty(r.Category) ? "(unmapped)" : r.Category,
                        r.SourceLayer ?? string.Empty,
                        r.Handle ?? string.Empty,
                        r.HorizontalOffset.HasValue ? (r.HorizontalOffset.Value / Units(result)).ToString("0.00", CultureInfo.InvariantCulture) : string.Empty,
                        r.VerticalDifference.HasValue ? (r.VerticalDifference.Value / Units(result)).ToString("0.00", CultureInfo.InvariantCulture) : string.Empty,
                        r.Confidence.ToString("0.00", CultureInfo.InvariantCulture),
                        r.Decision == ReviewDecision.None ? (r.Action == PlannedAction.None ? string.Empty : "auto " + r.Action) : r.Decision.ToString(),
                        (string.IsNullOrEmpty(r.ApplyProblem) ? string.Empty : "NOT APPLIED: " + r.ApplyProblem + "; ") + r.NotesText
                    })
                    { Tag = r, ToolTipText = r.Description + "\n" + r.NotesText, ForeColor = ColorFor(r.EffectiveStatus) };
                    if (selected.Contains(r.Handle)) item.Selected = true;
                    _list.Items.Add(item);
                }
            }
            _list.EndUpdate();
        }

        private static double Units(ComparisonResult result) { return result.UnitsPerFoot > 0 ? result.UnitsPerFoot : 1.0; }

        private static bool Passes(FeatureResult r, string filter)
        {
            switch (filter)
            {
                case "All": return true;
                case "Missing": return r.EffectiveStatus == MatchStatus.Missing;
                case "Matched": return r.EffectiveStatus == MatchStatus.Matched;
                case "Partial": return r.EffectiveStatus == MatchStatus.Partial;
                case "Discrepancies": return r.EffectiveStatus == MatchStatus.Discrepancy;
                case "Needs Review": return r.EffectiveStatus == MatchStatus.NeedsReview;
                case "Not Evaluated": return r.EffectiveStatus == MatchStatus.NotEvaluated;
                case "Excluded": return r.EffectiveStatus == MatchStatus.Excluded;
                case "Decided": return r.Decision != ReviewDecision.None;
                case "Planned changes": return r.Action != PlannedAction.None;
                default: return r.EffectiveStatus == MatchStatus.Discrepancy || r.EffectiveStatus == MatchStatus.NeedsReview || r.EffectiveStatus == MatchStatus.NotEvaluated || r.EffectiveStatus == MatchStatus.Partial;
            }
        }

        private static bool Finds(FeatureResult r, string find)
        {
            if (find.Length == 0) return true;
            return (r.Handle ?? string.Empty).IndexOf(find, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   (r.Category ?? string.Empty).IndexOf(find, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   (r.SourceLayer ?? string.Empty).IndexOf(find, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   (r.Description ?? string.Empty).IndexOf(find, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   r.NotesText.IndexOf(find, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static int Order(MatchStatus s)
        {
            switch (s)
            {
                case MatchStatus.Discrepancy: return 0;
                case MatchStatus.NeedsReview: return 1;
                case MatchStatus.NotEvaluated: return 2;
                case MatchStatus.Partial: return 3;
                case MatchStatus.Missing: return 4;
                case MatchStatus.Matched: return 5;
                default: return 6;
            }
        }

        private static Color ColorFor(MatchStatus s)
        {
            switch (s)
            {
                case MatchStatus.Discrepancy: return DipBuilderForm.Bad;
                case MatchStatus.NeedsReview: case MatchStatus.NotEvaluated: case MatchStatus.Partial: return DipBuilderForm.Warn;
                case MatchStatus.Matched: return DipBuilderForm.Good;
                default: return DipBuilderForm.Ink;
            }
        }

        private static string Describe(FeatureResult r)
        {
            var lines = new List<string>
            {
                r.Describe(),
                "Automatic result: " + r.Status + " (confidence " + r.Confidence.ToString("0.00", CultureInfo.InvariantCulture) + ")" +
                    (r.Decision != ReviewDecision.None ? "   decision: " + r.Decision + (string.IsNullOrEmpty(r.DecisionNote) ? string.Empty : " - " + r.DecisionNote) : string.Empty),
                "Plan: " + (r.Action == PlannedAction.None ? "keep as is" : r.Action + " -> " + r.NonplotLayer)
            };
            if (!string.IsNullOrEmpty(r.ApplyProblem)) lines.Add("NOT APPLIED: " + r.ApplyProblem);
            if (r.MatchedHandles.Count > 0) lines.Add("Consultant object(s): " + string.Join(", ", r.MatchedHandles.ToArray()));
            if (r.Kind == FeatureKind.Line && r.Intervals.Count > 0)
                lines.Add("Stations: " + string.Join("  ", r.Intervals.Select(i => i.ToString()).ToArray()));
            if (!string.IsNullOrEmpty(r.AssociatedFeatureId)) lines.Add("Belongs to: " + r.AssociatedFeatureId);
            if (!string.IsNullOrEmpty(r.Description)) lines.Add("Text / description: " + r.Description);
            lines.AddRange(r.Notes.Select(n => "- " + n));
            return string.Join(Environment.NewLine, lines.ToArray());
        }
    }

    internal static class FeatureResultDisplay
    {
        public static string Describe(this FeatureResult r)
        {
            return r.Kind + " " + r.EntityType + (string.IsNullOrEmpty(r.BlockName) ? string.Empty : " " + r.BlockName) +
                   " on " + r.SourceLayer + "  [" + (string.IsNullOrEmpty(r.Category) ? "unmapped" : r.Category) + "]  handle " + r.Handle;
        }
    }
}
