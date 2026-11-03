using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using FieldCodes.Settings;

namespace FieldCodes.Review
{
    /// <summary>How serious a review item is.</summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum ReviewSeverity
    {
        /// <summary>Worth a look; nothing is wrong on the plan.</summary>
        Info = 0,
        /// <summary>FTF did something it could not confirm, or left something out on purpose.</summary>
        Warning = 1,
        /// <summary>Something is wrong or missing on the plan until a drafter deals with it.</summary>
        Error = 2
    }

    /// <summary>Where a review item stands.</summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum ReviewItemStatus
    {
        /// <summary>Needs a drafter.</summary>
        Open = 0,
        /// <summary>Fixed: the check that raised it no longer finds it.</summary>
        Resolved = 1,
        /// <summary>A drafter decided it does not apply, and said why.</summary>
        NotApplicable = 2,
        /// <summary>A drafter looked at it and accepted it as it is.</summary>
        Accepted = 3,
        /// <summary>A drafter approved FTF's proposed correction, and FTF drew it as a separate,
        /// traceable object (the original linework is never changed).</summary>
        Corrected = 4
    }

    /// <summary>One thing that happened to a review item: found, decided, corrected, reopened.
    /// Appended, never rewritten, so the item carries its own history.</summary>
    public sealed class ReviewEvent
    {
        [JsonProperty("at")] public DateTime At { get; set; }
        [JsonProperty("by", NullValueHandling = NullValueHandling.Ignore)] public string By { get; set; }
        /// <summary>"opened", "found again", "resolved", "not applicable", "accepted",
        /// "corrected", "reopened", "source changed", "correction erased".</summary>
        [JsonProperty("action")] public string Action { get; set; }
        [JsonProperty("note", NullValueHandling = NullValueHandling.Ignore)] public string Note { get; set; }
        /// <summary>The source fingerprint when it happened.</summary>
        [JsonProperty("fp", NullValueHandling = NullValueHandling.Ignore)] public string Fingerprint { get; set; }
    }

    /// <summary>
    /// One thing a drafter has to look at: an unpaired gate post, a code nothing handles, a
    /// pavement arrow whose direction could not be decided. Raised by the command that found
    /// it, kept in the drawing, and closed by the drafter (or by the check no longer finding
    /// it on a rerun).
    /// </summary>
    public sealed class ReviewItem
    {
        /// <summary>Stable across reruns: the kind and the subject ("gate.unpaired:10075").</summary>
        [JsonProperty("id")] public string Id { get; set; }
        /// <summary>A kind from <see cref="ReviewKinds"/> ("gate.unpaired").</summary>
        [JsonProperty("kind")] public string Kind { get; set; }
        /// <summary>The check that raised it ("Gates", "Fence heights", "Codes").</summary>
        [JsonProperty("source")] public string Source { get; set; }
        /// <summary>What it is on the plan ("Gate", "Fence", "Point").</summary>
        [JsonProperty("feature")] public string FeatureType { get; set; }
        /// <summary>Point numbers, comma separated ("10075, 10076").</summary>
        [JsonProperty("points")] public string Points { get; set; }
        /// <summary>Drawing handles of the entities concerned (to zoom to and highlight).</summary>
        [JsonProperty("handles", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<string> Handles { get; set; }
        [JsonProperty("x", NullValueHandling = NullValueHandling.Ignore)] public double? X { get; set; }
        [JsonProperty("y", NullValueHandling = NullValueHandling.Ignore)] public double? Y { get; set; }
        /// <summary>The problem, short ("Gate post has no partner").</summary>
        [JsonProperty("issue")] public string Issue { get; set; }
        /// <summary>Why FTF raised it, with the numbers.</summary>
        [JsonProperty("reason")] public string Reason { get; set; }
        /// <summary>What to do about it.</summary>
        [JsonProperty("action")] public string SuggestedAction { get; set; }
        [JsonProperty("severity")] public ReviewSeverity Severity { get; set; }
        /// <summary>Must be closed before Drafting QC Complete (from Settings > Review and QC).</summary>
        [JsonProperty("required")] public bool Required { get; set; }
        [JsonProperty("status")] public ReviewItemStatus Status { get; set; }
        /// <summary>Why it does not apply, or the reviewer's note when accepted.</summary>
        [JsonProperty("note")] public string Note { get; set; }
        [JsonProperty("by")] public string ReviewedBy { get; set; }
        [JsonProperty("at", NullValueHandling = NullValueHandling.Ignore)] public DateTime? ReviewedAt { get; set; }
        /// <summary>The command that corrects it (FTFGATE, FTFORIENT...).</summary>
        [JsonProperty("fix")] public string FixCommand { get; set; }
        /// <summary>The settings page that governs it ("Gates").</summary>
        [JsonProperty("settings")] public string SettingsPage { get; set; }
        [JsonProperty("firstSeen")] public DateTime FirstSeen { get; set; }
        [JsonProperty("lastSeen")] public DateTime LastSeen { get; set; }

        /// <summary>The code or feature it is about ("FNC", "CB"), for grouping in the window.</summary>
        [JsonProperty("code", NullValueHandling = NullValueHandling.Ignore)] public string Code { get; set; }

        /// <summary>A short hash of what the item was raised from as it was last found: the
        /// points' raw descriptions and positions, the line ends. When a person decides an item
        /// (not applicable, accepted, corrected) the fingerprint is kept with the decision, and a
        /// later run that finds the source changed reopens the item rather than trusting the
        /// old decision.</summary>
        [JsonProperty("fingerprint", NullValueHandling = NullValueHandling.Ignore)] public string Fingerprint { get; set; }

        /// <summary>The source fingerprint at the time of the person's decision.</summary>
        [JsonProperty("decisionFingerprint", NullValueHandling = NullValueHandling.Ignore)] public string DecisionFingerprint { get; set; }

        /// <summary>What the finding rests on, one fact per line ("same figure FNC1",
        /// "103 not coded E", "8.0' gap, median spacing 20.0'").</summary>
        [JsonProperty("evidence", NullValueHandling = NullValueHandling.Ignore, ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<string> Evidence { get; set; }

        /// <summary>For linework continuity: "probable", "possible" or "intentional".</summary>
        [JsonProperty("confidence", NullValueHandling = NullValueHandling.Ignore)] public string Confidence { get; set; }

        /// <summary>A proposed correction, as world points [x, y, z] in order: what Approve draws.</summary>
        [JsonProperty("geometry", NullValueHandling = NullValueHandling.Ignore, ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<double[]> Geometry { get; set; }

        /// <summary>The layer an approved correction is drawn on (the source line's layer).</summary>
        [JsonProperty("targetLayer", NullValueHandling = NullValueHandling.Ignore)] public string TargetLayer { get; set; }

        /// <summary>The handle of the correction FTF drew when it was approved.</summary>
        [JsonProperty("correction", NullValueHandling = NullValueHandling.Ignore)] public string CorrectionHandle { get; set; }

        [JsonProperty("history", NullValueHandling = NullValueHandling.Ignore, ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<ReviewEvent> History { get; set; }

        public ReviewItem()
        {
            Handles = new List<string>();
            Status = ReviewItemStatus.Open;
        }

        /// <summary>Still needs a drafter.</summary>
        [JsonIgnore] public bool IsOpen { get { return Status == ReviewItemStatus.Open; } }

        /// <summary>A person decided it: not applicable, accepted, or corrected.</summary>
        [JsonIgnore]
        public bool IsDecided
        {
            get { return Status == ReviewItemStatus.NotApplicable || Status == ReviewItemStatus.Accepted || Status == ReviewItemStatus.Corrected; }
        }

        /// <summary>FTF has a correction it can draw when a person approves it.</summary>
        [JsonIgnore] public bool HasProposal { get { return Geometry != null && Geometry.Count >= 2; } }

        /// <summary>A copy, so a list on screen cannot change the register behind it.</summary>
        public ReviewItem Clone()
        {
            var c = (ReviewItem)MemberwiseClone();
            c.Handles = new List<string>(Handles ?? new List<string>());
            c.Evidence = Evidence == null ? null : new List<string>(Evidence);
            c.Geometry = Geometry == null ? null : Geometry.Select(p => (double[])p.Clone()).ToList();
            c.History = History == null ? null : new List<ReviewEvent>(History);
            return c;
        }

        /// <summary>Most events kept per item: the first (when it was opened) and the latest.</summary>
        public const int HistoryLimit = 40;

        /// <summary>Adds one event to the item's history.</summary>
        public void Log(DateTime at, string by, string action, string note)
        {
            if (History == null) History = new List<ReviewEvent>();
            History.Add(new ReviewEvent { At = at, By = by, Action = action, Note = note, Fingerprint = Fingerprint });
            if (History.Count > HistoryLimit) History.RemoveAt(1);
        }
    }

    /// <summary>
    /// Every review item in one drawing. Each check reports what it finds; an item it found
    /// before and no longer finds is resolved; an item a drafter marked not applicable or
    /// accepted stays that way when it is found again.
    /// </summary>
    public sealed class ReviewRegister
    {
        [JsonProperty("version")] public int Version { get; set; }

        [JsonProperty("items", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<ReviewItem> Items { get; set; }

        /// <summary>When a drafter last marked the drawing Drafting QC Complete, and who; null
        /// when it is not marked (or a later required item reopened it).</summary>
        [JsonProperty("qcCompleteAt", NullValueHandling = NullValueHandling.Ignore)]
        public DateTime? QcCompleteAt { get; set; }

        [JsonProperty("qcCompleteBy", NullValueHandling = NullValueHandling.Ignore)]
        public string QcCompleteBy { get; set; }

        public ReviewRegister()
        {
            Version = 1;
            Items = new List<ReviewItem>();
        }

        /// <summary>
        /// Records what one check found. New items open; items found again keep their
        /// status (a reopened one if it had been resolved); this check's items that were not
        /// found are resolved -- unless <paramref name="complete"/> is false (the check
        /// looked at part of the drawing only, like one fence), when nothing is resolved.
        /// Returns how many items are new.
        /// </summary>
        public int Report(string source, IEnumerable<ReviewItem> found, DateTime now, bool complete = true,
                          ReviewSettings settings = null)
        {
            if (string.IsNullOrWhiteSpace(source)) throw new ArgumentException("source");
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var added = 0;
            foreach (var item in found ?? Enumerable.Empty<ReviewItem>())
            {
                if (item == null || string.IsNullOrWhiteSpace(item.Id)) continue;
                if (!seen.Add(item.Id)) continue;
                item.Source = string.IsNullOrWhiteSpace(item.Source) ? source : item.Source;
                ReviewKinds.Fill(item, settings);
                var existing = Find(item.Id);
                if (existing == null)
                {
                    var fresh = item.Clone();
                    fresh.Status = ReviewItemStatus.Open;
                    fresh.FirstSeen = now;
                    fresh.LastSeen = now;
                    fresh.History = null;
                    fresh.Log(now, "FTF", "opened", source);
                    Items.Add(fresh);
                    added++;
                    continue;
                }
                existing.Kind = item.Kind;
                existing.Source = item.Source;
                existing.FeatureType = item.FeatureType;
                existing.Points = item.Points;
                existing.Handles = new List<string>(item.Handles ?? new List<string>());
                existing.X = item.X;
                existing.Y = item.Y;
                existing.Issue = item.Issue;
                existing.Reason = item.Reason;
                existing.SuggestedAction = item.SuggestedAction;
                existing.Severity = item.Severity;
                existing.Required = item.Required;
                existing.FixCommand = item.FixCommand;
                existing.SettingsPage = item.SettingsPage;
                existing.Code = item.Code;
                existing.Evidence = item.Evidence == null ? null : new List<string>(item.Evidence);
                existing.Confidence = item.Confidence;
                existing.TargetLayer = item.TargetLayer;
                // A decided item keeps the geometry its decision was made on.
                if (!existing.IsDecided) existing.Geometry = item.Geometry == null ? null : item.Geometry.Select(p => (double[])p.Clone()).ToList();
                existing.Fingerprint = item.Fingerprint;
                existing.LastSeen = now;
                if (existing.Status == ReviewItemStatus.Resolved)
                {
                    existing.Status = ReviewItemStatus.Open;
                    existing.Note = "found again " + Stamp(now);
                    existing.ReviewedBy = null;
                    existing.ReviewedAt = null;
                    existing.Log(now, "FTF", "found again", source);
                }
                else if (existing.IsDecided && SourceChanged(existing))
                {
                    // The decision was made on something that is no longer there: never carry it over.
                    var was = existing.Status == ReviewItemStatus.NotApplicable ? "not applicable"
                            : existing.Status == ReviewItemStatus.Corrected ? "corrected" : "accepted";
                    existing.Note = "source changed since it was marked " + was +
                                    (string.IsNullOrWhiteSpace(existing.ReviewedBy) ? string.Empty : " by " + existing.ReviewedBy) +
                                    (existing.ReviewedAt.HasValue ? " " + Stamp(existing.ReviewedAt.Value) : string.Empty) +
                                    (string.IsNullOrWhiteSpace(existing.Note) ? string.Empty : " (" + existing.Note + ")") +
                                    (string.IsNullOrWhiteSpace(existing.CorrectionHandle) ? string.Empty
                                        : "; the correction " + existing.CorrectionHandle + " was kept: check or erase it");
                    existing.Status = ReviewItemStatus.Open;
                    existing.ReviewedBy = null;
                    existing.ReviewedAt = null;
                    existing.DecisionFingerprint = null;
                    if (item.Geometry != null) existing.Geometry = item.Geometry.Select(p => (double[])p.Clone()).ToList();
                    existing.Log(now, "FTF", "source changed", existing.Note);
                }
            }

            if (complete)
            {
                foreach (var item in Items.Where(i => string.Equals(i.Source, source, StringComparison.OrdinalIgnoreCase) &&
                                                      i.Status == ReviewItemStatus.Open && !seen.Contains(i.Id)))
                {
                    item.Status = ReviewItemStatus.Resolved;
                    item.Note = "no longer found when " + source + " ran " + Stamp(now);
                    item.ReviewedBy = "FTF";
                    item.ReviewedAt = now;
                    item.Log(now, "FTF", "resolved", item.Note);
                }

                // A correction closed a finding that is no longer found at all: the source changed
                // under it (the lines were rebuilt, joined by hand, or the gap closes by itself).
                // The correction is never erased; the drafter decides.
                foreach (var item in Items.Where(i => string.Equals(i.Source, source, StringComparison.OrdinalIgnoreCase) &&
                                                      i.Status == ReviewItemStatus.Corrected && !seen.Contains(i.Id)))
                {
                    item.Status = ReviewItemStatus.Open;
                    item.Note = "what this correction closed is no longer found (the source changed); the correction " +
                                (item.CorrectionHandle ?? string.Empty) + " was kept -- check it, erase it if it is not needed, then accept";
                    item.ReviewedBy = null;
                    item.ReviewedAt = null;
                    item.DecisionFingerprint = null;
                    item.Log(now, "FTF", "source changed", item.Note);
                }
            }

            ReopenQcIfNeeded(settings);
            return added;
        }

        /// <summary>True when a decided item's source is not what the decision was made on.</summary>
        private static bool SourceChanged(ReviewItem item)
        {
            return !string.IsNullOrEmpty(item.DecisionFingerprint) && !string.IsNullOrEmpty(item.Fingerprint) &&
                   !string.Equals(item.DecisionFingerprint, item.Fingerprint, StringComparison.Ordinal);
        }

        /// <summary>Resolves one item from a recheck that no longer finds it.</summary>
        public bool Resolve(string id, string note, string by, DateTime now)
        {
            var item = Find(id);
            if (item == null) return false;
            item.Status = ReviewItemStatus.Resolved;
            item.Note = note;
            item.ReviewedBy = by;
            item.ReviewedAt = now;
            item.Log(now, by, "resolved", note);
            return true;
        }

        /// <summary>A drafter decides the item does not apply. A reason is required.</summary>
        public bool MarkNotApplicable(string id, string reason, string by, DateTime now, out string problem)
        {
            problem = null;
            var item = Find(id);
            if (item == null) { problem = "That item is no longer in the review list."; return false; }
            if (string.IsNullOrWhiteSpace(reason)) { problem = "Say why it does not apply."; return false; }
            item.Status = ReviewItemStatus.NotApplicable;
            item.Note = reason.Trim();
            item.ReviewedBy = by;
            item.ReviewedAt = now;
            item.DecisionFingerprint = item.Fingerprint;
            item.Log(now, by, "not applicable", item.Note);
            return true;
        }

        /// <summary>Marks several items not applicable with one reason (a selection in the window,
        /// or every item of a kind and code). Returns how many changed; a blank reason changes none.</summary>
        public int MarkNotApplicable(IEnumerable<string> ids, string reason, string by, DateTime now, out string problem)
        {
            problem = null;
            if (string.IsNullOrWhiteSpace(reason)) { problem = "Say why they do not apply."; return 0; }
            var changed = 0;
            foreach (var id in (ids ?? Enumerable.Empty<string>()).Distinct(StringComparer.OrdinalIgnoreCase).ToList())
            {
                string one;
                if (MarkNotApplicable(id, reason, by, now, out one)) changed++;
            }
            return changed;
        }

        /// <summary>A drafter accepts the item as it is (records who and when). Kinds whose
        /// disposition must be documented (point accountability, linework continuity) need a note.</summary>
        public bool Accept(string id, string note, string by, DateTime now)
        {
            string problem;
            return Accept(id, note, by, now, out problem);
        }

        public bool Accept(string id, string note, string by, DateTime now, out string problem)
        {
            problem = null;
            var item = Find(id);
            if (item == null) { problem = "That item is no longer in the review list."; return false; }
            var kind = ReviewKinds.Get(item.Kind);
            if (kind != null && kind.NeedsNote && string.IsNullOrWhiteSpace(note))
            {
                problem = "Say why it is accepted: " + (kind.Title ?? item.Kind) + " needs a documented disposition.";
                return false;
            }
            item.Status = ReviewItemStatus.Accepted;
            item.Note = string.IsNullOrWhiteSpace(note) ? "accepted as drawn" : note.Trim();
            item.ReviewedBy = by;
            item.ReviewedAt = now;
            item.DecisionFingerprint = item.Fingerprint;
            item.Log(now, by, "accepted", item.Note);
            return true;
        }

        /// <summary>Records an approved correction: FTF drew it as <paramref name="correctionHandle"/>.</summary>
        public bool MarkCorrected(string id, string correctionHandle, string note, string by, DateTime now, out string problem)
        {
            problem = null;
            var item = Find(id);
            if (item == null) { problem = "That item is no longer in the review list."; return false; }
            if (!item.HasProposal) { problem = "FTF has no correction to draw for this item."; return false; }
            item.Status = ReviewItemStatus.Corrected;
            item.CorrectionHandle = correctionHandle;
            item.Note = string.IsNullOrWhiteSpace(note) ? "correction approved; drawn as " + correctionHandle : note.Trim();
            item.ReviewedBy = by;
            item.ReviewedAt = now;
            item.DecisionFingerprint = item.Fingerprint;
            item.Log(now, by, "corrected", item.Note);
            return true;
        }

        /// <summary>A correction FTF drew was erased: the item goes back on the open list.</summary>
        public bool CorrectionErased(string id, DateTime now)
        {
            var item = Find(id);
            if (item == null || item.Status != ReviewItemStatus.Corrected) return false;
            item.Status = ReviewItemStatus.Open;
            item.Note = "the approved correction " + item.CorrectionHandle + " was erased";
            item.ReviewedBy = null;
            item.ReviewedAt = null;
            item.DecisionFingerprint = null;
            item.CorrectionHandle = null;
            item.Log(now, "FTF", "correction erased", item.Note);
            ReopenQcIfNeeded(null);
            return true;
        }

        /// <summary>Puts an item back on the open list.</summary>
        public bool Reopen(string id)
        {
            return Reopen(id, null, DateTime.Now);
        }

        public bool Reopen(string id, string by, DateTime now)
        {
            var item = Find(id);
            if (item == null) return false;
            item.Status = ReviewItemStatus.Open;
            item.Note = null;
            item.ReviewedBy = null;
            item.ReviewedAt = null;
            item.DecisionFingerprint = null;
            item.Log(now, by, "reopened", null);
            ReopenQcIfNeeded(null);
            return true;
        }

        public ReviewItem Find(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            return Items.FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        [JsonIgnore] public IList<ReviewItem> Open { get { return Items.Where(i => i.IsOpen).ToList(); } }

        /// <summary>Open items that must be closed before Drafting QC Complete.</summary>
        [JsonIgnore] public IList<ReviewItem> RequiredOpen { get { return Items.Where(i => i.IsOpen && i.Required).ToList(); } }

        /// <summary>
        /// Marks the drawing Drafting QC Complete -- refused while required items are open
        /// (when <paramref name="settings"/> says required items block it). Never blocks
        /// saving or drafting; it only governs this mark.
        /// </summary>
        public bool MarkQcComplete(ReviewSettings settings, string by, DateTime now, out string problem)
        {
            problem = null;
            var blocking = settings == null || settings.BlockQcComplete ? RequiredOpen : new List<ReviewItem>();
            if (blocking.Count > 0)
            {
                problem = blocking.Count.ToString(CultureInfo.InvariantCulture) + " required review item(s) are still open: " +
                          string.Join("; ", blocking.Take(5).Select(i => Describe(i)).ToArray()) +
                          (blocking.Count > 5 ? "; ..." : string.Empty) +
                          ". Resolve them, or mark them not applicable with a reason, first.";
                return false;
            }
            QcCompleteAt = now;
            QcCompleteBy = by;
            return true;
        }

        /// <summary>An open required item takes the drawing back out of QC Complete.</summary>
        private void ReopenQcIfNeeded(ReviewSettings settings)
        {
            if (!QcCompleteAt.HasValue) return;
            if (settings != null && !settings.BlockQcComplete) return;
            if (RequiredOpen.Count == 0) return;
            QcCompleteAt = null;
            QcCompleteBy = null;
        }

        /// <summary>"Gate post has no partner (10075)".</summary>
        public static string Describe(ReviewItem item)
        {
            if (item == null) return string.Empty;
            return (item.Issue ?? item.Kind) + (string.IsNullOrWhiteSpace(item.Points) ? string.Empty : " (" + item.Points + ")");
        }

        private static string Stamp(DateTime when)
        {
            return when.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        }

        public string ToJson()
        {
            return JsonConvert.SerializeObject(this, Formatting.None);
        }

        /// <summary>The register stored in a drawing; an empty one when there is none or it
        /// cannot be read.</summary>
        public static ReviewRegister FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new ReviewRegister();
            try
            {
                var r = JsonConvert.DeserializeObject<ReviewRegister>(json);
                if (r == null) return new ReviewRegister();
                if (r.Items == null) r.Items = new List<ReviewItem>();
                r.Items.RemoveAll(i => i == null || string.IsNullOrWhiteSpace(i.Id));
                foreach (var i in r.Items) if (i.Handles == null) i.Handles = new List<string>();
                foreach (var i in r.Items) if (i.Geometry != null) i.Geometry.RemoveAll(p => p == null || p.Length < 2);
                return r;
            }
            catch (JsonException)
            {
                return new ReviewRegister();
            }
        }
    }
}
