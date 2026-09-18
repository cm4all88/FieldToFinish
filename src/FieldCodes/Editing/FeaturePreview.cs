using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FieldCodes.Editing
{
    /// <summary>
    /// Renders what FTF would do with one description, in the words a surveyor reads:
    ///
    ///     Power Pole
    ///
    ///     Civil 3D symbol: Existing
    ///     FTF action: Label
    ///     Label: POLE 1234
    ///     Layer: V-UTIL-POWR-TEXT
    ///     Tag: P#
    ///     Rotation: None
    ///
    /// A pure function of the parse result -- previewing can never touch a drawing,
    /// and the wording never claims FTF creates the Civil 3D geometry.
    /// </summary>
    public static class FeaturePreview
    {
        public static string Render(ParsedPoint p, string featureTitle)
        {
            if (p == null) return string.Empty;

            if (p.Ignored)
                return "Linework or never-drawn code.\nFTF places nothing; Civil 3D owns this.";

            if (p.NoContent)
                return "Bare code - nothing to finish.\nThe existing Civil 3D symbol stands as-is.";

            if (p.Unhandled)
                return "No enabled rule matches this description.\n" +
                       "It would appear in the unknown-codes report.";

            if (p.HasErrors)
                return "ERROR: " + p.Diagnostics
                    .First(d => d.Severity == Severity.Error).Message;

            var lines = new List<string>();
            lines.Add(string.IsNullOrWhiteSpace(featureTitle) ? p.RuleId : featureTitle);
            lines.Add(string.Empty);

            lines.Add(p.InsertBlock && !string.IsNullOrEmpty(p.BlockName)
                ? "Civil 3D symbol: none - FTF inserts block " + p.BlockName + " (opt-in rule)"
                : "Civil 3D symbol: Existing");

            var actions = new List<string>();
            if (p.RotationDegrees.HasValue) actions.Add("Rotate existing marker");
            if (!string.IsNullOrEmpty(p.LabelText)) actions.Add("Label");
            if (p.HasDripLine) actions.Add("Dripline");
            lines.Add("FTF action: " + (actions.Count > 0
                ? string.Join(" + ", actions.ToArray())
                : "None"));

            lines.Add("Rotation: " + (p.RotationDegrees.HasValue
                ? p.RotationDegrees.Value.ToString("0.#", CultureInfo.InvariantCulture) + "°"
                : "None"));

            if (!string.IsNullOrEmpty(p.LabelText))
            {
                lines.Add("Label: " + p.LabelText);
                lines.Add("Layer: " + p.LabelLayer);
            }

            if (p.HasDripLine && p.DripRadius.HasValue)
                lines.Add("Dripline: r=" +
                          p.DripRadius.Value.ToString("0.#", CultureInfo.InvariantCulture) +
                          " on " + p.DripLayer);

            if (!string.IsNullOrEmpty(p.TagPrefix))
                lines.Add("Tag: " + p.TagPrefix + "# (numbered on placement; numbers " +
                          "never reissued)");

            return string.Join(Environment.NewLine, lines.ToArray());
        }
    }
}
