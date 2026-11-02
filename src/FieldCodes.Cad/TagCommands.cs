using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using FieldCodes.Geometry;
using FieldCodes.Tagging;

using AcEntity = Autodesk.AutoCAD.DatabaseServices.Entity;

namespace FieldCodes.Cad
{
    /// <summary>
    /// Tags on the plan and the schedule beside it.
    ///
    /// Tag numbers live in XData, so a re-run reissues the same number to the same
    /// point. That is the whole point: a tag printed on an issued plan must never move
    /// to a different tree.
    ///
    /// UNTESTED: the assignment and table content are unit tested; the AutoCAD calls
    /// have never been run against a drawing.
    /// </summary>
    public sealed class TagCommands
    {
        [CommandMethod("FTFTAGS", CommandFlags.Modal)]
        public void FtfTags()
        {
            FtfSession.Run("FTFTAGS", (db, tr, ed) =>
            {
                var cfg = FtfSession.Rules(db);
                var settings = FtfSession.SettingsFor(db, cfg);
                var ts = settings.Tags;

                var parser = new FieldCodeParser(cfg);
                var classifier = new LayerClassifier(cfg);

                Ownership.EnsureRegApp(db, tr);

                // Read what was issued before -- the register in the drawing, then the tags on
                // it -- so numbers are reissued, never reused. Then erase the old tag entities
                // of this run's families only: a run limited to trees keeps the poles' tags.
                var register = DrawingStore.ReadTagRegister(db, tr);
                var previous = new Dictionary<string, string>(register, StringComparer.OrdinalIgnoreCase);
                foreach (var pair in ReadPreviousTags(db, tr)) previous[pair.Key] = pair.Value;

                var ann = settings.Annotation;
                var scope = FieldCodes.Annotation.AnnotationRun.TagScope(ann);
                var notes = CogoNotes.For(db, tr, settings);
                var scan = AnnotationScan.Read(db, tr, cfg, settings, parser, notes);
                Ownership.DeleteOwned(db, tr, s => s.Kind == FtfEntityKind.Tag && scan.InScope(s.PointNumber, scope));

                var points = new List<ParsedPoint>();
                var anchors = scan.Anchors;
                var skipped = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var styler = new AnnotationStyler(db, tr, settings, cfg);
                var styles = new Dictionary<string, FieldCodes.Styles.ResolvedAnnotationStyle>(StringComparer.OrdinalIgnoreCase);
                foreach (var number in scan.Order)
                {
                    var parsed = scan.Points[number];
                    if (string.IsNullOrEmpty(parsed.TagPrefix)) continue;
                    var decision = FieldCodes.Annotation.AnnotationPolicy.Tag(parsed, ann, scope, cfg);
                    var style = decision.Annotate ? styler.For(FieldCodes.Styles.StyledKind.Tag, parsed) : null;
                    if (style != null && !style.Enabled)
                        decision = new FieldCodes.Annotation.AnnotationDecision { Annotate = false, Reason = "tag turned off for the feature (" + style.OriginOf("Enabled") + ")" };
                    if (decision.Annotate) { points.Add(parsed); styles[number] = style; }
                    else { int n; skipped[decision.Reason] = skipped.TryGetValue(decision.Reason, out n) ? n + 1 : 1; }
                }

                var assigner = new TagAssigner { StartNumber = Math.Max(0, ts.StartNumber) };
                var assignments = assigner.Assign(points, previous);

                foreach (var pair in skipped) ed.WriteMessage("\nFTFTAGS: not tagged, {0}: {1}", pair.Key, pair.Value);
                foreach (var a in assignments) register[a.PointNumber] = a.Text;
                DrawingStore.WriteTagRegister(db, tr, register);

                if (assignments.Count == 0)
                {
                    ed.WriteMessage("\nFTFTAGS: nothing to tag. Only codes whose rule sets " +
                                    "a tagPrefix are tagged, and trees only when Settings > Trees is Tag only.\n");
                    return;
                }

                var scale = CadUtil.DrawingUnitsPerPlottedUnit(db);

                var obstacles = GatherObstacles(db, tr, classifier);

                var drawn = 0;
                var reissued = 0;
                var failed = 0;
                var leaders = 0;

                foreach (var assignment in assignments)
                {
                    Point3d anchor;
                    if (!anchors.TryGetValue(assignment.PointNumber, out anchor)) continue;

                    var parsed = points.First(p => p.PointNumber == assignment.PointNumber);
                    var style = styles[assignment.PointNumber];
                    // Settings > Tags' layer, else the feature's text layer -- unless a style
                    // assignment names one.
                    var layer = string.IsNullOrWhiteSpace(style.Layer) ? parsed.LabelLayer : style.Layer;
                    var layerId = CadUtil.EnsureLayer(db, tr, layer);
                    var contents = FieldCodes.Styles.StyleText.Compose(assignment.Text, style.Text);
                    var key = AnnotationStyler.StyleKey(style);

                    var text = styler.NewText(assignment.Text, style.Text, layerId, anchor, parsed.MarkerRotationDegrees);

                    double w, h, offsetX, offsetY;
                    if (!CadUtil.TryMeasureText(text, out w, out h, out offsetX, out offsetY))
                    {
                        text.Erase();
                        failed++;
                        continue;
                    }

                    var step = Math.Max(ts.TagOffsetPlotted, 0.01);
                    var placer = new LabelPlacer
                    {
                        BaseOffset = style.OffsetPlotted * scale,
                        RingStep = step * scale,
                        RingCount = FieldCodes.Styles.StyleResolver.RingsWithin(style.Leader.MaxLengthPlotted, style.OffsetPlotted, step, 4)
                    };
                    var placement = placer.Place(anchor.X, anchor.Y, w, h, obstacles);
                    if (!placement.Placed)
                    {
                        text.Erase();
                        failed++;
                        ed.WriteMessage("\n  {0}: no clear position for tag {1}.",
                                        assignment.PointNumber, assignment.Text);
                        continue;
                    }

                    var target = new Point3d(placement.Bounds.MinX - offsetX,
                                             placement.Bounds.MinY - offsetY, 0.0);

                    // A tag that could not sit on the first ring is far enough from
                    // its point that nothing connects the two. Tag and leader become
                    // ONE multileader in the drawing's current style, so moving the
                    // number re-routes the leader with it.
                    var wantsLeader = style.LeaderMode != LeaderMode.Never &&
                                      (style.LeaderMode == LeaderMode.Always || placement.NeedsLeader);
                    if (wantsLeader)
                    {
                        text.Erase();

                        using (var leadered = styler.NewLeadered(assignment.Text, style, layerId, target, anchor,
                                                                 parsed.MarkerRotationDegrees))
                        {
                            Ownership.Stamp(leadered, assignment.PointNumber,
                                            cfg.Version, FtfEntityKind.Tag, target,
                                            assignment.Text, key, assignment.Text, contents);
                        }
                        leaders++;
                    }
                    else
                    {
                        text.Location = target;
                        styler.Finish(text, style.Text);
                        Ownership.Stamp(text, assignment.PointNumber, cfg.Version,
                                        FtfEntityKind.Tag, target, assignment.Text, key, assignment.Text, contents);
                    }

                    obstacles.Add(new Obstacle(placement.Bounds, ObstacleClass.Hard,
                                               "tag:" + assignment.Text));

                    drawn++;
                    if (!assignment.IsNew) reissued++;
                }

                ed.WriteMessage(
                    "\nFTFTAGS: {0} tag(s) drawn -- {1} carried over unchanged, {2} newly " +
                    "issued, {3} with leaders, {4} with no clear position.\n",
                    drawn, reissued, drawn - reissued, leaders, failed);

                if (drawn - reissued > 0 && previous.Count > 0)
                    ed.WriteMessage("Existing tag numbers were preserved; new points " +
                                    "continue past the highest number issued.\n");
                styler.WriteProblems(ed, "FTFTAGS");
            });
        }

        /// <summary>
        /// Draws or refreshes the schedule. On a re-run it reappears where it already
        /// was, so a table someone positioned on the sheet does not jump.
        /// </summary>
        [CommandMethod("FTFTABLE", CommandFlags.Modal)]
        public void FtfTable()
        {
            FtfSession.Run("FTFTABLE", (db, tr, ed) =>
            {
                var cfg = FtfSession.Rules(db);
                var settings = FtfSession.SettingsFor(db, cfg);
                var ts = settings.Tags;

                var parser = new FieldCodeParser(cfg);
                Ownership.EnsureRegApp(db, tr);

                var previous = ReadPreviousTags(db, tr);
                var ann = settings.Annotation;
                if (previous.Count == 0 && !ann.ScheduleTrees)
                {
                    ed.WriteMessage("\nFTFTABLE: no tags in this drawing and the tree schedule is off " +
                                    "(Settings > Schedule). Nothing to list.\n");
                    return;
                }

                var existingCorner = ExistingTableCorner(db, tr);
                Ownership.DeleteOwnedKinds(db, tr, FtfEntityKind.Table);

                Point3d corner;
                if (existingCorner.HasValue)
                {
                    corner = existingCorner.Value;
                }
                else
                {
                    var prompt = new PromptPointOptions("\nUpper-left corner of the schedule: ");
                    var result = ed.GetPoint(prompt);
                    if (result.Status != PromptStatus.OK)
                    {
                        ed.WriteMessage("\nFTFTABLE: cancelled.\n");
                        return;
                    }
                    corner = result.Value;
                }

                var points = new List<ParsedPoint>();
                var notes = CogoNotes.For(db, tr, settings);
                foreach (var cogo in CadUtil.CogoPointsInOrder(db, tr))
                    points.Add(notes.Parse(parser, cogo));

                // Rebuild assignments from what is on the drawing, so the table always
                // agrees with the tags rather than recomputing them independently.
                var assignments = new List<TagAssignment>();
                foreach (var pair in previous)
                {
                    string prefix;
                    int number;
                    if (!TagAssigner.TrySplit(pair.Value, out prefix, out number)) continue;

                    assignments.Add(new TagAssignment
                    {
                        PointNumber = pair.Key,
                        Prefix = prefix,
                        Number = number
                    });
                }

                // Trees only when the tree schedule is on; untagged trees by point number.
                var rows = FieldCodes.Annotation.ScheduleSelection.Rows(assignments, points, ann, cfg);
                var model = TagTable.Build(rows, points, ColumnsFrom(ts),
                                           ts.TableTitle, settings.Trees.TrunkDecimals);

                if (model.RowCount == 0)
                {
                    ed.WriteMessage("\nFTFTABLE: no tagged points to list.\n");
                    return;
                }

                var scale = CadUtil.DrawingUnitsPerPlottedUnit(db);
                DrawTable(db, tr, model, corner, ts, scale, cfg.Version);

                ed.WriteMessage("\nFTFTABLE: {0} row(s) at {1:0.##},{2:0.##}.\n",
                                model.RowCount, corner.X, corner.Y);
            });
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>Point number to tag text, from the tags already in the drawing.</summary>
        private static Dictionary<string, string> ReadPreviousTags(Database db, Transaction tr)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var pair in Ownership.FindOwned(db, tr, s => s.Kind == FtfEntityKind.Tag))
            {
                var stamp = pair.Value;
                if (string.IsNullOrWhiteSpace(stamp.PointNumber)) continue;
                if (string.IsNullOrWhiteSpace(stamp.TagText)) continue;
                map[stamp.PointNumber] = stamp.TagText;
            }

            return map;
        }

        private static Point3d? ExistingTableCorner(Database db, Transaction tr)
        {
            foreach (var pair in Ownership.FindOwned(db, tr, s => s.Kind == FtfEntityKind.Table))
            {
                var table = tr.GetObject(pair.Key, OpenMode.ForRead, false, true) as Table;
                if (table != null) return table.Position;
            }
            return null;
        }

        private static IList<TagTableColumn> ColumnsFrom(FieldCodes.Settings.TagSettings ts)
        {
            var columns = new List<TagTableColumn>();

            foreach (var name in ts.TableColumns ?? new List<string>())
            {
                TagTableColumn column;
                try
                {
                    column = (TagTableColumn)Enum.Parse(typeof(TagTableColumn), name, true);
                }
                catch (ArgumentException)
                {
                    continue;   // an unknown column name is ignored, not fatal
                }
                columns.Add(column);
            }

            return columns.Count > 0 ? columns : TagTable.DefaultColumns();
        }

        private static void DrawTable(Database db, Transaction tr, TagTableModel model,
                                      Point3d corner, FieldCodes.Settings.TagSettings ts,
                                      double scale, string rulesVersion)
        {
            var textHeight = ts.TableTextHeightPlotted * scale;

            using (var table = new Table())
            {
                table.SetDatabaseDefaults(db);
                table.TableStyle = db.Tablestyle;
                table.Position = corner;
                table.LayerId = CadUtil.EnsureLayer(db, tr, ts.TableLayer);

                // Title row + header row + one row per entry.
                table.SetSize(model.RowCount + 2, model.ColumnCount);

                table.Cells.TextHeight = textHeight;

                table.Cells[0, 0].TextString = model.Title;

                for (var c = 0; c < model.ColumnCount; c++)
                {
                    table.Cells[1, c].TextString = model.Headers[c];
                    table.Columns[c].Width = ColumnWidth(model, c, textHeight);
                }

                for (var r = 0; r < model.RowCount; r++)
                    for (var c = 0; c < model.ColumnCount; c++)
                        table.Cells[r + 2, c].TextString = model.Rows[r][c] ?? string.Empty;

                // Presentation: text sits centred in its cell, and every row is the
                // same height. Without the explicit heights, any cell that wraps makes
                // its whole row taller and the schedule comes out ragged.
                table.Cells.Alignment = CellAlignment.MiddleCenter;

                table.GenerateLayout();

                // After GenerateLayout, which recomputes heights from contents: data
                // rows all get the same height, the title keeps a little extra.
                var rowHeight = textHeight * 2.0;
                table.Rows[0].Height = rowHeight * 1.2;
                for (var r = 1; r < model.RowCount + 2; r++)
                    table.Rows[r].Height = rowHeight;

                CadUtil.AddToModelSpace(db, tr, table);
                Ownership.Stamp(table, string.Empty, rulesVersion, FtfEntityKind.Table, corner);
            }
        }

        /// <summary>
        /// Width from the longest entry in the column -- header included, so a heading
        /// like STEMS can never wrap even when its data cells are empty.
        ///
        /// The old factor of 0.75 glyph-widths per character was measured too tight in
        /// practice: DECIDUOUS wrapped in the SPECIES column, and one wrapped cell makes
        /// its whole row taller than the others. A full glyph-height per character plus
        /// a one-glyph cell margin each side is deliberately generous -- a slightly wide
        /// column reads fine, a wrapped one reads broken.
        /// </summary>
        private static double ColumnWidth(TagTableModel model, int column, double textHeight)
        {
            var longest = model.Headers[column].Length;

            for (var r = 0; r < model.RowCount; r++)
            {
                var value = model.Rows[r][column];
                if (value != null && value.Length > longest) longest = value.Length;
            }

            return Math.Max(4.0, longest * textHeight) + textHeight * 2.0;
        }

        /// <summary>
        /// Existing entities a tag must not cover. Same classification the labels use:
        /// CogoPoints carry the symbol, so they are never covered.
        /// </summary>
        private static List<Obstacle> GatherObstacles(Database db, Transaction tr,
                                                      LayerClassifier classifier)
        {
            var obstacles = new List<Obstacle>();
            var ms = CadUtil.ModelSpace(db, tr, OpenMode.ForRead);

            foreach (ObjectId id in ms)
            {
                if (id.IsErased) continue;

                var entity = tr.GetObject(id, OpenMode.ForRead, false, true) as AcEntity;
                if (entity == null) continue;

                Box2d box;
                try
                {
                    var e = entity.GeometricExtents;
                    box = new Box2d(e.MinPoint.X, e.MinPoint.Y, e.MaxPoint.X, e.MaxPoint.Y);
                }
                catch (Autodesk.AutoCAD.Runtime.Exception) { continue; }

                if (entity is Autodesk.Civil.DatabaseServices.CogoPoint)
                {
                    obstacles.Add(new Obstacle(box, ObstacleClass.Hard, "cogopoint"));
                    continue;
                }

                var stamp = Ownership.Read(entity);
                if (stamp != null)
                {
                    if (stamp.Kind == FtfEntityKind.Label || stamp.Kind == FtfEntityKind.Block)
                        obstacles.Add(new Obstacle(box, ObstacleClass.Hard, "ftf"));
                    continue;
                }

                obstacles.Add(new Obstacle(box, classifier.ObstacleForLayer(entity.Layer),
                                           entity.Layer));
            }

            return obstacles;
        }
    }
}
