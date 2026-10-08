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

        /// <summary>
        /// Can AutoCAD hand us a picture of the drawing itself? PNGOUT is AutoCAD exporting its
        /// own view to a raster file -- nothing here captures the screen. Used to prove the
        /// mechanism before a demo is built on it.
        /// </summary>
        [CommandMethod("UIRASTER", CommandFlags.Session)]
        public void Raster()
        {
            Directory.CreateDirectory(OutDir);
            _log = new StreamWriter(Path.Combine(OutDir, "ui-test.log"), false, Encoding.UTF8) { AutoFlush = true };
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            var shot = Path.Combine(OutDir, "raster-test.png");
            try { if (File.Exists(shot)) File.Delete(shot); } catch (System.Exception) { }

            Log("raster test: writing " + shot);
            doc.SendStringToExecute("_.ZOOM _E ", true, false, true);
            doc.SendStringToExecute("FILEDIA\n0\n", true, false, true);
            doc.SendStringToExecute("_.PNGOUT\n\"" + shot + "\"\n\n", true, false, true);
            doc.SendStringToExecute("FILEDIA\n1\n", true, false, true);

            // The sends run when the drawing is idle; report from a timer once they have.
            var timer = new Timer { Interval = 2000 };
            var waited = 0;
            timer.Tick += (s, e) =>
            {
                waited += 2;
                if (!File.Exists(shot) && waited < 60) return;
                timer.Stop();
                Log(File.Exists(shot)
                    ? "raster test: " + new FileInfo(shot).Length + " bytes after " + waited + "s"
                    : "raster test: nothing written after " + waited + "s");
                Log("");
                Log("UI TEST DONE: raster probe finished");
                _log.Flush();
            };
            timer.Start();
        }

        /// <summary>The drafter's own pick sizes, put back when the run ends.</summary>
        private static short _pickbox;
        private static short _aperture;

        private static void Begin(bool dips, bool easements)
        {
            Directory.CreateDirectory(OutDir);
            _log = new StreamWriter(Path.Combine(OutDir, "ui-test.log"), false, Encoding.UTF8) { AutoFlush = true };
            // The AutoCAD text window goes to its own log beside this one, so a command that
            // stops at an unexpected prompt can be read back afterwards instead of guessed at.
            try
            {
                AcApp.SetSystemVariable("LOGFILEPATH", OutDir);
                AcApp.SetSystemVariable("LOGFILEMODE", 1);
            }
            catch (System.Exception ex) { Log("command-line log not started: " + ex.Message); }

            // PICKBOX and APERTURE live in the Civil 3D profile, not in the drawing, so they are
            // whatever the person at this machine likes. A wide pickbox picks the hand-drawn pipe
            // instead of the survey point two feet away, and the test would be measuring the
            // drafter's preferences. Set to the shipped defaults for the run; put theirs back at
            // the end.
            try
            {
                _pickbox = Convert.ToInt16(AcApp.GetSystemVariable("PICKBOX"));
                _aperture = Convert.ToInt16(AcApp.GetSystemVariable("APERTURE"));
                AcApp.SetSystemVariable("PICKBOX", (short)3);
                AcApp.SetSystemVariable("APERTURE", (short)10);
                Log("PICKBOX " + _pickbox + " and APERTURE " + _aperture + " set to 3 and 10 for the run");
            }
            catch (System.Exception ex) { Log("pick size not set: " + ex.Message); }

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
                    foreach (var line in CommandLineTail(15)) Log("   | " + line);
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

        /// <summary>The last lines of the AutoCAD text window, from its own log file: what a
        /// stuck command is actually prompting for.</summary>
        // ------------------------------------------ the office update check, inside Civil 3D
        // Driven through scratch folders: a pretend bundle and a pretend office copy. Nothing
        // real is installed, and the launch decision is read rather than acted on -- a test that
        // actually ran an installer would replace the build it is testing.

        private static Type OfficeUpdateType { get { return Plugin.GetType("FieldCodes.Cad.OfficeUpdate"); } }

        private static object OfficeUpdateProp(string name)
        {
            return OfficeUpdateType.GetProperty(name).GetValue(null, null);
        }

        private static void OfficeUpdateSet(string name, object value)
        {
            OfficeUpdateType.GetProperty(name).SetValue(null, value, null);
        }

        /// <summary>Points FTF at a scratch bundle and re-runs the check; returns the state's name.</summary>
        private static string CheckUpdate(string bundle)
        {
            OfficeUpdateSet("BundleOverride", bundle);
            return Convert.ToString(OfficeUpdateType.GetMethod("Check").Invoke(null, null));
        }

        /// <summary>A folder that looks like what the installer leaves behind.</summary>
        private static string FakeBundle(string root, string version, string officeFolder)
        {
            var bundle = Path.Combine(root, "bundle");
            Directory.CreateDirectory(bundle);
            if (version != null) File.WriteAllText(Path.Combine(bundle, "version.txt"), version);
            if (officeFolder != null) File.WriteAllText(Path.Combine(bundle, "source.txt"), officeFolder);
            return bundle;
        }

        /// <summary>A folder that looks like the setup folder on the office drive.</summary>
        private static string FakeOffice(string root, string version)
        {
            var office = Path.Combine(root, "office");
            Directory.CreateDirectory(Path.Combine(office, "Bundle"));
            File.WriteAllText(Path.Combine(office, "install.ps1"), "# pretend installer");
            File.WriteAllText(Path.Combine(office, "Bundle\\PackageContents.xml"), "<ApplicationPackage />");
            if (version != null) File.WriteAllText(Path.Combine(office, "version.txt"), version);
            return office;
        }

        /// <summary>
        /// What a pick at this spot could possibly hit, and how big the drawing's view is -- the
        /// two halves of "that is not a COGO point".
        /// </summary>
        private static void WhatIsAt(double x, double y, double reach)
        {
            try
            {
                var doc = AcApp.DocumentManager.MdiActiveDocument;
                var ed = doc.Editor;
                using (var view = ed.GetCurrentView())
                    Log("   view: centre " + view.CenterPoint.ToString() + " height " +
                        view.Height.ToString("0.00", CultureInfo.InvariantCulture) +
                        ", screen " + Screen.PrimaryScreen.WorkingArea);

                foreach (var size in new[] { 0.2, reach })
                {
                    var picked = ed.SelectCrossingWindow(new Point3d(x - size, y - size, 0),
                                                         new Point3d(x + size, y + size, 0));
                    var kinds = new List<string>();
                    if (picked.Status == Autodesk.AutoCAD.EditorInput.PromptStatus.OK)
                    {
                        using (var tr = doc.Database.TransactionManager.StartOpenCloseTransaction())
                            foreach (AcDb.ObjectId id in picked.Value.GetObjectIds())
                            {
                                var e = tr.GetObject(id, AcDb.OpenMode.ForRead);
                                kinds.Add(e.GetType().Name + "/" + e.Handle);
                            }
                    }
                    Log("   within " + size.ToString("0.0", CultureInfo.InvariantCulture) + " of " +
                        x.ToString("0", CultureInfo.InvariantCulture) + "," + y.ToString("0", CultureInfo.InvariantCulture) +
                        ": " + (kinds.Count == 0 ? "(nothing)" : string.Join(", ", kinds.ToArray())));
                }
            }
            catch (System.Exception ex) { Log("   what-is-at failed: " + ex.Message); }
        }

        /// <summary>Frozen, off or locked: each of them stops a pick in its own way.</summary>
        private static string LayerState(string name)
        {
            try
            {
                var doc = AcApp.DocumentManager.MdiActiveDocument;
                using (var tr = doc.Database.TransactionManager.StartOpenCloseTransaction())
                {
                    var lt = (AcDb.LayerTable)tr.GetObject(doc.Database.LayerTableId, AcDb.OpenMode.ForRead);
                    if (!lt.Has(name)) return "(no such layer)";
                    var layer = (AcDb.LayerTableRecord)tr.GetObject(lt[name], AcDb.OpenMode.ForRead);
                    return "frozen=" + layer.IsFrozen + " off=" + layer.IsOff + " locked=" + layer.IsLocked;
                }
            }
            catch (System.Exception ex) { return ex.Message; }
        }

        /// <summary>Whether a layer is frozen -- a frozen point cannot be picked.</summary>
        private static bool LayerFrozen(string name)
        {
            try
            {
                var doc = AcApp.DocumentManager.MdiActiveDocument;
                using (var tr = doc.Database.TransactionManager.StartOpenCloseTransaction())
                {
                    var lt = (AcDb.LayerTable)tr.GetObject(doc.Database.LayerTableId, AcDb.OpenMode.ForRead);
                    if (!lt.Has(name)) return false;
                    return ((AcDb.LayerTableRecord)tr.GetObject(lt[name], AcDb.OpenMode.ForRead)).IsFrozen;
                }
            }
            catch (System.Exception) { return false; }
        }

        /// <summary>The command each button on a ribbon tab posts, panel by panel.</summary>
        private static List<string> RibbonCommands(Autodesk.Windows.RibbonTab tab)
        {
            var found = new List<string>();
            if (tab == null) return found;
            foreach (var panel in tab.Panels)
                if (panel.Source != null) WalkRibbon(panel.Source.Items, found);
            return found;
        }

        private static void WalkRibbon(IEnumerable<Autodesk.Windows.RibbonItem> items, List<string> into)
        {
            foreach (var item in items)
            {
                var button = item as Autodesk.Windows.RibbonButton;
                if (button != null && button.CommandParameter != null)
                    into.Add(Convert.ToString(button.CommandParameter).Trim());
                var row = item as Autodesk.Windows.RibbonRowPanel;
                if (row != null) WalkRibbon(row.Items, into);
            }
        }

        /// <summary>Every command name the plugin registers -- what a ribbon button may post.</summary>
        private static HashSet<string> PluginCommands()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Type[] types;
            try { types = Plugin.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray(); }
            foreach (var type in types)
                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                                                       BindingFlags.Instance | BindingFlags.Static))
                    foreach (Autodesk.AutoCAD.Runtime.CommandMethodAttribute attribute in
                             method.GetCustomAttributes(typeof(Autodesk.AutoCAD.Runtime.CommandMethodAttribute), false))
                        names.Add(attribute.GlobalName);
            return names;
        }

        /// <summary>How many Dip Builder actions are still queued for the drawing.</summary>
        private static int Pending()
        {
            var field = SessionType.GetField("Pending", BindingFlags.NonPublic | BindingFlags.Static);
            var queue = field == null ? null : field.GetValue(null) as System.Collections.ICollection;
            return queue == null ? -1 : queue.Count;
        }

        /// <summary>What the drawing and the action queue are doing right now -- for tracing a
        /// step that waits on a queued command.</summary>
        private static void Diag(string where)
        {
            try
            {
                var doc = AcApp.DocumentManager.MdiActiveDocument;
                var field = SessionType.GetField("Pending", BindingFlags.NonPublic | BindingFlags.Static);
                var queue = field == null ? null : field.GetValue(null) as System.Collections.ICollection;
                Log("   [diag " + where + "] cmd='" + doc.CommandInProgress + "' pending=" +
                    (queue == null ? "?" : queue.Count.ToString(CultureInfo.InvariantCulture)) +
                    " last='" + Convert.ToString(SessionType.GetField("LastMessage").GetValue(null)) + "'");
                foreach (var line in CommandLineTail(3)) Log("     | " + line);
            }
            catch (System.Exception ex) { Log("   [diag " + where + "] " + ex.Message); }
        }

        private static string[] CommandLineTail(int count)
        {
            try
            {
                var file = Directory.GetFiles(OutDir, "*.log")
                                    .Where(f => !f.EndsWith("ui-test.log", StringComparison.OrdinalIgnoreCase))
                                    .OrderBy(f => File.GetLastWriteTimeUtc(f)).LastOrDefault();
                if (file == null) return new[] { "(no command-line log)" };
                var lines = new List<string>();
                using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new StreamReader(stream))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                        if (line.Trim().Length > 0) lines.Add(line.TrimEnd());
                }
                return lines.Skip(Math.Max(0, lines.Count - count)).ToArray();
            }
            catch (System.Exception ex) { return new[] { "(command-line log unreadable: " + ex.Message + ")" }; }
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

        /// <summary>
        /// Opens a card's More actions if they are not already open, then presses the first of
        /// the given labels that is there. More than one label because a card only offers what
        /// applies: an unconnected pipe says "Connects to...", a connected one "Change where it
        /// connects".
        /// </summary>
        private static void CardMore(int index, params string[] texts)
        {
            var card = Card(index);
            var open = (bool)card.GetType().GetProperty("MoreOpen").GetValue(card, null);
            if (!open) ClickIn(card, "More ▾");
            foreach (var text in texts)
                if (FindButton(Card(index), text) != null) { ClickIn(Card(index), text); return; }
            Fail("no More action on card " + index + " called " + string.Join(" or ", texts));
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

        // ------------------------------------------- driving the capture control
        // The panel is chips plus ONE chooser strip. These read its contract -- what has been
        // chosen, which chooser is open, whether the MD is live -- rather than its controls.

        private static object QuickProp(string name)
        {
            var q = Quick;
            return q.GetType().GetProperty(name).GetValue(q, null);
        }

        private static string OpenChooser { get { return (string)QuickProp("OpenChooser"); } }
        private static IList<string> ChooserButtons { get { return (IList<string>)QuickProp("ChooserButtons"); } }
        private static double? ChosenSize { get { return (double?)QuickProp("ChosenSize"); } }
        private static string ChosenType { get { return (string)QuickProp("ChosenType"); } }
        private static string ChosenDirection { get { return (string)QuickProp("ChosenDirection"); } }
        private static double? ChosenAzimuth { get { return (double?)QuickProp("ChosenAzimuth"); } }
        private static FU.MeasurementReference ChosenReference { get { return (FU.MeasurementReference)QuickProp("ChosenReference"); } }
        private static string ReferenceChip { get { return (string)QuickProp("ReferenceChipText"); } }
        private static bool MdEnabled { get { return (bool)QuickProp("MdEnabled"); } }
        private static bool MdFocusAsked { get { return (bool)QuickProp("MdFocusAsked"); } }

        private static string MdText
        {
            get { return (string)QuickProp("MdText"); }
            set { Quick.GetType().GetProperty("MdText").SetValue(Quick, value, null); }
        }

        /// <summary>How many pipes 1047 had before the undipped one was entered.</summary>
        private static int _undippedBefore;

        /// <summary>Which card holds the 17.5" RIBBED PVC N/NW pipe -- the last one entered at 1047.</summary>
        private static int _nnw;

        /// <summary>Types a structure type into the header and commits it, as leaving the box does.</summary>
        private static void SetStructureType(string type)
        {
            Field<ComboBox>("_type").Text = type;
            Window.GetType().GetMethod("SaveStructureFields", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(Window, null);
        }

        /// <summary>Presses a button in the chooser strip, as the drafter would.</summary>
        private static bool Choose(string text)
        {
            return (bool)Quick.GetType().GetMethod("PressInChooser").Invoke(Quick, new object[] { text });
        }

        private static bool TypeInChooser(string text)
        {
            return (bool)Quick.GetType().GetMethod("TypeInChooser").Invoke(Quick, new object[] { text });
        }

        private static void OpenChooserFor(string which)
        {
            Quick.GetType().GetMethod("OpenChooserFor").Invoke(Quick, new object[] { which });
        }

        /// <summary>Enter, or Escape, in the MD box -- the same path the key handler takes.</summary>
        private static void PressInMd(Keys key)
        {
            Quick.GetType().GetMethod("PressInMd").Invoke(Quick, new object[] { key });
        }

        /// <summary>The whole normal capture: size, type, direction, then the MD and Enter.</summary>
        private static bool Capture(string size, string type, string direction, string md)
        {
            var found = Pick(size) && Pick(type) && Pick(direction);
            MdText = md;
            PressInMd(Keys.Enter);
            return found;
        }

        /// <summary>Presses a chooser button, and says what was on offer when it is not there.</summary>
        private static bool Pick(string text)
        {
            if (Choose(text)) return true;
            Log("   no \"" + text + "\" in the " + OpenChooser + " chooser; it offered: " +
                string.Join(" ", ChooserButtons.ToArray()));
            return false;
        }

        /// <summary>The words above the chips -- "Pipe 2 of 3" while a slot is open.</summary>
        private static string SlotWords { get { return QuickField<Label>("_title").Text; } }

        private static T QuickField<T>(string name) where T : class
        {
            var q = Quick;
            var f = q.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            return f == null ? null : f.GetValue(q) as T;
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

        /// <summary>
        /// Clicks a button that asks for a point in the drawing, and answers it.
        ///
        /// The zoom is NOT sent here. Clicking the button queues the window's command, and a
        /// zoom sent in the same breath lands in the same input queue: AutoCAD then reads the
        /// queued command as the zoom's answer and the point as a command, and the window's
        /// command is left typed at the prompt with nothing to finish it. The zoom goes in its
        /// own step before this one (<see cref="ZoomTo"/>), which the harness only runs once
        /// the command line is idle.
        /// </summary>
        private static void PickPoint(string button, double x, double y)
        {
            Click(button);
            Send(string.Format(CultureInfo.InvariantCulture, "{0},{1} ", x, y));
        }

        /// <summary>
        /// Answers FTF's "select the structure point" prompt with the survey point itself.
        ///
        /// A typed coordinate only selects what the pickbox happens to touch there, which depends
        /// on the marker the point style draws, the view height and the drafter's own PICKBOX --
        /// none of which this suite is testing. Naming the entity tests what it means to test:
        /// FTF's prompt, its COGO-point filter, and what it does with the point it is given.
        /// </summary>
        private static void PickStructure(string button, uint pointNumber)
        {
            var handle = HandleOfPoint(pointNumber);
            Click(button);
            if (handle == null)
            {
                Fail("no survey point " + pointNumber + " in the drawing to select");
                Send("\x03");
                return;
            }
            Send("(handent \"" + handle + "\") ");
        }

        /// <summary>The drawing handle of a seeded survey point, or null when it is not there.</summary>
        private static string HandleOfPoint(uint pointNumber)
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            using (var tr = doc.Database.TransactionManager.StartOpenCloseTransaction())
            {
                var ms = (AcDb.BlockTableRecord)tr.GetObject(
                    AcDb.SymbolUtilityServices.GetBlockModelSpaceId(doc.Database), AcDb.OpenMode.ForRead);
                foreach (AcDb.ObjectId id in ms)
                {
                    var point = tr.GetObject(id, AcDb.OpenMode.ForRead) as CivDb.CogoPoint;
                    if (point != null && point.PointNumber == pointNumber) return point.Handle.ToString();
                }
            }
            return null;
        }

        /// <summary>
        /// Answers a point prompt, but only once FTFDIPACT is really asking. Reopening the drawing
        /// can leave a queued FTFDIPACT of its own to run first, and a point typed at the bare
        /// "Command:" prompt is lost -- after which the real prompt waits for ever and every click
        /// behind it queues up. The caller tries again in the next step.
        /// </summary>
        private static bool AnswerPoint(double x, double y)
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            if (string.IsNullOrEmpty(doc.CommandInProgress)) return false;
            Send(string.Format(CultureInfo.InvariantCulture, "{0},{1} ", x, y));
            return true;
        }

        /// <summary>Puts the point in view, in a step of its own so it finishes before anything is clicked.</summary>
        private static void ZoomTo(double x, double y)
        {
            Send(string.Format(CultureInfo.InvariantCulture, "_.ZOOM _C {0},{1} 30 ", x, y));
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

            // ------------------------------------------------ the ribbon, in both suites
            // Two tabs: the drawing being finished, and the record work written from it. A
            // button whose command was renamed away would look fine and do nothing, so every
            // one of them is checked against the commands FTF really registers.
            add("the ribbon has an FTF tab and an FTF Boundary tab", () =>
            {
                var ribbon = Autodesk.Windows.ComponentManager.Ribbon;
                Check(ribbon != null, "Civil 3D has a ribbon for FTF to add to");
                if (ribbon == null) return;

                var drawing = ribbon.FindTab("FTF_RIBBON_TAB");
                var boundary = ribbon.FindTab("FTF_BOUNDARY_RIBBON_TAB");
                Check(drawing != null && drawing.Title == "FTF", "the drawing's own work is on the FTF tab");
                Check(boundary != null && boundary.Title == "FTF Boundary", "the record work has a tab of its own");
                if (drawing == null || boundary == null) return;

                Log("   FTF panels: " + string.Join(", ", drawing.Panels.Select(p => p.Source.Title).ToArray()));
                Log("   FTF Boundary panels: " + string.Join(", ", boundary.Panels.Select(p => p.Source.Title).ToArray()));

                var mine = RibbonCommands(drawing);
                var record = RibbonCommands(boundary);
                Log("   FTF: " + string.Join(" ", mine.ToArray()));
                Log("   FTF Boundary: " + string.Join(" ", record.ToArray()));

                Check(mine.Count > 0 && record.Count > 0, "both tabs have buttons (" + mine.Count + ", " + record.Count + ")");
                Check(!mine.Intersect(record).Any(), "no button sits on both tabs");
                Check(mine.Contains("FTFDIP") && mine.Contains("FTFLABELLINE") && mine.Contains("FTFRUN") && mine.Contains("FTFCLEAN"),
                      "the FTF tab keeps labelling, finishing, dips and clean up");
                var strayed = mine.Where(c => c.Contains("EASEMENT") || c.Contains("RECORD") || c.Contains("EXHIBIT")).ToList();
                Check(strayed.Count == 0, "and none of the record work (" + string.Join(", ", strayed.ToArray()) + ")");
                Check(record.Contains("FTFRECORD") && record.Contains("STRIPEASEMENT") &&
                      record.Contains("FTFEXHIBIT") && record.Contains("FTFEASEMENTLEGAL"),
                      "the FTF Boundary tab holds the recorded survey, the easements and the exhibits");
                Check(!record.Contains("FTFDIP") && !record.Contains("FTFLABELLINE"), "and none of the drawing's own finishing");

                var known = PluginCommands();
                Log("   commands FTF registers: " + known.Count);
                var unknown = mine.Concat(record).Where(c => !known.Contains(c)).ToList();
                Check(unknown.Count == 0, "every button runs a command FTF really has (" + string.Join(", ", unknown.ToArray()) + ")");
            }, 500);

            // What UISEED actually left in the drawing, and what the view is looking at. A pick
            // that reports "not a COGO point" is either aimed wrong or aimed at nothing, and the
            // log has to say which.
            add("the seeded survey points", () =>
            {
                var doc = AcApp.DocumentManager.MdiActiveDocument;
                var found = 0;
                using (var tr = doc.Database.TransactionManager.StartOpenCloseTransaction())
                {
                    var ms = (AcDb.BlockTableRecord)tr.GetObject(
                        AcDb.SymbolUtilityServices.GetBlockModelSpaceId(doc.Database), AcDb.OpenMode.ForRead);
                    foreach (AcDb.ObjectId id in ms)
                    {
                        var e = tr.GetObject(id, AcDb.OpenMode.ForRead) as AcDb.Entity;
                        var point = e as CivDb.CogoPoint;
                        if (point == null)
                        {
                            Log("   entity: " + (e == null ? "?" : e.GetType().Name) +
                                " on " + (e == null ? "?" : e.Layer));
                            continue;
                        }
                        found++;
                        Log("   point " + point.PointNumber + " at " +
                            point.Location.X.ToString("0.00", CultureInfo.InvariantCulture) + "," +
                            point.Location.Y.ToString("0.00", CultureInfo.InvariantCulture) +
                            " \"" + point.RawDescription + "\" visible=" + point.Visible +
                            " style=" + (point.StyleId.IsNull ? "(none)" : "set") +
                            " layer=" + point.Layer + " frozen=" + LayerFrozen(point.Layer));
                    }
                }
                Check(found == 4, "UISEED left four survey points in the drawing (" + found + ")");

                // Can AutoCAD select the point at all, and is the drawing looking where the test
                // thinks it is? A pick answers with a coordinate, which is read in the current UCS.
                var ed = doc.Editor;
                var ucs = ed.CurrentUserCoordinateSystem.CoordinateSystem3d;
                Log("   UCS origin " + ucs.Origin.ToString() + " x-axis " + ucs.Xaxis.ToString());
                using (var view = ed.GetCurrentView())
                    Log("   view centre " + view.CenterPoint.ToString() + " height " +
                        view.Height.ToString("0.0", CultureInfo.InvariantCulture) + " twist " +
                        view.ViewTwist.ToString("0.000", CultureInfo.InvariantCulture));
                Log("   V-NODE: " + LayerState("V-NODE"));

                var window = ed.SelectCrossingWindow(new Point3d(4990, 4990, 0), new Point3d(5010, 5010, 0));
                if (window.Status != Autodesk.AutoCAD.EditorInput.PromptStatus.OK)
                {
                    Check(false, "nothing at all can be selected around 1045 (" + window.Status + ")");
                }
                else
                {
                    var kinds = new List<string>();
                    using (var tr = doc.Database.TransactionManager.StartOpenCloseTransaction())
                        foreach (AcDb.ObjectId id in window.Value.GetObjectIds())
                            kinds.Add(tr.GetObject(id, AcDb.OpenMode.ForRead).GetType().Name);
                    Log("   selectable around 1045: " + string.Join(", ", kinds.ToArray()));
                    Check(kinds.Contains("CogoPoint"), "the survey point at 1045 can be selected (" +
                          string.Join(", ", kinds.ToArray()) + ")");
                }
            }, 500);

            // ------------------------------- the update check, in both suites
            // Surveyors install from the office copy and never think about it again: FTF compares
            // itself against that copy at startup and installs a newer one when Civil 3D closes.
            add("FTF compares itself against the office copy", () =>
            {
                Check((bool)OfficeUpdateProp("Armed"), "the check is armed when FTF loads");

                var root = Path.Combine(Path.GetTempPath(), "ftf-update-ui-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                var mine = "FTF 2026-09-01 08:00  office aaaaaaa  (Release, Civil 3D 2024)";
                try
                {
                    // A newer office copy: the one case that does anything.
                    var office = FakeOffice(root, "FTF 2026-09-30 17:00  office bbbbbbb  (Release, Civil 3D 2024)");
                    var state = CheckUpdate(FakeBundle(root, mine, office));
                    Log("   state: " + state + " | " + OfficeUpdateProp("Sentence"));
                    Check(state == "UpdateWaiting", "a newer office copy is an update waiting (" + state + ")");
                    var sentence = Convert.ToString(OfficeUpdateProp("Sentence"));
                    Check(sentence.Contains("installs itself when you close Civil 3D"),
                          "and the window says so, with nothing to answer");
                    Check(sentence.Contains("2026-09-30 17:00"), "naming the build that is waiting");

                    var launch = Convert.ToString(OfficeUpdateType.GetMethod("UpdaterArguments").Invoke(null, null));
                    Log("   would run: powershell " + launch);
                    Check(launch.Contains("install.ps1") && launch.Contains("-WaitForCivil3D"),
                          "at quit it runs the office copy's own installer, which waits for Civil 3D to close");
                    Check(launch.Contains(office), "from the folder it was installed from");

                    // Everything else leaves the machine alone.
                    Directory.Delete(root, true);
                    office = FakeOffice(root, mine);
                    state = CheckUpdate(FakeBundle(root, mine, office));
                    Check(state == "Current", "the same build is current (" + state + ")");
                    Check(OfficeUpdateType.GetMethod("UpdaterArguments").Invoke(null, null) == null,
                          "and nothing is run at quit");

                    Directory.Delete(root, true);
                    office = FakeOffice(root, "FTF 2026-08-01 08:00  office ccccccc  (Release, Civil 3D 2024)");
                    state = CheckUpdate(FakeBundle(root, mine, office));
                    Check(state == "AheadOfOffice", "an older office copy never drags a machine back (" + state + ")");
                    Check(OfficeUpdateType.GetMethod("UpdaterArguments").Invoke(null, null) == null,
                          "and nothing is run at quit for it either");

                    Directory.Delete(root, true);
                    state = CheckUpdate(FakeBundle(root, mine, null));
                    Check(state == "Unknown", "a copy installed by hand has no office copy to check (" + state + ")");
                    Check(Convert.ToString(OfficeUpdateProp("Trouble")).Contains("installed by hand"),
                          "and says as much: " + OfficeUpdateProp("Trouble"));

                    Directory.Delete(root, true);
                    state = CheckUpdate(FakeBundle(root, mine, Path.Combine(root, "not-there")));
                    Check(state == "Unknown", "an unreachable office drive changes nothing (" + state + ")");
                    Check(Convert.ToString(OfficeUpdateProp("Trouble")).Contains("not reachable"),
                          "and says that too: " + OfficeUpdateProp("Trouble"));
                }
                finally
                {
                    // Back to this machine's real state: whatever it is, it is not the test's.
                    OfficeUpdateSet("BundleOverride", null);
                    OfficeUpdateType.GetMethod("Check").Invoke(null, null);
                    try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch (System.Exception) { }
                }
                Log("   this machine: " + OfficeUpdateProp("State") + " | " + OfficeUpdateProp("Sentence"));
            }, 800);

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
            add("view select structure 1045 from the drawing", () => ZoomTo(5000, 5000), 500);
            add("select structure 1045 from the drawing", () =>
            {
                WhatIsAt(5000, 5000, 3);
                PickStructure("Select structure point...", 1045);
            }, 1500);
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
                Check(label.Contains("12\" RCP (N) IE = 322.01'") && label.Contains("18\" RCP (SW) IE = 321.40'") &&
                      label.Contains("8\" PVC (E) TOP = 323.19'") && label.Contains("BOT = 320.60"),
                      "live label reads size, material, direction, then the dip");
                Shot("03-notes-read");
            }, 3000);

            // Find all connections: every open pipe at once; the sure ones connect, the rest are listed --------
            add("find all connections", () => Click("Find all connections"), 500);
            add("sure pairs confirmed, the rest listed", () =>
            {
                Check(StatusOf("1045", 0) == "Confirmed->1046" && StatusOf("1045", 1) == "Confirmed->1047",
                      "12 RCP N -> CB 1046 and 18 RCP SW -> SDMH 1047 confirmed automatically (" + StatusOf("1045", 0) + ", " + StatusOf("1045", 1) + ")");
                // One connection holds both ends; which end it is written from is whichever pipe the pairing took first.
                var c = Project.ConnectionFor(S("1045").Id, S("1045").Field.Pipes[0].Id);
                var ends = c == null ? new string[0] : new[] { c.FromPipeId, c.ToPipeId };
                Check(c != null && ends.Contains(S("1045").Field.Pipes[0].Id) && ends.Contains(S("1046").Field.Pipes[0].Id) &&
                      c.Basis.Any(b => b.StartsWith("Confirmed automatically by Find all connections")),
                      "each is one connection holding both observed ends, and says Find all connections confirmed it");
                Check(Project.ConnectionFor(S("1046").Id, S("1046").Field.Pipes[0].Id) == c, "the far end shares that one connection");
                var list = Field<ListView>("_proposalList");
                var rows = list.Items.Cast<ListViewItem>().Select(i => string.Join(" | ", i.SubItems.Cast<ListViewItem.ListViewSubItem>().Select(x => x.Text).ToArray())).ToList();
                foreach (var r in rows) Log("   row: " + r);
                Check(Field<TabControl>("_tabs").SelectedIndex == 1 && Shown(Field<Control>("_proposalPanel")), "the Map tab shows what was found");
                Check(rows.Count(r => r.Contains("Confirmed automatically")) == 2, "two rows confirmed automatically");
                // 1048 is not in the drawing's dip data yet, so the east pipe has nothing to run to.
                Check(rows.Any(r => r.StartsWith("SDMH 1045:  E 8\" PVC") && r.Contains("Nothing found") && r.Contains("no surveyed structure")),
                      "8 PVC E says no surveyed structure lies that way yet");
                Check(rows.Any(r => r.Contains("? 10\" RCP") && r.Contains("Nothing found") && r.Contains("no direction")), "the pipe with no direction says why nothing was found");
                Check(rows.Count == 5 && rows.Count(r => r.Contains("18\" RCP")) == 1 && rows.Count(r => r.Contains("12\" RCP")) == 1,
                      "each pipe is listed once: a confirmed pair is one row, not one per end (" + rows.Count + " rows)");
                Log("   summary: " + Field<Label>("_proposalSummary").Text);
                Shot("03b-find-all");
                Send("_.U ");
            }, 4000);
            add("one undo takes it all back", () =>
            {
                Check(StatusOf("1045", 0) == "none" && StatusOf("1045", 1) == "none", "Undo removed every connection Find all connections made (" + StatusOf("1045", 0) + ", " + StatusOf("1045", 1) + ")");
                Tab(0);
            }, 4000);

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

            // Finding where one pipe runs is on the pipe's own card now, not in a section below.
            add("find connections for 12 RCP N", () => ClickIn(Card(0), "Find where it runs"), 500);
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

            add("find connections for 18 RCP SW", () => ClickIn(Card(1), "Find where it runs"), 500);
            add("change the suggestion: pick 1048 instead", () =>
            {
                var list = Field<ListView>("_candidateList");
                Check(list.Items.Count >= 1 && list.Items[0].Text == "SDMH 1047", "SDMH 1047 suggested for 18 RCP SW");
                Send("_.ZOOM _C 5120,5000 30 ");
                CardMore(1, "Change where it connects", "Connects to...");
                Send("5120,5000 ");
                Send("UI TEST CHANGE\n");
            }, 800);
            add("manual choice recorded", () =>
            {
                Check(StatusOf("1045", 1) == "ManualOverride->1048", "manual pick recorded as ManualOverride to 1048 (" + StatusOf("1045", 1) + ")");
                SelectPipeRow(1);
                CardMore(1, "Suggest where it runs");
            }, 3000);
            add("change back to 1047", () =>
            {
                Field<ListView>("_candidateList").Items[0].Selected = true;
                Click("Confirm selected");
            }, 800);
            add("changed back", () => Check(StatusOf("1045", 1) == "Confirmed->1047", "suggestion changed back to 1047 (" + StatusOf("1045", 1) + ")"), 3000);

            add("leave 8 PVC E unresolved", () => CardMore(2, "Leave unresolved"), 500);
            add("left unresolved", () => Check(StatusOf("1045", 2) == "LeftUnresolved->?", "8 PVC E left unresolved (" + StatusOf("1045", 2) + ")"), 3000);
            add("mark 6\" W as running outside the survey limits", () => CardMore(3, "Runs outside survey limits"), 500);
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
                Check(Field<TextBox>("_labelPreview").Text.Contains("6\" (W) TOP = 324.32'"), "live label shows 6\" (W) TOP = 324.32'");
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
            add("view select 1047", () => ZoomTo(4850, 4850), 500);
            add("select 1047", () => PickStructure("Select structure point...", 1047), 500);
            add("1047 read as invert by default", () =>
                Check(S("1047").Field.Pipes[0].Reference == FU.MeasurementReference.Invert && S("1047").Field.Pipes[0].ReferenceBasis == FU.ReferenceBasis.FieldNoteConvention,
                      "1047's unmarked dip is the invert by the office default"), 3500);

            // + Add pipe: direction -> size -> material -> MD / reference -> Add ------------------
            // ---- capturing a pipe: size, type, direction, then the MD ----------------------
            add("+ Add pipe opens a clean slot", () =>
            {
                Click("+ Add pipe");
            }, 1200);
            add("nothing is filled in but the reference", () =>
            {
                Check(Shown(Quick), "the capture panel is open");
                Check(!ChosenSize.HasValue && ChosenType == null && ChosenDirection == null,
                      "size, type and direction are unset (" + ChosenSize + "/" + ChosenType + "/" + ChosenDirection + ")");
                Check(ChosenReference == FU.MeasurementReference.Invert && ReferenceChip.StartsWith("IE"),
                      "the office default is visible on its chip (" + ReferenceChip + ")");
                Check(!MdEnabled, "the MD cannot be typed yet");
                Check(S("1047").Field.Pipes.Count == 1, "opening a slot creates no observation (" + S("1047").Field.Pipes.Count + " pipe)");
                Check(OpenChooser == "Size", "the strip opens on the first question (" + OpenChooser + ")");
                Shot("05b-add-pipe-panel");
            }, 1500);

            add("the one strip advances size -> type -> direction", () =>
            {
                var sizes = ChooserButtons;
                Log("   SDMH sizes: " + string.Join(" ", sizes.ToArray()));
                Check(sizes.Contains("12\"") && sizes.Contains("20\"") && sizes.Contains("Larger"),
                      "the strip holds this structure's usual sizes, then Larger");
                Choose("12\"");
                Check(ChosenSize == 12 && OpenChooser == "Type", "choosing a size fills it and advances to type (" + OpenChooser + ")");

                var materials = ChooserButtons;
                Log("   SDMH materials: " + string.Join(" ", materials.ToArray()));
                Check(materials.First() == "RCP" && materials.Contains("More...") && !materials.Contains("VCP"),
                      "the same strip now holds the storm materials, then More...");
                Choose("RCP");
                Check(ChosenType == "RCP" && OpenChooser == "Direction", "choosing a type advances to direction (" + OpenChooser + ")");

                var directions = ChooserButtons;
                Check(FU.DirectionShortcuts.Names.All(n => directions.Contains(n)), "all sixteen directions are on the strip");
                Check(!MdEnabled, "the MD is still not typable");
                Choose("N");
                Check(ChosenDirection == "N" && OpenChooser == "None", "choosing a direction closes the strip (" + OpenChooser + ")");
                Check(MdEnabled && MdFocusAsked, "the MD is enabled and asked for the caret");
            }, 1500);

            add("Enter finishes the pipe, with no Add button", () =>
            {
                Check(FindButton(Quick, "Add pipe") == null && FindButton(Quick, "Add + next") == null,
                      "there is no Add or Add + next to press");
                MdText = "6.41";
                PressInMd(Keys.Enter);
            }, 1500);
            add("one record, and the next slot is clean", () =>
            {
                var st = S("1047");
                Check(st.Field.Pipes.Count == 2, "exactly one pipe was created (" + st.Field.Pipes.Count + " total)");
                var p = st.Field.Pipes[1];
                Check(p.WidthIn == 12 && p.Material == "RCP" && p.Direction.Text == "N" &&
                      p.Reference == FU.MeasurementReference.Invert && p.MeasuredDip == 6.41,
                      "12\" RCP N invert 6.41 saved from the panel");
                Check(p.Source == FU.ObservationSource.UserEntry, "recorded as entered by the drafter");
                Check(Shown(Quick) && !ChosenSize.HasValue && ChosenType == null && ChosenDirection == null,
                      "the next slot starts clean -- nothing carried forward");
                Check(ChosenReference == FU.MeasurementReference.Invert && !MdEnabled && MdText == "",
                      "and back to the default reference with the MD disabled");
            }, 1500);

            add("two more pipes through the real control", () =>
            {
                Capture("15\"", "PVC", "SW", "5.10");
            }, 1500);
            add("third pipe", () => Capture("8\"", "CONC", "E", "4.25"), 1500);
            add("three pipes, three records", () =>
            {
                var st = S("1047");
                Check(st.Field.Pipes.Count == 4, "each Enter made exactly one record (" + st.Field.Pipes.Count + " with the note's pipe)");
                Check(st.Field.Pipes[2].WidthIn == 15 && st.Field.Pipes[2].Material == "PVC" && st.Field.Pipes[2].MeasuredDip == 5.10 &&
                      st.Field.Pipes[3].WidthIn == 8 && st.Field.Pipes[3].Material == "CONC" && st.Field.Pipes[3].MeasuredDip == 4.25,
                      "both later pipes are exactly as entered");
                Check(st.Field.Pipes.All(x => x.Reference == FU.MeasurementReference.Invert), "all three took the office default invert");
            }, 1500);

            // ---- safety: nothing is created by accident -----------------------------------
            add("nothing is created by accident, but a pipe may go undipped", () =>
            {
                var before = S("1047").Field.Pipes.Count;
                PressInMd(Keys.Enter);                       // MD disabled, nothing chosen
                Check(S("1047").Field.Pipes.Count == before, "Enter with nothing chosen creates nothing");

                Choose("12\""); Choose("RCP"); Choose("W");
                MdText = "six";
                PressInMd(Keys.Enter);
                Check(S("1047").Field.Pipes.Count == before, "Enter with an unreadable MD creates nothing");

                MdText = "3.30";
                Check(S("1047").Field.Pipes.Count == before, "typing the MD alone saves nothing");
                PressInMd(Keys.Escape);
                Check(S("1047").Field.Pipes.Count == before, "abandoning the slot creates nothing");
            }, 2000);

            // ---- a measurement that is not an invert ---------------------------------------
            add("a top-of-pipe pipe", () =>
            {
                Click("+ Add pipe");
            }, 1200);
            add("change IE to Top of pipe", () =>
            {
                Choose("8\""); Choose("PVC"); Choose("E");
                OpenChooserFor("Reference");
                Check(OpenChooser == "Reference" && ChooserButtons.Contains("Top of pipe") && ChooserButtons.Contains("Not stated"),
                      "the same strip offers the references");
                Choose("Top of pipe");
                Check(ChosenReference == FU.MeasurementReference.TopOfPipe && ReferenceChip.StartsWith("TOP"),
                      "the chip follows the choice (" + ReferenceChip + ")");
                MdText = "5.23";
                PressInMd(Keys.Enter);
            }, 1500);
            add("saved as a top-of-pipe measurement", () =>
            {
                var p = S("1047").Field.Pipes.Last();
                Check(p.Reference == FU.MeasurementReference.TopOfPipe && p.ReferenceBasis == FU.ReferenceBasis.EnteredByDrafter && p.MeasuredDip == 5.23,
                      "saved as top of pipe, chosen by the drafter");
                Check(ChosenReference == FU.MeasurementReference.Invert, "the next pipe is back at the office default");
            }, 1500);

            // A structure can have three pipes and one dip: silted, submerged, or out of reach.
            // What was seen is recorded; FTF says NOT DIPPED rather than inventing a depth.
            add("a pipe nobody could dip is still recorded", () =>
            {
                // SW, where there is no other structure: an undipped pipe aimed at one would
                // connect itself and quietly change what the later steps are looking at.
                _undippedBefore = S("1047").Field.Pipes.Count;
                Check(Capture("8\"", "PVC", "SW", ""), "the buttons were all there");
            }, 1500);
            add("the undipped pipe is kept, and says so", () =>
            {
                var st = S("1047");
                Check(st.Field.Pipes.Count == _undippedBefore + 1,
                      "a pipe with no measure down is recorded all the same (" + st.Field.Pipes.Count + ")");
                var p = st.Field.Pipes.Last();
                Check(p.WidthIn == 8 && p.Material == "PVC" && p.Direction.Text == "SW",
                      "8\" PVC SW kept exactly");
                Check(!p.MeasuredDip.HasValue, "with no measure down");
                Check(p.Reference == FU.MeasurementReference.Unspecified &&
                      p.ReferenceBasis == FU.ReferenceBasis.NotStated,
                      "and no claim about what it was measured to, since nothing was");
                Check(Shown(Quick) && !ChosenSize.HasValue, "and the next slot starts clean");
            }, 1500);

            // ---- the values no button carries ----------------------------------------------
            add("Larger, an exact 17.5, More... and a custom material", () =>
            {
                Check(OpenChooser == "Size", "the new slot is asking for the size");
                Choose("Larger");
                var larger = ChooserButtons;
                Log("   larger: " + string.Join(" ", larger.ToArray()));
                Check(larger.Contains("Usual sizes"), "Larger offers the way back");
                TypeInChooser("17.5");
                Choose("Use");
                Check(ChosenSize == 17.5 && OpenChooser == "Type", "17.5\" kept exactly and the strip moved on (" + ChosenSize + ")");

                Choose("More...");
                var more = ChooserButtons;
                Log("   more materials: " + string.Join(" ", more.ToArray()));
                Check(more.Contains("VCP") || more.Contains("DI"), "More... shows the other office materials");
                TypeInChooser("ribbed pvc");
                Choose("Use");
                Check(ChosenType == "RIBBED PVC" && OpenChooser == "Direction", "a typed material is kept, in capitals (" + ChosenType + ")");
            }, 2000);
            add("sixteen directions, a typed bearing, an azimuth and the dial", () =>
            {
                var ok = 0;
                foreach (var name in FU.DirectionShortcuts.Names)
                {
                    OpenChooserFor("Direction");
                    if (Choose(name) && ChosenDirection == name &&
                        ChosenAzimuth == FU.DirectionShortcuts.For(name).AzimuthDegrees) ok++;
                }
                Check(ok == 16, "all 16 directions set their own bearing (" + ok + "/16)");

                OpenChooserFor("Direction");
                TypeInChooser("N22-30-00W");
                Choose("Use");
                Check(ChosenAzimuth.HasValue && Math.Abs(ChosenAzimuth.Value - 337.5) < 0.01, "a typed bearing is read (" + ChosenAzimuth + ")");

                OpenChooserFor("Direction");
                TypeInChooser("");
                TypeInChooser("112.30");        // a bare number is an azimuth, read as the notes write one: 112-30-00
                Choose("Use");
                Check(ChosenAzimuth.HasValue && Math.Abs(ChosenAzimuth.Value - 112.5) < 0.01, "a typed azimuth is read (" + ChosenAzimuth + ")");

                OpenChooserFor("Direction");
                Check(Choose("\u25ce dial"), "the dial is there for anyone who would rather aim");
                Check(Choose("rows"), "and it goes back to the rows");

                OpenChooserFor("Direction");
                Check(Choose("?"), "an unknown direction is still possible");
                Check(ChosenDirection == "?", "and is recorded as unknown");

                OpenChooserFor("Direction");
                Choose("N/NW");
            }, 2500);
            add("the unusual pipe is kept exactly", () =>
            {
                OpenChooserFor("Reference");
                Choose("Not stated");
                MdText = "6.41";
                PressInMd(Keys.Enter);
            }, 1500);
            add("17.5 and the custom material kept; reference not stated", () =>
            {
                var p = S("1047").Field.Pipes.Last();
                Check(p.WidthIn == 17.5 && p.Material == "RIBBED PVC" && p.Direction.Text == "N/NW" && p.MeasuredDip == 6.41,
                      "17.5\" RIBBED PVC N/NW 6.41 kept exactly");
                Check(p.Reference == FU.MeasurementReference.Unspecified && p.ReferenceBasis == FU.ReferenceBasis.NotStated,
                      "Not stated is honoured: FTF does not fill it in");
                Check(p.ReferenceUnconfirmed, "and it reads as unconfirmed for QC and slopes");
                Shot("05c-pipe-cards");
            }, 1500);

            // ---- the buttons follow the structure ------------------------------------------
            add("change 1047 to a CB with a slot open", () =>
            {
                Click("+ Add pipe");
            }, 1200);
            add("CB buttons and W x L", () =>
            {
                SetStructureType("CB");
            }, 2000);
            add("the strip follows the structure", () =>
            {
                var sizes = ChooserButtons;
                Log("   CB sizes: " + string.Join(" ", sizes.ToArray()));
                Check(sizes.Contains("6\"") && sizes.Contains("24\""), "a CB shows catch basin sizes");
                Check(S("1047").EffectiveCode == "CB", "the drafter's type is what drives the buttons");
                Check(!string.IsNullOrEmpty(S("1047").Field.FieldCode) || S("1047").TypeSetByDrafter,
                      "the field code is untouched; the type is marked as the drafter's");
            }, 1500);
            add("back to SDMH", () =>
            {
                SetStructureType("SDMH");
            }, 2000);
            add("and the storm sizes are back", () =>
            {
                Check(ChooserButtons.Contains("8\""), "back to the storm sizes for SDMH");
                PressInMd(Keys.Escape);
                Check(!Shown(Quick), "Escape closes the slot");
            }, 1500);

            add("N/NW pipe: Connects to... 1048", () =>
            {
                _nnw = S("1047").Field.Pipes.Count - 1;      // the unusual pipe just entered
                Send("_.ZOOM _C 5120,5000 30 ");
                // Picking the far structure by hand is behind More: a normal pipe never needs it.
                CardMore(_nnw, "Connects to...", "Change where it connects");
                Send("5120,5000 ");
                Send("UI TEST WALK\n");
            }, 800);
            add("manual connection on the card", () =>
            {
                Check(StatusOf("1047", _nnw) == "ManualOverride->1048", "Connects to... records a drafter's manual connection (" + StatusOf("1047", _nnw) + ")");
                var c = Project.ConnectionFor(S("1047").Id, S("1047").Field.Pipes[_nnw].Id);
                Check(c != null && c.ToPipeId == null && c.Basis.Any(b => b.Contains("chosen manually by the drafter")), "recorded as the drafter's choice, not a field observation");
                Log("   card: " + Headline(_nnw) + "  |  " + Detail(_nnw));
                Check(Detail(_nnw).Contains("picked by drafter") && Detail(_nnw).Contains("not drawn yet") && Detail(_nnw).Contains("no slope yet"), "card shows where it goes, not drawn, no slope yet");
                Check(ButtonStarting(Card(_nnw), "Open ") != null && FindButton(Card(_nnw), "Draw pipe + label") != null, "card offers Open and Draw pipe + label");
                Check(FindButton(Card(_nnw), "Suggest") == null && FindButton(Card(_nnw), "Delete") == null,
                      "a connected pipe does not carry Suggest or Delete on its face");
                ClickIn(Card(_nnw), "Draw pipe + label");
            }, 3000);
            add("drawn from the card", () =>
            {
                var c = Counts();
                // 17.5" is over the 12" double-line threshold, so the pipe is two lines with one label.
                Check(c["pipe"] == 2 && c["pipelabel"] == 1, "Draw pipe + label drew the pipe (double line) and its label (" + c["pipe"] + ", " + c["pipelabel"] + ")");
                // Drawn: the card stops offering Draw, and Redraw moves out of the way into More.
                Check(Detail(_nnw).Contains("drawn") && FindButton(Card(_nnw), "Draw pipe + label") == null &&
                      ((System.Collections.IEnumerable)Card(_nnw).GetType().GetProperty("MoreActions").GetValue(Card(_nnw), null))
                          .Cast<Button>().Any(b => b.Text == "Redraw pipe + label"),
                      "the card now says drawn, and Redraw is under More");
                Shot("05d-card-connected");
                Send("_.U ");
            }, 3000);
            add("walk to 1048", () =>
            {
                Check(Counts()["pipe"] == 0, "undo removed the pipe drawn from the card");
                Press(ButtonStarting(Card(_nnw), "Open "));
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
            }, 3000);

            // ---- the optional pipe count ------------------------------------------------
            // A drafter who knows how many pipes are here says so once and the slot counts them
            // off. It is wording only: a number never creates a pipe, and a fourth one is fine.
            add("an empty structure offers the count", () =>
            {
                var row = Field<Control>("_countRow");
                var names = row == null ? new List<string>() : row.Controls.OfType<Button>().Select(b => b.Text).ToList();
                Log("   count row: " + string.Join(" ", names.ToArray()));
                Check(row != null && Shown(row), "How many pipes? is offered where nothing is entered yet");
                Check(names.Contains("1") && names.Contains("5") && names.Contains("+"), "1 to 5, and + for more");
                ClickIn(row, "3");
            }, 1500);
            add("choosing 3 creates nothing", () =>
            {
                Check(S("1048").Field.Pipes.Count == 0,
                      "saying 3 created no pipes (" + S("1048").Field.Pipes.Count + " records)");
                Check(Shown(Quick) && SlotWords == "Pipe 1 of 3", "the first slot opened and counts (" + SlotWords + ")");
                Check(!Shown(Field<Control>("_countRow")), "the count row steps out of the way once entry starts");
                PressInMd(Keys.Escape);
            }, 1500);
            add("an unfilled slot is nothing at all", () =>
            {
                Check(S("1048").Field.Pipes.Count == 0, "the abandoned slot left no record (" + S("1048").Field.Pipes.Count + ")");
                Check(Field<Control>("_cards").Controls.Cast<Control>().Any(x => (x.Tag as string) == "incoming"),
                      "and the pipe from 1047 is still waiting to be completed");
                Press(ButtonStarting(Field<Control>("_cards"), "Complete pipe from SDMH 1047"));
            }, 3000);
            add("Complete pipe from 1047: copied, not observed", () =>
            {
                Check(Shown(Quick) && QuickField<Label>("_title").Text.StartsWith("Complete pipe from SDMH 1047"),
                      "the panel opens to complete the pipe (" + QuickField<Label>("_title").Text + ")");
                Check(ChosenDirection == "S/SE" && ChosenSize == 17.5 && ChosenType == "RIBBED PVC",
                      "opposite direction, size and material copied (S/SE 17.5\" RIBBED PVC)");
                Check(MdText == "" && MdEnabled, "the MD starts empty -- never copied from 1047 -- and is ready to type");
                Check(ChosenReference == FU.MeasurementReference.Invert,
                      "the reference starts at the office default here too, not at the far end's");
                Check(Shown(QuickField<Label>("_prefillNote")) && QuickField<Label>("_prefillNote").Text.Contains("Copied from SDMH 1047"), "the panel says what was copied");
                Check(FindButton(Quick, "Add matching pipe") == null && FindButton(Quick, "Add + next") == null, "no submit button: Enter finishes it");
                Check(S("1048").Field.Pipes.Count == 0, "still nothing added while the panel is open");
                Shot("05f-complete-pipe-panel");
                PressInMd(Keys.Escape);
                Check(S("1048").Field.Pipes.Count == 0 && Field<Control>("_cards").Controls.Cast<Control>().Any(x => (x.Tag as string) == "incoming"),
                      "Cancel adds nothing; the pipe still waits to be completed");
                Press(ButtonStarting(Field<Control>("_cards"), "Complete pipe from SDMH 1047"));
                Check(SlotWords.StartsWith("Complete pipe"), "completing a connected pipe says so instead of counting (" + SlotWords + ")");
                // At 1048 the crew measured an 18" pipe: the drafter changes the size, keeps the rest, enters the MD.
                OpenChooserFor("Size");
                Check(Choose("18\""), "the size chooser opens on the usual sizes, 18\" among them");
                Check(ChosenSize == 18 && ChosenType == "RIBBED PVC" && ChosenDirection == "S/SE",
                      "changing the size keeps what was copied");
                Check(ChosenReference == FU.MeasurementReference.Invert, "and the reference is already the office default");
                MdText = "6.9";
                PressInMd(Keys.Enter);
            }, 500);
            add("the other end is tied to the connection", () =>
            {
                var b = S("1048");
                Check(b.Field.Pipes.Count == 1, "Enter completed the far end: one pipe at 1048 (" + b.Field.Pipes.Count + ")");
                if (b.Field.Pipes.Count != 1) return;
                var p = b.Field.Pipes[0];
                Check(p.Direction.Text == "S/SE" && p.WidthIn == 18 && p.Material == "RIBBED PVC" && p.MeasuredDip == 6.9 &&
                      p.Reference == FU.MeasurementReference.Invert && p.Source == FU.ObservationSource.UserEntry, "S/SE 18\" RIBBED PVC IE 6.9 entered at 1048");
                Check(p.Prefilled.SequenceEqual(new[] { "direction", "material" }) && p.PrefilledFrom.StartsWith("SDMH 1047"),
                      "direction and material marked as copied from 1047; the size is 1048's own (" + string.Join(",", p.Prefilled) + ")");
                var a = S("1047");
                var c = Project.ConnectionFor(a.Id, a.Field.Pipes[_nnw].Id);
                Check(c != null && c.ToPipeId == p.Id && c.Status == FU.ConnectionStatus.ManualOverride && Project.ConnectionFor(b.Id, p.Id) == c,
                      "the new pipe is the other end of the same connection, still the drafter's manual connection");
                Check(Project.Overrides.Any(o => o.What == "Pipe completed from connected pipe"), "completion recorded as an override");
                Check(!Field<Control>("_cards").Controls.Cast<Control>().Any(x => (x.Tag as string) == "incoming"), "nothing left waiting at 1048");
                Log("   card: " + Headline(0) + "  |  " + Detail(0));
                // 1047's end of this pipe was entered Not stated on purpose, so the two dips are not
                // comparable yet and the card says so. A slope from two comparable dips is covered
                // by the drawn pipe labels and by the rebuild step further down.
                Check(Detail(0).Contains("no slope") && Detail(0).Contains("copied from SDMH 1047"),
                      "the card says why there is no slope yet, and what was copied (" + Detail(0) + ")");
                Shot("05g-completed-at-1048");
                ClickIn(Card(0), "Edit");
                Check(ChosenDirection == "S/SE" && ChosenSize == 18, "editing opens the panel on that pipe's values (" + ChosenSize + ")");
                OpenChooserFor("Direction");
                Choose("S/SE");         // the drafter checks the book: S/SE was observed here too
                PressInMd(Keys.Enter);
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
                Check(Field<Label>("_structureTitle").Text.EndsWith("1047") && Cards().Count == _nnw + 1,
                      "Back returned to 1047 and every pipe entered there (" + Cards().Count + " cards)");
                Window.GetType().GetMethod("OpenStructure", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(Window, new object[] { S("1046").Id });
            }, 3000);
            add("a CB from the notes: W x L and catch basin buttons", () =>
            {
                Check(Shown(Field<TextBox>("_insideWidth")) && Shown(Field<TextBox>("_insideLength")) && !Shown(Field<ComboBox>("_diameter")),
                      "CB 1046 (rectangular) asks for inside W x L, not a diameter");
                Check(Field<Label>("_choiceNote").Text == "Buttons for: Catch basins and inlets", "CB 1046 gets the catch basin pipe buttons");
                Click("+ Add pipe");
                Check(ChooserButtons.First() == "6\"", "its first size button is 6\"");
                PressInMd(Keys.Escape);
            }, 3000);

            // Drawing with existing pipe choices ---------------------------------
            add("view back to 1045", () => ZoomTo(5000, 5000), 500);
            add("back to 1045", () => PickStructure("Select structure point...", 1045), 500);
            add("draw 1 (existing hand pipe: Keep)", () =>
            {
                Click("Draw this structure's pipes");
                Send("Keep ");
            }, 3000);
            // One more FTF line than the confirmed pipes: the 6" W pipe runs outside the survey
            // limits, so it is drawn as a stub with a ~ at its loose end.
            add("after Keep", () => CountsAre(2, 1, "Keep"), 3000);
            add("draw 2 (Keep FTF N pipe, New beside hand pipe)", () =>
            {
                Click("Draw this structure's pipes");
                Send("Keep ");
                Send("New ");
            }, 500);
            add("after New", () => { CountsAre(4, 1, "New"); Log("   pipe labels: " + Texts(20)); }, 3000);
            add("draw 3 (Keep, Replace)", () =>
            {
                Click("Draw this structure's pipes");
                Send("Keep ");
                Send("Replace ");
            }, 500);
            add("after Replace", () => CountsAre(4, 0, "Replace"), 3000);
            add("undo the Replace", () => Send("_.U "), 500);
            add("after undo", () => CountsAre(4, 1, "Undo of Replace"), 3000);
            add("draw 4 (Keep, Update existing)", () =>
            {
                Click("Draw this structure's pipes");
                Send("Keep ");
                Send("Update ");
            }, 500);
            add("after Update", () =>
            {
                Log("   status bar: " + Field<Label>("_status").Text + " | session message: " + Convert.ToString(SessionType.GetField("LastMessage").GetValue(null)));
                CountsAre(3, 0, "Update");
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
                Check(text.Contains("SILTED 25%") && text.Contains("12\" RCP (N) IE = 322.01'"), "edited leader placed as an MLeader with the edited text");
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
                Diag("before reload click");
                Click("Reload from drawing");
            }, 8000);
            // Drafted pipes and the leader now pass through the point; freeze their
            // layers so the pick lands on the COGO point, as a drafter would.
            add("freeze pipe layers", () => { Diag("after reload"); SetFrozen(true); }, 1000);
            add("view pick 1045 in the reopened drawing", () => { Diag("before zoom"); ZoomTo(5000, 5000); }, 500);
            // A just-reopened drawing is slow enough that the window's own queued action -- the
            // reload -- can still be waiting when the next click arrives. Run the queue down
            // first, or the click's FTFDIPACT would run the reload instead and the point typed
            // for the pick would land at the bare Command: prompt.
            for (var tries = 0; tries < 5; tries++)
                add("let the reopened drawing run its queued actions", () => { if (Pending() > 0) Send("FTFDIPACT "); }, 2000);
            add("pick 1045 in the reopened drawing", () =>
            {
                Check(Pending() == 0, "the window's queued actions have all run (" + Pending() + " left)");
                PickStructure("Select structure point...", 1045);
            }, 3000);
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
                Check(c["pipe"] == 3 && c["structurelabel"] == 1 && c["pipelabel"] == 3, "drafting persisted, stub included (" + c["pipe"] + " pipes, " + c["pipelabel"] + " labels, " + c["structurelabel"] + " leader)");
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

            // Back at 1048, where one pipe is already entered: the count is not asked again, and
            // each slot the drafter opens is only a slot until Enter makes it a record.
            add("open 1048 again", () =>
                Window.GetType().GetMethod("OpenStructure", BindingFlags.NonPublic | BindingFlags.Instance)
                      .Invoke(Window, new object[] { S("1048").Id }), 1500);
            add("the count is not asked where pipes exist", () =>
            {
                Check(S("1048").Field.Pipes.Count == 1, "1048 still has just the completed pipe (" + S("1048").Field.Pipes.Count + ")");
                Check(!Shown(Field<Control>("_countRow")), "How many pipes? is not asked again");
                Click("+ Add pipe");
            }, 1500);
            add("a second slot, then two more pipes", () =>
            {
                Check(Shown(Quick) && SlotWords == "Pipe 2", "the slot says which pipe this is (" + SlotWords + ")");
                Check(S("1048").Field.Pipes.Count == 1, "opening a slot created nothing (" + S("1048").Field.Pipes.Count + ")");
                Check(Capture("12\"", "RCP", "N", "3.10"), "the second pipe's buttons were all there");
            }, 2000);
            add("one Enter, one record", () =>
            {
                Check(S("1048").Field.Pipes.Count == 2, "the second pipe is recorded (" + S("1048").Field.Pipes.Count + ")");
                Check(Shown(Quick) && SlotWords == "Pipe 3", "and the next slot is counting (" + SlotWords + ")");
                Check(Capture("8\"", "PVC", "W", "2.80"), "the third pipe's buttons were all there");
            }, 2000);
            add("three records, and an extra slot is still nothing", () =>
            {
                var st = S("1048");
                Check(st.Field.Pipes.Count == 3, "three pipes entered here, three records (" + st.Field.Pipes.Count + ")");
                Check(st.Field.Pipes[1].WidthIn == 12 && st.Field.Pipes[1].MeasuredDip == 3.10 &&
                      st.Field.Pipes[2].WidthIn == 8 && st.Field.Pipes[2].MeasuredDip == 2.80, "both are exactly as entered");
                Check(Shown(Quick) && SlotWords == "Pipe 4", "a fourth slot is offered straight away (" + SlotWords + ")");
                PressInMd(Keys.Escape);
                Check(!Shown(Quick) && S("1048").Field.Pipes.Count == 3, "Escape leaves the three entered pipes alone");
                Shot("12b-pipe-count");
                Tab(0);
            }, 2000);

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
                Send("STRIPEASEMENT  6150,5325 6146,5311 6140,5200 6150,5050 6120,4836.25  Centered 20  " +
                     LotLine(0) + LotLine(1) + "  "), 2500);
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
                Send("STRIPEASEMENT 6000,5400 6300,5300 6280,4776.25  Centered 10 20 " +
                     LotLine(0) + LotLine(2) + " 6250,4787 "), 2500);
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

                // ---- the drafting panel: what it will look like, before anything is drawn
                CheckDrafting(form);
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

                // What the drafting panel was set to is what was drawn, and it is stored with
                // the easement so a rebuild draws it the same way.
                Check(r.Drafting != null && r.Drafting.HatchPattern == "ANSI38",
                      "the hatch pattern picked in the preview is stored with the easement (" + (r.Drafting == null ? "nothing stored" : r.Drafting.HatchPattern) + ")");
                Check(r.Drafting != null && r.Drafting.TextLayer == null && r.Drafting.BoundaryLayer == null,
                      "only what was changed is stored; the layers still follow the profile");
                var drawnB = EasementTexts(r.Id);
                foreach (var line in drawnB.Where(x => x.StartsWith("HATCH", StringComparison.Ordinal) || x.StartsWith("DIM", StringComparison.Ordinal))) Log("   " + line);
                Check(drawnB.Any(x => x.StartsWith("HATCH ANSI38 ", StringComparison.Ordinal)), "the easement is hatched with the pattern picked in the preview");
                if (temporary != null)
                {
                    var drawnT = EasementTexts(temporary.Id);
                    foreach (var line in drawnT.Where(x => x.StartsWith("HATCH", StringComparison.Ordinal))) Log("   temporary: " + line);
                    Check(drawnT.Any(x => x.StartsWith("HATCH ANSI37 ", StringComparison.Ordinal)),
                          "the temporary construction easement gets its own hatch pattern");
                    Check(temporary.Drafting != null && temporary.Drafting.HatchPattern == "ANSI38",
                          "the temporary easement remembers the same drafting choices");
                }
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
                try
                {
                    if (_pickbox > 0) AcApp.SetSystemVariable("PICKBOX", _pickbox);
                    if (_aperture > 0) AcApp.SetSystemVariable("APERTURE", _aperture);
                    Log("PICKBOX and APERTURE put back to " + _pickbox + " and " + _aperture);
                }
                catch (System.Exception ex) { Log("pick size not put back: " + ex.Message); }

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
        /// <summary>
        /// The preview's drafting panel: it starts from the office settings, it shows the
        /// drafting live on the plan, and what is changed there is what gets drawn.
        /// </summary>
        private static void CheckDrafting(Form form)
        {
            Func<string, object> field = name => form.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form);
            var canvas = (Control)field("_canvas");
            Func<string, object> canvasValue = name => canvas.GetType().GetProperty(name).GetValue(canvas, null);
            var drafting = (FieldCodes.Settings.EasementSettings)form.GetType()
                .GetProperty("Chosen", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form, null);

            var hatchOn = (CheckBox)field("_hatchOn");
            var pattern = (ComboBox)field("_hatchPattern");
            var temporaryOn = (CheckBox)field("_temporaryHatchOn");
            var temporaryPattern = (ComboBox)field("_temporaryHatchPattern");
            var where = (ComboBox)field("_labelWhere");
            var layers = (Dictionary<string, TextBox>)field("_layerBoxes");

            Check(hatchOn != null && pattern != null, "the preview has a drafting panel with the hatch on it");
            Check(temporaryOn != null && temporaryPattern != null, "a temporary construction easement gets its own hatch setting");
            Check(pattern.Text == drafting.HatchPattern, "the panel starts from the office hatch pattern (" + pattern.Text + ")");
            Check(temporaryPattern.Text == "ANSI37", "the temporary easement starts from its own office pattern (" + temporaryPattern.Text + ")");
            Log("   layers offered: " + string.Join(", ", layers.Select(x => x.Key + "=" + x.Value.Text).ToArray()));
            Check(layers.ContainsKey("Hatch") && layers.ContainsKey("Dimensions") && layers.ContainsKey("Temporary hatch"),
                  "the layers each piece goes on can be seen and changed");
            Check(layers["Hatch"].Text == drafting.HatchLayer, "the layer boxes show the profile's layers");

            // The plan preview draws what the panel says.
            Check((string)canvasValue("HatchPattern") == drafting.HatchPattern, "the plan shows the hatch");
            Check((string)canvasValue("TemporaryHatchPattern") == "ANSI37", "the plan shows the temporary hatch separately");
            var labels = (System.Collections.IList)canvasValue("Labels");
            Check(canvasValue("Title") != null && labels != null && labels.Count > 0,
                  "the plan shows the title and the course labels (" + (labels == null ? 0 : labels.Count) + ")");
            Check(canvasValue("Dimension") != null, "the plan shows the width dimension");

            // Turning the hatch off takes it off the plan; back on puts it back.
            hatchOn.Checked = false;
            Check((string)canvasValue("HatchPattern") == null, "unticking the hatch takes it off the plan");
            hatchOn.Checked = true;

            // The centerline is what gets labelled; switching to the outline moves the labels.
            var centerlineLabels = labels.Count;
            where.SelectedIndex = 1;
            var outlineLabels = ((System.Collections.IList)canvasValue("Labels")).Count;
            Check(outlineLabels != centerlineLabels || !drafting.LabelCenterline,
                  "labelling the outline instead of the centerline changes the plan (" + centerlineLabels + " -> " + outlineLabels + ")");
            where.SelectedIndex = 0;
            Check(drafting.LabelCenterline, "back to labelling the centerline");

            // A different hatch for this easement only.
            pattern.Text = "ANSI38";
            Check(drafting.HatchPattern == "ANSI38" && (string)canvasValue("HatchPattern") == "ANSI38", "a new pattern reaches the plan at once");
        }

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
                    var hatch = e as AcDb.Hatch;
                    if (hatch != null) list.Add("HATCH " + hatch.PatternName + " on " + e.Layer);
                    var dim = e as AcDb.Dimension;
                    if (dim != null) list.Add("DIM " + dim.DimensionStyleName + " on " + e.Layer);
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

        /// <summary>
        /// The seeded lot lines, by drawing handle. A command that says "select the lot line" is
        /// answered with the line itself rather than a coordinate on it: what is under a typed
        /// coordinate depends on the view and on the drafter's own pickbox, and neither is the
        /// subject of this test.
        /// </summary>
        private static readonly List<string> _lotLines = new List<string>();

        /// <summary>A lot line as an answer to a selection prompt: top 0, bottom 1, side 2.</summary>
        private static string LotLine(int which)
        {
            return which < _lotLines.Count ? "(handent \"" + _lotLines[which] + "\") " : "";
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
                    _lotLines.Add(line.Handle.ToString());
                }
                tr.Commit();
                Log("   lot lines seeded: " + string.Join(", ", _lotLines.ToArray()));
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
                    // A settled structure keeps the connections collapsed to a line, so open them
                    // the way the drafter would before checking they are usable at this text size.
                    type.GetMethod("ShowConnections", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(form, new object[] { true });
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
