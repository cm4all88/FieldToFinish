using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace FieldCodes.Settings
{
    /// <summary>
    /// How a tree is annotated. The three choices exclude each other: a tree gets a numbered
    /// tag (keyed to the schedule), or a descriptive label (18" FIR), or neither -- never both
    /// by default, which is what the plan used to end up with.
    /// </summary>
    public enum TreeAnnotation
    {
        /// <summary>The descriptive label only (species and size from the description).</summary>
        DescriptionOnly = 0,
        /// <summary>The numbered tag only (T12); the description goes in the schedule if one is drawn.</summary>
        TagOnly = 1,
        /// <summary>No tree annotation: the symbol and dripline stand on their own.</summary>
        None = 2
    }

    /// <summary>
    /// Feature families Label All (and the label and tag stages of Process Drawing) can be told
    /// to process or leave alone. A point belongs to exactly one.
    /// </summary>
    public enum AnnotationCategory
    {
        Trees = 0,
        /// <summary>Poles, light poles, hydrants, valves, guy anchors, junction boxes, vaults.</summary>
        Utilities = 1,
        /// <summary>Storm and sewer structures -- manholes, catch basins, storm drains -- whose
        /// dips the Dip Builder records and labels.</summary>
        StormSewer = 2,
        Signs = 3,
        Control = 4,
        /// <summary>Pavement markings and words.</summary>
        Pavement = 5,
        /// <summary>Everything else with a label rule: mailboxes, posts, bollards...</summary>
        OtherPoints = 6,
        /// <summary>Linework labels (the whole-drawing line pass).</summary>
        Lines = 7
    }

    /// <summary>
    /// Settings > Label All: which feature families are annotated, how trees are annotated,
    /// and what is never labelled. Read by FTFLABELALL, FTFLABELS, FTFTAGS, FTFTABLE and the
    /// Process Drawing presets.
    /// </summary>
    public sealed class AnnotationSettings : ISettingsSection
    {
        public string Title { get { return "Label All"; } }
        public string AffectedCommands { get { return "FTFLABELALL, FTFLABELS, FTFTAGS, FTFTABLE, FTFRUN"; } }

        /// <summary>Families annotated. A family not listed is left alone: no new labels, and
        /// labels FTF drew there before are kept, not erased.</summary>
        [JsonProperty("categories", ObjectCreationHandling = ObjectCreationHandling.Replace, ItemConverterType = typeof(StringEnumConverter))]
        public List<AnnotationCategory> Categories { get; set; }

        [JsonProperty("treeAnnotation")]
        [JsonConverter(typeof(StringEnumConverter))]
        public TreeAnnotation TreeAnnotation { get; set; }

        /// <summary>Draw the tree schedule. Independent of the tree annotation: with descriptive
        /// labels the schedule is keyed by point number instead of tag.</summary>
        [JsonProperty("scheduleTrees")]
        public bool ScheduleTrees { get; set; }

        /// <summary>Notes that are utility depth measurements (dips: "12 RCP N 6.41", "MD 6.25",
        /// "BOT 7.82", "WL 6.9") are not used as label text: the Dip Builder owns those numbers.
        /// The point is still labelled from its code when its family is on.</summary>
        [JsonProperty("excludeDepthNotes")]
        public bool ExcludeDepthNotes { get; set; }

        /// <summary>Words in a description or note that mean "do not label this point".</summary>
        [JsonProperty("noLabelWords", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<string> NoLabelWords { get; set; }

        /// <summary>A point that already has a label someone typed (not FTF's) within this
        /// distance, in plotted inches, saying the same thing is not labelled again.</summary>
        [JsonProperty("existingLabelSearchPlotted")]
        public double ExistingLabelSearchPlotted { get; set; }

        [JsonProperty("respectManualLabels")]
        public bool RespectManualLabels { get; set; }

        public AnnotationSettings() { RestoreDefaults(); }

        public static IList<AnnotationCategory> DefaultCategories()
        {
            // Storm and sewer structures are left to the Dip Builder by default.
            return new List<AnnotationCategory>
            {
                AnnotationCategory.Trees, AnnotationCategory.Utilities, AnnotationCategory.Signs,
                AnnotationCategory.Control, AnnotationCategory.Pavement, AnnotationCategory.OtherPoints,
                AnnotationCategory.Lines
            };
        }

        public void RestoreDefaults()
        {
            Categories = new List<AnnotationCategory>(DefaultCategories());
            TreeAnnotation = TreeAnnotation.DescriptionOnly;
            ScheduleTrees = false;
            ExcludeDepthNotes = true;
            NoLabelWords = new List<string> { "NOLABEL", "NOLBL", "NO-LABEL" };
            ExistingLabelSearchPlotted = 0.5;
            RespectManualLabels = true;
        }

        public bool Includes(AnnotationCategory category)
        {
            return Categories != null && Categories.Contains(category);
        }

        public void Validate(ICollection<string> problems)
        {
            if (Categories == null) Categories = new List<AnnotationCategory>(DefaultCategories());
            Categories = Categories.Distinct().ToList();
            if (NoLabelWords == null) NoLabelWords = new List<string>();
            if (!Enum.IsDefined(typeof(TreeAnnotation), TreeAnnotation))
                problems.Add("Label All: the tree annotation must be tag only, description only or none.");
            Require.NotNegative(ExistingLabelSearchPlotted,
                "Label All: the search distance for labels already on the plan cannot be negative.", problems);
        }
    }
}
