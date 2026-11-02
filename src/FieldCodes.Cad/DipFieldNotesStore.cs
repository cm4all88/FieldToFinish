using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using FieldCodes.Utilities;
using FieldCodes.Utilities.FieldNotes;

namespace FieldCodes.Cad
{
    /// <summary>
    /// The field-notes header (project name and number, date of observation, crew) kept in the drawing, so the next
    /// Save Field Notes PDF -- or an automatic one -- starts from what the drafter typed last time. Stored as JSON in
    /// one Xrecord under FTF's own dictionary in the named object dictionary, beside the dip project, so it travels
    /// with the DWG and undoes with the command that wrote it. Nothing else lives under this key.
    /// </summary>
    internal static class DipFieldNotesStore
    {
        private const string Root = "FTF_FIELDTOFINISH";
        private const string Key = "DIPFIELDNOTES";

        public static FieldNotesHeader Load(Database db, Transaction tr)
        {
            var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
            if (!nod.Contains(Root)) return new FieldNotesHeader();
            var root = (DBDictionary)tr.GetObject(nod.GetAt(Root), OpenMode.ForRead);
            if (!root.Contains(Key)) return new FieldNotesHeader();
            var xrecord = tr.GetObject(root.GetAt(Key), OpenMode.ForRead) as Xrecord;
            if (xrecord == null) return new FieldNotesHeader();
            using (var data = xrecord.Data)
            {
                if (data == null) return new FieldNotesHeader();
                return FieldNotesHeader.FromJson(TextChunks.Join(data.AsArray()
                    .Where(tv => tv.TypeCode == (int)DxfCode.Text)
                    .Select(tv => (string)tv.Value)));
            }
        }

        public static void Save(Database db, Transaction tr, FieldNotesHeader header)
        {
            var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
            DBDictionary root;
            if (nod.Contains(Root)) root = (DBDictionary)tr.GetObject(nod.GetAt(Root), OpenMode.ForWrite);
            else
            {
                nod.UpgradeOpen();
                root = new DBDictionary();
                nod.SetAt(Root, root);
                tr.AddNewlyCreatedDBObject(root, true);
            }

            var values = TextChunks.Split((header ?? new FieldNotesHeader()).ToJson())
                .Select(chunk => new TypedValue((int)DxfCode.Text, chunk)).ToArray();
            using (var buffer = new ResultBuffer(values.Length == 0 ? new[] { new TypedValue((int)DxfCode.Text, string.Empty) } : values))
            {
                if (root.Contains(Key))
                {
                    var existing = (Xrecord)tr.GetObject(root.GetAt(Key), OpenMode.ForWrite);
                    existing.Data = buffer;
                    return;
                }
                var xrecord = new Xrecord { Data = buffer };
                root.SetAt(Key, xrecord);
                tr.AddNewlyCreatedDBObject(xrecord, true);
            }
        }

        /// <summary>
        /// The header to start a save from: the one stored in the drawing; where that has no project name or number,
        /// the project and number the drafter typed for an exhibit in this drawing. Nothing is made up -- whatever
        /// neither has stays blank and prints as "not recorded".
        /// </summary>
        public static FieldNotesHeader Suggest(Database db, Transaction tr)
        {
            var header = Load(db, tr);
            if (!string.IsNullOrWhiteSpace(header.ProjectName) && !string.IsNullOrWhiteSpace(header.ProjectNumber)) return header;
            foreach (var exhibit in DrawingStore.LoadExhibits(db, tr))
            {
                var info = exhibit != null ? exhibit.Info : null;
                if (info == null) continue;
                if (string.IsNullOrWhiteSpace(header.ProjectName) && !string.IsNullOrWhiteSpace(info.Project)) header.ProjectName = info.Project.Trim();
                if (string.IsNullOrWhiteSpace(header.ProjectNumber) && !string.IsNullOrWhiteSpace(info.ProjectNumber)) header.ProjectNumber = info.ProjectNumber.Trim();
                if (!string.IsNullOrWhiteSpace(header.ProjectName) && !string.IsNullOrWhiteSpace(header.ProjectNumber)) break;
            }
            return header;
        }
    }
}
