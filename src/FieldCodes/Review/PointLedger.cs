using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FieldCodes.Annotation;
using FieldCodes.Linework;
using FieldCodes.Orient;
using FieldCodes.Settings;
using FieldCodes.Standards;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace FieldCodes.Review
{
    /// <summary>What accounts for a point, strongest first.</summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum PointDisposition
    {
        /// <summary>Not accounted for: something it needs is missing, or a decision is open.</summary>
        Review = 0,
        /// <summary>FTF objects stamped with the point: label, tag, block, dripline, gate label.</summary>
        Drawn = 1,
        /// <summary>A vertex of (or on) a drawn line of its own feature.</summary>
        Linework = 2,
        /// <summary>A survey control or found monument code.</summary>
        Control = 3,
        /// <summary>Its point style draws the code's own symbol.</summary>
        Symbol = 4,
        /// <summary>A surface is built from it.</summary>
        Surface = 5,
        /// <summary>Excluded with a documented reason: an ignore entry's, or a reviewer's.</summary>
        Excluded = 6,
        /// <summary>It was recorded before and is no longer in the drawing.</summary>
        Deleted = 7,
        /// <summary>Its Civil 3D point style shows it, as its feature's standard has it.</summary>
        PointStyle = 8,
        /// <summary>The standard draws nothing for it: the survey point is the record.</summary>
        PointOnly = 9
    }

    /// <summary>One thing that happened to a point: first recorded, changed, deleted, restored.</summary>
    public sealed class LedgerEvent
    {
        [JsonProperty("at")] public DateTime At { get; set; }
        [JsonProperty("action")] public string Action { get; set; }
        [JsonProperty("note", NullValueHandling = NullValueHandling.Ignore)] public string Note { get; set; }
    }

    /// <summary>One survey point in the ledger: what it is, what it needs, what the drawing holds
    /// for it, and how it is accounted for.</summary>
    public sealed class LedgerPoint
    {
        [JsonProperty("n")] public string Number { get; set; }
        [JsonProperty("h", NullValueHandling = NullValueHandling.Ignore)] public string Handle { get; set; }
        /// <summary>The raw description exactly as Civil 3D stores it.</summary>
        [JsonProperty("raw")] public string RawDescription { get; set; }
        [JsonProperty("x")] public double X { get; set; }
        [JsonProperty("y")] public double Y { get; set; }
        [JsonProperty("z")] public double Z { get; set; }
        [JsonProperty("code", NullValueHandling = NullValueHandling.Ignore)] public string Code { get; set; }
        /// <summary>How FTF read it: "rule TREE", "bare code", "linework FNC1", "ignored: ...".</summary>
        [JsonProperty("read", NullValueHandling = NullValueHandling.Ignore)] public string Reading { get; set; }
        /// <summary>What the point is: "Surface only", "Control/Check", "Sign (address / house number)".</summary>
        [JsonProperty("class", NullValueHandling = NullValueHandling.Ignore)] public string Classification { get; set; }
        /// <summary>The drafting standard that applied (Settings > Point Features), and what it asks for.</summary>
        [JsonProperty("standard", NullValueHandling = NullValueHandling.Ignore)] public string Standard { get; set; }
        [JsonProperty("needs", NullValueHandling = NullValueHandling.Ignore)] public string Needs { get; set; }
        /// <summary>Surface data: designated, verified, excluded, or not verifiable (the last three
        /// only when surface membership is verified).</summary>
        [JsonProperty("surface", NullValueHandling = NullValueHandling.Ignore)] public string SurfaceStatus { get; set; }
        /// <summary>Its standard requires its symbol to be rotated: open orientation items about
        /// it hold it for review.</summary>
        [JsonProperty("rotate", DefaultValueHandling = DefaultValueHandling.Ignore)] public bool RotationRequired { get; set; }

        [JsonProperty("expected", ObjectCreationHandling = ObjectCreationHandling.Replace)] public List<string> Expected { get; set; }
        [JsonProperty("found", ObjectCreationHandling = ObjectCreationHandling.Replace)] public List<string> Found { get; set; }
        [JsonProperty("missing", ObjectCreationHandling = ObjectCreationHandling.Replace)] public List<string> Missing { get; set; }
        [JsonProperty("evidence", ObjectCreationHandling = ObjectCreationHandling.Replace)] public List<string> Evidence { get; set; }
        /// <summary>Review items about it ("point.nosymbol:204", "line.gap:FNC:103-105").</summary>
        [JsonProperty("issues", ObjectCreationHandling = ObjectCreationHandling.Replace)] public List<string> Issues { get; set; }

        [JsonProperty("disposition")] public PointDisposition Disposition { get; set; }
        /// <summary>Something in the drawing shows it (an FTF object, a line, a visible marker, a surface).</summary>
        [JsonProperty("represented")] public bool Represented { get; set; }
        /// <summary>Everything it needs is drawn, or decided by a person with a reason.</summary>
        [JsonProperty("complete")] public bool DraftingComplete { get; set; }
        /// <summary>The decisions recorded on its review items ("not applicable: ... (JD 2026-11-03)").</summary>
        [JsonProperty("decision", NullValueHandling = NullValueHandling.Ignore)] public string Decision { get; set; }

        [JsonProperty("fp")] public string Fingerprint { get; set; }
        [JsonProperty("firstSeen")] public DateTime FirstSeen { get; set; }
        [JsonProperty("lastSeen")] public DateTime LastSeen { get; set; }
        [JsonProperty("deleted")] public bool Deleted { get; set; }
        [JsonProperty("history", ObjectCreationHandling = ObjectCreationHandling.Replace)] public List<LedgerEvent> History { get; set; }

        public LedgerPoint()
        {
            Expected = new List<string>();
            Found = new List<string>();
            Missing = new List<string>();
            Evidence = new List<string>();
            Issues = new List<string>();
            History = new List<LedgerEvent>();
        }

        [JsonIgnore] public bool Accounted { get { return Disposition != PointDisposition.Review; } }

        public void Log(DateTime at, string action, string note)
        {
            if (History == null) History = new List<LedgerEvent>();
            History.Add(new LedgerEvent { At = at, Action = action, Note = note });
            if (History.Count > 30) History.RemoveAt(1);
        }
    }

    /// <summary>One accounting run's numbers.</summary>
    public sealed class LedgerRun
    {
        [JsonProperty("at")] public DateTime At { get; set; }
        [JsonProperty("source", NullValueHandling = NullValueHandling.Ignore)] public string Source { get; set; }
        [JsonProperty("points")] public int Points { get; set; }
        [JsonProperty("accounted")] public int Accounted { get; set; }
        [JsonProperty("review")] public int Review { get; set; }
        [JsonProperty("deleted")] public int Deleted { get; set; }
        [JsonProperty("represented")] public int Represented { get; set; }
        [JsonProperty("complete")] public int DraftingComplete { get; set; }
        [JsonProperty("by", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public Dictionary<string, int> ByDisposition { get; set; }
        [JsonProperty("gaps")] public int OpenLineItems { get; set; }
        /// <summary>Points accounted for with an open warning or note (not a required action).</summary>
        [JsonProperty("warnings")] public int Warnings { get; set; }

        public LedgerRun() { ByDisposition = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase); }

        /// <summary>"1,115 points: 1,080 accounted for (Drawn 400, Linework 500, ...), 15 for review, 2 deleted".</summary>
        public string Describe()
        {
            var parts = ByDisposition.Where(p => p.Value > 0 && p.Key != "Review" && p.Key != "Deleted")
                                     .Select(p => p.Key + " " + p.Value.ToString("N0", CultureInfo.InvariantCulture)).ToArray();
            return Points.ToString("N0", CultureInfo.InvariantCulture) + " points: " +
                   Accounted.ToString("N0", CultureInfo.InvariantCulture) + " accounted for" +
                   (parts.Length > 0 ? " (" + string.Join(", ", parts) + ")" : string.Empty) + ", " +
                   Review.ToString("N0", CultureInfo.InvariantCulture) + " for review" +
                   (Warnings > 0 ? " (" + Warnings.ToString("N0", CultureInfo.InvariantCulture) + " more with a warning to look at)" : string.Empty) +
                   (Deleted > 0 ? ", " + Deleted.ToString("N0", CultureInfo.InvariantCulture) + " deleted since first recorded" : string.Empty) +
                   "; drafting complete for " + DraftingComplete.ToString("N0", CultureInfo.InvariantCulture) +
                   ", represented in CAD " + Represented.ToString("N0", CultureInfo.InvariantCulture) +
                   (OpenLineItems > 0 ? "; " + OpenLineItems.ToString("N0", CultureInfo.InvariantCulture) + " linework continuity item(s) open" : string.Empty);
        }
    }

    /// <summary>
    /// The drawing's point ledger: every survey point ever recorded in it (the first run is the
    /// baseline inventory), so a point that disappears between runs is still there to say so.
    /// Stored in the drawing (key ACCOUNT); the review list holds the decisions.
    /// </summary>
    public sealed class PointLedger
    {
        [JsonProperty("version")] public int Version { get; set; }
        [JsonProperty("baselineAt", NullValueHandling = NullValueHandling.Ignore)] public DateTime? BaselineAt { get; set; }
        [JsonProperty("points", ObjectCreationHandling = ObjectCreationHandling.Replace)] public List<LedgerPoint> Points { get; set; }
        [JsonProperty("runs", ObjectCreationHandling = ObjectCreationHandling.Replace)] public List<LedgerRun> Runs { get; set; }

        public PointLedger()
        {
            Version = 1;
            Points = new List<LedgerPoint>();
            Runs = new List<LedgerRun>();
        }

        public LedgerPoint Find(string number)
        {
            if (string.IsNullOrWhiteSpace(number)) return null;
            return Points.FirstOrDefault(p => string.Equals(p.Number, number, StringComparison.OrdinalIgnoreCase));
        }

        [JsonIgnore] public LedgerRun LastRun { get { return Runs.Count == 0 ? null : Runs[Runs.Count - 1]; } }

        public string ToJson() { return JsonConvert.SerializeObject(this, Formatting.None); }

        public static PointLedger FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new PointLedger();
            try
            {
                var l = JsonConvert.DeserializeObject<PointLedger>(json) ?? new PointLedger();
                if (l.Points == null) l.Points = new List<LedgerPoint>();
                if (l.Runs == null) l.Runs = new List<LedgerRun>();
                l.Points.RemoveAll(p => p == null || string.IsNullOrWhiteSpace(p.Number));
                return l;
            }
            catch (JsonException) { return new PointLedger(); }
        }
    }

    // ------------------------------------------------------------------------- facts

    /// <summary>An FTF object stamped with the point.</summary>
    public sealed class OutputFact
    {
        /// <summary>Block, Label, Tag, DripArc, Leader, Mask, GateLabel, Bench, Correction...</summary>
        public string Kind { get; set; }
        public string Handle { get; set; }
        public bool MovedByHand { get; set; }
    }

    /// <summary>What the point's style (or its point group's override) draws as the marker.</summary>
    public sealed class MarkerFact
    {
        public bool Known { get; set; }
        public bool Visible { get; set; }
        /// <summary>UsePointForMarker, UseCustomMarker or UseSymbolForMarker.</summary>
        public string MarkerType { get; set; }
        public string SymbolName { get; set; }
        public string StyleName { get; set; }
        public string Problem { get; set; }
        /// <summary>The point's own Civil 3D label is shown (label visible, its point style's
        /// label component on, a label style set). Null: could not be read.</summary>
        public bool? LabelShown { get; set; }
        public string LabelStyleName { get; set; }
    }

    /// <summary>A surface the point is part of.</summary>
    public sealed class SurfaceFact
    {
        public string Surface { get; set; }
        /// <summary>True: a point group in the surface's definition holds the point. False: none
        /// does. Null: the definition could not be read.</summary>
        public bool? InDefinition { get; set; }
        public string PointGroup { get; set; }
        /// <summary>A vertex of the triangulation coincides with the point (plan and elevation).</summary>
        public bool IsVertex { get; set; }
    }

    /// <summary>Everything the drawing says about one point.</summary>
    public sealed class PointFacts
    {
        public string Number { get; set; }
        public string Handle { get; set; }
        public string RawDescription { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        /// <summary>The point as FTFPOINTS parses it (the note joined when one is read).</summary>
        public ParsedPoint Parsed { get; set; }
        /// <summary>The point as the label stages parse it (dip notes kept out); null: as Parsed.</summary>
        public ParsedPoint ForLabel { get; set; }
        public LineworkShot Coding { get; set; }
        public List<OutputFact> Outputs { get; set; }
        /// <summary>Text someone typed near the point that matches its label.</summary>
        public bool HandLabelled { get; set; }
        public MarkerFact Marker { get; set; }
        public List<SurfaceFact> Surfaces { get; set; }

        public PointFacts()
        {
            Outputs = new List<OutputFact>();
            Surfaces = new List<SurfaceFact>();
        }
    }

    /// <summary>What the accounting needs besides the points.</summary>
    public sealed class AccountingContext
    {
        public RulesConfig Rules { get; set; }
        public FtfSettings Settings { get; set; }
        public CodeDiagnostics Diagnostics { get; set; }
        public ContinuityResult Continuity { get; set; }
        /// <summary>A figure code's feature family (aliases); null: the code itself.</summary>
        public Func<string, string> FamilyOf { get; set; }
        /// <summary>The drawing has an FTF control table.</summary>
        public bool ControlTableExists { get; set; }
        /// <summary>Point numbers the control table lists.</summary>
        public ICollection<string> ControlTablePoints { get; set; }
        /// <summary>Point numbers in a gate FTF labelled (the posts in its tag).</summary>
        public ICollection<string> GatePostsLabelled { get; set; }
        public double UnitsPerFoot { get; set; }
        /// <summary>Label and tag scopes; null: the settings' categories.</summary>
        public ICollection<AnnotationCategory> LabelScope { get; set; }
        public ICollection<AnnotationCategory> TagScope { get; set; }
        public string Source { get; set; }

        public AccountingContext()
        {
            ControlTablePoints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            GatePostsLabelled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            UnitsPerFoot = 1.0;
        }
    }

    /// <summary>The result of one accounting pass, before the review decisions are applied.</summary>
    public sealed class AccountingResult
    {
        public PointLedger Ledger { get; set; }
        /// <summary>The point review items to report (source "Points").</summary>
        public List<ReviewItem> Items { get; private set; }
        public AccountingResult() { Items = new List<ReviewItem>(); }
    }

    /// <summary>
    /// Accounts for every point from evidence in the drawing, against what its feature's drafting
    /// standard (Settings > Point Features, <see cref="FeatureStandard"/>) requires -- never a
    /// list of its own:
    /// <list type="bullet">
    /// <item>a symbol: its own (a generic marker is not enough), any point style marker, or none;</item>
    /// <item>a label: the one its rule writes, a required one, the point's own Civil 3D label, or none;</item>
    /// <item>surface data (a ground shot) is accounted for as designated surface data, checked
    /// against the surfaces only when Verify surface membership is on;</item>
    /// <item>a control check shot is accounted for as Control/Check;</item>
    /// <item>a linework code needs a drawn line of its feature through it;</item>
    /// <item>what its rule plans that FTF draws (tag, block, dripline) must be there;</item>
    /// <item>an unknown code is for review until it has a rule or a standard.</item>
    /// </list>
    /// What must be done is a required item; what may be wrong is a warning; what is only worth
    /// knowing is information. Only required items hold a point for review.
    /// </summary>
    public static class PointAccounting
    {
        public static AccountingResult Build(PointLedger previous, IList<PointFacts> points, AccountingContext ctx, DateTime now)
        {
            var result = new AccountingResult();
            var ledger = new PointLedger
            {
                BaselineAt = previous != null && previous.BaselineAt.HasValue ? previous.BaselineAt : now,
                Runs = previous != null ? new List<LedgerRun>(previous.Runs ?? new List<LedgerRun>()) : new List<LedgerRun>()
            };
            result.Ledger = ledger;
            var settings = ctx.Settings ?? new FtfSettings();
            var acc = settings.Accountability ?? new AccountabilitySettings();
            var upf = ctx.UnitsPerFoot > 0 ? ctx.UnitsPerFoot : 1.0;
            var list = (points ?? new List<PointFacts>()).Where(p => p != null && !string.IsNullOrWhiteSpace(p.Number)).ToList();
            var current = new HashSet<string>(list.Select(p => p.Number), StringComparer.OrdinalIgnoreCase);

            // Duplicates: two points at one plan position.
            var duplicates = Duplicates(list, Math.Max(0, acc.DuplicateToleranceFeet) * upf);

            foreach (var p in list)
            {
                var old = previous == null ? null : previous.Find(p.Number);
                var entry = new LedgerPoint
                {
                    Number = p.Number,
                    Handle = p.Handle,
                    RawDescription = p.RawDescription ?? string.Empty,
                    X = p.X, Y = p.Y, Z = p.Z,
                    Fingerprint = PointFingerprint(p.RawDescription, p.X, p.Y, p.Z),
                    FirstSeen = old != null ? old.FirstSeen : now,
                    LastSeen = now,
                    History = old != null && old.History != null ? new List<LedgerEvent>(old.History) : new List<LedgerEvent>()
                };
                if (old == null) entry.Log(now, previous == null || previous.Points.Count == 0 ? "baseline" : "first recorded", Describe(entry));
                else if (old.Deleted) entry.Log(now, "restored", "back in the drawing: " + Describe(entry));

                Account(entry, p, ctx, settings, acc, result.Items);

                // Changed in Civil 3D since the last run: recorded, and reported as information.
                if (old != null && !old.Deleted && old.Fingerprint != entry.Fingerprint)
                {
                    var change = Change(old, entry, upf);
                    entry.Log(now, "source changed", change);
                    var item = Item(ReviewKinds.PointSourceChanged, entry, change);
                    result.Items.Add(item);
                }

                List<string> partners;
                if (duplicates.TryGetValue(p.Number, out partners))
                {
                    var item = Item(ReviewKinds.PointDuplicate, entry,
                        "At the same position as " + string.Join(", ", partners.ToArray()) + " (within " +
                        AccountabilitySettings.Fmt(acc.DuplicateToleranceFeet) + "'): " + Quote(p.RawDescription) +
                        string.Join(string.Empty, partners.Select(n => { var q = list.First(x => x.Number == n); return "; " + n + " " + Quote(q.RawDescription); }).ToArray()));
                    item.Points = string.Join(", ", new[] { p.Number }.Concat(partners).ToArray());
                    item.Fingerprint = Review.Fingerprint.Of(new[] { entry.Fingerprint }.Concat(partners.Select(n => { var q = list.First(x => x.Number == n); return PointFingerprint(q.RawDescription, q.X, q.Y, q.Z); })));
                    result.Items.Add(item);
                    entry.Issues.Add(item.Id);
                }
                ledger.Points.Add(entry);
            }

            // Points recorded before and gone now: kept in the ledger, and for review.
            foreach (var old in previous == null ? new List<LedgerPoint>() : previous.Points)
            {
                if (current.Contains(old.Number)) continue;
                var gone = Copy(old);
                gone.Deleted = true;
                gone.Disposition = PointDisposition.Deleted;
                gone.Represented = false;
                gone.DraftingComplete = false;
                gone.Issues = new List<string>();
                if (!old.Deleted) gone.Log(now, "deleted", "no longer in the drawing (last seen " + old.LastSeen.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + ")");
                var item = Item(ReviewKinds.PointDeleted, gone,
                    "Point " + old.Number + " " + Quote(old.RawDescription) + " was in the drawing (first recorded " +
                    old.FirstSeen.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ", last seen " +
                    old.LastSeen.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + ") and is not now.");
                item.Handles.Clear();
                result.Items.Add(item);
                gone.Issues.Add(item.Id);
                ledger.Points.Add(gone);
            }

            ledger.Points = ledger.Points.OrderBy(x => SourcePointIndex.NumberKey(x.Number)).ThenBy(x => x.Number, StringComparer.Ordinal).ToList();
            return result;
        }

        /// <summary>
        /// After the items are in the review list: a point is accounted for when every item about
        /// it is closed -- by the check no longer finding it, or by a person's documented decision
        /// -- and then by its strongest evidence. Adds the run's numbers to the ledger.
        /// </summary>
        public static LedgerRun Finish(PointLedger ledger, ReviewRegister register, DateTime now, string source)
        {
            var warned = 0;
            foreach (var entry in ledger.Points)
            {
                // A symbol the standard requires turned: its open orientation items are about the point.
                if (entry.RotationRequired && register != null && !entry.Deleted)
                    foreach (var o in register.Items.Where(i => i.IsOpen && (i.Kind == ReviewKinds.OrientReview || i.Kind == ReviewKinds.MarkingDirection) && Names(i.Points, entry.Number)))
                        if (!entry.Issues.Contains(o.Id)) entry.Issues.Add(o.Id);
                var open = new List<ReviewItem>();
                var warnings = new List<ReviewItem>();
                var decided = new List<ReviewItem>();
                foreach (var id in entry.Issues)
                {
                    var item = register == null ? null : register.Find(id);
                    if (item == null) continue;
                    if (item.IsOpen)
                    {
                        // Information (a changed description) never holds a point; a warning is
                        // something to look at, not a missing deliverable.
                        if (item.Kind == ReviewKinds.PointSourceChanged) continue;
                        if (item.Required) open.Add(item); else warnings.Add(item);
                    }
                    else if (item.IsDecided) decided.Add(item);
                }
                if (!entry.Deleted && open.Count == 0 && warnings.Count > 0) warned++;
                entry.Decision = decided.Count == 0 ? null
                    : string.Join("; ", decided.Select(i => i.Status.ToString() + ": " + (i.Note ?? string.Empty) +
                        (string.IsNullOrWhiteSpace(i.ReviewedBy) ? string.Empty : " (" + i.ReviewedBy +
                        (i.ReviewedAt.HasValue ? " " + i.ReviewedAt.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : string.Empty) + ")")).ToArray());

                if (entry.Deleted)
                {
                    // Always Deleted; complete only once a person has documented why it went.
                    entry.Disposition = PointDisposition.Deleted;
                    entry.DraftingComplete = open.Count == 0;
                    continue;
                }
                entry.DraftingComplete = open.Count == 0;
                if (open.Count > 0) { entry.Disposition = PointDisposition.Review; continue; }
                entry.Disposition = Strongest(entry);
                if (entry.Disposition == PointDisposition.Review && decided.Count > 0) entry.Disposition = PointDisposition.Excluded;
            }

            var run = new LedgerRun { At = now, Source = source, Warnings = warned };
            foreach (var e in ledger.Points)
            {
                // Deleted points are counted apart (open ones only): they are not in the drawing.
                if (e.Deleted) { if (!e.DraftingComplete) run.Deleted++; continue; }
                run.Points++;
                if (e.Accounted) run.Accounted++;
                if (e.Disposition == PointDisposition.Review) run.Review++;
                if (e.Represented) run.Represented++;
                if (e.DraftingComplete) run.DraftingComplete++;
                var key = e.Disposition.ToString();
                int n; run.ByDisposition.TryGetValue(key, out n); run.ByDisposition[key] = n + 1;
            }
            if (register != null)
                run.OpenLineItems = register.Items.Count(i => i.IsOpen && string.Equals(i.Source, ReviewKinds.ContinuitySource, StringComparison.OrdinalIgnoreCase));
            ledger.Runs.Add(run);
            if (ledger.Runs.Count > 100) ledger.Runs.RemoveRange(0, ledger.Runs.Count - 100);
            return run;
        }

        private static bool Names(string points, string number)
        {
            return !string.IsNullOrWhiteSpace(points) &&
                   points.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries).Any(n => string.Equals(n.Trim(), number, StringComparison.OrdinalIgnoreCase));
        }

        // ------------------------------------------------------------------ one point

        private static void Account(LedgerPoint entry, PointFacts p, AccountingContext ctx, FtfSettings settings,
                                    AccountabilitySettings acc, List<ReviewItem> items)
        {
            var rules = ctx.Rules;
            var parsed = p.Parsed;
            var raw = p.RawDescription ?? string.Empty;
            var token = FieldCodeParser.FirstToken(raw.Trim());
            entry.Code = parsed != null && !string.IsNullOrWhiteSpace(parsed.Code) ? parsed.Code : token;
            var coding = p.Coding;

            // What the office's standard says this feature needs (Settings > Point Features).
            var req = FeatureRequirements.For(rules, parsed, raw, parsed != null ? parsed.Note : null);
            entry.Classification = EffectiveRequirement.ClassName(req.Classification) +
                                   (req.SignCategory != SignCategory.None ? " (" + EffectiveRequirement.SignName(req.SignCategory) + ")" : string.Empty);
            entry.Standard = req.Source;
            entry.Needs = req.Describe();
            entry.RotationRequired = req.Rotation;

            // Anything FTF drew for it.
            foreach (var o in p.Outputs)
                entry.Evidence.Add("FTF " + o.Kind.ToLowerInvariant() + " " + o.Handle + (o.MovedByHand ? " (moved by hand)" : string.Empty));
            var marker = p.Marker;
            var visibleMarker = marker != null && marker.Known && marker.Visible;
            var vertexOfSurface = p.Surfaces.Any(s => s.IsVertex);

            // The code check's verdict first: an unreadable description or a code nothing handles
            // has its item there (one per code), not another here.
            var issue = ctx.Diagnostics != null && parsed != null ? ctx.Diagnostics.Classify(parsed) : null;
            var codeIsFigure = coding != null && coding.IsLinework && coding.Leading.Count == 0;
            // A code the office ignores (Settings > Diagnostics) is an exclusion, never a code nothing
            // handles, whatever list the classifier was given.
            if (issue != null && issue.Category == CodeIssueCategory.NeedsConfiguration && parsed != null && !parsed.Recognized &&
                (ctx.Settings ?? settings).Diagnostics != null && (ctx.Settings ?? settings).Diagnostics.NoteFor(token) != null)
                issue = null;
            // A feature the standard names (by its code, or by its description: "HOUSE NUMBER 4735
            // SIGN") is handled by the standard, even where no rule reads its words.
            if (issue != null && !codeIsFigure &&
                (issue.Kind == CodeIssueKind.StandardFeature || (issue.Category == CodeIssueCategory.NeedsConfiguration && req.Specific)))
            {
                entry.Evidence.Add("no rule reads its words; its drafting standard applies: " + req.Source);
                issue = null;
            }
            if (issue != null && issue.Category == CodeIssueCategory.Error)
            {
                entry.Reading = "error: " + issue.Reason;
                entry.Issues.Add((issue.Kind == CodeIssueKind.InvalidData ? ReviewKinds.CodeInvalid : ReviewKinds.CodeUnreadable) + ":" + p.Number);
            }
            else if (issue != null && issue.Category == CodeIssueCategory.NeedsConfiguration && !codeIsFigure)
            {
                entry.Reading = "unknown code " + issue.Code;
                entry.Issues.Add(ReviewKinds.CodeUnknown + ":" + issue.Code);
            }

            // Linework: every figure the shot is coded onto needs a line of its feature through it.
            if (coding != null && coding.IsLinework)
            {
                foreach (var f in coding.Figures)
                {
                    var family = Family(ctx, f.Code);
                    var want = "linework " + f.FigureId;
                    entry.Expected.Add(want);
                    var lines = ctx.Continuity != null ? ctx.Continuity.LinesOf(p.Number, family) : new List<string>();
                    if (lines.Count > 0)
                    {
                        entry.Found.Add(want);
                        entry.Evidence.Add("on " + f.FigureId + " line " + string.Join(", ", lines.ToArray()));
                        continue;
                    }
                    entry.Missing.Add(want);
                    var findings = ctx.Continuity != null ? ctx.Continuity.FindingsFor(p.Number, f.FigureId) : new List<string>();
                    if (findings.Count > 0)
                        foreach (var id in findings) { if (!entry.Issues.Contains(id)) entry.Issues.Add(id); }
                    else
                    {
                        var item = Item(ReviewKinds.PointMissingLinework, entry,
                            Quote(raw) + " is coded onto " + f.FigureId + " but no drawn line of " + family + " goes through it.");
                        item.Code = f.FigureId;
                        if (!items.Any(i => i.Id == item.Id)) items.Add(item);
                        if (!entry.Issues.Contains(item.Id)) entry.Issues.Add(item.Id);
                    }
                }
                if (entry.Reading == null) entry.Reading = "linework " + string.Join(" ", coding.Figures.Select(f => f.FigureId).ToArray());
            }

            // The point code itself (when the description does not start with a figure).
            if (!codeIsFigure && (issue == null || issue.Category != CodeIssueCategory.Error) &&
                (issue == null || issue.Category != CodeIssueCategory.NeedsConfiguration))
                PointCode(entry, p, token, req, ctx, settings, acc, items);
            else if (codeIsFigure && parsed != null && parsed.Recognized && !parsed.Ignored)
                PointCode(entry, p, token, req, ctx, settings, acc, items);

            entry.Represented = p.Outputs.Count > 0 || entry.Found.Any(f => f.StartsWith("linework ", StringComparison.Ordinal)) ||
                                visibleMarker || vertexOfSurface || (marker != null && marker.LabelShown == true);
            if (marker != null && marker.Known && marker.Visible)
                entry.Evidence.Add("marker: " + (marker.StyleName ?? "style") + " (" + (marker.MarkerType ?? "?") +
                                   (string.IsNullOrWhiteSpace(marker.SymbolName) ? string.Empty : " " + marker.SymbolName) + ")");
            if (!string.IsNullOrWhiteSpace(req.Layer)) entry.Evidence.Add("standard layer " + req.Layer);
            entry.Found = entry.Found.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            // Never a point left for review with nothing anyone can close: if no evidence accounts
            // for it and no item is about it, it gets one.
            if (entry.Issues.Count == 0 && entry.Missing.Count == 0 && Strongest(entry) == PointDisposition.Review)
            {
                entry.Missing.Add("an output");
                var item = Item(ReviewKinds.PointMissingOutput, entry,
                    Quote(raw) + ": nothing in the drawing accounts for this point (no FTF object, no line, no symbol, no surface, no documented exclusion).");
                items.Add(item);
                entry.Issues.Add(item.Id);
            }
            entry.Disposition = entry.Issues.Count == 0 && entry.Missing.Count == 0 ? Strongest(entry) : PointDisposition.Review;
            entry.DraftingComplete = entry.Missing.Count == 0 && entry.Issues.Count == 0;
        }

        private static void PointCode(LedgerPoint entry, PointFacts p, string token, EffectiveRequirement req, AccountingContext ctx,
                                      FtfSettings settings, AccountabilitySettings acc, List<ReviewItem> items)
        {
            var rules = ctx.Rules;
            var parsed = p.Parsed;
            var raw = p.RawDescription ?? string.Empty;

            // Survey control and found monuments.
            if (rules != null && rules.ControlCodes != null && rules.ControlCodes.Any(c => string.Equals((c ?? string.Empty).Trim(), token, StringComparison.OrdinalIgnoreCase)))
            {
                entry.Reading = entry.Reading ?? "control " + token;
                entry.Evidence.Add("control code " + token);
                if (ctx.ControlTableExists)
                {
                    entry.Expected.Add("control table");
                    if (ctx.ControlTablePoints.Contains(p.Number)) { entry.Found.Add("control table"); entry.Evidence.Add("in the control table"); }
                    else Missing(entry, items, "control table", "Control point " + p.Number + " " + Quote(raw) + " is not in the drawing's control table (rerun FTFCONTROL).");
                }
                else entry.Found.Add("control");
                return;
            }

            if (parsed == null) return;

            // Excluded by an ignore list -- unless the drafting standard names the feature, which
            // then says what it needs (GS and ZK are on the ignore list so nothing draws them; their
            // standard is what accounts for them). The office's own ignored codes (Settings >
            // Diagnostics) apply whenever no rule matched, however the parser flagged it.
            var officeNote = !parsed.Recognized && settings.Diagnostics != null ? settings.Diagnostics.NoteFor(token) : null;
            if ((parsed.Ignored || officeNote != null) && !req.Specific)
            {
                var ruleNote = parsed.Ignored && rules != null ? rules.IgnoreNoteFor(token) : null;
                var userNote = ruleNote == null ? officeNote ?? (settings.Diagnostics != null ? settings.Diagnostics.NoteFor(token) : null) : null;
                var note = ruleNote ?? userNote;
                if (note != null)
                {
                    var list = ruleNote != null ? "the rules' ignore list" : "the codes the office ignores";
                    entry.Reading = entry.Reading ?? "ignored (" + (note.Code ?? token) + ")";
                    if (note.HasReason)
                    {
                        entry.Found.Add("excluded");
                        entry.Evidence.Add("excluded by " + list + ": " + note.Reason +
                                           (string.IsNullOrWhiteSpace(note.By) ? string.Empty : " (" + note.By +
                                           (note.At.HasValue ? " " + note.At.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : string.Empty) + ")"));
                    }
                    else if (acc.RequireIgnoreReasons)
                    {
                        // Excluded all the same: the missing reason is a warning to document it.
                        entry.Found.Add("excluded");
                        entry.Evidence.Add("excluded by " + list + " (" + (note.Code ?? token) + "); the entry gives no reason");
                        var item = Item(ReviewKinds.PointExcludedNoReason, entry,
                            Quote(raw) + " is excluded by " + list + " (" + (note.Code ?? token) + ") but the entry gives no reason.");
                        item.Code = note.Code ?? token;
                        items.Add(item);
                        entry.Issues.Add(item.Id);
                    }
                    if (note.ExpectsSurface) Surface(entry, p, token, acc, items, "its ignore entry says it is surface data");
                }
                return;
            }
            if (parsed.Ignored) entry.Evidence.Add("on the ignore list (nothing drawn for it); its drafting standard applies");

            // What its code's rule draws that FTF places: a block, a dripline, a tag, a gate label.
            Func<string, bool> has = kind => p.Outputs.Any(o => string.Equals(o.Kind, kind, StringComparison.OrdinalIgnoreCase));
            var symbolLabel = parsed.RuleId == FieldCodeParser.SymbolLabelRuleId;
            if (parsed.Recognized)
            {
                entry.Reading = entry.Reading == null || entry.Reading.StartsWith("linework", StringComparison.Ordinal)
                    ? (entry.Reading == null ? string.Empty : entry.Reading + "; ") + (symbolLabel ? "bare code " + token : "rule " + (parsed.RuleId ?? parsed.Code ?? token))
                    : entry.Reading;
                if (parsed.InsertBlock && !string.IsNullOrEmpty(parsed.BlockName))
                    Want(entry, items, "block " + parsed.BlockName, has("Block"), "FTF block " + parsed.BlockName + " is not in the drawing for " + p.Number + " (rerun FTFPOINTS).");
                if (parsed.HasDripLine)
                    Want(entry, items, "dripline", has("DripArc"), "The dripline for " + p.Number + " is not in the drawing (rerun FTFDRIP).");
                var ann = settings.Annotation ?? new AnnotationSettings();
                if (!string.IsNullOrEmpty(parsed.TagPrefix) && !parsed.HasErrors)
                {
                    var decision = AnnotationPolicy.Tag(parsed, ann, ctx.TagScope ?? AnnotationRun.TagScope(ann), rules);
                    if (decision.Annotate)
                        Want(entry, items, "tag", has("Tag"), "Tag " + parsed.TagPrefix + "# is not drawn for " + p.Number + " (FTFTAGS).");
                }
                // Gate posts: the gate label names the post.
                var gateCodes = settings.Gates != null && settings.Gates.PostCodes != null ? settings.Gates.PostCodes : new List<string>();
                if (gateCodes.Any(c => string.Equals((c ?? string.Empty).Trim(), ContinuitySettings.BaseCode(token), StringComparison.OrdinalIgnoreCase)) &&
                    ctx.GatePostsLabelled.Contains(p.Number))
                {
                    entry.Found.Add("gate label");
                    entry.Evidence.Add("post of a gate FTF labelled");
                }
            }
            else if (parsed.NoContent) entry.Reading = entry.Reading ?? "bare code " + token;
            else if (req.Specific) entry.Reading = entry.Reading ?? "standard " + (req.Entry != null && !string.IsNullOrWhiteSpace(req.Entry.Name) ? req.Entry.Name : token);

            // What the standard requires.
            Label(entry, p, req, ctx, settings, items);
            Symbol(entry, p, token, req, ctx, acc, items);
            if (req.Surface) Surface(entry, p, token, acc, items, "its drafting standard: " + req.Source);
            if (req.Disposition == IntendedDisposition.ControlCheck)
            {
                entry.Found.Add("control check");
                entry.Evidence.Add("control check shot (" + req.Source + "): in the control check report");
                if (entry.Reading == null || entry.Reading.StartsWith("bare code", StringComparison.Ordinal) ||
                    entry.Reading.StartsWith("standard ", StringComparison.Ordinal)) entry.Reading = "control check " + token;
            }
            if (req.Review)
            {
                var item = Item(ReviewKinds.PointReview, entry, Quote(raw) + ": the standard (" + req.Source + ") asks for every one to be reviewed" +
                                (string.IsNullOrWhiteSpace(req.Note) ? "." : ": " + req.Note));
                items.Add(item);
                entry.Issues.Add(item.Id);
            }
            // Intentionally only the survey point: nothing to draw, and nothing else accounts for it.
            if (entry.Expected.Count == 0 && entry.Found.Count == 0 && !req.Review && entry.Issues.Count == 0)
            {
                entry.Found.Add("point only");
                entry.Evidence.Add("the standard draws nothing for it (" + req.Source + ")");
            }
        }

        /// <summary>The label the standard requires: the one its rule writes (where Label All
        /// labels its family), a required one, the point's own Civil 3D label, or none.</summary>
        private static void Label(LedgerPoint entry, PointFacts p, EffectiveRequirement req, AccountingContext ctx, FtfSettings settings, List<ReviewItem> items)
        {
            var parsed = p.Parsed;
            var forLabel = p.ForLabel ?? parsed;
            var ftf = p.Outputs.Any(o => string.Equals(o.Kind, "Label", StringComparison.OrdinalIgnoreCase));
            var labelText = forLabel != null && !string.IsNullOrEmpty(forLabel.LabelText) && !forLabel.HasErrors ? forLabel.LabelDisplay : null;
            switch (req.Label)
            {
                case LabelRequirement.None:
                    if (ftf) entry.Evidence.Add("an FTF label is drawn (the standard does not require one)");
                    return;

                case LabelRequirement.AsPlanned:
                {
                    if (labelText == null) return;
                    var ann = settings.Annotation ?? new AnnotationSettings();
                    var decision = AnnotationPolicy.Label(forLabel, ann, ctx.LabelScope ?? AnnotationRun.LabelScope(ann), ctx.Rules);
                    if (!decision.Annotate) { entry.Evidence.Add("no label expected: " + decision.Reason); return; }
                    if (ftf) Want(entry, items, "label", true, null);
                    else if (p.HandLabelled) HandLabel(entry, forLabel);
                    else Want(entry, items, "label", false, "Label \"" + labelText + "\" is not drawn for " + p.Number + " (FTFLABELS, or FTFLABELALL).");
                    return;
                }

                default:
                {
                    entry.Expected.Add("label");
                    if (ftf) { entry.Found.Add("label"); return; }
                    if (p.HandLabelled && forLabel != null) { HandLabel(entry, forLabel); return; }
                    var m = p.Marker;
                    // The point's own label stands in only where it says all FTF's label would.
                    var saysIt = FeatureRequirements.PointLabelSaysIt(forLabel, p.RawDescription);
                    if (req.Label == LabelRequirement.PointLabel && saysIt && m != null && m.LabelShown == true &&
                        (req.LabelStyles.Count == 0 || FeatureStandard.MatchesAny(req.LabelStyle, m.LabelStyleName)))
                    {
                        entry.Found.Add("label (point label)");
                        entry.Evidence.Add("its own Civil 3D label shows it (label style " + (m.LabelStyleName ?? "?") + ")");
                        return;
                    }
                    string why;
                    if (req.Label == LabelRequirement.PointLabel && !saysIt)
                        why = "its FTF label (\"" + labelText + "\") says more than the description its Civil 3D label shows";
                    else if (req.Label == LabelRequirement.PointLabel)
                        why = m == null || !m.LabelShown.HasValue ? "its Civil 3D label could not be read"
                            : m.LabelShown == false ? "its Civil 3D label is hidden"
                            : "its Civil 3D label style " + (m.LabelStyleName ?? "?") + " is not one the standard names (" + req.LabelStyle + ")";
                    else why = "the standard requires a label";
                    Missing(entry, items, "label", Quote(p.RawDescription) + ": " + why + ", and no FTF label is drawn" +
                            (labelText == null ? " (no rule writes its label: label it by hand, or give it a rule)" : " (\"" + labelText + "\": FTFLABELS)") +
                            ". Standard: " + req.Source + ".");
                    return;
                }
            }
        }

        private static void HandLabel(LedgerPoint entry, ParsedPoint forLabel)
        {
            entry.Expected.Add("label");
            entry.Found.Add("label (typed by hand)");
            entry.Evidence.Add("labelled by hand: \"" + forLabel.LabelDisplay + "\" typed nearby");
        }

        private static void Want(LedgerPoint entry, List<ReviewItem> items, string what, bool found, string missingReason)
        {
            entry.Expected.Add(what);
            if (found) { entry.Found.Add(what); return; }
            Missing(entry, items, what, missingReason);
        }

        private static void Missing(LedgerPoint entry, List<ReviewItem> items, string what, string reason)
        {
            entry.Missing.Add(what);
            var id = ReviewKinds.PointMissingOutput + ":" + entry.Number;
            var existing = items.FirstOrDefault(i => i.Id == id);
            if (existing != null)
            {
                existing.Reason += " " + reason;
                existing.Fingerprint = Review.Fingerprint.Of(entry.Fingerprint, string.Join("|", entry.Missing.ToArray()));
                return;
            }
            var item = Item(ReviewKinds.PointMissingOutput, entry, reason);
            item.Fingerprint = Review.Fingerprint.Of(entry.Fingerprint, string.Join("|", entry.Missing.ToArray()));
            items.Add(item);
            entry.Issues.Add(item.Id);
        }

        /// <summary>The symbol the standard requires: its own (a block FTF placed, or a point style
        /// whose marker is the code's symbol, or one the standard names), any point style marker,
        /// or none. A generic marker is flagged only where the standard requires the feature's own.</summary>
        private static void Symbol(LedgerPoint entry, PointFacts p, string token, EffectiveRequirement req, AccountingContext ctx, AccountabilitySettings acc, List<ReviewItem> items)
        {
            var block = p.Outputs.Any(o => string.Equals(o.Kind, "Block", StringComparison.OrdinalIgnoreCase));
            if (req.Symbol == SymbolRequirement.None) return;
            entry.Expected.Add("symbol");
            if (block) { entry.Found.Add("symbol"); return; }
            var m = p.Marker;
            string problem;
            if (req.Symbol == SymbolRequirement.PointStyle)
            {
                if (m != null && m.Known && m.Visible &&
                    (req.SymbolStyles.Count == 0 || FeatureStandard.MatchesAny(req.SymbolStyle, m.StyleName) || FeatureStandard.MatchesAny(req.SymbolStyle, m.SymbolName)))
                {
                    entry.Found.Add("point style");
                    entry.Evidence.Add("shown by its point style " + (m.StyleName ?? "?"));
                    return;
                }
                problem = m == null || !m.Known ? "its marker could not be read" + (m != null && !string.IsNullOrWhiteSpace(m.Problem) ? " (" + m.Problem + ")" : string.Empty)
                        : !m.Visible ? "its point style draws no marker (" + (m.StyleName ?? "style") + ")"
                        : "its point style " + (m.StyleName ?? "?") + " is not one the standard names (" + req.SymbolStyle + ")";
            }
            else
            {
                if (OwnSymbol(m, token, ctx, acc, req, out problem))
                {
                    entry.Found.Add("symbol");
                    entry.Evidence.Add("its own symbol: " + (m.SymbolName ?? m.StyleName) + " (style " + (m.StyleName ?? "?") + ")");
                    return;
                }
            }
            entry.Missing.Add("symbol");
            var item = Item(ReviewKinds.PointNoSymbol, entry, Quote(p.RawDescription) + ": " + problem + ". Standard: " + req.Source + " (" + req.Describe() + ").");
            item.Code = ContinuitySettings.BaseCode(token);
            item.Fingerprint = Review.Fingerprint.Of(entry.Fingerprint, m == null ? "-" : (m.MarkerType ?? "") + "|" + (m.SymbolName ?? "") + "|" + (m.StyleName ?? "") + "|" + m.Visible);
            items.Add(item);
            entry.Issues.Add(item.Id);
        }

        /// <summary>True when the marker is the code's own symbol block, or a point style or symbol
        /// the standard names for it.</summary>
        public static bool OwnSymbol(MarkerFact m, string code, AccountingContext ctx, AccountabilitySettings acc, out string problem)
        {
            return OwnSymbol(m, code, ctx, acc, null, out problem);
        }

        public static bool OwnSymbol(MarkerFact m, string code, AccountingContext ctx, AccountabilitySettings acc, EffectiveRequirement req, out string problem)
        {
            problem = null;
            var bare = ContinuitySettings.BaseCode(code);
            if (m == null || !m.Known) { problem = "its marker could not be read" + (m != null && !string.IsNullOrWhiteSpace(m.Problem) ? " (" + m.Problem + ")" : string.Empty); return false; }
            if (!m.Visible) { problem = "its point style draws no marker (" + (m.StyleName ?? "style") + ")"; return false; }
            // A point style the standard names as the feature's own.
            if (req != null && req.SymbolStyles.Count > 0 &&
                (FeatureStandard.MatchesAny(req.SymbolStyle, m.StyleName) || FeatureStandard.MatchesAny(req.SymbolStyle, m.SymbolName)))
                return true;
            if (!string.Equals(m.MarkerType, "UseSymbolForMarker", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(m.SymbolName))
            {
                problem = "its marker is a generic " + (string.Equals(m.MarkerType, "UsePointForMarker", StringComparison.OrdinalIgnoreCase) ? "point" : "custom") +
                          " marker (style " + (m.StyleName ?? "?") + "), not " + bare + "'s symbol";
                return false;
            }
            var symbol = m.SymbolName.Trim();
            foreach (var pattern in acc.SymbolBlocksFor(bare))
                if (OrientationStandard.Glob(pattern, symbol)) return true;
            var orientation = ctx.Rules != null ? ctx.Rules.Orientation : null;
            var rule = orientation != null ? orientation.SymbolFor(code) : null;
            if (rule != null && rule.DrawnWith(symbol)) return true;
            foreach (var word in symbol.ToUpperInvariant().Split(new[] { '-', '_', ' ', '.', '$', '|' }, StringSplitOptions.RemoveEmptyEntries))
                if (word == bare) return true;
            problem = "its marker is block " + symbol + " (style " + (m.StyleName ?? "?") + "), which is not " + bare + "'s symbol" +
                      (rule != null && !string.IsNullOrWhiteSpace(rule.Block) ? " (" + rule.Block + ")" : string.Empty) +
                      " -- name it in the feature's symbol style (Point Features) or under Point Accountability > symbol blocks if it is";
            return false;
        }

        /// <summary>
        /// Surface data. Accounted for as a designated surface point; checked against the surfaces
        /// only when Verify surface membership is on, and then a verified member, an explicit
        /// exclusion (a warning), or not verifiable (information, one item per code). A missing
        /// surface is never a drafting error.
        /// </summary>
        private static void Surface(LedgerPoint entry, PointFacts p, string token, AccountabilitySettings acc, List<ReviewItem> items, string why)
        {
            entry.Found.Add("surface data");
            entry.Evidence.Add("designated surface point (" + why + ")");
            entry.SurfaceStatus = "designated";
            if (!acc.VerifySurfaceMembership) return;

            var defined = p.Surfaces.Where(s => s.InDefinition == true && s.IsVertex).ToList();
            var coincident = p.Surfaces.Where(s => s.IsVertex).ToList();
            if (defined.Count > 0 || coincident.Count > 0)
            {
                entry.SurfaceStatus = "verified";
                foreach (var s in defined)
                    entry.Evidence.Add("surface " + s.Surface + ": in its definition" + (string.IsNullOrWhiteSpace(s.PointGroup) ? string.Empty : " (point group " + s.PointGroup + ")") + " and a TIN vertex");
                foreach (var s in coincident.Where(c => !defined.Contains(c)))
                    entry.Evidence.Add("surface " + s.Surface + ": a TIN vertex at the point (" +
                                       (s.InDefinition == false ? "not through a point group in its definition: a point file, breakline or edit" : "its definition could not be read") +
                                       "; coordinates only)");
                return;
            }
            var unused = p.Surfaces.Where(s => s.InDefinition == true).ToList();
            if (unused.Count > 0)
            {
                entry.SurfaceStatus = "excluded";
                var item = Item(ReviewKinds.PointSurfaceExcluded, entry, "Point " + p.Number + " " + Quote(p.RawDescription) + " is in surface " + unused[0].Surface + "'s definition" +
                                (string.IsNullOrWhiteSpace(unused[0].PointGroup) ? string.Empty : " (point group " + unused[0].PointGroup + ")") +
                                " but its triangulation leaves it out (a boundary, an edge limit or a duplicate elevation).");
                items.Add(item);
                entry.Issues.Add(item.Id);
                return;
            }
            entry.SurfaceStatus = "not verifiable";
            var code = ContinuitySettings.BaseCode(token);
            var id = ReviewKinds.PointSurfaceUnverified + ":" + code;
            var group = items.FirstOrDefault(i => i.Id == id);
            if (group == null)
            {
                group = ReviewKinds.New(ReviewKinds.PointSurfaceUnverified, code, null);
                group.Source = ReviewKinds.PointsSource;
                group.Code = code;
                group.Points = string.Empty;
                items.Add(group);
            }
            var numbers = (group.Points ?? string.Empty).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToList();
            numbers.Add(p.Number);
            group.Points = string.Join(", ", numbers.ToArray());
            group.Reason = numbers.Count + " " + code + " point(s) are designated surface data, but no surface in the drawing can be shown to hold them " +
                           "(no surface is built from them, or not from point groups). Nothing to draft.";
            group.Fingerprint = Review.Fingerprint.Of(numbers.ToArray());
            if (group.X == 0 && group.Y == 0) { group.X = entry.X; group.Y = entry.Y; }
            if (!entry.Issues.Contains(id)) entry.Issues.Add(id);
        }

        private static PointDisposition Strongest(LedgerPoint e)
        {
            if (e.Evidence.Any(x => x.StartsWith("FTF ", StringComparison.Ordinal)) || e.Found.Contains("label (typed by hand)") || e.Found.Contains("gate label")) return PointDisposition.Drawn;
            if (e.Found.Any(f => f.StartsWith("linework ", StringComparison.Ordinal))) return PointDisposition.Linework;
            if (e.Found.Contains("control") || e.Found.Contains("control table") || e.Found.Contains("control check")) return PointDisposition.Control;
            if (e.Found.Contains("symbol")) return PointDisposition.Symbol;
            if (e.Found.Contains("point style") || e.Found.Contains("label (point label)")) return PointDisposition.PointStyle;
            if (e.Found.Contains("surface") || e.Found.Contains("surface data")) return PointDisposition.Surface;
            if (e.Found.Contains("excluded")) return PointDisposition.Excluded;
            if (e.Found.Contains("point only")) return PointDisposition.PointOnly;
            return PointDisposition.Review;
        }

        // ------------------------------------------------------------------ helpers

        private static string Family(AccountingContext ctx, string code)
        {
            var c = ContinuitySettings.BaseCode(code);
            var f = ctx.FamilyOf != null ? ctx.FamilyOf(c) : null;
            return string.IsNullOrWhiteSpace(f) ? c : f.Trim().ToUpperInvariant();
        }

        private static ReviewItem Item(string kind, LedgerPoint entry, string reason)
        {
            var item = ReviewKinds.New(kind, entry.Number, reason);
            item.Source = ReviewKinds.PointsSource;
            item.Points = entry.Number;
            item.Code = entry.Code;
            if (!string.IsNullOrEmpty(entry.Handle)) item.Handles.Add(entry.Handle);
            item.X = entry.X;
            item.Y = entry.Y;
            item.Fingerprint = entry.Fingerprint;
            return item;
        }

        public static string PointFingerprint(string raw, double x, double y, double z)
        {
            return Review.Fingerprint.Of((raw ?? string.Empty),
                                         x.ToString("0.###", CultureInfo.InvariantCulture),
                                         y.ToString("0.###", CultureInfo.InvariantCulture),
                                         z.ToString("0.###", CultureInfo.InvariantCulture));
        }

        private static Dictionary<string, List<string>> Duplicates(IList<PointFacts> points, double tolerance)
        {
            var found = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            if (tolerance <= 0) tolerance = 1e-9;
            var cell = Math.Max(tolerance, 1e-6);
            var grid = new Dictionary<long, List<PointFacts>>();
            Func<double, long> k = v => (long)Math.Floor(v / cell);
            foreach (var p in points)
            {
                var key = k(p.X) * 73856093L ^ k(p.Y) * 19349663L;
                List<PointFacts> bucket;
                if (!grid.TryGetValue(key, out bucket)) { bucket = new List<PointFacts>(); grid[key] = bucket; }
                bucket.Add(p);
            }
            foreach (var p in points)
            {
                var kx = k(p.X); var ky = k(p.Y);
                for (var dx = -1; dx <= 1; dx++)
                    for (var dy = -1; dy <= 1; dy++)
                    {
                        List<PointFacts> bucket;
                        if (!grid.TryGetValue((kx + dx) * 73856093L ^ (ky + dy) * 19349663L, out bucket)) continue;
                        foreach (var q in bucket)
                        {
                            if (ReferenceEquals(p, q)) continue;
                            if (Math.Sqrt((p.X - q.X) * (p.X - q.X) + (p.Y - q.Y) * (p.Y - q.Y)) > tolerance) continue;
                            List<string> list;
                            if (!found.TryGetValue(p.Number, out list)) { list = new List<string>(); found[p.Number] = list; }
                            if (!list.Contains(q.Number)) list.Add(q.Number);
                        }
                    }
            }
            foreach (var list in found.Values) list.Sort((a, b) => SourcePointIndex.NumberKey(a).CompareTo(SourcePointIndex.NumberKey(b)));
            return found;
        }

        private static string Change(LedgerPoint old, LedgerPoint now, double upf)
        {
            var parts = new List<string>();
            if (!string.Equals(old.RawDescription ?? string.Empty, now.RawDescription ?? string.Empty, StringComparison.Ordinal))
                parts.Add("description " + Quote(old.RawDescription) + " -> " + Quote(now.RawDescription));
            var moved = Math.Sqrt((old.X - now.X) * (old.X - now.X) + (old.Y - now.Y) * (old.Y - now.Y)) / upf;
            if (moved > 0.0005) parts.Add("moved " + moved.ToString("0.###", CultureInfo.InvariantCulture) + "'");
            if (Math.Abs(old.Z - now.Z) / upf > 0.0005) parts.Add("elevation " + old.Z.ToString("0.###", CultureInfo.InvariantCulture) + " -> " + now.Z.ToString("0.###", CultureInfo.InvariantCulture));
            return parts.Count == 0 ? "changed" : string.Join("; ", parts.ToArray());
        }

        private static LedgerPoint Copy(LedgerPoint p)
        {
            return JsonConvert.DeserializeObject<LedgerPoint>(JsonConvert.SerializeObject(p));
        }

        private static string Describe(LedgerPoint p)
        {
            return Quote(p.RawDescription) + " at " + p.X.ToString("0.###", CultureInfo.InvariantCulture) + ", " +
                   p.Y.ToString("0.###", CultureInfo.InvariantCulture) + ", " + p.Z.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string Quote(string s) { return "\"" + (s ?? string.Empty).Trim() + "\""; }
    }
}
