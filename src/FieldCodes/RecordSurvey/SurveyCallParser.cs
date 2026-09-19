using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace FieldCodes.RecordSurvey
{
    /// <summary>A reading of a value that differs from the chosen one: what it would be and how likely.</summary>
    public sealed class ValueAlternative
    {
        public string Text { get; set; }
        public double Value { get; set; }
        public double Confidence { get; set; }
        /// <summary>Why it exists: "second OCR pass", "digit confusion 3/8", "closure".</summary>
        public string Reason { get; set; }

        public override string ToString()
        {
            return Text + " (" + Reason + ")";
        }
    }

    /// <summary>
    /// The outcome of reading one survey value out of document text. Never guesses: a
    /// malformed or impossible value is refused with the reason, and a value read through
    /// OCR look-alike repairs (O for 0, l for 1) says so in its confidence and notes.
    /// </summary>
    public sealed class SurveyValueRead
    {
        public bool Ok { get; set; }
        /// <summary>Azimuth in degrees for directions, feet for distances, degrees for angles.</summary>
        public double Value { get; set; }
        /// <summary>The value as it reads after repairs, in a canonical spelling.</summary>
        public string Normalized { get; set; }
        /// <summary>What was actually on the page.</summary>
        public string Raw { get; set; }
        public double Confidence { get; set; }
        public string Error { get; set; }
        public List<ValueAlternative> Alternatives { get; private set; }
        public List<string> Notes { get; private set; }
        /// <summary>For distances: the unit as written (ft, m, ch, rd). Empty when none was written.</summary>
        public string Unit { get; set; }

        public SurveyValueRead()
        {
            Alternatives = new List<ValueAlternative>();
            Notes = new List<string>();
            Unit = string.Empty;
        }

        public static SurveyValueRead Failure(string raw, string error)
        {
            return new SurveyValueRead { Ok = false, Raw = raw, Error = error, Confidence = 0 };
        }
    }

    /// <summary>
    /// Recognises the ways recorded plats and Records of Survey write bearings, distances,
    /// angles and curve data, as OCR delivers them. The engine's output is repaired only
    /// where a character is a well-known look-alike in a slot that can only hold a digit or
    /// a symbol (O in 89O42' is a degree sign or a zero -- never a letter), each repair
    /// lowering the confidence; anything else is refused. Pure: no Autodesk types.
    /// </summary>
    public static class SurveyCallParser
    {
        // ------------------------------------------------------------ normalisation

        /// <summary>
        /// One canonical spelling of the symbols OCR renders a dozen ways: any degree-like
        /// mark becomes °, any minute-like mark ', any second-like mark ". Letters are
        /// upper-cased. Nothing else changes, so the raw text is still recognisable.
        /// </summary>
        public static string NormalizeSymbols(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            var sb = new StringBuilder(text.Length);
            var chars = text.ToCharArray();
            for (var i = 0; i < chars.Length; i++)
            {
                var c = chars[i];
                switch (c)
                {
                    case 'º': case '˚': case '^': case '*': case '⁰': case '°':
                        sb.Append('°'); break;
                    case '’': case '′': case '`': case '´': case '‘':
                        sb.Append('\''); break;
                    case '”': case '″': case '“':
                        sb.Append('"'); break;
                    case 'Δ': case '∆':      // Δ, ∆
                        sb.Append('Δ'); break;
                    case ' ':
                        sb.Append(' '); break;
                    default:
                        sb.Append(char.ToUpperInvariant(c)); break;
                }
            }
            // Two apostrophes are how a typewriter -- and many OCR engines -- write seconds.
            return sb.ToString().Replace("''", "\"");
        }

        /// <summary>
        /// Repairs look-alike letters inside a token that must be a number: O to 0, l/I to 1,
        /// S to 5, B to 8, Z to 2. Returns the number of repairs made, which the caller turns
        /// into a confidence penalty. A token with more letters than digits is left alone --
        /// that is a word, not a damaged number.
        /// </summary>
        public static string RepairDigits(string token, out int repairs)
        {
            repairs = 0;
            if (string.IsNullOrEmpty(token)) return token;
            if (token.Count(char.IsLetter) == 0) return token;
            // Only a token made of digits, separators and the known look-alikes is repaired; a word is
            // a word. (The parsers' patterns already restrict their slots to exactly these characters.)
            if (token.Any(c => !char.IsDigit(c) && c != '.' && c != ',' && "OQILSBZoqilsbz".IndexOf(c) < 0)) return token;

            var sb = new StringBuilder(token.Length);
            foreach (var c in token)
            {
                var r = c;
                switch (char.ToUpperInvariant(c))
                {
                    case 'O': case 'Q': r = '0'; break;
                    case 'I': case 'L': r = '1'; break;
                    case 'S': r = '5'; break;
                    case 'B': r = '8'; break;
                    case 'Z': r = '2'; break;
                }
                if (r != c) repairs++;
                sb.Append(r);
            }
            return sb.ToString();
        }

        private const double RepairPenalty = 0.85;

        // ------------------------------------------------------------ bearings

        // N 89°42'18" E, N89°42'18"E, N 89 42 18 E, N 89-42-18 E, N 89D42'18" E, NORTH 89°42'18" EAST,
        // N 89°42' E, N 89° E.  Degrees may be one to three digits so a misread 189 is refused, not folded.
        private static readonly Regex BearingRegex = new Regex(
            @"(?<ns>\b(?:N|S|NORTH|SOUTH))\s*" +
            @"(?<deg>[0-9OIlSB]{1,3})\s*(?:°|D(?![A-Z])|\s|-)\s*" +
            @"(?:(?<min>[0-9OIlSB]{1,2})\s*(?:'|-|\s)\s*" +
            @"(?:(?<sec>[0-9OIlSB]{1,2}(?:\.[0-9OIlSB]+)?)\s*(?:""|'')?)?)?\s*" +
            @"(?<ew>(?:E|W|EAST|WEST)\b)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex DueRegex = new Regex(
            @"\bDUE\s+(?<dir>NORTH|SOUTH|EAST|WEST)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>
        /// Reads a quadrant bearing. The result is an azimuth in degrees clockwise from north,
        /// the convention every FTF direction uses. Seconds not written are zero and noted; a
        /// bearing over 90° or with 60+ minutes or seconds is refused rather than folded.
        /// </summary>
        public static SurveyValueRead ParseBearing(string text)
        {
            var raw = text ?? string.Empty;
            var s = NormalizeSymbols(raw).Trim();
            if (s.Length == 0) return SurveyValueRead.Failure(raw, "Nothing to read.");

            var due = DueRegex.Match(s);
            if (due.Success && !BearingRegex.IsMatch(s))
            {
                var dir = due.Groups["dir"].Value.ToUpperInvariant();
                var az = dir == "NORTH" ? 0.0 : dir == "EAST" ? 90.0 : dir == "SOUTH" ? 180.0 : 270.0;
                return new SurveyValueRead { Ok = true, Value = az, Raw = raw, Normalized = "DUE " + dir, Confidence = 1.0 };
            }

            var m = BearingRegex.Match(s);
            if (!m.Success)
                return SurveyValueRead.Failure(raw, "Not a quadrant bearing (expected N 89°42'18\" E or similar).");

            var read = new SurveyValueRead { Raw = raw };
            int repairs;
            var total = 0;
            var degText = RepairDigits(m.Groups["deg"].Value, out repairs); total += repairs;
            var minText = m.Groups["min"].Success ? RepairDigits(m.Groups["min"].Value, out repairs) : null; total += repairs;
            var secText = m.Groups["sec"].Success ? RepairDigits(m.Groups["sec"].Value, out repairs) : null; total += repairs;

            double deg, min = 0, sec = 0;
            if (!double.TryParse(degText, NumberStyles.Float, CultureInfo.InvariantCulture, out deg))
                return SurveyValueRead.Failure(raw, "The degrees '" + m.Groups["deg"].Value + "' are not a number.");
            if (minText != null && !double.TryParse(minText, NumberStyles.Float, CultureInfo.InvariantCulture, out min))
                return SurveyValueRead.Failure(raw, "The minutes '" + m.Groups["min"].Value + "' are not a number.");
            if (secText != null && !double.TryParse(secText, NumberStyles.Float, CultureInfo.InvariantCulture, out sec))
                return SurveyValueRead.Failure(raw, "The seconds '" + m.Groups["sec"].Value + "' are not a number.");

            if (deg > 90.0)
                return SurveyValueRead.Failure(raw, string.Format(CultureInfo.InvariantCulture,
                    "A quadrant bearing cannot exceed 90°; read {0}°. Check the image -- it may be a misread digit.", deg));
            if (min >= 60.0)
                return SurveyValueRead.Failure(raw, "Minutes must be under 60; read " + min.ToString("0", CultureInfo.InvariantCulture) + "'. Check the image.");
            if (sec >= 60.0)
                return SurveyValueRead.Failure(raw, "Seconds must be under 60; read " + sec.ToString("0.#", CultureInfo.InvariantCulture) + "\". Check the image.");
            if (deg == 90.0 && (min > 0 || sec > 0))
                return SurveyValueRead.Failure(raw, "A bearing of 90° cannot carry minutes or seconds.");

            var ns = char.ToUpperInvariant(m.Groups["ns"].Value[0]);
            var ew = char.ToUpperInvariant(m.Groups["ew"].Value[0]);
            var angle = deg + min / 60.0 + sec / 3600.0;
            double azimuth;
            if (ns == 'N') azimuth = ew == 'E' ? angle : 360.0 - angle;
            else azimuth = ew == 'E' ? 180.0 - angle : 180.0 + angle;
            azimuth = Geometry.Angles.NormalizeDegrees(azimuth);

            read.Ok = true;
            read.Value = azimuth;
            read.Confidence = Math.Pow(RepairPenalty, total);
            if (total > 0) read.Notes.Add(total + " look-alike character(s) repaired (O/0, l/1, S/5, B/8).");
            if (minText == null) read.Notes.Add("No minutes written; read as whole degrees.");
            else if (secText == null) read.Notes.Add("No seconds written; read as zero seconds.");

            // The match must account for the whole token run it sits in: "N 189°..." fails
            // above; "N 89°42'18\" E 1320.45'" is fine because the distance is outside it.
            read.Normalized = string.Format(CultureInfo.InvariantCulture, "{0} {1:00}°{2:00}'{3}\" {4}",
                ns, (int)deg, (int)min, FormatSeconds(sec), ew);
            return read;
        }

        private static string FormatSeconds(double sec)
        {
            var whole = Math.Floor(sec);
            if (Math.Abs(sec - whole) < 1e-9) return ((int)whole).ToString("00", CultureInfo.InvariantCulture);
            return sec.ToString("00.##", CultureInfo.InvariantCulture);
        }

        // ------------------------------------------------------------ angles

        private static readonly Regex AngleRegex = new Regex(
            @"(?<![A-Z0-9])(?<deg>[0-9OIlSB]{1,3})\s*°\s*(?:(?<min>[0-9OIlSB]{1,2})\s*'\s*(?:(?<sec>[0-9OIlSB]{1,2}(?:\.[0-9OIlSB]+)?)\s*""?)?)?",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex AngleLooseRegex = new Regex(
            @"(?<![A-Z0-9.])(?<deg>[0-9]{1,3})\s+(?<min>[0-9]{1,2})\s+(?<sec>[0-9]{1,2}(?:\.[0-9]+)?)(?![0-9.])",
            RegexOptions.CultureInvariant);

        /// <summary>
        /// Reads an angle written as degrees-minutes-seconds without quadrant letters -- a curve's
        /// central angle (Δ=45°12'10") or an interior angle. Degrees, 0 to 360.
        /// </summary>
        public static SurveyValueRead ParseAngle(string text)
        {
            var raw = text ?? string.Empty;
            var s = NormalizeSymbols(raw).Trim();
            var m = AngleRegex.Match(s);
            var loose = false;
            if (!m.Success) { m = AngleLooseRegex.Match(s); loose = true; }
            if (!m.Success) return SurveyValueRead.Failure(raw, "Not an angle (expected 45°12'10\").");

            int repairs, total = 0;
            var degText = RepairDigits(m.Groups["deg"].Value, out repairs); total += repairs;
            var minText = m.Groups["min"].Success ? RepairDigits(m.Groups["min"].Value, out repairs) : null; total += repairs;
            var secText = m.Groups["sec"].Success ? RepairDigits(m.Groups["sec"].Value, out repairs) : null; total += repairs;

            double deg, min = 0, sec = 0;
            if (!double.TryParse(degText, NumberStyles.Float, CultureInfo.InvariantCulture, out deg) ||
                (minText != null && !double.TryParse(minText, NumberStyles.Float, CultureInfo.InvariantCulture, out min)) ||
                (secText != null && !double.TryParse(secText, NumberStyles.Float, CultureInfo.InvariantCulture, out sec)))
                return SurveyValueRead.Failure(raw, "The angle components are not numbers.");
            if (deg > 360.0) return SurveyValueRead.Failure(raw, "An angle cannot exceed 360°.");
            if (min >= 60.0) return SurveyValueRead.Failure(raw, "Minutes must be under 60.");
            if (sec >= 60.0) return SurveyValueRead.Failure(raw, "Seconds must be under 60.");

            var read = new SurveyValueRead
            {
                Ok = true, Raw = raw, Value = deg + min / 60.0 + sec / 3600.0,
                Confidence = Math.Pow(RepairPenalty, total) * (loose ? 0.9 : 1.0),
                Normalized = string.Format(CultureInfo.InvariantCulture, "{0}°{1:00}'{2}\"", (int)deg, (int)min, FormatSeconds(sec))
            };
            if (total > 0) read.Notes.Add(total + " look-alike character(s) repaired.");
            if (loose) read.Notes.Add("Read as degrees minutes seconds from three plain numbers.");
            return read;
        }

        // ------------------------------------------------------------ distances

        /// <summary>US survey feet per metre. Washington records are in US survey feet.</summary>
        public const double SurveyFeetPerMetre = 3937.0 / 1200.0;
        public const double FeetPerChain = 66.0;
        public const double FeetPerRod = 16.5;

        // 1320.45', 1,320.45 FT, 1320.45 FEET, 402.44 M, 20.00 CH, 4 RODS, or a bare number.
        private static readonly Regex DistanceRegex = new Regex(
            @"(?<![A-Z0-9°'"".-])(?<num>(?:[0-9OIlSB]{1,3}(?:,[0-9OIlSB]{3})+|[0-9OIlSB]+)(?:\.[0-9OIlSB]+)?|\.[0-9OIlSB]+)\s*" +
            @"(?<unit>'|FT\.?|FEET|METERS|METRES|M\b|CH\.?|CHAINS|CHS|RODS|RDS|RD\.?)?(?!\s*°)(?![0-9])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>
        /// Reads a distance. Feet unless the unit says metres, chains or rods, which are
        /// converted (US survey foot) and noted. A bare number with no unit reads as feet at a
        /// lower confidence, since the context (a bearing beside it) is what makes it a distance.
        /// </summary>
        public static SurveyValueRead ParseDistance(string text)
        {
            var raw = text ?? string.Empty;
            var s = NormalizeSymbols(raw).Trim();
            var m = DistanceRegex.Match(s);
            if (!m.Success) return SurveyValueRead.Failure(raw, "Not a distance (expected 1320.45').");
            // Look-alikes may be repaired inside a number; a token with no digit at all is a word.
            if (m.Groups["num"].Value.Count(char.IsDigit) == 0) return SurveyValueRead.Failure(raw, "Not a distance (expected 1320.45').");

            int repairs;
            var numText = RepairDigits(m.Groups["num"].Value.Replace(",", string.Empty), out repairs);
            double value;
            if (!double.TryParse(numText, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                return SurveyValueRead.Failure(raw, "'" + m.Groups["num"].Value + "' is not a number.");
            if (value <= 0) return SurveyValueRead.Failure(raw, "A distance must be greater than zero.");

            var read = new SurveyValueRead { Ok = true, Raw = raw, Confidence = Math.Pow(RepairPenalty, repairs) };
            if (repairs > 0) read.Notes.Add(repairs + " look-alike character(s) repaired.");

            var unit = (m.Groups["unit"].Success ? m.Groups["unit"].Value : string.Empty).ToUpperInvariant().TrimEnd('.');
            var feet = value;
            if (unit == "M" || unit == "METERS" || unit == "METRES")
            {
                feet = value * SurveyFeetPerMetre;
                read.Unit = "m";
                read.Notes.Add("Written in metres; converted at 1 m = 3.2808333 US survey feet.");
            }
            else if (unit == "CH" || unit == "CHAINS" || unit == "CHS")
            {
                feet = value * FeetPerChain;
                read.Unit = "ch";
                read.Notes.Add("Written in chains; converted at 66 feet per chain.");
            }
            else if (unit == "RODS" || unit == "RDS" || unit == "RD")
            {
                feet = value * FeetPerRod;
                read.Unit = "rd";
                read.Notes.Add("Written in rods; converted at 16.5 feet per rod.");
            }
            else if (unit.Length > 0)
            {
                read.Unit = "ft";
            }
            else
            {
                read.Confidence *= 0.9;
                read.Notes.Add("No unit written; read as feet.");
            }

            // A distance token has no reason to carry more than a few decimals; a long
            // fraction is usually a misread symbol, so say so.
            var dot = numText.IndexOf('.');
            if (dot >= 0 && numText.Length - dot - 1 > 4)
            {
                read.Confidence *= 0.8;
                read.Notes.Add("More than four decimals read; check the image.");
            }

            read.Value = feet;
            read.Normalized = feet.ToString("0.00", CultureInfo.InvariantCulture) + "'";
            return read;
        }

        // ------------------------------------------------------------ tokens

        /// <summary>
        /// Splits one line of document text into the survey tokens it holds, left to right:
        /// bearings, distances, angles, curve keys (R=, L=, Δ=, CH=, CB=, T=), record and
        /// measured tags ((R), (M), (R1), (C), (P), (D), R1:, M:), curve tags (C1) and line
        /// tags (L1). Anything else is returned as plain words so the caller can classify the
        /// line.
        /// </summary>
        public static List<SurveyToken> Tokenize(string text)
        {
            var tokens = new List<SurveyToken>();
            var s = NormalizeSymbols(text ?? string.Empty);
            if (s.Trim().Length == 0) return tokens;

            var covered = new bool[s.Length];

            // Order matters: a bearing contains digits that would otherwise read as distances,
            // and a curve key contains the value that follows it.
            foreach (Match m in BearingRegex.Matches(s))
                Claim(tokens, covered, m, SurveyTokenKind.Bearing, ParseBearing(m.Value));
            foreach (Match m in DueRegex.Matches(s))
                Claim(tokens, covered, m, SurveyTokenKind.Bearing, ParseBearing(m.Value));
            foreach (Match m in CurveKeyRegex.Matches(s))
                Claim(tokens, covered, m, SurveyTokenKind.CurveKey, null, CurveKeyName(m.Groups["key"].Value));
            foreach (Match m in TagRegex.Matches(s))
                Claim(tokens, covered, m, SurveyTokenKind.Tag, null, TagName(m));
            foreach (Match m in CurveTagRegex.Matches(s))
                Claim(tokens, covered, m, SurveyTokenKind.CurveTag, null, "C" + m.Groups["n"].Value);
            foreach (Match m in LineTagRegex.Matches(s))
                Claim(tokens, covered, m, SurveyTokenKind.LineTag, null, "L" + m.Groups["n"].Value);
            foreach (Match m in AngleRegex.Matches(s))
                Claim(tokens, covered, m, SurveyTokenKind.Angle, ParseAngle(m.Value));
            foreach (Match m in DistanceRegex.Matches(s))
            {
                // A distance must be a number, not a run of look-alikes; and "1" inside a
                // word is not a distance.
                var num = m.Groups["num"].Value;
                if (num.Count(char.IsDigit) == 0) continue;
                if (m.Index > 0 && char.IsLetter(s[m.Index - 1])) continue;
                Claim(tokens, covered, m, SurveyTokenKind.Distance, ParseDistance(m.Value));
            }

            // Whatever is left over, word by word.
            var i = 0;
            while (i < s.Length)
            {
                if (covered[i] || char.IsWhiteSpace(s[i])) { i++; continue; }
                var start = i;
                while (i < s.Length && !covered[i] && !char.IsWhiteSpace(s[i])) i++;
                tokens.Add(new SurveyToken { Kind = SurveyTokenKind.Word, Text = s.Substring(start, i - start), Start = start, Length = i - start });
            }

            tokens.Sort((a, b) => a.Start.CompareTo(b.Start));
            return tokens;
        }

        private static void Claim(List<SurveyToken> tokens, bool[] covered, Match m, SurveyTokenKind kind,
                                  SurveyValueRead read, string name = null)
        {
            if (m.Length == 0) return;
            for (var i = m.Index; i < m.Index + m.Length; i++)
                if (covered[i]) return;
            for (var i = m.Index; i < m.Index + m.Length; i++) covered[i] = true;
            tokens.Add(new SurveyToken { Kind = kind, Text = m.Value.Trim(), Start = m.Index, Length = m.Length, Read = read, Name = name });
        }

        // R=250.00', RADIUS = 250.00', L=197.22', ARC=, A=, Δ=45°12'10", DELTA=, D=, CH=, CHORD=, C=, CD=,
        // CB=N45°12'10"E, CH BRG=, CHORD BEARING=, T=, TAN=, TANGENT=.  Also "R 250.00'" with a space.
        private static readonly Regex CurveKeyRegex = new Regex(
            @"(?<![A-Z])(?<key>RADIUS|RAD|R|ARC LENGTH|ARC|LENGTH|LEN|L|DELTA|Δ|D|CHORD BEARING|CHORD BRG|CH BRG|CH\.? BEARING|CB|CHORD|CHD|CH|CD|C|TANGENT|TAN|T)\s*(?:=|:)\s*",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static string CurveKeyName(string key)
        {
            var k = key.ToUpperInvariant().Replace(".", string.Empty);
            if (k == "R" || k == "RAD" || k == "RADIUS") return "R";
            if (k == "L" || k == "ARC" || k == "ARC LENGTH" || k == "LENGTH" || k == "LEN") return "L";
            if (k == "D" || k == "Δ" || k == "DELTA") return "DELTA";
            if (k == "CB" || k.Contains("BEARING") || k.Contains("BRG")) return "CB";
            if (k == "CH" || k == "CHD" || k == "CHORD" || k == "CD" || k == "C") return "CH";
            if (k == "T" || k == "TAN" || k == "TANGENT") return "T";
            return k;
        }

        // (R), (M), (R1), (R2), (C), (P), (D), (R&M), (R1 & M), R1:, M:, (MEAS), (REC), (CALC)
        private static readonly Regex TagRegex = new Regex(
            @"\(\s*(?<tags>(?:R\d*|M|C|P|D|MEAS|MEASURED|REC|RECORD|CALC|CALCULATED|COMP|COMPUTED|PLAT|DEED)(?:\s*[&/,+]\s*(?:R\d*|M|C|P|D|MEAS|MEASURED|REC|RECORD|CALC|CALCULATED|COMP|COMPUTED|PLAT|DEED))*)\s*\)" +
            @"|(?<![A-Z0-9])(?<tags2>R\d+|M|MEAS|REC|CALC)\s*:(?![0-9])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static string TagName(Match m)
        {
            var text = m.Groups["tags"].Success ? m.Groups["tags"].Value : m.Groups["tags2"].Value;
            var parts = Regex.Split(text.ToUpperInvariant(), @"\s*[&/,+]\s*");
            var names = new List<string>();
            foreach (var p in parts)
            {
                var t = p.Trim();
                if (t.Length == 0) continue;
                if (t == "MEAS" || t == "MEASURED") t = "M";
                else if (t == "REC" || t == "RECORD") t = "R";
                else if (t == "CALC" || t == "CALCULATED" || t == "COMP" || t == "COMPUTED") t = "C";
                else if (t == "PLAT") t = "P";
                else if (t == "DEED") t = "D";
                names.Add(t);
            }
            return string.Join("&", names.ToArray());
        }

        private static readonly Regex CurveTagRegex = new Regex(@"(?<![A-Z0-9])C(?<n>\d{1,3})(?![0-9A-Z°'])", RegexOptions.CultureInvariant);
        private static readonly Regex LineTagRegex = new Regex(@"(?<![A-Z0-9])L(?<n>\d{1,3})(?![0-9A-Z°'])", RegexOptions.CultureInvariant);

        // ------------------------------------------------------------ alternatives

        /// <summary>
        /// The digit pairs OCR most often confuses at survey annotation size. Used only to
        /// OFFER alternatives for a low-confidence token; nothing is ever chosen from them.
        /// </summary>
        private static readonly char[][] Confusions =
        {
            new[] { '3', '8' }, new[] { '5', '8' }, new[] { '5', '6' }, new[] { '1', '7' }, new[] { '0', '8' }
        };

        /// <summary>
        /// Alternatives for a distance token read at low confidence: single-digit swaps
        /// from the confusion table, most consequential (leftmost decimal digit) first,
        /// limited to <paramref name="max"/>. Each is a candidate for the reviewer, never a
        /// replacement.
        /// </summary>
        public static List<ValueAlternative> DigitAlternatives(string numberText, double confidence, int max)
        {
            var result = new List<ValueAlternative>();
            if (string.IsNullOrEmpty(numberText) || max <= 0) return result;
            var chars = numberText.ToCharArray();
            var order = Enumerable.Range(0, chars.Length).OrderBy(i => Rank(chars, i)).ToList();
            foreach (var i in order)
            {
                if (!char.IsDigit(chars[i])) continue;
                foreach (var pair in Confusions)
                {
                    char other;
                    if (chars[i] == pair[0]) other = pair[1];
                    else if (chars[i] == pair[1]) other = pair[0];
                    else continue;
                    var copy = (char[])chars.Clone();
                    copy[i] = other;
                    var text = new string(copy);
                    double value;
                    if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) continue;
                    if (result.Any(r => r.Text == text)) continue;
                    result.Add(new ValueAlternative
                    {
                        Text = text, Value = value, Confidence = confidence * 0.5,
                        Reason = "digit confusion " + chars[i] + "/" + other
                    });
                    if (result.Count >= max) return result;
                }
            }
            return result;
        }

        /// <summary>Decimal digits first (a wrong tenth is the classic 148.52 vs 148.82), then the rest right to left.</summary>
        private static int Rank(char[] chars, int i)
        {
            var dot = Array.IndexOf(chars, '.');
            if (dot >= 0 && i > dot) return i - dot;             // 1, 2, ... after the dot
            return 100 + (chars.Length - i);                     // then integer digits, rightmost first
        }
    }

    public enum SurveyTokenKind { Word, Bearing, Distance, Angle, CurveKey, Tag, CurveTag, LineTag }

    /// <summary>One token of a document line.</summary>
    public sealed class SurveyToken
    {
        public SurveyTokenKind Kind { get; set; }
        public string Text { get; set; }
        public int Start { get; set; }
        public int Length { get; set; }
        /// <summary>The parsed value for bearings, distances and angles.</summary>
        public SurveyValueRead Read { get; set; }
        /// <summary>Canonical name for keys and tags: R, L, DELTA, CH, CB, T; R1, M, C, P, D; C1; L1.</summary>
        public string Name { get; set; }

        public override string ToString() { return Kind + ":" + (Name ?? Text); }
    }
}
