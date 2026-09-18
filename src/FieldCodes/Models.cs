using System;
using System.Collections.Generic;
using System.Linq;

namespace FieldCodes
{
    public enum Severity { Info, Warning, Error }

    /// <summary>
    /// A single problem found while parsing. Errors mean the point cannot be drawn
    /// (and indicate the upstream code audit let something through). Warnings mean
    /// the point is drawable but suspicious.
    /// </summary>
    public sealed class Diagnostic
    {
        public Severity Severity { get; private set; }
        public string Code { get; private set; }
        public string Message { get; private set; }

        public Diagnostic(Severity severity, string code, string message)
        {
            Severity = severity;
            Code = code;
            Message = message;
        }

        public override string ToString()
        {
            return string.Format("{0} [{1}] {2}",
                Severity.ToString().ToUpperInvariant(), Code, Message);
        }
    }

    public enum LeaderMode { Auto, Always, Never }

    /// <summary>How multi-stem trunk diameters collapse to a single reported size.</summary>
    public enum StemAverageMethod
    {
        /// <summary>Plain mean of the stem diameters.</summary>
        Arithmetic,
        /// <summary>sqrt(sum of squares). Common in municipal tree ordinances.</summary>
        Quadratic,
        /// <summary>Largest stem plus 50% of each additional stem.</summary>
        LargestPlusHalf,
        /// <summary>Straight sum of all stems.</summary>
        Sum
    }

    public sealed class ResolvedModifier
    {
        public string Id { get; private set; }
        public string Token { get; private set; }
        public int Priority { get; private set; }
        public IDictionary<string, string> Fields { get; private set; }

        public ResolvedModifier(string id, string token, int priority,
                                IDictionary<string, string> fields)
        {
            Id = id;
            Token = token;
            Priority = priority;
            Fields = fields ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Everything the CAD layer needs to draw one point. Contains no Autodesk types
    /// by design -- this assembly must load without a CAD licence.
    /// </summary>
    public sealed class ParsedPoint
    {
        public ParsedPoint()
        {
            Fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Modifiers = new List<ResolvedModifier>();
            Stems = new List<double>();
            Diagnostics = new List<Diagnostic>();
            BlockScale = 1.0;
            StemCount = 1;
            Leader = LeaderMode.Auto;
        }

        // --- source ---
        public string PointNumber { get; set; }
        public string RawDescription { get; set; }

        // --- classification ---

        /// <summary>
        /// The code is on the never-draw list -- ground shots and notes that will never
        /// produce drawing content. Silent by design.
        /// </summary>
        public bool Ignored { get; set; }

        /// <summary>
        /// The description carries nothing to act on -- a bare code such as PP, CB or
        /// ASPH with no value after it. There is nothing to draw and nothing to report:
        /// the survey recorded that a thing is there, and the point style already shows
        /// it. Silent by design.
        ///
        /// If such a code later arrives carrying data ("PP 1234"), it stops being
        /// content-free and is handled or reported like anything else.
        /// </summary>
        public bool NoContent { get; set; }

        /// <summary>
        /// No rule handles this code yet. Distinct from an error: the description is
        /// not malformed, the tool simply has nothing configured for it.
        ///
        /// Kept separate because the difference matters. A code that matches a rule and
        /// then fails inside it is a data problem and stays a loud Error. A code nobody
        /// has configured is an unfinished setup, and reporting hundreds of those as
        /// errors buries the real ones.
        /// </summary>
        public bool Unhandled { get; set; }

        public bool Recognized { get; set; }
        public string RuleId { get; set; }
        public string Code { get; set; }

        /// <summary>Raw named capture groups, plus any contributed by modifiers.</summary>
        public IDictionary<string, string> Fields { get; private set; }
        public IList<ResolvedModifier> Modifiers { get; private set; }

        // --- derived values ---
        public string Species { get; set; }
        public double? TrunkInches { get; set; }
        public IList<double> Stems { get; private set; }
        public int StemCount { get; set; }
        public double? DripRadius { get; set; }

        /// <summary>AutoCAD convention: degrees counter-clockwise from east.</summary>
        public double? RotationDegrees { get; set; }

        // --- drawing instructions ---
        /// <summary>
        /// True only when the rule explicitly opted in. Civil 3D's description keys own
        /// the survey symbol; FTF inserts one only where Civil 3D cannot.
        /// </summary>
        public bool InsertBlock { get; set; }

        /// <summary>
        /// The block this rule names. Populated even when <see cref="InsertBlock"/> is
        /// false, so the setup window can show what a rule would place -- but nothing
        /// draws it unless the rule opted in.
        /// </summary>
        public string BlockName { get; set; }
        public string BlockLayer { get; set; }
        public double BlockScale { get; set; }
        public string DripLayer { get; set; }
        public bool DripUnify { get; set; }
        public string LabelText { get; set; }
        public string LabelLayer { get; set; }
        public LeaderMode Leader { get; set; }
        public string TagPrefix { get; set; }

        // --- results ---
        public IList<Diagnostic> Diagnostics { get; private set; }

        public bool HasErrors
        {
            get { return Diagnostics.Any(d => d.Severity == Severity.Error); }
        }

        public bool HasWarnings
        {
            get { return Diagnostics.Any(d => d.Severity == Severity.Warning); }
        }

        /// <summary>True when this point carries geometry that participates in drip-line union.</summary>
        public bool HasDripLine
        {
            get { return DripRadius.HasValue && DripRadius.Value > 0 && !string.IsNullOrEmpty(DripLayer); }
        }

        public void Add(Severity severity, string code, string message)
        {
            Diagnostics.Add(new Diagnostic(severity, code, message));
        }

        public override string ToString()
        {
            return string.Format("PT {0}  {1}  -> {2}",
                PointNumber, RawDescription, Recognized ? (LabelText ?? RuleId) : "UNRECOGNIZED");
        }
    }
}
