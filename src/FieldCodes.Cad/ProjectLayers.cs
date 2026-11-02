using System;
using System.Runtime.CompilerServices;
using Autodesk.AutoCAD.DatabaseServices;
using FieldCodes.Settings;

namespace FieldCodes.Cad
{
    /// <summary>
    /// The project's layer mapping (Settings > Layers), applied to every layer FTF creates or
    /// draws on. <see cref="CadUtil.EnsureLayer(Database, Transaction, string)"/> asks here
    /// first, so labels, driplines, tags, the schedule, spots, contours, line labels, the
    /// orientation QC and the production tools all honour one setting -- before this, only the
    /// dip and record tools read it.
    ///
    /// A command run through <see cref="FtfSession.Run"/> reads the mapping once for its
    /// transaction; anything else reads it from the drawing's settings, cached until the
    /// settings are saved again.
    /// </summary>
    internal static class ProjectLayers
    {
        private sealed class Entry
        {
            public int Version;
            public LayerMap Map;
        }

        [ThreadStatic] private static LayerMap _ambient;
        [ThreadStatic] private static Database _ambientDb;
        private static readonly ConditionalWeakTable<Database, Entry> Cache = new ConditionalWeakTable<Database, Entry>();

        /// <summary>The layer FTF should use in place of <paramref name="layer"/>.</summary>
        public static string Apply(Database db, string layer)
        {
            if (string.IsNullOrWhiteSpace(layer) || db == null) return layer;
            var map = For(db);
            return map == null ? layer : map.Apply(layer);
        }

        /// <summary>Holds the mapping for the length of one command's transaction.</summary>
        public static IDisposable Use(Database db)
        {
            var previous = _ambient;
            var previousDb = _ambientDb;
            _ambient = Load(db);
            _ambientDb = db;
            return new Restore(previous, previousDb);
        }

        private static LayerMap For(Database db)
        {
            if (_ambient != null && ReferenceEquals(_ambientDb, db)) return _ambient;
            Entry entry;
            if (Cache.TryGetValue(db, out entry) && entry.Version == FtfSession.SettingsVersion) return entry.Map;
            var map = Load(db);
            Cache.Remove(db);
            Cache.Add(db, new Entry { Version = FtfSession.SettingsVersion, Map = map });
            return map;
        }

        private static LayerMap Load(Database db)
        {
            try
            {
                var rules = FtfSession.Rules(db);
                var settings = FtfSession.SettingsFor(db, rules);
                return new LayerMap(settings.General.LayerMappings);
            }
            catch (ConfigException) { return null; }
            catch (System.IO.IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        // ------------------------------------------------------------ layer standards

        private sealed class StandardsEntry
        {
            public int Version;
            public LayerStandardsSettings Standards;
        }

        private static readonly ConditionalWeakTable<Database, StandardsEntry> StandardsCache = new ConditionalWeakTable<Database, StandardsEntry>();

        /// <summary>
        /// The layer standards in the drawing's settings (Settings > Layers), read the way the
        /// mapping is and cached until the settings are saved again. Null when the settings cannot
        /// be read; a layer is then created as before, from its family.
        /// </summary>
        public static LayerStandardsSettings Standards(Database db)
        {
            if (db == null) return null;
            StandardsEntry entry;
            if (StandardsCache.TryGetValue(db, out entry) && entry.Version == FtfSession.SettingsVersion) return entry.Standards;
            LayerStandardsSettings standards = null;
            try
            {
                var rules = FtfSession.Rules(db);
                var settings = FtfSession.SettingsFor(db, rules);
                standards = settings.LayerStandards;
            }
            catch (ConfigException) { standards = null; }
            catch (System.IO.IOException) { standards = null; }
            catch (UnauthorizedAccessException) { standards = null; }
            StandardsCache.Remove(db);
            StandardsCache.Add(db, new StandardsEntry { Version = FtfSession.SettingsVersion, Standards = standards });
            return standards;
        }

        private sealed class Restore : IDisposable
        {
            private readonly LayerMap _map;
            private readonly Database _db;
            private bool _done;
            public Restore(LayerMap map, Database db) { _map = map; _db = db; }
            public void Dispose()
            {
                if (_done) return;
                _done = true;
                _ambient = _map;
                _ambientDb = _db;
            }
        }
    }
}
