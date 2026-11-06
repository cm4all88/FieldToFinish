#if TRACE_EDITION
using System;
using System.Drawing;
using Autodesk.AutoCAD.Windows;
using FieldCodes.TraceUi;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace FieldCodes.Cad
{
    /// <summary>
    /// The dockable TRACE palette. Resizable, collapsible (auto-hide) and dockable left or
    /// right; Civil 3D remembers its size, dock side and position because it is created with a
    /// fixed id. Whether it was open is remembered by TRACE, so it is not forced open every
    /// session: the very first run shows it once, after that it reopens only if you left it open.
    /// </summary>
    internal static class TracePalette
    {
        private static readonly Guid PaletteId = new Guid("6C0A3F55-2B9E-4D71-9F0E-3A6D8B1C5E42");
        private static PaletteSet _set;

        /// <summary>Restores the palette once Civil 3D is idle (too early at plugin load).</summary>
        public static void StartWhenIdle()
        {
            AcadApp.Idle += OnIdle;
        }

        private static void OnIdle(object sender, EventArgs e)
        {
            AcadApp.Idle -= OnIdle;
            TraceRibbon.EnsureBuilt();
            try
            {
                var state = TraceUiState.Load(TraceUiState.DefaultPath());
                TraceLog.Write("idle: palette visible last time = " + (state.PaletteVisible == null ? "never used" : state.PaletteVisible.ToString()));
                if (state.PaletteVisible == null) Show(true);          // first run: show it once
                else if (state.PaletteVisible == true) Show(false);    // left open last time
            }
            catch (System.Exception ex)
            {
                // The palette is a convenience. A failure here must not affect Civil 3D; the log says why.
                TraceLog.Write("restoring the palette", ex);
            }
        }

        /// <summary>Shows the palette, or hides it if it is already showing.</summary>
        public static void Toggle()
        {
            if (_set != null && _set.Visible)
            {
                _set.Visible = false;
                Remember(false);
                return;
            }
            Show(false);
        }

        private static void Show(bool firstRun)
        {
            if (_set == null)
            {
                _set = new PaletteSet("TRACE", PaletteId);
                _set.Style = PaletteSetStyles.ShowPropertiesMenu |
                             PaletteSetStyles.ShowAutoHideButton |
                             PaletteSetStyles.ShowCloseButton;
                _set.MinimumSize = new Size(300, 420);
                _set.DockEnabled = DockSides.Left | DockSides.Right;
                _set.Add("TRACE", new TracePanel());
                if (firstRun) _set.Size = new Size(360, 760);
            }

            _set.Visible = true;
            if (firstRun) _set.Dock = DockSides.Right;
            Remember(true);
        }

        private static void Remember(bool visible)
        {
            var state = TraceUiState.Load(TraceUiState.DefaultPath());
            state.PaletteVisible = visible;
            state.Save(TraceUiState.DefaultPath());
        }

        /// <summary>Called when Civil 3D closes: remember whether the palette was open.</summary>
        public static void OnExit()
        {
            try
            {
                if (_set != null) Remember(_set.Visible);
            }
            catch (System.Exception)
            {
                // Closing down: nothing useful to do with a failure.
            }
        }
    }
}
#endif
