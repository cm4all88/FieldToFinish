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
using FieldCodes.Lots;
using FieldCodes.Settings;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace FieldCodes.Cad
{
    /// <summary>
    /// FTFLOTS: divides a platted block into lots. The drafter picks the block's corners (or a closed
    /// polyline around it), a popup asks which side the lots front, how many and how wide, how deep,
    /// where an alley runs, where each lot drains, and the setbacks and easements, and FTF draws the
    /// lot lines with their bearings and distances, the alley, the building setback and easement
    /// lines, lot numbers and areas, drainage arrows, the block title and the plat notes.
    ///
    /// The geometry and every word come from FieldCodes.Lots, which is unit tested; this file only
    /// picks and draws. Like FTFDRAWLINE and FTFROW, what it draws is the user's deliberate work:
    /// stamped DraftLine and DraftAnnotation, managed by FTFDRAFTCLEAN. Layers are looked up, never
    /// created from a configured name.
    ///
    /// Run headless against a drawing by tests\LiveSmoke\run-rowlots.ps1: a block divided into
    /// four lots with a rear alley, setbacks, easements, drainage and the checks. The popup itself
    /// has still never been shown -- accoreconsole has no window, so there the same questions are
    /// asked on the command line (see AskOnCommandLine).
    /// </summary>
    public sealed class LotCommands
    {
        private const string Lots = "FTFLOTS";

        private sealed class Standard
        {
            public LotSettings Lots;
            public DraftingLineType LotType;
            public DraftingLineType AlleyType;
            public ObjectId LotLayer;
            public string LotLinetype;
            public ObjectId LotAnnotationLayer;
            public ObjectId AlleyLayer;
            public string AlleyLinetype;
            public ObjectId AlleyAnnotationLayer;
            public ObjectId SetbackLayer;
            public ObjectId EasementLayer;
            public ObjectId LotTextLayer;
            public ObjectId DrainLayer;
            public ObjectId NotesLayer;
            public ObjectId TextStyle;
            public double TextHeight;
            public double TextOffset;
            public double UnitsPerFoot;
            public string Version;
        }

        [CommandMethod("FTFLOTS", CommandFlags.Modal)]
        public void FtfLots()
        {
            FtfSession.Run("FTFLOTS", (db, tr, ed) =>
            {
                RulesConfig rules = null;
                try { rules = FtfSession.Rules(db); }
                catch (ConfigException) { }
                var settings = FtfSession.ResolveSettings(db, rules).Settings;

                // Fail before the corners are picked, not after the popup is filled in.
                var std = ResolveStandard(db, tr, ed, settings);
                if (std == null) return;

                var job = new LotJob { UnitsPerFoot = std.UnitsPerFoot };
                var labels = new List<string>();
                if (!PickBlock(db, tr, ed, job, labels)) { ed.WriteMessage("\nFTFLOTS: cancelled -- nothing drawn.\n"); return; }

                var filled = EasementInspectCommands.Headless()
                    ? AskOnCommandLine(ed, job, std.Lots, labels)
                    : ShowForm(job, std.Lots, labels);
                if (!filled) { ed.WriteMessage("\nFTFLOTS: cancelled -- nothing drawn.\n"); return; }

                var plan = LotBuilder.Build(job);
                if (!plan.Ok)
                {
                    foreach (var error in plan.Errors) ed.WriteMessage("\nFTFLOTS: {0}", error);
                    ed.WriteMessage("\nNothing was drawn.\n");
                    return;
                }

                LotLabels.Apply(plan, job, std.Lots, std.LotType, std.AlleyType, std.UnitsPerFoot, "%%d");
                if (!ResolveRoleLayers(db, tr, ed, std, plan)) return;

                var created = Draw(db, tr, std, plan);

                foreach (var note in plan.Notes) ed.WriteMessage("\n  {0}", note);
                foreach (var warning in plan.Warnings) ed.WriteMessage("\n  WARNING: {0}", warning);
                ed.WriteMessage("\nFTFLOTS: drew {0} object(s): {1} lot(s), {2} line(s){3}.\n", created, plan.Lots.Count, plan.Lines.Count,
                                plan.Alley != null ? ", an alley" : string.Empty);
            });
        }

        // --------------------------------------------------------------- picking

        /// <summary>The block's corners in order: clicked (survey points snap), or taken from a closed
        /// polyline of straight sides.</summary>
        private static bool PickBlock(Database db, Transaction tr, Editor ed, LotJob job, IList<string> labels)
        {
            ed.WriteMessage("\nPick the block's corners in order -- survey points snap. Enter closes the block.");
            while (true)
            {
                var count = job.Corners.Count;
                var prompt = count == 0
                    ? "\nFirst corner [Polyline]: "
                    : string.Format(CultureInfo.InvariantCulture, "\nCorner {0}{1}: ", count + 1, count >= 3 ? " <done>" : string.Empty);
                var options = new PromptPointOptions(prompt) { AllowNone = count >= 3 };
                if (count == 0)
                {
                    options.Keywords.Add("Polyline");
                    options.AppendKeywordsToMessage = false;
                }
                else
                {
                    options.UseBasePoint = true;
                    options.BasePoint = new Point3d(job.Corners[count - 1].X, job.Corners[count - 1].Y, 0);
                }

                SelectedLocation location;
                string keyword;
                var status = EasementCommands.PickLocation(db, tr, ed, options, out location, out keyword);
                if (status == PromptStatus.Keyword) return PickPolyline(tr, ed, job, labels);
                if (status == PromptStatus.Cancel) return false;
                if (location == null) return job.Corners.Count >= 3;

                job.Corners.Add(location.Point);
                labels.Add(string.IsNullOrWhiteSpace(location.PointNumber) ? (count + 1).ToString(CultureInfo.InvariantCulture) : location.PointNumber);
                ed.WriteMessage("\n  Corner {0}: {1}", count + 1,
                    string.IsNullOrWhiteSpace(location.PointNumber) ? "picked location" : "survey point " + location.PointNumber);
            }
        }

        private static bool PickPolyline(Transaction tr, Editor ed, LotJob job, IList<string> labels)
        {
            var options = new PromptEntityOptions("\nSelect the closed polyline around the block: ");
            options.SetRejectMessage("\nThat is not a polyline.");
            options.AddAllowedClass(typeof(Autodesk.AutoCAD.DatabaseServices.Polyline), true);
            var picked = ed.GetEntity(options);
            if (picked.Status != PromptStatus.OK) return false;

            var polyline = tr.GetObject(picked.ObjectId, OpenMode.ForRead) as Autodesk.AutoCAD.DatabaseServices.Polyline;
            if (polyline == null) return false;
            if (!polyline.Closed)
            {
                ed.WriteMessage("\nFTFLOTS: that polyline is not closed.");
                return false;
            }

            for (var i = 0; i < polyline.NumberOfVertices; i++)
            {
                if (Math.Abs(polyline.GetBulgeAt(i)) > 1e-9)
                {
                    ed.WriteMessage("\nFTFLOTS: that polyline has curved segments. Pick the corners instead; round them in the popup.");
                    return false;
                }
                var point = polyline.GetPoint2dAt(i);
                job.Corners.Add(new P2(point.X, point.Y));
                labels.Add((i + 1).ToString(CultureInfo.InvariantCulture));
            }
            return job.Corners.Count >= 3;
        }

        // Kept out of FtfLots so the command line never loads the window's types.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static bool ShowForm(LotJob job, LotSettings settings, IList<string> labels)
        {
            using (var form = new Ui.LotForm(job, settings, labels))
                return AcadApp.ShowModalDialog(form) == System.Windows.Forms.DialogResult.OK;
        }

        // ------------------------------------------------ the popup, on the command line

        /// <summary>
        /// What <see cref="Ui.LotForm"/> asks, asked on the command line instead: accoreconsole has
        /// no window to show. The same questions in the same order with the same defaults, so a
        /// headless run and a drafter's run divide the block the same way.
        /// </summary>
        private static bool AskOnCommandLine(Editor ed, LotJob job, LotSettings settings, IList<string> labels)
        {
            var name = DraftPrompts.Text(ed, Lots, "Block name or letter", job.BlockName);
            if (name == null) return false;
            job.BlockName = name;

            int first;
            if (!DraftPrompts.Count(ed, Lots, "First lot number", job.FirstLotNumber, 1, out first)) return false;
            job.FirstLotNumber = first;

            ed.WriteMessage("\nThe block's sides, each from one corner to the next:");
            for (var i = 0; i < job.Corners.Count; i++)
                ed.WriteMessage("\n  Side {0}: {1} to {2}, {3:0.00}'", i + 1, labels[i], labels[(i + 1) % job.Corners.Count],
                                SideFeet(job, i));

            job.RowA = AskRow(ed, job, settings, "The first row", 0);
            if (job.RowA == null) return false;

            job.RowB = null;
            var second = EasementCommands.Keyword(ed, "\nA second row of lots on another side [Yes/No] <No>: ", "No", "Yes", "No");
            if (second == null) return false;
            if (second == "Yes")
            {
                job.RowB = AskRow(ed, job, settings, "The second row", Math.Min(2, job.Corners.Count - 1));
                if (job.RowB == null) return false;
            }

            if (!AskAlley(ed, job, settings)) return false;
            if (!AskDrainage(ed, job, settings)) return false;
            if (!AskOffsets(ed, job, settings)) return false;
            return AskRadii(ed, job, labels);
        }

        private static double SideFeet(LotJob job, int side)
        {
            var n = job.Corners.Count;
            return job.Corners[side].DistanceTo(job.Corners[(side + 1) % n]) / job.UnitsPerFoot;
        }

        /// <summary>One row of lots: the side it fronts, how many and how wide, how deep, and what
        /// happens when the frontages do not add up to the side. Null when cancelled.</summary>
        private static LotRow AskRow(Editor ed, LotJob job, LotSettings settings, string what, int defaultSide)
        {
            int side;
            if (!DraftPrompts.Count(ed, Lots, what + ": lots front which side (1-" + job.Corners.Count + ")",
                                    defaultSide + 1, 1, out side)) return null;
            if (side > job.Corners.Count)
            {
                ed.WriteMessage("\nFTFLOTS: this block has {0} sides.", job.Corners.Count);
                return null;
            }

            var row = new LotRow { FrontSide = side - 1 };
            var frontage = SideFeet(job, row.FrontSide);

            int count;
            if (!DraftPrompts.Count(ed, Lots, what + ": how many lots", 4, 1, out count)) return null;

            double extra;
            if (!DraftPrompts.Feet(ed, Lots, what + ": extra frontage on the corner lots (ft)", 0.0, false, out extra)) return null;

            var equal = LotRow.EqualWidths(frontage, count, extra);
            ed.WriteMessage("\n  Dividing {0:0.00}' into {1} lot(s): {2}", frontage, count,
                            string.Join(", ", equal.Select(w => w.ToString("0.##", CultureInfo.InvariantCulture)).ToArray()));

            var typed = DraftPrompts.Text(ed, Lots, what + ": frontages, comma separated, or Enter for that division", string.Empty);
            if (typed == null) return null;
            if (typed.Length > 0)
            {
                var widths = new List<double>();
                foreach (var part in typed.Split(',', ' '))
                {
                    if (part.Trim().Length == 0) continue;
                    double w;
                    if (!double.TryParse(part.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out w) || !(w > 0))
                    {
                        ed.WriteMessage("\nFTFLOTS: \"{0}\" is not a frontage.", part.Trim());
                        return null;
                    }
                    widths.Add(w);
                }
                if (widths.Count == 0)
                {
                    ed.WriteMessage("\nFTFLOTS: {0} has no lots.", what);
                    return null;
                }
                row.WidthsFeet.AddRange(widths);
            }
            else row.WidthsFeet.AddRange(equal);

            double depth;
            if (!DraftPrompts.Feet(ed, Lots, what + ": lot depth (ft, 0 runs them to the back)", 0.0, false, out depth)) return null;
            row.DepthFeet = depth;

            var close = EasementCommands.Keyword(ed, "\n" + what + ": if the frontages miss the side [Refuse/FirstLot/LastLot] <Refuse>: ",
                                                 "Refuse", "Refuse", "FirstLot", "LastLot");
            if (close == null) return null;
            row.Close = close == "FirstLot" ? LotWidthClose.FirstLot : close == "LastLot" ? LotWidthClose.LastLot : LotWidthClose.Refuse;
            return row;
        }

        private static bool AskAlley(Editor ed, LotJob job, LotSettings settings)
        {
            var alley = EasementCommands.Keyword(ed, "\nAlley [None/Rear/MidBlock] <None>: ", "None", "None", "Rear", "MidBlock");
            if (alley == null) return false;
            job.Alley = alley == "Rear" ? LotAlleyMode.Rear : alley == "MidBlock" ? LotAlleyMode.MidBlock : LotAlleyMode.None;
            if (job.Alley == LotAlleyMode.None)
            {
                job.AlleyWidthFeet = 0;
                return true;
            }

            double width;
            if (!DraftPrompts.Feet(ed, Lots, "Alley width (ft)", settings.DefaultAlleyWidthFeet, true, out width)) return false;
            job.AlleyWidthFeet = width;

            if (job.Alley != LotAlleyMode.Rear) return true;
            int back;
            if (!DraftPrompts.Count(ed, Lots, "The alley runs along which side (0 is the one furthest from the front)", 0, 0, out back)) return false;
            if (back > job.Corners.Count)
            {
                ed.WriteMessage("\nFTFLOTS: this block has {0} sides.", job.Corners.Count);
                return false;
            }
            job.BackSide = back - 1;
            return true;
        }

        /// <summary>Where the lots drain, for the block and then for any lot that differs.</summary>
        private static bool AskDrainage(Editor ed, LotJob job, LotSettings settings)
        {
            var drain = EasementCommands.Keyword(ed, "\nEvery lot drains [Front/Rear/Left/Right/Both/None] <Front>: ",
                                                 "Front", "Front", "Rear", "Left", "Right", "Both", "None");
            if (drain == null) return false;
            job.DefaultDrain = Drain(drain);

            double slope;
            if (!DraftPrompts.Feet(ed, Lots, "Slope (%)", settings.DefaultSlopePercent, false, out slope)) return false;
            job.SlopePercent = slope;

            job.DrainOverrides.Clear();
            var typed = DraftPrompts.Text(ed, Lots, "Lots that drain differently, e.g. 3=Rear 5=None, or Enter for none", string.Empty);
            if (typed == null) return false;
            foreach (var part in typed.Split(',', ' '))
            {
                if (part.Trim().Length == 0) continue;
                var halves = part.Split('=');
                int number;
                if (halves.Length != 2 || !int.TryParse(halves[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
                {
                    ed.WriteMessage("\nFTFLOTS: \"{0}\" is not a lot number and a direction.", part.Trim());
                    return false;
                }
                var where = halves[1].Trim();
                if (!DrainNames.Any(n => string.Equals(n, where, StringComparison.OrdinalIgnoreCase)))
                {
                    ed.WriteMessage("\nFTFLOTS: \"{0}\" is not one of {1}.", where, string.Join(", ", DrainNames));
                    return false;
                }
                job.DrainOverrides[number] = Drain(where);
            }
            return true;
        }

        private static readonly string[] DrainNames = { "Front", "Rear", "Left", "Right", "Both", "None" };

        private static LotDrain Drain(string keyword)
        {
            switch ((keyword ?? string.Empty).Trim().ToUpperInvariant())
            {
                case "REAR": return LotDrain.ToRear;
                case "LEFT": return LotDrain.ToLeft;
                case "RIGHT": return LotDrain.ToRight;
                case "BOTH": return LotDrain.FrontAndRear;
                case "NONE": return LotDrain.None;
                default: return LotDrain.ToFront;
            }
        }

        /// <summary>The setbacks, easements and the minimums each lot is checked against. A zero
        /// draws nothing for that one, exactly as the popup's hint says.</summary>
        private static bool AskOffsets(Editor ed, LotJob job, LotSettings settings)
        {
            var fields = new[]
            {
                Offset("Front setback (ft)", settings.DefaultFrontSetbackFeet, v => job.FrontSetbackFeet = v),
                Offset("Side setback (ft)", settings.DefaultSideSetbackFeet, v => job.SideSetbackFeet = v),
                Offset("Corner lot street-side setback (ft)", settings.DefaultCornerSideSetbackFeet, v => job.CornerSideSetbackFeet = v),
                Offset("Rear setback (ft)", settings.DefaultRearSetbackFeet, v => job.RearSetbackFeet = v),
                Offset("Front utility easement (ft)", settings.DefaultFrontEasementFeet, v => job.FrontEasementFeet = v),
                Offset("Side utility easement (ft)", settings.DefaultSideEasementFeet, v => job.SideEasementFeet = v),
                Offset("Rear utility easement (ft)", settings.DefaultRearEasementFeet, v => job.RearEasementFeet = v),
                Offset("Minimum lot area (sq ft)", settings.MinimumLotAreaSqFt, v => job.MinLotAreaSqFt = v),
                Offset("Minimum frontage (ft)", settings.MinimumLotWidthFeet, v => job.MinLotWidthFeet = v)
            };

            foreach (var field in fields)
            {
                double value;
                if (!DraftPrompts.Feet(ed, Lots, field.Key, field.Value.Key, false, out value)) return false;
                field.Value.Value(value);
            }
            return true;
        }

        private static KeyValuePair<string, KeyValuePair<double, Action<double>>> Offset(string caption, double start, Action<double> set)
        {
            return new KeyValuePair<string, KeyValuePair<double, Action<double>>>(
                caption, new KeyValuePair<double, Action<double>>(start, set));
        }

        private static bool AskRadii(Editor ed, LotJob job, IList<string> labels)
        {
            job.CornerRadiiFeet.Clear();
            for (var i = 0; i < job.Corners.Count; i++)
            {
                double radius;
                if (!DraftPrompts.Feet(ed, Lots, "Radius at block corner " + labels[i] + " (ft, 0 is a sharp corner)", 0.0, false, out radius)) return false;
                job.CornerRadiiFeet.Add(radius);
            }
            return true;
        }

        // ------------------------------------------------------------- standard

        private static Standard ResolveStandard(Database db, Transaction tr, Editor ed, FtfSettings settings)
        {
            var lots = settings.Lots;
            var lotType = settings.Drafting != null ? settings.Drafting.FindType(lots.LotLineTypeName) : null;
            var alleyType = settings.Drafting != null ? settings.Drafting.FindType(lots.AlleyTypeName) : null;
            if (lotType == null || !lotType.Enabled)
            {
                ed.WriteMessage("\nFTFLOTS: the Drafting Lines type \"{0}\" is missing or disabled. Set the type under " +
                                "FTF Settings > Platted Blocks.\n", lots.LotLineTypeName);
                return null;
            }

            var std = new Standard
            {
                Lots = lots,
                LotType = lotType,
                AlleyType = alleyType,
                LotLinetype = string.Empty,
                AlleyLinetype = string.Empty,
                Version = settings.Version,
                UnitsPerFoot = settings.General.UnitsPerFoot > 0 ? settings.General.UnitsPerFoot : 1.0
            };

            if (!LineStandard(db, tr, ed, lotType, out std.LotLayer, out std.LotLinetype, out std.LotAnnotationLayer)) return null;

            // The alley's type is only needed when an alley is drawn, which the popup decides; a type that
            // is set up is resolved now, one that is not is refused later with the same explanation.
            std.LotTextLayer = OptionalLayer(db, tr, ed, lots.LotTextLayer, "lot numbers and areas", std.LotAnnotationLayer);
            if (std.LotTextLayer.IsNull) return null;
            std.DrainLayer = OptionalLayer(db, tr, ed, lots.DrainLayer, "drainage arrows", std.LotTextLayer);
            if (std.DrainLayer.IsNull) return null;
            std.NotesLayer = OptionalLayer(db, tr, ed, lots.NotesLayer, "plat notes", std.LotTextLayer);
            if (std.NotesLayer.IsNull) return null;

            std.TextStyle = Setup.DrawingResources.FindTextStyle(db, tr, string.IsNullOrWhiteSpace(lots.TextStyle) ? lotType.TextStyle : lots.TextStyle);
            var scale = CadUtil.DrawingUnitsPerPlottedUnit(db);
            std.TextHeight = lots.TextHeightPlotted * scale;
            std.TextOffset = lots.TextOffsetPlotted * scale;
            return std;
        }

        /// <summary>A Drafting Lines type's layer, linetype and annotation layer, against the drawing.</summary>
        private static bool LineStandard(Database db, Transaction tr, Editor ed, DraftingLineType type,
                                         out ObjectId layer, out string linetype, out ObjectId annotationLayer)
        {
            layer = ObjectId.Null;
            linetype = string.Empty;
            annotationLayer = ObjectId.Null;

            if (string.IsNullOrWhiteSpace(type.Layer))
            {
                ed.WriteMessage("\nFTFLOTS: \"{0}\" has no layer configured. Set it under FTF Settings > Drafting Lines.\n", type.Name);
                return false;
            }
            layer = DraftPrimitives.FindLayer(db, tr, type.Layer);
            if (layer.IsNull)
            {
                ed.WriteMessage("\nFTFLOTS: layer \"{0}\" (the {1} line layer) is not in this drawing. FTF never creates office " +
                                "layers from a configured name.\n", type.Layer, type.Name);
                return false;
            }

            if (!string.IsNullOrWhiteSpace(type.Linetype))
            {
                var linetypes = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
                if (!linetypes.Has(type.Linetype))
                {
                    ed.WriteMessage("\nFTFLOTS: linetype \"{0}\" (configured for {1}) is not loaded in this drawing.\n", type.Linetype, type.Name);
                    return false;
                }
                linetype = type.Linetype.Trim();
            }

            string textLayer;
            if (!string.IsNullOrWhiteSpace(type.AnnotationLayer))
            {
                if (DraftPrimitives.FindLayer(db, tr, type.AnnotationLayer).IsNull)
                {
                    ed.WriteMessage("\nFTFLOTS: annotation layer \"{0}\" (configured for {1}) is not in this drawing.\n", type.AnnotationLayer, type.Name);
                    return false;
                }
                textLayer = type.AnnotationLayer.Trim();
            }
            else
            {
                var catalog = FtfLineworkService.LayerNames(db, tr);
                textLayer = LabelLayerResolver.Resolve(type.Layer.Trim(), null, catalog, type.Layer.Trim()).Layer;
            }
            annotationLayer = DraftPrimitives.FindLayer(db, tr, textLayer);
            if (annotationLayer.IsNull) annotationLayer = layer;
            return true;
        }

        /// <summary>The layers only some plans need: the alley's, and the setback and easement lines'.</summary>
        private static bool ResolveRoleLayers(Database db, Transaction tr, Editor ed, Standard std, LotPlan plan)
        {
            if (plan.Alley != null || plan.Lines.Any(l => l.Kind == LotLineKind.Alley))
            {
                if (std.AlleyType == null || !std.AlleyType.Enabled)
                {
                    ed.WriteMessage("\nFTFLOTS: the Drafting Lines type \"{0}\" for the alley is missing or disabled. Set it under " +
                                    "FTF Settings > Platted Blocks.\n", std.Lots.AlleyTypeName);
                    return false;
                }
                if (!LineStandard(db, tr, ed, std.AlleyType, out std.AlleyLayer, out std.AlleyLinetype, out std.AlleyAnnotationLayer)) return false;
            }

            if (plan.Envelopes.Any(e => e.Kind == LotOffsetKind.Setback && e.Polygon.Count >= 3))
            {
                std.SetbackLayer = RequireLayer(db, tr, ed, std.Lots.SetbackLayer, "building setback lines");
                if (std.SetbackLayer.IsNull) return false;
            }
            if (plan.Envelopes.Any(e => e.Kind == LotOffsetKind.Easement && e.Polygon.Count >= 3))
            {
                std.EasementLayer = RequireLayer(db, tr, ed, std.Lots.EasementLayer, "utility easement lines");
                if (std.EasementLayer.IsNull) return false;
            }
            return true;
        }

        private static ObjectId RequireLayer(Database db, Transaction tr, Editor ed, string name, string what)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                ed.WriteMessage("\nFTFLOTS: no layer is set for {0}. Set it under FTF Settings > Platted Blocks.\n", what);
                return ObjectId.Null;
            }
            var id = DraftPrimitives.FindLayer(db, tr, name);
            if (id.IsNull)
                ed.WriteMessage("\nFTFLOTS: layer \"{0}\" (set for {1}) is not in this drawing. FTF never creates office layers " +
                                "from a configured name.\n", name, what);
            return id;
        }

        private static ObjectId OptionalLayer(Database db, Transaction tr, Editor ed, string name, string what, ObjectId fallback)
        {
            return string.IsNullOrWhiteSpace(name) ? fallback : RequireLayer(db, tr, ed, name, what);
        }

        // --------------------------------------------------------------- drawing

        private static int Draw(Database db, Transaction tr, Standard std, LotPlan plan)
        {
            Ownership.EnsureRegApp(db, tr);
            var c = new DraftContext
            {
                Db = db, Tr = tr, Version = std.Version, Source = "LOTS",
                TextStyle = std.TextStyle, TextHeight = std.TextHeight, TextOffset = std.TextOffset
            };
            var count = 0;
            var centre = plan.Lots.Count > 0
                ? new P2(plan.Lots.Average(l => l.LabelPoint.X), plan.Lots.Average(l => l.LabelPoint.Y))
                : new P2((plan.Minimum.X + plan.Maximum.X) / 2.0, (plan.Minimum.Y + plan.Maximum.Y) / 2.0);

            foreach (var line in plan.Lines)
            {
                var alley = line.Kind == LotLineKind.Alley;
                var perimeter = line.Kind == LotLineKind.Perimeter;
                var layer = alley ? std.AlleyLayer : std.LotLayer;
                var type = alley ? std.AlleyType : std.LotType;
                var annotation = alley ? std.AlleyAnnotationLayer : std.LotAnnotationLayer;

                if (!perimeter || std.Lots.DrawBlockPerimeter)
                    count += DraftPrimitives.DrawCourse(c, line.Course, layer, alley ? std.AlleyLinetype : std.LotLinetype);

                if (perimeter && line.Course.Kind == CourseKind.Line)
                    count += LabelInside(c, line, annotation, centre);
                else
                    count += DraftPrimitives.Annotate(c, line.Course, line.Text, annotation, type.Placement);
            }

            foreach (var envelope in plan.Envelopes)
            {
                if (envelope.Polygon.Count < 3) continue;
                var layer = envelope.Kind == LotOffsetKind.Setback ? std.SetbackLayer : std.EasementLayer;
                count += DraftPrimitives.DrawOutline(c, envelope.Polygon, layer);
            }

            foreach (var lot in plan.Lots)
            {
                count += DraftPrimitives.DrawBlock(c, lot.Text, lot.LabelPoint, lot.Along, std.LotTextLayer);
                foreach (var arrow in lot.Drains) count += DrawArrow(c, std, arrow);
            }

            if (plan.Alley != null && !string.IsNullOrEmpty(plan.Alley.Text))
                count += DraftPrimitives.DrawBlock(c, new List<string> { plan.Alley.Text }, plan.Alley.LabelPoint,
                                                   plan.Lots.Count > 0 ? plan.Lots[0].Along : new P2(1, 0), std.LotTextLayer);

            if (!string.IsNullOrEmpty(plan.Title))
                count += DraftPrimitives.DrawText(c, plan.Title,
                    new Point3d((plan.Minimum.X + plan.Maximum.X) / 2.0, plan.Maximum.Y + 2.5 * std.TextHeight, 0.0), 0.0, std.NotesLayer);

            for (var i = 0; i < plan.GeneralNotes.Count; i++)
                count += DraftPrimitives.DrawText(c, plan.GeneralNotes[i],
                    new Point3d(plan.Maximum.X + 3.0 * std.TextHeight, plan.Maximum.Y - i * 1.6 * std.TextHeight, 0.0), 0.0, std.NotesLayer, false);
            return count;
        }

        /// <summary>A distance on the block's own boundary, set on the lot side of the line so it does
        /// not sit on the right-of-way drawing's own labels.</summary>
        private static int LabelInside(DraftContext c, LotLine line, ObjectId layer, P2 inside)
        {
            if (line.Text.Count == 0) return 0;
            var a = line.Course.Start;
            var b = line.Course.End;
            var along = (b - a).Normalized();
            var mid = (a + b) * 0.5;
            var toInside = inside - mid;
            var side = P2.Cross(along, toInside) >= 0 ? 1.0 : -1.0;
            var normal = new P2(-along.Y, along.X) * side;
            var gap = c.TextOffset + c.TextHeight / 2.0;
            var rotation = LineLabelPlanner.NormalizeReadable(Math.Atan2(along.Y, along.X));
            return DraftPrimitives.DrawText(c, line.Text[0], DraftPrimitives.Pt(mid + normal * gap), rotation, layer);
        }

        /// <summary>A drainage arrow: the shaft, two barbs at the tip and the slope beside it.</summary>
        private static int DrawArrow(DraftContext c, Standard std, LotDrainArrow arrow)
        {
            var from = DraftPrimitives.Pt(arrow.From);
            var to = DraftPrimitives.Pt(arrow.To);
            var shaft = to - from;
            if (shaft.Length < 1e-9) return 0;

            var count = DraftPrimitives.DrawSegment(c, from, to, std.DrainLayer);
            var unit = shaft.GetNormal();
            var barb = Math.Min(shaft.Length * 0.3, c.TextHeight * 2.5);
            count += DraftPrimitives.DrawSegment(c, to, to - unit.RotateBy(0.45, Vector3d.ZAxis) * barb, std.DrainLayer);
            count += DraftPrimitives.DrawSegment(c, to, to - unit.RotateBy(-0.45, Vector3d.ZAxis) * barb, std.DrainLayer);

            if (!string.IsNullOrEmpty(arrow.Text))
            {
                var readable = LineLabelPlanner.NormalizeReadable(Math.Atan2(shaft.Y, shaft.X));
                var up = new Vector3d(-Math.Sin(readable), Math.Cos(readable), 0.0);
                var mid = new Point3d((from.X + to.X) / 2.0, (from.Y + to.Y) / 2.0, 0.0);
                count += DraftPrimitives.DrawText(c, arrow.Text, mid + up * (c.TextOffset + c.TextHeight), readable, std.DrainLayer);
            }
            return count;
        }
    }
}
