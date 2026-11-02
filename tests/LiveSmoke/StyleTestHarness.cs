// Headless checks that Settings > Text Styles, Leader Styles and Style Assignments control the
// objects FTFLABELS draws, and that FTFRESTYLE updates them in place.
//
// The drawing folder holds an ftf-settings.json (written by deploy\smoke-test.ps1) with:
//   text style  "Smoke text":   drawing text style FTFSMOKE, 0.12" high, colour 3, prefix "S-", background mask
//   leader      "Smoke leader": spline, _Dot arrow 0.05", colour 1, no landing
//   assignments: family Utilities -> Smoke text, layer FTF-SMOKE-UTIL
//                code FH          -> Smoke leader, leader Always
//                code MB          -> not labelled
//
// STYLESEED     a text style FTFSMOKE and four COGO points: FH 12, PP 1234, MB 5, SN 135 STOP
// STYLEVERIFY   checks the stage named: STYLED (after FTFLABELS), PREVIEW (after FTFRESTYLE Preview),
//               RESTYLED (after STYLEEDIT and FTFRESTYLE Apply)
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
        private static bool _moved;

        private static Editor Ed { get { return Application.DocumentManager.MdiActiveDocument.Editor; } }

        private static void Check(bool ok, string what)
        {
            if (ok) _passes++; else _failures++;
            Ed.WriteMessage("\n  [{0}] {1}", ok ? "PASS" : "FAIL", what);
            Record((ok ? "PASS " : "FAIL ") + what);
        }

        private static void Record(string line)
        {
            var file = Environment.GetEnvironmentVariable("FTF_STYLE_RESULT");
            if (string.IsNullOrEmpty(file)) return;
            try { File.AppendAllText(file, line + Environment.NewLine); }
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
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            Record("BEGIN " + DateTime.UtcNow.ToString("o", C));
            var plugin = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "FieldCodes.Cad");
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
            Check(Numbers.Count == 4, "four COGO points and text style FTFSMOKE seeded (scale " + Scale(db).ToString("0.##", C) + ")");
        }

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
            var stage = Ed.GetString("\nStage: ").StringResult.Trim().ToUpperInvariant();
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
                Check(fh is AcDb.MLeader, stage + ": the hydrant (leader Always) is one multileader");
                if (fh is AcDb.MLeader)
                {
                    var ml = (AcDb.MLeader)fh;
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
                Check(pp != null, stage + ": the pole is labelled");
                if (pp != null)
                {
                    Check(Contents(pp).StartsWith("S-POLE 1234", StringComparison.Ordinal), stage + ": pole text has the family's prefix (" + Contents(pp) + ")");
                    Check(Near(Height(pp), smokeHeight), stage + ": pole text height " + Height(pp).ToString("0.###", C) + " = " + smokeHeight.ToString("0.###", C));
                    var colour = pp is AcDb.MText ? pp.Color.ColorIndex : ((AcDb.MLeader)pp).TextColor.ColorIndex;
                    Check(colour == smokeColour, stage + ": pole text colour " + colour + " = " + smokeColour);
                    Check(pp.Layer.Equals("FTF-SMOKE-UTIL", StringComparison.OrdinalIgnoreCase), stage + ": pole label on FTF-SMOKE-UTIL");
                    if (_moved) Check(Where(pp).DistanceTo(_movedTo) < 1e-6, stage + ": the pole label moved by hand stayed where it was put");
                }

                var mb = LabelOf(tr, db, "MB", out xd);
                Check(mb == null, stage + ": the mailbox (labelling turned off for MB) has no label");

                var sn = LabelOf(tr, db, "SN", out xd);
                Check(sn != null, stage + ": the sign is labelled");
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
            var db = Application.DocumentManager.MdiActiveDocument.Database;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                List<string> xd;
                var pp = LabelOf(tr, db, "PP", out xd);
                if (pp != null)
                {
                    pp.UpgradeOpen();
                    pp.TransformBy(Matrix3d.Displacement(new Vector3d(3, 2, 0)));
                    _movedTo = Where(pp);
                    _moved = true;
                }
                tr.Commit();
            }
            Check(_moved, "the pole's label was moved by hand");
        }

        [CommandMethod("STYLEEDIT")]
        public void Edit()
        {
            var db = Application.DocumentManager.MdiActiveDocument.Database;
            var path = Path.Combine(Path.GetDirectoryName(db.Filename), "ftf-settings.json");
            var json = File.ReadAllText(path);
            var edited = json.Replace("\"heightPlotted\": 0.12", "\"heightPlotted\": 0.15").Replace("\"color\": \"3\"", "\"color\": \"5\"");
            File.WriteAllText(path, edited);

            var plugin = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "FieldCodes.Cad");
            var session = plugin != null ? plugin.GetType("FieldCodes.Cad.FtfSession") : null;
            var invalidate = session != null ? session.GetMethod("InvalidateSettings", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static) : null;
            if (invalidate != null) invalidate.Invoke(null, null);
            Check(edited != json && invalidate != null, "settings edited (Smoke text 0.15\", colour 5) and re-read");
        }

        [CommandMethod("STYLEEND")]
        public void End()
        {
            var verdict = _failures == 0 ? "PASS" : "FAIL";
            Ed.WriteMessage("\nFTF STYLE CHECK: {0} ({1} passed, {2} failed; stages {3})\n", verdict, _passes, _failures,
                            string.Join(", ", Stages.ToArray()));
            Record("RESULT " + verdict + " " + _passes + " " + _failures);
            Record("COMPLETE");
        }
    }
}
