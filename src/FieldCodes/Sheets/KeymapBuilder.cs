using System;
using System.Collections.Generic;

namespace FieldCodes.Sheets
{
    /// <summary>One sheet's cell inside the key map, in key map local units
    /// (origin bottom-left).</summary>
    public struct KeymapCell
    {
        public string Name;
        public string ShortName;
        public double X, Y, W, H;
        public bool Current;
    }

    /// <summary>The whole key map, scaled to the requested width.</summary>
    public sealed class Keymap
    {
        public double Width { get; set; }
        public double Height { get; set; }
        public IList<KeymapCell> Cells { get; set; }

        public Keymap() { Cells = new List<KeymapCell>(); }
    }

    /// <summary>
    /// Shrinks the sheet grid into the little index diagram every plan sheet
    /// carries: all the windows at true relative positions, aspect preserved,
    /// the current sheet flagged so it can be highlighted. Cells carry a short
    /// name -- the trailing number when the sheet has one -- because a 2-inch
    /// cell has no room for "SHEET 12".
    /// </summary>
    public static class KeymapBuilder
    {
        public static Keymap Build(IList<SheetWindow> windows, string currentSheet,
                                   double targetWidth)
        {
            var map = new Keymap();
            if (windows == null || windows.Count == 0 || targetWidth <= 0) return map;

            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;
            foreach (var window in windows)
            {
                minX = Math.Min(minX, window.MinX);
                minY = Math.Min(minY, window.MinY);
                maxX = Math.Max(maxX, window.MaxX);
                maxY = Math.Max(maxY, window.MaxY);
            }
            if (maxX <= minX || maxY <= minY) return map;

            var scale = targetWidth / (maxX - minX);
            map.Width = targetWidth;
            map.Height = (maxY - minY) * scale;

            foreach (var window in windows)
            {
                map.Cells.Add(new KeymapCell
                {
                    Name = window.Name,
                    ShortName = ShortName(window.Name),
                    X = (window.MinX - minX) * scale,
                    Y = (window.MinY - minY) * scale,
                    W = window.Width * scale,
                    H = window.Height * scale,
                    Current = string.Equals(window.Name, currentSheet,
                                            StringComparison.OrdinalIgnoreCase)
                });
            }

            return map;
        }

        /// <summary>"SHEET 12" -> "12"; a name with no trailing number stays.</summary>
        internal static string ShortName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;

            var trimmed = name.Trim();
            var start = trimmed.Length;
            while (start > 0 && char.IsDigit(trimmed[start - 1])) start--;

            return start < trimmed.Length ? trimmed.Substring(start) : trimmed;
        }
    }
}
