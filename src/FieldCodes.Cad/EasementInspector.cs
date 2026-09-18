using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using FieldCodes.Drafting;
using FieldCodes.Easements;
using FieldCodes.Exhibits;
using FieldCodes.Settings;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcEntity = Autodesk.AutoCAD.DatabaseServices.Entity;

namespace FieldCodes.Cad
{
    /// <summary>One line of an inspection: what, its value, and where it came from.</summary>
    internal sealed class InspectRow
    {
        public string Section;
        public string Item;
        public string Value;
        public string Source;
        /// <summary>The CAD object behind the row, to highlight or zoom to.</summary>
        public string Handle;
        /// <summary>Paper-space layout the object lives in; null for model space.</summary>
        public string Layout;
        /// <summary>Empty, Warning or Error.</summary>
        public string Severity = string.Empty;
    }

    /// <summary>
    /// FTFEASEMENTINSPECT / FTFEXHIBITINSPECT: an audit of one easement or exhibit. Every item
    /// says where it came from -- a user entry, a survey point, an object handle, a calculation --
    /// and rows with a CAD object can zoom to it. Nothing is changed.
    /// </summary>
    public sealed class EasementInspectCommands
    {
        [CommandMethod("FTFEASEMENTINSPECT", CommandFlags.Modal)]
        public void InspectEasement() { Inspect("FTFEASEMENTINSPECT"); }

        [CommandMethod("FTFEXHIBITINSPECT", CommandFlags.Modal)]
        public void InspectExhibit() { Inspect("FTFEXHIBITINSPECT"); }

        private static void Inspect(string command)
        {
            List<InspectRow> rows = null;
            string title = null;
            FtfSession.Run(command, (db, tr, ed) =>
            {
                var settings = FtfSession.SettingsFor(db, FtfSession.Rules(db));
                var records = DrawingStore.LoadEasements(db, tr);
                var exhibits = DrawingStore.LoadExhibits(db, tr);

                var options = new PromptEntityOptions("\nSelect an FTF easement or exhibit object" + (command == "FTFEXHIBITINSPECT" ? " <this layout>" : string.Empty) + ": ") { AllowNone = true };
                var picked = ed.GetEntity(options);
                ExhibitRecord exhibit = null;
                EasementRecord easement = null;
                if (picked.Status == PromptStatus.None)
                {
                    var layout = LayoutManager.Current.CurrentLayout;
                    exhibit = exhibits.FirstOrDefault(x => string.Equals(x.LayoutName, layout, StringComparison.OrdinalIgnoreCase));
                    if (exhibit == null) { ed.WriteMessage("\n{0}: layout \"{1}\" is not an FTF exhibit.\n", command, layout); return; }
                }
                else if (picked.Status == PromptStatus.OK)
                {
                    var stamp = Ownership.Read((AcEntity)tr.GetObject(picked.ObjectId, OpenMode.ForRead));
                    if (stamp == null) { ed.WriteMessage("\n{0}: that object is not FTF easement or exhibit drafting.\n", command); return; }
                    if (stamp.Kind >= FtfEntityKind.ExhibitViewport) exhibit = exhibits.FirstOrDefault(x => x.Id == stamp.PointNumber);
                    else easement = records.FirstOrDefault(r => r.Id == stamp.PointNumber);
                    if (exhibit == null && easement == null) { ed.WriteMessage("\n{0}: no stored easement or exhibit owns that object.\n", command); return; }
                }
                else return;

                rows = exhibit != null ? ExhibitRows(db, tr, exhibit, records, settings) : EasementRows(db, tr, easement, records, exhibits, settings);
                title = exhibit != null ? "Exhibit: " + exhibit.LayoutName : easement.Title;

                if (Headless())
                {
                    ed.WriteMessage("\n{0}: {1}", command, title);
                    foreach (var r in rows)
                        ed.WriteMessage("\n  {0}{1} | {2}: {3}{4}", r.Severity.Length > 0 ? "[" + r.Severity.ToUpperInvariant() + "] " : string.Empty,
                                        r.Section, r.Item, r.Value, string.IsNullOrEmpty(r.Source) ? string.Empty : "   (source: " + r.Source + ")");
                    ed.WriteMessage("\n");
                }
            });
            if (rows != null && !Headless()) ShowWindow(title, rows);
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        internal static void ShowWindow(string title, List<InspectRow> rows)
        {
            var form = new Ui.InspectorForm(title, rows);
            AcadApp.ShowModelessDialog(form);
        }

        internal static bool Headless()
        {
            try { return System.Diagnostics.Process.GetCurrentProcess().ProcessName.StartsWith("accoreconsole", StringComparison.OrdinalIgnoreCase); }
            catch (InvalidOperationException) { return false; }
        }

        // ================================================================ easement

        internal static List<InspectRow> EasementRows(Database db, Transaction tr, EasementRecord r, IList<EasementRecord> records,
                                                      IList<ExhibitRecord> exhibits, FtfSettings settings)
        {
            var es = settings.Easements;
            var rows = new List<InspectRow>();
            Action<string, string, string, string, string, string> add = (section, item, value, source, handle, severity) =>
                rows.Add(new InspectRow { Section = section, Item = item, Value = value ?? "-", Source = source, Handle = handle, Severity = severity ?? string.Empty });

            // ---- the easement
            const string e = "Easement";
            add(e, "Type", Kind(r), "Stored easement record " + r.Id, null, null);
            add(e, "Title", r.Title, "Title format in the profile, from the purpose and width", null, null);
            add(e, "Purpose", r.Purpose, "User entered", null, null);
            add(e, "Created", r.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), "Easement record", null, null);
            add(e, "Last rebuild", r.RebuiltUtc.HasValue ? r.RebuiltUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : "never rebuilt", "Easement record", null, null);
            var changes = EasementCommands.Changes(db, tr, r);
            add(e, "Status", changes.Count == 0 ? "CURRENT with the survey" : "STALE -- " + changes.Count + " source(s) changed; run FTFEASEMENTCHECK",
                "Every source object compared with its fingerprint at build", null, changes.Count == 0 ? null : "Warning");
            foreach (var c in changes) add(e, "Changed source", c, "Fingerprint comparison", null, "Warning");
            if (EasementCommands.DraftingEdited(db, tr, r))
                add(e, "Manual override", "The drawn boundary was edited by hand since it was built", "Drawn outline compared with the stored boundary", OutlineHandle(db, tr, r), "Warning");
            add(e, "Area", SqFt(r.DisplayAreaSquareFeet), r.HasComposition ? "Calculated from the final boundaries: component A after exclusions, plus each component" + (r.ComponentsOverlap ? " (overlap counted once)" : string.Empty) : "Calculated from the final closed easement boundary", OutlineHandle(db, tr, r), null);
            if (r.Components != null && r.Components.Count > 0)
            {
                add(e, "SUM OF COMPONENT AREAS", SqFt(r.AreaSquareFeet), "Each component's own area added up" + (r.ComponentsOverlap ? " -- shared ground counted twice" : string.Empty), null, r.ComponentsOverlap ? "Warning" : null);
                add(e, "TOTAL PHYSICAL AREA", r.PhysicalAreaSquareFeet.HasValue ? SqFt(r.PhysicalAreaSquareFeet.Value) : "not worked out -- the overlap is too tangled to resolve exactly",
                    "Geometric union of the components (informational; the legal description's area is the surveyor's decision)", null, r.ComponentsOverlap ? "Warning" : null);
            }
            if (r.PrimaryAreaSquareFeet.HasValue) add(e, "Area, component A", SqFt(r.PrimaryAreaSquareFeet.Value), "Calculated after its exclusions", null, null);
            add(e, "Profile", string.IsNullOrWhiteSpace(r.Profile) ? "(none selected: drawing / user settings)" : r.Profile, "Drafting profile when built", null, null);
            if (!string.IsNullOrWhiteSpace(r.ExhibitGroup)) add(e, "Exhibit group", r.ExhibitGroup, "FTFEASEMENTGROUP", null, null);
            if (r.GroupId != null)
                foreach (var linked in records.Where(x => x.GroupId == r.GroupId && x.Id != r.Id))
                    add(e, linked.IsTemporary ? "Temporary easement" : "Linked easement", linked.Title + ", " + SqFt(linked.AreaSquareFeet), "Built together on the same centerline", OutlineHandle(db, tr, linked), null);

            // ---- where the geometry came from
            const string s = "Sources";
            if (r.LotLines != null && r.LotLines.Count > 0)
                foreach (var line in r.LotLines)
                    add(s, "Lot line (lot built by FTF from separate lines)", line.EntityType + " " + line.Handle, "Object handle " + line.Handle, line.Handle, Missing(db, tr, line.Handle));
            else if (r.Parcel != null) add(s, r.IsPortion ? "Lot boundary" : "Parent parcel (clipping boundary)", r.Parcel.EntityType + " " + r.Parcel.Handle, "Object handle " + r.Parcel.Handle, r.Parcel.Handle, Missing(db, tr, r.Parcel.Handle));
            foreach (var follow in r.CommencementTieFollows ?? new List<GeometrySource>())
                add(s, "Tie from the Point of Commencement follows", follow.EntityType + " " + follow.Handle, "Object handle " + follow.Handle, follow.Handle, Missing(db, tr, follow.Handle));
            if (r.PointOfCommencement != null) add(s, "Point of Commencement", Location(r.PointOfCommencement), SourceOf(r.PointOfCommencement), r.PointOfCommencement.Handle, Manual(r.PointOfCommencement));
            if (r.TruePointOfBeginning != null)
                add(s, r.Trimmable || r.IsArea ? "Point of Beginning" : "True Point of Beginning", Location(r.TruePointOfBeginning),
                    r.TruePointOfBeginning.Source == LocationSource.Computed ? "Calculated where the easement line meets " + (r.BeginsOn != null ? r.BeginsOn.Role.ToLowerInvariant() + " (" + r.BeginsOn.EntityType + " " + r.BeginsOn.Handle + ")" : "a trim line") : SourceOf(r.TruePointOfBeginning),
                    r.TruePointOfBeginning.Handle ?? (r.BeginsOn != null ? r.BeginsOn.Handle : null), Manual(r.TruePointOfBeginning));
            for (var i = 0; i < (r.AnglePoints ?? new List<SelectedLocation>()).Count; i++)
                add(s, r.IsArea ? "Corner " + (i + 1) : "Angle point " + (i + 1), Location(r.AnglePoints[i]), SourceOf(r.AnglePoints[i]), r.AnglePoints[i].Handle, Manual(r.AnglePoints[i]));
            foreach (var src in (r.RouteSources ?? new List<GeometrySource>()).Where(x => x.Role == "ROUTE"))
                add(s, "Controlling geometry", src.EntityType + " " + src.Handle, "Object handle " + src.Handle, src.Handle, Missing(db, tr, src.Handle));
            foreach (var t in r.TrimLines ?? new List<GeometrySource>())
                add(s, "Trim line", t.EntityType + " " + t.Handle, "Object handle " + t.Handle, t.Handle, Missing(db, tr, t.Handle));
            if (r.BeginsOn != null) add(s, "Point of Beginning is on", r.BeginsOn.EntityType + " " + r.BeginsOn.Handle, "Object handle " + r.BeginsOn.Handle, r.BeginsOn.Handle, null);
            if (r.EndsOn != null) add(s, "Terminus is on", r.EndsOn.EntityType + " " + r.EndsOn.Handle, "Object handle " + r.EndsOn.Handle, r.EndsOn.Handle, null);
            if (r.CommencementAlong != null) add(s, "Commencement tie runs along", r.CommencementAlong.EntityType + " " + r.CommencementAlong.Handle, "Object handle " + r.CommencementAlong.Handle, r.CommencementAlong.Handle, null);
            if (r.TerminusTiePoint != null) add(s, "Terminus tie corner", Location(r.TerminusTiePoint), SourceOf(r.TerminusTiePoint), r.TerminusTiePoint.Handle, Manual(r.TerminusTiePoint));
            for (var i = 0; i < (r.AreaSides ?? new List<AreaSide>()).Count; i++)
                if (!string.IsNullOrWhiteSpace(r.AreaSides[i].FollowHandle))
                    add(s, "Side " + (i + 1) + " follows", r.AreaSides[i].FollowType + " " + r.AreaSides[i].FollowHandle, "Object handle " + r.AreaSides[i].FollowHandle, r.AreaSides[i].FollowHandle, Missing(db, tr, r.AreaSides[i].FollowHandle));
            foreach (var step in r.PortionSteps ?? new List<PortionStep>())
                add(s, "Portion call", "THE " + step.Side + " " + Ft(step.Distance, es), "User entered portion call; measured at right angles from " + (string.IsNullOrWhiteSpace(step.Handle) ? "the stored line" : "object handle " + step.Handle + ", course " + (step.Segment + 1)), step.Handle, null);

            // ---- geometry
            const string g = "Geometry";
            if (!r.IsPortion && !r.IsArea && r.Width != null)
                add(g, "Width", r.Width.Mode == WidthMode.Centered ? Ft(r.Width.Total, es) + " centered (" + Ft(r.Width.Left, es) + " each side)" : Ft(r.Width.Left, es) + " left, " + Ft(r.Width.Right, es) + " right",
                    "User entered", null, null);
            if (r.Trimmable)
                add(g, "Termination", r.TrimLines.Count == 0 ? "square at the first and last angle points" : "trimmed to " + r.TrimLines.Count + " line(s); " + (r.KeepPoints ?? new List<P2>()).Count + " piece(s) kept",
                    "Trim lines picked by the user; pieces chosen in the preview", null, null);
            else if (r.Begin != null && !r.IsPortion && !r.IsArea)
                add(g, "Termination", "begins " + r.Begin.Method + ", ends " + (r.End != null ? r.End.Method.ToString() : "-"), "User chosen", r.Begin.BoundaryHandle, null);
            if (r.ClippedToParcel) add(g, "Clipping", "clipped to the parent parcel", "Parent parcel boundary", r.Parcel != null ? r.Parcel.Handle : null, null);
            var curves = (r.BoundaryCourses ?? new List<CourseData>()).Where(d => d.Course.Kind == CourseKind.Arc).ToList();
            add(g, "Curves", curves.Count == 0 ? "none" : string.Join("; ", curves.Select(d => d.Id + " R=" + Ft(d.Radius ?? 0, es) + " L=" + Ft(d.Length, es) + " D=" + SurveyDirection.FormatAzimuth(d.DeltaDegrees ?? 0, es.BearingSecondsDecimals, "°")).ToArray()),
                "Exact arcs kept from the source geometry", null, null);
            for (var i = 0; i < (r.Exclusions ?? new List<Exclusion>()).Count; i++) ExclusionRow(add, g, "Exclusion " + (i + 1), r.Exclusions[i], es);
            foreach (var c in r.Components ?? new List<EasementComponent>())
            {
                add(g, "Component " + c.Label, c.Kind.ToLowerInvariant() + ", " + SqFt(c.AreaSquareFeet) + ", joined by " + (c.Connector ?? "(connector not chosen)"),
                    c.Parcel != null ? "Object handle " + c.Parcel.Handle : "Clicked corners", c.Parcel != null ? c.Parcel.Handle : null, c.Connector == null ? "Warning" : null);
                for (var i = 0; i < (c.Exclusions ?? new List<Exclusion>()).Count; i++) ExclusionRow(add, g, "Component " + c.Label + " exclusion " + (i + 1), c.Exclusions[i], es);
            }
            add(g, "Boundary courses", (r.BoundaryCourses ?? new List<CourseData>()).Count + " course(s)" + (r.Holes != null && r.Holes.Count > 0 ? ", " + r.Holes.Count + " interior hole(s)" : string.Empty),
                "Final closed boundary stored with the easement", OutlineHandle(db, tr, r), null);

            // ---- closure
            ClosureRows(add, Closure.ForRecord(r, es), es);

            // ---- drafting and settings
            const string d2 = "Drafting";
            var owned = Ownership.FindOwned(db, tr, st => st.PointNumber == r.Id);
            var layers = new Dictionary<string, int>();
            foreach (var kv in owned)
            {
                var entity = tr.GetObject(kv.Key, OpenMode.ForRead) as AcEntity;
                if (entity == null) continue;
                int n;
                layers.TryGetValue(entity.Layer, out n);
                layers[entity.Layer] = n + 1;
                var hatch = entity as Hatch;
                if (hatch != null) add(d2, "Hatch", hatch.PatternName + " on " + hatch.Layer + ", " + SqFt(hatch.Area / Math.Pow(settings.General.UnitsPerFoot, 2)), "Drawn hatch; pattern from the profile", entity.Handle.ToString(),
                                       (r.Components == null || r.Components.Count == 0) && Math.Abs(hatch.Area / Math.Pow(settings.General.UnitsPerFoot, 2) - r.AreaSquareFeet) > 1 ? "Warning" : null);
                var dim = entity as Dimension;
                if (dim != null) add(d2, "Dimension", dim.DimensionStyleName + ", measures " + dim.Measurement.ToString("0.00", CultureInfo.InvariantCulture), "Drawn dimension; style from the profile or the drawing", entity.Handle.ToString(), null);
            }
            add(d2, "Drafted objects", owned.Count + " object(s)", "Owned by this easement (XData)", null, owned.Count == 0 ? "Warning" : null);
            add(d2, "Layers", string.Join(", ", layers.Select(kv => kv.Key + " (" + kv.Value + ")").ToArray()), "Profile layers, resolved against the drawing", null, null);
            add(d2, "Labels", es.LabelMode + (es.LabelCenterline ? ", centerline and ties" : ", outline courses") + ", bearings " + (es.BearingSpaces ? "with" : "without") + " spaces",
                "Profile: Strip Easements", null, null);
            add(d2, "Tables", "line prefix " + es.LinePrefix + ", curve prefix " + es.CurvePrefix + ", style " + (string.IsNullOrWhiteSpace(es.TableStyle) ? "(current)" : es.TableStyle), "Profile: Strip Easements", null, null);
            add(d2, "Dimension style", string.IsNullOrWhiteSpace(es.DimensionStyleOverride) ? "(drawing's current style)" : es.DimensionStyleOverride, "Profile: Strip Easements", null, null);

            // ---- warnings, legal, exhibits
            foreach (var w in r.Warnings ?? new List<string>()) add("Warnings", "Note", w, "Recorded when built", null, "Warning");
            add("Legal draft", "Status", r.LegalStatus, "FTFEASEMENTLEGAL", null, r.LegalStatus != null && r.LegalStatus.Contains("NOT READY") ? "Error" : r.LegalStatus != null && r.LegalStatus.Contains("OUT OF DATE") ? "Warning" : null);
            foreach (var x in exhibits.Where(x => x.Sources.Any(src => src.EasementId == r.Id)))
            {
                var stale = ExhibitPlanner.StaleSources(x, records).Any(m => m.StartsWith(r.Title, StringComparison.Ordinal));
                rows.Add(new InspectRow { Section = "Exhibits", Item = "Shown on", Value = x.LayoutName + (stale ? " -- MAY BE STALE" : " -- current"), Source = "Exhibit record " + x.Id, Layout = x.LayoutName, Handle = x.ViewportHandle, Severity = stale ? "Warning" : string.Empty });
            }
            return rows;
        }

        private static void ExclusionRow(Action<string, string, string, string, string, string> add, string section, string name, Exclusion x, EasementSettings es)
        {
            var what = x.Kind == Exclusion.PortionKind
                ? "EXCEPT " + PortionBuilder.Describe(x.PortionSteps ?? new List<PortionStep>(), es.DistanceDecimals) + (x.Of == null ? " THEREOF" : " " + (x.Description ?? "[LOT]"))
                : "EXCEPT THAT PORTION LYING WITHIN " + (x.Description ?? "[not described]");
            add(section, name, what + " -- " + (x.Effect == "HOLE" ? "interior hole" : x.Effect == "CUT" ? "cuts the outline" : "does not touch") + ", " + SqFt(x.RemovedSquareFeet) + " removed",
                x.Kind == Exclusion.PortionKind ? "User entered portion calls" + (x.Of != null ? " on object handle " + x.Of.Handle : " on the easement itself") : "Object handle " + (x.Source != null ? x.Source.Handle : "?"),
                x.Source != null ? x.Source.Handle : x.Of != null ? x.Of.Handle : null, x.Effect == "NONE" ? "Warning" : null);
        }

        internal static void ClosureRows(Action<string, string, string, string, string, string> add, RecordClosure closure, EasementSettings es)
        {
            const string c = "Closure";
            foreach (var item in closure.Items)
            {
                if (item.Cad != null)
                {
                    var cad = item.Cad;
                    add(c, item.Name + " -- CAD geometry",
                        (cad.ClosesByConstruction ? "closes by construction (gap 0.000')" : "gap " + Closure.Ft(cad.Gap)) +
                        "; perimeter " + Ft(cad.Perimeter, es) + "; area " + SqFt(cad.Area) + "; self-intersections " + (cad.SelfIntersections.Count == 0 ? "none" : cad.SelfIntersections.Count.ToString(CultureInfo.InvariantCulture)) +
                        "; overlaps " + (cad.Overlaps == 0 ? "none" : cad.Overlaps.ToString(CultureInfo.InvariantCulture)),
                        "Exact CAD polygon -- no precision ratio applies to geometry that closes by construction", null, cad.Ok ? null : "Error");
                }
                if (item.Courses != null)
                {
                    var r = item.Courses;
                    var text = new StringBuilder();
                    if (r.ClosedFigure)
                        text.Append("misclosure ").Append(Closure.Ft(r.Misclosure))
                            .Append(r.MisclosureAzimuth.HasValue ? " toward " + SurveyDirection.FormatBearing(r.MisclosureAzimuth.Value, es.BearingSecondsDecimals, "°", true) : string.Empty)
                            .Append("; perimeter ").Append(Ft(r.Perimeter, es)).Append("; precision ").Append(Closure.PrecisionText(r)).Append("; ");
                    text.Append("largest departure from the CAD corners ").Append(Closure.Ft(r.MaxDeviation))
                        .Append(r.MaxDeviation > 0 ? " at course " + r.WorstCourse : string.Empty)
                        .Append(r.Reproduces ? " -- REPRODUCES the CAD geometry" : " -- DOES NOT REPRODUCE the CAD geometry");
                    add(c, item.Name + " -- stated courses (legal)", text.ToString(),
                        "Independent traverse of the bearings, distances and curve data as stated (" + es.BearingSecondsDecimals + " decimal seconds, " + es.DistanceDecimals + " decimal feet); tolerance " + Closure.Ft(r.Tolerance),
                        null, r.Reproduces && item.Problems.Count == 0 ? null : "Error");
                }
                if (item.Cad == null && item.Courses == null)
                    add(c, item.Name, item.Problems.Count == 0 ? string.Join(" ", item.Notes.ToArray()) : string.Join(" ", item.Problems.ToArray()), "Portion calls as stated", null, item.Problems.Count == 0 ? null : "Error");
                foreach (var p in item.Problems.Where(p => item.Cad != null || item.Courses != null)) add(c, item.Name + " -- problem", p, "Closure check", null, "Error");
            }
            add(c, "Legal draft readiness", closure.Ready ? "the stated courses reproduce the CAD easement" : "NOT READY -- fix the problems above before the draft is reviewed",
                "Closure and reproduction checks", null, closure.Ready ? null : "Error");
        }

        // ================================================================ exhibit

        internal static List<InspectRow> ExhibitRows(Database db, Transaction tr, ExhibitRecord x, IList<EasementRecord> records, FtfSettings settings)
        {
            var rows = new List<InspectRow>();
            Action<string, string, string, string, string, string, string> add = (section, item, value, source, handle, layout, severity) =>
                rows.Add(new InspectRow { Section = section, Item = item, Value = value ?? "-", Source = source, Handle = handle, Layout = layout, Severity = severity ?? string.Empty });

            const string e = "Exhibit";
            add(e, "Layout", x.LayoutName, "Exhibit record " + x.Id, x.ViewportHandle, x.LayoutName, null);
            add(e, "Profile / template", string.IsNullOrWhiteSpace(x.ProfileName) ? "(drawing settings)" : x.ProfileName, "Chosen when the exhibit was made", null, null, null);
            add(e, "Created", x.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), "Exhibit record", null, null, null);
            add(e, "Last rebuild", x.RebuiltUtc.HasValue ? x.RebuiltUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : "never rebuilt", "Exhibit record", null, null, null);
            var stale = ExhibitPlanner.StaleSources(x, records);
            add(e, "Status", stale.Count == 0 ? "CURRENT with its easements" : "MAY BE STALE -- run FTFEXHIBITREBUILD", "Easement fingerprints compared with those at build", null, null, stale.Count == 0 ? null : "Warning");
            foreach (var m in stale) add(e, "Changed", m, "Fingerprint comparison", null, null, "Warning");
            add(e, "Viewport scale", "1\" = " + x.Scale.ToString("0.##", CultureInfo.InvariantCulture) + "'", x.ScaleChosenByUser ? "Chosen by the user" : "Largest profile scale that fits", x.ViewportHandle, x.LayoutName, null);
            add(e, "Viewport rotation", x.RotationDegrees.ToString("0.##", CultureInfo.InvariantCulture) + " degrees" + (Math.Abs(x.RotationDegrees) < 1e-9 ? " (north up)" : " (view only; survey geometry is not rotated)"),
                x.RotateAllowed ? "Rotation allowed for this exhibit" : "North up", x.ViewportHandle, x.LayoutName, null);
            foreach (var kv in new Dictionary<string, string> { { "Title", x.Info.Title }, { "Project", x.Info.Project }, { "Parcel", x.Info.Parcel }, { "Owner", x.Info.Owner }, { "APN", x.Info.Apn }, { "County", x.Info.County }, { "Sheet", x.Info.Sheet }, { "Prepared by", x.Info.PreparedBy }, { "Date", x.Info.Date } })
                if (!string.IsNullOrWhiteSpace(kv.Value)) add(e, kv.Key, kv.Value, "User entered", null, null, null);

            foreach (var src in x.Sources)
            {
                var r = records.FirstOrDefault(q => q.Id == src.EasementId);
                add("Easements", r != null ? r.Title : src.Title, r != null ? SqFt(r.AreaSquareFeet) + (r.Parcel != null ? "; parcel/lot " + r.Parcel.Handle : string.Empty) : "NO LONGER STORED",
                    "Easement record " + src.EasementId, r != null ? OutlineHandle(db, tr, r) : null, null, r == null ? "Error" : null);
            }

            var byKey = Ownership.FindOwnedInLayouts(db, tr, x.LayoutName, s => s.PointNumber == x.Id).ToDictionary(kv => kv.Key.Handle.ToString(), kv => kv.Key);
            foreach (var item in x.Items)
            {
                ObjectId id;
                var present = item.Handle != null && byKey.TryGetValue(item.Handle, out id);
                var note = present ? string.Empty : " -- erased from the sheet";
                string severity = present ? null : "Warning";
                if (present)
                {
                    var entity = tr.GetObject(byKey[item.Handle], OpenMode.ForRead) as AcEntity;
                    var moved = ExhibitCommands.Moved(entity, item);
                    var edited = ExhibitCommands.Edited(entity, item);
                    if (moved) note += " -- moved by hand" + (item.KeepPosition ? " (kept on rebuild)" : " (regenerated position on rebuild)");
                    if (edited) { note += " -- TEXT EDITED BY HAND (a rebuild reports a conflict and keeps the edit)"; severity = "Warning"; }
                }
                add(item.Kind == "TABLE" ? "Tables" : item.Kind == "LABEL" || item.Kind == "LEADER" || item.Kind == "DIMENSION" || item.Kind == "AREALABEL" ? "Generated annotation" : "Sheet",
                    ItemName(item.Key, records), (item.Text ?? string.Empty).Replace("\\P", " / ").Replace("\n", " / ").Replace("%%d", "\u00B0") + note,
                    "Generated by FTFEXHIBIT (" + item.Key.Split(':')[0] + ")", item.Handle, x.LayoutName, severity);
            }
            foreach (var review in x.Review)
                add("Review", review.Severity, review.Message, "Collision review at the last build", null, x.LayoutName, review.Severity == "Error" ? "Error" : "Warning");
            return rows;
        }

        // ================================================================ helpers

        /// <summary>"Course label 3 -- 20.00' WIDE UTILITY EASEMENT" for LABEL:{id}:3.</summary>
        private static string ItemName(string key, IList<EasementRecord> records)
        {
            var parts = (key ?? string.Empty).Split(':');
            if (parts.Length < 2) return key;
            var r = records.FirstOrDefault(q => q.Id == parts[1]);
            var owner = r == null ? "easement no longer stored" : r.Title;
            var what = parts.Length > 2 ? parts[2] : string.Empty;
            switch (parts[0])
            {
                case "LABEL": return "Course label " + what + " -- " + owner;
                case "POINT": return (what == "POC" ? "Commencement" : what == "POB" ? "Beginning" : what == "TERMINUS" ? "Terminus" : what) + " leader -- " + owner;
                case "DIM": return (what == "WIDTH" ? "Width dimension" : "Dimension " + what) + " -- " + owner;
                case "AREA": return "Title/area label -- " + owner;
                default: return key;
            }
        }

        private static string Kind(EasementRecord r)
        {
            if (r.IsPortion) return "Portion easement" + (r.IsTemporary ? " (temporary)" : string.Empty);
            if (r.IsArea) return "Construction area (metes and bounds)";
            if (r.IsTemporary) return "Temporary construction easement (strip)";
            return "Strip easement";
        }

        private static string OutlineHandle(Database db, Transaction tr, EasementRecord r)
        {
            var outline = Ownership.FindOwned(db, tr, s => s.PointNumber == r.Id && s.Kind == FtfEntityKind.EasementBoundary).FirstOrDefault();
            return outline.Key.IsNull ? null : outline.Key.Handle.ToString();
        }

        private static string Missing(Database db, Transaction tr, string handle)
        {
            return EasementCommands.Resolve(db, tr, handle) == null ? "Error" : null;
        }

        private static string Manual(SelectedLocation l)
        {
            return l != null && l.Source == LocationSource.Manual ? "Warning" : null;
        }

        private static string Location(SelectedLocation l)
        {
            return string.Format(CultureInfo.InvariantCulture, "N {0:0.000}  E {1:0.000}", l.Y, l.X);
        }

        private static string SourceOf(SelectedLocation l)
        {
            switch (l.Source)
            {
                case LocationSource.CogoPoint: return "COGO Point " + l.PointNumber + " (handle " + l.Handle + ")";
                case LocationSource.CadPoint: return "CAD point, object handle " + l.Handle;
                case LocationSource.GeometryEndpoint: return "Line end / vertex, object handle " + l.Handle;
                case LocationSource.Computed: return "Calculated";
                default: return "Manual selection -- typed or clicked coordinate, not tied to survey data";
            }
        }

        private static string Ft(double feet, EasementSettings es)
        {
            return EasementAnnotation.Distance(feet, es);
        }

        private static string SqFt(double sqft)
        {
            return sqft.ToString("N0", CultureInfo.InvariantCulture) + " SF / " + (sqft / EasementAnnotation.SquareFeetPerAcre).ToString("0.000", CultureInfo.InvariantCulture) + " AC";
        }

        /// <summary>The whole report as text, for a file or the clipboard.</summary>
        internal static string AsText(string title, IList<InspectRow> rows)
        {
            var sb = new StringBuilder();
            sb.AppendLine(title);
            string section = null;
            foreach (var r in rows)
            {
                if (r.Section != section) { sb.AppendLine().AppendLine("== " + r.Section.ToUpperInvariant() + " =="); section = r.Section; }
                sb.Append(r.Severity.Length > 0 ? "[" + r.Severity.ToUpperInvariant() + "] " : string.Empty).Append(r.Item).Append(": ").AppendLine(r.Value);
                if (!string.IsNullOrEmpty(r.Source)) sb.Append("    Source: ").AppendLine(r.Source);
            }
            return sb.ToString();
        }
    }
}
