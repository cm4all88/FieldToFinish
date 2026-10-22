using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using FieldCodes.Compare;

namespace FieldCodes.Cad.Compare
{
    /// <summary>
    /// A revision cloud around every discrepancy and a note beside it, on a plotting
    /// review layer, so the person reviewing (and the consultant, if the drawing goes
    /// out with them) sees exactly which object differs and by how much. Drawn when the
    /// comparison runs, removed by restore and before the next run; every mark is
    /// stamped as ours (FtfEntityKind.CompareCloud) so nothing accumulates.
    ///
    /// UNTESTED in Civil 3D until the smoke test says otherwise.
    /// </summary>
    internal static class CompareReviewMarks
    {
        public const string Tag = "COMPARE-CLOUD";

        public static int Clear(Database db, Transaction tr)
        {
            return Ownership.DeleteOwned(db, tr, st => st.Kind == FtfEntityKind.CompareCloud);
        }

        public static int Count(Database db, Transaction tr)
        {
            return Ownership.FindOwned(db, tr, st => st.Kind == FtfEntityKind.CompareCloud).Count;
        }

        /// <summary>Removes the previous marks and draws one cloud and note per discrepancy.</summary>
        public static int Refresh(Database db, Transaction tr, ComparisonResult result, CompareProfile profile, Editor ed)
        {
            Clear(db, tr);
            if (result == null || profile == null || profile.Review == null || !profile.Review.DrawClouds) return 0;
            var upf = result.UnitsPerFoot > 0 ? result.UnitsPerFoot : 1.0;
            var marked = 0;
            var layerReady = false;
            foreach (var r in result.Results)
            {
                if (r.Status != MatchStatus.Discrepancy) continue;
                if (!layerReady) { EnsureLayer(db, tr, profile); layerReady = true; }
                if (Draw(db, tr, r, profile, upf))
                {
                    r.Note("revision cloud drawn");
                    marked++;
                }
            }
            if (ed != null && marked > 0)
                ed.WriteMessage("\n  {0} discrepancy(ies) marked with a revision cloud and note on layer {1} (they plot; FTFCOMPARERESTORE removes them).", marked, profile.Review.Layer);
            return marked;
        }

        private static void EnsureLayer(Database db, Transaction tr, CompareProfile profile)
        {
            var table = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (table.Has(profile.Review.Layer))
            {
                var existing = (LayerTableRecord)tr.GetObject(table[profile.Review.Layer], OpenMode.ForRead);
                if (existing.IsFrozen || existing.IsOff)
                {
                    existing.UpgradeOpen();
                    existing.IsFrozen = false;
                    existing.IsOff = false;
                }
                return;
            }
            table.UpgradeOpen();
            var record = new LayerTableRecord
            {
                Name = profile.Review.Layer,
                IsPlottable = true,
                Color = Color.FromColorIndex(ColorMethod.ByAci, profile.Review.ColorIndex),
                Description = "FTFCOMPARE: revision clouds and notes marking discrepancies with the consultant base map."
            };
            table.Add(record);
            tr.AddNewlyCreatedDBObject(record, true);
        }

        private static bool Draw(Database db, Transaction tr, FeatureResult r, CompareProfile profile, double upf)
        {
            var margin = profile.Review.MarginFeet * upf;
            double minX, minY, maxX, maxY;
            if (r.Kind == FeatureKind.Line && !r.Bounds.IsEmpty)
            {
                minX = r.Bounds.MinX - margin; minY = r.Bounds.MinY - margin;
                maxX = r.Bounds.MaxX + margin; maxY = r.Bounds.MaxY + margin;
            }
            else
            {
                minX = r.Location.X - margin; minY = r.Location.Y - margin;
                maxX = r.Location.X + margin; maxY = r.Location.Y + margin;
            }

            var ms = CadUtil.ModelSpace(db, tr, OpenMode.ForWrite);
            var cloud = Cloud(minX, minY, maxX, maxY, Math.Max(1.5 * upf, margin / 2.0));
            cloud.Layer = profile.Review.Layer;
            ms.AppendEntity(cloud);
            tr.AddNewlyCreatedDBObject(cloud, true);
            Ownership.Stamp(cloud, r.Handle, string.Empty, FtfEntityKind.CompareCloud, null, Tag);

            var height = profile.Review.TextHeightFeet * upf;
            var note = new MText
            {
                Contents = NoteText(r),
                TextHeight = height,
                Width = height * 45,
                Location = new Point3d(minX, maxY + height * 0.5, 0),
                Attachment = AttachmentPoint.BottomLeft,
                Layer = profile.Review.Layer
            };
            ms.AppendEntity(note);
            tr.AddNewlyCreatedDBObject(note, true);
            Ownership.Stamp(note, r.Handle, string.Empty, FtfEntityKind.CompareCloud, null, Tag);
            return true;
        }

        private static string NoteText(FeatureResult r)
        {
            var what = string.IsNullOrEmpty(r.Category) ? r.EntityType : r.Category;
            var reasons = r.Notes == null ? string.Empty : string.Join("; ", r.Notes.Where(n => n != "revision cloud drawn").ToArray());
            return string.Format(CultureInfo.InvariantCulture, "FTFCOMPARE DISCREPANCY {0} ({1}): {2}", r.Handle, what, reasons);
        }

        /// <summary>A rectangle of outward arcs (bulge 0.5 on a counter-clockwise loop).</summary>
        private static Polyline Cloud(double minX, double minY, double maxX, double maxY, double arcChord)
        {
            var corners = new[] { new Point2d(minX, minY), new Point2d(maxX, minY), new Point2d(maxX, maxY), new Point2d(minX, maxY) };
            var pl = new Polyline();
            var index = 0;
            for (var i = 0; i < 4; i++)
            {
                var a = corners[i];
                var b = corners[(i + 1) % 4];
                var length = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
                var n = Math.Max(1, (int)Math.Round(length / arcChord));
                for (var k = 0; k < n; k++)
                {
                    var p = new Point2d(a.X + (b.X - a.X) * k / n, a.Y + (b.Y - a.Y) * k / n);
                    pl.AddVertexAt(index++, p, 0.5, 0, 0);
                }
            }
            pl.Closed = true;
            return pl;
        }
    }
}
