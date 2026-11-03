using System;
using System.Collections.Generic;
using System.Linq;
using FieldCodes.Settings;

namespace FieldCodes.Review
{
    /// <summary>One kind of review item: what it means, what to do, and whether it must be
    /// closed before Drafting QC Complete unless Settings > Review and QC says otherwise.</summary>
    public sealed class ReviewKind
    {
        public string Id { get; private set; }
        public string Source { get; private set; }
        public string FeatureType { get; private set; }
        public string Title { get; private set; }
        public string Action { get; private set; }
        public string FixCommand { get; private set; }
        public string SettingsPage { get; private set; }
        public ReviewSeverity Severity { get; private set; }
        public bool Required { get; private set; }

        public ReviewKind(string id, string source, string feature, string title, string action,
                          string fix, string settingsPage, ReviewSeverity severity, bool required)
        {
            Id = id; Source = source; FeatureType = feature; Title = title; Action = action;
            FixCommand = fix; SettingsPage = settingsPage; Severity = severity; Required = required;
        }
    }

    /// <summary>
    /// Every kind of review item FTF raises. Codes FTF ignores on purpose (ignore list, codes
    /// marked ignored, normal Civil 3D linework codes) are never review items; only things a
    /// drafter has to decide are.
    /// </summary>
    public static class ReviewKinds
    {
        public const string CodeUnknown = "code.unknown";
        public const string CodeUnreadable = "code.unreadable";
        public const string CodeInvalid = "code.invalid";
        public const string CodeModifier = "code.modifier";
        public const string LineUnidentified = "line.unidentified";
        public const string LabelFailed = "label.failed";
        public const string FenceHeight = "fence.height";
        public const string GateUnpaired = "gate.unpaired";
        public const string GateAmbiguous = "gate.ambiguous";
        public const string GateNarrow = "gate.narrow";
        public const string GateWide = "gate.wide";
        public const string GateNoFence = "gate.nofence";
        public const string GateWidthDisagrees = "gate.widthdisagrees";
        public const string OrientReview = "orient.review";
        public const string MarkingDirection = "orient.marking";
        public const string SettingMissing = "settings.missing";
        public const string CommandError = "error.command";

        public static readonly IList<ReviewKind> All = new List<ReviewKind>
        {
            new ReviewKind(CodeUnknown, "Codes", "Point", "Code nothing handles",
                "Add a rule for the code (Point Features), or mark it ignored if it should draw nothing.",
                null, "Point Features", ReviewSeverity.Warning, true),
            new ReviewKind(CodeUnreadable, "Codes", "Point", "Description missing or unreadable",
                "Correct the point's description in Civil 3D, then recheck.",
                null, null, ReviewSeverity.Error, true),
            new ReviewKind(CodeInvalid, "Codes", "Point", "Value FTF cannot use",
                "Correct the value in the point's description (a trunk size, a number), then recheck.",
                null, "Point Features", ReviewSeverity.Error, true),
            new ReviewKind(CodeModifier, "Codes", "Point", "Linework modifier not recognised",
                "Check the line code after the feature code; add it to the linework codes if it is the office's.",
                null, "Point Features", ReviewSeverity.Warning, true),
            new ReviewKind(LineUnidentified, "Line labels", "Line", "Line not identified",
                "Label it with FTFLABELLINE (you choose the wording), or give the feature a label on Line Labels.",
                "FTFLABELLINE", "Line Labels", ReviewSeverity.Warning, false),
            new ReviewKind(LabelFailed, "Labels", "Label", "Label could not be placed",
                "Place the label by hand, or fix what the reason names and rerun.",
                null, null, ReviewSeverity.Error, true),
            new ReviewKind(FenceHeight, "Fence heights", "Fence", "Fence height not readable",
                "Correct the height in the shot's description (FNC B 3.5), or ignore it with a reason.",
                "FTFLABELLINE", "Line Labels", ReviewSeverity.Warning, true),
            new ReviewKind(GateUnpaired, "Gates", "Gate", "Gate post has no partner",
                "Pair it with FTFGATE (pick both posts), or mark it not applicable (a lone post, a removed gate).",
                "FTFGATE", "Gates", ReviewSeverity.Warning, true),
            new ReviewKind(GateAmbiguous, "Gates", "Gate", "Gate posts could pair more than one way",
                "Pick the right two posts with FTFGATE.",
                "FTFGATE", "Gates", ReviewSeverity.Warning, true),
            new ReviewKind(GateNarrow, "Gates", "Gate", "Gate narrower than the smallest believed",
                "Check the posts; label it with FTFGATE if it is a gate, or set a narrow-gate label on Gates.",
                "FTFGATE", "Gates", ReviewSeverity.Warning, true),
            new ReviewKind(GateWide, "Gates", "Gate", "Gate wider than the largest believed",
                "Check the posts: two gates, or posts from different gates? Label with FTFGATE when confirmed.",
                "FTFGATE", "Gates", ReviewSeverity.Warning, true),
            new ReviewKind(GateNoFence, "Gates", "Gate", "Gate posts away from any fence",
                "Confirm the pair with FTFGATE, or mark it not applicable.",
                "FTFGATE", "Gates", ReviewSeverity.Warning, true),
            new ReviewKind(GateWidthDisagrees, "Gates", "Gate", "Recorded gate width differs from the posts",
                "Check which is right; FTFGATE places the label with the width you choose.",
                "FTFGATE", "Gates", ReviewSeverity.Warning, true),
            new ReviewKind(OrientReview, "Orientation", "Symbol", "Symbol rotation needs review",
                "Rotate it by hand (FTFORIENT keeps a hand rotation), or fix the direction words and rerun.",
                "FTFORIENT", "Symbols and Rotations", ReviewSeverity.Warning, true),
            new ReviewKind(MarkingDirection, "Orientation", "Pavement marking", "Marking direction not decided",
                "Write the direction of travel on the shot (ASTO N), or rotate the arrow by hand.",
                "FTFORIENT", "Symbols and Rotations", ReviewSeverity.Warning, true),
            new ReviewKind(SettingMissing, "Settings", "Setting", "Setting or style missing",
                "Load the named style into the drawing, or choose another in Settings.",
                "FTFCHECKSTANDARDS", null, ReviewSeverity.Warning, false),
            new ReviewKind(CommandError, "Commands", "Command", "Command did not finish",
                "Read the reason, fix it and rerun the command.",
                null, null, ReviewSeverity.Error, true),
        }.AsReadOnly();

        public static ReviewKind Get(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            return All.FirstOrDefault(k => string.Equals(k.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Fills what the producer left blank from the kind, and decides Required
        /// from the settings (or the kind's default).</summary>
        public static void Fill(ReviewItem item, ReviewSettings settings)
        {
            if (item == null) return;
            var kind = Get(item.Kind);
            if (kind != null)
            {
                if (string.IsNullOrWhiteSpace(item.Source)) item.Source = kind.Source;
                if (string.IsNullOrWhiteSpace(item.FeatureType)) item.FeatureType = kind.FeatureType;
                if (string.IsNullOrWhiteSpace(item.Issue)) item.Issue = kind.Title;
                if (string.IsNullOrWhiteSpace(item.SuggestedAction)) item.SuggestedAction = kind.Action;
                if (string.IsNullOrWhiteSpace(item.FixCommand)) item.FixCommand = kind.FixCommand;
                if (string.IsNullOrWhiteSpace(item.SettingsPage)) item.SettingsPage = kind.SettingsPage;
            }
            item.Required = settings != null ? settings.IsRequired(item.Kind) : kind != null && kind.Required;
            if (kind != null && item.Severity < kind.Severity) item.Severity = kind.Severity;
        }

        /// <summary>A review item of a kind, its id made from the kind and the subject.</summary>
        public static ReviewItem New(string kind, string subject, string reason)
        {
            var item = new ReviewItem
            {
                Kind = kind,
                Id = kind + ":" + (subject ?? string.Empty).Trim(),
                Reason = reason
            };
            Fill(item, null);
            return item;
        }
    }
}
