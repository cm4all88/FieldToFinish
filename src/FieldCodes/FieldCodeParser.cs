using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace FieldCodes
{
    /// <summary>
    /// Turns a raw field description into drawing instructions.
    ///
    /// Descriptions are assumed to have already passed the office code audit, so this
    /// parser is deliberately strict: anything it cannot parse is reported as an Error
    /// and the point is not drawn. It does not guess at missing values, because a
    /// plausible guess in a survey drawing outlives everyone's memory of it being a guess.
    ///
    /// Contains no Autodesk references -- this type must be usable outside CAD.
    /// </summary>
    public sealed class FieldCodeParser
    {
        private static readonly Regex Placeholder =
            new Regex(@"\{(?<name>[A-Za-z_][A-Za-z0-9_.]*)(?::(?<fmt>[^}]+))?\}",
                      RegexOptions.Compiled | RegexOptions.CultureInvariant);

        // Whitespace is a stem separator: the office writes a cluster as its individual
        // diameters, "CON 6 8 8 12 18 18 . 30". The others are kept for older notes.
        private static readonly char[] StemSeparators = new[] { ',', '/', '+', ' ', '\t' };

        private readonly RulesConfig _cfg;

        private readonly Dictionary<string, SymbolLabelRule> _symbolLabels;

        public FieldCodeParser(RulesConfig config)
        {
            if (config == null) throw new ArgumentNullException("config");
            _cfg = config;

            _symbolLabels = new Dictionary<string, SymbolLabelRule>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var symbol in config.SymbolLabels ?? new List<SymbolLabelRule>())
            {
                if (symbol == null || !symbol.Enabled ||
                    string.IsNullOrWhiteSpace(symbol.Code)) continue;
                if (!_symbolLabels.ContainsKey(symbol.Code.Trim()))
                    _symbolLabels[symbol.Code.Trim()] = symbol;
            }
        }

        public ParsedPoint Parse(string pointNumber, string rawDescription)
        {
            var p = new ParsedPoint
            {
                PointNumber = pointNumber,
                RawDescription = rawDescription
            };

            if (string.IsNullOrWhiteSpace(rawDescription))
            {
                p.Add(Severity.Error, "EMPTY", "Description is blank.");
                return p;
            }

            // Collapse internal runs of whitespace so a stray double space does not
            // change the token positions. Does not alter the stored raw description.
            var normalized = Regex.Replace(rawDescription.Trim(), @"\s+", " ");

            // Codes this tool does not draw -- topo shots, monuments, structures. They
            // are skipped silently rather than reported: a base map contains hundreds
            // of them, and an exception report buried in curb shots is one nobody reads.
            // Anything NOT listed here and NOT matching a rule is still a loud error.
            foreach (var ignore in _cfg.IgnorePatterns)
            {
                if (!ignore.IsMatch(normalized)) continue;
                p.Ignored = true;
                return p;
            }

            Match match = null;
            CodeRule rule = null;
            foreach (var c in _cfg.Codes)
            {
                // A disabled rule does not exist as far as matching goes: its codes
                // fall through to the bare-code / unknown handling below.
                if (!c.Enabled) continue;

                var m = c.Regex.Match(normalized);
                if (m.Success) { match = m; rule = c; break; }
            }

            if (rule == null)
            {
                p.Code = FirstToken(normalized);

                // Linework. TBC and Civil 3D build these into figures; the tool labels
                // the resulting polylines rather than the points that defined them, so
                // there is nothing to do here and nothing worth reporting.
                foreach (var linework in _cfg.LineworkPatterns)
                {
                    if (!linework.IsMatch(normalized)) continue;
                    p.Ignored = true;
                    return p;
                }

                // A bare code carries nothing to act on. The survey recorded that a
                // thing is there and the point style already shows it; there is no
                // value to label and nothing to draw -- UNLESS the office has listed
                // this code in symbolLabels, in which case the symbol is labelled
                // with the code itself, verbatim as shot, on that family's text
                // layer. Trailing figure digits match the base code (CB2 -> CB).
                if (!HasDataBeyondCode(normalized))
                {
                    var symbol = SymbolFor(normalized);
                    if (symbol != null)
                    {
                        p.Recognized = true;
                        p.RuleId = SymbolLabelRuleId;
                        p.LabelText = string.IsNullOrWhiteSpace(symbol.Label)
                            ? normalized : symbol.Label;
                        p.LabelLayer = symbol.Layer;
                        p.Leader = LeaderMode.Auto;
                        return p;
                    }

                    p.NoContent = true;
                    return p;
                }

                // Something with data that nothing is configured for. That is an
                // unfinished setup rather than bad survey data, so it is summarised
                // rather than shouted about per point. A code that DOES match a rule
                // and then fails inside it is still a hard error -- see below.
                p.Unhandled = true;
                p.Add(Severity.Info, "UNHANDLED",
                    "No rule is configured for this field code.");
                return p;
            }

            p.Recognized = true;
            p.RuleId = rule.Id;
            CaptureGroups(match, rule.Regex, p.Fields);
            p.Code = Get(p.Fields, "code");

            ResolveSpecies(p, rule);
            ResolveStems(p, rule);
            ResolveModifiers(p, rule);
            ResolveDrip(p, rule);
            ResolveRotation(p, rule);
            ResolveDrawing(p, rule);
            RunChecks(p, rule);

            return p;
        }

        /// <summary>
        /// As <see cref="Parse(string, string)"/>, for a point whose crew note arrived apart from
        /// its code -- in a point property or the full description (see <see cref="FieldNoteReader"/>).
        /// The note is read as if it had been typed after the code, but only where the code takes
        /// notes: a bare code (PP, SN, CB) or a rule with a notes vocabulary (the pole). A tree's
        /// grammar has no place for "LEANING", so a tree is parsed exactly as before. A bare code
        /// the office labels with its code (symbolLabels) is labelled with the note instead when no
        /// rule reads it. Whatever the note cannot improve is parsed exactly as before.
        /// </summary>
        public ParsedPoint Parse(string pointNumber, string rawDescription, string note)
        {
            var plain = Parse(pointNumber, rawDescription);
            note = FieldNoteReader.Strip(note, rawDescription);
            if (note == null || plain.Ignored) return plain;

            var bare = plain.NoContent || plain.RuleId == SymbolLabelRuleId;
            var rule = plain.Recognized && !bare
                ? _cfg.Codes.FirstOrDefault(c => c.Enabled && c.Id == plain.RuleId)
                : null;
            if (!bare && (rule == null || rule.Notes == null)) return plain;

            var combined = Parse(pointNumber, FieldNoteReader.Normalize(rawDescription) + " " + note);
            if (combined.Recognized && !combined.HasErrors && !combined.Unhandled && !combined.Ignored &&
                combined.RuleId != SymbolLabelRuleId)
            {
                combined.Note = note;
                return combined;
            }

            if (plain.RuleId == SymbolLabelRuleId)
            {
                var symbol = SymbolFor(FieldNoteReader.Normalize(rawDescription));
                plain.LabelLines.Clear();
                if (symbol != null && !string.IsNullOrWhiteSpace(symbol.Label)) plain.LabelLines.Add(symbol.Label);
                plain.LabelLines.Add(note);
                plain.LabelText = string.Join(MTextLineBreak, plain.LabelLines.ToArray());
                plain.Fields["note"] = note;
                plain.Note = note;
                plain.RawDescription = FieldNoteReader.Normalize(rawDescription) + " " + note;
            }
            return plain;
        }

        private const string SymbolLabelRuleId = "symbol-label";

        /// <summary>The symbolLabels entry for a bare code; trailing figure digits match the
        /// base code (CB2 -> CB). Null when the code is not listed.</summary>
        private SymbolLabelRule SymbolFor(string normalized)
        {
            var trimmedEnd = normalized.Length;
            while (trimmedEnd > 0 && char.IsDigit(normalized[trimmedEnd - 1]))
                trimmedEnd--;
            var baseCode = trimmedEnd > 0 ? normalized.Substring(0, trimmedEnd) : normalized;

            SymbolLabelRule symbol;
            return _symbolLabels.TryGetValue(baseCode, out symbol) ? symbol : null;
        }

        public IList<ParsedPoint> ParseAll(IEnumerable<KeyValuePair<string, string>> points)
        {
            // Deterministic order: identical input must produce identical output, or
            // every drawing comparison shows phantom changes on re-run.
            return points
                .Select(kv => Parse(kv.Key, kv.Value))
                .OrderBy(x => NumericKey(x.PointNumber))
                .ThenBy(x => x.PointNumber, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        // ------------------------------------------------------------------ stages

        private static void CaptureGroups(Match m, Regex rx, IDictionary<string, string> into)
        {
            foreach (var name in rx.GetGroupNames())
            {
                int ignored;
                if (int.TryParse(name, out ignored)) continue;   // skip positional groups
                var g = m.Groups[name];
                if (g.Success && !string.IsNullOrWhiteSpace(g.Value))
                    into[name] = g.Value.Trim();
            }
        }

        private void ResolveSpecies(ParsedPoint p, CodeRule rule)
        {
            var letter = Get(p.Fields, "species");
            if (string.IsNullOrEmpty(letter)) return;

            string name;
            if (rule.Species != null && rule.Species.TryGetValue(letter, out name))
            {
                p.Species = name;
                p.Fields["species"] = name;
            }
            else
            {
                p.Add(Severity.Error, "SPECIES",
                    string.Format("Unknown species letter '{0}' in code '{1}'.", letter, p.Code));
            }
        }

        private void ResolveStems(ParsedPoint p, CodeRule rule)
        {
            var raw = Get(p.Fields, "trunk");
            if (string.IsNullOrEmpty(raw)) return;

            var parts = raw.Split(StemSeparators, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                double v;
                if (TryNum(part, out v)) p.Stems.Add(v);
                else
                {
                    p.Add(Severity.Error, "TRUNK",
                        string.Format("Trunk value '{0}' is not a number.", part));
                    return;
                }
            }

            if (p.Stems.Count == 0) return;

            // More than one diameter means a cluster. A modifier carrying an explicit
            // count can still override this.
            p.StemCount = p.Stems.Count;

            p.TrunkInches = Average(p.Stems, _cfg.MultiStemAverage);

            // Keep the individual stems. If the reviewing agency uses a different
            // multi-stem formula than the office does, the average alone cannot be
            // recomputed -- the raw stems can.
            p.Fields["trunk.stems"] = string.Join(",", p.Stems.Select(
                s => s.ToString(CultureInfo.InvariantCulture)).ToArray());
            p.Fields["trunk.count"] = p.Stems.Count.ToString(CultureInfo.InvariantCulture);

            // Unrounded, for anything that drives geometry. "trunk" is rounded for
            // display, and a symbol scaled off a rounded label is a symbol drawn at
            // the wrong size -- a 10.5" average would scale as 10".
            p.Fields["trunk.exact"] = p.TrunkInches.Value.ToString("R", CultureInfo.InvariantCulture);

            p.Fields["trunk"] = p.TrunkInches.Value.ToString(
                "F" + _cfg.TrunkDecimals, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Collapses a cluster's stem diameters to one reported size. Public so the
        /// setup window can show a worked example of what each method produces --
        /// this is the number that goes to the reviewing agency.
        /// </summary>
        public static double Average(IList<double> stems, StemAverageMethod method)
        {
            if (stems.Count == 1) return stems[0];
            switch (method)
            {
                case StemAverageMethod.Quadratic:
                    return Math.Sqrt(stems.Sum(s => s * s));
                case StemAverageMethod.LargestPlusHalf:
                    var max = stems.Max();
                    return max + 0.5 * (stems.Sum() - max);
                case StemAverageMethod.Sum:
                    return stems.Sum();
                default:
                    return stems.Average();
            }
        }

        private void ResolveModifiers(ParsedPoint p, CodeRule rule)
        {
            var modsText = Get(p.Fields, "mods");
            if (string.IsNullOrEmpty(modsText)) return;

            var tokens = modsText.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var notes = rule.Notes;
            var values = new List<NoteValue>();

            var i = 0;
            while (i < tokens.Length)
            {
                // The rule's own vocabulary first: it may span words ("DEAD END" on a sign
                // is wording, not the DEAD modifier) and it is the more specific reading.
                if (notes != null)
                {
                    var consumed = ReadTerm(notes, tokens, i, values);
                    if (consumed > 0) { i += consumed; continue; }
                }

                var token = tokens[i];
                var hit = _cfg.Modifiers.FirstOrDefault(m => m.Regex.IsMatch(token));
                if (hit != null)
                {
                    ApplyModifier(p, hit, token);
                    i++;
                    continue;
                }

                if (notes == null)
                {
                    // Never silently drop an unrecognised modifier. A token like
                    // DEAD-PROTECTED that the parser ignores is how a tree ends up
                    // on the wrong plan sheet.
                    p.Add(Severity.Error, "MODIFIER",
                        string.Format("Unrecognised modifier token '{0}'.", token));
                    i++;
                    continue;
                }

                // A note word in no term. Kept as written (and said so), or refused --
                // never dropped without a word.
                switch (notes.Unknown)
                {
                    case UnknownNoteMode.Refuse:
                        p.Add(Severity.Error, "NOTE",
                            string.Format("'{0}' is not in the {1} notes vocabulary.", token, rule.Id));
                        break;
                    case UnknownNoteMode.Flag:
                        p.Add(Severity.Warning, "NOTE",
                            string.Format("'{0}' is not in the {1} notes vocabulary; labelled as written. Add it to the vocabulary to word it.", token, rule.Id));
                        values.Add(new NoteValue(notes.UnknownGroup, token.ToUpperInvariant(), int.MaxValue, i));
                        break;
                    default:
                        p.Add(Severity.Info, "NOTE",
                            string.Format("'{0}' labelled as written.", token));
                        values.Add(new NoteValue(notes.UnknownGroup, token.ToUpperInvariant(), int.MaxValue, i));
                        break;
                }
                i++;
            }

            if (notes != null) FillNoteGroups(p, notes, values);

            // Apply order is by priority so that reordering tokens in the field note
            // cannot change the resulting label.
            var ordered = p.Modifiers.OrderBy(m => m.Priority).ThenBy(m => m.Id).ToList();
            p.Modifiers.Clear();
            foreach (var m in ordered) p.Modifiers.Add(m);
        }

        private void ApplyModifier(ParsedPoint p, ModifierRule hit, string token)
        {
            var m2 = hit.Regex.Match(token);
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            CaptureGroups(m2, hit.Regex, fields);

            foreach (var kv in fields)
                p.Fields[kv.Key] = kv.Value;

            p.Modifiers.Add(new ResolvedModifier(hit.Id, token, hit.Priority, fields));

            if (hit.Flag)
                p.Add(Severity.Warning, "FLAG",
                    string.Format("Modifier '{0}' is flagged for review.", hit.Id));

            if (!string.IsNullOrEmpty(hit.StemCountFrom))
            {
                var cs = Get(fields, hit.StemCountFrom);
                int count;
                if (int.TryParse(cs, NumberStyles.Integer, CultureInfo.InvariantCulture, out count))
                {
                    p.StemCount = count;
                    if (count < 2)
                        p.Add(Severity.Warning, "CLUSTER",
                            string.Format("Cluster count of {0} on a cluster modifier is probably a typo.", count));
                    if (p.Stems.Count > 1 && p.Stems.Count != count)
                        p.Add(Severity.Warning, "CLUSTER",
                            string.Format("Cluster count is {0} but {1} stem diameters were recorded.",
                                count, p.Stems.Count));
                }
                else
                {
                    p.Add(Severity.Error, "CLUSTER",
                        string.Format("Cluster count '{0}' is not an integer.", cs));
                }
            }
        }

        // ------------------------------------------------------------------ notes

        /// <summary>One note word (or phrase) read into a group.</summary>
        private sealed class NoteValue
        {
            public readonly string Group;
            public readonly string Text;
            public readonly int TermIndex;
            public readonly int TokenIndex;

            public NoteValue(string group, string text, int termIndex, int tokenIndex)
            {
                Group = group; Text = text; TermIndex = termIndex; TokenIndex = tokenIndex;
            }
        }

        /// <summary>
        /// Tries the terms, in the order they are listed, at word <paramref name="start"/>.
        /// Returns how many words the first matching term took, 0 when none matched.
        /// </summary>
        private static int ReadTerm(NotesRule notes, string[] tokens, int start, List<NoteValue> values)
        {
            var rest = string.Join(" ", tokens, start, tokens.Length - start);
            for (var t = 0; t < notes.Terms.Count; t++)
            {
                var term = notes.Terms[t];
                if (term == null || term.Regex == null) continue;
                var m = term.Regex.Match(rest);
                if (!m.Success || m.Length == 0) continue;

                var words = m.Value.Trim();
                var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                CaptureGroups(m, term.Regex, fields);
                fields["token"] = words.ToUpperInvariant();
                var text = string.IsNullOrWhiteSpace(term.Text)
                    ? words.ToUpperInvariant()
                    : Expand(term.Text, fields).Trim();
                if (text.Length > 0) values.Add(new NoteValue(term.Group, text, t, start));
                return words.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length;
            }
            return 0;
        }

        /// <summary>
        /// Puts each group's values in its reading order (the vocabulary's, or as written)
        /// and stores the joined text as a field the label can name. A field the pattern
        /// already captured under the same name keeps its value first.
        /// </summary>
        private static void FillNoteGroups(ParsedPoint p, NotesRule notes, List<NoteValue> values)
        {
            var groupTexts = new List<string>();
            foreach (var group in notes.GroupNames())
            {
                var rule = notes.GroupRule(group);
                var order = rule != null && rule.Order.HasValue
                    ? rule.Order.Value
                    : (string.Equals(group, notes.UnknownGroup, StringComparison.OrdinalIgnoreCase)
                        ? NoteGroupOrder.AsShot : NoteGroupOrder.Vocabulary);
                var joiner = rule != null && rule.Joiner != null
                    ? rule.Joiner
                    : (order == NoteGroupOrder.AsShot ? " " : ", ");

                var mine = values.Where(v => string.Equals(v.Group, group, StringComparison.OrdinalIgnoreCase));
                mine = order == NoteGroupOrder.AsShot
                    ? mine.OrderBy(v => v.TokenIndex)
                    : mine.OrderBy(v => v.TermIndex).ThenBy(v => v.TokenIndex);

                var texts = new List<string>();
                foreach (var v in mine)
                    if (!texts.Contains(v.Text, StringComparer.OrdinalIgnoreCase)) texts.Add(v.Text);
                if (texts.Count == 0) continue;

                var joined = string.Join(joiner, texts.ToArray());
                string captured;
                if (p.Fields.TryGetValue(group, out captured) && !string.IsNullOrWhiteSpace(captured) &&
                    !texts.Contains(captured, StringComparer.OrdinalIgnoreCase))
                    joined = captured + joiner + joined;
                p.Fields[group] = joined;
                groupTexts.Add(joined);
            }

            if (groupTexts.Count > 0 && !p.Fields.ContainsKey("notes"))
                p.Fields["notes"] = string.Join(" ", groupTexts.ToArray());
        }

        private void ResolveDrip(ParsedPoint p, CodeRule rule)
        {
            if (rule.DripLine == null || string.IsNullOrEmpty(rule.DripLine.RadiusFrom)) return;

            var raw = Get(p.Fields, rule.DripLine.RadiusFrom);
            if (string.IsNullOrEmpty(raw))
            {
                p.Add(Severity.Error, "DRIP",
                    "No drip radius in description; cannot draw drip line.");
                return;
            }

            double feet;
            if (!TryNum(raw, out feet))
            {
                p.Add(Severity.Error, "DRIP",
                    string.Format("Drip radius '{0}' is not a number.", raw));
                return;
            }

            p.DripRadius = feet * _cfg.UnitsPerFoot;
            p.DripLayer = rule.DripLine.Layer;
            p.DripUnify = rule.DripLine.Unify;
        }

        private void ResolveRotation(ParsedPoint p, CodeRule rule)
        {
            if (rule.Rotation == null || string.IsNullOrEmpty(rule.Rotation.From)) return;

            var raw = Get(p.Fields, rule.Rotation.From);
            if (string.IsNullOrEmpty(raw)) return;

            double deg;
            if (!TryNum(raw, out deg))
            {
                p.Add(Severity.Error, "ROTATION",
                    string.Format("Rotation value '{0}' is not a number.", raw));
                return;
            }

            // Survey azimuth runs clockwise from north; AutoCAD runs counter-clockwise
            // from east. Getting this backwards leaves every sign in the plan cocked.
            p.RotationDegrees = rule.Rotation.Mode == RotationMode.Azimuth
                ? Geometry.Angles.AzimuthToCadDegrees(deg)
                : Geometry.Angles.NormalizeDegrees(deg);
        }

        private void ResolveDrawing(ParsedPoint p, CodeRule rule)
        {
            var block = rule.Block;

            // More than one recorded diameter means a cluster, so the label says so
            // without needing a modifier token to announce it.
            var label = rule.Label == null
                ? null
                : (p.Stems.Count > 1 && !string.IsNullOrEmpty(rule.Label.ClusterFormat)
                    ? rule.Label.ClusterFormat
                    : rule.Label.Format);

            var blockLayer = rule.BlockLayer;
            var labelLayer = rule.Label != null ? rule.Label.Layer : null;
            var dripLayer = p.DripLayer;
            var suffix = string.Empty;

            foreach (var m in p.Modifiers)
            {
                var mr = _cfg.Modifiers.First(x => x.Id == m.Id);
                if (!string.IsNullOrEmpty(mr.Block)) block = mr.Block;
                if (!string.IsNullOrEmpty(mr.LabelFormat)) label = mr.LabelFormat;
                if (!string.IsNullOrEmpty(mr.LabelSuffix)) label = AppendToFirstLine(label, mr.LabelSuffix);
                if (!string.IsNullOrEmpty(mr.LayerSuffix)) suffix += mr.LayerSuffix;
            }

            p.BlockName = Expand(block, p.Fields);
            ComposeLabel(p, label);
            p.BlockLayer = Append(Expand(blockLayer, p.Fields), suffix);
            p.LabelLayer = Append(Expand(labelLayer, p.Fields), suffix);
            p.DripLayer = Append(Expand(dripLayer, p.Fields), suffix);
            p.TagPrefix = rule.TagPrefix;

            // Civil 3D already placed the symbol. A rule has to say so explicitly
            // before FTF adds one of its own.
            p.InsertBlock = rule.InsertBlock;
            if (rule.Label != null) p.Leader = rule.Label.Leader;

            if (!string.IsNullOrEmpty(rule.BlockScaleFrom))
            {
                double v;
                if (TryNum(Get(p.Fields, rule.BlockScaleFrom), out v))
                {
                    var div = rule.BlockScaleDivisor;
                    if (Math.Abs(div) < 1e-9)
                    {
                        p.Add(Severity.Warning, "SCALE", "blockScaleDivisor is zero; using scale 1.");
                        p.BlockScale = 1.0;
                    }
                    else
                    {
                        p.BlockScale = v / div;
                        if (p.BlockScale <= 0)
                        {
                            p.Add(Severity.Warning, "SCALE",
                                "Computed block scale is not positive; using scale 1.");
                            p.BlockScale = 1.0;
                        }
                    }
                }
            }
        }

        // ------------------------------------------------------------------ label text

        private const string MTextLineBreak = "\\P";

        /// <summary>
        /// The template's first line gets the modifier's suffix: "(DEAD)" belongs beside
        /// "POLE 1234", not at the bottom of a three-line label.
        /// </summary>
        private static string AppendToFirstLine(string label, string suffix)
        {
            if (string.IsNullOrEmpty(label)) return (label ?? string.Empty) + suffix;
            var breakAt = FirstBreak(label);
            return breakAt < 0 ? label + suffix : label.Insert(breakAt, suffix);
        }

        private static int FirstBreak(string template)
        {
            var mtext = template.IndexOf(MTextLineBreak, StringComparison.Ordinal);
            var newline = template.IndexOf('\n');
            if (mtext < 0) return newline;
            if (newline < 0) return mtext;
            return Math.Min(mtext, newline);
        }

        /// <summary>
        /// Builds the label. A plain one-line template expands exactly as it always has.
        /// A template with [optional parts] or line breaks (\P) is expanded line by line:
        /// optional parts with no value vanish, spaces collapse, and an empty line is
        /// dropped -- so a pole with no attachments has no "W/" line at all.
        /// </summary>
        private static void ComposeLabel(ParsedPoint p, string template)
        {
            p.LabelLines.Clear();
            if (template == null) { p.LabelText = null; return; }

            if (template.IndexOf('[') < 0 && FirstBreak(template) < 0)
            {
                p.LabelText = Expand(template, p.Fields);
                if (!string.IsNullOrEmpty(p.LabelText)) p.LabelLines.Add(p.LabelText);
                return;
            }

            foreach (var line in ComposeLines(template, p.Fields))
                p.LabelLines.Add(line);
            p.LabelText = string.Join(MTextLineBreak, p.LabelLines.ToArray());
        }

        /// <summary>
        /// The label lines a template produces from the fields. Public so the setup window
        /// and the bench and driveway commands use the same rules for their own templates.
        /// </summary>
        public static IList<string> ComposeLines(string template, IDictionary<string, string> fields)
        {
            var lines = new List<string>();
            if (string.IsNullOrEmpty(template)) return lines;
            var normalized = template.Replace("\r\n", "\n").Replace(MTextLineBreak, "\n");
            foreach (var raw in normalized.Split('\n'))
            {
                var line = Expand(ResolveOptional(raw, fields), fields);
                line = Regex.Replace(line, " {2,}", " ").Trim();
                if (line.Length > 0) lines.Add(line);
            }
            return lines;
        }

        /// <summary>Keeps a [bracketed] part only when a placeholder inside it has a value;
        /// a part with no placeholder is kept as written. [[ and ]] are literal brackets.</summary>
        private static string ResolveOptional(string line, IDictionary<string, string> fields)
        {
            if (line.IndexOf('[') < 0 && line.IndexOf(']') < 0) return line;
            var sb = new System.Text.StringBuilder(line.Length);
            var i = 0;
            while (i < line.Length)
            {
                var c = line[i];
                if ((c == '[' || c == ']') && i + 1 < line.Length && line[i + 1] == c)
                {
                    sb.Append(c);
                    i += 2;
                    continue;
                }
                if (c == '[')
                {
                    var close = line.IndexOf(']', i + 1);
                    if (close < 0) { sb.Append(line.Substring(i)); break; }
                    var inner = line.Substring(i + 1, close - i - 1);
                    if (SegmentHasValue(inner, fields)) sb.Append(inner);
                    i = close + 1;
                    continue;
                }
                sb.Append(c);
                i++;
            }
            return sb.ToString();
        }

        private static bool SegmentHasValue(string segment, IDictionary<string, string> fields)
        {
            var any = false;
            foreach (Match m in Placeholder.Matches(segment))
            {
                any = true;
                string value;
                if (fields.TryGetValue(m.Groups["name"].Value, out value) && !string.IsNullOrWhiteSpace(value))
                    return true;
            }
            return !any;
        }

        private static string Append(string layer, string suffix)
        {
            if (string.IsNullOrEmpty(layer) || string.IsNullOrEmpty(suffix)) return layer;
            return layer + suffix;
        }

        private void RunChecks(ParsedPoint p, CodeRule rule)
        {
            foreach (var check in rule.Validate)
            {
                var raw = Get(p.Fields, check.Field);
                if (string.IsNullOrEmpty(raw)) continue;

                double v;
                if (!TryNum(raw, out v)) continue;

                if ((check.Min.HasValue && v < check.Min.Value) ||
                    (check.Max.HasValue && v > check.Max.Value))
                {
                    var msg = check.Message ?? string.Format(
                        "Value {0} for '{1}' is outside the expected range.", raw, check.Field);
                    p.Add(check.Severity, "RANGE", msg + string.Format(" (got {0})", raw));
                }
            }

            // Structural check: a drip radius smaller than the trunk radius almost
            // always means the two values were entered in the wrong order.
            if (p.DripRadius.HasValue && p.TrunkInches.HasValue)
            {
                var trunkRadiusUnits = (p.TrunkInches.Value / 2.0 / 12.0) * _cfg.UnitsPerFoot;
                if (p.DripRadius.Value <= trunkRadiusUnits)
                    p.Add(Severity.Warning, "SWAPPED",
                        "Drip radius is not larger than the trunk radius; values may be transposed.");
            }
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>
        /// Substitutes {placeholder} fields into a template. Public so the CAD layer can
        /// work out which block names a rule is capable of producing.
        /// </summary>
        public static string Expand(string template, IDictionary<string, string> fields)
        {
            if (string.IsNullOrEmpty(template)) return template;

            return Placeholder.Replace(template, m =>
            {
                var name = m.Groups["name"].Value;
                string val;
                if (!fields.TryGetValue(name, out val)) return string.Empty;

                var fmt = m.Groups["fmt"].Success ? m.Groups["fmt"].Value : null;
                if (string.IsNullOrEmpty(fmt)) return val;

                double num;
                if (!TryNum(val, out num)) return val;

                // A malformed format specifier must degrade to the raw value, not
                // throw: one bad rule template would otherwise abort every draw
                // command for every point. The rule editor validates formats too;
                // this is the last line of defence for hand-edited files.
                try
                {
                    return num.ToString(fmt, CultureInfo.InvariantCulture);
                }
                catch (FormatException)
                {
                    return val;
                }
            });
        }

        /// <summary>
        /// True when the description says more than just its code. "PP" does not;
        /// "PP 1234" does.
        /// </summary>
        internal static bool HasDataBeyondCode(string normalized)
        {
            if (string.IsNullOrEmpty(normalized)) return false;
            return normalized.IndexOf(' ') >= 0;
        }

        /// <summary>The leading token, which is the field code for every rule shape.</summary>
        public static string FirstToken(string normalized)
        {
            if (string.IsNullOrEmpty(normalized)) return string.Empty;
            var space = normalized.IndexOf(' ');
            return space < 0 ? normalized : normalized.Substring(0, space);
        }

        private static string Get(IDictionary<string, string> d, string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            string v;
            return d.TryGetValue(key, out v) ? v : null;
        }

        private static bool TryNum(string s, out double value)
        {
            return double.TryParse((s ?? string.Empty).Trim(),
                NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private static long NumericKey(string pointNumber)
        {
            long n;
            return long.TryParse(pointNumber, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out n) ? n : long.MaxValue;
        }
    }
}
