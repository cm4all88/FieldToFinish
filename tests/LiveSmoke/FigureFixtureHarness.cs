// Headless Civil 3D checks of FTF against REAL survey figures, driven by tests\LiveSmoke\run-figures.ps1
// on a copy of tests\LiveSmoke\fixtures\figures-synthetic.dwg (built once in Civil 3D from the
// synthetic points in fixtures\figures-synthetic.csv; see fixtures\README.md). Survey figures can
// only be made by a survey database import, so this cannot seed its own drawing as the other
// harnesses do.
//
// FIGSNAP     requires survey figures in the drawing (FATAL otherwise), snapshots every figure and
//             point, writes the figure manifest (what Civil 3D made of the coding: names, layers,
//             closure, vertices and the shots at them) and checks, against the SurveyFigure API:
//               API        every figure has a name and at least two points from GetPoints(AllPoints)
//               VERTICES   the plugin's own vertex reader returns exactly those points, in order
//               IDENTITY   the plugin's source-point reader finds a shot at every vertex of a
//                          figure without curves (independently of the plugin's search)
//               CODING     FTF's linework parser agrees with Civil 3D: every shot coded onto a
//                          figure lies on a figure of that name, and every figure's name is a figure
//                          its own shots are coded onto; a closed figure is closed
// FIGVERIFY   first requires that FTFLINES ran exactly once since the previous stage, then checks:
//               LINES      each figure's FTFLINES row (by handle): Survey Figure, its full name (the
//                          figure number kept), "figure name" for a configured feature, its vertex
//                          count and the shots at its vertices
//               AGAIN      a second FTFLINES writes the same inventory, byte for byte
//             and at every stage that no figure and no point changed and nothing FTF-owned appeared.
// FIGEND      writes the verdict and COMPLETE to the result file.
//
// Result protocol as the other harnesses: lines appended to the file named by FTF_FIGURE_RESULT;
// the manifest goes to FTF_FIGURE_MANIFEST. C# 5 only (the .NET Framework csc).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AcDb = Autodesk.AutoCAD.DatabaseServices;
using Civil = Autodesk.Civil;
using CivDb = Autodesk.Civil.DatabaseServices;

[assembly: CommandClass(typeof(FtfFigureTest.FigureTestCommands))]

namespace FtfFigureTest
{
    public class FigureTestCommands
    {
        private static readonly CultureInfo C = CultureInfo.InvariantCulture;
        private const string AppName = "FTF_FIELDTOFINISH";
        private const double Tolerance = 0.01;

        private static int _failures;
        private static int _passes;
        private static readonly List<string> Stages = new List<string>();
        private static string _figureSnapshot;
        private static string _pointSnapshot;
        private static string _firstCsv;
        private static Assembly _plugin;
        private static int _seenStarted;
        private static int _seenFinished;

        private static Editor Ed
        {
            get { return Application.DocumentManager.MdiActiveDocument.Editor; }
        }

        private static void Check(Editor ed, bool ok, string what)
        {
            if (ok) _passes++; else _failures++;
            ed.WriteMessage("\n  [{0}] {1}", ok ? "PASS" : "FAIL", what);
            Record((ok ? "PASS " : "FAIL ") + what);
        }

        private static void Record(string line)
        {
            var file = Environment.GetEnvironmentVariable("FTF_FIGURE_RESULT");
            if (string.IsNullOrEmpty(file)) return;
            try { File.AppendAllText(file, line + Environment.NewLine); }
            catch (System.Exception) { }
        }

        private static void Fatal(string why)
        {
            _failures++;
            try { Ed.WriteMessage("\n  [FATAL] {0}\n", why); } catch (System.Exception) { }
            Record("FATAL " + why);
            Environment.Exit(3);
        }

        // ================================================================ what is in the drawing

        private sealed class Shot
        {
            public string Number;
            public string Description;
            public Point3d At;
        }

        private sealed class Figure
        {
            public string Handle;
            public string Name;
            public string Layer;
            public bool Closed;
            public List<Point3d> Points = new List<Point3d>();
            public AcDb.ObjectId Id;
        }

        private static List<Shot> Shots(AcDb.Database db, AcDb.Transaction tr)
        {
            var shots = new List<Shot>();
            var ms = (AcDb.BlockTableRecord)tr.GetObject(AcDb.SymbolUtilityServices.GetBlockModelSpaceId(db), AcDb.OpenMode.ForRead);
            foreach (AcDb.ObjectId id in ms)
            {
                var p = tr.GetObject(id, AcDb.OpenMode.ForRead) as CivDb.CogoPoint;
                if (p == null) continue;
                shots.Add(new Shot { Number = p.PointNumber.ToString(C), Description = p.RawDescription ?? string.Empty, At = p.Location });
            }
            return shots;
        }

        private static List<Figure> Figures(AcDb.Database db, AcDb.Transaction tr)
        {
            var figures = new List<Figure>();
            var ms = (AcDb.BlockTableRecord)tr.GetObject(AcDb.SymbolUtilityServices.GetBlockModelSpaceId(db), AcDb.OpenMode.ForRead);
            foreach (AcDb.ObjectId id in ms)
            {
                var f = tr.GetObject(id, AcDb.OpenMode.ForRead) as CivDb.SurveyFigure;
                if (f == null) continue;
                var figure = new Figure { Handle = f.Handle.ToString(), Name = f.Name ?? string.Empty, Layer = f.Layer, Closed = f.Closed, Id = id };
                var points = f.GetPoints(Civil.FeatureLinePointType.AllPoints);
                foreach (Point3d p in points) figure.Points.Add(p);
                figures.Add(figure);
            }
            figures.Sort(delegate (Figure a, Figure b) { return string.CompareOrdinal(a.Handle, b.Handle); });
            return figures;
        }

        /// <summary>The shots within the tolerance of a plan position, nearest first then by number.
        /// Written here, not borrowed from the plugin, so it checks the plugin's own search.</summary>
        private static List<string> ShotsAt(List<Shot> shots, double x, double y)
        {
            var near = new List<KeyValuePair<double, Shot>>();
            foreach (var s in shots)
            {
                var d = Math.Sqrt((s.At.X - x) * (s.At.X - x) + (s.At.Y - y) * (s.At.Y - y));
                if (d <= Tolerance) near.Add(new KeyValuePair<double, Shot>(d, s));
            }
            near.Sort(delegate (KeyValuePair<double, Shot> a, KeyValuePair<double, Shot> b)
            {
                var byDistance = a.Key.CompareTo(b.Key);
                return byDistance != 0 ? byDistance : NumberKey(a.Value.Number).CompareTo(NumberKey(b.Value.Number));
            });
            return near.Select(n => n.Value.Number).ToList();
        }

        private static double NumberKey(string number)
        {
            double n;
            return double.TryParse(number, NumberStyles.Float, C, out n) ? n : double.MaxValue;
        }

        /// <summary>The shot numbers along a figure, as FTFLINES lists them (a repeat in a row once).</summary>
        private static List<string> ShotsAlong(List<Shot> shots, Figure figure)
        {
            var list = new List<string>();
            foreach (var v in figure.Points)
                foreach (var n in ShotsAt(shots, v.X, v.Y))
                    if (list.Count == 0 || list[list.Count - 1] != n) list.Add(n);
            return list;
        }

        /// <summary>The figure IDs FTF's parser reads from a description ("FNC1", "EP", ...).</summary>
        private static List<string> FigureIds(object rules, string description)
        {
            var coding = rules.GetType().Assembly.GetType("FieldCodes.Linework.LineworkCoding");
            if (coding == null) Fatal("FieldCodes.Linework.LineworkCoding not found: the loaded build predates Step 1.");
            var parse = coding.GetMethod("Parse", new[] { typeof(string), rules.GetType() });
            var shot = parse.Invoke(null, new object[] { description, rules });
            var ids = new List<string>();
            foreach (var f in (IEnumerable)shot.GetType().GetProperty("Figures").GetValue(shot, null))
                ids.Add((string)f.GetType().GetProperty("FigureId").GetValue(f, null));
            return ids;
        }

        private static bool LiesOn(AcDb.Transaction tr, Figure figure, Point3d at)
        {
            var curve = (AcDb.Curve)tr.GetObject(figure.Id, AcDb.OpenMode.ForRead);
            Point3d closest;
            try { closest = curve.GetClosestPointTo(at, false); }
            catch (System.Exception) { return false; }
            var dx = closest.X - at.X;
            var dy = closest.Y - at.Y;
            return Math.Sqrt(dx * dx + dy * dy) <= Tolerance;
        }

        /// <summary>A figure's name read as one of the figure IDs its shots are coded onto: the same
        /// name, or the same name with a suffix Civil 3D added (recorded in the manifest as such).</summary>
        private static bool NameMatches(string figureName, string id, out bool renamed)
        {
            renamed = false;
            if (string.Equals(figureName, id, StringComparison.OrdinalIgnoreCase)) return true;
            if (!figureName.StartsWith(id, StringComparison.OrdinalIgnoreCase) || figureName.Length == id.Length) return false;
            var rest = figureName.Substring(id.Length);
            if (char.IsLetterOrDigit(rest[0])) return false;      // FNC10 is not a renamed FNC1
            renamed = rest.Skip(1).Any() && rest.Skip(1).All(ch => char.IsLetterOrDigit(ch) || ch == ')' || ch == ']');
            return renamed;
        }

        private static bool HasCurve(AcDb.Transaction tr, Figure figure)
        {
            // A figure whose plan length is longer than its chords has an arc in it.
            var curve = (AcDb.Curve)tr.GetObject(figure.Id, AcDb.OpenMode.ForRead);
            double chords = 0;
            for (var i = 1; i < figure.Points.Count; i++)
            {
                var a = figure.Points[i - 1];
                var b = figure.Points[i];
                chords += Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
            }
            double length;
            try { length = curve.GetDistanceAtParameter(curve.EndParam); }
            catch (System.Exception) { return false; }      // unknown: keep the vertex check strict
            return length - chords > 0.001;
        }

        // ================================================================ FIGSNAP

        [CommandMethod("FIGSNAP")]
        public void Snap()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;
            Record("BEGIN " + DateTime.UtcNow.ToString("o", C));

            var loaded = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => string.Equals(a.GetName().Name, "FieldCodes.Cad", StringComparison.OrdinalIgnoreCase)).ToList();
            if (loaded.Count != 1) Fatal("FieldCodes.Cad is loaded " + loaded.Count + " times; expected once.");
            _plugin = loaded[0];
            _seenStarted = Trace("StartedCount");
            _seenFinished = Trace("FinishedCount");

            var manifest = new StringBuilder();
            manifest.AppendLine("FTF survey figure fixture manifest (what Civil 3D made of the synthetic coding)");
            manifest.AppendLine("drawing: " + Path.GetFileName(db.Filename));

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var figures = Figures(db, tr);
                var shots = Shots(db, tr);
                if (figures.Count == 0) Fatal("the drawing has no survey figures: build the fixture as tests\\LiveSmoke\\fixtures\\README.md says.");
                Check(ed, true, "the fixture holds " + figures.Count + " survey figure(s) and " + shots.Count + " point(s)");

                // API: what the SurveyFigure API itself reports.
                var unnamed = figures.Where(f => string.IsNullOrWhiteSpace(f.Name)).Select(f => f.Handle).ToList();
                Check(ed, unnamed.Count == 0, "API: every SurveyFigure has a Name" + (unnamed.Count == 0 ? string.Empty : " (not: " + string.Join(", ", unnamed.ToArray()) + ")"));
                var thin = figures.Where(f => f.Points.Count < 2).Select(f => f.Name).ToList();
                Check(ed, thin.Count == 0, "API: GetPoints(AllPoints) returns at least two points for every figure" + (thin.Count == 0 ? string.Empty : " (not: " + string.Join(", ", thin.ToArray()) + ")"));

                // VERTICES: the plugin reads every point of a figure, not only its ends.
                var lineworkCommands = _plugin.GetType("FieldCodes.Cad.LineworkCommands");
                var verticesOf = lineworkCommands == null ? null : lineworkCommands.GetMethod("VerticesOf", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (verticesOf == null) Fatal("FieldCodes.Cad.LineworkCommands.VerticesOf not found.");
                var differ = new List<string>();
                foreach (var f in figures)
                {
                    var curve = (AcDb.Curve)tr.GetObject(f.Id, AcDb.OpenMode.ForRead);
                    var read = ((IEnumerable)verticesOf.Invoke(null, new object[] { curve })).Cast<Point3d>().ToList();
                    var same = read.Count == f.Points.Count;
                    for (var i = 0; same && i < read.Count; i++) same = read[i].DistanceTo(f.Points[i]) < 1e-9;
                    if (!same) differ.Add(f.Name + " (" + read.Count + " of " + f.Points.Count + ")");
                }
                Check(ed, differ.Count == 0, "VERTICES: the plugin reads every figure point, in order" + (differ.Count == 0 ? string.Empty : ": " + string.Join(", ", differ.ToArray())));

                // IDENTITY: the plugin's reader finds a shot at every vertex (figures without arcs).
                var reader = _plugin.GetType("FieldCodes.Cad.SourcePointReader");
                if (reader == null) Fatal("FieldCodes.Cad.SourcePointReader not found: the loaded build predates Step 1.");
                var index = reader.GetMethod("Read", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { db, tr });
                var at = index.GetType().GetMethod("At");
                var bare = new List<string>();
                var disagree = new List<string>();
                foreach (var f in figures)
                {
                    var curved = HasCurve(tr, f);
                    for (var i = 0; i < f.Points.Count; i++)
                    {
                        var v = f.Points[i];
                        var plugin = ((IEnumerable)at.Invoke(index, new object[] { v.X, v.Y })).Cast<object>()
                            .Select(p => (string)p.GetType().GetProperty("Number").GetValue(p, null)).ToList();
                        var mine = ShotsAt(shots, v.X, v.Y);
                        if (string.Join(" ", plugin.ToArray()) != string.Join(" ", mine.ToArray()))
                            disagree.Add(f.Name + " vertex " + i + ": plugin [" + string.Join(" ", plugin.ToArray()) + "] here [" + string.Join(" ", mine.ToArray()) + "]");
                        if (mine.Count == 0 && !curved) bare.Add(f.Name + " vertex " + i);
                    }
                }
                Check(ed, disagree.Count == 0, "IDENTITY: the plugin finds the same shots at every figure vertex as an independent search" + (disagree.Count == 0 ? string.Empty : ": " + string.Join("; ", disagree.Take(8).ToArray())));
                Check(ed, bare.Count == 0, "IDENTITY: every vertex of a figure without arcs is at a shot" + (bare.Count == 0 ? string.Empty : ": " + string.Join(", ", bare.Take(12).ToArray())));

                // CODING: FTF's parser and Civil 3D's linework processing agree.
                var session = _plugin.GetType("FieldCodes.Cad.FtfSession");
                var rules = session.GetMethod("Rules", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { db });
                if (rules == null) Fatal("FtfSession.Rules returned nothing: the rules file did not load.");
                var offFigure = new List<string>();
                var coded = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);   // figure id -> shots coded onto it
                manifest.AppendLine();
                manifest.AppendLine("POINTS (number | description | figure IDs FTF reads | figures the point lies on)");
                foreach (var s in shots.OrderBy(x => NumberKey(x.Number)))
                {
                    var ids = FigureIds(rules, s.Description);
                    var on = figures.Where(f => LiesOn(tr, f, s.At)).Select(f => f.Name).ToList();
                    manifest.AppendLine(string.Format(C, "{0} | {1} | {2} | {3}", s.Number, s.Description,
                        string.Join(" ", ids.ToArray()), string.Join(" ", on.ToArray())));
                    foreach (var id in ids)
                    {
                        List<string> list;
                        if (!coded.TryGetValue(id, out list)) { list = new List<string>(); coded[id] = list; }
                        list.Add(s.Number);
                        bool renamed;
                        if (!on.Any(n => NameMatches(n, id, out renamed)))
                            offFigure.Add(s.Number + " \"" + s.Description + "\" is coded onto " + id + " but lies on " + (on.Count == 0 ? "no figure" : string.Join("/", on.ToArray())));
                    }
                }
                Check(ed, offFigure.Count == 0, "CODING: every shot coded onto a figure lies on a figure of that name" + (offFigure.Count == 0 ? string.Empty : ": " + string.Join("; ", offFigure.Take(10).ToArray())));

                var strayNames = new List<string>();
                var renames = new List<string>();
                manifest.AppendLine();
                manifest.AppendLine("FIGURES (handle | name | layer | closed | arcs | points | shots along it)");
                foreach (var f in figures)
                {
                    var along = ShotsAlong(shots, f);
                    var ids = along.SelectMany(n => FigureIds(rules, shots.First(s => s.Number == n).Description)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                    string matched = null;
                    var wasRenamed = false;
                    foreach (var id in ids)
                    {
                        bool renamed;
                        if (NameMatches(f.Name, id, out renamed)) { matched = id; wasRenamed = renamed; break; }
                    }
                    if (matched == null) strayNames.Add(f.Name + " (its shots are coded " + string.Join("/", ids.ToArray()) + ")");
                    else if (wasRenamed) renames.Add(matched + " -> " + f.Name);
                    manifest.AppendLine(string.Format(C, "{0} | {1} | {2} | {3} | {4} | {5} | {6}", f.Handle, f.Name, f.Layer,
                        f.Closed ? "closed" : "open", HasCurve(tr, f) ? "arcs" : "-", f.Points.Count, string.Join(" ", along.ToArray())));
                    foreach (var v in f.Points)
                        manifest.AppendLine(string.Format(C, "    ({0:0.000}, {1:0.000}, {2:0.000}) {3}", v.X, v.Y, v.Z, string.Join(" ", ShotsAt(shots, v.X, v.Y).ToArray())));
                }
                Check(ed, strayNames.Count == 0, "CODING: every figure is named for a figure its own shots are coded onto" + (strayNames.Count == 0 ? string.Empty : ": " + string.Join("; ", strayNames.ToArray())));
                manifest.AppendLine();
                manifest.AppendLine("RENAMED BY CIVIL 3D: " + (renames.Count == 0 ? "none" : string.Join(", ", renames.ToArray())));
                Record("RENAMED " + (renames.Count == 0 ? "none" : string.Join(", ", renames.ToArray())));

                // A shot coded with a close command ends on a closed figure.
                var closeCodes = new List<string>();
                var openClosed = new List<string>();
                foreach (var s in shots)
                    foreach (var id in CloseIds(rules, s.Description))
                    {
                        closeCodes.Add(s.Number);
                        bool renamed;
                        var figure = figures.FirstOrDefault(f => NameMatches(f.Name, id, out renamed) && LiesOn(tr, f, s.At));
                        if (figure == null) continue;      // reported by CODING above
                        var looped = figure.Points.Count > 2 && figure.Points[0].DistanceTo(figure.Points[figure.Points.Count - 1]) < Tolerance;
                        if (!figure.Closed && !looped) openClosed.Add(figure.Name + " (closed at " + s.Number + ")");
                    }
                Check(ed, closeCodes.Count > 0 && openClosed.Count == 0, "CODING: a figure coded closed (" + string.Join(" ", closeCodes.ToArray()) + ") is closed" +
                      (openClosed.Count == 0 ? string.Empty : ": not " + string.Join(", ", openClosed.ToArray())));

                _figureSnapshot = FigureSnapshot(figures);
                _pointSnapshot = PointSnapshot(shots);
                tr.Commit();
            }

            var manifestPath = Environment.GetEnvironmentVariable("FTF_FIGURE_MANIFEST");
            if (!string.IsNullOrEmpty(manifestPath))
            {
                try { File.WriteAllText(manifestPath, manifest.ToString(), new UTF8Encoding(false)); }
                catch (System.Exception ex) { Check(ed, false, "the manifest could not be written: " + ex.Message); }
            }
            Check(ed, CountOwned(db) == 0, "SNAP: nothing FTF-owned in the fixture before FTF runs (" + CountOwned(db) + ")");
            Record("STAGE SNAP " + (_failures == 0 ? "PASS" : "FAIL"));
        }

        /// <summary>The figure IDs a description closes (its figure has a close command).</summary>
        private static List<string> CloseIds(object rules, string description)
        {
            var coding = rules.GetType().Assembly.GetType("FieldCodes.Linework.LineworkCoding");
            var shot = coding.GetMethod("Parse", new[] { typeof(string), rules.GetType() }).Invoke(null, new object[] { description, rules });
            var ids = new List<string>();
            foreach (var f in (IEnumerable)shot.GetType().GetProperty("Figures").GetValue(shot, null))
                if ((bool)f.GetType().GetProperty("Closes").GetValue(f, null))
                    ids.Add((string)f.GetType().GetProperty("FigureId").GetValue(f, null));
            return ids;
        }

        // ================================================================ FIGVERIFY

        [CommandMethod("FIGVERIFY")]
        public void Verify()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;
            var stage = ed.GetString("\nStage: ").StringResult.Trim().ToUpperInvariant();
            Stages.Add(stage);
            var before = _failures;
            if (_figureSnapshot == null) Fatal("FIGVERIFY before FIGSNAP.");

            RequireRanOnce();
            if (stage == "LINES") VerifyLines(db, ed, false);
            else if (stage == "AGAIN") VerifyLines(db, ed, true);
            else Fatal("unknown stage " + stage);

            using (var tr = db.TransactionManager.StartTransaction())
            {
                Check(ed, FigureSnapshot(Figures(db, tr)) == _figureSnapshot, stage + ": every survey figure unchanged (name, layer, closure, points)");
                Check(ed, PointSnapshot(Shots(db, tr)) == _pointSnapshot, stage + ": every COGO point unchanged (number, raw description, XYZ)");
                tr.Commit();
            }
            Check(ed, CountOwned(db) == 0, stage + ": no FTF-owned entity created (" + CountOwned(db) + ")");
            Record("STAGE " + stage + " " + (_failures == before ? "PASS" : "FAIL"));
        }

        private static void VerifyLines(AcDb.Database db, Editor ed, bool again)
        {
            var path = Path.Combine(Path.GetDirectoryName(db.Filename), Path.GetFileNameWithoutExtension(db.Filename) + ".ftf-linework.csv");
            Check(ed, File.Exists(path), "FTFLINES wrote " + Path.GetFileName(path));
            if (!File.Exists(path)) return;
            var text = File.ReadAllText(path, Encoding.UTF8);
            if (again)
            {
                Check(ed, text == _firstCsv, "the second FTFLINES wrote exactly the same inventory");
                return;
            }
            _firstCsv = text;

            var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            var header = Split(lines[0]);
            var rows = lines.Skip(1).Select(Split).Where(r => r.Count == header.Count).ToList();
            Func<List<string>, string, string> cell = delegate (List<string> r, string column) { return r[header.IndexOf(column)]; };

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var figures = Figures(db, tr);
                var shots = Shots(db, tr);
                var features = Features(db);
                foreach (var f in figures)
                {
                    var row = rows.FirstOrDefault(r => cell(r, "Handle") == f.Handle);
                    if (row == null) { Check(ed, false, f.Name + ": no FTFLINES row for its handle " + f.Handle); continue; }
                    var expectedShots = string.Join(" ", ShotsAlong(shots, f).ToArray());
                    var got = cell(row, "EntityType") + " | " + cell(row, "Figure") + " | " + cell(row, "Source") + " | " + cell(row, "Vertices") + " | " + cell(row, "Shots");
                    var code = BaseCode(f.Name);
                    var ok = cell(row, "EntityType") == "Survey Figure"
                             && cell(row, "Figure") == f.Name
                             && (!features.Contains(code) || cell(row, "Source") == "figure name")
                             && cell(row, "Vertices") == f.Points.Count.ToString(C)
                             && cell(row, "Shots") == expectedShots;
                    Check(ed, ok, f.Name + ": " + got + (ok ? string.Empty : "  (expected Survey Figure | " + f.Name + " | " +
                          (features.Contains(code) ? "figure name" : "any") + " | " + f.Points.Count + " | " + expectedShots + ")"));
                }
                tr.Commit();
            }
        }

        /// <summary>The configured line feature codes, from the rules the plugin loaded.</summary>
        private static HashSet<string> Features(AcDb.Database db)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var session = _plugin.GetType("FieldCodes.Cad.FtfSession");
            var rules = session.GetMethod("Rules", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { db });
            var list = rules.GetType().GetProperty("LineFeatures").GetValue(rules, null) as IEnumerable;
            if (list == null) return set;
            foreach (var feature in list)
            {
                var code = feature.GetType().GetProperty("Code").GetValue(feature, null) as string;
                if (!string.IsNullOrEmpty(code)) set.Add(code);
            }
            return set;
        }

        /// <summary>FNC1 -> FNC, BLD12 -> BLD.</summary>
        private static string BaseCode(string name)
        {
            var end = name.Length;
            while (end > 1 && char.IsDigit(name[end - 1])) end--;
            return name.Substring(0, end);
        }

        [CommandMethod("FIGEND")]
        public void End()
        {
            var verdict = _failures == 0 ? "PASS" : "FAIL";
            Ed.WriteMessage("\nFTF FIGURES: {0} ({1} passed, {2} failed)\n", verdict, _passes, _failures);
            Record("STAGES SNAP," + string.Join(",", Stages.ToArray()));
            Record("RESULT " + verdict + " " + _passes + " " + _failures);
            Record("COMPLETE");
        }

        // ================================================================ helpers

        private static void RequireRanOnce()
        {
            var started = Trace("StartedCount");
            var finished = Trace("FinishedCount");
            if (started == _seenStarted) Fatal("FTFLINES never ran (Unknown command, or the script fell out of step).");
            if (started != _seenStarted + 1) Fatal("FTFLINES ran " + (started - _seenStarted) + " times where once was expected.");
            if (finished != _seenFinished + 1) Fatal("FTFLINES started but did not finish (see the command line above).");
            _seenStarted = started;
            _seenFinished = finished;
        }

        private static int Trace(string method)
        {
            var trace = _plugin.GetType("FieldCodes.Cad.FtfCommandTrace");
            if (trace == null) Fatal("FieldCodes.Cad.FtfCommandTrace not found.");
            return (int)trace.GetMethod(method, BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { "FTFLINES" });
        }

        private static string FigureSnapshot(List<Figure> figures)
        {
            var parts = new List<string>();
            foreach (var f in figures)
            {
                var sb = new StringBuilder(f.Handle + "|" + f.Name + "|" + f.Layer + "|" + f.Closed);
                foreach (var p in f.Points) sb.Append(string.Format(C, " ({0:R},{1:R},{2:R})", p.X, p.Y, p.Z));
                parts.Add(sb.ToString());
            }
            parts.Sort(StringComparer.Ordinal);
            return string.Join("\n", parts.ToArray());
        }

        private static string PointSnapshot(List<Shot> shots)
        {
            var parts = shots.Select(s => string.Format(C, "{0}|{1}|{2:R}|{3:R}|{4:R}", s.Number, s.Description, s.At.X, s.At.Y, s.At.Z)).ToList();
            parts.Sort(StringComparer.Ordinal);
            return string.Join("\n", parts.ToArray());
        }

        private static int CountOwned(AcDb.Database db)
        {
            var n = 0;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (AcDb.BlockTableRecord)tr.GetObject(AcDb.SymbolUtilityServices.GetBlockModelSpaceId(db), AcDb.OpenMode.ForRead);
                foreach (AcDb.ObjectId id in ms)
                {
                    var e = tr.GetObject(id, AcDb.OpenMode.ForRead) as AcDb.Entity;
                    if (e == null) continue;
                    var xd = e.GetXDataForApplication(AppName);
                    if (xd != null) { n++; xd.Dispose(); }
                }
                tr.Commit();
            }
            return n;
        }

        /// <summary>One CSV line into fields, honouring quotes.</summary>
        private static List<string> Split(string line)
        {
            var fields = new List<string>();
            var sb = new StringBuilder();
            var quoted = false;
            for (var i = 0; i < line.Length; i++)
            {
                var ch = line[i];
                if (quoted)
                {
                    if (ch == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else if (ch == '"') quoted = false;
                    else sb.Append(ch);
                }
                else if (ch == '"') quoted = true;
                else if (ch == ',') { fields.Add(sb.ToString()); sb.Length = 0; }
                else sb.Append(ch);
            }
            fields.Add(sb.ToString());
            return fields;
        }
    }
}
