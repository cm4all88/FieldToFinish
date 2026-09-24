using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using FieldCodes.Easements;
using FieldCodes.RecordSurvey;
using FieldCodes.Settings;
using Newtonsoft.Json;

using AcEntity = Autodesk.AutoCAD.DatabaseServices.Entity;

namespace FieldCodes.Cad
{
    /// <summary>
    /// Source metadata written onto every reconstructed entity (extension dictionary
    /// FTF_RECORD), so the geometry can be traced to the recorded survey without the
    /// drawing's project record -- and so anything that reads the DWG can see it.
    /// </summary>
    public sealed class RecordEntityMetadata
    {
        [JsonProperty("document")] public string Document { get; set; }
        [JsonProperty("recordingNumber")] public string RecordingNumber { get; set; }
        [JsonProperty("sheet")] public string Sheet { get; set; }
        [JsonProperty("surveyType")] public string SurveyType { get; set; }
        [JsonProperty("project")] public string ProjectId { get; set; }
        [JsonProperty("call")] public string CallId { get; set; }
        [JsonProperty("figure")] public string Figure { get; set; }
        [JsonProperty("lot")] public string Lot { get; set; }
        [JsonProperty("block")] public string Block { get; set; }
        [JsonProperty("objectType")] public string ObjectType { get; set; }
        [JsonProperty("basis")] public string Basis { get; set; }
        [JsonProperty("recordSource")] public string RecordSource { get; set; }
        [JsonProperty("recordReference")] public string RecordReference { get; set; }
        [JsonProperty("measuredBearing")] public string MeasuredBearing { get; set; }
        [JsonProperty("measuredDistance")] public string MeasuredDistance { get; set; }
        [JsonProperty("recordBearing")] public string RecordBearing { get; set; }
        [JsonProperty("recordDistance")] public string RecordDistance { get; set; }
        [JsonProperty("curve")] public string Curve { get; set; }
        [JsonProperty("confidence")] public double Confidence { get; set; }
        [JsonProperty("sourcePage")] public int SourcePage { get; set; }
        [JsonProperty("sourceBox")] public PageBox SourceBox { get; set; }
        [JsonProperty("sourceText")] public string SourceText { get; set; }
        [JsonProperty("createdUtc")] public DateTime CreatedUtc { get; set; }
        [JsonProperty("sharedWith", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<string> SharedWith { get; set; }

        public RecordEntityMetadata() { SharedWith = new List<string>(); CreatedUtc = DateTime.UtcNow; }
    }

    /// <summary>Everything one build created, for the project record.</summary>
    internal sealed class RecordBuildOutcome
    {
        public List<BuiltEntity> Built = new List<BuiltEntity>();
        public List<string> Messages = new List<string>();
        public List<string> Problems = new List<string>();
        public List<TraverseResult> Traverses = new List<TraverseResult>();
        public List<SharedLine> Shared = new List<SharedLine>();
        public int Lines, Curves, Labels, Monuments, Tables;
    }

    /// <summary>
    /// Puts a reconstructed survey into the drawing and reads it back. Geometry comes only
    /// from the traverse (the written calls); the standards resolution decides layers and
    /// styles and what is withheld. Every entity is stamped (project id, call id) and carries
    /// its source metadata.
    ///
    /// UNTESTED against a drawing. Every calculation it draws from is unit tested in FieldCodes.
    /// </summary>
    internal static class RecordDrafter
    {
        public const string MetadataKey = "FTF_RECORD";

        // ------------------------------------------------------------ inventory

        /// <summary>What the drawing has, for the standards resolver.</summary>
        public static DrawingInventory Inventory(Database db, Transaction tr)
        {
            var resources = new Setup.DrawingResources(db, tr);
            var inventory = new DrawingInventory
            {
                Layers = resources.Layers.ToList(),
                TextStyles = resources.TextStyles.ToList(),
                Linetypes = resources.Linetypes.ToList(),
                Blocks = resources.Blocks.ToList(),
                Scale = resources.Scale
            };
            try
            {
                var current = tr.GetObject(db.Textstyle, OpenMode.ForRead) as TextStyleTableRecord;
                if (current != null) inventory.CurrentTextStyle = current.Name;
            }
            catch (Autodesk.AutoCAD.Runtime.Exception) { }
            try
            {
                var styles = (DBDictionary)tr.GetObject(db.TableStyleDictionaryId, OpenMode.ForRead);
                foreach (DBDictionaryEntry e in styles) inventory.TableStyles.Add(e.Key);
            }
            catch (Autodesk.AutoCAD.Runtime.Exception) { }
            inventory.LineLabelStyles.AddRange(CivilLabels.LineLabelStyleNames(db, tr));
            inventory.CurveLabelStyles.AddRange(CivilLabels.CurveLabelStyleNames(db, tr));
            return inventory;
        }

        // ------------------------------------------------------------ building

        /// <summary>
        /// Builds every figure of the project. Shared lot lines are drawn once. Anything the
        /// standard cannot place (missing layer, style, block) is withheld and said.
        /// </summary>
        public static RecordBuildOutcome Build(Database db, Transaction tr, Editor ed, RecordSurveyProject project, FtfSettings settings,
                                               StandardsResolution standards, bool placeLabels)
        {
            var outcome = new RecordBuildOutcome();
            var s = settings.RecordSurvey;
            var upf = settings.General.UnitsPerFoot > 0 ? settings.General.UnitsPerFoot : 1.0;
            var options = new TraverseOptions
            {
                PreferMeasured = s.PreferMeasured, UnitsPerFoot = upf, ClosureToleranceFeet = s.ClosureToleranceFt,
                DistanceToleranceFeet = s.DistanceToleranceFt, AngleToleranceSeconds = s.BearingToleranceSeconds,
                RequireApproved = true, MinimumPrecision = s.MinimumClosurePrecision
            };
            Ownership.EnsureRegApp(db, tr);

            outcome.Traverses = TraverseBuilder.BuildAll(project, options);
            foreach (var t in outcome.Traverses)
            {
                outcome.Messages.AddRange(t.Notes);
                outcome.Problems.AddRange(t.Problems);
                foreach (var c in t.Courses) outcome.Messages.AddRange(c.Notes.Select(n => t.Figure + " " + c.Call.Id + ": " + n));
                foreach (var sug in t.Suggestions) outcome.Messages.Add(t.Figure + ": " + sug);
            }
            outcome.Shared = SharedLineMatcher.Match(outcome.Traverses, s.SharedLineToleranceFt * upf, s.DistanceToleranceFt, s.BearingToleranceSeconds);
            foreach (var sl in outcome.Shared.Where(x => x.IsShared))
                foreach (var d in sl.Discrepancies) outcome.Messages.Add("Shared line: " + d);

            // ---- geometry: one entity per unique course
            var byCall = new Dictionary<string, ObjectId>(StringComparer.OrdinalIgnoreCase);
            foreach (var sl in outcome.Shared)
            {
                var owner = sl.Owners[0];
                var traverse = outcome.Traverses.First(t => t.Figure == owner.Key);
                var tc = traverse.Courses.First(c => c.Call.Id == owner.Value);
                var std = standards.For(tc.Call.ObjectType) ?? standards.For(s.DefaultObjectType);
                if (std == null || !std.CanDraw)
                {
                    outcome.Problems.Add(tc.Call.Id + " (" + tc.Call.ObjectType + "): not drawn -- " + (std == null ? "no standard for this object type" : "its standard is not resolved in this drawing (see the issues above)"));
                    continue;
                }
                AcEntity entity = ToEntity(tc.Course);
                entity.SetDatabaseDefaults(db);
                entity.LayerId = LayerId(db, tr, std.Layer, s.CreateMissingLayers);
                if (!string.IsNullOrEmpty(std.Linetype)) entity.Linetype = std.Linetype;
                CadUtil.AddToModelSpace(db, tr, entity);
                var kind = tc.Course.Kind == CourseKind.Arc ? FtfEntityKind.RecordCurve : FtfEntityKind.RecordLine;
                Ownership.Stamp(entity, project.Id, settings.Version, kind, null, tc.Call.Id);
                var meta = Metadata(project, tc, s);
                meta.SharedWith.AddRange(sl.Owners.Skip(1).Select(o => o.Value));
                WriteMetadata(tr, entity, meta);
                // The fingerprint is taken from the entity as AutoCAD holds it (an arc always runs
                // counter-clockwise), the same way HandEdits reads it back; a clockwise course would
                // otherwise never match its own fingerprint.
                string extractProblem;
                var asDrawn = EasementCommands.Extract(entity, out extractProblem);
                var built = new BuiltEntity { Handle = entity.Handle.ToString(), Role = kind == FtfEntityKind.RecordCurve ? "Curve" : "Line", CallId = tc.Call.Id, Figure = owner.Key, Layer = std.Layer,
                                              Fingerprint = EasementAnnotation.Fingerprint(asDrawn ?? new[] { tc.Course }) };
                built.SharedWith.AddRange(sl.Owners.Skip(1).Select(o => o.Value));
                outcome.Built.Add(built);
                byCall[tc.Call.Id] = entity.ObjectId;
                foreach (var o in sl.Owners.Skip(1)) byCall[o.Value] = entity.ObjectId;
                if (kind == FtfEntityKind.RecordCurve) outcome.Curves++; else outcome.Lines++;
            }

            // ---- monuments at reconstructed corners
            if (s.DrawMonuments) PlaceMonuments(db, tr, project, settings, standards, outcome);

            // ---- labels
            if (placeLabels && s.LabelsEnabled) PlaceLabels(db, tr, ed, project, settings, standards, outcome, byCall, new List<ObjectId>());

            // ---- closures onto the project
            project.Closures.Clear();
            foreach (var t in outcome.Traverses.Where(t => t.Closure != null)) project.Closures.Add(t.Closure);
            return outcome;
        }

        private static AcEntity ToEntity(Course c)
        {
            if (c.Kind == CourseKind.Line)
                return new Line(new Point3d(c.Start.X, c.Start.Y, 0), new Point3d(c.End.X, c.End.Y, 0));
            // An AutoCAD arc always runs counter-clockwise from its start angle to its end angle.
            var start = c.CounterClockwise ? c.StartAngle : c.EndAngle;
            var end = c.CounterClockwise ? c.EndAngle : c.StartAngle;
            return new Arc(new Point3d(c.Center.X, c.Center.Y, 0), Vector3d.ZAxis, c.Radius, start, end);
        }

        private static ObjectId LayerId(Database db, Transaction tr, string layer, bool create)
        {
            var table = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (table.Has(layer)) return table[layer];
            if (!create) throw new ConfigException("Layer \"" + layer + "\" is not in the drawing.");
            return CadUtil.EnsureLayer(db, tr, layer);
        }

        private static RecordEntityMetadata Metadata(RecordSurveyProject project, TraverseCourse tc, RecordSurveySettings s)
        {
            var call = tc.Call;
            var figure = project.Figures.FirstOrDefault(f => string.Equals(f.Name, call.Figure, StringComparison.OrdinalIgnoreCase));
            var record = call.Record;
            var reference = record != null && !string.IsNullOrEmpty(record.SourceId) ? project.FindReference(record.SourceId) : null;
            var m = new RecordEntityMetadata
            {
                Document = project.Document.Path,
                RecordingNumber = project.Document.RecordingNumber,
                Sheet = project.Document.Sheet,
                SurveyType = project.Document.SurveyType,
                ProjectId = project.Id,
                CallId = call.Id,
                Figure = call.Figure,
                Lot = figure != null ? figure.Lot : null,
                Block = figure != null ? figure.Block : null,
                ObjectType = call.ObjectType,
                Basis = tc.Basis.ToString(),
                RecordSource = record != null ? record.SourceId : null,
                RecordReference = reference != null ? reference.Description : null,
                Confidence = call.Confidence,
                SourcePage = call.Source != null ? call.Source.Page : 0,
                SourceBox = call.Source != null ? call.Source.Box : null,
                SourceText = call.Source != null ? call.Source.RawText : null
            };
            if (call.Measured != null && !call.Measured.Empty)
            {
                m.MeasuredBearing = call.Measured.AzimuthDegrees.HasValue ? Drafting.SurveyDirection.FormatBearing(call.Measured.AzimuthDegrees.Value, s.BearingSecondsDecimals, "°") : null;
                m.MeasuredDistance = call.Measured.DistanceFeet.HasValue ? Drafting.SurveyDirection.FormatDistance(call.Measured.DistanceFeet.Value, s.DistanceDecimals, true) : null;
            }
            if (record != null && !record.Empty)
            {
                m.RecordBearing = record.AzimuthDegrees.HasValue ? Drafting.SurveyDirection.FormatBearing(record.AzimuthDegrees.Value, s.BearingSecondsDecimals, "°") : null;
                m.RecordDistance = record.DistanceFeet.HasValue ? Drafting.SurveyDirection.FormatDistance(record.DistanceFeet.Value, s.DistanceDecimals, true) : null;
            }
            if (call.Kind == CallKind.Curve && call.Curve != null) m.Curve = ReviewSession.CurveSummary(call.Curve, s);
            return m;
        }

        // ------------------------------------------------------------ metadata on entities

        public static void WriteMetadata(Transaction tr, AcEntity entity, RecordEntityMetadata metadata)
        {
            var json = JsonConvert.SerializeObject(metadata);
            if (entity.ExtensionDictionary.IsNull) entity.CreateExtensionDictionary();
            var dict = (DBDictionary)tr.GetObject(entity.ExtensionDictionary, OpenMode.ForWrite);
            var values = Utilities.TextChunks.Split(json).Select(chunk => new TypedValue((int)DxfCode.Text, chunk)).ToArray();
            using (var buffer = new ResultBuffer(values))
            {
                if (dict.Contains(MetadataKey))
                {
                    var existing = (Xrecord)tr.GetObject(dict.GetAt(MetadataKey), OpenMode.ForWrite);
                    existing.Data = buffer;
                    return;
                }
                var xrecord = new Xrecord { Data = buffer };
                dict.SetAt(MetadataKey, xrecord);
                tr.AddNewlyCreatedDBObject(xrecord, true);
            }
        }

        public static RecordEntityMetadata ReadMetadata(Transaction tr, AcEntity entity)
        {
            if (entity == null || entity.ExtensionDictionary.IsNull) return null;
            var dict = tr.GetObject(entity.ExtensionDictionary, OpenMode.ForRead) as DBDictionary;
            if (dict == null || !dict.Contains(MetadataKey)) return null;
            var xrecord = tr.GetObject(dict.GetAt(MetadataKey), OpenMode.ForRead) as Xrecord;
            if (xrecord == null) return null;
            using (var data = xrecord.Data)
            {
                if (data == null) return null;
                var json = Utilities.TextChunks.Join(data.AsArray().Where(tv => tv.TypeCode == (int)DxfCode.Text).Select(tv => (string)tv.Value));
                try { return JsonConvert.DeserializeObject<RecordEntityMetadata>(json); }
                catch (JsonException) { return null; }
            }
        }

        // ------------------------------------------------------------ monuments

        private static void PlaceMonuments(Database db, Transaction tr, RecordSurveyProject project, FtfSettings settings,
                                           StandardsResolution standards, RecordBuildOutcome outcome)
        {
            var s = settings.RecordSurvey;
            var scale = CadUtil.DrawingUnitsPerPlottedUnit(db);
            foreach (var m in project.Monuments.Where(x => x.ReviewStatus != CallStatus.Rejected && !string.IsNullOrEmpty(x.Corner)))
            {
                // Corner = "<figure>/<vertex index>", assigned in the review.
                var parts = m.Corner.Split('/');
                int index;
                if (parts.Length != 2 || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out index)) continue;
                var traverse = outcome.Traverses.FirstOrDefault(t => string.Equals(t.Figure, parts[0], StringComparison.OrdinalIgnoreCase));
                if (traverse == null || index < 0 || index >= traverse.Vertices.Count) { outcome.Problems.Add("Monument " + m.Id + ": its corner " + m.Corner + " is not in the built geometry."); continue; }
                ResolvedMonumentStandard std;
                if (!standards.Monuments.TryGetValue(m.Status.ToString(), out std) || !std.CanDraw)
                {
                    outcome.Problems.Add("Monument " + m.Id + " (" + m.Status + "): not drawn -- no block or layer is resolved for " + m.Status.ToString().ToLowerInvariant() + " monuments.");
                    continue;
                }
                var defId = CadUtil.FindBlock(db, tr, std.Block);
                if (defId.IsNull) { outcome.Problems.Add("Monument " + m.Id + ": block " + std.Block + " is not in the drawing."); continue; }
                var at = traverse.Vertices[index];
                var reference = new BlockReference(new Point3d(at.X, at.Y, 0), defId);
                reference.SetDatabaseDefaults(db);
                reference.ScaleFactors = new Scale3d(std.Standard.ScalePlotted * scale);
                reference.LayerId = LayerId(db, tr, std.Layer, false);
                CadUtil.AddToModelSpace(db, tr, reference);
                var btr = (BlockTableRecord)tr.GetObject(defId, OpenMode.ForRead);
                foreach (ObjectId id in btr)
                {
                    var def = tr.GetObject(id, OpenMode.ForRead) as AttributeDefinition;
                    if (def == null || def.Constant) continue;
                    var attRef = new AttributeReference();
                    attRef.SetAttributeFromBlock(def, reference.BlockTransform);
                    attRef.TextString = def.TextString;
                    reference.AttributeCollection.AppendAttribute(attRef);
                    tr.AddNewlyCreatedDBObject(attRef, true);
                }
                Ownership.Stamp(reference, project.Id, settings.Version, FtfEntityKind.RecordMonument, reference.Position, m.Id);
                WriteMetadata(tr, reference, new RecordEntityMetadata
                {
                    Document = project.Document.Path, RecordingNumber = project.Document.RecordingNumber, Sheet = project.Document.Sheet, SurveyType = project.Document.SurveyType,
                    ProjectId = project.Id, CallId = m.Id, Figure = parts[0], ObjectType = "Monument", Basis = ValueBasis.Recorded.ToString(),
                    SourceText = m.Description, Confidence = m.Confidence, SourcePage = m.Source != null ? m.Source.Page : 0, SourceBox = m.Source != null ? m.Source.Box : null
                });
                outcome.Built.Add(new BuiltEntity { Handle = reference.Handle.ToString(), Role = "Monument", CallId = m.Id, Figure = parts[0], Layer = std.Layer });
                outcome.Monuments++;
            }
        }

        // ------------------------------------------------------------ labels

        /// <summary>
        /// Places the labels the planner decides on. A Civil 3D general segment label where the
        /// standard resolves one (editable, style-driven); otherwise plain text in the text style,
        /// with the mask the standard asks for. Withheld labels are reported, never placed in a
        /// stand-in style. Hand-moved labels from an earlier run are obstacles.
        /// </summary>
        public static void PlaceLabels(Database db, Transaction tr, Editor ed, RecordSurveyProject project, FtfSettings settings,
                                       StandardsResolution standards, RecordBuildOutcome outcome, Dictionary<string, ObjectId> byCall,
                                       List<ObjectId> keptLabelIds)
        {
            var s = settings.RecordSurvey;
            var scale = CadUtil.DrawingUnitsPerPlottedUnit(db);
            var upf = settings.General.UnitsPerFoot > 0 ? settings.General.UnitsPerFoot : 1.0;
            var textHeight = s.TextHeightPlotted * scale;
            var options = new LabelPlanOptions
            {
                TextHeight = textHeight, Offset = s.OffsetPlotted * scale, Settings = s, UnitsPerFoot = upf,
                MeasureWidth = text => MeasureWidthFactor(db, tr, standards, text, textHeight)
            };

            // Obstacles: monuments at every corner, and labels a person moved.
            var obstacles = new List<LabelObstacle>();
            var monumentRadius = 0.0;
            foreach (var m in standards.Monuments.Values.Where(x => x.CanDraw)) monumentRadius = Math.Max(monumentRadius, m.Standard.ScalePlotted * scale * 0.06);
            if (monumentRadius > 0)
                foreach (var t in outcome.Traverses)
                    foreach (var v in t.Vertices) obstacles.Add(new LabelObstacle { X = v.X, Y = v.Y, Radius = monumentRadius, What = "monument" });
            foreach (var id in keptLabelIds)
            {
                var kept = tr.GetObject(id, OpenMode.ForRead) as AcEntity;
                double w, h, ox, oy;
                if (kept == null || !CadUtil.TryMeasureText(kept, out w, out h, out ox, out oy)) continue;
                var ext = kept.GeometricExtents;
                obstacles.Add(new LabelObstacle { X = (ext.MinPoint.X + ext.MaxPoint.X) / 2, Y = (ext.MinPoint.Y + ext.MaxPoint.Y) / 2, Width = w, Height = h, What = "kept label" });

                // Every mask of the project was erased with the labels; a kept text label gets a new one
                // under its present position.
                var keptText = kept as DBText;
                var keptStamp = Ownership.Read(kept);
                if (s.Mask && keptText != null && keptStamp != null)
                {
                    var maskId = TextMask.Place(db, tr, keptText, LabelAnchor(keptText), keptText.Rotation, keptText.Height, keptText.LayerId);
                    if (!maskId.IsNull)
                    {
                        var wipeout = (AcEntity)tr.GetObject(maskId, OpenMode.ForWrite);
                        Ownership.Stamp(wipeout, project.Id, settings.Version, FtfEntityKind.RecordMask, null, keptStamp.TagText);
                        outcome.Built.Add(new BuiltEntity { Handle = wipeout.Handle.ToString(), Role = "Mask", CallId = keptStamp.TagText, Layer = kept.Layer });
                    }
                }
            }

            var plan = RecordLabelPlanner.Plan(outcome.Traverses, outcome.Shared, obstacles, options);
            outcome.Messages.AddRange(plan.Notes.Select(n => "Label: " + n));

            foreach (var label in plan.Labels)
            {
                if (label.Kind == "Lot")
                {
                    PlaceLotText(db, tr, project, settings, standards, label, outcome);
                    continue;
                }
                var call = project.FindCall(label.CallId);
                if (call == null) continue;
                var std = standards.For(call.ObjectType) ?? standards.For(s.DefaultObjectType);
                if (std == null) continue;
                ObjectId entityId;
                if (!byCall.TryGetValue(call.Id, out entityId)) continue;

                var civil = s.UseCivil3DLabels && !label.InTable && (call.Kind == CallKind.Curve ? std.CanLabelCurvesCivil : std.CanLabelLinesCivil);
                if (civil)
                {
                    var styleName = call.Kind == CallKind.Curve ? std.CurveLabelStyle : std.LineLabelStyle;
                    string problem;
                    var labelId = CivilLabels.CreateSegmentLabel(db, tr, entityId, new Point3d(label.X, label.Y, 0), styleName, std.LabelLayer, out problem);
                    if (!labelId.IsNull)
                    {
                        var entity = (AcEntity)tr.GetObject(labelId, OpenMode.ForWrite);
                        Ownership.Stamp(entity, project.Id, settings.Version, FtfEntityKind.RecordLabel, new Point3d(label.X, label.Y, 0), call.Id);
                        outcome.Built.Add(new BuiltEntity { Handle = entity.Handle.ToString(), Role = "Label", CallId = call.Id, Figure = label.Figure, Layer = std.LabelLayer });
                        outcome.Labels++;
                        continue;
                    }
                    outcome.Messages.Add("Label " + call.Id + ": the Civil 3D label could not be created (" + problem + ")" + (s.PlainTextWhenStyleMissing ? "; plain text placed instead." : "; withheld."));
                    if (!s.PlainTextWhenStyleMissing) continue;
                }
                if (!std.CanLabelText)
                {
                    outcome.Messages.Add("Label " + call.Id + " withheld: " + string.Join("; ", std.Issues.Where(i => i.Severity == "Missing").Select(i => i.Resource + " " + i.Name).ToArray()));
                    continue;
                }
                PlaceText(db, tr, project, settings, label, std.TextStyle, std.LabelLayer, textHeight, call.Id, outcome);
            }

            if (plan.TableRows.Count > 0)
            {
                if (!standards.CanDrawTable) outcome.Messages.Add("Line/curve table withheld: the table layer " + s.TableLayer + " is not in the drawing.");
                else PlaceTable(db, tr, ed, project, settings, standards, plan, outcome);
            }
        }

        private static double MeasureWidthFactor(Database db, Transaction tr, StandardsResolution standards, string text, double height)
        {
            // Measure the real text in the drawing's style: create, measure, discard.
            var styleName = standards.Entities.Values.Select(e => e.TextStyle).FirstOrDefault(n => !string.IsNullOrEmpty(n));
            var styleId = Setup.DrawingResources.FindTextStyle(db, tr, styleName);
            try
            {
                using (var probe = new DBText())
                {
                    probe.SetDatabaseDefaults(db);
                    if (!styleId.IsNull) probe.TextStyleId = styleId;
                    probe.TextString = (text ?? string.Empty).Replace("°", "%%d");
                    probe.Height = height;
                    probe.Position = Point3d.Origin;
                    double w, h, ox, oy;
                    if (CadUtil.TryMeasureText(probe, out w, out h, out ox, out oy) && height > 0) return w / height;
                }
            }
            catch (Autodesk.AutoCAD.Runtime.Exception) { }
            return (text ?? string.Empty).Length * 0.75;
        }

        private static void PlaceText(Database db, Transaction tr, RecordSurveyProject project, FtfSettings settings, PlannedRecordLabel label,
                                      string textStyle, string layer, double textHeight, string callId, RecordBuildOutcome outcome)
        {
            var s = settings.RecordSurvey;
            var styleId = Setup.DrawingResources.FindTextStyle(db, tr, textStyle);
            var layerId = LayerId(db, tr, layer, false);
            var lines = label.Lines.Count == 0 ? new List<string> { string.Empty } : label.Lines;
            var spacing = textHeight * 1.5;
            var up = new Vector3d(-Math.Sin(label.RotationRadians), Math.Cos(label.RotationRadians), 0);
            var centre = new Point3d(label.X, label.Y, 0);
            for (var i = 0; i < lines.Count; i++)
            {
                var offset = (lines.Count - 1) / 2.0 - i;
                var position = centre + up * (offset * spacing);
                using (var text = new DBText())
                {
                    text.SetDatabaseDefaults(db);
                    if (!styleId.IsNull) text.TextStyleId = styleId;
                    text.TextString = lines[i].Replace("°", "%%d").Replace("Δ", "\\U+0394");
                    text.Height = textHeight;
                    text.Rotation = label.RotationRadians;
                    text.HorizontalMode = TextHorizontalMode.TextCenter;
                    text.VerticalMode = TextVerticalMode.TextVerticalMid;
                    text.AlignmentPoint = position;
                    text.LayerId = layerId;
                    CadUtil.AddToModelSpace(db, tr, text);
                    if (s.Mask)
                    {
                        var maskId = TextMask.Place(db, tr, text, position, label.RotationRadians, textHeight, layerId);
                        if (!maskId.IsNull)
                        {
                            var wipeout = (AcEntity)tr.GetObject(maskId, OpenMode.ForWrite);
                            Ownership.Stamp(wipeout, project.Id, settings.Version, FtfEntityKind.RecordMask, null, callId);
                            outcome.Built.Add(new BuiltEntity { Handle = wipeout.Handle.ToString(), Role = "Mask", CallId = callId, Figure = label.Figure, Layer = layer });
                        }
                    }
                    Ownership.Stamp(text, project.Id, settings.Version, FtfEntityKind.RecordLabel, position, callId);
                    outcome.Built.Add(new BuiltEntity { Handle = text.Handle.ToString(), Role = "Label", CallId = callId, Figure = label.Figure, Layer = layer });
                }
            }
            outcome.Labels++;
        }

        private static void PlaceLotText(Database db, Transaction tr, RecordSurveyProject project, FtfSettings settings, StandardsResolution standards,
                                         PlannedRecordLabel label, RecordBuildOutcome outcome)
        {
            var s = settings.RecordSurvey;
            var layer = standards.LotTextLayer;
            if (string.IsNullOrEmpty(layer))
            {
                var std = standards.For(s.LotObjectType) ?? standards.For(s.DefaultObjectType);
                layer = std != null ? std.LabelLayer : null;
            }
            if (string.IsNullOrEmpty(layer)) { outcome.Messages.Add("Lot label " + label.Figure + " withheld: no lot text layer is resolved."); return; }
            var textStyle = standards.Entities.Values.Select(e => e.TextStyle).FirstOrDefault(n => !string.IsNullOrEmpty(n));
            var scale = CadUtil.DrawingUnitsPerPlottedUnit(db);
            PlaceText(db, tr, project, settings, label, textStyle, layer, s.LotTextHeightPlotted * scale, "LOT:" + label.Figure, outcome);
        }

        private static void PlaceTable(Database db, Transaction tr, Editor ed, RecordSurveyProject project, FtfSettings settings, StandardsResolution standards,
                                       LabelPlan plan, RecordBuildOutcome outcome)
        {
            var s = settings.RecordSurvey;
            var scale = CadUtil.DrawingUnitsPerPlottedUnit(db);
            var textHeight = s.TextHeightPlotted * scale;
            var all = outcome.Traverses.SelectMany(t => t.Vertices).ToList();
            var position = all.Count == 0 ? Point3d.Origin : new Point3d(all.Max(p => p.X) + textHeight * 6, all.Max(p => p.Y), 0);
            var styleId = ObjectId.Null;
            if (!string.IsNullOrEmpty(standards.TableStyle))
            {
                var styles = (DBDictionary)tr.GetObject(db.TableStyleDictionaryId, OpenMode.ForRead);
                if (styles.Contains(standards.TableStyle)) styleId = styles.GetAt(standards.TableStyle);
            }
            var lineRows = plan.TableRows.Where(r => r.Kind == "Line").ToList();
            var curveRows = plan.TableRows.Where(r => r.Kind == "Curve").ToList();
            var layerId = LayerId(db, tr, standards.TableLayer, false);
            if (lineRows.Count > 0)
            {
                var table = MakeTable(db, styleId, position, textHeight, "LINE TABLE", new[] { "LINE NO.", "BEARING", "DISTANCE" }, new[] { 9.0, 17.0, 12.0 },
                    lineRows.Select(r => new[] { r.Tag, r.Lines.Count > 0 ? r.Lines[0] : string.Empty, r.Lines.Count > 1 ? r.Lines[1] : string.Empty }).ToList());
                table.LayerId = layerId;
                CadUtil.AddToModelSpace(db, tr, table);
                Ownership.Stamp(table, project.Id, settings.Version, FtfEntityKind.RecordTable, position, "LINE TABLE");
                outcome.Built.Add(new BuiltEntity { Handle = table.Handle.ToString(), Role = "Table", CallId = "LINE TABLE", Layer = standards.TableLayer });
                position = new Point3d(position.X, position.Y - table.Height - textHeight * 4, 0);
                outcome.Tables++;
            }
            if (curveRows.Count > 0)
            {
                var table = MakeTable(db, styleId, position, textHeight, "CURVE TABLE", new[] { "CURVE NO.", "RADIUS", "LENGTH", "DELTA", "CHORD" }, new[] { 9.0, 12.0, 12.0, 14.0, 24.0 },
                    curveRows.Select(r => new[] { r.Tag, Strip(r.Lines, "R="), Strip(r.Lines, "L="), Strip(r.Lines, "Δ="), Strip(r.Lines, "CH=") }).ToList());
                table.LayerId = layerId;
                CadUtil.AddToModelSpace(db, tr, table);
                Ownership.Stamp(table, project.Id, settings.Version, FtfEntityKind.RecordTable, position, "CURVE TABLE");
                outcome.Built.Add(new BuiltEntity { Handle = table.Handle.ToString(), Role = "Table", CallId = "CURVE TABLE", Layer = standards.TableLayer });
                outcome.Tables++;
            }
        }

        private static string Strip(IList<string> lines, string prefix)
        {
            var line = lines.FirstOrDefault(l => l.StartsWith(prefix, StringComparison.Ordinal));
            return line == null ? "-" : line.Substring(prefix.Length);
        }

        private static Table MakeTable(Database db, ObjectId style, Point3d position, double textHeight, string title, string[] headers, double[] widths, IList<string[]> rows)
        {
            var table = new Table();
            table.SetDatabaseDefaults(db);
            if (!style.IsNull) table.TableStyle = style;
            table.Position = position;
            table.SetSize(rows.Count + 2, headers.Length);
            table.MergeCells(CellRange.Create(table, 0, 0, 0, headers.Length - 1));
            SetCell(table, 0, 0, title, textHeight);
            for (var c = 0; c < headers.Length; c++)
            {
                table.Columns[c].Width = widths[c] * textHeight;
                SetCell(table, 1, c, headers[c], textHeight);
            }
            for (var r = 0; r < rows.Count; r++)
                for (var c = 0; c < headers.Length; c++)
                    SetCell(table, r + 2, c, rows[r][c], textHeight);
            for (var r = 0; r < rows.Count + 2; r++) table.Rows[r].Height = textHeight * 2.0;
            table.GenerateLayout();
            return table;
        }

        private static void SetCell(Table table, int row, int column, string text, double height)
        {
            var cell = table.Cells[row, column];
            cell.TextHeight = height;
            cell.TextString = (text ?? string.Empty).Replace("Δ", "\\U+0394");
            cell.Alignment = CellAlignment.MiddleCenter;
        }

        // ------------------------------------------------------------ reading back

        /// <summary>Everything the project owns in the drawing, as the QC compares it.</summary>
        public static CadState ReadBack(Database db, Transaction tr, RecordSurveyProject project, FtfSettings settings)
        {
            var state = new CadState { UnitsPerFoot = settings.General.UnitsPerFoot > 0 ? settings.General.UnitsPerFoot : 1.0 };
            foreach (var pair in Ownership.FindOwned(db, tr, st => st.PointNumber == project.Id))
            {
                var entity = tr.GetObject(pair.Key, OpenMode.ForRead) as AcEntity;
                if (entity == null) continue;
                var stamp = pair.Value;
                var layer = entity.Layer;
                switch (stamp.Kind)
                {
                    case FtfEntityKind.RecordLine:
                    case FtfEntityKind.RecordCurve:
                    {
                        string problem;
                        var courses = EasementCommands.Extract(entity, out problem);
                        if (courses == null || courses.Count == 0) continue;
                        var meta = ReadMetadata(tr, entity);
                        var course = new CadCourse { Handle = entity.Handle.ToString(), CallId = stamp.TagText, Course = courses[0], Layer = layer, Linetype = entity.Linetype };
                        if (meta != null) { course.Figure = meta.Figure; course.ObjectType = meta.ObjectType; course.SharedWith.AddRange(meta.SharedWith); }
                        state.Courses.Add(course);
                        break;
                    }
                    case FtfEntityKind.RecordLabel:
                    {
                        var label = new CadLabel { Handle = entity.Handle.ToString(), CallId = stamp.TagText, Layer = layer, Kind = "Label" };
                        var text = entity as DBText;
                        if (text != null) { var style = tr.GetObject(text.TextStyleId, OpenMode.ForRead) as TextStyleTableRecord; label.Style = style != null ? style.Name : null; }
                        else { label.IsCivil3D = true; label.Style = CivilLabels.StyleNameOf(tr, entity); }
                        state.Labels.Add(label);
                        break;
                    }
                    case FtfEntityKind.RecordMonument:
                    {
                        var reference = entity as BlockReference;
                        var monument = new CadMonument { Handle = entity.Handle.ToString(), MonumentId = stamp.TagText, Layer = layer };
                        if (reference != null)
                        {
                            monument.Position = new P2(reference.Position.X, reference.Position.Y);
                            var btr = tr.GetObject(reference.DynamicBlockTableRecord, OpenMode.ForRead) as BlockTableRecord;
                            monument.Block = btr != null ? btr.Name : null;
                        }
                        state.Monuments.Add(monument);
                        break;
                    }
                }
            }
            return state;
        }

        /// <summary>Erases what the project owns, keeping hand-moved labels when asked. Returns the kept label ids.</summary>
        public static List<ObjectId> Erase(Database db, Transaction tr, RecordSurveyProject project, FtfSettings settings, bool geometry, bool labels, bool keepMovedLabels)
        {
            var kept = keepMovedLabels && labels ? MovedLabels(db, tr, project, settings) : new List<ObjectId>();
            var keep = new HashSet<ObjectId>(kept);
            var doomed = new List<ObjectId>();
            foreach (var pair in Ownership.FindOwned(db, tr, st => st.PointNumber == project.Id))
            {
                var st = pair.Value;
                var isLabel = st.Kind == FtfEntityKind.RecordLabel || st.Kind == FtfEntityKind.RecordMask || st.Kind == FtfEntityKind.RecordTable || st.Kind == FtfEntityKind.RecordText;
                var isGeometry = st.Kind == FtfEntityKind.RecordLine || st.Kind == FtfEntityKind.RecordCurve || st.Kind == FtfEntityKind.RecordMonument;
                if (!((isLabel && labels) || (isGeometry && geometry))) continue;
                if (keep.Contains(pair.Key)) continue;
                doomed.Add(pair.Key);
            }
            foreach (var id in doomed)
            {
                if (id.IsErased) continue;
                var entity = tr.GetObject(id, OpenMode.ForWrite, false, true) as AcEntity;
                if (entity != null) entity.Erase();
            }
            return kept;
        }

        /// <summary>Labels of the project a person moved since they were placed: kept by FTFRECORDLABEL and routed around.</summary>
        public static List<ObjectId> MovedLabels(Database db, Transaction tr, RecordSurveyProject project, FtfSettings settings)
        {
            var moved = new List<ObjectId>();
            var tolerance = settings.Cleanup.MovedTolerance;
            foreach (var pair in Ownership.FindOwned(db, tr, st => st.PointNumber == project.Id && st.Kind == FtfEntityKind.RecordLabel))
            {
                var entity = tr.GetObject(pair.Key, OpenMode.ForRead) as AcEntity;
                if (entity == null) continue;
                var text = entity as DBText;
                if (text != null)
                {
                    // Stamped with the alignment point it was placed at; AutoCAD fills Position in
                    // from it, so Position is not what was stamped.
                    if (pair.Value.WasMovedByHand(LabelAnchor(text), tolerance)) moved.Add(pair.Key);
                    continue;
                }
                // A Civil 3D label says itself whether someone dragged it.
                if (CivilLabels.WasDragged(entity)) moved.Add(pair.Key);
            }
            return moved;
        }

        /// <summary>The point a text label is placed by: its alignment point unless it is left/base
        /// justified, where the insertion point is the only one AutoCAD keeps.</summary>
        private static Point3d LabelAnchor(DBText text)
        {
            return text.HorizontalMode == TextHorizontalMode.TextLeft && text.VerticalMode == TextVerticalMode.TextBase
                ? text.Position : text.AlignmentPoint;
        }

        /// <summary>Whether the built geometry differs from what the project recorded at build time (a hand edit).</summary>
        public static List<string> HandEdits(Database db, Transaction tr, RecordSurveyProject project)
        {
            var edits = new List<string>();
            foreach (var built in project.Built.Where(b => b.Role == "Line" || b.Role == "Curve"))
            {
                long value;
                ObjectId id;
                if (!long.TryParse(built.Handle, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value) || !db.TryGetObjectId(new Handle(value), out id) || id.IsErased)
                {
                    edits.Add(built.CallId + ": its entity " + built.Handle + " is no longer in the drawing.");
                    continue;
                }
                var entity = tr.GetObject(id, OpenMode.ForRead) as AcEntity;
                string problem;
                var courses = entity != null ? EasementCommands.Extract(entity, out problem) : null;
                if (courses == null) continue;
                if (!string.IsNullOrEmpty(built.Fingerprint) && EasementAnnotation.Fingerprint(courses) != built.Fingerprint)
                    edits.Add(built.CallId + ": entity " + built.Handle + " was edited by hand since it was built.");
            }
            return edits;
        }
    }

    /// <summary>
    /// The Civil 3D label styles and general segment labels, kept in one place because the
    /// API lives in AeccDbMgd and every call is guarded: a drawing without Civil 3D styles, or
    /// an object the label cannot attach to, is reported, never fatal.
    /// UNTESTED against Civil 3D. To confirm on the first run: GeneralSegmentLabel.Create on a
    /// plain Line/Arc entity (the API documents it for feature lines and general segments),
    /// Label.Dragged as the "someone moved it" flag, and the label's station measured from the
    /// entity's own start (see CreateSegmentLabel).
    /// </summary>
    internal static class CivilLabels
    {
        public static IList<string> LineLabelStyleNames(Database db, Transaction tr)
        {
            return Names(db, tr, true);
        }

        public static IList<string> CurveLabelStyleNames(Database db, Transaction tr)
        {
            return Names(db, tr, false);
        }

        private static IList<string> Names(Database db, Transaction tr, bool lines)
        {
            var names = new List<string>();
            try
            {
                var civil = Autodesk.Civil.ApplicationServices.CivilDocument.GetCivilDocument(db);
                var collection = lines ? civil.Styles.LabelStyles.GeneralLineLabelStyles : civil.Styles.LabelStyles.GeneralCurveLabelStyles;
                foreach (ObjectId id in collection)
                {
                    var style = tr.GetObject(id, OpenMode.ForRead) as Autodesk.Civil.DatabaseServices.Styles.LabelStyle;
                    if (style != null && !string.IsNullOrEmpty(style.Name)) names.Add(style.Name);
                }
            }
            catch (System.Exception) { }
            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }

        private static ObjectId FindStyle(Database db, Transaction tr, string name, bool lines)
        {
            if (string.IsNullOrWhiteSpace(name)) return ObjectId.Null;
            try
            {
                var civil = Autodesk.Civil.ApplicationServices.CivilDocument.GetCivilDocument(db);
                var collection = lines ? civil.Styles.LabelStyles.GeneralLineLabelStyles : civil.Styles.LabelStyles.GeneralCurveLabelStyles;
                foreach (ObjectId id in collection)
                {
                    var style = tr.GetObject(id, OpenMode.ForRead) as Autodesk.Civil.DatabaseServices.Styles.LabelStyle;
                    if (style != null && string.Equals(style.Name, name, StringComparison.OrdinalIgnoreCase)) return id;
                }
            }
            catch (System.Exception) { }
            return ObjectId.Null;
        }

        /// <summary>A Civil 3D general line/curve label on the entity at the point of it nearest <paramref name="near"/>
        /// (the label's planned spot), on the layer. Null id and a reason when it cannot be made.</summary>
        public static ObjectId CreateSegmentLabel(Database db, Transaction tr, ObjectId entityId, Point3d near, string styleName, string layer, out string problem)
        {
            problem = null;
            var entity = tr.GetObject(entityId, OpenMode.ForRead) as AcEntity;
            var isArc = entity is Arc;
            // Civil 3D takes both label styles at once and uses the one that fits the segment, so the style name is
            // looked up in both collections; the entity's own kind is the one that has to be there.
            var lineStyleId = FindStyle(db, tr, styleName, true);
            var curveStyleId = FindStyle(db, tr, styleName, false);
            var styleId = isArc ? curveStyleId : lineStyleId;
            if (styleId.IsNull) { problem = "label style \"" + styleName + "\" not found"; return ObjectId.Null; }
            try
            {
                // The ratio Civil 3D wants runs along the entity as AutoCAD holds it (an arc from its
                // counter-clockwise start), which is not always the course's own direction: measure it
                // from the nearest point instead of trusting a direction.
                var ratio = 0.5;
                var curve = entity as Curve;
                if (curve != null)
                {
                    var closest = curve.GetClosestPointTo(near, false);
                    var span = curve.EndParam - curve.StartParam;
                    if (Math.Abs(span) > 1e-12) ratio = (curve.GetParameterAtPoint(closest) - curve.StartParam) / span;
                }
                var labelId = Autodesk.Civil.DatabaseServices.GeneralSegmentLabel.Create(entityId, Math.Max(0.05, Math.Min(0.95, ratio)), lineStyleId, curveStyleId);
                if (labelId.IsNull) { problem = "Civil 3D returned no label"; return ObjectId.Null; }
                if (!string.IsNullOrEmpty(layer))
                {
                    var label = tr.GetObject(labelId, OpenMode.ForWrite) as AcEntity;
                    var table = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                    if (label != null && table.Has(layer)) label.LayerId = table[layer];
                }
                return labelId;
            }
            catch (System.Exception ex)
            {
                problem = ex.Message;
                return ObjectId.Null;
            }
        }

        /// <summary>Whether a Civil 3D label was dragged from its computed spot. False for anything else.</summary>
        public static bool WasDragged(AcEntity entity)
        {
            try
            {
                var label = entity as Autodesk.Civil.DatabaseServices.Label;
                return label != null && label.Dragged;
            }
            catch (System.Exception) { return false; }
        }

        public static string StyleNameOf(Transaction tr, AcEntity entity)
        {
            try
            {
                var label = entity as Autodesk.Civil.DatabaseServices.Label;
                if (label == null) return null;
                var style = tr.GetObject(label.StyleId, OpenMode.ForRead) as Autodesk.Civil.DatabaseServices.Styles.LabelStyle;
                return style != null ? style.Name : null;
            }
            catch (System.Exception) { return null; }
        }
    }
}
