#if TRACE_EDITION
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace FieldCodes.Cad
{
    /// <summary>Runs a command for the ribbon and the palette, without echoing its name.</summary>
    internal static class TraceDispatch
    {
        public static void Send(string command)
        {
            if (string.IsNullOrEmpty(command)) return;

            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            // activate: true brings the drawing window forward; echo: false keeps the command
            // name off the command line.
            doc.SendStringToExecute(command + " ", true, false, false);
        }
    }
}
#endif
