using System;
using System.Collections.Generic;
using System.Linq;
using FieldCodes.Linework;
using FieldCodes.Settings;

namespace FieldCodes.RecordSurvey
{
    /// <summary>The named resources the open drawing actually has. Filled by the CAD layer; built by hand in tests.</summary>
    public sealed class DrawingInventory
    {
        public List<string> Layers { get; set; }
        public List<string> TextStyles { get; set; }
        public List<string> Linetypes { get; set; }
        public List<string> Blocks { get; set; }
        public List<string> LineLabelStyles { get; set; }
        public List<string> CurveLabelStyles { get; set; }
        public List<string> TableStyles { get; set; }
        public List<string> DimensionStyles { get; set; }
        /// <summary>Drawing units per plotted unit at the annotation scale.</summary>
        public double Scale { get; set; }
        public string CurrentTextStyle { get; set; }

        public DrawingInventory()
        {
            Layers = new List<string>(); TextStyles = new List<string>(); Linetypes = new List<string>(); Blocks = new List<string>();
            LineLabelStyles = new List<string>(); CurveLabelStyles = new List<string>(); TableStyles = new List<string>(); DimensionStyles = new List<string>();
            Scale = 1.0;
            CurrentTextStyle = "Standard";
        }

        public bool HasLayer(string name) { return Has(Layers, name); }
        public bool HasTextStyle(string name) { return Has(TextStyles, name); }
        public bool HasLinetype(string name) { return Has(Linetypes, name); }
        public bool HasBlock(string name) { return Has(Blocks, name); }
        public bool HasLineLabelStyle(string name) { return Has(LineLabelStyles, name); }
        public bool HasCurveLabelStyle(string name) { return Has(CurveLabelStyles, name); }
        public bool HasTableStyle(string name) { return Has(TableStyles, name); }

        /// <summary>The drawing's spelling of the name, or null.</summary>
        public static string Exact(IEnumerable<string> names, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            return (names ?? Enumerable.Empty<string>()).FirstOrDefault(n => string.Equals(n, name.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        private static bool Has(IEnumerable<string> names, string name) { return Exact(names, name) != null; }
    }

    /// <summary>One thing the standard asks for that the drawing does not have, or that had to fall back.</summary>
    public sealed class StandardsIssue
    {
        public string Entity { get; set; }
        /// <summary>layer, label layer, text style, line label style, curve label style, block, linetype, table style.</summary>
        public string Resource { get; set; }
        public string Name { get; set; }
        /// <summary>"Missing" (withheld) or "Fallback" (drawn another way, visibly).</summary>
        public string Severity { get; set; }
        public string Effect { get; set; }
        public List<string> Candidates { get; private set; }

        public StandardsIssue() { Candidates = new List<string>(); }

        public override string ToString()
        {
            return Severity + ": " + Entity + " " + Resource + " \"" + Name + "\" -- " + Effect +
                   (Candidates.Count > 0 ? " Similar: " + string.Join(", ", Candidates.ToArray()) : string.Empty);
        }
    }

    /// <summary>The standard for one entity type, settled against the drawing.</summary>
    public sealed class ResolvedEntityStandard
    {
        public RecordEntityStandard Standard { get; set; }
        public string Layer { get; set; }
        /// <summary>Existing, Mapped, SameNameDifferentSpelling, Create, or Missing.</summary>
        public string LayerDecision { get; set; }
        public string Linetype { get; set; }
        public string LabelLayer { get; set; }
        public string LabelLayerSource { get; set; }
        public string TextStyle { get; set; }
        public string LineLabelStyle { get; set; }
        public string CurveLabelStyle { get; set; }
        public double TextHeight { get; set; }
        /// <summary>The geometry can be drawn.</summary>
        public bool CanDraw { get; set; }
        /// <summary>Plain-text labels can be placed.</summary>
        public bool CanLabelText { get; set; }
        /// <summary>Civil 3D line labels can be placed.</summary>
        public bool CanLabelLinesCivil { get; set; }
        public bool CanLabelCurvesCivil { get; set; }
        public List<StandardsIssue> Issues { get; private set; }
        internal string LineLabelStyleField;
        internal string CurveLabelStyleField;

        public ResolvedEntityStandard() { Issues = new List<StandardsIssue>(); }
    }

    public sealed class ResolvedMonumentStandard
    {
        public MonumentStandard Standard { get; set; }
        public string Block { get; set; }
        public string Layer { get; set; }
        public string LabelLayer { get; set; }
        public bool CanDraw { get; set; }
        public List<StandardsIssue> Issues { get; private set; }
        public ResolvedMonumentStandard() { Issues = new List<StandardsIssue>(); }
    }

    public sealed class StandardsResolution
    {
        public Dictionary<string, ResolvedEntityStandard> Entities { get; private set; }
        public Dictionary<string, ResolvedMonumentStandard> Monuments { get; private set; }
        public List<StandardsIssue> Issues { get; private set; }
        public string TableLayer { get; set; }
        public string TableStyle { get; set; }
        public bool CanDrawTable { get; set; }
        public string LotTextLayer { get; set; }

        public StandardsResolution()
        {
            Entities = new Dictionary<string, ResolvedEntityStandard>(StringComparer.OrdinalIgnoreCase);
            Monuments = new Dictionary<string, ResolvedMonumentStandard>(StringComparer.OrdinalIgnoreCase);
            Issues = new List<StandardsIssue>();
        }

        public ResolvedEntityStandard For(string objectType)
        {
            ResolvedEntityStandard r;
            return Entities.TryGetValue(objectType ?? string.Empty, out r) ? r : null;
        }

        public bool AllMissingResolved { get { return !Issues.Any(i => i.Severity == "Missing"); } }
        public int MissingCount { get { return Issues.Count(i => i.Severity == "Missing"); } }
    }

    /// <summary>
    /// Maps the survey entity standards onto what the drawing actually contains. The drawing
    /// is the authority: a configured layer, style or block it does not have is reported as
    /// missing and whatever needed it is withheld -- never replaced by a look-alike or an
    /// invented name. Pure; the CAD layer only supplies the inventory.
    /// </summary>
    public static class StandardsResolver
    {
        public static StandardsResolution Resolve(RecordSurveySettings settings, DrawingInventory inventory,
                                                  IEnumerable<LayerMapping> mappings, IEnumerable<string> neededObjectTypes)
        {
            var result = new StandardsResolution();
            settings = settings ?? new RecordSurveySettings();
            inventory = inventory ?? new DrawingInventory();
            var needed = new HashSet<string>(neededObjectTypes ?? settings.Entities.Select(e => e.Name), StringComparer.OrdinalIgnoreCase);
            var catalog = LabelLayerResolver.BuildCatalog(inventory.Layers);

            foreach (var standard in settings.Entities.Where(e => e != null && !string.IsNullOrWhiteSpace(e.Name)))
            {
                var r = new ResolvedEntityStandard { Standard = standard, TextHeight = standard.TextHeightPlotted * inventory.Scale };
                result.Entities[standard.Name] = r;
                // A standard no call needs is not checked against the drawing, so it is not drawable
                // either: a course given that type later (in the review) must be resolved again first.
                if (!needed.Contains(standard.Name)) { r.CanDraw = false; r.LayerDecision = "NotResolved"; continue; }

                // ---- layer
                if (string.IsNullOrWhiteSpace(standard.Layer))
                {
                    r.LayerDecision = "Missing";
                    r.Issues.Add(new StandardsIssue { Entity = standard.Name, Resource = "layer", Name = "(none configured)", Severity = "Missing", Effect = "No layer is configured for " + standard.Name + " courses, so they are not drawn. Set one under Settings > Recorded Surveys from this drawing's layer list." });
                }
                else
                {
                    var layer = ProductionLayerResolver.Resolve(standard.Layer, inventory.Layers, mappings);
                    r.LayerDecision = layer.Decision.ToString();
                    switch (layer.Decision)
                    {
                        case LayerDecision.Existing:
                        case LayerDecision.Mapped:
                        case LayerDecision.SameNameDifferentSpelling:
                            r.Layer = layer.Layer;
                            if (layer.Decision != LayerDecision.Existing)
                                r.Issues.Add(new StandardsIssue { Entity = standard.Name, Resource = "layer", Name = standard.Layer, Severity = "Fallback", Effect = "Drawn on the drawing's layer " + layer.Layer + " (" + (layer.Decision == LayerDecision.Mapped ? "project layer mapping" : "same name, different spelling") + ")." });
                            break;
                        default:
                            if (settings.CreateMissingLayers)
                            {
                                r.Layer = layer.Layer;
                                r.LayerDecision = "Create";
                                var issue = new StandardsIssue { Entity = standard.Name, Resource = "layer", Name = standard.Layer, Severity = "Fallback", Effect = "The layer is not in the drawing and will be CREATED (Settings > Recorded Surveys > create missing layers is on)." };
                                issue.Candidates.AddRange(layer.Similar);
                                r.Issues.Add(issue);
                            }
                            else
                            {
                                r.LayerDecision = "Missing";
                                var issue = new StandardsIssue { Entity = standard.Name, Resource = "layer", Name = standard.Layer, Severity = "Missing", Effect = standard.Name + " courses are not drawn until the layer exists in the drawing or the standard points at one that does." };
                                issue.Candidates.AddRange(layer.Similar);
                                r.Issues.Add(issue);
                            }
                            break;
                    }
                }
                r.CanDraw = standard.Enabled && !string.IsNullOrEmpty(r.Layer);

                // ---- linetype: cosmetic, so a missing one falls back to ByLayer, visibly.
                if (!string.IsNullOrWhiteSpace(standard.Linetype))
                {
                    var lt = DrawingInventory.Exact(inventory.Linetypes, standard.Linetype);
                    if (lt != null) r.Linetype = lt;
                    else r.Issues.Add(new StandardsIssue { Entity = standard.Name, Resource = "linetype", Name = standard.Linetype, Severity = "Fallback", Effect = "Linetype not loaded; courses are drawn with the layer's linetype (ByLayer)." });
                }

                if (!standard.Label || !settings.LabelsEnabled) continue;

                // ---- label layer: configured must exist; empty derives from the line layer's family.
                if (!string.IsNullOrWhiteSpace(standard.LabelLayer))
                {
                    var ll = DrawingInventory.Exact(inventory.Layers, standard.LabelLayer);
                    if (ll != null) { r.LabelLayer = ll; r.LabelLayerSource = "configured"; }
                    else r.Issues.Add(new StandardsIssue { Entity = standard.Name, Resource = "label layer", Name = standard.LabelLayer, Severity = "Missing", Effect = "Labels for " + standard.Name + " courses are withheld until the label layer exists." });
                }
                else if (!string.IsNullOrEmpty(r.Layer))
                {
                    var res = LabelLayerResolver.Resolve(r.Layer, null, catalog, r.Layer);
                    r.LabelLayer = res.Layer;
                    r.LabelLayerSource = res.Source.ToString();
                    if (res.Source == LabelLayerSource.Default)
                        r.Issues.Add(new StandardsIssue { Entity = standard.Name, Resource = "label layer", Name = "(derived)", Severity = "Fallback", Effect = "No office text layer exists in the " + r.Layer + " family; labels go on the course's own layer." });
                }

                // ---- text style: configured must exist; empty uses the drawing's current style (reported, not hidden).
                var styleName = !string.IsNullOrWhiteSpace(standard.TextStyle) ? standard.TextStyle : settings.TextStyle;
                if (!string.IsNullOrWhiteSpace(styleName))
                {
                    var ts = DrawingInventory.Exact(inventory.TextStyles, styleName);
                    if (ts != null) r.TextStyle = ts;
                    else r.Issues.Add(new StandardsIssue { Entity = standard.Name, Resource = "text style", Name = styleName, Severity = "Missing", Effect = "Plain-text labels for " + standard.Name + " courses are withheld until the text style exists." });
                }
                else
                {
                    r.TextStyle = inventory.CurrentTextStyle;
                    r.Issues.Add(new StandardsIssue { Entity = standard.Name, Resource = "text style", Name = "(none configured)", Severity = "Fallback", Effect = "No text style is configured; the drawing's current style (" + inventory.CurrentTextStyle + ") is used." });
                }
                r.CanLabelText = !string.IsNullOrEmpty(r.LabelLayer) && !string.IsNullOrEmpty(r.TextStyle);

                // ---- Civil 3D label styles: configured and present, or withheld / plain text per the setting.
                if (settings.UseCivil3DLabels)
                {
                    ResolveCivilStyle(settings, inventory.LineLabelStyles, standard.LineLabelStyle, standard.Name, "line label style", r, out r.LineLabelStyleField);
                    ResolveCivilStyle(settings, inventory.CurveLabelStyles, standard.CurveLabelStyle, standard.Name, "curve label style", r, out r.CurveLabelStyleField);
                    r.LineLabelStyle = r.LineLabelStyleField;
                    r.CurveLabelStyle = r.CurveLabelStyleField;
                    r.CanLabelLinesCivil = !string.IsNullOrEmpty(r.LineLabelStyle) && !string.IsNullOrEmpty(r.LabelLayer);
                    r.CanLabelCurvesCivil = !string.IsNullOrEmpty(r.CurveLabelStyle) && !string.IsNullOrEmpty(r.LabelLayer);
                    // A missing Civil 3D style withholds plain text too unless the setting allows it.
                    if (!r.CanLabelLinesCivil && !settings.PlainTextWhenStyleMissing && !string.IsNullOrWhiteSpace(standard.LineLabelStyle)) r.CanLabelText = false;
                }
            }

            // ---- monuments
            foreach (var m in settings.Monuments.Where(x => x != null && !string.IsNullOrWhiteSpace(x.Status)))
            {
                var r = new ResolvedMonumentStandard { Standard = m };
                result.Monuments[m.Status] = r;
                if (!settings.DrawMonuments) continue;
                var layer = DrawingInventory.Exact(inventory.Layers, m.Layer);
                if (layer != null) r.Layer = layer;
                else r.Issues.Add(new StandardsIssue { Entity = "Monument (" + m.Status + ")", Resource = "layer", Name = m.Layer ?? string.Empty, Severity = "Missing", Effect = m.Status + " monuments are not drawn until the layer exists." });
                if (string.IsNullOrWhiteSpace(m.Block))
                    r.Issues.Add(new StandardsIssue { Entity = "Monument (" + m.Status + ")", Resource = "block", Name = "(none configured)", Severity = "Missing", Effect = "No monument block is configured for " + m.Status.ToLowerInvariant() + " monuments; they are listed but not drawn. Choose the office block under Settings > Recorded Surveys." });
                else
                {
                    var block = DrawingInventory.Exact(inventory.Blocks, m.Block);
                    if (block != null) r.Block = block;
                    else r.Issues.Add(new StandardsIssue { Entity = "Monument (" + m.Status + ")", Resource = "block", Name = m.Block, Severity = "Missing", Effect = "The block is not in the drawing; " + m.Status.ToLowerInvariant() + " monuments are not drawn. Insert the office symbol library or point the standard at an existing block." });
                }
                r.LabelLayer = DrawingInventory.Exact(inventory.Layers, m.LabelLayer) ?? r.Layer;
                r.CanDraw = !string.IsNullOrEmpty(r.Layer) && !string.IsNullOrEmpty(r.Block);
            }

            // ---- table and lot text
            var tableLayer = DrawingInventory.Exact(inventory.Layers, settings.TableLayer);
            result.TableLayer = tableLayer;
            if (settings.LabelsEnabled && (settings.LabelTableMode || settings.LabelAutoMode) && tableLayer == null)
                result.Issues.Add(new StandardsIssue { Entity = "Line/curve table", Resource = "layer", Name = settings.TableLayer ?? string.Empty, Severity = "Missing", Effect = "The table layer is not in the drawing; courses that would go in a table are labelled on the line instead when they fit, otherwise withheld." });
            if (!string.IsNullOrWhiteSpace(settings.TableStyle))
            {
                result.TableStyle = DrawingInventory.Exact(inventory.TableStyles, settings.TableStyle);
                if (result.TableStyle == null)
                    result.Issues.Add(new StandardsIssue { Entity = "Line/curve table", Resource = "table style", Name = settings.TableStyle, Severity = "Fallback", Effect = "Table style not in the drawing; the current table style is used." });
            }
            result.CanDrawTable = tableLayer != null;
            result.LotTextLayer = DrawingInventory.Exact(inventory.Layers, settings.LotTextLayer);

            foreach (var e in result.Entities.Values) result.Issues.AddRange(e.Issues);
            foreach (var m in result.Monuments.Values) result.Issues.AddRange(m.Issues);
            return result;
        }

        private static void ResolveCivilStyle(RecordSurveySettings settings, IList<string> available, string configured, string entity,
                                              string resource, ResolvedEntityStandard r, out string resolved)
        {
            resolved = null;
            if (string.IsNullOrWhiteSpace(configured))
            {
                r.Issues.Add(new StandardsIssue { Entity = entity, Resource = resource, Name = "(none configured)", Severity = settings.PlainTextWhenStyleMissing ? "Fallback" : "Missing",
                    Effect = settings.PlainTextWhenStyleMissing ? "No Civil 3D " + resource + " is configured; plain text labels are placed instead." : "No Civil 3D " + resource + " is configured for " + entity + "; its labels are withheld. Choose the office style under Settings > Recorded Surveys, or set 'when a label style is missing' to PlainText." });
                return;
            }
            var found = DrawingInventory.Exact(available, configured);
            if (found != null) { resolved = found; return; }
            r.Issues.Add(new StandardsIssue { Entity = entity, Resource = resource, Name = configured, Severity = settings.PlainTextWhenStyleMissing ? "Fallback" : "Missing",
                Effect = settings.PlainTextWhenStyleMissing ? "The Civil 3D " + resource + " is not in the drawing; plain text labels are placed instead." : "The Civil 3D " + resource + " is not in the drawing; " + entity + " labels are withheld rather than placed in another style." });
        }
    }
}
