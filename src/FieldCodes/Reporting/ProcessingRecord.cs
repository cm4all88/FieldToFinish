using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace FieldCodes.Reporting
{
    /// <summary>How the last processing run ended.</summary>
    public enum ProcessingOutcome
    {
        /// <summary>No stage ran (nothing selected, or no record).</summary>
        NothingRun = 0,
        /// <summary>Every stage that ran completed.</summary>
        Succeeded = 1,
        /// <summary>Some stages failed; the rest completed.</summary>
        CompletedWithFailures = 2,
        /// <summary>A stage failed and the run stopped there.</summary>
        StoppedEarly = 3
    }

    /// <summary>One stage of a recorded run.</summary>
    public sealed class ProcessingStage
    {
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("ok")] public bool Succeeded { get; set; }
        [JsonProperty("error", NullValueHandling = NullValueHandling.Ignore)] public string Error { get; set; }
    }

    /// <summary>
    /// The last processing run, stored INSIDE the drawing so "when was this drawing
    /// last finished, and did it work" survives closing it -- the window's own log
    /// does not. Small on purpose: when, what ran, how it ended, and the point counts
    /// at that moment.
    ///
    /// No Autodesk types; the CAD layer stores the JSON in the drawing's FTF
    /// dictionary beside the profile name.
    /// </summary>
    public sealed class ProcessingRecord
    {
        [JsonProperty("version")] public int Version { get; set; }

        /// <summary>When the run finished, UTC.</summary>
        [JsonProperty("finishedUtc")] public DateTime FinishedUtc { get; set; }

        /// <summary>What started it: "FTF window", "FTFRUN".</summary>
        [JsonProperty("source")] public string Source { get; set; }

        /// <summary>Who ran it (the Windows user name), when known.</summary>
        [JsonProperty("user", NullValueHandling = NullValueHandling.Ignore)] public string User { get; set; }

        [JsonProperty("stages", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<ProcessingStage> Stages { get; set; }

        [JsonProperty("stoppedEarly")] public bool StoppedEarly { get; set; }

        // point counts straight after the run
        [JsonProperty("points")] public int Points { get; set; }
        [JsonProperty("processed")] public int Processed { get; set; }
        [JsonProperty("errors")] public int Errors { get; set; }
        [JsonProperty("needsConfiguration")] public int NeedsConfiguration { get; set; }
        [JsonProperty("skipped")] public int Skipped { get; set; }

        /// <summary>FTF-owned objects in the drawing after the run.</summary>
        [JsonProperty("ownedObjects")] public int OwnedObjects { get; set; }

        // point accountability straight after the run (null on runs before it existed)
        [JsonProperty("accountedFor", NullValueHandling = NullValueHandling.Ignore)] public int? AccountedFor { get; set; }
        [JsonProperty("forReview", NullValueHandling = NullValueHandling.Ignore)] public int? ForReview { get; set; }
        [JsonProperty("lineworkItems", NullValueHandling = NullValueHandling.Ignore)] public int? LineworkItems { get; set; }
        [JsonProperty("accountability", NullValueHandling = NullValueHandling.Ignore)] public string Accountability { get; set; }

        public ProcessingRecord()
        {
            Version = 1;
            Source = string.Empty;
            Stages = new List<ProcessingStage>();
        }

        [JsonIgnore]
        public int Completed { get { return (Stages ?? new List<ProcessingStage>()).Count(s => s != null && s.Succeeded); } }

        [JsonIgnore]
        public int Failed { get { return (Stages ?? new List<ProcessingStage>()).Count(s => s != null && !s.Succeeded); } }

        [JsonIgnore]
        public ProcessingOutcome Outcome
        {
            get
            {
                if (Stages == null || Stages.Count == 0) return ProcessingOutcome.NothingRun;
                if (StoppedEarly) return ProcessingOutcome.StoppedEarly;
                return Failed > 0 ? ProcessingOutcome.CompletedWithFailures : ProcessingOutcome.Succeeded;
            }
        }

        /// <summary>"Succeeded - 6 stages", "Stopped early - 2 of 3 stages failed".</summary>
        [JsonIgnore]
        public string OutcomeText
        {
            get
            {
                var total = Stages == null ? 0 : Stages.Count;
                switch (Outcome)
                {
                    case ProcessingOutcome.Succeeded:
                        return string.Format(CultureInfo.InvariantCulture,
                            "Succeeded - {0} stage{1}", total, total == 1 ? string.Empty : "s");
                    case ProcessingOutcome.CompletedWithFailures:
                        return string.Format(CultureInfo.InvariantCulture,
                            "Completed with failures - {0} of {1} stages failed", Failed, total);
                    case ProcessingOutcome.StoppedEarly:
                        return string.Format(CultureInfo.InvariantCulture,
                            "Stopped early - {0} of {1} stages failed", Failed, total);
                    default:
                        return "Nothing ran";
                }
            }
        }

        /// <summary>The finish time in local time, minute precision.</summary>
        public string FinishedLocalText()
        {
            return FinishedUtc == default(DateTime)
                ? "unknown"
                : DateTime.SpecifyKind(FinishedUtc, DateTimeKind.Utc).ToLocalTime()
                          .ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        }

        /// <summary>A few lines for the Reports page.</summary>
        public string Describe()
        {
            var sb = new StringBuilder();
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0} - {1}{2}",
                FinishedLocalText(), OutcomeText,
                string.IsNullOrEmpty(Source) ? string.Empty : " (" + Source + ")"));

            foreach (var stage in Stages ?? new List<ProcessingStage>())
            {
                if (stage == null) continue;
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "  {0}  {1}{2}",
                    stage.Succeeded ? "OK    " : "FAILED", stage.Name,
                    string.IsNullOrEmpty(stage.Error) ? string.Empty : " - " + stage.Error));
            }

            sb.Append(string.Format(CultureInfo.InvariantCulture,
                "Afterwards: {0} point(s), {1} processed, {2} error(s), {3} needing configuration, " +
                "{4} skipped intentionally; {5} FTF object(s).",
                Points, Processed, Errors, NeedsConfiguration, Skipped, OwnedObjects));
            if (!string.IsNullOrEmpty(Accountability))
                sb.AppendLine().Append("Point accountability: ").Append(Accountability);
            return sb.ToString();
        }

        // ------------------------------------------------------------- persistence

        public string ToJson()
        {
            return JsonConvert.SerializeObject(this, Formatting.None, JsonSettings());
        }

        /// <summary>Null for blank or unreadable text: a damaged record must never stop
        /// the window from opening.</summary>
        public static ProcessingRecord FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                var record = JsonConvert.DeserializeObject<ProcessingRecord>(json, JsonSettings());
                if (record == null) return null;
                if (record.Stages == null) record.Stages = new List<ProcessingStage>();
                if (record.Source == null) record.Source = string.Empty;
                if (record.FinishedUtc.Kind != DateTimeKind.Utc)
                    record.FinishedUtc = DateTime.SpecifyKind(record.FinishedUtc, DateTimeKind.Utc);
                return record;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static JsonSerializerSettings JsonSettings()
        {
            return new JsonSerializerSettings
            {
                DateTimeZoneHandling = DateTimeZoneHandling.Utc,
                DateFormatHandling = DateFormatHandling.IsoDateFormat
            };
        }
    }

    /// <summary>
    /// Names and orders the kinds of object FTF owns in a drawing, for the Overview:
    /// "412 point labels, 380 masks, 25 tags". Keys are the CAD layer's ownership
    /// kind names (Label, Mask, DripArc...); unknown kinds keep their name.
    /// </summary>
    public static class OwnedObjects
    {
        private static readonly Dictionary<string, string> Names =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Block", "inserted blocks" },
                { "DripArc", "dripline arcs" },
                { "Label", "point labels" },
                { "Mask", "label masks" },
                { "Leader", "leaders" },
                { "Tag", "tags" },
                { "Table", "schedules" },
                { "LineLabel", "line labels" },
                { "LineMask", "line-label masks" },
                { "DraftLine", "drafted lines" },
                { "DraftAnnotation", "drafted-course labels" },
                { "DraftMask", "drafted-course masks" },
                { "SheetArea", "sheet windows" },
                { "MatchLine", "match lines" },
                { "KeyMap", "key maps" },
                { "Spot", "spot elevations" },
                { "Legend", "legends" },
                { "ControlTable", "control tables" },
                { "UtilityPipe", "dip pipes" },
                { "UtilityPipeLabel", "pipe labels" },
                { "StructureLabel", "structure labels" },
                { "LineGapProposal", "proposed linework corrections (not plotted)" },
                { "LineCorrection", "approved linework corrections" }
            };

        public static string NameOf(string kind)
        {
            if (string.IsNullOrWhiteSpace(kind)) return "other";
            string name;
            if (Names.TryGetValue(kind, out name)) return name;

            // EasementHatch -> easement hatch
            var sb = new StringBuilder();
            foreach (var ch in kind)
            {
                if (char.IsUpper(ch) && sb.Length > 0) sb.Append(' ');
                sb.Append(char.ToLowerInvariant(ch));
            }
            return sb.ToString();
        }

        /// <summary>Most numerous first: "412 point labels, 380 label masks, 25 tags".
        /// "none" when empty.</summary>
        public static string Describe(IDictionary<string, int> byKind, int take)
        {
            if (byKind == null || byKind.Count == 0 || byKind.Values.Sum() == 0) return "none";

            var ordered = byKind.Where(kv => kv.Value > 0)
                .OrderByDescending(kv => kv.Value)
                .ThenBy(kv => NameOf(kv.Key), StringComparer.OrdinalIgnoreCase)
                .ToList();

            var shown = ordered.Take(Math.Max(1, take))
                .Select(kv => kv.Value.ToString(CultureInfo.InvariantCulture) + " " + NameOf(kv.Key))
                .ToArray();

            var text = string.Join(", ", shown);
            var rest = ordered.Skip(shown.Length).Sum(kv => kv.Value);
            return rest > 0
                ? text + string.Format(CultureInfo.InvariantCulture, ", {0} other", rest)
                : text;
        }

        /// <summary>After minus before, per kind; only kinds that changed.</summary>
        public static IDictionary<string, int> Difference(IDictionary<string, int> before,
                                                          IDictionary<string, int> after)
        {
            var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (before != null) foreach (var k in before.Keys) keys.Add(k);
            if (after != null) foreach (var k in after.Keys) keys.Add(k);

            foreach (var key in keys)
            {
                int b = 0, a = 0;
                if (before != null) before.TryGetValue(key, out b);
                if (after != null) after.TryGetValue(key, out a);
                if (a != b) result[key] = a - b;
            }
            return result;
        }
    }
}
