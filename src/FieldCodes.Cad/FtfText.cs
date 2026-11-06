using System.Drawing;
using System.Windows.Forms;
using Autodesk.AutoCAD.EditorInput;
using FieldCodes.TraceUi;

namespace FieldCodes.Cad
{
    /// <summary>
    /// Where the commercial edition's wording is applied. In the internal edition every method
    /// here passes text through unchanged, so its output is exactly what it always was.
    /// </summary>
    internal static class FtfText
    {
        /// <summary>The text, in TRACE wording when this is the commercial edition.</summary>
        public static string Brand(string text)
        {
            return Branding.IsCommercial ? TraceWording.Brand(text) : text;
        }

        /// <summary>
        /// Writes to the command line exactly as Editor.WriteMessage does (formatting only when
        /// arguments are given), in TRACE wording for the commercial edition.
        /// </summary>
        public static void Tell(this Editor ed, string format, params object[] args)
        {
            if (ed == null || format == null) return;
            var text = (args == null || args.Length == 0) ? format : string.Format(format, args);
            ed.WriteMessage("{0}", Brand(text));
        }

        /// <summary>
        /// Commercial edition only: puts TRACE wording and the Segoe UI face on a window's
        /// captions. Editable input (text boxes, grids, lists) is never touched.
        /// </summary>
        public static void BrandControls(Control root)
        {
            if (!Branding.IsCommercial || root == null) return;

            if (root is Label || root is ButtonBase || root is GroupBox || root is Form)
            {
                var text = TraceWording.Brand(root.Text);
                if (text != root.Text) root.Text = text;
            }

            var font = root.Font;
            if (font != null && font.FontFamily.Name == SystemFonts.DefaultFont.FontFamily.Name)
                root.Font = new Font("Segoe UI", font.Size * 9f / SystemFonts.DefaultFont.Size, font.Style);

            foreach (Control child in root.Controls) BrandControls(child);
        }
    }
}
