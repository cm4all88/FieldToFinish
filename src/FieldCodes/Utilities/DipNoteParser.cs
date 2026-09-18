using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using FieldCodes.Drafting;
using FieldCodes.Settings;

namespace FieldCodes.Utilities
{
    /// <summary>A problem found while reading notes. The raw line is kept so the
    /// drafter can see exactly what was not understood.</summary>
    public sealed class NoteDiagnostic
    {
        public Severity Severity { get; set; }
        public int LineNumber { get; set; }
        public string Line { get; set; }
        public string Message { get; set; }

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "Line {0}: {1} -- \"{2}\"",
                                 LineNumber, Message, Line);
        }
    }

    public sealed class DipNoteParseResult
    {
        public IList<StructureObservation> Structures { get; private set; }
        public IList<NoteDiagnostic> Diagnostics { get; private set; }

        public DipNoteParseResult()
        {
            Structures = new List<StructureObservation>();
            Diagnostics = new List<NoteDiagnostic>();
        }

        public bool HasErrors
        {
            get { return Diagnostics.Any(d => d.Severity == Severity.Error); }
        }
    }

    /// <summary>
    /// Reads structure dip notes as the crew writes them:
    /// <code>
    /// PT 1045 SDMH
    /// BOT 7.82
    /// WL 6.94
    /// 12 RCP N 6.41
    /// 18 RCP SW 7.02 OUT
    /// 24X36 BOX CONC E 5.10 TOP SILTED
    /// </code>
    /// A pipe line is: size, then in any order shape, material, direction, dip,
    /// reference, IN/OUT and conditions. Anything not understood is kept as a note
    /// AND reported -- the parser never guesses a value that was not written.
    /// </summary>
    public sealed class DipNoteParser
    {
        private static readonly Regex Header =
            new Regex(@"^(?:PT|POINT|STR)\.?\s*#?\s*(?<num>[A-Z0-9-]+)(?:\s+(?<code>[A-Z][A-Z0-9]*))?(?<rest>.*)$",
                      RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex Size =
            new Regex(@"^(?<w>\d+(?:\.\d+)?)(?:""|IN)?(?:[X×](?<h>\d+(?:\.\d+)?)(?:""|IN)?)?$",
                      RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex Number =
            new Regex(@"^-?\d+(?:\.\d+)?$", RegexOptions.CultureInvariant);

        private static readonly Dictionary<string, double> Cardinals =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                { "N", 0 }, { "NE", 45 }, { "E", 90 }, { "SE", 135 },
                { "S", 180 }, { "SW", 225 }, { "W", 270 }, { "NW", 315 }
            };

        private static readonly Dictionary<string, PipeShape> Shapes =
            new Dictionary<string, PipeShape>(StringComparer.OrdinalIgnoreCase)
            {
                { "RND", PipeShape.Round }, { "ROUND", PipeShape.Round },
                { "ELL", PipeShape.Elliptical }, { "ELLIP", PipeShape.Elliptical },
                { "ELLIPTICAL", PipeShape.Elliptical },
                { "ARCH", PipeShape.Arch }, { "BOX", PipeShape.Box },
                { "CUSTOM", PipeShape.Custom }
            };

        private static readonly Dictionary<string, MeasurementReference> References =
            new Dictionary<string, MeasurementReference>(StringComparer.OrdinalIgnoreCase)
            {
                { "INV", MeasurementReference.Invert }, { "IE", MeasurementReference.Invert },
                { "INVERT", MeasurementReference.Invert },
                { "TOP", MeasurementReference.TopOfPipe }, { "TP", MeasurementReference.TopOfPipe },
                { "TOP-OF-PIPE", MeasurementReference.TopOfPipe }, { "CROWN", MeasurementReference.TopOfPipe },
                { "TOPPIPE", MeasurementReference.TopOfPipe }, { "TOP-PIPE", MeasurementReference.TopOfPipe },
                { "SPR", MeasurementReference.Springline }, { "SL", MeasurementReference.Springline },
                { "SPRING", MeasurementReference.Springline },
                { "SPRINGLINE", MeasurementReference.Springline }
            };

        private static readonly Dictionary<string, string> ConditionWords =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "SUB", "SUBMERGED" }, { "SUBMERGED", "SUBMERGED" },
                { "SILT", "SILTED" }, { "SILTED", "SILTED" },
                { "BLK", "BLOCKED" }, { "BLOCKED", "BLOCKED" },
                { "NODIP", "UNABLE TO DIP" }, { "UTD", "UNABLE TO DIP" },
                { "UNABLE", "UNABLE TO DIP" }, { "DRY", "DRY" },
                { "DAMAGED", "DAMAGED" }, { "BROKEN", "DAMAGED" }, { "ROOTS", "ROOTS" }
            };

        private readonly UtilitySettings _settings;
        private readonly HashSet<string> _materials;

        public DipNoteParser(UtilitySettings settings)
        {
            _settings = settings ?? new UtilitySettings();
            _materials = new HashSet<string>(_settings.Materials ?? new List<string>(),
                                             StringComparer.OrdinalIgnoreCase);
        }

        public DipNoteParseResult Parse(string text)
        {
            var result = new DipNoteParseResult();
            if (string.IsNullOrWhiteSpace(text)) return result;

            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            StructureObservation current = null;
            var raw = new List<string>();

            for (var i = 0; i < lines.Length; i++)
            {
                var original = lines[i];
                var line = Regex.Replace(original.Trim(), @"\s+", " ");
                if (line.Length == 0) continue;

                var header = Header.Match(line);
                if (header.Success && LooksLikeHeader(line))
                {
                    if (current != null) current.RawText = string.Join("\n", raw);
                    raw.Clear();

                    current = new StructureObservation
                    {
                        PointNumber = header.Groups["num"].Value.ToUpperInvariant(),
                        FieldCode = header.Groups["code"].Success
                            ? header.Groups["code"].Value.ToUpperInvariant() : null
                    };
                    var rest = header.Groups["rest"].Value.Trim();
                    if (rest.Length > 0) current.Notes.Add(rest);

                    result.Structures.Add(current);
                    raw.Add(original);
                    continue;
                }

                if (current == null)
                {
                    result.Diagnostics.Add(Diag(Severity.Error, i + 1, original,
                        "Dip line before any \"PT <number>\" header; it belongs to no structure."));
                    continue;
                }

                raw.Add(original);
                var before = result.Diagnostics.Count;
                ParseBodyLine(current, line, original, i + 1, result);
                foreach (var d in result.Diagnostics.Skip(before).Where(d => d.Severity == Severity.Error))
                    current.NoteProblems.Add("Line " + d.LineNumber.ToString(CultureInfo.InvariantCulture) + ": " +
                                             d.Message + " (\"" + original.Trim() + "\")");
            }

            if (current != null) current.RawText = string.Join("\n", raw);

            foreach (var group in result.Structures.GroupBy(s => s.PointNumber)
                                                   .Where(g => g.Count() > 1))
                result.Diagnostics.Add(Diag(Severity.Warning, 0, "PT " + group.Key,
                    "Point " + group.Key + " has more than one note block."));

            return result;
        }

        private static bool LooksLikeHeader(string line)
        {
            var first = line.Split(' ')[0].ToUpperInvariant().TrimEnd('.');
            return first == "PT" || first == "POINT" || first == "STR" ||
                   first.StartsWith("PT#", StringComparison.Ordinal) ||
                   (first.StartsWith("PT", StringComparison.Ordinal) &&
                    first.Length > 2 && char.IsDigit(first[2]));
        }

        private void ParseBodyLine(StructureObservation structure, string line,
                                   string original, int lineNumber,
                                   DipNoteParseResult result)
        {
            // "MD" (measure down) is how the crew writes a dip; it carries no value itself.
            var tokens = line.Split(' ').Where(t => !t.Equals("MD", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (tokens.Length == 0) return;
            var keyword = tokens[0].ToUpperInvariant();

            if (keyword == "BOT" || keyword == "BTM" || keyword == "BOTTOM")
            {
                structure.BottomDip = Keep(structure.BottomDip, ReadSingleDip(tokens, original, lineNumber, "bottom", result),
                                           original, lineNumber, "bottom", result);
                return;
            }
            if (keyword == "ID" || keyword == "INSIDE")
            {
                ReadInsideSize(structure, tokens, original, lineNumber, result);
                return;
            }
            if (keyword == "WL" || keyword == "WATER")
            {
                if (tokens.Length > 1 && tokens[1].Equals("DRY", StringComparison.OrdinalIgnoreCase))
                {
                    structure.Conditions.Add("DRY");
                    return;
                }
                structure.WaterDip = Keep(structure.WaterDip, ReadSingleDip(tokens, original, lineNumber, "water", result),
                                          original, lineNumber, "water", result);
                return;
            }

            if (Size.IsMatch(tokens[0]))
            {
                var pipe = ParsePipe(tokens, original, lineNumber, result);
                if (pipe != null) structure.Pipes.Add(pipe);
                return;
            }

            // A line made only of condition words describes the structure.
            if (tokens.All(t => ConditionWords.ContainsKey(t)))
            {
                foreach (var t in tokens) structure.Conditions.Add(ConditionWords[t]);
                return;
            }

            structure.Notes.Add(line);
            result.Diagnostics.Add(Diag(Severity.Info, lineNumber, original,
                "Kept as a structure note (not a dip, bottom, water or pipe line)."));
        }

        /// <summary>A second bottom or water line never silently replaces the first: an
        /// unreadable one leaves the readable value alone, and a different readable one
        /// is reported and ignored so the drafter decides which is right.</summary>
        private static double? Keep(double? existing, double? reading, string original, int lineNumber,
                                    string what, DipNoteParseResult result)
        {
            if (!existing.HasValue) return reading;
            if (!reading.HasValue) return existing;
            if (Math.Abs(existing.Value - reading.Value) > 1e-9)
                result.Diagnostics.Add(Diag(Severity.Error, lineNumber, original,
                    "A second " + what + " dip (" + reading.Value.ToString("0.00", CultureInfo.InvariantCulture) +
                    ") differs from the first (" + existing.Value.ToString("0.00", CultureInfo.InvariantCulture) +
                    "); the first is kept -- check the notes."));
            return existing;
        }

        private static void ReadInsideSize(StructureObservation structure, string[] tokens, string original,
                                           int lineNumber, DipNoteParseResult result)
        {
            var size = tokens.Length >= 2 ? Size.Match(tokens[1]) : Match.Empty;
            if (size.Success)
            {
                structure.InsideWidthIn = double.Parse(size.Groups["w"].Value, CultureInfo.InvariantCulture);
                return;
            }
            result.Diagnostics.Add(Diag(Severity.Error, lineNumber, original,
                "The inside-size line has no readable size in inches."));
        }

        private static double? ReadSingleDip(string[] tokens, string original, int lineNumber,
                                             string what, DipNoteParseResult result)
        {
            double value;
            if (tokens.Length >= 2 &&
                double.TryParse(tokens[1], NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
                value >= 0)
            {
                if (tokens.Length > 2)
                    result.Diagnostics.Add(Diag(Severity.Warning, lineNumber, original,
                        "Extra words after the " + what + " dip were ignored."));
                return value;
            }

            result.Diagnostics.Add(Diag(Severity.Error, lineNumber, original,
                "The " + what + " line has no readable dip."));
            return null;
        }

        private PipeObservation ParsePipe(string[] tokens, string original, int lineNumber,
                                          DipNoteParseResult result)
        {
            var pipe = new PipeObservation { RawText = original, Source = ObservationSource.FieldNote };
            var notes = new List<string>();

            var size = Size.Match(tokens[0]);
            var width = double.Parse(size.Groups["w"].Value, CultureInfo.InvariantCulture);
            pipe.WidthIn = width;
            pipe.HeightIn = size.Groups["h"].Success
                ? double.Parse(size.Groups["h"].Value, CultureInfo.InvariantCulture)
                : width;
            var twoDimensions = size.Groups["h"].Success;
            pipe.Shape = twoDimensions ? PipeShape.Custom : PipeShape.Round;

            var shapeSeen = false;
            var referenceSeen = false;

            for (var i = 1; i < tokens.Length; i++)
            {
                var token = tokens[i];

                PipeShape shape;
                if (!shapeSeen && Shapes.TryGetValue(token, out shape))
                {
                    pipe.Shape = shape;
                    shapeSeen = true;
                    continue;
                }

                if (pipe.Material == null && _materials.Contains(token))
                {
                    pipe.Material = token.ToUpperInvariant();
                    continue;
                }

                if (!pipe.Direction.IsKnown && pipe.Direction.Text == null)
                {
                    var direction = ParseDirection(token);
                    if (direction != null)
                    {
                        pipe.Direction = direction;
                        continue;
                    }
                }

                if (Number.IsMatch(token))
                {
                    if (pipe.MeasuredDip == null)
                    {
                        var dip = double.Parse(token, CultureInfo.InvariantCulture);
                        if (dip < 0)
                        {
                            result.Diagnostics.Add(Diag(Severity.Error, lineNumber, original,
                                "A negative dip would put the pipe above the rim; not recorded."));
                            continue;
                        }
                        pipe.MeasuredDip = dip;
                    }
                    else
                    {
                        notes.Add(token);
                        result.Diagnostics.Add(Diag(Severity.Warning, lineNumber, original,
                            "A second number (" + token + ") on a pipe line was kept as a note, not used."));
                    }
                    continue;
                }

                MeasurementReference reference;
                if (!referenceSeen && References.TryGetValue(token, out reference))
                {
                    pipe.Reference = reference;
                    pipe.ReferenceBasis = ReferenceBasis.StatedInFieldNote;
                    referenceSeen = true;
                    continue;
                }
                // "TOP PIPE" is one reference written as two words.
                if (referenceSeen && pipe.Reference == MeasurementReference.TopOfPipe &&
                    token.Equals("PIPE", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (token.Equals("IN", StringComparison.OrdinalIgnoreCase)) { pipe.Role = FlowRole.In; continue; }
                if (token.Equals("OUT", StringComparison.OrdinalIgnoreCase)) { pipe.Role = FlowRole.Out; continue; }

                string condition;
                if (ConditionWords.TryGetValue(token, out condition))
                {
                    if (!pipe.HasCondition(condition)) pipe.Conditions.Add(condition);
                    continue;
                }

                notes.Add(token);
            }

            if (!referenceSeen && pipe.MeasuredDip.HasValue)
            {
                if (_settings.UnmarkedDipsAreInvertsByConvention)
                {
                    // Only a documented office convention may classify an unmarked dip.
                    pipe.Reference = MeasurementReference.Invert;
                    pipe.ReferenceBasis = ReferenceBasis.FieldNoteConvention;
                    pipe.ReferenceNote = "Unmarked dip classified as invert by the field note convention: " +
                                         _settings.UnmarkedDipConventionSource;
                }
                else
                {
                    pipe.Reference = MeasurementReference.Unspecified;
                    pipe.ReferenceBasis = ReferenceBasis.NotStated;
                    result.Diagnostics.Add(Diag(Severity.Warning, lineNumber, original,
                        "The dip does not say what it was measured to (INV, TOP...). Kept exactly and marked " +
                        "unspecified; confirm the reference before it is used as an invert."));
                }
            }
            else if (!referenceSeen)
            {
                pipe.Reference = MeasurementReference.Unspecified;
                pipe.ReferenceBasis = ReferenceBasis.NotStated;
            }

            if (twoDimensions && !shapeSeen)
                result.Diagnostics.Add(Diag(Severity.Info, lineNumber, original,
                    "Two dimensions with no shape word; recorded as a custom width x height."));
            if (!twoDimensions && shapeSeen && pipe.Shape != PipeShape.Round)
                result.Diagnostics.Add(Diag(Severity.Warning, lineNumber, original,
                    pipe.Shape + " pipe with only one dimension; height taken equal to width -- check the note."));

            if (pipe.Material == null)
                result.Diagnostics.Add(Diag(Severity.Warning, lineNumber, original,
                    "No recognised material; left blank rather than assumed."));

            if (!pipe.Direction.IsKnown)
            {
                if (pipe.Direction.Text == null) pipe.Direction = ObservedDirection.Unknown("?");
                result.Diagnostics.Add(Diag(Severity.Warning, lineNumber, original,
                    "No readable direction; the pipe cannot be connected until one is entered."));
            }

            if (pipe.MeasuredDip == null && !pipe.HasCondition("UNABLE TO DIP"))
                result.Diagnostics.Add(Diag(Severity.Warning, lineNumber, original,
                    "No dip on this pipe line and no \"unable to dip\" note."));

            if (notes.Count > 0)
            {
                pipe.Notes = string.Join(" ", notes);
                result.Diagnostics.Add(Diag(Severity.Info, lineNumber, original,
                    "Unrecognised words kept as pipe notes: " + pipe.Notes));
            }

            return pipe;
        }

        /// <summary>
        /// A direction token: cardinal/intercardinal (N, SW), a quadrant bearing
        /// (N45E, N45.3015E), or an azimuth (AZ215, @215.30). Null when the token is
        /// not a direction. "?" or UNK records an explicitly unknown direction.
        /// </summary>
        public static ObservedDirection ParseDirection(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return null;
            var t = token.Trim().ToUpperInvariant();

            if (t == "?" || t == "UNK") return ObservedDirection.Unknown(t);

            double cardinal;
            if (Cardinals.TryGetValue(t, out cardinal))
                return new ObservedDirection { Text = t, Kind = DirectionKind.Cardinal, AzimuthDegrees = cardinal };

            if (t.StartsWith("AZ", StringComparison.Ordinal) || t.StartsWith("@", StringComparison.Ordinal))
            {
                var body = t.StartsWith("@", StringComparison.Ordinal) ? t.Substring(1) : t.Substring(2);
                var az = SurveyDirection.ParseAzimuth(body);
                return az.Ok
                    ? new ObservedDirection { Text = t, Kind = DirectionKind.Azimuth, AzimuthDegrees = az.Value }
                    : null;
            }

            if ((t[0] == 'N' || t[0] == 'S') && t.Length > 2 &&
                (t[t.Length - 1] == 'E' || t[t.Length - 1] == 'W') && char.IsDigit(t[1]))
            {
                var bearing = SurveyDirection.ParseBearing(t);
                if (bearing.Ok)
                    return new ObservedDirection { Text = t, Kind = DirectionKind.Bearing, AzimuthDegrees = bearing.Value };
            }

            return null;
        }

        private static NoteDiagnostic Diag(Severity severity, int line, string text, string message)
        {
            return new NoteDiagnostic { Severity = severity, LineNumber = line, Line = text, Message = message };
        }
    }
}
