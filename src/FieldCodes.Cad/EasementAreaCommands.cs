using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using FieldCodes.Easements;
using FieldCodes.Settings;

using AcEntity = Autodesk.AutoCAD.DatabaseServices.Entity;

namespace FieldCodes.Cad
{
    /// <summary>
    /// Easements that are not strips along a line:
    /// PORTIONEASEMENT -- "the west 10 feet of the south 50 feet of Lot 2", each distance
    /// measured at right angles from a picked lot line; and
    /// CONSTRUCTIONAREA -- a metes and bounds area clicked corner by corner (sides can follow
    /// an existing line or curve), on its own layer, hatched and labelled per the clicks.
    /// Both are stored like strip easements: FTFEASEMENTCHECK rebuilds them when the survey
    /// moves, and FTFEASEMENTLEGAL writes their draft legal descriptions.
    /// </summary>
    public sealed class EasementAreaCommands
    {
        private const string LotRole = "LOT";

        // ======================================================== PORTIONEASEMENT

        [CommandMethod("PORTIONEASEMENT", CommandFlags.Modal)]
        public void PortionEasement()
        {
            FtfSession.Run("PORTIONEASEMENT", (db, tr, ed) =>
            {
                var rules = FtfSession.Rules(db);
                var settings = FtfSession.SettingsFor(db, rules);
                var es = settings.Easements;
                var tolerance = es.ToleranceFt * settings.General.UnitsPerFoot;

                // The lot.
                PromptStatus lotStatus;
                Autodesk.AutoCAD.Geometry.Point3d lotClick;
                var lotEntity = EasementCommands.PickCurve(db, tr, ed, "\nSelect the lot boundary (a closed polyline, or one of its lot lines): ", false, out lotStatus, out lotClick);
                if (lotEntity == null) { ed.WriteMessage("\nPORTIONEASEMENT: cancelled.\n"); return; }
                string problem;
                var lot = EasementCommands.Extract(lotEntity, out problem);
                if (lot == null || Loops.LargestGap(lot) > tolerance)
                {
                    // Lot lines in production drawings overlap the closed lot polyline: use the closed one under the pick.
                    var closed = EasementCommands.ClosedUnder(db, tr, ed, lotClick, tolerance);
                    if (closed != null)
                    {
                        ed.WriteMessage("\n  The pick was on an open {0}; using the closed {1} under it.", EasementCommands.TypeName(lotEntity), EasementCommands.TypeName(closed));
                        lotEntity = closed;
                        lot = EasementCommands.Extract(lotEntity, out problem);
                    }
                }
                List<GeometrySource> lotLines = null;
                if (lot == null || Loops.LargestGap(lot) > tolerance)
                {
                    // No closed object: the lot as its separate lines, joined by FTF for its own use only.
                    var answer = EasementCommands.Keyword(ed, "\nThe lot is not one closed object. Build it from its separate lot lines [Lines/Cancel] <Lines>: ", "Lines", "Lines", "Cancel");
                    if (answer != "Lines") { ed.WriteMessage("\nPORTIONEASEMENT: cancelled -- the lot boundary must be closed" + (problem != null ? " (" + problem + ")" : string.Empty) + ".\n"); return; }
                    lot = PickLotLines(db, tr, ed, lotEntity, tolerance, out lotLines, out problem);
                    if (lot == null)
                    {
                        ed.WriteMessage("\nPORTIONEASEMENT: the selected lines do not make one closed lot -- " + problem +
                                        "\n  Nothing was drawn and no line was changed. Select only this lot's lines (or draw its closed boundary) and run PORTIONEASEMENT again.\n");
                        return;
                    }
                    ed.WriteMessage("\n  Lot built from {0} line(s): {1} courses, {2:N0} sq ft, closes exactly. The lines themselves are not joined or changed.",
                                    lotLines.Count, lot.Count, Math.Abs(Loops.SignedArea(lot)) / (settings.General.UnitsPerFoot * settings.General.UnitsPerFoot));
                    lotEntity = null;
                }

                // The calls, in reading order.
                bool cancelled;
                var steps = GatherCalls(db, tr, ed, es, lotEntity, lot, tolerance, out cancelled);
                if (cancelled) { ed.WriteMessage("\nPORTIONEASEMENT: cancelled.\n"); return; }
                var purpose = ed.GetString(new PromptStringOptions("\nPurpose for the title <" + es.DefaultPurpose + ">: ") { AllowSpaces = true });
                if (purpose.Status == PromptStatus.Cancel) return;
                var record = new EasementRecord
                {
                    Kind = EasementRecord.PortionKind, Profile = DrawingStore.ReadProfileName(db),
                    Purpose = purpose.Status == PromptStatus.OK && !string.IsNullOrWhiteSpace(purpose.StringResult) ? purpose.StringResult.Trim().ToUpperInvariant() : es.DefaultPurpose
                };
                record.PortionSteps = steps;
                record.Parcel = lotEntity != null
                    ? new GeometrySource { Handle = lotEntity.Handle.ToString(), EntityType = EasementCommands.TypeName(lotEntity), Role = LotRole, Fingerprint = EasementAnnotation.Fingerprint(lot) }
                    : new GeometrySource { Handle = null, EntityType = LotLinesType, Role = LotRole, Fingerprint = EasementAnnotation.Fingerprint(lot) };
                record.LotLines = lotLines;

                if (!FinishPortion(db, tr, ed, settings, rules.Version, record, lot)) return;
                DrawingStore.SaveEasement(db, tr, record);
                ed.WriteMessage("\nPORTIONEASEMENT: {0} -- {1}. Stored; FTFEASEMENTLEGAL writes its draft description.\n",
                                PortionBuilder.Describe(steps, es.DistanceDecimals) + " THE LOT", EasementAnnotation.AreaLine(record.AreaSquareFeet, es, record.Purpose));
            });
        }

        internal const string LotLinesType = "Lot lines";
        internal const string LotLineRole = "LOT LINE ";
        internal const string TieFollowsRole = "TIE FOLLOWS ";

        /// <summary>
        /// The lot as separately drawn lines: the drafter selects the lines around it and FTF joins them into its
        /// own closed boundary -- only when they close unambiguously. The objects are never joined or changed.
        /// </summary>
        private static IList<Course> PickLotLines(Database db, Transaction tr, Editor ed, AcEntity picked, double tolerance, out List<GeometrySource> sources, out string problem)
        {
            sources = null;
            problem = null;
            var options = new PromptSelectionOptions { MessageForAdding = "\nSelect the lines and curves around the lot (the one picked is included), then Enter: " };
            var result = ed.GetSelection(options);
            if (result.Status != PromptStatus.OK && result.Status != PromptStatus.Error) { problem = "nothing was selected."; return null; }
            var ids = new List<ObjectId> { picked.ObjectId };
            if (result.Value != null) ids.AddRange(result.Value.GetObjectIds().Where(id => id != picked.ObjectId));
            var entities = new List<AcEntity>();
            var pieces = new List<IList<Course>>();
            foreach (var id in ids.Distinct())
            {
                var entity = tr.GetObject(id, OpenMode.ForRead) as AcEntity;
                if (!(entity is Curve)) continue;
                string why;
                var courses = EasementCommands.Extract(entity, out why);
                if (courses == null) { problem = EasementCommands.TypeName(entity) + " " + entity.Handle + " cannot be read (" + why + ")."; return null; }
                entities.Add(entity);
                pieces.Add(courses);
            }
            var chain = JoinLot(pieces, tolerance);
            if (!chain.Ok) { problem = chain.Problem; return null; }
            if (chain.IgnoredLength > tolerance)
            {
                // Lines that run on past the lot's corners: say how much is set aside and let the drafter decide.
                var upf2 = Math.Abs(Loops.SignedArea(chain.Courses));
                ed.WriteMessage("\n  The selected lines enclose one area ({0:N0} sq units, {1} courses); {2:N2}' of selected line beyond its corners is not part of it.",
                                upf2, chain.Courses.Count, chain.IgnoredLength);
                var use = EasementCommands.Keyword(ed, "\nUse that area as the lot [Yes/No] <Yes>: ", "Yes", "Yes", "No");
                if (use != "Yes") { problem = "not used -- nothing was drawn."; return null; }
            }
            sources = entities.Select((e, i) => new GeometrySource { Handle = e.Handle.ToString(), EntityType = EasementCommands.TypeName(e), Role = LotLineRole + (i + 1), Fingerprint = EasementAnnotation.Fingerprint(pieces[i]) }).ToList();
            return chain.Courses;
        }

        /// <summary>
        /// The lot from separate objects: joined end to end when they meet exactly; otherwise the one area they
        /// enclose when lines run past the corners. Anything else is refused with both reasons.
        /// </summary>
        private static ChainResult JoinLot(IList<IList<Course>> pieces, double tolerance)
        {
            var strict = CurveChain.Closed(pieces, tolerance);
            if (strict.Ok) return strict;
            var enclosed = CurveChain.Enclosed(pieces, tolerance);
            if (enclosed.Ok) return enclosed;
            return new ChainResult { Problem = enclosed.Problem + " (" + strict.Problem.TrimEnd('.') + ".)" };
        }

        /// <summary>
        /// A portion easement's lot as the drawing has it now: its closed object, or its separate lot lines joined
        /// again (and checked again). <paramref name="refreshed"/> gets the lot line sources with current fingerprints.
        /// </summary>
        internal static IList<Course> LotCourses(Database db, Transaction tr, EasementRecord r, double tolerance, out string problem, List<GeometrySource> refreshed = null)
        {
            problem = null;
            if (r.LotLines != null && r.LotLines.Count > 0)
            {
                var pieces = new List<IList<Course>>();
                foreach (var s in r.LotLines)
                {
                    var entity = EasementCommands.Resolve(db, tr, s.Handle);
                    if (entity == null) { problem = "lot line " + s.Handle + " was erased."; return null; }
                    var courses = EasementCommands.Extract(entity, out problem);
                    if (courses == null) return null;
                    pieces.Add(courses);
                    if (refreshed != null) refreshed.Add(new GeometrySource { Handle = s.Handle, EntityType = s.EntityType, Role = s.Role, Fingerprint = EasementAnnotation.Fingerprint(courses) });
                }
                var chain = JoinLot(pieces, tolerance);
                if (!chain.Ok) { problem = "the lot lines no longer make one closed lot: " + chain.Problem; return null; }
                return chain.Courses;
            }
            var lotEntity = EasementCommands.Resolve(db, tr, r.Parcel != null ? r.Parcel.Handle : null);
            if (lotEntity == null) { problem = "the lot boundary was erased."; return null; }
            return EasementCommands.Extract(lotEntity, out problem);
        }

        /// <summary>
        /// Lines and curves a commencement tie follows, selected by the drafter. Null when cancelled.
        /// </summary>
        internal static List<GeometrySource> PickTieFollows(Database db, Transaction tr, Editor ed)
        {
            var result = ed.GetSelection(new PromptSelectionOptions { MessageForAdding = "\nSelect the lines and curves the tie runs along from the Point of Commencement, then Enter: " });
            if (result.Status != PromptStatus.OK) return null;
            var sources = new List<GeometrySource>();
            foreach (var id in result.Value.GetObjectIds())
            {
                var entity = tr.GetObject(id, OpenMode.ForRead) as AcEntity;
                if (!(entity is Curve)) continue;
                string problem;
                var courses = EasementCommands.Extract(entity, out problem);
                if (courses == null) { ed.WriteMessage("\n  {0} {1} is skipped: {2}", EasementCommands.TypeName(entity), entity.Handle, problem); continue; }
                sources.Add(new GeometrySource { Handle = entity.Handle.ToString(), EntityType = EasementCommands.TypeName(entity), Role = TieFollowsRole + (sources.Count + 1), Fingerprint = EasementAnnotation.Fingerprint(courses) });
            }
            return sources;
        }

        /// <summary>
        /// The tie from the Point of Commencement to the Point of Beginning: straight, or along the objects it
        /// follows -- line and curve exactly as drawn, never replaced by a chord. Null, with the reason, when the
        /// followed objects do not run unambiguously through both points. <paramref name="refreshed"/> gets the
        /// followed objects with their current fingerprints.
        /// </summary>
        internal static List<Course> TiePath(Database db, Transaction tr, P2 poc, P2 pob, IList<GeometrySource> follows, double tolerance, string rolePrefix,
                                             List<GeometrySource> refreshed, out string problem)
        {
            problem = null;
            if (follows == null || follows.Count == 0) return new List<Course> { Course.Line(poc, pob) };
            var pieces = new List<IList<Course>>();
            for (var i = 0; i < follows.Count; i++)
            {
                var f = follows[i];
                var entity = EasementCommands.Resolve(db, tr, f.Handle);
                if (entity == null) { problem = "the " + (f.EntityType ?? "object").ToLowerInvariant() + " the tie follows was erased."; return null; }
                var courses = EasementCommands.Extract(entity, out problem);
                if (courses == null) return null;
                pieces.Add(courses);
                if (refreshed != null) refreshed.Add(new GeometrySource { Handle = f.Handle, EntityType = f.EntityType, Role = rolePrefix + TieFollowsRole + (i + 1), Fingerprint = EasementAnnotation.Fingerprint(courses) });
            }
            var chain = CurveChain.Open(pieces, poc, tolerance);
            if (!chain.Ok) { problem = "the objects the tie follows " + Lower(chain.Problem); return null; }
            var start = EasementBuilder.NearestStation(chain.Courses, poc);
            var end = EasementBuilder.NearestStation(chain.Courses, pob);
            if (start.Item2 > tolerance) { problem = "the Point of Commencement is " + start.Item2.ToString("0.000", CultureInfo.InvariantCulture) + "' off the line the tie follows."; return null; }
            if (end.Item2 > tolerance) { problem = "the Point of Beginning is " + end.Item2.ToString("0.000", CultureInfo.InvariantCulture) + "' off the line the tie follows."; return null; }
            var path = AreaPath.Between(chain.Courses, poc, pob, tolerance, out problem);
            if (path == null) { problem = "the tie " + Lower(problem); return null; }
            return path;
        }

        private static string Lower(string s)
        {
            return string.IsNullOrEmpty(s) ? string.Empty : s.Substring(0, 1).ToLowerInvariant() + s.Substring(1);
        }

        /// <summary>
        /// Portion calls in reading order: a lot line, its distance at right angles and what the
        /// line is called, until Enter. Shared by PORTIONEASEMENT, portion exclusions and portion
        /// components, so they all ask the same way.
        /// </summary>
        internal static List<PortionStep> GatherCalls(Database db, Transaction tr, Editor ed, EasementSettings es, AcEntity lotEntity,
                                                      IList<Course> lot, double tolerance, out bool cancelled)
        {
            cancelled = false;
            string problem = null;
            ed.WriteMessage("\nGive the calls in the order they are read: for \"the WEST 10 feet of the SOUTH 50 feet\", pick the west line first.");
            var steps = new List<PortionStep>();
            var highlighted = new List<AcEntity>();
            if (lotEntity != null) { highlighted.Add(lotEntity); lotEntity.Highlight(); }
            try
            {
                while (true)
                {
                    PromptStatus status;
                    Autodesk.AutoCAD.Geometry.Point3d click;
                    var entity = EasementCommands.PickCurve(db, tr, ed, steps.Count == 0
                        ? "\nLot line the first distance is measured from: "
                        : "\nLot line the next distance is measured from <done>: ", steps.Count > 0, out status, out click);
                    if (status == PromptStatus.None) break;
                    if (entity == null) { cancelled = true; return null; }

                    var courses = lotEntity != null && entity.Handle == lotEntity.Handle ? lot : EasementCommands.Extract(entity, out problem);
                    if (courses == null) { ed.WriteMessage("\n  " + problem); continue; }
                    var segment = NearestCourse(courses, new P2(click.X, click.Y));
                    var line = courses[segment];
                    if (line.Kind != CourseKind.Line) { ed.WriteMessage("\n  That side is a curve; pick a straight lot line."); continue; }

                    double distance;
                    if (!EasementCommands.Distance(ed, "\nDistance measured at right angles from that line (ft): ", false, out distance)) { cancelled = true; return null; }
                    var guess = PortionBuilder.SideOf(lot, line, tolerance);
                    var side = EasementCommands.Keyword(ed, "\nThe line is the lot's [North/East/South/West] <" + Title(guess) + ">: ",
                                                        Title(guess), "North", "East", "South", "West");
                    if (side == null) { cancelled = true; return null; }
                    steps.Add(new PortionStep { Side = side.ToUpperInvariant(), Distance = distance, Line = line, Handle = entity.Handle.ToString(), Segment = segment });
                    if (lotEntity == null || entity.Handle != lotEntity.Handle) { entity.Highlight(); highlighted.Add(entity); }
                    ed.WriteMessage("\n  So far: {0} ...", PortionBuilder.Describe(steps, es.DistanceDecimals));
                }
            }
            finally
            {
                foreach (var e in highlighted) e.Unhighlight();
            }
            return steps;
        }

        /// <summary>A clicked area as the drafter clicked it.</summary>
        internal sealed class ClickedArea
        {
            public SelectedLocation Poc;
            public List<SelectedLocation> Corners;
            public List<AreaSide> Sides;
            public List<List<Course>> SideCourses;
            public readonly List<GeometrySource> FollowSources = new List<GeometrySource>();
            /// <summary>The objects the tie from the Point of Commencement follows; empty for a straight tie.</summary>
            public List<GeometrySource> TieFollows = new List<GeometrySource>();
        }

        /// <summary>
        /// Point of Commencement, then corners with Follow and Undo, until closed. Shared by
        /// CONSTRUCTIONAREA and area components. Null when cancelled.
        /// </summary>
        internal static ClickedArea GatherCorners(Database db, Transaction tr, Editor ed, double tolerance, string commandName)
        {
            string keyword;
            SelectedLocation poc;
            if (EasementCommands.PickLocation(db, tr, ed, new PromptPointOptions("\nPoint of Commencement -- click the corner the description starts from <none>: ") { AllowNone = true },
                                              out poc, out keyword) != PromptStatus.OK) return null;
            var clicked = new ClickedArea { Poc = poc };

            SelectedLocation first;
            while (true)
            {
                // With a Point of Commencement the tie can follow the line and curve it runs along (Follow).
                var firstOptions = new PromptPointOptions(poc != null && clicked.TieFollows.Count == 0
                    ? "\nFirst corner -- the Point of Beginning [Follow]: "
                    : "\nFirst corner -- the Point of Beginning: ") { AppendKeywordsToMessage = false };
                if (poc != null && clicked.TieFollows.Count == 0) firstOptions.Keywords.Add("Follow");
                var firstStatus = EasementCommands.PickLocation(db, tr, ed, firstOptions, out first, out keyword);
                if (firstStatus == PromptStatus.Keyword && keyword == "Follow")
                {
                    var follows = PickTieFollows(db, tr, ed);
                    if (follows == null || follows.Count == 0) { ed.WriteMessage("\n  Nothing selected; the tie is straight."); continue; }
                    clicked.TieFollows = follows;
                    continue;
                }
                if (firstStatus != PromptStatus.OK || first == null)
                {
                    ed.WriteMessage("\n" + commandName + ": cancelled.\n");
                    return null;
                }
                if (clicked.TieFollows.Count > 0)
                {
                    string tieProblem;
                    var tie = TiePath(db, tr, poc.Point, first.Point, clicked.TieFollows, tolerance, string.Empty, null, out tieProblem);
                    if (tie == null)
                    {
                        ed.WriteMessage("\n  The tie cannot follow those objects: {0} Select them again (Follow), or click the Point of Beginning for a straight tie.", tieProblem);
                        clicked.TieFollows = new List<GeometrySource>();
                        continue;
                    }
                    ed.WriteMessage("\n  The tie follows the line as drawn: {0}.", string.Join(", ", tie.Select(c => c.Kind == CourseKind.Arc
                        ? "curve " + c.Length.ToString("0.00", CultureInfo.InvariantCulture) + "' R " + c.Radius.ToString("0.00", CultureInfo.InvariantCulture)
                        : "line " + c.Length.ToString("0.00", CultureInfo.InvariantCulture) + "'").ToArray()));
                    foreach (var c in tie) Preview(ed, c);
                }
                break;
            }

            var corners = new List<SelectedLocation> { first };
            var sides = new List<AreaSide>();
            var sideCourses = new List<List<Course>>();
            var closed = false;
            while (!closed)
            {
                var last = corners[corners.Count - 1];
                var canClose = corners.Count >= 3 || sideCourses.Any(s => s.Count > 1 || s.Any(c => c.Kind == CourseKind.Arc));
                var options = new PromptPointOptions("\nNext corner [Follow/Undo]" + (canClose ? " <close>" : string.Empty) + ": ")
                {
                    AllowNone = canClose, UseBasePoint = true, BasePoint = EasementCommands.ToUcs(ed, last.Point), AppendKeywordsToMessage = false
                };
                options.Keywords.Add("Follow");
                options.Keywords.Add("Undo");

                SelectedLocation next;
                var status = EasementCommands.PickLocation(db, tr, ed, options, out next, out keyword);
                if (status == PromptStatus.Cancel) { ed.WriteMessage("\n" + commandName + ": cancelled.\n"); return null; }
                if (status == PromptStatus.Keyword && keyword == "Undo")
                {
                    if (sides.Count == 0) { ed.WriteMessage("\n  Nothing to undo."); continue; }
                    corners.RemoveAt(corners.Count - 1);
                    sides.RemoveAt(sides.Count - 1);
                    sideCourses.RemoveAt(sideCourses.Count - 1);
                    ed.Regen();
                    continue;
                }

                AreaSide side;
                List<Course> courses;
                if (status == PromptStatus.Keyword && keyword == "Follow")
                {
                    PromptStatus followStatus;
                    Autodesk.AutoCAD.Geometry.Point3d followClick;
                    var entity = EasementCommands.PickCurve(db, tr, ed, "\nLine or curve this side follows: ", false, out followStatus, out followClick);
                    if (entity == null) continue;
                    string problem;
                    var path = EasementCommands.Extract(entity, out problem);
                    if (path == null) { ed.WriteMessage("\n  " + problem); continue; }

                    var leaveOptions = new PromptPointOptions("\nCorner where the side leaves that line: ") { UseBasePoint = true, BasePoint = EasementCommands.ToUcs(ed, last.Point) };
                    if (EasementCommands.PickLocation(db, tr, ed, leaveOptions, out next, out keyword) != PromptStatus.OK || next == null) continue;
                    courses = AreaPath.Between(path, last.Point, next.Point, tolerance, out problem);
                    if (courses == null) { ed.WriteMessage("\n  " + problem); continue; }
                    side = new AreaSide { FollowHandle = entity.Handle.ToString(), FollowType = EasementCommands.TypeName(entity) };
                    var role = "SIDE " + (sides.Count + 1).ToString(CultureInfo.InvariantCulture) + " FOLLOWS";
                    clicked.FollowSources.Add(new GeometrySource { Handle = side.FollowHandle, EntityType = side.FollowType, Role = role, Fingerprint = EasementAnnotation.Fingerprint(path) });
                }
                else if (next == null)
                {
                    // Close straight back to the Point of Beginning.
                    side = new AreaSide();
                    courses = new List<Course> { Course.Line(last.Point, first.Point) };
                    sides.Add(side);
                    sideCourses.Add(courses);
                    break;
                }
                else
                {
                    if (next.Point.DistanceTo(last.Point) <= tolerance) { ed.WriteMessage("\n  That is the same point as the last corner."); continue; }
                    side = new AreaSide();
                    courses = new List<Course> { Course.Line(last.Point, next.Point) };
                }

                foreach (var c in courses) Preview(ed, c);
                sides.Add(side);
                sideCourses.Add(courses);
                if (next.Point.DistanceTo(first.Point) <= tolerance && corners.Count >= 2) closed = true;   // clicked back on the beginning
                else corners.Add(next);
            }
            clicked.Corners = corners;
            clicked.Sides = sides;
            clicked.SideCourses = sideCourses;
            return clicked;
        }
        [CommandMethod("FTFPORTION", CommandFlags.Modal)]
        public void FtfPortion() { PortionEasement(); }

        private static string Title(string word)
        {
            return word.Substring(0, 1).ToUpperInvariant() + word.Substring(1).ToLowerInvariant();
        }

        private static int NearestCourse(IList<Course> courses, P2 at)
        {
            var best = 0;
            var bestDistance = double.MaxValue;
            for (var i = 0; i < courses.Count; i++)
            {
                double along;
                var d = courses[i].Closest(at, out along).DistanceTo(at);
                if (d < bestDistance) { bestDistance = d; best = i; }
            }
            return best;
        }

        private static bool FinishPortion(Database db, Transaction tr, Editor ed, FtfSettings settings, string version, EasementRecord record, IList<Course> lot)
        {
            var es = settings.Easements;
            var tolerance = es.ToleranceFt * settings.General.UnitsPerFoot;
            var result = PortionBuilder.Build(lot, record.PortionSteps, tolerance);
            foreach (var w in result.Warnings) ed.WriteMessage("\n  ! " + w);
            if (!result.Ok)
            {
                foreach (var e in result.Errors) ed.WriteMessage("\n  X " + e);
                ed.WriteMessage("\nNot drawn.\n");
                return false;
            }

            var upf = settings.General.UnitsPerFoot;
            record.Title = record.Purpose + " EASEMENT";
            record.Width = WidthSpec.Sides(0, 0);
            record.BoundaryCourses = EasementAnnotation.Number(result.Loop, es);
            record.RouteCourses = new List<CourseData>();
            record.AreaSquareFeet = result.Area / (upf * upf);
            record.Acres = record.AreaSquareFeet / EasementAnnotation.SquareFeetPerAcre;
            record.Warnings = result.Warnings.ToList();
            record.RouteSources = new List<GeometrySource> { record.Parcel };
            if (record.LotLines != null) record.RouteSources.AddRange(record.LotLines);
            for (var i = 0; i < record.PortionSteps.Count; i++)
            {
                var s = record.PortionSteps[i];
                if (s.Handle != record.Parcel.Handle && (record.LotLines == null || record.LotLines.All(l => l.Handle != s.Handle)))
                    record.RouteSources.Add(new GeometrySource { Handle = s.Handle, EntityType = "Line", Role = "PORTION LINE " + (i + 1), Fingerprint = EasementAnnotation.Fingerprint(new[] { s.Line }) });
            }

            // Exclusions and further components, when the easement has them.
            var composed = Compose(db, tr, ed, settings, record, result.Loop);
            if (composed == null) return false;

            // Drafting: outline, hatch, title and area, and a dimension for each distance.
            var draft = new Drafter(db, tr, ed, settings, version, record);
            var layer = record.IsTemporary ? es.TemporaryLayer : es.BoundaryLayer;
            var pattern = record.IsTemporary ? es.TemporaryHatchPattern : es.HatchPattern;
            var hatchLayer = record.IsTemporary ? es.TemporaryLayer : es.HatchLayer;
            var hatch = record.IsTemporary ? !string.IsNullOrWhiteSpace(pattern) : es.DrawHatch;
            if (composed.Components.Count == 0 && composed.Primary.Holes.Count == 0 && !record.HasComposition)
            {
                var outline = draft.Outline(result.Loop, layer);
                if (hatch) draft.Hatch(outline, pattern, hatchLayer);
            }
            else
                draft.Regions(composed, layer, hatch, pattern, hatchLayer);
            var inside = StripTrim.PointInside(composed.Primary.Outer, tolerance);
            if (!composed.Primary.Contains(inside)) inside = StripTrim.PointInside(result.Loop, tolerance);
            draft.Text(new[] { record.Title }.Concat(EasementAnnotation.AreaLines(record.DisplayAreaSquareFeet, es, record.Purpose)), inside, 0);
            if (es.DrawWidthDimensions)
                foreach (var s in record.PortionSteps)
                {
                    var along = s.Line.StartDirection;
                    var foot = s.Line.Start + along * P2.Dot(inside - s.Line.Start, along);
                    var inward = P2.Dot(inside - foot, along.LeftNormal()) >= 0 ? along.LeftNormal() : along.LeftNormal() * -1;
                    draft.Dimension(foot, foot + inward * s.Distance, along * (draft.TextHeight * 3));
                }
            draft.Finish();
            return true;
        }

        // ======================================================= CONSTRUCTIONAREA

        [CommandMethod("CONSTRUCTIONAREA", CommandFlags.Modal)]
        public void ConstructionArea()
        {
            FtfSession.Run("CONSTRUCTIONAREA", (db, tr, ed) =>
            {
                var rules = FtfSession.Rules(db);
                var settings = FtfSession.SettingsFor(db, rules);
                var es = settings.Easements;
                var tolerance = es.ToleranceFt * settings.General.UnitsPerFoot;

                var record = new EasementRecord { Kind = EasementRecord.AreaKind, Profile = DrawingStore.ReadProfileName(db) };
                var sources = new List<GeometrySource>();

                var clicked = GatherCorners(db, tr, ed, tolerance, "CONSTRUCTIONAREA");
                if (clicked == null) return;
                if (clicked.Poc != null) { record.PointOfCommencement = clicked.Poc; sources.Add(EasementCommands.SourceForLocation(db, tr, clicked.Poc, "POC")); }
                sources.AddRange(clicked.FollowSources);
                record.CommencementTieFollows = clicked.TieFollows.Count > 0 ? clicked.TieFollows : null;
                var corners = clicked.Corners;
                var sides = clicked.Sides;
                var sideCourses = clicked.SideCourses;
                var purpose = ed.GetString(new PromptStringOptions("\nPurpose for the title <" + es.AreaPurpose + ">: ") { AllowSpaces = true });
                if (purpose.Status == PromptStatus.Cancel) return;
                record.Purpose = purpose.Status == PromptStatus.OK && !string.IsNullOrWhiteSpace(purpose.StringResult) ? purpose.StringResult.Trim().ToUpperInvariant() : es.AreaPurpose;
                record.AnglePoints = corners;
                record.AreaSides = sides;
                for (var i = 0; i < corners.Count; i++) sources.Add(EasementCommands.SourceForLocation(db, tr, corners[i], EasementCommands.AngleRole(i)));
                record.RouteSources = sources;

                if (!FinishArea(db, tr, ed, settings, rules.Version, record, sideCourses)) return;
                DrawingStore.SaveEasement(db, tr, record);
                ed.WriteMessage("\nCONSTRUCTIONAREA: {0}, {1}. Stored; FTFEASEMENTLEGAL writes its draft description.\n",
                                record.Title, EasementAnnotation.AreaOfAreaLine(record.AreaSquareFeet, es, record.Purpose));
            });
        }

        [CommandMethod("FTFAREA", CommandFlags.Modal)]
        public void FtfArea() { ConstructionArea(); }

        private static void Preview(Editor ed, Course c)
        {
            var steps = c.Kind == CourseKind.Arc ? Math.Max(2, (int)Math.Ceiling(c.Sweep / (Math.PI / 36))) : 1;
            for (var i = 0; i < steps; i++)
                ed.DrawVector(EasementCommands.ToUcs(ed, c.PointAt(c.Length * i / steps)), EasementCommands.ToUcs(ed, c.PointAt(c.Length * (i + 1) / steps)), 6, false);
        }

        private static bool FinishArea(Database db, Transaction tr, Editor ed, FtfSettings settings, string version, EasementRecord record,
                                       IList<List<Course>> sideCourses)
        {
            var es = settings.Easements;
            var tolerance = es.ToleranceFt * settings.General.UnitsPerFoot;
            var loop = sideCourses.SelectMany(s => s).Where(c => c.Length > tolerance).ToList();
            var problems = AreaPath.Check(loop, tolerance);
            if (problems.Count > 0)
            {
                foreach (var p in problems) ed.WriteMessage("\n  X " + p);
                ed.WriteMessage("\nNot drawn.\n");
                return false;
            }

            var upf = settings.General.UnitsPerFoot;
            var beginning = record.AnglePoints[0];
            record.Title = EasementAnnotation.AreaTitle(record.Purpose, es);
            record.Width = WidthSpec.Sides(0, 0);
            record.TruePointOfBeginning = beginning;
            record.CommencementTie = null;
            record.CommencementTieCourses = null;
            if (record.PointOfCommencement != null)
            {
                // Straight, or along the line and curve the tie follows -- as drawn, never as a chord.
                string tieProblem;
                var follows = new List<GeometrySource>();
                var tiePath = TiePath(db, tr, record.PointOfCommencement.Point, beginning.Point, record.CommencementTieFollows, tolerance, string.Empty, follows, out tieProblem);
                if (tiePath == null)
                {
                    ed.WriteMessage("\n  X The commencement tie: " + tieProblem + "\nNot drawn.\n");
                    return false;
                }
                CourseData tie;
                List<CourseData> tieCourses;
                Ties.Set(tiePath, out tie, out tieCourses);
                record.CommencementTie = tie;
                record.CommencementTieCourses = tieCourses;
                if (follows.Count > 0)
                {
                    record.CommencementTieFollows = follows;
                    record.RouteSources = (record.RouteSources ?? new List<GeometrySource>()).Where(s => s.Role == null || !s.Role.StartsWith(TieFollowsRole, StringComparison.Ordinal)).Concat(follows).ToList();
                }
            }
            record.RouteCourses = EasementAnnotation.Number(loop, es);
            record.BoundaryCourses = record.RouteCourses;
            record.Terminus = beginning.Point;
            record.AreaSquareFeet = Math.Abs(Loops.SignedArea(loop)) / (upf * upf);
            record.Acres = record.AreaSquareFeet / EasementAnnotation.SquareFeetPerAcre;
            record.Warnings = new List<string>();

            var composed = Compose(db, tr, ed, settings, record, loop);
            if (composed == null) return false;

            var draft = new Drafter(db, tr, ed, settings, version, record);
            if (!record.HasComposition)
            {
                var outline = draft.Outline(loop, es.AreaLayer);
                if (!string.IsNullOrWhiteSpace(es.AreaHatchPattern)) draft.Hatch(outline, es.AreaHatchPattern, es.AreaLayer);
            }
            else
                draft.Regions(composed, es.AreaLayer, !string.IsNullOrWhiteSpace(es.AreaHatchPattern), es.AreaHatchPattern, es.AreaLayer);
            draft.Text(new[] { record.Title, EasementAnnotation.AreaOfAreaLine(record.DisplayAreaSquareFeet, es, record.Purpose) },
                       StripTrim.PointInside(loop, tolerance), 0);

            // Labels per the clicks: each side's courses on the line (outside the area) or in the table.
            var plan = EasementAnnotation.PlanLabels(record.TieCourses(), loop, null, es, draft.TextHeight, EasementCommands.DegreeSymbol);
            var outward = Loops.SignedArea(loop) > 0 ? -1.0 : 1.0;    // counter-clockwise: outside is on the right
            foreach (var label in plan)
            {
                var c = label.Data.Course;
                var lines = label.InTable ? new List<string> { label.Data.Id } : label.Lines.ToList();
                var mid = c.PointAt(c.Length / 2);
                var dir = c.DirectionAt(c.Length / 2);
                var sign = label.IsTie ? 1.0 : outward;
                var at = mid + dir.LeftNormal() * (sign * draft.TextHeight * (0.9 * lines.Count + 0.4));
                draft.Text(lines, at, EasementCommands.Readable(Math.Atan2(dir.Y, dir.X)));
            }
            var rows = plan.Where(p => p.InTable).Select(p => p.Data).ToList();
            if (rows.Count > 0) draft.Tables(rows, loop);

            if (es.DrawPointLabels)
            {
                var first = loop[0];
                var awayFromArea = new P2(first.StartDirection.Y, -first.StartDirection.X) * outward * -1 - first.StartDirection;
                draft.Leader(es.BeginningLabel, beginning.Point, awayFromArea.Normalized());
                if (record.PointOfCommencement != null)
                {
                    var poc = record.PointOfCommencement.Point;
                    var tie = record.TieCourses();
                    var away = tie.Count > 0 && tie[0].Course.Length > 1e-6 ? tie[0].Course.StartDirection * -1
                             : poc.DistanceTo(beginning.Point) > 1e-6 ? (poc - beginning.Point).Normalized() : awayFromArea.Normalized();
                    draft.Leader(es.CommencementLabel, poc, away);
                }
            }
            draft.Finish();
            return true;
        }

        /// <summary>Exclusions and components on top of the built boundary; null (with messages) when they cannot be resolved.</summary>
        private static EasementComposition.Composed Compose(Database db, Transaction tr, Editor ed, FtfSettings settings, EasementRecord record, IList<Course> loop)
        {
            var composed = record.HasComposition
                ? EasementComposition.Apply(db, tr, record, loop, settings)
                : new EasementComposition.Composed { Primary = new RegionShape(loop) };
            foreach (var w in composed.Warnings) ed.WriteMessage("\n  ! " + w);
            if (!composed.Ok)
            {
                foreach (var e in composed.Errors) ed.WriteMessage("\n  X " + e);
                ed.WriteMessage("\nNot drawn.\n");
                return null;
            }
            if (record.HasComposition) EasementComposition.Store(record, composed, settings);
            return composed;
        }

        // ================================================================ rebuild

        /// <summary>
        /// Rebuilds a portion easement or clicked area from the current drawing. The new
        /// geometry is worked out first; the old drafting is replaced only when it is valid.
        /// Returns the rebuilt record, or null with a reason.
        /// </summary>
        internal static EasementRecord Rebuild(Database db, Transaction tr, Editor ed, FtfSettings settings, string version,
                                               EasementRecord old, out string problem)
        {
            problem = null;
            var es = settings.Easements;
            var tolerance = es.ToleranceFt * settings.General.UnitsPerFoot;
            var record = new EasementRecord
            {
                Id = old.Id, CreatedUtc = old.CreatedUtc, Kind = old.Kind, Purpose = old.Purpose, Profile = DrawingStore.ReadProfileName(db),
                GroupId = old.GroupId, Legal = old.Legal, Role = old.Role, ExhibitGroup = old.ExhibitGroup,
                Exclusions = old.Exclusions, Components = old.Components, RebuiltUtc = DateTime.UtcNow,
                CommencementTieFollows = old.CommencementTieFollows, LotLines = old.LotLines,
                HatchScales = old.HatchScales, HatchPatternScaleByHand = old.HatchPatternScaleByHand,
                LegalStatus = old.Legal != null ? "DRAFT OUT OF DATE - rebuilt after survey changes; write the draft again with FTFEASEMENTLEGAL" : old.LegalStatus
            };

            if (old.IsPortion)
            {
                var refreshedLines = new List<GeometrySource>();
                var lot = LotCourses(db, tr, old, tolerance, out problem, refreshedLines);
                if (lot == null) return null;
                var lotEntity = old.LotLines != null && old.LotLines.Count > 0 ? null : EasementCommands.Resolve(db, tr, old.Parcel.Handle);
                record.Parcel = new GeometrySource { Handle = old.Parcel.Handle, EntityType = old.Parcel.EntityType, Role = LotRole, Fingerprint = EasementAnnotation.Fingerprint(lot) };
                record.LotLines = refreshedLines.Count > 0 ? refreshedLines : null;
                record.PortionSteps = new List<PortionStep>();
                foreach (var s in old.PortionSteps)
                {
                    var entity = lotEntity != null && s.Handle == old.Parcel.Handle ? lotEntity : EasementCommands.Resolve(db, tr, s.Handle);
                    var courses = entity == null ? null : (lotEntity != null && entity == lotEntity ? lot : EasementCommands.Extract(entity, out problem));
                    if (courses == null || s.Segment >= courses.Count) { problem = "the " + s.Side.ToLowerInvariant() + " line was erased or changed shape."; return null; }
                    record.PortionSteps.Add(new PortionStep { Side = s.Side, Distance = s.Distance, Line = courses[s.Segment], Handle = s.Handle, Segment = s.Segment });
                }
                if (!PortionBuilder.Build(lot, record.PortionSteps, tolerance).Ok) { problem = "the portion is no longer valid for the lot as drawn."; return null; }
                Replace(db, tr, old);
                if (!FinishPortion(db, tr, ed, settings, version, record, lot)) throw new InvalidOperationException("The rebuilt portion is invalid; nothing was changed.");
            }
            else
            {
                var corners = new List<SelectedLocation>();
                var sources = new List<GeometrySource>();
                if (old.PointOfCommencement != null)
                {
                    record.PointOfCommencement = EasementCommands.Refresh(db, tr, old, old.PointOfCommencement, "POC");
                    if (record.PointOfCommencement == null) { problem = "the Point of Commencement object was erased."; return null; }
                    sources.Add(EasementCommands.SourceForLocation(db, tr, record.PointOfCommencement, "POC"));
                }
                for (var i = 0; i < old.AnglePoints.Count; i++)
                {
                    var corner = EasementCommands.Refresh(db, tr, old, old.AnglePoints[i], EasementCommands.AngleRole(i));
                    if (corner == null) { problem = "corner " + (i + 1) + " was erased."; return null; }
                    corners.Add(corner);
                    sources.Add(EasementCommands.SourceForLocation(db, tr, corner, EasementCommands.AngleRole(i)));
                }
                var sideCourses = new List<List<Course>>();
                for (var i = 0; i < old.AreaSides.Count; i++)
                {
                    var from = corners[i].Point;
                    var to = corners[(i + 1) % corners.Count].Point;
                    var side = old.AreaSides[i];
                    if (string.IsNullOrWhiteSpace(side.FollowHandle)) { sideCourses.Add(new List<Course> { Course.Line(from, to) }); continue; }
                    var entity = EasementCommands.Resolve(db, tr, side.FollowHandle);
                    var path = entity == null ? null : EasementCommands.Extract(entity, out problem);
                    if (path == null) { problem = "the line side " + (i + 1) + " follows was erased."; return null; }
                    var courses = AreaPath.Between(path, from, to, tolerance, out problem);
                    if (courses == null) { problem = "side " + (i + 1) + ": " + problem; return null; }
                    sideCourses.Add(courses);
                    sources.Add(new GeometrySource { Handle = side.FollowHandle, EntityType = side.FollowType, Role = "SIDE " + (i + 1) + " FOLLOWS", Fingerprint = EasementAnnotation.Fingerprint(path) });
                }
                if (AreaPath.Check(sideCourses.SelectMany(s => s).ToList(), tolerance).Count > 0) { problem = "the area's sides now cross or do not close."; return null; }
                record.AnglePoints = corners;
                record.AreaSides = old.AreaSides.ToList();
                record.RouteSources = sources;
                Replace(db, tr, old);
                if (!FinishArea(db, tr, ed, settings, version, record, sideCourses)) throw new InvalidOperationException("The rebuilt area is invalid; nothing was changed.");
            }

            record.Warnings.Add(string.Format(CultureInfo.InvariantCulture, "Rebuilt {0:yyyy-MM-dd HH:mm} UTC after survey changes; area was {1:N0} sq ft.", DateTime.UtcNow, old.AreaSquareFeet));
            DrawingStore.SaveEasement(db, tr, record);
            return record;
        }

        private static void Replace(Database db, Transaction tr, EasementRecord old)
        {
            Ownership.DeleteOwned(db, tr, s => s.PointNumber == old.Id && s.Kind >= FtfEntityKind.EasementBoundary && s.Kind <= FtfEntityKind.EasementTable);
        }

        // ================================================================ drafting

        /// <summary>Draws and stamps one record's entities on the profile's layers.</summary>
        private sealed class Drafter
        {
            private readonly Database _db;
            private readonly Transaction _tr;
            private readonly Editor _ed;
            private readonly FtfSettings _settings;
            private readonly string _version;
            private readonly EasementRecord _record;
            private readonly ObjectId _style;
            private readonly List<string> _drafted = new List<string>();

            public double TextHeight { get; private set; }

            public Drafter(Database db, Transaction tr, Editor ed, FtfSettings settings, string version, EasementRecord record)
            {
                _db = db; _tr = tr; _ed = ed; _settings = settings; _version = version; _record = record;
                TextHeight = settings.Easements.TextHeightPlotted * CadUtil.DrawingUnitsPerPlottedUnit(db);
                _style = Setup.DrawingResources.FindTextStyle(db, tr, settings.Easements.TextStyle);
            }

            private void Add(AcEntity entity, FtfEntityKind kind, string layer)
            {
                entity.LayerId = ProductionLayers.Get(_db, _tr, layer, _settings);
                CadUtil.AddToModelSpace(_db, _tr, entity);
                Ownership.Stamp(entity, _record.Id, _version, kind, null, _record.Title);
                _drafted.Add(entity.Handle.ToString());
            }

            public Polyline Outline(IList<Course> loop, string layer)
            {
                var outline = EasementCommands.ToPolyline(loop, true);
                Add(outline, FtfEntityKind.EasementBoundary, layer);
                return outline;
            }

            public void Hatch(Polyline outline, string pattern, string layer)
            {
                try
                {
                    var hatch = new Hatch();
                    hatch.SetDatabaseDefaults(_db);
                    Add(hatch, FtfEntityKind.EasementHatch, layer);
                    hatch.PatternScale = _settings.Easements.HatchScale * CadUtil.DrawingUnitsPerPlottedUnit(_db);
                    hatch.SetHatchPattern(HatchPatternType.PreDefined, pattern);
                    hatch.Associative = false;
                    hatch.AppendLoop(HatchLoopTypes.External, new ObjectIdCollection { outline.ObjectId });
                    hatch.EvaluateHatch(true);
                    var ms = CadUtil.ModelSpace(_db, _tr, OpenMode.ForRead);
                    ((DrawOrderTable)_tr.GetObject(ms.DrawOrderTableId, OpenMode.ForWrite)).MoveToBottom(new ObjectIdCollection { hatch.ObjectId });
                }
                catch (Autodesk.AutoCAD.Runtime.Exception ex)
                {
                    _ed.WriteMessage("\n  Hatch \"{0}\" could not be made ({1}); the outline is drawn without it.", pattern, ex.Message);
                }
            }

            public void Text(IEnumerable<string> lines, P2 at, double rotation)
            {
                var text = new MText();
                text.SetDatabaseDefaults(_db);
                if (!_style.IsNull) text.TextStyleId = _style;
                text.TextHeight = TextHeight;
                text.Attachment = AttachmentPoint.MiddleCenter;
                text.Location = new Point3d(at.X, at.Y, 0);
                text.Rotation = rotation;
                text.Contents = string.Join("\\P", lines.Where(l => !string.IsNullOrEmpty(l)).Select(EasementCommands.Escape).ToArray());
                CadUtil.Mask(text, _settings.Easements.LabelMask);
                Add(text, FtfEntityKind.EasementText, _settings.Easements.TextLayer);
            }

            public void Dimension(P2 from, P2 to, P2 lineOffset)
            {
                var es = _settings.Easements;
                var style = _db.Dimstyle;
                if (!string.IsNullOrWhiteSpace(es.DimensionStyleOverride))
                {
                    var table = (DimStyleTable)_tr.GetObject(_db.DimStyleTableId, OpenMode.ForRead);
                    if (table.Has(es.DimensionStyleOverride)) style = table[es.DimensionStyleOverride];
                }
                var line = from + lineOffset;
                var dim = new AlignedDimension(new Point3d(from.X, from.Y, 0), new Point3d(to.X, to.Y, 0), new Point3d(line.X, line.Y, 0), string.Empty, style);
                dim.SetDatabaseDefaults(_db);
                dim.DimensionStyle = style;
                Add(dim, FtfEntityKind.EasementDimension, es.DimensionLayer);
            }

            public void Tables(IList<CourseData> rows, IList<Course> boundary)
            {
                EasementCommands.Tables(_db, _tr, _ed, rows, boundary, _settings.Easements, TextHeight, (e, k, l) => Add(e, k, l), null);
            }

            public void Leader(string text, P2 point, P2 away)
            {
                if (string.IsNullOrWhiteSpace(text)) return;
                var at = point + away * (TextHeight * 8.0);
                var entity = CadUtil.NewLeaderedLabel(_db, _tr, EasementCommands.Escape(text), TextHeight, _style,
                                                      ProductionLayers.Get(_db, _tr, _settings.Easements.TextLayer, _settings),
                                                      new Point3d(at.X, at.Y, 0), new Point3d(point.X, point.Y, 0),
                                                      ObjectId.Null, _settings.Easements.LabelMask);
                Ownership.Stamp(entity, _record.Id, _version, FtfEntityKind.EasementText, null, _record.Title);
                _drafted.Add(entity.Handle.ToString());
            }

            public void Regions(EasementComposition.Composed composed, string layer, bool hatch, string pattern, string hatchLayer)
            {
                EasementComposition.DraftRegions(_db, _tr, _ed, composed, Add, layer, hatch, pattern, hatchLayer,
                    _settings.Easements.HatchScale * CadUtil.DrawingUnitsPerPlottedUnit(_db), _settings.Easements.TextLayer, _style, TextHeight,
                    _settings.Easements.ToleranceFt * _settings.General.UnitsPerFoot, _settings.Easements.LabelMask);
            }

            public void Finish()
            {
                _record.DraftedHandles = _drafted.ToList();
                _record.DraftedFingerprint = EasementAnnotation.Fingerprint(_record.BoundaryCourses.Select(d => d.Course));
            }
        }
    }
}
