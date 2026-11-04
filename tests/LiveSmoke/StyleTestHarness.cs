// Headless checks that Settings > Text Styles, Leader Styles and Style Assignments control the
// objects FTFLABELS draws, and that FTFRESTYLE updates them in place.
//
// A REQUIRED stage of the deploy smoke test (deploy\smoke-test.ps1, Invoke-RequiredCheck): a failure
// makes UPDATEFTF put the previous build back.
//
// The drawing folder holds the build's own rules.json and an ftf-settings.json (both written by
// deploy\smoke-test.ps1). The settings:
//   text style  "Smoke text":   drawing text style FTFSMOKE, 0.12" high, colour 3, prefix "S-", background mask
//   leader      "Smoke leader": spline, _Dot arrow 0.05", colour 1, no landing
//   assignments: family Utilities -> Smoke text, layer FTF-SMOKE-UTIL
//                code FH          -> Smoke leader, leader Always
//                code WFH         -> Smoke leader, leader Always
//                code MB          -> not labelled
// The drafting standard (built in): a fire hydrant (WFH, FH) gets no text label. STYLESEED adds
// an office standard to the rules beside the drawing that labels FH -- on purpose, so the hydrant's
// label styling is tested -- and leaves WFH to the built-in standard: its leader Always assignment
// must then draw nothing, since a leader belongs to a label and the standard wants none.
//
// STYLESEED     the office standard for FH, a text style FTFSMOKE and five COGO points:
//               FH 12, WFH 13, PP 1234, MB 5, SN 135 STOP
// STYLEVERIFY   first requires that the named command ran exactly once since the previous stage, then
//               checks the stage named: STYLED (after FTFLABELS), PREVIEW (after FTFRESTYLE Preview: nothing
//               changed), RESTYLED (after STYLEEDIT and FTFRESTYLE Apply: the new look, nothing moved)
// STYLEMOVE     moves the pole's label by hand
// STYLEEDIT     rewrites the settings: Smoke text 0.15" and colour 5, then has FTF re-read them
// STYLEEND      writes the verdict
//
// Results go to the file named by FTF_STYLE_RESULT. Compiled with the .NET Framework csc (C# 5 only).
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
using Civil = Autodesk.Civil.DatabaseServices;

[assembly: CommandClass(typeof(FtfStyleTest.StyleTestCommands))]

namespace FtfStyleTest
{
    public class StyleTestCommands
    {
        private static readonly CultureInfo C = CultureInfo.InvariantCulture;
        private const string AppName = "FTF_FIELDTOFINISH";
        private static short LabelKind = 3;

        private static int _failures;
        private static int _passes;
        private static readonly List<string> Stages = new List<string>();
        private static readonly Dictionary<string, string> Numbers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static Point3d _movedTo;
        private static Point3d _poleBefore;
        private static bool _moved;
        private static Point3d? _hydrantText;
        private static Point3d? _hydrantArrow;
        private static Assembly _plugin;
        private static readonly Dictionary<string, int> SeenStarted = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, int> SeenFinished = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private const string StartedCount = "FieldCodes.Cad.FtfCommandTrace.StartedCount(System.String)";
        private const string FinishedCount = "FieldCodes.Cad.FtfCommandTrace.FinishedCount(System.String)";
        // The office standard STYLESEED puts in the rules beside the drawing: FH is labelled.
        private const string HydrantLabelled = @"{ ""requirements"": [ { ""name"": ""Fire hydrant, labelled (style smoke test)"", ""codes"": [""FH""], ""label"": ""AsPlanned"", ""symbol"": ""PointStyle"" } ] }";

        private static Editor Ed { get { return Application.DocumentManager.MdiActiveDocument.Editor; } }

        private static void Check(bool ok, string what)
        {
            if (ok) _passes++; else _failures++;
            Ed.WriteMessage("\n  [{0}] {1}", ok ? "PASS" : "FAIL", what);
            Record((ok ? "PASS " : "FAIL ") + what);
        }

        /// <summary>Stops the run now: nothing after this can be trusted.</summary>
        private static void Fatal(string why)
        {
            _failures++;
            try { Ed.WriteMessage("\n  [FATAL] {0}\n", why); } catch (System.Exception) { }
            Record("FATAL " + why);
            Environment.Exit(3);
        }

        // ==== plugin contract: this block is the same in every Civil 3D harness (HarnessContractTests) ====
        // The plugin is reached by reflection by exact signature, "Namespace.Type.Method(Full.Type, ...)",
        // never by name alone: a name is ambiguous as soon as the plugin adds an overload. That is how
        // IDVERIFY READER died at 68804df: SourcePointReader gained Read(db, tr, notes), GetMethod("Read")
        // threw AmbiguousMatchException, nothing caught it, and accoreconsole ended without a word.
        // A signature that matches no method, or several, throws with the overloads the build has.
        // Guard records any exception in full after the steps that led to it, and makes it FATAL.

        private static readonly Dictionary<string, MethodInfo> ContractCache = new Dictionary<string, MethodInfo>(StringComparer.Ordinal);

        /// <summary>The one method <paramref name="signature"/> names, in the plugin or in the
        /// FieldCodes library it loaded.</summary>
        private static MethodInfo Contract(Assembly plugin, string signature)
        {
            MethodInfo cached;
            if (ContractCache.TryGetValue(signature, out cached)) return cached;
            var open = signature.IndexOf('(');
            var dot = open < 0 ? -1 : signature.LastIndexOf('.', open);
            if (dot < 0 || !signature.EndsWith(")", StringComparison.Ordinal))
                throw new ArgumentException("plugin contract: not a signature: " + signature);
            var typeName = signature.Substring(0, dot);
            var name = signature.Substring(dot + 1, open - dot - 1);
            var wanted = signature.Substring(open + 1, signature.Length - open - 2).Replace(" ", string.Empty);
            var type = ContractType(plugin, typeName);
            if (type == null) throw new InvalidOperationException("plugin contract: the loaded build has no type " + typeName + " (" + signature + ")");
            var named = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                            .Where(m => m.Name == name).ToList();
            var matches = named.Where(m => ParameterList(m) == wanted).ToList();
            if (matches.Count != 1)
                throw new InvalidOperationException("plugin contract: " + signature + " matches " + matches.Count + " methods; the loaded build has " +
                    (named.Count == 0 ? "no method " + name : string.Join("; ", named.Select(m => name + "(" + ParameterList(m) + ")").ToArray())));
            ContractCache[signature] = matches[0];
            return matches[0];
        }

        /// <summary>Parameter types as the signatures write them: full names, generic arguments in
        /// brackets, the same on .NET Framework and .NET.</summary>
        private static string ParameterList(MethodInfo m)
        {
            return string.Join(",", m.GetParameters().Select(p => TypeText(p.ParameterType)).ToArray());
        }

        private static string TypeText(Type t)
        {
            if (t.IsByRef) return TypeText(t.GetElementType()) + "&";
            if (t.IsArray) return TypeText(t.GetElementType()) + "[]";
            if (t.IsGenericType && !t.IsGenericTypeDefinition)
                return t.GetGenericTypeDefinition().FullName + "[" + string.Join(",", t.GetGenericArguments().Select(a => TypeText(a)).ToArray()) + "]";
            return t.FullName ?? t.Name;
        }

        private static Type ContractType(Assembly plugin, string typeName)
        {
            var type = plugin.GetType(typeName);
            if (type != null) return type;
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!string.Equals(a.GetName().Name, "FieldCodes", StringComparison.OrdinalIgnoreCase)) continue;
                type = a.GetType(typeName);
                if (type != null) return type;
            }
            // Civil 3D loads the library only when the plugin first needs it. Have the plugin load it
            // now, through its own binding (so it is the same FieldCodes the plugin uses): resolve the
            // return types of its methods until one is a library type.
            var methods = new List<MethodInfo>(ContractCache.Values);
            Type[] types;
            try { types = plugin.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray(); }
            foreach (var t in types)
                methods.AddRange(t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly));
            foreach (var m in methods)
            {
                Assembly library;
                try { library = m.ReturnType.Assembly; }
                catch (System.Exception) { continue; }
                if (string.Equals(library.GetName().Name, "FieldCodes", StringComparison.OrdinalIgnoreCase)) return library.GetType(typeName);
            }
            return null;
        }

        /// <summary>Calls the method <paramref name="signature"/> names (<paramref name="target"/> is
        /// ignored for a static one). An exception it throws comes out as itself, not wrapped.</summary>
        private static object Call(Assembly plugin, string signature, object target, params object[] args)
        {
            var m = Contract(plugin, signature);
            try { return m.Invoke(m.IsStatic ? null : target, args); }
            catch (TargetInvocationException ex)
            {
                if (ex.InnerException != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }
        }

        /// <summary>Runs a command's body. An exception from it is recorded in full (type, message,
        /// inner exceptions, stacks) after the steps that led to it, and is FATAL.</summary>
        private static void Guard(string what, Action body)
        {
            Step(what + ": begin");
            try { body(); }
            catch (System.Exception ex)
            {
                var inner = ex;
                while (inner is TargetInvocationException && inner.InnerException != null) inner = inner.InnerException;
                foreach (var line in ex.ToString().Split('\n'))
                    if (line.Trim().Length > 0) Record("EXCEPTION " + line.TrimEnd('\r'));
                Fatal(what + " threw " + inner.GetType().FullName + ": " + inner.Message);
            }
            Step(what + ": end");
        }

        /// <summary>"STEP time what" in the result file, on the disk before the step is taken.</summary>
        private static void Step(string what)
        {
            Record("STEP " + DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + " " + what);
        }

        /// <summary>Appends a line and writes it through to the disk before returning, so it
        /// survives the process ending at the next instruction.</summary>
        private static void AppendDurably(string file, string line)
        {
            var bytes = Encoding.UTF8.GetBytes(line + Environment.NewLine);
            using (var stream = new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.ReadWrite, 4096, FileOptions.WriteThrough))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
        }
        // ==== end of plugin contract ====

        private static void Record(string line)
        {
            var file = Environment.GetEnvironmentVariable("FTF_STYLE_RESULT");
            if (string.IsNullOrEmpty(file)) return;
            try { AppendDurably(file, line); }
            catch (System.Exception) { }
        }

        private static double Scale(AcDb.Database db)
        {
            var s = db.Cannoscale;
            if (s == null || s.PaperUnits <= 0) return 1.0;
            return s.DrawingUnits / s.PaperUnits;
        }

        [CommandMethod("STYLESEED")]
        public void Seed()
        {
            Guard("STYLESEED", delegate { SeedBody(); });
        }

        private static void SeedBody()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            Record("BEGIN " + DateTime.UtcNow.ToString("o", C));
            var plugin = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "FieldCodes.Cad");
            if (plugin == null) Fatal("FieldCodes.Cad is not loaded.");
            _plugin = plugin;
            foreach (var signature in new[] { StartedCount, FinishedCount }) Contract(plugin, signature);
            foreach (var name in new[] { "FTFLABELS", "FTFRESTYLE" })
            {
                SeenStarted[name] = Trace(StartedCount, name);
                SeenFinished[name] = Trace(FinishedCount, name);
            }
            OfficeStandard(db);
            var kinds = plugin != null ? plugin.GetType("FieldCodes.Cad.FtfEntityKind") : null;
            if (kinds != null) LabelKind = Convert.ToInt16(Enum.Parse(kinds, "Label"));
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var styles = (AcDb.TextStyleTable)tr.GetObject(db.TextStyleTableId, AcDb.OpenMode.ForWrite);
                if (!styles.Has("FTFSMOKE"))
                {
                    var style = new AcDb.TextStyleTableRecord();
                    style.Name = "FTFSMOKE";
                    style.FileName = "romans.shx";
                    styles.Add(style);
                    tr.AddNewlyCreatedDBObject(style, true);
                }

                var civil = Autodesk.Civil.ApplicationServices.CivilDocument.GetCivilDocument(db);
                var seeds = new[]
                {
                    new KeyValuePair<string, Point3d>("FH 12", new Point3d(1000, 1000, 100)),
                    new KeyValuePair<string, Point3d>("WFH 13", new Point3d(1000, 1200, 100)),
                    new KeyValuePair<string, Point3d>("PP 1234", new Point3d(1200, 1000, 100)),
                    new KeyValuePair<string, Point3d>("MB 5", new Point3d(1400, 1000, 100)),
                    new KeyValuePair<string, Point3d>("SN 135 STOP", new Point3d(1600, 1000, 100))
                };
                foreach (var seed in seeds)
                {
                    var id = civil.CogoPoints.Add(seed.Value, seed.Key, false);
                    var point = (Civil.CogoPoint)tr.GetObject(id, AcDb.OpenMode.ForRead);
                    Numbers[seed.Key.Split(' ')[0]] = point.PointNumber.ToString(C);
                }
                tr.Commit();
            }
            Check(Numbers.Count == 5, "five COGO points and text style FTFSMOKE seeded (scale " + Scale(db).ToString("0.##", C) + ")");
        }

        /// <summary>The office standard that labels FH, added to the build's rules.json beside the
        /// drawing (deploy\smoke-test.ps1 copies it there). The plugin re-reads rules whose file changed.</summary>
        private static void OfficeStandard(AcDb.Database db)
        {
            var path = Path.Combine(Path.GetDirectoryName(db.Filename), "rules.json");
            if (!File.Exists(path)) Fatal("no rules.json beside the drawing (" + path + "): the smoke test copies the build's own there.");
            var text = File.ReadAllText(path);
            if (text.Contains("\"featureStandard\"")) Fatal("the rules beside the drawing already have a featureStandard; the style check needs the factory rules.");
            text = text.TrimEnd();
            if (!text.EndsWith("}", StringComparison.Ordinal)) Fatal("rules.json does not end with }");
            var before = File.GetLastWriteTimeUtc(path);
            File.WriteAllText(path, text.Substring(0, text.Length - 1).TrimEnd() + ",\r\n  \"featureStandard\": " + HydrantLabelled + "\r\n}\r\n", new UTF8Encoding(false));
            var now = DateTime.UtcNow;
            File.SetLastWriteTimeUtc(path, now < before.AddSeconds(2) ? before.AddSeconds(2) : now);
            Record("NOTE office standard in the rules beside the drawing: " + HydrantLabelled);
        }

        private static int Trace(string signature, string command)
        {
            return (int)Call(_plugin, signature, null, command);
        }

        /// <summary>The named command ran exactly once, and finished, since the previous stage.</summary>
        private static void RequireRanOnce(string name)
        {
            var started = Trace(StartedCount, name);
            var finished = Trace(FinishedCount, name);
            int seenStarted, seenFinished;
            SeenStarted.TryGetValue(name, out seenStarted);
            SeenFinished.TryGetValue(name, out seenFinished);
            if (started == seenStarted) Fatal(name + " never ran (Unknown command, or the script fell out of step).");
            if (started != seenStarted + 1) Fatal(name + " ran " + (started - seenStarted) + " times where once was expected.");
            if (finished != seenFinished + 1) Fatal(name + " started but did not finish (see the command line above).");
            SeenStarted[name] = started;
            SeenFinished[name] = finished;
        }

        /// <summary>Every FTF label (text or multileader) drawn for the point.</summary>
        private static int LabelsOf(AcDb.Transaction tr, AcDb.Database db, string code)
        {
            string number;
            if (!Numbers.TryGetValue(code, out number)) return -1;
            var n = 0;
            var ms = (AcDb.BlockTableRecord)tr.GetObject(AcDb.SymbolUtilityServices.GetBlockModelSpaceId(db), AcDb.OpenMode.ForRead);
            foreach (AcDb.ObjectId id in ms)
            {
                var e = tr.GetObject(id, AcDb.OpenMode.ForRead) as AcDb.Entity;
                if (e == null || !(e is AcDb.MText || e is AcDb.MLeader)) continue;
                using (var rb = e.GetXDataForApplication(AppName))
                {
                    if (rb == null) continue;
                    var s = new List<string>();
                    short kind = -1;
                    foreach (AcDb.TypedValue tv in rb)
                    {
                        if (tv.TypeCode == (short)AcDb.DxfCode.ExtendedDataAsciiString) s.Add(tv.Value as string);
                        if (tv.TypeCode == (short)AcDb.DxfCode.ExtendedDataInteger16) kind = Convert.ToInt16(tv.Value);
                    }
                    if (kind == LabelKind && s.Count >= 3 && s[2] == number) n++;
                }
            }
            return n;
        }

        private static int Multileaders(AcDb.Transaction tr, AcDb.Database db)
        {
            var n = 0;
            var ms = (AcDb.BlockTableRecord)tr.GetObject(AcDb.SymbolUtilityServices.GetBlockModelSpaceId(db), AcDb.OpenMode.ForRead);
            foreach (AcDb.ObjectId id in ms) if (tr.GetObject(id, AcDb.OpenMode.ForRead) is AcDb.MLeader) n++;
            return n;
        }

        /// <summary>The arrow end of a multileader's first leader line.</summary>
        private static Point3d? ArrowOf(AcDb.MLeader ml)
        {
            try
            {
                foreach (var index in ml.GetLeaderLineIndexes(0))
                    return ml.GetFirstVertex(Convert.ToInt32(index, C));
            }
            catch (System.Exception) { }
            return null;
        }

        private static string Xy(Point3d p) { return p.X.ToString("0.###", C) + "," + p.Y.ToString("0.###", C); }

        /// <summary>FTF's label for a point: its text entity (MText or MLeader) and its XData strings.</summary>
        private static AcDb.Entity LabelOf(AcDb.Transaction tr, AcDb.Database db, string code, out List<string> strings)
        {
            strings = null;
            string number;
            if (!Numbers.TryGetValue(code, out number)) return null;
            var ms = (AcDb.BlockTableRecord)tr.GetObject(AcDb.SymbolUtilityServices.GetBlockModelSpaceId(db), AcDb.OpenMode.ForRead);
            foreach (AcDb.ObjectId id in ms)
            {
                var e = tr.GetObject(id, AcDb.OpenMode.ForRead) as AcDb.Entity;
                if (e == null || !(e is AcDb.MText || e is AcDb.MLeader)) continue;
                using (var rb = e.GetXDataForApplication(AppName))
                {
                    if (rb == null) continue;
                    var s = new List<string>();
                    short kind = -1;
                    foreach (AcDb.TypedValue tv in rb)
                    {
                        if (tv.TypeCode == (short)AcDb.DxfCode.ExtendedDataAsciiString) s.Add(tv.Value as string);
                        if (tv.TypeCode == (short)AcDb.DxfCode.ExtendedDataInteger16) kind = Convert.ToInt16(tv.Value);
                    }
                    if (kind != LabelKind || s.Count < 3 || s[2] != number) continue;
                    strings = s;
                    return e;
                }
            }
            return null;
        }

        private static string Contents(AcDb.Entity e)
        {
            var mt = e as AcDb.MText;
            if (mt != null) return mt.Contents;
            var ml = e as AcDb.MLeader;
            if (ml != null && ml.MText != null) return ml.MText.Contents;
            return string.Empty;
        }

        private static double Height(AcDb.Entity e)
        {
            var mt = e as AcDb.MText;
            if (mt != null) return mt.TextHeight;
            var ml = e as AcDb.MLeader;
            return ml != null && ml.MText != null ? ml.MText.TextHeight : -1;
        }

        private static string StyleName(AcDb.Transaction tr, AcDb.Entity e)
        {
            var mt = e as AcDb.MText;
            var id = mt != null ? mt.TextStyleId : ((AcDb.MLeader)e).MText.TextStyleId;
            var record = tr.GetObject(id, AcDb.OpenMode.ForRead) as AcDb.TextStyleTableRecord;
            return record != null ? record.Name : string.Empty;
        }

        private static Point3d Where(AcDb.Entity e)
        {
            var mt = e as AcDb.MText;
            if (mt != null) return mt.Location;
            return ((AcDb.MLeader)e).TextLocation;
        }

        private static bool Near(double a, double b) { return Math.Abs(a - b) <= Math.Max(1e-6, Math.Abs(b) * 1e-3); }

        [CommandMethod("STYLEVERIFY")]
        public void Verify()
        {
            Guard("STYLEVERIFY", delegate { VerifyBody(); });
        }

        private static void VerifyBody()
        {
            var stage = Ed.GetString("\nStage: ").StringResult.Trim().ToUpperInvariant();
            if (stage == "STYLED") RequireRanOnce("FTFLABELS");
            else if (stage == "PREVIEW" || stage == "RESTYLED") RequireRanOnce("FTFRESTYLE");
            else Fatal("unknown stage " + stage);
            var db = Application.DocumentManager.MdiActiveDocument.Database;
            var scale = Scale(db);
            var failuresBefore = _failures;
            var smokeHeight = (stage == "RESTYLED" ? 0.15 : 0.12) * scale;
            var smokeColour = (short)(stage == "RESTYLED" ? 5 : 3);
            Ed.WriteMessage("\nSTYLEVERIFY {0}:", stage);

            using (var tr = db.TransactionManager.StartTransaction())
            {
                List<string> xd;
                var fh = LabelOf(tr, db, "FH", out xd);
                Check(fh is AcDb.MLeader && LabelsOf(tr, db, "FH") == 1,
                      stage + ": the hydrant (leader Always; labelled by the office standard) is one multileader (" +
                      (fh == null ? "no label" : fh.GetType().Name) + ", " + LabelsOf(tr, db, "FH") + " label(s))");
                // WFH: leader Always is assigned, but its standard (built in) draws no label -- and so no leader.
                Check(LabelsOf(tr, db, "WFH") == 0, stage + ": the hydrant WFH 13 (its standard: no label) has no label, its leader Always assignment notwithstanding (" +
                      LabelsOf(tr, db, "WFH") + ")");
                Check(Multileaders(tr, db) == (fh is AcDb.MLeader ? 1 : 0) + (LabelOf(tr, db, "PP", out xd) is AcDb.MLeader ? 1 : 0) + (LabelOf(tr, db, "SN", out xd) is AcDb.MLeader ? 1 : 0),
                      stage + ": no multileader but the labels' own (" + Multileaders(tr, db) + ")");
                fh = LabelOf(tr, db, "FH", out xd);
                if (fh is AcDb.MLeader)
                {
                    var ml = (AcDb.MLeader)fh;
                    var arrowAt = ArrowOf(ml);
                    if (stage == "STYLED") { _hydrantText = ml.TextLocation; _hydrantArrow = arrowAt; }
                    else if (_hydrantText.HasValue)
                    {
                        Check(ml.TextLocation.DistanceTo(_hydrantText.Value) < 1e-6 && arrowAt.HasValue && _hydrantArrow.HasValue && arrowAt.Value.DistanceTo(_hydrantArrow.Value) < 1e-6,
                              stage + ": FTFRESTYLE moved nothing: the hydrant's text and arrow are where FTFLABELS put them (text " + Xy(_hydrantText.Value) + " -> " + Xy(ml.TextLocation) +
                              (arrowAt.HasValue && _hydrantArrow.HasValue ? ", arrow " + Xy(_hydrantArrow.Value) + " -> " + Xy(arrowAt.Value) : ", arrow not read") + ")");
                    }
                    Check(Contents(fh).StartsWith("S-HYDRANT 12", StringComparison.Ordinal), stage + ": hydrant text has the style's prefix (" + Contents(fh) + ")");
                    Check(StyleName(tr, fh).Equals("FTFSMOKE", StringComparison.OrdinalIgnoreCase), stage + ": hydrant text style is FTFSMOKE (" + StyleName(tr, fh) + ")");
                    Check(Near(Height(fh), smokeHeight), stage + ": hydrant text height " + Height(fh).ToString("0.###", C) + " = " + smokeHeight.ToString("0.###", C));
                    Check(ml.LeaderLineType == AcDb.LeaderType.SplineLeader, stage + ": hydrant leader is a spline");
                    Check(ml.LeaderLineColor != null && ml.LeaderLineColor.ColorIndex == 1, stage + ": hydrant leader colour 1");
                    var arrow = ml.ArrowSymbolId.IsNull ? null : tr.GetObject(ml.ArrowSymbolId, AcDb.OpenMode.ForRead) as AcDb.BlockTableRecord;
                    Check(arrow != null && arrow.Name.Equals("_DOT", StringComparison.OrdinalIgnoreCase), stage + ": hydrant arrowhead is _Dot (" + (arrow != null ? arrow.Name : "style default") + ")");
                    Check(!ml.EnableLanding, stage + ": hydrant leader has no landing");
                    Check(ml.MText != null && ml.MText.BackgroundFill, stage + ": hydrant text has a background mask");
                    Check(ml.TextColor != null && ml.TextColor.ColorIndex == smokeColour, stage + ": hydrant text colour " + smokeColour);
                    Check(fh.Layer.Equals("FTF-SMOKE-UTIL", StringComparison.OrdinalIgnoreCase), stage + ": hydrant label on FTF-SMOKE-UTIL (" + fh.Layer + ")");
                    Check(xd != null && xd.Count >= 6 && xd[5] == "Smoke text|Smoke leader|background", stage + ": hydrant stamp records its style (" + (xd != null && xd.Count >= 6 ? xd[5] : "none") + ")");
                }

                var pp = LabelOf(tr, db, "PP", out xd);
                Check(pp != null && LabelsOf(tr, db, "PP") == 1, stage + ": the pole is labelled, once (" + LabelsOf(tr, db, "PP") + ")");
                if (pp != null)
                {
                    Check(Contents(pp).StartsWith("S-POLE 1234", StringComparison.Ordinal), stage + ": pole text has the family's prefix (" + Contents(pp) + ")");
                    Check(Near(Height(pp), smokeHeight), stage + ": pole text height " + Height(pp).ToString("0.###", C) + " = " + smokeHeight.ToString("0.###", C));
                    var colour = pp is AcDb.MText ? pp.Color.ColorIndex : ((AcDb.MLeader)pp).TextColor.ColorIndex;
                    Check(colour == smokeColour, stage + ": pole text colour " + colour + " = " + smokeColour);
                    Check(pp.Layer.Equals("FTF-SMOKE-UTIL", StringComparison.OrdinalIgnoreCase), stage + ": pole label on FTF-SMOKE-UTIL");
                    if (_moved)
                        Check(Where(pp).DistanceTo(_movedTo) < 1e-6, stage + ": the pole label moved by hand stayed where it was put (" + pp.GetType().Name +
                              ": placed " + Xy(_poleBefore) + ", moved to " + Xy(_movedTo) + ", now " + Xy(Where(pp)) + ")");
                }

                var mb = LabelOf(tr, db, "MB", out xd);
                Check(mb == null, stage + ": the mailbox (labelling turned off for MB) has no label");

                var sn = LabelOf(tr, db, "SN", out xd);
                Check(sn != null && LabelsOf(tr, db, "SN") == 1, stage + ": the sign is labelled, once");
                if (sn != null)
                {
                    Check(!Contents(sn).StartsWith("S-", StringComparison.Ordinal), stage + ": the sign (no assignment) has no prefix");
                    Check(Near(Height(sn), 0.08 * scale), stage + ": the sign uses Point Labels' height " + Height(sn).ToString("0.###", C));
                }
                tr.Commit();
            }
            var ok = _failures == failuresBefore;
            Stages.Add(stage + (ok ? "" : "(FAIL)"));
            Record("STAGE " + stage + (ok ? " PASS" : " FAIL"));
        }

        [CommandMethod("STYLEMOVE")]
        public void Move()
        {
            Guard("STYLEMOVE", delegate { MoveBody(); });
        }

        private static void MoveBody()
        {
            var db = Application.DocumentManager.MdiActiveDocument.Database;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                List<string> xd;
                var pp = LabelOf(tr, db, "PP", out xd);
                if (pp != null)
                {
                    _poleBefore = Where(pp);
                    pp.UpgradeOpen();
                    pp.TransformBy(Matrix3d.Displacement(new Vector3d(3, 2, 0)));
                    _movedTo = Where(pp);
                    _moved = true;
                }
                tr.Commit();
            }
            Check(_moved, "the pole's label was moved by hand (" + (_moved ? Xy(_poleBefore) + " -> " + Xy(_movedTo) : "no pole label") + ")");
        }

        [CommandMethod("STYLEEDIT")]
        public void Edit()
        {
            Guard("STYLEEDIT", delegate { EditBody(); });
        }

        private static void EditBody()
        {
            var db = Application.DocumentManager.MdiActiveDocument.Database;
            var path = Path.Combine(Path.GetDirectoryName(db.Filename), "ftf-settings.json");
            var json = File.ReadAllText(path);
            var edited = json.Replace("\"heightPlotted\": 0.12", "\"heightPlotted\": 0.15").Replace("\"color\": \"3\"", "\"color\": \"5\"");
            File.WriteAllText(path, edited);

            var plugin = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "FieldCodes.Cad");
            if (plugin == null) Fatal("FieldCodes.Cad is not loaded.");
            Call(plugin, "FieldCodes.Cad.FtfSession.InvalidateSettings()", null);
            Check(edited != json, "settings edited (Smoke text 0.15\", colour 5) and re-read");
        }

        [CommandMethod("STYLEEND")]
        public void End()
        {
            Guard("STYLEEND", delegate { EndBody(); });
        }

        private static void EndBody()
        {
            var verdict = _failures == 0 ? "PASS" : "FAIL";
            Ed.WriteMessage("\nFTF STYLE CHECK: {0} ({1} passed, {2} failed; stages {3})\n", verdict, _passes, _failures,
                            string.Join(", ", Stages.ToArray()));
            Record("RESULT " + verdict + " " + _passes + " " + _failures);
            Record("COMPLETE");
        }
    }
}
