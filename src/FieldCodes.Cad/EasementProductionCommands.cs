using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using FieldCodes.Easements;
using FieldCodes.Settings;

using AcEntity = Autodesk.AutoCAD.DatabaseServices.Entity;

namespace FieldCodes.Cad
{
    /// <summary>
    /// Turning single easements into production easements: exclusions ("EXCEPT ..."), further
    /// components ("TOGETHER WITH ..."), and exhibit groups (a permanent easement with its
    /// temporary one). Each change is stored on the existing easement record and the easement
    /// is rebuilt through the same rebuild FTFEASEMENTCHECK uses.
    /// </summary>
    public sealed class EasementProductionCommands
    {
        // =================================================== FTFEASEMENTEXCLUDE

        [CommandMethod("FTFEASEMENTEXCLUDE", CommandFlags.Modal)]
        public void Exclude()
        {
            FtfSession.Run("FTFEASEMENTEXCLUDE", (db, tr, ed) =>
            {
                var rules = FtfSession.Rules(db);
                var settings = FtfSession.SettingsFor(db, rules);
                var es = settings.Easements;
                var tolerance = es.ToleranceFt * settings.General.UnitsPerFoot;
                var records = DrawingStore.LoadEasements(db, tr);
                var record = PickAny(ed, tr, records, "\nSelect the easement to exclude an area from: ");
                if (record == null) { ed.WriteMessage("\nFTFEASEMENTEXCLUDE: cancelled.\n"); return; }

                var target = WhichArea(ed, record);
                if (target == null) return;
                var kind = EasementCommands.Keyword(ed, "\nExclude [Boundary/Portion] <Boundary>: ", "Boundary", "Boundary", "Portion");
                if (kind == null) return;

                var exclusion = new Exclusion { Kind = kind == "Portion" ? Exclusion.PortionKind : Exclusion.BoundaryKind };
                if (exclusion.Kind == Exclusion.BoundaryKind)
                {
                    IList<Course> courses;
                    var entity = PickClosed(db, tr, ed, "\nSelect the closed boundary to exclude (an existing easement, a building...): ", tolerance, out courses);
                    if (entity == null) return;
                    exclusion.Source = new GeometrySource { Handle = entity.Handle.ToString(), EntityType = EasementCommands.TypeName(entity), Role = "EXCLUSION", Fingerprint = EasementAnnotation.Fingerprint(courses) };
                    exclusion.Description = AskText(ed, "\nDescribe it for the legal draft (e.g. THE EXISTING 15 FOOT UTILITY EASEMENT RECORDED UNDER ...) <leave blank>: ");
                }
                else
                {
                    var on = EasementCommands.Keyword(ed, "\nMeasured on [Easement/Lot] -- \"THEREOF\" (the easement) or a lot <Easement>: ", "Easement", "Easement", "Lot");
                    if (on == null) return;
                    AcEntity basisEntity;
                    IList<Course> basis;
                    if (on == "Lot")
                    {
                        basisEntity = PickClosed(db, tr, ed, "\nSelect the lot boundary the distances are measured on: ", tolerance, out basis);
                        if (basisEntity == null) return;
                        exclusion.Of = new GeometrySource { Handle = basisEntity.Handle.ToString(), EntityType = EasementCommands.TypeName(basisEntity), Role = "EXCLUSION OF", Fingerprint = EasementAnnotation.Fingerprint(basis) };
                        exclusion.Description = AskText(ed, "\nName that lot for the legal draft (e.g. LOT 2, SHORT PLAT NO. ...) <leave blank>: ");
                    }
                    else
                    {
                        basisEntity = OutlineOf(db, tr, record, target);
                        basis = basisEntity == null ? null : EasementCommands.Extract(basisEntity, out _);
                        if (basis == null) { ed.WriteMessage("\nFTFEASEMENTEXCLUDE: the easement's outline is not in the drawing -- run FTFEASEMENTCHECK first.\n"); return; }
                    }
                    bool cancelled;
                    var steps = EasementAreaCommands.GatherCalls(db, tr, ed, es, basisEntity, basis, tolerance, out cancelled);
                    if (cancelled || steps == null || steps.Count == 0) { ed.WriteMessage("\nFTFEASEMENTEXCLUDE: cancelled.\n"); return; }
                    if (!NotFtfDrafting(db, tr, ed, steps, exclusion.Of)) return;
                    if (on == "Easement")
                    {
                        // Lines of the easement's own outline are rebuilt with it: keep their geometry, not their handle.
                        foreach (var s in steps.Where(s => s.Handle == basisEntity.Handle.ToString())) { s.Handle = null; s.Segment = 0; }
                    }
                    exclusion.PortionSteps = steps;
                }

                var list = target == "A" ? (record.Exclusions = record.Exclusions ?? new List<Exclusion>()) : record.Components.First(c => c.Label == target).Exclusions;
                list.Add(exclusion);
                var before = record.AreaSquareFeet;
                string problem;
                var rebuilt = EasementCommands.RebuildRecord(db, tr, ed, settings, rules.Version, record, "after an exclusion was added", out problem);
                if (rebuilt == null) throw new InvalidOperationException("The exclusion could not be applied: " + problem);
                var applied = target == "A" ? rebuilt.Exclusions.Last() : rebuilt.Components.First(c => c.Label == target).Exclusions.Last();
                ed.WriteMessage("\nFTFEASEMENTEXCLUDE: {0} -- {1}; {2:N0} sq ft removed; area {3:N0} -> {4:N0} sq ft. The legal draft marks it for surveyor review.\n",
                                rebuilt.Title, applied.Effect == "HOLE" ? "an interior hole" : applied.Effect == "CUT" ? "cut from the outline" : "it does not touch the easement",
                                applied.RemovedSquareFeet, before, rebuilt.AreaSquareFeet);
            });
        }

        // ================================================= FTFEASEMENTCOMPONENT

        [CommandMethod("FTFEASEMENTCOMPONENT", CommandFlags.Modal)]
        public void AddComponent()
        {
            FtfSession.Run("FTFEASEMENTCOMPONENT", (db, tr, ed) =>
            {
                var rules = FtfSession.Rules(db);
                var settings = FtfSession.SettingsFor(db, rules);
                var es = settings.Easements;
                var tolerance = es.ToleranceFt * settings.General.UnitsPerFoot;
                var records = DrawingStore.LoadEasements(db, tr);
                var record = PickAny(ed, tr, records, "\nSelect the easement to add a component to: ");
                if (record == null) { ed.WriteMessage("\nFTFEASEMENTCOMPONENT: cancelled.\n"); return; }

                var kind = EasementCommands.Keyword(ed, "\nComponent [Portion/Area/Boundary] <Portion>: ", "Portion", "Portion", "Area", "Boundary");
                if (kind == null) return;
                var connector = EasementCommands.Keyword(ed, "\nJoined in the description by [TogetherWith/And/Also/Later] <Later>: ", "Later", "TogetherWith", "And", "Also", "Later");
                if (connector == null) return;

                record.Components = record.Components ?? new List<EasementComponent>();
                var component = new EasementComponent
                {
                    Label = RegionBuilder.NextLabel(record.Components),
                    Connector = connector == "TogetherWith" ? "TOGETHER WITH" : connector == "And" ? "AND" : connector == "Also" ? "ALSO" : null
                };
                if (kind == "Portion")
                {
                    IList<Course> lot;
                    var lotEntity = PickClosed(db, tr, ed, "\nSelect the lot boundary for this component: ", tolerance, out lot);
                    if (lotEntity == null) return;
                    bool cancelled;
                    var steps = EasementAreaCommands.GatherCalls(db, tr, ed, es, lotEntity, lot, tolerance, out cancelled);
                    if (cancelled || steps == null || steps.Count == 0) { ed.WriteMessage("\nFTFEASEMENTCOMPONENT: cancelled.\n"); return; }
                    if (!NotFtfDrafting(db, tr, ed, steps, null)) return;
                    component.Kind = EasementComponent.PortionKind;
                    component.Parcel = new GeometrySource { Handle = lotEntity.Handle.ToString(), EntityType = EasementCommands.TypeName(lotEntity), Role = "LOT", Fingerprint = EasementAnnotation.Fingerprint(lot) };
                    component.PortionSteps = steps;
                }
                else if (kind == "Area")
                {
                    var clicked = EasementAreaCommands.GatherCorners(db, tr, ed, tolerance, "FTFEASEMENTCOMPONENT");
                    if (clicked == null) return;
                    component.Kind = EasementComponent.AreaKind;
                    component.PointOfCommencement = clicked.Poc;
                    component.CommencementTieFollows = clicked.TieFollows.Count > 0 ? clicked.TieFollows : null;
                    component.AnglePoints = clicked.Corners;
                    component.AreaSides = clicked.Sides;
                }
                else
                {
                    IList<Course> courses;
                    var entity = PickClosed(db, tr, ed, "\nSelect the closed boundary of this component: ", tolerance, out courses);
                    if (entity == null) return;
                    component.Kind = EasementComponent.BoundaryKind;
                    component.Parcel = new GeometrySource { Handle = entity.Handle.ToString(), EntityType = EasementCommands.TypeName(entity), Role = "BOUNDARY", Fingerprint = EasementAnnotation.Fingerprint(courses) };
                }

                record.Components.Add(component);
                var before = record.AreaSquareFeet;
                string problem;
                var rebuilt = EasementCommands.RebuildRecord(db, tr, ed, settings, rules.Version, record, "after component " + component.Label + " was added", out problem);
                if (rebuilt == null) throw new InvalidOperationException("The component could not be added: " + problem);
                var added = rebuilt.Components.First(c => c.Label == component.Label);
                ed.WriteMessage("\nFTFEASEMENTCOMPONENT: component {0} of {1}, {2:N0} sq ft; total {3:N0} -> {4:N0} sq ft.{5}\n",
                                added.Label, rebuilt.Title, added.AreaSquareFeet, before, rebuilt.AreaSquareFeet,
                                added.Connector == null ? " The connector (TOGETHER WITH / AND / ALSO) is left for the surveyor." : string.Empty);
            });
        }

        // ===================================================== FTFEASEMENTGROUP

        [CommandMethod("FTFEASEMENTGROUP", CommandFlags.Modal)]
        public void Group()
        {
            FtfSession.Run("FTFEASEMENTGROUP", (db, tr, ed) =>
            {
                var rules = FtfSession.Rules(db);
                var settings = FtfSession.SettingsFor(db, rules);
                var records = DrawingStore.LoadEasements(db, tr);
                var chosen = new List<EasementRecord>();
                while (true)
                {
                    var record = PickAny(ed, tr, records, chosen.Count == 0 ? "\nSelect an easement for the exhibit group: " : "\nNext easement for the group <done>: ", chosen.Count > 0);
                    if (record == null) break;
                    if (chosen.Any(r => r.Id == record.Id)) { ed.WriteMessage("\n  Already in the group."); continue; }
                    chosen.Add(record);
                    ed.WriteMessage("\n  {0}", record.Title);
                }
                if (chosen.Count == 0) { ed.WriteMessage("\nFTFEASEMENTGROUP: cancelled.\n"); return; }

                // A strip easement and its temporary construction easement travel together.
                foreach (var linked in records.Where(r => r.GroupId != null && chosen.Any(c => c.GroupId == r.GroupId) && chosen.All(c => c.Id != r.Id)).ToList())
                {
                    chosen.Add(linked);
                    ed.WriteMessage("\n  {0} (linked)", linked.Title);
                }

                var existing = chosen.Select(r => r.ExhibitGroup).FirstOrDefault(g => !string.IsNullOrWhiteSpace(g));
                var fallback = existing ?? "EXHIBIT GROUP " + (records.Select(r => r.ExhibitGroup).Where(g => !string.IsNullOrWhiteSpace(g)).Distinct().Count() + 1);
                var name = ed.GetString(new PromptStringOptions("\nGroup name (NONE to take them out of any group) <" + fallback + ">: ") { AllowSpaces = true });
                if (name.Status == PromptStatus.Cancel) return;
                var group = name.Status == PromptStatus.OK && !string.IsNullOrWhiteSpace(name.StringResult) ? name.StringResult.Trim().ToUpperInvariant() : fallback;
                if (group == "NONE") group = null;

                foreach (var record in chosen)
                {
                    record.ExhibitGroup = group;
                    var restyle = false;
                    if (record.IsPortion)
                    {
                        // Permanent and temporary easements look different, as the profile says.
                        var answer = EasementCommands.Keyword(ed, "\n  " + record.Title + " is [Permanent/Temporary] <" + (record.IsTemporary ? "Temporary" : "Permanent") + ">: ",
                                                              record.IsTemporary ? "Temporary" : "Permanent", "Permanent", "Temporary");
                        if (answer == null) return;
                        var role = answer == "Temporary" ? EasementRecord.TemporaryRole : EasementRecord.PermanentRole;
                        restyle = role != (record.Role ?? EasementRecord.PermanentRole);
                        record.Role = role;
                    }
                    if (restyle)
                    {
                        string problem;
                        var rebuilt = EasementCommands.RebuildRecord(db, tr, ed, settings, rules.Version, record, "to restyle it as " + record.Role.ToLowerInvariant(), out problem);
                        if (rebuilt == null) { ed.WriteMessage("\n  Not restyled: " + problem); DrawingStore.SaveEasement(db, tr, record); }
                    }
                    else
                        DrawingStore.SaveEasement(db, tr, record);
                }
                ed.WriteMessage(group == null
                    ? "\nFTFEASEMENTGROUP: {0} easement(s) taken out of their group.\n"
                    : "\nFTFEASEMENTGROUP: {0} easement(s) in \"" + group + "\"; FTFEXHIBIT shows them together.\n", chosen.Count);
            });
        }

        // ================================================================ helpers

        /// <summary>Any stored easement by picking its drafting, including a temporary construction easement.</summary>
        internal static EasementRecord PickAny(Editor ed, Transaction tr, IList<EasementRecord> records, string message, bool allowNone = false)
        {
            if (records.Count == 0) { ed.WriteMessage("\n  No easements are stored in this drawing."); return null; }
            while (true)
            {
                var options = new PromptEntityOptions(message) { AllowNone = allowNone };
                var picked = ed.GetEntity(options);
                if (picked.Status != PromptStatus.OK) return null;
                var stamp = Ownership.Read((AcEntity)tr.GetObject(picked.ObjectId, OpenMode.ForRead));
                var record = stamp == null ? null : records.FirstOrDefault(r => r.Id == stamp.PointNumber);
                if (record != null) return record;
                // On a busy survey drawing the pick often lands on topography or a label over the easement: a click
                // inside a stored easement's boundary picks it (the smallest one when they nest).
                var at = picked.PickedPoint.TransformBy(ed.CurrentUserCoordinateSystem);
                var inside = records.Where(r => r.BoundaryCourses != null && r.BoundaryCourses.Count > 0 &&
                                                StripTrim.Inside(r.BoundaryCourses.Select(d => d.Course).ToList(), new P2(at.X, at.Y)))
                                    .OrderBy(r => r.AreaSquareFeet).FirstOrDefault();
                if (inside != null) { ed.WriteMessage("\n  Picked inside {0}.", inside.Title); return inside; }
                ed.WriteMessage("\n  That is not part of a stored FTF easement.");
            }
        }

        private static string WhichArea(Editor ed, EasementRecord record)
        {
            if (record.Components == null || record.Components.Count == 0) return "A";
            var labels = new[] { "A" }.Concat(record.Components.Select(c => c.Label)).ToArray();
            return EasementCommands.Keyword(ed, "\nFrom component [" + string.Join("/", labels) + "] <A>: ", "A", labels);
        }

        private static AcEntity PickClosed(Database db, Transaction tr, Editor ed, string message, double tolerance, out IList<Course> courses)
        {
            courses = null;
            PromptStatus status;
            Autodesk.AutoCAD.Geometry.Point3d click;
            var entity = EasementCommands.PickCurve(db, tr, ed, message, false, out status, out click);
            if (entity == null) return null;
            string problem;
            courses = EasementCommands.Extract(entity, out problem);
            if (courses == null || Loops.LargestGap(courses) > tolerance)
            {
                ed.WriteMessage("\n  That boundary is not closed" + (problem != null ? " (" + problem + ")" : string.Empty) + ".");
                courses = null;
                return null;
            }
            return entity;
        }

        /// <summary>The drawn outline of the easement (A) or a component, to measure "THEREOF" portions on.</summary>
        private static AcEntity OutlineOf(Database db, Transaction tr, EasementRecord record, string label)
        {
            var target = label == "A" ? record.BoundaryCourses : record.Components.First(c => c.Label == label).BoundaryCourses;
            if (target == null || target.Count == 0) return null;
            var fingerprint = EasementAnnotation.Fingerprint(target.Select(d => d.Course));
            foreach (var pair in Ownership.FindOwned(db, tr, s => s.PointNumber == record.Id &&
                                                          (s.Kind == FtfEntityKind.EasementBoundary || s.Kind == FtfEntityKind.EasementLine)))
            {
                var outline = tr.GetObject(pair.Key, OpenMode.ForRead) as Polyline;
                if (outline == null || !outline.Closed) continue;
                string problem;
                var courses = EasementCommands.Extract(outline, out problem);
                if (courses != null && EasementAnnotation.Fingerprint(courses) == fingerprint) return outline;
            }
            return null;
        }

        /// <summary>Portion calls must be measured from survey lines, not from FTF's own drafting that a rebuild replaces.</summary>
        private static bool NotFtfDrafting(Database db, Transaction tr, Editor ed, IList<PortionStep> steps, GeometrySource basis)
        {
            foreach (var s in steps)
            {
                var entity = EasementCommands.Resolve(db, tr, s.Handle);
                var stamp = entity == null ? null : Ownership.Read(entity);
                if (stamp != null && stamp.Kind >= FtfEntityKind.EasementBoundary && stamp.Kind <= FtfEntityKind.EasementTable &&
                    (basis == null || basis.Handle != s.Handle))
                {
                    // The easement outline itself is allowed only as the THEREOF basis; the caller clears its handle.
                    if (stamp.Kind == FtfEntityKind.EasementBoundary || stamp.Kind == FtfEntityKind.EasementLine) continue;
                    ed.WriteMessage("\n  The " + (s.Side ?? string.Empty).ToLowerInvariant() + " line is FTF drafting; pick the survey lot line instead.");
                    return false;
                }
            }
            return true;
        }

        private static string AskText(Editor ed, string message)
        {
            var answer = ed.GetString(new PromptStringOptions(message) { AllowSpaces = true });
            return answer.Status == PromptStatus.OK && !string.IsNullOrWhiteSpace(answer.StringResult) ? answer.StringResult.Trim().ToUpperInvariant() : null;
        }
    }
}
