using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using FieldCodes.Annotation;
using FieldCodes.Commands;
using FieldCodes.Settings;

namespace FieldCodes.Cad.Setup
{
    /// <summary>
    /// Settings > Workflows: named processing runs beside the built-in presets -- the stages, the
    /// feature families the label and tag stages cover, and what runs afterwards. Each saved
    /// workflow appears in the Process page's preset list and at FTFRUN's prompt, and runs exactly
    /// as a preset does. New CAD functionality still needs code; putting existing stages together
    /// in a new way does not.
    ///
    /// The editor's controls opt out of the window's automatic change tracking (selecting another
    /// workflow fills them, which is not an edit) and say "changed" themselves when the user edits.
    ///
    /// UNTESTED as UI; the workflow model, its validation and the plans it builds are unit tested.
    /// </summary>
    [SettingsPage(Group = "FieldToFinish", Label = "Workflows", Order = 130, Sections = "workflows", Commands = "FTFRUN,FTF")]
    internal sealed class WorkflowsPage : SetupPage
    {
        private ListBox _list;
        private TableLayoutPanel _editor;
        private TextBox _name;
        private TextBox _description;
        private CheckedListBox _stages;
        private CheckedListBox _families;
        private CheckedListBox _thenRun;
        private Label _preview;
        private Label _problems;

        private List<WorkflowDefinition> _items = new List<WorkflowDefinition>();
        private AnnotationSettings _annotation = new AnnotationSettings();
        private int _editing = -1;
        private bool _filling;

        public override string Title { get { return "Workflows"; } }
        public override string AffectedCommands { get { return "FTFRUN and the FTF window's Process page"; } }

        protected override void BuildBody()
        {
            Note("A workflow is a named processing run: the stages, the feature families the label and tag " +
                 "stages cover, and commands to run afterwards. Saved workflows appear in the Process page's " +
                 "preset list, after the built-in presets, and as choices at FTFRUN's prompt.");

            Heading("Saved workflows");
            _list = new ListBox { Width = 300, Height = 120, IntegralHeight = false };
            _list.SelectedIndexChanged += (s, e) => { if (!_filling) Edit(_list.SelectedIndex); };
            var add = new Button { Text = "New" };
            add.Click += (s, e) => Add(NewWorkflow());
            var copy = new Button { Text = "Duplicate" };
            copy.Click += (s, e) => Duplicate();
            var remove = new Button { Text = "Delete" };
            remove.Click += (s, e) => Delete();
            var up = new Button { Text = "Move Up" };
            up.Click += (s, e) => Move(-1);
            var down = new Button { Text = "Move Down" };
            down.Click += (s, e) => Move(1);
            AddWithSideButtons(_list, SideButtons(add, copy, remove, up, down));

            Heading("Selected workflow");
            _editor = BuildEditor();
            AddFullWidth(_editor);

            _preview = LiveNote();
            _problems = LiveNote();
            _problems.ForeColor = Color.FromArgb(170, 40, 30);

            Note("Draw order is added when a stage draws annotation, and stages always run in pipeline order. " +
                 "No family ticked means the families set on Label All. Only commands that never stop to ask " +
                 "anything can run afterwards.");
        }

        private TableLayoutPanel BuildEditor()
        {
            // ViewOnly: the window does not watch these; Changed() below says when the user edits.
            var editor = new TableLayoutPanel
            {
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 3,
                Margin = new Padding(0, 0, 0, 4), Tag = Ui.SettingsPanel.ViewOnly
            };

            editor.Controls.Add(Caption("Name"), 0, 0);
            _name = new TextBox { Width = 280 };
            editor.Controls.Add(_name, 1, 0);
            editor.SetColumnSpan(_name, 2);

            editor.Controls.Add(Caption("Description"), 0, 1);
            _description = new TextBox { Width = 520 };
            editor.Controls.Add(_description, 1, 1);
            editor.SetColumnSpan(_description, 2);

            editor.Controls.Add(Caption("Stages (in run order)"), 0, 2);
            editor.Controls.Add(Caption("Feature families"), 1, 2);
            editor.Controls.Add(Caption("Then run"), 2, 2);

            _stages = new CheckedListBox { Width = 210, Height = 150, CheckOnClick = true, IntegralHeight = false };
            foreach (var step in new FtfPipeline().Steps) _stages.Items.Add(new Choice(step.Name, step.Title));
            _families = new CheckedListBox { Width = 210, Height = 150, CheckOnClick = true, IntegralHeight = false };
            foreach (var c in AnnotationPolicy.All()) _families.Items.Add(new Choice(c.ToString(), AnnotationPolicy.Name(c)));
            _thenRun = new CheckedListBox { Width = 210, Height = 150, CheckOnClick = true, IntegralHeight = false };
            foreach (var command in SettingsRegistry.WorkflowFollowUps)
            {
                var button = RibbonLayout.AllButtons.FirstOrDefault(b => b.Command == command);
                _thenRun.Items.Add(new Choice(command, (button != null ? button.Title + " " : string.Empty) + "(" + command + ")"));
            }
            editor.Controls.Add(_stages, 0, 3);
            editor.Controls.Add(_families, 1, 3);
            editor.Controls.Add(_thenRun, 2, 3);

            _name.TextChanged += (s, e) => Changed(true);
            _description.TextChanged += (s, e) => Changed(false);
            foreach (var list in new[] { _stages, _families, _thenRun })
            {
                var box = list;
                box.ItemCheck += (s, e) => { if (!_filling) box.BeginInvoke((Action)(() => Changed(false))); };
            }
            return editor;
        }

        private static Label Caption(string text)
        {
            return new Label { Text = text, AutoSize = true, Margin = new Padding(0, 6, 12, 3) };
        }

        /// <summary>A stage, family or command in one of the editor's lists.</summary>
        private sealed class Choice
        {
            public readonly string Key;
            public readonly string Text;
            public Choice(string key, string text) { Key = key; Text = text; }
            public override string ToString() { return Text; }
        }

        // ------------------------------------------------------------------ loading

        public override void LoadFrom(FtfSettings s)
        {
            _annotation = s.Annotation ?? new AnnotationSettings();
            _items = (s.Workflows != null && s.Workflows.Items != null ? s.Workflows.Items : new List<WorkflowDefinition>())
                .Where(w => w != null).Select(w => w.Clone()).ToList();
            _editing = -1;
            FillList(_items.Count > 0 ? 0 : -1);
        }

        public override void SaveTo(FtfSettings s, ICollection<string> problems)
        {
            Commit();
            var section = new WorkflowSettings { Items = _items.Select(w => w.Clone()).ToList() };
            section.Normalize();
            s.Workflows = section;

            // Show what will be saved: stages in run order, draw order where it is needed.
            _items = section.Items.Select(w => w.Clone()).ToList();
            var keep = _editing;
            _editing = -1;
            FillList(keep);
        }

        public override void RestoreDefaults(FtfSettings s)
        {
            s.Workflows = new WorkflowSettings();
            LoadFrom(s);
            MarkChanged();
        }

        // ------------------------------------------------------------------ editing

        private void FillList(int select)
        {
            _filling = true;
            try
            {
                _list.Items.Clear();
                foreach (var w in _items) _list.Items.Add(Display(w));
                _list.SelectedIndex = select >= 0 && select < _items.Count ? select : -1;
            }
            finally { _filling = false; }
            Edit(_list.SelectedIndex);
        }

        private static string Display(WorkflowDefinition w)
        {
            return string.IsNullOrWhiteSpace(w.Name) ? "(no name)" : w.Name.Trim();
        }

        /// <summary>Puts the editor's values back into the workflow being edited.</summary>
        private void Commit()
        {
            if (_editing < 0 || _editing >= _items.Count) return;
            var w = _items[_editing];
            w.Name = (_name.Text ?? string.Empty).Trim();
            w.Description = (_description.Text ?? string.Empty).Trim();
            w.Stages = Checked(_stages).ToList();
            w.Families = Checked(_families).Select(k => (AnnotationCategory)Enum.Parse(typeof(AnnotationCategory), k)).ToList();
            w.ThenRun = Checked(_thenRun).ToList();
        }

        private static IEnumerable<string> Checked(CheckedListBox list)
        {
            return list.CheckedItems.Cast<Choice>().Select(c => c.Key);
        }

        /// <summary>Shows workflow <paramref name="index"/> in the editor (none: the editor is off).</summary>
        private void Edit(int index)
        {
            Commit();
            _editing = index >= 0 && index < _items.Count ? index : -1;
            var w = _editing >= 0 ? _items[_editing] : null;

            _filling = true;
            try
            {
                _editor.Enabled = w != null;
                _name.Text = w != null ? w.Name : string.Empty;
                _description.Text = w != null ? w.Description : string.Empty;
                SetChecked(_stages, w != null ? w.Stages : null);
                SetChecked(_families, w != null ? w.Families.Select(f => f.ToString()) : null);
                SetChecked(_thenRun, w != null ? w.ThenRun : null);
            }
            finally { _filling = false; }
            ShowPreview();
        }

        private static void SetChecked(CheckedListBox list, IEnumerable<string> keys)
        {
            var wanted = new HashSet<string>(keys ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < list.Items.Count; i++) list.SetItemChecked(i, wanted.Contains(((Choice)list.Items[i]).Key));
        }

        /// <summary>An edit in the editor: the workflow takes it, the list and preview follow, and
        /// the window shows unsaved changes.</summary>
        private void Changed(bool renamed)
        {
            if (_filling || _editing < 0) return;
            Commit();
            if (renamed)
            {
                _filling = true;
                try
                {
                    _list.Items[_editing] = Display(_items[_editing]);
                    _list.SelectedIndex = _editing;
                }
                finally { _filling = false; }
            }
            ShowPreview();
            MarkChanged();
        }

        private void ShowPreview()
        {
            var problems = new List<string>();
            new WorkflowSettings { Items = _items.Select(x => x.Clone()).ToList() }.Validate(problems);
            _problems.Text = string.Join(Environment.NewLine, problems.ToArray());

            if (_editing < 0)
            {
                _preview.Text = _items.Count == 0 ? "No workflows yet: New starts one." : string.Empty;
                return;
            }

            var w = _items[_editing];
            var plan = ProcessPresets.Build(w, _annotation);
            var titles = _stages.Items.Cast<Choice>().ToDictionary(c => c.Key, c => c.Text, StringComparer.OrdinalIgnoreCase);
            var lines = new List<string>();
            var keyword = WorkflowSettings.Keyword(w.Name);
            if (keyword.Length > 0) lines.Add("At FTFRUN's prompt: " + keyword + ".");
            string title;
            lines.Add(plan.Stages.Count == 0
                ? "Runs nothing as it stands."
                : "Runs: " + string.Join(" > ", plan.Stages.Select(st => titles.TryGetValue(st, out title) ? title : st).ToArray()) + ".");
            lines.Add("Families: " + (w.Families.Count == 0 ? "those set on Label All (" : string.Empty) +
                      string.Join(", ", plan.LabelFamilies.Select(AnnotationPolicy.Name).ToArray()) +
                      (w.Families.Count == 0 ? ")." : "."));
            if (plan.ThenRun.Count > 0) lines.Add("Then: " + string.Join(", ", plan.ThenRun.ToArray()) + ".");
            lines.AddRange(plan.Adjustments);
            _preview.Text = Escape(string.Join(Environment.NewLine, lines.ToArray()));
        }

        // ------------------------------------------------------------------ the list

        private static WorkflowDefinition NewWorkflow()
        {
            return new WorkflowDefinition
            {
                Name = "New Workflow",
                Stages = new List<string> { ProcessStage.Points, ProcessStage.Labels, ProcessStage.Order }
            };
        }

        private void Add(WorkflowDefinition w)
        {
            Commit();
            w.Name = UniqueName(w.Name);
            _items.Add(w);
            _editing = -1;
            FillList(_items.Count - 1);
            MarkChanged();
            _name.Focus();
            _name.SelectAll();
        }

        private void Duplicate()
        {
            Commit();
            if (_editing < 0) return;
            var copy = _items[_editing].Clone();
            copy.Name = copy.Name + " Copy";
            Add(copy);
        }

        private void Delete()
        {
            if (_editing < 0) return;
            var index = _editing;
            _editing = -1;          // nothing to commit: it is going
            _items.RemoveAt(index);
            FillList(Math.Min(index, _items.Count - 1));
            MarkChanged();
        }

        private void Move(int by)
        {
            Commit();
            var from = _editing;
            var to = from + by;
            if (from < 0 || to < 0 || to >= _items.Count) return;
            var w = _items[from];
            _items.RemoveAt(from);
            _items.Insert(to, w);
            _editing = -1;
            FillList(to);
            MarkChanged();
        }

        private string UniqueName(string name)
        {
            var root = string.IsNullOrWhiteSpace(name) ? "Workflow" : name.Trim();
            var candidate = root;
            for (var n = 2; _items.Any(w => string.Equals((w.Name ?? string.Empty).Trim(), candidate, StringComparison.OrdinalIgnoreCase)); n++)
                candidate = root + " " + n;
            return candidate;
        }
    }
}
