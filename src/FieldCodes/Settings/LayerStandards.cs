using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace FieldCodes.Settings
{
    /// <summary>One property a layer standard can set.</summary>
    public enum LayerProperty
    {
        Color = 0,
        Linetype = 1,
        LineWeight = 2,
        Plot = 3,
        Transparency = 4,
        Description = 5
    }

    /// <summary>
    /// What one layer -- or every layer matching a pattern -- looks like in the office standard:
    /// colour, linetype, lineweight, plot, transparency and description. Every property is
    /// optional; an unset one is left as the drawing has it. A name with * or ? is a pattern
    /// ("V-*-TEXT-E"); any other name is one exact layer.
    /// </summary>
    public sealed class LayerStandard
    {
        /// <summary>The layer name, or a pattern with * and ?.</summary>
        [JsonProperty("name")] public string Name { get; set; }

        /// <summary>An ACI number 1-255, one of AutoCAD's seven colour names, or a true colour "r,g,b".</summary>
        [JsonProperty("color", NullValueHandling = NullValueHandling.Ignore)] public string Color { get; set; }

        /// <summary>A linetype name. Loaded from acad.lin when the drawing does not have it.</summary>
        [JsonProperty("linetype", NullValueHandling = NullValueHandling.Ignore)] public string Linetype { get; set; }

        /// <summary>Millimetres as AutoCAD lists them ("0.25") or "Default".</summary>
        [JsonProperty("lineweight", NullValueHandling = NullValueHandling.Ignore)] public string LineWeight { get; set; }

        /// <summary>True: the layer plots. False: it does not (QC and construction layers).</summary>
        [JsonProperty("plot", NullValueHandling = NullValueHandling.Ignore)] public bool? Plot { get; set; }

        /// <summary>Percent, 0 (opaque) to 90, as the Layer Properties Manager shows it.</summary>
        [JsonProperty("transparency", NullValueHandling = NullValueHandling.Ignore)] public int? Transparency { get; set; }

        [JsonProperty("description", NullValueHandling = NullValueHandling.Ignore)] public string Description { get; set; }

        /// <summary>True when the name carries * or ?: the standard applies to every layer it matches.</summary>
        [JsonIgnore]
        public bool IsPattern { get { return LayerStandards.IsPattern(Name); } }

        /// <summary>True when the standard sets nothing.</summary>
        [JsonIgnore]
        public bool IsEmpty
        {
            get
            {
                return string.IsNullOrWhiteSpace(Color) && string.IsNullOrWhiteSpace(Linetype) &&
                       string.IsNullOrWhiteSpace(LineWeight) && !Plot.HasValue && !Transparency.HasValue &&
                       string.IsNullOrWhiteSpace(Description);
            }
        }

        /// <summary>True when the standard sets <paramref name="property"/>.</summary>
        public bool Sets(LayerProperty property)
        {
            switch (property)
            {
                case LayerProperty.Color: return !string.IsNullOrWhiteSpace(Color);
                case LayerProperty.Linetype: return !string.IsNullOrWhiteSpace(Linetype);
                case LayerProperty.LineWeight: return !string.IsNullOrWhiteSpace(LineWeight);
                case LayerProperty.Plot: return Plot.HasValue;
                case LayerProperty.Transparency: return Transparency.HasValue;
                default: return !string.IsNullOrWhiteSpace(Description);
            }
        }

        /// <summary>The property as written in the standard, for display ("Plot", "No plot", "40").</summary>
        public string Text(LayerProperty property)
        {
            switch (property)
            {
                case LayerProperty.Color: return (Color ?? string.Empty).Trim();
                case LayerProperty.Linetype: return (Linetype ?? string.Empty).Trim();
                case LayerProperty.LineWeight: return (LineWeight ?? string.Empty).Trim();
                case LayerProperty.Plot: return Plot.HasValue ? (Plot.Value ? "Plot" : "No plot") : string.Empty;
                case LayerProperty.Transparency: return Transparency.HasValue ? Transparency.Value.ToString(CultureInfo.InvariantCulture) : string.Empty;
                default: return (Description ?? string.Empty).Trim();
            }
        }

        /// <summary>Copies one property from another standard (unset there clears it here).</summary>
        public void CopyFrom(LayerStandard other, LayerProperty property)
        {
            switch (property)
            {
                case LayerProperty.Color: Color = other == null ? null : other.Color; break;
                case LayerProperty.Linetype: Linetype = other == null ? null : other.Linetype; break;
                case LayerProperty.LineWeight: LineWeight = other == null ? null : other.LineWeight; break;
                case LayerProperty.Plot: Plot = other == null ? null : other.Plot; break;
                case LayerProperty.Transparency: Transparency = other == null ? null : other.Transparency; break;
                default: Description = other == null ? null : other.Description; break;
            }
        }

        public LayerStandard Clone() { return (LayerStandard)MemberwiseClone(); }
    }

    /// <summary>
    /// Settings > Layers, the properties half: what each layer FTF uses looks like. Applied when
    /// FTF creates a layer (<see cref="ApplyToNewLayers"/>), and to layers already in a drawing
    /// only through FTFLAYERUPDATE / "Update Drawing Layers", which previews first. Nothing here
    /// moves, restyles or erases an entity, and a layer FTF does not use is never touched.
    /// </summary>
    public sealed class LayerStandardsSettings : ISettingsSection
    {
        public string Title { get { return "Layer Standards"; } }
        public string AffectedCommands { get { return "Every command that creates a layer; FTFLAYERUPDATE; FTFCHECKSTANDARDS"; } }

        /// <summary>A layer FTF creates takes its standard's properties. Off: it takes the look of
        /// its family (V-UTIL-POWR-TEXT-E beside V-UTIL-POWR-E) as before.</summary>
        [JsonProperty("applyToNewLayers")]
        public bool ApplyToNewLayers { get; set; }

        /// <summary>One entry per layer or pattern. An exact name beats a pattern, property by property.</summary>
        [JsonProperty("layers", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<LayerStandard> Layers { get; set; }

        public LayerStandardsSettings() { RestoreDefaults(); }

        public void RestoreDefaults()
        {
            ApplyToNewLayers = true;
            Layers = new List<LayerStandard>();
        }

        public void Validate(ICollection<string> problems)
        {
            if (Layers == null) Layers = new List<LayerStandard>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in Layers)
            {
                if (s == null) continue;
                var name = (s.Name ?? string.Empty).Trim();
                foreach (var p in LayerStandards.Problems(s))
                    problems.Add("Layers: standard for " + (name.Length == 0 ? "(no name)" : name) + ": " + p + ".");
                if (name.Length > 0 && !seen.Add(name))
                    problems.Add("Layers: there are two standards for " + name + "; keep one.");
            }
        }

        /// <summary>The standard in force for one layer, exact before pattern.</summary>
        public ResolvedLayerStandard For(string layer)
        {
            return LayerStandards.Resolve(Layers, layer);
        }

        /// <summary>The exact (non-pattern) standard for a layer, or null.</summary>
        public LayerStandard Exact(string layer)
        {
            if (Layers == null || string.IsNullOrWhiteSpace(layer)) return null;
            return Layers.FirstOrDefault(s => s != null && !s.IsPattern &&
                                              string.Equals((s.Name ?? string.Empty).Trim(), layer.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>The layer's own standard, added (empty) when it has none.</summary>
        public LayerStandard ExactOrNew(string layer)
        {
            if (Layers == null) Layers = new List<LayerStandard>();
            var found = Exact(layer);
            if (found != null) return found;
            found = new LayerStandard { Name = (layer ?? string.Empty).Trim() };
            Layers.Add(found);
            return found;
        }

        /// <summary>Removes the layer's own standard (a pattern that matches it stays).</summary>
        public bool RemoveExact(string layer)
        {
            var found = Exact(layer);
            return found != null && Layers.Remove(found);
        }

        /// <summary>Drops standards that set nothing.</summary>
        public void RemoveEmpty()
        {
            if (Layers == null) { Layers = new List<LayerStandard>(); return; }
            Layers.RemoveAll(s => s == null || s.IsEmpty);
        }

        public LayerStandardsSettings Clone()
        {
            return new LayerStandardsSettings
            {
                ApplyToNewLayers = ApplyToNewLayers,
                Layers = (Layers ?? new List<LayerStandard>()).Where(s => s != null).Select(s => s.Clone()).ToList()
            };
        }
    }

    /// <summary>The properties in force for one layer, and which standard set each.</summary>
    public sealed class ResolvedLayerStandard
    {
        private readonly Dictionary<LayerProperty, string> _from = new Dictionary<LayerProperty, string>();

        public ResolvedLayerStandard(string layer)
        {
            Layer = layer;
            Values = new LayerStandard { Name = layer };
        }

        public string Layer { get; private set; }

        /// <summary>The merged values: each property from the most specific standard that sets it.</summary>
        public LayerStandard Values { get; private set; }

        /// <summary>The standard (exact name or pattern) a property came from; null when none sets it.</summary>
        public string Source(LayerProperty property)
        {
            string from;
            return _from.TryGetValue(property, out from) ? from : null;
        }

        /// <summary>True when the property came from a pattern rather than the layer's own standard.</summary>
        public bool FromPattern(LayerProperty property)
        {
            return LayerStandards.IsPattern(Source(property));
        }

        public bool IsEmpty { get { return _from.Count == 0; } }

        internal void Take(LayerStandard from, LayerProperty property)
        {
            if (_from.ContainsKey(property) || from == null || !from.Sets(property)) return;
            Values.CopyFrom(from, property);
            _from[property] = (from.Name ?? string.Empty).Trim();
        }
    }

    /// <summary>A colour as a layer can have it: an ACI index or a true colour. Never ByLayer.</summary>
    public sealed class LayerColor : IEquatable<LayerColor>
    {
        private static readonly string[] Names = { "red", "yellow", "green", "cyan", "blue", "magenta", "white" };

        private LayerColor() { }

        public short? Aci { get; private set; }
        public byte Red { get; private set; }
        public byte Green { get; private set; }
        public byte Blue { get; private set; }
        public bool IsTrueColor { get { return !Aci.HasValue; } }

        public static LayerColor FromAci(short index) { return new LayerColor { Aci = index }; }

        public static LayerColor FromRgb(byte r, byte g, byte b) { return new LayerColor { Red = r, Green = g, Blue = b }; }

        /// <summary>How the standard writes it: "3", "255,128,0".</summary>
        public override string ToString()
        {
            return Aci.HasValue
                ? Aci.Value.ToString(CultureInfo.InvariantCulture)
                : string.Format(CultureInfo.InvariantCulture, "{0},{1},{2}", Red, Green, Blue);
        }

        /// <summary>For people: "3 (green)", "250", "255,128,0".</summary>
        public string Describe()
        {
            if (Aci.HasValue && Aci.Value >= 1 && Aci.Value <= 7) return Aci.Value.ToString(CultureInfo.InvariantCulture) + " (" + Names[Aci.Value - 1] + ")";
            return ToString();
        }

        public bool Equals(LayerColor other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (Aci.HasValue || other.Aci.HasValue) return Aci == other.Aci;
            return Red == other.Red && Green == other.Green && Blue == other.Blue;
        }

        public override bool Equals(object obj) { return Equals(obj as LayerColor); }

        public override int GetHashCode()
        {
            return Aci.HasValue ? Aci.Value : (Red << 16) | (Green << 8) | Blue | (1 << 24);
        }
    }

    /// <summary>One layer as it is in the drawing, for comparing with its standard.</summary>
    public sealed class LayerProperties
    {
        public string Name { get; set; }
        /// <summary>Null when the drawing's colour is one FTF cannot describe (a colour book).</summary>
        public LayerColor Color { get; set; }
        public string Linetype { get; set; }
        /// <summary>Hundredths of a millimetre; -3 is Default.</summary>
        public int LineWeight { get; set; }
        public bool Plot { get; set; }
        /// <summary>Percent, 0 to 90.</summary>
        public int Transparency { get; set; }
        public string Description { get; set; }

        /// <summary>The value as the page shows it beside the standard.</summary>
        public string Text(LayerProperty property)
        {
            switch (property)
            {
                case LayerProperty.Color: return Color == null ? "(colour book)" : Color.Describe();
                case LayerProperty.Linetype: return Linetype ?? string.Empty;
                case LayerProperty.LineWeight: return LayerStandards.FormatLineWeight(LineWeight);
                case LayerProperty.Plot: return Plot ? "Plot" : "No plot";
                case LayerProperty.Transparency: return Transparency.ToString(CultureInfo.InvariantCulture);
                default: return Description ?? string.Empty;
            }
        }

        /// <summary>One line for the table: "3 (green) | Continuous | 0.25 | Plot".</summary>
        public string Summary()
        {
            var parts = new List<string> { Text(LayerProperty.Color), Text(LayerProperty.Linetype), Text(LayerProperty.LineWeight), Text(LayerProperty.Plot) };
            if (Transparency != 0) parts.Add("transp. " + Transparency.ToString(CultureInfo.InvariantCulture));
            return string.Join(" | ", parts.ToArray());
        }
    }

    /// <summary>One property of one drawing layer that differs from its standard.</summary>
    public sealed class LayerPropertyChange
    {
        public string Layer { get; set; }
        public LayerProperty Property { get; set; }
        /// <summary>The drawing's value, for people.</summary>
        public string From { get; set; }
        /// <summary>The standard's value, as written in the standard.</summary>
        public string To { get; set; }
        /// <summary>The standard it comes from: the layer's own name, or a pattern.</summary>
        public string Standard { get; set; }

        public override string ToString()
        {
            return Layer + ": " + LayerStandards.PropertyName(Property) + " " + From + " -> " + To +
                   (string.Equals(Standard, Layer, StringComparison.OrdinalIgnoreCase) ? string.Empty : " (standard " + Standard + ")");
        }
    }

    /// <summary>What FTFLAYERUPDATE would change, before anything is changed.</summary>
    public sealed class LayerUpdatePlan
    {
        public LayerUpdatePlan()
        {
            Changes = new List<LayerPropertyChange>();
            Problems = new List<string>();
        }

        public IList<LayerPropertyChange> Changes { get; private set; }

        /// <summary>Standard values that cannot be applied (an unreadable colour in a hand-edited file); skipped.</summary>
        public IList<string> Problems { get; private set; }

        /// <summary>FTF layers in the drawing that were compared.</summary>
        public int LayersCompared { get; set; }

        /// <summary>FTF layers in the drawing that no standard covers: left as they are.</summary>
        public int LayersWithoutStandard { get; set; }

        public int LayersChanged
        {
            get { return Changes.Select(c => c.Layer).Distinct(StringComparer.OrdinalIgnoreCase).Count(); }
        }
    }

    /// <summary>
    /// The pure rules of layer standards: which standard applies to a layer, whether a value is
    /// one AutoCAD accepts, and what a drawing layer would change to. No Autodesk types.
    /// </summary>
    public static class LayerStandards
    {
        /// <summary>AutoCAD's lineweights, hundredths of a millimetre (the LineWeight enum).</summary>
        public static readonly int[] LineWeights =
        {
            0, 5, 9, 13, 15, 18, 20, 25, 30, 35, 40, 50, 53, 60, 70, 80, 90, 100, 106, 120, 140, 158, 200, 211
        };

        /// <summary>AutoCAD's "Default" lineweight.</summary>
        public const int DefaultLineWeight = -3;

        public static readonly LayerProperty[] Properties =
        {
            LayerProperty.Color, LayerProperty.Linetype, LayerProperty.LineWeight,
            LayerProperty.Plot, LayerProperty.Transparency, LayerProperty.Description
        };

        public static string PropertyName(LayerProperty property)
        {
            switch (property)
            {
                case LayerProperty.Color: return "Color";
                case LayerProperty.Linetype: return "Linetype";
                case LayerProperty.LineWeight: return "Lineweight";
                case LayerProperty.Plot: return "Plot";
                case LayerProperty.Transparency: return "Transparency";
                default: return "Description";
            }
        }

        // ------------------------------------------------------------ matching

        public static bool IsPattern(string name)
        {
            return !string.IsNullOrEmpty(name) && name.IndexOfAny(new[] { '*', '?' }) >= 0;
        }

        /// <summary>True when the standard's name or pattern covers the layer (case-insensitive, as AutoCAD's names are).</summary>
        public static bool Matches(string nameOrPattern, string layer)
        {
            if (string.IsNullOrWhiteSpace(nameOrPattern) || string.IsNullOrWhiteSpace(layer)) return false;
            var pattern = nameOrPattern.Trim();
            if (!IsPattern(pattern)) return string.Equals(pattern, layer.Trim(), StringComparison.OrdinalIgnoreCase);
            var rx = "^" + Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
            return Regex.IsMatch(layer.Trim(), rx, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        /// <summary>How specific a pattern is: its literal characters. "V-UTIL-*-TEXT-E" beats "*-TEXT-E".</summary>
        private static int Specificity(string pattern)
        {
            return (pattern ?? string.Empty).Count(c => c != '*' && c != '?');
        }

        /// <summary>The standards that cover a layer, the one that wins first: the exact name, then
        /// patterns most specific first, then the order they are listed.</summary>
        public static IList<LayerStandard> Candidates(IEnumerable<LayerStandard> standards, string layer)
        {
            var list = (standards ?? Enumerable.Empty<LayerStandard>()).Where(s => s != null).ToList();
            return list.Select((s, i) => new { s, i })
                       .Where(x => Matches(x.s.Name, layer))
                       .OrderBy(x => x.s.IsPattern ? 1 : 0)
                       .ThenByDescending(x => x.s.IsPattern ? Specificity(x.s.Name) : int.MaxValue)
                       .ThenBy(x => x.i)
                       .Select(x => x.s)
                       .ToList();
        }

        /// <summary>The single standard that wins for a layer, or null.</summary>
        public static LayerStandard Find(IEnumerable<LayerStandard> standards, string layer)
        {
            return Candidates(standards, layer).FirstOrDefault();
        }

        /// <summary>The properties in force for a layer: each from the most specific standard that sets
        /// it, so the layer's own standard beats a pattern property by property.</summary>
        public static ResolvedLayerStandard Resolve(IEnumerable<LayerStandard> standards, string layer)
        {
            var resolved = new ResolvedLayerStandard(layer);
            foreach (var s in Candidates(standards, layer))
                foreach (var p in Properties)
                    resolved.Take(s, p);
            return resolved;
        }

        // ------------------------------------------------------------ values

        /// <summary>Reads a layer colour: 1-255, a colour name (red, yellow, green, cyan, blue, magenta,
        /// white), or "r,g,b" (also "RGB:r,g,b"). ByLayer, ByBlock, 0 and 256 are not layer colours.</summary>
        public static bool TryParseColor(string text, out LayerColor color, out string problem)
        {
            color = null;
            problem = null;
            var t = (text ?? string.Empty).Trim();
            if (t.Length == 0) { problem = "no colour given"; return false; }

            if (t.StartsWith("RGB:", StringComparison.OrdinalIgnoreCase)) t = t.Substring(4).Trim();
            if (t.IndexOf(',') >= 0)
            {
                var parts = t.Split(',');
                byte r, g, b;
                if (parts.Length == 3 &&
                    byte.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out r) &&
                    byte.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out g) &&
                    byte.TryParse(parts[2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out b))
                {
                    color = LayerColor.FromRgb(r, g, b);
                    return true;
                }
                problem = "colour \"" + text.Trim() + "\" is not a true colour (three numbers 0-255: 255,128,0)";
                return false;
            }

            if (string.Equals(t, "ByLayer", StringComparison.OrdinalIgnoreCase) || string.Equals(t, "ByBlock", StringComparison.OrdinalIgnoreCase) ||
                t == "0" || t == "256")
            {
                problem = "a layer's colour cannot be ByLayer or ByBlock; use 1-255, a colour name or r,g,b";
                return false;
            }

            var index = LayerAppearance.ColorIndex(t);
            if (index.HasValue && index.Value >= 1 && index.Value <= 255)
            {
                color = LayerColor.FromAci(index.Value);
                return true;
            }
            problem = "colour \"" + text.Trim() + "\" is not an AutoCAD colour (1-255, a colour name, or r,g,b)";
            return false;
        }

        /// <summary>Reads a layer lineweight: "Default", or millimetres ("0.25") or hundredths ("25")
        /// that are one of AutoCAD's lineweights. ByLayer and ByBlock are not layer lineweights.</summary>
        public static bool TryParseLineWeight(string text, out int hundredths, out string problem)
        {
            hundredths = 0;
            problem = null;
            var t = (text ?? string.Empty).Trim();
            if (t.Length == 0) { problem = "no lineweight given"; return false; }
            if (string.Equals(t, "ByLayer", StringComparison.OrdinalIgnoreCase) || string.Equals(t, "ByBlock", StringComparison.OrdinalIgnoreCase))
            {
                problem = "a layer's lineweight cannot be ByLayer or ByBlock; use Default or a width";
                return false;
            }
            var value = LayerAppearance.LineWeightHundredths(t);
            if (value.HasValue && (value.Value == DefaultLineWeight || Array.IndexOf(LineWeights, value.Value) >= 0))
            {
                hundredths = value.Value;
                return true;
            }
            problem = "lineweight \"" + t + "\" is not one of AutoCAD's lineweights (Default, " +
                      string.Join(", ", LineWeights.Select(FormatLineWeight).ToArray()) + " mm)";
            return false;
        }

        /// <summary>"Default", "0.25", "2.11".</summary>
        public static string FormatLineWeight(int hundredths)
        {
            if (hundredths == DefaultLineWeight) return "Default";
            if (hundredths == -1) return "ByLayer";
            if (hundredths == -2) return "ByBlock";
            return (hundredths / 100.0).ToString("0.00", CultureInfo.InvariantCulture);
        }

        /// <summary>Every choice for a lineweight dropdown: Default, then the widths.</summary>
        public static IList<string> LineWeightChoices()
        {
            var list = new List<string> { "Default" };
            list.AddRange(LineWeights.Select(FormatLineWeight));
            return list;
        }

        /// <summary>A linetype name AutoCAD accepts: not blank, no &lt;&gt;/\":;?*|,=`.</summary>
        public static bool IsValidLinetypeName(string name, out string problem)
        {
            problem = null;
            if (string.IsNullOrWhiteSpace(name)) { problem = "a linetype name cannot be blank"; return false; }
            if (name.Trim() != name) { problem = "a linetype name cannot start or end with a space"; return false; }
            if (string.Equals(name, "ByLayer", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "ByBlock", StringComparison.OrdinalIgnoreCase))
            { problem = "a layer's linetype cannot be ByLayer or ByBlock"; return false; }
            if (name.IndexOfAny("<>/\\\":;?*|,=`".ToCharArray()) >= 0) { problem = "linetype \"" + name + "\" has a character AutoCAD does not allow"; return false; }
            return true;
        }

        /// <summary>What is wrong with one standard, in words; empty when it is valid.</summary>
        public static IList<string> Problems(LayerStandard s)
        {
            var problems = new List<string>();
            if (s == null) return problems;
            var name = (s.Name ?? string.Empty).Trim();
            string why;
            if (name.Length == 0) problems.Add("a layer standard needs a layer name or a pattern");
            else if (!LayerMap.IsValidName(name.Replace('*', 'X').Replace('?', 'X'), out why)) problems.Add(why);

            LayerColor colour;
            if (s.Color != null && s.Color.Trim().Length > 0 && !TryParseColor(s.Color, out colour, out why)) problems.Add(why);
            if (s.Linetype != null && s.Linetype.Length > 0 && !IsValidLinetypeName(s.Linetype, out why)) problems.Add(why);
            int weight;
            if (s.LineWeight != null && s.LineWeight.Trim().Length > 0 && !TryParseLineWeight(s.LineWeight, out weight, out why)) problems.Add(why);
            if (s.Transparency.HasValue && (s.Transparency.Value < 0 || s.Transparency.Value > 90))
                problems.Add("transparency must be 0 to 90 percent");
            return problems;
        }

        /// <summary>A standard that says exactly what a drawing layer has now ("Copy from Drawing"):
        /// colour, linetype, lineweight, plot and transparency, and the description when there is one.</summary>
        public static LayerStandard FromDrawing(LayerProperties layer)
        {
            if (layer == null) return null;
            return new LayerStandard
            {
                Name = layer.Name,
                Color = layer.Color == null ? null : layer.Color.ToString(),
                Linetype = string.IsNullOrWhiteSpace(layer.Linetype) ? null : layer.Linetype.Trim(),
                LineWeight = FormatLineWeight(layer.LineWeight),
                Plot = layer.Plot,
                Transparency = layer.Transparency,
                Description = string.IsNullOrWhiteSpace(layer.Description) ? null : layer.Description.Trim()
            };
        }

        // ------------------------------------------------------------ comparing

        /// <summary>The properties of one drawing layer that differ from its standard. A property the
        /// standard does not set, or sets to something unreadable, is not a change.</summary>
        public static IList<LayerPropertyChange> Differences(LayerProperties drawing, ResolvedLayerStandard standard, ICollection<string> problems = null)
        {
            var changes = new List<LayerPropertyChange>();
            if (drawing == null || standard == null || standard.IsEmpty) return changes;
            var s = standard.Values;
            string why;

            foreach (var p in Properties)
            {
                if (!s.Sets(p)) continue;
                var differs = false;
                switch (p)
                {
                    case LayerProperty.Color:
                        LayerColor colour;
                        if (!TryParseColor(s.Color, out colour, out why)) { Report(problems, drawing.Name, why); continue; }
                        differs = !colour.Equals(drawing.Color);
                        break;
                    case LayerProperty.Linetype:
                        if (!IsValidLinetypeName(s.Linetype.Trim(), out why)) { Report(problems, drawing.Name, why); continue; }
                        differs = !string.Equals(s.Linetype.Trim(), (drawing.Linetype ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);
                        break;
                    case LayerProperty.LineWeight:
                        int weight;
                        if (!TryParseLineWeight(s.LineWeight, out weight, out why)) { Report(problems, drawing.Name, why); continue; }
                        differs = weight != drawing.LineWeight;
                        break;
                    case LayerProperty.Plot:
                        differs = s.Plot.Value != drawing.Plot;
                        break;
                    case LayerProperty.Transparency:
                        if (s.Transparency.Value < 0 || s.Transparency.Value > 90) { Report(problems, drawing.Name, "transparency must be 0 to 90 percent"); continue; }
                        differs = s.Transparency.Value != drawing.Transparency;
                        break;
                    default:
                        differs = !string.Equals(s.Description.Trim(), (drawing.Description ?? string.Empty).Trim(), StringComparison.Ordinal);
                        break;
                }
                if (!differs) continue;
                changes.Add(new LayerPropertyChange
                {
                    Layer = drawing.Name,
                    Property = p,
                    From = drawing.Text(p),
                    To = Display(s, p),
                    Standard = standard.Source(p)
                });
            }
            return changes;
        }

        /// <summary>The standard's value for people, normalised the way the drawing value is shown.</summary>
        public static string Display(LayerStandard s, LayerProperty p)
        {
            LayerColor colour;
            int weight;
            string why;
            if (p == LayerProperty.Color && TryParseColor(s.Color, out colour, out why)) return colour.Describe();
            if (p == LayerProperty.LineWeight && TryParseLineWeight(s.LineWeight, out weight, out why)) return FormatLineWeight(weight);
            return s.Text(p);
        }

        private static void Report(ICollection<string> problems, string layer, string why)
        {
            if (problems != null) problems.Add(layer + ": " + why + " -- not applied");
        }

        /// <summary>
        /// FTFLAYERUPDATE's preview: for each layer FTF uses that is in the drawing, the properties
        /// that differ from its standard. Layers FTF does not use are never in the plan, and nor are
        /// xref layers (NAME|LAYER), which belong to their own drawing.
        /// </summary>
        public static LayerUpdatePlan PlanUpdate(IEnumerable<string> ftfLayers, IEnumerable<LayerProperties> drawingLayers,
                                                 IEnumerable<LayerStandard> standards)
        {
            var plan = new LayerUpdatePlan();
            var used = new HashSet<string>((ftfLayers ?? Enumerable.Empty<string>()).Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l.Trim()),
                                           StringComparer.OrdinalIgnoreCase);
            var list = (standards ?? Enumerable.Empty<LayerStandard>()).Where(s => s != null).ToList();
            var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var layer in (drawingLayers ?? Enumerable.Empty<LayerProperties>())
                                  .Where(l => l != null && !string.IsNullOrWhiteSpace(l.Name))
                                  .OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (!used.Contains(layer.Name) || layer.Name.IndexOf('|') >= 0 || !done.Add(layer.Name)) continue;
                plan.LayersCompared++;
                var resolved = Resolve(list, layer.Name);
                if (resolved.IsEmpty) { plan.LayersWithoutStandard++; continue; }
                foreach (var change in Differences(layer, resolved, plan.Problems))
                    plan.Changes.Add(change);
            }
            return plan;
        }
    }
}
