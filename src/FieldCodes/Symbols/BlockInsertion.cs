using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using FieldCodes.Easements;
using FieldCodes.Geometry;
using FieldCodes.Orient;

namespace FieldCodes.Symbols
{
    /// <summary>Where an opted-in block goes relative to its point ("blockInsertion" in rules.json).</summary>
    public enum BlockPlacement
    {
        /// <summary>On the point. The default, and what FTF always did.</summary>
        AtPoint = 0,
        /// <summary>Moved by blockOffsetX east and blockOffsetY north (drawing units), whatever
        /// way the block turns.</summary>
        Offset = 1,
        /// <summary>Moved by blockOffsetX / blockOffsetY along the block's own x and y, so the
        /// offset turns with the block (a panel beside its post).</summary>
        OffsetRotated = 2
    }

    /// <summary>How an opted-in block is turned ("blockRotation" in rules.json).</summary>
    public enum BlockRotationMethod
    {
        /// <summary>From the rule's field rotation ("SIGN STOP 135"), turned against the symbol's
        /// native zero when the orientation standard describes this block. The default, and what
        /// FTF always did; no field rotation leaves it at 0.</summary>
        Field = 0,
        /// <summary>Never turned: rotation 0.</summary>
        None = 1,
        /// <summary>The point's own marker rotation: what FTFPOINTS set from the field azimuth or
        /// FTFORIENT decided. Read when the block is placed (FTFPOINTS).</summary>
        Marker = 2
    }

    /// <summary>
    /// The decisions FTFPOINTS makes when it inserts a block for a rule that opted in
    /// (insertBlock): scale, rotation and position. Pure, so the defaults can be proved to be
    /// exactly what FTF did before these settings existed.
    /// </summary>
    public static class BlockInsertion
    {
        /// <summary>
        /// The block's scale: from the field the rule names (already worked out by the parser as
        /// <see cref="ParsedPoint.BlockScale"/>, the value divided by blockScaleDivisor), or else the
        /// rule's fixed blockScale (1 when absent). Never zero or negative.
        /// </summary>
        public static double Scale(CodeRule rule, ParsedPoint p)
        {
            if (rule == null || !string.IsNullOrWhiteSpace(rule.BlockScaleFrom))
                return p != null && p.BlockScale > 0 ? p.BlockScale : 1.0;
            return rule.BlockScale > 0 && !double.IsInfinity(rule.BlockScale) ? rule.BlockScale : 1.0;
        }

        /// <summary>
        /// The block's rotation in AutoCAD degrees, or null to leave it at 0.
        /// Field (default): the parsed rotation; the orientation standard's native zero is taken off
        /// only when the standard describes this very block (its zero belongs to its own block).
        /// None: null. Marker: <paramref name="markerDegrees"/>, the point's marker rotation.
        /// </summary>
        public static double? RotationDegrees(CodeRule rule, ParsedPoint p, OrientationStandard orient,
                                              string blockName, double? markerDegrees)
        {
            var method = rule != null ? rule.BlockRotation : BlockRotationMethod.Field;
            switch (method)
            {
                case BlockRotationMethod.None:
                    return null;
                case BlockRotationMethod.Marker:
                    return markerDegrees.HasValue ? Angles.NormalizeDegrees(markerDegrees.Value) : (double?)null;
                default:
                    if (p == null || !p.RotationDegrees.HasValue) return null;
                    var symbol = orient != null ? orient.SymbolFor(p.Code) : null;
                    return symbol != null && symbol.DrawnWith(blockName)
                        ? p.MarkerRotationDegrees ?? p.RotationDegrees.Value
                        : p.RotationDegrees.Value;
            }
        }

        /// <summary>Where the block's base point goes for a point at <paramref name="point"/>, the
        /// block turned <paramref name="rotationDegrees"/>. The offset is in drawing units and is
        /// not scaled with the block.</summary>
        public static P2 Position(CodeRule rule, P2 point, double rotationDegrees)
        {
            if (rule == null) return point;
            var offset = new P2(Finite(rule.BlockOffsetX), Finite(rule.BlockOffsetY));
            switch (rule.BlockInsertion)
            {
                case BlockPlacement.Offset:
                    return point + offset;
                case BlockPlacement.OffsetRotated:
                    return point + PreviewFrame.Rotate(offset, rotationDegrees);
                default:
                    return point;
            }
        }

        /// <summary>"Scale 1.5 from trunk.exact / 12; turned from the field azimuth; on the point".</summary>
        public static string Describe(CodeRule rule)
        {
            if (rule == null) return string.Empty;
            var c = CultureInfo.InvariantCulture;
            var scale = !string.IsNullOrWhiteSpace(rule.BlockScaleFrom)
                ? "scale from field " + rule.BlockScaleFrom + (Math.Abs(rule.BlockScaleDivisor - 1) > 1e-12 ? " / " + rule.BlockScaleDivisor.ToString("0.###", c) : string.Empty)
                : "scale " + (rule.BlockScale > 0 ? rule.BlockScale : 1).ToString("0.###", c);
            string turn;
            switch (rule.BlockRotation)
            {
                case BlockRotationMethod.None: turn = "never turned"; break;
                case BlockRotationMethod.Marker: turn = "turned with the point's marker (FTFORIENT)"; break;
                default: turn = rule.Rotation != null ? "turned from field '" + rule.Rotation.From + "'" : "not turned (no field rotation)"; break;
            }
            string where;
            switch (rule.BlockInsertion)
            {
                case BlockPlacement.Offset:
                    where = string.Format(c, "{0:0.###} E, {1:0.###} N of the point", rule.BlockOffsetX, rule.BlockOffsetY);
                    break;
                case BlockPlacement.OffsetRotated:
                    where = string.Format(c, "{0:0.###}, {1:0.###} from the point, turning with the block", rule.BlockOffsetX, rule.BlockOffsetY);
                    break;
                default: where = "on the point"; break;
            }
            return scale + "; " + turn + "; " + where;
        }

        private static double Finite(double v)
        {
            return double.IsNaN(v) || double.IsInfinity(v) ? 0.0 : v;
        }
    }

    /// <summary>
    /// The text FTFPOINTS writes into an inserted block's attributes ("blockAttributes": tag ->
    /// template). Templates use the label syntax: {group} for any named group of the rule's
    /// pattern or note vocabulary, {name:0.00} to format a number, [optional text] kept only when a
    /// placeholder inside it has a value, \P between lines (joined with a space: an attribute is
    /// one line). Besides the rule's own fields, these are always available:
    /// {point} point number, {elevation} / {elev}, {northing}, {easting}, {raw} the description as
    /// shot, {note}, {label} the label FTF would write, {rule} the rule id, {azimuth} the field
    /// azimuth when the rule reads one. A group of the pattern with one of these names wins.
    /// </summary>
    public static class AttributeTemplate
    {
        /// <summary>Placeholders FTF supplies for every point, beside the rule's own fields.</summary>
        public static readonly string[] BuiltInTokens =
            { "point", "elevation", "elev", "northing", "easting", "raw", "note", "label", "rule", "azimuth" };

        private static readonly Regex Placeholder =
            new Regex(@"\{(?<name>[A-Za-z_][A-Za-z0-9_.]*)(?::[^}]+)?\}", RegexOptions.CultureInvariant);

        private static readonly Regex Tag = new Regex(@"^[^\s{}]+$", RegexOptions.CultureInvariant);

        /// <summary>An attribute tag AutoCAD accepts: no spaces.</summary>
        public static bool IsValidTag(string tag)
        {
            return !string.IsNullOrWhiteSpace(tag) && Tag.IsMatch(tag.Trim());
        }

        /// <summary>The placeholder names a template uses, in order, each once.</summary>
        public static IList<string> Placeholders(string template)
        {
            var names = new List<string>();
            if (string.IsNullOrEmpty(template)) return names;
            foreach (Match m in Placeholder.Matches(template))
            {
                var name = m.Groups["name"].Value;
                if (!names.Contains(name, StringComparer.OrdinalIgnoreCase)) names.Add(name);
            }
            return names;
        }

        /// <summary>Every value a template can use for one point: the parsed fields, then the
        /// built-ins the fields do not already define.</summary>
        public static IDictionary<string, string> Values(ParsedPoint p, double easting, double northing, double elevation)
        {
            var c = CultureInfo.InvariantCulture;
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (p != null)
                foreach (var kv in p.Fields)
                    values[kv.Key] = kv.Value;

            Action<string, string> builtIn = (name, value) =>
            {
                if (!values.ContainsKey(name) && value != null) values[name] = value;
            };
            if (p != null)
            {
                builtIn("point", p.PointNumber);
                builtIn("raw", p.RawDescription);
                builtIn("note", p.Note);
                builtIn("label", p.LabelDisplay);
                builtIn("rule", p.RuleId);
                builtIn("code", p.Code);
                builtIn("species", p.Species);
                if (p.RotationDegrees.HasValue && p.RotationIsAzimuth)
                    builtIn("azimuth", Angles.NormalizeDegrees(90.0 - p.RotationDegrees.Value).ToString("R", c));
            }
            // A point with no elevation reads as NaN or as a huge sentinel: no value, so an
            // [optional] part around {elevation} drops out.
            if (Real(elevation))
            {
                builtIn("elevation", elevation.ToString("R", c));
                builtIn("elev", elevation.ToString("R", c));
            }
            if (Real(northing)) builtIn("northing", northing.ToString("R", c));
            if (Real(easting)) builtIn("easting", easting.ToString("R", c));
            return values;
        }

        private static bool Real(double v)
        {
            return !double.IsNaN(v) && !double.IsInfinity(v) && Math.Abs(v) < 1e99;
        }

        /// <summary>The attribute text a template makes from the values; one line.</summary>
        public static string Format(string template, IDictionary<string, string> values)
        {
            if (string.IsNullOrEmpty(template)) return string.Empty;
            var lines = FieldCodeParser.ComposeLines(template, values ?? new Dictionary<string, string>());
            return string.Join(" ", lines.ToArray());
        }
    }
}
