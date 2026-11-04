using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using FieldCodes.Annotation;
using FieldCodes.Settings;

using AcEntity = Autodesk.AutoCAD.DatabaseServices.Entity;

namespace FieldCodes.Cad
{
    /// <summary>
    /// One read of the drawing for the label and tag stages and their preview: every point
    /// parsed as it will be labelled (dip notes kept out when the settings say so), its family,
    /// which points FTF already labelled or tagged, which labels were moved by hand, and the
    /// text people typed themselves. FTFLABELS, FTFTAGS, FTFLABELALL's preview and the Process
    /// preview all decide from this, through <see cref="AnnotationPolicy"/>.
    /// </summary>
    internal sealed class AnnotationScan
    {
        public readonly Dictionary<string, ParsedPoint> Points = new Dictionary<string, ParsedPoint>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, Point3d> Anchors = new Dictionary<string, Point3d>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Each point's handle, for review items that zoom to it.</summary>
        public readonly Dictionary<string, string> Handles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public readonly List<string> Order = new List<string>();
        public readonly HashSet<string> FtfLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<string> FtfTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Points whose own Civil 3D label is shown (a feature the standard labels by its
        /// point label gets no FTF label then).</summary>
        public readonly HashSet<string> PointLabelShown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> KeptByHand = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public ManualAnnotationIndex Manual;
        public double ManualRadius;
        public int DepthNotesKeptOut;

        private RulesConfig _rules;

        public static AnnotationScan Read(Database db, Transaction tr, RulesConfig rules, FtfSettings settings,
                                          FieldCodeParser parser, CogoNotes notes)
        {
            var scan = new AnnotationScan { _rules = rules };
            var ann = settings.Annotation ?? new AnnotationSettings();
            foreach (var cogo in CadUtil.CogoPointsInOrder(db, tr))
            {
                var number = cogo.PointNumber.ToString(CultureInfo.InvariantCulture);
                bool keptOut;
                var parsed = notes.ParseForLabel(parser, cogo, ann, out keptOut);
                if (keptOut) scan.DepthNotesKeptOut++;
                scan.Points[number] = parsed;
                scan.Anchors[number] = CadUtil.Flatten(cogo.Location);
                scan.Handles[number] = cogo.Handle.ToString();
                scan.Order.Add(number);
                string labelStyle;
                if (Compare.CogoPointPlotting.LabelShown(cogo, db, tr, out labelStyle) == true) scan.PointLabelShown.Add(number);
            }
            foreach (var pair in Ownership.FindOwned(db, tr, s => s.Kind == FtfEntityKind.Label || s.Kind == FtfEntityKind.Tag))
            {
                if (string.IsNullOrWhiteSpace(pair.Value.PointNumber)) continue;
                (pair.Value.Kind == FtfEntityKind.Tag ? scan.FtfTags : scan.FtfLabels).Add(pair.Value.PointNumber);
            }

            var scale = CadUtil.DrawingUnitsPerPlottedUnit(db);
            scan.ManualRadius = Math.Max(0, ann.ExistingLabelSearchPlotted) * scale;
            scan.Manual = new ManualAnnotationIndex(ManualText(db, tr), Math.Max(scan.ManualRadius, 1e-6));
            return scan;
        }

        /// <summary>The family a point is in; a stamp whose point is gone counts as in every
        /// scope, so its orphaned label is cleared by whichever run comes next.</summary>
        public bool InScope(string pointNumber, ICollection<AnnotationCategory> scope)
        {
            ParsedPoint p;
            if (!Points.TryGetValue(pointNumber ?? string.Empty, out p)) return true;
            return scope != null && scope.Contains(AnnotationPolicy.CategoryOf(p, _rules));
        }

        public bool HasHandLabel(string pointNumber, string labelText)
        {
            Point3d at;
            if (Manual == null || ManualRadius <= 0 || !Anchors.TryGetValue(pointNumber, out at)) return false;
            return Manual.HasMatch(labelText, at.X, at.Y, ManualRadius);
        }

        public AnnotationPlan Plan(AnnotationSettings settings, ICollection<AnnotationCategory> labelScope,
                                   ICollection<AnnotationCategory> tagScope, bool includeTags)
        {
            var candidates = Order.Select(n => new AnnotationCandidate
            {
                Parsed = Points[n],
                HasFtfLabel = FtfLabels.Contains(n),
                HasFtfTag = FtfTags.Contains(n),
                LabelMovedByHand = KeptByHand.Contains(n),
                ManualLabelNearby = !string.IsNullOrEmpty(Points[n].LabelText) && HasHandLabel(n, Points[n].LabelText),
                PointLabelShown = PointLabelShown.Contains(n)
            });
            return AnnotationPlan.Build(candidates, settings, labelScope, tagScope, includeTags, _rules);
        }

        /// <summary>Text in model space that FTF did not draw: what someone typed.</summary>
        private static IEnumerable<ExistingAnnotation> ManualText(Database db, Transaction tr)
        {
            var found = new List<ExistingAnnotation>();
            var ms = CadUtil.ModelSpace(db, tr, OpenMode.ForRead);
            foreach (ObjectId id in ms)
            {
                if (id.IsErased) continue;
                var e = tr.GetObject(id, OpenMode.ForRead, false, true) as AcEntity;
                if (e == null || Ownership.Read(e) != null) continue;
                var mtext = e as MText;
                if (mtext != null) { found.Add(new ExistingAnnotation { Text = mtext.Contents, X = mtext.Location.X, Y = mtext.Location.Y }); continue; }
                var text = e as DBText;
                if (text != null) { found.Add(new ExistingAnnotation { Text = text.TextString, X = text.Position.X, Y = text.Position.Y }); continue; }
                var leader = e as MLeader;
                if (leader != null && leader.ContentType == ContentType.MTextContent && leader.MText != null)
                    found.Add(new ExistingAnnotation { Text = leader.MText.Contents, X = leader.MText.Location.X, Y = leader.MText.Location.Y });
            }
            return found;
        }
    }
}
