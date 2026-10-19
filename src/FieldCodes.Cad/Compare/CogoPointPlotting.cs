using System;
using System.Reflection;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.Civil;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using Autodesk.Civil.DatabaseServices.Styles;

namespace FieldCodes.Cad.Compare
{
    /// <summary>
    /// What a COGO point actually draws: its marker through the point style (whose
    /// display layer may be fixed rather than "0 = the point's layer"), and its label.
    /// Moving the point to a nonplot layer is enough only when the marker follows the
    /// point's layer; otherwise the point gets the comparison's own nonplot point style,
    /// and the label is hidden on the point itself. A point group that overrides the
    /// point style wins over the point, so such a point is refused rather than changed.
    ///
    /// Point-group override properties are read by reflection: their names have moved
    /// between releases, and a missing member must mean "could not determine", never a
    /// plugin that fails to load. UNTESTED in Civil 3D.
    /// </summary>
    internal static class CogoPointPlotting
    {
        public const string NonplotStyleName = "PMX-COMP-NP";

        public sealed class Effective
        {
            public bool StyleOverriddenByGroup;
            public string GroupName;
            public string MarkerLayer;
            public bool MarkerVisible = true;
            public bool MarkerKnown;
            public ObjectId StyleId;
            public string Problem;
        }

        /// <summary>The style that really draws the marker, and where it puts it.</summary>
        public static Effective Resolve(CogoPoint point, Database db, Transaction tr)
        {
            var e = new Effective();
            try
            {
                e.StyleId = point.StyleId;

                var groupId = ReadProperty(point, "PrimaryPointGroupId");
                if (groupId is ObjectId && !((ObjectId)groupId).IsNull)
                {
                    var group = tr.GetObject((ObjectId)groupId, OpenMode.ForRead);
                    var overridden = ReadProperty(group, "IsPointStyleOverridden");
                    if (overridden == null)
                    {
                        e.Problem = "point group style override could not be determined (API member not found)";
                        return e;
                    }
                    if (overridden is bool && (bool)overridden)
                    {
                        e.StyleOverriddenByGroup = true;
                        e.GroupName = Convert.ToString(ReadProperty(group, "Name"));
                        var groupStyle = ReadProperty(group, "PointStyleId");
                        if (groupStyle is ObjectId && !((ObjectId)groupStyle).IsNull) e.StyleId = (ObjectId)groupStyle;
                    }
                }

                if (e.StyleId.IsNull) { e.Problem = "the point has no point style"; return e; }
                var style = tr.GetObject(e.StyleId, OpenMode.ForRead) as PointStyle;
                if (style == null) { e.Problem = "the point style could not be opened"; return e; }
                var marker = style.GetDisplayStylePlan(PointDisplayStyleType.Marker);
                e.MarkerLayer = marker.Layer;
                e.MarkerVisible = marker.Visible;
                e.MarkerKnown = true;
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex) { e.Problem = ex.Message; }
            catch (TargetInvocationException ex) { e.Problem = ex.InnerException != null ? ex.InnerException.Message : ex.Message; }
            return e;
        }

        /// <summary>Does the point plot: its marker on the layer its style puts it on, or
        /// its label (taken to sit on the point's layer; a label style with its own fixed
        /// layer is why nonplot points get their label hidden rather than relocated).</summary>
        public static PlotVisibility.Verdict Plots(CogoPoint point, Database db, Transaction tr, Func<string, bool> layerPlots)
        {
            if (!point.Visible) return PlotVisibility.Verdict.No("invisible");
            var e = Resolve(point, db, tr);
            if (e.Problem != null) return PlotVisibility.Verdict.Unknown(e.Problem);

            var markerLayer = string.IsNullOrEmpty(e.MarkerLayer) || e.MarkerLayer == "0" ? point.Layer : e.MarkerLayer;
            if (e.MarkerVisible && layerPlots(markerLayer))
                return PlotVisibility.Verdict.Yes("marker on layer " + markerLayer + (markerLayer == point.Layer ? string.Empty : " (fixed by the point style)") +
                                                  (e.StyleOverriddenByGroup ? ", style from point group " + e.GroupName : string.Empty));
            if (point.IsLabelVisible)
            {
                if (layerPlots(point.Layer)) return PlotVisibility.Verdict.Yes("label visible on layer " + point.Layer);
                return PlotVisibility.Verdict.Unknown("label visible; its style's own layer was not checked");
            }
            return PlotVisibility.Verdict.No("marker on " + markerLayer + " does not plot; label hidden");
        }

        /// <summary>The comparison's own point style: marker and label on the nonplot layer.</summary>
        public static ObjectId EnsureNonplotStyle(Database db, Transaction tr, string layer)
        {
            var civil = CivilDocument.GetCivilDocument(db);
            var styles = civil.Styles.PointStyles;
            var id = styles.Contains(NonplotStyleName) ? styles[NonplotStyleName] : styles.Add(NonplotStyleName);
            var style = (PointStyle)tr.GetObject(id, OpenMode.ForWrite);
            style.MarkerType = PointMarkerDisplayType.UseCustomMarker;
            foreach (var display in new[]
            {
                style.GetDisplayStylePlan(PointDisplayStyleType.Marker), style.GetDisplayStyleModel(PointDisplayStyleType.Marker),
                style.GetDisplayStylePlan(PointDisplayStyleType.Label), style.GetDisplayStyleModel(PointDisplayStyleType.Label)
            })
            {
                display.Layer = layer;
                display.Visible = true;
            }
            return id;
        }

        private static object ReadProperty(object target, string name)
        {
            if (target == null) return null;
            var property = target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            if (property == null) return null;
            return property.GetValue(target, null);
        }
    }
}
