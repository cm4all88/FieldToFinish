using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using FieldCodes.Settings;
using FieldCodes.Standards;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace FieldCodes.Cad
{
    /// <summary>
    /// Layer standards against a real drawing: what its layers look like, applying a standard to
    /// a layer FTF creates or (after a preview) to one FTF already uses, loading a standard's
    /// linetype from acad.lin, and the names Check Standards compares the settings with. Layer
    /// properties only: nothing here touches an entity.
    ///
    /// UNTESTED against a drawing: the decisions are in FieldCodes.Settings.LayerStandards and
    /// FieldCodes.Standards.StandardsCheck, which are.
    /// </summary>
    internal static class LayerStandardsCad
    {
        /// <summary>The linetype files tried, in order, for a linetype the drawing does not have.</summary>
        private static readonly string[] LinFiles = { "acad.lin", "acadiso.lin" };

        // ------------------------------------------------------------ reading

        /// <summary>Every layer in the drawing, as the standards compare them.</summary>
        public static List<LayerProperties> ReadLayers(Database db, Transaction tr)
        {
            var layers = new List<LayerProperties>();
            var table = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            foreach (ObjectId id in table)
            {
                var record = tr.GetObject(id, OpenMode.ForRead) as LayerTableRecord;
                if (record == null || record.IsErased) continue;
                layers.Add(Read(record, tr));
            }
            return layers;
        }

        public static LayerProperties Read(LayerTableRecord record, Transaction tr)
        {
            var props = new LayerProperties
            {
                Name = record.Name,
                Color = ColorOf(record.Color),
                LineWeight = (int)record.LineWeight,
                Plot = record.IsPlottable,
                Description = record.Description ?? string.Empty,
                Transparency = PercentOf(record.Transparency),
                Linetype = string.Empty
            };
            if (!record.LinetypeObjectId.IsNull)
            {
                var linetype = tr.GetObject(record.LinetypeObjectId, OpenMode.ForRead) as LinetypeTableRecord;
                if (linetype != null) props.Linetype = linetype.Name;
            }
            return props;
        }

        private static LayerColor ColorOf(Autodesk.AutoCAD.Colors.Color color)
        {
            if (color == null) return null;
            if (color.ColorMethod == Autodesk.AutoCAD.Colors.ColorMethod.ByAci) return LayerColor.FromAci(color.ColorIndex);
            if (color.ColorMethod == Autodesk.AutoCAD.Colors.ColorMethod.ByColor) return LayerColor.FromRgb(color.Red, color.Green, color.Blue);
            return null;
        }

        /// <summary>Transparency as the Layer Properties Manager shows it, 0 to 90 percent.</summary>
        private static int PercentOf(Autodesk.AutoCAD.Colors.Transparency transparency)
        {
            if (!transparency.IsByAlpha) return 0;
            return (int)Math.Round(100.0 - transparency.Alpha * 100.0 / 255.0);
        }

        private static Autodesk.AutoCAD.Colors.Transparency TransparencyFor(int percent)
        {
            var clamped = Math.Max(0, Math.Min(90, percent));
            return new Autodesk.AutoCAD.Colors.Transparency((byte)Math.Round(255.0 * (100 - clamped) / 100.0));
        }

        private static Autodesk.AutoCAD.Colors.Color ColorFor(LayerColor colour)
        {
            return colour.Aci.HasValue
                ? Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, colour.Aci.Value)
                : Autodesk.AutoCAD.Colors.Color.FromRgb(colour.Red, colour.Green, colour.Blue);
        }

        // ------------------------------------------------------------ linetypes

        private static IList<string> _acadLin;
        private static bool _acadLinRead;

        /// <summary>The linetypes acad.lin defines, read once; null when the file cannot be found or read.</summary>
        public static IList<string> AcadLinLinetypes(Database db)
        {
            if (_acadLinRead) return _acadLin;
            _acadLinRead = true;
            try
            {
                var path = HostApplicationServices.Current.FindFile("acad.lin", db ?? HostApplicationServices.WorkingDatabase, FindFileHint.Default);
                if (!string.IsNullOrEmpty(path) && File.Exists(path)) _acadLin = StandardsCheck.LinetypesIn(File.ReadAllLines(path));
            }
            catch (Autodesk.AutoCAD.Runtime.Exception) { _acadLin = null; }
            catch (IOException) { _acadLin = null; }
            catch (UnauthorizedAccessException) { _acadLin = null; }
            catch (ArgumentException) { _acadLin = null; }
            return _acadLin;
        }

        /// <summary>
        /// The linetype's id, loaded from acad.lin (then acadiso.lin) when the drawing does not
        /// have it. Null when it cannot be had; <paramref name="problem"/> says why. Never another
        /// linetype in its place.
        /// </summary>
        public static ObjectId EnsureLinetype(Database db, Transaction tr, string name, out string problem)
        {
            problem = null;
            var wanted = (name ?? string.Empty).Trim();
            if (wanted.Length == 0) { problem = "no linetype named"; return ObjectId.Null; }

            var table = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
            if (table.Has(wanted)) return table[wanted];

            foreach (var file in LinFiles)
            {
                try { db.LoadLineTypeFile(wanted, file); }
                catch (Autodesk.AutoCAD.Runtime.Exception) { }
                table = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
                if (table.Has(wanted)) return table[wanted];
            }
            problem = "linetype " + wanted + " is not in this drawing, acad.lin or acadiso.lin";
            return ObjectId.Null;
        }

        // ------------------------------------------------------------ applying

        /// <summary>
        /// Gives a layer FTF is creating its standard's properties (the record is not in the table
        /// yet). A property that cannot be set -- a linetype nobody can load -- is left at the
        /// layer's default and said on the command line.
        /// </summary>
        public static void ApplyToNew(Database db, Transaction tr, LayerTableRecord record, ResolvedLayerStandard standard)
        {
            if (record == null || standard == null || standard.IsEmpty) return;
            foreach (var problem in Apply(db, tr, record, standard.Values, LayerStandards.Properties).Values)
                Say("FTF: layer " + record.Name + " created without its standard " + problem +
                    ". Correct the standard or load what it names, then run FTFLAYERUPDATE.");
        }

        /// <summary>Sets the listed properties the standard sets; returns, by property, what could not be set.</summary>
        public static IDictionary<LayerProperty, string> Apply(Database db, Transaction tr, LayerTableRecord record, LayerStandard values,
                                                               IEnumerable<LayerProperty> properties)
        {
            var problems = new Dictionary<LayerProperty, string>();
            string why;
            foreach (var p in properties.Distinct())
            {
                if (!values.Sets(p)) continue;
                try
                {
                    switch (p)
                    {
                        case LayerProperty.Color:
                            LayerColor colour;
                            if (LayerStandards.TryParseColor(values.Color, out colour, out why)) record.Color = ColorFor(colour);
                            else problems[p] = "colour (" + why + ")";
                            break;
                        case LayerProperty.Linetype:
                            var id = EnsureLinetype(db, tr, values.Linetype, out why);
                            if (!id.IsNull) record.LinetypeObjectId = id;
                            else problems[p] = "linetype (" + why + ")";
                            break;
                        case LayerProperty.LineWeight:
                            int weight;
                            if (LayerStandards.TryParseLineWeight(values.LineWeight, out weight, out why) && Enum.IsDefined(typeof(LineWeight), weight))
                                record.LineWeight = (LineWeight)weight;
                            else problems[p] = "lineweight (" + (why ?? values.LineWeight) + ")";
                            break;
                        case LayerProperty.Plot:
                            record.IsPlottable = values.Plot.Value;
                            break;
                        case LayerProperty.Transparency:
                            record.Transparency = TransparencyFor(values.Transparency.Value);
                            break;
                        default:
                            record.Description = values.Description.Trim();
                            break;
                    }
                }
                catch (Autodesk.AutoCAD.Runtime.Exception ex)
                {
                    problems[p] = LayerStandards.PropertyName(p).ToLowerInvariant() + " (" + ex.Message + ")";
                }
            }
            return problems;
        }

        /// <summary>The result of applying an update plan.</summary>
        public sealed class UpdateResult
        {
            public UpdateResult() { Applied = new List<LayerPropertyChange>(); Failed = new List<string>(); }
            public IList<LayerPropertyChange> Applied { get; private set; }
            public IList<string> Failed { get; private set; }
        }

        /// <summary>
        /// Applies the chosen changes of a preview, layer by layer, and nothing else: no entity is
        /// moved, restyled or erased, no layer is renamed, and a layer not in the changes is not
        /// opened for write.
        /// </summary>
        public static UpdateResult ApplyChanges(Database db, Transaction tr, IEnumerable<LayerPropertyChange> changes, LayerStandardsSettings standards)
        {
            var result = new UpdateResult();
            var table = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            foreach (var layer in (changes ?? Enumerable.Empty<LayerPropertyChange>()).GroupBy(c => c.Layer, StringComparer.OrdinalIgnoreCase))
            {
                if (!table.Has(layer.Key)) { result.Failed.Add(layer.Key + ": no longer in the drawing"); continue; }
                var record = (LayerTableRecord)tr.GetObject(table[layer.Key], OpenMode.ForWrite);
                var values = standards.For(layer.Key).Values;
                var problems = Apply(db, tr, record, values, layer.Select(c => c.Property));
                foreach (var change in layer)
                {
                    string problem;
                    if (problems.TryGetValue(change.Property, out problem)) result.Failed.Add(layer.Key + ": " + problem + " not changed");
                    else result.Applied.Add(change);
                }
            }
            return result;
        }

        // ------------------------------------------------------------ which layers

        /// <summary>The layers FTF uses that this drawing has, after the project mapping -- the only
        /// layers FTFLAYERUPDATE may change.</summary>
        public static LayerUpdatePlan Plan(RulesConfig rules, FtfSettings settings, IList<LayerProperties> drawingLayers,
                                           LayerStandardsSettings standards, IEnumerable<LayerMapping> mappings)
        {
            var names = drawingLayers.Select(l => l.Name).ToList();
            var uses = LayerCatalog.Build(rules, settings, names);
            var used = LayerCatalog.EffectiveLayers(uses, mappings);
            return LayerStandards.PlanUpdate(used, drawingLayers, standards != null ? standards.Layers : null);
        }

        // ------------------------------------------------------------ names for the check

        /// <summary>Everything Check Standards compares the settings with.</summary>
        public static DrawingNames ReadNames(Database db, Transaction tr)
        {
            var names = new DrawingNames
            {
                Layers = SymbolNames(tr, db.LayerTableId),
                Linetypes = SymbolNames(tr, db.LinetypeTableId),
                TextStyles = SymbolNames(tr, db.TextStyleTableId),
                DimensionStyles = SymbolNames(tr, db.DimStyleTableId),
                MultileaderStyles = DictionaryNames(tr, db.MLeaderStyleDictionaryId),
                TableStyles = DictionaryNames(tr, db.TableStyleDictionaryId),
                Blocks = BlockNames(db, tr),
                LoadableLinetypes = AcadLinLinetypes(db)
            };
            try
            {
                names.LineLabelStyles = CivilLabels.LineLabelStyleNames(db, tr);
                names.CurveLabelStyles = CivilLabels.CurveLabelStyleNames(db, tr);
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                // Without Civil 3D's styles those two kinds are not checked rather than all reported missing.
                names.LineLabelStyles = null;
                names.CurveLabelStyles = null;
            }
            return names;
        }

        private static IList<string> SymbolNames(Transaction tr, ObjectId tableId)
        {
            var names = new List<string>();
            if (tableId.IsNull) return names;
            var table = (SymbolTable)tr.GetObject(tableId, OpenMode.ForRead);
            foreach (ObjectId id in table)
            {
                var record = tr.GetObject(id, OpenMode.ForRead) as SymbolTableRecord;
                if (record != null && !record.IsErased && !string.IsNullOrEmpty(record.Name)) names.Add(record.Name);
            }
            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }

        private static IList<string> DictionaryNames(Transaction tr, ObjectId dictionaryId)
        {
            if (dictionaryId.IsNull) return null;
            var names = new List<string>();
            var dictionary = (DBDictionary)tr.GetObject(dictionaryId, OpenMode.ForRead);
            foreach (DBDictionaryEntry entry in dictionary) names.Add(entry.Key);
            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }

        private static IList<string> BlockNames(Database db, Transaction tr)
        {
            var names = new List<string>();
            var table = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            foreach (ObjectId id in table)
            {
                var record = tr.GetObject(id, OpenMode.ForRead) as BlockTableRecord;
                if (record == null || record.IsErased || record.IsLayout || record.IsAnonymous) continue;
                names.Add(record.Name);
            }
            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }

        // ------------------------------------------------------------ from a window

        /// <summary>Runs <paramref name="body"/> in its own locked, committed transaction on the drawing in front
        /// (Settings > Layers is a modeless window). False when no drawing is open.</summary>
        public static bool InActiveDrawing(Action<Database, Transaction> body)
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return false;
            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                body(doc.Database, tr);
                tr.Commit();
            }
            return true;
        }

        /// <summary>Creates one layer now, exactly as named, with its standard. Nothing else changes.</summary>
        public static Setup.FixOutcome CreateLayerNow(string name, LayerStandardsSettings standards)
        {
            string why;
            if (!LayerMap.IsValidName(name, out why)) return Setup.FixOutcome.Failed("\"" + name + "\": " + why + ".");
            var existed = false;
            try
            {
                var ran = InActiveDrawing((db, tr) =>
                {
                    var table = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                    existed = table.Has(name);
                    if (!existed) CadUtil.EnsureLayerExact(db, tr, name, null, standards);
                });
                if (!ran) return Setup.FixOutcome.Failed("No drawing is open.");
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex) { return Setup.FixOutcome.Failed("Could not create " + name + ": " + ex.Message); }
            return Setup.FixOutcome.Ok(existed ? name + " is already in the drawing." : "Created layer " + name + ".");
        }

        /// <summary>Loads one linetype now from acad.lin (or acadiso.lin).</summary>
        public static Setup.FixOutcome LoadLinetypeNow(string name)
        {
            string problem = null;
            try
            {
                var ran = InActiveDrawing((db, tr) => EnsureLinetype(db, tr, name, out problem));
                if (!ran) return Setup.FixOutcome.Failed("No drawing is open.");
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex) { return Setup.FixOutcome.Failed("Could not load " + name + ": " + ex.Message); }
            return problem == null ? Setup.FixOutcome.Ok("Loaded linetype " + name + ".") : Setup.FixOutcome.Failed(Capital(problem) + ".");
        }

        private static string Capital(string text)
        {
            return string.IsNullOrEmpty(text) ? text : char.ToUpperInvariant(text[0]) + text.Substring(1);
        }

        /// <summary>The report of an update, for the command line or a message box.</summary>
        public static string Describe(UpdateResult result)
        {
            var lines = new List<string>
            {
                string.Format("Updated {0} propert(ies) on {1} layer(s).", result.Applied.Count,
                              result.Applied.Select(c => c.Layer).Distinct(StringComparer.OrdinalIgnoreCase).Count())
            };
            foreach (var layer in result.Applied.GroupBy(c => c.Layer, StringComparer.OrdinalIgnoreCase).Take(40))
                lines.Add("  " + layer.Key + ": " + string.Join(", ", layer.Select(c => LayerStandards.PropertyName(c.Property) + " " + c.From + " -> " + c.To).ToArray()));
            foreach (var f in result.Failed) lines.Add("  Not changed: " + f);
            return string.Join("\n", lines.ToArray());
        }

        // ------------------------------------------------------------ messages

        /// <summary>A line on the command line of the drawing in front, when there is one.</summary>
        public static void Say(string message)
        {
            try
            {
                var doc = AcadApp.DocumentManager.MdiActiveDocument;
                if (doc != null) doc.Editor.WriteMessage("\n" + message);
            }
            catch (Autodesk.AutoCAD.Runtime.Exception) { }
        }
    }
}
