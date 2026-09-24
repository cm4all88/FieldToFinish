using System;
using System.Collections.Generic;
using System.Reflection;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;

namespace FieldCodes.Cad.Setup
{
    /// <summary>
    /// A snapshot of the named things in the current drawing, so the setup window can
    /// offer dropdowns instead of asking anyone to type a layer name correctly.
    ///
    /// Taken once when the dialog opens. All Autodesk queries live here, which keeps
    /// the individual setup pages free of database code.
    ///
    /// UNTESTED: never run against a drawing.
    /// </summary>
    internal sealed class DrawingResources
    {
        public IList<string> TextStyles { get; private set; }
        public IList<string> Layers { get; private set; }
        public IList<string> Blocks { get; private set; }
        public IList<string> Linetypes { get; private set; }

        /// <summary>Civil 3D general line and curve label styles, for the Recorded Surveys
        /// standards page. Empty in a drawing without Civil 3D styles.</summary>
        public IList<string> LineLabelStyles { get; private set; }
        public IList<string> CurveLabelStyles { get; private set; }

        /// <summary>
        /// Styles with a non-zero height. AutoCAD ignores a text height set in code for
        /// these, so choosing one silently overrides the height setting.
        /// </summary>
        public HashSet<string> FixedHeightTextStyles { get; private set; }

        /// <summary>Drawing units per plotted unit, from the annotation scale.</summary>
        public double Scale { get; private set; }

        public string AnnotationScaleName { get; private set; }
        public string HostVersion { get; private set; }
        public string DrawingPath { get; private set; }

        public DrawingResources(Database db, Transaction tr)
        {
            TextStyles = new List<string>();
            Layers = new List<string>();
            Blocks = new List<string>();
            Linetypes = new List<string>();
            LineLabelStyles = new List<string>();
            CurveLabelStyles = new List<string>();
            FixedHeightTextStyles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Scale = 1.0;
            AnnotationScaleName = "(none)";
            HostVersion = "unknown";
            DrawingPath = string.Empty;

            if (db == null || tr == null) return;

            DrawingPath = db.Filename ?? string.Empty;
            Scale = CadUtil.DrawingUnitsPerPlottedUnit(db);

            try
            {
                var cannoscale = db.Cannoscale;
                if (cannoscale != null) AnnotationScaleName = cannoscale.Name;
            }
            catch (Autodesk.AutoCAD.Runtime.Exception) { }

            HostVersion = DescribeHost();

            ReadTextStyles(db, tr);
            ReadLayers(db, tr);
            ReadBlocks(db, tr);
            ReadLinetypes(db, tr);
            LineLabelStyles = CivilLabels.LineLabelStyleNames(db, tr);
            CurveLabelStyles = CivilLabels.CurveLabelStyleNames(db, tr);
        }

        private void ReadTextStyles(Database db, Transaction tr)
        {
            var table = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
            var names = new List<string>();

            foreach (ObjectId id in table)
            {
                var record = (TextStyleTableRecord)tr.GetObject(id, OpenMode.ForRead);
                if (string.IsNullOrEmpty(record.Name)) continue;

                names.Add(record.Name);
                if (record.TextSize > 0) FixedHeightTextStyles.Add(record.Name);
            }

            names.Sort(StringComparer.OrdinalIgnoreCase);
            TextStyles = names;
        }

        private void ReadLayers(Database db, Transaction tr)
        {
            var table = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            var names = new List<string>();

            foreach (ObjectId id in table)
            {
                var record = (LayerTableRecord)tr.GetObject(id, OpenMode.ForRead);
                names.Add(record.Name);
            }

            names.Sort(StringComparer.OrdinalIgnoreCase);
            Layers = names;
        }

        private void ReadBlocks(Database db, Transaction tr)
        {
            var table = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var names = new List<string>();

            foreach (ObjectId id in table)
            {
                var record = (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead);
                if (record.IsLayout || record.IsAnonymous) continue;
                names.Add(record.Name);
            }

            names.Sort(StringComparer.OrdinalIgnoreCase);
            Blocks = names;
        }

        private void ReadLinetypes(Database db, Transaction tr)
        {
            var table = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
            var names = new List<string>();

            foreach (ObjectId id in table)
            {
                var record = (LinetypeTableRecord)tr.GetObject(id, OpenMode.ForRead);
                names.Add(record.Name);
            }

            names.Sort(StringComparer.OrdinalIgnoreCase);
            Linetypes = names;
        }

        /// <summary>
        /// Best available description of the host. The true Civil 3D product string
        /// lives in AeccMgd.dll, which this project deliberately does not reference, so
        /// the AeccDbMgd assembly version stands in for the Civil 3D release.
        /// </summary>
        private static string DescribeHost()
        {
            var acad = "AutoCAD ?";
            try
            {
                acad = "AutoCAD " + Application.Version;
            }
            catch (System.Exception) { }

            try
            {
                var aecc = typeof(Autodesk.Civil.DatabaseServices.CogoPoint)
                            .Assembly.GetName().Version;
                return string.Format("Civil 3D ({0}, Aecc {1})", acad, aecc);
            }
            catch (System.Exception)
            {
                return acad;
            }
        }

        /// <summary>Resolves a text style name to its record id, or Null when absent.</summary>
        public static ObjectId FindTextStyle(Database db, Transaction tr, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return ObjectId.Null;

            var table = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
            return table.Has(name) ? table[name] : ObjectId.Null;
        }
    }
}
