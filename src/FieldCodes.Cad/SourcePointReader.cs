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
            var points = new List<SourcePoint>();
            foreach (var cogo in CadUtil.CogoPointsInOrder(db, tr))
            {
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
            }
            return new SourcePointIndex(points);
        }
    }
}
