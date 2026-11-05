using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace FieldCodes.Editing
{
    /// <summary>The rules file's wetlandBuffer section (FTFWETLANDBUFFER) as Settings > Wetland
    /// Buffers shows it.</summary>
    public sealed class EditableWetlandBuffer
    {
        /// <summary>The layer the buffer is drawn on.</summary>
        public string Layer { get; set; }

        /// <summary>The buffer distance the command offers first, in drawing units.</summary>
        public double Distance { get; set; }

        /// <summary>The smoothing radius for inside corners the command offers first (0 = none).</summary>
        public double Smoothing { get; set; }

        /// <summary>Whether the command's trim prompt defaults to Yes.</summary>
        public bool TrimAtHardSurfaces { get; set; }

        /// <summary>Layers whose lines are hard surfaces, in order; * and ? as wildcards.</summary>
        public List<string> HardSurfaceLayers { get; set; }

        /// <summary>True when the rules file has its own wetlandBuffer section; false means these
        /// are the built-in values, which the first save writes into the file.</summary>
        public bool InFile { get; set; }

        public static EditableWetlandBuffer Defaults()
        {
            var d = new WetlandBufferRule();
            return new EditableWetlandBuffer
            {
                Layer = d.Layer, Distance = d.Distance, Smoothing = d.Smoothing,
                TrimAtHardSurfaces = d.TrimAtHardSurfaces, HardSurfaceLayers = d.HardSurfaceLayers.ToList()
            };
        }

        /// <summary>The layer list as it is written: trimmed, blanks dropped, each once (ignoring
        /// case), in the order given.</summary>
        public static List<string> CleanLayers(IEnumerable<string> layers)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<string>();
            foreach (var l in layers ?? Enumerable.Empty<string>())
            {
                var t = (l ?? string.Empty).Trim();
                if (t.Length > 0 && seen.Add(t)) result.Add(t);
            }
            return result;
        }
    }

    public sealed partial class RuleDocument
    {
        private const string WetlandBufferKey = "wetlandBuffer";

        /// <summary>The wetlandBuffer section, with the built-in value for anything it leaves out.</summary>
        public EditableWetlandBuffer GetWetlandBuffer()
        {
            var result = EditableWetlandBuffer.Defaults();
            var o = _root[WetlandBufferKey] as JObject;
            if (o == null) return result;
            result.InFile = true;

            var layer = o["layer"];
            if (layer != null && layer.Type == JTokenType.String) result.Layer = (string)layer;
            double number;
            if (TryNumber(o["distance"], out number)) result.Distance = number;
            if (TryNumber(o["smoothing"], out number)) result.Smoothing = number;
            var trim = o["trimAtHardSurfaces"];
            if (trim != null && trim.Type == JTokenType.Boolean) result.TrimAtHardSurfaces = (bool)trim;
            var layers = o["hardSurfaceLayers"] as JArray;
            if (layers != null)
                result.HardSurfaceLayers = layers.Where(t => t.Type == JTokenType.String).Select(t => (string)t).ToList();
            return result;
        }

        /// <summary>
        /// Writes what differs from the file into the wetlandBuffer section, creating it the first
        /// time. Nothing else in the file (comments, other sections, properties of this section the
        /// editor does not know) is touched.
        /// </summary>
        public void ApplyWetlandBuffer(EditableWetlandBuffer edit)
        {
            if (edit == null) throw new ArgumentNullException("edit");
            var current = GetWetlandBuffer();
            var layer = (edit.Layer ?? string.Empty).Trim();
            if (!current.InFile || !string.Equals(current.Layer, layer, StringComparison.Ordinal)) WetlandBufferSection()["layer"] = layer;
            if (!current.InFile || Math.Abs(current.Distance - edit.Distance) > 1e-12) WetlandBufferSection()["distance"] = edit.Distance;
            if (!current.InFile || Math.Abs(current.Smoothing - edit.Smoothing) > 1e-12) WetlandBufferSection()["smoothing"] = edit.Smoothing;
            if (!current.InFile || current.TrimAtHardSurfaces != edit.TrimAtHardSurfaces) WetlandBufferSection()["trimAtHardSurfaces"] = edit.TrimAtHardSurfaces;
            var layers = EditableWetlandBuffer.CleanLayers(edit.HardSurfaceLayers);
            if (!current.InFile || !layers.SequenceEqual(current.HardSurfaceLayers ?? new List<string>(), StringComparer.Ordinal))
                WetlandBufferSection()["hardSurfaceLayers"] = new JArray(layers.Cast<object>().ToArray());
        }

        /// <summary>Puts this document's wetland buffer settings onto another (Save re-reads the file
        /// first, so another page's save in between is not lost).</summary>
        public void CopyWetlandBufferTo(RuleDocument other)
        {
            if (other == null) throw new ArgumentNullException("other");
            if (!(_root[WetlandBufferKey] is JObject)) return;
            other.ApplyWetlandBuffer(GetWetlandBuffer());
        }

        private JObject WetlandBufferSection()
        {
            var o = _root[WetlandBufferKey] as JObject;
            if (o == null)
            {
                o = new JObject();
                _root[WetlandBufferKey] = o;
            }
            return o;
        }

        /// <summary>What the page cannot save: checked on the document, so each problem names its field.</summary>
        private void WetlandBufferProblems(List<string> problems)
        {
            var o = _root[WetlandBufferKey] as JObject;
            if (o == null) return;
            const string page = "Wetland Buffers: ";
            var w = GetWetlandBuffer();

            var layerToken = o["layer"];
            if (layerToken != null && layerToken.Type != JTokenType.String) problems.Add(page + "the buffer layer must be text.");
            string why;
            if ((w.Layer ?? string.Empty).IndexOf('*') >= 0 || (w.Layer ?? string.Empty).IndexOf('?') >= 0)
                problems.Add(page + "the buffer layer is one layer, not a pattern.");
            else if (!FieldCodes.Settings.LayerMap.IsValidName(w.Layer, out why))
                problems.Add(page + "the buffer layer \"" + w.Layer + "\" cannot be used: " + why + ".");

            if (o["distance"] != null && !TryNumber(o["distance"], out _)) problems.Add(page + "the distance must be a number.");
            else if (!(w.Distance > 0)) problems.Add(page + "the distance must be greater than zero.");
            if (o["smoothing"] != null && !TryNumber(o["smoothing"], out _)) problems.Add(page + "the smoothing radius must be a number.");
            else if (w.Smoothing < 0) problems.Add(page + "the smoothing radius cannot be negative.");
            var trim = o["trimAtHardSurfaces"];
            if (trim != null && trim.Type != JTokenType.Boolean) problems.Add(page + "\"cut back at hard surfaces\" must be true or false.");

            var layers = o["hardSurfaceLayers"];
            if (layers != null && !(layers is JArray)) { problems.Add(page + "the hard-surface layers must be a list."); return; }
            if (layers is JArray && ((JArray)layers).Any(t => t.Type != JTokenType.String))
                problems.Add(page + "every hard-surface layer must be text.");
            foreach (var pattern in w.HardSurfaceLayers ?? new List<string>())
            {
                if (string.IsNullOrWhiteSpace(pattern)) { problems.Add(page + "a hard-surface layer is blank."); continue; }
                // * and ? are the wildcards; everything else must be a layer name AutoCAD accepts.
                var bare = pattern.Trim().Replace("*", "A").Replace("?", "A");
                if (!FieldCodes.Settings.LayerMap.IsValidName(bare, out why))
                    problems.Add(string.Format(CultureInfo.InvariantCulture, "{0}hard-surface layer \"{1}\" cannot be used: {2}.", page, pattern, why));
            }
            var duplicates = (w.HardSurfaceLayers ?? new List<string>()).Where(p => !string.IsNullOrWhiteSpace(p))
                .GroupBy(p => p.Trim(), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            if (duplicates.Count > 0) problems.Add(page + "listed twice: " + string.Join(", ", duplicates.ToArray()) + ".");
        }
    }
}
