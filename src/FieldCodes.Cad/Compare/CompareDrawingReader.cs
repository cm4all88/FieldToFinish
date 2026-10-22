using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.DatabaseServices.Filters;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Civil.DatabaseServices;
using FieldCodes.Compare;

using AcEntity = Autodesk.AutoCAD.DatabaseServices.Entity;
using AcPolyline = Autodesk.AutoCAD.DatabaseServices.Polyline;
using CivilPipe = Autodesk.Civil.DatabaseServices.Pipe;
using CivilStructure = Autodesk.Civil.DatabaseServices.Structure;

namespace FieldCodes.Cad.Compare
{
    /// <summary>Where the consultant's data is: the survey drawing's xrefs, or a separate file.</summary>
    internal sealed class ConsultantSourceSpec
    {
        [Newtonsoft.Json.JsonProperty("mode")] public string Mode { get; set; }
        [Newtonsoft.Json.JsonProperty("filePath")] public string FilePath { get; set; }
        /// <summary>Xref names to use; null or empty means every xref in the drawing.</summary>
        [Newtonsoft.Json.JsonProperty("xrefNames")] public List<string> XrefNames { get; set; }
        /// <summary>Comparison limits chosen for this drawing (overrides the profile's).</summary>
        [Newtonsoft.Json.JsonProperty("boundary")] public List<double[]> Boundary { get; set; }

        public ConsultantSourceSpec() { Mode = "Xrefs"; XrefNames = new List<string>(); Boundary = new List<double[]>(); }

        [Newtonsoft.Json.JsonIgnore] public bool IsFile { get { return string.Equals(Mode, "File", StringComparison.OrdinalIgnoreCase); } }
        [Newtonsoft.Json.JsonIgnore] public bool HasBoundary { get { return Boundary != null && Boundary.Count >= 3; } }

        public string ToJson() { return Newtonsoft.Json.JsonConvert.SerializeObject(this, CompareJson.Settings); }
        public static ConsultantSourceSpec FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try { return Newtonsoft.Json.JsonConvert.DeserializeObject<ConsultantSourceSpec>(json, CompareJson.Settings); }
            catch (Newtonsoft.Json.JsonException) { return null; }
        }

        public string Describe()
        {
            if (IsFile) return "file " + (FilePath ?? "(none)");
            return XrefNames == null || XrefNames.Count == 0 ? "every xref in the drawing" : "xref(s) " + string.Join(", ", XrefNames.ToArray());
        }
    }

    /// <summary>
    /// Extracts comparable features from drawings into the CAD-free model. Reads only:
    /// nothing here opens an object for write. Geometry is sampled into chords in the
    /// survey drawing's coordinates, with every xref and block transform applied.
    /// Anything that could not be read is recorded as an issue or a NotEvaluated
    /// feature rather than dropped.
    ///
    /// UNTESTED in Civil 3D: compiled against the 2024 API; see the README section on
    /// FTFCOMPARE for the verification steps.
    /// </summary>
    internal static class CompareDrawingReader
    {
        private const int MaxNesting = 8;

        // ================================================================= units

        /// <summary>Drawing units per foot from INSUNITS, or null when undefined.</summary>
        public static double? UnitsPerFootOf(Database db)
        {
            // By INSUNITS code rather than enum member, so the US survey units added in
            // later releases (21-24) resolve on every host without a compile-time dependency.
            switch ((int)db.Insunits)
            {
                case 1: return 12.0;            // inches
                case 2: return 1.0;             // feet
                case 4: return 304.8;           // millimetres
                case 5: return 30.48;           // centimetres
                case 6: return 0.3048;          // metres
                case 10: return 1.0 / 3.0;      // yards
                case 14: return 3.048;          // decimetres
                case 21: return 1.0;            // US survey feet (3.048006 mm/ft difference is below tolerance)
                case 22: return 12.0;           // US survey inches
                case 23: return 1.0 / 3.0;      // US survey yards
                default: return null;
            }
        }

        // ================================================================= survey

        /// <summary>
        /// Every survey feature in model space (or only the given objects). Xref
        /// inserts are never survey; earlier comparison split copies are skipped
        /// (restore removes them anyway).
        /// </summary>
        public static CompareDataset ReadSurvey(Database db, Transaction tr, CompareProfile profile,
                                                ICollection<ObjectId> onlyThese, ICompareProgress progress)
        {
            var dataset = new CompareDataset
            {
                Source = CompareSource.Survey,
                Name = string.IsNullOrEmpty(db.Filename) ? "(unsaved drawing)" : Path.GetFileName(db.Filename)
            };
            var upf = UnitsPerFootOf(db);
            dataset.UnitsPerFoot = upf ?? 1.0;
            if (!upf.HasValue)
                dataset.AddIssue(Severity.Info, "UNITS", "The survey drawing's INSUNITS is undefined; feet are assumed.", false);
            if (!string.IsNullOrEmpty(db.Filename)) dataset.FilesRead.Add(db.Filename);

            var ids = new List<ObjectId>();
            if (onlyThese != null) ids.AddRange(onlyThese);
            else
            {
                var ms = CadUtil.ModelSpace(db, tr, OpenMode.ForRead);
                foreach (ObjectId id in ms) ids.Add(id);
            }

            var context = new ReadContext
            {
                Dataset = dataset, Profile = profile, Progress = progress, Transform = Affine2.Identity, OwnerPath = string.Empty,
                ChordTolerance = profile.Tolerances.ChordToleranceFeet * dataset.UnitsPerFoot, IsSurvey = true
            };
            var n = 0;
            foreach (var id in ids)
            {
                if (progress != null && progress.IsCancelled) throw new OperationCanceledException("Comparison cancelled.");
                if (id.IsErased) continue;
                var entity = tr.GetObject(id, OpenMode.ForRead, false, true) as AcEntity;
                if (entity == null) continue;

                var stamp = Ownership.Read(entity);
                if (stamp != null && (stamp.Kind == FtfEntityKind.CompareSplit || stamp.Kind == FtfEntityKind.CompareCloud)) continue;

                var br = entity as BlockReference;
                if (br != null && IsXref(tr, br)) continue;

                // Survey objects that do not plot as the drawing stands (frozen, off or
                // no-plot layer, invisible) are not deliverable content: left out, counted.
                var verdict = PlotVisibility.Of(entity, db, tr);
                if (verdict.Plots == false)
                {
                    dataset.CountHidden(verdict.Note);
                    continue;
                }

                ReadEntity(entity, tr, context);
                if (++n % 500 == 0 && progress != null) progress.Step(n, ids.Count);
            }
            context.Finish();
            return dataset;
        }

        // ============================================================= consultant

        /// <summary>
        /// The consultant's data: resolved xrefs of the host drawing (geometry already in
        /// host coordinates and units, nested references followed), or a separate file read
        /// as a side database (its own xrefs resolved by path, units converted only on the
        /// profile's explicit say-so). Unloaded or unresolved references make the dataset
        /// INCOMPLETE so that nothing can be declared missing against it.
        /// </summary>
        public static CompareDataset ReadConsultant(Database hostDb, Transaction tr, ConsultantSourceSpec spec, CompareProfile profile,
                                                    double surveyUnitsPerFoot, ICompareProgress progress)
        {
            spec = spec ?? new ConsultantSourceSpec();
            var dataset = new CompareDataset { Source = CompareSource.Consultant, UnitsPerFoot = surveyUnitsPerFoot };
            var context = new ReadContext
            {
                Dataset = dataset, Profile = profile, Progress = progress, Transform = Affine2.Identity, OwnerPath = string.Empty,
                ChordTolerance = profile.Tolerances.ChordToleranceFeet * surveyUnitsPerFoot, IsSurvey = false
            };
            var opened = new Dictionary<string, Database>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (spec.IsFile)
                {
                    if (string.IsNullOrWhiteSpace(spec.FilePath) || !File.Exists(spec.FilePath))
                        throw new ConfigException("Consultant drawing not found: " + (spec.FilePath ?? "(none)"));
                    dataset.Name = Path.GetFileName(spec.FilePath);
                    var side = OpenSide(spec.FilePath, opened);
                    var fileUpf = UnitsPerFootOf(side);
                    var transform = FileTransform(profile, fileUpf, surveyUnitsPerFoot, dataset);
                    context.Transform = transform;
                    context.OwnerPath = Path.GetFileNameWithoutExtension(spec.FilePath);
                    context.HostDirectory = Path.GetDirectoryName(spec.FilePath);
                    ReadDatabase(side, context, opened, 0);
                }
                else
                {
                    dataset.Name = "xrefs of " + (string.IsNullOrEmpty(hostDb.Filename) ? "the drawing" : Path.GetFileName(hostDb.Filename));
                    context.HostDirectory = string.IsNullOrEmpty(hostDb.Filename) ? null : Path.GetDirectoryName(hostDb.Filename);
                    context.HostLayers = HostLayerStates(hostDb, tr);
                    var wanted = spec.XrefNames != null && spec.XrefNames.Count > 0
                        ? new HashSet<string>(spec.XrefNames, StringComparer.OrdinalIgnoreCase) : null;
                    var found = 0;
                    var ms = CadUtil.ModelSpace(hostDb, tr, OpenMode.ForRead);
                    foreach (ObjectId id in ms)
                    {
                        if (id.IsErased) continue;
                        var br = tr.GetObject(id, OpenMode.ForRead, false, true) as BlockReference;
                        if (br == null) continue;
                        var btr = tr.GetObject(br.BlockTableRecord, OpenMode.ForRead) as BlockTableRecord;
                        if (btr == null || !btr.IsFromExternalReference) continue;
                        if (wanted != null && !wanted.Contains(btr.Name)) continue;
                        found++;
                        ReadXrefInsert(br, btr, tr, context, opened, 0);
                    }
                    if (found == 0)
                        dataset.AddIssue(Severity.Error, "NO-SOURCE",
                            wanted == null ? "The drawing has no external references to compare against." : "None of the chosen xrefs are inserted in the drawing.", true);
                }
            }
            finally
            {
                foreach (var db in opened.Values) { try { db.Dispose(); } catch (Autodesk.AutoCAD.Runtime.Exception) { } }
            }
            context.Finish();
            return dataset;
        }

        private static Affine2 FileTransform(CompareProfile profile, double? fileUpf, double surveyUpf, CompareDataset dataset)
        {
            var scale = 1.0;
            var declared = profile.Units.ConsultantUnitsPerFoot;
            if (declared.HasValue)
            {
                scale = surveyUpf / declared.Value;
                if (fileUpf.HasValue && Math.Abs(fileUpf.Value - declared.Value) > 1e-9)
                    dataset.AddIssue(Severity.Warning, "UNITS", string.Format(CultureInfo.InvariantCulture,
                        "The consultant file's INSUNITS says {0} unit(s) per foot but the profile declares {1}; the profile's value is used.", fileUpf.Value, declared.Value), false);
            }
            else if (fileUpf.HasValue && Math.Abs(fileUpf.Value - surveyUpf) > 1e-9)
            {
                dataset.AddIssue(Severity.Error, "UNITS", string.Format(CultureInfo.InvariantCulture,
                    "The consultant file is in different units ({0} per foot vs {1}). Set units.consultantUnitsPerFoot in the profile to confirm the conversion.", fileUpf.Value, surveyUpf), true);
                dataset.UnitsPerFoot = fileUpf.Value;   // the engine refuses the mismatch
            }
            var t = Affine2.Insertion(profile.Units.ConsultantShiftX, profile.Units.ConsultantShiftY, profile.Units.ConsultantRotationDegrees * Math.PI / 180.0, scale);
            if (profile.Units.HasTransform)
                dataset.AddIssue(Severity.Info, "TRANSFORM", "Consultant file transformed per the profile: " + t, false);
            else if (Math.Abs(scale - 1) > 1e-12)
                dataset.AddIssue(Severity.Info, "UNITS", string.Format(CultureInfo.InvariantCulture, "Consultant file scaled by {0:0.######} per the profile's units.", scale), false);
            return t;
        }

        private static Database OpenSide(string path, Dictionary<string, Database> opened)
        {
            var full = Path.GetFullPath(path);
            Database db;
            if (opened.TryGetValue(full, out db)) return db;
            db = new Database(false, true);
            db.ReadDwgFile(full, FileShare.Read, true, string.Empty);
            db.CloseInput(true);
            opened[full] = db;
            return db;
        }

        /// <summary>A resolved xref insert in a host drawing: its database as AutoCAD loaded it.</summary>
        private static void ReadXrefInsert(BlockReference br, BlockTableRecord btr, Transaction tr, ReadContext context,
                                           Dictionary<string, Database> opened, int depth)
        {
            var name = btr.Name;
            var path = btr.PathName;
            if (depth > MaxNesting)
            {
                context.Dataset.AddIssue(Severity.Warning, "XREF-NESTING", "Reference " + name + " is nested deeper than " + MaxNesting + " levels and was not read.", true);
                return;
            }

            // An insert the drawing does not show is not consultant information in this
            // deliverable; comparing against it would suppress ours for nothing.
            var insertDb = br.Database;
            if (!br.Visible || !PlotVisibility.LayerPlots(insertDb, tr, br.Layer))
            {
                context.Dataset.AddIssue(Severity.Error, "XREF-HIDDEN", "Reference " + name + " is not shown (insert invisible, or its layer " + br.Layer +
                    " frozen, off or no-plot); thaw it or detach it before comparing.", true);
                return;
            }

            Database xdb = null;
            var status = btr.XrefStatus;
            switch (status)
            {
                case XrefStatus.Resolved:
                    try { xdb = btr.GetXrefDatabase(false); } catch (Autodesk.AutoCAD.Runtime.Exception) { xdb = null; }
                    break;
                case XrefStatus.Unloaded:
                    context.Dataset.AddIssue(Severity.Error, "XREF-UNLOADED", "Reference " + name + " is unloaded (" + path + "). Reload it before comparing.", true);
                    return;
                case XrefStatus.FileNotFound:
                    context.Dataset.AddIssue(Severity.Error, "XREF-UNRESOLVED", "Reference " + name + " was not found: " + path, true);
                    return;
                case XrefStatus.Unresolved:
                    context.Dataset.AddIssue(Severity.Error, "XREF-UNRESOLVED", "Reference " + name + " is unresolved: " + path, true);
                    return;
                default:
                    context.Dataset.AddIssue(Severity.Error, "XREF-UNRESOLVED", "Reference " + name + " is " + status + ": " + path, true);
                    return;
            }

            if (xdb == null)
            {
                // Resolved by AutoCAD but not handed out (a side database host, for instance):
                // open it ourselves from its path.
                var resolved = ResolvePath(path, context.HostDirectory, br.Database);
                if (resolved == null)
                {
                    context.Dataset.AddIssue(Severity.Error, "XREF-UNRESOLVED", "Reference " + name + " could not be opened: " + path, true);
                    return;
                }
                try { xdb = OpenSide(resolved, opened); }
                catch (Autodesk.AutoCAD.Runtime.Exception ex)
                {
                    context.Dataset.AddIssue(Severity.Error, "XREF-UNRESOLVED", "Reference " + name + " could not be read (" + ex.Message + "): " + resolved, true);
                    return;
                }
                catch (IOException ex)
                {
                    context.Dataset.AddIssue(Severity.Error, "XREF-UNRESOLVED", "Reference " + name + " could not be read (" + ex.Message + "): " + resolved, true);
                    return;
                }
            }

            var child = context.Child(FromMatrix(br.BlockTransform), name);
            var clip = ReadClip(br, tr, context);
            if (clip != null)
            {
                child.Clips.Add(clip);
                context.Dataset.AddIssue(Severity.Info, "XREF-CLIPPED", "Reference " + name + " is clipped" + (clip.Inverted ? " (inverted)" : string.Empty) +
                    "; only the content inside the clip is compared.", false);
            }
            else if (IsClipped(br, tr))
            {
                context.Dataset.AddIssue(Severity.Error, "XREF-CLIPPED", "Reference " + name + " is clipped but the clip boundary could not be read; its content is left out of the comparison.", true);
                return;
            }
            child.HostDirectory = Path.GetDirectoryName(SafeFilename(xdb)) ?? context.HostDirectory;
            if (!string.IsNullOrEmpty(SafeFilename(xdb)) && !context.Dataset.FilesRead.Contains(SafeFilename(xdb), StringComparer.OrdinalIgnoreCase))
                context.Dataset.FilesRead.Add(SafeFilename(xdb));
            ReadDatabase(xdb, child, opened, depth + 1);
        }

        private static string SafeFilename(Database db)
        {
            try { return db.Filename; } catch (Autodesk.AutoCAD.Runtime.Exception) { return null; }
        }

        private static string ResolvePath(string pathName, string hostDirectory, Database db)
        {
            if (string.IsNullOrWhiteSpace(pathName)) return null;
            try
            {
                if (Path.IsPathRooted(pathName) && File.Exists(pathName)) return pathName;
                if (!string.IsNullOrEmpty(hostDirectory))
                {
                    var relative = Path.GetFullPath(Path.Combine(hostDirectory, pathName));
                    if (File.Exists(relative)) return relative;
                    var byName = Path.Combine(hostDirectory, Path.GetFileName(pathName));
                    if (File.Exists(byName)) return byName;
                }
                var found = HostApplicationServices.Current.FindFile(pathName, db, FindFileHint.XRefDrawing);
                if (!string.IsNullOrEmpty(found) && File.Exists(found)) return found;
            }
            catch (Autodesk.AutoCAD.Runtime.Exception) { }
            catch (ArgumentException) { }
            return null;
        }

        private static bool IsClipped(BlockReference br, Transaction tr)
        {
            try
            {
                if (br.ExtensionDictionary.IsNull) return false;
                var dict = tr.GetObject(br.ExtensionDictionary, OpenMode.ForRead) as DBDictionary;
                return dict != null && dict.Contains("ACAD_FILTER");
            }
            catch (Autodesk.AutoCAD.Runtime.Exception) { return false; }
        }

        /// <summary>The insert's spatial filter as a polygon in the coordinates the content is
        /// read in, or null when it has none. The boundary is taken through the filter's own
        /// clip-to-world transform, the inverse block transform recorded when the clip was
        /// made, and the insert's current transform, so a clipped xref that was moved since
        /// still clips where it shows on screen.</summary>
        private static Clip ReadClip(BlockReference br, Transaction tr, ReadContext context)
        {
            try
            {
                if (br.ExtensionDictionary.IsNull) return null;
                var dict = tr.GetObject(br.ExtensionDictionary, OpenMode.ForRead) as DBDictionary;
                if (dict == null || !dict.Contains("ACAD_FILTER")) return null;
                var filters = tr.GetObject(dict.GetAt("ACAD_FILTER"), OpenMode.ForRead) as DBDictionary;
                if (filters == null || !filters.Contains("SPATIAL")) return null;
                var filter = tr.GetObject(filters.GetAt("SPATIAL"), OpenMode.ForRead) as SpatialFilter;
                if (filter == null) return null;
                var definition = filter.Definition;   // a struct in the 2024 API: never null
                if (!definition.Enabled) return null;

                var points = definition.GetPoints();
                var corners = new List<Point3d>();
                if (points.Count == 2)
                {
                    var minX = Math.Min(points[0].X, points[1].X); var maxX = Math.Max(points[0].X, points[1].X);
                    var minY = Math.Min(points[0].Y, points[1].Y); var maxY = Math.Max(points[0].Y, points[1].Y);
                    corners.Add(new Point3d(minX, minY, 0)); corners.Add(new Point3d(maxX, minY, 0));
                    corners.Add(new Point3d(maxX, maxY, 0)); corners.Add(new Point3d(minX, maxY, 0));
                }
                else
                {
                    foreach (Point2d p in points) corners.Add(new Point3d(p.X, p.Y, 0));
                }
                if (corners.Count < 3) return null;

                var toWorld = filter.ClipSpaceToWorldCoordinateSystemTransform;
                Matrix3d? originalInverse = null;
                try { originalInverse = filter.OriginalInverseBlockTransform; } catch (Autodesk.AutoCAD.Runtime.Exception) { }
                var current = br.BlockTransform;
                var polygon = new List<CPoint>();
                foreach (var corner in corners)
                {
                    var w = corner.TransformBy(toWorld);
                    if (originalInverse.HasValue) w = w.TransformBy(originalInverse.Value).TransformBy(current);
                    polygon.Add(context.Transform.Apply(new CPoint(w.X, w.Y)));
                }
                var inverted = false;
                var flag = ReadProperty(filter, "Inverted") ?? ReadProperty(definition, "Inverted");
                if (flag is bool) inverted = (bool)flag;
                return new Clip { Polygon = polygon, Inverted = inverted };
            }
            catch (Autodesk.AutoCAD.Runtime.Exception) { return null; }
            catch (TargetInvocationException) { return null; }
        }

        private static object ReadProperty(object target, string name)
        {
            if (target == null) return null;
            var property = target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            return property == null ? null : property.GetValue(target, null);
        }

        private static object Invoke(object target, string method, params object[] args)
        {
            if (target == null) return null;
            var m = target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.Public);
            return m == null ? null : m.Invoke(target, args);
        }

        /// <summary>Host-side state of every layer, including the xref-dependent ones
        /// ("BASE|E-CURB"), which is what the drawing actually shows and plots.</summary>
        private static Dictionary<string, LayerState> HostLayerStates(Database db, Transaction tr)
        {
            var states = new Dictionary<string, LayerState>(StringComparer.OrdinalIgnoreCase);
            var table = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            foreach (ObjectId id in table)
            {
                var record = tr.GetObject(id, OpenMode.ForRead) as LayerTableRecord;
                if (record == null) continue;
                states[record.Name] = new LayerState { Name = record.Name, Exists = true, Plottable = record.IsPlottable, Frozen = record.IsFrozen, Off = record.IsOff };
            }
            return states;
        }

        /// <summary>Model space of a database, through its own open/close transaction.</summary>
        private static void ReadDatabase(Database db, ReadContext context, Dictionary<string, Database> opened, int depth)
        {
            if (!context.Dataset.FilesRead.Contains(SafeFilename(db) ?? string.Empty, StringComparer.OrdinalIgnoreCase) && !string.IsNullOrEmpty(SafeFilename(db)))
                context.Dataset.FilesRead.Add(SafeFilename(db));
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                var n = 0;
                foreach (ObjectId id in ms)
                {
                    if (context.Progress != null && context.Progress.IsCancelled) throw new OperationCanceledException("Comparison cancelled.");
                    if (id.IsErased) continue;
                    var entity = tr.GetObject(id, OpenMode.ForRead, false, true) as AcEntity;
                    if (entity == null) continue;
                    var br = entity as BlockReference;
                    if (br != null)
                    {
                        var btr = tr.GetObject(br.BlockTableRecord, OpenMode.ForRead) as BlockTableRecord;
                        if (btr != null && btr.IsFromExternalReference)
                        {
                            ReadXrefInsert(br, btr, tr, context, opened, depth);
                            continue;
                        }
                    }
                    ReadEntity(entity, tr, context);
                    if (++n % 500 == 0 && context.Progress != null) context.Progress.Step(n, 0);
                }
                tr.Commit();
            }
        }

        // ================================================================ entities

        private sealed class Clip
        {
            public List<CPoint> Polygon;
            public bool Inverted;
        }

        private sealed class ReadContext
        {
            public CompareDataset Dataset;
            public CompareProfile Profile;
            public ICompareProgress Progress;
            public Affine2 Transform;
            public string OwnerPath;
            public string HostDirectory;
            public double ChordTolerance;
            public bool IsSurvey;
            public int BlockDepth;
            public readonly Dictionary<string, int> Unsupported = new Dictionary<string, int>(StringComparer.Ordinal);
            public int CivilLabels;
            public int OpaqueLabels;
            /// <summary>Every clip in force for this content: the insert's own and its ancestors'.</summary>
            public List<Clip> Clips = new List<Clip>();
            /// <summary>The host drawing's layer states, which override an xref's own.</summary>
            public Dictionary<string, LayerState> HostLayers;

            public ReadContext Child(Affine2 insert, string ownerName)
            {
                return new ReadContext
                {
                    Dataset = Dataset, Profile = Profile, Progress = Progress,
                    Transform = Transform.Then(insert),
                    OwnerPath = string.IsNullOrEmpty(OwnerPath) ? ownerName : OwnerPath + ">" + ownerName,
                    HostDirectory = HostDirectory, ChordTolerance = ChordTolerance, IsSurvey = IsSurvey, BlockDepth = BlockDepth,
                    Shared = Shared ?? this,
                    Clips = new List<Clip>(Clips),
                    HostLayers = HostLayers
                };
            }

            /// <summary>Whether a consultant entity's layer is shown and plotted: the host's
            /// xref-dependent record when there is one, else the layer in its own file.</summary>
            public bool LayerShown(AcEntity entity, Transaction tr, out string reason)
            {
                reason = null;
                var layer = entity.Layer ?? string.Empty;
                if (HostLayers != null && !string.IsNullOrEmpty(OwnerPath) && !OwnerPath.StartsWith("BLOCK:", StringComparison.Ordinal))
                {
                    var path = OwnerPath;
                    var blockAt = path.IndexOf(">BLOCK:", StringComparison.Ordinal);
                    if (blockAt >= 0) path = path.Substring(0, blockAt);
                    foreach (var candidate in new[] { path.Replace(">", "|") + "|" + layer, path.Split('>')[0] + "|" + layer })
                    {
                        LayerState state;
                        if (!HostLayers.TryGetValue(candidate, out state)) continue;
                        if (state.Frozen) { reason = "layer " + candidate + " frozen in the drawing"; return false; }
                        if (state.Off) { reason = "layer " + candidate + " off in the drawing"; return false; }
                        if (!state.Plottable) { reason = "layer " + candidate + " set to no-plot in the drawing"; return false; }
                        return true;
                    }
                }
                if (!PlotVisibility.LayerPlots(entity.Database, tr, layer))
                {
                    reason = "layer " + layer + " frozen, off or no-plot in " + (string.IsNullOrEmpty(OwnerPath) ? "the drawing" : OwnerPath);
                    return false;
                }
                return true;
            }

            /// <summary>The root context, which owns the tallies.</summary>
            public ReadContext Shared;

            public void Finish()
            {
                var root = Shared ?? this;
                foreach (var kv in root.Unsupported)
                    Dataset.AddIssue(Severity.Info, "UNSUPPORTED", kv.Value + " " + kv.Key + " object(s) are not compared (listed as not evaluated).", false);
                if (root.CivilLabels > 0)
                    Dataset.AddIssue(Severity.Info, "LABELS", root.CivilLabels + " Civil 3D label(s) read as annotation" +
                        (root.OpaqueLabels > 0 ? "; " + root.OpaqueLabels + " of them with text that could not be read (" + (IsSurvey ? "not evaluated, still plotting" : "they send our annotation beside them to review") + ")" : string.Empty) + ".", false);
            }

            public void CountUnsupported(string type)
            {
                var root = Shared ?? this;
                int n;
                root.Unsupported.TryGetValue(type, out n);
                root.Unsupported[type] = n + 1;
            }
        }

        private static void ReadEntity(AcEntity entity, Transaction tr, ReadContext context)
        {
            try
            {
                if (!context.IsSurvey)
                {
                    string reason;
                    if (!entity.Visible) { context.Dataset.CountHidden("invisible"); return; }
                    if (!context.LayerShown(entity, tr, out reason)) { context.Dataset.CountHidden(reason); return; }
                }
                var feature = ToFeature(entity, tr, context);
                if (feature == null) return;
                foreach (var piece in ApplyClips(feature, context))
                    context.Dataset.Add(piece);
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                context.Dataset.Add(Shell(entity, context, "could not be read: " + ex.Message));
            }
        }

        /// <summary>What remains of a feature inside every clip in force: points and text
        /// inside or nothing; linework cut to its inside pieces (each its own feature).</summary>
        private static IEnumerable<CompareFeature> ApplyClips(CompareFeature feature, ReadContext context)
        {
            if (context.Clips.Count == 0) { yield return feature; yield break; }
            if (feature.Kind != FeatureKind.Line)
            {
                foreach (var clip in context.Clips)
                {
                    var inside = Geometry2.PointInPolygon(feature.Position, clip.Polygon);
                    if (inside == clip.Inverted) { context.Dataset.CountHidden("outside xref clip"); yield break; }
                }
                yield return feature;
                yield break;
            }
            var pieces = new List<List<CPoint>> { feature.Vertices };
            foreach (var clip in context.Clips)
            {
                var next = new List<List<CPoint>>();
                foreach (var piece in pieces) next.AddRange(Geometry2.ClipPolylineToPolygon(piece, clip.Polygon, clip.Inverted));
                pieces = next;
            }
            if (pieces.Count == 0) { context.Dataset.CountHidden("outside xref clip"); yield break; }
            if (pieces.Count == 1 && pieces[0].Count == feature.Vertices.Count)
            {
                feature.Vertices = pieces[0];
                feature.InvalidateCache();
                yield return feature;
                yield break;
            }
            for (var i = 0; i < pieces.Count; i++)
            {
                yield return new CompareFeature
                {
                    Handle = feature.Handle + "#" + (i + 1), OwnerPath = feature.OwnerPath, EntityType = feature.EntityType, Layer = feature.Layer,
                    Kind = FeatureKind.Line, Description = feature.Description, Attributes = feature.Attributes, Vertices = pieces[i],
                    HasCurves = feature.HasCurves, Closed = false
                };
            }
        }

        private static CompareFeature Shell(AcEntity entity, ReadContext context, string problem)
        {
            var f = new CompareFeature
            {
                Handle = entity.Handle.ToString(),
                OwnerPath = context.OwnerPath,
                EntityType = entity.GetType().Name.ToUpperInvariant(),
                Layer = entity.Layer,
                Kind = FeatureKind.Point,
                ReadProblem = problem
            };
            try { f.Position = context.Transform.Apply(ToPoint(entity.GeometricExtents.MinPoint)); }
            catch (Autodesk.AutoCAD.Runtime.Exception) { f.Position = new CPoint(0, 0); }
            return f;
        }

        private static CompareFeature ToFeature(AcEntity entity, Transaction tr, ReadContext context)
        {
            var handle = entity.Handle.ToString();
            var layer = entity.Layer;
            var t = context.Transform;

            // ---- Civil 3D ---------------------------------------------------------
            var cogo = entity as CogoPoint;
            if (cogo != null)
            {
                var location = t.Apply(new CPoint(cogo.Location.X, cogo.Location.Y, cogo.Location.Z));
                var f = NewFeature(handle, context, "COGOPOINT", layer, FeatureKind.Point);
                f.Description = string.IsNullOrWhiteSpace(cogo.RawDescription) ? cogo.FullDescription : cogo.RawDescription;
                f.Attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    { "PointNumber", cogo.PointNumber.ToString(CultureInfo.InvariantCulture) }
                };
                f.Position = location;
                f.Elevation = location.HasZ ? location.Z : (double?)null;
                return f;
            }

            var structure = entity as CivilStructure;
            if (structure != null)
            {
                var f = NewFeature(handle, context, "STRUCTURE", layer, FeatureKind.Point);
                f.Position = t.Apply(ToPoint(structure.Position));
                f.Description = structure.Name;
                f.Attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                try { f.Attributes["PartSize"] = structure.PartSizeName; } catch (Autodesk.AutoCAD.Runtime.Exception) { }
                try { f.Attributes["RIM"] = structure.RimElevation.ToString("0.00", CultureInfo.InvariantCulture); f.Elevation = structure.RimElevation; }
                catch (Autodesk.AutoCAD.Runtime.Exception) { }
                return f;
            }

            var pipe = entity as CivilPipe;
            if (pipe != null)
            {
                var f = NewFeature(handle, context, "PIPE", layer, FeatureKind.Line);
                f.Description = pipe.Name;
                f.Attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                try { f.Attributes["PartSize"] = pipe.PartSizeName; } catch (Autodesk.AutoCAD.Runtime.Exception) { }
                f.Vertices = new List<CPoint> { t.Apply(ToPoint(pipe.StartPoint)), t.Apply(ToPoint(pipe.EndPoint)) };
                return f;
            }

            var featureLine = entity as FeatureLine;
            if (featureLine != null)
            {
                var f = NewFeature(handle, context, entity is SurveyFigure ? "SURVEYFIGURE" : "FEATURELINE", layer, FeatureKind.Line);
                f.Description = featureLine.Name;
                bool hasCurves;
                f.Vertices = SampleCivilCurve(featureLine, context, out hasCurves);
                f.HasCurves = hasCurves;
                return f.Vertices.Count >= 2 ? f : null;
            }

            // A survey figure that is not a feature line in this release: a Civil curve,
            // sampled along its real geometry, identified by its figure name.
            var figure = entity as SurveyFigure;
            if (figure != null)
            {
                var f = NewFeature(handle, context, "SURVEYFIGURE", layer, FeatureKind.Line);
                f.Description = figure.Name;
                f.Vertices = SampleGeneric(figure, context);
                f.HasCurves = true;
                return f.Vertices.Count >= 2 ? f : null;
            }

            var civilLabel = entity as Autodesk.Civil.DatabaseServices.Label;
            if (civilLabel != null)
            {
                var root = context.Shared ?? context;
                root.CivilLabels++;
                var f = NewFeature(handle, context, entity.GetType().Name.ToUpperInvariant(), layer, FeatureKind.Annotation);
                var labelText = ReadLabelText(civilLabel);
                CPoint position;
                var location = ReadProperty(civilLabel, "LabelLocation");
                if (location is Point3d) position = ToPoint((Point3d)location);
                else
                {
                    var extents = civilLabel.GeometricExtents;
                    position = new CPoint((extents.MinPoint.X + extents.MaxPoint.X) / 2, (extents.MinPoint.Y + extents.MaxPoint.Y) / 2);
                }
                f.Position = t.Apply(position);
                if (labelText != null)
                {
                    f.Description = labelText;
                    return f;
                }
                root.OpaqueLabels++;
                f.Description = string.Empty;
                if (context.IsSurvey) f.ReadProblem = "Civil 3D " + entity.GetType().Name + " text is style-driven and could not be read; it keeps plotting";
                else f.Opaque = true;
                return f;
            }

            // ---- AutoCAD linework -------------------------------------------------
            var line = entity as Line;
            if (line != null)
            {
                var f = NewFeature(handle, context, "LINE", layer, FeatureKind.Line);
                f.Vertices = new List<CPoint> { t.Apply(ToPoint(line.StartPoint)), t.Apply(ToPoint(line.EndPoint)) };
                return f;
            }

            var pl = entity as AcPolyline;
            if (pl != null)
            {
                var f = NewFeature(handle, context, "LWPOLYLINE", layer, FeatureKind.Line);
                var flip = pl.Normal.Z < 0;
                var count = pl.NumberOfVertices;
                var segments = pl.Closed ? count : count - 1;
                var points = new List<CPoint>();
                var hasCurves = false;
                for (var i = 0; i < segments; i++)
                {
                    var a = ToPoint(pl.GetPoint3dAt(i));
                    var b = ToPoint(pl.GetPoint3dAt((i + 1) % count));
                    var bulge = pl.GetBulgeAt(i);
                    if (flip) bulge = -bulge;
                    if (Math.Abs(bulge) > 1e-9) hasCurves = true;
                    var chords = Geometry2.ChordBulge(a, b, bulge, context.ChordTolerance);
                    for (var k = points.Count == 0 ? 0 : 1; k < chords.Count; k++) points.Add(t.Apply(chords[k]));
                }
                if (count == 1) points.Add(t.Apply(ToPoint(pl.GetPoint3dAt(0))));
                f.Vertices = points;
                f.Closed = pl.Closed;
                f.HasCurves = hasCurves;
                return f.Vertices.Count >= 2 ? f : null;
            }

            var p3 = entity as Polyline3d;
            if (p3 != null)
            {
                var f = NewFeature(handle, context, "POLYLINE3D", layer, FeatureKind.Line);
                if (p3.PolyType != Poly3dType.SimplePoly)
                {
                    f.Vertices = SampleGeneric(p3, context);
                    f.HasCurves = true;
                }
                else
                {
                    var points = new List<CPoint>();
                    foreach (ObjectId vid in p3)
                    {
                        var v = tr.GetObject(vid, OpenMode.ForRead) as PolylineVertex3d;
                        if (v != null && v.VertexType != Vertex3dType.ControlVertex) points.Add(t.Apply(ToPoint(v.Position)));
                    }
                    if (p3.Closed && points.Count > 0) points.Add(points[0]);
                    f.Vertices = points;
                    f.Closed = p3.Closed;
                }
                return f.Vertices.Count >= 2 ? f : null;
            }

            var p2 = entity as Polyline2d;
            if (p2 != null)
            {
                var f = NewFeature(handle, context, "POLYLINE2D", layer, FeatureKind.Line);
                if (p2.PolyType != Poly2dType.SimplePoly)
                {
                    f.Vertices = SampleGeneric(p2, context);
                    f.HasCurves = true;
                }
                else
                {
                    var raw = new List<KeyValuePair<CPoint, double>>();
                    foreach (ObjectId vid in p2)
                    {
                        var v = tr.GetObject(vid, OpenMode.ForRead) as Vertex2d;
                        if (v != null && v.VertexType == Vertex2dType.SimpleVertex) raw.Add(new KeyValuePair<CPoint, double>(ToPoint(p2.VertexPosition(v)), v.Bulge));
                    }
                    var points = new List<CPoint>();
                    var segments = p2.Closed ? raw.Count : raw.Count - 1;
                    for (var i = 0; i < segments; i++)
                    {
                        var a = raw[i].Key;
                        var b = raw[(i + 1) % raw.Count].Key;
                        if (Math.Abs(raw[i].Value) > 1e-9) f.HasCurves = true;
                        var chords = Geometry2.ChordBulge(a, b, raw[i].Value, context.ChordTolerance);
                        for (var k = points.Count == 0 ? 0 : 1; k < chords.Count; k++) points.Add(t.Apply(chords[k]));
                    }
                    f.Vertices = points;
                    f.Closed = p2.Closed;
                }
                return f.Vertices.Count >= 2 ? f : null;
            }

            var arc = entity as Arc;
            if (arc != null)
            {
                var f = NewFeature(handle, context, "ARC", layer, FeatureKind.Line);
                var center = ToPoint(arc.Center);
                var chords = Geometry2.ChordArc(center, arc.Radius, arc.StartAngle, arc.EndAngle, arc.Normal.Z >= 0, context.ChordTolerance);
                chords[0] = ToPoint(arc.StartPoint);
                chords[chords.Count - 1] = ToPoint(arc.EndPoint);
                f.Vertices = chords.Select(t.Apply).ToList();
                f.HasCurves = true;
                return f;
            }

            var circle = entity as Circle;
            if (circle != null)
            {
                // Small circles are symbols (a manhole, a pole); large ones are outlines.
                var radiusFeet = circle.Radius * t.Scale / context.Dataset.UnitsPerFoot;
                if (radiusFeet <= 10.0)
                {
                    var f = NewFeature(handle, context, "CIRCLE", layer, FeatureKind.Point);
                    f.Position = t.Apply(ToPoint(circle.Center));
                    f.Description = "CIRCLE R=" + circle.Radius.ToString("0.##", CultureInfo.InvariantCulture);
                    return f;
                }
                var outline = NewFeature(handle, context, "CIRCLE", layer, FeatureKind.Line);
                outline.Vertices = Geometry2.ChordArc(ToPoint(circle.Center), circle.Radius, 0, 2 * Math.PI, true, context.ChordTolerance).Select(t.Apply).ToList();
                outline.Closed = true;
                outline.HasCurves = true;
                return outline;
            }

            if (entity is Spline || entity is Ellipse)
            {
                var f = NewFeature(handle, context, entity is Spline ? "SPLINE" : "ELLIPSE", layer, FeatureKind.Line);
                f.Vertices = SampleGeneric((Curve)entity, context);
                f.HasCurves = true;
                return f.Vertices.Count >= 2 ? f : null;
            }

            // ---- annotation ---------------------------------------------------------
            var text = entity as DBText;
            if (text != null)
            {
                if (string.IsNullOrWhiteSpace(text.TextString)) return null;
                var f = NewFeature(handle, context, "TEXT", layer, FeatureKind.Annotation);
                f.Description = text.TextString;
                f.Position = t.Apply(ToPoint(text.Position));
                return f;
            }

            var mtext = entity as MText;
            if (mtext != null)
            {
                if (string.IsNullOrWhiteSpace(mtext.Text)) return null;
                var f = NewFeature(handle, context, "MTEXT", layer, FeatureKind.Annotation);
                f.Description = mtext.Contents;
                f.Position = t.Apply(ToPoint(mtext.Location));
                return f;
            }

            var mleader = entity as MLeader;
            if (mleader != null)
            {
                var f = NewFeature(handle, context, "MLEADER", layer, FeatureKind.Annotation);
                string content = null;
                try
                {
                    if (mleader.ContentType == ContentType.MTextContent && mleader.MText != null) content = mleader.MText.Contents;
                    else if (mleader.ContentType == ContentType.BlockContent && !mleader.BlockContentId.IsNull)
                    {
                        var btr = tr.GetObject(mleader.BlockContentId, OpenMode.ForRead) as BlockTableRecord;
                        if (btr != null) content = btr.Name;
                    }
                }
                catch (Autodesk.AutoCAD.Runtime.Exception) { }
                if (string.IsNullOrWhiteSpace(content)) return null;
                f.Description = content;
                f.Position = t.Apply(ToPoint(LeaderArrow(mleader)));
                return f;
            }

            // ---- blocks -------------------------------------------------------------
            var br = entity as BlockReference;
            if (br != null) return FromBlock(br, tr, context);

            // ---- the rest -----------------------------------------------------------
            if (entity is Viewport || entity is Wipeout || entity is Ray || entity is Xline || entity is Autodesk.AutoCAD.DatabaseServices.Leader || entity is Hatch || entity is Dimension)
            {
                if (entity is Hatch || entity is Dimension || entity is Autodesk.AutoCAD.DatabaseServices.Leader)
                    context.CountUnsupported(entity.GetType().Name.ToUpperInvariant());
                return entity is Hatch || entity is Dimension ? Shell(entity, context, entity.GetType().Name.ToUpperInvariant() + " objects are not compared") : null;
            }

            context.CountUnsupported(entity.GetType().Name.ToUpperInvariant());
            return Shell(entity, context, entity.GetType().Name.ToUpperInvariant() + " objects are not compared");
        }

        private static CompareFeature FromBlock(BlockReference br, Transaction tr, ReadContext context)
        {
            var t = context.Transform;
            var btrId = br.IsDynamicBlock ? br.DynamicBlockTableRecord : br.BlockTableRecord;
            var btr = tr.GetObject(btrId, OpenMode.ForRead) as BlockTableRecord;
            var name = btr != null ? btr.Name : "?";

            // Blocks the profile says are drawn linework (a curb as a block): read their
            // contents in place of the insert. Never nested more than a few levels.
            if (btr != null && context.BlockDepth < 3 && Wildcard.MatchesAny(context.Profile.ExplodeBlocks, name))
            {
                var child = context.Child(FromMatrix(br.BlockTransform), "BLOCK:" + name);
                child.BlockDepth = context.BlockDepth + 1;
                var defBtr = tr.GetObject(br.BlockTableRecord, OpenMode.ForRead) as BlockTableRecord;
                if (defBtr != null)
                    foreach (ObjectId id in defBtr)
                    {
                        if (id.IsErased) continue;
                        var inner = tr.GetObject(id, OpenMode.ForRead, false, true) as AcEntity;
                        if (inner == null || inner is AttributeDefinition) continue;
                        var feature = ToFeature(inner, tr, child);
                        if (feature == null) continue;
                        // Layer 0 inside a block takes the insert's layer.
                        if (string.Equals(feature.Layer, "0", StringComparison.Ordinal)) feature.Layer = br.Layer;
                        feature.Handle = br.Handle + ":" + inner.Handle;
                        context.Dataset.Add(feature);
                    }
                return null;
            }

            var f = NewFeature(br.Handle.ToString(), context, "INSERT", br.Layer, FeatureKind.Point);
            f.BlockName = name;
            f.Description = name;
            var position = t.Apply(ToPoint(br.Position));
            f.Position = position;
            if (position.HasZ && Math.Abs(position.Z) > 0.01) f.Elevation = position.Z;

            f.Attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (ObjectId attId in br.AttributeCollection)
                {
                    if (attId.IsErased) continue;
                    var att = tr.GetObject(attId, OpenMode.ForRead) as AttributeReference;
                    if (att == null || att.Invisible) continue;
                    var value = att.IsMTextAttribute ? att.MTextAttribute.Contents : att.TextString;
                    if (string.IsNullOrWhiteSpace(value)) continue;
                    var tag = string.IsNullOrEmpty(att.Tag) ? "ATTR" + f.Attributes.Count : att.Tag;
                    f.Attributes[tag] = value;
                }
            }
            catch (Autodesk.AutoCAD.Runtime.Exception) { }
            return f;
        }

        // ================================================================ helpers

        private static CompareFeature NewFeature(string handle, ReadContext context, string type, string layer, FeatureKind kind)
        {
            return new CompareFeature { Handle = handle, OwnerPath = context.OwnerPath, EntityType = type, Layer = layer, Kind = kind };
        }

        private static CPoint ToPoint(Point3d p) { return new CPoint(p.X, p.Y, p.Z); }

        /// <summary>The planar part of an insertion matrix, plus its Z scale and offset.</summary>
        internal static Affine2 FromMatrix(Matrix3d m)
        {
            var cs = m.CoordinateSystem3d;
            return new Affine2
            {
                M11 = cs.Xaxis.X, M21 = cs.Xaxis.Y,
                M12 = cs.Yaxis.X, M22 = cs.Yaxis.Y,
                Tx = cs.Origin.X, Ty = cs.Origin.Y,
                Sz = cs.Zaxis.Z, Tz = cs.Origin.Z
            };
        }

        private static Point3d LeaderArrow(MLeader mleader)
        {
            try
            {
                if (mleader.LeaderCount > 0)
                {
                    // The 2024 API hands the line indexes back as an untyped collection.
                    foreach (var line in mleader.GetLeaderLineIndexes(0))
                        return mleader.GetFirstVertex(Convert.ToInt32(line, CultureInfo.InvariantCulture));
                }
            }
            catch (Autodesk.AutoCAD.Runtime.Exception) { }
            try { return mleader.TextLocation; } catch (Autodesk.AutoCAD.Runtime.Exception) { }
            return mleader.GeometricExtents.MinPoint;
        }

        /// <summary>The visible text of a Civil 3D label through its text components, read
        /// by reflection so a release without these members degrades to "could not read"
        /// instead of a plugin that fails to load. Null when the text is not available or
        /// still contains unresolved property fields.</summary>
        private static string ReadLabelText(Autodesk.Civil.DatabaseServices.Label label)
        {
            try
            {
                var ids = Invoke(label, "GetTextComponentIds") as ObjectIdCollection;
                if (ids == null || ids.Count == 0) return null;
                var parts = new List<string>();
                foreach (ObjectId id in ids)
                {
                    var text = Invoke(label, "GetTextComponentText", id) as string;
                    if (string.IsNullOrWhiteSpace(text)) continue;
                    if (text.IndexOf("<[", StringComparison.Ordinal) >= 0) return null;
                    parts.Add(text.Trim());
                }
                return parts.Count == 0 ? null : string.Join(" ", parts.ToArray());
            }
            catch (Autodesk.AutoCAD.Runtime.Exception) { return null; }
            catch (TargetInvocationException) { return null; }
            catch (ArgumentException) { return null; }
        }

        /// <summary>Points along any curve: a step small enough that chords of a tight
        /// arc stay within tolerance, never more than 4000 samples, ends exact.</summary>
        private static List<CPoint> SampleGeneric(Curve curve, ReadContext context)
        {
            var points = new List<CPoint>();
            double length;
            try { length = curve.GetDistanceAtParameter(curve.EndParam) - curve.GetDistanceAtParameter(curve.StartParam); }
            catch (Autodesk.AutoCAD.Runtime.Exception) { return points; }
            if (length <= 1e-9) return points;
            var step = Math.Max(0.5 * context.Dataset.UnitsPerFoot, length / 4000.0);
            var start = curve.GetDistanceAtParameter(curve.StartParam);
            for (var d = 0.0; d < length; d += step)
                points.Add(context.Transform.Apply(ToPoint(curve.GetPointAtDist(start + d))));
            points.Add(context.Transform.Apply(ToPoint(curve.EndPoint)));
            return points;
        }

        /// <summary>A Civil 3D feature line or survey figure: its PI and elevation points
        /// exactly, with arcs filled in by distance sampling between them.</summary>
        private static List<CPoint> SampleCivilCurve(FeatureLine fl, ReadContext context, out bool hasCurves)
        {
            hasCurves = false;
            var stations = new List<double>();
            double length;
            try { length = fl.GetDistanceAtParameter(fl.EndParam) - fl.GetDistanceAtParameter(fl.StartParam); }
            catch (Autodesk.AutoCAD.Runtime.Exception) { return new List<CPoint>(); }
            if (length <= 1e-9) return new List<CPoint>();
            var start = fl.GetDistanceAtParameter(fl.StartParam);

            var pis = new List<double>();
            try
            {
                var points = fl.GetPoints(Autodesk.Civil.FeatureLinePointType.AllPoints);
                foreach (Point3d p in points)
                {
                    try { pis.Add(fl.GetDistAtPoint(fl.GetClosestPointTo(p, false)) - start); }
                    catch (Autodesk.AutoCAD.Runtime.Exception) { }
                }
            }
            catch (Autodesk.AutoCAD.Runtime.Exception) { }
            catch (NotImplementedException) { }

            var chordStep = Math.Max(0.5 * context.Dataset.UnitsPerFoot, length / 4000.0);
            stations.Add(0);
            for (var d = chordStep; d < length; d += chordStep) stations.Add(d);
            stations.AddRange(pis.Where(d => d > 0 && d < length));
            stations.Add(length);
            stations.Sort();

            var result = new List<CPoint>();
            double? previousDirection = null;
            CPoint? previous = null;
            var lastStation = double.NegativeInfinity;
            foreach (var d in stations)
            {
                if (d - lastStation < 1e-6) continue;
                lastStation = d;
                CPoint p;
                try { p = context.Transform.Apply(ToPoint(fl.GetPointAtDist(start + d))); }
                catch (Autodesk.AutoCAD.Runtime.Exception) { continue; }
                if (previous.HasValue)
                {
                    var dir = Math.Atan2(p.Y - previous.Value.Y, p.X - previous.Value.X);
                    var turn = Math.Abs(dir - (previousDirection ?? dir));
                    if (previousDirection.HasValue && turn > 0.02 && turn < 0.5) hasCurves = true;
                    previousDirection = dir;
                }
                previous = p;
                result.Add(p);
            }
            return result;
        }

        private static bool IsXref(Transaction tr, BlockReference br)
        {
            var btr = tr.GetObject(br.BlockTableRecord, OpenMode.ForRead) as BlockTableRecord;
            return btr != null && btr.IsFromExternalReference;
        }

        // ================================================================== xrefs

        public static IList<XrefState> XrefStates(Database db, Transaction tr)
        {
            var states = new List<XrefState>();
            var table = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            foreach (ObjectId id in table)
            {
                var btr = tr.GetObject(id, OpenMode.ForRead) as BlockTableRecord;
                if (btr == null || !btr.IsFromExternalReference) continue;
                var status = btr.XrefStatus;
                states.Add(new XrefState
                {
                    Name = btr.Name, Path = btr.PathName,
                    Resolved = status == XrefStatus.Resolved || status == XrefStatus.Unloaded,
                    Loaded = status == XrefStatus.Resolved
                });
            }
            return states;
        }

        public static IList<string> XrefNames(Database db, Transaction tr)
        {
            return XrefStates(db, tr).Select(x => x.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }
}
