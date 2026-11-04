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

        /// <summary>What FTF could read of a point's own Civil 3D label.</summary>
        public sealed class LabelRead
        {
            /// <summary>True: shown. False: hidden (the reason says by what). Null: not read.</summary>
            public bool? Shown;
            /// <summary>The label style's name; null when not read.</summary>
            public string StyleName;
            /// <summary>Why it is hidden, or what could not be read.</summary>
            public string Problem;
        }

        /// <summary>
        /// Whether the point's own label is shown: the label is visible on the point, the point
        /// style (its point group's when that overrides it) draws its label component, and it has a
        /// label style (its point group's when that overrides it). Read through Civil 3D's typed
        /// members (CogoPoint.IsLabelVisible, PointStyle.GetDisplayStylePlan, CogoPoint.LabelStyleId,
        /// StyleBase.Name); only the point group override is read by reflection. Whatever goes wrong
        /// is kept in Problem: "not read" is never reported as "hidden".
        /// </summary>
        public static LabelRead ReadLabel(CogoPoint point, Database db, Transaction tr)
        {
            var r = new LabelRead();
            try
            {
                bool visible;
                try { visible = point.IsLabelVisible; }
                catch (System.Exception ex) { r.Problem = "label visibility: " + Why(ex); return r; }
                if (!visible) { r.Shown = false; r.Problem = "the point's label is turned off"; return r; }

                var e = Resolve(point, db, tr);
                if (e.Problem != null) { r.Problem = "point style: " + e.Problem; return r; }
                if (e.StyleId.IsNull) { r.Problem = "the point has no point style"; return r; }
                var style = tr.GetObject(e.StyleId, OpenMode.ForRead) as PointStyle;
                if (style == null) { r.Problem = "its point style could not be opened"; return r; }
                bool component;
                try { component = style.GetDisplayStylePlan(PointDisplayStyleType.Label).Visible; }
                catch (System.Exception ex) { r.Problem = "the point style's label component: " + Why(ex); return r; }
                if (!component) { r.Shown = false; r.Problem = "its point style draws no label in plan"; return r; }

                ObjectId labelStyleId;
                string from;
                if (!LabelStyleOf(point, tr, out labelStyleId, out from, out r.Problem)) return r;
                if (labelStyleId.IsNull) { r.Shown = false; r.Problem = "it has no label style" + from; return r; }
                r.Shown = true;
                try
                {
                    var labelStyle = tr.GetObject(labelStyleId, OpenMode.ForRead) as Autodesk.Civil.DatabaseServices.Styles.StyleBase;
                    if (labelStyle == null) r.Problem = "its label style could not be opened" + from;
                    else r.StyleName = labelStyle.Name ?? string.Empty;
                }
                catch (System.Exception ex) { r.Problem = "label style name" + from + ": " + Why(ex); }
                return r;
            }
            catch (System.Exception ex)
            {
                // Read for every point by FTFLABELS and point accountability: whatever goes wrong here
                // means "not read" (reported as such), never a command that stops.
                r.Shown = null;
                r.Problem = "the label could not be read: " + Why(ex);
                return r;
            }
        }

        /// <summary>The label style in force: the point group's when it overrides the points' own.</summary>
        private static bool LabelStyleOf(CogoPoint point, Transaction tr, out ObjectId id, out string from, out string problem)
        {
            id = ObjectId.Null;
            from = string.Empty;
            problem = null;
            var groupId = FieldCodes.PropertyReader.Read(point, "PrimaryPointGroupId");
            if (groupId is ObjectId && !((ObjectId)groupId).IsNull)
            {
                var group = tr.GetObject((ObjectId)groupId, OpenMode.ForRead);
                var overridden = FieldCodes.PropertyReader.Read(group, "IsPointLabelStyleOverridden");
                if (overridden is bool && (bool)overridden)
                {
                    var groupStyle = FieldCodes.PropertyReader.Read(group, "PointLabelStyleId");
                    if (!(groupStyle is ObjectId))
                    {
                        problem = "its point group overrides the label style, and the override could not be read";
                        return false;
                    }
                    id = (ObjectId)groupStyle;
                    from = " (from point group " + Convert.ToString(FieldCodes.PropertyReader.Read(group, "Name")) + ")";
                    return true;
                }
            }
            try { id = point.LabelStyleId; }
            catch (System.Exception ex) { problem = "label style: " + Why(ex); return false; }
            return true;
        }

        /// <summary>Whether the point's own label is shown (see <see cref="ReadLabel"/>); null: not read.</summary>
        public static bool? LabelShown(CogoPoint point, Database db, Transaction tr, out string labelStyleName)
        {
            var r = ReadLabel(point, db, tr);
            labelStyleName = r.StyleName;
            return r.Shown;
        }

        private static string Why(System.Exception ex)
        {
            var inner = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
            return inner.GetType().Name + ": " + inner.Message;
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

        /// <summary>A property read by reflection: the most derived declaration when Civil 3D declares
        /// one more than once (Type.GetProperty would throw), null when it is not there or throws.</summary>
        private static object ReadProperty(object target, string name)
        {
            return FieldCodes.PropertyReader.Read(target, name);
        }
    }
}
