using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FieldCodes.Settings;

namespace FieldCodes.Utilities
{
    /// <summary>
    /// One pipe as the drafter records it from the field book in the Dip Builder's Add pipe panel: direction, size,
    /// material, measure down and what it was measured to. Values are kept exactly as entered -- a 17.5" pipe stays
    /// 17.5", an unlisted material stays as typed -- and a measure down with no stated reference stays Unspecified.
    /// The office convention for unmarked dips applies to field-note text only, never to this panel.
    /// </summary>
    public sealed class QuickPipeEntry
    {
        public ObservedDirection Direction { get; set; }
        public double? SizeIn { get; set; }
        public string Material { get; set; }
        public double? MeasuredDip { get; set; }
        public MeasurementReference Reference { get; set; }

        public const string DirectionField = "direction";
        public const string SizeField = "size";
        public const string MaterialField = "material";

        /// <summary>Which of direction / size / material still carry values copied from the connected pipe.</summary>
        public List<string> Prefilled { get; set; }

        /// <summary>The pipe they were copied from, in words.</summary>
        public string PrefilledFrom { get; set; }

        public QuickPipeEntry() { Prefilled = new List<string>(); }

        /// <summary>A new observation, entered by the drafter.</summary>
        public PipeObservation Create()
        {
            var pipe = new PipeObservation { Source = ObservationSource.UserEntry };
            ApplyTo(pipe);
            pipe.Reference = Reference;
            pipe.ReferenceBasis = Reference == MeasurementReference.Unspecified ? ReferenceBasis.NotStated : ReferenceBasis.EnteredByDrafter;
            pipe.Prefilled = (Prefilled ?? new List<string>()).Distinct().ToList();
            pipe.PrefilledFrom = pipe.Prefilled.Count > 0 ? PrefilledFrom : null;
            return pipe;
        }

        /// <summary>
        /// The start of the other end of a connected pipe: the opposite direction, the size and the material, copied
        /// and marked as copied. The measure down and what it was measured to are never copied -- they are this
        /// structure's own observation and start empty (Unspecified).
        /// </summary>
        public static QuickPipeEntry ForOtherEnd(PipeObservation source, string sourceLabel)
        {
            var entry = new QuickPipeEntry { Reference = MeasurementReference.Unspecified };
            if (source == null) return entry;
            entry.Direction = DirectionShortcuts.Opposite(source.Direction);
            if (entry.Direction != null) entry.Prefilled.Add(DirectionField);
            entry.SizeIn = source.WidthIn;
            if (source.WidthIn.HasValue) entry.Prefilled.Add(SizeField);
            entry.Material = source.Material;
            if (!string.IsNullOrEmpty(source.Material)) entry.Prefilled.Add(MaterialField);
            entry.PrefilledFrom = (sourceLabel ?? "the connected structure") + ": " + ConnectionFinder.Describe(source);
            return entry;
        }

        /// <summary>Copies the measured values (not the reference) onto an observation.</summary>
        public void ApplyTo(PipeObservation pipe)
        {
            pipe.Direction = Direction ?? ObservedDirection.Unknown("?");
            pipe.WidthIn = SizeIn;
            // Round pipe: the rise is the size. A non-round pipe keeps its observed rise.
            if (pipe.Shape == PipeShape.Round || !pipe.HeightIn.HasValue) pipe.HeightIn = SizeIn;
            var material = (Material ?? string.Empty).Trim().ToUpperInvariant();
            pipe.Material = material.Length == 0 ? null : material;
            pipe.MeasuredDip = MeasuredDip;
        }

        /// <summary>The entry an existing observation would show in the panel.</summary>
        public static QuickPipeEntry From(PipeObservation pipe)
        {
            return new QuickPipeEntry
            {
                Direction = pipe.Direction,
                SizeIn = pipe.WidthIn,
                Material = pipe.Material,
                MeasuredDip = pipe.MeasuredDip,
                Reference = pipe.Reference,
                Prefilled = (pipe.Prefilled ?? new List<string>()).ToList(),
                PrefilledFrom = pipe.PrefilledFrom
            };
        }

        /// <summary>
        /// The observation in one line, as the pipe card shows it: N/NW 12" RCP IE 6.41, or N/NW 12" RCP 6.41 Unspecified
        /// when the dip does not say what it was measured to.
        /// </summary>
        public static string Summary(PipeObservation pipe, UtilitySettings settings)
        {
            settings = settings ?? new UtilitySettings();
            var parts = new List<string>();
            parts.Add(pipe.Direction != null && !string.IsNullOrEmpty(pipe.Direction.Text) ? pipe.Direction.Text : "?");
            parts.Add(pipe.WidthIn.HasValue ? SizeWords(pipe) : "size ?");
            if (!string.IsNullOrEmpty(pipe.Material)) parts.Add(pipe.Material);
            if (!pipe.MeasuredDip.HasValue) parts.Add("not dipped");
            else
            {
                var md = Exact(pipe.MeasuredDip.Value);
                if (pipe.Reference == MeasurementReference.Unspecified) { parts.Add(md); parts.Add("Unspecified"); }
                else { parts.Add(Prefix(pipe.Reference, settings)); parts.Add(md); }
            }
            return string.Join(" ", parts.ToArray());
        }

        /// <summary>A number with every decimal it was entered with: 17.5, 6.415, 8. Reads back as the same value.</summary>
        public static string Exact(double value)
        {
            var r = value.ToString("R", CultureInfo.InvariantCulture);
            return r.IndexOf('E') < 0 ? r : value.ToString("0.###############", CultureInfo.InvariantCulture);
        }

        private static string SizeWords(PipeObservation pipe)
        {
            var w = UtilityLabelFormatter.SizeNumber(pipe.WidthIn.Value) + "\"";
            if (pipe.Shape != PipeShape.Round && pipe.HeightIn.HasValue && Math.Abs(pipe.HeightIn.Value - pipe.WidthIn.Value) > 1e-9)
                w += "x" + UtilityLabelFormatter.SizeNumber(pipe.HeightIn.Value) + "\"";
            return w;
        }

        private static string Prefix(MeasurementReference reference, UtilitySettings settings)
        {
            switch (reference)
            {
                case MeasurementReference.Invert: return string.IsNullOrWhiteSpace(settings.PrefixInvert) ? "IE" : settings.PrefixInvert;
                case MeasurementReference.TopOfPipe: return string.IsNullOrWhiteSpace(settings.PrefixTop) ? "TOP" : settings.PrefixTop;
                case MeasurementReference.Springline: return string.IsNullOrWhiteSpace(settings.PrefixSpringline) ? "SPR" : settings.PrefixSpringline;
                default: return DipElevations.Describe(reference).ToUpperInvariant();
            }
        }
    }
}
