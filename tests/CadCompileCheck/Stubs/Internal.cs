// Internal FieldCodes.Cad types the scaffold does not compile from source (their files pull in
// the whole plugin). Shapes copied from the real definitions.
namespace FieldCodes.Cad
{
    internal sealed class InspectRow
    {
        public string Section;
        public string Item;
        public string Value;
        public string Source;
        public string Handle;
        public string Layout = null;
        public string Severity = string.Empty;
    }
}

namespace FieldCodes.Cad
{
    internal static class EasementInspectCommands
    {
        internal static void ShowWindow(string title, System.Collections.Generic.List<InspectRow> rows) { }
        internal static bool Headless() { return true; }
        internal static string AsText(string title, System.Collections.Generic.IList<InspectRow> rows) { return string.Empty; }
    }

    internal static class EasementCommands
    {
        internal static System.Collections.Generic.IList<FieldCodes.Easements.Course> Extract(Autodesk.AutoCAD.DatabaseServices.Entity entity, out string problem) { problem = null; return null; }
    }
}

namespace FieldCodes.Cad.Ui
{
    internal static class ThemePreference
    {
        public static bool LoadDark() { return true; }
        public static void SaveDark(bool dark) { }
    }

    internal static class DipBuilderForm
    {
        internal static System.Drawing.Color Ground = System.Drawing.Color.Black, Surface = System.Drawing.Color.Black, Ink = System.Drawing.Color.Black, Muted = System.Drawing.Color.Black, PreviewBack = System.Drawing.Color.Black, Warn = System.Drawing.Color.Black, Good = System.Drawing.Color.Black;
        internal static bool Dark;
        internal static void UseTheme(bool dark) { Dark = dark; }
        internal static System.Drawing.Font F(float size, bool heavy) { return null; }
    }
}
