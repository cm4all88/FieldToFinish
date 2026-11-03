// Headless Civil 3D checks for point accountability and linework continuity, driven by
// deploy\smoke-test.ps1 as a BLOCKING stage: a failure makes UPDATEFTF put the previous build back.
//
// ACCSEED     a fresh drawing gets 34 synthetic COGO points (invented local coordinates) and the plain
//             polylines drawn through them (the TBC-export reality), plus a TIN surface:
//               A1  interrupted fence 101-103 / 105-107, "PP" 104 between, 105 re-begun   -> probable gap
//               A2  gate: fence ends at FGP posts 202 / 203, 6 ft apart                  -> intentional, no item
//               A4  FNC1 401-402 and FNC2 403-404, 3 ft apart                             -> never joined
//               A6  601 / 602 SSCO 0.004 apart                                            -> duplicates
//               A7  701 "XYZQ 12" (no rule), 702 bare SSCO with the template's marker    -> unknown, no symbol
//               A8  EP 801-805 with no line                                               -> missing segment
//               A9  GS 901-903 TIN vertices, ZK 904 (ignored, no reason), GS 905 off the TIN
//               A10 FIP 5/8 1001 (control)                                                -> control
//               A11 WFH 12 1101 (a rule that plans a label)
//               MV  FNC 1201-1203, the line skips 1202                                     -> missing vertex
// ACCVERIFY   first requires that the named command ran exactly once since the previous stage, then:
//               RUN       after FTFRUN: the checks ran after it; the expected items, ledger dispositions,
//                         previews on a non-plotted layer, reports beside the drawing
//               AGAIN     FTFRUN again: the same open items, the same FTF objects by kind (no duplicates)
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
// ACCMOVE     moves 702 one foot east (the harness, standing in for a surveyor's edit).
// ACCERASE    erases point 1001 and the approved correction.
// ACCEND      writes the verdict and COMPLETE.
//
// Result protocol as ContourTestHarness: lines appended to the file named by FTF_ACCOUNT_RESULT.
// Compiled with the .NET Framework csc (C# 5 only). FTF types are read by reflection.
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

        private static int _failures;
        private static int _passes;
        private static readonly List<string> Stages = new List<string>();
        private static readonly List<string> LineHandles = new List<string>();
        private static Assembly _plugin;
        private static short _correctionKind = -1;
        private static short _proposalKind = -1;
        private static short _labelKind = -1;
        private static string _pointSnapshot;
        private static string _lineSnapshot;
        private static List<string> _openAfterRun;
        private static Dictionary<short, int> _ownedAfterRun;
        private static string _correctionHandle;
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

        private static void Note(Editor ed, string what)
        {
            ed.WriteMessage("\n  [NOTE] {0}", what);
            Record("NOTE " + what);
        }

        private static void Record(string line)
        {
            var file = Environment.GetEnvironmentVariable("FTF_ACCOUNT_RESULT");
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

        [CommandMethod("ACCSEED")]
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
                SeenStarted[name] = Trace("StartedCount", name);
                SeenFinished[name] = Trace("FinishedCount", name);
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
                Point(tr, cdoc, 701, 0, 400, 100, "XYZQ 12"); Point(tr, cdoc, 702, 30, 400, 100, "SSCO");

                // A8 EP with no line
                for (var i = 0; i < 5; i++)
                    Point(tr, cdoc, (uint)(801 + i), i * 10.0, 500, 100, i == 0 ? "EP B" : i == 4 ? "EP E" : "EP");

                // A9 ground shots, an ignore entry without a reason
                Point(tr, cdoc, 901, 0, 600, 100, "GS"); Point(tr, cdoc, 902, 10, 600, 101, "GS"); Point(tr, cdoc, 903, 0, 610, 102, "GS");
                Point(tr, cdoc, 904, 30, 600, 100, "ZK"); Point(tr, cdoc, 905, 100, 650, 100, "GS");

                // A10 control, A11 a rule that plans a label
                Point(tr, cdoc, 1001, 0, 700, 100, "FIP 5/8");
                Point(tr, cdoc, 1101, 0, 800, 100, "WFH 12");

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
            Check(ed, CountPoints(db) == 34, "34 synthetic COGO points seeded (" + CountPoints(db) + ")");
            Record("SEEDED");
        }

        // ================================================================ verify

        [CommandMethod("ACCVERIFY")]
        public void Verify()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;
            var stage = ed.GetString("\nStage: ").StringResult.Trim().ToUpperInvariant();
            Stages.Add(stage);
            var before = _failures;

            if (stage == "RUN") { RequireRanOnce("FTFRUN"); RequireRanOnce("FTFREVIEWCHECK"); AfterRun(db, ed); }
            else if (stage == "AGAIN") { RequireRanOnce("FTFRUN"); RequireRanOnce("FTFREVIEWCHECK"); Again(db, ed); }
            else if (stage == "APPROVED") { RequireRanOnce("-FTFREVIEW"); Approved(db, ed); }
            else if (stage == "KEPT") { RequireRanOnce("FTFREVIEWCHECK"); Kept(db, ed); }
            else if (stage == "CHANGED") { RequireRanOnce("-FTFREVIEW"); RequireRanOnce("FTFREVIEWCHECK"); Changed(db, ed); }
            else if (stage == "REFUSED") { RequireRanOnce("FTFQC"); Refused(db, ed); }
            else if (stage == "QC") { RequireRanOnce("-FTFREVIEW"); RequireRanOnce("FTFQC"); Qc(db, ed); }
            else if (stage == "DELETED") { RequireRanOnce("FTFREVIEWCHECK"); Deleted(db, ed); }
            else Fatal("unknown stage " + stage);

            Check(ed, LineSnapshot(db) == _lineSnapshot, stage + ": every seeded survey line unchanged");
            Check(ed, PointSnapshot(db) == _pointSnapshot, stage + ": no COGO point changed (number, raw description, XYZ) except by the harness");
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
            Check(ed, item("point.duplicate:601") != null && item("point.duplicate:602") != null, "A6: both duplicates are review items");
            Check(ed, item("code.unknown:XYZQ") != null, "A7: the code nothing handles is a review item");
            Check(ed, item("point.nosymbol:702") != null, "A7: a bare code with the template's generic marker needs its own symbol");
            Check(ed, item("line.missingsegment:EP:801-805") != null, "A8: the coded EP string with no line is a missing segment");
            Check(ed, item("line.missingvertex:FNC:1201-1202-1203") != null, "MV: the line that skips 1202 is a missing vertex");
            Check(ed, item("point.excludednoreason:904") != null, "A9: ZK, ignored without a reason, is a review item");
            Check(ed, item("point.missingoutput:905") != null, "A9: a ground shot no surface is built from is a review item");

            var ledger = Ledger(db);
            Check(ed, ledger != null && Count(P(ledger, "Points")) == 34, "the point ledger holds every point (" + (ledger == null ? 0 : Count(P(ledger, "Points"))) + ")");
            Check(ed, Disposition(ledger, "901") == "Surface", "A9: a ground shot at a TIN vertex is accounted for by the surface (" + Disposition(ledger, "901") + ")");
            var evidence = string.Join("; ", Strings(P(Point(ledger, "901"), "Evidence")).ToArray());
            Note(ed, "surface evidence for 901: " + evidence);
            Check(ed, Disposition(ledger, "1001") == "Control", "A10: FIP 5/8 is a control point (" + Disposition(ledger, "1001") + ")");
            Check(ed, Disposition(ledger, "103") == "Linework" && Disposition(ledger, "105") == "Linework", "the fence shots are on their lines");
            Check(ed, Disposition(ledger, "803") == "Review" && Strings(P(Point(ledger, "803"), "Issues")).Contains("line.missingsegment:EP:801-805"),
                  "A8: an EP shot with no line is for review, held by the missing-segment item");
            var labelled = Owned(db).Any(o => o.Value == _labelKind && Tag(o.Key, 2) == "1101");
            Check(ed, labelled ? Disposition(ledger, "1101") == "Drawn" : item("point.missingoutput:1101") != null,
                  "A11: the hydrant is " + (labelled ? "labelled by FTFRUN and accounted for as drawn" : "not labelled and its missing label is a review item"));
            var run = P(ledger, "LastRun");
            Check(ed, run != null && (int)P(run, "Points") == 34 && (int)P(run, "Review") > 0 && (int)P(run, "Accounted") > 0,
                  "the run's numbers: " + (run == null ? "none" : Convert.ToString(run.GetType().GetMethod("Describe").Invoke(run, null))));

            var proposals = Owned(db).Where(o => o.Value == _proposalKind).ToList();
            var withGeometry = open.Count(i => S(i, "Source") == "Linework continuity" && Count(P(i, "Geometry")) >= 2);
            Check(ed, proposals.Count == 2 * withGeometry && withGeometry >= 3, "a preview (line and note) for each open proposal (" + proposals.Count + " for " + withGeometry + ")");
            Check(ed, LayerPlots(db, "FTF-QC-CONTINUITY") == false, "the previews are on a non-plotted layer");
            Check(ed, !Owned(db).Any(o => o.Value == _correctionKind), "nothing is corrected before a person approves it");

            var dir = Path.GetDirectoryName(db.Filename);
            var name = Path.GetFileNameWithoutExtension(db.Filename);
            Check(ed, File.Exists(Path.Combine(dir, name + ".ftf-points.csv")) && File.Exists(Path.Combine(dir, name + ".ftf-continuity.csv")),
                  "the point ledger and continuity reports are written beside the drawing");

            _openAfterRun = open.Select(i => S(i, "Id")).OrderBy(x => x, StringComparer.Ordinal).ToList();
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

        [CommandMethod("ACCMOVE")]
        public void Move()
        {
            var db = Application.DocumentManager.MdiActiveDocument.Database;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var p = FindPoint(db, tr, 702);
                if (p == null) Fatal("point 702 is gone.");
                p.UpgradeOpen();
                p.Location = new Point3d(p.Location.X + 1, p.Location.Y, p.Location.Z);
                tr.Commit();
            }
            _pointSnapshot = PointSnapshot(db);
            Record("MOVED 702");
        }

        [CommandMethod("ACCERASE")]
        public void Erase()
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
            var verdict = _failures == 0 ? "PASS" : "FAIL";
            Ed.WriteMessage("\nFTF ACCOUNTABILITY: {0} ({1} passed, {2} failed)\n", verdict, _passes, _failures);
            Record("STAGES " + string.Join(",", Stages.ToArray()));
            Record("RESULT " + verdict + " " + _passes + " " + _failures);
            Record("COMPLETE");
        }

        // ================================================================ reading FTF's state

        private static object Register(AcDb.Database db)
        {
            return Store(db, "ReadReview");
        }

        private static object Ledger(AcDb.Database db)
        {
            return Store(db, "ReadLedger");
        }

        private static object Store(AcDb.Database db, string method)
        {
            var store = _plugin.GetType("FieldCodes.Cad.DrawingStore");
            if (store == null) Fatal("FieldCodes.Cad.DrawingStore not found.");
            var m = store.GetMethod(method, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (m == null) Fatal("DrawingStore." + method + " not found: the loaded build predates point accountability.");
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var result = m.Invoke(null, new object[] { db, tr });
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
            return ledger.GetType().GetMethod("Find").Invoke(ledger, new object[] { number });
        }

        private static string Disposition(object ledger, string number)
        {
            var p = Point(ledger, number);
            return p == null ? "(not in the ledger)" : S(p, "Disposition");
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

        private static void Point(AcDb.Transaction tr, CivApp.CivilDocument cdoc, uint number, double x, double y, double z, string description)
        {
            var id = cdoc.CogoPoints.Add(new Point3d(x, y, z), description, false);
            var cp = (CivDb.CogoPoint)tr.GetObject(id, AcDb.OpenMode.ForWrite);
            try { cp.PointNumber = number; }
            catch (System.Exception ex) { Fatal("could not set point number " + number + ": " + ex.Message); }
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
