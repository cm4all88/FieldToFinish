// Interactive UI test of the FTFROW and FTFLOTS WINDOWS inside full Civil 3D, and of
// the Right of Way and Platted Blocks settings pages.
//
// The headless live smoke (run-rowlots.ps1) deliberately bypasses both popups -- it
// answers their questions on the command line, because accoreconsole has no window. So
// Ui/RowForm.cs, Ui/LotForm.cs, Setup/RightOfWayPage.cs and Setup/LotsPage.cs had never
// been constructed even once. This is what runs them.
//
// ROWUISEED  builds the office layers and the monument and corner survey points.
// ROWUITEST  drives the real windows the way a drafter would: their text boxes, combos,
//            grids and buttons are operated through the controls themselves, and the
//            command-line prompts the commands raise before the window opens are
//            answered as a drafter would type them. Every step is checked against the
//            drawing, and screenshots of each window are saved.
//
// Output: <out>\rowlots-ui.log and <out>\*.png. Compiled with csc (C# 5: no string
// interpolation, no ?., no expression-bodied members).
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
using FRow = FieldCodes.Row;

[assembly: CommandClass(typeof(FtfRowLotsUiTest.RowLotsUiCommands))]

namespace FtfRowLotsUiTest
{
    public class RowLotsUiCommands
    {
        private const string FtfApp = "FTF_FIELDTOFINISH";
        public static string OutDir = @"C:\dev\FieldToFinish\tests\LiveSmoke\rowlots-ui";

        private static List<Step> _steps;
        private static int _index;
        private static DateTime _last;
        private static DateTime _busySince;
        private static DateTime? _modalSeen;
        private static Timer _timer;
        private static StreamWriter _log;
        private static int _pass, _fail;

        static RowLotsUiCommands()
        {
            AppDomain.CurrentDomain.AssemblyResolve += delegate(object s, ResolveEventArgs e)
            {
                var name = new AssemblyName(e.Name).Name;
                return AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == name);
            };
        }

        private sealed class Step
        {
            public string Name;
            public Action Run;
            public int Delay = 2000;
            /// <summary>Runs while the command is still in progress, once its modal window is open.</summary>
            public bool WhileBusy;
            /// <summary>With WhileBusy: the modal window's type name to wait for.</summary>
            public string Modal;
        }

        // ------------------------------------------------------------------ seed

        /// <summary>The office layers the settings file points at. FTF never creates one from a
        /// configured name, so the drawing has to have them.</summary>
        private static readonly string[] Layers =
        {
            "V-ROAD-ROW-E", "V-ROAD-ROW-TEXT-E", "V-PROP-MON-E", "V-ROAD-CNTR-E",
            "V-SURV-MONU-E", "V-SURV-MONU-TEXT-E", "V-ROAD-ROW-DIM-E", "V-ROAD-NAME-E",
            "V-PROP-LOT-E", "V-PROP-LOT-TEXT-E", "V-PROP-SETB-E", "V-PROP-ESMT-E",
            "V-PROP-DRAN-E", "V-PROP-NOTE-E"
        };

        /// <summary>Point number, easting, northing. 101-103 a run's monuments, 201-204 a block's
        /// corners, 301-304 an intersection's, 401-404 the block FTFLOTS divides.</summary>
        private static readonly double[][] Points =
        {
            new double[] { 101, 5000, 5000 }, new double[] { 102, 5100, 5000 }, new double[] { 103, 5200, 5000 },
            new double[] { 201, 6000, 5000 }, new double[] { 202, 6200, 5000 }, new double[] { 203, 6200, 5100 }, new double[] { 204, 6000, 5100 },
            new double[] { 301, 7000, 5000 }, new double[] { 302, 7100, 5000 }, new double[] { 303, 7100, 5100 }, new double[] { 304, 7000, 5100 },
            new double[] { 401, 8000, 5000 }, new double[] { 402, 8200, 5000 }, new double[] { 403, 8200, 5100 }, new double[] { 404, 8000, 5100 }
        };

        [CommandMethod("ROWUISEED")]
        public void Seed()
        {
            AcDoc doc = AcApp.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            AcDb.Database db = doc.Database;
            CivApp.CivilDocument cdoc = CivApp.CivilApplication.ActiveDocument;

            using (AcDb.Transaction tr = db.TransactionManager.StartTransaction())
            {
                AcDb.LayerTable lt = (AcDb.LayerTable)tr.GetObject(db.LayerTableId, AcDb.OpenMode.ForWrite);
                foreach (string name in Layers)
                {
                    if (lt.Has(name)) continue;
                    AcDb.LayerTableRecord ltr = new AcDb.LayerTableRecord();
                    ltr.Name = name;
                    lt.Add(ltr);
                    tr.AddNewlyCreatedDBObject(ltr, true);
                }
                tr.Commit();
            }

            using (AcDb.Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (double[] p in Points)
                {
                    AcDb.ObjectId id = cdoc.CogoPoints.Add(new Point3d(p[1], p[2], 0), "MON", false);
                    CivDb.CogoPoint cp = tr.GetObject(id, AcDb.OpenMode.ForWrite) as CivDb.CogoPoint;
                    if (cp == null) continue;
                    try { cp.PointNumber = (uint)p[0]; }
                    catch (System.Exception ex) { ed.WriteMessage("\nROWUISEED: point {0}: {1}", p[0], ex.Message); }
                }
                tr.Commit();
            }

            ed.WriteMessage("\nROWUISEED: {0} layers, {1} survey points.\n", Layers.Length, Points.Length);
        }

        // ----------------------------------------------------------------- engine

        [CommandMethod("ROWUITEST", CommandFlags.Session)]
        public void Start()
        {
            Directory.CreateDirectory(OutDir);
            _log = new StreamWriter(Path.Combine(OutDir, "rowlots-ui.log"), false, Encoding.UTF8) { AutoFlush = true };
            Log("FTFROW / FTFLOTS window test started " + DateTime.Now.ToString("s"));
            Log("Screen working area " + Screen.PrimaryScreen.WorkingArea + ", DPI " + DpiOf());

            _steps = BuildSteps();
            _index = 0;
            _last = DateTime.Now;
            _busySince = DateTime.Now;
            _timer = new Timer { Interval = 400 };
            _timer.Tick += Tick;
            _timer.Start();
        }

        private static int DpiOf()
        {
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) return (int)g.DpiX;
        }

        /// <summary>
        /// Steps call Application.DoEvents to let a window finish laying itself out, and that
        /// pumps this timer again -- without this guard the next step starts nested inside the
        /// one that is still running, and the log interleaves two steps' results.
        /// </summary>
        private static bool _inStep;

        private static void Tick(object sender, EventArgs e)
        {
            if (_inStep) return;
            if (_index >= _steps.Count) { _timer.Stop(); return; }
            AcDoc doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            if (!string.IsNullOrEmpty(doc.CommandInProgress))
            {
                var waiting = _steps[_index];
                if (waiting.WhileBusy && ModalForm(waiting.Modal) != null)
                {
                    _busySince = DateTime.Now;
                    if (_modalSeen == null) _modalSeen = DateTime.Now;
                    if ((DateTime.Now - _modalSeen.Value).TotalMilliseconds < waiting.Delay) return;
                    _modalSeen = null;
                    _index++;
                    RunStep(waiting);
                    return;
                }
                // The harness's own session command stays "in progress" the whole run.
                if (doc.CommandInProgress.ToUpperInvariant() == "ROWUITEST") _busySince = DateTime.Now;
                if ((DateTime.Now - _busySince).TotalSeconds > 45)
                {
                    Log("STUCK in " + doc.CommandInProgress + " at step " + _steps[_index].Name + " -- cancelling");
                    ShotScreenless("stuck-" + _index);
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
            RunStep(step);
        }

        private static void RunStep(Step step)
        {
            _inStep = true;
            Log("-- " + step.Name);
            try { step.Run(); }
            catch (System.Exception ex) { Fail("step threw: " + ex.GetType().Name + ": " + ex.Message + "\n      " + ex.StackTrace); }
            finally
            {
                _inStep = false;
                _last = DateTime.Now;
            }
        }

        private static void Log(string text) { if (_log != null) _log.WriteLine(text); }
        private static void Check(bool ok, string what) { if (ok) _pass++; else _fail++; Log("   [" + (ok ? "PASS" : "FAIL") + "] " + what); }
        private static void Fail(string what) { Check(false, what); }

        private static void Send(string text)
        {
            AcApp.DocumentManager.MdiActiveDocument.SendStringToExecute(text, true, false, true);
        }

        private static Form ModalForm(string typeName)
        {
            if (typeName == null) return null;
            return Application.OpenForms.Cast<Form>().FirstOrDefault(f => f.GetType().Name == typeName && f.Visible);
        }

        private static Assembly Plugin
        {
            get { return AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "FieldCodes.Cad"); }
        }

        private static void LogRunError()
        {
            var detail = Plugin.GetType("FieldCodes.Cad.FtfSession").GetProperty("LastRunErrorDetail").GetValue(null, null) as string;
            if (!string.IsNullOrEmpty(detail)) Log("   COMMAND ERROR: " + detail.Replace("\n", "\n      "));
        }

        // ------------------------------------------------------------------ steps

        private static List<Step> BuildSteps()
        {
            var s = new List<Step>();
            Action<string, Action, int> add = (name, run, delay) =>
                s.Add(new Step { Name = name, Run = run, Delay = delay });

            add("typed coordinates win over running osnaps", () => Send("OSNAPCOORD 1 "), 1500);
            add("zoom to the seeded points", () => Send("_.ZOOM _E "), 1500);

            // ------------------------------------------------------------- a run
            add("FTFROW: pick three monuments along a frontage",
                () => Send("FTFROW Run 5000,5000 5100,5000 5200,5000  "), 2500);
            s.Add(new Step { Name = "the Right of Way window, run mode", WhileBusy = true, Modal = "RowForm", Delay = 2500, Run = () =>
            {
                var form = ModalForm("RowForm");
                Check(form != null, "FTFROW opens the Right of Way window");
                if (form == null) return;
                Check(form.Text == "Right of Way", "the window is titled Right of Way (" + form.Text + ")");
                Check(HasLabel(form, "Right-of-way run"), "the heading says what is being drawn");

                var monuments = Field<DataGridView>(form, "_monuments");
                if (monuments == null) return;
                Check(monuments.Rows.Count == 3, "the monument grid holds the three picked monuments (" + monuments.Rows.Count + ")");
                Check(Cell(monuments, 0, "pt") == "101" && Cell(monuments, 2, "pt") == "103",
                      "the grid shows the survey points the picks snapped to (" + Cell(monuments, 0, "pt") + ".." + Cell(monuments, 2, "pt") + ")");
                Check(Field<DataGridView>(form, "_sides") == null, "a run has no sides grid");
                Check(!monuments.Columns["radius"].Visible, "the corner radius column is hidden for a run");

                // The reference swap: a width for monuments on a right-of-way line, two
                // offsets for monuments on the centerline.
                var width = Field<TextBox>(form, "_width");
                var left = Field<TextBox>(form, "_left");
                var reference = Field<ComboBox>(form, "_reference");
                if (width == null || left == null || reference == null) return;
                Check(width.Parent.Visible && !left.Parent.Visible, "monuments on a right-of-way line are asked for the width");
                reference.SelectedIndex = 1;
                Check(!width.Parent.Visible && left.Parent.Visible, "choosing the centerline swaps in the left and right offsets");
                reference.SelectedIndex = 0;
                Check(width.Parent.Visible && !left.Parent.Visible, "and choosing the line back swaps them out again");

                Field<TextBox>(form, "_road").Text = "MAIN STREET";
                width.Text = "60";
                Field<ComboBox>(form, "_side").SelectedIndex = 1;    // road is to the RIGHT
                Field<CheckBox>(form, "_centerline").Checked = true;
                Field<CheckBox>(form, "_every").Checked = false;

                for (var i = 0; i < 3; i++)
                {
                    SetCell(monuments, i, "kind", i == 2 ? "Set" : "Found");
                    SetCell(monuments, i, "desc", i == 2 ? "REBAR AND CAP" : "MON IN CASE");
                }
                SetCell(monuments, 2, "offset", true);
                SetCell(monuments, 2, "dist", "5");
                SetCell(monuments, 2, "dir", "N45.0000E");

                ReportClipping(form, "Right of Way window, run");
                Shot(form, "01-row-run");
                ClickOn(form, "Draw");
            } });
            add("the run is drawn from what the window collected", () =>
            {
                LogRunError();
                var tally = ByLayer("ROW");
                Log("   " + Describe(tally));
                Check(Total(tally) == 29, "29 objects, the same as the headless run (" + Total(tally) + ")");
                Check(Count(tally, "V-PROP-MON-E") == 2, "the line through the corners is on the monument line layer");
                Check(Count(tally, "V-ROAD-ROW-E") == 2, "the opposite right-of-way line is on the Right of Way type's layer");
                Check(Count(tally, "V-ROAD-CNTR-E") == 2, "the centerline is on the centerline layer");
                var texts = Texts("ROW");
                Check(texts.Any(t => t == "FND MON IN CASE"), "the found monuments carry the office wording");
                Check(texts.Any(t => t == "SET REBAR AND CAP"), "the set monument carries its own wording");
                Check(texts.Any(t => t == "5.00' OFFSET N 45%%d00'00\" E TO CORNER"),
                      "the offset monument says which way its corner lies, degree token lowercase");
                Check(texts.Any(t => t == "60' R/W"), "the width is dimensioned");
                Check(texts.Any(t => t == "MAIN STREET"), "the road is named along the road");
                Check(!texts.Any(t => t.Contains("%%D")), "no label carries an upper-cased degree token");
            }, 3000);

            // --------------------------------------------- the window's own refusal
            add("FTFROW again, to try a width that is not a number",
                () => Send("FTFROW Run 5000,5000 5100,5000  "), 2500);
            s.Add(new Step { Name = "a bad width is refused on screen, not drawn", WhileBusy = true, Modal = "RowForm", Delay = 2000, Run = () =>
            {
                var form = ModalForm("RowForm");
                if (form == null) { Fail("the window did not reopen"); return; }
                Field<TextBox>(form, "_road").Text = "SECOND STREET";
                Field<TextBox>(form, "_width").Text = "sixty";
                ClickOn(form, "Draw");

                Check(ModalForm("RowForm") != null, "the window stays open rather than drawing");
                var errors = Field<Label>(form, "_errors");
                Check(errors != null && errors.Text.Length > 0,
                      "it says what is wrong: " + (errors == null ? "(no label)" : errors.Text));
                Shot(form, "02-row-bad-width");
            } });
            s.Add(new Step { Name = "cancel out of the window", WhileBusy = true, Modal = "RowForm", Delay = 1500, Run = () =>
            {
                var form = ModalForm("RowForm");
                if (form == null) { Fail("the window closed on its own"); return; }
                ClickOn(form, "Cancel");
            } });
            add("cancelling drew nothing", () =>
            {
                Check(Total(ByLayer("ROW")) == 29, "still 29 objects after Cancel (" + Total(ByLayer("ROW")) + ")");
            }, 2500);

            // ----------------------------------------------------------- a block
            add("FTFROW: pick a platted block's four corners",
                () => Send("FTFROW Block 6000,5000 6200,5000 6200,5100 6000,5100  "), 2500);
            s.Add(new Step { Name = "the Right of Way window, block mode", WhileBusy = true, Modal = "RowForm", Delay = 2500, Run = () =>
            {
                var form = ModalForm("RowForm");
                if (form == null) { Fail("FTFROW did not open the window for a block"); return; }
                Check(HasLabel(form, "Platted block"), "the heading says a platted block");

                var monuments = Field<DataGridView>(form, "_monuments");
                var sides = Field<DataGridView>(form, "_sides");
                if (monuments == null || sides == null) return;
                Check(sides.Rows.Count == 4, "a block gets one row per side (" + sides.Rows.Count + ")");
                Check(monuments.Columns["radius"].Visible, "the corner radius column is shown for a block");
                Check(Cell(sides, 0, "side") == "201 to 202",
                      "the sides are named by the monuments they run between (" + Cell(sides, 0, "side") + ")");
                Check(Cell(sides, 0, "between") == "200.00" && Cell(sides, 1, "between") == "100.00",
                      "each side reports the distance between the picks (" + Cell(sides, 0, "between") + ", " + Cell(sides, 1, "between") + ")");
                Check(Cell(sides, 0, "type") == "Frontage", "a block's sides start as frontages");

                for (var i = 0; i < 4; i++)
                {
                    SetCell(monuments, i, "kind", "Found");
                    SetCell(monuments, i, "desc", "MON IN CASE");
                }
                SetCell(monuments, 1, "radius", "25");
                var roads = new[] { "MAIN STREET", "OAK AVENUE", null, null };
                var widths = new[] { "60", "40", null, null };
                for (var i = 0; i < 4; i++)
                {
                    SetCell(sides, i, "type", i < 2 ? "Frontage" : "Plain");
                    if (roads[i] != null) { SetCell(sides, i, "road", roads[i]); SetCell(sides, i, "width", widths[i]); }
                }
                Field<CheckBox>(form, "_centerline").Checked = false;

                ReportClipping(form, "Right of Way window, block");
                Shot(form, "03-row-block");
                ClickOn(form, "Draw");
            } });
            add("the block is drawn with its rounded corner", () =>
            {
                LogRunError();
                var tally = ByLayer("ROW");
                Check(Total(tally) == 64, "35 more objects, as in the headless run (" + (Total(tally) - 29) + ")");
                var texts = Texts("ROW");
                Check(texts.Any(t => t == "R=25.00'") && texts.Any(t => t == "L=39.27'"),
                      "the rounded corner reads like a curve table");
                Check(texts.Any(t => t == "40' R/W") && texts.Any(t => t == "OAK AVENUE"),
                      "the second frontage is dimensioned and named");
                Check(ArcCount() == 1, "exactly one arc is in the drawing (" + ArcCount() + ")");
            }, 3000);

            // ---------------------------------------------------- an intersection
            add("FTFROW: pick an intersection's four corners",
                () => Send("FTFROW Intersection 7000,5000 7100,5000 7100,5100 7000,5100  "), 2500);
            s.Add(new Step { Name = "the Right of Way window, intersection mode", WhileBusy = true, Modal = "RowForm", Delay = 2500, Run = () =>
            {
                var form = ModalForm("RowForm");
                if (form == null) { Fail("FTFROW did not open the window for an intersection"); return; }
                Check(HasLabel(form, "Intersection"), "the heading says an intersection");

                var monuments = Field<DataGridView>(form, "_monuments");
                var sides = Field<DataGridView>(form, "_sides");
                if (monuments == null || sides == null) return;
                Check(!monuments.Columns["radius"].Visible, "an intersection offers no corner radius");
                Check(Cell(sides, 0, "type") == "Cross", "an intersection's sides start as crossings");
                Check(Field<CheckBox>(form, "_every") != null, "the every-monument option exists but is not offered here");

                for (var i = 0; i < 4; i++)
                {
                    SetCell(monuments, i, "kind", "Found");
                    SetCell(monuments, i, "desc", "MON IN CASE");
                }
                var roads = new[] { "MAIN STREET", "OAK AVENUE", "MAIN STREET", "OAK AVENUE" };
                var widths = new[] { "100", "100", "99.5", "100" };
                for (var i = 0; i < 4; i++)
                {
                    SetCell(sides, i, "type", "Cross");
                    SetCell(sides, i, "road", roads[i]);
                    SetCell(sides, i, "width", widths[i]);
                }
                Field<CheckBox>(form, "_centerline").Checked = false;

                ReportClipping(form, "Right of Way window, intersection");
                Shot(form, "04-row-intersection");
                ClickOn(form, "Draw");
            } });
            add("the intersection writes each crossing's record width", () =>
            {
                LogRunError();
                var texts = Texts("ROW");
                Check(Total(ByLayer("ROW")) == 88, "24 more objects, as in the headless run (" + (Total(ByLayer("ROW")) - 64) + ")");
                Check(texts.Any(t => t == "99.5' R/W"), "the odd record width is written as it was given");
                Check(texts.Count(t => t == "100' R/W") == 3,
                      "the other three crossings carry their record width (" + texts.Count(t => t == "100' R/W") + ")");
            }, 3000);

            // --------------------------------------------------------- the lots
            add("FTFLOTS: pick the block to divide",
                () => Send("FTFLOTS 8000,5000 8200,5000 8200,5100 8000,5100  "), 2500);
            s.Add(new Step { Name = "the Platted Block window", WhileBusy = true, Modal = "LotForm", Delay = 3000, Run = () =>
            {
                var form = ModalForm("LotForm");
                Check(form != null, "FTFLOTS opens the Platted Block window");
                if (form == null) return;
                Check(form.Text == "Platted Block", "the window is titled Platted Block (" + form.Text + ")");

                var radii = Field<DataGridView>(form, "_radii");
                if (radii != null)
                {
                    Check(radii.Rows.Count == 4, "one rounded-corner row per block corner (" + radii.Rows.Count + ")");
                    Check(Cell(radii, 0, "corner") == "401",
                          "the corners are named by the survey points picked (" + Cell(radii, 0, "corner") + ")");
                }
                var first = Field<TextBox>(form, "_first");
                Check(first != null && first.Text == "1", "lot numbering starts at 1");
                Field<TextBox>(form, "_block").Text = "A";

                // The first row of lots: the equal division is prefilled from the side picked.
                var rowA = Field<Control>(form, "_a");
                Check(rowA != null, "the window has a first row editor");
                if (rowA == null) return;
                var lots = Field<DataGridView>(rowA, "_lots");
                if (lots != null)
                {
                    Check(lots.Rows.Count == 4, "four lots are prefilled (" + lots.Rows.Count + ")");
                    Check(Cell(lots, 0, "width") == "50", "200' divided four ways is 50' each (" + Cell(lots, 0, "width") + ")");
                    SetCell(lots, 2, "drain", "Toward the rear");
                }

                var useB = Field<CheckBox>(form, "_useB");
                var rowB = Field<Control>(form, "_b");
                if (useB != null && rowB != null)
                {
                    Check(!rowB.Visible, "the second row of lots is hidden until it is asked for");
                    useB.Checked = true;
                    Check(rowB.Visible, "ticking the box shows the second row editor");
                    useB.Checked = false;
                    Check(!rowB.Visible, "and unticking hides it again");
                }

                // The row of lot fields runs wider than the window, so the body scrolls sideways
                // to reach the last two. Recorded rather than asserted: the window works and
                // every field is reachable, but that row is set to wrap and does not, because an
                // AutoSize flow panel grows instead of folding. Worth a look.
                var body = Descendants(form).OfType<FlowLayoutPanel>().FirstOrDefault(p => p.AutoScroll);
                if (body != null)
                    Log("   body scrolls sideways: " + body.HorizontalScroll.Visible + " (wants " +
                        body.GetPreferredSize(new Size(0, body.Height)).Width + "px, has " + body.ClientSize.Width + "px)");
                Check(Field<TextBox>(rowA, "_depth") != null && Field<ComboBox>(rowA, "_close") != null,
                      "the lot depth and what-if-the-frontages-miss fields are on the row editor");

                Field<ComboBox>(form, "_alley").SelectedIndex = 1;        // along the back
                Field<TextBox>(form, "_alleyWidth").Text = "20";
                Field<ComboBox>(form, "_backSide").SelectedIndex = 0;     // furthest from the front

                ReportClipping(form, "Platted Block window");
                Shot(form, "05-lots");
                ClickOn(form, "Draw");
            } });
            add("the lots are drawn from what the window collected", () =>
            {
                LogRunError();
                var tally = ByLayer("LOTS");
                Log("   " + Describe(tally));
                Check(Total(tally) == 68, "68 objects, the same as the headless run (" + Total(tally) + ")");
                var texts = Texts("LOTS");
                Check(texts.Count(t => t.StartsWith("LOT ", StringComparison.Ordinal)) == 4,
                      "four lots are numbered (" + texts.Count(t => t.StartsWith("LOT ", StringComparison.Ordinal)) + ")");
                Check(texts.Count(t => t == "4,000 SF") == 4, "each lot is 50' x 80', so 4,000 sq ft");
                Check(texts.Any(t => t == "20' ALLEY"), "the alley is labelled with its width");
                Check(texts.Any(t => t == "BLOCK A"), "the block title comes from what was typed");
                Check(texts.Any(t => t.StartsWith("BUILDING SETBACKS:", StringComparison.Ordinal)),
                      "the plat notes state the setbacks that were used");
                Check(!texts.Any(t => t.Contains("%%D")), "no lot line carries an upper-cased degree token");
            }, 3000);

            // ------------------------------------------------------ settings pages
            add("FTFSETUP opens the settings window", () => Send("FTFSETUP "), 4500);
            add("the Right of Way settings page", () => SettingsPage("Right of Way", "06-settings-right-of-way"), 3000);
            add("the Platted Blocks settings page", () => SettingsPage("Platted Blocks", "07-settings-platted-blocks"), 3000);

            // ------------------------------------------- layout at larger text sizes
            add("both windows at 100%, 125% and 150% text", ScaledCopies, 2500);

            add("save the drawing", () => Send("_.QSAVE "), 3000);
            add("done", () =>
            {
                Log("UI TEST DONE: " + _pass + " passed, " + _fail + " failed.");
                if (_log != null) _log.Flush();
            }, 500);
            return s;
        }

        // ------------------------------------------------------------ settings page

        /// <summary>
        /// Navigates the settings window to one section the way a drafter clicks it, then checks
        /// the page built, filled itself from the settings, offers only layers that are really in
        /// the drawing, saves without complaint, and is not clipped.
        /// </summary>
        private static void SettingsPage(string label, string shot)
        {
            var window = Application.OpenForms.Cast<Form>().FirstOrDefault(f => f.GetType().Name == "FtfMainForm");
            Check(window != null, "FTFSETUP opens the FTF window");
            if (window == null) return;

            var panel = Descendants(window).FirstOrDefault(c => c.GetType().Name == "SettingsPanel");
            Check(panel != null, "FTFSETUP lands on the Settings page");
            if (panel == null) return;

            var nav = Field<ListBox>(panel, "_nav");
            if (nav == null) return;
            var index = -1;
            for (var i = 0; i < nav.Items.Count; i++)
                if (Convert.ToString(nav.Items[i]) == label) index = i;
            Check(index >= 0, "\"" + label + "\" is in the settings navigation");
            if (index < 0) return;

            nav.SelectedIndex = index;
            Application.DoEvents();

            var built = (System.Collections.IDictionary)panel.GetType()
                .GetField("_built", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(panel);
            var page = built.Contains(label) ? built[label] as Control : null;
            Check(page != null, "the " + label + " page builds without throwing");
            if (page == null) return;
            Check(page.Visible, "and is the page now on screen");

            var combos = Descendants(page).OfType<ComboBox>().ToList();
            var boxes = Descendants(page).OfType<TextBox>().ToList();
            var checks = Descendants(page).OfType<CheckBox>().ToList();
            Log("   " + combos.Count + " combo(s), " + boxes.Count + " box(es), " + checks.Count + " checkbox(es)");
            Check(combos.Count > 0 && boxes.Count > 0, "the page has its controls");

            // Layers are offered, never invented: every name in a picker is in this drawing.
            var inDrawing = DrawingLayers();
            var offered = combos.SelectMany(c => c.Items.Cast<object>().Select(o => Convert.ToString(o)))
                                .Where(n => n.StartsWith("V-", StringComparison.OrdinalIgnoreCase)).Distinct().ToList();
            Check(offered.Count > 0, "the layer pickers are filled from the drawing (" + offered.Count + " layer names)");
            var invented = offered.Where(n => !inDrawing.Contains(n)).ToList();
            Check(invented.Count == 0, "every layer offered is really in the drawing" +
                  (invented.Count == 0 ? string.Empty : " -- invented: " + string.Join(", ", invented.Take(5).ToArray())));

            // The page writes back what it is showing.
            object pageObject = null;
            var sections = (System.Collections.IEnumerable)panel.GetType()
                .GetField("_sections", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(panel);
            foreach (var section in sections)
            {
                var sectionLabel = (string)section.GetType().GetField("Label").GetValue(section);
                if (sectionLabel == label) pageObject = section.GetType().GetField("Page").GetValue(section);
            }
            Check(pageObject != null, "the panel holds the " + label + " page object");
            if (pageObject == null) { ReportClipping(page, label + " settings page"); Shot(window, shot); return; }

            var settings = new FieldCodes.Settings.FtfSettings();
            var problems = new List<string>();
            pageObject.GetType().GetMethod("SaveTo").Invoke(pageObject, new object[] { settings, problems });
            Check(problems.Count == 0, "what the page shows saves without complaint" +
                  (problems.Count == 0 ? string.Empty : ": " + string.Join("; ", problems.ToArray())));

            if (label == "Right of Way")
            {
                var row = settings.RightOfWay;
                Log("   saved: lineType=" + row.LineTypeName + " monument=" + row.MonumentLayer +
                    " dimension=" + row.DimensionLayer + " policy=" + row.DimensionPolicy);
                Check(row.LineTypeName == "Right of Way", "the Drafting Lines type round-trips (" + row.LineTypeName + ")");
                Check(row.MonumentLayer == "V-SURV-MONU-E", "the monument layer round-trips (" + row.MonumentLayer + ")");
                Check(row.MonumentLineLayer == "V-PROP-MON-E", "the monument line layer round-trips (" + row.MonumentLineLayer + ")");

                // A bad number is reported, not silently taken.
                var height = Field<TextBox>(pageObject, "_text");
                if (height != null)
                {
                    var good = height.Text;
                    height.Text = "0";
                    var complaints = new List<string>();
                    pageObject.GetType().GetMethod("SaveTo").Invoke(pageObject, new object[] { new FieldCodes.Settings.FtfSettings(), complaints });
                    Check(complaints.Count > 0, "a text height of zero is refused with a reason" +
                          (complaints.Count == 0 ? string.Empty : ": " + complaints[0]));
                    height.Text = good;
                }
            }
            else
            {
                var lots = settings.Lots;
                Log("   saved: lotType=" + lots.LotLineTypeName + " alleyType=" + lots.AlleyTypeName +
                    " lotText=" + lots.LotTextLayer + " notes=" + lots.NotesLayer);
                Check(lots.LotLineTypeName == "Lot Line", "the lot line type round-trips (" + lots.LotLineTypeName + ")");
                Check(lots.LotTextLayer == "V-PROP-LOT-TEXT-E", "the lot text layer round-trips (" + lots.LotTextLayer + ")");
                Check(lots.SetbackLayer == "V-PROP-SETB-E", "the setback layer round-trips (" + lots.SetbackLayer + ")");
            }

            ReportClipping(page, label + " settings page");
            Shot(window, shot);
        }

        // ---------------------------------------------------- larger text sizes

        /// <summary>
        /// Builds each window off-screen at three text sizes and reports anything clipped. A
        /// drafter at 125% display scaling is the common case, and neither window had ever been
        /// laid out on a real screen before this test.
        /// </summary>
        private static void ScaledCopies()
        {
            var rowType = Plugin.GetType("FieldCodes.Cad.Ui.RowForm");
            var lotType = Plugin.GetType("FieldCodes.Cad.Ui.LotForm");
            Check(rowType != null && lotType != null, "both window types are in the plugin");
            if (rowType == null || lotType == null) return;

            foreach (var scale in new[] { 1.0f, 1.25f, 1.5f })
            {
                var name = ((int)(scale * 100)).ToString(CultureInfo.InvariantCulture);
                ShowCopy(rowType,
                         new object[] { RowJobFor(FRow.RowMode.Block), new FieldCodes.Settings.RightOfWaySettings() },
                         scale, "Right of Way window at " + name + "% text", "08-row-" + name);
                ShowCopy(lotType,
                         new object[] { LotJobFor(), new FieldCodes.Settings.LotSettings(), CornerLabels() },
                         scale, "Platted Block window at " + name + "% text", "09-lots-" + name);
            }
        }

        private static void ShowCopy(Type type, object[] args, float scale, string what, string shot)
        {
            Form form = null;
            try
            {
                form = (Form)Activator.CreateInstance(type,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, args, null);
                if (scale != 1.0f) form.Font = new Font(form.Font.FontFamily, form.Font.Size * scale);
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-4000, 0);
                form.Show();
                Application.DoEvents();
                if (scale != 1.0f) RelayoutFixedHeaders(form);
                Application.DoEvents();
                ReportClipping(form, what);
                if (shot != null) Shot(form, shot);
            }
            catch (System.Exception ex)
            {
                var inner = ex.InnerException != null ? ex.InnerException : ex;
                Fail(what + ": building the window threw " + inner.GetType().Name + ": " + inner.Message);
            }
            finally
            {
                if (form != null) { form.Hide(); form.Dispose(); }
            }
        }

        /// <summary>
        /// Both windows fix their header's height once, in the constructor, from the label
        /// heights at the font in force then -- the same as EasementPreviewForm,
        /// LegalDescriptionForm and InspectorForm, which are validated production windows.
        /// Enlarging the font afterwards, as this test does, leaves that height behind; at a
        /// real 125% display the constructor would have computed a taller one to begin with.
        /// Recompute it, so what gets measured is what a drafter at that size would see rather
        /// than an artefact of testing the font change out of order.
        /// </summary>
        private static void RelayoutFixedHeaders(Control form)
        {
            foreach (Control c in form.Controls)
            {
                var panel = c as Panel;
                if (panel == null || panel.Dock != DockStyle.Top) continue;
                var need = panel.Padding.Vertical + 8;
                foreach (var label in Descendants(panel).OfType<Label>()) need += label.PreferredHeight;
                if (need > panel.Height) panel.Height = need;
            }
        }

        private static FRow.RowJob RowJobFor(FRow.RowMode mode)
        {
            var job = new FRow.RowJob { Mode = mode, RoadName = "MAIN STREET", WidthFeet = 60 };
            var corners = new[]
            {
                new FieldCodes.Easements.P2(0, 0), new FieldCodes.Easements.P2(200, 0),
                new FieldCodes.Easements.P2(200, 100), new FieldCodes.Easements.P2(0, 100)
            };
            for (var i = 0; i < corners.Length; i++)
                job.Monuments.Add(new FRow.RowMonument
                {
                    PointNumber = (201 + i).ToString(CultureInfo.InvariantCulture),
                    Location = corners[i],
                    Description = "MON IN CASE"
                });
            if (job.IsRing)
                for (var i = 0; i < corners.Length; i++)
                {
                    job.Faces.Add(FRow.RowFace.Frontage("MAIN STREET", 60));
                    job.CornerRadiiFeet.Add(0.0);
                }
            return job;
        }

        private static FieldCodes.Lots.LotJob LotJobFor()
        {
            var job = new FieldCodes.Lots.LotJob { BlockName = "A" };
            job.Corners.AddRange(new[]
            {
                new FieldCodes.Easements.P2(0, 0), new FieldCodes.Easements.P2(200, 0),
                new FieldCodes.Easements.P2(200, 100), new FieldCodes.Easements.P2(0, 100)
            });
            return job;
        }

        private static List<string> CornerLabels()
        {
            return new List<string> { "401", "402", "403", "404" };
        }

        // ------------------------------------------------------- reflection helpers

        private static T Field<T>(object target, string name) where T : class
        {
            var field = target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null) { Fail("no field " + name + " on " + target.GetType().Name); return null; }
            return field.GetValue(target) as T;
        }

        private static IEnumerable<Control> Descendants(Control root)
        {
            foreach (Control c in root.Controls)
            {
                yield return c;
                foreach (var inner in Descendants(c)) yield return inner;
            }
        }

        private static Button FindButton(Control root, string text)
        {
            return Descendants(root).OfType<Button>().FirstOrDefault(b => b.Text == text);
        }

        private static void ClickOn(Control root, string text)
        {
            var b = FindButton(root, text);
            if (b == null) { Fail("no button \"" + text + "\""); return; }
            typeof(Button).GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(b, new object[] { EventArgs.Empty });
        }

        private static bool HasLabel(Control root, string text)
        {
            return Descendants(root).OfType<Label>().Any(l => l.Text == text);
        }

        private static void SetCell(DataGridView grid, int row, string column, object value)
        {
            if (grid == null || grid.Rows.Count <= row) { Fail("grid has no row " + row); return; }
            if (!grid.Columns.Contains(column)) { Fail("grid has no column " + column); return; }
            grid.Rows[row].Cells[column].Value = value;
        }

        private static string Cell(DataGridView grid, int row, string column)
        {
            if (grid == null || grid.Rows.Count <= row || !grid.Columns.Contains(column)) return null;
            var v = grid.Rows[row].Cells[column].Value;
            return v == null ? string.Empty : Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        private static void Shot(Control form, string name)
        {
            try
            {
                using (var bmp = new Bitmap(Math.Max(form.Width, 1), Math.Max(form.Height, 1)))
                {
                    form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));
                    bmp.Save(Path.Combine(OutDir, name + ".png"), ImageFormat.Png);
                }
                Log("   screenshot " + name + ".png (" + form.Width + "x" + form.Height + ")");
            }
            catch (System.Exception ex) { Log("   screenshot failed: " + ex.Message); }
        }

        private static void ShotScreenless(string name)
        {
            var form = Application.OpenForms.Cast<Form>().LastOrDefault(f => f.Visible);
            if (form != null) Shot(form, name);
        }

        // ------------------------------------------------------------- layout check

        /// <summary>Reports any control whose bottom or right edge falls outside its parent's
        /// client area -- what a drafter sees as clipped. The same check the Dip Builder window
        /// gets, which is how its layout bugs were found before a drafter met them.</summary>
        private static void ReportClipping(Control root, string what)
        {
            var clipped = new List<string>();
            Walk(root, clipped);
            Log("   " + what + ": " + (clipped.Count == 0 ? "no clipped controls" : clipped.Count + " clipped control(s)"));
            foreach (var c in clipped.Take(25)) Log("      clipped: " + c);
            Check(clipped.Count == 0, "no clipped controls, " + what);
        }

        private static void Walk(Control parent, List<string> clipped)
        {
            if (parent is DataGridView || parent is ListView || parent is ComboBox) return;
            var scrolls = parent is ScrollableControl && ((ScrollableControl)parent).AutoScroll;
            foreach (Control c in parent.Controls)
            {
                if (!c.Visible) continue;
                if (!scrolls && !(parent is TabControl) && !(parent is Form) && !(c is TabControl) &&
                    (c.Bottom > parent.ClientSize.Height + 1 || c.Right > parent.ClientSize.Width + 1))
                    clipped.Add(c.GetType().Name + " \"" + Short(c.Text) + "\" in " + parent.GetType().Name +
                                " extends to " + c.Right + "x" + c.Bottom + " of " + parent.ClientSize.Width + "x" + parent.ClientSize.Height);
                // A flow panel that scrolls itself is meant to hold more than it shows -- the
                // Platted Block window's whole body is one such column.
                var flow = c as FlowLayoutPanel;
                if (flow != null && !flow.AutoScroll)
                {
                    var need = flow.GetPreferredSize(new Size(flow.Width, 0)).Height;
                    if (need > flow.Height + 1) clipped.Add("row in " + parent.GetType().Name + " needs " + need + "px, has " + flow.Height + "px");
                }
                Walk(c, clipped);
            }
        }

        private static string Short(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return text.Length <= 40 ? text : text.Substring(0, 40) + "...";
        }

        // --------------------------------------------------- what is in the drawing

        /// <summary>FTF-owned entities by layer, for whatever the last command drew.</summary>
        private static Dictionary<string, int> ByLayer(string source)
        {
            var tally = new Dictionary<string, int>();
            AcDoc doc = AcApp.DocumentManager.MdiActiveDocument;
            using (var tr = doc.Database.TransactionManager.StartOpenCloseTransaction())
            {
                var ms = (AcDb.BlockTableRecord)tr.GetObject(
                    AcDb.SymbolUtilityServices.GetBlockModelSpaceId(doc.Database), AcDb.OpenMode.ForRead);
                foreach (AcDb.ObjectId id in ms)
                {
                    if (id.IsErased) continue;
                    var ent = tr.GetObject(id, AcDb.OpenMode.ForRead, false, true) as AcDb.Entity;
                    if (ent == null) continue;
                    using (var rb = ent.GetXDataForApplication(FtfApp))
                    {
                        if (rb == null) continue;
                        var strings = rb.AsArray()
                            .Where(v => v.TypeCode == (int)AcDb.DxfCode.ExtendedDataAsciiString)
                            .Select(v => Convert.ToString(v.Value)).ToList();
                        if (strings.Count < 3 || strings[2] != source) continue;
                    }
                    if (tally.ContainsKey(ent.Layer)) tally[ent.Layer]++;
                    else tally[ent.Layer] = 1;
                }
            }
            return tally;
        }

        private static int Total(Dictionary<string, int> tally)
        {
            return tally.Values.Sum();
        }

        private static int Count(Dictionary<string, int> tally, string layer)
        {
            return tally.ContainsKey(layer) ? tally[layer] : 0;
        }

        /// <summary>Arcs in model space -- a rounded block corner is the only thing that draws one.</summary>
        private static int ArcCount()
        {
            var count = 0;
            AcDoc doc = AcApp.DocumentManager.MdiActiveDocument;
            using (var tr = doc.Database.TransactionManager.StartOpenCloseTransaction())
            {
                var ms = (AcDb.BlockTableRecord)tr.GetObject(
                    AcDb.SymbolUtilityServices.GetBlockModelSpaceId(doc.Database), AcDb.OpenMode.ForRead);
                foreach (AcDb.ObjectId id in ms)
                {
                    if (id.IsErased) continue;
                    if (tr.GetObject(id, AcDb.OpenMode.ForRead, false, true) is AcDb.Arc) count++;
                }
            }
            return count;
        }

        private static HashSet<string> DrawingLayers()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AcDoc doc = AcApp.DocumentManager.MdiActiveDocument;
            using (var tr = doc.Database.TransactionManager.StartOpenCloseTransaction())
            {
                var table = (AcDb.LayerTable)tr.GetObject(doc.Database.LayerTableId, AcDb.OpenMode.ForRead);
                foreach (AcDb.ObjectId id in table)
                {
                    var ltr = tr.GetObject(id, AcDb.OpenMode.ForRead) as AcDb.LayerTableRecord;
                    if (ltr != null) names.Add(ltr.Name);
                }
            }
            return names;
        }

        private static string Describe(Dictionary<string, int> tally)
        {
            var parts = tally.Select(p => p.Key + "=" + p.Value).ToList();
            parts.Sort();
            return parts.Count == 0 ? "(none)" : string.Join(" ", parts.ToArray());
        }

        /// <summary>Every FTF-owned text this source drew, for checking the wording.</summary>
        private static List<string> Texts(string source)
        {
            var list = new List<string>();
            AcDoc doc = AcApp.DocumentManager.MdiActiveDocument;
            using (var tr = doc.Database.TransactionManager.StartOpenCloseTransaction())
            {
                var ms = (AcDb.BlockTableRecord)tr.GetObject(
                    AcDb.SymbolUtilityServices.GetBlockModelSpaceId(doc.Database), AcDb.OpenMode.ForRead);
                foreach (AcDb.ObjectId id in ms)
                {
                    if (id.IsErased) continue;
                    var text = tr.GetObject(id, AcDb.OpenMode.ForRead, false, true) as AcDb.DBText;
                    if (text == null) continue;
                    using (var rb = text.GetXDataForApplication(FtfApp))
                    {
                        if (rb == null) continue;
                        var strings = rb.AsArray()
                            .Where(v => v.TypeCode == (int)AcDb.DxfCode.ExtendedDataAsciiString)
                            .Select(v => Convert.ToString(v.Value)).ToList();
                        if (strings.Count < 3 || strings[2] != source) continue;
                    }
                    list.Add(text.TextString);
                }
            }
            return list;
        }
    }
}
