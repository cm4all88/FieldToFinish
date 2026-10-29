using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.DatabaseServices;
using FieldCodes.Drafting;
using FieldCodes.Easements;
using FieldCodes.Settings;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcDocument = Autodesk.AutoCAD.ApplicationServices.Document;
using AcEntity = Autodesk.AutoCAD.DatabaseServices.Entity;

namespace FieldCodes.Cad
{
    /// <summary>
    /// FTFBENCH: a bench from its two end shots. The crew shoots the middle of each end and
    /// notes the depth (D1.5, 1.5D, DEPTH 18IN) and often the height. The outline is the line
    /// between the shots offset half the depth to each side, ends closed, on the bench layer;
    /// the label says its size ("BENCH 3'x1.5'"). Pick the two points, or the two-point line
    /// Civil 3D drew through them. Anything the notes do not say is asked for, never guessed.
    ///
    /// The outline and label are deliberate drafting, like FTFDRAWLINE's lines: FTFCLEAN
    /// leaves them alone and FTFDRAFTCLEAN manages them. Running FTFBENCH again on the same
    /// two shots replaces that bench.
    ///
    /// UNTESTED against a drawing. The reading, outline and label are unit tested in
    /// FieldCodes.Drafting.Bench.
    /// </summary>
    public sealed class BenchCommands
    {
        /// <summary>TagText on every entity of a bench, so FTFBENCH can find its own.</summary>
        internal const string BenchTag = "BENCH";

        private const double CoincidenceTolerance = 0.01;

        private sealed class BenchEnds
        {
            public Point3d A;
            public Point3d B;
            public string DescriptionA;
            public string DescriptionB;
            public string Key;
            public string Source;
        }

        [CommandMethod("FTFBENCH", CommandFlags.Modal)]
        public void FtfBench()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;
            var db = doc.Database;

            RulesConfig cfg;
            FtfSettings settings;
            try
            {
                cfg = FtfSession.Rules(db);
                settings = FtfSession.SettingsFor(db, cfg);
            }
            catch (ConfigException ex)
            {
                ed.WriteMessage("\nRules problem: {0}\n", ex.Message);
                return;
            }

            var drawn = 0;
            while (true)
            {
                var ends = PickEnds(doc, cfg.Bench);
                if (ends == null) break;

                using (doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    if (DrawOne(doc, tr, ends, cfg, settings)) drawn++;
                    tr.Commit();
                }
            }
            ed.WriteMessage("\nFTFBENCH: {0} bench(es) drawn.\n", drawn);
        }

        // ------------------------------------------------------------------ picking

        /// <summary>The two ends: two survey points, a two-point line, or two picked
        /// positions. Null when the person is done.</summary>
        private static BenchEnds PickEnds(AcDocument doc, BenchRule rule)
        {
            var ed = doc.Editor;
            var db = doc.Database;
            while (true)
            {
                // Keywords cannot contain spaces: the bracketed word must be exactly "Pick".
                var first = new PromptEntityOptions("\nSelect a bench end point or the bench line, or pick positions [Pick] <done>: ", "Pick")
                {
                    AllowNone = true
                };
                var picked = ed.GetEntity(first);
                if (picked.Status == PromptStatus.Keyword) return PickPositions(ed);
                if (picked.Status != PromptStatus.OK) return null;

                using (var tr = db.TransactionManager.StartOpenCloseTransaction())
                {
                    var entity = tr.GetObject(picked.ObjectId, OpenMode.ForRead, false, true) as AcEntity;
                    var cogo = entity as CogoPoint;
                    if (cogo != null)
                    {
                        var other = new PromptEntityOptions("\nSelect the point at the other end of the bench: ");
                        other.SetRejectMessage("\nThat is not a survey point.");
                        other.AddAllowedClass(typeof(CogoPoint), true);
                        var second = ed.GetEntity(other);
                        if (second.Status != PromptStatus.OK) return null;
                        var cogoB = tr.GetObject(second.ObjectId, OpenMode.ForRead) as CogoPoint;
                        if (cogoB == null || cogoB.PointNumber == cogo.PointNumber)
                        {
                            ed.WriteMessage("\nPick the point at the OTHER end of the bench.");
                            tr.Commit();
                            continue;
                        }
                        var low = Math.Min(cogo.PointNumber, cogoB.PointNumber);
                        var high = Math.Max(cogo.PointNumber, cogoB.PointNumber);
                        var ends = new BenchEnds
                        {
                            A = CadUtil.Flatten(cogo.Location),
                            B = CadUtil.Flatten(cogoB.Location),
                            DescriptionA = cogo.RawDescription,
                            DescriptionB = cogoB.RawDescription,
                            Key = low.ToString(CultureInfo.InvariantCulture) + "-" + high.ToString(CultureInfo.InvariantCulture),
                            Source = "points " + cogo.PointNumber.ToString(CultureInfo.InvariantCulture) + " and " +
                                     cogoB.PointNumber.ToString(CultureInfo.InvariantCulture)
                        };
                        tr.Commit();
                        return ends;
                    }

                    var curve = entity as Curve;
                    if (curve != null && Ownership.Read(curve) == null)
                    {
                        var vertices = Vertices(curve);
                        if (vertices.Count != 2)
                        {
                            ed.WriteMessage("\nThat line has {0} vertices. A bench line runs end shot to end shot -- " +
                                            "pick the two end points instead.", vertices.Count);
                            tr.Commit();
                            continue;
                        }
                        var ends = new BenchEnds
                        {
                            A = CadUtil.Flatten(vertices[0]),
                            B = CadUtil.Flatten(vertices[1]),
                            DescriptionA = DescriptionAt(db, tr, vertices[0]),
                            DescriptionB = DescriptionAt(db, tr, vertices[1]),
                            Key = "line-" + curve.Handle.ToString(),
                            Source = "the line on " + curve.Layer
                        };
                        tr.Commit();
                        return ends;
                    }
                    tr.Commit();
                }
                ed.WriteMessage("\nPick a survey point at one end of the bench, or the line between its end shots.");
            }
        }

        private static BenchEnds PickPositions(Editor ed)
        {
            var a = ed.GetPoint(new PromptPointOptions("\nFirst end of the bench (middle of the end): "));
            if (a.Status != PromptStatus.OK) return null;
            var b = ed.GetPoint(new PromptPointOptions("\nOther end of the bench: ") { UseBasePoint = true, BasePoint = a.Value });
            if (b.Status != PromptStatus.OK) return null;
            var pa = CadUtil.Flatten(a.Value);
            var pb = CadUtil.Flatten(b.Value);
            return new BenchEnds
            {
                A = pa,
                B = pb,
                Key = "xy-" + Coordinate(pa) + "-" + Coordinate(pb),
                Source = "two picked positions (no survey notes)"
            };
        }

        private static string Coordinate(Point3d p)
        {
            return Math.Round(p.X, 2).ToString("0.##", CultureInfo.InvariantCulture) + "," +
                   Math.Round(p.Y, 2).ToString("0.##", CultureInfo.InvariantCulture);
        }

        /// <summary>A line's vertices: every integer parameter of a polyline, else its ends.</summary>
        private static IList<Point3d> Vertices(Curve curve)
        {
            var points = new List<Point3d>();
            try
            {
                if (curve is Polyline || curve is Polyline2d || curve is Polyline3d)
                {
                    for (var p = Math.Ceiling(curve.StartParam); p <= curve.EndParam + 1e-9; p += 1.0)
                        points.Add(curve.GetPointAtParameter(p));
                    // A closed polyline repeats its start at the end parameter.
                    if (points.Count > 1 && points[0].DistanceTo(points[points.Count - 1]) < 1e-9) points.RemoveAt(points.Count - 1);
                }
                else
                {
                    points.Add(curve.StartPoint);
                    points.Add(curve.EndPoint);
                }
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                points.Clear();
            }
            return points;
        }

        /// <summary>The note of the survey point that created a vertex: coincident within a
        /// hundredth, as FTFLABELLINE reads line identity. Null when no point sits there.</summary>
        private static string DescriptionAt(Database db, Transaction tr, Point3d vertex)
        {
            var flat = CadUtil.Flatten(vertex);
            foreach (var cogo in CadUtil.CogoPointsInOrder(db, tr))
            {
                var at = CadUtil.Flatten(cogo.Location);
                if (Math.Abs(at.X - flat.X) <= CoincidenceTolerance && Math.Abs(at.Y - flat.Y) <= CoincidenceTolerance)
                    return cogo.RawDescription;
            }
            return null;
        }

        // ------------------------------------------------------------------ drawing

        private static bool DrawOne(AcDocument doc, Transaction tr, BenchEnds ends, RulesConfig cfg, FtfSettings settings)
        {
            var ed = doc.Editor;
            var db = doc.Database;
            var rule = cfg.Bench ?? new BenchRule();
            var ls = settings.LineLabels;
            var upf = settings.General.UnitsPerFoot > 0 ? settings.General.UnitsPerFoot : 1.0;

            ed.WriteMessage("\nBench from {0}.", ends.Source);
            if (ends.DescriptionA != null || ends.DescriptionB != null)
            {
                ed.WriteMessage("\n  Notes: \"{0}\" / \"{1}\"", ends.DescriptionA ?? "(none)", ends.DescriptionB ?? "(none)");
                if (!BenchPlanner.IsBenchCode(ends.DescriptionA, rule.Codes) && !BenchPlanner.IsBenchCode(ends.DescriptionB, rule.Codes))
                    ed.WriteMessage("\n  Neither shot is coded as a bench ({0}); drawn as picked.", string.Join(", ", rule.Codes.ToArray()));
            }

            var reading = BenchNotes.Combine(BenchNotes.Read(ends.DescriptionA), BenchNotes.Read(ends.DescriptionB));
            foreach (var p in reading.Problems) ed.WriteMessage("\n  " + p);
            if (reading.UnreadNumbers.Count > 0)
                ed.WriteMessage("\n  Numbers with no D or H beside them, not used: {0}.", string.Join(", ", reading.UnreadNumbers.ToArray()));

            var depth = reading.Problems.Any(p => p.Contains("depths")) ? null : reading.DepthFeet;
            var height = reading.Problems.Any(p => p.Contains("heights")) ? null : reading.HeightFeet;

            if (!depth.HasValue)
            {
                var ask = new PromptDoubleOptions("\nDepth of the bench, in feet: ") { AllowNegative = false, AllowZero = false, AllowNone = false };
                var answer = ed.GetDouble(ask);
                if (answer.Status != PromptStatus.OK) { ed.WriteMessage("\nNo depth; nothing drawn."); return false; }
                depth = answer.Value;
            }
            else ed.WriteMessage("\n  Depth {0}' from the notes.", depth.Value.ToString("0.###", CultureInfo.InvariantCulture));

            if (!height.HasValue && BenchPlanner.UsesHeight(rule.LabelFormat))
            {
                var ask = new PromptDoubleOptions("\nHeight of the bench, in feet <leave it off>: ") { AllowNegative = false, AllowZero = false, AllowNone = true };
                var answer = ed.GetDouble(ask);
                if (answer.Status == PromptStatus.OK) height = answer.Value;
                else if (answer.Status != PromptStatus.None) return false;
            }

            var plan = BenchPlanner.Plan(new P2(ends.A.X, ends.A.Y), new P2(ends.B.X, ends.B.Y), depth.Value, height, rule.LabelFormat, upf);
            if (!plan.Ok)
            {
                foreach (var p in plan.Problems) ed.WriteMessage("\n  " + p);
                ed.WriteMessage("\nNothing drawn.");
                return false;
            }
            foreach (var w in plan.Warnings) ed.WriteMessage("\n  CHECK: " + w);

            Ownership.EnsureRegApp(db, tr);

            // The same two shots again: replace that bench rather than draw a second.
            var replaced = 0;
            foreach (var old in Ownership.FindOwned(db, tr, s => s.TagText == BenchTag && s.PointNumber == ends.Key &&
                                                             (s.Kind == FtfEntityKind.DraftLine || s.Kind == FtfEntityKind.DraftAnnotation || s.Kind == FtfEntityKind.DraftMask)))
            {
                var entity = tr.GetObject(old.Key, OpenMode.ForWrite, false, true) as AcEntity;
                if (entity == null) continue;
                entity.Erase();
                replaced++;
            }

            var outline = new Polyline(4);
            outline.SetDatabaseDefaults(db);
            for (var i = 0; i < plan.Corners.Length; i++)
                outline.AddVertexAt(i, new Point2d(plan.Corners[i].X, plan.Corners[i].Y), 0.0, 0.0, 0.0);
            outline.Closed = true;
            outline.LayerId = CadUtil.EnsureLayer(db, tr, rule.Layer);
            CadUtil.AddToModelSpace(db, tr, outline);
            Ownership.Stamp(outline, ends.Key, cfg.Version, FtfEntityKind.DraftLine, null, BenchTag);

            if (plan.LabelLines.Count > 0)
                PlaceLabel(db, tr, ed, plan, rule, ls, upf, ends.Key, cfg.Version);

            ed.WriteMessage("\nDrawn: {0}, {1} x {2} ft on {3}{4}.",
                plan.LabelLines.Count > 0 ? string.Join(" / ", plan.LabelLines.ToArray()) : "(no label)",
                plan.LengthFeet.ToString("0.##", CultureInfo.InvariantCulture),
                plan.DepthFeet.ToString("0.##", CultureInfo.InvariantCulture),
                rule.Layer,
                replaced > 0 ? "; replaced the bench drawn earlier from the same shots" : string.Empty);
            return true;
        }

        private static void PlaceLabel(Database db, Transaction tr, Editor ed, BenchPlan plan, BenchRule rule,
                                       LineLabelSettings ls, double upf, string key, string rulesVersion)
        {
            var scale = CadUtil.DrawingUnitsPerPlottedUnit(db);
            var textHeight = ls.TextHeightPlotted * scale;
            var styleId = Setup.DrawingResources.FindTextStyle(db, tr, ls.TextStyle);
            var rotation = ls.AlignToLine ? plan.LabelRotationRadians : 0.0;

            // Beside the bench by default: at plotted text sizes the label is often taller
            // than the bench is deep, and on top of it would hide the outline.
            var up = new Vector3d(-Math.Sin(rotation), Math.Cos(rotation), 0.0);
            var centre = new Point3d(plan.Centre.X, plan.Centre.Y, 0.0);
            var clearance = plan.DepthFeet * upf / 2.0 + textHeight * (0.75 * plan.LabelLines.Count + 0.5);
            var beside = centre + up * clearance;

            var where = ed.GetPoint(new PromptPointOptions("\nLabel position <beside the bench>: ")
            {
                AllowNone = true,
                UseBasePoint = true,
                BasePoint = centre
            });
            var position = where.Status == PromptStatus.OK ? CadUtil.Flatten(where.Value) : beside;

            var layerId = CadUtil.EnsureLayer(db, tr, rule.LabelLayer);
            var text = new MText();
            text.SetDatabaseDefaults(db);
            if (!styleId.IsNull) text.TextStyleId = styleId;
            text.Contents = plan.LabelText;
            text.TextHeight = textHeight;
            text.Attachment = AttachmentPoint.MiddleCenter;
            text.Location = position;
            text.Rotation = rotation;
            text.LayerId = layerId;
            CadUtil.AddToModelSpace(db, tr, text);
            Ownership.Stamp(text, key, rulesVersion, FtfEntityKind.DraftAnnotation, position, BenchTag);

            if (ls.DrawMask)
            {
                // TextMask.Place sizes for one line of text; a label of several lines is
                // masked from the MText's own unrotated size instead.
                var maskId = plan.LabelLines.Count > 1
                    ? TextMask.PlaceBox(db, tr, text.ObjectId, position, rotation, text.ActualWidth, text.ActualHeight, layerId)
                    : TextMask.Place(db, tr, text, position, rotation, textHeight, layerId);
                if (!maskId.IsNull)
                {
                    var mask = (AcEntity)tr.GetObject(maskId, OpenMode.ForWrite);
                    Ownership.Stamp(mask, key, rulesVersion, FtfEntityKind.DraftMask, null, BenchTag);
                }
            }
        }
    }
}
