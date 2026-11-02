using System;
using System.Collections.Generic;
using System.Linq;
using FieldCodes.Settings;

namespace FieldCodes.Styles
{
    /// <summary>What a drawing has, as far as the style check needs to know.</summary>
    public interface IStyleResources
    {
        bool HasTextStyle(string name);
        bool HasMLeaderStyle(string name);
        bool HasBlock(string name);
        bool HasLinetype(string name);
    }

    /// <summary>
    /// Checks the styles against a drawing before anything is drawn: every text style,
    /// multileader style, arrow block and linetype the settings name has to exist. What is
    /// missing is listed by name with where it is set, so the drafter fixes the setting or
    /// loads the resource -- FTF never quietly draws with something else and says nothing.
    /// </summary>
    public static class StyleCheck
    {
        /// <summary>AutoCAD's built-in arrowheads (DIMBLK names). FTF loads them into the
        /// drawing on first use, the way AutoCAD does when one is picked in a dialog.</summary>
        public static readonly IList<string> BuiltInArrows = new List<string>
        {
            "_ClosedBlank", "_Closed", "_Dot", "_ArchTick", "_Oblique", "_Open", "_Origin", "_Origin2",
            "_Open90", "_Open30", "_DotSmall", "_DotBlank", "_Small", "_BoxBlank", "_BoxFilled",
            "_DatumBlank", "_DatumFilled", "_Integral", "_None"
        }.AsReadOnly();

        public static bool IsBuiltInArrow(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            return BuiltInArrows.Any(a => string.Equals(a, name.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>A built-in arrow's name as AutoCAD lists it ("Dot small").</summary>
        public static string ArrowDisplayName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Closed filled (style default)";
            var n = name.Trim();
            if (!IsBuiltInArrow(n)) return n + " (block)";
            var bare = n.TrimStart('_');
            var sb = new System.Text.StringBuilder();
            for (var i = 0; i < bare.Length; i++)
            {
                if (i > 0 && char.IsUpper(bare[i])) sb.Append(' ').Append(char.ToLowerInvariant(bare[i]));
                else sb.Append(bare[i]);
            }
            return sb.ToString();
        }

        public static bool IsSpecialLinetype(string name)
        {
            var n = (name ?? string.Empty).Trim();
            return n.Length == 0 ||
                   string.Equals(n, "ByLayer", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(n, "ByBlock", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(n, "Continuous", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Everything the settings name that this drawing does not have, in words.</summary>
        public static IList<string> Missing(FtfSettings settings, IStyleResources drawing)
        {
            var problems = new List<string>();
            if (drawing == null) return problems;
            var s = settings ?? new FtfSettings();
            var styles = s.Styles ?? new StyleSettings();
            styles.FillMissing();

            Text(drawing, s.Labels != null ? s.Labels.TextStyle : null, "Settings > Point Labels > Text style", problems);
            Text(drawing, s.Tags != null ? s.Tags.TagTextStyle : null, "Settings > Tags > Text style", problems);
            Text(drawing, s.LineLabels != null ? s.LineLabels.TextStyle : null, "Settings > Line Labels > Text style", problems);

            foreach (var t in styles.Text)
                Text(drawing, t.TextStyle, "Text style \"" + t.Name + "\"", problems);

            foreach (var l in styles.Leaders)
            {
                var who = "Leader style \"" + l.Name + "\"";
                if (!string.IsNullOrWhiteSpace(l.MLeaderStyle) && !drawing.HasMLeaderStyle(l.MLeaderStyle.Trim()))
                    problems.Add(who + ": multileader style \"" + l.MLeaderStyle.Trim() + "\" is not in this drawing.");
                if (!string.IsNullOrWhiteSpace(l.ArrowBlock) && !IsBuiltInArrow(l.ArrowBlock) && !drawing.HasBlock(l.ArrowBlock.Trim()))
                    problems.Add(who + ": arrowhead block \"" + l.ArrowBlock.Trim() + "\" is not in this drawing.");
                if (!IsSpecialLinetype(l.Linetype) && !drawing.HasLinetype(l.Linetype.Trim()))
                    problems.Add(who + ": linetype \"" + l.Linetype.Trim() + "\" is not loaded in this drawing.");
            }
            return problems;
        }

        private static void Text(IStyleResources drawing, string name, string where, ICollection<string> problems)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            if (!drawing.HasTextStyle(name.Trim()))
                problems.Add(where + ": text style \"" + name.Trim() + "\" is not in this drawing.");
        }
    }
}
