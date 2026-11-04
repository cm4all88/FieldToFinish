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
using FieldCodes.Review;

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
    /// words). Nothing else -- no blocks inserted, no coordinates, descriptions or linework
    /// touched. The rotation before the first change is kept on the point (XData FTF_ORIENT), so
    /// FTFORIENTRESET puts every symbol back. A marker turned by hand since FTF last set it is
    /// kept; a rotation a drafter set or accepted with FTFORIENTPOINT is never changed by
    /// FTFORIENT. QC graphics go on non-plotting layers (only for what needs review, unless the
    /// office asks for all); inferred pipe connections are on the review list and in the report,
    /// and drawn only in Preview or when the office turns that on. The full decision list goes to
    /// a CSV beside the drawing.
    /// </summary>
    public sealed class OrientationCommands
    {
        internal const string XApp = "FTF_ORIENT";
        private const string ManualTag = "manual";

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
                    if (PutBack(tr, cogo)) restored++;
                var qc = Ownership.DeleteOwnedKinds(db, tr, FtfEntityKind.OrientQc);
                ed.WriteMessage("\nFTFORIENTRESET: {0} symbol(s) put back as they were; {1} QC object(s) removed.\n", restored, qc);
            });
        }

        /// <summary>The Process Drawing stage: Apply, without prompting.</summary>
        internal static void ApplyForPipeline()
        {
            Run(false, false);
        }

        // ------------------------------------------------------------------ the scene

        private sealed class Context
        {
            public RulesConfig Config;
            public FieldCodes.Settings.FtfSettings Settings;
            public OrientationStandard Standard;
            public OrientationScene Scene;
            public Dictionary<string, CogoPoint> Points;
        }

        private static Context Build(Database db, Transaction tr)
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
                // "ASTO S" may reach Civil 3D's full description as "STRAIGHT ARROW S": a
                // written direction of travel is read from either.
                try { f.FullDescription = cogo.FullDescription; }
                catch (Autodesk.AutoCAD.Runtime.Exception) { }

                // The office sign rule reads its number as the field azimuth ("SN 135").
                var parsed = notes.Parse(parser, cogo);
                // Its drafting standard: a feature whose standard says outright not to turn it by
                // inference (an address or private sign) is turned only by a recorded direction.
                // A standard that says nothing leaves FTFORIENT's own rules to decide.
                var requirement = FieldCodes.Standards.FeatureRequirements.For(cfg, parsed, raw, f.Note);
                if (requirement.RotationDeclined) f.InferFacing = false;
                if (parsed.RotationDegrees.HasValue && parsed.RotationIsAzimuth && f.Kind != OrientKind.Other)
                {
                    f.MeasuredAzimuth = Angles.NormalizeDegrees(90.0 - parsed.RotationDegrees.Value);
                    f.MeasuredFrom = "field azimuth in the description (" + raw + ")";
                }
                scene.Features.Add(f);
                points[number] = cogo;
            }

            ReadLinework(db, tr, std, scene);
            return new Context { Config = cfg, Settings = settings, Standard = std, Scene = scene, Points = points };
        }

        private static void Run(bool preview, bool overwriteExisting)
        {
            FtfSession.Run("FTFORIENT", (db, tr, ed) =>
            {
                var c = Build(db, tr);
                var std = c.Standard;
                var result = new OrientationEngine(std).Run(c.Scene);
                var outcomes = Apply(db, tr, std, result, c.Points, preview ? ApplyMode.Preview : ApplyMode.Write, overwriteExisting, null);
                if (!preview)
                {
                    var review = ReportReview(db, tr, c, result, outcomes);
                    if (review > 0)
                        ed.WriteMessage("\nFTFORIENT: {0} item(s) on the review list (FTFREVIEW).", review);
                }
                var qc = DrawQc(db, tr, c.Config.Version, std, result, outcomes, preview);
                // Blocks FTF inserted whose rule says "follow the marker" turn with it now, not
                // only on the next FTFPOINTS.
                var followed = preview ? 0 : InsertedBlocks.FollowMarkers(db, tr);
                if (followed > 0) ed.WriteMessage("\nFTFORIENT: {0} inserted block(s) turned with their marker.", followed);

                ed.WriteMessage("\nFTFORIENT{0}: {1} road/utility line(s) read.", preview ? " (preview: nothing rotated)" : "",
                                c.Scene.Lines.Count);
                foreach (var line in OrientationReport.Summary(result, outcomes)) ed.WriteMessage("\n" + line);
                ed.WriteMessage("\n{0} QC object(s) on {1} / {2} (not plotted; {3}).", qc, std.QcLayer, std.QcInferredLayer, QcWords(std, preview));
                WriteReport(db, ed, c.Settings, result, outcomes);
                ed.WriteMessage("\nFTFORIENTRESET puts every symbol back. FTFORIENTPOINT shows why one point was turned (or not).\n");
            });
        }

        private static string QcWords(OrientationStandard std, bool preview)
        {
            if (preview) return "preview: every decision and inferred connection";
            switch (std.QcGraphics)
            {
                case QcGraphicsMode.Off: return "QC graphics are off";
                case QcGraphicsMode.All: return "every decision" + (std.QcShowInferredConnections ? " and inferred connection" : "");
                default: return "only what needs review" + (std.QcShowInferredConnections ? ", and inferred connections" : "");
            }
        }

        private static void WriteReport(Database db, Editor ed, FieldCodes.Settings.FtfSettings settings, OrientationResult result,
                                        IDictionary<OrientationDecision, OrientOutcome> outcomes)
        {
            var path = ReportPath(db, settings);
            if (path == null) return;
            try
            {
                File.WriteAllText(path, OrientationReport.Csv(result, outcomes));
                ed.WriteMessage("\nEvery decision and its evidence: {0}", path);
            }
            catch (IOException ex) { ed.WriteMessage("\nCould not write {0}: {1}", path, ex.Message); }
            catch (UnauthorizedAccessException ex) { ed.WriteMessage("\nCould not write {0}: {1}", path, ex.Message); }
        }

        /// <summary>Arrows whose direction could not be decided, symbols needing review, points
        /// that could not be turned, and inferred connections: on the review list until a drafter
        /// deals with them.</summary>
        private static int ReportReview(Database db, Transaction tr, Context c, OrientationResult result,
                                        IDictionary<OrientationDecision, OrientOutcome> outcomes)
        {
            var handles = c.Points.ToDictionary(p => p.Key, p => p.Value.Handle.ToString(), StringComparer.OrdinalIgnoreCase);
            var review = ReviewChecks.Orientation(result, outcomes, handles);
            ReviewStore.Report(db, tr, ReviewChecks.OrientationSource, review, true, c.Settings.Review);
            return review.Count;
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
                // Only road, utility, stop-bar, crosswalk and sign-panel lines by their layer or
                // figure code: fences, property lines and anything else are never read.
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

        /// <summary>Preview: nothing changes. Evaluate: the outcomes Write would have, without
        /// writing (for FTFORIENTPOINT's review refresh). Write: turn the markers.</summary>
        private enum ApplyMode { Preview, Evaluate, Write }

        private static Dictionary<OrientationDecision, OrientOutcome> Apply(Database db, Transaction tr, OrientationStandard std,
            OrientationResult result, IDictionary<string, CogoPoint> points, ApplyMode mode, bool overwriteExisting, string onlyPoint)
        {
            var outcomes = new Dictionary<OrientationDecision, OrientOutcome>();
            if (mode == ApplyMode.Write) EnsureApp(db, tr);
            foreach (var d in result.Decisions)
            {
                CogoPoint cogo;
                if (!points.TryGetValue(d.Feature.Id, out cogo)) continue;
                var record = Record.Read(cogo);
                var write = mode == ApplyMode.Write && (onlyPoint == null || string.Equals(onlyPoint, d.Feature.Id, StringComparison.OrdinalIgnoreCase));

                // Set or accepted by a drafter (FTFORIENTPOINT): never changed by FTFORIENT, even
                // with All; FTFORIENTPOINT Recheck or FTFORIENTRESET hands it back.
                if (record != null && record.Manual) { outcomes[d] = OrientOutcome.ManualOverride; continue; }
                if (d.Symbol == null) { outcomes[d] = OrientOutcome.NoSymbol; continue; }
                if (!d.Symbol.Directional || d.Tier == OrientationTier.NotDirectional) { outcomes[d] = OrientOutcome.NotDirectional; continue; }
                if (d.Tier == OrientationTier.Review) { outcomes[d] = OrientOutcome.Review; continue; }
                if (d.Tier == OrientationTier.NoSupport) { outcomes[d] = OrientOutcome.LeftAsSurveyed; continue; }
                if (!d.Applies(std.ApplyMinimumConfidence, std.ApplyWeakestTier)) { outcomes[d] = OrientOutcome.BelowThreshold; continue; }

                var current = cogo.MarkerRotation;
                var target = d.MarkerRotation.Value;

                // Turned by hand since FTF set it, or turned before FTF ever did (by hand or by a
                // description key): kept, unless the user asked for All, or the crew measured it
                // and the office lets a measurement replace a hand rotation.
                var touched = record != null
                    ? !Same(current, record.Applied)
                    : Math.Abs(Angles.NormalizeRadians(current)) > 1e-9 && !Same(current, target);
                var measuredReplaces = std.HandTurned == HandTurnedRotation.MeasuredReplaces && d.Tier == OrientationTier.Measured;
                if (touched && !overwriteExisting && !measuredReplaces)
                {
                    d.Evidence.Add(new Evidence(OrientationTier.Review, 0, "marker already turned to " +
                        Angles.ToDegrees(Angles.NormalizeRadians(current)).ToString("0.#", CultureInfo.InvariantCulture) + " deg, not by FTF: kept (FTFORIENT All to replace)"));
                    outcomes[d] = OrientOutcome.KeptByHand;
                    continue;
                }
                if (mode == ApplyMode.Preview) { outcomes[d] = OrientOutcome.Preview; continue; }
                if (Same(current, target) && record != null) { outcomes[d] = OrientOutcome.AlreadyRight; continue; }
                if (!write) { outcomes[d] = Same(current, target) ? OrientOutcome.AlreadyRight : OrientOutcome.Rotated; continue; }

                try
                {
                    var w = (CogoPoint)tr.GetObject(cogo.ObjectId, OpenMode.ForWrite);
                    var originalMarker = record != null ? record.OriginalMarker : current;
                    var originalLabel = record != null ? record.OriginalLabel : w.LabelRotation;
                    w.MarkerRotation = target;
                    if (d.Symbol.RotateLabel) w.LabelRotation = target;
                    Record.Write(w, target, originalMarker, originalLabel, d, false);
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

        /// <summary>Puts one point's marker and label back as they were before FTF first turned them.</summary>
        private static bool PutBack(Transaction tr, CogoPoint cogo)
        {
            var record = Record.Read(cogo);
            if (record == null) return false;
            try
            {
                var w = (CogoPoint)tr.GetObject(cogo.ObjectId, OpenMode.ForWrite);
                w.MarkerRotation = record.OriginalMarker;
                w.LabelRotation = record.OriginalLabel;
                w.XData = new ResultBuffer(new TypedValue((int)DxfCode.ExtendedDataRegAppName, XApp));
                return true;
            }
            catch (Autodesk.AutoCAD.Runtime.Exception) { return false; }
        }

        /// <summary>What FTFORIENT keeps on a point it turned: the rotation it set, the rotations
        /// before its first change, why, and whether a drafter set it. Its own application name,
        /// never FTF's ownership stamp: FTF does not own COGO points, and FTFCLEAN must never erase
        /// one.</summary>
        internal sealed class Record
        {
            public double Applied;
            public double OriginalMarker;
            public double OriginalLabel;
            public string Tier;
            public double Confidence;
            public string Association;
            public bool Manual;

            public static Record Read(CogoPoint cogo)
            {
                ResultBuffer rb;
                try { rb = cogo.GetXDataForApplication(XApp); }
                catch (Autodesk.AutoCAD.Runtime.Exception) { return null; }
                if (rb == null) return null;
                var values = rb.Cast<TypedValue>().ToList();
                var reals = values.Where(v => v.TypeCode == (int)DxfCode.ExtendedDataReal)
                                  .Select(v => Convert.ToDouble(v.Value, CultureInfo.InvariantCulture)).ToList();
                var texts = values.Where(v => v.TypeCode == (int)DxfCode.ExtendedDataAsciiString).Select(v => Convert.ToString(v.Value, CultureInfo.InvariantCulture)).ToList();
                if (reals.Count < 3) return null;
                return new Record
                {
                    Applied = reals[0], OriginalMarker = reals[1], OriginalLabel = reals[2],
                    Tier = texts.Count > 0 ? texts[0] : null,
                    Confidence = reals.Count > 3 ? reals[3] : 0,
                    Association = texts.Count > 1 ? texts[1] : null,
                    Manual = texts.Any(t => string.Equals(t, ManualTag, StringComparison.OrdinalIgnoreCase))
                };
            }

            public static void Write(CogoPoint cogo, double applied, double originalMarker, double originalLabel, OrientationDecision d, bool manual)
            {
                cogo.XData = new ResultBuffer(
                    new TypedValue((int)DxfCode.ExtendedDataRegAppName, XApp),
                    new TypedValue((int)DxfCode.ExtendedDataReal, applied),
                    new TypedValue((int)DxfCode.ExtendedDataReal, originalMarker),
                    new TypedValue((int)DxfCode.ExtendedDataReal, originalLabel),
                    new TypedValue((int)DxfCode.ExtendedDataAsciiString, d != null ? d.Tier.ToString() : "Manual"),
                    new TypedValue((int)DxfCode.ExtendedDataReal, d != null ? d.Confidence : 1.0),
                    new TypedValue((int)DxfCode.ExtendedDataReal, d != null ? (d.Azimuth ?? 0.0) : 0.0),
                    new TypedValue((int)DxfCode.ExtendedDataAsciiString, Clip(d != null ? (d.Association ?? "") : "set by a drafter", 200)),
                    new TypedValue((int)DxfCode.ExtendedDataAsciiString, manual ? ManualTag : "auto"));
            }

            private static string Clip(string s, int n) { return s.Length <= n ? s : s.Substring(0, n); }
        }

        // ------------------------------------------------------------------ QC graphics

        /// <summary>
        /// Non-plotting QC graphics, all owned (OrientQc) so FTFORIENT and FTFORIENTRESET remove
        /// them and nothing surveyed is touched. Sized from plotted heights through the drawing's
        /// annotation scale, never from the length of what they mark. By default only what needs
        /// review is drawn: an arrow for the suggestion that starts beside the symbol (so it is
        /// never mistaken for the symbol), a circle and a "?". Inferred pipe connections are drawn
        /// only in Preview or when the office turns them on, dashed, with a short tag.
        /// </summary>
        private static int DrawQc(Database db, Transaction tr, string version, OrientationStandard std, OrientationResult result,
                                  IDictionary<OrientationDecision, OrientOutcome> outcomes, bool preview)
        {
            Ownership.DeleteOwnedKinds(db, tr, FtfEntityKind.OrientQc);
            var mode = preview ? QcGraphicsMode.All : std.QcGraphics;
            if (mode == QcGraphicsMode.Off) return 0;
            Ownership.EnsureRegApp(db, tr);
            var scale = CadUtil.DrawingUnitsPerPlottedUnit(db);
            var length = std.QcArrowPlotted * scale;
            var textHeight = (std.QcTextPlotted > 0 ? std.QcTextPlotted : 0.06) * scale;
            var layer = NonPlotLayer(db, tr, std.QcLayer);
            var count = 0;

            foreach (var d in result.Decisions.Where(x => x.Tier != OrientationTier.NotDirectional))
            {
                OrientOutcome outcome;
                outcomes.TryGetValue(d, out outcome);
                if (outcome == OrientOutcome.NoSymbol || outcome == OrientOutcome.NotDirectional) continue;
                var open = d.Tier == OrientationTier.Review || outcome == OrientOutcome.Review || outcome == OrientOutcome.BelowThreshold ||
                           outcome == OrientOutcome.Failed;
                if (mode == QcGraphicsMode.ReviewOnly && !open) continue;
                count += DrawDecision(db, tr, version, layer, d, length, textHeight, open);
            }

            if (preview || std.QcShowInferredConnections)
            {
                var inferredLayer = NonPlotLayer(db, tr, std.QcInferredLayer);
                foreach (var c in result.Connections) count += DrawConnection(db, tr, version, inferredLayer, c, textHeight);
            }

            if (mode == QcGraphicsMode.All)
                foreach (var node in result.Intersections)
                {
                    using (var c = new Circle(new Point3d(node.Center.X, node.Center.Y, 0), Vector3d.ZAxis, length * 0.25))
                        count += Add(db, tr, version, "", c, layer, 8);
                    foreach (var leg in node.Approaches.Where(a => a.InboundAllowed))
                    {
                        var start = node.Center + leg.Outward * (length * 2.5);
                        count += Arrow(db, tr, version, "", layer, 8, start, SymbolRotation.AzimuthOf(leg.Inbound), length * 0.8, false, 0);
                    }
                }
            return count;
        }

        private static int DrawDecision(Database db, Transaction tr, string version, ObjectId layer, OrientationDecision d,
                                        double length, double textHeight, bool open)
        {
            var p = d.Feature.Location;
            var color = open ? (short)1 : ColorFor(d.Tier);
            var count = 0;
            var gap = length * 0.4;
            if (d.Azimuth.HasValue)
                count += Arrow(db, tr, version, d.Feature.Id, layer, color, p, d.Azimuth.Value, length,
                               d.Symbol != null && d.Symbol.Period > 0 && d.Symbol.Period < 360, gap);
            if (d.AlternativeAzimuth.HasValue)
                count += Arrow(db, tr, version, d.Feature.Id, layer, 1, p, d.AlternativeAzimuth.Value, length * 0.7, false, gap);
            if (open)
            {
                using (var c = new Circle(new Point3d(p.X, p.Y, 0), Vector3d.ZAxis, length * 0.35))
                    count += Add(db, tr, version, d.Feature.Id, c, layer, 1);
            }
            var tag = open ? "?" : d.Tier.ToString().Substring(0, 1) + " " + d.Confidence.ToString(".00", CultureInfo.InvariantCulture);
            count += Text(db, tr, version, d.Feature.Id, layer, color, new P2(p.X + length * 0.4, p.Y - length * 0.45), tag, textHeight, 0);
            return count;
        }

        /// <summary>A probable pipe connection: a dashed line between the points it was inferred
        /// from, and a short tag beside its middle along the line. Nothing longer, never on a
        /// plotting layer, never utility linework.</summary>
        private static int DrawConnection(Database db, Transaction tr, string version, ObjectId layer, InferredConnection c, double textHeight)
        {
            var id = string.Join(" ", c.FeatureIds.ToArray());
            var count = 0;
            using (var l = new Line(new Point3d(c.From.X, c.From.Y, 0), new Point3d(c.To.X, c.To.Y, 0)))
            {
                var dashed = Dashed(db, tr);
                if (!dashed.IsNull)
                {
                    l.LinetypeId = dashed;
                    l.LinetypeScale = Math.Max(0.01, textHeight);
                }
                count += Add(db, tr, version, id, l, layer, 30);
            }
            var span = c.To - c.From;
            if (span.Length < 1e-6) return count;
            var along = span.Normalized();
            var side = new P2(-along.Y, along.X);
            var angle = Math.Atan2(along.Y, along.X);
            if (angle > Math.PI / 2 || angle <= -Math.PI / 2) angle += Math.PI;           // reads left to right
            var mid = new P2((c.From.X + c.To.X) / 2, (c.From.Y + c.To.Y) / 2) + side * (textHeight * 0.8);
            var tag = (c.Kind ?? "connection").Replace(" (inferred)", "") + "?";
            count += Text(db, tr, version, id, layer, 30, mid, tag, textHeight * 0.8, angle);
            return count;
        }

        private static ObjectId Dashed(Database db, Transaction tr)
        {
            var table = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
            foreach (var name in new[] { "HIDDEN", "DASHED", "HIDDEN2", "DASHED2" })
                if (table.Has(name)) return table[name];
            return ObjectId.Null;
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

        /// <summary>A direction arrow that starts <paramref name="gap"/> out from the feature, so it
        /// is read as an indicator beside the symbol, not as the symbol.</summary>
        private static int Arrow(Database db, Transaction tr, string version, string id, ObjectId layer, short color, P2 from, double azimuth, double length, bool bothWays, double gap)
        {
            var dir = SymbolRotation.DirectionOf(azimuth);
            var head = length * 0.25;
            var count = 0;
            var ways = bothWays ? new[] { dir, dir * -1 } : new[] { dir };
            foreach (var w in ways)
            {
                var start = from + w * gap;
                var tip = start + w * length;
                using (var pl = new Polyline())
                {
                    var a = tip - w * head + new P2(-w.Y, w.X) * (head * 0.5);
                    var b = tip - w * head - new P2(-w.Y, w.X) * (head * 0.5);
                    pl.AddVertexAt(0, new Point2d(a.X, a.Y), 0, 0, 0);
                    pl.AddVertexAt(1, new Point2d(tip.X, tip.Y), 0, 0, 0);
                    pl.AddVertexAt(2, new Point2d(b.X, b.Y), 0, 0, 0);
                    count += Add(db, tr, version, id, pl, layer, color);
                }
                using (var shaft = new Line(new Point3d(start.X, start.Y, 0), new Point3d(tip.X, tip.Y, 0)))
                    count += Add(db, tr, version, id, shaft, layer, color);
            }
            return count;
        }

        private static int Text(Database db, Transaction tr, string version, string id, ObjectId layer, short color, P2 at, string text, double height, double rotation)
        {
            using (var m = new MText())
            {
                m.SetDatabaseDefaults(db);
                m.Contents = text;
                m.TextHeight = height;
                m.Rotation = rotation;
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

        // ================================================================== one point

        /// <summary>
        /// FTFORIENTPOINT: one symbol's decision, and what a drafter can do about it. Shows the
        /// rotation now and before FTF, the rotation FTFORIENT would set, the evidence, the
        /// reference line and its distance, the confidence and whether it would be applied; then
        /// Zoom, Preview (a QC arrow, nothing turned), Apply (turn it to the suggestion), Override
        /// (set the direction yourself), Recheck (hand it back to FTFORIENT and decide again) or
        /// Reset (as it was before FTF). Apply and Override are kept by every later FTFORIENT. The
        /// review list's Fix runs this for the item's point.
        /// </summary>
        [CommandMethod("FTFORIENTPOINT", CommandFlags.Modal)]
        public void OrientPoint()
        {
            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;
            var number = AskPoint(doc);
            if (number == null) return;

            var explained = Explain(number);
            if (!explained) return;
            while (true)
            {
                var ask = new PromptKeywordOptions("\nPoint " + number + " [Zoom/Preview/Apply/Override/Recheck/Reset/eXit] <eXit>: ") { AllowNone = true };
                foreach (var k in new[] { "Zoom", "Preview", "Apply", "Override", "Recheck", "Reset", "eXit" }) ask.Keywords.Add(k);
                ask.Keywords.Default = "eXit";
                var answer = ed.GetKeywords(ask);
                if (answer.Status != PromptStatus.OK) return;
                switch (answer.StringResult)
                {
                    case "Zoom": ZoomToPoint(doc, number); break;
                    case "Preview": PointAction(number, "preview", null); break;
                    case "Apply": PointAction(number, "apply", null); break;
                    case "Override":
                        var az = AskDirection(doc, number);
                        if (az.HasValue) PointAction(number, "override", az);
                        break;
                    case "Recheck": PointAction(number, "recheck", null); Explain(number); break;
                    case "Reset": PointAction(number, "reset", null); break;
                    default: return;
                }
            }
        }

        private static string AskPoint(Autodesk.AutoCAD.ApplicationServices.Document doc)
        {
            var ed = doc.Editor;
            var ask = new PromptStringOptions("\nPoint number <select a point>: ") { AllowSpaces = false };
            var typed = ed.GetString(ask);
            if (typed.Status != PromptStatus.OK) return null;
            var number = (typed.StringResult ?? string.Empty).Trim();
            if (number.Length > 0) return number;
            var pick = new PromptEntityOptions("\nSelect a COGO point: ");
            pick.SetRejectMessage("\nThat is not a COGO point.");
            pick.AddAllowedClass(typeof(CogoPoint), true);
            var picked = ed.GetEntity(pick);
            if (picked.Status != PromptStatus.OK) return null;
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var cogo = tr.GetObject(picked.ObjectId, OpenMode.ForRead) as CogoPoint;
                tr.Commit();
                return cogo == null ? null : cogo.PointNumber.ToString(CultureInfo.InvariantCulture);
            }
        }

        /// <summary>The direction the drafter wants the symbol to point: an azimuth (degrees
        /// clockwise from north, as surveyed) or a point picked in the direction it points.</summary>
        private static double? AskDirection(Autodesk.AutoCAD.ApplicationServices.Document doc, string number)
        {
            var ed = doc.Editor;
            var how = new PromptKeywordOptions("\nSet its direction by [Azimuth/Pick] <Azimuth>: ") { AllowNone = true };
            how.Keywords.Add("Azimuth");
            how.Keywords.Add("Pick");
            how.Keywords.Default = "Azimuth";
            var answer = ed.GetKeywords(how);
            if (answer.Status != PromptStatus.OK && answer.Status != PromptStatus.None) return null;
            if (answer.Status == PromptStatus.OK && answer.StringResult == "Pick")
            {
                Point3d at;
                if (!PointLocation(doc, number, out at)) return null;
                var p = new PromptPointOptions("\nPick a point in the direction the symbol should point: ") { UseBasePoint = true, BasePoint = at };
                var picked = ed.GetPoint(p);
                if (picked.Status != PromptStatus.OK) return null;
                var v = new P2(picked.Value.X - at.X, picked.Value.Y - at.Y);
                if (v.Length < 1e-9) return null;
                return SymbolRotation.AzimuthOf(v.Normalized());
            }
            var deg = ed.GetDouble(new PromptDoubleOptions("\nAzimuth the symbol should point (degrees clockwise from north): ") { AllowNegative = true });
            if (deg.Status != PromptStatus.OK) return null;
            return Angles.NormalizeDegrees(deg.Value);
        }

        private static bool PointLocation(Autodesk.AutoCAD.ApplicationServices.Document doc, string number, out Point3d at)
        {
            at = Point3d.Origin;
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var cogo = FindPoint(doc.Database, tr, number);
                tr.Commit();
                if (cogo == null) return false;
                at = cogo.Location;
                return true;
            }
        }

        private static CogoPoint FindPoint(Database db, Transaction tr, string number)
        {
            return CadUtil.CogoPointsInOrder(db, tr)
                          .FirstOrDefault(c => string.Equals(c.PointNumber.ToString(CultureInfo.InvariantCulture), number, StringComparison.OrdinalIgnoreCase));
        }

        private static void ZoomToPoint(Autodesk.AutoCAD.ApplicationServices.Document doc, string number)
        {
            Point3d at;
            if (!PointLocation(doc, number, out at)) return;
            string handle = null;
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var cogo = FindPoint(doc.Database, tr, number);
                if (cogo != null) handle = cogo.Handle.ToString();
                tr.Commit();
            }
            var item = new ReviewItem { X = at.X, Y = at.Y };
            if (handle != null) item.Handles.Add(handle);
            ReviewStore.ZoomTo(doc, item);
        }

        /// <summary>Prints one point's decision and why. False when the point has no symbol FTFORIENT turns.</summary>
        private static bool Explain(string number)
        {
            var shown = false;
            FtfSession.Run("FTFORIENTPOINT", (db, tr, ed) =>
            {
                var c = Build(db, tr);
                CogoPoint cogo;
                if (!c.Points.TryGetValue(number, out cogo))
                {
                    var exists = FindPoint(db, tr, number) != null;
                    ed.WriteMessage(exists
                        ? "\nPoint {0}: its code has no symbol rule in Symbols and Rotations, so FTFORIENT never turns it.\n"
                        : "\nNo COGO point {0} in this drawing.\n", number);
                    return;
                }
                var result = new OrientationEngine(c.Standard).Run(c.Scene);
                var d = result.For(number);
                if (d == null) { ed.WriteMessage("\nPoint {0}: FTFORIENT makes no decision for it.\n", number); return; }
                var outcomes = Apply(db, tr, c.Standard, result, c.Points, ApplyMode.Evaluate, false, null);
                OrientOutcome outcome;
                outcomes.TryGetValue(d, out outcome);
                var record = Record.Read(cogo);
                foreach (var line in Describe(d, outcome, record, cogo.MarkerRotation, c.Standard)) ed.WriteMessage("\n" + line);
                foreach (var conn in result.Connections.Where(x => x.FeatureIds.Contains(number)))
                    ed.WriteMessage("\n  inferred:   {0}, confidence {1:0.00}: review only, never utility linework", conn.Kind, conn.Confidence);
                ed.WriteMessage("\n");
                shown = true;
            });
            return shown;
        }

        internal static IList<string> Describe(OrientationDecision d, OrientOutcome outcome, Record record, double currentRotation, OrientationStandard std)
        {
            var inv = CultureInfo.InvariantCulture;
            var lines = new List<string>();
            lines.Add(string.Format(inv, "Point {0}  {1}  ({2})", d.Feature.Id, d.Feature.Description, d.Feature.Kind));
            var now = Angles.ToDegrees(Angles.NormalizeRadians(currentRotation));
            if (record == null) lines.Add(string.Format(inv, "  marker now: {0:0.0} deg (FTF has not turned it)", now));
            else if (record.Manual) lines.Add(string.Format(inv, "  marker now: {0:0.0} deg, set by a drafter (FTFORIENT keeps it; Recheck hands it back); {1:0.0} deg before FTF",
                                                           now, Angles.ToDegrees(Angles.NormalizeRadians(record.OriginalMarker))));
            else lines.Add(string.Format(inv, "  marker now: {0:0.0} deg; FTF set {1:0.0}; {2:0.0} deg before FTF", now,
                                         Angles.ToDegrees(Angles.NormalizeRadians(record.Applied)), Angles.ToDegrees(Angles.NormalizeRadians(record.OriginalMarker))));
            lines.Add(string.Format(inv, "  decision:   {0}, confidence {1:0.00} (needs {2:0.00} and {3} or stronger to turn); source: {4}",
                                    d.Tier, d.Confidence, std.ApplyMinimumConfidence, std.ApplyWeakestTier, d.Source));
            if (d.Azimuth.HasValue)
            {
                var marker = d.MarkerRotation ?? (d.Symbol != null && d.Symbol.Directional ? SymbolRotation.MarkerRadians(d.Symbol, d.Azimuth.Value) : null);
                lines.Add(string.Format(inv, "  direction:  {0:0.0} deg azimuth{1}", d.Azimuth.Value,
                                        marker.HasValue ? string.Format(inv, " (marker rotation {0:0.0} deg)", Angles.ToDegrees(Angles.NormalizeRadians(marker.Value))) : ""));
            }
            if (d.AlternativeAzimuth.HasValue) lines.Add(string.Format(inv, "  or:         {0:0.0} deg azimuth", d.AlternativeAzimuth.Value));
            if (!string.IsNullOrEmpty(d.Reference))
                lines.Add(string.Format(inv, "  reference:  {0}", d.Reference));
            else if (!string.IsNullOrEmpty(d.Association))
                lines.Add(string.Format(inv, "  related to: {0}", d.Association));
            foreach (var e in d.Evidence) lines.Add("  why:        " + e.Text);
            if (!string.IsNullOrEmpty(d.ReviewReason)) lines.Add("  review:     " + d.ReviewReason);
            lines.Add("  FTFORIENT:  " + OutcomeWords(outcome));
            return lines;
        }

        private static string OutcomeWords(OrientOutcome o)
        {
            switch (o)
            {
                case OrientOutcome.Rotated: return "would turn it to this direction";
                case OrientOutcome.AlreadyRight: return "it already points this way";
                case OrientOutcome.KeptByHand: return "keeps it: the marker was turned by hand (All replaces hand rotations)";
                case OrientOutcome.BelowThreshold: return "leaves it: not sure enough to turn (Apply turns it to the suggestion)";
                case OrientOutcome.Review: return "leaves it for review (Apply takes the suggestion, Override sets your own)";
                case OrientOutcome.NotDirectional: return "never turns it: its symbol looks the same every way";
                case OrientOutcome.LeftAsSurveyed: return "leaves it as surveyed: its standard turns it only by a recorded direction";
                case OrientOutcome.ManualOverride: return "keeps it: set by a drafter";
                case OrientOutcome.Failed: return "could not turn it (the point may be locked by its group or style)";
                default: return o.ToString();
            }
        }

        /// <summary>One action on one point, then the review list is brought up to date.</summary>
        private static void PointAction(string number, string action, double? azimuth)
        {
            FtfSession.Run("FTFORIENTPOINT", (db, tr, ed) =>
            {
                var c = Build(db, tr);
                CogoPoint cogo;
                if (!c.Points.TryGetValue(number, out cogo)) { ed.WriteMessage("\nPoint {0} has no symbol FTFORIENT turns.\n", number); return; }
                var std = c.Standard;
                var result = new OrientationEngine(std).Run(c.Scene);
                var d = result.For(number);
                var record = Record.Read(cogo);
                EnsureApp(db, tr);
                switch (action)
                {
                    case "preview":
                    {
                        if (d == null || !d.Azimuth.HasValue) { ed.WriteMessage("\nNo direction to preview for point {0}.\n", number); return; }
                        Ownership.EnsureRegApp(db, tr);
                        var scale = CadUtil.DrawingUnitsPerPlottedUnit(db);
                        var n = DrawDecision(db, tr, c.Config.Version, NonPlotLayer(db, tr, std.QcLayer), d, std.QcArrowPlotted * scale,
                                             (std.QcTextPlotted > 0 ? std.QcTextPlotted : 0.06) * scale, d.Tier == OrientationTier.Review);
                        ed.WriteMessage("\nPreview: {0} QC object(s) on {1} (not plotted); the symbol was not turned.\n", n, std.QcLayer);
                        return;
                    }
                    case "apply":
                    {
                        if (d == null || !d.Azimuth.HasValue || d.Symbol == null || !d.Symbol.Directional)
                        { ed.WriteMessage("\nPoint {0} has no suggested direction to apply.\n", number); return; }
                        var target = d.MarkerRotation ?? SymbolRotation.MarkerRadians(d.Symbol, d.Azimuth.Value);
                        if (!target.HasValue) { ed.WriteMessage("\nPoint {0}'s symbol is not turned.\n", number); return; }
                        Turn(tr, cogo, record, target.Value, d, true, d.Symbol.RotateLabel);
                        ed.WriteMessage("\nPoint {0} turned to the suggestion ({1:0.0} deg azimuth); kept by later FTFORIENT runs.\n", number, d.Azimuth.Value);
                        break;
                    }
                    case "override":
                    {
                        var symbol = d != null ? d.Symbol : std.SymbolFor(OrientationStandard.BaseCode(cogo.RawDescription));
                        if (symbol == null || !symbol.Directional) { ed.WriteMessage("\nPoint {0}'s symbol is not turned (it looks the same every way).\n", number); return; }
                        var target = SymbolRotation.MarkerRadians(symbol, azimuth.Value);
                        if (!target.HasValue) return;
                        Turn(tr, cogo, record, target.Value, null, true, symbol.RotateLabel);
                        ed.WriteMessage("\nPoint {0} set to {1:0.0} deg azimuth by you; kept by later FTFORIENT runs.\n", number, azimuth.Value);
                        break;
                    }
                    case "recheck":
                    {
                        if (record != null && record.Manual)
                        {
                            var w = (CogoPoint)tr.GetObject(cogo.ObjectId, OpenMode.ForWrite);
                            // Handed back to FTFORIENT: the drafter's rotation stays until FTF decides.
                            Record.Write(w, w.MarkerRotation, record.OriginalMarker, record.OriginalLabel, d, false);
                        }
                        var o = Apply(db, tr, std, result, c.Points, ApplyMode.Write, false, number);
                        OrientOutcome mine;
                        if (d != null && o.TryGetValue(d, out mine)) ed.WriteMessage("\nPoint {0} rechecked: {1}.\n", number, OutcomeWords(mine));
                        break;
                    }
                    case "reset":
                        ed.WriteMessage(PutBack(tr, cogo) ? "\nPoint {0} put back as it was before FTF.\n" : "\nPoint {0} was never turned by FTF.\n", number);
                        break;
                }
                // Bring the review list up to date for every point, without turning anything else.
                var outcomes = Apply(db, tr, std, result, c.Points, ApplyMode.Evaluate, false, null);
                ReportReview(db, tr, c, result, outcomes);
            });
        }

        private static void Turn(Transaction tr, CogoPoint cogo, Record record, double target, OrientationDecision d, bool manual, bool rotateLabel)
        {
            var w = (CogoPoint)tr.GetObject(cogo.ObjectId, OpenMode.ForWrite);
            var originalMarker = record != null ? record.OriginalMarker : w.MarkerRotation;
            var originalLabel = record != null ? record.OriginalLabel : w.LabelRotation;
            w.MarkerRotation = target;
            if (rotateLabel) w.LabelRotation = target;
            Record.Write(w, target, originalMarker, originalLabel, d, manual);
        }
    }
}
