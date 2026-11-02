using System;

namespace FieldCodes.Cad.Setup
{
    /// <summary>
    /// Hand-off from the review pages to Settings &gt; Point Features. The Unknown
    /// Codes page sets what the person chose, then navigates; the Point Features
    /// page reads it when it builds or becomes visible, prefills a new rule for the
    /// code (or selects the rule a failing point matched), and clears it.
    ///
    /// Only state: no UI, no drawing access. Set before navigating, so a page that
    /// is built lazily on first visit still finds it.
    /// </summary>
    public static class PointFeatureNavigation
    {
        /// <summary>The field code to configure ("XYZ"), already stripped of sequence
        /// digits; null when nothing is pending.</summary>
        public static string PendingCode { get; set; }

        /// <summary>A real description carrying the code ("XYZ 12 OAK"), for the new
        /// rule's test input. May be null.</summary>
        public static string PendingSample { get; set; }

        /// <summary>The rule a failing point matched (an error rather than a missing
        /// rule), to select instead of creating one. May be null.</summary>
        public static string PendingRuleId { get; set; }

        /// <summary>Raised after every request, so a Point Features page that is
        /// already built can react without being rebuilt.</summary>
        public static event EventHandler Requested;

        /// <summary>Records a request and raises <see cref="Requested"/>.</summary>
        public static void Request(string code, string sample, string ruleId)
        {
            PendingCode = string.IsNullOrWhiteSpace(code) ? null : code.Trim();
            PendingSample = string.IsNullOrWhiteSpace(sample) ? null : sample.Trim();
            PendingRuleId = string.IsNullOrWhiteSpace(ruleId) ? null : ruleId.Trim();

            var handler = Requested;
            if (handler != null) handler(null, EventArgs.Empty);
        }

        /// <summary>True when a request is waiting.</summary>
        public static bool HasPending
        {
            get { return PendingCode != null || PendingRuleId != null; }
        }

        /// <summary>Forgets the request once the page has acted on it.</summary>
        public static void Clear()
        {
            PendingCode = null;
            PendingSample = null;
            PendingRuleId = null;
        }
    }
}
