using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using FieldCodes.Commands;
using FieldCodes.Settings;

namespace FieldCodes.Cad.Setup
{
    /// <summary>
    /// Settings > Commands: every FTF command in one searchable, sortable table -- what it does,
    /// where it is on the ribbon, the settings pages that change it (each a link to that page)
    /// and whether it is a processing stage. The list is read from the plugin's own
    /// [CommandMethod] attributes and the Settings page registry, so a new command appears here
    /// without this page changing.
    ///
    /// Ticking a command puts it on the person's own My Commands panel on the FieldToFinish tab
    /// (ui-state.json, not the settings file: the office standard is untouched). The tick is
    /// saved at once; the panel appears when the ribbon is next built, or straight away with
    /// Rebuild Ribbon Now.
    ///
    /// UNTESTED as UI; the catalog and the panel it builds are unit tested.
    /// </summary>
    [SettingsPage(Group = "Administration", Label = "Commands", Order = 40)]
    internal sealed class CommandsPage : SetupPage
    {
        private const int ColCommand = 0, ColName = 1, ColRibbon = 2, ColSettings = 3, ColStage = 4;

        private TextBox _search;
        private CheckBox _mineOnly;
        private ListView _list;
        private Label _count;
        private Label _detailTitle;
        private Label _detailText;
        private FlowLayoutPanel _links;
        private Label _ribbonStatus;

        private IList<CommandEntry> _entries = new List<CommandEntry>();
        private IList<string> _everyCommand = new List<string>();
        private List<string> _mine = new List<string>();
        private bool _filling;

        public override string Title { get { return "Commands"; } }
        public override string AffectedCommands { get { return "your own My Commands ribbon panel (kept for you, not in the settings file)"; } }
        public override bool IsReadOnly { get { return true; } }

        protected override void BuildBody() { }

        protected override Control BuildFilled()
        {
            var root = new TableLayoutPanel { ColumnCount = 1, RowCount = 5 };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            root.Controls.Add(new Label
            {
                Text = "Every FTF command: what it does, where it is on the ribbon and which settings change it. " +
                       "Click a column to sort; select a command for its settings. Tick a command to put it on " +
                       "a My Commands panel on the FieldToFinish tab -- your own, kept apart from the office settings.",
                AutoSize = true, MaximumSize = new Size(760, 0), ForeColor = SystemColors.GrayText,
                Margin = new Padding(0, 0, 0, 6)
            }, 0, 0);

            var bar = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = new Padding(0, 0, 0, 4) };
            bar.Controls.Add(new Label { Text = "Search", AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
            _search = new TextBox { Width = 220, Tag = Ui.SettingsPanel.ViewOnly };
            _search.TextChanged += (s, e) => Fill();
            bar.Controls.Add(_search);
            _mineOnly = new CheckBox { Text = "Only My Commands", AutoSize = true, Margin = new Padding(12, 5, 0, 0), Tag = Ui.SettingsPanel.ViewOnly };
            _mineOnly.CheckedChanged += (s, e) => Fill();
            bar.Controls.Add(_mineOnly);
            _count = new Label { AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(12, 6, 0, 0) };
            bar.Controls.Add(_count);
            root.Controls.Add(bar, 0, 1);

            _list = new ListView
            {
                View = View.Details,
                CheckBoxes = true,
                FullRowSelect = true,
                HideSelection = false,
                MultiSelect = false,
                GridLines = true,
                Dock = DockStyle.Fill,
                HeaderStyle = ColumnHeaderStyle.Clickable
            };
            _list.Columns.Add("Command", 150);
            _list.Columns.Add("Name", 140);
            _list.Columns.Add("On the ribbon", 210);
            _list.Columns.Add("Settings", 230);
            _list.Columns.Add("Processing", 110);
            Ui.ListViewSorting.Attach(_list);
            _list.ItemChecked += (s, e) => OnTicked(e.Item);
            _list.SelectedIndexChanged += (s, e) => ShowDetail();
            root.Controls.Add(_list, 0, 2);

            var detail = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true,
                Dock = DockStyle.Fill, Margin = new Padding(0, 6, 0, 0)
            };
            _detailTitle = new Label { AutoSize = true, Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold) };
            _detailText = new Label { AutoSize = true, MaximumSize = new Size(760, 0), Margin = new Padding(0, 2, 0, 2) };
            _links = new FlowLayoutPanel { AutoSize = true, WrapContents = true, MaximumSize = new Size(760, 0), Margin = new Padding(0) };
            detail.Controls.Add(_detailTitle);
            detail.Controls.Add(_detailText);
            detail.Controls.Add(_links);
            root.Controls.Add(detail, 0, 3);

            var ribbon = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = new Padding(0, 6, 0, 0) };
            var rebuild = new Button { Text = "Rebuild Ribbon Now", AutoSize = true };
            Tip(rebuild, "Rebuilds the FieldToFinish ribbon tab so My Commands shows your ticks now. " +
                         "Otherwise the panel changes the next time Civil 3D starts.");
            rebuild.Click += (s, e) => RebuildRibbon();
            ribbon.Controls.Add(rebuild);
            _ribbonStatus = new Label { AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(10, 7, 0, 0) };
            ribbon.Controls.Add(_ribbonStatus);
            root.Controls.Add(ribbon, 0, 4);

            try
            {
                _entries = FtfCommandCatalog.Entries();
                _everyCommand = CommandCatalog.PagesForEveryCommand(SettingsPages.All);
            }
            catch (Exception ex)
            {
                _detailText.Text = "The command list could not be read: " + ex.Message;
            }
            return root;
        }

        // ------------------------------------------------------------------ filling

        public override void LoadFrom(FtfSettings settings)
        {
            var saved = Ui.UiState.Load().QuickCommands;
            _mine = saved != null ? saved.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim().ToUpperInvariant()).Distinct().ToList()
                                  : new List<string>();
            Fill();
        }

        // Nothing here is a setting: the ticks are saved as they are made, to the person's own file.
        public override void SaveTo(FtfSettings settings, ICollection<string> problems) { }
        public override void RestoreDefaults(FtfSettings settings) { }

        private void Fill()
        {
            if (_list == null) return;
            var selected = _list.SelectedItems.Count > 0 ? ((CommandEntry)_list.SelectedItems[0].Tag).Command : null;
            var text = _search.Text;
            var shown = _entries.Where(e => e.Matches(text) && (!_mineOnly.Checked || _mine.Contains(e.Command))).ToList();

            _filling = true;
            _list.BeginUpdate();
            try
            {
                _list.Items.Clear();
                var items = shown.Select(Row).ToArray();
                _list.Items.AddRange(items);
                foreach (var item in items)
                    if (((CommandEntry)item.Tag).Command == selected) { item.Selected = true; item.EnsureVisible(); }
            }
            finally
            {
                _list.EndUpdate();
                _filling = false;
            }
            _count.Text = shown.Count + " of " + _entries.Count + " commands; " + _mine.Count + " on My Commands";
            ShowDetail();
        }

        private ListViewItem Row(CommandEntry e)
        {
            var aliases = e.Aliases.Count > 0 ? " (" + string.Join(", ", e.Aliases.ToArray()) + ")" : string.Empty;
            var stage = e.IsStage ? "Stage " + e.StageNumber : e.CanFollowWorkflow ? "After a workflow" : string.Empty;
            var item = new ListViewItem(new[]
            {
                e.Command + aliases,
                e.Title,
                e.OnRibbon ? string.Join("; ", e.RibbonPlaces.ToArray()) : "-",
                e.SettingsPages.Count > 0 ? string.Join(", ", e.SettingsPages.ToArray()) : "-",
                stage
            })
            {
                Tag = e,
                Checked = _mine.Contains(e.Command),
                ToolTipText = e.Description
            };
            return item;
        }

        private void ShowDetail()
        {
            _links.Controls.Clear();
            var entry = _list.SelectedItems.Count > 0 ? (CommandEntry)_list.SelectedItems[0].Tag : null;
            if (entry == null)
            {
                _detailTitle.Text = string.Empty;
                _detailText.Text = "Select a command to see the settings that change it.";
                return;
            }

            _detailTitle.Text = Escape(entry.Command + (entry.Title.Length > 0 ? " -- " + entry.Title : string.Empty));
            var lines = new List<string>();
            if (entry.Description.Length > 0) lines.Add(entry.Description);
            if (entry.Aliases.Count > 0) lines.Add("Also typed as " + string.Join(", ", entry.Aliases.ToArray()) + ".");
            lines.Add(entry.OnRibbon ? "On the ribbon: " + string.Join("; ", entry.RibbonPlaces.ToArray()) + "."
                                     : "Not on the ribbon: type it, or tick it for My Commands.");
            if (entry.IsStage) lines.Add("Processing stage " + entry.StageNumber + ": Process Drawing, FTFRUN and saved workflows can run it.");
            if (entry.CanFollowWorkflow) lines.Add("A saved workflow can run it afterwards (Settings > Workflows).");
            _detailText.Text = Escape(string.Join(Environment.NewLine, lines.ToArray()));

            if (entry.SettingsPages.Count > 0)
            {
                _links.Controls.Add(new Label { Text = "Settings that change it:", AutoSize = true, Margin = new Padding(0, 4, 4, 0) });
                foreach (var page in entry.SettingsPages) _links.Controls.Add(PageLink(page));
            }
            if (_everyCommand.Count > 0)
            {
                _links.Controls.Add(new Label { Text = "Every command also follows:", AutoSize = true, Margin = new Padding(10, 4, 4, 0) });
                foreach (var page in _everyCommand) _links.Controls.Add(PageLink(page));
            }
        }

        /// <summary>A link that shows that settings page.</summary>
        private LinkLabel PageLink(string label)
        {
            var link = new LinkLabel { Text = Escape(label), AutoSize = true, Margin = new Padding(0, 4, 6, 0) };
            link.LinkClicked += (s, e) => { if (Context.Navigate != null) Context.Navigate(label); };
            return link;
        }

        // ------------------------------------------------------------- my commands

        private void OnTicked(ListViewItem item)
        {
            if (_filling || item == null) return;
            var command = ((CommandEntry)item.Tag).Command;
            // The list re-raises this for ticked rows when its window is first created: no change.
            if (item.Checked == _mine.Contains(command)) return;
            if (item.Checked) _mine.Add(command);
            else _mine.Remove(command);
            Ui.UiState.SaveQuickCommands(_mine);
            _count.Text = _list.Items.Count + " of " + _entries.Count + " commands; " + _mine.Count + " on My Commands";
            _ribbonStatus.Text = "Saved. My Commands changes when the ribbon is next built, or now with Rebuild Ribbon Now.";
        }

        private void RebuildRibbon()
        {
            _ribbonStatus.Text = FtfRibbon.RebuildFieldToFinishTab()
                ? "The FieldToFinish tab was rebuilt: " + (_mine.Count == 0 ? "no My Commands panel." : _mine.Count + " command(s) on My Commands.")
                : "The ribbon could not be rebuilt now; My Commands appears the next time Civil 3D starts.";
        }
    }
}
