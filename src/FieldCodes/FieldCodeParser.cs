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
                    var trimmedEnd = normalized.Length;
                    while (trimmedEnd > 0 && char.IsDigit(normalized[trimmedEnd - 1]))
                        trimmedEnd--;
                    var baseCode = trimmedEnd > 0
                        ? normalized.Substring(0, trimmedEnd) : normalized;

                    SymbolLabelRule symbol;
                    if (_symbolLabels.TryGetValue(baseCode, out symbol))
                    {
                        p.Recognized = true;
                        p.RuleId = "symbol-label";
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
            ResolveModifiers(p, normalized);
            ResolveDrip(p, rule);
            ResolveRotation(p, rule);
            ResolveDrawing(p, rule);
            RunChecks(p, rule);

            return p;
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

        private void ResolveModifiers(ParsedPoint p, string normalized)
        {
            var modsText = Get(p.Fields, "mods");
            if (string.IsNullOrEmpty(modsText)) return;

            var tokens = modsText.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var token in tokens)
            {
                var hit = _cfg.Modifiers.FirstOrDefault(m => m.Regex.IsMatch(token));
                if (hit == null)
                {
                    // Never silently drop an unrecognised modifier. A token like
                    // DEAD-PROTECTED that the parser ignores is how a tree ends up
                    // on the wrong plan sheet.
                    p.Add(Severity.Error, "MODIFIER",
                        string.Format("Unrecognised modifier token '{0}'.", token));
                    continue;
                }

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

            // Apply order is by priority so that reordering tokens in the field note
            // cannot change the resulting label.
            var ordered = p.Modifiers.OrderBy(m => m.Priority).ThenBy(m => m.Id).ToList();
            p.Modifiers.Clear();
            foreach (var m in ordered) p.Modifiers.Add(m);
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
                if (!string.IsNullOrEmpty(mr.LabelSuffix)) label = (label ?? string.Empty) + mr.LabelSuffix;
                if (!string.IsNullOrEmpty(mr.LayerSuffix)) suffix += mr.LayerSuffix;
            }

            p.BlockName = Expand(block, p.Fields);
            p.LabelText = Expand(label, p.Fields);
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
        internal static string FirstToken(string normalized)
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
