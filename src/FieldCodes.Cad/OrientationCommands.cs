using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.DatabaseServices;
using FieldCodes.Easements;
using FieldCodes.Geometry;
using FieldCodes.Orient;

using AcEntity = Autodesk.AutoCAD.DatabaseServices.Entity;

namespace FieldCodes.Cad
{
    /// <summary>
    /// FTFORIENT: turns the survey symbols Civil 3D already drew so they point the way the
    /// surveyed features do -- signs at the traffic they face, hydrant nozzles, light and signal
    /// arms, valve pipe axes, pavement arrows and words with their lane. Decisions come from
    /// FieldCodes.Orient (measured, then relationships, then alignment, then inference;
    /// anything unsure is listed for review and left alone).
    ///
    /// What it changes: each COGO point's marker rotation (and label rotation for painted
    /// words). Nothing else -- no blocks inserted, no coordinates or linework touched. The
    /// rotation before the first change is kept on the point (XData FTF_ORIENT), so
    /// FTFORIENTRESET puts every symbol back, and a marker turned by hand since FTF last set
    /// it is kept. QC graphics go on non-plotting layers; the full decision list goes to a CSV
    /// beside the drawing.
    /// </summary>
    public sealed class OrientationCommands
    {
        internal const string XApp = "FTF_ORIENT";

        [CommandMethod("FTFORIENT", CommandFlags.Modal)]
        public void Orient()
        {
            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ask = new PromptKeywordOptions("\nOrient symbols [Apply/Preview/All] <Apply>: ") { AllowNone = true };
            ask.Keywords.Add("Apply");
            ask.Keywords.Add("Preview");
            ask.Keywords.Add("All");
            ask.Keywords.Default = "Apply";
            var answer = doc.Editor.GetKeywords(ask);
            if (answer.Status != PromptStatus.OK && answer.Status != PromptStatus.None) return;
            var mode = answer.Status == PromptStatus.None ? "Apply" : answer.StringResult;
            Run(mode == "Preview", mode == "All");
        }

        /// <summary>Puts every symbol FTFORIENT turned back as it was, and removes the QC.</summary>
        [CommandMethod("FTFORIENTRESET", CommandFlags.Modal)]
        public void Reset()
        {
            FtfSession.Run("FTFORIENTRESET", (db, tr, ed) =>
            {
                var restored = 0;
                foreach (var cogo in CadUtil.CogoPointsInOrder(db, tr))
                {
                    var record = Record.Read(cogo);
                    if (record == null) continue;
                    try
                    {
                        var w = (CogoPoint)tr.GetObject(cogo.ObjectId, OpenMode.ForWrite);
                        w.MarkerRotation = record.OriginalMarker;
                        w.LabelRotation = record.OriginalLabel;
                        w.XData = new ResultBuffer(new TypedValue((int)DxfCode.ExtendedDataRegAppName, XApp));
                        restored++;
                    }
                    catch (Autodesk.AutoCAD.Runtime.Exception) { }
                }
                var qc = Ownership.DeleteOwnedKinds(db, tr, FtfEntityKind.OrientQc);
                ed.WriteMessage("\nFTFORIENTRESET: {0} symbol(s) put back as they were; {1} QC object(s) removed.\n", restored, qc);
            });
        }

        private static void Run(bool preview, bool overwriteExisting)
        {
            FtfSession.Run("FTFORIENT", (db, tr, ed) =>
            {
                var cfg = FtfSession.Rules(db);
                var settings = FtfSession.SettingsFor(db, cfg);
                var std = cfg.Orientation ?? OrientationStandard.Pmx();
                var parser = new FieldCodeParser(cfg);
                var notes = CogoNotes.For(db, tr, settings);

                var scene = new OrientationScene();
                var points = new Dictionary<string, CogoPoint>(StringComparer.OrdinalIgnoreCase);
                foreach (var cogo in CadUtil.CogoPointsInOrder(db, tr))
                {
                    var raw = cogo.RawDescription ?? string.Empty;
                    var code = OrientationStandard.BaseCode(raw);
                    var symbol = std.SymbolFor(code);
                    var luminaire = std.IsLuminaire(code);
                    if (symbol == null && !luminaire) continue;

                    var number = cogo.PointNumber.ToString(CultureInfo.InvariantCulture);
                    var f = new OrientFeature
                    {
                        Id = number, Code = code, Kind = symbol != null ? symbol.Kind : OrientKind.Other,
                        Location = new P2(cogo.Location.X, cogo.Location.Y), Elevation = cogo.Location.Z,
                        Description = raw, Note = notes.NoteOf(cogo), CurrentRotation = cogo.MarkerRotation
                    };

                    // The office sign rule reads its number as the field azimuth ("SN 135").
                    var parsed = notes.Parse(parser, cogo);
                    if (parsed.RotationDegrees.HasValue && parsed.RotationIsAzimuth && f.Kind != OrientKind.Other)
                    {
                        f.MeasuredAzimuth = Angles.NormalizeDegrees(90.0 - parsed.RotationDegrees.Value);
                        f.MeasuredFrom = "field azimuth in the description (" + raw + ")";
                    }
                    scene.Features.Add(f);
                    points[number] = cogo;
                }

                ReadLinework(db, tr, std, scene);
                var result = new OrientationEngine(std).Run(scene);
                var outcomes = Apply(db, tr, std, result, points, preview, overwriteExisting);
                var qc = DrawQc(db, tr, cfg.Version, std, result);

                ed.WriteMessage("\nFTFORIENT{0}: {1} road/utility line(s) read.", preview ? " (preview: nothing rotated)" : "",
                                scene.Lines.Count);
                foreach (var line in OrientationReport.Summary(result, outcomes)) ed.WriteMessage("\n" + line);
                ed.WriteMessage("\n{0} QC object(s) on {1} / {2} (not plotted).", qc, std.QcLayer, std.QcInferredLayer);

                var path = ReportPath(db, settings);
                if (path != null)
                {
                    try
                    {
                        File.WriteAllText(path, OrientationReport.Csv(result, outcomes));
                        ed.WriteMessage("\nEvery decision and its evidence: {0}", path);
                    }
                    catch (IOException ex) { ed.WriteMessage("\nCould not write {0}: {1}", path, ex.Message); }
                    catch (UnauthorizedAccessException ex) { ed.WriteMessage("\nCould not write {0}: {1}", path, ex.Message); }
                }
                ed.WriteMessage("\nFTFORIENTRESET puts every symbol back.\n");
            });
        }

        // ------------------------------------------------------------------ reading

        private static void ReadLinework(Database db, Transaction tr, OrientationStandard std, OrientationScene scene)
        {
            var ms = CadUtil.ModelSpace(db, tr, OpenMode.ForRead);
            foreach (ObjectId id in ms)
            {
                if (id.IsErased) continue;
                var entity = tr.GetObject(id, OpenMode.ForRead, false, true) as AcEntity;
                var curve = entity as Curve;
                if (curve == null) continue;
                // FTF's own annotation is not survey linework; lines drafted on purpose
                // (FTFDRAWLINE, utility pipes) are.
                var stamp = Ownership.Read(entity);
                if (stamp != null && stamp.Kind != FtfEntityKind.DraftLine && stamp.Kind != FtfEntityKind.UtilityPipe) continue;

                string figure = null;
                var survey = entity as SurveyFigure;
                if (survey != null) figure = survey.Name;
                else
                {
                    var feature = entity as FeatureLine;
                    if (feature != null) figure = feature.Name;
                }
                var role = std.RoleFor(entity.Layer, figure);
                if (role == LineRole.Other) continue;

                var line = new OrientLine
                {
                    Id = !string.IsNullOrWhiteSpace(figure) ? figure : entity.Layer + " " + entity.Handle,
                    Layer = entity.Layer,
                    Role = role
                };
                foreach (var p in Sample(curve)) line.Points.Add(new P2(p.X, p.Y));
                if (line.Points.Count >= 2) scene.Lines.Add(line);
            }
        }

        /// <summary>Points along a curve: its ends and every 2.5 ft between, so arcs and survey
        /// figures are followed, not chorded end to end.</summary>
        private static IEnumerable<Point3d> Sample(Curve curve)
        {
            var points = new List<Point3d>();
            try
            {
                if (curve is Line) return new[] { curve.StartPoint, curve.EndPoint };
                var length = curve.GetDistanceAtParameter(curve.EndParam);
                var step = Math.Max(2.5, length / 4000.0);
                for (var s = 0.0; s < length; s += step) points.Add(curve.GetPointAtDist(s));
                points.Add(curve.EndPoint);
            }
            catch (Autodesk.AutoCAD.Runtime.Exception) { }
            return points;
        }

        // ------------------------------------------------------------------ applying

        private static Dictionary<OrientationDecision, OrientOutcome> Apply(Database db, Transaction tr, OrientationStandard std,
            OrientationResult result, IDictionary<string, CogoPoint> points, bool preview, bool overwriteExisting)
        {
            var outcomes = new Dictionary<OrientationDecision, OrientOutcome>();
            if (!preview) EnsureApp(db, tr);
            foreach (var d in result.Decisions)
            {
                CogoPoint cogo;
                if (!points.TryGetValue(d.Feature.Id, out cogo)) continue;
                if (d.Symbol == null) { outcomes[d] = OrientOutcome.NoSymbol; continue; }
                if (!d.Symbol.Directional || d.Tier == OrientationTier.NotDirectional) { outcomes[d] = OrientOutcome.NotDirectional; continue; }
                if (d.Tier == OrientationTier.Review) { outcomes[d] = OrientOutcome.Review; continue; }
                if (!d.Applies(std.ApplyMinimumConfidence, std.ApplyWeakestTier)) { outcomes[d] = OrientOutcome.BelowThreshold; continue; }

                var record = Record.Read(cogo);
                var current = cogo.MarkerRotation;
                var target = d.MarkerRotation.Value;

                // Turned by hand since FTF set it, or turned before FTF ever did (by hand or by a
                // description key): kept, unless the crew measured it or the user asked for All.
                var touched = record != null
                    ? !Same(current, record.Applied)
                    : Math.Abs(Angles.NormalizeRadians(current)) > 1e-9 && !Same(current, target);
                if (touched && !overwriteExisting && d.Tier != OrientationTier.Measured)
                {
                    d.Evidence.Add(new Evidence(OrientationTier.Review, 0, "marker already turned to " +
                        Angles.ToDegrees(Angles.NormalizeRadians(current)).ToString("0.#", CultureInfo.InvariantCulture) + " deg, not by FTF: kept (FTFORIENT All to replace)"));
                    outcomes[d] = OrientOutcome.KeptByHand;
                    continue;
                }
                if (preview) { outcomes[d] = OrientOutcome.Preview; continue; }
                if (Same(current, target) && record != null) { outcomes[d] = OrientOutcome.AlreadyRight; continue; }

                try
                {
                    var w = (CogoPoint)tr.GetObject(cogo.ObjectId, OpenMode.ForWrite);
                    var originalMarker = record != null ? record.OriginalMarker : current;
                    var originalLabel = record != null ? record.OriginalLabel : w.LabelRotation;
                    w.MarkerRotation = target;
                    if (d.Symbol.RotateLabel) w.LabelRotation = target;
                    Record.Write(w, target, originalMarker, originalLabel, d);
                    outcomes[d] = Same(current, target) ? OrientOutcome.AlreadyRight : OrientOutcome.Rotated;
                }
                catch (Autodesk.AutoCAD.Runtime.Exception)
                {
                    outcomes[d] = OrientOutcome.Failed;   // a point locked by its group or style
                }
            }
            return outcomes;
        }

        private static bool Same(double a, double b)
        {
            var d = Math.Abs(Angles.NormalizeRadians(a) - Angles.NormalizeRadians(b));
            return d < 1e-6 || Math.Abs(d - 2 * Math.PI) < 1e-6;
        }

        private static void EnsureApp(Database db, Transaction tr)
        {
            var table = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
            if (table.Has(XApp)) return;
            table.UpgradeOpen();
            using (var record = new RegAppTableRecord())
            {
                record.Name = XApp;
                table.Add(record);
                tr.AddNewlyCreatedDBObject(record, true);
            }
        }

        /// <summary>What FTFORIENT keeps on a point it turned: the rotation it set, the rotations
        /// before its first change, and why. Its own application name, never FTF's ownership
        /// stamp: FTF does not own COGO points, and FTFCLEAN must never erase one.</summary>
        private sealed class Record
        {
            public double Applied;
            public double OriginalMarker;
            public double OriginalLabel;

            public static Record Read(CogoPoint cogo)
            {
                ResultBuffer rb;
                try { rb = cogo.GetXDataForApplication(XApp); }
                catch (Autodesk.AutoCAD.Runtime.Exception) { return null; }
                if (rb == null) return null;
                var reals = rb.Cast<TypedValue>().Where(v => v.TypeCode == (int)DxfCode.ExtendedDataReal)
                              .Select(v => Convert.ToDouble(v.Value, CultureInfo.InvariantCulture)).ToList();
                if (reals.Count < 3) return null;
                return new Record { Applied = reals[0], OriginalMarker = reals[1], OriginalLabel = reals[2] };
            }

            public static void Write(CogoPoint cogo, double applied, double originalMarker, double originalLabel, OrientationDecision d)
            {
                cogo.XData = new ResultBuffer(
                    new TypedValue((int)DxfCode.ExtendedDataRegAppName, XApp),
                    new TypedValue((int)DxfCode.ExtendedDataReal, applied),
                    new TypedValue((int)DxfCode.ExtendedDataReal, originalMarker),
                    new TypedValue((int)DxfCode.ExtendedDataReal, originalLabel),
                    new TypedValue((int)DxfCode.ExtendedDataAsciiString, d.Tier.ToString()),
                    new TypedValue((int)DxfCode.ExtendedDataReal, d.Confidence),
                    new TypedValue((int)DxfCode.ExtendedDataReal, d.Azimuth ?? 0.0),
                    new TypedValue((int)DxfCode.ExtendedDataAsciiString, Clip(d.Association ?? "", 200)));
            }

            private static string Clip(string s, int n) { return s.Length <= n ? s : s.Substring(0, n); }
        }

        // ------------------------------------------------------------------ QC graphics

        private static int DrawQc(Database db, Transaction tr, string version, OrientationStandard std, OrientationResult result)
        {
            Ownership.DeleteOwnedKinds(db, tr, FtfEntityKind.OrientQc);
            Ownership.EnsureRegApp(db, tr);
            var scale = CadUtil.DrawingUnitsPerPlottedUnit(db);
            var length = std.QcArrowPlotted * scale;
            var textHeight = 0.06 * scale;
            var layer = NonPlotLayer(db, tr, std.QcLayer);
            var inferredLayer = NonPlotLayer(db, tr, std.QcInferredLayer);
            var count = 0;

            foreach (var d in result.Decisions.Where(x => x.Tier != OrientationTier.NotDirectional))
            {
                var p = d.Feature.Location;
                var color = ColorFor(d.Tier);
                if (d.Azimuth.HasValue)
                {
                    count += Arrow(db, tr, version, d.Feature.Id, layer, color, p, d.Azimuth.Value, length,
                                   d.Symbol != null && d.Symbol.Period > 0 && d.Symbol.Period < 360);
                }
                if (d.AlternativeAzimuth.HasValue)
                    count += Arrow(db, tr, version, d.Feature.Id, layer, 1, p, d.AlternativeAzimuth.Value, length * 0.7, false);
                if (d.Tier == OrientationTier.Review)
                {
                    using (var c = new Circle(new Point3d(p.X, p.Y, 0), Vector3d.ZAxis, length * 0.35))
                        count += Add(db, tr, version, d.Feature.Id, c, layer, 1);
                }
                if (d.AssociatedPoint.HasValue)
                {
                    var q = d.AssociatedPoint.Value;
                    using (var l = new Line(new Point3d(p.X, p.Y, 0), new Point3d(q.X, q.Y, 0)))
                        count += Add(db, tr, version, d.Feature.Id, l, layer, 8);
                }
                var tag = d.Tier == OrientationTier.Review ? "?" : d.Tier.ToString().Substring(0, 1) + " " + d.Confidence.ToString(".00", CultureInfo.InvariantCulture);
                count += Text(db, tr, version, d.Feature.Id, layer, color, new P2(p.X + length * 0.15, p.Y - length * 0.3), tag, textHeight);
            }

            foreach (var c in result.Connections)
            {
                using (var l = new Line(new Point3d(c.From.X, c.From.Y, 0), new Point3d(c.To.X, c.To.Y, 0)))
                    count += Add(db, tr, version, string.Join(" ", c.FeatureIds.ToArray()), l, inferredLayer, 30);
                var mid = new P2((c.From.X + c.To.X) / 2, (c.From.Y + c.To.Y) / 2);
                count += Text(db, tr, version, "", inferredLayer, 30, mid, c.Kind.ToUpperInvariant(), textHeight);
            }

            foreach (var node in result.Intersections)
            {
                using (var c = new Circle(new Point3d(node.Center.X, node.Center.Y, 0), Vector3d.ZAxis, length * 0.25))
                    count += Add(db, tr, version, "", c, layer, 8);
                foreach (var leg in node.Approaches.Where(a => a.InboundAllowed))
                {
                    var start = node.Center + leg.Outward * (length * 2.5);
                    count += Arrow(db, tr, version, "", layer, 8, start, SymbolRotation.AzimuthOf(leg.Inbound), length * 0.8, false);
                }
            }
            return count;
        }

        private static short ColorFor(OrientationTier tier)
        {
            switch (tier)
            {
                case OrientationTier.Measured: return 3;       // green
                case OrientationTier.Relationship: return 4;   // cyan
                case OrientationTier.Alignment: return 5;      // blue
                case OrientationTier.Inferred: return 2;       // yellow
                default: return 1;                             // red: review
            }
        }

        private static int Arrow(Database db, Transaction tr, string version, string id, ObjectId layer, short color, P2 from, double azimuth, double length, bool bothWays)
        {
            var dir = SymbolRotation.DirectionOf(azimuth);
            var tip = from + dir * length;
            var tail = bothWays ? from - dir * length : from;
            var left = new P2(-dir.Y, dir.X);
            var head = length * 0.25;
            var count = 0;
            foreach (var t in bothWays ? new[] { Tuple.Create(tip, dir), Tuple.Create(tail, dir * -1) } : new[] { Tuple.Create(tip, dir) })
            {
                using (var pl = new Polyline())
                {
                    var a = t.Item1 - t.Item2 * head + new P2(-t.Item2.Y, t.Item2.X) * (head * 0.5);
                    var b = t.Item1 - t.Item2 * head - new P2(-t.Item2.Y, t.Item2.X) * (head * 0.5);
                    pl.AddVertexAt(0, new Point2d(a.X, a.Y), 0, 0, 0);
                    pl.AddVertexAt(1, new Point2d(t.Item1.X, t.Item1.Y), 0, 0, 0);
                    pl.AddVertexAt(2, new Point2d(b.X, b.Y), 0, 0, 0);
                    count += Add(db, tr, version, id, pl, layer, color);
                }
            }
            using (var shaft = new Line(new Point3d(tail.X, tail.Y, 0), new Point3d(tip.X, tip.Y, 0)))
                count += Add(db, tr, version, id, shaft, layer, color);
            return count;
        }

        private static int Text(Database db, Transaction tr, string version, string id, ObjectId layer, short color, P2 at, string text, double height)
        {
            using (var m = new MText())
            {
                m.SetDatabaseDefaults(db);
                m.Contents = text;
                m.TextHeight = height;
                m.Location = new Point3d(at.X, at.Y, 0);
                return Add(db, tr, version, id, m, layer, color);
            }
        }

        private static int Add(Database db, Transaction tr, string version, string id, AcEntity e, ObjectId layer, short color)
        {
            e.SetDatabaseDefaults(db);
            e.LayerId = layer;
            e.ColorIndex = color;
            CadUtil.AddToModelSpace(db, tr, e);
            Ownership.Stamp(e, id, version, FtfEntityKind.OrientQc, null);
            return 1;
        }

        private static ObjectId NonPlotLayer(Database db, Transaction tr, string name)
        {
            var id = CadUtil.EnsureLayer(db, tr, name);
            var layer = (LayerTableRecord)tr.GetObject(id, OpenMode.ForRead);
            if (layer.IsPlottable)
            {
                layer.UpgradeOpen();
                layer.IsPlottable = false;
            }
            return id;
        }

        private static string ReportPath(Database db, FieldCodes.Settings.FtfSettings settings)
        {
            if (string.IsNullOrWhiteSpace(db.Filename)) return null;
            var name = Path.GetFileNameWithoutExtension(db.Filename) + OrientationReport.Suffix;
            var folder = settings.General.ReportLocation == FieldCodes.Settings.ReportLocation.CustomFolder &&
                         !string.IsNullOrWhiteSpace(settings.General.ReportFolder)
                ? settings.General.ReportFolder
                : Path.GetDirectoryName(db.Filename);
            return string.IsNullOrWhiteSpace(folder) ? null : Path.Combine(folder, name);
        }
    }
}
