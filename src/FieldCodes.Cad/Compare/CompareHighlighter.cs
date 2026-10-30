using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;
using FieldCodes.Compare;

using AcEntity = Autodesk.AutoCAD.DatabaseServices.Entity;

namespace FieldCodes.Cad.Compare
{
    /// <summary>
    /// Temporary review graphics: our feature in one colour, the consultant's matching
    /// geometry in another, drawn as transients so nothing in the drawing changes and
    /// nothing plots. Cleared on the next selection or when the window closes.
    ///
    /// UNTESTED in Civil 3D.
    /// </summary>
    internal static class CompareHighlighter
    {
        private static readonly List<AcEntity> Transients = new List<AcEntity>();
        private static ObjectId _highlighted = ObjectId.Null;

        public static void Show(Database db, Transaction tr, Editor ed, FeatureResult result, IList<ConsultantShape> theirs,
                                short surveyColor, short consultantColor, bool zoom)
        {
            Clear(tr);
            if (result == null) return;

            var id = CompareLayerManager.ResolveId(db, result.Handle);
            Extents3d? extents = null;
            if (!id.IsNull)
            {
                var entity = tr.GetObject(id, OpenMode.ForRead, false, true) as AcEntity;
                if (entity != null)
                {
                    try { entity.Highlight(); _highlighted = id; } catch (Autodesk.AutoCAD.Runtime.Exception) { }
                    try { extents = entity.GeometricExtents; } catch (Autodesk.AutoCAD.Runtime.Exception) { }
                    AddTransient(Copy(entity, result, surveyColor));
                }
            }

            foreach (var shape in theirs ?? new List<ConsultantShape>())
            {
                var drawable = FromShape(shape, consultantColor);
                if (drawable == null) continue;
                AddTransient(drawable);
                try
                {
                    var e = drawable.GeometricExtents;
                    if (extents == null) extents = e; else { var x = extents.Value; x.AddExtents(e); extents = x; }
                }
                catch (Autodesk.AutoCAD.Runtime.Exception) { }
            }

            if (extents == null && result.Kind != FeatureKind.Line)
                extents = new Extents3d(new Point3d(result.Location.X - 5, result.Location.Y - 5, 0), new Point3d(result.Location.X + 5, result.Location.Y + 5, 0));
            if (zoom && extents.HasValue) UtilityCadService.ZoomTo(ed, extents.Value);
            if (!id.IsNull) ed.SetImpliedSelection(new[] { id });
        }

        public static void Clear(Transaction tr)
        {
            var manager = TransientManager.CurrentTransientManager;
            foreach (var e in Transients)
            {
                try { manager.EraseTransient(e, new IntegerCollection()); } catch (Autodesk.AutoCAD.Runtime.Exception) { }
                try { e.Dispose(); } catch (Autodesk.AutoCAD.Runtime.Exception) { }
            }
            Transients.Clear();
            if (!_highlighted.IsNull && tr != null)
            {
                try
                {
                    if (!_highlighted.IsErased)
                    {
                        var entity = tr.GetObject(_highlighted, OpenMode.ForRead, false, true) as AcEntity;
                        if (entity != null) entity.Unhighlight();
                    }
                }
                catch (Autodesk.AutoCAD.Runtime.Exception) { }
            }
            _highlighted = ObjectId.Null;
        }

        private static void AddTransient(AcEntity entity)
        {
            if (entity == null) return;
            try
            {
                TransientManager.CurrentTransientManager.AddTransient(entity, TransientDrawingMode.Highlight, 128, new IntegerCollection());
                Transients.Add(entity);
            }
            catch (Autodesk.AutoCAD.Runtime.Exception) { entity.Dispose(); }
        }

        /// <summary>A heavier copy of our feature's shape in the survey colour.</summary>
        private static AcEntity Copy(AcEntity entity, FeatureResult result, short color)
        {
            try
            {
                var curve = entity as Curve;
                if (curve != null)
                {
                    var points = new Point3dCollection();
                    var length = curve.GetDistanceAtParameter(curve.EndParam) - curve.GetDistanceAtParameter(curve.StartParam);
                    var start = curve.GetDistanceAtParameter(curve.StartParam);
                    var step = Math.Max(0.5, length / 300.0);
                    for (var d = 0.0; d < length; d += step) points.Add(curve.GetPointAtDist(start + d));
                    points.Add(curve.EndPoint);
                    if (points.Count < 2) return null;
                    return Style(new Polyline3d(Poly3dType.SimplePoly, points, false), color, LineWeight.LineWeight070);
                }
                var extents = entity.GeometricExtents;
                var center = new Point3d((extents.MinPoint.X + extents.MaxPoint.X) / 2, (extents.MinPoint.Y + extents.MaxPoint.Y) / 2, 0);
                var radius = Math.Max(1.5, Math.Max(extents.MaxPoint.X - extents.MinPoint.X, extents.MaxPoint.Y - extents.MinPoint.Y) * 0.75);
                return Style(new Circle(center, Vector3d.ZAxis, radius), color, LineWeight.LineWeight050);
            }
            catch (Autodesk.AutoCAD.Runtime.Exception) { return null; }
        }

        private static AcEntity FromShape(ConsultantShape shape, short color)
        {
            if (shape == null) return null;
            if (shape.IsPoint || shape.Vertices == null || shape.Vertices.Count < 2)
                return Style(new Circle(new Point3d(shape.Position.X, shape.Position.Y, 0), Vector3d.ZAxis, 2.5), color, LineWeight.LineWeight050);
            var points = new Point3dCollection();
            foreach (var v in shape.Vertices) points.Add(new Point3d(v.X, v.Y, 0));
            return Style(new Polyline3d(Poly3dType.SimplePoly, points, false), color, LineWeight.LineWeight050);
        }

        private static AcEntity Style(AcEntity entity, short color, LineWeight weight)
        {
            entity.ColorIndex = color;
            entity.LineWeight = weight;
            return entity;
        }
    }
}
