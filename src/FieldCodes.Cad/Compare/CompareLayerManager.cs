using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Civil.DatabaseServices;
using FieldCodes.Compare;

using AcEntity = Autodesk.AutoCAD.DatabaseServices.Entity;

namespace FieldCodes.Cad.Compare
{
    /// <summary>
    /// The only code that changes survey objects: makes approved duplicates not plot,
    /// copies the uncovered portions of partly represented lines back onto their plotting
    /// layer, and reverses all of it from the record it keeps.
    ///
    /// Every change is VERIFIED the moment it is made, with the same resolver the final
    /// QC uses (layer, visibility, block definition content, point style and label). A
    /// change that leaves the object plotting, or whose plot state cannot be determined,
    /// is reverted on the spot and reported; it is never recorded as done.
    ///
    /// Original geometry is never altered: a split is a stamped COPY of the portion that
    /// still plots, while the original moves to nonplot intact. Shared block definitions
    /// are never edited: an insert whose block draws on its own layers is pointed at a
    /// nonplot twin instead. UNTESTED in Civil 3D.
    /// </summary>
    internal static class CompareLayerManager
    {
        public const string CompareTag = "COMPARE";

        // ================================================================= layers

        public static void EnsureNonplotLayers(Database db, Transaction tr, CompareProfile profile, Editor ed)
        {
            EnsureNonplotLayers(db, tr, profile, null, ed);
        }

        /// <summary>The nonplot layers this plan needs: in fixed mode the five named ones, in
        /// suffix mode one per source layer the plan moves something from.</summary>
        public static void EnsureNonplotLayers(Database db, Transaction tr, CompareProfile profile, ChangePlan plan, Editor ed)
        {
            var wanted = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);   // nonplot layer -> source layer
            if (!profile.NonplotLayers.UseSuffix) foreach (var name in profile.NonplotLayers.All) wanted[name] = null;
            if (plan != null)
                foreach (var c in plan.Changes)
                    if (!string.IsNullOrEmpty(c.ToLayer) && !wanted.ContainsKey(c.ToLayer)) wanted[c.ToLayer] = c.FromLayer;
            foreach (var kv in wanted) EnsureNonplotLayer(db, tr, profile, kv.Key, kv.Value, ed);
        }

        /// <summary>One nonplot layer: created no-plot (colour, linetype and lineweight from
        /// the source layer when there is one, else the profile colour), or if it already
        /// exists, forced no-plot, thawed and on so the objects stay visible for review.</summary>
        public static void EnsureNonplotLayer(Database db, Transaction tr, CompareProfile profile, string name, string sourceLayer, Editor ed)
        {
            if (string.IsNullOrEmpty(name)) return;
            var table = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            LayerTableRecord record;
            if (table.Has(name))
            {
                record = (LayerTableRecord)tr.GetObject(table[name], OpenMode.ForRead);
                if (record.IsPlottable)
                {
                    record.UpgradeOpen();
                    record.IsPlottable = false;
                    if (ed != null) ed.WriteMessage("\n  Layer {0} was plottable; set to no-plot.", name);
                }
                if (record.IsFrozen || record.IsOff)
                {
                    record.UpgradeOpen();
                    record.IsFrozen = false;
                    record.IsOff = false;
                }
                return;
            }
            table.UpgradeOpen();
            record = new LayerTableRecord
            {
                Name = name,
                IsPlottable = false,
                Color = Color.FromColorIndex(ColorMethod.ByAci, profile.NonplotLayers.ColorIndex),
                Description = "FTFCOMPARE: survey objects the consultant base map already shows. Does not plot."
            };
            if (!string.IsNullOrEmpty(sourceLayer) && table.Has(sourceLayer))
            {
                var source = (LayerTableRecord)tr.GetObject(table[sourceLayer], OpenMode.ForRead);
                record.Color = source.Color;
                record.LinetypeObjectId = source.LinetypeObjectId;
                record.LineWeight = source.LineWeight;
                record.Description = "FTFCOMPARE: no-plot twin of " + sourceLayer + " for survey objects the consultant base map already shows.";
            }
            table.Add(record);
            tr.AddNewlyCreatedDBObject(record, true);
            if (ed != null) ed.WriteMessage("\n  Created nonplot layer {0}.", name);
        }

        // ================================================================== apply

        /// <summary>
        /// Brings the drawing in line with the plan: changes already applied and still in
        /// the plan are left alone, changes no longer in the plan (a reviewer chose Keep
        /// Plotting) are reversed, new ones are made and verified. Returns the record to
        /// store; <paramref name="problems"/> lists every change refused, with why.
        /// </summary>
        public static AppliedChangeSet Reconcile(Database db, Transaction tr, ChangePlan plan, ComparisonResult result, CompareProfile profile,
                                                 string rulesVersion, AppliedChangeSet existing, bool includeAutomatic, Editor ed,
                                                 out int applied, out int reversed, out int skipped, IDictionary<string, string> problems,
                                                 FtfEntityKind splitKind = FtfEntityKind.CompareSplit)
        {
            applied = reversed = skipped = 0;
            problems = problems ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var record = existing ?? new AppliedChangeSet();
            record.RunId = result != null ? result.RunId : record.RunId;
            record.Profile = profile.Name;
            record.DrawingPath = db.Filename;
            record.AppliedUtc = DateTime.UtcNow;

            var wanted = new Dictionary<string, PlannedChange>(StringComparer.OrdinalIgnoreCase);
            foreach (var change in plan.Changes)
            {
                if (!includeAutomatic && change.AutoDecided) continue;
                wanted[change.Handle] = change;
            }

            // Reverse what the plan no longer asks for, or asks for differently.
            foreach (var old in record.Changes.ToList())
            {
                PlannedChange now;
                var keep = wanted.TryGetValue(old.Handle, out now) && now.Action == old.Action &&
                           string.Equals(now.IntervalsKey, old.IntervalsKey, StringComparison.Ordinal) &&
                           string.Equals(now.ToLayer, old.NonplotLayer, StringComparison.OrdinalIgnoreCase);
                if (keep) continue;
                RestoreOne(db, tr, old, ed);
                record.Changes.Remove(old);
                reversed++;
            }

            EnsureNonplotLayers(db, tr, profile, plan, ed);
            var minUncovered = profile.Tolerances.MinUncoveredFeet * (result != null && result.UnitsPerFoot > 0 ? result.UnitsPerFoot : 1.0);
            ObjectId nonplotPointStyle = ObjectId.Null;

            foreach (var change in wanted.Values)
            {
                if (record.Find(change.Handle) != null) continue;
                var entity = Resolve(db, tr, change.Handle, OpenMode.ForWrite);
                if (entity == null)
                {
                    skipped++;
                    problems[change.Handle] = "object no longer in the drawing";
                    continue;
                }

                var done = new AppliedChange
                {
                    Handle = change.Handle,
                    Kind = change.Kind,
                    Category = change.Category,
                    EntityType = entity.GetType().Name.ToUpperInvariant(),
                    OriginalLayer = entity.Layer,
                    OriginalColor = entity.Color.ToString(),
                    OriginalLinetype = entity.Linetype,
                    OriginalLineweight = entity.LineWeight.ToString(),
                    OriginalPlotStyle = SafePlotStyle(entity),
                    OriginalTransparency = entity.Transparency.ToString(),
                    OriginalVisible = entity.Visible,
                    NonplotLayer = change.ToLayer,
                    Action = change.Action,
                    IntervalsKey = change.IntervalsKey,
                    Method = "Layer"
                };

                string problem = null;
                try
                {
                    if (change.Action == PlannedAction.SplitNonplot)
                    {
                        var feature = result != null ? result.Find(change.Handle) : null;
                        bool approximated;
                        var copies = SplitCopies(db, tr, entity, change, feature, minUncovered, rulesVersion, splitKind, out approximated, out problem);
                        if (problem == null && copies.Count == 0 && change.KeepIntervals.Count > 0)
                            problem = "the uncovered portion could not be copied";
                        if (problem == null)
                        {
                            done.SplitApproximated = approximated;
                            done.SplitHandles.AddRange(copies);
                            foreach (var h in copies)
                            {
                                var copy = Resolve(db, tr, h, OpenMode.ForRead);
                                var verdict = PlotVisibility.Of(copy, db, tr);
                                if (verdict.Plots != true) { problem = "the plotting copy would not plot (" + verdict.Note + ")"; break; }
                            }
                        }
                    }

                    if (problem == null) problem = MakeNonplot(entity, change, done, db, tr, profile, ref nonplotPointStyle);

                    if (problem == null)
                    {
                        var verdict = PlotVisibility.Of(entity, db, tr);
                        if (verdict.Plots == true) problem = "it would still plot: " + verdict.Note;
                        else if (verdict.Plots == null) problem = "its plot state could not be determined: " + verdict.Note;
                        else done.Verified = true;
                    }
                }
                catch (Autodesk.AutoCAD.Runtime.Exception ex) { problem = "AutoCAD refused the change: " + ex.Message; }
                catch (InvalidOperationException ex) { problem = ex.Message; }

                if (problem != null)
                {
                    // Undo whatever part of the change was made; the object stays as it was.
                    RestoreOne(db, tr, done, null);
                    skipped++;
                    problems[change.Handle] = problem;
                    if (ed != null) ed.WriteMessage("\n  {0}: NOT changed - {1}", change.Handle, problem);
                    continue;
                }
                record.Changes.Add(done);
                applied++;
            }
            return record;
        }

        /// <summary>Moves the object to its nonplot layer and does whatever else its kind
        /// needs so that it really stops plotting. Returns a problem, or null.</summary>
        private static string MakeNonplot(AcEntity entity, PlannedChange change, AppliedChange done, Database db, Transaction tr,
                                          CompareProfile profile, ref ObjectId nonplotPointStyle)
        {
            entity.Layer = change.ToLayer;

            var cogo = entity as CogoPoint;
            if (cogo != null)
            {
                done.OriginalLabelVisible = cogo.IsLabelVisible;
                done.OriginalPointStyle = cogo.StyleId.IsNull ? null : cogo.StyleId.Handle.ToString();
                var effective = CogoPointPlotting.Resolve(cogo, db, tr);
                if (effective.Problem != null) return "point display could not be determined (" + effective.Problem + ")";
                if (effective.StyleOverriddenByGroup)
                    return "point group '" + effective.GroupName + "' overrides the point style, so the marker would still plot; remove the override, or exclude the point";

                cogo.IsLabelVisible = false;
                var markerLayer = string.IsNullOrEmpty(effective.MarkerLayer) || effective.MarkerLayer == "0" ? change.ToLayer : effective.MarkerLayer;
                if (effective.MarkerVisible && PlotVisibility.LayerPlots(db, tr, markerLayer))
                {
                    if (!profile.NonplotLayers.RestyleFixedMarkerPoints)
                        return "its point style pins the marker to layer " + markerLayer + ", which plots; layer-only mode (nonplotLayers.restyleFixedMarkerPoints = false) leaves such points to a person";
                    // The style fixes the marker's layer: give the point the nonplot style.
                    // Suffix mode: the style's marker and label sit on layer 0, which follows
                    // the point's own (now nonplot) layer; fixed mode: on the point nonplot layer.
                    if (nonplotPointStyle.IsNull) nonplotPointStyle = CogoPointPlotting.EnsureNonplotStyle(db, tr, profile.NonplotLayers.UseSuffix ? "0" : profile.NonplotLayers.Point);
                    cogo.StyleId = nonplotPointStyle;
                    done.Method = "LayerAndPointStyle";
                }
                return null;
            }

            var br = entity as BlockReference;
            if (br != null)
            {
                foreach (ObjectId attId in br.AttributeCollection)
                {
                    if (attId.IsErased) continue;
                    var att = tr.GetObject(attId, OpenMode.ForWrite) as AttributeReference;
                    if (att == null) continue;
                    done.AttributeLayers[att.Handle.ToString()] = att.Layer;
                    att.Layer = change.ToLayer;
                }
                var definition = tr.GetObject(br.BlockTableRecord, OpenMode.ForRead) as BlockTableRecord;
                if (definition == null) return "the block definition could not be opened";
                if (!PlotVisibility.DrawsOnOwnLayers(definition, tr, 0)) return null;

                if (br.IsDynamicBlock)
                {
                    // A dynamic block's displayed geometry lives in an anonymous definition
                    // that AutoCAD manages; it is hidden as a whole rather than twinned.
                    br.Visible = false;
                    done.Method = "Invisible";
                    return null;
                }
                done.OriginalBlockDefinition = br.BlockTableRecord.Handle.ToString();
                Func<string, string> twinLayer = layer =>
                {
                    if (!profile.NonplotLayers.UseSuffix) return profile.NonplotLayers.Block;
                    if (string.IsNullOrEmpty(layer) || layer == "0") return "0";   // follows the insert, which is nonplot
                    var name = profile.NonplotLayers.For(layer, profile.NonplotLayers.Block);
                    EnsureNonplotLayer(db, tr, profile, name, layer, null);
                    return name;
                };
                var twinId = BlockTwins.Ensure(db, tr, br.BlockTableRecord, twinLayer, 0);
                done.TwinBlockDefinition = twinId.Handle.ToString();
                br.BlockTableRecord = twinId;
                done.Method = "BlockTwin";
                return null;
            }
            return null;
        }

        private static string SafePlotStyle(AcEntity entity)
        {
            try { return entity.PlotStyleName; } catch (Autodesk.AutoCAD.Runtime.Exception) { return string.Empty; }
        }

        // ================================================================== split

        /// <summary>
        /// Copies of the stations to keep plotting. The original curve is split with the real
        /// AutoCAD geometry at the closest points to the interval's end points (arcs stay arcs,
        /// elevations stay); closed curves and Civil 3D figures, which do not split cleanly,
        /// get a 3D polyline sampled along the real curve, and the record says so. Every
        /// piece is checked against the expected end points before it is kept.
        /// </summary>
        private static List<string> SplitCopies(Database db, Transaction tr, AcEntity entity, PlannedChange change, FeatureResult feature,
                                                double minUncovered, string rulesVersion, FtfEntityKind splitKind, out bool approximated, out string problem)
        {
            approximated = false;
            problem = null;
            var handles = new List<string>();
            var curve = entity as Curve;
            if (curve == null) { problem = "not a curve; cannot be split"; return handles; }

            double trueLength, start;
            try
            {
                start = curve.GetDistanceAtParameter(curve.StartParam);
                trueLength = curve.GetDistanceAtParameter(curve.EndParam) - start;
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex) { problem = "length could not be read: " + ex.Message; return handles; }
            if (trueLength <= 1e-9) { problem = "zero length"; return handles; }

            var chordLength = feature != null && feature.Length > 0 ? feature.Length : trueLength;
            var closed = IsClosed(curve);
            var civil = entity is FeatureLine;
            var tolerance = Math.Max(0.05, minUncovered * 0.25);

            foreach (var interval in change.KeepIntervals)
            {
                double a, b;
                Point3d expectedStart, expectedEnd;
                if (!StationsOf(curve, start, trueLength, chordLength, interval, out a, out b, out expectedStart, out expectedEnd)) continue;
                if (b - a < Math.Max(0.01, minUncovered * 0.5)) continue;

                AcEntity copy = null;
                if (!closed && !civil)
                {
                    try { copy = SplitPiece(curve, start, a, b, trueLength); }
                    catch (Autodesk.AutoCAD.Runtime.Exception) { copy = null; }
                    if (copy != null && !EndsMatch(copy, expectedStart, expectedEnd, tolerance))
                    {
                        copy.Dispose();
                        copy = null;
                    }
                }
                if (copy == null)
                {
                    copy = SampledPolyline(curve, start, a, b);
                    approximated = true;
                    if (copy != null && !EndsMatch(copy, expectedStart, expectedEnd, tolerance))
                    {
                        copy.Dispose();
                        problem = string.Format(CultureInfo.InvariantCulture, "the piece from station {0:0.0} to {1:0.0} did not land on the expected points", a, b);
                        return handles;
                    }
                }
                if (copy == null) { problem = "the piece could not be built"; return handles; }

                copy.SetDatabaseDefaults(db);
                copy.Layer = change.FromLayer;
                copy.Color = entity.Color;
                try { copy.Linetype = entity.Linetype; } catch (Autodesk.AutoCAD.Runtime.Exception) { }
                try { copy.LinetypeScale = entity.LinetypeScale; } catch (Autodesk.AutoCAD.Runtime.Exception) { }
                copy.LineWeight = entity.LineWeight;
                try { copy.PlotStyleName = entity.PlotStyleName; } catch (Autodesk.AutoCAD.Runtime.Exception) { }
                try { copy.Transparency = entity.Transparency; } catch (Autodesk.AutoCAD.Runtime.Exception) { }
                copy.Visible = true;
                CadUtil.AddToModelSpace(db, tr, copy);
                Ownership.Stamp(copy, change.Handle, rulesVersion ?? string.Empty, splitKind, null, splitKind == FtfEntityKind.TrimSplit ? "TRIM" : CompareTag);
                handles.Add(copy.Handle.ToString());
            }
            return handles;
        }

        /// <summary>Stations on the real curve for an interval: from its recorded end points
        /// (closest points on the curve) when it has them, else by scaled station.</summary>
        private static bool StationsOf(Curve curve, double start, double trueLength, double chordLength, CoverageInterval interval,
                                       out double a, out double b, out Point3d expectedStart, out Point3d expectedEnd)
        {
            a = b = 0;
            expectedStart = expectedEnd = Point3d.Origin;
            var havePoints = interval.StartPoint.X != 0 || interval.StartPoint.Y != 0 || interval.EndPoint.X != 0 || interval.EndPoint.Y != 0;
            try
            {
                if (havePoints)
                {
                    expectedStart = curve.GetClosestPointTo(ToPoint3d(interval.StartPoint, curve), false);
                    expectedEnd = curve.GetClosestPointTo(ToPoint3d(interval.EndPoint, curve), false);
                    a = curve.GetDistAtPoint(expectedStart) - start;
                    b = curve.GetDistAtPoint(expectedEnd) - start;
                }
                else
                {
                    var scale = trueLength / chordLength;
                    a = Math.Max(0, interval.Start * scale);
                    b = Math.Min(trueLength, interval.End * scale);
                    expectedStart = curve.GetPointAtDist(start + a);
                    expectedEnd = curve.GetPointAtDist(start + b);
                }
            }
            catch (Autodesk.AutoCAD.Runtime.Exception) { return false; }
            if (b < a) { var t = a; a = b; b = t; var p = expectedStart; expectedStart = expectedEnd; expectedEnd = p; }
            a = Math.Max(0, a);
            b = Math.Min(trueLength, b);
            return true;
        }

        private static Point3d ToPoint3d(CPoint p, Curve curve)
        {
            var z = p.HasZ ? p.Z : curve.StartPoint.Z;
            return new Point3d(p.X, p.Y, z);
        }

        private static bool EndsMatch(AcEntity piece, Point3d expectedStart, Point3d expectedEnd, double tolerance)
        {
            var c = piece as Curve;
            if (c == null) return false;
            try
            {
                var s = Flat(c.StartPoint); var e = Flat(c.EndPoint);
                var es = Flat(expectedStart); var ee = Flat(expectedEnd);
                return (s.DistanceTo(es) <= tolerance && e.DistanceTo(ee) <= tolerance) ||
                       (s.DistanceTo(ee) <= tolerance && e.DistanceTo(es) <= tolerance);
            }
            catch (Autodesk.AutoCAD.Runtime.Exception) { return false; }
        }

        private static Point3d Flat(Point3d p) { return new Point3d(p.X, p.Y, 0); }

        private static bool IsClosed(Curve curve)
        {
            var pl = curve as Autodesk.AutoCAD.DatabaseServices.Polyline;
            if (pl != null) return pl.Closed;
            var p2 = curve as Polyline2d;
            if (p2 != null) return p2.Closed;
            var p3 = curve as Polyline3d;
            if (p3 != null) return p3.Closed;
            return curve.Closed;
        }

        private static AcEntity SplitPiece(Curve curve, double start, double a, double b, double length)
        {
            const double eps = 1e-6;
            var cutStart = a > eps;
            var cutEnd = b < length - eps;
            if (!cutStart && !cutEnd) return (AcEntity)curve.Clone();

            var parameters = new DoubleCollection();
            if (cutStart) parameters.Add(curve.GetParameterAtDistance(start + a));
            if (cutEnd) parameters.Add(curve.GetParameterAtDistance(start + b));
            var pieces = curve.GetSplitCurves(parameters);
            try
            {
                if (pieces.Count == 0) return null;
                var index = cutStart ? 1 : 0;
                if (index >= pieces.Count) index = pieces.Count - 1;
                var piece = pieces[index] as AcEntity;
                for (var i = 0; i < pieces.Count; i++) if (i != index) pieces[i].Dispose();
                return piece;
            }
            finally { pieces.Dispose(); }
        }

        private static AcEntity SampledPolyline(Curve curve, double start, double a, double b)
        {
            var points = new Point3dCollection();
            var step = Math.Max(0.25, (b - a) / 400.0);
            for (var d = a; d < b; d += step)
            {
                try { points.Add(curve.GetPointAtDist(start + d)); } catch (Autodesk.AutoCAD.Runtime.Exception) { }
            }
            try { points.Add(curve.GetPointAtDist(start + b)); } catch (Autodesk.AutoCAD.Runtime.Exception) { }
            if (points.Count < 2) return null;
            return new Polyline3d(Poly3dType.SimplePoly, points, false);
        }

        // ================================================================ restore

        /// <summary>Puts every recorded object back exactly, erases the split copies, then
        /// erases any split copy no record owns and any block twin nothing uses.</summary>
        public static int RestoreAll(Database db, Transaction tr, AppliedChangeSet applied, Editor ed, out int orphansErased, out int twinsErased)
        {
            return RestoreAll(db, tr, applied, ed, out orphansErased, out twinsErased, FtfEntityKind.CompareSplit);
        }

        /// <summary>Puts every recorded object back and erases every split copy of the given
        /// kind (the comparison's and the trim's copies are stamped apart so each restore
        /// takes only its own).</summary>
        public static int RestoreAll(Database db, Transaction tr, AppliedChangeSet applied, Editor ed, out int orphansErased, out int twinsErased, FtfEntityKind splitKind)
        {
            var restored = 0;
            if (applied != null)
                foreach (var change in applied.Changes)
                    if (RestoreOne(db, tr, change, ed)) restored++;

            orphansErased = 0;
            foreach (var pair in Ownership.FindOwned(db, tr, s => s.Kind == splitKind))
            {
                var entity = tr.GetObject(pair.Key, OpenMode.ForWrite, false, true) as AcEntity;
                if (entity == null) continue;
                entity.Erase();
                orphansErased++;
            }
            twinsErased = BlockTwins.Purge(db, tr);
            return restored;
        }

        private static bool RestoreOne(Database db, Transaction tr, AppliedChange change, Editor ed)
        {
            var ok = false;
            var entity = Resolve(db, tr, change.Handle, OpenMode.ForWrite);
            if (entity != null)
            {
                if (LayerExists(db, tr, change.OriginalLayer)) entity.Layer = change.OriginalLayer;
                else if (ed != null) ed.WriteMessage("\n  {0}: original layer {1} no longer exists; left on {2}.", change.Handle, change.OriginalLayer, entity.Layer);
                if (change.OriginalVisible.HasValue) entity.Visible = change.OriginalVisible.Value;

                var br = entity as BlockReference;
                if (br != null)
                {
                    if (change.AttributeLayers != null)
                        foreach (ObjectId attId in br.AttributeCollection)
                        {
                            if (attId.IsErased) continue;
                            var att = tr.GetObject(attId, OpenMode.ForWrite) as AttributeReference;
                            if (att == null) continue;
                            string layer;
                            if (change.AttributeLayers.TryGetValue(att.Handle.ToString(), out layer) && LayerExists(db, tr, layer)) att.Layer = layer;
                        }
                    if (!string.IsNullOrEmpty(change.OriginalBlockDefinition))
                    {
                        var original = ResolveId(db, change.OriginalBlockDefinition);
                        if (!original.IsNull) br.BlockTableRecord = original;
                        else if (ed != null) ed.WriteMessage("\n  {0}: its original block definition no longer exists; left on the nonplot twin.", change.Handle);
                    }
                }

                var cogo = entity as CogoPoint;
                if (cogo != null)
                {
                    if (change.OriginalLabelVisible.HasValue) cogo.IsLabelVisible = change.OriginalLabelVisible.Value;
                    if (!string.IsNullOrEmpty(change.OriginalPointStyle))
                    {
                        var style = ResolveId(db, change.OriginalPointStyle);
                        if (!style.IsNull) cogo.StyleId = style;
                        else if (ed != null) ed.WriteMessage("\n  {0}: its original point style no longer exists; style left as is.", change.Handle);
                    }
                }
                ok = true;
            }
            foreach (var h in change.SplitHandles)
            {
                var copy = Resolve(db, tr, h, OpenMode.ForWrite);
                if (copy != null) copy.Erase();
            }
            return ok;
        }

        private static bool LayerExists(Database db, Transaction tr, string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            var table = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            return table.Has(name);
        }

        // ================================================================ resolve

        public static AcEntity Resolve(Database db, Transaction tr, string handle, OpenMode mode)
        {
            var id = ResolveId(db, handle);
            if (id.IsNull) return null;
            try { return tr.GetObject(id, mode, false, true) as AcEntity; }
            catch (Autodesk.AutoCAD.Runtime.Exception) { return null; }
        }

        public static ObjectId ResolveId(Database db, string handle)
        {
            long value;
            if (string.IsNullOrWhiteSpace(handle) || !long.TryParse(handle, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value)) return ObjectId.Null;
            ObjectId id;
            return db.TryGetObjectId(new Handle(value), out id) && !id.IsErased ? id : ObjectId.Null;
        }

        // ===================================================================== QC

        /// <summary>Everything the final QC looks at, in one read-only pass: the real plot
        /// state of every object in the results and every split copy.</summary>
        public static QcInput GatherQc(Database db, Transaction tr, CompareProfile profile, ComparisonResult result, ChangePlan plan,
                                       AppliedChangeSet applied, string outputFolder)
        {
            var input = new QcInput { Profile = profile, Result = result, Plan = plan, Applied = applied, DrawingPath = db.Filename, OutputFolder = outputFolder };

            var layers = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            Func<string, LayerState> layerState = name =>
            {
                if (string.IsNullOrEmpty(name) || !layers.Has(name)) return new LayerState { Name = name, Exists = false };
                var record = (LayerTableRecord)tr.GetObject(layers[name], OpenMode.ForRead);
                return new LayerState { Name = name, Exists = true, Plottable = record.IsPlottable, Frozen = record.IsFrozen, Off = record.IsOff };
            };
            var nonplotNames = new List<string>();
            if (!profile.NonplotLayers.UseSuffix) nonplotNames.AddRange(profile.NonplotLayers.All);
            if (plan != null) nonplotNames.AddRange(plan.Changes.Select(c => c.ToLayer));
            if (applied != null) nonplotNames.AddRange(applied.Changes.Select(c => c.NonplotLayer));
            foreach (var name in nonplotNames.Where(n => !string.IsNullOrEmpty(n)).Distinct(StringComparer.OrdinalIgnoreCase)) input.Layers[name] = layerState(name);

            Func<string, EntityState> stateOf = handle =>
            {
                var entity = Resolve(db, tr, handle, OpenMode.ForRead);
                if (entity == null) return new EntityState { Handle = handle, Exists = false };
                var verdict = PlotVisibility.Of(entity, db, tr);
                if (!input.Layers.ContainsKey(entity.Layer)) input.Layers[entity.Layer] = layerState(entity.Layer);
                return new EntityState { Handle = handle, Exists = true, Layer = entity.Layer, Plots = verdict.Plots, PlotNote = verdict.Note };
            };

            if (result != null)
                foreach (var r in result.Results) input.Entities[r.Handle] = stateOf(r.Handle);

            var owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (applied != null)
                foreach (var change in applied.Changes)
                    foreach (var h in change.SplitHandles)
                    {
                        owned.Add(h);
                        input.Entities[h] = stateOf(h);
                    }
            foreach (var pair in Ownership.FindOwned(db, tr, s => s.Kind == FtfEntityKind.CompareSplit))
            {
                var handle = pair.Key.Handle.ToString();
                if (!owned.Contains(handle)) input.StaleSplitHandles.Add(handle);
            }

            input.Xrefs.AddRange(CompareDrawingReader.XrefStates(db, tr));

            if (!string.IsNullOrEmpty(outputFolder))
            {
                try
                {
                    Directory.CreateDirectory(outputFolder);
                    var probe = Path.Combine(outputFolder, ".ftf-write-test");
                    File.WriteAllText(probe, "ok");
                    File.Delete(probe);
                    input.OutputFolderWritable = true;
                }
                catch (IOException) { input.OutputFolderWritable = false; }
                catch (UnauthorizedAccessException) { input.OutputFolderWritable = false; }
            }

            var clouds = CompareReviewMarks.Count(db, tr);
            if (clouds > 0)
                input.Warnings.Add(clouds + " revision cloud/note object(s) on layer " + profile.Review.Layer + " mark discrepancies and will plot; delete them, or run FTFCOMPARERESTORE, if the deliverable should not carry them.");
            if (applied != null)
            {
                var approximated = applied.Changes.Count(c => c.SplitApproximated);
                if (approximated > 0)
                    input.Warnings.Add(approximated + " split copy(ies) were sampled along the real curve as 3D polylines (closed polylines or Civil 3D figures): check them by eye.");
                var hidden = applied.Changes.Count(c => c.Method == "Invisible");
                if (hidden > 0)
                    input.Warnings.Add(hidden + " dynamic block insert(s) whose definition draws on its own layers were hidden rather than moved; FTFCOMPARERESTORE makes them visible again.");
            }
            return input;
        }
    }
}
