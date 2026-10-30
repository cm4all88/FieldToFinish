using System;
using System.Collections.Generic;
using System.Windows.Forms;
using FieldCodes.Settings;

namespace FieldCodes.Cad.Setup
{
    /// <summary>
    /// The Platted Blocks section: the standard FTFLOTS applies. Lot lines and the alley take the
    /// layer and annotation of the Drafting Lines types named here; setback, easement, lot-text,
    /// drainage and notes layers are set here, and the values the popup starts from.
    /// </summary>
    internal sealed class LotsPage : SetupPage
    {
        private TextBox _lotType;
        private TextBox _alleyType;
        private CheckBox _perimeter;
        private ComboBox _setback;
        private ComboBox _easement;
        private ComboBox _lotText;
        private ComboBox _drain;
        private ComboBox _notes;
        private TextBox _lotNumber;
        private TextBox _lotArea;
        private TextBox _lotAcres;
        private TextBox _blockTitle;
        private TextBox _alleyFormat;
        private TextBox _drainFormat;
        private TextBox _setbackNote;
        private TextBox _easementNote;
        private CheckBox _upper;
        private TextBox _text;
        private TextBox _textOffset;
        private TextBox _style;
        private TextBox _slope;
        private TextBox _alleyWidth;
        private TextBox _front, _side, _corner, _rear;
        private TextBox _frontEase, _sideEase, _rearEase;
        private TextBox _minArea;
        private TextBox _minWidth;

        public override string Title { get { return "Platted Blocks"; } }
        public override string AffectedCommands { get { return "FTFLOTS"; } }

        protected override void BuildBody()
        {
            Heading("Lines");
            Note("FTFLOTS divides a block into lots. Lot lines and the alley use the layer, linetype and bearing/distance " +
                 "format of the Drafting Lines types named here, so they always agree with FTFDRAWLINE.");
            _lotType = TextRow("Lot line type", "A type under Drafting Lines, normally Lot Line.", 200);
            _alleyType = TextRow("Alley type", "A type under Drafting Lines, normally Right of Way.", 200);
            _perimeter = CheckRow("Draw the block's own boundary again as lot lines",
                                  "Off: the boundary is the right-of-way drawing's (FTFROW). Its lot distances are still labelled.");

            Heading("Layers");
            Note("Layers are never created from a name here; a layer that is not set or not in the drawing is refused. " +
                 "Lot text, drainage and notes layers left blank use the lot lines' annotation layer.");
            var layers = new List<object> { string.Empty };
            foreach (var name in Context.Drawing.Layers) layers.Add(name);
            _setback = ComboRow("Building setback lines", layers, null);
            _easement = ComboRow("Utility easement lines", layers, null);
            _lotText = ComboRow("Lot numbers and areas", layers, null);
            _drain = ComboRow("Drainage arrows", layers, null);
            _notes = ComboRow("Plat notes and block title", layers, null);

            Heading("Wording");
            Note("Tokens: {num} {block} for a lot; {sf}, {ac} for its area (blank omits the line); {block} for the title; " +
                 "{width} for the alley; {pct} for the slope.");
            _lotNumber = TextRow("Lot number", null, 260);
            _lotArea = TextRow("Lot area", null, 260);
            _lotAcres = TextRow("Lot acres", null, 260);
            _blockTitle = TextRow("Block title", null, 260);
            _alleyFormat = TextRow("Alley", null, 260);
            _drainFormat = TextRow("Drainage arrow", null, 260);
            _setbackNote = TextRow("Setback note starts", null, 260);
            _easementNote = TextRow("Easement note starts", null, 260);
            _upper = CheckRow("Set all notes in capitals", null);
            _text = TextRow("Text height (plotted)", null);
            _textOffset = TextRow("Text gap (plotted)", null);
            _style = TextRow("Text style", "Blank uses the lot line type's style, else the drawing's current style.", 200);

            Heading("What the popup starts from");
            Note("These are the office's numbers, not any jurisdiction's. Zero draws nothing for a setback or easement.");
            _slope = TextRow("Drainage slope (%)", null);
            _alleyWidth = TextRow("Alley width (feet)", null);
            _front = TextRow("Front setback (feet)", null);
            _side = TextRow("Side setback (feet)", null);
            _corner = TextRow("Corner lot street-side setback (feet)", null);
            _rear = TextRow("Rear setback (feet)", null);
            _frontEase = TextRow("Front easement (feet)", null);
            _sideEase = TextRow("Side easement (feet)", null);
            _rearEase = TextRow("Rear easement (feet)", null);
            _minArea = TextRow("Minimum lot area (sq ft)", "0 omits the check.");
            _minWidth = TextRow("Minimum frontage (feet)", "0 omits the check.");
        }

        public override void LoadFrom(FtfSettings settings)
        {
            var s = settings.Lots;
            _lotType.Text = s.LotLineTypeName ?? string.Empty;
            _alleyType.Text = s.AlleyTypeName ?? string.Empty;
            _perimeter.Checked = s.DrawBlockPerimeter;
            ComboHelp.SelectOrAdd(_setback, s.SetbackLayer, true);
            ComboHelp.SelectOrAdd(_easement, s.EasementLayer, true);
            ComboHelp.SelectOrAdd(_lotText, s.LotTextLayer, true);
            ComboHelp.SelectOrAdd(_drain, s.DrainLayer, true);
            ComboHelp.SelectOrAdd(_notes, s.NotesLayer, true);
            _lotNumber.Text = s.LotNumberFormat ?? string.Empty;
            _lotArea.Text = s.LotAreaFormat ?? string.Empty;
            _lotAcres.Text = s.LotAcresFormat ?? string.Empty;
            _blockTitle.Text = s.BlockTitleFormat ?? string.Empty;
            _alleyFormat.Text = s.AlleyFormat ?? string.Empty;
            _drainFormat.Text = s.DrainFormat ?? string.Empty;
            _setbackNote.Text = s.SetbackNotePrefix ?? string.Empty;
            _easementNote.Text = s.EasementNotePrefix ?? string.Empty;
            _upper.Checked = s.UpperCase;
            _text.Text = Fmt(s.TextHeightPlotted);
            _textOffset.Text = Fmt(s.TextOffsetPlotted);
            _style.Text = s.TextStyle ?? string.Empty;
            _slope.Text = Fmt(s.DefaultSlopePercent);
            _alleyWidth.Text = Fmt(s.DefaultAlleyWidthFeet);
            _front.Text = Fmt(s.DefaultFrontSetbackFeet);
            _side.Text = Fmt(s.DefaultSideSetbackFeet);
            _corner.Text = Fmt(s.DefaultCornerSideSetbackFeet);
            _rear.Text = Fmt(s.DefaultRearSetbackFeet);
            _frontEase.Text = Fmt(s.DefaultFrontEasementFeet);
            _sideEase.Text = Fmt(s.DefaultSideEasementFeet);
            _rearEase.Text = Fmt(s.DefaultRearEasementFeet);
            _minArea.Text = Fmt(s.MinimumLotAreaSqFt);
            _minWidth.Text = Fmt(s.MinimumLotWidthFeet);
        }

        public override void SaveTo(FtfSettings settings, ICollection<string> problems)
        {
            var s = settings.Lots;
            s.LotLineTypeName = _lotType.Text.Trim();
            s.AlleyTypeName = _alleyType.Text.Trim();
            s.DrawBlockPerimeter = _perimeter.Checked;
            s.SetbackLayer = Convert.ToString(_setback.SelectedItem) ?? string.Empty;
            s.EasementLayer = Convert.ToString(_easement.SelectedItem) ?? string.Empty;
            s.LotTextLayer = Convert.ToString(_lotText.SelectedItem) ?? string.Empty;
            s.DrainLayer = Convert.ToString(_drain.SelectedItem) ?? string.Empty;
            s.NotesLayer = Convert.ToString(_notes.SelectedItem) ?? string.Empty;
            s.LotNumberFormat = _lotNumber.Text.Trim();
            s.LotAreaFormat = _lotArea.Text.Trim();
            s.LotAcresFormat = _lotAcres.Text.Trim();
            s.BlockTitleFormat = _blockTitle.Text.Trim();
            s.AlleyFormat = _alleyFormat.Text.Trim();
            s.DrainFormat = _drainFormat.Text.Trim();
            s.SetbackNotePrefix = _setbackNote.Text.Trim();
            s.EasementNotePrefix = _easementNote.Text.Trim();
            s.UpperCase = _upper.Checked;
            s.TextStyle = _style.Text.Trim();

            var v = s.TextHeightPlotted;
            ReadDouble(_text, "Platted blocks: text height", x => x > 0, "must be greater than zero", problems, ref v);
            s.TextHeightPlotted = v;
            v = s.TextOffsetPlotted;
            ReadDouble(_textOffset, "Platted blocks: text gap", x => x >= 0, "cannot be negative", problems, ref v);
            s.TextOffsetPlotted = v;
            v = s.DefaultSlopePercent;
            ReadDouble(_slope, "Platted blocks: slope", x => x >= 0, "cannot be negative", problems, ref v);
            s.DefaultSlopePercent = v;
            v = s.DefaultAlleyWidthFeet;
            ReadDouble(_alleyWidth, "Platted blocks: alley width", x => x >= 0, "cannot be negative", problems, ref v);
            s.DefaultAlleyWidthFeet = v;
            v = s.DefaultFrontSetbackFeet;
            ReadDouble(_front, "Platted blocks: front setback", x => x >= 0, "cannot be negative", problems, ref v);
            s.DefaultFrontSetbackFeet = v;
            v = s.DefaultSideSetbackFeet;
            ReadDouble(_side, "Platted blocks: side setback", x => x >= 0, "cannot be negative", problems, ref v);
            s.DefaultSideSetbackFeet = v;
            v = s.DefaultCornerSideSetbackFeet;
            ReadDouble(_corner, "Platted blocks: corner side setback", x => x >= 0, "cannot be negative", problems, ref v);
            s.DefaultCornerSideSetbackFeet = v;
            v = s.DefaultRearSetbackFeet;
            ReadDouble(_rear, "Platted blocks: rear setback", x => x >= 0, "cannot be negative", problems, ref v);
            s.DefaultRearSetbackFeet = v;
            v = s.DefaultFrontEasementFeet;
            ReadDouble(_frontEase, "Platted blocks: front easement", x => x >= 0, "cannot be negative", problems, ref v);
            s.DefaultFrontEasementFeet = v;
            v = s.DefaultSideEasementFeet;
            ReadDouble(_sideEase, "Platted blocks: side easement", x => x >= 0, "cannot be negative", problems, ref v);
            s.DefaultSideEasementFeet = v;
            v = s.DefaultRearEasementFeet;
            ReadDouble(_rearEase, "Platted blocks: rear easement", x => x >= 0, "cannot be negative", problems, ref v);
            s.DefaultRearEasementFeet = v;
            v = s.MinimumLotAreaSqFt;
            ReadDouble(_minArea, "Platted blocks: minimum lot area", x => x >= 0, "cannot be negative", problems, ref v);
            s.MinimumLotAreaSqFt = v;
            v = s.MinimumLotWidthFeet;
            ReadDouble(_minWidth, "Platted blocks: minimum frontage", x => x >= 0, "cannot be negative", problems, ref v);
            s.MinimumLotWidthFeet = v;

            var own = new List<string>();
            s.Validate(own);
            foreach (var message in own) problems.Add(message);
        }

        public override void RestoreDefaults(FtfSettings settings)
        {
            settings.Lots.RestoreDefaults();
            LoadFrom(settings);
        }
    }
}
