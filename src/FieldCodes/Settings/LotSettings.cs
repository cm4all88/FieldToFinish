using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace FieldCodes.Settings
{
    /// <summary>
    /// The office standard for FTFLOTS: dividing a platted block into lots.
    ///
    /// Lot lines and the alley take the layer, linetype and bearing/distance format of Drafting
    /// Lines types named here (Lot Line, and Right of Way for the alley), so FTFLOTS and
    /// FTFDRAWLINE can never disagree. The rest of what FTFLOTS draws has its layer here, and an
    /// empty layer is refused at draw time rather than guessed. The values the popup starts from
    /// (setbacks, easements, slope, minimums) are the office's to set; the shipped defaults are
    /// zero or common practice, not any jurisdiction's ordinance.
    /// </summary>
    public sealed class LotSettings : ISettingsSection
    {
        public string Title { get { return "Platted Blocks"; } }
        public string AffectedCommands { get { return "FTFLOTS"; } }

        [JsonProperty("lotLineTypeName")] public string LotLineTypeName { get; set; }
        [JsonProperty("alleyTypeName")] public string AlleyTypeName { get; set; }

        [JsonProperty("setbackLayer")] public string SetbackLayer { get; set; }
        [JsonProperty("easementLayer")] public string EasementLayer { get; set; }
        [JsonProperty("lotTextLayer")] public string LotTextLayer { get; set; }
        [JsonProperty("drainLayer")] public string DrainLayer { get; set; }
        [JsonProperty("notesLayer")] public string NotesLayer { get; set; }

        /// <summary>Draw the block's own boundary again as lot lines. Off by default: FTFROW draws it.</summary>
        [JsonProperty("drawBlockPerimeter")] public bool DrawBlockPerimeter { get; set; }

        /// <summary>Tokens: {num}, {block}.</summary>
        [JsonProperty("lotNumberFormat")] public string LotNumberFormat { get; set; }

        /// <summary>Tokens: {sf}. Empty omits the line.</summary>
        [JsonProperty("lotAreaFormat")] public string LotAreaFormat { get; set; }

        /// <summary>Tokens: {ac}. Empty omits the line.</summary>
        [JsonProperty("lotAcresFormat")] public string LotAcresFormat { get; set; }

        /// <summary>Tokens: {block}. Empty omits the title.</summary>
        [JsonProperty("blockTitleFormat")] public string BlockTitleFormat { get; set; }

        /// <summary>Tokens: {width}.</summary>
        [JsonProperty("alleyFormat")] public string AlleyFormat { get; set; }

        /// <summary>Tokens: {pct}.</summary>
        [JsonProperty("drainFormat")] public string DrainFormat { get; set; }

        [JsonProperty("setbackNotePrefix")] public string SetbackNotePrefix { get; set; }
        [JsonProperty("easementNotePrefix")] public string EasementNotePrefix { get; set; }
        [JsonProperty("upperCase")] public bool UpperCase { get; set; }

        [JsonProperty("textHeightPlotted")] public double TextHeightPlotted { get; set; }
        [JsonProperty("textOffsetPlotted")] public double TextOffsetPlotted { get; set; }
        [JsonProperty("textStyle")] public string TextStyle { get; set; }

        // What the popup starts from.
        [JsonProperty("defaultSlopePercent")] public double DefaultSlopePercent { get; set; }
        [JsonProperty("defaultAlleyWidthFeet")] public double DefaultAlleyWidthFeet { get; set; }
        [JsonProperty("defaultFrontSetbackFeet")] public double DefaultFrontSetbackFeet { get; set; }
        [JsonProperty("defaultSideSetbackFeet")] public double DefaultSideSetbackFeet { get; set; }
        [JsonProperty("defaultCornerSideSetbackFeet")] public double DefaultCornerSideSetbackFeet { get; set; }
        [JsonProperty("defaultRearSetbackFeet")] public double DefaultRearSetbackFeet { get; set; }
        [JsonProperty("defaultFrontEasementFeet")] public double DefaultFrontEasementFeet { get; set; }
        [JsonProperty("defaultSideEasementFeet")] public double DefaultSideEasementFeet { get; set; }
        [JsonProperty("defaultRearEasementFeet")] public double DefaultRearEasementFeet { get; set; }
        [JsonProperty("minimumLotAreaSqFt")] public double MinimumLotAreaSqFt { get; set; }
        [JsonProperty("minimumLotWidthFeet")] public double MinimumLotWidthFeet { get; set; }

        public LotSettings() { RestoreDefaults(); }

        public void RestoreDefaults()
        {
            LotLineTypeName = "Lot Line";
            AlleyTypeName = "Right of Way";
            SetbackLayer = string.Empty;
            EasementLayer = string.Empty;
            LotTextLayer = string.Empty;
            DrainLayer = string.Empty;
            NotesLayer = string.Empty;
            DrawBlockPerimeter = false;

            LotNumberFormat = "LOT {num}";
            LotAreaFormat = "{sf} SF";
            LotAcresFormat = string.Empty;
            BlockTitleFormat = "BLOCK {block}";
            AlleyFormat = "{width}' ALLEY";
            DrainFormat = "{pct}%";
            SetbackNotePrefix = "BUILDING SETBACKS:";
            EasementNotePrefix = "UTILITY EASEMENTS:";
            UpperCase = true;

            TextHeightPlotted = 0.08;
            TextOffsetPlotted = 0.04;
            TextStyle = string.Empty;

            DefaultSlopePercent = 2.0;
            DefaultAlleyWidthFeet = 20.0;
            DefaultFrontSetbackFeet = 0.0;
            DefaultSideSetbackFeet = 0.0;
            DefaultCornerSideSetbackFeet = 0.0;
            DefaultRearSetbackFeet = 0.0;
            DefaultFrontEasementFeet = 0.0;
            DefaultSideEasementFeet = 0.0;
            DefaultRearEasementFeet = 0.0;
            MinimumLotAreaSqFt = 0.0;
            MinimumLotWidthFeet = 0.0;
        }

        public void Validate(ICollection<string> problems)
        {
            const string label = "Platted blocks: ";
            if (string.IsNullOrWhiteSpace(LotLineTypeName)) problems.Add(label + "name the Drafting Lines type the lot lines use.");
            if (string.IsNullOrWhiteSpace(AlleyTypeName)) problems.Add(label + "name the Drafting Lines type the alley uses.");
            if (TextHeightPlotted <= 0) problems.Add(label + "text height must be greater than zero.");
            if (TextOffsetPlotted < 0) problems.Add(label + "the text offset cannot be negative.");
            if (DefaultSlopePercent < 0) problems.Add(label + "the slope cannot be negative.");

            foreach (var value in new[]
            {
                DefaultAlleyWidthFeet, DefaultFrontSetbackFeet, DefaultSideSetbackFeet, DefaultCornerSideSetbackFeet,
                DefaultRearSetbackFeet, DefaultFrontEasementFeet, DefaultSideEasementFeet, DefaultRearEasementFeet,
                MinimumLotAreaSqFt, MinimumLotWidthFeet
            })
            {
                if (value < 0) { problems.Add(label + "setbacks, easements, the alley width and the minimums cannot be negative."); break; }
            }

            if (!string.IsNullOrEmpty(AlleyFormat) && AlleyFormat.IndexOf("{width}", StringComparison.Ordinal) < 0)
                problems.Add(label + "the alley format needs the {width} token.");
            if (!string.IsNullOrEmpty(DrainFormat) && DrainFormat.IndexOf("{pct}", StringComparison.Ordinal) < 0)
                problems.Add(label + "the drainage format needs the {pct} token.");
        }
    }
}
