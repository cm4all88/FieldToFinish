using System.Collections.Generic;
using System.Windows.Forms;
using FieldCodes.Drafting;
using FieldCodes.Editing;
using FieldCodes.Settings;

namespace FieldCodes.Cad.Setup
{
    /// <summary>
    /// Settings > Survey Drafting > Benches: FTFBENCH's settings, the rules file's bench section.
    /// The outline layer, the size label's wording (with buttons that put in the length, depth or
    /// height, and a preview of exactly what FTFBENCH writes) and layer, and the codes a bench is
    /// shot with. The label's text height, style and placement are the Line Labels page's, linked
    /// from here. Loading, staging and saving are RulesSectionPage's.
    ///
    /// UNTESTED as UI; the document editing, checks and preview are unit tested
    /// (BenchSettingsEditingTests).
    /// </summary>
    [SettingsPage(Group = "Survey Drafting", Label = "Benches", Order = 32, Commands = "FTFBENCH")]
    internal sealed class BenchPage : RulesSectionPage
    {
        private ComboBox _layer;
        private ComboBox _labelLayer;
        private TextBox _format;
        private Label _preview;
        private TextBox _codes;

        public override string Title { get { return "Benches"; } }
        public override string AffectedCommands { get { return "FTFBENCH"; } }
        protected override string SectionKey { get { return "bench"; } }
        protected override string WhatChanges { get { return "bench"; } }
        protected override string SaveText { get { return "Save Bench Settings"; } }
        protected override string UsedBy { get { return "FTFBENCH"; } }

        protected override void BuildRows()
        {
            Note("FTFBENCH draws a bench from its two end shots: the outline (the line between the shots, widened by the depth) " +
                 "and a size label. The depth and height come from the shots' notes (D1.5, 18IN D, H 1.5); anything the notes " +
                 "do not say, it asks for.");

            Heading("Outline");
            _layer = LayerRow("Outline layer", "The layer the bench outline goes on. PMX: V-SURF-LAND-E.", PmxLayers.BenchLayer);

            Heading("Size label");
            _format = TextRow("Label wording",
                "What the label says. {length}, {depth} and {height} are in feet; :0.# after the name sets the decimals. " +
                "A part in [ ] shows only when its values are known; \\P starts a new line.", 300);
            var insert = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = new Padding(0, 0, 0, 2) };
            insert.Controls.Add(new Label { Text = "Put in:", AutoSize = true, Margin = new Padding(0, 7, 4, 0) });
            insert.Controls.Add(InsertButton("Length", "{length:0.#}'", "The length between the two end shots, in feet."));
            insert.Controls.Add(InsertButton("Depth", "{depth:0.#}'", "The depth from the notes (or typed), in feet."));
            insert.Controls.Add(InsertButton("Height", "[x{height:0.#}']", "x and the height, shown only when the notes give one (or it is typed): BENCH 6'x1.5'x1.5'."));
            insert.Controls.Add(InsertButton("New Line", "\\P", "Starts a second line of the label."));
            AddFullWidth(insert);
            _preview = LiveNote();
            _labelLayer = LayerRow("Label layer", "The layer the size label goes on. PMX: V-SURF-LAND-TEXT-E.", PmxLayers.BenchLabelLayer);
            var link = new LinkLabel { Text = "Text height, style and placement: Line Labels", AutoSize = true, Margin = new Padding(0, 4, 0, 6) };
            link.LinkClicked += (s, e) => { if (Context.Navigate != null) Context.Navigate("Line Labels"); };
            Tip(link, "The bench label is drawn like a line label: its text height, text style, mask and whether it turns with the bench are set there.");
            AddFullWidth(link);

            Heading("Bench codes");
            _codes = TextRow("Codes a bench end is shot with",
                "Separate codes with commas. FTFBENCH draws any two points you pick; these codes only let it tell you when a picked point is coded as something else.", 300);
            Note("Example: BENCH, BNCH, BENCHL. A figure number after the code (BENCHL2) is fine.");

            _format.TextChanged += (s, e) => Stage();
            _codes.TextChanged += (s, e) => Stage();
        }

        private Button InsertButton(string text, string field, string hint)
        {
            var b = new Button { Text = text, AutoSize = true, Margin = new Padding(0, 0, 4, 0) };
            Tip(b, hint);
            b.Click += (s, e) =>
            {
                _format.SelectedText = field;
                _format.Focus();
            };
            return b;
        }

        protected override bool ShowSection(RuleDocument document)
        {
            var values = document != null ? document.GetBench() : EditableBench.Defaults();
            Show(values);
            return values.InFile;
        }

        protected override void ShowBuiltIn()
        {
            Show(EditableBench.Defaults());
        }

        protected override void ApplyControls(RuleDocument document, ICollection<string> problems)
        {
            if (document != null) document.ApplyBench(Read());
        }

        protected override void CopySection(RuleDocument from, RuleDocument to)
        {
            from.CopyBenchTo(to);
        }

        protected override void Edited()
        {
            var format = _format.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(format))
            {
                _preview.ForeColor = Pending;
                _preview.Text = "The label wording is empty.";
                return;
            }
            _preview.ForeColor = System.Drawing.SystemColors.GrayText;
            var shown = BenchPlanner.Preview(format);
            _preview.Text = "A bench 6 ft long, 1.5 ft deep and 1.5 ft high is labelled:  " + (shown.Length > 0 ? shown : "(nothing)") +
                            (BenchPlanner.UsesHeight(format) ? "   (FTFBENCH asks for the height when the notes do not give one.)" : string.Empty);
        }

        private void Show(EditableBench values)
        {
            _layer.Text = values.Layer ?? string.Empty;
            _labelLayer.Text = values.LabelLayer ?? string.Empty;
            _format.Text = values.LabelFormat ?? string.Empty;
            _codes.Text = string.Join(", ", (values.Codes ?? new List<string>()).ToArray());
        }

        private EditableBench Read()
        {
            return new EditableBench
            {
                Layer = (_layer.Text ?? string.Empty).Trim(),
                LabelLayer = (_labelLayer.Text ?? string.Empty).Trim(),
                LabelFormat = _format.Text ?? string.Empty,
                Codes = EditableBench.SplitCodes(_codes.Text)
            };
        }
    }
}
