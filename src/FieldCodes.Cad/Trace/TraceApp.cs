#if TRACE_EDITION
using Autodesk.AutoCAD.Runtime;

[assembly: ExtensionApplication(typeof(FieldCodes.Cad.TraceApp))]

namespace FieldCodes.Cad
{
    /// <summary>
    /// Startup for the TRACE edition: builds the TRACE ribbon tab and restores the palette.
    /// The internal edition keeps its own startup (FtfRibbon) and never sees this class.
    /// </summary>
    public sealed class TraceApp : IExtensionApplication
    {
        public void Initialize()
        {
            TraceLog.Write("TRACE " + typeof(TraceApp).Assembly.GetName().Version + " loaded from " + typeof(TraceApp).Assembly.Location);
            try
            {
                TraceRibbon.Start();
            }
            catch (System.Exception ex)
            {
                TraceLog.Write("ribbon start", ex);
            }
            TracePalette.StartWhenIdle();
        }

        public void Terminate()
        {
            TraceLog.Write("Civil 3D closing");
            TracePalette.OnExit();
        }
    }
}
#endif
