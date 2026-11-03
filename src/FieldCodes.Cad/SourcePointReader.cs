using System.Collections.Generic;
using System.Globalization;
using Autodesk.AutoCAD.DatabaseServices;
using FieldCodes.Linework;

namespace FieldCodes.Cad
{
    /// <summary>
    /// Reads every COGO point in model space into the one source-point index the linework checks
    /// share: point number, handle, raw description as stored, and location (Z kept). Read-only:
    /// points are opened for read and nothing is written.
    /// </summary>
    internal static class SourcePointReader
    {
        public static SourcePointIndex Read(Database db, Transaction tr)
        {
            return Read(db, tr, null);
        }

        /// <summary>As <see cref="Read(Database, Transaction)"/>, with each point's crew note (from
        /// <paramref name="notes"/>, the source Settings > Point Labels names) kept beside its raw
        /// description.</summary>
        public static SourcePointIndex Read(Database db, Transaction tr, CogoNotes notes)
        {
            // Each step goes to the smoke test's trace file (FtfCommandTrace.Step) when there is one,
            // so a run that ends inside the reader says at which point and which property.
            var tracing = FtfCommandTrace.Tracing;
            if (tracing) FtfCommandTrace.Step("reader: start (notes " + (notes == null ? "not read" : "read") + ")");
            var points = new List<SourcePoint>();
            var n = 0;
            foreach (var cogo in CadUtil.CogoPointsInOrder(db, tr))
            {
                n++;
                if (tracing) FtfCommandTrace.Step("reader: point " + n + " handle " + cogo.Handle + ": reading");
                string raw;
                try { raw = cogo.RawDescription; }
                catch (Autodesk.AutoCAD.Runtime.Exception) { raw = null; }
                string note = null;
                if (notes != null)
                {
                    try { note = notes.NoteOf(cogo); }
                    catch (Autodesk.AutoCAD.Runtime.Exception) { note = null; }
                }
                var at = cogo.Location;
                points.Add(new SourcePoint
                {
                    Number = cogo.PointNumber.ToString(CultureInfo.InvariantCulture),
                    Handle = cogo.Handle.ToString(),
                    RawDescription = raw ?? string.Empty,
                    Note = note,
                    X = at.X,
                    Y = at.Y,
                    Z = at.Z
                });
                if (tracing) FtfCommandTrace.Step("reader: point " + n + " number " + points[points.Count - 1].Number + ": read");
            }
            if (tracing) FtfCommandTrace.Step("reader: done, " + points.Count + " points");
            return new SourcePointIndex(points);
        }
    }
}
