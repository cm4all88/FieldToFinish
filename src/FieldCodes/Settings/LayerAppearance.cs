using System;
using System.Globalization;

namespace FieldCodes.Settings
{
    /// <summary>
    /// Reads the colour and lineweight an office setting names, so a layer FTF has to
    /// create looks like the office's own. Both are written the way a drafter says them:
    /// a colour by name or ACI number, a lineweight in millimetres.
    ///
    /// Anything FTF cannot read comes back null and the caller leaves the layer alone
    /// rather than inventing a value.
    /// </summary>
    public static class LayerAppearance
    {
        private static readonly string[] Names = { "red", "yellow", "green", "cyan", "blue", "magenta", "white" };

        /// <summary>An ACI colour index: "green", "3", "250". Null when unset or unreadable.</summary>
        public static short? ColorIndex(string setting)
        {
            var text = (setting ?? string.Empty).Trim();
            if (text.Length == 0) return null;

            short index;
            if (short.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out index))
                return index >= 0 && index <= 256 ? index : (short?)null;

            for (var i = 0; i < Names.Length; i++)
                if (string.Equals(text, Names[i], StringComparison.OrdinalIgnoreCase)) return (short)(i + 1);
            return null;
        }

        /// <summary>
        /// A lineweight in hundredths of a millimetre, which is how AutoCAD numbers them:
        /// "0.40" and "40" are both LineWeight040. "ByLayer", "ByBlock" and "Default" are
        /// AutoCAD's own -1, -2 and -3.
        /// </summary>
        public static int? LineWeightHundredths(string setting)
        {
            var text = (setting ?? string.Empty).Trim();
            if (text.Length == 0) return null;
            if (string.Equals(text, "ByLayer", StringComparison.OrdinalIgnoreCase)) return -1;
            if (string.Equals(text, "ByBlock", StringComparison.OrdinalIgnoreCase)) return -2;
            if (string.Equals(text, "Default", StringComparison.OrdinalIgnoreCase)) return -3;

            double mm;
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out mm)) return null;
            // Written either way: 0.40 mm or 40 hundredths.
            var hundredths = (int)Math.Round(mm < 3.0 ? mm * 100.0 : mm);
            return hundredths >= 0 && hundredths <= 211 ? hundredths : (int?)null;
        }
    }
}
