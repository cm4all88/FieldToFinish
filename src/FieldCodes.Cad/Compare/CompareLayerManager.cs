using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using FieldCodes.Compare;

using AcEntity = Autodesk.AutoCAD.DatabaseServices.Entity;

namespace FieldCodes.Cad.Compare
{
    /// <summary>
    /// The only code that changes survey objects: moves approved duplicates to the
    /// comparison nonplot layers, copies the uncovered portions of partly represented
    /// lines back onto their plotting layer, and reverses all of it from the record
    /// it keeps. Original geometry is never altered: a split is a stamped COPY of the
    /// portion that still plots, while the original moves to nonplot intact.
    ///
    /// UNTESTED in Civil 3D.
    /// </summary>
    internal static class CompareLayerManager
    {
        public const string CompareTag = "COMPARE";

        // ================================================================= layers

        public static void EnsureNonplotLayers(Database db, Transaction tr, CompareProfile profile, Editor ed)
        {
            var table = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            foreach (var name in profile.NonplotLayers.All)
            {
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
                        // Nonplot objects must stay visible on screen for review.
                        record.UpgradeOpen();
                        record.IsFrozen = false;
                        record.IsOff = false;
                    }
                    continue;
                }
                table.UpgradeOpen();
                record = new LayerTableRecord
                {
                    Name = name,
                    IsPlottable = false,
                    Color = Color.FromColorIndex(ColorMethod.ByAci, profile.NonplotLayers.ColorIndex),
                    Description = "FTFCOMPARE: survey objects the consultant base map already shows. Does not plot."
                };
                table.Add(record);
                tr.AddNewlyCreatedDBObject(record, true);
                if (ed != null) ed.WriteMessage("\n  Created nonplot layer {0}.", name);
            }
        }

        // ================================================================== apply

        /// <summary>
        /// Brings the drawing in line with the plan: changes already applied and still in
        /// the plan are left alone, changes no longer in the plan (a reviewer chose Keep
        /// Plotting) are reversed, new ones are made. Returns the record to store.
        /// </summary>
        public static AppliedChangeSet Reconcile(Database db, Transaction tr, ChangePlan plan, ComparisonResult result, CompareProfile profile,
                                                 string rulesVersion, AppliedChangeSet existing, bool includeAutomatic, Editor ed,
                                                 out int applied, out int reversed, out int skipped)
        {
            applied = reversed = skipped = 0;
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

            EnsureNonplotLayers(db, tr, profile, ed);
            var minUncovered = profile.Tolerances.MinUncoveredFeet * (result != null && result.UnitsPerFoot > 0 ? result.UnitsPerFoot : 1.0);

            foreach (var change in wanted.Values)
            {
                if (record.Find(change.Handle) != null) continue;
                var entity = Resolve(db, tr, change.Handle, OpenMode.ForWrite);
                if (entity == null)
                {
                    skipped++;
                    if (ed != null) ed.WriteMessage("\n  {0}: object no longer in the drawing; skipped.", change.Handle);
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
                    NonplotLayer = change.ToLayer,
                    Action = change.Action,
                    IntervalsKey = change.IntervalsKey
                };

                if (change.Action == PlannedAction.SplitNonplot)
                {
                    var feature = result != null ? result.Find(change.Handle) : null;
                    bool approximated;
                    var copies = SplitCopies(db, tr, entity, change, feature, minUncovered, rulesVersion, out approximated);
                    done.SplitApproximated = approximated;
                    if (copies.Count == 0 && change.KeepIntervals.Count > 0)
                    {
                        skipped++;
                        if (ed != null) ed.WriteMessage("\n  {0}: the uncovered portion could not be copied; left plotting as is.", change.Handle);
                        continue;
                    }
                    done.SplitHandles.AddRange(copies);
                }

                entity.Layer = change.ToLayer;
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
                }
                record.Changes.Add(done);
                applied++;
            }
            return record;
        }

        private static string SafePlotStyle(AcEntity entity)
        {
            try { return entity.PlotStyleName; } catch (Autodesk.AutoCAD.Runtime.Exception) { return string.Empty; }
        }

        // ================================================================== split

        /// <summary>
        /// Copies of the stations to keep plotting. The original curve is split with the
        /// real AutoCAD geometry (arcs stay arcs); closed curves and Civil 3D figures,
        /// which do not split cleanly, get a 3D polyline sampled along the real curve
        /// instead, and the record says so.
        /// </summary>
        private static List<string> SplitCopies(Database db, Transaction tr, AcEntity entity, PlannedChange change, FeatureResult feature,
                                                double minUncovered, string rulesVersion, out bool approximated)
        {
            approximated = false;
            var handles = new List<string>();
            var curve = entity as Curve;
            if (curve == null) return handles;

            double trueLength;
            double start;
            try
            {
                start = curve.GetDistanceAtParameter(curve.StartParam);
                trueLength = curve.GetDistanceAtParameter(curve.EndParam) - start;
            }
            catch (Autodesk.AutoCAD.Runtime.Exception) { return handles; }
            if (trueLength <= 1e-9) return handles;

            // Stations were measured along the chorded copy; scale them onto the real curve.
            var chordLength = feature != null && feature.Length > 0 ? feature.Length : trueLength;
            var scale = trueLength / chordLength;
            var closed = IsClosed(curve);
            var civil = entity is Autodesk.Civil.DatabaseServices.FeatureLine;

            foreach (var interval in change.KeepIntervals)
            {
                var a = Math.Max(0, interval.Start * scale);
                var b = Math.Min(trueLength, interval.End * scale);
                if (b - a < Math.Max(0.01, minUncovered * 0.5)) continue;

                AcEntity copy = null;
                if (!closed && !civil)
                {
                    try { copy = SplitPiece(curve, start, a, b, trueLength); }
                    catch (Autodesk.AutoCAD.Runtime.Exception) { copy = null; }
                }
                if (copy == null)
                {
                    copy = SampledPolyline(curve, start, a, b, db);
                    approximated = true;
                }
                if (copy == null) continue;

                copy.SetDatabaseDefaults(db);
                copy.Layer = change.FromLayer;
                copy.Color = entity.Color;
                try { copy.Linetype = entity.Linetype; } catch (Autodesk.AutoCAD.Runtime.Exception) { }
                try { copy.LinetypeScale = entity.LinetypeScale; } catch (Autodesk.AutoCAD.Runtime.Exception) { }
                copy.LineWeight = entity.LineWeight;
                try { copy.PlotStyleName = entity.PlotStyleName; } catch (Autodesk.AutoCAD.Runtime.Exception) { }
                CadUtil.AddToModelSpace(db, tr, copy);
                Ownership.Stamp(copy, change.Handle, rulesVersion ?? string.Empty, FtfEntityKind.CompareSplit, null, CompareTag);
                handles.Add(copy.Handle.ToString());
            }
            return handles;
        }

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

        private static AcEntity SampledPolyline(Curve curve, double start, double a, double b, Database db)
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

        /// <summary>Puts every recorded object back on its original layer and erases the
        /// split copies, then erases any split copy no record owns. Returns objects restored.</summary>
        public static int RestoreAll(Database db, Transaction tr, AppliedChangeSet applied, Editor ed, out int orphansErased)
        {
            var restored = 0;
            if (applied != null)
                foreach (var change in applied.Changes)
                    if (RestoreOne(db, tr, change, ed)) restored++;

            var owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (applied != null) foreach (var c in applied.Changes) foreach (var h in c.SplitHandles) owned.Add(h);
            orphansErased = 0;
            foreach (var pair in Ownership.FindOwned(db, tr, s => s.Kind == FtfEntityKind.CompareSplit))
            {
                var entity = tr.GetObject(pair.Key, OpenMode.ForWrite, false, true) as AcEntity;
                if (entity == null) continue;
                entity.Erase();
                orphansErased++;
            }
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
                var br = entity as BlockReference;
                if (br != null && change.AttributeLayers != null)
                {
                    foreach (ObjectId attId in br.AttributeCollection)
                    {
                        if (attId.IsErased) continue;
                        var att = tr.GetObject(attId, OpenMode.ForWrite) as AttributeReference;
                        if (att == null) continue;
                        string layer;
                        if (change.AttributeLayers.TryGetValue(att.Handle.ToString(), out layer) && LayerExists(db, tr, layer)) att.Layer = layer;
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
            if (string.IsNullOrWhiteSpace(handle)) return null;
            long value;
            if (!long.TryParse(handle, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value)) return null;
            ObjectId id;
            if (!db.TryGetObjectId(new Handle(value), out id) || id.IsNull || id.IsErased) return null;
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

        /// <summary>Everything the final QC looks at, in one read-only pass.</summary>
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
            foreach (var name in profile.NonplotLayers.All) input.Layers[name] = layerState(name);

            if (result != null)
                foreach (var r in result.Results)
                {
                    var entity = Resolve(db, tr, r.Handle, OpenMode.ForRead);
                    input.Entities[r.Handle] = new EntityState { Handle = r.Handle, Exists = entity != null, Layer = entity != null ? entity.Layer : null };
                    if (entity != null && !input.Layers.ContainsKey(entity.Layer)) input.Layers[entity.Layer] = layerState(entity.Layer);
                }

            var owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (applied != null)
                foreach (var change in applied.Changes)
                    foreach (var h in change.SplitHandles)
                    {
                        owned.Add(h);
                        var copy = Resolve(db, tr, h, OpenMode.ForRead);
                        input.Entities[h] = new EntityState { Handle = h, Exists = copy != null, Layer = copy != null ? copy.Layer : null };
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

            input.Warnings.AddRange(BlockDefinitionWarnings(db, tr, applied, profile));
            if (applied != null && applied.Changes.Any(c => c.EntityType == "COGOPOINT"))
                input.Warnings.Add(applied.Changes.Count(c => c.EntityType == "COGOPOINT") +
                    " COGO point(s) were moved to a nonplot layer. A point style whose marker or label layer is fixed (not layer 0 / entity layer) still plots: check the styles in use, or set those layers to no-plot.");
            return input;
        }

        /// <summary>Blocks moved to nonplot whose definitions draw on their own layers: those
        /// layers still plot. Reported, since the block cannot be changed here.</summary>
        private static IEnumerable<string> BlockDefinitionWarnings(Database db, Transaction tr, AppliedChangeSet applied, CompareProfile profile)
        {
            var warnings = new List<string>();
            if (applied == null) return warnings;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var layers = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            foreach (var change in applied.Changes.Where(c => c.EntityType == "BLOCKREFERENCE" && c.Action != PlannedAction.None))
            {
                var br = Resolve(db, tr, change.Handle, OpenMode.ForRead) as BlockReference;
                if (br == null) continue;
                var btr = tr.GetObject(br.BlockTableRecord, OpenMode.ForRead) as BlockTableRecord;
                if (btr == null || !seen.Add(btr.Name)) continue;
                var other = new List<string>();
                foreach (ObjectId id in btr)
                {
                    var inner = tr.GetObject(id, OpenMode.ForRead, false, true) as AcEntity;
                    if (inner == null || inner is AttributeDefinition) continue;
                    var layer = inner.Layer;
                    if (layer == "0" || profile.NonplotLayers.Contains(layer) || other.Contains(layer)) continue;
                    if (layers.Has(layer) && !((LayerTableRecord)tr.GetObject(layers[layer], OpenMode.ForRead)).IsPlottable) continue;
                    other.Add(layer);
                }
                if (other.Count > 0)
                    warnings.Add("Block " + btr.Name + " draws on layer(s) " + string.Join(", ", other.ToArray()) +
                                 " inside its definition; those parts still plot although the insert is nonplot. Redefine the block on layer 0 or set those layers to no-plot.");
            }
            return warnings;
        }
    }
}
