using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using FieldCodes.Linework;
using FieldCodes.Review;
using FieldCodes.Settings;

using AcEntity = Autodesk.AutoCAD.DatabaseServices.Entity;

namespace FieldCodes.Cad
{
    /// <summary>What one accounting pass found, for the command line and the window.</summary>
    internal sealed class AccountabilitySummary
    {
        public LedgerRun Run;
        public int CodeItems;
        public int ModifierItems;
        public int PointItems;
        public int LineItems;
        public int Intentional;
        public int Proposals;
        public int CorrectionsErased;
        public string PointsCsv;
        public string ContinuityCsv;
        public readonly List<string> Notes = new List<string>();

        /// <summary>"Accounted for 1,080 / 1,115; 15 for review; 2 linework continuity items (FTFREVIEW)".</summary>
        public string OneLine()
        {
            if (Run == null) return "Point accountability did not run.";
            return "Accounted for " + Run.Accounted.ToString("N0", CultureInfo.InvariantCulture) + " / " +
                   Run.Points.ToString("N0", CultureInfo.InvariantCulture) + " point(s); " +
                   Run.Review.ToString("N0", CultureInfo.InvariantCulture) + " for review" +
                   (Run.Deleted > 0 ? ", " + Run.Deleted.ToString("N0", CultureInfo.InvariantCulture) + " deleted" : string.Empty) + "; " +
                   Run.OpenLineItems.ToString("N0", CultureInfo.InvariantCulture) + " linework continuity item(s) open" +
                   (Intentional > 0 ? ", " + Intentional.ToString("N0", CultureInfo.InvariantCulture) + " intentional break(s)" : string.Empty) +
                   " (FTFREVIEW).";
        }
    }

    /// <summary>Everything one read of the drawing gives the accounting and continuity checks.
    /// Read-only: nothing here writes to the drawing.</summary>
    internal sealed class AccountabilityFacts
    {
        public readonly List<PointFacts> Points = new List<PointFacts>();
        public readonly ContinuityInput Continuity = new ContinuityInput();
        public bool ControlTableExists;
        public readonly HashSet<string> ControlTablePoints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<string> GatePostsLabelled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Handles of the approved corrections (kind LineCorrection) in model space.</summary>
        public readonly Dictionary<string, FtfStamp> Corrections = new Dictionary<string, FtfStamp>(StringComparer.OrdinalIgnoreCase);
        public Func<string, string> FamilyOf;
        public readonly List<string> Problems = new List<string>();
    }

    /// <summary>Reads the drawing for point accountability and linework continuity.</summary>
    internal static class AccountabilityCollector
    {
        public static AccountabilityFacts Collect(Database db, Transaction tr, RulesConfig cfg, FtfSettings settings)
        {
            var facts = new AccountabilityFacts();
            var acc = settings.Accountability ?? new AccountabilitySettings();
            var upf = cfg.UnitsPerFoot > 0 ? cfg.UnitsPerFoot : 1.0;
            facts.FamilyOf = FamilyFunction(cfg);

            var parser = new FieldCodeParser(cfg);
            var notes = CogoNotes.For(db, tr, settings);
            var scan = AnnotationScan.Read(db, tr, cfg, settings, parser, notes);
            var sources = SourcePointReader.Read(db, tr);

            // ---- FTF's own objects, by the point they were drawn for
            var outputs = new Dictionary<string, List<OutputFact>>(StringComparer.OrdinalIgnoreCase);
            Action<string, string, string> add = (number, kind, handle) =>
            {
                if (string.IsNullOrWhiteSpace(number)) return;
                List<OutputFact> list;
                if (!outputs.TryGetValue(number.Trim(), out list)) { list = new List<OutputFact>(); outputs[number.Trim()] = list; }
                list.Add(new OutputFact { Kind = kind, Handle = handle });
            };
            foreach (var pair in Ownership.FindOwned(db, tr, null))
            {
                var s = pair.Value;
                var handle = pair.Key.Handle.ToString();
                switch (s.Kind)
                {
                    case FtfEntityKind.Block: add(s.PointNumber, "Block", handle); break;
                    case FtfEntityKind.DripArc: add(s.PointNumber, "DripArc", handle); break;
                    case FtfEntityKind.Label: add(s.PointNumber, "Label", handle); break;
                    case FtfEntityKind.Tag: add(s.PointNumber, "Tag", handle); break;
                    case FtfEntityKind.LineLabel:
                        if (!string.IsNullOrEmpty(s.TagText) && s.TagText.StartsWith(LineworkCommands.GateTag, StringComparison.OrdinalIgnoreCase))
                            foreach (var post in s.TagText.Substring(LineworkCommands.GateTag.Length).Split('-'))
                                if (!string.IsNullOrWhiteSpace(post)) { facts.GatePostsLabelled.Add(post.Trim()); add(post, "GateLabel", handle); }
                        break;
                    case FtfEntityKind.DraftLine:
                        if (string.Equals(s.TagText, BenchCommands.BenchTag, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(s.PointNumber))
                            foreach (var end in s.PointNumber.Split('-')) add(end, "Bench", handle);
                        break;
                    case FtfEntityKind.ControlTable:
                        facts.ControlTableExists = true;
                        ReadControlTable(tr, pair.Key, facts.ControlTablePoints);
                        break;
                    case FtfEntityKind.LineCorrection:
                        facts.Corrections[handle] = s;
                        break;
                }
            }

            // ---- surfaces: definitions and triangulation vertices
            var surfaces = ReadSurfaces(db, tr, Math.Max(1e-6, acc.SurfaceToleranceFeet * upf), facts.Problems);

            // ---- the points
            var styles = new Dictionary<ObjectId, MarkerFact>();
            var gateCodes = new HashSet<string>((settings.Gates != null && settings.Gates.PostCodes != null ? settings.Gates.PostCodes : new List<string>())
                                                .Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim().ToUpperInvariant()), StringComparer.OrdinalIgnoreCase);
            foreach (var cogo in CadUtil.CogoPointsInOrder(db, tr))
            {
                var number = cogo.PointNumber.ToString(CultureInfo.InvariantCulture);
                string raw;
                try { raw = cogo.RawDescription ?? string.Empty; }
                catch (Autodesk.AutoCAD.Runtime.Exception) { raw = string.Empty; }
                ParsedPoint parsed;
                try { parsed = notes.Parse(parser, cogo); }
                catch (Autodesk.AutoCAD.Runtime.Exception) { parsed = parser.Parse(number, raw); }
                TreeCommands.MissingBlock(db, tr, parsed);
                ParsedPoint forLabel;
                scan.Points.TryGetValue(number, out forLabel);
                var at = cogo.Location;
                var coding = LineworkCoding.Parse(raw, cfg);
                var p = new PointFacts
                {
                    Number = number,
                    Handle = cogo.Handle.ToString(),
                    RawDescription = raw,
                    X = at.X, Y = at.Y, Z = at.Z,
                    Parsed = parsed,
                    ForLabel = forLabel,
                    Coding = coding,
                    HandLabelled = forLabel != null && !string.IsNullOrEmpty(forLabel.LabelText) && scan.HasHandLabel(number, forLabel.LabelText),
                    Marker = Marker(cogo, db, tr, styles)
                };
                List<OutputFact> made;
                if (outputs.TryGetValue(number, out made)) p.Outputs.AddRange(made);
                foreach (var s in surfaces) p.Surfaces.AddRange(s.Facts(cogo.PointNumber, at));
                facts.Points.Add(p);

                facts.Continuity.Shots.Add(new ContinuityShot { Number = number, Handle = p.Handle, RawDescription = raw, X = at.X, Y = at.Y, Z = at.Z, Coding = coding });
                if (raw.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).Any(w => gateCodes.Contains(ContinuitySettings.BaseCode(w))))
                    facts.Continuity.GatePosts.Add(number);
            }

            // ---- the lines
            var catalog = new LineworkCatalog(cfg.LineFeatures, cfg.IgnoreLineNames);
            var ms = CadUtil.ModelSpace(db, tr, OpenMode.ForRead);
            foreach (ObjectId id in ms)
            {
                if (id.IsErased) continue;
                var entity = tr.GetObject(id, OpenMode.ForRead, false, true) as AcEntity;
                var curve = entity as Curve;
                if (curve == null || entity is CogoPoint) continue;
                var stamp = Ownership.Read(entity);
                var isCorrection = stamp != null && stamp.Kind == FtfEntityKind.LineCorrection;
                if (stamp != null && stamp.Kind != FtfEntityKind.DraftLine && !isCorrection) continue;   // FTF's annotation is not survey linework

                string figureName = null;
                string type;
                var figure = entity as FeatureLine;
                if (figure != null) { figureName = figure.Name; type = entity is SurveyFigure ? "Survey Figure" : "Feature Line"; }
                else if (entity is Polyline || entity is Polyline2d || entity is Polyline3d) type = "Polyline";
                else if (entity is Line) type = "Line";
                else if (entity is Arc) type = "Arc";
                else continue;

                var line = new ContinuityLine
                {
                    Handle = entity.Handle.ToString(),
                    Layer = entity.Layer,
                    EntityType = type,
                    FigureName = figureName,
                    IsCorrection = isCorrection
                };
                try { line.Closed = curve.Closed; } catch (System.Exception) { }
                var points = Densify(curve, Math.Max(1e-6, (acc.Continuity != null ? acc.Continuity.OnLineToleranceFeet : 0.05) * upf / 4));
                // A closed curve read by parameter comes back to its first vertex: drop the repeat,
                // the line is closed anyway.
                if (line.Closed && points.Count > 2 && points[0].Key.DistanceTo(points[points.Count - 1].Key) < 1e-9) points.RemoveAt(points.Count - 1);
                foreach (var v in points)
                {
                    line.Vertices.Add(new[] { v.Key.X, v.Key.Y, v.Key.Z });
                    line.ShotsAtVertex.Add(v.Value ? sources.At(v.Key.X, v.Key.Y).Select(s => s.Number).ToList() : new List<string>());
                }
                if (line.Vertices.Count < 2) continue;
                if (line.ShotsAtVertex.All(s => s.Count == 0))
                {
                    double? length = null;
                    try { length = curve.GetDistanceAtParameter(curve.EndParam) - curve.GetDistanceAtParameter(curve.StartParam); } catch (System.Exception) { }
                    var row = catalog.Identify(type, figureName, entity.Layer, length);
                    if (row.Source != LineIdentitySource.None && row.Codes != null && row.Codes.IndexOf('/') < 0) line.FeatureCode = row.Codes;
                }
                facts.Continuity.Lines.Add(line);
            }

            facts.Continuity.Settings = acc.Continuity ?? new ContinuitySettings();
            facts.Continuity.UnitsPerFoot = upf;
            facts.Continuity.FamilyOf = facts.FamilyOf;
            facts.Continuity.LayerOf = family =>
            {
                var f = (cfg.LineFeatures ?? new List<LineFeatureRule>()).FirstOrDefault(r => string.Equals(r.Code, family, StringComparison.OrdinalIgnoreCase));
                return f != null ? f.Layer : null;
            };
            return facts;
        }

        /// <summary>A figure code's feature: the line feature whose code or alias it is, else itself.</summary>
        internal static Func<string, string> FamilyFunction(RulesConfig cfg)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in cfg.LineFeatures ?? new List<LineFeatureRule>())
            {
                if (f == null || string.IsNullOrWhiteSpace(f.Code)) continue;
                if (!map.ContainsKey(f.Code.Trim())) map[f.Code.Trim()] = f.Code.Trim().ToUpperInvariant();
                foreach (var a in f.Aliases ?? new List<string>())
                    if (!string.IsNullOrWhiteSpace(a) && !map.ContainsKey(a.Trim())) map[a.Trim()] = f.Code.Trim().ToUpperInvariant();
            }
            return code =>
            {
                string family;
                var c = ContinuitySettings.BaseCode(code);
                return map.TryGetValue(c, out family) ? family : c;
            };
        }

        /// <summary>The curve's vertices (true) and, along its arcs, points between them (false),
        /// close enough that a shot on the arc is within <paramref name="chordError"/> of a chord.</summary>
        private static List<KeyValuePair<Point3d, bool>> Densify(Curve curve, double chordError)
        {
            var list = new List<KeyValuePair<Point3d, bool>>();
            var vertices = LineworkCommands.VerticesOf(curve).ToList();
            if (curve is Arc)
            {
                try
                {
                    var n = Steps(curve, curve.StartParam, curve.EndParam, chordError);
                    for (var i = 0; i <= n; i++)
                    {
                        var t = curve.StartParam + (curve.EndParam - curve.StartParam) * i / n;
                        list.Add(new KeyValuePair<Point3d, bool>(curve.GetPointAtParameter(t), i == 0 || i == n));
                    }
                    return list;
                }
                catch (System.Exception) { list.Clear(); }
            }
            for (var i = 0; i < vertices.Count; i++)
            {
                list.Add(new KeyValuePair<Point3d, bool>(vertices[i], true));
                if (i + 1 >= vertices.Count) break;
                try
                {
                    var a = curve.GetParameterAtPoint(curve.GetClosestPointTo(vertices[i], false));
                    var b = curve.GetParameterAtPoint(curve.GetClosestPointTo(vertices[i + 1], false));
                    if (b <= a) continue;
                    var mid = curve.GetPointAtParameter((a + b) / 2);
                    var chord = new Point3d((vertices[i].X + vertices[i + 1].X) / 2, (vertices[i].Y + vertices[i + 1].Y) / 2, mid.Z);
                    if (mid.DistanceTo(chord) < chordError) continue;      // straight enough
                    var k = Steps(curve, a, b, chordError);
                    for (var j = 1; j < k; j++) list.Add(new KeyValuePair<Point3d, bool>(curve.GetPointAtParameter(a + (b - a) * j / k), false));
                }
                catch (System.Exception) { }
            }
            return list;
        }

        /// <summary>How many chords between two parameters keep every chord within the error of the
        /// curve (doubling from 8, at most 1024).</summary>
        private static int Steps(Curve curve, double a, double b, double chordError)
        {
            var n = 8;
            while (n < 1024)
            {
                var p = curve.GetPointAtParameter(a);
                var q = curve.GetPointAtParameter(a + (b - a) / n);
                var m = curve.GetPointAtParameter(a + (b - a) / (2.0 * n));
                var c = new Point3d((p.X + q.X) / 2, (p.Y + q.Y) / 2, m.Z);
                if (m.DistanceTo(c) <= chordError) break;
                n *= 2;
            }
            return n;
        }

        private static void ReadControlTable(Transaction tr, ObjectId id, ICollection<string> points)
        {
            try
            {
                var table = tr.GetObject(id, OpenMode.ForRead) as Table;
                if (table == null) return;
                for (var r = 2; r < table.Rows.Count; r++)
                {
                    var text = (table.Cells[r, 0].TextString ?? string.Empty).Trim();
                    if (text.Length > 0) points.Add(text);
                }
            }
            catch (System.Exception) { }
        }

        /// <summary>What a point's marker is: the style (or its point group's override), its type and block.</summary>
        private static MarkerFact Marker(CogoPoint cogo, Database db, Transaction tr, IDictionary<ObjectId, MarkerFact> cache)
        {
            var e = Compare.CogoPointPlotting.Resolve(cogo, db, tr);
            var fact = new MarkerFact { Known = e.MarkerKnown, Visible = e.MarkerVisible, Problem = e.Problem };
            bool visible;
            try { visible = cogo.Visible; } catch (System.Exception) { visible = true; }
            if (!visible) fact.Visible = false;
            if (e.StyleId.IsNull) return fact;
            MarkerFact style;
            if (!cache.TryGetValue(e.StyleId, out style))
            {
                style = new MarkerFact();
                try
                {
                    var obj = tr.GetObject(e.StyleId, OpenMode.ForRead);
                    style.StyleName = Convert.ToString(Read(obj, "Name"));
                    style.MarkerType = Convert.ToString(Read(obj, "MarkerType"));
                    style.SymbolName = Convert.ToString(Read(obj, "MarkerSymbolName"));
                }
                catch (System.Exception ex) { style.Problem = ex.Message; }
                cache[e.StyleId] = style;
            }
            fact.StyleName = style.StyleName;
            fact.MarkerType = style.MarkerType;
            fact.SymbolName = style.SymbolName;
            if (string.IsNullOrEmpty(fact.MarkerType) && fact.Problem == null) fact.Problem = style.Problem ?? "the marker type could not be read";
            return fact;
        }

        private static object Read(object target, string property)
        {
            if (target == null) return null;
            var p = target.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.Instance);
            if (p == null) return null;
            try { return p.GetValue(target, null); }
            catch (TargetInvocationException) { return null; }
        }

        // ---------------------------------------------------------------- surfaces

        private sealed class SurfaceRead
        {
            public string Name;
            public double Tolerance;
            public bool DefinitionKnown;
            /// <summary>Point group name -> the group (to ask ContainsPoint).</summary>
            public readonly List<KeyValuePair<string, DBObject>> Groups = new List<KeyValuePair<string, DBObject>>();
            public readonly Dictionary<long, List<Point3d>> Vertices = new Dictionary<long, List<Point3d>>();

            private long Key(double x, double y) { return (long)Math.Floor(x / Tolerance) * 73856093L ^ (long)Math.Floor(y / Tolerance) * 19349663L; }

            public void AddVertex(Point3d p)
            {
                var k = Key(p.X, p.Y);
                List<Point3d> list;
                if (!Vertices.TryGetValue(k, out list)) { list = new List<Point3d>(); Vertices[k] = list; }
                list.Add(p);
            }

            public bool IsVertex(Point3d at)
            {
                var kx = (long)Math.Floor(at.X / Tolerance);
                var ky = (long)Math.Floor(at.Y / Tolerance);
                for (var dx = -1; dx <= 1; dx++)
                    for (var dy = -1; dy <= 1; dy++)
                    {
                        List<Point3d> list;
                        if (!Vertices.TryGetValue((kx + dx) * 73856093L ^ (ky + dy) * 19349663L, out list)) continue;
                        foreach (var v in list)
                            if (Math.Abs(v.X - at.X) <= Tolerance && Math.Abs(v.Y - at.Y) <= Tolerance && Math.Abs(v.Z - at.Z) <= Tolerance) return true;
                    }
                return false;
            }

            public IEnumerable<SurfaceFact> Facts(uint number, Point3d at)
            {
                string group = null;
                bool? inDefinition = DefinitionKnown ? (bool?)false : null;
                foreach (var g in Groups)
                {
                    var contains = ContainsPoint(g.Value, number);
                    if (contains == true) { inDefinition = true; group = g.Key; break; }
                    if (contains == null) inDefinition = inDefinition == true ? true : (bool?)null;
                }
                var vertex = IsVertex(at);
                if (vertex || inDefinition == true) yield return new SurfaceFact { Surface = Name, InDefinition = inDefinition, PointGroup = group, IsVertex = vertex };
            }
        }

        /// <summary>
        /// Every TIN surface: its triangulation vertices (as FTFCONTOUR reads them) and the point
        /// groups its definition is built from. The definition is read by reflection
        /// (TinSurface.PointGroupsDefinition, PointGroup.ContainsPoint) so a member that is not
        /// there means "definition not readable" -- coincidence only -- never a plugin that fails
        /// to load. UNTESTED in Civil 3D.
        /// </summary>
        private static List<SurfaceRead> ReadSurfaces(Database db, Transaction tr, double tolerance, ICollection<string> problems)
        {
            var found = new List<SurfaceRead>();
            ObjectIdCollection ids;
            try { ids = CivilDocument.GetCivilDocument(db).GetSurfaceIds(); }
            catch (System.Exception) { return found; }
            foreach (ObjectId id in ids)
            {
                if (id.IsNull || id.IsErased) continue;
                TinSurface surface;
                try { surface = tr.GetObject(id, OpenMode.ForRead, false, true) as TinSurface; }
                catch (System.Exception) { continue; }
                if (surface == null) continue;
                var read = new SurfaceRead { Name = surface.Name, Tolerance = tolerance };
                try
                {
                    foreach (TinSurfaceTriangle t in surface.GetTriangles(false))
                    {
                        read.AddVertex(t.Vertex1.Location);
                        read.AddVertex(t.Vertex2.Location);
                        read.AddVertex(t.Vertex3.Location);
                    }
                }
                catch (System.Exception ex) { problems.Add("Surface " + surface.Name + ": its triangles could not be read (" + ex.Message + ")."); }
                try
                {
                    var definition = Read(surface, "PointGroupsDefinition");
                    if (definition != null)
                    {
                        read.DefinitionKnown = true;
                        foreach (var groupId in GroupIds(definition))
                        {
                            DBObject group;
                            try { group = tr.GetObject(groupId, OpenMode.ForRead, false, true); }
                            catch (System.Exception) { continue; }
                            if (group == null) continue;
                            read.Groups.Add(new KeyValuePair<string, DBObject>(Convert.ToString(Read(group, "Name")), group));
                        }
                    }
                }
                catch (System.Exception ex) { read.DefinitionKnown = false; problems.Add("Surface " + surface.Name + ": its definition could not be read (" + ex.Message + "); coordinates only."); }
                found.Add(read);
            }
            return found;
        }

        /// <summary>The point group ids a surface's point-group definition names, whatever shape the
        /// collection has in this release (ObjectIds, or operations carrying a group id).</summary>
        private static IEnumerable<ObjectId> GroupIds(object definition)
        {
            var ids = new List<ObjectId>();
            var enumerable = definition as IEnumerable;
            if (enumerable == null) return ids;
            foreach (var item in enumerable)
            {
                if (item is ObjectId) { ids.Add((ObjectId)item); continue; }
                foreach (var name in new[] { "PointGroupId", "ObjectId", "Id" })
                {
                    var v = Read(item, name);
                    if (v is ObjectId && !((ObjectId)v).IsNull) { ids.Add((ObjectId)v); break; }
                }
                var many = Read(item, "PointGroupIds") as ObjectIdCollection;
                if (many != null) foreach (ObjectId x in many) ids.Add(x);
            }
            return ids.Where(x => !x.IsNull).Distinct().ToList();
        }

        private static bool? ContainsPoint(DBObject group, uint number)
        {
            var m = group.GetType().GetMethod("ContainsPoint", new[] { typeof(uint) });
            if (m == null) return null;
            try { return (bool)m.Invoke(group, new object[] { number }); }
            catch (TargetInvocationException) { return null; }
            catch (InvalidCastException) { return null; }
        }
    }

    /// <summary>
    /// The checks that run after processing (FTFRUN, the window, FTFLABELALL), on FTFREVIEWCHECK
    /// and before FTFQC: the code check, linework continuity and point accountability. Writes only
    /// the review list, the point ledger, the non-plotted proposal previews and the reports beside
    /// the drawing. Never writes a point or a line of the survey.
    /// </summary>
    internal static class PostRunChecks
    {
        public static AccountabilitySummary Run(Database db, Transaction tr, RulesConfig cfg, FtfSettings settings, string source)
        {
            var summary = new AccountabilitySummary();
            var now = DateTime.Now;
            int codes, modifiers;
            ReviewStore.CheckCodes(db, tr, cfg, settings, out codes, out modifiers);
            summary.CodeItems = codes;
            summary.ModifierItems = modifiers;

            var facts = AccountabilityCollector.Collect(db, tr, cfg, settings);
            summary.Notes.AddRange(facts.Problems);
            var acc = settings.Accountability ?? new AccountabilitySettings();

            // Linework continuity: the survey linework only (an approved correction never hides a finding).
            var continuityResult = acc.Continuity != null && acc.Continuity.Enabled
                ? LineContinuity.Analyze(facts.Continuity)
                : CoverageOnly(facts.Continuity);
            var shots = facts.Continuity.Shots.GroupBy(s => s.Number).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            var lineItems = continuityResult.ReviewItems(shots);
            summary.Intentional = continuityResult.Findings.Count(f => f.Confidence == GapConfidence.Intentional);
            summary.LineItems = lineItems.Count;

            var register = DrawingStore.ReadReview(db, tr);
            if (acc.Continuity != null && acc.Continuity.Enabled)
            {
                // A feature switched off is not checked this run: its earlier items stay as they
                // are (neither resolved nor reopened) until it is switched on again.
                var reported = new List<ReviewItem>(lineItems);
                foreach (var kept in register.Items.Where(i => string.Equals(i.Source, ReviewKinds.ContinuitySource, StringComparison.OrdinalIgnoreCase) &&
                                                               !string.IsNullOrEmpty(i.Code) && !acc.Continuity.For(i.Code).Enabled &&
                                                               !reported.Any(r => string.Equals(r.Id, i.Id, StringComparison.OrdinalIgnoreCase))).ToList())
                    reported.Add(kept.Clone());
                register.Report(ReviewKinds.ContinuitySource, reported, now, true, settings.Review);
            }
            else summary.Notes.Add("Linework continuity is switched off (Settings > Point Accountability): its earlier items are left as they are.");

            // A correction that was erased puts its item back on the list.
            foreach (var item in register.Items.Where(i => i.Status == ReviewItemStatus.Corrected).ToList())
                if (string.IsNullOrEmpty(item.CorrectionHandle) || !facts.Corrections.ContainsKey(item.CorrectionHandle))
                    if (register.CorrectionErased(item.Id, now)) summary.CorrectionsErased++;

            // Point accountability.
            var previous = DrawingStore.ReadLedger(db, tr);
            var context = new AccountingContext
            {
                Rules = cfg,
                Settings = settings,
                Diagnostics = new CodeDiagnostics(cfg, settings.Diagnostics != null ? settings.Diagnostics.Codes() : null),
                Continuity = continuityResult,
                FamilyOf = facts.FamilyOf,
                ControlTableExists = facts.ControlTableExists,
                ControlTablePoints = facts.ControlTablePoints,
                GatePostsLabelled = facts.GatePostsLabelled,
                UnitsPerFoot = cfg.UnitsPerFoot > 0 ? cfg.UnitsPerFoot : 1.0,
                Source = source
            };
            var accounting = PointAccounting.Build(previous, facts.Points, context, now);
            summary.PointItems = accounting.Items.Count(i => i.Kind != ReviewKinds.PointSourceChanged);
            register.Report(ReviewKinds.PointsSource, accounting.Items, now, true, settings.Review);
            summary.Run = PointAccounting.Finish(accounting.Ledger, register, now, source);

            DrawingStore.WriteReview(db, tr, register);
            DrawingStore.WriteLedger(db, tr, accounting.Ledger);

            // Proposals: redrawn every run, for what is still open.
            summary.Proposals = LineCorrections.DrawProposals(db, tr, cfg, acc, register);

            if (acc.WriteReport) WriteReports(db, accounting.Ledger, continuityResult, register, summary);
            return summary;
        }

        /// <summary>Continuity switched off: the ledger still needs to know which shots are drawn.</summary>
        private static ContinuityResult CoverageOnly(ContinuityInput input)
        {
            var quiet = new ContinuitySettings();
            foreach (var f in new[] { quiet.Defaults }.Concat(quiet.Features)) f.Enabled = false;
            var copy = new ContinuityInput { Shots = input.Shots, Lines = input.Lines, GatePosts = input.GatePosts, Settings = quiet, UnitsPerFoot = input.UnitsPerFoot, FamilyOf = input.FamilyOf, LayerOf = input.LayerOf };
            return LineContinuity.Analyze(copy);
        }

        private static void WriteReports(Database db, PointLedger ledger, ContinuityResult continuity, ReviewRegister register, AccountabilitySummary summary)
        {
            var saved = UtilityCommands.SavedDrawingPath(db);                 // an unsaved drawing's Filename is its template
            var points = FieldCodes.Reporting.AccountabilityReport.PointsPathFor(saved);
            var lines = FieldCodes.Reporting.AccountabilityReport.ContinuityPathFor(saved);
            if (points == null) { summary.Notes.Add("The drawing has never been saved: no report was written beside it."); return; }
            try
            {
                File.WriteAllText(points, FieldCodes.Reporting.AccountabilityReport.RenderPoints(ledger), new UTF8Encoding(true));
                summary.PointsCsv = points;
                File.WriteAllText(lines, FieldCodes.Reporting.AccountabilityReport.RenderContinuity(continuity, register), new UTF8Encoding(true));
                summary.ContinuityCsv = lines;
            }
            catch (IOException ex) { summary.Notes.Add("Could not write the reports: " + ex.Message); }
            catch (UnauthorizedAccessException ex) { summary.Notes.Add("Could not write the reports: " + ex.Message); }
        }

        /// <summary>Runs the checks in their own FTF session (one transaction, rolled back on
        /// error, traced as FTFREVIEWCHECK) and prints the summary. Used after FTFRUN, the
        /// window's processing and FTFLABELALL; does nothing when Settings > Point Accountability
        /// says not to run after processing.</summary>
        public static AccountabilitySummary AfterProcessing(string source)
        {
            AccountabilitySummary summary = null;
            FtfSession.Run("FTFREVIEWCHECK", (db, tr, ed) =>
            {
                var cfg = FtfSession.Rules(db);
                var settings = FtfSession.SettingsFor(db, cfg);
                if (settings.Accountability != null && !settings.Accountability.RunAfterProcessing)
                {
                    ed.WriteMessage("\nPoint accountability: not run after processing (Settings > Point Accountability). FTFREVIEWCHECK runs it.\n");
                    return;
                }
                summary = Run(db, tr, cfg, settings, source);
                Print(ed, summary);
            });
            ReviewCommands.ReloadWindow();
            return summary;
        }

        public static void Print(Autodesk.AutoCAD.EditorInput.Editor ed, AccountabilitySummary s)
        {
            if (ed == null || s == null) return;
            ed.WriteMessage("\nChecks: {0} code item(s), {1} linework word item(s).", s.CodeItems, s.ModifierItems);
            if (s.Run != null) ed.WriteMessage("\nPoints: {0}", s.Run.Describe());
            ed.WriteMessage("\n{0}", s.OneLine());
            if (s.Proposals > 0) ed.WriteMessage("\n{0} proposed correction(s) previewed on the non-plotted QC layer; approve them in FTFREVIEW.", s.Proposals);
            if (s.CorrectionsErased > 0) ed.WriteMessage("\n{0} approved correction(s) were erased: their items are open again.", s.CorrectionsErased);
            foreach (var n in s.Notes) ed.WriteMessage("\n  note: {0}", n);
            if (s.PointsCsv != null) ed.WriteMessage("\nPoint ledger: {0}", s.PointsCsv);
            if (s.ContinuityCsv != null) ed.WriteMessage("\nLinework continuity: {0}", s.ContinuityCsv);
            ed.WriteMessage("\n");
        }
    }

    /// <summary>
    /// Proposed linework corrections (previews on a non-plotted layer, kind LineGapProposal) and
    /// approved ones (kind LineCorrection): a separate line on the feature's layer, traceable to
    /// its review item. The original figures and polylines are never edited, exploded or joined.
    /// </summary>
    internal static class LineCorrections
    {
        public const string FixTag = "linefix:";

        /// <summary>The tag naming a review item on FTF's proposal and correction objects. XData
        /// strings stop at 255 characters, so a long id (a line skipping many shots) is named by
        /// its hash instead, which still matches exactly one item.</summary>
        internal static string TagFor(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length <= 200) return id ?? string.Empty;
            return "#" + Fingerprint.Of(id);
        }

        public static int DrawProposals(Database db, Transaction tr, RulesConfig cfg, AccountabilitySettings acc, ReviewRegister register)
        {
            Ownership.DeleteOwnedKinds(db, tr, FtfEntityKind.LineGapProposal);
            var open = register.Items.Where(i => i.IsOpen && i.HasProposal &&
                                                 string.Equals(i.Source, ReviewKinds.ContinuitySource, StringComparison.OrdinalIgnoreCase)).ToList();
            if (open.Count == 0) return 0;
            var layerName = acc.Continuity != null && !string.IsNullOrWhiteSpace(acc.Continuity.ProposalLayer) ? acc.Continuity.ProposalLayer : "FTF-QC-CONTINUITY";
            var layer = CadUtil.EnsureLayer(db, tr, layerName);
            var record = (LayerTableRecord)tr.GetObject(layer, OpenMode.ForRead);
            if (record.IsPlottable) { record.UpgradeOpen(); record.IsPlottable = false; }
            var textHeight = Math.Max(0.08 * CadUtil.DrawingUnitsPerPlottedUnit(db), 1e-3);
            var count = 0;
            foreach (var item in open)
            {
                var color = (short)(item.Confidence == "possible" ? 6 : item.Kind == ReviewKinds.LineGap ? 2 : 30);
                using (var pl = new Polyline())
                {
                    for (var i = 0; i < item.Geometry.Count; i++) pl.AddVertexAt(i, new Point2d(item.Geometry[i][0], item.Geometry[i][1]), 0, 0, 0);
                    pl.LayerId = layer;
                    pl.ColorIndex = color;
                    pl.ConstantWidth = textHeight * 0.25;
                    CadUtil.AddToModelSpace(db, tr, pl);
                    Ownership.Stamp(pl, item.Code ?? string.Empty, cfg.Version, FtfEntityKind.LineGapProposal, null, TagFor(item.Id));
                    count++;
                }
                var first = item.Geometry[0];
                using (var text = new MText())
                {
                    text.Contents = (item.Confidence ?? "proposal").ToUpperInvariant() + ": " + (item.Issue ?? item.Kind);
                    text.TextHeight = textHeight;
                    text.Location = new Point3d(first[0], first[1], 0);
                    text.LayerId = layer;
                    text.ColorIndex = color;
                    CadUtil.AddToModelSpace(db, tr, text);
                    Ownership.Stamp(text, item.Code ?? string.Empty, cfg.Version, FtfEntityKind.LineGapProposal, null, TagFor(item.Id));
                }
            }
            return count;
        }

        /// <summary>
        /// Draws the approved correction for an item and records it. One correction per item: an
        /// earlier one FTF drew is replaced only if nobody edited it; one edited by hand is kept
        /// and nothing is drawn over it.
        /// </summary>
        public static bool Approve(Database db, Transaction tr, RulesConfig cfg, ReviewRegister register, string id, string note, string by,
                                   out string handle, out string problem)
        {
            handle = null;
            problem = null;
            var item = register.Find(id);
            if (item == null) { problem = "No review item " + id + "."; return false; }
            if (!item.HasProposal) { problem = item.Id + " has no proposed correction."; return false; }
            if (item.Status != ReviewItemStatus.Open) { problem = item.Id + " is " + item.Status.ToString().ToLowerInvariant() + ", not open."; return false; }

            foreach (var pair in Ownership.FindOwned(db, tr, s => s.Kind == FtfEntityKind.LineCorrection &&
                                                                  string.Equals(s.TagText, FixTag + TagFor(item.Id), StringComparison.OrdinalIgnoreCase)))
            {
                var existing = (AcEntity)tr.GetObject(pair.Key, OpenMode.ForRead);
                if (EditedByHand(existing, pair.Value))
                {
                    problem = "an earlier correction for this item (" + existing.Handle + ") was edited by hand; it is kept and nothing is drawn over it. Erase it first to draw FTF's.";
                    return false;
                }
                existing.UpgradeOpen();
                existing.Erase();
            }

            var layer = !string.IsNullOrWhiteSpace(item.TargetLayer) ? CadUtil.EnsureLayer(db, tr, item.TargetLayer) : db.Clayer;
            AcEntity line;
            var z = item.Geometry[0].Length > 2 ? item.Geometry[0][2] : 0.0;
            var flat = item.Geometry.All(p => (p.Length > 2 ? p[2] : 0.0) == z);
            if (flat)
            {
                var pl = new Polyline();
                for (var i = 0; i < item.Geometry.Count; i++) pl.AddVertexAt(i, new Point2d(item.Geometry[i][0], item.Geometry[i][1]), 0, 0, 0);
                pl.Elevation = z;
                line = pl;
            }
            else
            {
                var points = new Point3dCollection();
                foreach (var p in item.Geometry) points.Add(new Point3d(p[0], p[1], p.Length > 2 ? p[2] : 0.0));
                line = new Polyline3d(Poly3dType.SimplePoly, points, false);
            }
            line.LayerId = layer;
            CadUtil.AddToModelSpace(db, tr, line);
            Ownership.Stamp(line, item.Code ?? string.Empty, cfg.Version, FtfEntityKind.LineCorrection, null,
                            FixTag + TagFor(item.Id), null, null, GeometryText(line));
            handle = line.Handle.ToString();
            line.Dispose();

            if (!register.MarkCorrected(item.Id, handle, note, by, DateTime.Now, out problem)) return false;
            Ownership.DeleteOwned(db, tr, s => s.Kind == FtfEntityKind.LineGapProposal && string.Equals(s.TagText, TagFor(item.Id), StringComparison.OrdinalIgnoreCase));
            return true;
        }

        /// <summary>The geometry FTF drew, as text: a correction whose geometry no longer hashes to
        /// its stamp was edited by hand.</summary>
        internal static string GeometryText(AcEntity e)
        {
            var curve = e as Curve;
            if (curve == null) return string.Empty;
            var sb = new StringBuilder();
            foreach (var v in LineworkCommands.VerticesOf(curve))
                sb.Append(v.X.ToString("0.###", CultureInfo.InvariantCulture)).Append(',')
                  .Append(v.Y.ToString("0.###", CultureInfo.InvariantCulture)).Append(',')
                  .Append(v.Z.ToString("0.###", CultureInfo.InvariantCulture)).Append(';');
            return sb.ToString();
        }

        internal static bool EditedByHand(AcEntity e, FtfStamp stamp)
        {
            if (stamp == null || string.IsNullOrEmpty(stamp.ContentsHash)) return false;
            return !string.Equals(Ownership.ContentsHashOf(GeometryText(e)), stamp.ContentsHash, StringComparison.OrdinalIgnoreCase);
        }
    }
}
