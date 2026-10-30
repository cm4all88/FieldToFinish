using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace FieldCodes.Settings
{
    /// <summary>
    /// The office standard for FTFROW: right-of-way runs, blocks and intersections drawn
    /// from picked monuments.
    ///
    /// The right-of-way LINES do not get a layer here. They take the layer, linetype and
    /// bearing/distance annotation of the Drafting Lines type named by
    /// <see cref="LineTypeName"/>, so a right-of-way line drawn by FTFROW and one drawn by
    /// FTFDRAWLINE can never disagree. Everything else FTFROW draws (centerline, monument
    /// symbols and notes, dimensions, road names) has its layer here.
    ///
    /// Layers are named but never invented: an empty layer is refused at draw time with
    /// an explanation, exactly as FTFDRAWLINE does. The wording of the notes is the
    /// office's to set; the defaults are common platting practice, not a claim about
    /// any one office's standard.
    /// </summary>
    public sealed class RightOfWaySettings : ISettingsSection
    {
        public const string DimensionsEnds = "Ends";
        public const string DimensionsEvery = "Every";

        public string Title { get { return "Right of Way"; } }
        public string AffectedCommands { get { return "FTFROW"; } }

        /// <summary>The Drafting Lines type whose layer, linetype and annotation the
        /// right-of-way lines use.</summary>
        [JsonProperty("lineTypeName")] public string LineTypeName { get; set; }

        /// <summary>The line through the picked monuments when they are not on the road centerline.</summary>
        [JsonProperty("monumentLineLayer")] public string MonumentLineLayer { get; set; }
        [JsonProperty("centerlineLayer")] public string CenterlineLayer { get; set; }
        [JsonProperty("monumentLayer")] public string MonumentLayer { get; set; }
        [JsonProperty("monumentTextLayer")] public string MonumentTextLayer { get; set; }
        [JsonProperty("dimensionLayer")] public string DimensionLayer { get; set; }
        [JsonProperty("roadNameLayer")] public string RoadNameLayer { get; set; }

        /// <summary>Note for a found monument. Tokens: {desc}, {pt}.</summary>
        [JsonProperty("foundFormat")] public string FoundFormat { get; set; }

        /// <summary>Note for a set monument. Tokens: {desc}, {pt}.</summary>
        [JsonProperty("setFormat")] public string SetFormat { get; set; }

        /// <summary>Second note line for an offset monument. Tokens: {dist}, {dir} (from
        /// the monument TO the corner), {fromdir} (from the corner to the monument).</summary>
        [JsonProperty("offsetFormat")] public string OffsetFormat { get; set; }

        /// <summary>The right-of-way dimension. Tokens: {width}.</summary>
        [JsonProperty("dimensionFormat")] public string DimensionFormat { get; set; }

        /// <summary>The road name note. Tokens: {name}.</summary>
        [JsonProperty("roadNameFormat")] public string RoadNameFormat { get; set; }

        /// <summary>Set every note in capitals, the usual plat convention.</summary>
        [JsonProperty("upperCase")] public bool UpperCase { get; set; }

        [JsonProperty("drawPickedLine")] public bool DrawPickedLine { get; set; }
        [JsonProperty("drawCenterline")] public bool DrawCenterline { get; set; }
        [JsonProperty("labelOppositeLine")] public bool LabelOppositeLine { get; set; }
        [JsonProperty("labelCenterline")] public bool LabelCenterline { get; set; }

        /// <summary>"Ends" dimensions the width at the first and last monument of a run;
        /// "Every" tries every monument.</summary>
        [JsonProperty("dimensionPolicy")] public string DimensionPolicy { get; set; }

        [JsonProperty("monumentRadiusPlotted")] public double MonumentRadiusPlotted { get; set; }
        [JsonProperty("textHeightPlotted")] public double TextHeightPlotted { get; set; }
        [JsonProperty("textOffsetPlotted")] public double TextOffsetPlotted { get; set; }
        [JsonProperty("textStyle")] public string TextStyle { get; set; }

        /// <summary>Corner radius offered by default at block corners, feet. 0 is a sharp corner.</summary>
        [JsonProperty("defaultCornerRadiusFeet")] public double DefaultCornerRadiusFeet { get; set; }

        /// <summary>A measured cross-street width differing from the record width by more
        /// than this is flagged, feet.</summary>
        [JsonProperty("widthToleranceFeet")] public double WidthToleranceFeet { get; set; }

        public RightOfWaySettings() { RestoreDefaults(); }

        public void RestoreDefaults()
        {
            LineTypeName = "Right of Way";
            MonumentLineLayer = string.Empty;
            CenterlineLayer = string.Empty;
            MonumentLayer = string.Empty;
            MonumentTextLayer = string.Empty;
            DimensionLayer = string.Empty;
            RoadNameLayer = string.Empty;

            FoundFormat = "FND {desc}";
            SetFormat = "SET {desc}";
            OffsetFormat = "{dist}' OFFSET {dir} TO CORNER";
            DimensionFormat = "{width}' R/W";
            RoadNameFormat = "{name}";
            UpperCase = true;

            DrawPickedLine = true;
            DrawCenterline = true;
            LabelOppositeLine = true;
            LabelCenterline = false;
            DimensionPolicy = DimensionsEnds;

            MonumentRadiusPlotted = 0.04;
            TextHeightPlotted = 0.08;
            TextOffsetPlotted = 0.04;
            TextStyle = string.Empty;

            DefaultCornerRadiusFeet = 0.0;
            WidthToleranceFeet = 0.05;
        }

        /// <summary>True when every monument dimension is wanted, not only the run's ends.</summary>
        [JsonIgnore]
        public bool DimensionEveryMonument
        {
            get
            {
                return string.Equals((DimensionPolicy ?? string.Empty).Trim(), DimensionsEvery,
                                     StringComparison.OrdinalIgnoreCase);
            }
        }

        public void Validate(ICollection<string> problems)
        {
            const string label = "Right of way: ";

            if (string.IsNullOrWhiteSpace(LineTypeName))
                problems.Add(label + "name the Drafting Lines type the right-of-way lines use.");

            var policy = (DimensionPolicy ?? string.Empty).Trim();
            if (!string.Equals(policy, DimensionsEnds, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(policy, DimensionsEvery, StringComparison.OrdinalIgnoreCase))
                problems.Add(label + "dimension policy must be Ends or Every.");

            if (MonumentRadiusPlotted <= 0) problems.Add(label + "the monument symbol size must be greater than zero.");
            if (TextHeightPlotted <= 0) problems.Add(label + "text height must be greater than zero.");
            if (TextOffsetPlotted < 0) problems.Add(label + "the text offset cannot be negative.");
            if (DefaultCornerRadiusFeet < 0) problems.Add(label + "the default corner radius cannot be negative.");
            if (WidthToleranceFeet < 0) problems.Add(label + "the width tolerance cannot be negative.");

            if (string.IsNullOrWhiteSpace(DimensionFormat) || DimensionFormat.IndexOf("{width}", StringComparison.Ordinal) < 0)
                problems.Add(label + "the dimension format needs the {width} token.");
            if (string.IsNullOrWhiteSpace(RoadNameFormat) || RoadNameFormat.IndexOf("{name}", StringComparison.Ordinal) < 0)
                problems.Add(label + "the road name format needs the {name} token.");
        }
    }
}
