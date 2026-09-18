using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using FieldCodes.Settings;

namespace FieldCodes.Cad.Setup
{
    /// <summary>How FTFEXHIBIT lays out an easement exhibit, saved with the drafting profile.</summary>
    internal sealed class ExhibitsPage : SetupPage
    {
        private readonly Dictionary<string, TextBox> _numbers = new Dictionary<string, TextBox>();
        private readonly Dictionary<string, TextBox> _texts = new Dictionary<string, TextBox>();
        private readonly Dictionary<string, CheckBox> _checks = new Dictionary<string, CheckBox>();
        private ComboBox _orientation, _areaTable, _narrow, _power, _hatches, _stamp, _headings;
        private readonly Dictionary<string, TextBox> _officeTexts = new Dictionary<string, TextBox>();
        private readonly Dictionary<string, TextBox> _officeNumbers = new Dictionary<string, TextBox>();
        private CheckBox _areaLabelTitle, _stripAlong;

        // The office's own exhibit standard: its drawings, blocks, styles and what the viewport shows.
        private static readonly List<Tuple<string, string, string, Func<ExhibitSettings, string>, Action<ExhibitSettings, string>>> OfficeTexts =
            new List<Tuple<string, string, string, Func<ExhibitSettings, string>, Action<ExhibitSettings, string>>>
        {
            T("ctb", "Plot style table", "e.g. PMX Survey BW.ctb -- set on the exhibit layout and used by the PDF preview.", x => x.PlotStyleTable, (x, v) => x.PlotStyleTable = v),
            T("templateFile", "Template drawing", "The .dwt or .dwg the template layout comes from when this drawing does not have it.", x => x.TemplateFile, (x, v) => x.TemplateFile = v),
            T("library", "Block library drawing", "Logo, stamp, scale bar and title block definitions are brought in from here when the drawing lacks them.", x => x.BlockLibrary, (x, v) => x.BlockLibrary = v),
            T("tbName", "Title block (block name)", "A block in the drawing or the library, inserted at the title block position.", x => x.TitleBlockName, (x, v) => x.TitleBlockName = v),
            T("tbAtts", "Title block attributes", "TAG={field}; e.g. JobNo={projectNumber}; SheetNo={sheetNo}; CheckedBy={checkedBy}. Others are left alone and listed.", x => x.TitleBlockAttributes, (x, v) => x.TitleBlockAttributes = v),
            T("sheetBlocks", "Sheet blocks", "NAME@X,Y or NAME@X,Y@LAYER separated by ; -- e.g. a logo and a stamp placeholder.", x => x.SheetBlocks, (x, v) => x.SheetBlocks = v),
            T("titleStyle", "Title text style", null, x => x.TitleTextStyle, (x, v) => x.TitleTextStyle = v),
            T("titleLayer", "Title layer", null, x => x.TitleLayer, (x, v) => x.TitleLayer = v),
            T("barBlock", "Scale bar block", "An office scale bar block used instead of the drawn one.", x => x.ScaleBarBlock, (x, v) => x.ScaleBarBlock = v),
            T("barState", "Scale bar visibility state", "The dynamic block state for the scale, e.g. 1\" = {scale}'", x => x.ScaleBarVisibility, (x, v) => x.ScaleBarVisibility = v),
            T("leaderStyle", "Leader style", "Multileader style for POC / POB / terminus leaders.", x => x.LeaderStyle, (x, v) => x.LeaderStyle = v),
            T("legendFormat", "Legend entry", "{name} and {sqft}, e.g. {name} ({sqft} SQ. FT.)", x => x.LegendFormat, (x, v) => x.LegendFormat = v),
            T("lineTitle", "Line table title", null, x => x.LineTableTitle, (x, v) => x.LineTableTitle = v),
            T("lineCols", "Line table columns", "HEADER={token}|...: {id} {distance} {bearing}", x => x.LineTableColumns, (x, v) => x.LineTableColumns = v),
            T("curveTitle", "Curve table title", null, x => x.CurveTableTitle, (x, v) => x.CurveTableTitle = v),
            T("curveCols", "Curve table columns", "HEADER={token}|...: {id} {radius} {length} {delta}", x => x.CurveTableColumns, (x, v) => x.CurveTableColumns = v),
            T("vpRules", "Viewport layers", "LAYER=Show|Hide|Relevant|User; wildcards allowed. In this viewport only -- model space is never changed. Relevant shows a layer only where it holds the exhibit's easements or their source objects.", x => x.ViewportLayerRules, (x, v) => x.ViewportLayerRules = v),
            T("required", "Required exhibit information", "Fields FTFEXHIBITQA expects, e.g. title; location; county; purpose", x => x.RequiredInfo, (x, v) => x.RequiredInfo = v),
            T("northProp", "North arrow rotation property", "The scale bar block's dynamic rotation for its north arrow (office G-ScalebarFig: Angle1). Set and checked when the view is turned.", x => x.NorthArrowProperty, (x, v) => x.NorthArrowProperty = v),
            T("powerLayers", "Overhead power layers", "Wildcards, e.g. V-UTIL-POWR-OVHD*", x => x.OverheadPowerLayers, (x, v) => x.OverheadPowerLayers = v),
            T("hatchLayers", "Other exhibits' hatch layers", "Wildcards, e.g. C-PROP-RWAY-PATT*; C-BNDY-LIMT-PATT*", x => x.OtherHatchLayers, (x, v) => x.OtherHatchLayers = v),
            T("areaFormat", "Area wording on the exhibit", "{purpose} {sqft} {acres} {title}; empty uses the easement area wording. Offices vary: " + string.Join(" | ", ExhibitSettings.AreaWordings), x => x.AreaLabelFormat, (x, v) => x.AreaLabelFormat = v),
            T("hatchSpacings", "Hatch spacing by pattern", "PATTERN=inches; e.g. ANSI31=0.03125; ANSI37=0.0977 -- others use the spacing below.", x => x.HatchSpacings, (x, v) => x.HatchSpacings = v),
            T("tableSpots", "Other table places", "X,Y; X,Y -- tried when a line/curve table runs into the logo, stamp or off the sheet.", x => x.TableSpots, (x, v) => x.TableSpots = v),
            T("stampLayer", "Stamp layer", "Empty uses the symbol layer.", x => x.StampLayer, (x, v) => x.StampLayer = v),
            T("stampPlaceholder", "Stamp placeholder block", "Empty draws a plain circle marked SURVEYOR'S STAMP.", x => x.StampPlaceholderBlock, (x, v) => x.StampPlaceholderBlock = v),
            T("stampLibrary", "Stamp library drawing", "Where FTFEXHIBITSTAMP offers stamp blocks from; empty offers this drawing's blocks.", x => x.StampLibrary, (x, v) => x.StampLibrary = v),
        };

        private static readonly List<Tuple<string, string, string, Func<ExhibitSettings, double>, Action<ExhibitSettings, double>>> OfficeNumbers =
            new List<Tuple<string, string, string, Func<ExhibitSettings, double>, Action<ExhibitSettings, double>>>
        {
            N("bL", "Border left (in)", "All four zero: the border sits on the printable margin.", x => x.BorderLeftIn, (x, v) => x.BorderLeftIn = v),
            N("bB", "Border bottom (in)", null, x => x.BorderBottomIn, (x, v) => x.BorderBottomIn = v),
            N("bR", "Border right (in)", null, x => x.BorderRightIn, (x, v) => x.BorderRightIn = v),
            N("bT", "Border top (in)", null, x => x.BorderTopIn, (x, v) => x.BorderTopIn = v),
            N("titleW", "Title width (in)", "Zero fits between the margins.", x => x.TitleWidthIn, (x, v) => x.TitleWidthIn = v),
            N("titleScale", "First title line size (x title height)", "e.g. 1.42857 sets EXHIBIT B larger than the lines under it.", x => x.TitleFirstLineScale, (x, v) => x.TitleFirstLineScale = v),
            N("colScale", "Table column width (x default)", "e.g. 0.65 for a narrow office text style.", x => x.TableColumnScale, (x, v) => x.TableColumnScale = v),
            N("curveX", "Curve table X", "Curve table X and Y both zero: under the line table.", x => x.CurveTableX, (x, v) => x.CurveTableX = v),
            N("curveY", "Curve table Y", null, x => x.CurveTableY, (x, v) => x.CurveTableY = v),
            N("tbX", "Title block X", null, x => x.TitleBlockX, (x, v) => x.TitleBlockX = v),
            N("tbY", "Title block Y", null, x => x.TitleBlockY, (x, v) => x.TitleBlockY = v),
            N("hatchSpacing", "Hatch line spacing on the exhibit (in)", "Easement hatches are scaled to print their lines this far apart at the exhibit scale; 0 leaves them as drafted. A hatch scale set by hand is kept.", x => x.HatchSpacingIn, (x, v) => x.HatchSpacingIn = v),
            N("stampX", "Stamp X", null, x => x.StampX, (x, v) => x.StampX = v),
            N("stampY", "Stamp Y", null, x => x.StampY, (x, v) => x.StampY = v),
            N("stampSize", "Stamp placeholder size (in)", null, x => x.StampSizeIn, (x, v) => x.StampSizeIn = v),
        };

        public override string Title { get { return "Easement Exhibits"; } }
        public override string AffectedCommands { get { return "FTFEXHIBIT, FTFEXHIBITREBUILD, FTFEXHIBITINSPECT"; } }

        // Name, caption, hint, width, getter, setter: one table drives the page, load and save.
        private static readonly List<Tuple<string, string, string, Func<ExhibitSettings, double>, Action<ExhibitSettings, double>>> Numbers =
            new List<Tuple<string, string, string, Func<ExhibitSettings, double>, Action<ExhibitSettings, double>>>
        {
            N("sheetW", "Sheet width (in)", null, x => x.SheetWidthIn, (x, v) => x.SheetWidthIn = v),
            N("sheetH", "Sheet height (in)", null, x => x.SheetHeightIn, (x, v) => x.SheetHeightIn = v),
            N("margin", "Printable margin (in)", "Anything outside it is flagged in the review.", x => x.MarginIn, (x, v) => x.MarginIn = v),
            N("vpL", "Viewport left (in)", null, x => x.ViewportLeftIn, (x, v) => x.ViewportLeftIn = v),
            N("vpB", "Viewport bottom (in)", null, x => x.ViewportBottomIn, (x, v) => x.ViewportBottomIn = v),
            N("vpW", "Viewport width (in)", null, x => x.ViewportWidthIn, (x, v) => x.ViewportWidthIn = v),
            N("vpH", "Viewport height (in)", null, x => x.ViewportHeightIn, (x, v) => x.ViewportHeightIn = v),
            N("fit", "Fit margin (share of viewport)", "Kept clear around the geometry, e.g. 0.12.", x => x.FitMargin, (x, v) => x.FitMargin = v),
            N("text", "Text height (in)", null, x => x.TextHeightIn, (x, v) => x.TextHeightIn = v),
            N("title", "Title text height (in)", null, x => x.TitleTextHeightIn, (x, v) => x.TitleTextHeightIn = v),
            N("north", "North arrow size (in)", null, x => x.NorthArrowSizeIn, (x, v) => x.NorthArrowSizeIn = v),
            N("bar", "Scale bar length (in)", null, x => x.ScaleBarLengthIn, (x, v) => x.ScaleBarLengthIn = v),
            N("titleX", "Title X", null, x => x.TitleX, (x, v) => x.TitleX = v), N("titleY", "Title Y", null, x => x.TitleY, (x, v) => x.TitleY = v),
            N("northX", "North arrow X", null, x => x.NorthArrowX, (x, v) => x.NorthArrowX = v), N("northY", "North arrow Y", null, x => x.NorthArrowY, (x, v) => x.NorthArrowY = v),
            N("barX", "Scale bar X", null, x => x.ScaleBarX, (x, v) => x.ScaleBarX = v), N("barY", "Scale bar Y", null, x => x.ScaleBarY, (x, v) => x.ScaleBarY = v),
            N("legendX", "Legend X", null, x => x.LegendX, (x, v) => x.LegendX = v), N("legendY", "Legend Y", null, x => x.LegendY, (x, v) => x.LegendY = v),
            N("areaX", "Area table X", null, x => x.AreaTableX, (x, v) => x.AreaTableX = v), N("areaY", "Area table Y", null, x => x.AreaTableY, (x, v) => x.AreaTableY = v),
            N("lineX", "Line table X", null, x => x.LineTableX, (x, v) => x.LineTableX = v), N("lineY", "Line table Y", null, x => x.LineTableY, (x, v) => x.LineTableY = v),
            N("notesX", "Notes X", null, x => x.NotesX, (x, v) => x.NotesX = v), N("notesY", "Notes Y", null, x => x.NotesY, (x, v) => x.NotesY = v),
            N("infoX", "Information X", null, x => x.InfoX, (x, v) => x.InfoX = v), N("infoY", "Information Y", null, x => x.InfoY, (x, v) => x.InfoY = v),
        };

        private static readonly List<Tuple<string, string, string, Func<ExhibitSettings, string>, Action<ExhibitSettings, string>>> Texts =
            new List<Tuple<string, string, string, Func<ExhibitSettings, string>, Action<ExhibitSettings, string>>>
        {
            T("device", "Plot device", "e.g. DWG To PDF.pc3", x => x.PlotDevice, (x, v) => x.PlotDevice = v),
            T("media", "Paper (media name)", "e.g. ANSI_A_(8.50_x_11.00_Inches)", x => x.MediaName, (x, v) => x.MediaName = v),
            T("template", "Template layout", "A layout in the drawing to copy for each exhibit; empty makes a blank layout.", x => x.TemplateLayout, (x, v) => x.TemplateLayout = v),
            T("titleblock", "Title block drawing", "A .dwg inserted as the title block; attributes TITLE, PROJECT, PARCEL, OWNER, APN, COUNTY, PURPOSE, SHEET, PREPAREDBY, DATE are filled.", x => x.TitleBlockPath, (x, v) => x.TitleBlockPath = v),
            T("name", "Layout name", "{sheet}, {title} and the other exhibit fields.", x => x.SheetNameFormat, (x, v) => x.SheetNameFormat = v),
            T("scales", "Scales (feet per inch)", "e.g. 10,20,30,40,50,60,100", x => x.Scales, (x, v) => x.Scales = v),
            T("freeze", "Freeze in viewport", "Model-space label layers the exhibit redraws on paper; separate with ;", x => x.FreezeInViewport, (x, v) => x.FreezeInViewport = v),
            T("existing", "Existing easement layers", "Kept visible in the viewport.", x => x.ExistingEasementLayers, (x, v) => x.ExistingEasementLayers = v),
            T("vpLayer", "Viewport layer", null, x => x.ViewportLayer, (x, v) => x.ViewportLayer = v),
            T("annoLayer", "Annotation layer", null, x => x.AnnotationLayer, (x, v) => x.AnnotationLayer = v),
            T("tableLayer", "Table layer", null, x => x.TableLayer, (x, v) => x.TableLayer = v),
            T("symbolLayer", "Symbol layer", null, x => x.SymbolLayer, (x, v) => x.SymbolLayer = v),
            T("borderLayer", "Border layer", null, x => x.BorderLayer, (x, v) => x.BorderLayer = v),
            T("dimLayer", "Dimension layer", null, x => x.DimensionLayer, (x, v) => x.DimensionLayer = v),
            T("dimStyle", "Dimension style", "Empty uses the current style.", x => x.DimensionStyle, (x, v) => x.DimensionStyle = v),
            T("tableStyle", "Table style", "Empty uses the current style.", x => x.TableStyle, (x, v) => x.TableStyle = v),
            T("textStyle", "Text style", "Empty uses the current style.", x => x.TextStyle, (x, v) => x.TextStyle = v),
            T("lineweights", "Layer lineweights", "LAYER=0.50; ... applied only where the layer still has the default lineweight.", x => x.Lineweights, (x, v) => x.Lineweights = v),
            T("northBlock", "North arrow block", "A block in the drawing; empty draws a simple arrow.", x => x.NorthArrowBlock, (x, v) => x.NorthArrowBlock = v),
            T("scaleText", "Scale text", "{scale}", x => x.ScaleTextFormat, (x, v) => x.ScaleTextFormat = v),
            T("titleLines", "Title lines", "Separate lines with |; {title} {location} {county} {purpose} ...", x => x.TitleLines, (x, v) => x.TitleLines = v),
            T("infoLines", "Information lines", "Lines whose field is blank are left off.", x => x.InfoLines, (x, v) => x.InfoLines = v),
            T("combined", "Combined area row", "What the sum row is called -- not a legal total unless you say so.", x => x.CombinedAreaLabel, (x, v) => x.CombinedAreaLabel = v),
            T("notes", "Standard notes", "Separate notes with |", x => x.Notes, (x, v) => x.Notes = v),
        };

        private static readonly List<Tuple<string, string, Func<ExhibitSettings, bool>, Action<ExhibitSettings, bool>>> Checks =
            new List<Tuple<string, string, Func<ExhibitSettings, bool>, Action<ExhibitSettings, bool>>>
        {
            C("border", "Draw a border when there is no template or title block", x => x.DrawBorder, (x, v) => x.DrawBorder = v),
            C("lock", "Lock the viewport", x => x.LockViewport, (x, v) => x.LockViewport = v),
            C("titleOn", "Title", x => x.DrawTitle, (x, v) => x.DrawTitle = v),
            C("northOn", "North arrow", x => x.DrawNorthArrow, (x, v) => x.DrawNorthArrow = v),
            C("barOn", "Scale bar", x => x.DrawScaleBar, (x, v) => x.DrawScaleBar = v),
            C("legendOn", "Legend", x => x.DrawLegend, (x, v) => x.DrawLegend = v),
            C("acres", "Acres in the area table", x => x.AreaTableAcres, (x, v) => x.AreaTableAcres = v),
            C("combinedOn", "Combined area row", x => x.AreaTableCombined, (x, v) => x.AreaTableCombined = v),
            C("lineTable", "Line/curve table", x => x.DrawLineCurveTable, (x, v) => x.DrawLineCurveTable = v),
            C("courseLabels", "Course labels", x => x.DrawCourseLabels, (x, v) => x.DrawCourseLabels = v),
            C("easementLabels", "Easement title/area labels", x => x.DrawEasementLabels, (x, v) => x.DrawEasementLabels = v),
            C("pointLabels", "POC / POB / terminus leaders", x => x.DrawPointLabels, (x, v) => x.DrawPointLabels = v),
            C("dims", "Width dimensions", x => x.DrawWidthDimensions, (x, v) => x.DrawWidthDimensions = v),
            C("parcel", "Parcel / APN label", x => x.DrawParcelLabel, (x, v) => x.DrawParcelLabel = v),
        };

        private static Tuple<string, string, string, Func<ExhibitSettings, double>, Action<ExhibitSettings, double>> N(string k, string c, string h, Func<ExhibitSettings, double> g, Action<ExhibitSettings, double> s)
        { return Tuple.Create(k, c, h, g, s); }

        private static Tuple<string, string, string, Func<ExhibitSettings, string>, Action<ExhibitSettings, string>> T(string k, string c, string h, Func<ExhibitSettings, string> g, Action<ExhibitSettings, string> s)
        { return Tuple.Create(k, c, h, g, s); }

        private static Tuple<string, string, Func<ExhibitSettings, bool>, Action<ExhibitSettings, bool>> C(string k, string c, Func<ExhibitSettings, bool> g, Action<ExhibitSettings, bool> s)
        { return Tuple.Create(k, c, g, s); }

        protected override void BuildBody()
        {
            Heading("Sheet and template");
            foreach (var t in Texts.Take(6)) _texts[t.Item1] = TextRow(t.Item2, t.Item3, 320);
            foreach (var n in Numbers.Take(3)) _numbers[n.Item1] = TextRow(n.Item2, n.Item3);
            Heading("Viewport");
            foreach (var n in Numbers.Skip(3).Take(5)) _numbers[n.Item1] = TextRow(n.Item2, n.Item3);
            _orientation = ComboRow("Orientation", new object[] { "NorthUp", "AllowRotate" }, "AllowRotate turns the view (never the geometry) when it gives a larger scale.");
            foreach (var t in Texts.Skip(6).Take(3)) _texts[t.Item1] = TextRow(t.Item2, t.Item3, 320);
            Heading("Text, layers and styles");
            foreach (var n in Numbers.Skip(8).Take(2)) _numbers[n.Item1] = TextRow(n.Item2, n.Item3);
            foreach (var t in Texts.Skip(9).Take(9)) _texts[t.Item1] = TextRow(t.Item2, t.Item3, 320);
            Heading("What is drawn");
            foreach (var c in Checks) _checks[c.Item1] = CheckRow(c.Item2, null);
            _areaTable = ComboRow("Area table", new object[] { "Multiple", "Always", "Never" }, "Multiple: only with more than one easement or component.");
            foreach (var n in Numbers.Skip(10).Take(2)) _numbers[n.Item1] = TextRow(n.Item2, n.Item3);
            foreach (var t in Texts.Skip(18)) _texts[t.Item1] = TextRow(t.Item2, t.Item3, 420);
            Heading("Where things go (inches from the lower-left of the sheet)");
            foreach (var n in Numbers.Skip(12)) _numbers[n.Item1] = TextRow(n.Item2, n.Item3);
            Heading("Office standard");
            foreach (var t in OfficeTexts) _officeTexts[t.Item1] = TextRow(t.Item2, t.Item3, 420);
            foreach (var n in OfficeNumbers) _officeNumbers[n.Item1] = TextRow(n.Item2, n.Item3);
            _areaLabelTitle = CheckRow("Repeat the easement title in the plan label", "Off when the sheet title already names the easement.");
            _stripAlong = CheckRow("Strip labels run along the strip", "Off: the title/area label reads horizontally beside the strip.");
            _narrow = ComboRow("Narrow strip label", new object[] { "NoLeader", "Leader", "Ask" }, "When a strip is too narrow on paper for its label: beside it, beside it with a leader, or ask each time.");
            _power = ComboRow("Overhead power in the viewport", new object[] { ExhibitSettings.User, ExhibitSettings.Show, ExhibitSettings.Hide }, "User leaves it as the drawing has it. In the exhibit viewport only; each exhibit can choose otherwise.");
            _hatches = ComboRow("Other exhibits' hatches", new object[] { ExhibitSettings.User, ExhibitSettings.Relevant, ExhibitSettings.Show, ExhibitSettings.Hide }, "Relevant shows them only where they belong to the easements on the exhibit. In the viewport only.");
            _stamp = ComboRow("Surveyor stamp", new object[] { ExhibitSettings.StampNone, ExhibitSettings.StampPlaceholder, ExhibitSettings.StampBlock }, "Placeholder marks its place; Block lets the surveyor choose a stamp block with FTFEXHIBITSTAMP. FTF never picks a surveyor or seal.");
            _headings = ComboRow("Line table headings", new object[] { ExhibitSettings.HeadingsCustom, ExhibitSettings.HeadingsDistanceBearing, ExhibitSettings.HeadingsLengthDirection }, "Custom uses the line table columns as written.");
        }

        public override void LoadFrom(FtfSettings s)
        {
            var x = s.Exhibits;
            foreach (var n in Numbers) _numbers[n.Item1].Text = Fmt(n.Item4(x));
            foreach (var t in Texts) _texts[t.Item1].Text = t.Item4(x) ?? string.Empty;
            foreach (var c in Checks) _checks[c.Item1].Checked = c.Item3(x);
            ComboHelp.SelectOrAdd(_orientation, x.Orientation, false);
            ComboHelp.SelectOrAdd(_areaTable, x.AreaTable, false);
            foreach (var t in OfficeTexts) _officeTexts[t.Item1].Text = t.Item4(x) ?? string.Empty;
            foreach (var n in OfficeNumbers) _officeNumbers[n.Item1].Text = Fmt(n.Item4(x));
            _areaLabelTitle.Checked = x.AreaLabelTitle;
            _stripAlong.Checked = x.StripLabelAlong;
            ComboHelp.SelectOrAdd(_narrow, x.NarrowStripLabel, false);
            ComboHelp.SelectOrAdd(_power, x.OverheadPower, false);
            ComboHelp.SelectOrAdd(_hatches, x.OtherHatches, false);
            ComboHelp.SelectOrAdd(_stamp, x.StampMode, false);
            ComboHelp.SelectOrAdd(_headings, x.LineTableHeadings, false);
        }

        public override void SaveTo(FtfSettings s, ICollection<string> problems)
        {
            var x = s.Exhibits;
            foreach (var n in Numbers)
            {
                var v = n.Item4(x);
                if (ReadDouble(_numbers[n.Item1], "Exhibits: " + n.Item2, d => d >= 0, "must not be negative", problems, ref v)) n.Item5(x, v);
            }
            foreach (var t in Texts) t.Item5(x, (_texts[t.Item1].Text ?? string.Empty).Trim());
            foreach (var c in Checks) c.Item4(x, _checks[c.Item1].Checked);
            x.Orientation = Convert.ToString(_orientation.SelectedItem) ?? "NorthUp";
            x.AreaTable = Convert.ToString(_areaTable.SelectedItem) ?? "Multiple";
            foreach (var t in OfficeTexts) t.Item5(x, (_officeTexts[t.Item1].Text ?? string.Empty).Trim());
            foreach (var n in OfficeNumbers)
            {
                var v = n.Item4(x);
                if (ReadDouble(_officeNumbers[n.Item1], "Exhibits: " + n.Item2, d => d >= 0, "must not be negative", problems, ref v)) n.Item5(x, v);
            }
            x.AreaLabelTitle = _areaLabelTitle.Checked;
            x.StripLabelAlong = _stripAlong.Checked;
            x.NarrowStripLabel = Convert.ToString(_narrow.SelectedItem) ?? "NoLeader";
            x.OverheadPower = Convert.ToString(_power.SelectedItem) ?? ExhibitSettings.User;
            x.OtherHatches = Convert.ToString(_hatches.SelectedItem) ?? ExhibitSettings.User;
            x.StampMode = Convert.ToString(_stamp.SelectedItem) ?? ExhibitSettings.StampNone;
            x.LineTableHeadings = Convert.ToString(_headings.SelectedItem) ?? ExhibitSettings.HeadingsCustom;
            x.Validate(problems);
        }

        public override void RestoreDefaults(FtfSettings s)
        {
            s.Exhibits.RestoreDefaults();
            LoadFrom(s);
        }
    }
}
