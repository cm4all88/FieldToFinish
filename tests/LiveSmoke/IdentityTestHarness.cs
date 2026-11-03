// Headless Civil 3D checks for point accountability Step 0 and Step 1 (source-point identity and
// linework coding), driven by deploy\smoke-test.ps1 as a BLOCKING stage: a failure makes UPDATEFTF put
// the previous build back.
//
// IDSEED      a fresh drawing gets synthetic COGO points (invented local coordinates, no project data)
//             and the linework drawn through them, the way a TBC export arrives (plain polylines):
//               A  fence  101 "FNC B 3.5" 102 "FNC" 103 "FNC E", LW polyline at elevation 0, points at Z 100+
//               B  hog wire 201/202 "FHW B"/"FHW E": the first vertex 0.000002 from its shot, across a 0.01
//                  rounding boundary (the old string bucket missed exactly this)
//               C  edge of pavement 301-303 "EP B"/"EP"/"EP E" as a 3D polyline (Z kept), on an unconfigured layer
//               D  fence with a doubled shot: 401 and 402 both "FNC B" at one spot, 403 "FNC E"
//               E  a line with no shots at all
//               F  a near miss: 501 is 0.05 from the polyline's first vertex (not a coincidence), 502 at its end
//               G  fence 601 "FNC1 B 6' WOOD" / 602 "FNC1 E": WOOD is a word the review list must flag
//             and snapshots every point (number, raw description, XYZ) and every seeded curve's vertices.
// IDVERIFY    first requires that the named command ran exactly once since the previous stage (FATAL
//             otherwise), then checks:
//               READER    (no command) FieldCodes.Cad.SourcePointReader reads every point with the number,
//                         handle, raw description and Z that CogoPoint itself reports
//               LINES     FTFLINES: each seeded line's CSV row (by handle) has the expected source, codes,
//                         vertex count and shot numbers
//               AGAIN     FTFLINES re-run: the CSV is identical, byte for byte
//               REVIEW    FTFREVIEWCHECK: the review list (in the drawing) holds "code.modifier:FNC WOOD" for 601
//             Every stage also checks that no point and no seeded curve changed, and that no FTF-owned
//             entity was created (both commands are read-only for the drawing's geometry).
// IDEND       writes the verdict and COMPLETE to the result file.
//
// Result protocol as ContourTestHarness: lines appended to the file named by FTF_IDENTITY_RESULT.
// Compiled with the .NET Framework csc (C# 5 only: no interpolation, no ?., no expression bodies).
using System;
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
using CivApp = Autodesk.Civil.ApplicationServices;
using CivDb = Autodesk.Civil.DatabaseServices;

[assembly: CommandClass(typeof(FtfIdentityTest.IdentityTestCommands))]

namespace FtfIdentityTest
{
    public class IdentityTestCommands
    {
        private static readonly CultureInfo C = CultureInfo.InvariantCulture;
        private const string AppName = "FTF_FIELDTOFINISH";
        private const string Fence = "V-SURF-FENC-E";
        private const string Plain = "FTF-SMOKE-LINES";

        private static int _failures;
        private static int _passes;
        private static readonly List<string> Stages = new List<string>();
        private static readonly Dictionary<string, string> Handles = new Dictionary<string, string>();
        private static string _pointSnapshot;
        private static string _curveSnapshot;
        private static string _firstCsv;
        private static Assembly _plugin;
        private static readonly Dictionary<string, int> SeenStarted = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, int> SeenFinished = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

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
            var file = Environment.GetEnvironmentVariable("FTF_IDENTITY_RESULT");
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

        // ================================================================ seed

        [CommandMethod("IDSEED")]
        public void Seed()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;
            Record("BEGIN " + DateTime.UtcNow.ToString("o", C));

            var loaded = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => string.Equals(a.GetName().Name, "FieldCodes.Cad", StringComparison.OrdinalIgnoreCase)).ToList();
            if (loaded.Count != 1) Fatal("FieldCodes.Cad is loaded " + loaded.Count + " times; expected once.");
            _plugin = loaded[0];
            foreach (var name in new[] { "FTFLINES", "FTFREVIEWCHECK" })
            {
                SeenStarted[name] = Trace("StartedCount", name);
                SeenFinished[name] = Trace("FinishedCount", name);
            }

            var cdoc = CivApp.CivilDocument.GetCivilDocument(db);
            using (var tr = db.TransactionManager.StartTransaction())
            {
                Layer(db, tr, Fence);
                Layer(db, tr, Plain);

                Point(tr, cdoc, 101, 0, 0, 100.0, "FNC B 3.5");
                Point(tr, cdoc, 102, 10, 0, 100.5, "FNC");
                Point(tr, cdoc, 103, 20, 0, 101.0, "FNC E");
                Handles["A"] = Lw(db, tr, Fence, new[] { 0.0, 0, 10, 0, 20, 0 });

                Point(tr, cdoc, 201, 100.004999, 50.004999, 100, "FHW B");
                Point(tr, cdoc, 202, 110, 50, 100, "FHW E");
                Handles["B"] = Lw(db, tr, Fence, new[] { 100.005001, 50.005001, 110, 50 });

                Point(tr, cdoc, 301, 0, 100, 200, "EP B");
                Point(tr, cdoc, 302, 10, 100, 201, "EP");
                Point(tr, cdoc, 303, 20, 100, 199, "EP E");
                Handles["C"] = Poly3d(db, tr, Plain, new[] { new Point3d(0, 100, 200), new Point3d(10, 100, 201), new Point3d(20, 100, 199) });

                Point(tr, cdoc, 401, 50, 0, 100, "FNC B");
                Point(tr, cdoc, 402, 50, 0, 100, "FNC B");
                Point(tr, cdoc, 403, 60, 0, 100, "FNC E");
                Handles["D"] = Lw(db, tr, Fence, new[] { 50.0, 0, 60, 0 });

                var line = new AcDb.Line(new Point3d(0, 200, 0), new Point3d(10, 200, 0));
                line.Layer = Plain;
                Handles["E"] = Append(db, tr, line);

                Point(tr, cdoc, 501, 0, 300, 100, "FNC B");
                Point(tr, cdoc, 502, 10, 300, 100, "FNC E");
                Handles["F"] = Lw(db, tr, Fence, new[] { 0.05, 300, 10, 300 });

                Point(tr, cdoc, 601, 0, 400, 100, "FNC1 B 6' WOOD");
                Point(tr, cdoc, 602, 10, 400, 100, "FNC1 E");
                Handles["G"] = Lw(db, tr, Fence, new[] { 0.0, 400, 10, 400 });

                tr.Commit();
            }
            _pointSnapshot = PointSnapshot(db);
            _curveSnapshot = CurveSnapshot(db);
            Check(ed, CountPoints(db) == 15, "15 synthetic COGO points seeded (" + CountPoints(db) + ")");
            Record("SEEDED");
        }

        // ================================================================ verify

        [CommandMethod("IDVERIFY")]
        public void Verify()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;
            var stage = ed.GetString("\nStage: ").StringResult.Trim().ToUpperInvariant();
            Stages.Add(stage);
            var before = _failures;

            if (stage == "READER") VerifyReader(db, ed);
            else if (stage == "LINES") { RequireRanOnce("FTFLINES"); VerifyLines(db, ed, false); }
            else if (stage == "AGAIN") { RequireRanOnce("FTFLINES"); VerifyLines(db, ed, true); }
            else if (stage == "REVIEW") { RequireRanOnce("FTFREVIEWCHECK"); VerifyReview(db, ed); }
            else Fatal("unknown stage " + stage);

            Check(ed, PointSnapshot(db) == _pointSnapshot, stage + ": every COGO point unchanged (number, raw description, XYZ)");
            Check(ed, CurveSnapshot(db) == _curveSnapshot, stage + ": every seeded line unchanged");
            Check(ed, CountOwned(db) == 0, stage + ": no FTF-owned entity created (" + CountOwned(db) + ")");
            Record("STAGE " + stage + " " + (_failures == before ? "PASS" : "FAIL"));
        }

        private static void VerifyReader(AcDb.Database db, Editor ed)
        {
            var reader = _plugin.GetType("FieldCodes.Cad.SourcePointReader");
            if (reader == null) Fatal("FieldCodes.Cad.SourcePointReader not found: the loaded build predates Step 1.");
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var index = reader.GetMethod("Read", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                                  .Invoke(null, new object[] { db, tr });
                var all = ((System.Collections.IEnumerable)index.GetType().GetProperty("All").GetValue(index, null)).Cast<object>().ToList();
                Check(ed, all.Count == 15, "the reader returns every point (" + all.Count + ")");
                var mismatches = new List<string>();
                var ms = (AcDb.BlockTableRecord)tr.GetObject(AcDb.SymbolUtilityServices.GetBlockModelSpaceId(db), AcDb.OpenMode.ForRead);
                foreach (AcDb.ObjectId id in ms)
                {
                    var cogo = tr.GetObject(id, AcDb.OpenMode.ForRead) as CivDb.CogoPoint;
                    if (cogo == null) continue;
                    var number = cogo.PointNumber.ToString(C);
                    var found = all.FirstOrDefault(p => (string)Prop(p, "Number") == number);
                    if (found == null) { mismatches.Add(number + " missing"); continue; }
                    if ((string)Prop(found, "Handle") != cogo.Handle.ToString()) mismatches.Add(number + " handle");
                    if ((string)Prop(found, "RawDescription") != (cogo.RawDescription ?? string.Empty)) mismatches.Add(number + " raw description");
                    if (Math.Abs((double)Prop(found, "Z") - cogo.Location.Z) > 1e-9) mismatches.Add(number + " Z");
                    if (Math.Abs((double)Prop(found, "X") - cogo.Location.X) > 1e-9) mismatches.Add(number + " X");
                    if (Math.Abs((double)Prop(found, "Y") - cogo.Location.Y) > 1e-9) mismatches.Add(number + " Y");
                }
                Check(ed, mismatches.Count == 0, "number, handle, raw description and XYZ match CogoPoint for every point" +
                      (mismatches.Count == 0 ? string.Empty : ": " + string.Join(", ", mismatches.ToArray())));

                var at = index.GetType().GetMethod("At");
                var stacked = ((System.Collections.IEnumerable)at.Invoke(index, new object[] { 50.0, 0.0 })).Cast<object>().Select(p => (string)Prop(p, "Number")).ToArray();
                Check(ed, string.Join(" ", stacked) == "401 402", "both stacked shots found at one spot (" + string.Join(" ", stacked) + ")");
                var boundary = ((System.Collections.IEnumerable)at.Invoke(index, new object[] { 100.005001, 50.005001 })).Cast<object>().Count();
                Check(ed, boundary == 1, "a shot 0.000002 away across a rounding boundary is found (" + boundary + ")");
                var miss = ((System.Collections.IEnumerable)at.Invoke(index, new object[] { 0.05, 300.0 })).Cast<object>().Count();
                Check(ed, miss == 0, "a shot 0.05 away is not taken for the vertex's own (" + miss + ")");
                tr.Commit();
            }
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
            Check(ed, header.Contains("Handle") && header.Contains("Shots") && header.Contains("Vertices"), "the CSV has Handle, Vertices and Shots columns");
            var rows = lines.Skip(1).Select(Split).ToList();
            Func<string, List<string>> row = delegate (string key)
            {
                var h = Handles[key];
                return rows.FirstOrDefault(r => r.Count == header.Count && r[header.IndexOf("Handle")] == h);
            };
            Expect(ed, row("A"), header, "A fence", "point notes", "FNC", "3", "101 102 103");
            Expect(ed, row("B"), header, "B rounding boundary", "point notes", "FHW", "2", "201 202");
            Expect(ed, row("C"), header, "C 3D polyline", "point notes", "EP", "3", "301 302 303");
            Expect(ed, row("D"), header, "D doubled shot", "point notes", "FNC", "2", "401 402 403");
            Expect(ed, row("E"), header, "E no shots", "-", null, "2", "");
            Expect(ed, row("F"), header, "F near miss", "point notes", "FNC", "2", "502");
            Expect(ed, row("G"), header, "G figure number", "point notes", "FNC", "2", "601 602");
        }

        private static void Expect(Editor ed, List<string> r, List<string> header, string what, string source, string codes, string vertices, string shots)
        {
            if (r == null) { Check(ed, false, what + ": no CSV row for its handle"); return; }
            var got = r[header.IndexOf("Source")] + " | " + r[header.IndexOf("Codes")] + " | " + r[header.IndexOf("Vertices")] + " | " + r[header.IndexOf("Shots")];
            var ok = r[header.IndexOf("Source")] == source
                     && (codes == null || r[header.IndexOf("Codes")] == codes)
                     && r[header.IndexOf("Vertices")] == vertices
                     && r[header.IndexOf("Shots")] == shots;
            Check(ed, ok, what + ": " + got);
        }

        private static void VerifyReview(AcDb.Database db, Editor ed)
        {
            string json = null;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var nod = (AcDb.DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, AcDb.OpenMode.ForRead);
                if (nod.Contains(AppName))
                {
                    var root = (AcDb.DBDictionary)tr.GetObject(nod.GetAt(AppName), AcDb.OpenMode.ForRead);
                    if (root.Contains("REVIEW"))
                    {
                        var x = (AcDb.Xrecord)tr.GetObject(root.GetAt("REVIEW"), AcDb.OpenMode.ForRead);
                        var sb = new StringBuilder();
                        foreach (AcDb.TypedValue tv in x.Data) if (tv.TypeCode == (int)AcDb.DxfCode.Text) sb.Append((string)tv.Value);
                        json = sb.ToString();
                    }
                }
                tr.Commit();
            }
            Check(ed, json != null, "the review list is stored in the drawing");
            if (json == null) return;
            Check(ed, json.Contains("\"code.modifier:FNC WOOD\""), "WOOD after FNC1 is on the review list as code.modifier:FNC WOOD");
            Check(ed, json.Contains("\"points\":\"601\""), "the item names point 601");
            Check(ed, !json.Contains("code.modifier:FNC B") && !json.Contains("code.modifier:FNC 3.5"),
                  "begin codes and heights are not taken for unknown words");
        }

        [CommandMethod("IDEND")]
        public void End()
        {
            var verdict = _failures == 0 ? "PASS" : "FAIL";
            Ed.WriteMessage("\nFTF IDENTITY: {0} ({1} passed, {2} failed)\n", verdict, _passes, _failures);
            Record("STAGES " + string.Join(",", Stages.ToArray()));
            Record("RESULT " + verdict + " " + _passes + " " + _failures);
            Record("COMPLETE");
        }

        // ================================================================ helpers

        private static void RequireRanOnce(string name)
        {
            var started = Trace("StartedCount", name);
            var finished = Trace("FinishedCount", name);
            int seenStarted, seenFinished;
            SeenStarted.TryGetValue(name, out seenStarted);
            SeenFinished.TryGetValue(name, out seenFinished);
            if (started == seenStarted) Fatal(name + " never ran (Unknown command, or the script fell out of step).");
            if (started != seenStarted + 1) Fatal(name + " ran " + (started - seenStarted) + " times where once was expected.");
            if (finished != seenFinished + 1) Fatal(name + " started but did not finish (see the command line above).");
            SeenStarted[name] = started;
            SeenFinished[name] = finished;
        }

        private static int Trace(string method, string command)
        {
            var trace = _plugin.GetType("FieldCodes.Cad.FtfCommandTrace");
            if (trace == null) Fatal("FieldCodes.Cad.FtfCommandTrace not found.");
            return (int)trace.GetMethod(method, BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { command });
        }

        private static object Prop(object o, string name)
        {
            return o.GetType().GetProperty(name).GetValue(o, null);
        }

        private static void Layer(AcDb.Database db, AcDb.Transaction tr, string name)
        {
            var lt = (AcDb.LayerTable)tr.GetObject(db.LayerTableId, AcDb.OpenMode.ForRead);
            if (lt.Has(name)) return;
            lt.UpgradeOpen();
            var rec = new AcDb.LayerTableRecord { Name = name };
            lt.Add(rec);
            tr.AddNewlyCreatedDBObject(rec, true);
        }

        private static void Point(AcDb.Transaction tr, CivApp.CivilDocument cdoc, uint number, double x, double y, double z, string description)
        {
            var id = cdoc.CogoPoints.Add(new Point3d(x, y, z), description, false);
            var cp = (CivDb.CogoPoint)tr.GetObject(id, AcDb.OpenMode.ForWrite);
            try { cp.PointNumber = number; }
            catch (System.Exception ex) { Fatal("could not set point number " + number + ": " + ex.Message); }
        }

        private static string Lw(AcDb.Database db, AcDb.Transaction tr, string layer, double[] xy)
        {
            var pl = new AcDb.Polyline();
            for (var i = 0; i < xy.Length; i += 2) pl.AddVertexAt(i / 2, new Point2d(xy[i], xy[i + 1]), 0, 0, 0);
            pl.Layer = layer;
            return Append(db, tr, pl);
        }

        private static string Poly3d(AcDb.Database db, AcDb.Transaction tr, string layer, Point3d[] points)
        {
            var pl = new AcDb.Polyline3d(AcDb.Poly3dType.SimplePoly, new Point3dCollection(points), false);
            pl.Layer = layer;
            return Append(db, tr, pl);
        }

        private static string Append(AcDb.Database db, AcDb.Transaction tr, AcDb.Entity e)
        {
            var ms = (AcDb.BlockTableRecord)tr.GetObject(AcDb.SymbolUtilityServices.GetBlockModelSpaceId(db), AcDb.OpenMode.ForWrite);
            ms.AppendEntity(e);
            tr.AddNewlyCreatedDBObject(e, true);
            return e.Handle.ToString();
        }

        private static int CountPoints(AcDb.Database db)
        {
            var n = 0;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (AcDb.BlockTableRecord)tr.GetObject(AcDb.SymbolUtilityServices.GetBlockModelSpaceId(db), AcDb.OpenMode.ForRead);
                foreach (AcDb.ObjectId id in ms) if (tr.GetObject(id, AcDb.OpenMode.ForRead) is CivDb.CogoPoint) n++;
                tr.Commit();
            }
            return n;
        }

        private static string PointSnapshot(AcDb.Database db)
        {
            var parts = new List<string>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (AcDb.BlockTableRecord)tr.GetObject(AcDb.SymbolUtilityServices.GetBlockModelSpaceId(db), AcDb.OpenMode.ForRead);
                foreach (AcDb.ObjectId id in ms)
                {
                    var p = tr.GetObject(id, AcDb.OpenMode.ForRead) as CivDb.CogoPoint;
                    if (p == null) continue;
                    parts.Add(string.Format(C, "{0}|{1}|{2:R}|{3:R}|{4:R}", p.PointNumber, p.RawDescription, p.Location.X, p.Location.Y, p.Location.Z));
                }
                tr.Commit();
            }
            parts.Sort(StringComparer.Ordinal);
            return string.Join("\n", parts.ToArray());
        }

        private static string CurveSnapshot(AcDb.Database db)
        {
            var parts = new List<string>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (var pair in Handles)
                {
                    AcDb.ObjectId id;
                    if (!db.TryGetObjectId(new AcDb.Handle(Convert.ToInt64(pair.Value, 16)), out id) || id.IsErased) { parts.Add(pair.Key + " missing"); continue; }
                    var c = (AcDb.Curve)tr.GetObject(id, AcDb.OpenMode.ForRead);
                    var sb = new StringBuilder(pair.Key + " " + c.Layer);
                    for (var p = c.StartParam; p <= c.EndParam + 1e-9; p += 1.0)
                    {
                        var at = c.GetPointAtParameter(Math.Min(p, c.EndParam));
                        sb.Append(string.Format(C, " ({0:R},{1:R},{2:R})", at.X, at.Y, at.Z));
                    }
                    parts.Add(sb.ToString());
                }
                tr.Commit();
            }
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
