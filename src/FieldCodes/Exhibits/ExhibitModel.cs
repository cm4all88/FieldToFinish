using System;
using System.Collections.Generic;
using FieldCodes.Easements;
using Newtonsoft.Json;

namespace FieldCodes.Exhibits
{
    /// <summary>What the drafter typed for an exhibit. Anything blank is left off the sheet.</summary>
    public sealed class ExhibitInfo
    {
        [JsonProperty("title")] public string Title { get; set; }
        [JsonProperty("location")] public string Location { get; set; }
        [JsonProperty("project")] public string Project { get; set; }
        [JsonProperty("parcel")] public string Parcel { get; set; }
        [JsonProperty("owner")] public string Owner { get; set; }
        [JsonProperty("apn")] public string Apn { get; set; }
        [JsonProperty("county")] public string County { get; set; }
        [JsonProperty("purpose")] public string Purpose { get; set; }
        [JsonProperty("sheet")] public string Sheet { get; set; }
        [JsonProperty("preparedBy")] public string PreparedBy { get; set; }
        [JsonProperty("date")] public string Date { get; set; }
        [JsonProperty("projectNumber")] public string ProjectNumber { get; set; }
        [JsonProperty("client")] public string Client { get; set; }
        [JsonProperty("checkedBy")] public string CheckedBy { get; set; }
        [JsonProperty("revision")] public string Revision { get; set; }

        public ExhibitInfo Copy() { return (ExhibitInfo)MemberwiseClone(); }

        /// <summary>Every field by its template token. {sheetNo} and {sheetOf} come from a sheet like "2 OF 3".</summary>
        public Dictionary<string, string> Tokens()
        {
            string sheetNo = Sheet, sheetOf = null;
            var parts = (Sheet ?? string.Empty).ToUpperInvariant().Split(new[] { " OF " }, StringSplitOptions.None);
            if (parts.Length == 2) { sheetNo = parts[0].Trim(); sheetOf = parts[1].Trim(); }
            return new Dictionary<string, string>
            {
                { "{title}", Title }, { "{location}", Location }, { "{project}", Project }, { "{parcel}", Parcel },
                { "{owner}", Owner }, { "{apn}", Apn }, { "{county}", County }, { "{purpose}", Purpose },
                { "{sheet}", Sheet }, { "{preparedBy}", PreparedBy }, { "{date}", Date },
                { "{projectNumber}", ProjectNumber }, { "{client}", Client }, { "{checkedBy}", CheckedBy }, { "{revision}", Revision },
                { "{sheetNo}", sheetNo }, { "{sheetOf}", sheetOf }
            };
        }

        /// <summary>Fills {title}, {county}... in a template; a line that only had blanks in it comes back empty.</summary>
        public string Fill(string template)
        {
            if (string.IsNullOrEmpty(template)) return string.Empty;
            var values = Tokens();
            var anyToken = false;
            var anyValue = false;
            var text = template;
            foreach (var kv in values)
            {
                if (!text.Contains(kv.Key)) continue;
                anyToken = true;
                if (!string.IsNullOrWhiteSpace(kv.Value)) anyValue = true;
                text = text.Replace(kv.Key, (kv.Value ?? string.Empty).Trim().ToUpperInvariant());
            }
            return anyToken && !anyValue ? string.Empty : text.Trim();
        }
    }

    /// <summary>
    /// One thing FTF put on the exhibit, remembered so a rebuild can tell whether the drafter
    /// moved it, edited it or erased it.
    /// </summary>
    public sealed class ExhibitItem
    {
        /// <summary>Stable key: TITLE, NORTH, SCALEBAR, LEGEND, AREATABLE, LINETABLE, NOTES, INFO, BORDER,
        /// LABEL:{easement}:{n}, POINT:{easement}:POB, DIM:{easement}:{n}...</summary>
        [JsonProperty("key")] public string Key { get; set; }
        [JsonProperty("kind")] public string Kind { get; set; }
        [JsonProperty("handle")] public string Handle { get; set; }
        /// <summary>The text FTF wrote, to spot hand edits.</summary>
        [JsonProperty("text")] public string Text { get; set; }
        [JsonProperty("x")] public double X { get; set; }
        [JsonProperty("y")] public double Y { get; set; }
        /// <summary>Moves by hand are kept on a rebuild (tables, title, notes, legend, north arrow...).</summary>
        [JsonProperty("keepPosition")] public bool KeepPosition { get; set; }
    }

    /// <summary>Which easement an exhibit shows, and what it looked like when the exhibit was built.</summary>
    public sealed class ExhibitSource
    {
        [JsonProperty("easement")] public string EasementId { get; set; }
        [JsonProperty("title")] public string Title { get; set; }
        [JsonProperty("fingerprint")] public string Fingerprint { get; set; }
    }

    /// <summary>One thing the drafter should look at on the sheet.</summary>
    public sealed class ExhibitReviewItem
    {
        [JsonProperty("severity")] public string Severity { get; set; }
        [JsonProperty("item")] public string ItemKey { get; set; }
        [JsonProperty("message")] public string Message { get; set; }

        public override string ToString() { return Severity + ": " + Message; }
    }

    /// <summary>An easement exhibit: a paper-space layout built from FTF easement records.</summary>
    public sealed class ExhibitRecord
    {
        [JsonProperty("schema")] public string Schema { get; set; }
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("layout")] public string LayoutName { get; set; }
        [JsonProperty("profile")] public string ProfileName { get; set; }
        [JsonProperty("createdUtc")] public DateTime CreatedUtc { get; set; }
        [JsonProperty("rebuiltUtc")] public DateTime? RebuiltUtc { get; set; }
        [JsonProperty("info")] public ExhibitInfo Info { get; set; }
        [JsonProperty("sources")] public List<ExhibitSource> Sources { get; set; }

        [JsonProperty("viewport")] public string ViewportHandle { get; set; }
        /// <summary>Feet per paper inch.</summary>
        [JsonProperty("scale")] public double Scale { get; set; }
        /// <summary>View rotation in degrees (the view only; survey geometry is never rotated).</summary>
        [JsonProperty("rotation")] public double RotationDegrees { get; set; }
        [JsonProperty("viewCenter")] public P2 ViewCenter { get; set; }
        [JsonProperty("scaleWasChosen")] public bool ScaleChosenByUser { get; set; }
        [JsonProperty("rotateAllowed")] public bool RotateAllowed { get; set; }

        [JsonProperty("items")] public List<ExhibitItem> Items { get; set; }
        [JsonProperty("review")] public List<ExhibitReviewItem> Review { get; set; }

        /// <summary>Viewport layer states FTF set at the last build: layer name, frozen in this viewport.</summary>
        [JsonProperty("viewportLayers")] public Dictionary<string, bool> ViewportLayers { get; set; }
        /// <summary>Layers whose viewport state the drafter changed by hand; FTF leaves them alone from then on.</summary>
        [JsonProperty("userLayers")] public List<string> UserLayers { get; set; }
        /// <summary>For narrow strips when the profile says Ask: easement id, whether its label has a leader.</summary>
        [JsonProperty("labelLeaders")] public Dictionary<string, bool> LabelLeaders { get; set; }
        /// <summary>When FTFEXHIBITQA last ran, and what it found.</summary>
        [JsonProperty("qaUtc")] public DateTime? QaUtc { get; set; }
        [JsonProperty("qaSummary")] public string QaSummary { get; set; }

        /// <summary>This exhibit's own choice for overhead power (Show, Hide, User); null follows the profile.</summary>
        [JsonProperty("overheadPower")] public string OverheadPower { get; set; }

        /// <summary>This exhibit's own choice for other exhibits' hatches (Show, Hide, Relevant, User); null follows the profile.</summary>
        [JsonProperty("otherHatches")] public string OtherHatches { get; set; }

        /// <summary>The stamp block the surveyor chose with FTFEXHIBITSTAMP; null until chosen. FTF never chooses one.</summary>
        [JsonProperty("stampBlock")] public string StampBlock { get; set; }

        /// <summary>Whether the north arrow shown was checked against the viewport's turn on the last build.</summary>
        [JsonProperty("northVerified")] public bool NorthArrowVerified { get; set; }

        public ExhibitRecord()
        {
            Schema = "ftf-exhibit-1";
            Id = Guid.NewGuid().ToString("N");
            CreatedUtc = DateTime.UtcNow;
            Info = new ExhibitInfo();
            Sources = new List<ExhibitSource>();
            Items = new List<ExhibitItem>();
            Review = new List<ExhibitReviewItem>();
            ViewportLayers = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            UserLayers = new List<string>();
            LabelLeaders = new Dictionary<string, bool>();
        }

        public string ToJson() { return JsonConvert.SerializeObject(this, Formatting.None); }
        public static ExhibitRecord FromJson(string json) { return JsonConvert.DeserializeObject<ExhibitRecord>(json); }
    }
}
