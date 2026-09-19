using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Windows;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: ExtensionApplication(typeof(FieldCodes.Cad.FtfRibbon))]

namespace FieldCodes.Cad
{
    /// <summary>
    /// The FTF ribbon tab: every command a click away, grouped the way the work
    /// happens -- Label, Finish, Review, Clean up. Icons are drawn in code (simple
    /// geometry in the FTF blue), so no image files ship with the plugin and the
    /// icons cannot go missing.
    ///
    /// The ribbon may not exist yet when the plugin loads at startup, so building
    /// is deferred until the ribbon reports ready. UNTESTED in AutoCAD.
    /// </summary>
    public sealed class FtfRibbon : IExtensionApplication
    {
        private const string TabId = "FTF_RIBBON_TAB";

        public void Initialize()
        {
            if (ComponentManager.Ribbon != null)
            {
                Build();
                return;
            }
            ComponentManager.ItemInitialized += OnItemInitialized;
        }

        public void Terminate() { }

        private static void OnItemInitialized(object sender, RibbonItemEventArgs e)
        {
            if (ComponentManager.Ribbon == null) return;
            ComponentManager.ItemInitialized -= OnItemInitialized;
            Build();
        }

        private static void Build()
        {
            try
            {
                var ribbon = ComponentManager.Ribbon;
                if (ribbon == null || ribbon.FindTab(TabId) != null) return;

                var tab = new RibbonTab { Title = "FTF", Id = TabId };

                tab.Panels.Add(Panel("Label",
                    Large("Label\nLine", "FTFLABELLINE",
                        "Click a line, slide the preview, click to place. FTFL for short.",
                        FtfIcons.LabelLine),
                    Large("Between", "FTFLABELBETWEEN",
                        "Label the surface between two edges. FTFB for short.",
                        FtfIcons.Between),
                    Large("Stairs", "FTFLABELSTAIRS",
                        "Select the stair lines; the step count places itself. FTFS for short.",
                        FtfIcons.Stairs),
                    Small("Spot Shots", "FTFSPOT", FtfIcons.Spot)));

                tab.Panels.Add(Panel("Finish",
                    Large("Process\nDrawing", "FTFRUN",
                        "Every finishing stage, in order.", FtfIcons.Run),
                    Small("Points", "FTFPOINTS", FtfIcons.Points),
                    Small("Driplines", "FTFDRIP", FtfIcons.Drip),
                    Small("Labels", "FTFLABELS", FtfIcons.Labels),
                    Small("Tags", "FTFTAGS", FtfIcons.Tags),
                    Small("Schedule", "FTFTABLE", FtfIcons.Schedule),
                    Small("Draw Order", "FTFORDER", FtfIcons.Order),
                    Small("Control", "FTFCONTROL", FtfIcons.Control),
                    Small("Legend", "FTFLEGEND", FtfIcons.Legend)));

                tab.Panels.Add(Panel("Production",
                    Large("Dip\nBuilder", "FTFDIP",
                        "Storm and sewer structures from field dips: pipes, connections, labels, QC.",
                        FtfIcons.Dips),
                    Large("Strip\nEasement", "STRIPEASEMENT",
                        "Strip easement from its angle points and width, trimmed to the lot lines you click.",
                        FtfIcons.Easement),
                    Small("Check Dips", "FTFDIPCHECK", FtfIcons.Check),
                    Small("Check Easements", "FTFEASEMENTCHECK", FtfIcons.Check),
                    Small("Portion Easement", "PORTIONEASEMENT", FtfIcons.Easement),
                    Small("Construction Area", "CONSTRUCTIONAREA", FtfIcons.Easement),
                    Small("Legal Draft", "FTFEASEMENTLEGAL", FtfIcons.Easement),
                    Small("Exclude Area", "FTFEASEMENTEXCLUDE", FtfIcons.Easement),
                    Small("Add Component", "FTFEASEMENTCOMPONENT", FtfIcons.Easement),
                    Small("Exhibit Group", "FTFEASEMENTGROUP", FtfIcons.Easement),
                    Small("Inspect", "FTFEASEMENTINSPECT", FtfIcons.Check),
                    Small("Exhibit", "FTFEXHIBIT", FtfIcons.Easement),
                    Small("Rebuild Exhibit", "FTFEXHIBITREBUILD", FtfIcons.Check),
                    Small("Profile", "FTFPROFILE", FtfIcons.Settings)));

                tab.Panels.Add(Panel("Record",
                    Large("Recorded\nSurvey", "FTFRECORD",
                        "Upload a recorded plat or Record of Survey, review the extracted calls, build the geometry.",
                        FtfIcons.Record),
                    Small("Check Record", "FTFRECORDCHECK", FtfIcons.Check),
                    Small("Record Labels", "FTFRECORDLABEL", FtfIcons.Labels),
                    Small("Record Source", "FTFRECORDSOURCE", FtfIcons.Where),
                    Small("Rebuild Record", "FTFRECORDREBUILD", FtfIcons.Check)));

                tab.Panels.Add(Panel("Review",
                    Large("FTF\nWindow", "FTF",
                        "Status, review, settings and standards in one window.",
                        FtfIcons.Window),
                    Small("Lines", "FTFLINES", FtfIcons.Lines),
                    Small("Where", "FTFWHERE", FtfIcons.Where),
                    Small("Settings", "FTFSETUP", FtfIcons.Settings)));

                tab.Panels.Add(Panel("Sheets",
                    Large("Plan\nSheets", "FTFSHEETPLAN",
                        "Best-fit sheet grid over the site, with match lines.",
                        FtfIcons.PlanSheets),
                    Small("Make Layouts", "FTFSHEETMAKE", FtfIcons.MakeLayouts),
                    Small("From Layouts", "FTFSHEETS", FtfIcons.SheetAreas),
                    Small("Key Map", "FTFKEYMAP", FtfIcons.KeyMap)));

                tab.Panels.Add(Panel("Clean up",
                    Large("Clean", "FTFCLEAN",
                        "Removes everything FTF created. Survey geometry untouched.",
                        FtfIcons.Clean)));

                ribbon.Tabs.Add(tab);
            }
            catch (System.Exception)
            {
                // A ribbon that cannot build must never take the commands down with
                // it -- everything stays reachable from the command line.
            }
        }

        // ------------------------------------------------------------- assembly

        private static RibbonPanel Panel(string title, params RibbonItem[] items)
        {
            var source = new RibbonPanelSource { Title = title };
            RibbonRowPanel smallRow = null;

            foreach (var item in items)
            {
                if (item.Size == RibbonItemSize.Large)
                {
                    source.Items.Add(item);
                    continue;
                }

                // Small buttons stack three to a column inside a row panel.
                if (smallRow == null || smallRow.Items.Count >= 5)
                {
                    smallRow = new RibbonRowPanel();
                    source.Items.Add(smallRow);
                }
                if (smallRow.Items.Count > 0) smallRow.Items.Add(new RibbonRowBreak());
                smallRow.Items.Add(item);
            }

            return new RibbonPanel { Source = source };
        }

        private static RibbonButton Large(string text, string command, string tip,
                                          Func<int, ImageSource> icon)
        {
            var button = NewButton(text, command, tip, icon);
            button.Size = RibbonItemSize.Large;
            button.Orientation = System.Windows.Controls.Orientation.Vertical;
            return button;
        }

        private static RibbonButton Small(string text, string command,
                                          Func<int, ImageSource> icon)
        {
            var button = NewButton(text, command, command, icon);
            button.Size = RibbonItemSize.Standard;
            button.Orientation = System.Windows.Controls.Orientation.Horizontal;
            return button;
        }

        private static RibbonButton NewButton(string text, string command, string tip,
                                              Func<int, ImageSource> icon)
        {
            return new RibbonButton
            {
                Text = text,
                ShowText = true,
                ShowImage = true,
                Image = icon(16),
                LargeImage = icon(32),
                ToolTip = tip,
                CommandParameter = command,
                CommandHandler = new RunCommand()
            };
        }

        /// <summary>Posts the button's command to the command line, exactly as if
        /// the user typed it -- one behaviour whether clicked or typed. The post
        /// goes through the application-context marshal because a ribbon click
        /// arrives on WPF's dispatcher, not in a command context, and a direct
        /// call from there can take the whole session down.</summary>
        private sealed class RunCommand : System.Windows.Input.ICommand
        {
            public event EventHandler CanExecuteChanged { add { } remove { } }
            public bool CanExecute(object parameter) { return true; }

            public void Execute(object parameter)
            {
                try
                {
                    // The ribbon passes the BUTTON here, not its CommandParameter --
                    // the classic AutoCAD ribbon trap. Accept either shape.
                    var command = parameter as string;
                    if (command == null)
                    {
                        var item = parameter as RibbonCommandItem;
                        if (item != null) command = item.CommandParameter as string;
                    }
                    if (string.IsNullOrEmpty(command)) return;

                    var doc = AcadApp.DocumentManager.MdiActiveDocument;
                    if (doc == null) return;
                    doc.SendStringToExecute(command + " ", true, false, true);
                }
                catch (System.Exception ex)
                {
                    // A ribbon button must never crash the session -- but a silent
                    // button is almost as bad, so say what went wrong.
                    try
                    {
                        var doc = AcadApp.DocumentManager.MdiActiveDocument;
                        if (doc != null)
                            doc.Editor.WriteMessage("\nFTF ribbon: {0}\n", ex.Message);
                    }
                    catch (System.Exception) { }
                }
            }
        }
    }

    /// <summary>
    /// The toolbar icons, drawn as simple geometry: a line with its label, two
    /// edges with the label between, a staircase. One hue (the FTF blue) with a
    /// neutral secondary, both picked to read on the light and dark ribbons.
    /// </summary>
    internal static class FtfIcons
    {
        private static readonly Brush Blue = Freeze(new SolidColorBrush(Color.FromRgb(0x3E, 0x86, 0xC6)));
        private static readonly Brush Gray = Freeze(new SolidColorBrush(Color.FromRgb(0x9A, 0xA5, 0xB1)));

        private static Brush Freeze(SolidColorBrush b) { b.Freeze(); return b; }

        private static Pen BluePen(double w) { var p = new Pen(Blue, w); p.StartLineCap = PenLineCap.Round; p.EndLineCap = PenLineCap.Round; p.Freeze(); return p; }
        private static Pen GrayPen(double w) { var p = new Pen(Gray, w); p.StartLineCap = PenLineCap.Round; p.EndLineCap = PenLineCap.Round; p.Freeze(); return p; }

        /// <summary>Renders one icon. Drawn in a 32x32 space; the 16px size just
        /// scales the same drawing down.</summary>
        private static ImageSource Render(int px, Action<DrawingContext> draw)
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                if (px != 32) dc.PushTransform(new ScaleTransform(px / 32.0, px / 32.0));
                draw(dc);
                if (px != 32) dc.Pop();
            }
            var bitmap = new RenderTargetBitmap(px, px, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }

        private static void LabelBox(DrawingContext dc, double x, double y)
        {
            dc.DrawRectangle(Blue, null, new Rect(x, y, 12, 7));
        }

        // ------------------------------------------------------------ the icons

        /// <summary>A line with its label sitting on it.</summary>
        public static ImageSource LabelLine(int px)
        {
            return Render(px, dc =>
            {
                dc.DrawLine(GrayPen(2.5), new Point(3, 27), new Point(29, 7));
                dc.PushTransform(new RotateTransform(-37.6, 16, 17));
                LabelBox(dc, 10, 13.5);
                dc.Pop();
            });
        }

        /// <summary>Two edges, label centred between them.</summary>
        public static ImageSource Between(int px)
        {
            return Render(px, dc =>
            {
                dc.DrawLine(GrayPen(2.5), new Point(4, 6), new Point(28, 6));
                dc.DrawLine(GrayPen(2.5), new Point(4, 26), new Point(28, 26));
                LabelBox(dc, 10, 12.5);
            });
        }

        /// <summary>A staircase.</summary>
        public static ImageSource Stairs(int px)
        {
            return Render(px, dc =>
            {
                var geometry = new StreamGeometry();
                using (var g = geometry.Open())
                {
                    g.BeginFigure(new Point(4, 28), false, false);
                    g.LineTo(new Point(12, 28), true, true);
                    g.LineTo(new Point(12, 20), true, true);
                    g.LineTo(new Point(20, 20), true, true);
                    g.LineTo(new Point(20, 12), true, true);
                    g.LineTo(new Point(28, 12), true, true);
                    g.LineTo(new Point(28, 4), true, true);
                }
                geometry.Freeze();
                dc.DrawGeometry(null, BluePen(2.5), geometry);
            });
        }

        /// <summary>Run: a play triangle.</summary>
        public static ImageSource Run(int px)
        {
            return Render(px, dc =>
            {
                var geometry = new StreamGeometry();
                using (var g = geometry.Open())
                {
                    g.BeginFigure(new Point(9, 5), true, true);
                    g.LineTo(new Point(27, 16), true, true);
                    g.LineTo(new Point(9, 27), true, true);
                }
                geometry.Freeze();
                dc.DrawGeometry(Blue, null, geometry);
            });
        }

        /// <summary>Survey points.</summary>
        public static ImageSource Points(int px)
        {
            return Render(px, dc =>
            {
                dc.DrawEllipse(Blue, null, new Point(9, 10), 3.2, 3.2);
                dc.DrawEllipse(Blue, null, new Point(23, 8), 3.2, 3.2);
                dc.DrawEllipse(Blue, null, new Point(16, 23), 3.2, 3.2);
            });
        }

        /// <summary>A dripline canopy: circle with the trunk dot.</summary>
        public static ImageSource Drip(int px)
        {
            return Render(px, dc =>
            {
                dc.DrawEllipse(null, GrayPen(2.2), new Point(16, 16), 11, 11);
                dc.DrawEllipse(Blue, null, new Point(16, 16), 3, 3);
            });
        }

        /// <summary>A label beside its point.</summary>
        public static ImageSource Labels(int px)
        {
            return Render(px, dc =>
            {
                dc.DrawEllipse(Gray, null, new Point(7, 24), 3, 3);
                LabelBox(dc, 13, 7);
            });
        }

        /// <summary>A tag: leadered circle.</summary>
        public static ImageSource Tags(int px)
        {
            return Render(px, dc =>
            {
                dc.DrawLine(GrayPen(2.2), new Point(6, 26), new Point(17, 15));
                dc.DrawEllipse(null, BluePen(2.4), new Point(21, 11), 7, 7);
            });
        }

        /// <summary>The schedule grid.</summary>
        public static ImageSource Schedule(int px)
        {
            return Render(px, dc =>
            {
                dc.DrawRectangle(null, GrayPen(2), new Rect(5, 6, 22, 20));
                dc.DrawLine(BluePen(2), new Point(5, 12.5), new Point(27, 12.5));
                dc.DrawLine(GrayPen(1.6), new Point(5, 19), new Point(27, 19));
                dc.DrawLine(GrayPen(1.6), new Point(14, 12.5), new Point(14, 26));
            });
        }

        /// <summary>Draw order: stacked sheets.</summary>
        public static ImageSource Order(int px)
        {
            return Render(px, dc =>
            {
                dc.DrawRectangle(null, GrayPen(2), new Rect(9, 4, 18, 13));
                dc.DrawRectangle(Blue, null, new Rect(5, 13, 18, 13));
            });
        }

        /// <summary>The FTF window.</summary>
        public static ImageSource Window(int px)
        {
            return Render(px, dc =>
            {
                dc.DrawRectangle(null, BluePen(2.4), new Rect(4, 5, 24, 22));
                dc.DrawLine(BluePen(2.4), new Point(4, 11), new Point(28, 11));
                dc.DrawLine(GrayPen(2), new Point(11, 11), new Point(11, 27));
            });
        }

        /// <summary>The linework inventory: a line, checked.</summary>
        public static ImageSource Lines(int px)
        {
            return Render(px, dc =>
            {
                dc.DrawLine(GrayPen(2.2), new Point(4, 9), new Point(28, 9));
                dc.DrawLine(GrayPen(2.2), new Point(4, 16), new Point(28, 16));
                dc.DrawLine(GrayPen(2.2), new Point(4, 23), new Point(16, 23));
                dc.DrawLine(BluePen(2.8), new Point(19, 24), new Point(23, 28));
                dc.DrawLine(BluePen(2.8), new Point(23, 28), new Point(29, 19));
            });
        }

        /// <summary>Where the configuration lives: a map pin.</summary>
        public static ImageSource Where(int px)
        {
            return Render(px, dc =>
            {
                dc.DrawEllipse(null, BluePen(2.4), new Point(16, 12), 8, 8);
                dc.DrawLine(BluePen(2.4), new Point(16, 20), new Point(16, 29));
                dc.DrawEllipse(Blue, null, new Point(16, 12), 2.4, 2.4);
            });
        }

        /// <summary>Settings: a slider row.</summary>
        public static ImageSource Settings(int px)
        {
            return Render(px, dc =>
            {
                dc.DrawLine(GrayPen(2.2), new Point(5, 10), new Point(27, 10));
                dc.DrawLine(GrayPen(2.2), new Point(5, 22), new Point(27, 22));
                dc.DrawEllipse(Blue, null, new Point(12, 10), 3.4, 3.4);
                dc.DrawEllipse(Blue, null, new Point(21, 22), 3.4, 3.4);
            });
        }

        /// <summary>Sheet planning: a 2x2 grid of sheet windows.</summary>
        public static ImageSource PlanSheets(int px)
        {
            return Render(px, dc =>
            {
                dc.DrawRectangle(null, BluePen(2.2), new Rect(4, 5, 11, 10));
                dc.DrawRectangle(null, BluePen(2.2), new Rect(17, 5, 11, 10));
                dc.DrawRectangle(null, BluePen(2.2), new Rect(4, 17, 11, 10));
                dc.DrawRectangle(null, BluePen(2.2), new Rect(17, 17, 11, 10));
            });
        }

        /// <summary>Make layouts: a sheet with a plus.</summary>
        public static ImageSource MakeLayouts(int px)
        {
            return Render(px, dc =>
            {
                dc.DrawRectangle(null, GrayPen(2.2), new Rect(4, 6, 16, 20));
                dc.DrawLine(BluePen(2.8), new Point(24, 16), new Point(24, 26));
                dc.DrawLine(BluePen(2.8), new Point(19, 21), new Point(29, 21));
            });
        }

        /// <summary>Sheet areas from layouts: two sheets with their match line.</summary>
        public static ImageSource SheetAreas(int px)
        {
            return Render(px, dc =>
            {
                dc.DrawRectangle(null, GrayPen(2.2), new Rect(4, 8, 11, 16));
                dc.DrawRectangle(null, GrayPen(2.2), new Rect(17, 8, 11, 16));
                dc.DrawLine(BluePen(2.6), new Point(16, 5), new Point(16, 27));
            });
        }

        /// <summary>A spot elevation: the X with its value beside it.</summary>
        public static ImageSource Spot(int px)
        {
            return Render(px, dc =>
            {
                dc.DrawLine(BluePen(2.6), new Point(5, 21), new Point(13, 29));
                dc.DrawLine(BluePen(2.6), new Point(5, 29), new Point(13, 21));
                dc.PushTransform(new RotateTransform(-45, 21, 12));
                dc.DrawRectangle(Gray, null, new Rect(14, 9, 14, 6));
                dc.Pop();
            });
        }

        /// <summary>Survey control: the classic triangle with its dot.</summary>
        public static ImageSource Control(int px)
        {
            return Render(px, dc =>
            {
                var triangle = new StreamGeometry();
                using (var g = triangle.Open())
                {
                    g.BeginFigure(new Point(16, 5), false, true);
                    g.LineTo(new Point(28, 26), true, true);
                    g.LineTo(new Point(4, 26), true, true);
                }
                triangle.Freeze();
                dc.DrawGeometry(null, BluePen(2.4), triangle);
                dc.DrawEllipse(Blue, null, new Point(16, 20), 2.4, 2.4);
            });
        }

        /// <summary>Legend: sample lines with their names.</summary>
        public static ImageSource Legend(int px)
        {
            return Render(px, dc =>
            {
                dc.DrawLine(BluePen(2.4), new Point(4, 8), new Point(13, 8));
                dc.DrawRectangle(Gray, null, new Rect(16, 6, 12, 4));
                dc.DrawLine(BluePen(2.4), new Point(4, 16), new Point(13, 16));
                dc.DrawRectangle(Gray, null, new Rect(16, 14, 12, 4));
                dc.DrawLine(BluePen(2.4), new Point(4, 24), new Point(13, 24));
                dc.DrawRectangle(Gray, null, new Rect(16, 22, 12, 4));
            });
        }

        /// <summary>Dip builder: a manhole with a pipe leaving it and a dip tape.</summary>
        public static ImageSource Dips(int px)
        {
            return Render(px, dc =>
            {
                dc.DrawEllipse(null, BluePen(2.6), new Point(11, 16), 7, 7);
                dc.DrawLine(GrayPen(3.2), new Point(18, 14), new Point(29, 14));
                dc.DrawLine(GrayPen(3.2), new Point(18, 20), new Point(29, 20));
                dc.DrawLine(BluePen(1.8), new Point(11, 3), new Point(11, 16));
                dc.DrawRectangle(Blue, null, new Rect(9, 15, 4, 4));
            });
        }

        /// <summary>Strip easement: a hatched band between two parallel curves.</summary>
        /// <summary>A recorded sheet with a closed traverse drawn on it.</summary>
        public static ImageSource Record(int px)
        {
            return Render(px, dc =>
            {
                dc.DrawRectangle(null, GrayPen(2), new Rect(5, 3, 22, 26));
                var figure = new StreamGeometry();
                using (var g = figure.Open())
                {
                    g.BeginFigure(new Point(9, 9), false, true);
                    g.LineTo(new Point(22, 8), true, false);
                    g.LineTo(new Point(23, 20), true, false);
                    g.LineTo(new Point(12, 24), true, false);
                }
                figure.Freeze();
                dc.DrawGeometry(null, BluePen(2.5), figure);
                foreach (var p in new[] { new Point(9, 9), new Point(22, 8), new Point(23, 20), new Point(12, 24) })
                    dc.DrawEllipse(Blue, null, p, 2, 2);
            });
        }

        public static ImageSource Easement(int px)
        {
            return Render(px, dc =>
            {
                var band = new StreamGeometry();
                using (var g = band.Open())
                {
                    g.BeginFigure(new Point(3, 12), true, true);
                    g.QuadraticBezierTo(new Point(16, 2), new Point(29, 12), true, true);
                    g.LineTo(new Point(29, 21), true, true);
                    g.QuadraticBezierTo(new Point(16, 11), new Point(3, 21), true, true);
                }
                band.Freeze();
                dc.DrawGeometry(Gray, BluePen(2.2), band);
                dc.DrawLine(BluePen(1.4), new Point(9, 26), new Point(23, 26));
            });
        }

        /// <summary>Check: a tick over a changed point.</summary>
        public static ImageSource Check(int px)
        {
            return Render(px, dc =>
            {
                dc.DrawEllipse(Gray, null, new Point(9, 23), 4, 4);
                dc.DrawLine(BluePen(3), new Point(12, 14), new Point(18, 21));
                dc.DrawLine(BluePen(3), new Point(18, 21), new Point(29, 6));
            });
        }

        /// <summary>Key map: the sheet grid with the current cell filled.</summary>
        public static ImageSource KeyMap(int px)
        {
            return Render(px, dc =>
            {
                dc.DrawRectangle(null, GrayPen(2), new Rect(4, 6, 24, 20));
                dc.DrawLine(GrayPen(1.8), new Point(16, 6), new Point(16, 26));
                dc.DrawLine(GrayPen(1.8), new Point(4, 16), new Point(28, 16));
                dc.DrawRectangle(Blue, null, new Rect(17.2, 7.2, 9.6, 7.6));
            });
        }

        /// <summary>Clean: an eraser mid-swipe.</summary>
        public static ImageSource Clean(int px)
        {
            return Render(px, dc =>
            {
                dc.PushTransform(new RotateTransform(-35, 16, 14));
                dc.DrawRectangle(Blue, null, new Rect(9, 10, 14, 9));
                dc.Pop();
                dc.DrawLine(GrayPen(2), new Point(6, 27), new Point(14, 27));
                dc.DrawLine(GrayPen(2), new Point(17, 27), new Point(21, 27));
            });
        }
    }
}
