using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using FieldCodes.Easements;
using FieldCodes.Linework;
using FieldCodes.Row;
using FieldCodes.Settings;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcEntity = Autodesk.AutoCAD.DatabaseServices.Entity;

namespace FieldCodes.Cad
{
    /// <summary>
    /// FTFROW: right-of-way runs, platted blocks and intersections from picked monuments.
    ///
    /// The drafter picks the monuments (survey points snap), a popup asks for what the
    /// survey does not know, and FTF draws the right-of-way lines with their bearings and
    /// distances, offsets the opposite right-of-way line and the centerline to the road
    /// side, labels the monuments and the right-of-way width, and ties an offset monument
    /// back to the corner it marks. The geometry and every word come from FieldCodes.Row,
    /// which is unit tested; this file only picks and draws.
    ///
    /// Like FTFDRAWLINE, what it draws is the user's deliberate work: stamped DraftLine and
    /// DraftAnnotation, left alone by FTFCLEAN, managed by FTFDRAFTCLEAN. Layers are looked
    /// up, never created from a configured name.
    ///
    /// UNTESTED against a drawing. Written to the same patterns as FTFDRAWLINE and
    /// STRIPEASEMENT, but not built against Civil 3D; see the implementation report.
    /// </summary>
    public sealed class RowCommands
    {
        /// <summary>The standard resolved against the open drawing before anything is drawn.</summary>
        private sealed class Standard
        {
            public DraftingLineType Type;
            public RightOfWaySettings Row;
            public ObjectId RowLayer;
            public string RowLayerName;
            public string Linetype;
            public ObjectId CenterlineLayer;
            public ObjectId MonumentLayer;
            public ObjectId DimensionLayer;
            public ObjectId AnnotationLayer;
            public ObjectId MonumentTextLayer;
            public ObjectId RoadNameLayer;
            public ObjectId TextStyle;
            public double TextHeight;
            public double TextOffset;
            public double SymbolRadius;
            public double UnitsPerFoot;
            public string Version;
        }

        [CommandMethod("FTFROW", CommandFlags.Modal)]
        public void FtfRow()
        {
            FtfSession.Run("FTFROW", (db, tr, ed) =>
            {
                RulesConfig rules = null;
                try { rules = FtfSession.Rules(db); }
                catch (ConfigException) { }
                var settings = FtfSession.ResolveSettings(db, rules).Settings;

                // Fail before the monuments are picked, not after the popup is filled in.
                var std = ResolveStandard(db, tr, ed, settings);
                if (std == null) return;

                var mode = ChooseMode(ed);
                if (!mode.HasValue) { ed.WriteMessage("\nFTFROW: cancelled -- nothing drawn.\n"); return; }

                var job = new RowJob
                {
                    Mode = mode.Value,
                    UnitsPerFoot = std.UnitsPerFoot,
                    DrawPickedLine = std.Row.DrawPickedLine,
                    DrawCenterline = std.Row.DrawCenterline,
                    LabelOppositeLine = std.Row.LabelOppositeLine,
                    LabelCenterline = std.Row.LabelCenterline,
                    EveryMonumentDimension = std.Row.DimensionEveryMonument,
                    WidthToleranceFeet = std.Row.WidthToleranceFeet
                };

                if (!PickMonuments(db, tr, ed, job)) { ed.WriteMessage("\nFTFROW: cancelled -- nothing drawn.\n"); return; }

                // Defaults the popup starts from; the drafter changes them there.
                if (job.IsRing)
                {
                    for (var i = 0; i < job.Monuments.Count; i++)
                    {
                        job.Faces.Add(mode == RowMode.Intersection ? RowFace.Cross(string.Empty, 0) : RowFace.Frontage(string.Empty, 0));
                        job.CornerRadiiFeet.Add(mode == RowMode.Block ? std.Row.DefaultCornerRadiusFeet : 0.0);
                    }
                }

                if (!ShowForm(job, std.Row)) { ed.WriteMessage("\nFTFROW: cancelled -- nothing drawn.\n"); return; }

                var plan = RowBuilder.Build(job);
                if (!plan.Ok)
                {
                    foreach (var error in plan.Errors) ed.WriteMessage("\nFTFROW: {0}", error);
                    ed.WriteMessage("\nNothing was drawn.\n");
                    return;
                }

                RowLabels.Apply(plan, std.Row, std.Type, std.UnitsPerFoot, "%%d");

                if (!ResolveRoleLayers(db, tr, ed, std, plan)) return;

                var created = Draw(db, tr, std, plan);

                foreach (var note in plan.Notes) ed.WriteMessage("\n  {0}", note);
                foreach (var warning in plan.Warnings) ed.WriteMessage("\n  WARNING: {0}", warning);
                ed.WriteMessage("\nFTFROW: drew {0} object(s): {1} line(s), {2} dimension(s), {3} monument(s) on layer {4}.\n",
                                created, plan.Lines.Count, plan.Dimensions.Count, plan.Monuments.Count, std.RowLayerName);
            });
        }

        // --------------------------------------------------------------- prompts

        private static RowMode? ChooseMode(Editor ed)
        {
            var options = new PromptKeywordOptions("\nWhat are you drawing [Run/Block/Intersection] <Run>: ", "Run Block Intersection")
            {
                AllowNone = true
            };
            options.Keywords.Default = "Run";
            var answer = ed.GetKeywords(options);
            if (answer.Status == PromptStatus.None) return RowMode.Run;
            if (answer.Status != PromptStatus.OK) return null;
            if (answer.StringResult == "Block") return RowMode.Block;
            if (answer.StringResult == "Intersection") return RowMode.Intersection;
            return RowMode.Run;
        }

        /// <summary>Monuments in the order they run: along the road for a run, around the
        /// ring for a block or intersection. Enter finishes once there are enough.</summary>
        private static bool PickMonuments(Database db, Transaction tr, Editor ed, RowJob job)
        {
            var minimum = job.Mode == RowMode.Run ? 2 : 3;
            ed.WriteMessage(job.Mode == RowMode.Run
                ? "\nPick the monuments in order along the right-of-way line -- survey points snap. Enter finishes."
                : "\nPick the corner monuments in order around the {0} -- survey points snap. Enter closes it.",
                job.Mode == RowMode.Block ? "block" : "intersection");

            while (true)
            {
                var count = job.Monuments.Count;
                var prompt = count == 0
                    ? "\nFirst monument: "
                    : string.Format(CultureInfo.InvariantCulture, "\nMonument {0}{1}: ", count + 1, count >= minimum ? " <done>" : string.Empty);
                var options = new PromptPointOptions(prompt) { AllowNone = count >= minimum };
                if (count > 0)
                {
                    options.UseBasePoint = true;
                    options.BasePoint = new Point3d(job.Monuments[count - 1].Location.X, job.Monuments[count - 1].Location.Y, 0);
                }

                SelectedLocation location;
                string keyword;
                var status = EasementCommands.PickLocation(db, tr, ed, options, out location, out keyword);
                if (status == PromptStatus.Cancel) return false;
                if (location == null) return job.Monuments.Count >= minimum;

                job.Monuments.Add(new RowMonument
                {
                    PointNumber = location.PointNumber,
                    Location = location.Point,
                    Description = string.Empty
                });
                ed.WriteMessage("\n  Monument {0}: {1}", count + 1,
                    string.IsNullOrWhiteSpace(location.PointNumber) ? "picked location" : "survey point " + location.PointNumber);
            }
        }

        // Kept out of FtfRow so the command line never loads the window's types.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static bool ShowForm(RowJob job, RightOfWaySettings settings)
        {
            using (var form = new Ui.RowForm(job, settings))
                return AcadApp.ShowModalDialog(form) == System.Windows.Forms.DialogResult.OK;
        }

        // ------------------------------------------------------------- standard

        /// <summary>
        /// The right-of-way line's standard (the Drafting Lines type's layer, linetype and
        /// annotation) and the text settings, against the open drawing. Refusals explain
        /// exactly what is missing; no layer is created from a configured name.
        /// </summary>
        private static Standard ResolveStandard(Database db, Transaction tr, Editor ed, FtfSettings settings)
        {
            var row = settings.RightOfWay;
            var type = settings.Drafting != null ? settings.Drafting.FindType(row.LineTypeName) : null;
            if (type == null || !type.Enabled)
            {
                ed.WriteMessage("\nFTFROW: the Drafting Lines type \"{0}\" is missing or disabled. Set the type " +
                                "under FTF Settings > Right of Way.\n", row.LineTypeName);
                return null;
            }

            if (string.IsNullOrWhiteSpace(type.Layer))
            {
                ed.WriteMessage("\nFTFROW: \"{0}\" has no layer configured. Set it under FTF Settings > Drafting Lines.\n", type.Name);
                return null;
            }

            var rowLayer = FindLayer(db, tr, type.Layer);
            if (rowLayer.IsNull)
            {
                ed.WriteMessage("\nFTFROW: layer \"{0}\" (the {1} line layer) is not in this drawing. FTF never creates " +
                                "office layers from a configured name.\n", type.Layer, type.Name);
                return null;
            }

            var std = new Standard
            {
                Type = type,
                Row = row,
                RowLayer = rowLayer,
                RowLayerName = type.Layer.Trim(),
                Linetype = string.Empty,
                Version = settings.Version,
                UnitsPerFoot = settings.General.UnitsPerFoot > 0 ? settings.General.UnitsPerFoot : 1.0
            };

            if (!string.IsNullOrWhiteSpace(type.Linetype))
            {
                var linetypes = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
                if (!linetypes.Has(type.Linetype))
                {
                    ed.WriteMessage("\nFTFROW: linetype \"{0}\" (configured for {1}) is not loaded in this drawing.\n", type.Linetype, type.Name);
                    return null;
                }
                std.Linetype = type.Linetype.Trim();
            }

            // The monument layer and the dimension layer are always used.
            std.MonumentLayer = RequireLayer(db, tr, ed, row.MonumentLayer, "monument symbols");
            if (std.MonumentLayer.IsNull) return null;
            std.DimensionLayer = RequireLayer(db, tr, ed, row.DimensionLayer, "right-of-way dimensions");
            if (std.DimensionLayer.IsNull) return null;

            // Annotation: the type's own layer, else the text layer of the line layer's family,
            // by the same resolver the line labels use.
            string textLayer;
            if (!string.IsNullOrWhiteSpace(type.AnnotationLayer))
            {
                if (FindLayer(db, tr, type.AnnotationLayer).IsNull)
                {
                    ed.WriteMessage("\nFTFROW: annotation layer \"{0}\" (configured for {1}) is not in this drawing.\n", type.AnnotationLayer, type.Name);
                    return null;
                }
                textLayer = type.AnnotationLayer.Trim();
            }
            else
            {
                var catalog = FtfLineworkService.LayerNames(db, tr);
                textLayer = LabelLayerResolver.Resolve(std.RowLayerName, null, catalog, std.RowLayerName).Layer;
            }
            std.AnnotationLayer = FindLayer(db, tr, textLayer);
            if (std.AnnotationLayer.IsNull) std.AnnotationLayer = rowLayer;

            std.MonumentTextLayer = OptionalLayer(db, tr, ed, row.MonumentTextLayer, "monument notes", std.AnnotationLayer);
            if (std.MonumentTextLayer.IsNull) return null;
            std.RoadNameLayer = OptionalLayer(db, tr, ed, row.RoadNameLayer, "road names", std.MonumentTextLayer);
            if (std.RoadNameLayer.IsNull) return null;

            std.TextStyle = Setup.DrawingResources.FindTextStyle(db, tr, string.IsNullOrWhiteSpace(row.TextStyle) ? type.TextStyle : row.TextStyle);
            var scale = CadUtil.DrawingUnitsPerPlottedUnit(db);
            std.TextHeight = row.TextHeightPlotted * scale;
            std.TextOffset = row.TextOffsetPlotted * scale;
            std.SymbolRadius = row.MonumentRadiusPlotted * scale;
            return std;
        }

        /// <summary>The centerline layer is needed only when a centerline is drawn, which the
        /// popup can change; checked once the plan says whether it is.</summary>
        private static bool ResolveRoleLayers(Database db, Transaction tr, Editor ed, Standard std, RowPlan plan)
        {
            if (plan.Lines.All(l => l.Role != RowLayerRole.Centerline)) return true;
            std.CenterlineLayer = RequireLayer(db, tr, ed, std.Row.CenterlineLayer, "the road centerline");
            return !std.CenterlineLayer.IsNull;
        }

        private static ObjectId RequireLayer(Database db, Transaction tr, Editor ed, string name, string what)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                ed.WriteMessage("\nFTFROW: no layer is set for {0}. Set it under FTF Settings > Right of Way.\n", what);
                return ObjectId.Null;
            }
            var id = FindLayer(db, tr, name);
            if (id.IsNull)
                ed.WriteMessage("\nFTFROW: layer \"{0}\" (set for {1}) is not in this drawing. FTF never creates office layers " +
                                "from a configured name.\n", name, what);
            return id;
        }

        /// <summary>A text layer that may be left blank, in which case the fallback is used.
        /// A layer that is set but missing is still refused.</summary>
        private static ObjectId OptionalLayer(Database db, Transaction tr, Editor ed, string name, string what, ObjectId fallback)
        {
            return string.IsNullOrWhiteSpace(name) ? fallback : RequireLayer(db, tr, ed, name, what);
        }

        private static ObjectId FindLayer(Database db, Transaction tr, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return ObjectId.Null;
            var table = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            var trimmed = name.Trim();
            return table.Has(trimmed) ? table[trimmed] : ObjectId.Null;
        }

        // --------------------------------------------------------------- drawing

        private static Point3d Pt(P2 p)
        {
            return new Point3d(p.X, p.Y, 0.0);
        }

        private static int Draw(Database db, Transaction tr, Standard std, RowPlan plan)
        {
            Ownership.EnsureRegApp(db, tr);
            var count = 0;

            foreach (var line in plan.Lines)
            {
                ObjectId layer;
                switch (line.Role)
                {
                    case RowLayerRole.Centerline: layer = std.CenterlineLayer; break;
                    case RowLayerRole.CrossLine: layer = std.DimensionLayer; break;
                    default: layer = std.RowLayer; break;
                }

                count += AddCourse(db, tr, std, line.Course, layer, line.Role == RowLayerRole.RowLine);
                if (line.Text.Count > 0) count += AnnotateCourse(db, tr, std, line);
            }

            foreach (var dimension in plan.Dimensions) count += DrawDimension(db, tr, std, dimension);
            foreach (var mark in plan.Monuments) count += DrawMonument(db, tr, std, mark);
            foreach (var label in plan.RoadLabels)
            {
                var rotation = LineLabelPlanner.NormalizeReadable(Math.Atan2(label.Along.Y, label.Along.X));
                count += AddText(db, tr, std, label.Text, Pt(label.At), rotation, std.RoadNameLayer);
            }
            return count;
        }

        private static int AddCourse(Database db, Transaction tr, Standard std, Course course, ObjectId layer, bool useLinetype)
        {
            AcEntity entity;
            if (course.Kind == CourseKind.Line)
                entity = new Line(Pt(course.Start), Pt(course.End));
            else
            {
                var start = course.CounterClockwise ? course.StartAngle : course.EndAngle;
                var end = course.CounterClockwise ? course.EndAngle : course.StartAngle;
                entity = new Arc(Pt(course.Center), Vector3d.ZAxis, course.Radius, start, end);
            }

            using (entity)
            {
                entity.SetDatabaseDefaults(db);
                entity.LayerId = layer;
                if (useLinetype && std.Linetype.Length > 0) entity.Linetype = std.Linetype;
                CadUtil.AddToModelSpace(db, tr, entity);
                Ownership.Stamp(entity, "ROW", std.Version, FtfEntityKind.DraftLine, null);
            }
            return 1;
        }

        private static int AddSegment(Database db, Transaction tr, Standard std, Point3d a, Point3d b, ObjectId layer)
        {
            using (var line = new Line(a, b))
            {
                line.SetDatabaseDefaults(db);
                line.LayerId = layer;
                CadUtil.AddToModelSpace(db, tr, line);
                Ownership.Stamp(line, "ROW", std.Version, FtfEntityKind.DraftLine, null);
            }
            return 1;
        }

        private static int AddText(Database db, Transaction tr, Standard std, string content, Point3d position, double rotation, ObjectId layer)
        {
            if (string.IsNullOrWhiteSpace(content)) return 0;
            using (var text = new DBText())
            {
                text.SetDatabaseDefaults(db);
                if (!std.TextStyle.IsNull) text.TextStyleId = std.TextStyle;
                text.TextString = content;
                text.Height = std.TextHeight;
                text.Rotation = rotation;
                text.HorizontalMode = TextHorizontalMode.TextCenter;
                text.VerticalMode = TextVerticalMode.TextVerticalMid;
                text.AlignmentPoint = position;
                text.LayerId = layer;
                CadUtil.AddToModelSpace(db, tr, text);
                Ownership.Stamp(text, "ROW", std.Version, FtfEntityKind.DraftAnnotation, position);
            }
            return 1;
        }

        /// <summary>Bearing and distance beside a line, curve data beside an arc. Never upside down;
        /// the reported direction comes from the geometry and does not change when the text flips.</summary>
        private static int AnnotateCourse(Database db, Transaction tr, Standard std, RowLine line)
        {
            var course = line.Course;
            var count = 0;
            var gap = std.TextOffset + std.TextHeight / 2.0;
            var step = std.TextHeight * 1.3;

            if (course.Kind == CourseKind.Arc)
            {
                var sweep = course.Sweep;
                var middle = course.CounterClockwise ? course.StartAngle + sweep / 2.0 : course.StartAngle - sweep / 2.0;
                var radial = new Vector3d(Math.Cos(middle), Math.Sin(middle), 0.0);
                var onArc = new Point3d(course.Center.X + course.Radius * Math.Cos(middle), course.Center.Y + course.Radius * Math.Sin(middle), 0.0);
                var tangent = course.CounterClockwise ? middle + Math.PI / 2.0 : middle - Math.PI / 2.0;
                var rotation = LineLabelPlanner.NormalizeReadable(tangent);
                for (var i = 0; i < line.Text.Count; i++)
                    count += AddText(db, tr, std, line.Text[i], onArc + radial * (gap + i * step), rotation, std.AnnotationLayer);
                return count;
            }

            var mid = new Point3d((course.Start.X + course.End.X) / 2.0, (course.Start.Y + course.End.Y) / 2.0, 0.0);
            var readable = LineLabelPlanner.NormalizeReadable(Math.Atan2(course.End.Y - course.Start.Y, course.End.X - course.Start.X));
            var up = new Vector3d(-Math.Sin(readable), Math.Cos(readable), 0.0);

            if (line.Text.Count == 1)
            {
                var placement = (std.Type.Placement ?? "Above").Trim().Replace(" ", string.Empty);
                Point3d position;
                if (string.Equals(placement, "Below", StringComparison.OrdinalIgnoreCase)) position = mid - up * gap;
                else if (string.Equals(placement, "OnLine", StringComparison.OrdinalIgnoreCase)) position = mid;
                else position = mid + up * gap;
                return AddText(db, tr, std, line.Text[0], position, readable, std.AnnotationLayer);
            }

            if (line.Text.Count == 2)
            {
                count += AddText(db, tr, std, line.Text[0], mid + up * gap, readable, std.AnnotationLayer);
                count += AddText(db, tr, std, line.Text[1], mid - up * gap, readable, std.AnnotationLayer);
                return count;
            }

            for (var i = 0; i < line.Text.Count; i++)
                count += AddText(db, tr, std, line.Text[i], mid - up * (gap + i * step), readable, std.AnnotationLayer);
            return count;
        }

        /// <summary>A right-of-way width: a line square across with a tick at each end and the
        /// text beside it. A line across a road is already drawn as a course, so only its text is.</summary>
        private static int DrawDimension(Database db, Transaction tr, Standard std, RowDimension dimension)
        {
            var from = Pt(dimension.From);
            var to = Pt(dimension.To);
            var direction = to - from;
            if (direction.Length < 1e-9) return 0;

            var angle = Math.Atan2(direction.Y, direction.X);
            var readable = LineLabelPlanner.NormalizeReadable(angle);
            var up = new Vector3d(-Math.Sin(readable), Math.Cos(readable), 0.0);
            var gap = std.TextOffset + std.TextHeight / 2.0;
            var mid = new Point3d((from.X + to.X) / 2.0, (from.Y + to.Y) / 2.0, 0.0);
            var count = 0;

            if (!dimension.IsCross)
            {
                count += AddSegment(db, tr, std, from, to, std.DimensionLayer);
                var across = direction.GetNormal().RotateBy(Math.PI / 2.0, Vector3d.ZAxis) * (std.TextHeight * 0.6);
                count += AddSegment(db, tr, std, from - across, from + across, std.DimensionLayer);
                count += AddSegment(db, tr, std, to - across, to + across, std.DimensionLayer);
            }

            // Across a road the bearing and distance sit above the line; the width goes below it.
            var position = dimension.IsCross ? mid - up * gap : mid + up * gap;
            count += AddText(db, tr, std, dimension.Text, position, readable, std.DimensionLayer);
            return count;
        }

        /// <summary>The symbol where the monument is (a circle; a set monument also gets a cross),
        /// its note on the side away from the road, and for an offset monument a tie to the corner.</summary>
        private static int DrawMonument(Database db, Transaction tr, Standard std, RowMonumentMark mark)
        {
            var count = 0;
            var centre = Pt(mark.Location);

            using (var circle = new Circle(centre, Vector3d.ZAxis, std.SymbolRadius))
            {
                circle.SetDatabaseDefaults(db);
                circle.LayerId = std.MonumentLayer;
                CadUtil.AddToModelSpace(db, tr, circle);
                Ownership.Stamp(circle, "ROW", std.Version, FtfEntityKind.DraftLine, null);
                count++;
            }

            if (mark.Monument.Kind == MonumentKind.Set)
            {
                var r = std.SymbolRadius;
                count += AddSegment(db, tr, std, new Point3d(centre.X - r, centre.Y, 0), new Point3d(centre.X + r, centre.Y, 0), std.MonumentLayer);
                count += AddSegment(db, tr, std, new Point3d(centre.X, centre.Y - r, 0), new Point3d(centre.X, centre.Y + r, 0), std.MonumentLayer);
            }

            if (mark.Monument.IsOffset)
                count += AddSegment(db, tr, std, centre, Pt(mark.Corner), std.MonumentLayer);

            var rotation = LineLabelPlanner.NormalizeReadable(Math.Atan2(mark.AlongDirection.Y, mark.AlongDirection.X));
            var outward = new Vector3d(mark.NoteDirection.X, mark.NoteDirection.Y, 0.0);
            var first = std.SymbolRadius + std.TextOffset + std.TextHeight / 2.0;
            for (var i = 0; i < mark.Text.Count; i++)
                count += AddText(db, tr, std, mark.Text[i], centre + outward * (first + i * std.TextHeight * 1.3), rotation, std.MonumentTextLayer);
            return count;
        }
    }
}
