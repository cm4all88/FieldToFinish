using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace FieldCodes.Compare
{
    public sealed class BatchSite
    {
        [JsonProperty("name")] public string Name { get; set; }
        /// <summary>The survey drawing (a working copy; the original is never written).</summary>
        [JsonProperty("surveyDrawing")] public string SurveyDrawing { get; set; }
        /// <summary>The consultant base drawing. Empty means the survey drawing's own xrefs.</summary>
        [JsonProperty("consultantDrawing")] public string ConsultantDrawing { get; set; }
        [JsonProperty("profile")] public string Profile { get; set; }
        [JsonProperty("outputFolder")] public string OutputFolder { get; set; }
    }

    /// <summary>A list of sites to process the same way. JSON, written by hand or by the window.</summary>
    public sealed class BatchManifest
    {
        [JsonProperty("defaultProfile")] public string DefaultProfile { get; set; }
        [JsonProperty("defaultOutputFolder")] public string DefaultOutputFolder { get; set; }
        [JsonProperty("sites")] public List<BatchSite> Sites { get; set; }

        public BatchManifest() { Sites = new List<BatchSite>(); DefaultProfile = CompareProfile.DefaultName; }

        public string ProfileFor(BatchSite site) { return string.IsNullOrWhiteSpace(site.Profile) ? DefaultProfile : site.Profile; }
        public string OutputFolderFor(BatchSite site) { return string.IsNullOrWhiteSpace(site.OutputFolder) ? DefaultOutputFolder : site.OutputFolder; }

        public void Validate(ICollection<string> problems)
        {
            if (Sites == null || Sites.Count == 0) { problems.Add("The manifest lists no sites."); return; }
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var site in Sites)
            {
                var name = string.IsNullOrWhiteSpace(site.Name) ? Path.GetFileNameWithoutExtension(site.SurveyDrawing ?? string.Empty) : site.Name;
                if (string.IsNullOrWhiteSpace(name)) problems.Add("A site has no name and no survey drawing.");
                else if (!names.Add(name)) problems.Add("Site name '" + name + "' is used twice.");
                if (string.IsNullOrWhiteSpace(site.SurveyDrawing)) problems.Add("Site '" + name + "': no survey drawing.");
                if (string.IsNullOrWhiteSpace(OutputFolderFor(site))) problems.Add("Site '" + name + "': no output folder (set defaultOutputFolder).");
            }
        }

        public string ToJson() { return JsonConvert.SerializeObject(this, Formatting.Indented, CompareJson.Settings); }

        public static BatchManifest FromJson(string json)
        {
            BatchManifest m;
            try { m = JsonConvert.DeserializeObject<BatchManifest>(json, CompareJson.Settings); }
            catch (JsonException ex) { throw new ConfigException("Batch manifest is not valid JSON: " + ex.Message, ex); }
            if (m == null) throw new ConfigException("Batch manifest is empty.");
            if (m.Sites == null) m.Sites = new List<BatchSite>();
            if (string.IsNullOrWhiteSpace(m.DefaultProfile)) m.DefaultProfile = CompareProfile.DefaultName;
            return m;
        }

        public static BatchManifest Load(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("Batch manifest not found.", path);
            return FromJson(File.ReadAllText(path));
        }
    }

    public sealed class BatchSiteOutcome
    {
        public string Name { get; set; }
        public bool Succeeded { get; set; }
        /// <summary>True when the site has unresolved results: its output went to the review folder, not the approved one.</summary>
        public bool NeedsReview { get; set; }
        public string OutputPath { get; set; }
        public string ReportPath { get; set; }
        public ComparisonSummary Summary { get; set; }
        public string Error { get; set; }
        public double Seconds { get; set; }
        public int Applied { get; set; }
    }

    /// <summary>The batch summary CSV and text.</summary>
    public static class BatchSummary
    {
        public const string Header = "Site,Succeeded,NeedsReview,Matched,Missing,Partial,Discrepancies,NeedsReviewCount,NotEvaluated,Excluded,Applied,OutputPath,ReportPath,Seconds,Error";

        public static string RenderCsv(IEnumerable<BatchSiteOutcome> outcomes)
        {
            var sb = new StringBuilder();
            sb.AppendLine(Header);
            foreach (var o in outcomes ?? Enumerable.Empty<BatchSiteOutcome>())
            {
                var s = o.Summary ?? new ComparisonSummary();
                sb.AppendLine(string.Join(",", new[]
                {
                    o.Name, o.Succeeded ? "yes" : "no", o.NeedsReview ? "yes" : "no",
                    s.Matched.ToString(CultureInfo.InvariantCulture), s.Missing.ToString(CultureInfo.InvariantCulture), s.Partial.ToString(CultureInfo.InvariantCulture),
                    s.Discrepancies.ToString(CultureInfo.InvariantCulture), s.NeedsReview.ToString(CultureInfo.InvariantCulture), s.NotEvaluated.ToString(CultureInfo.InvariantCulture),
                    s.Excluded.ToString(CultureInfo.InvariantCulture), o.Applied.ToString(CultureInfo.InvariantCulture),
                    o.OutputPath ?? string.Empty, o.ReportPath ?? string.Empty, o.Seconds.ToString("0.0", CultureInfo.InvariantCulture), o.Error ?? string.Empty
                }.Select(CompareReport.Csv).ToArray()));
            }
            return sb.ToString();
        }

        public static string RenderText(IList<BatchSiteOutcome> outcomes)
        {
            outcomes = outcomes ?? new List<BatchSiteOutcome>();
            var lines = new List<string>();
            lines.Add(string.Format(CultureInfo.InvariantCulture, "Batch: {0} site(s), {1} approved, {2} need review, {3} failed",
                outcomes.Count, outcomes.Count(o => o.Succeeded && !o.NeedsReview), outcomes.Count(o => o.Succeeded && o.NeedsReview), outcomes.Count(o => !o.Succeeded)));
            foreach (var o in outcomes)
            {
                if (!o.Succeeded) { lines.Add("  FAILED   " + o.Name + ": " + o.Error); continue; }
                lines.Add((o.NeedsReview ? "  REVIEW   " : "  APPROVED ") + o.Name + ": " + (o.Summary != null ? o.Summary.ToString() : string.Empty) + " -> " + o.OutputPath);
            }
            return string.Join(Environment.NewLine, lines.ToArray());
        }
    }
}
