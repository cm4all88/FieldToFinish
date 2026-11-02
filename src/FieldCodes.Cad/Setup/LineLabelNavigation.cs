using System;
using System.Collections.Generic;

namespace FieldCodes.Cad.Setup
{
    /// <summary>
    /// Hand-off from the Lines review page to Settings &gt; Line Labels. The Lines
    /// page sets the line feature the person selected, then navigates; the Line
    /// Labels page reads it when it builds or becomes visible, focuses that
    /// feature's label configuration, and clears it.
    ///
    /// Only state: no UI, no drawing access. Set before navigating, so a page that
    /// is built lazily on first visit still finds it.
    /// </summary>
    public static class LineLabelNavigation
    {
        /// <summary>The line feature code to show ("FNC"). For a line on a shared
        /// layer, the first candidate; null for a line that is not a configured line
        /// feature (then <see cref="PendingLayer"/> says what it was).</summary>
        public static string PendingFeature { get; set; }

        /// <summary>Every candidate code when the line was identified by a shared
        /// layer ("FNC", "FHW"); otherwise the one feature. Never null.</summary>
        public static IList<string> PendingCandidates { get; set; }

        /// <summary>The survey figure or feature line name ("FNC2"), when it has one.</summary>
        public static string PendingFigure { get; set; }

        /// <summary>The line's own layer, for a new line feature's layer.</summary>
        public static string PendingLayer { get; set; }

        /// <summary>Raised after every request, so a Line Labels page that is already
        /// built can react without being rebuilt.</summary>
        public static event EventHandler Requested;

        static LineLabelNavigation()
        {
            PendingCandidates = new List<string>();
        }

        /// <summary>Records a request and raises <see cref="Requested"/>.</summary>
        public static void Request(string feature, IList<string> candidates, string figure, string layer)
        {
            PendingFeature = string.IsNullOrWhiteSpace(feature) ? null : feature.Trim();
            PendingCandidates = candidates != null ? new List<string>(candidates) : new List<string>();
            PendingFigure = string.IsNullOrWhiteSpace(figure) ? null : figure.Trim();
            PendingLayer = string.IsNullOrWhiteSpace(layer) ? null : layer.Trim();

            var handler = Requested;
            if (handler != null) handler(null, EventArgs.Empty);
        }

        /// <summary>True when a request is waiting.</summary>
        public static bool HasPending
        {
            get { return PendingFeature != null || PendingLayer != null; }
        }

        /// <summary>Forgets the request once the page has acted on it.</summary>
        public static void Clear()
        {
            PendingFeature = null;
            PendingCandidates = new List<string>();
            PendingFigure = null;
            PendingLayer = null;
        }
    }
}
