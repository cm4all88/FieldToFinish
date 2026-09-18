// Interactive UI test of the Dip Builder WINDOW inside full Civil 3D.
//
// UISEED   builds a realistic small storm network: structure COGO points and a
//          pipe somebody already drew by hand.
// DIPUITEST drives the real FTFDIP window the way a drafter would: its buttons,
//          text boxes, grid and lists are operated through the controls
//          themselves; command-line prompts the window raises are answered as a
//          drafter would type them. Every step is checked against the drawing and
//          the stored dip data, and screenshots of the window are saved.
//
// Output: <out>\ui-test.log and <out>\*.png. Compiled with csc (C# 5).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcDoc = Autodesk.AutoCAD.ApplicationServices.Document;
using AcDb = Autodesk.AutoCAD.DatabaseServices;
using CivApp = Autodesk.Civil.ApplicationServices;
using CivDb = Autodesk.Civil.DatabaseServices;
using FU = FieldCodes.Utilities;

[assembly: CommandClass(typeof(FtfUiTest.UiTestCommands))]

namespace FtfUiTest
{
    public class UiTestCommands
    {
        private const string FtfApp = "FTF_FIELDTOFINISH";
        public static string OutDir = @"C:\dev\FieldToFinish\tests\LiveSmoke\ui";

        static UiTestCommands()
        {
            AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
            {
                var name = new AssemblyName(e.Name).Name;
                return AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == name);
            };
        }

        // ------------------------------------------------------------------ seed

        [CommandMethod("UISEED")]
        public void Seed()
        {
            AcDoc doc = AcApp.DocumentManager.MdiActiveDocument;
            AcDb.Database db = doc.Database;
            CivApp.CivilDocument cdoc = CivApp.CivilApplication.ActiveDocument;

            using (AcDb.Transaction tr = db.TransactionManager.StartTransaction())
            {
                AcDb.LayerTable lt = (AcDb.LayerTable)tr.GetObject(db.LayerTableId, AcDb.OpenMode.ForWrite);
                foreach (string name in new[] { "V-UTIL-STRM-E", "V-UTIL-STRM-TEXT-E" })
                {
                    if (lt.Has(name)) continue;
                    AcDb.LayerTableRecord ltr = new AcDb.LayerTableRecord();
                    ltr.Name = name;
                    lt.Add(ltr);
                    tr.AddNewlyCreatedDBObject(ltr, true);
                }

                object[][] pts = new object[][]
                {
                    new object[] { 1045u, 5000.0, 5000.0, 328.42, "SDMH" },
                    new object[] { 1046u, 5000.0, 5180.0, 330.10, "CB" },
                    new object[] { 1047u, 4850.0, 4850.0, 326.00, "SDMH" },
                    new object[] { 1048u, 5120.0, 5000.0, 327.90, "CB" }
                };
                foreach (object[] p in pts)
                {
                    AcDb.ObjectId id = cdoc.CogoPoints.Add(new Point3d((double)p[1], (double)p[2], (double)p[3]), (string)p[4], false);
                    CivDb.CogoPoint cp = (CivDb.CogoPoint)tr.GetObject(id, AcDb.OpenMode.ForWrite);
                    cp.PointNumber = (uint)p[0];
                }

                // Hand-drawn pipe between 1045 and 1047, stopping short of both centres
                // the way drafters draw to the structure wall.
                AcDb.BlockTableRecord ms = (AcDb.BlockTableRecord)tr.GetObject(
                    AcDb.SymbolUtilityServices.GetBlockModelSpaceId(db), AcDb.OpenMode.ForWrite);
                AcDb.Line hand = new AcDb.Line(new Point3d(4998.6, 4998.6, 0), new Point3d(4851.4, 4851.4, 0));
                hand.Layer = "V-UTIL-STRM-E";
                ms.AppendEntity(hand);
                tr.AddNewlyCreatedDBObject(hand, true);
                tr.Commit();
            }
            doc.Editor.WriteMessage("\nUISEED: done.\n");
        }

        // ------------------------------------------------------------ the script

        private static List<Step> _steps;
        private static int _index;
        private static DateTime _last;
        private static DateTime _busySince;
        private static Timer _timer;
        private static StreamWriter _log;
        private static int _pass, _fail;
        private static string _drawingPath;

        private sealed class Step
        {
            public string Name;
            public Action Run;
            public int Delay = 2500;
            /// <summary>Runs while a command is still in progress, once its modal window is open.</summary>
            public bool WhileBusy;
            /// <summary>With WhileBusy: run once the modal window has closed but the command is still asking.</summary>
            public bool AfterPreview;
            /// <summary>With WhileBusy: the modal window's type name to wait for.</summary>
            public string Modal = "EasementPreviewForm";
        }

        private static DateTime? _modalSeen;

        private static Form Preview
        {
            get { return ModalForm("EasementPreviewForm"); }
        }

        private static Form ModalForm(string typeName)
        {
            return Application.OpenForms.Cast<Form>().FirstOrDefault(f => f.GetType().Name == typeName && f.Visible);
        }

        [CommandMethod("DIPUITEST", CommandFlags.Session)]
        public void Start() { Begin(true, false); }

        /// <summary>Only the strip easement preview and legal description windows.</summary>
        [CommandMethod("ESMTUITEST", CommandFlags.Session)]
        public void StartEasements() { Begin(false, true); }

        /// <summary>Both suites, one after the other.</summary>
        [CommandMethod("ALLUITEST", CommandFlags.Session)]
        public void StartAll() { Begin(true, true); }

        private static void Begin(bool dips, bool easements)
        {
            Directory.CreateDirectory(OutDir);
            _log = new StreamWriter(Path.Combine(OutDir, "ui-test.log"), false, Encoding.UTF8) { AutoFlush = true };
            _drawingPath = AcApp.DocumentManager.MdiActiveDocument.Name;
            Log("UI test started " + DateTime.Now.ToString("s") + " on " + _drawingPath);
            Log("Screen working area " + Screen.PrimaryScreen.WorkingArea + ", DPI " + DpiOf());

            _steps = BuildSteps(dips, easements);
            _index = 0;
            _last = DateTime.Now;
            _timer = new Timer { Interval = 400 };
            _timer.Tick += Tick;
            _timer.Start();
        }

        private static int DpiOf()
        {
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) return (int)g.DpiX;
        }

        private static void Tick(object sender, EventArgs e)
        {
            if (_index >= _steps.Count) { _timer.Stop(); return; }
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            if (!string.IsNullOrEmpty(doc.CommandInProgress))
            {
                if (_steps[_index].WhileBusy && (_steps[_index].AfterPreview ? Preview == null : ModalForm(_steps[_index].Modal) != null))
                {
                    _busySince = DateTime.Now;
                    if (_modalSeen == null) _modalSeen = DateTime.Now;
                    if ((DateTime.Now - _modalSeen.Value).TotalMilliseconds < _steps[_index].Delay) return;
                    _modalSeen = null;
                    var modalStep = _steps[_index++];
                    Log("-- " + modalStep.Name);
                    try { modalStep.Run(); }
                    catch (System.Exception ex) { Fail("step threw: " + ex.GetType().Name + ": " + ex.Message); }
                    _last = DateTime.Now;
                    return;
                }
                // The harness's own start-up command can stay "in progress" while Civil 3D finishes loading.
                if (doc.CommandInProgress.ToUpperInvariant() == "DIPUITEST") _busySince = DateTime.Now;
                if ((DateTime.Now - _busySince).TotalSeconds > 45)
                {
                    Log("STUCK in " + doc.CommandInProgress + " at step " + _steps[_index].Name + " -- cancelling");
                    Shot("stuck-" + _index);
                    doc.SendStringToExecute("\x03\x03", true, false, false);
                    _busySince = DateTime.Now;
                    _fail++;
                }
                _last = DateTime.Now;
                return;
            }
            _busySince = DateTime.Now;
            if ((DateTime.Now - _last).TotalMilliseconds < _steps[_index].Delay) return;

            var step = _steps[_index++];
            Log("-- " + step.Name);
            try { step.Run(); }
            catch (System.Exception ex) { Fail("step threw: " + ex.GetType().Name + ": " + ex.Message); }
            _last = DateTime.Now;
        }

        private static void Log(string text) { if (_log != null) _log.WriteLine(text); }
        private static void Check(bool ok, string what) { if (ok) _pass++; else _fail++; Log("   [" + (ok ? "PASS" : "FAIL") + "] " + what); }
        private static void Fail(string what) { Check(false, what); }

        private static void Send(string text)
        {
            AcApp.DocumentManager.MdiActiveDocument.SendStringToExecute(text, true, false, true);
        }

        // ------------------------------------------------------------- the window

        private static Assembly Plugin
        {
            get { return AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "FieldCodes.Cad"); }
        }

        private static Type SessionType { get { return Plugin.GetType("FieldCodes.Cad.DipSession"); } }

        private static Form Window
        {
            get { return (Form)SessionType.GetField("Form").GetValue(null); }
        }

        private static FU.UtilityProject Project
        {
            get { return (FU.UtilityProject)SessionType.GetField("Project").GetValue(null); }
        }

        private static T Field<T>(string name) where T : class
        {
            return Window.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(Window) as T;
        }

        private static Button FindButton(Control root, string text)
        {
            foreach (Control c in root.Controls)
            {
                var b = c as Button;
                if (b != null && b.Text == text) return b;
                var inner = FindButton(c, text);
                if (inner != null) return inner;
            }
            return null;
        }

        private static void Click(string text)
        {
            var b = FindButton(Window, text);
            if (b == null) { Fail("no button \"" + text + "\""); return; }
            typeof(Button).GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(b, new object[] { EventArgs.Empty });
        }

        private static void Tab(int index)
        {
            Field<TabControl>("_tabs").SelectedIndex = index;
        }

        private static void SelectPipeRow(int row)
        {
            var grid = Field<DataGridView>("_grid");
            if (grid.Rows.Count <= row) { Fail("grid has no row " + row); return; }
            grid.CurrentCell = grid.Rows[row].Cells[0];
            grid.Rows[row].Selected = true;
        }

        private static void Shot(string name)
        {
            var form = Window;
            if (form == null) return;
            if (form.WindowState == FormWindowState.Minimized || !form.Visible || form.Height < 100)
            {
                // Civil 3D is not in front (someone is using another program), so the
                // window is hidden with it; render an off-screen copy of the same state.
                Log("   window hidden with Civil 3D; capturing an off-screen copy");
                RenderCopy(name, 1.0f, false);
                return;
            }
            try
            {
                using (var bmp = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));
                    bmp.Save(Path.Combine(OutDir, name + ".png"), ImageFormat.Png);
                }
                Log("   screenshot " + name + ".png (" + form.Width + "x" + form.Height + ")");
            }
            catch (System.Exception ex) { Log("   screenshot failed: " + ex.Message); }
        }

        private static FU.StructureRecord S(string number) { return Project == null ? null : Project.StructureByPoint(number); }

        private static string StatusOf(string structure, int pipe)
        {
            var s = S(structure);
            if (s == null || s.Field.Pipes.Count <= pipe) return "missing";
            var c = Project.ConnectionFor(s.Id, s.Field.Pipes[pipe].Id);
            if (c == null) return "none";
            var other = Project.Structure(c.FromStructureId == s.Id ? c.ToStructureId : c.FromStructureId);
            return c.Status + "->" + (other != null ? other.Field.PointNumber : "?");
        }

        private static void PickPoint(string button, double x, double y)
        {
            Send(string.Format(CultureInfo.InvariantCulture, "_.ZOOM _C {0},{1} 30 ", x, y));
            Click(button);
            Send(string.Format(CultureInfo.InvariantCulture, "{0},{1} ", x, y));
        }

        // Counts in the drawing ------------------------------------------------

        private static Dictionary<string, int> Counts()
        {
            var result = new Dictionary<string, int> { { "pipe", 0 }, { "pipelabel", 0 }, { "structurelabel", 0 }, { "hand", 0 } };
            var doc = AcApp.DocumentManager.MdiActiveDocument;

            using (var tr = doc.Database.TransactionManager.StartOpenCloseTransaction())
            {
                var ms = (AcDb.BlockTableRecord)tr.GetObject(AcDb.SymbolUtilityServices.GetBlockModelSpaceId(doc.Database), AcDb.OpenMode.ForRead);
                foreach (AcDb.ObjectId id in ms)
                {
                    var e = tr.GetObject(id, AcDb.OpenMode.ForRead) as AcDb.Entity;
                    if (e == null) continue;
                    var kind = KindOf(e);
                    if (kind == 19) result["pipe"]++;
                    else if (kind == 20) result["pipelabel"]++;
                    else if (kind == 21) result["structurelabel"]++;
                    else if (kind < 0 && e is AcDb.Line && e.Layer == "V-UTIL-STRM-E") result["hand"]++;
                }

            }
            return result;
        }

        private static string Texts(int kind)
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            var sb = new StringBuilder();

            using (var tr = doc.Database.TransactionManager.StartOpenCloseTransaction())
            {
                var ms = (AcDb.BlockTableRecord)tr.GetObject(AcDb.SymbolUtilityServices.GetBlockModelSpaceId(doc.Database), AcDb.OpenMode.ForRead);
                foreach (AcDb.ObjectId id in ms)
                {
                    var e = tr.GetObject(id, AcDb.OpenMode.ForRead) as AcDb.Entity;
                    if (e == null || KindOf(e) != kind) continue;
                    var mt = e as AcDb.MText;
                    if (mt != null) sb.Append(mt.Contents).Append(" | ");
                    var ml = e as AcDb.MLeader;
                    if (ml != null && ml.MText != null) sb.Append(ml.MText.Contents).Append(" | ");
                }

            }
            return sb.ToString();
        }

        private static int KindOf(AcDb.Entity e)
        {
            var rb = e.GetXDataForApplication(FtfApp);
            if (rb == null) return -1;
            using (rb)
                foreach (AcDb.TypedValue tv in rb)
                    if (tv.TypeCode == (short)AcDb.DxfCode.ExtendedDataInteger16) return (short)tv.Value;
            return -1;
        }

        private static void CountsAre(int pipes, int hand, string when)
        {
            var c = Counts();
            Check(c["pipe"] == pipes && c["hand"] == hand,
                  when + ": " + c["pipe"] + " FTF pipe line(s) (expected " + pipes + "), " + c["hand"] + " hand-drawn (expected " + hand + ")");
        }

        // ------------------------------------------------------------------ steps

        private const string Notes =
            "PT 1045 SDMH\r\n" +
            "BOT 7.82\r\n" +
            "WL 6.94\r\n" +
            "12 RCP N 6.41 INV\r\n" +
            "18 RCP SW 7.02\r\n" +
            "8 PVC E TOP 5.23\r\n" +
            "6 XYZ W 4.10\r\n" +
            "10 RCP ? 5.50\r\n" +
            "WL ???\r\n" +
            "PT 1046 CB\r\n" +
            "12 RCP S 7.20 INV OUT\r\n" +
            "PT 1047 SDMH\r\n" +
            "18 RCP NE 6.10 IN";

        private static List<Step> BuildSteps(bool dips, bool easements)
        {
            var s = new List<Step>();
            Action<string, Action, int> add = (name, run, delay) => s.Add(new Step { Name = name, Run = run, Delay = delay });

            if (dips)
            {
            add("open the Dip Builder", () => Send("FTFDIP "), 1000);
            add("window opened", () =>
            {
                Check(Window != null && Window.Visible, "FTFDIP opens the window");
                Log("   window size " + Window.Size + ", client " + Window.ClientSize + ", min " + Window.MinimumSize);
                Check(Window.Bottom <= Screen.FromControl(Window).WorkingArea.Bottom && Window.Right <= Screen.FromControl(Window).WorkingArea.Right,
                      "window fits the screen working area");
                Shot("01-opened");
            }, 3000);
            add("basic and advanced views", () =>
            {
                var toggle = Field<CheckBox>("_advancedToggle");
                var grid = Field<DataGridView>("_grid");
                toggle.Checked = false;
                var basicCols = grid.Columns.Cast<DataGridViewColumn>().Count(c => c.Visible);
                var basicMove = FindButton(Window, "Move up").Visible;
                Shot("01b-basic");
                toggle.Checked = true;
                var advancedCols = grid.Columns.Cast<DataGridViewColumn>().Count(c => c.Visible);
                Check(FindButton(Window, "Move up").Visible && !basicMove, "Move up only shows in Advanced");
                Check(basicCols == 6 && advancedCols == 14, "grid shows " + basicCols + " columns in Basic and " + advancedCols + " in Advanced");
                Shot("01c-advanced");
                toggle.Checked = false;
            }, 1000);


            // Select structure --------------------------------------------------
            add("select structure 1045 from the drawing", () => PickPoint("Select structure point...", 5000, 5000), 1500);
            add("point details populated", () =>
            {
                var info = Field<Label>("_structureTitle").Text + " " + Field<Label>("_pointInfo").Text;
                Log("   point info: " + info);
                Check(info.Contains("1045") && info.Contains("RIM 328.42") && info.Contains("\"SDMH\"") &&
                      info.Contains("N 5000.00") && info.Contains("E 5000.00"),
                      "point number, northing, easting, rim and description shown");
                Check(Field<ComboBox>("_type").Text == "SDMH", "structure type classified from the code (SDMH)");
                Shot("02-structure-selected");
            }, 3000);

            // Notes -------------------------------------------------------------
            add("paste realistic notes and read them", () =>
            {
                Field<TextBox>("_notes").Text = Notes;
                Click("Read notes");
            }, 1000);
            add("notes read", () =>
            {
                var d = Field<ListBox>("_diagnostics").Items.Cast<object>().Select(o => o.ToString()).ToList();
                foreach (var line in d) Log("   diag: " + line);
                Check(!d.Any(x => x.Contains("does not say what it was measured to")), "no confirm-the-reference warnings: unmarked dips are inverts by default");
                Check(d.Any(x => x.Contains("No recognised material")), "unknown material reported");
                Check(d.Any(x => x.Contains("No readable direction")), "unknown direction reported");
                Check(d.Any(x => x.Contains("water line has no readable dip")), "malformed WL line reported");

                var st = S("1045");
                Check(st != null && st.Field.Pipes.Count == 5, "5 pipes read for 1045");
                Check(st != null && st.Field.BottomDip == 7.82, "BOT 7.82 kept");
                Check(st != null && st.Field.WaterDip == 6.94, "WL 6.94 kept even though a later WL line was unreadable (got " + (st == null ? "?" : Convert.ToString(st.Field.WaterDip)) + ")");

                var grid = Field<DataGridView>("_grid");
                Check(grid.Rows.Count == 5, "grid shows 5 pipes");
                if (grid.Rows.Count == 5)
                {
                    for (var r = 0; r < 5; r++)
                        Log("   row " + r + ": " + string.Join(" | ", grid.Rows[r].Cells.Cast<DataGridViewCell>().Select(c => Convert.ToString(c.Value)).ToArray()));
                    Check(Convert.ToString(grid.Rows[0].Cells["Calc"].Value).StartsWith("322.01 invert"), "12 RCP N INV -> 322.01 invert");
                    Check(Convert.ToString(grid.Rows[1].Cells["Calc"].Value).StartsWith("321.40 invert") && st.Field.Pipes[1].ReferenceBasis == FU.ReferenceBasis.FieldNoteConvention, "18 RCP SW unmarked -> 321.40 invert by the office default");
                    Check(Convert.ToString(grid.Rows[1].Cells["RefStatus"].Value) == "office default (invert)", "basis shown as office default");
                    Check(Convert.ToString(grid.Rows[2].Cells["Calc"].Value).StartsWith("323.19 top of pipe"), "8 PVC E TOP 5.23 -> 323.19 top of pipe, not an invert");
                    Check(Convert.ToString(grid.Rows[3].Cells["Material"].Value) == "", "unknown material XYZ left blank");
                    Check(Convert.ToString(grid.Rows[4].Cells["Direction"].Value) == "?", "unknown direction shown as ?");
                }
                var label = Field<TextBox>("_labelPreview").Text;
                Log("   label preview:\r\n" + label);
                Check(label.Contains("IE (N) = 322.01") && label.Contains("IE (SW) = 321.40") && label.Contains("TOP (E) = 323.19") &&
                      label.Contains("BOT = 320.60"), "live label built from the observations");
                Shot("03-notes-read");
            }, 3000);

            // Connections -------------------------------------------------------
            add("enter the manhole diameter", () =>
            {
                var box = Field<TextBox>("_insideWidth");
                Check(box.Visible, "SDMH shows a Diameter box");
                box.Text = "48";
                Window.GetType().GetMethod("SaveStructureFields", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(Window, null);
            }, 500);
            add("diameter on the label", () =>
            {
                Check(S("1045").EnteredInsideWidthIn == 48, "diameter 48 saved");
                Check(Field<TextBox>("_labelPreview").Text.StartsWith("SDMH 1045 48\""), "label header reads SDMH 1045 48\"");
            }, 3500);

            add("find connections for 12 RCP N", () => { SelectPipeRow(0); Click("Find connections"); }, 500);
            add("candidates shown, nothing confirmed", () =>
            {
                var list = Field<ListView>("_candidateList");
                foreach (ListViewItem item in list.Items)
                    Log("   candidate: " + string.Join(" | ", item.SubItems.Cast<ListViewItem.ListViewSubItem>().Select(x => x.Text).ToArray()));
                Check(list.Items.Count >= 1 && list.Items[0].Text == "CB 1046" && list.Items[0].SubItems[4].Text == "High",
                      "CB 1046 offered first with High confidence");
                Check(StatusOf("1045", 0) == "none", "no connection recorded before the drafter confirms (" + StatusOf("1045", 0) + ")");
                Log("   state label: " + Field<Label>("_connectionState").Text);
                Shot("04-candidates");
            }, 800);
            add("confirm CB 1046", () => { Field<ListView>("_candidateList").Items[0].Selected = true; Click("Confirm selected"); }, 300);
            add("confirmed", () => Check(StatusOf("1045", 0) == "Confirmed->1046", "12 RCP N confirmed to 1046 (" + StatusOf("1045", 0) + ")"), 3000);

            add("find connections for 18 RCP SW", () => { SelectPipeRow(1); Click("Find connections"); }, 500);
            add("change the suggestion: pick 1048 instead", () =>
            {
                var list = Field<ListView>("_candidateList");
                Check(list.Items.Count >= 1 && list.Items[0].Text == "SDMH 1047", "SDMH 1047 suggested for 18 RCP SW");
                Send("_.ZOOM _C 5120,5000 30 ");
                Click("Pick a different structure...");
                Send("5120,5000 ");
                Send("UI TEST CHANGE\n");
            }, 800);
            add("manual choice recorded", () =>
            {
                Check(StatusOf("1045", 1) == "ManualOverride->1048", "manual pick recorded as ManualOverride to 1048 (" + StatusOf("1045", 1) + ")");
                SelectPipeRow(1);
                Click("Find connections");
            }, 3000);
            add("change back to 1047", () =>
            {
                Field<ListView>("_candidateList").Items[0].Selected = true;
                Click("Confirm selected");
            }, 800);
            add("changed back", () => Check(StatusOf("1045", 1) == "Confirmed->1047", "suggestion changed back to 1047 (" + StatusOf("1045", 1) + ")"), 3000);

            add("leave 8 PVC E unresolved", () => { SelectPipeRow(2); Click("Leave unresolved"); }, 500);
            add("left unresolved", () => Check(StatusOf("1045", 2) == "LeftUnresolved->?", "8 PVC E left unresolved (" + StatusOf("1045", 2) + ")"), 3000);
            add("mark 6\" W as running outside the survey limits", () => { SelectPipeRow(3); Click("Runs outside survey limits"); }, 500);
            add("outside limits recorded", () =>
            {
                Check(StatusOf("1045", 3) == "OutsideSurveyLimits->?", "6\" W recorded as outside survey limits (" + StatusOf("1045", 3) + ")");
                var list = Field<ListView>("_connections");
                Check(list.Items.Count == 5 && list.Items[3].SubItems[3].Text == "Outside survey limits", "connections pane shows Outside survey limits");
            }, 3000);

            // Unmarked dip confirmation -----------------------------------------
            add("tick Top of pipe for 6\" W", () =>
            {
                var row = Field<DataGridView>("_grid").Rows[3];
                row.Cells["Top"].Value = true;
            }, 500);
            add("top of pipe applied, label follows", () =>
            {
                var p = S("1045").Field.Pipes[3];
                Check(p.Reference == FU.MeasurementReference.TopOfPipe && p.ReferenceBasis == FU.ReferenceBasis.EnteredByDrafter && p.MeasuredDip == 4.10,
                      "ticking Top of pipe makes 6\" W a top-of-pipe dip, set by drafter, dip still 4.10");
                Check(Convert.ToString(Field<DataGridView>("_grid").Rows[3].Cells["Calc"].Value).StartsWith("324.32 top of pipe"), "grid shows 324.32 top of pipe");
                Check(Field<TextBox>("_labelPreview").Text.Contains("TOP (W) = 324.32"), "live label shows TOP (W) = 324.32");
                Shot("05-top-of-pipe");
            }, 3500);
            add("two quick edits: untick row 3, tick row 4 back to back", () =>
            {
                var grid = Field<DataGridView>("_grid");
                grid.Rows[3].Cells["Top"].Value = false;
                grid.Rows[4].Cells["Top"].Value = true;
            }, 3000);
            add("both quick edits applied", () =>
            {
                var st = S("1045");
                Check(st.Field.Pipes[3].Reference == FU.MeasurementReference.Invert && st.Field.Pipes[4].Reference == FU.MeasurementReference.TopOfPipe,
                      "both back-to-back edits were applied, neither dropped");
            }, 5000);
            add("select 1047", () => PickPoint("Select structure point...", 4850, 4850), 500);
            add("1047 read as invert by default", () =>
                Check(S("1047").Field.Pipes[0].Reference == FU.MeasurementReference.Invert && S("1047").Field.Pipes[0].ReferenceBasis == FU.ReferenceBasis.FieldNoteConvention,
                      "1047's unmarked dip is the invert by the office default"), 3500);

            // The reported bug: a pipe typed into the grid vanished when Add pipe was pressed again.
            add("add a pipe at 1047", () => Click("Add pipe"), 500);
            add("type the new pipe in, then press Add pipe again straight away", () =>
            {
                var grid = Field<DataGridView>("_grid");
                Check(grid.Rows.Count == 2, "Add pipe gave 1047 a second row (" + grid.Rows.Count + ")");
                if (grid.Rows.Count < 2) return;
                var row = grid.Rows[1];
                row.Cells["W"].Value = "15";
                row.Cells["Material"].Value = "RCP";
                row.Cells["Direction"].Value = "E";
                row.Cells["Dip"].Value = "5.5";
                row.Cells["Reference"].Value = "Invert";
                Click("Add pipe");
            }, 4000);
            add("typed pipe kept", () =>
            {
                var st = S("1047");
                Check(st.Field.Pipes.Count == 3, "1047 now has 3 pipes (" + st.Field.Pipes.Count + ")");
                if (st.Field.Pipes.Count < 2) return;
                var p = st.Field.Pipes[1];
                Check(p.WidthIn == 15 && p.Material == "RCP" && p.Direction.Text == "E" && p.MeasuredDip == 5.5 && p.Reference == FU.MeasurementReference.Invert,
                      "the typed pipe survived pressing Add pipe again (15\" RCP E 5.5 INV)");
                var grid = Field<DataGridView>("_grid");
                Check(grid.Rows.Count == 3 && Convert.ToString(grid.Rows[1].Cells["Material"].Value) == "RCP", "the grid still shows it");
                Shot("05b-added-pipes");
            }, 5000);

            // Drawing with existing pipe choices ---------------------------------
            add("back to 1045", () => PickPoint("Select structure point...", 5000, 5000), 500);
            add("draw 1 (existing hand pipe: Keep)", () =>
            {
                Click("Draw this structure's pipes");
                Send("Keep ");
            }, 3000);
            add("after Keep", () => CountsAre(1, 1, "Keep"), 3000);
            add("draw 2 (Keep FTF N pipe, New beside hand pipe)", () =>
            {
                Click("Draw this structure's pipes");
                Send("Keep ");
                Send("New ");
            }, 500);
            add("after New", () => { CountsAre(3, 1, "New"); Log("   pipe labels: " + Texts(20)); }, 3000);
            add("draw 3 (Keep, Replace)", () =>
            {
                Click("Draw this structure's pipes");
                Send("Keep ");
                Send("Replace ");
            }, 500);
            add("after Replace", () => CountsAre(3, 0, "Replace"), 3000);
            add("undo the Replace", () => Send("_.U "), 500);
            add("after undo", () => CountsAre(3, 1, "Undo of Replace"), 3000);
            add("draw 4 (Keep, Update existing)", () =>
            {
                Click("Draw this structure's pipes");
                Send("Keep ");
                Send("Update ");
            }, 500);
            add("after Update", () =>
            {
                Log("   status bar: " + Field<Label>("_status").Text + " | session message: " + Convert.ToString(SessionType.GetField("LastMessage").GetValue(null)));
                CountsAre(2, 0, "Update");
                var labels = Texts(20);
                Log("   pipe labels: " + labels);
                Check(labels.Contains("12\" RCP SD @ 0.49%") && labels.Contains("18\" RCP SD @ 0.71%"), "both pipe labels with slopes from confirmed inverts");
                Check(Project.Overrides.Any(o => o.What == "Existing hand-drawn pipe adopted"), "adoption recorded as an override");
                Shot("06-drawn");
            }, 3000);

            // Leader -------------------------------------------------------------
            add("edit the leader text and place it", () =>
            {
                var box = Field<TextBox>("_labelPreview");
                box.Text = box.Text + "\r\nSILTED 25%";
                Click("Place structure label...");
                Send("5030,5040 ");
            }, 500);
            add("leader placed", () =>
            {
                var text = Texts(21);
                Log("   structure label: " + text);
                Check(text.Contains("SILTED 25%") && text.Contains("IE (N) = 322.01"), "edited leader placed as an MLeader with the edited text");
                Check(Project.Overrides.Any(o => o.What == "Structure label text"), "edited label text recorded as an override");
                Shot("07-leader");
                var map = Field<Control>("_map");
                Check(map != null && map.Width > 200 && map.Height > 150, "connection map shown beside the steps (" + (map == null ? "missing" : map.Size.ToString()) + ")");
            }, 3000);
            add("undo the leader", () => Send("_.U "), 500);
            add("leader undone", () =>
            {
                Check(Counts()["structurelabel"] == 0, "undo removes the leader");
                Check(Project == null || !Project.Overrides.Any(o => o.What == "Structure label text"), "undo also removes its override from the dip data");
            }, 3500);
            add("place the generated leader", () =>
            {
                Click("Regenerate label");
                Click("Place structure label...");
                Send("5030,5040 ");
            }, 500);
            add("generated leader placed", () => Check(Counts()["structurelabel"] == 1, "generated leader placed"), 3000);

            // Review & revisit ---------------------------------------------------
            add("review tab", () => { Tab(2); }, 500);
            add("review shown", () =>
            {
                var findings = Field<ListView>("_findings");
                foreach (ListViewItem item in findings.Items)
                    Log("   finding: " + string.Join(" | ", item.SubItems.Cast<ListViewItem.ListViewSubItem>().Select(x => x.Text).ToArray()));
                Log("   summary: " + Field<Label>("_summary").Text);
                Shot("08-review");
                Tab(3);
            }, 1000);
            add("revisit tab", () =>
            {
                Log("   revisit:\r\n" + Field<TextBox>("_revisit").Text);
                Check(Field<TextBox>("_revisit").Text.Contains("WL ???"), "unreadable WL line on the revisit list");
                Shot("09-revisit");
                Tab(0);
            }, 1000);

            // Stale data -----------------------------------------------------------
            add("survey revision: move 1046", () =>
            {
                var doc = AcApp.DocumentManager.MdiActiveDocument;
                using (doc.LockDocument())
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    foreach (AcDb.ObjectId id in CivApp.CivilApplication.ActiveDocument.CogoPoints)
                    {
                        var cp = (CivDb.CogoPoint)tr.GetObject(id, AcDb.OpenMode.ForWrite);
                        if (cp.PointNumber != 1046) continue;
                        cp.Easting += 0.5;
                        cp.Elevation += 0.10;
                    }
                    tr.Commit();
                }
                Tab(2);
                Click("Check for changed survey points / rebuild...");
                Send("Yes ");
            }, 500);
            add("rebuilt", () =>
            {
                var labels = Texts(20);
                Log("   pipe labels: " + labels);
                Check(labels.Contains("12\" RCP SD @ 0.55%"), "rebuild through the window updated the slope to 0.55%");
                Check(Math.Abs(S("1046").Cad.Rim - 330.20) < 0.001, "1046 snapshot now rim 330.20");
                Tab(0);
            }, 4000);

            // DPI simulation (fonts as they grow at 125% / 150%) -------------------
            add("100%, 125% and 150% text size", () =>
            {
                RenderCopy("10-text-100", 1.0f, true);
                RenderCopy("10-text-125", 1.25f, true);
                RenderCopy("10-text-150", 1.5f, true);
            }, 500);

            // Save, reopen, verify ---------------------------------------------------
            add("save", () => Send("_.QSAVE "), 500);
            add("close and reopen", () =>
            {
                var doc = AcApp.DocumentManager.MdiActiveDocument;
                var path = doc.Name;
                Autodesk.AutoCAD.ApplicationServices.DocumentExtension.CloseAndDiscard(doc);
                Autodesk.AutoCAD.ApplicationServices.DocumentCollectionExtension.Open(AcApp.DocumentManager, path, false);
            }, 3000);
            add("reload the window from the reopened drawing", () =>
            {
                Click("Reload from drawing");
            }, 8000);
            // Drafted pipes and the leader now pass through the point; freeze their
            // layers so the pick lands on the COGO point, as a drafter would.
            add("freeze pipe layers", () => SetFrozen(true), 1000);
            add("pick 1045 in the reopened drawing", () => PickPoint("Select structure point...", 5000, 5000), 3000);
            add("thaw pipe layers", () => SetFrozen(false), 1000);
            add("persisted", () =>
            {
                Log("   project loaded: " + (Project != null) + ", structures " + (Project == null ? 0 : Project.Structures.Count) +
                    ", point info: " + Field<Label>("_pointInfo").Text);
                if (S("1045") == null) { Fail("1045 not in the reloaded dip data"); return; }
                var grid = Field<DataGridView>("_grid");
                Check(grid.Rows.Count == 5, "after reopening the window shows 1045's 5 pipes");
                Check(StatusOf("1045", 0) == "Confirmed->1046" && StatusOf("1045", 1) == "Confirmed->1047" && StatusOf("1045", 2) == "LeftUnresolved->?",
                      "connections persisted");
                Check(S("1045").Field.Pipes[1].ReferenceBasis == FU.ReferenceBasis.FieldNoteConvention, "office-default invert basis persisted");
                Check(S("1045").Field.Pipes[0].MeasuredDip == 6.41 && S("1045").Field.Pipes[0].RawText == "12 RCP N 6.41 INV", "observation unchanged after reopen");
                var c = Counts();
                Check(c["pipe"] == 2 && c["structurelabel"] == 1 && c["pipelabel"] == 2, "drafting persisted (" + c["pipe"] + " pipes, " + c["pipelabel"] + " labels, " + c["structurelabel"] + " leader)");
                Shot("11-reopened");
            }, 3500);

            add("map tab shows every structure", () =>
            {
                Tab(1);
                var map = Field<Control>("_projectMap");
                Check(map != null && map.Width > 400 && map.Height > 250, "Map tab shows the project map (" + (map == null ? "missing" : map.Size.ToString()) + ")");
                Log("   map summary: " + Field<Label>("_projectSummary").Text);
                Shot("12-map");
                Click("Label all structures");
            }, 1500);
            add("label all placed the missing labels", () =>
            {
                var c = Counts();
                Check(c["structurelabel"] == 3, "Label all structures added leaders for 1046 and 1047 and kept 1045's (" + c["structurelabel"] + " leaders)");
                Tab(0);
            }, 4000);

            }

            if (easements)
            {
            // ------------------------------------------------ strip easement trimming
            // A markup like the drafter's sketch: a top and a bottom lot line, a side lot
            // line, one easement running from the top line to the bottom line, and one
            // that overshoots the top line and crosses the side line.
            add("seed lot lines", () =>
            {
                SeedLotLines();
                Send("_.ZOOM _E ");
            }, 1500);
            add("easement ending on two lot lines", () =>
                Send("STRIPEASEMENT  6150,5325 6146,5311 6140,5200 6150,5050 6120,4836.25  Centered 20  6100,5350 6050,4862.5   "), 2500);
            s.Add(new Step { Name = "preview: ends on lot lines", WhileBusy = true, Delay = 2500, Run = () =>
            {
                var form = Preview;
                Check(form != null, "STRIPEASEMENT opens the preview window");
                if (form == null) return;
                var split = PreviewSplit(form);
                var pieces = (System.Collections.IList)split.GetType().GetProperty("Pieces").GetValue(split, null);
                Log("   pieces: " + pieces.Count);
                Check(pieces.Count >= 1, "the trim lines divide the strip into pieces (" + pieces.Count + ")");
                Check(FindButton(form, "Draw easement").Enabled, "Draw easement is available with the largest piece kept");
                ShotForm(form, "20-easement-preview-ends");
                ClickOn(form, "Draw easement");
            } });
            s.Add(new Step { Name = "answer the line table location", WhileBusy = true, AfterPreview = true, Delay = 2000, Run = () =>
            {
                var doc = AcApp.DocumentManager.MdiActiveDocument;
                Check(!string.IsNullOrEmpty(doc.CommandInProgress), "the short first course asks where the line table goes");
                if (!string.IsNullOrEmpty(doc.CommandInProgress)) Send(" ");
            } });
            add("easement A stored and inside the lot lines", () =>
            {
                LogRunError();
                var r = Easements().LastOrDefault(x => !x.IsTemporary);
                Check(r != null, "the easement is stored");
                if (r == null) return;
                Log("   " + r.Title + " area " + r.AreaSquareFeet.ToString("0.00", CultureInfo.InvariantCulture) + ", " + r.BoundaryCourses.Count + " courses");
                Check(r.BoundaryCourses.All(c => c.Course.Start.Y <= Top(c.Course.Start.X) + 0.01 && c.Course.Start.Y >= Bottom(c.Course.Start.X) - 0.01),
                      "every corner lies between the top and bottom lot lines");
                Check(r.BoundaryCourses.Count(c => OnTop(c.Course)) == 1 && r.BoundaryCourses.Count(c => OnBottom(c.Course)) == 1,
                      "the easement meets each lot line along a single course");
                var drawn = EasementTexts(r.Id);
                foreach (var line in drawn) Log("   drawn: " + line);
                Check(drawn.Any(x => x.StartsWith("TABLE LINE TABLE | LINE NO. | DISTANCE | BEARING | L1 |", StringComparison.Ordinal)),
                      "the short first course is L1 in a LINE NO. | DISTANCE | BEARING table");
                Check(drawn.Any(x => x.StartsWith("TABLE", StringComparison.Ordinal) && !x.Contains("%%") && !x.Contains("\\U+") && x.Contains("\u00B0")), "table bearings show a real degree sign");
                Check(drawn.Any(x => (x.StartsWith("TEXT N", StringComparison.Ordinal) || x.StartsWith("TEXT S", StringComparison.Ordinal)) && (x.Contains("\"E ") || x.Contains("\"W "))),
                      "longer courses are labelled on the line with exhibit bearings");
                Check(drawn.Contains("LEADER POINT OF BEGINNING") && drawn.Contains("LEADER POINT OF TERMINUS"), "Point of Beginning and terminus leaders are drawn");
                Check(drawn.Any(x => x.Contains("APPROX. UTILITY EASEMENT AREA = ") && x.EndsWith(" SF", StringComparison.Ordinal)), "the area reads APPROX. ... EASEMENT AREA = ... SF");
            }, 4000);

            add("easement overshooting the lot lines", () =>
                Send("STRIPEASEMENT 6000,5400 6300,5300 6280,4776.25  Centered 10 20 6100,5350 6290,5011  6250,4787 "), 2500);
            s.Add(new Step { Name = "preview: pick the piece inside the lot", WhileBusy = true, Delay = 2500, Run = () =>
            {
                var form = Preview;
                Check(form != null, "the preview opens for the overshooting easement");
                if (form == null) return;
                var split = PreviewSplit(form);
                var pieces = (System.Collections.IList)split.GetType().GetProperty("Pieces").GetValue(split, null);
                var trimCount = ((System.Collections.IList)PreviewField(form, "Trims")).Count;
                Log("   trim lines picked: " + trimCount);
                Check(pieces.Count >= 3, "top line and side line cut the strip into " + pieces.Count + " pieces");
                Check(PreviewField(form, "TemporarySplit") != null, "the preview carries the 20' temporary construction easement");
                Check(PreviewField(form, "Commencement") != null && PreviewField(form, "TerminusCorner") != null, "the preview knows the Point of Commencement and the terminus corner");
                ShotForm(form, "21-easement-preview-overshoot-default");

                var inside = split.GetType().GetMethod("PieceAt").Invoke(split, new object[] { new FieldCodes.Easements.P2(6297, 5150) });
                Check(inside != null, "a piece contains the point inside the lot");
                if (inside == null) return;
                var number = (int)inside.GetType().GetProperty("Number").GetValue(inside, null);
                var keep = (HashSet<int>)form.GetType().GetField("_keep", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form);
                keep.Clear();
                keep.Add(number);
                form.GetType().GetMethod("Recalculate", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(form, new object[] { true });
                var purpose = (TextBox)form.GetType().GetField("_purpose", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form);
                purpose.Text = "drainage";
                Check(FindButton(form, "Draw easement").Enabled, "Draw easement is available with piece " + number + " kept");
                ShotForm(form, "22-easement-preview-overshoot-picked");
                ClickOn(form, "Draw easement");
            } });
            add("easement B trimmed to the top and side lot lines", () =>
            {
                LogRunError();
                var r = Easements().LastOrDefault(x => !x.IsTemporary);
                Check(r != null && r.Title.Contains("DRAINAGE"), "the purpose typed in the preview is in the title (" + (r == null ? "none" : r.Title) + ")");
                if (r == null) return;
                Log("   " + r.Title + " area " + r.AreaSquareFeet.ToString("0.00", CultureInfo.InvariantCulture) + ", " + r.BoundaryCourses.Count + " courses");
                Check(r.BoundaryCourses.All(c => c.Course.Start.Y <= Top(c.Course.Start.X) + 0.01 && SideOfR(c.Course.Start) <= 0.01),
                      "every corner lies below the top line and inside the side lot line");
                Check(r.KeepPoints != null && r.KeepPoints.Count == 1 && r.TrimLines.Count == 2, "the kept piece and both trim lines are remembered");
                var all = Easements();
                var temporary = all.FirstOrDefault(x => x.IsTemporary && x.GroupId == r.GroupId);
                Check(temporary != null && temporary.Title.StartsWith("20.00' WIDE TEMPORARY CONSTRUCTION", StringComparison.Ordinal) && temporary.AreaSquareFeet > r.AreaSquareFeet,
                      "the 20' temporary construction easement is stored with the drainage easement (" + (temporary == null ? "missing" : temporary.Title + ", " + temporary.AreaSquareFeet.ToString("0.00", CultureInfo.InvariantCulture)) + ")");
                if (temporary != null)
                    Check(temporary.BoundaryCourses.All(c => c.Course.Start.Y <= Top(c.Course.Start.X) + 0.01 && SideOfR(c.Course.Start) <= 0.01),
                          "the temporary easement is trimmed to the same lot lines");
                Check(r.CommencementTie != null && r.CommencementAlong != null,
                      "the tie from the Point of Commencement runs along the top lot line (" + (r.CommencementTie == null ? "none" : r.CommencementTie.Length.ToString("0.00", CultureInfo.InvariantCulture) + "'") + ")");
                Check(r.EndsOn != null && r.TerminusTie != null, "the terminus is on the side lot line and tied to its corner");
            }, 4000);
            add("draft legal description for easement B", () => Send("FTFEASEMENTLEGAL 6299.272,5149.918 "), 2500);
            s.Add(new Step { Name = "fill in the legal description window", WhileBusy = true, Modal = "LegalDescriptionForm", Delay = 2500, Run = () =>
            {
                var form = ModalForm("LegalDescriptionForm");
                Check(form != null, "FTFEASEMENTLEGAL opens the draft window");
                if (form == null) return;
                var names = new Dictionary<string, string>
                {
                    { "That portion of", "LOT 2, TEST SHORT PLAT NO. 1, BEING A PORTION OF SECTION 27, TOWNSHIP 28 NORTH, RANGE 5 EAST, W.M." },
                    { "Commencing at", "THE NORTHWEST CORNER OF SAID LOT 2" },
                    { "Point of Beginning is on", "THE NORTH LINE OF SAID LOT 2" },
                    { "Terminus is on", "THE EAST LINE OF SAID LOT 2" },
                    { "Terminus is tied to", "THE SOUTHEAST CORNER OF SAID LOT 2" },
                    { "County", "SNOHOMISH" }
                };
                var boxes = (System.Collections.IEnumerable)form.GetType().GetField("_boxes", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form);
                var captions = new List<string>();
                foreach (var pair in boxes)
                {
                    var key = pair.GetType().GetProperty("Key").GetValue(pair, null);
                    var caption = (string)key.GetType().GetField("Caption").GetValue(key);
                    captions.Add(caption);
                    var box = (TextBox)pair.GetType().GetProperty("Value").GetValue(pair, null);
                    string value;
                    if (names.TryGetValue(caption, out value)) box.Text = value;
                }
                Log("   blanks: " + string.Join(", ", captions.ToArray()));
                Check(captions.Contains("Commencing at") && captions.Contains("Terminus is tied to") && !captions.Contains("Beginning at"),
                      "the window asks only for the blanks this easement uses");
                var draft = ((TextBox)form.GetType().GetField("_draft", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form)).Text;
                Check(draft.Contains("TOGETHER WITH A 20.00 FOOT WIDE TEMPORARY CONSTRUCTION EASEMENT") && draft.Contains("COMMENCING AT THE NORTHWEST CORNER OF SAID LOT 2;") &&
                      draft.Contains("THENCE ALONG THE NORTH LINE OF SAID LOT 2, ") && draft.Contains("TO WHICH THE SOUTHEAST CORNER OF SAID LOT 2 BEARS") &&
                      !draft.Contains("["),
                      "the draft fills in as the names are typed, with nothing left blank");
                ShotForm(form, "23-legal-description");
                ClickOn(form, "Save draft");
            } });
            add("legal description saved", () =>
            {
                var r = Easements().LastOrDefault(x => !x.IsTemporary);
                Check(r != null && r.Legal != null && r.Legal.County == "SNOHOMISH" && r.LegalStatus.StartsWith("DRAFT", StringComparison.Ordinal),
                      "the names and draft status are stored with the easement");
                var file = Directory.GetFiles(Path.GetDirectoryName(_drawingPath), "*DRAINAGE*.legal-draft.txt").FirstOrDefault();
                Check(file != null, "the draft is written beside the drawing (" + (file == null ? "none" : Path.GetFileName(file)) + ")");
            }, 3000);
            add("undo easement B", () => Send("_.U _.U "), 1500);
            add("undo removed easement B's drafting", () =>
            {
                Check(Easements().Count == 1, "undo leaves only the first easement (" + Easements().Count + ")");
            }, 3000);

            // ------------------------------------------------ exhibit and inspectors
            add("exhibit for easement A", () => Send("FTFEXHIBIT 6153.31,5150.67  "), 2500);
            s.Add(new Step { Name = "fill in the exhibit window", WhileBusy = true, Modal = "ExhibitInfoForm", Delay = 2500, Run = () =>
            {
                var form = ModalForm("ExhibitInfoForm");
                Check(form != null, "FTFEXHIBIT opens the exhibit information window");
                if (form == null) return;
                var boxes = Descendants(form).OfType<TextBox>().ToList();
                Check(boxes.Count == 15, "the window has the 15 information fields, title block ones included (" + boxes.Count + ")");
                Check(FindButton(form, "Create exhibit") != null && FindButton(form, "Create exhibit").Enabled, "nothing is required before Create exhibit");
                if (boxes.Count == 15)
                {
                    boxes[1].Text = "NE 1/4 OF SECTION 27, TOWNSHIP 28 N, RANGE 5 E, W.M.";
                    boxes[4].Text = "UI TEST OWNER";
                    boxes[5].Text = "280527001";
                    boxes[11].Text = "2169171001";
                    boxes[13].Text = "UIT";
                }
                ShotForm(form, "24-exhibit-information");
                ClickOn(form, "Create exhibit");
            } });
            add("exhibit layout made", () =>
            {
                LogRunError();
                var x = Exhibits().LastOrDefault();
                Check(x != null, "the exhibit is stored in the drawing");
                if (x == null) return;
                Log("   " + x.LayoutName + ": 1\" = " + x.Scale + "', " + x.Items.Count + " items, " + x.Review.Count + " review item(s)");
                foreach (var r in x.Review) Log("   review: " + r);
                Check(AcDb.LayoutManager.Current.CurrentLayout == x.LayoutName, "FTFEXHIBIT opens the new layout (" + AcDb.LayoutManager.Current.CurrentLayout + ")");
                Check(x.Items.Any(i => i.Key == "NORTH") && x.Items.Any(i => i.Key == "SCALEBAR") && x.Items.Any(i => i.Key == "TITLE") && x.Items.Any(i => i.Key.StartsWith("AREA:", StringComparison.Ordinal)),
                      "north arrow, scale bar, title and area label are on the sheet");
                Check(x.Info.Owner == "UI TEST OWNER" && x.Info.Apn == "280527001" && x.Info.Location.StartsWith("NE 1/4", StringComparison.Ordinal)
                      && x.Info.ProjectNumber == "2169171001" && x.Info.CheckedBy == "UIT", "what was typed in the window is stored with the exhibit");
                Check(!x.Review.Any(r => r.Severity == "Error"), "the default sheet has no errors for a single strip easement");
            }, 5000);
            add("inspect this exhibit layout", () => Send("FTFEXHIBITINSPECT  "), 2500);
            add("exhibit inspector, back to the easement", () =>
            {
                var form = ModalForm("InspectorForm");
                Check(form != null, "FTFEXHIBITINSPECT opens the inspector for the current layout");
                if (form == null) return;
                var list = (ListView)form.GetType().GetField("_list", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form);
                var groups = Sections(list);
                Log("   " + list.Items.Count + " rows in " + string.Join(", ", groups.ToArray()));
                Check(groups.Contains("Exhibit") && groups.Contains("Easements") && groups.Contains("Sheet"), "the exhibit inspector lists the exhibit, its easements and the sheet items");
                Check(!list.Items.Cast<ListViewItem>().Any(i => i.SubItems.Count > 1 && i.SubItems[1].Text.Contains("by hand")),
                      "nothing on a freshly built exhibit is reported as moved or edited by hand");
                ShotForm(form, "25-exhibit-inspector");
                var easementRow = list.Items.Cast<ListViewItem>().FirstOrDefault(i => (string)RowField(i, "Section") == "Easements" && RowField(i, "Handle") != null);
                Check(easementRow != null, "the easement row points at the easement outline");
                if (easementRow == null) { form.Close(); return; }
                easementRow.Selected = true;
                form.GetType().GetMethod("GoToSelected", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(form, null);
                var status = (Label)form.GetType().GetField("_status", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form);
                Check(AcDb.LayoutManager.Current.CurrentLayout == "Model" && status.Text.StartsWith("Showing", StringComparison.Ordinal),
                      "Zoom to source goes from the sheet back to the easement in model space (" + AcDb.LayoutManager.Current.CurrentLayout + ": " + status.Text + ")");
                form.Close();
            }, 3000);
            add("inspect easement A", () => Send("FTFEASEMENTINSPECT 6153.31,5150.67 "), 2500);
            add("easement inspector, over to the exhibit", () =>
            {
                var form = ModalForm("InspectorForm");
                Check(form != null, "FTFEASEMENTINSPECT opens the inspector");
                if (form == null) return;
                var list = (ListView)form.GetType().GetField("_list", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form);
                var groups = Sections(list);
                Log("   " + list.Items.Count + " rows in " + string.Join(", ", groups.ToArray()));
                Check(groups.Contains("Sources") && groups.Contains("Closure") && groups.Contains("Legal draft"), "the easement inspector shows sources, closure and legal status");
                ShotForm(form, "26-easement-inspector");
                var shown = list.Items.Cast<ListViewItem>().FirstOrDefault(i => (string)RowField(i, "Item") == "Shown on");
                var x = Exhibits().LastOrDefault();
                Check(shown != null && x != null && ((string)RowField(shown, "Value")).StartsWith(x.LayoutName, StringComparison.Ordinal), "the easement lists the exhibit it is shown on");
                if (shown == null || x == null) { form.Close(); return; }
                shown.Selected = true;
                form.GetType().GetMethod("GoToSelected", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(form, null);
                Check(AcDb.LayoutManager.Current.CurrentLayout == x.LayoutName, "Zoom to source goes from the easement to its exhibit layout (" + AcDb.LayoutManager.Current.CurrentLayout + ")");
                form.Close();
            }, 3000);

            }

            add("done", () =>
            {
                Log("");
                Log("UI TEST DONE: " + _pass + " passed, " + _fail + " failed");
                _log.Flush();
            }, 500);
            return s;
        }

        // Top lot line y = 5400 - 0.5 (x - 6000); bottom y = 4900 - 0.375 (x - 5950);
        // side lot line from (6330, 5235) to (6250, 4787).
        private static double Top(double x) { return 5400 - 0.5 * (x - 6000); }
        private static double Bottom(double x) { return 4900 - 0.375 * (x - 5950); }
        private static bool OnTop(FieldCodes.Easements.Course c) { return Math.Abs(c.Start.Y - Top(c.Start.X)) < 0.01 && Math.Abs(c.End.Y - Top(c.End.X)) < 0.01; }
        private static bool OnBottom(FieldCodes.Easements.Course c) { return Math.Abs(c.Start.Y - Bottom(c.Start.X)) < 0.01 && Math.Abs(c.End.Y - Bottom(c.End.X)) < 0.01; }
        private static double SideOfR(FieldCodes.Easements.P2 p)
        {
            // Positive on the far (east) side of the side lot line, looking along it southward.
            double dx = 6250 - 6330, dy = 4787 - 5235;
            return (dx * (p.Y - 5235) - dy * (p.X - 6330)) / Math.Sqrt(dx * dx + dy * dy) * Math.Sign(dx * (5150 - 5235) - dy * (6297 - 6330)) * -1;
        }

        /// <summary>Text, leader and table contents drafted for one easement, as "KIND text".</summary>
        private static List<string> EasementTexts(string id)
        {
            var list = new List<string>();
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            using (var tr = doc.Database.TransactionManager.StartOpenCloseTransaction())
            {
                var ms = (AcDb.BlockTableRecord)tr.GetObject(AcDb.SymbolUtilityServices.GetBlockModelSpaceId(doc.Database), AcDb.OpenMode.ForRead);
                foreach (AcDb.ObjectId oid in ms)
                {
                    var e = tr.GetObject(oid, AcDb.OpenMode.ForRead) as AcDb.Entity;
                    if (e == null) continue;
                    var rb = e.GetXDataForApplication(FtfApp);
                    if (rb == null) continue;
                    var owned = rb.AsArray().Any(v => Convert.ToString(v.Value) == id);
                    rb.Dispose();
                    if (!owned) continue;
                    var mt = e as AcDb.MText;
                    if (mt != null) list.Add("TEXT " + mt.Text.Replace("\r\n", " / "));
                    var ml = e as AcDb.MLeader;
                    if (ml != null && ml.MText != null) list.Add("LEADER " + ml.MText.Text);
                    var tb = e as AcDb.Table;
                    if (tb != null)
                    {
                        var cells = new List<string>();
                        for (var r = 0; r < tb.Rows.Count; r++)
                            for (var c = 0; c < tb.Columns.Count; c++)
                                if (!string.IsNullOrEmpty(tb.Cells[r, c].TextString)) cells.Add(tb.Cells[r, c].TextString);
                        list.Add("TABLE " + string.Join(" | ", cells.ToArray()));
                    }
                }
            }
            return list;
        }

        private static void SeedLotLines()
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var ms = (AcDb.BlockTableRecord)tr.GetObject(AcDb.SymbolUtilityServices.GetBlockModelSpaceId(doc.Database), AcDb.OpenMode.ForWrite);
                foreach (var pair in new[]
                {
                    new[] { 6000.0, 5400, 6400, 5200 },
                    new[] { 5950.0, 4900, 6350, 4750 },
                    new[] { 6330.0, 5235, 6250, 4787 }
                })
                {
                    var line = new AcDb.Line(new Point3d(pair[0], pair[1], 0), new Point3d(pair[2], pair[3], 0));
                    ms.AppendEntity(line);
                    tr.AddNewlyCreatedDBObject(line, true);
                }
                tr.Commit();
            }
        }

        private static void LogRunError()
        {
            var detail = Plugin.GetType("FieldCodes.Cad.FtfSession").GetProperty("LastRunErrorDetail").GetValue(null, null) as string;
            if (!string.IsNullOrEmpty(detail)) Log("   COMMAND ERROR: " + detail.Replace("\n", "\n      "));
        }

        private static object PreviewField(Form form, string name)
        {
            var preview = form.GetType().GetField("_preview", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form);
            return preview.GetType().GetField(name).GetValue(preview);
        }

        private static object PreviewSplit(Form form)
        {
            var preview = form.GetType().GetField("_preview", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form);
            return preview.GetType().GetField("Split").GetValue(preview);
        }

        private static IList<FieldCodes.Easements.EasementRecord> Easements()
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            using (var tr = doc.Database.TransactionManager.StartOpenCloseTransaction())
            {
                var load = Plugin.GetType("FieldCodes.Cad.DrawingStore").GetMethod("LoadEasements", BindingFlags.Public | BindingFlags.Static);
                return ((IList<FieldCodes.Easements.EasementRecord>)load.Invoke(null, new object[] { doc.Database, tr })).OrderBy(r => r.CreatedUtc).ToList();
            }
        }

        private static IList<FieldCodes.Exhibits.ExhibitRecord> Exhibits()
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            using (var tr = doc.Database.TransactionManager.StartOpenCloseTransaction())
            {
                var load = Plugin.GetType("FieldCodes.Cad.DrawingStore").GetMethod("LoadExhibits", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                return ((IList<FieldCodes.Exhibits.ExhibitRecord>)load.Invoke(null, new object[] { doc.Database, tr })).OrderBy(r => r.CreatedUtc).ToList();
            }
        }

        private static IEnumerable<Control> Descendants(Control root)
        {
            foreach (Control c in root.Controls)
            {
                yield return c;
                foreach (var inner in Descendants(c)) yield return inner;
            }
        }

        private static List<string> Sections(ListView list)
        {
            return list.Items.Cast<ListViewItem>().Where(i => i.Tag != null).Select(i => (string)RowField(i, "Section")).Distinct().ToList();
        }

        private static object RowField(ListViewItem item, string name)
        {
            return item.Tag == null ? null : item.Tag.GetType().GetField(name).GetValue(item.Tag);
        }

        private static void ClickOn(Form form, string text)
        {
            var b = FindButton(form, text);
            if (b == null) { Fail("no button \"" + text + "\""); return; }
            typeof(Button).GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(b, new object[] { EventArgs.Empty });
        }

        private static void ShotForm(Form form, string name)
        {
            try
            {
                using (var bmp = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));
                    bmp.Save(Path.Combine(OutDir, name + ".png"), ImageFormat.Png);
                }
                Log("   screenshot " + name + ".png (" + form.Width + "x" + form.Height + ")");
            }
            catch (System.Exception ex) { Log("   screenshot failed: " + ex.Message); }
        }

        private static void SetFrozen(bool frozen)
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var table = (AcDb.LayerTable)tr.GetObject(doc.Database.LayerTableId, AcDb.OpenMode.ForRead);
                foreach (var name in new[] { "V-UTIL-STRM-E", "V-UTIL-STRM-TEXT-E" })
                    if (table.Has(name)) ((AcDb.LayerTableRecord)tr.GetObject(table[name], AcDb.OpenMode.ForWrite)).IsFrozen = frozen;
                tr.Commit();
            }
            doc.Editor.Regen();
        }

        private static void RenderCopy(string name, float scale, bool report)
        {
            var type = Plugin.GetType("FieldCodes.Cad.Ui.DipBuilderForm");
            var real = Window;
            var form = (Form)Activator.CreateInstance(type, true);
            try
            {
                if (scale != 1.0f)
                    form.Font = new Font(SystemFonts.MessageBoxFont.FontFamily, SystemFonts.MessageBoxFont.Size * scale);
                var id = type.GetField("_structureId", BindingFlags.NonPublic | BindingFlags.Instance);
                id.SetValue(form, id.GetValue(real));
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-4000, 0);
                form.Show();
                type.GetMethod("RefreshFromSession").Invoke(form, null);
                Application.DoEvents();
                using (var bmp = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));
                    bmp.Save(Path.Combine(OutDir, name + ".png"), ImageFormat.Png);
                }
                Log("   screenshot " + name + ".png (off-screen copy, " + form.Width + "x" + form.Height + ")");
                if (report)
                {
                    ReportClipping(form, scale);
                    var grid = (DataGridView)type.GetField("_grid", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form);
                    var list = (ListView)type.GetField("_connections", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form);
                    var preview = (TextBox)type.GetField("_labelPreview", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form);
                    Log("   sizes at " + (int)(scale * 100) + "%: grid " + grid.Size + ", candidates " + list.Size + ", preview " + preview.Size);
                    Check(grid.Height >= 120 && grid.Width >= 500, "pipe grid usable at " + (int)(scale * 100) + "% (" + grid.Size + ")");
                    Check(list.Height >= 80 && list.Width >= 500, "connections list usable at " + (int)(scale * 100) + "% (" + list.Size + ")");
                    Check(preview.Height >= 80 && preview.Width >= 250, "leader preview usable at " + (int)(scale * 100) + "% (" + preview.Size + ")");
                }
            }
            finally
            {
                form.Hide();
                form.Dispose();
                SessionType.GetField("Form").SetValue(null, real);
            }
        }

        /// <summary>Reports any control whose bottom or right edge falls outside its
        /// parent's client area -- what a user sees as clipped.</summary>
        private static void ReportClipping(Form form, float scale)
        {
            var clipped = new List<string>();
            Walk(form, clipped);
            Log("   at " + (int)(scale * 100) + "% text: " + (clipped.Count == 0 ? "no clipped controls" : clipped.Count + " clipped control(s)"));
            foreach (var c in clipped.Take(25)) Log("      clipped: " + c);
            Check(clipped.Count == 0, "no clipped controls at " + (int)(scale * 100) + "% text size");
        }

        private static void Walk(Control parent, List<string> clipped)
        {
            if (parent is DataGridView || parent is ListView) return;
            var scrolls = parent is ScrollableControl && ((ScrollableControl)parent).AutoScroll;
            foreach (Control c in parent.Controls)
            {
                if (!c.Visible) continue;
                var where = (parent is GroupBox ? ((GroupBox)parent).Text : parent.Parent is GroupBox ? ((GroupBox)parent.Parent).Text : parent.GetType().Name);
                // The tab control is deliberately a few pixels oversize so its stock frame is hidden.
                if (!scrolls && !(parent is TabControl) && !(parent is Form) && !(c is TabControl) &&
                    (c.Bottom > parent.ClientSize.Height + 1 || c.Right > parent.ClientSize.Width + 1))
                    clipped.Add(c.GetType().Name + " \"" + c.Text + "\" in \"" + where + "\" extends to " + c.Right + "x" + c.Bottom + " of " + parent.ClientSize.Width + "x" + parent.ClientSize.Height);
                var flow = c as FlowLayoutPanel;
                if (flow != null)
                {
                    var need = flow.GetPreferredSize(new Size(flow.Width, 0)).Height;
                    if (need > flow.Height + 1)
                        clipped.Add("row in \"" + where + "\" needs " + need + "px, has " + flow.Height + "px");
                }
                var grid = c as DataGridView;
                if (grid != null && grid.Rows.Count >= 3 && grid.DisplayedRowCount(false) < 3)
                    clipped.Add("pipe grid shows only " + grid.DisplayedRowCount(false) + " full row(s)");
                Walk(c, clipped);
            }
        }
    }
}
