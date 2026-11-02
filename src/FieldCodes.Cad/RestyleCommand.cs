using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using FieldCodes.Settings;
using FieldCodes.Styles;

using AcEntity = Autodesk.AutoCAD.DatabaseServices.Entity;

namespace FieldCodes.Cad
{
    /// <summary>
    /// FTFRESTYLE: brings the labels, tags and line labels FTF already drew up to the current
    /// annotation styles -- text style, height, colour, mask, prefix and suffix, rounding,
    /// leader look and layer -- in place.
    ///
    /// Preview first: it lists what would change and changes nothing. Apply changes only
    /// entities FTF drew and still owns (its stamp, on the entity it was written on). A copy
    /// someone made, text someone typed, and every other drawing entity are never touched.
    /// Nothing is moved: a label someone dragged keeps its place, and a leader keeps its
    /// vertices. Wording someone edited is kept; only the look is updated.
    ///
    /// New labels and tags pick up the styles on their own (FTFLABELS, FTFTAGS, Process
    /// Drawing); this command is for annotation that is already on the sheet.
    ///
    /// UNTESTED in Civil 3D: compiled against the API stand-ins only.
    /// </summary>
    public sealed class RestyleCommand
    {
        [CommandMethod("FTFRESTYLE", CommandFlags.Modal)]
        public void FtfRestyle()
        {
            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ask = new PromptKeywordOptions("\nUpdate FTF annotation to the current styles [Preview/Apply] <Preview>: ") { AllowNone = true };
            ask.Keywords.Add("Preview");
            ask.Keywords.Add("Apply");
            ask.Keywords.Default = "Preview";
            var answer = doc.Editor.GetKeywords(ask);
            if (answer.Status != PromptStatus.OK && answer.Status != PromptStatus.None) return;
            var apply = answer.Status == PromptStatus.OK && answer.StringResult == "Apply";
            Run(apply);
        }

        /// <summary>Preview (apply false) or update; the window's Update Existing button calls this too.</summary>
        internal static RestyleReport Run(bool apply)
        {
            RestyleReport result = null;
            FtfSession.Run("FTFRESTYLE", (db, tr, ed) =>
            {
                var cfg = FtfSession.Rules(db);
                var settings = FtfSession.SettingsFor(db, cfg);
                var parser = new FieldCodeParser(cfg);
                var notes = CogoNotes.For(db, tr, settings);
                var scan = AnnotationScan.Read(db, tr, cfg, settings, parser, notes);
                var styler = new AnnotationStyler(db, tr, settings, cfg);
                var report = new RestyleReport();

                var owned = Ownership.FindOwned(db, tr, s =>
                    s.Kind == FtfEntityKind.Label || s.Kind == FtfEntityKind.Tag || s.Kind == FtfEntityKind.LineLabel);

                foreach (var pair in owned)
                {
                    var stamp = pair.Value;
                    var entity = tr.GetObject(pair.Key, OpenMode.ForRead, false, true) as AcEntity;
                    if (entity == null) continue;
                    var mtext = entity as MText;
                    var leader = entity as MLeader;
                    if (mtext == null && (leader == null || leader.ContentType != ContentType.MTextContent)) continue;

                    StyledKind kind;
                    ResolvedAnnotationStyle style;
                    double? symbol = null;
                    string baseText;
                    string layer;

                    if (stamp.Kind == FtfEntityKind.LineLabel)
                    {
                        kind = StyledKind.LineLabel;
                        style = styler.For(new StyleSubject
                        {
                            Kind = kind,
                            Category = AnnotationCategory.Lines,
                            Code = stamp.PointNumber,
                            BaseLayer = entity.Layer
                        });
                        baseText = null;
                        layer = string.IsNullOrWhiteSpace(style.Layer) ? entity.Layer : style.Layer;
                    }
                    else
                    {
                        ParsedPoint p;
                        if (!scan.Points.TryGetValue(stamp.PointNumber ?? string.Empty, out p)) { report.NoPoint++; continue; }
                        kind = stamp.Kind == FtfEntityKind.Tag ? StyledKind.Tag : StyledKind.Label;
                        style = styler.For(kind, p);
                        symbol = p.MarkerRotationDegrees;
                        baseText = kind == StyledKind.Tag ? stamp.TagText : p.LabelText;
                        layer = string.IsNullOrWhiteSpace(style.Layer) ? p.LabelLayer : style.Layer;
                    }

                    // The wording: re-formatted from the text FTF formatted it from, unless
                    // someone has edited it since.
                    var current = mtext != null ? mtext.Contents : ContentsOf(leader);
                    string source;
                    bool unedited;
                    if (!string.IsNullOrEmpty(stamp.ContentsHash))
                    {
                        source = stamp.SourceText;
                        unedited = !string.IsNullOrEmpty(source) && Ownership.ContentsHashOf(current) == stamp.ContentsHash;
                    }
                    else if (kind == StyledKind.LineLabel)
                    {
                        // Drawn before styles: FTF wrote the line's label as it was.
                        source = current;
                        unedited = true;
                    }
                    else
                    {
                        source = baseText;
                        unedited = !string.IsNullOrEmpty(source) && string.Equals(current, source, StringComparison.Ordinal);
                    }
                    var wanted = unedited ? StyleText.Compose(source, style.Text) : current;

                    var mappedLayer = ProjectLayers.Apply(db, layer);
                    var changes = Differences(db, tr, styler, entity, mtext, leader, style, mappedLayer, current, wanted, stamp);
                    report.Saw(kind, changes, !unedited);
                    if (!apply || changes.Count == 0) continue;

                    entity.UpgradeOpen();
                    if (!string.IsNullOrWhiteSpace(layer)) entity.LayerId = CadUtil.EnsureLayer(db, tr, layer);
                    if (mtext != null)
                    {
                        // A line label keeps the angle of its line.
                        styler.Restyle(mtext, unedited ? source : null, style.Text, symbol, kind == StyledKind.LineLabel);
                    }
                    else
                    {
                        styler.Restyle(leader, unedited ? source : null, style, symbol);
                    }

                    // Same point, kind, tag and computed position: hand-move detection is
                    // unaffected. Only the style record is new.
                    Ownership.Stamp(entity, stamp.PointNumber, stamp.RulesVersion, stamp.Kind, stamp.ComputedPosition,
                                    stamp.TagText, AnnotationStyler.StyleKey(style), unedited ? source : stamp.SourceText,
                                    mtext != null ? mtext.Contents : ContentsOf(leader));
                }

                report.UserCopiesLeftAlone = CountUserCopies(db, tr);
                if (apply && report.Changing > 0)
                    MaskSync.Rebuild(db, tr, ed, settings, cfg.Version);

                ed.WriteMessage("\nFTFRESTYLE {0}:", apply ? "updated" : "preview -- nothing changed");
                foreach (var line in report.Lines(apply)) ed.WriteMessage("\n  " + line);
                if (!apply && report.Changing > 0)
                    ed.WriteMessage("\n  Run FTFRESTYLE again and choose Apply to make these changes.");
                ed.WriteMessage("\n");
                styler.WriteProblems(ed, "FTFRESTYLE");
                result = report;
            });
            return result;
        }

        private static string ContentsOf(MLeader leader)
        {
            if (leader == null) return string.Empty;
            try
            {
                using (var mt = leader.MText) return mt != null ? mt.Contents : string.Empty;
            }
            catch (Autodesk.AutoCAD.Runtime.Exception) { return string.Empty; }
        }

        /// <summary>What would change on one entity, in words. Read only.</summary>
        private static List<string> Differences(Database db, Transaction tr, AnnotationStyler styler, AcEntity entity,
                                                MText mtext, MLeader leader, ResolvedAnnotationStyle style,
                                                string mappedLayer, string current, string wanted, FtfStamp stamp)
        {
            var changes = new List<string>();
            Action<string> add = c => { if (c != null) changes.Add(c); };

            if (!string.IsNullOrWhiteSpace(mappedLayer)) add(RestyleReport.Change("layer", entity.Layer, mappedLayer));

            var t = style.Text;
            var styleId = styler.TextStyleId(t.DrawingTextStyle);
            ObjectId currentStyle;
            double height;
            if (mtext != null)
            {
                currentStyle = mtext.TextStyleId;
                height = mtext.TextHeight;
            }
            else
            {
                currentStyle = leader.TextStyleId;
                height = leader.TextHeight;
                try { using (var mt = leader.MText) if (mt != null) { currentStyle = mt.TextStyleId; height = mt.TextHeight; } }
                catch (Autodesk.AutoCAD.Runtime.Exception) { }
            }
            var annotative = t.Annotative || (leader != null && style.Leader.Annotative);
            var wantedHeight = annotative ? t.HeightPlotted : styler.ModelHeight(t);
            if (!styleId.IsNull && styleId != currentStyle)
                add(RestyleReport.Change("text style", NameOf(tr, currentStyle), t.DrawingTextStyle));
            var isAnnotative = entity.Annotative == AnnotativeStates.True;
            if (isAnnotative != annotative) add(annotative ? "made annotative" : "no longer annotative");
            else add(RestyleReport.Change("text height", height, wantedHeight));
            if (!string.Equals(current ?? string.Empty, wanted ?? string.Empty, StringComparison.Ordinal))
                add("text formatting (prefix, suffix, rounding or width)");

            var newKey = AnnotationStyler.StyleKey(style);
            if (!string.IsNullOrEmpty(stamp.Style) && !string.Equals(stamp.Style, newKey, StringComparison.OrdinalIgnoreCase))
                add(RestyleReport.Change("style", Describe(stamp.Style), Describe(newKey)));
            else if (string.IsNullOrEmpty(stamp.Style) && changes.Count == 0 &&
                     (style.Text.Name.IndexOf("(no style)", StringComparison.Ordinal) < 0 ||
                      style.Leader.Name.IndexOf("(no style)", StringComparison.Ordinal) < 0))
                add("style applied: " + Describe(newKey));
            return changes;
        }

        private static string Describe(string key)
        {
            var parts = (key ?? string.Empty).Split('|');
            var text = parts.Length > 0 ? parts[0] : string.Empty;
            var leader = parts.Length > 1 ? parts[1] : string.Empty;
            return leader.Length == 0 ? text : text + " / " + leader;
        }

        private static string NameOf(Transaction tr, ObjectId styleId)
        {
            if (styleId.IsNull) return string.Empty;
            try
            {
                var record = tr.GetObject(styleId, OpenMode.ForRead) as SymbolTableRecord;
                return record != null ? record.Name : string.Empty;
            }
            catch (Autodesk.AutoCAD.Runtime.Exception) { return string.Empty; }
        }

        private static int CountUserCopies(Database db, Transaction tr)
        {
            var count = 0;
            var ms = CadUtil.ModelSpace(db, tr, OpenMode.ForRead);
            foreach (ObjectId id in ms)
            {
                if (id.IsErased) continue;
                var e = tr.GetObject(id, OpenMode.ForRead, false, true) as AcEntity;
                if (e == null || !Ownership.IsUserCopy(e)) continue;
                var s = Ownership.ReadIncludingCopies(e);
                if (s != null && (s.Kind == FtfEntityKind.Label || s.Kind == FtfEntityKind.Tag || s.Kind == FtfEntityKind.LineLabel)) count++;
            }
            return count;
        }
    }
}
