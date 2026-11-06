#if TRACE_EDITION
using System;
using System.Windows.Media;
using Autodesk.Windows;
using FieldCodes.TraceUi;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace FieldCodes.Cad
{
    /// <summary>
    /// The TRACE ribbon tab. Built entirely from TraceCatalog, so every button runs an existing
    /// command and the palette shows the same tools. Users see TRACE wording only.
    /// </summary>
    internal static class TraceRibbon
    {
        private const string TabId = "TRACE_RIBBON_TAB";

        public static void Start()
        {
            if (ComponentManager.Ribbon != null)
            {
                TraceLog.Write("ribbon already available at startup; building the TRACE tab");
                Build();
                return;
            }
            TraceLog.Write("ribbon not ready yet; waiting for it");
            ComponentManager.ItemInitialized += OnItemInitialized;
        }

        private static void OnItemInitialized(object sender, RibbonItemEventArgs e)
        {
            if (ComponentManager.Ribbon == null) return;
            ComponentManager.ItemInitialized -= OnItemInitialized;
            TraceLog.Write("ribbon became ready; building the TRACE tab");
            Build();
        }

        /// <summary>
        /// A second chance once Civil 3D is idle, in case the ribbon-ready notification never
        /// reached us. Does nothing if the tab is already there.
        /// </summary>
        internal static void EnsureBuilt()
        {
            try
            {
                if (ComponentManager.Ribbon == null)
                {
                    TraceLog.Write("idle check: the ribbon still does not exist");
                    return;
                }
                if (ComponentManager.Ribbon.FindTab(TabId) != null) return;
                TraceLog.Write("idle check: TRACE tab missing; building it now");
                Build();
            }
            catch (System.Exception ex)
            {
                TraceLog.Write("idle ribbon check", ex);
            }
        }

        /// <summary>True on Civil 3D's light colour theme. Dark is the default.</summary>
        internal static bool IsLightTheme()
        {
            try
            {
                return Convert.ToInt32(AcadApp.GetSystemVariable("COLORTHEME")) == 1;
            }
            catch (System.Exception)
            {
                return false;
            }
        }

        private static void Build()
        {
            try
            {
                var ribbon = ComponentManager.Ribbon;
                if (ribbon == null || ribbon.FindTab(TabId) != null) return;

                var light = IsLightTheme();
                var tab = new RibbonTab { Title = "TRACE", Id = TabId };

                tab.Panels.Add(Panel("TRACE",
                    Button("Run TRACE", TraceCatalog.RunCommand,
                        "Run the steps ticked in the TRACE palette. Your own edits are left alone.",
                        "run", TraceButtonSize.Large, light),
                    Button("Palette", TraceCatalog.OpenCommand,
                        "Show or hide the TRACE palette.",
                        "trace", TraceButtonSize.Large, light)));

                foreach (var group in TraceCatalog.Groups)
                {
                    var items = new System.Collections.Generic.List<RibbonItem>();
                    foreach (var b in group.Buttons)
                        items.Add(Button(b.Text, b.Command, b.Tip, b.Icon, b.Size, light));
                    tab.Panels.Add(Panel(group.Title, items.ToArray()));
                }

                ribbon.Tabs.Add(tab);
                TraceLog.Write("TRACE tab added with " + tab.Panels.Count + " panels");
            }
            catch (System.Exception ex)
            {
                // A ribbon that cannot build must never take Civil 3D down. The palette and
                // the TRACE command still work; the log says why.
                TraceLog.Write("building the TRACE tab", ex);
            }
        }

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

                // Small buttons stack in a column inside a row panel, as in the existing ribbon.
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

        private static RibbonButton Button(string text, string command, string tip, string icon,
                                           TraceButtonSize size, bool light)
        {
            var large = size == TraceButtonSize.Large;

            // Large buttons wrap their label onto two lines at the first space.
            var label = text;
            if (large)
            {
                var space = text.IndexOf(' ');
                if (space > 0) label = text.Substring(0, space) + "\n" + text.Substring(space + 1);
            }

            var button = new RibbonButton
            {
                Text = label,
                ShowText = true,
                ShowImage = true,
                Image = TraceIconWpf.Render(icon, 16, light),
                LargeImage = TraceIconWpf.Render(icon, 32, light),
                ToolTip = tip,
                CommandParameter = command,
                CommandHandler = new RunCommand(),
                Size = large ? RibbonItemSize.Large : RibbonItemSize.Standard,
                Orientation = large
                    ? System.Windows.Controls.Orientation.Vertical
                    : System.Windows.Controls.Orientation.Horizontal
            };
            return button;
        }

        /// <summary>
        /// Posts the button's command to the command line without echoing its name. Goes through
        /// SendStringToExecute because a ribbon click arrives on WPF's dispatcher, not in a
        /// command context.
        /// </summary>
        private sealed class RunCommand : System.Windows.Input.ICommand
        {
            public event EventHandler CanExecuteChanged { add { } remove { } }
            public bool CanExecute(object parameter) { return true; }

            public void Execute(object parameter)
            {
                try
                {
                    // The ribbon passes the BUTTON here, not its CommandParameter.
                    var command = parameter as string;
                    if (command == null)
                    {
                        var item = parameter as RibbonCommandItem;
                        if (item != null) command = item.CommandParameter as string;
                    }
                    TraceDispatch.Send(command);
                }
                catch (System.Exception)
                {
                    // A button must never crash the session.
                }
            }
        }
    }
}
#endif
