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

        /// <summary>Accepting it needs a note: point accountability and linework continuity
        /// items are closed only with a documented disposition.</summary>
        public bool NeedsNote { get; private set; }

        public ReviewKind(string id, string source, string feature, string title, string action,
                          string fix, string settingsPage, ReviewSeverity severity, bool required,
                          bool needsNote = false)
        {
            Id = id; Source = source; FeatureType = feature; Title = title; Action = action;
            FixCommand = fix; SettingsPage = settingsPage; Severity = severity; Required = required;
            NeedsNote = needsNote;
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

        // Point accountability (Points). Id = kind + point number.
        public const string PointMissingOutput = "point.missingoutput";
        public const string PointMissingLinework = "point.missinglinework";
        public const string PointNoSymbol = "point.nosymbol";
        public const string PointDuplicate = "point.duplicate";
        public const string PointExcludedNoReason = "point.excludednoreason";
        public const string PointDeleted = "point.deleted";
        public const string PointSourceChanged = "point.sourcechanged";
        /// <summary>The standard asks for every point of this feature to be reviewed.</summary>
        public const string PointReview = "point.review";
        /// <summary>Surface membership check (optional): in a surface's definition but left out of
        /// its triangulation. Id = kind + point number.</summary>
        public const string PointSurfaceExcluded = "point.surfaceexcluded";
        /// <summary>Surface membership check (optional): surface points no surface can be shown to
        /// hold. One per code; Points lists them.</summary>
        public const string PointSurfaceUnverified = "point.surfaceunverified";
        /// <summary>The standard requires a symbol; the point shows a marker, but FTF could not read
        /// enough of its point style to confirm it is that symbol. Not the same as a missing symbol
        /// (point.nosymbol). One per code; Points lists them.</summary>
        public const string PointSymbolUnverified = "point.symbolunverified";
        /// <summary>The standard lets the point's own Civil 3D label stand in for a label, and FTF
        /// could not read whether that label is shown. Not the same as a missing label
        /// (point.missingoutput). One per code; Points lists them.</summary>
        public const string PointLabelUnverified = "point.labelunverified";

        /// <summary>A required symbol or label FTF could not confirm, as against one it found missing.</summary>
        public static bool IsUnverified(string kind)
        {
            return string.Equals(kind, PointSymbolUnverified, System.StringComparison.Ordinal) ||
                   string.Equals(kind, PointLabelUnverified, System.StringComparison.Ordinal);
        }

        // Linework continuity. Ids are made from the shots and line ends they are about.
        public const string LineGap = "line.gap";
        public const string LineMissingVertex = "line.missingvertex";
        public const string LineMissingSegment = "line.missingsegment";

        public const string PointsSource = "Points";
        public const string ContinuitySource = "Linework continuity";

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

            new ReviewKind(PointMissingOutput, PointsSource, "Point", "Required output missing",
                "Draw what is missing (rerun FTFRUN, or place it by hand), change the feature's standard (Settings > Point Features) if it does not need it, or mark it not applicable with the reason.",
                "FTFRUN", "Point Features", ReviewSeverity.Error, true, true),
            new ReviewKind(PointMissingLinework, PointsSource, "Point", "Linework shot on no line of its code",
                "Draw the line through the shot, approve the proposed correction, or mark it not applicable with the reason.",
                null, "Point Accountability", ReviewSeverity.Warning, true, true),
            new ReviewKind(PointNoSymbol, PointsSource, "Point", "Required symbol missing",
                "The feature's standard (Settings > Point Features) requires a symbol the drawing does not show: give the point a style whose marker is that symbol (description keys), add a rule that draws it, change the standard if it is wrong, or mark it not applicable with the reason.",
                null, "Point Features", ReviewSeverity.Error, true, true),
            // A possible deficiency: listed for a look, not a missing deliverable.
            new ReviewKind(PointDuplicate, PointsSource, "Point", "Another point at the same position",
                "Check both shots: delete the duplicate in Civil 3D, or mark it not applicable (two features at one spot).",
                null, "Point Accountability", ReviewSeverity.Warning, false, true),
            new ReviewKind(PointExcludedNoReason, PointsSource, "Point", "Excluded without a documented reason",
                "Give the ignore entry a reason (rules.json ignoreCodes, or Unknown Codes > Ignore), give the code a drafting standard (Point Features), or mark the point not applicable with one.",
                null, "Point Accountability", ReviewSeverity.Warning, false, true),
            new ReviewKind(PointReview, PointsSource, "Point", "Feature the standard asks to be reviewed",
                "Look at the point: the standard for its feature (Settings > Point Features) asks for every one to be reviewed. Accept it with a note, or mark it not applicable.",
                null, "Point Features", ReviewSeverity.Warning, true, true),
            new ReviewKind(PointSurfaceExcluded, PointsSource, "Point", "Surface point left out of the surface",
                "The point is in the surface's definition but its triangulation leaves it out (a boundary, an edge limit, a duplicate elevation). Check the surface if it should be in; otherwise nothing to draft.",
                null, "Point Accountability", ReviewSeverity.Warning, false, true),
            new ReviewKind(PointSurfaceUnverified, PointsSource, "Point", "Surface membership not verifiable",
                "Information: these surface points are accounted for as surface data; no surface in the drawing can be shown to hold them (none built, or not from point groups). Nothing to draft.",
                null, "Point Accountability", ReviewSeverity.Info, false),
            // Present but not confirmed: a person checks the drawing once per code and accepts it with a
            // note, or fixes the style. Required, so it is never passed by silence; never called missing.
            new ReviewKind(PointSymbolUnverified, PointsSource, "Point", "Required symbol not verified",
                "The points show a marker, but FTF could not read enough of their point style to confirm it is the symbol their standard requires (the reason says what could not be read). Look at them: accept with a note when the symbol is right, or give them the right style. Nothing is missing until a check shows it is.",
                null, "Point Features", ReviewSeverity.Warning, true, true),
            new ReviewKind(PointLabelUnverified, PointsSource, "Point", "Required label not verified",
                "Their standard lets the point's own Civil 3D label show the description, but FTF could not read whether that label is shown (the reason says why). Look at them: accept with a note when the label is on the plan, or label them (FTFLABELS).",
                null, "Point Features", ReviewSeverity.Warning, true, true),
            new ReviewKind(PointDeleted, PointsSource, "Point", "Point gone since it was first recorded",
                "Restore the point if it was deleted by mistake, or mark it not applicable with the reason it was removed.",
                null, "Point Accountability", ReviewSeverity.Error, true, true),
            new ReviewKind(PointSourceChanged, PointsSource, "Point", "Point changed since it was last checked",
                "Information: its raw description or position changed in Civil 3D. Decisions about it were reopened.",
                null, "Point Accountability", ReviewSeverity.Info, false),

            new ReviewKind(LineGap, ContinuitySource, "Line", "Possible missing connection",
                "Look at the proposal (dashed, on the QC layer): approve the correction, or mark it not applicable with the reason (a gate, a driveway, the fence stops).",
                null, "Linework Continuity", ReviewSeverity.Warning, true, true),
            new ReviewKind(LineMissingVertex, ContinuitySource, "Line", "Line skips a coded shot",
                "The line runs past a shot coded onto it: approve the proposed path, redraw the line, or mark it not applicable with the reason.",
                null, "Linework Continuity", ReviewSeverity.Warning, true, true),
            new ReviewKind(LineMissingSegment, ContinuitySource, "Line", "Coded linework not drawn",
                "Shots coded as one string have no line between them: approve the proposed line, draw it, or mark it not applicable with the reason.",
                null, "Linework Continuity", ReviewSeverity.Warning, true, true),
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
