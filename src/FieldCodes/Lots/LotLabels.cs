using System;
using System.Collections.Generic;
using System.Globalization;
using FieldCodes.Drafting;
using FieldCodes.Row;
using FieldCodes.Settings;

namespace FieldCodes.Lots
{
    /// <summary>
    /// The words on a platted block, in the office's formats. Every figure comes from the plan's
    /// geometry, so a label cannot disagree with the lot it labels.
    /// </summary>
    public static class LotLabels
    {
        /// <summary>
        /// Fills the text of every line, lot, drainage arrow and the alley, and the plat notes.
        /// <paramref name="lotType"/> and <paramref name="alleyType"/> are the Drafting Lines types
        /// whose annotation format the lot lines and the alley use. A line on the block's own boundary
        /// carries its distance only. <paramref name="degreeSymbol"/> is "%%d" for DBText.
        /// </summary>
        public static void Apply(LotPlan plan, LotJob job, LotSettings settings, DraftingLineType lotType,
                                 DraftingLineType alleyType, double unitsPerFoot, string degreeSymbol)
        {
            if (plan == null) throw new ArgumentNullException("plan");
            if (job == null) throw new ArgumentNullException("job");
            if (settings == null) throw new ArgumentNullException("settings");
            var upf = unitsPerFoot > 0 ? unitsPerFoot : 1.0;
            var lotStyle = lotType ?? new DraftingLineType();
            var alleyStyle = alleyType ?? new DraftingLineType();

            foreach (var line in plan.Lines)
            {
                line.Text.Clear();
                var style = line.Kind == LotLineKind.Alley ? alleyStyle : lotStyle;
                if (line.DistanceOnly && line.Course.Kind == FieldCodes.Easements.CourseKind.Line)
                {
                    if (!style.WantsAnnotation) continue;
                    var distance = style.Clone();
                    distance.Annotation = DraftingLineType.AnnotationDistanceOnly;
                    distance.Stacked = false;
                    style = distance;
                }
                foreach (var text in RowLabels.Course(line.Data, style, upf, degreeSymbol)) line.Text.Add(Fit(text, settings, degreeSymbol));
            }

            foreach (var lot in plan.Lots)
            {
                lot.Text.Clear();
                var number = Tokens(settings.LotNumberFormat, new Dictionary<string, string>
                {
                    { "num", lot.Number }, { "block", (job.BlockName ?? string.Empty).Trim() }
                });
                if (number.Length > 0) lot.Text.Add(Fit(number, settings, degreeSymbol));
                if (!string.IsNullOrWhiteSpace(settings.LotAreaFormat))
                    lot.Text.Add(Fit(Tokens(settings.LotAreaFormat, new Dictionary<string, string> { { "sf", lot.AreaSqFt.ToString("#,##0", CultureInfo.InvariantCulture) } }), settings, degreeSymbol));
                if (!string.IsNullOrWhiteSpace(settings.LotAcresFormat))
                    lot.Text.Add(Fit(Tokens(settings.LotAcresFormat, new Dictionary<string, string> { { "ac", (lot.AreaSqFt / 43560.0).ToString("0.000", CultureInfo.InvariantCulture) } }), settings, degreeSymbol));

                foreach (var arrow in lot.Drains)
                    arrow.Text = string.IsNullOrWhiteSpace(settings.DrainFormat)
                        ? string.Empty
                        : Fit(Tokens(settings.DrainFormat, new Dictionary<string, string> { { "pct", Feet(arrow.Percent) } }), settings, degreeSymbol);
            }

            if (plan.Alley != null)
                plan.Alley.Text = string.IsNullOrWhiteSpace(settings.AlleyFormat)
                    ? string.Empty
                    : Fit(Tokens(settings.AlleyFormat, new Dictionary<string, string> { { "width", Feet(plan.Alley.WidthFeet) } }), settings, degreeSymbol);

            plan.Title = string.IsNullOrWhiteSpace(job.BlockName) || string.IsNullOrWhiteSpace(settings.BlockTitleFormat)
                ? string.Empty
                : Fit(Tokens(settings.BlockTitleFormat, new Dictionary<string, string> { { "block", job.BlockName.Trim() } }), settings, degreeSymbol);

            plan.GeneralNotes.Clear();
            var setbacks = new List<string>();
            Add(setbacks, "FRONT", job.FrontSetbackFeet);
            Add(setbacks, "SIDE", job.SideSetbackFeet);
            Add(setbacks, "CORNER SIDE", job.CornerSideSetbackFeet);
            Add(setbacks, "REAR", job.RearSetbackFeet);
            if (setbacks.Count > 0) plan.GeneralNotes.Add(Fit(Join(settings.SetbackNotePrefix, setbacks), settings, degreeSymbol));

            var easements = new List<string>();
            Add(easements, "FRONT", job.FrontEasementFeet);
            Add(easements, "SIDE", job.SideEasementFeet);
            Add(easements, "REAR", job.RearEasementFeet);
            if (easements.Count > 0) plan.GeneralNotes.Add(Fit(Join(settings.EasementNotePrefix, easements), settings, degreeSymbol));
        }

        private static void Add(IList<string> items, string name, double feet)
        {
            if (feet > 0) items.Add(name + " " + Feet(feet) + "'");
        }

        private static string Join(string prefix, IList<string> items)
        {
            return ((prefix ?? string.Empty).Trim() + " " + string.Join(", ", new List<string>(items).ToArray())).Trim();
        }

        /// <summary>25 not 25.00, 12.5 as is.</summary>
        public static string Feet(double feet)
        {
            return feet.ToString("0.##", CultureInfo.InvariantCulture);
        }

        private static string Tokens(string format, IDictionary<string, string> values)
        {
            var text = format ?? string.Empty;
            foreach (var pair in values) text = text.Replace("{" + pair.Key + "}", pair.Value);
            return text.Trim();
        }

        /// <summary>
        /// Capitals when the office wants them, but never over the degree token -- see the same
        /// note on RowLabels.Fit. A lot line's bearing goes through here, so this is the label
        /// that would otherwise carry %%D.
        /// </summary>
        private static string Fit(string text, LotSettings settings, string degreeSymbol)
        {
            if (!settings.UpperCase) return text;
            var upper = text.ToUpperInvariant();
            return string.IsNullOrEmpty(degreeSymbol)
                ? upper
                : upper.Replace(degreeSymbol.ToUpperInvariant(), degreeSymbol);
        }
    }
}
