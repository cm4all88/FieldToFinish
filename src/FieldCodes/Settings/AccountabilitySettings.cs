using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;

namespace FieldCodes.Settings
{
    /// <summary>
    /// How one feature family's linework is checked for continuity: how far apart two ends may be
    /// and still be one string, how many shots may come between, how sharp a turn is still a
    /// continuation. The values shipped are INITIAL TESTING VALUES chosen for the first trials,
    /// not proven standards: tune them per feature against real drawings.
    /// </summary>
    public sealed class FeatureContinuity
    {
        /// <summary>The feature codes this row is for (FNC, FHW, ...); empty on the defaults row.</summary>
        [JsonProperty("codes", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<string> Codes { get; set; }

        /// <summary>A short name for people ("Fences").</summary>
        [JsonProperty("name")] public string Name { get; set; }

        /// <summary>Checked at all. Off: no continuity items for these codes.</summary>
        [JsonProperty("enabled")] public bool Enabled { get; set; }

        /// <summary>A gap longer than this many times the strings' median shot spacing is not
        /// proposed as a missing connection.</summary>
        [JsonProperty("spacingFactor")] public double SpacingFactor { get; set; }

        /// <summary>The longest gap ever proposed, whatever the spacing (feet).</summary>
        [JsonProperty("maxGapFeet")] public double MaxGapFeet { get; set; }

        /// <summary>How many shots may come between the end of one string and the start of the
        /// next for the two to be one interrupted collection sequence.</summary>
        [JsonProperty("sequenceWindow")] public int SequenceWindow { get; set; }

        /// <summary>The sharpest turn (degrees) across a gap that still reads as one line.</summary>
        [JsonProperty("maxDeflectionDegrees")] public double MaxDeflectionDegrees { get; set; }

        /// <summary>Another feature's line crossing the gap (a driveway edge, a road, a wall)
        /// makes the gap intentional.</summary>
        [JsonProperty("crossingMeansIntentional")] public bool CrossingMeansIntentional { get; set; }

        /// <summary>Gate posts at both ends make the gap a gate opening, never a missing line.</summary>
        [JsonProperty("gatePostsMeanIntentional")] public bool GatePostsMeanIntentional { get; set; }

        public FeatureContinuity()
        {
            Codes = new List<string>();
            Enabled = true;
            SpacingFactor = 3.0;
            MaxGapFeet = 50.0;
            SequenceWindow = 25;
            MaxDeflectionDegrees = 30.0;
            CrossingMeansIntentional = true;
            GatePostsMeanIntentional = true;
        }

        public FeatureContinuity Copy()
        {
            var c = (FeatureContinuity)MemberwiseClone();
            c.Codes = new List<string>(Codes ?? new List<string>());
            return c;
        }
    }

    /// <summary>Linework continuity: which features are checked and with what limits.</summary>
    public sealed class ContinuitySettings
    {
        [JsonProperty("enabled")] public bool Enabled { get; set; }

        /// <summary>The non-plotted layer proposals are previewed on.</summary>
        [JsonProperty("proposalLayer")] public string ProposalLayer { get; set; }

        /// <summary>The values for any feature without its own row.</summary>
        [JsonProperty("defaults")] public FeatureContinuity Defaults { get; set; }

        /// <summary>Rows per feature family; the first row naming a code wins.</summary>
        [JsonProperty("features", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<FeatureContinuity> Features { get; set; }

        /// <summary>Shots within this distance (feet) of a line count as on it.</summary>
        [JsonProperty("onLineToleranceFeet")] public double OnLineToleranceFeet { get; set; }

        public ContinuitySettings() { RestoreDefaults(); }

        public void RestoreDefaults()
        {
            Enabled = true;
            ProposalLayer = "FTF-QC-CONTINUITY";
            OnLineToleranceFeet = 0.05;
            Defaults = new FeatureContinuity { Name = "Any other linework" };
            Features = new List<FeatureContinuity>
            {
                new FeatureContinuity { Name = "Fences", Codes = new List<string> { "FNC", "FHW", "FBD", "FRL", "FBW", "FCK", "GRL", "HDR" } },
                new FeatureContinuity { Name = "Walls", Codes = new List<string> { "RWC", "RWB", "RWT", "RWRK", "RWBK", "RWMT", "RWWD", "JBAR" } },
                new FeatureContinuity { Name = "Curbs and flowlines", Codes = new List<string> { "CU", "CX", "CG", "TBC", "AFL", "TILT" } },
                new FeatureContinuity { Name = "Pavement and surface edges", Codes = new List<string> { "EP", "EC", "EGR", "EGG", "PATH", "PATCH", "ASPH" } },
                new FeatureContinuity { Name = "Buildings", Codes = new List<string> { "BLD", "BLC", "BLE" }, MaxDeflectionDegrees = 95.0 },
                new FeatureContinuity { Name = "Striping", Codes = new List<string> { "LN", "LNSO", "FOG", "LNDY", "XW", "BAR" }, Enabled = false },
            };
        }

        /// <summary>The row that governs a feature code (figure digits ignored).</summary>
        public FeatureContinuity For(string code)
        {
            var c = BaseCode(code);
            foreach (var f in Features ?? new List<FeatureContinuity>())
                if (f != null && f.Codes != null && f.Codes.Any(x => string.Equals((x ?? string.Empty).Trim(), c, StringComparison.OrdinalIgnoreCase)))
                    return f;
            return Defaults ?? new FeatureContinuity();
        }

        public static string BaseCode(string code)
        {
            var c = (code ?? string.Empty).Trim().ToUpperInvariant();
            var end = c.Length;
            while (end > 1 && char.IsDigit(c[end - 1])) end--;
            return c.Substring(0, end);
        }

        public void Validate(ICollection<string> problems)
        {
            if (Defaults == null) Defaults = new FeatureContinuity { Name = "Any other linework" };
            if (Features == null) Features = new List<FeatureContinuity>();
            if (string.IsNullOrWhiteSpace(ProposalLayer)) ProposalLayer = "FTF-QC-CONTINUITY";
            if (OnLineToleranceFeet < 0) problems.Add("Linework continuity: the on-line tolerance cannot be negative.");
            foreach (var f in new[] { Defaults }.Concat(Features))
            {
                if (f == null) continue;
                var name = string.IsNullOrWhiteSpace(f.Name) ? string.Join(", ", (f.Codes ?? new List<string>()).ToArray()) : f.Name;
                if (f.SpacingFactor <= 0) problems.Add("Linework continuity (" + name + "): the spacing factor must be greater than zero.");
                if (f.MaxGapFeet <= 0) problems.Add("Linework continuity (" + name + "): the longest gap must be greater than zero.");
                if (f.SequenceWindow < 0) problems.Add("Linework continuity (" + name + "): the sequence window cannot be negative.");
                if (f.MaxDeflectionDegrees < 0 || f.MaxDeflectionDegrees > 180)
                    problems.Add("Linework continuity (" + name + "): the sharpest turn must be 0 to 180 degrees.");
            }
            var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in Features)
                foreach (var code in (f == null ? new List<string>() : f.Codes ?? new List<string>()))
                {
                    if (string.IsNullOrWhiteSpace(code)) continue;
                    string other;
                    if (seen.TryGetValue(code.Trim(), out other))
                        problems.Add("Linework continuity: " + code.Trim() + " is in two rows (" + other + ", " + f.Name + "); the first wins.");
                    else seen[code.Trim()] = f.Name;
                }
        }
    }

    /// <summary>
    /// Point accountability: every imported survey point is accounted for by what the drawing
    /// actually holds for it -- an FTF object, a line through it, its symbol, a surface built
    /// from it, the control table, or an exclusion with a reason -- and what is missing goes on
    /// the review list. Read-only for the points and the survey linework; it writes the review
    /// list, the point ledger in the drawing, the non-plotted proposal previews, and a report
    /// beside the drawing.
    /// </summary>
    public sealed class AccountabilitySettings : ISettingsSection
    {
        public string Title { get { return "Point Accountability"; } }
        public string AffectedCommands { get { return "FTFRUN, FTFLABELALL, FTFREVIEWCHECK, FTFQC, FTFREVIEW"; } }

        /// <summary>Run the accountability and continuity checks after FTFRUN, the window's
        /// processing and FTFLABELALL. FTFREVIEWCHECK and FTFQC always run them.</summary>
        [JsonProperty("runAfterProcessing")] public bool RunAfterProcessing { get; set; }

        /// <summary>Two points closer than this (plan, feet) are duplicates.</summary>
        [JsonProperty("duplicateToleranceFeet")] public double DuplicateToleranceFeet { get; set; }

        /// <summary>A surface vertex this close to a point (plan and elevation, feet) is the point.</summary>
        [JsonProperty("surfaceToleranceFeet")] public double SurfaceToleranceFeet { get; set; }

        /// <summary>
        /// Code -> the symbol blocks that are that code's own marker (wildcards allowed).
        /// A bare code is accounted for by its marker only when the marker is its own symbol:
        /// one named here, one the orientation standard gives the code, or one whose name holds
        /// the code as a word (PMX-CB for CB). A generic marker (a dot, an X, another code's
        /// symbol) never is.
        /// </summary>
        [JsonProperty("symbolBlocks")]
        public Dictionary<string, List<string>> SymbolBlocks { get; set; }

        /// <summary>An ignore-list entry without a reason makes its points review items.</summary>
        [JsonProperty("requireIgnoreReasons")] public bool RequireIgnoreReasons { get; set; }

        /// <summary>Write the point ledger beside the drawing (&lt;drawing&gt;.ftf-points.csv).</summary>
        [JsonProperty("writeReport")] public bool WriteReport { get; set; }

        /// <summary>
        /// Check that surface points (ground shots, and any feature the standard marks as surface
        /// data) are in a surface: verified member, explicitly excluded (in a surface's definition
        /// but left out of its triangulation), or not verifiable. Off: a surface point is accounted
        /// for as designated surface data, whether or not the drawing has a surface. Never a
        /// drafting error either way: excluded points are warnings, the rest information.
        /// </summary>
        [JsonProperty("verifySurfaceMembership")] public bool VerifySurfaceMembership { get; set; }

        /// <summary>A control check shot is paired with the control point within this distance
        /// (plan, feet) in the control check report.</summary>
        [JsonProperty("controlCheckSearchFeet")] public double ControlCheckSearchFeet { get; set; }

        [JsonProperty("continuity")] public ContinuitySettings Continuity { get; set; }

        public AccountabilitySettings() { RestoreDefaults(); }

        public void RestoreDefaults()
        {
            RunAfterProcessing = true;
            DuplicateToleranceFeet = 0.01;
            SurfaceToleranceFeet = 0.01;
            SymbolBlocks = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            RequireIgnoreReasons = true;
            WriteReport = true;
            VerifySurfaceMembership = false;
            ControlCheckSearchFeet = 2.0;
            Continuity = new ContinuitySettings();
        }

        public void Validate(ICollection<string> problems)
        {
            if (SymbolBlocks == null) SymbolBlocks = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            if (Continuity == null) Continuity = new ContinuitySettings();
            if (DuplicateToleranceFeet < 0) problems.Add("Point Accountability: the duplicate tolerance cannot be negative.");
            if (SurfaceToleranceFeet < 0) problems.Add("Point Accountability: the surface tolerance cannot be negative.");
            if (ControlCheckSearchFeet <= 0) problems.Add("Point Accountability: the control check search distance must be greater than zero.");
            Continuity.Validate(problems);
        }

        /// <summary>The symbol blocks named here for a code (sequence digits ignored).</summary>
        public IList<string> SymbolBlocksFor(string code)
        {
            var c = ContinuitySettings.BaseCode(code);
            var list = new List<string>();
            if (SymbolBlocks == null) return list;
            foreach (var pair in SymbolBlocks)
                if (string.Equals((pair.Key ?? string.Empty).Trim(), c, StringComparison.OrdinalIgnoreCase) && pair.Value != null)
                    list.AddRange(pair.Value.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()));
            return list;
        }

        internal static string Fmt(double v)
        {
            return v.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }
}
