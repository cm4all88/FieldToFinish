using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using FieldCodes.Symbols;

namespace FieldCodes.Cad
{
    /// <summary>
    /// The office's approved symbol library: a drawing whose block definitions the Symbols and
    /// Rotations page offers beside the drawing's own. The library is read into a side database
    /// (never opened in the editor) to list and preview its blocks; a block is copied into the
    /// drawing only by <see cref="BringIn"/>, which the page calls when the user asks for it.
    ///
    /// UNTESTED against a drawing. Reads are cached per file and its last-write time.
    /// </summary>
    internal static class SymbolLibraryReader
    {
        private sealed class Cached
        {
            public DateTime Stamp;
            public IList<string> Names;
            public readonly Dictionary<string, BlockSketch> Sketches = new Dictionary<string, BlockSketch>(StringComparer.OrdinalIgnoreCase);
        }

        private static readonly Dictionary<string, Cached> Cache = new Dictionary<string, Cached>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The library's block names (no layouts, anonymous or xref blocks), sorted; empty
        /// with a reason when the file is missing or cannot be read.</summary>
        public static IList<string> BlockNames(string path, out string problem)
        {
            problem = null;
            if (string.IsNullOrWhiteSpace(path)) return new List<string>();
            var cached = Entry(path, out problem);
            return cached != null ? cached.Names : new List<string>();
        }

        /// <summary>A library block's geometry for the preview, without bringing it in; null when
        /// the library has no such block.</summary>
        public static BlockSketch Sketch(string path, string blockName)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(blockName)) return null;
            string problem;
            var cached = Entry(path, out problem);
            if (cached == null) return null;
            BlockSketch sketch;
            if (cached.Sketches.TryGetValue(blockName.Trim(), out sketch)) return sketch;
            try
            {
                using (var source = new Database(false, true))
                {
                    source.ReadDwgFile(path, FileShare.Read, true, string.Empty);
                    sketch = BlockSketchReader.Read(source, blockName.Trim());
                }
            }
            catch (Autodesk.AutoCAD.Runtime.Exception) { sketch = null; }
            catch (IOException) { sketch = null; }
            catch (UnauthorizedAccessException) { sketch = null; }
            if (sketch != null) cached.Sketches[blockName.Trim()] = sketch;
            return sketch;
        }

        /// <summary>
        /// Copies one block definition from the library into <paramref name="db"/>. A block the
        /// drawing already has is left as the drawing has it (nothing is redefined). The caller
        /// locks the document. Returns what happened, for the page to show.
        /// </summary>
        public static bool BringIn(Database db, string path, string blockName, out string message)
        {
            message = null;
            if (db == null || string.IsNullOrWhiteSpace(blockName)) { message = "No block chosen."; return false; }
            var name = blockName.Trim();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) { message = "The symbol library " + path + " was not found."; return false; }

            try
            {
                using (var tr = db.TransactionManager.StartOpenCloseTransaction())
                {
                    var table = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    var have = table.Has(name);
                    tr.Commit();
                    if (have)
                    {
                        message = "\"" + name + "\" is already in this drawing; its definition was left as it is.";
                        return true;
                    }
                }

                using (var source = new Database(false, true))
                {
                    source.ReadDwgFile(path, FileShare.Read, true, string.Empty);
                    ObjectId sourceId;
                    using (var st = source.TransactionManager.StartOpenCloseTransaction())
                    {
                        var blocks = (BlockTable)st.GetObject(source.BlockTableId, OpenMode.ForRead);
                        sourceId = blocks.Has(name) ? blocks[name] : ObjectId.Null;
                        st.Commit();
                    }
                    if (sourceId.IsNull)
                    {
                        message = "The symbol library has no block \"" + name + "\".";
                        return false;
                    }
                    source.WblockCloneObjects(new ObjectIdCollection { sourceId }, db.BlockTableId, new IdMapping(),
                                              DuplicateRecordCloning.Ignore, false);
                }
                message = "\"" + name + "\" was brought into this drawing from " + Path.GetFileName(path) + ".";
                return true;
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex) { message = "\"" + name + "\" could not be brought in: " + ex.Message; }
            catch (IOException ex) { message = "\"" + name + "\" could not be brought in: " + ex.Message; }
            catch (UnauthorizedAccessException ex) { message = "\"" + name + "\" could not be brought in: " + ex.Message; }
            return false;
        }

        private static Cached Entry(string path, out string problem)
        {
            problem = null;
            if (!File.Exists(path)) { problem = "The symbol library " + path + " was not found."; return null; }
            DateTime stamp;
            try { stamp = File.GetLastWriteTimeUtc(path); }
            catch (IOException ex) { problem = ex.Message; return null; }
            catch (UnauthorizedAccessException ex) { problem = ex.Message; return null; }

            Cached cached;
            if (Cache.TryGetValue(path, out cached) && cached.Stamp == stamp) return cached;

            try
            {
                var names = new List<string>();
                using (var source = new Database(false, true))
                {
                    source.ReadDwgFile(path, FileShare.Read, true, string.Empty);
                    using (var st = source.TransactionManager.StartOpenCloseTransaction())
                    {
                        var table = (BlockTable)st.GetObject(source.BlockTableId, OpenMode.ForRead);
                        foreach (ObjectId id in table)
                        {
                            var record = st.GetObject(id, OpenMode.ForRead) as BlockTableRecord;
                            if (record == null || record.IsLayout || record.IsAnonymous || record.IsFromExternalReference || record.IsDependent) continue;
                            names.Add(record.Name);
                        }
                        st.Commit();
                    }
                }
                cached = new Cached { Stamp = stamp, Names = names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList() };
                Cache[path] = cached;
                return cached;
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex) { problem = "The symbol library could not be read: " + ex.Message; }
            catch (IOException ex) { problem = "The symbol library could not be read: " + ex.Message; }
            catch (UnauthorizedAccessException ex) { problem = "The symbol library could not be read: " + ex.Message; }
            return null;
        }
    }
}
