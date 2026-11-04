using System;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Windows;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
// Aliases, not a using of the namespace: an alias always wins over a same-named type in
// Autodesk.Windows, so these names can never become ambiguous against the real ribbon API.
using ButtonLayout = FieldCodes.Commands.RibbonButtonLayout;
using ButtonSize = FieldCodes.Commands.RibbonButtonSize;
using PanelLayout = FieldCodes.Commands.RibbonPanelLayout;
using RibbonLayout = FieldCodes.Commands.RibbonLayout;
using TabLayout = FieldCodes.Commands.RibbonTabLayout;

[assembly: ExtensionApplication(typeof(FieldCodes.Cad.FtfRibbon))]

namespace FieldCodes.Cad
{
    /// <summary>
    /// FTF's ribbon: four tabs, one per kind of work -- FieldToFinish (the drawing being
    /// finished), Survey Drafting, Boundary and Easements. What is on them lives in
    /// <see cref="RibbonLayout"/> in the FieldCodes library, where it is unit tested; this class
    /// only turns that layout into the real ribbon. Icons are drawn in code (simple geometry in
    /// the FTF blue), so no image files ship with the plugin and the icons cannot go missing.
    ///
    /// The ribbon may not exist yet when the plugin loads at startup, so building is deferred
    /// until the ribbon reports ready. Each build first takes off any tab with one of FTF's ids,
    /// so a reload replaces the tabs instead of adding a second set. UNTESTED in AutoCAD.
    /// </summary>
    public sealed class FtfRibbon : IExtensionApplication
    {
        private static readonly RunCommand Runner = new RunCommand();

        public void Initialize()
        {
            // Quiet, on its own thread, and never a reason for loading to fail: is this machine's
            // FTF the same as the office copy? An update installs itself when Civil 3D closes.
            OfficeUpdate.Start();

            // One line on the command line once Civil 3D settles: which build this is, and a
            // warning when it is a development build, not the installed one, or out of date.
            FtfBuildInfo.AnnounceWhenReady();

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

        /// <summary>
        /// Puts the layout's tabs on the ribbon, each replacing any tab already there with its id.
        /// A tab is built in full before the old one comes off, so a tab that cannot build leaves
        /// the old one standing rather than nothing. Tabs an older build used and this one does
        /// not are taken off too.
        /// </summary>
        private static void Build()
        {
            RibbonControl ribbon;
            try { ribbon = ComponentManager.Ribbon; }
            catch (System.Exception) { return; }
            if (ribbon == null) return;

            foreach (var layout in RibbonLayout.Tabs)
            {
                try
                {
                    var tab = Tab(layout);
                    RemoveTab(ribbon, layout.Id);
                    ribbon.Tabs.Add(tab);
                }
                catch (System.Exception)
                {
                    // A ribbon that cannot build must never take the commands down with
                    // it -- everything stays reachable from the command line.
                }
            }

            foreach (var id in RibbonLayout.RetiredTabIds)
            {
                try { RemoveTab(ribbon, id); }
                catch (System.Exception) { }
            }
        }

        /// <summary>Takes every tab with this id off the ribbon (a reload can have left more than one).</summary>
        private static void RemoveTab(RibbonControl ribbon, string id)
        {
            for (var guard = 0; guard < 8; guard++)
            {
                var old = ribbon.FindTab(id);
                if (old == null) return;
                ribbon.Tabs.Remove(old);
                if (ribbon.FindTab(id) == old) return;   // would not come off; never loop on it
            }
        }

        // ------------------------------------------------------------- assembly

        private static RibbonTab Tab(TabLayout layout)
        {
            var tab = new RibbonTab { Title = layout.Title, Id = layout.Id };
            foreach (var panel in layout.Panels) tab.Panels.Add(Panel(panel));
            if (layout.Id == RibbonLayout.FieldToFinishTabId) AddUserPanel(tab);
            return tab;
        }

        // ------------------------------------------------------------ my commands

        /// <summary>
        /// The person's own My Commands panel (Settings > Commands), last on the FieldToFinish tab.
        /// Per user (ui-state.json), never part of <see cref="RibbonLayout"/>: the office ribbon
        /// stays the same for everyone. A command no longer registered is left off. Nothing here
        /// can stop the standard tab from building.
        /// </summary>
        private static void AddUserPanel(RibbonTab tab)
        {
            try
            {
                var chosen = Ui.UiState.Load().QuickCommands;
                if (chosen == null || chosen.Count == 0) return;

                var registered = FtfCommandCatalog.Names();
                var commands = new System.Collections.Generic.List<string>();
                foreach (var command in chosen)
                    if (command != null && registered.Contains(command.Trim())) commands.Add(command.Trim());

                var layout = FieldCodes.Commands.UserRibbonPanel.Build(commands, FtfCommandCatalog.Descriptions());
                if (layout != null) tab.Panels.Add(Panel(layout));
            }
            catch (System.Exception) { }
        }

        /// <summary>
        /// Rebuilds the FieldToFinish tab in place -- the same replace-by-id a reload does, put back
        /// where the old tab stood -- so a change to My Commands shows without restarting. False
        /// when there is no ribbon to rebuild (the panel then appears the next time it is built).
        /// </summary>
        internal static bool RebuildFieldToFinishTab()
        {
            RibbonControl ribbon;
            try { ribbon = ComponentManager.Ribbon; }
            catch (System.Exception) { return false; }
            if (ribbon == null) return false;

            TabLayout layout = null;
            foreach (var candidate in RibbonLayout.Tabs)
                if (candidate.Id == RibbonLayout.FieldToFinishTabId) layout = candidate;
            if (layout == null) return false;

            try
            {
                var tab = Tab(layout);
                var old = ribbon.FindTab(layout.Id);
                var index = old != null ? ribbon.Tabs.IndexOf(old) : -1;
                RemoveTab(ribbon, layout.Id);
                if (index >= 0 && index <= ribbon.Tabs.Count) ribbon.Tabs.Insert(index, tab);
                else ribbon.Tabs.Add(tab);
                return true;
            }
            catch (System.Exception) { return false; }
        }

        private static RibbonPanel Panel(PanelLayout layout)
        {
            var source = new RibbonPanelSource { Title = layout.Title };
            RibbonRowPanel column = null;

            foreach (var spec in layout.Buttons)
            {
                var button = Button(spec);
                if (spec.Size == ButtonSize.Large)
                {
                    source.Items.Add(button);
                    column = null;
                    continue;
                }

                // Small buttons stack three to a column inside a row panel.
                if (column == null || column.Items.Count >= 5)
                {
                    column = new RibbonRowPanel();
                    source.Items.Add(column);
                }
                if (column.Items.Count > 0) column.Items.Add(new RibbonRowBreak());
                column.Items.Add(button);
            }

            return new RibbonPanel { Source = source };
        }

        /// <summary>
        /// One button. The tooltip is the ribbon's own: the button's name, what it does, and the
        /// command it types underneath, so the command can be learnt from the button. An icon that
        /// cannot be drawn leaves a text-only button, never a missing one.
        /// </summary>
        private static RibbonButton Button(ButtonLayout spec)
        {
            var large = spec.Size == ButtonSize.Large;
            var button = new RibbonButton
            {
                Text = spec.Label,
                ShowText = true,
                ShowImage = false,
                Size = large ? RibbonItemSize.Large : RibbonItemSize.Standard,
                Orientation = large ? System.Windows.Controls.Orientation.Vertical
                                    : System.Windows.Controls.Orientation.Horizontal,
                ToolTip = new RibbonToolTip { Title = spec.Title, Content = spec.Tip, Command = spec.Command },
                CommandParameter = spec.Command,
                CommandHandler = Runner
            };

            var icon = FtfIcons.Find(spec.Icon);
            if (icon != null)
            {
                try
                {
                    button.Image = icon(16);
                    button.LargeImage = icon(32);
                    button.ShowImage = true;
                }
                catch (System.Exception) { }
            }
            return button;
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
    /// The ribbon icons, drawn as simple geometry: a line with its label, two edges with the
    /// label between, a staircase. One hue (the FTF blue) for the subject and a neutral gray for
    /// its context, both picked to read on the light and dark ribbons. Every icon is drawn in the
    /// same 32x32 space with the same four stroke weights; the 16 px size scales that drawing.
    ///
    /// A family of commands shares a glyph and tells its members apart with a badge in the lower
    /// right corner, cut clear of the glyph: a tick checks, a circling arrow rebuilds, a turning
    /// arrow puts back, a magnifier inspects, plus adds, minus takes away, a play mark runs, an
    /// eraser cleans, an outgoing arrow exports. So Record, Rebuild Record and Check vs Record are
    /// related at a glance and still never the same picture.
    ///
    /// The ribbon finds an icon by the method's name (RibbonLayout's icon key), so every icon is a
    /// public static method taking the size in pixels; RibbonLayoutTests reads them from this file.
    /// </summary>
    internal static class FtfIcons
    {
        private static readonly Brush Blue = Freeze(new SolidColorBrush(Color.FromRgb(0x3E, 0x86, 0xC6)));
        private static readonly Brush Gray = Freeze(new SolidColorBrush(Color.FromRgb(0x9A, 0xA5, 0xB1)));

        // The four stroke weights, in the 32 px drawing space (half that on a 16 px icon).
        private const double Fine = 1.6;      // hatching and small inner detail
        private const double Light = 2.0;     // context: sheets, pages, outlines
        private const double Regular = 2.4;   // the subject's own lines
        private const double Heavy = 2.8;     // ticks, arrows and marks that must read at 16 px

        private static Brush Freeze(SolidColorBrush b) { b.Freeze(); return b; }

        private static Pen BluePen(double w) { return Stroke(Blue, w); }
        private static Pen GrayPen(double w) { return Stroke(Gray, w); }

        private static Pen Stroke(Brush brush, double w)
        {
            var p = new Pen(brush, w)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round
            };
            p.Freeze();
            return p;
        }

        /// <summary>A centerline: gray, dashed.</summary>
        private static Pen DashedGray(double w)
        {
            var p = new Pen(Gray, w) { DashStyle = new DashStyle(new[] { 2.0, 1.4 }, 0) };
            p.Freeze();
            return p;
        }

        private static Point P(double x, double y) { return new Point(x, y); }

        /// <summary>The icon a ribbon button names: the public method of that name, or null.</summary>
        internal static Func<int, ImageSource> Find(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var method = typeof(FtfIcons).GetMethod(name, BindingFlags.Public | BindingFlags.Static,
                                                     null, new[] { typeof(int) }, null);
            if (method == null || method.ReturnType != typeof(ImageSource)) return null;
            return (Func<int, ImageSource>)Delegate.CreateDelegate(typeof(Func<int, ImageSource>), method);
        }

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

        /// <summary>A polyline, or a closed polygon, through the points.</summary>
        private static void Path(DrawingContext dc, Brush fill, Pen pen, bool closed, params Point[] points)
        {
            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                g.BeginFigure(points[0], fill != null, closed);
                for (var i = 1; i < points.Length; i++) g.LineTo(points[i], true, true);
            }
            geometry.Freeze();
            dc.DrawGeometry(fill, pen, geometry);
        }

        private static void LabelBox(DrawingContext dc, double x, double y)
        {
            dc.DrawRectangle(Blue, null, new Rect(x, y, 12, 7));
        }

        /// <summary>A page with its corner folded: settings files and written documents.</summary>
        private static void Page(DrawingContext dc)
        {
            Path(dc, null, GrayPen(Light), true, P(7, 3), P(20, 3), P(26, 9), P(26, 29), P(7, 29));
            Path(dc, null, GrayPen(Light), false, P(20, 3), P(20, 9), P(26, 9));
        }

        // ---------------------------------------------------------------- badges

        private static readonly Point BadgeCentre = new Point(24, 24);
        private const double BadgeClear = 9;   // the glyph is cut back this far round the badge

        /// <summary>A family glyph with an action badge in its lower right corner.</summary>
        private static ImageSource Badged(int px, Action<DrawingContext> glyph, Action<DrawingContext> badge)
        {
            return Render(px, dc =>
            {
                var clear = new CombinedGeometry(GeometryCombineMode.Exclude,
                    new RectangleGeometry(new Rect(0, 0, 32, 32)),
                    new EllipseGeometry(BadgeCentre, BadgeClear, BadgeClear));
                clear.Freeze();
                dc.PushClip(clear);
                glyph(dc);
                dc.Pop();
                badge(dc);
            });
        }

        /// <summary>Checks: a tick.</summary>
        private static void TickBadge(DrawingContext dc)
        {
            Path(dc, null, BluePen(Heavy), false, P(18.5, 24), P(22.5, 28), P(29.5, 19.5));
        }

        /// <summary>Rebuilds: three quarters of a circle, clockwise, with its arrowhead.</summary>
        private static void RebuildBadge(DrawingContext dc)
        {
            var arc = new StreamGeometry();
            using (var g = arc.Open())
            {
                g.BeginFigure(P(28.76, 21.25), false, false);
                g.ArcTo(P(21.25, 19.24), new System.Windows.Size(5.5, 5.5), 0, true, SweepDirection.Clockwise, true, true);
            }
            arc.Freeze();
            dc.DrawGeometry(null, BluePen(Regular), arc);
            Path(dc, Blue, null, true, P(23.5, 17.94), P(22.2, 21.69), P(19.6, 17.19));
        }

        /// <summary>Puts back: an arrow that turns back on itself.</summary>
        private static void UndoBadge(DrawingContext dc)
        {
            var hook = new StreamGeometry();
            using (var g = hook.Open())
            {
                g.BeginFigure(P(29, 29), false, false);
                g.LineTo(P(29, 25), true, true);
                g.ArcTo(P(23.5, 19.5), new System.Windows.Size(5.5, 5.5), 0, false, SweepDirection.Counterclockwise, true, true);
                g.LineTo(P(21, 19.5), true, true);
            }
            hook.Freeze();
            dc.DrawGeometry(null, BluePen(Regular), hook);
            Path(dc, Blue, null, true, P(17, 19.5), P(21.5, 16), P(21.5, 23));
        }

        /// <summary>Inspects: a magnifier.</summary>
        private static void InspectBadge(DrawingContext dc)
        {
            dc.DrawEllipse(null, BluePen(Regular), P(22.5, 22.5), 4.2, 4.2);
            dc.DrawLine(BluePen(Heavy), P(25.7, 25.7), P(29.5, 29.5));
        }

        /// <summary>Adds: a plus.</summary>
        private static void PlusBadge(DrawingContext dc)
        {
            dc.DrawLine(BluePen(Heavy), P(24, 18), P(24, 30));
            dc.DrawLine(BluePen(Heavy), P(18, 24), P(30, 24));
        }

        /// <summary>Takes away: a minus.</summary>
        private static void MinusBadge(DrawingContext dc)
        {
            dc.DrawLine(BluePen(Heavy), P(18, 24), P(30, 24));
        }

        /// <summary>Runs: a play mark.</summary>
        private static void PlayBadge(DrawingContext dc)
        {
            Path(dc, Blue, null, true, P(19.5, 18), P(30, 24), P(19.5, 30));
        }

        /// <summary>Cleans: an eraser over its swipe.</summary>
        private static void EraseBadge(DrawingContext dc)
        {
            dc.PushTransform(new RotateTransform(-35, 24, 23));
            dc.DrawRectangle(Blue, null, new Rect(19, 20, 10, 6));
            dc.Pop();
            dc.DrawLine(GrayPen(Light), P(19, 30.5), P(27, 30.5));
        }

        /// <summary>Exports: an arrow leaving the corner.</summary>
        private static void ExportBadge(DrawingContext dc)
        {
            dc.DrawLine(BluePen(Heavy), P(19, 29), P(29, 19));
            Path(dc, null, BluePen(Heavy), false, P(22.5, 19), P(29, 19), P(29, 25.5));
        }

        // ================================================= FieldToFinish tab

        /// <summary>Process Drawing: a play triangle -- run every stage.</summary>
        public static ImageSource Run(int px)
        {
            return Render(px, dc => Path(dc, Blue, null, true, P(9, 5), P(27, 16), P(9, 27)));
        }

        /// <summary>The FTF window.</summary>
        public static ImageSource Window(int px)
        {
            return Render(px, dc =>
            {
                dc.DrawRectangle(null, BluePen(Regular), new Rect(4, 5, 24, 22));
                dc.DrawLine(BluePen(Regular), P(4, 11), P(28, 11));
                dc.DrawLine(GrayPen(Light), P(11, 11), P(11, 27));
            });
        }

        /// <summary>Draw order: stacked sheets.</summary>
        public static ImageSource Order(int px)
        {
            return Render(px, dc =>
            {
                dc.DrawRectangle(null, GrayPen(Light), new Rect(9, 4, 18, 13));
                dc.DrawRectangle(Blue, null, new Rect(5, 13, 18, 13));
            });
        }

        /// <summary>Clean up: an eraser mid-swipe.</summary>
        public static ImageSource Clean(int px)
        {
            return Render(px, dc =>
            {
                dc.PushTransform(new RotateTransform(-35, 16, 14));
                dc.DrawRectangle(Blue, null, new Rect(9, 10, 14, 9));
                dc.Pop();
                dc.DrawLine(GrayPen(Light), P(6, 27), P(14, 27));
                dc.DrawLine(GrayPen(Light), P(17, 27), P(21, 27));
            });
        }

        /// <summary>Survey points.</summary>
        public static ImageSource Points(int px)
        {
            return Render(px, dc =>
            {
                dc.DrawEllipse(Blue, null, P(9, 10), 3.2, 3.2);
                dc.DrawEllipse(Blue, null, P(23, 8), 3.2, 3.2);
                dc.DrawEllipse(Blue, null, P(16, 23), 3.2, 3.2);
            });
        }

        /// <summary>A symbol turned to face its way: a point with a direction arrow.</summary>
        public static ImageSource Orient(int px) { return Render(px, OrientGlyph); }

        /// <summary>Reset Orient: the orient arrow, put back.</summary>
        public static ImageSource OrientReset(int px) { return Badged(px, OrientGlyph, UndoBadge); }

        private static void OrientGlyph(DrawingContext dc)
        {
            dc.DrawEllipse(Gray, null, P(10, 22), 3.4, 3.4);
            dc.DrawLine(BluePen(Regular), P(10, 22), P(25, 7));
            dc.DrawLine(BluePen(Regular), P(25, 7), P(16, 8));
            dc.DrawLine(BluePen(Regular), P(25, 7), P(24, 16));
        }

        /// <summary>Survey control: the classic triangle with its dot.</summary>
        public static ImageSource Control(int px)
        {
            return Render(px, dc =>
            {
                Path(dc, null, BluePen(Regular), true, P(16, 5), P(28, 26), P(4, 26));
                dc.DrawEllipse(Blue, null, P(16, 20), 2.4, 2.4);
            });
        }

        /// <summary>Legend: sample lines with their names.</summary>
        public static ImageSource Legend(int px)
        {
            return Render(px, dc =>
            {
                for (var y = 8; y <= 24; y += 8)
                {
                    dc.DrawLine(BluePen(Regular), P(4, y), P(13, y));
                    dc.DrawRectangle(Gray, null, new Rect(16, y - 2, 12, 4));
                }
            });
        }

        /// <summary>Label Everything: a line with its label above, and a point with its label beside it.</summary>
        public static ImageSource LabelAll(int px)
        {
            return Render(px, dc =>
            {
                dc.DrawLine(GrayPen(Regular), P(3, 12.5), P(29, 12.5));
                LabelBox(dc, 10, 3.5);
                dc.DrawEllipse(Gray, null, P(7, 24), 3, 3);
                LabelBox(dc, 13, 20.5);
            });
        }

        /// <summary>Point labels: a label beside its point.</summary>
        public static ImageSource Labels(int px)
        {
            return Render(px, dc =>
            {
                dc.DrawEllipse(Gray, null, P(7, 24), 3, 3);
                LabelBox(dc, 13, 7);
            });
        }

        /// <summary>Check Standards: a checklist with a tick.</summary>
        public static ImageSource CheckStandards(int px)
        {
            return Render(px, dc =>
            {
                for (var y = 7; y <= 23; y += 8) dc.DrawRectangle(Gray, null, new Rect(14, y - 1.5, 14, 3));
                Path(dc, null, BluePen(Regular), false, P(3, 15), P(6.5, 19), P(12, 9));
            });
        }

        /// <summary>Update Layers: stacked layer sheets with an arrow onto the top one.</summary>
        public static ImageSource LayerUpdate(int px)
        {
            return Render(px, dc =>
            {
                Path(dc, null, GrayPen(Light), true, P(4, 22), P(16, 16), P(28, 22), P(16, 28));
                Path(dc, Gray, null, true, P(4, 16), P(16, 10), P(28, 16), P(16, 22));
                dc.DrawLine(BluePen(Regular), P(16, 2), P(16, 9));
                Path(dc, Blue, null, true, P(12.5, 6.5), P(19.5, 6.5), P(16, 11));
            });
        }

        /// <summary>Update annotation styles: a label with a curved arrow going round it.</summary>
        public static ImageSource Restyle(int px)
        {
            return Render(px, dc =>
            {
                dc.DrawEllipse(Gray, null, P(6, 25), 2.6, 2.6);
                LabelBox(dc, 10, 13);
                dc.DrawLine(BluePen(Regular), P(4, 9), P(12, 4));
                dc.DrawLine(BluePen(Regular), P(12, 4), P(22, 4));
                Path(dc, Blue, null, true, P(22, 1), P(27, 4), P(22, 7));
            });
        }

        /// <summary>A dripline canopy: circle with the trunk dot.</summary>
        public static ImageSource Drip(int px)
        {
            return Render(px, dc =>
            {
                dc.DrawEllipse(null, GrayPen(Light), P(16, 16), 11, 11);
                dc.DrawEllipse(Blue, null, P(16, 16), 3, 3);
            });
        }

        /// <summary>A tree tag: a leadered circle.</summary>
        public static ImageSource Tags(int px)
        {
            return Render(px, dc =>
            {
                dc.DrawLine(GrayPen(Light), P(6, 26), P(17, 15));
                dc.DrawEllipse(null, BluePen(Regular), P(21, 11), 7, 7);
            });
        }

        /// <summary>The schedule grid.</summary>
        public static ImageSource Schedule(int px)
        {
            return Render(px, dc =>
            {
                dc.DrawRectangle(null, GrayPen(Light), new Rect(5, 6, 22, 20));
                dc.DrawLine(BluePen(Light), P(5, 12.5), P(27, 12.5));
                dc.DrawLine(GrayPen(Fine), P(5, 19), P(27, 19));
                dc.DrawLine(GrayPen(Fine), P(14, 12.5), P(14, 26));
            });
        }

        /// <summary>Dip builder: a manhole with a pipe leaving it and a dip tape.</summary>
        public static ImageSource Dips(int px) { return Render(px, DipsGlyph); }

        /// <summary>Check Dips: the manhole, checked.</summary>
        public static ImageSource DipCheck(int px) { return Badged(px, DipsGlyph, TickBadge); }

        /// <summary>Dip field notes PDF: a field-book page with a manhole sketched on it, going out.</summary>
        public static ImageSource DipNotesPdf(int px) { return Badged(px, DipNotesGlyph, ExportBadge); }

        private static void DipNotesGlyph(DrawingContext dc)
        {
            Page(dc);
            dc.DrawEllipse(null, BluePen(Regular), P(13, 15), 3.5, 3.5);
            dc.DrawLine(BluePen(Fine), P(16.5, 15), P(22, 15));
            dc.DrawLine(GrayPen(Fine), P(9.5, 22), P(21, 22));
            dc.DrawLine(GrayPen(Fine), P(9.5, 25.5), P(17, 25.5));
        }

        private static void DipsGlyph(DrawingContext dc)
        {
            dc.DrawEllipse(null, BluePen(Regular), P(11, 16), 7, 7);
            dc.DrawLine(GrayPen(Heavy), P(18, 14), P(29, 14));
            dc.DrawLine(GrayPen(Heavy), P(18, 20), P(29, 20));
            dc.DrawLine(BluePen(Fine), P(11, 3), P(11, 16));
            dc.DrawRectangle(Blue, null, new Rect(9, 15, 4, 4));
        }

        /// <summary>Settings: a slider row.</summary>
        public static ImageSource Settings(int px)
        {
            return Render(px, dc =>
            {
                dc.DrawLine(GrayPen(Light), P(5, 10), P(27, 10));
                dc.DrawLine(GrayPen(Light), P(5, 22), P(27, 22));
                dc.DrawEllipse(Blue, null, P(12, 10), 3.4, 3.4);
                dc.DrawEllipse(Blue, null, P(21, 22), 3.4, 3.4);
            });
        }

        /// <summary>A drafting profile: a settings file -- a page with two sliders on it.</summary>
        public static ImageSource Profile(int px)
        {
            return Render(px, dc =>
            {
                Page(dc);
                dc.DrawLine(GrayPen(Fine), P(10.5, 15), P(22.5, 15));
                dc.DrawLine(GrayPen(Fine), P(10.5, 22.5), P(22.5, 22.5));
                dc.DrawEllipse(Blue, null, P(14, 15), 2.8, 2.8);
                dc.DrawEllipse(Blue, null, P(19.5, 22.5), 2.8, 2.8);
            });
        }

        /// <summary>Which build this is: an information mark.</summary>
        public static ImageSource Version(int px)
        {
            return Render(px, dc =>
            {
                dc.DrawEllipse(null, BluePen(Regular), P(16, 16), 12, 12);
                dc.DrawEllipse(Blue, null, P(16, 10), 2, 2);
                dc.DrawLine(BluePen(Heavy), P(16, 15), P(16, 23));
            });
        }

        /// <summary>The office copy: an arrow coming down into the tray.</summary>
        public static ImageSource Update(int px)
        {
            return Render(px, dc =>
            {
                dc.DrawLine(BluePen(Heavy), P(16, 3), P(16, 19));
                Path(dc, null, BluePen(Heavy), false, P(10, 13), P(16, 19), P(22, 13));
                Path(dc, null, GrayPen(Light), false, P(5, 18), P(5, 27), P(27, 27), P(27, 18));
            });
        }

        /// <summary>The linework inventory: lines, checked.</summary>
        public static ImageSource Lines(int px)
        {
            return Render(px, dc =>
            {
                dc.DrawLine(GrayPen(Light), P(4, 9), P(28, 9));
                dc.DrawLine(GrayPen(Light), P(4, 16), P(28, 16));
                dc.DrawLine(GrayPen(Light), P(4, 23), P(16, 23));
                Path(dc, null, BluePen(Heavy), false, P(19, 24), P(23, 28), P(29, 19));
            });
        }

        /// <summary>Block definitions: a block, drawn as a cube.</summary>
        public static ImageSource Blocks(int px)
        {
            return Render(px, dc =>
            {
                Path(dc, null, BluePen(Regular), true, P(16, 3.5), P(27, 9.5), P(27, 22.5), P(16, 28.5), P(5, 22.5), P(5, 9.5));
                Path(dc, null, GrayPen(Light), false, P(5, 9.5), P(16, 15.5), P(27, 9.5));
                dc.DrawLine(GrayPen(Light), P(16, 15.5), P(16, 28.5));
            });
        }

        /// <summary>Where the rules file lives: a map pin.</summary>
        public static ImageSource Where(int px)
        {
            return Render(px, dc =>
            {
                dc.DrawEllipse(null, BluePen(Regular), P(16, 12), 8, 8);
                dc.DrawLine(BluePen(Regular), P(16, 20), P(16, 29));
                dc.DrawEllipse(Blue, null, P(16, 12), 2.4, 2.4);
            });
        }

        // =============================================== Survey Drafting tab

        /// <summary>A spot elevation: the X with its value beside it.</summary>
        public static ImageSource Spot(int px)
        {
            return Render(px, dc =>
            {
                dc.DrawLine(BluePen(Regular), P(5, 21), P(13, 29));
                dc.DrawLine(BluePen(Regular), P(5, 29), P(13, 21));
                dc.PushTransform(new RotateTransform(-45, 21, 12));
                dc.DrawRectangle(Gray, null, new Rect(14, 9, 14, 6));
                dc.Pop();
            });
        }

        /// <summary>Contours: two nested level lines over a hill, one picked out.</summary>
        public static ImageSource Contour(int px)
        {
            return Render(px, dc =>
            {
                dc.DrawEllipse(null, GrayPen(Light), P(16, 17), 13, 10);
                dc.DrawEllipse(null, BluePen(Regular), P(16, 17), 8, 5.5);
                dc.DrawEllipse(Gray, null, P(16, 17), 2, 2);
            });
        }

        /// <summary>A line with its label sitting on it.</summary>
        public static ImageSource LabelLine(int px)
        {
            return Render(px, dc =>
            {
                dc.DrawLine(GrayPen(Regular), P(3, 27), P(29, 7));
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
                dc.DrawLine(GrayPen(Regular), P(4, 6), P(28, 6));
                dc.DrawLine(GrayPen(Regular), P(4, 26), P(28, 26));
                LabelBox(dc, 10, 12.5);
            });
        }

        /// <summary>A driveway: the street's edge, the driveway's two edges flaring into it, and
        /// its label running up between them.</summary>
        public static ImageSource Driveway(int px)
        {
            return Render(px, dc =>
            {
                dc.DrawLine(GrayPen(Regular), P(2, 5), P(30, 5));
                Path(dc, null, GrayPen(Regular), false, P(4, 5), P(9.5, 13), P(9.5, 29));
                Path(dc, null, GrayPen(Regular), false, P(28, 5), P(22.5, 13), P(22.5, 29));
                dc.DrawRectangle(Blue, null, new Rect(12.5, 14, 7, 13));
            });
        }

        /// <summary>Gates: a fence with an opening between two posts, its label in the gap.</summary>
        public static ImageSource Gates(int px) { return Render(px, GatesGlyph); }

        private static void GatesGlyph(DrawingContext dc)
        {
            dc.DrawLine(GrayPen(Regular), P(2, 22), P(10, 22));
            dc.DrawLine(GrayPen(Regular), P(22, 22), P(30, 22));
            dc.DrawRectangle(Blue, null, new Rect(8.5, 15, 3.5, 11));
            dc.DrawRectangle(Blue, null, new Rect(20, 15, 3.5, 11));
            dc.DrawRectangle(Gray, null, new Rect(12.5, 7, 7, 4));
        }

        /// <summary>Pair a Gate: the gate glyph with a plus -- two posts you pick.</summary>
        public static ImageSource GatePair(int px) { return Badged(px, GatesGlyph, PlusBadge); }

        /// <summary>Review: a list with a flag on its open item.</summary>
        public static ImageSource Review(int px) { return Render(px, ReviewGlyph); }

        private static void ReviewGlyph(DrawingContext dc)
        {
            for (var y = 7; y <= 25; y += 6) dc.DrawRectangle(Gray, null, new Rect(12, y - 1.5, 16, 3));
            dc.DrawLine(BluePen(Regular), P(5, 4), P(5, 27));
            Path(dc, Blue, null, true, P(5, 4), P(11, 7), P(5, 10));
        }

        /// <summary>Drafting QC Complete: the review list with a tick.</summary>
        public static ImageSource QcComplete(int px) { return Badged(px, ReviewGlyph, TickBadge); }

        /// <summary>A staircase.</summary>
        public static ImageSource Stairs(int px)
        {
            return Render(px, dc => Path(dc, null, BluePen(Regular), false,
                P(4, 28), P(12, 28), P(12, 20), P(20, 20), P(20, 12), P(28, 12), P(28, 4)));
        }

        /// <summary>Draw Lines: a pencil drafting a course.</summary>
        public static ImageSource DrawLine(int px) { return Render(px, DrawLineGlyph); }

        /// <summary>Clean Drafting: the drafted course, erased.</summary>
        public static ImageSource DraftClean(int px) { return Badged(px, DrawLineGlyph, EraseBadge); }

        private static void DrawLineGlyph(DrawingContext dc)
        {
            dc.DrawLine(GrayPen(Regular), P(3.5, 28.5), P(13.5, 18.5));
            // The pencil along the course, its point where the line ends.
            Path(dc, Blue, null, true, P(13.5, 18.5), P(19.65, 16.87), P(29.19, 7.33), P(24.67, 2.81), P(15.13, 12.35));
            dc.DrawLine(GrayPen(Light), P(19.65, 16.87), P(15.13, 12.35));
        }

        /// <summary>A break mark: a tilde across a line.</summary>
        public static ImageSource Tilde(int px)
        {
            return Render(px, dc =>
            {
                dc.DrawLine(GrayPen(Regular), P(3, 16), P(29, 16));
                var wave = new StreamGeometry();
                using (var g = wave.Open())
                {
                    g.BeginFigure(P(6, 17), false, false);
                    g.BezierTo(P(10, 9), P(13, 10), P(16, 16), true, true);
                    g.BezierTo(P(19, 22), P(22, 23), P(26, 15), true, true);
                }
                wave.Freeze();
                dc.PushTransform(new RotateTransform(-60, 16, 16));
                dc.DrawGeometry(null, BluePen(Heavy), wave);
                dc.Pop();
            });
        }

        /// <summary>A right-of-way width: the dimension from the right-of-way line to the centerline.</summary>
        public static ImageSource RowWidth(int px)
        {
            return Render(px, dc =>
            {
                dc.DrawLine(GrayPen(Light), P(3, 5), P(29, 5));
                dc.DrawLine(DashedGray(Light), P(3, 27), P(29, 27));
                dc.DrawLine(BluePen(Regular), P(16, 11), P(16, 21));
                Path(dc, Blue, null, true, P(16, 7.5), P(12.5, 12.5), P(19.5, 12.5));
                Path(dc, Blue, null, true, P(16, 24.5), P(12.5, 19.5), P(19.5, 19.5));
            });
        }

        /// <summary>A bench: its outline, with the two end shots at the middles of its ends.</summary>
        public static ImageSource Bench(int px)
        {
            return Render(px, dc =>
            {
                dc.DrawRectangle(null, GrayPen(Regular), new Rect(4, 11, 24, 10));
                dc.DrawEllipse(Blue, null, P(4, 16), 2.5, 2.5);
                dc.DrawEllipse(Blue, null, P(28, 16), 2.5, 2.5);
            });
        }

        /// <summary>A wetland buffer: the wetland, its round buffer line, stopped at a road edge.</summary>
        public static ImageSource WetlandBuffer(int px)
        {
            return Render(px, dc =>
            {
                Path(dc, Gray, null, true, P(11, 12), P(14, 10), P(17, 12), P(20, 11), P(19, 15), P(20, 19), P(16, 21),
                     P(13, 20), P(10, 21), P(11, 17), P(9, 14));
                var arc = new System.Collections.Generic.List<Point>();
                for (var a = 50; a <= 310; a += 10)
                    arc.Add(P(15 + 12 * System.Math.Cos(a * System.Math.PI / 180), 16 - 12 * System.Math.Sin(a * System.Math.PI / 180)));
                Path(dc, null, BluePen(Regular), false, arc.ToArray());
                dc.DrawLine(GrayPen(Regular), P(23, 3), P(23, 29));
            });
        }

        /// <summary>Sheet planning: a 2x2 grid of sheet windows.</summary>
        public static ImageSource PlanSheets(int px)
        {
            return Render(px, dc =>
            {
                dc.DrawRectangle(null, BluePen(Light), new Rect(4, 5, 11, 10));
                dc.DrawRectangle(null, BluePen(Light), new Rect(17, 5, 11, 10));
                dc.DrawRectangle(null, BluePen(Light), new Rect(4, 17, 11, 10));
                dc.DrawRectangle(null, BluePen(Light), new Rect(17, 17, 11, 10));
            });
        }

        /// <summary>Make layouts: a sheet with a plus.</summary>
        public static ImageSource MakeLayouts(int px)
        {
            return Render(px, dc =>
            {
                dc.DrawRectangle(null, GrayPen(Light), new Rect(4, 6, 16, 20));
                dc.DrawLine(BluePen(Heavy), P(24, 16), P(24, 26));
                dc.DrawLine(BluePen(Heavy), P(19, 21), P(29, 21));
            });
        }

        /// <summary>Sheet areas from layouts: two sheets with their match line.</summary>
        public static ImageSource SheetAreas(int px)
        {
            return Render(px, dc =>
            {
                dc.DrawRectangle(null, GrayPen(Light), new Rect(4, 8, 11, 16));
                dc.DrawRectangle(null, GrayPen(Light), new Rect(17, 8, 11, 16));
                dc.DrawLine(BluePen(Regular), P(16, 5), P(16, 27));
            });
        }

        /// <summary>Key map: the sheet grid with the current cell filled.</summary>
        public static ImageSource KeyMap(int px)
        {
            return Render(px, dc =>
            {
                dc.DrawRectangle(null, GrayPen(Light), new Rect(4, 6, 24, 20));
                dc.DrawLine(GrayPen(Fine), P(16, 6), P(16, 26));
                dc.DrawLine(GrayPen(Fine), P(4, 16), P(28, 16));
                dc.DrawRectangle(Blue, null, new Rect(17.2, 7.2, 9.6, 7.6));
            });
        }

        /// <summary>A match line: the zigzag along the seam between two sheets.</summary>
        public static ImageSource MatchLine(int px)
        {
            return Render(px, dc =>
            {
                dc.DrawRectangle(null, GrayPen(Light), new Rect(3, 6, 9.5, 20));
                dc.DrawRectangle(null, GrayPen(Light), new Rect(19.5, 6, 9.5, 20));
                Path(dc, null, BluePen(Regular), false,
                    P(16, 3), P(14, 7), P(18, 11), P(14, 15), P(18, 19), P(14, 23), P(18, 27), P(16, 29));
            });
        }

        // ===================================================== Boundary tab

        /// <summary>A recorded sheet with a closed traverse drawn on it.</summary>
        public static ImageSource Record(int px) { return Render(px, RecordGlyph); }

        /// <summary>Rebuild Record: the recorded traverse, rebuilt.</summary>
        public static ImageSource RecordRebuild(int px) { return Badged(px, RecordGlyph, RebuildBadge); }

        /// <summary>Line Source: the recorded traverse, inspected.</summary>
        public static ImageSource RecordSource(int px) { return Badged(px, RecordGlyph, InspectBadge); }

        /// <summary>Check vs Record: the recorded traverse, checked.</summary>
        public static ImageSource RecordCheck(int px) { return Badged(px, RecordGlyph, TickBadge); }

        private static void RecordGlyph(DrawingContext dc)
        {
            dc.DrawRectangle(null, GrayPen(Light), new Rect(5, 3, 22, 26));
            var corners = new[] { P(9, 9), P(22, 8), P(23, 20), P(12, 24) };
            Path(dc, null, BluePen(Regular), true, corners);
            foreach (var p in corners) dc.DrawEllipse(Blue, null, p, 2, 2);
        }

        /// <summary>Record labels: a call above its line, in the parentheses a record call is written in.</summary>
        public static ImageSource RecordLabel(int px)
        {
            return Render(px, dc =>
            {
                dc.DrawLine(GrayPen(Regular), P(3, 24), P(29, 24));
                LabelBox(dc, 10, 12);
                var brackets = new StreamGeometry();
                using (var g = brackets.Open())
                {
                    g.BeginFigure(P(7.5, 10), false, false);
                    g.QuadraticBezierTo(P(4, 15.5), P(7.5, 21), true, true);
                    g.BeginFigure(P(24.5, 10), false, false);
                    g.QuadraticBezierTo(P(28, 15.5), P(24.5, 21), true, true);
                }
                brackets.Freeze();
                dc.DrawGeometry(null, BluePen(Light), brackets);
            });
        }

        /// <summary>A right of way: its two lines, the dashed centerline between them, and the
        /// monuments it is drawn from.</summary>
        public static ImageSource RightOfWay(int px)
        {
            return Render(px, dc =>
            {
                dc.DrawLine(BluePen(Regular), P(3, 7), P(29, 7));
                dc.DrawLine(BluePen(Regular), P(3, 25), P(29, 25));
                dc.DrawLine(DashedGray(Light), P(3, 16), P(29, 16));
                dc.DrawEllipse(Gray, null, P(6, 7), 2.8, 2.8);
                dc.DrawEllipse(Gray, null, P(26, 7), 2.8, 2.8);
            });
        }

        /// <summary>A platted block divided into lots.</summary>
        public static ImageSource Lots(int px)
        {
            return Render(px, dc =>
            {
                dc.DrawRectangle(null, GrayPen(Regular), new Rect(3, 5, 26, 22));
                dc.DrawLine(BluePen(Light), P(9.5, 5), P(9.5, 27));
                dc.DrawLine(BluePen(Light), P(16, 5), P(16, 27));
                dc.DrawLine(BluePen(Light), P(22.5, 5), P(22.5, 27));
            });
        }

        /// <summary>Compare: our survey (blue) over the consultant's base map (gray).</summary>
        public static ImageSource Compare(int px) { return Render(px, CompareGlyph); }

        /// <summary>Run Compare: the comparison, run.</summary>
        public static ImageSource CompareRun(int px) { return Badged(px, CompareGlyph, PlayBadge); }

        /// <summary>Apply: the comparison's decisions, applied.</summary>
        public static ImageSource CompareApply(int px) { return Badged(px, CompareGlyph, TickBadge); }

        /// <summary>Restore Compare: the comparison, put back.</summary>
        public static ImageSource CompareRestore(int px) { return Badged(px, CompareGlyph, UndoBadge); }

        private static void CompareGlyph(DrawingContext dc)
        {
            dc.DrawEllipse(null, GrayPen(Regular), P(11.5, 15), 8, 8);
            dc.DrawEllipse(null, BluePen(Regular), P(20.5, 15), 8, 8);
        }

        /// <summary>Final QC: a clipboard, ticked.</summary>
        public static ImageSource CompareQc(int px)
        {
            return Render(px, dc =>
            {
                dc.DrawRectangle(null, GrayPen(Light), new Rect(6, 5, 20, 24));
                dc.DrawRectangle(Gray, null, new Rect(11.5, 2.5, 9, 5));
                Path(dc, null, BluePen(Heavy), false, P(10.5, 17.5), P(14.5, 21.5), P(22, 12.5));
            });
        }

        /// <summary>Save Deliverable: a disk.</summary>
        public static ImageSource CompareSave(int px)
        {
            return Render(px, dc =>
            {
                Path(dc, null, GrayPen(Light), true, P(5, 5), P(23, 5), P(27, 9), P(27, 27), P(5, 27));
                dc.DrawRectangle(Blue, null, new Rect(10, 5, 11, 7));
                dc.DrawRectangle(null, BluePen(Light), new Rect(9.5, 17, 13, 8));
            });
        }

        /// <summary>Batch Sites: a stack of drawings, run one after another.</summary>
        public static ImageSource CompareBatch(int px)
        {
            return Badged(px, dc =>
            {
                Path(dc, null, GrayPen(Light), false, P(11, 8), P(11, 4), P(27, 4), P(27, 16), P(23, 16));
                Path(dc, null, GrayPen(Light), false, P(7, 12), P(7, 8), P(23, 8), P(23, 20), P(19, 20));
                dc.DrawRectangle(null, BluePen(Regular), new Rect(3, 12, 16, 12));
            }, PlayBadge);
        }

        /// <summary>Trim Aerial: scissors.</summary>
        public static ImageSource Trim(int px) { return Render(px, TrimGlyph); }

        /// <summary>Restore Trim: the scissors, put back.</summary>
        public static ImageSource TrimRestore(int px) { return Badged(px, TrimGlyph, UndoBadge); }

        private static void TrimGlyph(DrawingContext dc)
        {
            // Drawn pointing right, then turned so the blades point up and to the right and the
            // lower right corner stays clear for a badge.
            dc.PushTransform(new RotateTransform(-45, 16, 16));
            dc.DrawLine(BluePen(Regular), P(28, 12), P(10, 18));
            dc.DrawLine(BluePen(Regular), P(28, 20), P(10, 14));
            dc.DrawEllipse(null, GrayPen(Light), P(7, 19.5), 3.2, 3.2);
            dc.DrawEllipse(null, GrayPen(Light), P(7, 12.5), 3.2, 3.2);
            dc.DrawEllipse(Blue, null, P(16, 16), 1.6, 1.6);
            dc.Pop();
        }

        // ==================================================== Easements tab

        /// <summary>Strip easement: a filled band between two parallel curves.</summary>
        public static ImageSource Easement(int px) { return Render(px, EasementGlyph); }

        /// <summary>Exclude Area: the easement, with a piece taken away.</summary>
        public static ImageSource EasementExclude(int px) { return Badged(px, EasementGlyph, MinusBadge); }

        /// <summary>Add Component: the easement, with a piece added.</summary>
        public static ImageSource EasementAdd(int px) { return Badged(px, EasementGlyph, PlusBadge); }

        /// <summary>Export Courses: the easement's geometry, written out.</summary>
        public static ImageSource EasementExport(int px) { return Badged(px, EasementGlyph, ExportBadge); }

        /// <summary>Inspect Easement: the easement, inspected.</summary>
        public static ImageSource EasementInspect(int px) { return Badged(px, EasementGlyph, InspectBadge); }

        /// <summary>Check Easements: the easement, checked.</summary>
        public static ImageSource EasementCheck(int px) { return Badged(px, EasementGlyph, TickBadge); }

        private static void EasementGlyph(DrawingContext dc)
        {
            var band = new StreamGeometry();
            using (var g = band.Open())
            {
                g.BeginFigure(P(3, 12), true, true);
                g.QuadraticBezierTo(P(16, 2), P(29, 12), true, true);
                g.LineTo(P(29, 21), true, true);
                g.QuadraticBezierTo(P(16, 11), P(3, 21), true, true);
            }
            band.Freeze();
            dc.DrawGeometry(Gray, BluePen(Regular), band);
            dc.DrawLine(BluePen(Fine), P(9, 26), P(23, 26));
        }

        /// <summary>Portion easement: a lot, with its south-west part taken.</summary>
        public static ImageSource PortionEasement(int px)
        {
            return Render(px, dc =>
            {
                dc.DrawRectangle(Blue, null, new Rect(4, 17, 9, 11));
                dc.DrawRectangle(null, GrayPen(Regular), new Rect(4, 4, 24, 24));
            });
        }

        /// <summary>Area easement: a metes-and-bounds area, hatched.</summary>
        public static ImageSource AreaEasement(int px)
        {
            return Render(px, dc =>
            {
                var area = new StreamGeometry();
                using (var g = area.Open())
                {
                    g.BeginFigure(P(5, 10), true, true);
                    g.LineTo(P(16, 4), true, true);
                    g.LineTo(P(28, 10), true, true);
                    g.LineTo(P(25, 27), true, true);
                    g.LineTo(P(7, 25), true, true);
                }
                area.Freeze();
                dc.PushClip(area);
                for (var c = -32; c < 32; c += 5) dc.DrawLine(GrayPen(Fine), P(c, 32), P(c + 32, 0));
                dc.Pop();
                dc.DrawGeometry(null, BluePen(Regular), area);
            });
        }

        /// <summary>A legal description: a page of writing.</summary>
        public static ImageSource Legal(int px)
        {
            return Render(px, dc =>
            {
                Page(dc);
                dc.DrawLine(BluePen(Light), P(10.5, 14), P(22.5, 14));
                dc.DrawLine(BluePen(Light), P(10.5, 19), P(22.5, 19));
                dc.DrawLine(BluePen(Light), P(10.5, 24), P(17, 24));
            });
        }

        /// <summary>An exhibit: a sheet with its title block and the easement in its viewport.</summary>
        public static ImageSource Exhibit(int px) { return Render(px, ExhibitGlyph); }

        /// <summary>Rebuild Exhibit: the exhibit, rebuilt.</summary>
        public static ImageSource ExhibitRebuild(int px) { return Badged(px, ExhibitGlyph, RebuildBadge); }

        /// <summary>Exhibit Group: the exhibit, with another easement added to it.</summary>
        public static ImageSource ExhibitGroup(int px) { return Badged(px, ExhibitGlyph, PlusBadge); }

        /// <summary>Exhibit QA: the exhibit, checked.</summary>
        public static ImageSource ExhibitQa(int px) { return Badged(px, ExhibitGlyph, TickBadge); }

        /// <summary>Inspect Exhibit: the exhibit, inspected.</summary>
        public static ImageSource ExhibitInspect(int px) { return Badged(px, ExhibitGlyph, InspectBadge); }

        private static void ExhibitGlyph(DrawingContext dc)
        {
            dc.DrawRectangle(null, GrayPen(Light), new Rect(3, 6, 26, 20));
            dc.DrawLine(GrayPen(Light), P(22, 6), P(22, 26));
            Path(dc, Blue, null, true, P(6, 21), P(15, 9.5), P(19, 12.5), P(10, 24));
        }

        /// <summary>Preview PDF: a plotter, with the sheet coming out.</summary>
        public static ImageSource Plot(int px)
        {
            return Render(px, dc =>
            {
                dc.DrawRectangle(null, GrayPen(Light), new Rect(10, 3, 12, 6));
                dc.DrawRectangle(Blue, null, new Rect(4, 9, 24, 10));
                dc.DrawRectangle(null, GrayPen(Light), new Rect(9, 19, 14, 10));
                dc.DrawLine(GrayPen(Fine), P(12, 23), P(20, 23));
                dc.DrawLine(GrayPen(Fine), P(12, 26), P(18, 26));
            });
        }

        /// <summary>A stamp: the rubber stamp and the impression it leaves.</summary>
        public static ImageSource Stamp(int px)
        {
            return Render(px, dc =>
            {
                dc.DrawEllipse(Blue, null, P(16, 7), 4, 3.5);
                dc.DrawRectangle(Blue, null, new Rect(14, 9, 4, 8));
                dc.DrawRectangle(Blue, null, new Rect(6, 16, 20, 6));
                dc.DrawLine(GrayPen(Regular), P(6, 27), P(26, 27));
            });
        }
    }
}
