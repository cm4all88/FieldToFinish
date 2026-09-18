using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;

namespace FieldCodes.Cad
{
    /// <summary>
    /// Block-definition diagnostics.
    ///
    /// The rules file names the block to place for each species. When that name is not
    /// in the drawing the point is skipped rather than drawn with something else, so
    /// there has to be an easy way to see what the drawing actually contains.
    ///
    /// UNTESTED: never run against a drawing.
    /// </summary>
    public sealed class BlockCommands
    {
        /// <summary>
        /// Lists block definitions in the drawing, optionally filtered by a substring,
        /// and reports which of the blocks the rules file expects are present.
        /// </summary>
        [CommandMethod("FTFBLOCKS", CommandFlags.Modal)]
        public void FtfBlocks()
        {
            FtfSession.Run("FTFBLOCKS", (db, tr, ed) =>
            {
                var opts = new PromptStringOptions(
                    "\nFilter block names by (RETURN for tree-ish names, * for all): ")
                {
                    AllowSpaces = false,
                    DefaultValue = "*"
                };

                var res = ed.GetString(opts);
                var filter = res.Status == PromptStatus.OK ? (res.StringResult ?? string.Empty).Trim() : "*";

                var table = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);

                var names = new List<string>();
                foreach (ObjectId id in table)
                {
                    var btr = (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead);
                    if (btr.IsLayout || btr.IsAnonymous) continue;
                    names.Add(btr.Name);
                }
                names.Sort(StringComparer.OrdinalIgnoreCase);

                IEnumerable<string> shown;
                if (filter == "*" || filter.Length == 0)
                {
                    shown = names;
                }
                else
                {
                    shown = names.Where(n =>
                        n.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0);
                }

                var list = shown.ToList();
                ed.WriteMessage("\n{0} block definition(s){1}:",
                    list.Count, filter == "*" ? "" : " matching '" + filter + "'");
                foreach (var n in list) ed.WriteMessage("\n    {0}", n);

                // Now the part that matters: what the rules file is asking for.
                ReportExpected(db, tr, ed, names);
            });
        }

        /// <summary>
        /// Every block name the rules file can produce, and whether it exists. Names
        /// containing a placeholder are expanded through the species map.
        /// </summary>
        private static void ReportExpected(Database db, Transaction tr, Editor ed,
                                           IList<string> present)
        {
            RulesConfig cfg;
            try
            {
                cfg = FtfSession.Rules(db);
            }
            catch (ConfigException ex)
            {
                ed.WriteMessage("\n\nCould not read the rules file: {0}\n", ex.Message);
                return;
            }

            var lookup = new HashSet<string>(present, StringComparer.OrdinalIgnoreCase);
            var wanted = new List<string>();

            foreach (var rule in cfg.Codes)
            {
                if (string.IsNullOrEmpty(rule.Block)) continue;

                if (rule.Species != null && rule.Species.Count > 0)
                {
                    foreach (var species in rule.Species)
                    {
                        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            { "species", species.Value },
                            { "code", species.Key }
                        };
                        wanted.Add(FieldCodeParser.Expand(rule.Block, fields));
                    }
                }
                else
                {
                    wanted.Add(rule.Block);
                }

                // Modifiers can substitute a different block entirely.
                foreach (var mod in cfg.Modifiers.Where(m => !string.IsNullOrEmpty(m.Block)))
                {
                    if (rule.Species == null) continue;
                    foreach (var species in rule.Species)
                    {
                        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            { "species", species.Value },
                            { "code", species.Key }
                        };
                        wanted.Add(FieldCodeParser.Expand(mod.Block, fields));
                    }
                }
            }

            var distinct = wanted
                .Where(w => !string.IsNullOrEmpty(w) && w.IndexOf('{') < 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(w => w, StringComparer.OrdinalIgnoreCase)
                .ToList();

            ed.WriteMessage("\n\nBlocks the rules file expects:");
            var missing = 0;
            foreach (var name in distinct)
            {
                var have = lookup.Contains(name);
                if (!have) missing++;
                ed.WriteMessage("\n    [{0}] {1}", have ? "ok " : "  -", name);
            }

            if (missing > 0)
                ed.WriteMessage(
                    "\n\n{0} missing. Either insert blocks with these names, or point " +
                    "the rules file at the names this drawing uses ('block' on the code " +
                    "rule, and 'block' on any modifier that substitutes one).\n", missing);
            else
                ed.WriteMessage("\n\nAll expected blocks are present.\n");
        }
    }
}
