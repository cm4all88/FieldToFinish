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

        /// <summary>Selects the pipe as a drafter does: by clicking its card.</summary>
        private static void SelectPipeRow(int row)
        {
            var card = Card(row);
            if (card == null) { Fail("no pipe card " + row); return; }
            card.GetType().GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(card, new object[] { EventArgs.Empty });
        }

        // The Add pipe panel and the pipe cards ------------------------------------

        /// <summary>Whether a control is set to show, whether or not Civil 3D (and so the window) is in front.</summary>
        private static bool Shown(Control c)
        {
            return c != null && (bool)typeof(Control).GetMethod("GetState", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(c, new object[] { 2 });
        }

        private static Control Quick { get { return Field<Control>("_quick"); } }

        private static Control Compass { get { return QuickField<Control>("_compass"); } }

        private static string CompassSelected { get { return (string)Compass.GetType().GetProperty("Selected").GetValue(Compass, null); } }

        private static Point CompassPoint(string name)
        {
            return (Point)Compass.GetType().GetMethod("PointFor").Invoke(Compass, new object[] { name });
        }

        /// <summary>A left click on the compass at this point, through the same handler the mouse uses.</summary>
        private static void ClickCompassAt(Point p)
        {
            Compass.GetType().GetMethod("OnMouseClick", BindingFlags.NonPublic | BindingFlags.Instance)
                   .Invoke(Compass, new object[] { new MouseEventArgs(MouseButtons.Left, 1, p.X, p.Y, 0) });
        }

        private static void ClickCompass(string name) { ClickCompassAt(CompassPoint(name)); }

        private static void ClickCompassAtAzimuth(double azimuth)
        {
            var c = Compass;
            var r = Math.Min(c.Width, c.Height) * 0.3;
            var a = azimuth * Math.PI / 180.0;
            ClickCompassAt(new Point((int)Math.Round(c.Width / 2.0 + Math.Sin(a) * r), (int)Math.Round(c.Height / 2.0 - Math.Cos(a) * r)));
        }

        private static T QuickField<T>(string name) where T : class
        {
            var q = Quick;
            return q.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(q) as T;
        }

        private static FU.QuickPipeEntry QuickEntry
        {
            get { return (FU.QuickPipeEntry)Quick.GetType().GetProperty("Current").GetValue(Quick, null); }
        }

        private static List<Control> Cards()
        {
            var list = Field<Control>("_cards");
            return list == null ? new List<Control>() : list.Controls.Cast<Control>().Where(c => c.GetType().Name == "PipeCard").ToList();
        }

        private static Control Card(int index)
        {
            var cards = Cards();
            return index < cards.Count ? cards[index] : null;
        }

        private static string Headline(int index)
        {
            var card = Card(index);
            return card == null ? "(no card)" : ((Label)card.GetType().GetField("Headline").GetValue(card)).Text;
        }

        private static string Detail(int index)
        {
            var card = Card(index);
            return card == null ? "(no card)" : ((Label)card.GetType().GetField("Detail").GetValue(card)).Text;
        }

        /// <summary>Clicks a button inside one part of the window (a card, the Add pipe panel).</summary>
        private static void ClickIn(Control root, string text)
        {
            var b = root == null ? null : FindButton(root, text);
            if (b == null) { Fail("no button \"" + text + "\" there"); return; }
            typeof(Button).GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(b, new object[] { EventArgs.Empty });
        }

        private static void Press(Button b)
        {
            if (b == null) { Fail("no such button"); return; }
            typeof(Button).GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(b, new object[] { EventArgs.Empty });
        }

        private static Button ButtonStarting(Control root, string prefix)
        {
            foreach (Control c in root.Controls)
            {
                var b = c as Button;
                if (b != null && b.Text.StartsWith(prefix, StringComparison.Ordinal)) return b;
                var inner = ButtonStarting(c, prefix);
                if (inner != null) return inner;
            }
            return null;
        }

        /// <summary>The texts of the buttons in one choice group of the Add pipe panel.</summary>
        private static List<string> Buttons(string panelField)
        {
            var panel = QuickField<Control>(panelField);
            return panel == null ? new List<string>() : panel.Controls.OfType<Button>().Select(b => b.Text).ToList();
        }

        private static bool Picked(Button b)
        {
            return (bool)Plugin.GetType("FieldCodes.Cad.Ui.PipeQuickEntry").GetMethod("IsPicked", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { b });
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
                var basicGrid = grid.Visible;
                var basicMove = FindButton(Window, "Move up").Visible;
                Check(FindButton(Window, "+ Add pipe").Visible, "+ Add pipe is on the Basic view");
                Shot("01b-basic");
                toggle.Checked = true;
                var advancedCols = grid.Columns.Cast<DataGridViewColumn>().Count(c => c.Visible);
                Check(FindButton(Window, "Move up").Visible && !basicMove, "Move up only shows in Advanced");
                Check(!basicGrid && grid.Visible && advancedCols == 14, "the pipe table is the Advanced view only (" + advancedCols + " columns there)");
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
                var cards = Cards();
                for (var i = 0; i < cards.Count; i++) Log("   card " + i + ": " + Headline(i) + "  |  " + Detail(i));
                Check(cards.Count == 5, "5 pipe cards");
                Check(Headline(0) == "N 12\" RCP IE 6.41" && Headline(2) == "E 8\" PVC TOP 5.23" && Headline(4) == "? 10\" RCP IE 5.5",
                      "cards read like the field book");
                var label = Field<TextBox>("_labelPreview").Text;
                Log("   label preview:\r\n" + label);
                Check(label.Contains("IE (N) = 322.01") && label.Contains("IE (SW) = 321.40") && label.Contains("TOP (E) = 323.19") &&
                      label.Contains("BOT = 320.60"), "live label built from the observations");
                Shot("03-notes-read");
            }, 3000);

            // Connections -------------------------------------------------------
            add("enter the manhole diameter", () =>
            {
                var box = Field<ComboBox>("_diameter");
                Check(box.Visible && !Field<TextBox>("_insideWidth").Visible, "SDMH shows a Diameter pick list, not the W x L box");
                var items = box.Items.Cast<object>().Select(Convert.ToString).ToList();
                Check(items.Take(5).SequenceEqual(new[] { "48", "54", "60", "72", "96" }) && items.Last() == "More...",
                      "diameter list: 48 54 60 72 96, then More...");
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

            // + Add pipe: direction -> size -> material -> MD / reference -> Add ------------------
            add("+ Add pipe at 1047 opens the panel", () =>
            {
                Click("+ Add pipe");
                Check(Shown(Quick), "+ Add pipe opens the quick-entry panel");
                Check(QuickField<Label>("_title").Text == "Add pipe at SDMH 1047", "panel says where the pipe is going (" + QuickField<Label>("_title").Text + ")");
                Check(Compass != null && Shown(Compass) && Compass.Width >= 200, "the direction is a compass to click (" + (Compass == null ? "missing" : Compass.Size.ToString()) + ")");
                Check(Field<Button>("_addPipe").Enabled == false, "+ Add pipe waits while the panel is open");
            }, 500);
            add("all 16 directions", () =>
            {
                var ok = 0;
                foreach (var name in FU.DirectionShortcuts.Names)
                {
                    ClickCompass(name);
                    var d = QuickEntry.Direction;
                    if (d != null && d.Text == name && d.AzimuthDegrees == FU.DirectionShortcuts.For(name).AzimuthDegrees && CompassSelected == name) ok++;
                    else Log("   direction " + name + " -> " + (d == null ? "null" : d.Text + " " + d.AzimuthDegrees));
                }
                Check(ok == 16, "clicking each of the 16 wedges sets that direction and shows it chosen (" + ok + "/16)");
                // A click anywhere in a wedge takes that direction: 10 degrees is still N, 12 is N/NE, 350 is N.
                var snapped = new[] { 10.0, 12.0, 350.0, 100.0 }.Select(az => { ClickCompassAtAzimuth(az); return QuickEntry.Direction.Text; }).ToArray();
                Check(snapped.SequenceEqual(new[] { "N", "N/NE", "N", "E" }), "clicks between labels snap to the nearest direction (" + string.Join(" ", snapped) + ")");
                ClickCompassAt(CompassPoint("?"));
                Check(QuickEntry.Direction != null && !QuickEntry.Direction.IsKnown && QuickEntry.Direction.Text == "?", "the centre records the direction as unknown (?)");
                ClickCompass("E");
            }, 300);
            add("usual sizes for an SDMH, then size, material, MD, Invert", () =>
            {
                var sizes = Buttons("_sizes");
                Log("   SDMH sizes: " + string.Join(" ", sizes));
                Check(sizes.SequenceEqual(new[] { "8\"", "10\"", "12\"", "15\"", "18\"", "24\"", "30\"", "36\"", "48\"", "Larger" }),
                      "an SDMH shows the storm sizes, then Larger");
                Check(!Shown(QuickField<TextBox>("_sizeTyped").Parent), "no typed size until Larger");
                var materials = Buttons("_materials");
                Log("   SDMH materials: " + string.Join(" ", materials));
                Check(materials.First() == "RCP" && materials.Last() == "More..." && !materials.Contains("VCP"), "storm materials first, then More...");
                ClickIn(Quick, "15\"");
                ClickIn(Quick, "RCP");
                QuickField<TextBox>("_dip").Text = "5.5";
                ClickIn(QuickField<Control>("_references"), "Invert");
                Check(QuickField<Label>("_preview").Text == "E 15\" RCP IE 5.5", "the panel previews E 15\" RCP IE 5.5 (" + QuickField<Label>("_preview").Text + ")");
                Shot("05b-add-pipe-panel");
                ClickIn(Quick, "Add + next");
            }, 500);
            add("added, panel ready for the next pipe", () =>
            {
                var st = S("1047");
                Check(st.Field.Pipes.Count == 2, "1047 now has 2 pipes (" + st.Field.Pipes.Count + ")");
                if (st.Field.Pipes.Count < 2) return;
                var p = st.Field.Pipes[1];
                Check(p.WidthIn == 15 && p.Material == "RCP" && p.Direction.Text == "E" && p.Direction.AzimuthDegrees == 90 && p.MeasuredDip == 5.5 &&
                      p.Reference == FU.MeasurementReference.Invert && p.ReferenceBasis == FU.ReferenceBasis.EnteredByDrafter && p.Source == FU.ObservationSource.UserEntry,
                      "E 15\" RCP 5.5 recorded as an invert entered by the drafter");
                Check(Shown(Quick) && QuickEntry.Direction == null && !QuickEntry.SizeIn.HasValue, "Add + next leaves the panel open and empty");
                Check(Headline(1) == "E 15\" RCP IE 5.5", "the new pipe shows as a card (" + Headline(1) + ")");
            }, 3000);
            add("an unlisted size and material, no reference", () =>
            {
                ClickCompass("N/NW");
                ClickIn(Quick, "Larger");
                var larger = Buttons("_sizes");
                Log("   larger: " + string.Join(" ", larger));
                Check(larger.Contains("54\"") && larger.Contains("6\"") && !larger.Contains("12\"") && larger.Last() == "Usual sizes",
                      "Larger shows the rest of the sizes, not the usual ones");
                Check(Shown(QuickField<TextBox>("_sizeTyped").Parent), "Larger offers a typed size");
                QuickField<TextBox>("_sizeTyped").Text = "17.5";
                ClickIn(Quick, "Use size");
                ClickIn(Quick, "More...");
                var more = Buttons("_materials");
                Log("   more materials: " + string.Join(" ", more));
                Check(more.Contains("VCP") && more.Contains("DIP") && !more.Contains("RCP") && more.Last() == "Usual materials", "More... shows the other office materials");
                QuickField<TextBox>("_materialTyped").Text = "ribbed pvc";
                ClickIn(Quick, "Use material");
                QuickField<TextBox>("_dip").Text = "6.41";
                Check(Picked(FindButton(QuickField<Control>("_references"), "Not stated")), "the reference starts as Not stated");
                Check(QuickField<Label>("_preview").Text == "N/NW 17.5\" RIBBED PVC 6.41 Unspecified", "preview: " + QuickField<Label>("_preview").Text);
                ClickIn(Quick, "Add pipe");
            }, 500);
            add("17.5 and the custom material kept; MD unspecified", () =>
            {
                var st = S("1047");
                Check(st.Field.Pipes.Count == 3, "1047 now has 3 pipes (" + st.Field.Pipes.Count + ")");
                if (st.Field.Pipes.Count < 3) return;
                var p = st.Field.Pipes[2];
                Check(p.WidthIn == 17.5 && p.HeightIn == 17.5, "17.5\" kept exactly (" + p.WidthIn + ")");
                Check(p.Material == "RIBBED PVC", "custom material kept (" + p.Material + ")");
                Check(p.Direction.Text == "N/NW" && p.Direction.AzimuthDegrees == 337.5, "N/NW recorded at 337.5");
                Check(p.Reference == FU.MeasurementReference.Unspecified && p.ReferenceBasis == FU.ReferenceBasis.NotStated,
                      "an MD with no reference stays Unspecified, even with the office invert convention on");
                Check(!Shown(Quick) && Field<Button>("_addPipe").Enabled, "Add pipe closes the panel");
                Check(Headline(2) == "N/NW 17.5\" RIBBED PVC 6.41 Unspecified", "card: " + Headline(2));
                Check(Field<TextBox>("_labelPreview").Text.Contains("(N/NW)"), "the structure label lists the N/NW pipe");
                Shot("05c-pipe-cards");
            }, 3000);

            // The buttons follow the structure -----------------------------------------------------
            add("change 1047 to a CB with the panel open", () =>
            {
                Click("+ Add pipe");
                Field<ComboBox>("_type").Text = "CB";
                Window.GetType().GetMethod("SaveStructureFields", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(Window, null);
            }, 500);
            add("CB buttons and W x L", () =>
            {
                var sizes = Buttons("_sizes");
                var materials = Buttons("_materials");
                Log("   CB sizes: " + string.Join(" ", sizes) + " | materials: " + string.Join(" ", materials));
                Check(sizes.SequenceEqual(new[] { "6\"", "8\"", "10\"", "12\"", "15\"", "18\"", "24\"", "Larger" }), "a CB shows catch basin sizes, 24\" included");
                Check(materials.First() == "PVC" && materials.Contains("CPEP"), "a CB shows catch basin materials");
                Check(Field<Label>("_choiceNote").Text == "Buttons for: Catch basins and inlets", Field<Label>("_choiceNote").Text);
                // The drafter's type now drives everything type-dependent; the field code stays as observed.
                Check(Shown(Field<TextBox>("_insideWidth")) && Shown(Field<TextBox>("_insideLength")) && !Shown(Field<ComboBox>("_diameter")),
                      "set to CB, 1047 asks for inside W x L like any CB");
                Check(Field<Label>("_structureTitle").Text == "CB 1047", "the title follows the drafter's type (" + Field<Label>("_structureTitle").Text + ")");
                Check(Field<Label>("_pointInfo").Text.Contains("Type CB set by drafter (field code SDMH)"), "the original field code is still shown");
                Check(S("1047").Field.FieldCode == "SDMH" && S("1047").TypeSetByDrafter, "the field code is unchanged; the type is marked as the drafter's");
                Check(Project.Overrides.Any(o => o.What == "Structure type set by drafter" && o.Entered == "CB"), "the type change is recorded as an override");
                Check(Field<TextBox>("_labelPreview").Text.StartsWith("CB 1047"), "the label header follows the type");
                Field<ComboBox>("_type").Text = "SDMH";
                Window.GetType().GetMethod("SaveStructureFields", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(Window, null);
            }, 3500);
            add("back to SDMH", () =>
            {
                Check(Buttons("_sizes").First() == "8\"", "back to the storm sizes for SDMH");
                Check(Shown(Field<ComboBox>("_diameter")) && !Shown(Field<TextBox>("_insideWidth")), "an SDMH (round) asks for a diameter again");
                Check(!S("1047").TypeSetByDrafter && Field<Label>("_structureTitle").Text == "SDMH 1047", "set back to its field code, 1047 is an SDMH again");
                ClickIn(Quick, "Cancel");
                Check(!Shown(Quick), "Cancel closes the panel without adding");
                Check(S("1047").Field.Pipes.Count == 3, "nothing added by Cancel");
            }, 3500);

            // Editing from the card ------------------------------------------------------------
            add("edit the E pipe: Top of pipe", () =>
            {
                ClickIn(Card(1), "Edit");
                Check(Shown(Quick) && FindButton(Quick, "Save pipe") != null, "Edit opens the panel on that pipe");
                Check(CompassSelected == "E" && Picked(FindButton(Quick, "15\"")) && Picked(FindButton(Quick, "RCP")),
                      "its direction, size and material show as picked");
                ClickIn(QuickField<Control>("_references"), "Top of pipe");
                ClickIn(Quick, "Save pipe");
            }, 500);
            add("edit saved like a table edit", () =>
            {
                var p = S("1047").Field.Pipes[1];
                Check(p.Reference == FU.MeasurementReference.TopOfPipe && p.ReferenceBasis == FU.ReferenceBasis.EnteredByDrafter && p.MeasuredDip == 5.5 && p.WidthIn == 15,
                      "now top of pipe, set by drafter; MD and size unchanged");
                Check(Project.Overrides.Any(o => o.Target == p.Id && o.What == "Measurement reference set by drafter"), "the reference change is recorded as an override");
                ClickIn(Card(2), "Edit");
                Check(QuickEntry.SizeIn == 17.5 && QuickField<TextBox>("_sizeTyped").Text == "17.5" && Shown(QuickField<TextBox>("_sizeTyped").Parent),
                      "the typed 17.5\" comes back into the panel's size box");
                Check(QuickField<TextBox>("_materialTyped").Text == "RIBBED PVC", "and the custom material into its box");
                ClickIn(QuickField<Control>("_references"), "Invert");
                ClickIn(Quick, "Save pipe");
            }, 3000);
            add("unspecified confirmed as invert", () =>
            {
                var p = S("1047").Field.Pipes[2];
                Check(p.Reference == FU.MeasurementReference.Invert && p.ReferenceBasis == FU.ReferenceBasis.ConfirmedByDrafter,
                      "choosing Invert for an unspecified MD confirms it (ConfirmedByDrafter), as the table does");
                Check(p.WidthIn == 17.5 && p.MeasuredDip == 6.41 && p.Material == "RIBBED PVC", "17.5\" and 6.41 unchanged by the edit");
            }, 3000);

            // Connects to... from the card, then walk the network ----------------------------
            add("N/NW pipe: Connects to... 1048", () =>
            {
                Send("_.ZOOM _C 5120,5000 30 ");
                ClickIn(Card(2), "Connects to...");
                Send("5120,5000 ");
                Send("UI TEST WALK\n");
            }, 800);
            add("manual connection on the card", () =>
            {
                Check(StatusOf("1047", 2) == "ManualOverride->1048", "Connects to... records a drafter's manual connection (" + StatusOf("1047", 2) + ")");
                var c = Project.ConnectionFor(S("1047").Id, S("1047").Field.Pipes[2].Id);
                Check(c != null && c.ToPipeId == null && c.Basis.Any(b => b.Contains("chosen manually by the drafter")), "recorded as the drafter's choice, not a field observation");
                Log("   card: " + Headline(2) + "  |  " + Detail(2));
                Check(Detail(2).Contains("picked by drafter") && Detail(2).Contains("not drawn yet") && Detail(2).Contains("no slope yet"), "card shows where it goes, not drawn, no slope yet");
                Check(ButtonStarting(Card(2), "Open ") != null && FindButton(Card(2), "Draw pipe + label") != null, "card offers Open and Draw pipe + label");
                ClickIn(Card(2), "Draw pipe + label");
            }, 3000);
            add("drawn from the card", () =>
            {
                var c = Counts();
                // 17.5" is over the 12" double-line threshold, so the pipe is two lines with one label.
                Check(c["pipe"] == 2 && c["pipelabel"] == 1, "Draw pipe + label drew the pipe (double line) and its label (" + c["pipe"] + ", " + c["pipelabel"] + ")");
                Check(Detail(2).Contains("drawn") && FindButton(Card(2), "Redraw pipe + label") != null, "the card now says drawn");
                Shot("05d-card-connected");
                Send("_.U ");
            }, 3000);
            add("walk to 1048", () =>
            {
                Check(Counts()["pipe"] == 0, "undo removed the pipe drawn from the card");
                Press(ButtonStarting(Card(2), "Open "));
            }, 3000);
            add("at 1048, Back to 1047", () =>
            {
                Check(Field<Label>("_structureTitle").Text.EndsWith("1048"), "Open on the card opened 1048 (" + Field<Label>("_structureTitle").Text + ")");
                var back = Field<Button>("_back");
                Check(Shown(back) && back.Text == "Back to SDMH 1047", "Back offers SDMH 1047 (" + back.Text + ")");
                var incoming = Field<Control>("_cards").Controls.Cast<Control>().FirstOrDefault(x => (x.Tag as string) == "incoming");
                var text = incoming == null ? "" : string.Join(" ", incoming.Controls.Cast<Control>().Select(x => x.Text).ToArray());
                Log("   incoming: " + text);
                Check(text.Contains("Runs in from SDMH 1047") && text.Contains("N/NW 17.5\" RIBBED PVC"), "1048 shows the pipe running in from 1047");
                Shot("05e-walked-to-1048");
                Check(S("1048").Field.Pipes.Count == 0, "no pipe at 1048 until the drafter adds one");
                Press(ButtonStarting(incoming, "Complete pipe from SDMH 1047"));
            }, 3000);
            add("Complete pipe from 1047: copied, not observed", () =>
            {
                Check(Shown(Quick) && QuickField<Label>("_title").Text == "Complete pipe from SDMH 1047 at CB 1048", "the panel opens to complete the pipe (" + QuickField<Label>("_title").Text + ")");
                var e = QuickEntry;
                Check(e.Direction != null && e.Direction.Text == "S/SE" && e.SizeIn == 17.5 && e.Material == "RIBBED PVC",
                      "opposite direction, size and material copied (S/SE 17.5\" RIBBED PVC)");
                Check(!e.MeasuredDip.HasValue && e.Reference == FU.MeasurementReference.Unspecified && QuickField<TextBox>("_dip").Text == "",
                      "the MD and reference start empty -- never copied from 1047");
                Check(CompassSelected == "S/SE" && (bool)Compass.GetType().GetProperty("SelectedIsPrefilled").GetValue(Compass, null), "the copied direction shows lighter on the compass");
                Check(Shown(QuickField<Label>("_prefillNote")) && QuickField<Label>("_prefillNote").Text.Contains("Copied from SDMH 1047"), "the panel says what was copied");
                Check(FindButton(Quick, "Add matching pipe") != null && !Shown(FindButton(Quick, "Add + next")), "one explicit Add matching pipe");
                Check(S("1048").Field.Pipes.Count == 0, "still nothing added while the panel is open");
                Shot("05f-complete-pipe-panel");
                ClickIn(Quick, "Cancel");
                Check(S("1048").Field.Pipes.Count == 0 && Field<Control>("_cards").Controls.Cast<Control>().Any(x => (x.Tag as string) == "incoming"),
                      "Cancel adds nothing; the pipe still waits to be completed");
                Press(ButtonStarting(Field<Control>("_cards"), "Complete pipe from SDMH 1047"));
                // At 1048 the crew measured an 18" pipe: the drafter changes the size, keeps the rest, enters the MD.
                ClickIn(Quick, "Usual sizes");   // 17.5" opened the longer list; 18" is a usual one
                ClickIn(Quick, "18\"");
                QuickField<TextBox>("_dip").Text = "6.9";
                ClickIn(QuickField<Control>("_references"), "Invert");
                ClickIn(Quick, "Add matching pipe");
            }, 500);
            add("the other end is tied to the connection", () =>
            {
                var b = S("1048");
                Check(b.Field.Pipes.Count == 1, "Add matching pipe added one pipe at 1048 (" + b.Field.Pipes.Count + ")");
                if (b.Field.Pipes.Count != 1) return;
                var p = b.Field.Pipes[0];
                Check(p.Direction.Text == "S/SE" && p.WidthIn == 18 && p.Material == "RIBBED PVC" && p.MeasuredDip == 6.9 &&
                      p.Reference == FU.MeasurementReference.Invert && p.Source == FU.ObservationSource.UserEntry, "S/SE 18\" RIBBED PVC IE 6.9 entered at 1048");
                Check(p.Prefilled.SequenceEqual(new[] { "direction", "material" }) && p.PrefilledFrom.StartsWith("SDMH 1047"),
                      "direction and material marked as copied from 1047; the size is 1048's own (" + string.Join(",", p.Prefilled) + ")");
                var a = S("1047");
                var c = Project.ConnectionFor(a.Id, a.Field.Pipes[2].Id);
                Check(c != null && c.ToPipeId == p.Id && c.Status == FU.ConnectionStatus.ManualOverride && Project.ConnectionFor(b.Id, p.Id) == c,
                      "the new pipe is the other end of the same connection, still the drafter's manual connection");
                Check(Project.Overrides.Any(o => o.What == "Pipe completed from connected pipe"), "completion recorded as an override");
                Check(!Field<Control>("_cards").Controls.Cast<Control>().Any(x => (x.Tag as string) == "incoming"), "nothing left waiting at 1048");
                Log("   card: " + Headline(0) + "  |  " + Detail(0));
                Check(Detail(0).Contains("slope ") && Detail(0).Contains("copied from SDMH 1047"), "the card shows the slope from both dips and what was copied");
                Shot("05g-completed-at-1048");
                ClickIn(Card(0), "Edit");
                Check((bool)Compass.GetType().GetProperty("SelectedIsPrefilled").GetValue(Compass, null), "editing shows the copied direction lighter");
                ClickCompass("S/SE");   // the drafter checks the book: S/SE was observed here too
                ClickIn(Quick, "Save pipe");
            }, 3000);
            add("confirmed direction no longer copied", () =>
            {
                var p = S("1048").Field.Pipes[0];
                Check(p.Prefilled.SequenceEqual(new[] { "material" }), "clicking S/SE confirms the direction as observed at 1048 (" + string.Join(",", p.Prefilled) + ")");
                Check(Project.Overrides.Any(o => o.Target == p.Id && o.What == "Copied pipe values confirmed or changed at this structure"), "the confirmation is recorded");
                Press(Field<Button>("_back"));
            }, 3000);

            add("back at 1047", () =>
            {
                Check(Field<Label>("_structureTitle").Text.EndsWith("1047") && Cards().Count == 3, "Back returned to 1047 and its 3 pipes");
                Window.GetType().GetMethod("OpenStructure", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(Window, new object[] { S("1046").Id });
            }, 3000);
            add("a CB from the notes: W x L and catch basin buttons", () =>
            {
                Check(Shown(Field<TextBox>("_insideWidth")) && Shown(Field<TextBox>("_insideLength")) && !Shown(Field<ComboBox>("_diameter")),
                      "CB 1046 (rectangular) asks for inside W x L, not a diameter");
                Check(Field<Label>("_choiceNote").Text == "Buttons for: Catch basins and inlets", "CB 1046 gets the catch basin pipe buttons");
                Click("+ Add pipe");
                Check(Buttons("_sizes").First() == "6\"", "its first size button is 6\"");
                ClickIn(Quick, "Cancel");
            }, 3000);

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
                Check(c["structurelabel"] == 4, "Label all structures added leaders for 1046, 1047 and 1048 and kept 1045's (" + c["structurelabel"] + " leaders)");
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
                    var cards = (Control)type.GetField("_cards", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form);
                    Log("   pipe cards at " + (int)(scale * 100) + "%: " + cards.Size);
                    Check(cards.Width >= 500 && cards.Height >= 60, "pipe cards usable at " + (int)(scale * 100) + "% (" + cards.Size + ")");
                    if (grid.Visible) Check(grid.Height >= 120 && grid.Width >= 500, "pipe table usable at " + (int)(scale * 100) + "% (" + grid.Size + ")");
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
