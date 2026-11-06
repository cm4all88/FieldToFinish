using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace FieldCodes.TraceUi
{
    /// <summary>
    /// Turns the existing tools' internal wording (FTFDRIP, "FTF finishing", ...) into TRACE
    /// wording for the commercial edition. The tools keep their own message text; this is applied
    /// where the text is shown, so nothing about how they work changes and the internal edition
    /// (which never calls this) prints exactly what it always did.
    /// </summary>
    public static class TraceWording
    {
        // Longer names are matched whole, so FTFLABELLINE is never read as FTFLABEL + LINE.
        private static readonly Dictionary<string, string> Names = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
        {
            { "FTFPOINTS", "Points" },
            { "FTFTREES", "Trees" },
            { "FTFDRIP", "Driplines" },
            { "FTFLABELS", "Labels" },
            { "FTFTAGS", "Tags" },
            { "FTFTABLE", "Schedule" },
            { "FTFORDER", "Draw order" },
            { "FTFLINELABELS", "Line labels" },
            { "FTFLABELLINE", "Label line" },
            { "FTFLABELBETWEEN", "Label between" },
            { "FTFLABELSTAIRS", "Stairs" },
            { "FTFLINES", "Line inventory" },
            { "FTFLINEMETA", "Line details" },
            { "FTFSPOT", "Spot elevations" },
            { "FTFCONTROL", "Control table" },
            { "FTFLEGEND", "Legend" },
            { "FTFDRAWLINE", "Draw line" },
            { "FTFDRAFTCLEAN", "Clean drafted" },
            { "FTFCLEAN", "Clean up" },
            { "FTFSETUP", "Setup" },
            { "FTFSETTINGS", "Setup" },
            { "FTFCONFIG", "Setup" },
            { "FTFOPTIONS", "Setup" },
            { "FTFPROFILE", "Switch profile" },
            { "FTFWHERE", "Diagnostics" },
            { "FTFBLOCKS", "Block check" },
            { "FTFRUN", "Run TRACE" },
            { "FTF", "TRACE" }
        };

        private static readonly Regex Token = new Regex(@"\bFTF[A-Z0-9]*\b", RegexOptions.Compiled);
        private static readonly Regex Owned = new Regex(@"\bFTF-owned\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex Product = new Regex(@"\bField to Finish\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// Returns the text with no internal FTF names left. Text that mentions none comes back
        /// unchanged. Names such as FTF_FIELDTOFINISH (stored data) are not touched.
        /// </summary>
        public static string Brand(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            if (text.IndexOf("FTF", System.StringComparison.OrdinalIgnoreCase) < 0 &&
                text.IndexOf("Field to Finish", System.StringComparison.OrdinalIgnoreCase) < 0)
                return text;

            var result = Owned.Replace(text, "TRACE-made");
            result = Product.Replace(result, "TRACE");
            result = Token.Replace(result, m =>
            {
                string friendly;
                return Names.TryGetValue(m.Value, out friendly) ? friendly : "TRACE";
            });
            return result;
        }
    }
}
