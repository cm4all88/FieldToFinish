// Headless Civil 3D checks for point accountability and linework continuity, driven by
// deploy\smoke-test.ps1 as a BLOCKING stage: a failure makes UPDATEFTF put the previous build back.
//
// ACCSEED     a fresh drawing gets 37 synthetic COGO points (invented local coordinates) and the plain
//             polylines drawn through them (the TBC-export reality), plus a TIN surface:
//               A1  interrupted fence 101-103 / 105-107, "PP" 104 between, 105 re-begun   -> probable gap
//               A2  gate: fence ends at FGP posts 202 / 203, 6 ft apart                  -> intentional, no item
//               A4  FNC1 401-402 and FNC2 403-404, 3 ft apart                             -> never joined
//               A6  601 / 602 SSCO 0.004 apart                                            -> duplicates (a warning)
//               A7  701 "XYZQ 12" (no rule), 702 bare WFH with the template's marker     -> unknown; required symbol missing
//               A8  EP 801-805 with no line                                               -> missing segment
//               A9  GS 901-903 TIN vertices, GS 905 off the TIN                           -> surface data, no item
//               A10 FIP 5/8 1001 (control); ZK 1002 beside it, ZK 904 alone              -> control; control checks
//               A11 WFH 12 1101 with a generic marker                                     -> symbol missing, no label wanted
//               SG  1301 "HOUSE NUMBER 4735 SIGN"                                         -> address sign, its description kept
//               HY  WFH 14 1401 whose point style draws a hydrant block                  -> accounted for by its symbol
//               MV  FNC 1201-1203, the line skips 1202                                     -> missing vertex
// ACCVERIFY   first requires that the named command ran exactly once since the previous stage, then:
//               RUN       after FTFRUN: the checks ran after it; the expected items, ledger dispositions,
//                         previews on a non-plotted layer, reports beside the drawing (the control check
//                         report among them); no ground shot or control check shot is a required action
//               AGAIN     FTFRUN again: the same open items, the same FTF objects by kind (no duplicates)
//               STANDARD  ACCSTANDARD Label + FTFREVIEWCHECK: the drafting standard in the rules now requires
//                         a label on SSCO, so 601 / 602 each have a required missing-label item
//               RESTORED  ACCSTANDARD Restore + FTFREVIEWCHECK: those items resolved by themselves, their
//                         history kept, and the open point items exactly as after RUN
//               SURFACE   ACCSURFACE On + FTFREVIEWCHECK: surface membership verified for 901-903; 905 listed
//                         once, as information, under point.surfaceunverified:GS
//               SURFACEOFF ACCSURFACE Off + FTFREVIEWCHECK: that item resolved, 905 designated again
//               APPROVED  -FTFREVIEW Approve: one separate correction on the fence layer, item Corrected,
//                         its preview gone
//               KEPT      FTFREVIEWCHECK: the correction kept, still one, the item still Corrected
//               CHANGED   -FTFREVIEW NotApplicable on 702, then 702 moved: the decision reopened
//               REFUSED   FTFQC Complete with required items open: refused
//               QC        every open item closed with a reason, FTFQC Complete: marked
//               DELETED   point 1001 and the correction erased: point.deleted, QC mark taken off,
//                         the gap reopened ("correction erased")
//             and at every stage that no seeded line changed and no point changed except the ones the
//             harness itself moved or erased.
// ACCSTANDARD Label / Restore: adds a drafting standard (featureStandard) to the rules.json beside the
//             drawing, then puts the file back as it was. The plugin re-reads rules whose file changed.
// ACCSURFACE  On / Off: turns Settings > Point Accountability > verify surface membership on in the
//             ftf-settings.json beside the drawing, then puts the file back as it was.
// ACCMOVE     moves 702 one foot east (the harness, standing in for a surveyor's edit).
// ACCERASE    erases point 1001 and the approved correction.
// ACCEND      writes the verdict and COMPLETE.
//
// Result protocol as ContourTestHarness: lines appended to the file named by FTF_ACCOUNT_RESULT, each
// written through to the disk at once, with STEP, EXCEPTION and the plugin's TRACE lines beside them
// (as IdentityTestHarness). Compiled with the .NET Framework csc (C# 5 only). FTF types are read by
// reflection, by exact signature only (the plugin contract block below).
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
using CivApp = Autodesk.Civil.ApplicationServices;
using CivDb = Autodesk.Civil.DatabaseServices;

[assembly: CommandClass(typeof(FtfAccountTest.AccountTestCommands))]

namespace FtfAccountTest
{
    public class AccountTestCommands
    {
        private static readonly CultureInfo C = CultureInfo.InvariantCulture;
        private const string AppName = "FTF_FIELDTOFINISH";
        private const string Fence = "V-SURF-FENC-E";
        private const string GapId = "line.gap:FNC:103-105";
        private const string HydrantBlock = "FTF-SMOKE-WFH";
        // The drafting standard ACCSTANDARD adds, and the settings ACCSURFACE writes (the same text as
        // AccountabilityHarnessExpectationTests, which runs them through the library first).
        private const string CleanoutStandard = @"{ ""requirements"": [ { ""name"": ""Cleanout"", ""codes"": [""SSCO""], ""label"": ""Required"" } ] }";
        private const string SurfaceSettings = @"{ ""version"": ""1"", ""accountability"": { ""verifySurfaceMembership"": true } }";
        private static readonly string[] GroundAndCheckShots = { "901", "902", "903", "904", "905", "1002" };

        private static int _failures;
        private static int _passes;
        private static readonly List<string> Stages = new List<string>();
        private static readonly List<string> LineHandles = new List<string>();
        private static Assembly _plugin;

        // Everything of the plugin this harness calls, by exact signature; checked before anything is seeded.
        private const string ReadReviewSignature = "FieldCodes.Cad.DrawingStore.ReadReview(Autodesk.AutoCAD.DatabaseServices.Database, Autodesk.AutoCAD.DatabaseServices.Transaction)";
        private const string ReadLedgerSignature = "FieldCodes.Cad.DrawingStore.ReadLedger(Autodesk.AutoCAD.DatabaseServices.Database, Autodesk.AutoCAD.DatabaseServices.Transaction)";
        private const string FindPointSignature = "FieldCodes.Review.PointLedger.Find(System.String)";
        private const string DescribeRunSignature = "FieldCodes.Review.LedgerRun.Describe()";
        private const string StartedCount = "FieldCodes.Cad.FtfCommandTrace.StartedCount(System.String)";
        private const string FinishedCount = "FieldCodes.Cad.FtfCommandTrace.FinishedCount(System.String)";
        private static readonly string[] PluginContract = { ReadReviewSignature, ReadLedgerSignature, FindPointSignature, DescribeRunSignature, StartedCount, FinishedCount };
        private static short _correctionKind = -1;
        private static short _proposalKind = -1;
        private static short _labelKind = -1;
        private static string _pointSnapshot;
        private static string _lineSnapshot;
        private static List<string> _openAfterRun;
        private static Dictionary<short, int> _ownedAfterRun;
        private static string _correctionHandle;
        private static string _hydrantStyleProblem = "not set up";
        private static string _rulesOriginal;
        private static string _settingsOriginal;
        private static List<string> _openPointItemsAfterRun;
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

        private static void Note(Editor ed, string what)
        {
            ed.WriteMessage("\n  [NOTE] {0}", what);
            Record("NOTE " + what);
        }

        private static void Record(string line)
        {
            var file = Environment.GetEnvironmentVariable("FTF_ACCOUNT_RESULT");
            if (string.IsNullOrEmpty(file)) return;
            try { AppendDurably(file, line); }
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

        [CommandMethod("ACCSEED")]
        public void Seed()
        {
            Guard("ACCSEED", delegate { SeedBody(); });
        }

        private static void SeedBody()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;
            Record("BEGIN " + DateTime.UtcNow.ToString("o", C));

            var loaded = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => string.Equals(a.GetName().Name, "FieldCodes.Cad", StringComparison.OrdinalIgnoreCase)).ToList();
            if (loaded.Count != 1) Fatal("FieldCodes.Cad is loaded " + loaded.Count + " times; expected once.");
            _plugin = loaded[0];
            Step("ACCSEED: checking the plugin contract (" + PluginContract.Length + " signatures)");
            foreach (var signature in PluginContract) Contract(_plugin, signature);
            Step("ACCSEED: plugin contract resolved");
            var kinds = _plugin.GetType("FieldCodes.Cad.FtfEntityKind");
            if (kinds == null) Fatal("FieldCodes.Cad.FtfEntityKind not found.");
            try
            {
                _correctionKind = Convert.ToInt16(Enum.Parse(kinds, "LineCorrection"));
                _proposalKind = Convert.ToInt16(Enum.Parse(kinds, "LineGapProposal"));
                _labelKind = Convert.ToInt16(Enum.Parse(kinds, "Label"));
            }
            catch (System.Exception) { Fatal("the loaded build has no LineCorrection / LineGapProposal kinds: it predates point accountability."); }
            foreach (var name in new[] { "FTFRUN", "FTFREVIEWCHECK", "-FTFREVIEW", "FTFQC" })
            {
                SeenStarted[name] = Trace(StartedCount, name);
                SeenFinished[name] = Trace(FinishedCount, name);
            }

            var cdoc = CivApp.CivilDocument.GetCivilDocument(db);
            using (var tr = db.TransactionManager.StartTransaction())
            {
                Layer(db, tr, Fence);

                // A1 interrupted fence
                Point(tr, cdoc, 101, 0, 0, 100, "FNC B"); Point(tr, cdoc, 102, 20, 0, 100, "FNC"); Point(tr, cdoc, 103, 40, 0, 100, "FNC");
                Point(tr, cdoc, 104, 45, 30, 100, "PP");
                Point(tr, cdoc, 105, 48, 0, 100, "FNC B"); Point(tr, cdoc, 106, 68, 0, 100, "FNC"); Point(tr, cdoc, 107, 88, 0, 100, "FNC E");
                LineHandles.Add(Lw(db, tr, new[] { 0.0, 0, 20, 0, 40, 0 }));
                LineHandles.Add(Lw(db, tr, new[] { 48.0, 0, 68, 0, 88, 0 }));

                // A2 gate
                Point(tr, cdoc, 201, 0, 100, 100, "FNC B"); Point(tr, cdoc, 202, 20, 100, 100, "FGP FNC");
                Point(tr, cdoc, 203, 26, 100, 100, "FGP FNC B"); Point(tr, cdoc, 204, 46, 100, 100, "FNC E");
                LineHandles.Add(Lw(db, tr, new[] { 0.0, 100, 20, 100 }));
                LineHandles.Add(Lw(db, tr, new[] { 26.0, 100, 46, 100 }));

                // A4 parallel strings
                Point(tr, cdoc, 401, 0, 200, 100, "FNC1 B"); Point(tr, cdoc, 402, 20, 200, 100, "FNC1 E");
                Point(tr, cdoc, 403, 23, 200, 100, "FNC2 B"); Point(tr, cdoc, 404, 43, 200, 100, "FNC2 E");
                LineHandles.Add(Lw(db, tr, new[] { 0.0, 200, 20, 200 }));
                LineHandles.Add(Lw(db, tr, new[] { 23.0, 200, 43, 200 }));

                // A6 duplicates, A7 unknown and bare
                Point(tr, cdoc, 601, 0, 300, 100, "SSCO"); Point(tr, cdoc, 602, 0.004, 300, 100, "SSCO");
                Point(tr, cdoc, 701, 0, 400, 100, "XYZQ 12"); Point(tr, cdoc, 702, 30, 400, 100, "WFH");

                // A8 EP with no line
                for (var i = 0; i < 5; i++)
                    Point(tr, cdoc, (uint)(801 + i), i * 10.0, 500, 100, i == 0 ? "EP B" : i == 4 ? "EP E" : "EP");

                // A9 ground shots; A10 a control check shot with no control point near it
                Point(tr, cdoc, 901, 0, 600, 100, "GS"); Point(tr, cdoc, 902, 10, 600, 101, "GS"); Point(tr, cdoc, 903, 0, 610, 102, "GS");
                Point(tr, cdoc, 904, 30, 600, 100, "ZK"); Point(tr, cdoc, 905, 100, 650, 100, "GS");

                // A10 control and its check shot, A11 a hydrant with the template's generic marker
                Point(tr, cdoc, 1001, 0, 700, 100, "FIP 5/8");
                Point(tr, cdoc, 1002, 0.03, 700.04, 100.02, "ZK");
                Point(tr, cdoc, 1101, 0, 800, 100, "WFH 12");

                // SG a homeowner's house number sign; HY a hydrant whose point style draws its symbol
                Point(tr, cdoc, 1301, 0, 1000, 100, "HOUSE NUMBER 4735 SIGN");
                var hydrant = Point(tr, cdoc, 1401, 0, 1100, 100, "WFH 14");
                HydrantStyle(db, tr, cdoc, hydrant);

                // MV the line skips 1202
                Point(tr, cdoc, 1201, 0, 900, 100, "FNC B"); Point(tr, cdoc, 1202, 10, 903, 100, "FNC"); Point(tr, cdoc, 1203, 20, 900, 100, "FNC E");
                LineHandles.Add(Lw(db, tr, new[] { 0.0, 900, 20, 900 }));

                var surfaceId = CivDb.TinSurface.Create(db, "FTF-SMOKE-GS");
                var surface = (CivDb.TinSurface)tr.GetObject(surfaceId, AcDb.OpenMode.ForWrite);
                var vertices = new Point3dCollection();
                vertices.Add(new Point3d(0, 600, 100));
                vertices.Add(new Point3d(10, 600, 101));
                vertices.Add(new Point3d(0, 610, 102));
                vertices.Add(new Point3d(10, 610, 103));
                surface.AddVertices(vertices);
                surface.Rebuild();
                tr.Commit();
            }
            _pointSnapshot = PointSnapshot(db);
            _lineSnapshot = LineSnapshot(db);
            Check(ed, CountPoints(db) == 37, "37 synthetic COGO points seeded (" + CountPoints(db) + ")");
            Record("SEEDED");
        }

        // ================================================================ verify

        [CommandMethod("ACCVERIFY")]
        public void Verify()
        {
            Guard("ACCVERIFY", delegate { VerifyBody(); });
        }

        private static void VerifyBody()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;
            var stage = ed.GetString("\nStage: ").StringResult.Trim().ToUpperInvariant();
            Stages.Add(stage);
            Step("ACCVERIFY " + stage + ": begin");
            var before = _failures;

            if (stage == "RUN") { RequireRanOnce("FTFRUN"); RequireRanOnce("FTFREVIEWCHECK"); AfterRun(db, ed); }
            else if (stage == "AGAIN") { RequireRanOnce("FTFRUN"); RequireRanOnce("FTFREVIEWCHECK"); Again(db, ed); }
            else if (stage == "STANDARD") { RequireRanOnce("FTFREVIEWCHECK"); WithStandard(db, ed); }
            else if (stage == "RESTORED") { RequireRanOnce("FTFREVIEWCHECK"); Restored(db, ed); }
            else if (stage == "SURFACE") { RequireRanOnce("FTFREVIEWCHECK"); SurfaceOn(db, ed); }
            else if (stage == "SURFACEOFF") { RequireRanOnce("FTFREVIEWCHECK"); SurfaceOff(db, ed); }
            else if (stage == "APPROVED") { RequireRanOnce("-FTFREVIEW"); Approved(db, ed); }
            else if (stage == "KEPT") { RequireRanOnce("FTFREVIEWCHECK"); Kept(db, ed); }
            else if (stage == "CHANGED") { RequireRanOnce("-FTFREVIEW"); RequireRanOnce("FTFREVIEWCHECK"); Changed(db, ed); }
            else if (stage == "REFUSED") { RequireRanOnce("FTFQC"); Refused(db, ed); }
            else if (stage == "QC") { RequireRanOnce("-FTFREVIEW"); RequireRanOnce("FTFQC"); Qc(db, ed); }
            else if (stage == "DELETED") { RequireRanOnce("FTFREVIEWCHECK"); Deleted(db, ed); }
            else Fatal("unknown stage " + stage);

            Check(ed, LineSnapshot(db) == _lineSnapshot, stage + ": every seeded survey line unchanged");
            Check(ed, PointSnapshot(db) == _pointSnapshot, stage + ": no COGO point changed (number, raw description, XYZ) except by the harness");
            Step("ACCVERIFY " + stage + ": end");
            Record("STAGE " + stage + " " + (_failures == before ? "PASS" : "FAIL"));
        }

        private static void AfterRun(AcDb.Database db, Editor ed)
        {
            var register = Register(db);
            var open = Items(register).Where(i => S(i, "Status") == "Open").ToList();
            Func<string, object> item = id => Items(register).FirstOrDefault(i => S(i, "Id") == id);

            var gap = item(GapId);
            Check(ed, gap != null && S(gap, "Status") == "Open" && S(gap, "Confidence") == "probable" && (bool)P(gap, "Required") && Count(P(gap, "Geometry")) == 2,
                  "A1: the interrupted fence is a probable, required gap with a proposed connector (" + Describe(gap) + ")");
            Check(ed, !Items(register).Any(i => S(i, "Id").StartsWith("line.gap:FNC:202") || S(i, "Id").StartsWith("line.gap:FNC:20")),
                  "A2: the gate opening is not a review item");
            Check(ed, !Items(register).Any(i => S(i, "Id").Contains("FNC1") || S(i, "Id").Contains("FNC2")),
                  "A4: parallel strings FNC1 / FNC2 are never joined");
            var dup = item("point.duplicate:601");
            Check(ed, dup != null && item("point.duplicate:602") != null && !(bool)P(dup, "Required"),
                  "A6: both duplicates are listed, as a warning rather than a required action (" + Describe(dup) + ")");
            Check(ed, item("code.unknown:XYZQ") != null, "A7: the code nothing handles is a review item");
            var bare = item("point.nosymbol:702");
            Check(ed, bare != null && (bool)P(bare, "Required"), "A7: a bare hydrant with the template's generic marker is missing its required symbol (" + Describe(bare) + ")");
            Check(ed, item("line.missingsegment:EP:801-805") != null, "A8: the coded EP string with no line is a missing segment");
            Check(ed, item("line.missingvertex:FNC:1201-1202-1203") != null, "MV: the line that skips 1202 is a missing vertex");
            var shotItems = Items(register).Where(i => GroundAndCheckShots.Any(n => Names(i, n))).ToList();
            Check(ed, !shotItems.Any(i => (bool)P(i, "Required")),
                  "A9/A10: no ground shot or control check shot is a required action (" + string.Join(", ", shotItems.Select(i => S(i, "Id")).ToArray()) + ")");
            Check(ed, !Items(register).Any(i => Names(i, "905") || Names(i, "904") || Names(i, "1002")),
                  "A9/A10: a ground shot off the TIN and the control check shots are not on the list (surface verification is off)");

            var ledger = Ledger(db);
            Check(ed, ledger != null && Count(P(ledger, "Points")) == 37, "the point ledger holds every point (" + (ledger == null ? 0 : Count(P(ledger, "Points"))) + ")");
            foreach (var n in new[] { "901", "902", "903", "905" })
                Check(ed, Disposition(ledger, n) == "Surface" && S(Point(ledger, n), "SurfaceStatus") == "designated" && S(Point(ledger, n), "Classification") == "Surface only",
                      "A9: GS " + n + " is surface data, surface only, with or without a surface (" + Disposition(ledger, n) + ", " + S(Point(ledger, n), "SurfaceStatus") + ")");
            var evidence = string.Join("; ", Strings(P(Point(ledger, "901"), "Evidence")).ToArray());
            Note(ed, "surface evidence for 901: " + evidence);
            Check(ed, Disposition(ledger, "1001") == "Control", "A10: FIP 5/8 is a control point (" + Disposition(ledger, "1001") + ")");
            foreach (var n in new[] { "904", "1002" })
                Check(ed, Disposition(ledger, n) == "Control" && S(Point(ledger, n), "Reading") == "control check ZK" && S(Point(ledger, n), "Classification") == "Control/Check",
                      "A10: ZK " + n + " is a control check shot (" + Disposition(ledger, n) + ", " + S(Point(ledger, n), "Reading") + ")");
            Check(ed, Disposition(ledger, "601") == "PointStyle", "A6: a duplicate's point style still accounts for it (" + Disposition(ledger, "601") + ")");
            Check(ed, Disposition(ledger, "103") == "Linework" && Disposition(ledger, "105") == "Linework", "the fence shots are on their lines");
            Check(ed, Disposition(ledger, "803") == "Review" && Strings(P(Point(ledger, "803"), "Issues")).Contains("line.missingsegment:EP:801-805"),
                  "A8: an EP shot with no line is for review, held by the missing-segment item");

            // A11: the hydrant's standard asks for its symbol and no text label.
            var hydrantSymbol = item("point.nosymbol:1101");
            Check(ed, hydrantSymbol != null && (bool)P(hydrantSymbol, "Required"), "A11: a hydrant with a generic marker is missing its required symbol (" + Describe(hydrantSymbol) + ")");
            Check(ed, item("point.missingoutput:1101") == null, "A11: no missing label for the hydrant: its standard wants none");
            Check(ed, !Owned(db).Any(o => o.Value == _labelKind && (Tag(o.Key, 2) == "1101" || Tag(o.Key, 2) == "1401" || Tag(o.Key, 2) == "702")),
                  "A11: FTFRUN drew no text label for a hydrant");

            // HY: a hydrant whose point style draws its symbol is accounted for by it.
            if (_hydrantStyleProblem == null)
                Check(ed, Disposition(ledger, "1401") == "Symbol" && !Items(register).Any(i => Names(i, "1401")),
                      "HY: a hydrant whose point style draws its symbol is accounted for, with nothing to review (" + Disposition(ledger, "1401") + "; " +
                      string.Join("; ", Strings(P(Point(ledger, "1401"), "Evidence")).ToArray()) + ")");
            else Note(ed, "HY: NOT VERIFIED in Civil 3D -- the harness could not set up 1401's symbol point style (" + _hydrantStyleProblem + ")");

            // SG: the house number sign keeps its description, is an address sign, not a code nothing handles.
            var sign = Point(ledger, "1301");
            Check(ed, sign != null && S(sign, "RawDescription") == "HOUSE NUMBER 4735 SIGN" && S(sign, "Classification") == "Sign (address / house number)",
                  "SG: 1301 keeps its description and is an address / house number sign (" + S(sign, "Classification") + ")");
            Check(ed, item("code.unknown:HOUSE") == null, "SG: HOUSE is not a code nothing handles: the standard names the sign by its words");
            var signLabel = item("point.missingoutput:1301");
            if (Disposition(ledger, "1301") == "PointStyle" && Strings(P(sign, "Found")).Contains("label (point label)"))
            {
                Check(ed, signLabel == null, "SG: its own Civil 3D point label shows it; no separate label is required");
                Note(ed, "SG: 1301's point label is shown: " + string.Join("; ", Strings(P(sign, "Evidence")).ToArray()));
            }
            else
            {
                Check(ed, signLabel != null && S(signLabel, "Reason").Contains("label is hidden"),
                      "SG: with its point label hidden, the missing label is a required item that says so (" + Describe(signLabel) + ": " + S(signLabel, "Reason") + ")");
                Note(ed, "SG: the template's point label for 1301 is hidden, so its description shows nowhere on the plan");
            }

            var run = P(ledger, "LastRun");
            Check(ed, run != null && (int)P(run, "Points") == 37 && (int)P(run, "Review") > 0 && (int)P(run, "Accounted") > 0,
                  "the run's numbers: " + (run == null ? "none" : Convert.ToString(Call(_plugin, DescribeRunSignature, run))));

            var proposals = Owned(db).Where(o => o.Value == _proposalKind).ToList();
            var withGeometry = open.Count(i => S(i, "Source") == "Linework continuity" && Count(P(i, "Geometry")) >= 2);
            Check(ed, proposals.Count == 2 * withGeometry && withGeometry >= 3, "a preview (line and note) for each open proposal (" + proposals.Count + " for " + withGeometry + ")");
            Check(ed, LayerPlots(db, "FTF-QC-CONTINUITY") == false, "the previews are on a non-plotted layer");
            Check(ed, !Owned(db).Any(o => o.Value == _correctionKind), "nothing is corrected before a person approves it");

            var dir = Path.GetDirectoryName(db.Filename);
            var name = Path.GetFileNameWithoutExtension(db.Filename);
            Check(ed, File.Exists(Path.Combine(dir, name + ".ftf-points.csv")) && File.Exists(Path.Combine(dir, name + ".ftf-continuity.csv")),
                  "the point ledger and continuity reports are written beside the drawing");
            var checks = Path.Combine(dir, name + ".ftf-control-checks.csv");
            var rows = File.Exists(checks) ? File.ReadAllLines(checks).ToList() : new List<string>();
            Check(ed, rows.Any(r => r.StartsWith("1002,ZK,", StringComparison.Ordinal) && r.Contains(",1001,FIP 5/8,")),
                  "A10: the control check report pairs check shot 1002 with control point 1001 (" + (rows.Count == 0 ? "no report" : rows.Count + " rows") + ")");
            Check(ed, rows.Any(r => r.StartsWith("904,ZK,", StringComparison.Ordinal) && r.Contains("no control point within")),
                  "A10: and says check shot 904 has no control point near it");

            _openAfterRun = open.Select(i => S(i, "Id")).OrderBy(x => x, StringComparer.Ordinal).ToList();
            _openPointItemsAfterRun = OpenPointItems(register);
            _ownedAfterRun = OwnedByKind(db);
        }

        private static void Again(AcDb.Database db, Editor ed)
        {
            var open = Items(Register(db)).Where(i => S(i, "Status") == "Open").Select(i => S(i, "Id")).OrderBy(x => x, StringComparer.Ordinal).ToList();
            Check(ed, string.Join("|", open.ToArray()) == string.Join("|", _openAfterRun.ToArray()), "the same open items after a second FTFRUN (" + open.Count + ")");
            var owned = OwnedByKind(db);
            var same = owned.Count == _ownedAfterRun.Count && owned.All(p => _ownedAfterRun.ContainsKey(p.Key) && _ownedAfterRun[p.Key] == p.Value);
            Check(ed, same, "the same FTF objects by kind: nothing duplicated (" + string.Join(", ", owned.Select(p => p.Key + "x" + p.Value).ToArray()) + ")");
        }

        private static void WithStandard(AcDb.Database db, Editor ed)
        {
            var register = Register(db);
            var ledger = Ledger(db);
            foreach (var n in new[] { "601", "602" })
            {
                var missing = Items(register).FirstOrDefault(i => S(i, "Id") == "point.missingoutput:" + n);
                Check(ed, missing != null && S(missing, "Status") == "Open" && (bool)P(missing, "Required") &&
                          S(missing, "Reason").Contains("the standard requires a label") && S(missing, "Reason").Contains("Cleanout (SSCO)"),
                      "STANDARD: the drafting standard now requires a label on SSCO " + n + ": a required item (" + Describe(missing) + ")");
                Check(ed, Disposition(ledger, n) == "Review", "STANDARD: " + n + " is for review until it is labelled (" + Disposition(ledger, n) + ")");
            }
        }

        private static void Restored(AcDb.Database db, Editor ed)
        {
            var register = Register(db);
            var ledger = Ledger(db);
            foreach (var n in new[] { "601", "602" })
            {
                var missing = Items(register).FirstOrDefault(i => S(i, "Id") == "point.missingoutput:" + n);
                var history = missing == null ? new List<string>() : Objects(P(missing, "History")).Select(h => S(h, "Action")).ToList();
                Check(ed, missing != null && S(missing, "Status") == "Resolved" && history.Contains("opened") && history.Contains("resolved"),
                      "RESTORED: with the standard back, " + n + "'s item resolved by itself, its history kept (" + Describe(missing) + ": " + string.Join(", ", history.ToArray()) + ")");
                Check(ed, Disposition(ledger, n) == "PointStyle", "RESTORED: " + n + " is accounted for by its point style again (" + Disposition(ledger, n) + ")");
            }
            var now = OpenPointItems(register);
            Check(ed, string.Join("|", now.ToArray()) == string.Join("|", _openPointItemsAfterRun.ToArray()),
                  "RESTORED: the open point items are exactly those after RUN (" + now.Count + " now, " + _openPointItemsAfterRun.Count + " then)");
            var ids = Items(register).Select(i => S(i, "Id")).ToList();
            Check(ed, ids.Count == ids.Distinct().Count(), "RESTORED: one item per point and kind, however many checks ran");
        }

        private static void SurfaceOn(AcDb.Database db, Editor ed)
        {
            var register = Register(db);
            var ledger = Ledger(db);
            foreach (var n in new[] { "901", "902", "903" })
                Check(ed, Disposition(ledger, n) == "Surface" && S(Point(ledger, n), "SurfaceStatus") == "verified",
                      "SURFACE: GS " + n + " is verified in the TIN (" + S(Point(ledger, n), "SurfaceStatus") + "; " +
                      string.Join("; ", Strings(P(Point(ledger, n), "Evidence")).Where(e => e.StartsWith("surface ")).ToArray()) + ")");
            Check(ed, Disposition(ledger, "905") == "Surface" && S(Point(ledger, "905"), "SurfaceStatus") == "not verifiable",
                  "SURFACE: GS 905 off the TIN is still surface data, not verifiable (" + S(Point(ledger, "905"), "SurfaceStatus") + ")");
            var unverified = Items(register).FirstOrDefault(i => S(i, "Id") == "point.surfaceunverified:GS");
            Check(ed, unverified != null && S(unverified, "Status") == "Open" && !(bool)P(unverified, "Required") && S(unverified, "Severity") == "Info" && S(unverified, "Points") == "905",
                  "SURFACE: 905 is listed once, as information, not a required action (" + Describe(unverified) + ", " + S(unverified, "Severity") + ", points " + S(unverified, "Points") + ")");
            Check(ed, !Items(register).Any(i => S(i, "Status") == "Open" && (bool)P(i, "Required") && GroundAndCheckShots.Any(n => Names(i, n))),
                  "SURFACE: still no required action for a ground shot or a control check shot");
        }

        private static void SurfaceOff(AcDb.Database db, Editor ed)
        {
            var register = Register(db);
            var unverified = Items(register).FirstOrDefault(i => S(i, "Id") == "point.surfaceunverified:GS");
            Check(ed, unverified != null && S(unverified, "Status") == "Resolved", "SURFACEOFF: the surface note resolved when the check was turned off (" + Describe(unverified) + ")");
            Check(ed, S(Point(Ledger(db), "905"), "SurfaceStatus") == "designated", "SURFACEOFF: 905 is designated surface data again");
        }

        private static void Approved(AcDb.Database db, Editor ed)
        {
            var gap = Items(Register(db)).FirstOrDefault(i => S(i, "Id") == GapId);
            Check(ed, gap != null && S(gap, "Status") == "Corrected", "the approved gap is Corrected (" + Describe(gap) + ")");
            var corrections = Owned(db).Where(o => o.Value == _correctionKind).ToList();
            Check(ed, corrections.Count == 1, "exactly one correction drawn (" + corrections.Count + ")");
            if (corrections.Count != 1) return;
            _correctionHandle = corrections[0].Key.Handle.ToString();
            Check(ed, gap != null && S(gap, "CorrectionHandle") == _correctionHandle, "the item names the correction it drew");
            Check(ed, Tag(corrections[0].Key, 3) == "linefix:" + GapId, "the correction is traceable to its item (" + Tag(corrections[0].Key, 3) + ")");
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var c = (AcDb.Curve)tr.GetObject(corrections[0].Key, AcDb.OpenMode.ForRead);
                Check(ed, c.Layer == Fence, "the correction is on the fence's layer (" + c.Layer + ")");
                Check(ed, c.StartPoint.DistanceTo(new Point3d(40, 0, c.StartPoint.Z)) < 1e-6 && c.EndPoint.DistanceTo(new Point3d(48, 0, c.EndPoint.Z)) < 1e-6,
                      "the correction joins 103 to 105");
                tr.Commit();
            }
            Check(ed, !Owned(db).Any(o => o.Value == _proposalKind && Tag(o.Key, 3) == GapId), "its preview is gone");
        }

        private static void Kept(AcDb.Database db, Editor ed)
        {
            var corrections = Owned(db).Where(o => o.Value == _correctionKind).ToList();
            Check(ed, corrections.Count == 1 && corrections[0].Key.Handle.ToString() == _correctionHandle, "a recheck keeps the one correction (" + corrections.Count + ")");
            var gap = Items(Register(db)).FirstOrDefault(i => S(i, "Id") == GapId);
            Check(ed, gap != null && S(gap, "Status") == "Corrected", "the gap stays Corrected while its source is unchanged");
        }

        private static void Changed(AcDb.Database db, Editor ed)
        {
            var register = Register(db);
            var nosymbol = Items(register).FirstOrDefault(i => S(i, "Id") == "point.nosymbol:702");
            Check(ed, nosymbol != null && S(nosymbol, "Status") == "Open" && S(nosymbol, "Note").Contains("source changed"),
                  "a decision on a point that then moved is reopened (" + Describe(nosymbol) + ")");
            var history = nosymbol == null ? new List<string>() : Objects(P(nosymbol, "History")).Select(h => S(h, "Action")).ToList();
            Check(ed, history.Contains("not applicable") && history.Contains("source changed"), "its history keeps the decision and the change (" + string.Join(", ", history.ToArray()) + ")");
            Check(ed, Items(register).Any(i => S(i, "Id") == "point.sourcechanged:702"), "the change itself is recorded");
        }

        private static void Refused(AcDb.Database db, Editor ed)
        {
            var register = Register(db);
            Check(ed, P(register, "QcCompleteAt") == null, "Drafting QC Complete is refused while required items are open");
        }

        private static void Qc(AcDb.Database db, Editor ed)
        {
            var register = Register(db);
            var requiredOpen = Count(P(register, "RequiredOpen"));
            Check(ed, requiredOpen == 0, "every required item closed with a reason (" + requiredOpen + " open)");
            Check(ed, P(register, "QcCompleteAt") != null, "Drafting QC Complete is marked once they are");
        }

        private static void Deleted(AcDb.Database db, Editor ed)
        {
            var register = Register(db);
            var gone = Items(register).FirstOrDefault(i => S(i, "Id") == "point.deleted:1001");
            Check(ed, gone != null && S(gone, "Status") == "Open", "an erased point is a review item (" + Describe(gone) + ")");
            var entry = Point(Ledger(db), "1001");
            Check(ed, entry != null && (bool)P(entry, "Deleted") && S(entry, "RawDescription") == "FIP 5/8", "the ledger keeps the erased point with its description");
            Check(ed, P(register, "QcCompleteAt") == null, "the new required item takes Drafting QC Complete off");
            var gap = Items(register).FirstOrDefault(i => S(i, "Id") == GapId);
            Check(ed, gap != null && S(gap, "Status") == "Open" && S(gap, "Note").Contains("erased"), "an erased correction puts its item back on the list (" + Describe(gap) + ")");
        }

        // ================================================================ edits the harness makes

        [CommandMethod("ACCSTANDARD")]
        public void Standard()
        {
            Guard("ACCSTANDARD", delegate { StandardBody(); });
        }

        private static void StandardBody()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var what = doc.Editor.GetString("\nLabel or Restore: ").StringResult.Trim().ToUpperInvariant();
            var path = Path.Combine(Path.GetDirectoryName(doc.Database.Filename), "rules.json");
            if (!File.Exists(path)) Fatal("ACCSTANDARD: no rules.json beside the drawing (" + path + ")");
            if (what == "LABEL")
            {
                _rulesOriginal = File.ReadAllText(path);
                if (_rulesOriginal.Contains("\"featureStandard\"")) Fatal("ACCSTANDARD: the rules beside the drawing already have a featureStandard; the stage needs the factory rules");
                var text = _rulesOriginal.TrimEnd();
                if (!text.EndsWith("}", StringComparison.Ordinal)) Fatal("ACCSTANDARD: rules.json does not end with }");
                Rewrite(path, text.Substring(0, text.Length - 1).TrimEnd() + ",\r\n  \"featureStandard\": " + CleanoutStandard + "\r\n}\r\n");
                Record("STANDARD added to the rules beside the drawing: " + CleanoutStandard);
            }
            else if (what == "RESTORE")
            {
                if (_rulesOriginal == null) Fatal("ACCSTANDARD Restore before Label");
                Rewrite(path, _rulesOriginal);
                Record("STANDARD the rules beside the drawing are the factory rules again");
            }
            else Fatal("ACCSTANDARD: expected Label or Restore, not " + what);
        }

        [CommandMethod("ACCSURFACE")]
        public void Surface()
        {
            Guard("ACCSURFACE", delegate { SurfaceBody(); });
        }

        private static void SurfaceBody()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var what = doc.Editor.GetString("\nOn or Off: ").StringResult.Trim().ToUpperInvariant();
            var path = Path.Combine(Path.GetDirectoryName(doc.Database.Filename), "ftf-settings.json");
            if (what == "ON")
            {
                _settingsOriginal = File.Exists(path) ? File.ReadAllText(path) : null;
                Rewrite(path, SurfaceSettings);
                Record("SURFACE settings beside the drawing: " + SurfaceSettings);
            }
            else if (what == "OFF")
            {
                if (_settingsOriginal == null) { if (File.Exists(path)) File.Delete(path); }
                else Rewrite(path, _settingsOriginal);
                Record("SURFACE settings beside the drawing put back");
            }
            else Fatal("ACCSURFACE: expected On or Off, not " + what);
        }

        /// <summary>Writes the file and moves its time on at least two seconds: the plugin re-reads a
        /// rules or settings file whose time changed, whatever the file system's clock resolution.</summary>
        private static void Rewrite(string path, string text)
        {
            var before = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
            File.WriteAllText(path, text, new UTF8Encoding(false));
            var now = DateTime.UtcNow;
            File.SetLastWriteTimeUtc(path, before != DateTime.MinValue && now < before.AddSeconds(2) ? before.AddSeconds(2) : now);
        }

        [CommandMethod("ACCMOVE")]
        public void Move()
        {
            Guard("ACCMOVE", delegate { MoveBody(); });
        }

        private static void MoveBody()
        {
            var db = Application.DocumentManager.MdiActiveDocument.Database;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var p = FindPoint(db, tr, 702);
                if (p == null) Fatal("point 702 is gone.");
                p.UpgradeOpen();
                // CogoPoint.Location is read-only in Civil 3D; a point moves by its easting.
                p.Easting = p.Easting + 1;
                tr.Commit();
            }
            _pointSnapshot = PointSnapshot(db);
            Record("MOVED 702");
        }

        [CommandMethod("ACCERASE")]
        public void Erase()
        {
            Guard("ACCERASE", delegate { EraseBody(); });
        }

        private static void EraseBody()
        {
            var db = Application.DocumentManager.MdiActiveDocument.Database;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var p = FindPoint(db, tr, 1001);
                if (p == null) Fatal("point 1001 is gone.");
                p.UpgradeOpen();
                p.Erase();
                foreach (var o in Owned(db).Where(x => x.Value == _correctionKind))
                    tr.GetObject(o.Key, AcDb.OpenMode.ForWrite).Erase();
                tr.Commit();
            }
            _pointSnapshot = PointSnapshot(db);
            Record("ERASED 1001 and the correction");
        }

        [CommandMethod("ACCEND")]
        public void End()
        {
            Guard("ACCEND", delegate { EndBody(); });
        }

        private static void EndBody()
        {
            var verdict = _failures == 0 ? "PASS" : "FAIL";
            Ed.WriteMessage("\nFTF ACCOUNTABILITY: {0} ({1} passed, {2} failed)\n", verdict, _passes, _failures);
            Record("STAGES " + string.Join(",", Stages.ToArray()));
            Record("RESULT " + verdict + " " + _passes + " " + _failures);
            Record("COMPLETE");
        }

        // ================================================================ reading FTF's state

        private static object Register(AcDb.Database db)
        {
            return Store(db, ReadReviewSignature);
        }

        private static object Ledger(AcDb.Database db)
        {
            return Store(db, ReadLedgerSignature);
        }

        private static object Store(AcDb.Database db, string signature)
        {
            Step("read the drawing's store: " + signature.Substring(signature.LastIndexOf('.', signature.IndexOf('(')) + 1));
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var result = Call(_plugin, signature, null, db, tr);
                tr.Commit();
                return result;
            }
        }

        private static IEnumerable<object> Items(object register)
        {
            return register == null ? new List<object>() : Objects(P(register, "Items"));
        }

        private static object Point(object ledger, string number)
        {
            if (ledger == null) return null;
            return Call(_plugin, FindPointSignature, ledger, number);
        }

        private static string Disposition(object ledger, string number)
        {
            var p = Point(ledger, number);
            return p == null ? "(not in the ledger)" : S(p, "Disposition");
        }

        /// <summary>True when the item is about the point: its id ends with the number, or it lists it.</summary>
        private static bool Names(object item, string number)
        {
            return S(item, "Id").EndsWith(":" + number, StringComparison.Ordinal) ||
                   S(item, "Points").Split(',').Select(x => x.Trim()).Contains(number);
        }

        private static List<string> OpenPointItems(object register)
        {
            return Items(register).Where(i => S(i, "Status") == "Open" && S(i, "Source") == "Points").Select(i => S(i, "Id"))
                                  .OrderBy(x => x, StringComparer.Ordinal).ToList();
        }

        private static string Describe(object item)
        {
            if (item == null) return "not found";
            return S(item, "Id") + " " + S(item, "Status") + (string.IsNullOrEmpty(S(item, "Note")) ? string.Empty : " -- " + S(item, "Note"));
        }

        private static object P(object o, string name)
        {
            if (o == null) return null;
            var p = o.GetType().GetProperty(name);
            return p == null ? null : p.GetValue(o, null);
        }

        private static string S(object o, string name)
        {
            var v = P(o, name);
            return v == null ? string.Empty : Convert.ToString(v, C);
        }

        private static IEnumerable<object> Objects(object list)
        {
            var e = list as IEnumerable;
            return e == null ? new List<object>() : e.Cast<object>().ToList();
        }

        private static List<string> Strings(object list)
        {
            return Objects(list).Select(x => Convert.ToString(x, C)).ToList();
        }

        private static int Count(object list)
        {
            return Objects(list).Count();
        }

        // ================================================================ helpers

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

        private static int Trace(string signature, string command)
        {
            return (int)Call(_plugin, signature, null, command);
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

        private static bool? LayerPlots(AcDb.Database db, string name)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var lt = (AcDb.LayerTable)tr.GetObject(db.LayerTableId, AcDb.OpenMode.ForRead);
                bool? plots = null;
                if (lt.Has(name)) plots = ((AcDb.LayerTableRecord)tr.GetObject(lt[name], AcDb.OpenMode.ForRead)).IsPlottable;
                tr.Commit();
                return plots;
            }
        }

        private static AcDb.ObjectId Point(AcDb.Transaction tr, CivApp.CivilDocument cdoc, uint number, double x, double y, double z, string description)
        {
            var id = cdoc.CogoPoints.Add(new Point3d(x, y, z), description, false);
            var cp = (CivDb.CogoPoint)tr.GetObject(id, AcDb.OpenMode.ForWrite);
            try { cp.PointNumber = number; }
            catch (System.Exception ex) { Fatal("could not set point number " + number + ": " + ex.Message); }
            return id;
        }

        /// <summary>
        /// HY: a block FTF-SMOKE-WFH, a point style of that name whose marker is the block
        /// (MarkerType UseSymbolForMarker, MarkerSymbolName), and 1401 given that style -- a hydrant
        /// drafted the way the office template does it. Read and set by reflection, so a Civil 3D
        /// that names them differently is reported (NOTE, and 1401's check is not counted as passed)
        /// rather than a harness that does not compile.
        /// </summary>
        private static void HydrantStyle(AcDb.Database db, AcDb.Transaction tr, CivApp.CivilDocument cdoc, AcDb.ObjectId pointId)
        {
            Step("ACCSEED: point style " + HydrantBlock + " for 1401");
            try
            {
                var bt = (AcDb.BlockTable)tr.GetObject(db.BlockTableId, AcDb.OpenMode.ForRead);
                if (!bt.Has(HydrantBlock))
                {
                    bt.UpgradeOpen();
                    var btr = new AcDb.BlockTableRecord { Name = HydrantBlock };
                    bt.Add(btr);
                    tr.AddNewlyCreatedDBObject(btr, true);
                    var circle = new AcDb.Circle(Point3d.Origin, Vector3d.ZAxis, 0.5);
                    btr.AppendEntity(circle);
                    tr.AddNewlyCreatedDBObject(circle, true);
                }
                var pointStyles = P(P(cdoc, "Styles"), "PointStyles");
                if (pointStyles == null) throw new InvalidOperationException("CivilDocument.Styles.PointStyles is not there");
                var add = pointStyles.GetType().GetMethods().FirstOrDefault(m => m.Name == "Add" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(string));
                if (add == null) throw new InvalidOperationException("PointStyles has no Add(string)");
                var styleId = (AcDb.ObjectId)Invoke(add, pointStyles, HydrantBlock);
                var style = tr.GetObject(styleId, AcDb.OpenMode.ForWrite);
                var markerType = Writable(style, "MarkerType");
                markerType.SetValue(style, Enum.Parse(markerType.PropertyType, "UseSymbolForMarker"), null);
                Writable(style, "MarkerSymbolName").SetValue(style, HydrantBlock, null);
                var point = tr.GetObject(pointId, AcDb.OpenMode.ForWrite);
                Writable(point, "StyleId").SetValue(point, styleId, null);
                _hydrantStyleProblem = null;
                Note(Ed, "HY: 1401's point style " + HydrantBlock + " draws block " + HydrantBlock + " as its marker (" + S(style, "MarkerType") + ")");
            }
            catch (System.Exception ex)
            {
                var inner = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
                _hydrantStyleProblem = inner.GetType().Name + ": " + inner.Message;
                Note(Ed, "HY: could not give 1401 a symbol point style, so its check is not run: " + _hydrantStyleProblem);
            }
        }

        private static object Invoke(MethodInfo m, object target, params object[] args)
        {
            try { return m.Invoke(target, args); }
            catch (TargetInvocationException ex)
            {
                if (ex.InnerException != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }
        }

        private static PropertyInfo Writable(object o, string name)
        {
            var p = o.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance).FirstOrDefault(x => x.Name == name && x.CanWrite);
            if (p == null) throw new InvalidOperationException(o.GetType().FullName + " has no settable " + name);
            return p;
        }

        private static CivDb.CogoPoint FindPoint(AcDb.Database db, AcDb.Transaction tr, uint number)
        {
            var ms = (AcDb.BlockTableRecord)tr.GetObject(AcDb.SymbolUtilityServices.GetBlockModelSpaceId(db), AcDb.OpenMode.ForRead);
            foreach (AcDb.ObjectId id in ms)
            {
                var p = tr.GetObject(id, AcDb.OpenMode.ForRead) as CivDb.CogoPoint;
                if (p != null && p.PointNumber == number) return p;
            }
            return null;
        }

        private static string Lw(AcDb.Database db, AcDb.Transaction tr, double[] xy)
        {
            var pl = new AcDb.Polyline();
            for (var i = 0; i < xy.Length; i += 2) pl.AddVertexAt(i / 2, new Point2d(xy[i], xy[i + 1]), 0, 0, 0);
            pl.Layer = Fence;
            var ms = (AcDb.BlockTableRecord)tr.GetObject(AcDb.SymbolUtilityServices.GetBlockModelSpaceId(db), AcDb.OpenMode.ForWrite);
            ms.AppendEntity(pl);
            tr.AddNewlyCreatedDBObject(pl, true);
            return pl.Handle.ToString();
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

        private static string LineSnapshot(AcDb.Database db)
        {
            var parts = new List<string>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (var h in LineHandles)
                {
                    AcDb.ObjectId id;
                    if (!db.TryGetObjectId(new AcDb.Handle(Convert.ToInt64(h, 16)), out id) || id.IsErased) { parts.Add(h + " missing"); continue; }
                    var c = (AcDb.Polyline)tr.GetObject(id, AcDb.OpenMode.ForRead);
                    var sb = new StringBuilder(h + " " + c.Layer + " " + c.NumberOfVertices);
                    for (var i = 0; i < c.NumberOfVertices; i++)
                    {
                        var at = c.GetPoint3dAt(i);
                        sb.Append(string.Format(C, " ({0:R},{1:R},{2:R})", at.X, at.Y, at.Z));
                    }
                    parts.Add(sb.ToString());
                }
                tr.Commit();
            }
            return string.Join("\n", parts.ToArray());
        }

        /// <summary>Every FTF-owned entity in model space with its kind.</summary>
        private static List<KeyValuePair<AcDb.ObjectId, short>> Owned(AcDb.Database db)
        {
            var found = new List<KeyValuePair<AcDb.ObjectId, short>>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (AcDb.BlockTableRecord)tr.GetObject(AcDb.SymbolUtilityServices.GetBlockModelSpaceId(db), AcDb.OpenMode.ForRead);
                foreach (AcDb.ObjectId id in ms)
                {
                    var e = tr.GetObject(id, AcDb.OpenMode.ForRead) as AcDb.Entity;
                    if (e == null) continue;
                    using (var rb = e.GetXDataForApplication(AppName))
                    {
                        if (rb == null) continue;
                        foreach (AcDb.TypedValue tv in rb)
                            if (tv.TypeCode == (short)AcDb.DxfCode.ExtendedDataInteger16) { found.Add(new KeyValuePair<AcDb.ObjectId, short>(id, Convert.ToInt16(tv.Value))); break; }
                    }
                }
                tr.Commit();
            }
            return found;
        }

        private static Dictionary<short, int> OwnedByKind(AcDb.Database db)
        {
            var map = new Dictionary<short, int>();
            foreach (var o in Owned(db))
            {
                int n;
                map.TryGetValue(o.Value, out n);
                map[o.Value] = n + 1;
            }
            return map;
        }

        /// <summary>The stamp's text field: 0 schema, 1 rules version, 2 point number, 3 tag.</summary>
        private static string Tag(AcDb.ObjectId id, int index)
        {
            var db = id.Database;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var e = (AcDb.Entity)tr.GetObject(id, AcDb.OpenMode.ForRead);
                var texts = new List<string>();
                using (var rb = e.GetXDataForApplication(AppName))
                    if (rb != null)
                        foreach (AcDb.TypedValue tv in rb)
                            if (tv.TypeCode == (short)AcDb.DxfCode.ExtendedDataAsciiString) texts.Add(Convert.ToString(tv.Value));
                tr.Commit();
                return index < texts.Count ? texts[index] : string.Empty;
            }
        }
    }
}
