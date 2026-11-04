using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using FieldCodes.Settings;
using FieldCodes.Styles;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace FieldCodes.Cad
{
    /// <summary>
    /// Puts Settings > Text Styles, Leader Styles and Style Assignments onto the MTEXT and
    /// MLEADER entities FTF draws. One per command run: it looks each drawing resource up
    /// once and keeps the problems it meets, so the command reports every missing style by
    /// name instead of drawing with something else and saying nothing.
    ///
    /// Every leader setting is a real MLEADER property; one left empty is not touched, so
    /// the multileader style the drawing (or the leader style) names decides it.
    ///
    /// UNTESTED in Civil 3D: compiled against the API stand-ins only.
    /// </summary>
    internal sealed class AnnotationStyler : IStyleResources
    {
        private readonly Database _db;
        private readonly Transaction _tr;
        private readonly FtfSettings _settings;
        private readonly RulesConfig _rules;
        private readonly HashSet<string> _reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ObjectId> _textStyles = new Dictionary<string, ObjectId>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ObjectId> _leaderStyles = new Dictionary<string, ObjectId>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ObjectId> _arrows = new Dictionary<string, ObjectId>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ObjectId> _linetypes = new Dictionary<string, ObjectId>(StringComparer.OrdinalIgnoreCase);

        public readonly List<string> Problems = new List<string>();

        /// <summary>Drawing units per plotted unit.</summary>
        public double Scale { get; private set; }

        public AnnotationStyler(Database db, Transaction tr, FtfSettings settings, RulesConfig rules)
        {
            _db = db;
            _tr = tr;
            _settings = settings ?? new FtfSettings();
            _rules = rules;
            Scale = CadUtil.DrawingUnitsPerPlottedUnit(db);
            foreach (var missing in StyleCheck.Missing(_settings, this)) Report(missing);
        }

        public ResolvedAnnotationStyle For(StyledKind kind, ParsedPoint point)
        {
            var r = StyleResolver.Resolve(_settings, StyleSubject.For(kind, point, _rules));
            foreach (var p in r.Problems) Report(p);
            return r;
        }

        public ResolvedAnnotationStyle For(StyleSubject subject)
        {
            var r = StyleResolver.Resolve(_settings, subject);
            foreach (var p in r.Problems) Report(p);
            return r;
        }

        public void Report(string problem)
        {
            if (!string.IsNullOrWhiteSpace(problem) && _reported.Add(problem)) Problems.Add(problem);
        }

        /// <summary>A resource missing when it is used: said once, and not again when the
        /// up-front check already named it (with where it is set).</summary>
        private void ReportMissing(string name, string problem)
        {
            var quoted = "\"" + name + "\"";
            if (Problems.Any(p => p.IndexOf(quoted, StringComparison.OrdinalIgnoreCase) >= 0 &&
                                  p.IndexOf("not ", StringComparison.OrdinalIgnoreCase) >= 0)) return;
            Report(problem);
        }

        /// <summary>Writes what was missing, once, under the command's name.</summary>
        public void WriteProblems(Editor ed, string command)
        {
            if (ed == null || Problems.Count == 0) return;
            ed.WriteMessage("\n{0}: {1} style problem(s) -- the labels concerned used the values shown " +
                            "in brackets; fix the setting or load the resource (Settings > Check Styles):", command, Problems.Count);
            foreach (var p in Problems) ed.WriteMessage("\n  " + p);
            ed.WriteMessage("\n");
        }

        // ------------------------------------------------------------------ sizes

        /// <summary>Model-space text height for this style.</summary>
        public double ModelHeight(ResolvedTextStyle t) { return t.HeightPlotted * Scale; }

        public double ModelOffset(ResolvedAnnotationStyle s) { return s.OffsetPlotted * Scale; }

        // ------------------------------------------------------------------ text

        /// <summary>A label's text, styled and in model space at <paramref name="at"/>, measured
        /// at model height so the placer sizes its box right; annotative styles are switched on
        /// by <see cref="Finish(MText, ResolvedTextStyle)"/> once it is placed.</summary>
        public MText NewText(string labelText, ResolvedTextStyle t, ObjectId layerId, Point3d at, double? symbolDegrees)
        {
            var text = new MText();
            text.SetDatabaseDefaults(_db);
            var styleId = TextStyleId(t.DrawingTextStyle);
            if (!styleId.IsNull) text.TextStyleId = styleId;
            text.Contents = StyleText.Compose(labelText, t);
            text.TextHeight = ModelHeight(t);
            text.Location = at;
            text.LayerId = layerId;
            text.Attachment = PlainAttachment(t.Justification);
            text.Rotation = Radians(StyleText.AngleDegrees(t, symbolDegrees));
            if (!string.IsNullOrWhiteSpace(t.Color)) Colour(text, t.Color);
            CadUtil.Mask(text, t.Mask == MaskMode.Background);
            CadUtil.AddToModelSpace(_db, _tr, text);
            return text;
        }

        /// <summary>Restyles an existing FTF label's text in place: it keeps its position and
        /// contents' wording (FTFRESTYLE).</summary>
        public void Restyle(MText text, string labelText, ResolvedTextStyle t, double? symbolDegrees, bool keepRotation)
        {
            // Where the text is now -- where FTF put it, or where someone moved it -- is kept.
            var at = text.Location;
            var styleId = TextStyleId(t.DrawingTextStyle);
            if (!styleId.IsNull) text.TextStyleId = styleId;
            if (labelText != null) text.Contents = StyleText.Compose(labelText, t);
            if (text.Annotative == AnnotativeStates.True) text.Annotative = AnnotativeStates.False;
            text.TextHeight = ModelHeight(t);
            if (!keepRotation) text.Rotation = Radians(StyleText.AngleDegrees(t, symbolDegrees));
            Colour(text, t.Color);
            CadUtil.Mask(text, t.Mask == MaskMode.Background);
            Finish(text, t);
            if (text.Location.DistanceTo(at) > 1e-9) text.Location = at;
        }

        /// <summary>Makes placed text annotative when its style says so.</summary>
        public void Finish(MText text, ResolvedTextStyle t)
        {
            if (!t.Annotative) return;
            try
            {
                text.TextHeight = t.HeightPlotted;
                text.Annotative = AnnotativeStates.True;
                if (_db.Cannoscale != null) text.AddContext(_db.Cannoscale);
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                Report("Annotative text could not be set on a label; it was drawn at the drawing's scale instead.");
            }
        }

        private static AttachmentPoint PlainAttachment(TextJustification j)
        {
            switch (j)
            {
                case TextJustification.Center: return AttachmentPoint.TopCenter;
                case TextJustification.Right: return AttachmentPoint.TopRight;
                default: return AttachmentPoint.TopLeft;
            }
        }

        // ------------------------------------------------------------------ leaders

        /// <summary>Text and leader as one multileader, the arrow on the point, in the style's
        /// look. The same geometry FTF has always drawn; only the look comes from the style.</summary>
        public MLeader NewLeadered(string labelText, ResolvedAnnotationStyle s, ObjectId layerId,
                                   Point3d textLocation, Point3d anchor, double? symbolDegrees)
        {
            var t = s.Text;
            var leaderStyleId = LeaderStyleId(s.Leader.MLeaderStyle);
            var styleId = TextStyleId(t.DrawingTextStyle);
            var annotative = t.Annotative || s.Leader.Annotative;
            var height = annotative ? t.HeightPlotted : ModelHeight(t);

            var leader = CadUtil.NewLeaderedLabel(_db, _tr, StyleText.Compose(labelText, t), height, styleId,
                                                  layerId, textLocation, anchor, leaderStyleId, t.Mask == MaskMode.Background);
            StyleLeader(leader, s, textLocation, anchor, symbolDegrees);
            return leader;
        }

        /// <summary>Applies a leader style to a multileader FTF drew (new, or FTFRESTYLE).</summary>
        public void StyleLeader(MLeader leader, ResolvedAnnotationStyle s, Point3d textLocation, Point3d anchor, double? symbolDegrees)
        {
            var l = s.Leader;
            var t = s.Text;

            var leaderStyleId = LeaderStyleId(l.MLeaderStyle);
            if (!leaderStyleId.IsNull) Try(() => leader.MLeaderStyle = leaderStyleId);

            if (!string.IsNullOrWhiteSpace(l.ArrowBlock))
            {
                var arrow = ArrowId(l.ArrowBlock);
                if (!arrow.IsNull) Try(() => leader.ArrowSymbolId = arrow);
            }

            if (l.LineType.HasValue)
                Try(() => leader.LeaderLineType = l.LineType.Value == LeaderLineKind.Spline ? LeaderType.SplineLeader : LeaderType.StraightLeader);

            if (!string.IsNullOrWhiteSpace(l.Color))
            {
                var c = ColourOf(l.Color);
                if (c != null) Try(() => leader.LeaderLineColor = c);
            }
            if (!string.IsNullOrWhiteSpace(l.Linetype))
            {
                var lt = LinetypeId(l.Linetype);
                if (!lt.IsNull) Try(() => leader.LeaderLineTypeId = lt);
            }
            if (!string.IsNullOrWhiteSpace(l.Lineweight))
            {
                var lw = FieldCodes.Settings.LayerAppearance.LineWeightHundredths(l.Lineweight);
                if (lw.HasValue) Try(() => leader.LeaderLineWeight = (LineWeight)lw.Value);
            }

            var annotative = t.Annotative || l.Annotative;
            var unit = annotative ? 1.0 : Scale;
            if (l.Landing.HasValue)
                Try(() => { leader.EnableLanding = l.Landing.Value; leader.EnableDogleg = l.Landing.Value; });
            if (l.DoglegPlotted.HasValue) Try(() => leader.DoglegLength = l.DoglegPlotted.Value * unit);
            if (l.LandingGapPlotted.HasValue) Try(() => leader.LandingGap = l.LandingGapPlotted.Value * unit);

            var attachment = Attachment(l.Attachment);
            Try(() =>
            {
                leader.TextAttachmentType = attachment;
                leader.SetTextAttachmentType(attachment, LeaderDirectionType.LeftLeader);
                leader.SetTextAttachmentType(attachment, LeaderDirectionType.RightLeader);
            });

            switch (t.Justification)
            {
                case TextJustification.Center: Try(() => leader.TextAlignmentType = TextAlignmentType.CenterAlignment); break;
                case TextJustification.Right: Try(() => leader.TextAlignmentType = TextAlignmentType.RightAlignment); break;
                default: Try(() => leader.TextAlignmentType = TextAlignmentType.LeftAlignment); break;
            }

            if (t.Rotation != TextRotation.Horizontal)
            {
                var degrees = StyleText.AngleDegrees(t, symbolDegrees);
                Try(() =>
                {
                    leader.TextAngleType = TextAngleType.InsertAngle;
                    var mt = leader.MText;
                    if (mt != null)
                    {
                        mt.Rotation = Radians(degrees);
                        leader.MText = mt;
                    }
                });
            }

            if (!string.IsNullOrWhiteSpace(t.Color))
            {
                var c = ColourOf(t.Color);
                if (c != null) Try(() => leader.TextColor = c);
            }

            if (annotative)
            {
                Try(() =>
                {
                    leader.Annotative = AnnotativeStates.True;
                    if (_db.Cannoscale != null) leader.AddContext(_db.Cannoscale);
                });
            }
            else if (l.SizeArrow && l.ArrowSizePlotted > 0)
            {
                // The same sizing FTF always did, now from the leader style when it sets one.
                var size = l.ArrowSizePlotted * Scale;
                Try(() =>
                {
                    // An annotative multileader style already sizes its arrow per scale.
                    if (leader.Annotative == AnnotativeStates.True) return;
                    var overall = leader.Scale;
                    leader.ArrowSize = overall > 0 ? size / overall : size;
                });
            }
        }

        /// <summary>Restyles an FTF multileader in place: style, look and text, never its
        /// position or its leader's vertices (FTFRESTYLE).</summary>
        public void Restyle(MLeader leader, string labelText, ResolvedAnnotationStyle s, double? symbolDegrees)
        {
            // What is on the sheet: the text where it is (FTF's place, or where someone moved it)
            // and each leader's arrow. A multileader re-seats its text against its landing when the
            // text height, attachment or landing changes, which moved a label a drafter had put
            // somewhere (the style smoke test at e979c7f: "the pole label moved by hand stayed where
            // it was put"). Both are put back after the look is applied; the landing follows the text.
            var textAt = leader.TextLocation;
            var arrows = ArrowPoints(leader);
            var t = s.Text;
            var annotative = t.Annotative || s.Leader.Annotative;
            Try(() =>
            {
                var mt = leader.MText;
                if (mt != null)
                {
                    var styleId = TextStyleId(t.DrawingTextStyle);
                    if (!styleId.IsNull) mt.TextStyleId = styleId;
                    if (labelText != null) mt.Contents = StyleText.Compose(labelText, t);
                    mt.TextHeight = annotative ? t.HeightPlotted : ModelHeight(t);
                    CadUtil.Mask(mt, t.Mask == MaskMode.Background);
                    leader.MText = mt;
                }
            });
            StyleLeader(leader, s, leader.TextLocation, leader.TextLocation, symbolDegrees);
            Try(() => { if (leader.TextLocation.DistanceTo(textAt) > 1e-9) leader.TextLocation = textAt; });
            foreach (var a in arrows)
            {
                var line = a.Key;
                var point = a.Value;
                Try(() => { if (leader.GetFirstVertex(line).DistanceTo(point) > 1e-9) leader.SetFirstVertex(line, point); });
            }
        }

        /// <summary>Each leader line's arrow end (its first vertex), by leader line index.</summary>
        private static List<KeyValuePair<int, Point3d>> ArrowPoints(MLeader leader)
        {
            var points = new List<KeyValuePair<int, Point3d>>();
            try
            {
                foreach (var index in leader.GetLeaderLineIndexes(0))
                {
                    var line = Convert.ToInt32(index, System.Globalization.CultureInfo.InvariantCulture);
                    points.Add(new KeyValuePair<int, Point3d>(line, leader.GetFirstVertex(line)));
                }
            }
            catch (Autodesk.AutoCAD.Runtime.Exception) { }
            return points;
        }

        private static TextAttachmentType Attachment(LeaderAttachment a)
        {
            switch (a)
            {
                case LeaderAttachment.TopOfTopLine: return TextAttachmentType.AttachmentTopOfTop;
                case LeaderAttachment.MiddleOfTopLine: return TextAttachmentType.AttachmentMiddleOfTop;
                case LeaderAttachment.BottomOfTopLine: return TextAttachmentType.AttachmentBottomOfTop;
                case LeaderAttachment.UnderlineTopLine: return TextAttachmentType.AttachmentBottomOfTopLine;
                case LeaderAttachment.MiddleOfBottomLine: return TextAttachmentType.AttachmentMiddleOfBottom;
                case LeaderAttachment.BottomOfBottomLine: return TextAttachmentType.AttachmentBottomOfBottom;
                case LeaderAttachment.UnderlineBottomLine: return TextAttachmentType.AttachmentBottomLine;
                case LeaderAttachment.UnderlineAllText: return TextAttachmentType.AttachmentAllLine;
                default: return TextAttachmentType.AttachmentMiddle;
            }
        }

        // ------------------------------------------------------------------ resources

        public ObjectId TextStyleId(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return ObjectId.Null;
            ObjectId id;
            if (_textStyles.TryGetValue(name.Trim(), out id)) return id;
            id = Setup.DrawingResources.FindTextStyle(_db, _tr, name.Trim());
            _textStyles[name.Trim()] = id;
            if (id.IsNull)
                ReportMissing(name.Trim(), "Text style \"" + name.Trim() + "\" is not in this drawing [the drawing's current text style].");
            return id;
        }

        public ObjectId LeaderStyleId(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return ObjectId.Null;
            ObjectId id;
            if (_leaderStyles.TryGetValue(name.Trim(), out id)) return id;
            id = UtilityCadService.LeaderStyle(_db, _tr, name.Trim());
            _leaderStyles[name.Trim()] = id;
            if (id.IsNull)
                ReportMissing(name.Trim(), "Multileader style \"" + name.Trim() + "\" is not in this drawing [the current multileader style].");
            return id;
        }

        public ObjectId LinetypeId(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return ObjectId.Null;
            ObjectId id;
            if (_linetypes.TryGetValue(name.Trim(), out id)) return id;
            var table = (LinetypeTable)_tr.GetObject(_db.LinetypeTableId, OpenMode.ForRead);
            id = table.Has(name.Trim()) ? table[name.Trim()] : ObjectId.Null;
            _linetypes[name.Trim()] = id;
            if (id.IsNull)
                ReportMissing(name.Trim(), "Linetype \"" + name.Trim() + "\" is not loaded in this drawing [the multileader style's leader linetype].");
            return id;
        }

        /// <summary>An arrowhead block. AutoCAD's own arrows (_Dot, _Open30...) are loaded on
        /// first use the way AutoCAD loads them, through DIMBLK, which is put back after.</summary>
        public ObjectId ArrowId(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return ObjectId.Null;
            var key = name.Trim();
            ObjectId id;
            if (_arrows.TryGetValue(key, out id)) return id;

            var blocks = (BlockTable)_tr.GetObject(_db.BlockTableId, OpenMode.ForRead);
            if (!blocks.Has(key) && StyleCheck.IsBuiltInArrow(key) && _db == HostApplicationServices.WorkingDatabase)
            {
                object old = null;
                try
                {
                    old = AcadApp.GetSystemVariable("DIMBLK");
                    AcadApp.SetSystemVariable("DIMBLK", key);
                }
                catch (System.Exception) { }
                finally
                {
                    try
                    {
                        var previous = old as string;
                        AcadApp.SetSystemVariable("DIMBLK", string.IsNullOrEmpty(previous) ? "." : previous);
                    }
                    catch (System.Exception) { }
                }
                blocks = (BlockTable)_tr.GetObject(_db.BlockTableId, OpenMode.ForRead);
            }
            id = blocks.Has(key) ? blocks[key] : ObjectId.Null;
            _arrows[key] = id;
            if (id.IsNull)
                ReportMissing(key, "Arrowhead \"" + key + "\" is not in this drawing [the multileader style's arrow].");
            return id;
        }

        // IStyleResources, for the up-front check.
        public bool HasTextStyle(string name) { return !Setup.DrawingResources.FindTextStyle(_db, _tr, name).IsNull; }
        public bool HasMLeaderStyle(string name) { return !UtilityCadService.LeaderStyle(_db, _tr, name).IsNull; }
        public bool HasBlock(string name)
        {
            var blocks = (BlockTable)_tr.GetObject(_db.BlockTableId, OpenMode.ForRead);
            return blocks.Has(name);
        }
        public bool HasLinetype(string name)
        {
            var table = (LinetypeTable)_tr.GetObject(_db.LinetypeTableId, OpenMode.ForRead);
            return table.Has(name);
        }

        // ------------------------------------------------------------------ helpers

        private static Autodesk.AutoCAD.Colors.Color ColourOf(string setting)
        {
            var index = StyleColor.Index(setting);
            if (!index.HasValue) return null;
            if (index.Value == 256) return Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByLayer, 256);
            if (index.Value == 0) return Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByBlock, 0);
            return Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, index.Value);
        }

        /// <summary>What the stamp records: "text style|leader style|mask".</summary>
        public static string StyleKey(ResolvedAnnotationStyle s)
        {
            if (s == null) return string.Empty;
            return (s.Text.Name ?? string.Empty).Replace("|", "/") + "|" +
                   (s.Kind == StyledKind.LineLabel ? string.Empty : (s.Leader.Name ?? string.Empty).Replace("|", "/")) + "|" +
                   s.Text.Mask.ToString().ToLowerInvariant();
        }

        /// <summary>Blank is ByLayer.</summary>
        private static void Colour(Autodesk.AutoCAD.DatabaseServices.Entity e, string setting)
        {
            var c = string.IsNullOrWhiteSpace(setting)
                ? Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByLayer, 256)
                : ColourOf(setting);
            if (c != null) e.Color = c;
        }

        private static double Radians(double degrees) { return degrees * Math.PI / 180.0; }

        /// <summary>A property a particular multileader refuses keeps its style's value.</summary>
        private void Try(Action set)
        {
            try { set(); }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                Report("A multileader refused a style setting (" + ex.Message + "); it keeps its multileader style's value.");
            }
        }
    }
}
