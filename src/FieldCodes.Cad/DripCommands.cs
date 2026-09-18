using System;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using FieldCodes.Geometry;

namespace FieldCodes.Cad
{
    /// <summary>
    /// Drip-line drawing.
    ///
    /// The trimming maths lives in FieldCodes.Geometry.DripLineTrimmer and is unit
    /// tested, including the tangency and containment cases. This class only turns
    /// its output into AutoCAD entities.
    ///
    /// UNTESTED: the AutoCAD half has never been run against a drawing.
    /// </summary>
    public sealed class DripCommands
    {
        /// <summary>
        /// Draws the outer envelope of the drip circles: overlapping canopies are
        /// trimmed to the arcs that are not inside any other canopy.
        ///
        /// Safe to run twice.
        /// </summary>
        [CommandMethod("FTFDRIP", CommandFlags.Modal)]
        public void FtfDrip()
        {
            FtfSession.Run("FTFDRIP", (db, tr, ed) =>
            {
                var cfg = FtfSession.Rules(db);
                var settings = FtfSession.SettingsFor(db, cfg);
                var parser = new FieldCodeParser(cfg);

                Ownership.EnsureRegApp(db, tr);

                var removed = Ownership.DeleteOwnedKinds(db, tr, FtfEntityKind.DripArc);
                if (removed > 0)
                    ed.WriteMessage("\nFTFDRIP: removed {0} previous drip entities.", removed);

                var circles = new List<Circle2d>();
                var owners = new List<ParsedPoint>();
                var centres = new List<Point3d>();

                foreach (var cogo in CadUtil.CogoPointsInOrder(db, tr))
                {
                    var number = cogo.PointNumber.ToString(CultureInfo.InvariantCulture);
                    var parsed = parser.Parse(number, cogo.RawDescription);

                    if (parsed.HasErrors) continue;          // reported by FTFTREES
                    if (!parsed.HasDripLine) continue;

                    var loc = CadUtil.Flatten(cogo.Location);
                    circles.Add(new Circle2d(loc.X, loc.Y, parsed.DripRadius.Value));
                    owners.Add(parsed);
                    centres.Add(loc);
                }

                if (circles.Count == 0)
                {
                    ed.WriteMessage("\nFTFDRIP: no drip lines to draw.\n");
                    return;
                }

                var linetype = ResolveLinetype(db, tr, ed, settings.Drip.Linetype);

                // Points whose rule turns unification off keep their whole circle.
                var trimmer = new DripLineTrimmer { Tolerance = ToleranceFor(cfg) };
                var results = trimmer.TrimAll(circles);

                var arcs = 0;
                var whole = 0;
                var hidden = 0;

                for (var i = 0; i < results.Count; i++)
                {
                    var parsed = owners[i];
                    var layerId = CadUtil.EnsureLayer(db, tr, parsed.DripLayer);
                    var result = results[i];

                    // A rule that turns unification off wins; otherwise the setting decides.
                    if (!parsed.DripUnify || !settings.Drip.UnifyByDefault)
                    {
                        DrawCircle(db, tr, centres[i], circles[i].R, layerId, parsed,
                                   cfg.Version, linetype);
                        whole++;
                        continue;
                    }

                    if (result.FullyHidden) { hidden++; continue; }

                    if (result.FullCircle)
                    {
                        DrawCircle(db, tr, centres[i], circles[i].R, layerId, parsed,
                                   cfg.Version, linetype);
                        whole++;
                        continue;
                    }

                    foreach (var span in result.Arcs)
                    {
                        DrawArc(db, tr, centres[i], circles[i].R, span, layerId, parsed,
                                cfg.Version, linetype);
                        arcs++;
                    }
                }

                ed.WriteMessage(
                    "\nFTFDRIP: {0} whole circle(s), {1} arc(s), {2} canopy(ies) fully covered.\n",
                    whole, arcs, hidden);
            });
        }

        /// <summary>
        /// Containment tolerance in drawing units. Scaled off the smallest canopy so a
        /// metric drawing does not get a tolerance sized for feet.
        /// </summary>
        private static double ToleranceFor(RulesConfig cfg)
        {
            return 1e-6 * Math.Max(1.0, cfg.UnitsPerFoot);
        }

        /// <summary>
        /// The configured drip linetype, ready to assign -- or empty for the layer's
        /// own. A linetype the drawing does not have yet is loaded from acad.lin;
        /// if it cannot be, the driplines fall back to the layer's linetype and the
        /// command says so rather than failing the whole run.
        /// </summary>
        private static string ResolveLinetype(Database db, Transaction tr,
                                              Autodesk.AutoCAD.EditorInput.Editor ed,
                                              string configured)
        {
            var name = (configured ?? string.Empty).Trim();
            if (name.Length == 0) return string.Empty;

            var table = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
            if (table.Has(name)) return name;

            try
            {
                db.LoadLineTypeFile(name, "acad.lin");
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                // fall through to the re-check below
            }

            table = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
            if (table.Has(name)) return name;

            ed.WriteMessage("\nFTFDRIP: linetype '{0}' is not in this drawing and not " +
                            "in acad.lin; driplines use the layer's linetype instead.",
                            name);
            return string.Empty;
        }

        private static void DrawCircle(Database db, Transaction tr, Point3d centre, double radius,
                                       ObjectId layerId, ParsedPoint parsed, string rulesVersion,
                                       string linetype)
        {
            using (var circle = new Circle(centre, Vector3d.ZAxis, radius))
            {
                circle.LayerId = layerId;
                if (linetype.Length > 0) circle.Linetype = linetype;
                CadUtil.AddToModelSpace(db, tr, circle);
                Ownership.Stamp(circle, parsed.PointNumber, rulesVersion,
                                FtfEntityKind.DripArc, null);
            }
        }

        private static void DrawArc(Database db, Transaction tr, Point3d centre, double radius,
                                    ArcSpan span, ObjectId layerId, ParsedPoint parsed,
                                    string rulesVersion, string linetype)
        {
            // Arc takes angles counter-clockwise from east, which is what ArcSpan uses.
            using (var arc = new Arc(centre, Vector3d.ZAxis, radius, span.StartAngle, span.EndAngle))
            {
                arc.LayerId = layerId;
                if (linetype.Length > 0) arc.Linetype = linetype;
                CadUtil.AddToModelSpace(db, tr, arc);
                Ownership.Stamp(arc, parsed.PointNumber, rulesVersion,
                                FtfEntityKind.DripArc, null);
            }
        }
    }
}
