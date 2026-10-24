using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Forms;
using FieldCodes.Settings;

namespace FieldCodes.Cad.Setup
{
    /// <summary>
    /// The Right of Way section: the standard FTFROW applies. The right-of-way lines
    /// themselves take the layer and annotation of a Drafting Lines type, named here;
    /// everything else FTFROW draws has its layer and wording set on this page.
    /// </summary>
    internal sealed class RightOfWayPage : SetupPage
    {
        private TextBox _lineType;
        private ComboBox _monumentLine;
        private ComboBox _centerline;
        private ComboBox _monument;
        private ComboBox _monumentText;
        private ComboBox _dimension;
        private ComboBox _roadName;
        private TextBox _found;
        private TextBox _set;
        private TextBox _offset;
        private TextBox _dimensionFormat;
        private TextBox _roadFormat;
        private CheckBox _upper;
        private CheckBox _picked;
        private CheckBox _drawCenter;
        private CheckBox _labelOpposite;
        private CheckBox _labelCenter;
        private ComboBox _policy;
        private TextBox _symbol;
        private TextBox _text;
        private TextBox _textOffset;
        private TextBox _style;
        private TextBox _cornerRadius;
        private TextBox _tolerance;

        private static readonly object[] Policies =
        {
            RightOfWaySettings.DimensionsEnds, RightOfWaySettings.DimensionsEvery
        };

        public override string Title { get { return "Right of Way"; } }
        public override string AffectedCommands { get { return "FTFROW"; } }

        protected override void BuildBody()
        {
            Heading("Lines");
            Note("FTFROW draws right-of-way lines from picked monuments. They use the layer, " +
                 "linetype and bearing/distance format of the Drafting Lines type named here, " +
                 "so a line drawn by FTFROW and one drawn by FTFDRAWLINE always agree.");
            _lineType = TextRow("Drafting Lines type", "The name of a type under Drafting Lines, normally Right of Way.", 200);
            _picked = CheckRow("Draw the line through the picked monuments", null);
            _labelOpposite = CheckRow("Label the opposite right-of-way line", null);
            _drawCenter = CheckRow("Draw the road centerline", null);
            _labelCenter = CheckRow("Label the centerline", null);

            Heading("Layers");
            Note("Layers are never created from a name here. Pick the office layers from this drawing; " +
                 "FTFROW refuses to draw on a layer that is not set or not in the drawing. " +
                 "Text layers left blank use the right-of-way line's annotation layer.");
            var layers = new List<object> { string.Empty };
            foreach (var name in Context.Drawing.Layers) layers.Add(name);
            _monumentLine = ComboRow("Monument line (the line through the picked monuments)", layers, null);
            _centerline = ComboRow("Road centerline", layers, null);
            _monument = ComboRow("Monument symbols, offset ties and dimensions", layers, null);
            _dimension = ComboRow("Right-of-way dimensions", layers, null);
            _monumentText = ComboRow("Monument notes", layers, null);
            _roadName = ComboRow("Road names", layers, null);

            Heading("Wording");
            Note("Tokens: {desc} {pt} for monuments; {dist} {dir} {fromdir} for an offset note ({dir} reads from the " +
                 "monument to the corner); {width} for the dimension; {name} for the road.");
            _found = TextRow("Found monument", null, 260);
            _set = TextRow("Set monument", null, 260);
            _offset = TextRow("Offset monument second line", null, 260);
            _dimensionFormat = TextRow("Right-of-way dimension", null, 260);
            _roadFormat = TextRow("Road name", null, 260);
            _upper = CheckRow("Set all notes in capitals", null);

            Heading("Dimensions and symbols");
            _policy = ComboRow("Dimension the width at", Policies, "Ends: first and last monument of a run. Every: each monument where it fits.");
            _symbol = TextRow("Monument symbol radius (plotted)", null);
            _text = TextRow("Text height (plotted)", null);
            _textOffset = TextRow("Text gap (plotted)", null);
            _style = TextRow("Text style", "Blank uses the drawing's current text style.", 200);
            _cornerRadius = TextRow("Default block corner radius (feet)", "0 leaves block corners sharp.");
            _tolerance = TextRow("Cross-street width tolerance (feet)", "A measured width further from the record width than this is flagged.");
        }

        public override void LoadFrom(FtfSettings settings)
        {
            var s = settings.RightOfWay;
            _lineType.Text = s.LineTypeName ?? string.Empty;
            _picked.Checked = s.DrawPickedLine;
            _labelOpposite.Checked = s.LabelOppositeLine;
            _drawCenter.Checked = s.DrawCenterline;
            _labelCenter.Checked = s.LabelCenterline;
            ComboHelp.SelectOrAdd(_monumentLine, s.MonumentLineLayer, true);
            ComboHelp.SelectOrAdd(_centerline, s.CenterlineLayer, true);
            ComboHelp.SelectOrAdd(_monument, s.MonumentLayer, true);
            ComboHelp.SelectOrAdd(_dimension, s.DimensionLayer, true);
            ComboHelp.SelectOrAdd(_monumentText, s.MonumentTextLayer, true);
            ComboHelp.SelectOrAdd(_roadName, s.RoadNameLayer, true);
            _found.Text = s.FoundFormat ?? string.Empty;
            _set.Text = s.SetFormat ?? string.Empty;
            _offset.Text = s.OffsetFormat ?? string.Empty;
            _dimensionFormat.Text = s.DimensionFormat ?? string.Empty;
            _roadFormat.Text = s.RoadNameFormat ?? string.Empty;
            _upper.Checked = s.UpperCase;
            _policy.SelectedItem = s.DimensionEveryMonument ? RightOfWaySettings.DimensionsEvery : RightOfWaySettings.DimensionsEnds;
            _symbol.Text = Fmt(s.MonumentRadiusPlotted);
            _text.Text = Fmt(s.TextHeightPlotted);
            _textOffset.Text = Fmt(s.TextOffsetPlotted);
            _style.Text = s.TextStyle ?? string.Empty;
            _cornerRadius.Text = Fmt(s.DefaultCornerRadiusFeet);
            _tolerance.Text = Fmt(s.WidthToleranceFeet);
        }

        public override void SaveTo(FtfSettings settings, ICollection<string> problems)
        {
            var s = settings.RightOfWay;
            s.LineTypeName = _lineType.Text.Trim();
            s.DrawPickedLine = _picked.Checked;
            s.LabelOppositeLine = _labelOpposite.Checked;
            s.DrawCenterline = _drawCenter.Checked;
            s.LabelCenterline = _labelCenter.Checked;
            s.MonumentLineLayer = Convert.ToString(_monumentLine.SelectedItem) ?? string.Empty;
            s.CenterlineLayer = Convert.ToString(_centerline.SelectedItem) ?? string.Empty;
            s.MonumentLayer = Convert.ToString(_monument.SelectedItem) ?? string.Empty;
            s.DimensionLayer = Convert.ToString(_dimension.SelectedItem) ?? string.Empty;
            s.MonumentTextLayer = Convert.ToString(_monumentText.SelectedItem) ?? string.Empty;
            s.RoadNameLayer = Convert.ToString(_roadName.SelectedItem) ?? string.Empty;
            s.FoundFormat = _found.Text.Trim();
            s.SetFormat = _set.Text.Trim();
            s.OffsetFormat = _offset.Text.Trim();
            s.DimensionFormat = _dimensionFormat.Text.Trim();
            s.RoadNameFormat = _roadFormat.Text.Trim();
            s.UpperCase = _upper.Checked;
            s.DimensionPolicy = Convert.ToString(_policy.SelectedItem) ?? RightOfWaySettings.DimensionsEnds;
            s.TextStyle = _style.Text.Trim();

            var v = s.MonumentRadiusPlotted;
            ReadDouble(_symbol, "Right of way: monument symbol radius", x => x > 0, "must be greater than zero", problems, ref v);
            s.MonumentRadiusPlotted = v;
            v = s.TextHeightPlotted;
            ReadDouble(_text, "Right of way: text height", x => x > 0, "must be greater than zero", problems, ref v);
            s.TextHeightPlotted = v;
            v = s.TextOffsetPlotted;
            ReadDouble(_textOffset, "Right of way: text gap", x => x >= 0, "cannot be negative", problems, ref v);
            s.TextOffsetPlotted = v;
            v = s.DefaultCornerRadiusFeet;
            ReadDouble(_cornerRadius, "Right of way: default corner radius", x => x >= 0, "cannot be negative", problems, ref v);
            s.DefaultCornerRadiusFeet = v;
            v = s.WidthToleranceFeet;
            ReadDouble(_tolerance, "Right of way: width tolerance", x => x >= 0, "cannot be negative", problems, ref v);
            s.WidthToleranceFeet = v;

            var own = new List<string>();
            s.Validate(own);
            foreach (var message in own) problems.Add(message);
        }

        public override void RestoreDefaults(FtfSettings settings)
        {
            settings.RightOfWay.RestoreDefaults();
            LoadFrom(settings);
        }
    }
}
