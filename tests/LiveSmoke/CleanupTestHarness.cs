// Headless checks for the production cleanup pass, on state plane coordinates like the Kenmore drawings:
//
// CLEANSEED     the NE 192nd St south margin as a line and a separate curve (R 210.50), a lot drawn as four
//               separate lines plus a stray line touching one corner, an overhead power line, and a stamp block.
// CLEANPROFILE  the LIVESMOKE CLEANUP exhibit profile (hatch spacing, overhead power hidden, stamp, wording,
//               LENGTH/DIRECTION headings); "Block" switches its stamp to a surveyor-chosen block.
// CLEANSCALE    sets the exhibit to a scale as if the drafter chose it (then FTFEXHIBITREBUILD).
// CLEANHATCH    changes the construction area hatch's pattern scale by hand.
// CLEANHAND     the drafter moves a course label and types over two title block attributes.
// CLEANVERIFY   checks what is stored and drawn for the stage named: CREATED, RESCALED, HANDHATCH, STAMPED,
//               TITLEBLOCK, HANDKEPT.
//
// Compiled with the .NET Framework csc (C# 5 only).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AcDb = Autodesk.AutoCAD.DatabaseServices;

[assembly: CommandClass(typeof(FtfCleanupTest.CleanupTestCommands))]

namespace FtfCleanupTest
{
    public class CleanupTestCommands
    {
        private static readonly CultureInfo C = CultureInfo.InvariantCulture;
        private static int _failures;
        private static double _handScale;
        private static double _createdPatternScale;
        private static string _movedKey;
        private static Point3d _movedTo;

        static CleanupTestCommands()
        {
            AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
            {
                var name = new AssemblyName(e.Name).Name;
                var loaded = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == name);
                if (loaded != null) return loaded;
                var plugin = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "FieldCodes.Cad");
                if (plugin == null) return null;
                var path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(plugin.Location), name + ".dll");
                return System.IO.File.Exists(path) ? Assembly.LoadFrom(path) : null;
            };
        }

        private static void Check(Editor ed, bool ok, string what)
        {
            if (!ok) _failures++;
            ed.WriteMessage("\n  [{0}] {1}", ok ? "PASS" : "FAIL", what);
        }

        private static readonly Point3d Center = new Point3d(1294138.9871, 283167.8494, 0);

        [CommandMethod("CLEANSEED")]
        public void Seed()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var lt = (AcDb.LayerTable)tr.GetObject(db.LayerTableId, AcDb.OpenMode.ForWrite);
                foreach (var name in new[] { "V-PROP-RWAY-E", "V-PROP-LOTL-E", "V-UTIL-POWR-OVHD-E", "V-PROP-TEXT-E" })
                {
                    if (lt.Has(name)) continue;
                    var ltr = new AcDb.LayerTableRecord { Name = name };
                    lt.Add(ltr);
                    tr.AddNewlyCreatedDBObject(ltr, true);
                }
                var ms = (AcDb.BlockTableRecord)tr.GetObject(AcDb.SymbolUtilityServices.GetBlockModelSpaceId(db), AcDb.OpenMode.ForWrite);
                Action<AcDb.Entity, string> add = (e, layer) => { e.Layer = layer; ms.AppendEntity(e); tr.AddNewlyCreatedDBObject(e, true); };

                // The right-of-way margin: a tangent line and, separately, the curve it leaves.
                add(new AcDb.Line(new Point3d(1294176.5646, 283374.9681, 0), new Point3d(1294590.8782, 283299.7994, 0)), "V-PROP-RWAY-E");
                add(new AcDb.Arc(Center, 210.5, 79.7167 * Math.PI / 180, 121.1667 * Math.PI / 180), "V-PROP-RWAY-E");

                // A 100' x 100' lot drawn as separate lines (one backwards), its west line running on past both corners like a
                // street line, a stray line from its north-east corner, and a line across the middle (the next lot's line).
                add(new AcDb.Line(new Point3d(1294250, 283200, 0), new Point3d(1294350, 283200, 0)), "V-PROP-LOTL-E");
                add(new AcDb.Line(new Point3d(1294350, 283300, 0), new Point3d(1294350, 283200, 0)), "V-PROP-LOTL-E");
                add(new AcDb.Line(new Point3d(1294350, 283300, 0), new Point3d(1294250, 283300, 0)), "V-PROP-LOTL-E");
                add(new AcDb.Line(new Point3d(1294250, 283320, 0), new Point3d(1294250, 283180, 0)), "V-PROP-LOTL-E");
                add(new AcDb.Line(new Point3d(1294350, 283300, 0), new Point3d(1294400, 283330, 0)), "V-PROP-LOTL-E");
                add(new AcDb.Line(new Point3d(1294300, 283200, 0), new Point3d(1294300, 283300, 0)), "V-PROP-LOTL-E");

                // Overhead power across the construction area, and a parcel label.
                add(new AcDb.Line(new Point3d(1294120, 283345, 0), new Point3d(1294210, 283352, 0)), "V-UTIL-POWR-OVHD-E");
                add(new AcDb.MText { Contents = "LOT C", TextHeight = 2.4, Location = new Point3d(1294162, 283350, 0), Attachment = AcDb.AttachmentPoint.MiddleCenter }, "V-PROP-TEXT-E");

                // A stamp block for FTFEXHIBITSTAMP to offer.
                var bt = (AcDb.BlockTable)tr.GetObject(db.BlockTableId, AcDb.OpenMode.ForWrite);
                var stamp = new AcDb.BlockTableRecord { Name = "TEST_STAMP" };
                bt.Add(stamp);
                tr.AddNewlyCreatedDBObject(stamp, true);
                var ring = new AcDb.Circle(Point3d.Origin, Vector3d.ZAxis, 0.75);
                stamp.AppendEntity(ring);
                tr.AddNewlyCreatedDBObject(ring, true);

                // A title block like the office's: a job number, a note, and person fields -- two of them carrying
                // initials of their own, as an office block's defaults can.
                var tb = new AcDb.BlockTableRecord { Name = "TEST_TB" };
                bt.Add(tb);
                tr.AddNewlyCreatedDBObject(tb, true);
                var frame = new AcDb.Polyline();
                frame.AddVertexAt(0, new Point2d(0, 0), 0, 0, 0);
                frame.AddVertexAt(1, new Point2d(3, 0), 0, 0, 0);
                frame.AddVertexAt(2, new Point2d(3, 0.6), 0, 0, 0);
                frame.AddVertexAt(3, new Point2d(0, 0.6), 0, 0, 0);
                frame.Closed = true;
                tb.AppendEntity(frame);
                tr.AddNewlyCreatedDBObject(frame, true);
                var y = 0.05;
                foreach (var def in new[] { new[] { "JOBNO", "" }, new[] { "NOTE", "ORIGINAL" }, new[] { "DRAWNBY", "" }, new[] { "CHECKEDBY", "JRD" }, new[] { "APPROVEDBY", "XX" } })
                {
                    var att = new AcDb.AttributeDefinition(new Point3d(0.1, y, 0), def[1], def[0], def[0], db.Textstyle) { Height = 0.08 };
                    tb.AppendEntity(att);
                    tr.AddNewlyCreatedDBObject(att, true);
                    y += 0.1;
                }
                tr.Commit();
            }
            doc.Editor.WriteMessage("\nCLEANSEED: done.\n");
        }

        [CommandMethod("CLEANPROFILE", CommandFlags.NoUndoMarker)]
        public void Profile()
        {
            var ed = Application.DocumentManager.MdiActiveDocument.Editor;
            var r = ed.GetString("\nStamp mode: ");
            var s = new FieldCodes.Settings.FtfSettings();
            var x = s.Exhibits;
            x.HatchSpacingIn = 0.05;
            x.OverheadPower = "Hide";
            x.OverheadPowerLayers = "V-UTIL-POWR-OVHD*";
            x.StampMode = r.Status == PromptStatus.OK && r.StringResult.Trim().Length > 0 ? r.StringResult.Trim() : "Placeholder";
            x.StampX = 6.5; x.StampY = 1.5;
            x.AreaLabelFormat = "APPROX {purpose} AREA = {sqft} SF";
            x.LineTableHeadings = "LENGTH/DIRECTION";
            x.Scales = "10,20,30,40,50,60,100";
            s.Easements.AskTableLocation = false;
            if (string.Equals(x.StampMode, "TitleBlock", StringComparison.OrdinalIgnoreCase))
            {
                // An older profile that maps an approval field and a fixed name. Saving it is refused now; written as it
                // might already be on disk, the exhibit must still not fill those fields.
                x.StampMode = "Block";
                x.TitleBlockName = "TEST_TB";
                x.TitleBlockX = 1.0; x.TitleBlockY = 0.1;
                x.TitleBlockAttributes = "JobNo={projectNumber}; CheckedBy={checkedBy}; ApprovedBy={checkedBy}; DrawnBy=CMM; Note={parcel}";
                var refused = false;
                try { FieldCodes.Settings.FtfSettings.SaveProfile("LIVESMOKE CLEANUP", s); }
                catch (FieldCodes.ConfigException) { refused = true; }
                Check(ed, refused, "a profile mapping ApprovedBy, or a fixed name into DrawnBy, cannot be saved");
                System.IO.File.WriteAllText(FieldCodes.Settings.FtfSettings.ProfilePath("LIVESMOKE CLEANUP"), Newtonsoft.Json.JsonConvert.SerializeObject(s, Newtonsoft.Json.Formatting.Indented));
                ed.WriteMessage("\nCLEANPROFILE: LIVESMOKE CLEANUP written with the TEST_TB title block.\n");
                return;
            }
            FieldCodes.Settings.FtfSettings.SaveProfile("LIVESMOKE CLEANUP", s);
            ed.WriteMessage("\nCLEANPROFILE: LIVESMOKE CLEANUP saved (stamp " + x.StampMode + ").\n");
        }

        /// <summary>The drafter moves the first course label and types over two title block attributes.</summary>
        [CommandMethod("CLEANHAND")]
        public void Hand()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var x = Exhibits(tr, doc.Database).First();
                var label = x.Items.First(i => i.Kind == "LABEL");
                var text = (AcDb.MText)tr.GetObject(Resolve(tr, doc.Database, label.Handle).ObjectId, AcDb.OpenMode.ForWrite);
                text.Location = text.Location + new Vector3d(0.25, 0.15, 0);
                _movedKey = label.Key;
                _movedTo = text.Location;
                var border = x.Items.First(i => i.Key == "BORDER");
                var block = (AcDb.BlockReference)tr.GetObject(Resolve(tr, doc.Database, border.Handle).ObjectId, AcDb.OpenMode.ForRead);
                foreach (AcDb.ObjectId id in block.AttributeCollection)
                {
                    var att = (AcDb.AttributeReference)tr.GetObject(id, AcDb.OpenMode.ForWrite);
                    if (att.Tag == "NOTE") att.TextString = "EDITED BY HAND";
                    if (att.Tag == "JOBNO") att.TextString = "9999";
                }
                tr.Commit();
            }
            doc.Editor.WriteMessage("\nCLEANHAND: label " + _movedKey + " moved; NOTE and JOBNO typed over.\n");
        }

        private static Dictionary<string, string> Attributes(AcDb.Transaction tr, AcDb.Database db, FieldCodes.Exhibits.ExhibitRecord x)
        {
            var result = new Dictionary<string, string>();
            var border = x.Items.FirstOrDefault(i => i.Key == "BORDER");
            var block = border == null ? null : Resolve(tr, db, border.Handle) as AcDb.BlockReference;
            if (block == null) return result;
            foreach (AcDb.ObjectId id in block.AttributeCollection)
            {
                var att = (AcDb.AttributeReference)tr.GetObject(id, AcDb.OpenMode.ForRead);
                result[att.Tag] = att.TextString;
            }
            return result;
        }

        /// <summary>The drafter changes the exhibit viewport's scale by hand (unlocked, scale set, centre kept).</summary>
        [CommandMethod("CLEANSCALE")]
        public void Scale()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var r = doc.Editor.GetString("\nScale: ");
            double scale;
            if (r.Status != PromptStatus.OK || !double.TryParse(r.StringResult, NumberStyles.Float, C, out scale)) return;
            string layout;
            using (var tr = doc.Database.TransactionManager.StartOpenCloseTransaction())
                layout = Exhibits(tr, doc.Database).First().LayoutName;
            AcDb.LayoutManager.Current.CurrentLayout = layout;
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var x = Exhibits(tr, doc.Database).First();
                var vp = (AcDb.Viewport)tr.GetObject(Resolve(tr, doc.Database, x.ViewportHandle).ObjectId, AcDb.OpenMode.ForWrite);
                vp.Locked = false;
                vp.CustomScale = 1.0 / scale;
                tr.Commit();
            }
            AcDb.LayoutManager.Current.CurrentLayout = "Model";
            doc.Editor.WriteMessage("\nCLEANSCALE: the exhibit viewport was set to 1\" = " + scale + "' by hand.\n");
        }

        [CommandMethod("CLEANHATCH")]
        public void HandHatch()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var hatch = AreaHatch(tr, doc.Database);
                hatch.UpgradeOpen();
                hatch.PatternScale = hatch.PatternScale * 3;
                hatch.SetHatchPattern(hatch.PatternType, hatch.PatternName);
                hatch.EvaluateHatch(true);
                _handScale = hatch.PatternScale;
                tr.Commit();
            }
            doc.Editor.WriteMessage("\nCLEANHATCH: the construction area hatch scale was changed by hand to " + _handScale.ToString("0.###", C) + ".\n");
        }

        [CommandMethod("CLEANVERIFY", CommandFlags.NoUndoMarker)]
        public void Verify()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;
            var db = doc.Database;
            var stageResult = ed.GetString("\nStage: ");
            var stage = stageResult.Status == PromptStatus.OK ? stageResult.StringResult.Trim().ToUpperInvariant() : string.Empty;
            _failures = 0;
            ed.WriteMessage("\nCLEANVERIFY " + stage);
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var records = Easements(tr, db);
                var area = records.FirstOrDefault(r => r.IsArea);
                var portions = records.Where(r => r.IsPortion).ToList();
                var exhibit = Exhibits(tr, db).FirstOrDefault();
                var es = new FieldCodes.Settings.EasementSettings();

                if (stage == "CREATED")
                {
                    Check(ed, area != null, "the construction area is stored");
                    if (area != null)
                    {
                        var tie = area.TieCourses();
                        Check(ed, tie.Count == 2 && tie[0].Course.Kind == FieldCodes.Easements.CourseKind.Line && tie[1].Course.Kind == FieldCodes.Easements.CourseKind.Arc,
                              "the commencement tie follows the margin: a line then the R 210.50 curve (" + string.Join(", ", tie.Select(t => t.Course.Kind + " " + t.Length.ToString("0.00", C)).ToArray()) + ")");
                        Check(ed, tie.Count == 2 && Math.Abs(tie[1].Radius.GetValueOrDefault() - 210.5) < 0.001, "the tie's curve keeps its radius, not a chord");
                        Check(ed, area.CommencementTieFollows != null && area.CommencementTieFollows.Count == 2 &&
                                  area.RouteSources.Count(s => s.Role != null && s.Role.StartsWith("TIE FOLLOWS", StringComparison.Ordinal)) == 2,
                              "the line and curve the tie follows are sources of the easement (a change to them is noticed)");
                        Check(ed, area.AreaSquareFeet > 1738.8 && area.AreaSquareFeet < 1738.95, "the construction area is 1,739 sq ft as in the office legal (" + area.AreaSquareFeet.ToString("0.00", C) + ")");
                        var closure = FieldCodes.Easements.Closure.ForRecord(area, es);
                        Check(ed, closure.Ready, "closure: the stated courses, tie included, reproduce the drawing (" + string.Join("; ", closure.Problems.ToArray()) + ")");
                        var legal = FieldCodes.Easements.LegalDescriptionWriter.WriteArea(area, new FieldCodes.Easements.LegalInputs(), es, null);
                        Check(ed, legal.Text.Contains("THENCE ALONG [LINE THE TIE RUNS ALONG], ") && legal.Text.Contains("THENCE CONTINUING ALONG SAID LINE, ALONG A CURVE"),
                              "the legal draft states the tie along the margin, with its curve");
                    }

                    Check(ed, portions.Count == 1, "one portion easement: the ambiguous lot selection (a line across the lot: two areas) made nothing (" + portions.Count + ")");
                    var portion = portions.FirstOrDefault();
                    if (portion != null)
                    {
                        Check(ed, portion.LotLines != null && portion.LotLines.Count == 5 && portion.Parcel != null && portion.Parcel.Handle == null,
                              "the lot was built from its separate lines (west line past the corners, stray line set aside); no lot object was made or joined");
                        Check(ed, Math.Abs(portion.AreaSquareFeet - 2000) < 0.01, "THE WEST 20 FEET of the 100' lot is 2,000 sq ft (" + portion.AreaSquareFeet.ToString("0.00", C) + ")");
                        var lines = CountOnLayer(tr, db, "V-PROP-LOTL-E");
                        Check(ed, lines == 6, "the lot lines are untouched: still 6 separate lines on their layer (" + lines + ")");
                    }
                }

                if (exhibit != null)
                {
                    ed.WriteMessage("\n   exhibit {0}: 1\" = {1}' items {2}", exhibit.LayoutName, exhibit.Scale, exhibit.Items.Count);
                    foreach (var n in exhibit.Review) ed.WriteMessage("\n     review: " + n.ToString().Replace("{", "{{").Replace("}", "}}"));
                    var hatch = AreaHatch(tr, db);
                    var printed = Spacing(hatch) / exhibit.Scale;
                    area = Easements(tr, db).First(r => r.IsArea);
                    if (stage == "CREATED" || stage == "RESCALED")
                    {
                        Check(ed, Math.Abs(printed - 0.05) < 1e-4, stage + ": the hatch prints its lines 0.05\" apart at 1\" = " + exhibit.Scale + "' (" + printed.ToString("0.0000", C) + ")");
                        Check(ed, area.HatchScales != null && area.HatchScales.ContainsKey(hatch.Handle.ToString()) && Math.Abs(area.HatchScales[hatch.Handle.ToString()] - hatch.PatternScale) < 1e-9,
                              stage + ": the scale FTF gave the hatch is recorded");
                        if (stage == "CREATED") _createdPatternScale = hatch.PatternScale;
                        else Check(ed, Math.Abs(hatch.PatternScale - _createdPatternScale * 2) < 1e-6, "rebuilt at 1\" = 40' the hatch scale doubled");
                    }
                    if (stage == "HANDHATCH")
                    {
                        Check(ed, Math.Abs(hatch.PatternScale - _handScale) < 1e-9, "the hatch scale set by hand is kept on rebuild");
                        Check(ed, area.HatchPatternScaleByHand.HasValue && Math.Abs(area.HatchPatternScaleByHand.Value - _handScale) < 1e-9, "the hand-set hatch scale is recorded with the easement");
                    }
                    if (stage == "CREATED")
                    {
                        Check(ed, exhibit.NorthArrowVerified, "the north arrow is checked (north up)");
                        var vp = Resolve(tr, db, exhibit.ViewportHandle) as AcDb.Viewport;
                        var frozen = vp == null ? new List<string>() : vp.GetFrozenLayers().Cast<AcDb.ObjectId>().Select(id => ((AcDb.LayerTableRecord)tr.GetObject(id, AcDb.OpenMode.ForRead)).Name).ToList();
                        Check(ed, frozen.Contains("V-UTIL-POWR-OVHD-E"), "overhead power is hidden in the exhibit viewport (profile: Hide)");
                        var power = (AcDb.LayerTableRecord)tr.GetObject(((AcDb.LayerTable)tr.GetObject(db.LayerTableId, AcDb.OpenMode.ForRead))["V-UTIL-POWR-OVHD-E"], AcDb.OpenMode.ForRead);
                        Check(ed, !power.IsFrozen && !power.IsOff, "overhead power is not frozen or off in model space");
                        Check(ed, !frozen.Contains("V-PROP-RWAY-E") && !frozen.Contains("V-PROP-LOTL-E"), "right-of-way and lot lines stay visible as context");
                        var areaLabel = exhibit.Items.FirstOrDefault(i => i.Key == "AREA:" + area.Id);
                        Check(ed, areaLabel != null && (areaLabel.Text ?? string.Empty).Contains("APPROX. TEMPORARY CONSTRUCTION AREA = 1,739 SF") && !(areaLabel.Text ?? string.Empty).Contains("EASEMENT AREA"),
                              "a construction area keeps its area wording; the profile's easement wording is not forced on it (" + (areaLabel == null ? "none" : areaLabel.Text) + ")");
                        var stamp = exhibit.Items.FirstOrDefault(i => i.Key == "STAMP");
                        var stampRef = stamp == null ? null : Resolve(tr, db, stamp.Handle) as AcDb.BlockReference;
                        Check(ed, stampRef != null && stampRef.Name.StartsWith("FTF_STAMP_PLACEHOLDER", StringComparison.Ordinal) && exhibit.StampBlock == null,
                              "a stamp placeholder marks the stamp's place; no stamp was chosen by FTF");
                        var table = exhibit.Items.FirstOrDefault(i => i.Key == "LINETABLE");
                        var t = table == null ? null : Resolve(tr, db, table.Handle) as AcDb.Table;
                        if (t != null) Check(ed, t.Cells[1, 1].TextString == "LENGTH" && t.Cells[1, 2].TextString == "DIRECTION", "the line table uses the LENGTH / DIRECTION headings");
                        else ed.WriteMessage("\n   (no line table at this scale: every course fits along its line)");
                    }
                    if (stage == "TITLEBLOCK")
                    {
                        var a = Attributes(tr, db, exhibit);
                        var messages = string.Join(" | ", exhibit.Review.Select(n => n.Message).ToArray());
                        ed.WriteMessage("\n   TEST_TB: " + string.Join(", ", a.Select(kv => kv.Key + "=" + kv.Value).ToArray()));
                        Check(ed, a.Count == 5, "the office title block is placed with its attributes");
                        Check(ed, a.ContainsKey("JOBNO") && a["JOBNO"] == "5543744009" && a.ContainsKey("NOTE") && a["NOTE"] == "LOT C", "mapped fields carry the exhibit's own values (job number, parcel)");
                        Check(ed, a.ContainsKey("APPROVEDBY") && a["APPROVEDBY"] == "XX" && messages.Contains("APPROVEDBY was not filled"),
                              "the approval field is never filled by FTF, even when an older profile maps it");
                        Check(ed, a.ContainsKey("DRAWNBY") && a["DRAWNBY"] == string.Empty && messages.Contains("DRAWNBY was not filled"),
                              "a fixed name in the profile is not written into a person field");
                        Check(ed, messages.Contains("CHECKEDBY shows the block's own value \"JRD\"") && messages.Contains("APPROVEDBY shows the block's own value \"XX\""),
                              "initials the block itself carries are flagged, not passed off as this exhibit's");
                    }
                    if (stage == "HANDKEPT" || stage == "HANDKEPT2")
                    {
                        var a = Attributes(tr, db, exhibit);
                        var messages = string.Join(" | ", exhibit.Review.Select(n => n.Message).ToArray());
                        Check(ed, a.ContainsKey("NOTE") && a["NOTE"] == "EDITED BY HAND" && a.ContainsKey("JOBNO") && a["JOBNO"] == "9999",
                              stage + ": title block values typed by hand survive the rebuild (" + string.Join(", ", a.Select(kv => kv.Key + "=" + kv.Value).ToArray()) + ")");
                        Check(ed, messages.Contains("keeps the value typed by hand"), stage + ": the kept hand values are listed for review");
                        var moved = exhibit.Items.FirstOrDefault(i => i.Key == _movedKey);
                        var text = moved == null ? null : Resolve(tr, db, moved.Handle) as AcDb.MText;
                        Check(ed, text != null && text.Location.DistanceTo(_movedTo) < 1e-6 && moved.HandPosition,
                              stage + ": the course label moved by hand stays where the drafter put it" + (text == null ? " (missing)" : " (" + text.Location.DistanceTo(_movedTo).ToString("0.####", C) + " in away)"));
                        Check(ed, messages.Contains("stays where it was moved by hand"), stage + ": the kept label is listed for review");
                    }
                    if (stage == "STAMPED")
                    {
                        var stamp = exhibit.Items.FirstOrDefault(i => i.Key == "STAMP");
                        var stampRef = stamp == null ? null : Resolve(tr, db, stamp.Handle) as AcDb.BlockReference;
                        Check(ed, exhibit.StampBlock == "TEST_STAMP" && stampRef != null && stampRef.Name == "TEST_STAMP", "FTFEXHIBITSTAMP placed the stamp block the surveyor named");
                    }
                }
                ed.WriteMessage("\nCLEANVERIFY {0}: {1} failure(s).\n", stage, _failures);
            }
        }

        // ---------------------------------------------------------------- helpers

        private static AcDb.Hatch AreaHatch(AcDb.Transaction tr, AcDb.Database db)
        {
            var area = Easements(tr, db).First(r => r.IsArea);
            var ms = (AcDb.BlockTableRecord)tr.GetObject(AcDb.SymbolUtilityServices.GetBlockModelSpaceId(db), AcDb.OpenMode.ForRead);
            foreach (AcDb.ObjectId id in ms)
            {
                var h = tr.GetObject(id, AcDb.OpenMode.ForRead) as AcDb.Hatch;
                if (h == null) continue;
                var rb = h.GetXDataForApplication("FTF_FIELDTOFINISH");
                if (rb == null) continue;
                if (rb.AsArray().Any(v => v.Value is string && (string)v.Value == area.Id)) return h;
            }
            return null;
        }

        private static double Spacing(AcDb.Hatch hatch)
        {
            var best = double.MaxValue;
            for (var i = 0; i < hatch.NumberOfPatternDefinitions; i++)
            {
                var d = hatch.GetPatternDefinitionAt(i);
                var across = Math.Abs(-Math.Sin(d.Angle) * d.OffsetX + Math.Cos(d.Angle) * d.OffsetY);
                if (across > 1e-9) best = Math.Min(best, across);
            }
            return best;
        }

        private static int CountOnLayer(AcDb.Transaction tr, AcDb.Database db, string layer)
        {
            var ms = (AcDb.BlockTableRecord)tr.GetObject(AcDb.SymbolUtilityServices.GetBlockModelSpaceId(db), AcDb.OpenMode.ForRead);
            return ms.Cast<AcDb.ObjectId>().Select(id => tr.GetObject(id, AcDb.OpenMode.ForRead) as AcDb.Entity).Count(e => e is AcDb.Line && e.Layer == layer);
        }

        private static AcDb.Entity Resolve(AcDb.Transaction tr, AcDb.Database db, string handle)
        {
            long value;
            if (handle == null || !long.TryParse(handle, NumberStyles.HexNumber, C, out value)) return null;
            AcDb.ObjectId id;
            if (!db.TryGetObjectId(new AcDb.Handle(value), out id) || id.IsErased) return null;
            return tr.GetObject(id, AcDb.OpenMode.ForRead) as AcDb.Entity;
        }

        private static AcDb.DBDictionary Store(AcDb.Transaction tr, AcDb.Database db, string name)
        {
            var nod = (AcDb.DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, AcDb.OpenMode.ForRead);
            if (!nod.Contains("FTF_FIELDTOFINISH")) return null;
            var root = (AcDb.DBDictionary)tr.GetObject(nod.GetAt("FTF_FIELDTOFINISH"), AcDb.OpenMode.ForRead);
            return root.Contains(name) ? (AcDb.DBDictionary)tr.GetObject(root.GetAt(name), AcDb.OpenMode.ForRead) : null;
        }

        private static string Join(AcDb.Transaction tr, AcDb.DBDictionary dict, string key)
        {
            var xr = (AcDb.Xrecord)tr.GetObject(dict.GetAt(key), AcDb.OpenMode.ForRead);
            var sb = new System.Text.StringBuilder();
            foreach (AcDb.TypedValue tv in xr.Data) if (tv.Value is string) sb.Append((string)tv.Value);
            return sb.ToString();
        }

        private static List<FieldCodes.Easements.EasementRecord> Easements(AcDb.Transaction tr, AcDb.Database db)
        {
            var dict = Store(tr, db, "EASEMENTS");
            var list = new List<FieldCodes.Easements.EasementRecord>();
            if (dict == null) return list;
            foreach (AcDb.DBDictionaryEntry e in dict) list.Add(FieldCodes.Easements.EasementRecord.FromJson(Join(tr, dict, e.Key)));
            return list;
        }

        private static List<FieldCodes.Exhibits.ExhibitRecord> Exhibits(AcDb.Transaction tr, AcDb.Database db)
        {
            var dict = Store(tr, db, "EXHIBITS");
            var list = new List<FieldCodes.Exhibits.ExhibitRecord>();
            if (dict == null) return list;
            foreach (AcDb.DBDictionaryEntry e in dict) list.Add(FieldCodes.Exhibits.ExhibitRecord.FromJson(Join(tr, dict, e.Key)));
            return list;
        }

        /// <summary>Writes a record back the way the plugin stores it (text chunks in an Xrecord).</summary>
        private static void Save(AcDb.Transaction tr, AcDb.Database db, string store, string id, string json)
        {
            var dict = Store(tr, db, store);
            var xr = (AcDb.Xrecord)tr.GetObject(dict.GetAt(id), AcDb.OpenMode.ForWrite);
            var values = new List<AcDb.TypedValue>();
            for (var i = 0; i < json.Length; i += 250) values.Add(new AcDb.TypedValue((int)AcDb.DxfCode.Text, json.Substring(i, Math.Min(250, json.Length - i))));
            xr.Data = new AcDb.ResultBuffer(values.ToArray());
        }
    }
}
