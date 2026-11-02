using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using FieldCodes.Annotation;
using FieldCodes.Settings;
using FieldCodes.Styles;
using Newtonsoft.Json;

namespace FieldCodes.Cad.Setup
{
    /// <summary>
    /// The one working copy of the annotation styles that the Text Styles, Leader Styles,
    /// Style Assignments and Point Features pages all edit, so an edit on one page is seen by
    /// the others and Save writes them together. Keyed by the loaded section: after Discard or
    /// an import the settings hold a new section and the pages start again from it.
    /// </summary>
    internal sealed class StyleWorkspace
    {
        private static readonly ConditionalWeakTable<StyleSettings, StyleWorkspace> Table =
            new ConditionalWeakTable<StyleSettings, StyleWorkspace>();

        public StyleSettings Styles { get; private set; }

        /// <summary>Raised when a style is added, renamed or removed, so the other pages refresh their lists.</summary>
        public event Action NamesChanged;

        private StyleWorkspace(StyleSettings from) { Styles = Copy(from); }

        public static StyleWorkspace For(FtfSettings settings)
        {
            if (settings.Styles == null) settings.Styles = new StyleSettings();
            settings.Styles.FillMissing();
            return Table.GetValue(settings.Styles, s => new StyleWorkspace(s));
        }

        /// <summary>Writes the working copy into the settings, in place.</summary>
        public void WriteTo(FtfSettings settings)
        {
            var target = settings.Styles;
            var copy = Copy(Styles);
            copy.Prune();
            target.Text = copy.Text;
            target.Leaders = copy.Leaders;
            target.Assignments = copy.Assignments;
        }

        public void AnnounceNames() { var h = NamesChanged; if (h != null) h(); }

        private static StyleSettings Copy(StyleSettings s)
        {
            var copy = JsonConvert.DeserializeObject<StyleSettings>(JsonConvert.SerializeObject(s ?? new StyleSettings()));
            copy.FillMissing();
            return copy;
        }

        /// <summary>The settings to preview against: the page's working styles with the
        /// loaded Point Labels, Tags and Line Labels values beneath them.</summary>
        public FtfSettings PreviewSettings(FtfSettings loaded, StyleSettings styles = null)
        {
            var s = new FtfSettings
            {
                Labels = loaded.Labels,
                Tags = loaded.Tags,
                LineLabels = loaded.LineLabels,
                Styles = styles ?? Styles
            };
            return s;
        }
    }

    /// <summary>The drawing as the style check sees it, from the window's snapshot.</summary>
    internal sealed class SnapshotResources : IStyleResources
    {
        private readonly DrawingResources _d;
        public SnapshotResources(DrawingResources d) { _d = d; }
        private static bool Has(IList<string> list, string name)
        {
            return list != null && list.Any(n => string.Equals(n, (name ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase));
        }
        public bool HasTextStyle(string name) { return Has(_d.TextStyles, name); }
        public bool HasMLeaderStyle(string name) { return Has(_d.MLeaderStyles, name); }
        public bool HasBlock(string name) { return Has(_d.Blocks, name); }
        public bool HasLinetype(string name) { return Has(_d.Linetypes, name); }
    }

    /// <summary>Small helpers shared by the style pages.</summary>
    internal static class StyleUi
    {
        public const string Inherit = "(inherit)";
        public const string Any = "(any)";

        public static ComboBox Editable(IEnumerable<string> items, int width)
        {
            var c = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDown,
                Width = width,
                AutoCompleteMode = AutoCompleteMode.SuggestAppend,
                AutoCompleteSource = AutoCompleteSource.ListItems
            };
            c.Items.Add(string.Empty);
            foreach (var i in items ?? Enumerable.Empty<string>()) c.Items.Add(i);
            return c;
        }

        public static ComboBox Choices(IEnumerable<string> items, int width)
        {
            var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = width };
            foreach (var i in items) c.Items.Add(i);
            return c;
        }

        public static void Pick(ComboBox c, string value)
        {
            var v = value ?? string.Empty;
            for (var i = 0; i < c.Items.Count; i++)
                if (string.Equals(Convert.ToString(c.Items[i], CultureInfo.InvariantCulture), v, StringComparison.OrdinalIgnoreCase)) { c.SelectedIndex = i; return; }
            if (c.DropDownStyle == ComboBoxStyle.DropDownList) c.SelectedIndex = c.Items.Count > 0 ? 0 : -1;
            else c.Text = v;
        }

        public static string Text(ComboBox c)
        {
            var v = c.DropDownStyle == ComboBoxStyle.DropDownList ? Convert.ToString(c.SelectedItem, CultureInfo.InvariantCulture) : c.Text;
            v = (v ?? string.Empty).Trim();
            return v.Length == 0 || v == Inherit || v == Any ? null : v;
        }

        /// <summary>Blank: null. A number: it. Anything else: false and the caption names it.</summary>
        public static bool Number(TextBox box, out double? value)
        {
            value = null;
            var t = (box.Text ?? string.Empty).Trim();
            if (t.Length == 0) return true;
            double v;
            if (!double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return false;
            value = v;
            return true;
        }

        public static string Show(double? v) { return v.HasValue ? v.Value.ToString("0.####", CultureInfo.InvariantCulture) : string.Empty; }

        public static string[] EnumChoices<T>() where T : struct
        {
            return new[] { Inherit }.Concat(Enum.GetNames(typeof(T))).ToArray();
        }

        public static T? EnumOf<T>(ComboBox c) where T : struct
        {
            var t = Text(c);
            T v;
            return t != null && Enum.TryParse(t, out v) ? v : (T?)null;
        }

        public static readonly string[] YesNo = { Inherit, "Yes", "No" };

        public static bool? Bool(ComboBox c)
        {
            var t = Text(c);
            return t == "Yes" ? true : t == "No" ? (bool?)false : null;
        }

        public static string Bool(bool? b) { return b.HasValue ? (b.Value ? "Yes" : "No") : Inherit; }

        public static readonly string[] Colours = { "", "ByLayer", "ByBlock", "red", "yellow", "green", "cyan", "blue", "magenta", "white", "8", "9" };
        public static readonly string[] Lineweights =
        {
            "", "ByLayer", "ByBlock", "0.00", "0.05", "0.09", "0.13", "0.15", "0.18", "0.20", "0.25", "0.30", "0.35",
            "0.40", "0.50", "0.53", "0.60", "0.70", "0.80", "0.90", "1.00", "1.06", "1.20", "1.40", "1.58", "2.00", "2.11"
        };
    }

    /// <summary>
    /// Shared frame for the two named-style pages: the list of styles on the left with New,
    /// Copy and Delete; the fields and a live preview on the right. Edits land in the working
    /// copy at once and the preview follows; Save writes them.
    /// </summary>
    internal abstract class NamedStylePage : SetupPage
    {
        protected StyleWorkspace Workspace;
        protected FtfSettings Loaded;
        protected ListBox List;
        protected TableLayoutPanel Fields;
        protected StylePreview Preview;
        protected Label PreviewNote;
        protected Label Problems;
        protected TextBox NameBox;
        protected bool Loading;
        private int _row;

        protected abstract IList<string> Names();
        protected abstract void AddNew(string name, bool copySelected);
        protected abstract void Remove(string name);
        protected abstract void Rename(string from, string to);
        protected abstract void BuildFields();
        protected abstract void ShowSelected();
        protected abstract void ReadFields(ICollection<string> problems);
        protected abstract ResolvedAnnotationStyle PreviewStyle();
        protected abstract bool PreviewLeader { get; }
        protected abstract string Intro { get; }

        protected string Selected { get { return List.SelectedItem as string; } }

        protected override void BuildBody() { }

        protected override Control BuildFilled()
        {
            var split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1, Size = new Size(900, 600) };
            split.SplitterDistance = 220;
            split.Panel1MinSize = 160;

            var left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
            left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            left.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            left.Controls.Add(new Label { Text = Intro, AutoSize = true, MaximumSize = new Size(210, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(0, 0, 0, 6) }, 0, 0);
            List = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
            List.Tag = Ui.SettingsPanel.ViewOnly;
            left.Controls.Add(List, 0, 1);
            var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Dock = DockStyle.Fill };
            var add = new Button { Text = "New", AutoSize = true };
            var copy = new Button { Text = "Copy", AutoSize = true };
            var del = new Button { Text = "Delete", AutoSize = true };
            buttons.Controls.AddRange(new Control[] { add, copy, del });
            left.Controls.Add(buttons, 0, 2);
            split.Panel1.Controls.Add(left);

            var right = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(10, 0, 4, 0) };
            Fields = new TableLayoutPanel
            {
                Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2,
                GrowStyle = TableLayoutPanelGrowStyle.AddRows
            };
            Fields.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            Fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            NameBox = new TextBox { Width = 220 };
            Field("Name", NameBox, "What assignments and the Point Features screen call this style.");
            BuildFields();

            Preview = new StylePreview { Width = 440, Height = 190, Margin = new Padding(0, 10, 0, 2) };
            FullWidth(Preview);
            PreviewNote = new Label { AutoSize = true, MaximumSize = new Size(440, 0), ForeColor = SystemColors.GrayText };
            FullWidth(PreviewNote);
            Problems = new Label { AutoSize = true, MaximumSize = new Size(440, 0), ForeColor = Color.FromArgb(170, 0, 0), Margin = new Padding(0, 6, 0, 4) };
            FullWidth(Problems);
            right.Controls.Add(Fields);
            split.Panel2.Controls.Add(right);

            List.SelectedIndexChanged += (s, e) => { Loading = true; try { ShowSelected(); } finally { Loading = false; } UpdatePreview(); };
            add.Click += (s, e) => NewStyle(false);
            copy.Click += (s, e) => { if (Selected != null) NewStyle(true); };
            del.Click += (s, e) => DeleteSelected();
            NameBox.Leave += (s, e) => RenameSelected();
            NameBox.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) RenameSelected(); };
            return split;
        }

        protected void Field(string caption, Control c, string hint)
        {
            var l = new Label { Text = Escape(caption), AutoSize = true, Margin = new Padding(0, 6, 10, 3) };
            c.Margin = new Padding(0, 3, 0, 3);
            Fields.Controls.Add(l, 0, _row);
            Fields.Controls.Add(c, 1, _row);
            _row++;
            if (!string.IsNullOrEmpty(hint)) { Tip(c, hint); Tip(l, hint); }
            var box = c as TextBox;
            if (box != null && c != NameBox) box.TextChanged += (s, e) => Edited();
            var combo = c as ComboBox;
            if (combo != null) { combo.TextChanged += (s, e) => Edited(); combo.SelectedIndexChanged += (s, e) => Edited(); }
        }

        protected void Section(string text)
        {
            FullWidth(new Label { Text = text, Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold), AutoSize = true, Margin = new Padding(0, 10, 0, 2) });
        }

        protected void FullWidth(Control c)
        {
            Fields.Controls.Add(c, 0, _row);
            Fields.SetColumnSpan(c, 2);
            _row++;
        }

        private void Edited()
        {
            if (Loading || Selected == null) return;
            var problems = new List<string>();
            ReadFields(problems);
            Problems.Text = string.Join(Environment.NewLine, problems.ToArray());
            UpdatePreview();
            MarkChanged();
        }

        protected void UpdatePreview()
        {
            if (Preview == null) return;
            var style = PreviewStyle();
            Preview.ShowStyle(style, "18\" FIR\\PDBH 6.418", PreviewLeader, Context.Drawing.TextStyleFonts);
            PreviewNote.Text = style == null ? string.Empty
                : "Preview at plot scale with a sample label (\"18\" FIR\" over \"DBH 6.418\"). " + Preview.Caveat;
            CheckDrawing();
        }

        /// <summary>What this page's styles name that the drawing does not have.</summary>
        private void CheckDrawing()
        {
            var problems = new List<string>();
            Workspace.Styles.Validate(problems);
            var missing = StyleCheck.Missing(Workspace.PreviewSettings(Loaded), new SnapshotResources(Context.Drawing));
            var lines = problems.Concat(missing).Where(p => Selected == null || p.IndexOf("\"" + Selected + "\"", StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            if (Problems.Text.Length == 0 || lines.Count > 0) Problems.Text = string.Join(Environment.NewLine, lines.ToArray());
        }

        private void NewStyle(bool copySelected)
        {
            var baseName = copySelected ? Selected + " copy" : "New style";
            var name = baseName;
            for (var i = 2; Names().Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)); i++) name = baseName + " " + i;
            AddNew(name, copySelected);
            Workspace.AnnounceNames();
            RefreshList(name);
            MarkChanged();
        }

        private void DeleteSelected()
        {
            var name = Selected;
            if (name == null) return;
            var used = Workspace.Styles.Assignments.Count(a => string.Equals(a.TextStyle, name, StringComparison.OrdinalIgnoreCase) ||
                                                              string.Equals(a.LeaderStyle, name, StringComparison.OrdinalIgnoreCase));
            var question = used > 0
                ? "\"" + name + "\" is used by " + used + " assignment(s). Delete it? Those features go back to the values beneath it."
                : "Delete \"" + name + "\"?";
            if (MessageBox.Show(question, "Annotation styles", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            Remove(name);
            Workspace.AnnounceNames();
            RefreshList(null);
            MarkChanged();
        }

        private void RenameSelected()
        {
            var from = Selected;
            if (from == null || Loading) return;
            var to = (NameBox.Text ?? string.Empty).Trim();
            if (to.Length == 0 || string.Equals(from, to, StringComparison.Ordinal)) { NameBox.Text = from; return; }
            if (Names().Any(n => !string.Equals(n, from, StringComparison.OrdinalIgnoreCase) && string.Equals(n, to, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show("There is already a style named \"" + to + "\".", "Annotation styles", MessageBoxButtons.OK, MessageBoxIcon.Information);
                NameBox.Text = from;
                return;
            }
            Rename(from, to);
            Workspace.AnnounceNames();
            RefreshList(to);
            MarkChanged();
        }

        protected void RefreshList(string select)
        {
            Loading = true;
            try
            {
                List.Items.Clear();
                foreach (var n in Names()) List.Items.Add(n);
                if (select != null) List.SelectedItem = select;
                if (List.SelectedIndex < 0 && List.Items.Count > 0) List.SelectedIndex = 0;
                Fields.Enabled = List.SelectedIndex >= 0;
                ShowSelected();
            }
            finally { Loading = false; }
            UpdatePreview();
        }

        public override void LoadFrom(FtfSettings settings)
        {
            Loaded = settings;
            Workspace = StyleWorkspace.For(settings);
            RefreshList(Selected);
        }

        public override void SaveTo(FtfSettings settings, ICollection<string> problems)
        {
            if (Workspace == null) return;
            ReadFields(problems);
            Workspace.WriteTo(settings);
        }

        public override void RestoreDefaults(FtfSettings settings)
        {
            if (MessageBox.Show("Remove every named style on this page? Assignments that use them go back to the values beneath them.",
                                "Annotation styles", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            foreach (var n in Names().ToList()) Remove(n);
            Workspace.AnnounceNames();
            RefreshList(null);
            MarkChanged();
        }

        protected static void Unassign(StyleSettings s, string name, bool text)
        {
            foreach (var a in s.Assignments)
            {
                if (text && string.Equals(a.TextStyle, name, StringComparison.OrdinalIgnoreCase)) a.TextStyle = null;
                if (!text && string.Equals(a.LeaderStyle, name, StringComparison.OrdinalIgnoreCase)) a.LeaderStyle = null;
            }
        }

        protected static void Reassign(StyleSettings s, string from, string to, bool text)
        {
            foreach (var a in s.Assignments)
            {
                if (text && string.Equals(a.TextStyle, from, StringComparison.OrdinalIgnoreCase)) a.TextStyle = to;
                if (!text && string.Equals(a.LeaderStyle, from, StringComparison.OrdinalIgnoreCase)) a.LeaderStyle = to;
            }
        }
    }

    /// <summary>Settings > Text Styles: named text looks for labels, tags and line labels.</summary>
    internal sealed class TextStylesPage : NamedStylePage
    {
        private ComboBox _textStyle, _rotation, _justification, _colour, _mask, _maskLayer, _upper, _annotative;
        private TextBox _height, _width, _oblique, _rotationDegrees, _prefix, _suffix, _decimals;
        private Label _fixedHeight;

        public override string Title { get { return "Text Styles"; } }
        public override string AffectedCommands { get { return "FTFLABELS, FTFLABELALL, FTFTAGS, FTFLINELABELS, FTFRESTYLE"; } }
        protected override bool PreviewLeader { get { return true; } }
        protected override string Intro
        {
            get { return "Named text looks. A field left blank takes the value from Point Labels, Tags or Line Labels, so a style only says what makes it different. Use them in Style Assignments or on a Point Features screen."; }
        }

        protected override IList<string> Names() { return Workspace.Styles.Text.Select(t => t.Name ?? string.Empty).ToList(); }

        private TextStyleDef Def { get { return Workspace == null ? null : Workspace.Styles.FindText(Selected); } }

        protected override void BuildFields()
        {
            Section("Text");
            _textStyle = StyleUi.Editable(Context.Drawing.TextStyles, 220);
            Field("Drawing text style", _textStyle, "A text style in this drawing (STYLE). Blank: Point Labels' text style.");
            _fixedHeight = new Label { AutoSize = true, ForeColor = Color.FromArgb(170, 90, 0), MaximumSize = new Size(420, 0) };
            FullWidth(_fixedHeight);
            _height = new TextBox { Width = 80 };
            Field("Height (plotted, in)", _height, "Text height on the sheet; 0.08 is a common label height. Blank: inherited.");
            _width = new TextBox { Width = 80 };
            Field("Width factor", _width, "1 is the font's own width; 0.8 is narrower. Written as an MTEXT width code.");
            _oblique = new TextBox { Width = 80 };
            Field("Oblique angle (deg)", _oblique, "Slant; 0 is upright.");
            _rotation = StyleUi.Choices(StyleUi.EnumChoices<TextRotation>(), 140);
            Field("Rotation", _rotation, "Horizontal; a fixed angle; or follow the symbol's rotation (never upside down).");
            _rotationDegrees = new TextBox { Width = 80 };
            Field("Fixed angle (deg)", _rotationDegrees, "Counter-clockwise from east, for a Fixed rotation.");
            _justification = StyleUi.Choices(StyleUi.EnumChoices<TextJustification>(), 140);
            Field("Justification", _justification, "Auto lines the text up against the side its leader comes from.");
            _colour = StyleUi.Editable(StyleUi.Colours, 140);
            Field("Colour", _colour, "Blank or ByLayer: the layer's colour. A name or an AutoCAD colour number (1-255).");

            Section("Mask");
            _mask = StyleUi.Choices(StyleUi.EnumChoices<MaskMode>(), 140);
            Field("Mask", _mask, "Wipeout: a separate wipeout FTF keeps under the text. Background: the text's own background mask. None.");
            _maskLayer = StyleUi.Editable(Context.Drawing.Layers, 220);
            Field("Wipeout layer", _maskLayer, "Blank: the label's own layer (or Point Labels' mask layer).");

            Section("Wording");
            _prefix = new TextBox { Width = 140 };
            Field("Prefix", _prefix, "Put in front of the label. \\P starts a new line.");
            _suffix = new TextBox { Width = 140 };
            Field("Suffix", _suffix, "Put after the label, e.g. ' for feet.");
            _decimals = new TextBox { Width = 60 };
            Field("Decimal places", _decimals, "Rounds numbers written with a decimal point (6.418 to 6.42). Whole numbers -- pole and tag numbers -- are never changed. Blank: as written.");
            _upper = StyleUi.Choices(StyleUi.YesNo, 100);
            Field("Upper case", _upper, null);
            _annotative = StyleUi.Choices(StyleUi.YesNo, 100);
            Field("Annotative", _annotative, "Annotative text is sized per annotation scale by AutoCAD. Off: sized once for the drawing's scale.");
        }

        protected override void ShowSelected()
        {
            var d = Def;
            Fields.Enabled = d != null;
            if (d == null) return;
            NameBox.Text = d.Name;
            StyleUi.Pick(_textStyle, d.TextStyle);
            _height.Text = StyleUi.Show(d.HeightPlotted);
            _width.Text = StyleUi.Show(d.WidthFactor);
            _oblique.Text = StyleUi.Show(d.ObliqueDegrees);
            StyleUi.Pick(_rotation, d.Rotation.HasValue ? d.Rotation.Value.ToString() : StyleUi.Inherit);
            _rotationDegrees.Text = StyleUi.Show(d.RotationDegrees);
            StyleUi.Pick(_justification, d.Justification.HasValue ? d.Justification.Value.ToString() : StyleUi.Inherit);
            StyleUi.Pick(_colour, d.Color);
            StyleUi.Pick(_mask, d.Mask.HasValue ? d.Mask.Value.ToString() : StyleUi.Inherit);
            StyleUi.Pick(_maskLayer, d.MaskLayer);
            _prefix.Text = d.Prefix ?? string.Empty;
            _suffix.Text = d.Suffix ?? string.Empty;
            _decimals.Text = d.Decimals.HasValue ? d.Decimals.Value.ToString(CultureInfo.InvariantCulture) : string.Empty;
            StyleUi.Pick(_upper, StyleUi.Bool(d.UpperCase));
            StyleUi.Pick(_annotative, StyleUi.Bool(d.Annotative));
            ShowFixedHeight(d.TextStyle);
        }

        private void ShowFixedHeight(string style)
        {
            _fixedHeight.Text = !string.IsNullOrWhiteSpace(style) && Context.Drawing.FixedHeightTextStyles.Contains(style.Trim())
                ? "\"" + style.Trim() + "\" has a fixed height in this drawing: AutoCAD uses that height, not the one below."
                : string.Empty;
        }

        protected override void ReadFields(ICollection<string> problems)
        {
            var d = Def;
            if (d == null) return;
            var who = "Text style \"" + d.Name + "\"";
            d.TextStyle = StyleUi.Text(_textStyle);
            double? v;
            if (StyleUi.Number(_height, out v)) d.HeightPlotted = v; else problems.Add(who + ": height is not a number.");
            if (StyleUi.Number(_width, out v)) d.WidthFactor = v; else problems.Add(who + ": width factor is not a number.");
            if (StyleUi.Number(_oblique, out v)) d.ObliqueDegrees = v; else problems.Add(who + ": oblique angle is not a number.");
            d.Rotation = StyleUi.EnumOf<TextRotation>(_rotation);
            if (StyleUi.Number(_rotationDegrees, out v)) d.RotationDegrees = v; else problems.Add(who + ": fixed angle is not a number.");
            d.Justification = StyleUi.EnumOf<TextJustification>(_justification);
            d.Color = StyleUi.Text(_colour);
            d.Mask = StyleUi.EnumOf<MaskMode>(_mask);
            d.MaskLayer = StyleUi.Text(_maskLayer);
            d.Prefix = string.IsNullOrEmpty(_prefix.Text) ? null : _prefix.Text;
            d.Suffix = string.IsNullOrEmpty(_suffix.Text) ? null : _suffix.Text;
            int places;
            var dec = (_decimals.Text ?? string.Empty).Trim();
            if (dec.Length == 0) d.Decimals = null;
            else if (int.TryParse(dec, NumberStyles.Integer, CultureInfo.InvariantCulture, out places)) d.Decimals = places;
            else problems.Add(who + ": decimal places is not a whole number.");
            d.UpperCase = StyleUi.Bool(_upper);
            d.Annotative = StyleUi.Bool(_annotative);
            ShowFixedHeight(d.TextStyle);
        }

        protected override ResolvedAnnotationStyle PreviewStyle()
        {
            var d = Def;
            if (d == null) return null;
            var temp = new StyleSettings();
            temp.Text.Add(d.Clone());
            temp.Assignments.Add(new StyleAssignment { Kind = StyledKind.Label, TextStyle = d.Name });
            return StyleResolver.Resolve(Workspace.PreviewSettings(Loaded, temp),
                                         new StyleSubject { Kind = StyledKind.Label, Category = AnnotationCategory.Trees, BaseLeader = LeaderMode.Auto });
        }

        protected override void AddNew(string name, bool copySelected)
        {
            var d = copySelected && Def != null ? Def.Clone() : new TextStyleDef();
            d.Name = name;
            Workspace.Styles.Text.Add(d);
        }

        protected override void Remove(string name)
        {
            Workspace.Styles.Text.RemoveAll(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
            Unassign(Workspace.Styles, name, true);
        }

        protected override void Rename(string from, string to)
        {
            var d = Workspace.Styles.FindText(from);
            if (d == null) return;
            d.Name = to;
            Reassign(Workspace.Styles, from, to, true);
        }
    }

    /// <summary>Settings > Leader Styles: named multileader looks for labels and tags.</summary>
    internal sealed class LeaderStylesPage : NamedStylePage
    {
        private ComboBox _mleader, _arrow, _lineType, _colour, _linetype, _lineweight, _landing, _attachment, _annotative;
        private TextBox _arrowSize, _dogleg, _gap, _maxLength;

        public override string Title { get { return "Leader Styles"; } }
        public override string AffectedCommands { get { return "FTFLABELS, FTFLABELALL, FTFTAGS, FTFRESTYLE"; } }
        protected override bool PreviewLeader { get { return true; } }
        protected override string Intro
        {
            get
            {
                return "Named leader looks. FTF leaders are AutoCAD multileaders: every field is a real MLEADER property, and one left blank is decided by the multileader style. " +
                       "Civil 3D point label styles (on the COGO points themselves) are not multileaders -- edit those in Civil 3D's Settings tab.";
            }
        }

        protected override IList<string> Names() { return Workspace.Styles.Leaders.Select(t => t.Name ?? string.Empty).ToList(); }

        private LeaderStyleDef Def { get { return Workspace == null ? null : Workspace.Styles.FindLeader(Selected); } }

        protected override void BuildFields()
        {
            Section("Multileader");
            _mleader = StyleUi.Editable(Context.Drawing.MLeaderStyles, 220);
            Field("Multileader style", _mleader, "A multileader style in this drawing (MLEADERSTYLE). Blank: the drawing's current one.");
            _lineType = StyleUi.Choices(StyleUi.EnumChoices<LeaderLineKind>(), 140);
            Field("Leader line", _lineType, "Straight or spline.");
            _colour = StyleUi.Editable(StyleUi.Colours, 140);
            Field("Leader colour", _colour, "Blank: the multileader style's.");
            _linetype = StyleUi.Editable(new[] { "ByLayer", "ByBlock" }.Concat(Context.Drawing.Linetypes), 180);
            Field("Leader linetype", _linetype, "A linetype loaded in this drawing.");
            _lineweight = StyleUi.Editable(StyleUi.Lineweights, 100);
            Field("Leader lineweight", _lineweight, "Millimetres.");

            Section("Arrowhead");
            _arrow = StyleUi.Editable(StyleCheck.BuiltInArrows.Concat(Context.Drawing.Blocks), 180);
            Field("Arrowhead", _arrow, "AutoCAD's arrows (_Dot, _DotSmall, _Open30, _ArchTick, _None...) or a block in this drawing. Blank: the style's arrow.");
            _arrowSize = new TextBox { Width = 80 };
            Field("Arrow size (plotted, in)", _arrowSize, "Blank: Settings > Point Labels > Arrow size. 0: the multileader style's own size.");

            Section("Landing and text");
            _landing = StyleUi.Choices(StyleUi.YesNo, 100);
            Field("Landing", _landing, "The short horizontal dogleg into the text.");
            _dogleg = new TextBox { Width = 80 };
            Field("Landing length (plotted)", _dogleg, null);
            _gap = new TextBox { Width = 80 };
            Field("Gap to text (plotted)", _gap, "Space between the landing and the text.");
            _attachment = StyleUi.Choices(new[] { StyleUi.Inherit }.Concat(Enum.GetNames(typeof(LeaderAttachment))).ToArray(), 180);
            Field("Text attachment", _attachment, "Where the landing meets the text, as MLEADERSTYLE names it. FTF's default is the middle of the text.");
            _maxLength = new TextBox { Width = 80 };
            Field("Maximum length (plotted)", _maxLength, "The farthest a label may sit from its point; positions farther out are not tried. Blank: the label search rings decide.");
            _annotative = StyleUi.Choices(StyleUi.YesNo, 100);
            Field("Annotative", _annotative, null);
        }

        protected override void ShowSelected()
        {
            var d = Def;
            Fields.Enabled = d != null;
            if (d == null) return;
            NameBox.Text = d.Name;
            StyleUi.Pick(_mleader, d.MLeaderStyle);
            StyleUi.Pick(_lineType, d.LineType.HasValue ? d.LineType.Value.ToString() : StyleUi.Inherit);
            StyleUi.Pick(_colour, d.Color);
            StyleUi.Pick(_linetype, d.Linetype);
            StyleUi.Pick(_lineweight, d.Lineweight);
            StyleUi.Pick(_arrow, d.ArrowBlock);
            _arrowSize.Text = StyleUi.Show(d.ArrowSizePlotted);
            StyleUi.Pick(_landing, StyleUi.Bool(d.Landing));
            _dogleg.Text = StyleUi.Show(d.DoglegPlotted);
            _gap.Text = StyleUi.Show(d.LandingGapPlotted);
            StyleUi.Pick(_attachment, d.Attachment.HasValue ? d.Attachment.Value.ToString() : StyleUi.Inherit);
            _maxLength.Text = StyleUi.Show(d.MaxLengthPlotted);
            StyleUi.Pick(_annotative, StyleUi.Bool(d.Annotative));
        }

        protected override void ReadFields(ICollection<string> problems)
        {
            var d = Def;
            if (d == null) return;
            var who = "Leader style \"" + d.Name + "\"";
            d.MLeaderStyle = StyleUi.Text(_mleader);
            d.LineType = StyleUi.EnumOf<LeaderLineKind>(_lineType);
            d.Color = StyleUi.Text(_colour);
            d.Linetype = StyleUi.Text(_linetype);
            d.Lineweight = StyleUi.Text(_lineweight);
            d.ArrowBlock = StyleUi.Text(_arrow);
            double? v;
            if (StyleUi.Number(_arrowSize, out v)) d.ArrowSizePlotted = v; else problems.Add(who + ": arrow size is not a number.");
            d.Landing = StyleUi.Bool(_landing);
            if (StyleUi.Number(_dogleg, out v)) d.DoglegPlotted = v; else problems.Add(who + ": landing length is not a number.");
            if (StyleUi.Number(_gap, out v)) d.LandingGapPlotted = v; else problems.Add(who + ": gap is not a number.");
            d.Attachment = StyleUi.EnumOf<LeaderAttachment>(_attachment);
            if (StyleUi.Number(_maxLength, out v)) d.MaxLengthPlotted = v; else problems.Add(who + ": maximum length is not a number.");
            d.Annotative = StyleUi.Bool(_annotative);
        }

        protected override ResolvedAnnotationStyle PreviewStyle()
        {
            var d = Def;
            if (d == null) return null;
            var temp = new StyleSettings();
            temp.Leaders.Add(d.Clone());
            temp.Assignments.Add(new StyleAssignment { Kind = StyledKind.Label, LeaderStyle = d.Name });
            return StyleResolver.Resolve(Workspace.PreviewSettings(Loaded, temp),
                                         new StyleSubject { Kind = StyledKind.Label, Category = AnnotationCategory.Trees, BaseLeader = LeaderMode.Auto });
        }

        protected override void AddNew(string name, bool copySelected)
        {
            var d = copySelected && Def != null ? Def.Clone() : new LeaderStyleDef();
            d.Name = name;
            Workspace.Styles.Leaders.Add(d);
        }

        protected override void Remove(string name)
        {
            Workspace.Styles.Leaders.RemoveAll(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
            Unassign(Workspace.Styles, name, false);
        }

        protected override void Rename(string from, string to)
        {
            var d = Workspace.Styles.FindLeader(from);
            if (d == null) return;
            d.Name = to;
            Reassign(Workspace.Styles, from, to, false);
        }
    }

    /// <summary>
    /// Settings > Style Assignments: which text style, leader style, layer, offset and leader
    /// use each kind of annotation gets -- for every feature, a family, a rule or one field
    /// code -- and a tester that shows, for a description typed as the crew would, every
    /// value the label will get and where each one comes from.
    /// </summary>
    internal sealed class StyleAssignmentsPage : SetupPage
    {
        private StyleWorkspace _workspace;
        private FtfSettings _loaded;
        private DataGridView _grid;
        private ComboBox _testKind;
        private TextBox _testInput;
        private ListView _origins;
        private StylePreview _preview;
        private Label _status;
        private bool _loading;

        private static readonly string[] Kinds = { "Point label", "Tag", "Line label" };
        private static readonly string[] LeaderModes = { "", "Auto", "Always", "Never" };
        private static readonly string[] OnOff = { "", "Yes", "No" };

        public override string Title { get { return "Style Assignments"; } }
        public override string AffectedCommands { get { return "FTFLABELS, FTFLABELALL, FTFTAGS, FTFLINELABELS, FTFRESTYLE"; } }

        protected override void BuildBody() { }

        protected override Control BuildFilled()
        {
            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, Size = new Size(900, 640) };
            split.SplitterDistance = 300;
            split.Panel1MinSize = 160;

            var top = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
            top.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            top.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            top.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            top.Controls.Add(new Label
            {
                Text = "Each row gives one kind of annotation its look: for every feature (family and code blank), a family, a rule, or one field code. " +
                       "The most specific row wins field by field; a blank cell inherits. Feature rows are easiest to set on each Point Features screen.",
                AutoSize = true, MaximumSize = new Size(820, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(0, 0, 0, 6)
            }, 0, 0);

            _grid = new DataGridView
            {
                Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                EditMode = DataGridViewEditMode.EditOnEnter
            };
            _grid.DataError += (s, e) => { e.ThrowException = false; };
            _grid.CellValueChanged += (s, e) => { if (!_loading) { ReadGrid(null); RunTest(); MarkChanged(); } };
            _grid.CurrentCellDirtyStateChanged += (s, e) => { if (_grid.IsCurrentCellDirty && _grid.CurrentCell is DataGridViewComboBoxCell) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            top.Controls.Add(_grid, 0, 1);

            var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Dock = DockStyle.Fill };
            var add = new Button { Text = "Add Row", AutoSize = true };
            var del = new Button { Text = "Delete Row", AutoSize = true };
            var restyle = new Button { Text = "Update Existing Annotation...", AutoSize = true };
            Tip(restyle, "After saving: FTFRESTYLE previews, then updates the labels, tags and line labels FTF already drew. Nothing else in the drawing is touched.");
            add.Click += (s, e) => { _workspace.Styles.Assignments.Add(new StyleAssignment { Kind = StyledKind.Label }); FillGrid(); MarkChanged(); };
            del.Click += (s, e) => DeleteRow();
            restyle.Click += (s, e) => MessageBox.Show(
                "Save the settings, close this window, then run FTFRESTYLE. It lists what would change first (Preview) and changes nothing until you choose Apply. " +
                "Only FTF's own labels, tags and line labels are updated, in place: nothing is moved and wording someone edited is kept.",
                "Update existing annotation", MessageBoxButtons.OK, MessageBoxIcon.Information);
            buttons.Controls.AddRange(new Control[] { add, del, restyle });
            top.Controls.Add(buttons, 0, 2);
            split.Panel1.Controls.Add(top);

            var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3 };
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55f));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45f));
            bottom.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            bottom.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            bottom.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var testRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Dock = DockStyle.Fill };
            testRow.Controls.Add(new Label { Text = "Test:", AutoSize = true, Margin = new Padding(0, 6, 6, 0), Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold) });
            _testKind = StyleUi.Choices(Kinds, 100);
            _testKind.SelectedIndex = 0;
            _testKind.Tag = Ui.SettingsPanel.ViewOnly;
            testRow.Controls.Add(_testKind);
            _testInput = new TextBox { Width = 220, Text = "FH 12" };
            _testInput.Tag = Ui.SettingsPanel.ViewOnly;
            Tip(_testInput, "A description as the crew types it (point labels and tags), or a line code (line labels).");
            testRow.Controls.Add(_testInput);
            bottom.Controls.Add(testRow, 0, 0);
            bottom.SetColumnSpan(testRow, 2);

            _origins = new ListView { View = View.Details, FullRowSelect = true, Dock = DockStyle.Fill, HeaderStyle = ColumnHeaderStyle.Nonclickable };
            _origins.Columns.Add("Setting", 130);
            _origins.Columns.Add("Value", 130);
            _origins.Columns.Add("From", 260);
            bottom.Controls.Add(_origins, 0, 1);
            _preview = new StylePreview { Dock = DockStyle.Fill };
            bottom.Controls.Add(_preview, 1, 1);
            _status = new Label { AutoSize = true, MaximumSize = new Size(820, 0), Margin = new Padding(0, 6, 0, 0) };
            bottom.Controls.Add(_status, 0, 2);
            bottom.SetColumnSpan(_status, 2);
            split.Panel2.Controls.Add(bottom);

            _testKind.SelectedIndexChanged += (s, e) => RunTest();
            _testInput.TextChanged += (s, e) => RunTest();
            return split;
        }

        private void BuildColumns()
        {
            _grid.Columns.Clear();
            var families = new[] { "" }.Concat(AnnotationPolicy.All().Concat(new[] { AnnotationCategory.Lines }).Distinct().Select(c => c.ToString())).ToArray();
            var rules = new[] { "" }.Concat((Context.Rules != null ? Context.Rules.Codes : new List<CodeRule>()).Select(r => r.Id).Where(i => !string.IsNullOrEmpty(i)).OrderBy(i => i, StringComparer.OrdinalIgnoreCase)).ToArray();
            var texts = new[] { "" }.Concat(_workspace.Styles.Text.Select(t => t.Name)).ToArray();
            var leaders = new[] { "" }.Concat(_workspace.Styles.Leaders.Select(t => t.Name)).ToArray();
            _grid.Columns.Add(Combo("Kind", "For", Kinds, 90));
            _grid.Columns.Add(Combo("Family", "Family", families, 100));
            _grid.Columns.Add(Combo("Rule", "Rule", rules, 120));
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Code", HeaderText = "Code", FillWeight = 60 });
            _grid.Columns.Add(Combo("Text", "Text style", texts, 120));
            _grid.Columns.Add(Combo("Leader", "Leader style", leaders, 120));
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Layer", HeaderText = "Layer", FillWeight = 120 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Offset", HeaderText = "Offset (in)", FillWeight = 60 });
            _grid.Columns.Add(Combo("Mode", "Leader use", LeaderModes, 70));
            _grid.Columns.Add(Combo("On", "Labelled", OnOff, 60));
        }

        private static DataGridViewComboBoxColumn Combo(string name, string header, string[] items, int weight)
        {
            var c = new DataGridViewComboBoxColumn { Name = name, HeaderText = header, FillWeight = weight, FlatStyle = FlatStyle.Flat };
            c.Items.AddRange(items.Cast<object>().ToArray());
            return c;
        }

        private void FillGrid()
        {
            _loading = true;
            try
            {
                BuildColumns();
                _grid.Rows.Clear();
                foreach (var a in _workspace.Styles.Assignments)
                {
                    var i = _grid.Rows.Add(
                        Kinds[(int)a.Kind], Family(a.Category), a.Rule ?? "", a.Code ?? "", Known(a.TextStyle, _workspace.Styles.Text.Select(t => t.Name)),
                        Known(a.LeaderStyle, _workspace.Styles.Leaders.Select(t => t.Name)), a.Layer ?? "",
                        StyleUi.Show(a.OffsetPlotted), a.Leader.HasValue ? a.Leader.Value.ToString() : "",
                        a.Enabled.HasValue ? (a.Enabled.Value ? "Yes" : "No") : "");
                    _grid.Rows[i].Tag = a;
                }
            }
            finally { _loading = false; }
        }

        private static string Family(string category)
        {
            var c = StyleResolver.TryCategory(category);
            return c.HasValue ? c.Value.ToString() : "";
        }

        private static string Known(string name, IEnumerable<string> names)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            var match = names.FirstOrDefault(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
            return match ?? "";
        }

        private void ReadGrid(ICollection<string> problems)
        {
            foreach (DataGridViewRow row in _grid.Rows)
            {
                var a = row.Tag as StyleAssignment;
                if (a == null) continue;
                Func<string, string> cell = n => { var text = Convert.ToString(row.Cells[n].Value, CultureInfo.InvariantCulture); return string.IsNullOrWhiteSpace(text) ? null : text.Trim(); };
                var kind = Array.IndexOf(Kinds, cell("Kind") ?? Kinds[0]);
                a.Kind = (StyledKind)Math.Max(0, kind);
                a.Category = cell("Family");
                a.Rule = cell("Rule");
                a.Code = cell("Code");
                a.TextStyle = cell("Text");
                a.LeaderStyle = cell("Leader");
                a.Layer = cell("Layer");
                var offset = cell("Offset");
                double v;
                if (offset == null) a.OffsetPlotted = null;
                else if (double.TryParse(offset, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) a.OffsetPlotted = v;
                else if (problems != null) problems.Add("Style assignment (" + a.Scope + "): offset \"" + offset + "\" is not a number.");
                LeaderMode mode;
                a.Leader = cell("Mode") != null && Enum.TryParse(cell("Mode"), out mode) ? mode : (LeaderMode?)null;
                var on = cell("On");
                a.Enabled = on == "Yes" ? true : on == "No" ? (bool?)false : null;
            }
        }

        private void DeleteRow()
        {
            if (_grid.CurrentRow == null) return;
            var a = _grid.CurrentRow.Tag as StyleAssignment;
            if (a == null) return;
            _workspace.Styles.Assignments.Remove(a);
            FillGrid();
            RunTest();
            MarkChanged();
        }

        /// <summary>Resolves the test description and lists every value with its origin.</summary>
        private void RunTest()
        {
            if (_workspace == null || _origins == null) return;
            var kind = (StyledKind)Math.Max(0, _testKind.SelectedIndex);
            var input = (_testInput.Text ?? string.Empty).Trim();
            StyleSubject subject;
            string sample = input;
            double? symbol = null;
            if (kind == StyledKind.LineLabel || Context.Rules == null)
            {
                subject = new StyleSubject { Kind = kind, Category = kind == StyledKind.LineLabel ? AnnotationCategory.Lines : AnnotationCategory.OtherPoints, Code = FieldCodeParser.FirstToken(input) };
            }
            else
            {
                ParsedPoint parsed;
                try { parsed = new FieldCodeParser(Context.Rules).Parse("test", input); }
                catch (System.Exception) { parsed = null; }
                subject = parsed != null ? StyleSubject.For(kind, parsed, Context.Rules) : new StyleSubject { Kind = kind };
                if (parsed != null)
                {
                    sample = kind == StyledKind.Tag ? (parsed.TagPrefix ?? "T") + "12" : (parsed.LabelText ?? input);
                    symbol = parsed.MarkerRotationDegrees;
                }
            }

            var r = StyleResolver.Resolve(_workspace.PreviewSettings(_loaded), subject);
            _origins.BeginUpdate();
            _origins.Items.Clear();
            Action<string, string> row = (name, value) =>
                _origins.Items.Add(new ListViewItem(new[] { name, value, r.OriginOf(name) ?? "" }));
            row("Family", AnnotationPolicy.Name(subject.Category));
            row("Enabled", r.Enabled ? "yes" : "no -- not drawn");
            row("Layer", string.IsNullOrEmpty(r.Layer) ? "(rule's)" : r.Layer);
            row("Text style", Or(r.Text.DrawingTextStyle, "(current)"));
            row("Text height", r.Text.HeightPlotted.ToString("0.###", CultureInfo.InvariantCulture) + "\"");
            row("Width factor", r.Text.WidthFactor.ToString("0.###", CultureInfo.InvariantCulture));
            row("Rotation", r.Text.Rotation.ToString());
            row("Text colour", Or(r.Text.Color, "ByLayer"));
            row("Mask", r.Text.Mask.ToString());
            row("Prefix", r.Text.Prefix);
            row("Suffix", r.Text.Suffix);
            row("Decimals", r.Text.Decimals.HasValue ? r.Text.Decimals.Value.ToString(CultureInfo.InvariantCulture) : "as written");
            row("Offset", r.OffsetPlotted.ToString("0.###", CultureInfo.InvariantCulture) + "\"");
            if (kind != StyledKind.LineLabel)
            {
                row("Leader", r.LeaderMode.ToString());
                row("Multileader style", Or(r.Leader.MLeaderStyle, "(current)"));
                row("Arrowhead", StyleCheck.ArrowDisplayName(r.Leader.ArrowBlock));
                row("Arrow size", r.Leader.SizeArrow ? r.Leader.ArrowSizePlotted.ToString("0.###", CultureInfo.InvariantCulture) + "\"" : "(style's)");
                row("Leader line", r.Leader.LineType.HasValue ? r.Leader.LineType.Value.ToString() : "(style's)");
                row("Attachment", r.Leader.Attachment.ToString());
            }
            _origins.EndUpdate();

            _preview.ShowStyle(r, sample, kind != StyledKind.LineLabel && r.LeaderMode != LeaderMode.Never, Context.Drawing.TextStyleFonts, symbol);

            var problems = new List<string>(r.Problems);
            _workspace.Styles.Validate(problems);
            problems.AddRange(StyleCheck.Missing(_workspace.PreviewSettings(_loaded), new SnapshotResources(Context.Drawing)));
            _status.ForeColor = problems.Count == 0 ? Color.FromArgb(0, 110, 0) : Color.FromArgb(170, 0, 0);
            _status.Text = problems.Count == 0
                ? "Every style, text style, multileader style, arrow and linetype named here exists in this drawing."
                : string.Join(Environment.NewLine, problems.Distinct().Take(12).ToArray()) +
                  (problems.Count > 12 ? Environment.NewLine + "... and " + (problems.Count - 12) + " more." : string.Empty);
        }

        private static string Or(string v, string blank) { return string.IsNullOrWhiteSpace(v) ? blank : v; }

        public override void LoadFrom(FtfSettings settings)
        {
            _loaded = settings;
            if (_workspace != null) _workspace.NamesChanged -= OnNames;
            _workspace = StyleWorkspace.For(settings);
            _workspace.NamesChanged += OnNames;
            FillGrid();
            RunTest();
        }

        private void OnNames() { if (_grid != null) { ReadGrid(null); FillGrid(); RunTest(); } }

        public override void OnShown()
        {
            if (_workspace == null) return;
            FillGrid();
            RunTest();
        }

        public override void SaveTo(FtfSettings settings, ICollection<string> problems)
        {
            if (_workspace == null) return;
            ReadGrid(problems);
            _workspace.WriteTo(settings);
        }

        public override void RestoreDefaults(FtfSettings settings)
        {
            if (MessageBox.Show("Remove every style assignment? Features go back to Point Labels, Tags and Line Labels.",
                                "Style assignments", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            _workspace.Styles.Assignments.Clear();
            FillGrid();
            RunTest();
            MarkChanged();
        }
    }
}
