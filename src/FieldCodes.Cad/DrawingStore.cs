using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using FieldCodes.Utilities;

namespace FieldCodes.Cad
{
    /// <summary>
    /// FTF data that lives INSIDE the drawing: the storm/sewer dip project, the
    /// easement records and the selected drafting profile. Stored as Xrecords under
    /// one dictionary in the named object dictionary, so the data travels with the
    /// DWG, survives reopening, and every change is part of AutoCAD's undo.
    /// </summary>
    internal static class DrawingStore
    {
        private const string Root = "FTF_FIELDTOFINISH";
        private const string DipsKey = "DIPS";
        private const string EasementsKey = "EASEMENTS";
        private const string ProfileKey = "PROFILE";

        // ------------------------------------------------------------- raw text

        private static DBDictionary RootDictionary(Transaction tr, Database db, bool create)
        {
            var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
            if (nod.Contains(Root))
                return (DBDictionary)tr.GetObject(nod.GetAt(Root), create ? OpenMode.ForWrite : OpenMode.ForRead);
            if (!create) return null;

            nod.UpgradeOpen();
            var dict = new DBDictionary();
            nod.SetAt(Root, dict);
            tr.AddNewlyCreatedDBObject(dict, true);
            return dict;
        }

        private static DBDictionary SubDictionary(Transaction tr, Database db, string key, bool create)
        {
            var root = RootDictionary(tr, db, create);
            if (root == null) return null;
            if (root.Contains(key))
                return (DBDictionary)tr.GetObject(root.GetAt(key), create ? OpenMode.ForWrite : OpenMode.ForRead);
            if (!create) return null;

            var dict = new DBDictionary();
            root.SetAt(key, dict);
            tr.AddNewlyCreatedDBObject(dict, true);
            return dict;
        }

        private static string ReadText(Transaction tr, DBDictionary dict, string key)
        {
            if (dict == null || !dict.Contains(key)) return null;
            var xrecord = tr.GetObject(dict.GetAt(key), OpenMode.ForRead) as Xrecord;
            if (xrecord == null) return null;
            using (var data = xrecord.Data)
            {
                if (data == null) return null;
                return TextChunks.Join(data.AsArray()
                    .Where(tv => tv.TypeCode == (int)DxfCode.Text)
                    .Select(tv => (string)tv.Value));
            }
        }

        private static void WriteText(Transaction tr, DBDictionary dict, string key, string text)
        {
            var values = TextChunks.Split(text ?? string.Empty)
                .Select(chunk => new TypedValue((int)DxfCode.Text, chunk))
                .ToArray();

            using (var buffer = new ResultBuffer(values.Length == 0
                       ? new[] { new TypedValue((int)DxfCode.Text, string.Empty) }
                       : values))
            {
                if (dict.Contains(key))
                {
                    var existing = (Xrecord)tr.GetObject(dict.GetAt(key), OpenMode.ForWrite);
                    existing.Data = buffer;
                    return;
                }

                var xrecord = new Xrecord { Data = buffer };
                dict.SetAt(key, xrecord);
                tr.AddNewlyCreatedDBObject(xrecord, true);
            }
        }

        // ------------------------------------------------------------ dip project

        public static UtilityProject LoadDips(Database db, Transaction tr)
        {
            var root = RootDictionary(tr, db, false);
            return UtilityProject.FromJson(ReadText(tr, root, DipsKey));
        }

        public static void SaveDips(Database db, Transaction tr, UtilityProject project)
        {
            WriteText(tr, RootDictionary(tr, db, true), DipsKey, project.ToJson());
        }

        // -------------------------------------------------------------- easements

        public static IList<FieldCodes.Easements.EasementRecord> LoadEasements(Database db, Transaction tr)
        {
            var records = new List<FieldCodes.Easements.EasementRecord>();
            var dict = SubDictionary(tr, db, EasementsKey, false);
            if (dict == null) return records;

            foreach (DBDictionaryEntry entry in dict)
            {
                var json = ReadText(tr, dict, entry.Key);
                if (string.IsNullOrWhiteSpace(json)) continue;
                try { records.Add(FieldCodes.Easements.EasementRecord.FromJson(json)); }
                catch (Newtonsoft.Json.JsonException) { }
            }
            return records;
        }

        public static void SaveEasement(Database db, Transaction tr, FieldCodes.Easements.EasementRecord record)
        {
            WriteText(tr, SubDictionary(tr, db, EasementsKey, true), record.Id, record.ToJson());
        }

        // ---------------------------------------------------------------- exhibits

        private const string ExhibitsKey = "EXHIBITS";

        public static IList<FieldCodes.Exhibits.ExhibitRecord> LoadExhibits(Database db, Transaction tr)
        {
            var records = new List<FieldCodes.Exhibits.ExhibitRecord>();
            var dict = SubDictionary(tr, db, ExhibitsKey, false);
            if (dict == null) return records;
            foreach (DBDictionaryEntry entry in dict)
            {
                try { records.Add(FieldCodes.Exhibits.ExhibitRecord.FromJson(ReadText(tr, dict, entry.Key))); }
                catch (Newtonsoft.Json.JsonException) { }
            }
            return records;
        }

        public static void SaveExhibit(Database db, Transaction tr, FieldCodes.Exhibits.ExhibitRecord record)
        {
            WriteText(tr, SubDictionary(tr, db, ExhibitsKey, true), record.Id, record.ToJson());
        }

        public static void DeleteExhibit(Database db, Transaction tr, string id)
        {
            var dict = SubDictionary(tr, db, ExhibitsKey, false);
            if (dict == null || !dict.Contains(id)) return;
            var obj = tr.GetObject(dict.GetAt(id), OpenMode.ForWrite);
            dict.UpgradeOpen();
            dict.Remove(id);
            obj.Erase();
        }

        public static void DeleteEasement(Database db, Transaction tr, string id)
        {
            var dict = SubDictionary(tr, db, EasementsKey, true);
            if (!dict.Contains(id)) return;
            var obj = tr.GetObject(dict.GetAt(id), OpenMode.ForWrite);
            dict.Remove(id);
            obj.Erase();
        }

        // ----------------------------------------------------------------- profile

        /// <summary>The drafting profile this drawing selected, or null. Read in its
        /// own open/close transaction so it is safe inside a running command.</summary>
        public static string ReadProfileName(Database db)
        {
            if (db == null) return null;
            try
            {
                using (var tr = db.TransactionManager.StartOpenCloseTransaction())
                {
                    var name = ReadText(tr, RootDictionary(tr, db, false), ProfileKey);
                    tr.Commit();
                    return string.IsNullOrWhiteSpace(name) ? null : name;
                }
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                return null;
            }
        }

        public static void WriteProfileName(Database db, Transaction tr, string name)
        {
            WriteText(tr, RootDictionary(tr, db, true), ProfileKey, name ?? string.Empty);
        }
    }
}
