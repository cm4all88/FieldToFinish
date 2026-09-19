using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;

namespace FieldCodes.Settings
{
    /// <summary>
    /// How FTFEXHIBIT lays out an easement exhibit. Every choice is the profile's: sheet,
    /// template, viewport, text, layers, symbols, tables, notes and naming. The defaults are a
    /// plain letter-size portrait exhibit, not any one company's standard.
    /// Sheet positions are in inches from the lower-left corner of the sheet.
    /// </summary>
    public sealed class ExhibitSettings : ISettingsSection
    {
        public string Title { get { return "Easement Exhibits"; } }
        public string AffectedCommands { get { return "FTFEXHIBIT, FTFEXHIBITREBUILD, FTFEXHIBITINSPECT"; } }

        // Sheet and template.
        [JsonProperty("sheetWidthIn")] public double SheetWidthIn { get; set; }
        [JsonProperty("sheetHeightIn")] public double SheetHeightIn { get; set; }
        [JsonProperty("marginIn")] public double MarginIn { get; set; }
        [JsonProperty("plotDevice")] public string PlotDevice { get; set; }
        [JsonProperty("mediaName")] public string MediaName { get; set; }
        /// <summary>A layout already in the drawing to copy for each exhibit (title block and all). Empty creates a blank layout.</summary>
        [JsonProperty("templateLayout")] public string TemplateLayout { get; set; }
        /// <summary>A drawing inserted as the title block. Empty draws a simple border and title.</summary>
        [JsonProperty("titleBlockPath")] public string TitleBlockPath { get; set; }
        [JsonProperty("drawBorder")] public bool DrawBorder { get; set; }
        [JsonProperty("sheetNameFormat")] public string SheetNameFormat { get; set; }

        // Viewport.
        [JsonProperty("viewportLeftIn")] public double ViewportLeftIn { get; set; }
        [JsonProperty("viewportBottomIn")] public double ViewportBottomIn { get; set; }
        [JsonProperty("viewportWidthIn")] public double ViewportWidthIn { get; set; }
        [JsonProperty("viewportHeightIn")] public double ViewportHeightIn { get; set; }
        [JsonProperty("viewportLayer")] public string ViewportLayer { get; set; }
        [JsonProperty("lockViewport")] public bool LockViewport { get; set; }
        /// <summary>Engineering scales offered, feet per inch, e.g. "10,20,30,40,50,60,100".</summary>
        [JsonProperty("scales")] public string Scales { get; set; }
        /// <summary>Share of the viewport kept clear around the geometry.</summary>
        [JsonProperty("fitMargin")] public double FitMargin { get; set; }
        /// <summary>NorthUp, or AllowRotate: turn the view (never the geometry) when it reads better.</summary>
        [JsonProperty("orientation")] public string Orientation { get; set; }
        /// <summary>Layers frozen in the exhibit viewport (model-space labels the exhibit redraws on paper).</summary>
        [JsonProperty("freezeInViewport")] public string FreezeInViewport { get; set; }
        /// <summary>Existing easement layers kept visible in the viewport even if the list above would freeze them.</summary>
        [JsonProperty("existingEasementLayers")] public string ExistingEasementLayers { get; set; }

        // Text and layers.
        [JsonProperty("textStyle")] public string TextStyle { get; set; }
        [JsonProperty("textHeightIn")] public double TextHeightIn { get; set; }
        [JsonProperty("titleTextHeightIn")] public double TitleTextHeightIn { get; set; }
        [JsonProperty("annotationLayer")] public string AnnotationLayer { get; set; }
        [JsonProperty("tableLayer")] public string TableLayer { get; set; }
        [JsonProperty("symbolLayer")] public string SymbolLayer { get; set; }
        [JsonProperty("borderLayer")] public string BorderLayer { get; set; }
        [JsonProperty("dimensionLayer")] public string DimensionLayer { get; set; }
        [JsonProperty("dimensionStyle")] public string DimensionStyle { get; set; }
        [JsonProperty("tableStyle")] public string TableStyle { get; set; }
        /// <summary>Layer lineweights applied when the layer still has the default lineweight: "V-ESMT-E=0.50; V-ESMT-TEMP-E=0.25".</summary>
        [JsonProperty("lineweights")] public string Lineweights { get; set; }

        // What is drawn.
        [JsonProperty("title")] public bool DrawTitle { get; set; }
        /// <summary>Title lines; {title} {location} {county} {purpose} {project} {parcel} {owner} {apn} {sheet} {preparedBy} {date}. Lines left empty are skipped.</summary>
        [JsonProperty("titleLines")] public string TitleLines { get; set; }
        [JsonProperty("infoLines")] public string InfoLines { get; set; }
        [JsonProperty("northArrow")] public bool DrawNorthArrow { get; set; }
        [JsonProperty("northArrowBlock")] public string NorthArrowBlock { get; set; }
        [JsonProperty("northArrowSizeIn")] public double NorthArrowSizeIn { get; set; }
        [JsonProperty("scaleBar")] public bool DrawScaleBar { get; set; }
        [JsonProperty("scaleBarLengthIn")] public double ScaleBarLengthIn { get; set; }
        [JsonProperty("scaleTextFormat")] public string ScaleTextFormat { get; set; }
        [JsonProperty("legend")] public bool DrawLegend { get; set; }
        /// <summary>Always, Multiple (only with more than one easement or component), or Never.</summary>
        [JsonProperty("areaTable")] public string AreaTable { get; set; }
        [JsonProperty("areaTableAcres")] public bool AreaTableAcres { get; set; }
        [JsonProperty("areaTableCombined")] public bool AreaTableCombined { get; set; }
        /// <summary>The row naming the sum; it is not called a legal total unless the profile says so.</summary>
        [JsonProperty("combinedAreaLabel")] public string CombinedAreaLabel { get; set; }
        [JsonProperty("lineCurveTable")] public bool DrawLineCurveTable { get; set; }
        [JsonProperty("courseLabels")] public bool DrawCourseLabels { get; set; }
        [JsonProperty("easementLabels")] public bool DrawEasementLabels { get; set; }
        [JsonProperty("pointLabels")] public bool DrawPointLabels { get; set; }
        [JsonProperty("widthDimensions")] public bool DrawWidthDimensions { get; set; }
        [JsonProperty("parcelLabel")] public bool DrawParcelLabel { get; set; }
        /// <summary>Standard notes, one per line.</summary>
        [JsonProperty("notes")] public string Notes { get; set; }

        // Where things go on the sheet (inches).
        [JsonProperty("titleX")] public double TitleX { get; set; }
        [JsonProperty("titleY")] public double TitleY { get; set; }
        [JsonProperty("northArrowX")] public double NorthArrowX { get; set; }
        [JsonProperty("northArrowY")] public double NorthArrowY { get; set; }
        [JsonProperty("scaleBarX")] public double ScaleBarX { get; set; }
        [JsonProperty("scaleBarY")] public double ScaleBarY { get; set; }
        [JsonProperty("legendX")] public double LegendX { get; set; }
        [JsonProperty("legendY")] public double LegendY { get; set; }
        [JsonProperty("areaTableX")] public double AreaTableX { get; set; }
        [JsonProperty("areaTableY")] public double AreaTableY { get; set; }
        [JsonProperty("lineTableX")] public double LineTableX { get; set; }
        [JsonProperty("lineTableY")] public double LineTableY { get; set; }
        /// <summary>Curve table corner; both zero puts it under the line table.</summary>
        /// <summary>Table column widths are this share of the default (narrow office fonts need less).</summary>
        [JsonProperty("tableColumnScale")] public double TableColumnScale { get; set; }
        [JsonProperty("curveTableX")] public double CurveTableX { get; set; }
        [JsonProperty("curveTableY")] public double CurveTableY { get; set; }
        [JsonProperty("notesX")] public double NotesX { get; set; }
        [JsonProperty("notesY")] public double NotesY { get; set; }
        [JsonProperty("infoX")] public double InfoX { get; set; }
        [JsonProperty("infoY")] public double InfoY { get; set; }

        // Office standard: the drawings, blocks and styles an office's own exhibits use.
        /// <summary>Plot style table set on the layout and used by FTFEXHIBITPREVIEW, e.g. "PMX Survey BW.ctb". Empty keeps the layout's.</summary>
        [JsonProperty("plotStyleTable")] public string PlotStyleTable { get; set; }
        /// <summary>A drawing or template the template layout is imported from when it is not already in the drawing.</summary>
        [JsonProperty("templateFile")] public string TemplateFile { get; set; }
        /// <summary>A drawing whose block definitions (logo, stamp, scale bar, title block) are brought in when the drawing lacks them.</summary>
        [JsonProperty("blockLibrary")] public string BlockLibrary { get; set; }
        /// <summary>The border rectangle, inches; all zero draws it at the printable margin.</summary>
        [JsonProperty("borderLeftIn")] public double BorderLeftIn { get; set; }
        [JsonProperty("borderBottomIn")] public double BorderBottomIn { get; set; }
        [JsonProperty("borderRightIn")] public double BorderRightIn { get; set; }
        [JsonProperty("borderTopIn")] public double BorderTopIn { get; set; }
        /// <summary>Title text style and layer; empty uses the text style and annotation layer.</summary>
        [JsonProperty("titleStyle")] public string TitleTextStyle { get; set; }
        [JsonProperty("titleLayer")] public string TitleLayer { get; set; }
        /// <summary>Title column width in inches; zero fits it between the margins.</summary>
        [JsonProperty("titleWidthIn")] public double TitleWidthIn { get; set; }
        /// <summary>The first title line is this many times the title height ("EXHIBIT B" larger than the rest).</summary>
        [JsonProperty("titleFirstLineScale")] public double TitleFirstLineScale { get; set; }
        /// <summary>A title block by block name (in the drawing or the block library), inserted at TitleBlockX/Y.</summary>
        [JsonProperty("titleBlockName")] public string TitleBlockName { get; set; }
        [JsonProperty("titleBlockX")] public double TitleBlockX { get; set; }
        [JsonProperty("titleBlockY")] public double TitleBlockY { get; set; }
        /// <summary>
        /// Which exhibit field goes into which title block attribute: "JobNo={projectNumber}; SheetNo={sheetNo}; DrawnBy={preparedBy}".
        /// Empty uses attribute tags named after the fields (TITLE, PROJECT, PARCEL, OWNER, APN, COUNTY, PURPOSE, SHEET, PREPAREDBY, DATE...).
        /// Attributes not mapped are left exactly as the block defines them, and listed in the review.
        /// </summary>
        [JsonProperty("titleBlockAttributes")] public string TitleBlockAttributes { get; set; }
        /// <summary>A scale bar block used instead of the drawn one, e.g. an office dynamic block.</summary>
        [JsonProperty("scaleBarBlock")] public string ScaleBarBlock { get; set; }
        /// <summary>Visibility state picked for the exhibit scale, e.g. 1" = {scale}'. Empty sets none.</summary>
        [JsonProperty("scaleBarVisibility")] public string ScaleBarVisibility { get; set; }
        /// <summary>Further sheet blocks, "NAME@X,Y", "NAME@X,Y@LAYER" or "NAME@X,Y@LAYER@PROPERTY=VALUE,..." separated by ; -- a logo, say, with its office picked.</summary>
        [JsonProperty("sheetBlocks")] public string SheetBlocks { get; set; }
        /// <summary>Multileader style for POC/POB/terminus leaders; empty uses the current style.</summary>
        [JsonProperty("leaderStyle")] public string LeaderStyle { get; set; }
        /// <summary>Whether the plan label repeats the easement title above its area (off when the sheet title names it).</summary>
        [JsonProperty("areaLabelTitle")] public bool AreaLabelTitle { get; set; }
        /// <summary>Line table title and columns: "HEADER={id}|HEADER={distance}|HEADER={bearing}".</summary>
        [JsonProperty("lineTableTitle")] public string LineTableTitle { get; set; }
        [JsonProperty("lineTableColumns")] public string LineTableColumns { get; set; }
        /// <summary>Curve table title and columns: {id} {radius} {length} {delta}.</summary>
        [JsonProperty("curveTableTitle")] public string CurveTableTitle { get; set; }
        [JsonProperty("curveTableColumns")] public string CurveTableColumns { get; set; }
        /// <summary>
        /// What the exhibit viewport shows, by layer: "PATTERN=Show|Hide|Relevant|User" separated by ;
        /// (wildcards * and ?). Show and Hide set the layer in this viewport only; Relevant shows it only
        /// when it holds something of the easements on the exhibit or their source objects; User means FTF
        /// never changes it. Layers matching no rule are left as they are. Model-space layers are never changed.
        /// </summary>
        [JsonProperty("viewportLayerRules")] public string ViewportLayerRules { get; set; }
        /// <summary>A strip too narrow on paper for its title/area label: NoLeader (label beside it), Leader, or Ask each time.</summary>
        [JsonProperty("narrowStripLabel")] public string NarrowStripLabel { get; set; }
        /// <summary>A strip's title/area label runs along the strip (true) or reads horizontally beside it (false).</summary>
        [JsonProperty("stripLabelAlong")] public bool StripLabelAlong { get; set; }
        /// <summary>Exhibit fields FTFEXHIBITQA expects filled in, e.g. "title;location;county;purpose".</summary>
        [JsonProperty("requiredInfo")] public string RequiredInfo { get; set; }
        /// <summary>A legend entry: {name} and {sqft}, e.g. "{name} ({sqft} SQ. FT.)".</summary>
        [JsonProperty("legendFormat")] public string LegendFormat { get; set; }

        // Production cleanup: office variations and choices exposed rather than fixed.
        /// <summary>
        /// The dynamic rotation property of the scale bar block that turns its north arrow (the office G-ScalebarFig
        /// block's "Angle1"). When the view is turned FTF sets it and checks the arrow's geometry; empty means the
        /// block's arrow cannot be turned and the exhibit says so.
        /// </summary>
        [JsonProperty("northArrowProperty")] public string NorthArrowProperty { get; set; }
        /// <summary>How far apart easement hatch lines print on the exhibit, inches. Zero leaves the hatch as drafted.</summary>
        [JsonProperty("hatchSpacingIn")] public double HatchSpacingIn { get; set; }
        /// <summary>Spacing by hatch pattern where offices differ by pattern, "ANSI31=0.03125; ANSI37=0.0977"; others use the spacing above.</summary>
        [JsonProperty("hatchSpacings")] public string HatchSpacings { get; set; }
        /// <summary>Overhead power in the exhibit viewport: Show, Hide or User (leave it as the drawing has it).</summary>
        [JsonProperty("overheadPower")] public string OverheadPower { get; set; }
        [JsonProperty("overheadPowerLayers")] public string OverheadPowerLayers { get; set; }
        /// <summary>
        /// Hatches of other exhibits and easements (layers named below) in the exhibit viewport: Show, Hide, Relevant
        /// (only when they belong to the easements on this exhibit) or User.
        /// </summary>
        [JsonProperty("otherHatches")] public string OtherHatches { get; set; }
        [JsonProperty("otherHatchLayers")] public string OtherHatchLayers { get; set; }
        /// <summary>
        /// Surveyor stamp: None; Placeholder (a marked place for the stamp); or Block (a stamp block the surveyor
        /// chooses with FTFEXHIBITSTAMP -- FTF never picks a surveyor or a seal itself).
        /// </summary>
        [JsonProperty("stampMode")] public string StampMode { get; set; }
        [JsonProperty("stampX")] public double StampX { get; set; }
        [JsonProperty("stampY")] public double StampY { get; set; }
        [JsonProperty("stampSizeIn")] public double StampSizeIn { get; set; }
        [JsonProperty("stampLayer")] public string StampLayer { get; set; }
        /// <summary>A placeholder block for the stamp's place (office "stamp here" block); empty draws a plain marked circle.</summary>
        [JsonProperty("stampPlaceholderBlock")] public string StampPlaceholderBlock { get; set; }
        /// <summary>A drawing holding the stamp blocks FTFEXHIBITSTAMP offers; empty offers the drawing's own blocks.</summary>
        [JsonProperty("stampLibrary")] public string StampLibrary { get; set; }
        /// <summary>The area line for an easement on the exhibit: {purpose} {sqft} {acres} {title}. Empty uses the easement area wording. Construction and acquisition areas keep the area wording.</summary>
        [JsonProperty("areaLabelFormat")] public string AreaLabelFormat { get; set; }
        /// <summary>Line table headings: DISTANCE/BEARING, LENGTH/DIRECTION, or Custom (the line table columns as written).</summary>
        [JsonProperty("lineTableHeadings")] public string LineTableHeadings { get; set; }
        /// <summary>Other places a line/curve table may go, "X,Y; X,Y", tried when its own place runs into sheet furniture or off the sheet.</summary>
        [JsonProperty("tableSpots")] public string TableSpots { get; set; }

        public ExhibitSettings() { RestoreDefaults(); }

        public void RestoreDefaults()
        {
            SheetWidthIn = 8.5;
            SheetHeightIn = 11;
            MarginIn = 0.5;
            PlotDevice = "DWG To PDF.pc3";
            MediaName = "ANSI_full_bleed_A_(8.50_x_11.00_Inches)";     // no device margin: sheet inches are layout inches
            TemplateLayout = string.Empty;
            TitleBlockPath = string.Empty;
            DrawBorder = true;
            SheetNameFormat = "EXHIBIT {sheet}";

            ViewportLeftIn = 0.75;
            ViewportBottomIn = 4.4;
            ViewportWidthIn = 7.0;
            ViewportHeightIn = 4.9;
            ViewportLayer = "V-ANNO-VPRT";
            LockViewport = true;
            Scales = "10,20,30,40,50,60,100,200,300,400,500";
            FitMargin = 0.12;
            Orientation = "NorthUp";
            FreezeInViewport = "V-ESMT-TEXT-E;V-ESMT-TABL-E;V-ESMT-DIMS-E";
            ExistingEasementLayers = string.Empty;

            TextStyle = string.Empty;
            TextHeightIn = 0.08;
            TitleTextHeightIn = 0.14;
            AnnotationLayer = "V-ANNO-TEXT";
            TableLayer = "V-ANNO-TABL";
            SymbolLayer = "V-ANNO-SYMB";
            BorderLayer = "V-ANNO-TTLB";
            DimensionLayer = "V-ANNO-DIMS";
            DimensionStyle = string.Empty;
            TableStyle = string.Empty;
            Lineweights = string.Empty;

            DrawTitle = true;
            TitleLines = "{title}|{location}|{county}|{purpose}";
            InfoLines = "PROJECT: {project}|PARCEL: {parcel}|OWNER: {owner}|APN: {apn}|PREPARED BY: {preparedBy}|DATE: {date}|SHEET {sheet}";
            DrawNorthArrow = true;
            NorthArrowBlock = string.Empty;
            NorthArrowSizeIn = 0.5;
            DrawScaleBar = true;
            ScaleBarLengthIn = 2.0;
            ScaleTextFormat = "1 INCH = {scale} FT.";
            DrawLegend = true;
            AreaTable = "Multiple";
            AreaTableAcres = true;
            AreaTableCombined = true;
            CombinedAreaLabel = "COMBINED AREA SHOWN";
            DrawLineCurveTable = true;
            DrawCourseLabels = true;
            DrawEasementLabels = true;
            DrawPointLabels = true;
            DrawWidthDimensions = true;
            DrawParcelLabel = true;
            Notes = string.Empty;

            // 8.5 x 11 portrait: title across the top, the view under it, and below the view the
            // line/curve tables and the legend on the left, the area table, scale bar, north arrow and
            // information on the right. Tables and text hang down from their corner, and what hangs
            // under a table moves down when the table grows.
            TitleX = 4.25; TitleY = 10.35;
            LineTableX = 0.75; LineTableY = 4.25;
            CurveTableX = 0; CurveTableY = 0;
            TableColumnScale = 1;
            AreaTableX = 4.2; AreaTableY = 4.25;
            ScaleBarX = 4.2; ScaleBarY = 2.75;
            NorthArrowX = 7.45; NorthArrowY = 2.8;
            InfoX = 4.2; InfoY = 2.3;
            LegendX = 0.75; LegendY = 1.55;
            NotesX = 0.75; NotesY = 0.95;

            PlotStyleTable = string.Empty;
            TemplateFile = string.Empty;
            BlockLibrary = string.Empty;
            BorderLeftIn = BorderBottomIn = BorderRightIn = BorderTopIn = 0;
            TitleTextStyle = string.Empty;
            TitleLayer = string.Empty;
            TitleWidthIn = 0;
            TitleFirstLineScale = 1;
            TitleBlockName = string.Empty;
            TitleBlockX = TitleBlockY = 0;
            TitleBlockAttributes = string.Empty;
            ScaleBarBlock = string.Empty;
            ScaleBarVisibility = string.Empty;
            SheetBlocks = string.Empty;
            LeaderStyle = string.Empty;
            AreaLabelTitle = true;
            LineTableTitle = "LINE TABLE";
            LineTableColumns = "LINE NO.={id}|DISTANCE={distance}|BEARING={bearing}";
            CurveTableTitle = "CURVE TABLE";
            CurveTableColumns = "CURVE NO.={id}|RADIUS={radius}|LENGTH={length}|DELTA={delta}";
            NarrowStripLabel = "NoLeader";
            StripLabelAlong = true;
            RequiredInfo = "title";
            LegendFormat = "{name}";
            // FTF's own easement layers show in a viewport only where they hold the easements on the exhibit.
            ViewportLayerRules = "V-ESMT-E=Relevant; V-ESMT-TEMP-E=Relevant; V-ESMT-CONS-E=Relevant";

            NorthArrowProperty = string.Empty;
            HatchSpacingIn = 0;
            HatchSpacings = string.Empty;
            OverheadPower = User;
            OverheadPowerLayers = "*-POWR-OVHD*";
            OtherHatches = User;
            OtherHatchLayers = string.Empty;
            StampMode = StampNone;
            StampX = 6.5; StampY = 2.0;
            StampSizeIn = 1.5;
            StampLayer = string.Empty;
            StampPlaceholderBlock = string.Empty;
            StampLibrary = string.Empty;
            AreaLabelFormat = string.Empty;
            LineTableHeadings = HeadingsCustom;
            TableSpots = string.Empty;
        }

        public const string StampNone = "None", StampPlaceholder = "Placeholder", StampBlock = "Block";
        public const string HeadingsDistanceBearing = "DISTANCE/BEARING", HeadingsLengthDirection = "LENGTH/DIRECTION", HeadingsCustom = "Custom";

        /// <summary>Office area wordings seen on delivered exhibits, offered as choices; none is imposed.</summary>
        public static readonly string[] AreaWordings =
        {
            "APPROX {purpose} EASEMENT AREA = {sqft} SF",
            "APPROX. EASEMENT AREA= {sqft} SF",
            "{purpose} EASEMENT ({sqft} SQ. FT.)",
            "AREA = {sqft} SQ. FT."
        };

        /// <summary>The line table columns: from the headings choice, or as written when Custom.</summary>
        public List<KeyValuePair<string, string>> LineColumns()
        {
            if (string.Equals(LineTableHeadings, HeadingsDistanceBearing, System.StringComparison.OrdinalIgnoreCase))
                return Columns("LINE NO.={id}|DISTANCE={distance}|BEARING={bearing}");
            if (string.Equals(LineTableHeadings, HeadingsLengthDirection, System.StringComparison.OrdinalIgnoreCase))
                return Columns("LINE NO.={id}|LENGTH={distance}|DIRECTION={bearing}");
            var columns = Columns(LineTableColumns);
            return columns.Count > 0 ? columns : Columns("LINE NO.={id}|DISTANCE={distance}|BEARING={bearing}");
        }

        /// <summary>The area line for an easement, when the profile words it; null to use the easement area wording.</summary>
        public string AreaLine(string purpose, string title, double squareFeet)
        {
            if (string.IsNullOrWhiteSpace(AreaLabelFormat)) return null;
            return AreaLabelFormat
                .Replace("{purpose}", (purpose ?? string.Empty).Trim().ToUpperInvariant())
                .Replace("{title}", (title ?? string.Empty).Trim().ToUpperInvariant())
                .Replace("{sqft}", squareFeet.ToString("N0", CultureInfo.InvariantCulture))
                .Replace("{acres}", (squareFeet / 43560.0).ToString("0.000", CultureInfo.InvariantCulture))
                .Replace("  ", " ").Trim();
        }

        /// <summary>The printed line spacing wanted for a hatch pattern, inches; zero leaves that hatch as drafted.</summary>
        public double HatchSpacingFor(string pattern)
        {
            foreach (var part in Split(HatchSpacings))
            {
                var eq = part.IndexOf('=');
                double value;
                if (eq > 0 && Wildcard(part.Substring(0, eq).Trim(), pattern) &&
                    double.TryParse(part.Substring(eq + 1).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && value >= 0)
                    return value;
            }
            return HatchSpacingIn;
        }

        /// <summary>Other table places, in order.</summary>
        public List<KeyValuePair<double, double>> TableSpotList()
        {
            var list = new List<KeyValuePair<double, double>>();
            foreach (var part in Split(TableSpots))
            {
                var xy = part.Split(',');
                double x, y;
                if (xy.Length == 2 && double.TryParse(xy[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out x) &&
                    double.TryParse(xy[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out y))
                    list.Add(new KeyValuePair<double, double>(x, y));
            }
            return list;
        }

        private static string Choice(string value, params string[] allowed)
        {
            return allowed.FirstOrDefault(a => string.Equals(a, (value ?? string.Empty).Trim(), System.StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>A viewport layer rule: which layers (wildcards) and what the viewport does with them.</summary>
        public sealed class LayerRule
        {
            public string Pattern;
            public string Action;
        }

        public const string Show = "Show", Hide = "Hide", Relevant = "Relevant", User = "User";

        /// <summary>
        /// The effective viewport layer rules, first match wins: the rules as written, then the older
        /// "existing easement layers" as Show and "freeze in viewport" list as Hide.
        /// </summary>
        public List<LayerRule> LayerRules()
        {
            return LayerRules(null, null);
        }

        /// <summary>
        /// As above, with the overhead power and other-hatch choices first -- the profile's, or this exhibit's own
        /// when the drafter chose differently (<paramref name="overheadPower"/>, <paramref name="otherHatches"/>; null uses the profile).
        /// </summary>
        public List<LayerRule> LayerRules(string overheadPower, string otherHatches)
        {
            var rules = new List<LayerRule>();
            var power = Choice(overheadPower, Show, Hide, User) ?? Choice(OverheadPower, Show, Hide, User) ?? User;
            if (power != User)
                foreach (var pattern in Split(OverheadPowerLayers)) rules.Add(new LayerRule { Pattern = pattern, Action = power });
            var hatches = Choice(otherHatches, Show, Hide, Relevant, User) ?? Choice(OtherHatches, Show, Hide, Relevant, User) ?? User;
            if (hatches != User)
                foreach (var pattern in Split(OtherHatchLayers)) rules.Add(new LayerRule { Pattern = pattern, Action = hatches });
            foreach (var part in Split(ViewportLayerRules))
            {
                var eq = part.LastIndexOf('=');
                if (eq <= 0) continue;
                var action = part.Substring(eq + 1).Trim();
                var known = new[] { Show, Hide, Relevant, User }.FirstOrDefault(a => string.Equals(a, action, System.StringComparison.OrdinalIgnoreCase));
                if (known == null) continue;
                rules.Add(new LayerRule { Pattern = part.Substring(0, eq).Trim(), Action = known });
            }
            foreach (var name in Split(ExistingEasementLayers)) rules.Add(new LayerRule { Pattern = name, Action = Show });
            foreach (var name in Split(FreezeInViewport)) rules.Add(new LayerRule { Pattern = name, Action = Hide });
            return rules;
        }

        /// <summary>The action for a layer, or null when no rule names it.</summary>
        public static string ActionFor(IList<LayerRule> rules, string layer)
        {
            foreach (var r in rules)
                if (Wildcard(r.Pattern, layer)) return r.Action;
            return null;
        }

        public static bool Wildcard(string pattern, string text)
        {
            var regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern ?? string.Empty).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
            return System.Text.RegularExpressions.Regex.IsMatch(text ?? string.Empty, regex, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        /// <summary>Table columns "HEADER={token}|..." as header/token pairs.</summary>
        public static List<KeyValuePair<string, string>> Columns(string spec)
        {
            var list = new List<KeyValuePair<string, string>>();
            foreach (var part in (spec ?? string.Empty).Split('|'))
            {
                var eq = part.LastIndexOf('=');
                if (eq <= 0) continue;
                list.Add(new KeyValuePair<string, string>(part.Substring(0, eq).Trim(), part.Substring(eq + 1).Trim()));
            }
            return list;
        }

        /// <summary>Sheet blocks "NAME@X,Y[@LAYER]".</summary>
        public List<SheetBlock> SheetBlockList()
        {
            var list = new List<SheetBlock>();
            foreach (var part in Split(SheetBlocks))
            {
                var bits = part.Split('@');
                if (bits.Length < 2) continue;
                var xy = bits[1].Split(',');
                double x, y;
                if (xy.Length != 2 || !double.TryParse(xy[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out x) ||
                    !double.TryParse(xy[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out y)) continue;
                var block = new SheetBlock { Name = bits[0].Trim(), X = x, Y = y, Layer = bits.Length > 2 && bits[2].Trim().Length > 0 ? bits[2].Trim() : null };
                if (bits.Length > 3)
                    foreach (var assignment in bits[3].Split(','))
                    {
                        var eq = assignment.IndexOf('=');
                        if (eq <= 0) continue;
                        var name = assignment.Substring(0, eq).Trim();
                        var value = assignment.Substring(eq + 1).Trim();
                        double scale;
                        if (string.Equals(name, "scale", System.StringComparison.OrdinalIgnoreCase) && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out scale) && scale > 0)
                            block.Scale = scale;
                        else block.Properties[name] = value;
                    }
                list.Add(block);
            }
            return list;
        }

        /// <summary>A sheet block; Properties are dynamic block values to set, e.g. Visibility1 = Puyallup.</summary>
        public sealed class SheetBlock
        {
            public string Name;
            public double X, Y;
            public string Layer;
            public double Scale = 1;
            public readonly Dictionary<string, string> Properties = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Title block attribute tag and the exhibit field template it gets, "TAG={field}".</summary>
        public List<KeyValuePair<string, string>> AttributeMap()
        {
            var list = new List<KeyValuePair<string, string>>();
            foreach (var part in Split(TitleBlockAttributes))
            {
                var eq = part.IndexOf('=');
                if (eq <= 0) continue;
                list.Add(new KeyValuePair<string, string>(part.Substring(0, eq).Trim(), part.Substring(eq + 1).Trim()));
            }
            return list;
        }

        /// <summary>
        /// Title block attributes that belong to the surveyor alone -- approval, acceptance, certification, licence,
        /// seal, stamp, signature. FTF never writes them, whatever a profile maps.
        /// </summary>
        public static bool IsProfessionalTag(string tag)
        {
            var t = new string((tag ?? string.Empty).ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
            if (t.Length == 0) return false;
            if (new[] { "APPROV", "ACCEPT", "CERTIF", "LICEN", "SEAL", "PLS" }.Any(t.Contains)) return true;
            if (t.StartsWith("SIGN", StringComparison.Ordinal)) return true;
            // A plot or date stamp is not a surveyor's stamp.
            return t.Contains("STAMP") && !new[] { "TIME", "PLOT", "DATE", "FILE" }.Any(t.Contains);
        }

        /// <summary>A person field ("DrawnBy", "CheckedBy"): filled only from what the drafter typed for the exhibit.</summary>
        public static bool IsPersonTag(string tag)
        {
            var t = new string((tag ?? string.Empty).ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
            return t.EndsWith("BY", StringComparison.Ordinal);
        }

        /// <summary>
        /// Why FTF will not fill this title block attribute from this mapping, or null when it may. Approval and seal
        /// fields are never filled; a person field only from an exhibit field (a fixed name in the profile would put the
        /// same person on every exhibit).
        /// </summary>
        public static string AttributeRefusal(string tag, string template)
        {
            if (IsProfessionalTag(tag)) return "approval, certification and seal fields are the surveyor's; FTF never fills them";
            if (IsPersonTag(tag) && !string.IsNullOrWhiteSpace(template) && template.IndexOf('{') < 0)
                return "a fixed name in the profile would appear on every exhibit; map it to a field the drafter fills, such as {preparedBy} or {checkedBy}";
            return null;
        }

        public void Validate(ICollection<string> problems)
        {
            foreach (var m in AttributeMap())
            {
                var refusal = AttributeRefusal(m.Key, m.Value);
                if (refusal != null) problems.Add("Exhibits: title block attribute " + m.Key + " cannot be mapped: " + refusal + ".");
            }
            if (SheetWidthIn <= 1 || SheetHeightIn <= 1) problems.Add("Exhibits: the sheet size is too small.");
            if (ViewportWidthIn <= 0.5 || ViewportHeightIn <= 0.5) problems.Add("Exhibits: the viewport is too small.");
            if (ViewportLeftIn < 0 || ViewportBottomIn < 0 || ViewportLeftIn + ViewportWidthIn > SheetWidthIn + 1e-9 || ViewportBottomIn + ViewportHeightIn > SheetHeightIn + 1e-9)
                problems.Add("Exhibits: the viewport does not fit on the sheet.");
            if (ScaleList().Count == 0) problems.Add("Exhibits: list at least one scale (feet per inch).");
            if (TextHeightIn <= 0 || TitleTextHeightIn <= 0) problems.Add("Exhibits: text heights must be greater than zero.");
            if (FitMargin < 0 || FitMargin >= 0.5) problems.Add("Exhibits: the fit margin must be between 0 and 0.5.");
            if (HatchSpacingIn < 0 || HatchSpacingIn > 2) problems.Add("Exhibits: the hatch spacing must be between 0 (leave the hatch as drafted) and 2 inches.");
            if (Choice(OverheadPower, Show, Hide, User) == null) problems.Add("Exhibits: overhead power must be Show, Hide or User.");
            if (Choice(OtherHatches, Show, Hide, Relevant, User) == null) problems.Add("Exhibits: other hatches must be Show, Hide, Relevant or User.");
            if (Choice(StampMode, StampNone, StampPlaceholder, StampBlock) == null) problems.Add("Exhibits: the stamp must be None, Placeholder or Block.");
            if (Choice(LineTableHeadings, HeadingsDistanceBearing, HeadingsLengthDirection, HeadingsCustom) == null) problems.Add("Exhibits: line table headings must be DISTANCE/BEARING, LENGTH/DIRECTION or Custom.");
        }

        public List<double> ScaleList()
        {
            var list = new List<double>();
            foreach (var part in (Scales ?? string.Empty).Split(',', ';', ' '))
            {
                double v;
                if (double.TryParse(part.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v) && v > 0) list.Add(v);
            }
            return list.Distinct().OrderBy(v => v).ToList();
        }

        public static List<string> Split(string list)
        {
            return (list ?? string.Empty).Split(';', '|', '\n').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        }
    }
}
