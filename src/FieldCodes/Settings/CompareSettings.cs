using System.Collections.Generic;
using Newtonsoft.Json;

namespace FieldCodes.Settings
{
    /// <summary>
    /// How FTFCOMPARE behaves on this machine: which comparison profile is active, where
    /// deliverables go, and the review highlight colours. The mappings and tolerances
    /// themselves live in the comparison profile (one per consultant), not here.
    /// </summary>
    public sealed class CompareSettings : ISettingsSection
    {
        public string Title { get { return "Survey Comparison"; } }
        public string AffectedCommands { get { return "FTFCOMPARE, FTFCOMPARERUN, FTFCOMPAREAPPLY, FTFCOMPARESAVE, FTFCOMPAREBATCH"; } }

        /// <summary>Name of the comparison profile to use when the drawing has not chosen one.</summary>
        [JsonProperty("activeProfile")] public string ActiveProfile { get; set; }

        /// <summary>Where approved deliverables are saved. Empty asks each time (the profile's
        /// own deliveryFolder wins when set).</summary>
        [JsonProperty("deliveryFolder")] public string DeliveryFolder { get; set; }

        /// <summary>Appended to the drawing name on save: survey.dwg → survey-ACC.dwg.</summary>
        [JsonProperty("deliverySuffix")] public string DeliverySuffix { get; set; }

        /// <summary>Write the CSV report beside the drawing after every run.</summary>
        [JsonProperty("writeCsvReport")] public bool WriteCsvReport { get; set; }

        /// <summary>Apply confident automatic results without a click. Off means every
        /// change needs an explicit decision in the review window.</summary>
        [JsonProperty("applyAutomaticResults")] public bool ApplyAutomaticResults { get; set; }

        /// <summary>Xrefs: the consultant data is the survey drawing's external references.
        /// File: a separate drawing read without being attached.</summary>
        [JsonProperty("consultantSource")] public string ConsultantSource { get; set; }
        [JsonProperty("consultantFile")] public string ConsultantFile { get; set; }

        /// <summary>AutoCAD colour indexes for the temporary review highlight.</summary>
        [JsonProperty("highlightSurveyColor")] public short HighlightSurveyColor { get; set; }
        [JsonProperty("highlightConsultantColor")] public short HighlightConsultantColor { get; set; }

        /// <summary>Also keep the review decisions in a JSON file beside the drawing, so a
        /// copy of the drawing (or a re-run on a revised consultant base) reuses them.</summary>
        [JsonProperty("decisionsBesideDrawing")] public bool DecisionsBesideDrawing { get; set; }

        public CompareSettings() { RestoreDefaults(); }

        public void Validate(ICollection<string> problems)
        {
            if (HighlightSurveyColor < 1 || HighlightSurveyColor > 255) problems.Add("Survey highlight colour must be 1-255.");
            if (HighlightConsultantColor < 1 || HighlightConsultantColor > 255) problems.Add("Consultant highlight colour must be 1-255.");
            if (ConsultantSource != "Xrefs" && ConsultantSource != "File") problems.Add("Consultant source must be Xrefs or File.");
            if (string.IsNullOrWhiteSpace(ActiveProfile)) problems.Add("Active comparison profile needs a name.");
        }

        public void RestoreDefaults()
        {
            ActiveProfile = "Default";
            DeliveryFolder = string.Empty;
            DeliverySuffix = "-ACC";
            WriteCsvReport = true;
            ApplyAutomaticResults = true;
            ConsultantSource = "Xrefs";
            ConsultantFile = string.Empty;
            HighlightSurveyColor = 6;
            HighlightConsultantColor = 4;
            DecisionsBesideDrawing = true;
        }
    }
}
