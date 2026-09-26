using Newtonsoft.Json;
using FieldCodes.Settings;

namespace FieldCodes.Easements
{
    /// <summary>
    /// What the drafter chose in the preview for ONE easement: hatch, labels, width
    /// dimensions, and the layers each piece goes on.
    ///
    /// Every field is optional. Null means "whatever the office profile says", so a
    /// later change to the profile still flows through to a rebuild; only what the
    /// drafter actually changed is stored with the easement. FTF never invents an
    /// office standard here -- the preview starts from the profile and records the
    /// difference.
    /// </summary>
    public sealed class EasementDrafting
    {
        [JsonProperty("drawHatch", NullValueHandling = NullValueHandling.Ignore)] public bool? DrawHatch { get; set; }
        [JsonProperty("hatchPattern", NullValueHandling = NullValueHandling.Ignore)] public string HatchPattern { get; set; }
        [JsonProperty("hatchScale", NullValueHandling = NullValueHandling.Ignore)] public double? HatchScale { get; set; }

        [JsonProperty("drawCenterline", NullValueHandling = NullValueHandling.Ignore)] public bool? DrawCenterline { get; set; }
        [JsonProperty("drawSidelines", NullValueHandling = NullValueHandling.Ignore)] public bool? DrawSidelines { get; set; }

        [JsonProperty("labelCenterline", NullValueHandling = NullValueHandling.Ignore)] public bool? LabelCenterline { get; set; }
        [JsonProperty("labelMode", NullValueHandling = NullValueHandling.Ignore)] public EasementLabelMode? LabelMode { get; set; }

        [JsonProperty("drawWidthDimensions", NullValueHandling = NullValueHandling.Ignore)] public bool? DrawWidthDimensions { get; set; }
        [JsonProperty("drawPointLabels", NullValueHandling = NullValueHandling.Ignore)] public bool? DrawPointLabels { get; set; }

        [JsonProperty("boundaryLayer", NullValueHandling = NullValueHandling.Ignore)] public string BoundaryLayer { get; set; }
        [JsonProperty("hatchLayer", NullValueHandling = NullValueHandling.Ignore)] public string HatchLayer { get; set; }
        [JsonProperty("textLayer", NullValueHandling = NullValueHandling.Ignore)] public string TextLayer { get; set; }
        [JsonProperty("dimensionLayer", NullValueHandling = NullValueHandling.Ignore)] public string DimensionLayer { get; set; }
        [JsonProperty("centerlineLayer", NullValueHandling = NullValueHandling.Ignore)] public string CenterlineLayer { get; set; }
        [JsonProperty("sidelineLayer", NullValueHandling = NullValueHandling.Ignore)] public string SidelineLayer { get; set; }

        /// <summary>A temporary construction easement's own hatch and layers.</summary>
        [JsonProperty("temporaryHatchPattern", NullValueHandling = NullValueHandling.Ignore)] public string TemporaryHatchPattern { get; set; }
        [JsonProperty("temporaryLayer", NullValueHandling = NullValueHandling.Ignore)] public string TemporaryLayer { get; set; }
        [JsonProperty("temporaryHatchLayer", NullValueHandling = NullValueHandling.Ignore)] public string TemporaryHatchLayer { get; set; }
        [JsonProperty("temporaryTextLayer", NullValueHandling = NullValueHandling.Ignore)] public string TemporaryTextLayer { get; set; }
        [JsonProperty("temporaryDimensionLayer", NullValueHandling = NullValueHandling.Ignore)] public string TemporaryDimensionLayer { get; set; }

        /// <summary>True when nothing was changed from the profile, so nothing needs storing.</summary>
        [JsonIgnore]
        public bool IsEmpty
        {
            get
            {
                return !DrawHatch.HasValue && HatchPattern == null && !HatchScale.HasValue &&
                       !DrawCenterline.HasValue && !DrawSidelines.HasValue &&
                       !LabelCenterline.HasValue && !LabelMode.HasValue &&
                       !DrawWidthDimensions.HasValue && !DrawPointLabels.HasValue &&
                       BoundaryLayer == null && HatchLayer == null && TextLayer == null &&
                       DimensionLayer == null && CenterlineLayer == null && SidelineLayer == null &&
                       TemporaryHatchPattern == null && TemporaryLayer == null && TemporaryHatchLayer == null &&
                       TemporaryTextLayer == null && TemporaryDimensionLayer == null;
            }
        }

        /// <summary>
        /// The office settings with this easement's choices laid over them. The office
        /// settings themselves are never changed: a choice made for one easement must
        /// not follow the drafter into the next command.
        /// </summary>
        public EasementSettings ApplyTo(EasementSettings office)
        {
            if (office == null) return null;
            var result = office.Copy();
            if (DrawHatch.HasValue) result.DrawHatch = DrawHatch.Value;
            if (HatchPattern != null) result.HatchPattern = HatchPattern;
            if (HatchScale.HasValue) result.HatchScale = HatchScale.Value;
            if (DrawCenterline.HasValue) result.DrawCenterline = DrawCenterline.Value;
            if (DrawSidelines.HasValue) result.DrawSidelines = DrawSidelines.Value;
            if (LabelCenterline.HasValue) result.LabelCenterline = LabelCenterline.Value;
            if (LabelMode.HasValue) result.LabelMode = LabelMode.Value;
            if (DrawWidthDimensions.HasValue) result.DrawWidthDimensions = DrawWidthDimensions.Value;
            if (DrawPointLabels.HasValue) result.DrawPointLabels = DrawPointLabels.Value;
            if (BoundaryLayer != null) result.BoundaryLayer = BoundaryLayer;
            if (HatchLayer != null) result.HatchLayer = HatchLayer;
            if (TextLayer != null) result.TextLayer = TextLayer;
            if (DimensionLayer != null) result.DimensionLayer = DimensionLayer;
            if (CenterlineLayer != null) result.CenterlineLayer = CenterlineLayer;
            if (SidelineLayer != null) result.SidelineLayer = SidelineLayer;
            if (TemporaryHatchPattern != null) result.TemporaryHatchPattern = TemporaryHatchPattern;
            if (TemporaryLayer != null) result.TemporaryLayer = TemporaryLayer;
            if (TemporaryHatchLayer != null) result.TemporaryHatchLayer = TemporaryHatchLayer;
            if (TemporaryTextLayer != null) result.TemporaryTextLayer = TemporaryTextLayer;
            if (TemporaryDimensionLayer != null) result.TemporaryDimensionLayer = TemporaryDimensionLayer;
            return result;
        }

        /// <summary>
        /// What the drafter changed: every field that differs between the office settings
        /// and the ones the preview ended with. Returns null when nothing differs.
        /// </summary>
        public static EasementDrafting Difference(EasementSettings office, EasementSettings chosen)
        {
            if (office == null || chosen == null) return null;
            var d = new EasementDrafting();
            if (chosen.DrawHatch != office.DrawHatch) d.DrawHatch = chosen.DrawHatch;
            if (!Same(chosen.HatchPattern, office.HatchPattern)) d.HatchPattern = chosen.HatchPattern ?? string.Empty;
            if (chosen.HatchScale != office.HatchScale) d.HatchScale = chosen.HatchScale;
            if (chosen.DrawCenterline != office.DrawCenterline) d.DrawCenterline = chosen.DrawCenterline;
            if (chosen.DrawSidelines != office.DrawSidelines) d.DrawSidelines = chosen.DrawSidelines;
            if (chosen.LabelCenterline != office.LabelCenterline) d.LabelCenterline = chosen.LabelCenterline;
            if (chosen.LabelMode != office.LabelMode) d.LabelMode = chosen.LabelMode;
            if (chosen.DrawWidthDimensions != office.DrawWidthDimensions) d.DrawWidthDimensions = chosen.DrawWidthDimensions;
            if (chosen.DrawPointLabels != office.DrawPointLabels) d.DrawPointLabels = chosen.DrawPointLabels;
            if (!Same(chosen.BoundaryLayer, office.BoundaryLayer)) d.BoundaryLayer = chosen.BoundaryLayer ?? string.Empty;
            if (!Same(chosen.HatchLayer, office.HatchLayer)) d.HatchLayer = chosen.HatchLayer ?? string.Empty;
            if (!Same(chosen.TextLayer, office.TextLayer)) d.TextLayer = chosen.TextLayer ?? string.Empty;
            if (!Same(chosen.DimensionLayer, office.DimensionLayer)) d.DimensionLayer = chosen.DimensionLayer ?? string.Empty;
            if (!Same(chosen.CenterlineLayer, office.CenterlineLayer)) d.CenterlineLayer = chosen.CenterlineLayer ?? string.Empty;
            if (!Same(chosen.SidelineLayer, office.SidelineLayer)) d.SidelineLayer = chosen.SidelineLayer ?? string.Empty;
            if (!Same(chosen.TemporaryHatchPattern, office.TemporaryHatchPattern)) d.TemporaryHatchPattern = chosen.TemporaryHatchPattern ?? string.Empty;
            if (!Same(chosen.TemporaryLayer, office.TemporaryLayer)) d.TemporaryLayer = chosen.TemporaryLayer ?? string.Empty;
            if (!Same(chosen.TemporaryHatchLayer, office.TemporaryHatchLayer)) d.TemporaryHatchLayer = chosen.TemporaryHatchLayer ?? string.Empty;
            if (!Same(chosen.TemporaryTextLayer, office.TemporaryTextLayer)) d.TemporaryTextLayer = chosen.TemporaryTextLayer ?? string.Empty;
            if (!Same(chosen.TemporaryDimensionLayer, office.TemporaryDimensionLayer)) d.TemporaryDimensionLayer = chosen.TemporaryDimensionLayer ?? string.Empty;
            return d.IsEmpty ? null : d;
        }

        private static bool Same(string a, string b)
        {
            return string.Equals(a ?? string.Empty, b ?? string.Empty, System.StringComparison.Ordinal);
        }
    }
}
