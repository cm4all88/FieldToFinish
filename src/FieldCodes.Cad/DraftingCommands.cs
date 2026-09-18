using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using FieldCodes.Drafting;
using FieldCodes.Linework;
using FieldCodes.Settings;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcDocument = Autodesk.AutoCAD.ApplicationServices.Document;
using AcEntity = Autodesk.AutoCAD.DatabaseServices.Entity;

namespace FieldCodes.Cad
{
    /// <summary>
    /// FTFDRAWLINE: the survey line construction service. This is FTF's OTHER world,
    /// deliberately separate from the post-processing pipeline:
    ///
    ///   - The pipeline polishes geometry Civil 3D created and never creates linework.
    ///   - FTFDRAWLINE creates linework, because the user explicitly asked it to --
    ///     the small set of cadastral / record lines an office drafts by hand:
    ///     boundary, right of way, section lines, easements.
    ///
    /// One construction engine serves every line type; the type only chooses the
    /// standard (layer, linetype, annotation). The drawing's own layer table is the
    /// authority: a configured layer that is missing is refused with an explanation,
    /// never created from a guessed name.
    ///
    /// Everything drafted is stamped with its own ownership kinds (DraftLine,
    /// DraftAnnotation, DraftMask), which FTFCLEAN deliberately skips -- cleaning up
    /// a bad pipeline run must never erase a boundary the surveyor drew on purpose.
    /// FTFDRAFTCLEAN manages this world instead.
    ///
    /// UNTESTED against a drawing; bearing parsing, formatting and the settings
    /// model are unit tested.
    /// </summary>
    public sealed class DraftingCommands
    {
        /// <summary>Everything one drafted course creates, so Undo can take the
        /// geometry and its annotation back together.</summary>
        private sealed class Course
        {
            public Point3d Start;
            public Point3d End;
            public string Summary;
            public readonly List<ObjectId> Created = new List<ObjectId>();
        }

        /// <summary>The line type's standard, resolved against the open drawing
        /// once, before any geometry is created.</summary>
        private sealed class ResolvedStandard
        {
            public DraftingLineType Type;
            public ObjectId LineLayerId;
            public string LineLayerName;
            public string Linetype;              // empty = leave ByLayer
            public string AnnotationKind;        // canonical, validated
            public bool Annotate;
            public ObjectId TextLayerId;
            public string TextLayerName;
            public ObjectId TextStyleId;
            public double TextHeight;            // drawing units
            public double Offset;                // drawing units
        }

        // =============================================================== FTFDRAWLINE

        [CommandMethod("FTFDRAWLINE", CommandFlags.Modal)]
        public void FtfDrawLine()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            var ed = doc.Editor;
            var db = doc.Database;

            var settings = ResolveSettings(db);
            var enabledTypes = EnabledTypes(settings);
            if (enabledTypes.Count == 0)
            {
                ed.WriteMessage("\nFTFDRAWLINE: no drafting line types are enabled. " +
                                "Configure them under FTF Settings > Drafting Lines.\n");
                return;
            }

            var type = ChooseLineType(ed, enabledTypes);
            if (type == null) return;

            ResolvedStandard std;
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                std = ResolveStandard(db, tr, ed, type);
                tr.Commit();
            }
            if (std == null) return;    // the refusal already explained itself

            Point3d current;
            if (!AcquireStartPoint(doc, out current)) return;

            var courses = new Stack<Course>();
            var method = "Bearing";

            while (true)
            {
                var choice = PromptNextAction(ed, method, courses.Count);
                if (choice == null || choice == "Finish") break;

                if (choice == "Undo")
                {
                    if (courses.Count == 0)
                    {
                        ed.WriteMessage("\nNothing to undo.");
                        continue;
                    }
                    var undone = courses.Pop();
                    EraseCourse(doc, undone);
                    current = undone.Start;
                    ed.WriteMessage("\nUndid {0}. Back at the previous point.",
                                    undone.Summary);
                    continue;
                }

                method = choice;

                Point3d end;
                if (!AcquireEndPoint(doc, choice, current, out end)) continue;

                if (end.DistanceTo(current) < 1e-8)
                {
                    ed.WriteMessage("\nThat would be a zero-length course; nothing drawn.");
                    continue;
                }

                Course course;
                using (doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    course = CreateCourse(db, tr, current, end, std, settings);
                    tr.Commit();
                }

                courses.Push(course);
                current = end;
                ed.WriteMessage("\nDrew {0}", course.Summary);
            }

            ed.WriteMessage("\nFTFDRAWLINE: {0} course(s) on layer {1}.\n",
                            courses.Count, std.LineLayerName);
        }

        // ============================================================= FTFDRAFTCLEAN

        /// <summary>
        /// Cleanup for the drafted world, separate from FTFCLEAN on purpose.
        /// Annotation is derived from the drafted geometry, so removing it is cheap
        /// to reverse; the geometry itself is the user's deliberate work and is only
        /// ever erased after an explicit confirmation.
        /// </summary>
        [CommandMethod("FTFDRAFTCLEAN", CommandFlags.Modal)]
        public void FtfDraftClean()
        {
            FtfSession.Run("FTFDRAFTCLEAN", (db, tr, ed) =>
            {
                var lines = Ownership.FindOwned(db, tr,
                    s => s.Kind == FtfEntityKind.DraftLine).Count;
                var annotation = Ownership.FindOwned(db, tr,
                    s => s.Kind == FtfEntityKind.DraftAnnotation ||
                         s.Kind == FtfEntityKind.DraftMask).Count;

                if (lines == 0 && annotation == 0)
                {
                    ed.WriteMessage("\nFTFDRAFTCLEAN: nothing drafted by FTFDRAWLINE " +
                                    "is in this drawing.\n");
                    return;
                }

                ed.WriteMessage("\nFTFDRAFTCLEAN: {0} drafted line(s), {1} annotation " +
                                "entities (FTFDRAWLINE).", lines, annotation);

                var options = new PromptKeywordOptions("\nRemove")
                {
                    AllowNone = false
                };
                options.Keywords.Add("Annotation");
                options.Keywords.Add("Everything");
                options.Keywords.Default = "Annotation";

                var answer = ed.GetKeywords(options);
                if (answer.Status != PromptStatus.OK) return;

                if (string.Equals(answer.StringResult, "Annotation", StringComparison.Ordinal))
                {
                    var removed = Ownership.DeleteOwnedKinds(db, tr,
                        FtfEntityKind.DraftAnnotation, FtfEntityKind.DraftMask);
                    ed.WriteMessage("\nFTFDRAFTCLEAN: removed {0} annotation entities. " +
                                    "The drafted geometry was not touched.\n", removed);
                    return;
                }

                // Everything: the geometry was drafted deliberately, so erasing it
                // takes a deliberate answer, not a default.
                var confirm = new PromptKeywordOptions(string.Format(
                    "\nThis erases {0} deliberately drafted survey line(s) AND their " +
                    "annotation. Erase?", lines))
                {
                    AllowNone = false
                };
                confirm.Keywords.Add("Yes");
                confirm.Keywords.Add("No");
                confirm.Keywords.Default = "No";

                var confirmed = ed.GetKeywords(confirm);
                if (confirmed.Status != PromptStatus.OK ||
                    !string.Equals(confirmed.StringResult, "Yes", StringComparison.OrdinalIgnoreCase))
                {
                    ed.WriteMessage("\nNothing was removed.\n");
                    return;
                }

                var total = Ownership.DeleteOwnedKinds(db, tr,
                    FtfEntityKind.DraftLine, FtfEntityKind.DraftAnnotation,
                    FtfEntityKind.DraftMask);
                ed.WriteMessage("\nFTFDRAFTCLEAN: removed {0} drafted entities.\n", total);
            });
        }

        // ================================================================== settings

        /// <summary>
        /// Drafting reads only the behaviour settings; the grammar in rules.json is
        /// the pipeline's concern. A missing rules file must not block drafting, so
        /// it is tolerated here (it is only ever a first-time migration seed).
        /// </summary>
        private static FtfSettings ResolveSettings(Database db)
        {
            RulesConfig rules = null;
            try { rules = FtfSession.Rules(db); }
            catch (ConfigException) { }

            return FtfSession.ResolveSettings(db, rules).Settings;
        }

        private static IList<DraftingLineType> EnabledTypes(FtfSettings settings)
        {
            var enabled = new List<DraftingLineType>();
            if (settings.Drafting != null && settings.Drafting.LineTypes != null)
            {
                foreach (var type in settings.Drafting.LineTypes)
                {
                    if (type != null && type.Enabled && !string.IsNullOrWhiteSpace(type.Name))
                        enabled.Add(type);
                }
            }
            return enabled;
        }

        // ============================================================ type selection

        private static DraftingLineType ChooseLineType(Editor ed,
                                                       IList<DraftingLineType> types)
        {
            var options = new PromptKeywordOptions("\nLine type") { AllowNone = false };
            var byKeyword = new Dictionary<string, DraftingLineType>(StringComparer.Ordinal);

            foreach (var type in types)
            {
                var keyword = KeywordFor(type.Name);
                var unique = keyword;
                var n = 2;
                while (byKeyword.ContainsKey(unique))
                {
                    unique = keyword + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    n++;
                }
                byKeyword[unique] = type;
                options.Keywords.Add(unique);
            }
            options.Keywords.Default = options.Keywords[0].GlobalName;

            var answer = ed.GetKeywords(options);
            if (answer.Status != PromptStatus.OK) return null;

            DraftingLineType chosen;
            return byKeyword.TryGetValue(answer.StringResult, out chosen) ? chosen : null;
        }

        /// <summary>"Right of Way" -> "RightOfWay": a command-line keyword the
        /// prompt can offer.</summary>
        private static string KeywordFor(string name)
        {
            var sb = new System.Text.StringBuilder();
            var upperNext = true;
            foreach (var c in (name ?? string.Empty).Trim())
            {
                if (char.IsLetterOrDigit(c))
                {
                    sb.Append(upperNext ? char.ToUpperInvariant(c) : c);
                    upperNext = false;
                }
                else
                {
                    upperNext = true;
                }
            }
            return sb.Length == 0 ? "Type" : sb.ToString();
        }

        // ======================================================= standard resolution

        /// <summary>
        /// The type's standard against the open drawing. Refusals explain exactly
        /// what is missing and return null -- no layer is ever created here, because
        /// a guessed office layer that happens to look right is how a boundary ends
        /// up on a layer nobody plots.
        /// </summary>
        private static ResolvedStandard ResolveStandard(Database db, Transaction tr,
                                                        Editor ed, DraftingLineType type)
        {
            if (string.IsNullOrWhiteSpace(type.Layer))
            {
                ed.WriteMessage("\nFTFDRAWLINE: \"{0}\" has no layer configured. Set it " +
                                "under FTF Settings > Drafting Lines, from this " +
                                "drawing's own layer list.\n", type.Name);
                return null;
            }

            var lineLayerId = FindLayer(db, tr, type.Layer);
            if (lineLayerId.IsNull)
            {
                ed.WriteMessage("\nFTFDRAWLINE: layer \"{0}\" (configured for {1}) is " +
                                "not in this drawing. FTF never creates office layers " +
                                "from a configured name -- add the layer to the drawing " +
                                "or template, or point the type at one that exists.\n",
                                type.Layer, type.Name);
                return null;
            }

            // The annotation standard comes from settings; a value this build does
            // not understand is refused rather than silently treated as None.
            var annotationKind = type.AnnotationKind;
            if (annotationKind == null)
            {
                ed.WriteMessage("\nFTFDRAWLINE: \"{0}\" has an unrecognised annotation " +
                                "type \"{1}\". Fix it under FTF Settings > Drafting " +
                                "Lines.\n", type.Name, type.Annotation);
                return null;
            }

            if (annotationKind == DraftingLineType.AnnotationFeatureText &&
                string.IsNullOrWhiteSpace(type.FeatureText))
            {
                ed.WriteMessage("\nFTFDRAWLINE: \"{0}\" uses FeatureText annotation but " +
                                "no feature text is configured. Set it under FTF " +
                                "Settings > Drafting Lines -- FTF will not invent the " +
                                "label from the type name.\n", type.Name);
                return null;
            }

            var std = new ResolvedStandard
            {
                Type = type,
                LineLayerId = lineLayerId,
                LineLayerName = type.Layer.Trim(),
                Linetype = string.Empty,
                AnnotationKind = annotationKind,
                Annotate = type.WantsAnnotation
            };

            if (!string.IsNullOrWhiteSpace(type.Linetype))
            {
                var linetypes = (LinetypeTable)tr.GetObject(db.LinetypeTableId,
                                                            OpenMode.ForRead);
                if (!linetypes.Has(type.Linetype))
                {
                    ed.WriteMessage("\nFTFDRAWLINE: linetype \"{0}\" (configured for " +
                                    "{1}) is not loaded in this drawing. Load it, or " +
                                    "clear the setting to use the layer's own linetype.\n",
                                    type.Linetype, type.Name);
                    return null;
                }
                std.Linetype = type.Linetype.Trim();
            }

            ed.WriteMessage("\n{0}: layer {1}{2}", type.Name, std.LineLayerName,
                            std.Linetype.Length > 0
                                ? ", linetype " + std.Linetype : " (ByLayer linetype)");

            if (!std.Annotate) return std;

            // The annotation layer: the type's explicit choice first; otherwise the
            // office-standard text layer derived from the line layer's own family,
            // by the same resolver the line labels use -- and when the drawing has
            // no such text layer, the line's own layer, visibly, rather than a
            // guessed name.
            string textLayer;
            if (!string.IsNullOrWhiteSpace(type.AnnotationLayer))
            {
                if (FindLayer(db, tr, type.AnnotationLayer).IsNull)
                {
                    ed.WriteMessage("\nFTFDRAWLINE: annotation layer \"{0}\" (configured " +
                                    "for {1}) is not in this drawing. FTF never creates " +
                                    "office layers -- add it, or clear the setting to " +
                                    "derive the text layer from the drawing.\n",
                                    type.AnnotationLayer, type.Name);
                    return null;
                }
                textLayer = type.AnnotationLayer.Trim();
                ed.WriteMessage("\nAnnotation layer: {0} (configured)", textLayer);
            }
            else
            {
                var catalog = FtfLineworkService.LayerNames(db, tr);
                var resolution = LabelLayerResolver.Resolve(std.LineLayerName, null,
                                                            catalog, std.LineLayerName);
                textLayer = resolution.Layer;
                ed.WriteMessage("\nAnnotation layer: {0}{1}", resolution.Describe(),
                                resolution.Source == LabelLayerSource.Default
                                    ? " -- the line's own layer" : string.Empty);
            }

            std.TextLayerName = textLayer;
            std.TextLayerId = FindLayer(db, tr, textLayer);

            std.TextStyleId = Setup.DrawingResources.FindTextStyle(db, tr, type.TextStyle);
            if (!string.IsNullOrWhiteSpace(type.TextStyle) && std.TextStyleId.IsNull)
                ed.WriteMessage("\nFTFDRAWLINE: text style \"{0}\" is not in this " +
                                "drawing; using the current style.", type.TextStyle);

            var scale = CadUtil.DrawingUnitsPerPlottedUnit(db);
            std.TextHeight = type.TextHeightPlotted * scale;
            std.Offset = type.OffsetPlotted * scale;

            return std;
        }

        /// <summary>Strict lookup: the layer's id, or Null. Never creates -- that is
        /// the whole difference from CadUtil.EnsureLayer.</summary>
        private static ObjectId FindLayer(Database db, Transaction tr, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return ObjectId.Null;

            var table = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            var trimmed = name.Trim();
            return table.Has(trimmed) ? table[trimmed] : ObjectId.Null;
        }

        // ============================================================== acquisition

        private static bool AcquireStartPoint(AcDocument doc, out Point3d start)
        {
            var ed = doc.Editor;
            start = Point3d.Origin;

            while (true)
            {
                var options = new PromptPointOptions("\nStart point or [COGO]")
                {
                    AllowNone = false,
                    AppendKeywordsToMessage = true
                };
                options.Keywords.Add("COGO");

                var answer = ed.GetPoint(options);
                if (answer.Status == PromptStatus.Keyword)
                {
                    Point3d location;
                    if (AcquireCogoPoint(doc, out location))
                    {
                        start = location;
                        return true;
                    }
                    continue;       // not found (already reported): ask again
                }

                if (answer.Status != PromptStatus.OK) return false;

                start = CadUtil.Flatten(answer.Value);
                return true;
            }
        }

        /// <summary>What the next step is. Returns the method keyword, "Undo",
        /// "Finish", or null when the user cancelled.</summary>
        private static string PromptNextAction(Editor ed, string lastMethod,
                                               int coursesDrawn)
        {
            var options = new PromptKeywordOptions("\nNext course") { AllowNone = false };
            options.Keywords.Add("Bearing");
            options.Keywords.Add("Azimuth");
            options.Keywords.Add("Pick");
            options.Keywords.Add("COGO");
            if (coursesDrawn > 0) options.Keywords.Add("Undo");
            options.Keywords.Add("Finish");
            options.Keywords.Default = lastMethod;

            var answer = ed.GetKeywords(options);
            if (answer.Status != PromptStatus.OK) return null;
            return answer.StringResult;
        }

        private static bool AcquireEndPoint(AcDocument doc, string method,
                                            Point3d current, out Point3d end)
        {
            var ed = doc.Editor;
            end = Point3d.Origin;

            if (method == "Pick")
            {
                var options = new PromptPointOptions("\nEndpoint")
                {
                    AllowNone = false,
                    UseBasePoint = true,
                    BasePoint = current,
                    UseDashedLine = true
                };
                var answer = ed.GetPoint(options);
                if (answer.Status != PromptStatus.OK) return false;

                end = CadUtil.Flatten(answer.Value);
                return true;
            }

            if (method == "COGO")
                return AcquireCogoPoint(doc, out end);

            // Bearing or azimuth, then a distance: the typed record course.
            double azimuthDegrees;
            if (method == "Azimuth")
            {
                var typed = GetTypedString(ed,
                    "\nAzimuth (D.MMSS, e.g. 215.3000 = 215d30'00\"):");
                if (typed == null) return false;

                var parsed = SurveyDirection.ParseAzimuth(typed);
                if (!parsed.Ok)
                {
                    ed.WriteMessage("\n{0}", parsed.Error);
                    return false;
                }
                azimuthDegrees = parsed.Value;
            }
            else
            {
                var typed = GetTypedString(ed,
                    "\nBearing (D.MMSS, e.g. N45.2536E = N 45d25'36\" E):");
                if (typed == null) return false;

                var parsed = SurveyDirection.ParseBearing(typed);
                if (!parsed.Ok)
                {
                    ed.WriteMessage("\n{0}", parsed.Error);
                    return false;
                }
                azimuthDegrees = parsed.Value;
            }

            var distanceTyped = GetTypedString(ed, "\nDistance (survey feet):");
            if (distanceTyped == null) return false;

            var distanceParsed = SurveyDirection.ParseDistance(distanceTyped);
            if (!distanceParsed.Ok)
            {
                ed.WriteMessage("\n{0}", distanceParsed.Error);
                return false;
            }

            var settings = ResolveSettings(doc.Database);
            var unitsPerFoot = settings.General.UnitsPerFoot > 0
                ? settings.General.UnitsPerFoot : 1.0;
            var distance = distanceParsed.Value * unitsPerFoot;

            var azimuthRadians = FieldCodes.Geometry.Angles.ToRadians(azimuthDegrees);
            end = new Point3d(current.X + distance * Math.Sin(azimuthRadians),
                              current.Y + distance * Math.Cos(azimuthRadians),
                              0.0);
            return true;
        }

        private static string GetTypedString(Editor ed, string message)
        {
            var options = new PromptStringOptions(message) { AllowSpaces = true };
            var answer = ed.GetString(options);
            if (answer.Status != PromptStatus.OK) return null;
            if (string.IsNullOrWhiteSpace(answer.StringResult)) return null;
            return answer.StringResult;
        }

        /// <summary>A COGO point's coordinates, by point number, flattened to
        /// elevation zero -- cadastral drafting is plan-view work.</summary>
        private static bool AcquireCogoPoint(AcDocument doc, out Point3d location)
        {
            var ed = doc.Editor;
            location = Point3d.Origin;

            var options = new PromptIntegerOptions("\nCOGO point number:")
            {
                AllowNegative = false,
                AllowZero = false,
                AllowNone = false
            };
            var answer = ed.GetInteger(options);
            if (answer.Status != PromptStatus.OK) return false;

            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                foreach (var cogo in CadUtil.CogoPointsInOrder(doc.Database, tr))
                {
                    if (cogo.PointNumber == (uint)answer.Value)
                    {
                        location = CadUtil.Flatten(cogo.Location);
                        tr.Commit();
                        return true;
                    }
                }
                tr.Commit();
            }

            ed.WriteMessage("\nCOGO point {0} is not in this drawing.", answer.Value);
            return false;
        }

        // ================================================================= creation

        private static Course CreateCourse(Database db, Transaction tr, Point3d start,
                                           Point3d end, ResolvedStandard std,
                                           FtfSettings settings)
        {
            Ownership.EnsureRegApp(db, tr);

            var course = new Course { Start = start, End = end };

            using (var line = new Line(start, end))
            {
                line.SetDatabaseDefaults(db);
                line.LayerId = std.LineLayerId;
                if (std.Linetype.Length > 0) line.Linetype = std.Linetype;

                CadUtil.AddToModelSpace(db, tr, line);
                Ownership.Stamp(line, std.Type.Name, settings.Version,
                                FtfEntityKind.DraftLine, null);
                course.Created.Add(line.ObjectId);
            }

            // Annotation is computed from the drawn geometry, never echoed from what
            // was typed: if they ever differ, the geometry is the truth.
            var dx = end.X - start.X;
            var dy = end.Y - start.Y;
            var azimuth = SurveyDirection.AzimuthFromVector(dx, dy);
            var unitsPerFoot = settings.General.UnitsPerFoot > 0
                ? settings.General.UnitsPerFoot : 1.0;
            var feet = Math.Sqrt(dx * dx + dy * dy) / unitsPerFoot;

            var type = std.Type;
            course.Summary = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{0} course {1} {2}", type.Name,
                FormatDirection(type, azimuth, "°"),
                SurveyDirection.FormatDistance(feet, type.DistanceDecimals, true));

            if (std.Annotate)
                PlaceCourseAnnotation(db, tr, std, settings, start, end, azimuth, feet,
                                      course.Created);

            return course;
        }

        private static void PlaceCourseAnnotation(Database db, Transaction tr,
                                                  ResolvedStandard std,
                                                  FtfSettings settings,
                                                  Point3d start, Point3d end,
                                                  double azimuth, double feet,
                                                  List<ObjectId> created)
        {
            var type = std.Type;

            // %%d is what DBText renders as the degree symbol.
            var bearing = FormatDirection(type, azimuth, "%%d");
            var distance = SurveyDirection.FormatDistance(feet,
                type.DistanceDecimals, type.FootSymbol);

            var mid = new Point3d((start.X + end.X) / 2.0,
                                  (start.Y + end.Y) / 2.0, 0.0);

            // The text runs along the course but never upside down -- the same
            // readability rule as every line label. Flipping affects only the text
            // rotation; the reported bearing above came from the geometry and does
            // not change.
            var direction = Math.Atan2(end.Y - start.Y, end.X - start.X);
            var readable = LineLabelPlanner.NormalizeReadable(direction);

            // "Above" in the text's own reading direction.
            var up = new Vector3d(-Math.Sin(readable), Math.Cos(readable), 0.0);
            var gap = std.Offset + std.TextHeight / 2.0;

            // Stacked bearing-over-distance is its own layout; every other standard
            // is one text at the configured placement.
            if (std.AnnotationKind == DraftingLineType.AnnotationBearingDistance &&
                type.Stacked)
            {
                PlaceAnnotationText(db, tr, std, settings, bearing, mid + up * gap,
                                    readable, created);
                PlaceAnnotationText(db, tr, std, settings, distance, mid - up * gap,
                                    readable, created);
                return;
            }

            string content;
            if (std.AnnotationKind == DraftingLineType.AnnotationBearingOnly)
                content = bearing;
            else if (std.AnnotationKind == DraftingLineType.AnnotationDistanceOnly)
                content = distance;
            else if (std.AnnotationKind == DraftingLineType.AnnotationFeatureText)
                content = type.FeatureText.Trim();
            else
                content = bearing + "  " + distance;

            var placement = (type.Placement ?? "Above").Trim().Replace(" ", string.Empty);
            Point3d position;
            if (string.Equals(placement, "Below", StringComparison.OrdinalIgnoreCase))
                position = mid - up * gap;
            else if (string.Equals(placement, "OnLine", StringComparison.OrdinalIgnoreCase))
                position = mid;
            else
                position = mid + up * gap;

            PlaceAnnotationText(db, tr, std, settings, content, position, readable,
                                created);
        }

        /// <summary>A direction in this type's configured format -- quadrant bearing
        /// or whole-circle azimuth.</summary>
        private static string FormatDirection(DraftingLineType type, double azimuth,
                                              string degreeSymbol)
        {
            return type.WantsAzimuthFormat
                ? SurveyDirection.FormatAzimuth(azimuth, type.BearingSecondsDecimals,
                                                degreeSymbol)
                : SurveyDirection.FormatBearing(azimuth, type.BearingSecondsDecimals,
                                                degreeSymbol);
        }

        private static void PlaceAnnotationText(Database db, Transaction tr,
                                                ResolvedStandard std,
                                                FtfSettings settings, string content,
                                                Point3d position, double rotation,
                                                List<ObjectId> created)
        {
            using (var text = new DBText())
            {
                text.SetDatabaseDefaults(db);
                if (!std.TextStyleId.IsNull) text.TextStyleId = std.TextStyleId;

                text.TextString = content;
                text.Height = std.TextHeight;
                text.Rotation = rotation;
                text.HorizontalMode = TextHorizontalMode.TextCenter;
                text.VerticalMode = TextVerticalMode.TextVerticalMid;
                text.AlignmentPoint = position;
                text.LayerId = std.TextLayerId;

                CadUtil.AddToModelSpace(db, tr, text);

                if (std.Type.Mask)
                {
                    var maskId = TextMask.Place(db, tr, text, position, rotation,
                                                std.TextHeight, std.TextLayerId);
                    if (!maskId.IsNull)
                    {
                        var wipeout = (AcEntity)tr.GetObject(maskId, OpenMode.ForWrite);
                        Ownership.Stamp(wipeout, std.Type.Name, settings.Version,
                                        FtfEntityKind.DraftMask, null);
                        created.Add(maskId);
                    }
                }

                Ownership.Stamp(text, std.Type.Name, settings.Version,
                                FtfEntityKind.DraftAnnotation, position);
                created.Add(text.ObjectId);
            }
        }

        // ===================================================================== undo

        private static void EraseCourse(AcDocument doc, Course course)
        {
            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                foreach (var id in course.Created)
                {
                    if (id.IsErased) continue;

                    var entity = tr.GetObject(id, OpenMode.ForWrite, false, true)
                        as AcEntity;
                    if (entity != null) entity.Erase();
                }
                tr.Commit();
            }
        }
    }
}
