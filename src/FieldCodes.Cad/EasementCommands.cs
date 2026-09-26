using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using FieldCodes.Easements;
using FieldCodes.Settings;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcEntity = Autodesk.AutoCAD.DatabaseServices.Entity;
using Alignment = Autodesk.Civil.DatabaseServices.Alignment;
using CogoPoint = Autodesk.Civil.DatabaseServices.CogoPoint;

namespace FieldCodes.Cad
{
    /// <summary>
    /// Strip easements: a controlling route, a width and two terminations become
    /// one exact closed boundary (arcs stay arcs), drafted on the profile's layers
    /// and remembered in the drawing so it can be checked against later survey
    /// changes. The selected survey geometry is only ever read.
    ///
    /// The geometry is unit tested in FieldCodes.Easements; the CAD glue here was
    /// exercised against test drawings only as far as the implementation report
    /// states.
    /// </summary>
    public sealed class EasementCommands
    {
        internal const string DegreeSymbol = "%%d";

        // ----------------------------------------------------------------- inputs

        /// <summary>Everything the user chose, in a form a rebuild can re-read from
        /// the drawing.</summary>
        internal sealed class EasementJob
        {
            public string Purpose;
            public SelectedLocation Poc;
            public SelectedLocation Tpob;
            public readonly List<GeometrySource> Sources = new List<GeometrySource>();
            public readonly List<IList<Course>> RoutePieces = new List<IList<Course>>();
            public WidthSpec Width;
            public TerminationSpec Begin;
            public TerminationSpec End;
            public GeometrySource Parcel;
            public List<Course> ParcelCourses;

            /// <summary>Where the line table sat before a rebuild; null asks the user.</summary>
            public Point3d? TablePosition;

            // Easements built from angle points and trim lines.
            public bool Trimmable;
            public readonly List<SelectedLocation> AnglePoints = new List<SelectedLocation>();
            public readonly List<IList<Course>> Trims = new List<IList<Course>>();
            public readonly List<GeometrySource> TrimSources = new List<GeometrySource>();

            /// <summary>Points inside the pieces to keep, from an earlier build; null asks.</summary>
            public List<P2> KeepPoints;

            /// <summary>The corner the terminus is tied to, if any.</summary>
            public SelectedLocation TerminusCorner;

            /// <summary>This job is a temporary construction easement (on a rebuild).</summary>
            public bool Temporary;

            /// <summary>A temporary construction easement on the same centerline, when asked for.</summary>
            public WidthSpec TemporaryWidth;
        }

        /// <summary>What the preview shows, and what the drafter picked in it.</summary>
        internal sealed class TrimPreview
        {
            public TrimResult Split;
            public List<Course> Route;
            public List<P2> AnglePoints;
            public List<IList<Course>> Trims;
            public WidthSpec Width;
            public string Purpose;
            public List<string> Notes;
            public double Tolerance;
            public double UnitsPerFoot;

            /// <summary>The office settings, as the profile has them: what the preview starts from.</summary>
            public EasementSettings Settings;

            /// <summary>The settings this easement will actually be drafted with -- a separate copy the
            /// preview's drafting panel edits. The office settings are never changed.</summary>
            public EasementSettings Drafting;

            /// <summary>Drawing units per plotted unit, so the preview shows text and hatch at the size
            /// they will really be drawn at on this drawing's annotation scale.</summary>
            public double PlotScale;

            /// <summary>The temporary construction easement, split by the same lines; null when there is none.</summary>
            public TrimResult TemporarySplit;
            public WidthSpec TemporaryWidth;

            public P2? Commencement;
            public P2? TerminusCorner;

            public List<int> Keep;
        }

        // =========================================================== STRIPEASEMENT

        /// <summary>
        /// Builds a strip easement: click its angle points, give the width, click the
        /// lines it is trimmed to, then keep the right pieces in the preview. A temporary
        /// construction easement on the same centerline is built alongside when asked.
        /// </summary>
        [CommandMethod("STRIPEASEMENT", CommandFlags.Modal)]
        public void StripEasement()
        {
            FtfSession.Run("STRIPEASEMENT", (db, tr, ed) =>
            {
                var rules = FtfSession.Rules(db);
                var settings = FtfSession.SettingsFor(db, rules);
                var es = settings.Easements;

                var job = new EasementJob { Trimmable = true, Purpose = es.DefaultPurpose, Begin = TerminationSpec.Perpendicular(), End = TerminationSpec.Perpendicular() };
                if (!Gather(db, tr, ed, job)) { ed.WriteMessage("\nSTRIPEASEMENT: cancelled -- nothing drawn.\n"); return; }

                var record = new EasementRecord { Profile = DrawingStore.ReadProfileName(db) };
                var temporary = job.TemporaryWidth != null ? new EasementRecord { Profile = record.Profile } : null;
                if (!BuildAndDraft(db, tr, ed, settings, rules.Version, job, record, temporary)) return;

                foreach (var r in new[] { record, temporary }.Where(r => r != null))
                {
                    DrawingStore.SaveEasement(db, tr, r);
                    ed.WriteMessage("\nSTRIPEASEMENT: {0}, {1}.", r.Title, string.Join(", ", EasementAnnotation.AreaLines(r.AreaSquareFeet, es, r.Purpose).ToArray()));
                }
                ed.WriteMessage("\nStored in the drawing; FTFEASEMENTCHECK finds it if the survey changes.\n");
            });
        }

        /// <summary>Same command under the FTF prefix.</summary>
        [CommandMethod("FTFEASEMENT", CommandFlags.Modal)]
        public void FtfEasement() { StripEasement(); }

        private const short RouteColor = 1;    // red, like an easement line on a markup

        private static bool Gather(Database db, Transaction tr, Editor ed, EasementJob job)
        {
            // 0. Point of Commencement, optional.
            SelectedLocation poc;
            string keyword;
            var pocStatus = PickLocation(db, tr, ed, new PromptPointOptions("\nPoint of Commencement -- click the corner the description starts from <none>: ") { AllowNone = true },
                                         out poc, out keyword);
            if (pocStatus != PromptStatus.OK) return false;
            if (poc != null)
            {
                job.Poc = poc;
                job.Sources.Add(SourceForLocation(db, tr, poc, "POC"));
            }

            // 1. The easement line: angle points in order, or an existing line / curve.
            ed.WriteMessage("\nClick the easement line's angle points in order, starting at the Point of Beginning end -- snaps work, and it can run past the lot lines.");
            while (true)
            {
                var count = job.AnglePoints.Count;
                var options = new PromptPointOptions(count == 0
                    ? "\nFirst angle point [Object]: "
                    : string.Format(CultureInfo.InvariantCulture, "\nAngle point {0}{1}: ", count + 1, count >= 2 ? " <done>" : string.Empty));
                if (count == 0)
                {
                    options.Keywords.Add("Object");
                    options.AppendKeywordsToMessage = false;
                }
                else
                {
                    options.UseBasePoint = true;
                    options.BasePoint = ToUcs(ed, job.AnglePoints[count - 1].Point);
                    options.AllowNone = count >= 2;
                }

                SelectedLocation location;
                var status = PickLocation(db, tr, ed, options, out location, out keyword);
                if (status == PromptStatus.Keyword) return PickRouteObject(db, tr, ed, job) && GatherRest(db, tr, ed, job);
                if (status == PromptStatus.Cancel) return false;
                if (location == null) break;
                if (count > 0 && location.Point.DistanceTo(job.AnglePoints[count - 1].Point) < 1e-6)
                {
                    ed.WriteMessage("\n  That is the same point as the last one.");
                    continue;
                }
                if (count > 0)
                    ed.DrawVector(ToUcs(ed, job.AnglePoints[count - 1].Point), ToUcs(ed, location.Point), RouteColor, false);
                job.AnglePoints.Add(location);
            }

            var courses = new List<Course>();
            for (var i = 1; i < job.AnglePoints.Count; i++)
                courses.Add(Course.Line(job.AnglePoints[i - 1].Point, job.AnglePoints[i].Point));
            job.RoutePieces.Add(courses);
            job.Tpob = job.AnglePoints[0];
            for (var i = 0; i < job.AnglePoints.Count; i++)
                job.Sources.Add(SourceForLocation(db, tr, job.AnglePoints[i], AngleRole(i)));
            return GatherRest(db, tr, ed, job);
        }

        internal static string AngleRole(int index)
        {
            return "ANGLE POINT " + (index + 1).ToString(CultureInfo.InvariantCulture);
        }

        private const string TerminusTieRole = "TERMINUS TIE";

        private static bool PickRouteObject(Database db, Transaction tr, Editor ed, EasementJob job)
        {
            var options = new PromptEntityOptions("\nSelect the easement line (line, polyline, arc or alignment), near the end it starts from: ");
            options.SetRejectMessage("\nSelect a line, polyline, arc or alignment.");
            options.AddAllowedClass(typeof(Line), true);
            options.AddAllowedClass(typeof(Polyline), true);
            options.AddAllowedClass(typeof(Arc), true);
            options.AddAllowedClass(typeof(Polyline3d), true);
            options.AddAllowedClass(typeof(Alignment), true);
            var picked = ed.GetEntity(options);
            if (picked.Status != PromptStatus.OK) return false;

            string problem;
            var entity = (AcEntity)tr.GetObject(picked.ObjectId, OpenMode.ForRead);
            var courses = Extract(entity, out problem);
            if (courses == null) { ed.WriteMessage("\n  " + problem); return false; }

            // It starts at the end nearer where it was clicked.
            var click = picked.PickedPoint.TransformBy(ed.CurrentUserCoordinateSystem);
            var near = new P2(click.X, click.Y);
            var list = courses.ToList();
            if (near.DistanceTo(list[list.Count - 1].End) < near.DistanceTo(list[0].Start)) list = EasementBuilder.Reverse(list);

            job.RoutePieces.Add(list);
            job.Tpob = new SelectedLocation { X = list[0].Start.X, Y = list[0].Start.Y, Source = LocationSource.GeometryEndpoint, Handle = entity.Handle.ToString() };
            job.Sources.Add(new GeometrySource { Handle = entity.Handle.ToString(), EntityType = TypeName(entity), Role = "ROUTE", Fingerprint = EasementAnnotation.Fingerprint(courses) });
            ed.WriteMessage("\n  {0}: {1} course(s), starting at N {2:0.00} E {3:0.00}.", TypeName(entity), list.Count, list[0].Start.Y, list[0].Start.X);
            return true;
        }

        private static bool GatherRest(Database db, Transaction tr, Editor ed, EasementJob job)
        {
            // 2. Width, and the temporary construction easement's width if there is one.
            var mode = Keyword(ed, "\nWidth [Centered/Sides] <Centered>: ", "Centered", "Centered", "Sides");
            if (mode == null) return false;
            if (mode == "Centered")
            {
                double total;
                if (!Distance(ed, "\nTotal easement width (ft): ", false, out total)) return false;
                job.Width = WidthSpec.Centered(total);

                double? temporary;
                if (!OptionalDistance(ed, "\nTemporary construction easement total width (ft) <none>: ", out temporary)) return false;
                if (temporary.HasValue) job.TemporaryWidth = WidthSpec.Centered(temporary.Value);
            }
            else
            {
                double left, right;
                ed.WriteMessage("\nLeft and right are measured looking along the easement line from its first point. Enter 0 for a one-sided easement.");
                if (!Distance(ed, "\nWidth LEFT of the line (ft): ", true, out left)) return false;
                if (!Distance(ed, "\nWidth RIGHT of the line (ft): ", true, out right)) return false;
                job.Width = WidthSpec.Sides(left, right);

                double? tempLeft, tempRight = null;
                if (!OptionalDistance(ed, "\nTemporary construction easement width LEFT of the line (ft) <none>: ", out tempLeft)) return false;
                if (tempLeft.HasValue)
                {
                    if (!OptionalDistance(ed, "\nTemporary construction easement width RIGHT of the line (ft): ", out tempRight)) return false;
                    job.TemporaryWidth = WidthSpec.Sides(tempLeft.Value, tempRight ?? job.Width.Right);
                }
            }
            if (job.TemporaryWidth != null &&
                (job.TemporaryWidth.Left < job.Width.Left - 1e-9 || job.TemporaryWidth.Right < job.Width.Right - 1e-9 ||
                 job.TemporaryWidth.Total <= job.Width.Total + 1e-9))
            {
                ed.WriteMessage("\n  The temporary construction easement must be wider than the easement on each side it covers.");
                return false;
            }

            // 3. Trim lines, any number.
            var highlighted = new List<AcEntity>();
            try
            {
                while (true)
                {
                    var options = new PromptEntityOptions(job.Trims.Count == 0
                        ? "\nClick each line the easement is trimmed to -- lot lines, right-of-way -- then Enter <none>: "
                        : "\nNext trim line <done>: ");
                    options.SetRejectMessage("\nSelect a line, polyline or arc.");
                    options.AddAllowedClass(typeof(Curve), false);
                    options.AllowNone = true;
                    var picked = ed.GetEntity(options);
                    if (picked.Status == PromptStatus.None) break;
                    if (picked.Status != PromptStatus.OK) return false;

                    var entity = (AcEntity)tr.GetObject(picked.ObjectId, OpenMode.ForRead);
                    string problem;
                    var courses = Extract(entity, out problem);
                    if (courses == null)
                    {
                        // On production drawings a Civil 3D label or text often sits on the lot line: use the line under the pick.
                        var under = CurveUnder(db, tr, ed, picked.PickedPoint.TransformBy(ed.CurrentUserCoordinateSystem), entity.ObjectId);
                        if (under == null) { ed.WriteMessage("\n  " + problem); continue; }
                        ed.WriteMessage("\n  The pick was on a {0}; using the {1} under it.", TypeName(entity), TypeName(under));
                        entity = under;
                        courses = Extract(entity, out problem);
                    }
                    var handle = entity.Handle.ToString();
                    if (job.TrimSources.Any(s => s.Handle == handle)) { ed.WriteMessage("\n  That line is already picked."); continue; }

                    var source = new GeometrySource
                    {
                        Handle = handle, EntityType = TypeName(entity),
                        Role = "TRIM LINE " + (job.Trims.Count + 1).ToString(CultureInfo.InvariantCulture),
                        Fingerprint = EasementAnnotation.Fingerprint(courses)
                    };
                    job.Trims.Add(courses);
                    job.TrimSources.Add(source);
                    job.Sources.Add(source);
                    entity.Highlight();
                    highlighted.Add(entity);
                }
            }
            finally
            {
                foreach (var e in highlighted) e.Unhighlight();
            }

            // 4. A corner the terminus is tied to, optional.
            SelectedLocation corner;
            string keyword;
            var status = PickLocation(db, tr, ed, new PromptPointOptions("\nCorner the terminus is tied to <none>: ") { AllowNone = true }, out corner, out keyword);
            if (status != PromptStatus.OK) return false;
            if (corner != null)
            {
                job.TerminusCorner = corner;
                job.Sources.Add(SourceForLocation(db, tr, corner, TerminusTieRole));
            }
            return true;
        }

        private static bool OptionalDistance(Editor ed, string message, out double? value)
        {
            value = null;
            var result = ed.GetDistance(new PromptDistanceOptions(message) { AllowNegative = false, AllowZero = false, AllowNone = true });
            if (result.Status == PromptStatus.None) return true;
            if (result.Status != PromptStatus.OK || double.IsNaN(result.Value)) return false;
            value = result.Value;
            return true;
        }

        internal static Point3d ToUcs(Editor ed, P2 world)
        {
            return new Point3d(world.X, world.Y, 0).TransformBy(ed.CurrentUserCoordinateSystem.Inverse());
        }

        /// <summary>Which pieces to keep, and the purpose for the title: the preview
        /// window in Civil 3D, or command-line questions where there is no window.</summary>
        private static bool Choose(Editor ed, TrimPreview preview)
        {
            if (Headless())
            {
                ed.WriteMessage("\nThe trim lines divide the easement into {0} piece(s), largest first:", preview.Split.Pieces.Count);
                foreach (var piece in preview.Split.Pieces)
                    ed.WriteMessage("\n  {0}: {1:N0} sq ft around N {2:0.00} E {3:0.00}", piece.Number,
                                    piece.Area / (preview.UnitsPerFoot * preview.UnitsPerFoot), piece.InsidePoint.Y, piece.InsidePoint.X);
                var keep = ed.GetString(new PromptStringOptions("\nKeep which pieces (numbers, comma separated) <1>: ") { AllowSpaces = true });
                if (keep.Status == PromptStatus.Cancel) return false;
                var numbers = new List<int>();
                foreach (var part in (keep.Status == PromptStatus.OK ? keep.StringResult : string.Empty).Split(',', ' '))
                {
                    int n;
                    if (int.TryParse(part.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) numbers.Add(n);
                }
                preview.Keep = numbers.Count > 0 ? numbers : new List<int> { 1 };

                var purpose = ed.GetString(new PromptStringOptions("\nPurpose for the title <" + preview.Purpose + ">: ") { AllowSpaces = true });
                if (purpose.Status == PromptStatus.Cancel) return false;
                if (purpose.Status == PromptStatus.OK && !string.IsNullOrWhiteSpace(purpose.StringResult))
                    preview.Purpose = purpose.StringResult.Trim().ToUpperInvariant();
                return true;
            }

            return ShowPreview(preview);
        }

        // Kept out of Choose so the command line never loads the window's types.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static bool ShowPreview(TrimPreview preview)
        {
            using (var form = new Ui.EasementPreviewForm(preview))
                return AcadApp.ShowModalDialog(form) == System.Windows.Forms.DialogResult.OK;
        }

        private static bool Headless()
        {
            try { return System.Diagnostics.Process.GetCurrentProcess().ProcessName.StartsWith("accoreconsole", StringComparison.OrdinalIgnoreCase); }
            catch (InvalidOperationException) { return false; }
        }

        /// <summary>The pieces of a split that contain any of the points. A point in no
        /// piece is returned in <paramref name="missing"/>.</summary>
        internal static List<int> PiecesAt(TrimResult split, IEnumerable<P2> points, out P2? missing)
        {
            missing = null;
            var keep = new List<int>();
            foreach (var p in points)
            {
                var piece = split.PieceAt(p);
                if (piece == null) { missing = p; continue; }
                if (!keep.Contains(piece.Number)) keep.Add(piece.Number);
            }
            return keep;
        }

        // ================================================================== build

        /// <summary>One strip before drafting: built and, for a trimmable easement, split.</summary>
        private sealed class Shape
        {
            public EasementBuildResult Built;
            public TrimResult Split;
            public List<string> Warnings;
        }

        private static Shape MakeShape(Editor ed, EasementJob job, IList<Course> route, WidthSpec width, double tolerance, string what)
        {
            // An end that stops on a trim line runs on past it, so a skewed lot line is met on both sides.
            var courses = job.Trims.Count > 0 ? StripTrim.ExtendEndsToTrims(route, width, job.Trims, tolerance) : route.ToList();
            var built = EasementBuilder.Build(courses, width, job.Begin, job.End, tolerance);
            // Trimming decides the final width and area; the untrimmed strip's width check would only confuse.
            var warnings = job.Trimmable ? built.Warnings.Where(w => !w.StartsWith("Average width", StringComparison.Ordinal)).ToList() : built.Warnings.ToList();
            foreach (var w in warnings) ed.WriteMessage("\n  ! " + what + w);
            if (!built.Ok)
            {
                foreach (var e in built.Errors) ed.WriteMessage("\n  X " + what + e);
                ed.WriteMessage("\nSTRIPEASEMENT: not drawn -- the easement geometry is invalid.\n");
                return null;
            }

            var shape = new Shape { Built = built, Warnings = warnings };
            if (!job.Trimmable) return shape;

            shape.Split = StripTrim.Split(built.Boundary, job.Trims, tolerance);
            foreach (var w in shape.Split.Warnings) ed.WriteMessage("\n  ! " + what + w);
            if (!shape.Split.Ok)
            {
                foreach (var e in shape.Split.Errors) ed.WriteMessage("\n  X " + what + e);
                ed.WriteMessage("\nSTRIPEASEMENT: not drawn.\n");
                return null;
            }
            shape.Warnings.AddRange(shape.Split.Warnings);
            return shape;
        }

        /// <summary>
        /// Builds, validates and drafts an easement into its record -- and, when the job has
        /// a temporary construction easement and a record for it, that one too, on the same
        /// centerline, trim lines and kept pieces. Returns false, with nothing drawn, when
        /// the geometry is invalid.
        /// </summary>
        internal static bool BuildAndDraft(Database db, Transaction tr, Editor ed, FtfSettings settings, string rulesVersion,
                                           EasementJob job, EasementRecord record, EasementRecord temporaryRecord)
        {
            var es = settings.Easements;
            var tolerance = es.ToleranceFt * settings.General.UnitsPerFoot;

            var route = EasementBuilder.OrderRoute(job.RoutePieces, job.Tpob.Point, tolerance);
            foreach (var w in route.Warnings) ed.WriteMessage("\n  ! " + w);
            if (!route.Ok)
            {
                foreach (var e in route.Errors) ed.WriteMessage("\n  X " + e);
                ed.WriteMessage("\nSTRIPEASEMENT: not drawn -- fix the route and run it again.\n");
                return false;
            }

            var main = MakeShape(ed, job, route.Courses, job.Width, tolerance, string.Empty);
            if (main == null) return false;
            main.Warnings.InsertRange(0, route.Warnings);

            Shape temporary = null;
            if (job.TemporaryWidth != null && temporaryRecord != null)
            {
                temporary = MakeShape(ed, job, route.Courses, job.TemporaryWidth, tolerance, "Temporary construction easement: ");
                if (temporary == null) return false;
            }

            List<int> keep = null;
            if (job.Trimmable)
            {
                if (job.KeepPoints != null)
                {
                    P2? missing;
                    keep = PiecesAt(main.Split, job.KeepPoints, out missing);
                    if (missing.HasValue)
                    {
                        ed.WriteMessage("\n  X The piece kept last time (around N {0:0.00} E {1:0.00}) is no longer part of the easement.", missing.Value.Y, missing.Value.X);
                        return false;
                    }
                }
                else
                {
                    var preview = new TrimPreview
                    {
                        Split = main.Split, Route = main.Built.Centerline, Trims = job.Trims.ToList(), Width = job.Width,
                        AnglePoints = job.AnglePoints.Select(a => a.Point).ToList(), Purpose = job.Purpose,
                        Notes = main.Warnings.Concat(temporary != null ? temporary.Warnings.Select(w => "Temporary: " + w) : new string[0]).ToList(),
                        Tolerance = tolerance, UnitsPerFoot = settings.General.UnitsPerFoot, Settings = es, Keep = new List<int> { 1 },
                        Drafting = es.Copy(), PlotScale = CadUtil.DrawingUnitsPerPlottedUnit(db),
                        TemporarySplit = temporary != null ? temporary.Split : null, TemporaryWidth = job.TemporaryWidth,
                        Commencement = job.Poc != null ? job.Poc.Point : (P2?)null,
                        TerminusCorner = job.TerminusCorner != null ? job.TerminusCorner.Point : (P2?)null
                    };
                    if (!Choose(ed, preview)) { ed.WriteMessage("\nSTRIPEASEMENT: cancelled -- nothing drawn.\n"); return false; }
                    keep = preview.Keep;
                    job.Purpose = preview.Purpose;
                    // Only what the drafter changed is stored, so a later profile change still
                    // reaches everything they left alone.
                    record.Drafting = EasementDrafting.Difference(es, preview.Drafting);
                    if (temporaryRecord != null) temporaryRecord.Drafting = record.Drafting;
                }
            }

            var groupId = record.GroupId ?? (temporary != null ? record.Id : null);
            if (!Finish(db, tr, ed, settings, rulesVersion, job, record, main, job.Width, job.Purpose, keep, job.Temporary, groupId, tolerance))
                return false;

            if (temporary != null)
            {
                // The temporary easement keeps whatever surrounds the kept pieces of the easement.
                P2? missing;
                var temporaryKeep = PiecesAt(temporary.Split, record.KeepPoints ?? new List<P2>(), out missing);
                if (missing.HasValue || temporaryKeep.Count == 0)
                {
                    ed.WriteMessage("\n  X The temporary construction easement could not be matched to the kept pieces.");
                    return false;
                }
                temporaryRecord.Id = temporaryRecord.Id ?? Guid.NewGuid().ToString("N");
                job.TablePosition = null;
                if (!Finish(db, tr, ed, settings, rulesVersion, job, temporaryRecord, temporary, job.TemporaryWidth, es.TemporaryPurpose,
                            temporaryKeep, true, groupId, tolerance))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// The office easement settings with this easement's own preview choices laid over
        /// them. The office settings are never changed, so a choice made for one easement
        /// does not follow the drafter into the next command.
        /// </summary>
        internal static EasementSettings EsFor(EasementRecord record, FtfSettings settings)
        {
            return record != null && record.Drafting != null ? record.Drafting.ApplyTo(settings.Easements) : settings.Easements;
        }

        private static bool Finish(Database db, Transaction tr, Editor ed, FtfSettings settings, string rulesVersion, EasementJob job,
                                   EasementRecord record, Shape shape, WidthSpec width, string purpose, List<int> keep, bool isTemporary,
                                   string groupId, double tolerance)
        {
            var es = EsFor(record, settings);
            var built = shape.Built;
            var boundary = built.Boundary;
            var warnings = shape.Warnings.ToList();
            var clipped = false;
            var trimmed = false;
            List<List<Course>> centerParts = null;
            List<P2> keepPoints = null;

            if (job.Trimmable)
            {
                var split = shape.Split;
                string failure;
                var merged = StripTrim.Merge(split, keep, tolerance, out failure);
                if (merged == null)
                {
                    ed.WriteMessage("\n  X " + failure);
                    ed.WriteMessage("\nSTRIPEASEMENT: not drawn.\n");
                    return false;
                }
                keepPoints = split.Pieces.Where(p => keep.Contains(p.Number)).Select(p => p.InsidePoint).ToList();
                centerParts = StripTrim.PartsInside(built.Centerline, merged, tolerance);
                if (centerParts.Count == 0)
                {
                    ed.WriteMessage("\n  X None of the easement line is inside the kept area -- keep the pieces the line runs through.");
                    ed.WriteMessage("\nSTRIPEASEMENT: not drawn.\n");
                    return false;
                }

                if (split.Pieces.Count > 1)
                {
                    trimmed = true;
                    var note = string.Format(CultureInfo.InvariantCulture,
                        "Trimmed to {0} line(s); kept {1} of {2} piece(s).", split.TrimCuts.Count(c => c), keep.Count, split.Pieces.Count);
                    warnings.Add(note);
                    if (!isTemporary) ed.WriteMessage("\n  " + note);
                }
                boundary = merged;
            }
            if (job.ParcelCourses != null)
            {
                string failure;
                var clippedBoundary = ClipToParcel(boundary, job.ParcelCourses, tolerance, out failure);
                if (clippedBoundary == null)
                {
                    ed.WriteMessage("\n  X Parcel clipping failed: " + failure);
                    ed.WriteMessage("\nSTRIPEASEMENT: not drawn.\n");
                    return false;
                }
                var removed = built.Area - Math.Abs(Loops.SignedArea(clippedBoundary));
                if (removed > tolerance)
                {
                    clipped = true;
                    var note = string.Format(CultureInfo.InvariantCulture,
                        "Clipped to the parent parcel: {0:N0} sq ft outside the parcel removed.", removed / Sq(settings.General.UnitsPerFoot));
                    warnings.Add(note);
                    ed.WriteMessage("\n  ! " + note);
                    boundary = clippedBoundary;
                }
            }

            // Area in square feet regardless of drawing units.
            var upf = settings.General.UnitsPerFoot;
            var areaSqFt = Math.Abs(Loops.SignedArea(boundary)) / Sq(upf);

            var legalRoute = centerParts != null ? centerParts.SelectMany(p => p).ToList() : built.Centerline;
            var beginning = legalRoute[0].Start;
            var terminus = legalRoute[legalRoute.Count - 1].End;
            record.Purpose = purpose;
            record.Title = EasementAnnotation.Title(width, purpose, es);
            record.GroupId = groupId;
            record.Role = isTemporary ? EasementRecord.TemporaryRole : EasementRecord.PermanentRole;
            record.PointOfCommencement = job.Poc;
            record.TruePointOfBeginning = job.Tpob;
            if (centerParts != null && beginning.DistanceTo(job.Tpob.Point) > tolerance)
                record.TruePointOfBeginning = new SelectedLocation { X = beginning.X, Y = beginning.Y, Source = LocationSource.Computed };
            record.CommencementTie = job.Poc != null ? EasementAnnotation.Describe(Course.Line(job.Poc.Point, beginning)) : null;
            record.RouteSources = job.Sources.ToList();
            record.RouteCourses = EasementAnnotation.Number(legalRoute, es);
            record.Terminus = terminus;
            record.TerminusTiePoint = job.TerminusCorner;
            record.TerminusTie = job.TerminusCorner != null ? EasementAnnotation.Describe(Course.Line(terminus, job.TerminusCorner.Point)) : null;
            if (job.Trimmable)
            {
                record.RouteStart = job.Tpob.Point;
                record.AnglePoints = job.AnglePoints.Count > 0 ? job.AnglePoints.ToList() : null;
                record.TrimLines = job.TrimSources.ToList();
                record.KeepPoints = keepPoints;
                record.BeginsOn = LineThrough(job, tolerance, beginning);
                record.EndsOn = LineThrough(job, tolerance, terminus);
                record.CommencementAlong = job.Poc != null ? LineThrough(job, tolerance, job.Poc.Point, beginning) : null;
                if (record.CommencementAlong != null)
                {
                    // The tie runs along a trim line: it is stated as that line runs between the two points -- a
                    // curve as a curve and each bend as a course -- not as the chord across it.
                    var along = job.Trims[job.TrimSources.IndexOf(record.CommencementAlong)];
                    string alongProblem;
                    var path = AreaPath.Between(along, job.Poc.Point, beginning, tolerance, out alongProblem);
                    if (path != null && path.Count > 0)
                    {
                        CourseData tie;
                        List<CourseData> tieCourses;
                        Ties.Set(path, out tie, out tieCourses);
                        record.CommencementTie = tie;
                        record.CommencementTieCourses = tieCourses;
                    }
                }
            }
            record.Width = width;
            record.Begin = job.Begin;
            record.End = job.End;
            record.Parcel = job.Parcel;
            record.BoundaryCourses = EasementAnnotation.Number(boundary, es);
            record.AreaSquareFeet = areaSqFt;
            record.Acres = areaSqFt / EasementAnnotation.SquareFeetPerAcre;
            record.Warnings = warnings;
            record.ClippedToParcel = clipped;

            // Exclusions and further components, when the easement has them.
            EasementComposition.Composed composed = null;
            if (record.HasComposition)
            {
                composed = EasementComposition.Apply(db, tr, record, boundary, settings);
                foreach (var w in composed.Warnings) ed.WriteMessage("\n  ! " + w);
                if (!composed.Ok)
                {
                    foreach (var e in composed.Errors) ed.WriteMessage("\n  X " + e);
                    ed.WriteMessage("\nSTRIPEASEMENT: not drawn.\n");
                    return false;
                }
                EasementComposition.Store(record, composed, settings);
            }

            var outer = job.TemporaryWidth != null && !isTemporary ? job.TemporaryWidth : width;
            Draft(db, tr, ed, settings, rulesVersion, record, built, boundary, clipped, trimmed, centerParts, job.TablePosition, isTemporary, outer, composed);
            record.DraftedFingerprint = EasementAnnotation.Fingerprint(composed != null ? composed.Primary.Outer : boundary);
            return true;
        }

        /// <summary>The first trim line every point lies on, or null.</summary>
        /// <summary>A tie for the report: one straight course as before, or each course of a tie that follows the line.</summary>
        private static string TieText(IList<CourseData> tie, EasementSettings es, string deg)
        {
            if (!Ties.Follows(tie)) return EasementAnnotation.LineText(tie[0], es, deg);
            return string.Join(", then ", tie.Select(d => CourseText(d, es, deg)).ToArray());
        }

        private static GeometrySource LineThrough(EasementJob job, double tolerance, params P2[] points)
        {
            for (var t = 0; t < job.Trims.Count; t++)
                if (points.All(p => job.Trims[t].Any(c => { double along; return c.Closest(p, out along).DistanceTo(p) <= tolerance; })))
                    return job.TrimSources[t];
            return null;
        }

        internal static double Sq(double v) { return v * v; }

        // ============================================================= clipping

        /// <summary>
        /// Intersects the easement with the parcel using AutoCAD regions, which keep
        /// arcs exact, then rebuilds the loop. A clip that splits the strip into
        /// pieces is refused rather than guessed at.
        /// </summary>
        private static List<Course> ClipToParcel(IList<Course> boundary, IList<Course> parcel, double tolerance, out string failure)
        {
            failure = null;
            Region easement = null, parent = null;
            try
            {
                easement = MakeRegion(boundary);
                parent = MakeRegion(parcel);
                if (easement == null || parent == null) { failure = "could not make regions from the boundaries."; return null; }

                easement.BooleanOperation(BooleanOperationType.BoolIntersect, parent);
                if (easement.Area <= tolerance) { failure = "the easement lies entirely outside the parcel."; return null; }

                var pieces = new DBObjectCollection();
                easement.Explode(pieces);
                var courses = new List<Course>();
                foreach (DBObject piece in pieces)
                {
                    using (piece)
                    {
                        if (piece is Region) { failure = "the clipped easement is in more than one piece -- check the parcel."; return null; }
                        string problem;
                        var c = Extract((AcEntity)piece, out problem);
                        if (c == null) { failure = problem; return null; }
                        courses.AddRange(c);
                    }
                }

                IList<Course> loose;
                var loops = Loops.Chain(courses, tolerance, out loose);
                if (loops.Count != 1 || loose.Count > 0)
                {
                    failure = "the clipped easement is in " + loops.Count + " piece(s) -- the parcel splits the strip.";
                    return null;
                }

                // Restart the loop at the original boundary's first point when it is still
                // a vertex, so numbering stays stable.
                var loop = loops[0].ToList();
                if (Loops.SignedArea(loop) * Loops.SignedArea(boundary) < 0) loop = EasementBuilder.Reverse(loop);
                var start = boundary[0].Start;
                var at = loop.FindIndex(c => c.Start.DistanceTo(start) <= tolerance);
                if (at > 0) loop = loop.Skip(at).Concat(loop.Take(at)).ToList();
                return loop;
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                failure = ex.Message;
                return null;
            }
            finally
            {
                if (easement != null) easement.Dispose();
                if (parent != null) parent.Dispose();
            }
        }

        private static Region MakeRegion(IList<Course> loop)
        {
            var curves = new DBObjectCollection();
            foreach (var c in loop) curves.Add(ToCurve(c));
            try
            {
                var regions = Region.CreateFromCurves(curves);
                if (regions.Count != 1)
                {
                    foreach (DBObject r in regions) r.Dispose();
                    return null;
                }
                return (Region)regions[0];
            }
            finally
            {
                foreach (DBObject c in curves) c.Dispose();
            }
        }

        private static Curve ToCurve(Course c)
        {
            if (c.Kind == CourseKind.Line)
                return new Line(new Point3d(c.Start.X, c.Start.Y, 0), new Point3d(c.End.X, c.End.Y, 0));
            var start = c.CounterClockwise ? c.StartAngle : c.EndAngle;
            var end = c.CounterClockwise ? c.EndAngle : c.StartAngle;
            return new Arc(new Point3d(c.Center.X, c.Center.Y, 0), Vector3d.ZAxis, c.Radius, start, end);
        }

        // ============================================================= drafting

        private static void Draft(Database db, Transaction tr, Editor ed, FtfSettings settings, string version,
                                  EasementRecord record, EasementBuildResult built, IList<Course> boundary, bool clipped,
                                  bool trimmed, List<List<Course>> centerParts, Point3d? tablePosition, bool temporary,
                                  WidthSpec outerWidth, EasementComposition.Composed composed)
        {
            var es = EsFor(record, settings);
            var scale = CadUtil.DrawingUnitsPerPlottedUnit(db);
            var textHeight = es.TextHeightPlotted * scale;
            var styleId = Setup.DrawingResources.FindTextStyle(db, tr, es.TextStyle);
            if (!string.IsNullOrWhiteSpace(es.TextStyle) && styleId.IsNull)
                ed.WriteMessage("\n  Text style \"{0}\" is not in this drawing; using the current style.", es.TextStyle);

            var drafted = new List<string>();
            Action<AcEntity, FtfEntityKind, string> add = (entity, kind, layer) =>
            {
                entity.LayerId = ProductionLayers.Get(db, tr, layer, settings);
                CadUtil.AddToModelSpace(db, tr, entity);
                Ownership.Stamp(entity, record.Id, version, kind, null, record.Title);
                drafted.Add(entity.Handle.ToString());
            };

            var pattern = temporary ? es.TemporaryHatchPattern : es.HatchPattern;
            Polyline outline;
            if (composed == null)
            {
                outline = ToPolyline(boundary, true);
                add(outline, FtfEntityKind.EasementBoundary, temporary ? es.TemporaryLayer : es.BoundaryLayer);
            }
            else
            {
                outline = EasementComposition.DraftRegions(db, tr, ed, composed, add, temporary ? es.TemporaryLayer : es.BoundaryLayer,
                    temporary ? !string.IsNullOrWhiteSpace(pattern) : es.DrawHatch, pattern, temporary ? es.TemporaryHatch() : es.HatchLayer,
                    es.HatchScale * scale, temporary ? es.TemporaryText() : es.TextLayer, styleId, textHeight, es.ToleranceFt * settings.General.UnitsPerFoot, es.LabelMask);
            }

            // A temporary construction easement shares the easement's centerline, sidelines and
            // course labels; it adds only its outline, optional hatch, width and area.
            if (es.DrawSidelines && !temporary)
            {
                if (clipped || trimmed)
                    ed.WriteMessage("\n  Sidelines not drawn separately: the boundary was " + (trimmed ? "trimmed" : "clipped to the parcel") + ", so the untrimmed sidelines would disagree with it.");
                else
                {
                    add(ToPolyline(built.LeftSideline, false), FtfEntityKind.EasementLine, es.SidelineLayer);
                    add(ToPolyline(built.RightSideline, false), FtfEntityKind.EasementLine, es.SidelineLayer);
                }
            }
            var parts = centerParts ?? new List<List<Course>> { built.Centerline };
            if (es.DrawCenterline && !temporary)
                foreach (var part in parts)
                    add(ToPolyline(part, false), FtfEntityKind.EasementLine, es.CenterlineLayer);
            var longest = parts.OrderByDescending(EasementBuilder.RouteLength).First();

            if (composed == null && (temporary ? !string.IsNullOrWhiteSpace(pattern) : es.DrawHatch))
            {
                try
                {
                    var hatch = new Hatch();
                    hatch.SetDatabaseDefaults(db);
                    add(hatch, FtfEntityKind.EasementHatch, temporary ? es.TemporaryHatch() : es.HatchLayer);
                    // The office sets the permanent easement's hatch colour on the object and leaves the
                    // temporary one ByLayer; an empty setting leaves it ByLayer here too.
                    var colour = CadUtil.ColorFrom(temporary ? es.TemporaryHatchColor : es.HatchColor);
                    if (colour != null) hatch.Color = colour;
                    hatch.PatternScale = es.HatchScale * scale;
                    hatch.SetHatchPattern(HatchPatternType.PreDefined, pattern);
                    hatch.Associative = false;
                    hatch.AppendLoop(HatchLoopTypes.External, new ObjectIdCollection { outline.ObjectId });
                    hatch.EvaluateHatch(true);

                    var ms = CadUtil.ModelSpace(db, tr, OpenMode.ForRead);
                    var order = (DrawOrderTable)tr.GetObject(ms.DrawOrderTableId, OpenMode.ForWrite);
                    order.MoveToBottom(new ObjectIdCollection { hatch.ObjectId });
                }
                catch (Autodesk.AutoCAD.Runtime.Exception ex)
                {
                    ed.WriteMessage("\n  Hatch \"{0}\" could not be made ({1}); the boundary is drawn without it.", es.HatchPattern, ex.Message);
                }
            }

            // The title and the area are kept off the plan, as the office exhibits keep them: the
            // title on a leader pointing at the easement, the area on its own horizontal line beside
            // it. Written across the strip they would sit on the hatch and on the course labels.
            P2 direction;
            var anchor = EasementAnnotation.LabelPoint(longest, record.Width, out direction);
            if (trimmed && !StripTrim.Inside(boundary, anchor)) anchor = StripTrim.PointInside(boundary, es.ToleranceFt * settings.General.UnitsPerFoot);
            var textLayer = record.IsTemporary ? es.TemporaryText() : es.TextLayer;
            var outward = direction.LeftNormal() * (temporary ? -1.0 : 1.0);
            // Clear of the easement, and of any temporary easement around it. The temporary
            // easement's own wording goes out the other side so the two do not meet.
            var clearance = (temporary ? record.Width.Right : (outerWidth ?? record.Width).Left) + textHeight * 2.0;
            var titleAt = anchor + outward * clearance;

            var callout = CadUtil.NewLeaderedLabel(db, tr, Escape(record.Title), textHeight, styleId,
                                                   ProductionLayers.Get(db, tr, textLayer, settings),
                                                   new Point3d(titleAt.X, titleAt.Y, 0), new Point3d(anchor.X, anchor.Y, 0),
                                                   ObjectId.Null, es.LabelMask);
            Ownership.Stamp(callout, record.Id, version, FtfEntityKind.EasementText, null, record.Title);
            drafted.Add(callout.Handle.ToString());

            var areaText = new MText();
            areaText.SetDatabaseDefaults(db);
            if (!styleId.IsNull) areaText.TextStyleId = styleId;
            areaText.TextHeight = textHeight;
            areaText.Attachment = AttachmentPoint.TopLeft;
            var areaAt = titleAt + outward * (textHeight * 2.0);
            areaText.Location = new Point3d(areaAt.X, areaAt.Y, 0);
            areaText.Contents = string.Join("\\P", EasementAnnotation.AreaLines(record.AreaSquareFeet, es, record.Purpose).Select(Escape).ToArray());
            CadUtil.Mask(areaText, es.LabelMask);
            add(areaText, FtfEntityKind.EasementText, textLayer);

            if (es.DrawWidthDimensions)
                DrawWidthDimension(db, tr, ed, es, record, longest, boundary, add, scale, es.ToleranceFt * settings.General.UnitsPerFoot,
                                   temporary ? new[] { 0.5, 0.75, 0.375, 0.625, 0.875, 0.25, 0.125 } : new[] { 0.25, 0.5, 0.75, 0.125, 0.375, 0.625, 0.875 });

            if (temporary) { record.DraftedHandles = drafted; return; }
            if (es.LabelCenterline)
                CenterlineLabels(db, tr, ed, record, parts, boundary, es, styleId, textHeight, add, tablePosition, outerWidth ?? record.Width);
            else if (es.LabelMode == EasementLabelMode.Direct)
                DirectLabels(db, record, boundary, es, styleId, textHeight, add);
            else if (es.LabelMode == EasementLabelMode.Table || es.LabelMode == EasementLabelMode.Auto)
                TableLabels(db, tr, ed, record, boundary, es, styleId, textHeight, add, tablePosition);

            if (es.DrawPointLabels)
                PointLabels(db, tr, record, parts, es, styleId, textHeight, settings, outerWidth ?? record.Width, drafted, version);

            record.DraftedHandles = drafted;
        }

        private static void DrawWidthDimension(Database db, Transaction tr, Editor ed, EasementSettings es, EasementRecord record,
                                               List<Course> centerline, IList<Course> boundary, Action<AcEntity, FtfEntityKind, string> add,
                                               double scale, double tolerance, double[] fractions)
        {
            var styleId = db.Dimstyle;
            if (!string.IsNullOrWhiteSpace(es.DimensionStyleOverride))
            {
                var table = (DimStyleTable)tr.GetObject(db.DimStyleTableId, OpenMode.ForRead);
                if (table.Has(es.DimensionStyleOverride)) styleId = table[es.DimensionStyleOverride];
                else ed.WriteMessage("\n  Dimension style \"{0}\" is not in this drawing; using the current style.", es.DimensionStyleOverride);
            }

            // A quarter of the way along, square across the full width -- or the nearest
            // place along the line where a trim has not narrowed it.
            P2 direction = new P2(1, 0), left = new P2(), right = new P2(), at = new P2();
            var length = EasementBuilder.RouteLength(centerline);
            var found = false;
            foreach (var fraction in fractions)
            {
                at = EasementBuilder.PointAtStation(centerline, length * fraction, out direction);
                var normal = direction.LeftNormal();
                left = at + normal * record.Width.Left;
                right = at - normal * record.Width.Right;
                if (StripTrim.OnOutline(boundary, left, tolerance * 10) && StripTrim.OnOutline(boundary, right, tolerance * 10)) { found = true; break; }
            }
            if (!found)
            {
                ed.WriteMessage("\n  Width dimension not placed: the trim lines narrow the easement everywhere along it.");
                return;
            }
            var line = at + direction * (es.TextHeightPlotted * scale * 3.0);

            var dim = new AlignedDimension(new Point3d(right.X, right.Y, 0), new Point3d(left.X, left.Y, 0),
                                           new Point3d(line.X, line.Y, 0), string.Empty, styleId);
            dim.SetDatabaseDefaults(db);
            dim.DimensionStyle = styleId;
            add(dim, FtfEntityKind.EasementDimension, record.IsTemporary ? es.TemporaryDimensions() : es.DimensionLayer);
        }

        private static void DirectLabels(Database db, EasementRecord record, IList<Course> boundary, EasementSettings es,
                                         ObjectId styleId, double textHeight, Action<AcEntity, FtfEntityKind, string> add)
        {
            var outwardSign = Loops.SignedArea(boundary) > 0 ? -1.0 : 1.0;   // CCW loop: outside is to the right
            foreach (var d in record.BoundaryCourses)
            {
                var c = d.Course;
                var lines = c.Kind == CourseKind.Line
                    ? new List<string> { EasementAnnotation.LineText(d, es, DegreeSymbol) }
                    : EasementAnnotation.CurveLines(d, es, DegreeSymbol).ToList();

                var mid = c.PointAt(c.Length / 2.0);
                var dir = c.DirectionAt(c.Length / 2.0);
                var offset = textHeight * (0.9 * lines.Count + 0.5);
                var at = mid + dir.LeftNormal() * (outwardSign * offset);

                var text = new MText();
                text.SetDatabaseDefaults(db);
                if (!styleId.IsNull) text.TextStyleId = styleId;
                text.TextHeight = textHeight;
                text.Attachment = AttachmentPoint.MiddleCenter;
                text.Location = new Point3d(at.X, at.Y, 0);
                text.Rotation = Readable(Math.Atan2(dir.Y, dir.X));
                text.Contents = string.Join("\\P", lines.Select(Escape).ToArray());
                CadUtil.Mask(text, es.LabelMask);
                add(text, FtfEntityKind.EasementText, record.IsTemporary ? es.TemporaryText() : es.TextLayer);
            }
        }

        /// <summary>
        /// Exhibit-style labels: the tie from the Point of Commencement, each centerline
        /// course and the tie to the terminus corner, in description order. A course whose
        /// bearing and distance fit is labelled along it, outside the easement; the others
        /// are tagged L1, C1... and listed in the line / curve table.
        /// </summary>
        private static void CenterlineLabels(Database db, Transaction tr, Editor ed, EasementRecord record, List<List<Course>> parts,
                                             IList<Course> boundary, EasementSettings es, ObjectId styleId, double textHeight,
                                             Action<AcEntity, FtfEntityKind, string> add, Point3d? tablePosition, WidthSpec outerWidth)
        {
            var plan = EasementAnnotation.PlanLabels(record.TieCourses(), parts.SelectMany(p => p), record.TerminusTie,
                                                     es, textHeight, DegreeSymbol);
            foreach (var label in plan)
            {
                var c = label.Data.Course;
                var mid = c.PointAt(c.Length / 2.0);
                var dir = c.DirectionAt(c.Length / 2.0);
                var lines = label.InTable ? new List<string> { label.Data.Id } : label.Lines.ToList();
                // Centerline labels sit just outside the easement (and any temporary easement) on
                // the left; ties, which run along lot lines, sit just off their line.
                var clearance = label.IsTie ? 0.0 : outerWidth.Left;
                var offset = clearance + textHeight * (0.9 * lines.Count + 0.4);
                var at = mid + dir.LeftNormal() * offset;

                var text = new MText();
                text.SetDatabaseDefaults(db);
                if (!styleId.IsNull) text.TextStyleId = styleId;
                text.TextHeight = textHeight;
                text.Attachment = AttachmentPoint.MiddleCenter;
                text.Location = new Point3d(at.X, at.Y, 0);
                text.Rotation = Readable(Math.Atan2(dir.Y, dir.X));
                text.Contents = string.Join("\\P", lines.Select(Escape).ToArray());
                CadUtil.Mask(text, es.LabelMask);
                add(text, FtfEntityKind.EasementText, record.IsTemporary ? es.TemporaryText() : es.TextLayer);
            }

            var rows = plan.Where(p => p.InTable).Select(p => p.Data).ToList();
            if (rows.Count > 0)
                Tables(db, tr, ed, rows, boundary, es, textHeight, add, tablePosition);
        }

        /// <summary>POINT OF COMMENCEMENT, POINT OF BEGINNING and POINT OF TERMINUS as
        /// leaders on the points, text set off to the right of the easement line.</summary>
        private static void PointLabels(Database db, Transaction tr, EasementRecord record, List<List<Course>> parts,
                                        EasementSettings es, ObjectId styleId, double textHeight, FtfSettings settings,
                                        WidthSpec outerWidth, List<string> drafted, string version)
        {
            var layerId = ProductionLayers.Get(db, tr, record.IsTemporary ? es.TemporaryText() : es.TextLayer, settings);
            var first = parts[0][0];
            var lastPart = parts[parts.Count - 1];
            var last = lastPart[lastPart.Count - 1];
            var reach = outerWidth.Right + textHeight * 6.0;

            Action<string, P2, P2> leader = (text, point, away) =>
            {
                if (string.IsNullOrWhiteSpace(text)) return;
                var at = point + away * reach;
                var entity = CadUtil.NewLeaderedLabel(db, tr, Escape(text), textHeight, styleId, layerId,
                                                      new Point3d(at.X, at.Y, 0), new Point3d(point.X, point.Y, 0),
                                                      ObjectId.Null, es.LabelMask);
                Ownership.Stamp(entity, record.Id, version, FtfEntityKind.EasementText, null, record.Title);
                drafted.Add(entity.Handle.ToString());
            };

            // Right of travel, and a little back / ahead so the text clears the ends.
            var startRight = new P2(first.StartDirection.Y, -first.StartDirection.X);
            var endRight = new P2(last.EndDirection.Y, -last.EndDirection.X);
            leader(es.BeginningLabel, first.Start, (startRight - first.StartDirection * 0.5).Normalized());
            leader(es.TerminusLabel, last.End, (endRight + last.EndDirection * 0.5).Normalized());
            if (record.PointOfCommencement != null)
            {
                var poc = record.PointOfCommencement.Point;
                var away = poc.DistanceTo(first.Start) > 1e-6 ? (poc - first.Start).Normalized() : startRight;
                leader(es.CommencementLabel, poc, (away + new P2(away.Y, -away.X) * 0.5).Normalized());
            }
        }

        private static void TableLabels(Database db, Transaction tr, Editor ed, EasementRecord record, IList<Course> boundary,
                                        EasementSettings es, ObjectId styleId, double textHeight,
                                        Action<AcEntity, FtfEntityKind, string> add, Point3d? tablePosition)
        {
            var outwardSign = Loops.SignedArea(boundary) > 0 ? -1.0 : 1.0;
            foreach (var d in record.BoundaryCourses)
            {
                var c = d.Course;
                var mid = c.PointAt(c.Length / 2.0);
                var dir = c.DirectionAt(c.Length / 2.0);
                var at = mid + dir.LeftNormal() * (outwardSign * textHeight * 1.2);
                var tag = new MText();
                tag.SetDatabaseDefaults(db);
                if (!styleId.IsNull) tag.TextStyleId = styleId;
                tag.TextHeight = textHeight;
                tag.Attachment = AttachmentPoint.MiddleCenter;
                tag.Location = new Point3d(at.X, at.Y, 0);
                tag.Rotation = Readable(Math.Atan2(dir.Y, dir.X));
                tag.Contents = Escape(d.Id);
                CadUtil.Mask(tag, es.LabelMask);
                add(tag, FtfEntityKind.EasementText, record.IsTemporary ? es.TemporaryText() : es.TextLayer);
            }
            Tables(db, tr, ed, record.BoundaryCourses, boundary, es, textHeight, add, tablePosition);
        }

        /// <summary>The line table (LINE NO. | DISTANCE | BEARING) and curve table for tagged courses.</summary>
        internal static void Tables(Database db, Transaction tr, Editor ed, IList<CourseData> rows, IList<Course> boundary,
                                   EasementSettings es, double textHeight, Action<AcEntity, FtfEntityKind, string> add,
                                   Point3d? tablePosition)
        {
            var lineRows = rows.Where(d => d.Course.Kind == CourseKind.Line).ToList();
            var curveRows = rows.Where(d => d.Course.Kind == CourseKind.Arc).ToList();

            Point3d position;
            var picked = tablePosition.HasValue || !es.AskTableLocation ? null
                : ed.GetPoint(new PromptPointOptions("\nLine/curve table location <beside the easement>: ") { AllowNone = true });
            if (tablePosition.HasValue)
                position = tablePosition.Value;
            else if (picked != null && picked.Status == PromptStatus.OK)
                position = picked.Value.TransformBy(ed.CurrentUserCoordinateSystem);
            else
            {
                var maxX = boundary.Max(c => Math.Max(c.Start.X, c.End.X));
                var maxY = boundary.Max(c => Math.Max(c.Start.Y, c.End.Y));
                position = new Point3d(maxX + textHeight * 6, maxY, 0);
            }

            var tableStyle = db.Tablestyle;
            if (!string.IsNullOrWhiteSpace(es.TableStyle))
            {
                var styles = (DBDictionary)tr.GetObject(db.TableStyleDictionaryId, OpenMode.ForRead);
                if (styles.Contains(es.TableStyle)) tableStyle = styles.GetAt(es.TableStyle);
                else ed.WriteMessage("\n  Table style \"{0}\" is not in this drawing; using the current style.", es.TableStyle);
            }

            if (lineRows.Count > 0)
            {
                var table = MakeTable(db, tableStyle, position, textHeight, "LINE TABLE",
                    new[] { "LINE NO.", "DISTANCE", "BEARING" }, new[] { 9.0, 12.0, 17.0 },
                    lineRows.Select(d => new[]
                    {
                        d.Id, EasementAnnotation.Distance(d.Length, es), EasementAnnotation.Bearing(d.AzimuthDegrees, es, DegreeSymbol)
                    }).ToList());
                add(table, FtfEntityKind.EasementTable, es.TableLayer);
                position = new Point3d(position.X, position.Y - table.Height - textHeight * 4, 0);
            }
            if (curveRows.Count > 0)
            {
                var headers = new List<string> { "CURVE NO.", "RADIUS", "LENGTH", "DELTA" };
                var widths = new List<double> { 8.0, 12.0, 12.0, 14.0 };
                if (es.CurveShowChord) { headers.Add("CHORD BEARING"); headers.Add("CHORD"); widths.Add(18.0); widths.Add(12.0); }
                if (es.CurveShowTangent) { headers.Add("TANGENT"); widths.Add(12.0); }

                var table = MakeTable(db, tableStyle, position, textHeight, "CURVE TABLE", headers.ToArray(), widths.ToArray(),
                    curveRows.Select(d =>
                    {
                        var row = new List<string>
                        {
                            d.Id, EasementAnnotation.Distance(d.Radius ?? 0, es), EasementAnnotation.Distance(d.Length, es),
                            EasementAnnotation.Angle(d.DeltaDegrees ?? 0, es, DegreeSymbol)
                        };
                        if (es.CurveShowChord)
                        {
                            row.Add(EasementAnnotation.Bearing(d.ChordAzimuthDegrees ?? 0, es, DegreeSymbol));
                            row.Add(EasementAnnotation.Distance(d.ChordLength ?? 0, es));
                        }
                        if (es.CurveShowTangent) row.Add(d.Tangent.HasValue ? EasementAnnotation.Distance(d.Tangent.Value, es) : "-");
                        return row.ToArray();
                    }).ToList());
                add(table, FtfEntityKind.EasementTable, es.TableLayer);
            }
        }

        private static Table MakeTable(Database db, ObjectId style, Point3d position, double textHeight, string title,
                                       string[] headers, double[] widthsInTextHeights, IList<string[]> rows)
        {
            var table = new Table();
            table.SetDatabaseDefaults(db);
            if (!style.IsNull) table.TableStyle = style;
            table.Position = position;
            table.SetSize(rows.Count + 2, headers.Length);

            table.MergeCells(CellRange.Create(table, 0, 0, 0, headers.Length - 1));
            SetCell(table, 0, 0, title, textHeight);
            for (var c = 0; c < headers.Length; c++)
            {
                table.Columns[c].Width = widthsInTextHeights[c] * textHeight;
                SetCell(table, 1, c, headers[c], textHeight);
            }
            for (var r = 0; r < rows.Count; r++)
                for (var c = 0; c < headers.Length; c++)
                    SetCell(table, r + 2, c, rows[r][c], textHeight);
            for (var r = 0; r < rows.Count + 2; r++) table.Rows[r].Height = textHeight * 2.0;

            table.GenerateLayout();
            return table;
        }

        private static void SetCell(Table table, int row, int column, string text, double height)
        {
            var cell = table.Cells[row, column];
            cell.TextHeight = height;
            cell.TextString = text.Replace("Δ", "\\U+0394").Replace(DegreeSymbol, "\u00B0");
            cell.Alignment = CellAlignment.MiddleCenter;
        }

        internal static Polyline ToPolyline(IList<Course> courses, bool closed)
        {
            var pl = new Polyline();
            for (var i = 0; i < courses.Count; i++)
            {
                var c = courses[i];
                var bulge = c.Kind == CourseKind.Arc ? Math.Tan(c.Sweep / 4.0) * (c.CounterClockwise ? 1 : -1) : 0.0;
                pl.AddVertexAt(i, new Point2d(c.Start.X, c.Start.Y), bulge, 0, 0);
            }
            if (!closed)
            {
                var last = courses[courses.Count - 1];
                pl.AddVertexAt(courses.Count, new Point2d(last.End.X, last.End.Y), 0, 0, 0);
            }
            pl.Closed = closed;
            return pl;
        }

        internal static double Readable(double angle)
        {
            while (angle > Math.PI / 2 + 1e-9) angle -= Math.PI;
            while (angle <= -Math.PI / 2 + 1e-9) angle += Math.PI;
            return angle;
        }

        internal static string Escape(string s)
        {
            return (s ?? string.Empty).Replace("{", "\\{").Replace("}", "\\}").Replace("Δ", "\\U+0394");
        }

        // ================================================= geometry from entities

        /// <summary>Exact courses from a CAD object, in the object's own direction.
        /// Returns null with a reason when the object cannot be represented exactly.</summary>
        internal static IList<Course> Extract(AcEntity entity, out string problem)
        {
            problem = null;
            var line = entity as Line;
            if (line != null)
                return new List<Course> { Course.Line(P(line.StartPoint), P(line.EndPoint)) };

            var arc = entity as Arc;
            if (arc != null)
                return new List<Course> { Course.Arc(P(arc.StartPoint), P(arc.EndPoint), P(arc.Center), arc.Normal.Z > 0) };

            var pl = entity as Polyline;
            if (pl != null)
            {
                var flip = pl.Normal.Z < 0;
                var courses = new List<Course>();
                var count = pl.Closed ? pl.NumberOfVertices : pl.NumberOfVertices - 1;
                for (var i = 0; i < count; i++)
                {
                    var a = P(pl.GetPoint3dAt(i));
                    var b = P(pl.GetPoint3dAt((i + 1) % pl.NumberOfVertices));
                    if (a.DistanceTo(b) < 1e-9) continue;
                    var bulge = pl.GetBulgeAt(i);
                    courses.Add(Course.FromBulge(a, b, flip ? -bulge : bulge));
                }
                if (courses.Count == 0) { problem = "The polyline has no length."; return null; }
                return courses;
            }

            var p3 = entity as Polyline3d;
            if (p3 != null)
            {
                if (p3.PolyType != Poly3dType.SimplePoly) { problem = "Splined 3D polylines are not exact survey geometry."; return null; }
                var points = new List<P2>();
                foreach (ObjectId id in p3)
                {
                    var v = id.GetObject(OpenMode.ForRead) as PolylineVertex3d;
                    if (v != null) points.Add(P(v.Position));
                }
                if (p3.Closed && points.Count > 0) points.Add(points[0]);
                var courses = new List<Course>();
                for (var i = 1; i < points.Count; i++)
                    if (points[i].DistanceTo(points[i - 1]) > 1e-9) courses.Add(Course.Line(points[i - 1], points[i]));
                if (courses.Count == 0) { problem = "The 3D polyline has no length."; return null; }
                return courses;
            }

            var alignment = entity as Alignment;
            if (alignment != null) return FromAlignment(alignment, out problem);

            // Anything else that is a curve (a Civil 3D parcel line, a circle...) is read
            // through its exact geometry when that is lines and arcs.
            var curve = entity as Curve;
            if (curve != null)
            {
                try
                {
                    var courses = new List<Course>();
                    if (FromGe(curve.GetGeCurve(), courses)) return courses;
                }
                catch (Autodesk.AutoCAD.Runtime.Exception) { }
                catch (NotSupportedException) { }
            }

            problem = "A " + TypeName(entity) + " cannot be used here -- it is not made of exact lines and arcs.";
            return null;
        }

        private static bool FromGe(Curve3d ge, List<Course> into)
        {
            var segment = ge as LineSegment3d;
            if (segment != null)
            {
                into.Add(Course.Line(P(segment.StartPoint), P(segment.EndPoint)));
                return true;
            }
            var arc = ge as CircularArc3d;
            if (arc != null)
            {
                if (Math.Abs(Math.Abs(arc.Normal.Z) - 1) > 1e-9) return false;
                into.Add(Course.Arc(P(arc.StartPoint), P(arc.EndPoint), P(arc.Center), arc.Normal.Z > 0));
                return true;
            }
            var composite = ge as CompositeCurve3d;
            if (composite != null)
            {
                foreach (var part in composite.GetCurves())
                    if (!FromGe(part, into)) return false;
                return into.Count > 0;
            }
            return false;
        }

        private static IList<Course> FromAlignment(Alignment alignment, out string problem)
        {
            problem = null;
            var courses = new List<Course>();
            var entities = alignment.Entities;
            for (var i = 0; i < entities.Count; i++)
            {
                var ae = entities.GetEntityByOrder(i);
                for (var s = 0; s < ae.SubEntityCount; s++)
                {
                    var sub = ae[s];
                    var subLine = sub as Autodesk.Civil.DatabaseServices.AlignmentSubEntityLine;
                    if (subLine != null)
                    {
                        courses.Add(Course.Line(P(subLine.StartPoint), P(subLine.EndPoint)));
                        continue;
                    }
                    var subArc = sub as Autodesk.Civil.DatabaseServices.AlignmentSubEntityArc;
                    if (subArc != null)
                    {
                        courses.Add(Course.Arc(P(subArc.StartPoint), P(subArc.EndPoint), P(subArc.CenterPoint), !subArc.Clockwise));
                        continue;
                    }
                    problem = "Alignment \"" + alignment.Name + "\" contains a spiral. Spirals cannot be offset exactly as lines and arcs, so the easement is not built from it -- select lines/arcs instead.";
                    return null;
                }
            }
            if (courses.Count == 0) { problem = "Alignment \"" + alignment.Name + "\" has no geometry."; return null; }
            return courses;
        }

        private static P2 P(Point3d p) { return new P2(p.X, p.Y); }
        private static P2 P(Point2d p) { return new P2(p.X, p.Y); }

        internal static string TypeName(AcEntity entity)
        {
            if (entity is CogoPoint) return "COGO point";
            if (entity is Alignment) return "Alignment";
            return entity.GetType().Name;
        }

        // ================================================================ prompts

        /// <summary>
        /// Asks for a location the way drafters pick one: a point, with object snaps
        /// (ENDpoint, NODe, INSertion) working as usual. What sits at the picked spot
        /// decides the source that is recorded -- a COGO point, the end or a vertex of a
        /// line / polyline / arc, or a CAD point -- so the easement can later tell
        /// whether that object moved. Nothing there means a typed coordinate, and the
        /// drafter is told so.
        /// </summary>
        internal static PromptStatus PickLocation(Database db, Transaction tr, Editor ed, PromptPointOptions options,
                                                 out SelectedLocation location, out string keyword)
        {
            location = null;
            keyword = null;
            var picked = ed.GetPoint(options);
            if (picked.Status == PromptStatus.Keyword) { keyword = picked.StringResult; return PromptStatus.Keyword; }
            if (picked.Status == PromptStatus.None) return PromptStatus.OK;
            if (picked.Status != PromptStatus.OK) return PromptStatus.Cancel;

            var at = picked.Value.TransformBy(ed.CurrentUserCoordinateSystem);
            var p = new P2(at.X, at.Y);
            // A pick near a survey point or line end snaps to it -- but never further than a foot: zoomed out on a
            // dense survey drawing the pick box covers many feet, and moving a corner that far would change the easement.
            var tolerance = Math.Min(PickTolerance(), SnapLimitUnits(db));

            AcEntity bestEntity = null;
            P2 bestPoint = p;
            var bestRank = int.MaxValue;
            var bestDistance = double.MaxValue;
            var onLine = false;
            foreach (var id in NearbyEntities(db, tr, ed, at, tolerance))
            {
                var entity = tr.GetObject(id, OpenMode.ForRead) as AcEntity;
                if (entity == null) continue;
                var curve = entity as Curve;
                if (curve != null && !onLine)
                {
                    try { onLine = curve.GetClosestPointTo(at, false).DistanceTo(at) <= 1e-6; }
                    catch (Autodesk.AutoCAD.Runtime.Exception) { }
                }
                // Survey points first, then line ends / vertices, then plain CAD points.
                int rank;
                List<P2> spots;
                var cogo = entity as CogoPoint;
                var dbPoint = entity as DBPoint;
                if (cogo != null) { rank = 0; spots = new List<P2> { new P2(cogo.Easting, cogo.Northing) }; }
                else if (entity is Curve) { rank = 1; spots = Vertices(entity); }
                else if (dbPoint != null) { rank = 2; spots = new List<P2> { P(dbPoint.Position) }; }
                else continue;

                foreach (var spot in spots)
                {
                    var d = spot.DistanceTo(p);
                    if (d > tolerance) continue;
                    if (rank < bestRank || (rank == bestRank && d < bestDistance))
                    {
                        bestRank = rank; bestDistance = d; bestEntity = entity; bestPoint = spot;
                    }
                }
            }

            // A point exactly on a line (snapped to it, or typed on it) stays where it is: it is
            // never moved to a nearby line end or survey point it does not coincide with.
            if (onLine && bestEntity != null && bestDistance > 1e-6)
            {
                bestEntity = null;
                ed.WriteMessage("\n  On a line between its ends -- kept exactly where it is.");
            }

            if (bestEntity is CogoPoint)
            {
                var cogo = (CogoPoint)bestEntity;
                location = new SelectedLocation
                {
                    X = bestPoint.X, Y = bestPoint.Y, Source = LocationSource.CogoPoint,
                    PointNumber = cogo.PointNumber.ToString(CultureInfo.InvariantCulture), Handle = cogo.Handle.ToString()
                };
                ed.WriteMessage("\n  Using COGO point {0}{1}.", cogo.PointNumber, Moved(bestDistance));
            }
            else if (bestEntity is Curve)
            {
                location = new SelectedLocation { X = bestPoint.X, Y = bestPoint.Y, Source = LocationSource.GeometryEndpoint, Handle = bestEntity.Handle.ToString() };
                ed.WriteMessage("\n  Using the end / vertex of the {0} at N {1:0.00} E {2:0.00}{3}.", TypeName(bestEntity), bestPoint.Y, bestPoint.X, Moved(bestDistance));
            }
            else if (bestEntity is DBPoint)
            {
                location = new SelectedLocation { X = bestPoint.X, Y = bestPoint.Y, Source = LocationSource.CadPoint, Handle = bestEntity.Handle.ToString() };
                ed.WriteMessage("\n  Using the CAD point at N {0:0.00} E {1:0.00}.", bestPoint.Y, bestPoint.X);
            }
            else
            {
                location = new SelectedLocation { X = p.X, Y = p.Y, Source = LocationSource.Manual };
                ed.WriteMessage("\n  Not on a survey point or line end -- kept as a coordinate (N {0:0.00} E {1:0.00}); a rebuild will not follow it if the survey moves.", p.Y, p.X);
            }
            return PromptStatus.OK;
        }

        /// <summary>The pick box, in drawing units: how close a click must land to count
        /// as being on a point or line end.</summary>
        /// <summary>
        /// Picks a line, polyline, arc or other curve. A click that lands on a hatch, text or
        /// dimension lying over the line uses the nearest curve under the click instead of
        /// rejecting it -- clicking a lot line through an easement hatch just works.
        /// </summary>
        internal static AcEntity PickCurve(Database db, Transaction tr, Editor ed, string message, bool allowNone,
                                           out PromptStatus status, out Point3d picked)
        {
            picked = Point3d.Origin;
            var options = new PromptEntityOptions(message) { AllowNone = allowNone };
            while (true)
            {
                var result = ed.GetEntity(options);
                status = result.Status;
                if (status != PromptStatus.OK) return null;
                picked = result.PickedPoint.TransformBy(ed.CurrentUserCoordinateSystem);
                var entity = tr.GetObject(result.ObjectId, OpenMode.ForRead) as AcEntity;
                if (entity is Curve) return entity;

                var tolerance = PickTolerance();
                Curve best = null;
                var bestDistance = double.MaxValue;
                foreach (var id in NearbyEntities(db, tr, ed, picked, tolerance))
                {
                    var curve = tr.GetObject(id, OpenMode.ForRead) as Curve;
                    if (curve == null) continue;
                    double d;
                    try { d = curve.GetClosestPointTo(picked, false).DistanceTo(picked); }
                    catch (Autodesk.AutoCAD.Runtime.Exception) { continue; }
                    if (d < bestDistance) { bestDistance = d; best = curve; }
                }
                if (best != null && bestDistance <= tolerance)
                {
                    ed.WriteMessage("\n  Using the {0} under the pick.", TypeName(best));
                    return best;
                }
                ed.WriteMessage("\n  Select a line, polyline or arc.");
            }
        }

        private static string Moved(double distance)
        {
            return distance > 0.005 ? " -- " + distance.ToString("0.00", CultureInfo.InvariantCulture) + " from where you picked" : string.Empty;
        }

        /// <summary>One foot in drawing units: the furthest a pick is ever moved to a nearby point.</summary>
        private static double SnapLimitUnits(Database db)
        {
            try { return FtfSession.SettingsFor(db, FtfSession.Rules(db)).General.UnitsPerFoot; }
            catch (System.Exception) { return 1.0; }
        }

        /// <summary>A closed boundary FTF can use whose outline passes through the pick, when lot lines lie on top of it.</summary>
        internal static AcEntity ClosedUnder(Database db, Transaction tr, Editor ed, Point3d picked, double closure)
        {
            var tolerance = PickTolerance();
            AcEntity best = null;
            var bestDistance = double.MaxValue;
            foreach (var id in NearbyEntities(db, tr, ed, picked, tolerance))
            {
                var curve = tr.GetObject(id, OpenMode.ForRead) as Curve;
                if (curve == null) continue;
                string problem;
                var courses = Extract(curve, out problem);
                if (courses == null || Loops.LargestGap(courses) > closure) continue;
                double d;
                try { d = curve.GetClosestPointTo(picked, false).DistanceTo(picked); }
                catch (Autodesk.AutoCAD.Runtime.Exception) { continue; }
                if (d < bestDistance) { bestDistance = d; best = curve; }
            }
            return best != null && bestDistance <= tolerance ? best : null;
        }

        /// <summary>The nearest line, polyline or arc at a pick that FTF can use, other than the object actually picked.</summary>
        internal static AcEntity CurveUnder(Database db, Transaction tr, Editor ed, Point3d picked, ObjectId except)
        {
            var tolerance = PickTolerance();
            AcEntity best = null;
            var bestDistance = double.MaxValue;
            foreach (var id in NearbyEntities(db, tr, ed, picked, tolerance))
            {
                if (id == except) continue;
                var curve = tr.GetObject(id, OpenMode.ForRead) as Curve;
                if (curve == null) continue;
                string problem;
                if (Extract(curve, out problem) == null) continue;
                double d;
                try { d = curve.GetClosestPointTo(picked, false).DistanceTo(picked); }
                catch (Autodesk.AutoCAD.Runtime.Exception) { continue; }
                if (d < bestDistance) { bestDistance = d; best = curve; }
            }
            return best != null && bestDistance <= tolerance ? best : null;
        }

        private static double PickTolerance()
        {
            try
            {
                var viewHeight = Convert.ToDouble(AcadApp.GetSystemVariable("VIEWSIZE"), CultureInfo.InvariantCulture);
                var screen = (Point2d)AcadApp.GetSystemVariable("SCREENSIZE");
                var aperture = Convert.ToDouble(AcadApp.GetSystemVariable("APERTURE"), CultureInfo.InvariantCulture);
                if (viewHeight > 0 && screen.Y > 0) return Math.Max(0.001, viewHeight / screen.Y * Math.Max(3.0, aperture));
            }
            catch (System.Exception) { }
            return 0.5;
        }

        private static IEnumerable<ObjectId> NearbyEntities(Database db, Transaction tr, Editor ed, Point3d at, double tolerance)
        {
            var ucs = ed.CurrentUserCoordinateSystem.Inverse();
            var a = new Point3d(at.X - tolerance, at.Y - tolerance, at.Z).TransformBy(ucs);
            var b = new Point3d(at.X + tolerance, at.Y + tolerance, at.Z).TransformBy(ucs);
            var window = ed.SelectCrossingWindow(a, b);
            if (window.Status == PromptStatus.OK) return window.Value.GetObjectIds();
            if (window.Status == PromptStatus.None) return new ObjectId[0];

            // Off screen: look through model space instead.
            var ms = CadUtil.ModelSpace(db, tr, OpenMode.ForRead);
            var ids = new List<ObjectId>();
            foreach (ObjectId id in ms) ids.Add(id);
            return ids;
        }

        /// <summary>Ends and vertices of a line, arc, polyline or 3D polyline.</summary>
        private static List<P2> Vertices(AcEntity entity)
        {
            var list = new List<P2>();
            var pl = entity as Polyline;
            if (pl != null)
            {
                for (var i = 0; i < pl.NumberOfVertices; i++) list.Add(P(pl.GetPoint3dAt(i)));
                return list;
            }
            var p3 = entity as Polyline3d;
            if (p3 != null)
            {
                foreach (ObjectId id in p3)
                {
                    var v = id.GetObject(OpenMode.ForRead) as PolylineVertex3d;
                    if (v != null) list.Add(P(v.Position));
                }
                return list;
            }
            var p2 = entity as Polyline2d;
            if (p2 != null)
            {
                foreach (ObjectId id in p2)
                {
                    var v = id.GetObject(OpenMode.ForRead) as Vertex2d;
                    if (v != null) list.Add(P(p2.VertexPosition(v)));
                }
                return list;
            }
            var curve = entity as Curve;
            if (curve != null)
            {
                try { list.Add(P(curve.StartPoint)); list.Add(P(curve.EndPoint)); }
                catch (Autodesk.AutoCAD.Runtime.Exception) { }
            }
            return list;
        }

        internal static GeometrySource SourceForLocation(Database db, Transaction tr, SelectedLocation location, string role)
        {
            return new GeometrySource
            {
                Handle = location.Handle,
                EntityType = location.Source.ToString(),
                Role = role,
                Fingerprint = EasementAnnotation.FingerprintPoint(location.X, location.Y)
            };
        }

        internal static string Keyword(Editor ed, string message, string defaultKeyword, params string[] keywords)
        {
            var options = new PromptKeywordOptions(message);
            options.AppendKeywordsToMessage = false;
            foreach (var k in keywords) options.Keywords.Add(k);
            options.Keywords.Default = defaultKeyword;
            options.AllowNone = true;
            var result = ed.GetKeywords(options);
            if (result.Status == PromptStatus.None) return defaultKeyword;
            return result.Status == PromptStatus.OK ? result.StringResult : null;
        }

        internal static bool Distance(Editor ed, string message, bool allowZero, out double value)
        {
            value = 0;
            var options = new PromptDistanceOptions(message) { AllowNegative = false, AllowZero = allowZero, AllowNone = false };
            var result = ed.GetDistance(options);
            if (result.Status != PromptStatus.OK) return false;
            value = result.Value;
            return !double.IsNaN(value);
        }

        // ======================================================= FTFEASEMENTCHECK

        /// <summary>
        /// Checks every stored easement against the objects it was built from. A
        /// changed or erased source is reported; nothing changes unless the user
        /// chooses to rebuild that easement.
        /// </summary>
        [CommandMethod("FTFEASEMENTCHECK", CommandFlags.Modal)]
        public void CheckEasements()
        {
            FtfSession.Run("FTFEASEMENTCHECK", (db, tr, ed) =>
            {
                var rules = FtfSession.Rules(db);
                var settings = FtfSession.SettingsFor(db, rules);
                var records = DrawingStore.LoadEasements(db, tr);
                if (records.Count == 0) { ed.WriteMessage("\nFTFEASEMENTCHECK: no strip easements are stored in this drawing.\n"); return; }

                var all = false;
                var current = 0;
                var rebuiltIds = new List<string>();
                foreach (var record in records)
                {
                    var changes = Changes(db, tr, record);
                    var handEdited = DraftingEdited(db, tr, record);
                    if (changes.Count == 0 && !handEdited) { current++; continue; }

                    ed.WriteMessage("\n{0} ({1:N0} sq ft):", record.Title, record.AreaSquareFeet);
                    foreach (var c in changes) ed.WriteMessage("\n  - " + c);
                    if (handEdited) ed.WriteMessage("\n  - The drawn boundary was edited by hand since it was built; a rebuild would replace that edit.");
                    if (changes.Count == 0) continue;

                    if (!all)
                    {
                        var answer = Keyword(ed, "\n  Rebuild this easement from the current survey geometry? [Yes/No/All] <No>: ", "No", "Yes", "No", "All");
                        if (answer == null) return;
                        if (answer == "No") continue;
                        if (answer == "All") all = true;
                    }

                    string problem;
                    var before = record.AreaSquareFeet;
                    var redone = RebuildRecord(db, tr, ed, settings, rules.Version, record, "after survey changes", out problem);
                    if (redone == null) { ed.WriteMessage("\n  Not rebuilt: " + problem); continue; }
                    ed.WriteMessage("\n  Rebuilt: area {0:N0} -> {1:N0} sq ft.", before, redone.AreaSquareFeet);
                    rebuiltIds.Add(record.Id);
                }
                ed.WriteMessage("\nFTFEASEMENTCHECK: {0} of {1} easement(s) current with the survey.", current, records.Count);

                // Exhibits built from these easements: changed or rebuilt easements make them stale.
                var fresh = DrawingStore.LoadEasements(db, tr);
                foreach (var exhibit in DrawingStore.LoadExhibits(db, tr))
                {
                    var reasons = FieldCodes.Exhibits.ExhibitPlanner.StaleSources(exhibit, fresh);
                    var changedNow = exhibit.Sources.Where(s => fresh.Any(r => r.Id == s.EasementId && Changes(db, tr, r).Count > 0)).Select(s => s.Title).ToList();
                    if (reasons.Count == 0 && changedNow.Count == 0) continue;
                    ed.WriteMessage("\nEXHIBIT MAY BE STALE: layout \"{0}\"", exhibit.LayoutName);
                    foreach (var title in changedNow) ed.WriteMessage("\n  - EASEMENT CHANGED: " + title + " (not rebuilt yet)");
                    foreach (var reason in reasons) ed.WriteMessage("\n  - " + reason);
                    ed.WriteMessage("\n  Run FTFEXHIBITREBUILD to update it.");
                }
                ed.WriteMessage("\n");
            });
        }

        /// <summary>
        /// Rebuilds one easement record from the current drawing and its stored definition --
        /// strip, portion or area, with its exclusions and components -- replacing its drafting.
        /// Returns the saved record, or null with a reason when its sources cannot be read. An
        /// invalid result throws, so the command's transaction leaves the old drafting in place.
        /// </summary>
        internal static EasementRecord RebuildRecord(Database db, Transaction tr, Editor ed, FtfSettings settings, string version,
                                                     EasementRecord record, string reason, out string problem)
        {
            problem = null;
            if (record.IsPortion || record.IsArea)
                return EasementAreaCommands.Rebuild(db, tr, ed, settings, version, record, out problem);

            var job = JobFromRecord(db, tr, record, settings.Easements, out problem);
            if (job == null) return null;

            var rebuilt = new EasementRecord
            {
                Id = record.Id, CreatedUtc = record.CreatedUtc, Profile = DrawingStore.ReadProfileName(db), GroupId = record.GroupId, Legal = record.Legal,
                ExhibitGroup = record.ExhibitGroup, Exclusions = record.Exclusions, Components = record.Components, RebuiltUtc = DateTime.UtcNow,
                HatchScales = record.HatchScales, HatchPatternScaleByHand = record.HatchPatternScaleByHand,
                // A rebuild redraws what the drafter chose in the preview, not the profile default.
                Drafting = record.Drafting,
                LegalStatus = record.Legal != null ? "DRAFT OUT OF DATE - rebuilt " + reason + "; write the draft again with FTFEASEMENTLEGAL" : record.LegalStatus
            };
            var before = record.AreaSquareFeet;
            foreach (var pair in Ownership.FindOwned(db, tr, s => s.PointNumber == record.Id && s.Kind == FtfEntityKind.EasementTable))
            {
                var table = tr.GetObject(pair.Key, OpenMode.ForRead) as Table;
                if (table != null && (job.TablePosition == null || table.Position.Y > job.TablePosition.Value.Y))
                    job.TablePosition = table.Position;
            }
            Ownership.DeleteOwned(db, tr, s => s.PointNumber == record.Id && s.Kind >= FtfEntityKind.EasementBoundary && s.Kind <= FtfEntityKind.EasementTable);
            if (!BuildAndDraft(db, tr, ed, settings, version, job, rebuilt, null))
            {
                // The erase above is in this transaction; abort the whole command so the
                // old drafting stays exactly as it was.
                throw new InvalidOperationException("The rebuilt easement is invalid; nothing was changed. Fix the survey geometry or rebuild by hand.");
            }
            rebuilt.Warnings.Add(string.Format(CultureInfo.InvariantCulture, "Rebuilt {0:yyyy-MM-dd HH:mm} UTC {1}; area was {2:N0} sq ft.", DateTime.UtcNow, reason, before));
            DrawingStore.SaveEasement(db, tr, rebuilt);
            return rebuilt;
        }

        internal static AcEntity Resolve(Database db, Transaction tr, string handle)
        {
            if (string.IsNullOrWhiteSpace(handle)) return null;
            long value;
            if (!long.TryParse(handle, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value)) return null;
            ObjectId id;
            if (!db.TryGetObjectId(new Handle(value), out id) || id.IsNull || id.IsErased) return null;
            return tr.GetObject(id, OpenMode.ForRead, false) as AcEntity;
        }

        internal static IList<string> Changes(Database db, Transaction tr, EasementRecord record)
        {
            var changes = new List<string>();
            foreach (var source in record.RouteSources.Where(s => !string.IsNullOrWhiteSpace(s.Handle)))
            {
                var entity = Resolve(db, tr, source.Handle);
                if (entity == null) { changes.Add(source.Role + " (" + source.EntityType + ") was erased."); continue; }

                string fingerprint;
                if (source.EntityType == LocationSource.CogoPoint.ToString() || source.EntityType == LocationSource.CadPoint.ToString() ||
                    source.EntityType == LocationSource.GeometryEndpoint.ToString())
                {
                    var location = LocationOf(entity, record, source);
                    fingerprint = location == null ? null : EasementAnnotation.FingerprintPoint(location.Value.X, location.Value.Y);
                }
                else
                {
                    string problem;
                    var courses = Extract(entity, out problem);
                    fingerprint = courses == null ? null : EasementAnnotation.Fingerprint(courses);
                }
                if (fingerprint != source.Fingerprint)
                    changes.Add(source.Role + " (" + source.EntityType + ") has moved or changed shape.");
            }
            return changes;
        }

        internal static bool DraftingEdited(Database db, Transaction tr, EasementRecord record)
        {
            if (string.IsNullOrWhiteSpace(record.DraftedFingerprint)) return false;
            foreach (var pair in Ownership.FindOwned(db, tr, s => s.PointNumber == record.Id && s.Kind == FtfEntityKind.EasementBoundary))
            {
                var outline = tr.GetObject(pair.Key, OpenMode.ForRead) as Polyline;
                if (outline == null) continue;
                string problem;
                var courses = Extract(outline, out problem);
                return courses == null || EasementAnnotation.Fingerprint(courses) != record.DraftedFingerprint;
            }
            return false;
        }

        /// <summary>The live position of a location source. Endpoints keep the end that
        /// was chosen: the one nearer the stored coordinate of that role.</summary>
        private static P2? LocationOf(AcEntity entity, EasementRecord record, GeometrySource source)
        {
            var cogo = entity as CogoPoint;
            if (cogo != null) return new P2(cogo.Easting, cogo.Northing);
            var dbPoint = entity as DBPoint;
            if (dbPoint != null) return P(dbPoint.Position);
            if (!(entity is Curve)) return null;

            // A line end or polyline vertex: the vertex nearest where it was when picked.
            var stored = StoredLocation(record, source);
            if (stored == null) return null;
            var vertices = Vertices(entity);
            if (vertices.Count == 0) return null;
            return vertices.OrderBy(v => v.DistanceTo(stored.Point)).First();
        }

        private static SelectedLocation StoredLocation(EasementRecord record, GeometrySource source)
        {
            switch (source.Role)
            {
                case "POC": return record.PointOfCommencement;
                case "TPOB": return record.TruePointOfBeginning;
                case "BEGIN POINT": return record.Begin != null ? record.Begin.Point : null;
                case "END POINT": return record.End != null ? record.End.Point : null;
                case TerminusTieRole: return record.TerminusTiePoint;
                default:
                    if (record.Components != null && source.Role.StartsWith(EasementComposition.RolePrefixComponent, StringComparison.Ordinal))
                    {
                        var rest = source.Role.Substring(EasementComposition.RolePrefixComponent.Length);
                        var space = rest.IndexOf(' ');
                        var component = space > 0 ? record.Components.FirstOrDefault(c => c.Label == rest.Substring(0, space)) : null;
                        if (component != null)
                        {
                            var what = rest.Substring(space + 1);
                            if (what == "POC") return component.PointOfCommencement;
                            int k;
                            if (what.StartsWith("ANGLE POINT ", StringComparison.Ordinal) && component.AnglePoints != null &&
                                int.TryParse(what.Substring("ANGLE POINT ".Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out k) &&
                                k >= 1 && k <= component.AnglePoints.Count)
                                return component.AnglePoints[k - 1];
                        }
                        return null;
                    }
                    if (record.AnglePoints != null && source.Role.StartsWith("ANGLE POINT ", StringComparison.Ordinal))
                    {
                        int n;
                        if (int.TryParse(source.Role.Substring("ANGLE POINT ".Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out n) &&
                            n >= 1 && n <= record.AnglePoints.Count)
                            return record.AnglePoints[n - 1];
                    }
                    return null;
            }
        }

        internal static SelectedLocation Refresh(Database db, Transaction tr, EasementRecord record, SelectedLocation stored, string role)
        {
            if (stored == null) return null;
            if (string.IsNullOrWhiteSpace(stored.Handle)) return stored;       // manual coordinate
            var source = record.RouteSources.FirstOrDefault(s => s.Role == role);
            var entity = Resolve(db, tr, stored.Handle);
            if (entity == null || source == null) return null;
            var p = LocationOf(entity, record, source);
            if (p == null) return null;
            return new SelectedLocation { X = p.Value.X, Y = p.Value.Y, Source = stored.Source, PointNumber = stored.PointNumber, Handle = stored.Handle };
        }

        /// <summary>An angle-point / trim-line easement, re-read from the current drawing.
        /// It keeps the pieces that contain the points kept last time.</summary>
        private static EasementJob JobFromTrimRecord(Database db, Transaction tr, EasementRecord record, out string problem)
        {
            problem = null;
            var job = new EasementJob
            {
                Trimmable = true, Purpose = record.Purpose, Width = record.Width, Temporary = record.IsTemporary,
                Begin = TerminationSpec.Perpendicular(), End = TerminationSpec.Perpendicular(),
                KeepPoints = record.KeepPoints != null ? record.KeepPoints.ToList() : new List<P2>()
            };

            if (record.PointOfCommencement != null)
            {
                job.Poc = Refresh(db, tr, record, record.PointOfCommencement, "POC");
                if (job.Poc == null) { problem = "the Point of Commencement object was erased."; return null; }
                job.Sources.Add(SourceForLocation(db, tr, job.Poc, "POC"));
            }

            if (record.AnglePoints != null)
            {
                for (var i = 0; i < record.AnglePoints.Count; i++)
                {
                    var role = AngleRole(i);
                    var point = Refresh(db, tr, record, record.AnglePoints[i], role);
                    if (point == null) { problem = "angle point " + (i + 1) + " was erased."; return null; }
                    job.AnglePoints.Add(point);
                    job.Sources.Add(SourceForLocation(db, tr, point, role));
                }
                var courses = new List<Course>();
                for (var i = 1; i < job.AnglePoints.Count; i++) courses.Add(Course.Line(job.AnglePoints[i - 1].Point, job.AnglePoints[i].Point));
                job.RoutePieces.Add(courses);
                job.Tpob = job.AnglePoints[0];
            }
            else
            {
                var source = record.RouteSources.FirstOrDefault(s => s.Role == "ROUTE");
                var entity = source == null ? null : Resolve(db, tr, source.Handle);
                if (entity == null) { problem = "the easement line object was erased."; return null; }
                var courses = Extract(entity, out problem);
                if (courses == null) return null;
                var list = courses.ToList();
                var start = record.RouteStart ?? list[0].Start;
                if (start.DistanceTo(list[list.Count - 1].End) < start.DistanceTo(list[0].Start)) list = EasementBuilder.Reverse(list);
                job.RoutePieces.Add(list);
                job.Tpob = new SelectedLocation { X = list[0].Start.X, Y = list[0].Start.Y, Source = LocationSource.GeometryEndpoint, Handle = source.Handle };
                job.Sources.Add(new GeometrySource { Handle = source.Handle, EntityType = source.EntityType, Role = "ROUTE", Fingerprint = EasementAnnotation.Fingerprint(courses) });
            }

            if (record.TerminusTiePoint != null)
            {
                job.TerminusCorner = Refresh(db, tr, record, record.TerminusTiePoint, TerminusTieRole);
                if (job.TerminusCorner == null) { problem = "the corner the terminus is tied to was erased."; return null; }
                job.Sources.Add(SourceForLocation(db, tr, job.TerminusCorner, TerminusTieRole));
            }

            foreach (var trim in record.TrimLines)
            {
                var entity = Resolve(db, tr, trim.Handle);
                if (entity == null) { problem = trim.Role.ToLowerInvariant() + " was erased."; return null; }
                var courses = Extract(entity, out problem);
                if (courses == null) return null;
                var source = new GeometrySource { Handle = trim.Handle, EntityType = trim.EntityType, Role = trim.Role, Fingerprint = EasementAnnotation.Fingerprint(courses) };
                job.Trims.Add(courses);
                job.TrimSources.Add(source);
                job.Sources.Add(source);
            }
            return job;
        }

        private static EasementJob JobFromRecord(Database db, Transaction tr, EasementRecord record, EasementSettings es, out string problem)
        {
            problem = null;
            if (record.Trimmable) return JobFromTrimRecord(db, tr, record, out problem);
            var job = new EasementJob { Purpose = record.Purpose, Width = record.Width };

            job.Poc = Refresh(db, tr, record, record.PointOfCommencement, "POC");
            if (record.PointOfCommencement != null && job.Poc == null) { problem = "the Point of Commencement object is gone."; return null; }
            job.Tpob = Refresh(db, tr, record, record.TruePointOfBeginning, "TPOB");
            if (job.Tpob == null) { problem = "the TPOB object is gone."; return null; }

            // Route: whole objects in order; point routes are rebuilt from their points.
            var pointGroups = new Dictionary<string, List<P2>>();
            foreach (var source in record.RouteSources)
            {
                if (source.Role == "ROUTE")
                {
                    var entity = Resolve(db, tr, source.Handle);
                    if (entity == null) { problem = "a route object was erased."; return null; }
                    var courses = Extract(entity, out problem);
                    if (courses == null) return null;
                    job.RoutePieces.Add(courses);
                    job.Sources.Add(new GeometrySource { Handle = source.Handle, EntityType = source.EntityType, Role = source.Role, Fingerprint = EasementAnnotation.Fingerprint(courses) });
                }
                else if (source.Role.StartsWith("ROUTE POINT", StringComparison.Ordinal))
                {
                    if (string.IsNullOrWhiteSpace(source.Handle)) { problem = "a point route used a typed coordinate, which cannot be re-read -- rebuild it by hand."; return null; }
                    var entity = Resolve(db, tr, source.Handle);
                    var cogo = entity as CogoPoint;
                    var dbPoint = entity as DBPoint;
                    if (cogo == null && dbPoint == null) { problem = "a route point was erased or was a line end, which cannot be re-read -- rebuild it by hand."; return null; }
                    var p = cogo != null ? new P2(cogo.Easting, cogo.Northing) : P(dbPoint.Position);
                    List<P2> group;
                    if (!pointGroups.TryGetValue(source.Role, out group))
                    {
                        group = new List<P2>();
                        pointGroups[source.Role] = group;
                        job.RoutePieces.Add(new List<Course>());   // placeholder keeps the order
                    }
                    group.Add(p);
                    job.Sources.Add(new GeometrySource { Handle = source.Handle, EntityType = source.EntityType, Role = source.Role, Fingerprint = EasementAnnotation.FingerprintPoint(p.X, p.Y) });
                }
                else if (source.Role != "POC" && source.Role != "TPOB")
                {
                    job.Sources.Add(source);
                }
            }
            var placeholders = job.RoutePieces.Select((piece, i) => new { piece, i }).Where(x => x.piece.Count == 0).Select(x => x.i).ToList();
            var groups = pointGroups.Values.ToList();
            for (var k = 0; k < placeholders.Count && k < groups.Count; k++)
            {
                var pts = groups[k];
                var courses = new List<Course>();
                for (var i = 1; i < pts.Count; i++) courses.Add(Course.Line(pts[i - 1], pts[i]));
                job.RoutePieces[placeholders[k]] = courses;
            }
            job.Sources.Insert(0, new GeometrySource { Handle = job.Tpob.Handle, EntityType = job.Tpob.Source.ToString(), Role = "TPOB", Fingerprint = EasementAnnotation.FingerprintPoint(job.Tpob.X, job.Tpob.Y) });
            if (job.Poc != null)
                job.Sources.Insert(0, new GeometrySource { Handle = job.Poc.Handle, EntityType = job.Poc.Source.ToString(), Role = "POC", Fingerprint = EasementAnnotation.FingerprintPoint(job.Poc.X, job.Poc.Y) });

            job.Begin = RefreshTermination(db, tr, record, record.Begin, "BEGIN", out problem);
            if (job.Begin == null) return null;
            job.End = RefreshTermination(db, tr, record, record.End, "END", out problem);
            if (job.End == null) return null;

            if (record.Parcel != null)
            {
                var entity = Resolve(db, tr, record.Parcel.Handle);
                if (entity == null) { problem = "the parent parcel was erased."; return null; }
                var courses = Extract(entity, out problem);
                if (courses == null) return null;
                job.ParcelCourses = courses.ToList();
                job.Parcel = new GeometrySource { Handle = record.Parcel.Handle, EntityType = record.Parcel.EntityType, Role = "PARCEL", Fingerprint = EasementAnnotation.Fingerprint(courses) };
                job.Sources.RemoveAll(s => s.Role == "PARCEL");
                job.Sources.Add(job.Parcel);
            }

            // Refresh fingerprints of boundary / point terminations in the sources.
            foreach (var s in job.Sources.Where(s => s.Role.EndsWith("BOUNDARY", StringComparison.Ordinal) || s.Role.EndsWith(" POINT", StringComparison.Ordinal)).ToList())
            {
                if (s.Role.StartsWith("ROUTE", StringComparison.Ordinal)) continue;
                var spec = s.Role.StartsWith("BEGIN", StringComparison.Ordinal) ? job.Begin : job.End;
                var replacement = new GeometrySource { Handle = s.Handle, EntityType = s.EntityType, Role = s.Role };
                if (spec.Method == TerminationMethod.Boundary) replacement.Fingerprint = EasementAnnotation.Fingerprint(spec.Boundary);
                else if (spec.Point != null) replacement.Fingerprint = EasementAnnotation.FingerprintPoint(spec.Point.X, spec.Point.Y);
                else replacement.Fingerprint = s.Fingerprint;
                job.Sources[job.Sources.IndexOf(s)] = replacement;
            }
            return job;
        }

        private static TerminationSpec RefreshTermination(Database db, Transaction tr, EasementRecord record, TerminationSpec spec, string which, out string problem)
        {
            problem = null;
            if (spec == null) return TerminationSpec.Perpendicular();
            switch (spec.Method)
            {
                case TerminationMethod.Boundary:
                {
                    var entity = Resolve(db, tr, spec.BoundaryHandle);
                    if (entity == null) { problem = "the " + which.ToLowerInvariant() + " termination geometry was erased."; return null; }
                    var courses = Extract(entity, out problem);
                    if (courses == null) return null;
                    return new TerminationSpec { Method = spec.Method, BoundaryKind = spec.BoundaryKind, Boundary = courses.ToList(), BoundaryHandle = spec.BoundaryHandle };
                }
                case TerminationMethod.Point:
                {
                    var point = Refresh(db, tr, record, spec.Point, which + " POINT");
                    if (point == null) { problem = "the " + which.ToLowerInvariant() + " termination point was erased."; return null; }
                    return new TerminationSpec { Method = spec.Method, Point = point };
                }
                default:
                    return spec;
            }
        }

        // ====================================================== FTFEASEMENTEXPORT

        /// <summary>
        /// Writes each stored easement's ordered geometry -- commencement, tie,
        /// beginning, courses, terminus, width, area -- beside the drawing. It is
        /// the raw material for a legal description, clearly marked as not one.
        /// </summary>
        [CommandMethod("FTFEASEMENTEXPORT", CommandFlags.Modal)]
        public void ExportEasements()
        {
            FtfSession.Run("FTFEASEMENTEXPORT", (db, tr, ed) =>
            {
                var settings = FtfSession.SettingsFor(db, FtfSession.Rules(db));
                var es = settings.Easements;
                var records = DrawingStore.LoadEasements(db, tr);
                if (records.Count == 0) { ed.WriteMessage("\nFTFEASEMENTEXPORT: no strip easements are stored in this drawing.\n"); return; }
                if (string.IsNullOrWhiteSpace(db.Filename) || !File.Exists(db.Filename))
                { ed.WriteMessage("\nFTFEASEMENTEXPORT: save the drawing first so the file has somewhere to go.\n"); return; }

                var path = Path.Combine(Path.GetDirectoryName(db.Filename), Path.GetFileNameWithoutExtension(db.Filename) + ".ftf-easements.txt");
                var sb = new StringBuilder();
                // Each temporary construction easement follows the easement it belongs to.
                foreach (var r in records.OrderBy(r => r.CreatedUtc).ThenBy(r => r.GroupId ?? r.Id).ThenBy(r => r.IsTemporary))
                    sb.Append(DescribeRecord(r, es, db, tr)).AppendLine();
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
                ed.WriteMessage("\nFTFEASEMENTEXPORT: {0} easement(s) written to {1}\n", records.Count, path);
            });
        }

        internal static string DescribeRecord(EasementRecord r, EasementSettings es, Database db, Transaction tr)
        {
            const string deg = "°";
            var sb = new StringBuilder();
            sb.AppendLine("==== " + r.Title + " ====");
            sb.AppendLine("STATUS: " + r.LegalStatus);
            if (r.IsPortion || r.IsArea)
            {
                var staleArea = Changes(db, tr, r);
                if (staleArea.Count > 0) sb.AppendLine("SURVEY HAS CHANGED SINCE THIS WAS BUILT: " + string.Join("; ", staleArea.ToArray()));
                if (r.IsPortion)
                    sb.AppendLine(PortionBuilder.Describe(r.PortionSteps, es.DistanceDecimals) + " THE LOT, " + PortionBuilder.MeasuredClause(r.PortionSteps));
                if (r.PointOfCommencement != null)
                    sb.AppendLine("Point of Commencement: " + Where(r.PointOfCommencement) + (r.CommencementTie != null ? "  tie " + TieText(r.TieCourses(), es, deg) : string.Empty));
                sb.AppendLine(r.IsArea ? "Boundary (from the Point of Beginning):" : "Boundary (closed):");
                foreach (var d in r.BoundaryCourses) sb.AppendLine("  " + CourseText(d, es, deg));
                sb.AppendLine(r.IsArea ? EasementAnnotation.AreaOfAreaLine(r.AreaSquareFeet, es, r.Purpose) : string.Join("  ", EasementAnnotation.AreaLines(r.AreaSquareFeet, es, r.Purpose).ToArray()));
                sb.AppendLine(r.IsArea ? EasementAnnotation.AreaLegalLine(r.AreaSquareFeet, es, r.Purpose) : EasementAnnotation.LegalAreaLine(r.AreaSquareFeet, es, r.Purpose));
                foreach (var w in r.Warnings) sb.AppendLine("NOTE: " + w);
                return sb.ToString();
            }
            sb.AppendLine("Built " + r.CreatedUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC" +
                          (string.IsNullOrWhiteSpace(r.Profile) ? string.Empty : ", profile " + r.Profile));
            var stale = Changes(db, tr, r);
            if (stale.Count > 0) sb.AppendLine("SURVEY HAS CHANGED SINCE THIS WAS BUILT: " + string.Join("; ", stale.ToArray()));

            if (r.IsTemporary) sb.AppendLine("Temporary construction easement on the same centerline as the easement above.");
            if (r.PointOfCommencement != null)
            {
                sb.AppendLine("Point of Commencement: " + Where(r.PointOfCommencement));
                if (r.CommencementTie != null)
                    sb.AppendLine("  Thence " + (r.CommencementAlong != null ? "along " + LineName(r.CommencementAlong) + ", " : string.Empty) +
                                  TieText(r.TieCourses(), es, deg) + " to the Point of Beginning");
            }
            sb.AppendLine((r.Trimmable ? "Point of Beginning: " : "True Point of Beginning: ") + Where(r.TruePointOfBeginning) +
                          (r.BeginsOn != null ? "  on " + LineName(r.BeginsOn) : string.Empty));
            sb.AppendLine("Width: " + (r.Width.Mode == WidthMode.Centered
                ? r.Width.Total.ToString("0.00", CultureInfo.InvariantCulture) + "' centered on the route"
                : r.Width.Left.ToString("0.00", CultureInfo.InvariantCulture) + "' left, " + r.Width.Right.ToString("0.00", CultureInfo.InvariantCulture) + "' right of the route"));
            if (r.Trimmable)
            {
                if (r.AnglePoints != null)
                    for (var i = 0; i < r.AnglePoints.Count; i++) sb.AppendLine("Angle point " + (i + 1) + ": " + Where(r.AnglePoints[i]));
                sb.AppendLine("All sidelines shortened or lengthened to meet at all angle points.");
                sb.AppendLine(r.TrimLines.Count == 0
                    ? "Not trimmed; the ends are square."
                    : "Begins, ends or is cut at: " + string.Join(", ", r.TrimLines.Select(s => s.EntityType + " " + s.Handle).ToArray()));
            }
            else
                sb.AppendLine("Begins: " + Termination(r.Begin) + "   Ends: " + Termination(r.End));
            if (r.ClippedToParcel) sb.AppendLine("Clipped to the parent parcel.");

            sb.AppendLine(r.Trimmable ? "Centerline (from the Point of Beginning):" : "Controlling route (from the TPOB):");
            foreach (var d in r.RouteCourses) sb.AppendLine("  " + CourseText(d, es, deg));
            sb.AppendLine("  Terminus: " + r.Terminus + (r.EndsOn != null ? "  on " + LineName(r.EndsOn) : string.Empty));
            if (r.TerminusTie != null)
                sb.AppendLine("  To which " + Where(r.TerminusTiePoint) + " bears " + EasementAnnotation.LineText(r.TerminusTie, es, deg));

            sb.AppendLine("Boundary (closed):");
            foreach (var d in r.BoundaryCourses) sb.AppendLine("  " + CourseText(d, es, deg));
            sb.AppendLine(string.Join("  ", EasementAnnotation.AreaLines(r.AreaSquareFeet, es, r.Purpose).ToArray()));
            sb.AppendLine(EasementAnnotation.LegalAreaLine(r.AreaSquareFeet, es, r.Purpose));
            foreach (var w in r.Warnings) sb.AppendLine("NOTE: " + w);
            return sb.ToString();
        }

        private static string CourseText(CourseData d, EasementSettings es, string deg)
        {
            return d.Course.Kind == CourseKind.Line
                ? d.Id + "  " + EasementAnnotation.LineText(d, es, deg)
                : d.Id + "  " + string.Join("  ", EasementAnnotation.CurveLines(d, es, deg).ToArray()) + "  (" + d.TurnDirection + ")";
        }

        private static string LineName(GeometrySource s)
        {
            return s.Role.ToLowerInvariant() + " (" + s.EntityType + " " + s.Handle + ")";
        }

        internal static string Where(SelectedLocation l)
        {
            if (l == null) return "-";
            var at = string.Format(CultureInfo.InvariantCulture, "N {0:0.000}  E {1:0.000}", l.Y, l.X);
            switch (l.Source)
            {
                case LocationSource.CogoPoint: return "COGO point " + l.PointNumber + "  " + at;
                case LocationSource.CadPoint: return "CAD point  " + at;
                case LocationSource.GeometryEndpoint: return "end of drawn geometry  " + at;
                case LocationSource.Computed: return "where the easement line meets a trim line  " + at;
                default: return "typed coordinate  " + at;
            }
        }

        private static string Termination(TerminationSpec t)
        {
            if (t == null) return "square";
            switch (t.Method)
            {
                case TerminationMethod.Boundary: return "at " + (t.BoundaryKind ?? "boundary");
                case TerminationMethod.Station: return "square at station " + (t.Station ?? 0).ToString("0.00", CultureInfo.InvariantCulture);
                case TerminationMethod.Point: return "square through " + Where(t.Point);
                default: return "square";
            }
        }
    }
}
