using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace FieldCodes.RecordSurvey
{
    /// <summary>The kinds of text a recorded survey carries, as far as this build tells them apart.</summary>
    public static class SurveyEntityKind
    {
        public const string BearingDistance = "BearingDistance";
        public const string Bearing = "Bearing";
        public const string Distance = "Distance";
        public const string CurveData = "CurveData";
        public const string CurveTableRow = "CurveTableRow";
        public const string LineTableRow = "LineTableRow";
        public const string LotNumber = "LotNumber";
        public const string BlockNumber = "BlockNumber";
        public const string Tract = "Tract";
        public const string RecordReference = "RecordReference";
        public const string RecordSourceKey = "RecordSourceKey";
        public const string Monument = "Monument";
        public const string BasisOfBearing = "BasisOfBearing";
        public const string Scale = "Scale";
        public const string StreetName = "StreetName";
        public const string SheetNumber = "SheetNumber";
        public const string SurveyTitle = "SurveyTitle";
        public const string Surveyor = "Surveyor";
        public const string Legend = "Legend";
        public const string Note = "Note";
        public const string Certification = "Certification";
        public const string Other = "Other";
    }

    /// <summary>What a line of document text was recognised as.</summary>
    public sealed class ClassifiedLine
    {
        public DocumentLine Line { get; set; }
        public int Page { get; set; }
        public string Kind { get; set; }
        public double Confidence { get; set; }
        public List<SurveyToken> Tokens { get; set; }
        /// <summary>For lots, blocks, tracts, references, sheets: the identifier found ("7", "A", "R1", "3 OF 4").</summary>
        public string Key { get; set; }

        public ClassifiedLine() { Tokens = new List<SurveyToken>(); Confidence = 1.0; }

        public SourceRef Source
        {
            get { return new SourceRef(Page, Line.Box, Line.Text, Line.EffectiveConfidence); }
        }

        public override string ToString() { return Kind + (Key != null ? "[" + Key + "]" : string.Empty) + ": " + Line.Text; }
    }

    /// <summary>
    /// Decides what each line of document text is. Rules, not learning: every decision can
    /// be read off the regular expressions below, and a line no rule claims is Other rather
    /// than a guess. Pure and unit tested.
    /// </summary>
    public static class EntityClassifier
    {
        private static readonly RegexOptions O = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

        private static readonly Regex Lot = new Regex(@"^\s*(?:LOT|LT\.?)\s*(?:NO\.?\s*)?(?<n>\d{1,4}[A-Z]?)\b(?<rest>.*)$", O);
        private static readonly Regex Block = new Regex(@"\bBLOCK\s*(?:NO\.?\s*)?(?<n>\d{1,4}[A-Z]?)\b", O);
        private static readonly Regex TractRx = new Regex(@"^\s*TRACT\s+(?<n>[A-Z]{1,2}|\d{1,3})\b", O);
        private static readonly Regex Afn = new Regex(
            @"\b(?:A\.?\s?F\.?\s?N\.?|AFN|AUDITOR'?S?\s+FILE\s+(?:NO|NUMBER)\.?|REC(?:ORDING)?\.?\s*(?:NO|NUMBER|#)\.?|RECORDED\s+UNDER\s+(?:NO\.?|NUMBER|AFN|A\.F\.N\.)|INSTRUMENT\s+(?:NO|NUMBER)\.?)\s*:?\s*(?<n>\d{6,16}(?:-\d+)?)", O);
        private static readonly Regex VolPage = new Regex(
            @"\b(?:VOL(?:UME)?\.?)\s*(?<v>\d{1,4})\s*(?:OF\s+(?<of>PLATS|SURVEYS|SHORT\s+PLATS|DEEDS))?\s*,?\s*(?:PAGES?|PGS?|PS?)\.?\s*(?<p>\d{1,4}(?:\s*(?:-|THRU|THROUGH|AND|&)\s*\d{1,4})?)", O);
        private static readonly Regex RefKey = new Regex(@"^\s*\(?\s*(?<id>R\d{1,2})\s*\)?\s*[:=-]?\s*(?<rest>.+)$", O);
        private static readonly Regex MonumentRx = new Regex(
            @"\b(?:FOUND|FD\.?|FND\.?|SET|CALC(?:ULATED|'D)?|CALC\.)\b.*\b(?:REBAR|R/?B|IRON\s+PIPE|I\.?P\.?|IRON\s+ROD|I\.?R\.?|MON(?:UMENT)?\.?|PIN|NAIL|TACK|LEAD|CAP|BRASS|DISK|DISC|PK|MAG|SPIKE|STONE|CONC(?:RETE)?|HUB|CASE|PIPE|BOLT|RIVET|CORNER)\b", O);
        private static readonly Regex MonumentBare = new Regex(
            @"^\s*(?:\d+/\d+""?|5/8|1/2|3/4)?\s*(?:REBAR|IRON\s+PIPE|MON(?:UMENT)?\.?\s+IN\s+CASE|BRASS\s+(?:CAP|DISK)|CONC(?:RETE)?\s+MON)\b", O);
        private static readonly Regex Basis = new Regex(@"\bBASIS\s+OF\s+BEARINGS?\b", O);
        private static readonly Regex ScaleRx = new Regex(@"\bSCALE\s*:?\s*1\s*""?\s*=\s*(?<n>\d{1,4})\s*'?", O);
        private static readonly Regex ScaleBare = new Regex(@"^\s*1\s*""\s*=\s*(?<n>\d{1,4})\s*'\s*$", O);
        private static readonly Regex Street = new Regex(
            @"\b(?:[NSEW]\.?\s?[EW]?\.?\s+)?(?:\d{1,3}(?:ST|ND|RD|TH)|[A-Z]{3,})\s+(?:STREET|ST\.?|AVENUE|AVE\.?|ROAD|RD\.?|WAY|PLACE|PL\.?|COURT|CT\.?|DRIVE|DR\.?|LANE|LN\.?|BOULEVARD|BLVD\.?|HIGHWAY|HWY\.?|CIRCLE|CIR\.?|PARKWAY|PKWY\.?|TERRACE|TER\.?|LOOP|TRAIL|TRL\.?)(?:\s+(?:N\.?E\.?|N\.?W\.?|S\.?E\.?|S\.?W\.?|N\.?|S\.?|E\.?|W\.?))?\s*$", O);
        private static readonly Regex Sheet = new Regex(@"\bSHEET\s*(?<n>\d{1,3})\s*OF\s*(?<m>\d{1,3})\b", O);
        private static readonly Regex Title = new Regex(
            @"\b(?<t>RECORD\s+OF\s+SURVEY|SHORT\s+(?:SUBDIVISION|PLAT)|BOUNDARY\s+LINE\s+ADJUSTMENT|LOT\s+LINE\s+ADJUSTMENT|LARGE\s+LOT\s+(?:SUBDIVISION|PLAT)|BINDING\s+SITE\s+PLAN|PLAT\s+OF|SUBDIVISION|EASEMENT\s+EXHIBIT|EXHIBIT\s+[A-Z]\b)", O);
        private static readonly Regex SurveyorRx = new Regex(@"\b(?:P\.?L\.?S\.?|PROFESSIONAL\s+LAND\s+SURVEYOR|LAND\s+SURVEYOR|SURVEYOR'?S\s+CERTIFICATE|CERTIFICATE\s+NO\.?|LICENSE\s+NO\.?)\b", O);
        private static readonly Regex CertRx = new Regex(@"\b(?:I\s+HEREBY\s+CERTIFY|CERTIFICATION|DECLARATION|ACKNOWLEDGMENT|APPROVALS?|AUDITOR'?S\s+CERTIFICATE|TREASURER'?S\s+CERTIFICATE|SEAL)\b", O);
        private static readonly Regex LegendRx = new Regex(@"^\s*LEGEND\b", O);
        private static readonly Regex NoteRx = new Regex(@"^\s*(?:NOTES?|SURVEY\s+NOTES?|GENERAL\s+NOTES?)\s*:?\s*(?:\d+[.)])?", O);
        private static readonly Regex NumberedNote = new Regex(@"^\s*\d{1,2}[.)]\s+[A-Z]", O);
        private static readonly Regex TableHeader = new Regex(@"\b(?:CURVE|LINE)\s*(?:TABLE|NO\.?|#)\b|\bRADIUS\b.*\bDELTA\b|\bBEARING\b.*\bDISTANCE\b|\bLENGTH\b.*\bDIRECTION\b", O);

        /// <summary>Classifies one line. The tokens are computed once here and reused by the extractor.</summary>
        public static ClassifiedLine Classify(DocumentLine line, int page)
        {
            var result = new ClassifiedLine { Line = line, Page = page, Kind = SurveyEntityKind.Other };
            var text = SurveyCallParser.NormalizeSymbols(line.Text ?? string.Empty).Trim();
            if (text.Length == 0) return result;

            result.Tokens = SurveyCallParser.Tokenize(text);
            var bearings = result.Tokens.Count(t => t.Kind == SurveyTokenKind.Bearing && t.Read != null && t.Read.Ok);
            var distances = result.Tokens.Count(t => t.Kind == SurveyTokenKind.Distance && t.Read != null && t.Read.Ok);
            var angles = result.Tokens.Count(t => t.Kind == SurveyTokenKind.Angle && t.Read != null && t.Read.Ok);
            var keys = result.Tokens.Where(t => t.Kind == SurveyTokenKind.CurveKey).Select(t => t.Name).ToList();
            var curveTag = result.Tokens.FirstOrDefault(t => t.Kind == SurveyTokenKind.CurveTag);
            var lineTag = result.Tokens.FirstOrDefault(t => t.Kind == SurveyTokenKind.LineTag);
            var words = result.Tokens.Where(t => t.Kind == SurveyTokenKind.Word).Select(t => t.Text).ToList();

            Match m;

            // Identity and headings first: "LOT 7" must not become a distance of 7.
            if (TableHeader.IsMatch(text) && bearings == 0 && distances == 0) return Set(result, SurveyEntityKind.Legend, 0.9, null);
            if ((m = Sheet.Match(text)).Success) return Set(result, SurveyEntityKind.SheetNumber, 0.95, m.Groups["n"].Value + " OF " + m.Groups["m"].Value);
            if ((m = ScaleRx.Match(text)).Success || (m = ScaleBare.Match(text)).Success) return Set(result, SurveyEntityKind.Scale, 0.95, m.Groups["n"].Value);
            if (Basis.IsMatch(text)) return Set(result, SurveyEntityKind.BasisOfBearing, 0.95, null);
            if ((m = Afn.Match(text)).Success && bearings == 0)
            {
                var key = RefKey.Match(text);
                return Set(result, SurveyEntityKind.RecordReference, 0.9, key.Success ? key.Groups["id"].Value.ToUpperInvariant() : m.Groups["n"].Value);
            }
            if ((m = VolPage.Match(text)).Success && bearings == 0)
            {
                var key = RefKey.Match(text);
                return Set(result, SurveyEntityKind.RecordReference, 0.85, key.Success ? key.Groups["id"].Value.ToUpperInvariant() : "VOL " + m.Groups["v"].Value + " PG " + m.Groups["p"].Value);
            }
            if ((m = RefKey.Match(text)).Success && bearings == 0 && distances == 0 && m.Groups["rest"].Value.Trim().Length > 3)
                return Set(result, SurveyEntityKind.RecordSourceKey, 0.8, m.Groups["id"].Value.ToUpperInvariant());
            if ((m = Lot.Match(text)).Success && bearings == 0)
            {
                // "LOT 7" or "LOT 7, BLOCK 2"; a lot line with a call on it is still a call.
                var block = Block.Match(text);
                result.Key = m.Groups["n"].Value.ToUpperInvariant() + (block.Success ? "/" + block.Groups["n"].Value.ToUpperInvariant() : string.Empty);
                return Set(result, SurveyEntityKind.LotNumber, 0.9, result.Key);
            }
            if ((m = Block.Match(text)).Success && bearings == 0 && distances == 0) return Set(result, SurveyEntityKind.BlockNumber, 0.9, m.Groups["n"].Value.ToUpperInvariant());
            if ((m = TractRx.Match(text)).Success && bearings == 0) return Set(result, SurveyEntityKind.Tract, 0.9, m.Groups["n"].Value.ToUpperInvariant());

            // Courses.
            if (curveTag != null && (distances + angles + bearings) >= 2 && keys.Count == 0)
                return Set(result, SurveyEntityKind.CurveTableRow, 0.85, curveTag.Name);
            if (lineTag != null && bearings >= 1 && distances >= 1 && keys.Count == 0)
                return Set(result, SurveyEntityKind.LineTableRow, 0.9, lineTag.Name);
            if (keys.Count > 0 && (distances + angles + bearings) >= 1)
                return Set(result, SurveyEntityKind.CurveData, 0.9, curveTag != null ? curveTag.Name : null);
            if (bearings >= 1 && distances >= 1) return Set(result, SurveyEntityKind.BearingDistance, 0.95, null);
            if (bearings >= 1) return Set(result, SurveyEntityKind.Bearing, 0.9, null);

            // Everything that is prose.
            if (MonumentRx.IsMatch(text) || MonumentBare.IsMatch(text)) return Set(result, SurveyEntityKind.Monument, 0.85, null);
            if (LegendRx.IsMatch(text)) return Set(result, SurveyEntityKind.Legend, 0.9, null);
            if ((m = Title.Match(text)).Success) return Set(result, SurveyEntityKind.SurveyTitle, 0.85, m.Groups["t"].Value.ToUpperInvariant());
            if (SurveyorRx.IsMatch(text)) return Set(result, SurveyEntityKind.Surveyor, 0.8, null);
            if (CertRx.IsMatch(text)) return Set(result, SurveyEntityKind.Certification, 0.7, null);
            if (NoteRx.IsMatch(text) || NumberedNote.IsMatch(text)) return Set(result, SurveyEntityKind.Note, 0.7, null);
            if (Street.IsMatch(text) && words.Count <= 6) return Set(result, SurveyEntityKind.StreetName, 0.75, null);

            // A lone distance on a line: usually the second half of a stacked bearing/distance label.
            if (distances == 1 && bearings == 0 && angles == 0 && words.Count <= 1) return Set(result, SurveyEntityKind.Distance, 0.7, null);

            return result;
        }

        private static ClassifiedLine Set(ClassifiedLine c, string kind, double confidence, string key)
        {
            c.Kind = kind;
            c.Confidence = confidence;
            c.Key = key;
            return c;
        }

        /// <summary>The survey type from title lines, as the document names itself.</summary>
        public static string SurveyTypeFrom(IEnumerable<ClassifiedLine> lines)
        {
            foreach (var l in lines.Where(x => x.Kind == SurveyEntityKind.SurveyTitle))
            {
                var t = (l.Key ?? string.Empty).ToUpperInvariant();
                if (t.StartsWith("RECORD OF SURVEY")) return "Record of Survey";
                if (t.StartsWith("SHORT")) return "Short Plat";
                if (t.Contains("BOUNDARY LINE") || t.Contains("LOT LINE")) return "Boundary Line Adjustment";
                if (t.StartsWith("LARGE LOT")) return "Large Lot Subdivision";
                if (t.StartsWith("BINDING SITE")) return "Binding Site Plan";
                if (t.StartsWith("EASEMENT") || t.StartsWith("EXHIBIT")) return "Easement Exhibit";
                if (t.StartsWith("PLAT OF") || t == "SUBDIVISION") return "Subdivision Plat";
            }
            return "Unknown";
        }

        /// <summary>Scale in feet per inch from a scale line, or null.</summary>
        public static double? ScaleOf(ClassifiedLine line)
        {
            if (line == null || line.Kind != SurveyEntityKind.Scale) return null;
            double n;
            return double.TryParse(line.Key, NumberStyles.Float, CultureInfo.InvariantCulture, out n) && n > 0 ? n : (double?)null;
        }

        /// <summary>Recording number and volume/page out of a reference line, when present.</summary>
        public static RecordReference ReferenceOf(ClassifiedLine line)
        {
            if (line == null) return null;
            var text = line.Line.Text ?? string.Empty;
            var r = new RecordReference { Source = line.Source, Confidence = line.Confidence * line.Line.EffectiveConfidence, Description = text.Trim() };
            var key = RefKey.Match(text);
            r.Id = key.Success ? key.Groups["id"].Value.ToUpperInvariant() : string.Empty;
            var afn = Afn.Match(text);
            if (afn.Success) r.RecordingNumber = afn.Groups["n"].Value;
            var vp = VolPage.Match(text);
            if (vp.Success)
            {
                r.Volume = vp.Groups["v"].Value;
                r.Page = vp.Groups["p"].Value;
                if (vp.Groups["of"].Success) r.Kind = vp.Groups["of"].Value.ToUpperInvariant();
            }
            var t = text.ToUpperInvariant();
            if (r.Kind == null)
            {
                if (t.Contains("SURVEY")) r.Kind = "SURVEY";
                else if (t.Contains("PLAT")) r.Kind = "PLAT";
                else if (t.Contains("DEED")) r.Kind = "DEED";
            }
            return r;
        }

        /// <summary>Found / set / calculated from a monument description.</summary>
        public static MonumentStatus MonumentStatusOf(string text)
        {
            var t = (text ?? string.Empty).ToUpperInvariant();
            if (Regex.IsMatch(t, @"\b(?:FOUND|FD\.?|FND\.?)\b")) return MonumentStatus.Found;
            if (Regex.IsMatch(t, @"\bSET\b")) return MonumentStatus.Set;
            if (Regex.IsMatch(t, @"\bCALC")) return MonumentStatus.Calculated;
            return MonumentStatus.Unknown;
        }
    }
}
