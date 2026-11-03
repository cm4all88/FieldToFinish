using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using FieldCodes.Linework;
using FieldCodes.Review;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcEntity = Autodesk.AutoCAD.DatabaseServices.Entity;

namespace FieldCodes.Cad
{
    /// <summary>
    /// Gates: gate posts (FGP) paired into gates and labelled between the posts with the width,
    /// through the same line label style, layer, height and mask as the fence's own label.
    /// FTFGATES runs it on its own; the automatic line label pass, Label All and FTFLABELLINE (for
    /// the fence clicked) run it too. A gate label someone moved or reworded is kept on every
    /// rerun; a pair FTF is unsure of goes to the review list (FTFREVIEW) instead of the plan.
    /// </summary>
    public sealed partial class LineworkCommands
    {
        /// <summary>Tag on a gate label FTF paired: "gate:10075-10076".</summary>
        internal const string GateTag = "gate:";

        /// <summary>Tag on a gate label a drafter paired with FTFGATE: never touched by a rerun,
        /// and its posts are left out of automatic pairing.</summary>
        internal const string ConfirmedGateTag = "gatefix:";

        internal const string GateReviewSource = "Gates";

        internal static bool IsGateTag(string tag)
        {
            return !string.IsNullOrEmpty(tag) &&
                   (tag.StartsWith(GateTag, StringComparison.Ordinal) || tag.StartsWith(ConfirmedGateTag, StringComparison.Ordinal));
        }

        /// <summary>The span markers that are not gate posts: with gate finding on, the gate
        /// pass labels gates, so the old two-marked-shots rule must not label them again.</summary>
        internal static IList<SpanMarkerRule> SpanMarkersWithoutGates(IList<SpanMarkerRule> markers,
                                                                     FieldCodes.Settings.GateSettings gates)
        {
            if (markers == null || gates == null || !gates.Enabled || gates.PostCodes == null) return markers;
            return markers.Where(m => m == null || string.IsNullOrWhiteSpace(m.Token) ||
                                      !gates.PostCodes.Any(c => string.Equals((c ?? string.Empty).Trim(), m.Token.Trim(),
                                                                              StringComparison.OrdinalIgnoreCase)))
                          .ToList();
        }

        internal sealed class GateOutcome
        {
            public GateResult Result;
            public int Placed;
            public int Unchanged;
            public int KeptByHand;
            public int Replaced;
            public int Removed;
            public int Confirmed;
            public int TurnedOff;
            public readonly List<string> Notes = new List<string>();
        }

        private sealed class FenceInfo
        {
            public GateFence Fence;
            public string Layer;
            public IList<LineFeatureRule> Candidates;
            public ObjectId Id;
        }

        private sealed class ExistingGateLabel
        {
            public ObjectId Id;
            public FtfStamp Stamp;
            public string Key;
            public bool Confirmed;
            public bool ByHand;
            public string Contents;
            public Point3d Location;
        }

        [CommandMethod("FTFGATES", CommandFlags.Modal)]
        public void FtfGates()
        {
            FtfSession.Run("FTFGATES", (db, tr, ed) =>
            {
                var cfg = FtfSession.Rules(db);
                var settings = FtfSession.SettingsFor(db, cfg);
                if (!settings.Gates.Enabled)
                {
                    ed.WriteMessage("\nFTFGATES: gate finding is turned off (Settings > Gates).\n");
                    return;
                }
                Ownership.EnsureRegApp(db, tr);
                var styler = new AnnotationStyler(db, tr, settings, cfg);
                var outcome = RunGatePass(db, tr, ed, settings, cfg, styler, null, ObjectId.Null);
                WriteGateOutcome(ed, "FTFGATES", outcome);
                styler.WriteProblems(ed, "FTFGATES");
                ed.WriteMessage("\n");
            });
        }

        /// <summary>
        /// Pairs two gate posts the drafter picks and labels the gate between them -- to confirm a
        /// pair FTF was unsure of, or to change one it made. The label is the drafter's: reruns
        /// never move or remove it, and its posts are left out of automatic pairing.
        /// </summary>
        [CommandMethod("FTFGATE", CommandFlags.Modal)]
        public void FtfGate()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;
            var db = doc.Database;

            RulesConfig cfg;
            FieldCodes.Settings.FtfSettings settings;
            try
            {
                cfg = FtfSession.Rules(db);
                settings = FtfSession.SettingsFor(db, cfg);
            }
            catch (ConfigException ex)
            {
                ed.WriteMessage("\nRules problem: {0}\n", ex.Message);
                return;
            }

            var first = PickGatePost(ed, "\nSelect the first gate post (a COGO point): ");
            if (first.IsNull) return;
            var second = PickGatePost(ed, "\nSelect the other gate post: ");
            if (second.IsNull) return;
            if (first == second)
            {
                ed.WriteMessage("\nThat is the same post twice; nothing was placed.\n");
                return;
            }

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var a = GatePostOf(tr, first);
                var b = GatePostOf(tr, second);
                if (a == null || b == null)
                {
                    ed.WriteMessage("\nPick two COGO points; nothing was placed.\n");
                    return;
                }

                var gs = settings.Gates;
                var pair = GateFinder.Build(a, b, gs, FenceCodes(cfg));
                ed.WriteMessage("\nPosts {0} and {1}: {2} ft apart ({3}).", pair.A.Id, pair.B.Id,
                                pair.Spacing.ToString("0.00", CultureInfo.InvariantCulture), pair.Basis);
                if (!gs.PostCodes.Any(c => GateFinder.IsPost(a.Description, new[] { c })) ||
                    !gs.PostCodes.Any(c => GateFinder.IsPost(b.Description, new[] { c })))
                    ed.WriteMessage("\nNote: {0} is not coded as a gate post ({1}); placing the label you asked for.",
                                    GateFinder.IsPost(a.Description, gs.PostCodes) ? b.Id : a.Id,
                                    string.Join(", ", gs.PostCodes.ToArray()));

                var text = pair.Text;
                if (pair.Decision == GateDecision.Review)
                    ed.WriteMessage("\nFTF would not label this on its own: {0}.", pair.Reason);

                var options = new PromptKeywordOptions(text == null
                    ? "\nLabel it [Text/Cancel] <Text>: "
                    : "\nLabel \"" + text + "\" [Accept/Text/Cancel] <Accept>: ");
                options.AllowNone = true;
                if (text != null) options.Keywords.Add("Accept");
                options.Keywords.Add("Text");
                options.Keywords.Add("Cancel");
                var answer = ed.GetKeywords(options);
                if (answer.Status == PromptStatus.Cancel) { ed.WriteMessage("\nNothing was placed.\n"); return; }
                var choice = answer.Status == PromptStatus.None || string.IsNullOrEmpty(answer.StringResult)
                    ? (text == null ? "Text" : "Accept") : answer.StringResult;
                if (choice == "Cancel") { ed.WriteMessage("\nNothing was placed.\n"); return; }
                if (choice == "Text")
                {
                    var typed = AskForWording(ed);
                    if (typed == null) { ed.WriteMessage("\nNothing was placed.\n"); return; }
                    text = typed;
                }

                Ownership.EnsureRegApp(db, tr);
                var styler = new AnnotationStyler(db, tr, settings, cfg);
                var noteIndex = BuildNoteIndex(db, tr);
                var fences = GateFences(db, tr, cfg, noteIndex, settings.Gates);
                var fence = NearestFence(fences, pair, gs.FenceReachFeet);

                // A pair the drafter decides replaces whatever FTF paired either post with.
                var existing = ExistingGateLabels(db, tr);
                var removed = 0;
                foreach (var old in existing.Where(e => Involves(e.Key, a.Id) || Involves(e.Key, b.Id)))
                {
                    if (old.Confirmed && old.Key != pair.Key) continue;   // another pair the drafter made
                    removed += EraseGateLabel(db, tr, old);
                }

                var placed = PlaceGateLabel(db, tr, settings, cfg, styler, pair, text, fence, ConfirmedGateTag + pair.Key);
                styler.WriteProblems(ed, "FTFGATE");

                var register = DrawingStore.ReadReview(db, tr);
                var now = DateTime.Now;
                var by = Reviewer(settings.Review);
                foreach (var item in register.Items.Where(i => i.IsOpen && i.Kind != null && i.Kind.StartsWith("gate.", StringComparison.Ordinal) &&
                                                               (ListsPoint(i.Points, a.Id) || ListsPoint(i.Points, b.Id))).ToList())
                    register.Resolve(item.Id, "paired by hand with FTFGATE (" + pair.Key + ", \"" + text + "\")", by, now);
                DrawingStore.WriteReview(db, tr, register);

                tr.Commit();
                ed.WriteMessage(placed
                    ? "\nFTFGATE: \"{0}\" placed between posts {1}{2}. Move it wherever you like; reruns keep it.\n"
                    : "\nFTFGATE: the line label style for this fence is turned off (Settings > Style Assignments); nothing was placed{2}.\n",
                    text, pair.Key.Replace("-", " and "),
                    removed > 0 ? "; FTF's earlier label for these posts was removed" : string.Empty);
            }
        }

        private static ObjectId PickGatePost(Editor ed, string message)
        {
            var options = new PromptEntityOptions(message);
            options.SetRejectMessage("\nThat is not a COGO point.");
            options.AddAllowedClass(typeof(Autodesk.Civil.DatabaseServices.CogoPoint), true);
            var picked = ed.GetEntity(options);
            return picked.Status == PromptStatus.OK ? picked.ObjectId : ObjectId.Null;
        }

        private static GatePost GatePostOf(Transaction tr, ObjectId id)
        {
            var cogo = tr.GetObject(id, OpenMode.ForRead, false, true) as Autodesk.Civil.DatabaseServices.CogoPoint;
            return cogo == null ? null : GatePostOf(cogo);
        }

        private static GatePost GatePostOf(Autodesk.Civil.DatabaseServices.CogoPoint cogo)
        {
            var at = cogo.Location;
            return new GatePost
            {
                Id = cogo.PointNumber.ToString(CultureInfo.InvariantCulture),
                X = at.X,
                Y = at.Y,
                Z = at.Z,
                Description = cogo.RawDescription,
                Handle = cogo.Handle.ToString()
            };
        }

        /// <summary>
        /// The gate pass. <paramref name="onlyFence"/> limits the labels to gates on that one fence
        /// (FTFLABELLINE); the review list is then left alone, since only part of the drawing was
        /// looked at.
        /// </summary>
        internal static GateOutcome RunGatePass(Database db, Transaction tr, Editor ed,
                                                FieldCodes.Settings.FtfSettings settings, RulesConfig cfg,
                                                AnnotationStyler styler, SourcePointIndex noteIndex,
                                                ObjectId onlyFence)
        {
            var gs = settings.Gates;
            var outcome = new GateOutcome();
            if (gs == null || !gs.Enabled) return outcome;
            noteIndex = noteIndex ?? BuildNoteIndex(db, tr);

            var posts = new List<GatePost>();
            foreach (var cogo in CadUtil.CogoPointsInOrder(db, tr))
            {
                string description;
                try { description = cogo.RawDescription; }
                catch (Autodesk.AutoCAD.Runtime.Exception) { continue; }
                if (GateFinder.IsPost(description, gs.PostCodes)) posts.Add(GatePostOf(cogo));
            }

            var fences = GateFences(db, tr, cfg, noteIndex, gs);
            var existing = ExistingGateLabels(db, tr);
            var confirmed = new HashSet<string>(StringComparer.Ordinal);
            foreach (var e in existing.Where(e => e.Confirmed))
            {
                outcome.Confirmed++;
                var ids = GateKeys.Posts(e.Key);
                if (ids != null) { confirmed.Add(ids[0]); confirmed.Add(ids[1]); }
            }

            var result = GateFinder.Find(posts, fences.Select(f => f.Fence).ToList(), gs, confirmed, FenceCodes(cfg));
            outcome.Result = result;

            var onlyHandle = onlyFence.IsNull ? null : onlyFence.Handle.ToString();
            var labelled = result.Pairs.Where(p => p.Decision == GateDecision.Label &&
                                                   (onlyHandle == null || (p.Fence != null && p.Fence.Id == onlyHandle)))
                                       .ToList();
            var wanted = new HashSet<string>(labelled.Select(p => p.Key), StringComparer.Ordinal);

            foreach (var pair in labelled)
            {
                var fence = fences.FirstOrDefault(f => pair.Fence != null && f.Fence.Id == pair.Fence.Id);
                var mine = existing.Where(e => !e.Confirmed && e.Key == pair.Key).ToList();
                if (mine.Any(e => e.ByHand))
                {
                    outcome.KeptByHand++;
                    continue;
                }
                var position = new Point3d(pair.MidX, pair.MidY, 0.0);
                var same = mine.FirstOrDefault(e => e.Stamp.SourceText == pair.Text && e.Location.DistanceTo(position) < 0.01);
                if (same != null && mine.Count == 1)
                {
                    outcome.Unchanged++;
                    continue;
                }
                foreach (var old in mine) { EraseGateLabel(db, tr, old); outcome.Replaced++; }
                if (PlaceGateLabel(db, tr, settings, cfg, styler, pair, pair.Text, fence, GateTag + pair.Key)) outcome.Placed++;
                else outcome.TurnedOff++;
            }

            // FTF's earlier gate labels that no longer stand for a gate: removed, unless moved or
            // reworded by hand (then kept, and said).
            if (onlyHandle == null)
            {
                foreach (var old in existing.Where(e => !e.Confirmed && !wanted.Contains(e.Key)))
                {
                    if (old.ByHand)
                    {
                        outcome.Notes.Add("gate label \"" + old.Contents + "\" for posts " + old.Key.Replace("-", " and ") +
                                          " was moved or edited by hand and is kept, but FTF no longer pairs those posts");
                        continue;
                    }
                    outcome.Removed += EraseGateLabel(db, tr, old) > 0 ? 1 : 0;
                }

                var register = DrawingStore.ReadReview(db, tr);
                register.Report(GateReviewSource, result.Review, DateTime.Now, true, settings.Review);
                DrawingStore.WriteReview(db, tr, register);
            }

            return outcome;
        }

        internal static void WriteGateOutcome(Editor ed, string command, GateOutcome o)
        {
            if (o == null || o.Result == null) return;
            var r = o.Result;
            ed.WriteMessage("\n{0}: {1} gate(s) labelled{2}{3}{4}{5}.", command, o.Placed,
                o.Unchanged > 0 ? ", " + o.Unchanged + " already labelled" : string.Empty,
                o.KeptByHand > 0 ? ", " + o.KeptByHand + " kept as you moved or edited them" : string.Empty,
                o.Confirmed > 0 ? ", " + o.Confirmed + " paired by hand (FTFGATE)" : string.Empty,
                o.Removed > 0 ? ", " + o.Removed + " earlier label(s) no longer a gate removed" : string.Empty);
            foreach (var p in r.Pairs.Where(p => p.Decision == GateDecision.Label))
                ed.WriteMessage("\n  {0}  posts {1}: {2} ft, {3}.", p.Text, p.Key.Replace("-", " and "),
                                p.Spacing.ToString("0.00", CultureInfo.InvariantCulture), p.Basis);
            if (o.TurnedOff > 0)
                ed.WriteMessage("\n  {0} gate(s) not labelled: the fence's line label is turned off (Settings > Style Assignments).", o.TurnedOff);
            foreach (var n in o.Notes) ed.WriteMessage("\n  Check: {0}.", n);
            if (r.Review.Count > 0)
            {
                ed.WriteMessage("\n{0}: {1} gate item(s) for review (FTFREVIEW):", command, r.Review.Count);
                foreach (var item in r.Review.Take(20))
                    ed.WriteMessage("\n  {0}: {1}.", ReviewRegister.Describe(item), item.Reason);
                if (r.Review.Count > 20) ed.WriteMessage("\n  ...");
            }
        }

        /// <summary>Every fence line in model space, flattened to plan.</summary>
        private static List<FenceInfo> GateFences(Database db, Transaction tr, RulesConfig cfg,
                                                  SourcePointIndex noteIndex,
                                                  FieldCodes.Settings.GateSettings gs)
        {
            var catalog = new LineworkCatalog(cfg.LineFeatures, cfg.IgnoreLineNames);
            var fences = new List<FenceInfo>();
            var ms = CadUtil.ModelSpace(db, tr, OpenMode.ForRead);
            foreach (ObjectId id in ms)
            {
                if (id.IsErased) continue;
                var curve = tr.GetObject(id, OpenMode.ForRead, false, true) as Curve;
                if (curve == null || Ownership.Read(curve) != null) continue;
                string how;
                var candidates = StagedCandidates(db, tr, curve, catalog, out how, noteIndex);
                if (candidates == null || candidates.Count == 0 || !candidates.Any(IsFence)) continue;
                var points = PlanPoints(curve);
                if (points.Count < 2) continue;
                fences.Add(new FenceInfo
                {
                    Id = id,
                    Layer = curve.Layer,
                    Candidates = candidates,
                    Fence = new GateFence
                    {
                        Id = id.Handle.ToString(),
                        Code = candidates[0].Code,
                        Layer = curve.Layer,
                        Points = points
                    }
                });
            }
            return fences;
        }

        /// <summary>A fence for gates: any feature named or labelled FENCE, or one told to read
        /// heights (a wall).</summary>
        private static bool IsFence(LineFeatureRule rule)
        {
            return LineHeights.Applies(rule);
        }

        /// <summary>The codes of every fence feature, with aliases: a recorded gate width stops at them.</summary>
        private static IList<string> FenceCodes(RulesConfig cfg)
        {
            return (cfg.LineFeatures ?? new List<LineFeatureRule>())
                .Where(IsFence)
                .SelectMany(f => new[] { f.Code }.Concat(f.Aliases ?? new List<string>()))
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>The curve in plan: its vertices, or points every foot along an arc.</summary>
        private static List<FieldCodes.Easements.P2> PlanPoints(Curve curve)
        {
            var hasArc = curve is Arc || curve is Circle || curve is Ellipse || curve is Spline;
            var pl = curve as Autodesk.AutoCAD.DatabaseServices.Polyline;
            if (pl != null)
                for (var i = 0; i < pl.NumberOfVertices && !hasArc; i++)
                    if (Math.Abs(pl.GetBulgeAt(i)) > 1e-9) hasArc = true;

            var points = new List<FieldCodes.Easements.P2>();
            if (!hasArc)
            {
                foreach (var v in VerticesOf(curve))
                {
                    var p = new FieldCodes.Easements.P2(v.X, v.Y);
                    if (points.Count == 0 || points[points.Count - 1].DistanceTo(p) > 1e-6) points.Add(p);
                }
                return points;
            }
            try
            {
                var length = curve.GetDistanceAtParameter(curve.EndParam);
                var steps = (int)Math.Min(2000, Math.Max(8, Math.Ceiling(length)));
                for (var k = 0; k <= steps; k++)
                {
                    var at = curve.GetPointAtDist(length * k / steps);
                    points.Add(new FieldCodes.Easements.P2(at.X, at.Y));
                }
            }
            catch (Autodesk.AutoCAD.Runtime.Exception) { }
            return points;
        }

        private static FenceInfo NearestFence(IList<FenceInfo> fences, GatePair pair, double reach)
        {
            FenceInfo best = null;
            var bestOffset = double.MaxValue;
            foreach (var f in fences)
                foreach (var p in new[] { pair.A, pair.B })
                {
                    double offset, station, length;
                    GateFinder.Closest(p.At, f.Fence.Points, out offset, out station, out length);
                    if (offset <= reach && offset < bestOffset) { best = f; bestOffset = offset; }
                }
            return best;
        }

        private static List<ExistingGateLabel> ExistingGateLabels(Database db, Transaction tr)
        {
            var found = new List<ExistingGateLabel>();
            foreach (var pair in Ownership.FindOwned(db, tr, s => s.Kind == FtfEntityKind.LineLabel && IsGateTag(s.TagText)))
            {
                var text = tr.GetObject(pair.Key, OpenMode.ForRead, false, true) as MText;
                if (text == null) continue;
                var stamp = pair.Value;
                var confirmed = stamp.TagText.StartsWith(ConfirmedGateTag, StringComparison.Ordinal);
                var key = stamp.TagText.Substring(confirmed ? ConfirmedGateTag.Length : GateTag.Length);
                var moved = stamp.WasMovedByHand(text.Location, 0.01);
                var edited = !string.IsNullOrEmpty(stamp.ContentsHash) &&
                             Ownership.ContentsHashOf(text.Contents) != stamp.ContentsHash;
                found.Add(new ExistingGateLabel
                {
                    Id = pair.Key,
                    Stamp = stamp,
                    Key = key,
                    Confirmed = confirmed,
                    ByHand = moved || edited,
                    Contents = text.Text,
                    Location = text.Location
                });
            }
            return found;
        }

        /// <summary>Erases a gate label and the mask placed with it. Returns how many entities went.</summary>
        private static int EraseGateLabel(Database db, Transaction tr, ExistingGateLabel label)
        {
            var tag = label.Stamp.TagText;
            var erased = 0;
            var text = tr.GetObject(label.Id, OpenMode.ForWrite, false, true) as AcEntity;
            if (text != null && !text.IsErased) { text.Erase(); erased++; }

            // Its mask: the line mask with the same tag nearest the label.
            var masks = Ownership.FindOwned(db, tr, s => s.Kind == FtfEntityKind.LineMask && s.TagText == tag);
            foreach (var m in masks)
            {
                var mask = tr.GetObject(m.Key, OpenMode.ForWrite, false, true) as AcEntity;
                if (mask == null || mask.IsErased) continue;
                Extents3d box;
                try { box = mask.GeometricExtents; }
                catch (Autodesk.AutoCAD.Runtime.Exception) { continue; }
                var centre = new Point3d((box.MinPoint.X + box.MaxPoint.X) / 2, (box.MinPoint.Y + box.MaxPoint.Y) / 2, 0);
                if (centre.DistanceTo(new Point3d(label.Location.X, label.Location.Y, 0)) > 50) continue;
                mask.Erase();
                erased++;
            }
            return erased;
        }

        private static bool PlaceGateLabel(Database db, Transaction tr, FieldCodes.Settings.FtfSettings settings,
                                           RulesConfig cfg, AnnotationStyler styler, GatePair pair, string text,
                                           FenceInfo fence, string tag)
        {
            var ls = settings.LineLabels;
            var gs = settings.Gates;
            string layer;
            if (!string.IsNullOrWhiteSpace(gs.LabelLayer)) layer = gs.LabelLayer.Trim();
            else if (fence != null)
                layer = LabelLayerResolver.Resolve(fence.Layer, fence.Candidates, FtfLineworkService.LayerNames(db, tr),
                                                   ls.DefaultLabelLayer).Layer;
            else layer = ls.DefaultLabelLayer;

            var code = fence != null && fence.Candidates.Count > 0 ? fence.Candidates[0].Code : "GATE";
            var style = styler.For(new FieldCodes.Styles.StyleSubject
            {
                Kind = FieldCodes.Styles.StyledKind.LineLabel,
                Category = FieldCodes.Settings.AnnotationCategory.Lines,
                Code = code,
                BaseLayer = layer
            });
            if (!style.Enabled) return false;
            if (!string.IsNullOrWhiteSpace(style.Layer) && string.IsNullOrWhiteSpace(gs.LabelLayer)) layer = style.Layer;
            var layerId = CadUtil.EnsureLayer(db, tr, layer);

            // Between the posts, along the opening; the posts themselves are only read.
            var plan = LineLabelPlanner.PlaceAt(pair.MidX, pair.MidY, pair.Direction, LineLabelSide.OnLine, 0.0, ls.AlignToLine);
            PlaceStyledLineLabel(db, tr, styler, style, plan, text, layerId, code, cfg.Version, tag);
            return true;
        }

        private static bool Involves(string key, string post)
        {
            var ids = GateKeys.Posts(key);
            return ids != null && (ids[0] == post || ids[1] == post);
        }

        private static bool ListsPoint(string points, string id)
        {
            if (string.IsNullOrWhiteSpace(points)) return false;
            return points.Split(',').Any(p => string.Equals(p.Trim(), id, StringComparison.Ordinal));
        }

        internal static string Reviewer(FieldCodes.Settings.ReviewSettings s)
        {
            if (s != null && !string.IsNullOrWhiteSpace(s.ReviewerName)) return s.ReviewerName.Trim();
            try { return Environment.UserName; }
            catch (InvalidOperationException) { return "drafter"; }
        }
    }
}
