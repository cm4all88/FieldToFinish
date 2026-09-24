using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace FieldCodes.Cad
{
    /// <summary>What a plugin-created entity is. Stored in XData so a re-run can
    /// delete exactly the entities a given command produced and nothing else.</summary>
    public enum FtfEntityKind : short
    {
        Unknown = 0,
        Block = 1,
        DripArc = 2,
        Label = 3,
        Mask = 4,
        Leader = 5,
        /// <summary>A tag such as T1 on the plan. Carries its number in XData so a
        /// re-run reissues the same one.</summary>
        Tag = 6,
        /// <summary>The model-space schedule.</summary>
        Table = 7,
        /// <summary>A label placed along existing Civil 3D linework. Its own kind so
        /// FTFLINELABELS can regenerate without touching point labels.</summary>
        LineLabel = 8,
        /// <summary>The wipeout under a line label. Separate from Mask so FTFLABELS
        /// cannot erase it.</summary>
        LineMask = 9,
        /// <summary>A survey line deliberately drafted through FTFDRAWLINE. Unlike
        /// every other kind, this is the user's own work rather than pipeline output:
        /// FTFCLEAN leaves it alone, and FTFDRAFTCLEAN erases it only after explicit
        /// confirmation.</summary>
        DraftLine = 10,
        /// <summary>Bearing/distance annotation on a drafted course. Derived from the
        /// drafted geometry, so it can be cleaned separately from it.</summary>
        DraftAnnotation = 11,
        /// <summary>The wipeout under drafted-course annotation.</summary>
        DraftMask = 12,
        /// <summary>A sheet's plot-window footprint drawn in model space.</summary>
        SheetArea = 13,
        /// <summary>A match line between adjacent sheets, with its labels.</summary>
        MatchLine = 14,
        /// <summary>The key map index diagram on a layout's paper space.</summary>
        KeyMap = 15,
        /// <summary>A spot elevation: the X marker and its elevation text.</summary>
        Spot = 16,
        /// <summary>The legend of line features present in the drawing.</summary>
        Legend = 17,
        /// <summary>The control point table.</summary>
        ControlTable = 18,

        // Production drafting. Like DraftLine these are deliberate deliverable
        // drafting, so FTFCLEAN leaves them alone; each tool manages its own.

        /// <summary>A storm/sewer pipe line drawn from confirmed dip observations.</summary>
        UtilityPipe = 19,
        /// <summary>The label along a drafted pipe.</summary>
        UtilityPipeLabel = 20,
        /// <summary>The leadered structure label (rim, inverts, bottom, water).</summary>
        StructureLabel = 21,
        /// <summary>The closed easement boundary.</summary>
        EasementBoundary = 22,
        /// <summary>Easement sidelines and centerline.</summary>
        EasementLine = 23,
        /// <summary>Easement hatch.</summary>
        EasementHatch = 24,
        /// <summary>Easement width dimensions.</summary>
        EasementDimension = 25,
        /// <summary>Easement title, area, course labels and tags.</summary>
        EasementText = 26,
        /// <summary>Easement line/curve table.</summary>
        EasementTable = 27,

        // Easement exhibits: paper-space entities owned by an exhibit (PointNumber = exhibit id,
        // TagText = the item key).
        /// <summary>The exhibit's main viewport.</summary>
        ExhibitViewport = 28,
        /// <summary>Title, notes, information and other exhibit text.</summary>
        ExhibitText = 29,
        /// <summary>Area and line/curve tables on the sheet.</summary>
        ExhibitTable = 30,
        /// <summary>North arrow, scale bar, legend graphics.</summary>
        ExhibitSymbol = 31,
        /// <summary>Course, easement, parcel and point labels placed on the sheet.</summary>
        ExhibitLabel = 32,
        /// <summary>Border or inserted title block.</summary>
        ExhibitBorder = 33,
        /// <summary>Width dimensions drawn on the sheet.</summary>
        ExhibitDimension = 34,

        // Recorded survey reconstruction (FTFRECORD). Deliberate production drafting like the
        // easements: FTFCLEAN leaves it alone; FTFRECORDREBUILD manages it per project.
        // PointNumber = project id, TagText = call / monument id.

        /// <summary>A course of a recorded survey built from its written call (a line).</summary>
        RecordLine = 35,
        /// <summary>A curve of a recorded survey built from its stated elements.</summary>
        RecordCurve = 36,
        /// <summary>A bearing/distance or curve label on a reconstructed course (plain text or Civil 3D label).</summary>
        RecordLabel = 37,
        /// <summary>The wipeout under a record label.</summary>
        RecordMask = 38,
        /// <summary>A monument symbol at a reconstructed corner.</summary>
        RecordMonument = 39,
        /// <summary>The line/curve table for tagged courses.</summary>
        RecordTable = 40,
        /// <summary>Lot, tract and area text inside a figure.</summary>
        RecordText = 41
    }

    /// <summary>The XData this plugin stamps on every entity it creates.</summary>
    public sealed class FtfStamp
    {
        public string SchemaVersion { get; set; }
        public string RulesVersion { get; set; }
        public string PointNumber { get; set; }

        /// <summary>
        /// Tag text such as T1, on tag entities. This is what makes tag numbers stable:
        /// the number lives with the entity, so a re-run reads it back rather than
        /// recomputing it and risking a renumber on an issued drawing.
        /// </summary>
        public string TagText { get; set; }

        public FtfEntityKind Kind { get; set; }

        /// <summary>Where the plugin last computed this entity should go. Labels only.
        /// If the entity is no longer here, a human moved it -- see <see cref="WasMovedByHand"/>.</summary>
        public Point3d? ComputedPosition { get; set; }

        public bool WasMovedByHand(Point3d actual, double tolerance)
        {
            if (!ComputedPosition.HasValue) return false;
            return ComputedPosition.Value.DistanceTo(actual) > tolerance;
        }
    }

    /// <summary>
    /// Ownership marking and cleanup.
    ///
    /// Every entity this plugin creates carries XData naming the source point and the
    /// rules version that produced it. Every command deletes what it owns before it
    /// draws, so running a command twice is a no-op rather than a duplicate. Nothing
    /// is ever deleted on the basis of layer or block name -- only on our own mark.
    /// </summary>
    public static class Ownership
    {
        /// <summary>Registered application name for our XData. Never change this:
        /// entities stamped with the old name would become unowned and undeletable.</summary>
        public const string AppName = "FTF_FIELDTOFINISH";

        /// <summary>Layout of the XData record itself, separate from the rules version.</summary>
        public const string SchemaVersion = "ftf-1";

        // ------------------------------------------------------------------ writing

        /// <summary>Adds our application to the RegApp table if it is not already there.
        /// XData attached under an unregistered application name is silently discarded.</summary>
        public static void EnsureRegApp(Database db, Transaction tr)
        {
            if (db == null) throw new ArgumentNullException("db");
            if (tr == null) throw new ArgumentNullException("tr");

            var table = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
            if (table.Has(AppName)) return;

            table.UpgradeOpen();
            using (var record = new RegAppTableRecord())
            {
                record.Name = AppName;
                table.Add(record);
                tr.AddNewlyCreatedDBObject(record, true);
            }
        }

        /// <summary>
        /// Stamps an entity as ours. The entity must already be database-resident.
        /// </summary>
        public static void Stamp(Entity entity, string pointNumber, string rulesVersion,
                                 FtfEntityKind kind, Point3d? computedPosition,
                                 string tagText = null)
        {
            if (entity == null) throw new ArgumentNullException("entity");

            // A drawing no FTF command has touched yet has no registered application, and
            // AutoCAD refuses the XData: register it here so no command can forget to.
            var db = entity.Database;
            var top = db != null ? db.TransactionManager.TopTransaction : null;
            if (top != null) EnsureRegApp(db, top);

            // String order is the read order in Read(); a fourth string is appended
            // rather than inserted, so entities stamped by an earlier build still parse.
            var values = new List<TypedValue>
            {
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, AppName),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, SchemaVersion),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, Clip(rulesVersion)),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, Clip(pointNumber)),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, Clip(tagText ?? string.Empty)),
                new TypedValue((int)DxfCode.ExtendedDataInteger16, (short)kind)
            };

            // ExtendedDataXCoordinate (1010), NOT WorldXCoordinate (1011): AutoCAD
            // transforms 1011 values along with the entity, so a hand-moved label's
            // stamp would move WITH it and the move could never be detected. 1010 is
            // plain data the host never touches -- the whole point of the stamp.
            if (computedPosition.HasValue)
                values.Add(new TypedValue((int)DxfCode.ExtendedDataXCoordinate,
                                          computedPosition.Value));

            using (var rb = new ResultBuffer(values.ToArray()))
            {
                entity.XData = rb;
            }
        }

        /// <summary>XData strings cap at 255 characters.</summary>
        private static string Clip(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Length <= 255 ? s : s.Substring(0, 255);
        }

        // ------------------------------------------------------------------ reading

        /// <summary>Reads our stamp, or null if the entity is not ours.</summary>
        public static FtfStamp Read(Entity entity)
        {
            if (entity == null) return null;

            using (var rb = entity.GetXDataForApplication(AppName))
            {
                if (rb == null) return null;

                var stamp = new FtfStamp();
                var stringIndex = 0;

                foreach (var tv in rb)
                {
                    switch ((DxfCode)tv.TypeCode)
                    {
                        case DxfCode.ExtendedDataRegAppName:
                            break;

                        case DxfCode.ExtendedDataAsciiString:
                            var s = tv.Value as string;
                            if (stringIndex == 0) stamp.SchemaVersion = s;
                            else if (stringIndex == 1) stamp.RulesVersion = s;
                            else if (stringIndex == 2) stamp.PointNumber = s;
                            else if (stringIndex == 3) stamp.TagText = s;
                            stringIndex++;
                            break;

                        case DxfCode.ExtendedDataInteger16:
                            stamp.Kind = (FtfEntityKind)Convert.ToInt16(tv.Value);
                            break;

                        // 1010 is the current form; 1011 is read for entities stamped
                        // by earlier builds (their positions travelled with any manual
                        // move, so moves made before this fix are undetectable -- but
                        // the stamp still parses).
                        case DxfCode.ExtendedDataXCoordinate:
                        case DxfCode.ExtendedDataWorldXCoordinate:
                            if (tv.Value is Point3d) stamp.ComputedPosition = (Point3d)tv.Value;
                            break;
                    }
                }

                return stamp;
            }
        }

        public static bool IsOurs(Entity entity)
        {
            return Read(entity) != null;
        }

        // ------------------------------------------------------------------ deleting

        /// <summary>
        /// Erases every entity in model space that we own and that matches the predicate.
        /// Written before anything that creates entities, so that no command can be
        /// run twice and leave duplicates behind.
        /// </summary>
        /// <returns>Number of entities erased.</returns>
        public static int DeleteOwned(Database db, Transaction tr, Func<FtfStamp, bool> predicate)
        {
            if (db == null) throw new ArgumentNullException("db");
            if (tr == null) throw new ArgumentNullException("tr");

            // Two passes on purpose. Erasing while enumerating a BlockTableRecord
            // mutates the collection being walked, which is undefined behaviour in the
            // AutoCAD API -- it survives small counts and starts skipping or throwing
            // once a drawing owns a few hundred entities. Everything re-runnable in
            // this plugin goes through here, so it collects first and erases after.
            var doomed = new List<ObjectId>();

            var msId = SymbolUtilityServices.GetBlockModelSpaceId(db);
            var ms = (BlockTableRecord)tr.GetObject(msId, OpenMode.ForRead);

            foreach (ObjectId id in ms)
            {
                if (id.IsErased) continue;

                var entity = tr.GetObject(id, OpenMode.ForRead, false, true) as Entity;
                if (entity == null) continue;

                var stamp = Read(entity);
                if (stamp == null) continue;
                if (predicate != null && !predicate(stamp)) continue;

                doomed.Add(id);
            }

            var erased = 0;
            for (var i = 0; i < doomed.Count; i++)
            {
                if (doomed[i].IsErased) continue;

                var entity = tr.GetObject(doomed[i], OpenMode.ForWrite, false, true) as Entity;
                if (entity == null) continue;

                entity.Erase();
                erased++;
            }

            return erased;
        }

        /// <summary>Erases everything we own, of any kind.</summary>
        public static int DeleteAllOwned(Database db, Transaction tr)
        {
            return DeleteOwned(db, tr, null);
        }

        /// <summary>Erases only the given kinds.</summary>
        public static int DeleteOwnedKinds(Database db, Transaction tr, params FtfEntityKind[] kinds)
        {
            var wanted = new HashSet<FtfEntityKind>(kinds ?? new FtfEntityKind[0]);
            return DeleteOwned(db, tr, s => wanted.Contains(s.Kind));
        }

        /// <summary>
        /// Collects our entities without erasing them. Used by label placement, which
        /// must leave a hand-moved label alone but still treat it as an obstacle.
        /// </summary>
        /// <summary>Owned entities in one paper-space layout (or in every layout when the name is null).</summary>
        public static IList<KeyValuePair<ObjectId, FtfStamp>> FindOwnedInLayouts(
            Database db, Transaction tr, string layoutName, Func<FtfStamp, bool> predicate)
        {
            var found = new List<KeyValuePair<ObjectId, FtfStamp>>();
            var layouts = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
            foreach (DBDictionaryEntry entry in layouts)
            {
                var layout = (Layout)tr.GetObject(entry.Value, OpenMode.ForRead);
                if (layout.ModelType) continue;
                if (layoutName != null && !string.Equals(layout.LayoutName, layoutName, StringComparison.OrdinalIgnoreCase)) continue;
                var space = (BlockTableRecord)tr.GetObject(layout.BlockTableRecordId, OpenMode.ForRead);
                foreach (ObjectId id in space)
                {
                    if (id.IsErased) continue;
                    var entity = tr.GetObject(id, OpenMode.ForRead, false, true) as Entity;
                    if (entity == null) continue;
                    var stamp = Read(entity);
                    if (stamp == null || (predicate != null && !predicate(stamp))) continue;
                    found.Add(new KeyValuePair<ObjectId, FtfStamp>(id, stamp));
                }
            }
            return found;
        }

        public static IList<KeyValuePair<ObjectId, FtfStamp>> FindOwned(
            Database db, Transaction tr, Func<FtfStamp, bool> predicate)
        {
            var found = new List<KeyValuePair<ObjectId, FtfStamp>>();
            var msId = SymbolUtilityServices.GetBlockModelSpaceId(db);
            var ms = (BlockTableRecord)tr.GetObject(msId, OpenMode.ForRead);

            foreach (ObjectId id in ms)
            {
                if (id.IsErased) continue;

                var entity = tr.GetObject(id, OpenMode.ForRead, false, true) as Entity;
                if (entity == null) continue;

                var stamp = Read(entity);
                if (stamp == null) continue;
                if (predicate != null && !predicate(stamp)) continue;

                found.Add(new KeyValuePair<ObjectId, FtfStamp>(id, stamp));
            }

            return found;
        }
    }
}
