using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace FieldCodes.Settings
{
    /// <summary>
    /// One manually drafted survey line type -- Boundary, Right of Way, Section
    /// Line -- and the office standard FTFDRAWLINE applies to it. All types share
    /// one construction engine; only these standards differ.
    ///
    /// Layers are named here but never created: the drawing's own layer table is
    /// the authority, and a type whose layer is missing from the drawing is
    /// refused at draw time with an explanation, not silently invented.
    /// </summary>
    public sealed class DraftingLineType
    {
        /// <summary>Annotation value: bearing and distance computed from the drawn
        /// geometry.</summary>
        public const string AnnotationBearingDistance = "BearingDistance";
        /// <summary>Annotation value: the bearing alone, computed from the geometry.</summary>
        public const string AnnotationBearingOnly = "BearingOnly";
        /// <summary>Annotation value: the distance alone, computed from the geometry.</summary>
        public const string AnnotationDistanceOnly = "DistanceOnly";
        /// <summary>Annotation value: a fixed feature label such as "SECTION LINE",
        /// from <see cref="FeatureText"/>.</summary>
        public const string AnnotationFeatureText = "FeatureText";
        /// <summary>Annotation value: the line is drawn with no annotation.
        /// (A "CurveData" value is reserved for the curve milestone.)</summary>
        public const string AnnotationNone = "None";

        /// <summary>Bearing format value: quadrant form, N 42°18'36" E.</summary>
        public const string BearingFormatQuadrant = "QuadrantBearing";
        /// <summary>Bearing format value: whole-circle azimuth, 215°30'00".</summary>
        public const string BearingFormatAzimuth = "Azimuth";

        [JsonProperty("name")]
        public string Name { get; set; }

        /// <summary>Disabled types are not offered by FTFDRAWLINE.</summary>
        [JsonProperty("enabled")]
        public bool Enabled { get; set; }

        /// <summary>The office layer the line is drawn on. Empty means not yet
        /// configured, which FTFDRAWLINE reports rather than guessing.</summary>
        [JsonProperty("layer")]
        public string Layer { get; set; }

        /// <summary>Explicit linetype for the line entity. Empty leaves the entity
        /// at ByLayer, which is the normal office standard.</summary>
        [JsonProperty("linetype")]
        public string Linetype { get; set; }

        /// <summary>"BearingDistance", "BearingOnly", "DistanceOnly", "FeatureText"
        /// or "None". Different drafting lines carry different standards: a boundary
        /// normally shows bearing + distance, a section line may show only its name
        /// or nothing at all.</summary>
        [JsonProperty("annotation")]
        public string Annotation { get; set; }

        /// <summary>The fixed label for FeatureText annotation ("SECTION LINE").
        /// Required when that annotation type is chosen; never guessed from the
        /// type name.</summary>
        [JsonProperty("featureText")]
        public string FeatureText { get; set; }

        /// <summary>"QuadrantBearing" or "Azimuth": how a direction is written
        /// wherever this type reports one.</summary>
        [JsonProperty("bearingFormat")]
        public string BearingFormat { get; set; }

        /// <summary>Explicit layer for the annotation text. Empty means resolve it
        /// from the drawing the same way line labels do: the office-standard text
        /// layer in the line layer's own family, when one actually exists.</summary>
        [JsonProperty("annotationLayer")]
        public string AnnotationLayer { get; set; }

        /// <summary>Text style for the annotation. Empty means the drawing's
        /// current style.</summary>
        [JsonProperty("textStyle")]
        public string TextStyle { get; set; }

        /// <summary>Annotation text height as plotted.</summary>
        [JsonProperty("textHeightPlotted")]
        public double TextHeightPlotted { get; set; }

        /// <summary>Gap between the line and the nearest edge of the text, as
        /// plotted.</summary>
        [JsonProperty("offsetPlotted")]
        public double OffsetPlotted { get; set; }

        /// <summary>"Above", "Below" or "OnLine", in the text's own reading
        /// direction -- the readability flip never changes the reported bearing,
        /// and never moves the text to the other side. Ignored when
        /// <see cref="Stacked"/> is on.</summary>
        [JsonProperty("placement")]
        public string Placement { get; set; }

        /// <summary>Bearing above the line and distance below it, instead of one
        /// combined text.</summary>
        [JsonProperty("stacked")]
        public bool Stacked { get; set; }

        /// <summary>Wipeout under each annotation text.</summary>
        [JsonProperty("mask")]
        public bool Mask { get; set; }

        /// <summary>Decimal places on the bearing's seconds. 0 gives N 42°18'36" E.</summary>
        [JsonProperty("bearingSecondsDecimals")]
        public int BearingSecondsDecimals { get; set; }

        /// <summary>Decimal places on the distance. 2 gives 184.27.</summary>
        [JsonProperty("distanceDecimals")]
        public int DistanceDecimals { get; set; }

        /// <summary>Append the foot symbol to the distance: 184.27'.</summary>
        [JsonProperty("footSymbol")]
        public bool FootSymbol { get; set; }

        public DraftingLineType()
        {
            Enabled = true;
            Layer = string.Empty;
            Linetype = string.Empty;
            Annotation = AnnotationBearingDistance;
            FeatureText = string.Empty;
            BearingFormat = BearingFormatQuadrant;
            AnnotationLayer = string.Empty;
            TextStyle = string.Empty;
            TextHeightPlotted = 0.08;
            OffsetPlotted = 0.04;
            Placement = "Above";
            Stacked = false;
            Mask = false;
            BearingSecondsDecimals = 0;
            DistanceDecimals = 2;
            FootSymbol = true;
        }

        /// <summary>The canonical constant for this type's annotation value, or null
        /// when the value is not one of the known kinds.</summary>
        [JsonIgnore]
        public string AnnotationKind
        {
            get
            {
                var value = (Annotation ?? string.Empty).Trim();
                foreach (var known in new[]
                {
                    AnnotationNone, AnnotationBearingDistance, AnnotationBearingOnly,
                    AnnotationDistanceOnly, AnnotationFeatureText
                })
                {
                    if (string.Equals(value, known, StringComparison.OrdinalIgnoreCase))
                        return known;
                }
                return null;
            }
        }

        [JsonIgnore]
        public bool WantsAnnotation
        {
            get
            {
                var kind = AnnotationKind;
                return kind != null && kind != AnnotationNone;
            }
        }

        [JsonIgnore]
        public bool WantsAzimuthFormat
        {
            get
            {
                return string.Equals((BearingFormat ?? string.Empty).Trim(),
                                     BearingFormatAzimuth,
                                     StringComparison.OrdinalIgnoreCase);
            }
        }

        public DraftingLineType Clone()
        {
            return (DraftingLineType)MemberwiseClone();
        }
    }

    /// <summary>
    /// The manual survey drafting catalog for FTFDRAWLINE. Deliberately separate
    /// from Point Features and the line-label standards: those describe how FTF
    /// polishes geometry Civil 3D created, while these describe the small set of
    /// cadastral / record lines the user explicitly asks FTF to create.
    /// </summary>
    public sealed class DraftingSettings : ISettingsSection
    {
        public string Title { get { return "Drafting Lines"; } }
        public string AffectedCommands { get { return "FTFDRAWLINE, FTFDRAFTCLEAN"; } }

        /// <summary>Replace, not append: the constructor seeds the shipped catalog,
        /// and without this Newtonsoft would add a loaded file's types AFTER those
        /// seeds, doubling the list on every load.</summary>
        [JsonProperty("lineTypes", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<DraftingLineType> LineTypes { get; set; }

        public DraftingSettings() { RestoreDefaults(); }

        /// <summary>
        /// The shipped catalog: the usual cadastral types, every layer left empty on
        /// purpose. A plausible-looking guessed layer name would be worse than an
        /// explicit "not configured" -- the office standard is set once in the
        /// Drafting Lines settings page, from the drawing's real layer list.
        /// </summary>
        public void RestoreDefaults()
        {
            LineTypes = new List<DraftingLineType>
            {
                new DraftingLineType { Name = "Boundary" },
                new DraftingLineType { Name = "Right of Way" },
                new DraftingLineType { Name = "Centerline" },
                new DraftingLineType { Name = "Section Line" },
                new DraftingLineType { Name = "Quarter Section" },
                new DraftingLineType { Name = "Easement" },
                new DraftingLineType { Name = "Lot Line" },
                new DraftingLineType { Name = "Property Line" }
            };
        }

        /// <summary>The configured type by name, or null. Case-insensitive.</summary>
        public DraftingLineType FindType(string name)
        {
            if (LineTypes == null || string.IsNullOrWhiteSpace(name)) return null;
            foreach (var type in LineTypes)
            {
                if (type != null &&
                    string.Equals(type.Name, name, StringComparison.OrdinalIgnoreCase))
                    return type;
            }
            return null;
        }

        public void Validate(ICollection<string> problems)
        {
            if (LineTypes == null) LineTypes = new List<DraftingLineType>();

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var type in LineTypes)
            {
                if (type == null) continue;

                if (string.IsNullOrWhiteSpace(type.Name))
                {
                    problems.Add("Drafting lines: every line type needs a name.");
                    continue;
                }

                var label = "Drafting lines: " + type.Name + ": ";

                if (!seen.Add(type.Name.Trim()))
                    problems.Add(label + "the type name is used more than once.");

                var kind = type.AnnotationKind;
                if (kind == null)
                    problems.Add(label + "annotation must be BearingDistance, " +
                                 "BearingOnly, DistanceOnly, FeatureText or None.");

                // The feature label is what would be drawn; an enabled type cannot
                // point at an empty one. It is never derived from the type name.
                if (kind == DraftingLineType.AnnotationFeatureText && type.Enabled &&
                    string.IsNullOrWhiteSpace(type.FeatureText))
                    problems.Add(label + "FeatureText annotation needs the feature " +
                                 "text to draw.");

                var format = (type.BearingFormat ?? string.Empty).Trim();
                if (format.Length > 0 &&
                    !string.Equals(format, DraftingLineType.BearingFormatQuadrant,
                                   StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(format, DraftingLineType.BearingFormatAzimuth,
                                   StringComparison.OrdinalIgnoreCase))
                    problems.Add(label + "bearing format must be QuadrantBearing or " +
                                 "Azimuth.");

                if (type.TextHeightPlotted <= 0)
                    problems.Add(label + "text height must be greater than zero.");
                if (type.OffsetPlotted < 0)
                    problems.Add(label + "the annotation offset cannot be negative.");

                var placement = (type.Placement ?? string.Empty).Trim()
                    .Replace(" ", string.Empty);
                if (!placement.Equals("Above", StringComparison.OrdinalIgnoreCase) &&
                    !placement.Equals("Below", StringComparison.OrdinalIgnoreCase) &&
                    !placement.Equals("OnLine", StringComparison.OrdinalIgnoreCase))
                    problems.Add(label + "placement must be Above, Below or OnLine.");

                if (type.BearingSecondsDecimals < 0 || type.BearingSecondsDecimals > 3)
                    problems.Add(label + "bearing seconds decimals must be between 0 and 3.");
                if (type.DistanceDecimals < 0 || type.DistanceDecimals > 4)
                    problems.Add(label + "distance decimals must be between 0 and 4.");
            }
        }
    }
}
